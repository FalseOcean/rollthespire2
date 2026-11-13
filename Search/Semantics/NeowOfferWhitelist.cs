using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

// Author intent is the allowed set, never a persisted complement of today's catalog.
public sealed record NeowOfferWhitelistSearchCondition(IReadOnlyList<ModelKey> AllowedKeys);

internal static class NeowOfferWhitelist
{
    internal static bool Matches(NeowOfferWhitelistSearchCondition? condition, IEnumerable<ModelKey> offers) =>
        condition is null || offers.All(condition.AllowedKeys.Contains);

    internal static bool Matches(NeowOfferWhitelistSearchCondition? condition, ModelKey first, ModelKey second, ModelKey third) =>
        condition is null || condition.AllowedKeys.Contains(first) && condition.AllowedKeys.Contains(second) && condition.AllowedKeys.Contains(third);

    internal static bool TryProject(NeowOfferWhitelistSearchCondition? condition,
        RuntimeContextAuthoritySnapshot authority, ModelKeySetFilter original, out ModelKeySetFilter projected)
    {
        projected = original;
        if (condition is null) return true;
        if (!ModernNeowIdentityRules.TryGetEligibleCursePool(authority, out var curses)) return false;
        var preparation = ModernNeowIdentityRules.Prepare(authority.PlayersCount,
            authority.AllCharacterCardPoolsUnlocked, authority.IsScrollBoxesAllowed);
        var universe = curses.Concat(curses.SelectMany(curse =>
        {
            var pool = preparation.ForCurse(curse);
            return pool.FixedCandidates.Concat(pool.AppendChoices.SelectMany(choice =>
                new[] { choice.TrueCandidate, choice.FalseCandidate }));
        })).Distinct();
        projected = original with { Ban = original.Ban.Concat(universe.Where(key =>
            !condition.AllowedKeys.Contains(key))).Distinct().ToArray() };
        return true;
    }
}
