using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Private, immutable facts for the bounded Large->K arrival. A Capsule has two
// distinct bag draws; authored targets occupy slots, but never constrain the
// identities of the remaining slots. No deck or mutable bag crosses ABI1.
internal sealed record BonesHiddenUpgradeContinuation(int Mode, int MetadataOffset, uint[] Metadata)
{
    internal const string Revision = ".HiddenUpgradeArrival.PinnedAndCpu.20261101.v2";
    internal const string KnownFullGpuRevision = ".KnownFullAuthored.20261101.v1";
    internal string ModeName => Mode switch { 1 => "direct", 2 => "branches", 3 => "direct-first", _ => "legacy" };
    internal bool Ready => Metadata[0] != uint.MaxValue;
    internal int AttackDraws => unchecked((int)Metadata[0]);
    internal int SkillDraws => unchecked((int)Metadata[1]);
    internal uint ExplicitMask => Metadata[2];
    internal int UnknownSlots => checked((int)Metadata[3]);
    internal bool KnownFull => Ready && UnknownSlots == 0;
    // The bounded conservative union wins the complete Search comparison.
    // Keep explicit controls for reproducible recall/performance diagnostics.
    internal static int CaptureMode() => (Environment.GetEnvironmentVariable("RT2_BONES_HIDDEN_UPGRADES") ?? "branches") switch
    {
        "legacy" => 0, "direct" => 1, "" or "branches" => 2, "direct-first" => 3,
        var value => throw new InvalidOperationException("BonesK.UnknownHiddenUpgradeMode:" + value)
    };

    internal static BonesHiddenUpgradeContinuation Compile(SearchModel request, RelicFullGpuPlan plan, int mode)
    {
        var row = request.Evaluation.StructuredNeowEffects.Single(c => !c.IsEmpty && c.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule);
        bool exact = NeowCapsuleNicheContinuation.TryInitialCounts(request,out int attacks,out int skills);
        uint[] facts = new uint[10];
        facts[0] = exact ? (uint)NeowAuthoredUpgradeContinuation.Draws(attacks) : uint.MaxValue;
        facts[1] = exact ? (uint)NeowAuthoredUpgradeContinuation.Draws(skills) : uint.MaxValue;
        facts[2] = (row.OutputKeys.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone) ? 1u : 0u) |
                   (row.OutputKeys.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint) ? 2u : 0u);
        facts[3] = checked((uint)(2 - row.OutputKeys.Count));
        Array.Fill(facts, uint.MaxValue, 4, 6);
        if (!RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var dense, out _, out _)) facts[0] = uint.MaxValue;
        else
        {
            ModelKey[] keys = [BaseGameModelKeys.OrdinaryRelics.Whetstone, BaseGameModelKeys.OrdinaryRelics.WarPaint];
            for (int i = 0; i < keys.Length; i++)
            {
                if (!dense.TryGetValue(keys[i], out ushort id)) { facts[0]=uint.MaxValue; continue; }
                for (int b = 0; b < pool.BucketCount; b++)
                {
                    if (pool.BucketScopes[b] != 1 || pool.BucketKinds[b] is < 1 or > 3) continue;
                    int at = Array.IndexOf(pool.DenseRelicIds, id, pool.BucketOffsets[b], pool.BucketLengths[b]);
                    if (at < 0) continue;
                    facts[4 + i * 3] = (uint)b; facts[5 + i * 3] = (uint)(at - pool.BucketOffsets[b]); facts[6 + i * 3] = pool.BucketKinds[b];
                    break;
                }
                // A missing observation is local to this continuation. Retain its
                // K predicate for Exact rather than infer absence from missing data.
                if (facts[4 + i * 3] == uint.MaxValue) facts[0] = uint.MaxValue;
            }
        }
        return new(mode, plan.CapsuleMetadata.Length, facts);
    }

    internal uint[] AppendTo(uint[] metadata) => [..metadata, ..Metadata];
    internal bool PossibleMask(uint mask) => (mask & ExplicitMask) == ExplicitMask &&
        System.Numerics.BitOperations.PopCount(mask & ~ExplicitMask) <= UnknownSlots;
    internal int Advance(uint mask) => ((mask & 1) != 0 ? AttackDraws : 0) + ((mask & 2) != 0 ? SkillDraws : 0);
    internal bool LargeFirstMatches(ulong root, NeowReplayPlan n, Beta110FastRng rewards, uint[] capsuleMetadata)
    {
        if (!Ready) return n.CapsuleNicheContinuation is null || NeowFamilyReplay.Route(root,n,
            Beta110FastRelicCatalog.LargeCapsule,Beta110FastRelicCatalog.Kaleidoscope,rewards);
        if (UnknownSlots == 0) return NeowSingleplayerReplay.RouteAtArrival(root, n, Beta110FastRelicCatalog.LargeCapsule,
            Beta110FastRelicCatalog.Kaleidoscope, rewards, Advance(ExplicitMask), 0);
        if (Mode is 1 or 3) return NeowSingleplayerReplay.RouteAtArrival(root, n, Beta110FastRelicCatalog.LargeCapsule,
            Beta110FastRelicCatalog.Kaleidoscope, rewards, Advance(ActualMask(root, rewards, capsuleMetadata)), 0);
        if(n.CapsuleNicheContinuation is not null) return NeowFamilyReplay.Route(root,n,
            Beta110FastRelicCatalog.LargeCapsule,Beta110FastRelicCatalog.Kaleidoscope,rewards);
        for (uint mask = 0; mask < 4; mask++)
            if (PossibleMask(mask) && NeowSingleplayerReplay.RouteAtArrival(root, n, Beta110FastRelicCatalog.LargeCapsule,
                Beta110FastRelicCatalog.Kaleidoscope, rewards, Advance(mask), 0)) return true;
        return false;
    }

    // CPU diagnostic projection mirrors tracked ranks, independently of legacy N.
    internal uint ActualMask(ulong root, Beta110FastRng rewards, uint[] capsuleMetadata)
    {
        Span<uint> rarities = stackalloc uint[2];
        for (int i = 0; i < 2; i++) { float roll = rewards.NextFloat(); rarities[i] = roll < .5f ? 1u : roll < .83f ? 2u : 3u; }
        uint p = Metadata[5], q = Metadata[8];
        int last = checked((int)Math.Max(Metadata[4], Metadata[7]));
        var rng = new Beta110FastRng(unchecked(root + XxHash64.Hash("up_front"u8, 0)));
        for (int bucket = 0; bucket <= last; bucket++)
        for (int remaining = checked((int)capsuleMetadata[106 + bucket]); remaining > 1; remaining--)
        {
            uint selected = (uint)rng.NextInt(remaining), tail = (uint)(remaining - 1);
            if (bucket == Metadata[4]) Swap(ref p, selected, tail);
            if (bucket == Metadata[7]) Swap(ref q, selected, tail);
        }
        uint mask = 0;
        for (int i = 0; i < 2; i++)
        {
            uint ordinal = i == 1 && rarities[0] == rarities[1] ? 1u : 0u;
            if (rarities[i] == Metadata[6] && ordinal == p) mask |= 1;
            if (rarities[i] == Metadata[9] && ordinal == q) mask |= 2;
        }
        return mask;
    }
    private static void Swap(ref uint position, uint selected, uint tail)
    { if (position == selected) position = tail; else if (position == tail) position = selected; }
}
