using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

// A draw is sampled once and advances every requirement that observes its source.
// Draw indices are independent under the probability model. Actual seeds continue
// to use their real personal and event streams in Exact and in the kernels.
internal static class ScopedTransformationDistribution
{
    internal sealed record Estimate(double? Probability, string Evidence);
    private const string Evidence = "ScopedTransformations.OrderedPools.JointScopedCounts";
    private const string Budget = "ScopedTransformations.JointDistributionBudget";
    private const string Missing = "ScopedTransformations.JointProbabilityUnavailable";
    private readonly record struct Card(ModelKey Key, bool? Rare);
    private sealed record Block(string Source, Card[] Pool, int Draws, Card[]? AlternatePool = null, bool BonesOpening = false)
    {
        internal IEnumerable<Card> PossibleCards => AlternatePool is null ? Pool : Pool.Concat(AlternatePool);
    }
    private readonly record struct Category(ModelKey? Key, bool? Rare);
    private enum CounterKind { Target, Rare, NonRareTargetLimit }
    private sealed record Counter(CounterKind Kind, ModelKey? Card, ulong Limit, ulong Stride)
    {
        internal bool Full(ulong state) => state / Stride % (Limit + 1) == Limit;
    }
    private sealed record Row(IReadOnlyList<string> Sources, HashSet<ModelKey> Targets, bool RareRemainder, List<Counter> Counters);
    private sealed record Outcome(Counter[]? First, Counter[]? Second, double Mass);
    // Even the largest admitted mixed-radix state is below ulong.MaxValue.
    private const ulong Rejected = ulong.MaxValue;

    internal static Estimate For(SearchModel model, TransformationAggregateCondition condition)
    {
        if (!(condition.IsBones ? ScopedTransformationPools.SupportsBonesDistribution(model, condition) :
                ScopedTransformationPools.Supports(model, condition))) return new(null, Missing);
        // Keep the linear one-counter calculation for deep same-card searches.
        if (!condition.IsBones && ScopedTransformationCountPlan.TryCreate(model)?.EstimateWork() is { } count)
            return new(count.Survival, ScopedTransformationCountPlan.WorkEstimate.Evidence);

        IReadOnlyList<TransformationResultRequirement> requirements = condition.ResultRequirements.Count > 0
            ? condition.ResultRequirements : [new(condition.SourceCounts().Keys.ToArray(), condition.Predicate,
                condition.MinimumRareCount, condition.TargetMultiset)];
        if (requirements.Count > 64) return new(null, Budget);
        var sources = requirements.SelectMany(row => row.Sources).ToHashSet(StringComparer.Ordinal);
        if (Capture(model, condition, sources) is not { } blocks) return new(null, Missing);
        var rows = new List<Row>();
        var required = new List<Counter>();
        var shared = new Dictionary<(string Scope, CounterKind Kind, ModelKey? Card, int Limit), Counter>();
        ulong stride = 1;
        foreach (var requirement in requirements)
        {
            bool remainder = requirement.Predicate == TransformationAggregatePredicate.ContainsMultisetAndRemainingRare;
            var relevant = blocks.Where(b => requirement.Sources.Contains(b.Source)).ToArray();
            if ((remainder || requirement.MinimumRareCount > 0) && relevant.Any(b => b.PossibleCards.Any(c => c.Rare is null)))
                return new(null, Missing);
            var counters = new List<Counter>();
            string scope = string.Join('|', requirement.Sources.Order(StringComparer.Ordinal));
            bool Add(CounterKind kind, ModelKey? key, int limit)
            {
                var identity = (scope, kind, key, limit);
                if (shared.TryGetValue(identity, out var existing))
                {
                    counters.Add(existing);
                    return true;
                }
                ulong radix = (ulong)limit + 1;
                if (stride > ulong.MaxValue / radix) return false;
                var counter = new Counter(kind, key, (ulong)limit, stride);
                shared.Add(identity, counter);
                stride *= radix;
                counters.Add(counter);
                if (kind != CounterKind.NonRareTargetLimit) required.Add(counter);
                return true;
            }
            if (requirement.MinimumRareCount > 0 && !Add(CounterKind.Rare, null, requirement.MinimumRareCount))
                return new(null, Budget);
            foreach (var target in requirement.TargetMultiset.GroupBy(key => key))
            {
                int copies = target.Count();
                if (!Add(CounterKind.Target, target.Key, copies)) return new(null, Budget);
                // Exact assigns every non-Rare copy to a matching target slot.
                // Extra Rare copies are allowed, even when they share an identity.
                if (remainder && relevant.Any(b => b.PossibleCards.Any(c => c.Key == target.Key && c.Rare == false)) &&
                    !Add(CounterKind.NonRareTargetLimit, target.Key, copies)) return new(null, Budget);
            }
            rows.Add(new(requirement.Sources, requirement.TargetMultiset.ToHashSet(), remainder, counters));
        }

        // Store reachable zero-mass states as well: floating point underflow
        // must not convert an attainable end state into a proof of impossibility.
        var states = new Dictionary<(ulong First, ulong Second), double> { [(0, 0)] = condition.TrialNondescript ? 1d / 3 : 1d };
        string evidence = blocks.Any(block => block.AlternatePool is not null)
            ? Evidence + ";T.SharedTransformOrderUnion" : Evidence;
        long remainingWork = 1_000_000;
        foreach (var block in blocks)
        {
            var outcomes = Outcomes(block, rows);
            int transitionWork = outcomes.Sum(outcome => 1 + (outcome.First?.Length ?? 0) +
                (ReferenceEquals(outcome.First, outcome.Second) ? 0 : outcome.Second?.Length ?? 0));
            for (int draw = 0; draw < block.Draws; draw++)
            {
                remainingWork -= (long)states.Count * transitionWork;
                if (remainingWork < 0) return new(null, Budget);
                var next = new Dictionary<(ulong, ulong), double>();
                foreach (var (state, probability) in states)
                foreach (var outcome in outcomes)
                {
                    ulong first = Advance(state.First, outcome.First);
                    ulong second = state.First == state.Second && ReferenceEquals(outcome.First, outcome.Second)
                        ? first : Advance(state.Second, outcome.Second);
                    if (first == Rejected && second == Rejected) continue;
                    var updated = (first, second);
                    next[updated] = next.GetValueOrDefault(updated) + probability * outcome.Mass;
                    if (next.Count > 16_384) return new(null, Budget);
                }
                if (next.Count == 0) return new(0, evidence);
                states = next;
            }
        }
        double result = 0;
        bool reachable = false;
        bool Matches(ulong state) => state != Rejected && required.All(counter => counter.Full(state));
        foreach (var (state, probability) in states)
        {
            // Each route must satisfy all rows of its own witness, including the
            // same shared event and Ancient outcomes following the opening.
            if (!Matches(state.First) && !Matches(state.Second)) continue;
            reachable = true;
            result += probability;
        }
        return reachable && result == 0 ? new(null, "ScopedTransformations.ProbabilityUnderflow") :
            new(Math.Clamp(result, 0, 1), evidence);
    }

    private static ulong Advance(ulong state, Counter[]? changes)
    {
        if (state == Rejected || changes is null) return Rejected;
        ulong updated = state;
        foreach (var counter in changes)
        {
            if (!counter.Full(state)) updated += counter.Stride;
            else if (counter.Kind == CounterKind.NonRareTargetLimit) return Rejected;
        }
        return updated;
    }

    private static List<Outcome> Outcomes(Block block, List<Row> rows)
    {
        var observing = rows.Where(row => row.Sources.Contains(block.Source)).ToArray();
        var targets = observing.SelectMany(row => row.Targets).ToHashSet();
        bool needRarity = observing.Any(row => row.RareRemainder || row.Counters.Any(c => c.Kind == CounterKind.Rare));
        Category Classify(Card card) => new(targets.Contains(card.Key) ? card.Key : null, needRarity ? card.Rare : null);
        var transitions = new Dictionary<Category, Counter[]?>();
        Counter[]? Changes(Category category)
        {
            if (transitions.TryGetValue(category, out var known)) return known;
            var (key, rare) = category;
            if (rare == false && observing.Any(r => r.RareRemainder && (key is null || !r.Targets.Contains(key.Value))))
                return transitions[category] = null;
            return transitions[category] = observing.SelectMany(row => row.Counters).Where(c => c.Kind switch
            {
                CounterKind.Rare => rare == true,
                CounterKind.Target => c.Card == key,
                _ => rare == false && c.Card == key
            }).Distinct().ToArray();
        }

        var outcomes = new List<Outcome>();
        if (block.BonesOpening)
        {
            var firstPool = block.Pool.Select(Classify).ToArray();
            var secondPool = block.AlternatePool?.Select(Classify).ToArray() ?? firstPool;
            var pairs = SharedTransformationDrawRanks.CountPairs(firstPool, secondPool);
            foreach (var (pair, mass) in pairs)
            {
                var first = Changes(pair.First);
                var second = Changes(pair.Second);
                if (first is not null || second is not null)
                    outcomes.Add(new(first, second, mass / (double)NeowStaticDrawGateCompiler.MantissaSpace));
            }
        }
        else
        {
            foreach (var category in block.Pool.GroupBy(Classify))
                if (Changes(category.Key) is { } update)
                    outcomes.Add(new(update, update, category.Count() / (double)block.Pool.Length));
        }
        return outcomes;
    }

    private static List<Block>? Capture(SearchModel model, TransformationAggregateCondition condition, HashSet<string> sources)
    {
        var blocks = new List<Block>();
        var effects = model.Authority.EffectAuthority;
        string? openingSource = sources.FirstOrDefault(s => s.StartsWith("N.", StringComparison.Ordinal));
        string ancientSource = "A." + condition.RequiredAncientKey.Entry;
        bool countAncient = condition.UsesAncient && sources.Contains(ancientSource) && condition.AncientOpportunityCount > 0;
        var opening = condition.UsesNeow ? condition.RequiredOpeningKey : model.Evaluation.NeowRoute?.RouteRelicKey;
        if ((openingSource is not null || countAncient) && opening.HasValue)
        {
            if (effects is null || effects.CapturedProfileId != model.Authority.ProfileId) return null;
            ModelKey[][] first;
            ModelKey[][]? second = null;
            // A separately selected Bones pair can establish the Ancient cursor
            // without contributing any opening card to a transformation row.
            if (opening == BaseGameModelKeys.Relics.NeowsBones)
            {
                if (ScopedTransformationPools.Bones(model, condition) is not { } pickups) return null;
                (first, second) = pickups;
            }
            else if (ScopedTransformationPools.Opening(model, opening, effects) is { } pools) first = pools;
            else return null;
            if (openingSource is not null)
            {
                // Exact reads opening-output rarity from TransformPool by identity.
                var rarities = effects.TransformPool?.GroupBy(c => c.CardKey)
                    .ToDictionary(g => g.Key, g => (bool?)(g.First().Rarity == EffectCardRarity.Rare));
                Card[] Cards(ModelKey[] pool) => pool.Select(key => new Card(key, rarities?.GetValueOrDefault(key))).ToArray();
                for (int i = 0; i < first.Length; i++)
                    blocks.Add(new(openingSource, Cards(first[i]), 1, second is null ? null : Cards(second[i]), condition.IsBones));
            }
        }
        if (countAncient)
        {
            if (TransformationRelicProjector.Capture(effects) is not { } projector) return null;
            var pool = projector.OrderedCandidates.ToArray().Select(c => new Card(c.CardKey, c.Rarity == EffectCardRarity.Rare));
            blocks.Add(new(ancientSource, pool.ToArray(), condition.AncientOpportunityCount));
        }
        foreach (var kind in EventResultTransformSemantics.Selected(condition))
        {
            string source = "E." + EventResultTransformSemantics.Entry(kind);
            if (!sources.Contains(source) && kind != EventResultConditionKind.TrialNondescriptInitialBasicsContains) continue;
            if (condition.EventScenario is not { } scenario) return null;
            MorphicGrovePredictor.ValidateAuthority(model.Detection, model.Authority, scenario);
            if (!sources.Contains(source)) continue;
            if (ScopedTransformationPools.Event(kind, scenario) is not { } pools) return null;
            foreach (var pool in pools)
                blocks.Add(new(source, pool.Select(c => new Card(c.CardKey, c.Rarity == "Rare")).ToArray(), 1));
        }
        return blocks.Any(b => b.Pool.Length == 0 || b.AlternatePool is { Length: 0 }) ? null : blocks;
    }
}
