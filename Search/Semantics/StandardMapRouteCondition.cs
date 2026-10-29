using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Search.Semantics;

public enum StandardMapRoomType { Monster, Elite, RestSite, Shop, Unknown }

/// <summary>One unordered multiset. Group order in the containing route remains significant.</summary>
public sealed record StandardMapRouteGroup(IReadOnlyList<StandardMapRoomType> Nodes);

/// <summary>
/// An existential route in a complete Act map. Extra rooms are allowed; groups cannot
/// interleave or reuse rooms. Generated fixed rooms do not pay authored occurrences.
/// </summary>
public sealed record StandardMapRouteCondition(int Act, IReadOnlyList<StandardMapRouteGroup> Groups)
{
    public bool IsValid => Act is >= 1 and <= 3 && Groups is not null &&
        Groups.All(g => g is not null && g.Nodes is not null && g.Nodes.All(t => Enum.IsDefined(t)));

    public bool IsEmpty => Groups.All(g => g.Nodes.Count == 0);

    public StandardMapRouteCondition Normalize() => this with
    {
        Groups = Groups.Select(g => new StandardMapRouteGroup(g.Nodes.OrderBy(t => t).ToArray())).ToArray()
    };

    internal string Fingerprint => $"{Act}:" + string.Join(">", Groups.Select(g =>
        "[" + string.Join(",", g.Nodes.OrderBy(t => t).Select(t => ((int)t).ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]"));

    internal MapRouteSequenceQuery ToSequenceQuery() => MapRouteSequenceQuery.FromGroups(Groups.Select(g =>
        new MapRouteGroup(g.Nodes.Select(t => t switch
        {
            StandardMapRoomType.Monster => MapPointType.Monster,
            StandardMapRoomType.Elite => MapPointType.Elite,
            StandardMapRoomType.RestSite => MapPointType.RestSite,
            StandardMapRoomType.Shop => MapPointType.Shop,
            StandardMapRoomType.Unknown => MapPointType.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(t))
        }))));
}
