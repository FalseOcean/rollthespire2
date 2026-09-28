using Godot;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;


namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// E-family immutable resource authority.  These two buffers contain only the
/// EventResult plan and the optional Colorful Philosophers character pool.
/// Merchant and Relic resources are deliberately outside this contract.
/// </summary>
internal static class EventResultGpuPacking
{
    private static readonly ulong TrashHeapHash = XxHash64.Hash("TRASH_HEAP"u8, 0UL);
    private static readonly ulong FakeMerchantHash = XxHash64.Hash("FAKE_MERCHANT"u8, 0UL);
    private static readonly ulong ColorfulHash = XxHash64.Hash("COLORFUL_PHILOSOPHERS"u8, 0UL);

    internal const int BufferCount = 2;

    internal static uint[] PackForFamily(IReadOnlyList<EventResultSearchCondition> conditions, Beta111EventResultAuthority? eventAuthority, int playerSlot, out uint[] colorfulPool)
    {
        var output = new List<uint> { 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u };
        foreach (EventResultSearchCondition condition in conditions)
        {
            int target = condition.Kind switch
            {
                EventResultConditionKind.TrashHeapGrabCard => IndexOf(Beta111EventResultCatalog.TrashHeapGrabCards, condition.TargetKey),
                EventResultConditionKind.TrashHeapDiveRelic => IndexOf(Beta111EventResultCatalog.TrashHeapDiveRelics, condition.TargetKey),
                EventResultConditionKind.FakeMerchantOfferedFakeRelic => IndexOf(Beta111EventResultCatalog.FakeMerchantRelics, condition.TargetKey),
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => IndexOf(Beta111EventResultCatalog.ColorfulCharacterOrder, condition.TargetKey),
                _ => -1
            };
            uint kind = condition.Kind switch
            {
                EventResultConditionKind.TrashHeapGrabCard => 0u,
                EventResultConditionKind.TrashHeapDiveRelic => 1u,
                EventResultConditionKind.FakeMerchantOfferedFakeRelic => 2u,
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => 3u,
                _ => byte.MaxValue
            };
            output.Add(kind);
            output.Add(target < 0 ? ushort.MaxValue : checked((uint)target));
        }

        var colors = new List<uint>();
        if (eventAuthority is { ColorfulPoolAuthorityExact: true } authority)
        {
            foreach (ModelKey key in Beta111EventResultCatalog.ColorfulCharacterOrder)
            {
                if (key == authority.OwnerCharacterKey ||
                    !authority.UnlockedCharacterCardPoolKeys.Contains(key)) continue;
                colors.Add(checked((uint)IndexOf(Beta111EventResultCatalog.ColorfulCharacterOrder, key)));
            }
        }
        output[0] = checked((uint)conditions.Count);
        output[1] = checked((uint)colors.Count);
        output[2] = checked((uint)playerSlot);
        WriteU64(output, 3, TrashHeapHash);
        WriteU64(output, 5, FakeMerchantHash);
        WriteU64(output, 7, ColorfulHash);
        colorfulPool = colors.ToArray();
        return output.ToArray();
    }

    private static int IndexOf(IReadOnlyList<ModelKey> values, ModelKey target)
    {
        for (int index = 0; index < values.Count; index++)
            if (values[index] == target) return index;
        return -1;
    }

    private static void WriteU64(List<uint> values, int offset, ulong value)
    {
        values[offset] = unchecked((uint)value);
        values[offset + 1] = unchecked((uint)(value >> 32));
    }
}
