using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.World;

/// <summary>
/// User-declared assumptions for Ancient option predicates that are simple,
/// binary, and not yet derivable from the immutable route-state snapshot.
/// These values affect only option-pool construction; they never mutate game state.
/// All enabled is the broad/default preset inherited from the validated g10ab5 selector.
/// </summary>
public sealed record AncientOptionConditionProfile(
    bool TezcataraHasBasicStrike,
    bool NonupeipeSwiftEnchantableAtLeast4,
    bool TanxInstinctEnchantableAtLeast3,
    bool PaelGoopyDefendCardsAtLeast3 = true,
    bool PaelAllowLegionNoEventPet = true,
    bool PaelRemovableCardsAtLeast5 = true,
    bool OrobasArchaicToothConditionMet = true,
    bool OrobasTouchOfOrobasConditionMet = true,
    bool DarvAllowPandorasBoxRelicSet = true)
{
    public static IReadOnlyList<ModelKey> AuthoredOptionKeys { get; } = new[]
    {
        Relic("PAELS_CLAW"),
        Relic("PAELS_LEGION"),
        Relic("PAELS_TOOTH"),
        Relic("ARCHAIC_TOOTH"),
        Relic("TOUCH_OF_OROBAS"),
        Relic("NUTRITIOUS_SOUP"),
        Relic("BEAUTIFUL_BRACELET"),
        Relic("TRI_BOOMERANG"),
        Relic("PANDORAS_BOX")
    };

    public static AncientOptionConditionProfile BroadDefault { get; } = new(
        TezcataraHasBasicStrike: true,
        NonupeipeSwiftEnchantableAtLeast4: true,
        TanxInstinctEnchantableAtLeast3: true,
        PaelGoopyDefendCardsAtLeast3: true,
        PaelAllowLegionNoEventPet: true,
        PaelRemovableCardsAtLeast5: true,
        OrobasArchaicToothConditionMet: true,
        OrobasTouchOfOrobasConditionMet: true,
        DarvAllowPandorasBoxRelicSet: true);

    public bool IsBroadDefault =>
        TezcataraHasBasicStrike &&
        NonupeipeSwiftEnchantableAtLeast4 &&
        TanxInstinctEnchantableAtLeast3 &&
        PaelGoopyDefendCardsAtLeast3 &&
        PaelAllowLegionNoEventPet &&
        PaelRemovableCardsAtLeast5 &&
        OrobasArchaicToothConditionMet &&
        OrobasTouchOfOrobasConditionMet &&
        DarvAllowPandorasBoxRelicSet;

    public bool TryGetOptionEligibility(ModelKey option, out bool eligible)
    {
        eligible = option.Entry switch
        {
            "NUTRITIOUS_SOUP" => TezcataraHasBasicStrike,
            "BEAUTIFUL_BRACELET" => NonupeipeSwiftEnchantableAtLeast4,
            "TRI_BOOMERANG" => TanxInstinctEnchantableAtLeast3,
            "PAELS_CLAW" => PaelGoopyDefendCardsAtLeast3,
            "PAELS_LEGION" => PaelAllowLegionNoEventPet,
            "PAELS_TOOTH" => PaelRemovableCardsAtLeast5,
            "ARCHAIC_TOOTH" => OrobasArchaicToothConditionMet,
            "TOUCH_OF_OROBAS" => OrobasTouchOfOrobasConditionMet,
            "PANDORAS_BOX" => DarvAllowPandorasBoxRelicSet,
            _ => true
        };
        return option.Entry is
            "NUTRITIOUS_SOUP" or "BEAUTIFUL_BRACELET" or "TRI_BOOMERANG" or
            "PAELS_CLAW" or "PAELS_LEGION" or "PAELS_TOOTH" or
            "ARCHAIC_TOOTH" or "TOUCH_OF_OROBAS" or "PANDORAS_BOX";
    }

    public AncientOptionConditionProfile WithOptionEligibility(ModelKey option, bool eligible) => option.Entry switch
    {
        "NUTRITIOUS_SOUP" => this with { TezcataraHasBasicStrike = eligible },
        "BEAUTIFUL_BRACELET" => this with { NonupeipeSwiftEnchantableAtLeast4 = eligible },
        "TRI_BOOMERANG" => this with { TanxInstinctEnchantableAtLeast3 = eligible },
        "PAELS_CLAW" => this with { PaelGoopyDefendCardsAtLeast3 = eligible },
        "PAELS_LEGION" => this with { PaelAllowLegionNoEventPet = eligible },
        "PAELS_TOOTH" => this with { PaelRemovableCardsAtLeast5 = eligible },
        "ARCHAIC_TOOTH" => this with { OrobasArchaicToothConditionMet = eligible },
        "TOUCH_OF_OROBAS" => this with { OrobasTouchOfOrobasConditionMet = eligible },
        "PANDORAS_BOX" => this with { DarvAllowPandorasBoxRelicSet = eligible },
        _ => this
    };

    public string Fingerprint => string.Join(
        ":",
        TezcataraHasBasicStrike ? "1" : "0",
        NonupeipeSwiftEnchantableAtLeast4 ? "1" : "0",
        TanxInstinctEnchantableAtLeast3 ? "1" : "0",
        PaelGoopyDefendCardsAtLeast3 ? "1" : "0",
        PaelAllowLegionNoEventPet ? "1" : "0",
        PaelRemovableCardsAtLeast5 ? "1" : "0",
        OrobasArchaicToothConditionMet ? "1" : "0",
        OrobasTouchOfOrobasConditionMet ? "1" : "0",
        DarvAllowPandorasBoxRelicSet ? "1" : "0");

    private static ModelKey Relic(string entry) => new(BaseGameModelKeys.Categories.Relic, entry);
}
