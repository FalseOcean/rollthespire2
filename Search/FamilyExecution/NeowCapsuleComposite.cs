using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Search.FamilyExecution;

// R-owned predicate and numeric projection for an optional N+R physical.
// Direct Capsule or bounded Bones Small+Large, no ordinary sequence/remainder.
// Pure N never creates or retains this object.
internal sealed record NeowCapsuleComposite(uint[] Metadata, CapsuleRelicReplay Reference, NeowFamilyProjections Projection,
    bool UsesBonesCheckpoint = false, bool UsesBonesArcane = false)
{
    internal const string DenseRevision = "N.Neow.Composite.RCapsule.TargetRank.DenseCarry8.CanonicalAbi1Ready.20260906.v2";
    internal const string CompactRevision = "N.Neow.Composite.RCapsule.TargetRank.CompactFullDecode.CanonicalAbi1Ready.20260906.v2";
    internal const string BonesDenseRevision = "N.Neow.Composite.RCapsule.BonesGrouped.DenseCarry8.CanonicalAbi1Ready.20260906.v1";
    internal const string BonesStagedRevision = "N.Neow.Composite.RCapsule.BonesGrouped.DensePreBones.CanonicalAbi1Ready.20260906.v1";
    internal const string BonesCompactRevision = "N.Neow.Composite.RCapsule.BonesGrouped.CompactFullDecode.CanonicalAbi1Ready.20260906.v1";
    internal const string BonesArcaneDenseRevision = "N.Neow.Composite.RCapsule.BonesArcane.DenseCarry8.CanonicalAbi1Ready.20260907.v1";
    internal const string BonesArcaneStagedRevision = "N.Neow.Composite.RCapsule.BonesArcane.DensePreBones.CanonicalAbi1Ready.20260907.v1";
    internal const string BonesArcaneCompactRevision = "N.Neow.Composite.RCapsule.BonesArcane.CompactFullDecode.CanonicalAbi1Ready.20260907.v1";
    internal string Revision(bool compact, bool staged) => UsesBonesArcane
        ? compact ? BonesArcaneCompactRevision : staged ? BonesArcaneStagedRevision : BonesArcaneDenseRevision
        : UsesBonesCheckpoint
        ? compact ? BonesCompactRevision : staged ? BonesStagedRevision : BonesDenseRevision
        : compact ? CompactRevision : DenseRevision;
    internal static bool TryCreate(ExactSearchExecutionRequest request, NeowReplayPlan n, out NeowCapsuleComposite? composite)
    {
        composite = null;
        if (TryCreateBonesArcane(request, n, out composite)) return true;
        if (n.StructuredConditions.Length != 0 || n.HasFinalCurseFastProjection || n.ExactOnly.Length != 0 ||
            request.Evaluation.RelicSequenceConditions.Any(c => !c.IsEmpty) || request.Evaluation.RelicShopSequenceConditions.Any(c => !c.IsEmpty) ||
            !request.Evaluation.CapsuleContainedRelics.IsEmpty || request.Evaluation.RequireWhetstone || request.Evaluation.RequireWarPaint)
            return false;
        var conditions = request.Evaluation.StructuredNeowEffects.Where(c => !c.IsEmpty).ToArray();
        if (n.Bones)
        {
            // The grouped output is invariant under Small->Large vs Large->Small:
            // both consume the same three Rewards rolls and the same rarity-lane
            // prefixes. No other companion/local predicate is admitted here.
            if (conditions.Length != 1 || !NeowReplayPlan.IsGroupedCapsule(conditions[0]) ||
                request.Evaluation.RequiredBonesAcquisitionOrder.Count != 0 ||
                !request.Authority.CanUseCurrentModel || request.Authority.NoRunModifiers != true ||
                request.Authority.PlayersCount != 1 || request.Authority.AllCharacterCardPoolsUnlocked != true)
                return false;
            NeowReplayPlan groupedRoute = NeowReplayPlan.Compile(request, true);
            if (!RelicFullGpuPlan.TryCreate(request, groupedRoute, out var full, out _) || full is null ||
                full.CapsuleMetadata[0] != RelicFullGpuPlan.GroupedBonesArrival)
                return false;
            composite = new(full.CapsuleMetadata, new CapsuleRelicReplay(request, groupedRoute),
                new NeowFamilyProjections(request, groupedRoute, true, full.SequencePlan.Pool), true);
            return true;
        }
        if (n.Selected is not (Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule)) return false;
        if (conditions.Length != 1 || !NeowReplayPlan.IsCapsule(conditions[0]) ||
            !Beta110FastRelicCatalog.TryGetId(conditions[0].SourceRelicKey, out byte source) || source != n.Selected)
            return false;
        NeowReplayPlan r = NeowReplayPlan.Compile(request, true);
        if (r.ExactOnly.Length != 0 || !RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var dense, out _, out _)) return false;
        var targets = conditions[0].OutputKeys.ToArray();
        if (targets.Length is < 1 or > 2 || (source == Beta110FastRelicCatalog.SmallCapsule && targets.Length != 1) ||
            targets.Distinct().Count() != targets.Length) return false;
        // Donor mode 2 requires non-depleting rarity lanes. Unusual/empty pools
        // retain the general R physical with its rarity fallback semantics.
        for (int lane = 1; lane <= 3; lane++)
        {
            int[] buckets = Enumerable.Range(0, pool.BucketCount).Where(i => pool.BucketScopes[i] == 1 && pool.BucketKinds[i] == lane).ToArray();
            if (buckets.Length != 1 || pool.BucketLengths[buckets[0]] < 2 || pool.DrawDirections[buckets[0]] != 1) return false;
        }
        // [bucket count, source, target count, bucket/rank/rarity for two targets, lengths...]
        var meta = new uint[9 + pool.BucketCount];
        meta[0] = (uint)pool.BucketCount; meta[1] = source; meta[2] = (uint)targets.Length;
        for (int i = 0; i < pool.BucketCount; i++) meta[9 + i] = (uint)pool.BucketLengths[i];
        for (int t = 0; t < targets.Length; t++)
        {
            if (!dense.TryGetValue(targets[t], out ushort id)) return false;
            bool found = false;
            for (int bucket = 0; bucket < pool.BucketCount; bucket++)
            {
                if (pool.BucketScopes[bucket] != 1 || pool.BucketKinds[bucket] is < 1 or > 3) continue;
                int position = Array.IndexOf(pool.DenseRelicIds, id, pool.BucketOffsets[bucket], pool.BucketLengths[bucket]);
                if (position < 0) continue;
                meta[3 + t * 3] = (uint)bucket; meta[4 + t * 3] = (uint)(position - pool.BucketOffsets[bucket]);
                meta[5 + t * 3] = pool.BucketKinds[bucket]; found = true; break;
            }
            if (!found) return false;
        }
        composite = new(meta, new CapsuleRelicReplay(request, r), new NeowFamilyProjections(request, r, true, pool));
        return true;
    }

    private static bool TryCreateBonesArcane(ExactSearchExecutionRequest request, NeowReplayPlan n,
        out NeowCapsuleComposite? composite)
    {
        composite = null;
        if (!n.Bones || n.HasFinalCurseFastProjection || n.ExactOnly.Length != 0 ||
            !request.Authority.CanUseCurrentModel || request.Authority.NoRunModifiers != true ||
            request.Authority.PlayersCount != 1 || request.Authority.AllCharacterCardPoolsUnlocked != true ||
            request.Evaluation.RelicSequenceConditions.Any(c => !c.IsEmpty) ||
            request.Evaluation.RelicShopSequenceConditions.Any(c => !c.IsEmpty) ||
            !request.Evaluation.CapsuleContainedRelics.IsEmpty || request.Evaluation.RequireWhetstone || request.Evaluation.RequireWarPaint)
            return false;
        var pair = request.Evaluation.RequiredBonesAcquisitionOrder.Count == 2
            ? request.Evaluation.RequiredBonesAcquisitionOrder : request.Evaluation.RequiredBonesCombination;
        if (request.Evaluation.RequiredBonesAcquisitionOrder.Count is not (0 or 2) || pair.Count != 2 ||
            !pair.ToHashSet().SetEquals([BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.ArcaneScroll]))
            return false;
        var conditions = request.Evaluation.StructuredNeowEffects.Where(c => !c.IsEmpty).ToArray();
        if (conditions.Length != 2 || n.StructuredConditions.Length != 1) return false;
        var arcane = conditions.FirstOrDefault(c => c.SourceRelicKey == BaseGameModelKeys.Relics.ArcaneScroll);
        var capsule = conditions.FirstOrDefault(c => c.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule);
        if (arcane is null || capsule is null || arcane.Kind != NeowStructuredConditionKind.ExactSingle ||
            arcane.Scope != NeowStructuredEffectScope.ProductRelevantEffects || arcane.OutputKind != NeowStructuredOutputKind.Card ||
            arcane.OutputKeys.Count != 1 || !NeowReplayPlan.IsCapsule(capsule) ||
            capsule.Kind != NeowStructuredConditionKind.ExactUnorderedPair || capsule.OutputKeys.Count != 2 ||
            capsule.AllowDuplicateOutputs || capsule.OutputKeys.Distinct().Count() != 2)
            return false;
        var route = NeowReplayPlan.Compile(request, true);
        if (!RelicFullGpuPlan.TryCreateBonesArcaneCheckpoint(request, route, out var full, out _) || full is null)
            return false;
        composite = new(full.CapsuleMetadata, new CapsuleRelicReplay(request, route),
            new NeowFamilyProjections(request, route, true, full.SequencePlan.Pool), true, true);
        return true;
    }
}
