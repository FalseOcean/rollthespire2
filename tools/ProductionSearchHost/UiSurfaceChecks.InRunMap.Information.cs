using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;
using NativePointType = MegaCrit.Sts2.Core.Map.MapPointType;

internal static partial class UiSurfaceChecks
{
    private static async Task RunInRunMapInformationFlow(Node root, string output, NMapScreen screen,
        InRunMapPredictionPanel panel, RunState run, IReadOnlyDictionary<MapPosition, NMapPoint> nativePoints,
        Func<string> signature, Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(object value, string name) => value.GetType().GetField(name, members)!.GetValue(value)!;
        void Call(object value, string name) => value.GetType().GetMethod(name, members)!.Invoke(value, null);
        async Task Frame(int count = 5)
        {
            for (int i = 0; i < count; i++)
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        async Task Picture(string name)
        {
            await Frame();
            using var image = root.GetViewport().GetTexture().GetImage();
            image.SavePng(Path.Combine(output, name));
        }
        Button ChoiceButton(InRunMapNodeDetails view, string id) => Descendants(view).OfType<Button>()
            .Single(button => button.IsVisibleInTree() && button.HasMeta("choice_id") && button.GetMeta("choice_id").AsString() == id);
        string before = signature();
        var graph = run.Map;
        var coordinate = run.CurrentMapCoord;
        var draft = panel.DisplayedDraft!;
        // The drawing fixture begins at a completed departure. Explicitly
        // withdraw that native flag for this boundary-transition check.
        screen.SetTravelEnabled(false);
        draft.Clear(); Call(panel, "UpdateStatus"); await Frame();
        var session = (InRunMapPredictionSession)Field(panel, "_prediction");
        check(session.WaitingForBoundary && session.Snapshot != null,
            "withdrawing departure retains the previously established detached boundary");
        // A new view has no earlier valid boundary to retain. Exercise that
        // separate missing-observation case through the production lifecycle.
        Call(panel, "ResetInformation"); Call(panel, "UpdateStatus"); await Frame();
        session = (InRunMapPredictionSession)Field(panel, "_prediction");
        check(!screen.IsTravelEnabled && session.WaitingForBoundary && session.Snapshot == null,
            "information waits for native departure flag rather than inventing a finished current room");

        // Exercise the actual source hook and setter, rather than setting its
        // private backing property or giving the session an invented true flag.
        screen.SetTravelEnabled(true);
        check(screen.IsTravelEnabled && !screen.IsTraveling, "native SetTravelEnabled establishes observed departure boundary");
        Call(panel, "QueueInformationRefresh"); await Frame();
        check(session.Snapshot is { CanLeaveCurrentNode: true } && !session.WaitingForBoundary && session.Nodes.IsEmpty,
            "native departure captures present state without an undrawn suffix");
        var first = run.Map.StartingMapPoint.Children.FirstOrDefault(point => point.PointType == NativePointType.Monster && point.Children.Count > 0);
        check(first != null, "native fixture has a first combat with a connected successor for take/Skip flow");
        // Standard maps guarantee the first battle, not another battle in its
        // next row. Preserve this seed's native graph and use its actual next
        // room as the suffix whose deck dependency is redrawn.
        var second = first!.Children.OrderBy(point => point.PointType == NativePointType.Monster ? 0 :
            point.PointType == NativePointType.Shop ? 1 : 2).First();
        var a = InRunMapSnapshot.Position(first.coord); var b = InRunMapSnapshot.Position(second.coord);
        check(draft.ApplyStroke([a, b], PredictorMapStrokeMode.Draw) == PredictorMapDraftEditResult.Changed,
            "production route draft accepts connected short selection");
        Call(panel, "UpdateStatus"); await Frame();
        check(session.Nodes.Count == 2 && session.Nodes.ContainsKey((0, a)) && session.Nodes.ContainsKey((0, b)),
            "production session projects exactly selected connected room prefix");
        var annotations = (Dictionary<MapPosition, Control>)Field(panel, "_annotations");
        check(annotations.ContainsKey(a) &&
            annotations.Values.All(node => node.MouseFilter == Control.MouseFilterEnum.Ignore),
            "production resident encounter observations are present and do not acquire native travel input");
        var presenter = (InRunMapNodePresenter)Field(panel, "_presenter");
        var details = (InRunMapNodeDetails)Field(panel, "_details");
        var text = (IUiTextProvider)Field(panel, "_text");
        var names = (IGameContentNameResolver)Field(panel, "_names");
        CheckInRunMapFailureBoundaries(panel, session, names, text, signature, check);
        var observed = session.Nodes[(0, a)];
        var eventPreview = CheckInRunMapEventPreviewText(panel, observed, a, check);
        var stagedEvent = CheckInRunMapEventStages(observed, a, names, text, check);
        var data = presenter.Detail((0, a))!;
        check(!data.Subtitle.Contains(text.Format("predictor.runtime.position", observed.Position.Act, observed.Position.Floor), StringComparison.Ordinal),
            "ordinary hover omits the redundant act/floor subtitle");
        check(data.Sections.Count(section => section.GainReward) == observed.Rewards.Count(reward =>
            reward.Kind is PredictorRewardKind.Potion or PredictorRewardKind.Relic),
            "compact gain entries retain each actual potion/relic reward opportunity");
        await CheckInRunMapGainsLayout(root, output, details, check);
        var actualEvents = CheckInRunMapActualEventGroups(run, draft, a, names, text, signature, check);
        var actualRooms = CheckInRunMapActualRooms(run, draft, a, names, text, signature, check);
        await CheckInRunMapLayeredLayout(root, output, details, nativePoints[a], data, eventPreview, actualEvents.Colorful,
            stagedEvent, actualEvents.Fight, check);
        // Frozen display receipts from the actual controlled-input projections
        // above. No authored callbacks are attached to this photographic view.
        var actualEventView = new InRunMapNodeDetails((RolltheSpire2.Ui.Icons.IGameIconResolver)Field(details, "_icons"), names);
        root.AddChild(actualEventView);
        // Root Window uses the physical OS pointer, unlike the SubViewport
        // input fixture above. Freeze only these read-only photographs.
        actualEventView.SetProcess(false);
        foreach (var sample in new[] { (Name: "colorful", Data: actualEvents.Colorful), (Name: "event-fight", Data: actualEvents.Fight),
            (Name: "rest", Data: actualRooms.Campfire), (Name: "rest-unresolved", Data: actualRooms.UnresolvedSmith),
            (Name: "shop", Data: actualRooms.Shop) })
        {
            actualEventView.ShowNode(sample.Data with { IsCurrent = null,
                Subtitle = sample.Data.Subtitle + " · Controlled actual route projection", EditorActions = [], DiagnosticsLabel = null },
                new Rect2(new(120, 230), new(56, 56)), screen.GetGlobalRect());
            await Frame();
            actualEventView.UpdateAnchor(new Rect2(new(120, 230), new(56, 56)), screen.GetGlobalRect());
            await Frame();
            if (sample.Name == "rest-unresolved")
            {
                ((Button)actualEventView.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                for (int frame = 0; frame < 8; frame++)
                {
                    typeof(InRunMapNodeDetails).GetMethod("LayoutHover", members)!.Invoke(actualEventView, null);
                    await Frame(1);
                }
                check(!actualEventView.HasRewardCardDock && Descendants(actualEventView).OfType<Button>()
                        .Count(button => button.IsVisibleInTree() && button.Text.StartsWith(
                            text.Get("predictor.runtime.plan.remove_action"), StringComparison.Ordinal)) == 1,
                    "actual ordinary unresolved Smith photograph retains one removal entrance and no invented large-card outcome");
            }
            using var screenshot = root.GetViewport().GetTexture().GetImage();
            if (sample.Name is "rest" or "shop")
                check(!actualEventView.HasRewardCardDock, "authored room action history stays compact instead of becoming invented reward card groups");
            screenshot.SavePng(Path.Combine(output, "in-run-map-actual-" + sample.Name + ".png"));
            if (sample.Name == "colorful")
            {
                check(actualEventView.FindChild("MapRewardCardDock", true, false) is Control viewport && viewport.IsVisibleInTree(),
                    "read-only actual Colorful photograph includes its actual reward card stacks after native layout settles");
                using var groupScreenshot = root.GetViewport().GetTexture().GetImage();
                groupScreenshot.SavePng(Path.Combine(output, "in-run-map-actual-colorful-group-display.png"));
            }
        }
        actualEventView.CloseHover(); actualEventView.QueueFree(); await Frame(1);
        await RunInRunMapPlayerInputsSmoke(root, output, screen, panel, observed, a, check);
        var diagnosticReceipt = await RunInRunMapDiagnosticsSmoke(root, output, screen, panel,
            session, draft, a, b, signature, check);
        var card = observed.Rewards.First(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card &&
            reward.DefaultSkipped && reward.Value!.CanSkip);
        check(card.Value!.Cards.Length >= 1 && card.AppliedChoice == null &&
            card.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault && session.GetPlan(0, a) == null &&
            !session.Nodes[(0, b)].DeckPendingCauses.Any(cause => cause.Code == "PendingCardChoice" && cause.NodeId == observed.Position.NodeId),
            "ordinary card offers keep their generated options while approved default Skip resolves the suffix without an authored plan");
        data = presenter.Detail((0, a))!;
        var cardSection = data.Sections.Single(section => section.Objects.Count == card.Value.Cards.Length &&
            section.Objects.All(item => item.Kind == GameContentKind.Card) &&
            section.Objects.Select(item => item.Key).SequenceEqual(card.Value.Cards.Select(option => option.Key)));
        check(cardSection.Choices.Count(choice => choice.Object?.Kind == GameContentKind.Card) == card.Value.Cards.Length &&
            !cardSection.Choices.Any(choice => choice.Label == text.Get("predictor.runtime.plan.skip")) &&
            cardSection.Text.Contains(text.Get("predictor.runtime.default_skip"), StringComparison.Ordinal) &&
            cardSection.Choices.All(choice => !choice.Selected),
            "full hover labels default Skip without styling it as player input and retains every generated card without a redundant Skip control");
        var map = screen.GetNode<Control>("TheMap");
        Vector2 delta = new Vector2(540, 380) - nativePoints[a].GetGlobalRect().GetCenter();
        var target = map.Position + delta;
        target.Y = Math.Clamp(target.Y, -600, 1800);
        map.Position = target;
        typeof(NMapScreen).GetField("_targetDragPos", members)!.SetValue(screen, map.Position);
        await Frame();
        // Godot 4.5 root Window get_mouse_position reads DisplayServer, rather
        // than the mouse event passed to push_input. This offscreen host reports
        // (0,0). Suspend only this view's automatic hover-close process for the
        // rendering/geometry receipt and real deferred hover button callbacks.
        // This does not verify live pointer entry/exit; restore normal process
        // immediately after opening the persistent editor.
        bool viewProcess = details.IsProcessing();
        long takeRevision = session.Revision;
        details.SetProcess(false);
        try
        {
            // The main branch outcome now persists after a click. Further reward
            // operations retain the separate read-only native overlay.
            details.ShowNode(data, nativePoints[a].GetGlobalRect(), screen.GetGlobalRect(), nativePoints[a]); await Frame();
            var hoveredOption = data.Sections.SelectMany(section => section.Choices).First(choice =>
                choice.Object?.Kind == GameContentKind.Card && choice.PreviewFactory != null);
            long beforeOptionHover = session.Revision;
            ChoiceButton(details, hoveredOption.Id).EmitSignal(Control.SignalName.MouseEntered); await Frame();
            var optionOverlay = (RolltheSpire2.Ui.InRunOperations.NeowOperationOverlay)details.FindChild("MapEventOptionCards", true, false)!;
            var previewViewport = (Control)details.FindChild("NodeOptionCardsViewport", true, false)!;
            check(optionOverlay.Visible && optionOverlay.VisibleRegions.Count > 0 &&
                details.ContainsPointer(previewViewport.GetGlobalRect().GetCenter()) && session.Revision == beforeOptionHover,
                "reward-operation hover reuses detached native display without authoring a route plan");
            details.ShowNode(data with { Subtitle = data.Subtitle + " · refresh" },
                nativePoints[a].GetGlobalRect(), screen.GetGlobalRect(), nativePoints[a]); await Frame();
            check(!optionOverlay.Visible && details.HoverNodeId == data.NodeId && session.Revision == beforeOptionHover,
                "same-node replacement keeps the panel open and retires old operation anchors");
            details.CloseHover();
            details.ShowNode(eventPreview, nativePoints[a].GetGlobalRect(), screen.GetGlobalRect(), nativePoints[a]); await Frame();
            check(!Descendants(details).OfType<Label>().Any(label => label.Text.Contains("Missing translation", StringComparison.Ordinal)),
                "selected event result fixture contains no untranslated placeholder");
            await Picture("in-run-map-event-inline-results.png");
            details.ShowNode(data, nativePoints[a].GetGlobalRect(), screen.GetGlobalRect(), nativePoints[a]);
            await Frame(); details.UpdateAnchor(nativePoints[a].GetGlobalRect(), screen.GetGlobalRect()); await Frame();
            check(details.HoverOpen && details.HoverNodeId == observed.Position.NodeId,
                "production full reward hover renders at original native node with host automatic hover process suspended");
            var hoverRegion = details.FindChild("ContinuousHoverRegion", true, false)!;
            var rewardSurface = (Control)details.FindChild("CompactNodeHover", true, false)!;
            check(hoverRegion.GetChildren().OfType<PanelContainer>().Count() == 1 &&
                details.FindChild("CompleteRewards", true, false) == null && details.FindChild("Information", true, false) == null,
                "node hover paints one compact surface for identity, rewards and secondary premises");
            foreach (var option in card.Value.Cards)
                check(Descendants(rewardSurface).OfType<Label>().Any(label => label.Text.StartsWith(
                        names.Resolve(option.Key, GameContentKind.Card), StringComparison.Ordinal)),
                    "small-icon hover renders generated card name " + option.Key.Serialized);
            var rewardScroll = (ScrollContainer)details.FindChild("NodeDetailScroll", true, false)!;
            var rewardButtons = cardSection.Choices.Select(choice => ChoiceButton(details, choice.Id)).ToArray();
            check(cardSection.RewardActionsRow && rewardButtons.Length >= card.Value.Cards.Length &&
                rewardButtons.Select(button => button.GetParent()).Distinct().Single() is HBoxContainer &&
                !cardSection.Choices.Any(choice => choice.Label == text.Get("predictor.runtime.plan.skip")),
                "card reward keeps every small card icon/name and applicable alternative in one horizontal row without a redundant Skip");
            var cardButtons = cardSection.Choices.Where(choice => choice.Object?.Kind == GameContentKind.Card)
                .Select(choice => ChoiceButton(details, choice.Id)).ToArray();
            check(cardButtons.Select(button => Math.Round(button.GetGlobalRect().Position.Y)).Distinct().Count() == 1 &&
                data.EditorActions.Count == 0 && data.DiagnosticsLabel == null &&
                details.FindChild("NodeSecondaryExpansion", true, false) == null,
                "three reward cards share one row; ordinary combat exposes premises without the retired simulator or diagnostics footer");
            check(data.Sections.Where(section => section.Objects.Any(item => item.Kind == GameContentKind.Potion))
                .All(section => section.Choices.Count == 0), "potion drops have no route inventory management actions");
            check(card.Value.Cards.Length == 3 && cardSection.Choices.All(choice =>
                    rewardScroll.GetGlobalRect().Encloses(ChoiceButton(details, choice.Id).GetGlobalRect())) &&
                rewardScroll.GetVScrollBar().Value == 0,
                "ordinary three-card offer fits on the first hover screen without scrolling");
            var nightFrame = (Control)details.FindChild("NodeNightFrame", true, false)!;
            check(!nightFrame.GetGlobalRect().Encloses(nativePoints[a].GetGlobalRect()) &&
                nightFrame.GetGlobalRect().Encloses(rewardSurface.GetGlobalRect()) &&
                screen.GetGlobalRect().Encloses(rewardSurface.GetGlobalRect()),
                "independent textured panel bounds its content without enclosing the unmoved native source");
            await Picture("in-run-map-information-hover.png");
            var visuals = (System.Collections.IDictionary)Field(panel, "_visuals");
            var productNodeRegion = (Rect2)typeof(InRunMapPredictionPanel).GetMethod("GlobalRegion", members)!
                .Invoke(panel, [visuals[a]])!;
            string hoverObservation = $"hoverOpen={details.HoverOpen}; hoverNode={details.HoverNodeId}; expectedNode={observed.Position.NodeId}; " +
                $"detailsPointer={details.GetGlobalMousePosition()}; panelPointer={panel.GetGlobalMousePosition()}; " +
                $"viewportPointer={root.GetViewport().GetMousePosition()}; hoverLifecycle=NotValidatedOffscreenHost; viewProcessSuspended={!details.IsProcessing()}; " +
                $"hoverRegion={details.HoverGlobalRegion}; actualNodeRegion={nativePoints[a].GetGlobalRect()}; productNodeRegion={productNodeRegion}; " +
                $"nodeAnchor={Field(details, "_nodeGlobal")}; viewportAnchor={Field(details, "_viewportGlobal")}; " +
                $"nativeDrawingMode={screen.Drawings.GetLocalDrawingMode()}; nativeDrawingActive={screen.Drawings.IsLocalDrawing()}; drawingSuppressed={Field(details, "_drawingSuppressed")}; " +
                $"activeScreenCurrent={ActiveScreenContext.Instance.IsCurrent(screen)}; activeScreen={ActiveScreenContext.Instance.GetCurrentScreen()?.GetType().FullName}; " +
                $"screenOpen={screen.IsOpen}; panelVisible={panel.IsVisibleInTree()}; detailsVisible={details.IsVisibleInTree()}; " +
                $"predictionFault={Field(panel, "_predictionFault")}; mapPosition={map.Position}; targetDragPosition={Field(screen, "_targetDragPos")}";
            File.WriteAllText(Path.Combine(output, "in-run-map-hover-observation.txt"), hoverObservation);
            check(details.HoverOpen && details.HoverNodeId == observed.Position.NodeId &&
                details.ContainsPointer(nativePoints[a].GetGlobalRect().GetCenter()) &&
                details.ContainsPointer(rewardSurface.GetGlobalRect().GetCenter()) &&
                details.ContainsPointer((nativePoints[a].GetGlobalRect().GetCenter() + rewardSurface.GetGlobalRect().GetCenter()) * .5f) &&
                details.BlocksMapInput(rewardSurface.GetGlobalRect().GetCenter()) &&
                screen.GetGlobalRect().Encloses(rewardSurface.GetGlobalRect()),
                "continuous hover region covers native node and single reward surface and blocks native input; mouse lifecycle unverified; " + hoverObservation);

            // The first native encounter need not have configurable combat
            // sources. A view-only field probe verifies the same-surface callback
            // contract without inventing a prerequisite in the detached session.
            var probe = data with { Premises = (data.Premises ?? new InRunMapNodeEditorData(data.NodeId,
                "Fixture", [], [], [], [], text.Get("predictor.map.close_editor"))) with
            {
                Numbers = [new("fixture-premise-number", "Fixture count", 1, 0, 5)],
                Toggles = [new("fixture-premise-toggle", "Fixture enabled", false)], Selections = []
            } };
            var fieldReceipts = new List<(string Node, string Field, InRunMapDetailFieldValue Value)>();
            void ObserveField(string node, string field, InRunMapDetailFieldValue value) => fieldReceipts.Add((node, field, value));
            details.FieldChanged += ObserveField;
            try
            {
                details.ShowNode(probe, nativePoints[a].GetGlobalRect(), screen.GetGlobalRect(), nativePoints[a]); await Frame();
                check(!Descendants(rewardSurface).OfType<SpinBox>().Any() && !details.EditorOpen,
                    "combat fields start collapsed inside the transient node surface");
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Frame();
                var number = Descendants(rewardSurface).OfType<SpinBox>().Single();
                var premiseToggle = Descendants(rewardSurface).OfType<CheckButton>().Single();
                number.Value = 2; premiseToggle.ButtonPressed = true; await Frame();
                check(fieldReceipts.Any(value => value.Node == observed.Position.NodeId && value.Field == "fixture-premise-number" && value.Value.Number == 2) &&
                    fieldReceipts.Any(value => value.Node == observed.Position.NodeId && value.Field == "fixture-premise-toggle" && value.Value.Toggle == true) &&
                    details.HoverOpen && !details.EditorOpen && session.Revision == takeRevision,
                    "expanded secondary premises emit guarded number/toggle callbacks without opening a modal or authoring invented session facts");
                int acceptedFields = fieldReceipts.Count;
                number.Value = 3;
                details.ShowNode(data, nativePoints[a].GetGlobalRect(), screen.GetGlobalRect(), nativePoints[a]); await Frame();
                check(fieldReceipts.Count == acceptedFields, "hover rebuild rejects deferred callbacks from the replaced field generation");
                check((bool)Field(details, "_premisesExpanded"), "same-node refresh preserves secondary expansion");
                if (data.Premises != null)
                    ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Frame();
            }
            finally { details.FieldChanged -= ObserveField; }

            var directTake = cardSection.Choices.First(choice => choice.Enabled && choice.Object?.Kind == GameContentKind.Card &&
                choice.Object.Key == card.Value.Cards[0].Key);
            ChoiceButton(details, directTake.Id).EmitSignal(Button.SignalName.Pressed); await Frame();
            check(!details.EditorOpen && details.HoverOpen && details.HoverNodeId == observed.Position.NodeId, "card Take is available directly and same-node recompute keeps the panel open");

            data = presenter.Detail((0, a))!;
            check(data.DiagnosticsLabel == null && data.EditorActions.Count == 0,
                "taking a card does not restore the retired ordinary diagnostics entrance");
            // H07 keeps the complete editor/diagnostic APIs for their separate
            // checks, while ordinary prediction no longer exposes this modal.
            details.ShowEditor(presenter.Editor((0, a))!, screen.GetGlobalRect());
            await Frame();
        }
        finally { details.SetProcess(viewProcess); }
        check(details.EditorOpen && details.EditorNodeId == observed.Position.NodeId && !details.HoverOpen,
            "retained explicit editor API opens its persistent surface and closes transient hover");
        var editor = presenter.Editor((0, a))!;
        var plan = session.GetPlan(0, a)!;
        var receipt = plan.RewardChoices.Single(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card);
        check(session.Revision > takeRevision && receipt.CardIndex == 0 && !receipt.Skip &&
            session.Nodes[(0, a)].Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card &&
                reward.Value!.Status == PredictorRewardStatus.Taken),
            "actual hover button authors semantic card take and recomputes detached rewards");
        check(!session.Nodes[(0, b)].DeckPendingCauses.Any(cause => cause.Code == "PendingCardChoice" &&
                cause.NodeId == observed.Position.NodeId) && details.EditorOpen && details.EditorNodeId == observed.Position.NodeId,
            "card take preserves a resolved suffix and the retained editor API renders independently");
        check(signature() == before && ReferenceEquals(graph, run.Map) && run.CurrentMapCoord == coordinate,
            "production take callback leaves native inventory/map/history/RNG and travel coordinate unchanged");
        CheckInRunMapDiagnosticsRecomputed(session, presenter, b, diagnosticReceipt, check);
        await Picture("in-run-map-information-card-taken.png");

        editor = presenter.Editor((0, a))!;
        var clear = editor.Sections.SelectMany(section => section.Choices).Single(choice =>
            choice.Label == text.Get("predictor.runtime.plan.clear"));
        check(presenter.ApplyChoice(observed.Position.NodeId, clear.Id), "retained complete editor API can explicitly clear the authored plan");
        Call(panel, "RenderInformation");
        await Frame();
        check(session.GetPlan(0, a) == null && details.EditorOpen,
            "explicit clear removes the authored take and keeps persistent editor open");
        check(session.Nodes[(0, a)].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).DefaultSkipped &&
            presenter.Detail((0, a))!.Sections.Any(section => section.Text.Contains(text.Get("predictor.runtime.default_skip"), StringComparison.Ordinal)),
            "clearing an authored pickup restores the visible default Skip without asking for a new click");
        ((Button)details.FindChild("EditorClose", true, false)!).EmitSignal(Button.SignalName.Pressed); await Frame();
        long cancelRevision = session.Revision;
        details.SetProcess(false);
        try
        {
            data = presenter.Detail((0, a))!;
            details.ShowNode(data, nativePoints[a].GetGlobalRect(), screen.GetGlobalRect(), nativePoints[a]); await Frame();
            details.UpdateAnchor(nativePoints[a].GetGlobalRect(), screen.GetGlobalRect()); await Frame();
            var defaultSection = data.Sections.Single(section => section.Objects.Select(item => item.Key)
                .SequenceEqual(card.Value.Cards.Select(option => option.Key)));
            var retake = defaultSection.Choices.First(choice => choice.Object?.Kind == GameContentKind.Card && choice.Enabled);
            ChoiceButton(details, retake.Id).EmitSignal(Button.SignalName.Pressed); await Frame();
            data = presenter.Detail((0, a))!;
            var cancel = data.Sections.SelectMany(section => section.Choices).Single(choice => choice.Selected &&
                choice.Object?.Key == retake.Object!.Key && choice.Label == text.Get("predictor.runtime.cancel"));
            check(cancel.PreviewFactory?.Invoke() is { OptionKey: "CANCEL_TAKE" },
                "the actual selected card exposes a pure cancellation preview before its second click");
            ChoiceButton(details, cancel.Id).EmitSignal(Button.SignalName.Pressed); await Frame();
            check(!details.EditorOpen, "selected-card second click cancels Take directly without an editor modal");
            data = presenter.Detail((0, a))!;
            check(data.DiagnosticsLabel == null && data.EditorActions.Count == 0 &&
                !Descendants(details).OfType<Button>().Any(button => button.IsVisibleInTree() && button.HasMeta("choice_id") &&
                    button.GetMeta("choice_id").AsString() == "diagnostics"),
                "cancelling Take preserves the premises-only ordinary surface");
            details.ShowEditor(presenter.Editor((0, a))!, screen.GetGlobalRect()); await Frame();
        }
        finally { details.SetProcess(viewProcess); }
        var cancelledPlan = session.GetPlan(0, a);
        string cancelObservation = JsonSerializer.Serialize(new
        {
            BeforeRevision = cancelRevision, AfterRevision = session.Revision, Plan = cancelledPlan,
            Node = session.Nodes[(0, a)], Suffix = session.Nodes[(0, b)],
            details.EditorOpen, details.EditorNodeId, session.WaitingForBoundary,
            PredictionFault = Field(panel, "_predictionFault")
        });
        File.WriteAllText(Path.Combine(output, "in-run-map-cancel-observation.json"), cancelObservation);
        check(session.Revision > cancelRevision && cancelledPlan?.RewardChoices.Any(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card) != true &&
            session.Nodes[(0, a)].Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card &&
                reward.Value!.Status == PredictorRewardStatus.Skipped && reward.DefaultSkipped && reward.AppliedChoice == null &&
                reward.DispositionOrigin == PredictorKnowledgeOrigin.ApprovedDefault),
            "the actual selected-card second click removes only its authored Take and restores approved default Skip; " + cancelObservation);
        check(!session.Nodes[(0, b)].DeckPendingCauses.Any(cause => cause.Code == "PendingCardChoice" &&
                cause.NodeId == observed.Position.NodeId) && details.EditorOpen && details.EditorNodeId == observed.Position.NodeId,
            "cancelling Take leaves its suffix independent and the retained editor API renderable afterward");
        var retainedTake = presenter.Editor((0, a))!.Sections.Where(section => section.RewardActionsRow)
            .SelectMany(section => section.Choices).First(choice => choice.Object?.Kind == GameContentKind.Card && choice.Enabled);
        ChoiceButton(details, retainedTake.Id).EmitSignal(Button.SignalName.Pressed); await Frame();
        check(session.GetPlan(0, a)!.RewardChoices.Any(choice => choice.CardIndex == 0 && !choice.Skip),
            "the retained real Take callback remains usable after cancellation and supplies authored intent for native clear");
        check(signature() == before && ReferenceEquals(graph, run.Map) && run.CurrentMapCoord == coordinate && !screen.IsTraveling,
            "Take/cancel callbacks and reward hover never trigger native travel or mutate real RunState/RNG");
        await RunInRunMapDiagnosticsOccurrenceSmoke(root, screen, panel, run, draft, a, b, signature, check);
        CheckInRunMapReadOnlyRewardPreviews(panel, run, draft, a, signature, check);
        await RunInRunMapRewardAlternativesSmoke(root, output, screen, panel, run, draft, a, signature, check);
        await Picture("in-run-map-information-card-skipped.png");
        var closeButton = details.FindChild("EditorClose", true, false) as Button;
        check(closeButton != null && closeButton.IsVisibleInTree() && closeButton.Size.X >= 90 && closeButton.Size.Y >= 40 &&
            screen.GetGlobalRect().Encloses(closeButton.GetGlobalRect()), "persistent editor close control remains visible and inside the screen");
        closeButton!.EmitSignal(Button.SignalName.Pressed);
        await Frame();
        check(!details.EditorOpen, "visible close button exits persistent editor and releases its input shield");
        var drafts = (Dictionary<int, PredictorMapRouteDraft>)Field(panel, "_drafts");
        check(!session.Plans.IsEmpty && drafts.Values.Any(value => !value.Selected.IsEmpty),
            "global clear fixture has authored node choices and route intent to cancel");
        await RunInRunMapLocalizationSmoke(root, screen, panel, a, nativePoints[a], signature, check);
        await CheckMapFeatureLifecycle(root, panel, check);
        screen.Drawings.ClearDrawnLinesLocal(); await Frame();
        check(session.Plans.IsEmpty && drafts.Values.All(value => value.Selected.IsEmpty && !value.CanUndo && !value.Undo()),
            "actual native local clear cancels every draft and authored node plan without Undo restoration");
        draft.ApplyNativeStroke([a, b], PredictorMapStrokeMode.Draw);
        Call(panel, "UpdateStatus"); await Frame();
        check(session.Plans.IsEmpty && session.GetPlan(0, a) == null,
            "explicit route redraw after native clear cannot restore old node plans from the prior redraw snapshot");
    }
}
