namespace RolltheSpire2.Search.Semantics;

internal static class UnknownRoomPrefixRequirements
{
    internal static int EventCount(SearchQuery query) => Math.Max(query.FirstActUnknownEventPrefixCount,
        query.Players.Select(p => p.Conditions.FirstActUnknownEventPrefixCount).DefaultIfEmpty().Max());
    internal static int MonsterCount(SearchQuery query) => Math.Max(query.FirstActUnknownMonsterPrefixCount,
        query.Players.Select(p => p.Conditions.FirstActUnknownMonsterPrefixCount).DefaultIfEmpty().Max());
    internal static int Count(SearchQuery query) => Math.Max(EventCount(query), MonsterCount(query));
    internal static bool HasConditions(SearchQuery query) => Count(query) > 0;
    internal static bool HasConflict(SearchQuery query) => EventCount(query) > 0 && MonsterCount(query) > 0;
}
