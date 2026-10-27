using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// MEND heals another player. It does not execute HealRestSiteOption's reward
// generation, and its health consequences never belong to the acting player.
internal sealed record PredictorOperationMendTarget(ulong PlayerId, int CurrentHp, int MaximumHp,
    int RegalPillows, int StoneHumidifiers)
{
    public bool ActiveForHooks { get; init; } = true;
}
internal sealed record PredictorOperationMendOutcome(ulong PlayerId, int BeforeHp, int BeforeMaximumHp,
    int AfterHp, int AfterMaximumHp, decimal NominalHeal, int MaxHpLoss)
{
    public int MaxHpGain { get; init; }
    public int RegalPillowsNormalized { get; init; }
}
internal sealed record SelectMendTarget(ulong? PlayerId) : PredictorInput;

internal sealed partial class PredictorRun
{
    private ImmutableArray<PredictorOperationMendTarget> _operationMendTargets = [];
    internal ImmutableArray<PredictorOperationMendTarget> OperationMendTargets => _operationMendTargets;
    internal bool OperationMendTargetPending { get; private set; }
    internal PredictorOperationMendOutcome? OperationMendOutcome { get; private set; }

    internal void RestoreOperationMendTargets(ImmutableArray<PredictorOperationMendTarget> targets, bool pending = false)
    {
        if (targets.IsDefault || targets.Any(t => t == null || t.CurrentHp < 0 || t.MaximumHp is < 1 or > 999999999 ||
                t.CurrentHp > t.MaximumHp || t.RegalPillows < 0 || t.StoneHumidifiers < 0 ||
                t.PlayerId == Context.Operation?.PlayerId) || targets.Select(t => t.PlayerId).Distinct().Count() != targets.Length)
            throw new InvalidDataException("PredictorOperationMendTargetsInvalid");
        _operationMendTargets = targets;
        if (pending && targets.IsEmpty) throw new InvalidDataException("PredictorOperationMendTargetsMissing");
        OperationMendTargetPending = pending;
        OperationMendOutcome = null;
    }

    internal PredictorInputResult? TrySubmitOperationMend(PredictorInput input)
    {
        if (Context.Operation == null) return null;
        if (input is SelectRestAction { Action: PredictorRestAction.Mend })
        {
            if (Phase != PredictorPhase.RestSite || !RestOptions.Contains(PredictorRestAction.Mend) || OperationMendTargetPending)
                return PredictorInputResult.Invalid;
            if (_operationMendTargets.IsEmpty) return PredictorInputResult.NotAllowed;
            OperationMendTargetPending = true;
            Accept(new("BeginRestMendTarget", 0, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (!OperationMendTargetPending)
            return input is SelectMendTarget ? PredictorInputResult.Invalid : null;
        if (input is not SelectMendTarget selected) return PredictorInputResult.Invalid;
        if (selected.PlayerId == null)
        {
            OperationMendTargetPending = false;
            Accept(new("CancelRestMendTarget", 0, [], null, null));
            return PredictorInputResult.Accepted;
        }
        var target = _operationMendTargets.SingleOrDefault(t => t.PlayerId == selected.PlayerId);
        if (target == null) return PredictorInputResult.Invalid;
        decimal nominalHeal = Context.HasRunModifier("NIGHT_TERRORS")
            ? target.MaximumHp : target.MaximumHp * .3m + (target.ActiveForHooks ? 15m * target.RegalPillows : 0m);
        int hp = (int)Math.Min(target.MaximumHp, target.CurrentHp + nominalHeal);
        // HealInternal reactivates hooks on resurrection, before AfterRestSiteHeal.
        bool activeAfterHeal = target.ActiveForHooks || target.CurrentHp == 0 && hp > 0;
        int gainedMax = activeAfterHeal ? Math.Min(checked(5 * target.StoneHumidifiers), 999999999 - target.MaximumHp) : 0;
        int maxHp = checked(target.MaximumHp + gainedMax);
        hp = checked(hp + gainedMax);
        int lostMax = Context.HasRunModifier("NIGHT_TERRORS") ? Math.Min(5, maxHp - 1) : 0;
        maxHp -= lostMax;
        hp = Math.Min(hp, maxHp);
        OperationMendOutcome = new(target.PlayerId, target.CurrentHp, target.MaximumHp, hp, maxHp, nominalHeal, lostMax)
            { MaxHpGain = gainedMax, RegalPillowsNormalized = activeAfterHeal ? target.RegalPillows : 0 };
        _operationMendTargets = _operationMendTargets.SetItem(_operationMendTargets.IndexOf(target),
            target with { CurrentHp = hp, MaximumHp = maxHp, ActiveForHooks = activeAfterHeal });
        OperationMendTargetPending = false;
        // No actor EventHealthConsequences: recipient facts are separate above.
        Accept(new("RestMend", 0, [], null, null));
        CompleteRestAction(PredictorRestAction.Mend);
        return PredictorInputResult.Accepted;
    }
}
