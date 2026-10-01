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

    internal void ImportSearchOpening(SearchCandidate candidate, WorkbenchSearchDraft? draft, SeedLibraryContext? context)
    {
        _searchOpeningSeed = candidate.Seed;
        _searchOpeningQuery = draft?.Query ?? SearchQuery.Empty;
        _searchOpeningPending = candidate.IsUnverified;
        if (context is { Players.Count: 1 })
        {
            _librarySoloUnlocks = LobbyUnlockReadout.Copy(context.Players[0].Unlocks);
            _librarySoloUnlockSource = context.Players[0].UnlockSource;
        }
        _page.SetSearchOrigin(candidate.Seed, candidate.IsUnverified, candidate.IsUnverified && draft is null);
    }

    private void ApplySearchOpening(SeedPredictionRequest request, SeedPredictionDocument document)
    {
        if (!_searchOpeningPending || document.CanonicalSeed != _searchOpeningSeed) return;
        _searchOpeningPending = false;
        if (_searchOpeningQuery.OpeningRoute is null) return;
        var selected = ResolveSearchOpening(document, _searchOpeningQuery);
        // This selects a real predicted opening, not a witness that the Query matched.
        // If the requested opening is absent, retain the ordinary view with an explicit notice.
        _page.SetSearchOrigin(_searchOpeningSeed, true, !selected.Available);
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
