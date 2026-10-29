using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Workspace;

internal static class SearchResultProjection
{
    internal static PersistedSearchResult FromCandidate(SearchCandidate candidate, string queryFingerprint)
    {
        var route = candidate.PrimaryWitness is { OpeningRouteId.Length: > 0 } primary
            ? primary : candidate.Witnesses.FirstOrDefault(witness => !string.IsNullOrWhiteSpace(witness.OpeningRouteId));
        return new()
        {
            Seed = candidate.Seed, CharacterKey = candidate.CharacterKey.Serialized, Ascension = candidate.Ascension,
            QueryFingerprint = queryFingerprint, Party = candidate.Document?.Party, IsUnverified = candidate.IsUnverified,
            IsUnfiltered = candidate.IsUnverified && candidate.IsUnfiltered, ValidationDisposition = candidate.ValidationDisposition,
            ValidationIssue = candidate.ValidationIssue, SupportScope = candidate.SupportScope, MapSearch = candidate.MapSearch,
            ExecutionWarnings = candidate.ExecutionWarnings.ToArray(), WitnessOpeningRouteId = route?.OpeningRouteId ?? "",
            TreasureSources = TreasureSources(candidate), MapRouteWitnesses = MapRouteWitnessProjection.FromCandidate(candidate)
        };
    }

    internal static IReadOnlyList<PersistedTreasureSource> TreasureSources(SearchCandidate candidate)
    {
        if (candidate.ValidationDisposition != SearchDisposition.Match) return [];
        var witness = candidate.PrimaryWitness is { OpeningRouteId.Length: > 0 } primary
            ? primary : candidate.Witnesses.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.OpeningRouteId)) ?? candidate.PrimaryWitness;
        if (witness is null) return [];
        var sources = new List<PersistedTreasureSource>();
        foreach (var evidence in candidate.MatchEvidence)
        {
            bool early = evidence.Code == "TreasureRelicObtainedAtOpening";
            if (!early && evidence.Code != "TreasureRelicAtEncounter") continue;
            int slot = evidence.ConditionId switch { "TreasureRelics.Slot1" => 1, "TreasureRelics.Slot2" => 2, _ => 0 };
            if (slot == 0 || evidence.RelatedKey is not { IsValid: true } relic ||
                relic.Category != BaseGameModelKeys.Categories.Relic ||
                !witness.MatchedConditions.Contains(evidence.ConditionId!, StringComparer.Ordinal) ||
                (string.IsNullOrEmpty(evidence.RouteId)
                    ? !string.IsNullOrEmpty(evidence.OutcomeId)
                    : evidence.RouteId != witness.OpeningRouteId || (evidence.OutcomeId ?? "") != witness.OutcomeId) ||
                (early ? evidence.Ordinal is not null : evidence.Ordinal is not (>= 1 and <= 2))) continue;
            sources.Add(new(relic.Serialized, slot, evidence.Ordinal, early));
        }
        // Conflicting receipts are not a source for one selected target slot.
        return sources.Distinct().GroupBy(source => source.TargetSlot).Where(group => group.Count() == 1)
            .Select(group => group.Single()).OrderBy(source => source.TargetSlot).ToArray();
    }

    internal static ExactSearchExecutionRequest RebuildSavedValidationPlan(ModRuntimeSnapshot runtime,
        QueryHistoryEntry history, PersistedSearchResult result)
    {
        var draft = history.Draft ?? throw new InvalidOperationException("ManualValidationOriginalContextUnavailable");
        var context = result.Context ?? history.Context ?? throw new InvalidOperationException("ManualValidationOriginalContextUnavailable");
        if (context.Profile != runtime.Profile.ProfileId || context.GameVersion != runtime.Detection.NormalizedVersion ||
            context.Mode != draft.Mode || context.Ascension != draft.Ascension ||
            QueryHistoryIdentity.Key(draft) != history.QueryKey ||
            (draft.Players.Count == 0 ? context.Players.Count != 1 || context.Players[0].Character != draft.Character :
                context.Players.Count != draft.Players.Count || draft.Players.Where((player, slot) =>
                    player.Slot != slot || context.Players[slot].Slot != slot || context.Players[slot].Character != player.Character).Any()))
            throw new InvalidOperationException("ManualValidationOriginalContextMismatch");
        // Bind the saved roster and unlocks. Current editors never participate.
        // Solo compilation captures local authority; the semantic fingerprint
        // below must prove that it still equals the original saved authority.
        var bound = draft with
        {
            Version = draft.Players.Count > 0 ? 4 : 3,
            GameVersion = context.GameVersion, Profile = context.Profile,
            Players = draft.Players.Select(player => player with
                { Unlocks = LobbyUnlockReadout.Copy(context.Players[player.Slot].Unlocks),
                    UnlockSource = context.Players[player.Slot].UnlockSource }).ToArray()
        };
        var compiled = bound.Compile(runtime, out _);
        if (!SearchWorkspace.ProvesManualValidationSource(history, result, compiled))
            throw new InvalidOperationException("ManualValidationOriginalAuthorityMismatch");
        var execution = ExactSearchExecutionRequestFactory.Compile(compiled, new(result.Seed, 1, 1, 1, SkipExactValidation: false));
        return execution.Success && execution.Plan is { } plan ? plan : throw new InvalidOperationException(execution.Issue);
    }
}
