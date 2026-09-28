using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Ui.Controls.Pickers;

namespace RolltheSpire2.Ui.Pages.Search.Relic;

internal sealed record RelicSequenceSearchUiCatalog(
    RuntimeProfileId ProfileId,
    IReadOnlyDictionary<RelicSequenceKind, IReadOnlyList<ModelKey>> LaneCandidates,
    IReadOnlyList<ModelKey> AllCandidates,
    IReadOnlyDictionary<ModelKey, RelicSequenceKind> LaneByRelic,
    IReadOnlyDictionary<ModelKey, RelicPickerCategory> Categories,
    bool LaneMappingExact,
    bool CatalogAvailable,
    string EvidenceCode)
{
    private static readonly RelicSequenceKind[] LaneOrder =
    {
        RelicSequenceKind.Common,
        RelicSequenceKind.Uncommon,
        RelicSequenceKind.Rare,
        RelicSequenceKind.Shop
    };

    public static RelicSequenceSearchUiCatalog Empty(RuntimeProfileId profileId, string evidenceCode) => new(
        profileId,
        LaneOrder.ToDictionary(
            lane => lane,
            _ => (IReadOnlyList<ModelKey>)Array.Empty<ModelKey>()),
        Array.Empty<ModelKey>(),
        new Dictionary<ModelKey, RelicSequenceKind>(ModelKeyComparer.Instance),
        new Dictionary<ModelKey, RelicPickerCategory>(ModelKeyComparer.Instance),
        false,
        false,
        evidenceCode);

    public static RelicSequenceSearchUiCatalog FromAuthority(
        RuntimeProfileId profileId,
        WorldAuthoritySnapshot? world)
    {
        if (world is null)
        {
            return Empty(profileId, "relic-sequence-ui-world-authority-missing");
        }

        return profileId == RuntimeProfileId.Stable107
            ? FromLegacy(profileId, world)
            : RuntimeProfilePolicies.IsModernCore(profileId)
                ? FromModern(profileId, world)
                : Empty(profileId, "relic-sequence-ui-profile-unsupported");
    }

    public IReadOnlyList<ModelKey> CandidatesFor(RelicSequenceKind lane) =>
        LaneCandidates.TryGetValue(lane, out IReadOnlyList<ModelKey>? candidates)
            ? candidates
            : Array.Empty<ModelKey>();

    public bool TryResolveLane(ModelKey relicKey, out RelicSequenceKind lane) =>
        LaneByRelic.TryGetValue(relicKey, out lane);

    private static RelicSequenceSearchUiCatalog FromLegacy(
        RuntimeProfileId profileId,
        WorldAuthoritySnapshot world)
    {
        IReadOnlyList<NeowEffectRelicSnapshot> source =
            (world.SharedRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>())
            .Concat(world.CharacterRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>())
            .Where(relic => relic.RelicKey.IsValid)
            .ToArray();
        if (source.Count == 0)
        {
            return Empty(profileId, "relic-sequence-ui-legacy-catalog-empty");
        }

        var lanes = NewLaneLists();
        foreach (NeowEffectRelicSnapshot relic in source)
        {
            if (!TryMapLegacyRarity(relic, out RelicSequenceKind lane))
            {
                continue;
            }
            if (lane == RelicSequenceKind.Shop && !relic.IsAllowedInShops)
            {
                continue;
            }
            AddDistinct(lanes[lane], relic.RelicKey);
        }

        return Build(
            profileId,
            lanes,
            world.HasExactLegacyFoundation,
            world.HasExactLegacyFoundation,
            world.HasExactLegacyFoundation
                ? "relic-sequence-ui-legacy-runtime-catalog"
                : "relic-sequence-ui-legacy-runtime-catalog-partial");
    }

    private static RelicSequenceSearchUiCatalog FromModern(
        RuntimeProfileId profileId,
        WorldAuthoritySnapshot world)
    {
        Beta109WorldGenerationSnapshot? modern = world.Beta109Generation;
        if (modern is null || modern.PlayerRelicBuckets.Count == 0)
        {
            return Empty(profileId, "relic-sequence-ui-modern-catalog-empty");
        }

        var lanes = NewLaneLists();
        foreach (Beta109RelicBucketSnapshot bucket in modern.PlayerRelicBuckets)
        {
            if (!TryMapBucket(bucket.BucketId, out RelicSequenceKind lane))
            {
                continue;
            }

            if (bucket.OrderedEntries.Count == bucket.OrderedRelics.Count &&
                bucket.OrderedEntries.Count > 0)
            {
                foreach (Beta109RelicBucketEntrySnapshot entry in bucket.OrderedEntries)
                {
                    if (!entry.RelicKey.IsValid ||
                        lane == RelicSequenceKind.Shop && !entry.IsAllowedInShops)
                    {
                        continue;
                    }
                    AddDistinct(lanes[lane], entry.RelicKey);
                }
            }
            else
            {
                foreach (ModelKey key in bucket.OrderedRelics.Where(key => key.IsValid))
                {
                    AddDistinct(lanes[lane], key);
                }
            }
        }

        bool exact = modern.RelicInitializationExact &&
                     modern.SharedRelicPoolOrderExact &&
                     modern.CharacterRelicPoolOrderExact &&
                     modern.RelicRarityAuthorityExact &&
                     modern.RelicShopEligibilityAuthorityExact &&
                     modern.PlayerRelicPoolCompositionExact;
        bool laneMappingExact = modern.RelicRarityAuthorityExact &&
                                modern.RelicShopEligibilityAuthorityExact &&
                                modern.PlayerRelicPoolCompositionExact;
        return Build(
            profileId,
            lanes,
            exact,
            laneMappingExact,
            exact
                ? "relic-sequence-ui-modern-runtime-catalog"
                : "relic-sequence-ui-modern-runtime-catalog-partial");
    }

    private static RelicSequenceSearchUiCatalog Build(
        RuntimeProfileId profileId,
        IReadOnlyDictionary<RelicSequenceKind, List<ModelKey>> laneLists,
        bool exact,
        bool laneMappingExact,
        string evidenceCode)
    {
        // UI discovery is deliberately weaker than Prediction authority. A Mod
        // character can make relic composition/rarity authority Partial while the
        // runtime snapshot still contains stable bucket membership for known or
        // opaque relic identities. Preserve those captured candidates for authoring
        // instead of collapsing the whole Relic Picker to empty. Search/Prediction
        // still consume the original exactness flags and may downgrade/fail closed.
        var memberships = new Dictionary<ModelKey, HashSet<RelicSequenceKind>>(ModelKeyComparer.Instance);
        foreach (RelicSequenceKind lane in LaneOrder)
        {
            foreach (ModelKey key in laneLists[lane])
            {
                if (!memberships.TryGetValue(key, out HashSet<RelicSequenceKind>? lanesForKey))
                {
                    lanesForKey = new HashSet<RelicSequenceKind>();
                    memberships[key] = lanesForKey;
                }
                lanesForKey.Add(lane);
            }
        }

        var laneByRelic = new Dictionary<ModelKey, RelicSequenceKind>(ModelKeyComparer.Instance);
        foreach ((ModelKey key, HashSet<RelicSequenceKind> lanesForKey) in memberships)
        {
            if (lanesForKey.Count == 1)
            {
                laneByRelic[key] = lanesForKey.Single();
            }
        }

        var lanes = new Dictionary<RelicSequenceKind, IReadOnlyList<ModelKey>>();
        var all = new List<ModelKey>();
        var categories = new Dictionary<ModelKey, RelicPickerCategory>(ModelKeyComparer.Instance);
        foreach (RelicSequenceKind lane in LaneOrder)
        {
            ModelKey[] keys = laneLists[lane]
                .Where(key => laneByRelic.TryGetValue(key, out RelicSequenceKind mapped) && mapped == lane)
                .ToArray();
            lanes[lane] = keys;
            foreach (ModelKey key in keys)
            {
                AddDistinct(all, key);
                categories[key] = PickerCategory(lane);
            }
        }

        bool ambiguousMappingsExcluded = memberships.Count != laneByRelic.Count;
        return new RelicSequenceSearchUiCatalog(
            profileId,
            lanes,
            all,
            laneByRelic,
            categories,
            laneMappingExact,
            all.Count > 0,
            evidenceCode + (exact ? ":exact" : ":partial") +
            (laneMappingExact ? string.Empty : ":lane-mapping-partial-ui-discovery") +
            (ambiguousMappingsExcluded ? ":ambiguous-mappings-excluded" : string.Empty));
    }

    private static Dictionary<RelicSequenceKind, List<ModelKey>> NewLaneLists() =>
        LaneOrder.ToDictionary(lane => lane, _ => new List<ModelKey>());

    private static void AddDistinct(List<ModelKey> target, ModelKey key)
    {
        if (!target.Contains(key, ModelKeyComparer.Instance))
        {
            target.Add(key);
        }
    }

    private static bool TryMapLegacyRarity(
        NeowEffectRelicSnapshot relic,
        out RelicSequenceKind lane)
    {
        if (!string.IsNullOrWhiteSpace(relic.RarityCode) &&
            TryMapRarityToken(relic.RarityCode, out lane))
        {
            return true;
        }

        lane = relic.Rarity switch
        {
            EffectRelicRarity.Common => RelicSequenceKind.Common,
            EffectRelicRarity.Uncommon => RelicSequenceKind.Uncommon,
            EffectRelicRarity.Rare => RelicSequenceKind.Rare,
            EffectRelicRarity.Shop => RelicSequenceKind.Shop,
            _ => default
        };
        return relic.Rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or
            EffectRelicRarity.Rare or EffectRelicRarity.Shop;
    }

    private static bool TryMapBucket(string bucketId, out RelicSequenceKind lane)
    {
        int separator = bucketId.LastIndexOf(':');
        string rarity = separator >= 0 && separator < bucketId.Length - 1
            ? bucketId[(separator + 1)..]
            : bucketId;
        return TryMapRarityToken(rarity, out lane);
    }

    private static bool TryMapRarityToken(string token, out RelicSequenceKind lane)
    {
        switch (token.Trim().ToUpperInvariant())
        {
            case "COMMON": lane = RelicSequenceKind.Common; return true;
            case "UNCOMMON": lane = RelicSequenceKind.Uncommon; return true;
            case "RARE": lane = RelicSequenceKind.Rare; return true;
            case "SHOP": lane = RelicSequenceKind.Shop; return true;
            default: lane = default; return false;
        }
    }

    private static RelicPickerCategory PickerCategory(RelicSequenceKind lane) => lane switch
    {
        RelicSequenceKind.Common => RelicPickerCategory.Common,
        RelicSequenceKind.Uncommon => RelicPickerCategory.Uncommon,
        RelicSequenceKind.Rare => RelicPickerCategory.Rare,
        RelicSequenceKind.Shop => RelicPickerCategory.Shop,
        _ => RelicPickerCategory.Other
    };
}
