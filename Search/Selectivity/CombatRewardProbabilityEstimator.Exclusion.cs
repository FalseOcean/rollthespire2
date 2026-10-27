using System.Numerics;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class CombatRewardProbabilityEstimator
{
    // A probability-only absence event. It never creates a negative Fast filter.
    // Safe selections use the original full-pool denominator and no-duplicate
    // group counts, so banned draws lose mass without changing rarity fallback.
    internal static SearchSelectivityEstimate EstimateCardExclusion(SearchSelectivityInput input)
    {
        var exclusion = input.CompiledSearch.NormalizedQuery.CombatCardExclusion;
        SearchSelectivityEstimate Unknown(string issue) => SearchSelectivityEstimate.Unpriced(
            "Probability.CombatReward.Exclusion:" + issue, "Card exclusion needs the captured card pool and explicit reward context.");
        if (exclusion is null || exclusion.IsEmpty) return Result(1);
        if (exclusion.BattleCount is < 1 or > 6) return Unknown("WindowUnsupported");
        if (input.Authority.AvailableCardIdentities is not { } identities) return Unknown("CardIdentityAuthorityUnavailable");
        var registered = identities.ToHashSet(ModelKeyComparer.Instance);
        if (exclusion.CardKeys.Any(key => !registered.Contains(key))) return Unknown("MemberUnavailable");
        var authority = input.Authority.EffectAuthority;
        if (authority?.HasExactCharacterRewardPool != true) return Unknown("CardPoolAuthorityMissing");
        if (!TryBuildCardPool(authority, new Dictionary<ModelKey, int>(ModelKeyComparer.Instance), input.Authority.PlayersCount > 1,
            out var cards, out string poolIssue)) return Unknown(poolIssue);
        var row = new NormalCombatRewardSearchCondition(1, new([], [], exclusion.CardKeys),
            NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, null, null);
        TryBuildMatcherContext([row], null, null, out var matcher, out _);
        if (!TryResolveAuthoredProfile(input, ProbabilitySemanticProjection.From(input), matcher,
            out var profile, out string detail, out string issue)) return Unknown(issue + ";" + detail);
        var banned = exclusion.CardKeys.ToHashSet(ModelKeyComparer.Instance);
        var safeTotal = new int[3]; var safePower = new int[3];
        foreach (var card in cards.Entries.Where(card => !banned.Contains(card.Key)))
        {
            int rarity = RarityIndex(card.Rarity); safeTotal[rarity]++;
            if (card.Type == EffectCardType.Power) safePower[rarity]++;
        }
        var states = new Dictionary<int, double> { [0] = 1 };
        var memo = new Dictionary<(int Battle, int Pity), Dictionary<int, double>>();
        for (int battle = 1; battle <= exclusion.BattleCount; battle++)
        {
            var next = new Dictionary<int, double>();
            foreach (var (pity, mass) in states)
            {
                if (!memo.TryGetValue((battle, pity), out var outcomes))
                {
                    outcomes = Group(new Dictionary<int, double> { [pity] = 1 }, profile.LastingCandy && battle % 2 == 0);
                    for (int extra = 0; extra < profile.AdditionalCardRewardCount; extra++) outcomes = Group(outcomes, false);
                    memo[(battle, pity)] = outcomes;
                }
                foreach (var (ending, probability) in outcomes) AddMass(next, ending, mass * probability);
            }
            states = next;
        }
        return Result(Math.Clamp(states.Values.Sum(), 0, 1));

        Dictionary<int, double> Group(Dictionary<int, double> incoming, bool candy)
        {
            var sequence = incoming.ToDictionary(pair => new CardSequenceState(pair.Key, BigInteger.Zero, default), pair => pair.Value);
            for (int ordinal = 0; ordinal < CardsPerReward + (candy ? 1 : 0); ordinal++)
            {
                bool powerOnly = candy && ordinal == CardsPerReward;
                var next = new Dictionary<CardSequenceState, double>();
                foreach (var (state, mass) in sequence)
                {
                    if (powerOnly && cards.PowerByRarity.Sum() == 0) { AddMass(next, state, mass); continue; }
                    var group = powerOnly && Enumerable.Range(0, 3).Sum(rarity => AvailableCount(cards, state.Group, rarity, true)) == 0
                        ? default : state.Group;
                    var branches = powerOnly ? CandyRarityBranches(input.Ascension, state.PityStreak) : RarityBranches(input.Ascension, state.PityStreak);
                    foreach (var (rolled, pity, probability) in branches)
                    {
                        int rarity = NextAvailableRarity(cards, group, rolled, powerOnly);
                        if (rarity < 0) continue;
                        int available = AvailableCount(cards, group, rarity, powerOnly);
                        if (available == 0) continue;
                        int powers = safePower[rarity] - UsedPower(group, rarity);
                        int ordinary = safeTotal[rarity] - safePower[rarity] - UsedTotal(group, rarity) + UsedPower(group, rarity);
                        if (powers > 0) AddMass(next, new CardSequenceState(pity, BigInteger.Zero,
                            ConsumeCard(group, rarity, true, -1)), mass * probability * powers / available);
                        if (!powerOnly && ordinary > 0) AddMass(next, new CardSequenceState(pity, BigInteger.Zero,
                            ConsumeCard(group, rarity, false, -1)), mass * probability * ordinary / available);
                    }
                }
                sequence = next;
            }
            var output = new Dictionary<int, double>();
            foreach (var (state, mass) in sequence) AddMass(output, state.PityStreak, mass);
            return output;
        }

        SearchSelectivityEstimate Result(double p) => SearchSelectivityEstimate.Exact(p,
            SearchSelectivityMethod.ConditionalChain, SearchSelectivityCoverage.ExactRequestedPredicate,
            SearchSelectivityDependencyClass.AssumedIndependent, "Probability.CombatReward.ExclusionAbsence",
            "Chronological rarity/pity and per-reward distinct selection; every reward group avoids the authored set.",
            ["ExclusionProbabilityOnly=true;ExecutionRemainsProductionExact", "UnspecifiedOpeningOutcomesHaveNoInfluence=OwnerApproved",
                "ExplicitWheelCandyAndCapturedMultiplayerPoolsRetained=true"]);
    }
}
