using System.Text.Json;
using System.Text.Json.Nodes;

namespace RolltheSpire2.Core.Authority.Runtime;

// Stable copied execution inputs for resume/manual-attempt identity. This is
// deliberately stricter than the user-facing game/content/unlock environment:
// eligibility, unknown facts and authority completeness still affect execution.
internal static class RuntimePredictionInputIdentity
{
    private static readonly HashSet<string> DerivedOrDiagnostic = new(StringComparer.Ordinal)
    {
        "CapturedAtUtc", "CaptureDiagnosticCode", "WarningCodes", "SupportScope",
        "UnlockSnapshotFingerprint", "EffectSnapshotFingerprint", "WorldSnapshotFingerprint",
        "CatalogFingerprint", "SnapshotFingerprint", "UnlockFingerprint", "DeckFingerprint",
        "RelicBagFingerprint", "PotionPoolFingerprint", "ContextFingerprint",
        "CaptureEvidence", "EvidenceCode", "AuthorityEvidenceCode", "EventIsSharedAuthorityEvidence",
        "EncounterAuthorityEvidenceCode", "VerifiedRealGameFixtureIds", "ModInspectionEvidence",
        "LoadedModsInspected", "LoadedOtherMods"
    };

    internal static JsonNode? AuthorityPayload(RuntimeContextAuthoritySnapshot authority) =>
        Canonical(JsonSerializer.SerializeToNode(authority));

    private static JsonNode? Canonical(JsonNode? value)
    {
        if (value is JsonObject obj)
        {
            var copy = new JsonObject();
            foreach (var item in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (DerivedOrDiagnostic.Contains(item.Key)) continue;
                // Current tutorial/encounter rules distinguish first run from later
                // runs only. Keep missing facts unknown and leave source DTOs intact.
                if (item.Key is "NumberOfRuns" or "EncounterNumberOfRuns" or "number_of_runs" or "encounter_number_of_runs" &&
                    item.Value is JsonValue count && count.TryGetValue<int>(out int runs))
                    copy[item.Key] = JsonValue.Create(runs == 0 ? 0 : 1);
                else copy[item.Key] = Canonical(item.Value);
            }
            return copy;
        }
        if (value is JsonArray array)
        {
            var copy = new JsonArray();
            foreach (var item in array) copy.Add(Canonical(item));
            return copy;
        }
        return value?.DeepClone();
    }
}
