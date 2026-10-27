using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static void CheckWingUpgradeScopes(PredictorCrystalSnapshot source,string output)
    {
        foreach(string name in new[]{"WHETSTONE","WAR_PAINT","MOLTEN_EGG"})
        {
            var gift=new ModelKey("RELIC",name);string rarity=name=="MOLTEN_EGG"?"Rare":"Common";
            var bag=new PredictorRelicBag(false,[new(gift,rarity)],[new(rarity,[gift])],[]);
            var controlled=source with {State=source.State with {PersonalBag=bag,SharedBag=bag,Position=source.State.Position with {Act=1}}};
            string folder=Path.Combine(output,"quota-raw");Directory.CreateDirectory(folder);
            int physicalUpgradeEdges=0,changedInsertions=0;
            var outcomes=CheckPaelsWingSmallOracle(controlled,folder,includeBoardRelic:false,label:name.ToLowerInvariant()+"-scope",
                observePhysicalUpgrades:(physical,inserted)=>{physicalUpgradeEdges=physical;changedInsertions=inserted;});
            Check(outcomes.Any(row=>row.Any(o=>o.Kind==PredictorRewardKind.Relic && o.Key==gift)),"Upgrade relic remains an obtainable identity: "+name);
            Check(outcomes.Any(row=>row.Any(o=>o.Kind==PredictorRewardKind.Card && o.UpgradeLevel==0) &&
                row.Any(o=>o.Kind==PredictorRewardKind.Relic && o.Key==gift)),"New upgrade relic preserves original level-zero card targets: "+name);
            Check(outcomes.All(row=>!row.Any(o=>o.Kind==PredictorRewardKind.Card && o.UpgradeLevel==1)),"New upgrade relic contributes no derived +1 target: "+name);
            Check(physicalUpgradeEdges+changedInsertions>0,"Controlled boundary fixture retains actual deck/insertion upgrades: "+name);
            ProductionSearchReplay.Write(Path.Combine(output,name.ToLowerInvariant()+"-scope.json"),new {name,seed=source.Context.Seed,
                nicheState=controlled.State.Streams.Single(s=>s.Stream==PredictorStream.Niche),nicheAdvance=0,physicalUpgradeEdges,changedInsertions,
                scope="Act1/no-natural-upgrade original row; raw Take group/position binds original offer independently of production goal helper; real deck/Niche/insertion effects retained; per-candidate language comparisons recorded separately"});
            Console.WriteLine($"CRYSTAL_WING_UPGRADE_SCOPE_PASS gift={name};identityAvailable=true;originalLevelZero=true;derivedPlusOne=false;physicalUpgradeEdges={physicalUpgradeEdges};changedInsertions={changedInsertions};nicheAdvance=0");
        }
    }
}
