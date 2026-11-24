using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// An unfinished route node is a replay from its committed entry boundary,
// never a serialized Working snapshot or a synthetic skipped checkpoint.
internal sealed record PredictorNodeDraft(ImmutableArray<PredictorRoute> EntryRoutes,
    ImmutableArray<PredictorInput> Inputs, string ExpectedNodeId, PredictorPhase ExpectedPhase)
{
    public ResolveCombat? CombatInputForEditing { get; init; }
}

internal sealed partial class PredictorRun
{
    private bool _automaticRewardPickup = true;
    internal bool AutomaticRewardPickup
    {
        get => _automaticRewardPickup;
        set
        {
            if (_automaticRewardPickup == value) return;
            _automaticRewardPickup = value;
            CompleteRouteRedrawUndo();
        }
    }

    internal PredictorNodeDraft? CaptureNodeDraft()
    {
        RequireRouteSimulation();
        if (Phase == PredictorPhase.Complete) return null;
        if (!_nodeReplayAvailable)
            throw new InvalidOperationException("PredictorActiveNodeSaveRequiresRouteReplay");
        return new(_nodeEntryRoutes, _nodeReplayInputs, Working.Position.NodeId, Phase)
        { CombatInputForEditing = _combatInputForEditing };
    }

    internal void RestoreNodeDraft(PredictorNodeDraft draft, CancellationToken token = default)
    {
        RequireRouteSimulation();
        if (Phase != PredictorPhase.Complete || draft == null || draft.EntryRoutes.IsDefault || draft.Inputs.IsDefault ||
            draft.Inputs.Any(input => input == null || input is AdjustCurrentCombat) ||
            string.IsNullOrWhiteSpace(draft.ExpectedNodeId) || !Enum.IsDefined(draft.ExpectedPhase) ||
            draft.ExpectedPhase == PredictorPhase.Complete)
            throw new InvalidDataException("PredictorNodeDraftInvalid");
        var currentRoutes = Routes;
        RestoreRoutes(draft.EntryRoutes, _preview.Settings.AcceptsPrefixes);
        AdvanceRoute(token);
        foreach (var input in draft.Inputs)
        {
            token.ThrowIfCancellationRequested();
            if (Request is not { } request || Submit(request, input, token) is PredictorInputResult.Invalid or PredictorInputResult.Stale)
                throw new InvalidDataException("PredictorNodeDraftReplayInputMismatch:" + input.GetType().Name);
        }
        if (Working.Position.NodeId != draft.ExpectedNodeId || Phase != draft.ExpectedPhase)
            throw new InvalidDataException("PredictorNodeDraftReplayBoundaryMismatch");
        // AdjustCombat truncates the replay to the pre-combat prefix while
        // retaining the old declaration for editing; it is presentation residue.
        _combatInputForEditing = draft.CombatInputForEditing;
        RestoreRoutes(currentRoutes, _preview.Settings.AcceptsPrefixes);
    }
}
