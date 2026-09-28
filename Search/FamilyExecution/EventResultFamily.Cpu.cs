using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class EventResultFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            var compiled = EventResultCpuPlan.TryCompile(_request,Plan);
            bool inventory = _request.Authority.CanUseCurrentModel && _request.Authority.PlayersCount == 1 &&
                Plan.GpuSupported && Plan.PlayerSlot == 0 &&
                Plan.Conditions.Count(c => c.Kind == EventResultConditionKind.FakeMerchantOfferedFakeRelic) == 6 &&
                Plan.Conditions.Where(c => c.Kind == EventResultConditionKind.FakeMerchantOfferedFakeRelic)
                    .All(c => Beta111EventResultCatalog.FakeMerchantRelics.Contains(c.TargetKey)) &&
                Plan.Conditions.Count(c => c.Kind == EventResultConditionKind.TrashHeapGrabCard) == 1 &&
                Plan.Conditions.Where(c => c.Kind == EventResultConditionKind.TrashHeapGrabCard)
                    .All(c => Beta111EventResultCatalog.TrashHeapGrabCards.Contains(c.TargetKey));
            bool rareColor = inventory && compiled is not null && Plan.Conditions.Length == 10 &&
                Plan.Conditions.Count(c => c.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor) == 3 &&
                System.Numerics.BitOperations.PopCount(compiled.AllowedRemovalMask) == 1;
            bool rareLocal = inventory && compiled is null && Plan.Conditions.Length == 7;
            if (compiled is not null)
                foreach (int workers in (rareColor ? new[] { 1, 8 } : new[] { 1 }).Where(w => w <= _request.WorkerCount))
                yield return new FamilyCpuExecution(_request,this,"E.EventResult.Cpu.CompiledColor.20260913.v1",
                    compiled.Matches,workers,g=>workers == 1 ? EventResultPhysicalPricing.QuoteCpuCompiledColor(_request,Plan,compiled,g) : null);
            foreach (int workers in (rareLocal ? new[] { 1, 8 } : new[] { 1 }).Where(w => w <= _request.WorkerCount))
                yield return new FamilyCpuExecution(_request,this,CpuRevision,
                    Plan.Matches,workers,g=>workers == 1 ? EventResultPhysicalPricing.QuoteCpu(_request,Plan,g) : null);
        }
    }
}
