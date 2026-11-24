using System.Text.Json.Serialization;

namespace RolltheSpire2.Search.Semantics;

public enum ElitePrefixResearchMode { FixedCounts = 0, FloorSeven = 1 }

/// <summary>
/// Act 1 universal opening-path predicate. FixedCounts preserves previously
/// saved requests; FloorSeven requires the first Elite on floor 7 and only
/// Monster/Unknown icons before it, with unrestricted counts on each path.
/// Unknowns refer to map icons, not their eventual room outcomes.
/// </summary>
public sealed record ElitePrefixResearchCondition(int ExtraMonsters, int Unknowns)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ElitePrefixResearchMode Mode { get; init; }

    public static ElitePrefixResearchCondition FloorSeven { get; } = new(0, 0) { Mode = ElitePrefixResearchMode.FloorSeven };

    public bool IsValid => ExtraMonsters >= 0 && Unknowns >= 0 &&
        Mode is ElitePrefixResearchMode.FixedCounts or ElitePrefixResearchMode.FloorSeven;
}
