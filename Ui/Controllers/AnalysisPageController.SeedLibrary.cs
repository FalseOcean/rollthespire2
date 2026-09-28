using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Controllers;

internal sealed partial class AnalysisPageController
{
    private SerializableUnlockState? _librarySoloUnlocks, _lastPredictionUnlocks;
    private string _librarySoloUnlockSource = "CapturedLocalProfile";
    private string _strictLibrarySeed = "", _libraryPredictionIssue = "";
    private SeedLibraryOpeningSelection? _librarySoloSelection;
    private SearchQuery _librarySoloPremise = SearchQuery.Empty;
    private bool _soloExplicitOpening;

    internal void ClearSeedLibraryOverrides()
    {
        _librarySoloUnlocks = null; _librarySoloSelection = null;
        _librarySoloPremise = SearchQuery.Empty;
        _soloExplicitOpening = false;
        _librarySoloUnlockSource = "CapturedLocalProfile"; _strictLibrarySeed = "";
    }

    private UnlockState? CapturePredictionUnlocks()
    {
        _lastPredictionUnlocks = _librarySoloUnlocks is not null ? LobbyUnlockReadout.Copy(_librarySoloUnlocks)
            : SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable();
        // Ordinary prediction keeps the existing profile-specific capture path.
        // Only an explicitly reopened favorite supplies an override.
        return _librarySoloUnlocks is null ? null : UnlockState.FromSerializable(_lastPredictionUnlocks);
    }

    internal SeedLibraryContext CaptureSeedLibraryContext()
    {
        if (!_page.CanFavoriteVisibleSeed || _libraryPredictionIssue.Length > 0) throw new InvalidOperationException("SeedLibrary.ValidPredictionRequired");
        var current = _page.CurrentReactiveDraft;
        if (!_runtime.Profile.TryCanonicalizeSeed(current.RawSeed, out var seed, out _) ||
            _page.LastDocument?.CanonicalSeed != seed || _page.LastRequest is not { } request ||
            request.Character.CharacterKey != current.CharacterKey || request.Ascension != current.Ascension ||
            request.PlayersCount != current.PlayersCount || request.PlayerSlotIndex != current.PlayerSlotIndex)
            throw new InvalidOperationException("SeedLibrary.ValidPredictionRequired");
        if (_partyDraft is { } draft)
        {
            return new(_runtime.Detection.NormalizedVersion, _runtime.Profile.ProfileId, WorldGameMode.Multiplayer,
                draft.Ascension, draft.Players.Select(p =>
                {
                    var selection = _partySelections.GetValueOrDefault(p.Slot);
                    return new SeedLibraryPlayer(p.Slot, p.Character, LobbyUnlockReadout.Copy(p.Unlocks), p.UnlockSource,
                        draft.Query.Players[p.Slot].AncientPremises,
                        SeedLibraryContextCapture.OpeningPremise(draft.Query.Players[p.Slot].Conditions),
                        selection.Choice.HasValue || !string.IsNullOrWhiteSpace(selection.Route)
                            ? new(selection.Choice, selection.Route ?? "", _partyExplicitSelections.Contains(p.Slot)) : null);
                }).ToArray(), current.PlayerSlotIndex);
        }
        if (_lastPredictionUnlocks is null) throw new InvalidOperationException("SeedLibrary.UnlockContextMissing");
        return new(_runtime.Detection.NormalizedVersion, _runtime.Profile.ProfileId, WorldGameMode.Singleplayer,
            current.Ascension, [new(0, current.CharacterKey, LobbyUnlockReadout.Copy(_lastPredictionUnlocks), _librarySoloUnlockSource,
                current.AncientOptionConditions, _librarySoloPremise,
                new(_page.PreferredOpeningChoiceSlotIndex, _page.PreferredOpeningRouteId, _soloExplicitOpening))]);
    }

    internal void OpenSeedLibraryEntry(SeedLibraryEntry entry)
    {
        if (!entry.CanOpen || entry.Context is not { } context) throw new InvalidOperationException(entry.Issue);
        SeedLibraryStore.ValidateContext(context);
        SeedLibraryStore.ValidateRuntimeReferences(context, Bootstrap.RuntimeAuthorityEnvironment.Current.Authority);
        if (context.GameVersion != _runtime.Detection.NormalizedVersion || context.Profile != _runtime.Profile.ProfileId)
            throw new InvalidOperationException("SeedLibrary.StaleContext: " + context.GameVersion);
        if (!_runtime.Profile.TryCanonicalizeSeed(entry.Seed, out var seed, out var issue)) throw new InvalidOperationException(issue);
        var characters = RuntimeCharacterCatalogCapture.Capture().EffectiveCharacters;
        if (context.Players.Any(p => !characters.Contains(p.Character))) throw new InvalidOperationException("SeedLibrary.CharacterUnavailable");
        // Rebuild authority now. Persisted query data contains only N premises and
        // Ancient assumptions; no saved result is treated as current evidence.
        var players = context.Players.Select(p => new WorkbenchPlayerDraft(p.Slot, p.Character,
            LobbyUnlockReadout.Copy(p.Unlocks), p.UnlockSource)).ToArray();
        var query = SearchQuery.Empty with { Players = context.Players.Select(p => new PlayerOfferQuery(p.Slot, ModelKeySetFilter.Empty)
            { Conditions = p.OpeningPremise, AncientPremises = p.AncientPremises }).ToArray() };
        _libraryPredictionIssue = "";
        _strictLibrarySeed = seed;
        if (context.Mode == WorldGameMode.Singleplayer)
        {
            SetPartyDraft(null);
            var player = context.Players[0];
            _librarySoloUnlocks = LobbyUnlockReadout.Copy(player.Unlocks);
            _librarySoloUnlockSource = player.UnlockSource;
            _librarySoloSelection = player.Selection;
            _librarySoloPremise = player.OpeningPremise;
            _soloExplicitOpening = player.Selection?.Explicit == true;
            _page.SetPredictorContext(player.Character, context.Ascension, 1, 0, player.AncientPremises,
                player.Selection?.Route ?? "", player.Selection?.ChoiceSlot, "", false);
            _page.SetSeedText(seed, commit: true);
            AnalyzeCurrentDraft();
        }
        else
        {
            var draft = new WorkbenchSearchDraft(players[0].Character, context.Ascension, query, AncientOptionConditionProfile.BroadDefault)
            { Version = 4, Mode = WorldGameMode.Multiplayer, Players = players,
                GameVersion = context.GameVersion, Profile = context.Profile,
                ObservationVersion = PartyNeowQuery.HasTransactions(query) ? PartyNeowAdmission.ObservationVersion : OrderedPartyAuthority.ObservationVersion };
            // Explicitly install every slot before analysis; SetPartyDraft would
            // otherwise analyze P1 before the saved selection table is present.
            draft.Compile(_runtime, out _);
            _partyDraft = draft; _lastPartyDraft = draft;
            _partySelectionSeed = seed;
            _sourcePartyDraftKey = System.Text.Json.JsonSerializer.Serialize(draft);
            _partySelections.Clear(); _partyExplicitSelections.Clear();
            foreach (var player in context.Players)
                if (player.Selection is { } selection)
                {
                    _partySelections[player.Slot] = (selection.ChoiceSlot, selection.Route);
                    if (selection.Explicit) _partyExplicitSelections.Add(player.Slot);
                }
            _page.ConfigureParty(players.Length); _page.SetSeedText(seed, commit: true);
            SelectPartyPlayer(context.SelectedSlot);
        }
        if (_libraryPredictionIssue.Length > 0) throw new InvalidOperationException(_libraryPredictionIssue);
        if (_page.LastDocument?.CanonicalSeed != seed) throw new InvalidOperationException("SeedLibrary.PredictionUnavailable");
    }

    private static void ValidateLibrarySelection(IReadOnlyList<NeowChoiceResult> choices, int? choice, string? route)
    {
        var selected = choice.HasValue ? choices.Where(c => c.SlotIndex == choice).ToArray() : choices.ToArray();
        if (selected.Length == 0 || !string.IsNullOrWhiteSpace(route) && !selected.Any(c =>
            PartyNeowProjection.ConcreteRoutes(c).Any(r => r.OpeningRewardContinuations?.Routes.Any(x => x.Route.RouteId == route) == true)))
            throw new InvalidOperationException("SeedLibrary.OpeningSelectionUnavailable");
    }
}
