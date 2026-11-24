using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Drawing priorities (H-PATH-DRAW-07). Concrete event execution still owns
// every branch and target; this controls only the live drawing's main choices.
// Gold differences alone are deferred knowledge, alongside low-priority card
// and health variation. Runtime still requires different public RNG, real
// combat/obtain branches and each actual later gold-dependent consumer.
internal sealed record PredictorRouteEventInputPolicy(bool BranchRequired,
    bool SpecificCardTargetRequired = false);

internal static class PredictorRouteEventPolicy
{
    public static IReadOnlyDictionary<string, PredictorRouteEventInputPolicy> All { get; } =
        new Dictionary<string, PredictorRouteEventInputPolicy>(StringComparer.Ordinal)
        {
            ["ABYSSAL_BATHS"] = new(false),
            ["AMALGAMATOR"] = new(false),
            ["AROMA_OF_CHAOS"] = new(false),
            ["BATTLEWORN_DUMMY"] = new(true),
            ["BRAIN_LEECH"] = new(true),
            ["BUGSLAYER"] = new(false),
            ["BYRDONIS_NEST"] = new(false),
            ["COLORFUL_PHILOSOPHERS"] = new(false),
            ["COLOSSAL_FLOWER"] = new(true),
            ["CRYSTAL_SPHERE"] = new(true),
            ["DENSE_VEGETATION"] = new(true),
            ["DOLL_ROOM"] = new(true),
            ["DOORS_OF_LIGHT_AND_DARK"] = new(false),
            ["DROWNING_BEACON"] = new(true),
            ["ENDLESS_CONVEYOR"] = new(true),
            // Owner default: do not shop or fight, even with a Foul Potion.
            // Purchases and combat remain explicit overrides of that default.
            ["FAKE_MERCHANT"] = new(false),
            ["FIELD_OF_MAN_SIZED_HOLES"] = new(false),
            ["GRAVE_OF_THE_FORGOTTEN"] = new(false),
            ["HUNGRY_FOR_MUSHROOMS"] = new(false),
            ["INFESTED_AUTOMATON"] = new(false),
            ["JUNGLE_MAZE_ADVENTURE"] = new(false),
            ["LOST_WISP"] = new(true),
            ["LUMINOUS_CHOIR"] = new(true),
            ["MORPHIC_GROVE"] = new(false),
            ["POTION_COURIER"] = new(true),
            ["PUNCH_OFF"] = new(true),
            ["RANWID_THE_ELDER"] = new(true),
            ["REFLECTIONS"] = new(false),
            ["RELIC_TRADER"] = new(true),
            ["ROOM_FULL_OF_CHEESE"] = new(true),
            ["ROUND_TEA_PARTY"] = new(true),
            ["SAPPHIRE_SEED"] = new(false),
            ["SELF_HELP_BOOK"] = new(false),
            ["SLIPPERY_BRIDGE"] = new(false),
            ["SPIRALING_WHIRLPOOL"] = new(false),
            ["SPIRIT_GRAFTER"] = new(false),
            ["STONE_OF_ALL_TIME"] = new(false),
            ["SUNKEN_STATUE"] = new(true),
            ["SUNKEN_TREASURY"] = new(false),
            ["SYMBIOTE"] = new(false),
            ["TABLET_OF_TRUTH"] = new(false),
            ["TEA_MASTER"] = new(true),
            ["THE_FUTURE_OF_POTIONS"] = new(false),
            ["THE_LANTERN_KEY"] = new(true),
            ["THE_LEGENDS_WERE_TRUE"] = new(true),
            ["THIS_OR_THAT"] = new(true),
            ["TINKER_TIME"] = new(false),
            ["TRASH_HEAP"] = new(true),
            ["TRIAL"] = new(true),
            ["UNREST_SITE"] = new(true),
            ["WAR_HISTORIAN_REPY"] = new(true),
            ["WATERLOGGED_SCRIPTORIUM"] = new(false),
            ["WELCOME_TO_WONGOS"] = new(true),
            ["WELLSPRING"] = new(true),
            ["WHISPERING_HOLLOW"] = new(false),
            ["WOOD_CARVINGS"] = new(false),
            ["ZEN_WEAVER"] = new(false)
        };

    public static PredictorRouteEventInputPolicy For(ModelKey? eventKey) =>
        eventKey is { Category: "EVENT" } key && All.TryGetValue(key.Entry, out var policy)
            ? policy : new(true, SpecificCardTargetRequired: true);
}
