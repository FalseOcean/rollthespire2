using System.Reflection;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class RitsuLibObservation
{
    // RitsuLib ships distinct assemblies for the same package version. Both
    // implement the observed registry, room filter and run notification paths.
    private static readonly Guid Compat111 = new("d2503f60-ef65-4343-9d72-d7add9712e96");
    private static readonly Guid Compat113 = new("316d1ca8-84e9-4001-8741-ab2830a20f33");

    internal static bool IsAuditedRuntime(Module module) =>
        module.ModuleVersionId == Compat111 || module.ModuleVersionId == Compat113;
}
