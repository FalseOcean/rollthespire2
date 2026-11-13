using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;

internal static class UnknownPrefixRelationChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string reason) { checks++; if (!ok) throw new InvalidDataException("UnknownPrefix." + reason); }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        PreparedSearch Prepare(SearchQuery q, SearchContext? c = null) => SearchPreparationCompiler.Compile(SearchCompiler.Compile(q, c ?? context),
            new("000000000000", 65536, int.MaxValue, 1)).Plan ?? throw new InvalidDataException("UnknownPrefix.Prepare");
        for (int count = 1; count <= 3; count++)
        foreach (bool monster in new[] { false, true })
        {
            var q = monster ? SearchQuery.Empty with { FirstActUnknownMonsterPrefixCount = count } :
                SearchQuery.Empty with { FirstActUnknownEventPrefixCount = count };
            var request = Prepare(q);
            var initial = InitialRoomsPlan.TryCompileUnknown(request.Model)!;
            Check(initial.UnknownMonster == monster && initial.UnknownPrefixCount == count, "InitialPlanCategory");
            Check(VariantOpeningFamily.TryPrepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var body, false) &&
                body!.Plan.UnknownMonster == monster && body.Plan.UnknownPrefixCount == count, "VariantPlanCategory");
            var native = new VariantOpeningGpuPlan(body!.Plan, GpuNumericalVariant.NativeFloat64);
            var integer = new VariantOpeningGpuPlan(body.Plan, GpuNumericalVariant.IntegerRne);
            Check(native.ShaderSource().Contains("if(next_float()>0.1)return false;", StringComparison.Ordinal) == monster &&
                !integer.ShaderSource().Contains("GL_EXT_shader_explicit_arithmetic_types_float64", StringComparison.Ordinal), "GpuNativeAndIntegerSpecialization");
            Check(initial.ShaderSource().Contains("if(next_float()>0.1)return false;", StringComparison.Ordinal) == monster,
                "InitialGpuSpecialization");
            var physical = FamilyExecutionCoordinator.Plan(request, false);
            Check(!physical.FilterRemainders.Any(r => r.Contains(InitialRoomsInvocation.UnknownCoverage, StringComparison.Ordinal)), "CompleteCoverage");
            for (ulong ordinal = 0; ordinal < 8192; ordinal++)
            {
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
                var sequence = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, count, false);
                bool expected = sequence.Steps.All(s => s.Room == (monster ? UnknownRoomCategory.Monster : UnknownRoomCategory.Event));
                Check(initial.Matches(root) == expected && initial.Reference(root) == expected && body.Plan.Matches(root) == expected,
                    "NumericalAndProductionRoomReplay");
                Check(!InitialRoomsPlan.MatchesUnknownCategory(root, count, true, true), "DeclaredJuzuBlocksMonster");
                if (ordinal < 128)
                    Check(ProductionExactSearchEvaluator.Evaluate(request.Model,
                        TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).Evaluation.Disposition ==
                        (expected ? SearchDisposition.Match : SearchDisposition.NoMatch), "ProductionExact");
            }
            var probability = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request.Model));
            Check(probability.Probability is { } p && Math.Abs(p - FirstActUnknownEventProbability.ConditionalPrefix(count, false, monster)) < 1e-12,
                "JointProbabilityOnce");
            Check(JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(q))!.FirstActUnknownMonsterPrefixCount == q.FirstActUnknownMonsterPrefixCount,
                "SavedCategoryRoundTrip");
        }
        var conflict = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 1, FirstActUnknownMonsterPrefixCount = 3 };
        var compiledConflict = SearchCompiler.Compile(conflict, context);
        Check(compiledConflict.Status == QueryNormalizationStatus.Impossible &&
            compiledConflict.Query.FirstActUnknownEventPrefixCount == 1 && compiledConflict.Query.FirstActUnknownMonsterPrefixCount == 3,
            "ContradictionRetainsBothAuthoredConditions");
        Check(JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(compiledConflict)).ExactlyImpossible, "ContradictionProbability");
        var juzuQuery = SearchQuery.Empty with { FirstActUnknownMonsterPrefixCount = 1,
            OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule),
            LegacyNeow = SearchQuery.Empty.LegacyNeow with { CapsuleContainedRelics = new([new("RELIC", "JUZU_BRACELET")], [], []) } };
        var declared = SearchCompiler.Compile(juzuQuery, context);
        Check(ExplicitOpeningRelicFacts.HasDeclaredJuzuForUnknownRooms(declared.NormalizedQuery) &&
            SearchFeasibilityAnalyzer.Analyze(declared).IsImpossible &&
            JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(declared)).ExactlyImpossible,
            "DeclaredJuzuContradictionIsProvenBeforeSearch");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var factory = typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!;
        foreach (int players in new[] { 2, 4 })
        {
            var party = (SearchContext)factory.Invoke(null, [context, players, null, null])!;
            var q = SearchQuery.Empty with { FirstActUnknownMonsterPrefixCount = 1,
                Players = Enumerable.Range(0, players).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) {
                    Conditions = SearchQuery.Empty with { FirstActUnknownMonsterPrefixCount = slot == players - 1 ? 3 : 0 } }).ToArray() };
            var request = Prepare(q, party);
            var plan = InitialRoomsPlan.TryCompileUnknown(request.Model)!;
            Check(plan.UnknownMonster && plan.UnknownPrefixCount == 3 && PartyInitialRooms.Prefixes(request.Model).Length == 2,
                "PartySharedMaximumAndSources");
            var physical = FamilyExecutionCoordinator.Plan(request, false);
            Check(physical.ExecutionOrderUnits.Count(f => f.FamilyId == VariantOpeningFamily.Id) == 1, "PartyOneSharedFilter");
            for (ulong ordinal = 0; ordinal < 128; ordinal++)
            {
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
                bool expected = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, 3, false).Steps.All(s => s.Room == UnknownRoomCategory.Monster);
                Check(plan.Matches(root) == expected && PartyInitialRooms.Evaluate(request.Model, root).Disposition ==
                    (expected ? SearchDisposition.Match : SearchDisposition.NoMatch), "PartySharedRoomStream");
            }
            var incompatible = q with { Players = q.Players.Select(p => p.Slot == 0 ? p with {
                Conditions = p.Conditions with { FirstActUnknownEventPrefixCount = 1 } } : p).ToArray() };
            Check(SearchCompiler.Compile(incompatible, party).Status == QueryNormalizationStatus.Impossible,
                "PartyCrossSourceContradiction");
        }
        Console.WriteLine($"UNKNOWN_PREFIX_RELATIONS=PASS;checks={checks};counts=1..3;eventMonster=true;party=2,4;nativeGpuNotRun=true");
        return 0;
    }
}
