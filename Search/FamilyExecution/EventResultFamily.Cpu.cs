using RolltheSpire2.Search.Contracts;
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
                yield return new FamilyCpuExecution(_request,this,"E.EventResult.Cpu.CompiledColor.20260913.v1",
                    compiled.Matches,1,g=>EventResultPhysicalPricing.QuoteCpuCompiledColor(_request,Plan,compiled,g));
            yield return new FamilyCpuExecution(_request,this,"E.EventResult.Cpu.EventLocal.20260907.v1",
                Plan.Matches,1,g=>EventResultPhysicalPricing.QuoteCpu(_request,Plan,g));
        }
    }
}
