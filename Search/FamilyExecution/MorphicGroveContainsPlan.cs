using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>Morphic numerical donor, shared by the five explicit E transform branches.
/// Captured pool and bounded prefix are configuration, never population payload.</summary>
internal sealed class MorphicGroveContainsPlan
{
    internal ulong EventSeedOffset { get; }
    internal int DrawCount { get; }
    internal uint Prefix { get; } // 0 none, 1 Whisper gold, 2 Trial case==Nondescript
    internal uint[] TargetPositions { get; }
    internal bool RequiresPair { get; }
    private MorphicGroveContainsPlan(uint[] positions, EventResultSearchCondition c) {
        TargetPositions = positions; RequiresPair = c.MorphicGroveSecondCard.HasValue;
        // Morphic is shared; the other admitted event transforms use the owner
        // slot. Capture this immutable seed offset once for CPU and GPU alike.
        EventSeedOffset = unchecked(XxHash64.HashUtf8(EventResultTransformSemantics.Entry(c.Kind), 0) +
            (c.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains ? 0UL :
                (ulong)c.MorphicGroveScenario!.Authority.PlayerSlotIndex));
        DrawCount = EventResultTransformSemantics.DrawCount(c.Kind);
        Prefix = c.Kind == EventResultConditionKind.WhisperingHollowInitialBasicTransform ? 1u :
            c.Kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains ? 2u : 0u;
    }
    internal static MorphicGroveContainsPlan? Compile(EventResultSearchCondition condition) =>
        EventResultTransformSemantics.ClosedPool(condition) is { } pool
            ? new(pool.Select(c => (c.CardKey == condition.TargetKey ? 1u : 0u) |
                (c.CardKey == condition.MorphicGroveSecondCard ? 2u : 0u)).ToArray(), condition) : null;
    internal bool Matches(ulong root)
    {
        var rng = new Beta110FastRng(unchecked(root + EventSeedOffset));
        if (Prefix == 1) rng.NextInt(19);
        if (Prefix == 2 && rng.NextInt(3) != 2) return false;
        uint a = TargetPositions[rng.NextInt(TargetPositions.Length)];
        if (DrawCount == 1) return a != 0;
        if (!RequiresPair) return a != 0 || TargetPositions[rng.NextInt(TargetPositions.Length)] != 0;
        if (a == 0) return false;
        uint b = TargetPositions[rng.NextInt(TargetPositions.Length)];
        return (a & 1) != 0 && (b & 2) != 0 || (a & 2) != 0 && (b & 1) != 0;
    }
}
