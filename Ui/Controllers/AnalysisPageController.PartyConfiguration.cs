using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Controllers;

internal sealed partial class AnalysisPageController
{
    private void OpenPartyConfiguration()
    {
        if (_partyDraft is not { } current) return;
        _page.OpenPartyConfiguration(current, next =>
        {
            // Apply atomically; cancel and failed lobby reads never modify the active roster.
            ClearSeedLibraryOverrides();
            _partyDraft = next;
            _lastPartyDraft = next;
            _sourcePartyDraftKey = System.Text.Json.JsonSerializer.Serialize(next);
            _partySelections.Clear(); _partyExplicitSelections.Clear();
            _partySelectionSeed = _page.CurrentDraft.RawSeed;
            _page.ConfigureParty(next.Players.Count);
            SelectPartyPlayer(Math.Min(_page.CurrentDraft.PlayerSlotIndex, next.Players.Count - 1));
            PersistPartyConfiguration();
        });
    }

    private void PreparePartySelection(AnalysisRequestDraft next)
    {
        if (_partyDraft is not { } party) return;
        bool newSeed = _partySelectionSeed != next.RawSeed;
        bool changedCharacter = party.Players[next.PlayerSlotIndex].Character != next.CharacterKey;
        if (!newSeed && !changedCharacter) return;
        if (newSeed) { _partySelections.Clear(); _partyExplicitSelections.Clear(); }
        else { _partySelections.Remove(next.PlayerSlotIndex); _partyExplicitSelections.Remove(next.PlayerSlotIndex); }
        _strictLibrarySeed = "";
        _partySelectionSeed = next.RawSeed;
        _page.SetPredictorContext(next.CharacterKey, next.Ascension, next.PlayersCount, next.PlayerSlotIndex,
            next.AncientOptionConditions, "", null, "", false);
    }

    private SeedLibraryContext PartyContext(WorkbenchSearchDraft draft) => new(
        _runtime.Detection.NormalizedVersion, _runtime.Profile.ProfileId, WorldGameMode.Multiplayer,
        draft.Ascension, draft.Players.Select(p =>
        {
            var selection = _partySelections.GetValueOrDefault(p.Slot);
            return new SeedLibraryPlayer(p.Slot, p.Character, LobbyUnlockReadout.Copy(p.RequireUnlocks()), p.UnlockSource,
                draft.Query.Players[p.Slot].AncientPremises,
                SeedLibraryContextCapture.OpeningPremise(draft.Query.Players[p.Slot].Conditions),
                selection.Choice.HasValue || !string.IsNullOrWhiteSpace(selection.Route)
                    ? new(selection.Choice, selection.Route ?? "", _partyExplicitSelections.Contains(p.Slot)) : null);
        }).ToArray(), _partyDraft is null ? 0 : _page.CurrentDraft.PlayerSlotIndex);

    private void PersistPartyConfiguration()
    {
        if ((_partyDraft ?? _lastPartyDraft) is not { } draft) return;
        _lastPartyDraft = draft;
        _persistence.SavePredictorParty(PartyContext(draft), _partySelectionSeed, _partyDraft is not null);
    }

    internal void RestorePartyConfiguration(PredictorContextDocument saved, bool restoreMode)
    {
        if (saved.Party is not { Mode: WorldGameMode.Multiplayer } context) return;
        try
        {
            SeedLibraryStore.ValidateContext(context);
            var available = Infrastructure.Snapshots.RuntimeCharacterCatalogCapture.Capture().EffectiveCharacters;
            if (context.Players.Any(p => !available.Contains(p.Character)))
                throw new InvalidOperationException("Party.CharacterUnavailable");
            var draft = new WorkbenchSearchDraft(context.Players[0].Character, context.Ascension,
                SearchQuery.Empty with { Players = context.Players.Select(p => new PlayerOfferQuery(p.Slot, ModelKeySetFilter.Empty)
                    { Conditions = p.OpeningPremise, AncientPremises = p.AncientPremises }).ToArray() },
                context.Players[0].AncientPremises)
            {
                Mode = WorldGameMode.Multiplayer,
                Players = context.Players.Select(p => new WorkbenchPlayerDraft(p.Slot, p.Character,
                    LobbyUnlockReadout.Copy(p.Unlocks), p.UnlockSource)).ToArray()
            };
            _lastPartyDraft = draft;
            _partySelectionSeed = saved.PartySeed;
            _partySelections.Clear(); _partyExplicitSelections.Clear();
            if (context.GameVersion == _runtime.Detection.NormalizedVersion && context.Profile == _runtime.Profile.ProfileId)
                foreach (var p in context.Players)
                    if (p.Selection is { } selection)
                    {
                        _partySelections[p.Slot] = (selection.ChoiceSlot, selection.Route);
                        if (selection.Explicit) _partyExplicitSelections.Add(p.Slot);
                    }
            if (!restoreMode || !saved.PartyActive) return;
            _soloDraft = _page.CurrentDraft;
            _partyDraft = draft;
            _page.ConfigureParty(draft.Players.Count);
            var slot = Math.Clamp(context.SelectedSlot, 0, draft.Players.Count - 1);
            var choice = _partySelections.GetValueOrDefault(slot);
            _page.SetPredictorContext(draft.Players[slot].Character, draft.Ascension, draft.Players.Count, slot,
                draft.Query.Players[slot].AncientPremises, choice.Route ?? "", choice.Choice, "", false);
        }
        catch (Exception ex) { RuntimeLog.Warn("predictorPartyRestoreFailed=" + ex.Message); }
    }
}
