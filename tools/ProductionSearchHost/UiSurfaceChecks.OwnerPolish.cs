using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunOwnerPolishAsync(Node root, string output, ModRuntimeSnapshot runtime)
    {
        var args = OS.GetCmdlineUserArgs();
        int scopeAt = Array.IndexOf(args, "--owner-ui-polish-scope");
        string scope = scopeAt >= 0 ? args[scopeAt + 1] : "all";
        bool Run(string group) => scope == "all" || scope == group;
        // Keep popup interaction inside this isolated host viewport, independent
        // of focus changes in the Owner's other desktop windows.
        root.GetWindow().GuiEmbedSubwindows = true;
        var save = MegaCrit.Sts2.Core.Saves.SaveManager.Instance;
        Vector2I previousPointer = DisplayServer.MouseGetPosition();
        var original = save.Progress;
        var progress = original.ToSerializable();
        progress.CharStats = [new() { Id = new("CHARACTER", "IRONCLAD"), TotalLosses = 1 }];
        progress.Epochs = MegaCrit.Sts2.Core.Timeline.EpochModel.AllEpochIds.Select(id =>
            new MegaCrit.Sts2.Core.Saves.SerializableEpoch(id, MegaCrit.Sts2.Core.Saves.EpochState.Revealed)).ToList();
        progress.DiscoveredActs = new[] { "OVERGROWTH", "UNDERDOCKS", "HIVE", "GLORY" }
            .Select(entry => new MegaCrit.Sts2.Core.Models.ModelId("ACT", entry)).ToList();
        save.Progress = MegaCrit.Sts2.Core.Saves.ProgressState.FromSerializable(progress,
            new MegaCrit.Sts2.Core.Saves.Validation.DeserializationContext());
        try
        {
            if (Run("chrome") || Run("state") || Run("followup") || Run("release-copy") || scope == "candidate-defaults")
            {
                var host = new Control { Size = new(1600, 900) }; root.AddChild(host);
                var shell = new WorkspaceShell();
                shell.InitializeWithPersistence(runtime, Path.Combine(output, "owner-main-state", Guid.NewGuid().ToString("N")));
                host.AddChild(shell); shell.Open(); await AwaitPageReady(root, shell);
                try
                {
                    if (Run("chrome")) await RunOwnerChromeChecksAsync(root, shell, output);
                    if (Run("state"))
                    {
                        await RunOwnerPolishStateAsync(root, shell, output);
                        await RunOwnerCharacterChangeChecksAsync(root, shell, output);
                    }
                    if (Run("followup")) await RunOwnerFollowupChecksAsync(root, shell, output);
                    if (scope == "release-copy") await RunOwnerFollowupChecksAsync(root, shell, output, aboutOnly: true);
                    if (scope == "candidate-defaults") await RunCandidateDefaultsChecksAsync(root, shell, output);
                }
                finally { host.QueueFree(); await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
            }
            if (Run("editors")) await RunOwnerPolishEditorChecksAsync(root, output, runtime);
            if (Run("content") || scope == "reference-copy")
                await RunOwnerPolishContentChecks(root, output, runtime, referenceCopyOnly: scope == "reference-copy");
            if (Run("map")) await RunMapWitnessChecksAsync(root, output, runtime);
            GD.Print("OwnerUiPolish PASS scope=" + scope);
        }
        finally { save.Progress = original; DisplayServer.WarpMouse(previousPointer); }
    }

    private static async Task RunOwnerPolishStateAsync(Node root, WorkspaceShell shell, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, flags)!.GetValue(value)!;
        object? Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, flags)!.Invoke(value, args);
        int checks = 0;
        void Check(bool valid, string why) { checks++; if (!valid) throw new InvalidOperationException("OwnerState:" + why); }
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var transform = Field<TransformationSearchEditor>(frame, "_transformationEditor");
        var neow = Field<NeowSearchEditor>(frame, "_neowEditor");
        var persistence = Field<SearchWorkspace>(shell, "_persistence");
        var json = Field<JsonSerializerOptions>(persistence, "_json");
        var screenshots = new List<string>();
        async Task Shot(string name)
        {
            await Frames(); RenderingServer.ForceDraw();
            string file = Path.Combine(output, "owner-state-" + name + ".png");
            Check(root.GetViewport().GetTexture().GetImage().SavePng(file) == Error.Ok, "save " + name);
            screenshots.Add(file);
        }
        var aggregate = new TransformationAggregateCondition(TransformationOpening.LeafyPoultice,
            TransformationPickupOrder.Any, false, false, false, TransformationAggregatePredicate.RareCountAtLeast, 2, []);
        var initial = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 0,
            SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice), TransformationAggregate = aggregate },
            AncientOptionConditionProfile.BroadDefault);
        foreach (string locale in new[] { "zh", "en" })
        {
            MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
            frame.RestoreDraft(initial, render: false); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
            Call(frame, "SelectDomain", "transform"); await Frames();
            Check(frame.CaptureDraft().Query.TransformationAggregate?.MinimumRareCount == 2, "real Leafy active predicate");
            var quantity = (MapQuantityInput)transform.FindChild("TransformationRareCount", true, false)!;
            var input = (LineEdit)quantity.FindChild("Input", true, false)!;
            input.Text = "1"; input.EmitSignal(LineEdit.SignalName.TextChanged, "1");
            Check(frame.CaptureDraft().Query.TransformationAggregate?.MinimumRareCount == 1, "direct quantity input");
            ((Button)quantity.FindChild("Plus", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
            Check(frame.CaptureDraft().Query.TransformationAggregate?.MinimumRareCount == 2, "plus quantity input");
            await Shot("transform-active-" + locale);
            Call(neow, "OnSourceRelicPressed", BaseGameModelKeys.Relics.ArcaneScroll); await Frames();
            var inactive = frame.CaptureDraft();
            Check(inactive.Query.TransformationAggregate is null && inactive.TransformationEditor!.TakenOver.Contains("N.LeafyPoultice") &&
                inactive.TransformationEditor.RareCount == 2, "explicit source replacement suspends and retains authored targets");
            string saved = JsonSerializer.Serialize(inactive, json);
            frame.RestoreDraft(JsonSerializer.Deserialize<WorkbenchSearchDraft>(saved, json)!, render: false);
            frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
            Check(frame.CaptureDraft().Query.TransformationAggregate is null, "inactive takeover survives persistence round trip");
            Call(frame, "SelectDomain", "transform"); await Shot("transform-inactive-" + locale);
            Call(neow, "OnSourceRelicPressed", BaseGameModelKeys.Relics.LeafyPoultice); await Frames();
            Check(frame.CaptureDraft().Query.TransformationAggregate?.MinimumRareCount == 2, "returning source reuses original target");
            ModelKey card = neow.TransformationCardCandidates(0).First();
            var current = frame.CaptureDraft();
            foreach (int mode in new[] { 1, 2 })
            {
                frame.RestoreDraft(current with { TransformationEditor = current.TransformationEditor! with
                    { Objective = mode, Cards = [card] } }, render: false);
                frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
                Call(frame, "SelectDomain", "transform");
                Check(frame.CaptureDraft().Query.TransformationAggregate is { } condition &&
                    condition.TargetMultiset.SequenceEqual([card]) && condition.Predicate == (mode == 1
                        ? TransformationAggregatePredicate.ContainsMultiset : TransformationAggregatePredicate.ContainsMultisetAndRemainingRare),
                    "target modes retain declared cards " + mode);
                await Shot("transform-mode" + mode + "-" + locale);
            }
            frame.RestoreDraft(current, render: false); frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
            transform.Refresh(locale, JsonUiTextProvider.CreateUi13(locale), 0, [], [], false,
                new HashSet<string> { "N.LeafyPoultice" });
            bool unavailable = false;
            try { transform.ExportCondition(initial.Query); }
            catch (InvalidOperationException exception)
            { unavailable = exception.Message == "TransformationDeclaredSourcesUnavailable:N.LeafyPoultice"; }
            Check(unavailable, "still-declared missing source retains real failure");
            Call(frame, "RefreshTransformationContext", null, true);
        }
        File.WriteAllText(Path.Combine(output, "owner-state-checks.json"), JsonSerializer.Serialize(
            new { Checks = checks, Screenshots = screenshots, HistoryTouched = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
