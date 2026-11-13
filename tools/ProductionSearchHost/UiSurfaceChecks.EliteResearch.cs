using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunEliteResearchUiAsync(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, members)!.GetValue(owner)!;
        void Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, members)!.Invoke(owner, args);
        T Find<T>(Node owner, string name) where T : Node => (T)(owner.FindChild(name, true, false)
            ?? throw new InvalidOperationException("EliteResearchUi.MissingControl." + name));
        int checks = 0;
        void Check(bool ok, string why)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("EliteResearchUi." + why);
        }
        async Task Frames()
        {
            for (int i = 0; i < 4; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var window = root.GetWindow();
        var previousSize = window.Size;
        var previousScale = window.ContentScaleMode;
        float previousFactor = window.ContentScaleFactor;
        window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        window.ContentScaleFactor = 1;
        var host = new Control { Size = new(1280, 720) };
        root.AddChild(host);
        var shell = new WorkspaceShell();
        string state = Path.Combine(output, "elite-research-state");
        shell.InitializeWithPersistence(runtime, state);
        host.AddChild(shell);
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var store = Field<SearchWorkspace>(shell, "_persistence");
        var map = Field<ActInformationSearchEditor>(frame, "_actInformationEditor");
        var basis = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        try
        {
            shell.Open();
            await Frames();
            frame.RestoreDraft(basis, render: false);
            frame.Refresh("zh", JsonUiTextProvider.CreateUi13("zh"));
            Call(frame, "SelectDomain", "map");
            await Frames();
            Check(!Find<CheckButton>(map, "ElitePrefixResearchEnabled").ButtonPressed &&
                frame.CaptureDraft().Query.ElitePrefixResearch is null, "DefaultOff");
            Find<CheckButton>(map, "ElitePrefixResearchEnabled").ButtonPressed = true;
            await Frames();
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == ElitePrefixResearchCondition.FloorSeven, "DefaultFloorSeven");
            Check(map.FindChild("ElitePrefixExtraMonsters", true, false) is null &&
                map.FindChild("ElitePrefixUnknowns", true, false) is null, "FloorSevenHasNoCountInputs");
            var modern = frame.CaptureDraft();
            store.SaveWorkbench(modern);
            store.FlushAll();
            frame.RestoreDraft(new SearchWorkspace(state, false).LoadWorkbench()!, render: false);
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == ElitePrefixResearchCondition.FloorSeven, "ModernSavedQueryRoundTrip");
            frame.ClearCategoryConditions();
            Check(frame.CaptureDraft().Query.ElitePrefixResearch is null, "ModernClearMap");
            frame.UndoClearConditions();
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == ElitePrefixResearchCondition.FloorSeven, "ModernUndoMap");

            frame.RestoreDraft(basis with { Query = SearchQuery.Empty with { ElitePrefixResearch = new(0, 4) } }, render: false);
            Call(map, "Render");
            await Frames();
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == new ElitePrefixResearchCondition(0, 4), "LegacyKeepsCounts");
            Find<MapQuantityInput>(map, "ElitePrefixExtraMonsters").Value = 1;
            Find<MapQuantityInput>(map, "ElitePrefixUnknowns").Value = 3;
            var authored = frame.CaptureDraft();
            Check(authored.Query.ElitePrefixResearch == new ElitePrefixResearchCondition(1, 3) &&
                SearchQueryProjection.CountConditions(authored.Query) == 1 && map.MapConditionCount() == 1,
                "ControlsReachAuthoredQueryAndCounts");
            Check(QueryHistoryIdentity.Key(authored) != QueryHistoryIdentity.Key(basis), "HistoryIncludesResearch");
            Find<CheckButton>(map, "ElitePrefixResearchEnabled").ButtonPressed = false;
            var disabled = frame.CaptureDraft();
            Check(disabled.Query.ElitePrefixResearch is null && disabled.MapEditor?.EliteResearchDraft == new ElitePrefixResearchCondition(1, 3),
                "OffKeepsOnlyInactiveDraft");
            frame.RestoreDraft(disabled, render: false);
            Call(map, "Render");
            Find<CheckButton>(map, "ElitePrefixResearchEnabled").ButtonPressed = true;
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == authored.Query.ElitePrefixResearch, "ReenableRestoresCounts");
            frame.ClearCategoryConditions();
            Check(frame.CaptureDraft().Query.ElitePrefixResearch is null, "ClearMap");
            frame.UndoClearConditions();
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == authored.Query.ElitePrefixResearch, "UndoMap");
            frame.ClearAllConditions();
            Check(frame.CaptureDraft().Query.ElitePrefixResearch is null, "ClearAll");
            frame.UndoClearConditions();
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == authored.Query.ElitePrefixResearch, "UndoAll");
            store.SaveWorkbench(authored);
            store.FlushAll();
            var restored = new SearchWorkspace(state, false).LoadWorkbench()!;
            frame.RestoreDraft(restored, render: false);
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == authored.Query.ElitePrefixResearch, "SavedQueryRoundTrip");
            Call(map, "Render");
            await Frames();
            Find<Button>(map, "ElitePrefixUseFloorSeven").EmitSignal(BaseButton.SignalName.Pressed);
            await Frames();
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == ElitePrefixResearchCondition.FloorSeven, "ExplicitReplaceLegacy");
            Find<CheckButton>(map, "ElitePrefixResearchEnabled").ButtonPressed = false;
            var modernOff = frame.CaptureDraft();
            Check(modernOff.Query.ElitePrefixResearch is null &&
                modernOff.MapEditor?.EliteResearchDraft == ElitePrefixResearchCondition.FloorSeven, "ModernInactiveDraft");
            frame.RestoreDraft(modernOff, render: false);
            Call(map, "Render");
            Find<CheckButton>(map, "ElitePrefixResearchEnabled").ButtonPressed = true;
            Check(frame.CaptureDraft().Query.ElitePrefixResearch == ElitePrefixResearchCondition.FloorSeven, "ModernReenable");

            foreach (string locale in new[] { "zh", "en" })
            {
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(locale == "zh" ? "zhs" : "eng");
                store.SetLanguageOverride(locale);
                shell.GetType().GetField("_languageCode", members)!.SetValue(shell, "");
                Call(shell, "RefreshLanguage");
                window.Size = new(1280, 720);
                host.Size = window.Size;
                frame.Refresh(locale, JsonUiTextProvider.CreateUi13(locale));
                Call(frame, "SelectDomain", "map");
                await Frames();
                var toggle = Find<CheckButton>(map, "ElitePrefixResearchEnabled");
                var bounds = map.GetGlobalRect();
                foreach (Control control in new Control[] { toggle })
                {
                    var rectangle = control.GetGlobalRect();
                    Check(rectangle.Position.X >= bounds.Position.X - 1 && rectangle.End.X <= bounds.End.X + 1,
                        "ControlFits." + locale + "." + control.Name);
                }
                RenderingServer.ForceDraw();
                Check(root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "elite-research-" + locale + ".png")) == Error.Ok,
                    "Screenshot." + locale);
            }
            File.WriteAllText(Path.Combine(output, "elite-research-ui.json"), JsonSerializer.Serialize(new
            {
                passed = true, checks, authored.Query.ElitePrefixResearch,
                defaultOff = true, clearUndo = true, persistence = true, locales = new[] { "zh", "en" }
            }));
            GD.Print($"EliteResearchUi=PASS;checks={checks}");
        }
        finally
        {
            host.QueueFree();
            await Frames();
            window.Size = previousSize;
            window.ContentScaleMode = previousScale;
            window.ContentScaleFactor = previousFactor;
        }
    }
}
