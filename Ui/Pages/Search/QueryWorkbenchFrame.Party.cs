using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private int _partyAscension = 10;
    private readonly string[] _partyUnlockSources = Enumerable.Repeat("AssumedFullyUnlocked", MaximumPreviewPlayers).ToArray();
    private bool _partyConfigured;
    private readonly Dictionary<int, List<Action>> _inactiveSeats = [];

    private void ResizeParty(int count)
    {
        for (int slot = count; slot < _playerCount; slot++)
        {
            var restore = new List<Action>();
            _neowEditor.StashPartySeat(slot, restore);
            _combatEditor.StashPartySeat(slot, restore);
            _shopEditor.StashPartySeat(slot, restore);
            _relicEditor.StashPartySeat(slot, restore);
            _ancientEditor.StashPartySeat(slot, restore);
            _eventEditor.StashPartySeat(slot, restore);
            _inactiveSeats[slot] = restore;
        }
        for (int slot = _playerCount; slot < count; slot++)
            if (_inactiveSeats.Remove(slot, out var restore))
                foreach (var restoreDraft in restore) restoreDraft();
        _playerCount = count;
        _seat = Math.Min(_seat, count - 1);
    }

    private static (SerializableUnlockState Unlocks, string Source) ManualSeatUnlocks(int slot)
    {
        if (slot == 0)
        {
            try { return (LobbyUnlockReadout.Copy(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable()), "CapturedLocalProfile"); }
            catch (Exception ex) { Bootstrap.RuntimeLog.Warn("partyLocalProgressUnavailable=" + ex.Message); }
        }
        return (UnlockState.all.ToSerializable(), "AssumedFullyUnlocked");
    }

    private static (SerializableUnlockState Unlocks, string Source) ReadSeatUnlocks(SerializableUnlockState? unlocks)
    {
        if (unlocks?.UnlockedEpochs is not null && unlocks.EncountersSeen is not null)
            return (LobbyUnlockReadout.Copy(unlocks), "CapturedLobbySlot");
        return (UnlockState.all.ToSerializable(), "AssumedFullyUnlocked");
    }

    private void UseManualPartyUnlocks()
    {
        _readLobby = null;
        _readRoster = "";
        _partyConfigured = true;
        for (int slot = 0; slot < MaximumPreviewPlayers; slot++)
            (_lobbyUnlocks[slot], _partyUnlockSources[slot]) = ManualSeatUnlocks(slot);
    }

    private bool DropExpiredLobby()
    {
        if (_readLobby is null) return false;
        var current = IsInsideTree() ? LobbyUnlockReadout.Find(GetTree().Root) : null;
        if (ReferenceEquals(current, _readLobby) && LobbyUnlockReadout.Roster(_readLobby) == _readRoster) return false;
        UseManualPartyUnlocks();
        return true;
    }

    private WorkbenchSearchDraft PrepareSavedEditorParty(WorkbenchSearchDraft draft)
    {
        // A saved editor can outlive its lobby and account. Results and checkpoints
        // retain their original snapshots through their own restore paths.
        if (draft.Players.Count == 0 || draft.RequiresLobbyRead) return draft;
        var players = draft.Players.Select(p =>
        {
            var (unlocks, source) = ManualSeatUnlocks(p.Slot);
            return p with { Unlocks = unlocks, UnlockSource = source };
        }).ToArray();
        // Manual progress has just been captured for the running version. Preserve
        // the existing version check for old lobby captures and malformed versions.
        var saved = GameVersionIdentity.From(draft.GameVersion);
        var current = GameVersionIdentity.From(_runtime.Detection.NormalizedVersion);
        if (saved.IsExact && current.IsExact && saved != current && draft.Players.All(p =>
            p.UnlockSource == "AssumedFullyUnlocked" || p.Slot == 0 && p.UnlockSource == "CapturedLocalProfile"))
            draft = draft with { GameVersion = current.ExactVersion, Profile = _runtime.Profile.ProfileId };
        return draft with { Players = players };
    }

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
        { Freshness = _editorInputs.Freshness, Ascension = _partyAscension, Characters = _seatCharacters.Take(_playerCount), Unlocks = _lobbyUnlocks.Take(_playerCount), Sources = _partyUnlockSources.Take(_playerCount) });
        if (key != _editorPartyKey)
        {
            _editorParty = _editorInputs.Party(_runtime,
                _seatCharacters.Take(_playerCount).ToArray(), _lobbyUnlocks.Take(_playerCount).Select(u => u!).ToArray(),
                _partyAscension, _partyUnlockSources.Take(_playerCount).ToArray());
            _editorPartyKey = key;
        }
        return _editorParty;
    }

    private WorkbenchSearchDraft CapturePartyDraft()
    {
        var players = Enumerable.Range(0, _playerCount).Select(slot => new WorkbenchPlayerDraft(slot, _seatCharacters[slot],
            _lobbyUnlocks[slot], _partyUnlockSources[slot])
        { AncientEditor = _ancientEditor.ExportPartyEditorState(slot), NeowEditor = _neowEditor.ExportPartyEditorState(slot),
            EventEditor = _eventEditor.ExportPartyEditorState(slot), CombatRewardEditor = _combatEditor.ExportPartyEditorState(slot) }).ToArray();
        var queries = new List<PlayerOfferQuery>();
        for (int slot = 0; slot < _playerCount; slot++)
        {
            var local = _neowEditor.ExportPartyConditions(slot) with
            {
                RelicSequenceConstraints = _relicEditor.BuildPartyConditions(slot),
                TreasureRelics = _relicEditor.BuildPartyTreasureCondition(slot),
                CombatCardRewards = _combatEditor.ExportPartyCondition(slot),
                CombatPotionRewards = _combatEditor.ExportPartyPotionCondition(slot),
                CombatCardExclusion = _combatEditor.ExportPartyExclusionCondition(slot)
            };
            local = _shopEditor.ExportPartyQuery(slot, _eventEditor.ExportPartyQuery(slot, _ancientEditor.ExportPartyQuery(slot, local)));
            queries.Add(new(slot, _neowEditor.ExportPartyOffers(slot))
            { Conditions = local, AncientPremises = _ancientEditor.PartyOptionConditions(slot) });
        }
        var q = _eventEditor.ExportSharedPartyQuery(_ancientEditor.ExportSharedPartyQuery(_actInformationEditor.ExportQuery(SearchQuery.Empty))) with { Players = queries };
        return new(players[0].Character, _partyAscension, q, AncientOptionConditionProfile.BroadDefault)
        { Version = 4, Players = players,
            PartyAncientEditor = _ancientEditor.ExportSharedPartyEditorState(),
            MapEditor = _actInformationEditor.ExportMapState(),
            Mode = Core.World.Snapshots.WorldGameMode.Multiplayer, GameVersion = _runtime.Detection.NormalizedVersion,
            Profile = _runtime.Profile.ProfileId, ObservationVersion = PartyNeowQuery.HasTransactions(q) ? Core.Effects.PartyNeowAdmission.ObservationVersion : OrderedPartyAuthority.ObservationVersion };
    }
    private void RestorePartyDraft(WorkbenchSearchDraft draft, bool render)
    {
        if (draft.Players.Count is < 2 or > MaximumPreviewPlayers || draft.Query.Players.Count != draft.Players.Count ||
            draft.Players.Where((p, i) => p.Slot != i || draft.Query.Players[i].Slot != i).Any())
            throw new InvalidOperationException("Party.InvalidDraftSlots");
        draft = draft.RefreshAssumedPartyUnlocks(_runtime);
        bool legacyPending = draft.RequiresLobbyRead;
        if (legacyPending)
        {
            Persistence.SearchPresetCompatibilityResolver.ValidateWorkbenchShape(draft.ToPresetIntent());
            // Older editors could save an expired roster or individually unread seats.
            // Rebind only the editable draft. Stored execution snapshots remain exact.
            bool incomplete = draft.Players.Any(p => p.Unlocks is null);
            draft = draft with { Players = draft.Players.Select(p =>
            {
                if (incomplete && p.Unlocks is not null) return p;
                var (unlocks, source) = ManualSeatUnlocks(p.Slot);
                return p with { Unlocks = unlocks, UnlockSource = source };
            }).ToArray(), RequiresLobbyRead = false };
        }
        else _editorInputs.Compile(draft, _runtime);
        // Map predicates on a saved player are shared facts at compilation too.
        // Import that same bound map intent instead of losing it in personal editors.
        var boundMaps = PartyInitialQuery.BindSharedConditions(draft.Query);
        var mapQuery = draft.Query with { StandardMaps = boundMaps.StandardMaps,
            StandardMapRoutes = boundMaps.StandardMapRoutes, ElitePrefixResearch = boundMaps.ElitePrefixResearch,
            EncounterQueues = boundMaps.EncounterQueues };
        ValidateRepresentable(mapQuery with { Players = [] }, allowSharedAncientIdentity: true);
        foreach (var p in draft.Query.Players) ValidateRepresentable(p.Offers.IsEmpty ? p.Conditions :
            p.Conditions with { LegacyNeow = p.Conditions.LegacyNeow with { NeowRelics = p.Offers } });
        _readLobby = null; _readRoster = ""; _partyConfigured = true;
        // Purge the previous request's extra seats before replacing the roster.
        ResizeParty(draft.Players.Count);
        _inactiveSeats.Clear();
        for (int slot = draft.Players.Count; slot < MaximumPreviewPlayers; slot++)
        {
            _seatCharacters[slot] = BaseGameModelKeys.Characters.Ironclad;
            (_lobbyUnlocks[slot], _partyUnlockSources[slot]) = ManualSeatUnlocks(slot);
        }
        _multiplayer = true; _seat = 0; _partyAscension = draft.Ascension;
        _editorParty = null; _editorPartyKey = "";
        foreach (var p in draft.Players)
        {
            _seatCharacters[p.Slot] = p.Character; _lobbyUnlocks[p.Slot] = p.Unlocks is null ? null : LobbyUnlockReadout.Copy(p.Unlocks);
            _partyUnlockSources[p.Slot] = p.UnlockSource;
            _neowEditor.ImportPartyOffers(p.Slot, ModelKeySetFilter.Empty);
        }
        // Shared identity/mode owns personal A interpretation. Import it before any
        // personal refresh can revalidate against the previous table's identities.
        _ancientEditor.ImportSharedPartyQuery(draft.Query, draft.PartyAncientEditor, draft.Players);
        RefreshEditorContexts(false);
        _eventEditor.ImportSharedPartyQuery(draft.Query);
        foreach (var p in draft.Query.Players)
        {
            _seat = p.Slot; RefreshEditorContexts(false);
            var local = p.Conditions;
            if (!p.Offers.IsEmpty)
                local = local with { LegacyNeow = local.LegacyNeow with { NeowRelics = p.Offers } };
            if (p.SelectedOption is { } oldPlan)
                local = local with { OpeningRoute = new(oldPlan.Option), StructuredOpeningEffects = p.Results };
            _neowEditor.ImportQuery(local);
            if (draft.Players[p.Slot].NeowEditor is { } neowState) _neowEditor.ImportEditorState(neowState);
            _relicEditor.ImportQuery(local); _combatEditor.ImportQuery(local);
            if (draft.Players[p.Slot].CombatRewardEditor is { } combatState) _combatEditor.ImportEditorState(combatState);
            _ancientEditor.ImportQuery(local, p.AncientPremises, draft.Players[p.Slot].AncientEditor);
            _eventEditor.ImportQuery(local, importQueue: false);
            if (draft.Players[p.Slot].EventEditor is { } eventState) _eventEditor.ImportEditorState(eventState);
            _shopEditor.ImportQuery(local);
        }
        _seat = 0;
        _ancientEditor.ImportSharedPartyQuery(draft.Query, draft.PartyAncientEditor, draft.Players);
        _actInformationEditor.ImportQuery(mapQuery, draft.MapEditor); RefreshEditorContexts(render);
        if (legacyPending && System.Text.Json.JsonSerializer.Serialize(draft.Query) !=
            System.Text.Json.JsonSerializer.Serialize(CapturePartyDraft().Query))
            throw new InvalidOperationException("integration.load_shape");
    }
}
