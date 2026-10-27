using System.Reflection;
using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task CheckDrawingNarrowSummaryGeometry(Node root, string output, NMapScreen screen,
        InRunMapNodeDetails details, InRunMapNodeDetailData actual, OperationPreview actualPreview,
        InRunMapDetailSection[] actualRewards, IGameContentNameResolver names, string language, Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var previousSize = root.GetWindow().Size;
        var receipts = new List<object>();
        // Native identities and localized names, deliberately combined only
        // for a bounded layout fixture. This does not assert that one event
        // naturally awards these particular relics and potions together.
        var relics = ModelDb.AllRelics.Where(model => !new[] { "MOCK", "TEST", "DEPRECATED" }.Any(prefix =>
                model.Id.Entry.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(model => new ModelKey(model.Id.Category, model.Id.Entry))
            .OrderByDescending(key => names.Resolve(key, GameContentKind.Relic).Length).Take(2).ToImmutableArray();
        var potions = new[] { "DROPLET_OF_PRECOGNITION", "BLESSING_OF_THE_FORGE" }
            .Select(entry => ModelDb.AllPotions.Single(model => model.Id.Entry == entry))
            .Select(model => new ModelKey(model.Id.Category, model.Id.Entry)).ToImmutableArray();
        var summary = actualPreview with
        {
            OptionKey = "geometry-fixture:narrow-native-summary",
            Style = new(OperationTextMode.GroupRows, OperationCardLayout.Stack),
            Branches = actualPreview.Branches.SetItem(0, actualPreview.Branches[0] with
            {
                Sections = actualPreview.Branches[0].Sections.Add(new(actualPreview.Source,
                    [new(OperationPreviewAction.ObtainRelic, relics, [], false),
                     new(OperationPreviewAction.GeneratePotion, potions, [], false)], []))
            })
        };
        async Task Layout()
        {
            await DiagnosticFrame(root);
            typeof(InRunMapNodeDetails).GetMethod("LayoutHover", members)!.Invoke(details, null);
            await DiagnosticFrame(root);
        }
        bool PreviewContains(Vector2 point) => (bool)typeof(InRunMapNodeDetails)
            .GetMethod("PreviewContains", members)!.Invoke(details, [point])!;
        IEnumerable<ScrollContainer> ScrollParents(Control control)
        {
            for (Node? parent = control.GetParent(); parent != null && parent != details; parent = parent.GetParent())
                if (parent is ScrollContainer scroll) yield return scroll;
        }
        try
        {
            root.GetWindow().Size = new(800, 720); await DiagnosticFrame(root);
            foreach (var sample in new[] { (WithCombat: false, AnchorY: 250f, Suffix: "False", Move: false, Information: false),
                (WithCombat: true, AnchorY: 250f, Suffix: "True", Move: false, Information: false),
                (WithCombat: true, AnchorY: 650f, Suffix: "bottom-True", Move: false, Information: false),
                (WithCombat: false, AnchorY: 250f, Suffix: "move-False", Move: true, Information: false),
                (WithCombat: true, AnchorY: 250f, Suffix: "move-True", Move: true, Information: false),
                (WithCombat: false, AnchorY: 250f, Suffix: "move-information-False", Move: true, Information: true),
                (WithCombat: true, AnchorY: 250f, Suffix: "move-information-True", Move: true, Information: true) })
            {
                bool withCombat = sample.WithCombat;
                details.CloseHover();
                string scope = "geometry-fixture:narrow-summary:" + sample.Suffix;
                var hover = new InRunMapDetailChoice(scope, actualPreview.Heading ?? actual.Title) { Preview = summary };
                var data = actual with
                {
                    NodeId = scope, IsCurrent = null, Premises = null, Subtitle = "",
                    Information = sample.Information ? Enumerable.Range(0, 8).Select(index =>
                        "Geometry prerequisite fixture " + index + ": known source-dependent information stays in its bounded scrolling area.").ToArray() : [],
                    Sections = [new("", "", [], [hover]) { BranchRow = true },
                        .. (withCombat ? actualRewards.Select(section => section with { CombatPanel = true }) :
                            Enumerable.Empty<InRunMapDetailSection>())]
                };
                var anchor = new Rect2(new(370, sample.AnchorY), new(56, 56));
                details.ShowNode(data, anchor, screen.GetGlobalRect());
                await Layout();
                var before = details.HoverGlobalRegion;
                var button = Descendants(details).OfType<Button>().Single(button => button.IsVisibleInTree() &&
                    button.HasMeta("choice_id") && button.GetMeta("choice_id").AsString() == hover.Id);
                button.EmitSignal(Control.SignalName.MouseEntered); await Layout();
                long openedGeneration = (long)DiagnosticField(details, "_hoverGeneration");
                var middleMain = details.HoverGlobalRegion;
                if (sample.Move)
                {
                    anchor = new(new(370, 650), new(56, 56));
                    details.UpdateAnchor(anchor, screen.GetGlobalRect());
                    await Layout();
                    // The source moved, so its panel may move with it. Later
                    // summary scrolling must preserve this new settled origin.
                    before = details.HoverGlobalRegion;
                }
                bool generationRetained = openedGeneration == (long)DiagnosticField(details, "_hoverGeneration");
                var prerequisiteScroll = DiagnosticField(details, "_prerequisiteScroll") as ScrollContainer;
                bool premiseAreaBounded = !sample.Information || prerequisiteScroll?.Size.Y is >= 63 and <= 65;
                var viewport = (ScrollContainer)DiagnosticField(details, "_previewViewport");
                var overlay = (NeowOperationOverlay)DiagnosticField(details, "_optionPreview");
                var canvas = details.GetCanvasTransform().AffineInverse() * details.GetViewportRect();
                var surfaces = new[] { "_hover", "_combatPanel", "_previewViewport", "_previewCardsViewport" }
                    .Select(field => DiagnosticField(details, field) as Control).Where(control => control?.IsVisibleInTree() == true)
                    .Cast<Control>().ToArray();
                var actualSurfaces = new[] { "_surface", "_frame", "_combatSurface", "_combatFrame" }
                    .Select(field => DiagnosticField(details, field) as Control).Where(control => control != null &&
                        GodotObject.IsInstanceValid(control) && control.IsVisibleInTree())
                    .Cast<Control>().ToArray();
                var mainSurface = (Control)DiagnosticField(details, "_surface");
                var mainFrame = (Control)DiagnosticField(details, "_frame");
                var combatSurface = DiagnosticField(details, "_combatSurface") as Control;
                var combatFrame = DiagnosticField(details, "_combatFrame") as Control;
                bool SameBounds(Control first, Control second) => first.GetGlobalRect().Position.IsEqualApprox(second.GetGlobalRect().Position) &&
                    first.GetGlobalRect().Size.IsEqualApprox(second.GetGlobalRect().Size);
                bool paintedBoundsMatch = SameBounds(mainSurface, mainFrame) &&
                    (!withCombat || combatSurface != null && combatFrame != null && SameBounds(combatSurface, combatFrame));
                var readingAreas = new[] { mainSurface, withCombat ? combatSurface : null, viewport }
                    .OfType<Control>().Where(control => control.IsVisibleInTree()).ToArray();
                bool paintedAreasSeparate = readingAreas.SelectMany((area, index) => readingAreas.Skip(index + 1)
                    .Select(other => (area, other))).All(pair => !pair.area.GetGlobalRect().Intersects(pair.other.GetGlobalRect()));
                var labels = Descendants(overlay).OfType<Label>().Where(label => label.IsVisibleInTree()).ToArray();
                var nameLabels = labels.Where(label => relics.Any(key => label.Text.Contains(names.Resolve(key, GameContentKind.Relic), StringComparison.Ordinal)) ||
                    potions.Any(key => label.Text.Contains(names.Resolve(key, GameContentKind.Potion), StringComparison.Ordinal))).ToArray();
                var clipping = nameLabels.Where(label => label.GetGlobalRect().Position.X < viewport.GetGlobalRect().Position.X - 1 ||
                    label.GetGlobalRect().End.X > viewport.GetGlobalRect().End.X + 1).ToArray();
                var ordered = surfaces.Where(surface => surface.Name.ToString() != "NodeOptionCardsViewport")
                    .OrderBy(surface => surface.GetGlobalRect().Position.Y).ToArray();
                var gaps = ordered.Zip(ordered.Skip(1)).Select(pair =>
                {
                    var upper = pair.First.GetGlobalRect(); var lower = pair.Second.GetGlobalRect();
                    float x = (Math.Max(upper.Position.X, lower.Position.X) + Math.Min(upper.End.X, lower.End.X)) * .5f;
                    var midpoint = new Vector2(x, (upper.End.Y + lower.Position.Y) * .5f);
                    return new { Upper = pair.First.Name.ToString(), Lower = pair.Second.Name.ToString(), Point = midpoint.ToString(),
                        RetainsHover = details.ContainsPointer(midpoint), RetainsPreview = PreviewContains(midpoint),
                        BlocksMap = details.BlocksMapInput(midpoint) };
                }).ToArray();
                var combat = surfaces.SingleOrDefault(surface => surface.Name.ToString() == "EventCombatPanel");
                var combatCenter = combat?.GetGlobalRect().GetCenter();
                var combatPath = combatCenter is { } center ? new
                {
                    Point = center.ToString(), RetainsHover = details.ContainsPointer(center),
                    RetainsPreview = PreviewContains(center), BlocksMap = details.BlocksMapInput(center)
                } : null;
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, $"drawing-narrow-summary-{sample.Suffix}-{language}-top.png"));
                // The map's outer viewport owns user scrolling. Never move an
                // inner MouseIgnore scrollbar to manufacture visible content.
                var tail = nameLabels.LastOrDefault();
                var tailScrolls = tail == null ? Array.Empty<ScrollContainer>() : ScrollParents(tail).ToArray();
                if (tail != null)
                {
                    viewport.EnsureControlVisible(tail);
                    await Layout();
                }
                Rect2 tailRect = tail?.GetGlobalRect() ?? default;
                Rect2 tailVisible = tailRect.Intersection(canvas);
                foreach (var scroll in tailScrolls) tailVisible = tailVisible.Intersection(scroll.GetGlobalRect());
                bool tailReadable = tail != null && tailVisible.Size.X >= Math.Min(24, tailRect.Size.X) - 1 &&
                    tailVisible.Size.Y >= tailRect.Size.Y - 1;
                var tailReceipt = new
                {
                    Text = tail?.Text, Rect = tailRect.ToString(), VisiblePart = tailVisible.ToString(), Readable = tailReadable,
                    Scrolls = tailScrolls.Select(scroll => new
                    {
                        Path = scroll.GetPath().ToString(), Rect = scroll.GetGlobalRect().ToString(),
                        MouseFilter = scroll.MouseFilter.ToString(), scroll.ScrollVertical,
                        Vertical = new { scroll.GetVScrollBar().Value, scroll.GetVScrollBar().Page, scroll.GetVScrollBar().MaxValue }
                    }).ToArray()
                };
                var receipt = new
                {
                    withCombat, sample.Move, sample.Information, Anchor = anchor.ToString(), Canvas = canvas.ToString(),
                    MiddleMain = middleMain.ToString(), Before = before.ToString(), Main = details.HoverGlobalRegion.ToString(),
                    openedGeneration, generationRetained, premiseAreaBounded, Prerequisite = prerequisiteScroll?.GetGlobalRect().ToString(),
                    Summary = viewport.GetGlobalRect().ToString(), DesiredSummary = overlay.SummaryDesiredSize.ToString(),
                    Relics = relics.Select(key => new { Key = key.ToString(), Name = names.Resolve(key, GameContentKind.Relic) }).ToArray(),
                    Potions = potions.Select(key => new { Key = key.ToString(), Name = names.Resolve(key, GameContentKind.Potion) }).ToArray(),
                    Labels = nameLabels.Select(label => new { label.Text, Rect = label.GetGlobalRect().ToString(),
                        Minimum = label.GetCombinedMinimumSize().ToString() }).ToArray(),
                    Surfaces = surfaces.Select(surface => new { Name = surface.Name.ToString(), Rect = surface.GetGlobalRect().ToString() }).ToArray(),
                    PaintedSurfaces = actualSurfaces.Select(surface => new { Name = surface.Name.ToString(), Rect = surface.GetGlobalRect().ToString(),
                        Minimum = surface.GetCombinedMinimumSize().ToString() }).ToArray(), paintedBoundsMatch, paintedAreasSeparate,
                    gaps, combatPath, tailReceipt
                };
                receipts.Add(receipt);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, $"drawing-narrow-summary-{sample.Suffix}-{language}.png"));
                check(viewport.IsVisibleInTree() && nameLabels.Length >= 4 && clipping.Length == 0,
                    "native-name narrow summary geometry fixture keeps each real long relic/potion name within its viewport; combat=" + withCombat +
                    "/" + language + "; geometry=" + JsonSerializer.Serialize(receipt));
                check(generationRetained && premiseAreaBounded,
                    "narrow summary source motion keeps the same open control generation and bounded premise area; " +
                    sample.Suffix + "/" + language + "; geometry=" + JsonSerializer.Serialize(receipt));
                check(tailReadable,
                    "native-name narrow summary geometry fixture exposes its full tail item using only the outer vertical scrollbar; combat=" +
                    withCombat + "/" + language + "; geometry=" + JsonSerializer.Serialize(tailReceipt));
                check(surfaces.Where(surface => surface.Name.ToString() != "ContinuousHoverRegion")
                        .All(surface => !surface.GetGlobalRect().Intersects(details.HoverGlobalRegion)) &&
                    details.HoverGlobalRegion.Position.IsEqualApprox(before.Position) && Math.Abs(details.HoverGlobalRegion.Size.X - before.Size.X) <= 1,
                    "narrow summary geometry fixture keeps auxiliary surfaces separate while its source-relative main option remains stable; combat=" + withCombat + "/" + language);
                check(paintedBoundsMatch && paintedAreasSeparate,
                    "narrow summary geometry fixture keeps the actual painted main/combat surfaces and matching frames separate even when the source moves beyond the canvas; " +
                    sample.Suffix + "/" + language + "; geometry=" + JsonSerializer.Serialize(receipt));
                check(gaps.All(gap => gap.RetainsHover && !gap.BlocksMap),
                    "narrow summary geometry fixture retains the short main-to-" + (withCombat ? "combat-to-" : "") +
                    "summary path without owning map clicks in " + language + "; geometry=" + JsonSerializer.Serialize(receipt));
                check(gaps.All(gap => gap.RetainsPreview) && (!withCombat || combatPath is
                    { RetainsHover: true, RetainsPreview: true, BlocksMap: true }),
                    "narrow summary geometry fixture keeps the option preview alive through the actual main/combat/summary path while retaining combat input ownership; combat=" +
                    withCombat + "/" + language + "; geometry=" + JsonSerializer.Serialize(receipt));
                if (!sample.Move && sample.AnchorY == 250)
                {
                    var initialOffset = details.HoverGlobalRegion.Position - anchor.Position;
                    var cardViewport = (Control)DiagnosticField(details, "_previewCardsViewport");
                    int Side(Control control)
                    {
                        var rect = control.GetGlobalRect(); var main = details.HoverGlobalRegion;
                        return rect.End.Y <= main.Position.Y - 9 ? -1 : rect.Position.Y >= main.End.Y + 9 ? 1 : 0;
                    }
                    int[] Sides() => new[] { cardViewport, viewport, combat }.OfType<Control>()
                        .Where(control => control.IsVisibleInTree()).Select(Side).ToArray();
                    async Task At(float y)
                    {
                        anchor = new(new(370, y), new(56, 56));
                        details.UpdateAnchor(anchor, screen.GetGlobalRect()); await Layout();
                        var reading = new[] { cardViewport, viewport, combat }.OfType<Control>()
                            .Where(control => control.IsVisibleInTree()).ToArray();
                        bool separate = reading.All(control => Side(control) != 0) &&
                            reading.SelectMany((control, index) => reading.Skip(index + 1)
                                .Select(other => (control, other))).All(pair => !pair.control.GetGlobalRect().Intersects(pair.other.GetGlobalRect()));
                        receipts.Add(new { Scenario = "edge-adaptive", sample.Suffix, SourceY = y,
                            Anchor = anchor.ToString(), Main = details.HoverGlobalRegion.ToString(),
                            Offset = (details.HoverGlobalRegion.Position - anchor.Position).ToString(), separate,
                            Surfaces = reading.Select(control => new { Name = control.Name.ToString(),
                                Side = Side(control), Rect = control.GetGlobalRect().ToString() }).ToArray() });
                        if (y is 12 or 650)
                        {
                            using var edgeImage = root.GetViewport().GetTexture().GetImage();
                            edgeImage.SavePng(Path.Combine(output, $"drawing-edge-adaptive-{sample.Suffix}-{y}-{language}.png"));
                        }
                        check(Math.Abs(details.HoverGlobalRegion.Position.X - anchor.Position.X - initialOffset.X) < 1 && separate,
                            "edge-adaptive card/child allocation keeps horizontal source anchoring and separate same-side surfaces; sourceY=" + y +
                            ";combat=" + withCombat + "/" + language);
                    }
                    await At(12);
                    check(cardViewport.IsVisibleInTree() && Side(cardViewport) == 1 && Side(viewport) == 1,
                        "top-edge cards flip below beside child information without moving the main in " + sample.Suffix + "/" + language);
                    await At(650);
                    check(Side(cardViewport) == -1 && Side(viewport) == -1,
                        "bottom-edge cards and child information share the upper side without overlap in " + sample.Suffix + "/" + language);
                    await At(500);
                    var retainedSides = Sides();
                    foreach (float y in new[] { 498f, 502f, 499f, 501f, 500f })
                    {
                        await At(y);
                        check(Sides().SequenceEqual(retainedSides),
                            "small alternating source drags retain auxiliary sides instead of restarting default placement; sourceY=" + y +
                            ";combat=" + withCombat + "/" + language);
                    }
                    root.GetWindow().Size = new(800, 360); await DiagnosticFrame(root);
                    await At(140);
                    check(viewport.ClipContents && cardViewport.ClipContents &&
                        new[] { viewport, cardViewport as ScrollContainer }.OfType<ScrollContainer>().Any(scroll =>
                            scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page ||
                            !((details.GetCanvasTransform().AffineInverse() * details.GetViewportRect()).Grow(1)).Encloses(scroll.GetGlobalRect())),
                        "when neither side fits, auxiliary content scrolls or clips while the main retains its horizontal source side in " + sample.Suffix + "/" + language);
                    // A different source owns a fresh placement preference.
                    // Give both defaults room so this tests reset rather than
                    // another shortage-driven flip on the new node.
                    root.GetWindow().Size = new(1680, 1080); await DiagnosticFrame(root);
                    anchor = new(new(370, 400), new(56, 56));
                    details.ShowNode(data with { NodeId = scope + ":fresh-source", Information = [],
                        Sections = [new("", "", [], [hover]) { BranchRow = true }] }, anchor, screen.GetGlobalRect());
                    await Layout();
                    var freshMain = details.HoverGlobalRegion;
                    var freshButton = Descendants(details).OfType<Button>().Single(value => value.HasMeta("choice_id") &&
                        value.GetMeta("choice_id").AsString() == hover.Id);
                    freshButton.EmitSignal(Control.SignalName.MouseEntered); await Layout();
                    check(Side(cardViewport) == -1 && Side(viewport) == 1 &&
                        details.HoverGlobalRegion.Position.IsEqualApprox(freshMain.Position),
                        "a fresh source resets cards-above and child-below defaults after previous edge flips without shifting its main in " + sample.Suffix + "/" + language);
                    root.GetWindow().Size = new(800, 720); await DiagnosticFrame(root);
                }
            }
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "drawing-narrow-summary-" + language + ".json"), JsonSerializer.Serialize(new
                { fixture = "controlled combination of actual combat reward/card projection and native long relic/potion identities", receipts },
                new JsonSerializerOptions { WriteIndented = true }));
            details.CloseHover(); root.GetWindow().Size = previousSize; await DiagnosticFrame(root);
        }
    }

    private static async Task CheckDrawingSavedSelectionUi(Node root, string output, NMapScreen screen,
        InRunMapNodeDetails details, Func<string, bool, InRunMapPredictionSession> fixture, int act, MapPosition point,
        IGameContentNameResolver names, IUiTextProvider text, string language, Func<string> signature, Action<bool, string> check)
    {
        string nativeBefore = signature();
        var sizeReceipts = new List<object>();
        var session = fixture("ROOM_FULL_OF_CHEESE", false);
        var presenter = new InRunMapNodePresenter(session, names, text);
        InRunMapNodeDetailData Current() => presenter.Detail((act, point))!;
        IEnumerable<InRunMapDetailSection> Sections(IEnumerable<InRunMapDetailSection> source)
        {
            foreach (var section in source)
            {
                yield return section;
                if (section.EmbeddedEditor != null)
                    foreach (var nested in Sections(section.EmbeddedEditor.Sections)) yield return nested;
            }
        }
        InRunMapDetailSection Picker(InRunMapNodeDetailData data) => Sections(data.Premises!.Sections).Single(section =>
            section.Choices.Count(choice => choice.Object?.CardSnapshot != null) >= 2 &&
            section.Choices.Any(choice => choice.Label == text.Get("predictor.runtime.confirm")));
        InRunMapDetailChoice[] Cards(InRunMapDetailSection section) => section.Choices
            .Where(choice => choice.Object?.CardSnapshot != null).ToArray();
        async Task Show(InRunMapNodeDetailData data)
        {
            details.ShowNode(data with { IsCurrent = null }, new(new(120, 400), new(56, 56)), screen.GetGlobalRect());
            await DiagnosticFrame(root);
            if (!(bool)DiagnosticField(details, "_premisesExpanded"))
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
            await DiagnosticFrame(root);
        }
        object SizeGeometry() => new
        {
            Main = details.HoverGlobalRegion.ToString(),
            SurfaceMinimum = ((Control)DiagnosticField(details, "_surface")).GetCombinedMinimumSize().ToString(),
            RootMinimum = ((Control)DiagnosticField(details, "_hoverRoot")).GetCombinedMinimumSize().ToString(),
            BodyMinimum = ((Control)DiagnosticField(details, "_hoverBody")).GetCombinedMinimumSize().ToString(),
            ScrollMinimum = ((Control)DiagnosticField(details, "_hoverScroll")).GetCombinedMinimumSize().ToString(),
            ScrollRect = ((Control)DiagnosticField(details, "_hoverScroll")).GetGlobalRect().ToString()
        };
        async Task CaptureSize(string label)
        {
            // Normal _Process relayout is deliberately suspended in this
            // offscreen pointer fixture. Reproduce its width -> container sort
            // -> height feedback explicitly before judging the final geometry.
            var samples = new List<object> { SizeGeometry() };
            Vector2 previousSize = details.HoverGlobalRegion.Size, previousBody =
                ((Control)DiagnosticField(details, "_hoverBody")).GetCombinedMinimumSize();
            bool settled = false;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                typeof(InRunMapNodeDetails).GetMethod("LayoutHover", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(details, null);
                await DiagnosticFrame(root);
                Vector2 currentSize = details.HoverGlobalRegion.Size, currentBody =
                    ((Control)DiagnosticField(details, "_hoverBody")).GetCombinedMinimumSize();
                settled = currentSize.IsEqualApprox(previousSize) && currentBody.IsEqualApprox(previousBody);
                previousSize = currentSize; previousBody = currentBody;
                samples.Add(SizeGeometry());
            }
            var surface = (Control)DiagnosticField(details, "_surface");
            var scroll = (ScrollContainer)DiagnosticField(details, "_hoverScroll");
            var body = (Control)DiagnosticField(details, "_hoverBody");
            float headerUsed = scroll.GetGlobalRect().Position.Y - surface.GetGlobalRect().Position.Y;
            float horizontalChrome = scroll.GetHScrollBar().IsVisibleInTree() ? scroll.GetHScrollBar().Size.Y : 0;
            float bottomMargin = surface.GetThemeStylebox("panel").ContentMarginBottom;
            // Read the natural body, not the scroll container's artificially
            // allocated minimum. A clipped viewport may be smaller than this
            // upper bound; empty space beyond all natural content is a defect.
            float contentUpperHeight = headerUsed + body.GetCombinedMinimumSize().Y + horizontalChrome + bottomMargin;
            sizeReceipts.Add(new { label, settled, Initial = samples[0], Final = SizeGeometry(), samples,
                headerUsed, BodyNatural = body.GetCombinedMinimumSize().ToString(), horizontalChrome, bottomMargin,
                contentUpperHeight });
            File.WriteAllText(Path.Combine(output, "drawing-selection-size-" + language + ".json"),
                JsonSerializer.Serialize(sizeReceipts, new JsonSerializerOptions { WriteIndented = true }));
            using var image = root.GetViewport().GetTexture().GetImage();
            image.SavePng(Path.Combine(output, "drawing-selection-size-" + label + "-" + language + ".png"));
            check(settled && details.HoverGlobalRegion.Size.Y <= contentUpperHeight + 2,
                label + " reaches a compact layout fixed point after eight explicit frames in " + language +
                "; actual=" + details.HoverGlobalRegion + "; naturalContentUpperHeight=" + contentUpperHeight +
                "; geometry=" + JsonSerializer.Serialize(sizeReceipts));
        }
        async Task CheckReopenedHeight(InRunMapNodeDetailData current, string label)
        {
            // The caller has just shown this live receipt. Capture its warm
            // container before any rebuild could erase the size history.
            await CaptureSize(label + "-warm");
            var warm = details.HoverGlobalRegion;
            details.CloseHover(); await Show(current);
            await CaptureSize(label + "-reopened");
            var reopened = details.HoverGlobalRegion;
            // Reopening discards the transient container-size history. Wider
            // established panels are allowed; compare only height, since a
            // retained wider panel has no reason to need more vertical space.
            check(warm.Size.Y <= reopened.Size.Y + 1,
                label + " actual content converges without retained vertical blank space in " + language +
                "; warm=" + warm + "; reopened=" + reopened + "; geometry=" + JsonSerializer.Serialize(sizeReceipts));
        }
        async Task TogglePremises()
        {
            ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
            await DiagnosticFrame(root);
            details.UpdateAnchor(new(new(120, 400), new(56, 56)), screen.GetGlobalRect());
            await DiagnosticFrame(root);
        }
        async Task Press(InRunMapNodeDetailData data, InRunMapDetailChoice choice, string label)
        {
            await Show(data);
            bool applied = false;
            void Apply(string nodeId, string choiceId) => applied = presenter.ApplyChoice(nodeId, choiceId);
            details.ChoiceRequested += Apply;
            try
            {
                var button = Descendants(details).OfType<Button>().Single(button => button.IsVisibleInTree() &&
                    button.HasMeta("choice_id") && button.GetMeta("choice_id").AsString() == choice.Id);
                // Exercise AddChoice's real deferred Pressed -> ChoiceRequested
                // callback instead of manually writing the authored child.
                button.EmitSignal(Button.SignalName.Pressed);
                await DiagnosticFrame(root);
                check(applied, label + " uses the actual rendered choice callback in " + language);
            }
            finally { details.ChoiceRequested -= Apply; }
        }
        async Task CheckSelected(InRunMapNodeDetailData data, InRunMapDetailChoice[] choices, int[] selected, string label)
        {
            await Show(data);
            var expected = choices.Select((_, index) => selected.Contains(index)).ToArray();
            check(choices.Select(choice => choice.Selected).SequenceEqual(expected) && choices.All(choice =>
                Descendants(details).OfType<Button>().Count(button => button.IsVisibleInTree() && button.HasMeta("choice_id") &&
                    button.GetMeta("choice_id").AsString() == choice.Id) == 1),
                label + " has exactly the saved/draft selected feedback on its real controls in " + language +
                    "; expected=" + string.Join(',', selected) + "; actual=" + string.Join(',', choices.Select((choice, index) =>
                        (choice, index)).Where(item => item.choice.Selected).Select(item => item.index)));
        }

        var data = Current();
        var gorge = data.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices)
            .Single(choice => choice.Label == text.Get("predictor.runtime.option.GORGE"));
        await Press(data, gorge, "Cheese GORGE parent");
        data = Current();
        string parentOnly = JsonSerializer.Serialize(session.GetPlan(act, point));
        long beforeDraft = session.Revision;
        await Press(data, Cards(Picker(data))[0], "Cheese first draft card");
        data = Current();
        await Press(data, Cards(Picker(data))[1], "Cheese second draft card");
        data = Current();
        presenter.RebindLocalization(names, text);
        data = Current();
        await CheckSelected(data, Cards(Picker(data)), [0, 1], "Cheese unconfirmed draft after ordinary localized redraw");
        check(session.Revision == beforeDraft && JsonSerializer.Serialize(session.GetPlan(act, point)) == parentOnly,
            "Cheese two-card draft redraw preserves the actual parent plan without saving its child in " + language);
        await Press(data, Picker(data).Choices.Single(choice => choice.Label == text.Get("predictor.runtime.confirm")), "Cheese draft confirmation");
        data = Current();
        check(session.GetPlan(act, point)!.Event!.Actions is { Length: 2 } cheeseActions &&
            cheeseActions[1] is { Action: PredictorEventActionKind.Cards, CardIndices.Length: 2 },
            "Cheese confirmation saves the actual two-card child after its parent in " + language);
        await CheckSelected(data, Cards(Picker(data)), [0, 1], "Cheese saved child");
        gorge = data.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices)
            .Single(choice => choice.Label == text.Get("predictor.runtime.option.GORGE"));
        await Press(data, gorge, "Cheese same-parent reselection");
        data = Current();
        check(session.GetPlan(act, point)!.Event!.Actions.Length == 1,
            "Cheese same-parent reselection actually truncates its previously accepted child in " + language);
        await CheckSelected(data, Cards(Picker(data)), [], "Cheese parent reselection clears former saved selection");
        await CheckReopenedHeight(data, "cheese-reselected");
        using (var image = root.GetViewport().GetTexture().GetImage())
            image.SavePng(Path.Combine(output, "drawing-cheese-reselected-" + language + ".png"));
        details.CloseHover();

        // Meat Cleaver is the real native Cook provider. No authored rest
        // action or target is planted into the fixture's plan.
        session = fixture("ABYSSAL_BATHS", true);
        presenter = new(session, names, text);
        data = Current();
        var smithChoices = data.Premises!.Sections.Single(section => section.Title == text.Get("predictor.runtime.rest.Smith") &&
                section.Choices.Any(choice => choice.Object?.CardSnapshot != null))
            .Choices.Where(choice => choice.Object?.CardSnapshot != null).ToArray();
        await Press(data, smithChoices[0], "Default Smith actual target");
        data = Current();
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1) { presenter = new(session, names, text); data = Current(); }
            smithChoices = data.Premises!.Sections.Single(section => section.Title == text.Get("predictor.runtime.rest.Smith") &&
                    section.Choices.Any(choice => choice.Object?.CardSnapshot != null))
                .Choices.Where(choice => choice.Object?.CardSnapshot != null).ToArray();
            await CheckSelected(data, smithChoices, [0], "Smith saved target in " + (pass == 0 ? "same" : "new") + " presenter");
        }
        await Press(data, smithChoices[1], "Smith replacement target");
        data = Current();
        smithChoices = data.Premises!.Sections.Single(section => section.Title == text.Get("predictor.runtime.rest.Smith") &&
                section.Choices.Any(choice => choice.Object?.CardSnapshot != null))
            .Choices.Where(choice => choice.Object?.CardSnapshot != null).ToArray();
        await CheckSelected(data, smithChoices, [1], "Smith replacement clears former marker");
        check(session.GetPlan(act, point) is { RestActions.Length: 1, LeaveRest: true } smithPlan &&
            smithPlan.RestActions[0].SmithCard != null, "Smith feedback edits the same saved action and preserves leave-rest in " + language);
        details.CloseHover();

        session = fixture("ABYSSAL_BATHS", true);
        presenter = new(session, names, text);
        data = Current();
        var cook = data.Premises!.Sections.SelectMany(section => section.Choices)
            .Single(choice => choice.Label == text.Get("predictor.runtime.rest.Cook"));
        await Press(data, cook, "Meat Cleaver Cook action");
        data = Current();
        await Press(data, Cards(Picker(data))[0], "Cook first draft target");
        data = Current();
        await Press(data, Cards(Picker(data))[1], "Cook second draft target");
        data = Current();
        await Press(data, Picker(data).Choices.Single(choice => choice.Label == text.Get("predictor.runtime.confirm")), "Cook target confirmation");
        data = Current();
        check(session.GetPlan(act, point)!.RestActions is { Length: 1 } cookSteps &&
            cookSteps[0] is { Action: PredictorRestAction.Cook, CookCards.Length: 2 } &&
            cookSteps[0].CookCards.Select(card => card.InstanceId).Distinct().Count() == 2,
            "Cook actual callbacks save two distinct real card identities on their single action in " + language);
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1) { presenter = new(session, names, text); data = Current(); }
            await CheckSelected(data, Cards(Picker(data)), [0, 1], "Cook saved pair in " + (pass == 0 ? "same" : "new") + " presenter");
        }
        string savedCook = JsonSerializer.Serialize(session.GetPlan(act, point));
        await Press(data, Cards(Picker(data))[0], "Cook unselect former draft target");
        data = Current();
        await Press(data, Cards(Picker(data))[2], "Cook select replacement draft target");
        data = Current();
        await CheckSelected(data, Cards(Picker(data)), [1, 2], "Cook replacement draft");
        check(JsonSerializer.Serialize(session.GetPlan(act, point)) == savedCook,
            "Cook replacement draft keeps its old saved pair until confirmation in " + language);
        await Press(data, Picker(data).Choices.Single(choice => choice.Label == text.Get("predictor.runtime.confirm")), "Cook replacement confirmation");
        presenter = new(session, names, text); data = Current();
        await CheckSelected(data, Cards(Picker(data)), [1, 2], "Cook replacement saved pair in new presenter");
        string beforeFolds = JsonSerializer.Serialize(session.GetPlan(act, point));
        await CheckReopenedHeight(data, "cook-expanded");
        var expandedSize = details.HoverGlobalRegion.Size;
        await TogglePremises();
        await CaptureSize("cook-collapsed");
        var collapsedSize = details.HoverGlobalRegion.Size;
        check(collapsedSize.Y < expandedSize.Y - 1,
            "collapsing the actual Cook editor releases its removed premise height in " + language +
            "; expanded=" + expandedSize + "; collapsed=" + collapsedSize + "; geometry=" + JsonSerializer.Serialize(sizeReceipts));
        await TogglePremises();
        await CaptureSize("cook-expanded-again");
        check(details.HoverGlobalRegion.Size.Y <= expandedSize.Y + 1,
            "reopening actual Cook premises does not grow the settled main panel from old container minima in " + language);
        await TogglePremises();
        await CaptureSize("cook-collapsed-again");
        check(details.HoverGlobalRegion.Size.Y <= collapsedSize.Y + 1 &&
            JsonSerializer.Serialize(session.GetPlan(act, point)) == beforeFolds,
            "repeated Cook premise folds converge to the same compact height and preserve the saved pair in " + language);
        await TogglePremises();
        using (var image = root.GetViewport().GetTexture().GetImage())
            image.SavePng(Path.Combine(output, "drawing-rest-cook-selected-" + language + ".png"));
        details.CloseHover();
        check(signature() == nativeBefore, "Cheese and Smith/Cook actual UI callbacks leave the native RunState and RNG unchanged in " + language);
    }

    private static async Task CheckDrawingSourceAnchoring(Node root, string output, NMapScreen screen,
        InRunMapNodeDetails details, InRunMapNodeDetailData actual, string language,
        Func<string> signature, Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var previousSize = root.GetWindow().Size;
        string before = signature();
        var receipts = new List<object>();
        async Task Layout()
        {
            for (int frame = 0; frame < 8; frame++)
            {
                typeof(InRunMapNodeDetails).GetMethod("LayoutHover", members)!.Invoke(details, null);
                await DiagnosticFrame(root);
            }
        }
        try
        {
            root.GetWindow().Size = new(904, 916); await DiagnosticFrame(root);
            details.CloseHover();
            var anchor = new Rect2(new(150, 140), new(56, 56));
            details.ShowNode(actual with { IsCurrent = null }, anchor, screen.GetGlobalRect());
            await Layout();
            long generation = (long)DiagnosticField(details, "_hoverGeneration");
            var sourceOffset = details.HoverGlobalRegion.Position - anchor.Position;
            check(Math.Abs(sourceOffset.Y) < 1, "new source away from screen edges starts the main panel at its own Y in " + language);
            bool adjustedAtEdge = false;
            int index = 0;
            foreach (float sourceY in new[] { 140f, 850f, 500f, -180f, 1100f, 260f })
            {
                anchor = new(new(150, sourceY), new(56, 56));
                details.UpdateAnchor(anchor, screen.GetGlobalRect()); await Layout();
                var main = details.HoverGlobalRegion;
                var canvas = details.GetCanvasTransform().AffineInverse() * details.GetViewportRect();
                var offset = main.Position - anchor.Position;
                adjustedAtEdge |= Math.Abs(offset.Y) > 1;
                receipts.Add(new { sourceY, Anchor = anchor.ToString(), Main = main.ToString(), Offset = offset.ToString() });
                check((long)DiagnosticField(details, "_hoverGeneration") == generation && Math.Abs(offset.X - sourceOffset.X) < 1 &&
                    main.Position.Y >= canvas.Position.Y + 11 && main.End.Y <= canvas.End.Y - 11,
                    "actual Sunken Statue preserves its horizontal source side and stays vertically readable through edge/offscreen movement: " +
                    language + ";source=" + anchor + ";main=" + main + ";offset=" + offset + ";initial=" + sourceOffset);
                using var image = root.GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(output, "drawing-source-anchor-" + index++ + "-" + language + ".png"));
            }
            check(adjustedAtEdge, "screen-edge adjustment is exercised while retaining the original node and horizontal anchor in " + language);
            // Small drags across a screen boundary must clamp continuously,
            // without reopening the node or switching its horizontal side.
            Rect2? prior = null;
            foreach (float sourceY in new[] { 9f, 11f, 12f, 13f, 15f, 13f, 11f })
            {
                anchor = new(new(150, sourceY), new(56, 56));
                details.UpdateAnchor(anchor, screen.GetGlobalRect()); await Layout();
                var main = details.HoverGlobalRegion;
                check((long)DiagnosticField(details, "_hoverGeneration") == generation &&
                    Math.Abs(main.Position.X - anchor.Position.X - sourceOffset.X) < 1 &&
                    (prior == null || Math.Abs(main.Position.Y - prior.Value.Position.Y) <= 2.1f),
                    "small alternating edge drags retain the node and move the main continuously in " + language);
                prior = main;
            }
            var settled = details.HoverGlobalRegion;
            var choice = actual.Sections.SelectMany(section => section.Choices)
                .First(value => value.PreviewFactory != null || value.Preview != null);
            var button = Descendants(details).OfType<Button>().Single(value => value.HasMeta("choice_id") &&
                value.GetMeta("choice_id").AsString() == choice.Id);
            button.EmitSignal(Control.SignalName.MouseEntered); await Layout();
            check(details.HoverGlobalRegion.Position.IsEqualApprox(settled.Position) && signature() == before,
                "opening the actual source branch preview keeps its settled main anchor and leaves native/forecast/plan state unchanged in " + language);
            typeof(InRunMapNodeDetails).GetMethod("CloseOptionPreview", members)!.Invoke(details, null); await Layout();
            check(details.HoverGlobalRegion.Position.IsEqualApprox(settled.Position) && signature() == before,
                "closing the actual source branch preview also preserves its main anchor and native/forecast/plan state in " + language);
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "drawing-source-anchor-" + language + ".json"),
                JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }));
            details.CloseHover(); root.GetWindow().Size = previousSize; await DiagnosticFrame(root);
        }
    }

    private static async Task CheckDrawingComplexGeometry(Node root, string output, NMapScreen screen,
        InRunMapNodeDetails details, InRunMapNodeDetailData rewards, InRunMapNodeDetailData cardFree,
        string language, Func<string> signature, Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var previousSize = root.GetWindow().Size;
        string before = signature();
        var receipts = new List<object>();
        var reachabilityFailures = new List<object>();
        object lastReachability = new { };
        Control? Surface(string field) => DiagnosticField(details, field) as Control;
        async Task Layout()
        {
            await DiagnosticFrame(root);
            typeof(InRunMapNodeDetails).GetMethod("LayoutHover", members)!.Invoke(details, null);
            await DiagnosticFrame(root);
        }
        IEnumerable<ScrollContainer> ScrollParents(Control control)
        {
            for (Node? node = control.GetParent(); node != null && node != details; node = node.GetParent())
                if (node is ScrollContainer scroll) yield return scroll;
        }
        Rect2 VisiblePart(Control control)
        {
            Rect2 part = control.GetGlobalRect();
            foreach (var scroll in ScrollParents(control)) part = part.Intersection(scroll.GetGlobalRect());
            return part.Intersection(details.GetCanvasTransform().AffineInverse() * details.GetViewportRect());
        }
        async Task<bool> Reachable(Control control)
        {
            // Inner horizontal reward rows and the outer vertical outcome list
            // each own scrolling. Exercise both, rather than treating a button
            // in the tree as proof that the player can reach it.
            foreach (var scroll in ScrollParents(control))
            {
                scroll.EnsureControlVisible(control);
                await DiagnosticFrame(root);
            }
            Rect2 rect = control.GetGlobalRect(), part = VisiblePart(control);
            bool reachable = part.Size.X >= Math.Min(24, rect.Size.X) - 1 && part.Size.Y >= rect.Size.Y - 1;
            lastReachability = new
            {
                Window = root.GetWindow().Size.ToString(), Control = control.GetPath().ToString(),
                Main = details.HoverGlobalRegion.ToString(), Rect = rect.ToString(), VisiblePart = part.ToString(),
                RequiredVisibleWidth = Math.Min(24, rect.Size.X) - 1, RequiredVisibleHeight = rect.Size.Y - 1,
                Minimum = control.GetCombinedMinimumSize().ToString(),
                Scrolls = ScrollParents(control).Select(scroll => new
                {
                    Path = scroll.GetPath().ToString(), Rect = scroll.GetGlobalRect().ToString(),
                    Minimum = scroll.GetCombinedMinimumSize().ToString(), scroll.ScrollHorizontal, scroll.ScrollVertical,
                    Horizontal = new { scroll.GetHScrollBar().Value, scroll.GetHScrollBar().Page, scroll.GetHScrollBar().MinValue,
                        scroll.GetHScrollBar().MaxValue, scroll.GetHScrollBar().Visible },
                    Vertical = new { scroll.GetVScrollBar().Value, scroll.GetVScrollBar().Page, scroll.GetVScrollBar().MinValue,
                        scroll.GetVScrollBar().MaxValue, scroll.GetVScrollBar().Visible },
                    Children = scroll.GetChildren().OfType<Control>().Where(child => child is not ScrollBar)
                        .Select(child => new { Path = child.GetPath().ToString(), Rect = child.GetGlobalRect().ToString(),
                            Minimum = child.GetCombinedMinimumSize().ToString() }).ToArray()
                }).ToArray()
            };
            if (!reachable)
            {
                reachabilityFailures.Add(lastReachability);
                using var image = root.GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(output, $"drawing-complex-reachability-failure-{reachabilityFailures.Count}-{language}.png"));
            }
            return reachable;
        }
        string[] RewardGroups() => Descendants(details).OfType<Control>()
            .Where(control => control.IsVisibleInTree() && control.HasMeta("reward_group_has_stable_identity") &&
                control.GetMeta("reward_group_has_stable_identity").AsBool())
            .Select(control => control.GetMeta("reward_group").AsString()).Order().ToArray();
        var expectedGroups = rewards.Sections.Where(section => section.LargeCardResults && section.StableId != null)
            .Select(section => section.StableId!).Order().ToArray();
        var actualChoices = rewards.Sections.SelectMany(section => section.Choices).Where(choice => choice.Enabled).ToArray();
        // Only the position, canvas and prerequisite paragraph lengths are
        // synthetic. Reward opportunities and snapshots remain the actual Hunt
        // projection. This is not evidence of natural event occurrence rates.
        var longRewards = rewards with
        {
            IsCurrent = null,
            Information = Enumerable.Range(1, 12).Select(index =>
                $"Geometry fixture {index}: {rewards.PremisesLabel} · {rewards.Title} · {rewards.PremisesLabel} · {rewards.Title}").ToArray()
        };
        var quiet = cardFree with { IsCurrent = null, Information = [], Sections = [], Premises = null };
        var cases = new[]
        {
            (Name: "top-left", Size: new Vector2I(800, 720), Position: new Vector2(12, 12)),
            (Name: "top-right", Size: new Vector2I(800, 720), Position: new Vector2(732, 12)),
            (Name: "bottom-left", Size: new Vector2I(800, 720), Position: new Vector2(12, 652)),
            (Name: "bottom-right", Size: new Vector2I(800, 720), Position: new Vector2(732, 652)),
            (Name: "four-three", Size: new Vector2I(1024, 768), Position: new Vector2(600, 440)),
            (Name: "sixteen-nine", Size: new Vector2I(1280, 720), Position: new Vector2(800, 420))
        };
        try
        {
            foreach (var sample in cases)
            {
                root.GetWindow().Size = sample.Size;
                await DiagnosticFrame(root);
                Rect2 anchor = new(sample.Position, new(56, 56));
                details.CloseHover();
                details.ShowNode(longRewards, anchor, screen.GetGlobalRect());
                await Layout();
                var visible = details.GetCanvasTransform().AffineInverse() * details.GetViewportRect();
                var main = details.HoverGlobalRegion;
                var choiceReceipts = new List<object>();
                receipts.Add(new { sample.Name, Window = sample.Size.ToString(), Anchor = anchor.ToString(),
                    Main = main.ToString(), Canvas = visible.ToString(), Groups = RewardGroups(),
                    Dock = details.RewardCardDockGlobalRegion.ToString(),
                    DockDesired = details.RewardCardDockDesiredSize.ToString(), Choices = choiceReceipts });
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, $"drawing-complex-{sample.Name}-{language}.png"));
                check(main.Position.Y >= visible.Position.Y + 11 && main.End.Y <= visible.End.Y - 11 && RewardGroups().SequenceEqual(expectedGroups),
                    $"complex geometry fixture {sample.Name}/{language} keeps the main vertically readable and one host per real reward at an edge; main={main}; canvas={visible}");
                var offset = main.Position - anchor.Position;
                // Move the same generation to the top before checking each
                // scroll owner, retaining its horizontal source relationship.
                anchor = new(new(sample.Position.X, 12), new(56, 56));
                details.UpdateAnchor(anchor, screen.GetGlobalRect()); await Layout();
                main = details.HoverGlobalRegion;
                check(Math.Abs(main.Position.X - anchor.Position.X - offset.X) < 1,
                    $"complex geometry fixture {sample.Name}/{language} retains its horizontal source side for scroll reachability checks");
                var prerequisiteScroll = (ScrollContainer)DiagnosticField(details, "_prerequisiteScroll");
                var prerequisiteLabels = Descendants(prerequisiteScroll).OfType<Label>().ToArray();
                check(prerequisiteLabels.Length == 12 && await Reachable(prerequisiteLabels[^1]) &&
                    prerequisiteScroll.Size.Y <= 65,
                    $"complex geometry fixture {sample.Name}/{language} scrolls to its final long prerequisite in the bounded area");
                foreach (var choice in actualChoices)
                {
                    var buttons = Descendants(details).OfType<Button>().Where(button => button.IsVisibleInTree() &&
                        button.HasMeta("choice_id") && button.GetMeta("choice_id").AsString() == choice.Id).ToArray();
                    bool reachable = buttons.Length == 1 && !buttons[0].Disabled && await Reachable(buttons[0]);
                    choiceReceipts.Add(new { choice.Id, Count = buttons.Length, Reachable = reachable,
                        Rect = buttons.Length == 1 ? buttons[0].GetGlobalRect().ToString() : null });
                    check(reachable, $"complex geometry fixture {sample.Name}/{language} makes actual reward action {choice.Id} singular and reachable through its scroll owners");
                }
                check(details.HoverGlobalRegion.Position.IsEqualApprox(main.Position) &&
                    details.HoverGlobalRegion.Size.IsEqualApprox(main.Size),
                    $"complex geometry fixture {sample.Name}/{language} scrolling prerequisites and rewards keeps the main panel stable");
                details.ShowNode(quiet, anchor, screen.GetGlobalRect());
                await Layout();
                check(Math.Abs(details.HoverGlobalRegion.Position.Y - anchor.Position.Y) < 1 && RewardGroups().Length == 0 &&
                    new[] { "_rewardCardDock", "_previewViewport", "_previewCardsViewport", "_combatPanel" }
                        .All(field => Surface(field)?.IsVisibleInTree() != true),
                    $"complex geometry fixture {sample.Name}/{language} switches to a card-free node without retaining any prior result surface");
            }

            // Keep the same data/control generation open across the whole
            // resize cycle. Reopening a node would mask stale width/anchor state.
            root.GetWindow().Size = new(1680, 1080);
            await DiagnosticFrame(root);
            var movingAnchor = new Rect2(new(700, 480), new(56, 56));
            details.CloseHover(); details.ShowNode(longRewards, movingAnchor, screen.GetGlobalRect());
            await Layout();
            long generation = (long)DiagnosticField(details, "_hoverGeneration");
            var resizeOffset = details.HoverGlobalRegion.Position - movingAnchor.Position;
            foreach (var size in new[] { new Vector2I(1024, 640), new Vector2I(800, 720), new Vector2I(1680, 1080) })
            {
                root.GetWindow().Size = size;
                await DiagnosticFrame(root);
                movingAnchor = new(new(size.X * .61f, 12), new(56, 56));
                details.UpdateAnchor(movingAnchor, screen.GetGlobalRect());
                await Layout();
                var visible = details.GetCanvasTransform().AffineInverse() * details.GetViewportRect();
                check(details.HoverOpen && details.HoverNodeId == rewards.NodeId &&
                    (long)DiagnosticField(details, "_hoverGeneration") == generation &&
                    Math.Abs(details.HoverGlobalRegion.Position.X - movingAnchor.Position.X - resizeOffset.X) < 1 &&
                    details.HoverGlobalRegion.Position.Y >= visible.Position.Y + 11 && details.HoverGlobalRegion.End.Y <= visible.End.Y - 11 &&
                    RewardGroups().SequenceEqual(expectedGroups),
                    $"open-node resize {size}/{language} preserves the live generation, horizontal source side, vertical bounds and real reward ownership");
                var expansion = (Button)details.FindChild("PremisesExpansion", true, false)!;
                bool expansionReachable = await Reachable(expansion);
                receipts.Add(new { Name = "open-node-resize", Window = size.ToString(), Anchor = movingAnchor.ToString(),
                    ExpansionReachable = expansionReachable, Geometry = lastReachability });
                check(expansionReachable, $"open-node resize {size}/{language} retains a reachable premise expansion action; geometry=" +
                    JsonSerializer.Serialize(lastReachability));
            }
            details.CloseHover(); await Layout();
            check(!details.HoverOpen && !details.ContainsPointer(movingAnchor.GetCenter()) &&
                !details.BlocksMapInput(movingAnchor.GetCenter()) &&
                new[] { "_rewardCardDock", "_previewViewport", "_previewCardsViewport", "_combatPanel" }
                    .All(field => Surface(field)?.IsVisibleInTree() != true) && signature() == before,
                "complex geometry close releases all result surfaces and native map input without changing real state, forecast or authored plans in " + language);
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "drawing-complex-geometry-" + language + ".json"),
                JsonSerializer.Serialize(new { fixture = "controlled geometry; actual Hunt reward projection; no natural occurrence-rate claim",
                    receipts, reachabilityFailures }, new JsonSerializerOptions { WriteIndented = true }));
            details.CloseHover(); root.GetWindow().Size = previousSize;
            await DiagnosticFrame(root);
        }
    }
}
