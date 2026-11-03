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
    internal static FamilyCanonicalCostKey? Describe(FilterBodyDescription body, bool compact, int rootWindow)
    {
        if (rootWindow <= 0 || !FamilyCanonicalMeasuredCost.InScope(body)) return null;
        string? work = body.Cost.CanonicalWorkShape?.Invoke(compact);
        if (work is null) return null;
        work += body.Cost.OptimizationShape?.Invoke(compact) ?? "";
        if (body.RequiredPassedCoverage is { } required)
            work += ";RequiredPassedCoverage=" + string.Join(',', required.Order(StringComparer.Ordinal));
        string? children = body.Cost.OrderedChildBodies is null ? "" : body.Cost.OrderedChildBodies(compact, rootWindow);
        if (children is null) return null;
        var device = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        return new(SchemaVersion, body.FamilyId, body.UsesGpu ? "GPU" : "CPU", device.RuntimeTarget,
            device.CpuIdentity, body.UsesGpu ? device.GpuIdentity : "", body.UsesGpu ? device.RenderingBackend : "",
            body.ProfileId.ToString(), body.ResolveConditionPerformance(compact).PhysicalImplementationRevision,
            body.CpuWorkers, body.PlayersCount, compact, rootWindow, work, children);
    }

    internal static string? DescribeInitialRooms(string familyId, InitialRoomsPlan plan)
    {
        var shape = plan.DescribePricingShape();
        return familyId + ".InitialRooms.SourceShape.20261025.v1;" + shape.Key + ";Present=" +
            string.Join(',', shape.Targets.Select(t => t.PresentInSharedBag));
    }

    internal static string? DescribeChildBody(FilterBodyDescription child, bool compact, int rootWindow)
    {
        var key = Describe(child, compact, rootWindow);
        return key is null ? null : $"body={key.PhysicalRevision};workers={key.ConfiguredWorkers};shape={key.WorkShape};children={key.OrderedChildBodies}";
    }

    internal static string? DescribeEvent(EventResultFamilyPlan plan, uint? compiledColorAllowedMask = null)
    {
        if (!plan.NumericalSupported) return null;
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
        if (compiledColorAllowedMask is uint mask)
            compiledColor = ";CompiledColorAllowed=" + BitOperations.PopCount(mask);
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
