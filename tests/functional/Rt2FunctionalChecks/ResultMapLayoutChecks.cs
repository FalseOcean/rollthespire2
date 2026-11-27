using Godot;
using HarmonyLib;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Tests.FastSearch;

internal static class ResultMapLayoutChecks
{
    public static async Task<int> RunAsync()
    {
        if (!ClassDB.ClassExists("SpineSprite") && OperatingSystem.IsWindows())
        {
            var pck = System.Environment.GetEnvironmentVariable("RT2_NATIVE_GAME_PCK")!;
            var library = Path.Combine(Path.GetDirectoryName(pck)!, "libspine_godot.windows.template_release.x86_64.dll");
            var extension = Path.Combine(OS.GetUserDataDir(), "result-map-spine.gdextension");
            File.WriteAllText(extension, "[configuration]\nentry_symbol = \"spine_godot_library_init\"\ncompatibility_minimum = \"4.1\"\n[libraries]\nwindows.x86_64 = \"" + library.Replace('\\', '/') + "\"\n");
            GDExtensionManager.LoadExtension(extension);
        }
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard).Assembly);
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("ResultMapLayout") with
        {
            Detection = new("0.114.0", "0.114.0", "beta", "ResultMapLayout", true, "")
        };
        var tree = (SceneTree)Engine.GetMainLoop();
        var canvas = new Control { Size = new(1600, 900) };
        tree.CurrentScene.AddChild(canvas);
        var store = SearchWorkspace.Open(OS.GetUserDataDir());
        var shell = new WorkspaceShell();
        store.SetLanguageOverride("zh");
        shell.Initialize(runtime);
        canvas.AddChild(shell);
        shell.Open();
        async Task Frames(int count)
        {
            for (int i = 0; i < count; i++) await canvas.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        try
        {
            var character = BaseGameModelKeys.Characters.Silent;
            var context = new SeedLibraryContext(runtime.Detection.NormalizedVersion, RuntimeProfileId.Beta111,
                WorldGameMode.Singleplayer, 10, [new(0, character, MegaCrit.Sts2.Core.Unlocks.UnlockState.all.ToSerializable(),
                    "AssumedFullyUnlocked", AncientOptionConditionProfile.BroadDefault, SearchQuery.Empty, null)]);
            var draft = new WorkbenchSearchDraft(character, 10, SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LargeCapsule) }, AncientOptionConditionProfile.BroadDefault);
            var compiled = draft.Compile(runtime, out var authority);
            store.InitializeWorkbenchCursor();
            var plan = RolltheSpire2.Search.Compilation.SearchPreparationCompiler.Compile(compiled, new(store.CurrentNextCursorSeed, 100, 5, 1)).Plan!;
            var prepared = store.PrepareSearchStart(draft, plan, context);
            store.BindPreparedResultWorkspace(prepared);
            bool first = true;
            foreach (var seed in new[] { "8JJBXVKET4GA", "8JJWQNMTMWRD", "8JNASNC77LTL" })
            {
                var candidate = new RolltheSpire2.Search.Contracts.SearchCandidate(seed, RuntimeProfileId.Beta111, character, 10,
                    compiled.SemanticFingerprint, authority, RolltheSpire2.Search.Contracts.SearchQueryEvaluation.Match());
                store.AppendResult(candidate, compiled.SemanticFingerprint, prepared.History, prepared.Batch);
                var action = store.ResultAction(store.DisplayResults.Single(r => r.Seed == seed));
                if (action.Live is null) throw new Exception("No live result attached");
                AccessTools.Method(typeof(WorkspaceShell), "OpenSearchResult").Invoke(shell, [action]);
                await Frames(10);
                if (first)
                {
                    // Replay a retained over-height right column before opening the next result.
                    // The field report does not establish what originally enlarged it.
                    var previousRail = (Control)shell.FindChild("PredictorAuxiliaryInset", owned: false);
                    previousRail.Size += new Vector2(0, 360);
                    first = false;
                }
            }
            var result = new PersistedSearchResult { Seed = "8JNASNC77LTL", CharacterKey = character.Serialized, Ascension = 10, Context = context };

            var page = (AnalysisPage)AccessTools.Field(typeof(WorkspaceShell), "_predictor").GetValue(shell)!;
            ((Button)page.FindChild("OverviewTab", owned: false)).EmitSignal(Button.SignalName.Pressed);
            var surface = (Control)page.FindChild("Act1OverviewMap", owned: false);
            for (int i = 0; i < 600 && surface.GetChildCount() == 0; i++) await Frames(1);
            if (surface.GetChildCount() == 0) throw new Exception("Search result map did not render");
            await Frames(8);
            var bounds = ((Control)AccessTools.Field(typeof(WorkspaceShell), "_content").GetValue(shell)!).GetGlobalRect();
            GD.Print($"RESULT_MAP_LAYOUT initial={surface.GetGlobalRect()} canvas={bounds}");
            if (!bounds.Encloses(surface.GetGlobalRect())) throw new Exception("Settled result map exceeds canvas");
            foreach (var icon in surface.GetChildren().OfType<TextureRect>())
                if (!bounds.Encloses(icon.GetGlobalRect())) throw new Exception("Map icon outside canvas: " + icon.GetGlobalRect());
            AccessTools.Method(typeof(WorkspaceShell), "OpenSearchResult").Invoke(shell, [store.ResultAction(result)]);
            await Frames(12);
            var savedSurface = (Control)page.FindChild("Act1OverviewMap", owned: false);
            if (!bounds.Encloses(savedSurface.GetGlobalRect())) throw new Exception("Saved result map exceeds canvas");
            return 0;
        }
        finally { canvas.QueueFree(); }
    }
}
