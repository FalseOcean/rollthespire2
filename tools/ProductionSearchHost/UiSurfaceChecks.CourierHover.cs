using System.Diagnostics;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static NMerchantSlot? _courierHoverOwner;
    private static int _courierHoverFocusCount;
    private static void CourierRecordNativeFocus(NMerchantSlot __instance)
    {
        if (ReferenceEquals(__instance, _courierHoverOwner)) _courierHoverFocusCount++;
    }

    private static async Task RunCourierHoverStandaloneChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player, NGlobalUi global,
        Harmony fixture, Action<object, string, object?> set, Action<bool, string> check)
    {
        foreach (var layout in game.GetChildren().OfType<NEventLayout>()) layout.Hide();
        _operationFixturePools.Add((typeof(NCard), NodePool.Init<NCard>("res://scenes/cards/card.tscn", 0)));
        player.AddRelicInternal(ModelDb.Relic<TheCourier>().ToMutable(), silent: true);
        player.Gold = 999;
        var merchant = new MerchantRoom(); run.PushRoom(merchant); set(merchant, "_runState", run);
        var inventory = MerchantInventory.CreateForNormalMerchant(player); merchant.Inventories.Add(inventory);
        var shop = new NMerchantInventory(); _neowStaticSceneNodes.Add(shop);
        foreach (string callback in new[] { "_Ready", "_EnterTree", "_ExitTree" })
            fixture.Patch(AccessTools.Method(typeof(NMerchantInventory), callback),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(NeowSkipStaticSceneStartup)));
        foreach (string callback in new[] { "OnMerchantHandHovered", "OnMerchantHandUnhovered" })
            fixture.Patch(AccessTools.Method(typeof(NMerchantSlot), callback),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
        set(shop, nameof(NMerchantInventory.Inventory), inventory); set(shop, nameof(NMerchantInventory.IsOpen), true);
        game.AddChild(shop); game.MoveChild(shop, 0); shop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        string Signature() => JsonSerializer.Serialize(new { players = run.Players.Select(current => current.ToSerializable()),
            rng = run.Rng.ToSerializable(), shared = PredictorLiveRunCapture.Bag(run.SharedRelicGrabBag),
            personal = PredictorLiveRunCapture.Bag(player.RelicGrabBag) });
        try
        {
            await RunCourierHoverChecks(root, output, runtime, game, viewport, hoverRoot, run, player,
                inventory, shop, global, Signature, check, set);
            await RunOperationPolishDisplayChecks(root, output, viewport, hoverRoot,
                PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game), check);
            GD.Print("COURIER_HOVER_UI_PASS nativeMouseInput=true;failedPurchase=true;actualPurchase=true;refocus=false");
        }
        finally { shop.Free(); run.PopCurrentRoom(); }
    }

    // One native mouse hover spans a failed purchase and an actual purchase.
    // Neither path sends another pointer motion or calls RT2 Focus directly.
    private static async Task RunCourierHoverChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player,
        MerchantInventory inventory, NMerchantInventory shop, NGlobalUi global,
        Func<string> signature, Action<bool, string> check, Action<object, string, object?> set)
    {
        var receipt = Path.Combine(output, "shop-courier-hover-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        var original = signature();
        var originalRelics = player.Relics.ToArray();
        var originalDiscoveredCards = player.DiscoveredCards.ToArray();
        string originalNativeLanguage = LocManager.Instance.Language;
        var languagePreferences = new RolltheSpire2.Ui.Workspace.SearchWorkspace(OS.GetUserDataDir(), false);
        string originalUiLanguage = languagePreferences.Preferences.LanguageOverride;
        int originalGold = player.Gold;
        var originalRng = player.PlayerRng.ToSerializable();
        var originalRunRng = run.Rng.ToSerializable();
        var originalPersonalBag = player.RelicGrabBag.ToSerializable();
        var originalSharedBag = run.SharedRelicGrabBag.ToSerializable();
        var originalInventoryUi = global.RelicInventory;
        var entries = PredictorOperationCapture.Read<List<MerchantRelicEntry>>(inventory, "_relicEntries");
        var originalEntry = entries[0];
        var originalViewportSize = viewport.Size; var originalWindowSize = root.GetWindow().Size;
        var fixture = new Harmony("rt2.shop.courier.hover.fixture");
        NMerchantRelic? slot = null;
        NRelicInventory? relicInventory = null;
        bool passed = false;
        object? evidence = null;
        string? failure = null;
        NeowOperationHoverController? Controller() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        NeowOperationOverlay? View() => Controller()?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var timer = Stopwatch.StartNew();
            while (!ready() && timer.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
            check(ready(), reason);
        }
        void Move(Vector2 position) => viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
        void Click(Vector2 position)
        {
            viewport.PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
                ButtonIndex = MouseButton.Left, Pressed = true }, true);
            viewport.PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
                ButtonIndex = MouseButton.Left, Pressed = false }, true);
        }
        bool PublishedFor(Control? owner) => View() is { Visible: true } view && view.Modulate.A > .001f &&
            Controller() is { } controller && ReferenceEquals(PredictorOperationCapture.ReadNullable(controller, "_owner"), owner) &&
            PredictorOperationCapture.ReadNullable(controller, "_snapshot") is PredictorOperationSnapshot snapshot &&
            PredictorOperationCapture.IsCurrent(snapshot, run, runtime.Detection.NormalizedVersion, game);
        bool Published() => PublishedFor(slot);
        void CheckCourierSkin(string phase)
        {
            var view = View()!;
            var preview = PredictorOperationCapture.Read<OperationPreview>(view, "_preview");
            check(preview.Source.Entry == "THE_COURIER", phase + " controlled passive offer has the standalone Courier preview");
            var native = PredictorOperationCapture.Read<Control>(Controller()!, "_nativeTextTip");
            var paper = native.GetChildren().OfType<Control>().Select(tip => tip.GetNodeOrNull<NinePatchRect>("Bg"))
                .First(bg => bg?.IsVisibleInTree() == true)!;
            var background = Descendants(view.GetNode<Control>("Rt2NeowSummary")).OfType<NinePatchRect>().First(bg => !bg.ShowBehindParent);
            check(Math.Abs(background.GetGlobalRect().Size.X - paper.GetGlobalRect().Size.X) <= 1 &&
                background.Texture == paper.Texture && background.RegionRect == paper.RegionRect &&
                background.PatchMarginLeft == paper.PatchMarginLeft && background.PatchMarginRight == paper.PatchMarginRight &&
                background.PatchMarginTop == paper.PatchMarginTop && background.PatchMarginBottom == paper.PatchMarginBottom &&
                background.AxisStretchHorizontal == paper.AxisStretchHorizontal && background.AxisStretchVertical == paper.AxisStretchVertical,
                phase + " real Courier paper matches the native tooltip paper width, texture and ninepatch geometry");
        }
        try
        {
            check(player.Relics.OfType<TheCourier>().Any(relic => !relic.IsMelted) && !player.Relics.OfType<Anchor>().Any() &&
                run.CurrentMapPointHistoryEntry == null, "Courier hover fixture has an active Courier, unowned Anchor and no map history");
            Move(new(4, viewport.Size.Y - 4)); await Frames();
            var anchor = ModelDb.Relic<Anchor>().ToMutable();
            var entry = new MerchantRelicEntry(anchor, player); entries[0] = entry;
            slot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_relic.tscn").Instantiate<NMerchantRelic>();
            shop.AddChild(slot); slot.Position = new(1390, 690); slot.Initialize(shop); slot.FillSlot(entry);
            _courierHoverOwner = slot; _courierHoverFocusCount = 0;
            fixture.Patch(AccessTools.Method(typeof(NMerchantSlot), "OnFocus"),
                postfix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(CourierRecordNativeFocus)));
            // The mounted merchant omits its Spine hand and inventory animation.
            // Native input, payment, acquisition, refill and tip restoration run.
            fixture.Patch(AccessTools.Method(typeof(NMerchantSlot), "TriggerMerchantHandToPointHere"),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            fixture.Patch(AccessTools.Method(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic)),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            relicInventory = new NRelicInventory(); set(global, nameof(NGlobalUi.RelicInventory), relicInventory);
            player.Gold = 0; await Frames(4);
            var point = slot.Hitbox.GetGlobalRect().GetCenter(); Move(point);
            await Until(Published, "native Courier relic hover publishes before the failed purchase");
            int initialFocuses = _courierHoverFocusCount;
            check(initialFocuses > 0, "Courier fixture begins through native OnFocus");
            var failedView = View(); string beforeFailed = signature();
            Click(point);
            await Until(() => Published() && !ReferenceEquals(View(), failedView),
                "synchronous insufficient-gold purchase restores its native-hover prediction without mouse movement");
            check(signature() == beforeFailed && ReferenceEquals(entry.Model, anchor) && _courierHoverFocusCount == initialFocuses,
                "failed native purchase keeps stock, native state and RNG unchanged without another OnFocus");

            player.Gold = originalGold;
            await Until(Published, "same hover refreshes its affordable purchase inputs");
            CheckCourierSkin("before purchase");
            var beforePurchase = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
            var forecast = SurfaceOperationPreview.BuildShop(beforePurchase, PredictorLiveRunCapture.Id(entry));
            var expected = forecast.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                .Single(row => row.Action == OperationPreviewAction.Restock);
            int price = entry.Cost;
            Click(point);
            await Until(() => player.Relics.Contains(anchor) && !ReferenceEquals(entry.Model, anchor),
                "original mouse-release purchase obtains Anchor and refills the same entry");
            check(ReferenceEquals(slot.Entry, entry) && entry.Model?.Id.Entry == expected.Models.Single().Entry && player.Gold == originalGold - price,
                "actual Courier purchase preserves the slot and matches the predicted replacement and payment");
            string afterPurchase = signature();
            await Until(Published, "stationary native hover publishes the replacement relic's next-purchase preview");
            check(_courierHoverFocusCount == initialFocuses && signature() == afterPurchase,
                "replacement preview publishes without another native OnFocus and leaves purchased state/RNG unchanged");
            var current = PredictorOperationCapture.Read<PredictorOperationSnapshot>(Controller()!, "_snapshot");
            var capturedEntry = ((PredictorOperationShopScene)current.Scene).Entries.Single(item => item.Id == PredictorLiveRunCapture.Id(entry));
            check(capturedEntry.Key?.Entry == entry.Model!.Id.Entry && current.Fingerprint != beforePurchase.Fingerprint,
                "replacement preview captures current restocked identity rather than reusing the purchased offer");
            CheckCourierSkin("restored replacement at 1920");
            evidence = new { initialFocuses, finalFocuses = _courierHoverFocusCount, acquired = anchor.Id.Entry,
                replacement = entry.Model.Id.Entry, price, failedPurchaseRecovered = true, replacementPreviewRecovered = true,
                beforePurchase.Fingerprint, replacementFingerprint = current.Fingerprint };
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "shop-courier-hover-replacement.png"));

            Move(new(4, viewport.Size.Y - 4)); await Frames();
            viewport.Size = new(1280, 720); root.GetWindow().Size = viewport.Size;
            slot.Position = new(900, 440); await Frames(4);
            Move(slot.Hitbox.GetGlobalRect().GetCenter()); await Until(Published, "native replacement hover publishes at 1280");
            CheckCourierSkin("replacement at 1280");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "shop-courier-hover-replacement-1280.png"));
            Move(new(4, viewport.Size.Y - 4)); await Frames();
            viewport.Size = originalViewportSize; root.GetWindow().Size = originalWindowSize; slot.Hide(); await Frames();

            LocManager.Instance.SetLanguage("eng"); languagePreferences.SetLanguageOverride("en");
            slot.Position = new(1390, 690); slot.Show(); await Frames(4);
            Move(slot.Hitbox.GetGlobalRect().GetCenter()); await Until(Published, "native replacement hover publishes in English");
            CheckCourierSkin("English replacement at 1920");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "shop-courier-hover-replacement-en.png"));
            Move(new(4, viewport.Size.Y - 4)); slot.Hide(); await Frames();
            LocManager.Instance.SetLanguage(originalNativeLanguage); languagePreferences.SetLanguageOverride(originalUiLanguage);

            // The same native restoration contract serves card and potion slots.
            // A failed purchase covers their synchronous path without acquiring
            // inventory or bypassing their actual merchant callbacks.
            player.Gold = 0;
            foreach (bool card in new[] { true, false })
            {
                NMerchantSlot shared;
                if (card)
                {
                    var item = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_card.tscn").Instantiate<NMerchantCard>();
                    shop.AddChild(item); item.Position = new(840, 820); item.Initialize(shop); item.FillSlot(inventory.CharacterCardEntries[0]); shared = item;
                }
                else
                {
                    var item = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_potion.tscn").Instantiate<NMerchantPotion>();
                    shop.AddChild(item); item.Position = new(1400, 760); item.Initialize(shop); item.FillSlot(inventory.PotionEntries[0]); shared = item;
                }
                try
                {
                    _courierHoverOwner = shared; _courierHoverFocusCount = 0; await Frames(4);
                    var sharedPoint = shared.Hitbox.GetGlobalRect().GetCenter(); Move(sharedPoint);
                    await Until(() => PublishedFor(shared), "native " + shared.GetType().Name + " publishes its failed-purchase premise");
                    var previousView = View(); int focuses = _courierHoverFocusCount; string sharedBefore = signature();
                    Click(sharedPoint);
                    await Until(() => PublishedFor(shared) && !ReferenceEquals(previousView, View()),
                        "stationary " + shared.GetType().Name + " resumes after its synchronous failed purchase");
                    check(_courierHoverFocusCount == focuses && signature() == sharedBefore,
                        shared.GetType().Name + " native restoration requires no new focus and changes no run state or RNG");
                }
                finally { Move(new(4, viewport.Size.Y - 4)); NeowOperationHoverController.Leave(shared); shared.Free(); }
                await Frames();
            }
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            Move(new(4, viewport.Size.Y - 4));
            viewport.Size = originalViewportSize; root.GetWindow().Size = originalWindowSize;
            LocManager.Instance.SetLanguage(originalNativeLanguage); languagePreferences.SetLanguageOverride(originalUiLanguage);
            if (slot != null) NeowOperationHoverController.Leave(slot);
            _courierHoverOwner = null; fixture.UnpatchAll(fixture.Id);
            slot?.Free(); entries[0] = originalEntry;
            set(global, nameof(NGlobalUi.RelicInventory), originalInventoryUi); relicInventory?.Free();
            foreach (var relic in player.Relics.Where(relic => !originalRelics.Contains(relic)).ToArray())
                player.RemoveRelicInternal(relic, silent: true);
            player.Gold = originalGold;
            player.PlayerRng.LoadFromSerializable(originalRng); run.Rng.LoadFromSerializable(originalRunRng);
            player.RelicGrabBag.LoadFromSerializable(originalPersonalBag); run.SharedRelicGrabBag.LoadFromSerializable(originalSharedBag);
            player.DiscoveredCards = originalDiscoveredCards.ToList();
            bool restored = signature() == original;
            if (!restored)
            {
                File.WriteAllText(Path.Combine(output, "shop-courier-fixture-before.json"), original);
                File.WriteAllText(Path.Combine(output, "shop-courier-fixture-after.json"), signature());
            }
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = passed && restored, evidence, restored, failure,
                nativeMouseInput = true, actualPurchase = true, ownerRuntimeAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
            if (passed) check(restored, "Courier hover regression restores native player state, RNG, bags and merchant entry");
        }
    }
}
