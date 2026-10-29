using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunMapWitnessChecksAsync(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, flags)!.GetValue(value)!;
        void Set(object value, string name, object data) => value.GetType().GetField(name, flags)!.SetValue(value, data);
        void Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, flags)!.Invoke(value, args);
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException("MAP_WITNESS " + why); }
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string why)
        {
            for (int i = 0; i < 1200 && !ready(); i++) await Frames(1);
            Check(ready(), why);
        }
        async Task Click(Control control)
        {
            Check(control.IsVisibleInTree(), "native action visible " + control.Name);
            var position = control.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion { Position = position, GlobalPosition = position });
            Input.FlushBufferedEvents(); await Frames(1);
            Input.ParseInputEvent(new InputEventMouseButton { Position = position, GlobalPosition = position,
                ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
            Input.FlushBufferedEvents(); await Frames(1);
            Input.ParseInputEvent(new InputEventMouseButton { Position = position, GlobalPosition = position,
                ButtonIndex = MouseButton.Left, Pressed = false });
            Input.FlushBufferedEvents(); await Frames();
        }
        IEnumerable<Node> Nodes(Node value)
        { yield return value; foreach (Node child in value.GetChildren()) foreach (Node descendant in Nodes(child)) yield return descendant; }
        // This scenario needs ordinary editable maps as well as the independent
        // witness view. An empty new-profile mock has no complete event-pool Act
        // rows, so establish a declared vanilla account before capturing any
        // original search authority/context. No predicted rows/maps are injected.
        var save = MegaCrit.Sts2.Core.Saves.SaveManager.Instance;
        var originalProgress = save.Progress;
        var fixtureProgress = originalProgress.ToSerializable();
        fixtureProgress.CharStats = [new() { Id = new("CHARACTER", "IRONCLAD"), TotalLosses = 1 }];
        fixtureProgress.Epochs = MegaCrit.Sts2.Core.Timeline.EpochModel.AllEpochIds.Select(id =>
            new MegaCrit.Sts2.Core.Saves.SerializableEpoch(id, MegaCrit.Sts2.Core.Saves.EpochState.Revealed)).ToList();
        fixtureProgress.DiscoveredActs = new[] { "OVERGROWTH", "UNDERDOCKS", "HIVE", "GLORY" }
            .Select(entry => new MegaCrit.Sts2.Core.Models.ModelId("ACT", entry)).ToList();
        var progressValidation = new MegaCrit.Sts2.Core.Saves.Validation.DeserializationContext();
        save.Progress = MegaCrit.Sts2.Core.Saves.ProgressState.FromSerializable(fixtureProgress, progressValidation);
        Check(!progressValidation.HasFatal && save.GenerateUnlockStateFromProgress().NumberOfRuns == 1, "declared vanilla progression captures one normal run");
        RuntimeAuthorityEnvironment.CaptureOnMainThread(runtime);
        var host = new Control { Size = new(1600, 900) }; root.AddChild(host);
        var shell = new WorkspaceShell();
        shell.InitializeWithPersistence(runtime, Path.Combine(output, "map-witness-state", Guid.NewGuid().ToString("N")));
        host.AddChild(shell);
        try
        {
            shell.Open(); await AwaitPageReady(root, shell); await Frames();
            var frame = Field<QueryWorkbenchFrame>(shell, "_references");
            var store = Field<SearchWorkspace>(shell, "_persistence");
            const string seed = "000000000000";
            var generated = Beta111StandardMapGenerator.GenerateStandardMap(seed, StandardMapFamily.Context(1, 1));
            var path = new List<MapNode>();
            for (var node = generated.Start; ; node = node.Children.First())
            { path.Add(node); if (node == generated.Boss) break; }
            var rooms = path.Where(node => node.CanBeModified && node.PointType is MapPointType.Monster or MapPointType.Elite or
                MapPointType.RestSite or MapPointType.Shop or MapPointType.Unknown).Take(3)
                .Select(node => Enum.Parse<StandardMapRoomType>(node.PointType.ToString())).ToArray();
            var initial = frame.CaptureDraft();
            var draft = new WorkbenchSearchDraft(initial.Character, 1, SearchQuery.Empty with
            { StandardMapRoutes = [new(1, [new(rooms)])] }, initial.AncientPremises);
            var compiled = draft.Compile(runtime, out _);
            var plan = ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 1, 1, 1)).Plan!;
            var exact = ProductionExactSearchEvaluator.Evaluate(plan, TrustedRootHashInput.FromCanonicalSeed(runtime.Profile, seed));
            Check(exact.IsMatch && exact.Document is not null && exact.Request is not null, "bounded Production Exact fixture matches");
            var candidate = new SearchCandidate(seed, plan.ProfileId, plan.CharacterKey, plan.Ascension, plan.SnapshotFingerprint,
                exact.Request!, exact.Document!, exact.Authority, exact.Evaluation.Evidence, exact.Evaluation.MatchedRouteIds, exact.Evaluation.Witnesses);
            var prepared = store.PrepareSearchStart(draft, plan, SeedLibraryContextCapture.ForSearch(draft, runtime));
            store.BindPreparedResultWorkspace(prepared);
            store.AppendResult(candidate, compiled.SemanticFingerprint);
            var result = store.DisplayResults.Single();
            Check(result.MapRouteWitnesses.Count == 1, "result projection retains Exact map receipt");
            var receipt = result.MapRouteWitnesses.Single();
            var json = SeedLibraryStore.CreateJsonOptions();
            var saved = JsonSerializer.Deserialize<PersistedSearchResult>(JsonSerializer.Serialize(result, json), json)!;
            Check(saved.MapRouteWitnesses.Single() == receipt, "persisted result retains raw Act/RouteId/OutcomeId");
            Check(JsonSerializer.Deserialize<PersistedSearchResult>("{\"Seed\":\"000000000000\"}", json)!.MapRouteWitnesses.Count == 0,
                "legacy results retain absent-witness state");
            // Deliberately change today's editor before invoking the original result action.
            frame.RestoreDraft(draft with { Query = SearchQuery.Empty with { StandardMapRoutes =
                [new(2, [new([StandardMapRoomType.Shop])])] } }, render: false);
            Set(frame, "_showResults", true); Call(frame, "RenderResults"); Call(frame, "LockForRun", false); await Frames();
            var analyze = Nodes(frame).OfType<Button>().Single(button => button.Text == JsonUiTextProvider.CreateUi13(store.Preferences.LanguageOverride).Get("integration.analyze"));
            await Click(analyze);
            var page = Field<AnalysisPage>(shell, "_predictor");
            var controller = Field<AnalysisPageController>(shell, "_predictorController");
            await Until(() => page.LastDocument?.CanonicalSeed == seed, "original result opens Analysis");
            var open = (Button)page.FindChild("OpenMapWitness1", true, false);
            Check(!open.Disabled, "original Act 1 receipt remains available after editor changed to Act 2");
            var tabs = Field<List<Button>>(page, "_wbTabs");
            await Click(tabs[1]);
            Check(!((Control)page.FindChild("MapWitnessEntry2", true, false)).Visible &&
                ((Label)page.FindChild("MapWitnessStatus2", true, false)).Text.Length == 0,
                "browsing Act 2 does not claim that the original Act 1 witness expired");
            await Frames(); RenderingServer.ForceDraw();
            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "map-witness-other-act.png"));
            await Click(tabs[0]);
            open = (Button)page.FindChild("OpenMapWitness1", true, false);
            Check(!open.Disabled, "returning to the receipt's Act retains its valid entry");
            await Click(open);
            var workspace = Field<MapWorkspace>(page, "_mapWorkspace");
            await Until(() => workspace.IsWitnessPreview || Field<string>(workspace, "_witnessIssue").Length > 0, "witness opens directly from original result");
            Check(workspace.IsWitnessPreview, "witness entry has no ordinary-overview prerequisite");
            workspace.Close(); await Frames();
            var overviewRoutes = Field<Dictionary<int, MapRouteCommitment>>(page, "_overviewRoutes");
            var eventDomain = Field<RolltheSpire2.Presentation.Ui1.SeedAnalysisViewModel>(page, "_viewModel").EventPoolSequenceDomain;
            Check(eventDomain.Items.Any(item => item.Act == 1), "declared account supplies ordinary Act map context;domain=" + eventDomain);
            await Until(() => page.FindChild("MapMaximumElite", true, false) is Button { Disabled: false }, "overview map ready");
            await Click((Button)page.FindChild("MapMaximumElite", true, false));
            var overviewCommitment = overviewRoutes[1];
            Call(page, "OnMapRequested", 1, open); await Frames();
            await Until(() => Field<MapPrediction?>(workspace, "_routeMap") is not null, "MapWorkspace loads result map");
            ((Button)workspace.FindChild("RouteExtremumElite", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            ((Button)workspace.FindChild("ConfirmRoute", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            var commitment = workspace.CurrentCommitment;
            Check(commitment is not null, "existing player commitment fixture");
            workspace.Close(); await Frames();
            await Click(open);
            await Until(() => workspace.IsWitnessPreview || Field<string>(workspace, "_witnessIssue").Length > 0, "receipt preview completed");
            Check(workspace.IsWitnessPreview, "Exact receipt previews without substitution;issue=" + Field<string>(workspace, "_witnessIssue"));
            var displayedMap = Field<MapPrediction>(workspace, "_routeMap");
            Check(workspace.PreviewRoute!.SequenceEqual(MapRouteWitnessProjection.Read(receipt, seed, displayedMap)), "highlight is original full witness path");
            Check(displayedMap.Act.Entry == "OVERGROWTH" && displayedMap.Points.Count(point => point.Type == MapPointType.Boss) == 1,
                "witness graph uses Exact standard context");
            var variant = Beta111MapPredictor.Predict(seed, plan.ProfileId, new("ACT", "UNDERDOCKS"), 0, plan.Ascension, 1, true, default);
            bool variantRejected = false;
            try { MapRouteWitnessProjection.Read(receipt, seed, variant); }
            catch (InvalidOperationException exception) when (exception.Message == "MapWitness.NonStandardGraph") { variantRejected = true; }
            Check(variantRejected, "variant/extra-Boss graph cannot substitute for original Exact graph");
            Check(ReferenceEquals(workspace.CurrentCommitment, commitment) && ReferenceEquals(overviewRoutes[1], overviewCommitment),
                "witness preview preserves both existing route selections");
            Check(((Button)workspace.FindChild("ConfirmRoute", true, false)).Disabled, "witness preview cannot become an implicit player commitment");
            foreach (string language in new[] { "zh", "en" })
            {
                store.SetLanguageOverride(language); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                workspace.ApplyLocalization(JsonUiTextProvider.CreatePredictorUi13(language)); await Frames(); RenderingServer.ForceDraw();
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "map-witness-" + language + ".png"));
                Check(Field<Label>(workspace, "_routeState").Text == JsonUiTextProvider.CreatePredictorUi13(language).Get("ui1.map.witness.preview"),
                    "localized witness state " + language);
            }
            await Click((Button)workspace.FindChild("MapAct2", true, false));
            Check(!workspace.IsWitnessPreview && workspace.PreviewRoute is null, "Act switch immediately clears witness preview");
            workspace.SelectAct(1); await Until(() => Field<MapPrediction?>(workspace, "_routeMap")?.ActIndex == 0, "Act 1 reloads");
            Check(!workspace.IsWitnessPreview && workspace.PreviewRoute is null && ReferenceEquals(workspace.CurrentCommitment, commitment),
                "returning Act restores only player commitment");
            var malformed = receipt with { RouteId = "act-1:broken" };
            workspace.PreviewResultWitness(malformed, seed);
            Check(!workspace.IsWitnessPreview && Field<string>(workspace, "_witnessIssue").StartsWith("MapWitness.CoordinateFormatInvalid", StringComparison.Ordinal) &&
                malformed.RouteId == "act-1:broken" && ReferenceEquals(workspace.CurrentCommitment, commitment), "parser failure preserves raw evidence/cause and commitment");
            workspace.Close();
            controller.OpenSavedSearchResult(saved); await Frames();
            open = (Button)page.FindChild("OpenMapWitness1", true, false);
            Check(!open.Disabled, "saved result reopens its own receipt/context");
            await Click(open); await Until(() => workspace.IsWitnessPreview, "saved result preview highlights");
            page.SetSeedText("000000000001", commit: true); controller.AnalyzeCurrentDraft(); await Frames();
            Check(!workspace.IsWitnessPreview && workspace.PreviewRoute is null, "seed/context replacement clears preview");
            page.SetSearchSource(SeedLibraryResultProvenance.From(saved, null) with { AppliesToCurrentContext = false }, false);
            Check(((Button)page.FindChild("OpenMapWitness1", true, false)).Disabled &&
                ((Label)page.FindChild("MapWitnessStatus1", true, false)).Text.Contains(JsonUiTextProvider.CreatePredictorUi13("en").Get("ui1.map.witness.context")),
                "changed context exposes unavailable original witness");
            var unknown = JsonSerializer.Deserialize<PersistedSearchResult>(JsonSerializer.Serialize(saved, json), json)!;
            unknown.ValidationDisposition = SearchDisposition.Unknown;
            page.SetSearchSource(SeedLibraryResultProvenance.From(unknown, null) with { SourceSeed = page.LastDocument!.CanonicalSeed }, false);
            Check(((Button)page.FindChild("OpenMapWitness1", true, false)).Disabled, "unverified result cannot advertise a confirmed witness");
            // An origin from ordinary/legacy results is not evidence that this
            // Act ever had a map receipt. Missing rows must remain absent even
            // after the retained origin becomes stale.
            var original = SeedLibraryResultProvenance.From(saved, null) with { SourceSeed = page.LastDocument!.CanonicalSeed };
            foreach (string language in new[] { "zh", "en" })
            {
                store.SetLanguageOverride(language); Set(shell, "_languageCode", ""); Call(shell, "RefreshLanguage");
                page.SetSearchSource(null, false);
                Check(!((Control)page.FindChild("MapWitnessEntry1", true, false)).Visible, "ordinary prediction has no witness entry " + language);
                page.SetSearchSource(original with { MapRouteWitnesses = [], AppliesToCurrentContext = false }, false);
                var entry = (Control)page.FindChild("MapWitnessEntry1", true, false);
                Check(!entry.Visible && !((Label)page.FindChild("MapWitnessStatus1", true, false)).Visible &&
                    ((Label)page.FindChild("MapWitnessStatus1", true, false)).Text.Length == 0,
                    "missing legacy/ordinary witness never becomes a stale-context warning " + language);
                page.SetSearchSource(original with { MapRouteWitnesses = [receipt with { Act = 2 }], AppliesToCurrentContext = false }, false);
                Check(!entry.Visible, "other Act's retained receipt creates no entry for this Act " + language);
                await Frames(); RenderingServer.ForceDraw();
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "map-witness-absent-" + language + ".png"));
                page.SetSearchSource(original with { AppliesToCurrentContext = false }, false);
                Check(entry.Visible && ((Button)page.FindChild("OpenMapWitness1", true, false)).Disabled &&
                    ((Label)page.FindChild("MapWitnessStatus1", true, false)).Text == JsonUiTextProvider.CreatePredictorUi13(language).Get("ui1.map.witness.context"),
                    "real stale witness remains visible and unavailable " + language);
                await Frames(); RenderingServer.ForceDraw();
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "map-witness-stale-" + language + ".png"));
            }
            File.WriteAllText(Path.Combine(output, "map-witness.json"), JsonSerializer.Serialize(new { Passed = checks, seed, receipt,
                OriginalQuery = draft.Query, EditedQuery = frame.CaptureDraft().Query, PreviewDoesNotCommit = true }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("MAP_WITNESS_PASS checks=" + checks + ";original-result=true;persisted=true;act-seed-context-lifecycle=true;commitment-preserved=true");
        }
        finally { host.QueueFree(); save.Progress = originalProgress; await Frames(); }
    }
}
