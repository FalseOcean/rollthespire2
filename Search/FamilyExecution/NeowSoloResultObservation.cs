using RolltheSpire2.Core.Seed;
using static RolltheSpire2.Search.FamilyExecution.Beta110FastStructuredConditionKind;
using static RolltheSpire2.Search.FamilyExecution.NeowLocalOperators;

namespace RolltheSpire2.Search.FamilyExecution;

// Terminal local observation. The caller supplies the actual arrival on each
// required stream; the proof below bounds the unobserved tail of the operator.
internal sealed class NeowSoloResultObservation
{
    internal static readonly ulong TransformationsHash = XxHash64.Hash("transformations"u8, 0);
    private readonly NeowReplayPlan _plan;
    private readonly Beta110FastStructuredCondition? _primary;
    private readonly ushort _lostPotion;
    internal byte Source { get; }
    internal string Shape { get; }
    internal bool UsesRewards => Source is not (Beta110FastRelicCatalog.LeafyPoultice or
        Beta110FastRelicCatalog.NewLeaf or Beta110FastRelicCatalog.PhialHolster);
    internal bool UsesTransformations => Source is Beta110FastRelicCatalog.LeafyPoultice or Beta110FastRelicCatalog.NewLeaf;
    internal bool UsesNonCombat => Source is Beta110FastRelicCatalog.Kaleidoscope or Beta110FastRelicCatalog.PhialHolster;
    internal bool LocalFirst => UsesTransformations || Source == Beta110FastRelicCatalog.ArcaneScroll ||
        Source == Beta110FastRelicCatalog.LostCoffer && _primary is null;

    private NeowSoloResultObservation(NeowReplayPlan plan, byte source, string shape,
        Beta110FastStructuredCondition? primary, ushort lostPotion = Beta110FastDenseId.Invalid)
    {
        _plan = plan;
        Source = source;
        Shape = shape;
        _primary = primary;
        _lostPotion = lostPotion;
    }

    internal static NeowSoloResultObservation? TryCompile(NeowReplayPlan plan)
    {
        if (!plan.AuditedModern113IdentityReplay || !plan.Authority.UsesChildShuffle || plan.Authority.PlayersCount != 1 ||
            plan.HasFinalCurseFastProjection || plan.StructuredConditions is not [var first, ..] ||
            plan.StructuredConditions.Any(row => row.SourceRelicId != first.SourceRelicId)) return null;
        byte source = first.SourceRelicId;
        var c = plan.Authority.EffectCatalog;
        var rows = plan.StructuredConditions;
        bool single = rows.Length == 1;
        switch (source)
        {
            case Beta110FastRelicCatalog.ArcaneScroll when single && first.Kind == ArcaneScrollGeneratedCard &&
                c.CharacterRewardAuthorityExact && Valid(c.CharacterRewardPool.Rare, 1):
                return new(plan, source, "ArcaneIndex", first);
            case Beta110FastRelicCatalog.NewLeaf when single && first.Kind == NewLeafTransform &&
                c.NewLeafTransformAuthorityExact && Valid(c.NewLeafTransformPool, 1):
                return new(plan, source, "NewLeafIndex", first);
            case Beta110FastRelicCatalog.LeafyPoultice when single && first.Kind == LeafyPoulticeTransforms &&
                first.TargetCount is 1 or 2 && c.LeafyTransformAuthorityExact &&
                Valid(c.LeafyStrikeTransformPool, 1) && Valid(c.LeafyDefendTransformPool, 1):
                return new(plan, source, "LeafyTargets" + first.TargetCount, first);
            case Beta110FastRelicCatalog.HeftyTablet when single && first.Kind == HeftyTabletRareOffer &&
                c.CharacterRewardAuthorityExact && Valid(c.CharacterRewardPool.Rare, 3):
                return new(plan, source, "HeftyUntilHit", first);
            case Beta110FastRelicCatalog.LeadPaperweight when single && first.Kind == LeadPaperweightColorlessOffer &&
                c.ColorlessRewardAuthorityExact && ValidUnion(c.ColorlessRewardPool, 2):
                return new(plan, source, "LeadUntilHit", first);
            case Beta110FastRelicCatalog.LostCoffer when rows.Length <= 2 && c.CharacterRewardAuthorityExact &&
                c.PotionAuthorityExact && ValidUnion(c.CharacterRewardPool, 3) && ValidPotions(c.PotionPool, 1):
            {
                var cards = rows.Where(row => row.Kind == LostCofferCardOffer && row.TargetCount == 1).ToArray();
                var potions = rows.Where(row => row.Kind == LostCofferPotion && row.TargetCount == 1).ToArray();
                if (cards.Length > 1 || potions.Length > 1 || cards.Length + potions.Length != rows.Length) return null;
                string shape = cards.Length == 0 ? "LostPotion" : potions.Length == 0 ? "LostCard" : "LostCardAndPotion";
                return new(plan, source, shape, cards.Length == 0 ? null : cards[0],
                    potions.Length == 0 ? Beta110FastDenseId.Invalid : potions[0].Target0);
            }
            case Beta110FastRelicCatalog.PhialHolster when single && first.Kind == PhialHolsterPotions &&
                first.TargetCount is 1 or 2 && c.PotionAuthorityExact && ValidPotions(c.PotionPool, 2):
                return new(plan, source, "PhialTargets" + first.TargetCount, first);
            case Beta110FastRelicCatalog.Kaleidoscope when single && first.Kind == KaleidoscopeIndependentOfferTargets &&
                first.TargetCount is 1 or 2 && plan.Authority.AllCharacterCardPoolsUnlocked && c.OtherCharacterCardAuthorityExact &&
                c.OtherCharacterPools.Length is >= 3 and <= 16 && c.OtherCharacterPools.All(pool => ValidUnion(pool, 1)):
                return new(plan, source, first.OrderedKaleidoscope ? "KaleidoscopePosition" : "KaleidoscopeTargets" + first.TargetCount, first);
            case Beta110FastRelicCatalog.ScrollBoxes when single && c.CharacterCardAuthorityExact &&
                Valid(c.CharacterRewardPool.Common, 4) && Valid(c.CharacterRewardPool.Uncommon, 2) &&
                !c.CharacterRewardPool.Common.Intersect(c.CharacterRewardPool.Uncommon).Any():
            {
                if (first.Kind == ScrollBoxesCardComposition && first.TargetCount is >= 1 and <= 3)
                    return new(plan, source, "ScrollBoxesComposition", first);
                return first.Kind == ScrollBoxesTripleClaw && plan.Authority.UsesDefectScrollBoxesRule &&
                    c.ClawId != Beta110FastDenseId.Invalid ? new(plan, source, "ScrollBoxesClaw", null) : null;
            }
            default:
                return null;
        }
    }

    internal bool Matches(ref Beta110FastRng rewards, ref Beta110FastRng transformations, ref Beta110FastRng nonCombat)
    {
        var c = _plan.Authority.EffectCatalog;
        return Source switch
        {
            Beta110FastRelicCatalog.ArcaneScroll => c.CharacterRewardPool.Rare[rewards.NextInt(c.CharacterRewardPool.Rare.Length)] == _primary!.Value.Target0,
            Beta110FastRelicCatalog.NewLeaf => DrawNewLeafTransform(c, ref transformations) == _primary!.Value.Target0,
            Beta110FastRelicCatalog.LeafyPoultice => MatchLeafy(c, ref transformations, _primary!.Value),
            Beta110FastRelicCatalog.HeftyTablet => MatchCardOffer(c.CharacterRewardPool, ref rewards, 3, true, _primary!.Value.Target0, false),
            Beta110FastRelicCatalog.LeadPaperweight => MatchCardOffer(c.ColorlessRewardPool, ref rewards, 2, false, _primary!.Value.Target0, false),
            Beta110FastRelicCatalog.LostCoffer => MatchLostCoffer(c, ref rewards),
            Beta110FastRelicCatalog.PhialHolster => MatchPhial(c.PotionPool, ref nonCombat, _primary!.Value),
            Beta110FastRelicCatalog.Kaleidoscope => MatchKaleidoscope(c, ref rewards, ref nonCombat, _primary!.Value),
            Beta110FastRelicCatalog.ScrollBoxes => MatchScrollBoxes(c, ref rewards),
            _ => throw new InvalidOperationException("N.UnsupportedSoloObservationSource")
        };
    }

    private static bool MatchLeafy(Beta110FastEffectCatalog c, ref Beta110FastRng rng, Beta110FastStructuredCondition row)
    {
        ushort strike = c.LeafyStrikeTransformPool[rng.NextInt(c.LeafyStrikeTransformPool.Length)];
        if (row.TargetCount == 1 && strike == row.Target0) return true;
        if (row.TargetCount == 2 && strike != row.Target0 && strike != row.Target1) return false;
        ushort defend = c.LeafyDefendTransformPool[rng.NextInt(c.LeafyDefendTransformPool.Length)];
        return row.TargetCount == 1 ? defend == row.Target0 :
            strike == row.Target0 && defend == row.Target1 || strike == row.Target1 && defend == row.Target0;
    }

    private bool MatchLostCoffer(Beta110FastEffectCatalog c, ref Beta110FastRng rewards)
    {
        bool checkPotion = _lostPotion != Beta110FastDenseId.Invalid;
        if (_primary is { } card)
        {
            if (!MatchCardOffer(c.CharacterRewardPool, ref rewards, 3, false, card.Target0, checkPotion)) return false;
        }
        else AdvanceNormalCards(ref rewards, 3);
        return !checkPotion || RollPotion(c.PotionPool, [], ref rewards) == _lostPotion;
    }

    private bool MatchCardOffer(Beta110FastCardPool pool, ref Beta110FastRng rewards, int count, bool forcedRare,
        ushort target, bool continueStream)
    {
        Span<ushort> used = stackalloc ushort[3];
        for (int index = 0; index < count; index++)
        {
            ushort card = forcedRare ? SelectUnused(pool.Rare, used[..index], ref rewards) :
                RollCard(pool, _plan.Authority.Ascension, used[..index], ref rewards);
            if (card == target)
            {
                if (continueStream) AdvanceNormalCards(ref rewards, count - index - 1);
                return true;
            }
            used[index] = card;
        }
        return false;
    }

    private static bool MatchPhial(Beta110FastPotionPool pool, ref Beta110FastRng rng, Beta110FastStructuredCondition row)
    {
        Span<ushort> used = stackalloc ushort[1];
        ushort first = RollPotion(pool, [], ref rng);
        if (row.TargetCount == 1 && first == row.Target0) return true;
        if (row.TargetCount == 2 && (first != row.Target0 && first != row.Target1 || row.Target0 == row.Target1)) return false;
        used[0] = first;
        ushort second = RollPotion(pool, used, ref rng);
        return row.TargetCount == 1 ? second == row.Target0 :
            first == row.Target0 && second == row.Target1 || first == row.Target1 && second == row.Target0;
    }

    private bool MatchKaleidoscope(Beta110FastEffectCatalog c, ref Beta110FastRng rewards, ref Beta110FastRng nc,
        Beta110FastStructuredCondition row)
    {
        ushort firstTarget = row.OrderedKaleidoscope ? row.KaleidoscopeFirstTarget : row.Target0;
        ushort secondTarget = row.OrderedKaleidoscope ? row.KaleidoscopeSecondTarget : row.Target1;
        Span<ushort> cards = stackalloc ushort[3];
        if (row.OrderedKaleidoscope && firstTarget == Beta110FastDenseId.Invalid)
        {
            nc.ConsumeShuffleSeed();
            AdvanceNormalCards(ref rewards, 3);
            return KaleidoscopeGroupContains(c, ref rewards, ref nc, cards, secondTarget);
        }
        GenerateKaleidoscopeOffers(_plan.Authority.Ascension, c, ref rewards, ref nc, cards, 1, _plan.Authority.RngSemantics);
        bool firstInGroup = cards.Contains(firstTarget);
        if (row.OrderedKaleidoscope)
            return firstInGroup && (secondTarget == Beta110FastDenseId.Invalid ||
                KaleidoscopeGroupContains(c, ref rewards, ref nc, cards, secondTarget));
        if (row.TargetCount == 1)
            return firstInGroup || KaleidoscopeGroupContains(c, ref rewards, ref nc, cards, firstTarget);
        bool secondInFirstGroup = cards.Contains(secondTarget);
        if (!firstInGroup && !secondInFirstGroup) return false;
        GenerateKaleidoscopeOffers(_plan.Authority.Ascension, c, ref rewards, ref nc, cards, 1, _plan.Authority.RngSemantics);
        return firstInGroup && cards.Contains(secondTarget) || secondInFirstGroup && cards.Contains(firstTarget);

    }

    private bool KaleidoscopeGroupContains(Beta110FastEffectCatalog c, ref Beta110FastRng rewards,
        ref Beta110FastRng nc, Span<ushort> cards, ushort target)
    {
        GenerateKaleidoscopeOffers(_plan.Authority.Ascension, c, ref rewards, ref nc, cards, 1, _plan.Authority.RngSemantics);
        return target == Beta110FastDenseId.Invalid || cards.Contains(target);
    }

    private bool MatchScrollBoxes(Beta110FastEffectCatalog c, ref Beta110FastRng rewards)
    {
        if (_primary is null)
        {
            if (rewards.NextInt(100) == 0) return true;
            for (int draw = 0; draw < 3; draw++) rewards.ConsumeShuffleSeed();
            return rewards.NextInt(100) == 0;
        }
        Span<ushort> cards = stackalloc ushort[3];
        Span<ushort> used = stackalloc ushort[6];
        int count = 0;
        for (int bundle = 0; bundle < 2; bundle++)
        {
            GenerateScrollBoxesBundle(c, _plan.Authority.UsesDefectScrollBoxesRule, ref rewards, cards, used, ref count, out bool claw);
            // Claw is a special bundle (one compressed identity with
            // multiplicity three), outside the two-Common/one-Uncommon row.
            if (!claw && ContainsTargets(cards, _primary.Value)) return true;
        }
        return false;
    }

    internal static void AdvanceNormalCards(ref Beta110FastRng rewards, int cards)
    {
        for (int draw = 0; draw < 3 * cards; draw++) _ = rewards.NextDouble();
    }

    private static bool Valid(ushort[] pool, int count) =>
        !pool.Contains(Beta110FastDenseId.Invalid) && pool.Distinct().Take(count).Count() == count;

    private static bool ValidUnion(Beta110FastCardPool pool, int count) =>
        Valid(pool.Common.Concat(pool.Uncommon).Concat(pool.Rare).ToArray(), count);

    internal static bool ValidPotions(Beta110FastPotionPool pool, int count) =>
        Valid(pool.Common, count) && Valid(pool.Uncommon, count) && Valid(pool.Rare, count);
}
