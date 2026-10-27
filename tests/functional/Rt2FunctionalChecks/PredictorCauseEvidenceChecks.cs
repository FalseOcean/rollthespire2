using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorCauseEvidenceChecks
{
    internal static void Run()
    {
        PredictorCombatPendingCause Derived(string code, string node, ImmutableArray<PredictorCombatPendingCause> parents) =>
            PredictorRouteCauseEvidence.CreateExplicit(code, node, PredictorCombatDomain.Continuation,
                PredictorPendingReasonKind.DerivedDependency, "Proven dependency propagation.", ["fixture.result"], parents);
        var reward = new PredictorReward(91, PredictorRewardKind.Card, PredictorRewardOrigin.Event,
            new ModelKey("EVENT", "BRAIN_LEECH"), []);
        var firstChoice = PredictorRouteCauseEvidence.RewardChoice("map:1:2:3:4", reward, 0);
        var secondChoice = PredictorRouteCauseEvidence.RewardChoice("map:1:2:3:4", reward with { Id = 92 }, 1);
        Check(firstChoice.Identity != secondChoice.Identity, "Same-node rewards lost their occurrence identity.");
        Check(firstChoice.Identity == PredictorRouteCauseEvidence.RewardChoice("map:1:2:3:4", reward with { Id = 900 }, 0).Identity,
            "Reward choice identity depends on a witness instance ID.");
        Check(firstChoice.RewardOrigin == PredictorRewardOrigin.Event && firstChoice.Model == reward.Key,
            "Reward choice lost its source model or origin.");
        var first = PredictorRouteCauseEvidence.Create("PendingCardChoice", "map:1:2:3:4", PredictorCombatDomain.Deck,
            occurrenceKey: firstChoice.Identity, choice: firstChoice);
        var second = PredictorRouteCauseEvidence.Create("PendingCardChoice", "map:1:2:3:4", PredictorCombatDomain.Deck,
            occurrenceKey: secondChoice.Identity, choice: secondChoice);
        var observed = PredictorRouteCauseEvidence.Create("EnemyCreationCount", "map:1:2:3:4", PredictorCombatDomain.NicheRng);
        var wrapper = Derived("DerivedObtainDependency", "map:1:5:3:7", [first, second, observed]);
        var roots = PredictorRouteCauseEvidence.Roots([wrapper, first]);
        Check(roots.Length == 3 && roots.Any(c => c.Identity == first.Identity) && roots.Any(c => c.Identity == second.Identity),
            "Derived wrapper replaced independent original choices or duplicated a shared root.");
        Check(roots.All(c => c.NodeId == "map:1:2:3:4"), "A consumer node replaced the original node.");
        Check(PredictorRouteCauseEvidence.RequiredInputs(wrapper).IsEmpty, "A flow guard became a player obligation.");
        var sharedA = Derived("DerivedRngDependency", "map:1:4:3:6", [first]);
        var sharedB = sharedA with { Parents = [second] };
        var consumerA = Derived("DerivedShopDependency", "map:1:5:3:7", [sharedA]);
        var consumerB = Derived("DerivedEventDependency", "map:1:6:3:8", [sharedB]);
        Check(PredictorRouteCauseEvidence.Roots([consumerA, consumerB]).Length == 2,
            "A repeated shared cause identity hid a parent's other original choice.");
        var merged = PredictorRouteCauseEvidence.Normalize([wrapper,
            wrapper with { AffectedDomains = PredictorCombatDomain.Gold, Parents = [observed] }]);
        Check(merged.Length == 1 && merged[0].Parents.Length == 3 &&
            merged[0].AffectedDomains == (PredictorCombatDomain.Continuation | PredictorCombatDomain.Gold),
            "Same occurrence merge lost parent edges or affected domains.");
        var unknownA = new PredictorCombatPendingCause("UnprovedReader", "same text", PredictorCombatDomain.Deck);
        var unknownB = new PredictorCombatPendingCause("UnprovedReader", "same text", PredictorCombatDomain.Deck);
        var unknowns = PredictorRouteCauseEvidence.Normalize([unknownA, unknownB]);
        Check(unknowns.Length == 2 && unknowns.All(c => c.ReasonKind == PredictorPendingReasonKind.ImplementationGap &&
            PredictorRouteCauseEvidence.RequiredInputs(c).IsEmpty), "Unlocated causes merged or invented missing input.");
        Check(PredictorRouteCauseEvidence.Bind(unknownA).Identity == PredictorRouteCauseEvidence.Bind(unknownA).Identity,
            "Rebinding the same external cause changed its opaque identity.");
        var fault = unknowns[0] with { ReasonKind = PredictorPendingReasonKind.ExecutionFault,
            SourceEvidence = "Actual operation threw an execution exception." };
        Check(PredictorRouteCauseEvidence.Bind(fault).ReasonKind == PredictorPendingReasonKind.ExecutionFault,
            "Rebinding overwrote an explicit source classification.");
        var explicitFault = PredictorRouteCauseEvidence.CreateExplicit("FutureProducerFailure", "node:fault",
            PredictorCombatDomain.Continuation, PredictorPendingReasonKind.ExecutionFault, "Future producer failed.", ["operation.result"]);
        var reboundFault = PredictorRouteCauseEvidence.Normalize([explicitFault, explicitFault with { Parents = [first] }])[0];
        Check(reboundFault.ReasonKind == PredictorPendingReasonKind.ExecutionFault &&
            reboundFault.SourceEvidence == "Future producer failed." && reboundFault.RequiredPropertyKeys.SequenceEqual(["operation.result"]) &&
            reboundFault.Parents.Length == 1, "Normalize/Merge lost explicit classification, source, properties or parents.");
        Console.WriteLine("PREDICTOR_CAUSE_EVIDENCE_PASS reward occurrences; origin preservation; parent DAG; identity merge; explicit classification");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
