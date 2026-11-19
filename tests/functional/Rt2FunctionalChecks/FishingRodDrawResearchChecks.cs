using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static class FishingRodDrawResearchChecks
{
    internal static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        const string version = "0.113.1";
        const string matchingSeed = "6Q8LL102Y4CF";
        const string firstDrawOnlySeed = "6Q8ENGL0DX5W";
        const int range = 128;
        var profile = Beta111Profile.Instance;
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 10, 0, 1, version);
        var context = SearchContextFactory.From(profile.ProfileId, authority.Character.CharacterKey, 10, authority,
            new(version, version, "beta", "fixture", true, ""), AncientOptionConditionProfile.BroadDefault);
        var query = SearchQuery.Empty with { FishingRodDrawResearch = true };
        var compiled = SearchCompiler.Compile(query, context);
        Check(!JsonSerializer.Serialize(SearchQuery.Empty).Contains(nameof(query.FishingRodDrawResearch), StringComparison.Ordinal),
            "inactive research leaves previous serialized queries intact");
        var restored = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(query))!;
        Check(restored.FishingRodDrawResearch && SearchCompiler.Compile(restored, context).SemanticFingerprint == compiled.SemanticFingerprint &&
            SearchCompiler.Compile(SearchQuery.Empty, context).SemanticFingerprint != compiled.SemanticFingerprint,
            "persisted research is part of query identity");
        var explicitDefaults = SearchCompiler.Compile(query with { FishingRodDrawRequirements = [new(5, 5), new(3, 8), new(3, 11)] }, context);
        Check(explicitDefaults.SemanticFingerprint == compiled.SemanticFingerprint &&
            SearchContinuationIdentity.For(explicitDefaults) == SearchContinuationIdentity.For(compiled),
            "sorted equivalent defaults retain historical search identity");
        var inactive = SearchQuery.Empty with { FishingRodDrawRequirements = [new(4, 2)] };
        Check(JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(inactive))!.FishingRodDrawRequirements!.Single() == new FishingRodDrawRequirement(4, 2) &&
            SearchContinuationIdentity.For(SearchCompiler.Compile(inactive, context)) ==
                SearchContinuationIdentity.For(SearchCompiler.Compile(SearchQuery.Empty, context)),
            "inactive rows survive authoring but do not participate in search identity");
        foreach (FishingRodDrawRequirement[] invalid in new FishingRodDrawRequirement[][] { [], [new(0, 2)], [new(2, 0)] })
        {
            try
            {
                _ = SearchCompiler.Compile(query with { FishingRodDrawRequirements = invalid }, context);
                throw new InvalidDataException("Malformed manual rows were accepted");
            }
            catch (ArgumentException ex) when (ex.Message == FishingRodDrawResearch.InvalidRequirementsCode) { }
        }

        PreparedSearch Prepare(CompiledSearch authored, bool skip) => SearchPreparationCompiler.Compile(authored,
            new(matchingSeed, range, range + 1, 1, SkipExactValidation: skip)).Plan
            ?? throw new InvalidDataException("FishingRodDrawResearch preparation failed");
        TrustedRootHashInput Input(string seed) => TrustedRootHashInput.FromCanonicalSeed(profile, seed);
        var plan = Prepare(compiled, false);
        var positive = ProductionExactSearchEvaluator.Evaluate(plan.Model, Input(matchingSeed));
        Check(positive.IsMatch && positive.Request is null && positive.Evaluation.MatchedRouteIds.Count == 0 &&
            positive.Evaluation.Evidence.Single().OutcomeId == "call3=11/11;call5=5/5", "raw draw Exact does not synthesize a route");
        Check(ProductionExactSearchEvaluator.Evaluate(plan.Model, Input(firstDrawOnlySeed)).Evaluation.Disposition == SearchDisposition.NoMatch,
            "a matching first draw alone cannot confirm the request");

        Check(VisibleSeedCandidateCodec.TryParseOrdinal(profile, matchingSeed, out ulong first, out _, out _), "known seed can be scanned");
        FishingRodDrawRequirement[] manualRows = [new(2051, 3), new(2050, 2), new(1, 2), new(2051, 2)];
        var manual = SearchCompiler.Compile(query with { FishingRodDrawRequirements = manualRows }, context);
        Check(manual.NormalizedQuery.FishingRodDrawRequirements!.SequenceEqual(new[]
            { new FishingRodDrawRequirement(1, 2), new(2050, 2), new(2051, 3) }) && manual.SemanticFingerprint != compiled.SemanticFingerprint,
            "manual repeated ordinal keeps strongest last-slot threshold in order");
        int nativeMatches = 0;
        foreach (var current in new[] { compiled, manual })
        {
            var originalRows = current == compiled ? FishingRodDrawResearch.DefaultRequirements : manualRows;
            var expected = Enumerable.Range(0, range).Select(offset => VisibleSeedCandidateCodec.FormatOrdinal(profile, first + (ulong)offset))
                .Where(seed => originalRows.All(row => NativeMatches(seed, row))).ToArray();
            Check(expected.Length > 0 && expected.Length < range, "native sample includes positives and negatives");
            nativeMatches += expected.Length;
            foreach (bool skip in new[] { false, true })
            {
                var request = Prepare(current, skip);
                var physical = FamilyExecutionCoordinator.Plan(request, gpuAvailable: false);
                Check(physical.OrderedFamilies.Single().FamilyId == FishingRodDrawResearch.Id && !physical.AllConditionsUnfiltered &&
                    physical.FilterRemainders.Count == 0, "research registers actual CPU filtering");
                await using var session = FamilyExecutionCoordinator.Start(request, gpuAvailable: false);
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
                var results = new List<SearchCandidate>();
                while (session.TryReadCandidate(out var candidate))
                {
                    results.Add(candidate!);
                    session.AcknowledgeCandidate(candidate!.Seed);
                }
                Check(results.Select(candidate => candidate.Seed).SequenceEqual(expected) &&
                    results.All(candidate => !candidate.IsUnfiltered && candidate.ValidationDisposition ==
                        (skip ? SearchDisposition.NotEvaluatedByPolicy : SearchDisposition.Match)),
                    "scan agrees with native RNG and preserves validation status");
            }
        }
        var direct = new Xoshiro256StarStar(777);
        direct.Advance(250_000);
        var jumped = FishingRodDrawAdvance.Advance(new Xoshiro256StarStar(777), 250_000);
        Check(direct.State == jumped.State && direct.CallCount == jumped.CallCount && direct.NextInt(int.MaxValue) == jumped.NextInt(int.MaxValue),
            "large jump preserves Xoshiro state and bounded draw");
        Check(FishingRodDrawResearch.Matches(Input(matchingSeed).RootHash, [new(int.MaxValue, 1)]),
            "maximum ordinal executes without counter overflow");
        Console.WriteLine($"FISHING_ROD_DRAW_RESEARCH=PASS;nativeMatches={nativeMatches};manualRows=true;rawOnly=true;normalAndSkipExact=true");
        return 0;

        bool NativeMatches(string seed, FishingRodDrawRequirement row)
        {
            var rng = new MegaCrit.Sts2.Core.Random.Rng(profile.ComputeRootSeed(seed), "non_combat_generation");
            for (int position = 1; position < row.DrawOrdinal; position++) _ = rng.NextUnsignedLong();
            return rng.NextInt(0, row.PoolSize) == row.PoolSize - 1;
        }
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidDataException("FishingRodDrawResearch: " + description);
    }
}
