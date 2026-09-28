using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Presentation.Ui1;

internal static class SearchImpossibilityPresentation
{
    public static string Format(SearchImpossibilityProof? proof, IUiTextProvider text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (proof is null) return text.Get(Ui1TextKey.SearchProbabilityImpossible);

        return proof.ReasonCode switch
        {
            SearchImpossibilityReasonCode.BonesExactOrderRequiresTwoRelics =>
                text.Get(Ui1TextKey.SearchImpossibleBonesOrder),
            SearchImpossibilityReasonCode.KaleidoscopeOrderedSlotsInvalid =>
                text.Get(Ui1TextKey.SearchImpossibleKaleidoscopeOrder),
            SearchImpossibilityReasonCode.BonesGrantTargetUnavailable =>
                text.Get(Ui1TextKey.SearchImpossibleBonesTargetUnavailable),
            SearchImpossibilityReasonCode.DarvActConflict =>
                text.Get(Ui1TextKey.SearchImpossibleDarvActConflict),
            SearchImpossibilityReasonCode.SeaGlassTargetWithoutOption =>
                text.Get(Ui1TextKey.SearchImpossibleSeaGlassTargetWithoutOption),
            SearchImpossibilityReasonCode.SequencePositionPrefixConflict =>
                text.Get(Ui1TextKey.SearchImpossibleSequencePositionPrefixConflict),
            SearchImpossibilityReasonCode.IncludeExcludeConflict =>
                text.Get(Ui1TextKey.SearchImpossibleIncludeExcludeConflict),
            SearchImpossibilityReasonCode.OpeningRouteResultParentMismatch =>
                text.Get(Ui1TextKey.SearchImpossibleOpeningRouteParentMismatch),
            SearchImpossibilityReasonCode.CapsuleRelicQueueConflict =>
                text.Get(Ui1TextKey.SearchImpossibleCapsuleRelicQueueConflict),
            SearchImpossibilityReasonCode.CombatCardSequenceInvalid =>
                text.Get(Ui1TextKey.SearchImpossibleCombatCardSequence),
            SearchImpossibilityReasonCode.CombatPotionSequenceInvalid =>
                text.Get(Ui1TextKey.SearchImpossibleCombatPotionSequence),
            SearchImpossibilityReasonCode.ActVariantBossCombinationImpossible =>
                text.Format(Ui1TextKey.SearchImpossibleActVariantBoss, proof.Act ?? 0),
            SearchImpossibilityReasonCode.CombatCardTargetUnavailable when proof.Slot.HasValue =>
                text.Format(Ui1TextKey.SearchImpossibleCombatCardTargetAtSlot, proof.Slot.Value),
            SearchImpossibilityReasonCode.CombatCardTargetUnavailable =>
                text.Get(Ui1TextKey.SearchImpossibleCombatCardTarget),
            SearchImpossibilityReasonCode.CombatPotionTargetUnavailable when proof.Slot.HasValue =>
                text.Format(Ui1TextKey.SearchImpossibleCombatPotionTargetAtSlot, proof.Slot.Value),
            SearchImpossibilityReasonCode.CombatPotionTargetUnavailable =>
                text.Get(Ui1TextKey.SearchImpossibleCombatPotionTarget),
            _ => text.Get(Ui1TextKey.SearchImpossibleCanonicalConflict)
        };
    }
}
