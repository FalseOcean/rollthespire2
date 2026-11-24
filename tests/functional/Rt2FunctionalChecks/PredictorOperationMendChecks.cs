using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorOperationMendChecks
{
    internal static void Run(PredictorRun baseline)
    {
        var context = baseline.Context with { Live = null, Crystal = null, Operation = new(1, 0, 3, []), Modifiers = [] };
        var state = baseline.Working with { OpeningCompleted = true, CurrentRoomIsCombat = false,
            Relics = [], Position = new(1, 9, "operation:mend:fixture") };
        var scene = new PredictorOperationRestScene([PredictorRestAction.Heal, PredictorRestAction.Mend])
        { MendTargets = [new(2, 10, 81, 1, 1), new(3, 0, 80, 1, 1) { ActiveForHooks = false }] };
        var snapshot = new PredictorOperationSnapshot("mend:fixture", context, state, scene, 37, 80);
        var begin = PredictorOperation.Preview(snapshot, new SelectRestAction(PredictorRestAction.Mend));
        Require(begin.Disposition == PredictorInputResult.Accepted && begin.Waiting.MendTargetPending &&
            begin.Waiting.MendTargets.Select(t => t.PlayerId).SequenceEqual(new ulong[] { 2, 3 }) &&
            begin.Waiting.RestOffers.Length == 2 && begin.MendOutcome == null, "unset target must remain a real pending choice");
        var invalid = PredictorOperation.Preview(snapshot,
            [new SelectRestAction(PredictorRestAction.Mend), new SelectMendTarget(1)]);
        Require(invalid.Disposition == PredictorInputResult.Invalid && invalid.Waiting.MendTargetPending &&
            invalid.MendOutcome == null && invalid.Waiting.RestOffers.Length == 2, "actor cannot target self or consume rest on invalid input");
        var cancel = PredictorOperation.Preview(snapshot,
            [new SelectRestAction(PredictorRestAction.Mend), new SelectMendTarget(null)]);
        Require(cancel.Disposition == PredictorInputResult.Accepted && !cancel.Waiting.MendTargetPending &&
            cancel.Waiting.RestOffers.Length == 2 && cancel.MendOutcome == null, "cancel consumed Mend");
        var selected = PredictorOperation.Preview(snapshot,
            [new SelectRestAction(PredictorRestAction.Mend), new SelectMendTarget(2)]);
        Require(selected.Disposition == PredictorInputResult.Accepted && selected.MendOutcome is
            { PlayerId: 2, BeforeHp: 10, BeforeMaximumHp: 81, AfterHp: 54, AfterMaximumHp: 86,
                NominalHeal: 39.3m, MaxHpGain: 5, MaxHpLoss: 0, RegalPillowsNormalized: 1 } &&
            selected.Waiting.RestOffers.IsEmpty, "recipient Pillow/Humidifier decimal heal or actor rest consumption changed");
        Require(selected.CurrentHp == 37 && selected.MaximumHp == 80 &&
            selected.State.Streams.SequenceEqual(state.Streams) && selected.State.Deck.SequenceEqual(state.Deck) &&
            selected.State.PotionSlots.SequenceEqual(state.PotionSlots) && selected.Rewards.IsEmpty &&
            selected.CompletedChildRewards.IsEmpty && selected.State.CurrentHealthEffects.SequenceEqual(state.CurrentHealthEffects),
            "Mend leaked recipient health/rewards into actor state or generated heal rewards");
        Require(scene.MendTargets[0].CurrentHp == 10 && scene.MendTargets[1].CurrentHp == 0,
            "Mend mutated detached input facts");
        var restoredTargeting = PredictorOperation.Preview(snapshot with { Scene = scene with { MendTargetPending = true } },
            new SelectMendTarget(2));
        Require(restoredTargeting.MendOutcome == selected.MendOutcome && !restoredTargeting.Waiting.MendTargetPending,
            "live native target picker restoration must continue without selecting Mend again");
        var revive = PredictorOperation.Preview(snapshot,
            [new SelectRestAction(PredictorRestAction.Mend), new SelectMendTarget(3)]);
        Require(revive.MendOutcome is { NominalHeal: 24m, AfterHp: 29, AfterMaximumHp: 85, MaxHpGain: 5 },
            "revival must skip inactive Pillow before heal, then enable Humidifier afterward");
        var tiny = snapshot with { Scene = scene with
            { MendTargets = [new(2, 0, 1, 1, 1) { ActiveForHooks = false }] } };
        var noRevive = PredictorOperation.Preview(tiny,
            [new SelectRestAction(PredictorRestAction.Mend), new SelectMendTarget(2)]);
        Require(noRevive.MendOutcome is { NominalHeal: .3m, AfterHp: 0, AfterMaximumHp: 1, MaxHpGain: 0 },
            "fractional heal that rounds to zero cannot activate dead recipient hooks");
        var night = PredictorOperation.Preview(snapshot with { Context = context with
            { Modifiers = [new(new("MODIFIER", "NIGHT_TERRORS"))] } },
            [new SelectRestAction(PredictorRestAction.Mend), new SelectMendTarget(2)]);
        Require(night.MendOutcome is { NominalHeal: 81m, AfterHp: 81, AfterMaximumHp: 81, MaxHpGain: 5, MaxHpLoss: 5 },
            "Night Terrors must override heal and lose maxHP after Humidifier");
        var tinyNight = PredictorOperation.Preview(tiny with { Context = context with
            { Modifiers = [new(new("MODIFIER", "NIGHT_TERRORS"))] } },
            [new SelectRestAction(PredictorRestAction.Mend), new SelectMendTarget(2)]);
        Require(tinyNight.MendOutcome is { NominalHeal: 1m, AfterHp: 1, AfterMaximumHp: 1, MaxHpGain: 5, MaxHpLoss: 5 },
            "Night Terrors revival must activate Humidifier before maxHP loss");
        var tentState = state with { Relics = [new(state.NextInstanceId, new("RELIC", "MINIATURE_TENT"), 1)],
            NextInstanceId = state.NextInstanceId + 1 };
        var tent = PredictorOperation.Preview(snapshot with { State = tentState },
            [new SelectRestAction(PredictorRestAction.Mend), new SelectMendTarget(2)]);
        Require(tent.Waiting.RestOffers.Select(o => o.Action).SequenceEqual(new[] { PredictorRestAction.Heal }),
            "actor Miniature Tent must retain actor's other rest actions");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Predictor operation Mend: " + message);
    }
}
