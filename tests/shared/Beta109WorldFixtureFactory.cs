using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

internal static class Beta109WorldFixtureFactory
{
    private static readonly ulong ModernRoot = Beta109Profile.Instance.ComputeRootSeed("VADSEV23455G");
    internal static WorldAuthoritySnapshot Create(
        bool missingPrefix = false,
        bool missingCatalog = false,
        bool unknownHook = false)
    {
        ModelKey act1 = Key(BaseGameModelKeys.Categories.Event, "SYNTH_ACT_1");
        ModelKey act2 = Key(BaseGameModelKeys.Categories.Event, "SYNTH_ACT_2");
        ModelKey act3 = Key(BaseGameModelKeys.Categories.Event, "SYNTH_ACT_3");
        ModelKey neow = Key(BaseGameModelKeys.Categories.Event, "NEOW");
        ModelKey darv = Key(BaseGameModelKeys.Categories.Event, "DARV");
        ModelKey orobas = Key(BaseGameModelKeys.Categories.Event, "OROBAS");
        ModelKey pael = Key(BaseGameModelKeys.Categories.Event, "PAEL");
        ModelKey tezcatara = Key(BaseGameModelKeys.Categories.Event, "TEZCATARA");
        ModelKey nonupeipe = Key(BaseGameModelKeys.Categories.Event, "NONUPEIPE");
        ModelKey tanx = Key(BaseGameModelKeys.Categories.Event, "TANX");
        ModelKey vakuu = Key(BaseGameModelKeys.Categories.Event, "VAKUU");
        string seed = "VADSEV23455G";
        ulong root = ModernRoot;
        ModelKey sharedVisibleEvent = Key(BaseGameModelKeys.Categories.Event, "SYNTH_SHARED_EVENT_VISIBLE");
        ModelKey sharedHiddenEvent = Key(BaseGameModelKeys.Categories.Event, "SYNTH_SHARED_EVENT_HIDDEN");
        ModelKey[] sharedRawEvents = { sharedVisibleEvent, sharedHiddenEvent };
        var eventAuthority = new Beta109EventCatalogAuthoritySnapshot(
            OrderedSharedEventsRaw: sharedRawEvents,
            SharedRawOrderExact: true,
            EpochsInFilterOrder: new[]
            {
                new Beta109EventEpochSnapshot(
                    "EVENT1_EPOCH",
                    new[] { sharedHiddenEvent },
                    MembershipExact: true,
                    IsRevealed: false,
                    RevealFactExact: true),
                new Beta109EventEpochSnapshot(
                    "EVENT2_EPOCH",
                    Array.Empty<ModelKey>(),
                    MembershipExact: true,
                    IsRevealed: true,
                    RevealFactExact: true),
                new Beta109EventEpochSnapshot(
                    "EVENT3_EPOCH",
                    Array.Empty<ModelKey>(),
                    MembershipExact: true,
                    IsRevealed: true,
                    RevealFactExact: true)
            },
            EpochFilterOrderExact: true,
            EvidenceCode: "SyntheticEventAuthority");

        Beta109ActGenerationSnapshot[] acts =
        {
            Act(1, act1, neow, "A1", sharedVisibleEvent),
            Act(2, act2, pael, "A2", sharedVisibleEvent),
            Act(3, act3, vakuu, "A3", sharedVisibleEvent, includeSecondBossCandidate: true)
        };
        Beta109AncientEventContextSnapshot[] contexts =
        {
            Context(1, neow, "NEOW", NeowCatalog(neow), unknownHook),
            Context(2, darv, "DARV", DarvCatalog(darv), unknownHook),
            Context(2, orobas, "OROBAS", OrobasCatalog(orobas), unknownHook),
            Context(2, pael, "PAEL", PaelCatalog(pael), unknownHook),
            Context(2, tezcatara, "TEZCATARA", TezcataraCatalog(tezcatara), unknownHook),
            Context(3, nonupeipe, "NONUPEIPE", NonupeipeCatalog(nonupeipe), unknownHook),
            Context(3, tanx, "TANX", TanxCatalog(tanx), unknownHook),
            Context(3, vakuu, "VAKUU", VakuuCatalog(vakuu), unknownHook)
        };
        if (missingCatalog)
        {
            contexts = contexts.Select(context => context with
            {
                Catalog = null,
                EventContextExact = false,
                ContextFingerprint = context.ContextFingerprint + ":catalog-missing"
            }).ToArray();
        }

        var modern = new Beta109WorldGenerationSnapshot(
            Profile: RuntimeProfileId.Beta109,
            OriginalSeed: seed,
            CanonicalSeed: seed,
            ActSelectionRoot: root,
            ActSelectionRootExact: true,
            RunSeedRoot: root,
            RunSeedRootExact: true,
            RunSeedHashKind: Beta109RunSeedHashKind.ModernXxHash64,
            OldSeedBranchStatus: Beta109OldSeedBranchStatus.NewSeedHashed,
            SelectedActs: new[] { act1, act2, act3 },
            SelectedActProvenance: SelectedActProvenance.SyntheticFixture,
            SelectedActsExact: true,
            ActSelectionGroups: new[]
            {
                new Beta109ActSelectionGroupSnapshot(
                    Act: 1,
                    EligibleActsInSourceOrder: new[] { act1 },
                    EligibilityAndOrderExact: true),
                new Beta109ActSelectionGroupSnapshot(
                    Act: 2,
                    EligibleActsInSourceOrder: new[] { act2 },
                    EligibilityAndOrderExact: true),
                new Beta109ActSelectionGroupSnapshot(
                    Act: 3,
                    EligibleActsInSourceOrder: new[] { act3 },
                    EligibilityAndOrderExact: true)
            },
            ActSelectionAuthorityExact: true,
            IsMultiplayer: false,
            IsMultiplayerExact: true,
            TestModeIsOff: true,
            TestModeFactExact: true,
            TutorialBossOverrideWillApply: false,
            TutorialBossOverrideAuthorityExact: true,
            Act1OverrideRaw: "random",
            Act1OverrideResolvedKey: null,
            Act1OverrideExact: true,
            LobbyPlayers: new[]
            {
                new Beta109LobbyPlayerSnapshot(
                    Slot: 0,
                    CharacterKey: BaseGameModelKeys.Characters.Defect,
                    IsRandomCharacter: false,
                    Exact: true)
            },
            AllCharactersInSourceOrder: BaseGameModelKeys.Characters.All,
            RandomCharacterAuthorityExact: true,
            UnlockedCharacters: BaseGameModelKeys.Characters.All,
            UnlockedCharactersExact: true,
            CharacterKey: BaseGameModelKeys.Characters.Defect,
            Ascension: 10,
            GameMode: WorldGameMode.Singleplayer,
            ModeFactsExact: true,
            PlayerCount: 1,
            OrderedActCatalog: acts,
            SharedEvents: sharedRawEvents,
            SharedEventCatalogExact: true,
            AllSharedAncients: Array.Empty<ModelKey>(),
            AllSharedAncientCatalogExact: true,
            SharedAncients: Array.Empty<ModelKey>(),
            SharedAncientCatalogExact: true,
            UnlockFactsExact: true,
            SharedRelicBuckets: new[]
            {
                new Beta109RelicBucketSnapshot(
                    BucketId: "shared:Common",
                    OrderedRelics: new[]
                    {
                        Key(BaseGameModelKeys.Categories.Relic, "SYNTH_SHARED_RELIC_0"),
                        Key(BaseGameModelKeys.Categories.Relic, "SYNTH_SHARED_RELIC_1")
                    },
                    OrderExact: true)
            },
            PlayerRelicBuckets: new[]
            {
                new Beta109RelicBucketSnapshot(
                    BucketId: "player:Common",
                    OrderedRelics: new[]
                    {
                        Key(BaseGameModelKeys.Categories.Relic, "SYNTH_SHARED_RELIC_0"),
                        Key(BaseGameModelKeys.Categories.Relic, "SYNTH_CHARACTER_RELIC_0"),
                        Key(BaseGameModelKeys.Categories.Relic, "SYNTH_CHARACTER_RELIC_1")
                    },
                    OrderExact: true)
            },
            RelicInitializationExact: true,
            UpFrontPrefix: missingPrefix
                ? Beta109UpFrontPrefixSnapshot.Missing("FixtureMissingPriorUpFront")
                : new Beta109UpFrontPrefixSnapshot(
                    Kind: Beta109UpFrontPrefixAuthorityKind.ReplayFromRunStart,
                    Origin: Beta109UpFrontReplayOrigin.RunStartBeforeRelicInitialization,
                    Checkpoint: null,
                    PriorInputsExact: true,
                    EvidenceCode: "SyntheticReplayFromRunStart"),
            AncientEventContexts: contexts,
            NoUnknownHooksOrModifiers: true,
            IsVanilla: true,
            DirectSourceAudited: true,
            FixtureValidationStatus: Beta109FixtureValidationStatus.SyntheticOnly,
            VerifiedRealGameFixtureIds: Array.Empty<string>(),
            CatalogFingerprint: "synthetic-beta109-world-catalog-v1",
            UnlockFingerprint: "synthetic-beta109-world-unlock-v1",
            GenerationRuleFingerprint: "beta109-ag011-ag012-direct-source-v1",
            SnapshotFingerprint: "synthetic-beta109-world-snapshot-v1:SyntheticOnly",
            CaptureDiagnosticCode: "SyntheticCatalogFixtureNotRealGameParity")
        {
            EventAuthority = eventAuthority,
            SharedRelicPoolOrderExact = true,
            CharacterRelicPoolOrderExact = true,
            RelicRarityAuthorityExact = true,
            PlayerRelicPoolCompositionExact = true,
            RelicAuthorityEvidenceCode = "SyntheticDonorFirstRelicAuthority"
        };

        return new WorldAuthoritySnapshot(
            CapturedProfileId: RuntimeProfileId.Beta109,
            ActGroups: null,
            SharedEvents: null,
            SharedAncients: null,
            SharedRelicPoolSource: null,
            CharacterRelicPoolSource: null,
            UnlockedCharacters: BaseGameModelKeys.Characters.All,
            UnlockedCharactersExact: true,
            AncientOptionRelicCatalog: null,
            AncientOptionRelicCatalogExact: false,
            CatalogOrderExact: false,
            RelicInitializationExact: false,
            SourceAuthority: SourceAuthority.AuditedStaticExact,
            Completeness: SnapshotCompleteness.Complete,
            CatalogFingerprint: modern.CatalogFingerprint,
            SnapshotFingerprint: modern.SnapshotFingerprint,
            CapturedAtUtc: DateTimeOffset.UnixEpoch,
            CaptureDiagnosticCode: modern.CaptureDiagnosticCode,
            Beta109Generation: modern);
    }

    private static Beta109ActGenerationSnapshot Act(
        int act,
        ModelKey actKey,
        ModelKey ancient,
        string prefix,
        ModelKey sharedVisibleEvent,
        bool includeSecondBossCandidate = false)
    {
        var encounters = new List<Beta109EncounterEntrySnapshot>
        {
            Encounter(prefix + "_WEAK", Beta109RoomType.Weak, true, 0, "weak"),
            Encounter(prefix + "_REGULAR", Beta109RoomType.Regular, false, 1, "regular"),
            Encounter(prefix + "_ELITE", Beta109RoomType.Elite, false, 2, "elite"),
            Encounter(prefix + "_BOSS_1", Beta109RoomType.Boss, false, 3, "boss")
        };
        if (includeSecondBossCandidate)
        {
            encounters.Add(Encounter(prefix + "_BOSS_2", Beta109RoomType.Boss, false, 4, "boss"));
        }
        return new Beta109ActGenerationSnapshot(
            Act: act,
            ActKey: actKey,
            WeakEncounterSlots: 1,
            TotalNormalRooms: 2,
            OrderedEvents: new[] { Key(BaseGameModelKeys.Categories.Event, prefix + "_EVENT_1") },
            OrderedGenerateAllEncounters: encounters,
            OrderedAncients: new[] { ancient },
            CatalogOrderExact: true,
            UnlockFilteringExact: true,
            RoomShapeExact: true,
            GenerateAllEncountersOrderExact: true,
            EncounterTagComparerExact: true,
            EncounterWeightModelExact: true,
            EncounterClassificationExact: true,
            EncounterTagIdentityExact: true,
            EncounterReferenceIdentityExact: true,
            EncounterRetryShapeExact: true,
            EliteEncounterSlots: 15,
            EncounterCatalogOfficialVanilla: true,
            EncounterAuthorityEvidenceCode: "SyntheticEncounterAuthority")
        {
            OrderedRawEvents = new[] { Key(BaseGameModelKeys.Categories.Event, prefix + "_EVENT_1") },
            OrderedEligibleEvents = new[]
            {
                Key(BaseGameModelKeys.Categories.Event, prefix + "_EVENT_1"),
                sharedVisibleEvent
            },
            RawEventCatalogOrderExact = true,
            EventEpochMembershipExact = true,
            EventEpochRevealFactsExact = true,
            EligibleEventOrderExact = true
        };
    }

    private static Beta109EncounterEntrySnapshot Encounter(
        string entry,
        Beta109RoomType type,
        bool weak,
        int sourceOrdinal,
        params string[] tags) => new(
        EncounterKey: Key(BaseGameModelKeys.Categories.Encounter, entry),
        RoomType: type,
        IsWeak: weak,
        Tags: tags,
        Weight: 1d,
        SharesTagsWith: Array.Empty<ModelKey>(),
        TagComparisonExact: true,
        SourceOrdinal: sourceOrdinal,
        ReferenceIdentityId: sourceOrdinal,
        SharesTagsWithSourceOrdinals: Array.Empty<int>(),
        TagIdentityExact: true,
        ClassificationExact: true,
        ReferenceIdentityExact: true,
        IsOfficialVanilla: true);

    private static Beta109AncientEventContextSnapshot Context(
        int act,
        ModelKey ancient,
        string eventId,
        Beta109AncientOptionCatalogSnapshot catalog,
        bool unknownHook) => new(
        Act: act,
        AncientKey: ancient,
        EventIdEntry: eventId,
        PlayerSlot: 0,
        IsShared: false,
        CurrentActIndex: act - 1,
        CharacterKey: BaseGameModelKeys.Characters.Defect,
        UnlockedCharacters: BaseGameModelKeys.Characters.All,
        DeckFacts: new Beta109DeckFactSnapshot(
            BasicStrikeCount: 4,
            RemovableCardCount: 10,
            GoopyEnchantableCount: 5,
            SwiftEnchantableCount: 5,
            InstinctEnchantableCount: 5,
            Exact: true),
        BooleanFacts: new Dictionary<string, bool>(),
        IntegerFacts: new Dictionary<string, int>(),
        HookDecision: unknownHook ? Beta109HookDecision.Unknown : Beta109HookDecision.Allow,
        EventContextExact: true,
        DynamicFactsExact: true,
        ModifierFactsExact: !unknownHook,
        Catalog: catalog,
        ContextFingerprint: "synthetic-event-context:" + eventId)
    {
        RuntimeTypeName = "Synthetic." + eventId,
        UnlockedCharacterSourceOrderExact = true,
        EventRngRoot = Beta109WorldRng.DeriveEventLocalSeed(
            ModernRoot,
            playerSlot: 0,
            isShared: false,
            eventEntry: eventId),
        EventRngRootExact = true,
        EventRngFormulaVersion = "EventModel.BeginEvent.0.109.1",
        ProjectionPolicy = "SyntheticExact",
        AuthorityEvidenceCode = "SyntheticAncientOptionContext"
    };

    private static Beta109AncientOptionCatalogSnapshot NeowCatalog(ModelKey ancient) => new(
        AncientKey: ancient,
        EventIdEntry: "NEOW",
        Capability: Beta109AncientOptionSupport.PureBranch,
        Pools: new[]
        {
            Pool("neow.curse", "SYNTH_CURSE_0", "SYNTH_CURSE_1"),
            Pool("neow.positive.base", "SYNTH_POS_0", "SYNTH_POS_1", "SYNTH_POS_2", "SYNTH_POS_3", "SYNTH_POS_4"),
            Pool("neow.pair.capsule", "LAVA_ROCK", "SMALL_CAPSULE"),
            Pool("neow.pair.oyster", "NUTRITIOUS_OYSTER", "STONE_HUMIDIFIER"),
            Pool("neow.pair.talisman", "NEOWS_TALISMAN", "POMANDER"),
            Pool("neow.allowed-positive",
                "SYNTH_POS_0", "SYNTH_POS_1", "SYNTH_POS_2", "SYNTH_POS_3", "SYNTH_POS_4",
                "LAVA_ROCK", "SMALL_CAPSULE", "NUTRITIOUS_OYSTER", "STONE_HUMIDIFIER",
                "NEOWS_TALISMAN", "POMANDER")
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-neow-option-catalog");

    private static Beta109AncientOptionCatalogSnapshot DarvCatalog(ModelKey ancient) => new(
        AncientKey: ancient,
        EventIdEntry: "DARV",
        Capability: Beta109AncientOptionSupport.PureInstanceProjection,
        Pools: new[]
        {
            Pool("darv.valid.astrolabe", "ASTROLABE") with { SourceOrdinal = 0 },
            Pool("darv.valid.black-star", "BLACK_STAR") with { SourceOrdinal = 1 },
            Pool("darv.valid.calling-bell", "CALLING_BELL") with { SourceOrdinal = 2 },
            Pool("darv.valid.empty-cage", "EMPTY_CAGE") with { SourceOrdinal = 3 },
            Pool("darv.valid.pandoras-box", "PANDORAS_BOX") with { SourceOrdinal = 4 },
            Pool("darv.valid.runic-pyramid", "RUNIC_PYRAMID") with { SourceOrdinal = 5 },
            Pool("darv.valid.snecko-eye", "SNECKO_EYE") with { SourceOrdinal = 6 },
            Pool("darv.valid.ectoplasm", "ECTOPLASM") with { SourceOrdinal = 7 },
            Pool("darv.valid.sozu", "SOZU") with { SourceOrdinal = 8 },
            Pool("darv.valid.philosophers-stone", "PHILOSOPHERS_STONE") with { SourceOrdinal = 9 },
            Pool("darv.valid.velvet-choker", "VELVET_CHOKER") with { SourceOrdinal = 10 },
            Pool("darv.dusty-tome", "DUSTY_TOME")
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-darv-option-catalog");

    private static Beta109AncientOptionCatalogSnapshot OrobasCatalog(ModelKey ancient) => new(
        AncientKey: ancient,
        EventIdEntry: "OROBAS",
        Capability: Beta109AncientOptionSupport.PureInstanceProjection,
        Pools: new[]
        {
            Pool("orobas.pool1.true", "ELECTRIC_SHRYMP", "GLASS_EYE", "PRISMATIC_GEM"),
            Pool("orobas.pool1.false", "ELECTRIC_SHRYMP", "GLASS_EYE", "SEA_GLASS"),
            Pool("orobas.pool2", "ALCHEMICAL_COFFER", "DRIFTWOOD", "RADIANT_PEARL", "SAND_CASTLE") with { SourceOrdinal = 2 },
            Pool("orobas.pool3.touch", "TOUCH_OF_OROBAS") with { SourceOrdinal = 3 },
            Pool("orobas.pool3.tooth", "ARCHAIC_TOOTH") with { SourceOrdinal = 4 }
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-orobas-option-catalog");

    private static Beta109AncientOptionCatalogSnapshot ShuffleCatalog(
        ModelKey ancient,
        string eventId,
        Beta109AncientOptionSupport capability,
        string poolId,
        string prefix) => new(
        AncientKey: ancient,
        EventIdEntry: eventId,
        Capability: capability,
        Pools: new[]
        {
            Pool(poolId,
                prefix + "_0", prefix + "_1", prefix + "_2", prefix + "_3", prefix + "_4",
                prefix + "_5", prefix + "_6", prefix + "_7", prefix + "_8", prefix + "_9")
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-" + eventId.ToLowerInvariant() + "-option-catalog");

    private static Beta109AncientOptionCatalogSnapshot ThreePoolCatalog(
        ModelKey ancient,
        string eventId,
        string prefix) => new(
        AncientKey: ancient,
        EventIdEntry: eventId,
        Capability: Beta109AncientOptionSupport.DeckFactPredicate,
        Pools: new[]
        {
            Pool(prefix + ".pool1", prefix.ToUpperInvariant() + "_OPTION_1A", prefix.ToUpperInvariant() + "_OPTION_1B"),
            Pool(prefix + ".pool2", prefix.ToUpperInvariant() + "_OPTION_2A", prefix.ToUpperInvariant() + "_OPTION_2B"),
            Pool(prefix + ".pool3", prefix.ToUpperInvariant() + "_OPTION_3A", prefix.ToUpperInvariant() + "_OPTION_3B")
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-" + prefix + "-option-catalog");



    private static Beta109AncientOptionCatalogSnapshot PaelCatalog(ModelKey ancient) => new(
        AncientKey: ancient,
        EventIdEntry: "PAEL",
        Capability: Beta109AncientOptionSupport.DeckFactPredicate,
        Pools: new[]
        {
            Pool("pael.pool1", "PAELS_FLESH", "PAELS_HORN", "PAELS_TEARS") with { SourceOrdinal = 0 },
            Pool("pael.pool2.base", "PAELS_WING") with { SourceOrdinal = 1 },
            Pool("pael.pool2.goopy", "PAELS_CLAW") with { SourceOrdinal = 2 },
            Pool("pael.pool2.removable", "PAELS_TOOTH") with { SourceOrdinal = 3 },
            Pool("pael.pool2.growth", "PAELS_GROWTH") with { SourceOrdinal = 4 },
            Pool("pael.pool3.base", "PAELS_EYE", "PAELS_BLOOD") with { SourceOrdinal = 5 },
            Pool("pael.pool3.no-event-pet", "PAELS_LEGION") with { SourceOrdinal = 6 }
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-pael-option-catalog");

    private static Beta109AncientOptionCatalogSnapshot TezcataraCatalog(ModelKey ancient) => new(
        AncientKey: ancient,
        EventIdEntry: "TEZCATARA",
        Capability: Beta109AncientOptionSupport.DeckFactPredicate,
        Pools: new[]
        {
            Pool("tezcatara.pool1.base", "TEZCATARA_OPTION_1A", "TEZCATARA_OPTION_1B") with { SourceOrdinal = 0 },
            Pool("tezcatara.pool1.basic-strike", "TEZCATARA_OPTION_1C") with { SourceOrdinal = 1 },
            Pool("tezcatara.pool2", "TEZCATARA_OPTION_2A", "TEZCATARA_OPTION_2B") with { SourceOrdinal = 2 },
            Pool("tezcatara.pool3", "TEZCATARA_OPTION_3A", "TEZCATARA_OPTION_3B") with { SourceOrdinal = 3 }
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-tezcatara-option-catalog");

    private static Beta109AncientOptionCatalogSnapshot NonupeipeCatalog(ModelKey ancient) => new(
        AncientKey: ancient,
        EventIdEntry: "NONUPEIPE",
        Capability: Beta109AncientOptionSupport.DeckFactPredicate,
        Pools: new[]
        {
            Pool("nonupeipe.pool.base",
                "NONUPEIPE_BASE_1", "NONUPEIPE_BASE_2", "NONUPEIPE_BASE_3",
                "NONUPEIPE_BASE_4", "NONUPEIPE_BASE_5", "NONUPEIPE_BASE_6",
                "NONUPEIPE_BASE_7", "NONUPEIPE_BASE_8", "NONUPEIPE_BASE_9") with { SourceOrdinal = 0 },
            Pool("nonupeipe.pool.swift", "BEAUTIFUL_BRACELET") with { SourceOrdinal = 1 }
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-nonupeipe-option-catalog");

    private static Beta109AncientOptionCatalogSnapshot TanxCatalog(ModelKey ancient) => new(
        AncientKey: ancient,
        EventIdEntry: "TANX",
        Capability: Beta109AncientOptionSupport.DeckFactPredicate,
        Pools: new[]
        {
            Pool("tanx.pool.base",
                "TANX_BASE_1", "TANX_BASE_2", "TANX_BASE_3",
                "TANX_BASE_4", "TANX_BASE_5", "TANX_BASE_6",
                "TANX_BASE_7", "TANX_BASE_8", "TANX_BASE_9") with { SourceOrdinal = 0 },
            Pool("tanx.pool.instinct", "TRI_BOOMERANG") with { SourceOrdinal = 1 }
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-tanx-option-catalog");

    private static Beta109AncientOptionCatalogSnapshot VakuuCatalog(ModelKey ancient) => new(
        AncientKey: ancient,
        EventIdEntry: "VAKUU",
        Capability: Beta109AncientOptionSupport.PureShuffleTake,
        Pools: new[]
        {
            Pool("vakuu.pool1", "VAKUU_1A", "VAKUU_1B", "VAKUU_1C"),
            Pool("vakuu.pool2", "VAKUU_2A", "VAKUU_2B", "VAKUU_2C"),
            Pool("vakuu.pool3", "VAKUU_3A", "VAKUU_3B", "VAKUU_3C", "VAKUU_3D")
        },
        VariantData: new Dictionary<string, string>(),
        CatalogExact: true,
        MutableInstanceProjectionExact: true,
        CatalogFingerprint: "synthetic-vakuu-option-catalog");

    private static Beta109NamedOptionPoolSnapshot Pool(string id, params string[] entries) => new(
        PoolId: id,
        OrderedOptions: entries.Select(entry => Key(BaseGameModelKeys.Categories.Relic, entry)).ToArray(),
        OrderExact: true,
        InstanceProjectionExact: true)
    {
        SourceOrdinal = InferPoolOrdinal(id),
        FilterResultExact = true
    };

    private static int InferPoolOrdinal(string id)
    {
        int separator = id.LastIndexOf(".", StringComparison.Ordinal);
        if (separator >= 0 && int.TryParse(id[(separator + 1)..], out int ordinal)) return ordinal;
        return id switch
        {
            "darv.dusty-tome" => 11,
            _ => 0
        };
    }

    private static ModelKey Key(string category, string entry) => new(category, entry);
}
