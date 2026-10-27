using RolltheSpire2.Compatibility;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
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

    internal void ClearSeedLibraryOverrides(bool preserveResultOrigin = false)
    {
        _librarySoloUnlocks = null; _librarySoloSelection = null;
        _librarySoloPremise = SearchQuery.Empty;
        _soloExplicitOpening = false;
        _librarySoloUnlockSource = "CapturedLocalProfile"; _strictLibrarySeed = "";
        _searchOpeningPending = false; _searchOpeningQuery = SearchQuery.Empty;
        _searchOpeningUnavailable = false;
        if (!preserveResultOrigin)
        {
            _resultOrigin = null; _resultAssociation = null;
        }
        RefreshResultOrigin();
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
        if (!ProfileSeedRules.TryCanonicalizePredictionSeed(_runtime.Profile, current.RawSeed, out var seed, out _) ||
            _page.LastDocument?.CanonicalSeed != seed || _page.LastRequest is not { } request ||
            request.Character.CharacterKey != current.CharacterKey || request.Ascension != current.Ascension ||
            request.PlayersCount != current.PlayersCount || request.PlayerSlotIndex != current.PlayerSlotIndex)
            throw new InvalidOperationException("SeedLibrary.ValidPredictionRequired");
        if (_partyDraft is { } draft) return PartyContext(draft);
        if (_lastPredictionUnlocks is null) throw new InvalidOperationException("SeedLibrary.UnlockContextMissing");
        return new(_runtime.Detection.NormalizedVersion, _runtime.Profile.ProfileId, WorldGameMode.Singleplayer,
            current.Ascension, [new(0, current.CharacterKey, LobbyUnlockReadout.Copy(_lastPredictionUnlocks), _librarySoloUnlockSource,
                current.AncientOptionConditions, _librarySoloPremise,
                new(_page.PreferredOpeningChoiceSlotIndex, _page.PreferredOpeningRouteId, _soloExplicitOpening))]);
    }

    internal void OpenSeedLibraryEntry(SeedLibraryEntry entry)
    {
        if (!entry.CanOpen || entry.Context is not { } context) throw new InvalidOperationException(entry.Issue);
        OpenPredictionContext(entry.Seed, context);
        var association = entry.QueryAssociations.FirstOrDefault(a => a.QueryKey == entry.ResultProvenance?.SourceQueryKey &&
            a.SearchRecordId == entry.ResultProvenance.SearchRecordId);
        InstallResultOrigin(entry.ResultProvenance, association, entry.Seed, context);
    }

    // Seed favorites and the in-run shortcut share the existing detached-context import.
    internal void OpenPredictionContext(string rawSeed, SeedLibraryContext context)
    {
        SeedLibraryStore.ValidateContext(context);
        SeedLibraryStore.ValidateRuntimeReferences(context, Bootstrap.RuntimeAuthorityEnvironment.Current.Authority);
        if (context.GameVersion != _runtime.Detection.NormalizedVersion || context.Profile != _runtime.Profile.ProfileId)
            throw new InvalidOperationException("SeedLibrary.StaleContext: " + context.GameVersion);
        if (!ProfileSeedRules.TryCanonicalizePredictionSeed(_runtime.Profile, rawSeed, out var seed, out var issue)) throw new InvalidOperationException(issue);
        var characters = RuntimeCharacterCatalogCapture.Capture().EffectiveCharacters;
        if (context.Players.Any(p => !characters.Contains(p.Character))) throw new InvalidOperationException("SeedLibrary.CharacterUnavailable");
        ClearSeedLibraryOverrides();
        // Rebuild authority now. Persisted query data contains only N premises and
        // Ancient assumptions; no saved result is treated as current evidence.
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
            _searchOpeningSeed = seed;
            _searchOpeningQuery = player.OpeningPremise;
            _searchOpeningPending = player.Selection?.Explicit != true && player.OpeningPremise.OpeningRoute is not null;
            _page.SetPredictorContext(player.Character, context.Ascension, 1, 0, player.AncientPremises,
                player.Selection?.Route ?? "", player.Selection?.ChoiceSlot, "", false);
            _page.SetSeedText(seed, commit: true);
            AnalyzeCurrentDraft();
        }
        else
        {
            var draft = PartyDraftFromContext(context);
            draft = draft with
            { Version = 4, AncientPremises = AncientOptionConditionProfile.BroadDefault,
                GameVersion = context.GameVersion, Profile = context.Profile,
                ObservationVersion = PartyNeowQuery.HasTransactions(draft.Query)
                    ? PartyNeowAdmission.ObservationVersion : OrderedPartyAuthority.ObservationVersion };
            // Explicitly install every slot before analysis; SetPartyDraft would
            // otherwise analyze P1 before the saved selection table is present.
            draft.Compile(_runtime, out _);
            _partyDraft = draft; _lastPartyDraft = draft;
            _partySelectionSeed = seed;
            _sourcePartyDraftKey = System.Text.Json.JsonSerializer.Serialize(draft);
            InstallPartySelections(context, restoreSelections: true);
            _page.ConfigureParty(draft.Players.Count); _page.SetSeedText(seed, commit: true);
            SelectPartyPlayer(context.SelectedSlot);
        }
        if (_libraryPredictionIssue.Length > 0) throw new InvalidOperationException(_libraryPredictionIssue);
        if (_page.LastDocument?.CanonicalSeed != seed) throw new InvalidOperationException("SeedLibrary.PredictionUnavailable");
    }

    private static void ValidateLibrarySelection(IReadOnlyList<NeowChoiceResult> choices, int? choice, string? route)
    {
        // An absent/uncertain observation cannot prove the saved choice is illegal.
        // Its frozen selection remains in the library context; prediction shows the gap.
        if (choices.Count == 0 || choices.Any(c => c.IdentityPrecision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported)) return;
        var selected = choice.HasValue ? choices.Where(c => c.SlotIndex == choice).ToArray() : choices.ToArray();
        if (!string.IsNullOrWhiteSpace(route) && selected.Any(c => c.OpeningRewardContinuations is null ||
                c.OpeningRewardContinuations.Precision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported)) return;
        if (selected.Length == 0 || !string.IsNullOrWhiteSpace(route) && !selected.Any(c =>
            PartyNeowProjection.ConcreteRoutes(c).Any(r => r.OpeningRewardContinuations?.Routes.Any(x => x.Route.RouteId == route) == true)))
            throw new InvalidOperationException("SeedLibrary.OpeningSelectionUnavailable");
    }
}
