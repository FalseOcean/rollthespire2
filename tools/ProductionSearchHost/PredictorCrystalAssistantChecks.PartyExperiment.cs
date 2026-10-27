using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Shell;

internal static partial class PredictorCrystalAssistantChecks
{
    private static readonly JsonSerializerOptions PartyNativeJson = new(ProductionSearchReplay.JsonOptions) { IncludeFields = true };
    private sealed record CrystalPartyFixture(RunState Run, Player[] Players, CrystalSphere[] Events,
        CrystalSphereMinigame[] Games, int LocalSlot);

    // Controlled native multi-player collections using production capture,
    // not connected network peers. The search never sends network messages.
    private static void CheckCrystalPartyExperiment(string output, int ascension)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var stateProperty = typeof(RunManager).GetProperty("State", flags)!;
        var receipt = new List<object>();
        int oldPoolMismatches = 0;
        foreach (int count in new[] { 2, 4 })
        for (int local = 0; local < count; local++)
        {
            var f = Create(count, local);
            var p = LocalContext.GetMe(f.Run)!;
            Check(ReferenceEquals(p, f.Players[local]), "Local NetId chooses the actual slot, not player zero");
            Check(f.Events.All(e => !e.IsShared) && !ReferenceEquals(f.Events[0].Rng, f.Events[1].Rng), "Crystal events own independent RNG instances");
            Check(f.Events[0].Rng.ToSerializable() != f.Events[1].Rng.ToSerializable(), "Slot contributes to actual native event RNG seed");
            var old = CrystalSphereLiveCapture.CaptureScene(f.Run, p, f.Events[local], f.Games[local], "0.111.0").Branches.Single().Snapshot;
            var source = old;
            Check(source.Context.Crystal is { } owner && owner.PlayerSlot == local && owner.PlayerNetId == p.NetId && owner.PlayerCount == count,
                "Production context retains actual local identity and party size");
            Check(ReferenceEquals(CrystalSphereLiveCapture.CurrentPlayer(f.Run), p), "Production player resolution follows LocalContext");
            bool mismatchedOwner=false;
            try { CrystalSphereLiveCapture.CaptureScene(f.Run,p,f.Events[(local+1)%count],f.Games[local],"0.111.0"); }
            catch(InvalidOperationException ex) when(ex.Message=="CrystalPlayerMismatch") { mismatchedOwner=true; }
            Check(mismatchedOwner,"A different player's event cannot be combined with the local board");
            var actualPool = p.Character.CardPool.GetUnlockedCards(p.UnlockState, ((IRunState)f.Run).CardMultiplayerConstraint)
                .Where(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare).Select(c => c.Id.Entry).ToArray();
            var bridgePool = source.Context.Catalog!.CharacterCards.Where(c => c.IsUnlockedInCapturedPool && c.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
                .Select(c => c.CardKey.Entry).ToArray();
            Check(bridgePool.SequenceEqual(actualPool), "Production current-player pool preserves native MP order and eligibility");
            var oldPool = old.Context.Catalog!.CharacterCards.Where(c => c.IsUnlockedInCapturedPool && !c.IsMultiplayerOnly && c.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare).Select(c => c.CardKey.Entry).ToArray();
            bool poolMismatch = !oldPool.SequenceEqual(actualPool); if (poolMismatch) oldPoolMismatches++;
            string before = NativeState(f);
            var path = CardPath(source);
            var predicted = PredictorCrystalExplorer.Replay(source, path);
            var suffixes = PredictorCrystalRerolls.Prefixes(predicted).ToArray();
            Check(before == NativeState(f), "Current-player replay and suffix search mutate neither local nor remote native state");
            var native = Materialize(f, local, path);
            Check(CardSignature(native.Rewards.OfType<CardReward>()) == CardSignature(predicted.Rewards.Where(r => r.Kind == PredictorRewardKind.Card)), "Current-player native MP reward identity/upgrade parity after production capture");
            CheckNonCards(native, predicted);
            CheckRng(f, local, predicted);
            // Real event stream / actual MP pool, one reroll of every available group.
            var all = suffixes.First(n => n.Actions.OfType<RerollCardReward>().Count() == 3);
            foreach (var op in Operations(predicted, all).Where(o => o.Action == "Reroll")) ((CardReward)native.Rewards[op.Slot]).Reroll();
            Check(CardSignature(native.Rewards.OfType<CardReward>()) == CardSignature(all.Run.Rewards.Where(r => r.Kind == PredictorRewardKind.Card)), "Current-player native MP reroll continuation parity");
            CheckRng(f, local, all.Run);
            Console.WriteLine($"CRYSTAL_PARTY_LOCAL players={count};slot={local};netId={p.NetId};character={p.Character.Id.Entry};oldSoloPoolMismatch={poolMismatch};cards={actualPool.Length};prefixes={suffixes.Length};nativeParity=true;liveMutation=false");
            receipt.Add(new { count, local, p.NetId, character = p.Character.Id.Entry, oldSoloPoolMismatch = poolMismatch,
                added = actualPool.Except(oldPool).ToArray(), removed = oldPool.Except(actualPool).ToArray(), path,
                scope = "actual native MP player collection; production local capture; no network peer" });
            // Separate natural board continuation for the 4x4 relic, not a
            // manually prefilled reward or an impossible reveal-center call.
            var rf = Create(count, local);
            var rs = CrystalSphereLiveCapture.CaptureScene(rf.Run, rf.Players[local], rf.Events[local], rf.Games[local], "0.111.0").Branches.Single().Snapshot;
            var relicPath = RequiredItemPath(rs, item => item.Kind == "RELIC");
            var relicPrediction = PredictorCrystalExplorer.Replay(rs, relicPath);
            var relicNative = Materialize(rf, local, relicPath);
            Check(relicNative.Rewards.OfType<RelicReward>().Any(), "Legal six-click party path actually reveals a relic");
            CheckNonCards(relicNative, relicPrediction); CheckRng(rf, local, relicPrediction);
            var final = CrystalSphereLiveCapture.CaptureScene(rf.Run, rf.Players[local], rf.Events[local], rf.Games[local], "0.111.0").Branches.Single().Snapshot.State;
            Check(JsonSerializer.Serialize(final.PersonalBag, PartyNativeJson) == JsonSerializer.Serialize(relicPrediction.Working.PersonalBag, PartyNativeJson) &&
                JsonSerializer.Serialize(final.SharedBag, PartyNativeJson) == JsonSerializer.Serialize(relicPrediction.Working.SharedBag, PartyNativeJson), "Current-player MP relic reward personal/shared bag continuation parity");
            Console.WriteLine($"CRYSTAL_PARTY_RELIC players={count};slot={local};legalClicks={relicPath.Length};identityBagRngParity=true");
        }
        Check(oldPoolMismatches > 0, "Removing CrystalSoloOnly alone would retain an incorrect solo card catalog");
        CheckInterleaving(wing: false);
        CheckInterleaving(wing: true);
        CheckBagIsolation();
        CheckAdmissionDifferences();
        ProductionSearchReplay.Write(Path.Combine(output, "party-feasibility.json"), receipt);
        Console.WriteLine("CRYSTAL_PARTY_EXPERIMENT_PASS current-player detached search; production capture enabled; shared Niche ignored by guidance policy; network peers not tested");

        CrystalPartyFixture Create(int count, int local, bool wing = false)
        {
            string[] characters = ["IRONCLAD", "SILENT", "REGENT", "NECROBINDER"];
            var players = Enumerable.Range(0, count).Select(i => Player.CreateForNewRun(ModelDb.AllCharacters.Single(c => c.Id.Entry == characters[i]), UnlockState.all, (ulong)(1000 + i * 17))).ToArray();
            foreach (var player in players) player.Gold = 200;
            var run = RunState.CreateForTest(players, seed: "000000000000", ascensionLevel: ascension); run.CurrentActIndex = 1;
            stateProperty.SetValue(RunManager.Instance, run); LocalContext.NetId = players[local].NetId;
            run.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Unknown, RoomType.Event, ModelDb.Event<CrystalSphere>().Id);
            foreach (var player in players)
            {
                player.AddRelicInternal(ModelDb.GetById<RelicModel>(new ModelId("RELIC", "DRIFTWOOD")).ToMutable(), silent: true);
                if (wing) player.AddRelicInternal(ModelDb.GetById<RelicModel>(new ModelId("RELIC", "WING_CHARM")).ToMutable(), silent: true);
                player.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
            }
            run.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(new UnlockState(players.Select(p => p.UnlockState))), run.Rng.UpFront);
            var events = players.Select(p =>
            {
                var ev = (CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable(); ev.BeginEvent(p, null, false).GetAwaiter().GetResult();
                CardPileCmd.AddCurseToDeck<MegaCrit.Sts2.Core.Models.Cards.Debt>(p).GetAwaiter().GetResult(); return ev;
            }).ToArray();
            var games = players.Select((p, i) => new CrystalSphereMinigame(p, events[i].Rng, 6)).ToArray();
            return new(run, players, events, games, local);
        }

        void CheckInterleaving(bool wing)
        {
            var f = Create(2, 1, wing);
            PredictorCrystalSnapshot Capture(int slot) => CrystalSphereLiveCapture.CaptureScene(f.Run, f.Players[slot], f.Events[slot], f.Games[slot], "0.111.0").Branches.Single().Snapshot;
            var source = Capture(1); var path = CardPath(source);
            var predicted = PredictorCrystalExplorer.Replay(source, path);
            var partnerPath = CardPath(Capture(0));
            var eventBefore = source.EventRng;
            var fingerprintBefore = CrystalSphereLiveCapture.SceneFingerprint(f.Run, f.Players[1], f.Events[1], f.Games[1]);
            var partner = Materialize(f, 0, partnerPath);
            // No local board/reward operation has happened yet.
            var after = Capture(1);
            Check(eventBefore == after.EventRng && source.Hidden.SequenceEqual(after.Hidden) && source.Items.SequenceEqual(after.Items), "Partner completion preserves local board and event RNG");
            var fingerprintAfter = CrystalSphereLiveCapture.SceneFingerprint(f.Run, f.Players[1], f.Events[1], f.Games[1]);
            var refreshed = after;
            var predictedAfter = PredictorCrystalExplorer.Replay(refreshed, path);
            var native = Materialize(f, 1, path);
            Check(CardSignature(native.Rewards.OfType<CardReward>()) == CardSignature(predictedAfter.Rewards.Where(r => r.Kind == PredictorRewardKind.Card)), "Fresh MP capture restores full offer parity after partner generation");
            bool oldOffersChanged = CardSignature(predicted.Rewards.Where(r => r.Kind == PredictorRewardKind.Card)) != CardSignature(predictedAfter.Rewards.Where(r => r.Kind == PredictorRewardKind.Card));
            if (wing)
            {
                Check(source.State.Streams.Single(s => s.Stream == PredictorStream.Niche) != after.State.Streams.Single(s => s.Stream == PredictorStream.Niche), "Partner Wing Charm advances shared Niche before local rewards");
                Check(fingerprintBefore == fingerprintAfter, "Local-player fingerprint ignores later shared Niche changes");
                Check(oldOffersChanged, "Observed interleaving changes local enchantment placement despite identical event stream");
            }
            else Check(!oldOffersChanged, "No Wing Charm: partner generation leaves local card identities/upgrades unchanged in this fixture");
            Check(CrystalSphereGuidance.Matches(source, after, false), "Board guidance ignores partner-only shared state changes");
            var oldFrame = new CrystalRewardGuideFrame(predicted.Rewards, [], predicted.ExportCrystal().EventRng,
                predicted.Working.Streams.Single(s=>s.Stream==PredictorStream.Niche));
            var binding = CrystalSphereRewardObservation.Bind(oldFrame, native.Rewards.OfType<CardReward>(), localSnapshot:true);
            Check(binding!=null && CrystalSphereRewardObservation.Matches(oldFrame,binding,
                CrystalSphereLiveCapture.RngState(PredictorStream.Rewards,f.Games[1].Rng),
                CrystalSphereLiveCapture.RngState(PredictorStream.Niche,f.Run.Rng.Niche),localSnapshot:true),
                "Local reward guide accepts unchanged identities/upgrades despite different Niche/enchantments");
            Check(!CrystalSphereRewardObservation.Matches(oldFrame with { EventRng=oldFrame.EventRng with { Calls=oldFrame.EventRng.Calls+1 } },binding!,
                CrystalSphereLiveCapture.RngState(PredictorStream.Rewards,f.Games[1].Rng),
                CrystalSphereLiveCapture.RngState(PredictorStream.Niche,f.Run.Rng.Niche),localSnapshot:true),
                "Local guide still rejects a changed event RNG");
            Console.WriteLine($"CRYSTAL_PARTY_INTERLEAVING wing={wing};partnerCardGroups={partner.Rewards.OfType<CardReward>().Count()};localEventUnchanged=true;localOfferChanged={oldOffersChanged};fingerprintChanged={fingerprintBefore != fingerprintAfter};recaptureParity=true;scope=controlled-sequential-native-generation");
            receipt.Add(new { interleaving = true, wing, oldOffersChanged, fingerprintChanged = fingerprintBefore != fingerprintAfter,
                beforeNiche = source.State.Streams.Single(s => s.Stream == PredictorStream.Niche), afterNiche = after.State.Streams.Single(s => s.Stream == PredictorStream.Niche),
                scope = "controlled generation ordering, not reproduced network timing" });
        }

        void CheckBagIsolation()
        {
            var f = Create(2, 1);
            string localBag = JsonSerializer.Serialize(f.Players[1].RelicGrabBag.ToSerializable());
            string shared = JsonSerializer.Serialize(f.Run.SharedRelicGrabBag.ToSerializable());
            var relic = new RelicReward(f.Players[0]).SetRng(f.Events[0].Rng); relic.Populate();
            Check(localBag == JsonSerializer.Serialize(f.Players[1].RelicGrabBag.ToSerializable()), "Remote relic generation does not dequeue local personal bag");
            Check(shared != JsonSerializer.Serialize(f.Run.SharedRelicGrabBag.ToSerializable()), "Remote relic generation removes from shared bag");
            Console.WriteLine("CRYSTAL_PARTY_BAG remoteGeneration=true;localPersonalUnchanged=true;sharedChanged=true");
        }

        void CheckAdmissionDifferences()
        {
            var f = Create(2, 1);
            var source = CrystalSphereLiveCapture.CaptureScene(f.Run, f.Players[1], f.Events[1], f.Games[1], "0.111.0").Branches.Single().Snapshot;
            var boots = ModelDb.GetById<RelicModel>(new ModelId("RELIC", "WINGED_BOOTS"));
            Check(!boots.IsAllowed(f.Run) && !PredictorRewardGeneration.RelicAllowed(source.Context, source.State, new("RELIC", "WINGED_BOOTS")), "Production capture respects MP Winged Boots eligibility");
            while (f.Run.TotalFloor < 38) f.Run.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Unknown, RoomType.Event, ModelDb.Event<CrystalSphere>().Id);
            var egg = ModelDb.GetById<RelicModel>(new ModelId("RELIC", "MOLTEN_EGG"));
            var at38 = CrystalSphereLiveCapture.CaptureScene(f.Run,f.Players[1],f.Events[1],f.Games[1],"0.111.0").Branches.Single().Snapshot;
            Check(!egg.IsAllowed(f.Run) && !PredictorRewardGeneration.RelicAllowed(at38.Context, at38.State, new("RELIC", "MOLTEN_EGG")), "Production capture respects MP late cutoff at 38");
            Console.WriteLine("CRYSTAL_PARTY_ADMISSION_PARITY wingedBootsAt1=false;moltenEggAt38=false;productionCapture=true");
            receipt.Add(new { admission = true, wingedBootsAt1 = "native false / captured false", moltenEggAt38 = "native false / captured false" });
        }
    }

    private static string NativeState(CrystalPartyFixture f) => JsonSerializer.Serialize(new {
        players = f.Players.Select(p => new { save = p.ToSerializable(), bag = p.RelicGrabBag.ToSerializable() }),
        rng = f.Run.Rng.ToSerializable(), shared = f.Run.SharedRelicGrabBag.ToSerializable(),
        events = f.Events.Select(e => e.Rng.ToSerializable()),
        fingerprints = f.Players.Select((p, i) => CrystalSphereLiveCapture.SceneFingerprint(f.Run, p, f.Events[i], f.Games[i])),
        boards = f.Games.Select(g => new { g.DivinationCount, hidden = g.cells.Cast<CrystalSphereCell>().Select(c => c.IsHidden), items = g.Items.Select(i => i.ToSerializable()) })
    }, PartyNativeJson);

    private static RewardsSet Materialize(CrystalPartyFixture f, int slot, ImmutableArray<PredictorCrystalStep> path)
    {
        var game = f.Games[slot];
        foreach (var step in path)
        {
            Check(game.cells[step.X, step.Y].IsHidden, "Party native click center is hidden");
            game.SetTool(step.Tool == PredictorCrystalTool.Big ? CrystalSphereMinigame.CrystalSphereToolType.Big : CrystalSphereMinigame.CrystalSphereToolType.Small);
            game.CellClicked(game.cells[step.X, step.Y]).GetAwaiter().GetResult();
        }
        var items = (List<CrystalSphereItem>)typeof(CrystalSphereMinigame).GetField("_revealed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var rewards = new RewardsSet(f.Players[slot]).WithCustomRewards(items.Select(i => i.ToReward(f.Players[slot], game.Rng)).OfType<Reward>().ToList());
        rewards.GenerateWithoutOffering().GetAwaiter().GetResult(); return rewards;
    }

    private static void CheckRng(CrystalPartyFixture f, int slot, PredictorRun predicted)
    {
        var rng = f.Games[slot].Rng.ToSerializable(); var actual = predicted.ExportCrystal().EventRng;
        Check(actual.S0 == rng.state0 && actual.S1 == rng.state1 && actual.S2 == rng.state2 && actual.S3 == rng.state3 && actual.Calls == rng.counter, "Party full current-player event RNG parity");
    }

    private static void CheckNonCards(RewardsSet native, PredictorRun predicted)
    {
        var actual = native.Rewards.Where(r => r is not CardReward).Select(r => r switch {
            RelicReward relic => "Relic:" + relic.Relic!.Id.Entry, PotionReward potion => "Potion:" + potion.Potion!.Id.Entry,
            GoldReward gold => "Gold:" + gold.Amount, _ => throw new InvalidOperationException("Unexpected party reward kind") }).Order();
        var expected = predicted.Rewards.Where(r => r.Kind != PredictorRewardKind.Card).Select(r =>
            r.Kind == PredictorRewardKind.Gold ? "Gold:" + r.GoldAmount : r.Kind + ":" + r.Key.Entry).Order();
        Check(actual.SequenceEqual(expected), "Party complete non-card reward multiplicity/identity/gold parity");
    }
}
