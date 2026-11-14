namespace RolltheSpire2.Ui.Shell;

internal enum MapMetricScope { WholeRun, Act1, Act2, Act3 }
internal enum MapMetricKind
{
    GuaranteedMonster,
    GuaranteedElite,
    GuaranteedRest,
    GuaranteedUnknown,
    ReachableMaxMonster,
    ReachableMaxElite,
    ReachableMaxRest,
    ReachableMaxUnknown,
    ForcedMonsterPrefix
}
internal enum MapMetricComparison { AtLeast, AtMost }

internal sealed record MapMetricOption(bool IsAny, MapMetricComparison Comparison, int Value)
{
    public static readonly MapMetricOption Any = new(true, MapMetricComparison.AtLeast, 0);
}

internal sealed record MapMetricDescriptor(
    bool Multiplayer,
    bool Swarming,
    MapMetricScope Scope,
    MapMetricKind Metric,
    IReadOnlyList<MapMetricOption> Options);

internal static class MapMetricCapabilityCatalog
{
    private static readonly MapMetricKind[] MetricOrder =
    [
        MapMetricKind.GuaranteedMonster, MapMetricKind.GuaranteedElite, MapMetricKind.GuaranteedRest,
        MapMetricKind.GuaranteedUnknown, MapMetricKind.ReachableMaxMonster, MapMetricKind.ReachableMaxElite,
        MapMetricKind.ReachableMaxRest, MapMetricKind.ReachableMaxUnknown, MapMetricKind.ForcedMonsterPrefix
    ];

    private sealed record ObservedProfile(bool Multiplayer, bool Swarming, MapMetricScope Scope, int[] Bounds);
    private sealed record ObservedBound(bool Multiplayer, bool Swarming, MapMetricScope Scope,
        MapMetricKind Metric, int Minimum, int Maximum);

    // Map长运行汇总-20260916.xlsx, SP-MP极值. Each profile preserves its A0/Swarming law.
    private static readonly ObservedProfile[] Profiles =
    [
        P(false, false, 1, 1,8, 0,3, 1,4, 0,6, 1,13, 0,5, 1,5, 2,12, 1,6),
        P(false, true,  1, 1,8, 0,4, 1,4, 0,7, 1,13, 0,5, 1,5, 2,12, 1,6),
        P(false, false, 2, 1,7, 0,3, 1,4, 0,6, 1,12, 0,4, 1,4, 2,11, 1,6),
        P(false, true,  2, 1,7, 0,4, 1,4, 0,6, 1,12, 0,4, 1,4, 2,11, 1,6),
        P(false, false, 3, 1,7, 0,3, 1,3, 0,6, 1,11, 0,4, 1,4, 2,10, 1,5),
        P(false, true,  3, 1,6, 0,3, 1,3, 0,6, 1,11, 0,4, 1,4, 2,10, 1,5),

        P(true, false, 1, 1,7, 0,3, 1,4, 0,6, 1,12, 0,4, 1,4, 2,11, 1,6),
        P(true, true,  1, 1,6, 0,3, 1,4, 0,6, 1,12, 0,4, 1,4, 2,11, 1,5),
        P(true, false, 2, 1,7, 0,2, 1,3, 0,5, 1,11, 0,4, 1,4, 2,10, 1,5),
        P(true, true,  2, 1,6, 0,3, 1,3, 0,5, 1,11, 0,4, 1,4, 2,10, 1,5),
        P(true, false, 3, 1,6, 0,2, 1,3, 0,6, 1,10, 0,3, 1,3, 2,9, 1,5),
        P(true, true,  3, 1,5, 0,3, 1,3, 0,6, 1,10, 0,3, 1,3, 2,9, 1,5)
    ];

    private static readonly MapMetricDescriptor[] Descriptors = BuildDescriptors();

    public static MapMetricDescriptor? Find(int players, int ascension, int scope, MapMetricKind metric) =>
        Descriptors.FirstOrDefault(value => value.Multiplayer == (players > 1) &&
            value.Swarming == (ascension >= 1) && value.Scope == (MapMetricScope)scope && value.Metric == metric);

    private static ObservedProfile P(bool multiplayer, bool swarming, int act, params int[] bounds) =>
        new(multiplayer, swarming, (MapMetricScope)act, bounds);

    private static MapMetricDescriptor[] BuildDescriptors()
    {
        ObservedBound[] observed = Profiles.SelectMany(profile => MetricOrder.Select((metric, index) =>
            new ObservedBound(profile.Multiplayer, profile.Swarming, profile.Scope, metric,
                profile.Bounds[index * 2], profile.Bounds[index * 2 + 1]))).ToArray();
        var result = observed.Select(bound => new MapMetricDescriptor(bound.Multiplayer, bound.Swarming,
            bound.Scope, bound.Metric, BuildOptions(bound.Minimum, bound.Maximum, includeAtMost: true))).ToList();

        foreach (bool multiplayer in new[] { false, true })
        foreach (bool swarming in new[] { false, true })
        foreach (MapMetricKind metric in new[]
                 {
                     MapMetricKind.ReachableMaxElite,
                     MapMetricKind.ReachableMaxRest,
                     MapMetricKind.ReachableMaxUnknown
                 })
        {
            ObservedBound[] acts = observed.Where(value => value.Multiplayer == multiplayer &&
                value.Swarming == swarming && value.Metric == metric).ToArray();
            int aggregateMaximum = acts.Sum(value => value.Maximum);
            result.Add(new MapMetricDescriptor(multiplayer, swarming, MapMetricScope.WholeRun, metric,
                BuildOptions(0, aggregateMaximum, includeAtMost: false)));
        }
        return result.ToArray();
    }

    private static MapMetricOption[] BuildOptions(int minimum, int maximum, bool includeAtMost)
    {
        var options = new List<MapMetricOption> { MapMetricOption.Any };
        for (int value = 1; value <= maximum; value++)
            options.Add(new MapMetricOption(false, MapMetricComparison.AtLeast, value));
        if (includeAtMost)
            for (int value = minimum; value < maximum; value++)
                options.Add(new MapMetricOption(false, MapMetricComparison.AtMost, value));
        return options.ToArray();
    }
}
