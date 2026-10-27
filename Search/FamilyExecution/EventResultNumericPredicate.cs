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
    private static readonly ulong TrialHash = XxHash64.Hash("TRIAL"u8, 0UL);
    private static readonly ulong TinkerTimeHash = XxHash64.Hash("TINKER_TIME"u8, 0UL);

    internal readonly record struct Target(EventResultConditionKind Kind, int Index);

    // E-private request metadata. Pool order and occurrences remain those of the catalog;
    // only fixed identity mapping and group discovery move out of the per-root matcher.
    internal sealed class Compiled
    {
        internal bool CanReject { get; }
        internal bool ColorfulPoolKnown { get; }
        internal bool NeedsTrashGrab { get; }
        internal bool NeedsTrashDive { get; }
        internal bool NeedsFake { get; }
        internal bool NeedsColorful { get; }
        internal EventResultSearchCondition[] CheapConditions { get; }
        internal Target[] TrashTargets { get; }
        internal Target[] InventoryTargets { get; }
        internal int[] ColorfulPool { get; }

        internal Compiled(IReadOnlyList<EventResultSearchCondition> conditions, Beta111EventResultAuthority? authority)
        {
            CanReject = authority is { IsBeta111: true, StaticCatalogAuthorityExact: true };
            ColorfulPoolKnown = authority?.ColorfulPoolAuthorityExact == true;
            CheapConditions = conditions.Where(c => c.Kind is EventResultConditionKind.TrialCase or
                EventResultConditionKind.TinkerTimeTypeAndRider).ToArray();
            var trash = new List<Target>();
            var inventory = new List<Target>();
            foreach (var condition in conditions)
            {
                switch (condition.Kind)
                {
                    case EventResultConditionKind.TrashHeapGrabCard:
                        NeedsTrashGrab = true;
                        trash.Add(new(condition.Kind, IndexOf(Beta111EventResultCatalog.TrashHeapGrabCards, condition.TargetKey)));
                        break;
                    case EventResultConditionKind.TrashHeapDiveRelic:
                        NeedsTrashDive = true;
                        trash.Add(new(condition.Kind, IndexOf(Beta111EventResultCatalog.TrashHeapDiveRelics, condition.TargetKey)));
                        break;
                    case EventResultConditionKind.FakeMerchantOfferedFakeRelic:
                        NeedsFake = true;
                        inventory.Add(new(condition.Kind, IndexOf(Beta111EventResultCatalog.FakeMerchantRelics, condition.TargetKey)));
                        break;
                    case EventResultConditionKind.ColorfulPhilosophersOfferedColor:
                        NeedsColorful = true;
                        inventory.Add(new(condition.Kind, IndexOf(Beta111EventResultCatalog.ColorfulCharacterOrder, condition.TargetKey)));
                        break;
                }
            }
            TrashTargets = trash.ToArray();
            InventoryTargets = inventory.ToArray();
            ColorfulPool = NeedsColorful && CanReject && ColorfulPoolKnown
                ? Beta111EventResultCatalog.ColorfulCharacterOrder
                    .Select((key, index) => (key, index))
                    .Where(candidate => candidate.key != authority!.OwnerCharacterKey &&
                        Contains(authority!.UnlockedCharacterCardPoolKeys, candidate.key))
                    .Select(candidate => candidate.index).ToArray()
                : [];
        }
    }

    internal static bool RejectsEventResultForFamily(ulong rootHash, Compiled compiled, int playerSlot)
    {
        if (!compiled.CanReject) return false;

        foreach (EventResultSearchCondition condition in compiled.CheapConditions)
        {
            switch (condition.Kind)
            {
                case EventResultConditionKind.TrialCase:
                {
                    var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, false, TrialHash));
                    if (rng.NextInt(3) != (int)condition.TrialCase!) return true;
                    break;
                }
                case EventResultConditionKind.TinkerTimeTypeAndRider:
                {
                    var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, false, TinkerTimeHash));
                    Span<int> types = stackalloc int[] { 0, 1, 2 };
                    rng.UnstableShuffle(types);
                    int type = (int)condition.TinkerCardType!;
                    if (types[0] != type && types[1] != type) return true;
                    if (condition.TinkerRider is { } rider)
                    {
                        Span<int> riders = stackalloc int[] { type * 3, type * 3 + 1, type * 3 + 2 };
                        rng.UnstableShuffle(riders);
                        if (riders[0] != (int)rider && riders[1] != (int)rider) return true;
                    }
                    break;
                }
            }
        }

        int grab = -1, dive = -1;
        Span<int> fakeOrder = stackalloc int[9];
        Span<int> colorfulOrder = stackalloc int[5];
        int colorfulCount = 0;

        if (compiled.NeedsTrashGrab || compiled.NeedsTrashDive)
        {
            var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, shared: false, TrashHeapHash));
            // Both projections originally start from the same fresh checkpoint.
            double sample = rng.NextDouble();
            if (compiled.NeedsTrashGrab) grab = (int)(sample * Beta111EventResultCatalog.TrashHeapGrabCards.Count);
            if (compiled.NeedsTrashDive) dive = (int)(sample * Beta111EventResultCatalog.TrashHeapDiveRelics.Count);
        }
        // Preserve the original cheap-group rejection before independent inventories.
        foreach (Target target in compiled.TrashTargets)
        {
            if (target.Index < 0 || (target.Kind == EventResultConditionKind.TrashHeapGrabCard ? grab : dive) != target.Index)
                return true;
        }
        if (compiled.NeedsFake)
        {
            for (int i = 0; i < fakeOrder.Length; i++) fakeOrder[i] = i;
            var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, shared: true, FakeMerchantHash));
            rng.UnstableShuffle(fakeOrder);
        }
        if (compiled.NeedsColorful && compiled.ColorfulPoolKnown)
        {
            compiled.ColorfulPool.AsSpan().CopyTo(colorfulOrder);
            colorfulCount = compiled.ColorfulPool.Length;
            var rng = ColorfulRng(rootHash, playerSlot);
            int targetCount = Math.Min(3, colorfulCount);
            while (colorfulCount > targetCount)
            {
                int remove = rng.NextInt(colorfulCount);
                for (int i = remove; i < colorfulCount - 1; i++) colorfulOrder[i] = colorfulOrder[i + 1];
                colorfulCount--;
            }
        }

        foreach (Target target in compiled.InventoryTargets)
        {
            bool isColorful = target.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor;
            // Missing owner/unlock authority cannot produce a Fast rejection.
            if (isColorful && !compiled.ColorfulPoolKnown) continue;
            if (target.Index < 0) return true;
            bool found = false;
            if (isColorful)
                for (int i = 0; i < colorfulCount; i++) found |= colorfulOrder[i] == target.Index;
            else
                for (int i = 0; i < 6; i++) found |= fakeOrder[i] == target.Index;
            if (!found) return true;
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
