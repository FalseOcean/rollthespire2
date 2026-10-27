using System.Collections;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.World;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Pages.Analysis;

internal static partial class UiSurfaceChecks
{
    private static async Task RunAncientPremiseChecksAsync(AnalysisPage page)
    {
        object Field(string name) => page.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        DictionaryEntry[] Entries(IDictionary dictionary) => dictionary.Keys.Cast<object>()
            .Select(key => new DictionaryEntry(key, dictionary[key])).ToArray();
        var request = page.LastRequest!;
        var document = page.LastDocument!;
        var maps = Entries((IDictionary)Field("_overviewMaps"));
        var routes = Entries((IDictionary)Field("_overviewRoutes"));
        var selection = page.PreferredOpeningRouteId;
        var mapSurface = page.FindChild("Act2OverviewMap", true, false);
        var editor = (AnalysisAncientGroupCard)Field("_ancientGroupCard");
        int analyses = 0, variants = 0;
        void Analyze(AnalysisRequestDraft _) => analyses++;
        page.AnalyzeRequested += Analyze;
        try
        {
            foreach (var entry in Entries((IDictionary)Field("_ancientPremiseVariants")))
            {
                var table = entry.Value!;
                var keys = (RolltheSpire2.Core.Identity.ModelKey[])table.GetType().GetProperty("Keys")!.GetValue(table)!;
                for (int mask = 0; mask < 1 << keys.Length; mask++)
                {
                    var conditions = request.AncientOptionConditions;
                    for (int i = 0; i < keys.Length; i++) conditions = conditions.WithOptionEligibility(keys[i], (mask & (1 << i)) != 0);
                    // Exercise the real editor event, not the private cache selector.
                    editor.SetConditions(conditions, notify: true);
                    var expected = WorldPredictionEngine.Predict(Beta111Profile.Instance, document.CanonicalSeed,
                        request.Ascension, request.PlayerSlotIndex, request.Character.CharacterKey,
                        request.Authority.WorldAuthority, request.Authority.EffectAuthority, conditions);
                    var actual = page.LastDocument!.Sections.SelectMany(s => s.Ancients).ToArray();
                    if (JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(expected.Ancients) ||
                        page.LastRequest!.AncientOptionConditions != conditions ||
                        !ReferenceEquals(page.LastRequest.Authority, request.Authority))
                        throw new Exception("Ancient premise cache differs from fresh World prediction");
                    if (document.Sections.Where(s => s.Ancients.Count == 0).Any(s => !page.LastDocument.Sections.Contains(s)) ||
                        maps.Any(m => !ReferenceEquals(((IDictionary)Field("_overviewMaps"))[m.Key], m.Value)) ||
                        routes.Any(r => !ReferenceEquals(((IDictionary)Field("_overviewRoutes"))[r.Key], r.Value)) ||
                        selection != page.PreferredOpeningRouteId || analyses != 0 ||
                        !ReferenceEquals(mapSurface, page.FindChild("Act2OverviewMap", true, false)))
                        throw new Exception("Ancient premise editing invalidated independent observations");
                    variants++;
                }
            }
            if (variants == 0) throw new Exception("Fixture did not cover Ancient premise alternatives");
            editor.SetConditions(request.AncientOptionConditions, notify: true);
            // Hold an existing map result until after a premise revision. The
            // async surface must still bind it without a fresh prediction.
            var mapCache = (IDictionary)Field("_overviewMaps");
            var originalMap = (Task<MapPrediction>)mapCache[2]!;
            var pending = new TaskCompletionSource<MapPrediction>(TaskCreationOptions.RunContinuationsAsynchronously);
            mapCache[2] = pending.Task;
            page.GetType().GetMethod("RenderWorkbench", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null);
            var revision = page.LastRequest!.RequestId;
            editor.SetConditions(request.AncientOptionConditions with { PaelGoopyDefendCardsAtLeast3 = false }, notify: true);
            pending.SetResult(await originalMap);
            for (int frame = 0; frame < 3; frame++) await page.ToSignal(page.GetTree(), Godot.SceneTree.SignalName.ProcessFrame);
            if (page.LastRequest.RequestId == revision || page.FindChild("Act2OverviewMap", true, false).GetChildCount() == 0 || analyses != 0)
                throw new Exception("Premise revision discarded an in-flight map");
            mapCache[2] = originalMap;
            editor.SetConditions(request.AncientOptionConditions, notify: true);
            Godot.GD.Print($"ANCIENT_PREMISES_PASS variants={variants};fullAnalyses={analyses};authorityAndMapsReused=true;openingPreserved=true;inFlightMap=true");
        }
        finally { page.AnalyzeRequested -= Analyze; }
    }
}
