using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record CrystalRewardGuideAction(int RewardIndex,bool Reroll,ModelKey Card=default,int? UpgradeLevel=null,
    CrystalCardEnchantment? Enchantment=null);
internal sealed record CrystalRewardGuideFrame(ImmutableArray<PredictorReward> Rewards,
    ImmutableArray<PredictorCrystalTake> Taken,PredictorStreamState EventRng,PredictorStreamState Niche);
internal sealed record CrystalRewardGuide(ImmutableArray<CrystalRewardGuideAction> Actions,
    ImmutableArray<CrystalRewardGuideFrame> Frames);

internal static class PredictorCrystalRewardGuide
{
    // Owner policy: guide only the ordered rerolls, leaving every card reward
    // available for manual collection. Still validate the complete target witness.
    internal static CrystalRewardGuide BuildRerollGuide(PredictorCrystalSnapshot source,PredictorCrystalSolution solution)
    {
        var witness=Build(source,solution,immediateTakes:false);
        int count=solution.RerollRewardIndices.Length;
        return new(witness.Actions.Take(count).ToImmutableArray(),witness.Frames.Take(count+1).ToImmutableArray());
    }

    // Reconstruct the entire suffix using stable reward ordinals. Also validates
    // deserialized solutions: no dependency on cached request IDs or live objects.
    internal static CrystalRewardGuide Build(PredictorCrystalSnapshot source,PredictorCrystalSolution solution,bool immediateTakes=true)
    {
        var run=PredictorCrystalExplorer.Replay(source,solution.Steps);
        if(run.Phase!=PredictorPhase.Rewards) throw new InvalidOperationException("CrystalGuideNoRewards");
        var actions=ImmutableArray.CreateBuilder<CrystalRewardGuideAction>();
        var frames=ImmutableArray.CreateBuilder<CrystalRewardGuideFrame>();
        ImmutableArray<PredictorCrystalTake> taken=[];
        CrystalRewardGuideFrame Frame()=>new(run.Rewards,taken,run.ExportCrystal().EventRng,
            run.Working.Streams.Single(s=>s.Stream==PredictorStream.Niche));
        void Submit(PredictorInput input)
        {
            if(run.Submit(run.Request!,input)!=PredictorInputResult.Accepted)
                throw new InvalidOperationException("CrystalGuideSuffixRejected");
        }
        PredictorReward Reward(int index)=>index>=0 && index<run.Rewards.Length && run.Rewards[index].Kind==PredictorRewardKind.Card
            ?run.Rewards[index]:throw new InvalidOperationException("CrystalGuideRewardIndex");
        void Enter(PredictorReward reward)
        {
            if(run.CrystalSelectedReward==reward.Id) return;
            if(run.CrystalSelectedReward!=null) Submit(new ExitCardSelection());
            Submit(new EnterCardSelection(reward.Id));
        }
        void Take(PredictorCrystalTake take)
        {
            var reward=Reward(take.RewardIndex);
            var card=reward.Cards.FirstOrDefault(take.MatchesCard)
                ??throw new InvalidOperationException("CrystalGuideCardMissing");
            var before=run.Working.Deck.ToHashSet();
            Enter(reward);Submit(new TakeReward(reward.Id,card.Id));
            if(!run.Working.Deck.Where(id=>!before.Contains(id)).Select(run.Working.Card)
                .Any(take.MatchesCard))
                throw new InvalidOperationException("CrystalGuideTakeMismatch");
            taken=taken.Add(take);actions.Add(new(take.RewardIndex,false,take.Card,take.UpgradeLevel,take.Enchantment));frames.Add(Frame());
        }
        frames.Add(Frame());
        foreach(int index in solution.RerollRewardIndices)
        {
            var reward=Reward(index);
            Enter(reward);Submit(new RerollCardReward(reward.Id));
            actions.Add(new(index,true));frames.Add(Frame());
            // Card takes do not advance Crystal's event RNG or change later
            // fixed-rarity pools. Validate the reordered continuation here;
            // obtain the target in this still-open selector whenever possible.
            if(immediateTakes && solution.Takes.FirstOrDefault(t=>t.RewardIndex==index) is { } immediate) Take(immediate);
        }
        foreach(var take in solution.Takes.Where(t=>!taken.Any(done=>done.RewardIndex==t.RewardIndex))) Take(take);
        return new(actions.ToImmutable(),frames.ToImmutable());
    }
}
