using System.Collections.Immutable;
using System.Reflection;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;

// The production forecast comes from a real PreviewRouteContinuation call.
// Only the session's native-capture installation is bypassed in this pure UI
// data check; the presenter and diagnostic action handlers are unmodified.
internal static class PredictorTerminalBoundaryUiChecks
{
    private sealed class Text : IUiTextProvider
    {
        public string LanguageCode => "en";
        public bool TryGet(string key, out string value) { value = string.Empty; return false; }
        public string Get(string key) => key;
        public string Format(string key, params object?[] args) => key + ": " + string.Join(", ", args);
    }
    private sealed class Names : IGameContentNameResolver
    {
        public string LanguageCode => "en";
        public string Resolve(ModelKey key, GameContentKind kind) => key.Serialized;
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    internal static void Run(PredictorRouteContinuation blocked, PredictorRouteContinuation resolved)
    {
        var terminal = blocked.TerminalBoundary!;
        var session = new InRunMapPredictionSession();
        void Install(PredictorRouteContinuation forecast, long revision)
        {
            const BindingFlags member = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string property, object value) => typeof(InRunMapPredictionSession).GetProperty(property, member)!.SetValue(session, value);
            Set(nameof(InRunMapPredictionSession.Forecast), forecast);
            Set(nameof(InRunMapPredictionSession.Nodes), forecast.Nodes.ToImmutableDictionary(node =>
            {
                PredictorMapNodeIdentity.TryParse(node.Position.NodeId, out var id);
                return (id.Act - 1, id.Point);
            }));
            Set(nameof(InRunMapPredictionSession.TerminalBoundaries), InRunMapPredictionSession.TerminalLocations(forecast));
            Set(nameof(InRunMapPredictionSession.Revision), revision);
        }
        Install(blocked, 1);
        var location = session.TerminalBoundaries.Single().Key;
        Check(session.Nodes.IsEmpty && session.TerminalBoundaries[location] == terminal,
            "The UI session fabricated an ordinary node for a terminal entry boundary.");
        Check(!session.SetPlan(location.ActIndex, location.Point, new(terminal.TargetPosition.NodeId)),
            "An unentered terminal became an actionable room plan.");
        var presenter = new InRunMapNodePresenter(session, new Names(), new Text());
        var detail = presenter.Detail(location);
        var editor = presenter.Editor(location);
        Check(detail?.NodeId == terminal.TargetPosition.NodeId && detail.EditorActions.Count == 0 && detail.DiagnosticsLabel == null &&
            detail.Information.Count > 0 &&
            editor?.NodeId == terminal.TargetPosition.NodeId && editor.Numbers.Count == 0 &&
            editor.Toggles.Count == 0 && editor.Selections.Count == 0,
            "A terminal must expose its actual entry cause without a player-facing diagnostics entrance; internal evidence remains available.");
        var expand = editor!.Sections.SelectMany(section => section.Choices).Single(choice =>
            choice.Label == "predictor.map.diagnostics.expand");
        Check(presenter.ApplyChoice(editor.NodeId, expand.Id), "The terminal's advanced diagnostic expansion was unreachable.");
        editor = presenter.Editor(location)!;
        var allowed = terminal.Diagnostics!.Results.Select(terminal.Diagnostics.Snapshot.Fact)
            .Single(fact => fact.PropertyKey == "route.entry.allowed");
        var reason = editor.Sections.Single(section => section.Title.EndsWith("route.entry.allowed", StringComparison.Ordinal))
            .Choices.Single();
        Check(presenter.ApplyChoice(editor.NodeId, reason.Id), "The actual terminal entry cause could not be explained.");
        editor = presenter.Editor(location)!;
        Check(editor.Sections.Any(section => section.Title == "predictor.map.diagnostics.explanation" &&
            section.Text.Contains("capture:departure", StringComparison.Ordinal)) &&
            !editor.Sections.Any(section => section.Title == "predictor.map.diagnostics.after"),
            "Terminal diagnostics lost the source root or displayed an after-entry frame.");
        var stale = editor.Sections.SelectMany(section => section.Choices).First().Id;
        Install(resolved, 2);
        Check(session.TerminalBoundaries.IsEmpty && !presenter.ApplyChoice(terminal.TargetPosition.NodeId, stale) &&
            terminal.Diagnostics.Snapshot.Explain(allowed.Id).Roots.Any(root => root.NodeId == "capture:departure"),
            "Recompute retained a stale terminal action or mutated the old immutable explanation.");
        var refreshed = presenter.Editor(location);
        Check(refreshed != null && !refreshed.Sections.Any(section => section.Title == "predictor.map.diagnostics.explanation"),
            "The ordinary entered node reused an explanation from the previous terminal snapshot.");
        Console.WriteLine("PREDICTOR_TERMINAL_BOUNDARY_UI_PASS actual first-entry forecast; separate terminal mapping; reachable expansion/roots; no after/room actions; stale action rejection");
    }
}
