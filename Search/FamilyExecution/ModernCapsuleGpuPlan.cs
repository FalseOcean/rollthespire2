using System.Globalization;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record ModernCapsuleGpuPlan(uint[] Metadata, GpuNumericalVariant NumericalVariant, int MaxLaneLength,
    bool UsesTrackedRanks, bool HasBones, bool NeedsPositiveOffer)
{
    internal bool UsesShopFirstDraw { get; init; }
    internal const string ShopFirstDrawRevision = ".ShopFirstDraw.20261119.v1";

    internal ModernCapsuleGpuPlan WithOrdinarySequence(RelicFamilyPlan sequence)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("RT2_R_CAPSULE_SHOP_FIRST"), "off",
                StringComparison.OrdinalIgnoreCase) || sequence.AlwaysReject || sequence.Predicates.Length != 0 ||
            sequence.ShopPredicates is not [{ Count: 1, TargetIds: [var target] }] || target == ushort.MaxValue)
            return this;

        // A single named slot has the same meaning for ordered and unordered rows.
        // The plan compiler resolves one personal Shop bucket and verifies membership.
        int bucket = sequence.LastRequiredBucket;
        var pool = sequence.Pool;
        if (!pool.UsesChildShuffle || pool.BucketScopes[bucket] != 1 || pool.BucketKinds[bucket] != 4 ||
            pool.EntryFlags.AsSpan(pool.BucketOffsets[bucket], pool.BucketLengths[bucket]).Contains((byte)0))
            return this;
        return this with { UsesShopFirstDraw = true };
    }

    internal string Revision(bool compact) => "R.Relic.Gpu.ModernCapsuleHeads.20261114.v1" +
        (UsesTrackedRanks ? ".TrackedRanks" : "") +
        (UsesShopFirstDraw ? ShopFirstDrawRevision : "") +
        (NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne" : ".NativeFloat64") +
        (compact ? ".CompactAbi1" : ".DenseCarry8");

    internal string ShaderSource(int localStateCapacity)
    {
        string laneLength = Math.Max(1, MaxLaneLength).ToString(CultureInfo.InvariantCulture);
        bool integer = NumericalVariant == GpuNumericalVariant.IntegerRne;
        string numerics = integer
            ? GpuIntegerNumerics.NextIntSource + "\n" + GpuIntegerNumerics.UnitFloatSource + """

                uint mc_next_int(uint bound) {
                    uint64_t draw = next_u64();
                    return rt2_next_int_rne_raw32(uint(draw), uint(draw >> 32u), bound);
                }
                float mc_next_float() {
                    uint64_t draw = next_u64();
                    return rt2_next_float_from_words_u32(uvec2(uint(draw), uint(draw >> 32u)));
                }
                """
            : """
                double mc_next_double() {
                    return double(next_u64() >> 11u) * 1.1102230246251565e-16lf;
                }
                uint mc_next_int(uint bound) { return uint(mc_next_double() * double(bound)); }
                float mc_next_float() { return float(mc_next_double()); }
                """;
        string flags = "#define MC_META_BINDING 13\n#define MC_CHILD_INDEX(bound) mc_next_int(bound)\n" +
            "#define MC_COMPANION_INDEX(bound) mc_next_int(bound)\n#define MC_COMPANION_WORD() next_u64()\n" +
            "#define MC_MAX_LANE " + laneLength + "\n#define MC_TRACKED " + (UsesTrackedRanks ? "1" : "0") +
            "\n#define MC_BONES " + (HasBones ? "1" : "0") + "\n#define MC_POSITIVE " + (NeedsPositiveOffer ? "1" : "0") + "\n";
        string functions = flags + numerics + "\n" +
            FamilyGpuComputeUtility.LoadEmbeddedShader("ModernCapsuleRelic.glsl");
        string source = RelicFamilyGpuExecutor.ShaderSource(false, localStateCapacity: localStateCapacity, childShuffle: true);
        if (UsesShopFirstDraw) source = source.Replace("#version 450",
            "#version 450\n#define RT2_R_CAPSULE_SHOP_FIRST 1", StringComparison.Ordinal);
        // Attach the modern observation to ordinary R candidate dispatch.
        const string append = "    append_output(logical);";
        const string helpers = "uint lane_from_kind";
        source = GpuIntegerNumerics.ReplaceUnique(source, helpers, functions + "\n" + helpers, "R.ModernCapsule");
        source = GpuIntegerNumerics.ReplaceUnique(source, append,
            "    if (mc_matches(root)) append_output(logical);", "R.ModernCapsule");
        const string begin = "    rng_initialize(root + make_u64(0xda243e80u, 0x1a8a7b60u));";
        source = GpuIntegerNumerics.ReplaceUnique(source, begin,
            "    if (plan_meta.values[3u] != 0u || plan_meta.values[4u] != 0u) {\n" + begin, "R.ModernCapsule");
        source = GpuIntegerNumerics.ReplaceUnique(source, "    if (mc_matches(root)) append_output(logical);",
            "    }\n    if (mc_matches(root)) append_output(logical);", "R.ModernCapsule");
        if (integer) return GpuIntegerNumerics.RequireNoFloat64(source, "R.ModernCapsule");
        return source.Replace("#version 450", "#version 450\n#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require",
            StringComparison.Ordinal);
    }
}

internal sealed partial class ModernCapsuleRelicPlan
{
    internal ModernCapsuleGpuPlan? PackGpu(GpuNumericalVariant variant, bool withLocalNeow = false)
    {
        if (_maxBucketLength > RelicFamilyPlanCompiler.MaximumShaderLocalState ||
            _route.Authority.BonesEligibleRelicIds.Length > 64) return null;
        var meta = new List<uint>(new uint[withLocalNeow ? 63 : 58]);
        void Set(int at, int value) => meta[at] = unchecked((uint)value);
        void Hash(int at, ulong value)
        {
            meta[at] = (uint)value;
            meta[at + 1] = (uint)(value >> 32);
        }
        void CopyAt(int at, IEnumerable<uint> values)
        {
            Set(at, meta.Count);
            meta.AddRange(values);
        }
        void CopyKeys(int at, ushort[] targets)
        {
            CopyAt(at, targets.Select(id => (uint)id));
            Set(at + 1, targets.Length);
        }

        Set(0, _route.Authority.PlayerSlotIndex);
        Set(1, _route.Bones ? 1 : 0);
        Set(2, _route.Selected);
        Set(3, _route.First);
        Set(4, _route.Second);
        Set(5, _ineligible);
        Set(6, _lastBucket);
        Set(7, _maximumDraws);
        Hash(8, XxHash64.Hash("NEOW"u8, 0));
        Hash(10, NeowFamilyReplay.NonCombatHash);
        Hash(12, NeowFamilyReplay.RewardsHash);
        Hash(14, RelicCpuPlan.UpFrontHash);
        Hash(16, _route.BonesAny);
        Hash(18, _route.BonesAll);
        Hash(20, _route.BonesBan);
        CopyAt(22, _route.Authority.EligibleCurseRelicIds.Select(id => (uint)id));
        Set(23, _route.Authority.EligibleCurseRelicIds.Length);
        CopyAt(24, _route.Authority.BonesEligibleRelicIds.Select(id => (uint)id));
        Set(25, _route.Authority.BonesEligibleRelicIds.Length);
        CopyAt(26, _companionRewardsDraws.Select(count => unchecked((uint)count)));
        CopyKeys(27, _uncertainObtains);
        CopyKeys(29, _any);
        CopyKeys(31, _all);
        CopyKeys(33, _ban);
        Set(35, meta.Count);
        Set(36, _rows.Length);
        for (int row = 0; row < _rows.Length; row++) meta.AddRange([0u, 0u, 0u]);
        for (int row = 0; row < _rows.Length; row++)
        {
            int at = (int)meta[35] + row * 3;
            Set(at, _rows[row].Source);
            CopyKeys(at + 1, _rows[row].Targets);
        }
        for (int lane = 0; lane < 3; lane++)
        {
            int at = 37 + lane * 3;
            Set(at, _lanes[lane].Bucket);
            Set(at + 1, _lanes[lane].Entries.Length);
            Set(at + 2, _lanes[lane].EligibleCount);
        }
        bool tracked = _maximumDraws == 1 || _uncertainObtains.Length == 0;
        var targetSet = _rows.SelectMany(row => row.Targets).Concat(_any).Concat(_all).Concat(_ban).ToHashSet();
        if (withLocalNeow)
        {
            // The fused N suffix observes the actual upgrades even when they
            // are unnamed Capsule contents.
            tracked &= _uncertainObtains.Length == 0;
            targetSet.UnionWith(_nonCombatObtains);
            CopyKeys(58, _nonCombatObtains);
            for (int lane = 0; lane < 3; lane++)
                CopyAt(60 + lane, _lanes[lane].Entries.Select(id => (uint)id));
        }
        for (int lane = 0; lane < 3; lane++)
        {
            var entries = _lanes[lane].Entries;
            var positions = entries.Select((id, index) => (id, index))
                .Where(entry => entry.id != _ineligible && targetSet.Contains(entry.id)).ToArray();
            int at = 47 + lane * 3;
            CopyAt(at, positions.SelectMany(entry => new[] { (uint)entry.index, (uint)entry.id }));
            Set(at + 1, positions.Length);
            Set(at + 2, Array.IndexOf(entries, _ineligible));
            tracked &= positions.Length <= 4;
        }
        var laneByKey = new uint[Pool.DenseRelicIds.Length == 0 ? 0 : Pool.DenseRelicIds.Max() + 1];
        Array.Fill(laneByKey, uint.MaxValue);
        for (int lane = 0; lane < 3; lane++)
            foreach (ushort id in _lanes[lane].Entries)
                if (id != _ineligible) laneByKey[id] = (uint)lane;
        CopyAt(56, laneByKey);
        Set(57, laneByKey.Length);
        int identityTable = meta.Count;
        Set(46, identityTable);
        foreach (byte curse in _route.Authority.EligibleCurseRelicIds)
        {
            var identities = _route.Authority.Identity.ForCurse(curse);
            meta.Add((uint)identities.FixedCandidates.Length);
            meta.Add((uint)identities.AppendChoices.Length);
            foreach (byte source in identities.FixedCandidates) meta.Add(source);
            foreach (var choice in identities.AppendChoices)
            {
                meta.Add(choice.TrueCandidate);
                meta.Add(choice.FalseCandidate);
            }
            while ((meta.Count - identityTable) % 25 != 0) meta.Add(0);
        }
        bool positiveOffer = NeowIdentityOfferFacts.IsPositiveCandidate(_route.Selected);
        return new(meta.ToArray(), variant, _maxBucketLength, tracked, _route.Bones, positiveOffer);
    }
}
