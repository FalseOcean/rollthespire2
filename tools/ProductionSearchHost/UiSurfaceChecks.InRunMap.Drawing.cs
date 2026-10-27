using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;

internal static partial class UiSurfaceChecks
{
    private static async Task RunDrawingForecastUi(Node root, string output, NMapScreen screen,
        InRunMapPredictionPanel panel, RunState native, string version, MapPosition first, MapPosition suffix,
        Func<string> signature, Action<bool, string> check)
    {
        bool doorsOnly = OS.GetCmdlineUserArgs().Contains("--doors-branch-ui") ||
            OS.GetCmdlineUserArgs().Contains("--event-premises-ui");
        // The stock-host room fixture has no travel synchronizer. Explicitly
        // declare this detached fixture's completed boundary, rather than
        // treating the live panel's legitimate waiting state as a capture.
        var production = new InRunMapPredictionSession();
        production.Update(native, version, true, new Dictionary<int, PredictorMapRouteDraft>
            { [native.CurrentActIndex] = panel.DisplayedDraft! });
        var captured = production.Snapshot!;
        string before = signature(), productionPlans = JsonSerializer.Serialize(production.Plans);
        InRunMapPredictionSession Fixture(string entry, bool treasure = false, bool rest = false, bool extraCards = false, bool rerolls = false,
            int goldBudget = 1000, bool tradeRelic = false, bool cook = false, bool ordinaryRest = false, bool shop = false, string treasureRelic = "POMANDER")
        {
            var source = captured.Run;
            var state = source.Working;
            int act = state.Position.Act;
            var configured = state with
            {
                OpeningCompleted = true, GoldBudget = goldBudget,
                Position = entry == "PUNCH_OFF" ? state.Position with { Floor = 7 } : state.Position,
                Relics = cook ? [new(state.NextInstanceId, new("RELIC", "MEAT_CLEAVER"), 1)] :
                    rest && !ordinaryRest ? [new(state.NextInstanceId, new("RELIC", "MINIATURE_TENT"), 1),
                    new(state.NextInstanceId + 1, new("RELIC", "DREAM_CATCHER"), 1)] :
                    rerolls ? [new(state.NextInstanceId, new("RELIC", "DRIFTWOOD"), 1)] :
                    tradeRelic ? [new(state.NextInstanceId, new("RELIC", "BLOOD_VIAL"), 1)] : [],
                NextInstanceId = state.NextInstanceId + 2,
                VisitedEvents = [], UnknownOdds = new(0, -1, 0, 0),
                Maps = state.Maps.Select(map => map.Act != act ? map : map with
                { Map = map.Map with { Points = map.Map.Points.Select(point => point.Position == first ? point with
                    { Type = rest ? MapPointType.RestSite : treasure ? MapPointType.Treasure : shop ? MapPointType.Shop : MapPointType.Unknown } : point.Position == suffix ? point with
                    { Type = MapPointType.Monster } : point).ToArray() } }).ToImmutableArray(),
                SharedBag = treasure ? new(false, [], [new("Rare", treasureRelic == "GORGET" ? [new("RELIC", "GORGET")] : [new("RELIC", treasureRelic), new("RELIC", "GORGET")])], []) : state.SharedBag,
                Acts = state.Acts.Select(value => value.Act != act ? value : value with
                { RawEvents = [new("EVENT", entry)], EventCursor = 0,
                    NormalEncounters = [new("ENCOUNTER", "FABRICATOR_NORMAL")], NormalCursor = 0 }).ToImmutableArray()
            };
            if (extraCards) configured = PredictorObtainSources.AddCard(source.Context, configured, new ModelKey("CARD", "THE_HUNT"));
            var session = new InRunMapPredictionSession();
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), members)!
                .SetValue(session, captured with { Run = new(source.Context, configured) });
            typeof(InRunMapPredictionSession).GetField("_drafts", members)!
                .SetValue(session, DiagnosticField(production, "_drafts"));
            session.Recompute();
            return session;
        }
        InRunMapPredictionSession AncientFixture(string? openingGift = null, bool pael = false)
        {
            var original = captured.Run;
            int ancientAct = openingGift == null ? 2 : 1;
            var points = new[]
            {
                new MapPredictionPoint(new(3, 0), MapPointType.Ancient, true, [new(3, 1)]),
                new MapPredictionPoint(new(3, 1), MapPointType.Monster, true, [new(3, 2)]),
                new MapPredictionPoint(new(3, 2), MapPointType.Boss, true, [])
            };
            var map = original.RouteMap(1) with { Act = original.Working.Acts.Single(value => value.Act == ancientAct).Key,
                ActIndex = ancientAct - 1, Points = points };
            var selected = points.Take(2).Select(point => point.Position).ToImmutableArray();
            var state = original.Working with
            {
                OpeningCompleted = openingGift == null, StartedWithNeow = openingGift != null || original.Working.StartedWithNeow,
                OpeningOptions = openingGift == null ? original.Working.OpeningOptions :
                    [new("RELIC", openingGift), new("RELIC", "GOLDEN_PEARL"), new("RELIC", "STONE_HUMIDIFIER")],
                Visits = [], Maps = [new(ancientAct, map, [])],
                Position = new(ancientAct, 0, "display-fixture:preentry-ancient"),
                Acts = original.Working.Acts.Select(value => value.Act != ancientAct ? value : value with
                    { Ancient = new("EVENT", pael ? "PAEL" : openingGift == null ? "OROBAS" : "NEOW"),
                        NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 }).ToImmutableArray()
            };
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            var snapshotType = typeof(InRunMapPredictionSession).GetNestedType("DraftSnapshot", BindingFlags.NonPublic)!;
            var draft = Activator.CreateInstance(snapshotType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [map, null, selected, selected], null)!;
            var emptyDrafts = DiagnosticField(new InRunMapPredictionSession(), "_drafts");
            var drafts = emptyDrafts.GetType().GetMethod("SetItem")!.Invoke(emptyDrafts, [ancientAct - 1, draft]);
            // Controlled seed/geometry; all offers, targets, fifteen cards and
            // their effects come from the real native Ancient catalog.
            for (int ordinal = 0; ordinal < 128; ordinal++)
            {
                string seed = openingGift == null ? VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, (ulong)ordinal) : original.Context.Seed;
                var context = original.Context with { Seed = seed, Live = new(2, true, []),
                    AncientAuthority = Beta109WorldSnapshotProjector.ProjectForSeed(original.Context.AncientAuthority!, seed) };
                var session = new InRunMapPredictionSession();
                typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), members)!
                    .SetValue(session, captured with { Run = new(context, state) });
                typeof(InRunMapPredictionSession).GetField("_drafts", members)!.SetValue(session, drafts);
                session.Recompute();
                if (session.Nodes.TryGetValue((ancientAct - 1, selected[0]), out var node) &&
                    (pael ? new[] { "PAELS_TOOTH" } : openingGift == null ? new[] { "SEA_GLASS", "DRIFTWOOD", "ARCHAIC_TOOTH" } : [openingGift]).All(gift => node.Ancient!.Offers.Any(offer =>
                        offer.IsKnown && offer.Value is { Selectable: true } value && value.Relic.Entry == gift))) return session;
                if (openingGift != null) break;
            }
            throw new InvalidOperationException("Native pre-entry Ancient UI fixture not found: " + (pael ? "Pael's Tooth" : openingGift ?? "Orobas three gifts"));
        }
        var product = (InRunMapNodeDetails)DiagnosticField(panel, "_details");
        var names = (IGameContentNameResolver)DiagnosticField(panel, "_names");
        var icons = (IGameIconResolver)DiagnosticField(product, "_icons");
        var details = new InRunMapNodeDetails(icons, names);
        root.AddChild(details);
        details.SetProcess(false);
        async Task Layout()
        {
            await DiagnosticFrame(root);
            typeof(InRunMapNodeDetails).GetMethod("LayoutHover", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(details, null);
            await DiagnosticFrame(root);
        }
        PredictorCard[] TopCards() => new[] { "_rewardCardDock", "_previewCardsViewport" }
            .Select(field => DiagnosticField(details, field) as Control)
            .Where(control => control?.IsVisibleInTree() == true).Cast<Control>()
            .SelectMany(control => Descendants(control).OfType<NeowOperationOverlay>())
            .SelectMany(overlay => ((System.Collections.IEnumerable)DiagnosticField(overlay, "_stacks")).Cast<object>())
            .SelectMany(stack => (ImmutableArray<PredictorCard>)stack.GetType().GetField("Cards")!.GetValue(stack)!).ToArray();
        Button? VisibleChoice(string id) => Descendants(details).OfType<Button>().SingleOrDefault(button =>
            button.IsVisibleInTree() && button.HasMeta("choice_id") && button.GetMeta("choice_id").AsString() == id);
        bool HasVisibleName(Node parent, ModelKey key) => Descendants(parent).OfType<Label>().Any(label =>
            label.IsVisibleInTree() && label.Text.Contains(names.Resolve(key, GameContentKind.Card), StringComparison.Ordinal));
        IEnumerable<InRunMapDetailSection> NestedSections(IEnumerable<InRunMapDetailSection> sections)
        {
            foreach (var section in sections)
            {
                yield return section;
                if (section.EmbeddedEditor != null)
                    foreach (var child in NestedSections(section.EmbeddedEditor.Sections)) yield return child;
            }
        }
        IEnumerable<InRunMapDetailChoice> AllChoices(IEnumerable<InRunMapDetailSection> sections) => NestedSections(sections)
            .SelectMany(section => new[] { section.HeaderChoice }.OfType<InRunMapDetailChoice>()
                .Concat(section.Choices));
        IEnumerable<OperationPreviewRow> PreviewRows(OperationPreview preview) => preview.Branches
            .SelectMany(branch => branch.Sections).SelectMany(section => section.Rows);
        async Task CheckRewardHover(InRunMapNodeDetailData data, InRunMapDetailChoice choice, string label)
        {
            details.ShowNode(data with { IsCurrent = null }, new(new(100, 500), new(56, 56)), screen.GetGlobalRect());
            await Layout();
            var main = details.HoverGlobalRegion;
            var button = VisibleChoice(choice.Id)!;
            button.EmitSignal(Control.SignalName.MouseEntered);
            await Layout();
            check(details.HoverGlobalRegion.Size.IsEqualApprox(main.Size) &&
                details.HoverGlobalRegion.Position.IsEqualApprox(main.Position) &&
                (((Control)DiagnosticField(details, "_previewViewport")).Visible ||
                    ((Control)DiagnosticField(details, "_previewCardsViewport")).Visible),
                "actual reward " + label + " hover keeps the main panel's full size and origin; before=" + main + "; after=" + details.HoverGlobalRegion);
            var groups = data.Sections.Where(section => section.LargeCardResults && section.StableId != null).ToArray();
            check(groups.Length == 2 && groups.All(section => section.Choices.Where(candidate => candidate.Enabled)
                .Any(candidate => VisibleChoice(candidate.Id) is { Disabled: false } action &&
                    action.GetGlobalRect().Intersection(main).HasArea())),
                "both real reward opportunities retain visible interactive controls during " + label + " hover on the wide canvas");
            using (var image = root.GetViewport().GetTexture().GetImage())
                image.SavePng(Path.Combine(output, "drawing-reward-hover-" + label.Replace('/', '-') + ".png"));
            typeof(InRunMapNodeDetails).GetMethod("CloseOptionPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(details, null);
            await Layout();
            check(details.HoverGlobalRegion.Size.IsEqualApprox(main.Size) && details.HoverGlobalRegion.Position.IsEqualApprox(main.Position),
                "closing reward " + label + " preview keeps the main panel's full size and origin");
        }
        void CheckSurfaces(string label)
        {
            var visible = details.GetCanvasTransform().AffineInverse() * details.GetViewportRect();
            var surfaces = new[] { "_hover", "_combatPanel", "_rewardCardDock", "_previewViewport", "_previewCardsViewport" }
                .Select(field => DiagnosticField(details, field) as Control)
                .Where(control => control?.IsVisibleInTree() == true).Cast<Control>()
                .OrderBy(control => control.GetGlobalRect().Position.Y).ToArray();
            check(surfaces.All(control => control.GetGlobalRect().HasArea()),
                "every related surface retains a nonempty reading or scrolling area in " + label + "; visible=" + visible +
                "; surfaces=" + string.Join(';', surfaces.Select(control => control.Name + ":" + control.GetGlobalRect())));
            var main = details.HoverGlobalRegion;
            foreach (var top in surfaces.Where(control => control.Name.ToString() is "MapRewardCardDock" or "NodeOptionCardsViewport"))
            {
                var overlays = Descendants(top).OfType<NeowOperationOverlay>().ToArray();
                int totalCards = overlays.Sum(overlay => overlay.MapCardCount);
                var stacks = overlays.SelectMany(overlay => ((System.Collections.IEnumerable)DiagnosticField(overlay, "_stacks")).Cast<object>()).ToArray();
                check(stacks.All(stack => (bool)stack.GetType().GetField("Stacked")!.GetValue(stack)! == (totalCards > 5)),
                    "top card layout uses the total count: up to five flat, above five in existing groups in " + label);
                if (totalCards is > 0 and <= 5)
                {
                    var tiles = stacks.SelectMany(stack => (List<Control>)stack.GetType().GetField("Tiles")!.GetValue(stack)!)
                        .OrderBy(tile => tile.GetGlobalRect().Position.X).ToArray();
                    check(tiles.Length == totalCards && tiles.Zip(tiles.Skip(1)).All(pair =>
                        pair.First.GetGlobalRect().End.X <= pair.Second.GetGlobalRect().Position.X + 1),
                        "each of the few top cards is fully spread without card overlap in " + label);
                }
            }
            check(surfaces.Where(control => control.Name.ToString() != "ContinuousHoverRegion")
                    .All(control => control.GetGlobalRect().End.Y <= main.Position.Y - 9 ||
                        control.GetGlobalRect().Position.Y >= main.End.Y + 9) &&
                surfaces.Zip(surfaces.Skip(1)).All(pair => pair.First.GetGlobalRect().End.Y <= pair.Second.GetGlobalRect().Position.Y - 9),
                "cards and child results stay on a side of the source-relative main with separate reading areas, including when both use the same side in " + label);
            foreach (var pair in surfaces.Zip(surfaces.Skip(1)))
            {
                var upper = pair.First.GetGlobalRect(); var lower = pair.Second.GetGlobalRect();
                float left = Math.Max(upper.Position.X, lower.Position.X), right = Math.Min(upper.End.X, lower.End.X);
                if (right <= left) continue;
                var gapPoint = new Vector2((left + right) * .5f, (upper.End.Y + lower.Position.Y) * .5f);
                check(details.ContainsPointer(gapPoint) && !details.BlocksMapInput(gapPoint),
                    "vertical surface gaps retain hover without owning native map clicks in " + label);
            }
        }
        async Task CheckEventOverviewRelics()
        {
            const BindingFlags overviewMembers = BindingFlags.Instance | BindingFlags.NonPublic;
            var overviewMethod = typeof(InRunMapPredictionPanel).GetMethod("Overview", overviewMembers)!;
            var renderMethod = typeof(InRunMapPredictionPanel).GetMethod("RenderInformation", overviewMembers)!;
            var oldSession = DiagnosticField(panel, "_prediction");
            var oldPresenter = DiagnosticField(panel, "_presenter");
            bool wasProcessing = panel.IsProcessing(), detailProcessing = product.IsProcessing();
            string oldLanguage = InRunFeaturePreferences.LanguageOverride;
            string oldNativeLanguage = MegaCrit.Sts2.Core.Localization.LocManager.Instance.Language;
            string languageDirectory = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state");
            var oldKinds = panel.OverviewKinds;
            int overviewAct = captured.Run.Working.Position.Act - 1, overviewChecks = 0;
            var overviewScreenshots = new List<string>();
            void Require(bool value, string message) { overviewChecks++; check(value, "EventOverview: " + message); }
            InRunMapDetailObject[] Summary(PredictorRouteNodeKnowledge node) =>
                ((IEnumerable<InRunMapDetailObject>)overviewMethod.Invoke(panel, [node])!).ToArray();
            void Bind(InRunMapPredictionSession current, InRunMapNodePresenter currentPresenter)
            {
                typeof(InRunMapPredictionPanel).GetField("_prediction", overviewMembers)!.SetValue(panel, current);
                typeof(InRunMapPredictionPanel).GetField("_presenter", overviewMembers)!.SetValue(panel, currentPresenter);
            }
            InRunMapDetailChoice Option(InRunMapNodeDetailData data, string key) =>
                AllChoices(data.Sections.Concat(data.Premises?.Sections ?? [])).First(choice => choice.Enabled &&
                    (choice.Preview?.OptionKey ?? choice.PreviewFactory?.Invoke()?.OptionKey) == key);
            async Task PictureOverview(string name)
            {
                await DiagnosticFrame(root);
                RenderingServer.ForceDraw();
                using var image = root.GetViewport().GetTexture().GetImage();
                Require(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "screenshot " + name);
                overviewScreenshots.Add(name + ".png");
            }
            async Task ShowSummary(InRunMapPredictionSession current, InRunMapNodePresenter currentPresenter,
                IGameContentNameResolver currentNames, ModelKey[] expected, string scenario)
            {
                product.CloseHover(); product.CloseEditor(); Bind(current, currentPresenter);
                renderMethod.Invoke(panel, null); await DiagnosticFrame(root);
                var overviewNode = current.Nodes[(overviewAct, first)];
                var iconsShown = Summary(overviewNode).Distinct().Where(item => item.Kind == GameContentKind.Relic).Select(item => item.Key).ToArray();
                Require(iconsShown.OrderBy(key => key.Serialized).SequenceEqual(expected.OrderBy(key => key.Serialized)),
                    scenario + " summary contains exactly the adopted relic identities");
                var annotation = ((Dictionary<MapPosition, Control>)DiagnosticField(panel, "_annotations"))[first];
                Require(annotation.IsVisibleInTree() && expected.All(key => Descendants(annotation).OfType<Label>()
                    .Count(label => label.Text == currentNames.Resolve(key, GameContentKind.Relic)) == 1),
                    scenario + " closed-hover resident annotation displays each adopted relic once");
                var allDolls = new[] { "BING_BONG", "DAUGHTER_OF_THE_WIND", "MR_STRUGGLES" }.Select(entry => new ModelKey("RELIC", entry));
                Require(allDolls.Where(key => !expected.Contains(key)).All(key => !Descendants(annotation).OfType<Label>()
                    .Any(label => label.Text == currentNames.Resolve(key, GameContentKind.Relic))),
                    scenario + " does not advertise any unchosen or retired doll");
            }
            panel.SetProcess(false); product.SetProcess(false);
            try
            {
                foreach (string overviewLanguage in new[] { "zh", "en" })
                {
                    MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(overviewLanguage == "zh" ? "zhs" : "eng");
                    InRunFeaturePreferences.SetLanguage(languageDirectory, overviewLanguage);
                    typeof(InRunMapPredictionPanel).GetMethod("RefreshLocalization", overviewMembers)!.Invoke(panel, null);
                    var overviewNames = (IGameContentNameResolver)DiagnosticField(panel, "_names");
                    var overviewText = JsonUiTextProvider.CreatePredictorUi13(overviewLanguage);
                    foreach (string parentOption in new[] { "EXAMINE", "TAKE_SOME_TIME" })
                    {
                        var dollSession = Fixture("DOLL_ROOM");
                        var dollPresenter = new InRunMapNodePresenter(dollSession, overviewNames, overviewText);
                        string callerState = JsonSerializer.Serialize(dollSession.Snapshot!.Run.Working);
                        string scenario = parentOption + "-" + overviewLanguage;
                        await ShowSummary(dollSession, dollPresenter, overviewNames, [], scenario + " initial");
                        Require(dollSession.Nodes[(overviewAct, first)].Event!.AppliedResult == null,
                            scenario + " no unselected alternative has an adopted result");
                        var dollDetail = dollPresenter.Detail((overviewAct, first))!;
                        Require(dollPresenter.ApplyChoice(dollDetail.NodeId, Option(dollDetail, parentOption).Id),
                            scenario + " actual parent choice is accepted");
                        var parentNode = dollSession.Nodes[(overviewAct, first)].Event!;
                        var dollOptions = parentNode.Options.Where(option => option.Key.StartsWith("DOLL_", StringComparison.Ordinal)).ToArray();
                        Require(dollOptions.Length == (parentOption == "EXAMINE" ? 3 : 2) &&
                            parentNode.AppliedResult is { ObtainedRelics.IsEmpty: true },
                            scenario + " real parent exposes two/three dolls without acquiring the mutually exclusive candidates");
                        await ShowSummary(dollSession, dollPresenter, overviewNames, [], scenario + " parent only");
                        ModelKey? priorRelic = null;
                        foreach (var selectedDoll in dollOptions.Take(2))
                        {
                            dollDetail = dollPresenter.Detail((overviewAct, first))!;
                            Require(dollPresenter.ApplyChoice(dollDetail.NodeId, Option(dollDetail, selectedDoll.Key).Id),
                                scenario + " actual nested choice/reselection is accepted: " + selectedDoll.Key);
                            var selectedNode = dollSession.Nodes[(overviewAct, first)];
                            var selectedRelic = selectedDoll.Value!.OfferedRelic!.Value;
                            Require(selectedNode.Event!.AppliedResult!.ObtainedRelics.Select(relic => relic.Key).SequenceEqual([selectedRelic]) &&
                                selectedNode.Inventory!.Relics.Any(relic => relic.IsKnown && relic.Value.Key == selectedRelic) &&
                                !selectedNode.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Relic) &&
                                (priorRelic == null || !selectedNode.Inventory.Relics.Any(relic => relic.IsKnown && relic.Value.Key == priorRelic)),
                                scenario + " actual acquisition, inventory and absent reward fallback identify only the current doll");
                            await ShowSummary(dollSession, dollPresenter, overviewNames, [selectedRelic], scenario + " " + selectedDoll.Key);
                            await PictureOverview("event-overview-" + scenario + "-" + selectedDoll.Key + "-summary");
                            dollDetail = dollPresenter.Detail((overviewAct, first))!;
                            var adopted = dollDetail.Sections.Single(section => section.StableId == dollDetail.NodeId + "|event-applied-outcome");
                            Require(PreviewRows(adopted.OutcomePreview!).Where(row => row.Action == OperationPreviewAction.ObtainRelic)
                                .SelectMany(row => row.Models).SequenceEqual([selectedRelic]),
                                scenario + " detail and summary agree on the same singular adopted relic");
                            product.ShowNode(dollDetail with { IsCurrent = null }, new(new(100, 350), new(56, 56)), screen.GetGlobalRect());
                            await DiagnosticFrame(root);
                            typeof(InRunMapNodeDetails).GetMethod("LayoutHover", overviewMembers)!.Invoke(product, null);
                            typeof(InRunMapPredictionPanel).GetMethod("UpdateInformationPositions", overviewMembers)!.Invoke(panel, null);
                            var rendered = ((Dictionary<MapPosition, Control>)DiagnosticField(panel, "_annotations"))[first];
                            Require(!rendered.Visible && Descendants(product).OfType<Label>().Any(label => label.IsVisibleInTree() &&
                                label.Text.Contains(overviewNames.Resolve(selectedRelic, GameContentKind.Relic), StringComparison.Ordinal)),
                                scenario + " open actual detail exposes its adopted result while resident annotation is suppressed");
                            await PictureOverview("event-overview-" + scenario + "-" + selectedDoll.Key + "-detail");
                            product.CloseHover();
                            // Current-node accepted observations need not retain a
                            // hypothetical future draft merely to remain visible.
                            var plansField = typeof(InRunMapPredictionSession).GetField("_plans", overviewMembers)!;
                            var savedPlans = plansField.GetValue(dollSession);
                            try
                            {
                                plansField.SetValue(dollSession, plansField.GetValue(new InRunMapPredictionSession()));
                                Require(dollSession.GetPlan(overviewAct, first) == null &&
                                    Summary(selectedNode).Distinct().Count(item => item.Kind == GameContentKind.Relic && item.Key == selectedRelic) == 1,
                                    scenario + " accepted observation still summarizes with no hypothetical future plan");
                            }
                            finally { plansField.SetValue(dollSession, savedPlans); }
                            priorRelic = selectedRelic;
                        }
                        dollDetail = dollPresenter.Detail((overviewAct, first))!;
                        var clear = AllChoices(dollDetail.Premises!.Sections).Single(choice =>
                            choice.Label == overviewText.Get("predictor.runtime.plan.clear"));
                        Require(dollPresenter.ApplyChoice(dollDetail.NodeId, clear.Id) && dollSession.GetPlan(overviewAct, first) == null &&
                            dollSession.Nodes[(overviewAct, first)].Event!.AppliedResult == null,
                            scenario + " actual clear-plan callback retires the adopted event prefix");
                        await ShowSummary(dollSession, dollPresenter, overviewNames, [], scenario + " cleared");
                        Require(JsonSerializer.Serialize(dollSession.Snapshot.Run.Working) == callerState && signature() == before,
                            scenario + " display and all choice transitions preserve caller snapshot and native serialized facts");
                    }
                    var rewardSession = Fixture("PUNCH_OFF");
                    var rewardPresenter = new InRunMapNodePresenter(rewardSession, overviewNames, overviewText);
                    var rewardDetail = rewardPresenter.Detail((overviewAct, first))!;
                    Require(rewardPresenter.ApplyChoice(rewardDetail.NodeId, Option(rewardDetail, "NAB").Id),
                        "actual NAB reward branch is adopted in " + overviewLanguage);
                    var rewardNode = rewardSession.Nodes[(overviewAct, first)];
                    var takenRelic = rewardNode.Event!.AppliedResult!.ObtainedRelics.Single().Key;
                    Require(rewardNode.Rewards.Any(reward => reward.IsKnown && reward.Value is
                        { Kind: PredictorRewardKind.Relic, Status: PredictorRewardStatus.Taken } value && value.Key == takenRelic),
                        "actual taken reward receipt overlaps its applied acquisition in " + overviewLanguage);
                    Bind(rewardSession, rewardPresenter);
                    Require(Summary(rewardNode).Count(item => item.Kind == GameContentKind.Relic && item.Key == takenRelic) == 2,
                        "real reward/acquisition overlap reaches the shared summary deduplication boundary");
                    await ShowSummary(rewardSession, rewardPresenter, overviewNames, [takenRelic], "NAB-" + overviewLanguage);
                    panel.OverviewKinds &= ~InRunMapOverviewKind.Relics;
                    Require(!Summary(rewardNode).Any(item => item.Kind == GameContentKind.Relic), "relic visibility mask still applies");
                    panel.OverviewKinds = oldKinds;
                }
                Require(signature() == before && JsonSerializer.Serialize(production.Plans) == productionPlans,
                    "focused adopted-event checks preserve native state and ordinary production plan");
                File.WriteAllText(Path.Combine(output, "event-overview-relics-result.json"), JsonSerializer.Serialize(new {
                    passed = true, checks = overviewChecks, screenshots = overviewScreenshots,
                    scope = "DollRoom:EXAMINE+TAKE_SOME_TIME;zh+en;unselected+parentOnly+select+reselect+clear;NAB-reward-overlap",
                    nativeRunUnchanged = true, fixture = "controlled-detached-event-through-production-presenter-and-native-panel",
                    ownerRuntimeAcceptance = "pending"
                }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"EVENT_OVERVIEW_RELICS_UI_PASS checks={overviewChecks};screenshots={overviewScreenshots.Count};actualAdoption+summary+detail+readOnly=true");
            }
            finally
            {
                product.CloseHover(); product.CloseEditor(); panel.OverviewKinds = oldKinds;
                typeof(InRunMapPredictionPanel).GetField("_prediction", overviewMembers)!.SetValue(panel, oldSession);
                typeof(InRunMapPredictionPanel).GetField("_presenter", overviewMembers)!.SetValue(panel, oldPresenter);
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(oldNativeLanguage);
                InRunFeaturePreferences.SetLanguage(languageDirectory, oldLanguage);
                typeof(InRunMapPredictionPanel).GetField("_language", overviewMembers)!.SetValue(panel, "");
                typeof(InRunMapPredictionPanel).GetMethod("RefreshLocalization", overviewMembers)!.Invoke(panel, null);
                renderMethod.Invoke(panel, null);
                panel.SetProcess(wasProcessing); product.SetProcess(detailProcessing);
            }
        }
        async Task CheckCardCollectionOverview()
        {
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            var overviewMethod = typeof(InRunMapPredictionPanel).GetMethod("Overview", members)!;
            var renderMethod = typeof(InRunMapPredictionPanel).GetMethod("RenderInformation", members)!;
            var oldSession = DiagnosticField(panel, "_prediction");
            var oldPresenter = DiagnosticField(panel, "_presenter");
            var oldKinds = panel.OverviewKinds;
            bool oldDefault = panel.UsesDefaultOverviewCardCollection;
            var oldCollection = panel.OverviewCardCollection;
            bool oldProcessing = panel.IsProcessing(), oldDetailProcessing = product.IsProcessing();
            string oldLanguage = InRunFeaturePreferences.LanguageOverride;
            string oldNativeLanguage = MegaCrit.Sts2.Core.Localization.LocManager.Instance.Language;
            string languageDirectory = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state");
            int focusedChecks = 0, act = captured.Run.Working.Position.Act - 1;
            void Require(bool value, string message) { focusedChecks++; check(value, "CardCollectionOverview: " + message); }
            InRunMapDetailObject[] Summary(PredictorRouteNodeKnowledge node) =>
                ((IEnumerable<InRunMapDetailObject>)overviewMethod.Invoke(panel, [node])!).Where(item => item.Kind == GameContentKind.Card).ToArray();
            void Bind(InRunMapPredictionSession session, InRunMapNodePresenter presenter)
            {
                typeof(InRunMapPredictionPanel).GetField("_prediction", members)!.SetValue(panel, session);
                typeof(InRunMapPredictionPanel).GetField("_presenter", members)!.SetValue(panel, presenter);
            }
            CardCollectionSnapshot Collection(params ModelKey[] keys) => new("test:overview", "Controlled projection membership", 1, keys);
            panel.SetProcess(false); product.SetProcess(false);
            try
            {
                panel.OverviewKinds = InRunMapOverviewKind.Cards;
                var session = Fixture("ABYSSAL_BATHS");
                var presenter = new InRunMapNodePresenter(session, names, JsonUiTextProvider.CreatePredictorUi13("zh"));
                Bind(session, presenter);
                var catalog = session.Snapshot!.Run.Context.Catalog!;
                var rare = catalog.CardDefinitions.First(definition => definition.Rarity == "Rare").Prototype;
                var common = catalog.CardDefinitions.First(definition => definition.Rarity == "Common").Prototype;
                var upgraded = common with { UpgradeLevel = 1, Enchantment = new("ENCHANTMENT", "TEST_OVERVIEW_ONLY"), EnchantmentAmount = 3 };
                var bare = session.Nodes[(act, first)] with { Rewards = [], Shop = null, Event = null, Ancient = null, Treasure = null,
                    OptionalObtainInputs = [], Cards = [] };
                PredictorCombatRewardKnowledge Reward(params PredictorCard[] cards) => new(PredictorRewardKind.Card,
                    new(1, PredictorRewardKind.Card, PredictorRewardOrigin.Combat, default, cards.ToImmutableArray()), []);
                var rewardNode = bare with { Rewards = [Reward(rare, common, upgraded)] };
                panel.UseDefaultOverviewCardCollection();
                var defaults = panel.OverviewCardCollection!;
                var expectedDefault = catalog.CardDefinitions.Where(definition => definition.Rarity == "Rare").Select(definition => definition.Prototype.Key).ToHashSet();
                Require(panel.UsesDefaultOverviewCardCollection && defaults.Id == "builtin:all-rare" &&
                    defaults.Cards.ToHashSet().SetEquals(expectedDefault) && Summary(rewardNode).Select(item => item.Key).SequenceEqual([rare.Key]),
                    "built-in default is actual catalog Rare membership and preserves the existing rare-only view");
                var mutable = new List<ModelKey> { common.Key };
                panel.UseOverviewCardCollection(new("test:mutable", "Caller-owned selection", 7, mutable));
                mutable.Clear(); mutable.Add(rare.Key);
                var selectedSnapshot = panel.OverviewCardCollection!;
                Require(!panel.UsesDefaultOverviewCardCollection && selectedSnapshot.Revision == 7 && selectedSnapshot.Cards.SequenceEqual([common.Key]) &&
                    !ReferenceEquals(selectedSnapshot, panel.OverviewCardCollection), "selected collection and returned snapshot detach caller-owned storage");
                Require(Summary(rewardNode).Length == 2 && Summary(rewardNode).All(item => item.Key == common.Key) &&
                    Summary(rewardNode).Any(item => item.UpgradeLevel == 1), "custom common membership excludes unselected rare and ignores upgrade/enchantment for matching");

                panel.UseOverviewCardCollection(new("test:mutable", "Explicit updated selection", 8, mutable));
                Require(panel.OverviewCardCollection!.Revision == 8 && Summary(rewardNode).Select(item => item.Key).SequenceEqual([rare.Key]),
                    "only explicit reselection captures a newer caller revision");
                panel.UseOverviewCardCollection(Collection(common.Key));

                // Controlled projection DTOs test source routing only. They do
                // not claim native generation, receipts or executable state.
                var unknownDetail = new PredictorCombatPendingCause("ControlledEnchantmentPending", "DTO: enchantment only", PredictorCombatDomain.Deck);
                var marginal = new PredictorCombatRewardKnowledge(PredictorRewardKind.Card, null, [])
                    { CardOptions = [new(common, null, [unknownDetail]), new(rare, null, [])] };
                Require(Summary(bare with { Rewards = [marginal] }).Select(item => item.Key).SequenceEqual([common.Key]),
                    "controlled DTO: identity-known reward CardOptions match even when enchantment is pending");
                PredictorShopOfferKnowledge ShopCard(int slot, PredictorShopEntryKind kind, PredictorCard card) => new(slot, kind,
                    new(slot, 0, kind, null, card, 50, 50, false, true), []);
                Require(Summary(bare with { Shop = new([ShopCard(0, PredictorShopEntryKind.CharacterCard, common),
                    ShopCard(1, PredictorShopEntryKind.ColorlessCard, upgraded), ShopCard(2, PredictorShopEntryKind.CharacterCard, rare)], []) })
                    .Select(item => item.UpgradeLevel).SequenceEqual([0, 1]), "controlled DTO: character and Colorless shop stock both participate");
                var applied = new PredictorEventOptionResultKnowledge(PredictorEventNeededInput.None, [], [], [], [], [])
                    { GeneratedCards = [new(common, []), new(rare, []), new(upgraded, [unknownDetail])] };
                var eventNode = new PredictorEventNodeKnowledge(new("EVENT", "ABYSSAL_BATHS"), [], [], [], [], [], []) { AppliedResult = applied };
                Require(Summary(bare with { Event = eventNode }).Select(item => item.Key).SequenceEqual([common.Key]) &&
                    Summary(bare with { Event = eventNode with { AuthoredActionsNotApplied = true } }).Length == 0 &&
                    Summary(bare with { Event = eventNode with { AppliedResult = null, OfferedCards = [common] } }).Length == 0,
                    "controlled DTO: only known applied event generated cards participate, not unknown cards or unadopted offers");
                var source = new ModelKey("RELIC", "HEFTY_TABLET");
                var offer = new PredictorAncientOffer(1, source, true, null, null, null, null, null);
                var ancient = new PredictorAncientNodeKnowledge(new("EVENT", "NEOW"), [], [], [], [])
                    { Outcome = new(source, [common, rare], [], [], [], []) };
                Require(Summary(bare with { Ancient = ancient }).Select(item => item.Key).SequenceEqual([common.Key]),
                    "controlled DTO: actual Ancient AddedCards use membership");
                var offered = new PredictorObtainChoiceKnowledge(PredictorPhase.ObtainChooseCard, source, [], [common, rare], [], [], []);
                Require(Summary(bare with { Ancient = ancient with { Outcome = null, OptionalCards = new(offered, 0, [], true, offer) } })
                    .Select(item => item.Key).SequenceEqual([common.Key]), "controlled DTO: Ancient native optional offered cards participate");
                var bundles = offered with { Phase = PredictorPhase.ChooseBundle, Cards = [], Bundles = [[common], [rare]] };
                Require(Summary(bare with { Ancient = ancient with { Outcome = null, BundleChoice = new(bundles, 0, null, offer) } })
                    .Select(item => item.Key).SequenceEqual([common.Key]), "controlled DTO: Ancient native candidate bundles participate");
                var deckTargets = offered with { Phase = PredictorPhase.ObtainDeckCards, Cards = [], DeckCards = [new(common, [])] };
                Require(Summary(bare with { Cards = [new(common, [])], Ancient = ancient with { Outcome = null,
                    OptionalCards = new(deckTargets, 0, [], true, offer), RequiredChoices = [deckTargets] } }).Length == 0,
                    "controlled DTO: existing deck and receipt targets are not acquisitions");
                panel.OverviewKinds = 0;
                Require(Summary(rewardNode).Length == 0, "Cards visibility mask still controls selected membership");
                panel.OverviewKinds = InRunMapOverviewKind.Cards;

                foreach (string language in new[] { "zh", "en" })
                {
                    MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(language == "zh" ? "zhs" : "eng");
                    InRunFeaturePreferences.SetLanguage(languageDirectory, language);
                    typeof(InRunMapPredictionPanel).GetMethod("RefreshLocalization", members)!.Invoke(panel, null);
                    var currentNames = (IGameContentNameResolver)DiagnosticField(panel, "_names");
                    var text = JsonUiTextProvider.CreatePredictorUi13(language);
                    presenter = new(session, currentNames, text);
                    Bind(session, presenter);
                    var runtimeNode = session.Nodes[(act, suffix)];
                    var runtimeCard = runtimeNode.Rewards.SelectMany(reward => reward.IsKnown && reward.Value != null
                        ? (IEnumerable<PredictorCard>)reward.Value.Cards : reward.CardOptions.Select(option => option.Value)).First();
                    string caller = JsonSerializer.Serialize(session.Snapshot.Run.Working), plans = JsonSerializer.Serialize(session.Plans);
                    var forecast = session.Forecast;
                    var nodes = session.Nodes;
                    long revision = session.Revision;
                    int history = session.HistoryCount;
                    var detail = presenter.Detail((act, suffix));
                    panel.UseDefaultOverviewCardCollection();
                    Require(panel.UsesDefaultOverviewCardCollection && panel.OverviewCardCollection!.Name == text.Get("predictor.map.card_collection.all_rare"),
                        "built-in name is localized/" + language);
                    product.CloseHover(); product.CloseEditor();
                    renderMethod.Invoke(panel, null);
                    panel.UseOverviewCardCollection(Collection(runtimeCard.Key));
                    await DiagnosticFrame(root);
                    var annotations = (Dictionary<MapPosition, Control>)DiagnosticField(panel, "_annotations");
                    string runtimeName = currentNames.Resolve(runtimeCard.Key, GameContentKind.Card);
                    Require(Summary(runtimeNode).Any(item => item.Key == runtimeCard.Key) && annotations.TryGetValue(suffix, out var annotation) &&
                        annotation.IsVisibleInTree() && Descendants(annotation).OfType<Label>().Any(label => label.Text.StartsWith(runtimeName, StringComparison.Ordinal)),
                        "runtime custom selection refreshes the actual resident annotation/" + language);
                    panel.UseOverviewCardCollection(Collection());
                    await DiagnosticFrame(root);
                    annotations = (Dictionary<MapPosition, Control>)DiagnosticField(panel, "_annotations");
                    Require(!panel.UsesDefaultOverviewCardCollection && panel.OverviewCardCollection!.Cards.Count == 0 && Summary(runtimeNode).Length == 0 &&
                        Summary(rewardNode).Length == 0 && (!annotations.TryGetValue(suffix, out annotation) ||
                            !Descendants(annotation).OfType<Label>().Any(label => label.Text.StartsWith(runtimeName, StringComparison.Ordinal))),
                        "empty custom selection clears card icons without falling back/" + language);
                    panel.UseDefaultOverviewCardCollection();
                    await DiagnosticFrame(root);
                    Require(panel.UsesDefaultOverviewCardCollection && panel.OverviewCardCollection!.Cards.ToHashSet().SetEquals(expectedDefault) &&
                        Summary(rewardNode).Select(item => item.Key).SequenceEqual([rare.Key]), "reset restores the built-in rare collection/" + language);
                    Require(JsonSerializer.Serialize(session.Snapshot.Run.Working) == caller && JsonSerializer.Serialize(session.Plans) == plans &&
                        ReferenceEquals(forecast, session.Forecast) && ReferenceEquals(nodes, session.Nodes) && session.Revision == revision &&
                        session.HistoryCount == history && ReferenceEquals(detail, presenter.Detail((act, suffix))) && signature() == before,
                        "collection changes remain projection-only and preserve native, caller, plans, forecast, history and details/" + language);
                }
                Require(signature() == before && JsonSerializer.Serialize(production.Plans) == productionPlans,
                    "focused collection checks preserve original native state and production plans");
                File.WriteAllText(Path.Combine(output, "card-collection-overview-result.json"), JsonSerializer.Serialize(new {
                    passed = true, checks = focusedChecks, scope = "controlled-projection-source-matrix;native-detached-session-annotations-zh+en;default+custom+empty+reset+purity",
                    nativeRunUnchanged = true, ownerRuntimeAcceptance = "pending"
                }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"CARD_COLLECTION_OVERVIEW_UI_PASS checks={focusedChecks};nativeRunUnchanged=true");
            }
            finally
            {
                product.CloseHover(); product.CloseEditor();
                typeof(InRunMapPredictionPanel).GetField("_prediction", members)!.SetValue(panel, oldSession);
                typeof(InRunMapPredictionPanel).GetField("_presenter", members)!.SetValue(panel, oldPresenter);
                panel.OverviewKinds = oldKinds;
                if (oldDefault) panel.UseDefaultOverviewCardCollection(); else panel.UseOverviewCardCollection(oldCollection!);
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(oldNativeLanguage);
                InRunFeaturePreferences.SetLanguage(languageDirectory, oldLanguage);
                typeof(InRunMapPredictionPanel).GetField("_language", members)!.SetValue(panel, "");
                typeof(InRunMapPredictionPanel).GetMethod("RefreshLocalization", members)!.Invoke(panel, null);
                renderMethod.Invoke(panel, null);
                panel.SetProcess(oldProcessing); product.SetProcess(oldDetailProcessing);
            }
        }
        async Task CheckShopGold()
        {
            int focusedChecks = 0;
            void Require(bool value, string message) { focusedChecks++; check(value, "ShopGold: " + message); }
            int act = captured.Run.Working.Position.Act - 1;
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (string language in new[] { "zh", "en" })
            {
                var text = JsonUiTextProvider.CreatePredictorUi13(language);
                string M(string key) => text.Get("predictor.map." + key);
                var session = Fixture("ABYSSAL_BATHS", shop: true);
                string caller = JsonSerializer.Serialize(session.Snapshot!.Run.Working);
                var presenter = new InRunMapNodePresenter(session, names, text);
                var data = presenter.Detail((act, first))!;
                string Balance(int gold) => text.Format("predictor.map.shop_gold_balance", gold);
                Require(data.Subtitle == Balance(1000) && !data.Sections.Any(section => section.Text == data.Subtitle) && session.GetPlan(act, first) == null,
                    "unplanned shop displays its actual balance once/" + language);
                details.ShowNode(data with { IsCurrent = null }, new(100, 220, 56, 56), screen.GetGlobalRect());
                await Layout();
                var flow = (HFlowContainer)details.FindChild("ShopOfferRows", true, false)!;
                var buttons = flow.GetChildren().OfType<Button>().ToArray();
                Require(buttons.Length >= 4 && buttons.Take(3).All(button => Math.Abs(button.GlobalPosition.Y - buttons[0].GlobalPosition.Y) < 1) &&
                    buttons[3].GlobalPosition.Y > buttons[0].GlobalPosition.Y && buttons.Take(3).All(button => button.Size.X >= 200),
                    "wide shop has three readable offers per row/" + language);
                Require(Descendants(details).OfType<Label>().Any(label => label.IsVisibleInTree() && label.Text == Balance(1000)),
                    "balance reaches the real visible surface/" + language);
                using (var shot = root.GetViewport().GetTexture().GetImage()) shot.SavePng(Path.Combine(output, "shop-gold-wide-" + language + ".png"));
                details.CloseHover();
                details.ShowNode(data with { IsCurrent = null }, new(100, 220, 56, 56), new(0, 0, 740, 900));
                await Layout();
                flow = (HFlowContainer)details.FindChild("ShopOfferRows", true, false)!;
                buttons = flow.GetChildren().OfType<Button>().ToArray();
                Require(buttons.Count(button => Math.Abs(button.GlobalPosition.Y - buttons[0].GlobalPosition.Y) < 1) <= 2 &&
                    buttons.All(button => button.GetGlobalRect().End.X <= 740),
                    "constrained shop wraps without forcing three columns outside its viewport/" + language);
                details.CloseHover();
                var offer = session.Nodes[(act, first)].Shop!.Offers.First(offer => offer.IsKnown && offer.Affordable == true &&
                    offer.Value is { Stocked: true, Card: not null });
                var buy = data.Sections.Single(section => section.ItemFlow).Choices[offer.Slot];
                Require(presenter.ApplyChoice(data.NodeId, buy.Id), "main purchase succeeds/" + language);
                data = presenter.Detail((act, first))!;
                int remaining = session.Nodes[(act, first)].Shop!.GoldBalance!.Value;
                Require(remaining == 1000 - offer.Value!.Cost && data.Subtitle == Balance(remaining),
                    "purchase refreshes the actual remaining balance/" + language);
                var remove = data.Sections.Single(section => section.PlannedActionsRow).Choices.Single(choice => choice.Label == M("remove_shop_step"));
                Require(presenter.ApplyChoice(data.NodeId, remove.Id) && presenter.Detail((act, first))!.Subtitle == Balance(1000),
                    "removing the purchase restores the no-purchase balance/" + language);

                // Actual detached Jungle -> shop: public stock remains known,
                // while the unresolved event branch leaves only gold unknown.
                var uncertain = Fixture("JUNGLE_MAZE_ADVENTURE", goldBudget: 0);
                var snap = uncertain.Snapshot!;
                var state = snap.Run.Working with { Maps = snap.Run.Working.Maps.Select(map => map.Act != act + 1 ? map : map with
                    { Map = map.Map with { Points = map.Map.Points.Select(point => point.Position == suffix ? point with
                        { Type = MapPointType.Shop } : point).ToArray() } }).ToImmutableArray() };
                typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), members)!
                    .SetValue(uncertain, snap with { Run = new(snap.Run.Context, state) });
                uncertain.Recompute();
                var source = uncertain.Nodes[(act, first)];
                var consumer = uncertain.Nodes[(act, suffix)];
                var shop = consumer.Shop!;
                var roots = PredictorRouteCauseEvidence.OriginalCauses(shop.GoldPendingCauses);
                Require(shop.GoldBalance == null && roots.Any(cause => cause.NodeId == source.Position.NodeId && cause.Code == "EventOptionChoice") &&
                    shop.Offers.Any(offer => offer.IsKnown && offer.Affordable == null), "actual upstream event uncertainty reaches shop gold, preserving known stock/" + language);
                var uncertainPresenter = new InRunMapNodePresenter(uncertain, names, text);
                var uncertainData = uncertainPresenter.Detail((act, suffix))!;
                var rows = InRunMapPrerequisiteProjection.Project(consumer, id => id == source.Position.NodeId ? "Jungle source" : "Shop", text);
                Require(rows.Any(row => row.Text.Contains("Jungle source", StringComparison.Ordinal) &&
                    row.AffectedResults.Contains(M("prerequisite.gold_balance")) && row.AffectedResults.Contains(M("prerequisite.shop_affordability"))) &&
                    uncertainData.Information.Any(line => line.Contains(text.Format("predictor.map.prerequisite.source", source.Position.Floor,
                        names.Resolve(source.Event!.Event!.Value, GameContentKind.Event)), StringComparison.Ordinal)),
                    "node summary names the actual source and limits its gold/affordability impact/" + language);
                var healthCause = roots.First(cause => cause.Code == "EventHealthOutcomePending");
                Require(rows.All(row => row.Identity != healthCause.Identity) &&
                    InRunMapNodeDiagnosticsFormatter.Pending(shop.GoldPendingCauses, text).Any(row => row.Identity == healthCause.Identity),
                    "routine health notice is absent from drawing while full diagnostic evidence remains/" + language);
                var potionCause = PredictorRouteCauseEvidence.Create("MapPotionInventoryNotProjected", source.Position.NodeId,
                    PredictorCombatDomain.PotionInventory);
                var choiceCause = roots.First(cause => cause.Code == "EventOptionChoice");
                var mixedCause = choiceCause with { AffectedDomains = choiceCause.AffectedDomains |
                    PredictorCombatDomain.Health | PredictorCombatDomain.PotionInventory, Parents = choiceCause.Parents.Add(potionCause) };
                var visibleReasons = InRunMapPrerequisiteProjection.Pending([mixedCause], text);
                Require(visibleReasons.Contains(M("prerequisite.cause.EventOptionChoice")) &&
                    !visibleReasons.Contains(M("prerequisite.cause.EventHealthOutcomePending")) &&
                    !visibleReasons.Contains(M("prerequisite.cause.MapPotionInventoryNotProjected")),
                    "supplemental cause fixture hides health/potion bookkeeping without hiding a mixed-domain gold choice/" + language);
                var sourceData = uncertainPresenter.Detail((act, first))!;
                Require(sourceData.Information.All(line => !line.Contains(M("prerequisite.cause.EventHealthOutcomePending"), StringComparison.Ordinal)) &&
                    source.Event!.Options.Any(option => option.Value?.HealthDamage is > 0 ||
                        option.ResultPreview?.PendingHealthCommands.Any(command => command.HpDelta < 0) == true),
                    "source event removes the health warning while retaining real damage information/" + language);
                var tiles = uncertainData.Sections.Single(section => section.ItemFlow).Choices;
                Require(uncertainData.Subtitle == M("shop_gold_unknown") &&
                    shop.Offers.Where(offer => offer.IsKnown && offer.Affordable == null).All(offer => !tiles[offer.Slot].Enabled &&
                        string.IsNullOrEmpty(tiles[offer.Slot].Status)), "unknown gold appears once and its causes do not repeat in each known tile/" + language);
                details.ShowNode(uncertainData with { IsCurrent = null }, new(100, 220, 56, 56), screen.GetGlobalRect());
                await Layout();
                using (var shot = root.GetViewport().GetTexture().GetImage()) shot.SavePng(Path.Combine(output, "shop-gold-unknown-" + language + ".png"));
                details.CloseHover();
                var anchoring = Fixture("SUNKEN_STATUE");
                var anchorData = new InRunMapNodePresenter(anchoring, names, text).Detail((act, first))!;
                await CheckDrawingSourceAnchoring(root, output, screen, details, anchorData, language,
                    () => signature() + "|" + JsonSerializer.Serialize(anchoring.Plans) + "|" + JsonSerializer.Serialize(anchoring.Forecast), Require);
                Require(JsonSerializer.Serialize(session.Snapshot.Run.Working) == caller && signature() == before &&
                    JsonSerializer.Serialize(production.Plans) == productionPlans, "projection and edits preserve caller and native state/" + language);
            }
            File.WriteAllText(Path.Combine(output, "shop-gold-result.json"), JsonSerializer.Serialize(new {
                passed = true, checks = focusedChecks, scope = "zh+en;actual-balance-unplanned+buy+remove;Jungle-to-shop-gold-source;health+potion-notice-suppression;diagnostics+damage-retained;three-column+constrained-wrap;vertical-edge-anchoring+drag+preview",
                nativeRunUnchanged = true, ownerRuntimeAcceptance = "pending"
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"SHOP_GOLD_UI_PASS checks={focusedChecks};nativeRunUnchanged=true");
        }
        async Task CheckShopTreasurePremises()
        {
            int focusedChecks = 0;
            void Require(bool value, string message) { focusedChecks++; check(value, "ShopTreasurePremises: " + message); }
            async Task Render(InRunMapPredictionSession session, InRunMapNodeDetailData data, string label, bool expand = false)
            {
                string caller = JsonSerializer.Serialize(session.Snapshot!.Run.Working), plans = JsonSerializer.Serialize(session.Plans);
                var forecast = session.Forecast;
                long revision = session.Revision;
                int history = session.HistoryCount;
                details.ShowNode(data with { IsCurrent = null }, new(new(120, 230), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                var expansion = details.FindChild("PremisesExpansion", true, false) as Button;
                Require((expansion != null) == (data.Premises != null), label + " exposes premises exactly when supplemental input exists");
                if (expand)
                {
                    Require(expansion != null, label + " retains supplemental input");
                    expansion!.EmitSignal(Button.SignalName.Pressed);
                    await Layout();
                }
                Require(JsonSerializer.Serialize(session.Snapshot.Run.Working) == caller && JsonSerializer.Serialize(session.Plans) == plans &&
                    ReferenceEquals(session.Forecast, forecast) && session.Revision == revision && session.HistoryCount == history && signature() == before,
                    label + " rendering preserves caller, plans, forecast, revision, history and native game");
            }
            void MainOnly(InRunMapNodeDetailData data, string label) => Require(data.Premises == null, label + " has no duplicate or empty premise editor");
            foreach (string language in new[] { "zh", "en" })
            {
                var text = JsonUiTextProvider.CreatePredictorUi13(language);
                string R(string key) => text.Get("predictor.runtime." + key);
                string M(string key) => text.Get("predictor.map." + key);
                int act = captured.Run.Working.Position.Act - 1;
                var location = (act, first);
                var shop = Fixture("ABYSSAL_BATHS", shop: true);
                string shopCaller = JsonSerializer.Serialize(shop.Snapshot!.Run.Working);
                var shopPresenter = new InRunMapNodePresenter(shop, names, text);
                var shopData = shopPresenter.Detail(location)!;
                MainOnly(shopData, "initial shop/" + language);
                await Render(shop, shopData, "initial shop/" + language);
                var initialTiles = shopData.Sections.Single(section => section.ItemFlow).Choices;
                var buys = shop.Nodes[location].Shop!.Offers.Where(offer => offer.IsKnown && offer.Affordable == true &&
                    offer.Value is { Stocked: true, Card: not null }).Take(2).ToArray();
                Require(buys.Length == 2 && buys.All(offer => VisibleChoice(initialTiles[offer.Slot].Id) is { Disabled: false }),
                    "main shop exposes two real card purchases/" + language);
                foreach (var offer in buys)
                {
                    shopData = shopPresenter.Detail(location)!;
                    var buy = shopData.Sections.Single(section => section.ItemFlow).Choices[offer.Slot];
                    Require(buy.Enabled && shopPresenter.ApplyChoice(shopData.NodeId, buy.Id), "main shop card purchase authors slot " + offer.Slot + "/" + language);
                }
                shopData = shopPresenter.Detail(location)!;
                var originalOrder = shop.GetPlan(act, first)!.Shop!.Actions.Where(action => action.Action == PredictorShopActionKind.Buy)
                    .Select(action => action.ExpectedOffer!.Slot).ToArray();
                Require(originalOrder.SequenceEqual(buys.Select(offer => offer.Slot)) && shop.Nodes[location].Shop!.AppliedActions == 2,
                    "two main card purchases retain authored order and applied receipts/" + language);
                MainOnly(shopData, "purchased shop/" + language);
                await Render(shop, shopData, "purchased shop/" + language);
                var move = shopData.Sections.Where(section => section.PlannedActionsRow).Last().Choices.Single(choice => choice.Label == M("move_earlier"));
                Require(move.Enabled && VisibleChoice(move.Id) is { Disabled: false } && shopPresenter.ApplyChoice(shopData.NodeId, move.Id) &&
                    shop.GetPlan(act, first)!.Shop!.Actions.Where(action => action.Action == PredictorShopActionKind.Buy)
                        .Select(action => action.ExpectedOffer!.Slot).SequenceEqual(originalOrder.AsEnumerable().Reverse()),
                    "main plan move reorders the two actual purchases/" + language);
                shopData = shopPresenter.Detail(location)!;
                var remove = shopData.Sections.Where(section => section.PlannedActionsRow).First().Choices.Single(choice => choice.Label == M("remove_shop_step"));
                Require(shopPresenter.ApplyChoice(shopData.NodeId, remove.Id) && shop.GetPlan(act, first)!.Shop!.Actions.Count(action => action.Action == PredictorShopActionKind.Buy) == 1,
                    "main plan removal deletes only its selected purchase step/" + language);
                MainOnly(shopPresenter.Detail(location)!, "edited shop/" + language);
                details.CloseHover();

                var removalSession = Fixture("ABYSSAL_BATHS", shop: true);
                string removalCaller = JsonSerializer.Serialize(removalSession.Snapshot!.Run.Working);
                var removalPresenter = new InRunMapNodePresenter(removalSession, names, text);
                var removalData = removalPresenter.Detail(location)!;
                bool BeginRemoval()
                {
                    removalData = removalPresenter.Detail(location)!;
                    var begin = removalData.Sections.Single(section => section.ItemFlow).Choices.Single(choice => choice.Label == R("remove_card"));
                    return begin.Enabled && removalPresenter.ApplyChoice(removalData.NodeId, begin.Id);
                }
                Require(BeginRemoval(), "main removal tile opens the real card picker/" + language);
                removalData = removalPresenter.Detail(location)!;
                MainOnly(removalData, "pending removal/" + language);
                await Render(removalSession, removalData, "pending removal/" + language);
                var cancel = AllChoices(removalData.Sections).Single(choice => choice.Label == R("cancel"));
                Require(VisibleChoice(cancel.Id) is { Disabled: false } && removalPresenter.ApplyChoice(removalData.NodeId, cancel.Id) &&
                    !removalSession.Nodes[location].Shop!.PendingCauses.Any(cause => cause.Code == "ShopRemovalChoice"),
                    "main removal cancel closes the real pending choice/" + language);
                Require(BeginRemoval(), "main removal remains available after cancel/" + language);
                removalData = removalPresenter.Detail(location)!;
                var removeCard = AllChoices(removalData.Sections).First(choice => choice.Enabled && choice.Label == R("remove_card") && choice.Object?.CardSnapshot != null);
                long removedId = removeCard.Object!.CardSnapshot!.Id;
                Require(removalPresenter.ApplyChoice(removalData.NodeId, removeCard.Id) &&
                    removalSession.GetPlan(act, first)!.Shop!.Actions.Any(action => action.Action == PredictorShopActionKind.ChooseRemoval && action.Card?.InstanceId == removedId) &&
                    !removalSession.Nodes[location].Cards.Any(card => card.IsKnown && card.Value.Id == removedId),
                    "main removal target removes its actual card identity/" + language);
                MainOnly(removalPresenter.Detail(location)!, "completed removal/" + language);
                details.CloseHover();

                var poor = Fixture("ABYSSAL_BATHS", shop: true, goldBudget: 0);
                string poorCaller = JsonSerializer.Serialize(poor.Snapshot!.Run.Working);
                var poorData = new InRunMapNodePresenter(poor, names, text).Detail(location)!;
                var poorTiles = poorData.Sections.Single(section => section.ItemFlow).Choices;
                var unaffordable = poor.Nodes[location].Shop!.Offers.Where(offer => offer.Affordable == false).ToArray();
                Require(unaffordable.Length > 0 && unaffordable.All(offer => !poorTiles[offer.Slot].Enabled &&
                    poorTiles[offer.Slot].Status!.Contains(M("shop_insufficient_gold"), StringComparison.Ordinal)),
                    "poor shop preserves localized affordability causes on main tiles/" + language);
                MainOnly(poorData, "poor shop/" + language);
                await Render(poor, poorData, "poor shop/" + language);
                details.CloseHover();

                var chest = Fixture("ABYSSAL_BATHS", treasure: true, treasureRelic: "GORGET");
                string chestCaller = JsonSerializer.Serialize(chest.Snapshot!.Run.Working);
                var chestPresenter = new InRunMapNodePresenter(chest, names, text);
                var chestData = chestPresenter.Detail(location)!;
                Require(chest.Nodes[location].Treasure!.OfferedRelic?.Entry == "GORGET", "ordinary chest has its actual simple relic/" + language);
                MainOnly(chestData, "ordinary chest/" + language);
                await Render(chest, chestData, "ordinary chest/" + language);
                Require(new[] { "plan.take", "plan.skip" }.All(action => AllChoices(chestData.Sections).Single(choice => choice.Label == R(action)) is { } choice &&
                    VisibleChoice(choice.Id) is { Disabled: false }), "ordinary chest renders both main Take/Skip controls/" + language);
                foreach (string action in new[] { "plan.skip", "plan.take" })
                {
                    chestData = chestPresenter.Detail(location)!;
                    var choice = AllChoices(chestData.Sections).Single(candidate => candidate.Label == R(action));
                    Require(choice.Enabled && chestPresenter.ApplyChoice(chestData.NodeId, choice.Id) &&
                        chest.Nodes[location].Treasure!.RelicTaken == (action == "plan.take"),
                        "ordinary chest main " + action + " changes actual take/skip outcome/" + language);
                    MainOnly(chestPresenter.Detail(location)!, "ordinary chest " + action + "/" + language);
                }
                details.CloseHover();

                var targetChest = Fixture("ABYSSAL_BATHS", treasure: true);
                string targetCaller = JsonSerializer.Serialize(targetChest.Snapshot!.Run.Working);
                var targetPresenter = new InRunMapNodePresenter(targetChest, names, text);
                var targetData = targetPresenter.Detail(location)!;
                Require(targetChest.Nodes[location].Treasure!.OfferedRelic?.Entry == "POMANDER" &&
                    !AllChoices(targetData.Premises?.Sections ?? []).Any(choice => choice.Label == R("plan.skip") ||
                        choice.Label == R("plan.take") && choice.Object?.CardSnapshot == null),
                    "target chest keeps main Take/Skip out of supplemental targets/" + language);
                await Render(targetChest, targetData, "target chest/" + language, true);
                Require(new[] { "plan.take", "plan.skip" }.All(action => AllChoices(targetData.Sections).Single(choice => choice.Label == R(action)) is { } choice &&
                    VisibleChoice(choice.Id) is { Disabled: false }), "target chest retains both main Take/Skip controls with expanded targets/" + language);
                var firstTarget = AllChoices(targetData.Premises!.Sections).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                long firstId = firstTarget.Object!.CardSnapshot!.Id;
                Require(VisibleChoice(firstTarget.Id) is { Disabled: false } && targetPresenter.ApplyChoice(targetData.NodeId, firstTarget.Id) &&
                    targetChest.GetPlan(act, first)!.ObtainInputs.Single().Input.DeckCards.Single().InstanceId == firstId,
                    "target chest retains its real address-bound card target callback/" + language);
                targetData = targetPresenter.Detail(location)!;
                var secondTarget = AllChoices(targetData.Premises!.Sections).First(choice => choice.Enabled && choice.Object?.CardSnapshot is { } card && card.Id != firstId);
                long secondId = secondTarget.Object!.CardSnapshot!.Id;
                Require(targetPresenter.ApplyChoice(targetData.NodeId, secondTarget.Id) &&
                    targetChest.GetPlan(act, first)!.ObtainInputs.Single().Input.DeckCards.Single().InstanceId == secondId,
                    "target chest reselect replaces only its optional target/" + language);
                targetData = targetPresenter.Detail(location)!;
                var clear = AllChoices(targetData.Premises!.Sections).Single(choice => choice.Label == M("clear_optional_input"));
                Require(targetPresenter.ApplyChoice(targetData.NodeId, clear.Id) && targetChest.GetPlan(act, first)!.ObtainInputs.IsEmpty &&
                    targetChest.Nodes[(act, suffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                    "target chest clear restores targetless continuation with independent suffix rewards/" + language);
                details.CloseHover();
                Require(JsonSerializer.Serialize(shop.Snapshot.Run.Working) == shopCaller &&
                    JsonSerializer.Serialize(removalSession.Snapshot.Run.Working) == removalCaller && JsonSerializer.Serialize(poor.Snapshot.Run.Working) == poorCaller &&
                    JsonSerializer.Serialize(chest.Snapshot.Run.Working) == chestCaller && JsonSerializer.Serialize(targetChest.Snapshot.Run.Working) == targetCaller,
                    "shop and chest detached edits preserve every caller state/" + language);
            }
            Require(signature() == before && JsonSerializer.Serialize(production.Plans) == productionPlans,
                "focused shop and chest authoring preserves native game and original production plans");
            File.WriteAllText(Path.Combine(output, "shop-treasure-premises-result.json"), JsonSerializer.Serialize(new {
                passed = true, checks = focusedChecks, scope = "zh+en;shop-buy+move+remove+card-removal+poor;Gorget-take+skip;Pomander-select+reselect+clear",
                nativeRunUnchanged = true, ownerRuntimeAcceptance = "pending"
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"SHOP_TREASURE_PREMISES_UI_PASS checks={focusedChecks};nativeRunUnchanged=true");
        }
        async Task CheckRestAncientPremises()
        {
            int focusedChecks = 0;
            void Require(bool value, string message) { focusedChecks++; check(value, "RestAncientPremises: " + message); }
            async Task Render(InRunMapNodeDetailData data, string name, bool expand = false)
            {
                details.ShowNode(data with { IsCurrent = null }, new(new(120, 230), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                var expansion = details.FindChild("PremisesExpansion", true, false) as Button;
                Require((expansion != null) == (data.Premises != null), name + " renders an expansion exactly when supplemental inputs exist");
                if (expand)
                {
                    Require(expansion != null, name + " retains its supplemental picker");
                    expansion!.EmitSignal(Button.SignalName.Pressed);
                    await Layout();
                }
            }
            void NoDuplicates(InRunMapNodeDetailData data, string name)
            {
                var main = AllChoices(data.Sections).ToArray();
                var extra = AllChoices(data.Premises?.Sections ?? []).ToArray();
                Require(!extra.Any(choice => main.Any(primary => primary.Id == choice.Id)), name + " does not register the same callback twice");
                Require(!extra.Where(choice => choice.Object?.CardSnapshot == null).Any(choice => main.Any(primary => primary.Label == choice.Label && primary.Object?.Key == choice.Object?.Key)),
                    name + " keeps main actions and gift identities out of premises");
            }
            foreach (string language in new[] { "zh", "en" })
            {
                var text = JsonUiTextProvider.CreatePredictorUi13(language);
                int act = captured.Run.Working.Position.Act - 1;
                var ordinary = Fixture("ABYSSAL_BATHS", rest: true, ordinaryRest: true);
                string ordinaryCaller = JsonSerializer.Serialize(ordinary.Snapshot!.Run.Working);
                var ordinaryPresenter = new InRunMapNodePresenter(ordinary, names, text);
                var smith = ordinaryPresenter.Detail((act, first))!;
                var smithTarget = AllChoices(smith.Premises?.Sections ?? []).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                Require(ordinary.Nodes[(act, first)].DefaultRestAction is { Action: PredictorRestAction.Smith, SmithCard: null },
                    "ordinary default Smith has no fabricated target/" + language);
                NoDuplicates(smith, "default Smith/" + language);
                await Render(smith, "default Smith/" + language, true);
                Require(VisibleChoice(smithTarget.Id) is { Disabled: false } && TopCards().Length == 0,
                    "optional Smith target is usable and compact/" + language);
                details.CloseHover();
                Require(ordinaryPresenter.ApplyChoice(smith.NodeId, smithTarget.Id) && ordinary.GetPlan(act, first)!.RestActions[0].SmithCard != null,
                    "default Smith target still authors its actual action/" + language);
                Require(ordinary.SetPlan(act, first, new PredictorPlannedNode(ordinary.Nodes[(act, first)].Position.NodeId)
                    { RestActions = [new(PredictorRestAction.Heal) { MaximumHp = 80 }], LeaveRest = true }), "ordinary no-target Heal is authored/" + language);
                var healed = ordinaryPresenter.Detail((act, first))!;
                Require(healed.Premises == null && ordinary.Nodes[(act, first)].AppliedRestActions == 1,
                    "ordinary completed no-target Heal has no premise/" + language);
                await Render(healed, "ordinary no-target Heal/" + language);
                details.CloseHover();
                Require(JsonSerializer.Serialize(ordinary.Snapshot.Run.Working) == ordinaryCaller, "ordinary edits preserve caller/" + language);

                var tent = Fixture("ABYSSAL_BATHS", rest: true);
                string tentCaller = JsonSerializer.Serialize(tent.Snapshot!.Run.Working);
                Require(tent.SetPlan(act, first, new PredictorPlannedNode(tent.Nodes[(act, first)].Position.NodeId)
                    { RestActions = [new(PredictorRestAction.Smith), new(PredictorRestAction.Heal) { MaximumHp = 80 }], LeaveRest = true }),
                    "Tent has its actual two-action plan/" + language);
                var tentPresenter = new InRunMapNodePresenter(tent, names, text);
                var tentDetail = tentPresenter.Detail((act, first))!;
                NoDuplicates(tentDetail, "Tent/" + language);
                Require(tentDetail.Sections.Count(section => section.PlannedActionsRow) == 2 &&
                    !AllChoices(tentDetail.Premises?.Sections ?? []).Any(choice => choice.Label.StartsWith(text.Get("predictor.runtime.plan.remove_action"), StringComparison.Ordinal) ||
                        choice.Label == text.Get("predictor.runtime.plan.leave_rest")), "Tent retains main action rows without premise removal/leave copies/" + language);
                var tentTarget = AllChoices(tentDetail.Premises?.Sections ?? []).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                Require(tentPresenter.ApplyChoice(tentDetail.NodeId, tentTarget.Id) && tent.GetPlan(act, first) is
                    { RestActions.Length: 2 } updated && updated.RestActions[0].SmithCard != null && updated.RestActions[1].Action == PredictorRestAction.Heal &&
                    JsonSerializer.Serialize(tent.Snapshot.Run.Working) == tentCaller, "Tent optional Smith target preserves Heal suffix and caller/" + language);

                var ancientPoint = new MapPosition(3, 0);
                foreach (string gift in new[] { "ARCANE_SCROLL", "HEFTY_TABLET", "SCROLL_BOXES", "SEA_GLASS", "PAELS_TOOTH" })
                {
                    bool pael = gift == "PAELS_TOOTH", sea = gift == "SEA_GLASS";
                    int ancientAct = pael || sea ? 1 : 0;
                    var session = pael ? AncientFixture(pael: true) : sea ? AncientFixture() : AncientFixture(gift);
                    string caller = JsonSerializer.Serialize(session.Snapshot!.Run.Working);
                    var presenter = new InRunMapNodePresenter(session, names, text);
                    var data = presenter.Detail((ancientAct, ancientPoint))!;
                    Require(data.Premises == null, gift + " before selection has no supplemental input/" + language);
                    await Render(data, gift + " before selection/" + language);
                    details.CloseHover();
                    var choose = AllChoices(data.Sections).Single(choice => choice.Object?.Key.Entry == gift);
                    Require(presenter.ApplyChoice(data.NodeId, choose.Id), gift + " main gift remains actionable/" + language);
                    data = presenter.Detail((ancientAct, ancientPoint))!;
                    NoDuplicates(data, gift + "/" + language);
                    if (gift is "ARCANE_SCROLL" or "SCROLL_BOXES")
                    {
                        Require(data.Premises == null, gift + " has no supplemental editor after selection/" + language);
                        await Render(data, gift + " selected/" + language);
                        if (gift == "ARCANE_SCROLL")
                        {
                            var cancelGift = AllChoices(data.Sections).Single(choice => choice.Object?.Key.Entry == gift && choice.Selected);
                            Require(VisibleChoice(cancelGift.Id) is { Disabled: false } && presenter.ApplyChoice(data.NodeId, cancelGift.Id) &&
                                session.GetPlan(ancientAct, ancientPoint)?.Ancient == null,
                                "re-clicking the selected Arcane Scroll main gift clears its authored Ancient/" + language);
                            var cleared = presenter.Detail((ancientAct, ancientPoint))!;
                            Require(cleared.Premises == null && !AllChoices(cleared.Sections).Any(choice => choice.Object?.Key.Entry == gift && choice.Selected),
                                "cleared Arcane Scroll restores unselected main gift without a reset premise/" + language);
                            details.CloseHover();
                            await Render(cleared, "Arcane Scroll cleared/" + language);
                        }
                        if (gift == "SCROLL_BOXES")
                        {
                            var bundles = data.Sections.Where(section => section.StableId?.StartsWith("ancient-bundle-", StringComparison.Ordinal) == true)
                                .SelectMany(section => section.Choices).Where(choice => choice.Enabled).ToArray();
                            Require(bundles.Length == 2 && bundles.All(choice => VisibleChoice(choice.Id) is { Disabled: false }),
                                "Scroll Boxes keeps both actual bundle controls in main/" + language);
                            Require(presenter.ApplyChoice(data.NodeId, bundles[0].Id) && presenter.Detail((ancientAct, ancientPoint))!.Premises == null,
                                "Scroll Boxes chosen bundle remains main-only/" + language);
                        }
                    }
                    else
                    {
                        var target = AllChoices(data.Premises?.Sections ?? []).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                        Require(!AllChoices(data.Sections).Any(choice => choice.Object?.CardSnapshot == target.Object!.CardSnapshot),
                            gift + " optional target is absent from main choices/" + language);
                        await Render(data, gift + " selected/" + language, true);
                        Require(VisibleChoice(target.Id) is { Disabled: false }, gift + " retained target is visible and usable/" + language);
                        Require(presenter.ApplyChoice(data.NodeId, target.Id), gift + " retained target callback accepts real input/" + language);
                        if (gift == "HEFTY_TABLET")
                            Require(session.GetPlan(ancientAct, ancientPoint)!.Ancient!.Children.Single().CardIndices.Length == 1,
                                "Hefty optional target is saved/" + language);
                    }
                    details.CloseHover();
                    Require(JsonSerializer.Serialize(session.Snapshot.Run.Working) == caller, gift + " preserves caller state/" + language);
                }
            }
            Require(signature() == before && JsonSerializer.Serialize(production.Plans) == productionPlans,
                "focused authoring preserves native state and ordinary production plans");
            File.WriteAllText(Path.Combine(output, "rest-ancient-premises-result.json"), JsonSerializer.Serialize(new {
                passed = true, checks = focusedChecks, scope = "zh+en;ordinary-Heal+default-Smith+Tent;ArcaneScroll+Hefty+SeaGlass+ScrollBoxes+Pael",
                nativeRunUnchanged = true, ownerRuntimeAcceptance = "pending"
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"REST_ANCIENT_PREMISES_UI_PASS checks={focusedChecks};nativeRunUnchanged=true");
        }
        async Task CheckNoticeCleanup()
        {
            int focusedChecks = 0;
            void Require(bool value, string message) { focusedChecks++; check(value, "DrawingNotices: " + message); }
            string[] VisibleText(Node parent) => Descendants(parent).OfType<Label>()
                .Where(label => label.IsVisibleInTree()).Select(label => label.Text).ToArray();
            bool HasGenericPending(IEnumerable<string> lines) => new[] { "zh", "en" }.Any(language =>
                lines.Any(line => line.Contains(JsonUiTextProvider.CreateUi13(language)
                    .Get("operation.pending.undeterminedInputs"), StringComparison.Ordinal)));
            async Task Render(InRunMapNodeDetailData data)
            {
                details.CloseHover();
                details.ShowNode(data with { IsCurrent = null }, new(120, 230, 56, 56), screen.GetGlobalRect());
                await Layout();
            }
            int act = captured.Run.Working.Position.Act - 1;
            foreach (string language in new[] { "zh", "en" })
            {
                var text = JsonUiTextProvider.CreatePredictorUi13(language);
                var shop = Fixture("ABYSSAL_BATHS", shop: true);
                string caller = JsonSerializer.Serialize(shop.Snapshot!.Run.Working);
                var presenter = new InRunMapNodePresenter(shop, names, text);
                var data = presenter.Detail((act, first))!;
                var offer = shop.Nodes[(act, first)].Shop!.Offers.First(item => item.IsKnown && item.Affordable == true &&
                    item.Value is { Stocked: true, Card: not null });
                var buy = data.Sections.Single(section => section.ItemFlow).Choices[offer.Slot];
                var purchase = buy.PreviewFactory!()!;
                Require(purchase.PendingReasons.IsEmpty && purchase.Branches.All(branch => branch.FullyEnumerated && branch.PendingReasons.IsEmpty) &&
                    purchase.Notices.Any(line => line.Contains(offer.Value!.Cost + "G", StringComparison.Ordinal)) && purchase.Faults.IsEmpty,
                    "successful detached purchase retains the quote without classifying stock text as pending/" + language);
                await Render(data);
                VisibleChoice(buy.Id)!.EmitSignal(Control.SignalName.MouseEntered);
                await Layout();
                var overlay = (NeowOperationOverlay)DiagnosticField(details, "_optionPreview");
                Require(overlay.IsVisibleInTree() && VisibleText(overlay).Any(line => line.Contains(offer.Value!.Cost + "G", StringComparison.Ordinal)) &&
                    !HasGenericPending(VisibleText(overlay)) && shop.Plans.IsEmpty && JsonSerializer.Serialize(shop.Snapshot.Run.Working) == caller,
                    "actual successful shop hover displays stock and price without a false missing-input warning or authoring state/" + language);

                // Controlled presentation DTO: a specific localized reward cause,
                // supplied through the production adapter and real hover renderer.
                // This does not claim that a native reward generated this cause.
                string cause = text.Get("predictor.map.prerequisite.cause.EventOptionChoice");
                var reward = InRunMapOptionPreview.BuildReward(new("RELIC", "BLOOD_VIAL"), "notice-fixture", null, [], [cause], [cause]);
                var controlledChoice = new InRunMapDetailChoice("notice-fixture", "Controlled reward notice fixture") { Preview = reward };
                var controlled = data with { NodeId = "notice-fixture", Subtitle = "", Information = [], Premises = null,
                    Sections = [new("Controlled reward notice fixture", "", [], [controlledChoice])] };
                Require(reward.NoticesAreLocalized && reward.PendingReasons.IsEmpty && reward.Notices.Count(line => line == cause) == 1 &&
                    reward.Branches.All(branch => !branch.FullyEnumerated),
                    "controlled unresolved reward preserves its localized specific cause and incomplete status/" + language);
                await Render(controlled);
                VisibleChoice(controlledChoice.Id)!.EmitSignal(Control.SignalName.MouseEntered);
                await Layout();
                overlay = (NeowOperationOverlay)DiagnosticField(details, "_optionPreview");
                Require(overlay.IsVisibleInTree() && VisibleText(overlay).Count(line => line == cause) == 1 && !HasGenericPending(VisibleText(overlay)),
                    "actual reward overlay shows the complete localized cause once without a generic duplicate/" + language);

                var combat = Fixture("ABYSSAL_BATHS");
                var combatPresenter = new InRunMapNodePresenter(combat, names, text);
                var battle = combatPresenter.Detail((act, suffix))!;
                await Render(battle);
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Layout();
                var number = battle.Premises!.Numbers.First();
                Require(Descendants(details).OfType<SpinBox>().Any(spin => spin.IsVisibleInTree()) &&
                    !VisibleText(details).Any(line => line.Contains(text.Get("predictor.map.optional_niche_input"), StringComparison.Ordinal) ||
                        line.Contains(text.Get("predictor.runtime.combat_local_facts_pending"), StringComparison.Ordinal)) &&
                    combatPresenter.ApplyField(battle.NodeId, number.Id, new(Number: 2)) && combat.GetPlan(act, suffix)?.Combat != null,
                    "normal combat premises retain visible editable numeric inputs without repeated precision or aggregate health prose/" + language);

                var rest = Fixture("ABYSSAL_BATHS", rest: true, ordinaryRest: true);
                var restPresenter = new InRunMapNodePresenter(rest, names, text);
                var restData = restPresenter.Detail((act, first))!;
                await Render(restData);
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Layout();
                var heal = AllChoices(restData.Premises!.Sections).First(choice => choice.Label.StartsWith(text.Get("predictor.runtime.rest.Heal"), StringComparison.Ordinal));
                Require(VisibleChoice(heal.Id) is { Disabled: false } &&
                    !VisibleText(details).Any(line => line.Contains(text.Get("predictor.map.rest_snapshot_options"), StringComparison.Ordinal)) &&
                    restPresenter.ApplyChoice(restData.NodeId, heal.Id),
                    "ordinary rest retains its real Heal entrance without the snapshot implementation explanation/" + language);

                var ancient = AncientFixture("SCROLL_BOXES");
                var ancientPresenter = new InRunMapNodePresenter(ancient, names, text);
                var ancientPoint = new MapPosition(3, 0);
                var ancientData = ancientPresenter.Detail((0, ancientPoint))!;
                var gift = AllChoices(ancientData.Sections).Single(choice => choice.Object?.Key.Entry == "SCROLL_BOXES");
                Require(ancientPresenter.ApplyChoice(ancientData.NodeId, gift.Id), "Scroll Boxes real gift remains selectable/" + language);
                ancientData = ancientPresenter.Detail((0, ancientPoint))!;
                var bundles = ancientData.Sections.Where(section => section.StableId is "ancient-bundle-0" or "ancient-bundle-1")
                    .SelectMany(section => section.Choices).ToArray();
                await Render(ancientData);
                Require(bundles.Length == 2 && bundles.All(choice => !choice.Selected && VisibleChoice(choice.Id) is { Disabled: false }) &&
                    !VisibleText(details).Any(line => line.Contains(text.Get("predictor.map.ancient_bundle_unselected"), StringComparison.Ordinal)) &&
                    ancientPresenter.ApplyChoice(ancientData.NodeId, bundles[0].Id) &&
                    ancientPresenter.Detail((0, ancientPoint))!.Sections.Single(section => section.StableId == "ancient-bundle-0").Choices.Single().Selected,
                    "two real bundle choices express unselected and selected state without a redundant paragraph/" + language);
                ancientData = ancientPresenter.Detail((0, ancientPoint))!;
                await Render(ancientData);
                var renderedBundles = ancientData.Sections.Where(section => section.StableId is "ancient-bundle-0" or "ancient-bundle-1")
                    .SelectMany(section => section.Choices).ToArray();
                var selectedBundle = VisibleChoice(renderedBundles.Single(choice => choice.Selected).Id);
                var otherBundle = VisibleChoice(renderedBundles.Single(choice => !choice.Selected).Id);
                Require(selectedBundle is { Disabled: false } && otherBundle is { Disabled: false } &&
                    selectedBundle.GetThemeStylebox("normal") is StyleBoxFlat selectedStyle &&
                    otherBundle.GetThemeStylebox("normal") is StyleBoxFlat otherStyle && selectedStyle.BorderColor != otherStyle.BorderColor,
                    "rendered bundle selection remains visibly distinct while both real controls stay usable/" + language);
                using var shot = root.GetViewport().GetTexture().GetImage();
                shot.SavePng(Path.Combine(output, "drawing-notices-bundles-" + language + ".png"));
            }
            Require(signature() == before && JsonSerializer.Serialize(production.Plans) == productionPlans,
                "focused presentation and detached authoring preserve native state and production plans");
            File.WriteAllText(Path.Combine(output, "drawing-notices-result.json"), JsonSerializer.Serialize(new {
                passed = true, checks = focusedChecks,
                scope = "zh+en;native-shop-hover;controlled-localized-reward-overlay;combat-inputs;rest;ScrollBoxes",
                nativeRunUnchanged = true, ownerRuntimeAcceptance = "pending"
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"DRAWING_NOTICES_UI_PASS checks={focusedChecks};nativeRunUnchanged=true");
        }
        try
        {
            if (OS.GetCmdlineUserArgs().Contains("--drawing-notices-ui"))
            {
                await CheckNoticeCleanup();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--shop-gold-ui"))
            {
                await CheckShopGold();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--card-collection-overview-ui"))
            {
                await CheckCardCollectionOverview();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--shop-treasure-premises-ui"))
            {
                await CheckShopTreasurePremises();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--rest-ancient-premises-ui"))
            {
                await CheckRestAncientPremises();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--event-overview-relics-ui"))
            {
                await CheckEventOverviewRelics();
                return;
            }
            // Bounded native animation-pose fixtures: presentation refresh may
            // read these real nodes, but must not own their root/child scales.
            var nativeVisuals = new List<System.Collections.DictionaryEntry>();
            var nativeVisualEnumerator = ((System.Collections.IDictionary)DiagnosticField(panel, "_visuals")).GetEnumerator();
            while (nativeVisualEnumerator.MoveNext()) nativeVisuals.Add(nativeVisualEnumerator.Entry);
            var poseVisuals = new[]
            {
                nativeVisuals.Single(entry => (MapPosition)entry.Key == first &&
                    entry.Value!.GetType().GetProperty("Node")!.GetValue(entry.Value) is NNormalMapPoint),
                nativeVisuals.First(entry => entry.Value!.GetType().GetProperty("Node")!.GetValue(entry.Value) is NAncientMapPoint)
            };
            var hits = (HashSet<MapPosition>)DiagnosticField(panel, "_hits");
            var savedHits = hits.ToArray();
            var refresh = typeof(InRunMapPredictionPanel).GetMethod("RefreshVisuals", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var globalRegion = typeof(InRunMapPredictionPanel).GetMethod("GlobalRegion", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var entry in poseVisuals)
            {
                var node = (Control)entry.Value!.GetType().GetProperty("Node")!.GetValue(entry.Value)!;
                var scales = Descendants(node).OfType<Control>().ToDictionary(control => control, control => control.Scale);
                var nominal = (Rect2)globalRegion.Invoke(panel, [entry.Value])!;
                try
                {
                    node.Scale *= new Vector2(1.08f, .96f);
                    foreach (string path in new[] { "IconContainer", "IconContainer/Icon", "IconContainer/QuestIcon", "Icon" })
                        if (node.GetNodeOrNull<Control>(path) is { } child) child.Scale *= new Vector2(1.06f, .94f);
                    node.EmitSignal(Control.SignalName.MouseEntered);
                    var posed = scales.Keys.ToDictionary(control => control, control => control.Scale);
                    refresh.Invoke(panel, null);
                    check(posed.All(pair => pair.Key.Scale.IsEqualApprox(pair.Value)),
                        "refresh preserves native root and child animation-pose scales on selected/hovered " + node.GetType().Name);
                    hits.Add((MapPosition)entry.Key);
                    refresh.Invoke(panel, null);
                    check(posed.All(pair => pair.Key.Scale.IsEqualApprox(pair.Value)) &&
                        ((Rect2)globalRegion.Invoke(panel, [entry.Value])!).IsEqualApprox(nominal),
                        "a recognized stroke hit preserves native scales and nominal anchor despite a pulse pose on " + node.GetType().Name);
                }
                finally
                {
                    node.EmitSignal(Control.SignalName.MouseExited);
                    foreach (var (control, scale) in scales) control.Scale = scale;
                    hits.Clear(); hits.UnionWith(savedHits);
                }
            }
            refresh.Invoke(panel, null);
            foreach (string language in new[] { "zh", "en" })
            {
                var text = JsonUiTextProvider.CreatePredictorUi13(language);
                int act = captured.Run.Working.Position.Act - 1;
                var session = Fixture("ABYSSAL_BATHS");
                var presenter = new InRunMapNodePresenter(session, names, text);
                check(session.Nodes.Count == 2 && session.Nodes[(act, first)].Advanced &&
                    session.Nodes[(act, suffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                    "zero-public-consumption event continues to independent actual rewards in " + language);
                var lowValue = presenter.Detail((act, first))!;
                check(lowValue.Premises == null && lowValue.EditorActions.Count == 0 && lowValue.DiagnosticsLabel == null &&
                    lowValue.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices).Count() == 2,
                    "optional deck/health event exposes branches without an empty or duplicate premise panel in " + language);

                var doorsSession = Fixture("DOORS_OF_LIGHT_AND_DARK");
                string doorsCaller = JsonSerializer.Serialize(doorsSession.Snapshot!.Run.Working);
                var doorsPresenter = new InRunMapNodePresenter(doorsSession, names, text);
                var doorsDetail = doorsPresenter.Detail((act, first))!;
                var doorsNode = doorsSession.Nodes[(act, first)].Event!;
                var lightOption = doorsNode.EntryOptions.Single(option => option.Key == "LIGHT");
                var mainDoors = doorsDetail.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices).ToArray();
                var lightChoice = mainDoors.Single(choice => choice.Preview?.OptionKey == "LIGHT");
                check(mainDoors.Length == 2 && mainDoors.All(choice => choice.Enabled && !choice.Selected) &&
                    mainDoors.Any(choice => choice.Preview?.OptionKey == "DARK"),
                    "optional Light and Dark entrances are both directly available on the unselected main panel in " + language);
                check(doorsNode is { Advanced: true, BusinessChoiceRequired: false, AppliedActions: 0, AppliedResult: null } &&
                    doorsSession.GetPlan(act, first) == null && lightOption.ResultPreview!.ChangedCards.Any(change => change.Action == "Upgrade") &&
                    !doorsDetail.Information.Any(line => line.Contains(text.Get("predictor.map.prerequisite.node_continuation"), StringComparison.Ordinal)) &&
                    !doorsDetail.Sections.Any(section => section.StableId?.Contains("|event-outcome|", StringComparison.Ordinal) == true) &&
                    !lightChoice.Selected && PreviewRows(lightChoice.Preview!).Any(row => row.Action == OperationPreviewAction.UpgradeCard),
                    "unselected native Doors retains its exact optional Light preview without publishing its random upgrades as adopted results in " + language);
                details.ShowNode(doorsDetail with { IsCurrent = null }, new(new(120, 230), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                check(TopCards().Length == 0 && !details.HasRewardCardDock,
                    "unselected Doors has no resident random-target card faces in " + language);
                check(VisibleChoice(lightChoice.Id) is { Disabled: false } &&
                    VisibleChoice(mainDoors.Single(choice => choice.Preview?.OptionKey == "DARK").Id) is { Disabled: false },
                    "collapsed Doors premises do not hide either real branch entrance in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-doors-main-" + language + ".png"));
                check(doorsDetail.Premises == null && details.FindChild("PremisesExpansion", true, false) == null &&
                    TopCards().Length == 0 && VisibleChoice(lightChoice.Id) is { Disabled: false },
                    "unselected Doors has no premature Dark target inputs or empty premise entrance in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-doors-unselected-" + language + ".png"));
                details.CloseHover();
                check(doorsPresenter.ApplyChoice(doorsDetail.NodeId, lightChoice.Id) &&
                    doorsSession.GetPlan(act, first)!.Event!.Actions.Single().ExpectedOption?.Key == "LIGHT" &&
                    doorsSession.Nodes[(act, first)].Event!.AppliedResult!.ChangedCards.Any(change => change.Action == "Upgrade"),
                    "explicit native Light adoption retains its actual upgrade result in " + language);
                doorsPresenter = new(doorsSession, names, text);
                doorsDetail = doorsPresenter.Detail((act, first))!;
                check(doorsDetail.Sections.Single(section => section.StableId == doorsDetail.NodeId + "|event-applied-outcome")
                        .OutcomePreview is { } adoptedLight && PreviewRows(adoptedLight).Any(row => row.Action == OperationPreviewAction.UpgradeCard) &&
                    AllChoices(doorsDetail.Sections).Single(choice => choice.Preview?.OptionKey == "LIGHT").Selected &&
                    doorsDetail.Premises == null,
                    "recreated Doors presenter restores the saved branch and only its actual adopted upgrade result in " + language);
                var darkChoice = AllChoices(doorsDetail.Sections).Single(choice => choice.Preview?.OptionKey == "DARK");
                check(doorsPresenter.ApplyChoice(doorsDetail.NodeId, darkChoice.Id),
                    "Doors keeps the real optional Dark branch editable after Light adoption in " + language);
                doorsDetail = doorsPresenter.Detail((act, first))!;
                check(doorsDetail.Premises != null && !AllChoices(doorsDetail.Premises.Sections).Any(choice => choice.Preview?.OptionKey != null),
                    "adopted Dark exposes only its optional target controls in premises, without repeating branch choices in " + language);
                details.ShowNode(doorsDetail with { IsCurrent = null }, new(new(120, 230), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Layout();
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-doors-dark-premises-" + language + ".png"));
                details.CloseHover();
                var darkTarget = AllChoices(doorsDetail.Premises!.Sections).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                var removedCard = darkTarget.Object!.CardSnapshot!;
                check(doorsPresenter.ApplyChoice(doorsDetail.NodeId, darkTarget.Id) &&
                    doorsSession.GetPlan(act, first)!.Event!.Actions is { Length: 2 } darkActions &&
                    darkActions[0].ExpectedOption?.Key == "DARK" && darkActions[1].DeckCards.Single() ==
                        new PredictorPlannedCard(removedCard.Id, removedCard.Key, removedCard.FloorAdded),
                    "Doors Dark precision saves the actual chosen removal identity in " + language);
                doorsPresenter = new(doorsSession, names, text);
                doorsDetail = doorsPresenter.Detail((act, first))!;
                check(doorsDetail.Premises == null &&
                    AllChoices(doorsDetail.Sections).Any(choice => choice.Selected && choice.Object?.CardSnapshot?.Id == removedCard.Id) &&
                    doorsSession.Nodes[(act, suffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) &&
                    JsonSerializer.Serialize(doorsSession.Snapshot.Run.Working) == doorsCaller,
                    "Doors saved removal feedback and independent real rewards survive recreation without mutating the captured state in " + language);
                var adoptedDark = AllChoices(doorsDetail.Sections).Single(choice => choice.Preview?.OptionKey == "DARK");
                check(doorsPresenter.ApplyChoice(doorsDetail.NodeId, adoptedDark.Id) &&
                    doorsSession.GetPlan(act, first)!.Event!.Actions.IsEmpty &&
                    doorsPresenter.Detail((act, first)) is { Premises: null } cleared &&
                    AllChoices(cleared.Sections).Where(choice => choice.Preview?.OptionKey != null).All(choice => !choice.Selected),
                    "re-clicking the adopted branch clears its dependent targets and returns to an unselected forecast without requiring premises in " + language);
                foreach (string entry in new[] { "TABLET_OF_TRUTH", "SELF_HELP_BOOK", "AROMA_OF_CHAOS" })
                {
                    var deckSession = Fixture(entry);
                    string deckCaller = JsonSerializer.Serialize(deckSession.Snapshot!.Run.Working);
                    var deckDetail = new InRunMapNodePresenter(deckSession, names, text).Detail((act, first))!;
                    check(deckSession.GetPlan(act, first) == null && deckDetail.Premises == null &&
                        deckDetail.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices).Any() &&
                        deckDetail.Sections.Where(section => section.OutcomePreview != null)
                            .SelectMany(section => PreviewRows(section.OutcomePreview!)).All(row => row.Cards.IsEmpty) &&
                        deckSession.Nodes[(act, suffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) &&
                        JsonSerializer.Serialize(deckSession.Snapshot.Run.Working) == deckCaller,
                        "unselected " + entry + " exposes branches without irrelevant target inputs, while retaining real suffix rewards and caller state in " + language);
                }
                if (doorsOnly) continue;
                var battle = presenter.Detail((act, suffix))!;
                check(battle.Premises!.Numbers.Count != 0 && !battle.Information.Any(line => line.Contains(
                    text.Get("predictor.map.prerequisite.cause.EnemyCreationCount"), StringComparison.Ordinal)),
                    "optional Niche enemy count stays editable without blocking independent rewards in " + language);
                details.ShowNode(battle with { IsCurrent = null }, new(new(120, 230), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                check(details.FindChild("PremisesExpansion", true, false) is Button &&
                    details.FindChild("NodeSecondaryExpansion", true, false) == null,
                    "rendered forecast keeps premises and has no More entrance in " + language);
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Layout();
                check(Descendants(details).OfType<SpinBox>().Any() &&
                    !Descendants(details).OfType<Button>().Any(button => button.Text == text.Get("predictor.map.diagnostics.title")),
                    "expanded premise panel contains actual numeric inputs without diagnostics entry in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-premises-" + language + ".png"));
                details.CloseHover();

                // Labeled geometry-only fixtures reuse real native snapshots;
                // they do not claim these are generated reward opportunities.
                var cardObjects = battle.Sections.SelectMany(section => section.Objects.Concat(section.Choices
                    .Select(choice => choice.Object).OfType<InRunMapDetailObject>()))
                    .Where(item => item.Kind == GameContentKind.Card && item.CardSnapshot != null).ToArray();
                foreach (int[] counts in new[] { new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 4 }, new[] { 2, 3 }, new[] { 3, 3 } })
                {
                    var sections = counts.Select((count, index) => new InRunMapDetailSection("Layout fixture " + index, "",
                        Enumerable.Range(0, count).Select(item => cardObjects[item % cardObjects.Length]).ToArray(), [])
                        { StableId = "layout-fixture:" + index, LargeCardResults = true }).ToArray();
                    var fixture = battle with { NodeId = "layout-fixture", Title = "Layout fixture: " + counts.Sum() + " cards",
                        Subtitle = "", Information = [], Sections = sections, Premises = null, IsCurrent = null };
                    details.ShowNode(fixture, new(new(650, 500), new(56, 56)), screen.GetGlobalRect());
                    await Layout();
                    CheckSurfaces("card threshold/" + string.Join('+', counts) + "/" + language);
                    if (counts.Sum() == 5)
                        using (var image = root.GetViewport().GetTexture().GetImage())
                            image.SavePng(Path.Combine(output, "drawing-five-flat-" + language + ".png"));
                    details.CloseHover();
                }

                // This bounded display fixture uses the captured native catalog
                // prototype. It does not claim SpoilsMap was generated here.
                var spoilsCard = captured.Run.Context.Catalog!.CardDefinitions.Single(definition =>
                    definition.Prototype.Key.Entry == "SPOILS_MAP").Prototype;
                var spoilsObject = new InRunMapDetailObject(spoilsCard.Key, GameContentKind.Card)
                    { CardSnapshot = spoilsCard };
                var spoilsChoice = new InRunMapDetailChoice("display-fixture:spoils-map", text.Get("predictor.runtime.plan.take"), spoilsObject);
                var spoilsFixture = battle with { NodeId = "display-fixture:spoils-map", Title = "Display fixture: five ordinary cards + compact SpoilsMap",
                    Subtitle = "", Information = [], Premises = null, IsCurrent = null,
                    Sections = [new("Display fixture", "", [.. Enumerable.Repeat(cardObjects[0], 5), spoilsObject], [spoilsChoice])
                        { StableId = "display-fixture:spoils-map", LargeCardResults = true }] };
                details.ShowNode(spoilsFixture, new(new(650, 500), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                var spoilsButton = VisibleChoice(spoilsChoice.Id)!;
                check(TopCards().Length == 5 && TopCards().All(ordinary => ordinary.Key == cardObjects[0].Key) &&
                    !spoilsButton.Disabled && HasVisibleName(spoilsButton, spoilsCard.Key),
                    "labeled SpoilsMap display fixture keeps its compact name/action and excludes Quest from the five-card flat threshold in " + language);
                CheckSurfaces("SpoilsMap display fixture/" + language);
                var spoilsScroll = spoilsButton.GetParent().GetParent<ScrollContainer>();
                spoilsScroll.EnsureControlVisible(spoilsButton);
                await DiagnosticFrame(root);
                var spoilsVisible = spoilsScroll.GetGlobalRect().Intersection(spoilsButton.GetGlobalRect());
                check(spoilsVisible.Size.X > 0 && spoilsVisible.Size.Y > 0,
                    "scrolling the compact list makes the SpoilsMap action reachable inside its actual viewport in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-spoils-map-display-fixture-" + language + ".png"));
                details.CloseHover();

                session = Fixture("ABYSSAL_BATHS", extraCards: true);
                presenter = new(session, names, text);
                var multipleCards = presenter.Detail((act, suffix))!;
                check(session.Nodes[(act, suffix)].Rewards.Count(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) == 2,
                    "real Hunt fixture supplies two independent combat card groups in " + language);
                foreach (var size in new[] { new Vector2I(1024, 640), new Vector2I(800, 720) })
                {
                    root.GetWindow().Size = size;
                    await DiagnosticFrame(root);
                    details.ShowNode(multipleCards with { IsCurrent = null }, new(new(size.X * .64f, size.Y - 130), new(56, 56)), screen.GetGlobalRect());
                    await Layout();
                    CheckSurfaces("two card groups/" + size + "/" + language);
                    var dock = (ScrollContainer)DiagnosticField(details, "_rewardCardDock");
                    check(Descendants(dock).OfType<Control>().Count(control => control.HasMeta("reward_group_has_stable_identity")) == 2 && dock.ClipContents,
                        "both real opportunities remain in the clipped scrolling card dock in " + size + "/" + language);
                    using (var image = root.GetViewport().GetTexture().GetImage())
                        image.SavePng(Path.Combine(output, "drawing-two-groups-" + size.X + "-" + language + ".png"));
                    details.CloseHover();
                }
                root.GetWindow().Size = new(1680, 1080);
                await DiagnosticFrame(root);

                await CheckDrawingComplexGeometry(root, output, screen, details, multipleCards, lowValue, language,
                    () => signature() + "|" + JsonSerializer.Serialize(session.Plans) + "|" + JsonSerializer.Serialize(session.Forecast), check);
                var anchorSession = Fixture("SUNKEN_STATUE");
                var anchorPresenter = new InRunMapNodePresenter(anchorSession, names, text);
                await CheckDrawingSourceAnchoring(root, output, screen, details, anchorPresenter.Detail((act, first))!, language,
                    () => signature() + "|" + JsonSerializer.Serialize(anchorSession.Plans) + "|" + JsonSerializer.Serialize(anchorSession.Forecast), check);

                session = Fixture("ABYSSAL_BATHS", extraCards: true, rerolls: true);
                presenter = new(session, names, text);
                var rerollDetail = presenter.Detail((act, suffix))!;
                var rerollSections = rerollDetail.Sections.Where(section => section.LargeCardResults && section.StableId != null).ToArray();
                check(rerollSections.Length == 2 && session.Nodes[(act, suffix)].Rewards.Count(reward =>
                    reward.Kind == PredictorRewardKind.Card && reward.IsKnown && reward.Value!.CanReroll) == 2,
                    "actual Hunt and Driftwood supply two distinct editable reroll opportunities in " + language);
                string firstRerollId = rerollSections[0].StableId!, secondRerollId = rerollSections[1].StableId!;
                var rerollFirst = rerollSections[0].Choices.Single(choice => choice.Label == text.Get("predictor.runtime.alternative.REROLL"));
                await CheckRewardHover(rerollDetail, rerollFirst, "reroll/" + language);
                check(rerollFirst.PreviewFactory?.Invoke() is { } && presenter.ApplyChoice(rerollDetail.NodeId, rerollFirst.Id),
                    "the first actual Hunt opportunity can be previewed and rerolled in " + language);
                var firstRerollIntent = session.GetPlan(act, suffix)!.RewardChoices.Single();
                rerollDetail = presenter.Detail((act, suffix))!;
                var restoreFirst = rerollDetail.Sections.Single(section => section.StableId == firstRerollId).Choices
                    .Single(choice => choice.Selected && choice.Label == text.Get("predictor.runtime.alternative.REROLL"));
                await CheckRewardHover(rerollDetail, restoreFirst, "restore/" + language);
                var takeFirst = rerollDetail.Sections.Single(section => section.StableId == firstRerollId).Choices
                    .First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                check(takeFirst.PreviewFactory?.Invoke() is { } && presenter.ApplyChoice(rerollDetail.NodeId, takeFirst.Id),
                    "the first rerolled opportunity accepts one of its real replacement cards in " + language);
                var firstTakeIntent = session.GetPlan(act, suffix)!.RewardChoices.Single();
                rerollDetail = presenter.Detail((act, suffix))!;
                var rerollSecond = rerollDetail.Sections.Single(section => section.StableId == secondRerollId).Choices
                    .Single(choice => choice.Label == text.Get("predictor.runtime.alternative.REROLL"));
                check(rerollSecond.PreviewFactory?.Invoke() is { } && presenter.ApplyChoice(rerollDetail.NodeId, rerollSecond.Id) &&
                    session.GetPlan(act, suffix)!.RewardChoices is { Length: 2 } bothRerolls && bothRerolls.All(choice => choice.Reroll) &&
                    bothRerolls.Any(choice => JsonSerializer.Serialize(choice) == JsonSerializer.Serialize(firstTakeIntent)) &&
                    session.Nodes[(act, suffix)].Advanced && session.Nodes[(act, suffix)].Rewards.Count(reward =>
                        reward.Kind == PredictorRewardKind.Card && reward.IsKnown && reward.AppliedChoice?.Reroll == true &&
                        reward.Value?.HasRerolled == true) == 2,
                    "rerolling the second actual opportunity retains the first reroll and its taken replacement card in " + language);
                var secondRerollIntent = session.GetPlan(act, suffix)!.RewardChoices.Single(choice => choice.CardIndex == null);
                rerollDetail = presenter.Detail((act, suffix))!;
                var cancelFirst = rerollDetail.Sections.Single(section => section.StableId == firstRerollId).Choices
                    .Single(choice => choice.Selected && choice.Object?.Key == takeFirst.Object!.Key && choice.Label == text.Get("predictor.runtime.cancel"));
                var cancelFirstPreview = cancelFirst.PreviewFactory?.Invoke();
                bool? cancelFirstApplied = cancelFirstPreview is { OptionKey: "CANCEL_TAKE" }
                    ? presenter.ApplyChoice(rerollDetail.NodeId, cancelFirst.Id) : null;
                check(cancelFirstPreview is { OptionKey: "CANCEL_TAKE" } && cancelFirstApplied == true &&
                    session.GetPlan(act, suffix)!.RewardChoices is { Length: 2 } retainedRerolls &&
                    retainedRerolls.All(choice => choice.Reroll && choice.CardIndex == null && !choice.Skip) &&
                    retainedRerolls.Any(choice => JsonSerializer.Serialize(choice.ExpectedReward) == JsonSerializer.Serialize(firstRerollIntent.ExpectedReward)) &&
                    retainedRerolls.Any(choice => JsonSerializer.Serialize(choice) == JsonSerializer.Serialize(secondRerollIntent)) &&
                    session.Nodes[(act, first)].Advanced && session.Nodes[(act, suffix)].Advanced &&
                    session.Nodes[(act, suffix)].Rewards.Count(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown &&
                        reward.DefaultSkipped && reward.AppliedChoice?.Reroll == true && reward.Value?.HasRerolled == true) == 2,
                    "cancelling only the first take retains both original reroll identities and the known drawn suffix in " + language +
                    "; diagnostic=" + JsonSerializer.Serialize(new
                    {
                        Preview = cancelFirstPreview == null ? null : new
                        {
                            cancelFirstPreview.OptionKey, cancelFirstPreview.Notices, cancelFirstPreview.PendingReasons,
                            Faults = cancelFirstPreview.Faults.Select(fault => new { fault.Stage, Exception = fault.Exception.ToString() })
                        },
                        ApplyAttempted = cancelFirstApplied.HasValue, ApplyResult = cancelFirstApplied,
                        Plan = session.GetPlan(act, suffix),
                        OriginalFirstReroll = firstRerollIntent, FirstTaken = firstTakeIntent, SecondReroll = secondRerollIntent,
                        Nodes = new[] { first, suffix }.Select(point => new
                        {
                            Point = point, session.Nodes[(act, point)].Advanced,
                            Causes = session.Nodes[(act, point)].PendingCauses,
                            Rewards = session.Nodes[(act, point)].Rewards.Select(reward => new
                            {
                                reward.SourceIdentity, reward.Occurrence, reward.Kind, reward.IsKnown, reward.DefaultSkipped,
                                Status = reward.Value?.Status, HasRerolled = reward.Value?.HasRerolled, reward.AppliedChoice,
                                reward.PendingCauses
                            })
                        })
                    }));
                rerollDetail = presenter.Detail((act, suffix))!;
                details.ShowNode(rerollDetail with { IsCurrent = null }, new(new(100, 500), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                check(Descendants(details).OfType<Control>().Count(control => control.IsVisibleInTree() &&
                    control.HasMeta("reward_group_has_stable_identity")) == 2 && TopCards().Length == 6,
                    "both actual rerolled card groups remain displayed after cancelling one take in " + language);
                CheckSurfaces("two actual rerolls/" + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-two-rerolls-" + language + ".png"));
                details.CloseHover();

                foreach (bool left in new[] { false, true })
                {
                    var anchor = new Rect2(new Vector2(left ? 1500 : 100, 300), new Vector2(100, 180));
                    details.ShowNode(lowValue with { NodeId = "display-fixture:large-node:" + left,
                        Title = "Display fixture: large node without cards", Information = [], Sections = [], Premises = null, IsCurrent = null },
                        anchor, screen.GetGlobalRect());
                    await Layout();
                    var main = details.HoverGlobalRegion;
                    check(Math.Abs(main.Position.Y - anchor.Position.Y) <= 1 &&
                        Math.Abs((left ? main.End.X + 10 : main.Position.X - 10) - (left ? anchor.Position.X : anchor.End.X)) <= 1 &&
                        !details.HasRewardCardDock,
                        "a large card-free node keeps its Y and attaches the panel to the roomier " + (left ? "left" : "right") + " side in " + language +
                        "; main=" + main + "; anchor=" + anchor + "; surfaceMin=" +
                        ((Control)DiagnosticField(details, "_surface")).GetCombinedMinimumSize() +
                        "; rootMin=" + ((Control)DiagnosticField(details, "_hoverRoot")).GetCombinedMinimumSize() +
                        "; side=" + DiagnosticField(details, "_hoverLeft") + "; cards=" + details.HasRewardCardDock);
                    using (var image = root.GetViewport().GetTexture().GetImage())
                        image.SavePng(Path.Combine(output, "drawing-node-side-" + (left ? "left-" : "right-") + language + ".png"));
                    details.CloseHover();
                }
                var longItems = Enumerable.Repeat(cardObjects[0] with
                    { DisplayName = names.Resolve(cardObjects[0].Key, GameContentKind.Card) + " · display fixture reward" }, 3).ToArray();
                details.ShowNode(battle with { NodeId = "display-fixture:wide-reward", Title = "Display fixture: reward row grows main panel",
                    Information = [], Premises = null, IsCurrent = null,
                    Sections = [new("Display fixture", "", longItems, []) { LargeCardResults = true, StableId = "display-fixture:wide-reward" }] },
                    new(new(100, 500), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                var longRow = Descendants(details).OfType<Control>().Single(control => control.Name == "CardRewardRow");
                var longScroll = longRow.GetParent<ScrollContainer>();
                check(details.HoverGlobalRegion.Size.X > 460 && longRow.GetCombinedMinimumSize().X <= longScroll.Size.X + 1,
                    "a long comparable reward row first widens the main panel when its node side has room in " + language);
                CheckSurfaces("wide reward display fixture/" + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-wide-reward-display-fixture-" + language + ".png"));
                details.CloseHover();

                session = Fixture("ENDLESS_CONVEYOR");
                presenter = new(session, names, text);
                var conveyorDetail = presenter.Detail((act, first))!;
                InRunMapNodeEditorData ConveyorFields(InRunMapNodeDetailData data) => data.Sections.Select(section => section.EmbeddedEditor)
                    .OfType<InRunMapNodeEditorData>().Single(editor => editor.Title == text.Get("predictor.map.conveyor.title"));
                var conveyorFields = ConveyorFields(conveyorDetail);
                var slotsField = conveyorFields.Numbers.Single(field => field.Label == text.Get("predictor.map.event_potions.empty_count"));
                int suppliedSlots = slotsField.Value == 1 ? 0 : 1;
                var originalLostField = conveyorFields.Numbers.Single(field => field.Label == text.Get("predictor.map.conveyor.lost_hp"));
                long initialConveyorRevision = session.Revision;
                string initialConveyorForecast = JsonSerializer.Serialize(session.Forecast);
                check(conveyorFields.Numbers.Count == 2 && presenter.ApplyField(conveyorDetail.NodeId, slotsField.Id, new(Number: suppliedSlots)),
                    "Conveyor presents only empty slots/lost HP and edits the first atomic draft field in " + language);
                conveyorDetail = presenter.Detail((act, first))!;
                conveyorFields = ConveyorFields(conveyorDetail);
                var lostField = conveyorFields.Numbers.Single(field => field.Label == text.Get("predictor.map.conveyor.lost_hp"));
                check(!presenter.ApplyField(conveyorDetail.NodeId, originalLostField.Id, new(Number: 8)) &&
                    presenter.ApplyField(conveyorDetail.NodeId, lostField.Id, new(Number: 7)),
                    "superseded Conveyor field callbacks are rejected while the current lost-HP draft remains editable in " + language);
                conveyorDetail = presenter.Detail((act, first))!;
                conveyorFields = ConveyorFields(conveyorDetail);
                check(session.GetPlan(act, first) == null && session.Revision == initialConveyorRevision &&
                    JsonSerializer.Serialize(session.Forecast) == initialConveyorForecast &&
                    conveyorFields.Numbers.Single(field => field.Label == slotsField.Label).Value == suppliedSlots &&
                    conveyorFields.Numbers.Single(field => field.Label == lostField.Label).Value == 7,
                    "both Conveyor draft values survive projection refresh without prematurely changing forecast or plan in " + language);
                // A local draft belongs to this presenter, not to the currently
                // rendered node or its controls. Revisit it after a real view
                // close, another node, and a window resize before confirming.
                details.ShowNode(conveyorDetail with { IsCurrent = null }, new(new(100, 400), new(56, 56)), screen.GetGlobalRect());
                await Layout(); details.CloseHover();
                details.ShowNode(lowValue with { IsCurrent = null }, new(new(100, 300), new(56, 56)), screen.GetGlobalRect());
                await Layout(); details.CloseHover();
                root.GetWindow().Size = new(800, 720); await DiagnosticFrame(root);
                conveyorDetail = presenter.Detail((act, first))!;
                conveyorFields = ConveyorFields(conveyorDetail);
                details.ShowNode(conveyorDetail with { IsCurrent = null }, new(new(600, 420), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                var revisitedDraftNumbers = Descendants(details).OfType<SpinBox>().Where(spin => spin.IsVisibleInTree()).ToArray();
                check(revisitedDraftNumbers.Select(spin => spin.Value).Order().SequenceEqual(new[] { (double)suppliedSlots, 7d }.Order()) &&
                    session.GetPlan(act, first) == null && session.Revision == initialConveyorRevision &&
                    JsonSerializer.Serialize(session.Forecast) == initialConveyorForecast,
                    "closing, switching nodes and resizing retains both actual Conveyor draft controls without saving or recomputing in " + language);
                details.CloseHover(); root.GetWindow().Size = new(1680, 1080); await DiagnosticFrame(root);
                var confirmConveyor = conveyorFields.Sections.SelectMany(section => section.Choices)
                    .Single(choice => choice.Label == text.Get("predictor.runtime.confirm"));
                check(confirmConveyor.Enabled && presenter.ApplyChoice(conveyorDetail.NodeId, confirmConveyor.Id) &&
                    session.GetPlan(act, first)!.Event is { Potions: null, Actions.Length: 1 } atomic &&
                    atomic.Actions[0] is { Action: PredictorEventActionKind.ConveyorInputs, Conveyor: { } adoptedConveyor } &&
                    adoptedConveyor == new PredictorConveyorInput(suppliedSlots, 7) &&
                    session.Nodes[(act, first)].Event!.ConveyorInput == adoptedConveyor,
                    "one Conveyor confirmation atomically adopts both minimal premises and recomputes native options in " + language);
                conveyorDetail = presenter.Detail((act, first))!;
                conveyorFields = ConveyorFields(conveyorDetail);
                var appliedConveyor = conveyorFields.Sections.SelectMany(section => section.Choices)
                    .Single(choice => choice.Label == text.Get("predictor.runtime.confirm"));
                check(appliedConveyor.Selected && !appliedConveyor.Enabled && conveyorFields.Sections.Any(section =>
                    section.Text.Contains(text.Format("predictor.map.conveyor.applied", suppliedSlots, 7), StringComparison.Ordinal)),
                    "Conveyor confirmation shows the actual adopted values and a disabled selected confirmation in " + language);
                details.ShowNode(conveyorDetail with { IsCurrent = null }, new(new(100, 400), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                check(Descendants(details).OfType<SpinBox>().Count(spin => spin.IsVisibleInTree()) == 2,
                    "actual Conveyor controls contain exactly the two minimal numbers without duplicate current/max HP fields in " + language);
                details.CloseHover();
                long adoptedConveyorRevision = session.Revision;
                string adoptedConveyorOptions = JsonSerializer.Serialize(session.Nodes[(act, first)].Event!.Options);
                var editLost = conveyorFields.Numbers.Single(field => field.Label == lostField.Label);
                check(presenter.ApplyField(conveyorDetail.NodeId, editLost.Id, new(Number: 9)),
                    "adopted Conveyor premises can be revised as a local draft in " + language);
                conveyorDetail = presenter.Detail((act, first))!;
                conveyorFields = ConveyorFields(conveyorDetail);
                check(session.Revision == adoptedConveyorRevision && session.Nodes[(act, first)].Event!.ConveyorInput == new PredictorConveyorInput(suppliedSlots, 7) &&
                    JsonSerializer.Serialize(session.Nodes[(act, first)].Event!.Options) == adoptedConveyorOptions &&
                    conveyorFields.Sections.Any(section => section.Text.Contains(text.Get("predictor.map.conveyor.edited"), StringComparison.Ordinal)),
                    "dirty Conveyor edits retain adopted prediction and visibly require confirmation in " + language);
                var reconfirmConveyor = conveyorFields.Sections.SelectMany(section => section.Choices)
                    .Single(choice => choice.Label == text.Get("predictor.runtime.confirm"));
                check(!presenter.ApplyChoice(conveyorDetail.NodeId, appliedConveyor.Id) &&
                    presenter.ApplyChoice(conveyorDetail.NodeId, reconfirmConveyor.Id) &&
                    session.Nodes[(act, first)].Event!.ConveyorInput == new PredictorConveyorInput(suppliedSlots, 9) &&
                    session.GetPlan(act, first)!.Event!.Actions.Count(action => action.Action == PredictorEventActionKind.ConveyorInputs) == 1,
                    "reconfirmation replaces the single Conveyor premise, rejects stale confirmation and adopts the revised value in " + language);
                conveyorDetail = presenter.Detail((act, first))!;
                var chef = conveyorDetail.Sections.SelectMany(section => section.Choices)
                    .Single(choice => choice.Label == text.Get("predictor.runtime.option.OBSERVE_CHEF"));
                var chefPreview = chef.PreviewFactory?.Invoke() ?? chef.Preview!;
                check(chefPreview.Heading == chef.Label && chefPreview.Branches.SelectMany(branch => branch.Sections)
                    .SelectMany(section => section.Rows).Any(row => row.Action == OperationPreviewAction.UpgradeCard),
                    "actual Observe Chef preview owns its branch heading and real random upgrade result in " + language);
                details.ShowNode(conveyorDetail with { IsCurrent = null }, new(new(100, 400), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                VisibleChoice(chef.Id)!.EmitSignal(Control.SignalName.MouseEntered);
                await Layout();
                var chefOverlay = (NeowOperationOverlay)DiagnosticField(details, "_optionPreview");
                var chefHeading = (Label)chefOverlay.FindChild("Rt2MapOptionHeading", true, false)!;
                var chefLabels = Descendants(chefOverlay).OfType<Label>().Where(label => label.IsVisibleInTree() && label.Text.Length != 0).ToArray();
                check(chefHeading.IsVisibleInTree() && chefHeading.Text == chef.Label && chefHeading.GetIndex() == 0 &&
                    chefLabels.Count(label => label.Text == chef.Label) == 1 && chefLabels.All(label =>
                        chefHeading.GetGlobalRect().Position.Y <= label.GetGlobalRect().Position.Y + 1 &&
                        chefHeading.GetGlobalRect().Position.X <= label.GetGlobalRect().Position.X + 1),
                    "Observe Chef appears once at the upper left before the upgrade result in the actual branch hover in " + language);
                var chefTargets = chefPreview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                    .Where(row => row.Action == OperationPreviewAction.UpgradeCard).SelectMany(row => row.Cards).ToArray();
                check(chefTargets.Length != 0 && chefTargets.All(card => chefLabels.Any(label =>
                    label.Text.Contains(names.Resolve(card.Key, GameContentKind.Card), StringComparison.Ordinal))),
                    "the actual Observe Chef upgrade text includes every concrete changed card name in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-conveyor-heading-" + language + ".png"));
                details.CloseHover();

                var orobasSession = AncientFixture();
                var ancientPoint = new MapPosition(3, 0); var ancientSuffix = new MapPosition(3, 1);
                var orobasPresenter = new InRunMapNodePresenter(orobasSession, names, text);
                var orobasDetail = orobasPresenter.Detail((1, ancientPoint))!;
                string orobasCaller = JsonSerializer.Serialize(orobasSession.Snapshot!.Run.Working);
                var gifts = orobasDetail.Sections.Select(section => section.HeaderChoice).OfType<InRunMapDetailChoice>()
                    .Where(choice => choice.Object?.Kind == GameContentKind.Relic && choice.Enabled).ToArray();
                check(gifts.Length == 3 && orobasSession.Snapshot.Run.Working.Visits.IsEmpty &&
                    gifts.Select(choice => choice.Object!.Key.Entry).ToHashSet().SetEquals(["SEA_GLASS", "DRIFTWOOD", "ARCHAIC_TOOTH"]),
                    "controlled native-catalog Orobas fixture exposes all three real gifts before caller entry in " + language);
                var toothSection = orobasDetail.Sections.Single(section => section.HeaderChoice?.Object?.Key.Entry == "ARCHAIC_TOOTH");
                var seaSection = orobasDetail.Sections.Single(section => section.HeaderChoice?.Object?.Key.Entry == "SEA_GLASS");
                var toothOffer = orobasSession.Nodes[(1, ancientPoint)].Ancient!.Offers.Single(offer => offer.Value?.Relic.Entry == "ARCHAIC_TOOTH").Value!;
                var seaOffer = orobasSession.Nodes[(1, ancientPoint)].Ancient!.Offers.Single(offer => offer.Value?.Relic.Entry == "SEA_GLASS").Value!;
                var toothTarget = orobasSession.Nodes[(1, ancientPoint)].Cards.Single(card => card.IsKnown && card.Value.Id == toothOffer.StarterCardId).Value;
                check(toothSection.Text.Contains(text.Get("predictor.map.ancient_transform_target"), StringComparison.Ordinal) &&
                    toothSection.Text.Contains(names.Resolve(toothTarget.Key, GameContentKind.Card), StringComparison.Ordinal) &&
                    seaSection.Text.Contains(text.Get("predictor.map.ancient_card_character"), StringComparison.Ordinal) &&
                    seaSection.Text.Contains(names.Resolve(seaOffer.CharacterTarget!.Value, GameContentKind.Character), StringComparison.Ordinal) &&
                    new[] { toothSection, seaSection }.All(section => section.Objects.Count == 0 && !section.LargeCardResults && section.Choices.Count == 0),
                    "Orobas Tooth transformation target and Sea Glass character are captions owned by their actual gift headers, without standalone objects or top artwork in " + language);
                details.ShowNode(orobasDetail with { IsCurrent = null }, new(new(100, 350), new(100, 180)), screen.GetGlobalRect());
                await Layout();
                var toothButton = VisibleChoice(toothSection.HeaderChoice!.Id)!;
                var toothCaption = Descendants(toothButton.GetParent()).OfType<Label>().Single(label => label.Text == toothSection.Text);
                check(toothCaption.GetGlobalRect().Position.Y >= toothButton.GetGlobalRect().End.Y &&
                    !details.HasRewardCardDock && TopCards().Length == 0,
                    "the real Tooth caption is rendered beneath its gift button and no target card floats above the Orobas panel in " + language);
                var seaGift = gifts.Single(choice => choice.Object!.Key.Entry == "SEA_GLASS");
                var seaGiftButton = VisibleChoice(seaGift.Id)!;
                string seaGiftButtonName = seaGiftButton.Name;
                bool guiGiftApplied = false;
                void SelectGiftFromGui(string nodeId, string choiceId)
                {
                    guiGiftApplied = orobasPresenter.ApplyChoice(nodeId, choiceId);
                    if (guiGiftApplied) details.ShowNode(orobasPresenter.Detail((1, ancientPoint))! with { IsCurrent = null },
                        new(new(100, 350), new(100, 180)), screen.GetGlobalRect());
                }
                details.ChoiceRequested += SelectGiftFromGui;
                try
                {
                    await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var clickAt = seaGiftButton.GetGlobalRect().GetCenter();
                    var viewport = root.GetViewport();
                    viewport.PushInput(new InputEventMouseMotion { Position = clickAt, GlobalPosition = clickAt, ButtonMask = 0 }, true);
                    viewport.PushInput(new InputEventMouseButton { Position = clickAt, GlobalPosition = clickAt,
                        ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left }, true);
                    viewport.PushInput(new InputEventMouseButton { Position = clickAt, GlobalPosition = clickAt,
                        ButtonIndex = MouseButton.Left, Pressed = false, ButtonMask = 0 }, true);
                    await Layout();
                    check(guiGiftApplied && orobasSession.GetPlan(1, ancientPoint)!.Ancient!.ExpectedOffer.Relic.Entry == "SEA_GLASS" &&
                        orobasSession.Nodes[(1, ancientSuffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                        "a true viewport motion/press/release reaches the Orobas gift button and continues its real hypothesis in " + language +
                        "; button=" + seaGiftButtonName + "; click=" + clickAt + "; applied=" + guiGiftApplied);
                }
                finally { details.ChoiceRequested -= SelectGiftFromGui; }
                foreach (string gift in new[] { "DRIFTWOOD", "ARCHAIC_TOOTH", "SEA_GLASS" })
                {
                    orobasDetail = orobasPresenter.Detail((1, ancientPoint))!;
                    var chooseGift = orobasDetail.Sections.Select(section => section.HeaderChoice).OfType<InRunMapDetailChoice>()
                        .Single(choice => choice.Object?.Key.Entry == gift && choice.Enabled);
                    check(orobasPresenter.ApplyChoice(orobasDetail.NodeId, chooseGift.Id) &&
                        orobasSession.Nodes[(1, ancientPoint)].Advanced &&
                        orobasSession.Nodes[(1, ancientPoint)].Inventory!.Relics.Any(relic => relic.IsKnown && relic.Value.Key.Entry == gift) &&
                        orobasSession.Nodes[(1, ancientSuffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                        "preselecting actual Orobas " + gift + " acquires that gift and keeps following card rewards known in " + language);
                    orobasDetail = orobasPresenter.Detail((1, ancientPoint))!;
                    check(orobasDetail.Sections.Select(section => section.HeaderChoice).OfType<InRunMapDetailChoice>()
                        .Single(choice => choice.Object?.Key.Entry == gift).Selected,
                        "Orobas " + gift + " shows explicit selected feedback after recompute in " + language);
                    if (gift == "ARCHAIC_TOOTH")
                    {
                        var actualToothOriginal = orobasSession.Nodes[(1, ancientPoint)].Ancient!.Offers
                            .Single(offer => offer.Value?.Relic.Entry == "ARCHAIC_TOOTH").StarterCard;
                        var actualToothChange = orobasSession.Nodes[(1, ancientPoint)].Ancient!.Outcome!.ChangedCards
                            .Single(change => change.Action == "Transform" && change.Before == actualToothOriginal);
                        var actualToothSection = orobasDetail.Sections.Single(section => section.StableId == "ancient-change-Transform");
                        check(actualToothChange.After != null &&
                            actualToothSection.Text.Contains(names.Resolve(actualToothChange.Before.Key, GameContentKind.Card), StringComparison.Ordinal) &&
                            actualToothSection.Text.Contains(names.Resolve(actualToothChange.After.Key, GameContentKind.Card), StringComparison.Ordinal) &&
                            actualToothSection.Objects.Any(item => item.CardSnapshot == actualToothChange.Before) &&
                            actualToothSection.Objects.Any(item => item.CardSnapshot == actualToothChange.After),
                            "adopted Archaic Tooth explicitly groups the actual original card and transformation result in " + language);
                    }
                }
                var seaOptional = orobasSession.Nodes[(1, ancientPoint)].Ancient!.OptionalCards!;
                check(seaOptional.DefaultSkipped && seaOptional.SelectedIndices.IsEmpty && seaOptional.Choice.Cards.Length == 15 &&
                    seaOptional.Choice.MinimumCards == 0 && seaOptional.Choice.MaximumCards == 15 &&
                    orobasSession.GetPlan(1, ancientPoint)!.Ancient!.Children.IsEmpty,
                    "Sea Glass defaults its actual optional fifteen-card subset to empty without fabricating an authored child in " + language);
                int seaDeckBefore = orobasSession.Nodes[(1, ancientPoint)].Cards.Count(card => card.IsKnown);
                var seaCandidates = orobasDetail.Premises!.Sections.Where(section => section.Choices.Count(choice => choice.Object?.CardSnapshot != null) == 15).Single();
                var backfill = seaCandidates.Choices.First(choice => choice.Object?.CardSnapshot != null && choice.Enabled);
                check(orobasPresenter.ApplyChoice(orobasDetail.NodeId, backfill.Id) &&
                    orobasSession.GetPlan(1, ancientPoint)!.Ancient!.Children.IsEmpty,
                    "selecting a retained Sea Glass card edits its subset draft before confirmation in " + language);
                orobasDetail = orobasPresenter.Detail((1, ancientPoint))!;
                seaCandidates = orobasDetail.Premises!.Sections.Single(section => section.Choices.Count(choice => choice.Object?.CardSnapshot != null) == 15);
                var confirmSea = seaCandidates.Choices.Single(choice => choice.Label == text.Get("predictor.runtime.confirm"));
                check(confirmSea.Enabled && orobasPresenter.ApplyChoice(orobasDetail.NodeId, confirmSea.Id) &&
                    orobasSession.GetPlan(1, ancientPoint)!.Ancient!.Children.Single() is
                        { Action: PredictorObtainChildAction.ChooseManyCards, CardIndices.Length: 1, ExpectedCards.Length: 15 } seaChild &&
                    seaChild.CardIndices[0] == 0 && seaChild.ExpectedCards[0].Key == backfill.Object!.Key &&
                    orobasSession.Nodes[(1, ancientPoint)].Ancient!.OptionalCards is { DefaultSkipped: false, SelectedIndices.Length: 1 } &&
                    orobasSession.Nodes[(1, ancientPoint)].Cards.Count(card => card.IsKnown) == seaDeckBefore + 1 &&
                    orobasSession.Nodes[(1, ancientSuffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                    "confirming Sea Glass backfill takes exactly the authored real card and preserves the known suffix in " + language);
                check(JsonSerializer.Serialize(orobasSession.Snapshot.Run.Working) == orobasCaller &&
                    !orobasPresenter.ApplyChoice(orobasDetail.NodeId, confirmSea.Id),
                    "Orobas UI hypotheses preserve caller state and reject superseded child callbacks in " + language);
                orobasDetail = orobasPresenter.Detail((1, ancientPoint))!;
                details.ShowNode(orobasDetail with { IsCurrent = null }, new(new(100, 350), new(100, 180)), screen.GetGlobalRect());
                await Layout();
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-orobas-preentry-" + language + ".png"));
                details.CloseHover();

                var leafySession = AncientFixture("LEAFY_POULTICE");
                var leafyPresenter = new InRunMapNodePresenter(leafySession, names, text);
                var leafyDetail = leafyPresenter.Detail((0, ancientPoint))!;
                string leafyCaller = JsonSerializer.Serialize(leafySession.Snapshot!.Run.Working);
                var chooseLeafy = leafyDetail.Sections.Select(section => section.HeaderChoice).OfType<InRunMapDetailChoice>()
                    .Single(choice => choice.Object?.Key.Entry == "LEAFY_POULTICE");
                check(chooseLeafy.Enabled && leafyPresenter.ApplyChoice(leafyDetail.NodeId, chooseLeafy.Id) &&
                    leafySession.Nodes[(0, ancientPoint)].Advanced &&
                    !leafySession.Nodes[(0, ancientPoint)].Ancient!.RequiredChoices.Any(child => child.Phase == PredictorPhase.ObtainHealth) &&
                    leafySession.GetPlan(0, ancientPoint)!.Ancient!.Children.IsEmpty,
                    "actual Live Leafy Poultice continues independent effects without a forced health child or invented health input in " + language);
                var leafyNode = leafySession.Nodes[(0, ancientPoint)];
                var leafyEffect = leafyNode.ObtainHealthEffects.Single(effect => effect.Source.Entry == "LEAFY_POULTICE");
                var leafyRelic = leafyNode.Inventory!.Relics.Single(relic => relic.IsKnown && relic.Value.Key.Entry == "LEAFY_POULTICE").Value;
                check(leafyEffect.Kind == PredictorObtainHealthEffectKind.LoseMaxHp && leafyEffect.Amount == 12 &&
                    leafyEffect.Address.SourceId == leafyRelic.Id && !leafyEffect.PendingCauses.IsEmpty,
                    "Leafy health display retains its actual receipt address, twelve maximum-HP loss and unsettled settlement causes in " + language);
                var leafyOriginal = leafySession.Snapshot.Run.Working;
                var leafyBasics = leafyOriginal.Deck.Select(leafyOriginal.Card).Where(card =>
                    leafySession.Snapshot.Run.Context.Catalog!.CardDefinitions.Any(definition =>
                        definition.Prototype.Key == card.Key && definition.Rarity == "Basic")).ToArray();
                bool LeafyFamily(PredictorCard card, bool strike) => leafySession.Snapshot.Run.Context.Catalog!.CardDefinitions.Any(definition =>
                    definition.Prototype.Key == card.Key && (strike ? definition.IsStrike : definition.IsDefend));
                var leafyTargets = new[] { leafyBasics.FirstOrDefault(card => LeafyFamily(card, true))?.Id,
                    leafyBasics.FirstOrDefault(card => LeafyFamily(card, false))?.Id }.OfType<long>().ToHashSet();
                check(leafyTargets.Count != 0 && leafyNode.Cards.All(card => card.IsKnown && !leafyTargets.Contains(card.Value.Id)) &&
                    leafyOriginal.Deck.Where(id => !leafyTargets.Contains(id)).All(id =>
                        leafyNode.Cards.Any(card => card.IsKnown && card.Value == leafyOriginal.Card(id))) &&
                    leafyNode.Cards.Length == leafyOriginal.Deck.Length &&
                    leafySession.Nodes[(0, ancientSuffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) &&
                    JsonSerializer.Serialize(leafySession.Snapshot.Run.Working) == leafyCaller,
                    "Leafy executes real independent starter transformations and keeps the suffix known while preserving caller health/state in " + language);
                leafyDetail = leafyPresenter.Detail((0, ancientPoint))!;
                string leafyHealthText = text.Format("predictor.map.obtain_health.lose_max_hp", 12);
                string leafyUnsettledText = text.Get("predictor.map.obtain_health.unsettled");
                check(leafyDetail.Sections.Any(section => section.Text.Contains(leafyHealthText, StringComparison.Ordinal)) &&
                    leafyDetail.Sections.All(section => !section.Text.Contains(leafyUnsettledText, StringComparison.Ordinal)),
                    "Live Leafy main information shows the real maximum-HP loss without an unsettled HP notice in " + language);
                details.ShowNode(leafyDetail with { IsCurrent = null }, new(new(100, 350), new(100, 180)), screen.GetGlobalRect());
                await Layout();
                check(!Descendants(details).OfType<SpinBox>().Any(spin => spin.IsVisibleInTree()) &&
                    Descendants(details).OfType<Label>().Any(label => label.IsVisibleInTree() &&
                        label.Text.Contains(leafyHealthText, StringComparison.Ordinal)) &&
                    !Descendants(details).OfType<Label>().Any(label => label.IsVisibleInTree() &&
                        label.Text.Contains(leafyUnsettledText, StringComparison.Ordinal)),
                    "the actual collapsed-premises Leafy panel shows the health effect without forced current/max HP widgets in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-ancient-health-effect-" + language + ".png"));
                details.CloseHover();

                var tabletSession = AncientFixture("HEFTY_TABLET");
                var tabletPresenter = new InRunMapNodePresenter(tabletSession, names, text);
                var tabletDetail = tabletPresenter.Detail((0, ancientPoint))!;
                string tabletCaller = JsonSerializer.Serialize(tabletSession.Snapshot!.Run.Working);
                int originalInjuries = tabletSession.Snapshot.Run.Working.Deck.Count(id => tabletSession.Snapshot.Run.Working.Card(id).Key.Entry == "INJURY");
                var chooseTablet = tabletDetail.Sections.Select(section => section.HeaderChoice).OfType<InRunMapDetailChoice>()
                    .Single(choice => choice.Object?.Key.Entry == "HEFTY_TABLET");
                check(tabletPresenter.ApplyChoice(tabletDetail.NodeId, chooseTablet.Id) &&
                    tabletSession.Nodes[(0, ancientPoint)].Advanced && tabletSession.GetPlan(0, ancientPoint)!.Ancient!.Children.IsEmpty &&
                    tabletSession.Nodes[(0, ancientPoint)].Ancient!.OptionalCards is
                        { DefaultSkipped: true, SelectedIndices.IsEmpty: true, Choice.Phase: PredictorPhase.ObtainChooseCard,
                            Choice.MinimumCards: 0, Choice.MaximumCards: 1 } &&
                    tabletSession.Nodes[(0, ancientPoint)].Cards.Count(card => card.IsKnown && card.Value.Key.Entry == "INJURY") == originalInjuries + 1 &&
                    tabletSession.Nodes[(0, ancientSuffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                    "actual Hefty Tablet defaults its optional single card to null, applies mandatory Injury once and continues the suffix in " + language);
                tabletDetail = tabletPresenter.Detail((0, ancientPoint))!;
                var tabletOptions = tabletSession.Nodes[(0, ancientPoint)].Ancient!.OptionalCards!.Choice.Cards;
                var tabletCards = tabletDetail.Premises!.Sections.Single(section =>
                    section.Choices.Count(choice => choice.Object?.CardSnapshot != null) == tabletOptions.Length);
                var takeTablet = tabletCards.Choices.First(choice => choice.Object?.CardSnapshot != null && choice.Enabled);
                details.ShowNode(tabletDetail with { IsCurrent = null }, new(new(100, 350), new(100, 180)), screen.GetGlobalRect());
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Layout();
                check(VisibleChoice(takeTablet.Id) is { Disabled: false },
                    "the retained Hefty Tablet premise actually renders an actionable compact single-card control in " + language);
                details.CloseHover();
                check(!tabletCards.Choices.Any(choice => choice.Label == text.Get("predictor.runtime.plan.skip")) &&
                    tabletPresenter.ApplyChoice(tabletDetail.NodeId, takeTablet.Id) &&
                    tabletSession.GetPlan(0, ancientPoint)!.Ancient!.Children.Single() is
                        { Action: PredictorObtainChildAction.ChooseCard, ExpectedSource.Entry: "HEFTY_TABLET", CardIndices.Length: 1 } takenTablet &&
                    takenTablet.CardIndices[0] == 0 && takenTablet.ExpectedCards.SequenceEqual(tabletOptions) &&
                    tabletSession.Nodes[(0, ancientPoint)].Ancient!.OptionalCards!.SelectedIndices.SequenceEqual([0]) &&
                    tabletSession.Nodes[(0, ancientPoint)].Cards.Count(card => card.IsKnown && card.Value.Key.Entry == "INJURY") == originalInjuries + 1,
                    "the retained Hefty Tablet single-card action takes its real candidate without a Skip button or a second Injury in " + language);
                tabletDetail = tabletPresenter.Detail((0, ancientPoint))!;
                tabletCards = tabletDetail.Premises!.Sections.Single(section =>
                    section.Choices.Count(choice => choice.Object?.CardSnapshot != null) == tabletOptions.Length);
                var cancelTablet = tabletCards.Choices.Single(choice => choice.Selected && choice.Label == text.Get("predictor.runtime.cancel"));
                check(tabletPresenter.ApplyChoice(tabletDetail.NodeId, cancelTablet.Id) &&
                    tabletSession.GetPlan(0, ancientPoint)!.Ancient!.Children.Single() is
                        { Action: PredictorObtainChildAction.ChooseCard, ExpectedSource.Entry: "HEFTY_TABLET", CardIndices.IsEmpty: true } emptyTablet &&
                    emptyTablet.ExpectedCards.SequenceEqual(tabletOptions) &&
                    tabletSession.Nodes[(0, ancientPoint)].Ancient!.OptionalCards!.SelectedIndices.IsEmpty &&
                    tabletSession.Nodes[(0, ancientPoint)].Cards.Count(card => card.IsKnown && card.Value.Key.Entry == "INJURY") == originalInjuries + 1 &&
                    tabletSession.Nodes[(0, ancientSuffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) &&
                    JsonSerializer.Serialize(tabletSession.Snapshot.Run.Working) == tabletCaller,
                    "cancelling Hefty Tablet authors the real null single-card child, keeps mandatory Injury singular and preserves suffix/caller in " + language);

                // Controlled pre-entry geometry, actual PAEL generator and offer.
                // Storage identities are intentionally unspecified until the real
                // retained receipt picker below is confirmed.
                var paelSession = AncientFixture(pael: true);
                var paelPresenter = new InRunMapNodePresenter(paelSession, names, text);
                var paelDetail = paelPresenter.Detail((1, ancientPoint))!;
                string paelCaller = JsonSerializer.Serialize(paelSession.Snapshot!.Run.Working);
                var choosePaelTooth = paelDetail.Sections.Select(section => section.HeaderChoice).OfType<InRunMapDetailChoice>()
                    .Single(choice => choice.Object?.Key.Entry == "PAELS_TOOTH");
                check(choosePaelTooth.Enabled && paelPresenter.ApplyChoice(paelDetail.NodeId, choosePaelTooth.Id) &&
                    paelSession.Nodes[(1, ancientPoint)].Advanced && paelSession.Nodes[(1, ancientSuffix)].Advanced &&
                    paelSession.Nodes[(1, ancientSuffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                    "the actual pre-entry Pael Tooth gift continues the Ancient and independent card reward without five invented storage identities in " + language);
                var paelNode = paelSession.Nodes[(1, ancientPoint)];
                var paelStorage = paelNode.PaelsToothStorage.Single();
                var paelRelic = paelNode.Inventory!.Relics.Single(relic => relic.Value.Key.Entry == "PAELS_TOOTH");
                var paelReceipt = paelNode.OptionalObtainInputs.Single(input => input.Choice.Source.Entry == "PAELS_TOOTH");
                string paelParent = JsonSerializer.Serialize(paelSession.GetPlan(1, ancientPoint)!.Ancient);
                check(paelStorage is { InitialCount: 5, RemainingCount: 5 } && !paelStorage.PendingCauses.IsEmpty &&
                    paelStorage.SourceId == paelRelic.Value.Id && paelStorage.SourceNodeId == paelNode.Position.NodeId &&
                    JsonSerializer.Serialize(paelStorage.Address) == JsonSerializer.Serialize(paelReceipt.Address) &&
                    !paelRelic.StoredCardsKnown && paelRelic.Value.StoredCards.IsEmpty &&
                    paelSession.GetPlan(1, ancientPoint) is { ObtainInputs.IsEmpty: true, Ancient.Children.IsEmpty: true } &&
                    paelReceipt.Choice is { Phase: PredictorPhase.ObtainDeckCards, MinimumCards: 5, MaximumCards: 5 },
                    "Pael count-only knowledge retains the real source/receipt and eligible five-card count without publishing an invented empty storage payload or authored targets in " + language);
                var paelAfterDefault = paelSession.Nodes[(1, ancientSuffix)].Inventory!.Relics.Single(relic => relic.Value.Key.Entry == "PAELS_TOOTH");
                check(!paelAfterDefault.StoredCardsKnown && paelAfterDefault.Value.StoredCards.IsEmpty &&
                    paelAfterDefault.PaelsToothStorage is { InitialCount: 5, RemainingCount: 4 } &&
                    paelAfterDefault.PaelsToothStorage.SourceId == paelStorage.SourceId,
                    "the real combat return consumes one proven stored count while its identity remains unresolved in " + language);
                paelDetail = paelPresenter.Detail((1, ancientPoint))!;
                var paelCandidates = paelReceipt.Choice.DeckCards.Where(card => card.IsKnown).Select(card => card.Value).ToArray();
                InRunMapDetailSection PaelPicker(InRunMapNodeDetailData data) => data.Premises!.Sections.Single(section =>
                    section.Title == text.Format("predictor.runtime.select_cards", 5, 5) &&
                    section.Choices.Count(choice => choice.Object?.CardSnapshot != null) == paelCandidates.Length);
                var paelPicker = PaelPicker(paelDetail);
                check(paelCandidates.Length >= 5 && !paelPicker.LargeCardResults &&
                    !paelPicker.Choices.Single(choice => choice.Label == text.Get("predictor.runtime.confirm")).Enabled,
                    "Pael retains its real compact precise-five picker in premises without treating incomplete selection as an adopted storage input in " + language);
                string paelPendingCaption = text.Format("predictor.map.paels_tooth.storage_pending", 5);
                check(paelDetail.Sections.Any(section => section.Text.Contains(paelPendingCaption, StringComparison.Ordinal)) &&
                    !paelDetail.Sections.Any(section => section.Choices.Any(choice => choice.Object?.CardSnapshot != null)),
                    "the Pael main panel explains the proven five-card count and unresolved identities without requiring precise storage targets in " + language);
                details.ShowNode(paelDetail with { IsCurrent = null }, new(new(100, 350), new(100, 180)), screen.GetGlobalRect());
                await Layout();
                check(!details.HasRewardCardDock && TopCards().Length == 0 &&
                    Descendants(details).OfType<Label>().Any(label => label.IsVisibleInTree() &&
                        label.Text.Contains(paelPendingCaption, StringComparison.Ordinal)) &&
                    !paelPicker.Choices.Where(choice => choice.Object?.CardSnapshot != null).Any(choice => VisibleChoice(choice.Id) != null),
                    "the collapsed-premises Pael panel visibly explains pending storage without mandatory target controls or top target artwork in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-pael-storage-pending-" + language + ".png"));
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Layout();
                check(VisibleChoice(paelPicker.Choices.First(choice => choice.Object?.CardSnapshot != null).Id) is { Disabled: false },
                    "the Pael precise-storage premise has an actual enabled compact target control in " + language);
                details.CloseHover();
                string paelDefaultForecast = JsonSerializer.Serialize(paelSession.Forecast);
                long paelDefaultRevision = paelSession.Revision;
                string stalePaelTarget = paelPicker.Choices.First(choice => choice.Object?.CardSnapshot != null).Id;
                var paelChosen = paelCandidates.Take(5).ToArray();
                foreach (var paelCard in paelChosen)
                {
                    paelDetail = paelPresenter.Detail((1, ancientPoint))!;
                    var paelTarget = PaelPicker(paelDetail).Choices.Single(choice => choice.Object?.CardSnapshot?.Id == paelCard.Id);
                    check(paelTarget.Enabled && paelPresenter.ApplyChoice(paelDetail.NodeId, paelTarget.Id),
                        "Pael precise-storage draft accepts actual eligible instance " + paelCard.Id + " in " + language);
                }
                paelDetail = paelPresenter.Detail((1, ancientPoint))!;
                var confirmPael = PaelPicker(paelDetail).Choices.Single(choice => choice.Label == text.Get("predictor.runtime.confirm"));
                check(paelSession.Revision == paelDefaultRevision && paelSession.GetPlan(1, ancientPoint)!.ObtainInputs.IsEmpty &&
                    JsonSerializer.Serialize(paelSession.Forecast) == paelDefaultForecast && confirmPael.Enabled &&
                    !paelPresenter.ApplyChoice(paelDetail.NodeId, stalePaelTarget),
                    "Pael target edits stay in the draft until exactly five are confirmed and superseded callbacks cannot change them in " + language);
                check(paelPresenter.ApplyChoice(paelDetail.NodeId, confirmPael.Id) &&
                    paelSession.GetPlan(1, ancientPoint)!.ObtainInputs.Single() is { Input.Action: PredictorObtainChildAction.ChooseDeckCards,
                        Input.ExpectedSource.Entry: "PAELS_TOOTH", Input.DeckCards.Length: 5 } exactPael &&
                    exactPael.Input.DeckCards.SequenceEqual(paelChosen.Select(card => new PredictorPlannedCard(card.Id, card.Key, card.FloorAdded))) &&
                    JsonSerializer.Serialize(exactPael.Address) == JsonSerializer.Serialize(paelReceipt.Address) &&
                    JsonSerializer.Serialize(paelSession.GetPlan(1, ancientPoint)!.Ancient) == paelParent,
                    "confirming Pael writes exactly five real targets at the original optional receipt while preserving the actual Ancient parent in " + language);
                var paelStored = paelSession.Nodes[(1, ancientPoint)].Inventory!.Relics.Single(relic => relic.Value.Key.Entry == "PAELS_TOOTH");
                var paelReturned = paelSession.Nodes[(1, ancientSuffix)].Inventory!.Relics.Single(relic => relic.Value.Key.Entry == "PAELS_TOOTH");
                var returnedPaelCard = paelStored.Value.StoredCards.Where(card =>
                    !paelReturned.Value.StoredCards.Any(remaining => remaining.Id == card.Id)).ToArray();
                check(paelStored.StoredCardsKnown && paelStored.Value.Id == paelStorage.SourceId && paelStored.Value.StoredCards.Length == 5 &&
                    paelStored.Value.StoredCards.Select(card => card.Id).ToHashSet().SetEquals(paelChosen.Select(card => card.Id)) &&
                    paelReturned.StoredCardsKnown && paelReturned.Value.Id == paelStorage.SourceId &&
                    paelReturned.Value.StoredCards.Length == 4 && returnedPaelCard.Length == 1 &&
                    paelSession.Nodes[(1, ancientSuffix)].Cards.Any(card => card.IsKnown && card.Value.Key == returnedPaelCard[0].Key &&
                        card.Value.UpgradeLevel == returnedPaelCard[0].UpgradeLevel + 1) &&
                    paelSession.Nodes[(1, ancientSuffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                    "Pael exact backfill really stores five and upgrades/returns one after combat, leaving four and independent rewards known in " + language);
                paelDetail = paelPresenter.Detail((1, ancientPoint))!;
                long[] PaelSelected(InRunMapNodeDetailData data) => AllChoices(data.Premises!.Sections)
                    .Where(choice => choice.Selected && choice.Object?.CardSnapshot is { } selectedPaelCard &&
                        paelCandidates.Any(candidate => candidate.Id == selectedPaelCard.Id))
                    .Select(choice => choice.Object!.CardSnapshot!.Id).Order().ToArray();
                var paelRecreated = new InRunMapNodePresenter(paelSession, names, text).Detail((1, ancientPoint))!;
                check(PaelSelected(paelDetail).SequenceEqual(paelChosen.Select(card => card.Id).Order()) &&
                    PaelSelected(paelRecreated).SequenceEqual(paelChosen.Select(card => card.Id).Order()),
                    "all five exact Pael targets show selected feedback from saved intent in both current and recreated presenters in " + language);
                string paelSavedIntent = JsonSerializer.Serialize(paelSession.GetPlan(1, ancientPoint));
                string paelSavedForecast = JsonSerializer.Serialize(paelSession.Forecast);
                var editPaelTarget = PaelPicker(paelDetail).Choices.Single(choice => choice.Object?.CardSnapshot?.Id == paelChosen[0].Id);
                check(paelPresenter.ApplyChoice(paelDetail.NodeId, editPaelTarget.Id) &&
                    JsonSerializer.Serialize(paelSession.GetPlan(1, ancientPoint)) == paelSavedIntent &&
                    JsonSerializer.Serialize(paelSession.Forecast) == paelSavedForecast,
                    "editing an adopted Pael target remains an unconfirmed local draft and preserves its saved receipt and results in " + language);
                paelDetail = paelPresenter.Detail((1, ancientPoint))!;
                check(PaelSelected(paelDetail).Length == 4 &&
                    !PaelPicker(paelDetail).Choices.Single(choice => choice.Label == text.Get("predictor.runtime.confirm")).Enabled &&
                    PaelSelected(new InRunMapNodePresenter(paelSession, names, text).Detail((1, ancientPoint))!).Length == 5,
                    "a four-target dirty Pael draft stays editable without replacing the saved five shown by a recreated presenter in " + language);
                var clearPael = paelDetail.Premises!.Sections.SelectMany(section => section.Choices)
                    .Single(choice => choice.Label == text.Get("predictor.map.clear_optional_input"));
                check(!paelPresenter.ApplyChoice(paelDetail.NodeId, confirmPael.Id) && paelPresenter.ApplyChoice(paelDetail.NodeId, clearPael.Id) &&
                    paelSession.GetPlan(1, ancientPoint)!.ObtainInputs.IsEmpty &&
                    JsonSerializer.Serialize(paelSession.GetPlan(1, ancientPoint)!.Ancient) == paelParent &&
                    paelSession.Nodes[(1, ancientPoint)].PaelsToothStorage.Single() is { InitialCount: 5, RemainingCount: 5 } &&
                    paelSession.Nodes[(1, ancientPoint)].Inventory!.Relics.Single(relic => relic.Value.Key.Entry == "PAELS_TOOTH").Value.StoredCards.IsEmpty &&
                    paelSession.Nodes[(1, ancientSuffix)].Advanced &&
                    paelSession.Nodes[(1, ancientSuffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) &&
                    JsonSerializer.Serialize(paelSession.Snapshot.Run.Working) == paelCaller,
                    "clearing Pael precise storage restores targetless continuation, keeps parent/source and independent rewards, rejects stale confirmation and preserves caller state in " + language);
                check(PaelSelected(paelPresenter.Detail((1, ancientPoint))!).Length == 0 &&
                    PaelSelected(new InRunMapNodePresenter(paelSession, names, text).Detail((1, ancientPoint))!).Length == 0 &&
                    !PaelPicker(paelPresenter.Detail((1, ancientPoint))!).Choices.Single(choice => choice.Label == text.Get("predictor.runtime.confirm")).Enabled,
                    "clearing Pael stored-target intent also retires selected feedback and disables empty confirmation in current and recreated presenters in " + language);

                var arcaneSession = AncientFixture("ARCANE_SCROLL");
                var arcanePresenter = new InRunMapNodePresenter(arcaneSession, names, text);
                var arcaneDetail = arcanePresenter.Detail((0, ancientPoint))!;
                string arcaneCaller = JsonSerializer.Serialize(arcaneSession.Snapshot!.Run.Working);
                var chooseArcane = AllChoices(arcaneDetail.Sections).Single(choice => choice.Object?.Key.Entry == "ARCANE_SCROLL");
                check(arcanePresenter.ApplyChoice(arcaneDetail.NodeId, chooseArcane.Id) && arcaneSession.Nodes[(0, ancientPoint)].Advanced,
                    "the actual Arcane Scroll gift is adopted before direct generated results are displayed in " + language);
                var arcaneNode = arcaneSession.Nodes[(0, ancientPoint)];
                var arcaneOutcome = arcaneNode.Ancient!.Outcome!;
                arcaneDetail = arcanePresenter.Detail((0, ancientPoint))!;
                var arcaneDisplay = arcaneDetail.Sections.Single(section => section.StableId == "ancient-acquisition-result");
                check(arcaneOutcome.Source.Entry == "ARCANE_SCROLL" && !arcaneOutcome.AddedCards.IsEmpty &&
                    arcaneDisplay.LargeCardResults &&
                    arcaneOutcome.AddedCards.All(added => arcaneNode.Cards.Any(item => item.IsKnown && item.Value with { Id = 0 } == added) &&
                        arcaneDisplay.Objects.Count(shown => shown.CardSnapshot == added) == 1),
                    "Arcane Scroll's real known added card is projected once as the adopted gift's direct outcome in " + language);
                details.ShowNode(arcaneDetail with { IsCurrent = null }, new(new(100, 350), new(100, 180)), screen.GetGlobalRect());
                await Layout();
                check(arcaneOutcome.AddedCards.All(added => TopCards().Any(shown => shown.Id == added.Id && shown.Key == added.Key)) &&
                    arcaneSession.Nodes[(0, ancientSuffix)].Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card) &&
                    JsonSerializer.Serialize(arcaneSession.Snapshot.Run.Working) == arcaneCaller,
                    "the known Arcane Scroll result reaches the actual main card surface while suffix and caller state stay valid in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-arcane-scroll-result-" + language + ".png"));
                details.CloseHover();

                var boxesSession = AncientFixture("SCROLL_BOXES");
                var boxesPresenter = new InRunMapNodePresenter(boxesSession, names, text);
                var boxesDetail = boxesPresenter.Detail((0, ancientPoint))!;
                string boxesCaller = JsonSerializer.Serialize(boxesSession.Snapshot!.Run.Working);
                var chooseBoxes = AllChoices(boxesDetail.Sections).Single(choice => choice.Object?.Key.Entry == "SCROLL_BOXES");
                check(boxesPresenter.ApplyChoice(boxesDetail.NodeId, chooseBoxes.Id) && boxesSession.Nodes[(0, ancientPoint)].Advanced &&
                    boxesSession.Nodes[(0, ancientSuffix)].Advanced &&
                    boxesSession.Nodes[(0, ancientSuffix)].Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card),
                    "actual Scroll Boxes permits independent continuation before the optional bundle identity is chosen in " + language);
                var boxesDefault = boxesSession.Nodes[(0, ancientPoint)].Ancient!.BundleChoice!;
                boxesDetail = boxesPresenter.Detail((0, ancientPoint))!;
                string boxesParent = JsonSerializer.Serialize(boxesSession.GetPlan(0, ancientPoint)!.Ancient!.ExpectedOffer);
                InRunMapDetailSection BoxSection(InRunMapNodeDetailData data, int index) => data.Sections.Single(section => section.StableId == "ancient-bundle-" + index);
                check(boxesDefault.SelectedBundle == null && boxesDefault.Choice.Bundles.Length == 2 &&
                    boxesSession.GetPlan(0, ancientPoint)!.Ancient!.Children.IsEmpty &&
                    boxesDetail.Sections.Any(section => section.StableId == "ancient-bundle-unselected") &&
                    new[] { 0, 1 }.All(index => BoxSection(boxesDetail, index).LargeCardResults &&
                        !BoxSection(boxesDetail, index).Choices.Single().Selected &&
                        BoxSection(boxesDetail, index).Objects.Select(item => item.CardSnapshot).SequenceEqual(boxesDefault.Choice.Bundles[index])) &&
                    boxesDetail.Premises == null,
                    "Scroll Boxes retains both real candidate bundles without a fabricated selection or duplicate premise card list in " + language);
                var firstBox = BoxSection(boxesDetail, 0).Choices.Single();
                check(firstBox.Enabled && boxesPresenter.ApplyChoice(boxesDetail.NodeId, firstBox.Id) &&
                    boxesSession.Nodes[(0, ancientPoint)].Ancient!.BundleChoice!.SelectedBundle == 0 &&
                    boxesSession.GetPlan(0, ancientPoint)!.Ancient!.Children.Single() is
                        { Action: PredictorObtainChildAction.ChooseBundle, BundleIndex: 0, ExpectedSource.Entry: "SCROLL_BOXES" } firstBoxInput &&
                    JsonSerializer.Serialize(firstBoxInput.ExpectedBundles) == JsonSerializer.Serialize(boxesDefault.Choice.Bundles),
                    "choosing Scroll Boxes' first actual bundle authors its original complete candidate snapshot in " + language);
                boxesDetail = boxesPresenter.Detail((0, ancientPoint))!;
                var boxesRestored = new InRunMapNodePresenter(boxesSession, names, text).Detail((0, ancientPoint))!;
                check(BoxSection(boxesDetail, 0).Choices.Single().Selected && !BoxSection(boxesDetail, 1).Choices.Single().Selected &&
                    BoxSection(boxesRestored, 0).Choices.Single().Selected &&
                    !boxesDetail.Sections.Any(section => section.StableId == "ancient-acquisition-result" && section.Objects.Any(item => item.CardSnapshot != null)) &&
                    boxesSession.Nodes[(0, ancientPoint)].Ancient!.Outcome!.AddedCards.Length == boxesDefault.Choice.Bundles[0].Length,
                    "completed Scroll Boxes keeps its real chosen child selected and editable after presenter recreation without duplicating its cards as another result in " + language);
                details.ShowNode(boxesDetail with { IsCurrent = null }, new(new(100, 350), new(100, 180)), screen.GetGlobalRect());
                await Layout();
                check(VisibleChoice(BoxSection(boxesDetail, 0).Choices.Single().Id) is { Disabled: false } &&
                    VisibleChoice(BoxSection(boxesDetail, 1).Choices.Single().Id) is { Disabled: false } &&
                    boxesDefault.Choice.Bundles[0].All(item => TopCards().Any(shown => shown.Key == item.Key)),
                    "completed Scroll Boxes renders both live bundle edit controls and the actual chosen cards in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-scroll-boxes-selected-" + language + ".png"));
                details.CloseHover();
                var secondBox = BoxSection(boxesDetail, 1).Choices.Single();
                check(boxesPresenter.ApplyChoice(boxesDetail.NodeId, secondBox.Id) &&
                    boxesSession.GetPlan(0, ancientPoint)!.Ancient!.Children.Single() is { BundleIndex: 1 } &&
                    boxesSession.Nodes[(0, ancientPoint)].Ancient!.BundleChoice!.SelectedBundle == 1 &&
                    JsonSerializer.Serialize(boxesSession.GetPlan(0, ancientPoint)!.Ancient!.ExpectedOffer) == boxesParent &&
                    boxesSession.Nodes[(0, ancientSuffix)].Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card),
                    "editing a completed Scroll Boxes child replaces only its actual bundle choice and keeps the accepted parent and independent suffix in " + language);
                boxesDetail = boxesPresenter.Detail((0, ancientPoint))!;
                var clearBox = boxesDetail.Sections.Single(section => section.StableId == "ancient-bundle-clear").Choices.Single();
                check(!boxesPresenter.ApplyChoice(boxesDetail.NodeId, secondBox.Id) && boxesPresenter.ApplyChoice(boxesDetail.NodeId, clearBox.Id) &&
                    boxesSession.GetPlan(0, ancientPoint)!.Ancient!.Children.IsEmpty &&
                    boxesSession.Nodes[(0, ancientPoint)].Ancient!.BundleChoice!.SelectedBundle == null &&
                    boxesSession.Nodes[(0, ancientSuffix)].Advanced &&
                    JsonSerializer.Serialize(boxesSession.Snapshot.Run.Working) == boxesCaller,
                    "clearing Scroll Boxes returns to a truthful unselected default, rejects superseded callbacks and preserves caller state in " + language);

                var pomanderSession = AncientFixture("POMANDER");
                var pomanderPresenter = new InRunMapNodePresenter(pomanderSession, names, text);
                var pomanderDetail = pomanderPresenter.Detail((0, ancientPoint))!;
                string pomanderCaller = JsonSerializer.Serialize(pomanderSession.Snapshot!.Run.Working);
                var choosePomander = AllChoices(pomanderDetail.Sections).Single(choice => choice.Object?.Key.Entry == "POMANDER");
                check(pomanderPresenter.ApplyChoice(pomanderDetail.NodeId, choosePomander.Id) && pomanderSession.Nodes[(0, ancientPoint)].Advanced,
                    "actual Pomander gift adoption continues before optional target precision is supplied in " + language);
                pomanderDetail = pomanderPresenter.Detail((0, ancientPoint))!;
                var pomanderTarget = AllChoices(pomanderDetail.Premises!.Sections).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                var pomanderCard = pomanderTarget.Object!.CardSnapshot!;
                string pomanderParent = JsonSerializer.Serialize(pomanderSession.GetPlan(0, ancientPoint)!.Ancient);
                check(pomanderPresenter.ApplyChoice(pomanderDetail.NodeId, pomanderTarget.Id) &&
                    pomanderSession.GetPlan(0, ancientPoint)!.ObtainInputs.Single().Input.DeckCards.Single() ==
                        new PredictorPlannedCard(pomanderCard.Id, pomanderCard.Key, pomanderCard.FloorAdded),
                    "Pomander optional precision saves the real selected card identity in " + language);
                bool PomanderSelected(InRunMapNodeDetailData data) => AllChoices(data.Premises!.Sections)
                    .Count(choice => choice.Selected && choice.Object?.CardSnapshot?.Id == pomanderCard.Id) == 1;
                pomanderDetail = pomanderPresenter.Detail((0, ancientPoint))!;
                check(PomanderSelected(pomanderDetail) &&
                    PomanderSelected(new InRunMapNodePresenter(pomanderSession, names, text).Detail((0, ancientPoint))!),
                    "Pomander saved target has singular selected feedback in both current and recreated presenters in " + language);
                var clearPomander = AllChoices(pomanderDetail.Premises!.Sections)
                    .Single(choice => choice.Label == text.Get("predictor.map.clear_optional_input"));
                check(pomanderPresenter.ApplyChoice(pomanderDetail.NodeId, clearPomander.Id) &&
                    pomanderSession.GetPlan(0, ancientPoint)!.ObtainInputs.IsEmpty &&
                    !PomanderSelected(pomanderPresenter.Detail((0, ancientPoint))!) &&
                    !PomanderSelected(new InRunMapNodePresenter(pomanderSession, names, text).Detail((0, ancientPoint))!) &&
                    JsonSerializer.Serialize(pomanderSession.GetPlan(0, ancientPoint)!.Ancient) == pomanderParent &&
                    pomanderSession.Nodes[(0, ancientSuffix)].Rewards.Any(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Card) &&
                    JsonSerializer.Serialize(pomanderSession.Snapshot.Run.Working) == pomanderCaller,
                    "clearing Pomander removes saved and cached target feedback while preserving its true parent, independent suffix and caller state in " + language);

                foreach (var directCase in new[] { (Entry: "TRASH_HEAP", Option: "GRAB"), (Entry: "TEA_MASTER", Option: "BONE_TEA") })
                {
                    var directSession = Fixture(directCase.Entry);
                    var directPresenter = new InRunMapNodePresenter(directSession, names, text);
                    var directDetail = directPresenter.Detail((act, first))!;
                    string directCaller = JsonSerializer.Serialize(directSession.Snapshot!.Run.Working);
                    var directChoice = AllChoices(directDetail.Sections.Concat(directDetail.Premises!.Sections))
                        .First(choice => choice.Enabled && choice.Preview?.OptionKey == directCase.Option);
                    check(directPresenter.ApplyChoice(directDetail.NodeId, directChoice.Id) && directSession.Nodes[(act, first)].Advanced,
                        "the actual " + directCase.Entry + " branch is adopted without losing its direct acquisition in " + language);
                    directDetail = directPresenter.Detail((act, first))!;
                    var directOutcome = directDetail.Sections.Single(section => section.StableId == directDetail.NodeId + "|event-applied-outcome").OutcomePreview!;
                    var directNode = directSession.Nodes[(act, first)];
                    var originalCardIds = directSession.Snapshot.Run.Working.Deck.ToHashSet();
                    var directAddedCards = directNode.Cards.Where(item => item.IsKnown && !originalCardIds.Contains(item.Value.Id)).Select(item => item.Value).ToArray();
                    var originalRelicIds = directSession.Snapshot.Run.Working.Relics.Select(item => item.Id).ToHashSet();
                    var directAddedRelics = directNode.Inventory!.Relics.Where(item => item.IsKnown && !originalRelicIds.Contains(item.Value.Id)).Select(item => item.Value.Key).ToArray();
                    check(directCase.Entry == "TRASH_HEAP"
                        ? directAddedCards.Length != 0 && directAddedCards.All(item => PreviewRows(directOutcome)
                            .Where(row => row.Action == OperationPreviewAction.AddCard).Sum(row => row.Cards.Count(shown => shown == item with { Id = 0 })) == 1)
                        : directAddedRelics.Length != 0 && directAddedRelics.All(item => PreviewRows(directOutcome)
                            .Where(row => row.Action == OperationPreviewAction.ObtainRelic).Sum(row => row.Models.Count(shown => shown == item)) == 1),
                        "adopted " + directCase.Entry + " main outcome retains the actual direct result once in " + language);
                    if (directCase.Entry == "TRASH_HEAP")
                        check(directNode.Event!.AppliedResult!.GoldChange == 100 &&
                            directOutcome.Notices.Contains(text.Format("predictor.map.gold_gain", 100)),
                            "Trash Heap's adopted direct outcome includes its real hundred-gold gain without changing the saved semantic option identity in " + language);
                    details.CloseHover();
                    details.ShowNode(directDetail with { IsCurrent = null }, new(new(100, 350), new(56, 56)), screen.GetGlobalRect());
                    await Layout();
                    check(directCase.Entry == "TRASH_HEAP"
                        ? directAddedCards.All(item => TopCards().Any(shown => shown.Key == item.Key))
                        : directAddedRelics.All(item => Descendants(details).OfType<Label>().Any(label => label.IsVisibleInTree() &&
                            label.Text.Contains(names.Resolve(item, GameContentKind.Relic), StringComparison.Ordinal))),
                        "the actual adopted " + directCase.Entry + " result remains visible with premises collapsed in " + language);
                    using (var image = root.GetViewport().GetTexture().GetImage())
                        image.SavePng(Path.Combine(output, "drawing-adopted-direct-" + directCase.Entry + "-" + language + ".png"));
                    check(JsonSerializer.Serialize(directSession.Snapshot.Run.Working) == directCaller,
                        "direct event outcome display preserves caller state in " + directCase.Entry + "/" + language);
                    details.CloseHover();
                }

                // A real tradable relic supplies the native option's exact cost;
                // this is a controlled inventory fixture, not a forged offer.
                var traderSession = Fixture("RELIC_TRADER", tradeRelic: true);
                var traderPresenter = new InRunMapNodePresenter(traderSession, names, text);
                var traderDetail = traderPresenter.Detail((act, first))!;
                var traderOption = traderSession.Nodes[(act, first)].Event!.EntryOptions.First(option => option.Value?.RelicId != null);
                var traderLost = traderSession.Snapshot!.Run.Working.Relics.Single(item => item.Id == traderOption.Value!.RelicId).Key;
                var traderChoice = AllChoices(traderDetail.Sections).Single(choice => choice.Preview?.OptionKey == traderOption.Key);
                string traderLossText = text.Format("predictor.map.event_relic_lost", names.Resolve(traderLost, GameContentKind.Relic));
                check(traderOption.LostRelic == traderLost && traderChoice.Preview!.Notices.Contains(traderLossText),
                    "Trader's actual option explicitly names the exact relic surrendered as its cost in " + language);
                details.ShowNode(traderDetail with { IsCurrent = null }, new(new(100, 350), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                VisibleChoice(traderChoice.Id)!.EmitSignal(Control.SignalName.MouseEntered);
                await Layout();
                check(Descendants(details).OfType<Label>().Any(label => label.IsVisibleInTree() && label.Text.Contains(traderLossText, StringComparison.Ordinal)),
                    "Trader's real hover renders the localized relinquished-relic label in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-trader-cost-" + language + ".png"));
                details.CloseHover();

                var tinkerSession = Fixture("TINKER_TIME");
                var tinkerPresenter = new InRunMapNodePresenter(tinkerSession, names, text);
                var tinkerDetail = tinkerPresenter.Detail((act, first))!;
                var tinkerOption = tinkerSession.Nodes[(act, first)].Event!.EntryOptions.Single(option => option.Key == "CHOOSE_CARD_TYPE");
                var tinkerOutcome = tinkerDetail.Sections.Single(section => section.StableId == tinkerDetail.NodeId + "|event-outcome|CHOOSE_CARD_TYPE").OutcomePreview!;
                check(tinkerSession.Nodes[(act, first)].Advanced && !tinkerSession.Nodes[(act, first)].Event!.BusinessChoiceRequired &&
                    tinkerSession.GetPlan(act, first) == null && !tinkerOption.ResultPreview!.Options.IsEmpty &&
                    tinkerOutcome.Heading!.Contains(text.Get("predictor.map.unselected"), StringComparison.Ordinal) &&
                    tinkerOutcome.Heading.Contains(text.Get("predictor.map.branch_preview"), StringComparison.Ordinal) &&
                    tinkerOutcome.Branches.SelectMany(branch => branch.Sections).Any(section =>
                        section.Rows.Any(row => !row.Cards.IsEmpty) || section.CardGroups.Any(group => !group.Cards.IsEmpty)),
                    "Tinker Time keeps actual bounded nested card information on the optional main surface without inventing a mandatory branch decision in " + language);
                details.ShowNode(tinkerDetail with { IsCurrent = null }, new(new(100, 350), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                check(TopCards().Any(item => item.Key.Entry == "MAD_SCIENCE"),
                    "Tinker Time's existing nested Mad Science result reaches the real card surface with premises collapsed in " + language);
                details.CloseHover();

                // Controlled projection fixture: use a real unresolved Jungle
                // gold source, then attach its explicit consumer observation.
                // This does not claim the two-node route generated Morphic.
                session = Fixture("JUNGLE_MAZE_ADVENTURE", goldBudget: 0);
                var jungle = session.Nodes[(act, first)];
                var goldSource = PredictorRouteCauseEvidence.OriginalCauses(jungle.Event!.GoldPendingCauses)
                    .First(cause => cause.NodeId == jungle.Position.NodeId && cause.Code == "EventOptionChoice");
                var goldConsumer = PredictorRouteCauseEvidence.CreateExplicit("EventGoldAppearancePending",
                    session.Nodes[(act, suffix)].Position.NodeId, PredictorCombatDomain.Continuation,
                    PredictorPendingReasonKind.DerivedDependency, "Controlled display fixture: the event admission consumes the original unknown gold.",
                    ["event.appearance.MORPHIC_GROVE.allowed", "gold.balance"], [goldSource]);
                var consumerFixture = session.Nodes[(act, suffix)] with
                {
                    Room = PredictorRoomKind.Event, Model = new("EVENT", "MORPHIC_GROVE"), Rewards = [], PendingCauses = [goldConsumer],
                    Event = new(new("EVENT", "MORPHIC_GROVE"), [], [], [], [], [], [goldConsumer])
                };
                const string originalGoldSourceLabel = "Display fixture: original Jungle gold source";
                var consumerRows = InRunMapPrerequisiteProjection.Project(consumerFixture,
                    id => id == jungle.Position.NodeId ? originalGoldSourceLabel : "Display fixture: event consumer", text);
                check(jungle.Advanced && session.Nodes[(act, suffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown) &&
                    consumerRows.Any(row => row.Identity == goldSource.Identity && row.Source == originalGoldSourceLabel &&
                        row.AffectedResults.Contains(text.Get("predictor.map.prerequisite.event_occurrence")) &&
                        row.Text.Contains(originalGoldSourceLabel, StringComparison.Ordinal)),
                    "controlled consumer display preserves the actual Jungle gold origin and names event occurrence as its affected result in " + language);

                session = Fixture("COLOSSAL_FLOWER");
                presenter = new(session, names, text);
                var gold = presenter.Detail((act, first))!;
                var waiting = presenter.DeferredDetails(act, [first, suffix]);
                check(waiting.Count == 1 && waiting[(act, suffix)].Information.Any(line => line.Contains(gold.Title, StringComparison.Ordinal)) &&
                    waiting[(act, suffix)].Sections.Count == 0 && waiting[(act, suffix)].Premises == null &&
                    waiting[(act, suffix)].EditorActions.Count == 0 && !session.Nodes.ContainsKey((act, suffix)),
                    "uncomputed drawn suffix explains its actual event source without fabricating a room or reward in " + language);
                var originalSession = DiagnosticField(panel, "_prediction");
                var originalPresenter = DiagnosticField(panel, "_presenter");
                const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
                try
                {
                    typeof(InRunMapPredictionPanel).GetField("_prediction", members)!.SetValue(panel, session);
                    typeof(InRunMapPredictionPanel).GetField("_presenter", members)!.SetValue(panel, presenter);
                    typeof(InRunMapPredictionPanel).GetMethod("RenderInformation", members)!.Invoke(panel, null);
                    var annotations = (Dictionary<MapPosition, Control>)DiagnosticField(panel, "_annotations");
                    check(annotations.TryGetValue(suffix, out var annotation) &&
                        Descendants(annotation).OfType<Label>().Any(label => label.Text.Contains(gold.Title, StringComparison.Ordinal)),
                        "actual map render attaches the waiting-source feedback to the later drawn node in " + language);
                }
                finally
                {
                    typeof(InRunMapPredictionPanel).GetField("_prediction", members)!.SetValue(panel, originalSession);
                    typeof(InRunMapPredictionPanel).GetField("_presenter", members)!.SetValue(panel, originalPresenter);
                    typeof(InRunMapPredictionPanel).GetMethod("RenderInformation", members)!.Invoke(panel, null);
                }
                var extract = gold.Sections.SelectMany(section => section.Choices).Single(choice =>
                    choice.Label == text.Get("predictor.runtime.option.EXTRACT"));
                check(session.Nodes[(act, first)].Event!.BusinessChoiceRequired && extract.Enabled,
                    "real gold decision remains a main-panel input in " + language);
                details.ShowNode(gold with { IsCurrent = null }, new(new(120, 230), new(56, 56)), screen.GetGlobalRect());
                await Layout();
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-gold-" + language + ".png"));
                check(presenter.ApplyChoice(gold.NodeId, extract.Id) && session.Nodes[(act, first)].Event!.GoldBalance == 1035 &&
                    session.Nodes[(act, suffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                    "authored gold selection recomputes real balance and independent suffix in " + language);
                check(!presenter.ApplyChoice(gold.NodeId, extract.Id), "old choice cannot silently rebind after recompute in " + language);
                check(presenter.DeferredDetails(act, [first, suffix]).IsEmpty && waiting[(act, suffix)].IsCurrent?.Invoke() == false,
                    "the source edit retires waiting feedback and delivers actual suffix observations in " + language);
                details.CloseHover();

                foreach (var (entry, fightKey) in new[] { ("PUNCH_OFF", "I_CAN_TAKE_THEM"), ("THE_LANTERN_KEY", "KEEP_THE_KEY") })
                {
                    session = Fixture(entry);
                    presenter = new(session, names, text);
                    var eventDetail = presenter.Detail((act, first))!;
                    var eventNode = session.Nodes[(act, first)].Event!;
                    var fight = eventDetail.Sections.SelectMany(section => section.Choices).Single(choice =>
                        choice.Label == text.Get("predictor.runtime.option." + fightKey));
                    var preview = fight.PreviewFactory?.Invoke() ?? fight.Preview!;
                    check(preview != null && preview.Branches.SelectMany(branch => branch.Sections).Any(section =>
                        section.CardGroups.Any(group => !group.Cards.IsEmpty)) &&
                        !preview.Notices.Contains(text.Get("predictor.runtime.plan.pending.EventCombatChoice")),
                        "fight branch immediately previews its actual combat reward without an extra Fight click in " + entry + "/" + language);
                    var branchBattle = eventNode.EntryOptions.Single(option => option.Key == fightKey).ResultPreview!.Combat!;
                    check(preview!.Notices.Contains(text.Format("predictor.map.combat_gold_known", branchBattle.Rewards.Where(reward =>
                        reward.IsKnown && reward.Kind == PredictorRewardKind.Gold).Sum(reward => reward.Value!.GoldAmount))),
                        "combat preview includes its known actual gold instead of only cards and items in " + entry + "/" + language);
                    if (entry == "PUNCH_OFF")
                    {
                        var nab = eventNode.EntryOptions.Single(option => option.Key == "NAB").ResultPreview!;
                        var actualRelic = nab.ObtainedRelics.Single().Key;
                        int Relics(OperationPreview value) => value.Branches.SelectMany(branch => branch.Sections)
                            .SelectMany(section => section.Rows).Where(row => row.Action == OperationPreviewAction.ObtainRelic)
                            .Sum(row => row.Models.Count(key => key == actualRelic));
                        check(Relics(InRunMapOptionPreview.Build(eventNode.Event!.Value, "NAB", nab, [])) == 1 &&
                            Relics(InRunMapOptionPreview.Build(eventNode.Event.Value, "NAB", nab with
                                { ObtainedRelics = nab.ObtainedRelics.Add(nab.ObtainedRelics.Single()) }, [])) == 2,
                            "one actual relic receipt is displayed once and duplicate same-model receipts remain two in " + language +
                            "; actual=" + actualRelic + "; rewards=" + string.Join(',', nab.Rewards.Select(reward =>
                                reward.Value?.Key + ":" + reward.Value?.Status)));
                        check(NestedSections(eventDetail.Premises!.Sections).Sum(section =>
                            section.Objects.Count(item => item.Kind == GameContentKind.Relic && item.Key == actualRelic)) == 1,
                            "the actual unselected Punch Off NAB premise displays its single relic receipt once in " + language);
                    }
                    details.ShowNode(eventDetail with { IsCurrent = null }, new(new(650, 230), new(56, 56)), screen.GetGlobalRect());
                    await Layout();
                    var button = Descendants(details).OfType<Button>().Single(control => control.IsVisibleInTree() &&
                        control.HasMeta("choice_id") && control.GetMeta("choice_id").AsString() == fight.Id);
                    var stableBeforePreview = details.HoverGlobalRegion;
                    typeof(InRunMapNodeDetails).GetMethod("ShowOptionPreview", members)!.Invoke(details, [button, preview]);
                    await Layout();
                    typeof(InRunMapNodeDetails).GetMethod("LayoutOptionPreview", members)!.Invoke(details, null);
                    await DiagnosticFrame(root);
                    var frame = (Control)DiagnosticField(details, "_previewFrame");
                    var shadow = (Control)DiagnosticField(details, "_previewShadow");
                    var viewport = (ScrollContainer)DiagnosticField(details, "_previewViewport");
                    check(frame.Visible && frame.Position.X >= 0 && frame.Position.Y >= 0 && shadow.Position.X >= 0 &&
                        shadow.Position.Y >= 0 && viewport.ScrollHorizontal == 0 &&
                        frame.Position.X + frame.Size.X <= viewport.Size.X,
                        "option preview frame and shadow start inside the clipping viewport in " + entry + "/" + language);
                    CheckSurfaces("branch hover/" + entry + "/" + language);
                    if (entry == "THE_LANTERN_KEY")
                        check(TopCards().Length != 0 && TopCards().All(card => card.Key.Entry != "LANTERN_KEY"),
                            "actual Lantern Key combat hover retains ordinary offers without a large quest face in " + language);
                    check(details.HoverGlobalRegion.Position.IsEqualApprox(stableBeforePreview.Position) &&
                        Math.Abs(details.HoverGlobalRegion.Size.X - stableBeforePreview.Size.X) < 1,
                        "opening a branch preview preserves main origin and width in " + entry + "/" + language);
                    using (var image = root.GetViewport().GetTexture().GetImage())
                        image.SavePng(Path.Combine(output, "drawing-event-" + entry + "-" + language + ".png"));
                    check(presenter.ApplyChoice(eventDetail.NodeId, fight.Id) && session.Nodes[(act, first)].Event is
                        { Advanced: true, AppliedActions: 1 } completed &&
                        completed.Rewards.Where(reward => reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard)
                            .All(reward => reward.DefaultSkipped && reward.Value?.Status == PredictorRewardStatus.Skipped) &&
                        session.Nodes[(act, suffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                        "one adopted business branch defaults optional card/Quest offers to Skip and continues the actual suffix in " + entry + "/" + language);
                    var adopted = presenter.Detail((act, first))!;
                    var actualRewards = session.Nodes[(act, first)].Event!.Rewards;
                    var actualSections = adopted.Sections.Where(section => section.StableId?.Contains("|reward|", StringComparison.Ordinal) == true).ToArray();
                    var actualIds = actualSections.Select(section => section.StableId).ToHashSet();
                    check(actualSections.Length == actualRewards.Length && actualIds.Count == actualSections.Length &&
                        !adopted.Premises!.Sections.Any(section => actualIds.Contains(section.StableId)) &&
                        !adopted.Sections.Any(section => section.CombatPanel) &&
                        !actualSections.SelectMany(section => section.Choices).Any(choice => choice.Label == text.Get("predictor.runtime.plan.skip")),
                        "completed event owns each current reward once in the main panel without a separate combat panel or Skip buttons in " + entry + "/" + language);
                    check(adopted.Sections.SelectMany(section => section.Choices).Any(choice => choice.Selected && choice.Preview != null),
                        "adoption preserves the actual branch choice in " + entry + "/" + language);
                    if (entry == "PUNCH_OFF")
                        await CheckDrawingNarrowSummaryGeometry(root, output, screen, details, adopted, preview,
                            actualSections, names, language, check);
                    details.ShowNode(adopted with { IsCurrent = null }, new(new(650, 230), new(56, 56)), screen.GetGlobalRect());
                    await Layout();
                    int expectedGroups = actualSections.Count(section => section.Objects.Any(item => item.Kind == GameContentKind.Card &&
                        item.CardSnapshot is { } snapshot && NeowOperationOverlay.MapUsesCardFace(snapshot)));
                    // Reward text headings also carry reward_group. Count
                    // native dock hosts for ordinary actual combat offers.
                    // Quest receipts and optional branch inputs stay compact.
                    int CardGroups() => Descendants(details).OfType<Control>().Count(control => control.IsVisibleInTree() &&
                        control.HasMeta("reward_group_has_stable_identity") && actualIds.Contains(control.GetMeta("reward_group").AsString()));
                    using (var image = root.GetViewport().GetTexture().GetImage())
                        image.SavePng(Path.Combine(output, "drawing-event-" + entry + "-adopted-" + language + ".png"));
                    var adoptedOrigin = details.HoverGlobalRegion.Position;
                    var selectedBranch = adopted.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices)
                        .Single(choice => choice.Selected);
                    var selectedButton = Descendants(details).OfType<Button>().Single(control => control.IsVisibleInTree() &&
                        control.HasMeta("choice_id") && control.GetMeta("choice_id").AsString() == selectedBranch.Id);
                    selectedButton.EmitSignal(Control.SignalName.MouseEntered);
                    await Layout();
                    CheckSurfaces("selected business branch/" + entry + "/" + language);
                    check(details.HoverGlobalRegion.Position.IsEqualApprox(adoptedOrigin) &&
                        !((Control)DiagnosticField(details, "_previewViewport")).Visible &&
                        !((Control)DiagnosticField(details, "_previewCardsViewport")).Visible && CardGroups() == expectedGroups,
                        "the selected business branch's real mouse-enter creates no duplicate option subpanel or card offers in " + entry + "/" + language);
                    using (var image = root.GetViewport().GetTexture().GetImage())
                        image.SavePng(Path.Combine(output, "drawing-event-" + entry + "-nested-" + language + ".png"));
                    typeof(InRunMapNodeDetails).GetMethod("CloseOptionPreview", members)!.Invoke(details, null);
                    await Layout();
                    var beforeResizeSource = (Rect2)DiagnosticField(details, "_nodeGlobal");
                    var beforeResizeOffset = details.HoverGlobalRegion.Position - beforeResizeSource.Position;
                    root.GetWindow().Size = new(1024, 640);
                    await DiagnosticFrame(root);
                    details.ShowNode(adopted with { IsCurrent = null }, new(new(650, 510), new(56, 56)), screen.GetGlobalRect());
                    await Layout();
                    CheckSurfaces("adopted combat/1024/" + entry + "/" + language);
                    var narrowMain = details.HoverGlobalRegion;
                    var narrowNode = new Rect2(new(650, 510), new(56, 56));
                    // Resizing retains the established source-relative side.
                    // Measure its actual facing edges even if the far edge of
                    // the main extends beyond the narrower visible canvas.
                    bool mainOnRight = narrowMain.Position.X >= narrowNode.End.X;
                    bool mainOnLeft = narrowMain.End.X <= narrowNode.Position.X;
                    float facingY = narrowNode.GetCenter().Y;
                    var nodeFacing = new Vector2(mainOnRight ? narrowNode.End.X : narrowNode.Position.X, facingY);
                    var panelFacing = new Vector2(mainOnRight ? narrowMain.Position.X : narrowMain.End.X,
                        Math.Clamp(facingY, narrowMain.Position.Y, narrowMain.End.Y));
                    var narrowConnection = (nodeFacing + panelFacing) * .5f;
                    var corridor = Enumerable.Range(1, 4).Select(index => nodeFacing.Lerp(panelFacing, index / 5f)).ToArray();
                    var narrowGeometry = new
                    {
                        Main = narrowMain.ToString(), Node = narrowNode.ToString(),
                        Offset = (narrowMain.Position - narrowNode.Position).ToString(), ExpectedOffset = beforeResizeOffset.ToString(),
                        mainOnRight, mainOnLeft,
                        NodeFacing = nodeFacing.ToString(), PanelFacing = panelFacing.ToString(),
                        Connection = narrowConnection.ToString(), Length = nodeFacing.DistanceTo(panelFacing),
                        RetainsHover = details.ContainsPointer(narrowConnection), BlocksMap = details.BlocksMapInput(narrowConnection),
                        Corridor = corridor.Select(point => new { Point = point.ToString(),
                            RetainsHover = details.ContainsPointer(point), BlocksMap = details.BlocksMapInput(point) }).ToArray()
                    };
                    using (var image = root.GetViewport().GetTexture().GetImage())
                        image.SavePng(Path.Combine(output, "drawing-event-" + entry + "-narrow-" + language + ".png"));
                    File.WriteAllText(Path.Combine(output, "drawing-event-" + entry + "-narrow-geometry-" + language + ".json"),
                        JsonSerializer.Serialize(narrowGeometry, new JsonSerializerOptions { WriteIndented = true }));
                    check(Math.Abs(narrowMain.Position.X - narrowNode.Position.X - beforeResizeOffset.X) < 1 &&
                        narrowMain.Position.Y >= 11 && narrowMain.End.Y <= 629 &&
                        (mainOnRight || mainOnLeft) && nodeFacing.DistanceTo(panelFacing) <= 80 &&
                        narrowGeometry.RetainsHover && !narrowGeometry.BlocksMap &&
                        narrowGeometry.Corridor.All(point => point.RetainsHover && !point.BlocksMap),
                        "a narrower viewport retains horizontal source anchoring, vertical screen bounds and the actual short hover corridor without owning map clicks in " +
                        entry + "/" + language + "; geometry=" + JsonSerializer.Serialize(narrowGeometry));
                    var narrowOrigin = details.HoverGlobalRegion.Position;
                    details.ShowNode(adopted with { IsCurrent = null, Information = Enumerable.Repeat(
                        "Layout fixture: source-dependent prerequisites remain readable in their bounded area.", 5).ToArray() },
                        new(new(650, 510), new(56, 56)), screen.GetGlobalRect());
                    await Layout();
                    check(Math.Abs(details.HoverGlobalRegion.Position.X - narrowOrigin.X) < 1 &&
                        details.HoverGlobalRegion.Position.Y >= 11 && details.HoverGlobalRegion.End.Y <= 629,
                        "additional prerequisite text preserves horizontal anchoring and vertical readability in " + entry + "/" + language);
                    CheckSurfaces("long prerequisites/1024/" + entry + "/" + language);
                    root.GetWindow().Size = new(1680, 1080);
                    await DiagnosticFrame(root);
                    details.ShowNode(adopted with { IsCurrent = null }, new(new(650, 230), new(56, 56)), screen.GetGlobalRect());
                    await Layout();
                    check(expectedGroups > 0 && CardGroups() == expectedGroups &&
                        !((Control)DiagnosticField(details, "_previewViewport")).Visible &&
                        details.FindChild("EventCombatPanel", true, false) == null,
                        "rendered completed event clears old combat/hover surfaces and keeps one native group per ordinary opportunity in " + entry + "/" + language +
                        "; expected=" + expectedGroups + "; visible=" + CardGroups() + "; dock=" + details.RewardCardDockGlobalRegion +
                        "; hover=" + details.HoverOpen + "; preview=" + ((Control)DiagnosticField(details, "_previewViewport")).Visible +
                        "; combat=" + (details.FindChild("EventCombatPanel", true, false) as Control)?.Visible);
                    var stableBeforePremises = details.HoverGlobalRegion;
                    ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                    await Layout();
                    check(details.HoverGlobalRegion.Position.IsEqualApprox(stableBeforePremises.Position) &&
                        Math.Abs(details.HoverGlobalRegion.Size.X - stableBeforePremises.Size.X) < 1,
                        "expanding optional premises keeps the same main origin and reward-owned width in " + entry + "/" + language);
                    using (var image = root.GetViewport().GetTexture().GetImage())
                        image.SavePng(Path.Combine(output, "drawing-event-" + entry + "-premises-" + language + ".png"));
                    check(CardGroups() == expectedGroups,
                        "opening premises does not add a second copy of the adopted combat cards in " + entry + "/" + language);
                    if (entry == "PUNCH_OFF")
                        check(adopted.Premises!.Sections.SelectMany(section => section.Objects).Any(item => item.Key.Entry == "INJURY") &&
                            HasVisibleName(details, new("CARD", "INJURY")) && TopCards().Length != 0 &&
                            TopCards().All(card => card.Key.Entry != "INJURY"),
                            "expanded Punch Off keeps the other branch Injury compact and current combat cards above the panel in " + language);
                    else
                    {
                        var quest = actualSections.SelectMany(section => section.Choices).Single(choice =>
                            choice.Object?.Key.Entry == "LANTERN_KEY" && choice.Enabled);
                        check(TopCards().Length != 0 && TopCards().All(card => card.Key.Entry != "LANTERN_KEY") &&
                            VisibleChoice(quest.Id) is { Disabled: false } questButton && HasVisibleName(questButton, quest.Object!.Key) &&
                            quest.PreviewFactory?.Invoke() != null,
                            "actual Lantern Key receipt keeps its compact name and reward input while expanded premises add no quest face in " + language);
                    }
                    var other = adopted.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices)
                        .First(choice => choice.Enabled && !choice.Selected);
                    check(presenter.ApplyChoice(adopted.NodeId, other.Id),
                        "the retained business branch can still be changed after combat adoption in " + entry + "/" + language);
                    var changed = presenter.Detail((act, first))!;
                    details.ShowNode(changed with { IsCurrent = null }, new(new(650, 230), new(56, 56)), screen.GetGlobalRect());
                    await Layout();
                    using (var image = root.GetViewport().GetTexture().GetImage())
                        image.SavePng(Path.Combine(output, "drawing-event-" + entry + "-changed-" + language + ".png"));
                    check(!changed.Sections.Any(section => section.CombatPanel) &&
                        details.FindChild("EventCombatPanel", true, false) == null &&
                        !Descendants(details).OfType<Control>().Any(control => control.IsVisibleInTree() &&
                            control.HasMeta("reward_group_has_stable_identity") &&
                            actualIds.Contains(control.GetMeta("reward_group").AsString())),
                        "changing away from combat retires the previous combat panel and native card offers in " + entry + "/" + language +
                        "; sections=" + changed.Sections.Count(section => section.CombatPanel) + "; panel=" +
                        (details.FindChild("EventCombatPanel", true, false) != null) + "; oldGroups=" + CardGroups());
                    var chooseFight = changed.Sections.Where(section => section.BranchRow).SelectMany(section => section.Choices)
                        .Single(choice => choice.Label == text.Get("predictor.runtime.option." + fightKey));
                    check(presenter.ApplyChoice(changed.NodeId, chooseFight.Id), "combat branch can be adopted again for reward input in " + entry + "/" + language);
                    var editable = presenter.Detail((act, first))!;
                    string unselectedPlan = JsonSerializer.Serialize(session.GetPlan(act, first));
                    var take = editable.Sections.Where(section => section.StableId?.Contains("|reward|", StringComparison.Ordinal) == true)
                        .SelectMany(section => section.Choices).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null &&
                            (entry != "THE_LANTERN_KEY" || choice.Object.Key.Entry == "LANTERN_KEY"));
                    check(take.PreviewFactory?.Invoke() is { } && presenter.ApplyChoice(editable.NodeId, take.Id) &&
                        session.GetPlan(act, first)!.Event!.Actions.Select(action => action.Action).SequenceEqual(new[]
                            { PredictorEventActionKind.Option, PredictorEventActionKind.Reward }) &&
                        session.Nodes[(act, first)].Event!.Rewards.Any(reward =>
                            reward.Value?.Cards.Any(candidate => candidate.Key == take.Object!.Key) == true &&
                            reward.AppliedChoice?.CardIndex == 0 && reward.Value?.Status == PredictorRewardStatus.Taken),
                        "known event-combat cards, including the compact Lantern Key, can be previewed and taken with the actual adopted encounter in " + entry + "/" + language);
                    var selected = presenter.Detail((act, first))!;
                    var cancel = selected.Sections.SelectMany(section => section.Choices).Single(choice =>
                        choice.Selected && choice.Object?.Key == take.Object!.Key && choice.Label == text.Get("predictor.runtime.cancel"));
                    check(cancel.PreviewFactory?.Invoke() is { OptionKey: "CANCEL_TAKE" } && presenter.ApplyChoice(selected.NodeId, cancel.Id) &&
                        JsonSerializer.Serialize(session.GetPlan(act, first)) == unselectedPlan &&
                        session.Nodes[(act, first)].Event!.Rewards.Any(reward =>
                            reward.Value?.Cards.Any(candidate => candidate.Key == take.Object!.Key) == true &&
                            reward.DefaultSkipped && reward.AppliedChoice == null && reward.Value.Status == PredictorRewardStatus.Skipped) &&
                        session.Nodes[(act, suffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                        "clicking the selected card cancels only its take, restores default Skip and preserves the event prefix and suffix in " + entry + "/" + language);
                    details.CloseHover();
                }

                session = Fixture("ABYSSAL_BATHS", treasure: true);
                presenter = new(session, names, text);
                var relic = presenter.Detail((act, first))!;
                var target = relic.Premises!.Sections.SelectMany(section => section.Choices)
                    .First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                var card = target.Object!.CardSnapshot!;
                details.ShowNode(relic with { IsCurrent = null }, new(new(120, 230), new(56, 56)), screen.GetGlobalRect());
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Layout();
                check(TopCards().Length == 0 && !details.HasRewardCardDock && VisibleChoice(target.Id) is { Disabled: false } targetButton &&
                    HasVisibleName(targetButton, card.Key),
                    "optional receipt targets retain compact input controls without a top card dock in " + language);
                details.CloseHover();
                check(presenter.ApplyChoice(relic.NodeId, target.Id) &&
                    session.GetPlan(act, first)!.ObtainInputs.Length == 1 &&
                    session.Nodes[(act, first)].Cards.Any(candidate => candidate.IsKnown && candidate.Value.Id == card.Id &&
                        candidate.Value.UpgradeLevel == card.UpgradeLevel + 1),
                    "optional receipt target can be authored from the retained premise panel in " + language);
                relic = presenter.Detail((act, first))!;
                var clear = relic.Premises!.Sections.SelectMany(section => section.Choices)
                    .Single(choice => choice.Label == text.Get("predictor.map.clear_optional_input"));
                check(presenter.ApplyChoice(relic.NodeId, clear.Id) && session.GetPlan(act, first)!.ObtainInputs.IsEmpty &&
                    session.Nodes[(act, suffix)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                    "clearing only that optional target restores local uncertainty and keeps independent rewards in " + language);

                session = Fixture("ABYSSAL_BATHS", rest: true);
                presenter = new(session, names, text);
                var defaultRest = presenter.Detail((act, first))!;
                var defaultTarget = defaultRest.Premises!.Sections.Where(section => section.Title == text.Get("predictor.runtime.rest.Smith"))
                    .SelectMany(section => section.Choices).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                check(session.Nodes[(act, first)].DefaultRestAction is { Action: PredictorRestAction.Smith, SmithCard: null } &&
                    session.GetPlan(act, first) == null && defaultRest.Information.Count == 0 && session.Nodes[(act, suffix)].Rewards.Any(reward =>
                        reward.Kind == PredictorRewardKind.Card && reward.IsKnown),
                    "default Smith has no authored or representative target and leaves independent rewards known in " + language);
                details.ShowNode(defaultRest with { IsCurrent = null }, new(new(120, 230), new(56, 56)), screen.GetGlobalRect());
                ((Button)details.FindChild("PremisesExpansion", true, false)!).EmitSignal(Button.SignalName.Pressed);
                await Layout();
                check(TopCards().Length == 0 && !details.HasRewardCardDock &&
                    VisibleChoice(defaultTarget.Id) is { Disabled: false } smithButton && HasVisibleName(smithButton, defaultTarget.Object!.Key),
                    "expanded campfire retains compact Smith target controls without a top card dock in " + language);
                using (var image = root.GetViewport().GetTexture().GetImage())
                    image.SavePng(Path.Combine(output, "drawing-rest-default-" + language + ".png"));
                details.CloseHover();
                check(presenter.ApplyChoice(defaultRest.NodeId, defaultTarget.Id) && session.GetPlan(act, first) is
                    { RestActions.Length: 1, LeaveRest: true } chosen && chosen.RestActions[0].SmithCard != null &&
                    !session.Nodes[(act, first)].RestDefaultApplied,
                    "default Smith can receive a target directly from its premise panel without first authoring another action in " + language);
                var restPlan = new PredictorPlannedNode(session.Nodes[(act, first)].Position.NodeId)
                    { RestActions = [new(PredictorRestAction.Smith), new(PredictorRestAction.Heal)], LeaveRest = true };
                check(session.SetPlan(act, first, restPlan) && session.Nodes[(act, first)].AppliedRestActions == 2,
                    "targetless Smith retains and processes the Tent Heal suffix in " + language);
                presenter = new(session, names, text);
                var restDetail = presenter.Detail((act, first))!;
                var smith = restDetail.Premises!.Sections.Where(section => section.Title == text.Get("predictor.runtime.rest.Smith"))
                    .SelectMany(section => section.Choices).First(choice => choice.Enabled && choice.Object?.CardSnapshot != null);
                check(presenter.ApplyChoice(restDetail.NodeId, smith.Id) && session.GetPlan(act, first) is
                    { RestActions.Length: 2, LeaveRest: true } updated && updated.RestActions[0].SmithCard != null &&
                    updated.RestActions[1].Action == PredictorRestAction.Heal && session.Nodes[(act, first)].AppliedRestActions == 2,
                    "a processed Smith still accepts an optional target without deleting its authored suffix in " + language);
                await CheckDrawingSavedSelectionUi(root, output, screen, details,
                    (entry, cooking) => Fixture(entry, rest: cooking, cook: cooking),
                    act, first, names, text, language, signature, check);
            }
            await CheckMapFeatureLifecycle(root, panel, check);
            check(signature() == before && JsonSerializer.Serialize(production.Plans) == productionPlans,
                "focused UI authoring leaves native state and ordinary production plan untouched");
        }
        finally { details.Free(); }
    }
}
