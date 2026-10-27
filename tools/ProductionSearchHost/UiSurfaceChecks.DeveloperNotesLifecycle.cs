using System.Reflection;
using Godot;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.DeveloperNotes;
using RolltheSpire2.Presentation.DeveloperNotes;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.DeveloperNotes;
using RolltheSpire2.Ui.Theme;

internal static partial class UiSurfaceChecks
{
    // Called by the existing initialized UI host; no separate game/save harness.
    private static async Task RunDeveloperNotesLifecycleChecksAsync(Node root, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, flags)!.GetValue(owner)!;
        void Set(object owner, string name, object? value) => owner.GetType().GetField(name, flags)!.SetValue(owner, value);
        int checks = 0;
        void Check(bool value, string reason)
        { checks++; if (!value) throw new InvalidOperationException("NOTES_LIFECYCLE " + reason); }
        async Task Frame() => await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Ready(DeveloperNotesPage page)
        {
            for (int i = 0; i < 1800 && !page.IsPresentationReady && !page.HasPresentationFailure; i++) await Frame();
            Check(page.IsPresentationReady && !page.HasPresentationFailure, "full article, restored viewport and native draw complete");
        }

        string priorFont = MainPanelTheme.FontChoice;
        var surface = new Control { Size = new(1600, 900) };
        root.AddChild(surface);
        DeveloperNotesPage? page = null;
        try
        {
            MainPanelTheme.SelectFont("game-sans");
            // Real indexed documents retain BBCode, sections and acknowledgement metadata.
            var provider = new DeveloperNotesDocumentProvider();
            var resolution = provider.Resolve("zh");
            var document = resolution.Document!;
            var names = RuntimeGameContentNameResolver.Create("zh");
            page = new DeveloperNotesPage(provider);
            surface.AddChild(page); page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            page.ApplyLocalization(JsonUiTextProvider.Create("zh"), names, resolution);
            Check(!page.IsPresentationReady && !page.IsUsefulContentReady, "synchronous admission is not article readiness");
            page.SetPresentationActive(false);
            await Frame(); await Frame();
            Check(Field<int>(page, "_nextBlock") == 0,
                "an overlaid previous article yields to its owner's newly requested page even while still visible");
            page.SetPresentationActive(true);
            page.Hide();
            await Frame(); await Frame();
            Check(Field<int>(page, "_nextBlock") == 0 && !page.IsPresentationReady,
                "hidden initial article does not generate blocks or complete a draw");
            page.Show();
            await Ready(page);
            Check(Field<DeveloperNotesArticleRenderer>(page, "_renderer").GetChildCount() == document.Chapters[0].Blocks.Count,
                "every release block appears exactly once");
            Check(page.IsUsefulContentReady, "complete initial viewport is useful");
            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "notes-complete-sans.png"));
            int acknowledged = 0;
            page.AcknowledgementRequested += _ => acknowledged++;
            Field<Button>(page, "_acknowledge").EmitSignal(Button.SignalName.Pressed);
            Check(acknowledged == 1, "complete chapter retains explicit acknowledgement action");

            var scroll = Field<ScrollContainer>(page, "_articleScroll");
            scroll.ScrollVertical = 180;
            await Frame();
            int saved = scroll.ScrollVertical;
            Check(saved > 0, "real article provides a deep reading position");
            page.OpenChapter(DeveloperNotesChapterIds.Positioning);
            page.OpenChapter(DeveloperNotesChapterIds.Release);
            Check(!page.IsPresentationReady && !page.IsUsefulContentReady,
                "new revision cannot reuse the earlier article draw or top as a deep target receipt");
            page.Hide();
            int suspendedBlock = Field<int>(page, "_nextBlock");
            await Frame(); await Frame();
            Check(Field<int>(page, "_nextBlock") == suspendedBlock && !page.IsPresentationReady,
                "hiding retains the unfinished revision without advancing it");
            page.Show(); await Ready(page);
            Check(page.SelectedChapterId == DeveloperNotesChapterIds.Release && scroll.ScrollVertical == saved,
                "rapid chapter changes restore the requested chapter's own reading position");
            int retained = Field<DeveloperNotesArticleRenderer>(page, "_renderer").GetChildCount();
            page.Hide(); await Frame(); page.Show(); await Frame();
            Check(page.IsPresentationReady && Field<DeveloperNotesArticleRenderer>(page, "_renderer").GetChildCount() == retained,
                "completed revisit retains the same article");

            MainPanelTheme.SelectFont("game-serif"); MainPanelTheme.RefreshFonts(page);
            Check(!page.IsPresentationReady && !page.IsUsefulContentReady,
                "font replacement invalidates receipts before the next process");
            await Ready(page);
            Check(scroll.ScrollVertical == saved, "font replacement restores position after native layout");
            root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "notes-restored-serif.png"));

            page.ApplyLocalization(JsonUiTextProvider.Create("en"), RuntimeGameContentNameResolver.Create("en"));
            Check(!page.IsPresentationReady && !page.IsUsefulContentReady, "locale replacement invalidates old receipts");
            await Ready(page);
            Check(scroll.ScrollVertical == 0, "other locale starts at its own position");
            page.ApplyLocalization(JsonUiTextProvider.Create("zh"), names, resolution);
            await Ready(page);
            Check(scroll.ScrollVertical == saved, "returning locale retains its reading position");

            page.OpenChapter(DeveloperNotesChapterIds.Next);
            var broken = document.Chapters.Single(chapter => chapter.Id == DeveloperNotesChapterIds.Next) with
            { Blocks = [new DeveloperNotesBlock(DeveloperNotesBlockKinds.BulletList, null, null)] };
            Set(page, "_buildingChapter", broken);
            await Frame(); await Frame();
            Check(page.HasPresentationFailure && page.PresentationFailure is NullReferenceException && !page.IsPresentationReady,
                "native construction failure retains its original exception and cannot confirm the article");
            page.OpenChapter(DeveloperNotesChapterIds.Positioning);
            await Ready(page);
            Check(!page.HasPresentationFailure, "failure leaves chapter navigation available for independent content");
            page.OpenChapter(DeveloperNotesChapterIds.Devlog);
            page.SetProcess(false);
            typeof(DeveloperNotesPage).GetMethod("AwaitArticleDraw", flags)!.Invoke(page, null);
            MainPanelTheme.SelectFont("game-sans"); MainPanelTheme.RefreshFonts(page);
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Check(Field<object?>(page, "_articlePreDraw") is null && Field<object?>(page, "_articlePostDraw") is null &&
                !page.IsPresentationReady,
                "font invalidation releases obsolete draw callbacks even with article processing paused");
            page.SetProcess(true);
            page.RetirePresentation();
            Check(Field<object?>(page, "_articlePreDraw") is null && Field<object?>(page, "_articlePostDraw") is null &&
                Field<object?>(page, "_buildingChapter") is null && !page.IsPresentationReady,
                "retirement releases draw callbacks and unfinished task");
            await Frame();
            Check(!page.IsPresentationReady, "retired revision cannot deliver later");
            File.WriteAllText(Path.Combine(output, "developer-notes-lifecycle.json"),
                System.Text.Json.JsonSerializer.Serialize(new { Passed = checks }));
            GD.Print("NOTES_LIFECYCLE_PASS checks=" + checks);
        }
        finally
        {
            page?.RetirePresentation();
            surface.Free();
            MainPanelTheme.SelectFont(priorFont);
        }
    }
}
