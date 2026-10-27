using Godot;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

internal static partial class PartyPhase1Checks
{
    private static async Task CheckAncientEventCoverage(GameVersionDetection detection)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Party AE: " + why); checks++; }
        void Equal(double? actual, double expected, string why) => Check(actual.HasValue && Math.Abs(actual.Value - expected) < 1e-11,
            why + $" actual={actual};expected={expected}");
        var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, "000000000000",
            [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent],
            [UnlockState.all.ToSerializable(), UnlockState.all.ToSerializable()], 10, "0.111.0");
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
            10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
        var parent = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(SearchQuery.Empty with
            { Players = [new(0, ModelKeySetFilter.Empty), new(1, ModelKeySetFilter.Empty)] }, context), new("000000000000", 65, 1, 1)).Plan!;
        ExactSearchExecutionRequest Request(SearchQuery q, AncientOptionConditionProfile? premises = null) =>
            ExactSearchExecutionRequestFactory.ForPlayer(parent, SearchCompiler.CompilePlayer(q, parent.CompiledSearch.PlayerSearches[1].Context with
            { EvaluationAssumptions = new(premises ?? AncientOptionConditionProfile.BroadDefault) }));
        var empty = SearchSelectivityInput.From(Request(SearchQuery.Empty));
        foreach (var kind in new[] { EventResultConditionKind.MorphicGroveGroupInitialBasicsContains,
            EventResultConditionKind.SymbioteInitialBasicTransform, EventResultConditionKind.AromaOfChaosInitialBasicTransform,
            EventResultConditionKind.WhisperingHollowInitialBasicTransform, EventResultConditionKind.TrialNondescriptInitialBasicsContains })
        {
            var scenario = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredEventResult(party.Players[1], kind, UnlockState.all);
            var pool = scenario.Targets[0].OrderedSourceCandidates!;
            var condition = new EventResultSearchCondition(kind, pool[0].CardKey) { MorphicGroveScenario = scenario };
            double p = pool.Count(c => c.CardKey == condition.TargetKey) / (double)pool.Count;
            double expected = EventResultTransformSemantics.DrawCount(kind) == 1 ? p : 1 - (1 - p) * (1 - p);
            if (kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains) expected /= 3;
            Equal(EventResultProbabilityEstimator.EstimateSingle(empty, condition).Probability, expected, "MP transform marginal " + kind);
        }
        var template = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredEventResult(party.Players[1],
            EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, UnlockState.all);
        var a = template.Targets[0].OrderedSourceCandidates![0];
        var b = template.Targets[0].OrderedSourceCandidates!.First(c => c.CardKey != a.CardKey);
        EventResultSearchCondition Morphic(int slot, params MorphicGroveCard[] pool) => new(
            EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, a.CardKey)
        { MorphicGroveScenario = new(party.Players[slot], template.Premises,
            template.Targets.Select(t => t with { OrderedSourceCandidates = pool }).ToArray()) };
        var half = Morphic(0, a, b);
        var third = Morphic(1, a, b, b);
        Equal(EventResultProbabilityEstimator.EstimateTransformJoint([half, third]).Probability, 5d / 9,
            "shared Morphic with different pool sizes uses common draws");
        Equal(EventResultProbabilityEstimator.EstimateTransformJoint([half, half]).Probability, .75, "same Morphic fact paid once");
        Equal(EventResultProbabilityEstimator.EstimateTransformJoint([half, Morphic(1, b, a)]).Probability, .5,
            "shared Morphic disjoint per-draw target intervals");
        Equal(EventResultProbabilityEstimator.EstimateTransformJoint([half with { MorphicGroveSecondCard = a.CardKey },
            Morphic(1, b, a) with { MorphicGroveSecondCard = a.CardKey }]).Probability, 0, "shared Morphic pair conflict");
        EventResultSearchCondition Tinker(TinkerCardTypeTarget type, TinkerRiderTarget rider) =>
            new(EventResultConditionKind.TinkerTimeTypeAndRider, new("CARD", "MAD_SCIENCE")) { TinkerCardType = type, TinkerRider = rider };
        Equal(EventResultProbabilityEstimator.EstimateBlock(empty, [Tinker(TinkerCardTypeTarget.Attack, TinkerRiderTarget.Sapping),
            Tinker(TinkerCardTypeTarget.Skill, TinkerRiderTarget.Energized)], out _)?.Probability, 2d / 9,
            "Tinker distinct types share rider index shuffle");

        var oro = party.Players[1].WorldAuthority!.Beta109Generation!.AncientEventContexts.First(c =>
            c.PlayerSlot == 1 && c.AncientKey.Entry == "OROBAS");
        var sea = new ModelKey("RELIC", "SEA_GLASS");
        var target = oro.Catalog!.Pool("orobas.pool2")[0];
        var locked = AncientOptionConditionProfile.BroadDefault with
            { OrobasTouchOfOrobasConditionMet = false, OrobasArchaicToothConditionMet = false };
        var rows = new[] { new AncientSearchBranchCondition(oro.Act, oro.AncientKey, [sea], []),
            new AncientSearchBranchCondition(oro.Act, oro.AncientKey, [target], []) };
        var query = SearchQuery.Empty with { AncientBranches = rows };
        var lockedRequest = Request(query, locked);
        var inputPlan = SearchSelectivityInput.From(lockedRequest);
        var any = AncientOptionProbabilityEstimator.EstimateConditionalConjunction(inputPlan, oro.Act, oro.AncientKey,
            rows, [], []);
        Equal(any.Probability, AncientOptionProbabilityEstimator.EstimateConditional(inputPlan,
            new(oro.Act, oro.AncientKey, [sea, target], [])).Probability!.Value, "same Ancient rows are OR");
        var yes = AncientOptionProbabilityEstimator.EstimateConditionalConjunction(inputPlan, oro.Act, oro.AncientKey,
            [], [new([sea], [], [])], []);
        var no = AncientOptionProbabilityEstimator.EstimateConditionalConjunction(inputPlan, oro.Act, oro.AncientKey,
            [], [new([], [], [sea])], []);
        Equal(yes.Probability + no.Probability, 1, "legacy Any/Ban complements include locked Orobas third option");
        Equal(AncientOptionProbabilityEstimator.EstimateConditionalConjunction(inputPlan, oro.Act, oro.AncientKey,
            [], [new([], [target], [target])], []).Probability, 0, "same offer All/Ban contradiction");
        var legacy = Request(SearchQuery.Empty with { AncientBranches = [new(oro.Act, oro.AncientKey, [], [])],
            LegacyWorld = SearchQuery.Empty.LegacyWorld with { AncientOptionFilters = [new(oro.Act, new([], [], [sea]))],
                AncientSeaGlassTargetFilters = [new(oro.Act, new([party.Players[0].Character.CharacterKey], [], []))] } }, locked);
        await using var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
        foreach (var request in new[] { lockedRequest, legacy })
        {
            Check(AncientOptionFamily.TryCreate(request, out var family) && family is AncientOptionFamily { Plan.GpuSupported: true },
                "A changed shape admitted to GPU");
            var ancient = (AncientOptionFamily)family!;
            try
            {
                var batch = new SearchBatch(0, 65);
                var expected = Enumerable.Range(0, 65).Select(i => (ulong)i).Where(i => ancient.Plan.Reference(
                    Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, i)))).ToArray();
                Check(expected.Length > 0 && expected.Length < 65, "A source fixture has positive and negative roots");
                foreach (var candidates in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, 33).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var gpu = await family!.InvokeAsync(execution, new(batch, request), candidates, default);
                    Check(gpu.EnumerateLogicalOrdinals().SequenceEqual(expected.Where(i => candidates.IsDense || i % 2 == 0)),
                        "A dense/compact GPU agrees with source OR/legacy/locked option semantics");
                    Check(candidates.EnumerateLogicalOrdinals().Where(i => ancient.Plan.Matches(Beta111Profile.Instance.ComputeRootSeed(
                        VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, i))))
                        .SequenceEqual(expected.Where(i => candidates.IsDense || i % 2 == 0)), "A CPU agrees with source");
                }
            }
            finally { await family!.DisposeAsync(execution); }
        }
        GD.Print($"PARTY_ANCIENT_EVENT_COVERAGE_PASS checks={checks};gpuFixtures=2;rootsPerFixture=65");
    }
}
