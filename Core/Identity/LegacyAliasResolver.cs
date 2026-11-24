namespace RolltheSpire2.Core.Identity;

public enum AliasResolutionStatus
{
    Resolved,
    Unknown,
    Ambiguous
}

public readonly record struct AliasResolutionResult(
    AliasResolutionStatus Status,
    ModelKey Key)
{
    public bool IsResolved => Status == AliasResolutionStatus.Resolved && Key.IsValid;
}

/// <summary>
/// Compatibility-only parser for old fixture/config/user forms. Predictor, Snapshot,
/// Document, Formatter, and filter code must receive the resolved ModelKey and must not
/// call this resolver again.
/// </summary>
public static class LegacyAliasResolver
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<ModelKey>> RelicAliases =
        BuildAliases(BaseGameModelKeys.Relics.AllNeow);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<ModelKey>> CharacterAliases =
        BuildAliases(BaseGameModelKeys.Characters.All);

    public static AliasResolutionResult ResolveRelic(string? input) =>
        Resolve(input, BaseGameModelKeys.Categories.Relic, RelicAliases);

    public static AliasResolutionResult ResolveCharacter(string? input) =>
        Resolve(input, BaseGameModelKeys.Categories.Character, CharacterAliases);

    private static AliasResolutionResult Resolve(
        string? input,
        string expectedCategory,
        IReadOnlyDictionary<string, IReadOnlyList<ModelKey>> aliases)
    {
        string value = input?.Trim() ?? string.Empty;
        if (ModelKey.TryParseExact(value, out ModelKey exact))
        {
            return string.Equals(exact.Category, expectedCategory, StringComparison.Ordinal)
                ? new AliasResolutionResult(AliasResolutionStatus.Resolved, exact)
                : new AliasResolutionResult(AliasResolutionStatus.Unknown, default);
        }

        if (!aliases.TryGetValue(value, out IReadOnlyList<ModelKey>? candidates) || candidates.Count == 0)
        {
            return new AliasResolutionResult(AliasResolutionStatus.Unknown, default);
        }

        return candidates.Count == 1
            ? new AliasResolutionResult(AliasResolutionStatus.Resolved, candidates[0])
            : new AliasResolutionResult(AliasResolutionStatus.Ambiguous, default);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ModelKey>> BuildAliases(IEnumerable<ModelKey> keys)
    {
        var map = new Dictionary<string, HashSet<ModelKey>>(StringComparer.OrdinalIgnoreCase);
        foreach (ModelKey key in keys)
        {
            Register(map, key.Serialized, key);
            Register(map, key.Entry, key);
            Register(map, key.Entry.ToLowerInvariant(), key);
            Register(map, key.Entry.Replace("_", string.Empty, StringComparison.Ordinal), key);
            Register(map, ToDisplayAlias(key.Entry), key);
        }

        return map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ModelKey>)pair.Value.OrderBy(value => value.Serialized, StringComparer.Ordinal).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void Register(Dictionary<string, HashSet<ModelKey>> map, string alias, ModelKey key)
    {
        if (!map.TryGetValue(alias, out HashSet<ModelKey>? values))
        {
            values = new HashSet<ModelKey>(ModelKeyComparer.Instance);
            map[alias] = values;
        }
        values.Add(key);
    }

    private static string ToDisplayAlias(string entry) => string.Join(
        " ",
        entry.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length == 0
                ? word
                : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
}
