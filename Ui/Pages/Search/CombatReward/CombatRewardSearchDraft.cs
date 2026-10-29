using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Collections;
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
    // A selected collection is an authored snapshot. Editing the library does not
    // change saved presets, history, or an in-flight request.
    public CardCollectionSnapshot? ExcludedCollection { get; init; }
    public int ExclusionBattleCount { get; init; } = 3;

    // Query members may be normalized/deduplicated, but authored snapshots must
    // remain complete and unique. Callers own the policy when no snapshot exists.
    internal bool HasMatchingExclusionSnapshot(CombatCardExclusionSearchCondition? exclusion) =>
        ExcludedCollection is { } snapshot &&
        !string.IsNullOrWhiteSpace(snapshot.Id) && !string.IsNullOrWhiteSpace(snapshot.Name) && snapshot.Revision >= 1 &&
        snapshot.Cards is { Count: > 0 } && snapshot.Cards.All(k => k.IsValid && k.Category == BaseGameModelKeys.Categories.Card) &&
        snapshot.Cards.Distinct(ModelKeyComparer.Instance).Count() == snapshot.Cards.Count &&
        exclusion is { CardKeys: not null } && exclusion.BattleCount == ExclusionBattleCount &&
        new HashSet<ModelKey>(exclusion.CardKeys, ModelKeyComparer.Instance).SetEquals(snapshot.Cards);

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

    public bool HasAnyValue => Cards.HasAnyValue || Potions.HasAnyValue || ExcludedCollection is not null;
}
