using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

internal static class VariantOpeningIntegrationChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        { checks++; if (!condition) throw new InvalidDataException("VariantOpeningIntegration: " + name); }
        void Near(double? actual, double expected, string name) => Check(actual is { } value &&
            Math.Abs(value - expected) < 1e-12, name + ";actual=" + actual + ";expected=" + expected);
        // Reuse the captured-context-based, explicitly synthetic closed catalog
        // used by preparation checks. Admission and registration remain real.
        var context = VariantOpeningPreparationChecks.CreateContext();
        ModelKey a = VariantOpeningPreparationChecks.A, x = VariantOpeningPreparationChecks.X,
            y = VariantOpeningPreparationChecks.Y;
        SearchRunOptions Options(IReadOnlyList<string>? order = null) =>
            new("000000000000", 1024, 1, 2, ManualFamilyOrder: order);
        PreparedSearch Request(SearchQuery query, SearchContext? selected = null,
            IReadOnlyList<string>? order = null)
        {
            var result = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, selected ?? context), Options(order));
            Check(result.Success && result.Plan is not null, "factory admission: " + result.Issue);
            Check(result.Plan!.Model.VariantOpening is not null, "factory attaches request-owned V preparation");
            return result.Plan;
        }
        static FilterBodyDescription Owner(FilterBodyDescription physical) => physical;
        VariantOpeningPreparedBody Variant(FamilyExecutionPlan plan, PreparedSearch request)
        {
            _ = plan.ExecutionOrderUnits.Single(family => family.FamilyId == VariantOpeningFamily.Id);
            if (!VariantOpeningFamily.TryPrepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var prepared, false) || prepared is null)
                throw new InvalidDataException("VariantOpeningIntegration.MissingSelectedPreparation");
            return prepared;
        }
        void OnlyV(PreparedSearch request, string name)
        {
            var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false);
            Check(registered.Select(family => family.FamilyId).SequenceEqual([VariantOpeningFamily.Id]), name + ":only V registered");
            var selected = FamilyExecutionCoordinator.Plan(request, false);
            Check(selected.ExecutionOrderUnits.Select(family => family.FamilyId).SequenceEqual([VariantOpeningFamily.Id]), name + ":only V selected");
            Check(selected.FilterRemainders.All(value => value ==
                "Party:OnlySelectedBoundedFiltersParticipate;SlotAndSharedRemaindersRetainedByQuery"),
                name + ":no invented filtering remainder: " + string.Join(';', selected.FilterRemainders));
        }
        var explicitVariant = SearchQuery.Empty with { VariantBossBranches = [new(1, a,
            ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] };
        var pure = Request(explicitVariant);
        OnlyV(pure, "pure variant");
        Near(FamilyExecutionCoordinator.Plan(pure, false).Stages.Single().Survival, 1d / 3, "pure variant actual s");
        var unknown = Request(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 });
        OnlyV(unknown, "unknown prefix only");
        Near(FamilyExecutionCoordinator.Plan(unknown, false).Stages.Single().Survival,
            FirstActUnknownEventProbability.ConditionalPrefix(3, false), "ordinary prefix actual s once");

        EventSequenceSearchCondition Event(ModelKey key) => new(1, null, SearchSequenceRangeMode.FirstN, 1, new([key], [], []));
        var juzu = new ModelKey("RELIC", "JUZU_BRACELET");
        var prefix = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 };
        double eventMass = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(Request(
            SearchQuery.Empty with { EventSequenceConstraints = [Event(x)] }).Model)).Probability
            ?? throw new InvalidDataException("VariantOpeningIntegration.EventReferenceMassMissing");
        var declaredCases = new[] {
            (Name: "GoldenPearl", Query: prefix with { OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl) }, Juzu: false),
            (Name: "UnspecifiedBones", Query: prefix with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones) }, Juzu: false),
            (Name: "UnspecifiedSmallCapsule", Query: prefix with { OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule) }, Juzu: false),
            (Name: "UnspecifiedLargeCapsule", Query: prefix with { OpeningRoute = new(BaseGameModelKeys.Relics.LargeCapsule) }, Juzu: false),
            (Name: "DirectJuzu", Query: prefix with { OpeningRoute = new(juzu) }, Juzu: true),
            (Name: "NestedJuzu", Query: prefix with { OpeningRoute = new(BaseGameModelKeys.Relics.LargeCapsule),
                LegacyNeow = prefix.LegacyNeow with { CapsuleContainedRelics = new([], [juzu], []) } }, Juzu: true),
            (Name: "AlternativeJuzu", Query: prefix with { OpeningRoute = new(BaseGameModelKeys.Relics.LargeCapsule),
                LegacyNeow = prefix.LegacyNeow with { CapsuleContainedRelics = new([juzu, BaseGameModelKeys.Relics.GoldenPearl], [], []) } }, Juzu: false)
        };
        foreach (var fixture in declaredCases)
        foreach (bool eventRequested in new[] { false, true })
        {
            var query = fixture.Query with { EventSequenceConstraints = eventRequested ? [Event(x)] : [] };
            var request = Request(query);
            var plan = FamilyExecutionCoordinator.Plan(request, false);
            var v = Variant(plan, request);
            Check(v.Plan.UnknownPrefixCount == 3 && v.Plan.UnknownJuzuAcquired == fixture.Juzu,
                fixture.Name + ":declared prefix is physically checked with or without W");
            Check(v.Plan.CoversVariants == eventRequested, fixture.Name + ":event-derived variant check is independent");
            Check(!plan.FilterRemainders.Any(value => value.StartsWith("FirstActUnknownEventPrefix:", StringComparison.Ordinal) ||
                value == VariantOpeningFamily.Id + ":NoSelectedFilter"), fixture.Name + ":actual prefix coverage has no missing-family warning");
            double p = FirstActUnknownEventProbability.ConditionalPrefix(3, fixture.Juzu);
            Near(FirstActUnknownEventProbability.Estimate(SearchSelectivityInput.From(request.Model)).Probability, p,
                fixture.Name + ":q component uses the declared premise once");
            Near(v.Body.Survival.SurvivalProbability, p * (eventRequested ? 1d / 3 : 1),
                fixture.Name + ":actual V s uses the same declared premise");
            if (eventRequested)
            foreach (var order in new[] { new[] { VariantOpeningFamily.Id, "W.World" }, new[] { "W.World", VariantOpeningFamily.Id } })
            {
                var manual = FamilyExecutionCoordinator.Plan(Request(query, order: order), false);
                var seen = new HashSet<string>();
                double product = 1;
                foreach (var stage in manual.Stages.Where(stage => stage.Family.FamilyId is "V.VariantOpening" or "W.World"))
                {
                    double? s = stage.Family.ResolveSurvival(seen).SurvivalProbability;
                    Check(s.HasValue, fixture.Name + ":manual V/W survival known");
                    product *= s!.Value; seen.UnionWith(stage.Coverage);
                }
                Near(product, p * eventMass, fixture.Name + ":manual order pays prefix and event once");
            }
        }
        var eventOnly = SearchQuery.Empty with { EventSequenceConstraints = [Event(x)] };
        var firstAct = context.Authority.WorldAuthority!.Beta109Generation!.OrderedActCatalog.First(act => act.ActKey == a);
        ModelKey boss = firstAct.Bosses[0];
        var bossAndEvent = explicitVariant with {
            VariantBossBranches = [new(1, a, new([boss], [], []), ModelKeySetFilter.Empty, false)],
            EventSequenceConstraints = [Event(y)] };
        foreach (var fixture in new[] { (Name: "implicit event", Query: eventOnly), (Name: "parent-scoped boss and event", Query: bossAndEvent) })
        {
            double? originalQ = null;
            var terminalRates = new List<double>();
            foreach (string[] order in new[] { new[] { VariantOpeningFamily.Id, "W.World" }, new[] { "W.World", VariantOpeningFamily.Id } })
            {
                var request = Request(fixture.Query, order: order);
                string originalQuery = JsonSerializer.Serialize(request.Model.CompiledSearch.Query);
                string normalized = JsonSerializer.Serialize(request.Model.CompiledSearch.NormalizedQuery);
                double? q = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request.Model)).Probability;
                Check(q is > 0 and < 1, fixture.Name + ":original q known");
                originalQ ??= q;
                Near(q, originalQ ?? double.NaN, fixture.Name + ":order-independent original q");
                var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false);
                Check(registered.Select(family => family.FamilyId).ToHashSet().SetEquals([VariantOpeningFamily.Id, "W.World"]),
                    fixture.Name + ":actual V/W registration");
                var selected = FamilyExecutionCoordinator.Plan(request, false);
                Check(selected.ExecutionOrderUnits.Select(family => family.FamilyId).SequenceEqual(order),
                    fixture.Name + ":explicit order actually selected");
                Check(selected.FilterRemainders.Count == 0, fixture.Name + ":no lost filters: " + string.Join(';', selected.FilterRemainders));
                double product = 1;
                var passed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var stage in FamilySearchEtaProjectorV1.EstimationStages(selected))
                {
                    Check(!stage.Family.UsesGpu && stage.Family.CpuWorkers == request.WorkerCount && !stage.ConditionPerformance.UsesGpu,
                        fixture.Name + ":configured CPU substitute selected");
                    double? actual = stage.Family.ResolveSurvival(passed).SurvivalProbability;
                    Near(stage.Survival, actual ?? double.NaN, fixture.Name + ":planner uses actual passed-coverage projection");
                    Near(actual, Owner(stage.Family).ResolveSurvival(passed).SurvivalProbability ?? double.NaN,
                        fixture.Name + ":CPU substitute forwards owner conditional s");
                    product *= actual!.Value;
                    passed.UnionWith(stage.Coverage);
                }
                terminalRates.Add(product);
                Near(product, q!.Value, fixture.Name + ":physical complete sieve charges original variant mass once");
                Near(JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request.Model)).Probability, q.Value,
                    fixture.Name + ":planning leaves whole-query q unchanged");
                Check(originalQuery == JsonSerializer.Serialize(request.Model.CompiledSearch.Query) &&
                    normalized == JsonSerializer.Serialize(request.Model.CompiledSearch.NormalizedQuery), fixture.Name + ":planning preserves original and normalized intent");
                var v = Variant(selected, request);
                Check(v.Plan.CoversVariants && v.Plan.UnknownPrefixCount == 0, fixture.Name + ":event FirstN does not add unknown-room checks");
                _ = selected.ExecutionOrderUnits.Single(family => family.FamilyId == "W.World");
                var w = new WorldFamilyReplay(request.Model);
                Check(w.Plan.Acts.All(act => act.FamilyVariantAllowed), fixture.Name + ":W no longer repeats migrated variant filtering");
                if (fixture.Name == "parent-scoped boss and event")
                {
                    Check(w.Plan.Acts.Single(act => act.Act == 1 && act.FamilyBossBranches.Length > 0).FamilyBossBranches.Length == 1,
                        "boss predicate remains scoped to its actual variant");
                    Check(w.Plan.Acts.Count(act => act.Act == 1 && act.FamilyBossBranches.Length == 0) == 2,
                        "W boss rows are vacuous outside authored variants until V filters them");
                    if (order[0] == "W.World")
                        Check(selected.Stages.Single(stage => stage.Family.FamilyId == VariantOpeningFamily.Id).Survival is > 0 and < 1,
                            "W-first event cannot discharge stronger explicit variant");
                }
            }
            Near(terminalRates[0], terminalRates[1], fixture.Name + ":both manual orders retain same complete sieve probability");
        }

        var missingWorld = context with { Authority = context.Authority.WithWorldAuthority(null) };
        var partial = Request(explicitVariant with { FirstActUnknownEventPrefixCount = 2 }, missingWorld);
        var partialPlan = FamilyExecutionCoordinator.Plan(partial, false);
        Check(Variant(partialPlan, partial).Plan is { CoversVariants: false, UnknownPrefixCount: 2,
            CheckedPreparation.HasVariantRestrictions: false }, "missing variant authority admits only actual ordinary unknown check");
        Check(partial.Model.VariantOpening!.HasVariantRestrictions && partial.Model.CompiledSearch.NormalizedQuery.VariantBossBranches.Count == 1,
            "partial execution preserves authored variant intent");
        Check(partialPlan.FilterRemainders.Contains("ActVariants:ProductionExactOnly") &&
            !partialPlan.FilterRemainders.Any(value => value.StartsWith("FirstActUnknownEventPrefix:", StringComparison.Ordinal)),
            "partial V does not claim variant coverage or lose independent completed unknown check");
        Check(partialPlan.FilterRemainders.Contains(VariantOpeningFamily.Id + ":V.VariantAuthorityIncomplete"),
            "independent prefix coverage does not hide the real unavailable variant cause");
        var noVariantPlan = FamilyExecutionCoordinator.Plan(Request(explicitVariant, missingWorld), false);
        Check(noVariantPlan.FilterRemainders.Contains(VariantOpeningFamily.Id + ":NoSelectedFilter") &&
            noVariantPlan.FilterRemainders.Contains("ActVariants:ProductionExactOnly"),
            "genuine unavailable variant still discloses missing filtering and retained final duty");
        Near(partialPlan.Stages.Single().Survival, FirstActUnknownEventProbability.ConditionalPrefix(2, false),
            "prefix-only admission does not multiply unavailable variant mass");

        var partyFactory = typeof(CombinationRegressionChecks).GetMethod("PartyContext", BindingFlags.NonPublic | BindingFlags.Static)!;
        var party = (SearchContext)partyFactory.Invoke(null, [context, 3, null, null])!;
        var partyQuery = explicitVariant with { FirstActUnknownEventPrefixCount = 1,
            Players = Enumerable.Range(0, 3).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) {
                Conditions = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = slot + 1 } }).ToArray() };
        var partyRequest = Request(partyQuery, party);
        OnlyV(partyRequest, "party shared variant and ordinary unknown");
        var partyPlan = FamilyExecutionCoordinator.Plan(partyRequest, false);
        Check(Variant(partyPlan, partyRequest).Plan is { CoversVariants: true, UnknownPrefixCount: 3 },
            "party shared V uses maximum requested prefix across all slots and global source");
        Near(partyPlan.Stages.Single().Survival, FirstActUnknownEventProbability.ConditionalPrefix(3, false) / 3,
            "party shared variant and maximum prefix are counted once");
        Check(partyRequest.Model.CompiledSearch.NormalizedQuery.Players.Select(player => player.Conditions.FirstActUnknownEventPrefixCount)
            .SequenceEqual([1, 2, 3]), "party original per-slot prefix declarations retained");
        Console.WriteLine("VariantOpeningIntegration: " + checks + " passed");
        return 0;
    }
}
