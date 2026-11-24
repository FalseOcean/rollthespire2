using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

internal static partial class UiSurfaceChecks
{
    // Native Godot minimum-size/layout regression only. The real event supplies
    // frozen three/six-reveal branches; this check never starts a search worker
    // or makes a prediction claim about the synthetic continuation label.
    private static async Task RunCrystalLeftRailChecksAsync(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var failures = new List<string>();
        var cases = new List<object>();
        int checks = 0;
        bool observeOnly = OS.GetCmdlineUserArgs().Contains("--crystal-left-rail-observe");
        void Check(bool valid, string why) { checks++; if (!valid) failures.Add(why); }
        async Task Frames(int count = 10)
        { for (int index = 0; index < count; index++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        bool originalTestMode = TestMode.IsOn;
        CrystalSphereLiveSnapshot capture;
        RunState run;
        Player player;
        try
        {
            TestMode.IsOn = true;
            player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
            run = RunState.CreateForTest([player], seed: "000000000000", ascensionLevel: 10);
            run.CurrentActIndex = 1;
            player.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
            var ev = (CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();
            await ev.BeginEvent(player, null, false);
            capture = CrystalSphereLiveCapture.CaptureScene(run, player, ev, null, "0.111.0");
        }
        finally { TestMode.IsOn = originalTestMode; }
        var six = capture.Branches.Single(branch => branch.Mode == "Six").Snapshot;
        var remaining = capture with { Branches = [("Remaining", six)] };
        string SourceStamp() => JsonSerializer.Serialize(new { Run = run.Rng.ToSerializable(), Player = player.PlayerRng.ToSerializable() });
        string sourceBefore = SourceStamp();
        foreach (var resolution in new[] { new Vector2I(1920, 1080), new Vector2I(1280, 720) })
        {
            var viewport = new SubViewport { Name = "CrystalLeftRailViewport", Size = resolution, Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            root.AddChild(viewport);
            var panel = new CrystalSphereAssistantPanel();
            try
            {
                panel.Initialize(run, runtime);
                viewport.AddChild(panel);
                panel.SetProcess(false);
                float scale = Math.Min(1, Math.Min(resolution.X / 1600f, resolution.Y / 900f));
                panel.Scale = Vector2.One * scale;
                panel.Size = new(WorkspaceLayout.CanvasWidth, WorkspaceLayout.CanvasHeight);
                panel.Position = ((Vector2)resolution - panel.Size * scale) / 2;
                var workspace = panel.GetNode<Control>("CrystalAssistantWorkspace");
                var inset = workspace.GetNode<Control>("CrystalContextInset");
                var context = inset.GetNode<Control>("CrystalContext");
                var branch = (OptionButton)typeof(CrystalSphereAssistantPanel).GetField("_branch", flags)!.GetValue(panel)!;
                foreach (string language in new[] { "zh", "en" })
                {
                    panel.ApplyLanguage(language);
                    var text = JsonUiTextProvider.CreatePredictorUi13(language);
                    string T(string key) => text.Get("predictor.crystal." + key);
                    panel.ShowSnapshot(capture, startDiscovery: false);
                    await Observe("entry-three-six", false);
                    Check(branch.ItemCount == capture.Branches.Length && capture.Branches.Select((item, index) =>
                        branch.GetItemText(index) == T(item.Mode)).All(value => value),
                        language + " entry popup retains complete localized branch names");
                    panel.ShowSnapshot(remaining, startDiscovery: false);
                    await Observe("remaining-six", true);
                    string expected = T("Remaining") + " · " + string.Format(T("remaining"), 6);
                    Check(branch.GetItemText(branch.Selected) == expected && branch.Text == expected,
                        language + " selected continuation retains its complete six-reveal label");
                    Check(!branch.ClipText && branch.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming,
                        language + " continuation wraps complete text instead of clipping or adding an ellipsis");
                    var checkpoint = panel.SuspendSession()!;
                    panel.ShowSnapshot(remaining with { Branches = [("Remaining", six with { Remaining = 1 })] }, startDiscovery: false);
                    await Observe("remaining-one", false);
                    panel.RestoreSession(checkpoint);
                    await Observe("restored-six", false);
                    Check(branch.GetItemText(branch.Selected) == expected, language + " restored branch preserves the full remaining-six label");

                    async Task Observe(string state, bool screenshot)
                    {
                        await Frames();
                        float dividerX = (workspace.GetGlobalTransform() * new Vector2(
                            WorkspaceLayout.LeftRailWidth + WorkspaceLayout.RailGap / 2, 0)).X;
                        var buttons = context.FindChildren("*", "Button", recursive: true, owned: false).OfType<Button>()
                            .Where(button => button.IsVisibleInTree()).ToArray();
                        var branchRect = branch.GetGlobalRect();
                        var contextRect = context.GetGlobalRect();
                        string prefix = resolution.X + "/" + language + "/" + state + ": ";
                        Check(panel.Calculation.Worker == null && !panel.Calculation.Stale,
                            prefix + "layout fixture remains fresh without starting a solver or running scene validation");
                        Check(inset.Size.X <= WorkspaceLayout.LeftRailWidth + .1f,
                            prefix + "intrinsic control minima do not enlarge the fixed left rail");
                        Check(context.Size.X <= WorkspaceLayout.LeftRailWidth - WorkspaceLayout.ColumnInset * 2 + .1f,
                            prefix + "left content stays inside both column insets");
                        Check(buttons.All(button => button.GetGlobalRect().End.X <= dividerX - .5f),
                            prefix + "every visible left option/action stays before the actual divider");
                        Check(buttons.All(button => button.GetGlobalRect().End.X <= contextRect.End.X + .5f),
                            prefix + "options fit their actual left content instead of only fitting the viewport");
                        Check(branch.GetCombinedMinimumSize().X <= WorkspaceLayout.LeftRailWidth - WorkspaceLayout.ColumnInset * 2 + .1f,
                            prefix + "selected branch no longer contributes an oversized horizontal minimum");
                        Check(branch.Size.Y + .1f >= branch.GetCombinedMinimumSize().Y,
                            prefix + "selected text receives its complete wrapped minimum height");
                        string? shot = null;
                        if (screenshot)
                        {
                            await Frames(2); RenderingServer.ForceDraw();
                            shot = Path.Combine(output, "crystal-left-rail-" + resolution.X + "-" + language + ".png");
                            Check(viewport.GetTexture().GetImage().SavePng(shot) == Error.Ok, prefix + "save native geometry screenshot");
                        }
                        cases.Add(new { resolution = new[] { resolution.X, resolution.Y }, language, state, scale,
                            dividerX, railWidth = inset.Size.X, contentWidth = context.Size.X,
                            selected = branch.Text, minimumWidth = branch.GetCombinedMinimumSize().X,
                            minimumHeight = branch.GetCombinedMinimumSize().Y,
                            branchSize = new[] { branch.Size.X, branch.Size.Y }, branchRight = branchRect.End.X,
                            overflow = branchRect.End.X - dividerX,
                            controls = buttons.Select(button => new { button.Text, minimumWidth = button.GetCombinedMinimumSize().X,
                                right = button.GetGlobalRect().End.X, width = button.Size.X }).ToArray(), screenshot = shot });
                    }
                }
            }
            finally { viewport.Free(); }
        }
        Check(SourceStamp() == sourceBefore, "Crystal layout/language/restore checks preserve native RNG state");
        File.WriteAllText(Path.Combine(output, "crystal-left-rail-result.json"), JsonSerializer.Serialize(new
        { passed = failures.Count == 0, observeOnly, checks, failures, cases, nativePanelOnly = true,
            productAssembly = typeof(CrystalSphereAssistantPanel).Assembly.ManifestModule.ModuleVersionId,
            continuationFixture = "native six-reveal pre-payment snapshot displayed as the equivalent continuation label; no solver execution" },
            new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("CRYSTAL_LEFT_RAIL " + (failures.Count == 0 ? "PASS" : "OBSERVED_OVERFLOW") + " checks=" + checks + ";failures=" + failures.Count);
        if (!observeOnly && failures.Count > 0) throw new InvalidOperationException("CrystalLeftRail: " + string.Join("; ", failures));
    }
}
