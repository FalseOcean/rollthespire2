using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Shell;

// This menu owns candidates only. Every native dependency is observed again in
// the entry turn before a cross-frame candidate can be used. No candidate is
// published to formal Search or to the shared environment during idle work.
internal sealed class MenuWorkspacePreparation : IDisposable
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly SearchWorkspace _workspace;
    private readonly int _account;
    private readonly string _seed;
    private Task<SearchWorkspace.PreparedWorkbenchRead>? _draft;
    private RuntimeAuthorityObservationCursor? _global;
    private Task<RuntimeAuthoritySnapshot>? _globalDerived;
    private ContextCandidate? _environment, _selected;
    private EditorEnvironmentObservation? _editor;
    private Task<SearchEnvironmentSignature>? _signature;
    private Task<string>? _freshness;
    private string _diagnostics = "";
    private bool _disposed;
    private int _next;
    private bool _globalStarted, _editorStarted;
    private long _derivedTicks;
    internal int NativeSlices { get; private set; }
    internal double NativeMilliseconds { get; private set; }
    internal double MaxSliceMilliseconds { get; private set; }
    internal double ValidationMilliseconds { get; private set; }
    internal int ReusedContexts { get; private set; }
    internal bool IsReady => !_disposed && _signature?.IsCompletedSuccessfully == true &&
        _selected?.Derived?.IsCompletedSuccessfully == true && _freshness?.IsCompletedSuccessfully == true;
    internal object Status => new { IsReady, NativeSlices, NativeMilliseconds, MaxSliceMilliseconds,
        DerivedMilliseconds = (double)Interlocked.Read(ref _derivedTicks) / Stopwatch.Frequency * 1000,
        ValidationMilliseconds, ReusedContexts, Account = _account, Retired = _disposed };

    internal MenuWorkspacePreparation(ModRuntimeSnapshot runtime, SearchWorkspace workspace)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        _runtime = runtime; _workspace = workspace;
        _account = SaveManager.Instance.CurrentProfileId;
        _seed = ProfileSeedGenerator.CreateProbeSeed(runtime.Profile);
    }

    private Task<T> Compute<T>(Func<T> action) => Task.Run(() =>
    {
        long started = Stopwatch.GetTimestamp();
        try { return action(); }
        finally { Interlocked.Add(ref _derivedTicks, Stopwatch.GetTimestamp() - started); }
    });

    private void Start()
    {
        if (_draft is not null) return;
        _draft = Compute(_workspace.PrepareWorkbenchRead);
        _global = RuntimeAuthoritySnapshotCapture.BeginObservation(_runtime.Detection.DisplayVersion);
        _environment = CreateContext(BaseGameModelKeys.Characters.Silent, SeedPredictionInputLimits.MaximumAscension);
        _editor = new EditorEnvironmentObservation();
        _diagnostics = SearchEnvironmentSignatureBuilder.CaptureModuleDiagnostics();
    }

    private ContextCandidate CreateContext(ModelKey character, int ascension) => new(character, ascension,
        RuntimeContextAuthorityCapture.BeginObservation(_runtime.Profile, _seed, CharacterIdentity.FromKey(character),
            ascension, _runtime.Detection.DisplayVersion, predictionGameMode: WorldGameMode.Singleplayer,
            predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest));

    private void SelectContext(SearchWorkspace.PreparedWorkbenchRead receipt)
    {
        // A party draft is still restored through the existing ordered-party
        // authority. This candidate is only the initial/default solo editor.
        var draft = receipt.Draft;
        ModelKey character = draft is { Players.Count: 0 } ? draft.Character : BaseGameModelKeys.Characters.Ironclad;
        int ascension = draft is { Players.Count: 0 } ? draft.Ascension : 10;
        if (_selected?.Character == character && _selected.Ascension == ascension) return;
        _selected?.Dispose();
        _selected = CreateContext(character, ascension);
    }

    internal void Advance(TimeSpan budget)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (_disposed || SaveManager.Instance.CurrentProfileId != _account) return;
        long started = Stopwatch.GetTimestamp();
        Start();
        if (_draft!.IsCompleted) SelectContext(_draft.GetAwaiter().GetResult());
        if (IsReady) return;
        TimeSpan remaining = budget - Stopwatch.GetElapsedTime(started);
        // One owner and one soft slice per frame, including the all-deck read.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int selected = _next++ % 4;
            if (remaining <= TimeSpan.Zero) break;
            if (selected == 0 && !_global!.IsCompleted) { _globalStarted = true; _global.Advance(remaining); break; }
            if (selected == 1 && !_environment!.Observation.IsCompleted) { _environment.Advance(remaining); break; }
            if (selected == 2 && _selected is { Observation.IsCompleted: false }) { _selected.Advance(remaining); break; }
            if (selected == 3 && !_editor!.IsCompleted) { _editorStarted = true; _editor.Advance(remaining); break; }
        }
        ScheduleDerivations();
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        NativeSlices++; NativeMilliseconds += elapsed; MaxSliceMilliseconds = Math.Max(MaxSliceMilliseconds, elapsed);
    }

    private void ScheduleDerivations()
    {
        foreach (Task? pending in new Task?[] { _draft, _globalDerived, _environment?.Derived, _selected?.Derived, _signature, _freshness })
            if (pending?.IsFaulted == true) pending.GetAwaiter().GetResult();
        if (_global!.IsCompleted && _globalDerived is null)
        { var input = _global.Result; _globalDerived = Compute(input.Derive); }
        Schedule(_environment!);
        if (_selected is not null) Schedule(_selected);
        if (_signature is null && _globalDerived?.IsCompletedSuccessfully == true &&
            _environment!.Derived?.IsCompletedSuccessfully == true && _editor!.IsCompleted)
        {
            var authority = _environment.Derived.Result;
            var global = _globalDerived.Result;
            var editor = _editor.Result;
            string diagnostics = _diagnostics;
            _signature = Compute(() => SearchEnvironmentSignatureBuilder.FromObservedInputs(authority, global,
                editor.Unlocks, editor.StartingDecks, diagnostics));
            _freshness = Compute(() => editor.Freshness(_runtime, global));
        }
    }

    private void Schedule(ContextCandidate candidate)
    {
        if (candidate.Observation.IsCompleted && candidate.Derived is null)
        { var input = candidate.Observation.Result; candidate.Derived = Compute(() => input.Derive(_runtime.Profile)); }
    }

    internal MenuWorkspaceEntryInputs CompleteForEntry()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (_disposed || SaveManager.Instance.CurrentProfileId != _account)
            throw new InvalidOperationException("MenuPreparationAccountRetired");
        Start();
        long started = Stopwatch.GetTimestamp();
        // A cursor first touched in this entry turn already is the fresh read.
        // Cross-frame reads are candidates; matching their complete actual raw
        // observations is required, including the native SharesTagsWith matrix.
        bool globalWasStarted = _global!.IsCompleted || _globalStarted;
        if (globalWasStarted && !_global.IsCompleted)
        {
            _global.Dispose();
            _global = RuntimeAuthoritySnapshotCapture.BeginObservation(_runtime.Detection.DisplayVersion);
            globalWasStarted = false; _globalStarted = false;
        }
        var globalInput = _global.CompleteOnMainThread();
        var freshGlobal = globalInput;
        if (globalWasStarted)
        { using var fresh = RuntimeAuthoritySnapshotCapture.BeginObservation(_runtime.Detection.DisplayVersion);
            freshGlobal = fresh.CompleteOnMainThread(); }
        bool reuseGlobal = globalInput.SemanticEquals(freshGlobal);
        RuntimeAuthoritySnapshot global = reuseGlobal
            ? (_globalDerived ??= Compute(globalInput.Derive)).GetAwaiter().GetResult() with { CapturedAtUtc = freshGlobal.CapturedAtUtc }
            : Compute(freshGlobal.Derive).GetAwaiter().GetResult();
        var draft = _workspace.RefreshPreparedWorkbenchRead(_draft!.GetAwaiter().GetResult());
        SelectContext(draft);
        var (environment, reuseEnvironment) = Resolve(_environment!);
        var (solo, _) = Resolve(_selected!);
        bool editorWasStarted = _editor!.IsCompleted || _editorStarted;
        if (editorWasStarted && !_editor.IsCompleted)
        {
            _editor.Dispose(); _editor = new EditorEnvironmentObservation();
            editorWasStarted = false; _editorStarted = false;
        }
        var editorCandidate = _editor.CompleteOnMainThread();
        var editor = editorWasStarted ? EditorEnvironmentObservation.Capture() : editorCandidate;
        bool reuseEditor = editorCandidate.SemanticEquals(editor);
        ValidationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        RuntimeAuthorityEnvironment.PublishVerifiedObservation(_runtime, global, captureMilliseconds: ValidationMilliseconds);
        string diagnostics = SearchEnvironmentSignatureBuilder.CaptureModuleDiagnostics();
        SearchEnvironmentSignature signature = reuseGlobal && reuseEnvironment && reuseEditor
            ? (_signature ??= Compute(() => SearchEnvironmentSignatureBuilder.FromObservedInputs(environment, global,
                editor.Unlocks, editor.StartingDecks, diagnostics))).GetAwaiter().GetResult()
            : Compute(() => SearchEnvironmentSignatureBuilder.FromObservedInputs(environment, global,
                editor.Unlocks, editor.StartingDecks, diagnostics)).GetAwaiter().GetResult();
        signature.RuntimeModuleDiagnostics = diagnostics;
        string freshness = reuseGlobal && reuseEditor ? (_freshness ??= Compute(() => editor.Freshness(_runtime, global))).GetAwaiter().GetResult()
            : Compute(() => editor.Freshness(_runtime, global)).GetAwaiter().GetResult();
        _workspace.AdoptPreparedWorkbenchRead(draft);
        return new(signature, editor, freshness, solo, _selected!.Character, _selected.Ascension);
    }

    private (RuntimeContextAuthoritySnapshot, bool) Resolve(ContextCandidate candidate)
    {
        bool wasStarted = candidate.Started || candidate.Observation.IsCompleted;
        if (wasStarted && !candidate.Observation.IsCompleted)
        {
            candidate.Replace(CreateContext(candidate.Character, candidate.Ascension).Observation);
            wasStarted = false;
        }
        var input = candidate.Observation.CompleteOnMainThread();
        var freshInput = input;
        if (wasStarted)
        { using var fresh = CreateContext(candidate.Character, candidate.Ascension);
            freshInput = fresh.Observation.CompleteOnMainThread(); }
        bool reuse = input.SemanticEquals(freshInput);
        if (reuse && candidate.Derived is not null) ReusedContexts++;
        return (reuse ? (candidate.Derived ??= Compute(() => input.Derive(_runtime.Profile))).GetAwaiter().GetResult()
            : Compute(() => freshInput.Derive(_runtime.Profile)).GetAwaiter().GetResult(), reuse);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _global?.Dispose(); _environment?.Dispose(); _selected?.Dispose(); _editor?.Dispose();
        ObserveFailure(_draft); ObserveFailure(_globalDerived); ObserveFailure(_signature); ObserveFailure(_freshness);
    }
    private static void ObserveFailure(Task? task)
    { if (task is not null) _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); }

    private sealed class ContextCandidate(ModelKey character, int ascension, RuntimeContextObservation observation) : IDisposable
    {
        internal ModelKey Character { get; } = character;
        internal int Ascension { get; } = ascension;
        internal RuntimeContextObservation Observation { get; private set; } = observation;
        internal Task<RuntimeContextAuthoritySnapshot>? Derived;
        internal bool Started { get; private set; }
        internal void Replace(RuntimeContextObservation fresh)
        { Observation.Dispose(); Observation = fresh; Started = false; Derived = null; }
        internal void Advance(TimeSpan budget) { Started = true; Observation.Advance(budget); }
        public void Dispose() { Observation.Dispose(); ObserveFailure(Derived); }
    }
}

internal sealed record MenuWorkspaceEntryInputs(SearchEnvironmentSignature Environment,
    EditorEnvironmentObservedInputs Editor, string Freshness, RuntimeContextAuthoritySnapshot Solo,
    ModelKey Character, int Ascension);
