using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Persistence;

// Only authored context and legal opening selections cross the bookmark boundary.
// Prediction documents, witnesses, runtime pools and search conditions stay transient.
internal static class SeedLibraryContextCapture
{
    internal static SearchQuery OpeningPremise(SearchQuery query) => SearchQuery.Empty with
    {
        OpeningRoute = query.OpeningRoute,
        OpeningRouteRelicRequirement = query.OpeningRouteRelicRequirement,
        StructuredOpeningEffects = query.StructuredOpeningEffects
    };

    internal static SeedLibraryContext ForSearch(WorkbenchSearchDraft draft, ModRuntimeSnapshot runtime)
    {
        var players = draft.Mode == WorldGameMode.Multiplayer
            ? draft.Players.Select(p => new SeedLibraryPlayer(p.Slot, p.Character, LobbyUnlockReadout.Copy(p.Unlocks),
                p.UnlockSource, draft.Query.Players[p.Slot].AncientPremises,
                OpeningPremise(draft.Query.Players[p.Slot].Conditions), null)).ToArray()
            : [new SeedLibraryPlayer(0, draft.Character, SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable(),
                "CapturedLocalProfile", draft.AncientPremises, SearchQuery.Empty, null)];
        return new(runtime.Detection.NormalizedVersion, runtime.Profile.ProfileId, draft.Mode, draft.Ascension, players);
    }

    internal static SeedLibraryContext WithWitness(SeedLibraryContext context, SearchCandidate candidate)
    {
        if (context.Mode == WorldGameMode.Multiplayer)
        {
            var party = candidate.Document.Party ?? throw new InvalidOperationException("SeedLibrary.PartyWitnessMissing");
            if (party.Players.Count != context.Players.Count || party.Ascension != context.Ascension || party.Version != context.GameVersion ||
                party.Players.Where((p, i) => p.Slot != context.Players[i].Slot || p.Character != context.Players[i].Character ||
                    p.UnlockSource != context.Players[i].UnlockSource).Any())
                throw new InvalidOperationException("SeedLibrary.PartyWitnessMismatch");
            return context with { Players = context.Players.Select(p =>
            {
                var transaction = party.Transactions.FirstOrDefault(t => t.Slot == p.Slot);
                return p with { Selection = transaction is null ? null : new(
                    party.Players[p.Slot].Offers.ToList().IndexOf(transaction.Option) + 1, transaction.OpeningRouteId,
                    p.OpeningPremise.OpeningRoute is not null) };
            }).ToArray() };
        }
        string route = candidate.Witnesses.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.OpeningRouteId))?.OpeningRouteId ?? "";
        int? choice = null;
        const string prefix = "choice.";
        if (route.StartsWith(prefix, StringComparison.Ordinal))
        {
            int end = route.IndexOf('.', prefix.Length);
            if (end > prefix.Length && int.TryParse(route[prefix.Length..end], out int parsed))
            { choice = parsed; route = route[(end + 1)..]; }
        }
        return context with { Players = [context.Players[0] with { Selection =
            choice.HasValue || route.Length > 0 ? new(choice, route, true) : null }] };
    }
}
