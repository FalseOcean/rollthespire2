using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // This is forecast state, never part of a checkpoint or a serialized draft.
    // Forks share immutable policy and accepted inputs; mutable knowledge and
    // observations belong to one calculation. Real event handlers stay on Run.
    private RoutePreviewContext _preview = new();

    private sealed record RoutePreviewSettings(PredictorRoutePreviewPolicy Policy, bool AcceptsPrefixes,
        ImmutableArray<PredictorPlannedNode> Plans, ImmutableDictionary<string, PredictorPlannedEvent> PotionPlans)
    {
        internal static readonly RoutePreviewSettings Default = new(PredictorRoutePreviewPolicy.AuthoredChoices,
            false, [], ImmutableDictionary<string, PredictorPlannedEvent>.Empty);
    }

    private sealed class RoutePreviewContext
    {
        internal RoutePreviewSettings Settings = RoutePreviewSettings.Default;
        internal bool RequiresEventChoice;
        internal RoutePartialState? Partial;
        internal PredictorRouteFactRecorder? InputFacts;
        internal EventInputSnapshot? InputSnapshot;
        internal EventTraceBranch? EventTrace;
        internal PredictorKnowledgeOrigin OccurrenceOrigin = PredictorKnowledgeOrigin.PlayerInput;
        internal EventPreviewObservations Event = new();
        internal RewardPreviewObservations Rewards = new();
        internal ObtainPreviewObservations Obtains = new();
        internal Func<int, bool>? ShopAutomaticPurchaseAllowed;
        internal List<PredictorCombatPendingCause>? ShopEntryCauses;
        internal bool RestDefaultInProgress;
        internal ImmutableDictionary<long, LivePaelsToothStorage> PaelsToothStorage = ImmutableDictionary<long, LivePaelsToothStorage>.Empty;
        internal ImmutableArray<PredictorCombatPendingCause> PaelsToothCombatCauses = [];
    }

    private sealed class EventPreviewObservations
    {
        internal bool ProjectHealthIndependently;
        internal ImmutableArray<PredictorEventHealthDelta> PendingHealthCommands = [];
        internal bool UnknownHealingAmount;
        internal int? AssignedGold;
        internal ImmutableArray<RouteEventOptionOutcome> OptionOutcomes = [];
        internal readonly List<RouteEventObtainHealthProjection> ObtainHealthProjections = [];
        internal ImmutableArray<PredictorCombatRewardKnowledge> CombatRewardKnowledge = [];
        internal readonly List<RouteDependency> CombatAfterGeneration = [];
        internal readonly List<(int InputIndex, PredictorPlannedEventAction Action)> CombatProjections = [];
        internal PendingRouteEventCardEffect? PendingCardEffect;
        internal bool DeferCardTarget;
        internal bool BridgeEntryPending;
        internal bool BridgeEntrySummarized;

        internal void EnterCombatParent()
        {
            CombatProjections.Clear(); CombatAfterGeneration.Clear(); CombatRewardKnowledge = [];
        }

        internal void EnterEvent()
        {
            ProjectHealthIndependently = false; PendingHealthCommands = []; UnknownHealingAmount = false;
        }
    }

    private sealed class RewardPreviewObservations
    {
        internal readonly HashSet<long> DefaultCardSkips = [];
        internal readonly Dictionary<long, (string NodeId, PredictorCombatRewardKnowledge Knowledge)> DefaultCardSkipKnowledge = [];
        internal readonly Dictionary<long, (string NodeId, PredictorCombatRewardKnowledge Knowledge)> PotionDisplayKnowledge = [];
        internal readonly Dictionary<long, RouteRewardResult> Results = [];
        internal readonly Dictionary<long, ImmutableArray<PredictorReward>> SacrificeSourceRewards = [];
        internal readonly Dictionary<long, ImmutableArray<PredictorCombatRewardKnowledge>> SacrificeSourceKnowledge = [];
        internal PredictorCompletedRewardEdit? CompletedEdit;
    }

    private sealed class ObtainPreviewObservations
    {
        internal readonly List<(string Node, ModelKey Source, PredictorOptionalObtainAddress Address)> Addresses = [];
        internal readonly List<(string Node, PredictorObtainHealthEffect Effect)> HealthEffects = [];
        internal readonly HashSet<(string Node, ModelKey Source)> Projected = [];
        internal readonly List<(string Node, PredictorOptionalObtainInput Input)> OptionalInputs = [];
        internal readonly Dictionary<string, int> ReceiptCounts = [];
    }

    private RoutePartialState StartRouteKnowledge(List<RouteDependency> dependencies) =>
        _preview.Partial = new(Context, Working, dependencies, new PredictorRouteFactRecorder());

    private PredictorRun CreateRoutePreview(ImmutableArray<PredictorPlannedNode> plans,
        PredictorRoutePreviewPolicy policy, bool prefixes)
    {
        var copy = new PredictorRun(Context, _checkpoints, Revision)
        { CombatDefaults = CombatDefaults, AutomaticRewardPickup = AutomaticRewardPickup };
        copy._preview.Settings = new(policy, prefixes, plans, plans.Where(plan => plan.Event?.Potions != null)
            .ToImmutableDictionary(plan => plan.NodeId, plan => plan.Event!));
        return copy;
    }

    // Event replay needs the forecast policy but no current mutable knowledge.
    // Room-only observations historically use concrete execution with just the
    // selected-prefix contract. Neither mode starts from the final Working state.
    private enum PreviewReplayPurpose { EventObservation, EventPlanBinding, RoomObservation }

    private PredictorRun CreateEntryReplay(PreviewReplayPurpose purpose)
    {
        bool eventPolicy = purpose switch
        {
            PreviewReplayPurpose.EventObservation or PreviewReplayPurpose.EventPlanBinding => true,
            PreviewReplayPurpose.RoomObservation => false,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };
        var copy = new PredictorRun(Context, _checkpoints, Revision)
        { CombatDefaults = CombatDefaults, AutomaticRewardPickup = AutomaticRewardPickup };
        // A replay observes/binds its supplied prefix, not the route walker's
        // future obtain-target plan. Keep the existing boundary explicit.
        copy._preview.Settings = eventPolicy ? _preview.Settings with { Plans = [] }
            : RoutePreviewSettings.Default with { AcceptsPrefixes = _preview.Settings.AcceptsPrefixes };
        copy._preview.RequiresEventChoice = eventPolicy && _preview.RequiresEventChoice;
        if (eventPolicy) copy.CopyLivePaelsToothFrom(this, replayNodeEntry: true);
        return copy;
    }

    private static void AttachEventTrace(PredictorRun copy, PredictorRun source, string scope)
    {
        var trace = source.EventTrace();
        var facts = source._preview.Partial?.Facts ?? source._preview.InputFacts ?? trace?.Facts;
        copy._preview.InputFacts = facts?.Fork();
        copy._preview.EventTrace = trace?.Fork(scope, copy._preview.InputFacts!);
        copy._preview.EventTrace?.ReplayedBoundary(copy);
    }

    private static List<RouteDependency> AttachEventOptionContext(PredictorRun branch, PredictorRun source,
        List<RouteDependency> dependencies, string scope)
    {
        AttachEventTrace(branch, source, scope);
        if (source._preview.Partial is { } partial)
            branch._preview.InputSnapshot = new(branch.Working, partial.Deck, partial.EventGoldBalance(source.Working));
        var privateDependencies = new List<RouteDependency>(dependencies);
        if (branch._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults)
            branch._preview.Partial = new(branch.Context, branch.Working, privateDependencies,
                branch._preview.InputFacts ?? new PredictorRouteFactRecorder(), source._preview.Partial);
        return privateDependencies;
    }

    // Settlement starts at the actual combat boundary. Do not replay entry,
    // creatures or the parent event; its private placeholders remain masked.
    private PredictorRun CreateCombatSettlement(PredictorContext context, PredictorCombatEntry entry,
        PredictorCombatFacts facts, CancellationToken token)
    {
        var copy = new PredictorRun(context, Working)
        { _eventCombatEntry = _eventCombatEntry, _eventCombatResume = _eventCombatResume };
        copy.CopyLivePaelsToothFrom(this);
        copy.SettleCombat(entry, 1f, facts, token);
        copy.Working = copy.Working with { Position = Working.Position };
        copy.Routes = Routes; copy.CombatDefaults = CombatDefaults; copy.AutomaticRewardPickup = AutomaticRewardPickup;
        return copy;
    }

    // Only the private route walker relinquishes this context. The observation
    // API never exposes the witness, nor aliases it to its caller's real run.
    private PredictorRun AcceptSettledRouteCombat(PredictorRun settled)
    {
        // Keep settlement's updated Tooth proof and fresh node observations;
        // only forecast policy/partial knowledge continues across this handoff.
        settled._preview.Settings = _preview.Settings;
        settled._preview.Partial = _preview.Partial;
        _preview = new();
        return settled;
    }

    // A nested fight keeps this event's replay prefix and deferred return. Only
    // the already settled reward transaction joins it; entry is not executed.
    private void AcceptSettledEventCombat(PredictorRun settled, PredictorPlannedEventAction step,
        ImmutableArray<PredictorCombatRewardKnowledge> knowledge)
    {
        CopyLivePaelsToothFrom(settled);
        _preview.Event.CombatProjections.Add((_nodeReplayInputs.Length, step));
        Working = settled.Working; Rewards = settled.Rewards; GenerationStages = settled.GenerationStages;
        _inputs = _inputs.AddRange(settled._inputs);
        _parentReward = _cardSelection = _rewardRemovalId = null;
        _eventDeferredCombatExtras = settled._eventDeferredCombatExtras;
        _eventCombatRanOutOfTime = step.EventRanOutOfTime; _eventCombatAwaitRewards = true;
        _preview.Event.CombatRewardKnowledge = knowledge;
    }

    private void ResetPreviewToBoundary() => _preview = new() { Settings = _preview.Settings };
}
