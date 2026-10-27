using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

public static partial class RuntimeContextAuthorityCapture
{
    /// <summary>Attach only when a request needs global card existence, not reward eligibility.</summary>
    public static RuntimeContextAuthoritySnapshot AttachCardIdentityAuthority(RuntimeContextAuthoritySnapshot authority)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ArgumentNullException.ThrowIfNull(authority);
        try
        {
            // AllCards is the same complete registration source used by the
            // environment semantic catalogue. Unlocks and pool membership do not filter it.
            var keys = ModelDb.AllCards.Select(card => new ModelKey(card.Id.Category, card.Id.Entry)).ToArray();
            if (keys.Length == 0) throw new InvalidOperationException("CardIdentityCatalogEmpty");
            return authority.WithAvailableCardIdentities(keys);
        }
        catch (Exception ex)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(ex, "CardIdentityAuthorityCaptureFailed",
                nameof(RuntimeContextAuthorityCapture), "Search.CardIdentityCapture", "issues.capture_failed");
            return authority.WithAvailableCardIdentities(null);
        }
    }
}
