using RolltheSpire2.Ui.Workspace;
using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Enumeration;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

internal static partial class UiSurfaceChecks
{
    private static async Task RunRuntimeIssuesChecks(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(object value, string name) => (T)value.GetType().GetField(name, flags)!.GetValue(value)!;
        void Call(object value, string name) => value.GetType().GetMethod(name, flags)!.Invoke(value, null);
        IEnumerable<Node> Nodes(Node node)
        {
            yield return node;
            foreach (Node child in node.GetChildren()) foreach (Node nested in Nodes(child)) yield return nested;
        }
        int checks = 0;
        void Check(bool condition, string reason)
        { checks++; if (!condition) throw new InvalidOperationException("RuntimeIssuesUi: " + reason); }
        async Task Frames()
        { for (int i = 0; i < 5; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        var screenshots = new List<string>();
        var geometry = new List<object>();
        var seedEvidence = new List<object>();
        var host = new Control { Size = new(1600, 900) }; root.AddChild(host);
        var shell = new WorkspaceShell();
        shell.InitializeWithPersistence(runtime, Path.Combine(output, "runtime-issues-state", Guid.NewGuid().ToString("N")));
        host.AddChild(shell); shell.Open();
        var ledger = RuntimeIssueLedger.Current;
        try
        {
            await Frames();
            var entry = shell.GetNode<Button>("RuntimeIssuesEntry");
            var body = Field<Control>(shell, "_content");
            var scroll = shell.GetNode<ScrollContainer>("RuntimeIssuesBodyScroll");
            Check(ledger.Snapshot().Count == 0 && !entry.Visible && body.GetParent() == scroll &&
                scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled && scroll.Size == new Vector2(1536, 772),
                "clean shell retains full body with a passive non-scrolling host");
            int bodyTreeExits = 0;
            body.TreeExiting += () => bodyTreeExits++;
            foreach (string raw in new[] { "16_10_2026", "16_10_2026_2p", "  oi-test  ", "000000067C0S" })
            {
                string native = MegaCrit.Sts2.Core.Helpers.SeedHelper.CanonicalizeSeed(raw);
                Check(ProfileSeedRules.TryCanonicalizePredictionSeed(Beta111Profile.Instance, raw, out string canonical, out _) &&
                    canonical == native, "prediction preserves native canonical input " + raw);
                var nativeRng = new MegaCrit.Sts2.Core.Runs.RunRngSet(native);
                ulong rootHash = Beta111Profile.Instance.ComputeRootSeed(canonical);
                Check(rootHash == nativeRng.Seed, "prediction root equals actual native RunRngSet hash " + raw);
                var projectedStream = new Xoshiro256StarStar(Beta111Profile.Instance.DeriveNamedStreamSeed(rootHash, "up_front"));
                for (int draw = 0; draw < 8; draw++)
                    Check(projectedStream.NextInt(10000) == nativeRng.UpFront.NextInt(10000),
                        "native named stream draw parity " + raw + " draw " + draw);
                Check(rootHash != Beta111Profile.Instance.ComputeRootSeed(canonical.PadLeft(12, '0')) || canonical.Length >= 12,
                    "short input is never padded to a different RNG root");
                Check(Beta111Profile.Instance.TryCanonicalizeSeed(raw, out _, out _) == (raw == "000000067C0S"),
                    "Search still admits only its generated fixed length base34 inputs");
                seedEvidence.Add(new { raw, canonical, length = canonical.Length, rootHash });
            }
            Check(!ProfileSeedRules.TryCanonicalizePredictionSeed(Beta111Profile.Instance, "  ", out _, out _) &&
                !ProfileSeedRules.TryCanonicalizePredictionSeed(Beta110Profile.Instance, "16_10_2026", out _, out _),
                "empty prediction and historical profile contracts remain rejected");
            Check(new Base34SeedEnumerator(Beta111Profile.Instance, "000000000000").SeedAt(1) == "000000000001",
                "standard twelve-character Search enumeration is unchanged");
            try { _ = new Base34SeedEnumerator(Beta111Profile.Instance, "16_10_2026"); Check(false, "Search rejects daily seed"); }
            catch (ArgumentException) { Check(true, "Search rejects daily seed instead of inventing an ordinal"); }
            var libraryContext = new SeedLibraryContext("0.111.0", RuntimeProfileId.Beta111, WorldGameMode.Singleplayer, 0,
                [new SeedLibraryPlayer(0, BaseGameModelKeys.Characters.Silent,
                    MegaCrit.Sts2.Core.Unlocks.UnlockState.all.ToSerializable(), "NativeSeedContractCheck",
                    AncientOptionConditionProfile.BroadDefault, SearchQuery.Empty, null)]);
            string libraryState = Path.Combine(output, "native-seed-library");
            var library = new SeedLibraryStore(libraryState, []);
            var saved = library.Save("16_10_2026", libraryContext, "daily seed input contract");
            var reloaded = new SeedLibraryStore(libraryState, []);
            Check(reloaded.LoadIssues.Count == 0 && reloaded.TryFind("16_10_2026", libraryContext, out var reopened) &&
                reopened.Seed == saved.Seed && reopened.CanOpen,
                "ten-character native seed favorite roundtrips without padding or a schema change");
            RuntimeAuthorityEnvironment.CaptureOnMainThread(runtime);
            typeof(WorkspaceShell).GetMethod("CreatePredictor", flags)!.Invoke(shell, [null]);
            var controller = Field<AnalysisPageController>(shell, "_predictorController");
            controller.OpenPredictionContext(saved.Seed, libraryContext);
            var predictor = Field<AnalysisPage>(shell, "_predictor");
            Check(predictor.LastDocument?.CanonicalSeed == "16_10_2026" &&
                predictor.LastDocument.Sections.Count > 0, "native ten-character saved prediction reopens through the real controller");
            Check(predictor.CanFavoriteVisibleSeed, "native seed remains a valid committed input eligible for a favorite");
            Check(ledger.Snapshot().Count == 0, "native seed reopening does not create a retained computation failure");
            Field<Button>(shell, "_analysis").EmitSignal(Button.SignalName.Pressed);
            Check(predictor.Visible && !Field<Control>(shell, "_references").Visible,
                "actual Analysis navigation presents one workspace page for screenshots");
            FormatException CaptureFailure() => new("Expected 12 canonical seed characters, got 10.\n" +
                string.Join("\n", Enumerable.Range(0, 90).Select(i => $"original evidence line {i}: " + new string('W', 240))),
                new InvalidOperationException("Original inner exception remains available."));
            ledger.Record(CaptureFailure(),
                "LivePredictionCaptureFailed", nameof(WorkspaceShell), "CaptureLiveRun", "issues.prediction_failed");
            await Frames();
            Check(entry.IsVisibleInTree(), "recorded capture failure is visible");
            Check(entry.TooltipText.Length == 0 && entry.GetThemeFontSize("font_size") >= 16,
                "notice uses readable body text with no added tooltip");
            Check(entry.GetThemeStylebox("normal") is StyleBoxFlat box && box.BgColor.A == 1 &&
                box.ContentMarginLeft >= 16 && box.ContentMarginRight >= 16 &&
                box.ContentMarginTop > 0 && box.ContentMarginBottom > 0,
                "notice has an opaque contained surface and real text padding");
            Check(body.GetParent() == scroll && body.Size == new Vector2(1536, 772),
                "authored page dimensions are retained with a compact issue entry");
            foreach (var viewportSize in new[] { new Vector2I(1600, 900), new Vector2I(1280, 720) })
                foreach (string language in new[] { "en", "zh" })
                foreach (string font in new[] { "game-sans", "game-serif" })
                {
                    root.GetWindow().Size = viewportSize; host.Size = viewportSize;
                    var workspace = Field<SearchWorkspace>(shell, "_persistence");
                    workspace.SetLanguageOverride(language);
                    workspace.SetFontChoice(font);
                    Call(shell, "RefreshWorkspaceFont");
                    Call(shell, "RefreshLanguage");
                    Call(shell, "RefreshIssues"); await Frames();
                    Check(MainPanelTheme.FontChoice == font && entry.GetThemeFont("font") == MainPanelTheme.Regular,
                        "compact issue entry uses the current shared font " + font);
                    Rect2 bounds = shell.GetGlobalRect(), entryRect = entry.GetGlobalRect();
                    Rect2 divider = shell.GetNode<Control>("WorkspaceHeaderDivider").GetGlobalRect();
                    Check(bounds.Grow(1).Encloses(entryRect) && entryRect.Position.Y >= scroll.GetGlobalRect().End.Y &&
                        Math.Abs(entryRect.End.X - scroll.GetGlobalRect().End.X) <= shell.Scale.X &&
                        entryRect.Size.X <= 220 * shell.Scale.X,
                        "compact issue entry fits in the bottom right gutter at " + viewportSize + language);
                    Check(scroll.Position == new Vector2(32, 96) && scroll.Size == new Vector2(1536, 772) &&
                        scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled &&
                        bounds.Grow(1).Encloses(scroll.GetGlobalRect()) && divider.End.Y < scroll.GetGlobalRect().Position.Y,
                        "issue entry preserves full authored page viewport below the existing header divider");
                    Check(body.Size == new Vector2(1536, 772) && scroll.GetHScrollBar().Visible == false,
                        "full authored page width retained without horizontal scroll");
                    Check(entry.Text.Length > 0 && !entry.Text.Contains(JsonUiTextProvider.CreateUi13(language).Get("issues.prediction_failed")),
                        "localized compact entry avoids the latest-cause summary");
                    scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue; await Frames();
                    Check(body.GetGlobalRect().End.Y <= scroll.GetGlobalRect().End.Y + 1,
                        "full page bottom remains visible without outer scrolling");
                    scroll.ScrollVertical = 0; await Frames();
                    string name = $"runtime-issues-{language}-{font}-{viewportSize.X}.png";
                    RenderingServer.ForceDraw();
                    Check(root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, name)) == Error.Ok,
                        "native screenshot " + name); screenshots.Add(name);
                    geometry.Add(new { language, font, width = viewportSize.X, height = viewportSize.Y,
                        scale = shell.Scale.X, notice = entryRect.ToString(), divider = divider.ToString(),
                        bodyViewport = scroll.GetGlobalRect().ToString(), pageSize = body.Size.ToString() });
                }
            entry.EmitSignal(Button.SignalName.Pressed); await Frames();
            var panel = shell.GetNode<PanelContainer>("RuntimeIssuesPanel");
            var details = Nodes(panel).OfType<TextEdit>().Single();
            Check(!details.Visible, "exception detail remains collapsed initially");
            string detailsLabel = JsonUiTextProvider.CreateUi13("zh").Get("issues.details");
            Nodes(panel).OfType<Button>().Single(button => button.Text == detailsLabel).EmitSignal(Button.SignalName.Pressed);
            await Frames();
            Check(details.IsVisibleInTree() && details.Text.Contains("LivePredictionCaptureFailed") &&
                details.Text.Contains("Expected 12 canonical seed characters, got 10.") &&
                shell.GetGlobalRect().Grow(1).Encloses(panel.GetGlobalRect()),
                "expanded original cause is readable within the detail panel");
            Check(Nodes(panel).OfType<Button>().Any(button => button.Text ==
                JsonUiTextProvider.CreateUi13("zh").Get("issues.copy")), "copy evidence action remains available");
            RenderingServer.ForceDraw();
            const string detailScreenshot = "runtime-issues-detail-zh-1280.png";
            Check(root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, detailScreenshot)) == Error.Ok,
                "native detail screenshot"); screenshots.Add(detailScreenshot);

            // Use the real ledger refresh path while reading an expanded long cause.
            // Independent new exceptions with the same evidence share a stable Id.
            string Payload(RuntimeIssue issue) => $"{issue.Code}\n{issue.Source}/{issue.Stage}\n{issue.CorrelationId}\n{issue.ExceptionDetail}";
            // Native text controls and the Windows clipboard may normalize CRLF.
            // Compare every identity/cause character while allowing that transport change.
            string NormalizeLines(string value) => value.Replace("\r\n", "\n");
            bool SameEvidence(string actual, RuntimeIssue expected) => NormalizeLines(actual) == NormalizeLines(Payload(expected));
            string copyLabel = JsonUiTextProvider.CreateUi13("zh").Get("issues.copy");
            void CopyDetail(TextEdit detail) => Nodes(detail.GetParent()).OfType<Button>()
                .Single(button => button.Text == copyLabel).EmitSignal(Button.SignalName.Pressed);
            RuntimeIssue original = ledger.Snapshot().Single();
            CopyDetail(details);
            Check(SameEvidence(DisplayServer.ClipboardGet(), original) &&
                details.Text.Contains("Original inner exception remains available."),
                "copy contains the complete original exception chain and identity");
            for (int i = 0; i < 8; i++)
                ledger.Record(new InvalidOperationException("Independent retained issue " + i),
                    "ReadingFixture" + i, nameof(UiSurfaceChecks), "ReadingFixture", "issues.prediction_failed");
            await Frames();
            var detailScroll = Field<ScrollContainer>(shell, "_issuesDetailScroll");
            TextEdit secondDetails = Nodes(panel).OfType<TextEdit>().First(detail => detail != details);
            Nodes(secondDetails.GetParent()).OfType<Button>().Single(button => button.Text == detailsLabel)
                .EmitSignal(Button.SignalName.Pressed);
            await Frames();
            detailScroll.ScrollVertical = 100;
            details.ScrollVertical = 18;
            details.ScrollHorizontal = 90;
            await Frames();
            int listReadingPosition = detailScroll.ScrollVertical;
            double causeReadingPosition = details.ScrollVertical;
            int causeHorizontalPosition = details.ScrollHorizontal;
            Vector2 readingTargetPosition = details.GetGlobalRect().Position;
            Check(listReadingPosition > 0 && causeReadingPosition > 0 && causeHorizontalPosition > 0 &&
                details.Visible && secondDetails.Visible, "reading fixture has two expanded rows and nonzero outer/inner scroll");
            int groupsBeforeArrival = ledger.Snapshot().Count;
            RuntimeIssue arrival = ledger.Record(new InvalidOperationException("New arrival evidence",
                new ArgumentException("New arrival inner cause")), "ReadingNewArrival", nameof(UiSurfaceChecks),
                "ReadingArrival", "issues.prediction_failed")!;
            await Frames();
            Check(shell.GetNode<PanelContainer>("RuntimeIssuesPanel") == panel &&
                Nodes(panel).OfType<TextEdit>().Contains(details) && details.Visible && secondDetails.Visible &&
                detailScroll.ScrollVertical == listReadingPosition && details.ScrollVertical == causeReadingPosition &&
                details.ScrollHorizontal == causeHorizontalPosition && details.GetGlobalRect().Position == readingTargetPosition,
                "new issue preserves open rows, inner text reading positions and the visible list anchor");
            var arrivalDetails = Nodes(panel).OfType<TextEdit>().Single(detail => SameEvidence(detail.Text, arrival));
            Check(ledger.Snapshot().Count == groupsBeforeArrival + 1 &&
                Nodes(panel).OfType<TextEdit>().Count() == ledger.Snapshot().Count && !arrivalDetails.Visible,
                "new issue is retained as a new initially collapsed row");
            CopyDetail(arrivalDetails);
            Check(SameEvidence(DisplayServer.ClipboardGet(), arrival) &&
                DisplayServer.ClipboardGet().Contains("New arrival inner cause"),
                "new issue copy contains its current full exception evidence");
            RuntimeIssue repeated = ledger.Record(CaptureFailure(), "LivePredictionCaptureFailed",
                nameof(WorkspaceShell), "CaptureLiveRun", "issues.prediction_failed")!;
            await Frames();
            Check(repeated.Id == original.Id && repeated.OccurrenceCount == original.OccurrenceCount + 1 &&
                ledger.Snapshot()[0].Id == original.Id && ledger.Snapshot().Count == groupsBeforeArrival + 1,
                "independent same-group exception increments count and ledger recency without creating another row");
            Check(details.Visible && secondDetails.Visible &&
                detailScroll.ScrollVertical == listReadingPosition && details.ScrollVertical == causeReadingPosition &&
                details.ScrollHorizontal == causeHorizontalPosition && details.GetGlobalRect().Position == readingTargetPosition &&
                Nodes(panel).OfType<TextEdit>().Count() == ledger.Snapshot().Count,
                "same-group refresh preserves both expanded rows and both reading positions despite newest-first ledger order");
            string expectedMetadata = JsonUiTextProvider.CreateUi13("zh").Format("issues.metadata",
                repeated.LastOccurredUtc.ToLocalTime().ToString("HH:mm:ss"), repeated.OccurrenceCount);
            Check(details.GetParent().GetChildren().OfType<Label>().Any(label => label.Text == expectedMetadata),
                "same-group row metadata shows its updated time and occurrence count");
            CopyDetail(details);
            Check(SameEvidence(DisplayServer.ClipboardGet(), repeated) && SameEvidence(details.Text, original),
                "repeat retains the complete original chain for reading and copying");
            geometry.Add(new { readingState = "new-and-repeat", listReadingPosition, causeReadingPosition,
                causeHorizontalPosition, expandedRows = 2, originalIssue = original.Id, repeated.OccurrenceCount,
                groups = ledger.Snapshot().Count });

            // The view follows the bounded ledger; eviction above a surviving
            // reading target must move the scrollbar with that target.
            int toCapacity = 64 - ledger.Snapshot().Count;
            for (int i = 0; i < toCapacity; i++)
                ledger.Record(new InvalidOperationException("Capacity evidence " + i), "ReadingCapacity" + i,
                    nameof(UiSurfaceChecks), "ReadingCapacity", "issues.prediction_failed");
            await Frames();
            Nodes(arrivalDetails.GetParent()).OfType<Button>().Single(button => button.Text == detailsLabel)
                .EmitSignal(Button.SignalName.Pressed);
            await Frames();
            detailScroll.ScrollVertical = (int)((Control)arrivalDetails.GetParent()).Position.Y + 20;
            await Frames();
            Vector2 capacityAnchor = arrivalDetails.GetGlobalRect().Position;
            Guid evictedId = ledger.Snapshot()[^1].Id;
            ledger.Record(new InvalidOperationException("Capacity overflow evidence"), "ReadingCapacityOverflow",
                nameof(UiSurfaceChecks), "ReadingCapacity", "issues.prediction_failed");
            await Frames();
            Check(ledger.Snapshot().Count == 64 && ledger.Snapshot().All(issue => issue.Id != evictedId) &&
                Nodes(panel).OfType<TextEdit>().Count() == 64 && arrivalDetails.Visible &&
                arrivalDetails.GetGlobalRect().Position.DistanceTo(capacityAnchor) <= shell.Scale.Y,
                "capacity eviction removes only expired evidence and preserves the surviving reading anchor");
            shell.HandleBack(); await Frames();
            Check(!shell.HasNode("RuntimeIssuesPanel") && entry.IsVisibleInTree(),
                "Escape closes details while preserving the visible error entry");

            // There is intentionally no production dismiss/clear action. Only the
            // isolated process fixture clears its own ledger to check layout restoration.
            foreach (string field in new[] { "_groups", "_recent" })
            {
                object collection = typeof(RuntimeIssueLedger).GetField(field, flags)!.GetValue(ledger)!;
                collection.GetType().GetMethod("Clear")!.Invoke(collection, null);
            }
            typeof(WorkspaceShell).GetField("_issuesRevision", flags)!.SetValue(shell, -1L);
            Call(shell, "RefreshIssues"); await Frames();
            Check(!entry.Visible && body.GetParent() == scroll && scroll.Position == new Vector2(32, 96) &&
                body.Size == new Vector2(1536, 772) && scroll.Size == new Vector2(1536, 772) &&
                scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled && bodyTreeExits == 0,
                "empty issue state restores original body geometry without tearing down any page");
            File.WriteAllText(Path.Combine(output, "runtime-issues-ui-checks.json"), JsonSerializer.Serialize(
                new { passed = true, checks, screenshots, geometry, seedEvidence, seedScans = 0, nativeGameMutation = false },
                new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"RUNTIME_ISSUES_UI checks={checks};failures=0;screenshots={screenshots.Count};seedScans=0");
        }
        finally { host.QueueFree(); }
    }
}
