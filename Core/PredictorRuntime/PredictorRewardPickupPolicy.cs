using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorRewardPickupPendingReason
{
    PlayerChoice, RandomDeckChange, OrderSensitiveRelic, WaxRelicOrder,
    UnclassifiedRelic, PotionSlotsFull, PotionNotAllowed, ActiveSelection, ActivePhase
}

internal sealed record PredictorRewardPickupPending(long? RewardId, PredictorRewardPickupPendingReason Reason);
internal sealed record PredictorRewardPickupPlan(PredictorInput? NextInput,
    ImmutableArray<PredictorRewardPickupPending> Pending);

// A default pickup recommendation over ALREADY generated rewards. Concrete
// runtime scope is combat; detached previews may explicitly include ordinary
// non-combat relics. It owns no
// settlement, RNG, skipping, card selection or legality authority. Re-plan after
// every accepted input; a manual TakeReward remains available for pending items.
internal static class PredictorRewardPickupPolicy
{
    // TakeGold, AfterRelicAdded and NotifyOpenCardRewards establish these
    // immediate benefits for subsequent pickups, without a new player choice.
    private static readonly ImmutableHashSet<string> BenefitFirst = Set(
        "BOWLER_HAT POTION_BELT DRAGON_FRUIT MOLTEN_EGG TOXIC_EGG FROZEN_EGG");

    // PredictorObtain.BeginObtainRelic and SettlementEffects.AfterRelicAdded:
    // these obtain effects shuffle, transform or randomly alter the current
    // deck. W/WP are Whetstone and War Paint, not Wing Charm/White Star.
    private static readonly ImmutableHashSet<string> RandomDeck = Set(
        "WHETSTONE WAR_PAINT NEOWS_TALISMAN SAND_CASTLE BEAUTIFUL_BRACELET PANDORAS_BOX " +
        "DISTINGUISHED_CAPE ARCHAIC_TOOTH ARCANE_SCROLL");

    // These suspend obtain for card/health/bundle input or open child rewards.
    private static readonly ImmutableHashSet<string> ObtainChoice = Set(
        "LEES_WAFFLE FAKE_LEES_WAFFLE FRAGRANT_MUSHROOM SERE_TALON TRI_BOOMERANG ELECTRIC_SHRYMP " +
        "PAELS_GROWTH GNARLED_HAMMER KIFUDA PUNCH_DAGGER ROYAL_STAMP DOLLYS_MIRROR PAELS_TOOTH " +
        "ASTROLABE CLAWS SEA_GLASS NEW_LEAF LOST_COFFER CAULDRON SMALL_CAPSULE NEOWS_BONES " +
        "LARGE_CAPSULE CALLING_BELL KALEIDOSCOPE TOY_BOX ORRERY GLASS_EYE LEAD_PAPERWEIGHT " +
        "HEFTY_TABLET PRECISE_SCISSORS BIIIG_HUG PRECARIOUS_SHEARS EMPTY_CAGE PRESERVED_FOG " +
        "POMANDER YUMMY_COOKIE LEAFY_POULTICE SCROLL_BOXES TOUCH_OF_OROBAS");

    // Gold/potion denial, finite open-card modifiers, competing enchantments,
    // deck edits or potion generation make the pickup order a player decision.
    private static readonly ImmutableHashSet<string> OrderSensitive = Set(
        "ECTOPLASM SOZU SILKEN_TRESS SILVER_CRUCIBLE WING_CHARM FRESNEL_LENS GLITTER LAVA_LAMP " +
        "BYRDPIP PAELS_CLAW NUTRITIOUS_SOUP NEOWS_SACRIFICE DUSTY_TOME ALCHEMICAL_COFFER PHIAL_HOLSTER");

    internal static PredictorRewardPickupPlan Plan(PredictorRun run) =>
        Plan(run.Working, run.Rewards, run.Phase, run.Selection.CardRewardId != null);

    internal static PredictorRewardPickupPlan Plan(PredictorState state, ImmutableArray<PredictorReward> rewards,
        PredictorPhase phase = PredictorPhase.Rewards, bool activeCardSelection = false,
        bool includeOrdinaryNonCombatRelics = false, bool includeNonCombatPotions = false,
        bool liveMapDefaults = false, PredictorContext? context = null)
    {
        if (phase == PredictorPhase.Complete) return new(null, []);
        if (phase != PredictorPhase.Rewards)
            return new(null, [new(null, PredictorRewardPickupPendingReason.ActivePhase)]);
        if (activeCardSelection)
            return new(null, [new(null, PredictorRewardPickupPendingReason.ActiveSelection)]);

        var pending = ImmutableArray.CreateBuilder<PredictorRewardPickupPending>();
        PredictorReward? first = null, ordinary = null, gold = null, potion = null, exceptional = null;
        foreach (var reward in rewards.Where(r => r.Status == PredictorRewardStatus.Open))
        {
            // Reward provenance owns this boundary, not the current room flag:
            // rest/obtain children can outlive a combat room and may themselves
            // be mandatory. Concrete pickup still requires player input; the
            // explicit preview scope only expands the classified relic subset.
            // Fixed obtains and mandatory continuation remain settlement-owned.
            if (reward.Origin != PredictorRewardOrigin.Combat &&
                !(includeOrdinaryNonCombatRelics && reward.Kind == PredictorRewardKind.Relic) &&
                !(liveMapDefaults && reward.Kind == PredictorRewardKind.Gold) &&
                !(includeNonCombatPotions && reward.Kind == PredictorRewardKind.Potion))
            {
                pending.Add(new(reward.Id, PredictorRewardPickupPendingReason.PlayerChoice));
                continue;
            }
            PredictorRewardPickupPendingReason? reason = null;
            switch (reward.Kind)
            {
                case PredictorRewardKind.Gold:
                    gold ??= reward;
                    break;
                case PredictorRewardKind.Potion:
                    if (PredictorSettlementEffects.Has(state, "SOZU")) reason = PredictorRewardPickupPendingReason.PotionNotAllowed;
                    else if (state.PotionSlots.All(p => p != null)) reason = PredictorRewardPickupPendingReason.PotionSlotsFull;
                    else potion ??= reward;
                    break;
                case PredictorRewardKind.Relic:
                    if (liveMapDefaults && reward.IsWaxRelic) exceptional ??= reward;
                    else if (reward.IsWaxRelic) reason = PredictorRewardPickupPendingReason.WaxRelicOrder;
                    else if (BenefitFirst.Contains(reward.Key.Entry)) first ??= reward;
                    else if (liveMapDefaults && (RandomDeck.Contains(reward.Key.Entry) || ObtainChoice.Contains(reward.Key.Entry) ||
                             OrderSensitive.Contains(reward.Key.Entry))) exceptional ??= reward;
                    else if (RandomDeck.Contains(reward.Key.Entry)) reason = PredictorRewardPickupPendingReason.RandomDeckChange;
                    else if (ObtainChoice.Contains(reward.Key.Entry)) reason = PredictorRewardPickupPendingReason.PlayerChoice;
                    else if (OrderSensitive.Contains(reward.Key.Entry)) reason = PredictorRewardPickupPendingReason.OrderSensitiveRelic;
                    else if (HasSupportedObtain(reward, context)) ordinary ??= reward;
                    else reason = PredictorRewardPickupPendingReason.UnclassifiedRelic;
                    break;
                default:
                    reason = PredictorRewardPickupPendingReason.PlayerChoice;
                    break;
            }
            if (reason is { } value) pending.Add(new(reward.Id, value));
        }
        var next = first ?? ordinary ?? gold ?? potion ?? exceptional;
        return new(next == null ? null : new TakeReward(next.Id), pending.ToImmutable());
    }

    // Deterministic default omission is independent from the observed drop.
    // Callers must establish inventory/relic knowledge before using this to
    // suppress an unresolved pickup dependency in a detached forecast.
    internal static bool IsDefaultPotionOmission(PredictorState state, PredictorReward reward) =>
        reward.Kind == PredictorRewardKind.Potion &&
        (PredictorSettlementEffects.Has(state, "SOZU") || state.PotionSlots.All(p => p != null));

    // Existing supported obtains without an exceptional behavior above keep
    // their original reward order. Unsupported content remains pending locally;
    // this recommendation never turns it into a manual-pickup prohibition.
    private static bool HasSupportedObtain(PredictorReward reward, PredictorContext? context = null)
    {
        try { PredictorSettlementEffects.RequireImplementedRelic(reward.Key, context: context); return true; }
        catch (NotImplementedException) { return false; }
    }

    private static ImmutableHashSet<string> Set(string entries) =>
        entries.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToImmutableHashSet(StringComparer.Ordinal);
}
