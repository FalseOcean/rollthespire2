using System.Collections;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static readonly List<(Type Type, object Pool)> _operationFixturePools = [];

    private static void CleanupOperationFixturePools()
    {
        var pools = (IDictionary)typeof(NodePool).GetField("_pools", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        foreach (var (type, pool) in _operationFixturePools)
        {
            var free = PredictorOperationCapture.Read<IEnumerable>(pool, "_freeObjects").Cast<Node>();
            var used = PredictorOperationCapture.Read<IEnumerable>(pool, "_usedObjects").Cast<Node>();
            foreach (var node in free.Concat(used).Distinct().ToArray())
                if (GodotObject.IsInstanceValid(node)) node.Free();
            pools.Remove(type);
        }
        _operationFixturePools.Clear();
    }

    // Reuses the isolated native Neow host, its actual patched controls and
    // SubViewport pointer. These are controlled native pages, not claims that
    // one natural run rolls these pages in this order.
    private static async Task RunAdditionalOperationChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player,
        NEventRoom eventNode, NSceneContainer layoutScenes, NSceneContainer roomScenes,
        NGlobalUi global, NTopBar topBar, Harmony fixture, Action<object, string, object?> set)
    {
        if (System.Environment.GetEnvironmentVariable("RT2_OPERATION_DISPLAY_PERF") == "1")
        {
            await RunOperationDisplayPerformance(root, output, viewport, hoverRoot, player);
            return;
        }
        int checks = 0;
        var cases = new List<object>();
        var receipt = Path.Combine(output, "operation-surfaces-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        void Check(bool condition, string reason)
        { checks++; if (!condition) throw new InvalidOperationException("OperationSurfaceUi: " + reason); }
        if (!OS.GetCmdlineUserArgs().Contains("--operation-rf-coexistence"))
            await RunOperationPreferenceChecks(root, output, runtime, Check);
        // Low options with no native explanation must remain the spatial
        // owner of a text-only prediction; no card-height slot is reserved.
        foreach (float x in new[] { 120f, 460f, 1360f })
        {
            var screen = new Rect2(Vector2.Zero, new Vector2(1920, 1080));
            var owner = new Rect2(new(x, 820), new Vector2(390, 90));
            var areas = NeowHoverPlacement.Plan(screen, owner, [owner], [], new(460, 150), Vector2.Zero, false);
            Check(screen.Encloses(areas.Summary) && !areas.Summary.Intersects(owner) &&
                Math.Abs(areas.Summary.GetCenter().Y - owner.GetCenter().Y) <= 170 && areas.Cards.Size == Vector2.Zero,
                "low left/central/right options keep text-only prediction nearby without a reserved card area");
        }
        var unselectedRest = new PredictorOperationRestScene([PredictorRestAction.Heal]);
        string unselectedFingerprint = PredictorOperationCapture.SceneFingerprint(unselectedRest);
        Check(unselectedFingerprint.Length > 0 && unselectedFingerprint !=
            PredictorOperationCapture.SceneFingerprint(unselectedRest with { CandidateIds = [] }),
            "rest fingerprint accepts absent arrays and preserves absent versus explicit empty selection");
        Check(unselectedFingerprint != PredictorOperationCapture.SceneFingerprint(
            new PredictorOperationRestScene([PredictorRestAction.Heal], HatchSuppliers: [])),
            "rest fingerprint preserves absent versus explicit empty hatch suppliers");
        object? Field(object owner, string name) => PredictorOperationCapture.ReadNullable(owner, name);
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (wait.Elapsed < TimeSpan.FromSeconds(10) && !ready()) await Frames(1);
            Check(ready(), reason);
        }
        NeowOperationHoverController Controller() => hoverRoot.GetNode<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        NeowOperationOverlay? View() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName)
            ?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
        async Task Move(Vector2 point, int frames = 3)
        {
            viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await Frames(frames);
        }
        string Signature() => JsonSerializer.Serialize(new
        {
            players = run.Players.Select(p => p.ToSerializable()), rng = run.Rng.ToSerializable(),
            shared = PredictorLiveRunCapture.Bag(run.SharedRelicGrabBag),
            personal = PredictorLiveRunCapture.Bag(player.RelicGrabBag),
            eventRng = run.CurrentRoom is EventRoom ev ? ev.LocalMutableEvent.Rng.ToSerializable() : null,
            eventOptions = run.CurrentRoom is EventRoom optionRoom ? optionRoom.LocalMutableEvent.CurrentOptions
                .Select(option => new { option.TextKey, option.WasChosen, option.IsLocked }).ToArray() : null
        });
        async Task Exercise(string name, Control owner, Control hitbox, string source,
            Func<PredictorOperationScene, bool>? scene, Func<OperationPreview, bool> outcome,
            Action<NeowOperationOverlay, OperationPreview>? inspect = null, bool keepHover = false)
        {
            GD.Print("OPERATION_SURFACE_CASE_BEGIN " + name);
            if (!keepHover) await Move(new(4, viewport.Size.Y - 4));
            string before = Signature();
            await Move(hitbox.GetGlobalRect().GetCenter(), 0);
            var controller = Controller(); controller.SetProcess(false);
            Check(ReferenceEquals(Field(controller, "_owner"), owner), name + " original native hover owns the operation request");
            int seen = _neowSeenCalls;
            AccessTools.Method(typeof(NeowOperationHoverController), "BeginPrediction").Invoke(controller, null);
            var snapshot = (PredictorOperationSnapshot?)Field(controller, "_snapshot");
            var work = (Task<OperationPreview>?)Field(controller, "_work");
            Check(work != null && (scene == null || snapshot != null && scene(snapshot.Scene)), name + " captures its actual native scene and starts detached worker");
            await Until(() => work!.IsCompleted, name + " real detached worker finishes");
            Check(work!.IsCompletedSuccessfully, name + " real worker succeeds without hidden failure");
            var preview = work.Result;
            Check(preview.Source.Entry == source && preview.Faults.IsEmpty && outcome(preview),
                name + " predicts actual native identity and meaningful outcome;details=" + JsonSerializer.Serialize(new {
                    expectedSource = source, actualSource = preview.Source.Serialized, gold = snapshot?.State.GoldBudget,
                    preview.Notices, preview.PendingReasons, branches = preview.Branches.Length,
                    faults = preview.Faults.Select(fault => new { fault.Stage, cause = fault.Exception.ToString() }) }));
            // Even a long frame must not repeat the freshness read immediately
            // after publication, before anything could change on the main thread.
            controller._Process(.75);
            GD.Print("OPERATION_SURFACE_CASE_PUBLISH " + name);
            Check(PredictorOperationCapture.Read<NeowOperationHoverController.HoverTiming>(controller, "_timing").PollCount == 0,
                name + " publication does not immediately poll the same inputs again");
            controller.SetProcess(true);
            await Until(() => View() is { Visible: true }, name + " publishes actual native-hover renderer");
            await Frames(4);
            var view = View()!;
            Check(ReferenceEquals(Field(view, "_preview"), preview), name + " renderer uses exact completed worker projection");
            if (preview.Notices.IsEmpty && preview.Branches.SelectMany(branch => branch.Sections)
                .All(section => OperationDisplayPolicy.Resolve(preview, section).Text == OperationTextMode.None))
                Check(view.GetNodeOrNull<Control>("Rt2NeowSummary") == null && view.SummaryDesiredSize == Vector2.Zero,
                    name + " successful D display has no text panel or reserved summary space");
            Check(view.VisibleRegions.Count > 0 && view.VisibleRegions.All(rect =>
                view.GetViewportRect().Grow(.2f).Encloses(rect) && !rect.Intersects(hitbox.GetGlobalRect())),
                name + " rendered surfaces preserve screen bounds and current native input surface;hitbox=" + hitbox.GetGlobalRect() +
                ";regions=" + string.Join(";", view.VisibleRegions));
            Check(Descendants(view).OfType<NCard>().All(card => card.Model is { RunState: null, CombatState: null } &&
                card.MouseFilter == Control.MouseFilterEnum.Ignore), name + " artwork remains detached and passive");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GD.Print("OPERATION_SURFACE_CASE_DRAW " + name);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-" + name + "-zh.png"));
            inspect?.Invoke(view, preview);
            Check(Signature() == before && _neowSeenCalls == seen, name + " hover leaves actual run, RNG, offers and seen cards unchanged");
            cases.Add(new { name, source, nativeOwner = owner.GetType().Name,
                snapshot = snapshot?.Scene.GetType().Name ?? "PotionTargetContext",
                fingerprint = snapshot?.Fingerprint, preview.Branches.Length,
                nativeInput = hitbox.GetGlobalRect().ToString(), nativeOwnerRect = owner.GetGlobalRect().ToString(),
                nativeFaces = Descendants(view).OfType<NCard>().Count(),
                surfaces = view.VisibleRegions.Select(rect => rect.ToString()).ToArray() });
            if (keepHover) return;
            await Move(new(4, viewport.Size.Y - 4), 0);
            Check(View() == null && Field(controller, "_owner") == null, name + " original native unfocus clears immediately");
            await Frames();
        }
        void Bypass(MethodInfo method) => fixture.Patch(method,
            prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
        // Full-screen navigation/audio/shake are unrelated to these mounted
        // control scenes. Native hover, capture, simulation and rendering run.
        Bypass(AccessTools.Method(typeof(NGame), nameof(NGame.SetScreenShakeTarget)));
        Bypass(AccessTools.Method(typeof(NGame), nameof(NGame.ClearScreenShakeTarget)));
        Bypass(AccessTools.Method(typeof(ActiveScreenContext), nameof(ActiveScreenContext.Update)));
        Bypass(AccessTools.Method(typeof(ActiveScreenContext), nameof(ActiveScreenContext.FocusOnDefaultControl)));
        _operationFixturePools.Add((typeof(NCard), NodePool.Init<NCard>("res://scenes/cards/card.tscn", 0)));
        _operationFixturePools.Add((typeof(NGridCardHolder), NodePool.Init<NGridCardHolder>("res://scenes/cards/holders/grid_card_holder.tscn", 0)));
        foreach (var oldLayout in game.GetChildren().OfType<NEventLayout>()) oldLayout.Hide();
        // Ordinary event: actual canonical option generation, original
        // default layout and original Ransack native option callback.
        run.PushRoom(new EventRoom(ModelDb.Event<PotionCourier>()));
        RunManager.Instance.EventSynchronizer.BeginEvent(ModelDb.Event<PotionCourier>());
        var ordinary = RunManager.Instance.EventSynchronizer.GetLocalEvent();
        await Until(() => ordinary.CurrentOptions.Count > 0, "ordinary native event initialization completes");
        set(eventNode, "_event", ordinary);
        var eventLayout = PreloadManager.Cache.GetScene("res://scenes/events/default_event_layout.tscn").Instantiate<NEventLayout>();
        set(layoutScenes, nameof(NSceneContainer.CurrentScene), eventLayout);
        game.AddChild(eventLayout); game.MoveChild(eventLayout, 0); eventLayout.SetEvent(ordinary);
        eventLayout.SetTitle(ordinary.Title.GetFormattedText());
        ordinary.DynamicVars.AddTo(ordinary.Description!);
        eventLayout.SetDescription(ordinary.Description!.GetFormattedText());
        eventLayout.AddOptions(ordinary.CurrentOptions);
        foreach (var button in eventLayout.OptionButtons) button.EnableButton();
        await Frames(2); // Native button entry tweens are installed deferred.
        await Until(() => eventLayout.OptionButtons.All(button => button.Modulate.A > .99f) &&
            eventLayout.GetNode<RichTextLabel>("%EventDescription").Modulate.A > .99f,
            "original native ordinary-event option entry animation settles");
        var ransack = eventLayout.OptionButtons.Single(button =>
            PredictorOperationEventCapture.Option(ordinary, button.Option).Key == "RANSACK");
        if (OS.GetCmdlineUserArgs().Contains("--operation-rf-coexistence"))
        {
            await RunRfCoexistenceChecks(root, output, runtime, game, viewport, hoverRoot, run, player, topBar,
                ransack, Signature, Check);
            eventLayout.Free(); ordinary.EnsureCleanup(); run.PopCurrentRoom();
            return;
        }
        var operationFeedbackBasis = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
        await RunDisabledOperationSourceCheck(root, viewport, hoverRoot, ransack,
            runtime.Detection.NormalizedVersion, Signature, Check);
        await Exercise("event-potion-courier", ransack, ransack, "POTION_COURIER",
            scene => scene is PredictorOperationEventScene,
            preview => preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows).Any(r => r.Models.Any(k => k.Category == "POTION")));
        eventLayout.Free(); ordinary.EnsureCleanup(); run.PopCurrentRoom();

        // Original philosopher options and their real character-pool closures.
        // Advance only this isolated fixture's native option generator until
        // the requested pink/orange/green pages are present together.
        run.PushRoom(new EventRoom(ModelDb.Event<ColorfulPhilosophers>()));
        RunManager.Instance.EventSynchronizer.BeginEvent(ModelDb.Event<ColorfulPhilosophers>());
        var philosophers = RunManager.Instance.EventSynchronizer.GetLocalEvent();
        await Until(() => philosophers.CurrentOptions.Count > 0, "philosophers native initialization completes");
        string[] colors = ["NECROBINDER", "REGENT", "SILENT"];
        for (int attempts = 0; attempts < 64 && !colors.All(color => philosophers.CurrentOptions.Any(option =>
                 PredictorOperationEventCapture.Option(philosophers, option).Key == color)); attempts++)
            set(philosophers, "_currentOptions", AccessTools.Method(typeof(ColorfulPhilosophers), "GenerateInitialOptions").Invoke(philosophers, null));
        Check(colors.All(color => philosophers.CurrentOptions.Any(option =>
            PredictorOperationEventCapture.Option(philosophers, option).Key == color)),
            "native philosopher generator supplies pink/orange/green choices");
        set(eventNode, "_event", philosophers);
        var philosopherLayout = PreloadManager.Cache.GetScene("res://scenes/events/default_event_layout.tscn").Instantiate<NEventLayout>();
        set(layoutScenes, nameof(NSceneContainer.CurrentScene), philosopherLayout);
        game.AddChild(philosopherLayout); game.MoveChild(philosopherLayout, 0); philosopherLayout.SetEvent(philosophers);
        philosopherLayout.SetTitle(philosophers.Title.GetFormattedText());
        philosophers.DynamicVars.AddTo(philosophers.Description!);
        philosopherLayout.SetDescription(philosophers.Description!.GetFormattedText());
        philosopherLayout.AddOptions(philosophers.CurrentOptions);
        foreach (var button in philosopherLayout.OptionButtons) button.EnableButton();
        await Frames(2);
        await Until(() => philosopherLayout.OptionButtons.All(button => button.Modulate.A > .99f),
            "philosopher original option animation settles");
        var originalViewport = viewport.Size;
        foreach (var size in new[] { new Vector2I(1920, 1080), new Vector2I(1680, 1260) })
        {
            viewport.Size = size; root.GetWindow().Size = size; await Frames(6);
            Vector2? firstCardSize = null;
            float? firstSummaryWidth = null;
            foreach (var (color, ordinal) in new[] { ("NECROBINDER", 1), ("REGENT", 2), ("SILENT", 3), ("NECROBINDER", 4) })
            {
                var button = philosopherLayout.OptionButtons.Single(button =>
                    PredictorOperationEventCapture.Option(philosophers, button.Option).Key == color);
                await Exercise($"philosophers-{size.X}x{size.Y}-{ordinal}-{color.ToLowerInvariant()}", button, button,
                    "COLORFUL_PHILOSOPHERS", scene => scene is PredictorOperationEventScene,
                    preview => preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups)
                        .Count(group => group.Cards.Length == 3) == 3,
                    (view, preview) =>
                    {
                        var cards = Descendants(view).OfType<NCard>().ToArray();
                        var tiles = cards.Select(card => ((Control)card.GetParent()).GetGlobalRect().Size).ToArray();
                        Check(cards.Length == 9 && tiles.All(tile => tile.IsEqualApprox(tiles[0])),
                            "philosopher 3x3 groups retain nine equally sized native card tiles");
                        if (firstCardSize == null) firstCardSize = tiles[0];
                        Check(tiles.All(tile => tile.IsEqualApprox(firstCardSize.Value)) &&
                            tiles[0].IsEqualApprox(new Vector2(350, 470) * view.CardDisplayScale),
                            "philosopher pink/orange/green/pink return keeps viewport-defined card size");
                        var groups = preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).ToArray();
                        var groupLines = Descendants(view.GetNode<Control>("Rt2NeowSummary")).OfType<Label>().Select(label => label.Text).ToArray();
                        Check(groups.All(group => groupLines.Count(line => group.Cards.All(card => line.Contains(
                                ModelDb.GetById<CardModel>(new(card.Key.Category, card.Key.Entry)).Title, StringComparison.Ordinal))) == 1),
                            "philosopher A3 puts each complete three-card group on one text row");
                        var summary = view.GetNodeOrNull<Control>("Rt2NeowSummary");
                        Check(summary != null && summary.Size.X >= 240 && summary.Size.X <= size.X * .5f,
                            "philosopher summary uses sensible bounded ordinary text width");
                        if (firstSummaryWidth == null) firstSummaryWidth = summary!.Size.X;
                        Check(Math.Abs(summary!.Size.X - firstSummaryWidth.Value) < 1,
                            "philosopher equivalent three-group summary width remains stable across option switches");
                    }, keepHover: true);
            }
            await Move(new(4, viewport.Size.Y - 4), 0);
            Check(View() == null, "philosopher direct pink/orange/green/pink sequence clears on native unfocus");
            await Frames();
        }
        viewport.Size = originalViewport; await Frames(4);
        philosopherLayout.Free(); philosophers.EnsureCleanup(); run.PopCurrentRoom();

        // A known relic on a real native event option: four Coffer potion
        // attempts must reach the actual icon renderer, including overflow.
        var cofferCollateral = new[] { "ANCHOR", "BLOOD_VIAL", "HAPPY_FLOWER", "PEN_NIB", "ODDLY_SMOOTH_STONE" }
            .Select(key => ModelDb.GetById<RelicModel>(new("RELIC", key)).ToMutable()).ToArray();
        foreach (var relic in cofferCollateral) player.AddRelicInternal(relic, silent: true);
        run.PushRoom(new EventRoom(ModelDb.Event<RelicTrader>()));
        RunManager.Instance.EventSynchronizer.BeginEvent(ModelDb.Event<RelicTrader>());
        var cofferEvent = RunManager.Instance.EventSynchronizer.GetLocalEvent();
        await Until(() => cofferEvent.CurrentOptions.Count > 0, "native Trader initialization completes");
        var cofferOffers = PredictorOperationCapture.Read<IReadOnlyList<RelicModel>>(cofferEvent, "_newRelics").ToArray();
        cofferOffers[0] = ModelDb.Relic<AlchemicalCoffer>(); set(cofferEvent, "_newRelics", cofferOffers);
        // Rebuild the original native option hover tips from those same
        // initialized offers; neither stale tooltip identities nor a synthetic
        // hover callback may stand in for this controlled current page.
        set(cofferEvent, "_currentOptions", AccessTools.Method(typeof(RelicTrader), "GenerateInitialOptions").Invoke(cofferEvent, null));
        cofferEvent.CalculateVars();
        set(eventNode, "_event", cofferEvent);
        var cofferLayout = PreloadManager.Cache.GetScene("res://scenes/events/default_event_layout.tscn").Instantiate<NEventLayout>();
        set(layoutScenes, nameof(NSceneContainer.CurrentScene), cofferLayout);
        game.AddChild(cofferLayout); game.MoveChild(cofferLayout, 0); cofferLayout.SetEvent(cofferEvent);
        cofferLayout.SetTitle(cofferEvent.Title.GetFormattedText());
        cofferEvent.DynamicVars.AddTo(cofferEvent.Description!);
        cofferLayout.SetDescription(cofferEvent.Description!.GetFormattedText());
        cofferLayout.AddOptions(cofferEvent.CurrentOptions);
        foreach (var button in cofferLayout.OptionButtons) button.EnableButton();
        await Frames(2);
        await Until(() => cofferLayout.OptionButtons.All(button => button.Modulate.A > .99f), "native coffer option animation settles");
        var cofferButton = cofferLayout.OptionButtons.Single(button => PredictorOperationEventCapture.Option(cofferEvent, button.Option).Key == "TOP");
        await Exercise("event-trader-alchemical-coffer", cofferButton, cofferButton, "RELIC_TRADER",
            scene => scene is PredictorOperationEventScene,
            preview => preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows)
                .Where(row => row.Action == OperationPreviewAction.GeneratePotion).Sum(row => row.Models.Length) == 4,
            (view, preview) =>
            {
                var expected = preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                    .Where(row => row.Action == OperationPreviewAction.GeneratePotion).SelectMany(row => row.Models)
                    .Select(key => ModelDb.GetById<PotionModel>(new(key.Category, key.Entry)).Title.GetFormattedText()).ToArray();
                var rendered = Descendants(view.GetNode<Control>("Rt2NeowSummary")).OfType<HBoxContainer>()
                    .Where(row => row.GetChildren().OfType<TextureRect>().Any(icon =>
                        icon.CustomMinimumSize.IsEqualApprox(new Vector2(24, 24))))
                    .SelectMany(row => row.GetChildren().OfType<Label>()).Select(label => label.Text)
                    .Where(expected.Contains).ToArray();
                Check(expected.Length == 4 && rendered.OrderBy(name => name).SequenceEqual(expected.OrderBy(name => name)),
                    "Alchemical Coffer native hover renders all four generated potion identities/icons with multiplicity");
            });
        cofferLayout.Free(); cofferEvent.EnsureCleanup(); run.PopCurrentRoom();
        foreach (var relic in cofferCollateral) player.RemoveRelicInternal(relic, silent: true);

        // The rest synchronizer generates the real current Heal option.
        player.AddRelicInternal(ModelDb.Relic<DreamCatcher>().ToMutable(), silent: true);
        player.AddRelicInternal(ModelDb.Relic<TinyMailbox>().ToMutable(), silent: true);
        var rest = new RestSiteRoom(); run.PushRoom(rest);
        RunManager.Instance.RestSiteSynchronizer.BeginRestSite();
        set(rest, "_synchronizer", RunManager.Instance.RestSiteSynchronizer);
        var heal = NRestSiteButton.Create(rest.Options.Single(option => option.OptionId == "HEAL"));
        game.AddChild(heal); heal.Position = new(760, 780);
        await Until(() => heal.MouseFilter == Control.MouseFilterEnum.Stop, "native rest option animation enables real input");
        await Exercise("rest-heal", heal, heal, "TINY_MAILBOX", scene => scene is PredictorOperationRestScene,
            preview => preview.Branches.SelectMany(b => b.Sections).Any(s => s.Source.Entry == "DREAM_CATCHER" && s.CardGroups.Length > 0) &&
                preview.Branches.SelectMany(b => b.Sections).Any(s => s.Source.Entry == "TINY_MAILBOX" && s.Rows.Any(r => r.Models.Length > 0)));
        var mailbox = player.Relics.Single(relic => relic.Id.Entry == "TINY_MAILBOX");
        player.RemoveRelicInternal(mailbox, silent: true);
        await Exercise("rest-heal-dream-catcher-alone", heal, heal, "DREAM_CATCHER", scene => scene is PredictorOperationRestScene,
            preview => preview.Branches.SelectMany(branch => branch.Sections).Any(section =>
                section.Source.Entry == "DREAM_CATCHER" && section.CardGroups.Single().Cards.Length == 3),
            (view, preview) =>
            {
                var tiles = Descendants(view).OfType<NCard>().Select(card => card.GetParent<Control>()).ToArray();
                Check(tiles.Length == 3 && tiles.Zip(tiles.Skip(1)).All(pair =>
                    pair.Second.GlobalPosition.X > pair.First.GlobalPosition.X &&
                    Math.Abs(pair.Second.GlobalPosition.Y - pair.First.GlobalPosition.Y) < .02f &&
                    !pair.First.GetGlobalRect().Intersects(pair.Second.GetGlobalRect())),
                    "Dream Catcher alone keeps all three full native cards on one horizontal row");
            });
        player.AddRelicInternal(mailbox, silent: true);
        heal.Free(); run.PopCurrentRoom();

        // Exact local slot identity inside the original native topbar
        // potion container, with its real potion/holder scenes.
        var attack = ModelDb.Potion<AttackPotion>().ToMutable();
        player.AddPotionInternal(attack, silent: true);
        var holder = NPotionHolder.Create(true);
        var potionContainer = topBar.GetNode<NPotionContainer>("%PotionContainer");
        set(topBar, nameof(NTopBar.PotionContainer), potionContainer);
        potionContainer.AddChild(holder); holder.Position = new(50, 0);
        holder.AddPotion(NPotion.Create(attack)!); await Frames();
        await Exercise("potion-attack", holder, holder, "ATTACK_POTION", null,
            preview => preview.Branches.SelectMany(b => b.Sections).Any(s => s.CardGroups.Any(g => g.Cards.Length > 0)));
        holder.Free();

        // A real generated normal merchant inventory. Courier belongs to
        // this isolated fixture before generation; no purchase is executed.
        player.AddRelicInternal(ModelDb.Relic<TheCourier>().ToMutable(), silent: true);
        player.Gold = 999;
        var merchant = new MerchantRoom(); run.PushRoom(merchant);
        set(merchant, "_runState", run);
        var inventory = MerchantInventory.CreateForNormalMerchant(player); merchant.Inventories.Add(inventory);
        // The full merchant rug imports a game-only Spine hand, unavailable in
        // the stock Godot host. Mount its original card-slot scene under the
        // native inventory owner; preserve actual inventory/slot membership and
        // native Ready/focus while omitting only that decoration and navigation.
        var shop = new NMerchantInventory();
        _neowStaticSceneNodes.Add(shop);
        foreach (string callback in new[] { "_Ready", "_EnterTree", "_ExitTree" })
            fixture.Patch(AccessTools.Method(typeof(NMerchantInventory), callback),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(NeowSkipStaticSceneStartup)));
        Bypass(AccessTools.Method(typeof(NMerchantSlot), "OnMerchantHandHovered"));
        Bypass(AccessTools.Method(typeof(NMerchantSlot), "OnMerchantHandUnhovered"));
        set(shop, nameof(NMerchantInventory.Inventory), inventory); set(shop, nameof(NMerchantInventory.IsOpen), true);
        game.AddChild(shop); game.MoveChild(shop, 0); shop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var slot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_card.tscn").Instantiate<NMerchantCard>();
        shop.AddChild(slot); slot.Position = new(840, 820); slot.Initialize(shop); slot.FillSlot(inventory.CharacterCardEntries[0]);
        await Frames(4);
        await RunOperationAdmissionChecks(root, output, runtime, game, viewport, hoverRoot, run, player,
            inventory, shop, slot, Signature, Check);
        long shopId = PredictorLiveRunCapture.Id(slot.Entry);
        await Exercise("shop-courier-card", slot, slot.GetNode<Control>("%Hitbox"), "THE_COURIER",
            scene => scene is PredictorOperationShopScene s && s.Entries.Any(e => e.Id == shopId),
            preview => preview.Branches.SelectMany(b => b.Sections).Any(s => s.Source.Entry == "THE_COURIER" && s.Rows.Any(r => r.Cards.Length > 0)));
        slot.Hide();
        // The same relic can enter through an ancient offer or a shop slot;
        // exercise the shop's own projection rather than borrowing a Neow page.
        var orreryEntry = new MerchantRelicEntry(ModelDb.Relic<Orrery>().ToMutable(), player);
        PredictorOperationCapture.Read<List<MerchantRelicEntry>>(inventory, "_relicEntries")[0] = orreryEntry;
        var relicSlot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_relic.tscn").Instantiate<NMerchantRelic>();
        shop.AddChild(relicSlot); relicSlot.Position = new(1390, 690); relicSlot.Initialize(shop); relicSlot.FillSlot(orreryEntry);
        await Frames(4);
        long orreryId = PredictorLiveRunCapture.Id(orreryEntry);
        await RunShopOrreryTemporalChecks(root, output, runtime, game, viewport, hoverRoot, run, player,
            relicSlot, orreryEntry, orreryId, Signature, Check);
        await Exercise("shop-orrery", relicSlot, relicSlot.Hitbox, "ORRERY",
            scene => scene is PredictorOperationShopScene s && s.Entries.Any(e => e.Id == orreryId && e.Key?.Entry == "ORRERY"),
            preview => preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups).Count() == 5,
            (view, preview) =>
            {
                var groups = preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups).ToArray();
                var stacks = Descendants(view).OfType<Control>().Where(c => c.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
                Check(groups.All(group => group.Cards.Length == 3) && stacks.Length == 5 &&
                    stacks.All(stack => stack.GetMeta("rt2_stacked").AsBool() && Descendants(stack).OfType<NCard>().Count() == 3),
                    "native shop Orrery displays five stacks of three instead of fifteen flat cards");
                var textRows = Descendants(view).OfType<Label>().Where(label => Enumerable.Range(1, 5)
                    .Any(number => label.Text.StartsWith("组" + number + "：", StringComparison.Ordinal))).ToArray();
                Check(textRows.Length == 5 && groups.Select((group, index) =>
                    Descendants(stacks[index]).OfType<NCard>().Select(card => card.Model!.Id.Entry)
                        .SequenceEqual(group.Cards.Select(card => card.Key.Entry))).All(value => value),
                    "native shop Orrery preserves one text row per reward and each reward's ordered card identities");
            });
        relicSlot.Hide();
        var potionEntry = inventory.PotionEntries[0];
        var potionSlot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_potion.tscn").Instantiate<NMerchantPotion>();
        shop.AddChild(potionSlot); potionSlot.Position = new(1400, 760);
        potionSlot.Initialize(shop); potionSlot.FillSlot(potionEntry);
        var addedPotions = new List<PotionModel>();
        string beforePotionFixture = Signature();
        try
        {
            while (player.HasOpenPotionSlots)
            {
                var filler = ModelDb.Potion<AttackPotion>().ToMutable();
                Check(player.AddPotionInternal(filler, silent: true).success, "fill isolated merchant potion fixture");
                addedPotions.Add(filler);
            }
            Check(addedPotions.Count > 0 && potionEntry.EnoughGold, "merchant full-slot fixture has gold and a removable filler");
            await Frames(4);
            await Exercise("shop-potion-full", potionSlot, potionSlot.Hitbox, potionEntry.Model!.Id.Entry,
                scene => scene is PredictorOperationShopScene,
                preview => preview.Notices.Contains("Purchase:TooFull") && preview.Branches.IsEmpty,
                (view, _) => Check(Descendants(view).OfType<Label>().Any(label => label.Text == "药水栏已满") &&
                    !Descendants(view).OfType<Label>().Any(label => label.Text.Contains("尚未确定", StringComparison.Ordinal)),
                    "native merchant full potion slots display their known reason, not undetermined input"), keepHover: true);
            player.DiscardPotionInternal(addedPotions[0], silent: true);
            string afterFreeSlot = Signature();
            await Until(() => View() is { Modulate.A: > 0 } current &&
                Field(current, "_preview") is OperationPreview forecast && forecast.Notices.IsEmpty &&
                forecast.Branches.SelectMany(branch => branch.Sections).Any(section => section.Source.Entry == "THE_COURIER"),
                "freeing a potion slot updates the same hover to its Courier replacement without refocusing");
            Check(Signature() == afterFreeSlot, "full-slot hover recovery does not purchase or change run/RNG");
            await Move(new(4, viewport.Size.Y - 4));
        }
        finally
        {
            foreach (var filler in addedPotions.Where(player.PotionSlots.Contains)) player.DiscardPotionInternal(filler, silent: true);
            potionSlot.Free();
        }
        Check(Signature() == beforePotionFixture, "full-slot fixture restores the original potion slots and run/RNG");
        await RunShopMirrorChecks(root, output, runtime, game, viewport, run, player, inventory, shop,
            Signature, Check, Exercise, global, set);
        shop.Free(); run.PopCurrentRoom();

        // Rewards use the actual synchronizer-owned set and native overlay
        // list. Only the overlay's unrelated map-navigation startup is
        // bypassed; original reward and alternative controls stay live.
        while (run.CurrentRoom != null) run.PopCurrentRoom();
        run.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Monster, RoomType.Monster, null);
        var overlays = new NOverlayStack();
        _neowStaticSceneNodes.Add(overlays);
        fixture.Patch(AccessTools.Method(typeof(NOverlayStack), "_Ready"),
            prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(NeowSkipStaticSceneStartup)));
        var backstop = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        overlays.AddChild(backstop); set(overlays, "_backstop", backstop);
        set(global, nameof(NGlobalUi.Overlays), overlays); game.AddChild(overlays);
        overlays.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backstop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        SaveManager.Instance.MarkFtueAsComplete("obtain_relic_ftue");
        var reward = new RelicReward(ModelDb.Relic<LeadPaperweight>().ToMutable(), player);
        var rewards = new RewardsSet(player).WithCustomRewards([reward]); await rewards.GenerateWithoutOffering();
        _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(rewards);
        var rewardScreen = NRewardsScreen.ShowScreen(rewards, false, run); await Frames(40);
        var rewardButton = Descendants(rewardScreen).OfType<NRewardButton>().Single();
        await Exercise("reward-relic-obtain", rewardButton, rewardButton, "LEAD_PAPERWEIGHT",
            scene => scene is PredictorOperationRewardsScene,
            preview => preview.Branches.SelectMany(b => b.Sections).Any(s => s.CardGroups.Any(g => g.Cards.Length == 2)));
        overlays.Remove(rewardScreen); RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet(); await Frames();
        await RunRewardAdmissionChecks(root, output, viewport, hoverRoot, run, player, overlays, Signature, Check);

        player.AddRelicInternal(ModelDb.Relic<Driftwood>().ToMutable(), silent: true);
        var options = new CardCreationOptions([player.Character.CardPool], CardCreationSource.Other, CardRarityOddsType.RegularEncounter);
        var cardReward = new CardReward(options, 3, player);
        var cardSet = new RewardsSet(player).WithCustomRewards([cardReward]); await cardSet.GenerateWithoutOffering();
        _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(cardSet);
        var selecting = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(cardReward);
        await Until(() => overlays.Peek() is NCardRewardSelectionScreen, "native reward select opens its original card screen");
        var cardScreen = (NCardRewardSelectionScreen)overlays.Peek()!;
        await Frames(40);
        var alternatives = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(cardScreen, "_extraOptions");
        int rerollIndex = alternatives.ToList().FindIndex(alternative => alternative.OptionId == "REROLL");
        Check(rerollIndex >= 0, "actual native card reward exposes its reroll alternative");
        var reroll = Descendants(cardScreen).OfType<NCardRewardAlternativeButton>().ElementAt(rerollIndex);
        await Exercise("reward-reroll", reroll, reroll, "DRIFTWOOD",
            scene => scene is PredictorOperationRewardsScene,
            preview => preview.OptionKey == "REROLL" && preview.Branches.SelectMany(b => b.Sections).Any(s => s.CardGroups.Any(g => g.Cards.Length == 3)));
        // Complete the real pending native selection through its ordinary
        // skip path; this is fixture cleanup after purity was established.
        AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected").Invoke(cardScreen,
            [alternatives.ToList().FindIndex(alternative => alternative.OptionId.Equals("Skip", StringComparison.OrdinalIgnoreCase))]);
        await Until(() => selecting.IsCompleted, "native reward selection cleanup completes");
        Check(selecting.IsCompletedSuccessfully, "native reward selection cleanup completes without a hidden fault");
        RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
        await RunShopOrreryRerollChecks(root, output, runtime, game, run, player, global, overlays,
            Signature, Check, Exercise, set);
        await RunShopOrreryRerollChecks(root, output, runtime, game, run, player, global, overlays,
            Signature, Check, Exercise, set, sacrifice: true);
        await RunShopOrreryRerollChecks(root, output, runtime, game, run, player, global, overlays,
            Signature, Check, Exercise, set, normalPurchase: true);
        await RunShopCauldronChecks(root, output, runtime, game, run, player, global, overlays,
            Signature, Check, Exercise, set);
        await RunRewardSequenceChecks(root, output, runtime, game, run, player, global, overlays,
            Signature, Check, Exercise, set);
        await RunPhilosopherRerollRecipeChecks(root, output, runtime, game, run, player, eventNode, overlays,
            Signature, Check, Exercise, set);
        await RunCrystalLootOperationChecks(root, output, runtime, game, run, player, eventNode, global, overlays,
            Signature, Check, Exercise, set);
        RunEventFeedbackChecks(operationFeedbackBasis, output);
        await RunOperationPolishDisplayChecks(root, output, viewport, hoverRoot, operationFeedbackBasis, Check);
        await RunModCardDisplayChecks(root, output, viewport, hoverRoot, run, player, Signature, Check);
        await RunEventRoyaltiesInputChecks(root, output, runtime, game, viewport, hoverRoot, run, player,
            eventNode, layoutScenes, overlays, Signature, Check, set);
        await RunPotionSequenceChecks(root, output, runtime, game, viewport, hoverRoot, run, player,
            global, overlays, Signature, Check, Exercise, set);

        // Start a real reward obtain and leave its original transform picker
        // suspended. Only the unrelated inventory pickup animation is omitted;
        // RelicCmd/selection observations and the native grid remain live.
        var relicInventory = new NRelicInventory();
        set(global, nameof(NGlobalUi.RelicInventory), relicInventory);
        Bypass(AccessTools.Method(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic)));
        var leafReward = new RelicReward(ModelDb.Relic<NewLeaf>().ToMutable(), player);
        var leafSet = new RewardsSet(player).WithCustomRewards([leafReward]); await leafSet.GenerateWithoutOffering();
        _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(leafSet);
        var leafScreen = NRewardsScreen.ShowScreen(leafSet, false, run); await Frames(4);
        var leafSelection = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(leafReward);
        await Until(() => overlays.Peek() is NDeckTransformSelectScreen || leafSelection.IsCompleted,
            "real New Leaf reward opens its original pending transform picker");
        Check(!leafSelection.IsCompleted && overlays.Peek() is NDeckTransformSelectScreen,
            "native obtain remains suspended at the real transform selection");
        var transformScreen = (NDeckTransformSelectScreen)overlays.Peek()!;
        await Frames(8);
        var target = Descendants(transformScreen).OfType<NGridCardHolder>().First(holder =>
            holder.IsVisibleInTree() && transformScreen.GetViewportRect().Encloses(holder.Hitbox.GetGlobalRect()));
        long targetId = PredictorLiveRunCapture.Id(target.CardModel);
        string selectedBefore = string.Join(",", PredictorOperationCapture.Read<IEnumerable<CardModel>>(transformScreen, "_selectedCards")
            .Select(PredictorLiveRunCapture.Id));
        await Exercise("card-target-new-leaf", target, target.Hitbox, "NEW_LEAF",
            scene => scene is PredictorOperationObtainScene { Kind: PredictorOperationObtainChildKind.NewLeaf } obtain &&
                obtain.NewLeafCandidates.Contains(targetId),
            preview => preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups)
                .Any(group => group.Cards.Length == 1 && !group.Possible && group.Highlighted),
            (view, preview) =>
            {
                var stacks = Descendants(view).OfType<Control>().Where(node => node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
                Check(stacks.All(stack => !stack.GetMeta("rt2_stacked").AsBool()) &&
                    stacks.Where(stack => stack.GetMeta("rt2_highlighted").AsBool()).SelectMany(stack => Descendants(stack).OfType<NCard>())
                        .All(card => !card.GetNode<Control>("%Highlight").Visible && card.GetParent<Control>().Modulate == Colors.White),
                    "transform picker flattens ordinal output and keeps the current ordinal at full brightness without a native outline");
            });
        // The picker uses its ordinal flat-result contract, including current
        // highlighting, rather than reusing the overview candidate stack.
        Check(!leafSelection.IsCompleted && PredictorOperationCapture.SelectionIsPending(transformScreen) &&
            selectedBefore == string.Join(",", PredictorOperationCapture.Read<IEnumerable<CardModel>>(transformScreen, "_selectedCards")
                .Select(PredictorLiveRunCapture.Id)),
            "native target hover commits no selection and leaves the original obtain pending");
        // Close the isolated fixture only after proving hover purity. This
        // ordinary native empty-selection path lets the real obtain unwind.
        AccessTools.Method(typeof(NDeckTransformSelectScreen), "CloseSelection").Invoke(transformScreen, [null]);
        await Until(() => leafSelection.IsCompleted, "native transform selection cleanup finishes the real obtain");
        Check(leafSelection.IsCompletedSuccessfully, "native transform cleanup preserves the actual transaction result");
        overlays.Remove(leafScreen); // Claiming its only reward already completes this native set.
        await RunTransformConfirmationChecks(root, output, runtime, run, player);
        relicInventory.Free(); set(global, nameof(NGlobalUi.RelicInventory), null);
        overlays.Free(); set(global, nameof(NGlobalUi.Overlays), null);
        File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = true, checks, cases,
            nativeHoverCaptureWorkerRenderer = true, runAndRngUnchangedByHover = true,
            covered = new[] { "ordinary event", "Colorful Philosophers pink/orange/green/pink at two resolutions",
                "Alchemical Coffer four native potion icons", "rest", "local potion holder", "Courier shop",
                "relic reward obtain", "card reward reroll", "native New Leaf card target selector" },
            ownerRuntimeAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"OPERATION_SURFACES_UI_PASS checks={checks};cases={cases.Count};actualNativeHover=true;actualCapture=true;actualWorker=true");
    }
}
