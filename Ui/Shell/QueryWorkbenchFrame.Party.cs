using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class QueryWorkbenchFrame
{
    private int _partyAscension = 10;
    private readonly string[] _partyUnlockSources = Enumerable.Repeat("AssumedFullyUnlocked", MaximumPreviewPlayers).ToArray();
    private void UseFullyUnlockedParty()
    {
        _readLobby = null; _readRoster = "";
        for (int slot = 0; slot < MaximumPreviewPlayers; slot++)
        {
            _lobbyUnlocks[slot] = MegaCrit.Sts2.Core.Unlocks.UnlockState.all.ToSerializable();
            _partyUnlockSources[slot] = "AssumedFullyUnlocked"; _unlockReadStatus[slot] = 4;
        }
        UpdateSeatReadLabels(); RefreshEditorContexts();
    }
    internal WorkbenchSearchDraft? PartyInformationDraft => _multiplayer ? CapturePartyDraft() : null;
    public event Action<SearchCandidate, WorkbenchSearchDraft>? OpenPartyInformation;
    public event Action<SearchCandidate>? OpenSeedInformation;
    private WorkbenchSearchDraft? _runningPartyDraft;
    private OrderedPartyAuthority? _editorParty;
    private string _editorPartyKey = "";
    private string _editorContextIssue = "";
    private bool TryEditorParty(out OrderedPartyAuthority? party)
    {
        try
        {
            party = EditorParty();
            _editorContextIssue = "";
            return true;
        }
        catch (Exception ex)
        {
            party = null;
            if (_editorContextIssue != ex.Message)
                Bootstrap.RuntimeLog.Fault("partyEditorContextFailed=true", ex);
            _editorContextIssue = ex.Message;
            _editorParty = null; _editorPartyKey = "";
            _probabilityPreview.Invalidate(); _probabilityPending = false; _analysisKey = "";
            if (_start is not null) _start.Disabled = true;
            if (_status is not null) _status.Text = Explain(ex);
            return false;
        }
    }
    private OrderedPartyAuthority? EditorParty()
    {
        if (!_multiplayer || _lobbyUnlocks.Take(_playerCount).Any(u => u is null)) return null;
        string key = System.Text.Json.JsonSerializer.Serialize(new
        { Ascension = _partyAscension, Characters = _seatCharacters.Take(_playerCount), Unlocks = _lobbyUnlocks.Take(_playerCount), Sources = _partyUnlockSources.Take(_playerCount) });
        if (key != _editorPartyKey)
        {
            _editorParty = Infrastructure.Snapshots.PartyRuntimeAuthorityCapture.Capture(_runtime.Profile, "000000000000",
                _seatCharacters.Take(_playerCount).ToArray(), _lobbyUnlocks.Take(_playerCount).Select(u => u!).ToArray(),
                _partyAscension, _runtime.Detection.NormalizedVersion, _partyUnlockSources.Take(_playerCount).ToArray());
            _editorPartyKey = key;
        }
        return _editorParty;
    }

    private WorkbenchSearchDraft CapturePartyDraft()
    {
        if (_readLobby is not null)
        {
            if (_readLobby.Players.Count != _playerCount) throw new InvalidOperationException("Party.LobbySizeMismatch");
            if (!ReferenceEquals(LobbyUnlockReadout.Find(GetTree().Root), _readLobby) || LobbyUnlockReadout.Roster(_readLobby) != _readRoster)
                throw new InvalidOperationException("Party.LobbyChangedReadUnlocksAgain");
            for (int slot = 0; slot < _playerCount; slot++)
                _lobbyUnlocks[slot] = LobbyUnlockReadout.Copy(_readLobby.Players.Single(p => p.slotId == slot).unlockState);
        }
        var players = Enumerable.Range(0, _playerCount).Select(slot => new WorkbenchPlayerDraft(slot, _seatCharacters[slot],
            _lobbyUnlocks[slot] ?? throw new InvalidOperationException($"Party.UnlocksUnread:P{slot + 1}"), _partyUnlockSources[slot])
        { AncientEditor = _ancientEditor.ExportPartyEditorState(slot) }).ToArray();
        var queries = new List<PlayerOfferQuery>();
        for (int slot = 0; slot < _playerCount; slot++)
        {
            var local = _neowEditor.ExportPartyConditions(slot) with
            {
                RelicSequenceConstraints = _relicEditor.BuildPartyConditions(slot),
                CombatCardRewards = _combatEditor.ExportPartyCondition(slot),
                CombatPotionRewards = _combatEditor.ExportPartyPotionCondition(slot)
            };
            local = _shopEditor.ExportPartyQuery(slot, _eventEditor.ExportPartyQuery(slot, _ancientEditor.ExportPartyQuery(slot, local)));
            queries.Add(new(slot, _neowEditor.ExportPartyOffers(slot))
            { Conditions = local, AncientPremises = _ancientEditor.PartyOptionConditions(slot) });
        }
        var q = _ancientEditor.ExportSharedPartyQuery(_actInformationEditor.ExportQuery(SearchQuery.Empty)) with { Players = queries };
        return new(players[0].Character, _partyAscension, q, AncientOptionConditionProfile.BroadDefault)
        { Version = 4, Players = players, PartyAncientEditor = _ancientEditor.ExportSharedPartyEditorState(),
            Mode = Core.World.Snapshots.WorldGameMode.Multiplayer, GameVersion = _runtime.Detection.NormalizedVersion,
            Profile = _runtime.Profile.ProfileId, ObservationVersion = PartyNeowQuery.HasTransactions(q) ? Core.Effects.PartyNeowAdmission.ObservationVersion : OrderedPartyAuthority.ObservationVersion };
    }
    private void RestorePartyDraft(WorkbenchSearchDraft draft, bool render)
    {
        if (draft.Players.Count is < 2 or > MaximumPreviewPlayers || draft.Query.Players.Count != draft.Players.Count ||
            draft.Players.Where((p, i) => p.Slot != i || draft.Query.Players[i].Slot != i).Any())
            throw new InvalidOperationException("Party.InvalidDraftSlots");
        // Validate the entire authored grammar before touching editor state.
        draft.Compile(_runtime, out _);
        ValidateRepresentable(draft.Query with { Players = [] }, allowSharedAncientIdentity: true);
        foreach (var p in draft.Query.Players) ValidateRepresentable(p.Offers.IsEmpty ? p.Conditions :
            p.Conditions with { LegacyNeow = p.Conditions.LegacyNeow with { NeowRelics = p.Offers } });
        _readLobby = null; _readRoster = "";
        _multiplayer = true; _playerCount = draft.Players.Count; _seat = 0; _partyAscension = draft.Ascension;
        _editorParty = null; _editorPartyKey = "";
        foreach (var p in draft.Players)
        {
            _seatCharacters[p.Slot] = p.Character; _lobbyUnlocks[p.Slot] = LobbyUnlockReadout.Copy(p.Unlocks);
            _partyUnlockSources[p.Slot] = p.UnlockSource;
            _unlockReadStatus[p.Slot] = p.UnlockSource == "AssumedFullyUnlocked" ? 4 : 1;
            _neowEditor.ImportPartyOffers(p.Slot, ModelKeySetFilter.Empty);
        }
        // Shared identity/mode owns personal A interpretation. Import it before any
        // personal refresh can revalidate against the previous table's identities.
        _ancientEditor.ImportSharedPartyQuery(draft.Query, draft.PartyAncientEditor, draft.Players);
        RefreshEditorContexts(false);
        foreach (var p in draft.Query.Players)
        {
            _seat = p.Slot; RefreshEditorContexts(false);
            var local = p.Conditions;
            if (!p.Offers.IsEmpty)
                local = local with { LegacyNeow = local.LegacyNeow with { NeowRelics = p.Offers } };
            if (p.SelectedOption is { } oldPlan)
                local = local with { OpeningRoute = new(oldPlan.Option), StructuredOpeningEffects = p.Results };
            _neowEditor.ImportQuery(local);
            _relicEditor.ImportQuery(local); _combatEditor.ImportQuery(local);
            _ancientEditor.ImportQuery(local, p.AncientPremises, draft.Players[p.Slot].AncientEditor);
            _eventEditor.ImportQuery(local); _shopEditor.ImportQuery(local);
        }
        _seat = 0;
        _ancientEditor.ImportSharedPartyQuery(draft.Query, draft.PartyAncientEditor, draft.Players);
        _actInformationEditor.ImportQuery(draft.Query); RefreshEditorContexts(render);
    }
}
