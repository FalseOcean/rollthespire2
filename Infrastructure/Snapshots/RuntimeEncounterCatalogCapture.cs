using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>
/// Content identity only: this catalog is independent of unlocks, encounter pools
/// for a particular run, and the schema-1 semantic fingerprint's boss domain.
/// </summary>
internal sealed record RuntimeEncounterCatalogSnapshot(
    IReadOnlyList<ModelKey> EncounterKeys,
    bool IdentityCaptureComplete,
    string EvidenceCode);

internal static class RuntimeEncounterCatalogCapture
{
    public static RuntimeEncounterCatalogSnapshot Capture()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var keys = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        bool complete = true;
        try
        {
            // ModelDb.AllEncounters includes every act's weak, strong, elite and
            // boss encounters plus event encounters, without current-unlock filtering.
            // Read stable IDs only; no RunState, RNG or mutable model escapes.
            foreach (var encounter in ModelDb.AllEncounters)
            {
                if (encounter is null)
                {
                    complete = false;
                    continue;
                }
                var key = new ModelKey(encounter.Id.Category, encounter.Id.Entry);
                if (!key.IsValid || key.Category != BaseGameModelKeys.Categories.Encounter)
                {
                    complete = false;
                    continue;
                }
                keys.Add(key);
            }
            return new(keys.ToImmutableArray(), complete && keys.Count > 0,
                "ModelDb.AllEncounters:stable-identities");
        }
        catch (Exception ex)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(ex, "RuntimeEncounterCatalogCaptureFailed",
                nameof(RuntimeEncounterCatalogCapture), "EncounterCatalogCapture", "issues.capture_failed");
            return new(keys.ToImmutableArray(), false, "RuntimeEncounterCatalogCaptureFailed:" + ex.GetType().Name);
        }
    }
}
