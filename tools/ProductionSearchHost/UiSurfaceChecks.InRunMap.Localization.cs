using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.Settings;

internal static partial class UiSurfaceChecks
{
    private static async Task RunInRunMapLocalizationSmoke(Node root, NMapScreen screen,
        InRunMapPredictionPanel panel, MapPosition point, NMapPoint nativePoint,
        Func<string> signature, Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var session = (InRunMapPredictionSession)DiagnosticField(panel, "_prediction");
        var presenter = (InRunMapNodePresenter)DiagnosticField(panel, "_presenter");
        var details = (InRunMapNodeDetails)DiagnosticField(panel, "_details");
        string previousOverride = InRunFeaturePreferences.LanguageOverride;
        string runtimeDirectory = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state");
        var refresh = panel.GetType().GetMethod("RefreshLocalization", members)!;
        string before = signature(), plans = JsonSerializer.Serialize(session.Plans);
        var snapshot = session.Snapshot; var forecast = session.Forecast; var nodes = session.Nodes;
        long revision = session.Revision; int history = session.HistoryCount;
        var draft = panel.DisplayedDraft!; var selected = draft.Selected;
        var nativeDrawing = DiagnosticField(panel, "_nativeDrawing");
        var map = screen.GetNode<Control>("TheMap"); var mapPosition = map.Position;
        var localFields = new[] { "_selections", "_health", "_eventPotionDrafts", "_diagnosticsExpanded", "_diagnosticExplanations" }
            .ToDictionary(name => name, name => DiagnosticField(presenter, name));
        bool panelProcessing = panel.IsProcessing(), detailsProcessing = details.IsProcessing();
        panel.SetProcess(true); details.SetProcess(false);
        try
        {
            details.ShowNode(presenter.Detail((0, point))!, nativePoint.GetGlobalRect(), screen.GetGlobalRect());
            string nodeId = details.HoverNodeId!;
            foreach (string language in new[] { "zh", "en" })
            {
                InRunFeaturePreferences.SetLanguage(runtimeDirectory, language);
                if (language == "en")
                    panel.GetType().GetMethod("RebindRun", members)!.Invoke(panel, [DiagnosticField(panel, "_run")]);
                await DiagnosticFrame(root);
                var localized = JsonUiTextProvider.CreatePredictorUi13(language);
                check(panel.PredictionEnabled && panel.FindChild("PredictionToggle", true, false) == null &&
                        details.HoverNodeId == nodeId && presenter.Detail((0, point))!.PremisesLabel == localized.Get("predictor.map.reward_influences"),
                    "default-enabled map and open node inputs refresh without reintroducing mode/status chrome in " + language);
                check(ReferenceEquals(session, DiagnosticField(panel, "_prediction")) &&
                    ReferenceEquals(presenter, DiagnosticField(panel, "_presenter")) &&
                    ReferenceEquals(details, DiagnosticField(panel, "_details")) &&
                    ReferenceEquals(snapshot, session.Snapshot) && ReferenceEquals(forecast, session.Forecast) &&
                    ReferenceEquals(nodes, session.Nodes) && session.Revision == revision && session.HistoryCount == history &&
                    JsonSerializer.Serialize(session.Plans) == plans && ReferenceEquals(draft, panel.DisplayedDraft) &&
                    draft.Selected.SequenceEqual(selected) &&
                    localFields.All(pair => ReferenceEquals(pair.Value, DiagnosticField(presenter, pair.Key))) &&
                    ReferenceEquals(nativeDrawing, DiagnosticField(panel, "_nativeDrawing")) &&
                    map.Position == mapPosition && signature() == before &&
                    !(bool)DiagnosticField(panel, "_predictionQueued"),
                    "language refresh preserves route/plan/history/local drafts, native state and forecast without scheduling recomputation");
                var viewText = string.Join("\n", Descendants(details).OfType<Label>().Select(label => label.Text));
                check(!viewText.Contains("Missing translation", StringComparison.Ordinal),
                    "translated existing map hover contains no missing resource marker");
            }
            details.ShowEditor(presenter.DiagnosticView((0, point))!, screen.GetGlobalRect());
            InRunFeaturePreferences.SetLanguage(runtimeDirectory, "zh");
            await DiagnosticFrame(root);
            check(details.EditorNodeId == nodeId && ((Button)details.FindChild("EditorClose", true, false)!).Text ==
                JsonUiTextProvider.CreatePredictorUi13("zh").Get("predictor.map.close_editor") && session.Revision == revision,
                "open editor refreshes its translated controls without losing its node or recomputing");
            details.CloseEditor();
        }
        finally
        {
            InRunFeaturePreferences.SetLanguage(runtimeDirectory, previousOverride);
            refresh.Invoke(panel, null);
            details.CloseHover(); details.CloseEditor();
            panel.SetProcess(panelProcessing); details.SetProcess(detailsProcessing);
        }
    }
}
