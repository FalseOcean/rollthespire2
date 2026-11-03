using System.Numerics;

namespace RolltheSpire2.Search.FamilyExecution;

[Flags]
internal enum NeowEntryIdentityMode { Off = 0, RejectUnavailable = 1, TrackSingleRequired = 2 }

// Extend RT2's existing Bones/T target-position tracking to ordinary positive
// entry identities. Tracking belongs to one immutable GPU plan and is enabled
// where applicable; additional unavailable-target rejection remains opt-in.
internal static class NeowEntryIdentityOptimization
{
    internal static NeowEntryIdentityMode Capture() => Parse(Environment.GetEnvironmentVariable("RT2_N_ENTRY_EXPERIMENT"));
    internal static NeowEntryIdentityMode Parse(string? mode) => mode switch
    {
        null or "" or "default" => NeowEntryIdentityMode.TrackSingleRequired,
        "off" => NeowEntryIdentityMode.Off,
        "reject" => NeowEntryIdentityMode.RejectUnavailable,
        "track" => NeowEntryIdentityMode.TrackSingleRequired,
        "all" => NeowEntryIdentityMode.RejectUnavailable | NeowEntryIdentityMode.TrackSingleRequired,
        _ => throw new InvalidOperationException("UnknownNEntryExperiment:" + mode)
    };

    internal static NeowFamilyGpuPlan Apply(NeowReplayPlan plan, NeowFamilyGpuPlan gpu, NeowEntryIdentityMode mode)
    {
        if (mode == NeowEntryIdentityMode.Off) return gpu;
        var authority = plan.Authority;
        if (authority.PlayersCount != 1 || !authority.IdentityAuthorityExact) return gpu;
        // The copied solo identity ABI has ten fixed curse identities. The
        // eligible pool may be any admitted subset or ordering of these IDs.
        if (authority.EligibleCurseRelicIds.Any(id => id >= 10)) return gpu;
        const ulong curseMask = (1UL << 10) - 1;
        ulong selected = plan.Selected == 255 ? 0 : Beta110FastRelicCatalog.Bit(plan.Selected);
        ulong positiveRequired = (plan.TopAny | plan.TopAll | selected) & ~curseMask;
        bool track = (mode & NeowEntryIdentityMode.TrackSingleRequired) != 0 &&
            (plan.TopBan & ~curseMask) == 0 && BitOperations.PopCount(positiveRequired) == 1;
        byte target = track ? checked((byte)BitOperations.TrailingZeroCount(positiveRequired)) : (byte)255;
        // Single-target tracking deliberately leaves multi-target Any/All/Ban
        // matching in the original full-shuffle implementation.
        var metadata = track ? new uint[20] : [];
        uint rejected = 0;
        for (int ordinal = 0; ordinal < authority.EligibleCurseRelicIds.Length; ordinal++)
        {
            byte curse = authority.EligibleCurseRelicIds[ordinal];
            ulong possible = Beta110FastRelicCatalog.Bit(curse);
            uint fixedCount = 0, targetPosition = uint.MaxValue;
            foreach (byte id in authority.Identity.ForCurse(curse).FixedCandidates)
            {
                possible |= Beta110FastRelicCatalog.Bit(id);
                if (id == target) targetPosition = fixedCount;
                fixedCount++;
            }
            foreach (var choice in authority.Identity.ForCurse(curse).AppendChoices)
                possible |= Beta110FastRelicCatalog.Bit(choice.TrueCandidate) | Beta110FastRelicCatalog.Bit(choice.FalseCandidate);
            // Only impossibility is inferred from this union. It does not prove
            // co-offerability, nor may a possible partial request be accepted.
            if ((mode & NeowEntryIdentityMode.RejectUnavailable) != 0 &&
                ((plan.TopAny != 0 && (plan.TopAny & possible) == 0) ||
                 (plan.TopAll & possible) != plan.TopAll || (selected & possible) != selected))
                rejected |= 1u << ordinal;
            if (track)
            {
                metadata[curse * 2] = fixedCount;
                metadata[curse * 2 + 1] = targetPosition;
            }
        }
        uint[] meta = [..gpu.Meta, ..metadata];
        meta[69] |= rejected;
        meta[68] &= ~rejected;
        var applied = mode & NeowEntryIdentityMode.RejectUnavailable;
        if (track) applied |= NeowEntryIdentityMode.TrackSingleRequired;
        return gpu with { Meta = meta, EntryIdentityMode = applied, EntryIdentityTarget = target,
            EntryIdentityMetaOffset = track ? gpu.Meta.Length : -1 };
    }
}
