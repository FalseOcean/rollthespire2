using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Controllers;

internal sealed partial class AnalysisPageController
{
    private SearchQuery _searchOpeningQuery = SearchQuery.Empty;
    private bool _searchOpeningPending;
    private string _searchOpeningSeed = "";
    private SeedLibraryResultProvenance? _resultOrigin;
    private SeedQueryAssociation? _resultAssociation;

    internal void ImportSearchOpening(SearchCandidate candidate, WorkbenchSearchDraft? draft, SeedLibraryContext? context,
        SeedQueryAssociation? association = null)
    {
        _searchOpeningSeed = candidate.Seed;
        _searchOpeningQuery = draft?.Query ?? SearchQuery.Empty;
        _searchOpeningPending = candidate.IsUnverified;
        if (context is { Players.Count: 1 })
        {
            _librarySoloUnlocks = LobbyUnlockReadout.Copy(context.Players[0].Unlocks);
            _librarySoloUnlockSource = context.Players[0].UnlockSource;
            _librarySoloPremise = context.Players[0].OpeningPremise;
            _soloExplicitOpening = !candidate.IsUnverified && SeedLibraryContextCapture.WithWitness(context, candidate).Players[0].Selection?.Explicit == true;
        }
        else if (draft is { Mode: Core.World.Snapshots.WorldGameMode.Singleplayer })
            _librarySoloPremise = SeedLibraryContextCapture.OpeningPremise(draft.Query);
        var sourceContext = context is not null && !candidate.IsUnverified
            ? SeedLibraryContextCapture.WithWitness(context, candidate) : context;
        if (sourceContext is { Mode: Core.World.Snapshots.WorldGameMode.Multiplayer })
            foreach (var player in sourceContext.Players)
                if (player.Selection?.Explicit == true) _partyExplicitSelections.Add(player.Slot);
        _searchOpeningUnavailable = candidate.IsUnverified && draft is null;
        InstallResultOrigin(SeedLibraryResultProvenance.From(candidate, association), association, candidate.Seed, sourceContext);
    }

    private void InstallResultOrigin(SeedLibraryResultProvenance? provenance, SeedQueryAssociation? association,
        string seed, SeedLibraryContext? context)
    {
        _resultAssociation = association;
        _resultOrigin = provenance is not null && provenance.SourceContextIdentity.Length == 0
            ? context is not null
                ? provenance with { SourceSeed = seed, SourceContextIdentity = SeedLibraryStore.ContextIdentity(seed, context) }
                : provenance with { AppliesToCurrentContext = false } : provenance;
        RefreshResultOrigin();
    }

    internal (SeedQueryAssociation? Association, SeedLibraryResultProvenance? Provenance) CaptureResultOrigin(SeedLibraryContext context)
    {
        var origin = _resultOrigin?.ForContext(_page.LastDocument!.CanonicalSeed, context);
        return (origin?.AppliesToCurrentContext == true ? _resultAssociation : null, origin);
    }

    private void RefreshResultOrigin()
    {
        var provenance = _resultOrigin;
        if (provenance is not null)
        {
            try
            {
                var context = CaptureSeedLibraryContext();
                provenance = provenance.ForContext(_page.LastDocument!.CanonicalSeed, context);
            }
            catch (InvalidOperationException) { provenance = provenance with { AppliesToCurrentContext = false }; }
        }
        _page.SetSearchSource(provenance, _searchOpeningUnavailable);
    }

    internal void OpenSavedSearchResult(PersistedSearchResult result)
    {
        if (result.Context is not { } context) throw new InvalidOperationException("SeedLibrary.ContextMissing");
        OpenPredictionContext(result.Seed, context);
        InstallResultOrigin(SeedLibraryResultProvenance.From(result, result.SourceAssociation), result.SourceAssociation, result.Seed, context);
    }

    private bool _searchOpeningUnavailable;

    private void ApplySearchOpening(SeedPredictionRequest request, SeedPredictionDocument document)
    {
        if (!_searchOpeningPending && (_soloExplicitOpening || _librarySoloPremise.OpeningRoute is null)) return;
        _searchOpeningPending = false;
        _searchOpeningSeed = document.CanonicalSeed;
        if (_searchOpeningQuery.OpeningRoute is null) return;
        var selected = ResolveSearchOpening(document, _searchOpeningQuery);
        // This selects a real predicted opening, not a witness that the Query matched.
        // If the requested opening is absent, retain the ordinary view with an explicit notice.
        _searchOpeningUnavailable = !selected.Available;
        RefreshResultOrigin();
        if (selected.Available)
            _page.SetPredictorContext(request.Character.CharacterKey, request.Ascension, 1, 0,
                request.AncientOptionConditions, selected.Route, selected.ChoiceSlot, "", notify: false);
    }

    internal static (int? ChoiceSlot, string Route, bool Available) ResolveSearchOpening(SeedPredictionDocument document, SearchQuery query)
    {
        if (query.OpeningRoute is not { } opening) return (null, "", false);
        var choice = document.Sections.SelectMany(s => s.NeowChoices).FirstOrDefault(c => c.RelicKey == opening.RouteRelicKey);
        string route = "";
        bool unavailable = choice is null;
        if (choice is not null && query.OpeningRouteRelicRequirement is { IsEmpty: false } required)
        {
            var actual = choice.BonesOutcome?.OriginalRoutes.FirstOrDefault(r =>
                required.OrderMode == BonesRouteOrderMode.ExactOrder
                    ? r.AcquisitionOrder.SequenceEqual(required.RequiredRelicKeys)
                    : required.RequiredRelicKeys.All(k => r.AcquisitionOrder.Contains(k)));
            unavailable = actual is null;
            route = actual?.OpeningRewardContinuation?.Route.RouteId ?? actual?.RouteId ?? "";
        }
        return (choice?.SlotIndex, route, !unavailable);
    }
}
