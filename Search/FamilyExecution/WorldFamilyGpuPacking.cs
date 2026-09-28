using System.Diagnostics;
using System.Reflection;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// W-owned immutable world/predicate packing for the existing GPU numerical layout.
/// </summary>
internal static class WorldFamilyGpuPacking
{
    private const int ActMetaStrideUInts = 24;
    private const int EncounterStrideUInts = 4;
    private const int PredicateStrideUInts = 7;

    internal static void PackWorldPlan(
        Beta110WorldFastPlan world,
        out uint[] selectionMeta,
        out uint[] selectionIds,
        out uint[] actIndexMap,
        out uint[] actMeta,
        out uint[] encounters,
        out uint[] conflicts,
        out uint[] bossIds,
        out uint[] predicates,
        out uint[] predicateIds)
    {
        var selectionIdList = new List<uint>();
        selectionMeta = new uint[world.ActSelectionGroups.Length * 4];
        for (int index = 0; index < world.ActSelectionGroups.Length; index++)
        {
            WorldFastActSelectionGroup group = world.ActSelectionGroups[index];
            int offset = selectionIdList.Count;
            selectionIdList.AddRange(group.EligibleActIds.Select(value => (uint)value));
            selectionMeta[index * 4] = checked((uint)offset);
            selectionMeta[index * 4 + 1] = checked((uint)group.EligibleActIds.Length);
            selectionMeta[index * 4 + 2] = group.SelectionMode;
            selectionMeta[index * 4 + 3] = checked((uint)group.Act);
        }
        selectionIds = selectionIdList.ToArray();
        actIndexMap = world.ActPlanIndexByDenseId
            .Select(value => value < 0 ? uint.MaxValue : checked((uint)value))
            .ToArray();

        var encounterList = new List<uint>();
        var conflictList = new List<uint>();
        var bossList = new List<uint>();
        var predicateList = new List<uint>();
        var predicateIdList = new List<uint>();
        actMeta = new uint[world.Acts.Length * ActMetaStrideUInts];
        for (int actIndex = 0; actIndex < world.Acts.Length; actIndex++)
        {
            WorldFastActPlan act = world.Acts[actIndex];
            int meta = actIndex * ActMetaStrideUInts;
            actMeta[meta] = checked((uint)act.Act);
            actMeta[meta + 1] = act.ActId;
            actMeta[meta + 2] = checked((uint)act.EligibleEventCount);
            PackEncounterPool(act.WeakEncounters, act.EncounterConflictSourceOrdinals,
                encounterList, conflictList, out int weakOffset);
            actMeta[meta + 3] = checked((uint)weakOffset);
            actMeta[meta + 4] = checked((uint)act.WeakEncounters.Length);
            actMeta[meta + 5] = checked((uint)act.WeakEncounterSlots);
            PackEncounterPool(act.RegularEncounters, act.EncounterConflictSourceOrdinals,
                encounterList, conflictList, out int regularOffset);
            actMeta[meta + 6] = checked((uint)regularOffset);
            actMeta[meta + 7] = checked((uint)act.RegularEncounters.Length);
            actMeta[meta + 8] = checked((uint)act.RegularEncounterSlots);
            PackEncounterPool(act.EliteEncounters, act.EncounterConflictSourceOrdinals,
                encounterList, conflictList, out int eliteOffset);
            actMeta[meta + 9] = checked((uint)eliteOffset);
            actMeta[meta + 10] = checked((uint)act.EliteEncounters.Length);
            actMeta[meta + 11] = checked((uint)act.EliteEncounterSlots);
            int bossOffset = bossList.Count;
            bossList.AddRange(act.Bosses.Select(value => (uint)value));
            actMeta[meta + 12] = checked((uint)bossOffset);
            actMeta[meta + 13] = checked((uint)act.Bosses.Length);
            PackPredicates(act.BossActPredicates, predicateList, predicateIdList,
                out int bossActOffset, out int bossActCount);
            actMeta[meta + 14] = checked((uint)bossActOffset);
            actMeta[meta + 15] = checked((uint)bossActCount);
            PackPredicates(act.BossOrdinalOnePredicates, predicateList, predicateIdList,
                out int bossOneOffset, out int bossOneCount);
            actMeta[meta + 16] = checked((uint)bossOneOffset);
            actMeta[meta + 17] = checked((uint)bossOneCount);
            PackPredicates(act.BossOrdinalTwoPredicates, predicateList, predicateIdList,
                out int bossTwoOffset, out int bossTwoCount);
            actMeta[meta + 18] = checked((uint)bossTwoOffset);
            actMeta[meta + 19] = checked((uint)bossTwoCount);
            actMeta[meta + 20] = checked((uint)act.Ancients.Length);
        }
        encounters = encounterList.ToArray();
        conflicts = conflictList.ToArray();
        bossIds = bossList.ToArray();
        predicates = predicateList.ToArray();
        predicateIds = predicateIdList.ToArray();
    }

    private static void PackEncounterPool(
        WorldFastEncounterEntry[] source,
        int[] sourceConflicts,
        List<uint> encounters,
        List<uint> conflicts,
        out int entryOffset)
    {
        entryOffset = encounters.Count / EncounterStrideUInts;
        foreach (WorldFastEncounterEntry entry in source)
        {
            int conflictOffset = conflicts.Count;
            for (int index = 0; index < entry.ConflictCount; index++)
                conflicts.Add(unchecked((uint)sourceConflicts[entry.ConflictOffset + index]));
            encounters.Add(unchecked((uint)entry.SourceOrdinal));
            encounters.Add(entry.ReferenceIdentityId < 0 ? 0u : checked((uint)entry.ReferenceIdentityId + 1u));
            encounters.Add(checked((uint)conflictOffset));
            encounters.Add(entry.ConflictCount);
        }
    }

    internal static void PackPredicates(
        WorldFastDenseSetPredicate[] source,
        List<uint> predicates,
        List<uint> ids,
        out int predicateOffset,
        out int predicateCount)
    {
        predicateOffset = predicates.Count / PredicateStrideUInts;
        predicateCount = source.Length;
        foreach (WorldFastDenseSetPredicate predicate in source)
        {
            int anyOffset = ids.Count; ids.AddRange(predicate.Any.Select(value => (uint)value));
            int allOffset = ids.Count; ids.AddRange(predicate.All.Select(value => (uint)value));
            int banOffset = ids.Count; ids.AddRange(predicate.Ban.Select(value => (uint)value));
            predicates.Add(predicate.AlwaysReject ? 1u : 0u);
            predicates.Add(checked((uint)anyOffset)); predicates.Add(checked((uint)predicate.Any.Length));
            predicates.Add(checked((uint)allOffset)); predicates.Add(checked((uint)predicate.All.Length));
            predicates.Add(checked((uint)banOffset)); predicates.Add(checked((uint)predicate.Ban.Length));
        }
    }
}
