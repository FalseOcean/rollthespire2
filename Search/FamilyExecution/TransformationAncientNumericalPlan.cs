using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// A single Ancient source with a seed-independent material count and cursor.
/// All requirements in this shape observe the same draws. The Ancient offer
/// and final search disposition are established by their existing owners.
/// </summary>
internal sealed partial class TransformationAncientNumericalPlan
{
    private readonly record struct CandidateRule(int Counter, int Minimum, int Maximum, bool Rare);

    private static readonly TransformationAncientNumericalPlan Impossible = new(0, 0, [], 0, 0, 0, false);
    private readonly ulong _streamSeedOffset;
    private readonly int _drawCount, _counterCount, _minimumTargets, _minimumRare;
    private readonly CandidateRule[] _candidateRules;
    private readonly bool _requiresRareRemainder;

    private TransformationAncientNumericalPlan(ulong streamSeedOffset, int drawCount, CandidateRule[] candidateRules,
        int counterCount, int minimumTargets, int minimumRare, bool requiresRareRemainder)
    {
        _streamSeedOffset = streamSeedOffset;
        _drawCount = drawCount;
        _candidateRules = candidateRules;
        _counterCount = counterCount;
        _minimumTargets = minimumTargets;
        _minimumRare = minimumRare;
        _requiresRareRemainder = requiresRareRemainder;
    }

    internal static TransformationAncientNumericalPlan? TryCreate(SearchModel model)
    {
        var authority = model.Authority;
        var evaluation = model.Evaluation;
        if (evaluation.TransformationAggregate is not { UsesAncient: true, UsesNeow: false, UsesEvents: false } condition ||
            authority.ProfileId != RuntimeProfileId.Beta111 || authority.PlayersCount != 1 ||
            authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113 ||
            !ScopedTransformationPools.StartsAncientAtInitialCursor(model, evaluation.NeowRoute?.RouteRelicKey))
            return null;

        // The opening preserves the initial cursor. Its offer is checked by N
        // and Exact; the query already carries the planned material count.
        int drawCount = condition.AncientOpportunityCount;
        if (drawCount > TransformationRelicProjector.ProjectionDrawBudget) return null;
        if (drawCount == 0) return Impossible;

        var projector = TransformationRelicProjector.Capture(authority.EffectAuthority);
        if (projector is null) return null;
        var ordered = projector.OrderedCandidates;
        var rarities = new Dictionary<ModelKey, bool>();
        foreach (var card in ordered)
        {
            bool rare = card.Rarity == EffectCardRarity.Rare;
            if (!rarities.TryAdd(card.CardKey, rare) && rarities[card.CardKey] != rare) return null;
        }

        var requirements = condition.ResultRequirements.Count == 0
            ? [new TransformationResultRequirement([], condition.Predicate, condition.MinimumRareCount, condition.TargetMultiset)]
            : condition.ResultRequirements;
        var minimums = new Dictionary<ModelKey, int>();
        var nonRareMaximums = new Dictionary<ModelKey, int>();
        int minimumRare = 0;
        bool requiresRareRemainder = false;
        foreach (var requirement in requirements)
        {
            var targets = new Dictionary<ModelKey, int>();
            foreach (ModelKey key in requirement.TargetMultiset)
            {
                if (!rarities.ContainsKey(key)) return Impossible;
                targets[key] = targets.GetValueOrDefault(key) + 1;
            }
            foreach (var (key, count) in targets)
                minimums[key] = Math.Max(count, minimums.GetValueOrDefault(key));
            minimumRare = Math.Max(minimumRare, requirement.MinimumRareCount);
            if (requirement.Predicate != TransformationAggregatePredicate.ContainsMultisetAndRemainingRare) continue;
            requiresRareRemainder = true;
            foreach (var (key, rare) in rarities)
                if (!rare)
                    nonRareMaximums[key] = Math.Min(targets.GetValueOrDefault(key), nonRareMaximums.GetValueOrDefault(key, drawCount));
        }

        long minimumTargets = minimums.Values.Sum(value => (long)value);
        if (minimumTargets > drawCount || minimumRare > drawCount) return Impossible;
        var byCard = new Dictionary<ModelKey, CandidateRule>();
        int counterCount = 0;
        foreach (var (key, rare) in rarities)
        {
            int minimum = minimums.GetValueOrDefault(key);
            int maximum = nonRareMaximums.GetValueOrDefault(key, drawCount);
            if (minimum > maximum) return Impossible;
            byCard.Add(key, new(minimum > 0 || maximum > 0 && maximum < drawCount ? counterCount++ : -1, minimum, maximum, rare));
        }

        var candidateRules = new CandidateRule[ordered.Length];
        for (int index = 0; index < candidateRules.Length; index++)
            candidateRules[index] = byCard[ordered[index].CardKey];
        // Beta111 named streams add their name hash to the player root.
        ulong streamSeedOffset = Beta111Profile.Instance.DeriveNamedStreamSeed((ulong)authority.PlayerSlotIndex, "transformations");
        return new(streamSeedOffset, drawCount, candidateRules, counterCount, (int)minimumTargets, minimumRare, requiresRareRemainder);
    }

    internal bool Matches(ulong root)
    {
        if (_drawCount == 0) return false;
        var rng = new Beta110FastRng(unchecked(root + _streamSeedOffset));
        Span<int> occurrences = stackalloc int[_counterCount];
        occurrences.Clear();
        int targets = _minimumTargets, rare = _minimumRare;
        for (int remaining = _drawCount; remaining > 0; remaining--)
        {
            if (targets > remaining || rare > remaining) return false;
            if (targets == 0 && rare <= 0 && !_requiresRareRemainder) return true;
            var candidate = _candidateRules[rng.NextInt(_candidateRules.Length)];
            if (candidate.Rare) rare--;
            if (candidate.Maximum == 0) return false;
            if (candidate.Counter < 0) continue;
            int count = ++occurrences[candidate.Counter];
            if (count > candidate.Maximum) return false;
            if (count <= candidate.Minimum) targets--;
        }
        return targets == 0 && rare <= 0;
    }

    // Keep one entry per draw index, even when several indices select the same card.
    // A shared counter belongs to the card identity and observes all its occurrences.
    internal uint[][] PackGpu()
    {
        uint[] meta = new uint[8 + 2 * _counterCount];
        meta[0] = (uint)_streamSeedOffset;
        meta[1] = (uint)(_streamSeedOffset >> 32);
        meta[2] = (uint)_drawCount;
        meta[3] = (uint)_candidateRules.Length;
        meta[4] = (uint)_minimumTargets;
        meta[5] = (uint)_minimumRare;
        meta[6] = _requiresRareRemainder ? 1u : 0u;
        meta[7] = (uint)_counterCount;
        var pool = new uint[_candidateRules.Length];
        for (int index = 0; index < pool.Length; index++)
        {
            var rule = _candidateRules[index];
            pool[index] = (rule.Rare ? 0x80000000u : 0u) | (rule.Maximum == 0 ? 0x40000000u : 0u) |
                (uint)(rule.Counter + 1);
            if (rule.Counter < 0) continue;
            meta[8 + 2 * rule.Counter] = (uint)rule.Minimum;
            meta[9 + 2 * rule.Counter] = (uint)rule.Maximum;
        }
        return [meta, pool];
    }
}
