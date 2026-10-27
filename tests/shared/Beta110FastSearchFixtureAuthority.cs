using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Tests.FastSearch;

internal static class Beta110FastSearchFixtureAuthority
{
    public static RuntimeContextAuthoritySnapshot Create(ModelKey characterKey, int ascension, int playerSlot, int players)
        => Create(characterKey, ascension, playerSlot, players, RuntimeProfileId.Beta110, RuntimeVersionCompatibility.Beta110Version);

    public static RuntimeContextAuthoritySnapshot CreateBeta111(ModelKey characterKey, int ascension, int playerSlot, int players)
        => Create(characterKey, ascension, playerSlot, players, RuntimeProfileId.Beta111, RuntimeVersionCompatibility.Beta111Version);

    private static RuntimeContextAuthoritySnapshot Create(ModelKey characterKey, int ascension, int playerSlot, int players,
        RuntimeProfileId profileId, string gameVersion)
    {
        CharacterIdentity character = CharacterIdentity.FromKey(characterKey);
        NeowEffectAuthoritySnapshot effects = CreateEffectAuthority(characterKey) with
        {
            CapturedProfileId = profileId,
            CatalogFingerprint = profileId + "-benchmark-catalog",
            SnapshotFingerprint = profileId + "-benchmark-effect-authority"
        };
        if (profileId == RuntimeProfileId.Beta111)
        {
            // Controlled 0.111 vanilla-sized transform candidate fixture:
            // 87 synthetic non-Basic/non-Ancient character-pool cards.
            NeowEffectCardSnapshot[] extra = Enumerable.Range(19, 68).Select(index => Card(
                $"beta111-transform-{index}",
                new ModelKey(BaseGameModelKeys.Categories.Card, $"BETA111_TRANSFORM_{index}"),
                index, EffectCardRarity.Common, EffectCardType.Attack)).ToArray();
            effects = effects with { TransformPool = effects.TransformPool!.Concat(extra).ToArray() };
        }
        bool vanillaCharacter = BaseGameModelKeys.Characters.All.Contains(characterKey);
        return new RuntimeContextAuthoritySnapshot(
            character,
            ascension,
            playerSlot,
            players,
            noRunModifiers: true,
            vanillaNeowCatalogExact: true,
            allCharacterCardPoolsUnlocked: true,
            unlockedCommonCards: 16,
            unlockedUncommonCards: 16,
            gameVersion: gameVersion,
            profileId: profileId,
            isVanilla: vanillaCharacter,
            sourceModId: vanillaCharacter ? null : "mod.test.fastsearch",
            sourceAssembly: "Beta110FastSearchFixtureAuthority",
            sourceAuthority: vanillaCharacter
                ? SourceAuthority.AuditedStaticExact
                : SourceAuthority.ModdedRuntimeBestEffort,
            completeness: SnapshotCompleteness.Complete,
            resolutionStatus: IdentityResolutionStatus.Exact,
            effectAuthority: effects,
            worldAuthority: null,
            predictionGameMode: players == 1 ? WorldGameMode.Singleplayer : WorldGameMode.Multiplayer,
            predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
    }


    public static RuntimeContextAuthoritySnapshot CreateWithDivergentNewLeafDefendPool(
        ModelKey characterKey,
        int ascension,
        int playerSlot,
        int players)
    {
        RuntimeContextAuthoritySnapshot baseline = Create(characterKey, ascension, playerSlot, players);
        NeowEffectAuthoritySnapshot effects = baseline.EffectAuthority
            ?? throw new InvalidOperationException("Fixture effect authority is missing.");
        NeowEffectCardSnapshot[] deck = effects.OrderedDeck!
            .Select(card => card.IsDefend ? card with { PoolId = "defend-only" } : card)
            .ToArray();
        return baseline.WithEffectAuthority(effects with
        {
            OrderedDeck = deck,
            DeckFingerprint = effects.DeckFingerprint + ":new-leaf-divergent-defend-pool",
            SnapshotFingerprint = effects.SnapshotFingerprint + ":new-leaf-divergent-defend-pool"
        });
    }


    /// <summary>
    /// Recreates the pre-hotfix synthetic authority that exposed only two Rare
    /// character cards. Hefty Tablet offers three distinct Rare cards, so the
    /// production Fast compiler must conservatively place that condition in
    /// NoFastProjection for this deliberately insufficient fixture.
    /// </summary>
    public static RuntimeContextAuthoritySnapshot CreateWithInsufficientHeftyRarePool(
        ModelKey characterKey,
        int ascension,
        int playerSlot,
        int players)
    {
        RuntimeContextAuthoritySnapshot baseline = Create(characterKey, ascension, playerSlot, players);
        NeowEffectAuthoritySnapshot effects = baseline.EffectAuthority
            ?? throw new InvalidOperationException("Fixture effect authority is missing.");
        ModelKey thirdRare = new(BaseGameModelKeys.Categories.Card, "BENCH_CHARACTER_18");
        NeowEffectCardSnapshot[] rewardPool = effects.CharacterRewardPool!
            .Where(card => card.CardKey != thirdRare)
            .ToArray();
        return baseline.WithEffectAuthority(effects with
        {
            CharacterRewardPool = rewardPool,
            CatalogFingerprint = effects.CatalogFingerprint + ":hefty-rare-count-2",
            SnapshotFingerprint = effects.SnapshotFingerprint + ":hefty-rare-count-2"
        });
    }

    private static NeowEffectAuthoritySnapshot CreateEffectAuthority(ModelKey characterKey)
    {
        ModelKey strike = new(BaseGameModelKeys.Categories.Card, "STRIKE");
        ModelKey defend = new(BaseGameModelKeys.Categories.Card, "DEFEND");
        var deck = new[]
        {
            Card("strike-0", strike, 0, EffectCardRarity.Basic, EffectCardType.Attack, basic: true, strike: true),
            Card("strike-1", strike, 1, EffectCardRarity.Basic, EffectCardType.Attack, basic: true, strike: true),
            Card("strike-2", strike, 2, EffectCardRarity.Basic, EffectCardType.Attack, basic: true, strike: true),
            Card("strike-3", strike, 3, EffectCardRarity.Basic, EffectCardType.Attack, basic: true, strike: true),
            Card("defend-0", defend, 4, EffectCardRarity.Basic, EffectCardType.Skill, basic: true, defend: true),
            Card("defend-1", defend, 5, EffectCardRarity.Basic, EffectCardType.Skill, basic: true, defend: true),
            Card("defend-2", defend, 6, EffectCardRarity.Basic, EffectCardType.Skill, basic: true, defend: true),
            Card("defend-3", defend, 7, EffectCardRarity.Basic, EffectCardType.Skill, basic: true, defend: true),
            Card("starter-special", new ModelKey(BaseGameModelKeys.Categories.Card, "STARTER_SPECIAL"), 8,
                EffectCardRarity.Basic, EffectCardType.Skill, basic: true)
        };

        var characterPool = Enumerable.Range(0, 19)
            .Select(index => Card(
                $"character-{index}",
                new ModelKey(BaseGameModelKeys.Categories.Card, $"BENCH_CHARACTER_{index}"),
                index,
                index < 10 ? EffectCardRarity.Common : index < 16 ? EffectCardRarity.Uncommon : EffectCardRarity.Rare,
                index % 3 == 0 ? EffectCardType.Attack : index % 3 == 1 ? EffectCardType.Skill : EffectCardType.Power))
            .ToArray();
        var colorlessPool = Enumerable.Range(0, 9)
            .Select(index => Card(
                $"colorless-{index}",
                new ModelKey(BaseGameModelKeys.Categories.Card, $"BENCH_COLORLESS_{index}"),
                index,
                index < 5 ? EffectCardRarity.Common : index < 8 ? EffectCardRarity.Uncommon : EffectCardRarity.Rare,
                EffectCardType.Skill,
                poolId: "colorless"))
            .ToArray();

        ModelKey[] otherCharacters = BaseGameModelKeys.Characters.All
            .Where(key => key != characterKey)
            .ToArray();
        CharacterCardPoolSnapshot[] otherPools = otherCharacters
            .Select((key, poolIndex) => new CharacterCardPoolSnapshot(
                key,
                $"other-{poolIndex}",
                poolIndex,
                Enumerable.Range(0, 6)
                    .Select(index => Card(
                        $"other-{poolIndex}-{index}",
                        new ModelKey(BaseGameModelKeys.Categories.Card, $"BENCH_OTHER_{poolIndex}_{index}"),
                        index,
                        index < 3 ? EffectCardRarity.Common : index < 5 ? EffectCardRarity.Uncommon : EffectCardRarity.Rare,
                        EffectCardType.Attack,
                        poolId: $"other-{poolIndex}"))
                    .ToArray()))
            .ToArray();

        NeowEffectCardSnapshot[] transformPool = characterPool
            .Concat(colorlessPool)
            .ToArray();
        NeowEffectPotionSnapshot[] potions =
        {
            new(new ModelKey(BaseGameModelKeys.Categories.Potion, "BENCH_COMMON_POTION"), 0, EffectPotionRarity.Common),
            new(new ModelKey(BaseGameModelKeys.Categories.Potion, "BENCH_UNCOMMON_POTION"), 1, EffectPotionRarity.Uncommon),
            new(new ModelKey(BaseGameModelKeys.Categories.Potion, "BENCH_RARE_POTION"), 2, EffectPotionRarity.Rare)
        };
        NeowEffectRelicSnapshot[] relics =
        {
            new(BaseGameModelKeys.OrdinaryRelics.Whetstone, 0, EffectRelicRarity.Common, NestedRelicEffectKind.Whetstone),
            new(BaseGameModelKeys.OrdinaryRelics.WarPaint, 1, EffectRelicRarity.Common, NestedRelicEffectKind.WarPaint),
            new(new ModelKey(BaseGameModelKeys.Categories.Relic, "BENCH_UNCOMMON_RELIC"), 2, EffectRelicRarity.Uncommon,
                NestedRelicEffectKind.NoTrackedImmediateEffect),
            new(new ModelKey(BaseGameModelKeys.Categories.Relic, "BENCH_RARE_RELIC"), 3, EffectRelicRarity.Rare,
                NestedRelicEffectKind.NoTrackedImmediateEffect)
        };

        return new NeowEffectAuthoritySnapshot(
            OrderedDeck: deck,
            CharacterRewardPool: characterPool,
            ColorlessRewardPool: colorlessPool,
            OtherCharacterPools: otherPools,
            TransformPool: transformPool,
            PotionPool: potions,
            OrderedRelicBag: relics,
            GeneratedCursePool: new[] { BaseGameModelKeys.Cards.Greed, BaseGameModelKeys.Cards.Guilty },
            BonesEligibleRelics: BaseGameModelKeys.Relics.AllNeow,
            CharacterStrikeKey: strike,
            CharacterDefendKey: defend,
            ClawKey: BaseGameModelKeys.Cards.Claw,
            CardRewardPoolsExact: true,
            DeckExact: true,
            TransformPoolsExact: true,
            PotionPoolExact: true,
            RelicBagExact: true,
            CursePoolExact: true,
            BonesEligibilityExact: true,
            CardRewardHooksNoOpExact: true,
            PotionProcurementHooksNoOpExact: true,
            NestedRelicHooksExact: true,
            CurrentGold: 99,
            CurrentPotionCount: 0,
            PotionCapacity: 2,
            BranchBudget: 64,
            SourceAuthority: SourceAuthority.AuditedStaticExact,
            Completeness: SnapshotCompleteness.Complete,
            AuthoritySource: NeowEffectAuthoritySource.FixtureAuthority,
            CapturedProfileId: RuntimeProfileId.Beta110,
            CatalogFingerprint: "beta110-benchmark-catalog-v2-hefty-rare3",
            UnlockFingerprint: "beta110-benchmark-unlocks-v1",
            DeckFingerprint: "beta110-benchmark-deck-v1",
            RelicBagFingerprint: "beta110-benchmark-relics-v1",
            PotionPoolFingerprint: "beta110-benchmark-potions-v1",
            SnapshotFingerprint: "beta110-benchmark-effect-authority-v2-hefty-rare3",
            CapturedAtUtc: DateTimeOffset.UnixEpoch,
            WarningCodes: Array.Empty<PredictionWarningCode>(),
            CaptureDiagnosticCode: "SyntheticCompleteAuthorityForCursedPearlBenchmark",
            CharacterRewardPoolExact: true,
            ColorlessRewardPoolExact: true,
            OtherCharacterPoolsExact: true,
            CharacterRewardHooksNoOpExact: true,
            ColorlessRewardHooksNoOpExact: true,
            OtherCharacterRewardHooksNoOpExact: true,
            SharedRelicPoolSource: relics,
            CharacterRelicPoolSource: Array.Empty<NeowEffectRelicSnapshot>(),
            RelicBagSourcePoolsExact: true);
    }

    private static NeowEffectCardSnapshot Card(
        string instanceId,
        ModelKey key,
        int order,
        EffectCardRarity rarity,
        EffectCardType type,
        bool basic = false,
        bool strike = false,
        bool defend = false,
        string poolId = "character") => new(
        instanceId,
        key,
        order,
        rarity,
        type,
        IsBasic: basic,
        IsStrike: strike,
        IsDefend: defend,
        CanUpgrade: true,
        CanRemove: true,
        UpgradeLevel: 0,
        MaxUpgradeLevel: 1,
        UpgradeTargetKey: new ModelKey(key.Category, key.Entry + "_UPGRADED"),
        PoolId: poolId,
        CatalogProfileId: RuntimeProfileId.Beta110);
}
