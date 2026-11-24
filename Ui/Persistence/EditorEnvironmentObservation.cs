using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;

namespace RolltheSpire2.Ui.Persistence;

// Owned value snapshots only: no SaveManager, native model or live enumerator escapes.
internal sealed record EditorEnvironmentObservedInputs(int Account, SerializableUnlockState Unlocks,
    IReadOnlyList<CharacterStartingDeckSnapshot>? StartingDecks, IReadOnlyList<string> DiscoveredActs)
{
    internal bool SemanticEquals(EditorEnvironmentObservedInputs other) =>
        StartingDecks is not null && other.StartingDecks is not null &&
        Account == other.Account && Unlocks.NumberOfRuns == other.Unlocks.NumberOfRuns &&
        Unlocks.UnlockedEpochs.SequenceEqual(other.Unlocks.UnlockedEpochs) &&
        Unlocks.EncountersSeen.SequenceEqual(other.Unlocks.EncountersSeen) &&
        DiscoveredActs.SequenceEqual(other.DiscoveredActs) && StartingDecks.Count == other.StartingDecks.Count &&
        StartingDecks.Zip(other.StartingDecks).All(pair => pair.First.CharacterKey == pair.Second.CharacterKey &&
            pair.First.OrderedDeck.SequenceEqual(pair.Second.OrderedDeck));

    // May run on a worker once the supplied global authority has also been frozen.
    internal string Freshness(ModRuntimeSnapshot runtime, RuntimeAuthoritySnapshot global)
    {
        string serialized = JsonSerializer.Serialize(new {
            runtime.Profile.ProfileId, runtime.Detection.NormalizedVersion, Account,
            global.Fingerprint, global.CurrentUnlockState, global.CaptureIssues,
            Unlocks, StartingDecks, DiscoveredActs });
        if (StartingDecks is null || !global.Fingerprint.Complete)
            serialized += ":unavailable:" + Guid.NewGuid().ToString("N");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
    }
}

internal sealed class EditorEnvironmentObservation : IDisposable
{
    private RuntimeAuthoritySnapshotCapture.CharacterStartingDeckObservation? _decks;
    private int _stage, _account;
    private SerializableUnlockState? _unlocks;
    private string[] _acts = [];
    private EditorEnvironmentObservedInputs? _result;
    private bool _disposed;
    internal bool IsCompleted => _result is not null;
    internal EditorEnvironmentObservedInputs Result => _result ?? throw new InvalidOperationException("EditorEnvironmentObservationIncomplete");

    internal void Advance(TimeSpan budget)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (_disposed) throw new ObjectDisposedException(nameof(EditorEnvironmentObservation));
        long started = Stopwatch.GetTimestamp();
        while (!IsCompleted && Stopwatch.GetElapsedTime(started) < budget) Step();
    }

    private void Step()
    {
        switch (_stage)
        {
            case 0:
                _account = SaveManager.Instance.CurrentProfileId;
                // ToSerializable can retain collections supplied by a native getter.
                // Deep copy before any later slice or worker can observe the values.
                var unlocks = SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable();
                _unlocks = JsonSerializer.Deserialize<SerializableUnlockState>(JsonSerializer.SerializeToUtf8Bytes(unlocks))
                    ?? throw new InvalidDataException("EditorUnlockObservationNull");
                _stage++;
                break;
            case 1:
                _acts = SaveManager.Instance.Progress.DiscoveredActs.Select(key => key.ToString()).ToArray();
                _decks = new RuntimeAuthoritySnapshotCapture.CharacterStartingDeckObservation();
                _stage++;
                break;
            default:
                if (!_decks!.MoveNext())
                {
                    _result = new(_account, _unlocks!, _decks.Result, _acts);
                    _decks.Dispose(); _decks = null;
                }
                break;
        }
    }

    internal EditorEnvironmentObservedInputs CompleteOnMainThread()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (_disposed) throw new ObjectDisposedException(nameof(EditorEnvironmentObservation));
        while (!IsCompleted) Step();
        return Result;
    }

    internal static EditorEnvironmentObservedInputs Capture()
    {
        using var observation = new EditorEnvironmentObservation();
        return observation.CompleteOnMainThread();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _decks?.Dispose(); _decks = null;
    }
}
