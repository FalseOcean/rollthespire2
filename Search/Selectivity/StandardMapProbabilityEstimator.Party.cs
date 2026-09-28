using System.IO.Compression;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class StandardMapProbabilityEstimator
{
    private sealed record PairedMapSample(ulong Begin, ulong[][] Acts);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<bool, PairedMapSample> PartySamples = new();

    internal static JointSelectivityResult EstimateParty(SearchSelectivityInput input)
    {
        var conditions = input.CompiledSearch.NormalizedQuery.StandardMaps;
        if (conditions.Count == 0) return JointSelectivityResult.Exact(1,
            JointSelectivityCombinationMethod.SingleAuthorityTerm, "Probability.StandardMap.Party.Empty", "No map predicates.", "1");
        if (input.ProfileId != RuntimeProfileId.Beta111 || input.CompiledSearch.Context.Detection.NormalizedVersion != "0.111.0" ||
            input.Authority.PlayersCount is < 2 or > 4 || !input.Authority.IsVanilla)
            return JointSelectivityResult.Unpriced("Probability.StandardMap.Party.CorpusContextMismatch",
                "Paired corpus requires Beta111 vanilla standard multiplayer maps.", "No single-player corpus substitution.");

        var sample = PartySamples.GetOrAdd(input.Ascension > 0, LoadPartySample);
        int count = sample.Acts[0].Length, hits = 0;
        // All conditions are evaluated on the same ordinal's three Final signatures.
        // In particular, local maxima and total objectives are not independent factors.
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            bool matches = true;
            foreach (var condition in conditions)
            {
                int dimension = FinalDimension(condition.Metric);
                int value = condition.Scope == 0
                    ? sample.Acts.Sum(act => (int)(act[ordinal] >> (4 * dimension) & 15))
                    : (int)(sample.Acts[condition.Scope - 1][ordinal] >> (4 * dimension) & 15);
                if (condition.Comparison == StandardMapComparison.AtLeast ? value < condition.Value : value > condition.Value)
                { matches = false; break; }
            }
            if (matches) hits++;
        }
        string evidence = $"PartyMap.PairedFinal.20260914;begin={sample.Begin};samples={count};hits={hits};swarming={input.Ascension > 0}";
        string[] assumptions = [evidence, "ProspectiveContiguousCanonicalOrdinals;NoOutcomeSelection;NotUnbiasedGlobalSample",
            "AllMapPredicates=SameOrdinalJointCount;SharedMapPaidOnce", "MapAndOtherFamilies=AssumedIndependent",
            "EmpiricalFrequencyNotFiniteSeedProof;ZeroHitsNotImpossibility", "StandardPreHookMap;MP2To4ShareLaw"];
        if (hits == 0) return JointSelectivityResult.Unpriced("Probability.StandardMap.Party.NoObservedHits",
            evidence, "No observed conjunction in the paired corpus; no zero-probability claim.",
            ["M.StandardMap:NoObservedHits"], assumptions: assumptions);
        double probability = (double)hits / count;
        var component = new JointSelectivityComponent("M.StandardMap", "Shared map query", "QueryRoot", probability,
            evidence, JointSelectivityCombinationMethod.SingleAuthorityTerm, SearchSelectivityDependencyClass.StructuralDependence,
            true, true, "All same-Act and cross-Act conditions share one paired sample.", assumptions);
        return JointSelectivityResult.Exact(probability, JointSelectivityCombinationMethod.SingleAuthorityTerm,
            "Probability.StandardMap.Party.PairedEmpirical", evidence, "Joint hits / paired roots; counted once for the party.",
            components: [component], assumptions: assumptions) with { Confidence = SearchSelectivityConfidence.Low, ExactlyImpossible = false };
    }

    private static int FinalDimension(StandardMapMetric metric) => metric switch
    {
        StandardMapMetric.GuaranteedMonster => 0, StandardMapMetric.GuaranteedElite => 1,
        StandardMapMetric.GuaranteedRest => 2, StandardMapMetric.GuaranteedUnknown => 4,
        StandardMapMetric.ForcedMonsterPrefix => 5, StandardMapMetric.ReachableMaxMonster => 6,
        StandardMapMetric.ReachableMaxElite => 7, StandardMapMetric.ReachableMaxRest => 8,
        StandardMapMetric.ReachableMaxUnknown => 10, _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };

    private static PairedMapSample LoadPartySample(bool swarming)
    {
        var signatures = new ulong[3][]; ulong? begin = null; int? count = null;
        for (int act = 0; act < 3; act++)
        {
            string law = new[] { "Overgrowth", "Hive", "Glory" }[act] + (swarming ? "-swarming" : "-a0");
            using var source = typeof(StandardMapProbabilityEstimator).Assembly.GetManifestResourceStream(
                "RolltheSpire2.MapEvidence.MP-" + law + ".signatures.gz") ?? throw new InvalidDataException("PartyMapCorpusMissing:" + law);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            if (reader.ReadInt32() != 0x4d505331 || !reader.ReadBoolean() || reader.ReadString() != law)
                throw new InvalidDataException("PartyMapCorpusContext:" + law);
            ulong start = reader.ReadUInt64(); int n = reader.ReadInt32();
            if (n != 65536 || start != 1146000000000UL || begin.HasValue && (begin != start || count != n))
                throw new InvalidDataException("PartyMapCorpusPairing:" + law);
            begin = start; count = n; signatures[act] = new ulong[n];
            for (int i = 0; i < n; i++)
            {
                ulong final = reader.ReadUInt64();
                if (final >> 44 != 0) throw new InvalidDataException("PartyMapCorpusSignature:" + law);
                signatures[act][i] = final;
                _ = reader.ReadByte(); // Historical lossy FastMask is not probability evidence.
            }
            if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("PartyMapCorpusTrailingBytes:" + law);
        }
        return new(begin!.Value, signatures);
    }
}
