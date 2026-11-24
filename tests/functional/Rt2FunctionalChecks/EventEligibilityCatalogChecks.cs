using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Ui.Pages.Search.Event;

internal static class EventEligibilityCatalogChecks
{
    private static readonly ModelKey Museum = Event("MUSEUM_OF_TORTURE");
    private static readonly ModelKey Merchant = Event("FAKE_MERCHANT");
    private static readonly ModelKey Historian = Event("WAR_HISTORIAN_REPY");
    private static readonly ModelKey Colorful = Event("COLORFUL_PHILOSOPHERS");
    private static readonly ModelKey Baths = Event("THERMAE_NONUPEIPE");
    private static readonly ModelKey Shrine = Event("SHRINE_OF_THE_WARLORD");
    private static readonly ModelKey Wuppy = Event("WUPPY_IN_TROUBLE");
    private static readonly ModelKey Statue = Event("SUNKEN_STATUE");
    private static readonly ModelKey Custom = Event("MUSEUMOFTORTURE");

    internal static int Run()
    {
        int checks = 0;
        void Check(bool result, string reason)
        {
            checks++;
            if (!result) throw new InvalidOperationException("EventEligibilityCatalogChecks: " + reason);
        }

        var world = CreateWorld();
        var modern = world.Beta109Generation!;
        var solo = EventSequenceSearchUiCatalog.FromAuthority(RuntimeProfileId.Beta111, world);
        Check(solo.Find(1, Museum) is null && solo.Find(2, Museum) is not null && solo.Find(3, Museum) is null,
            "Modern113 museum shares the raw pool but is selectable only in Act 2");
        Check(solo.KnownAllowedActs(Museum).SequenceEqual([2]) && solo.IsAudited(Museum),
            "Museum's Act restriction remains available to a saved condition in an ineligible Act");
        Check(solo.KnownAllowedActs(Baths).SequenceEqual([3]) && solo.Find(3, Baths) is not null && solo.Find(3, Shrine) is not null,
            "Act 3 Ancient and future player conditions do not preclude the baths or shrine");
        Check(solo.Find(1, Wuppy) is not null && solo.Find(2, Merchant) is not null && solo.Find(2, Colorful) is not null,
            "Events with pet, gold, potion or satisfied card-pool prerequisites remain in the list");
        Check(solo.Find(3, Historian) is null && solo.IsAudited(Historian) && solo.KnownAllowedActs(Historian).Count == 0,
            "Lantern Key replacement is recognized without being a natural candidate");
        Check(solo.Find(1, Statue)?.VariantActKeys.SequenceEqual([Act("OVERGROWTH"), Act("UNDERDOCKS")]) == true,
            "A common Act 1 event uses membership in both captured variants");
        Check(solo.Find(3, Custom) is not null && !solo.IsAudited(Custom) && solo.KnownAllowedActs(Custom).Count == 0,
            "An unrecognized exact model identity remains selectable without inheriting a vanilla rule");

        var locked = Catalog(modern with { EventAuthority = modern.EventAuthority with { NonFoolCharacterCardPoolCount = 1 } });
        Check(locked.Find(2, Colorful) is null && locked.KnownAllowedActs(Colorful).SequenceEqual([2]),
            "Exact solo unlock facts use the non-Fool pool count in Modern113 and preserve Act facts");
        var unread = Catalog(modern with { EventAuthority = modern.EventAuthority with { NonFoolCharacterCardPoolCountExact = false } });
        Check(unread.Find(2, Colorful) is not null, "An unread unlock count does not rule out a future event");

        var party = Catalog(modern with { GameMode = WorldGameMode.Multiplayer, PlayerCount = 2, IsMultiplayer = true });
        Check(party.Find(2, Merchant) is null && party.Find(3, Merchant) is null && party.Find(2, Colorful) is not null,
            "A known party excludes the single-player merchant without treating one unlock count as the complete party");
        var old = Catalog(modern with { RngSemantics = RngSemanticProfileId.Modern110 });
        Check(old.Find(3, Museum) is not null && !old.IsAudited(Museum),
            "Older semantic profiles do not attribute unaudited Modern113 event rules to their catalog");

        var trash = Event("TRASH_HEAP");
        var epochFacts = modern.EventAuthority with { SharedRawOrderExact = false,
            EpochsInFilterOrder = [new("Event1Epoch", [trash], true, false, true)] };
        SearchQueryEvaluation Evaluate(ModelKeySetFilter keys, bool exactReveal = true)
        {
            var epochs = epochFacts with { EpochsInFilterOrder = [epochFacts.EpochsInFilterOrder[0] with { RevealFactExact = exactReveal }] };
            var variant = world with { EventAuthority = epochs, Beta109Generation = modern with
                { EventAuthority = epochs, SharedEventCatalogExact = false, WorldGenerationHooksExact = true } };
            var hero = BaseGameModelKeys.Characters.Ironclad;
            var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(hero, 10, 0, 1, "0.113.1").WithWorldAuthority(variant);
            var compiled = SearchCompiler.Compile(SearchQuery.Empty with { EventSequenceConstraints =
                [new(1, null, SearchSequenceRangeMode.FirstN, 1, keys)] }, new(RuntimeProfileId.Beta111, hero, 10,
                    authority, new("0.113.1", "0.113.1", "beta", "epoch", true, ""), SearchEvaluationAssumptions.BroadDefault));
            var model = new SearchModel(compiled, CompiledSearchEvaluationProjector.Project(compiled).Evaluation,
                CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint);
            return ProductionExactSearchEvaluator.Evaluate(model, TrustedRootHashInput.RootOnly(0)).Evaluation;
        }
        Check(Evaluate(new([trash], [], [])).Disposition == SearchDisposition.NoMatch, "unrevealed Epoch rules out required identity even with unknown queue");
        Check(Evaluate(new([], [trash, Statue], [])).Disposition == SearchDisposition.NoMatch, "one known-absent ALL identity disproves the row");
        Check(Evaluate(new([trash, Statue], [], [])).Disposition == SearchDisposition.Unknown, "another possible ANY identity remains unverified");
        Check(Evaluate(new([trash], [], []), exactReveal: false).Disposition == SearchDisposition.Unknown, "unread Epoch preserves the candidate");
        Console.WriteLine($"EventEligibilityCatalogChecks passed: {checks}");
        return 0;

        EventSequenceSearchUiCatalog Catalog(Beta109WorldGenerationSnapshot generation) =>
            EventSequenceSearchUiCatalog.FromAuthority(RuntimeProfileId.Beta111, world with { Beta109Generation = generation });
    }

    private static WorldAuthoritySnapshot CreateWorld()
    {
        var world = Beta109WorldFixtureFactory.Create();
        var generation = world.Beta109Generation!;
        ModelKey[] shared = [Museum, Merchant, Historian];
        var eventAuthority = generation.EventAuthority with
        {
            OrderedSharedEventsRaw = shared,
            CharacterCardPoolCount = 3,
            CharacterCardPoolCountExact = true,
            NonFoolCharacterCardPoolCount = 2,
            NonFoolCharacterCardPoolCountExact = true
        };
        var act1 = generation.OrderedActCatalog[0];
        Beta109ActGenerationSnapshot[] acts =
        [
            WithEvents(act1, "OVERGROWTH", Statue),
            WithEvents(act1, "UNDERDOCKS", Statue, Wuppy),
            WithEvents(generation.OrderedActCatalog[1], "HIVE", Colorful),
            WithEvents(generation.OrderedActCatalog[2], "GLORY", Baths, Shrine, Custom)
        ];
        return world with
        {
            CapturedProfileId = RuntimeProfileId.Beta111,
            SharedEvents = shared,
            EventAuthority = eventAuthority,
            Beta109Generation = generation with
            {
                Profile = RuntimeProfileId.Beta111,
                RngSemantics = RngSemanticProfileId.Modern113,
                GameMode = WorldGameMode.Singleplayer,
                ModeFactsExact = true,
                IsMultiplayer = false,
                IsMultiplayerExact = true,
                PlayerCount = 1,
                OrderedActCatalog = acts,
                EventAuthority = eventAuthority
            }
        };

        Beta109ActGenerationSnapshot WithEvents(Beta109ActGenerationSnapshot act, string variant, params ModelKey[] local) =>
            act with { ActKey = Act(variant), OrderedRawEvents = local, OrderedEligibleEvents = [.. local, .. shared] };
    }

    private static ModelKey Event(string entry) => new(BaseGameModelKeys.Categories.Event, entry);
    private static ModelKey Act(string entry) => new(BaseGameModelKeys.Categories.Act, entry);
}
