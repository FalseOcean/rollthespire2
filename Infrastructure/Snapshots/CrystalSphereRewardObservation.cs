using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

// Main-thread read only. Native display sorting is not the generation order;
// bind concrete reward objects by their complete initial offers, then retain
// those references across rerolls and removal of their UI buttons.
internal static class CrystalSphereRewardObservation
{
    private static string Signature(PredictorCard c)=>$"{c.Key.Category}.{c.Key.Entry}:{c.UpgradeLevel}:{c.Enchantment?.Entry}:{(c.Enchantment==null?null:(decimal?)c.EnchantmentAmount)}";
    private static bool SameCards(IEnumerable<PredictorCard> expected,IEnumerable<CardModel> actual,bool localSnapshot)=>
        localSnapshot ? expected.Select(c=>(c.Key.Category,c.Key.Entry,c.UpgradeLevel))
            .SequenceEqual(actual.Select(c=>(c.Id.Category,c.Id.Entry,c.CurrentUpgradeLevel))) :
        expected.Select(Signature).SequenceEqual(actual.Select(c=>$"{c.Id.Category}.{c.Id.Entry}:{c.CurrentUpgradeLevel}:{c.Enchantment?.Id.Entry}:{c.Enchantment?.Amount}"));

    internal static Dictionary<int,CardReward>? Bind(CrystalRewardGuideFrame initial,IEnumerable<CardReward> rewards,bool localSnapshot=false)
    {
        var remaining=rewards.ToList();var result=new Dictionary<int,CardReward>();
        for(int i=0;i<initial.Rewards.Length;i++)
        {
            var expected=initial.Rewards[i];if(expected.Kind!=PredictorRewardKind.Card) continue;
            var match=remaining.FirstOrDefault(r=>!r.SuccessfullySelected && r.CanReroll==expected.CanReroll && SameCards(expected.Cards,r.Cards,localSnapshot));
            if(match==null) return null;
            result.Add(i,match);remaining.Remove(match);
        }
        return remaining.Count==0?result:null;
    }

    internal static bool Matches(CrystalRewardGuideFrame frame,IReadOnlyDictionary<int,CardReward> binding,
        PredictorStreamState eventRng,PredictorStreamState niche,bool localSnapshot=false,bool observeNiche=true)
    {
        if(frame.EventRng!=eventRng || !localSnapshot && observeNiche && frame.Niche!=niche) return false;
        foreach(var (index,actual) in binding)
        {
            var expected=frame.Rewards[index];
            if(actual.CanReroll!=expected.CanReroll || actual.SuccessfullySelected!=(expected.Status==PredictorRewardStatus.Taken)) return false;
            var take=frame.Taken.FirstOrDefault(t=>t.RewardIndex==index);
            var cards=expected.Cards.AsEnumerable();
            if(take!=null)
            {
                var removed=expected.Cards.First(take.MatchesCard);
                cards=cards.Where(c=>c.Id!=removed.Id);
            }
            if(!SameCards(cards,actual.Cards,localSnapshot)) return false;
        }
        return true;
    }
}
