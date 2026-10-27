using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task RunShopOrreryRerollChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, RunState run, Player player, NGlobalUi global, NOverlayStack overlays,
        Func<string> signature, Action<bool, string> check,
        Func<string, Control, Control, string, Func<PredictorOperationScene, bool>?, Func<OperationPreview, bool>,
            Action<NeowOperationOverlay, OperationPreview>?, bool, Task> exercise,
        Action<object, string, object?> set, bool sacrifice = false, bool normalPurchase = false)
    {
        string action = normalPurchase ? "NORMAL" : sacrifice ? "SACRIFICE" : "REROLL";
        string caseName = normalPurchase ? "shop-orrery-normal-purchase" : sacrifice ? "shop-orrery-sacrifice" : "shop-orrery-reroll";
        string receipt = Path.Combine(output, caseName + "-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        string original = signature();
        var originalRelics = player.Relics.ToArray();
        var nativeRelicList = PredictorOperationCapture.Read<List<RelicModel>>(player, "_relics");
        Driftwood? omittedDriftwood = null;
        int originalDriftwoodIndex = -1;
        var originalDiscoveredCards = player.DiscoveredCards.ToArray();
        var originalDiscoveredRelics = player.DiscoveredRelics.ToArray();
        var originalCards = PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").ToArray();
        int originalGold = player.Gold;
        var originalRng = player.PlayerRng.ToSerializable();
        var originalOdds = player.PlayerOdds.ToSerializable();
        var originalRunRng = run.Rng.ToSerializable();
        var originalPersonalBag = player.RelicGrabBag.ToSerializable();
        var originalSharedBag = run.SharedRelicGrabBag.ToSerializable();
        var history = PredictorOperationCapture.Read<List<List<MapPointHistoryEntry>>>(run, "_mapPointHistory");
        var originalHistory = history.Select(act => act.ToArray()).ToArray();
        var originalInventoryUi = global.RelicInventory;
        var relicInventory = new NRelicInventory();
        var visualFixture = new Harmony("rt2.shop.orrery.reroll.native.visuals");
        MerchantRoom? merchant = null;
        NMerchantInventory? shop = null;
        NRewardsScreen? rewardScreen = null;
        NCardRewardSelectionScreen? cardScreen = null;
        RewardsSet? rewards = null;
        Task<bool>? purchase = null, selecting = null;
        bool passed = false;
        string? failure = null;
        object? evidence = null;
        PaelsWing? nativeWing = null;
        async Task Frames(int count = 3)
        { for (int frame = 0; frame < count; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
            check(ready(), reason);
        }
        void SelectAlternative(NCardRewardSelectionScreen screen, string option)
        {
            var alternatives = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(screen, "_extraOptions");
            int index = alternatives.ToList().FindIndex(alternative => alternative.OptionId.Equals(option, StringComparison.OrdinalIgnoreCase));
            check(index >= 0, "original Orrery card picker exposes " + option);
            AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected").Invoke(screen, [index]);
        }
        try
        {
            check(player.Relics.OfType<TheCourier>().Any() && player.Relics.OfType<Driftwood>().Any() &&
                !player.Relics.OfType<Orrery>().Any(), "native Orrery fixture starts with Courier and Driftwood, without owned Orrery");
            if (sacrifice)
            {
                // The vanilla picker supports at most two alternatives total:
                // Skip plus Sacrifice is legal; Skip/Reroll/Sacrifice is not.
                // Configure this isolated fixture without Driftwood, preserving
                // the original model (including its hook-removal flag) untouched.
                omittedDriftwood = player.Relics.OfType<Driftwood>().Single();
                originalDriftwoodIndex = nativeRelicList.IndexOf(omittedDriftwood);
                nativeRelicList.Remove(omittedDriftwood);
                nativeWing = (PaelsWing)ModelDb.Relic<PaelsWing>().ToMutable();
                nativeWing.RewardsSacrificed = 1; // Reachable history: one earlier sacrificed reward.
                player.AddRelicInternal(nativeWing, silent: true);
            }
            set(global, nameof(NGlobalUi.RelicInventory), relicInventory);
            // The isolated host omits tutorial modals, as with the existing
            // obtain-relic FTUE fixture. Keep the original reward picker live.
            SaveManager.Instance.MarkFtueAsComplete("power_card_ftue");
            visualFixture.Patch(AccessTools.Method(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic)),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            // The real normal merchant's third relic slot is Shop rarity. Only
            // permute its already available legal Shop bag member, then run
            // the normal inventory generator and original purchase entry.
            var shopBag = PredictorOperationCapture.Read<Dictionary<RelicRarity, List<RelicModel>>>(player.RelicGrabBag, "_deques")[RelicRarity.Shop];
            var canonicalOrrery = shopBag.OfType<Orrery>().Single();
            check(canonicalOrrery.IsAllowedInShops && canonicalOrrery.IsAllowed(run), "Orrery is a legal available Shop-bag member");
            shopBag.Remove(canonicalOrrery); shopBag.Add(canonicalOrrery);
            merchant = new MerchantRoom(); run.PushRoom(merchant); set(merchant, "_runState", run);
            run.AppendToMapPointHistory(MapPointType.Shop, RoomType.Shop, null);
            player.Gold = 999;
            var inventory = MerchantInventory.CreateForNormalMerchant(player); merchant.Inventories.Add(inventory);
            var entry = inventory.RelicEntries[2];
            check(entry.Model is Orrery, "normal native merchant generation supplies Orrery in its Shop-rarity slot");
            long entryId = PredictorLiveRunCapture.Id(entry);
            if (normalPurchase)
            {
                void RecordPurchaseForecast(string stage, PredictorOperationSnapshot captured, OperationPreview forecast)
                {
                    var scene = (PredictorOperationShopScene)captured.Scene;
                    var outcome = PredictorOperation.Preview(captured, new BuyShopEntry(entryId, scene.Revision));
                    File.WriteAllText(Path.Combine(output, caseName + "-" + stage + "-forecast.json"),
                        JsonSerializer.Serialize(new { stage, entryId, scene, outcome.Disposition, phase = outcome.Waiting.Phase,
                            outcome.ShopOffers, forecast.Notices, forecast.PendingReasons, forecast.Branches,
                            faults = forecast.Faults.Select(fault => new { fault.Stage, exception = fault.Exception.ToString() }) },
                            new JsonSerializerOptions { WriteIndented = true }));
                }
                // Capture correctly treats a merchant room with no inventory
                // UI as closed. Preserve that diagnostic, then mount the same
                // native open shop/slot used by the existing Mirror fixture.
                // Do not synthesize an open captured scene or submit OpenShop.
                var closedPurchase = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                RecordPurchaseForecast("before-open", closedPurchase, SurfaceOperationPreview.BuildShop(closedPurchase, entryId));
                shop = new NMerchantInventory(); _neowStaticSceneNodes.Add(shop);
                set(shop, nameof(NMerchantInventory.Inventory), inventory); set(shop, nameof(NMerchantInventory.IsOpen), true);
                game.AddChild(shop); game.MoveChild(shop, 0); shop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                var slot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_relic.tscn").Instantiate<NMerchantRelic>();
                shop.AddChild(slot); slot.Position = new(1390, 690); slot.Initialize(shop); slot.FillSlot(entry);
                visualFixture.Patch(AccessTools.Method(typeof(NMerchantSlot), "TriggerMerchantHandToPointHere"),
                    prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
                await Frames(4);
                var beforePurchase = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                var buyingScene = (PredictorOperationShopScene)beforePurchase.Scene;
                string beforePrediction = signature();
                var beforePreview = SurfaceOperationPreview.BuildShop(beforePurchase, entryId);
                RecordPurchaseForecast("purchase-time", beforePurchase, beforePreview);
                check(buyingScene.InventoryOpen, "purchase-time capture observes the actual mounted open merchant inventory");
                var beforeRestock = beforePreview.Branches.SelectMany(branch => branch.Sections)
                    .Single(section => section.Source.Entry == "THE_COURIER");
                var expected = beforeRestock.Rows.Single();
                check(beforePreview.Faults.IsEmpty && beforePreview.PendingReasons.IsEmpty &&
                    expected.Action == OperationPreviewAction.Restock && expected.Models.Length == 1 && expected.Cards.IsEmpty &&
                    beforeRestock.Label == "OrreryPurchaseRestockNoReroll" && expected.Price is > 0 &&
                    beforePreview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).Count() == 5,
                    "purchase-time Orrery preview preserves its five groups and gives a concrete priced restock conditional on no rerolls");
                var noAlternatives = beforePurchase with { State = beforePurchase.State with {
                    Relics = beforePurchase.State.Relics.Where(relic => relic.Key.Entry is not ("DRIFTWOOD" or "PAELS_WING")).ToImmutableArray() } };
                foreach (var variant in new[] { (Driftwood: false, Wing: false, Label: (string?)null),
                    (Driftwood: false, Wing: true, Label: (string?)"OrreryPurchaseRestockNoSacrifice"),
                    (Driftwood: true, Wing: true, Label: (string?)"OrreryPurchaseRestockNoAlternatives") })
                {
                    var state = noAlternatives.State;
                    foreach (string key in (variant.Driftwood ? new[] { "DRIFTWOOD" } : Array.Empty<string>())
                        .Concat(variant.Wing ? new[] { "PAELS_WING" } : Array.Empty<string>()))
                        state = state with { NextInstanceId = state.NextInstanceId + 1,
                            Relics = state.Relics.Add(new(state.NextInstanceId, new("RELIC", key), run.TotalFloor)) };
                    var variantPreview = SurfaceOperationPreview.BuildShop(noAlternatives with { State = state }, entryId);
                    var variantRestock = variantPreview.Branches.SelectMany(branch => branch.Sections)
                        .Single(section => section.Source.Entry == "THE_COURIER");
                    check(variantPreview.Faults.IsEmpty && variantPreview.PendingReasons.IsEmpty && variantRestock.Label == variant.Label &&
                        variantRestock.Rows.Single().Action == OperationPreviewAction.Restock &&
                        variantRestock.Rows.Single().Models.SequenceEqual(expected.Models) && variantRestock.Rows.Single().Cards.IsEmpty &&
                        variantRestock.Rows.Single().Price == expected.Price,
                        "detached purchase-time alternative availability qualifies only the ordinary completion assumption and preserves the restock");
                }
                check(signature() == beforePrediction, "purchase-time Orrery forecasts do not buy or mutate the real run/RNG");
                // The raw host overlay is constructed after the earlier shop
                // fixture, without its scene defaults. Its empty full-screen
                // root must not catch the underlying merchant's mouse motion.
                // Restore it before opening actual reward overlays.
                check(overlays.ScreenCount == 0, "purchase-time merchant hover starts with an actually empty overlay stack");
                var emptyOverlayMouseFilter = overlays.MouseFilter;
                void RecordHoverSurface(string stage)
                {
                    var hovered = shop.GetViewport().GuiGetHoveredControl();
                    var controller = Descendants(root).OfType<NeowOperationHoverController>().FirstOrDefault();
                    var capturedOwner = controller == null ? null : PredictorOperationCapture.ReadNullable(controller, "_owner") as Control;
                    var point = slot.Hitbox.GetGlobalRect().GetCenter();
                    File.WriteAllText(Path.Combine(output, caseName + "-" + stage + "-hover.json"), JsonSerializer.Serialize(new {
                        stage, overlayCount = overlays.ScreenCount, overlayMouseFilter = overlays.MouseFilter.ToString(),
                        originalOverlayMouseFilter = emptyOverlayMouseFilter.ToString(),
                        slot = slot.GetPath().ToString(), hitbox = slot.Hitbox.GetGlobalRect().ToString(),
                        hovered = hovered?.GetPath().ToString(), controllerOwner = capturedOwner?.GetPath().ToString(),
                        hitControls = Descendants(root).OfType<Control>().Where(control => control.IsVisibleInTree() &&
                            control.MouseFilter != Control.MouseFilterEnum.Ignore && control.GetGlobalRect().HasPoint(point))
                            .Select(control => new { path = control.GetPath().ToString(), filter = control.MouseFilter.ToString(), control.ZIndex,
                                rect = control.GetGlobalRect().ToString() }).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
                }
                RecordHoverSurface("before-ignore-empty-overlay");
                try
                {
                    overlays.MouseFilter = Control.MouseFilterEnum.Ignore;
                    await Frames(1);
                    await exercise(caseName, slot, slot.Hitbox, "ORRERY",
                    captured => captured is PredictorOperationShopScene s && s.Entries.Any(offer => offer.Id == entryId && offer.Key?.Entry == "ORRERY"),
                    preview => preview.PendingReasons.IsEmpty && preview.Branches.SelectMany(branch => branch.Sections)
                        .Any(section => section.Source.Entry == "THE_COURIER" && section.Label == "OrreryPurchaseRestockNoReroll" &&
                            section.Rows.Single().Action == OperationPreviewAction.Restock && section.Rows.Single().Models.SequenceEqual(expected.Models) &&
                            section.Rows.Single().Price == expected.Price),
                    (view, _) =>
                    {
                        var text = Descendants(view).OfType<Label>().Select(label => label.Text).ToArray();
                        check(text.Any(value => value == "补货") && !text.Any(value => value == "获得") &&
                            text.Any(value => value == expected.Price!.Value.ToString()) &&
                            !Descendants(view).OfType<Label>().Any(label => label.Text.Contains("领取效果完成后才会补货", StringComparison.Ordinal)),
                            "native Orrery purchase hover says restock, retains its price and removes the obsolete awaiting-choice hint");
                    }, false);
                    RecordHoverSurface("after-native-hover");
                }
                catch
                {
                    RecordHoverSurface("failed-native-hover");
                    await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    shop.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, caseName + "-failed-native-hover.png"));
                    throw;
                }
                finally { overlays.MouseFilter = emptyOverlayMouseFilter; }
                purchase = entry.OnTryPurchaseWrapper(inventory);
                await Until(() => overlays.Peek() is NRewardsScreen || purchase.IsCompleted,
                    "normal Orrery purchase opens its original card rewards");
                if (purchase.IsFaulted) throw new InvalidOperationException("Normal Orrery original purchase failed", purchase.Exception);
                check(!purchase.IsCompleted && overlays.Peek() is NRewardsScreen, "normal Orrery purchase waits at its five real rewards");
                rewardScreen = (NRewardsScreen)overlays.Peek()!;
                rewards = PredictorOperationCapture.Read<RewardsSet>(rewardScreen, "_rewardsSet");
                check(rewards.Rewards.OfType<CardReward>().Count() == 5, "normal Orrery purchase really offers five groups");
                RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
                if (GodotObject.IsInstanceValid(rewardScreen) && rewardScreen.GetParent() != null) overlays.Remove(rewardScreen);
                await Until(() => purchase.IsCompleted, "ordinary completion resumes the real merchant purchase");
                check(purchase.IsCompletedSuccessfully && await purchase && entry.Model?.Id.Entry == expected.Models.Single().Entry &&
                    entry.Cost == expected.Price,
                    "purchase-time restock identity and price match the native original purchase with ordinary skipping and no extra random actions");
                evidence = new { entryId, purchaseTimeRestock = expected.Models.Single().Entry, actualRestock = entry.Model?.Id.Entry,
                    expectedPrice = expected.Price, actualPrice = entry.Cost, ordinaryCompletionOnly = true,
                    purchaseSceneRevision = buyingScene.Revision, detachedAlternativeLabels = true };
                passed = true;
                return;
            }
            purchase = entry.OnTryPurchaseWrapper(inventory);
            await Until(() => overlays.Peek() is NRewardsScreen || purchase.IsCompleted,
                "original Orrery merchant purchase naturally opens its reward set");
            if (purchase.IsFaulted) throw new InvalidOperationException("Orrery original purchase failed", purchase.Exception);
            check(!purchase.IsCompleted && overlays.Peek() is NRewardsScreen, "Orrery purchase remains pending at its real child rewards");
            rewardScreen = (NRewardsScreen)overlays.Peek()!;
            rewards = PredictorOperationCapture.Read<RewardsSet>(rewardScreen, "_rewardsSet");
            check(rewards.Rewards.OfType<CardReward>().Count() == 5, "original Orrery supplies five real card rewards");
            var reward = rewards.Rewards.OfType<CardReward>().First();
            selecting = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
            await Until(() => overlays.Peek() is NCardRewardSelectionScreen || selecting.IsCompleted,
                "real Orrery reward selection opens its original card picker");
            if (selecting.IsFaulted) throw new InvalidOperationException("Orrery original reward selection failed", selecting.Exception);
            check(!selecting.IsCompleted && overlays.Peek() is NCardRewardSelectionScreen, "native Orrery card picker awaits player input");
            cardScreen = (NCardRewardSelectionScreen)overlays.Peek()!;
            await Frames(40);
            var snapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
            check(snapshot.Scene is PredictorOperationObtainScene { Source.Entry: "ORRERY", Kind: PredictorOperationObtainChildKind.Rewards,
                    Parent: PredictorOperationShopScene { PendingPurchase: { } pending } } && pending.EntryId == entryId,
                "capture observes the original suspended Orrery purchase ancestry and exact merchant entry without a fabricated parent");
            var obtain = (PredictorOperationObtainScene)snapshot.Scene;
            long rewardId = PredictorLiveRunCapture.Id(reward);
            long? wingId = nativeWing == null ? null : PredictorLiveRunCapture.Id(nativeWing);
            var predicted = SurfaceOperationPreview.BuildRewardAlternative(snapshot, rewardId, action, wingId);
            var restock = predicted.Branches.SelectMany(branch => branch.Sections).Single(section => section.Source.Entry == "THE_COURIER");
            var expectedKey = restock.Rows.Single().Models.Single();
            string[] predictedCards = sacrifice ? [] : predicted.Branches.SelectMany(branch => branch.Sections)
                .Where(section => section.Source.Entry == "DRIFTWOOD").SelectMany(section => section.CardGroups).First()
                .Cards.Select(card => card.Key.Entry).ToArray();
            if (!sacrifice) check(predicted.Branches.SelectMany(branch => branch.Sections)
                .Where(section => section.Source.Entry == "DRIFTWOOD").SelectMany(section => section.CardGroups).Count() == 5,
                "Orrery reroll retains the immediate cards and includes all five consecutive reward opportunities");
            var alternatives = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(cardScreen, "_extraOptions");
            int rerollIndex = alternatives.ToList().FindIndex(alternative => alternative.OptionId == action);
            check(rerollIndex >= 0, "original native Orrery picker exposes its actual " + action + " control");
            var reroll = Descendants(cardScreen).OfType<NCardRewardAlternativeButton>().ElementAt(rerollIndex);
            await exercise(caseName, reroll, reroll, sacrifice ? "PAELS_WING" : "DRIFTWOOD",
                captured => captured is PredictorOperationObtainScene { Source.Entry: "ORRERY", Kind: PredictorOperationObtainChildKind.Rewards,
                    Parent: PredictorOperationShopScene { PendingPurchase: { } parent } } && parent.EntryId == entryId,
                preview => preview.OptionKey == action && preview.PendingReasons.IsEmpty &&
                    (sacrifice || preview.Branches.SelectMany(branch => branch.Sections).Any(section => section.Source.Entry == "DRIFTWOOD" &&
                        section.CardGroups.Any(group => group.Cards.Length == 3))) &&
                    preview.Branches.SelectMany(branch => branch.Sections).Any(section => section.Source.Entry == "THE_COURIER" &&
                        section.Label == (sacrifice ? "OrreryFinalSacrificeRestock" : "OrreryFinalRerollRestock") &&
                        section.Style?.Text == OperationTextMode.GroupRows &&
                        section.CardGroups.IsEmpty && section.Rows.Length == 1 &&
                        section.Rows[0].Action == OperationPreviewAction.Restock &&
                        section.Rows[0].Models.SequenceEqual([expectedKey]) && section.Rows[0].Cards.IsEmpty &&
                        section.Rows[0].Label == null),
                (view, _) =>
                {
                    var restockAction = Descendants(view).OfType<Label>().Single(label => label.Text == "补货");
                    var rowLabels = Descendants(restockAction.GetParent()).OfType<Label>().Select(label => label.Text).ToArray();
                    check(Descendants(view).OfType<Label>().Any(label => label.Text.Contains(
                        sacrifice ? "献祭" : "最后一次", StringComparison.Ordinal)) &&
                        rowLabels.Any(text => text == "补货") && !rowLabels.Any(text => text == "获得") &&
                        !Descendants(view).OfType<Label>().Any(label => label.Text.Contains(" G", StringComparison.Ordinal)),
                        "Orrery alternative hover names the identity-only conditional restock as restock without a price");
                }, false);
            // Same real Orrery child rewards, intentionally without merchant
            // ancestry, model an external reward surface. These negative cases
            // are snapshot variants, never evidence of native parent capture.
            var external = snapshot with { Scene = obtain.RewardScene! };
            var noCourier = snapshot with { State = snapshot.State with {
                Relics = snapshot.State.Relics.Where(relic => relic.Key.Entry != "THE_COURIER").ToImmutableArray() } };
            foreach (var variant in new[] { external, noCourier })
            {
                var preview = SurfaceOperationPreview.BuildRewardAlternative(variant, rewardId, action, wingId);
                check(preview.Faults.IsEmpty && preview.Branches.SelectMany(branch => branch.Sections).All(section => section.Source.Entry != "THE_COURIER") &&
                    (sacrifice || preview.Branches.SelectMany(branch => branch.Sections).Any(section => section.CardGroups.Any(group => group.Cards.Length == 3))),
                    "external rewards or absent Courier retain reroll cards without leaking a merchant restock identity");
            }
            string? wingQualification = null;
            if (!sacrifice)
            {
                // This legacy conditional-label check models only the current
                // reroll opportunity. Future dual-relic pickers are prohibited
                // by vanilla's two-alternative limit, not another valid path.
                var withWing = snapshot with { Scene = obtain with { RewardScene = obtain.RewardScene! with {
                    Rewards = obtain.RewardScene!.Rewards.Select(row => row.Id == rewardId ? row : row with {
                        CanReroll = false, HasRerolled = true }).ToImmutableArray() } }, State = snapshot.State with {
                    NextInstanceId = snapshot.State.NextInstanceId + 1,
                    Relics = snapshot.State.Relics.Add(new(snapshot.State.NextInstanceId, new("RELIC", "PAELS_WING"), run.TotalFloor)) } };
                var wingPreview = SurfaceOperationPreview.BuildRewardAlternative(withWing, rewardId, "REROLL", null);
                var wingRestock = wingPreview.Branches.SelectMany(branch => branch.Sections)
                    .Single(section => section.Source.Entry == "THE_COURIER");
                wingQualification = wingRestock.Label;
                check(wingPreview.Faults.IsEmpty && wingRestock.Label == "OrreryFinalRerollRestockNoSacrifice" &&
                    wingRestock.Rows.Single().Models.SequenceEqual([expectedKey]) && wingRestock.Rows.Single().Label == null &&
                    wingPreview.Branches.SelectMany(branch => branch.Sections).Where(section => section.Source.Entry == "DRIFTWOOD")
                        .SelectMany(section => section.CardGroups).Single().Cards.Select(card => card.Key.Entry).SequenceEqual(predictedCards),
                    "Pael's Wing snapshot adds the no-more-reroll-or-sacrifice qualification while preserving restock identity and reroll cards");
            }
            string? expectedSacrificeRelic = null;
            if (sacrifice)
            {
                var withDriftwood = snapshot with { State = snapshot.State with {
                    NextInstanceId = snapshot.State.NextInstanceId + 1,
                    Relics = snapshot.State.Relics.Add(new(snapshot.State.NextInstanceId, new("RELIC", "DRIFTWOOD"), run.TotalFloor)) } };
                var driftwoodPreview = SurfaceOperationPreview.BuildRewardAlternative(withDriftwood, rewardId, action, wingId);
                var driftwoodRestock = driftwoodPreview.Branches.SelectMany(branch => branch.Sections)
                    .Single(section => section.Source.Entry == "THE_COURIER");
                wingQualification = driftwoodRestock.Label;
                check(driftwoodPreview.Faults.IsEmpty && driftwoodRestock.Label == "OrreryFinalSacrificeRestockNoReroll" &&
                    driftwoodRestock.Rows.Single().Models.SequenceEqual([expectedKey]) && driftwoodRestock.Rows.Single().Label == null,
                    "detached Driftwood-plus-Wing composition qualifies sacrifice restock without claiming the vanilla three-alternative picker supports it");
                var sacrificed = PredictorOperation.Preview(snapshot, new SacrificeCardReward(rewardId, wingId!.Value));
                check(sacrificed.Disposition == PredictorInputResult.Accepted && sacrificed.Waiting.Phase == PredictorPhase.Rewards,
                    "native sacrifice fixture's second sacrifice has a fully settled original relic obtain");
                expectedSacrificeRelic = sacrificed.State.Relics.Single(relic => !snapshot.State.Relics.Any(prior => prior.Id == relic.Id)).Key.Entry;
                var firstSacrifice = snapshot with { State = snapshot.State with {
                    Relics = snapshot.State.Relics.Select(relic => relic.Id == wingId ? relic with { RewardsSacrificed = 0 } : relic).ToImmutableArray() } };
                var oddResult = PredictorOperation.Preview(firstSacrifice, new SacrificeCardReward(rewardId, wingId.Value));
                var oddSettled = PredictorOperation.Preview(firstSacrifice,
                    [new SacrificeCardReward(rewardId, wingId.Value), new LeaveRewards()]);
                var oddPreview = SurfaceOperationPreview.BuildRewardAlternative(firstSacrifice, rewardId, action, wingId);
                check(oddResult.Disposition == PredictorInputResult.Accepted && oddResult.State.Relics.Length == firstSacrifice.State.Relics.Length &&
                    oddSettled.Disposition == PredictorInputResult.Accepted && oddSettled.Waiting.Phase == PredictorPhase.Shop &&
                    oddPreview.Faults.IsEmpty && oddPreview.Branches.SelectMany(branch => branch.Sections)
                        .Any(section => section.Source.Entry == "THE_COURIER" && section.Rows.Single().Models
                            .SequenceEqual([oddSettled.ShopOffers.Single(offer => offer.EntryId == entryId).Key!.Value]) && section.Rows.Single().Label == null),
                    "first odd sacrifice predicts eventual restock without inventing a newly obtained sacrifice relic");
                var lastReward = snapshot with { Scene = obtain with { RewardScene = obtain.RewardScene! with {
                    Rewards = obtain.RewardScene!.Rewards.Select(row => row.Id == rewardId ? row : row with {
                        Status = PredictorRewardStatus.Skipped, CardSelectionEntered = false }).ToImmutableArray() } } };
                var lastResult = PredictorOperation.Preview(lastReward, new SacrificeCardReward(rewardId, wingId.Value));
                var lastPreview = SurfaceOperationPreview.BuildRewardAlternative(lastReward, rewardId, action, wingId);
                check(lastResult.Disposition == PredictorInputResult.Accepted && lastResult.Waiting.Phase == PredictorPhase.Shop &&
                    lastPreview.Faults.IsEmpty && lastPreview.Branches.SelectMany(branch => branch.Sections)
                        .Any(section => section.Source.Entry == "THE_COURIER" && section.Rows.Single().Models
                            .SequenceEqual([lastResult.ShopOffers.Single(offer => offer.EntryId == entryId).Key!.Value])),
                    "sacrificing the final open Orrery reward already returns to Shop and projects restock without an extra LeaveRewards");
                SelectAlternative(cardScreen, action);
                await Until(() => selecting.IsCompleted, "original native SACRIFICE callback completes its actual relic obtain");
                if (selecting.IsFaulted) throw new InvalidOperationException("Orrery original sacrifice failed", selecting.Exception);
                check(selecting.IsCompletedSuccessfully && nativeWing!.RewardsSacrificed == 2 &&
                    player.Relics.Any(relic => !snapshot.State.Relics.Any(prior => prior.Id == PredictorLiveRunCapture.Id(relic)) && relic.Id.Entry == expectedSacrificeRelic),
                    "original second sacrifice increments the counter and obtains exactly the predicted native relic");
                await Frames(65); // Let the original asynchronous counter flash finish before removing fixture relics.
            }
            else
            {
                SelectAlternative(cardScreen, "REROLL");
                await Until(() => !reward.CanReroll, "original native REROLL callback rerolls the current Orrery reward once");
                await Frames(4);
                check(reward.Cards.Select(card => card.Id.Entry).SequenceEqual(predictedCards),
                    "existing REROLL card prediction matches the actual original reroll cards");
                SelectAlternative(cardScreen, "Skip");
                await Until(() => selecting.IsCompleted, "ordinary skip closes the actual rerolled card selection");
                check(selecting.IsCompletedSuccessfully, "native Orrery skip finishes without a hidden selection fault");
            }
            RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            if (GodotObject.IsInstanceValid(rewardScreen) && rewardScreen.GetParent() != null) overlays.Remove(rewardScreen);
            await Until(() => purchase.IsCompleted, "ordinary reward-set finish resumes the original merchant restock");
            check(purchase.IsCompletedSuccessfully && await purchase && entry.Model?.Id.Entry == expectedKey.Entry,
                "conditional final-action restock identity equals the actual original merchant replacement after reward completion");
            evidence = new { entryId, rewardId, nativeParent = snapshot.Scene.GetType().Name,
                expectedRestock = expectedKey.Entry, actualRestock = entry.Model?.Id.Entry,
                predictedCards = sacrifice ? null : predictedCards,
                actualCards = sacrifice ? null : reward.Cards.Select(card => card.Id.Entry).ToArray(),
                externalWithoutShop = true, noCourier = true, wingQualification, expectedSacrificeRelic,
                sacrificeCount = nativeWing?.RewardsSacrificed, priceDisplayed = false };
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            if (cardScreen != null && GodotObject.IsInstanceValid(cardScreen) && selecting is { IsCompleted: false })
            {
                PredictorOperationCapture.Read<TaskCompletionSource<int?>>(cardScreen, "_completionSource").TrySetResult(null);
                await Until(() => selecting.IsCompleted, "Orrery fixture cleanup ends its original pending selection");
            }
            if (rewards != null && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(rewards))
                RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            if (rewardScreen != null && GodotObject.IsInstanceValid(rewardScreen) && rewardScreen.GetParent() != null)
                overlays.Remove(rewardScreen);
            if (purchase is { IsCompleted: false }) await Until(() => purchase.IsCompleted, "Orrery fixture cleanup unwinds its original purchase");
            if (purchase?.IsFaulted == true) _ = purchase.Exception;
            shop?.Free();
            visualFixture.UnpatchAll(visualFixture.Id);
            set(global, nameof(NGlobalUi.RelicInventory), originalInventoryUi); relicInventory.Free();
            foreach (var relic in player.Relics.Where(relic => !originalRelics.Contains(relic)).ToArray()) player.RemoveRelicInternal(relic, silent: true);
            if (omittedDriftwood != null && !nativeRelicList.Contains(omittedDriftwood))
                nativeRelicList.Insert(originalDriftwoodIndex, omittedDriftwood);
            foreach (var card in PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").Where(card => !originalCards.Contains(card)).ToArray())
                run.RemoveCard(card);
            player.Gold = originalGold;
            set(player, nameof(Player.DiscoveredCards), originalDiscoveredCards.ToList());
            set(player, nameof(Player.DiscoveredRelics), originalDiscoveredRelics.ToList());
            player.PlayerRng.LoadFromSerializable(originalRng); player.PlayerOdds.LoadFromSerializable(originalOdds);
            run.Rng.LoadFromSerializable(originalRunRng);
            player.RelicGrabBag.LoadFromSerializable(originalPersonalBag); run.SharedRelicGrabBag.LoadFromSerializable(originalSharedBag);
            if (merchant != null && ReferenceEquals(run.CurrentRoom, merchant)) run.PopCurrentRoom();
            history.Clear(); foreach (var act in originalHistory) history.Add(act.ToList());
            bool restored = signature() == original;
            if (!restored)
            {
                File.WriteAllText(Path.Combine(output, caseName + "-restore-before.json"), original);
                File.WriteAllText(Path.Combine(output, caseName + "-restore-after.json"), signature());
            }
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = passed && restored, evidence, restored, failure,
                source = "Controlled legal Shop bag permutation, original normal merchant inventory, original Orrery purchase/rewards/" +
                    (normalPurchase ? "ordinary skip completion/restock. Actual purchase-time native slot hover; priced conditional restock, no extra random action." :
                        "card picker/" + action + " callback/set completion/restock. Native capture must observe the parent purchase; no constructed parent substitutes for it. One actual alternative, identity-only conditional display.") +
                    " No natural-seed or Owner acceptance claim." },
                new JsonSerializerOptions { WriteIndented = true }));
            if (passed) check(restored, "Orrery " + action + " fixture restores player/RNG/odds/bags/history and removes native reward surfaces");
        }
    }
}
