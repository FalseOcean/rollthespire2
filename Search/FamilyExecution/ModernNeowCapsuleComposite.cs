using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record ModernNeowCapsuleComposite(ModernCapsuleGpuPlan Capsule, long SourceMask)
{
    internal const string Revision = ".ModernCapsuleLocalSameRoute.20261114.v1";
    private const string Neow = "N.Neow";

    internal static bool PreferMeasuredWholeSearch(SearchModel model, NeowReplayPlan n, FilterBodyDescription unit,
        FamilyGpuInitializationSnapshot device)
    {
        // Only the tested solo/native pair and the measured renderer use the
        // complete allocation. Other shapes can still explicitly select it.
        if (!device.GpuEligible || !string.Equals(device.RenderingBackend, "d3d12", StringComparison.OrdinalIgnoreCase) ||
            device.EnvironmentKey.Split('|') is not [_, var deviceName, _] ||
            !deviceName.EndsWith("RTX 4060 Laptop GPU", StringComparison.OrdinalIgnoreCase) ||
            model.CharacterKey != BaseGameModelKeys.Characters.Ironclad || model.Ascension != 10 ||
            model.Authority.AllCharacterCardPoolsUnlocked != true ||
            !unit.PhysicalRevision.Contains(".TrackedRanks.NativeFloat64", StringComparison.Ordinal) ||
            n.HasFinalCurseFastProjection || n.First != Beta110FastRelicCatalog.InvalidId ||
            n.Second != Beta110FastRelicCatalog.InvalidId || n.TopAny != 0 || n.TopAll != 0 || n.TopBan != 0 ||
            n.BonesAny != 0 || n.BonesBan != 0 ||
            n.BonesAll != (Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.Kaleidoscope) |
                Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.LargeCapsule)) ||
            n.StructuredConditions is not [{ SourceRelicId: Beta110FastRelicCatalog.Kaleidoscope,
                Kind: Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets, OrderedKaleidoscope: false } k] ||
            model.Evaluation.StructuredNeowEffects is not [_, _] ||
            model.Evaluation.StructuredNeowEffects.FirstOrDefault(row => NeowReplayPlan.IsCapsule(row)) is not
                { Kind: NeowStructuredConditionKind.ExactUnorderedPair } r ||
            r.SourceRelicKey != BaseGameModelKeys.Relics.LargeCapsule || r.OutputKeys.Count != k.TargetCount ||
            r.OutputKeys.Distinct().Count() != r.OutputKeys.Count || k.TargetCount is < 1 or > 2 ||
            k.TargetCount == 2 && k.Target0 == k.Target1 ||
            !model.Evaluation.CapsuleContainedRelics.IsEmpty || model.Evaluation.RequireWhetstone || model.Evaluation.RequireWarPaint)
            return false;

        var pools = n.Authority.EffectCatalog.OtherCharacterPools;
        var bags = model.Authority.WorldAuthority?.Beta109Generation;
        return pools.Length == 5 && pools.Count(pool => pool.Rare.Length == 24) == 1 &&
            pools.All(pool => pool.Common.Length == 20 && pool.Uncommon.Length == 35 && pool.Rare.Length is 24 or 25) &&
            bags is not null && bags.SharedRelicBuckets.Select(bucket => bucket.OrderedEntries.Count).SequenceEqual([30, 25, 35, 25, 1, 2]) &&
            bags.PlayerRelicBuckets.Select(bucket => bucket.OrderedEntries.Count).SequenceEqual([32, 26, 38, 26]);
    }

    internal static bool TryCreate(SearchModel model, NeowReplayPlan n, FilterBodyDescription neow,
        FilterBodyDescription relic, bool allowFloat64, out FilterBodyDescription? composite)
    {
        composite = null;
        if (!n.AuditedModern113IdentityReplay || n.Authority.PlayersCount != 1 || !n.Bones ||
            n.StructuredConditions.Length == 0 && !n.HasFinalCurseFastProjection ||
            n.ExactOnly.Any(issue => issue != NeowCapsuleNonCombatContinuation.Remainder) ||
            !neow.UsesGpu || neow.FamilyId != Neow || !neow.Coverage.SequenceEqual([Neow]) ||
            !relic.UsesGpu || relic.FamilyId != RelicFamily.Id ||
            !relic.Coverage.SequenceEqual([ModernCapsuleRelicFamily.Obligation]) ||
            !relic.PhysicalRevision.StartsWith("R.Relic.Gpu.ModernCapsuleHeads.", StringComparison.Ordinal)) return false;

        var r = NeowReplayPlan.Compile(model, true);
        if (r.Selected != n.Selected || !r.Bones || r.First != n.First || r.Second != n.Second ||
            ModernCapsuleRelicPlan.TryCompile(model, r) is not { } bag) return false;

        var variant = allowFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne;
        if (bag.PackGpu(variant, withLocalNeow: true) is not { } capsule ||
            !NeowSingleplayerGpuPlan.TryCreate(n, out var packed, out _) || packed is null) return false;
        var gpu = packed with
        {
            NumericalVariant = allowFloat64 ? NeowGpuNumericalVariant.NativeFloat64 : NeowGpuNumericalVariant.IntegerUnitFloat,
            DirectNestedMode = 0
        };
        var local = new ModernNeowCapsuleComposite(capsule, gpu.FixedPairLocalSourceMask);
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var options = NeowGpuOptimizations.Capture().Applicable(gpu, false) with { CurseFirst = false };
        string RevisionFor(bool compact) => (compact ? NeowFamilyGpuPlan.CompactRevision :
            gpu.UsesStagedDense ? NeowFamilyGpuPlan.StagedDenseRevision : NeowFamilyGpuPlan.DenseRevision) +
            Revision + (capsule.UsesTrackedRanks ? ".TrackedRanks" : "") +
            (allowFloat64 ? ".NativeFloat64" : ".IntegerUnitFloat") +
            gpu.FixedPairLocalSourceRevision(compact || !gpu.UsesStagedDense ? 3 : 2) +
            gpu.LocalObservationRevision + gpu.ModernTransformGateRevision + gpu.EntryIdentityRevision +
            options.Applicable(gpu, compact).Revision + NeowIdentityOfferFacts.Revision + GpuRngRotation.Revision;
        string[] remainders = [..neow.Preparation.Remainders, ..relic.Preparation.Remainders];
        if (bag.LocalHasUnresolvedPickup)
            remainders = [..remainders, Neow + ":UnresolvedCapsulePickupContinuationRetainedForExact"];
        var preparation = new PreparedFilter(Neow)
        {
            Coverage = [Neow, ModernCapsuleRelicFamily.Obligation],
            Obligations = [..neow.Preparation.EffectiveObligations, ..relic.Preparation.EffectiveObligations],
            Remainders = remainders.Distinct().ToArray(),
            AnalyticalCost = new(Neow, NeowFamilyGpuExecutor.Capacity, [], "ModernLocalNeowCapsuleWorkUnpriced"),
            Survival = FamilySurvivalProjection.Unresolved(Neow, "ModernLocalNeowCapsuleProbabilityUnpriced")
        };
        composite = new(preparation, RevisionFor(false), profile.ProfileId)
        {
            InputIdentity = model.SnapshotFingerprint,
            UsesGpu = true,
            PlayersCount = 1,
            CpuWorkers = neow.CpuWorkers,
            WindowCapacity = NeowFamilyGpuExecutor.Capacity,
            PreferredRootWindowSize = neow.PreferredRootWindowSize,
            NumericRequirements = gpu.GpuNumericRequirements,
            PhysicalBodies = [neow, relic],
            PerformanceProjection = compact => new(Neow, RevisionFor(compact),
                "ModernNeowCapsule.LocalSameOrderedPickups", 1, "IndependentAbi1;Unpriced", true),
            CreateExecution = (self, _) => new GpuFilterExecution<NeowFamilyGpuExecutor>(self,
                device => new(device, gpu, 0x4d4e5243u, optimizations: options, modernLocalCapsule: local), (execution, input, token) =>
                {
                    var output = execution.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalMs, metrics.DispatchSyncMs,
                        metrics.ReadbackMs, metrics.ReadbackBytes);
                }, execution => execution.Dispose(), root => bag.MatchesLocal(root, n), profile,
                execution => (execution.Device, execution.SetupMs), 256)
        };
        return true;
    }

    internal string ShaderSource(int stage)
    {
        string binding = stage == 2 ? "13" : "11";
        string numeric = Capsule.NumericalVariant == GpuNumericalVariant.IntegerRne
            ? FamilyGpuComputeUtility.LoadEmbeddedShader("NextIntIntegerRne.glsl") + """

                uint mc_child_index(inout RngState child, uint bound) {
                    uint64_t word = next_u64(child);
                    return rt2_next_int_rne_raw32(uint(word), uint(word >> 32u), bound);
                }
                """
            : """
                uint mc_child_index(inout RngState child, uint bound) {
                    double unit = double(next_u64(child) >> 11u) * 1.1102230246251565e-16lf;
                    return uint(unit * double(bound));
                }
                """;
        return "#define RT2_N_MODERN_LOCAL_CAPSULE 1\n#define MC_LOCAL_NEOW 1\n#define MC_META_BINDING " + binding +
            "\n#define MC_CHILD_INDEX(bound) mc_child_index(child,bound)\n" +
            "#define MC_COMPANION_INDEX(bound) mc_child_index(rewards,bound)\n#define MC_COMPANION_WORD() next_u64(rewards)\n" +
            "#define MC_MAX_LANE " +
            Math.Max(1, Capsule.MaxLaneLength).ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "\n#define MC_TRACKED " + (Capsule.UsesTrackedRanks ? "1\n" : "0\n") + numeric + "\n" +
            FamilyGpuComputeUtility.LoadEmbeddedShader("ModernCapsuleRelic.glsl") + "\n" +
            FamilyGpuComputeUtility.LoadEmbeddedShader("ModernNeowCapsuleLocal.glsl");
    }
}
