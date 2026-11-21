using System.Collections;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    private static readonly Guid CardGuard093Module = new("3b36e7df-562f-4fd2-9f1a-439ffe4bb43d");

    // CardGuard's room postfix returns before applying its replacement policy when
    // the selected Ancient belongs to the game assembly, including shared Ancients.
    private static bool CanReplayCardGuardAncientFilter(MethodInfo hook, IReadOnlySet<MethodBase> patched,
        IReadOnlyCollection<Type>? sharedAncientTypes, bool allLocalAncientsNative) =>
        hook.Module.ModuleVersionId == CardGuard093Module &&
        hook.DeclaringType?.FullName == "Sts2CardGuard.Patches.ActModelGenerateRooms_AncientFilter_Patch" &&
        hook.Name == "Postfix" && allLocalAncientsNative && sharedAncientTypes is not null &&
        sharedAncientTypes.All(t => t.Assembly == typeof(MegaCrit.Sts2.Core.Models.AncientEventModel).Assembly) &&
        !patched.Contains(hook) && !patched.Any(m => m.Module == hook.Module &&
            m.DeclaringType?.FullName == "Sts2CardGuard.RelicGuardService" && m.Name == "get_IsPassThrough");

    private static MethodBase? UnknownRelicInitializationHook(IReadOnlySet<MethodBase> patched,
        bool sharedRelicsNative, bool characterRelicsNative)
    {
        return patched.FirstOrDefault(method => method.DeclaringType?.FullName switch
        {
            "MegaCrit.Sts2.Core.Runs.RunManager" => method.Name == "InitializeNewRun" &&
                !CanReplayPatchedRunInitialization(method, patched),
            "MegaCrit.Sts2.Core.Runs.RelicGrabBag" => method.Name == "Shuffle" || method.Name == "Populate" &&
                !CanReplayPatchedBagPopulation(method, patched, sharedRelicsNative, characterRelicsNative),
            "MegaCrit.Sts2.Core.Extensions.ListExtensions" => method.Name == "UnstableShuffle",
            "MegaCrit.Sts2.Core.Random.Rng" => method.Name is "NextUnsignedLong" or "NextInt" or "NextDouble",
            _ => false
        });
    }

    // CardGuard's two Populate postfixes only remove blocked, non-game relic instances.
    private static bool CanReplayPatchedBagPopulation(MethodBase target, IReadOnlySet<MethodBase> patched,
        bool sharedRelicsNative, bool characterRelicsNative)
    {
        bool playerBag = target.GetParameters().FirstOrDefault()?.ParameterType == typeof(Player);
        if (!sharedRelicsNative || playerBag && !characterRelicsNative) return false;
        var info = Harmony.GetPatchInfo(target);
        if (info is null || info.Prefixes.Count != 0 || HasOtherPatchKinds(info)) return false;
        string filter = playerBag ? "RelicGrabBag_PopulatePlayer_Patch" : "RelicGrabBag_PopulateShared_Patch";
        foreach (var patch in info.Postfixes)
        {
            var method = patch.PatchMethod;
            if (method.Module.ModuleVersionId != CardGuard093Module || method.Name != "Postfix" ||
                method.DeclaringType?.FullName != "Sts2CardGuard.Patches." + filter) return false;
            if (patched.Any(m => m.Module == method.Module && (m.DeclaringType == method.DeclaringType ||
                m.DeclaringType?.DeclaringType == method.DeclaringType ||
                m.DeclaringType?.FullName?.StartsWith("Sts2CardGuard.Patches.RelicFilterUtil", StringComparison.Ordinal) == true ||
                m.DeclaringType?.FullName == "Sts2CardGuard.RelicGuardService" &&
                    m.Name is "get_IsPassThrough" or "IsModRelic" or "IsRelicBlockedForCharacter" or "IsRelicBlockedForAny")))
                return false;
        }
        return true;
    }

    private static bool HasOtherPatchKinds(Patches info) =>
        info.Transpilers.Count != 0 || info.Finalizers.Count != 0 ||
        info.InnerPrefixes.Count != 0 || info.InnerPostfixes.Count != 0;

    // In the audited RitsuLib build these hooks deliver two notifications. The prefix
    // can additionally restore a pending payload. Inspect registrations without invoking
    // a subscriber, consuming the payload, or creating a lifecycle topic.
    private static bool CanReplayPatchedRunInitialization(MethodBase target, IReadOnlySet<MethodBase> patched)
    {
        var info = Harmony.GetPatchInfo(target);
        if (info is null || HasOtherPatchKinds(info)) return false;
        foreach (var patch in info.Prefixes.Concat(info.Postfixes))
        {
            var method = patch.PatchMethod;
            bool preparing = info.Prefixes.Contains(patch);
            if (!RitsuLibObservation.IsAuditedRuntime(method.Module)) return false;
            if (!preparing && method.DeclaringType?.FullName == "STS2RitsuLib.Models.HookedSingletonInitializePatch" &&
                method.Name == "Postfix")
            {
                if (!HasNoRunSingletons(method, patched)) return false;
                continue;
            }
            string owner = preparing ? "STS2RitsuLib.RunData.Patches.RunSavedDataInitializeNewRunPatch" :
                "STS2RitsuLib.Lifecycle.Patches.RunLifecyclePatch";
            if (method.DeclaringType?.FullName != owner || method.Name != (preparing ? "Prefix" : "Postfix")) return false;
            if (!RitsuRunNotificationHasNoEffect(method, patched, preparing)) return false;
        }
        return true;
    }

    // Initialize invokes AfterRunCreated on each instance of a registered run
    // singleton. Unrelated singleton types receive no such notification.
    private static bool HasNoRunSingletons(MethodInfo hook, IReadOnlySet<MethodBase> patched)
    {
        const BindingFlags fields = BindingFlags.Static | BindingFlags.NonPublic;
        const string registryName = "STS2RitsuLib.Content.ModContentRegistry";
        const string runtimeName = "STS2RitsuLib.Models.HookedSingletonRuntime";
        if (patched.Contains(hook) || patched.Any(m =>
            m.DeclaringType == typeof(MegaCrit.Sts2.Core.Runs.RunManager) && m.Name == "get_State" ||
            m.Module == hook.Module && (m.DeclaringType?.FullName == "STS2RitsuLib.Models.HookedSingletonCreatePatch" && m.Name == "Postfix" ||
                m.DeclaringType?.FullName == runtimeName && m.Name is "Create" or "Initialize" or "Get" ||
                m.DeclaringType?.FullName == registryName && m.Name == "GetRunModelTypes"))) return false;
        try
        {
            var assembly = hook.Module.Assembly;
            var registry = assembly.GetType(registryName);
            var runModel = assembly.GetType("STS2RitsuLib.Models.HookedSingletonModel");
            if (runModel == null || registry?.GetField("<IsFrozen>k__BackingField", fields)?.GetValue(null) is not true ||
                registry.GetField("SyncRoot", fields)?.GetValue(null) is not System.Threading.Lock sync ||
                registry.GetField("RegisteredSingletons", fields)?.GetValue(null) is not HashSet<Type> singletons) return false;
            lock (sync) return !singletons.Any(runModel.IsAssignableFrom);
        }
        catch (Exception ex)
        {
            RecordCaptureFailure(ex, registryName + ".RegisteredSingletons");
            return false;
        }
    }

    private static bool RitsuRunNotificationHasNoEffect(MethodInfo hook, IReadOnlySet<MethodBase> patched, bool preparing)
    {
        const BindingFlags fields = BindingFlags.Static | BindingFlags.NonPublic;
        var assembly = hook.Module.Assembly;
        const string frameworkName = "STS2RitsuLib.RitsuLibFramework";
        const string payloadName = "STS2RitsuLib.RunData.Patches.RunSavedDataLobbyBeginRunMessageState";
        if (patched.Any(m => m.Module == hook.Module && (m.DeclaringType == hook.DeclaringType ||
            m.DeclaringType?.FullName == frameworkName && m.Name is "PublishLifecycleEvent" or "GetLifecycleTopic" ||
            m.DeclaringType?.DeclaringType?.FullName == frameworkName && m.Name == "ReadSnapshot" ||
            preparing && m.DeclaringType?.FullName == payloadName))) return false;
        try
        {
            if (preparing)
            {
                var state = assembly.GetType(payloadName)!;
                var pending = state.GetField("<PendingNewRunPayload>k__BackingField", fields);
                var prepared = state.GetField("<PreparedNewRunPayload>k__BackingField", fields);
                if (pending?.FieldType != typeof(string) || prepared?.FieldType != typeof(string) ||
                    !string.IsNullOrWhiteSpace((string?)(pending.GetValue(null) ?? prepared.GetValue(null)))) return false;
            }
            var framework = assembly.GetType(frameworkName)!;
            string eventName = preparing ? "STS2RitsuLib.RunData.RunSavedDataPreparingEvent" : "STS2RitsuLib.RunStartedEvent";
            if (framework.GetField("SyncRoot", fields)?.GetValue(null) is not System.Threading.Lock sync)
                return false;
            lock (sync)
            {
                if (framework.GetField("_lifecycleObservers", fields)?.GetValue(null) is not Array { Length: 0 } ||
                    framework.GetField("LifecycleTopics", fields)?.GetValue(null) is not IDictionary topics) return false;
                foreach (DictionaryEntry entry in topics)
                {
                    if (entry.Key is not Type eventType || eventType.FullName != eventName) continue;
                    var topic = entry.Value;
                    if (topic?.GetType().GetField("_handlers", BindingFlags.Instance | BindingFlags.NonPublic)?
                        .GetValue(topic) is not Delegate[] handlers) return false;
                    foreach (var handler in handlers.SelectMany(d => d.GetInvocationList()))
                    {
                        // The library itself sets its update-checker's main-menu flag on RunStarted.
                        var callback = handler.Method;
                        if (preparing || callback.Module != hook.Module || patched.Contains(callback) ||
                            callback.DeclaringType?.FullName != "STS2RitsuLib.Updates.UpdateCheckSessionState+<>c" ||
                            callback.Name != "<Initialize>b__7_1" ||
                            callback.GetParameters() is not [{ ParameterType: var eventArgument }] || eventArgument != eventType)
                            return false;
                    }
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            RecordCaptureFailure(ex, hook.DeclaringType?.FullName ?? hook.Name);
            return false;
        }
    }
}
