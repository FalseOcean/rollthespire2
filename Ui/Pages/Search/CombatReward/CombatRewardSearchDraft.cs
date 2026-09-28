using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Pages.Search.CombatReward;

internal sealed record CombatRewardCardSequenceDraft(
    int Count,
    CombatRewardSequenceOrderMode OrderMode,
    IReadOnlyList<ModelKey?> Slots)
{
    public bool HasAnyValue => Slots.Take(Math.Clamp(Count, 1, 3)).Any(key => key.HasValue);
}

internal sealed record CombatRewardPotionSlotDraft(
    CombatPotionSlotRequirement? Requirement,
    ModelKey? PotionKey)
{
    public bool IsEmpty => !Requirement.HasValue;
}

internal sealed record CombatRewardPotionSequenceDraft(
    int Count,
    CombatRewardSequenceOrderMode OrderMode,
    IReadOnlyList<CombatRewardPotionSlotDraft> Slots)
{
    public bool HasAnyValue => Slots.Take(Math.Clamp(Count, 1, 3)).Any(slot => !slot.IsEmpty);
}

internal sealed record CombatRewardSearchDraft(
    CombatRewardCardSequenceDraft Cards,
    CombatRewardPotionSequenceDraft Potions)
{
    public static CombatRewardSearchDraft Empty { get; } = new(
        new CombatRewardCardSequenceDraft(
            1,
            CombatRewardSequenceOrderMode.Ordered,
            new ModelKey?[] { null, null, null }),
        new CombatRewardPotionSequenceDraft(
            1,
            CombatRewardSequenceOrderMode.Ordered,
            new[]
            {
                new CombatRewardPotionSlotDraft(null, null),
                new CombatRewardPotionSlotDraft(null, null),
                new CombatRewardPotionSlotDraft(null, null)
            }));

    public bool HasAnyValue => Cards.HasAnyValue || Potions.HasAnyValue;
}
