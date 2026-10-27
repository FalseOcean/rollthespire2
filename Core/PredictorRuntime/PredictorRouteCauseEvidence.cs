using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorPendingReasonKind
{
    UserChoice, MissingObservation, ImplementationGap, ExecutionFault,
    FlowGate, PlanBinding, KnownUnavailable, DerivedDependency
}

internal sealed record PredictorPendingChoice(string Identity, string Category, string SourceKey, int Occurrence)
{
    public PredictorRewardKind? RewardKind { get; init; }
    public PredictorRewardOrigin? RewardOrigin { get; init; }
    public ModelKey? Model { get; init; }
    public string Description => Category == "Reward"
        ? $"{RewardKind} · {RewardOrigin} · {Model?.Serialized ?? "—"} · #{Occurrence + 1}"
        : $"{Category} · {SourceKey} · #{Occurrence + 1}";
}

internal sealed record PredictorPendingInput(string PropertyKey, string Description);

// Evidence describes the current guard, not a new admission rule. Unknown or
// coarse readers have no invented player input. All existing domain decisions
// remain owned by the caller; this module only supplies identity/provenance.
internal static class PredictorRouteCauseEvidence
{
    private sealed record Definition(PredictorPendingReasonKind Kind, string Evidence,
        ImmutableArray<string> Properties);
    private sealed class OpaqueIdentity { internal string Value { get; } = Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<PredictorCombatPendingCause, OpaqueIdentity> Unlocated = new();

    internal static string Key(params string[] parts) => string.Concat(parts.Select(part =>
        part.Length.ToString(CultureInfo.InvariantCulture) + ":" + part));

    internal static PredictorPendingChoice RewardChoice(string nodeId, PredictorReward reward, int occurrence)
    {
        if (occurrence < 0) throw new ArgumentOutOfRangeException(nameof(occurrence));
        string source = Key(reward.Kind.ToString(), reward.Origin.ToString(), reward.Key.Serialized);
        return new(Key("reward-choice", nodeId, source, occurrence.ToString(CultureInfo.InvariantCulture)),
            "Reward", source, occurrence)
        { RewardKind = reward.Kind, RewardOrigin = reward.Origin, Model = reward.Key.IsValid ? reward.Key : null };
    }

    internal static PredictorCombatPendingCause Create(string code, string nodeId,
        PredictorCombatDomain domains, ImmutableArray<PredictorCombatPendingCause> parents = default,
        string? occurrenceKey = null, PredictorPendingChoice? choice = null,
        ImmutableArray<string> properties = default)
        => Bind(new(code, code, domains) { NodeId = nodeId, Choice = choice, ChoiceId = choice?.Identity,
            Parents = parents.IsDefault ? [] : parents,
            RequiredPropertyKeys = properties.IsDefault ? [] : properties }, nodeId, occurrenceKey);

    // For producers with stronger branch-specific evidence than the legacy
    // code inventory. Bind/Normalize/Merge preserve this explicit contract.
    internal static PredictorCombatPendingCause CreateExplicit(string code, string nodeId,
        PredictorCombatDomain domains, PredictorPendingReasonKind reasonKind, string sourceEvidence,
        ImmutableArray<string> properties, ImmutableArray<PredictorCombatPendingCause> parents = default,
        string? occurrenceKey = null, PredictorPendingChoice? choice = null)
    {
        if (string.IsNullOrWhiteSpace(sourceEvidence)) throw new ArgumentException("PredictorCauseSourceEvidenceRequired");
        return Bind(new(code, code, domains)
        {
            NodeId = nodeId, Choice = choice, ChoiceId = choice?.Identity, ReasonKind = reasonKind,
            SourceEvidence = sourceEvidence, RequiredPropertyKeys = properties.IsDefault ? [] : properties,
            Parents = parents.IsDefault ? [] : parents
        }, nodeId, occurrenceKey);
    }

    internal static PredictorCombatPendingCause Bind(PredictorCombatPendingCause cause,
        string? nodeId = null, string? occurrenceKey = null)
    {
        ArgumentNullException.ThrowIfNull(cause);
        string? originNode = cause.NodeId ?? nodeId;
        var definition = Describe(cause.Code);
        bool hasClassification = !string.IsNullOrWhiteSpace(cause.Identity) ||
            cause.SourceEvidence != "Dependency evidence has not been bound." ||
            cause.ReasonKind != PredictorPendingReasonKind.ImplementationGap;
        var reasonKind = hasClassification ? cause.ReasonKind : definition.Kind;
        string identity = !string.IsNullOrWhiteSpace(cause.Identity) ? cause.Identity :
            originNode != null ? Key("cause", originNode, cause.Code, cause.EnemySource?.ToString() ?? "",
                occurrenceKey ?? cause.ChoiceId ?? "") :
            Key("unlocated-cause", Unlocated.GetValue(cause, _ => new()).Value);
        string? choiceId = cause.ChoiceId ?? (reasonKind == PredictorPendingReasonKind.UserChoice
            ? Key("choice", originNode ?? "unlocated", cause.Code, occurrenceKey ?? identity) : null);
        var properties = hasClassification ? (cause.RequiredPropertyKeys.IsDefault ? [] : cause.RequiredPropertyKeys) :
            cause.RequiredPropertyKeys.IsDefaultOrEmpty ? definition.Properties : cause.RequiredPropertyKeys;
        if (!hasClassification && cause.Code == "EnemyCreationCount" && cause.EnemySource is { } source)
            properties = ["combat.enemyCreation." + source + ".count"];
        var parents = Normalize(cause.Parents.IsDefault ? [] : cause.Parents)
            .Where(parent => parent.Identity != identity).ToImmutableArray();
        return cause with
        {
            NodeId = originNode, Identity = identity, ChoiceId = choiceId,
            ReasonKind = reasonKind, RequiredPropertyKeys = properties,
            Parents = parents, SourceEvidence = hasClassification ? cause.SourceEvidence : definition.Evidence
        };
    }

    internal static PredictorCombatPendingCause WithParents(PredictorCombatPendingCause cause,
        ImmutableArray<PredictorCombatPendingCause> parents)
    {
        var bound = Bind(cause);
        return bound with { Parents = Normalize(bound.Parents.AddRange(parents.IsDefault ? [] : parents))
            .Where(parent => parent.Identity != bound.Identity).ToImmutableArray() };
    }

    internal static PredictorCombatPendingCause Merge(PredictorCombatPendingCause left,
        PredictorCombatPendingCause right)
    {
        left = Bind(left); right = Bind(right);
        if (left.Identity != right.Identity) throw new ArgumentException("PredictorCauseIdentityMismatch");
        return WithParents(left with
        {
            AffectedDomains = left.AffectedDomains | right.AffectedDomains,
            RequiredPropertyKeys = left.RequiredPropertyKeys.AddRange(right.RequiredPropertyKeys)
                .Distinct(StringComparer.Ordinal).ToImmutableArray()
        }, right.Parents);
    }

    internal static ImmutableArray<PredictorCombatPendingCause> Normalize(
        IEnumerable<PredictorCombatPendingCause> causes)
    {
        var result = new List<PredictorCombatPendingCause>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in causes)
        {
            var cause = string.IsNullOrWhiteSpace(source.Identity) ? Bind(source) : source;
            if (!index.TryGetValue(cause.Identity, out int at))
            { index.Add(cause.Identity, result.Count); result.Add(cause); }
            else result[at] = Merge(result[at], cause);
        }
        return result.ToImmutableArray();
    }

    // Both UI and trace retain a producer's own classified limitation alongside
    // its upstream causes. Only an explicit DerivedDependency is transparent:
    // having parents alone does not prove a flow guard or gap has no own limit.
    internal static bool IsOriginalCause(PredictorCombatPendingCause cause) =>
        cause.ReasonKind != PredictorPendingReasonKind.DerivedDependency;

    internal static ImmutableArray<PredictorCombatPendingCause> OriginalCauses(
        IEnumerable<PredictorCombatPendingCause> causes)
    {
        var evidence = new Dictionary<string, PredictorCombatPendingCause>(StringComparer.Ordinal);
        var expanded = new HashSet<PredictorCombatPendingCause>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<PredictorCombatPendingCause>(Normalize(causes).Reverse());
        while (stack.TryPop(out var value))
        {
            if (!expanded.Add(value)) continue;
            var cause = Bind(value);
            evidence[cause.Identity] = evidence.TryGetValue(cause.Identity, out var existing)
                ? Merge(existing, cause) : cause;
            foreach (var parent in cause.Parents.Reverse()) stack.Push(parent);
        }
        return evidence.Values.Where(IsOriginalCause).ToImmutableArray();
    }

    internal static ImmutableArray<PredictorCombatPendingCause> Roots(
        IEnumerable<PredictorCombatPendingCause> causes) => OriginalCauses(causes);
    internal static ImmutableArray<PredictorCombatPendingCause> Roots(PredictorCombatPendingCause cause)
        => OriginalCauses([cause]);

    internal static ImmutableArray<PredictorPendingInput> RequiredInputs(PredictorCombatPendingCause cause)
    {
        cause = Bind(cause);
        return cause.ReasonKind is PredictorPendingReasonKind.UserChoice or PredictorPendingReasonKind.MissingObservation
            ? cause.RequiredPropertyKeys.Select(key => new PredictorPendingInput(key, cause.Code + " · " + key)).ToImmutableArray()
            : [];
    }

    // Explicit producer-condition inventory, not a suffix/name heuristic.
    // FlowGate keys intentionally identify the coarse guard until its actual
    // source reader is proven. They are diagnostics, never player obligations.
    private static Definition Describe(string code)
    {
        if (code.StartsWith("LiveSourceHooksPending:", StringComparison.Ordinal))
            return new(PredictorPendingReasonKind.ImplementationGap,
                "PredictorLiveRunCapture: active model has no detached hook implementation.", []);
        return code switch
        {
            "EnemyCreationCount" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorCombatPreview.cs:34", ["combat.enemyCreation.count"]),
            "StolenCardIdentity" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorCombatPreview.cs:42", ["combat.stolenCards.identity", "combat.stolenCards.returned"]),
            "StolenGoldLoss" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorCombatPreview.cs:48", ["combat.gold.stolenLoss"]),
            "RecoveredGoldAmount" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorCombatPreview.cs:50", ["combat.gold.recoveredAmount", "combat.gold.stolenLoss"]),
            "VictoryHealth" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorCombatPreview.cs:54", ["health.victory.current", "health.victory.maximum"]),
            "FirstPlayerTurn" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorCombatPreview.cs:56", ["combat.firstPlayerTurnOccurred"]),
            "EventTimeout" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorCombatPreview.cs:58", ["combat.eventTimedOut"]),
            "SealOfGoldDeduction" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorCombatPreview.cs:61", ["configuration.sealGoldDeduction"]),
            "ExtraRewardOrder" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorCombatPreview.cs:91", ["combat.extraRewardOrder"]),
            "PlannedCombatFactsPending" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteContinuation.cs:111", ["flow.binding.PlannedCombatFactsPending"]),
            "RestRewardChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:191", ["choice.rest.reward"]),
            "RestChildChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:199", ["choice.obtain.child"]),
            "RestChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:206", ["choice.rest.action"]),
            "RestTargetChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:211", ["choice.rest.target"]),
            "RouteNodeOutsideContinuationSlice" => new(PredictorPendingReasonKind.ImplementationGap, "Core/PredictorRuntime/PredictorRouteContinuation.cs:239", ["flow.binding.RouteNodeOutsideContinuationSlice"]),
            "PlannedPotionStagePending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:252", ["flow.guard.PlannedPotionStagePending"]),
            "ActBoundaryOutsideContinuationSlice" => new(PredictorPendingReasonKind.ImplementationGap, "Core/PredictorRuntime/PredictorRouteContinuation.cs:320", ["flow.binding.ActBoundaryOutsideContinuationSlice"]),
            "RoutePlanNeedsUpdate" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteContinuation.cs:333", ["flow.binding.RoutePlanNeedsUpdate"]),
            "UnknownRoomLanternKeyPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:348", ["cards.lanternKey.membership"]),
            "UnknownRoomDowsingPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:348", ["cards.dowsing.membership"]),
            "ActMapPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:386", ["flow.guard.ActMapPending"]),
            "ActEntryCardPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:391", ["rng.niche.state"]),
            "NextActRouteRequired" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:397", ["choice.route.nextActPath"]),
            "PlannedRewardAlternativeUnavailable" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteRewardAlternatives.cs:ApplyRouteRewardAlternative", ["choice.reward.alternative"]),
            "PlannedRewardAlternativeDependent" => new(PredictorPendingReasonKind.DerivedDependency, "Core/PredictorRuntime/PredictorRouteRewardAlternatives.cs:ApplyRouteRewardAlternative; RerollReadDomains/SacrificeReadCauses retain the exact incoming reads", ["rng.Rewards.state", "relics.orderedInventory", "relics.paelsWing.rewardsSacrificed"]),
            "PlannedRerolledRewardChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteRewardAlternatives.cs:ApplyRouteRewardAlternative; SameOffer validates the recorded replacement", ["choice.reward.rerolledOffer"]),
            "PlannedRewardChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteContinuation.cs:481", ["flow.binding.PlannedRewardChanged"]),
            "PlannedRewardPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:489", ["flow.guard.PlannedRewardPending"]),
            "PlannedPotionReceiptPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:510", ["potions.capacity", "potions.occupiedSlots"]),
            "PlannedRelicObtainDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:519", ["flow.guard.PlannedRelicObtainDependent"]),
            "RewardChildChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:541", ["choice.obtain.child"]),
            "PotionReceiptPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:566", ["potions.capacity", "potions.occupiedSlots"]),
            "PlannedRestTargetPending" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteContinuation.cs:683", ["card.target.identity", "card.target.upgradeEligibility", "card.target.removable"]),
            "RestRelicRngPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:695", ["rng.rewards.state"]),
            "RestObtainDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:704", ["flow.guard.RestObtainDependent"]),
            "PlannedRestActionUnavailable" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteContinuation.cs:711", ["flow.binding.PlannedRestActionUnavailable"]),
            "RestMaximumHp" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorRouteContinuation.cs:727", ["health.rest.maximum"]),
            "EventOccurrenceChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteEvents.cs:28", ["choice.event.occurrence"]),
            "EventOccurrenceIdentityPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:30", ["flow.guard.EventOccurrenceIdentityPending"]),
            "PlannedEventOccurrenceChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:34", ["flow.binding.PlannedEventOccurrenceChanged"]),
            "PlannedEventOccurrencePending" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:37", ["flow.binding.PlannedEventOccurrencePending"]),
            "PlannedEventChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:44", ["flow.binding.PlannedEventChanged"]),
            "EventNotModeled" => new(PredictorPendingReasonKind.ImplementationGap, "Core/PredictorRuntime/PredictorRouteEvents.cs:129", ["flow.binding.EventNotModeled"]),
            "EventAccountPointsRequired" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorRouteEvents.cs:130", ["account.wongoPoints"]),
            "EventEntryDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:132", ["flow.guard.EventEntryDependent"]),
            "EventEntryTwoPotionsRequired" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteEvents.cs:135", ["flow.binding.EventEntryTwoPotionsRequired"]),
            "EventEntryPotionRequired" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteEvents.cs:136", ["flow.binding.EventEntryPotionRequired"]),
            "EventEntryRemovableCardRequired" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteEvents.cs:137", ["flow.binding.EventEntryRemovableCardRequired"]),
            "PlannedEventOptionPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:241", ["flow.guard.PlannedEventOptionPending"]),
            "PlannedEventOptionChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:244", ["flow.binding.PlannedEventOptionChanged"]),
            "PlannedEventActionDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:247", ["flow.guard.PlannedEventActionDependent"]),
            "PlannedEventCardPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:252", ["flow.guard.PlannedEventCardPending"]),
            "PlannedEventOfferedCardsPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:256", ["flow.guard.PlannedEventOfferedCardsPending"]),
            "PlannedEventOfferedCardsChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:259", ["flow.binding.PlannedEventOfferedCardsChanged"]),
            "PlannedEventDeckTargetPending" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:266", ["card.target.identity", "card.target.eventEligibility"]),
            "PlannedEventCardHooksPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:269", ["flow.guard.PlannedEventCardHooksPending"]),
            "PlannedEventHealthPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:274", ["health.event.current", "health.event.maximum", "flow.eventHealth"]),
            "PlannedEventHealthDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:279", ["flow.guard.PlannedEventHealthDependent"]),
            "PlannedEventRewardPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:286", ["flow.guard.PlannedEventRewardPending"]),
            "PlannedEventEncounterChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:294", ["flow.binding.PlannedEventEncounterChanged"]),
            "PlannedEventCombatDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:296", ["flow.guard.PlannedEventCombatDependent"]),
            "PlannedEventCombatFactsPending" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:299", ["flow.binding.PlannedEventCombatFactsPending"]),
            "EventCombatDeclarationRequired" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:302", ["flow.guard.EventCombatDeclarationRequired"]),
            "PlannedEventTimeoutChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:304", ["flow.binding.PlannedEventTimeoutChanged"]),
            "PlannedEventAbandonPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:322", ["flow.guard.PlannedEventAbandonPending"]),
            "PlannedEventCrystalChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:327", ["flow.binding.PlannedEventCrystalChanged"]),
            "PlannedEventCrystalDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:330", ["flow.guard.PlannedEventCrystalDependent"]),
            "PlannedEventInputPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:340", ["flow.guard.PlannedEventInputPending"]),
            "PlannedEventAcceptedPrefixChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:475", ["flow.binding.PlannedEventAcceptedPrefixChanged"]),
            "ShopAutomaticPurchasePending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteShop.cs:39", ["flow.guard.ShopAutomaticPurchasePending"]),
            "ShopRemovalChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteShop.cs:85", ["choice.shop.removalTarget"]),
            "ShopObtainChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteShop.cs:86", ["choice.obtain.child"]),
            "ShopChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteShop.cs:86", ["choice.shop.action"]),
            "PlannedShopObtainPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteShop.cs:142", ["flow.guard.PlannedShopObtainPending"]),
            "PlannedShopLeaveUnavailable" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteShop.cs:148", ["flow.binding.PlannedShopLeaveUnavailable"]),
            "PlannedShopRemovalUnavailable" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteShop.cs:152", ["flow.binding.PlannedShopRemovalUnavailable"]),
            "PlannedShopTargetPending" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteShop.cs:155", ["card.target.identity", "card.target.removable"]),
            "PlannedShopAffordabilityPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteShop.cs:157", ["economy.goldBalance", "shop.offer.price"]),
            "PlannedShopOfferChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteShop.cs:163", ["flow.binding.PlannedShopOfferChanged"]),
            "PlannedShopOfferPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteShop.cs:165", ["flow.guard.PlannedShopOfferPending"]),
            "PlannedShopInsufficientGold" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteShop.cs:168", ["economy.goldBalance", "shop.offer.price"]),
            "PlannedShopObtainDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteShop.cs:174", ["flow.guard.PlannedShopObtainDependent"]),
            "PlannedShopPotionReceiptPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteShop.cs:176", ["potions.capacity", "potions.occupiedSlots"]),
            "PlannedShopInventoryUnavailable" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteShop.cs:182", ["flow.binding.PlannedShopInventoryUnavailable"]),
            "PlannedShopPotionInventoryFull" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteShop.cs:187", ["potions.capacity", "potions.occupiedSlots"]),
            "PlannedShopPurchaseUnavailable" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteShop.cs:187", ["flow.binding.PlannedShopPurchaseUnavailable"]),
            "PlannedAncientChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteAncient.cs:18", ["flow.binding.PlannedAncientChanged"]),
            "AncientChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteAncient.cs:22", ["choice.ancient.offer"]),
            "PlannedAncientOfferPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:27", ["flow.guard.PlannedAncientOfferPending"]),
            "PlannedAncientObtainDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:29", ["flow.guard.PlannedAncientObtainDependent"]),
            "AncientChildChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteAncient.cs:64", ["choice.obtain.child"]),
            "PlannedObtainAcceptedPrefixChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteAncient.cs:95", ["flow.binding.PlannedObtainAcceptedPrefixChanged"]),
            "PlannedObtainSourceChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteAncient.cs:167", ["flow.binding.PlannedObtainSourceChanged"]),
            "PlannedObtainRewardChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteAncient.cs:178", ["flow.binding.PlannedObtainRewardChanged"]),
            "PlannedObtainRewardPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:186", ["flow.guard.PlannedObtainRewardPending"]),
            "PlannedObtainRewardDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:192", ["flow.guard.PlannedObtainRewardDependent"]),
            "PlannedObtainClosureDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:197", ["rng.niche.state"]),
            "PlannedObtainClosurePending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:209", ["flow.guard.PlannedObtainClosurePending"]),
            "PlannedObtainCardChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteAncient.cs:216", ["flow.binding.PlannedObtainCardChanged"]),
            "PlannedObtainCardPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:217", ["rng.rewards.state"]),
            "PlannedObtainDeckTargetPending" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteAncient.cs:226", ["card.target.identity", "card.target.obtainEligibility"]),
            "PlannedObtainTransformPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:228", ["rng.niche.state"]),
            "PlannedObtainBundleChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteAncient.cs:239", ["flow.binding.PlannedObtainBundleChanged"]),
            "PlannedObtainBundlePending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:240", ["rng.rewards.state"]),
            "PlannedObtainHealthPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:244", ["health.obtain.current", "health.obtain.maximum", "flow.obtainHealth"]),
            "PlannedObtainHealthDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:245", ["flow.guard.PlannedObtainHealthDependent"]),
            "PlannedObtainPotionTargetPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:251", ["potions.slot.identity", "flow.potionReplacement"]),
            "PlannedObtainInputPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteAncient.cs:281", ["flow.guard.PlannedObtainInputPending"]),
            "TreasureOpenOrLeaveChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteTreasure.cs:91", ["choice.treasure.open"]),
            "TreasureRelicOrLeaveChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteTreasure.cs:92", ["choice.treasure.relicDisposition"]),
            "TreasureObtainChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteTreasure.cs:92", ["choice.obtain.child"]),
            "PlannedTreasureInputPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteTreasure.cs:117", ["flow.guard.PlannedTreasureInputPending"]),
            "PlannedTreasureChoiceChanged" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteTreasure.cs:134", ["flow.guard.PlannedTreasureChoiceChanged"]),
            "PlannedTreasureAcceptedPrefixChanged" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteTreasure.cs:166", ["flow.binding.PlannedTreasureAcceptedPrefixChanged"]),
            "EventNonHealthActionDependent" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEventOptionResults.cs:149", ["flow.guard.EventNonHealthActionDependent"]),
            "EventHealthOutcomePending" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorRouteEventOptionResults.cs:189", ["health.event.outcome"]),
            "EventCardsChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteEventContinuationDefaults.cs:21", ["choice.event.cards"]),
            "EventRewardsChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteEventContinuationDefaults.cs:38", ["choice.event.reward"]),
            "PlannedPotionBindingPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteInventory.cs:114", ["potions.slot.identity", "flow.potionActionBoundary"]),
            "PlannedPotionActionUnavailable" => new(PredictorPendingReasonKind.KnownUnavailable, "Core/PredictorRuntime/PredictorRouteInventory.cs:119", ["flow.binding.PlannedPotionActionUnavailable"]),
            "PlannedPotionActionPending" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteInventory.cs:125", ["flow.guard.PlannedPotionActionPending"]),
            "RestDeckDependentAction" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteContinuation.cs:688", ["flow.guard.RestDeckDependentAction"]),
            "PlannedEventAcceptedPrefixMissing" => new(PredictorPendingReasonKind.PlanBinding, "Core/PredictorRuntime/PredictorRouteEvents.cs:466", ["flow.binding.PlannedEventAcceptedPrefixMissing"]),
            "PendingEventRewardReroll" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteEventContinuationDefaults.cs:55", ["choice.reward.reroll"]),
            "LiveCurrentNodePending" => new(PredictorPendingReasonKind.FlowGate, "Infrastructure/Snapshots/PredictorLiveRunCapture.cs:138", ["flow.nativeDeparture"]),
            "LiveGameVersionUnverified" => new(PredictorPendingReasonKind.ImplementationGap, "Infrastructure/Snapshots/PredictorLiveRunCapture.cs:141", []),
            "PendingGoldChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:633", ["choice.reward.disposition"]),
            "PendingCardChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:633", ["choice.reward.cardOrSkip"]),
            "PendingSpecialCardChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:633", ["choice.reward.returnedCard"]),
            "PendingRelicChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:633", ["choice.reward.relicDisposition"]),
            "PendingPotionChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:633", ["choice.reward.potionDisposition"]),
            "PendingRemoveCardChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteContinuation.cs:633", ["choice.reward.removalTarget"]),
            "EventOptionChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteEvents.cs:74", ["choice.event.option"]),
            "EventHealthChoice" => new(PredictorPendingReasonKind.MissingObservation, "Core/PredictorRuntime/PredictorRouteEvents.cs:74", ["health.event.current", "health.event.maximum"]),
            "EventCombatChoice" => new(PredictorPendingReasonKind.FlowGate, "Core/PredictorRuntime/PredictorRouteEvents.cs:74; actual combat preview carries missing fact producers", ["flow.eventCombatDeclaration"]),
            "EventObtainChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteEvents.cs:74", ["choice.obtain.child"]),
            "EventTrialAbandonChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteEvents.cs:74", ["choice.event.abandonConfirmation"]),
            "EventCrystalChoice" => new(PredictorPendingReasonKind.UserChoice, "Core/PredictorRuntime/PredictorRouteEvents.cs:74", ["choice.crystal.nextAction"]),
            _ => new(PredictorPendingReasonKind.ImplementationGap,
                "Cause classification or source dependency has not been proved: " + code, [])
        };
    }
}
