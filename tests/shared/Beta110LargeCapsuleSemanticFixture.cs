using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Tests.FastSearch;

internal sealed record Beta110LargeCapsuleSemanticFixtureResult(
    bool FixturePoolValid,
    bool ProductionEvaluatorSemanticValid,
    bool FastPlanSemanticValid,
    bool FixedSeedDualRelicMatch,
    bool UnorderedAbEqualsBa,
    bool DuplicateRuleValid,
    bool NegativePairRejected,
    bool BonesLargeCapsuleDualRelicMatch,
    bool FastCandidateEnteredExact,
    string DirectSeed,
    string BonesSeed,
    string Issue)
{
    public bool Passed =>
        FixturePoolValid &&
        ProductionEvaluatorSemanticValid &&
        FastPlanSemanticValid &&
        FixedSeedDualRelicMatch &&
        UnorderedAbEqualsBa &&
        DuplicateRuleValid &&
        NegativePairRejected &&
        BonesLargeCapsuleDualRelicMatch &&
        FastCandidateEnteredExact &&
        string.IsNullOrWhiteSpace(Issue);
}

/// <summary>
/// Deterministic Production Exact fixture for the Large Capsule unordered pair
/// contract. Whetstone and War Paint are deliberately used instead of synthetic
/// BENCH relic identities: both are present in the copied ordinary relic pool and
/// have audited production capability registrations, so OpeningRoute precision
/// remains exact and the structured identity condition is actually reachable.
/// </summary>
internal static class Beta110LargeCapsuleSemanticFixture
{
    public const string DirectDualRelicSeed = "000000000018";
    public const string BonesDualRelicSeed = "000000000HCX";

    public static ModelKey RelicA => BaseGameModelKeys.OrdinaryRelics.Whetstone;
    public static ModelKey RelicB => BaseGameModelKeys.OrdinaryRelics.WarPaint;
    public static ModelKey RelicC => new(BaseGameModelKeys.Categories.Relic, "BENCH_UNCOMMON_RELIC");

    public static NeowSearchFilter DirectSingle(ModelKey target) => StructuredRoute(
        BaseGameModelKeys.Relics.LargeCapsule,
        Structured(target));

    public static NeowSearchFilter DirectPair(ModelKey first, ModelKey second) => StructuredRoute(
        BaseGameModelKeys.Relics.LargeCapsule,
        Structured(first, second));

    public static NeowSearchFilter BonesPair(ModelKey first, ModelKey second) => NeowSearchFilter.Empty with
    {
        NeowRoute = new NeowRouteSearchCondition(BaseGameModelKeys.Relics.NeowsBones),
        RequireNeowsBones = true,
        RequiredBonesCombination = new[]
        {
            BaseGameModelKeys.Relics.LargeCapsule,
            BaseGameModelKeys.Relics.CursedPearl
        },
        StructuredNeowEffects = new[] { Structured(first, second) }
    };

    public static Beta110LargeCapsuleSemanticFixtureResult Validate(
        RuntimeContextAuthoritySnapshot authority,
        GameVersionDetection detection)
    {
        bool poolValid = ValidatePool(authority.EffectAuthority, out string poolIssue);
        if (!poolValid)
        {
            return Failed(poolIssue);
        }
        authority = WithMatchingInitialRelicWorld(authority);

        ExactProbe singleA = Probe(DirectSingle(RelicA), DirectDualRelicSeed, authority, detection);
        ExactProbe singleB = Probe(DirectSingle(RelicB), DirectDualRelicSeed, authority, detection);
        ExactProbe ab = Probe(DirectPair(RelicA, RelicB), DirectDualRelicSeed, authority, detection);
        ExactProbe ba = Probe(DirectPair(RelicB, RelicA), DirectDualRelicSeed, authority, detection);
        ExactProbe ac = Probe(DirectPair(RelicA, RelicC), DirectDualRelicSeed, authority, detection);
        ExactProbe bones = Probe(BonesPair(RelicA, RelicB), BonesDualRelicSeed, authority, detection);

        bool duplicateRule = DuplicatePairDoesNotMatch(authority, detection, out string duplicateIssue);
        bool evaluatorValid = singleA.IsMatch && singleB.IsMatch && ab.IsMatch && ba.IsMatch && !ac.IsMatch;
        bool unordered = ab.IsMatch && ba.IsMatch &&
                         string.Equals(ab.EvaluationFingerprint, ba.EvaluationFingerprint, StringComparison.Ordinal);
        bool fastPlanValid = ab.FastPlanCompiled && ba.FastPlanCompiled && bones.FastPlanCompiled;
        bool fastEnteredExact = ab.FastKept && ba.FastKept && bones.FastKept;
        string issue = FirstIssue(
            poolIssue,
            duplicateIssue,
            singleA.Issue,
            singleB.Issue,
            ab.Issue,
            ba.Issue,
            ac.Issue,
            bones.Issue,
            evaluatorValid ? string.Empty : "ProductionEvaluatorDidNotHonorLargeCapsuleUnorderedPair",
            unordered ? string.Empty : "LargeCapsuleABAndBAEvaluationDiffered",
            fastPlanValid ? string.Empty : "LargeCapsuleFastPlanDidNotCompile",
            fastEnteredExact ? string.Empty : "LargeCapsuleFixedSeedWasRejectedBeforeProductionExact");

        return new Beta110LargeCapsuleSemanticFixtureResult(
            poolValid,
            evaluatorValid,
            fastPlanValid,
            ab.IsMatch,
            unordered,
            duplicateRule,
            !ac.IsMatch,
            bones.IsMatch,
            fastEnteredExact,
            DirectDualRelicSeed,
            BonesDualRelicSeed,
            issue);
    }

    private static bool ValidatePool(NeowEffectAuthoritySnapshot? authority, out string issue)
    {
        if (authority?.HasExactRelicBagSourcePools != true)
        {
            issue = "RelicBagSourcePoolsNotExact";
            return false;
        }

        NeowEffectRelicSnapshot[] pool = authority.SharedRelicPoolSource!
            .Concat(authority.CharacterRelicPoolSource!)
            .Where(relic => relic.Rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare)
            .GroupBy(relic => relic.RelicKey, ModelKeyComparer.Instance)
            .Select(group => group.First())
            .ToArray();
        NeowEffectRelicSnapshot? first = pool.FirstOrDefault(relic => relic.RelicKey == RelicA);
        NeowEffectRelicSnapshot? second = pool.FirstOrDefault(relic => relic.RelicKey == RelicB);
        if (first is null || second is null)
        {
            issue = "LargeCapsuleFixtureTargetMissingFromOrdinaryRelicPool";
            return false;
        }
        if (first.RelicKey == second.RelicKey)
        {
            issue = "LargeCapsuleFixtureTargetsMustBeDistinct";
            return false;
        }
        if (!first.NestedClassificationExact || !second.NestedClassificationExact ||
            first.NestedEffectKind != NestedRelicEffectKind.Whetstone ||
            second.NestedEffectKind != NestedRelicEffectKind.WarPaint)
        {
            issue = "LargeCapsuleFixtureTargetCapabilityNotExact";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private static RuntimeContextAuthoritySnapshot WithMatchingInitialRelicWorld(RuntimeContextAuthoritySnapshot authority)
    {
        var effects = authority.EffectAuthority!;
        var shared = effects.SharedRelicPoolSource!;
        var character = effects.CharacterRelicPoolSource!;
        var player = shared.Concat(character).DistinctBy(relic => relic.RelicKey).ToArray();
        var rarities = new[] { EffectRelicRarity.Common, EffectRelicRarity.Uncommon, EffectRelicRarity.Rare, EffectRelicRarity.Shop };
        Beta109RelicBucketSnapshot[] Buckets(string scope, IEnumerable<NeowEffectRelicSnapshot> relics) => rarities
            .Select(rarity =>
            {
                // The original four-relic fixture has no Shop bucket entries.
                // These flags never participate in its Common/Uncommon/Rare draws.
                var entries = relics.Where(relic => relic.Rarity == rarity)
                    .Select(relic => new Beta109RelicBucketEntrySnapshot(relic.RelicKey, relic.IsAllowedInShops, true)).ToArray();
                return new Beta109RelicBucketSnapshot(scope + ":" + rarity,
                    entries.Select(entry => entry.RelicKey).ToArray(), OrderExact: true) { OrderedEntries = entries };
            }).ToArray();
        var template = RelicSequenceFixtureFactory.CreateModern();
        const string seed = "000000000000";
        ulong root = Beta110Profile.Instance.ComputeRootSeed(seed);
        var generation = template.Beta109Generation! with {
            Profile = RuntimeProfileId.Beta110, OriginalSeed = seed, CanonicalSeed = seed,
            ActSelectionRoot = root, RunSeedRoot = root, CharacterKey = authority.Character.CharacterKey,
            LobbyPlayers = new[] { new Beta109LobbyPlayerSnapshot(0, authority.Character.CharacterKey, false, true) },
            SharedRelicBuckets = Buckets("shared", shared), PlayerRelicBuckets = Buckets("player", player),
            CatalogFingerprint = effects.CatalogFingerprint,
            UnlockFingerprint = effects.UnlockFingerprint,
            SnapshotFingerprint = "large-capsule-four-relic-initial-world"
        };
        return authority.WithWorldAuthority(template with {
            CapturedProfileId = RuntimeProfileId.Beta110,
            SharedRelicPoolSource = shared, CharacterRelicPoolSource = character,
            RelicInitializationExact = true, CatalogFingerprint = generation.CatalogFingerprint,
            SnapshotFingerprint = generation.SnapshotFingerprint, Beta109Generation = generation
        });
    }

    private static bool DuplicatePairDoesNotMatch(
        RuntimeContextAuthoritySnapshot authority,
        GameVersionDetection detection,
        out string issue)
    {
        var duplicateCondition = new NeowStructuredEffectSearchCondition(
            BaseGameModelKeys.Relics.LargeCapsule,
            NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics,
            NeowStructuredOutputKind.Relic,
            new[] { RelicA, RelicA },
            AllowDuplicateOutputs: true);
        var result = Compile(
            StructuredRoute(BaseGameModelKeys.Relics.LargeCapsule, duplicateCondition),
            authority,
            detection);
        if (result.Plan is not { } plan)
        {
            issue = "LargeCapsuleDuplicateCompileFailed:" + result.Issue;
            return false;
        }
        var condition = plan.Evaluation.StructuredNeowEffects.Single();
        // A repeated target remains an authored multiplicity obligation. The
        // canonical entry may admit it; only the final observation rejects it.
        bool preserved = condition.AllowDuplicateOutputs && condition.OutputKeys.SequenceEqual(new[] { RelicA, RelicA });
        ulong root = Beta110Profile.Instance.ComputeRootSeed(DirectDualRelicSeed);
        if (!TrustedRootHashInput.TryBindCanonicalSeed(Beta110Profile.Instance, root, DirectDualRelicSeed,
                out var input, out string bindIssue))
        {
            issue = "LargeCapsuleDuplicateRootHashBindingFailed:" + bindIssue;
            return false;
        }
        var exact = ProductionExactSearchEvaluator.Evaluate(plan, input);
        bool rejected = preserved && exact.Evaluation.Disposition == SearchDisposition.NoMatch;
        issue = rejected ? string.Empty : "LargeCapsuleDuplicateOutputRuleMismatch:" + exact.FailureCode;
        return rejected;
    }

    private static ExactProbe Probe(
        NeowSearchFilter filter,
        string seed,
        RuntimeContextAuthoritySnapshot authority,
        GameVersionDetection detection)
    {
        var compile = Compile(filter, authority, detection);
        if (compile.Plan is null)
        {
            return new ExactProbe(false, false, false, string.Empty, "CompileFailed:" + compile.Issue);
        }

        var plan = compile.Plan;
        ulong rootHash = XxHash64.HashUtf8(seed, 0UL);
        var n = NeowReplayPlan.Compile(plan, false);
        var r = new CapsuleRelicReplay(plan, NeowReplayPlan.Compile(plan, true));
        bool fastKept = NeowFamilyReplay.Matches(rootHash, n) && r.Matches(rootHash);
        if (!TrustedRootHashInput.TryBindCanonicalSeed(
                Beta110Profile.Instance,
                rootHash,
                seed,
                out TrustedRootHashInput input,
                out string bindIssue))
        {
            return new ExactProbe(true, fastKept, false, string.Empty, "RootHashBindingFailed:" + bindIssue);
        }

        ProductionExactSearchResult exact = ProductionExactSearchEvaluator.Evaluate(plan, input);
        return new ExactProbe(
            true,
            fastKept,
            exact.IsMatch,
            EvaluationFingerprint(exact.Evaluation),
            exact.IsMatch || !string.IsNullOrWhiteSpace(exact.FailureCode)
                ? string.Empty
                : "ExactEvaluationReturnedNoDisposition");
    }

    private static ExactSearchExecutionCompileResult Compile(
        NeowSearchFilter filter,
        RuntimeContextAuthoritySnapshot authority,
        GameVersionDetection detection)
    {
        var context = SearchContextFactory.From(
            RuntimeProfileId.Beta110,
            authority.Character.CharacterKey,
            authority.Ascension,
            authority,
            detection,
            filter.AncientOptionConditions);
        var compiled = SearchCompiler.Compile(LegacySearchQueryAdapter.FromFilter(filter), context);
        return ExactSearchExecutionRequestFactory.Compile(compiled,
            new SearchRunOptions("000000000000", 1, 30, 1, IncludeDiagnostics: false));
    }

    private static NeowStructuredEffectSearchCondition Structured(params ModelKey[] outputs) => new(
        BaseGameModelKeys.Relics.LargeCapsule,
        NeowStructuredConditionKind.ExactUnorderedPair,
        NeowStructuredEffectScope.NestedRelics,
        NeowStructuredOutputKind.Relic,
        outputs);

    private static NeowSearchFilter StructuredRoute(
        ModelKey route,
        params NeowStructuredEffectSearchCondition[] conditions) => NeowSearchFilter.Empty with
    {
        NeowRoute = new NeowRouteSearchCondition(route),
        StructuredNeowEffects = conditions
    };

    private static string EvaluationFingerprint(SearchQueryEvaluation evaluation) => string.Join("|", new[]
    {
        evaluation.Disposition.ToString(),
        string.Join(",", evaluation.MatchedRouteIds.OrderBy(value => value, StringComparer.Ordinal)),
        string.Join(";", evaluation.Witnesses
            .OrderBy(witness => witness.OpeningRouteId, StringComparer.Ordinal)
            .Select(witness => string.Join(",", new[]
            {
                witness.OpeningRouteId,
                string.Join(">", witness.AcquisitionOrder.Select(key => key.Serialized))
            })))
    });

    private static Beta110LargeCapsuleSemanticFixtureResult Failed(string issue) => new(
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        DirectDualRelicSeed,
        BonesDualRelicSeed,
        issue);

    private static string FirstIssue(params string[] issues) =>
        issues.FirstOrDefault(issue => !string.IsNullOrWhiteSpace(issue)) ?? string.Empty;

    private readonly record struct ExactProbe(
        bool FastPlanCompiled,
        bool FastKept,
        bool IsMatch,
        string EvaluationFingerprint,
        string Issue);
}
