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
            if (compiled is not null)
                foreach (int workers in FamilyCpuExecution.WorkerConfigurations(_request))
                yield return new FamilyCpuExecution(_request,this,"E.EventResult.Cpu.CompiledColor.20261015.v3",
                    compiled.Matches,workers,g=>workers == 1 ? EventResultPhysicalPricing.QuoteCpuCompiledColor(_request,Plan,compiled,g) : null);
            foreach (int workers in FamilyCpuExecution.WorkerConfigurations(_request))
                yield return new FamilyCpuExecution(_request,this,CpuRevision,
                    Plan.Matches,workers,g=>workers == 1 ? EventResultPhysicalPricing.QuoteCpu(_request,Plan,g) : null);
        }
    }
}
