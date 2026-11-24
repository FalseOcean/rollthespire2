using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record AdjustCurrentCombat : PredictorInput;

// Detached requirements for the current unsimulated fight. Continuation owns
// these source facts; the route view only publishes them for declaration.
internal sealed record PredictorCombatRequirements(
    ImmutableArray<PredictorEnemyCreationSource> EnemyCreations,
    bool StolenCards, bool GremlinGold, bool FirstPlayerTurn,
    bool VictoryHealth, bool EventTimeout)
{
    internal static PredictorCombatRequirements None { get; } = new([], false, false, false, false, false);
    internal bool NeedsDeclaration => !EnemyCreations.IsEmpty || StolenCards || GremlinGold ||
        FirstPlayerTurn || VictoryHealth || EventTimeout;
}

internal sealed partial class PredictorRun
{
    // Live-node input prefix only, not an extra persistence format. Revising
    // combat replays accepted choices from the existing entry checkpoint.
    // This also retains an event parent's choices before its nested battle.
    private ImmutableArray<PredictorInput> _nodeReplayInputs = [];
    private ImmutableArray<PredictorInput>? _combatReplayPrefix;
    private ImmutableArray<PredictorRoute> _nodeEntryRoutes = [];
    private ResolveCombat? _combatInputForEditing;
    private bool _nodeReplayAvailable;

    private PredictorCombatDefaults _combatDefaults = new();
    internal PredictorCombatDefaults CombatDefaults
    {
        get => _combatDefaults;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
            if (_combatDefaults.CardRules.SequenceEqual(value.CardRules) &&
                _combatDefaults.RelicRules.SequenceEqual(value.RelicRules) && _combatDefaults.EnemyRules.SequenceEqual(value.EnemyRules)) return;
            _combatDefaults = value;
            CompleteRouteRedrawUndo();
        }
    }

    internal bool CanAdjustCurrentCombat => _combatReplayPrefix != null &&
        Phase == PredictorPhase.Rewards && !HasObtainChild;

    internal PredictorCombatRequirements CombatRequirements
    {
        get
        {
            if (Phase != PredictorPhase.CombatFacts) return PredictorCombatRequirements.None;
            var encounter = (_eventCombatEntry ?? _combatEntry)?.Encounter;
            return new(encounter?.EnemyCreationSources ?? [], encounter?.CanStealCards == true,
                encounter is not null && PredictorCombatRewardFacts.NeedsGremlinFacts(encounter),
                Working.Relics.Any(r => !r.Melted && r.Key.Entry == "BONE_TEA" && r.CombatsLeft > 0),
                PredictorSettlementEffects.Has(Working, "MEAT_ON_THE_BONE"),
                _eventCombatEntry?.Encounter.Key.Entry.StartsWith("BATTLEWORN_DUMMY_", StringComparison.Ordinal) == true);
        }
    }

    internal PredictorCombatDefaultResolution? ResolveCombatDefaults(PredictorCombatNodeOverrides? node = null)
    {
        if (Phase != PredictorPhase.CombatFacts) return null;
        RequireRouteSimulation();
        return PredictorCombatDefaultResolver.Resolve(Working, CombatRequirements, CombatDefaults, node, Context);
    }

    internal ResolveCombat? DefaultCombatInput() => ResolveCombatDefaults()?.Input;

    private PredictorInputResult AdjustCombat(CancellationToken token)
    {
        if (!CanAdjustCurrentCombat || !_nodeReplayAvailable) return PredictorInputResult.Invalid;
        var prefix = _combatReplayPrefix!.Value;
        var routes = _nodeEntryRoutes;
        var previous = _combatInputForEditing;
        var currentRoutes = Routes;
        var currentPlans = PlannedNodes;
        ResetToBoundary(); // invalidates every old reward/choice request
        Routes = routes;
        AdvanceRoute(token);
        foreach (var input in prefix)
        {
            token.ThrowIfCancellationRequested();
            if (Request is not { } request || Submit(request, input, token) is
                PredictorInputResult.Invalid or PredictorInputResult.Stale)
                throw new InvalidOperationException("PredictorCombatEditReplayMismatch");
        }
        if (Phase != PredictorPhase.CombatFacts)
            throw new InvalidOperationException("PredictorCombatEditBoundaryMissing");
        _combatInputForEditing = previous;
        RestoreRoutes(currentRoutes);
        RestorePlannedNodes(currentPlans);
        return PredictorInputResult.Accepted;
    }

    private void ResetCombatWorkflow()
    {
        _nodeReplayInputs = []; _combatReplayPrefix = null;
        _nodeEntryRoutes = []; _combatInputForEditing = null; _nodeReplayAvailable = false;
    }
}
