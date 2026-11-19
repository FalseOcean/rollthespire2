using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed class WorldFamilyGpuPlan
{
    // Scratch is bounded by the actual pool, not the authored event horizon.
    // Oversized FirstN observes the entire pool; an absent ExactSlot rejects.
    internal static bool Supports(WorldFamilyReplay replay) => replay.Plan.ActSelectionGroups.Length <= 8 &&
        replay.Plan.SharedAncients.Length <= 512 && replay.Plan.MaxEncounterPoolCount <= 512 &&
        replay.Plan.Acts.All(a => a.EventCandidates.Length <= 512);
    internal uint[][] Buffers { get; }
    internal int Capacity { get; }
    internal int GroupingK { get; } = 4;
    internal long GroupingWorkspaceBytes => (GroupingK - 1) * (Capacity * 8L + 64) + (GroupingK > 1 ? GroupingK * 32 : 0);
    internal int ScratchWords { get; }
    internal GpuNumericalVariant NumericalVariant { get; }
    internal GpuNumericRequirements NumericRequirements => GpuIntegerNumerics.Requirements(NumericalVariant);
    private readonly bool _soloConstants;
    private readonly bool _encounters;
    private readonly int _normalSlots;
    private readonly int _eliteSlots;
    private readonly bool _variantFilterMigrated;
    private readonly bool _childShuffle;
    private readonly bool _needsSharedAncientIds;
    private readonly bool _trimmedEventScratch;
    private readonly bool _usesEventPair;
    internal bool UsesEncounterRankMask { get; }
    internal long ScratchBytes => Math.Max(4, Capacity * ScratchWords * 4L);
    internal long WorkspaceBytes => Capacity * 8L + ScratchBytes + 64 + GroupingWorkspaceBytes +
        Buffers.Where((_, i) => i is not (0 or 12 or 16 or 17 or 18)).Sum(b => Math.Max(4, b.Length * 4L));
    internal string Revision(bool compact) => "W.World.Gpu." + (VariantOnly ? "Variant" : "Progression") +
        (compact ? ".CompactAbi1" : ".Dense") + $".K{GroupingK}." +
        (_soloConstants ? "SoloConstants.CanonicalAbi1Ready.20260929.v3" : "CanonicalAbi1Ready.20260907.v2") +
        (NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne.20261025.v1" : "") +
        (_variantFilterMigrated ? ".VariantFilterMovedToV.20261028.v1" : "") + (_encounters ? ".EncounterQueues.20261105.v1" : "") +
        (_childShuffle ? ".Modern113" : "") +
        (UsesSharedAncientCountOnly ? ".SharedAncientsCountOnly.20261114.v1" : "") +
        (_trimmedEventScratch ? ".ObservedEventScratch.20261115.v1" : "") +
        (UsesEncounterRankMask ? ".EncounterRankMask.20261115.v1" : "") +
        (_usesEventPair ? ".EffectiveEventPair.20261119.v1" : "") + GpuRngRotation.Revision;
    internal bool VariantOnly { get; }
    internal bool UsesSharedAncientCountOnly => _childShuffle && !VariantOnly && !_needsSharedAncientIds;
    internal WorldFamilyGpuPlan(WorldFamilyReplay replay, bool specializeSolo = false,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64, bool elideSharedAncientPermutation = true,
        bool? trimEventScratch = null, bool? encounterRankMask = null, bool? effectiveEventPair = null)
    {
        if (numericalVariant is not (GpuNumericalVariant.NativeFloat64 or GpuNumericalVariant.IntegerRne))
            throw new ArgumentOutOfRangeException(nameof(numericalVariant));
        NumericalVariant = numericalVariant;
        _variantFilterMigrated = replay.VariantFilterMigrated;
        var w = replay.Plan;
        _childShuffle = w.UsesChildShuffle;
        _needsSharedAncientIds = !elideSharedAncientPermutation || !_childShuffle || w.Acts.Any(a => a.HasAncientIdentityPredicate);
        _encounters = w.Acts.Any(a => a.HasEncounterPredicate);
        _normalSlots = Math.Max(1, w.Acts.Max(a => a.WeakEncounterSlots + a.RegularEncounterSlots));
        _eliteSlots = Math.Max(1, w.Acts.Max(a => a.EliteEncounterSlots));
        VariantOnly = w.MaxRequiredAct == 0;
        _soloConstants = specializeSolo && !VariantOnly;
        WorldFamilyGpuPacking.PackWorldPlan(w, out var sm, out var si, out var map,
            out var acts, out var encounters, out var conflicts, out var bosses, out var predicates, out var targets);
        var ps = predicates.ToList(); var ids = targets.ToList();
        var extra = new List<uint>(); var data = new List<uint>();
        bool enableEventPair = _childShuffle && (effectiveEventPair ?? !string.Equals(
            Environment.GetEnvironmentVariable("RT2_W_EVENT_PAIR"), "off", StringComparison.OrdinalIgnoreCase));
        int fallbackEventStride = 0;
        uint Store(IEnumerable<uint> values) { uint offset = (uint)data.Count; data.AddRange(values); return offset; }
        uint Pred(IEnumerable<WorldFastDenseSetPredicate> values)
        {
            WorldFamilyGpuPacking.PackPredicates(values.ToArray(), ps, ids, out int offset, out _);
            return (uint)offset;
        }
        // Per Act: variantAllowed, eventOffset/count, H, eventPredOffset/count,
        // ancientOffset, ancientPredOffset/count, branchOffset/count, hasBoss.
        foreach (var act in w.Acts)
        {
            uint events;
            if (enableEventPair && TryPackEventPair(act, out uint[] pair))
            {
                // A tagged offset refers to the three-word descriptor. Plain offsets
                // retain the original per-event packed table for all other acts.
                events = Store(pair) | 0x80000000u;
                _usesEventPair = true;
            }
            else
            {
                events = Store(act.EventCandidates.Select(c => (uint)c.EventId | (uint)c.Source << 16 | (c.StaticEligible ? 1u << 24 : 0)));
                if (act.HasEventPredicate) fallbackEventStride = Math.Max(fallbackEventStride, act.EventCandidates.Length);
            }
            uint eventPred = (uint)data.Count;
            foreach (var p in act.EventPredicates) data.AddRange([p.RangeMode,p.RangeValue,p.SourceFilter,Pred([p.Keys])]);
            uint ancients = Store(act.Ancients.Select(i => (uint)i));
            var ancientPreds = act.AncientIdentityPredicates.Concat(act.AncientBranchIdentityPredicates).ToArray();
            uint ancientPred = Pred(ancientPreds);
            uint branches = (uint)data.Count;
            foreach (var b in act.FamilyBossBranches) data.AddRange([Pred([b.First]),Pred([b.Second])]);
            extra.AddRange([act.FamilyVariantAllowed?1u:0u,events,(uint)act.EventCandidates.Length,
                act.EventPredicates.Length==0?0u:act.EventPredicates.Max(p=>(uint)p.RangeValue),eventPred,(uint)act.EventPredicates.Length,
                ancients,ancientPred,(uint)ancientPreds.Length,branches,(uint)act.FamilyBossBranches.Length,act.HasBossPredicate?1u:0u]);
        }
        uint sharedOffset = Store(w.SharedAncients.Select(i => (uint)i));
        bool needsEncounters = w.Acts.Any(a => a.Act <= w.MaxRequiredAct &&
            (a.HasEncounterPredicate || a.Act < w.MaxRequiredAct || a.HasBossPredicate || a.HasAncientPredicate || w.RequiresSecondBoss));
        bool useRankMask = encounterRankMask ?? !string.Equals(Environment.GetEnvironmentVariable("RT2_W_ENCOUNTER_RANK_MASK"),
            "off", StringComparison.OrdinalIgnoreCase);
        UsesEncounterRankMask = useRankMask && _childShuffle && !VariantOnly && !_encounters && needsEncounters &&
            w.MaxEncounterPoolCount is > 0 and <= WorldEncounterRankGpuSource.MaxPoolCount;
        uint[] encounterRankTable = UsesEncounterRankMask ? WorldEncounterRankGpuSource.Pack(w.Acts, acts, encounters.Length) : [];
        int encounterStride = needsEncounters && !UsesEncounterRankMask ? w.MaxEncounterPoolCount : 0;
        int fullEventStride = w.Acts.Max(a => a.EventCandidates.Length);
        // Only events_match writes this scratch. Unobserved Modern113 events
        // consume their parent shuffle seed without using the child permutation.
        bool trimScratch = trimEventScratch ?? !string.Equals(Environment.GetEnvironmentVariable("RT2_W_TRIM_EVENT_SCRATCH"),
            "off", StringComparison.OrdinalIgnoreCase);
        int eventStride = _childShuffle && trimScratch ? fallbackEventStride : fullEventStride;
        _trimmedEventScratch = eventStride < fullEventStride;
        ScratchWords = encounterStride + eventStride;
        // 32 MiB scratch ceiling independent of the external 2^24 root batch.
        Capacity = Math.Min(1 << 20, Math.Max(64, (32 * 1024 * 1024 / 4 / Math.Max(1, ScratchWords) / 64) * 64));
        uint[] meta = [0,(uint)replay.BucketLengths.Length,(uint)w.ActSelectionGroups.Length,(uint)map.Length,
            (uint)w.Acts.Length,(uint)w.SharedAncients.Length,(uint)w.MaxRequiredAct,w.RequiresSecondBoss?1u:0u,
            (uint)encounterStride,(uint)(ps.Count/7),0,w.Act1OverrideId==ushort.MaxValue?uint.MaxValue:w.Act1OverrideId,
            (uint)w.Ascension,(uint)eventStride,sharedOffset];
        // Bindings 0/12/16/17/18 are private dynamic batch/scratch/header/output/input.
        Buffers = [[],replay.BucketLengths,meta,sm,si,map,acts,encounters,conflicts,bosses,ps.ToArray(),ids.ToArray(),[],extra.ToArray(),data.ToArray(),
            _encounters ? WorldEncounterGpuSource.Pack(w) : encounterRankTable,[],[],[]];
    }

    private static bool TryPackEventPair(WorldFastActPlan act, out uint[] descriptor)
    {
        descriptor = [];
        if (act.EventPredicates.Length != 2 || act.EventCandidates.Length is < 2 or > 32) return false;
        var ordered = act.EventPredicates.OrderBy(p => p.RangeValue).ToArray();
        ushort[] targets = new ushort[2];
        for (int i = 0; i < ordered.Length; i++)
        {
            var p = ordered[i];
            if (p.RangeMode != 0 || p.RangeValue != i + 1 || p.SourceFilter != byte.MaxValue ||
                p.Keys.AlwaysReject || p.Keys.Ban.Length != 0 || p.Keys.Any.Length + p.Keys.All.Length != 1)
                return false;
            targets[i] = p.Keys.Any.Length == 1 ? p.Keys.Any[0] : p.Keys.All[0];
        }
        if (targets[0] == targets[1]) return false;

        uint eligible = 0;
        uint first = uint.MaxValue, second = uint.MaxValue;
        for (int i = 0; i < act.EventCandidates.Length; i++)
        {
            var candidate = act.EventCandidates[i];
            if (!candidate.StaticEligible) continue;
            eligible |= 1u << i;
            if (candidate.EventId == targets[0])
            {
                if (first != uint.MaxValue) return false;
                first = (uint)i;
            }
            else if (candidate.EventId == targets[1])
            {
                if (second != uint.MaxValue) return false;
                second = (uint)i;
            }
        }
        // A variant whose compiled eligible pool lacks either necessary identity
        // is already a definite mismatch in the full event-table matcher.
        descriptor = [eligible, first, second];
        return true;
    }

    internal string ShaderSource()
    {
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta110GpuWorldBossG1.comp.glsl");
        // Retain the actual donor numerical implementation, not a second copy.
        string rng = donor[donor.IndexOf("uint64_t s0;",StringComparison.Ordinal)..donor.IndexOf("void write_shared_state",StringComparison.Ordinal)];
        string numerical = donor[donor.IndexOf("bool encounter_eligible",StringComparison.Ordinal)..donor.IndexOf("void main()",StringComparison.Ordinal)];
        string body=FamilyGpuComputeUtility.LoadFamilyShaderWithVisibleSeedRootHash("WorldFamily.comp.glsl")
            .Replace("/*__W_DONOR__*/",rng+numerical,StringComparison.Ordinal)
            .Replace("/*__W_EVENT_PAIR__*/", _usesEventPair
                ? FamilyGpuComputeUtility.LoadEmbeddedShader("WorldEffectiveEventPair.glsl") : "", StringComparison.Ordinal)
            .Replace("#define RT2_W_CHILD_SHUFFLE 0", "#define RT2_W_CHILD_SHUFFLE " + (_childShuffle ? "1" : "0"), StringComparison.Ordinal)
            .Replace("#define RT2_W_SHARED_ANCIENT_IDS 1", "#define RT2_W_SHARED_ANCIENT_IDS " +
                (_needsSharedAncientIds ? "1" : "0"), StringComparison.Ordinal)
            .Replace("uint sharedIds[512]",$"uint sharedIds[{Math.Max(1,Buffers[2][5])}]",StringComparison.Ordinal)
            .Replace("uint maxAct=plan_meta_buffer.values[6u];", VariantOnly ? "return true; uint maxAct=0u;" : "uint maxAct=plan_meta_buffer.values[6u];",StringComparison.Ordinal);
        if (_encounters) body = WorldEncounterGpuSource.Extend(body, numerical, _normalSlots, _eliteSlots);
        if (_soloConstants)
        {
            // These packed values cannot change during an executor's lifetime.
            // No target identity or pool member becomes a shader constant.
            const string horizon="uint maxAct=plan_meta_buffer.values[6u];", second="bool second=plan_meta_buffer.values[7u]!=0u;";
            if (!body.Contains(horizon,StringComparison.Ordinal) || !body.Contains(second,StringComparison.Ordinal))
                throw new InvalidOperationException("W.SoloConstantsShaderSeam");
            body=body.Replace(horizon,
                "uint maxAct="+Buffers[2][6].ToString(System.Globalization.CultureInfo.InvariantCulture)+"u;",StringComparison.Ordinal)
                .Replace(second,
                    "bool second="+(Buffers[2][7]!=0 ? "true" : "false")+";",StringComparison.Ordinal);
        }
        if (NumericalVariant == GpuNumericalVariant.IntegerRne) body = WorldGpuNumericalSource.IntegerRne(body);
        return UsesEncounterRankMask ? WorldEncounterRankGpuSource.ReplaceQueue(body) : body;
    }
}
