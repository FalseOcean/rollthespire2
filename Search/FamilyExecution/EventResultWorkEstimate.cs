using RolltheSpire2.Core.Events;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class EventResultWorkEstimate
{
    private static (double Cpu, double Gpu, string Evidence) Work(EventResultFamilyPlan plan,
        IEnumerable<EventResultSearchCondition> conditions)
    {
        var rows = conditions.ToArray();
        bool trash = rows.Any(c => c.Kind is EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic);
        bool fake = rows.Any(c => c.Kind == EventResultConditionKind.FakeMerchantOfferedFakeRelic);
        bool color = rows.Any(c => c.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor);
        int colors = color ? Beta111EventResultCatalog.ColorfulCharacterOrder.Count(key => key != plan.Authority.OwnerCharacterKey &&
            plan.Authority.UnlockedCharacterCardPoolKeys.Contains(key)) : 0;
        int transforms = rows.Count(c => EventResultTransformSemantics.IsTransform(c.Kind));
        int scalar = rows.Select(c => c.Kind).Distinct().Count(k => k is EventResultConditionKind.TrialCase or
            EventResultConditionKind.TinkerTimeTypeAndRider);
        // Trash shares one fact; Fake shuffles once; Color processes its actual
        // inventory. Transform predicates retain their own numerical generation.
        double cpu = 30 + (trash ? 45 : 0) + (fake ? 120 : 0) + 90 * colors +
            rows.Where(c => EventResultTransformSemantics.IsTransform(c.Kind)).Sum(c =>
                c.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains ? 35 : 20) +
            20 * scalar + 4 * Math.Max(0, rows.Length - 1);
        double gpu = .35 + (trash ? .3 : 0) + (fake ? .7 : 0) + .08 * colors +
            .25 * transforms + .15 * scalar + .04 * Math.Max(0, rows.Length - 1);
        return (cpu, gpu, $"E.OperatorGroupAndWhitelistAnchors;TrashFacts={(trash ? 1 : 0)};FakeFacts={(fake ? 1 : 0)};" +
            $"ColorInventory={colors};TransformCalls={transforms};ScalarGroups={scalar};Comparisons={rows.Length};" +
            "SharedFactsOnce;NoTargetOrFullPredicateOrderKey;NoEarlyRejectDiscount");
    }

    internal static FamilyPhysicalQuote? Gpu(EventResultFamilyPlan plan, FamilyPhysicalQuoteRequest g)
    {
        var work = Work(plan, plan.Conditions);
        return FamilyCoarsePricing.Gpu("E.EventResult", "E.OperatorFacts", work.Gpu,
            plan.Capacity, g, work.Evidence, 100);
    }

    internal static FamilyPhysicalQuote? Cpu(EventResultFamilyPlan plan, int workers, FamilyPhysicalQuoteRequest g,
        EventResultCpuPlan? compiled = null, int capacity = 65536)
    {
        var work = Work(plan, compiled?.Remaining ?? plan.Conditions);
        double ns = work.Cpu;
        string evidence = work.Evidence;
        if (compiled is not null)
        {
            double reach = System.Numerics.BitOperations.PopCount(compiled.AllowedRemovalMask) / 4d;
            ns = 25 + reach * Math.Max(0, ns - 30);
            evidence += $";CompiledColorBase25;OwnRemainingReach={reach:G17}";
        }
        if (!plan.NumericalSupported) { ns *= 4; evidence += ";ReferenceProjectionConservativeWorkFactor=4"; }
        if (g.CompactInput) ns += 17;
        return FamilyCoarsePricing.Cpu("E.EventResult", compiled is null ? "E.OperatorFacts" : "E.CompiledColor",
            ns, workers, g, evidence, capacity);
    }
}
