using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record CrystalRewardGuideAction(int RewardIndex,bool Reroll,ModelKey Card=default,int? UpgradeLevel=null,
    CrystalCardEnchantment? Enchantment=null,bool Sacrifice=false,ModelKey Gift=default,int? OfferedUpgradeLevel=null);
internal sealed record CrystalRewardGuideFrame(ImmutableArray<PredictorReward> Rewards,
    ImmutableArray<PredictorCrystalTake> Taken,PredictorStreamState EventRng,PredictorStreamState Niche)
{
    internal ImmutableArray<int> Sacrificed { get; init; } = [];
    internal PredictorStreamState? RewardsRng { get; init; }
    internal ImmutableArray<int> WingCounters { get; init; } = [];
    internal ImmutableArray<ModelKey> Gifts { get; init; } = [];
    internal ImmutableArray<PredictorRelic> Relics { get; init; } = [];
    internal ImmutableArray<PredictorCard> AcquiredCards { get; init; } = [];
    internal ImmutableArray<PredictorCard> Deck { get; init; } = [];
    internal ImmutableDictionary<int,long> TakenCardIds { get; init; } = ImmutableDictionary<int,long>.Empty;
}
internal sealed record CrystalRewardGuide(ImmutableArray<CrystalRewardGuideAction> Actions,
    ImmutableArray<CrystalRewardGuideFrame> Frames);

internal static class PredictorCrystalRewardGuide
{
    // Without Wing, guide only rerolls and leave collection to the player.
    // Wing plans retain necessary takes alongside their ordered sacrifices.
    internal static CrystalRewardGuide BuildRerollGuide(PredictorCrystalSnapshot source,PredictorCrystalSolution solution)
    {
        // A Wing witness can require taking a level-zero card before obtaining
        // an Egg. Its ordered takes and sacrifices must stay together.
        if(!solution.RewardActions.IsEmpty) return Build(source,solution);
        var witness=Build(source,solution,immediateTakes:false);
        int count=solution.RerollRewardIndices.Length;
        return new(witness.Actions.Take(count).ToImmutableArray(),witness.Frames.Take(count+1).ToImmutableArray());
    }

    // Reconstruct the entire suffix using stable reward ordinals. Also validates
    // deserialized solutions: no dependency on cached request IDs or live objects.
    internal static CrystalRewardGuide Build(PredictorCrystalSnapshot source,PredictorCrystalSolution solution,bool immediateTakes=true)
    {
        var run=PredictorCrystalExplorer.Replay(source,solution.Steps);
        // A legal money-only plan can end without any reward, for example when
        // no gold is reachable under the current curse policy. There is no
        // reward suffix to guide after those verified board clicks.
        if(run.Phase==PredictorPhase.Complete && run.Rewards.IsEmpty && solution.Takes.IsEmpty &&
            solution.RerollRewardIndices.IsEmpty && solution.RerollActions.IsEmpty &&
            solution.RewardActions.IsEmpty && solution.SacrificeRelics.IsEmpty)
            return new([],[]);
        if(run.Phase!=PredictorPhase.Rewards) throw new InvalidOperationException("CrystalGuideNoRewards");
        var actions=ImmutableArray.CreateBuilder<CrystalRewardGuideAction>();
        var frames=ImmutableArray.CreateBuilder<CrystalRewardGuideFrame>();
        ImmutableArray<PredictorCrystalTake> taken=[];
        ImmutableArray<int> sacrificed=[];
        ImmutableArray<ModelKey> gifts=[];
        ImmutableArray<PredictorCard> acquired=[];
        var takenCardIds=ImmutableDictionary<int,long>.Empty;
        bool wingTrace=!solution.RewardActions.IsEmpty;
        CrystalRewardGuideFrame Frame()=>new(run.Rewards,taken,run.ExportCrystal().EventRng,
            run.Working.Streams.Single(s=>s.Stream==PredictorStream.Niche))
        {
            Sacrificed=sacrificed,
            RewardsRng=wingTrace?run.Working.Streams.Single(s=>s.Stream==PredictorStream.Rewards):null,
            WingCounters=wingTrace?run.Working.Relics.Where(r=>!r.Melted && r.Key.Entry=="PAELS_WING")
                .Select(r=>r.RewardsSacrificed).ToImmutableArray():[],
            Gifts=gifts,Relics=wingTrace?run.Working.Relics:[],
            AcquiredCards=acquired.Select(c=>run.Working.Card(c.Id)).ToImmutableArray(),TakenCardIds=takenCardIds,
            Deck=wingTrace?run.Working.Deck.Select(run.Working.Card).ToImmutableArray():[]
        };
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
            Enter(reward);
            var card=wingTrace?reward.Cards.FirstOrDefault(c=>c.Key==take.Card && ProducesTake(c)):
                reward.Cards.FirstOrDefault(take.MatchesCard);
            if(card==null) throw new InvalidOperationException("CrystalGuideCardMissing");
            bool ProducesTake(PredictorCard offered)
            {
                var preview=run.ForkCrystalRewards();var deck=preview.Working.Deck.ToHashSet();
                return preview.Submit(preview.Request!,new TakeReward(reward.Id,offered.Id))==PredictorInputResult.Accepted &&
                    preview.Working.Deck.Where(id=>!deck.Contains(id)).Select(preview.Working.Card).Any(take.MatchesCard);
            }
            var before=run.Working.Deck.ToHashSet();
            Submit(new TakeReward(reward.Id,card.Id));
            var inserted=run.Working.Deck.Where(id=>!before.Contains(id)).Select(run.Working.Card).ToArray();
            if(!inserted.Any(take.MatchesCard))
                throw new InvalidOperationException("CrystalGuideTakeMismatch");
            if(wingTrace) acquired=acquired.AddRange(inserted);
            takenCardIds=takenCardIds.Add(take.RewardIndex,card.Id);
            taken=taken.Add(take);actions.Add(new(take.RewardIndex,false,take.Card,take.UpgradeLevel,take.Enchantment,
                OfferedUpgradeLevel:card.UpgradeLevel));frames.Add(Frame());
        }
        frames.Add(Frame());
        if(wingTrace)
        {
            foreach(var action in solution.RewardActions)
            {
                if(action.Kind==CrystalRewardActionKind.TakeCard)
                {
                    Take(new(action.RewardIndex,action.Card,action.UpgradeLevel,action.Enchantment));
                    continue;
                }
                if(action.Kind!=CrystalRewardActionKind.Sacrifice)
                    throw new InvalidOperationException("CrystalGuideUnknownRewardAction");
                var reward=Reward(action.RewardIndex);
                var wing=run.Working.Relics.Single(r=>!r.Melted && r.Key.Entry=="PAELS_WING");
                var before=run.Working.Relics.Select(r=>r.Id).ToHashSet();
                Enter(reward);Submit(new SacrificeCardReward(reward.Id,wing.Id));
                var obtained=run.Working.Relics.Where(r=>!before.Contains(r.Id)).Select(r=>r.Key).ToArray();
                if(action.Gift.IsValid?!obtained.SequenceEqual([action.Gift]):obtained.Length!=0)
                    throw new InvalidOperationException("CrystalGuideSacrificeGiftMismatch");
                if(run.Rewards[action.RewardIndex].Status!=PredictorRewardStatus.Taken)
                    throw new InvalidOperationException("CrystalGuideSacrificeIncomplete");
                sacrificed=sacrificed.Add(action.RewardIndex);gifts=gifts.AddRange(obtained);
                actions.Add(new(action.RewardIndex,false,Sacrifice:true,Gift:action.Gift));frames.Add(Frame());
            }
            if(!taken.SequenceEqual(solution.Takes) || !solution.SacrificeRelics.SetEquals(gifts))
                throw new InvalidOperationException("CrystalGuideRewardTraceMismatch");
            return new(actions.ToImmutable(),frames.ToImmutable());
        }
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
