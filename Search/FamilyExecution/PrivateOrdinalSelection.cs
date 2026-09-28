using System.Text.Json;
using System.Text.Json.Serialization;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Explicit Owner input for existing bounded allocations, not pricing.
// Read once per plan; editing the file cannot change an in-flight session.
internal static class PrivateOrdinalSelection
{
    internal sealed record Selection(string Coverage, string Mode, string? Order = null);
    private static string PathForSelection => Environment.GetEnvironmentVariable("RT2_PRIVATE_ORDINAL_SELECTION")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2", "RolltheSpire2", "private-ordinal-candidate.json");
    internal static bool IsRequested => File.Exists(PathForSelection);
    internal static bool TrySelect(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered, FamilyExecutionPlan ordinary,
        out FamilyExecutionPlan? selected)
    {
        selected = null;
        string path = PathForSelection;
        if (!File.Exists(path)) return false;
        var choice = JsonSerializer.Deserialize<Selection>(File.ReadAllText(path), new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("PrivateOrdinal.SelectionMissing");
        if (choice.Coverage is not ("EW" or "NC" or "NWAE" or "NR") || choice.Mode is not ("ordinary" or "private" or "verify"))
            throw new InvalidDataException("PrivateOrdinal.UnknownSelection");
        if (choice.Coverage == "NR"
            ? choice.Mode == "ordinary" ? choice.Order is not null : choice.Order is not ("keep" or "serial" or "serial-direct" or "pregate-serial" or "rarity-serial" or "pregate-fused" or "entry-rarity-serial")
            : choice.Coverage == "NWAE" ? choice.Order is not ("ENAW" or "NEAW" or "EANW") : choice.Order is not null)
            throw new InvalidDataException("PrivateOrdinal.InvalidOrder");
        if (EwPhysicalExperiment.Mode.Length != 0 || NcPhysicalExperiment.Mode.Length != 0)
            throw new InvalidOperationException("PrivateOrdinal.ConflictingSelection");
        if (choice.Coverage == "NR")
        {
            string capsuleMode=choice.Mode=="ordinary"?"fused":(choice.Mode=="verify"?"verify-":"")+choice.Order;
            return DirectCapsuleAllocation.TrySelect(request,registered,out selected,capsuleMode);
        }
        if (choice.Coverage == "NWAE")
        {
            bool applicable = registered.Count == 4
                && registered.OfType<NeowFamily>().SingleOrDefault()?.GpuPlan is not null
                && registered.OfType<AncientOptionFamily>().SingleOrDefault() is { Plan.GpuSupported: true }
                && registered.OfType<EventResultFamily>().SingleOrDefault() is { Plan.GpuSupported: true }
                && registered.OfType<WorldFamily>().SingleOrDefault() is { } world && WorldFamilyGpuPlan.Supports(world.Replay);
            Bootstrap.RuntimeLog.TryBackgroundInfo($"privateOrdinalCandidateRequested=true;coverage=NWAE;mode={choice.Mode};order={choice.Order};applicable={applicable};normalPlan={ordinary.PlanId};pricingChanged=false");
            if (!applicable) return false;
            return NwaePhysicalExperiment.TrySelect(request, registered, ordinary, out selected, choice.Mode + "-" + choice.Order);
        }
        bool supported = registered.Count == 2 && (choice.Coverage == "EW"
            ? registered.OfType<EventResultFamily>().SingleOrDefault() is { Plan.GpuSupported: true }
                && registered.OfType<WorldFamily>().SingleOrDefault() is { } w && WorldFamilyGpuPlan.Supports(w.Replay)
            : registered.OfType<CombatRewardFamily>().SingleOrDefault()?.GpuPlan is not null
                && registered.OfType<NeowFamily>().SingleOrDefault()?.GpuPlan is not null);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"privateOrdinalCandidateRequested=true;coverage={choice.Coverage};mode={choice.Mode};applicable={supported};normalPlan={ordinary.PlanId};pricingChanged=false");
        if (!supported) return false; // Physical non-applicability, before selection.
        string mode = choice.Coverage == "EW"
            ? choice.Mode == "ordinary" ? "ordinary-ew" : choice.Mode + "-ordinal"
            : choice.Mode == "ordinary" ? "ordinary-nc" : choice.Mode + "-nc";
        return choice.Coverage == "EW"
            ? EwPhysicalExperiment.TrySelect(request, registered, ordinary, out selected, mode)
            : NcPhysicalExperiment.TrySelect(request, registered, ordinary, out selected, mode);
    }
}
