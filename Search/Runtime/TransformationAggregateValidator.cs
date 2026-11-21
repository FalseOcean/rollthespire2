using System.Runtime.CompilerServices;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Runtime;

/// <summary>Validates every predicate on each independently replayed opening witness.</summary>
internal static class TransformationAggregateValidator
{
    internal readonly record struct Output(ModelKey? Card, bool? Rare, string Source = "");

    private sealed record RelicPoolCapture(TransformationRelicProjector? Projector);
    private static readonly ConditionalWeakTable<NeowEffectAuthoritySnapshot, RelicPoolCapture> RelicPoolCaptures = new();
    private readonly record struct AncientProjectionStatus(string? FailureCode = null, bool OutputCountUnknown = false,
        bool InsufficientMaterials = false);
    private static readonly (EventResultConditionKind Kind, Func<TransformationAggregateCondition, bool> Selected)[] EventSources =
    [
        (EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, c => c.MorphicGrove),
        (EventResultConditionKind.AromaOfChaosInitialBasicTransform, c => c.AromaOfChaos),
        (EventResultConditionKind.WhisperingHollowInitialBasicTransform, c => c.WhisperingHollow),
        (EventResultConditionKind.SymbioteInitialBasicTransform, c => c.Symbiote),
        (EventResultConditionKind.TrialNondescriptInitialBasicsContains, c => c.TrialNondescript)
    ];

    internal static Func<ulong, List<Output>?>? PrepareIndependentEventCheck(SearchModel request)
    {
        if (request.Evaluation.TransformationAggregate is not { UsesAncient: true, UsesEvents: true } condition ||
            request.Authority.ProfileId != RuntimeProfileId.Beta111 || request.Authority.PlayersCount != 1)
            return null;
        var requirements = condition.ResultRequirements.Where(requirement =>
            requirement.Sources.All(source => source.StartsWith("E.", StringComparison.Ordinal))).ToArray();
        if (requirements.Length == 0) return null;

        // Event transforms use event-local streams and captured card inputs. Their
        // scoped requirements apply to every opening and Ancient witness.
        return root =>
        {
            var events = ProjectEvents(request, condition, root, request.Authority);
            if (events is null) return null;
            foreach (var requirement in requirements)
                if (EvaluateRequirement(requirement, events, null) == SearchDisposition.NoMatch) return null;
            return events;
        };
    }

    private static List<Output>? ProjectEvents(SearchModel request, TransformationAggregateCondition condition,
        ulong root, RuntimeContextAuthoritySnapshot authority)
    {
        var events = new List<Output>();
        if (condition.EventScenario is { } scenario)
        {
            MorphicGrovePredictor.ValidateAuthority(request.Detection, authority, scenario);
            foreach (var (kind, selected) in EventSources)
            {
                if (!selected(condition)) continue;
                // The combined source capture carries two targets. A one-draw
                // event consumes only the first captured target.
                var entry = EventResultTransformSemantics.Entry(kind);
                string source = "E." + entry;
                int count = EventResultTransformSemantics.DrawCount(kind);
                if (scenario.Targets.Count < count)
                {
                    if (kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains &&
                        Beta111TrialTinkerProjector.TrialCase(root, authority.PlayerSlotIndex) != (int)TrialCaseTarget.Nondescript)
                        return null;
                    AddUnknownOutputs(events, count, source);
                    continue;
                }
                IReadOnlyList<MorphicGroveCard?>? projection = kind switch
                {
                    EventResultConditionKind.MorphicGroveGroupInitialBasicsContains =>
                        Beta111MorphicGroveProjector.Project(root, MorphicGroveCommitment.InitialBasics, scenario.Premises, scenario.Targets)
                            .Transforms.Select(transform => transform.RawReplacement).ToArray(),
                    EventResultConditionKind.AromaOfChaosInitialBasicTransform =>
                        [Beta111SingleBasicTransformProjector.Project(root, authority.PlayerSlotIndex,
                            SingleBasicTransformEvent.AromaOfChaos, scenario.Premises, scenario.Targets[0]).RawReplacement],
                    EventResultConditionKind.WhisperingHollowInitialBasicTransform =>
                        [Beta111SingleBasicTransformProjector.Project(root, authority.PlayerSlotIndex,
                            SingleBasicTransformEvent.WhisperingHollow, scenario.Premises, scenario.Targets[0]).RawReplacement],
                    _ => ProjectOtherEvent(root, kind, count, scenario)
                };
                if (projection is { Count: 0 }) return null;
                if (projection is null) AddUnknownOutputs(events, count, source);
                else events.AddRange(projection.Select(card => new Output(card?.CardKey, card is null ? null : card.Rarity == "Rare", source)));
            }
        }
        return events;
    }

    internal static SearchQueryEvaluation Evaluate(SearchModel request, ulong root, RuntimeContextAuthoritySnapshot authority,
        List<Output>? knownEvents = null)
    {
        if (request.Evaluation.TransformationAggregate is not { } condition) return SearchQueryEvaluation.Match();
        if (authority.ProfileId != RuntimeProfileId.Beta111 || authority.PlayersCount != 1)
            return SearchQueryEvaluation.Unsupported("TransformationAggregate.SinglePlayerBeta111Required");

        var events = knownEvents ?? ProjectEvents(request, condition, root, authority);
        if (events is null) return SearchQueryEvaluation.NoMatch("TransformationAggregate.TrialCaseNotNondescript");

        var profile = RuntimeProfileRegistry.Select(request.Detection);
        // Per-root reprojection replaces relic bag authority only. The request's
        // captured card inputs are immutable and can be reused across candidates.
        var relicPoolAuthority = request.Authority.EffectAuthority ?? authority.EffectAuthority;
        string? openingSource = condition.UsesNeow
            ? condition.SourceCounts().Keys.Single(source => source.StartsWith("N.", StringComparison.Ordinal))
            : null;
        int openingCount = openingSource is null ? 0 : condition.Opening switch
        {
            TransformationOpening.NewLeaf => 1,
            TransformationOpening.BonesLeafyNewLeaf => 3,
            _ => 2
        };
        int? priorBasics = condition.UsesAncient && !condition.PandoraCountOverride.HasValue
            ? CountEarlierEventMaterials(request, condition, authority.EffectAuthority) : 0;
        ModelKey? boundOpening = request.Evaluation.NeowRoute?.RouteRelicKey;
        ModelKey? opening = condition.UsesNeow ? condition.RequiredOpeningKey : condition.UsesAncient ? boundOpening : null;

        SearchQueryEvaluation Witness(OpeningTransformationContinuation? continuation, string route,
            IReadOnlyList<ModelKey>? pickup, IEnumerable<PredictedEffectGroup>? groups, string? pendingOpening = null)
        {
            var outputs = new List<Output>();
            if (openingSource is not null)
            {
                if (groups is null) AddUnknownOutputs(outputs, openingCount, openingSource);
                else outputs.AddRange(NeowOutputs(groups, openingCount, authority, openingSource));
            }
            outputs.AddRange(events);

            AncientProjectionStatus ancient = default;
            string? unboundedSource = null;
            if (condition.UsesAncient)
            {
                string source = condition.AncientSource == TransformationAncientSource.Astrolabe ? "A.ASTROLABE" : "A.PANDORAS_BOX";
                ancient = AddAncientOutputs(outputs, profile, root, authority, relicPoolAuthority,
                    condition, continuation, priorBasics, source);
                if (ancient.InsufficientMaterials)
                    return SearchQueryEvaluation.NoMatch("TransformationAggregate.AstrolabeBasicMaterialsInsufficient");
                if (ancient.OutputCountUnknown) unboundedSource = source;
            }

            SearchDisposition disposition = EvaluateOutputs(condition, outputs, unboundedSource);
            if (disposition == SearchDisposition.NoMatch)
                return SearchQueryEvaluation.NoMatch("TransformationAggregate.PredicateRejected");
            if (pendingOpening is not null || disposition == SearchDisposition.Unknown)
                return SearchQueryEvaluation.Unknown(pendingOpening ?? ancient.FailureCode ??
                    condition.EventScenario?.Premises.InitialBasicCaptureIssue ?? "TransformationAggregate.OutputUnknown");
            return Result(outputs, route, pickup);
        }

        if (opening is not { } selectedOpening)
            return Witness(OpeningTransformationContinuation.Initial(authority.EffectAuthority), "events", null, null);

        if (!authority.HasExactModernNeowIdentityInputs || authority.EffectAuthority is null)
            return Witness(null, selectedOpening.Serialized, null, null, "TransformationAggregate.NeowAuthorityUnavailable");
        var identity = ModernNeowIdentityPredictor.PredictModernCore(root, authority, profile, true);
        if (identity.UnknownEligibilityAffectedPool)
            return Witness(null, selectedOpening.Serialized, null, null, "TransformationAggregate.NeowEligibilityUnknown");
        if (!identity.RelicKeys.Contains(selectedOpening))
            return SearchQueryEvaluation.NoMatch("TransformationAggregate.OpeningNotOffered");
        var engine = new NeowEffectProjectionEngine(profile, "TransformationAggregate.Neow", root, authority,
            request.CompiledSearch.RequiresComplexBonesDeckInteractionEvaluation)
            { CaptureTransformationContinuation = condition.UsesAncient };
        var projectionOfOpening = engine.Project(selectedOpening);

        if (selectedOpening != BaseGameModelKeys.Relics.NeowsBones)
        {
            bool boundContinuation = boundOpening == selectedOpening &&
                (condition.UsesAncient || request.Evaluation.RequiresNormalCombatRewardDomain ||
                 TransformationAggregateCondition.HasBoundDirectRelicContinuation(request.CompiledSearch.NormalizedQuery));
            return Witness(projectionOfOpening.TransformationContinuation, selectedOpening.Serialized,
                boundContinuation ? [selectedOpening] : null, projectionOfOpening.EffectGroups);
        }

        if (projectionOfOpening.BonesOutcome is not { } bones)
            return Witness(null, selectedOpening.Serialized, null, null, "TransformationAggregate.BonesOutcomeUnknown");
        if (condition.IsBones &&
            (!bones.OfferedRelics.Contains(BaseGameModelKeys.Relics.LeafyPoultice) || !bones.OfferedRelics.Contains(condition.BonesCompanion)))
            return SearchQueryEvaluation.NoMatch("TransformationAggregate.BonesPairNotOffered");
        if (request.Evaluation.RequiredBonesCombination.Any(key => !bones.OfferedRelics.Contains(key)))
            return SearchQueryEvaluation.NoMatch("TransformationAggregate.BonesPairNotOffered");
        if (bones.OriginalRoutes.Count == 0)
            return Witness(null, selectedOpening.Serialized, null, null, "TransformationAggregate.BonesOutcomeUnknown");

        var matched = new List<SearchMatchEvidence>();
        var unresolved = new List<SearchRouteRejection>();
        var demandedOrder = request.Evaluation.RequiredBonesAcquisitionOrder;
        foreach (var route in bones.OriginalRoutes)
        {
            if (demandedOrder.Count > 0 && !demandedOrder.SequenceEqual(route.AcquisitionOrder)) continue;
            if (condition.IsBones && condition.PickupOrder != TransformationPickupOrder.Any && route.AcquisitionOrder[0] !=
                (condition.LeafyFirst ? BaseGameModelKeys.Relics.LeafyPoultice : condition.BonesCompanion)) continue;
            var scoped = condition.Opening == TransformationOpening.BonesLeafyOther
                ? route.RelicScopedResults.Where(result => result.SourceRelicKey == BaseGameModelKeys.Relics.LeafyPoultice)
                : route.RelicScopedResults;
            var verdict = Witness(route.TransformationContinuation, route.RouteId, route.AcquisitionOrder,
                scoped.SelectMany(result => result.EffectGroups));
            if (verdict.Disposition == SearchDisposition.Match) matched.AddRange(verdict.Evidence);
            if (verdict.Disposition == SearchDisposition.Unknown)
                unresolved.Add(new(route.RouteId, route.AcquisitionOrder, verdict.Disposition, verdict.FailureCode));
        }
        if (matched.Count > 0) return SearchQueryEvaluation.Match(matched) with { RouteRejections = unresolved };
        if (unresolved.Count > 0) return SearchQueryEvaluation.Unknown(unresolved[0].Reason) with { RouteRejections = unresolved };
        if (projectionOfOpening.Precision == PredictionPrecision.Unknown)
            return Witness(null, selectedOpening.Serialized, null, null, "TransformationAggregate.BonesOutcomeUnknown");
        return SearchQueryEvaluation.NoMatch("TransformationAggregate.AllRoutesRejected");
    }

    private static IReadOnlyList<MorphicGroveCard?>? ProjectOtherEvent(ulong root, EventResultConditionKind kind,
        int count, MorphicGroveScenario scenario)
    {
        var eventScenario = count == 1 && scenario.Targets.Count != 1
            ? new MorphicGroveScenario(scenario.Authority, scenario.Premises, [scenario.Targets[0]])
            : scenario;
        return EventResultTransformSemantics.Project(root, new(kind, default) { MorphicGroveScenario = eventScenario });
    }

    private static int? CountEarlierEventMaterials(SearchModel request, TransformationAggregateCondition condition,
        NeowEffectAuthoritySnapshot? authority)
    {
        var preceding = request.Evaluation.EventSequenceConditions
            .Where(queue => !queue.IsEmpty && queue.Act < condition.AncientAct)
            .SelectMany(queue => queue.Keys.All.Concat(queue.Keys.Any.Count == 1 ? queue.Keys.Any : []))
            .Select(key => key.Entry)
            .ToHashSet(StringComparer.Ordinal);
        if (preceding.Count == 0) return 0;

        int count = 0;
        foreach (var (kind, selected) in EventSources)
        {
            if (!preceding.Contains(EventResultTransformSemantics.Entry(kind))) continue;
            var scenario = selected(condition) ? condition.EventScenario :
                request.Evaluation.EventResultConditions.FirstOrDefault(result => result.Kind == kind)?.MorphicGroveScenario;
            if (scenario is null) continue;
            if (scenario.Targets.Count < EventResultTransformSemantics.DrawCount(kind)) return null;
            foreach (var target in scenario.Targets.Take(EventResultTransformSemantics.DrawCount(kind)))
            {
                if (condition.AncientSource == TransformationAncientSource.Astrolabe ||
                    target.Original.CardKey == authority?.CharacterStrikeKey || target.Original.CardKey == authority?.CharacterDefendKey)
                    count++;
                else if (authority is not { HasExactDeck: true }) return null;
                else if (authority.OrderedDeck!.Any(card => card.CardKey == target.Original.CardKey &&
                    card.IsBasic && card.CanRemove && (card.IsStrike || card.IsDefend))) count++;
            }
        }
        return count;
    }

    private static AncientProjectionStatus AddAncientOutputs(List<Output> outputs, IRuntimeProfile profile, ulong root,
        RuntimeContextAuthoritySnapshot authority, NeowEffectAuthoritySnapshot? relicPoolAuthority, TransformationAggregateCondition condition,
        OpeningTransformationContinuation? continuation, int? priorBasics, string source)
    {
        bool upgrade = condition.AncientSource == TransformationAncientSource.Astrolabe;
        int? available = upgrade ? continuation?.BasicCount : continuation?.BasicStrikeDefendCount;
        int? materials = available is { } basic && priorBasics is { } spent
            ? Math.Max(0, basic - spent) : null;
        if (upgrade && materials < TransformationRelicProjector.AstrolabeTargetCount)
            return new(InsufficientMaterials: true);
        int? count = upgrade ? TransformationRelicProjector.AstrolabeTargetCount : condition.PandoraCountOverride ?? materials;
        if (count > TransformationRelicProjector.ProjectionDrawBudget) return new("TransformationAggregate.AncientDrawBudgetExceeded", true);
        if (count is null) return new("TransformationAggregate.AncientMaterialCountUnknown", true);
        if (count == 0) return default;

        string? pending = authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113
            ? "TransformationAggregate.AncientStreamContinuationUnavailableForVersion" :
            upgrade && materials is null ? "TransformationAggregate.AstrolabeBasicMaterialsUnknown" :
            continuation?.StreamOffset is null ? "TransformationAggregate.AncientStreamOffsetUnknown" : null;
        var projector = pending is null && relicPoolAuthority is not null
            ? RelicPoolCaptures.GetValue(relicPoolAuthority, snapshot => new(TransformationRelicProjector.Capture(snapshot))).Projector
            : null;
        if (projector is null)
        {
            AddUnknownOutputs(outputs, count.Value, source);
            return new(pending ?? "TransformationAggregate.AncientTransformPoolUnknown");
        }
        foreach (var card in projector.Project(profile, root, authority.PlayerSlotIndex, authority.VersionResolution.SemanticProfileId,
                     continuation!.StreamOffset!.Value, count.Value, upgrade))
            outputs.Add(new(card.CardKey, card.Rarity == EffectCardRarity.Rare, source));
        return default;
    }

    private static void AddUnknownOutputs(List<Output> outputs, int count, string source)
    {
        for (int i = 0; i < count; i++) outputs.Add(new(null, null, source));
    }

    private static IEnumerable<Output> NeowOutputs(IEnumerable<PredictedEffectGroup> groups, int expected,
        RuntimeContextAuthoritySnapshot authority, string source)
    {
        var transforms = groups.SelectMany(group => group.OrderedItems).Where(item => item.Kind == PredictedEffectKind.TransformCard).ToArray();
        if (transforms.Length > expected) throw new InvalidOperationException("TransformationAggregate.UnexpectedNeowOutputMultiplicity");
        foreach (var item in transforms)
        {
            ModelKey? key = item.Precision == PredictionPrecision.Exact ? item.TargetKey : null;
            var card = key is null ? null : authority.EffectAuthority?.TransformPool?.FirstOrDefault(value => value.CardKey == key);
            yield return new(key, card is null ? null : card.Rarity == EffectCardRarity.Rare, source);
        }
        for (int i = transforms.Length; i < expected; i++) yield return new(null, null, source);
    }

    internal static SearchDisposition EvaluateOutputs(TransformationAggregateCondition condition, IReadOnlyList<Output> outputs) =>
        EvaluateOutputs(condition, outputs, null);

    private static SearchDisposition EvaluateOutputs(TransformationAggregateCondition condition, IReadOnlyList<Output> outputs,
        string? unboundedSource)
    {
        if (condition.ResultRequirements.Count == 0)
        {
            if (!condition.UsesAncient && outputs.Count != condition.OpportunityCount)
                throw new InvalidOperationException("TransformationAggregate.OutputCountMismatch");
            return EvaluatePredicate(condition.Predicate, condition.MinimumRareCount, condition.TargetMultiset, outputs,
                unboundedSource is not null);
        }

        var result = SearchDisposition.Match;
        foreach (var requirement in condition.ResultRequirements)
        {
            var disposition = EvaluateRequirement(requirement, outputs, unboundedSource);
            if (disposition == SearchDisposition.NoMatch) return disposition;
            if (disposition == SearchDisposition.Unknown) result = disposition;
        }
        return result;
    }

    private static SearchDisposition EvaluateRequirement(TransformationResultRequirement requirement,
        IReadOnlyList<Output> outputs, string? unboundedSource) =>
        EvaluatePredicate(requirement.Predicate, requirement.MinimumRareCount, requirement.TargetMultiset,
            outputs.Where(output => requirement.Sources.Contains(output.Source)).ToArray(),
            unboundedSource is not null && requirement.Sources.Contains(unboundedSource));

    private static SearchDisposition EvaluatePredicate(TransformationAggregatePredicate predicate, int minimumRare,
        IReadOnlyList<ModelKey> targets, IReadOnlyList<Output> outputs, bool outputCountUnknown)
    {
        if (predicate == TransformationAggregatePredicate.ContainsMultisetAndRemainingRare)
            return EvaluateRareRemainder(targets, outputs, outputCountUnknown);

        int rare = 0, unknownRarity = 0, unknownIdentity = 0;
        var needed = targets.GroupBy(key => key).ToDictionary(group => group.Key, group => group.Count());
        int missing = targets.Count;
        foreach (var output in outputs)
        {
            if (output.Rare == true) rare++;
            if (output.Rare is null) unknownRarity++;
            if (output.Card is not { } card) unknownIdentity++;
            else if (needed.TryGetValue(card, out int count) && count > 0)
            {
                needed[card] = count - 1;
                missing--;
            }
        }
        if (outputCountUnknown)
            return rare >= minimumRare && missing == 0 ? SearchDisposition.Match : SearchDisposition.Unknown;
        if (rare + unknownRarity < minimumRare || missing > unknownIdentity)
            return SearchDisposition.NoMatch;
        return rare >= minimumRare && missing == 0 ? SearchDisposition.Match : SearchDisposition.Unknown;
    }

    private static SearchDisposition EvaluateRareRemainder(IReadOnlyList<ModelKey> targets, IReadOnlyList<Output> outputs,
        bool outputCountUnknown)
    {
        // Every known non-Rare must occupy a matching target slot. The available
        // copies of an identity can be assigned interchangeably. After the forced
        // known assignments, unknown identities share the leftover slots.
        var targetCounts = targets.GroupBy(key => key).ToDictionary(group => group.Key, group => group.Count());
        var available = new Dictionary<ModelKey, int>();
        var required = new Dictionary<ModelKey, int>();
        var requiredForProof = new Dictionary<ModelKey, int>();
        int unknown = 0, forcedUnknown = 0;
        bool allUnknownCanProveRemainder = true;
        foreach (var output in outputs)
        {
            if (output.Card is not { } card)
            {
                unknown++;
                if (output.Rare == false) forcedUnknown++;
                if (output.Rare != true) allUnknownCanProveRemainder = false;
                continue;
            }
            available[card] = available.GetValueOrDefault(card) + 1;
            if (output.Rare == false) required[card] = required.GetValueOrDefault(card) + 1;
            if (output.Rare != true) requiredForProof[card] = requiredForProof.GetValueOrDefault(card) + 1;
        }

        int missing = targetCounts.Sum(pair => Math.Max(0, pair.Value - available.GetValueOrDefault(pair.Key)));
        if (required.Any(pair => pair.Value > targetCounts.GetValueOrDefault(pair.Key)) ||
            forcedUnknown > targets.Count - required.Values.Sum())
            return SearchDisposition.NoMatch;
        if (outputCountUnknown) return SearchDisposition.Unknown;
        if (missing > unknown) return SearchDisposition.NoMatch;
        return missing == 0 && allUnknownCanProveRemainder &&
            requiredForProof.All(pair => pair.Value <= targetCounts.GetValueOrDefault(pair.Key))
            ? SearchDisposition.Match : SearchDisposition.Unknown;
    }

    private static SearchQueryEvaluation Result(IReadOnlyList<Output> outputs, string route, IReadOnlyList<ModelKey>? pickup) =>
        SearchQueryEvaluation.Match([new SearchMatchEvidence("TransformationAggregateMatched", null,
            "transformation-aggregate:" + route,
            AcquisitionOrder: pickup,
            EvidenceCode: new("TransformationAggregate.ConditionalRawOutputs.v1"),
            ChoicePolicyId: "AuthoredInitialBasicsTransformations.v1",
            OutcomeId: string.Join(',', outputs.Select(output => output.Card?.Serialized ?? "unknown")),
            ProfileId: RuntimeProfileId.Beta111, StreamDomain: "TransformationAggregate",
            ConditionId: "transformation-aggregate")]);
}
