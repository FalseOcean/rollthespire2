using MegaCrit.Sts2.Core.Modding;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Ui.InRunOperations;

// Loading and effective feature coverage are separate observations.
internal enum RandomForeseerStatus { NotLoaded, Known, Unknown }

internal sealed record RandomForeseerSnapshot(
    RandomForeseerStatus Status, string Version, string Reason)
{
    internal RfFeatures Features { get; init; }
    internal string Signature => $"{Status}:{Version}:{Reason}:{Features.Known}:{Features.Enabled}:{Features.Net}";
    internal bool IsLoaded => Status == RandomForeseerStatus.Known;
}

// An installed but inactive package supplies no coverage.
internal static class RandomForeseerCompatibility
{
    private static string? _lastFailure;

    internal static RandomForeseerStatus CaptureStatus() => Capture().Status;

    internal static RandomForeseerSnapshot Capture()
    {
        try
        {
            foreach (var mod in ModManager.Mods)
            {
                if (mod.state != ModLoadState.Loaded || mod.manifest?.id != "RandomForeseer") continue;
                _lastFailure = null;
                return new(RandomForeseerStatus.Known, mod.manifest.version ?? "", "Loaded") { Features = RandomForeseerSettings.Read() };
            }
            _lastFailure = null;
            return new(RandomForeseerStatus.NotLoaded, "", "NotLoaded");
        }
        catch (Exception exception)
        {
            string failure = exception.ToString();
            if (_lastFailure != failure)
            {
                _lastFailure = failure;
                RuntimeLog.WarnException("RandomForeseerLoadDetectionFailed;keepRt2=true", exception);
            }
            return new(RandomForeseerStatus.Unknown, "", "LoadDetectionFailed");
        }
    }
}
