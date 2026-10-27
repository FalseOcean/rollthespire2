using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;

namespace RolltheSpire2.Ui.Persistence;

internal enum SearchEnvironmentComparisonStatus { Unknown, Same, Changed }
internal sealed record SearchEnvironmentComparison(SearchEnvironmentComparisonStatus Status, IReadOnlyList<string> Reasons, string CurrentCaptureIssue = "");

internal static class SearchEnvironmentIdentity
{
    internal const int CurrentVersion = 3;
    internal static bool IsComparable(SearchEnvironmentSignature? signature) => signature is
        { IdentityVersion: CurrentVersion, CaptureComplete: true, Fingerprint.Length: > 0 };

    internal static SearchEnvironmentComparison Compare(SearchEnvironmentSignature? previous, SearchEnvironmentSignature? current)
    {
        var reasons = new List<string>();
        var beforeVersion = GameVersionIdentity.From(previous?.GameVersionIdentity);
        var afterVersion = GameVersionIdentity.From(current?.GameVersionIdentity);
        if (beforeVersion.IsExact && afterVersion.IsExact && beforeVersion != afterVersion)
            reasons.Add("GameVersionChanged");
        if (!IsComparable(previous) || !IsComparable(current))
        {
            if (!IsComparable(current)) reasons.Add("CurrentEnvironmentUnavailable");
            if (!IsComparable(previous)) reasons.Add("HistoricalEnvironmentUnavailable");
            return new(reasons.Contains("GameVersionChanged") ? SearchEnvironmentComparisonStatus.Changed : SearchEnvironmentComparisonStatus.Unknown,
                [..reasons, "EnvironmentComponentsUnavailable"], current?.CaptureIssue ?? "");
        }
        void Difference(string before, string after, string reason)
        { if (before.Length > 0 && after.Length > 0 && before != after) reasons.Add(reason); }
        Difference(previous!.CharacterCatalogFingerprint, current!.CharacterCatalogFingerprint, "CharacterCatalogChanged");
        Difference(previous.CardCatalogFingerprint, current.CardCatalogFingerprint, "CardCatalogChanged");
        Difference(previous.RelicCatalogFingerprint, current.RelicCatalogFingerprint, "RelicCatalogChanged");
        Difference(previous.PotionCatalogFingerprint, current.PotionCatalogFingerprint, "PotionCatalogChanged");
        Difference(previous.WorldCatalogFingerprint, current.WorldCatalogFingerprint, "WorldCatalogChanged");
        Difference(previous.UnlockFingerprint, current.UnlockFingerprint, "UnlockStateChanged");
        if (previous.Fingerprint == current.Fingerprint && reasons.Count == 0) return new(SearchEnvironmentComparisonStatus.Same, []);
        if (reasons.Count == 0) reasons.Add("ContentCatalogChanged");
        return new(SearchEnvironmentComparisonStatus.Changed, reasons);
    }

    internal static string PresetMarkerNoticeKey(SearchPresetDefinition preset, SearchEnvironmentSignature? current)
    {
        if (preset.Source != SearchPresetSource.User) return "";
        if (!IsComparable(current)) return "ui1.library.environment.current_unavailable";
        if (string.IsNullOrWhiteSpace(preset.EnvironmentFingerprint)) return "ui1.library.environment.preset_marker_missing";
        return preset.EnvironmentFingerprint == current!.Fingerprint
            ? "ui1.library.environment.preset_marker_same" : "ui1.library.environment.preset_marker_different";
    }

    internal static string Describe(SearchEnvironmentComparison comparison, bool english)
    {
        if (comparison.Status == SearchEnvironmentComparisonStatus.Same) return "";
        if (comparison.Status == SearchEnvironmentComparisonStatus.Unknown)
        {
            var text = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(english ? "en" : "zh");
            return comparison.Reasons.Contains("CurrentEnvironmentUnavailable")
                ? text.Get("ui1.library.environment.current_unavailable") +
                    (comparison.CurrentCaptureIssue.Length > 0 ? " " + comparison.CurrentCaptureIssue : "")
                : text.Get("ui1.library.environment.history_unavailable");
        }
        string Reason(string reason) => (reason, english) switch {
            ("GameVersionChanged", false) => "游戏版本",
            ("CharacterCatalogChanged", false) => "角色内容或顺序",
            ("CardCatalogChanged", false) => "卡牌属性、卡池或顺序",
            ("RelicCatalogChanged", false) => "遗物属性、池或顺序",
            ("PotionCatalogChanged", false) => "药水属性、池或顺序",
            ("WorldCatalogChanged", false) => "世界内容、属性或顺序",
            ("UnlockStateChanged", false) => "解锁状态",
            ("GameVersionChanged", true) => "game version",
            ("CharacterCatalogChanged", true) => "character content or order",
            ("CardCatalogChanged", true) => "card properties, pools or order",
            ("RelicCatalogChanged", true) => "relic properties, pools or order",
            ("PotionCatalogChanged", true) => "potion properties, pools or order",
            ("WorldCatalogChanged", true) => "world content, properties or order",
            ("UnlockStateChanged", true) => "unlock state",
            _ => english ? "game content" : "游戏内容"
        };
        string reasons = string.Join(english ? ", " : "、", comparison.Reasons
            .Where(r => r is not ("EnvironmentComponentsUnavailable" or "CurrentEnvironmentUnavailable" or "HistoricalEnvironmentUnavailable")).Select(Reason));
        string unknown = comparison.Reasons.Contains("EnvironmentComponentsUnavailable")
            ? english ? " Other environment components cannot be compared." : "其他环境分量无法判定。" : "";
        if (comparison.Reasons.Contains("CurrentEnvironmentUnavailable"))
            unknown += " " + RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(english ? "en" : "zh")
                .Get("ui1.library.environment.current_unavailable") +
                (comparison.CurrentCaptureIssue.Length > 0 ? " " + comparison.CurrentCaptureIssue : "");
        return (english ? "This record differs from the current game environment: " + reasons + ". "
            : "此记录与当前游戏环境不同：" + reasons + "。") + unknown;
    }
}

internal static partial class SearchEnvironmentSignatureBuilder
{
    // Only captured prediction inputs participate. Module/build identities and
    // confidence/evidence labels remain diagnostics, never environment identity.
    internal static SearchEnvironmentSignature FromCapturedInputs(string gameVersion, RuntimeAuthoritySnapshot runtime,
        NeowEffectAuthoritySnapshot? effect, Beta109WorldGenerationSnapshot? world, SerializableUnlockState unlocks,
        string moduleDiagnostics = "", IReadOnlyList<CharacterStartingDeckSnapshot>? startingDecks = null)
    {
        var semantic = runtime.Fingerprint;
        object? Cards(IReadOnlyList<NeowEffectCardSnapshot>? cards) => cards?.Select(c => new {
            c.CardKey, c.PoolOrder, c.Rarity, c.CardType, c.IsBasic, c.IsStrike, c.IsDefend,
            c.CanUpgrade, c.CanRemove, c.UpgradeLevel, c.MaxUpgradeLevel, c.UpgradeTargetKey, c.PoolId,
            c.CanBeGeneratedByModifiers, c.IsMultiplayerOnly, c.CanBeGeneratedInCombat,
            c.IsUnlockedInCapturedPool, c.IsDiscovered, c.EligibleForPostCombatRewardByPoolMembership
        }).ToArray();
        object? Relics(IReadOnlyList<NeowEffectRelicSnapshot>? relics) => relics?.Select(r => new {
            r.RelicKey, r.BagOrder, r.Rarity, r.NestedEffectKind, r.IsMultiplayerOnly, r.RarityCode, r.IsAllowedInShops
        }).ToArray();
        object Buckets(IReadOnlyList<Beta109RelicBucketSnapshot> buckets) => buckets.Select(b => new {
            b.BucketId, b.OrderedRelics, Entries = b.OrderedEntries.Select(e => new { e.RelicKey, e.IsAllowedInShops }).ToArray()
        }).ToArray();
        string characters = IdentityHash(new { Semantic = semantic.GetDomainHash(RuntimeAuthorityDomains.Characters),
            Order = world?.AllCharactersInSourceOrder,
            StartingDecks = startingDecks?.OrderBy(d => d.CharacterKey.Serialized, StringComparer.Ordinal)
                .Select(d => new { d.CharacterKey, Deck = Cards(d.OrderedDeck) }).ToArray() });
        string cards = IdentityHash(new { Semantic = semantic.GetDomainHash(RuntimeAuthorityDomains.Cards),
            Deck = Cards(effect?.OrderedDeck), Character = Cards(effect?.CharacterRewardPool),
            Colorless = Cards(effect?.ColorlessRewardPool), Merchant = Cards(effect?.MerchantColorlessOrderedPool),
            Transform = Cards(effect?.TransformPool), Other = effect?.OtherCharacterPools?.Select(p => new {
                p.CharacterKey, p.PoolId, p.PoolOrder, Cards = Cards(p.Cards) }).ToArray(),
            effect?.GeneratedCursePool, effect?.CharacterStrikeKey, effect?.CharacterDefendKey, effect?.ClawKey });
        string relics = IdentityHash(new { Semantic = semantic.GetDomainHash(RuntimeAuthorityDomains.Relics),
            Bag = Relics(effect?.OrderedRelicBag), Shared = Relics(effect?.SharedRelicPoolSource),
            Character = Relics(effect?.CharacterRelicPoolSource), effect?.BonesEligibleRelics,
            SharedBuckets = world is null ? null : Buckets(world.SharedRelicBuckets),
            PersonalBuckets = world is null ? null : Buckets(world.PlayerRelicBuckets) });
        string potions = IdentityHash(new { Semantic = semantic.GetDomainHash(RuntimeAuthorityDomains.Potions),
            Pool = effect?.PotionPool?.Select(p => new { p.PotionKey, p.PoolOrder, p.Rarity,
                p.AllowedOutOfCombat, p.IsMultiplayerOnly }).ToArray() });
        string worldCatalog = IdentityHash(new {
            Events = semantic.GetDomainHash(RuntimeAuthorityDomains.Events),
            Ancients = semantic.GetDomainHash(RuntimeAuthorityDomains.Ancients),
            Bosses = semantic.GetDomainHash(RuntimeAuthorityDomains.Bosses),
            Groups = world?.ActSelectionGroups.Select(g => new { g.Act, g.EligibleActsInSourceOrder, g.SelectionMode,
                Candidates = g.Candidates.Select(c => new { c.SourceActIndex, c.CandidateOrdinal, c.ActKey, c.IsDefault }).ToArray() }).ToArray(),
            Acts = world?.OrderedActCatalog.Select(a => new {
                a.Act, a.ActKey, a.WeakEncounterSlots, a.TotalNormalRooms, a.EliteEncounterSlots,
                a.OrderedRawEvents, a.OrderedEligibleEvents, a.OrderedEvents, a.OrderedAncients,
                Encounters = a.OrderedGenerateAllEncounters.Select(e => new { e.EncounterKey, e.RoomType, e.IsWeak,
                    e.Tags, e.Weight, e.SharesTagsWith, e.SourceOrdinal, e.ReferenceIdentityId, e.SharesTagsWithSourceOrdinals }).ToArray()
            }).ToArray(),
            world?.AllSharedAncients, world?.SharedAncients, world?.SharedEvents,
            SharedEventsRaw = world?.EventAuthority.OrderedSharedEventsRaw,
            Epochs = world?.EventAuthority.EpochsInFilterOrder.Select(e => new { e.EpochId, e.OrderedMemberKeys, e.IsRevealed }).ToArray(),
            Options = world?.AncientEventContexts.Select(c => new { c.Act, c.AncientKey, c.IsShared,
                Pools = c.Catalog?.Pools.Select(p => new { p.PoolId, p.SourceOrdinal, p.FilterPredicateId,
                    p.IsDynamicProjection, p.OrderedOptions }).ToArray(),
                Variants = c.Catalog?.VariantData.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray() }).ToArray()
        });
        string unlock = IdentityHash(new { semantic.UnlockUniverseHash,
            State = runtime.CurrentUnlockState.OrderBy(d => d.Domain, StringComparer.Ordinal).Select(d => new {
                d.Domain, Values = d.CurrentUnlocked.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() }).ToArray(),
            Epochs = unlocks.UnlockedEpochs.Order().ToArray(),
            Seen = unlocks.EncountersSeen.Select(k => k.ToString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            FirstRun = unlocks.NumberOfRuns == 0 });
        string content = IdentityHash(new { semantic.SchemaVersion, Domains = semantic.DomainHashes.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(),
            semantic.UnlockUniverseHash, characters, cards, relics, potions, worldCatalog });
        string catalog = IdentityHash(new { characters, cards, relics, potions, worldCatalog, content });
        bool decksComplete = world is not null && startingDecks is { Count: > 0 } &&
            startingDecks.All(d => d.CharacterKey.IsValid && d.OrderedDeck.Count > 0) &&
            startingDecks.Select(d => d.CharacterKey).Distinct().Count() == startingDecks.Count &&
            startingDecks.Select(d => d.CharacterKey).ToHashSet().SetEquals(world.AllCharactersInSourceOrder);
        bool complete = GameVersionIdentity.From(gameVersion).IsExact && semantic.Complete && effect is not null && world is not null && decksComplete &&
            runtime.CurrentUnlockState.Count > 0 && runtime.CurrentUnlockState.All(d => d.StateComplete);
        return new SearchEnvironmentSignature {
            GameVersionIdentity = gameVersion, CharacterCatalogFingerprint = characters, CardCatalogFingerprint = cards,
            RelicCatalogFingerprint = relics, PotionCatalogFingerprint = potions, WorldCatalogFingerprint = worldCatalog,
            UnlockFingerprint = unlock, CatalogFingerprint = catalog, RuntimeContentFingerprint = content,
            Fingerprint = complete ? IdentityHash(new { IdentityVersion = SearchEnvironmentIdentity.CurrentVersion, gameVersion, catalog, unlock }) : "",
            RuntimeModuleDiagnostics = moduleDiagnostics, CaptureComplete = complete,
            CaptureIssue = complete ? "" : !decksComplete ? "CharacterStartingDecksIncomplete" :
                semantic.Complete ? "CapturedEnvironmentInputsIncomplete" : semantic.EvidenceCode
        };
    }

    private static string IdentityHash(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}
