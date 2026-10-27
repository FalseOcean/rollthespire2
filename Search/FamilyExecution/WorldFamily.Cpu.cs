using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class WorldFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            foreach (int workers in FamilyCpuExecution.WorkerConfigurations(_request))
                yield return new FamilyCpuExecution(_request, this, "W.World.Cpu.Progression.20261016.v2", Replay.Matches, workers,
                    g => workers == 1 ? WorldPhysicalPricing.QuoteCpu(_request, Replay, _gpuPlan, g) : null);
        }
    }
}
