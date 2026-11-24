using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Local-player Crystal assumptions, not a multiplayer route runtime. Native
// eligibility is captured as values; unknown custom eligibility/hooks are logged
// and left unmodeled under the explicitly accepted base-properties policy.
internal sealed record PredictorCrystalContext(int PlayerCount, int PlayerSlot, ulong PlayerNetId,
    ImmutableHashSet<ModelKey> EligibleRelics)
{
    public ImmutableArray<ModelKey> UnmodeledSources { get; init; } = [];
}
