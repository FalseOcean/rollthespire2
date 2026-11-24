using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// A reward endpoint contains only a few card groups. Enumerate their finite
// take/sacrifice interleavings; every edge uses detached native settlement.
internal static class PredictorCrystalWing
{
    internal static bool Active(PredictorState state) => PredictorSettlementEffects.Has(state, "PAELS_WING");
    internal static int Parity(PredictorState state) => state.Relics.First(r => !r.Melted && r.Key.Entry == "PAELS_WING").RewardsSacrificed % 2;

    internal static ImmutableHashSet<ModelKey> GiftUpper(PredictorContext context, PredictorState state, int count)
    {
        if (count <= 0) return ImmutableHashSet<ModelKey>.Empty;
        bool Allowed(ModelKey key) => PredictorRewardGeneration.RelicAllowed(context, state, key);
        var bag = state.PersonalBag;
        return bag.Buckets.SelectMany(b => b.Entries.Where(Allowed).Distinct().Take(count))
            .Concat(bag.MultiplayerFallback.Where(Allowed).Distinct().Take(count))
            .Concat(bag.RefreshAllowed ? bag.Original.Select(e => e.Key).Where(Allowed) : [])
            .Append(new ModelKey("RELIC", "CIRCLET")).ToImmutableHashSet();
    }

    // Pruning only. In the captured native regular pool the only AfterObtained
    // overrides are Strawberry/PotionBelt/WarPaint/Whetstone/Pear/Mango/OldCoin.
    // None draws Rewards; W/WP draw Niche. Implemented card insertion likewise
    // leaves Rewards unchanged. Thus takes and Egg/upgrade ordering cannot move
    // these gift identities. Unknown/custom or special obtain scopes retain the
    // optimistic bag bound; every positive still requires ordered settlement.
    internal static ImmutableHashSet<ModelKey> GiftAvailability(PredictorContext context, PredictorState state, int count)
    {
        if(count<=0) return ImmutableHashSet<ModelKey>.Empty;
        if(context.Crystal==null || !context.Crystal.UnmodeledSources.IsEmpty || !PredictableGiftBag(state))
            return GiftUpper(context,state,count);
        return DrawModeledGifts(context,state,count);
    }

    // Query-only evidence under Crystal's captured base-properties /
    // no-custom-hooks premise. Card insertion and supported regular gift
    // obtains preserve Rewards, and remove only the obtained nonstackable key
    // from the bag; eligibility is frozen. Recheck this proof and its negative
    // reuse if a future card/gift handler changes either stream or bag.
    internal static ImmutableHashSet<ModelKey> ModeledGiftAvailability(PredictorContext context,PredictorState state,int count)
    {
        if(count<=0) return ImmutableHashSet<ModelKey>.Empty;
        if(context.Crystal==null || state.PersonalBag.RefreshAllowed && !state.PersonalBag.HasOriginalCatalog)
            return GiftUpper(context,state,count);
        // Unvisited unknown entries cannot affect these fixed pulls. Every
        // actually drawn gift must still satisfy the obtain-effect proof.
        return DrawModeledGifts(context,state,count,checkDrawnGifts:true);
    }

    private static ImmutableHashSet<ModelKey> DrawModeledGifts(PredictorContext context,PredictorState state,int count,bool checkDrawnGifts=false)
    {
        var source=state;
        var gifts=ImmutableHashSet.CreateBuilder<ModelKey>();
        for(int n=0;n<count;n++)
        {
            var key=PredictorRewardGeneration.PullRelic(context,ref state);
            if(checkDrawnGifts && !PredictableGift(key)) return GiftUpper(context,source,count);
            gifts.Add(key);
            // RelicCmd.Obtain removes all instances of a nonstackable key after
            // its selected deque entry was pulled. Circlet remains stackable.
            if(key.Entry!="CIRCLET") state=state with {PersonalBag=state.PersonalBag.Remove(key),SharedBag=state.SharedBag.Remove(key)};
        }
        return gifts.ToImmutable();
    }

    private static bool PredictableGiftBag(PredictorState state)
    {
        var bag=state.PersonalBag;
        if(bag.RefreshAllowed && !bag.HasOriginalCatalog) return false;
        bool Regular(string rarity)=>rarity is "Common" or "Uncommon" or "Rare";
        if(bag.Buckets.Where(b=>Regular(b.Rarity)).SelectMany(b=>b.Entries).Concat(bag.MultiplayerFallback).Any(k=>!PredictableGift(k)) ||
            bag.RefreshAllowed && bag.Original.Where(e=>Regular(e.Rarity)).Any(e=>!PredictableGift(e.Key)))
            return false;
        return true;
    }

    private static bool PredictableGift(ModelKey key)
    {
        if(SpecialGiftRecipes.Contains(key.Entry)) return false;
        // Bag labels are not implementation evidence. A missing handler stays
        // conservative and may still fault on replay; it never proves absence.
        try {PredictorSettlementEffects.RequireImplementedRelic(key);return true;}
        catch(NotImplementedException) {return false;}
    }

    // Nonregular modeled recipes are deliberately outside the consecutive-draw
    // proof, even if a controlled/custom bag labels one with a regular rarity.
    private static readonly HashSet<string> SpecialGiftRecipes=new(StringComparer.Ordinal)
    {
        "NEOWS_TALISMAN", "CURSED_PEARL", "NUTRITIOUS_OYSTER", "BIG_MUSHROOM", "LEES_WAFFLE", "FAKE_LEES_WAFFLE",
        "FAKE_MANGO", "LOOMING_FRUIT", "FRAGRANT_MUSHROOM", "SAND_CASTLE", "SERE_TALON", "DOWSING_ROD",
        "NEOWS_TORMENT", "BLOOD_SOAKED_ROSE", "JEWELRY_BOX", "STORYBOOK", "TANXS_WHISTLE", "PAELS_HORN",
        "SIGNET_RING", "PUMPKIN_CANDLE", "GOLDEN_COMPASS", "FUR_COAT", "BYRDPIP", "PAELS_LEGION",
        "BEAUTIFUL_BRACELET", "PAELS_CLAW", "NUTRITIOUS_SOUP", "TRI_BOOMERANG", "ELECTRIC_SHRYMP", "PAELS_GROWTH",
        "GNARLED_HAMMER", "KIFUDA", "PUNCH_DAGGER", "ROYAL_STAMP", "DOLLYS_MIRROR", "PAELS_TOOTH", "ASTROLABE",
        "CLAWS", "PANDORAS_BOX", "DISTINGUISHED_CAPE", "NEOWS_SACRIFICE", "DUSTY_TOME", "ARCHAIC_TOOTH",
        "TOUCH_OF_OROBAS", "SEA_GLASS", "ALCHEMICAL_COFFER", "NEW_LEAF", "ARCANE_SCROLL", "PHIAL_HOLSTER",
        "LOST_COFFER", "CAULDRON", "SMALL_CAPSULE", "NEOWS_BONES", "LARGE_CAPSULE", "CALLING_BELL", "KALEIDOSCOPE",
        "TOY_BOX", "ORRERY", "GLASS_EYE", "LEAD_PAPERWEIGHT", "HEFTY_TABLET", "MASSIVE_SCROLL", "PRECISE_SCISSORS",
        "BIIIG_HUG", "PRECARIOUS_SHEARS", "EMPTY_CAGE", "PRESERVED_FOG", "POMANDER", "YUMMY_COOKIE", "LEAFY_POULTICE", "SCROLL_BOXES"
    };

    internal static ImmutableArray<CrystalRewardOption> Options(PredictorRun run, bool exactEnchantments)
    {
        int groups = run.Rewards.Count(r => r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open);
        int count = (groups + Parity(run.Working)) / 2;
        var result = ImmutableArray.CreateBuilder<CrystalRewardOption>();
        foreach (var key in GiftAvailability(run.Context, run.Working, count)) result.Add(new(PredictorRewardKind.Relic, key));
        if (count > 0) result.Add(CrystalRewardOption.AnyRelic);
        // Card targets describe the generated offers under the captured state.
        // Newly obtained relic effects still run, but do not expand these goals.
        foreach (var reward in run.Rewards.Where(r => r.Kind == PredictorRewardKind.Card))
            foreach (var card in reward.Cards)
                result.Add(CrystalRewardOption.ForCard(card, exactEnchantments));
        return result.Distinct().ToImmutableArray();
    }

    internal static PredictorCrystalSolution? Verify(PredictorRun entry, CrystalRewardRoute row,
        ImmutableArray<CrystalRewardOption> goals)
    {
        if (entry.Phase != PredictorPhase.Rewards) return null;
        var cards = goals.Where(g => g.Kind == PredictorRewardKind.Card).ToImmutableArray();
        int groups=entry.Rewards.Count(r=>r.Kind==PredictorRewardKind.Card && r.Status==PredictorRewardStatus.Open);
        if(groups<cards.Length) return null;
        var giftUpper=GiftAvailability(entry.Context,entry.Working,(groups-cards.Length+Parity(entry.Working))/2);
        if(goals.Any(g=>g.Kind==PredictorRewardKind.Relic && g.Key.IsValid && !giftUpper.Contains(g.Key) &&
            !entry.Rewards.Any(r=>r.Status==PredictorRewardStatus.Open && g.Matches(r)))) return null;
        var preferredGift=goals.LastOrDefault(g=>g.Kind==PredictorRewardKind.Relic && g.Key.IsValid)?.Key;
        bool requireGift=preferredGift is { } preferred && giftUpper.Contains(preferred);
        // Relic offers remain available. Gifts are already obtained, and cannot
        // be counted twice as separate identities by the set of target options.
        bool NonCards(PredictorRun run, ImmutableHashSet<ModelKey> gifts) => goals.Where(g => g.Kind != PredictorRewardKind.Card).All(g =>
            g.Kind == PredictorRewardKind.Relic && (g.Key.IsValid ? gifts.Contains(g.Key) : !gifts.IsEmpty) ||
            run.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && g.Matches(r)) &&
                (g.Kind != PredictorRewardKind.Potion || !PredictorSettlementEffects.Has(run.Working, "SOZU")));

        PredictorCrystalSolution? Visit(PredictorRun run, ImmutableArray<int> remaining,
            ImmutableArray<PredictorCrystalTake> takes, ImmutableArray<CrystalRewardAction> actions,
            ImmutableHashSet<ModelKey> gifts, ImmutableArray<long> acquired)
        {
            if (remaining.IsEmpty && NonCards(run, gifts) && (!requireGift || gifts.Contains(preferredGift!.Value)) && acquired.All(run.Working.Deck.Contains))
                return new("Found", 0, row.Steps, takes) { RewardActions = actions, SacrificeRelics = gifts };
            var open = run.Rewards.Select((r, i) => (Reward: r, Index: i))
                .Where(p => p.Reward.Kind == PredictorRewardKind.Card && p.Reward.Status == PredictorRewardStatus.Open).ToArray();
            if (open.Length < remaining.Length) return null;
            // Match the original offer, before this suffix obtains any relic.
            // Egg notifications preserve key/order but replace upgraded cards
            // with fresh IDs. Stable positions bind the actual take to its offer.
            foreach (int n in remaining)
                foreach (var (reward, index) in open)
                    foreach (var offer in entry.Rewards[index].Cards.Select((card, position) => (Card: card, Position: position))
                        .Where(p => cards[n].MatchesCard(p.Card)))
                    {
                        if (reward.Cards.Length != entry.Rewards[index].Cards.Length)
                            throw new InvalidOperationException("CrystalWingOfferPositionsChanged");
                        var card = reward.Cards[offer.Position];
                        if (card.Key != offer.Card.Key)
                            throw new InvalidOperationException("CrystalWingOfferIdentityChanged");
                        var next = run.ForkCrystalRewards(); var deck = next.Working.Deck.ToHashSet();
                        if (next.Submit(next.Request!, new EnterCardSelection(reward.Id)) != PredictorInputResult.Accepted ||
                            next.Submit(next.Request!, new TakeReward(reward.Id, card.Id)) != PredictorInputResult.Accepted) continue;
                        var added = next.Working.Deck.Where(id => !deck.Contains(id)).Select(next.Working.Card).ToArray();
                        // The insertion signature includes any Egg/Fresnel hook.
                        foreach (var inserted in added.Where(c => c.Key == cards[n].Key))
                        {
                            var take = new PredictorCrystalTake(index, inserted.Key, inserted.UpgradeLevel,
                                cards[n].Enchantment == null ? null : CrystalCardEnchantment.Of(inserted));
                            var action = new CrystalRewardAction(index, CrystalRewardActionKind.TakeCard,
                                take.Card, take.UpgradeLevel, take.Enchantment);
                            var found = Visit(next, remaining.Remove(n), takes.Add(take), actions.Add(action), gifts,
                                acquired.Add(inserted.Id));
                            if (found != null) return found;
                        }
                    }
            if (open.Length <= remaining.Length) return null;
            foreach (var (reward, index) in open)
            {
                var next = run.ForkCrystalRewards();
                var wing = next.Working.Relics.FirstOrDefault(r => !r.Melted && r.Key.Entry == "PAELS_WING");
                if (wing == null) continue;
                var prior = next.Working.Relics.Select(r => r.Id).ToHashSet();
                if (next.Submit(next.Request!, new EnterCardSelection(reward.Id)) != PredictorInputResult.Accepted ||
                    next.Submit(next.Request!, new SacrificeCardReward(reward.Id, wing.Id)) != PredictorInputResult.Accepted) continue;
                if (next.Phase != PredictorPhase.Rewards || next.CrystalSelectedReward != null ||
                    next.Rewards[index].Status != PredictorRewardStatus.Taken)
                    throw new InvalidOperationException("CrystalWingObtainContinuationRequired");
                var obtained = next.Working.Relics.Where(r => !prior.Contains(r.Id)).Select(r => r.Key).ToArray();
                if (obtained.Length > 1) throw new InvalidOperationException("CrystalWingGiftMultiplicityChanged");
                var gift = obtained.SingleOrDefault();
                var found = Visit(next, remaining, takes,
                    actions.Add(new(index, CrystalRewardActionKind.Sacrifice, Gift: gift)),
                    gift.IsValid ? gifts.Add(gift) : gifts, acquired);
                if (found != null) return found;
            }
            return null;
        }
        var found=Visit(entry.ForkCrystalRewards(), Enumerable.Range(0, cards.Length).ToImmutableArray(), [], [],
            ImmutableHashSet<ModelKey>.Empty, []);
        if(found!=null || !requireGift) return found;
        requireGift=false;
        return Visit(entry.ForkCrystalRewards(), Enumerable.Range(0, cards.Length).ToImmutableArray(), [], [],
            ImmutableHashSet<ModelKey>.Empty, []);
    }
}
