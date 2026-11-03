using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Events;

/// <summary>Health commands before hooks, prevention and death are settled by the caller.</summary>
public readonly record struct EventHealthEffect(int HpDelta, int MaxHpDelta, bool FullHeal = false,
    bool AfterCardSelection = false)
{
    public bool RequiresHealthFact => FullHeal || HpDelta != 0 || MaxHpDelta != 0;
}

/// <summary>Native option affordability and the base Spent gold command, before caller-owned hooks.</summary>
public readonly record struct EventGoldCost(int Amount, bool RequiresSufficientGold = true)
{
    public bool CanChoose(int gold) => !RequiresSufficientGold || gold >= Amount;
    public int BalanceAfterPayment(int gold) => Math.Max(0, gold - Amount);
}

public sealed record EventRelicPurchase(EventGoldCost Cost, ModelKey Relic);
public sealed record EventHealthRelicChoice(EventHealthEffect Health, ModelKey Relic);
public sealed record ZenWeaverChoice(EventGoldCost Cost, ModelKey? AddedCard, int AddedCopies, int RemovalCount);
public sealed record ScriptoriumEnchantmentChoice(EventGoldCost Cost, int TargetCount, ModelKey Enchantment, int Amount);
public readonly record struct TabletDecipherChoice(int MaxHpCost, bool ForcedKill, bool UpgradeAll)
{
    public EventHealthEffect HealthBeforeKill(int maxHp) => new(0, -(ForcedKill ? maxHp - 1 : MaxHpCost));
}

/// <summary>
/// Bounded option/effect facts shared by offer construction and explicit settlement.
/// These are commands and products, not an effect runner: selection, RNG, instance
/// identity, payment ordering, health hooks and obtain continuations remain callers' work.
/// </summary>
public static class Beta111EventOptionRules
{
    public static EventHealthRelicChoice BeaconClimb { get; } = new(new(0, -13), new("RELIC", "FRESNEL_LENS"));
    public static ModelKey BeaconBottlePotion { get; } = new("POTION", "GLOWWATER_POTION");
    public static ModelKey UnrestRestCurse { get; } = new("CARD", "POOR_SLEEP");

    public static ModelKey? FixedRelicProduct(string entry, string option) => (entry, option) switch
    {
        ("DROWNING_BEACON", "CLIMB") => BeaconClimb.Relic,
        ("TEA_MASTER", "BONE_TEA" or "EMBER_TEA" or "TEA_OF_DISCOURTESY") => TeaMaster(option).Relic,
        ("SUNKEN_STATUE", "GRAB_SWORD") => new("RELIC", "SWORD_OF_STONE"),
        ("ROUND_TEA_PARTY", "ENJOY_TEA") => new("RELIC", "ROYAL_POISON"),
        ("LOST_WISP", "CLAIM") => new("RELIC", "LOST_WISP"),
        ("COLOSSAL_FLOWER", "POLLINOUS_CORE") => new("RELIC", "POLLINOUS_CORE"),
        ("GRAVE_OF_THE_FORGOTTEN", "ACCEPT") => new("RELIC", "FORGOTTEN_SOUL"),
        ("ROOM_FULL_OF_CHEESE", "SEARCH") => new("RELIC", "CHOSEN_CHEESE"),
        ("WELCOME_TO_WONGOS", "MYSTERY_BOX") => new("RELIC", "WONGOS_MYSTERY_TICKET"),
        ("WAR_HISTORIAN_REPY", "INITIAL_UNLOCK_CAGE" or "SECOND_UNLOCK_CAGE") => new("RELIC", "HISTORY_COURSE"),
        _ => null
    };

    public static ModelKey? FixedCardProduct(string entry, string option) => (entry, option) switch
    {
        ("BYRDONIS_NEST", "TAKE") => new("CARD", "BYRDONIS_EGG"),
        ("THE_LEGENDS_WERE_TRUE", "NAB_THE_MAP") => new("CARD", "SPOILS_MAP"),
        ("SPIRIT_GRAFTER", "LET_IT_IN") => new("CARD", "METAMORPHOSIS"),
        ("BUGSLAYER", "EXTERMINATION") => new("CARD", "EXTERMINATE"),
        ("BUGSLAYER", "SQUASH") => new("CARD", "SQUASH"),
        ("CRYSTAL_SPHERE", "PAYMENT_PLAN") => new("CARD", "DEBT"),
        ("ZEN_WEAVER", "BREATHING_TECHNIQUES") => ZenWeaver(option).AddedCard,
        ("UNREST_SITE", "REST") => UnrestRestCurse,
        ("LOST_WISP", "CLAIM") or ("GRAVE_OF_THE_FORGOTTEN", "CONFRONT") => new("CARD", "DECAY"),
        ("PUNCH_OFF", "NAB") => new("CARD", "INJURY"),
        ("TRIAL", "MERCHANT_INNOCENT") => new("CARD", "SHAME"),
        ("TRIAL", "MERCHANT_GUILTY" or "NOBLE_INNOCENT") => new("CARD", "REGRET"),
        ("TRIAL", "NONDESCRIPT_INNOCENT" or "NONDESCRIPT_GUILTY") => new("CARD", "DOUBT"),
        ("WELLSPRING", "BATHE") => new("CARD", "GUILTY"),
        ("FIELD_OF_MAN_SIZED_HOLES", "RESIST") => new("CARD", "NORMALITY"),
        ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH") => new("CARD", "SPORE_MIND"),
        ("AMALGAMATOR", "COMBINE_STRIKES") => new("CARD", "ULTIMATE_STRIKE"),
        ("AMALGAMATOR", "COMBINE_DEFENDS") => new("CARD", "ULTIMATE_DEFEND"),
        _ => null
    };

    // These products follow target removal/combination. Other fixed cards (for
    // example Trial's curses) precede their picker and must not be added again.
    public static ModelKey? CardProductAfterSelection(string entry, string option) => (entry, option) switch
    {
        ("WELLSPRING", "BATHE") or ("FIELD_OF_MAN_SIZED_HOLES", "RESIST") or
        ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH") or
        ("AMALGAMATOR", "COMBINE_STRIKES" or "COMBINE_DEFENDS") => FixedCardProduct(entry, option),
        _ => null
    };

    public static EventRelicPurchase TeaMaster(string option) => option switch
    {
        "BONE_TEA" => new(new(50), new("RELIC", "BONE_TEA")),
        "EMBER_TEA" => new(new(150), new("RELIC", "EMBER_TEA")),
        "TEA_OF_DISCOURTESY" => new(new(0), new("RELIC", "TEA_OF_DISCOURTESY")),
        _ => throw new ArgumentOutOfRangeException(nameof(option))
    };

    public static ZenWeaverChoice ZenWeaver(string option) => option switch
    {
        // Native offers Breathing Techniques without an affordability lock.
        "BREATHING_TECHNIQUES" => new(new(50, false), new ModelKey("CARD", "ENLIGHTENMENT"), 2, 0),
        "EMOTIONAL_AWARENESS" => new(new(125), null, 0, 1),
        "ARACHNID_ACUPUNCTURE" => new(new(250), null, 0, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(option))
    };

    public static ScriptoriumEnchantmentChoice Scriptorium(string option) => option switch
    {
        "TENTACLE_QUILL" => new(new(55), 1, new("ENCHANTMENT", "STEADY"), 1),
        "PRICKLY_SPONGE" => new(new(99), 2, new("ENCHANTMENT", "STEADY"), 1),
        _ => throw new ArgumentOutOfRangeException(nameof(option))
    };

    public static bool TryGoldCost(string entry, string option, out EventGoldCost cost)
    {
        EventGoldCost? value = (entry, option) switch
        {
            ("TEA_MASTER", "BONE_TEA" or "EMBER_TEA" or "TEA_OF_DISCOURTESY") => TeaMaster(option).Cost,
            ("ZEN_WEAVER", "BREATHING_TECHNIQUES" or "EMOTIONAL_AWARENESS" or "ARACHNID_ACUPUNCTURE") => ZenWeaver(option).Cost,
            ("WATERLOGGED_SCRIPTORIUM", "TENTACLE_QUILL" or "PRICKLY_SPONGE") => Scriptorium(option).Cost,
            _ => null
        };
        cost = value.GetValueOrDefault();
        return value.HasValue;
    }

    public static EventHealthEffect? FixedHealth(string entry, string option) => (entry, option) switch
    {
        ("DROWNING_BEACON", "CLIMB") => BeaconClimb.Health,
        ("UNREST_SITE", "REST") => new(0, 0, FullHeal: true),
        ("UNREST_SITE", "KILL") => new(0, -8),
        ("TABLET_OF_TRUTH", "SMASH") => new(20, 0),
        ("DOLL_ROOM", "TAKE_SOME_TIME") => new(-5, 0),
        ("DOLL_ROOM", "EXAMINE") => new(-15, 0),
        ("WATERLOGGED_SCRIPTORIUM", "BLOODY_INK") => new(6, 6),
        ("THE_LEGENDS_WERE_TRUE", "SLOWLY_FIND_AN_EXIT") => new(-8, 0),
        ("BRAIN_LEECH", "RIP") => new(-5, 0),
        ("BYRDONIS_NEST", "EAT") => new(7, 7),
        ("DENSE_VEGETATION", "TRUDGE_ON") => new(-8, 0),
        ("ROUND_TEA_PARTY", "CONTINUE_FIGHT") => new(-11, 0),
        ("SUNKEN_STATUE", "DIVE_INTO_WATER") => new(-7, 0),
        ("ROOM_FULL_OF_CHEESE", "SEARCH") => new(-14, 0),
        ("SPIRIT_GRAFTER", "LET_IT_IN") => new(25, 0),
        ("SPIRIT_GRAFTER", "REJECTION") => new(-10, 0, AfterCardSelection: true),
        ("MORPHIC_GROVE", "LONER") => new(5, 5),
        ("TRASH_HEAP", "DIVE_IN") => new(-8, 0),
        ("TRIAL", "NOBLE_GUILTY") => new(10, 0),
        ("THIS_OR_THAT", "PLAIN") => new(-6, 0),
        ("SAPPHIRE_SEED", "EAT") => new(9, 0),
        ("STONE_OF_ALL_TIME", "LIFT") => new(10, 10),
        ("STONE_OF_ALL_TIME", "PUSH") => new(-6, 0),
        ("JUNGLE_MAZE_ADVENTURE", "SOLO_QUEST") => new(-18, 0),
        ("WHISPERING_HOLLOW", "HUG") => new(-9, 0, AfterCardSelection: true),
        _ => null
    };

    public static bool RequiresHealthFact(string entry, string option) =>
        FixedHealth(entry, option)?.RequiresHealthFact == true || (entry, option) == ("TABLET_OF_TRUTH", "DECIPHER");

    public static int TabletMaxHpCost(int decipherCount, int? maxHp) => decipherCount switch
    {
        0 => 3, 1 => 6, 2 => 12, 3 => 24,
        4 => (maxHp ?? throw new ArgumentNullException(nameof(maxHp))) - 1,
        _ => throw new ArgumentOutOfRangeException(nameof(decipherCount))
    };

    public static TabletDecipherChoice TabletDecipher(int decipherCount, int maxHp)
    {
        int cost = TabletMaxHpCost(decipherCount, maxHp);
        return new(cost, cost >= maxHp, decipherCount == 4);
    }
}
