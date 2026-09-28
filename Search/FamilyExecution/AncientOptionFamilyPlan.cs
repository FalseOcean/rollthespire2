using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;
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
    private readonly (int Act, ModelKey[] Keys, WorldFastActPlan Fast, bool Exact)[] _acts;
    private readonly Dictionary<ModelKey, ushort> _ids = new(ModelKeyComparer.Instance);
    internal bool GpuSupported { get; }
    internal int Capacity { get; }
    internal int ScratchWords { get; }
    internal WorldFastAncientOptionPlan? SingleOptionPlan { get; }
    internal IReadOnlyList<WorldFastAncientOptionPlan> OptionPlans { get; }
    internal string PricingSignature => string.Join('|', OptionPlans.Select(p =>
        $"{p.Generator}:s{p.MaxScratchCount}:" + string.Join(',', p.Pools.Select(pool => $"{pool.Role}={pool.Values.Length}"))));
    internal uint[][] Buffers { get; }
    internal FamilySurvivalProjection Survival { get; }
    internal AncientOptionFamilyPlan(ExactSearchExecutionRequest request)
    {
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
        var compiled = new List<(int, ModelKey[], WorldFastActPlan, bool)>();
        var gates = new List<Beta110GpuAncientOptionPreGateGate>();
        bool gpu = request.Authority.PlayerSlotIndex == 0;
        foreach (int act in acts)
        {
            var branches = e.AncientBranchConditions.Where(b => b.Act == act).ToArray();
            bool legacy = e.AncientOptionFilters.Any(f => f.Act == act && !f.IsEmpty) || e.AncientSeaGlassTargetFilters.Any(f => f.Act == act && !f.IsEmpty);
            ModelKey[] possible = _generation.AncientEventContexts.Where(c => c.Act == act).Select(c => c.AncientKey).Distinct().ToArray();
            ModelKey[] keys = branches.Length > 0 ? branches.Select(b => b.AncientKey).ToArray() : possible;
            foreach (var key in keys.Concat(possible)) Add(key);
            var c = Beta110AncientOptionFastPlanCompiler.Compile(_generation, _filter, act, possible, Add, _ids);
            var fast = new WorldFastActPlan(act, 0, 0, 0, 0, [], 0, [], [], [], [], [], [], [], [], [], [], [],
                c.Plans, c.BranchPredicates, c.LegacyOptionPredicates, c.LegacySeaGlassPredicates, []);
            bool exact = c.AncientOptionExactOnlyPredicateCount == 0 && c.SeaGlassExactOnlyPredicateCount == 0 && request.Authority.PlayerSlotIndex == 0;
            // Legacy unscoped Any/All/Ban and absent SeaGlass use canonical CPU
            // semantics. The historical fast evaluator may conservatively skip them.
            compiled.Add((act, keys, fast, exact && !legacy));
            gpu &= exact && !legacy && branches.Length > 0;
            foreach (var branch in c.BranchPredicates)
            {
                var plan = c.Plans.FirstOrDefault(p => p.AncientId == branch.AncientId);
                gates.Add(new(act, branch.AncientId, fast, plan, branch, branch.HasOptionCondition, branch.HasSeaGlassCondition));
            }
        }
        _acts = compiled.ToArray();
        SingleOptionPlan = gates.Count == 1 ? gates[0].OptionPlan : null;
        OptionPlans = gates.Where(g => g.OptionPlan is not null).Select(g => g.OptionPlan!).ToArray();
        ScratchWords = Math.Max(1, gates.Where(g => g.OptionPlan != null).Select(g => g.OptionPlan!.MaxScratchCount).DefaultIfEmpty(1).Max());
        Capacity = Math.Min(1 << 20, Math.Max(64, (32 * 1024 * 1024 / 4 / ScratchWords / 64) * 64));
        GpuSupported = gpu && gates.Count > 0;
        AncientOptionGpuPacking.Pack(gates, ScratchWords, Capacity,
            out var meta, out var gm, out var plans, out var pools, out var poolIds, out var others, out var bp, out var bi);
        Buffers = [meta, gm, plans, pools, poolIds, others, bp, bi];
        Survival = AncientOptionPhysicalPricing.ResolveSurvival(request);
    }
    internal bool Matches(ulong root)
    {
        foreach (var act in _acts)
        {
            bool pass = false;
            foreach (var key in act.Keys)
            {
                if (act.Exact)
                {
                    var result = Beta110AncientOptionFastStage.Evaluate(root, _ids[key], act.Fast);
                    if (result.Evaluation == WorldFastAncientOptionEvaluation.ConservativeKeep)
                        throw new InvalidOperationException("A.CompiledExactProjectionFailed");
                    pass = result.Evaluation != WorldFastAncientOptionEvaluation.Reject;
                }
                else pass = ReferenceRow(root, act.Act, key);
                if (pass) break;
            }
            if (!pass) return false;
        }
        return true;
    }
    internal bool Reference(ulong root) => _acts.All(a => a.Keys.Any(k => ReferenceRow(root, a.Act, k)));
    private bool ReferenceRow(ulong root, int act, ModelKey key)
    {
        var branch = _filter.AncientBranchConditions.FirstOrDefault(b => b.Act == act && b.AncientKey == key);
        if (branch is { OptionAny.Count: 0, SeaGlassTargetAny.Count: 0 } &&
            !_filter.AncientOptionFilters.Any(f => f.Act == act && !f.IsEmpty) &&
            !_filter.AncientSeaGlassTargetFilters.Any(f => f.Act == act && !f.IsEmpty)) return true;
        var context = _generation.AncientEventContexts.FirstOrDefault(c => c.Act == act && c.AncientKey == key && c.PlayerSlot == _request.Authority.PlayerSlotIndex)
            ?? _generation.AncientEventContexts.FirstOrDefault(c => c.Act == act && c.AncientKey == key && c.IsShared)
            ?? throw new InvalidOperationException("A.ReferenceContextMissing");
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
        if (prediction.Options.Count == 0 || prediction.Options.Any(o => o.OptionPrecision != PredictionPrecision.Exact))
            throw new InvalidOperationException("A.ReferenceNotExact:" + prediction.IssueCode);
        var options = prediction.Options.Where(o => o.IsVisible).ToArray();

        var sea = options.FirstOrDefault(o => o.OptionKey.Entry == "SEA_GLASS");
        if (branch is not null)
        {
            if (branch.OptionAny.Count > 0 && !options.Any(o => branch.OptionAny.Contains(o.OptionKey))) return false;
            if (branch.SeaGlassTargetAny.Count > 0 && (sea?.CharacterTarget is not { Precision: PredictionPrecision.Exact, CharacterKey: { } target } ||
                !branch.SeaGlassTargetAny.Contains(target))) return false;
        }
        static bool Set(IEnumerable<ModelKey> values, ModelKeySetFilter f)
        {
            var v = values.ToArray(); return
            (f.Any.Count == 0 || f.Any.Any(v.Contains)) && f.All.All(v.Contains) && !f.Ban.Any(v.Contains);
        }
        if (!_filter.AncientOptionFilters.Where(f => f.Act == act).All(f => Set(options.Select(o => o.OptionKey), f.Keys))) return false;
        if (sea is not null && _filter.AncientSeaGlassTargetFilters.Any(f => f.Act == act && !f.IsEmpty))
        {
            if (sea.CharacterTarget is not { Precision: PredictionPrecision.Exact, CharacterKey: { } target })
                throw new InvalidOperationException("A.SeaGlassTargetNotExact");
            if (!_filter.AncientSeaGlassTargetFilters.Where(f => f.Act == act).All(f => Set([target], f.Keys))) return false;
        }
        return true;
    }
    internal string ShaderSource()
    {
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta110GpuAncientOptionPreGateP3A.comp.glsl");
        string numerical = donor[donor.IndexOf("struct RngState", StringComparison.Ordinal)..donor.IndexOf("void main()", StringComparison.Ordinal)];
        return FamilyGpuComputeUtility.LoadEmbeddedShader("AncientOptionFamily.comp.glsl").Replace("/*__DONOR__*/", numerical, StringComparison.Ordinal);
    }
}
