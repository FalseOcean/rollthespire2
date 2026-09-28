using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class Beta110GpuAncientOptionPreGateP3AConstants
{
    public const int PlanAbiVersion = 1;
    public const int ShaderAbiVersion = 1;
    public const int WorkgroupSize = 64;
    public const int MaximumInputCapacity = 1 << 20;
    public const int MaximumOutputCapacity = 1 << 20;
    public const long MaximumScratchBytes = 192L * 1024L * 1024L;
    public const int OptionPlanStrideUInts = 16;
    public const int OptionPoolStrideUInts = 4;
    public const int BranchPredicateStrideUInts = 12;
    public const int GateStrideUInts = 4;
    public const int HeaderUIntCount = 10;
    public const uint HeaderMagic = 0x33415047u; // "GPA3"
    public const int HeaderCandidateCountIndex = 4;
    public const int HeaderOverflowIndex = 5;
    public const int HeaderDroppedIndex = 6;
    public const int HeaderProcessedIndex = 7;
    public const int ParitySeedCount = 4_096;
    public const int ParityStrideUInts = 12;
    public const string ShaderResourceSuffix = "Beta110GpuAncientOptionPreGateP3A.comp.glsl";
}

internal sealed record Beta110GpuAncientOptionPreGateGate(
    int Act,
    ushort AncientId,
    WorldFastActPlan ActPlan,
    WorldFastAncientOptionPlan? OptionPlan,
    WorldFastAncientBranchOptionPredicate BranchPredicate,
    bool OptionRejectEnabled,
    bool SeaGlassRejectEnabled);

internal static class AncientOptionGpuPacking
{

    internal static void Pack(IReadOnlyList<Beta110GpuAncientOptionPreGateGate> gates, int maxScratch, int capacity,
        out uint[] planMeta, out uint[] gateMeta, out uint[] optionPlans, out uint[] optionPools,
        out uint[] optionPoolIds, out uint[] otherCharacters, out uint[] branchPredicates, out uint[] branchPredicateIds)
    {
        var plans = new List<uint>(); var pools = new List<uint>(); var poolIds = new List<uint>();
        var others = new List<uint>(); var branches = new List<uint>(); var branchIds = new List<uint>();
        var gm = new List<uint>();
        foreach (var gate in gates)
        {
            uint optionPlanIndex = uint.MaxValue;
            if (gate.OptionPlan is not null)
            {
                WorldFastAncientOptionPlan p = gate.OptionPlan;
                optionPlanIndex = checked((uint)(plans.Count / Beta110GpuAncientOptionPreGateP3AConstants.OptionPlanStrideUInts));
                int poolOffset = pools.Count / Beta110GpuAncientOptionPreGateP3AConstants.OptionPoolStrideUInts;
                foreach (WorldFastAncientOptionPool pool in p.Pools)
                {
                    int valueOffset = poolIds.Count; poolIds.AddRange(pool.Values.Select(v => (uint)v));
                    pools.Add((uint)pool.Role); pools.Add(checked((uint)valueOffset)); pools.Add(checked((uint)pool.Values.Length)); pools.Add(unchecked((uint)pool.SourceOrdinal));
                }
                int otherOffset = others.Count; others.AddRange(p.OtherUnlockedCharacters.Select(v => (uint)v));
                plans.Add(p.AncientId); plans.Add((uint)p.Generator); plans.Add(unchecked((uint)p.PlayerSlot)); plans.Add(p.IsShared ? 1u : 0u);
                plans.Add(unchecked((uint)p.EventEntryHash)); plans.Add(unchecked((uint)(p.EventEntryHash >> 32)));
                plans.Add(checked((uint)poolOffset)); plans.Add(checked((uint)p.Pools.Length));
                plans.Add(checked((uint)otherOffset)); plans.Add(checked((uint)p.OtherUnlockedCharacters.Length));
                plans.Add(p.SeaGlassOptionId); plans.Add(p.SeaGlassTargetAuthorityExact ? 1u : 0u); plans.Add(checked((uint)p.MaxScratchCount)); plans.Add(p.CurrentCharacterId); plans.Add(0u); plans.Add(0u);
            }
            uint branchIndex = checked((uint)(branches.Count / Beta110GpuAncientOptionPreGateP3AConstants.BranchPredicateStrideUInts));
            WorldFastAncientBranchOptionPredicate b = gate.BranchPredicate;
            int optionOffset = branchIds.Count; branchIds.AddRange(b.OptionAny.Select(v => (uint)v));
            int seaOffset = branchIds.Count; branchIds.AddRange(b.SeaGlassTargetAny.Select(v => (uint)v));
            uint flags = (gate.OptionRejectEnabled ? 1u : 0u) | (gate.SeaGlassRejectEnabled ? 2u : 0u) |
                         (b.OptionAlwaysReject ? 4u : 0u) | (b.SeaGlassAlwaysReject ? 8u : 0u);
            branches.Add(b.AncientId); branches.Add(checked((uint)optionOffset)); branches.Add(checked((uint)b.OptionAny.Length));
            branches.Add(checked((uint)seaOffset)); branches.Add(checked((uint)b.SeaGlassTargetAny.Length)); branches.Add(flags);
            branches.Add(b.HasOptionCondition ? 1u : 0u); branches.Add(b.HasSeaGlassCondition ? 1u : 0u);
            // Family-private predicates: conjunction of legacy sets over this same row.
            // Candidate ABI1 remains unchanged.
            branches.Add((uint)branchIds.Count);
            branches.Add((uint)gate.ActPlan.AncientOptionPredicates.Length);
            branches.Add((uint)gate.ActPlan.SeaGlassTargetPredicates.Length);
            branches.Add(0u);
            foreach (var set in gate.ActPlan.AncientOptionPredicates.Concat(gate.ActPlan.SeaGlassTargetPredicates))
            {
                branchIds.Add(set.AlwaysReject ? 1u : 0u);
                branchIds.Add((uint)set.Any.Length); branchIds.Add((uint)set.All.Length); branchIds.Add((uint)set.Ban.Length);
                branchIds.AddRange(set.Any.Select(x => (uint)x));
                branchIds.AddRange(set.All.Select(x => (uint)x));
                branchIds.AddRange(set.Ban.Select(x => (uint)x));
            }
            gm.Add(checked((uint)gate.Act)); gm.Add(gate.AncientId); gm.Add(optionPlanIndex); gm.Add(branchIndex);
        }
        planMeta = new uint[8];
        planMeta[0] = Beta110GpuAncientOptionPreGateP3AConstants.PlanAbiVersion;
        planMeta[1] = Beta110GpuAncientOptionPreGateP3AConstants.ShaderAbiVersion;
        planMeta[2] = checked((uint)gates.Count); planMeta[3] = checked((uint)maxScratch);
        planMeta[4] = checked((uint)capacity); planMeta[5] = Beta110GpuAncientOptionPreGateP3AConstants.ParitySeedCount;
        planMeta[6] = 1u /* ordered ordinal ABI version */; planMeta[7] = 2u /* two uints per ordinal */;
        gateMeta = gm.ToArray(); optionPlans = plans.ToArray(); optionPools = pools.ToArray(); optionPoolIds = poolIds.ToArray();
        otherCharacters = others.ToArray(); branchPredicates = branches.ToArray(); branchPredicateIds = branchIds.ToArray();
    }
}
