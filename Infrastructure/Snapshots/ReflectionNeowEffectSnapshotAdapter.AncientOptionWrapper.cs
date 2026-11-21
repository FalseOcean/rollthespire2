using System.Collections;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    private sealed record AncientWrapperObservation(string UnknownReason, Type[] RegisteredTypes)
    {
        internal bool CanReplay(Type ancient) =>
            UnknownReason.Length == 0 && !RegisteredTypes.Any(type => type.IsAssignableFrom(ancient));
    }

    private static AncientWrapperObservation ObserveAncientOptionWrapper(HashSet<MethodBase> patched)
    {
        const string patchType = "STS2RitsuLib.Scaffolding.Content.Patches.AncientEventInitialOptionsRegistryPatch";
        const string registryType = "STS2RitsuLib.Scaffolding.Ancients.Options.ModAncientOptionRegistry";
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Static;
        var wrapper = AccessTools.DeclaredMethod(typeof(AncientEventModel), "GenerateInitialOptionsWrapper");
        if (wrapper == null || !patched.Contains(wrapper)) return new("", []);

        var info = Harmony.GetPatchInfo(wrapper);
        const string target = "AncientEventModel.GenerateInitialOptionsWrapper";
        if (info == null || info.Prefixes.Count != 0 || info.Transpilers.Count != 0 ||
            info.InnerPrefixes.Count != 0 || info.InnerPostfixes.Count != 0 || info.Finalizers.Count != 0)
            return new(target + ":" + string.Join(",", info?.Owners.AsEnumerable() ?? []), []);

        var affectedTypes = new HashSet<Type>();
        foreach (var patch in info.Postfixes)
        {
            var method = patch.PatchMethod;
            if (!RitsuLibObservation.IsAuditedRuntime(method.Module) ||
                method.DeclaringType?.FullName != patchType || method.Name != "Postfix")
                return new(target + ":" + patch.owner + ":" + method.DeclaringType?.FullName + "." + method.Name, []);

            var registry = method.Module.Assembly.GetType(registryType);
            var content = method.Module.Assembly.GetType("STS2RitsuLib.Content.ModContentRegistry");
            // An added hook on the registry or the postfix helpers can inject options
            // even when RitsuLib's original registration table is empty.
            if (patched.Any(m =>
                m.DeclaringType == method.DeclaringType &&
                    m.Name is "Postfix" or "ReplaceDebugRelicOptionWithPreparedOption" or "ShouldSkipInjection" ||
                m.DeclaringType == registry && m.Name is "AppendRegisteredOptions" or "GetApplicableRulesSnapshot"))
                return new(target + ":RitsuRegistryBehaviorPatched", []);
            try
            {
                if (content?.GetField("<IsFrozen>k__BackingField", fields)?.GetValue(null) is not true ||
                    registry?.GetField("SyncRoot", fields)?.GetValue(null) is not System.Threading.Lock sync ||
                    registry.GetField("RulesByAncientType", fields)?.GetValue(null) is not IDictionary rules)
                    return new(target + ":RitsuRegistryUnavailable", []);
                lock (sync)
                    foreach (Type type in rules.Keys) affectedTypes.Add(type);
            }
            catch (Exception ex)
            {
                RecordCaptureFailure(ex, registryType);
                return new(target + ":RitsuRegistryReadFailed", []);
            }
        }
        return new("", affectedTypes.ToArray());
    }
}
