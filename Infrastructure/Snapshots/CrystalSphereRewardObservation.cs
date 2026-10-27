using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Entities.Players;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using System.Collections.Immutable;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal sealed record CrystalSphereWingRewardObservation(PredictorStreamState RewardsRng,
    ImmutableArray<int> WingCounters,ImmutableDictionary<ModelKey,int> Relics,ImmutableDictionary<string,int> Deck);

// Main-thread read only. Native display sorting is not the generation order;
// bind concrete reward objects by their complete initial offers, then retain
// those references across rerolls and removal of their UI buttons.
internal static class CrystalSphereRewardObservation
{
    private static string Signature(PredictorCard c)=>$"{c.Key.Category}.{c.Key.Entry}:{c.UpgradeLevel}:{c.Enchantment?.Entry}:{(c.Enchantment==null?null:(decimal?)c.EnchantmentAmount)}";
    private static string Signature(PredictorCard c,bool localSnapshot)=>localSnapshot?
        $"{c.Key.Category}.{c.Key.Entry}:{c.UpgradeLevel}":Signature(c);
    private static string Signature(CardModel c,bool localSnapshot)=>localSnapshot?
        $"{c.Id.Category}.{c.Id.Entry}:{c.CurrentUpgradeLevel}":
        $"{c.Id.Category}.{c.Id.Entry}:{c.CurrentUpgradeLevel}:{c.Enchantment?.Id.Entry}:{c.Enchantment?.Amount}";
    private static bool SameCards(IEnumerable<PredictorCard> expected,IEnumerable<CardModel> actual,bool localSnapshot)=>
        localSnapshot ? expected.Select(c=>(c.Key.Category,c.Key.Entry,c.UpgradeLevel))
            .SequenceEqual(actual.Select(c=>(c.Id.Category,c.Id.Entry,c.CurrentUpgradeLevel))) :
        expected.Select(Signature).SequenceEqual(actual.Select(c=>$"{c.Id.Category}.{c.Id.Entry}:{c.CurrentUpgradeLevel}:{c.Enchantment?.Id.Entry}:{c.Enchantment?.Amount}"));

    // Copy only the signatures owned by an ordered Wing continuation. This does
    // not generate rewards, invoke alternatives or advance a native stream.
    internal static CrystalSphereWingRewardObservation CaptureWing(Player player,bool localSnapshot)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        return new(CrystalSphereLiveCapture.RngState(PredictorStream.Rewards,player.PlayerRng.Rewards),
            player.Relics.OfType<PaelsWing>().Where(r=>!r.IsMelted).Select(r=>r.RewardsSacrificed).ToImmutableArray(),
            player.Relics.GroupBy(r=>new ModelKey(r.Id.Category,r.Id.Entry)).ToImmutableDictionary(g=>g.Key,g=>g.Count()),
            player.Deck.Cards.GroupBy(c=>Signature(c,localSnapshot)).ToImmutableDictionary(g=>g.Key,g=>g.Count()));
    }

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
        PredictorStreamState eventRng,PredictorStreamState niche,bool localSnapshot=false,bool observeNiche=true,
        CrystalSphereWingRewardObservation? wing=null)
    {
        if(frame.EventRng!=eventRng || !localSnapshot && observeNiche && frame.Niche!=niche) return false;
        if(frame.RewardsRng is { } expectedRewards)
        {
            if(wing==null || wing.RewardsRng!=expectedRewards || !frame.WingCounters.SequenceEqual(wing.WingCounters)) return false;
            // A completed reward alone cannot distinguish Sacrifice from Take.
            // Require the expected gift and inserted-card witnesses as well.
            foreach(var key in frame.Gifts.Distinct())
                if(wing.Relics.GetValueOrDefault(key)<frame.Relics.Count(r=>r.Key==key)) return false;
            foreach(var signature in frame.AcquiredCards.Select(c=>Signature(c,localSnapshot)).Distinct())
                if(wing.Deck.GetValueOrDefault(signature)<frame.Deck.Count(c=>Signature(c,localSnapshot)==signature)) return false;
        }
        foreach(var (index,actual) in binding)
        {
            var expected=frame.Rewards[index];
            if(actual.CanReroll!=expected.CanReroll || actual.SuccessfullySelected!=(expected.Status==PredictorRewardStatus.Taken)) return false;
            var take=frame.Taken.FirstOrDefault(t=>t.RewardIndex==index);
            var cards=expected.Cards.AsEnumerable();
            if(take!=null)
            {
                var removed=frame.TakenCardIds.TryGetValue(index,out long cardId)?expected.Cards.First(c=>c.Id==cardId):
                    expected.Cards.First(take.MatchesCard);
                cards=cards.Where(c=>c.Id!=removed.Id);
            }
            if(!SameCards(cards,actual.Cards,localSnapshot) &&
                // Native insertion hooks can clone an offered card (e.g. an
                // Egg upgrade); CardReward then retains the original offer.
                !(frame.RewardsRng!=null && take!=null && SameCards(expected.Cards,actual.Cards,localSnapshot))) return false;
        }
        return true;
    }
}
