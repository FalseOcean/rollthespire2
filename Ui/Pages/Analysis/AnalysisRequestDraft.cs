using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed record AnalysisRequestDraft(
    string RawSeed,
    ModelKey CharacterKey,
    int Ascension,
    int PlayersCount = 1,
    int PlayerSlotIndex = 0)
{
    public bool TezcataraHasBasicStrike { get; init; } = true;
    public bool NonupeipeSwiftEnchantableAtLeast4 { get; init; } = true;
    public bool TanxInstinctEnchantableAtLeast3 { get; init; } = true;
    public bool PaelGoopyDefendCardsAtLeast3 { get; init; } = true;
    public bool PaelAllowLegionNoEventPet { get; init; } = true;
    public bool PaelRemovableCardsAtLeast5 { get; init; } = true;
    public bool OrobasArchaicToothConditionMet { get; init; } = true;
    public bool OrobasTouchOfOrobasConditionMet { get; init; } = true;
    public bool DarvAllowPandorasBoxRelicSet { get; init; } = true;

    public AncientOptionConditionProfile AncientOptionConditions => new(
        TezcataraHasBasicStrike,
        NonupeipeSwiftEnchantableAtLeast4,
        TanxInstinctEnchantableAtLeast3,
        PaelGoopyDefendCardsAtLeast3,
        PaelAllowLegionNoEventPet,
        PaelRemovableCardsAtLeast5,
        OrobasArchaicToothConditionMet,
        OrobasTouchOfOrobasConditionMet,
        DarvAllowPandorasBoxRelicSet);

    public AnalysisRequestDraft WithAncientConditions(AncientOptionConditionProfile conditions) => this with
    {
        TezcataraHasBasicStrike = conditions.TezcataraHasBasicStrike,
        NonupeipeSwiftEnchantableAtLeast4 = conditions.NonupeipeSwiftEnchantableAtLeast4,
        TanxInstinctEnchantableAtLeast3 = conditions.TanxInstinctEnchantableAtLeast3,
        PaelGoopyDefendCardsAtLeast3 = conditions.PaelGoopyDefendCardsAtLeast3,
        PaelAllowLegionNoEventPet = conditions.PaelAllowLegionNoEventPet,
        PaelRemovableCardsAtLeast5 = conditions.PaelRemovableCardsAtLeast5,
        OrobasArchaicToothConditionMet = conditions.OrobasArchaicToothConditionMet,
        OrobasTouchOfOrobasConditionMet = conditions.OrobasTouchOfOrobasConditionMet,
        DarvAllowPandorasBoxRelicSet = conditions.DarvAllowPandorasBoxRelicSet
    };
}
