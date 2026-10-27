using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record OpenTreasure : PredictorInput;
internal sealed record TakeTreasureRelic(long InstanceId) : PredictorInput;
internal sealed record LeaveTreasure : PredictorInput;

internal sealed partial class PredictorRun
{
    internal PredictorRelic? TreasureRelic { get; private set; }
    private bool _treasureAllowed;
    private bool _treasureObtaining;

    private void EnterTreasure(PredictorPosition position, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PredictorSettlementEffects.RequireImplementedInventory(Working, context: Context);
        var trace = _preview.Partial?.BeginTreasureGeneration(this, position);
        Working = Working with { Position = position, Relics = Working.Relics.Select(r => r.Key.Entry == "SILVER_CRUCIBLE" && !r.Melted
            ? r with { TreasureRoomsEntered = r.TreasureRoomsEntered + 1 } : r).ToImmutableArray() };
        _treasureAllowed = !Working.Relics.Any(r => !r.Melted && r.Key.Entry == "SILVER_CRUCIBLE" && r.TreasureRoomsEntered <= 1);
        _treasureObtaining = false;
        TreasureRelic = null;
        trace?.TreasureEligibility(this);
        if (_treasureAllowed)
        {
            trace?.ReadStream(this, PredictorStream.TreasureRoomRelics);
            var rng = Working.Streams.Single(s => s.Stream == PredictorStream.TreasureRoomRelics).Restore();
            float roll = rng.NextFloat();
            string rarity = roll < .5f ? "Common" : roll < .83f ? "Uncommon" : "Rare";
            var state = Working.WithRng(PredictorStream.TreasureRoomRelics, rng);
            ModelKey key;
            bool firstRunGorget = false;
            var gorget = new ModelKey("RELIC", "GORGET");
            trace?.ReadGorgetProbe(this);
            if (Context.NumberOfRuns == 0 && RoomHistory.Count(v => v.Room == PredictorRoomKind.Treasure) == 1 &&
                state.PersonalBag.Buckets.Any(b => b.Entries.Contains(gorget)))
            {
                firstRunGorget = true;
                trace?.ReadGorgetBags(this);
                key = gorget;
                state = state with { PersonalBag = state.PersonalBag.Remove(key), SharedBag = state.SharedBag.Remove(key) };
            }
            else
            {
                trace?.ReadTreasureBag(this);
                var entry = state;
                var pulled = state.SharedBag.Pull(rarity, false, k => PredictorRewardGeneration.RelicAllowed(Context, entry, k), _ => true);
                key = pulled.Key ?? new("RELIC", "CIRCLET");
                state = state with { SharedBag = pulled.Bag };
            }
            TreasureRelic = new(state.NextInstanceId, key, position.Floor);
            Working = state with { NextInstanceId = state.NextInstanceId + 1 };
            trace?.TreasureGenerated(this, rarity, firstRunGorget);
        }
        else trace?.TreasureSuppressed(this);
        _inputs = []; Rewards = [];
        Phase = PredictorPhase.TreasureClosed;
        Accept(new("EnterTreasure", TreasureRelic?.Id ?? 0, [], null, null));
    }

    private PredictorInputResult SubmitTreasure(PredictorInput input, CancellationToken token)
    {
        var trace = _preview.Partial?.BeginTreasureCommand(this, input);
        var result = SubmitTreasureCore(input, token, trace);
        trace?.Return(this, result);
        return result;
    }

    private PredictorInputResult SubmitTreasureCore(PredictorInput input, CancellationToken token,
        RoutePartialState.RoomCalculation? trace)
    {
        if (input is LeaveTreasure && !_treasureObtaining)
        {
            Accept(new(Phase == PredictorPhase.TreasureClosed ? "LeaveUnopenedTreasure" : "LeaveTreasure", TreasureRelic?.Id ?? 0, [], null, null));
            TreasureRelic = null;
            CompleteNode();
            return PredictorInputResult.Accepted;
        }
        if (Phase == PredictorPhase.TreasureClosed && input is OpenTreasure)
        {
            trace?.ReadTreasureAllowed(this);
            if (_treasureAllowed)
            {
                trace?.ReadStream(this, PredictorStream.Rewards);
                trace?.Read("normalGold.rangeMinimum", 42);
                trace?.Read("normalGold.rangeMaximum", 53);
                (int amount, var state) = Working.Draw(PredictorStream.Rewards, 42, 53);
                trace?.Read("normalGold.ascension", Context.Ascension);
                if (Context.Ascension >= 3) trace?.Read("normalGold.ascensionMultiplier", .75m);
                if (Context.Ascension >= 3) amount = (int)(amount * .75);
                trace?.ReadGoldSettlement(this);
                Working = PredictorSettlementEffects.TakeGold(state, amount);
                trace?.TreasureNormalGold(this, amount);
                var visit = Working.Visits.LastOrDefault();
                if (visit != null)
                {
                    trace?.TreasureSpoilsBefore(this, visit.Point);
                    int spoils = CompleteSpoilsMapAtTreasure(visit.Point);
                    trace?.TreasureSpoilsAfter(this, spoils);
                }
            }
            else trace?.TreasureOpeningSuppressed(this);
            Accept(new("OpenTreasure", TreasureRelic?.Id ?? 0, [], null, null));
            SettleTreasureExtraRewards(token);
            Phase = PredictorPhase.TreasureRelic;
            return PredictorInputResult.Accepted;
        }
        if (Phase != PredictorPhase.TreasureRelic || input is not TakeTreasureRelic take || TreasureRelic?.Id != take.InstanceId || _treasureObtaining)
            return PredictorInputResult.Invalid;
        trace?.TreasureTake(this);
        trace?.Write("obtain.invoked", true, alias: "obtain",
            proof: "The validated TakeTreasureRelic branch invokes BeginObtainRelic for this offered model. Child effect internals are recorded by their own owner, not inferred here.");
        _treasureObtaining = true;
        if (BeginObtainRelic(TreasureRelic.Key, TreasureRelic.Id, PredictorObtainReturn.Treasure, take.InstanceId, token))
            ResumeRoomAfterObtain(PredictorObtainReturn.Treasure, take.InstanceId);
        return PredictorInputResult.Accepted;
    }

    private void ResumeRoomAfterObtain(PredictorObtainReturn destination, long sourceId)
    {
        switch (destination)
        {
            case PredictorObtainReturn.Treasure:
                Accept(new("TakeTreasureRelic", sourceId, [], null, null));
                TreasureRelic = null;
                _treasureObtaining = false;
                Phase = PredictorPhase.TreasureRelic;
                break;
            case PredictorObtainReturn.Ancient:
                CompleteNode();
                break;
            case PredictorObtainReturn.Rest:
                ResumeRestAfterObtain(sourceId);
                break;
            case PredictorObtainReturn.RewardAlternative:
                ResumeSacrificeCardReward(sourceId);
                break;
            case PredictorObtainReturn.RestHeal:
                ResumeRestAfterHealRewards(sourceId);
                break;
            case PredictorObtainReturn.EventRestHeal:
                ResumeEventAfterMimicRestHeal(sourceId);
                break;
            default: throw new InvalidOperationException("PredictorObtainReturnInvalid");
        }
    }

    internal void CompleteRunAbandon(string source)
    {
        Working = Working with { Outcome = PredictorRunOutcome.Abandoned };
        Rewards = Rewards.Select(r => r.Status == PredictorRewardStatus.Open ? r with
            { Status = PredictorRewardStatus.Aborted, RelicSubscriptionActive = false } : r).ToImmutableArray();
        Accept(new("RunAbandoned:" + source, 0, [], null, null));
        AbortObtainFrames();
        CompleteNode();
        ResetEvent(); ResetObtain();
    }
}
