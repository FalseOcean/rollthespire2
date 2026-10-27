using System.Diagnostics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Infrastructure.Snapshots;

// A main-thread observation cursor, not a worker task. Continuations are resumed
// inline by Advance; native references stay inside this cursor until observation ends.
internal sealed class RuntimeContextObservation : IDisposable
{
    private readonly NativeObservationSchedule _schedule = new();
    private readonly Task<RuntimeContextObservedInputs> _pending;
    internal RuntimeContextObservation(Func<NativeObservationSchedule, Task<RuntimeContextObservedInputs>> observe)
        => _pending = Observe(observe);
    private async Task<RuntimeContextObservedInputs> Observe(Func<NativeObservationSchedule, Task<RuntimeContextObservedInputs>> observe)
    {
        await _schedule.Start().ConfigureAwait(false);
        return await observe(_schedule).ConfigureAwait(false);
    }
    internal bool IsCompleted => _pending.IsCompleted;
    internal RuntimeContextObservedInputs Result => _pending.IsCompleted
        ? _pending.GetAwaiter().GetResult() : throw new InvalidOperationException("RuntimeObservationIncomplete");
    internal string Stage => _schedule.Stage;
    internal int StageCount => _schedule.StageCount;
    internal double TotalNativeMs => _schedule.TotalNativeMs;
    internal double MaxStepMs => _schedule.MaxStepMs;
    internal bool Advance(TimeSpan budget)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        _schedule.Advance(budget);
        return IsCompleted;
    }
    internal RuntimeContextObservedInputs CompleteOnMainThread()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        while (!IsCompleted) Advance(TimeSpan.MaxValue);
        return Result;
    }
    public void Dispose()
    {
        _schedule.Cancel();
        // A retired cursor has no remaining consumer. Observe a failure even when
        // retirement wins the race with the last native observation.
        if (_pending.IsFaulted) _ = _pending.Exception;
    }
}

internal sealed class NativeObservationSchedule
{
    private TaskCompletionSource<bool>? _resume;
    private long _deadline = long.MaxValue, _stepStarted;
    private bool _cancelled;
    internal string Stage { get; private set; } = "Pending";
    internal int StageCount { get; private set; }
    internal double TotalNativeMs { get; private set; }
    internal double MaxStepMs { get; private set; }
    internal Task Start() => (_resume = new()).Task;
    internal Task Checkpoint(string stage)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (_cancelled) throw new OperationCanceledException("RuntimeObservationRetired");
        long now = Stopwatch.GetTimestamp();
        if (_stepStarted != 0) MaxStepMs = Math.Max(MaxStepMs, Stopwatch.GetElapsedTime(_stepStarted, now).TotalMilliseconds);
        _stepStarted = now; Stage = stage; StageCount++;
        if (now < _deadline) return Task.CompletedTask;
        return (_resume = new()).Task;
    }
    internal void Advance(TimeSpan budget)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (_cancelled) throw new OperationCanceledException("RuntimeObservationRetired");
        if (_resume is null) return;
        long started = Stopwatch.GetTimestamp();
        _stepStarted = started;
        _deadline = budget == TimeSpan.MaxValue ? long.MaxValue :
            started + (long)Math.Max(0, budget.TotalSeconds * Stopwatch.Frequency);
        var resume = _resume; _resume = null;
        // Task's ConfigureAwait(false) continuation can otherwise reject inline
        // execution under Godot's SynchronizationContext and queue to the pool.
        // Only the continuation dispatch has a null context; restore the caller's
        // exact context before returning, including when an observation fails.
        var context = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            resume.SetResult(true);
        }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
        long ended = Stopwatch.GetTimestamp();
        TotalNativeMs += Stopwatch.GetElapsedTime(started, ended).TotalMilliseconds;
        MaxStepMs = Math.Max(MaxStepMs, Stopwatch.GetElapsedTime(_stepStarted, ended).TotalMilliseconds);
    }
    internal void Cancel()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        _cancelled = true;
        var resume = _resume; _resume = null;
        var context = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            resume?.SetCanceled();
        }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }
}

// Only copied observations cross the worker boundary. Equality compares every
// observed dependency, including the native encounter-comparer matrix.
internal sealed record RuntimeContextObservedInputs(
    string RawSeed, CharacterIdentity Character, int Ascension, string GameVersion,
    int PlayersCount, int PlayerSlotIndex, WorldGameMode GameMode,
    PredictionGameModeAuthority GameModeAuthority,
    ReflectionNeowEffectSnapshotAdapter.EffectObservedInputs Effect,
    ReflectionNeowEffectSnapshotAdapter.WorldObservedInputs World,
    bool? VanillaNeowCatalogExact, RequestSupportEvidence SupportEvidence)
{
    internal bool SemanticEquals(RuntimeContextObservedInputs other) =>
        this with { Effect=other.Effect, World=other.World, SupportEvidence=other.SupportEvidence } == other &&
        ReflectionNeowEffectSnapshotAdapter.EffectInputsEqual(Effect,other.Effect) &&
        ReflectionNeowEffectSnapshotAdapter.WorldInputsEqual(World,other.World) &&
        SameSupport(SupportEvidence,other.SupportEvidence);
    private static bool SameSupport(RequestSupportEvidence a, RequestSupportEvidence b) =>
        a with { LoadedOtherMods=b.LoadedOtherMods, Unlock=b.Unlock, Discovery=b.Discovery } == b &&
        a.LoadedOtherMods.SequenceEqual(b.LoadedOtherMods) &&
        a.Unlock with { RevealedEpochs=b.Unlock.RevealedEpochs, SeenEncounters=b.Unlock.SeenEncounters } == b.Unlock &&
        a.Unlock.RevealedEpochs.SequenceEqual(b.Unlock.RevealedEpochs) &&
        a.Unlock.SeenEncounters.SequenceEqual(b.Unlock.SeenEncounters) &&
        a.Discovery with { Acts=b.Discovery.Acts } == b.Discovery && a.Discovery.Acts.SequenceEqual(b.Discovery.Acts);
    internal RuntimeContextAuthoritySnapshot Derive(IRuntimeProfile? profile = null)
    {
        profile ??= Beta111Profile.Instance;
        if (profile.ProfileId != RuntimeProfileId.Beta111)
            throw new InvalidOperationException("RuntimeObservationDeriveProfileMismatch");
        var captured = ReflectionNeowEffectSnapshotAdapter.DeriveEffect(profile, RawSeed, Character,
            Ascension, PlayersCount, PlayerSlotIndex, GameVersion, Effect);
        var world = ReflectionNeowEffectSnapshotAdapter.DeriveWorld(profile, RawSeed, Character,
            Ascension, PlayersCount, PlayerSlotIndex, true, captured.EffectAuthority, GameMode,
            GameModeAuthority, GameVersion, World);
        bool vanilla = Character.IsKnownVanilla;
        bool unlockCaptured = !string.IsNullOrWhiteSpace(captured.UnlockFingerprint);
        bool exact = vanilla && unlockCaptured && captured.CharacterResolution == IdentityResolutionStatus.Exact &&
            SourceAuthorityRules.SupportsExactIdentity(captured.UnlockAuthority);
        var authority = exact ? SourceAuthority.OfficialRuntimeExact : vanilla ? SourceAuthority.Incomplete :
            captured.UnlockAuthority == SourceAuthority.Unknown ? SourceAuthority.ModdedRuntimeBestEffort : captured.UnlockAuthority;
        return new(Character, Ascension, PlayerSlotIndex, PlayersCount, true, VanillaNeowCatalogExact,
            captured.AllCharacterCardPoolsUnlocked, captured.UnlockedCommonCards, captured.UnlockedUncommonCards,
            GameVersion, profile.ProfileId, vanilla, vanilla ? null : "runtime-character", vanilla ? "sts2" : null,
            authority, unlockCaptured ? captured.UnlockCompleteness : SnapshotCompleteness.Missing,
            captured.CharacterResolution, captured.EffectAuthority, world, GameMode, GameModeAuthority,
            supportEvidence: SupportEvidence);
    }
}

public static partial class RuntimeContextAuthorityCapture
{
    internal static RuntimeContextObservation BeginObservation(IRuntimeProfile profile, string rawSeed,
        CharacterIdentity character, int ascension, string gameVersion, int playersCount = 1, int playerSlotIndex = 0,
        WorldGameMode predictionGameMode = WorldGameMode.Unknown,
        PredictionGameModeAuthority predictionGameModeAuthority = PredictionGameModeAuthority.Unknown,
        object? explicitUnlockState = null, IReadOnlyList<ModelKey>? orderedCharacters = null,
        IReadOnlyList<ModelKey>? explicitDiscoveredActs = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (profile.ProfileId != RuntimeProfileId.Beta111)
            throw new NotSupportedException("RuntimeObservationPreparationRequiresBeta111");
        // Freeze account provenance through the existing ingress. Passing these
        // facts to adapters does not relabel them as an explicit user premise.
        var roster = orderedCharacters?.ToArray();
        var discoveredActs = explicitDiscoveredActs?.ToArray();
        return new(async schedule =>
        {
            await schedule.Checkpoint("RequestAccountInputs").ConfigureAwait(false);
            var request = RequestSupportEvidenceCapture.FreezeInputs(explicitUnlockState, discoveredActs);
            var support = RequestSupportEvidenceCapture.Capture(request);
            var effect = await ReflectionNeowEffectSnapshotAdapter.ObserveEffect(profile, rawSeed, character,
                ascension, playersCount, playerSlotIndex, gameVersion, request.UnlockState, schedule,
                failSoft: explicitUnlockState is null).ConfigureAwait(false);
            var world = await ReflectionNeowEffectSnapshotAdapter.ObserveWorld(profile, character, playersCount,
                playerSlotIndex, predictionGameMode, predictionGameModeAuthority, request.UnlockState,
                roster, request.DiscoveredActs, schedule).ConfigureAwait(false);
            await schedule.Checkpoint("NeowCatalog").ConfigureAwait(false);
            bool? neow = ReflectionNeowEffectSnapshotAdapter.CaptureVanillaNeowCatalogExact(out string evidence);
            if (neow != true && Interlocked.Exchange(ref _neowCatalogWarning, 1) == 0)
                Bootstrap.RuntimeLog.TryBackgroundWarning($"runtimeNeowCatalogAuthority={neow?.ToString() ?? "Unknown"};evidence={evidence};scope=Neow;noInventedCatalog=true");
            return new(rawSeed, character, ascension, gameVersion, playersCount, playerSlotIndex,
                predictionGameMode, predictionGameModeAuthority, effect, world, neow, support);
        });
    }
}
