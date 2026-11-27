using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Effects;
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
    private const uint TargetSlot = 1, ForbiddenSlot = 2;
    private sealed record DrawBlock(uint[] Slots, int Count, bool Contributes);
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
    private bool? _targetRare;
    private Dictionary<ModelKey, bool?>? _openingRarity;
    internal bool RequiresRareRemainder => _requirement.Predicate == TransformationAggregatePredicate.ContainsMultisetAndRemainingRare;
    internal bool LimitTargetCopies => RequiresRareRemainder && _targetRare == false;
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
        if (model.Evaluation.TransformationAggregate is not { } condition ||
            !ScopedTransformationPools.Supports(model, condition)) return null;

        var sources = condition.SourceCounts().Keys.ToArray();
        var requirement = condition.ResultRequirements.Count switch {
            0 => new TransformationResultRequirement(sources, condition.Predicate, condition.MinimumRareCount, condition.TargetMultiset),
            1 => condition.ResultRequirements[0], _ => null
        };
        if (requirement is not { Predicate: TransformationAggregatePredicate.ContainsMultiset or TransformationAggregatePredicate.ContainsMultisetAndRemainingRare, MinimumRareCount: 0 } ||
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
            if (ancient is not null && !plan.AddBlock(personal, ancient.OrderedCandidates.ToArray(),
                condition.AncientOpportunityCount, true, card => card.CardKey, card => card.Rarity == EffectCardRarity.Rare)) return null;
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
        var pools = ScopedTransformationPools.Opening(_model, _opening, effects);
        if (pools is null) return false;
        bool contributes = _openingSource is not null && _requirement.Sources.Contains(_openingSource);
        if (RequiresRareRemainder && contributes)
            _openingRarity = effects.TransformPool?.GroupBy(card => card.CardKey)
                .ToDictionary(g => g.Key, g => (bool?)(g.First().Rarity == EffectCardRarity.Rare));
        foreach (var pool in pools)
            if (!AddBlock(stream, pool, 1, contributes, key => key, key => _openingRarity?.GetValueOrDefault(key))) return false;
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
            var pools = ScopedTransformationPools.Event(kind, scenario);
            if (pools is null) return false;
            foreach (var pool in pools)
                if (!AddBlock(stream, pool, 1, true, card => card.CardKey, card => card.Rarity == "Rare")) return false;
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

    private bool AddBlock<T>(DrawStream stream, IReadOnlyList<T> pool, int count, bool contributes,
        Func<T, ModelKey> key, Func<T, bool?> rarity)
    {
        if (pool.Count == 0) return false;
        var slots = new uint[pool.Count];
        for (int i = 0; contributes && i < pool.Count; i++)
        {
            bool target = key(pool[i]) == _target;
            if (target) slots[i] = TargetSlot;
            if (!RequiresRareRemainder) continue;
            if (rarity(pool[i]) is not { } rare) return false;
            if (target)
            {
                // With stable rarity, one counter suffices: a non-Rare target
                // must appear exactly the requested number of times.
                if (_targetRare.HasValue && _targetRare != rare) return false;
                _targetRare = rare;
            }
            else if (!rare) slots[i] = ForbiddenSlot;
        }
        stream.Blocks.Add(new(slots, count, contributes));
        return true;
    }

    // Compare with the gameplay projectors, including the actual offset left
    // by the direct transformation opening. Offer membership is a separate fact.
    internal bool Matches(ulong root)
    {
        var condition = _model.Evaluation.TransformationAggregate!;
        int supplied = 0;
        bool Observe(ModelKey? key, bool? rare)
        {
            if (key == _target)
            {
                supplied++;
                return !LimitTargetCopies || supplied <= _requirement.TargetMultiset.Count;
            }
            return !RequiresRareRemainder || rare == true;
        }
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
                foreach (var item in opening.EffectGroups.SelectMany(group => group.OrderedItems))
                    if (item.Kind == PredictedEffectKind.TransformCard &&
                        !Observe(item.TargetKey, item.TargetKey is { } key ? _openingRarity?.GetValueOrDefault(key) : null))
                        return false;
        }
        if (_ancient is not null)
            foreach (var card in _ancient.Project(Beta111Profile.Instance, root, 0, RngSemanticProfileId.Modern113, offset,
                condition.AncientOpportunityCount, condition.AncientSource == TransformationAncientSource.Astrolabe))
                if (!Observe(card.CardKey, card.Rarity == EffectCardRarity.Rare)) return false;
        if (_trialGateOnly && Beta111TrialTinkerProjector.TrialCase(root, 0) != (int)TrialCaseTarget.Nondescript) return false;
        foreach (var kind in _countedEvents)
        {
            var cards = Runtime.TransformationAggregateValidator.ProjectEvent(root, kind, condition.EventScenario!)
                ?? throw new InvalidOperationException("ScopedTransformCount.MissingClosedEventPool");
            if (cards.Count == 0) return false;
            foreach (var card in cards)
                if (!Observe(card?.CardKey, card?.Rarity == "Rare")) return false;
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
        if (RequiresRareRemainder) return EstimateRareRemainder(minimum, remaining);
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
                int hits = block.Contributes ? block.Slots.Count(slot => slot == TargetSlot) : 0;
                double chance = (double)hits / block.Slots.Length;
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

    private WorkEstimate? EstimateRareRemainder(int minimum, int remaining)
    {
        var pending = new double[minimum + 1];
        var next = new double[pending.Length];
        pending[minimum] = 1;
        double draws = 0, streams = 0;
        int possibleHits = 0, forcedHits = 0;
        bool hasLegalOutcome = true;
        foreach (var stream in _streams)
        {
            double reach = pending.Sum();
            streams += reach;
            if (stream.PrefixRange != 0) draws += reach;
            if (stream.RequiredPrefixIndex.HasValue)
                for (int k = 0; k < pending.Length; k++) pending[k] /= stream.PrefixRange;
            foreach (var block in stream.Blocks)
            {
                int hits = block.Slots.Count(slot => slot == TargetSlot);
                int other = block.Slots.Count(slot => slot == 0);
                if (hits != 0) possibleHits += block.Count;
                if (other == 0 && hits != 0) forcedHits += block.Count;
                if (hits == 0 && other == 0) hasLegalOutcome = false;
                double pHit = (double)hits / block.Slots.Length;
                double pOther = (double)other / block.Slots.Length;
                for (int draw = 0; draw < block.Count; draw++)
                {
                    Array.Clear(next);
                    for (int k = 0; k < pending.Length && k <= remaining; k++)
                    {
                        double mass = pending[k];
                        draws += mass;
                        next[k] += mass * pOther;
                        if (k > 0) next[k - 1] += mass * pHit;
                        else if (!LimitTargetCopies) next[0] += mass * pHit;
                    }
                    (pending, next) = (next, pending);
                    if (block.Contributes) remaining--;
                }
            }
        }
        double accepted = pending[0];
        if (accepted == 0 && hasLegalOutcome && possibleHits >= minimum &&
            (!LimitTargetCopies || forcedHits <= minimum)) return null;
        return new(Math.Clamp(accepted, 0, 1), draws, streams);
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
                meta.AddRange([(uint)mask.Count, (uint)block.Slots.Length, (uint)block.Count, block.Contributes ? 1u : 0u]);
                mask.AddRange(block.Slots);
            }
        }
        return [meta.ToArray(), mask.ToArray()];
    }
}
