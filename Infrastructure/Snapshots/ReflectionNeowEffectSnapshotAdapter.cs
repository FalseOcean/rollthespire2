using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal sealed record ReflectionSnapshotProfileRules(
    bool GeneratedCursePoolRequiresModifierFlag,
    bool OtherCharacterPoolsUseModelIdOrder);

/// <summary>
/// Narrow reflection boundary over the live STS2 model catalog/profile. It is intentionally
/// isolated from the pure core so the same DLL can run against both accepted game ABIs.
/// All returned values are copied scalar DTOs; no game object escapes this method.
/// </summary>
internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private static readonly string[] AuditedVanillaCharacters =
    {
        "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER"
    };

    public static RuntimeEffectSnapshotCaptureResult Capture(
        IRuntimeProfile profile,
        string rawSeed,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        RuntimeProfileId expectedProfile,
        string capturedGameVersion,
        ReflectionSnapshotProfileRules rules,
        object? explicitUnlockState = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (profile.ProfileId != expectedProfile)
        {
            return RuntimeEffectSnapshotCaptureResult.Missing(
                expectedProfile,
                "ProfileSpecificSnapshotAdapterMismatch",
                PredictionWarningCode.EffectSnapshotIncomplete);
        }
        // No scheduling is involved in this path: the unbounded observation
        // completes on the current main thread, preserving the direct adapter's
        // throwing boundary. NeowEffectRuntimeSnapshotCapture owns any fail-soft policy.
        var observed = ObserveEffectCore(profile, rawSeed, character, ascension, playersCount,
            playerSlotIndex, capturedGameVersion, explicitUnlockState, new NativeObservationSchedule(), rules)
            .GetAwaiter().GetResult() with { FailSoft = false };
        return DeriveEffect(profile, rawSeed, character, ascension, playersCount, playerSlotIndex,
            capturedGameVersion, observed);
    }

    internal sealed record PoolCapture(object? Items, bool Exact, bool IdentityExact, string Source)
    {
        public IReadOnlyList<NeowEffectCardSnapshot>? Cards => Items as IReadOnlyList<NeowEffectCardSnapshot>;
        public IReadOnlyList<NeowEffectPotionSnapshot>? Potions => Items as IReadOnlyList<NeowEffectPotionSnapshot>;
        public IReadOnlyList<NeowEffectRelicSnapshot>? Relics => Items as IReadOnlyList<NeowEffectRelicSnapshot>;
    }

    private static object? TryCaptureCurrentUnlockState(Assembly assembly, out string source)
    {
        Type? saveType = FindType(assembly, "MegaCrit.Sts2.Core.Saves.SaveManager", "SaveManager");
        object? save = saveType?.GetProperty("Instance", PublicStatic)?.GetValue(null);
        if (save is null)
        {
            source = "SaveManager.Instance missing";
            return null;
        }
        if (TryReadBool(save, "IsProfileInitialized") != true)
        {
            source = "Save profile not initialized";
            return null;
        }
        MethodInfo? method = save.GetType().GetMethod("GenerateUnlockStateFromProgress", PublicInstance, null, Type.EmptyTypes, null);
        if (method is null)
        {
            source = "GenerateUnlockStateFromProgress missing";
            return null;
        }
        try
        {
            object? result = method.Invoke(save, null);
            source = result is null ? "GenerateUnlockStateFromProgress returned null" : "SaveManager.GenerateUnlockStateFromProgress";
            return result;
        }
        catch (TargetInvocationException ex)
        {
            RecordCaptureFailure(ex, "SaveManager.GenerateUnlockStateFromProgress");
            source = $"GenerateUnlockStateFromProgress failed:{ex.InnerException?.GetType().Name ?? ex.GetType().Name}";
            return null;
        }
    }

    private static object? FindCharacterModel(
        Assembly assembly,
        object unlockState,
        ModelKey characterKey,
        out bool selectedCharacterUnlocked)
    {
        foreach (object item in Enumerate(ReadProperty(unlockState, "Characters")))
        {
            if (TryModelKey(item, out ModelKey key) && key == characterKey)
            {
                selectedCharacterUnlocked = true;
                return item;
            }
        }

        // A locked model may still be present in ModelDb. It can be copied for diagnostic/
        // best-effort projection, but it must never be presented as the current profile's
        // exact unlocked character authority.
        Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
        foreach (object item in Enumerate(modelDb?.GetProperty("AllCharacters", PublicStatic)?.GetValue(null)))
        {
            if (TryModelKey(item, out ModelKey key) && key == characterKey)
            {
                selectedCharacterUnlocked = false;
                return item;
            }
        }

        selectedCharacterUnlocked = false;
        return null;
    }

    private static bool IsAuditedVanillaCharacterModel(object model)
    {
        if (!TryModelKey(model, out ModelKey key) ||
            !AuditedVanillaCharacters.Contains(key.Entry, StringComparer.Ordinal))
        {
            return false;
        }
        Type type = model.GetType();
        return string.Equals(RuntimeAssemblyName(type), "sts2", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(type.Namespace, "MegaCrit.Sts2.Core.Models.Characters", StringComparison.Ordinal);
    }

    private static object? ResolveModelDbPool(Assembly assembly, string methodName, string namespaceName, string className)
    {
        Type? poolType = FindType(assembly, $"{namespaceName}.{className}", className);
        Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
        if (poolType is null || modelDb is null)
        {
            return null;
        }
        foreach (MethodInfo method in modelDb.GetMethods(PublicStatic))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
                !method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 1 ||
                method.GetParameters().Length != 0)
            {
                continue;
            }
            try
            {
                return method.MakeGenericMethod(poolType).Invoke(null, null);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static PoolCapture StampCardCatalogProfile(PoolCapture capture, RuntimeProfileId profileId)
    {
        if (capture.Cards is null)
        {
            return capture;
        }
        return capture with
        {
            Items = capture.Cards.Select(card => card with { CatalogProfileId = profileId }).ToArray()
        };
    }

    private static IReadOnlyList<CharacterCardPoolSnapshot>? StampCardCatalogProfile(
        IReadOnlyList<CharacterCardPoolSnapshot>? pools,
        RuntimeProfileId profileId) => pools?.Select(pool => pool with
        {
            Cards = pool.Cards.Select(card => card with { CatalogProfileId = profileId }).ToArray()
        }).ToArray();

    private static IReadOnlyList<NeowEffectCardSnapshot>? StampCardCatalogProfile(
        IReadOnlyList<NeowEffectCardSnapshot>? cards,
        RuntimeProfileId profileId) => cards?.Select(card =>
            card with { CatalogProfileId = profileId }).ToArray();

    private static object ToTypedReadOnlyList(List<object> values)
    {
        if (values.All(value => value is NeowEffectCardSnapshot))
        {
            return values.Cast<NeowEffectCardSnapshot>().ToArray();
        }
        if (values.All(value => value is NeowEffectPotionSnapshot))
        {
            return values.Cast<NeowEffectPotionSnapshot>().ToArray();
        }
        if (values.All(value => value is NeowEffectRelicSnapshot))
        {
            return values.Cast<NeowEffectRelicSnapshot>().ToArray();
        }
        return values.ToArray();
    }

    private static NeowEffectCardSnapshot? ConvertCardModel(object model, int order, string poolId) =>
        ConvertCardModel(model, order, poolId, $"{poolId}:{order}");

    internal static NeowEffectCardSnapshot? ConvertCardModel(object model, int order, string poolId, string instanceId)
    {
        if (!TryModelKey(model, out ModelKey key) || key.Category != BaseGameModelKeys.Categories.Card)
        {
            return null;
        }
        string rarityName = ReadEnumName(model, "Rarity");
        string typeName = ReadEnumName(model, "Type");
        if (!TryParseCardRarity(rarityName, out EffectCardRarity rarity) ||
            !TryParseCardType(typeName, out EffectCardType type))
        {
            return null;
        }
        bool? isUpgradable = TryReadBool(model, "IsUpgradable", "CanUpgrade");
        bool? isRemovable = TryReadBool(model, "IsRemovable", "CanRemove");
        int? maxUpgrade = TryReadInt(model, "MaxUpgradeLevel");
        int currentUpgrade = TryReadInt(model, "CurrentUpgradeLevel", "UpgradeLevel") ?? 0;
        bool strike = HasTag(model, "Strike") || (rarity == EffectCardRarity.Basic && key.Entry.Contains("STRIKE", StringComparison.OrdinalIgnoreCase));
        bool defend = HasTag(model, "Defend") || (rarity == EffectCardRarity.Basic && key.Entry.Contains("DEFEND", StringComparison.OrdinalIgnoreCase));

        bool behaviorMetadataExact = isUpgradable.HasValue && isRemovable.HasValue && maxUpgrade.HasValue;
        bool canUpgrade = isUpgradable ?? false;
        bool canRemove = isRemovable ?? false;
        int max = maxUpgrade ?? 0;
        if (max < 0 || (canUpgrade && max == 0) || (!canUpgrade && max != 0))
        {
            behaviorMetadataExact = false;
            canUpgrade = false;
            max = 0;
        }
        return new NeowEffectCardSnapshot(
            instanceId,
            key,
            order,
            rarity,
            type,
            rarity == EffectCardRarity.Basic,
            strike,
            defend,
            canUpgrade && currentUpgrade < max,
            canRemove,
            currentUpgrade,
            max,
            UpgradeTargetKey: null,
            PoolId: poolId,
            SourceAssembly: RuntimeAssemblyName(model.GetType()),
            SourceModId: SourceModIdFor(model),
            CanBeGeneratedByModifiers: TryReadBool(model,
                "CanBeGeneratedByModifiers",
                "CanGenerateByModifiers",
                "IsModifierGenerationAllowed"),
            IsMultiplayerOnly: IsMultiplayerOnly(model),
            CanBeGeneratedInCombat: TryReadBool(model, "CanBeGeneratedInCombat"),
            IsUnlockedInCapturedPool: true,
            IsDiscovered: TryReadBool(model, "IsDiscovered", "Discovered"),
            EligibleForPostCombatRewardByPoolMembership:
                rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare,
            EligibilityAuthority: "runtime-unlocked-card-pool-membership",
            BehaviorMetadataExact: behaviorMetadataExact);
    }

    private static object? ConvertPotionModel(object model, int order, string poolId)
    {
        if (!TryModelKey(model, out ModelKey key) || key.Category != BaseGameModelKeys.Categories.Potion ||
            !TryParsePotionRarity(ReadEnumName(model, "Rarity"), out EffectPotionRarity rarity))
        {
            return null;
        }
        bool? allowed = TryReadBool(model, "AllowedOutOfCombat", "CanUseOutOfCombat", "IsAllowedOutOfCombat");
        // Neow potion generation is sourced from the captured character + shared potion
        // pools. AllowedOutOfCombat is optional descriptive metadata only and must not
        // exclude an otherwise authoritative runtime pool member.
        return new NeowEffectPotionSnapshot(
            key,
            order,
            rarity,
            allowed,
            RuntimeAssemblyName(model.GetType()),
            SourceModId: SourceModIdFor(model),
            IsMultiplayerOnly: IsMultiplayerOnly(model));
    }

    private static object? ConvertRelicModel(object model, int order, string poolId)
    {
        if (!TryModelKey(model, out ModelKey key) || key.Category != BaseGameModelKeys.Categories.Relic ||
            !TryParseRelicRarity(ReadEnumName(model, "Rarity"), out EffectRelicRarity rarity))
        {
            return null;
        }
        NestedRelicEffectKind kind = ClassifyNestedRelicEffect(model, key, out bool classificationExact);
        bool? isAllowedInShops = TryReadBool(model, "IsAllowedInShops");
        return new NeowEffectRelicSnapshot(
            key,
            order,
            rarity,
            kind,
            classificationExact,
            RuntimeAssemblyName(model.GetType()),
            SourceModId: SourceModIdFor(model),
            IsMultiplayerOnly: IsMultiplayerOnly(model),
            RarityCode: ReadEnumName(model, "Rarity"),
            IsAllowedInShops: isAllowedInShops ?? false,
            ShopEligibilityExact: isAllowedInShops.HasValue);
    }

    private static PoolCapture FilterForPlayers(PoolCapture capture, int playersCount)
    {
        if (playersCount > 1)
        {
            return capture;
        }
        if (capture.Cards is not null)
        {
            return capture with { Items = capture.Cards.Where(item => !item.IsMultiplayerOnly).ToArray() };
        }
        if (capture.Potions is not null)
        {
            return capture with { Items = capture.Potions.Where(item => !item.IsMultiplayerOnly).ToArray() };
        }
        if (capture.Relics is not null)
        {
            return capture with { Items = capture.Relics.Where(item => !item.IsMultiplayerOnly).ToArray() };
        }
        return capture;
    }

    private static IReadOnlyList<NeowEffectPotionSnapshot>? MergePotions(PoolCapture character, PoolCapture shared)
    {
        if (character.Potions is null && shared.Potions is null)
        {
            return null;
        }
        var seen = new HashSet<ModelKey>();
        var output = new List<NeowEffectPotionSnapshot>();
        foreach (NeowEffectPotionSnapshot potion in (character.Potions ?? Array.Empty<NeowEffectPotionSnapshot>())
                     .Concat(shared.Potions ?? Array.Empty<NeowEffectPotionSnapshot>()))
        {
            if (seen.Add(potion.PotionKey))
            {
                output.Add(potion with { PoolOrder = output.Count });
            }
        }
        return output;
    }

    private static IReadOnlyList<NeowEffectRelicSnapshot>? BuildOrderedRelicBag(
        IRuntimeProfile profile,
        string canonicalSeed,
        PoolCapture shared,
        PoolCapture character,
        out bool exact,
        out bool nestedHooksExact)
    {
        exact = shared.Exact && character.Exact;
        if (shared.Relics is null || character.Relics is null)
        {
            nestedHooksExact = false;
            exact = false;
            return null;
        }

        // UpFront initialization creates the shared grab bag first, then the player bag,
        // using the same stream. Even though Capsule reads the player bag, every shared-bag
        // shuffle must be consumed first or the player-bag order is wrong.
        ulong root = profile.ComputeRootSeed(canonicalSeed);
        var rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(root, "up_front"));
        if (!ConsumeRelicBagShuffle(shared.Relics, rng, filterPlayerGrabBagRarities: false))
        {
            exact = false;
        }

        var seen = new HashSet<ModelKey>();
        var playerRelics = new List<NeowEffectRelicSnapshot>();
        foreach (NeowEffectRelicSnapshot relic in shared.Relics.Concat(character.Relics))
        {
            if (relic.Rarity is not (EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare or EffectRelicRarity.Shop))
            {
                continue;
            }
            if (seen.Add(relic.RelicKey))
            {
                playerRelics.Add(relic);
            }
        }

        nestedHooksExact = playerRelics.All(relic => relic.NestedClassificationExact);
        var output = new List<NeowEffectRelicSnapshot>();
        foreach (IGrouping<string, NeowEffectRelicSnapshot> bucket in GroupRelicsInSourceRarityOrder(playerRelics))
        {
            List<NeowEffectRelicSnapshot> shuffled = bucket.ToList();
            rng.UnstableShuffle(shuffled);
            foreach (NeowEffectRelicSnapshot relic in shuffled)
            {
                output.Add(relic with { BagOrder = output.Count });
            }
        }
        return output;
    }

    private static bool ConsumeRelicBagShuffle(
        IReadOnlyList<NeowEffectRelicSnapshot> relics,
        Xoshiro256StarStar rng,
        bool filterPlayerGrabBagRarities)
    {
        bool exact = true;
        IEnumerable<NeowEffectRelicSnapshot> source = filterPlayerGrabBagRarities
            ? relics.Where(relic => relic.Rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare or EffectRelicRarity.Shop)
            : relics;

        foreach (IGrouping<string, NeowEffectRelicSnapshot> bucket in GroupRelicsInSourceRarityOrder(source))
        {
            if (string.IsNullOrWhiteSpace(bucket.Key))
            {
                exact = false;
            }
            List<NeowEffectRelicSnapshot> shuffled = bucket.ToList();
            rng.UnstableShuffle(shuffled);
        }
        return exact;
    }

    private static IEnumerable<IGrouping<string, NeowEffectRelicSnapshot>> GroupRelicsInSourceRarityOrder(
        IEnumerable<NeowEffectRelicSnapshot> relics)
    {
        // Enumerable.GroupBy preserves first-key occurrence order and item source order,
        // matching RelicGrabBag.Populate's dictionary + rarityOrder construction.
        return relics.GroupBy(
            relic => string.IsNullOrWhiteSpace(relic.RarityCode)
                ? relic.Rarity.ToString()
                : relic.RarityCode,
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<ModelKey>? BuildGeneratedCursePool(
        PoolCapture curseCards,
        ReflectionSnapshotProfileRules rules,
        out bool exact)
    {
        exact = curseCards.Exact;
        if (curseCards.Cards is null)
        {
            exact = false;
            return null;
        }
        var output = new List<ModelKey>();
        foreach (NeowEffectCardSnapshot card in curseCards.Cards.OrderBy(card => card.CardKey.Category, StringComparer.Ordinal)
                     .ThenBy(card => card.CardKey.Entry, StringComparer.Ordinal))
        {
            if (!rules.GeneratedCursePoolRequiresModifierFlag)
            {
                output.Add(card.CardKey);
                continue;
            }
            if (!card.CanBeGeneratedByModifiers.HasValue)
            {
                exact = false;
                continue;
            }
            if (card.CanBeGeneratedByModifiers.Value)
            {
                output.Add(card.CardKey);
            }
        }
        exact &= output.Count > 0;
        return output;
    }

    private static bool? CaptureAllCharacterCardPoolsUnlocked(
        Assembly assembly,
        object unlockState,
        out bool exact)
    {
        exact = false;
        object? unlockedPoolsValue = ReadProperty(unlockState, "CharacterCardPools");
        Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
        object? allCharactersValue = modelDb?.GetProperty("AllCharacters", PublicStatic)?.GetValue(null);
        if (unlockedPoolsValue is null || allCharactersValue is null)
        {
            return null;
        }

        ModelKey[] unlockedPools = Enumerate(unlockedPoolsValue)
            .Select(pool => TryModelKey(pool, out ModelKey key) ? key : default)
            .Where(key => key.IsValid)
            .Distinct()
            .ToArray();
        ModelKey[] allCharacters = Enumerate(allCharactersValue)
            .Select(character => TryModelKey(character, out ModelKey key) ? key : default)
            .Where(key => key.IsValid)
            .Distinct()
            .ToArray();
        if (unlockedPools.Length == 0 || allCharacters.Length == 0)
        {
            return null;
        }

        exact = true;
        return unlockedPools.Length == allCharacters.Length;
    }

    private static bool ModelDbContainsAll(
        Assembly assembly,
        string propertyName,
        IEnumerable<ModelKey> requiredKeys)
    {
        Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
        object? value = modelDb?.GetProperty(propertyName, PublicStatic)?.GetValue(null);
        if (value is null) return false;
        var present = Enumerate(value)
            .Select(model => TryModelKey(model, out ModelKey key) ? key : default)
            .Where(key => key.IsValid)
            .ToHashSet(ModelKeyComparer.Instance);
        ModelKey[] required = requiredKeys
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        return required.Length > 0 && required.All(present.Contains);
    }

    private static bool IsAuditedBaseGameCardSnapshot(NeowEffectCardSnapshot card) =>
        string.Equals(card.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(card.SourceModId);

    private static bool IsAuditedBaseGamePotionSnapshot(NeowEffectPotionSnapshot potion) =>
        string.Equals(potion.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(potion.SourceModId);

    private static IReadOnlyList<ModelKey>? BuildBonesEligibleRelics(
        RuntimeProfileId profileId,
        int playersCount,
        bool? allCharacterPoolsUnlocked,
        int? unlockedCommonCards,
        int? unlockedUncommonCards,
        out bool exact)
    {
        exact = profileId is RuntimeProfileId.Stable107 or RuntimeProfileId.Beta109 or RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111;
        if (!exact)
        {
            return null;
        }
        IEnumerable<ModelKey> values = NeowEffectCoverageRegistry.GetApplicableKeys(profileId)
            .Where(key => key != BaseGameModelKeys.Relics.NeowsBones);
        if (RuntimeProfilePolicies.IsModernCore(profileId))
        {
            bool? scrollBoxesAllowed = unlockedCommonCards.HasValue && unlockedUncommonCards.HasValue
                ? unlockedCommonCards.Value >= 4 && unlockedUncommonCards.Value >= 2 : null;
            exact &= allCharacterPoolsUnlocked.HasValue && scrollBoxesAllowed.HasValue;
            values = values.Where(key => Core.Neow.ModernNeowIdentityPredictor.IsAllowed(
                key, playersCount, allCharacterPoolsUnlocked, scrollBoxesAllowed) != false);
        }
        return values.ToArray();
    }

    private static ModelKey? UniqueDeckKey(IReadOnlyList<NeowEffectCardSnapshot>? deck, Func<NeowEffectCardSnapshot, bool> predicate)
    {
        ModelKey[] keys = (deck ?? Array.Empty<NeowEffectCardSnapshot>())
            .Where(predicate)
            .Select(card => card.CardKey)
            .Distinct()
            .ToArray();
        return keys.Length == 1 ? keys[0] : null;
    }

    private static NestedRelicEffectKind ClassifyNestedRelicEffect(object model, ModelKey key, out bool exact)
    {
        exact = string.Equals(RuntimeAssemblyName(model.GetType()), "sts2", StringComparison.OrdinalIgnoreCase);
        if (key == BaseGameModelKeys.OrdinaryRelics.Whetstone)
        {
            return NestedRelicEffectKind.Whetstone;
        }
        if (key == BaseGameModelKeys.OrdinaryRelics.WarPaint)
        {
            return NestedRelicEffectKind.WarPaint;
        }
        Type? baseRelicType = model.GetType();
        while (baseRelicType?.BaseType is not null && !string.Equals(baseRelicType.Name, "RelicModel", StringComparison.Ordinal))
        {
            baseRelicType = baseRelicType.BaseType;
        }
        if (baseRelicType is null || !string.Equals(baseRelicType.Name, "RelicModel", StringComparison.Ordinal))
        {
            exact = false;
            return NestedRelicEffectKind.UnknownHook;
        }
        bool overrides = RuntimeMethodsFor(model.GetType(), PublicInstance | BindingFlags.NonPublic)
            .Where(method => method.DeclaringType == model.GetType())
            .Any(method => method.Name is "AfterObtained" or "OnObtained" or "OnObtain" or "AfterObtain");
        return overrides ? NestedRelicEffectKind.UnknownHook : NestedRelicEffectKind.NoTrackedImmediateEffect;
    }

    private static IEnumerable? InvokeUnlockedCollection(
        object pool,
        object unlockState,
        string poolId,
        int playersCount,
        out bool usedAuditedMethod)
    {
        usedAuditedMethod = false;
        string exactMethodName = poolId.StartsWith("potion", StringComparison.Ordinal)
            ? "GetUnlockedPotions"
            : poolId.StartsWith("relic", StringComparison.Ordinal)
                ? "GetUnlockedRelics"
                : "GetUnlockedCards";

        MethodInfo[] candidates = RuntimeMethodsFor(pool.GetType(), PublicInstance)
            .Where(method =>
                string.Equals(method.Name, exactMethodName, StringComparison.Ordinal) ||
                string.Equals(method.Name, "GetUnlockedModels", StringComparison.Ordinal) ||
                string.Equals(method.Name, "GetUnlocked", StringComparison.Ordinal))
            .OrderBy(method => string.Equals(method.Name, exactMethodName, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(method => method.GetParameters().Length)
            .ToArray();

        foreach (MethodInfo method in candidates)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length is < 1 or > 2 ||
                !parameters[0].ParameterType.IsInstanceOfType(unlockState))
            {
                continue;
            }

            object?[] args = new object?[parameters.Length];
            args[0] = unlockState;
            bool exactInvocation = string.Equals(method.Name, exactMethodName, StringComparison.Ordinal);

            if (parameters.Length == 2)
            {
                if (!TryCreateMultiplayerConstraint(parameters[1], playersCount, out object? constraint))
                {
                    continue;
                }
                args[1] = constraint;
            }

            try
            {
                if (method.Invoke(pool, args) is IEnumerable enumerable)
                {
                    usedAuditedMethod = exactInvocation;
                    return enumerable;
                }
            }
            catch
            {
                // Try another explicitly compatible overload. Failure never falls back to
                // a raw backing collection and never upgrades the pool to Exact.
            }
        }

        return null;
    }

    private static bool TryCreateMultiplayerConstraint(
        ParameterInfo parameter,
        int playersCount,
        out object? value)
    {
        Type type = parameter.ParameterType;
        if (!type.IsEnum)
        {
            if (parameter.HasDefaultValue)
            {
                value = parameter.DefaultValue;
                return true;
            }
            value = null;
            return false;
        }

        string[] preferredNames = playersCount > 1
            ? new[] { "MultiplayerOnly", "MultiPlayerOnly", "Multiplayer" }
            : new[] { "SingleplayerOnly", "SinglePlayerOnly", "Singleplayer" };
        foreach (string name in preferredNames)
        {
            if (Enum.GetNames(type).Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = Enum.Parse(type, name, ignoreCase: true);
                return true;
            }
        }

        value = null;
        return false;
    }

    private static Type? FindType(Assembly assembly, string fullName, string simpleName) =>
        assembly.GetType(fullName, throwOnError: false, ignoreCase: false) ??
        SafeGetTypes(assembly).FirstOrDefault(type => string.Equals(type.Name, simpleName, StringComparison.Ordinal));

    private static void RecordCaptureFailure(Exception exception, string member) =>
        Diagnostics.RuntimeIssueLedger.Current.Record(exception, "RuntimeCaptureMemberFailed",
            nameof(ReflectionNeowEffectSnapshotAdapter), member, "issues.capture_failed",
            Diagnostics.RuntimeIssueSeverity.Warning);

    private static IReadOnlyList<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { RecordCaptureFailure(ex, "Assembly.GetTypes"); return ex.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
        catch (Exception ex) { RecordCaptureFailure(ex, "Assembly.GetTypes"); return Array.Empty<Type>(); }
    }

    private static readonly ConcurrentDictionary<Type, Lazy<string?>> RuntimeAssemblyNames = new();
    private static string? RuntimeAssemblyName(Type type) => RuntimeAssemblyNames.GetOrAdd(type,
        static key => new Lazy<string?>(() => key.Assembly.GetName().Name)).Value;
    private sealed record RuntimeInstanceMember(PropertyInfo? Property, FieldInfo? Field);
    private static readonly ConcurrentDictionary<(Type Type, string Name), RuntimeInstanceMember> RuntimeInstanceMembers = new();
    private static readonly ConcurrentDictionary<(Type Type, BindingFlags Flags), MethodInfo[]> RuntimeInstanceMethods = new();
    private static MethodInfo[] RuntimeMethodsFor(Type type, BindingFlags flags) =>
        RuntimeInstanceMethods.GetOrAdd((type, flags), key => key.Type.GetMethods(key.Flags));

    private static object? ReadProperty(object? instance, string name)
    {
        if (instance is null) return null;
        try
        {
            var member = RuntimeInstanceMembers.GetOrAdd((instance.GetType(), name), key =>
            {
                var property = key.Type.GetProperty(key.Name, PublicInstance | BindingFlags.NonPublic);
                return property is not null && property.GetIndexParameters().Length == 0
                    ? new RuntimeInstanceMember(property, null)
                    : new RuntimeInstanceMember(null, key.Type.GetField(key.Name, PublicInstance | BindingFlags.NonPublic));
            });
            return member.Property is not null ? member.Property.GetValue(instance) : member.Field?.GetValue(instance);
        }
        catch (Exception ex) { RecordCaptureFailure(ex, instance.GetType().FullName + "." + name); return null; }
    }

    private static bool? TryReadBool(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = ReadProperty(instance, name);
            if (value is bool result) return result;
        }
        return null;
    }

    private static int? TryReadInt(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = ReadProperty(instance, name);
            if (value is int result) return result;
            if (value is IConvertible convertible)
            {
                try { return convertible.ToInt32(System.Globalization.CultureInfo.InvariantCulture); }
                catch { }
            }
        }
        return null;
    }

    private static string ReadEnumName(object instance, string name) => ReadProperty(instance, name)?.ToString() ?? string.Empty;

    private static bool HasTag(object model, string tagName)
    {
        foreach (object tag in Enumerate(ReadProperty(model, "Tags")))
        {
            if (string.Equals(tag.ToString(), tagName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (MethodInfo method in RuntimeMethodsFor(model.GetType(), PublicInstance).Where(method => string.Equals(method.Name, "HasTag", StringComparison.Ordinal)))
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum) continue;
            try
            {
                object tag = Enum.Parse(parameters[0].ParameterType, tagName, ignoreCase: true);
                if (method.Invoke(model, new[] { tag }) is true) return true;
            }
            catch { }
        }
        return false;
    }

    private static IEnumerable<object> Enumerate(object? value)
    {
        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (object? item in enumerable)
            {
                if (item is not null) yield return item;
            }
        }
    }

    private static bool TryModelKey(object? model, out ModelKey key)
    {
        key = default;
        object? id = model;
        if (model is not null && !string.Equals(model.GetType().Name, "ModelId", StringComparison.Ordinal))
        {
            id = ReadProperty(model, "Id");
        }
        if (id is null) return false;
        string category = ReadProperty(id, "Category")?.ToString() ?? string.Empty;
        string entry = ReadProperty(id, "Entry")?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(entry)) return false;
        key = new ModelKey(category, entry);
        return key.IsValid;
    }

    private static IEnumerable<string> EnumerateModelKeys(object unlockState)
    {
        foreach (PropertyInfo property in unlockState.GetType().GetProperties(PublicInstance).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            object? value;
            try { value = property.GetIndexParameters().Length == 0 ? property.GetValue(unlockState) : null; }
            catch (Exception ex) { RecordCaptureFailure(ex, unlockState.GetType().FullName + "." + property.Name); continue; }
            foreach (object item in Enumerate(value))
            {
                if (TryModelKey(item, out ModelKey key)) yield return $"{property.Name}:{key.Serialized}";
            }
        }
    }

    private static IReadOnlyDictionary<ModelKey, ModelKey> CaptureCharacterByCardPool(object unlockState)
    {
        var output = new Dictionary<ModelKey, ModelKey>(ModelKeyComparer.Instance);
        foreach (object character in Enumerate(ReadProperty(unlockState, "Characters")))
        {
            if (!TryModelKey(character, out ModelKey characterKey) ||
                !string.Equals(characterKey.Category, BaseGameModelKeys.Categories.Character, StringComparison.Ordinal))
            {
                continue;
            }
            object? pool = new[] { "CardPool", "CharacterCardPool", "Cards" }
                .Select(name => ReadProperty(character, name))
                .FirstOrDefault(value => value is not null);
            if (pool is null || !TryModelKey(pool, out ModelKey poolKey))
            {
                continue;
            }
            output[poolKey] = characterKey;
        }
        return output;
    }

    private static string CharacterEntryFromPool(string entry)
    {
        if (entry.EndsWith("_CARD_POOL", StringComparison.OrdinalIgnoreCase))
        {
            return entry[..^"_CARD_POOL".Length];
        }
        if (entry.EndsWith("CardPool", StringComparison.Ordinal))
        {
            return entry[..^"CardPool".Length].ToUpperInvariant();
        }
        return entry.ToUpperInvariant();
    }

    private static bool TryParseCardRarity(string value, out EffectCardRarity rarity) =>
        Enum.TryParse(value, ignoreCase: true, out rarity);

    private static bool TryParseCardType(string value, out EffectCardType type)
    {
        if (Enum.TryParse(value, ignoreCase: true, out type)) return true;
        type = EffectCardType.Other;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryParsePotionRarity(string value, out EffectPotionRarity rarity)
    {
        if (Enum.TryParse(value, ignoreCase: true, out rarity)) return true;
        rarity = EffectPotionRarity.Other;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryParseRelicRarity(string value, out EffectRelicRarity rarity)
    {
        if (Enum.TryParse(value, ignoreCase: true, out rarity)) return true;
        rarity = EffectRelicRarity.Other;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsMultiplayerOnly(object model)
    {
        foreach (string name in new[] { "MultiplayerConstraint", "CardMultiplayerConstraint", "PotionMultiplayerConstraint", "RelicMultiplayerConstraint" })
        {
            string value = ReadProperty(model, name)?.ToString() ?? string.Empty;
            if (string.Equals(value, "MultiplayerOnly", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? SourceModIdFor(object model)
    {
        string? assemblyName = RuntimeAssemblyName(model.GetType());
        return string.Equals(assemblyName, "sts2", StringComparison.OrdinalIgnoreCase)
            ? null
            : assemblyName;
    }

    private static string Fingerprint(string prefix, IEnumerable<string>? values)
    {
        string payload = prefix + "\n" + string.Join("\n", values ?? Array.Empty<string>());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static IEnumerable<string> EnumerateCardDescriptors(
        IReadOnlyList<NeowEffectCardSnapshot>? selected,
        IReadOnlyList<NeowEffectCardSnapshot>? colorless,
        IReadOnlyList<CharacterCardPoolSnapshot>? others)
    {
        foreach (NeowEffectCardSnapshot card in selected ?? Array.Empty<NeowEffectCardSnapshot>()) yield return "selected:" + CardDescriptor(card);
        foreach (NeowEffectCardSnapshot card in colorless ?? Array.Empty<NeowEffectCardSnapshot>()) yield return "colorless:" + CardDescriptor(card);
        foreach (CharacterCardPoolSnapshot pool in others ?? Array.Empty<CharacterCardPoolSnapshot>())
        {
            yield return "pool:" + pool.CharacterKey.Serialized + ":" + pool.PoolId;
            foreach (NeowEffectCardSnapshot card in pool.Cards) yield return "other:" + CardDescriptor(card);
        }
    }

    private static string CardDescriptor(NeowEffectCardSnapshot card) =>
        $"{card.PoolOrder}:{card.CardKey.Serialized}:{card.Rarity}:{card.CardType}:{card.IsBasic}:{card.IsStrike}:{card.IsDefend}:{card.CanUpgrade}:{card.CanRemove}:{card.UpgradeLevel}:{card.MaxUpgradeLevel}:{card.PoolId}:{card.CanBeGeneratedByModifiers?.ToString() ?? "unknown"}:{card.IsMultiplayerOnly}:{card.CanBeGeneratedInCombat?.ToString() ?? "unknown"}:{card.IsUnlockedInCapturedPool}:{card.IsDiscovered?.ToString() ?? "unknown"}:{card.EligibleForPostCombatRewardByPoolMembership}:{card.EligibilityAuthority}:{card.CatalogProfileId}:{card.BehaviorMetadataExact}";

    private static string RelicDescriptor(NeowEffectRelicSnapshot relic) =>
        $"{relic.BagOrder}:{relic.RelicKey.Serialized}:{relic.Rarity}:{relic.RarityCode}:{relic.NestedEffectKind}:{relic.NestedClassificationExact}:{relic.SourceAssembly}:{relic.SourceModId}:{relic.IsMultiplayerOnly}:{relic.IsAllowedInShops}:{relic.ShopEligibilityExact}";

    private static string PotionDescriptor(NeowEffectPotionSnapshot potion) =>
        $"{potion.PoolOrder}:{potion.PotionKey.Serialized}:{potion.Rarity}:{potion.AllowedOutOfCombat}:{potion.IsMultiplayerOnly}";
}
