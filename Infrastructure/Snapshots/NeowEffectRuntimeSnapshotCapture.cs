using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class NeowEffectRuntimeSnapshotCapture
{
    private static readonly IReadOnlyDictionary<RuntimeProfileId, INeowEffectSnapshotAdapter> Adapters =
        new Dictionary<RuntimeProfileId, INeowEffectSnapshotAdapter>
        {
            [RuntimeProfileId.Beta110] = new Beta110NeowEffectSnapshotAdapter(),
            [RuntimeProfileId.Beta111] = new Beta111NeowEffectSnapshotAdapter()
        };

    public static RuntimeEffectSnapshotCaptureResult Capture(
        IRuntimeProfile profile,
        string rawSeed,
        CharacterIdentity character,
        int ascension,
        string gameVersion,
        int playersCount,
        int playerSlotIndex)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RuntimeSnapshotThreadGuard.RequireMainThread();

        if (!Adapters.TryGetValue(profile.ProfileId, out INeowEffectSnapshotAdapter? adapter) ||
            adapter.ProfileId != profile.ProfileId)
        {
            return RuntimeEffectSnapshotCaptureResult.Missing(
                profile.ProfileId,
                "NoProfileSpecificEffectSnapshotAdapter",
                PredictionWarningCode.EffectSnapshotIncomplete);
        }

        try
        {
            return adapter.Capture(
                profile,
                rawSeed,
                character,
                ascension,
                gameVersion,
                playersCount,
                playerSlotIndex);
        }
        catch (Exception ex)
        {
            return RuntimeEffectSnapshotCaptureResult.Missing(
                profile.ProfileId,
                $"RuntimeEffectSnapshotCaptureFailed:{ex.GetType().Name}",
                PredictionWarningCode.EffectSnapshotIncomplete);
        }
    }
}
