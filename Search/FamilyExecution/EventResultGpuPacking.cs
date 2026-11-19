using Godot;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;


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
        var colorOrder = eventAuthority?.ColorfulOrder ?? Beta111EventResultCatalog.ColorfulCharacterOrder;
        ValidateFixedPoolGeometry(conditions, Beta111EventResultCatalog.TrashHeapGrabCards.Count,
            Beta111EventResultCatalog.TrashHeapDiveRelics.Count, Beta111EventResultCatalog.FakeMerchantRelics.Count,
            colorOrder.Count);
        var output = new List<uint> { 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u };
        foreach (EventResultSearchCondition condition in conditions)
        {
            if (EventOpportunitySemantics.IsEventLocalOpportunity(condition.Kind) ||
                condition.Kind == EventResultConditionKind.CrystalSphereRewards)
                throw new NotSupportedException("E.GpuPredicateUnavailable:" + condition.Kind);
            int target = condition.Kind switch
            {
                EventResultConditionKind.TrashHeapGrabCard => IndexOf(Beta111EventResultCatalog.TrashHeapGrabCards, condition.TargetKey),
                EventResultConditionKind.TrashHeapDiveRelic => IndexOf(Beta111EventResultCatalog.TrashHeapDiveRelics, condition.TargetKey),
                EventResultConditionKind.FakeMerchantOfferedFakeRelic => IndexOf(Beta111EventResultCatalog.FakeMerchantRelics, condition.TargetKey),
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => IndexOf(colorOrder, condition.TargetKey),
                EventResultConditionKind.TrialCase => (int)condition.TrialCase!,
                EventResultConditionKind.TinkerTimeTypeAndRider => (int)condition.TinkerCardType! * 4 +
                    (condition.TinkerRider is { } rider ? (int)rider % 3 + 1 : 0),
                _ => -1
            };
            uint kind = condition.Kind switch
            {
                EventResultConditionKind.TrashHeapGrabCard => 0u,
                EventResultConditionKind.TrashHeapDiveRelic => 1u,
                EventResultConditionKind.FakeMerchantOfferedFakeRelic => 2u,
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => 3u,
                EventResultConditionKind.TrialCase => 7u,
                EventResultConditionKind.TinkerTimeTypeAndRider => 8u,
                _ => byte.MaxValue
            };
            output.Add(kind);
            output.Add(target < 0 ? ushort.MaxValue : checked((uint)target));
        }

        // Keep the original header and condition pairs intact. The old predicate
        // ignores this request-only trailer and follows the same payload pointers.
        // [need bits, cheap count, Trash count, grouped original condition indices].
        // Every condition belongs to exactly one group; no predicate is removed.
        uint needs = 0;
        var cheap = new List<uint>();
        var trash = new List<uint>();
        var remaining = new List<uint>();
        for (int i = 0; i < conditions.Count; i++)
        {
            uint kind = output[9 + i * 2];
            if (kind <= 3) needs |= 1u << (int)kind;
            (kind is 7 or 8 ? cheap : kind is 0 or 1 ? trash : remaining).Add(checked((uint)i));
        }
        output.Add(needs);
        output.Add(checked((uint)cheap.Count));
        output.Add(checked((uint)trash.Count));
        output.AddRange(cheap);
        output.AddRange(trash);
        output.AddRange(remaining);

        // Existing E plan buffer owns the bounded two-draw constants. Cross-Family
        // input/output remains ordinal-only, including private execution.
        for (int i = 0; i < conditions.Count; i++)
        {
            if (conditions[i].Kind is EventResultConditionKind.TrialCase or EventResultConditionKind.TinkerTimeTypeAndRider)
            {
                uint value = output[10 + i * 2];
                output[10 + i * 2] = (uint)output.Count;
                ulong hash = XxHash64.HashUtf8(conditions[i].Kind == EventResultConditionKind.TrialCase ? "TRIAL" : "TINKER_TIME", 0);
                output.Add((uint)hash); output.Add((uint)(hash >> 32)); output.Add(value);
                continue;
            }
            if (!EventResultTransformSemantics.IsTransform(conditions[i].Kind)) continue;
            var morphic = MorphicGroveContainsPlan.Compile(conditions[i]);
            if (morphic is null) continue; // This plan is not GPU-admitted.
            output[9 + i * 2] = morphic.DrawCount == 1 ? 6u : morphic.RequiresPair ? 5u : 4u;
            output[10 + i * 2] = checked((uint)output.Count);
            output.Add(unchecked((uint)morphic.EventSeedOffset));
            output.Add(unchecked((uint)(morphic.EventSeedOffset >> 32)));
            output.Add(checked((uint)morphic.TargetPositions.Length));
            output.Add(morphic.Prefix);
            output.AddRange(morphic.TargetPositions);
        }

        var colors = new List<uint>();
        if (eventAuthority is { ColorfulPoolAuthorityExact: true } authority)
        {
            foreach (ModelKey key in Beta111EventRules.ColorfulCandidates(authority.OwnerCharacterKey,
                         authority.UnlockedCharacterCardPoolKeys, authority.RngSemantics))
            {
                colors.Add(checked((uint)IndexOf(colorOrder, key)));
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

    // The packed E predicate owns the stream offset, bounded draw and target
    // mapping. A preceding family may inline it only when this draw completes E.
    internal static FamilyFirstDraw? CompileFirstDraw(EventResultFamilyPlan plan)
    {
        if (!plan.GpuSupported || plan.Conditions.Length != 1) return null;
        uint[] words = plan.Buffers[0];
        uint kind = words[9], target = words[10];
        int count, hashIndex;
        string group;
        ulong slot = words[2];
        Func<int, bool> accepts;
        switch (kind)
        {
            case 0 or 1:
                count = Beta111EventResultCatalog.TrashHeapGrabCards.Count;
                int width = kind == 0 ? 1 : 2;
                if (target >= count / width) return null;
                group = "TrashHeap";
                hashIndex = 3;
                accepts = rank => rank / width == target;
                break;
            case 7:
                group = "Trial";
                hashIndex = checked((int)target);
                count = Beta111EventRules.TrialCaseCount;
                uint requiredCase = words[hashIndex + 2];
                if (requiredCase >= count) return null;
                accepts = rank => rank == requiredCase;
                break;
            case 6:
                hashIndex = checked((int)target);
                if (words[hashIndex + 3] != 0) return null;
                // This payload's immutable offset already includes its owner
                // slot. Each matching position, including duplicates, counts.
                slot = 0;
                count = checked((int)words[hashIndex + 2]);
                group = "InitialBasicTransform";
                accepts = rank => words[hashIndex + 4 + rank] != 0;
                break;
            default:
                return null;
        }

        ulong offset = unchecked((((ulong)words[hashIndex + 1] << 32) | words[hashIndex]) + slot);
        return FamilyFirstDraw.Compile(group, offset, count, accepts);
    }

    // The unchanged shader has fixed scratch sizes and derives Dive by Grab/2.
    // Check only selected observables so a changed pool cannot disable unrelated E paths.
    internal static void ValidateFixedPoolGeometry(IReadOnlyList<EventResultSearchCondition> conditions,
        int trashGrabCount, int trashDiveCount, int fakeCount, int colorfulCount)
    {
        foreach (var condition in conditions)
        {
            string? issue = condition.Kind switch
            {
                EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic
                    when trashGrabCount != 10 || trashDiveCount != 5 => $"TrashHeap:grab={trashGrabCount};dive={trashDiveCount};expected=10/5",
                EventResultConditionKind.FakeMerchantOfferedFakeRelic when fakeCount != 9 => $"FakeMerchant:count={fakeCount};expected=9",
                EventResultConditionKind.ColorfulPhilosophersOfferedColor when colorfulCount is not (5 or 6) => $"ColorfulPhilosophers:count={colorfulCount};expected=5/6",
                _ => null
            };
            if (issue != null) throw new NotSupportedException("E.GpuFixedPoolGeometryUnsupported:" + issue);
        }
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
