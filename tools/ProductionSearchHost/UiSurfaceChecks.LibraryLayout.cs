using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    // Uses the initialized preset host and actual shell/library navigation.
    private static async Task RunLibraryLayoutAsync(Node host, WorkspaceShell shell, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, flags)!.GetValue(value)!;
        int checks = 0;
        void Check(bool ok, string reason)
        { checks++; if (!ok) throw new InvalidOperationException("LIBRARY_LAYOUT " + reason); }
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Press(Button button)
        {
            Check(button.IsVisibleInTree() && !button.Disabled, "available action " + button.Name);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }
        var persistence = Field<SearchWorkspace>(shell, "_persistence");
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var draft = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        var capture = frame.CapturePreset(draft);
        string longTitle = string.Concat(Enumerable.Repeat("长名称保留全部原文 Long authored preset title / ", 60)) + "TITLE-END";
        var preset = persistence.Presets.SaveUserPreset(longTitle, "Layout fixture", [], capture);
        for (int i = 0; i < 18; i++)
            persistence.Presets.SaveUserPreset($"关联预设 {i:D2} / Associated preset {i:D2} with its complete authored name", "", [], capture);
        var query = persistence.EnsureQuery(draft, null);
        query.RunCount = 1; query.LastUsedAtUtc = DateTimeOffset.UtcNow;

        Press(Field<Button>(shell, "_seeds"));
        var library = Field<SeedLibraryCanvas>(shell, "_seedLibrary");
        async Task Ready()
        {
            for (int i = 0; i < 1200 && (!library.IsVisibleInTree() || !library.IsPresentationReady); i++) await Frames(1);
            Check(library.IsVisibleInTree() && library.IsPresentationReady && !library.HasPresentationFailure,
                "real library entry finishes presentation");
            await Frames();
        }
        Button Action(string name) => (Button)library.FindChild(name, true, false);
        async Task Select(string id)
        {
            await Ready();
            var row = library.FindChildren("LibraryItem*", "Button", true, false).OfType<Button>()
                .Single(button => button.GetMeta("entry_id").AsString() == id);
            if (!row.GetMeta("selected").AsBool()) Press(row);
            await Ready();
        }
        void Fits(Control control, string reason)
        {
            Rect2 rect = control.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, control.Size);
            Check(control.IsVisibleInTree() && rect.Size.X > 0 && rect.Size.Y > 0 &&
                host.GetViewport().GetVisibleRect().Grow(1).Encloses(rect), reason + "; rect=" + rect);
        }
        async Task ActionsFit(params string[] names)
        {
            var scroll = Field<ScrollContainer>(library, "_detailScroll");
            foreach (string name in names)
            {
                var button = Action(name);
                if (scroll.IsAncestorOf(button)) { scroll.EnsureControlVisible(button); await Frames(); }
                Fits(button, "action stays reachable inside window " + name);
            }
            Check(scroll.Size.Y >= 96, "title and notices leave space for multiple detail lines; height=" + scroll.Size.Y);
            Fits(scroll, "detail viewport stays inside window");
            Fits((Control)library.FindChild("SeedLibraryTabs", true, false), "tabs stay reachable");
        }
        async Task FullTitle(string expected)
        {
            var heading = (Label)library.FindChild("LibraryDetailTitle", true, false);
            var full = (Label)library.FindChild("LibraryFullTitle", true, false);
            Check(heading.Text == expected && heading.MaxLinesVisible == 2 && heading.GetLineCount() > 2 &&
                heading.Size.Y >= heading.GetLineHeight() * 2 - 1 &&
                heading.Size.Y <= heading.GetLineHeight() * 2 + Math.Max(0, heading.GetThemeConstant("line_spacing")) + 1,
                "only the fixed header is bounded; original title is retained");
            Fits(heading, "bounded title remains visible in the fixed header");
            Check(full.IsVisibleInTree() && full.Text == expected && full.MaxLinesVisible == -1,
                "complete unmodified title lives in scrolling details");
            var scroll = Field<ScrollContainer>(library, "_detailScroll");
            scroll.ScrollVertical = Math.Max(0, (int)Math.Ceiling(full.GetRect().End.Y - scroll.Size.Y));
            await Frames();
            RenderingServer.ForceDraw();
            host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "library-title-tail-latest.png"));
            Check(full.GetGlobalRect().End.Y <= scroll.GetGlobalRect().End.Y + 2 &&
                full.GetGlobalRect().End.Y > scroll.GetGlobalRect().Position.Y,
                "the full title's final line is reachable by its actual scroll container; title=" + full.GetGlobalRect() +
                "; local=" + full.GetRect() + "; viewport=" + scroll.GetGlobalRect() + "; offset=" + scroll.ScrollVertical +
                "; range=" + scroll.GetVScrollBar().MaxValue + "; page=" + scroll.GetVScrollBar().Page);
        }
        var collectionStore = Field<CardCollectionStore>(library, "_cardCollections");
        string collectionDirectory = Field<string>(collectionStore, "_directory");
        Check(Path.GetFullPath(collectionDirectory).StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase), "corrupt-file fixtures are isolated from player data");
        Directory.CreateDirectory(collectionDirectory);
        for (int i = 0; i < 4; i++) File.WriteAllText(Path.Combine(collectionDirectory, $"broken-layout-{i}.json"),
            "{\"SchemaVersion\":999,\"Reason\":\"" + new string('x', 2000) + "\"}");
        var healthy = collectionStore.Save(null, "Healthy layout fixture", [new ModelKey("CARD", "STRIKE_IRONCLAD")]);
        var canvas = shell.GetParent<Control>();
        var cases = new[] { (Language: "zh", Font: "game-serif", Size: new Vector2I(1600, 900)),
            (Language: "zh", Font: "game-sans", Size: new Vector2I(800, 600)),
            (Language: "en", Font: "game-serif", Size: new Vector2I(800, 600)),
            (Language: "en", Font: "game-sans", Size: new Vector2I(1280, 720)) };
        var screenshots = new List<string>();
        foreach (var scenario in cases)
        {
            persistence.SetLanguageOverride(scenario.Language);
            persistence.SetFontChoice(scenario.Font); MainPanelTheme.SelectFont(scenario.Font); MainPanelTheme.RefreshFonts(shell);
            host.GetWindow().Size = scenario.Size; canvas.Size = scenario.Size;
            await Frames(); library.Refresh(scenario.Language); await Ready();
            Press(Action("SeedLibraryPresets")); await Select(preset.Id);
            await FullTitle(longTitle);
            await ActionsFit("UseLibraryPreset", "ViewPresetHistory", "EditLibraryPreset", "DeleteLibraryPreset", "UpdatePresetEnvironment");
            Press(Action("ViewPresetHistory")); await Ready();
            string associatedTitle = string.Join(" / ", persistence.PresetsForQuery(query.QueryKey).Select(p => p.TitleFor(scenario.Language)));
            Check(persistence.PresetsForQuery(query.QueryKey).Count == 19, "history includes every associated preset");
            await FullTitle(associatedTitle); await ActionsFit("ReloadHistory", "HistoryConditions", "HistoryResults");
            string prefix = $"library-layout-{scenario.Language}-{scenario.Font}-{scenario.Size.X}";
            RenderingServer.ForceDraw();
            Check(host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, prefix + "-history.png")) == Error.Ok, "history screenshot");
            screenshots.Add(prefix + "-history.png");
            Press(Action("SeedLibraryCardCollections")); await Ready();
            Check(collectionStore.LoadIssues.Count == 4, "all four real file failures remain recorded");
            var receipt = Field<Label>(library, "_receipt");
            var details = Field<TextEdit>(library, "_issueDetails");
            RenderingServer.ForceDraw();
            host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, prefix + "-summary-latest.png"));
            Check(receipt.IsVisibleInTree() && receipt.MaxLinesVisible == 1 && receipt.Size.Y + 1 >= receipt.GetLineHeight() &&
                !receipt.Text.Contains('\n') && !details.Visible,
                "load errors start as a bounded summary; visible=" + receipt.IsVisibleInTree() + "; size=" + receipt.Size +
                "; lineHeight=" + receipt.GetLineHeight() + "; maxLines=" + receipt.MaxLinesVisible + "; expanded=" + details.Visible);
            Fits(receipt, "bounded error summary remains visible");
            Press(Action("LibraryIssueExpand")); await Frames();
            Check(details.IsVisibleInTree() && details.Size.Y <= 149 &&
                collectionStore.LoadIssues.All(issue => details.Text.Contains(issue.Replace("\r\n", "\n"), StringComparison.Ordinal)),
                "fixed-height scrollable details retain every full original cause; size=" + details.Size);
            details.ScrollVertical = int.MaxValue; await Frames();
            Check(details.ScrollVertical > 0, "long original reasons can be read to the end");
            Press(Action("LibraryIssueCopy"));
            string copied = DisplayServer.ClipboardGet().Replace("\r\n", "\n");
            Check(copied == Field<string>(library, "_issueMessage").Replace("\r\n", "\n") &&
                collectionStore.LoadIssues.All(issue => copied.Contains(issue.Replace("\r\n", "\n"), StringComparison.Ordinal)),
                "copy retains every original error cause through the native clipboard");
            await ActionsFit("NewCardCollection", "EditCardCollection", "DuplicateCardCollection", "DeleteCardCollection");
            Fits(details, "expanded error details remain inside window");
            Check(persistence.Presets.GetAll().Single(p => p.Id == preset.Id).Title == longTitle &&
                collectionStore.TryGet(healthy.Id, out _), "display bounds preserve saved user content");
            RenderingServer.ForceDraw();
            Check(host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, prefix + "-errors.png")) == Error.Ok, "error screenshot");
            screenshots.Add(prefix + "-errors.png");
        }
        File.WriteAllText(Path.Combine(output, "library-layout-checks.json"), JsonSerializer.Serialize(
            new { passed = true, checks, screenshots, seedScans = 0, nativeGameMutation = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"LIBRARY_LAYOUT checks={checks};failures=0;screenshots={screenshots.Count};seedScans=0");
    }
}
