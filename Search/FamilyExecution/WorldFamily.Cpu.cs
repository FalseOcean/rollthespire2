using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class WorldFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations => [new FamilyCpuExecution(_request,this,"W.World.Cpu.Progression.20260907.v1",Replay.Matches,1,g=>WorldPhysicalPricing.QuoteCpu(_request,Replay,_gpuPlan,g))];
}
