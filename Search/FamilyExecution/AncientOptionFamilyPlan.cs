using System.Collections.Frozen;
using System.Collections.Immutable;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Necessary options-only sieve: OR hypothetical Ancient rows within each Act,
/// AND across Acts. Actual Ancient appearance/row binding remains W/Exact truth.
/// All numerical option generation is delegated to the existing event-local core.
/// </summary>
internal sealed class AncientOptionFamilyPlan
{
    private readonly ExactSearchExecutionRequest _request;
    private readonly Beta109WorldGenerationSnapshot _generation;
    private readonly NeowSearchFilter _filter;
    private readonly ActBinding[] _acts;
    private sealed record NumericalRow(ushort AncientId, WorldFastActPlan Fast);
    private sealed record ActBinding(int Act, ModelKey[] Keys, bool Exact, NumericalRow[] NumericalRows);
    // Request-local static row inputs only; root-bound predictions are never cached.
    private readonly FrozenDictionary<(int Act, ModelKey Key), ReferenceRowBinding> _referenceRows;
    private sealed record ReferenceRowBinding(Beta109AncientEventContextSnapshot? Context,
        ImmutableArray<AncientSearchBranchCondition> Branches,
        ImmutableArray<ActModelKeySetFilter> OptionFilters,
        ImmutableArray<ActModelKeySetFilter> SeaGlassFilters,
        bool Unconditional, bool HasSeaGlassFilter);
    private readonly Dictionary<ModelKey, ushort> _ids = new(ModelKeyComparer.Instance);
    internal ushort PricingDenseId(ModelKey key) => _ids.TryGetValue(key, out ushort id) ? id : ushort.MaxValue;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int Act, ModelKey? Ancient), byte> _reportedUnresolved = new();
    internal bool GpuSupported { get; }
    internal GpuNumericalVariant NumericalVariant { get; }
    internal GpuNumericRequirements GpuNumericRequirements => GpuSupported
        ? GpuIntegerNumerics.Requirements(NumericalVariant) : GpuNumericRequirements.None;
    internal string GpuPreparationIssue { get; }
    internal string GpuPreparationFailureDetail { get; } = "";
    internal delegate uint[][] GpuPacker(IReadOnlyList<Beta110GpuAncientOptionPreGateGate> gates, int scratchWords, int capacity);
    internal int Capacity { get; }
    internal int ScratchWords { get; }
    internal WorldFastAncientOptionPlan? SingleOptionPlan { get; }
    internal IReadOnlyList<WorldFastAncientOptionPlan> OptionPlans { get; }
    internal bool UsesNumericalAct(int act) => _acts.Any(a => a.Act == act && a.Exact);
    internal string PricingSignature => string.Join('|', OptionPlans.Select(p =>
        $"{p.Generator}:s{p.MaxScratchCount}:" + string.Join(',', p.Pools.Select(pool => $"{pool.Role}={pool.Values.Length}"))));
    internal uint[][] Buffers { get; }
    internal FamilySurvivalProjection Survival { get; }
    internal AncientOptionFamilyPlan(ExactSearchExecutionRequest request, bool prepareGpu = true, GpuPacker? gpuPacker = null,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        NumericalVariant = numericalVariant;
        _request = request;
        _generation = request.Authority.WorldAuthority?.Beta109Generation ??
            throw new InvalidOperationException("A.OptionContextUnavailable");
        var e = request.Evaluation;
        _filter = NeowSearchFilter.Empty with
        {
            AncientBranchConditions = e.AncientBranchConditions,
            AncientOptionFilters = e.AncientOptionFilters,
            AncientSeaGlassTargetFilters = e.AncientSeaGlassTargetFilters,
            AncientOptionConditions = e.AncientOptionConditions
        };
        ushort Add(ModelKey key) { if (!_ids.TryGetValue(key, out var id)) { id = checked((ushort)_ids.Count); _ids.Add(key, id); } return id; }
        int[] acts = e.AncientBranchConditions.Where(b => b.OptionAny.Count > 0 || b.SeaGlassTargetAny.Count > 0).Select(b => b.Act)
            .Concat(e.AncientOptionFilters.Where(f => !f.IsEmpty).Select(f => f.Act))
            .Concat(e.AncientSeaGlassTargetFilters.Where(f => !f.IsEmpty).Select(f => f.Act)).Distinct().Order().ToArray();
        var compiled = new List<ActBinding>();
        var gates = new List<Beta110GpuAncientOptionPreGateGate>();
        bool gpu = request.Authority.PlayerSlotIndex >= 0 && request.Authority.PlayerSlotIndex < request.Authority.PlayersCount;
        foreach (int act in acts)
        {
            var branches = e.AncientBranchConditions.Where(b => b.Act == act).ToArray();
            bool legacy = e.AncientOptionFilters.Any(f => f.Act == act && !f.IsEmpty) || e.AncientSeaGlassTargetFilters.Any(f => f.Act == act && !f.IsEmpty);
            ModelKey[] possible = _generation.AncientEventContexts.Where(c => c.Act == act).Select(c => c.AncientKey).Distinct().ToArray();
            ModelKey[] keys = branches.Length > 0 ? branches.Select(b => b.AncientKey).Distinct().ToArray() : possible;
            foreach (var key in keys.Concat(possible)) Add(key);
            var c = Beta110AncientOptionFastPlanCompiler.Compile(_generation, _filter, act, keys, Add, _ids, request.Authority.PlayerSlotIndex);
            var fast = new WorldFastActPlan(act, 0, 0, 0, 0, [], 0, [], [], [], [], [], [], [], [], [], [], [],
                c.Plans, c.BranchPredicates, c.LegacyOptionPredicates, c.LegacySeaGlassPredicates, []);
            bool exact = c.AncientOptionExactOnlyPredicateCount == 0 && c.SeaGlassExactOnlyPredicateCount == 0;
            // Exact counters alone do not establish the provider's full row
            // contract. Legacy options additionally prove every hypothetical row;
            // SeaGlass target obligations retain their canonical reference path.
            bool legacyOptionsExact = legacy && exact && CanUseLegacyOptions(act, keys, fast);
            bool numerical = exact && (!legacy || legacyOptionsExact);
            var rows = numerical ? keys.Select(key =>
            {
                ushort id = _ids[key];
                return new NumericalRow(id, fast with {
                    AncientOptionPlans = fast.AncientOptionPlans.Where(p => p.AncientId == id).ToArray(),
                    AncientBranchOptionPredicates = fast.AncientBranchOptionPredicates.Where(p => p.AncientId == id).ToArray()
                });
            }).ToArray() : [];
            compiled.Add(new(act, keys, numerical, rows));
            gpu &= exact;
            foreach (var key in keys)
            {
                var rowPredicates = c.BranchPredicates.Where(b => b.AncientId == _ids[key]).ToArray();
                if (rowPredicates.Length == 0) rowPredicates = [new(_ids[key], [], [], false, false, false, false)];
                var plan = c.Plans.FirstOrDefault(p => p.AncientId == _ids[key]);
                if (legacy && plan is null) gpu = false;
                foreach (var branch in rowPredicates)
                    gates.Add(new(act, branch.AncientId, fast, plan, branch, branch.HasOptionCondition, branch.HasSeaGlassCondition));
            }
        }
        _acts = compiled.ToArray();
        var referenceRows = new Dictionary<(int Act, ModelKey Key), ReferenceRowBinding>();
        foreach (var act in _acts)
        {
            var optionFilters = _filter.AncientOptionFilters.Where(f => f.Act == act.Act).ToImmutableArray();
            var seaFilters = _filter.AncientSeaGlassTargetFilters.Where(f => f.Act == act.Act).ToImmutableArray();
            bool hasOptions = optionFilters.Any(f => !f.IsEmpty), hasSea = seaFilters.Any(f => !f.IsEmpty);
            foreach (var key in act.Keys)
            {
                var branches = _filter.AncientBranchConditions.Where(b => b.Act == act.Act && b.AncientKey == key).ToImmutableArray();
                var context = _generation.AncientEventContexts.FirstOrDefault(c => c.Act == act.Act && c.AncientKey == key && c.PlayerSlot == _request.Authority.PlayerSlotIndex)
                    ?? _generation.AncientEventContexts.FirstOrDefault(c => c.Act == act.Act && c.AncientKey == key && c.IsShared);
                referenceRows.Add((act.Act, key), new(context, branches, optionFilters, seaFilters,
                    branches.Any(b => b is { OptionAny.Count: 0, SeaGlassTargetAny.Count: 0 }) && !hasOptions && !hasSea, hasSea));
            }
        }
        _referenceRows = referenceRows.ToFrozenDictionary();
        SingleOptionPlan = gates.Count == 1 ? gates[0].OptionPlan : null;
        OptionPlans = gates.Where(g => g.OptionPlan is not null).Select(g => g.OptionPlan!).ToArray();
        ScratchWords = Math.Max(1, gates.Where(g => g.OptionPlan != null).Select(g => g.OptionPlan!.MaxScratchCount).DefaultIfEmpty(1).Max());
        Capacity = Math.Min(1 << 20, Math.Max(64, (32 * 1024 * 1024 / 4 / ScratchWords / 64) * 64));
        // CPU predicates and their projections are complete before optional GPU packing.
        Survival = FamilyAuxiliaryEvidence.Read("A.AncientOption", "InitialSurvival", () => AncientOptionPhysicalPricing.ResolveSurvival(request),
            FamilySurvivalProjection.Unresolved("A.AncientOption", "AuxiliarySurvivalFailed"), request.SnapshotFingerprint);
        Buffers = [];
        GpuPreparationIssue = !prepareGpu ? "CpuReferenceSelected" : !gpu || gates.Count == 0 ? "GpuPredicateNotAdmitted" : "";
        if (!prepareGpu || !gpu || gates.Count == 0) return;
        try
        {
            Buffers = (gpuPacker ?? PackGpu)(gates, ScratchWords, Capacity);
            GpuSupported = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            GpuPreparationIssue = "GpuPlanConstructionFailed";
            GpuPreparationFailureDetail = ex.ToString();
            RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundWarning("ancientGpuPreparationFailed=true;selected=CpuEventLocal;scope=A.AncientOption;exceptionJson=" + RolltheSpire2.Bootstrap.RuntimeLog.SafeJson(ex.ToString()));
        }
    }
    private static uint[][] PackGpu(IReadOnlyList<Beta110GpuAncientOptionPreGateGate> gates, int scratchWords, int capacity)
    {
        AncientOptionGpuPacking.Pack(gates, scratchWords, capacity,
            out var meta, out var gm, out var plans, out var pools, out var poolIds, out var others, out var bp, out var bi);
        return [meta, gm, plans, pools, poolIds, others, bp, bi];
    }
    private bool CanUseLegacyOptions(int act, ModelKey[] keys, WorldFastActPlan fast)
    {
        int optionCount = _filter.AncientOptionFilters.Count(f => f.Act == act && !f.IsEmpty);
        if (optionCount == 0 || keys.Length == 0 || fast.AncientOptionPredicates.Length != optionCount ||
            _generation.Profile != _request.ProfileId || _request.Authority.PlayerSlotIndex < 0 ||
            _request.Authority.PlayerSlotIndex >= _request.Authority.PlayersCount ||
            _filter.AncientSeaGlassTargetFilters.Any(f => f.Act == act && !f.IsEmpty) ||
            _filter.AncientBranchConditions.Any(b => b.Act == act && b.SeaGlassTargetAny.Count > 0)) return false;
        foreach (var key in keys)
        {
            var context = _generation.AncientEventContexts.FirstOrDefault(c => c.Act == act && c.AncientKey == key && c.PlayerSlot == _request.Authority.PlayerSlotIndex)
                ?? _generation.AncientEventContexts.FirstOrDefault(c => c.Act == act && c.AncientKey == key && c.IsShared);
            // These provider checks precede generation and identity precision.
            // The existing compiler separately proves generator/catalog/pool,
            // hook, modifier, context and RNG-root authority for the same row.
            if (context is null || context.CharacterKey != _request.CharacterKey ||
                context.Catalog is null || !key.IsValid ||
                !string.Equals(context.EventIdEntry, context.Catalog.EventIdEntry, StringComparison.Ordinal) ||
                !fast.AncientOptionPlans.Any(p => p.AncientId == _ids[key] && p.IsExact)) return false;
        }
        return true;
    }
    internal bool Matches(ulong root)
    {
        foreach (var act in _acts)
        {
            if (act.Keys.Length == 0)
            {
                ReferenceUnresolved(act.Act, null, "A.ReferenceActContextsMissing", reportUnresolved: true);
                continue;
            }
            bool pass = false;
            if (act.Exact)
            {
                foreach (var row in act.NumericalRows)
                {
                    var result = Beta110AncientOptionFastStage.Evaluate(root, row.AncientId, row.Fast);
                    if (result.Evaluation == WorldFastAncientOptionEvaluation.ConservativeKeep)
                        throw new InvalidOperationException("A.CompiledExactProjectionFailed");
                    pass = result.Evaluation != WorldFastAncientOptionEvaluation.Reject;
                    if (pass) break;
                }
            }
            else foreach (var key in act.Keys)
            {
                pass = ReferenceRow(root, act.Act, key, reportUnresolved: true) != SearchDisposition.NoMatch;
                if (pass) break;
            }
            if (!pass) return false;
        }
        return true;
    }
    internal bool Reference(ulong root) => ReferenceDisposition(root) != SearchDisposition.NoMatch;
    internal SearchDisposition ReferenceDisposition(ulong root)
    {
        bool unresolved = false;
        foreach (var act in _acts)
        {
            if (act.Keys.Length == 0) { unresolved = true; continue; }
            bool matched = false, rowUnresolved = false;
            foreach (var key in act.Keys)
            {
                var disposition = ReferenceRow(root, act.Act, key);
                if (disposition == SearchDisposition.Match) { matched = true; break; }
                rowUnresolved |= disposition != SearchDisposition.NoMatch;
            }
            if (!matched && !rowUnresolved) return SearchDisposition.NoMatch;
            unresolved |= !matched;
        }
        return unresolved ? SearchDisposition.Unknown : SearchDisposition.Match;
    }
    private SearchDisposition ReferenceRow(ulong root, int act, ModelKey key, bool reportUnresolved = false)
    {
        var row = _referenceRows[(act, key)];
        var branches = row.Branches;
        if (row.Unconditional) return SearchDisposition.Match;
        var context = row.Context;
        if (context is null) return Unresolved("A.ReferenceContextMissing");
        // Options start a fresh EventModel-local RNG. Preserve captured immutable
        // eligibility/shared assignment, derive only this hypothetical arrival root.
        var snapshot = _generation with
        {
            RunSeedRoot = root,
            AncientEventContexts = [context with {
            EventRngRoot=Beta109WorldRng.DeriveEventLocalSeed(root,context.PlayerSlot,context.IsShared,context.EventIdEntry)}]
        };
        var prediction = Beta109AncientOptionProvider.PredictEventLocal(snapshot, act, key, _request.Authority.PlayerSlotIndex,
            _request.CharacterKey, _filter.AncientOptionConditions);
        // Like Exact's option predicate, A consumes option identity precision,
        // not completeness of unrelated mutable post-obtain/setup projection.
        if (prediction.EvaluationStatus is not (AncientOptionsEvaluationStatus.EvaluatedNonEmpty or
            AncientOptionsEvaluationStatus.EvaluatedKnownEmpty or AncientOptionsEvaluationStatus.EvaluatedLocked) ||
            prediction.Options.Any(o => o.OptionPrecision != PredictionPrecision.Exact))
            return Unresolved("A.ReferenceNotExact:" + prediction.IssueCode);
        var options = prediction.Options.Where(o => o.IsVisible).ToArray();

        var sea = options.FirstOrDefault(o => o.OptionKey.Entry == "SEA_GLASS");
        bool branchMatched = branches.Length == 0, branchUnresolved = false;
        foreach (var branch in branches)
        {
            if (branch.OptionAny.Count > 0 && !options.Any(o => branch.OptionAny.Contains(o.OptionKey))) continue;
            if (branch.SeaGlassTargetAny.Count > 0)
            {
                if (sea is null) continue;
                if (sea.CharacterTarget is not { Precision: PredictionPrecision.Exact, CharacterKey: { } target })
                { branchUnresolved = true; continue; }
                if (!branch.SeaGlassTargetAny.Contains(target)) continue;
            }
            branchMatched = true;
            break;
        }
        if (!branchMatched && !branchUnresolved) return SearchDisposition.NoMatch;
        static bool Set(IEnumerable<ModelKey> values, ModelKeySetFilter f)
        {
            var v = values.ToArray(); return
            (f.Any.Count == 0 || f.Any.Any(v.Contains)) && f.All.All(v.Contains) && !f.Ban.Any(v.Contains);
        }
        if (!row.OptionFilters.All(f => Set(options.Select(o => o.OptionKey), f.Keys))) return SearchDisposition.NoMatch;
        if (sea is not null && row.HasSeaGlassFilter)
        {
            if (sea.CharacterTarget is not { Precision: PredictionPrecision.Exact, CharacterKey: { } target })
                return Unresolved("A.SeaGlassTargetNotExact:" + sea.CharacterTarget?.IssueCode);
            if (!row.SeaGlassFilters.All(f => Set([target], f.Keys))) return SearchDisposition.NoMatch;
        }
        return branchMatched ? SearchDisposition.Match : Unresolved("A.SeaGlassTargetNotExact:" + sea?.CharacterTarget?.IssueCode);

        SearchDisposition Unresolved(string cause) => ReferenceUnresolved(act, key, cause, reportUnresolved);
    }
    private SearchDisposition ReferenceUnresolved(int act, ModelKey? key, string cause, bool reportUnresolved)
    {
        // Retention is only a necessary-sieve decision. Production Exact
        // still owns the final disposition and its original domain causes.
        // Reference/parity/pricing probes never create execution notices.
        if (reportUnresolved && _reportedUnresolved.TryAdd((act, key), 0))
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation(
                "AncientOptionFilterIncomplete", nameof(AncientOptionFamilyPlan), $"Filter.Act{act}.{key?.Serialized ?? "Unscoped"}",
                "issues.filter_incomplete", $"act={act};ancient={key?.Serialized};cause={cause};retained=true;filterDecision=Unknown;requiresFinalCheck=true;matchClaimed=false");
        return SearchDisposition.Unknown;
    }
    internal string ShaderSource()
    {
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta110GpuAncientOptionPreGateP3A.comp.glsl");
        string numerical = donor[donor.IndexOf("struct RngState", StringComparison.Ordinal)..donor.IndexOf("void main()", StringComparison.Ordinal)];
        string source = FamilyGpuComputeUtility.LoadEmbeddedShader("AncientOptionFamily.comp.glsl").Replace("/*__DONOR__*/", numerical, StringComparison.Ordinal);
        if (NumericalVariant == GpuNumericalVariant.NativeFloat64) return source;
        if (NumericalVariant != GpuNumericalVariant.IntegerRne) throw new InvalidOperationException("A.UnknownGpuNumericalVariant");
        source = GpuIntegerNumerics.NormalizeLines(source);
        const string native = "double next_double(inout RngState r){return double(next_u64(r)>>11u)*1.1102230246251565e-16lf;}\n" +
            "float next_float(inout RngState r){return float(next_double(r));}\n" +
            "uint next_int(inout RngState r,uint bound){return uint(next_double(r)*double(bound));}";
        string integer = GpuIntegerNumerics.NextIntSource + "\n" + GpuIntegerNumerics.UnitFloatSource + "\n" +
            "float next_float(inout RngState r){uint64_t draw=next_u64(r);return rt2_next_float_from_words_u32(uvec2(uint(draw),uint(draw>>32u)));}\n" +
            "uint next_int(inout RngState r,uint bound){uint64_t draw=next_u64(r);return rt2_next_int_rne_raw32(uint(draw),uint(draw>>32u),bound);}";
        source = GpuIntegerNumerics.ReplaceUnique(source, native, integer, "A");
        return GpuIntegerNumerics.RequireNoFloat64(GpuIntegerNumerics.RemoveFloat64Extension(source, "A"), "A");
    }
}
