using System.Collections.Immutable;
using RolltheSpire2.Infrastructure.Persistence;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.FamilyExecution;

internal static class PredictorShopChecks
{
    private static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException("SHOP_NATIVE " + why); }
    private static ModelKey Key(ModelId id) => new(id.Category, id.Entry);

    internal static void Run(PredictorContext context, RuntimeContextAuthoritySnapshot authority)
    {
        var manager = RunManager.Instance;
        var stateProperty = typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        object? saved = stateProperty.GetValue(manager);
        object? savedAscension = ascensionProperty.GetValue(manager);
        try
        {
            string seed = Enumerable.Range(0, 500).Select(i => VisibleSeedCandidateCodec.FormatOrdinal(
                    RolltheSpire2.Compatibility.Beta111Profile.Instance, (ulong)i))
                .First(s => PredictorSeedCapture.CreateRun(context with { Seed = s }, authority)
                    .Working.OpeningOptions.Any(k => k.Entry == "GOLDEN_PEARL"));
            var runtime = PredictorSeedCapture.CreateRun(context with { Seed = seed }, authority);
            runtime.EnterOpening();
            Check(runtime.Submit(runtime.Request!, new ChooseOpeningRelic(new("RELIC", "GOLDEN_PEARL"))) == PredictorInputResult.Accepted,
                "opening choice");
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
            var native = RunState.CreateForTest([player],
                runtime.Working.Acts.Select(a => ModelDb.GetById<ActModel>(new ModelId(a.Key.Category, a.Key.Entry))).ToArray(),
                seed: seed, ascensionLevel: context.Ascension);
            stateProperty.SetValue(manager, native);
            ascensionProperty.SetValue(manager, new AscensionManager(context.Ascension));
            native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(UnlockState.all), native.Rng.UpFront);
            player.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
            manager.GenerateRooms();
            player.RelicGrabBag.Remove(ModelDb.Relic<GoldenPearl>());
            native.SharedRelicGrabBag.Remove(ModelDb.Relic<GoldenPearl>());
            // TestMode skips potion price rolls. Compare the normal product
            // path by disabling it only around native inventory creation.
            bool testMode = TestMode.IsOn;
            MerchantInventory source;
            try
            {
                TestMode.IsOn = false;
                source = MerchantInventory.CreateForNormalMerchant(player);
            }
            finally { TestMode.IsOn = testMode; }
            var shopStart = runtime.Working;
            runtime.EnterShop(new(1, 2, "shop-native-a1-f2"));
            var actual = source.AllEntries.ToArray();
            var predicted = runtime.ShopEntries;
            Check(actual.Length == predicted.Length && predicted.Length == 14, "entry count");
            for (int i = 0; i < 7; i++)
            {
                var card = (MerchantCardEntry)actual[i];
                Check(predicted[i].Key == Key(card.CreationResult!.Card.Id), $"card identity {i}");
                Check(predicted[i].BaseCost == card.Cost, $"card base cost {i}: {predicted[i].BaseCost} vs {card.Cost}");
                Check(predicted[i].IsOnSale == card.IsOnSale, $"sale {i}");
            }
            for (int i = 7; i < 10; i++)
            {
                var relic = (MerchantRelicEntry)actual[i];
                Check(predicted[i].Key == Key(relic.Model!.Id), $"relic identity {i}");
                Check(predicted[i].BaseCost == relic.Cost, $"relic base cost {i}");
            }
            for (int i = 10; i < 13; i++)
            {
                var potion = (MerchantPotionEntry)actual[i];
                Check(predicted[i].Key == Key(potion.Model!.Id), $"potion identity {i}");
                Check(predicted[i].BaseCost == potion.Cost, $"potion base cost {i}");
            }
            Check(predicted[13].BaseCost == actual[13].Cost, "removal base cost");
            foreach (var (stream, nativeRng) in new[] { (PredictorStream.Shops, player.PlayerRng.Shops),
                (PredictorStream.Rewards, player.PlayerRng.Rewards) })
            {
                var p = runtime.Working.Streams.Single(s => s.Stream == stream);
                var n = nativeRng.ToSerializable();
                Check((p.S0, p.S1, p.S2, p.S3, p.Calls) ==
                    (n.state0, n.state1, n.state2, n.state3, n.counter), $"{stream} continuation");
            }
            var targetCharacter = runtime.Context.Catalog!.CharacterPools
                .First(p => p.Character != runtime.Context.Character).Character;
            var unlocked = new UnlockState(context.RevealedEpochs,
                context.SeenEncounters.Select(k => new ModelId(k.Category, k.Entry)), context.NumberOfRuns);
            var altPlayer = Player.CreateForNewRun<Ironclad>(unlocked, 1);
            var characterCards = (CharacterCards)ModelDb.Modifier<CharacterCards>().ToMutable();
            characterCards.CharacterModel = new ModelId(targetCharacter.Category, targetCharacter.Entry);
            var altNative = RunState.CreateForTest([altPlayer],
                runtime.Working.Acts.Select(a => ModelDb.GetById<ActModel>(new ModelId(a.Key.Category, a.Key.Entry))).ToArray(),
                [characterCards], seed: seed, ascensionLevel: context.Ascension);
            stateProperty.SetValue(manager, altNative);
            altNative.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(unlocked), altNative.Rng.UpFront);
            altPlayer.PopulateRelicGrabBagIfNecessary(altNative.Rng.UpFront);
            manager.GenerateRooms();
            altPlayer.RelicGrabBag.Remove(ModelDb.Relic<GoldenPearl>());
            altNative.SharedRelicGrabBag.Remove(ModelDb.Relic<GoldenPearl>());
            MerchantInventory modifiedInventory;
            testMode = TestMode.IsOn;
            try { TestMode.IsOn = false; modifiedInventory = MerchantInventory.CreateForNormalMerchant(altPlayer); }
            finally { TestMode.IsOn = testMode; stateProperty.SetValue(manager, native); }
            var modifiedRun = new PredictorRun(runtime.Context with
                { Modifiers = [new(new ModelKey("MODIFIER", "CHARACTER_CARDS"), targetCharacter)] }, shopStart);
            modifiedRun.EnterShop(new(1, 2, "shop-native-character-cards"));
            for (int i = 0; i < 7; i++)
            {
                var expected = (MerchantCardEntry)modifiedInventory.AllEntries.ElementAt(i);
                Check(modifiedRun.ShopEntries[i].Key == Key(expected.CreationResult!.Card.Id) &&
                    modifiedRun.ShopEntries[i].BaseCost == expected.Cost,
                    $"CharacterCards merchant slot {i}");
            }
            foreach (var (stream, nativeRng) in new[] { (PredictorStream.Shops, altPlayer.PlayerRng.Shops),
                (PredictorStream.Rewards, altPlayer.PlayerRng.Rewards) })
            {
                var p = modifiedRun.Working.Streams.Single(s => s.Stream == stream);
                var n = nativeRng.ToSerializable();
                Check((p.S0, p.S1, p.S2, p.S3, p.Calls) ==
                    (n.state0, n.state1, n.state2, n.state3, n.counter), $"CharacterCards {stream} continuation");
            }
            var hoarderRun = new PredictorRun(runtime.Context with
                { Modifiers = [new(new ModelKey("MODIFIER", "HOARDER"))] }, shopStart);
            hoarderRun.EnterShop(new(1, 2, "shop-controlled-hoarder"));
            Check(!hoarderRun.ShopEntries[^1].Stocked && !hoarderRun.ShopOffers[^1].Selectable,
                "Hoarder disables merchant removal entry");
            player.Gold = runtime.Working.GoldBudget;
            var firstCard = (MerchantCardEntry)actual[0];
            int runtimeDeckBefore = runtime.Working.Deck.Length;
            int goldBefore = runtime.Working.GoldBudget;
            Check(!runtime.ShopInventoryOpen && runtime.CanUseFoulPotionInShop &&
                !runtime.ShopOffers[0].Selectable, "closed shop targets merchant and hides purchase action");
            Check(runtime.Submit(runtime.Request!, new OpenShopInventory()) == PredictorInputResult.Accepted &&
                runtime.ShopInventoryOpen && !runtime.CanUseFoulPotionInShop, "open shop inventory");
            Check(runtime.Submit(runtime.Request!, new BuyShopEntry(predicted[0].Id, runtime.ShopInventoryRevision)) ==
                PredictorInputResult.Accepted, "runtime card purchase");
            Check(runtime.Working.GoldBudget == goldBefore - firstCard.Cost, "card purchase gold");
            Check(runtime.Working.Deck.Length == runtimeDeckBefore + 1, "card purchase deck count");
            Check(runtime.Working.Card(runtime.Working.Deck[^1]).Key == Key(firstCard.CreationResult!.Card.Id), "card purchase identity");
            Check(!runtime.ShopEntries[0].Stocked, "card slot cleared");
            Check(runtime.Submit(runtime.Request!, new CloseShopInventory()) == PredictorInputResult.Accepted,
                "close shop inventory");
            Check(runtime.Submit(runtime.Request!, new LeaveShop()) == PredictorInputResult.Accepted, "leave shop");
            Check(runtime.Phase == PredictorPhase.Complete && runtime.Checkpoints[^1].State.GoldBudget == runtime.Working.GoldBudget,
                "shop checkpoint");
            var parasol = new ModelKey("RELIC", "LORDS_PARASOL");
            var parasolState = runtime.Working with
            {
                Relics = runtime.Working.Relics.Add(new(runtime.Working.NextInstanceId, parasol, runtime.Working.Position.Floor)),
                NextInstanceId = runtime.Working.NextInstanceId + 1,
                PersonalBag = PredictorRelicBag.Empty(false)
            };
            var parasolRun = new PredictorRun(runtime.Context, parasolState);
            parasolRun.EnterShop(new(1, 3, "shop-controlled-parasol"));
            var beforeParasolStock = parasolRun.ShopEntries.Select(e => e.Key).ToImmutableArray();
            int parasolGold = parasolRun.Working.GoldBudget;
            int parasolDeck = parasolRun.Working.Deck.Length;
            parasolRun.BeginShopParasol();
            Check(parasolRun.Phase == PredictorPhase.Shop && parasolRun.ShopParasolActive &&
                parasolRun.ShopParasolCursor == 13 && parasolRun.ShopRemovalCandidates.Length > 0,
                "parasol ordered purchase/removal pause");
            Check(parasolRun.Working.GoldBudget == parasolGold && parasolRun.Working.Deck.Length == parasolDeck + 7,
                "parasol card order/free payment");
            Check(parasolRun.Submit(parasolRun.Request!, new CancelShopCardRemoval()) == PredictorInputResult.Invalid,
                "parasol forced removal cannot cancel");
            int removalCount = parasolRun.Working.CardShopRemovalsUsed;
            Check(parasolRun.Submit(parasolRun.Request!, new ChooseShopRemovalCard(parasolRun.ShopRemovalCandidates[0])) ==
                PredictorInputResult.Accepted, "parasol forced removal choice");
            Check(!parasolRun.ShopParasolActive && parasolRun.Working.CardShopRemovalsUsed == removalCount + 1 &&
                parasolRun.Working.GoldBudget == parasolGold, "parasol removal effect");
            Check(parasolRun.Submit(parasolRun.Request!, new LeaveShop()) == PredictorInputResult.Accepted,
                "parasol leave after forced removal");
            parasolRun.Rewind(0);
            Check(parasolRun.ShopEntries.IsEmpty && !parasolRun.ShopParasolActive &&
                parasolRun.Working.CardShopRemovalsUsed == removalCount, "parasol rewind residue");
            parasolRun.EnterShop(new(1, 3, "shop-controlled-parasol"));
            Check(parasolRun.ShopEntries.Select(e => e.Key).SequenceEqual(beforeParasolStock),
                "parasol rewind inventory continuation");
            var noRemovalState = parasolState with
            { Cards = parasolState.Cards.Select(c => c with { Removable = false }).ToImmutableArray() };
            var noRemovalRun = new PredictorRun(runtime.Context, noRemovalState);
            noRemovalRun.EnterShop(new(1, 4, "shop-controlled-parasol-empty-removal"));
            var stockField = typeof(PredictorRun).GetField("_shopEntries", BindingFlags.Instance | BindingFlags.NonPublic)!;
            stockField.SetValue(noRemovalRun, noRemovalRun.ShopEntries.Select(e =>
                e.Kind is PredictorShopEntryKind.CharacterCard or PredictorShopEntryKind.ColorlessCard
                    ? e with { Stocked = false } : e).ToImmutableArray());
            int unusedRemovalCount = noRemovalRun.Working.CardShopRemovalsUsed;
            noRemovalRun.BeginShopParasol();
            Check(!noRemovalRun.ShopParasolActive && noRemovalRun.ShopRemovalCandidates.IsEmpty &&
                noRemovalRun.ShopEntries[^1].Stocked &&
                noRemovalRun.Working.CardShopRemovalsUsed == unusedRemovalCount,
                "Parasol empty removal candidate returns false and leaves removal in stock");
            Check(noRemovalRun.Submit(noRemovalRun.Request!, new LeaveShop()) == PredictorInputResult.Accepted,
                "Parasol empty removal finishes without forced choice");
            var gainsBlock = typeof(PredictorRun).GetMethod("ShopGainsBlock", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var nativeMad = (MadScience)ModelDb.Card<MadScience>().ToMutable();
            foreach (var (sourceType, runtimeType) in new[]
            {
                (CardType.Skill, RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill),
                (CardType.Attack, RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack),
                (CardType.Power, RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Power)
            })
            {
                nativeMad.TinkerTimeType = sourceType;
                var card = new PredictorCard(0, new("CARD", "MAD_SCIENCE"), runtimeType, 0, 0, true, 1)
                { TinkerTimeType = runtimeType };
                Check((bool)gainsBlock.Invoke(noRemovalRun, [card])! == nativeMad.GainsBlock,
                    $"MadScience dynamic GainsBlock {sourceType}");
            }
            var nativeBlade = ModelDb.Card<SovereignBlade>().ToMutable();
            var blade = new PredictorCard(0, new("CARD", "SOVEREIGN_BLADE"),
                RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack, 0, 0, true, 1);
            Check(!nativeBlade.GainsBlock && !(bool)gainsBlock.Invoke(noRemovalRun, [blade])!,
                "SovereignBlade out-of-combat GainsBlock is false");
            var lens = new ModelKey("RELIC", "FRESNEL_LENS");
            var lensState = runtime.Working with
            {
                Relics = runtime.Working.Relics.Add(new(runtime.Working.NextInstanceId, lens, runtime.Working.Position.Floor)),
                NextInstanceId = runtime.Working.NextInstanceId + 1
            };
            var lensRun = new PredictorRun(runtime.Context, lensState);
            player.AddRelicInternal(ModelDb.Relic<FresnelLens>().ToMutable());
            MerchantInventory lensInventory;
            testMode = TestMode.IsOn;
            try
            {
                TestMode.IsOn = false;
                lensInventory = MerchantInventory.CreateForNormalMerchant(player);
            }
            finally { TestMode.IsOn = testMode; }
            lensRun.EnterShop(new(1, 3, "shop-native-lens"));
            int nimble = 0;
            for (int i = 0; i < 7; i++)
            {
                var expected = lensInventory.CardEntries.ElementAt(i).CreationResult!.Card;
                var observed = lensRun.ShopEntries[i].Card!;
                Check(observed.Key == Key(expected.Id), $"lens card identity {i}");
                bool nativeNimble = expected.Enchantment is MegaCrit.Sts2.Core.Models.Enchantments.Nimble;
                Check((observed.Enchantment?.Entry == "NIMBLE") == nativeNimble, $"lens nimble {i}");
                if (nativeNimble) nimble++;
            }
            Check(nimble > 0, "lens sample did not exercise Nimble");
            Check(lensRun.Submit(lensRun.Request!, new LeaveShop()) == PredictorInputResult.Accepted, "leave lens shop");
            var courier = new ModelKey("RELIC", "THE_COURIER");
            var courierState = lensRun.Working with
            {
                Relics = lensRun.Working.Relics.Add(new(lensRun.Working.NextInstanceId, courier, lensRun.Working.Position.Floor)),
                NextInstanceId = lensRun.Working.NextInstanceId + 1
            };
            var courierRun = new PredictorRun(lensRun.Context, courierState);
            player.AddRelicInternal(ModelDb.Relic<TheCourier>().ToMutable());
            testMode = TestMode.IsOn;
            MerchantInventory courierInventory;
            try
            {
                TestMode.IsOn = false;
                courierInventory = MerchantInventory.CreateForNormalMerchant(player);
            }
            finally { TestMode.IsOn = testMode; }
            courierRun.EnterShop(new(1, 4, "shop-native-courier"));
            Check(courierRun.Submit(courierRun.Request!, new OpenShopInventory()) == PredictorInputResult.Accepted,
                "open courier inventory");
            var oldSlot = courierRun.ShopEntries[0];
            Check(courierRun.Submit(courierRun.Request!, new BuyShopEntry(oldSlot.Id, courierRun.ShopInventoryRevision)) ==
                PredictorInputResult.Accepted, "courier card buy");
            typeof(MerchantCardEntry).GetMethod("RestockAfterPurchase", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(courierInventory.CharacterCardEntries[0], [courierInventory]);
            var restocked = courierInventory.CharacterCardEntries[0];
            Check(courierRun.ShopEntries[0].Key == Key(restocked.CreationResult!.Card.Id), "courier card restock identity");
            Check(courierRun.ShopEntries[0].BaseCost == restocked.Cost, "courier card restock price");
            var potionSlot = courierRun.ShopEntries[10];
            Check(courierRun.Submit(courierRun.Request!, new BuyShopEntry(potionSlot.Id, courierRun.ShopInventoryRevision)) ==
                PredictorInputResult.Accepted, "courier potion buy");
            testMode = TestMode.IsOn;
            try
            {
                TestMode.IsOn = false;
                typeof(MerchantPotionEntry).GetMethod("RestockAfterPurchase", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(courierInventory.PotionEntries[0], [courierInventory]);
            }
            finally { TestMode.IsOn = testMode; }
            var potionRestocked = courierInventory.PotionEntries[0];
            Check(courierRun.ShopEntries[10].Key == Key(potionRestocked.Model!.Id), "courier potion restock identity");
            Check(courierRun.ShopEntries[10].BaseCost == potionRestocked.Cost, "courier potion restock price");
            foreach (var (stream, nativeRng) in new[] { (PredictorStream.Shops, player.PlayerRng.Shops),
                (PredictorStream.Rewards, player.PlayerRng.Rewards) })
            {
                var p = courierRun.Working.Streams.Single(s => s.Stream == stream);
                var n = nativeRng.ToSerializable();
                Check((p.S0, p.S1, p.S2, p.S3, p.Calls) ==
                    (n.state0, n.state1, n.state2, n.state3, n.counter), $"courier {stream} continuation");
            }
            Console.WriteLine($"RUNTIME_SHOP_NATIVE seed={seed} inventory=14 identity/cost/RNG, lens Nimble={nimble}, courier card+potion restock/RNG PASS; RT2 buy/leave/parasol forced removal PASS");
            RestContinuations(runtime, player);
            PaelsWingAlternatives(runtime);
            TutorialRewards(context, authority);
        }
        finally
        {
            stateProperty.SetValue(manager, saved);
            ascensionProperty.SetValue(manager, savedAscension);
        }
    }

    private static void TutorialRewards(PredictorContext context, RuntimeContextAuthoritySnapshot authority)
    {
        bool savedTestMode = TestMode.IsOn;
        // TestMode is a host flag. The product prediction request is for a
        // single-player run, whose world capture requires the live mode fact.
        TestMode.IsOn = false;
        try
        {
        var captured = ReflectionNeowEffectSnapshotAdapter.Capture(
            RolltheSpire2.Compatibility.Beta111Profile.Instance, context.Seed, authority.Character,
            context.Ascension, 1, 0, RolltheSpire2.Compatibility.RuntimeProfileId.Beta111,
            context.GameVersion, new(GeneratedCursePoolRequiresModifierFlag: true,
                OtherCharacterPoolsUseModelIdOrder: true), UnlockState.none);
        Check(captured.EffectAuthority != null, "first-run frozen effect capture");
        var world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(
            RolltheSpire2.Compatibility.Beta111Profile.Instance, context.Seed, authority.Character,
            context.Ascension, 1, 0, true, captured.EffectAuthority,
            RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer,
            PredictionGameModeAuthority.ExplicitRequest, context.GameVersion,
            explicitUnlockState: UnlockState.none, explicitDiscoveredActs: context.DiscoveredActs);
        var firstAuthority = new RuntimeContextAuthoritySnapshot(authority.Character, context.Ascension, 0, 1,
            true, authority.VanillaNeowCatalogExact, captured.AllCharacterCardPoolsUnlocked,
            captured.UnlockedCommonCards, captured.UnlockedUncommonCards, context.GameVersion,
            RolltheSpire2.Compatibility.RuntimeProfileId.Beta111, true, null,
            "sts2", captured.UnlockAuthority, captured.UnlockCompleteness,
            captured.CharacterResolution, captured.EffectAuthority, world,
            RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer,
            PredictionGameModeAuthority.ExplicitRequest);
        var tutorialContext = context with { NumberOfRuns = 0, RevealedEpochs = [], SeenEncounters = [],
            CatalogFingerprint = firstAuthority.CatalogFingerprint,
            UnlockFingerprint = firstAuthority.UnlockSnapshotFingerprint };
        var run = PredictorSeedCapture.CreateRun(tutorialContext, firstAuthority);
        Check(run.Working is { StartedWithNeow: false, OpeningCompleted: true } &&
            run.Working.OpeningOptions.IsEmpty &&
            run.RouteMap(1).Points[0].Type ==
                RolltheSpire2.Core.Prediction.Maps.MapPointType.Monster,
            "first-run starting point is Monster with no Neow opening");
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 1);
        var native = RunState.CreateForTest([player],
            run.Working.Acts.Select(a => ModelDb.GetById<ActModel>(new ModelId(a.Key.Category, a.Key.Entry))).ToArray(),
            seed: context.Seed, ascensionLevel: context.Ascension);
        var manager = RunManager.Instance;
        typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, native);
        typeof(RunManager).GetProperty("AscensionManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(manager, new AscensionManager(context.Ascension));
        native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(UnlockState.none), native.Rng.UpFront);
        player.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
        manager.GenerateRooms();
        var roomSetField = typeof(ActModel).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 0; i < native.Acts.Count; i++)
        {
            var rooms = (RoomSet)roomSetField.GetValue(native.Acts[i])!;
            var prediction = run.Working.Acts[i];
            Check(rooms.normalEncounters.Select(e => Key(e.Id)).SequenceEqual(prediction.NormalEncounters),
                $"first-run Act{i + 1} normal encounter queue");
            Check(rooms.eliteEncounters.Select(e => Key(e.Id)).SequenceEqual(prediction.EliteEncounters),
                $"first-run Act{i + 1} elite encounter queue");
            Check(rooms.events.Select(e => Key(e.Id)).SequenceEqual(prediction.RawEvents),
                $"first-run Act{i + 1} event queue");
            Check(prediction.Bosses.SequenceEqual(new[] { Key(rooms.Boss.Id) }
                .Concat(rooms.SecondBoss is { } second ? new[] { Key(second.Id) } : Array.Empty<ModelKey>())),
                $"first-run Act{i + 1} boss discovery and A10 second boss");
            Check(rooms.HasAncient == prediction.Ancient.IsValid &&
                (!rooms.HasAncient || Key(rooms.Ancient.Id) == prediction.Ancient),
                $"first-run Act{i + 1} Ancient identity or absence");
        }
        var upfront = run.Working.Streams.Single(s => s.Stream == PredictorStream.UpFront);
        var nativeUpfront = native.Rng.UpFront.ToSerializable();
        Check((upfront.S0, upfront.S1, upfront.S2, upfront.S3, upfront.Calls) ==
            (nativeUpfront.state0, nativeUpfront.state1, nativeUpfront.state2, nativeUpfront.state3, nativeUpfront.counter),
            "first-run GenerateRooms UpFront continuation");
        Console.WriteLine("RUNTIME_FIRST_RUN_NATIVE GenerateRooms 3 acts encounter/event/boss/Ancient/UpFront, no-Neow Monster start PASS");
        for (int battle = 1; battle <= 7; battle++)
        {
            var key = run.Working.Acts[0].NormalEncounters[run.Working.Acts[0].NormalCursor];
            var encounter = ModelDb.GetById<EncounterModel>(new ModelId(key.Category, key.Entry));
            native.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Monster, RoomType.Monster, encounter.Id);
            var room = new CombatRoom(encounter.ToMutable(), native);
            var expected = new RewardsSet(player).WithRewardsFromRoom(room);
            expected.GenerateWithoutOffering().GetAwaiter().GetResult();
            run.EnterCombatRewards(PredictorCombatKind.Monster, 1, battle + 1);
            Check(run.Rewards.OfType<PredictorReward>().Where(r => r.Kind == PredictorRewardKind.Card)
                .SelectMany(r => r.Cards).Select(c => c.Key.Entry)
                .SequenceEqual(expected.Rewards.OfType<CardReward>().SelectMany(r => r.Cards).Select(c => c.Id.Entry)),
                $"tutorial Monster {battle} manual card identity");
            Check(run.Rewards.Where(r => r.Kind == PredictorRewardKind.Gold).Select(r => r.GoldAmount)
                .SequenceEqual(expected.Rewards.OfType<GoldReward>().Select(r => r.Amount)),
                $"tutorial Monster {battle} gold/RNG");
            Check(run.Rewards.Where(r => r.Kind == PredictorRewardKind.Potion).Select(r => r.Key.Entry)
                .SequenceEqual(expected.Rewards.OfType<PotionReward>().Select(r => r.Potion!.Id.Entry)),
                $"tutorial Monster {battle} fixed potion");
            var predicted = run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards);
            var source = player.PlayerRng.Rewards.ToSerializable();
            Check((predicted.S0, predicted.S1, predicted.S2, predicted.S3, predicted.Calls) ==
                (source.state0, source.state1, source.state2, source.state3, source.counter),
                $"tutorial Monster {battle} Rewards RNG continuation");
            Check(run.Submit(run.Request!, new LeaveRewards()) == PredictorInputResult.Accepted,
                $"tutorial Monster {battle} leave");
        }
        Console.WriteLine("RUNTIME_TUTORIAL_NATIVE first seven Ironclad Monster rewards cards/gold/potions/Rewards RNG PASS");
        var routed = PredictorSeedCapture.CreateRun(tutorialContext, firstAuthority);
        var firstMap = routed.RouteMap(1);
        routed.SetRoute(1, new RolltheSpire2.Core.Prediction.Maps.MapRouteSet(firstMap,
            RolltheSpire2.Core.Prediction.Maps.MapPointType.Monster, minimize: true).At(0).ToImmutableArray());
        routed.AdvanceRoute();
        Check(routed.Phase == PredictorPhase.CombatFacts && routed.Working.Visits.Length == 1 &&
            routed.Working.Visits[0].Room == PredictorRoomKind.Monster,
            "first-run route enters actual starting Monster without Opening");
        var combatFacts = new PredictorCombatFacts
        {
            Potions = PredictorCombatPotionFacts.NoAdditional,
            EnemyCreations = routed.Project().RequiredEnemyCreationFacts
                .Select(s => new PredictorEnemyCreationFact(s, 0)).ToImmutableArray(),
            StolenCards = routed.Project().NeedsStolenCardFacts ? new([]) : null
        };
        Check(routed.Submit(routed.Request!, new ResolveCombat(combatFacts)) == PredictorInputResult.Accepted &&
            routed.Phase == PredictorPhase.Rewards,
            "first-run route settles starting Monster with declared combat facts");
        Check(routed.Submit(routed.Request!, new LeaveRewards()) == PredictorInputResult.Accepted &&
            routed.Phase == PredictorPhase.Complete, "first-run route commits first node");
        var reloaded = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(routed),
            tutorialContext.CatalogFingerprint, tutorialContext.UnlockFingerprint);
        Check(reloaded.Working.Visits.Length == routed.Working.Visits.Length &&
            reloaded.Working.Visits[0].Point == routed.Working.Visits[0].Point &&
            reloaded.Working.Visits[0].Room == routed.Working.Visits[0].Room &&
            reloaded.Working.Visits[0].CombatKinds.SequenceEqual(routed.Working.Visits[0].CombatKinds) &&
            reloaded.Working.StartedWithNeow == false && reloaded.Working.OpeningCompleted &&
            reloaded.Working.Streams.SequenceEqual(routed.Working.Streams) &&
            reloaded.Routes.Length == 1 && reloaded.Routes[0].Points.SequenceEqual(routed.Routes[0].Points),
            "first-run route save/reload after starting Monster");
        Console.WriteLine("RUNTIME_FIRST_RUN_ROUTE starting Monster no Opening, first combat reward checkpoint/save/reload PASS");
        ActEntryModifierOrder(tutorialContext, firstAuthority);
        StaleRoutePersistence(tutorialContext, firstAuthority);
        }
        finally { TestMode.IsOn = savedTestMode; }
    }

    private static void ActEntryModifierOrder(PredictorContext context, RuntimeContextAuthoritySnapshot authority)
    {
        var cursed = PredictorSeedCapture.CreateRun(context with
        { Modifiers = [new PredictorRunModifier(new("MODIFIER", "CURSED_RUN"))] }, authority);
        int cardsBefore = cursed.Working.Deck.Length;
        int nicheBefore = cursed.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls;
        var openingNiche = cursed.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Restore();
        var curses = cursed.Context.Catalog!.ModifierCurseCards;
        var expectedCurse = curses[openingNiche.NextInt(curses.Length)];
        cursed.SetRoute(1, new RolltheSpire2.Core.Prediction.Maps.MapRouteSet(cursed.RouteMap(1),
            RolltheSpire2.Core.Prediction.Maps.MapPointType.Monster, minimize: true).At(0).ToImmutableArray());
        cursed.AdvanceRoute();
        int nicheAfter = cursed.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls;
        Check(cursed.Phase == PredictorPhase.CombatFacts && cursed.Working.Deck.Length == cardsBefore + 1 &&
            cursed.Working.Card(cursed.Working.Deck[^1]).Key == expectedCurse &&
            cursed.Working.Card(cursed.Working.Deck[^1]).FloorAdded == 0 && nicheAfter >= nicheBefore + 1,
            $"CursedRun AfterActEntered before no-Neow first combat phase={cursed.Phase} cards={cursed.Working.Deck.Length - cardsBefore} niche={nicheAfter - nicheBefore} expected={expectedCurse} actual={cursed.Working.Card(cursed.Working.Deck[^1]).Key}");
        var facts = new PredictorCombatFacts
        {
            Potions = PredictorCombatPotionFacts.NoAdditional,
            EnemyCreations = cursed.Project().RequiredEnemyCreationFacts
                .Select(s => new PredictorEnemyCreationFact(s, 0)).ToImmutableArray(),
            StolenCards = cursed.Project().NeedsStolenCardFacts ? new([]) : null
        };
        Check(cursed.Submit(cursed.Request!, new ResolveCombat(facts)) == PredictorInputResult.Accepted &&
            cursed.Submit(cursed.Request!, new LeaveRewards()) == PredictorInputResult.Accepted &&
            cursed.Checkpoints[^1].CompletedNode!.Inputs.First().Action == "CursedRunActEntered",
            "CursedRun act-entry source precedes first node settlement in history");
        Console.WriteLine("RUNTIME_ACT_ENTRY CursedRun no-Neow act hook before first combat and checkpoint record PASS");
    }

    private static void StaleRoutePersistence(PredictorContext context, RuntimeContextAuthoritySnapshot authority)
    {
        var planned = PredictorSeedCapture.CreateRun(context, authority);
        planned.SetRoute(1, new RolltheSpire2.Core.Prediction.Maps.MapRouteSet(planned.RouteMap(1),
            RolltheSpire2.Core.Prediction.Maps.MapPointType.Monster, minimize: true).At(0).ToImmutableArray());
        var compass = new PredictorRelic(planned.Working.NextInstanceId,
            new("RELIC", "GOLDEN_COMPASS"), planned.Working.Position.Floor) { GoldenPathAct = 0 };
        var changed = planned.Working with { Relics = planned.Working.Relics.Add(compass),
            NextInstanceId = planned.Working.NextInstanceId + 1 };
        var stale = new PredictorRun(planned.Context, changed);
        stale.RestoreRoutes(planned.Routes);
        Check(stale.RouteActsNeedingReplan.SequenceEqual([1, 2, 3]),
            "changed map and missing future acts require explicit route choices");
        var reloaded = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(stale),
            context.CatalogFingerprint, context.UnlockFingerprint);
        Check(reloaded.Routes.Length == 1 && reloaded.Routes[0].Points.SequenceEqual(planned.Routes[0].Points) &&
            reloaded.RouteActsNeedingReplan.SequenceEqual([1, 2, 3]),
            "restore preserves stale route intent with visible replan status");
        bool refused = false;
        try { reloaded.AdvanceRoute(); }
        catch (InvalidOperationException e) when (e.Message == "PredictorRouteNeedsReplan:Act1") { refused = true; }
        Check(refused && reloaded.Working.Maps.IsEmpty, "stale route cannot travel or commit map");
        reloaded.SetRoute(1, new RolltheSpire2.Core.Prediction.Maps.MapRouteSet(reloaded.RouteMap(1),
            RolltheSpire2.Core.Prediction.Maps.MapPointType.Monster, minimize: true).At(0).ToImmutableArray());
        Check(reloaded.RouteActsNeedingReplan.SequenceEqual([2, 3]),
            "explicit replacement clears current Act replan while future acts still need routes");
        Console.WriteLine("RUNTIME_ROUTE_REPLAN changed geometry preserves saved intent, refuses stale travel, accepts new route PASS");
    }

    private static void PaelsWingAlternatives(PredictorRun baseline)
    {
        var wing = new ModelKey("RELIC", "PAELS_WING");
        var state = baseline.Working with
        {
            Relics = baseline.Working.Relics.Add(new(baseline.Working.NextInstanceId, wing,
                baseline.Working.Position.Floor)),
            NextInstanceId = baseline.Working.NextInstanceId + 1,
            PersonalBag = PredictorRelicBag.Empty(false)
        };
        long wingId = state.Relics[^1].Id;
        var run = new PredictorRun(baseline.Context, state);
        run.EnterCombatRewards(PredictorCombatKind.Monster, 1, 12);
        var first = run.Rewards.Single(r => r.Kind == PredictorRewardKind.Card);
        Check(run.Submit(run.Request!, new EnterCardSelection(first.Id)) == PredictorInputResult.Accepted &&
            run.CardRewardAlternatives.Single(a => a.Key == "SACRIFICE") ==
                new PredictorCardRewardAlternative(first.Id, wingId, "SACRIFICE"),
            "PaelsWing card reward alternative offered");
        int deckCount = run.Working.Deck.Length;
        Check(run.Submit(run.Request!, new SacrificeCardReward(first.Id, wingId)) == PredictorInputResult.Accepted &&
            run.Working.Relics.Single(r => r.Id == wingId).RewardsSacrificed == 1 &&
            run.Rewards.Single(r => r.Id == first.Id).Status == PredictorRewardStatus.Taken &&
            run.Working.Deck.Length == deckCount, "first sacrifice completes without relic");
        Check(run.Submit(run.Request!, new LeaveRewards()) == PredictorInputResult.Accepted,
            "first sacrifice reward set leaves");
        run.EnterCombatRewards(PredictorCombatKind.Monster, 1, 13);
        var second = run.Rewards.Single(r => r.Kind == PredictorRewardKind.Card);
        Check(run.Submit(run.Request!, new EnterCardSelection(second.Id)) == PredictorInputResult.Accepted,
            "second PaelsWing card selection");
        int rewardCalls = run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls;
        Check(run.Submit(run.Request!, new SacrificeCardReward(second.Id, wingId)) == PredictorInputResult.Accepted &&
            run.Working.Relics.Single(r => r.Id == wingId).RewardsSacrificed == 2 &&
            run.Working.Relics.Any(r => r.Key.Entry == "CIRCLET") &&
            run.Rewards.Single(r => r.Id == second.Id).Status == PredictorRewardStatus.Taken &&
            run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls == rewardCalls + 1,
            "second sacrifice pulls and obtains front relic before reward completes");
        var driftwoodState = baseline.Working with
        {
            Relics = baseline.Working.Relics.Add(new(baseline.Working.NextInstanceId,
                new("RELIC", "DRIFTWOOD"), baseline.Working.Position.Floor)),
            NextInstanceId = baseline.Working.NextInstanceId + 1
        };
        var reroll = new PredictorRun(baseline.Context, driftwoodState);
        reroll.EnterCombatRewards(PredictorCombatKind.Monster, 1, 14);
        var cardReward = reroll.Rewards.Single(r => r.Kind == PredictorRewardKind.Card);
        Check(cardReward.CanReroll && cardReward.RerollRecipe != null,
            "Driftwood late reward modifier enables reroll with recipe");
        Check(reroll.Submit(reroll.Request!, new EnterCardSelection(cardReward.Id)) == PredictorInputResult.Accepted &&
            reroll.CardRewardAlternatives.Select(a => a.Key).SequenceEqual(["SKIP", "REROLL"]),
            "Driftwood alternative source order");
        var previous = cardReward.Cards.Select(c => c.Id).ToImmutableArray();
        int calls = reroll.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls;
        Check(reroll.Submit(reroll.Request!, new RerollCardReward(cardReward.Id)) == PredictorInputResult.Accepted &&
            reroll.Rewards.Single(r => r.Id == cardReward.Id) is { CanReroll: false, HasRerolled: true } refreshed &&
            refreshed.Cards.All(c => !previous.Contains(c.Id)) &&
            reroll.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls > calls &&
            reroll.CardRewardAlternatives.Select(a => a.Key).SequenceEqual(["SKIP"]),
            "Driftwood reroll replaces option instances once");
        var pairState = driftwoodState with { Relics = driftwoodState.Relics.Add(
            new(driftwoodState.NextInstanceId, wing, driftwoodState.Position.Floor)),
            NextInstanceId = driftwoodState.NextInstanceId + 1 };
        var pair = new PredictorRun(baseline.Context, pairState);
        pair.EnterCombatRewards(PredictorCombatKind.Monster, 1, 15);
        long pairCardId = pair.Rewards.Single(r => r.Kind == PredictorRewardKind.Card).Id;
        bool tooMany = false;
        try { pair.Submit(pair.Request!, new EnterCardSelection(pairCardId)); }
        catch (InvalidOperationException e) when (e.Message.Contains("More than 2 card reward alternatives"))
        { tooMany = true; }
        Check(tooMany, "Driftwood plus PaelsWing preserves source alternative-count failure");
        Console.WriteLine("RUNTIME_CARD_ALTERNATIVE PaelsWing first/second sacrifice, Driftwood reroll PASS");
    }

    private static void RestContinuations(PredictorRun baseline, Player nativePlayer)
    {
        static PredictorState AddRelics(PredictorState source, params string[] names)
        {
            var state = source;
            foreach (string name in names)
            {
                state = state with { Relics = state.Relics.Add(new(state.NextInstanceId, new("RELIC", name), state.Position.Floor)),
                    NextInstanceId = state.NextInstanceId + 1 };
            }
            return state;
        }
        nativePlayer.AddRelicInternal(ModelDb.Relic<Shovel>().ToMutable());
        nativePlayer.AddRelicInternal(ModelDb.Relic<MeatCleaver>().ToMutable());
        nativePlayer.AddRelicInternal(ModelDb.Relic<PaelsGrowth>().ToMutable());
        nativePlayer.AddRelicInternal(ModelDb.Relic<PumpkinCandle>().ToMutable());
        nativePlayer.AddRelicInternal(ModelDb.Relic<Girya>().ToMutable());
        var nativeOptions = RestSiteOption.Generate(nativePlayer).Select(o => o.OptionId).ToArray();
        var optionState = AddRelics(baseline.Working, "SHOVEL", "MEAT_CLEAVER", "PAELS_GROWTH", "PUMPKIN_CANDLE", "GIRYA");
        var optionRun = new PredictorRun(baseline.Context, optionState);
        optionRun.EnterRestSite(1, 4);
        Check(optionRun.RestOptions.Select(o => o.ToString().ToUpperInvariant()).SequenceEqual(nativeOptions),
            "native rest option generation order");
        var midas = new PredictorRun(baseline.Context with
            { Modifiers = [new(new ModelKey("MODIFIER", "MIDAS"))] }, optionState);
        midas.EnterRestSite(1, 4);
        Check(!midas.RestOptions.Contains(PredictorRestAction.Smith) &&
            midas.RestOptions.Contains(PredictorRestAction.Heal), "Midas removes Smith only");
        var terrorContext = baseline.Context with
            { Modifiers = [new(new ModelKey("MODIFIER", "NIGHT_TERRORS"))] };
        var terror = new PredictorRun(terrorContext, AddRelics(baseline.Working,
            "REGAL_PILLOW", "STONE_HUMIDIFIER"));
        terror.EnterRestSite(1, 4);
        Check(terror.Submit(terror.Request!, new SelectRestAction(PredictorRestAction.Heal)) == PredictorInputResult.Accepted &&
            terror.Phase == PredictorPhase.RestHealFact, "NightTerrors requests MaxHp fact");
        Check(terror.Submit(terror.Request!, new ProvideRestMaxHpFact(80)) == PredictorInputResult.Accepted &&
            terror.Phase == PredictorPhase.RestSite, "NightTerrors accepts fact and completes heal");
        Check(terror.Submit(terror.Request!, new LeaveRestSite()) == PredictorInputResult.Accepted,
            "NightTerrors rest leave");
        var terrorOutcome = terror.Checkpoints[^1].CompletedNode!.Inputs.Last(i => i.Action == "RestHeal").RestHealOutcome;
        Check(terrorOutcome is { OverrideHealAmount: 80, FlatHealBonus: 0, MaxHpDelta: 0,
            MaxHpLoss: 5, IsMimicked: false }, "NightTerrors overrides RegalPillow and loses MaxHp after Humidifier");
        nativePlayer.AddRelicInternal(ModelDb.Relic<DreamCatcher>().ToMutable());
        nativePlayer.AddRelicInternal(ModelDb.Relic<TinyMailbox>().ToMutable());
        var nativeHealRewards = new List<Reward>();
        Hook.ModifyRestSiteHealRewards(nativePlayer.RunState, nativePlayer, nativeHealRewards, isMimicked: false);
        Check(nativeHealRewards.Count(r => r is CardReward) == 1 &&
            nativeHealRewards.Count(r => r is PotionReward) == 2, "native rest heal reward owners");
        var healState = AddRelics(baseline.Working, "DREAM_CATCHER", "TINY_MAILBOX", "REGAL_PILLOW",
            "STONE_HUMIDIFIER", "MINIATURE_TENT", "VENERABLE_TEA_SET");
        var heal = new PredictorRun(baseline.Context, healState);
        heal.EnterRestSite(1, 5);
        Check(heal.Working.Relics.Any(r => r.Key.Entry == "VENERABLE_TEA_SET" && r.GainEnergyInNextCombat),
            "tea-set rest entry flag");
        Check(heal.Submit(heal.Request!, new SelectRestAction(PredictorRestAction.Heal)) == PredictorInputResult.Accepted &&
            heal.Phase == PredictorPhase.Rewards, "rest heal child rewards");
        Check(heal.Rewards.Count(r => r.Kind == PredictorRewardKind.Card) == 1 &&
            heal.Rewards.Count(r => r.Kind == PredictorRewardKind.Potion) == 2, "rest heal reward list");
        static string HealOfferIdentity(PredictorRun run) => string.Join("|", run.Rewards.Select(r =>
            $"{r.Kind}:{r.Key}:{string.Join(",", r.Cards.Select(c => c.Key))}"));
        string offeredHeal = HealOfferIdentity(heal);
        var healStreams = heal.Working.Streams;
        Check(heal.Submit(heal.Request!, new LeaveRewards()) == PredictorInputResult.Accepted &&
            heal.Phase == PredictorPhase.RestSite && !heal.RestOptions.Contains(PredictorRestAction.Heal),
            "rest heal child return and tent action consumption");
        Check(heal.Submit(heal.Request!, new LeaveRestSite()) == PredictorInputResult.Accepted &&
            heal.Phase == PredictorPhase.Complete, "rest heal checkpoint");
        var outcome = heal.Checkpoints[^1].CompletedNode!.Inputs.Last(i => i.Action == "RestHeal").RestHealOutcome;
        Check(outcome is { BaseMaxHpFraction: .3m, FlatHealBonus: 15, MaxHpDelta: 5, IsMimicked: false },
            "typed rest heal outcome");
        heal.Rewind(0);
        Check(heal.RestOptions.IsEmpty && !heal.Working.Relics.Any(r => r.Key.Entry == "VENERABLE_TEA_SET" &&
            r.GainEnergyInNextCombat), "rest rewind entry residue");
        heal.EnterRestSite(1, 5);
        Check(heal.Submit(heal.Request!, new SelectRestAction(PredictorRestAction.Heal)) == PredictorInputResult.Accepted &&
            HealOfferIdentity(heal) == offeredHeal && heal.Working.Streams.SequenceEqual(healStreams),
            "rest nested reward rewind continuation");

        var digState = AddRelics(baseline.Working, "SHOVEL") with { PersonalBag = PredictorRelicBag.Empty(false) };
        var dig = new PredictorRun(baseline.Context, digState);
        dig.EnterRestSite(1, 6);
        int relicBefore = dig.Working.Relics.Length;
        int rewardCalls = dig.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls;
        Check(dig.Submit(dig.Request!, new SelectRestAction(PredictorRestAction.Dig)) == PredictorInputResult.Accepted &&
            dig.Working.Relics.Length == relicBefore + 1 && dig.Working.Relics[^1].Key.Entry == "CIRCLET" &&
            dig.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls == rewardCalls + 1 &&
            !dig.RestOptions.Contains(PredictorRestAction.Dig), "dig direct pull/obtain and action consumption");
        Check(dig.Submit(dig.Request!, new LeaveRestSite()) == PredictorInputResult.Accepted, "dig leave");

        var cloneCard = baseline.Working.Card(baseline.Working.Deck[0]);
        var crafted = AddRelics(baseline.Working.WithCard(cloneCard with
            { Enchantment = new("ENCHANTMENT", "CLONE"), EnchantmentAmount = 4 }),
            "MEAT_CLEAVER", "PAELS_GROWTH", "PUMPKIN_CANDLE", "MINIATURE_TENT");
        int candleIndex = crafted.Relics.FindIndex(r => r.Key.Entry == "PUMPKIN_CANDLE");
        crafted = crafted with { Relics = crafted.Relics.SetItem(candleIndex,
            crafted.Relics[candleIndex] with { KindleCount = 5 }) };
        var actions = new PredictorRun(baseline.Context, crafted);
        actions.EnterRestSite(1, 7);
        Check(actions.RestOptions.SequenceEqual(new[] { PredictorRestAction.Heal, PredictorRestAction.Smith,
            PredictorRestAction.Cook, PredictorRestAction.Clone, PredictorRestAction.Kindle }),
            "rest option listener order");
        Check(actions.Submit(actions.Request!, new SelectRestAction(PredictorRestAction.Cook)) == PredictorInputResult.Accepted &&
            actions.Phase == PredictorPhase.RestCookCards, "begin cook choice");
        Check(actions.Submit(actions.Request!, new CancelCookSelection()) == PredictorInputResult.Accepted &&
            actions.RestOptions.Contains(PredictorRestAction.Cook), "cook cancel keeps option");
        Check(actions.Submit(actions.Request!, new SelectRestAction(PredictorRestAction.Cook)) == PredictorInputResult.Accepted,
            "reenter cook");
        var remove = actions.RestCookCandidates.Where(id => id != cloneCard.Id).Take(2).ToImmutableArray();
        int deckBefore = actions.Working.Deck.Length;
        Check(actions.Submit(actions.Request!, new SelectCookCards(remove)) == PredictorInputResult.Accepted &&
            actions.Working.Deck.Length == deckBefore - 2 && !actions.RestOptions.Contains(PredictorRestAction.Cook),
            "cook confirmed remove two");
        deckBefore = actions.Working.Deck.Length;
        Check(actions.Submit(actions.Request!, new SelectRestAction(PredictorRestAction.Clone)) == PredictorInputResult.Accepted &&
            actions.Working.Deck.Length == deckBefore + 1 &&
            actions.Working.Card(actions.Working.Deck[^1]).Enchantment?.Entry == "CLONE",
            "clone start-of-action snapshot");
        Check(actions.Submit(actions.Request!, new SelectRestAction(PredictorRestAction.Kindle)) == PredictorInputResult.Accepted &&
            actions.Working.Relics.Single(r => r.Key.Entry == "PUMPKIN_CANDLE").KindleCount == 10,
            "kindle adds five");
        Check(actions.Submit(actions.Request!, new LeaveRestSite()) == PredictorInputResult.Accepted,
            "multi-action rest leave");
        Check(actions.Checkpoints[^1].CompletedNode!.Inputs.Any(i => i.Action == "RestCookRemoveTwoGainMaxHp5" &&
            i.EventHealthConsequences.Single() == new PredictorEventHealthDelta(5, 5)),
            "cook max HP consequence record");

        var eggState = PredictorObtainSources.AddCard(baseline.Context, baseline.Working,
            new ModelKey("CARD", "BYRDONIS_EGG"));
        eggState = PredictorObtainSources.AddCard(baseline.Context, eggState,
            new ModelKey("CARD", "BYRDONIS_EGG"));
        var hatch = new PredictorRun(baseline.Context, eggState);
        hatch.EnterRestSite(1, 8);
        Check(hatch.RestOptions.Count(o => o == PredictorRestAction.Hatch) == 2,
            "two egg listeners offer two hatches");
        Check(hatch.Submit(hatch.Request!, new SelectRestAction(PredictorRestAction.Hatch)) == PredictorInputResult.Accepted &&
            hatch.Working.Relics.Any(r => r.Key.Entry == "BYRDPIP") &&
            hatch.Working.Deck.Count(id => hatch.Working.Card(id).Key.Entry == "BYRD_SWOOP") == 2 &&
            hatch.Working.Deck.All(id => hatch.Working.Card(id).Key.Entry != "BYRDONIS_EGG"),
            "hatch obtains Byrdpip and transforms all eggs");
        Check(hatch.Submit(hatch.Request!, new LeaveRestSite()) == PredictorInputResult.Accepted,
            "hatch leave and checkpoint");
        Console.WriteLine("RUNTIME_REST heal child rewards/TeaSet/Tent, Shovel Dig, Cook cancel/confirm, Clone snapshot, Kindle PASS");
    }
}
