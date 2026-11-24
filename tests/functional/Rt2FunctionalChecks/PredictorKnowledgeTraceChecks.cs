using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorKnowledgeTraceChecks
{
    internal static void Run()
    {
        ChoiceToNicheToResult();
        IndependentRootsAndProof();
        CauseKinds();
        ImmutableSnapshotsAndRecomputation();
        InvalidRecords();
        Console.WriteLine("PREDICTOR_KNOWLEDGE_TRACE_PASS causal DAG; independent roots; invariant proof; cause kinds; immutable snapshots; invalid reference rejection");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(message);
    }

    private static PredictorKnowledgeRootId Choice(PredictorKnowledgeTrace trace, string key, string node,
        string choice = "pick-card") => trace.AddRoot(key, node, choice,
            PredictorKnowledgeRootKind.UserChoice, "请选择卡牌", [new("card-choice", "所选卡牌或明确跳过")]);

    private static PredictorKnowledgeFactId Unknown(PredictorKnowledgeTrace trace, string node, string property,
        ImmutableArray<PredictorKnowledgeFactId> reads = default,
        ImmutableArray<PredictorKnowledgeRootId> roots = default) => trace.Record("derive", node, "after",
            reads, new PredictorKnowledgeWrite(property, PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived,
                UnresolvedReadFactIds: reads, DirectRootIds: roots))[0];

    private static void ChoiceToNicheToResult()
    {
        var trace = new PredictorKnowledgeTrace();
        var root = Choice(trace, "floor3:card", "floor3");
        var choice = Unknown(trace, "floor3", "deck.membership", roots: [root]);
        var candidates = Unknown(trace, "floor6", "upgrade.candidateCount", [choice]);
        var niche = Unknown(trace, "floor6", "rng.niche.offset", [candidates]);
        var result = Unknown(trace, "floor9", "cardReward.enchantment", [niche]);
        // Another branch shares candidateCount. Explanation retains one node,
        // both causal edges, and one original cause rather than copying paths.
        var alternate = Unknown(trace, "floor6", "upgrade.target", [candidates]);
        var diamond = Unknown(trace, "floor9", "inventory", [result, alternate]);
        var snapshot = trace.Snapshot();
        var explanation = snapshot.Explain(diamond);
        Check(explanation.Facts.Length == 6 && explanation.CausalEdges.Length == 6 &&
            explanation.Roots.Length == 1 && explanation.Roots[0].Id == root &&
            explanation.Roots[0].NodeId == "floor3" && explanation.Roots[0].ChoiceId == "pick-card",
            "Later facts must retain the original floor-3 choice through the shared dependency DAG");
        Check(explanation.Facts.Count(f => f.Id == candidates) == 1 &&
            explanation.RequiredInputs.Length == 1 && explanation.RequiredInputs[0].RootId == root &&
            explanation.RequiredInputs[0].NodeId == "floor3" && snapshot.Operations.Length == 6,
            "Shared paths must neither duplicate facts nor invent a later root or input origin");
        Check(snapshot.Fact(niche).AllReadFactIds.SequenceEqual([candidates]) &&
            snapshot.Fact(niche).UnresolvedReadFactIds.SequenceEqual([candidates]),
            "Actual reads and causally unresolved reads must be explicitly inspectable");
    }

    private static void IndependentRootsAndProof()
    {
        var trace = new PredictorKnowledgeTrace();
        var first = Choice(trace, "choice:first", "floor3", "first");
        var second = Choice(trace, "choice:second", "floor4", "second");
        Check(first != second && Choice(trace, "choice:first", "floor3", "first") == first,
            "Root identity is explicit: equal wording does not merge independent choices");
        var partial = trace.Record("project-count", "floor6", "before", [],
            new PredictorKnowledgeWrite("deck.count", PredictorKnowledgeStatus.Partial, PredictorKnowledgeOrigin.Derived,
                "至少五张", DirectRootIds: [first], ProvenDetails: [new("lower-bound", "五张确定保留")]))[0];
        var unrelated = Unknown(trace, "floor4", "potion.slot", roots: [second]);
        var produced = trace.Record("check-independent", "floor6", "after", [partial, unrelated],
            new PredictorKnowledgeWrite("can-smith", PredictorKnowledgeStatus.Known, PredictorKnowledgeOrigin.Derived,
                "可以", Derivation: "确定保留的五张牌中有可升级牌，药水槽不参与判断"),
            new PredictorKnowledgeWrite("deck.complete", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived,
                UnresolvedReadFactIds: [partial]));
        var snapshot = trace.Snapshot();
        Check(snapshot.Explain(produced[0]).Roots.IsEmpty &&
            snapshot.Fact(produced[0]).AllReadFactIds.Length == 2 &&
            snapshot.Fact(produced[0]).UnresolvedReadFactIds.IsEmpty,
            "A proven invariant must retain its read audit without inheriting unrelated uncertainty");
        Check(snapshot.Explain(produced[1]).Roots.Select(r => r.Id).SequenceEqual([first]),
            "Each output propagates only its explicitly unresolved dependencies");
        var both = Unknown(trace, "floor9", "combined", [partial, unrelated]);
        Check(trace.Snapshot().Explain(both).Roots.Length == 2 &&
            trace.Snapshot().Explain(both).RequiredInputs.Length == 2,
            "Identical descriptions and input keys must retain both independent root identities");
    }

    private static void CauseKinds()
    {
        var trace = new PredictorKnowledgeTrace();
        var kinds = Enum.GetValues<PredictorKnowledgeRootKind>();
        var roots = kinds.Select((kind, i) => trace.AddRoot("cause:" + i, "floor6",
            kind == PredictorKnowledgeRootKind.UserChoice ? "original-choice" : null, kind,
            "原因待处理", kind is PredictorKnowledgeRootKind.UserChoice or PredictorKnowledgeRootKind.MissingObservation
                ? [new("observation", "需要原始观测或明确选择")] : [])).ToImmutableArray();
        var fact = Unknown(trace, "floor9", "result", roots: roots);
        Check(trace.Snapshot().Explain(fact).Roots.Select(r => r.Kind).SequenceEqual(kinds),
            "Choice, missing observation, implementation gap and execution fault must remain distinct");
        var notObserved = trace.Record("capture", "floor6", "before", [],
            new PredictorKnowledgeWrite("enemy.count", PredictorKnowledgeStatus.NotObserved, PredictorKnowledgeOrigin.Captured,
                DirectRootIds: [roots[1]]))[0];
        Check(trace.Snapshot().Fact(notObserved).DisplayValue == null,
            "An unobserved value must not acquire a witness display value");
    }

    private static void ImmutableSnapshotsAndRecomputation()
    {
        var trace = new PredictorKnowledgeTrace();
        var root = Choice(trace, "choice", "floor3");
        var pending = Unknown(trace, "floor6", "candidate-count", roots: [root]);
        var before = trace.Snapshot();
        _ = Unknown(trace, "floor9", "result", [pending]);
        Check(before.Facts.Length == 1 && before.Operations.Length == 1 && before.Roots.Length == 1 &&
            before.Explain(pending).Roots[0].Id == root && trace.Snapshot().Facts.Length == 2,
            "Appending records must not mutate an already published immutable snapshot");
        var recomputed = new PredictorKnowledgeTrace();
        var known = recomputed.Record("player-choice", "floor3", "after", [],
            new PredictorKnowledgeWrite("card-choice", PredictorKnowledgeStatus.Known, PredictorKnowledgeOrigin.PlayerInput, "明确跳过"))[0];
        var next = recomputed.Snapshot();
        Check(next.ForecastId != before.ForecastId && next.Roots.IsEmpty && next.Explain(known).Roots.IsEmpty &&
            before.Explain(pending).Roots.Length == 1,
            "A fresh recomputation must discard stale roots without modifying the old graph");
        Reject(() => Unknown(recomputed, "floor9", "stale", [pending]),
            "Facts from another forecast must not be accepted");
        Reject(() => Unknown(recomputed, "floor9", "stale-root", roots: [root]),
            "Roots from another forecast must not be accepted");
    }

    private static void InvalidRecords()
    {
        var trace = new PredictorKnowledgeTrace();
        Reject(() => Unknown(trace, "floor3", "unsupported"), "Unknown needs a declared cause");
        Reject(() => Unknown(trace, "floor3", "dangling", [new(trace.ForecastId, 1)]),
            "Forward or self fact references must be rejected");
        Reject(() => Unknown(trace, "floor3", "dangling-root", roots: [new(trace.ForecastId, 1)]),
            "Dangling root references must be rejected");
        var root = Choice(trace, "choice", "floor3");
        Reject(() => Choice(trace, "choice", "floor4"), "A root key cannot be rebound to a later node");
        Reject(() => trace.AddRoot("choice", "floor3", "pick-card",
            PredictorKnowledgeRootKind.ExecutionFault, "请选择卡牌"),
            "An existing root identity cannot change cause classification");
        var input = Unknown(trace, "floor3", "deck", roots: [root]);
        Reject(() => trace.Record("fake-known", "floor6", "after", [],
            new PredictorKnowledgeWrite("known", PredictorKnowledgeStatus.Known, PredictorKnowledgeOrigin.Derived, "五",
                DirectRootIds: [root])), "Known output cannot retain unresolved root causes");
        Reject(() => trace.Record("guess", "floor6", "after", [input],
            new PredictorKnowledgeWrite("known", PredictorKnowledgeStatus.Known, PredictorKnowledgeOrigin.Derived, "五")),
            "Known reads of partial/unknown state require an explicit invariance proof");
        Reject(() => trace.Record("guess", "floor6", "after", [input],
            new PredictorKnowledgeWrite("unknown", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived,
                "五", UnresolvedReadFactIds: [input])), "Unknown cannot expose a guessed payload");
        Reject(() => trace.Record("unread", "floor6", "after", [],
            new PredictorKnowledgeWrite("unknown", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived,
                UnresolvedReadFactIds: [input])), "Unresolved references must be actual reads");
        var before = trace.Snapshot();
        Reject(() => trace.Record("atomic", "floor6", "after", [],
            new PredictorKnowledgeWrite("first", PredictorKnowledgeStatus.Known, PredictorKnowledgeOrigin.ApprovedDefault, "零"),
            new PredictorKnowledgeWrite("second", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived)),
            "A bad later output must reject the whole operation");
        var after = trace.Snapshot();
        Check(before.Facts.SequenceEqual(after.Facts) && before.Operations.SequenceEqual(after.Operations),
            "Rejected operations must not consume fact IDs or publish partial writes");
    }
}
