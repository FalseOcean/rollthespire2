using System.Collections;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Diagnostics;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class WorldRoomPatchObservation
{
    private static readonly Guid BaseLibModule = new("a5354499-4a1f-43c8-9908-5d0f827bb473");
    private const string StateObserver = "BaseLib.Patches.Content.CurrentGeneratingRunState";
    private const string AncientInjection = "BaseLib.Patches.Content.AddCustomAncientsToPool";
    private const string AncientValidity = "STS2RitsuLib.Scaffolding.Content.Patches.ModAncientActValidityPatch";
    private const string ValidityFilter = "STS2RitsuLib.Scaffolding.Content.ModAncientActValidityFilter";
    private const string EventFilter = "STS2RitsuLib.Unlocks.Patches.GeneratedRoomEventUnlockFilterPatch";
    private const string UnlockRegistry = "STS2RitsuLib.Unlocks.ModUnlockRegistry";
    private const string ContentRegistry = "STS2RitsuLib.Content.ModContentRegistry";
    private const BindingFlags StaticFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    internal static bool CanReplay(MethodBase target, IReadOnlySet<MethodBase> patched,
        IReadOnlyCollection<Type>? sharedAncientTypes, Func<MethodInfo, bool> acceptsPostfix)
    {
        var info = Harmony.GetPatchInfo(target);
        if (info == null || info.Transpilers.Count != 0 || info.Finalizers.Count != 0 ||
            info.InnerPrefixes.Count != 0 || info.InnerPostfixes.Count != 0) return false;
        try
        {
            return info.Prefixes.All(p => Known(p.PatchMethod, true)) &&
                   info.Postfixes.All(p => Known(p.PatchMethod, false));
        }
        catch (Exception ex)
        {
            RecordFailure(ex, target.Name);
            return false;
        }

        bool Known(MethodInfo method, bool prefix)
        {
            var module = method.Module;
            var owner = method.DeclaringType?.FullName;
            if (module.ModuleVersionId == BaseLibModule && target.DeclaringType == typeof(RunManager) &&
                owner == StateObserver && method.Name == (prefix ? "GetState" : "ClearState"))
                return !patched.Contains(method) && !patched.Any(m =>
                    m.Module == module && m.DeclaringType?.FullName == StateObserver && m.Name == "set_State" ||
                    prefix && m.DeclaringType == typeof(RunManager) && m.Name == "get_State");
            if (target.DeclaringType != typeof(ActModel)) return false;
            if (module.ModuleVersionId == BaseLibModule && owner == AncientInjection &&
                method.Name == (prefix ? "AddToModelPool" : "ForceAncientToSpawn"))
            {
                // Both the subset insertions and the force-spawn lookup iterate this list.
                var catalog = module.Assembly.GetType("BaseLib.Patches.Content.CustomContentDictionary");
                return catalog?.GetField("CustomAncients", StaticFields)?.GetValue(null) is ICollection { Count: 0 } &&
                    !patched.Contains(method) && (!prefix || !patched.Any(m =>
                        m.Module == module && m.DeclaringType?.FullName == StateObserver && m.Name == "get_State"));
            }
            if (RitsuLibObservation.IsAuditedRuntime(module) && owner == AncientValidity && prefix && method.Name == "Prefix")
            {
                var rule = module.Assembly.GetType("STS2RitsuLib.Scaffolding.Content.IModAncientActValidity");
                return rule != null && sharedAncientTypes != null && !sharedAncientTypes.Any(rule.IsAssignableFrom) &&
                    !patched.Any(m => m.Module == module && (BelongsTo(m, AncientValidity) ||
                        m.DeclaringType?.FullName == ValidityFilter && m.Name == "IsValidForAct"));
            }
            if (RitsuLibObservation.IsAuditedRuntime(module) && owner == EventFilter && !prefix && method.Name == "Postfix")
                return !patched.Any(m =>
                    m.Module == module && (BelongsTo(m, EventFilter) ||
                        m.DeclaringType?.FullName == UnlockRegistry && m.Name == "IsUnlocked" ||
                        m.DeclaringType?.FullName == ContentRegistry && m.Name == "TryGetOwnerModId") ||
                    m.DeclaringType == typeof(UnlockState) && m.Name == "ToSerializable");
            return !prefix && acceptsPostfix(method);
        }
    }

    // Native filtering occurs after the RNG shuffle. Evaluate only registered event
    // IDs against the supplied run unlock state. The audited predicate only performs
    // locked registry lookups and reads the serialized revealed epoch IDs.
    internal static IReadOnlyList<ModelKey>? CaptureLockedEvents(IReadOnlySet<MethodBase> patched, UnlockState? unlock,
        IReadOnlySet<ModelKey> eventCandidates)
    {
        var method = patched.Where(m => m.DeclaringType == typeof(ActModel) && m.Name == "GenerateRooms")
            .SelectMany(m => Harmony.GetPatchInfo(m)?.Postfixes.Select(p => p.PatchMethod) ?? [])
            .FirstOrDefault(m => RitsuLibObservation.IsAuditedRuntime(m.Module) && m.DeclaringType?.FullName == EventFilter && m.Name == "Postfix");
        if (method == null) return [];
        try
        {
            var type = method.Module.Assembly.GetType(UnlockRegistry);
            if (type?.GetField("<IsFrozen>k__BackingField", StaticFields)?.GetValue(null) is not true ||
                type.GetField("SyncRoot", StaticFields)?.GetValue(null) is not System.Threading.Lock sync ||
                type.GetField("RequiredEpochsByModelId", StaticFields)?.GetValue(null) is not Dictionary<ModelId,string> rules)
                return null;
            lock (sync)
            {
                var ids = rules.Keys.Where(id => eventCandidates.Contains(new ModelKey(id.Category, id.Entry))).ToArray();
                if (ids.Length == 0) return [];
                var check = type.GetMethod("IsUnlocked", BindingFlags.Static | BindingFlags.NonPublic);
                if (check == null || unlock == null || patched.Any(m => m == check ||
                    m.Module == method.Module && m.DeclaringType?.FullName == ContentRegistry && m.Name == "TryGetOwnerModId" ||
                    m.DeclaringType == typeof(UnlockState) && m.Name == "ToSerializable"))
                    return null;
                var locked = new List<ModelKey>();
                foreach (var id in ids)
                {
                    var model = ModelDb.GetByIdOrNull<EventModel>(id);
                    // An event supplied by an external pool can be absent from the
                    // canonical catalog. Its type matters for mod-owned epoch overrides.
                    if (model == null) return null;
                    if (check.Invoke(null, [model, unlock]) is false) locked.Add(new(id.Category, id.Entry));
                }
                return locked.OrderBy(k => k.Serialized, StringComparer.Ordinal).ToArray();
            }
        }
        catch (Exception ex)
        {
            RecordFailure(ex, UnlockRegistry + ".IsUnlocked");
            return null;
        }
    }

    private static bool BelongsTo(MethodBase method, string typeName) =>
        method.DeclaringType?.FullName == typeName || method.DeclaringType?.DeclaringType?.FullName == typeName;

    private static void RecordFailure(Exception error, string stage) =>
        RuntimeIssueLedger.Current.Record(error, "WorldRoomPatchObservationFailed",
            nameof(WorldRoomPatchObservation), stage, "issues.capture_failed");
}
