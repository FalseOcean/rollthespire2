using System.IO.Compression;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

internal static partial class PartyPhase1Checks
{
    private static int CheckPairedMapProbability(SearchContext context)
    {
        var signatures = new ulong[3][];
        for (int act = 0; act < 3; act++)
        {
            string law = new[] { "Overgrowth", "Hive", "Glory" }[act] + (context.Ascension > 0 ? "-swarming" : "-a0");
            using var stream = typeof(StandardMapProbabilityEstimator).Assembly.GetManifestResourceStream(
                "RolltheSpire2.MapEvidence.MP-" + law + ".signatures.gz")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            _ = reader.ReadInt32(); _ = reader.ReadBoolean(); _ = reader.ReadString(); _ = reader.ReadUInt64();
            signatures[act] = new ulong[reader.ReadInt32()];
            for (int i = 0; i < signatures[act].Length; i++)
            { signatures[act][i] = reader.ReadUInt64(); _ = reader.ReadByte(); }
        }
        int Value(int act, int ordinal) => (int)(signatures[act][ordinal] >> 28 & 15); // Final reachable max Elite.
        int Cut(int act) => Enumerable.Range(0, signatures[act].Length).Select(i => Value(act, i)).Order().ElementAt(signatures[act].Length / 2);
        var first = new StandardMapSearchCondition(1, StandardMapMetric.ReachableMaxElite, StandardMapComparison.AtLeast, Cut(0));
        var sameAct = first with { Comparison = StandardMapComparison.AtMost, Value = first.Value + 1 };
        var secondAct = first with { Scope = 2, Value = Cut(1) };
        JointSelectivityResult Estimate(params SearchQuery[] seats)
        {
            var query = SearchQuery.Empty with { Players = Enumerable.Range(0, 2).Select(slot =>
                new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) { Conditions = slot < seats.Length ? seats[slot] : SearchQuery.Empty }).ToArray() };
            return JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(SearchCompiler.Compile(query, context)));
        }
        SearchQuery Query(params StandardMapSearchCondition[] conditions) => SearchQuery.Empty with { StandardMaps = conditions };
        int checks = 0;
        void Equal(JointSelectivityResult result, Func<int, bool> predicate, string label)
        {
            double expected = Enumerable.Range(0, signatures[0].Length).Count(predicate) / (double)signatures[0].Length;
            if (expected == 0 ? result.Probability is not null || result.ExactlyImpossible :
                result.Probability is not { } p || Math.Abs(p - expected) > 1e-12)
                throw new InvalidDataException("Party paired map probability: " + label + ";expected=" + expected + ";actual=" + result.Probability);
            checks++;
        }
        Equal(Estimate(Query(first), Query(first)), i => Value(0, i) >= first.Value, "duplicate shared query counted once");
        Equal(Estimate(Query(first, sameAct)), i => Value(0, i) >= first.Value && Value(0, i) <= sameAct.Value,
            "same Act predicates share the same Final signature");
        Equal(Estimate(Query(first), Query(secondAct)), i => Value(0, i) >= first.Value && Value(1, i) >= secondAct.Value,
            "cross Act predicates use paired roots");
        int beyondObserved = Enumerable.Range(0, signatures[0].Length).Max(i => Value(0, i)) + 1;
        Equal(Estimate(Query(first with { Value = beyondObserved })), _ => false, "zero empirical hits remain Unknown");
        return checks;
    }
}
