using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class AncientOptionFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations => FamilyCpuExecution.WorkerConfigurations(_request).Select(workers =>
        new FamilyCpuExecution(_request,this,"A.AncientOption.Cpu.EventLocal.20261016.v3",Plan.Matches,workers,
            g=>workers==1 ? AncientOptionPhysicalPricing.QuoteCpu(_request,Plan,g) : null));
}
