using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// A draw always advances its own stream. Only authored contributing sources add
// to the counter. Occurrence, offer and common-route witnesses belong to Exact.
internal sealed class ScopedTransformationCountPlan
{
    private sealed record DrawBlock(ModelKey[] Pool, int Count, bool Contributes);
    private sealed record DrawStream(ulong SeedOffset, List<DrawBlock> Blocks, int PrefixRange = 0,
        int? RequiredPrefixIndex = null);

    private readonly SearchModel _model;
    private readonly TransformationResultRequirement _requirement;
    private readonly ModelKey _target;
    private readonly TransformationRelicProjector? _ancient;
    private readonly ModelKey? _opening;
    private readonly string? _openingSource;
    private readonly List<DrawStream> _streams = [];
    private readonly List<EventResultConditionKind> _countedEvents = [];
    private bool _trialGateOnly;
    internal bool HasEventPrefix => _streams.Any(stream => stream.PrefixRange != 0);

    private ScopedTransformationCountPlan(SearchModel model, TransformationResultRequirement requirement,
        ModelKey? opening, string? openingSource, TransformationRelicProjector? ancient)
    {
        _model = model;
        _requirement = requirement;
        _target = requirement.TargetMultiset[0];
        _opening = opening;
        _openingSource = openingSource;
        _ancient = ancient;
    }

    internal static ScopedTransformationCountPlan? TryCreate(SearchModel model)
    {
        var authority = model.Authority;
        if (model.Evaluation.TransformationAggregate is not { UsesExtendedRequirements: true, IsBones: false } condition ||
            authority.PlayersCount != 1 || authority.PlayerSlotIndex != 0 ||
            authority.ProfileId != RuntimeProfileId.Beta111 ||
            authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113 ||
            condition.AncientOpportunityCount is < 0 or > TransformationRelicProjector.ProjectionDrawBudget)
            return null;

        var sources = condition.SourceCounts().Keys.ToArray();
        var requirement = condition.ResultRequirements.Count switch {
            0 => new TransformationResultRequirement(sources, condition.Predicate, condition.MinimumRareCount, condition.TargetMultiset),
            1 => condition.ResultRequirements[0], _ => null
        };
        if (requirement is not { Predicate: TransformationAggregatePredicate.ContainsMultiset, MinimumRareCount: 0 } ||
            requirement.Sources.Count == 0 || requirement.Sources.Any(source => !sources.Contains(source)) ||
            requirement.TargetMultiset.Count == 0 || requirement.TargetMultiset.Any(card => card != requirement.TargetMultiset[0]))
            return null;

        var opening = condition.UsesNeow ? condition.RequiredOpeningKey : model.Evaluation.NeowRoute?.RouteRelicKey;
        string? openingSource = sources.FirstOrDefault(source => source.StartsWith("N.", StringComparison.Ordinal));
        var effects = authority.EffectAuthority;
        bool countAncient = condition.UsesAncient && requirement.Sources.Contains("A." + condition.RequiredAncientKey.Entry) && condition.AncientOpportunityCount != 0;
        var ancient = countAncient ? TransformationRelicProjector.Capture(effects) : null;
        if (countAncient && ancient is null) return null;
        var plan = new ScopedTransformationCountPlan(model, requirement, opening, openingSource, ancient);
        if (countAncient || openingSource is not null && requirement.Sources.Contains(openingSource))
        {
            var personal = new DrawStream(Beta111Profile.Instance.DeriveNamedStreamSeed(0UL, "transformations"), []);
            if (opening.HasValue && (effects is null ||
                effects.CapturedProfileId != authority.ProfileId || !plan.AddOpening(personal, effects))) return null;
            // The query specifies three draws for Astrolabe and the authored
            // quantity for Pandora. Astrolabe upgrades after choosing each identity.
            if (ancient is not null) personal.Blocks.Add(new(ancient.OrderedCandidates.ToArray()
                .Select(card => card.CardKey).ToArray(), condition.AncientOpportunityCount, true));
            plan._streams.Add(personal);
        }
        foreach (var kind in EventResultTransformSemantics.Selected(condition))
        {
            bool counted = requirement.Sources.Contains("E." + EventResultTransformSemantics.Entry(kind));
            if ((counted || kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains) &&
                !plan.AddEvent(kind, counted, condition.EventScenario)) return null;
        }
        return plan;
    }

    private bool AddOpening(DrawStream stream, NeowEffectAuthoritySnapshot effects)
    {
        if (_opening == BaseGameModelKeys.Relics.LargeCapsule)
        {
            // The forced pair adds materials. Nested Whetstone and War Paint
            // upgrade via non_combat_generation, leaving transformations at zero.
            return OpeningTransformationContinuation.HasNativeCapsuleCursor(_model.Authority);
        }
        if (_opening.HasValue && NeowEffectCoverageRegistry.TryGet(_opening.Value, out var entry) &&
            entry.Family == NeowEffectFamily.DeterministicImmediate) return true;
        if (_opening != BaseGameModelKeys.Relics.NewLeaf && _opening != BaseGameModelKeys.Relics.LeafyPoultice) return false;
        var catalog = Beta110FastEffectCatalogCompiler.Compile(effects, []);
        ushort[][] pools;
        if (_opening == BaseGameModelKeys.Relics.NewLeaf && catalog.NewLeafTransformAuthorityExact)
            pools = [catalog.NewLeafTransformPool];
        else if (_opening == BaseGameModelKeys.Relics.LeafyPoultice && catalog.LeafyTransformAuthorityExact)
            pools = [catalog.LeafyStrikeTransformPool, catalog.LeafyDefendTransformPool];
        else return false;
        bool contributes = _openingSource is not null && _requirement.Sources.Contains(_openingSource);
        foreach (var pool in pools)
            stream.Blocks.Add(new(pool.Select(id => catalog.DenseKeys[id]).ToArray(), 1, contributes));
        return true;
    }

    private bool AddEvent(EventResultConditionKind kind, bool counted, MorphicGroveScenario? scenario)
    {
        if (scenario is null) return false;
        MorphicGrovePredictor.ValidateAuthority(_model.Detection, _model.Authority, scenario);
        bool trial = kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains;
        bool grove = kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains;
        string entry = EventResultTransformSemantics.Entry(kind);
        int prefixRange = trial ? 3 : kind == EventResultConditionKind.WhisperingHollowInitialBasicTransform ? 19 : 0;
        var stream = new DrawStream(Beta109WorldRng.DeriveEventLocalSeed(0UL, 0, grove, entry), [],
            prefixRange, trial ? (int)TrialCaseTarget.Nondescript : null);
        if (counted)
        {
            if (!scenario.Premises.VanillaLocalDependenciesBound || scenario.Premises.EntryFloor < 0 ||
                string.IsNullOrWhiteSpace(scenario.Premises.EventOccurrenceBasis)) return false;
            if ((trial || kind == EventResultConditionKind.SymbioteInitialBasicTransform) &&
                EventResultTransformSemantics.ClosedPool(EventResultTransformSemantics.ForAggregate(kind, scenario)) is null)
                return false;
            int draws = EventResultTransformSemantics.DrawCount(kind);
            if (scenario.Targets.Count < draws || grove && scenario.Targets.Count != draws) return false;
            var basics = MorphicGroveCommitment.InitialBasics;
            for (int i = 0; i < draws; i++)
            {
                // Group resolves each committed instance by ID. Single-target events take the
                // first captured target; Trial's closed contract requires identical ordered pools.
                string id = i == 0 ? basics.FirstInstanceId : basics.SecondInstanceId;
                var target = grove ? scenario.Targets.FirstOrDefault(t => t.InstanceId == id) : scenario.Targets[0];
                if (target is not { Original.CanTransform: true, Original.CardType: not "Quest", OrderedSourceCandidates.Count: > 0 })
                    return false;
                stream.Blocks.Add(new(target.OrderedSourceCandidates.Select(card => card.CardKey).ToArray(), 1, true));
            }
            _countedEvents.Add(kind);
        }
        if (trial)
        {
            // A selected Trial commits to Nondescript even if another source supplies
            // every requested card. Run this case gate before any successful count exit.
            _streams.Insert(0, stream);
            _trialGateOnly = !counted;
        }
        else _streams.Add(stream);
        return true;
    }

    // Compare with the gameplay projectors, including the actual offset left
    // by the direct transformation opening. Offer membership is a separate fact.
    internal bool Matches(ulong root)
    {
        var condition = _model.Evaluation.TransformationAggregate!;
        int supplied = 0;
        int offset = 0;
        if ((_ancient is not null || _openingSource is not null && _requirement.Sources.Contains(_openingSource)) &&
            (_opening == BaseGameModelKeys.Relics.NewLeaf || _opening == BaseGameModelKeys.Relics.LeafyPoultice))
        {
            var engine = new NeowEffectProjectionEngine(Beta111Profile.Instance, "ScopedTransformCount", root, _model.Authority)
                { CaptureTransformationContinuation = true };
            var opening = engine.Project(_opening.Value);
            offset = opening.TransformationContinuation?.StreamOffset ??
                throw new InvalidOperationException("ScopedTransformCount.OpeningCursorUnknown");
            if (_openingSource is not null && _requirement.Sources.Contains(_openingSource))
                supplied += opening.EffectGroups.SelectMany(group => group.OrderedItems).Count(item =>
                    item.Kind == PredictedEffectKind.TransformCard && item.TargetKey == _target);
        }
        if (_ancient is not null)
            supplied += _ancient.Project(Beta111Profile.Instance, root, 0, RngSemanticProfileId.Modern113, offset,
                condition.AncientOpportunityCount, condition.AncientSource == TransformationAncientSource.Astrolabe)
                .Count(card => card.CardKey == _target);
        if (_trialGateOnly && Beta111TrialTinkerProjector.TrialCase(root, 0) != (int)TrialCaseTarget.Nondescript) return false;
        foreach (var kind in _countedEvents)
        {
            var cards = Runtime.TransformationAggregateValidator.ProjectEvent(root, kind, condition.EventScenario!)
                ?? throw new InvalidOperationException("ScopedTransformCount.MissingClosedEventPool");
            if (cards.Count == 0) return false;
            supplied += cards.Count(card => card?.CardKey == _target);
        }
        return supplied >= _requirement.TargetMultiset.Count;
    }

    internal readonly record struct WorkEstimate(double Survival, double Draws, double Streams)
    {
        internal const string Evidence = "ScopedStreamCount.OrderedPools.IndependentIndexApproximation.StoppingTime";
    }

    // Planning only. Pool slots retain multiplicity. Estimate independent draw
    // indices; execution always derives every stream from the actual same root.
    internal WorkEstimate? EstimateWork()
    {
        var blocks = _streams.SelectMany(stream => stream.Blocks).ToArray();
        int remaining = blocks.Where(block => block.Contributes).Sum(block => block.Count);
        int minimum = _requirement.TargetMultiset.Count;
        if (minimum > remaining) return new(0, 0, _streams.Count == 0 ? 0 : 1);
        if ((long)minimum * blocks.Sum(block => block.Count) > 32768) return null;
        var pending = new double[minimum + 1];
        pending[minimum] = 1;
        double accepted = 0, expectedDraws = 0, expectedStreams = 0;
        int possible = 0;
        foreach (var stream in _streams)
        {
            double reach = pending.Sum();
            expectedStreams += reach;
            if (stream.PrefixRange != 0) expectedDraws += reach;
            if (stream.RequiredPrefixIndex.HasValue)
                for (int k = 1; k <= minimum; k++) pending[k] /= stream.PrefixRange;
            foreach (var block in stream.Blocks)
            {
                int hits = block.Contributes ? block.Pool.Count(card => card == _target) : 0;
                double chance = (double)hits / block.Pool.Length;
                if (hits != 0) possible += block.Count;
                for (int draw = 0; draw < block.Count; draw++)
                {
                    double pass = 0;
                    // Update in ascending order: mass moves from k to k-1.
                    for (int k = 1; k <= minimum; k++)
                    {
                        double mass = pending[k];
                        if (k > remaining) { pending[k] = 0; continue; }
                        expectedDraws += mass;
                        pending[k] = mass * (1 - chance);
                        if (k == 1) pass = mass * chance;
                        else pending[k - 1] += mass * chance;
                    }
                    accepted += pass;
                    if (block.Contributes) remaining--;
                }
            }
        }
        // Underflow in a physically possible tail is not evidence of impossibility.
        if (accepted == 0 && possible >= minimum) return null;
        return new(Math.Clamp(accepted, 0, 1), expectedDraws, expectedStreams);
    }

    internal uint[][] PackGpu()
    {
        var mask = new List<uint>();
        var meta = new List<uint> {
            (uint)_requirement.TargetMultiset.Count,
            (uint)_streams.Sum(stream => stream.Blocks.Where(block => block.Contributes).Sum(block => block.Count)),
            (uint)_streams.Count
        };
        bool withPrefix = HasEventPrefix;
        foreach (var stream in _streams)
        {
            meta.AddRange([(uint)stream.SeedOffset, (uint)(stream.SeedOffset >> 32), (uint)stream.Blocks.Count]);
            if (withPrefix) meta.AddRange([(uint)stream.PrefixRange, (uint)(stream.RequiredPrefixIndex + 1 ?? 0)]);
            foreach (var block in stream.Blocks)
            {
                meta.AddRange([(uint)mask.Count, (uint)block.Pool.Length, (uint)block.Count, block.Contributes ? 1u : 0u]);
                mask.AddRange(block.Pool.Select(card => card == _target ? 1u : 0u));
            }
        }
        return [meta.ToArray(), mask.ToArray()];
    }
}
