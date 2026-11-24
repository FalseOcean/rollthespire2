namespace RolltheSpire2.Search.FamilyExecution;

internal static class WorldEncounterRankGpuSource
{
    internal const int MaxPoolCount = 32;

    // Pool offset indexes a row descriptor. Each row is indexed by the
    // predecessor's packed encounter index, preserving the exact predecessor
    // even when source ordinal or reference identity is shared.
    internal static uint[] Pack(WorldFastActPlan[] acts, uint[] actMeta, int encounterWordCount)
    {
        const int actStride = 24;
        const int encounterStride = 4;
        var words = Enumerable.Repeat(0u, encounterWordCount / encounterStride).ToList();
        for (int a = 0; a < acts.Length; ++a)
        {
            var act = acts[a];
            int meta = a * actStride;
            int weakOffset = checked((int)actMeta[meta + 3]);
            int regularOffset = checked((int)actMeta[meta + 6]);
            int eliteOffset = checked((int)actMeta[meta + 9]);
            var normalPredecessors = act.WeakEncounters.Concat(act.RegularEncounters).ToArray();
            PackPool(weakOffset, act.WeakEncounters, weakOffset, act.WeakEncounters, act.EncounterConflictSourceOrdinals);
            PackPool(regularOffset, act.RegularEncounters, weakOffset, normalPredecessors, act.EncounterConflictSourceOrdinals);
            PackPool(eliteOffset, act.EliteEncounters, eliteOffset, act.EliteEncounters, act.EncounterConflictSourceOrdinals);
        }
        return words.ToArray();

        void PackPool(int offset, WorldFastEncounterEntry[] pool, int predecessorOffset,
            WorldFastEncounterEntry[] predecessors, int[] conflicts)
        {
            if (pool.Length == 0) return;
            words[offset] = checked((uint)words.Count);
            words.Add(checked((uint)predecessorOffset));
            foreach (var previous in predecessors)
            {
                uint allowed = 0;
                for (int i = 0; i < pool.Length; ++i)
                {
                    var candidate = pool[i];
                    bool sameReference = candidate.ReferenceIdentityId >= 0 &&
                        candidate.ReferenceIdentityId == previous.ReferenceIdentityId;
                    bool conflictsWithPrevious = conflicts.AsSpan(candidate.ConflictOffset, candidate.ConflictCount)
                        .Contains(previous.SourceOrdinal);
                    if (!sameReference && !conflictsWithPrevious) allowed |= 1u << i;
                }
                words.Add(allowed);
            }
        }
    }

    internal static string ReplaceQueue(string source)
    {
        const string first = "bool encounter_eligible(";
        const string last = "bool values_contain(";
        int start = source.IndexOf(first, StringComparison.Ordinal);
        int end = source.IndexOf(last, StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new InvalidOperationException("W.EncounterRankMaskShaderSeam");
        return source[..start] + FamilyGpuComputeUtility.LoadEmbeddedShader("WorldEncounterRank.glsl") + "\n" + source[end..];
    }
}
