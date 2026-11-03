using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

internal static partial class BonesHiddenUpgradeChecks
{
    // Reuses the independent, frozen full-range oracle. No old N/R survivor
    // list selects the roots or defines whether a refinement is safe.
    internal static void CheckSparseRefinement(string manifestPath, string output)
    {
        var manifest = ProductionSearchReplay.Read<Manifest>(manifestPath);
        var projections = new Dictionary<(string Context, ulong Root), IReadOnlyList<BonesAcquisitionRouteResult>>();
        var cases = new List<object>();
        int rejectedTotal = 0, masksTotal = 0, exactRetained = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var maskCoverage = new Dictionary<string, int>(StringComparer.Ordinal);
        var unavailable = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in manifest.Cases)
        {
            var capture = ProductionSearchReplay.Read<ProductionSearchReplayContext>(item.Context);
            var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(item.Workload);
            var request = Compile(capture, workload.Query, workload.Options);
            var plan = NeowReplayPlan.Compile(request.Model, false);
            var refine = NeowCapsuleSparseRefinement.Compile(request.Model, plan);
            var expected = ProductionSearchReplay.Read<List<OracleRow>>(item.Expected).ToDictionary(x => x.Seed, StringComparer.Ordinal);
            var seenExpected = new HashSet<string>(StringComparer.Ordinal);
            string contextKey = JsonSerializer.Serialize(capture.Context, ProductionSearchReplay.JsonOptions);
            int identities = 0, unionPass = 0, primaryPass = 0, rejected = 0, masks = 0;
            int smallMasks = 0, largeMasks = 0, dualMasks = 0;
            var rejectionSeeds = new List<string>();
            for (long offset = 0; offset < item.ScanCount; offset++)
            {
                string seed = Format(item.StartOrdinal + (ulong)offset);
                ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
                var observed = NeowFamilyReplay.Observe(root, plan);
                bool identity = observed.IdentityPass;
                if (!identity)
                {
                    if (expected.ContainsKey(seed)) throw new InvalidDataException("CapsuleSparse.ExpectedIdentityMissing:" + item.Id + ":" + seed);
                    continue;
                }
                identities++;
                bool union = NeowSingleplayerReplay.Matches(root, plan);
                var outcome = refine.Evaluate(root);
                if (outcome.Keep && !union) throw new InvalidDataException("CapsuleSparse.RefinementAddedCandidate:" + item.Id + ":" + seed);
                if (!union)
                {
                    if (expected.ContainsKey(seed)) throw new InvalidDataException("CapsuleSparse.ExpectedUnionMissing:" + item.Id + ":" + seed);
                }
                else
                {
                    unionPass++;
                    bool primary = refine.PrimaryMatches(root);
                    if (primary) primaryPass++;
                    if (primary && !outcome.Keep) throw new InvalidDataException("CapsuleSparse.PrimaryPassRejected:" + item.Id + ":" + seed);
                    reasons[outcome.Reason] = reasons.GetValueOrDefault(outcome.Reason) + 1;
                    if (expected.ContainsKey(seed))
                    {
                        if (!outcome.Keep) throw new InvalidDataException("CapsuleSparse.ExactOutputRejected:" + item.Id + ":" + seed);
                        seenExpected.Add(seed); exactRetained++;
                    }
                    if (!outcome.Keep)
                    {
                        if (outcome.Reason != "Rejected" || outcome.Observations == 0)
                            throw new InvalidDataException("CapsuleSparse.RejectionWithoutObservation:" + item.Id + ":" + seed);
                        var truth = Exact(request, seed);
                        if (truth.Evaluation.Disposition != SearchDisposition.NoMatch)
                            throw new InvalidDataException("CapsuleSparse.UnprovenRejection:" + item.Id + ":" + seed + ":" + truth.Evaluation.Disposition);
                        rejected++; rejectedTotal++; rejectionSeeds.Add(seed);
                    }
                }
                // Check the observer independently even for primary passes.
                // Core owns one shared bag and preserves the two source scopes.
                foreach (var route in NeowSingleplayerReplay.Routes(plan, observed))
                {
                    bool rejectedUnionRoute = union && !outcome.Keep && NeowFamilyReplay.Route(root, plan,
                        route.First, route.Second, observed.RewardsAfterBones);
                    var actual = refine.ObserveRoute(root, route.First, route.Second, observed.RewardsAfterBones);
                    if (!actual.Available)
                    {
                        if (rejectedUnionRoute) throw new InvalidDataException("CapsuleSparse.UnknownObservationRejected:" + item.Id + ":" + seed);
                        unavailable[actual.Issue] = unavailable.GetValueOrDefault(actual.Issue) + 1;
                        continue;
                    }
                    if (rejectedUnionRoute && ((actual.FirstMask | actual.SecondMask) != 0 ||
                        NeowSingleplayerReplay.RouteAtArrival(root, plan, route.First, route.Second, observed.RewardsAfterBones, 0, 0)))
                        throw new InvalidDataException("CapsuleSparse.RejectionLacksZeroProof:" + item.Id + ":" + seed);
                    var key = (contextKey, root);
                    if (!projections.TryGetValue(key, out var originalRoutes))
                    {
                        originalRoutes = new NeowEffectProjectionEngine(Beta111Profile.Instance, "capsule-sparse-independent-mask",
                            root, capture.Context.Authority, true).Project(BaseGameModelKeys.Relics.NeowsBones).BonesOutcome?.OriginalRoutes
                            ?? throw new InvalidDataException("CapsuleSparse.CoreRoutesUnavailable:" + item.Id + ":" + seed);
                        projections.Add(key, originalRoutes);
                    }
                    ModelKey first = Key(route.First), second = Key(route.Second);
                    var coreRoutes = originalRoutes.Where(r => r.AcquisitionOrder.SequenceEqual([first, second])).ToArray();
                    if (coreRoutes.Length == 0) throw new InvalidDataException("CapsuleSparse.CoreOrderUnavailable:" + item.Id + ":" + seed);
                    foreach (var core in coreRoutes)
                    {
                        uint Mask(ModelKey source) => (NestedFrom(core, source).Contains(W) ? 1u : 0u) |
                            (NestedFrom(core, source).Contains(WP) ? 2u : 0u);
                        if (actual.FirstMask != Mask(first) || actual.SecondMask != Mask(second))
                            throw new InvalidDataException("CapsuleSparse.SourceMaskMismatch:" + item.Id + ":" + seed + ":" + core.RouteId);
                    }
                    masks++; masksTotal++;
                    bool hasSmall = first == Small || second == Small, hasLarge = first == Large || second == Large;
                    if (hasSmall) smallMasks++; if (hasLarge) largeMasks++; if (hasSmall && hasLarge) dualMasks++;
                    string maskKey = $"{first.Entry}:{actual.FirstMask}/{second.Entry}:{actual.SecondMask}";
                    maskCoverage[maskKey] = maskCoverage.GetValueOrDefault(maskKey) + 1;
                }
            }
            if (seenExpected.Count != expected.Count)
                throw new InvalidDataException("CapsuleSparse.ExpectedOutputNotVisited:" + item.Id);
            cases.Add(new { item.Id, item.ScanCount, Identities = identities, UnionPass = unionPass, PrimaryPass = primaryPass,
                Rejected = rejected, ExactOutputsRetained = seenExpected.Count, MaskRoutes = masks, SmallMaskRoutes = smallMasks,
                LargeMaskRoutes = largeMasks, DualMaskRoutes = dualMasks, RejectionSeeds = rejectionSeeds, refine.Ready, refine.Issue });
            Console.WriteLine($"CAPSULE_SPARSE_CASE id={item.Id};identity={identities};union={unionPass};primary={primaryPass};rejected={rejected};exactRetained={seenExpected.Count};maskRoutes={masks}");
        }
        CompatibilityChecks.WriteEvidence(Path.Combine(output, "capsule-sparse-checks.json"), new {
            Passed = masksTotal > 0 && rejectedTotal > 0, Oracle = manifestPath, Revision = NeowCapsuleSparseRefinement.Revision,
            Cases = cases, Rejected = rejectedTotal, ExactOutputsRetained = exactRetained, MaskRoutes = masksTotal, Reasons = reasons,
            MaskCoverage = maskCoverage, UnavailableObservations = unavailable, NonvacuousRejection = rejectedTotal > 0 });
        if (masksTotal == 0) throw new InvalidDataException("CapsuleSparse.NoIndependentMaskObservations");
        if (rejectedTotal == 0) throw new InvalidDataException("CapsuleSparse.NoRealRejectionObserved");
        Console.WriteLine($"CAPSULE_SPARSE_PASS cases={cases.Count};rejected={rejectedTotal};exactRetained={exactRetained};independentMaskRoutes={masksTotal};nonvacuous={rejectedTotal > 0}");
    }

    private static ModelKey Key(byte id) => Beta110FastRelicCatalog.KeyOf(id);
}
