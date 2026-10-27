using MegaCrit.Sts2.Core.Modding;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Ui.InRunOperations;

// Known means RF is loaded. The Owner's coexistence policy treats its base
// predictions as enabled without inspecting RF configuration or implementation.
internal enum RandomForeseerStatus { NotLoaded, Known, Unknown }

internal sealed record RandomForeseerSnapshot(
    RandomForeseerStatus Status, string Version, string Reason)
{
    internal string Signature => $"{Status}:{Version}:{Reason}";
    internal bool IsLoaded => Status == RandomForeseerStatus.Known;
}

// Reads only the game's loaded-mod registry. RT2 owns supplemental output and
// keeps its own display whenever RF loading cannot be confirmed.
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
                return new(RandomForeseerStatus.Known, mod.manifest.version ?? "", "Loaded");
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
