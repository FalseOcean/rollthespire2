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
}
