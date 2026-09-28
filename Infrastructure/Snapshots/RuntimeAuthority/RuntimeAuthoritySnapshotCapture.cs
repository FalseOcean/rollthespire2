using System.Collections;
using System.Reflection;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;

/// <summary>
/// Main-thread-only capture of the live game facts RT2 actually relies on for content
/// identity/pool generation. No Godot/game model/UnlockState object escapes this method.
/// Reflection is intentionally limited to source-audited public runtime members.
/// </summary>
internal static class RuntimeAuthoritySnapshotCapture
{
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    public static RuntimeAuthoritySnapshot Capture(string gameVersion)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var issues = new List<string>();
        try
        {
            Assembly? gameAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
            if (gameAssembly is null)
                return RuntimeAuthoritySnapshot.Unavailable(gameVersion, "RuntimeAuthoritySts2AssemblyMissing");

            Type? modelDb = FindType(gameAssembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
            if (modelDb is null)
                return RuntimeAuthoritySnapshot.Unavailable(gameVersion, "RuntimeAuthorityModelDbMissing");

            IReadOnlyList<object> cardPools = ReadStaticModelsRaw(modelDb, "AllCardPools", issues);
            IReadOnlyList<object> relicPools = ReadStaticModelsRaw(modelDb, "AllRelicPools", issues);
            IReadOnlyList<object> potionPools = ReadStaticModelsRaw(modelDb, "AllPotionPools", issues);
            IReadOnlyList<object> acts = ReadActs(modelDb, issues);

            PoolMembershipCapture cardMembership = BuildPoolMembership(cardPools, "AllCards", issues, "Cards");
            PoolMembershipCapture relicMembership = BuildPoolMembership(relicPools, "AllRelics", issues, "Relics");
            PoolMembershipCapture potionMembership = BuildPoolMembership(potionPools, "AllPotions", issues, "Potions");

            var semanticDomains = new List<RuntimeSemanticDomainSnapshot>
            {
                CaptureCharacters(modelDb),
                CaptureCards(modelDb, cardMembership),
                CaptureRelics(modelDb, relicMembership),
                CaptureAncients(modelDb, acts),
                CapturePotions(potionPools, potionMembership),
                CaptureEvents(gameAssembly, modelDb, acts),
                CaptureBosses(acts)
            };

            object? currentUnlock = TryCaptureCurrentUnlockState(gameAssembly, out string currentUnlockEvidence);
            object? allUnlock = TryCaptureAllUnlockState(gameAssembly, out string allUnlockEvidence);
            (IReadOnlyList<RuntimeUnlockAuthorityDomainSnapshot> unlockAuthority,
                IReadOnlyList<RuntimeCurrentUnlockDomainSnapshot> currentUnlockState) = CaptureUnlockDomains(
                cardPools,
                relicPools,
                potionPools,
                acts,
                currentUnlock,
                allUnlock,
                currentUnlockEvidence,
                allUnlockEvidence);

            RuntimeSemanticFingerprintBundle fingerprint = RuntimeSemanticFingerprint.Build(
                RuntimeAuthorityFingerprintSchema.Current,
                semanticDomains,
                unlockAuthority);
            if (!fingerprint.Complete)
                issues.Add(fingerprint.EvidenceCode);

            return new RuntimeAuthoritySnapshot(
                gameVersion ?? string.Empty,
                RuntimeAuthorityFingerprintSchema.Current,
                semanticDomains,
                unlockAuthority,
                currentUnlockState,
                fingerprint,
                DateTimeOffset.UtcNow,
                issues.Distinct(StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex)
        {
            return RuntimeAuthoritySnapshot.Unavailable(
                gameVersion,
                "RuntimeAuthorityCaptureFailed:" + ex.GetType().Name + ":" + Compact(ex.Message));
        }
    }

    private static RuntimeSemanticDomainSnapshot CaptureCharacters(Type modelDb)
    {
        if (!TryReadStaticEnumerable(modelDb, "AllCharacters", out IReadOnlyList<object> values))
            return Missing(RuntimeAuthorityDomains.Characters, "ModelDb.AllCharactersMissing");

        var output = new List<RuntimeSemanticItem>();
        bool complete = true;
        foreach (object model in values)
        {
            if (!TryModelKey(model, out ModelKey key))
            {
                complete = false;
                continue;
            }
            string cardPool = ReadModelKeyProperty(model, "CardPool");
            string relicPool = ReadModelKeyProperty(model, "RelicPool");
            string potionPool = ReadModelKeyProperty(model, "PotionPool");
            if (cardPool.Length == 0 || relicPool.Length == 0 || potionPool.Length == 0)
                complete = false;
            output.Add(new RuntimeSemanticItem(
                key.Serialized,
                new[] { "cardPool=" + cardPool, "relicPool=" + relicPool, "potionPool=" + potionPool }));
        }
        return Domain(RuntimeAuthorityDomains.Characters, output, complete && output.Count == values.Count,
            "ModelDb.AllCharacters+CharacterModel.CardPool/RelicPool/PotionPool");
    }

    private static RuntimeSemanticDomainSnapshot CaptureCards(
        Type modelDb,
        PoolMembershipCapture membership)
    {
        if (!TryReadStaticEnumerable(modelDb, "AllCards", out IReadOnlyList<object> values))
            return Missing(RuntimeAuthorityDomains.Cards, "ModelDb.AllCardsMissing");

        // Schema 1 deliberately fingerprints only source-audited fields already consumed
        // by RT2's card generation / deck-coupled predictors. Presentation/localization
        // and implementation-code identity are excluded. Empty pool membership is legal
        // for non-generated special/status cards and is itself a stable semantic fact.
        var output = new List<RuntimeSemanticItem>();
        bool complete = true;
        foreach (object model in values)
        {
            if (!TryModelKey(model, out ModelKey key))
            {
                complete = false;
                continue;
            }

            string rarity = ReadEnumName(model, "Rarity");
            string cardType = ReadEnumName(model, "Type");
            bool? isUpgradable = TryReadBool(model, "IsUpgradable", "CanUpgrade");
            bool? isRemovable = TryReadBool(model, "IsRemovable", "CanRemove");
            int? maxUpgradeLevel = TryReadInt(model, "MaxUpgradeLevel");
            bool? canBeGeneratedByModifiers = TryReadBool(
                model,
                "CanBeGeneratedByModifiers",
                "CanGenerateByModifiers",
                "IsModifierGenerationAllowed");
            bool? multiplayerOnly = TryReadMultiplayerOnly(model);
            membership.Items.TryGetValue(key.Serialized, out string[]? pools);
            pools ??= Array.Empty<string>();

            if (rarity.Length == 0 || cardType.Length == 0 ||
                !isUpgradable.HasValue || !isRemovable.HasValue || !maxUpgradeLevel.HasValue ||
                !canBeGeneratedByModifiers.HasValue || !multiplayerOnly.HasValue)
            {
                complete = false;
            }

            output.Add(new RuntimeSemanticItem(
                key.Serialized,
                new[]
                {
                    "rarity=" + rarity,
                    "type=" + cardType,
                    "isUpgradable=" + CanonicalBool(isUpgradable),
                    "isRemovable=" + CanonicalBool(isRemovable),
                    "maxUpgradeLevel=" + CanonicalInt(maxUpgradeLevel),
                    "canBeGeneratedByModifiers=" + CanonicalBool(canBeGeneratedByModifiers),
                    "multiplayerOnly=" + CanonicalBool(multiplayerOnly),
                    "orderedPools=" + string.Join(",", pools)
                }));
        }
        return Domain(RuntimeAuthorityDomains.Cards, output,
            complete && membership.Exact && output.Count == values.Count,
            "ModelDb.AllCards+CardModel.Rarity/Type/upgrade-removal/modifier-generation+multiplayer applicability+ordered AllCardPools membership");
    }

    private static RuntimeSemanticDomainSnapshot CaptureRelics(
        Type modelDb,
        PoolMembershipCapture membership)
    {
        if (!TryReadStaticEnumerable(modelDb, "AllRelics", out IReadOnlyList<object> values))
            return Missing(RuntimeAuthorityDomains.Relics, "ModelDb.AllRelicsMissing");
        var output = new List<RuntimeSemanticItem>();
        bool complete = true;
        foreach (object model in values)
        {
            if (!TryModelKey(model, out ModelKey key))
            {
                complete = false;
                continue;
            }
            string rarity = ReadEnumName(model, "Rarity");
            bool? isAllowedInShops = TryReadBool(model, "IsAllowedInShops");
            membership.Items.TryGetValue(key.Serialized, out string[]? pools);
            pools ??= Array.Empty<string>();
            if (rarity.Length == 0 || !isAllowedInShops.HasValue) complete = false;
            output.Add(new RuntimeSemanticItem(
                key.Serialized,
                new[]
                {
                    "rarity=" + rarity,
                    "isAllowedInShops=" + CanonicalBool(isAllowedInShops),
                    "orderedPools=" + string.Join(",", pools)
                }));
        }
        return Domain(RuntimeAuthorityDomains.Relics, output,
            complete && membership.Exact && output.Count == values.Count,
            "ModelDb.AllRelics+RelicModel.Rarity/IsAllowedInShops+ordered AllRelicPools membership");
    }

    private static RuntimeSemanticDomainSnapshot CapturePotions(
        IReadOnlyList<object> potionPools,
        PoolMembershipCapture membership)
    {
        // Direct source audit shows PotionPoolModel.AllPotions as the authoritative
        // pool content source. Beta111 does not need (and may not expose) a global
        // ModelDb.AllPotions catalogue, so Schema 1 canonicalizes the union of live
        // AllPotionPools while retaining each potion's pool membership. Duplicate
        // identities must agree on the RT2-relevant static fields or authority fails.
        var byIdentity = new Dictionary<string, RuntimeSemanticItem>(StringComparer.Ordinal);
        bool complete = potionPools.Count > 0;
        foreach (object pool in potionPools)
        {
            if (!TryReadInstanceEnumerableAllowEmpty(pool, "AllPotions", out IReadOnlyList<object> values))
            {
                complete = false;
                continue;
            }

            foreach (object model in values)
            {
                if (!TryModelKey(model, out ModelKey key))
                {
                    complete = false;
                    continue;
                }

                string rarity = ReadEnumName(model, "Rarity");
                membership.Items.TryGetValue(key.Serialized, out string[]? pools);
                pools ??= Array.Empty<string>();
                if (rarity.Length == 0) complete = false;

                var item = new RuntimeSemanticItem(
                    key.Serialized,
                    new[]
                    {
                        "rarity=" + rarity,
                        "orderedPools=" + string.Join(",", pools)
                    });
                if (byIdentity.TryGetValue(key.Serialized, out RuntimeSemanticItem? existing) &&
                    !existing.Fields.SequenceEqual(item.Fields, StringComparer.Ordinal))
                {
                    complete = false;
                    continue;
                }
                byIdentity[key.Serialized] = item;
            }
        }

        return Domain(
            RuntimeAuthorityDomains.Potions,
            byIdentity.Values.OrderBy(item => item.StableIdentity, StringComparer.Ordinal).ToArray(),
            complete && membership.Exact,
            "ModelDb.AllPotionPools+PotionPoolModel.AllPotions+PotionModel.Rarity+ordered pool membership");
    }

    private static RuntimeSemanticDomainSnapshot CaptureAncients(Type modelDb, IReadOnlyList<object> acts)
    {
        var membership = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        bool complete = acts.Count > 0;
        if (!TryReadStaticEnumerableAllowEmpty(modelDb, "AllSharedAncients", out IReadOnlyList<object> shared))
        {
            complete = false;
        }
        else
        {
            AddMembership(shared, "shared", membership, ref complete);
        }

        foreach (object act in acts)
        {
            if (!TryModelKey(act, out ModelKey actKey))
            {
                complete = false;
                continue;
            }
            if (!TryReadInstanceEnumerableAllowEmpty(act, "AllAncients", out IReadOnlyList<object> ancients))
            {
                complete = false;
                continue;
            }
            AddMembership(ancients, "act=" + actKey.Serialized, membership, ref complete);
        }

        RuntimeSemanticItem[] items = membership
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new RuntimeSemanticItem(pair.Key, new[] { "membership=" + string.Join(",", pair.Value) }))
            .ToArray();
        return Domain(RuntimeAuthorityDomains.Ancients, items, complete,
            "ModelDb.AllSharedAncients+ActModel.AllAncients");
    }

    private static RuntimeSemanticDomainSnapshot CaptureEvents(
        Assembly assembly,
        Type modelDb,
        IReadOnlyList<object> acts)
    {
        if (!TryReadStaticEnumerable(modelDb, "AllEvents", out IReadOnlyList<object> allEvents))
            return Missing(RuntimeAuthorityDomains.Events, "ModelDb.AllEventsMissing");

        var membership = allEvents
            .Select(model => TryModelKey(model, out ModelKey key) ? key.Serialized : string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(value => value, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        bool complete = acts.Count > 0 && membership.Count == allEvents.Count;

        if (TryReadStaticEnumerableAllowEmpty(modelDb, "AllSharedEvents", out IReadOnlyList<object> shared))
            AddMembership(shared, "shared", membership, ref complete);
        else
            complete = false;

        foreach (object act in acts)
        {
            if (!TryModelKey(act, out ModelKey actKey))
            {
                complete = false;
                continue;
            }
            if (!TryReadInstanceEnumerableAllowEmpty(act, "AllEvents", out IReadOnlyList<object> events))
            {
                complete = false;
                continue;
            }
            AddMembership(events, "act=" + actKey.Serialized, membership, ref complete);
        }

        string[] epochTypeNames =
        {
            "MegaCrit.Sts2.Core.Timeline.Epochs.Event1Epoch",
            "MegaCrit.Sts2.Core.Timeline.Epochs.Event2Epoch",
            "MegaCrit.Sts2.Core.Timeline.Epochs.Event3Epoch"
        };
        for (int index = 0; index < epochTypeNames.Length; index++)
        {
            Type? epochType = assembly.GetType(epochTypeNames[index], false, false);
            if (epochType is null || !TryReadStaticEnumerableAllowEmpty(epochType, "Events", out IReadOnlyList<object> events))
            {
                complete = false;
                continue;
            }
            AddMembership(events, "epoch=" + (index + 1), membership, ref complete);
        }

        RuntimeSemanticItem[] items = membership
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new RuntimeSemanticItem(pair.Key, new[] { "membership=" + string.Join(",", pair.Value) }))
            .ToArray();
        return Domain(RuntimeAuthorityDomains.Events, items, complete,
            "ModelDb.AllEvents+AllSharedEvents+ActModel.AllEvents+Event1/2/3Epoch.Events");
    }

    private static RuntimeSemanticDomainSnapshot CaptureBosses(IReadOnlyList<object> acts)
    {
        var membership = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        bool complete = acts.Count > 0;
        foreach (object act in acts)
        {
            if (!TryModelKey(act, out ModelKey actKey))
            {
                complete = false;
                continue;
            }
            if (!TryReadInstanceEnumerableAllowEmpty(act, "AllBossEncounters", out IReadOnlyList<object> bosses) &&
                !TryReadInstanceEnumerableAllowEmpty(act, "BossEncounters", out bosses))
            {
                complete = false;
                continue;
            }
            AddMembership(bosses, "act=" + actKey.Serialized, membership, ref complete);
        }
        RuntimeSemanticItem[] items = membership
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new RuntimeSemanticItem(pair.Key, new[] { "membership=" + string.Join(",", pair.Value) }))
            .ToArray();
        return Domain(RuntimeAuthorityDomains.Bosses, items, complete,
            "ActModel.AllBossEncounters membership");
    }

    private static (IReadOnlyList<RuntimeUnlockAuthorityDomainSnapshot> Authority,
        IReadOnlyList<RuntimeCurrentUnlockDomainSnapshot> CurrentState) CaptureUnlockDomains(
        IReadOnlyList<object> cardPools,
        IReadOnlyList<object> relicPools,
        IReadOnlyList<object> potionPools,
        IReadOnlyList<object> acts,
        object? currentUnlock,
        object? allUnlock,
        string currentEvidence,
        string allEvidence)
    {
        var authority = new List<RuntimeUnlockAuthorityDomainSnapshot>();
        var current = new List<RuntimeCurrentUnlockDomainSnapshot>();

        AddPropertyDomain(RuntimeAuthorityDomains.Characters, "Characters");
        AddPoolDomain(RuntimeAuthorityDomains.Cards, cardPools, "GetUnlockedCards");
        AddPoolDomain(RuntimeAuthorityDomains.Relics, relicPools, "GetUnlockedRelics");
        AddAncientDomain();
        AddPoolDomain(RuntimeAuthorityDomains.Potions, potionPools, "GetUnlockedPotions");
        return (authority, current);

        void AddPropertyDomain(string domain, string property)
        {
            (string[] all, bool allExact) = ReadModelIdentityProperty(allUnlock, property);
            (string[] unlocked, bool currentExact) = ReadModelIdentityProperty(currentUnlock, property);
            authority.Add(new RuntimeUnlockAuthorityDomainSnapshot(
                domain, all, allExact, $"{allEvidence}:{property}"));
            current.Add(new RuntimeCurrentUnlockDomainSnapshot(
                domain, unlocked, currentExact, $"{currentEvidence}:{property}"));
        }

        void AddPoolDomain(string domain, IReadOnlyList<object> pools, string methodName)
        {
            (string[] all, bool allExact) = InvokeUnlockedAcrossPools(pools, methodName, allUnlock);
            (string[] unlocked, bool currentExact) = InvokeUnlockedAcrossPools(pools, methodName, currentUnlock);
            authority.Add(new RuntimeUnlockAuthorityDomainSnapshot(
                domain, all, allExact, $"{allEvidence}:{methodName};poolCount={pools.Count}"));
            current.Add(new RuntimeCurrentUnlockDomainSnapshot(
                domain, unlocked, currentExact, $"{currentEvidence}:{methodName};poolCount={pools.Count}"));
        }

        void AddAncientDomain()
        {
            (string[] all, bool allExact) = CaptureUnlockedAncients(acts, allUnlock);
            (string[] unlocked, bool currentExact) = CaptureUnlockedAncients(acts, currentUnlock);
            authority.Add(new RuntimeUnlockAuthorityDomainSnapshot(
                RuntimeAuthorityDomains.Ancients, all, allExact,
                $"{allEvidence};UnlockState.SharedAncients+ActModel.GetUnlockedAncients"));
            current.Add(new RuntimeCurrentUnlockDomainSnapshot(
                RuntimeAuthorityDomains.Ancients, unlocked, currentExact,
                $"{currentEvidence};UnlockState.SharedAncients+ActModel.GetUnlockedAncients"));
        }
    }

    private static (string[] Identities, bool Exact) CaptureUnlockedAncients(
        IReadOnlyList<object> acts,
        object? unlockState)
    {
        if (unlockState is null) return (Array.Empty<string>(), false);
        var output = new HashSet<string>(StringComparer.Ordinal);
        bool exact = true;
        (string[] shared, bool sharedExact) = ReadModelIdentityProperty(unlockState, "SharedAncients");
        exact &= sharedExact;
        foreach (string identity in shared) output.Add(identity);

        foreach (object act in acts)
        {
            if (!TryInvokeUnlocked(act, "GetUnlockedAncients", unlockState, out IReadOnlyList<object> values))
            {
                exact = false;
                continue;
            }
            foreach (object value in values)
            {
                if (TryModelKey(value, out ModelKey key)) output.Add(key.Serialized);
                else exact = false;
            }
        }
        return (output.OrderBy(value => value, StringComparer.Ordinal).ToArray(), exact);
    }

    private static (string[] Identities, bool Exact) InvokeUnlockedAcrossPools(
        IReadOnlyList<object> pools,
        string methodName,
        object? unlockState)
    {
        if (unlockState is null || pools.Count == 0) return (Array.Empty<string>(), false);
        var output = new HashSet<string>(StringComparer.Ordinal);
        bool exact = true;
        foreach (object pool in pools)
        {
            if (!TryInvokeUnlocked(pool, methodName, unlockState, out IReadOnlyList<object> values))
            {
                exact = false;
                continue;
            }
            foreach (object value in values)
            {
                if (TryModelKey(value, out ModelKey key)) output.Add(key.Serialized);
                else exact = false;
            }
        }
        return (output.OrderBy(value => value, StringComparer.Ordinal).ToArray(), exact);
    }

    private static bool TryInvokeUnlocked(
        object owner,
        string methodName,
        object unlockState,
        out IReadOnlyList<object> values)
    {
        values = Array.Empty<object>();
        MethodInfo[] candidates = owner.GetType().GetMethods(PublicInstance)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();
        foreach (MethodInfo method in candidates)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length is < 1 or > 2 || !parameters[0].ParameterType.IsInstanceOfType(unlockState))
                continue;
            var args = new object?[parameters.Length];
            args[0] = unlockState;
            if (parameters.Length == 2)
            {
                if (!TryCreateSingleplayerConstraint(parameters[1], out object? constraint))
                    continue;
                args[1] = constraint;
            }
            try
            {
                if (method.Invoke(owner, args) is IEnumerable enumerable)
                {
                    values = Enumerate(enumerable).ToArray();
                    return true;
                }
            }
            catch
            {
                // Fail closed. Do not substitute raw pool contents for unlock authority.
            }
        }
        return false;
    }

    private static bool TryCreateSingleplayerConstraint(ParameterInfo parameter, out object? value)
    {
        Type type = parameter.ParameterType;
        if (type.IsEnum)
        {
            foreach (string preferred in new[] { "SingleplayerOnly", "SinglePlayerOnly", "Singleplayer" })
            {
                if (Enum.GetNames(type).Any(name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase)))
                {
                    value = Enum.Parse(type, preferred, true);
                    return true;
                }
            }
        }
        if (parameter.HasDefaultValue)
        {
            value = parameter.DefaultValue;
            return true;
        }
        value = null;
        return false;
    }

    private static object? TryCaptureCurrentUnlockState(Assembly assembly, out string evidence)
    {
        Type? saveType = FindType(assembly, "MegaCrit.Sts2.Core.Saves.SaveManager", "SaveManager");
        object? save = saveType?.GetProperty("Instance", PublicStatic)?.GetValue(null);
        if (save is null)
        {
            evidence = "SaveManager.InstanceMissing";
            return null;
        }
        try
        {
            PropertyInfo? initialized = save.GetType().GetProperty("IsProfileInitialized", PublicInstance);
            if (initialized?.GetValue(save) is bool value && !value)
            {
                evidence = "SaveProfileNotInitialized";
                return null;
            }
            MethodInfo? method = save.GetType().GetMethod(
                "GenerateUnlockStateFromProgress",
                PublicInstance,
                null,
                Type.EmptyTypes,
                null);
            object? result = method?.Invoke(save, null);
            evidence = result is null
                ? "GenerateUnlockStateFromProgressUnavailable"
                : "SaveManager.GenerateUnlockStateFromProgress";
            return result;
        }
        catch (Exception ex)
        {
            evidence = "GenerateUnlockStateFromProgressFailed:" + ex.GetType().Name;
            return null;
        }
    }

    private static object? TryCaptureAllUnlockState(Assembly assembly, out string evidence)
    {
        Type? type = FindType(assembly, "MegaCrit.Sts2.Core.Unlocks.UnlockState", "UnlockState");
        if (type is null)
        {
            evidence = "UnlockStateTypeMissing";
            return null;
        }
        foreach (string name in new[] { "all", "All" })
        {
            try
            {
                object? property = type.GetProperty(name, PublicStatic)?.GetValue(null);
                if (property is not null)
                {
                    evidence = "UnlockState." + name;
                    return property;
                }
                object? field = type.GetField(name, PublicStatic)?.GetValue(null);
                if (field is not null)
                {
                    evidence = "UnlockState." + name;
                    return field;
                }
            }
            catch
            {
                // Try the other audited spelling/member shape.
            }
        }
        evidence = "UnlockState.allMissing";
        return null;
    }

    private static (string[] Values, bool Exact) ReadModelIdentityProperty(object? owner, string property)
    {
        if (owner is null) return (Array.Empty<string>(), false);
        object? raw;
        try { raw = owner.GetType().GetProperty(property, PublicInstance)?.GetValue(owner); }
        catch { return (Array.Empty<string>(), false); }
        if (raw is not IEnumerable enumerable) return (Array.Empty<string>(), false);
        var output = new HashSet<string>(StringComparer.Ordinal);
        bool exact = true;
        foreach (object value in Enumerate(enumerable))
        {
            if (TryModelKey(value, out ModelKey key)) output.Add(key.Serialized);
            else exact = false;
        }
        return (output.OrderBy(value => value, StringComparer.Ordinal).ToArray(), exact);
    }

    private static IReadOnlyList<object> ReadActs(Type modelDb, List<string> issues)
    {
        if (TryReadStaticEnumerable(modelDb, "AllActs", out IReadOnlyList<object> acts))
            return acts;
        try
        {
            object? raw = modelDb.GetProperty("ActsByIndex", PublicStatic)?.GetValue(null);
            if (raw is IEnumerable outer)
            {
                var output = new List<object>();
                foreach (object group in Enumerate(outer))
                {
                    if (group is IEnumerable inner)
                        output.AddRange(Enumerate(inner));
                }
                ModelKey[] keys = output.Select(item => TryModelKey(item, out ModelKey key) ? key : default)
                    .Where(key => key.IsValid).ToArray();
                if (keys.Length == output.Count && output.Count > 0)
                    return output.DistinctBy(item => TryModelKey(item, out ModelKey key) ? key.Serialized : string.Empty, StringComparer.Ordinal).ToArray();
            }
        }
        catch { }
        issues.Add("ModelDb.ActsByIndexMissing");
        return Array.Empty<object>();
    }

    private static IReadOnlyList<object> ReadStaticModelsRaw(Type modelDb, string property, List<string> issues)
    {
        if (TryReadStaticEnumerable(modelDb, property, out IReadOnlyList<object> values))
            return values;
        issues.Add("ModelDb." + property + "Missing");
        return Array.Empty<object>();
    }

    private sealed record PoolMembershipCapture(
        IReadOnlyDictionary<string, string[]> Items,
        bool Exact);

    private static PoolMembershipCapture BuildPoolMembership(
        IReadOnlyList<object> pools,
        string itemsProperty,
        List<string> issues,
        string domain)
    {
        // Pool model enumeration order is not hashed. Each pool is identified by stable
        // ModelKey. The ordinal inside a source-audited generator collection *is* semantic:
        // RT2's RNG selection indexes those ordered lists, so an official reorder changes
        // prediction identity even when the membership set stays the same.
        var membership = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        bool complete = pools.Count > 0;
        foreach (object pool in pools)
        {
            if (!TryModelKey(pool, out ModelKey poolKey))
            {
                complete = false;
                continue;
            }
            if (!TryReadInstanceEnumerableAllowEmpty(pool, itemsProperty, out IReadOnlyList<object> items))
            {
                complete = false;
                continue;
            }
            for (int ordinal = 0; ordinal < items.Count; ordinal++)
            {
                object item = items[ordinal];
                if (!TryModelKey(item, out ModelKey itemKey))
                {
                    complete = false;
                    continue;
                }
                if (!membership.TryGetValue(itemKey.Serialized, out SortedSet<string>? set))
                {
                    set = new SortedSet<string>(StringComparer.Ordinal);
                    membership[itemKey.Serialized] = set;
                }
                set.Add($"pool={poolKey.Serialized};ordinal={ordinal:D8}");
            }
        }
        if (!complete) issues.Add("RuntimeAuthorityPoolMembershipIncomplete:" + domain);
        return new PoolMembershipCapture(
            membership.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal),
            complete);
    }

    private static void AddMembership(
        IEnumerable<object> models,
        string membershipToken,
        IDictionary<string, SortedSet<string>> membership,
        ref bool complete)
    {
        int ordinal = 0;
        foreach (object model in models)
        {
            if (!TryModelKey(model, out ModelKey key))
            {
                complete = false;
                ordinal++;
                continue;
            }
            if (!membership.TryGetValue(key.Serialized, out SortedSet<string>? set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                membership[key.Serialized] = set;
            }
            set.Add($"{membershipToken};ordinal={ordinal:D8}");
            ordinal++;
        }
    }

    private static RuntimeSemanticDomainSnapshot Domain(
        string name,
        IReadOnlyList<RuntimeSemanticItem> items,
        bool complete,
        string evidence) => new(
            name,
            items.OrderBy(item => item.StableIdentity, StringComparer.Ordinal).ToArray(),
            complete && items.Count > 0,
            evidence);

    private static RuntimeSemanticDomainSnapshot Missing(string domain, string evidence) =>
        new(domain, Array.Empty<RuntimeSemanticItem>(), false, evidence);

    private static bool TryReadStaticEnumerable(Type type, string property, out IReadOnlyList<object> values)
    {
        values = Array.Empty<object>();
        try
        {
            if (type.GetProperty(property, PublicStatic)?.GetValue(null) is not IEnumerable enumerable)
                return false;
            values = Enumerate(enumerable).ToArray();
            return values.Count > 0;
        }
        catch { return false; }
    }

    private static bool TryReadStaticEnumerableAllowEmpty(Type type, string property, out IReadOnlyList<object> values)
    {
        values = Array.Empty<object>();
        try
        {
            if (type.GetProperty(property, PublicStatic)?.GetValue(null) is not IEnumerable enumerable)
                return false;
            values = Enumerate(enumerable).ToArray();
            return true;
        }
        catch { return false; }
    }

    private static bool TryReadInstanceEnumerableAllowEmpty(object owner, string property, out IReadOnlyList<object> values)
    {
        values = Array.Empty<object>();
        try
        {
            if (owner.GetType().GetProperty(property, PublicInstance)?.GetValue(owner) is not IEnumerable enumerable)
                return false;
            values = Enumerate(enumerable).ToArray();
            return true;
        }
        catch { return false; }
    }

    private static IEnumerable<object> Enumerate(IEnumerable source)
    {
        foreach (object? value in source)
            if (value is not null) yield return value;
    }

    private static bool? TryReadBool(object owner, params string[] properties)
    {
        foreach (string property in properties)
        {
            try
            {
                object? value = owner.GetType().GetProperty(property, PublicInstance)?.GetValue(owner);
                if (value is bool result) return result;
            }
            catch
            {
                // Try the next source-audited spelling.
            }
        }
        return null;
    }

    private static int? TryReadInt(object owner, params string[] properties)
    {
        foreach (string property in properties)
        {
            try
            {
                object? value = owner.GetType().GetProperty(property, PublicInstance)?.GetValue(owner);
                if (value is int result) return result;
            }
            catch
            {
                // Try the next source-audited spelling.
            }
        }
        return null;
    }

    private static bool? TryReadMultiplayerOnly(object model)
    {
        // Multiplayer applicability changes the effective single-player generation
        // universe consumed by RT2. Unlike the legacy projection adapter, fingerprint
        // authority cannot interpret a missing constraint member as a trustworthy
        // default false: doing so would allow a semantic change to hash as vanilla.
        foreach (string property in new[]
                 {
                     "MultiplayerConstraint",
                     "CardMultiplayerConstraint",
                     "PotionMultiplayerConstraint",
                     "RelicMultiplayerConstraint"
                 })
        {
            PropertyInfo? member = model.GetType().GetProperty(property, PublicInstance);
            if (member is null)
                continue;
            try
            {
                object? raw = member.GetValue(model);
                if (raw is null)
                    return null;
                string value = raw.ToString() ?? string.Empty;
                if (value.Length == 0)
                    return null;
                return string.Equals(value, "MultiplayerOnly", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "MultiPlayerOnly", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static string CanonicalBool(bool? value) => value.HasValue
        ? value.Value ? "true" : "false"
        : "unknown";

    private static string CanonicalInt(int? value) => value.HasValue
        ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "unknown";

    private static string ReadEnumName(object owner, string property)
    {
        try
        {
            object? value = owner.GetType().GetProperty(property, PublicInstance)?.GetValue(owner);
            return value?.ToString()?.Trim() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static string ReadModelKeyProperty(object owner, string property)
    {
        try
        {
            object? value = owner.GetType().GetProperty(property, PublicInstance)?.GetValue(owner);
            return value is not null && TryModelKey(value, out ModelKey key) ? key.Serialized : string.Empty;
        }
        catch { return string.Empty; }
    }

    private static bool TryModelKey(object model, out ModelKey key)
    {
        key = default;
        try
        {
            if (model is ModelKey direct)
            {
                key = direct;
                return direct.IsValid;
            }
            object? id = model.GetType().GetProperty("Id", PublicInstance)?.GetValue(model);
            if (id is null) return false;
            string category = id.GetType().GetProperty("Category", PublicInstance)?.GetValue(id)?.ToString() ?? string.Empty;
            string entry = id.GetType().GetProperty("Entry", PublicInstance)?.GetValue(id)?.ToString() ?? string.Empty;
            key = new ModelKey(category, entry);
            return key.IsValid;
        }
        catch { return false; }
    }

    private static Type? FindType(Assembly assembly, string fullName, string simpleName) =>
        assembly.GetType(fullName, false, false) ?? SafeGetTypes(assembly).FirstOrDefault(type =>
            string.Equals(type.Name, simpleName, StringComparison.Ordinal));

    private static IReadOnlyList<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
        catch { return Array.Empty<Type>(); }
    }

    private static string Compact(string value)
    {
        string compact = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 180 ? compact : compact[..180];
    }
}
