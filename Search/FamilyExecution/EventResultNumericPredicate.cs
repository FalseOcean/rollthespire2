using System.Runtime.CompilerServices;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// E-owned allocation-free event-result predicates. Missing authority cannot reject;
/// Production Exact remains final truth.
/// </summary>
internal static class EventResultNumericPredicate
{
    private static readonly ulong TrashHeapHash = XxHash64.Hash("TRASH_HEAP"u8, 0UL);
    private static readonly ulong FakeMerchantHash = XxHash64.Hash("FAKE_MERCHANT"u8, 0UL);
    private static readonly ulong ColorfulHash = XxHash64.Hash("COLORFUL_PHILOSOPHERS"u8, 0UL);
    private static readonly ulong ShopsHash = XxHash64.Hash("shops"u8, 0UL);

    internal static bool RejectsEventResultForFamily(ulong rootHash, IReadOnlyList<EventResultSearchCondition> conditions,
        Beta111EventResultAuthority? authority, int playerSlot)
    {
        if (authority is null || !authority.IsBeta111 || !authority.StaticCatalogAuthorityExact)
            return false;

        bool needsTrashGrab = false;
        bool needsTrashDive = false;
        bool needsFake = false;
        bool needsColorful = false;
        foreach (EventResultSearchCondition condition in conditions)
        {
            switch (condition.Kind)
            {
                case EventResultConditionKind.TrashHeapGrabCard: needsTrashGrab = true; break;
                case EventResultConditionKind.TrashHeapDiveRelic: needsTrashDive = true; break;
                case EventResultConditionKind.FakeMerchantOfferedFakeRelic: needsFake = true; break;
                case EventResultConditionKind.ColorfulPhilosophersOfferedColor: needsColorful = true; break;
            }
        }

        ModelKey grab = default;
        ModelKey dive = default;
        Span<int> fakeOrder = stackalloc int[9];
        Span<int> colorfulOrder = stackalloc int[5];
        int colorfulCount = 0;

        if (needsTrashGrab)
        {
            var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, shared: false, TrashHeapHash));
            grab = Beta111EventResultCatalog.TrashHeapGrabCards[rng.NextInt(Beta111EventResultCatalog.TrashHeapGrabCards.Count)];
        }
        if (needsTrashDive)
        {
            var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, shared: false, TrashHeapHash));
            dive = Beta111EventResultCatalog.TrashHeapDiveRelics[rng.NextInt(Beta111EventResultCatalog.TrashHeapDiveRelics.Count)];
        }
        if (needsFake)
        {
            for (int i = 0; i < fakeOrder.Length; i++) fakeOrder[i] = i;
            var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, shared: true, FakeMerchantHash));
            rng.UnstableShuffle(fakeOrder);
        }
        if (needsColorful && authority.ColorfulPoolAuthorityExact)
        {
            for (int i = 0; i < Beta111EventResultCatalog.ColorfulCharacterOrder.Count; i++)
            {
                ModelKey candidate = Beta111EventResultCatalog.ColorfulCharacterOrder[i];
                if (candidate == authority.OwnerCharacterKey || !Contains(authority.UnlockedCharacterCardPoolKeys, candidate))
                    continue;
                colorfulOrder[colorfulCount++] = i;
            }
            var rng = ColorfulRng(rootHash, playerSlot);
            int targetCount = Math.Min(3, colorfulCount);
            while (colorfulCount > targetCount)
            {
                int remove = rng.NextInt(colorfulCount);
                for (int i = remove; i < colorfulCount - 1; i++) colorfulOrder[i] = colorfulOrder[i + 1];
                colorfulCount--;
            }
        }

        foreach (EventResultSearchCondition condition in conditions)
        {
            switch (condition.Kind)
            {
                case EventResultConditionKind.TrashHeapGrabCard:
                    if (grab != condition.TargetKey) return true;
                    break;
                case EventResultConditionKind.TrashHeapDiveRelic:
                    if (dive != condition.TargetKey) return true;
                    break;
                case EventResultConditionKind.FakeMerchantOfferedFakeRelic:
                {
                    int targetIndex = IndexOf(Beta111EventResultCatalog.FakeMerchantRelics, condition.TargetKey);
                    if (targetIndex < 0) return true;
                    bool found = false;
                    for (int i = 0; i < 6; i++) found |= fakeOrder[i] == targetIndex;
                    if (!found) return true;
                    break;
                }
                case EventResultConditionKind.ColorfulPhilosophersOfferedColor:
                {
                    // Missing owner/unlock authority cannot produce a Fast rejection.
                    if (!authority.ColorfulPoolAuthorityExact) break;
                    int targetIndex = IndexOf(Beta111EventResultCatalog.ColorfulCharacterOrder, condition.TargetKey);
                    if (targetIndex < 0) return true;
                    bool found = false;
                    for (int i = 0; i < colorfulCount; i++) found |= colorfulOrder[i] == targetIndex;
                    if (!found) return true;
                    break;
                }
            }
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Beta110FastRng ColorfulRng(ulong rootHash, int playerSlot) =>
        new(EventSeed(rootHash, playerSlot, shared: false, ColorfulHash));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong EventSeed(ulong rootHash, int playerSlotIndex, bool shared, ulong eventHash)
    {
        unchecked
        {
            long signed = (long)rootHash + (shared ? 0L : playerSlotIndex);
            return (ulong)signed + eventHash;
        }
    }

    private static bool Contains(IReadOnlyList<ModelKey> values, ModelKey target)
    {
        for (int i = 0; i < values.Count; i++)
            if (values[i] == target) return true;
        return false;
    }

    private static int IndexOf(IReadOnlyList<ModelKey> values, ModelKey target)
    {
        for (int i = 0; i < values.Count; i++)
            if (values[i] == target) return i;
        return -1;
    }
}
