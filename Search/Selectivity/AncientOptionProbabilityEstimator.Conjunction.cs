using System.Numerics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class AncientOptionProbabilityEstimator
{
    // Identity is already conditioned by the caller. Modern rows are an OR;
    // legacy sets are conjunctions over that same visible offer, not marginals.
    internal static SearchSelectivityEstimate EstimateConditionalConjunction(SearchSelectivityInput plan,
        int act, ModelKey ancient, IReadOnlyList<AncientSearchBranchCondition> branches,
        IReadOnlyList<ModelKeySetFilter> optionFilters, IReadOnlyList<ModelKeySetFilter> seaFilters)
    {
        SearchSelectivityEstimate Result(double p) => SearchSelectivityEstimate.Exact(Math.Clamp(p, 0, 1),
            SearchSelectivityMethod.ConditionalChain, SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.StructuralDependence, "Probability.AncientOption.VisibleOfferConjunction",
            "One source-generated visible offer satisfies the branch OR and all option/Sea Glass sets; parent identity is paid once.",
            ["SameOfferTargetsNotIndependent=true", "ParentIdentityPaidOnce=true", "SuccessiveRandomDrawsUseProductModel=true"],
            conditionedOnDomains: [SearchSelectivityDomain.WorldEvent]);
        SearchSelectivityEstimate Missing(string why) => SearchSelectivityEstimate.Unpriced(
            "Probability.AncientOption.Conjunction." + why, "Captured event-local option authority is required.");
        var actRows = branches.Where(b => b.Act == act).ToArray();
        var rows = actRows.Where(b => b.AncientKey == ancient).ToArray();
        if (actRows.Length > 0 && rows.Length == 0) return Result(0);
        if (optionFilters.All(f => f.IsEmpty) && seaFilters.All(f => f.IsEmpty) &&
            (rows.Length == 0 || rows.Any(b => b.OptionAny.Count == 0 && b.SeaGlassTargetAny.Count == 0))) return Result(1);
        var context = FindContext(plan, new(act, ancient, [], []));
        var catalog = context?.Catalog;
        if (context is null || catalog is null || context.CharacterKey != plan.Authority.Character.CharacterKey ||
            !CatalogUsable(context, catalog) && !plan.Authority.UsesBestEffortModel || context.HookDecision == Beta109HookDecision.Unknown)
            return Missing("AuthorityMissing");
        var sea = new ModelKey(BaseGameModelKeys.Categories.Relic, "SEA_GLASS");
        var keys = rows.SelectMany(b => b.OptionAny).Concat(optionFilters.SelectMany(f => f.Any.Concat(f.All).Concat(f.Ban)))
            .Append(sea).Distinct().ToArray();
        var bits = keys.Select((key, index) => (key, bit: BigInteger.One << index)).ToDictionary(x => x.key, x => x.bit);
        BigInteger Mask(ModelKey key) => bits.GetValueOrDefault(key);
        Dictionary<BigInteger, double> Unit(BigInteger mask = default) => new() { [mask] = 1 };
        Dictionary<BigInteger, double> Pool(IReadOnlyList<ModelKey> values) => values.Count == 0 ? Unit() :
            values.GroupBy(Mask).ToDictionary(g => g.Key, g => g.Count() / (double)values.Count);
        static Dictionary<BigInteger, double> Join(Dictionary<BigInteger, double> a, Dictionary<BigInteger, double> b)
        {
            var result = new Dictionary<BigInteger, double>();
            foreach (var x in a) foreach (var y in b)
                result[x.Key | y.Key] = result.GetValueOrDefault(x.Key | y.Key) + x.Value * y.Value;
            return result;
        }
        static Dictionary<BigInteger, double> Mix(Dictionary<BigInteger, double> a, double weight, Dictionary<BigInteger, double> b)
        {
            var result = a.ToDictionary(x => x.Key, x => weight * x.Value);
            foreach (var x in b) result[x.Key] = result.GetValueOrDefault(x.Key) + (1 - weight) * x.Value;
            return result;
        }
        Dictionary<BigInteger, double>? Separate(IReadOnlyList<IReadOnlyList<ModelKey>> pools)
        {
            if (pools.Any(p => p.Count == 0)) return null;
            return pools.Aggregate(Unit(), (state, pool) => Join(state, Pool(pool)));
        }
        // Uniformly select k source representatives. A singleton representative
        // gives a physical shuffle entry; Darv supplies a weighted option pool.
        Dictionary<BigInteger, double>? Take(IReadOnlyList<IReadOnlyList<ModelKey>> pools, int requested)
        {
            if (pools.Count == 0 || pools.Any(p => p.Count == 0)) return null;
            int take = Math.Min(requested, pools.Count);
            var dp = Enumerable.Range(0, take + 1).Select(_ => new Dictionary<BigInteger, double>()).ToArray();
            dp[0] = Unit(); int processed = 0;
            foreach (var pool in pools)
            {
                var choices = Pool(pool);
                for (int k = Math.Min(take, ++processed); k > 0; k--)
                    foreach (var x in Join(dp[k - 1], choices)) dp[k][x.Key] = dp[k].GetValueOrDefault(x.Key) + x.Value;
            }
            double combinations = 1;
            for (int i = 0; i < take; i++) combinations *= (pools.Count - i) / (double)(i + 1);
            return dp[take].ToDictionary(x => x.Key, x => x.Value / combinations);
        }
        string name = NormalizeEntry(ancient.Entry);
        Dictionary<BigInteger, double>? outcomes;
        if (context.HookDecision == Beta109HookDecision.Deny)
        {
            if (!context.DynamicFactsExact || catalog.Pool("wrapper.proceed").Count != 1) return Missing("HookDeniedAuthorityMissing");
            outcomes = Pool(catalog.Pool("wrapper.proceed"));
        }
        else switch (name)
        {
            case "PAEL": outcomes = Separate(BuildPaelPools(plan, catalog)); break;
            case "TEZCATARA": outcomes = Separate(BuildTezcataraPools(plan, catalog)); break;
            case "VAKUU": outcomes = Separate([catalog.Pool("vakuu.pool1"), catalog.Pool("vakuu.pool2"), catalog.Pool("vakuu.pool3")]); break;
            case "NONUPEIPE": outcomes = Take(BuildNonupeipePool(plan, catalog).Select(k => (IReadOnlyList<ModelKey>)new[] { k }).ToArray(), 3); break;
            case "TANX": outcomes = Take(BuildTanxPool(plan, catalog).Select(k => (IReadOnlyList<ModelKey>)new[] { k }).ToArray(), 3); break;
            case "DARV":
            {
                var sets = catalog.PoolsWithPrefix("darv.valid.")
                    .Where(p => SemanticAssumptions(plan).DarvAllowPandorasBoxRelicSet || p.PoolId != "darv.valid.pandoras-box")
                    .Where(p => p.OrderedOptions.Count > 0).Select(p => p.OrderedOptions).ToArray();
                var dusty = catalog.Pool("darv.dusty-tome");
                var two = Take(sets, 2); var three = Take(sets, 3);
                outcomes = two is null || three is null || dusty.Count != 1 ? null : Mix(Join(two, Pool(dusty)), .5, three);
                break;
            }
            case "OROBAS":
            {
                var a = catalog.Pool("orobas.pool1.true"); var b = catalog.Pool("orobas.pool1.false");
                var second = catalog.Pool("orobas.pool2");
                var third = (SemanticAssumptions(plan).OrobasTouchOfOrobasConditionMet ? catalog.Pool("orobas.pool3.touch") : [])
                    .Concat(SemanticAssumptions(plan).OrobasArchaicToothConditionMet ? catalog.Pool("orobas.pool3.tooth") : []).ToArray();
                // No eligible third option is a locked placeholder, with no
                // visible identity. Its consumed draw does not add offer mass.
                outcomes = a.Count == 0 || b.Count == 0 || second.Count == 0 ? null :
                    Join(Join(Mix(Pool(a), (double)0.3333333f, Pool(b)), Pool(second)), Pool(third));
                break;
            }
            default: return Missing("GeneratorUnsupported:" + name);
        }
        if (outcomes is null) return Missing("PoolMissing");
        bool Set(BigInteger mask, ModelKeySetFilter f) =>
            (f.Any.Count == 0 || f.Any.Any(k => (mask & Mask(k)) != 0)) &&
            f.All.All(k => Mask(k) != 0 && (mask & Mask(k)) != 0) && f.Ban.All(k => (mask & Mask(k)) == 0);
        bool SeaSet(ModelKey target, ModelKeySetFilter f) =>
            (f.Any.Count == 0 || f.Any.Contains(target)) && f.All.All(k => k == target) && !f.Ban.Contains(target);
        bool needsSea = rows.Any(b => b.SeaGlassTargetAny.Count > 0) || seaFilters.Any(f => !f.IsEmpty);
        ModelKey[] characters = context.UnlockedCharacters.Where(k => k.IsValid &&
            k.Category == BaseGameModelKeys.Categories.Character && k != context.CharacterKey).ToArray();
        if (needsSea && outcomes.Any(x => x.Value > 0 && (x.Key & Mask(sea)) != 0) &&
            (!context.UnlockedCharacterSourceOrderExact || characters.Length == 0)) return Missing("SeaGlassTargetAuthorityMissing");
        bool Accept(BigInteger mask, ModelKey target)
        {
            if (!optionFilters.All(f => Set(mask, f))) return false;
            bool hasSea = (mask & Mask(sea)) != 0;
            // Canonical legacy Sea Glass filters are vacuous when not offered.
            if (hasSea && !seaFilters.All(f => SeaSet(target, f))) return false;
            return rows.Length == 0 || rows.Any(b =>
                (b.OptionAny.Count == 0 || b.OptionAny.Any(k => (mask & Mask(k)) != 0)) &&
                (b.SeaGlassTargetAny.Count == 0 || hasSea && b.SeaGlassTargetAny.Contains(target)));
        }
        double probability = outcomes.Sum(x => x.Value * (needsSea && (x.Key & Mask(sea)) != 0
            ? characters.Count(k => Accept(x.Key, k)) / (double)characters.Length : Accept(x.Key, default) ? 1 : 0));
        return Result(probability);
    }
}
