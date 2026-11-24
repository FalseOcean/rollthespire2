using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;

internal static class InRunMapDiagnosticsFormatterChecks
{
    private sealed class Text : IUiTextProvider
    {
        public string LanguageCode => "en";
        public string Get(string key) => key;
        public bool TryGet(string key, out string value) { value = ""; return false; }
        public string Format(string key, params object?[] args) => key + ": " + string.Join(", ", args);
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    internal static void Run()
    {
        var trace = new PredictorKnowledgeTrace();
        var text = new Text();
        var first = trace.AddRoot("first-choice", "floor3", "take-first",
            PredictorKnowledgeRootKind.UserChoice, "same-description", [new("deck-choice", "choose or explicitly skip")]);
        var second = trace.AddRoot("second-choice", "floor4", "take-second",
            PredictorKnowledgeRootKind.UserChoice, "same-description", [new("deck-choice", "choose or explicitly skip")]);
        var inputs = trace.Record("capture", "floor3", "before", [],
            new PredictorKnowledgeWrite("gold", PredictorKnowledgeStatus.Known, PredictorKnowledgeOrigin.Captured, "safe-gold"),
            new PredictorKnowledgeWrite("deck", PredictorKnowledgeStatus.Partial, PredictorKnowledgeOrigin.Derived, "safe-subset",
                DirectRootIds: [first], ProvenDetails: [new("minimum", "five guaranteed cards")]),
            new PredictorKnowledgeWrite("potion", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived, DirectRootIds: [second]));
        var middle = trace.Record("candidate-filter", "floor6", "after", [inputs[1]],
            new PredictorKnowledgeWrite("candidate-count", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived,
                UnresolvedReadFactIds: [inputs[1]]))[0];
        var result = trace.Record("reward-generation", "floor9", "after", [middle, inputs[2]],
            new PredictorKnowledgeWrite("rng.niche.offset", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived,
                UnresolvedReadFactIds: [middle, inputs[2]]))[0];
        var snapshot = trace.Snapshot();
        var known = InRunMapNodeDiagnosticsFormatter.Fact(snapshot, inputs[0], text);
        var partial = InRunMapNodeDiagnosticsFormatter.Fact(snapshot, inputs[1], text);
        Check(known.Text.Contains("safe-gold") && !known.CanExplain && partial.CanExplain &&
            partial.Text.Contains("safe-subset") && partial.Text.Contains("five guaranteed cards") &&
            known.Text.Contains("origin.Captured"), "Formatter must display established facts and their provenance");
        // Defense in depth: even a malformed imported snapshot must not expose
        // an Unknown field's witness value or structured payload to the view.
        var unsafeFacts = snapshot.Facts.Select(f => f.Id == result ? f with
            { DisplayValue = "unsafe-witness", ProvenDetails = [new("guess", "unsafe-detail")] } : f).ToImmutableArray();
        var malformed = new PredictorKnowledgeSnapshot(snapshot.ForecastId, unsafeFacts, snapshot.Roots, snapshot.Operations);
        var unknown = InRunMapNodeDiagnosticsFormatter.Fact(malformed, result, text);
        Check(unknown.CanExplain && !unknown.Text.Contains("unsafe-witness") && !unknown.Text.Contains("unsafe-detail"),
            "Unknown display cannot fall back to a witness payload");
        var operations = InRunMapNodeDiagnosticsFormatter.Operations(snapshot, "floor9", text);
        Check(operations.Contains("reward-generation") && operations.Contains("F" + middle.Ordinal) &&
            operations.Contains("candidate-count") && operations.Contains("rng.niche.offset") &&
            !operations.Contains("candidate-filter"), "Operation audit must retain actual reads/writes and node scope");
        var explanation = InRunMapNodeDiagnosticsFormatter.Explain(snapshot, result, text);
        Check(explanation.Contains("R" + first.Ordinal) && explanation.Contains("R" + second.Ordinal) &&
            explanation.Contains("floor3") && explanation.Contains("floor4") && explanation.Contains("floor6") &&
            explanation.Contains("take-first") && explanation.Contains("take-second") &&
            explanation.Contains("candidate-count") && explanation.Contains("deck-choice") &&
            explanation.Contains("choose or explicitly skip") && explanation.Contains("→"),
            "Explanation must show independent original identities, intermediate facts, edges and required input");
        Check(!explanation.Contains("unsafe-witness") &&
            InRunMapNodeDiagnosticsFormatter.Explain(snapshot, inputs[0], text).Contains("no_unresolved_dependency"),
            "An independent known fact must not acquire another field's roots");
        var consumption = trace.Record("consume-rng", "floor9", "after", [middle],
            new PredictorKnowledgeWrite("rng.Niche.consumed", PredictorKnowledgeStatus.Partial, PredictorKnowledgeOrigin.Derived,
                "safe-consumption-enclosure", UnresolvedReadFactIds: [middle],
                ProvenDetails: [new("range-kind", "conservative enclosure")]),
            new PredictorKnowledgeWrite("rng.Niche.calls", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived,
                UnresolvedReadFactIds: [middle]))[0];
        var causeOnly = trace.Record("dependency:random-source", "floor9", "dependency", [],
            new PredictorKnowledgeWrite("rng.Niche.consumed", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived,
                DirectRootIds: [first]))[0];
        snapshot = trace.Snapshot();
        var frame = new PredictorNodeDiagnostics(snapshot, [], [consumption], [causeOnly]);
        Check(InRunMapNodeDiagnosticsFormatter.RngConsumption(frame).SequenceEqual([consumption]) &&
            InRunMapNodeDiagnosticsFormatter.Fact(snapshot, consumption, text).Text.Contains("safe-consumption-enclosure"),
            "RNG consumption must select After frame facts, excluding dependency-only properties without parsing call-count displays");
        var cause1 = PredictorRouteCauseEvidence.Bind(new PredictorCombatPendingCause("PendingCardChoice",
            "same-wording", PredictorCombatDomain.Deck) { NodeId = "floor3", ChoiceId = "first-card" }, occurrenceKey: "first");
        var cause2 = PredictorRouteCauseEvidence.Bind(new PredictorCombatPendingCause("PendingCardChoice",
            "same-wording", PredictorCombatDomain.Deck) { NodeId = "floor4", ChoiceId = "second-card" }, occurrenceKey: "second");
        var wrapper = PredictorRouteCauseEvidence.CreateExplicit("RestDeckDependentAction", "floor9",
            PredictorCombatDomain.Continuation, PredictorPendingReasonKind.DerivedDependency,
            "The result depends on earlier choices.", ["result"], [cause1, cause2]);
        var pending = InRunMapNodeDiagnosticsFormatter.Pending([wrapper, cause1], text);
        Check(pending.Length == 2 && pending[0].Identity != pending[1].Identity &&
            pending.Any(p => p.Text.Contains("floor3") && p.Text.Contains("first-card")) &&
            pending.Any(p => p.Text.Contains("floor4") && p.Text.Contains("second-card")),
            "Ordinary pending rows must deduplicate original identities and retain both same-wording causes");
        var missing = new PredictorCombatPendingCause("UnregisteredCause", "Original diagnostic survives missing text",
            PredictorCombatDomain.Continuation) { NodeId = "floor9", Identity = "missing-translation" };
        foreach (string language in new[] { "zh", "en" })
        {
            var localized = JsonUiTextProvider.CreatePredictorUi13(language);
            Check(InRunMapNodeDiagnosticsFormatter.Pending([missing], localized).Single().Text.Contains(missing.Description),
                "Actual JSON missing-marker behavior must retain the original pending diagnostic");
            var cutoff = PredictorRouteCauseEvidence.CreateExplicit("RouteRelicDeckRandomnessCutoff", "floor9",
                PredictorCombatDomain.NicheRng, PredictorPendingReasonKind.FlowGate, "raw-cutoff-diagnostic",
                ["rng.Niche.state"]);
            Check(localized.TryGet("predictor.runtime.plan.pending.RouteRelicDeckRandomnessCutoff", out var cutoffLabel) &&
                InRunMapNodeDiagnosticsFormatter.Pending([cutoff], localized).Single().Text.Contains(cutoffLabel),
                "Both languages must provide the player-facing relic randomness cutoff reason");
            var causeTrace = new PredictorKnowledgeTrace();
            var causeRoot = causeTrace.AddRoot("missing-reason", "floor9", "choose-relic", PredictorKnowledgeRootKind.UserChoice,
                missing.Description, [new("relic-choice", "choose or skip the relic")]);
            var causeFact = causeTrace.Record("derive", "floor9", "after", [],
                new PredictorKnowledgeWrite("result", PredictorKnowledgeStatus.Unknown, PredictorKnowledgeOrigin.Derived,
                    DirectRootIds: [causeRoot]))[0];
            Check(InRunMapNodeDiagnosticsFormatter.Explain(causeTrace.Snapshot(), causeFact, localized).Contains(missing.Description),
                "Detailed explanation must preserve an unknown raw cause rather than the missing translation marker");
        }
        foreach (var kind in new[] { PredictorPendingReasonKind.ImplementationGap, PredictorPendingReasonKind.FlowGate,
                     PredictorPendingReasonKind.PlanBinding, PredictorPendingReasonKind.KnownUnavailable })
        {
            var limitation = PredictorRouteCauseEvidence.CreateExplicit("LimitedReader", "floor9",
                PredictorCombatDomain.Continuation, kind, "Consumer has its own limitation.", ["result"], [cause1]);
            var rows = InRunMapNodeDiagnosticsFormatter.Pending([limitation], text);
            Check(rows.Length == 2 && rows.Single(row => row.Identity == limitation.Identity).Text.StartsWith(
                    "predictor.map.diagnostics.kind." + kind + ": ", StringComparison.Ordinal) &&
                rows.Single(row => row.Identity == cause1.Identity).Text.Contains("kind.UserChoice") &&
                rows.Single(row => row.Identity == cause1.Identity).Text.Contains("floor3") &&
                PredictorRouteCauseEvidence.RequiredInputs(limitation).IsEmpty,
                "Ordinary Pending must show the classified consumer limit alongside its original choice without inventing repair duties");
        }
        Console.WriteLine("IN_RUN_MAP_DIAGNOSTICS_FORMATTER_PASS safe values; operation reads/writes; original roots; shared causal graph");
    }
}
