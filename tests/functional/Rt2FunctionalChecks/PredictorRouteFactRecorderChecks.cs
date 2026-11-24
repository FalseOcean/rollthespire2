using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorRouteFactRecorderChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static PredictorCombatPendingCause Choice(string node, string choice) =>
        PredictorRouteCauseEvidence.Bind(new("PendingCardChoice", "same wording", PredictorCombatDomain.Deck)
        { NodeId = node, ChoiceId = choice, RequiredPropertyKeys = ["deck.membership", "deck.count"] });
    private static PredictorCombatPendingCause Wrapper(params PredictorCombatPendingCause[] parents) =>
        PredictorRouteCauseEvidence.CreateExplicit("RestDeckDependentAction", "floor6", PredictorCombatDomain.NicheRng,
            PredictorPendingReasonKind.DerivedDependency, "Candidate count depends on earlier reward choices.",
            ["upgrade.candidateCount"], parents.ToImmutableArray());

    internal static void Run()
    {
        SameSourceGraphAndValueMasking();
        ExpandingCauseAndFrozenFrames();
        HypotheticalCauseExpansionStaysLocal();
        EstablishedValuesAndOrigins();
        ObservationAndFaultKinds();
        ClassifiedLimitWithParents();
        RecomputedChoiceDropsOldRoots();
        Console.WriteLine("PREDICTOR_ROUTE_FACT_RECORDER_PASS cause bridge; origin identity; versioned parents; masked witness; immutable node frames");
    }

    private static void SameSourceGraphAndValueMasking()
    {
        var recorder = new PredictorRouteFactRecorder();
        var first = Choice("floor3", "pick-first");
        var second = Choice("floor4", "pick-second");
        var wrapper = Wrapper(first, second);
        bool formatterCalled = false;
        var niche = recorder.RecordValue("rng.Niche.calls", "floor9", PredictorRouteFactPhase.After, 987654,
            [wrapper], operation: "read-niche", safeFormatter: value =>
            { formatterCalled = true; return "unsafe-" + value; });
        var result = recorder.RecordResult("cardReward.enchantment", "floor9", "unsafe-enchantment",
            allReadFactIds: [niche], unresolvedReadFactIds: [niche]);
        var snapshot = recorder.Snapshot();
        var explanation = snapshot.Explain(result);
        Check(!formatterCalled && snapshot.Fact(niche).DisplayValue == null && snapshot.Fact(result).DisplayValue == null &&
            !snapshot.Facts.Any(f => f.DisplayValue?.Contains("unsafe") == true),
            "Cause-dependent typed witnesses must be masked before formatting or publishing");
        Check(explanation.Roots.Length == 2 && explanation.Roots.All(r => r.Kind == PredictorKnowledgeRootKind.UserChoice) &&
            explanation.Roots.Any(r => r.NodeId == "floor3" && r.ChoiceId == "pick-first") &&
            explanation.Roots.Any(r => r.NodeId == "floor4" && r.ChoiceId == "pick-second") &&
            explanation.Roots.Select(r => r.Id).Distinct().Count() == 2,
            "Same-wording choices must preserve independent original nodes and identities");
        Check(explanation.Facts.Any(f => f.NodeId == "floor6" && f.PropertyKey == "upgrade.candidateCount") &&
            explanation.Facts.Count(f => f.PropertyKey == "deck.membership") == 2 &&
            explanation.Roots.All(r => r.RootKey != wrapper.Identity) &&
            explanation.RequiredInputs.Select(input => input.Input.Key).ToHashSet().SetEquals(["pick-first", "pick-second"]),
            "Consumer wrappers must remain intermediate facts, not new player repair obligations");
    }

    private static void ExpandingCauseAndFrozenFrames()
    {
        var recorder = new PredictorRouteFactRecorder();
        var first = Choice("floor3", "first");
        var second = Choice("floor4", "second");
        var wrapper = Wrapper(first);
        var original = recorder.RecordUnknown("candidate.count", "floor6", PredictorRouteFactPhase.Results, [wrapper]);
        var oldFrames = recorder.FreezeFrames();
        var oldSnapshot = oldFrames["floor6"].Snapshot;
        var firstRoot = oldSnapshot.Explain(original).Roots.Single().Id;
        int oldCount = oldSnapshot.Facts.Length;
        // Same wrapper identity arrives with an additional parent, including
        // when the producer supplies only that newly discovered parent here.
        var enlarged = wrapper with { Parents = [second] };
        var newer = recorder.RecordUnknown("candidate.count", "floor6", PredictorRouteFactPhase.Results, [enlarged]);
        var snapshot = recorder.Snapshot();
        Check(snapshot.Explain(newer).Roots.Length == 2 && snapshot.Explain(newer).Roots.Any(r => r.Id == firstRoot) &&
            oldSnapshot.Explain(original).Roots.Length == 1 && oldSnapshot.Facts.Length == oldCount &&
            oldFrames["floor6"].Results["candidate.count"] == original,
            "Expanded parent evidence must append a new fact version while preserving prior snapshots and original roots");
        var expandedProperties = first with { RequiredPropertyKeys = first.RequiredPropertyKeys.Add("deck.removable") };
        var newest = recorder.RecordUnknown("deck.complete", "floor9", PredictorRouteFactPhase.After, [expandedProperties]);
        snapshot = recorder.Snapshot();
        Check(snapshot.Explain(newest).Roots.Single().Id == firstRoot && snapshot.Roots.Length == 2 &&
            snapshot.Explain(newest).Facts.Any(f => f.PropertyKey == "deck.removable") &&
            snapshot.Explain(newest).RequiredInputs.Single().Input.Key == "first",
            "One origin expanding to more properties must keep one stable repair identity, not register new roots per field");
        var frames = recorder.FreezeFrames();
        Check(frames["floor6"].Results["candidate.count"] == newer &&
            ReferenceEquals(frames["floor6"].Snapshot, frames["floor9"].Snapshot) &&
            frames["floor9"].Diagnostics.After.Contains(newest),
            "Frozen node frames must use latest property IDs and a shared immutable forecast snapshot");
    }

    private static void HypotheticalCauseExpansionStaysLocal()
    {
        var recorder = new PredictorRouteFactRecorder();
        var first = Choice("floor3", "actual-choice");
        var second = Choice("floor4", "hypothetical-choice");
        var original = Wrapper(first);
        var before = recorder.RecordUnknown("parent.before", "floor6", PredictorRouteFactPhase.Before, [original]);
        var branch = recorder.Fork();
        var expanded = original with
        { RequiredPropertyKeys = original.RequiredPropertyKeys.Add("branch.deck.removable"), Parents = [second] };
        var projected = branch.RecordUnknown("branch.result", "floor6", PredictorRouteFactPhase.Results, [expanded]);

        // The shared cause identity is later read from its original context.
        // Neither the parent nor a sibling created after the hypothetical
        // calculation may acquire that branch's extra property or parent.
        var after = recorder.RecordUnknown("parent.after", "floor6", PredictorRouteFactPhase.After, [original]);
        var sibling = recorder.Fork();
        var siblingResult = sibling.RecordUnknown("sibling.result", "floor6", PredictorRouteFactPhase.Results, [original]);
        var frames = recorder.FreezeFrames();
        var frame = frames["floor6"];
        var snapshot = frame.Snapshot;
        var branchExplanation = snapshot.Explain(projected);
        Check(branchExplanation.Roots.Select(root => root.RootKey).ToHashSet().SetEquals([first.Identity, second.Identity]) &&
            branchExplanation.Facts.Any(fact => fact.PropertyKey == "branch.deck.removable"),
            "A hypothetical cause expansion must retain its additional property and original/new parent evidence");
        foreach (var result in new[] { before, after, siblingResult })
        {
            var explanation = snapshot.Explain(result);
            Check(explanation.Roots.Single().RootKey == first.Identity &&
                !explanation.Facts.Any(fact => fact.PropertyKey == "branch.deck.removable"),
                "Hypothetical cause properties and parents must not broaden later parent or sibling dependency facts");
        }
        Check(frame.Before["parent.before"] == before && frame.After["parent.after"] == after &&
            frame.Results["branch.result"] == projected && frame.Results["sibling.result"] == siblingResult &&
            snapshot.Fact(projected).Status == PredictorKnowledgeStatus.Unknown && snapshot.Fact(projected).DisplayValue == null &&
            branch.Fact(after) == snapshot.Fact(after) && sibling.Fact(projected) == snapshot.Fact(projected),
            "Branch observations must remain masked and resolve through the final forecast's shared fact IDs and diagnostic frame");
    }

    private static void EstablishedValuesAndOrigins()
    {
        var recorder = new PredictorRouteFactRecorder();
        var captured = recorder.RecordValue("gold", "floor3", PredictorRouteFactPhase.Before, 120,
            origin: PredictorKnowledgeOrigin.Captured);
        var approved = recorder.RecordValue("enemy.count", "floor3", PredictorRouteFactPhase.Before, 0,
            origin: PredictorKnowledgeOrigin.ApprovedDefault);
        var input = recorder.RecordValue("reward.disposition", "floor3", PredictorRouteFactPhase.After, "skip",
            origin: PredictorKnowledgeOrigin.PlayerInput);
        var derived = recorder.RecordResult("gold.unchanged", "floor3", 120, allReadFactIds: [captured, approved, input]);
        var partial = recorder.RecordPartial("deck.membership", "floor6", PredictorRouteFactPhase.Before,
            "five guaranteed cards", [Choice("floor3", "unpicked")]);
        var invariant = recorder.RecordResult("upgrade.possible", "floor6", true, allReadFactIds: [partial],
            derivation: "A guaranteed retained card is upgradable regardless of the optional addition");
        var snapshot = recorder.Snapshot();
        Check(snapshot.Fact(captured).Origin == PredictorKnowledgeOrigin.Captured &&
            snapshot.Fact(approved).Origin == PredictorKnowledgeOrigin.ApprovedDefault &&
            snapshot.Fact(input).Origin == PredictorKnowledgeOrigin.PlayerInput &&
            snapshot.Fact(derived).Origin == PredictorKnowledgeOrigin.Derived && snapshot.Fact(approved).DisplayValue == "0",
            "Established captured, approved default, player input and derived values must preserve their distinct origins");
        Check(snapshot.Fact(invariant).AllReadFactIds.Contains(partial) && snapshot.Explain(invariant).Roots.IsEmpty &&
            snapshot.Fact(partial).DisplayValue == "five guaranteed cards",
            "A model-proved invariant may read partial state without inheriting unresolved dependencies");
        var frozen = recorder.Freeze()["floor3"];
        Check(frozen.Before.SequenceEqual([captured, approved]) && frozen.After.SequenceEqual([input]) &&
            frozen.Results.SequenceEqual([derived]), "Before/After/Results must retain the owning phase and record order");
    }

    private static void ObservationAndFaultKinds()
    {
        var recorder = new PredictorRouteFactRecorder();
        var observation = PredictorRouteCauseEvidence.Bind(new("EnemyCreationCount", "creation count missing",
            PredictorCombatDomain.NicheRng, PredictorEnemyCreationSource.FabricatorBots) { NodeId = "floor6" });
        var missing = recorder.RecordUnknown("rng.Niche.offset", "floor9", PredictorRouteFactPhase.Results, [observation],
            status: PredictorKnowledgeStatus.NotObserved);
        var choice = Choice("floor3", "first");
        // Fully bound fault metadata comes from the source owner, not a code
        // naming heuristic. The original fault remains a root with parents.
        var fault = new PredictorCombatPendingCause("NativeReadFailed", "original read fault", PredictorCombatDomain.NicheRng)
        { Identity = "fault-read", NodeId = "floor8", ReasonKind = PredictorPendingReasonKind.ExecutionFault,
            Parents = [choice], RequiredPropertyKeys = ["rng.Niche.state"], SourceEvidence = "source read failure" };
        var failed = recorder.RecordUnknown("result", "floor9", PredictorRouteFactPhase.Results, [fault]);
        var gap = PredictorRouteCauseEvidence.Bind(new("EventNotModeled", "event model missing",
            PredictorCombatDomain.Continuation) { NodeId = "floor7" });
        var unsupported = recorder.RecordUnknown("event.result", "floor7", PredictorRouteFactPhase.Results, [gap]);
        var unavailable = new PredictorCombatPendingCause("EventEntryPotionRequired", "no qualifying potion",
            PredictorCombatDomain.Continuation)
        { Identity = "unavailable-entry", NodeId = "floor7", ReasonKind = PredictorPendingReasonKind.KnownUnavailable,
            RequiredPropertyKeys = ["event.option.available"] };
        var blocked = recorder.RecordUnknown("event.option.available", "floor7", PredictorRouteFactPhase.Results, [unavailable]);
        var dependency = new PredictorCombatPendingCause("DerivedCandidateCount", "count depends on earlier card choice",
            PredictorCombatDomain.Deck)
        { Identity = "derived-count", NodeId = "floor6", ReasonKind = PredictorPendingReasonKind.DerivedDependency,
            Parents = [choice], RequiredPropertyKeys = ["upgrade.candidateCount"] };
        var dependent = recorder.RecordUnknown("rng.Niche.calls", "floor9", PredictorRouteFactPhase.Results, [dependency]);
        var snapshot = recorder.Snapshot();
        Check(snapshot.Explain(missing).Roots.Single().Kind == PredictorKnowledgeRootKind.MissingObservation &&
            snapshot.Explain(missing).RequiredInputs.Length == 1 && snapshot.Fact(missing).DisplayValue == null,
            "Missing observations must retain repair identity while unobserved values stay absent");
        var faultExplanation = snapshot.Explain(failed);
        Check(faultExplanation.Roots.Any(r => r.Kind == PredictorKnowledgeRootKind.ExecutionFault && r.NodeId == "floor8") &&
            faultExplanation.Roots.Any(r => r.Kind == PredictorKnowledgeRootKind.UserChoice) &&
            faultExplanation.RequiredInputs.All(r => r.Input.Key == "first"),
            "A real execution fault must retain its original classification without inventing a player input for the fault");
        Check(snapshot.Explain(unsupported).Roots.Single().Kind == PredictorKnowledgeRootKind.ImplementationGap &&
            snapshot.Explain(unsupported).RequiredInputs.IsEmpty,
            "An unavailable event implementation must not be presented as a missing player observation");
        Check(snapshot.Explain(blocked).Roots.Single().Kind == PredictorKnowledgeRootKind.KnownUnavailable &&
            snapshot.Explain(blocked).RequiredInputs.IsEmpty,
            "A known unmet condition must retain its classification without a missing observation or player repair duty");
        Check(snapshot.Explain(dependent).Roots.Single().RootKey == choice.Identity &&
            snapshot.Explain(dependent).Facts.Any(f => f.NodeId == "floor6" && f.PropertyKey == "upgrade.candidateCount") &&
            snapshot.Explain(dependent).Roots.All(r => r.RootKey != dependency.Identity),
            "Derived dependency wrappers must preserve intermediate facts and only inherit original parent obligations");
    }

    private static void ClassifiedLimitWithParents()
    {
        foreach (var kind in new[] { PredictorPendingReasonKind.ImplementationGap, PredictorPendingReasonKind.FlowGate,
                     PredictorPendingReasonKind.PlanBinding, PredictorPendingReasonKind.KnownUnavailable })
        {
            var recorder = new PredictorRouteFactRecorder();
            var choice = Choice("floor3", "earlier-pick");
            var limit = PredictorRouteCauseEvidence.CreateExplicit("LimitedReader", "floor6", PredictorCombatDomain.NicheRng,
                kind, "The consumer has its own classified limitation.", ["rng.Niche.consumed"], [choice]);
            var result = recorder.RecordUnknown("later.result", "floor9", PredictorRouteFactPhase.Results, [limit]);
            var explanation = recorder.Snapshot().Explain(result);
            var original = PredictorRouteCauseEvidence.Roots(limit);
            Check(explanation.Roots.Length == 2 && original.Length == 2 &&
                explanation.Roots.Select(root => root.RootKey).ToHashSet().SetEquals(original.Select(cause => cause.Identity)) &&
                explanation.Roots.Single(root => root.RootKey == limit.Identity).Kind.ToString() == kind.ToString() &&
                explanation.Roots.Single(root => root.RootKey == limit.Identity).RequiredInputs.IsEmpty &&
                explanation.RequiredInputs.Single().Input.Key == "earlier-pick",
                "A consumer limitation and its earlier choice must remain distinct origins, with no player duty invented for " + kind);
        }
    }

    private static void RecomputedChoiceDropsOldRoots()
    {
        var previous = new PredictorRouteFactRecorder();
        var choice = Choice("floor3", "first");
        var unknown = previous.RecordUnknown("deck", "floor6", PredictorRouteFactPhase.After, [choice]);
        var old = previous.FreezeFrames()["floor6"];
        var recomputed = new PredictorRouteFactRecorder();
        var known = recomputed.RecordValue("deck", "floor6", PredictorRouteFactPhase.After, "explicitly skipped",
            origin: PredictorKnowledgeOrigin.PlayerInput);
        var fresh = recomputed.FreezeFrames()["floor6"];
        Check(fresh.Snapshot.ForecastId != old.Snapshot.ForecastId && fresh.Snapshot.Roots.IsEmpty &&
            fresh.Snapshot.Explain(known).Roots.IsEmpty && old.Snapshot.Explain(unknown).Roots.Length == 1,
            "Changing the authored choice must build a fresh forecast without stale roots or mutation of the old graph");
    }
}
