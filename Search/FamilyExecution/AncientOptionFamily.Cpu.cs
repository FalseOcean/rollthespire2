using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class AncientOptionFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations => [new FamilyCpuExecution(_request,this,"A.AncientOption.Cpu.EventLocal.20260907.v1",Plan.Matches,1,g=>AncientOptionPhysicalPricing.QuoteCpu(_request,Plan,g))];
}
