using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private sealed record WingFixture(Player Player,RunState Native,CrystalSphere Event,CrystalSphereMinigame Game,
        PredictorCrystalSnapshot Source,string Inventory,int? Counter,string Bag);
    private sealed record WingLanguageObservation(ImmutableArray<CrystalRewardOption> Selected,CrystalRewardOption? Target,
        CrystalGoalSearchResult Result,double ObservedMs);

    // Opt-in extension of the existing native Crystal host, not a separate test
    // harness. Inventories deliberately exclude Driftwood: vanilla supports at
    // most two alternatives (Skip + Sacrifice).
    private static int CheckPaelsWing(string output,int ascension)
    {
        Directory.CreateDirectory(output);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var manager=RunManager.Instance;
        var stateProperty=typeof(RunManager).GetProperty("State",flags)!;
        var ascensionProperty=typeof(RunManager).GetProperty("AscensionManager",flags)!;
        var priorState=stateProperty.GetValue(manager);var priorAscension=ascensionProperty.GetValue(manager);var priorLocal=LocalContext.NetId;
        var receipts=new List<object>();int nativeCount=0;
        bool performanceOnly=Environment.GetEnvironmentVariable("RT2_CRYSTAL_PAELS_WING_PERF_ONLY")=="1";
        try
        {
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_PAELS_WING_ALLOW_CURSE_RAW")=="1")
            {
                foreach(int subscriptions in new[]{0,2}) foreach(bool avoid in new[]{true,false})
                    CheckPaelsWingSmallOracle(Create("000000000000",1).Source,output,label:$"curse-{subscriptions}-avoid-{avoid}",
                        includeCurse:true,avoidCurse:avoid,curseSubscriptions:subscriptions);
                return 0;
            }
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_PAELS_WING_GIFT_SCOPE")=="1")
            {
                var scoped=Create("000000000000",0).Source;
                CheckWingModeledGiftScope(scoped);
                if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_PAELS_WING_GIFT_SCOPE_RAW")=="1")
                {
                    foreach(int scopeCounter in new[]{0,1})
                    {
                        var raw=scopeCounter==0?scoped:Create("000000000000",scopeCounter).Source;
                        raw=raw with {Context=raw.Context with {Crystal=raw.Context.Crystal! with {
                            UnmodeledSources=raw.Context.Crystal!.UnmodeledSources.Add(new("CARD","CRYSTAL_UNRELATED_CATALOG_CARD"))}}};
                        CheckPaelsWingSmallOracle(raw,output,label:"unrelated-catalog-"+scopeCounter);
                        if(scopeCounter==1) CheckPaelsWingSmallOracle(raw,output,includeBoardRelic:false,
                            label:"unrelated-catalog-two-gifts",includeThirdCard:true);
                    }
                }
                return 0;
            }
            if(performanceOnly)
            {
                foreach(string seed in new[]{"000000000000","9TW7Y8N6663H"})
                {
                    var f=Create(seed,1);var path=CardPath(f.Source);var root=PredictorCrystalExplorer.Replay(f.Source,path);
                    var slots=root.Rewards.Select((r,i)=>(r,i)).Where(x=>x.r.Kind==PredictorRewardKind.Card).Select(x=>x.i).ToArray();
                    var proof=ManualWingPlan(f.Source,path,slots.Select((slot,index)=>(slot,index==0)).ToArray());
                    Native(f,proof.Plan,proof.Run);
                    var session=Session(f.Source);session.AddWitnesses([new(path,root.Rewards)]);
                    ImmutableArray<CrystalRewardOption> goals=[new(PredictorRewardKind.Relic,proof.Plan.SacrificeRelics.Single())];
                    Check(session.ProjectKnown(goals).SelectedPlan!=null,"Narrow one-gift profile preserves the original native-confirmed warm witness");
                    ProductionSearchReplay.Write(Path.Combine(output,$"source-{seed}.json"),f.Source);
                    ProductionSearchReplay.Write(Path.Combine(output,$"native-witness-{seed}.json"),proof.Plan);
                    receipts.Add(Measure(session,f.Source,path,goals,"one-gift",1));
                    if(seed=="000000000000" && Environment.GetEnvironmentVariable("RT2_CRYSTAL_PAELS_WING_DIRECT")=="1")
                        receipts.Add(Direct(f,goals[0],new(PredictorRewardKind.Card,new("CARD","ANGER"),0)));
                }
                if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_PAELS_WING_QUOTA_RAW")=="1")
                {
                    CheckWingQuotaPruning(Create("000000000000",1).Source);
                    CheckWingUpgradeScopes(Create("9TW7Y8N6663H",1).Source,output);
                }
                ProductionSearchReplay.Write(Path.Combine(output,"paels-wing-native.json"),receipts);
                Console.WriteLine($"CRYSTAL_PAELS_WING_PERF_ONLY_PASS nativeCases={nativeCount};workloads=2;counter=1;warmWitness=true;wallBudgetSeconds=10;fullNativeAndOracleSuite=false;cutoffIsPending=true");
                return 0;
            }
            foreach(string seed in new[]{"000000000000","9TW7Y8N6663H","AAJ82GDRLS09"})
            foreach(int counter in new[]{0,1})
            {
                var f=Create(seed,counter);
                string frozen=JsonSerializer.Serialize(f.Source,ProductionSearchReplay.JsonOptions);
                string liveFingerprint=CrystalSphereLiveCapture.SceneFingerprint(f.Native,f.Player,f.Event,f.Game);
                var path=CardPath(f.Source);var root=PredictorCrystalExplorer.Replay(f.Source,path);
                var slots=root.Rewards.Select((r,i)=>(r,i)).Where(x=>x.r.Kind==PredictorRewardKind.Card).Select(x=>x.i).ToArray();
                Check(slots.Length==3,"Wing native fixture reveals three card reward groups");
                var order=slots.Take(2-counter).Select(i=>(i,true)).Concat(slots.Skip(2-counter).Select(i=>(i,false))).ToArray();
                var proof=ManualWingPlan(f.Source,path,order);
                Native(f,proof.Plan,proof.Run);
                var goals=WingObtainedOptions(root,proof.Run,CrystalRewardOption.UsesEnchantments(f.Source.State)).Where(o=>o.Kind==PredictorRewardKind.Relic ||
                    proof.Plan.Takes.Any(t=>t.Card==o.Key && t.UpgradeLevel==o.UpgradeLevel)).ToImmutableArray();
                Check(goals.Any(g=>g.Kind==PredictorRewardKind.Relic),"Native Wing case obtains an actual relic");
                var verified=PredictorCrystalExplorer.VerifySelection(f.Source,new(path,root.Rewards),goals);
                Check(verified!=null,"Production selection preserves native-confirmed Wing/card combination");
                var session=Session(f.Source);
                session.AddWitnesses([new(path,root.Rewards)]);
                var known=session.ProjectKnown(goals);
                Check(known.SelectedPlan!=null,"Wing witness enters existing compatible-candidate projection");
                Check(known.SacrificeRelics.IsSupersetOf(proof.Plan.SacrificeRelics),"Only replay-verified sacrifice provenance feeds the badge projection");
                if(seed=="000000000000" && counter==0) CheckWingBadge(f,session.ProjectKnown([]));
                receipts.Add(Measure(session,f.Source,path,goals,"relic-and-cards",counter));
                if(counter==1 && seed!="AAJ82GDRLS09")
                {
                    var giftSession=Session(f.Source);giftSession.AddWitnesses([new(path,root.Rewards)]);
                    ImmutableArray<CrystalRewardOption> giftOnly=[new(PredictorRewardKind.Relic,proof.Plan.SacrificeRelics.Single())];
                    Check(giftSession.ProjectKnown(giftOnly).SelectedPlan!=null,"Gift-only performance query starts with a native-confirmed legal witness");
                    receipts.Add(Measure(giftSession,f.Source,path,giftOnly,"one-gift",counter));
                }
                Check(frozen==JsonSerializer.Serialize(f.Source,ProductionSearchReplay.JsonOptions),"Wing search leaves frozen snapshot unchanged");
                Check(liveFingerprint==CrystalSphereLiveCapture.SceneFingerprint(f.Native,f.Player,f.Event,f.Game),"Wing prediction/search never changes the native source RunState");
                Console.WriteLine($"CRYSTAL_WING_NATIVE seed={seed};counter={counter};gifts={proof.Plan.SacrificeRelics.Count};takes={proof.Plan.Takes.Length}");
            }

            // Three sacrifices starting at 1/2 produce two distinct gift targets.
            var multiple=Create("000000000000",1);var multiplePath=CardPath(multiple.Source);
            var multipleRoot=PredictorCrystalExplorer.Replay(multiple.Source,multiplePath);
            var multipleSlots=multipleRoot.Rewards.Select((r,i)=>(r,i)).Where(x=>x.r.Kind==PredictorRewardKind.Card).Select(x=>x.i).ToArray();
            var multi=ManualWingPlan(multiple.Source,multiplePath,multipleSlots.Select(i=>(i,true)).ToArray());
            Native(multiple,multi.Plan,multi.Run);
            var multiGoals=multi.Plan.SacrificeRelics.Select(k=>new CrystalRewardOption(PredictorRewardKind.Relic,k)).ToImmutableArray();
            Check(multiGoals.Length==2 && PredictorCrystalExplorer.VerifySelection(multiple.Source,new(multiplePath,multipleRoot.Rewards),multiGoals)!=null,
                "Two specified sacrifice relics coexist despite the board's single-relic slot");

            // Reveal a board relic before sacrifice: both sources consume the
            // same personal/shared bags, while rarity uses different RNGs.
            var shared=Create("000000000000",1);
            var sharedPath=RequiredItemPath(shared.Source,i=>i.Kind is "RELIC" or "CARD_COMMON",300_000);
            var sharedRoot=PredictorCrystalExplorer.Replay(shared.Source,sharedPath);
            var sharedSlot=sharedRoot.Rewards.FindIndex(r=>r.Kind==PredictorRewardKind.Card);
            var sharedProof=ManualWingPlan(shared.Source,sharedPath,[(sharedSlot,true)]);
            Native(shared,sharedProof.Plan,sharedProof.Run);
            var boardRelic=sharedRoot.Rewards.Single(r=>r.Kind==PredictorRewardKind.Relic).Key;
            Check(!sharedProof.Plan.SacrificeRelics.Contains(boardRelic),"Generated board relic is removed before Wing pulls the next relic");
            var sharedGoals=sharedProof.Plan.SacrificeRelics.Select(k=>new CrystalRewardOption(PredictorRewardKind.Relic,k))
                .Append(new(PredictorRewardKind.Relic,boardRelic)).ToImmutableArray();
            Check(PredictorCrystalExplorer.VerifySelection(shared.Source,new(sharedPath,sharedRoot.Rewards),sharedGoals)!=null,
                "Board reward and sacrifice gift are jointly available concrete relic identities");

            // Actual late-offer and Add-to-Deck Egg hooks demand an ordered
            // trace. Taking the first unupgraded attack after the Egg is wrong.
            var egg=Create("000000000000",1,bag:"egg");var eggPath=CardPath(egg.Source);
            var eggRoot=PredictorCrystalExplorer.Replay(egg.Source,eggPath);
            var attacks=eggRoot.Rewards.Select((r,i)=>(r,i,Card:r.Cards.FirstOrDefault(c=>c.Type==RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack && c.UpgradeLevel==0)))
                .Where(x=>x.r.Kind==PredictorRewardKind.Card && x.Card!=null).Take(2).ToArray();
            Check(attacks.Length==2,"Egg fixture offers two unupgraded attack groups");
            int sacrifice=eggRoot.Rewards.FindIndex(r=>r.Kind==PredictorRewardKind.Card && !attacks.Any(a=>a.r.Id==r.Id));
            var eggProof=ManualWingPlan(egg.Source,eggPath,[(attacks[0].i,false),(sacrifice,true),(attacks[1].i,false)],
                new Dictionary<int,ModelKey> {{attacks[0].i,attacks[0].Card!.Key},{attacks[1].i,attacks[1].Card!.Key}});
            Native(egg,eggProof.Plan,eggProof.Run);
            Check(eggProof.Plan.SacrificeRelics.Contains(new("RELIC","MOLTEN_EGG")) && eggProof.Plan.Takes[0].UpgradeLevel==0 && eggProof.Plan.Takes[1].UpgradeLevel==1,
                "Native Egg acquisition preserves take-before-gift and upgraded take-after-gift");
            var eggGoals=eggProof.Plan.Takes.Select(t=>CrystalRewardOption.ForCard(eggRoot.Rewards[t.RewardIndex].Cards.Single(c=>c.Key==t.Card),true))
                .Append(new(PredictorRewardKind.Relic,new("RELIC","MOLTEN_EGG"))).ToImmutableArray();
            Check(PredictorCrystalExplorer.VerifySelection(egg.Source,new(eggPath,eggRoot.Rewards),eggGoals)!=null,
                "Production solver preserves original offer targets while the newly acquired Egg keeps its real native lifecycle");
            Check(PredictorCrystalExplorer.VerifySelection(egg.Source,new(eggPath,eggRoot.Rewards),
                [new(PredictorRewardKind.Relic,new("RELIC","MOLTEN_EGG")),eggGoals[1] with {UpgradeLevel=1}])==null,
                "Newly gifted Egg does not derive a +1 card target from this row's original level-zero offer");

            var ownedEgg=Create("000000000000",1,"MOLTEN_EGG");var ownedEggPath=CardPath(ownedEgg.Source);
            var ownedEggRoot=PredictorCrystalExplorer.Replay(ownedEgg.Source,ownedEggPath);
            Check(ownedEggRoot.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).SelectMany(r=>r.Cards)
                .Where(c=>c.Type==RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack).All(c=>c.UpgradeLevel>0),"Egg already owned at capture contributes upgraded original reward offers");
            var ownedSlots=ownedEggRoot.Rewards.Select((r,i)=>(r,i)).Where(x=>x.r.Kind==PredictorRewardKind.Card).Select(x=>x.i).ToArray();
            var ownedProof=ManualWingPlan(ownedEgg.Source,ownedEggPath,ownedSlots.Select((slot,index)=>(slot,index==0)).ToArray());
            Native(ownedEgg,ownedProof.Plan,ownedProof.Run);

            var enchanted=Create("000000000000",1,"FRESNEL_LENS+WING_CHARM+GLITTER");var enchantedPath=CardPath(enchanted.Source);
            var enchantedRoot=PredictorCrystalExplorer.Replay(enchanted.Source,enchantedPath);
            var enchantedSlots=enchantedRoot.Rewards.Select((r,i)=>(r,i)).Where(x=>x.r.Kind==PredictorRewardKind.Card).Select(x=>x.i).ToArray();
            var enchantedProof=ManualWingPlan(enchanted.Source,enchantedPath,[(enchantedSlots[0],true),(enchantedSlots[1],false),(enchantedSlots[2],false)]);
            Native(enchanted,enchantedProof.Plan,enchantedProof.Run);
            Check(enchantedProof.Plan.Takes.Any(t=>t.Enchantment?.Key!=null),"Wing native control actually observes exact enchanted cards");
            var enchantedGoals=enchantedProof.Plan.Takes.Select(t=>CrystalRewardOption.ForCard(
                enchantedRoot.Rewards[t.RewardIndex].Cards.Single(c=>c.Key==t.Card),true))
                .Concat(enchantedProof.Plan.SacrificeRelics.Select(k=>new CrystalRewardOption(PredictorRewardKind.Relic,k))).ToImmutableArray();
            Check(enchantedGoals.Any(g=>g.Enchantment?.Key!=null),"Exact language regression includes a named original-offer enchantment target");
            var enchantedTable=new CrystalReachabilityTable(enchanted.Source,includeRerolls:false,includeEnchantments:true);
            var enchantedLanguage=PredictorCrystalGoalLanguage.Solve(enchanted.Source,enchantedTable,enchantedGoals,true,TimeSpan.FromSeconds(10),rootCardSlots:true);
            Check(enchantedLanguage.Verdict==CrystalOffsetVerdict.Possible && enchantedLanguage.Plan!=null,
                "Fresh direct goal language preserves the native-confirmed exact-enchantment and sacrifice-gift combination");
            var enchantedWitness=enchantedLanguage.Plan!;var enchantedReplay=PredictorCrystalExplorer.Replay(enchanted.Source,enchantedWitness.Steps);
            Check(PredictorCrystalExplorer.VerifySelection(enchanted.Source,new(enchantedWitness.Steps,enchantedReplay.Rewards) {EnchantmentTargets=true},enchantedGoals)!=null,
                "Direct exact-enchantment witness verifies through original-offer typed settlement");
            ProductionSearchReplay.Write(Path.Combine(output,"wing-exact-enchantment-language.json"),new {enchantedGoals,result=enchantedLanguage,
                scope="Existing native-confirmed scenario; fresh direct goal query without a warm witness session; exact original offers plus known sacrifice gift"});
            Console.WriteLine($"CRYSTAL_WING_EXACT_ENCHANTMENT_LANGUAGE_PASS seconds={enchantedLanguage.Seconds:F3};geometryNodes={enchantedLanguage.GeometryNodes};verifiedWords={enchantedLanguage.VerifiedWords};freshQuery=true");

            var control=Create("000000000000",null);var controlPath=CardPath(control.Source);
            var controlRoot=PredictorCrystalExplorer.Replay(control.Source,controlPath);
            var controlOrder=controlRoot.Rewards.Select((r,i)=>(r,i)).Where(x=>x.r.Kind==PredictorRewardKind.Card).Select(x=>(x.i,false)).ToArray();
            var noWing=ManualWingPlan(control.Source,controlPath,controlOrder);
            var legacy=noWing.Plan with { RewardActions=[] };
            Native(control,legacy,noWing.Run);
            Check(PredictorCrystalRewardGuide.BuildRerollGuide(control.Source,legacy).Actions.IsEmpty,"No-Wing collection-free guidance retains its previous contract");

            var choice=Create("000000000000",1);var choicePath=CardPath(choice.Source);
            var choiceRoot=PredictorCrystalExplorer.Replay(choice.Source,choicePath);
            int choiceSlot=choiceRoot.Rewards.FindIndex(r=>r.Kind==PredictorRewardKind.Card);
            var sacrificed=ManualWingPlan(choice.Source,choicePath,[(choiceSlot,true)]);
            var taken=ManualWingPlan(choice.Source,choicePath,[(choiceSlot,false)]);
            Native(choice,sacrificed.Plan,sacrificed.Run,PredictorCrystalRewardGuide.Build(choice.Source,taken.Plan).Frames[1]);
            Native(choice,taken.Plan,taken.Run,PredictorCrystalRewardGuide.Build(choice.Source,sacrificed.Plan).Frames[1]);

            foreach(int? counter in new int?[]{null,0,1})
                CheckPaelsWingSmallOracle(Create("000000000000",counter).Source,output);
            CheckWingCustomGift(Create("000000000000",1).Source);
            ProductionSearchReplay.Write(Path.Combine(output,"paels-wing-native.json"),receipts);
            Console.WriteLine($"CRYSTAL_PAELS_WING_PASS nativeCases={nativeCount};counter0=true;counter1=true;sharedBag=true;multipleRelics=true;eggOrder=true;exactEnchantments=true;noWingControl=true;rawSmallBoardOracle=true;timeoutIsPending=true");
            return 0;
        }
        finally {stateProperty.SetValue(manager,priorState);ascensionProperty.SetValue(manager,priorAscension);LocalContext.NetId=priorLocal;}

        PredictorCrystalReachability Session(PredictorCrystalSnapshot source)=>new(source,true,potionScenarioBound:true,
            workers:Math.Clamp(Environment.ProcessorCount-1,1,4),targetDirected:true,goalLanguageProof:true,includeRerolls:false,includeEnchantments:true);

        void CheckWingQuotaPruning(PredictorCrystalSnapshot source)
        {
            var girya=new ModelKey("RELIC","GIRYA");var egg=new ModelKey("RELIC","MOLTEN_EGG");
            var bag=new PredictorRelicBag(false,[new(girya,"Rare"),new(egg,"Rare")],[new("Rare",[girya,egg])],[]);
            // Act 1 has zero natural reward-upgrade chance, so every +1 in
            // these controlled outcomes must come from the acquired Egg.
            var controlled=source with {State=source.State with {PersonalBag=bag,SharedBag=bag,Position=source.State.Position with {Act=1}}};
            string folder=Path.Combine(output,"quota-raw");Directory.CreateDirectory(folder);
            var ordinary=CheckPaelsWingSmallOracle(controlled,folder,includeBoardRelic:false,label:"no-board-non-upgrader");
            var onlyZero=ordinary.SelectMany(row=>row).Where(c=>c.Kind==PredictorRewardKind.Card && c.UpgradeLevel==0).Distinct()
                .FirstOrDefault(c=>!ordinary.Any(row=>row.Contains(c with {UpgradeLevel=1})));
            var missingPlusOne=(onlyZero??throw new InvalidOperationException("Controlled non-upgrader oracle has no level-zero card")) with {UpgradeLevel=1};
            Check(ordinary.All(row=>!row.Contains(missingPlusOne)),
                "Controlled ordinary non-upgrader gift cannot create a missing +1 card variant");
            var withBoard=CheckPaelsWingSmallOracle(controlled,folder,label:"board-girya-gift-egg");
            Check(withBoard.Any(row=>row.Any(c=>c.Kind==PredictorRewardKind.Relic && c.Key==girya) &&
                row.Any(c=>c.Kind==PredictorRewardKind.Relic && c.Key==egg) && row.Any(c=>c.Kind==PredictorRewardKind.Card && c.UpgradeLevel==0)),
                "Board-origin relic and gifted Egg remain compatible with an original level-zero card target");
            Check(withBoard.All(row=>!row.Any(c=>c.Kind==PredictorRewardKind.Card && c.UpgradeLevel==1)),"New Egg adds no derived upgrade goal to an Act1 original-offer row");
            Console.WriteLine("CRYSTAL_WING_QUOTA_RAW_PASS nonUpgraderMissingPlusOne=false;boardRelicThenGiftEggOriginalCard=true;newEggDerivedPlusOne=false;directLanguageMatchesCompleteRaw=true");
        }

        object Direct(WingFixture f,CrystalRewardOption selected,CrystalRewardOption target)
        {
            ImmutableArray<CrystalRewardOption> goals=[selected,target];
            var table=new CrystalReachabilityTable(f.Source,includeRerolls:false,includeEnchantments:true);
            var timer=Stopwatch.StartNew();
            var result=PredictorCrystalGoalLanguage.Solve(f.Source,table,goals,true,TimeSpan.FromSeconds(10),rootCardSlots:true,profileCompilation:true);
            double solveWallMs=timer.Elapsed.TotalMilliseconds;
            if(result.Plan is { } plan)
            {
                var replay=PredictorCrystalExplorer.Replay(f.Source,plan.Steps);
                Check(PredictorCrystalExplorer.VerifySelection(f.Source,new(plan.Steps,replay.Rewards),goals)!=null,"Fresh direct Wing query returns a complete typed target witness");
                var expected=ManualWingPlan(f.Source,plan.Steps,plan.RewardActions.Select(a=>(a.RewardIndex,a.Kind==CrystalRewardActionKind.Sacrifice)).ToArray(),
                    plan.RewardActions.Where(a=>a.Kind==CrystalRewardActionKind.TakeCard).ToDictionary(a=>a.RewardIndex,a=>a.Card));
                Native(f,plan,expected.Run);
            }
            ProductionSearchReplay.Write(Path.Combine(output,"direct-one-gift-anger0.json"),new {seed=f.Source.Context.Seed,goals,
                warmWitness=false,budgetSeconds=10,solveWallMs,totalWithValidationMs=timer.Elapsed.TotalMilliseconds,result});
            Console.WriteLine($"CRYSTAL_WING_DIRECT seed={f.Source.Context.Seed};target={target.CacheKey};warmWitness=false;verdict={result.Verdict};seconds={result.Seconds:F3};compileSeconds={result.CompileSeconds:F3};geometryNodes={result.GeometryNodes};verifiedWords={result.VerifiedWords};cutoffIsPending=true");
            return new {seed=f.Source.Context.Seed,workload="direct-one-gift-anger0",warmNativeWitness=false,goals,result.Verdict,result.Seconds,
                result.CompileSeconds,result.RewardStates,result.GeometryNodes,result.VerifiedWords,result.NegativeKind};
        }

        object Measure(PredictorCrystalReachability session,PredictorCrystalSnapshot source,ImmutableArray<PredictorCrystalStep> path,
            ImmutableArray<CrystalRewardOption> goals,string workload,int counter)
        {
            const double wallBudgetSeconds=10;
            var watch=Stopwatch.StartNew();CrystalReachabilityUpdate update;double initialMs,searchMs,stopMs;
            var observations=new ConcurrentQueue<WingLanguageObservation>();
            bool profileCompilation=Environment.GetEnvironmentVariable("RT2_CRYSTAL_PAELS_WING_PROFILE")=="1";
            session.ProfileLanguageCompilation=profileCompilation;
            if(performanceOnly || profileCompilation)
                session.LanguageResultObserved=(selection,target,result)=>observations.Enqueue(new(selection,target,result,watch.Elapsed.TotalMilliseconds));
            double candidateSliceMs=0,goldSliceMs=0;int candidateSlices=0,goldSlices=0;
            CrystalOptionProjection gold;
            using(var cancel=new CancellationTokenSource())
            {
                try
                {
                    CrystalReachabilityUpdate Candidates(TimeSpan budget)
                    {
                        var timer=Stopwatch.StartNew();var result=session.Advance(goals,budget,cancel.Token);
                        candidateSliceMs+=timer.Elapsed.TotalMilliseconds;candidateSlices++;return result;
                    }
                    update=Candidates(TimeSpan.FromMilliseconds(200));initialMs=watch.Elapsed.TotalMilliseconds;gold=update.Projection;
                    while(watch.Elapsed.TotalSeconds<wallBudgetSeconds && (!update.Complete || !gold.GoldComplete))
                    {
                        TimeSpan Slice()=>TimeSpan.FromMilliseconds(Math.Max(0,Math.Min(150,wallBudgetSeconds*1000-watch.Elapsed.TotalMilliseconds)));
                        if(!update.Complete) update=Candidates(Slice());
                        if(!gold.GoldComplete && watch.Elapsed.TotalSeconds<wallBudgetSeconds)
                        {
                            var timer=Stopwatch.StartNew();gold=session.AdvanceGold(goals,Slice(),cancel.Token);
                            goldSliceMs+=timer.Elapsed.TotalMilliseconds;goldSlices++;
                        }
                    }
                    searchMs=watch.Elapsed.TotalMilliseconds;
                }
                finally
                {
                    cancel.Cancel();
                    // Match the actual production language worker lifetime;
                    // leaving the bounded survey must not leave background work.
                    var queries=(System.Collections.IDictionary)typeof(PredictorCrystalReachability).GetField("_languageQueries",flags)!.GetValue(session)!;
                    var workers=queries.Values.Cast<object>().Select(q=>q.GetType().GetField("Worker",flags)?.GetValue(q) as Task)
                        .OfType<Task>().Distinct().ToArray();
                    var stop=Stopwatch.StartNew();
                    foreach(var worker in workers)
                    {
                        Check(Task.WaitAny([worker],TimeSpan.FromSeconds(5))==0,"Bounded Wing survey worker exits after cancellation");
                        try {worker.GetAwaiter().GetResult();} catch(OperationCanceledException) { }
                    }
                    stopMs=stop.Elapsed.TotalMilliseconds;
                }
            }
            update=session.Advance(goals,TimeSpan.Zero);gold=update.Projection;
            var table=(CrystalReachabilityTable)typeof(PredictorCrystalReachability).GetField("_table",flags)!.GetValue(session)!;
            var candidates=table.Candidates.Where(c=>!goals.Contains(c)).ToArray();
            var pending=candidates.Where(c=>!gold.Available.ContainsKey(c) && !update.ExcludedCandidates.Contains(c)).ToArray();
            Check(gold.SelectedPlan!=null,"Bounded performance survey retains its native-confirmed selection");
            Check(!update.Complete || pending.Length==0,"Candidate-complete Wing receipt has no pending actual candidate");
            var solves=observations.ToArray();
            if(performanceOnly || profileCompilation)
                ProductionSearchReplay.Write(Path.Combine(output,$"language-profile-{source.Context.Seed}-{counter}-{workload}.json"),new {
                    seed=source.Context.Seed,counter,workload,goals,profileCompilation,candidateSlices,candidateSliceMs,goldSlices,goldSliceMs,
                    pending=pending.Select(c=>c.CacheKey).ToArray(),
                    completedSolves=solves.Length,solveSeconds=solves.Sum(o=>o.Result.Seconds),compileSeconds=solves.Sum(o=>o.Result.CompileSeconds),
                    rewardStates=solves.Sum(o=>(long)o.Result.RewardStates),geometryNodes=solves.Sum(o=>o.Result.GeometryNodes),
                    targets=solves.Select(o=>new {o.Selected,o.Target,targetKey=o.Target?.CacheKey,o.ObservedMs,o.Result.Verdict,
                        o.Result.Seconds,o.Result.CompileSeconds,o.Result.RewardStates,o.Result.Recipes,o.Result.GeometryNodes,o.Result.VerifiedWords,
                        o.Result.PaddingUnknown,o.Result.LanguageNodes,o.Result.LanguageRoots,o.Result.LanguagePruned,o.Result.NegativeKind,
                        o.Result.NegativeCacheHit,o.Result.RootCardSlotsRejected,o.Result.CompilationProfile}).ToArray()});
            Console.WriteLine($"CRYSTAL_WING_PERF seed={source.Context.Seed};counter={counter};workload={workload};initialMs={initialMs:F2};wallMs={searchMs:F2};stopMs={stopMs:F2};candidateComplete={update.Complete};goldComplete={gold.GoldComplete};positive={gold.Available.Count};negative={update.ExcludedCandidates.Length};pending={pending.Length};gold={gold.Gold};goldUpper={gold.GoldUpperBound}");
            return new {seed=source.Context.Seed,counter,workload,path,goals,native=true,warmNativeWitness=true,wallBudgetSeconds,
                initialBudgetMs=200,initialMs,searchMs,stopMs,candidateComplete=update.Complete,gold.GoldComplete,
                profileCompilation,candidateSlices,candidateSliceMs,goldSlices,goldSliceMs,completedSolves=solves.Length,
                positive=gold.Available.Count,negative=update.ExcludedCandidates.Length,pending=pending.Length,
                pendingKeys=pending.Select(c=>c.CacheKey).ToArray(),gold.Gold,gold.GoldUpperBound,update.Progress,
                note="Panel production options; generated board with native-confirmed seeded witness; bounded warm candidates/gold scheduling; cutoff remains pending, no cold UI latency or global-optimum claim"};
        }

        WingFixture Create(string seed,int? counter,string inventory="",string bag="natural")
        {
            var player=Player.CreateForNewRun<Ironclad>(UnlockState.all,1);player.Gold=200;
            var native=RunState.CreateForTest([player],seed:seed,ascensionLevel:ascension);native.CurrentActIndex=1;
            stateProperty.SetValue(manager,native);ascensionProperty.SetValue(manager,new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(ascension));LocalContext.NetId=player.NetId;
            native.AppendToMapPointHistory(MapPointType.Unknown,RoomType.Event,ModelDb.Event<CrystalSphere>().Id);
            if(counter is { } count)
            {
                var wing=(PaelsWing)ModelDb.Relic<PaelsWing>().ToMutable();wing.RewardsSacrificed=count;player.AddRelicInternal(wing,silent:true);
            }
            foreach(string relic in inventory.Split('+',StringSplitOptions.RemoveEmptyEntries))
                player.AddRelicInternal(ModelDb.GetById<RelicModel>(new ModelId("RELIC",relic)).ToMutable(),silent:true);
            Check(player.Relics.All(r=>r.Id.Entry!="DRIFTWOOD"),"Wing fixture never combines unsupported native alternatives");
            player.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
            native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState),native.Rng.UpFront);
            if(bag=="egg")
            {
                // Controlled valid rarity bucket; Common/Uncommon requests
                // fall through to Rare by the actual game bag rules.
                var deques=(Dictionary<RelicRarity,List<RelicModel>>)typeof(RelicGrabBag).GetField("_deques",flags)!.GetValue(player.RelicGrabBag)!;
                deques.Clear();deques.Add(RelicRarity.Rare,[ModelDb.Relic<MoltenEgg>(),ModelDb.Relic<Mango>()]);
            }
            var ev=(CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();ev.BeginEvent(player,null,false).GetAwaiter().GetResult();
            CardPileCmd.AddCurseToDeck<MegaCrit.Sts2.Core.Models.Cards.Debt>(player).GetAwaiter().GetResult();
            var game=new CrystalSphereMinigame(player,ev.Rng,6);
            var source=CrystalSphereLiveCapture.CaptureScene(native,player,ev,game,"0.111.0").Branches.Single().Snapshot;
            Check(source.State.Relics.Where(r=>r.Key.Entry=="PAELS_WING").All(r=>r.RewardsSacrificed==counter),"Live capture preserves the actual Wing counter");
            return new(player,native,ev,game,source,inventory,counter,bag);
        }

        void CheckWingBadge(WingFixture f,CrystalOptionProjection known)
        {
            // This native fixture is synchronous and the panel is deliberately
            // detached: its synthetic fingerprint is not a capturable live scene.
            // Pump the existing presentation consumer explicitly on the main
            // thread; rendered visibility/freshness belongs to the run-entry checks.
            var panel=new RolltheSpire2.Ui.Shell.CrystalSphereAssistantPanel();
            try
            {
                panel.Initialize(f.Native,RolltheSpire2.Bootstrap.ModRuntimeSnapshot.NotInitialized);
                var panelType=panel.GetType();
                panelType.GetField("_snapshot",flags)!.SetValue(panel,new CrystalSphereLiveSnapshot("wing-badge",f.Source.Context.Seed,[("Remaining",f.Source)]));
                var branch=(Godot.OptionButton)panelType.GetField("_branch",flags)!.GetValue(panel)!;branch.AddItem("Remaining");branch.Select(0);
                var key=known.SacrificeRelics.First();var option=new CrystalRewardOption(PredictorRewardKind.Relic,key);
                Check(known.Available.TryGetValue(option,out var plan),"Marked relic also has a verified selectable candidate");
                var projection=new CrystalOptionProjection(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty.Add(option,plan!),plan,known.Gold)
                    {SacrificeRelics=ImmutableHashSet.Create(key)};
                var projectionField=panelType.GetField("_projection",flags)!;var render=panelType.GetMethod("Render",flags)!;
                var applyPresentation=panelType.GetMethod("ApplyPresentationSlice",flags)!;
                void SettlePresentation()
                {
                    // Exactly one candidate and no selected targets are authored
                    // here. Preserve a finite bound if that fixture ever changes.
                    for(int slice=0;slice<8;slice++)
                    {
                        applyPresentation.Invoke(panel,null);
                        var pending=(Queue<CrystalRewardOption>)panelType.GetField("_optionPresentationQueue",flags)!.GetValue(panel)!;
                        if(pending.Count==0) return;
                    }
                    Check(false,"Wing badge requested presentation settles through the bounded consumer");
                }
                projectionField.SetValue(panel,projection);render.Invoke(panel,null);
                var tiles=(Dictionary<CrystalRewardOption,Godot.Control>)panelType.GetField("_optionTiles",flags)!.GetValue(panel)!;
                Check(tiles.Count==0,"Wing badge render request defers cold tile construction");
                SettlePresentation();
                var badge=tiles[option].GetNode<Godot.TextureRect>("CrystalSacrificeBadge");
                Check(badge.Visible && badge.Texture!=null && badge.Position==new Godot.Vector2(3,3) && badge.Size==new Godot.Vector2(24,24) &&
                    badge.MouseFilter==Godot.Control.MouseFilterEnum.Ignore && string.IsNullOrEmpty(badge.TooltipText),"Verified sacrifice relic has the small top-left Wing icon without an extra tooltip or input handler");
                projectionField.SetValue(panel,projection with {SacrificeRelics=ImmutableHashSet<ModelKey>.Empty});render.Invoke(panel,null);SettlePresentation();
                Check(!badge.Visible,"Ordinary relic availability does not imply sacrifice provenance");
                projectionField.SetValue(panel,projection with {Available=ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,SelectedPlan=null});
                ((HashSet<CrystalRewardOption>)panelType.GetField("_pendingOptions",flags)!.GetValue(panel)!).Add(option);render.Invoke(panel,null);SettlePresentation();
                Check(!badge.Visible,"Pending bag candidates never show a proof badge");
                Console.WriteLine("CRYSTAL_WING_BADGE_PASS nativeTexture=true;topLeft=true;proofOnly=true;pendingHidden=true;extraTooltip=false");
            }
            finally {panel.Free();}
        }

        void Native(WingFixture source,PredictorCrystalSolution plan,PredictorRun expected,CrystalRewardGuideFrame? rejectedFrame=null)
        {
            // Always recreate; predictor exploration cannot advance this live run.
            var f=Create(source.Source.Context.Seed,source.Counter,source.Inventory,source.Bag);
            string before=JsonSerializer.Serialize(f.Source,ProductionSearchReplay.JsonOptions);
            Check(before==JsonSerializer.Serialize(source.Source,ProductionSearchReplay.JsonOptions),"Wing native fixture reproduces every frozen input");
            plan=JsonSerializer.Deserialize<PredictorCrystalSolution>(JsonSerializer.Serialize(plan,ProductionSearchReplay.JsonOptions),ProductionSearchReplay.JsonOptions)!;
            foreach(var step in plan.Steps)
            {
                Check(f.Game.cells[step.X,step.Y].IsHidden,"Wing native click center is hidden");
                f.Game.SetTool(step.Tool==PredictorCrystalTool.Big?CrystalSphereMinigame.CrystalSphereToolType.Big:CrystalSphereMinigame.CrystalSphereToolType.Small);
                f.Game.CellClicked(f.Game.cells[step.X,step.Y]).GetAwaiter().GetResult();
            }
            var items=(List<CrystalSphereItem>)typeof(CrystalSphereMinigame).GetField("_revealed",flags)!.GetValue(f.Game)!;
            var set=new RewardsSet(f.Player).WithCustomRewards(items.Select(i=>i.ToReward(f.Player,f.Game.Rng)).OfType<Reward>().ToList());set.GenerateWithoutOffering().GetAwaiter().GetResult();
            var initial=PredictorCrystalExplorer.Replay(f.Source,plan.Steps);
            Check(CardSignature(set.Rewards.OfType<CardReward>())==CardSignature(initial.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card)),"Wing native initial complete card offers");
            Check(set.Rewards.OfType<RelicReward>().Select(r=>r.Relic!.Id.Entry).SequenceEqual(initial.Rewards.Where(r=>r.Kind==PredictorRewardKind.Relic).Select(r=>r.Key.Entry)),"Wing board relic generation uses the shared personal bag");
            var guide=PredictorCrystalRewardGuide.Build(f.Source,plan);var binding=CrystalSphereRewardObservation.Bind(guide.Frames[0],set.Rewards.OfType<CardReward>().Reverse());
            Check(binding!=null,"Wing guide binds native card groups independently of reward display order");
            bool Matches(int step)=>CrystalSphereRewardObservation.Matches(guide.Frames[step],binding!,
                CrystalSphereLiveCapture.RngState(PredictorStream.Rewards,f.Game.Rng),CrystalSphereLiveCapture.RngState(PredictorStream.Niche,f.Native.Rng.Niche),
                wing:CrystalSphereRewardObservation.CaptureWing(f.Player,false));
            Check(Matches(0),"Wing serialized initial guide observes native counters and three RNG streams");
            var service=new NetSingleplayerGameService();using var sync=new PlayerChoiceSynchronizer(service,f.Native);
            foreach(var reward in set.Rewards.OfType<CardReward>()) typeof(CardReward).GetField("_synchronizer",flags)!.SetValue(reward,sync);
            int stepIndex=0;
            foreach(var action in guide.Actions)
            {
                var op=new RewardOp(action.RewardIndex,action.Sacrifice?"Sacrifice":"Take",action.Card.Entry,action.OfferedUpgradeLevel??action.UpgradeLevel,action.Enchantment);
                var queue=new Queue<RewardOp>([op]);
                using(var selector=CardSelectCmd.UseSelector(new RewardScript(queue))) binding![action.RewardIndex].SelectUnsynchronized().GetAwaiter().GetResult();
                Check(queue.Count==0 && Matches(++stepIndex),"Wing guide frame matches actual Generate-alternative/OnSacrifice/selection lifecycle");
                if(stepIndex==1 && rejectedFrame!=null)
                    Check(!CrystalSphereRewardObservation.Matches(rejectedFrame,binding!,
                        CrystalSphereLiveCapture.RngState(PredictorStream.Rewards,f.Game.Rng),CrystalSphereLiveCapture.RngState(PredictorStream.Niche,f.Native.Rng.Niche),
                        wing:CrystalSphereRewardObservation.CaptureWing(f.Player,false)),"An actual native Take cannot satisfy Sacrifice guidance, and Sacrifice cannot satisfy Take guidance");
            }
            var captured=CrystalSphereLiveCapture.CaptureScene(f.Native,f.Player,f.Event,f.Game,"0.111.0").Branches.Single().Snapshot;
            Check(captured.EventRng==expected.ExportCrystal().EventRng,"Wing native Event RNG parity");
            Check(captured.State.Streams.Single(s=>s.Stream==PredictorStream.Rewards)==expected.Working.Streams.Single(s=>s.Stream==PredictorStream.Rewards),"Wing native player Rewards RNG parity");
            Check(captured.State.Streams.Single(s=>s.Stream==PredictorStream.Niche)==expected.Working.Streams.Single(s=>s.Stream==PredictorStream.Niche),"Wing native Niche RNG parity");
            Check(JsonSerializer.Serialize(captured.State.PersonalBag,ProductionSearchReplay.JsonOptions)==JsonSerializer.Serialize(expected.Working.PersonalBag,ProductionSearchReplay.JsonOptions) &&
                JsonSerializer.Serialize(captured.State.SharedBag,ProductionSearchReplay.JsonOptions)==JsonSerializer.Serialize(expected.Working.SharedBag,ProductionSearchReplay.JsonOptions),"Wing native personal/shared bag continuation parity");
            Check(captured.State.Relics.Select(r=>$"{r.Key.Entry}:{r.RewardsSacrificed}").SequenceEqual(expected.Working.Relics.Select(r=>$"{r.Key.Entry}:{r.RewardsSacrificed}")),"Wing native actually obtained relics and cumulative counter parity");
            Check(captured.State.Deck.Select(captured.State.Card).Select(EnchantedIdentity).Order().SequenceEqual(expected.Working.Deck.Select(expected.Working.Card).Select(EnchantedIdentity).Order()),"Wing native complete Deck identity/upgrade/enchantment parity");
            Check(CardSignature(set.Rewards.OfType<CardReward>().Where(r=>!r.SuccessfullySelected))==CardSignature(expected.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card && r.Status==PredictorRewardStatus.Open)),"Sacrificed groups are completed and cannot supply a card target");
            nativeCount++;
        }
    }

    private static (PredictorCrystalSolution Plan,PredictorRun Run) ManualWingPlan(PredictorCrystalSnapshot source,
        ImmutableArray<PredictorCrystalStep> path,(int Slot,bool Sacrifice)[] order,IReadOnlyDictionary<int,ModelKey>? chosen=null)
    {
        var run=PredictorCrystalExplorer.Replay(source,path);var actions=ImmutableArray.CreateBuilder<CrystalRewardAction>();var takes=ImmutableArray.CreateBuilder<PredictorCrystalTake>();
        var gifts=ImmutableHashSet.CreateBuilder<ModelKey>();
        foreach(var op in order)
        {
            var reward=run.Rewards[op.Slot];Check(run.Submit(run.Request!,new EnterCardSelection(reward.Id))==PredictorInputResult.Accepted,"Wing explicit oracle enters open reward");
            if(op.Sacrifice)
            {
                var before=run.Working.Relics.Select(r=>r.Id).ToHashSet();var wing=run.Working.Relics.Single(r=>r.Key.Entry=="PAELS_WING");
                Check(run.Submit(run.Request!,new SacrificeCardReward(reward.Id,wing.Id))==PredictorInputResult.Accepted,"Wing explicit oracle sacrifices an entire group");
                var gift=run.Working.Relics.SingleOrDefault(r=>!before.Contains(r.Id))?.Key??default;
                if(gift.IsValid) gifts.Add(gift);actions.Add(new(op.Slot,CrystalRewardActionKind.Sacrifice,Gift:gift));
            }
            else
            {
                var card=chosen!=null && chosen.TryGetValue(op.Slot,out var key)?run.Rewards[op.Slot].Cards.First(c=>c.Key==key):run.Rewards[op.Slot].Cards[0];
                var exact=CrystalCardEnchantment.Of(card);var take=new PredictorCrystalTake(op.Slot,card.Key,card.UpgradeLevel,exact);
                Check(run.Submit(run.Request!,new TakeReward(reward.Id,card.Id))==PredictorInputResult.Accepted,"Wing explicit oracle takes one offered card");
                actions.Add(new(op.Slot,CrystalRewardActionKind.TakeCard,card.Key,card.UpgradeLevel,exact));takes.Add(take);
            }
        }
        return (new("Found",0,path,takes.ToImmutable()) {RewardActions=actions.ToImmutable(),SacrificeRelics=gifts.ToImmutable()},run);
    }

    private static ImmutableArray<CrystalRewardOption> WingObtainedOptions(PredictorRun entry,PredictorRun run,bool exact,
        IReadOnlyDictionary<long,PredictorCard>? cardEvidence=null)
        =>run.Working.Deck.Where(id=>!entry.Working.Deck.Contains(id)).Select(id=>cardEvidence?.GetValueOrDefault(id)??run.Working.Card(id)).Select(c=>CrystalRewardOption.ForCard(c,exact))
            .Concat(run.Working.Relics.Where(r=>!entry.Working.Relics.Any(prior=>prior.Id==r.Id)).Select(r=>new CrystalRewardOption(PredictorRewardKind.Relic,r.Key)))
            .Concat(run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Relic && r.Status==PredictorRewardStatus.Open).Select(r=>new CrystalRewardOption(PredictorRewardKind.Relic,r.Key)))
            .Distinct().ToImmutableArray();

    private static void CheckWingCustomGift(PredictorCrystalSnapshot source)
    {
        // An explicit captured external Rare identity, with no claim about its
        // unknown hooks. Only the existing Crystal base-property premise applies.
        var key=new ModelKey("RELIC","RT2_WING_CUSTOM_GIFT_PROBE");
        var bag=new PredictorRelicBag(false,[new(key,"Rare")],[new("Rare",[key])],[]);
        var crystal=source.Context.Crystal!;
        var marked=source with {Context=source.Context with {Crystal=crystal with {
            EligibleRelics=crystal.EligibleRelics.Add(key),UnmodeledSources=crystal.UnmodeledSources.Add(key)}},
            State=source.State with {PersonalBag=bag,SharedBag=bag}};
        var path=CardPath(marked);var entry=PredictorCrystalExplorer.Replay(marked,path);
        Check(PredictorCrystalWing.GiftAvailability(entry.Context,entry.Working,1).Contains(key),"Explicit unmodeled gift marker retains a permissive prefix upper set");
        int slot=entry.Rewards.FindIndex(r=>r.Kind==PredictorRewardKind.Card);
        var proof=ManualWingPlan(marked,path,[(slot,true)]);
        Check(proof.Plan.SacrificeRelics.Contains(key) && proof.Run.Working.Relics.Any(r=>r.Key==key) &&
            !proof.Run.Working.PersonalBag.Buckets.SelectMany(b=>b.Entries).Contains(key) &&
            !proof.Run.Working.SharedBag.Buckets.SelectMany(b=>b.Entries).Contains(key) &&
            proof.Run.Working.Relics.Single(r=>r.Key.Entry=="PAELS_WING").RewardsSacrificed==2,
            "Explicit captured unmodeled gift preserves base inventory, both bags, and sacrifice counter");
        var unmarked=marked with {Context=marked.Context with {Crystal=marked.Context.Crystal! with {UnmodeledSources=crystal.UnmodeledSources}}};
        bool failed=false;
        try {ManualWingPlan(unmarked,path,[(slot,true)]);}
        catch(NotImplementedException ex) when(ex.Message.Contains(key.Entry,StringComparison.Ordinal)) {failed=true;}
        Check(failed,"Unmarked unknown gift retains its explicit hook-implementation fault");
        Console.WriteLine("CRYSTAL_WING_CUSTOM_BOUNDARY_PASS markedBaseProperties=true;unmarkedUnknownFault=true;customHookParityClaim=false");
    }
}
