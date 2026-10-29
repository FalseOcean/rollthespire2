using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

public static partial class FamilyExecutionCoordinator
{
    private delegate bool CreateFamily(ExactSearchExecutionRequest request, out IFamilyInvocation? family);

    private static FamilyExecutionPlan PlanParty(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered) => request.RunOptions.ManualFamilyOrder is { Count: > 0 } manual
            ? FamilyPlanner.InManualOrder(registered, registered, manual)
            : FamilyPlanner.PlanSimple(registered, includeCpuAlternatives: true);

    private static IReadOnlyList<IFamilyInvocation> CreatePartyFamilies(ExactSearchExecutionRequest request, bool gpu, bool gpuFloat64)
    {
        var result = new List<IFamilyInvocation>();
        if (NeowFamily.TryCreateParty(request, out var neow, gpu,
            gpuPlanFactory: gpu && !gpuFloat64 ? NeowFamilyGpuPlan.TryCreateIntegerUnitFloat : null) && neow is not null)
            result.Add(neow);
        var children = request.CompiledSearch.PlayerSearches.Select(p => ExactSearchExecutionRequestFactory.ForPlayer(request, p)).ToArray();
        Add(MerchantShopColorlessFamily.TryCreate);
        Add(CreateRelic);
        var arrivals = new Dictionary<int, (int? Niche, int? Potions)>();
        bool hasCombat = children.Any(c => c.CompiledSearch.NormalizedQuery.HasCombatRewardConstraints);
        int? niche = 0, potions = 0;
        foreach (var child in children.Take(request.CompiledSearch.Context.Party!.Players.Count))
        {
            arrivals[child.Authority.PlayerSlotIndex] = (niche, potions);
            if (!hasCombat || child.CompiledSearch.NormalizedQuery.OpeningRoute is null) continue;
            try
            {
                var advance = NeowFamily.PartyInitial.SharedAdvance(child, NeowReplayPlan.Compile(child, false));
                niche = niche.HasValue && advance.Niche.HasValue ? niche.Value + advance.Niche.Value : null;
                potions = potions.HasValue && advance.Potions.HasValue ? potions.Value + advance.Potions.Value : null;
            }
            catch (InvalidOperationException ex)
            {
                // Missing numerical prefix authority is not a query rejection.
                // Only downstream consumers of these shared streams are tainted.
                niche = potions = null;
                RuntimeLog.TryBackgroundInfo($"partyOpeningPrefixUnresolved=true;slot={child.Authority.PlayerSlotIndex};reason={ex.Message};owner=WholeTableExact");
            }
        }
        Add(CreateCombat);
        VariantOpeningFamily.TryCreate(request, out var variant, gpu,
            gpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne);
        if (variant is not null) result.Add(variant);
        Add(CreateWorld, sharedOnly: true);
        Add(CreateAncient);
        Add(CreateEvents);
        // M continues at whole-table Exact. The single-player bounded map sieve
        // is not a multiplayer map authority; no map GPU is introduced here.
        RuntimeLog.TryBackgroundInfo($"partyFamilyRegistration=true;familyMajor=true;families={string.Join(',', result.Select(f => f.FamilyId))};gpuAvailable={gpu};cost=FamilyOwnedStageProjection");
        return result;

        bool CreateRelic(ExactSearchExecutionRequest child, out IFamilyInvocation? family) =>
            NeowReplayPlan.HasCapsule(child) ? CapsuleRelicFamily.TryCreate(child, out family, gpu,
                gpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne) : RelicFamily.TryCreate(child, out family, gpu,
                    gpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne);
        bool CreateAncient(ExactSearchExecutionRequest child, out IFamilyInvocation? family) =>
            AncientOptionFamily.TryCreate(child, out family, gpu, gpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne);
        bool CreateWorld(ExactSearchExecutionRequest child, out IFamilyInvocation? family) =>
            WorldFamily.TryCreate(child, out family, gpu, gpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne,
                (variant as VariantOpeningFamily)?.Plan.CheckedPreparation);
        bool CreateEvents(ExactSearchExecutionRequest child, out IFamilyInvocation? family) =>
            EventResultFamily.TryCreate(child, out family, gpu, gpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne);

        bool CreateCombat(ExactSearchExecutionRequest child, out IFamilyInvocation? family)
        {
            var arrival = arrivals[child.Authority.PlayerSlotIndex];
            bool admitted = CombatRewardFamily.TryCreateParty(child, arrival.Niche, arrival.Potions, out family, gpu,
                gpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne);
            if (!admitted && child.CompiledSearch.NormalizedQuery.HasCombatRewardConstraints)
                RuntimeLog.TryBackgroundInfo($"partyCombatRemainder=true;slot={child.Authority.PlayerSlotIndex};reason=UnsupportedProfileContextOrOpeningIdentity;owner=WholeTableExact");
            return admitted;
        }

        void Add(CreateFamily create, bool sharedOnly = false)
        {
            var stages = new List<(ExactSearchExecutionRequest Request, IFamilyInvocation Family)>();
            foreach (var child in sharedOnly ? children.TakeLast(1) : children)
            {
                if (!create(child, out var family) || family is null) continue;
                if (!gpu || !gpuFloat64 && !CanRunWithoutFloat64(family))
                {
                    var cpu = FamilyCpuExecution.ConfiguredRealizations(family).FirstOrDefault();
                    if (cpu is not null) family = cpu;
                    else if (family.ConditionPerformance.UsesGpu)
                        throw new InvalidOperationException("Party.CpuRealizationMissing:" + family.FamilyId);
                }
                stages.Add((child, family));
            }
            if (stages.Count > 0) result.Add(new PartyFamily(request, stages.ToArray()));
        }
    }

}
