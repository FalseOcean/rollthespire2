using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private sealed record DriftFixture(Player Player, RunState Native, CrystalSphereMinigame Game, PredictorCrystalSnapshot Source);
    private sealed record RewardOp(int Slot,string Action,string? Card=null,int? Upgrade=null,CrystalCardEnchantment? Enchantment=null);
    private sealed class RewardScript(Queue<RewardOp> operations) : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int min,int max)
            => throw new InvalidOperationException("Unexpected deck target choice in Driftwood fixture");
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)
        {
            var action=operations.Dequeue();
            return action.Action switch
            {
                "Reroll"=>new() { alternative=alternatives.Single(a=>a.OptionId=="REROLL") },
                "Exit"=>new() { alternative=alternatives.Single(a=>a.OptionId=="Skip") },
                "Sacrifice"=>new() { alternative=alternatives.Single(a=>a.OptionId=="SACRIFICE") },
                "Take"=>new() { card=options.First(c=>c.Card.Id.Entry==action.Card && c.Card.CurrentUpgradeLevel==action.Upgrade &&
                    (action.Enchantment==null || c.Card.Enchantment?.Id.Entry==action.Enchantment.Value.Key?.Entry &&
                        (c.Card.Enchantment?.Amount??0)==action.Enchantment.Value.Amount)).Card },
                _=>throw new InvalidOperationException("Unexpected reward action")
            };
        }
    }

    private static int CheckDriftwoodExperiment(string output,int ascension)
    {
        Directory.CreateDirectory(output);
        var manager=RunManager.Instance;var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var stateProperty=typeof(RunManager).GetProperty("State",flags)!;
        var ascensionProperty=typeof(RunManager).GetProperty("AscensionManager",flags)!;
        var priorState=stateProperty.GetValue(manager);var priorAscension=ascensionProperty.GetValue(manager);var priorLocal=LocalContext.NetId;
        try
        {
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_REACHABILITY")=="1")
                return CheckEnchantmentReachability(output,(seed,extra)=>Create(seed,extra).Source,
                    (source,path,node,extra,take)=>CheckNative(source,path,node,extra,take));
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_EXPERIMENT")=="1")
                return CheckEnchantmentExperiment(output,(seed,extra)=>Create(seed,extra).Source,
                    (source,path,node,extra,take)=>CheckNative(source,path,node,extra,take));
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_RESIDUAL") is { Length:>0 })
                return CheckLanguageResidual(output,(source,plan)=>CheckNative(source,plan.Steps,ReplayProof(source,plan),"",take:true));
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_HOLDOUT")=="1")
                return CheckHoldout(output,ascension,seed=>Create(seed,"").Source,
                    (source,plan)=>CheckNative(source,plan.Steps,ReplayProof(source,plan),"",take:true));
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_SEED_SURVEY")=="1")
                return CheckSeedSurvey(output,ascension,seed=>Create(seed,"").Source,
                    (source,plan)=>CheckNative(source,plan.Steps,ReplayProof(source,plan),"",take:true));
            foreach(var extra in new[]{"", "WING_CHARM", "MOLTEN_EGG", "FRESNEL_LENS", "BING_BONG", "LUCKY_FYSH", "BOOK_OF_FIVE_RINGS"})
            {
                var fixture=Create("000000000000",extra);
                if(extra=="" && Environment.GetEnvironmentVariable("RT2_CRYSTAL_SUPPORT_TRANSFER")=="stats")
                {
                    CheckSupportTransferPrevalence(fixture.Source,output);
                    foreach(string seed in new[]{"9TW7Y8N6663H","AAJ82GDRLS09","CKF62MPWQBL7"})
                        CheckSupportTransferPrevalence(Create(seed,"").Source,output);
                    Console.WriteLine("CRYSTAL_SUPPORT_TRANSFER_STATS_PASS readOnlyTable=true;noProofClaim=true");
                    return 0;
                }
                if(extra=="" && (Environment.GetEnvironmentVariable("RT2_CRYSTAL_SUPPORT_TRANSFER")=="sweep" ||
                    Environment.GetEnvironmentVariable("RT2_CRYSTAL_ROOT_PRODUCTION")=="1"))
                {
                    bool production=Environment.GetEnvironmentVariable("RT2_CRYSTAL_ROOT_PRODUCTION")=="1";
                    void Sweep(PredictorCrystalSnapshot source,ImmutableArray<CrystalRewardOption> selected,CrystalRewardOption? first)
                    {
                        if(production && PackingCoverEnabled) CheckPackingProduction(source,selected,output);
                        else if(production) CheckRootSlotProduction(source,selected,
                            (steps,node)=>CheckNative(source,steps,node,"",take:true),output);
                        else CheckSupportTransferSweep(source,selected,first,
                            (steps,node)=>CheckNative(source,steps,node,"",take:true),output);
                    }
                    var fixedPath=RequiredItemPath(fixture.Source,i=>i.Kind=="RELIC" || i.Kind=="CARD_COMMON");
                    var fixedRewards=PredictorCrystalExplorer.Replay(fixture.Source,fixedPath).Rewards;
                    var relic=fixedRewards.First(r=>r.Kind==PredictorRewardKind.Relic);
                    var common=fixedRewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards[0];
                    Sweep(fixture.Source,[new(PredictorRewardKind.Relic,relic.Key),
                        new(PredictorRewardKind.Card,common.Key,common.UpgradeLevel)],new(PredictorRewardKind.Card,new("CARD","STAMPEDE"),1));
                    foreach(string seed in new[]{"9TW7Y8N6663H","AAJ82GDRLS09","CKF62MPWQBL7"})
                    {
                        var natural=Create(seed,"");var naturalRun=PredictorCrystalExplorer.Replay(natural.Source,CardPath(natural.Source));
                        var card=naturalRun.Rewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards[0];
                        Sweep(natural.Source,[new(PredictorRewardKind.Card,card.Key,card.UpgradeLevel)],null);
                    }
                    Console.WriteLine(production && PackingCoverEnabled?"CRYSTAL_PACKING_PRODUCTION_PASS initialProjection=true;noGameInstallation=true":
                        production?"CRYSTAL_ROOT_PRODUCTION_PASS nativePositives=true;productionQueries=true;noGameInstallation=true":
                        "CRYSTAL_SUPPORT_TRANSFER_SWEEP_PASS nativePositives=true;sameFinalNegativeCache=true;noProductionDispatch=true");
                    return 0;
                }
                var path=CardPath(fixture.Source);
                var root=PredictorCrystalExplorer.Replay(fixture.Source,path);
                Check(root.Rewards.Count(r=>r.Kind==PredictorRewardKind.Card)==3,"Natural board path reveals all three card groups");
                var before=JsonSerializer.Serialize(fixture.Source,ProductionSearchReplay.JsonOptions);
                var watch=System.Diagnostics.Stopwatch.StartNew();
                var prefixes=PredictorCrystalRerolls.Prefixes(root).ToArray();
                Check(prefixes.Length==16,"Three reward instances have sixteen ordered reroll subsets");
                Console.WriteLine($"DRIFT_PREFIX seed={fixture.Source.Context.Seed};extra={extra};nodes={prefixes.Length};ms={watch.Elapsed.TotalMilliseconds:F3};initialCalls={root.ExportCrystal().EventRng.Calls}");
                foreach(var node in prefixes) CheckNative(fixture.Source,path,node,extra,take:false);
                Check(before==JsonSerializer.Serialize(fixture.Source,ProductionSearchReplay.JsonOptions),"Suffix traversal preserves frozen source");

                var cardRewards=root.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).ToArray();
                var white=cardRewards.Single(r=>r.RerollRecipe!.Rarity==RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common);
                var blue=cardRewards.Single(r=>r.RerollRecipe!.Rarity==RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon);
                var gold=cardRewards.Single(r=>r.RerollRecipe!.Rarity==RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Rare);
                var wg=prefixes.Single(n=>n.Actions.OfType<RerollCardReward>().Select(r=>r.RewardId).SequenceEqual(new[]{white.Id,gold.Id}));
                CrystalRewardOption Option(PredictorCard c)=>new(PredictorRewardKind.Card,c.Key,c.UpgradeLevel);
                var a=wg.Run.Rewards.Single(r=>r.Id==white.Id).Cards.Select(Option).First(o=>!white.Cards.Any(o.MatchesCard));
                var c=wg.Run.Rewards.Single(r=>r.Id==gold.Id).Cards.Select(Option).First(o=>!gold.Cards.Any(o.MatchesCard));
                ImmutableArray<CrystalRewardOption> goals=[a,Option(blue.Cards[0]),c];
                Check(PredictorCrystalExplorer.VerifySelection(fixture.Source,new(path,root.Rewards),goals)==null,"Old fixed-endpoint verifier misses reroll-only targets");
                var proof=PredictorCrystalRerolls.Take(wg,goals);
                Check(proof!=null,"White A / initial blue B / gold C can all be taken on one suffix");
                CheckNative(fixture.Source,path,proof!,extra,take:true);
                CheckContinuationWitness(fixture.Source,path,goals);
                CheckNative(fixture.Source,path,proof!,extra,take:true,immediateGuide:true);
                CheckNative(fixture.Source,path,proof!,extra,take:true,rerollOnlyGuide:true);
                ProductionSearchReplay.Write(Path.Combine(output,"snapshot"+(extra==""?"":"-"+extra)+".json"),fixture.Source);
                ProductionSearchReplay.Write(Path.Combine(output,"witness"+(extra==""?"":"-"+extra)+".json"),new {path,goals,operations=Operations(root,proof!),scope="natural generated board + controlled Act2 inventory; native selection parity"});
                CheckGuards(root);
                var rawOutcomes = CheckNormalization(root);
                CheckConditionalSuffix(prefixes, rawOutcomes, goals, extra);
                if(extra=="")
                {
                    var early=prefixes.Where(n=>n.Actions.OfType<RerollCardReward>().Count()<3)
                        .SelectMany(n=>n.Run.Rewards.Where(r=>r.Id==blue.Id).SelectMany(r=>r.Cards.Select(Option))).ToHashSet();
                    var deep=prefixes.Where(n=>n.Actions.OfType<RerollCardReward>().LastOrDefault()?.RewardId==blue.Id && n.Actions.OfType<RerollCardReward>().Count()==3)
                        .Select(n=>(Node:n,Goal:n.Run.Rewards.Single(r=>r.Id==blue.Id).Cards.Select(Option).FirstOrDefault(o=>!early.Contains(o))))
                        .FirstOrDefault(n=>n.Goal!=null);
                    Check(deep.Goal!=null,"Observed blue target requires two preceding rerolls at this endpoint");
                    var deepProof=PredictorCrystalRerolls.Take(deep.Node,[deep.Goal!]);
                    Check(deepProof!=null,"Deep blue target has a typed take witness");
                    Check(!prefixes.Any(n=>PredictorCrystalRerolls.Take(n,[Option(blue.Cards[0]),deep.Goal!])!=null),
                        "Individually reachable old/new blue cards cannot both be taken from this one blue reward instance");
                    CheckNative(fixture.Source,path,deepProof!,extra,take:true);
                    Console.WriteLine($"DRIFT_DEEP_BLUE target={deep.Goal};rerolls=3;shorterPrefixesExcludedWithinFixedEndpoint=true");
                    CheckSmallBoard(fixture.Source);
                    if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_CONTINUATION_COMPARE")=="1")
                        CompareContinuationSearch(fixture.Source,output,(steps,node)=>CheckNative(fixture.Source,steps,node,"",take:true));
                    if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_DRIFTWOOD_FRONTIER")=="1")
                        CheckRerollFrontier(fixture.Source,path,goals,(steps,node)=>CheckNative(fixture.Source,steps,node,"",take:true),output);
                }
            }
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_SUPPORT_TRANSFER")!="oracle" &&
                (Environment.GetEnvironmentVariable("RT2_CRYSTAL_DRIFTWOOD_FRONTIER")=="1" ||
                Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_FIRST_EXPERIMENT")=="1"))
                foreach(string seed in new[]{"9TW7Y8N6663H","AAJ82GDRLS09","CKF62MPWQBL7"})
                {
                    var fixture=Create(seed,"");
                    CheckDriftwoodProductionQueries(fixture.Source,
                        (steps,node)=>CheckNative(fixture.Source,steps,node,"",take:true),output);
                }
            CheckCrystalPartyExperiment(output, ascension);
            CheckCrystalModCapture();
            Console.WriteLine("CRYSTAL_DRIFTWOOD_EXPERIMENT_PASS native generation + selection lifecycle; exact fixed endpoints; controlled board; party read-only prototype; not product search coverage or Owner game acceptance");
            return 0;
        }
        finally {stateProperty.SetValue(manager,priorState);ascensionProperty.SetValue(manager,priorAscension);LocalContext.NetId=priorLocal;}

        DriftFixture Create(string seed,string extra)
        {
            var player=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);player.Gold=200;
            var native=RunState.CreateForTest([player],seed:seed,ascensionLevel:ascension);native.CurrentActIndex=EnchantmentChecksEnabled?2:1;
            stateProperty.SetValue(manager,native);ascensionProperty.SetValue(manager,new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(ascension));
            LocalContext.NetId=player.NetId;
            native.AppendToMapPointHistory(MapPointType.Unknown,RoomType.Event,ModelDb.Event<CrystalSphere>().Id);
            player.AddRelicInternal(ModelDb.GetById<RelicModel>(new ModelId("RELIC","DRIFTWOOD")).ToMutable(),silent:true);
            foreach(string relic in extra.Split('+',StringSplitOptions.RemoveEmptyEntries))
                player.AddRelicInternal(ModelDb.GetById<RelicModel>(new ModelId("RELIC",relic)).ToMutable(),silent:true);
            player.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
            native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState),native.Rng.UpFront);
            var ev=(CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();ev.BeginEvent(player,null,false).GetAwaiter().GetResult();
            CardPileCmd.AddCurseToDeck<MegaCrit.Sts2.Core.Models.Cards.Debt>(player).GetAwaiter().GetResult();
            var game=new CrystalSphereMinigame(player,ev.Rng,6);
            string toolFingerprint=CrystalSphereLiveCapture.SceneFingerprint(native,player,ev,game);
            game.SetTool(CrystalSphereMinigame.CrystalSphereToolType.Small);
            Check(toolFingerprint==CrystalSphereLiveCapture.SceneFingerprint(native,player,ev,game),"Free tool selection retains the same search context");
            game.SetTool(CrystalSphereMinigame.CrystalSphereToolType.Big);
            var source=CrystalSphereLiveCapture.CaptureScene(native,player,ev,game,"0.111.0").Branches.Single().Snapshot;
            return new(player,native,game,source);
        }

        void CheckNative(PredictorCrystalSnapshot source,ImmutableArray<PredictorCrystalStep> path,CrystalSuffixNode expected,string extra,bool take,bool immediateGuide=false,bool rerollOnlyGuide=false)
        {
            var f=Create(source.Context.Seed,extra);
            Check(f.Source.Items.SequenceEqual(source.Items) && f.Source.EventRng==source.EventRng,"Native board reproduces frozen source");
            foreach(var step in path)
            {
                Check(f.Game.cells[step.X,step.Y].IsHidden,"Every native click center is hidden");
                f.Game.SetTool(step.Tool==PredictorCrystalTool.Big?CrystalSphereMinigame.CrystalSphereToolType.Big:CrystalSphereMinigame.CrystalSphereToolType.Small);
                f.Game.CellClicked(f.Game.cells[step.X,step.Y]).GetAwaiter().GetResult();
            }
            var items=(List<CrystalSphereItem>)typeof(CrystalSphereMinigame).GetField("_revealed",flags)!.GetValue(f.Game)!;
            var set=new RewardsSet(f.Player).WithCustomRewards(items.Select(i=>i.ToReward(f.Player,f.Game.Rng)).OfType<Reward>().ToList());
            set.GenerateWithoutOffering().GetAwaiter().GetResult();
            var initial=PredictorCrystalExplorer.Replay(source,path);
            Check(CardSignature(set.Rewards.OfType<CardReward>())==CardSignature(initial.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card)),"Native initial full offers");
            var operations=Operations(initial,expected);
            var savedPlan=new PredictorCrystalSolution("Found",0,path,operations.Where(o=>o.Action=="Take")
                .Select(o=>new PredictorCrystalTake(o.Slot,new("CARD",o.Card!),o.Upgrade,o.Enchantment)).ToImmutableArray()) {
                RerollRewardIndices=operations.Where(o=>o.Action=="Reroll").Select(o=>o.Slot).ToImmutableArray() };
            savedPlan=JsonSerializer.Deserialize<PredictorCrystalSolution>(JsonSerializer.Serialize(savedPlan,ProductionSearchReplay.JsonOptions),ProductionSearchReplay.JsonOptions)!;
            var guide=rerollOnlyGuide?PredictorCrystalRewardGuide.BuildRerollGuide(source,savedPlan):
                PredictorCrystalRewardGuide.Build(source,savedPlan,immediateTakes:immediateGuide);
            if(immediateGuide || rerollOnlyGuide)
            {
                var continuation=initial.ForkCrystalRewards();var inputs=ImmutableArray.CreateBuilder<PredictorInput>();
                void Apply(PredictorInput input)
                { Check(continuation.Submit(continuation.Request!,input)==PredictorInputResult.Accepted,"Reward guide input valid");inputs.Add(input); }
                foreach(var action in guide.Actions)
                {
                    var reward=continuation.Rewards[action.RewardIndex];
                    if(continuation.CrystalSelectedReward!=reward.Id)
                    {
                        if(continuation.CrystalSelectedReward!=null) Apply(new ExitCardSelection());
                        Apply(new EnterCardSelection(reward.Id));
                    }
                    if(action.Reroll) Apply(new RerollCardReward(reward.Id));
                    else Apply(new TakeReward(reward.Id,reward.Cards.First(c=>savedPlan.Takes.Any(t=>t.RewardIndex==action.RewardIndex && t.MatchesCard(c))).Id));
                }
                if(continuation.CrystalSelectedReward!=null) Apply(new ExitCardSelection());
                expected=new(continuation,inputs.ToImmutable());operations=Operations(initial,expected);
                if(rerollOnlyGuide)
                {
                    Check(guide.Actions.All(a=>a.Reroll) && guide.Actions.Select(a=>a.RewardIndex).SequenceEqual(savedPlan.RerollRewardIndices),
                        "Collection-free guide preserves exactly the planned reroll order");
                    Check(continuation.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).All(r=>r.Status==PredictorRewardStatus.Open),
                        "All three card groups remain open, including the unrerolled group");
                    Check(guide.Frames.All(f=>f.Taken.IsEmpty),"Collection-free observation frames never require card acquisition");
                    var noRerolls=PredictorCrystalRewardGuide.BuildRerollGuide(source,new("Found",0,path,[]));
                    Check(noRerolls.Actions.IsEmpty && noRerolls.Frames.Length==1,"No rerolls means immediate manual collection");
                }
                else Check(Episodes(operations).Any(e=>e.Any(o=>o.Action=="Reroll") && e.Any(o=>o.Action=="Take")),
                    "Guide takes the target immediately after its reroll without exiting");
            }
            var binding=CrystalSphereRewardObservation.Bind(guide.Frames[0],set.Rewards.OfType<CardReward>().Reverse());
            Check(binding!=null,"Reward guide binds by contents despite native display sorting");
            int guideStep=0;
            bool GuideMatches(int step)=>CrystalSphereRewardObservation.Matches(guide.Frames[step],binding!,
                CrystalSphereLiveCapture.RngState(PredictorStream.Rewards,f.Game.Rng),
                CrystalSphereLiveCapture.RngState(PredictorStream.Niche,f.Native.Rng.Niche));
            Check(GuideMatches(0),"Serialized guide matches native initial rewards");
            if(rerollOnlyGuide && extra=="")
            {
                RelicCmd.Obtain<MegaCrit.Sts2.Core.Models.Relics.Vajra>(f.Player).GetAwaiter().GetResult();
                Check(GuideMatches(0),"Collecting an ordinary relic keeps the actual reward offers and reroll guide valid");
                var eventState=CrystalSphereLiveCapture.RngState(PredictorStream.Rewards,f.Game.Rng);
                Check(!CrystalSphereRewardObservation.Matches(guide.Frames[0],binding!,eventState with {Calls=eventState.Calls+1},
                    CrystalSphereLiveCapture.RngState(PredictorStream.Niche,f.Native.Rng.Niche),localSnapshot:true),
                    "A genuinely changed event RNG still fails reward-frame matching");
                Console.WriteLine("CRYSTAL_RELIC_COLLECTION_GUIDE_PASS nativeVajraAcquisition=true;offersAndRerollRngUnchanged=true;realRngMismatchRejected=true");
            }
            var service=new NetSingleplayerGameService();using var sync=new PlayerChoiceSynchronizer(service,f.Native);
            foreach(var reward in set.Rewards.OfType<CardReward>()) typeof(CardReward).GetField("_synchronizer",flags)!.SetValue(reward,sync);
            foreach(var episode in Episodes(operations))
            {
                var reward=(CardReward)set.Rewards[episode[0].Slot];
                if(take)
                {
                    var queue=new Queue<RewardOp>(episode.Skip(1));
                    using(var selector=CardSelectCmd.UseSelector(new RewardScript(queue))) reward.SelectUnsynchronized().GetAwaiter().GetResult();
                    Check(queue.Count==0,"Native selector consumed exactly the planned episode");
                }
                else foreach(var op in episode) if(op.Action=="Reroll") reward.Reroll();
                guideStep+=episode.Count(o=>o.Action is "Reroll" or "Take");
                Check(GuideMatches(guideStep),"Guide observes native reroll/take without submitting inputs");
            }
            Check(guideStep==guide.Actions.Length,"Guide covers complete native suffix");
            var nativeOpen=set.Rewards.OfType<CardReward>().Where(r=>!r.SuccessfullySelected);
            var predictedOpen=expected.Run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card && r.Status==PredictorRewardStatus.Open);
            Check(CardSignature(nativeOpen)==CardSignature(predictedOpen),"Native open offers/upgrade/enchantment parity after suffix");
            var rng=f.Game.Rng.ToSerializable();var final=expected.Run.ExportCrystal().EventRng;
            Check(final.S0==rng.state0 && final.S1==rng.state1 && final.S2==rng.state2 && final.S3==rng.state3 && final.Calls==rng.counter,"Native complete event RNG parity after suffix");
            var niche=f.Native.Rng.Niche.ToSerializable();var projectedNiche=expected.Run.Working.Streams.Single(s=>s.Stream==PredictorStream.Niche);
            Check(projectedNiche.S0==niche.state0 && projectedNiche.S1==niche.state1 && projectedNiche.S2==niche.state2 && projectedNiche.S3==niche.state3 && projectedNiche.Calls==niche.counter,"Native Niche continuation parity");
            if(take)
            {
                var nativeDeck=f.Player.Deck.Cards.Select(c=>$"{c.Id.Entry}:{c.CurrentUpgradeLevel}").Order();
                var rtDeck=expected.Run.Working.Deck.Select(expected.Run.Working.Card).Select(c=>$"{c.Key.Entry}:{c.UpgradeLevel}").Order();
                Check(nativeDeck.SequenceEqual(rtDeck),"Native selected cards actually reach Deck with expected versions");
                if(EnchantmentChecksEnabled)
                    Check(f.Player.Deck.Cards.Select(c=>$"{c.Id.Entry}:{c.CurrentUpgradeLevel}:{c.Enchantment?.Id.Entry}:{c.Enchantment?.Amount??0}").Order().SequenceEqual(
                        expected.Run.Working.Deck.Select(expected.Run.Working.Card).Select(EnchantedIdentity).Order()),"Native acquired card enchantments and amounts match");
                Console.WriteLine($"DRIFT_NATIVE_TAKE extra={extra};immediateGuide={immediateGuide};rerollOnlyGuide={rerollOnlyGuide};calls={rng.counter};operations={JsonSerializer.Serialize(operations)}");
            }
        }
    }

    private static string CardSignature(IEnumerable<CardReward> rewards)=>string.Join(';',rewards.Select(r=>string.Join(',',r.Cards.Select(c=>$"{c.Id.Entry}:{c.CurrentUpgradeLevel}:{c.Enchantment?.Id.Entry}:{c.Enchantment?.Amount}"))));
    private static string CardSignature(IEnumerable<PredictorReward> rewards)=>string.Join(';',rewards.Select(r=>string.Join(',',r.Cards.Select(c=>$"{c.Key.Entry}:{c.UpgradeLevel}:{c.Enchantment?.Entry}:{(c.Enchantment==null?null:(decimal?)c.EnchantmentAmount)}"))));

    private static List<RewardOp> Operations(PredictorRun root,CrystalSuffixNode node)
    {
        var copy=root.ForkCrystalRewards();var result=new List<RewardOp>();
        foreach(var action in node.Actions)
        {
            long id=action switch {EnterCardSelection e=>e.RewardId,RerollCardReward r=>r.RewardId,TakeReward t=>t.RewardId,ExitCardSelection=>copy.CrystalSelectedReward!.Value,_=>throw new InvalidOperationException()};
            int slot=copy.Rewards.FindIndex(r=>r.Id==id);
            var card=action is TakeReward take?copy.Rewards[slot].Cards.Single(c=>c.Id==take.CardId):null;
            result.Add(new(slot,action switch {EnterCardSelection=>"Enter",RerollCardReward=>"Reroll",ExitCardSelection=>"Exit",TakeReward=>"Take",_=>"?"},card?.Key.Entry,card?.UpgradeLevel,
                card!=null && EnchantmentChecksEnabled?CrystalCardEnchantment.Of(card):null));
            Check(copy.Submit(copy.Request!,action)==PredictorInputResult.Accepted,"Serialized suffix replays typed inputs");
        }
        return result;
    }
    private static IEnumerable<List<RewardOp>> Episodes(List<RewardOp> script)
    {
        List<RewardOp> current=[];
        foreach(var op in script) {current.Add(op);if(op.Action is "Exit" or "Take") {yield return current;current=[];}}
        Check(current.Count==0,"Reward episode terminates legally");
    }

    private static void CheckGuards(PredictorRun root)
    {
        var run=root.ForkCrystalRewards();var reward=run.Rewards.First(r=>r.Kind==PredictorRewardKind.Card);
        Check(run.Submit(run.Request!,new EnterCardSelection(reward.Id))==PredictorInputResult.Accepted,"Enter reroll selection");
        var priorRequest=run.Request!;long oldCard=reward.Cards[0].Id;
        Check(run.Submit(priorRequest,new RerollCardReward(reward.Id))==PredictorInputResult.Accepted,"First reroll accepted");
        string Snapshot()=>JsonSerializer.Serialize(new {run.Working,run.Rewards,Rng=run.ExportCrystal().EventRng},ProductionSearchReplay.JsonOptions);
        string frozen=Snapshot();
        Check(run.Submit(priorRequest,new TakeReward(reward.Id,oldCard))==PredictorInputResult.Stale,"Old request rejected after reroll");
        Check(run.Submit(run.Request!,new TakeReward(reward.Id,oldCard))==PredictorInputResult.Invalid,"Old offer instance rejected under new request");
        Check(run.Submit(run.Request!,new RerollCardReward(reward.Id))==PredictorInputResult.NotAllowed,"Second reroll rejected");
        Check(frozen==Snapshot(),"Rejected inputs do not mutate any continuation state");
        Check(run.Submit(run.Request!,new ExitCardSelection())==PredictorInputResult.Accepted,"Exit preserves rerolled open reward");
        Check(!run.Rewards.Single(r=>r.Id==reward.Id).RelicSubscriptionActive,"Exit removes original relic-obtained subscription");
        Check(run.Submit(run.Request!,new EnterCardSelection(reward.Id))==PredictorInputResult.Accepted,"Can re-enter the same rerolled reward");
    }

    private static string Obtained(PredictorRun root,PredictorRun state,bool enchanted=false)=>string.Join('|',state.Working.Deck.Where(id=>!root.Working.Deck.Contains(id))
        .Select(state.Working.Card).Select(c=>enchanted?EnchantedIdentity(c):$"{c.Key.Entry}:{c.UpgradeLevel}").Order());

    private static HashSet<string> CheckNormalization(PredictorRun root,bool enchanted=false)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();var seen=new HashSet<string>();var queue=new Queue<PredictorRun>();queue.Enqueue(root.ForkCrystalRewards());
        var raw=new HashSet<string>();long edges=0;
        while(queue.TryDequeue(out var run))
        {
            string key=JsonSerializer.Serialize(new {run.Working,run.Rewards,Rng=run.ExportCrystal().EventRng,Active=run.CrystalSelectedReward},ProductionSearchReplay.JsonOptions);
            if(!seen.Add(key)) continue;
            if(seen.Count>100_000) throw new InvalidOperationException("Driftwood oracle work cap: no completeness claim");
            raw.Add(Obtained(root,run,enchanted));
            var actions=new List<PredictorInput>();
            if(run.CrystalSelectedReward is { } active)
            {
                var reward=run.Rewards.Single(r=>r.Id==active);
                actions.Add(new ExitCardSelection());
                if(reward.CanReroll) actions.Add(new RerollCardReward(active));
                actions.AddRange(reward.Cards.Select(c=>new TakeReward(active,c.Id)));
            }
            else actions.AddRange(run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card && r.Status==PredictorRewardStatus.Open).Select(r=>new EnterCardSelection(r.Id)));
            foreach(var action in actions)
            {
                var copy=run.ForkCrystalRewards();Check(copy.Submit(copy.Request!,action)==PredictorInputResult.Accepted,"Raw reward operation is legal");queue.Enqueue(copy);edges++;
            }
        }
        var reduced=new HashSet<string>();
        void Picks(PredictorRun run,long[] rewards,int depth)
        {
            if(depth==rewards.Length) {reduced.Add(Obtained(root,run,enchanted));return;}
            Picks(run,rewards,depth+1);
            foreach(var card in run.Rewards.Single(r=>r.Id==rewards[depth]).Cards)
            {
                var copy=run.ForkCrystalRewards();
                Check(copy.Submit(copy.Request!,new EnterCardSelection(rewards[depth]))==PredictorInputResult.Accepted &&
                    copy.Submit(copy.Request!,new TakeReward(rewards[depth],card.Id))==PredictorInputResult.Accepted,"Reduced take sequence is legal");
                Picks(copy,rewards,depth+1);
            }
        }
        foreach(var node in PredictorCrystalRerolls.Prefixes(root))
            Picks(node.Run,node.Run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).Select(r=>r.Id).ToArray(),0);
        Check(raw.SetEquals(reduced),"Actual Runtime raw Enter/Exit/Reroll/Take outcomes equal take-deferred results for this controlled inventory");
        Console.WriteLine($"DRIFT_NORMALIZATION groups={root.Rewards.Count(r=>r.Kind==PredictorRewardKind.Card)};states={seen.Count};edges={edges};goalMultisets={raw.Count};equal=true;ms={watch.Elapsed.TotalMilliseconds:F3};enchantments={enchanted};scope=fixed-endpoint-obtained-multisets");
        return raw;
    }

    private static void CheckConditionalSuffix(CrystalSuffixNode[] prefixes, HashSet<string> raw,
        ImmutableArray<CrystalRewardOption> goals, string extra)
    {
        var universe = prefixes.SelectMany(n => n.Run.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).SelectMany(r => r.Cards))
            .Select(c => new CrystalRewardOption(PredictorRewardKind.Card, c.Key, c.UpgradeLevel)).Distinct().ToArray();
        string Key(CrystalRewardOption g) => $"{g.Key.Entry}:{g.UpgradeLevel}";
        foreach (int selectedCount in new[] { 0, 1, 2, 3 })
        {
            var selected = goals.Take(selectedCount).ToImmutableArray(); var watch = System.Diagnostics.Stopwatch.StartNew();
            var compatible = universe.Where(g => !selected.Contains(g) && prefixes.Any(n => PredictorCrystalRerolls.Take(n, selected.Add(g)) != null)).ToHashSet();
            double queryMs = watch.Elapsed.TotalMilliseconds;
            var exact = universe.Where(g => !selected.Contains(g) && raw.Any(outcome =>
            {
                var cards = outcome.Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                return selected.Append(g).All(goal => cards.Contains(Key(goal)));
            })).ToHashSet();
            Check(compatible.SetEquals(exact), "Conditional A -> A+B -> A+B+C candidates match raw legal reward-action oracle at this endpoint");
            Console.WriteLine($"DRIFT_CONDITIONAL extra={extra};selected={selectedCount};universe={universe.Length};compatible={compatible.Count};queryMs={queryMs:F3};withOracleMs={watch.Elapsed.TotalMilliseconds:F3};rawOracleEqual=true;scope=fixed-endpoint-key-upgrade-goals");
        }
    }

    private static void CheckSmallBoard(PredictorCrystalSnapshot source)
    {
        var hidden=new bool[121];var occupancy=Enumerable.Repeat(-1,121).ToArray();
        for(int x=2;x<4;x++) for(int y=2;y<4;y++) {hidden[x*11+y]=true;occupancy[x*11+y]=0;}
        hidden[8*11+8]=true;hidden[10*11+10]=true;
        var board=source with {Remaining=3,Hidden=hidden.ToImmutableArray(),Occupancy=occupancy.ToImmutableArray(),
            Items=[new("CARD_COMMON",2,2,2,2,1,false)],Revealed=[]};
        ImmutableArray<PredictorCrystalStep> witness=[new(2,2,PredictorCrystalTool.Big),new(8,8,PredictorCrystalTool.Small),new(10,10,PredictorCrystalTool.Small)];
        var root=PredictorCrystalExplorer.Replay(board,witness);var rerolled=PredictorCrystalRerolls.Prefixes(root).Single(n=>n.Actions.Length>0);
        var initial=root.Rewards.Single().Cards;
        var target=rerolled.Run.Rewards.Single().Cards.Select(c=>new CrystalRewardOption(PredictorRewardKind.Card,c.Key,c.UpgradeLevel)).First(o=>!initial.Any(o.MatchesCard));
        var table=new CrystalReachabilityTable(board);var old=new PredictorCrystalOffsetBound(board,table,true).Check([target]);
        Check(old.Verdict==CrystalOffsetVerdict.Impossible,"Old initial-only offset proof excludes this reroll-only target");
        Check(PredictorCrystalRerolls.Take(rerolled,[target])!=null,"Expanded action domain has a legal click plus reroll take witness");
        long paths=0;var rows=new HashSet<string>();var expanded=new HashSet<string>();var reduced=new HashSet<string>();
        void Visit(ImmutableArray<PredictorCrystalStep> steps)
        {
            var run=PredictorCrystalExplorer.Replay(board,steps);
            if(steps.Length==board.Remaining)
            {
                paths++;if(run.Phase!=PredictorPhase.Rewards) return;
                string key=JsonSerializer.Serialize(new {run.Working,run.Rewards,Rng=run.ExportCrystal().EventRng},ProductionSearchReplay.JsonOptions);
                if(!rows.Add(key)) return;
                foreach(var n in PredictorCrystalRerolls.Prefixes(run))
                    foreach(var c in n.Run.Rewards.Single().Cards)
                    {
                        var goal=new CrystalRewardOption(PredictorRewardKind.Card,c.Key,c.UpgradeLevel);
                        if(PredictorCrystalRerolls.Take(n,[goal])!=null) reduced.Add($"{c.Key.Entry}:{c.UpgradeLevel}");
                    }
                // One reward: raw legal operations still include viewing cycles,
                // old/new exclusivity and actual take hooks, not a lookup fixture.
                var queue=new Queue<PredictorRun>();queue.Enqueue(run);var seen=new HashSet<string>();
                while(queue.TryDequeue(out var s))
                {
                    string state=JsonSerializer.Serialize(new {s.Working,s.Rewards,Rng=s.ExportCrystal().EventRng,Active=s.CrystalSelectedReward},ProductionSearchReplay.JsonOptions);
                    if(!seen.Add(state)) continue;
                    foreach(long id in s.Working.Deck.Where(id=>!run.Working.Deck.Contains(id))) {var c=s.Working.Card(id);expanded.Add($"{c.Key.Entry}:{c.UpgradeLevel}");}
                    var r=s.Rewards.Single();var actions=new List<PredictorInput>();
                    if(r.Status!=PredictorRewardStatus.Open) continue;
                    if(s.CrystalSelectedReward==null) actions.Add(new EnterCardSelection(r.Id));
                    else {actions.Add(new ExitCardSelection());if(r.CanReroll) actions.Add(new RerollCardReward(r.Id));actions.AddRange(r.Cards.Select(c=>new TakeReward(r.Id,c.Id)));}
                    foreach(var action in actions) {var copy=s.ForkCrystalRewards();Check(copy.Submit(copy.Request!,action)==PredictorInputResult.Accepted,"Controlled raw suffix");queue.Enqueue(copy);}
                }
                return;
            }
            var live=run.ExportCrystal();
            for(int cell=0;cell<121;cell++) if(live.Hidden[cell])
                foreach(var tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big}) Visit(steps.Add(new(cell/11,cell%11,tool)));
        }
        Visit([]);Check(expanded.SetEquals(reduced) && expanded.Contains($"{target.Key.Entry}:{target.UpgradeLevel}"),"Raw legal small-board click 脳 suffix oracle agrees with endpoint expansion");
        CheckFocusedQueries(board,target,expanded);
        CheckProbeRetention(source);
        Console.WriteLine($"DRIFT_SMALL_BOARD rawPaths={paths};rewardEndpoints={rows.Count};candidates={expanded.Count};target={target};oldInitialProof={old.Verdict};expandedWitness=true;scope=controlled-geometry-real-runtime-generation");
    }

    private static ImmutableArray<PredictorCrystalStep> CardPath(PredictorCrystalSnapshot source)
        => RequiredItemPath(source, item => item.Kind.StartsWith("CARD_"));

    private static ImmutableArray<PredictorCrystalStep> RequiredItemPath(PredictorCrystalSnapshot source, Func<PredictorCrystalItem, bool> requiredItem,int stateLimit=int.MaxValue)
    {
        UInt128 Mask(PredictorCrystalItem item)=>(from x in Enumerable.Range(item.X,item.Width) from y in Enumerable.Range(item.Y,item.Height) select (UInt128)1<<(x*11+y)).Aggregate((UInt128)0,(a,b)=>a|b);
        UInt128 fog=0;for(int i=0;i<121;i++) if(source.Hidden[i]) fog|=(UInt128)1<<i;
        var masks=source.Items.Where(i=>i.X>=0).Select(Mask).ToArray();
        UInt128 required=source.Items.Where(i=>i.X>=0 && requiredItem(i)).Select(Mask).Aggregate((UInt128)0,(a,b)=>a|b);
        UInt128 curse=source.Items.Where(i=>i.X>=0 && i.Kind=="CURSE").Select(Mask).Aggregate((UInt128)0,(a,b)=>a|b);
        var seen=new HashSet<(UInt128,int)>();
        ImmutableArray<PredictorCrystalStep>? Visit(UInt128 current,ImmutableArray<PredictorCrystalStep> path)
        {
            if(seen.Count>=stateLimit) return null;
            if(!seen.Add((current,path.Length))) return null;
            if((current&required)==0)
            {
                var padded=path;for(int cell=0;cell<121 && padded.Length<source.Remaining;cell++)
                    if((current&((UInt128)1<<cell))!=0 && source.Occupancy[cell]<0) {padded=padded.Add(new(cell/11,cell%11,PredictorCrystalTool.Small));current&=~((UInt128)1<<cell);}
                return padded.Length==source.Remaining?padded:null;
            }
            if(path.Length==source.Remaining) return null;
            var moves=(from center in Enumerable.Range(0,121) where (current&((UInt128)1<<center))!=0
                let mask=PredictorCrystalSearch.Cells(center/11,center%11,PredictorCrystalTool.Big).Aggregate((UInt128)0,(a,c)=>a|((UInt128)1<<(c.X*11+c.Y)))
                let next=current&~mask where (current&required)!=(next&required) && (next&curse)!=0
                select (Center:center,Next:next,Gain:System.Numerics.BitOperations.PopCount((ulong)(current&required&mask))+System.Numerics.BitOperations.PopCount((ulong)((current&required&mask)>>64)))).OrderByDescending(m=>m.Gain);
            foreach(var m in moves) if(Visit(m.Next,path.Add(new(m.Center/11,m.Center%11,PredictorCrystalTool.Big))) is { } found) return found;
            return null;
        }
        return Visit(fog,[])??throw new InvalidOperationException(seen.Count>=stateLimit?"Required-item construction state cutoff; reachability unknown":"No legal required-item path in controlled native fixture");
    }
}
