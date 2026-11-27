using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// Captures the numerical T obligation. N offers, W occurrence, A menus and
// whole-query confirmation keep their existing owners.
internal sealed partial class ScopedTransformationGeneralPlan
{
    private readonly record struct Card(ModelKey Key, bool? Rare);
    private sealed record Block(string? Source, Card[] Pool, int Count);
    private sealed record Stream(ulong Offset, int PrefixRange, int RequiredPlusOne, List<Block> Blocks);
    private readonly List<Stream> _streams = [];
    private readonly List<EventResultConditionKind> _events = [];
    private readonly SearchModel _model;
    private readonly TransformationAggregateCondition _condition;
    private readonly TransformationResultRequirement[] _requirements;
    private readonly HashSet<string> _observed;
    private readonly string? _openingSource;
    private readonly string _ancientSource;
    private readonly ModelKey? _opening;
    private bool _projectOpening;
    private Dictionary<ModelKey, bool?>? _openingRarity;
    private TransformationRelicProjector? _ancient;

    private ScopedTransformationGeneralPlan(SearchModel model, TransformationAggregateCondition condition)
    {
        _model = model;
        _condition = condition;
        _requirements = condition.ResultRequirements.Count == 0 ?
            [new(condition.SourceCounts().Keys.ToArray(), condition.Predicate, condition.MinimumRareCount, condition.TargetMultiset)] :
            condition.ResultRequirements.ToArray();
        _observed = _requirements.SelectMany(r => r.Sources).ToHashSet(StringComparer.Ordinal);
        _openingSource = _observed.FirstOrDefault(s => s.StartsWith("N.", StringComparison.Ordinal));
        _ancientSource = "A." + condition.RequiredAncientKey.Entry;
        _opening = condition.UsesNeow ? condition.RequiredOpeningKey : model.Evaluation.NeowRoute?.RouteRelicKey;
    }

    internal static ScopedTransformationGeneralPlan? TryCreate(SearchModel model, out string reason)
    {
        if (model.Evaluation.TransformationAggregate is not { UsesExtendedRequirements: true } condition)
        { reason = "T.NoScopedRequirement"; return null; }
        if (model.Authority.PlayersCount != 1 || model.Authority.PlayerSlotIndex != 0 ||
            model.Authority.ProfileId != RuntimeProfileId.Beta111 ||
            model.Authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113)
        { reason = "T.PersonalStreamProfileUnsupported"; return null; }
        var plan = new ScopedTransformationGeneralPlan(model, condition);
        reason = plan.Capture() ?? plan.CompileCounters() ?? "";
        return reason.Length == 0 ? plan : null;
    }

    private string? Capture()
    {
        bool observeAncient = _observed.Contains(_ancientSource) && _condition.AncientOpportunityCount > 0;
        var effects = _model.Authority.EffectAuthority;
        if (_openingSource is not null || observeAncient)
        {
            var personal = new Stream(Beta111Profile.Instance.DeriveNamedStreamSeed(0, "transformations"), 0, 0, []);
            if (_opening == BaseGameModelKeys.Relics.NeowsBones)
                return "T.BonesPersonalStreamRequiresPickupWitness";
            if (!ScopedTransformationPools.StartsAncientAtInitialCursor(_model, _opening))
            {
                if (effects is null || effects.CapturedProfileId != _model.Authority.ProfileId)
                    return "T.OpeningTransformAuthorityMissing";
                if (ScopedTransformationPools.Opening(_model, _opening, effects) is not { } pools)
                    return "T.OpeningTransformCursorOrPoolUnknown";
                _projectOpening = true;
                if (_openingSource is not null)
                    _openingRarity = effects.TransformPool?.GroupBy(card => card.CardKey)
                        .ToDictionary(group => group.Key, group => (bool?)(group.First().Rarity == EffectCardRarity.Rare));
                foreach (var pool in pools)
                    personal.Blocks.Add(new(_openingSource,
                        pool.Select(key => new Card(key, _openingRarity?.GetValueOrDefault(key))).ToArray(), 1));
            }
            if (observeAncient)
            {
                if (_condition.AncientOpportunityCount > TransformationRelicProjector.ProjectionDrawBudget)
                    return "T.AncientDrawBudgetExceeded";
                _ancient = TransformationRelicProjector.Capture(effects);
                if (_ancient is null) return "T.AncientOrderedCardPoolUnknown";
                personal.Blocks.Add(new(_ancientSource, _ancient.OrderedCandidates.ToArray()
                    .Select(card => new Card(card.CardKey, card.Rarity == EffectCardRarity.Rare)).ToArray(),
                    _condition.AncientOpportunityCount));
            }
            if (personal.Blocks.Count > 0) _streams.Add(personal);
        }
        foreach (var kind in EventResultTransformSemantics.Selected(_condition))
        {
            string entry = EventResultTransformSemantics.Entry(kind);
            string source = "E." + entry;
            bool trial = kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains;
            bool observe = _observed.Contains(source);
            if (!trial && !observe) continue;
            if (_condition.EventScenario is not { } scenario) return source + ".EventCaptureMissing";
            MorphicGrovePredictor.ValidateAuthority(_model.Detection, _model.Authority, scenario);
            bool grove = kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains;
            int range = trial ? 3 : kind == EventResultConditionKind.WhisperingHollowInitialBasicTransform ? 19 : 0;
            var stream = new Stream(Beta109WorldRng.DeriveEventLocalSeed(0, 0, grove, entry), range,
                trial ? (int)TrialCaseTarget.Nondescript + 1 : 0, []);
            if (observe)
            {
                if (ScopedTransformationPools.Event(kind, scenario) is not { } pools) return source + ".EventTargetsOrOrderedPoolsUnknown";
                foreach (var pool in pools)
                    stream.Blocks.Add(new(source, pool.Select(c => new Card(c.CardKey, c.Rarity == "Rare")).ToArray(), 1));
                _events.Add(kind);
            }
            if (trial) _streams.Insert(0, stream);
            else _streams.Add(stream);
        }
        return null;
    }

    internal bool Matches(ulong root)
    {
        if (_condition.TrialNondescript &&
            Beta111TrialTinkerProjector.TrialCase(root, 0) != (int)TrialCaseTarget.Nondescript) return false;
        var outputs = new List<TransformationAggregateValidator.Output>();
        int offset = 0;
        if (_projectOpening)
        {
            var engine = new NeowEffectProjectionEngine(Beta111Profile.Instance, "ScopedTransformJoint", root, _model.Authority)
                { CaptureTransformationContinuation = true };
            var projection = engine.Project(_opening!.Value);
            offset = projection.TransformationContinuation?.StreamOffset ??
                throw new InvalidOperationException("ScopedTransformJoint.OpeningCursorUnknown");
            if (_openingSource is not null)
                foreach (var item in projection.EffectGroups.SelectMany(g => g.OrderedItems))
                    if (item.Kind == PredictedEffectKind.TransformCard && item.TargetKey is { } key)
                        outputs.Add(new(key, _openingRarity?.GetValueOrDefault(key), _openingSource));
        }
        if (_ancient is not null)
            foreach (var card in _ancient.Project(Beta111Profile.Instance, root, 0, RngSemanticProfileId.Modern113,
                offset, _condition.AncientOpportunityCount, _condition.AncientSource == TransformationAncientSource.Astrolabe))
                outputs.Add(new(card.CardKey, card.Rarity == EffectCardRarity.Rare, _ancientSource));
        foreach (var kind in _events)
        {
            var cards = TransformationAggregateValidator.ProjectEvent(root, kind, _condition.EventScenario!) ??
                throw new InvalidOperationException("ScopedTransformJoint.EventPoolUnknown");
            if (cards.Count == 0) return false;
            string source = "E." + EventResultTransformSemantics.Entry(kind);
            outputs.AddRange(cards.Select(c => new TransformationAggregateValidator.Output(c?.CardKey,
                c is null ? null : c.Rarity == "Rare", source)));
        }
        return TransformationAggregateValidator.EvaluateOutputs(_condition, outputs) switch
        {
            SearchDisposition.Match => true, SearchDisposition.NoMatch => false,
            _ => throw new InvalidOperationException("ScopedTransformJoint.IncompleteCapturedOutcome")
        };
    }
}
