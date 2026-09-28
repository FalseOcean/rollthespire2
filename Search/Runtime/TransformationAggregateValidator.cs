using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Runtime;

/// <summary>Search's aggregate validator consumes authoritative domain projections.
/// It never implements N identity, pool construction, or event RNG rules.</summary>
internal static class TransformationAggregateValidator
{
    internal readonly record struct Output(ModelKey? Card, bool? Rare);

    internal static SearchQueryEvaluation Evaluate(ExactSearchExecutionRequest request, ulong root,
        RuntimeContextAuthoritySnapshot authority)
    {
        if (request.Evaluation.TransformationAggregate is not { } c) return SearchQueryEvaluation.Match();
        if (authority.ProfileId != RuntimeProfileId.Beta111 || authority.PlayersCount != 1)
            return SearchQueryEvaluation.Unsupported("TransformationAggregate.SinglePlayerBeta111Required");
        var events = new List<Output>();
        if (c.EventScenario is { } scenario)
        {
            MorphicGrovePredictor.ValidateAuthority(request.Detection, authority, scenario);
            if (c.MorphicGrove)
            {
                var projection = Beta111MorphicGroveProjector.Project(root, MorphicGroveCommitment.InitialBasics,
                    scenario.Premises, scenario.Targets);
                events.AddRange(projection.Transforms.Select(t => EventOutput(t.RawReplacement)));
            }
            if (c.AromaOfChaos) events.Add(EventOutput(Beta111SingleBasicTransformProjector.Project(root,
                authority.PlayerSlotIndex, SingleBasicTransformEvent.AromaOfChaos, scenario.Premises, scenario.Targets[0]).RawReplacement));
            if (c.WhisperingHollow) events.Add(EventOutput(Beta111SingleBasicTransformProjector.Project(root,
                authority.PlayerSlotIndex, SingleBasicTransformEvent.WhisperingHollow, scenario.Premises, scenario.Targets[0]).RawReplacement));
        }
        if (c.EventScenario is { } extraScenario)
        {
            foreach (var kind in new[] { EventResultConditionKind.SymbioteInitialBasicTransform, EventResultConditionKind.TrialNondescriptInitialBasicsContains })
            {
                if (kind == EventResultConditionKind.SymbioteInitialBasicTransform ? !c.Symbiote : !c.TrialNondescript) continue;
                var eventScenario = EventResultTransformSemantics.DrawCount(kind) == 1
                    ? new MorphicGroveScenario(extraScenario.Authority, extraScenario.Premises, [extraScenario.Targets[0]]) : extraScenario;
                var projection = EventResultTransformSemantics.Project(root, new(kind, default) { MorphicGroveScenario = eventScenario });
                if (projection is null) return SearchQueryEvaluation.Unknown("TransformationAggregate.EventAuthorityUnavailable");
                if (projection.Count == 0) return SearchQueryEvaluation.NoMatch("TransformationAggregate.TrialCaseNotNondescript");
                events.AddRange(projection.Select(EventOutput));
            }
        }
        if (!c.UsesNeow) return Result(EvaluateOutputs(c, events), events, "events");
        if (!authority.HasExactModernNeowIdentityInputs || authority.EffectAuthority is null)
            return SearchQueryEvaluation.Unknown("TransformationAggregate.NeowAuthorityUnavailable");
        var profile = RuntimeProfileRegistry.Select(request.Detection);
        ModelKey selected = c.Opening switch {
            TransformationOpening.LeafyPoultice => BaseGameModelKeys.Relics.LeafyPoultice,
            TransformationOpening.NewLeaf => BaseGameModelKeys.Relics.NewLeaf,
            _ => BaseGameModelKeys.Relics.NeowsBones };
        var identity = ModernNeowIdentityPredictor.PredictModernCore(root, authority, profile, true);
        if (identity.UnknownEligibilityAffectedPool) return SearchQueryEvaluation.Unknown("TransformationAggregate.NeowEligibilityUnknown");
        if (!identity.RelicKeys.Contains(selected)) return SearchQueryEvaluation.NoMatch("TransformationAggregate.OpeningNotOffered");
        var n = new NeowEffectProjectionEngine(profile, "TransformationAggregate.Neow", root, authority).Project(selected);
        if (!c.IsBones)
        {
            var outputs = NeowOutputs(n.EffectGroups, c.Opening == TransformationOpening.LeafyPoultice ? 2 : 1, authority).Concat(events).ToArray();
            return Result(EvaluateOutputs(c, outputs), outputs, selected.Serialized);
        }
        if (n.BonesOutcome is not { } bones) return SearchQueryEvaluation.Unknown("TransformationAggregate.BonesOutcomeUnknown");
        if (!bones.OfferedRelics.Contains(BaseGameModelKeys.Relics.LeafyPoultice) || !bones.OfferedRelics.Contains(c.BonesCompanion))
            return SearchQueryEvaluation.NoMatch("TransformationAggregate.BonesPairNotOffered");
        bool unknown = bones.OriginalRoutes.Count == 0;
        var matched = new List<SearchMatchEvidence>();
        foreach (var route in bones.OriginalRoutes)
        {
            if (c.PickupOrder != TransformationPickupOrder.Any && route.AcquisitionOrder[0] !=
                (c.LeafyFirst ? BaseGameModelKeys.Relics.LeafyPoultice : c.BonesCompanion)) continue;
            var scoped = c.Opening == TransformationOpening.BonesLeafyOther ? route.RelicScopedResults.Where(r=>r.SourceRelicKey==BaseGameModelKeys.Relics.LeafyPoultice) : route.RelicScopedResults;
            var outputs = NeowOutputs(scoped.SelectMany(r => r.EffectGroups), c.Opening==TransformationOpening.BonesLeafyOther ? 2 : 3, authority).Concat(events).ToArray();
            var verdict = EvaluateOutputs(c, outputs);
            if (verdict == SearchDisposition.Match) matched.AddRange(Result(verdict, outputs, route.RouteId, route.AcquisitionOrder).Evidence);
            unknown |= verdict == SearchDisposition.Unknown;
        }
        if (matched.Count>0) return SearchQueryEvaluation.Match(matched);
        return unknown ? SearchQueryEvaluation.Unknown("TransformationAggregate.RouteOutputUnknown") :
            SearchQueryEvaluation.NoMatch("TransformationAggregate.AllRoutesRejected");
    }

    private static Output EventOutput(MorphicGroveCard? card) => new(card?.CardKey, card is null ? null : card.Rarity == "Rare");
    private static IEnumerable<Output> NeowOutputs(IEnumerable<PredictedEffectGroup> groups, int expected, RuntimeContextAuthoritySnapshot a)
    {
        var transforms = groups.SelectMany(g => g.OrderedItems).Where(i => i.Kind == PredictedEffectKind.TransformCard).ToArray();
        if (transforms.Length > expected) throw new InvalidOperationException("TransformationAggregate.UnexpectedNeowOutputMultiplicity");
        foreach (var item in transforms)
        {
            ModelKey? key = item.Precision == PredictionPrecision.Exact ? item.TargetKey : null;
            var card = key is null ? null : a.EffectAuthority?.TransformPool?.FirstOrDefault(x => x.CardKey == key);
            yield return new(key, card is null ? null : card.Rarity == EffectCardRarity.Rare);
        }
        for (int i = transforms.Length; i < expected; i++) yield return new(null, null);
    }

    internal static SearchDisposition EvaluateOutputs(TransformationAggregateCondition c, IReadOnlyList<Output> outputs)
    {
        if (outputs.Count != c.OpportunityCount) throw new InvalidOperationException("TransformationAggregate.OutputCountMismatch");
        if (c.Predicate == TransformationAggregatePredicate.RareCountAtLeast)
        {
            int rare = outputs.Count(x => x.Rare == true), unknown = outputs.Count(x => x.Rare is null);
            return rare >= c.MinimumRareCount ? SearchDisposition.Match : rare + unknown < c.MinimumRareCount
                ? SearchDisposition.NoMatch : SearchDisposition.Unknown;
        }
        int missing = c.TargetMultiset.GroupBy(k => k).Sum(g => Math.Max(0, g.Count() - outputs.Count(o => o.Card == g.Key)));
        return missing == 0 ? SearchDisposition.Match : missing > outputs.Count(o => o.Card is null)
            ? SearchDisposition.NoMatch : SearchDisposition.Unknown;
    }

    private static SearchQueryEvaluation Result(SearchDisposition disposition, IReadOnlyList<Output> outputs, string route,
        IReadOnlyList<ModelKey>? pickup = null) => disposition switch {
        SearchDisposition.Match => SearchQueryEvaluation.Match([new SearchMatchEvidence("TransformationAggregateMatched", null,
            "transformation-aggregate:" + route,
            AcquisitionOrder: pickup,
            EvidenceCode: new("TransformationAggregate.ConditionalRawOutputs.v1"),
            ChoicePolicyId: "AuthoredInitialBasicsTransformations.v1",
            OutcomeId: string.Join(',', outputs.Select(o => o.Card?.Serialized ?? "unknown")),
            ProfileId: RuntimeProfileId.Beta111, StreamDomain: "TransformationAggregate",
            ConditionId: "transformation-aggregate")]),
        SearchDisposition.NoMatch => SearchQueryEvaluation.NoMatch("TransformationAggregate.PredicateRejected"),
        _ => SearchQueryEvaluation.Unknown("TransformationAggregate.OutputUnknown") };
}
