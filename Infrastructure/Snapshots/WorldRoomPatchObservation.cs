using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Diagnostics;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class WorldRoomPatchObservation
{
    private const string EventFilter = "STS2RitsuLib.Unlocks.Patches.GeneratedRoomEventUnlockFilterPatch";
    private const string UnlockRegistry = "STS2RitsuLib.Unlocks.ModUnlockRegistry";
    private const BindingFlags StaticFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

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
                if (check == null || unlock == null) return null;
                var locked = new List<ModelKey>();
                foreach (var id in ids)
                {
                    var model = ModelDb.GetByIdOrNull<EventModel>(id);
                    // An event supplied by an external pool can be absent from the
                    // canonical catalog. Its type matters for mod-owned epoch overrides.
                    if (model == null || check.Invoke(null, [model, unlock]) is not bool allowed) return null;
                    if (!allowed) locked.Add(new(id.Category, id.Entry));
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

    private static void RecordFailure(Exception error, string stage) =>
        RuntimeIssueLedger.Current.Record(error, "WorldRoomPatchObservationFailed",
            nameof(WorldRoomPatchObservation), stage, "issues.capture_failed");
}
