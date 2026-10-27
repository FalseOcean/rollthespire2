using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private ModelKey? _eventLayoutEncounter;
    private PredictorCombatEntry? _eventCombatEntry;
    private bool _eventCombatResume;
    private bool _eventCombatAwaitRewards;
    private bool? _eventCombatRanOutOfTime;
    private ImmutableArray<PredictorRewardPlan> _eventDeferredCombatExtras = [];

    // Combat-layout events create enemies while showing the event, before a
    // Fight option is selected. The child room reuses that roster.
    internal void PrepareEventCombatLayout(ModelKey encounter)
    {
        if (_eventLayoutEncounter == encounter) return;
        if (_eventLayoutEncounter != null) throw new InvalidOperationException("PredictorEventLayoutAlreadyPrepared");
        int count = PredictorEncounterEntry.InitialEnemyCount(encounter, Beta111Profile.Instance.ComputeRootSeed(Context.Seed), Working.Position.Floor);
        Working = PredictorEncounterEntry.ConsumeInitialEnemyHp(Working, count);
        _eventLayoutEncounter = encounter;
    }

    internal void BeginEventCombat(ModelKey encounter, ImmutableArray<PredictorRewardPlan> extras,
        bool shouldResumeAfterCombat, CancellationToken token = default)
    {
        if (_eventKey == null || extras.IsDefault || Phase != PredictorPhase.EventChoice)
            throw new InvalidOperationException("PredictorEventCombatParentMissing");
        var position = Working.Position;
        Phase = PredictorPhase.Complete;
        try
        {
            _eventCombatEntry = PrepareCombatEntry(PredictorCombatKind.Monster, position.Act, position.Floor, token,
                encounter, _eventLayoutEncounter == encounter) with { ExtraRewards = extras };
        }
        catch { Phase = PredictorPhase.EventChoice; throw; }
        Working = Working with { Position = position };
        _eventCombatResume = shouldResumeAfterCombat;
        _eventCombatAwaitRewards = false;
        _eventDeferredCombatExtras = [];
        Phase = PredictorPhase.CombatFacts;
        Accept(new("EnterEventCombat:" + encounter.Entry, 0, [], null, null));
    }

    private PredictorInputResult SubmitEventCombat(ResolveCombat input, CancellationToken token)
    {
        if (_eventCombatEntry == null) return PredictorInputResult.Invalid;
        if (input.Facts == null || input.Facts.Potions == null) return PredictorInputResult.Invalid;
        if (!PredictorCombatRewardFacts.ValidGremlin(_eventCombatEntry.Encounter, input.Facts.GremlinGold)) return PredictorInputResult.Invalid;
        if (!PredictorCombatEnemyFacts.Valid(_eventCombatEntry.Encounter, input.Facts.EnemyCreations)) return PredictorInputResult.Invalid;
        if (_eventCombatEntry.Encounter.CanStealCards != (input.Facts.StolenCards != null)) return PredictorInputResult.Invalid;
        if (Working.Relics.Any(r => !r.Melted && r.Key.Entry == "BONE_TEA" && r.CombatsLeft > 0) && !input.Facts.FirstPlayerTurnOccurred.HasValue)
            return PredictorInputResult.Invalid;
        bool dummy = _eventCombatEntry.Encounter.Key.Entry.StartsWith("BATTLEWORN_DUMMY_", StringComparison.Ordinal);
        if (dummy != input.EventRanOutOfTime.HasValue) return PredictorInputResult.Invalid;
        _eventCombatRanOutOfTime = input.EventRanOutOfTime;
        Phase = PredictorPhase.Complete;
        try { SettleCombat(_eventCombatEntry, 1f, input.Facts, token, preserveInputs: true); }
        catch (ArgumentException) { Phase = PredictorPhase.CombatFacts; _eventCombatRanOutOfTime = null; return PredictorInputResult.Invalid; }
        catch { Phase = PredictorPhase.CombatFacts; throw; }
        _eventCombatAwaitRewards = true;
        if (input.EventRanOutOfTime is { } timedOut)
            Accept(new(timedOut ? "EventCombatTimedOut" : "EventCombatDefeated", 0, [], null, null));
        if (Rewards.IsEmpty) TryFinishEventCombatRewards();
        return PredictorInputResult.Accepted;
    }

    private bool TryFinishEventCombatRewards()
    {
        if (!_eventCombatAwaitRewards) return false;
        _combatReplayPrefix = null; _combatInputForEditing = null;
        bool resume = _eventCombatResume;
        _eventCombatAwaitRewards = false; _eventCombatEntry = null;
        if (resume)
        {
            CompletedChildRewards = CompletedChildRewards.AddRange(Rewards);
            Rewards = [];
        }
        Phase = PredictorPhase.EventChoice;
        Working = Working with { CurrentRoomIsCombat = false };
        ResumeEventAfterCombat(resume, _eventCombatRanOutOfTime);
        return true;
    }

    private void ResetEventCombat()
    {
        _eventLayoutEncounter = null; _eventCombatEntry = null;
        _eventCombatResume = _eventCombatAwaitRewards = false;
        _eventCombatRanOutOfTime = null;
        _eventDeferredCombatExtras = [];
    }
}
