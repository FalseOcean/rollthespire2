using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

internal static class RelicSequenceFixtureFactory
{
    public const string LegacySeed = "H0T0Z0S23C";
    public const string ModernSeed = "VADSEV23455G";

    public static WorldAuthoritySnapshot CreateLegacy(bool exactShopEligibility = true)
    {
        NeowEffectRelicSnapshot[] shared =
        {
            Relic("L_SHARED_C0", "Common", true, exactShopEligibility, 0),
            Relic("L_SHARED_C1", "Common", true, exactShopEligibility, 1),
            Relic("L_SHARED_U0", "Uncommon", true, exactShopEligibility, 2),
            Relic("L_SHARED_U1", "Uncommon", true, exactShopEligibility, 3),
            Relic("L_SHARED_R0", "Rare", true, exactShopEligibility, 4),
            Relic("L_SHARED_R1", "Rare", true, exactShopEligibility, 5),
            Relic("L_SHARED_S0", "Shop", true, exactShopEligibility, 6),
            Relic("L_SHARED_S_BLOCKED", "Shop", false, exactShopEligibility, 7),
            Relic("L_SHARED_S1", "Shop", true, exactShopEligibility, 8)
        };
        NeowEffectRelicSnapshot[] character =
        {
            Relic("L_CHARACTER_C0", "Common", true, exactShopEligibility, 0),
            Relic("L_CHARACTER_U0", "Uncommon", true, exactShopEligibility, 1),
            Relic("L_CHARACTER_R0", "Rare", true, exactShopEligibility, 2),
            Relic("L_CHARACTER_S0", "Shop", true, exactShopEligibility, 3)
        };
        var act = new WorldActSnapshot(
            1,
            new ModelKey("ACT", "SYNTH_ACT"),
            1,
            1,
            Array.Empty<ModelKey>(),
            Array.Empty<WorldEncounterSnapshot>(),
            Array.Empty<WorldEncounterSnapshot>(),
            Array.Empty<WorldEncounterSnapshot>(),
            new[] { new ModelKey("ENCOUNTER", "SYNTH_BOSS") },
            new[] { new ModelKey("ANCIENT", "SYNTH_ANCIENT") },
            GenerationInputsExact: true);
        return new WorldAuthoritySnapshot(
            RuntimeProfileId.Stable107,
            new[] { new WorldActGroupSnapshot(1, new[] { act }) },
            Array.Empty<ModelKey>(),
            Array.Empty<ModelKey>(),
            shared,
            character,
            Array.Empty<ModelKey>(),
            true,
            Array.Empty<ModelKey>(),
            true,
            true,
            true,
            SourceAuthority.AuditedStaticExact,
            SnapshotCompleteness.Complete,
            "legacy-relic-sequence-catalog-fixture",
            "legacy-relic-sequence-authority-fixture",
            DateTimeOffset.UnixEpoch,
            string.Empty);
    }

    public static WorldAuthoritySnapshot CreateModern(
        bool exactShopEligibility = true,
        int sharedCommonCount = 2,
        bool noUnknownHooksOrModifiers = true)
    {
        WorldAuthoritySnapshot source = Beta109WorldFixtureFactory.Create();
        Beta109WorldGenerationSnapshot modern = source.Beta109Generation!;
        Beta109RelicBucketSnapshot[] shared =
        {
            Bucket("shared:Common", Enumerable.Range(0, sharedCommonCount)
                .Select(index => Entry($"M_SHARED_C{index}", true, exactShopEligibility)).ToArray()),
            Bucket("shared:Uncommon", new[] { Entry("M_SHARED_U0", true, exactShopEligibility), Entry("M_SHARED_U1", true, exactShopEligibility) }),
            Bucket("shared:Rare", new[] { Entry("M_SHARED_R0", true, exactShopEligibility), Entry("M_SHARED_R1", true, exactShopEligibility) }),
            Bucket("shared:Shop", new[] { Entry("M_SHARED_S0", true, exactShopEligibility), Entry("M_SHARED_S_BLOCKED", false, exactShopEligibility) })
        };
        Beta109RelicBucketSnapshot[] player =
        {
            Bucket("player:Common", new[] { Entry("M_PLAYER_C0", true, exactShopEligibility), Entry("M_PLAYER_C1", true, exactShopEligibility), Entry("M_PLAYER_C2", true, exactShopEligibility), Entry("M_PLAYER_C3", true, exactShopEligibility) }),
            Bucket("player:Uncommon", new[] { Entry("M_PLAYER_U0", true, exactShopEligibility), Entry("M_PLAYER_U1", true, exactShopEligibility), Entry("M_PLAYER_U2", true, exactShopEligibility) }),
            Bucket("player:Rare", new[] { Entry("M_PLAYER_R0", true, exactShopEligibility), Entry("M_PLAYER_R1", true, exactShopEligibility), Entry("M_PLAYER_R2", true, exactShopEligibility) }),
            Bucket("player:Shop", new[] { Entry("M_PLAYER_S0", true, exactShopEligibility), Entry("M_PLAYER_S_BLOCKED", false, exactShopEligibility), Entry("M_PLAYER_S1", true, exactShopEligibility), Entry("M_PLAYER_S2", true, exactShopEligibility) })
        };
        Beta109WorldGenerationSnapshot projected = modern with
        {
            SharedRelicBuckets = shared,
            PlayerRelicBuckets = player,
            RelicShopEligibilityAuthorityExact = exactShopEligibility,
            NoUnknownHooksOrModifiers = noUnknownHooksOrModifiers,
            CatalogFingerprint = "modern-relic-sequence-catalog-fixture",
            SnapshotFingerprint = $"modern-relic-sequence-authority-fixture:{sharedCommonCount}:{exactShopEligibility}:{noUnknownHooksOrModifiers}"
        };
        return source with
        {
            CatalogFingerprint = projected.CatalogFingerprint,
            SnapshotFingerprint = projected.SnapshotFingerprint,
            Beta109Generation = projected
        };
    }

    private static NeowEffectRelicSnapshot Relic(
        string entry,
        string rarity,
        bool allowedInShops,
        bool eligibilityExact,
        int order) => new(
        new ModelKey("RELIC", entry),
        order,
        Enum.Parse<EffectRelicRarity>(rarity),
        NestedRelicEffectKind.NoTrackedImmediateEffect,
        RarityCode: rarity,
        IsAllowedInShops: allowedInShops,
        ShopEligibilityExact: eligibilityExact);

    private static Beta109RelicBucketEntrySnapshot Entry(
        string entry,
        bool allowedInShops,
        bool eligibilityExact) => new(
        new ModelKey("RELIC", entry),
        allowedInShops,
        eligibilityExact);

    private static Beta109RelicBucketSnapshot Bucket(
        string bucketId,
        IReadOnlyList<Beta109RelicBucketEntrySnapshot> entries) => new(
        bucketId,
        entries.Select(entry => entry.RelicKey).ToArray(),
        OrderExact: true)
    {
        OrderedEntries = entries
    };
}
