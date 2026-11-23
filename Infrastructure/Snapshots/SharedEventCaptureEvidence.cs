using System.Reflection;
using HarmonyLib;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Infrastructure.Snapshots;

// Capture evidence from the world reader's actual enumeration, without re-reading a mod's source.
internal sealed class SharedEventCaptureEvidence(Assembly gameAssembly)
{
    private const int AnomalyLimit = 24;
    private const int PatchLimit = 32;
    private const int RecordLimit = 16;
    private static readonly HashSet<string> Written = new(StringComparer.Ordinal);
    private readonly List<CatalogSource> _sources = [];
    private readonly List<CatalogEntry> _anomalies = [];
    private int _count, _invalid, _external;
    private string? _enumerationFailure;

    private sealed record CatalogSource(string Member, string Status, string? Type, string? Assembly);
    private sealed record CatalogEntry(int Position, string? Key, string Status, string? Type, string? Assembly, Guid? ModuleId);
    private sealed record PatchEntry(string Target, string Kind, string Owner, string Method, string Assembly,
        Guid ModuleId, int Index, int Priority, string[] Before, string[] After);

    internal void ObserveSource(string member, object? value)
    {
        var type = value?.GetType();
        _sources.Add(new(Limit(member)!, value switch
        {
            null => "Unavailable",
            System.Collections.IEnumerable when value is not string => "Enumerable",
            _ => "NotEnumerable"
        }, Limit(type?.FullName), Limit(type?.Assembly.GetName().Name)));
    }

    internal void ObserveEntry(object? model, string? key)
    {
        int position = _count++;
        var type = model?.GetType();
        bool external = type is not null && type.Assembly != gameAssembly;
        if (key is null) _invalid++;
        if (external) _external++;
        if ((key is not null && !external) || _anomalies.Count == AnomalyLimit) return;
        var entry = new CatalogEntry(position, Limit(key), model is null ? "NullEntry" :
            key is null ? "InvalidKey" : "AcceptedExternal",
            Limit(type?.FullName), Limit(type?.Assembly.GetName().Name), type?.Module.ModuleVersionId);
        _anomalies.Add(entry);
    }

    internal void EnumerationFailed(Exception error) => _enumerationFailure = Limit(error.GetType().FullName);

    internal void Write(string profile, string character, Type modelDb, bool sharedOrderExact, bool eventCatalogExact,
        IReadOnlyList<string> issues, IReadOnlySet<MethodBase> patchedMethods)
    {
        try
        {
            var patchEntries = new List<PatchEntry>();
            int patchCount = 0;
            string? patchEvidenceFailure = null;
            try
            {
                foreach (var target in patchedMethods.Where(method => method.DeclaringType?.FullName switch
                         {
                             "MegaCrit.Sts2.Core.Models.ActModel" => method.Name is "GetRandomList" or "GenerateRooms",
                             "MegaCrit.Sts2.Core.Runs.RunManager" => method.Name is "InitializeNewRun" or "GenerateRooms",
                             "MegaCrit.Sts2.Core.Runs.RelicGrabBag" => method.Name is "Shuffle" or "Populate",
                             "MegaCrit.Sts2.Core.Models.AncientEventModel" => method.Name == "GenerateInitialOptionsWrapper",
                             _ => method.DeclaringType == modelDb && method.Name is "get_AllSharedEvents" or "get_SharedEvents" ||
                                 method.Name == "GenerateInitialOptions" && method.DeclaringType is { } type &&
                                 typeof(MegaCrit.Sts2.Core.Models.AncientEventModel).IsAssignableFrom(type)
                         })
                         .OrderBy(MethodName, StringComparer.Ordinal))
                {
                    var patches = Harmony.GetPatchInfo(target);
                    if (patches is null) continue;
                    Add("Prefix", patches.Prefixes);
                    Add("Postfix", patches.Postfixes);
                    Add("Transpiler", patches.Transpilers);
                    Add("Finalizer", patches.Finalizers);
                    Add("InnerPrefix", patches.InnerPrefixes);
                    Add("InnerPostfix", patches.InnerPostfixes);

                    void Add(string kind, IEnumerable<Patch> group)
                    {
                        foreach (var patch in group.OrderBy(p => p.index))
                        {
                            patchCount++;
                            if (patchEntries.Count == PatchLimit) continue;
                            MethodInfo method = patch.PatchMethod;
                            patchEntries.Add(new(MethodName(target), kind, Limit(patch.owner) ?? "",
                                MethodName(method), Limit(method.Module.Assembly.GetName().Name) ?? "",
                                method.Module.ModuleVersionId, patch.index, patch.priority,
                                patch.before.Take(8).Select(id => Limit(id) ?? "").ToArray(),
                                patch.after.Take(8).Select(id => Limit(id) ?? "").ToArray()));
                        }
                    }
                }
            }
            catch (Exception error) { patchEvidenceFailure = Limit(error.GetType().FullName); }

            if (sharedOrderExact && eventCatalogExact && _external == 0 && patchCount == 0 &&
                patchEvidenceFailure is null) return;

            string json = RuntimeLog.SafeJson(new
            {
                Profile = profile,
                Character = character,
                SharedOrderExact = sharedOrderExact,
                EventCatalogExact = eventCatalogExact,
                Issues = issues.Take(24).Select(Limit).ToArray(),
                IssueCount = issues.Count,
                Sources = _sources,
                ObservedCount = _count,
                AcceptedCount = _count - _invalid,
                InvalidCount = _invalid,
                ExternalCount = _external,
                Anomalies = _anomalies,
                EnumerationFailure = _enumerationFailure,
                PatchEvidenceFailure = patchEvidenceFailure,
                PatchCount = patchCount,
                Patches = patchEntries
            });
            lock (Written)
            {
                if (Written.Count == RecordLimit || Written.Contains(json)) return;
                if (RuntimeLog.TryBackgroundWarning("worldAuthorityCapture=true;captureJson=" + json)) Written.Add(json);
            }
        }
        catch (Exception error)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(error, "WorldAuthorityEvidenceFailed",
                nameof(SharedEventCaptureEvidence), "WorldCatalog", "issues.capture_failed");
        }
    }

    private static string MethodName(MethodBase method) => Limit(method.DeclaringType?.FullName + "." + method.Name) ?? "";
    private static string? Limit(string? value) => value is { Length: > 180 } ? value[..180] : value;
}
