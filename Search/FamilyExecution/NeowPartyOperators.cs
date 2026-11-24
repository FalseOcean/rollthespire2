using RolltheSpire2.Core.Effects.Snapshots;
using static RolltheSpire2.Search.FamilyExecution.NeowLocalOperators;
namespace RolltheSpire2.Search.FamilyExecution;

// Party effect dispatch. Changes to the other mode do not enter this core.
internal static class NeowPartyOperators
{
    internal static bool ExecuteRelic(
        byte relicId,
        bool isActive,
        NeowReplayPlan plan,
        Beta110FastEffectCatalog catalog,
        bool needsRewards,
        bool needsNiche,
        bool needsTransformations,
        bool needsCombatPotionGeneration,
        ref Beta110FastRng rewards,
        ref Beta110FastRng niche,
        ref Beta110FastRng transformations,
        ref Beta110FastRng combatPotionGeneration,
        ref Beta110FastRng nonCombat,
        Span<byte> matched)
    {
        if (!isActive)
            return true;

        if (relicId == Beta110FastRelicCatalog.LargeCapsule || relicId == Beta110FastRelicCatalog.SmallCapsule)
        {
            if (needsRewards) {
                rewards.NextFloat();
                if (relicId == Beta110FastRelicCatalog.LargeCapsule) rewards.NextFloat();
            }
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.ArcaneScroll)
        {
            if (!needsRewards) return true;
            if (!catalog.CharacterRewardAuthorityExact) return false;
            ushort card = SelectUnused(catalog.CharacterRewardPool.Rare, ReadOnlySpan<ushort>.Empty, ref rewards);
            if (card == Beta110FastDenseId.Invalid) return false;
            MatchSingleOfferCondition(relicId, Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard,
                plan.StructuredConditions, card, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.MassiveScroll)
        {
            if (!needsRewards) return true;
            Span<ushort> offer = stackalloc ushort[3];
            if (!GenerateNormalCardOffer(catalog.MultiplayerRewardPool, plan.Authority.Ascension, ref rewards, offer)) return false;
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.MassiveScrollOffer, plan.StructuredConditions, offer, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.HeftyTablet)
        {
            if (!needsRewards) return true;
            if (!catalog.CharacterRewardAuthorityExact || catalog.CharacterRewardPool.Rare.Length < 3) return false;
            Span<ushort> offer = stackalloc ushort[3];
            if (!GenerateForcedRareOffer(catalog.CharacterRewardPool, ref rewards, offer)) return false;
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.HeftyTabletRareOffer,
                plan.StructuredConditions, offer, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.LeadPaperweight)
        {
            if (!needsRewards) return true;
            if (!catalog.ColorlessRewardAuthorityExact) return false;
            Span<ushort> offer = stackalloc ushort[2];
            if (!GenerateNormalCardOffer(catalog.ColorlessRewardPool, plan.Authority.Ascension, ref rewards, offer)) return false;
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.LeadPaperweightColorlessOffer,
                plan.StructuredConditions, offer, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.LostCoffer)
        {
            if (!needsRewards) return true;
            if (!catalog.CharacterRewardAuthorityExact || !catalog.PotionAuthorityExact) return false;
            Span<ushort> cards = stackalloc ushort[3];
            if (!GenerateNormalCardOffer(catalog.CharacterRewardPool, plan.Authority.Ascension, ref rewards, cards)) return false;
            ushort potion = RollPotion(catalog.PotionPool, ReadOnlySpan<ushort>.Empty, ref rewards);
            if (potion == Beta110FastDenseId.Invalid) return false;
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.LostCofferCardOffer,
                plan.StructuredConditions, cards, matched);
            MatchSingleOfferCondition(relicId, Beta110FastStructuredConditionKind.LostCofferPotion,
                plan.StructuredConditions, potion, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.Kaleidoscope)
        {
            if (!needsNiche && !needsRewards) return true;
            ref Beta110FastRng poolRng = ref (plan.Authority.UsesChildShuffle ? ref nonCombat : ref niche);
            bool hasCondition = HasStructuredConditionForRelic(plan.StructuredConditions, relicId);
            if (!needsRewards)
            {
                int poolCount = catalog.OtherCharacterPools.Length;
                if (!catalog.OtherCharacterCardAuthorityExact || poolCount < 3) return false;
                if (plan.Authority.UsesChildShuffle) { poolRng.ConsumeShuffleSeed(); poolRng.ConsumeShuffleSeed(); }
                else { poolRng.ConsumeUnstableShuffle(poolCount); poolRng.ConsumeUnstableShuffle(poolCount); }
                return true;
            }
            Span<ushort> offers = stackalloc ushort[6];
            offers.Fill(Beta110FastDenseId.Invalid);
            if (!GenerateKaleidoscopeOffers(plan.Authority.Ascension, catalog, ref rewards, ref poolRng, offers,
                    semanticProfile: plan.Authority.RngSemantics)) return false;
            if (hasCondition) MatchKaleidoscopeConditions(plan.StructuredConditions, offers, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.ScrollBoxes)
        {
            if (!needsRewards) return true;
            Span<ushort> cards = stackalloc ushort[6];
            cards.Fill(Beta110FastDenseId.Invalid);
            Span<byte> bundleSizes = stackalloc byte[2];
            Span<byte> clawBundles = stackalloc byte[2];
            bundleSizes.Clear();
            clawBundles.Clear();
            if (!GenerateScrollBoxes(
                    catalog,
                    plan.Authority.UsesDefectScrollBoxesRule,
                    ref rewards,
                    cards,
                    bundleSizes,
                    clawBundles)) return false;
            MatchScrollBoxesConditions(plan.StructuredConditions, cards, bundleSizes, clawBundles, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.PhialHolster)
        {
            if (!needsCombatPotionGeneration) return true;
            if (!catalog.PotionAuthorityExact) return false;
            ref Beta110FastRng potionRng = ref (plan.Authority.UsesChildShuffle ? ref nonCombat : ref combatPotionGeneration);
            Span<ushort> potions = stackalloc ushort[2];
            potions[0] = RollPotion(catalog.PotionPool, ReadOnlySpan<ushort>.Empty, ref potionRng);
            if (potions[0] == Beta110FastDenseId.Invalid) return false;
            potions[1] = RollPotion(catalog.PotionPool, potions[..1], ref potionRng);
            if (potions[1] == Beta110FastDenseId.Invalid) return false;
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.PhialHolsterPotions,
                plan.StructuredConditions, potions, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.LeafyPoultice)
        {
            if (!needsTransformations) return true;
            if (!catalog.LeafyTransformAuthorityExact ||
                catalog.LeafyStrikeTransformPool.Length == 0 || catalog.LeafyDefendTransformPool.Length == 0) return false;
            Span<ushort> transforms = stackalloc ushort[2];
            DrawLeafyTransforms(catalog, ref transformations, transforms);
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.LeafyPoulticeTransforms,
                plan.StructuredConditions, transforms, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.NewLeaf)
        {
            if (!(plan.Authority.UsesChildShuffle ? needsTransformations : needsNiche)) return true;
            if (!catalog.NewLeafTransformAuthorityExact || catalog.NewLeafTransformPool.Length == 0) return false;
            ushort replacement = DrawNewLeafTransform(catalog, ref (plan.Authority.UsesChildShuffle ? ref transformations : ref niche));
            if ((plan.EnabledDomains & Beta110FastDomain.NewLeafTransform) != 0)
            {
                MatchSingleOfferCondition(relicId, Beta110FastStructuredConditionKind.NewLeafTransform,
                    plan.StructuredConditions, replacement, matched);
            }
            return true;
        }

        // Pure player-choice/deck mutation relics and deterministic no-RNG
        // effects are deliberate continuation no-ops in the Fast RNG surface.
        return true;
    }
}
