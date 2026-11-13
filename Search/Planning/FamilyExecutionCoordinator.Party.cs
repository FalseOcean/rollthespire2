using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

public static partial class FamilyExecutionCoordinator
{
    private delegate bool CreateFamily(SearchModel model, out FilterBodyDescription? body);

    private static FamilyExecutionPlan PlanParty(PreparedSearch request, IReadOnlyList<FilterBodyDescription> registered) =>
        request.RunOptions.ManualFamilyOrder is { Count: > 0 } manual
            ? FamilyPlanner.InManualOrder(registered, registered, manual)
            : FamilyPlanner.PlanSimple(registered, includeCpuAlternatives: true);

    private static IReadOnlyList<FilterBodyDescription> CreatePartyFamilies(SearchModel model,
        FilterExecutionPreferences preferences, bool gpu, bool gpuFloat64)
    {
        var result = new List<FilterBodyDescription>();
        var numerical = gpuFloat64 ? GpuNumericalVariant.NativeFloat64 : GpuNumericalVariant.IntegerRne;
        if (NeowFamily.TryCreateParty(model, preferences, out var neow, gpu,
            gpuPlanFactory: gpu && !gpuFloat64 ? NeowFamilyGpuPlan.TryCreateIntegerUnitFloat : null) && neow is not null)
            result.Add(neow);
        var children = model.CompiledSearch.PlayerSearches.Select(p => SearchPreparationCompiler.ForPlayer(model, p)).ToArray();
        Add(CreateShop);
        Add(CreateRelic);
        var arrivals = new Dictionary<int, (int? Niche, int? Potions)>();
        bool hasCombat = children.Any(c => c.CompiledSearch.NormalizedQuery.HasCombatRewardConstraints);
        int? niche = 0, potions = 0;
        foreach (var child in children.Take(model.CompiledSearch.Context.Party!.Players.Count))
        {
            arrivals[child.Authority.PlayerSlotIndex] = (niche, potions);
            if (!hasCombat || child.CompiledSearch.NormalizedQuery.OpeningRoute is null) continue;
            try
            {
                var advance = NeowFamily.PartyInitial.SharedAdvance(child, NeowReplayPlan.Compile(child, false));
                niche = niche.HasValue && advance.Niche.HasValue ? niche.Value + advance.Niche.Value : null;
                potions = potions.HasValue && advance.Potions.HasValue ? potions.Value + advance.Potions.Value : null;
            }
            catch (InvalidOperationException error)
            {
                niche = potions = null;
                RuntimeLog.TryBackgroundInfo($"partyOpeningPrefixUnresolved=true;slot={child.Authority.PlayerSlotIndex};reason={error.Message};owner=WholeTableExact");
            }
        }
        Add(CreateCombat);
        VariantOpeningFamily.TryPrepare(model, preferences, out var variant, gpu, numerical);
        if (variant is not null) result.Add(variant.Body);
        Add(CreateWorld, sharedOnly: true);
        Add(CreateAncient);
        Add(CreateEvents);
        RuntimeLog.TryBackgroundInfo($"partyFamilyRegistration=true;familyMajor=true;families={string.Join(',', result.Select(f => f.FamilyId))};gpuAvailable={gpu};cost=FamilyOwnedStageProjection");
        return result;

        bool CreateShop(SearchModel child, out FilterBodyDescription? body) =>
            MerchantShopColorlessFamily.TryCreate(child, preferences, out body, gpu);
        bool CreateRelic(SearchModel child, out FilterBodyDescription? body) =>
            NeowReplayPlan.HasCapsule(child) && child.Authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113
                ? CapsuleRelicFamily.TryCreate(child, preferences, out body, gpu, numerical)
                : RelicFamily.TryCreate(child, preferences, out body, gpu, numerical);
        bool CreateAncient(SearchModel child, out FilterBodyDescription? body) =>
            AncientOptionFamily.TryCreate(child, preferences, out body, gpu, numerical);
        bool CreateWorld(SearchModel child, out FilterBodyDescription? body) =>
            WorldFamily.TryCreate(child, preferences, out body, gpu, numerical, variant?.Plan.CheckedPreparation);
        bool CreateEvents(SearchModel child, out FilterBodyDescription? body) =>
            EventResultFamily.TryCreate(child, preferences, out body, gpu, numerical);
        bool CreateCombat(SearchModel child, out FilterBodyDescription? body)
        {
            var arrival = arrivals[child.Authority.PlayerSlotIndex];
            bool admitted = CombatRewardFamily.TryCreateParty(child, preferences, arrival.Niche, arrival.Potions,
                out body, gpu, numerical);
            if (!admitted && child.CompiledSearch.NormalizedQuery.HasCombatRewardConstraints)
                RuntimeLog.TryBackgroundInfo($"partyCombatRemainder=true;slot={child.Authority.PlayerSlotIndex};reason=UnsupportedProfileContextOrOpeningIdentity;owner=WholeTableExact");
            return admitted;
        }
        void Add(CreateFamily create, bool sharedOnly = false)
        {
            var stages = new List<PartyFilterStage>();
            foreach (var child in sharedOnly ? children.TakeLast(1) : children)
            {
                if (!create(child, out var body) || body is null) continue;
                if (!gpu || !gpuFloat64 && !CanRunWithoutFloat64(body))
                {
                    var cpu = body.CpuAlternatives.FirstOrDefault(candidate => candidate.CpuWorkers == Math.Clamp(preferences.WorkerCount, 1, 32))
                        ?? body.CpuAlternatives.FirstOrDefault();
                    if (cpu is not null) body = cpu;
                    else if (body.UsesGpu) throw new InvalidOperationException("Party.CpuRealizationMissing:" + body.FamilyId);
                }
                stages.Add(new(child.Authority.PlayerSlotIndex, body));
            }
            if (stages.Count > 0) result.Add(PartyFamily.Prepare(model, preferences, stages));
        }
    }
}
