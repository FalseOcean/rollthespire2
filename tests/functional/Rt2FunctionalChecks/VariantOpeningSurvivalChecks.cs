using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

internal static class VariantOpeningSurvivalChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        { checks++; if (!condition) throw new InvalidDataException("VariantOpeningSurvival: " + name); }
        void Near(double? actual, double expected, string name) => Check(actual is { } p && Math.Abs(p - expected) < 1e-12,
            name + ";actual=" + actual + ";expected=" + expected);
        var context = VariantOpeningPreparationChecks.CreateContext();
        var a = VariantOpeningPreparationChecks.A; var b = VariantOpeningPreparationChecks.B;
        var c = VariantOpeningPreparationChecks.C; var x = VariantOpeningPreparationChecks.X;
        ExactSearchExecutionRequest Request(SearchQuery query, SearchContext? ctx = null)
        {
            var compiled = SearchCompiler.Compile(query, ctx ?? context);
            return new(compiled, new("000000000000", 10, 1, 1), "000000000000", 10,
                CompiledSearchEvaluationProjector.Project(compiled).Evaluation, CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint);
        }
        var eventQuery = SearchQuery.Empty with { EventSequenceConstraints = [new(1, null,
            SearchSequenceRangeMode.FirstN, 1, new([x], [], []))] };
        var request = Request(eventQuery);
        var prep = VariantOpeningPreparation.Prepare(request.CompiledSearch);
        WorldFastDenseSetPredicate Any(ushort id) => new([id], [], [], false);
        WorldFastActPlan Act(ushort id, bool containsTarget) => new(
            Act: 1, ActId: id, WeakEncounterSlots: 0, RegularEncounterSlots: 0, EliteEncounterSlots: 0,
            EventCandidates: [new(containsTarget ? (ushort)10 : (ushort)12, 0, true), new(11, 0, true)], EligibleEventCount: 2,
            WeakEncounters: [], RegularEncounters: [], EliteEncounters: [], EncounterConflictSourceOrdinals: [],
            Bosses: [20, 21, 22], Ancients: [30], BossActPredicates: [], BossOrdinalOnePredicates: [], BossOrdinalTwoPredicates: [],
            AncientIdentityPredicates: [], AncientBranchIdentityPredicates: [], AncientOptionPlans: [],
            AncientBranchOptionPredicates: [], AncientOptionPredicates: [], SeaGlassTargetPredicates: [],
            EventPredicates: [new(0, 1, byte.MaxValue, Any(10))]);
        Beta110WorldFastPlan Plan(params WorldFastActPlan[] acts) => Beta110WorldFastPlan.Disabled("") with {
            Enabled = true, ActSelectionGroups = [new(1, 0, [0, 1, 2])], ActPlanIndexByDenseId = [0, 1, 2],
            Acts = acts, MaxRequiredAct = 1, Ascension = 0, MaxEventCandidateCount = 2 };
        var physical = Plan(Act(0, true), Act(1, false), Act(2, false));
        var model = VariantOpeningWorldSurvival.Create(request, prep, physical);
        var empty = new HashSet<string>(); var passedV = new HashSet<string> { "V.VariantOpening" };
        var passedW = new HashSet<string> { "W.World" };
        Near(model.VariantProbability, 1d / 3, "implicit variant mass");
        Near(model.WorldProbability, 1d / 6, "actual W opening-skip event mixture");
        Near(model.ResolveWorld(passedV).SurvivalProbability, .5, "V first removes only already-paid variant mass");
        Near(model.ResolveVariant(passedW, 1).SurvivalProbability, 1, "complete actual event sieve implies necessary variant");
        Near(model.ResolveVariant(empty, .7).SurvivalProbability * model.ResolveWorld(passedV).SurvivalProbability,
            model.ResolveWorld(empty).SurvivalProbability!.Value * model.ResolveVariant(passedW, .7).SurvivalProbability!.Value,
            "both orders charge unknown prefix and variant once");
        Check(model.WorldGuaranteesVariant, "actual impossibility outside allowed variants proves W implication");

        var partial = physical with { Acts = physical.Acts.Select(act => act with { EventPredicates = [] }).ToArray() };
        var partialModel = VariantOpeningWorldSurvival.Create(request, prep, partial);
        Near(partialModel.WorldProbability, 1, "partial W body with no event predicates passes all");
        Near(partialModel.ResolveVariant(passedW, 1).SurvivalProbability, 1d / 3,
            "logical event q cannot certify a partial actual W body");
        Check(!partialModel.WorldGuaranteesVariant, "partial W does not inherit Exact implication");

        var stronger = Request(SearchQuery.Empty with { VariantBossBranches = [new(1, a, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] });
        var both = physical with { Acts = [Act(0, true), Act(1, true), Act(2, false)] };
        var strongerModel = VariantOpeningWorldSurvival.Create(stronger, VariantOpeningPreparation.Prepare(stronger.CompiledSearch), both);
        Near(strongerModel.ResolveVariant(passedW, 1).SurvivalProbability, .5,
            "stronger explicit variant still filters after W");
        Near(strongerModel.ResolveWorld(passedV).SurvivalProbability, .5, "W conditioned on stronger explicit allowance");

        var unknown = physical with { Acts = physical.Acts.Select(act => act with { AncientIdentityPredicates = [Any(30)] }).ToArray() };
        var unknownModel = VariantOpeningWorldSurvival.Create(request, prep, unknown);
        Check(unknownModel.WorldProbability is null && unknownModel.ResolveWorld(passedV).SurvivalProbability is null,
            "unavailable Ancient physical block stays Unknown");
        Near(unknownModel.ResolveVariant(passedW, 1).SurvivalProbability, 1,
            "actual event impossibility still proves variant after W despite independent unknown probability");
        var unknownPartial = VariantOpeningWorldSurvival.Create(request, prep,
            partial with { Acts = partial.Acts.Select(act => act with { AncientIdentityPredicates = [Any(30)] }).ToArray() });
        Check(unknownPartial.ResolveVariant(passedW, 1).SurvivalProbability is null,
            "unpriced partial W supplies no invented conditional mass");

        var branches = Request(SearchQuery.Empty with { VariantBossBranches = [
            new(1, a, new([new("ENCOUNTER", "V_BOSS_A")], [], []), ModelKeySetFilter.Empty, false),
            new(1, b, new([new("ENCOUNTER", "V_BOSS_B")], [], []), ModelKeySetFilter.Empty, false)] });
        var bossPhysical = partial with { Acts = [
            partial.Acts[0] with { FamilyBossBranches = [new(Any(20), new([], [], [], false))] },
            partial.Acts[1] with { FamilyBossBranches = [new(Any(21), new([], [], [], false))] }, partial.Acts[2]] };
        var bossModel = VariantOpeningWorldSurvival.Create(branches, VariantOpeningPreparation.Prepare(branches.CompiledSearch), bossPhysical);
        Near(bossModel.WorldProbability, 5d / 9, "W keeps boss predicates under each actual variant and neutral outside explicit set");
        Near(bossModel.ResolveVariant(passedW, 1).SurvivalProbability, .4, "W first preserves branch correlation with explicit V");
        Near(bossModel.ResolveWorld(passedV).SurvivalProbability, 1d / 3, "V first retains branch-local boss checks");
        var alternatives = bossPhysical with { Acts = [bossPhysical.Acts[0] with { FamilyBossBranches = [
            new(Any(20), new([], [], [], false)), new(Any(21), new([], [], [], false))] }, bossPhysical.Acts[1], bossPhysical.Acts[2]] };
        var alternativeModel = VariantOpeningWorldSurvival.Create(branches, VariantOpeningPreparation.Prepare(branches.CompiledSearch), alternatives);
        Near(alternativeModel.WorldProbability, 2d / 3, "same-variant boss rows remain alternatives");

        var world = context.Authority.WorldAuthority!;
        var weightedContext = context with { Authority = context.Authority.WithWorldAuthority(world with {
            Beta109Generation = world.Beta109Generation! with { ActSelectionGroups = [new(1, [a, a, b, c], true)] } }) };
        var weightedRequest = Request(eventQuery, weightedContext);
        var weighted = VariantOpeningWorldSurvival.Create(weightedRequest, VariantOpeningPreparation.Prepare(weightedRequest.CompiledSearch),
            physical with { ActSelectionGroups = [new(1, 0, [0, 0, 1, 2])] });
        Near(weighted.VariantProbability, .5, "duplicate source entries preserve actual variant weight");
        Near(weighted.WorldProbability, .25, "physical W mixture retains weighted candidates");
        var noW = VariantOpeningWorldSurvival.Create(request, prep, null);
        Near(noW.WorldProbability, 1, "no actual W is no filter");
        Near(noW.ResolveVariant(passedW, 1).SurvivalProbability, 1d / 3, "absent W cannot certify implicit variant");
        var duplicates = physical with { Acts = [physical.Acts[0] with {
            EventCandidates = [new(10, 0, true), new(10, 0, true), new(11, 0, true)] }, physical.Acts[1], physical.Acts[2]] };
        Check(VariantOpeningWorldSurvival.Create(request, prep, duplicates).WorldProbability is null,
            "duplicate event identities retain existing solver Unknown boundary");
        Console.WriteLine("VariantOpeningSurvival: " + checks + " passed");
        return 0;
    }
}
