using System.IO.Compression;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

// A fixed, versioned unbiased corpus, never runtime search survivor learning.
internal static partial class StandardMapProbabilityEstimator
{
    private sealed record Histogram(ulong Count, Dictionary<uint, ulong>[] Families);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Histogram> Cache = new();
    internal static JointSelectivityResult Compose(SearchSelectivityInput input, Func<SearchSelectivityInput, JointSelectivityResult> estimate)
    {
        var query = input.CompiledSearch.NormalizedQuery;
        var baseline = estimate(SearchSelectivityInput.From(SearchCompiler.Compile(query with { StandardMaps = [] }, input.CompiledSearch.Context)));
        if (baseline.ExactlyImpossible) return baseline;
        JointSelectivityResult Unknown(string why) => JointSelectivityResult.Partial("Probability.StandardMap." + why,
            "The available empirical joint projection cannot price this map query.", "No product of same-map marginals.",
            baseline.KnownComponents, baseline.UnknownComponents.Append("M.StandardMap:" + why).ToArray(), assumptions: baseline.Assumptions);
        if (input.ProfileId != RuntimeProfileId.Beta111 || input.CompiledSearch.Context.Detection.NormalizedVersion != "0.111.0" || input.Authority.PlayersCount != 1 || !input.Authority.IsVanilla)
            return Unknown("CorpusContextMismatch");
        var totals = query.StandardMaps.Where(c => c.Scope == 0).ToArray();
        var totalDimensions = totals.Select(c => MaximumDimension(c.Metric)).Distinct().ToArray();
        if (totalDimensions.Length > 1 || totalDimensions.Any(d => d < 0)) return Unknown("MultipleTotalMetricsJointMissing");
        var acts = totals.Length == 0 ? query.StandardMaps.Select(c => c.Scope).Distinct().ToArray() : new[] {1,2,3};
        var evidence = new List<string>(); double mapProbability = 1;
        var sumDistribution = new Dictionary<int,double> { [0] = 1 };
        foreach (int act in acts)
        {
            var local = query.StandardMaps.Where(c => c.Scope == act).ToArray();
            var maxima = local.Select(c => MaximumDimension(c.Metric)).Where(d => d >= 0).Concat(totalDimensions).Distinct().ToArray();
            if (maxima.Length > 1) return Unknown("MultipleMaximaJointMissing");
            int family = maxima.Length == 0 ? 0 : maxima[0] + 1;
            string name = (act switch { 1 => "Overgrowth", 2 => "Hive", 3 => "Glory", _ => throw new ArgumentException("MapAct") }) +
                (input.Ascension == 0 ? "-A0" : "-A1plus");
            var histogram = Cache.GetOrAdd(name, Load);
            ulong hits = 0; var distribution = new Dictionary<int,double>();
            foreach (var (key, count) in histogram.Families[family])
            {
                if (!local.All(c => c.Comparison == StandardMapComparison.AtLeast ? Value(key, c.Metric) >= c.Value : Value(key, c.Metric) <= c.Value)) continue;
                hits = checked(hits + count);
                int maximum = family == 0 ? 0 : (int)(key >> 24 & 15);
                distribution[maximum] = distribution.GetValueOrDefault(maximum) + (double)count/histogram.Count;
            }
            evidence.Add($"{name}:localHits={hits};samples={histogram.Count};Final;Phase5.UnbiasedCanonical;Beta111");
            if (hits == 0) return Unknown("NoObservedHits:" + string.Join("|", evidence));
            mapProbability *= (double)hits / histogram.Count;
            if (totals.Length > 0)
            {
                var next = new Dictionary<int,double>();
                foreach(var (sum, sumProbability) in sumDistribution) foreach(var (value,q) in distribution)
                    next[sum+value] = next.GetValueOrDefault(sum+value) + sumProbability*q;
                sumDistribution = next;
            }
        }
        if (totals.Length > 0)
        {
            mapProbability = sumDistribution.Where(x => totals.All(c => c.Comparison == StandardMapComparison.AtLeast ? x.Key >= c.Value : x.Key <= c.Value)).Sum(x => x.Value);
            if(mapProbability == 0) return Unknown("NoObservedSumSupport");
            evidence.Add("TotalMap=ConvolutionOfActHistogramsUnderAssumedIndependence;NotPairedSeedJointData");
        }
        var assumptions = baseline.Assumptions.Concat(evidence).Concat(new[] {
            "MapProbability=EmpiricalNotAnalytical", "SameActConditions=JointHistogram",
            "CrossActAndOtherFamilies=AssumedIndependent;NotMeasuredJoint", "NoZeroCountImpossibilityProof" }).ToArray();
        var component = new JointSelectivityComponent("M.StandardMap", "Map query", "QueryRoot", mapProbability,
            "Map.Phase5.FinalEmpirical", JointSelectivityCombinationMethod.IndependentProduct, SearchSelectivityDependencyClass.AssumedIndependent,
            true, true, "Joint empirical count within each Act", assumptions);
        var components = baseline.KnownComponents.Append(component).ToArray();
        if (baseline.Probability is not { } baselineProbability) return JointSelectivityResult.Partial("Probability.StandardMap.RemainderUnknown",
            "Map count known; other predicates unresolved.", "No missing factor substituted.", components, baseline.UnknownComponents, assumptions: assumptions);
        return JointSelectivityResult.Exact(baselineProbability * mapProbability, JointSelectivityCombinationMethod.IndependentProduct,
            "Probability.StandardMap.Empirical", "Versioned empirical map frequency, not a finite-seed proof.",
            "Joint map histograms × other query under stated independence assumption.", components: components, assumptions: assumptions,
            dependencyCoverage: "EmpiricalSameActJoint;ModeledCrossAct") with { Confidence = SearchSelectivityConfidence.Low, ExactlyImpossible = baseline.ExactlyImpossible };
    }
    private static int MaximumDimension(StandardMapMetric metric) => metric switch {
        StandardMapMetric.ReachableMaxMonster => 0, StandardMapMetric.ReachableMaxElite => 1,
        StandardMapMetric.ReachableMaxRest => 2, StandardMapMetric.ReachableMaxUnknown => 4, _ => -1 };
    private static int Value(uint key, StandardMapMetric metric)
    {
        if (MaximumDimension(metric) >= 0) return (int)(key >> 24 & 15);
        int d = metric switch { StandardMapMetric.GuaranteedMonster => 0, StandardMapMetric.GuaranteedElite => 1,
            StandardMapMetric.GuaranteedRest => 2, StandardMapMetric.GuaranteedUnknown => 4, StandardMapMetric.ForcedMonsterPrefix => 5,
            _ => throw new ArgumentException("MapMetric") };
        return (int)(key >> (4*d) & 15);
    }
    private static Histogram Load(string name)
    {
        using var stream = typeof(StandardMapProbabilityEstimator).Assembly.GetManifestResourceStream("RolltheSpire2.MapEvidence." + name + ".hist.gz")
            ?? throw new InvalidDataException("Map probability corpus missing: " + name);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);
        if (reader.ReadUInt32() != 0x4d354831) throw new InvalidDataException("MapCorpusMagic");
        ulong count = reader.ReadUInt64();
        if (count == 0) throw new InvalidDataException("MapCorpusEmpty");
        var families = new Dictionary<uint, ulong>[6];
        for (int i = 0; i < 6; i++)
        {
            int size = reader.ReadInt32(); if (size < 0 || size > 1000000) throw new InvalidDataException("MapCorpusSize");
            families[i] = new(); ulong total = 0;
            for (int j = 0; j < size; j++) { uint key = reader.ReadUInt32(); ulong n = reader.ReadUInt64();
                if (n == 0 || key >= (1U << (i == 0 ? 24 : 28))) throw new InvalidDataException("MapCorpusEntry");
                families[i].Add(key,n); total = checked(total+n); }
            if (total != count) throw new InvalidDataException("MapCorpusCount");
        }
        return new(count, families);
    }
}
