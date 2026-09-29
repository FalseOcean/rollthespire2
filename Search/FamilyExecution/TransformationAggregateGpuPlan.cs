using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed class TransformationAggregateGpuPlan
{
    internal const int WindowCapacity=1<<20;
    internal const int FullTargetCapacity=1<<24;
    internal const string FullTargetRevision="T.Gpu.FullTarget.LeafyFirst.Carry8.20260922.v2";
    internal static string Experiment => Environment.GetEnvironmentVariable("RT2_T_FULL_TARGET_EXPERIMENT") ?? "";
    internal static bool UsesFullTarget(TransformationAggregateNumericalPlan p) => Experiment != "baseline" && p.Closed &&
        p.Condition.Opening == TransformationOpening.LeafyPoultice &&
        p.Condition.Predicate == TransformationAggregatePredicate.ContainsMultiset &&
        p.Condition.TargetMultiset.Count == p.Condition.OpportunityCount;
    internal bool FullTarget { get; }
    internal int SeedsPerInvocation => FullTarget ? 8 : 1;
    internal int Capacity => FullTarget && Experiment != "full1m" ? FullTargetCapacity : WindowCapacity;
    internal uint[][] Buffers { get; }
    internal bool Supported { get; }
    internal TransformationAggregateGpuPlan(TransformationAggregateNumericalPlan p)
    {
        var n=p.Neow; var groups=p.DrawGroups;
        FullTarget = UsesFullTarget(p);
        Supported=p.Closed && (n is null || n.Authority.PlayerSlotIndex == 0 && n.Authority.EligibleCurseRelicIds.Length is >0 and <=32 &&
            (!p.Condition.IsBones || n.Authority.BonesEligibleRelicIds.Length is >=2 and <=32)) && groups.Length<=7 && groups.All(g=>g.Pools.Length<=2);
        var meta=new List<uint>(new uint[32]); var data=new List<uint>();
        uint Add(IEnumerable<uint> values) { uint o=(uint)data.Count;data.AddRange(values);return o; }
        void Hash(int at,ulong hash){meta[at]=(uint)hash;meta[at+1]=(uint)(hash>>32);}
        meta[0]=p.Condition.IsBones ? 3u : (uint)p.Condition.Opening; meta[1]=(uint)p.Condition.Predicate; meta[2]=(uint)p.Condition.MinimumRareCount;
        meta[3]=(uint)p.Condition.OpportunityCount; meta[4]=(uint)p.Condition.TargetMultiset.Count;
        meta[5]=(uint)groups.Length; meta[6]=32;
        Hash(13,XxHash64.Hash("NEOW"u8,0)); Hash(15,NeowFamilyReplay.RewardsHash);
        meta[17]=Beta110FastRelicCatalog.LargeCapsule;meta[18]=Beta110FastRelicCatalog.LeafyPoultice;
        meta[19]=Beta110FastRelicCatalog.NeowsBones;meta[20]=p.Condition.IsBones && Beta110FastRelicCatalog.TryGetId(p.Condition.BonesCompanion,out byte companion) ? companion : Beta110FastRelicCatalog.NewLeaf;
        meta[21]=Beta110FastRelicCatalog.LavaRock;meta[22]=Beta110FastRelicCatalog.SmallCapsule;
        meta[23]=Beta110FastRelicCatalog.NutritiousOyster;meta[24]=Beta110FastRelicCatalog.StoneHumidifier;
        meta[25]=Beta110FastRelicCatalog.NeowsTalisman;meta[26]=Beta110FastRelicCatalog.Pomander;
        if(n is not null)
        {
            var a=n.Authority;meta[7]=(uint)a.EligibleCurseRelicIds.Length;meta[8]=Add(a.EligibleCurseRelicIds.Select(i=>(uint)i));
            meta[9]=(uint)a.BonesEligibleRelicIds.Length;meta[10]=Add(a.BonesEligibleRelicIds.Select(i=>(uint)i));
            meta[11]=(uint)meta.Count;
            foreach(byte curse in a.EligibleCurseRelicIds)
            {
                byte[] positive=NeowFamilyReplay.Positives.ToArray().Where(id=>NeowLocalOperators.IsPositiveAllowed(id,curse,a)).ToArray();
                meta.Add(Add(positive.Select(i=>(uint)i)));meta.Add((uint)positive.Length);
            }
        }
        meta[6]=(uint)meta.Count;
        foreach(var group in groups)
        {
            meta.Add((uint)group.Hash);meta.Add((uint)(group.Hash>>32));meta.Add((uint)group.Prefix);meta.Add((uint)group.Pools.Length);
            foreach(var pool in group.Pools)
            {
                // Low bits identify target instances; bit 31 marks a legal Rare remainder.
                meta.Add(Add(pool.Select(id=>p.Condition.Predicate==TransformationAggregatePredicate.RareCountAtLeast
                    ? (p.IsRare(id)?1u:0u) : p.TargetBits(id) | (p.Condition.RequiresRareRemainder && p.IsRare(id) ? 0x80000000u : 0u))));meta.Add((uint)pool.Length);
            }
            for(int i=group.Pools.Length;i<2;i++){meta.Add(0);meta.Add(0);}
        }
        Buffers=[meta.ToArray(),data.ToArray()];
    }
    internal string ShaderSource()
    {
        // Audited shared seed codec / xxHash / xoshiro body only. No E predicate
        // or N output condition is secretly executed outside the T atom.
        string source=FamilyGpuComputeUtility.LoadEmbeddedShader("Beta111GpuEventResult.comp.glsl");
        string rng=source[source.IndexOf("uint64_t s0;",StringComparison.Ordinal)..source.IndexOf("bool evaluate_event_results",StringComparison.Ordinal)];
        if (FullTarget)
        {
            // Reuse the mature N dense Carry8 codec, without N semantic predicates.
            string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("NeowSingleplayerDonor.glsl");
            rng = "#define T_FULL_TARGET\n" + rng[..rng.IndexOf("uint64_t prime1()",StringComparison.Ordinal)] +
                donor[donor.IndexOf("uint alphabet_byte(",StringComparison.Ordinal)..donor.IndexOf("uint64_t identity_permutation(",StringComparison.Ordinal)];
        }
        return FamilyGpuComputeUtility.LoadEmbeddedShader("TransformationAggregate.comp.glsl").Replace("/*__RNG__*/",rng,StringComparison.Ordinal);
    }
}
