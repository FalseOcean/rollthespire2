using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// Complete invocation cost identity. Unlike a query fingerprint, WorkShape
// describes source work and target multiplicities/reach, never target identities.
internal sealed record FamilyCanonicalCostKey(int SchemaVersion, string FamilyId, string ExecutionClass,
    string RuntimeTarget, string CpuIdentity, string GpuIdentity, string RenderingBackend,
    string Profile, string PhysicalRevision, int ConfiguredWorkers, int Players, bool CompactInput,
    int RootWindow, string WorkShape, string OrderedChildBodies)
{
    [JsonIgnore] internal string StableKey => JsonSerializer.Serialize(this);
    // Current ordinary N GPU Invoke submits the same shaders, groups, ABI1
    // transfers and serial first-call parity irrespective of the request's CPU
    // worker budget. Exact and CPU partitions are outside this local boundary.
    // Keep the measured budget in the stored key; omit it only from the price
    // index for these reviewed bodies. CPU, Party and composed owners stay exact.
    [JsonIgnore] internal bool HasBudgetIndependentGpuWork => FamilyId == "N.Neow" &&
        ExecutionClass == "GPU" && Players == 1 && ConfiguredWorkers is >= 1 and <= 64 && OrderedChildBodies == "" &&
        PhysicalRevision.StartsWith("N.Neow.Gpu.LocalDonor.", StringComparison.Ordinal);
    [JsonIgnore] internal string PriceKey => HasBudgetIndependentGpuWork
        ? (this with { ConfiguredWorkers = 1 }).StableKey : StableKey;
}

internal static partial class FamilyCanonicalCostShape
{
    internal const int SchemaVersion = 1;

    // Null means no source-reviewed structural descriptor yet. It is not an
    // execution rejection and must not become a guessed/default canonical price.
    internal static FamilyCanonicalCostKey? Describe(ExactSearchExecutionRequest request,
        IFamilyInvocation invocation, bool compact, int rootWindow)
    {
        if (rootWindow <= 0) return null;
        IFamilyInvocation owner = invocation;
        if (invocation is FamilyCpuExecution cpu)
        {
            request = cpu.PricingRequest;
            owner = cpu.PricingOwner ?? invocation;
        }
        // This port admits only the reviewed solo public call boundary. Other
        // execution remains legal and continues through its existing pricing.
        if (!FamilyCanonicalMeasuredCost.InScope(request, invocation)) return null;
        string? work = WorkShape(request, owner, invocation);
        if (work is null) return null;
        if (invocation is FamilyCpuExecution optimizedCpu) work += optimizedCpu.OptimizationCostShape;
        else if (owner is NeowFamily optimizedNeow) work += optimizedNeow.PricingOptimizationShape(compact);
        if (invocation is FamilyCpuExecution { RequiredPassedCoverage: { } required })
            work += ";RequiredPassedCoverage=" + string.Join(',', required.Order(StringComparer.Ordinal));
        string children = "";
        if (owner is InitialRoomsInvocation { PricingOrdinary: { } child } initial)
        {
            var childKey = Describe(initial.PricingOrdinaryRequest, child, compact, rootWindow);
            if (childKey is null) return null;
            children = $"body={childKey.PhysicalRevision};workers={childKey.ConfiguredWorkers};shape={childKey.WorkShape};children={childKey.OrderedChildBodies}";
        }
        var device = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        bool gpu = invocation.ConditionPerformance.UsesGpu;
        return new(SchemaVersion, invocation.FamilyId, gpu ? "GPU" : "CPU", device.RuntimeTarget,
            device.CpuIdentity, gpu ? device.GpuIdentity : "", gpu ? device.RenderingBackend : "",
            request.ProfileId.ToString(), invocation.ResolveConditionPerformance(compact).PhysicalImplementationRevision,
            invocation is FamilyCpuExecution physical ? physical.Workers : request.WorkerCount,
            request.Authority.PlayersCount, compact, rootWindow, work, children);
    }

    private static string? WorkShape(ExactSearchExecutionRequest request, IFamilyInvocation owner, IFamilyInvocation actual)
    {
        if (owner is InitialRoomsInvocation initial)
        {
            var shape = initial.Plan.DescribePricingShape();
            return initial.FamilyId + ".InitialRooms.SourceShape.20261025.v1;" + shape.Key + ";Present=" +
                string.Join(',', shape.Targets.Select(t => t.PresentInSharedBag));
        }
        if (owner is not EventResultFamily events) return TryDescribeFamily(request, owner, actual);
        if (!events.Plan.NumericalSupported) return null;
        var plan = events.Plan;
        // Several predicates over the same transform share draws. A descriptor
        // needs their joint pool-membership geometry before admitting that case.
        if (plan.Conditions.Where(c => EventResultTransformSemantics.IsTransform(c.Kind))
            .GroupBy(c => c.Kind).Any(group => group.Count() > 1)) return null;
        var ordinary = plan.Conditions.Where(c => !EventResultTransformSemantics.IsTransform(c.Kind)).ToArray();
        var compiled = new EventResultNumericPredicate.Compiled(ordinary, plan.Authority);
        if (compiled.CanReject && CheapTrialTinkerCostShape(plan.PlayerSlot, plan.Conditions) is { } cheapTrialTinker)
            return cheapTrialTinker;
        var transforms = new List<string>();
        foreach (var condition in plan.Conditions.Where(c => EventResultTransformSemantics.IsTransform(c.Kind)))
        {
            var transform = MorphicGroveContainsPlan.Compile(condition);
            if (transform is null) return null;
            transforms.Add($"{condition.Kind}:draw={transform.DrawCount}:prefix={transform.Prefix}:pool={transform.TargetPositions.Length}:" +
                $"pair={transform.RequiresPair}:members={string.Join(',', Enumerable.Range(0, 4).Select(mask => transform.TargetPositions.Count(p => p == (uint)mask)))}");
        }
        int grabCount = Beta111EventResultCatalog.TrashHeapGrabCards.Count;
        int diveCount = Beta111EventResultCatalog.TrashHeapDiveRelics.Count;
        long low = 0, high = (long)grabCount * diveCount;
        var trashReach = new List<long>();
        foreach (var target in compiled.TrashTargets)
        {
            long scale = target.Kind == EventResultConditionKind.TrashHeapGrabCard ? diveCount : grabCount;
            if (target.Index < 0) high = low;
            else { low = Math.Max(low, target.Index * scale); high = Math.Min(high, (target.Index + 1L) * scale); }
            trashReach.Add(Math.Max(0, high - low));
        }
        // Counts/membership and overlap widths determine work/rejection reach;
        // actual card/relic/character identifiers and pool positions are omitted.
        string inventory = string.Join(',', compiled.InventoryTargets.Select(t =>
            $"{t.Kind}:present={t.Index >= 0}:inColorPool={t.Kind != EventResultConditionKind.ColorfulPhilosophersOfferedColor || compiled.ColorfulPool.Contains(t.Index)}"));
        var scalarGroups = new Dictionary<(string Role, int Value), int>();
        int Group(string role, int? value)
        {
            if (!value.HasValue) return -1;
            var key = (role, value.Value);
            if (!scalarGroups.TryGetValue(key, out int group)) scalarGroups.Add(key, group = scalarGroups.Count);
            return group;
        }
        string cheap = string.Join(',', compiled.CheapConditions.Select(c =>
            $"{c.Kind}:caseGroup={Group("Trial", (int?)c.TrialCase)}:typeGroup={Group("TinkerType", (int?)c.TinkerCardType)}:" +
            $"riderGroup={Group("TinkerRider", (int?)c.TinkerRider)}:case={c.TrialCase}:type={c.TinkerCardType}:" +
            $"rider={c.TinkerRider}:riderIndex={(c.TinkerRider.HasValue ? (int)c.TinkerRider.Value % 3 : -1)}"));
        string compiledColor = "";
        if (actual.ResolveConditionPerformance(false).PhysicalImplementationRevision.StartsWith("E.EventResult.Cpu.CompiledColor.", StringComparison.Ordinal))
        {
            var color = EventResultCpuPlan.TryCompile(request, plan);
            if (color is null) return null;
            compiledColor = ";CompiledColorAllowed=" + BitOperations.PopCount(color.AllowedRemovalMask);
        }
        string revision = compiled.CheapConditions.Length == 0 ? "E.SourceGroups.v1" : "E.SourceGroups.v2";
        return revision + ";slot=" + plan.PlayerSlot + ";canReject=" + compiled.CanReject +
            ";colorKnown=" + compiled.ColorfulPoolKnown + ";colorPool=" + compiled.ColorfulPool.Length +
            ";kinds=" + string.Join(',', ordinary.Select(c => c.Kind)) + ";cheap=" + cheap +
            ";trashDenominator=" + ((long)grabCount * diveCount) + ";trashPrefixReach=" + string.Join(',', trashReach) +
            ";inventory=" + inventory + ";transforms=" + string.Join('|', transforms) + compiledColor;
    }

    // A finite statistical cost class, not a claim of equal results at a root.
    // One Trial comparison and one compatible type/rider comparison have the
    // same shuffle/draw geometry. Multiple Tinker predicates retain the legacy
    // groups/riderIndex descriptor because they share post-type RNG outcomes.
    internal static string? CheapTrialTinkerCostShape(int slot, IReadOnlyList<EventResultSearchCondition> conditions)
    {
        if (slot < 0 || conditions.Count != 2 || conditions.Any(c => !c.IsValid) ||
            conditions.Count(c => c.Kind == EventResultConditionKind.TrialCase) != 1 ||
            conditions.Count(c => c.Kind == EventResultConditionKind.TinkerTimeTypeAndRider) != 1) return null;
        var tinker = conditions.Single(c => c.Kind == EventResultConditionKind.TinkerTimeTypeAndRider);
        if (tinker.TinkerCardType is not { } type || !Enum.IsDefined(type) ||
            tinker.TinkerRider is not { } rider || !Enum.IsDefined(rider) || (int)rider / 3 != (int)type) return null;
        return "E.CheapTrialTinker.SourceCost.20261021.v1;slot=" + slot + ";order=" +
            string.Join(',', conditions.Select(c => c.Kind)) + ";rider=CompatiblePresent";
    }
}
