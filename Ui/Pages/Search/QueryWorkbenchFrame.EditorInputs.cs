using System.Text.Json;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private EditorInputs _editorInputs = new();
    private string? _boundEditorFreshness;

    // Main-thread transaction-local input sharing. There is only one retained solo
    // request and one table request, discarded when observed content/progress changes.
    private sealed class EditorInputs
    {
        private int _depth;
        private string _freshness = "", _soloKey = "", _partyKey = "";
        private EditorRefreshContext? _solo;
        private OrderedPartyAuthority? _party;
        private SerializableUnlockState _unlocks = null!;
        private PreparedInputs? _prepared;
        private sealed record PreparedInputs(EditorEnvironmentObservedInputs Input, string Freshness,
            RuntimeContextAuthoritySnapshot? Solo, ModelKey Character, int Ascension);
        internal string Freshness => _freshness;
        internal int AuthorityCaptures { get; private set; }
        internal int Observations { get; private set; }

        internal void AdoptPreparedInputs(EditorEnvironmentObservedInputs input, string freshness,
            RuntimeContextAuthoritySnapshot? solo, ModelKey character, int ascension)
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            if (_depth != 0) throw new InvalidOperationException("EditorPreparedInputsInsideObservation");
            _prepared = new(input, freshness, solo, character, ascension);
        }

        internal IDisposable Observe(ModRuntimeSnapshot runtime)
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            if (_depth == 0)
            {
                // No runtime catalog mutation revision exists. The entry boundary
                // refreshes global content; progress/account and every starting deck
                // are observed here, never guessed from the selected character alone.
                // The entry owner may pass a fully reobserved same-turn receipt once.
                // Nested restore/bindings share it; every later outer transaction
                // returns to fresh native reads through the same capture consumer.
                var prepared = _prepared; _prepared = null;
                var input = prepared?.Input ?? EditorEnvironmentObservation.Capture();
                _unlocks = input.Unlocks;
                string freshness = prepared?.Freshness ?? input.Freshness(runtime, RuntimeAuthorityEnvironment.Current.Authority);
                if (_freshness != freshness)
                {
                    _freshness = freshness; _solo = null; _party = null;
                    _soloKey = _partyKey = "";
                }
                if (prepared?.Solo is { } solo)
                {
                    string key = $"{_freshness}/{prepared.Character}/{prepared.Ascension}/1/0";
                    // Authority keeps CaptureRuntimeReadOnly's CurrentAccount evidence;
                    // this handoff changes ownership, not support provenance.
                    _solo = new(key, _unlocks, solo); _soloKey = key; AuthorityCaptures++;
                }
                Observations++;
            }
            _depth++;
            return new Observation(this);
        }

        internal EditorRefreshContext Solo(ModRuntimeSnapshot runtime, ModelKey character, int ascension)
        {
            string key = $"{_freshness}/{character}/{ascension}/1/0";
            if (_solo is null || key != _soloKey)
            {
                var authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(runtime.Profile,
                    ProfileSeedGenerator.CreateProbeSeed(runtime.Profile), CharacterIdentity.FromKey(character),
                    ascension, runtime.Detection.DisplayVersion, predictionGameMode: WorldGameMode.Singleplayer,
                    predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
                // Keep production's CurrentAccount support-evidence provenance.
                // Its own unlock read is required by that boundary; editors then
                // share this authority instead of manufacturing ExplicitRequest evidence.
                _solo = new(key, _unlocks, authority); _soloKey = key; AuthorityCaptures++;
            }
            return _solo;
        }

        internal OrderedPartyAuthority Party(ModRuntimeSnapshot runtime, IReadOnlyList<ModelKey> characters,
            IReadOnlyList<SerializableUnlockState> unlocks, int ascension, IReadOnlyList<string> sources)
        {
            string key = JsonSerializer.Serialize(new { Freshness = _freshness, characters, unlocks, ascension, sources });
            if (_party is null || key != _partyKey)
            {
                _party = PartyRuntimeAuthorityCapture.Capture(runtime.Profile,
                    ProfileSeedGenerator.CreateProbeSeed(runtime.Profile), characters, unlocks,
                    ascension, runtime.Detection.NormalizedVersion, sources);
                _partyKey = key; AuthorityCaptures += characters.Count;
            }
            return _party;
        }

        internal CompiledSearch Compile(WorkbenchSearchDraft draft, ModRuntimeSnapshot runtime)
        {
            if (draft.Players.Count == 0)
                return draft.Compile(runtime, out _, Solo(runtime, draft.Character, draft.Ascension).Authority);
            var party = Party(runtime, draft.Players.Select(p => p.Character).ToArray(),
                draft.Players.Select(p => p.RequireUnlocks()).ToArray(), draft.Ascension,
                draft.Players.Select(p => p.UnlockSource).ToArray());
            return draft.Compile(runtime, out _, party.Players[0], party);
        }

        private sealed class Observation(EditorInputs owner) : IDisposable
        {
            public void Dispose() => owner._depth--;
        }
    }

    private EditorRefreshContext? ActiveEditorContext() => EditorContextForSeat(_multiplayer ? _seat : 0);

    private EditorRefreshContext? EditorContextForSeat(int seat)
    {
        if (!_multiplayer) return _editorInputs.Solo(_runtime, _soloCharacter, _soloAscension);
        var party = EditorParty();
        return party is null ? null : new EditorRefreshContext(party.Fingerprint + "/" + seat,
            _lobbyUnlocks[seat]!, party.Players[seat]);
    }

    internal IDisposable ObserveEnvironmentInputs() => _editorInputs.Observe(_runtime);

    internal void AdoptPreparedInputs(EditorEnvironmentObservedInputs input, string freshness,
        RuntimeContextAuthoritySnapshot? solo, ModelKey character, int ascension) =>
        _editorInputs.AdoptPreparedInputs(input, freshness, solo, character, ascension);

    // Called after the main-menu content observation, including hot opens.
    internal void RefreshEnvironmentInputs()
    {
        using var observation = _editorInputs.Observe(_runtime);
        // An outer Open observation may already have advanced Freshness. Compare
        // against the editors' last successful binding, not that observation.
        if (_boundEditorFreshness != _editorInputs.Freshness)
        {
            InvalidateEnvironmentProbabilityPreview();
            RefreshEditorContexts();
        }
    }

    internal (int Observations, int AuthorityCaptures) EditorInputCounts =>
        (_editorInputs.Observations, _editorInputs.AuthorityCaptures);
}
