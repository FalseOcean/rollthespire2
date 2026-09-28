using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial record RelicFullGpuPlan
{
    // General R-owned arrival replay supplements the existing tracked-rank fast
    // specialization. No mutable bag or Rewards state leaves this invocation.
    private static bool TryCreateGeneric(ExactSearchExecutionRequest request, NeowReplayPlan route,
        out RelicFullGpuPlan? plan, out string issue)
    {
        plan = null; issue = "";
        var a = route.Authority; var catalog = a.EffectCatalog;
        if (!a.IdentityAuthorityExact || a.EligibleCurseRelicIds.Length is < 1 or > 10 ||
            route.Bones && (!a.BonesAuthorityExact || a.BonesEligibleRelicIds.Length is < 2 or > 64))
            return Unsupported("R.GenericOpeningAuthority", out issue);
        if (!route.Bones && route.Selected is not (Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule))
            return Unsupported("R.GenericCapsuleSource", out issue);
        // K's six independent cards consume exactly 18 Rewards draws, so R
        // does not require a shared Niche checkpoint to observe Capsule contents.
        if (!catalog.CharacterRewardAuthorityExact || !catalog.ColorlessRewardAuthorityExact || !catalog.PotionAuthorityExact ||
            catalog.OtherCharacterPools.Length < 3 || catalog.OtherCharacterPools.Any(p => p.TotalCount == 0))
            return Unsupported("R.GenericOpeningPools", out issue);
        if (!RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var dense, out _, out issue)) return false;
        if (Enumerable.Range(0, pool.BucketCount).Where(i => pool.BucketScopes[i] == 1 && pool.BucketKinds[i] <= 3)
            .Sum(i => pool.BucketLengths[i]) > 512) return Unsupported("R.GenericPersonalBagCapacity", out issue);
        RelicFamilyPlan sequence;
        if (request.Evaluation.RequiresRelicSequenceDomain)
        {
            if (!RelicFamilyPlanCompiler.TryCompile(request, out var p, out issue) || p is null) return false;
            sequence = p;
        }
        else sequence = new(pool, [], [], [], new byte[4], new byte[4], [], new int[4], new byte[4], 0, false);
        var meta = new List<uint>(new uint[128]);
        uint Store(IEnumerable<uint> values) { uint at = (uint)meta.Count; meta.AddRange(values); return at; }
        void Hash(int at, ulong value) { meta[at] = (uint)value; meta[at + 1] = (uint)(value >> 32); }
        meta[0] = 4; meta[1] = route.Selected; meta[14] = (uint)a.PlayerSlotIndex; meta[15] = (uint)a.PlayersCount;
        meta[16] = (a.AllCharacterCardPoolsUnlocked ? 4u : 0) | (a.ScrollBoxesAllowed ? 8u : 0);
        meta[17] = (uint)a.EligibleCurseRelicIds.Length;
        Hash(18, XxHash64.Hash("NEOW"u8, 0)); Hash(20, NeowFamilyReplay.RewardsHash); Hash(22, XxHash64.Hash("up_front"u8, 0));
        meta[24] = route.Bones ? (uint)a.BonesEligibleRelicIds.Length : 0;
        meta[29] = route.First != 255 ? 1u : 0; meta[30] = route.First; meta[31] = route.Second;
        for (int i = 0; i < a.EligibleCurseRelicIds.Length; i++) meta[32 + i] = a.EligibleCurseRelicIds[i];
        for (int i = 0; i < a.BonesEligibleRelicIds.Length; i++) meta[42 + i] = a.BonesEligibleRelicIds[i];
        Hash(114, route.BonesAny); Hash(116, route.BonesAll); Hash(118, route.BonesBan);
        var cmeta = new uint[55]; cmeta[2] = (uint)a.PlayerSlotIndex; cmeta[3] = (uint)a.PlayersCount; cmeta[4] = (uint)a.Ascension;
        cmeta[6] = a.UsesDefectScrollBoxesRule ? 4u : 0;
        void CH(int at, ulong value) { cmeta[at] = (uint)value; cmeta[at + 1] = (uint)(value >> 32); }
        CH(14, NeowFamilyReplay.RewardsHash); CH(16, XxHash64.Hash("niche"u8, 0));
        var pools = CombatRewardGpuPacking.BuildPoolMeta(catalog with { OrdinaryRelics = [], PlayerRelicBuckets = [], SharedRelicConsumeShuffleLengths = [] }, out var ids);
        meta[106] = Store(cmeta); meta[107] = Store(pools); meta[108] = Store(ids);
        var circlet = new ModelKey("RELIC", "CIRCLET");
        meta[111] = dense.TryGetValue(circlet, out var ci) ? ci : (uint)dense.Count;
        uint Id(ModelKey key) => key == circlet ? meta[111] : dense.TryGetValue(key, out var id) ? id : uint.MaxValue;
        var rows = new List<uint>();
        void Row(uint source, uint mode, IEnumerable<ModelKey> keys)
        {
            var targets = keys.Select(Id).ToArray(); if (targets.Length == 0) return;
            rows.AddRange([source, mode, (uint)targets.Length, Store(targets)]);
        }
        foreach (var c in route.Filter.StructuredNeowEffects)
        {
            if (NeowReplayPlan.IsGroupedCapsule(c)) Row(255, 0, c.OutputKeys);
            else if (Beta110FastRelicCatalog.TryGetId(c.SourceRelicKey, out byte source)) Row(source, 0, c.OutputKeys);
        }
        Row(254, 0, request.Evaluation.CapsuleContainedRelics.All.Distinct());
        Row(254, 1, request.Evaluation.CapsuleContainedRelics.Any);
        Row(254, 2, request.Evaluation.CapsuleContainedRelics.Ban);
        if (request.Evaluation.RequireWhetstone) Row(254, 0, [BaseGameModelKeys.OrdinaryRelics.Whetstone]);
        if (request.Evaluation.RequireWarPaint) Row(254, 0, [BaseGameModelKeys.OrdinaryRelics.WarPaint]);
        meta[109] = Store(rows); meta[110] = (uint)(rows.Count / 4);
        var unresolved = RolltheSpire2.Search.Semantics.PartyInitialQuery.CapsuleEffectPremise(request.CompiledSearch.NormalizedQuery)
            .Values.SelectMany(keys => keys)
            .Where(k => !VanillaRelicRewardEffects.TryGet(request.ProfileId, k, out var e) || !e.NestedOnObtainPreservesRewardContinuation)
            .Distinct().Select(Id).ToArray();
        meta[120] = Store(unresolved); meta[121] = (uint)unresolved.Length;
        plan = new(sequence, meta.ToArray()) { GenericReplay = true };
        return true;
    }

    internal static string GenericShaderFunctions()
    {
        string top = FamilyGpuComputeUtility.LoadEmbeddedShader("RelicFullCapsule.glsl");
        top = top[..top.IndexOf("// Leaves the global RNG", StringComparison.Ordinal)];
        string draws = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta110GpuCombatRewardCommon.glsl");
        draws = draws[..draws.IndexOf("bool cr_execute_non_capsule_relic", StringComparison.Ordinal)];
        draws = draws.Replace("plan_meta.values[", "rfull_capsule.values[rfull_meta(106u)+", StringComparison.Ordinal)
            .Replace("pool_meta.values[", "rfull_capsule.values[rfull_meta(107u)+", StringComparison.Ordinal)
            .Replace("dense_ids.ids[", "rfull_capsule.values[rfull_meta(108u)+", StringComparison.Ordinal)
            .Replace("bones_ids.ids[", "rfull_capsule.values[42u+", StringComparison.Ordinal)
            .Replace("capability_meta.values[", "rfull_capsule.values[", StringComparison.Ordinal);
        return top + draws + FamilyGpuComputeUtility.LoadEmbeddedShader("RelicFullCapsuleGeneric.glsl");
    }
}
