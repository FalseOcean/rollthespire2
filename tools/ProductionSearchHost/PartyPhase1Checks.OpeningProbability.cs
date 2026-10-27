using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

internal static partial class PartyPhase1Checks
{
    private static int CheckOpeningProbability(SearchContext partyContext)
    {
        int checks = 0;
        var context = partyContext with { Party = null };
        double? Probability(SearchQuery q, SearchContext? ctx = null) => JointSelectivityEstimator.EstimateQuery(
            SearchSelectivityInput.From(SearchCompiler.CompilePlayer(q, ctx ?? context))).Probability;
        void Equal(double? actual, double expected, string label)
        {
            if (actual is not { } p || Math.Abs(p - expected) > 1e-12)
                throw new InvalidDataException("Party opening probability: " + label + ";actual=" + actual + ";expected=" + expected);
            checks++;
        }
        var effects = context.Authority.EffectAuthority!;
        var sixCards = effects.CharacterRewardPool!.DistinctBy(c => c.CardKey).Take(6)
            .Select(c => c with { Rarity = EffectCardRarity.Common, IsMultiplayerOnly = true }).ToArray();
        var smallContext = context with { Authority = context.Authority.WithEffectAuthority(effects with
            { CharacterRewardPool = sixCards, ColorlessRewardPool = [] }) };
        var massive = BaseGameModelKeys.Relics.MassiveScroll;
        var target = new NeowStructuredEffectSearchCondition(massive, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [sixCards[0].CardKey]);
        var query = SearchQuery.Empty with { OpeningRoute = new(massive), StructuredOpeningEffects = [target] };
        ModernNeowIdentityPredictor.TryEstimatePositiveOfferProbability(smallContext.Authority, massive, out double parent, out _);
        Equal(Probability(query, smallContext), parent * 3 / 6, "Massive Scroll captured six-card pool, three offers");
        Equal(Probability(query with { StructuredOpeningEffects = [target, target with { OutputKeys = [sixCards[0].CardKey] }] }, smallContext),
            parent * 3 / 6, "repeated same selected result is one fact");
        Equal(Probability(query with { StructuredOpeningEffects = [target, target with { OutputKeys = [sixCards[1].CardKey] }] }, smallContext),
            0, "one selected Massive card cannot satisfy two identities");
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var pool = effects.BonesEligibleRelics!.Where(k => k != bones).Distinct().ToArray();
        ModernNeowIdentityPredictor.TryEstimateOfferProbability(context.Authority, (a, b, c) => c == bones, out double bonesParent, out _);
        var bonesQuery = SearchQuery.Empty with { OpeningRoute = new(bones), LegacyNeow = SearchQuery.Empty.LegacyNeow with
            { BonesRelics = new([pool[0], pool[1]], [], [pool[2]]) } };
        int hits = 0;
        for (int i = 0; i < pool.Length; i++) for (int j = i + 1; j < pool.Length; j++)
            if ((i is 0 or 1 || j is 0 or 1) && i != 2 && j != 2) hits++;
        double pairMass = 2d * hits / (pool.Length * (pool.Length - 1));
        Equal(Probability(bonesQuery), bonesParent * pairMass, "Bones Any and Ban observe one unordered pair");
        var curse = effects.GeneratedCursePool![0];
        Equal(Probability(bonesQuery with { LegacyNeow = bonesQuery.LegacyNeow with { RequiredFinalCurse = curse } }),
            bonesParent * pairMass / effects.GeneratedCursePool.Count, "Bones final curse shares the parent once");
        Equal(Probability(bonesQuery with { OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl) }),
            0, "direct route cannot also satisfy Bones grants");
        return checks;
    }
}
