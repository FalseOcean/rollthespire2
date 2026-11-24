using System.Numerics;
using System.Reflection;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Predictability;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static class PredictabilityMathChecks
{
    internal static int Run()
    {
        int checks = 0;
        var failures = new List<string>();
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) failures.Add(name);
        }
        void Near(double actual, double expected, string name, double relativeTolerance = 2e-13)
        {
            Check(double.IsFinite(actual) &&
                Math.Abs(actual - expected) <= Math.Max(double.Epsilon, Math.Abs(expected) * relativeTolerance),
                $"{name}: actual={actual:G17}, expected={expected:G17}");
        }
        double Tail(double p, long n, int k) => SearchPredictabilityMath.ProbabilityAtLeastTarget(p, n, k);
        double One(double p, double n) => SearchPredictabilityMath.ProbabilityAtLeastOne(p, n);

        foreach (int k in new[] { int.MinValue, 0, 1, 2, 10 })
        {
            Near(Tail(0, 10, k), 0, "p=0");
            Near(Tail(1, 10, k), 1, "p=1");
            Near(Tail(.5, 0, k), 0, "no roots");
            Near(Tail(.5, -1, k), 0, "negative roots");
        }
        Near(Tail(1, 10, 11), 0, "target exceeds roots even with p=1");
        Near(Tail(-.1, 10, 2), 0, "negative p retains endpoint clamp");
        Near(Tail(1.1, 10, 2), 1, "p above one retains endpoint clamp");
        Check(double.IsNaN(Tail(double.NaN, 10, 2)), "invalid p propagates NaN rather than a probability");
        Check(double.IsNaN(One(double.NaN, 10)), "one invalid p remains NaN");
        Near(One(.5, double.NaN), 0, "unordered root horizon retains empty convention");
        Near(Tail(.25, 8, 0), One(.25, 8), "nonpositive target retains at-least-one convention");
        Near(One(0, 10), 0, "one p=0");
        Near(One(1, .25), 1, "one p=1 fractional roots");
        Near(One(.5, 0), 0, "one no roots");
        Near(One(.5, .5), 1 - Math.Sqrt(.5), "fractional fragment population");
        Near(One(1e-20, 1), 1e-20, "one tiny p");
        Check(One(double.Epsilon, 1) == double.Epsilon, "one subnormal p");
        Near(One(1e-20, .25), 2.5e-21, "fractional roots with tiny p");
        Near(One(1e-20, long.MaxValue), .08810800309658830158, "one tiny p over long range");
        Near(One(.5, double.PositiveInfinity), 1, "infinite positive horizon");
        foreach (double p in new[] { double.Epsilon, 1e-310, 1e-300, 1e-20, 1e-17, 1e-10, .001, .1, .5, .9999, Math.BitDecrement(1d) })
            Near(One(p, 1), p, "single-root identity");
        Near(Tail(1e-20, 2, 2), 1e-40, "all-success tiny upper tail");
        Near(Tail(1e-20, 3, 2), 3e-40, "tiny upper tail without subtraction");
        Near(Tail(1e-160, 3, 2), 3e-320, "representable subnormal upper tail");

        // Independent integer enumeration: p is an exact dyadic rational, so all
        // numerator arithmetic is exact and only the final division rounds.
        for (int n = 1; n <= 24; n++)
        foreach (int numerator in new[] { 1, 2, 4, 6, 7 })
        {
            BigInteger denominator = BigInteger.Pow(8, n);
            BigInteger choose = 1;
            var masses = new BigInteger[n + 1];
            for (int k = 0; k <= n; k++)
            {
                masses[k] = choose * BigInteger.Pow(numerator, k) * BigInteger.Pow(8 - numerator, n - k);
                if (k < n) choose = choose * (n - k) / (k + 1);
            }
            BigInteger upper = 0;
            for (int k = n; k >= 1; k--)
            {
                upper += masses[k];
                Near(Tail(numerator / 8d, n, k), (double)upper / (double)denominator,
                    $"integer enumeration n={n},p={numerator}/8,k={k}");
            }
        }

        // Symmetry gives this central value directly from C(2000,1000)/2^2000.
        const double central = 0.5089195055729272;
        Near(Tail(.5, 2000, 1000), central, "underflowed P[X=0] is not negligible lower tail");
        Near(Tail(.5, 2001, 1001), .5, "odd central symmetry");
        // Offline mpmath (65-90 decimal digits), with each input p converted
        // from its exact binary double. Small k: finite lower sums; tiny upper
        // tails: positive sums with a geometric remainder bound; 1e6 case:
        // regularized betainc. No sampling or production-algorithm duplication.
        foreach (var (p, n, k, expected) in new (double, long, int, double)[]
        {
            (1e-20, long.MaxValue, 2, .00400081164788241349468019277448956547),
            (1e-20, long.MaxValue, 3, .00012205205935048866279877275496612536),
            (1e-12, 1_000_000_000_000, 2, .264241117657115349409663598783009725),
            (1e-12, 1_000_000_000_000, 10, 1.11425478334665548484131442495520668e-7),
            (1e-12, 1_000_000_000_000, 100, 3.98128079964170283474498892428633119e-159),
            (.001, 1_000_000, 1000, .504211555166992293347618314420663475)
        }) Near(Tail(p, n, k), expected, $"high precision n={n},p={p:G17},k={k}");

        // Large central cases exercise the bounded summation. The symmetric
        // reference is .5+.5*C(n,n/2)/2^n, evaluated using 90-digit loggamma.
        // The long.MaxValue reference uses 65-digit loggamma + an upper sum
        // whose remaining probability is <5e-43. Allow accumulated roundoff;
        // this tolerance is separate from the analytical truncation bound.
        double largeSymmetry = Tail(.5, 2L * int.MaxValue, int.MaxValue);
        double largeRare = Tail(int.MaxValue / (double)long.MaxValue, long.MaxValue, int.MaxValue);
        Near(largeSymmetry, .500006087376105821184828213470531333,
            "full int target at central symmetry", 2e-11);
        Near(largeRare,
            .500002869616615211667569861392476722, "long range central rare probability", 2e-11);
        Near(Tail(.5, long.MaxValue, int.MaxValue), 1, "mean far beyond target");
        Near(Tail(double.Epsilon, long.MaxValue, 2), 0, "genuine unrepresentable upper tail");
        Near(Tail(Math.BitDecrement(1d), int.MaxValue, int.MaxValue), .9999997615814494307,
            "all successes near p=1");

        // Verify the actual ETA inverse caller shares the stable log-miss
        // calculation. Decimal ln at 100 digits supplies the root references.
        MethodInfo inverse = typeof(FamilySearchEtaProjectorV1).GetMethod("TryRootsForAtLeastOneConfidence",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var (p, expectedRoots) in new[] { (1e-17, 460517018598809013L), (1e-18, 4605170185988090149L) })
        {
            object[] arguments = [p, .99d, 0L];
            Check((bool)inverse.Invoke(null, arguments)!, "ETA rare confidence horizon is representable");
            long roots = (long)arguments[2];
            Check(Math.Abs((double)roots - expectedRoots) <= 2e-15 * expectedRoots, "ETA confidence root reference");
            Near(One(p, roots), .99, "ETA inverse and forward confidence agree");
        }
        object[] overflow = [1e-20, .99d, 0L];
        Check(!(bool)inverse.Invoke(null, overflow)!, "ETA true confidence overflow remains unavailable");
        MethodInfo ceil = typeof(FamilySearchEtaProjectorV1).GetMethod("TryCeil", BindingFlags.NonPublic | BindingFlags.Static)!;
        object[] boundaryOverflow = [9223372036854775808d, 0L];
        Check(!(bool)ceil.Invoke(null, boundaryOverflow)!, "ETA rejects double 2^63 before long conversion");
        object[] lastRepresentable = [Math.BitDecrement(9223372036854775808d), 0L];
        Check((bool)ceil.Invoke(null, lastRepresentable)! && (long)lastRepresentable[1] == long.MaxValue - 1023,
            "ETA accepts last representable double below 2^63");
        foreach (long n in new long[] { 10, 100, 2000 })
        foreach (double p in new[] { .125, .25, .5, .75, .875 })
        {
            double previous = 1;
            foreach (int k in new[] { 1, (int)(n / 2), (int)n })
            {
                double value = Tail(p, n, k);
                Check(value >= 0 && value <= 1, "probability range");
                Check(value <= previous + 2e-14, "monotone in target");
                Check(Tail(p, n + 1, k) + 2e-14 >= value, "monotone in roots");
                Check(Tail(Math.Min(1, p + .0625), n, k) + 2e-14 >= value, "monotone in p");
                Near(value + Tail(1 - p, n, (int)n - k + 1), 1, "complement under success/failure exchange");
                previous = value;
            }
        }

        CheckCombatAssignmentHistories(Check);
        CheckCombatRewardCache(Check);

        Console.WriteLine($"Predictability samples: central={Tail(.5, 2000, 1000):G17}; " +
            $"tinyOne={One(1e-20, 1):G17}; tinyTwo={Tail(1e-20, 3, 2):G17}; " +
            $"largeSymmetry={largeSymmetry:G17}; largeRare={largeRare:G17}");
        Console.WriteLine($"Runtime primitives: LogP1(-1e-20)={double.LogP1(-1e-20):G17}; " +
            $"ExpM1(-1e-20)={double.ExpM1(-1e-20):G17}");
        foreach (string failure in failures.Take(25)) Console.Error.WriteLine(failure);
        Console.WriteLine($"Predictability math: {checks} checks, {failures.Count} failures; managed arithmetic only.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static SearchContext CombatProbabilityFixture(string identityPrefix)
    {
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect, 10, 0, 1);
        var effects = authority.EffectAuthority! with
        {
            CharacterRewardIdentityPoolExact = true,
            CharacterRewardPool = authority.EffectAuthority!.CharacterRewardPool!.Select(c => c with
            {
                CardKey = new ModelKey("CARD", identityPrefix + c.CardKey.Entry),
                CatalogProfileId = RuntimeProfileId.Beta111
            }).ToArray()
        };
        var world = Beta109WorldFixtureFactory.Create();
        authority = authority.WithEffectAuthority(effects).WithWorldAuthority(world with
        {
            CapturedProfileId = RuntimeProfileId.Beta111,
            Beta109Generation = world.Beta109Generation! with { Profile = RuntimeProfileId.Beta111 }
        });
        return new SearchContext(RuntimeProfileId.Beta111, authority.Character.CharacterKey, 10, authority,
            new("0.111.0", "0.111.0", "beta", "C-cache-fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
    }

    internal static int RunCombatProbabilityBench()
    {
        // A separate process owns each sample. The tiny query exercises the entry/JIT
        // path, but does not pretend to warm every branch of the measured workloads.
        var warmContext = CombatProbabilityFixture("DP_TINY_WARMUP_");
        ModelKey warmTarget = warmContext.Authority.EffectAuthority!.CharacterRewardPool![0].CardKey;
        var tiny = SearchSelectivityInput.From(SearchCompiler.Compile(SearchQuery.Empty with
        { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [warmTarget]) }, warmContext));
        long warmupBefore = CombatRewardProbabilityEstimator.ProfileProbabilityCacheDiagnostics.Computations;
        double? warmup = CombatRewardProbabilityEstimator.Estimate(tiny).Probability;
        double? warmupHit = CombatRewardProbabilityEstimator.Estimate(tiny).Probability;
        long warmupDelta = CombatRewardProbabilityEstimator.ProfileProbabilityCacheDiagnostics.Computations - warmupBefore;
        bool valid = warmup is > 0 && warmupHit == warmup && warmupDelta == 1;
        Console.WriteLine($"C_PROBABILITY_BENCH processId={Environment.ProcessId};runtime={Environment.Version};" +
            $"freshProcessRequired=true;order=fixed;tinyWarmupDifferentIdentity=true;tinyWarmupDP={warmupDelta};" +
            "jit=entry-and-tiny-C1-only;compileOutsideTimer=true;forcedGC=false;notSearchThroughput=true");

        // Keep CACHE_ and the complete 19-card A10 pool identical to the original
        // six-distinct-unordered cache fixture used by --predictability-math.
        var context = CombatProbabilityFixture("CACHE_");
        var pool = context.Authority.EffectAuthority!.CharacterRewardPool!;
        ModelKey[] targets = pool.Take(6).Select(c => c.CardKey).ToArray();
        ModelKey rareAttack = pool.First(c => c.Rarity == EffectCardRarity.Rare && c.CardType == EffectCardType.Attack).CardKey;
        ModelKey rarePower = pool.First(c => c.Rarity == EffectCardRarity.Rare && c.CardType == EffectCardType.Power).CardKey;
        var noDrop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null);
        SearchQuery WithRelic(ModelKey target, string relic) => SearchQuery.Empty with
        {
            CombatCardRewards = new(2, CombatRewardSequenceOrderMode.Ordered, [null, target]),
            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, [new("RELIC", relic)])
        };
        var cases = new (string Name, SearchQuery Query)[]
        {
            ("C6-distinct-unordered", SearchQuery.Empty with { CombatCardRewards = new(6,
                CombatRewardSequenceOrderMode.Unordered, targets.Select(k => (ModelKey?)k).ToArray()) }),
            ("C3-repeated-unordered", SearchQuery.Empty with { CombatCardRewards = new(3,
                CombatRewardSequenceOrderMode.Unordered, [targets[0], targets[0], targets[1]]) }),
            ("C2-repeated-short", SearchQuery.Empty with { CombatCardRewards = new(2,
                CombatRewardSequenceOrderMode.Unordered, [targets[0], targets[0]]) }),
            ("C3-P2-independent-windows", SearchQuery.Empty with { CombatCardRewards = new(3,
                CombatRewardSequenceOrderMode.Unordered, [targets[0], targets[1], null]),
                CombatPotionRewards = new(2, CombatRewardSequenceOrderMode.Unordered, [noDrop, noDrop]) }),
            ("Wheel-B2-rare-attack", WithRelic(rareAttack, "PRAYER_WHEEL")),
            ("Candy-B2-rare-power", WithRelic(rarePower, "LASTING_CANDY"))
        };
        foreach (var (name, query) in cases)
        {
            var input = SearchSelectivityInput.From(SearchCompiler.Compile(query, context));
            long before = CombatRewardProbabilityEstimator.ProfileProbabilityCacheDiagnostics.Computations;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var cold = CombatRewardProbabilityEstimator.Estimate(input);
            double coldMs = watch.Elapsed.TotalMilliseconds;
            long coldAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            long coldDelta = CombatRewardProbabilityEstimator.ProfileProbabilityCacheDiagnostics.Computations - before;
            allocated = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            var warm = CombatRewardProbabilityEstimator.Estimate(input);
            double warmMs = watch.Elapsed.TotalMilliseconds;
            long warmAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            long warmDelta = CombatRewardProbabilityEstimator.ProfileProbabilityCacheDiagnostics.Computations - before - coldDelta;
            bool pass = cold.IsPriced && cold.Probability is double p && double.IsFinite(p) && p is >= 0 and <= 1 &&
                warm.IsPriced && warm.Probability == cold.Probability && coldDelta == 1 && warmDelta == 0;
            valid &= pass;
            Console.WriteLine(FormattableString.Invariant($"C_PROBABILITY_CASE name={name};p={cold.Probability:G17};coldMs={coldMs:F3};warmMs={warmMs:F3};coldComputations={coldDelta};warmComputations={warmDelta};coldThreadAllocatedBytes={coldAllocated};warmThreadAllocatedBytes={warmAllocated};pass={pass}"));
            if (!pass) Console.Error.WriteLine($"C probability bench rejected sample {name}: {cold.EvidenceCode};{cold.Notes}");
        }
        Console.WriteLine($"C_PROBABILITY_BENCH_END cases={cases.Length};pass={valid};allocationScope=current-thread;" +
            "scalarCache=process-local;compareThreeFreshProcessesByCase=true");
        return valid ? 0 : 1;
    }

    private static void CheckCombatAssignmentHistories(Action<bool, string> check)
    {
        var method = typeof(CombatRewardProbabilityEstimator).GetMethod("AdvanceAssignmentStates", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Combat assignment transition");
        var parameters = method.GetParameters();
        if (parameters.Length is not (3 or 4)) throw new InvalidOperationException("Unexpected combat assignment transition signature");
        long histories = 0, rejected = 0;
        // Independent oracle: assign requirement identities directly to different
        // battles in a complete history. It neither stores subset sets nor prunes.
        static bool HasDistinctAssignment(int[] history, int requirements, int target = 0, int usedBattles = 0)
        {
            if (target == requirements) return true;
            for (int battle = 0; battle < history.Length; battle++)
                if ((usedBattles & (1 << battle)) == 0 && (history[battle] & (1 << target)) != 0 &&
                    HasDistinctAssignment(history, requirements, target + 1, usedBattles | (1 << battle))) return true;
            return false;
        }
        void Compare(int[] history, int requirements)
        {
            ulong states = 1;
            for (int battle = 0; battle < history.Length && states != 0; battle++)
            {
                int hitMask = history[battle];
                Func<int, bool> matches = target => (hitMask & (1 << target)) != 0;
                int remaining = history.Length - battle - 1;
                object[] arguments = parameters.Length == 3 ? [states, requirements, matches] :
                    parameters[2].ParameterType == typeof(int) ? [states, requirements, remaining, matches] :
                    [states, requirements, matches, remaining];
                states = (ulong)method.Invoke(null, arguments)!;
            }
            bool actual = (states & (1UL << ((1 << requirements) - 1))) != 0;
            bool expected = HasDistinctAssignment(history, requirements);
            histories++;
            if (!expected) rejected++;
            check(actual == expected, $"Distinct-battle history r={requirements};H={history.Length};hits={string.Join(',', history)}");
        }
        for (int requirements = 1; requirements <= 3; requirements++)
        for (int horizon = 1; horizon <= 4; horizon++)
        {
            int historyCount = 1 << (requirements * horizon);
            int mask = (1 << requirements) - 1;
            for (int encoded = 0; encoded < historyCount; encoded++)
                Compare(Enumerable.Range(0, horizon).Select(b => (encoded >> (b * requirements)) & mask).ToArray(), requirements);
        }
        // Six copies of one identity remain six obligations, even when every
        // successful battle matches all six predicates. Include a short window.
        foreach (int horizon in new[] { 5, 6 })
        for (int encoded = 0; encoded < 1 << horizon; encoded++)
            Compare(Enumerable.Range(0, horizon).Select(b => (encoded & (1 << b)) == 0 ? 0 : 63).ToArray(), 6);
        Compare([1, 2, 4, 8, 16, 32], 6);
        Compare([3, 3, 3, 12, 12, 48], 6); // Full union does not prove a distinct assignment.
        Console.WriteLine($"C_ASSIGNMENT_HISTORY_ORACLE histories={histories};rejectedHistories={rejected};" +
            "oracle=independent-injective-battle-enumeration;notProductionStateCounts=true");
    }

    private static void CheckCombatRewardCache(Action<bool, string> check)
    {
        var context = CombatProbabilityFixture("CACHE_");
        var authority = context.Authority;
        var effects = authority.EffectAuthority!;
        var world = authority.WorldAuthority!;
        var empty = ModelKeySetFilter.Empty;
        ModelKey[] targets = effects.CharacterRewardPool!.Take(6).Select(c => c.CardKey).ToArray();
        double Estimate(SearchQuery query, SearchContext? changed = null) => CombatRewardProbabilityEstimator.Estimate(
            SearchSelectivityInput.From(SearchCompiler.Compile(query, changed ?? context))).Probability ?? double.NaN;
        long Computations() => CombatRewardProbabilityEstimator.ProfileProbabilityCacheDiagnostics.Computations;
        void Miss(SearchQuery query, string name, SearchContext? changed = null)
        {
            long before = Computations();
            double value = Estimate(query, changed);
            check(double.IsFinite(value) && Computations() == before + 1, "C cache miss: " + name);
            check(Estimate(query, changed) == value && Computations() == before + 1, "C cache hit: " + name);
        }
        SearchContext WithEffects(NeowEffectAuthoritySnapshot replacement) => context with
        { Authority = authority.WithEffectAuthority(replacement) };
        SearchQuery WithRelic(SearchQuery query, string relic) => query with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, [new("RELIC", relic)])
        };

        var c6 = SearchQuery.Empty with
        { CombatCardRewards = new(6, CombatRewardSequenceOrderMode.Unordered, targets.Select(k => (ModelKey?)k).ToArray()) };
        // This fixed fixture records preview DP latency, not Search throughput.
        long coldBefore = Computations();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double cold = Estimate(c6);
        double coldMs = watch.Elapsed.TotalMilliseconds;
        check(double.IsFinite(cold) && Computations() == coldBefore + 1, "C6 fixture computes once");
        var act = world.Beta109Generation!.OrderedActCatalog.First();
        var bossEdit = c6 with { VariantBossBranches = [new(act.Act, act.ActKey,
            new([act.OrderedGenerateAllEncounters.First(e => e.RoomType == Beta109RoomType.Boss).EncounterKey], [], []), empty, false)] };
        watch.Restart();
        double warm = Estimate(bossEdit);
        double warmMs = watch.Elapsed.TotalMilliseconds;
        check(warm == cold && Computations() == coldBefore + 1, "Changing unrelated W/Boss reuses identical C6 scalar");
        check(Estimate(c6 with { OpeningRoute = new(BaseGameModelKeys.Relics.SilkenTress) }) == cold &&
            Computations() == coldBefore + 1, "Neutral opening does not recompute identical C6 DP");
        Console.WriteLine($"C probability cache fixed fixture: six distinct unordered targets; coldMs={coldMs:F3}; " +
            $"sameCChangedBossMs={warmMs:F3}; probability={cold:G17}; scalarReuse=true; not Search throughput.");

        var c1 = SearchQuery.Empty with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [targets[0]]) };
        Miss(c1, "shorter window");
        Miss(c1 with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [targets[1]]) }, "target identity");
        Miss(c1, "ascension", context with { Ascension = 0 });
        var c2 = c1 with { CombatCardRewards = new(2, CombatRewardSequenceOrderMode.Ordered, [targets[0], null]) };
        Miss(c2, "window with empty trailing slot");
        Miss(c2 with { CombatCardRewards = new(2, CombatRewardSequenceOrderMode.Ordered, [null, targets[0]]) }, "ordered slot");
        Miss(c2 with { CombatCardRewards = new(2, CombatRewardSequenceOrderMode.Unordered, [targets[0], null]) }, "unordered window");
        Miss(c2 with { CombatCardRewards = new(2, CombatRewardSequenceOrderMode.Unordered, [targets[0], targets[0]]) }, "duplicate assignments");
        Miss(c2 with { CombatCardRewards = new(2, CombatRewardSequenceOrderMode.Unordered, [targets[1], targets[0]]) }, "target mapping");
        foreach (string relic in new[] { "PRAYER_WHEEL", "LASTING_CANDY", "AMETHYST_AUBERGINE" })
            Miss(WithRelic(c2, relic), "held profile " + relic);
        foreach (var replacement in new[]
        {
            effects.CharacterRewardPool!.Select((c, i) => i == 0 ? c with { Rarity = EffectCardRarity.Uncommon } : c).ToArray(),
            effects.CharacterRewardPool!.Select((c, i) => i == 0 ? c with { CardType = EffectCardType.Power } : c).ToArray(),
            effects.CharacterRewardPool!.Select((c, i) => i == 1 ? c with { CardType = EffectCardType.Power } : c).ToArray(),
            effects.CharacterRewardPool!.Skip(1).ToArray(),
            effects.CharacterRewardPool!.Append(effects.CharacterRewardPool![0] with
                { CardKey = new("CARD", "mod.example:|CacheExtra"), PoolOrder = 100 }).ToArray()
        }) Miss(c1, "actual card pool despite unchanged snapshot fingerprint", WithEffects(effects with { CharacterRewardPool = replacement }));

        var potionPool = effects.PotionPool!;
        var potionKey = potionPool.First().PotionKey;
        var potion = SearchQuery.Empty with { CombatPotionRewards = new(1, CombatRewardSequenceOrderMode.Ordered,
            [new(CombatPotionSlotRequirement.DropSpecific, potionKey)]) };
        Miss(potion, "potion target");
        Miss(potion, "potion rarity", WithEffects(effects with { PotionPool = potionPool.Select((p, i) =>
            i == 0 ? p with { Rarity = EffectPotionRarity.Uncommon } :
            i == 1 ? p with { Rarity = EffectPotionRarity.Common } : p).ToArray() }));
        Miss(potion, "potion counts", WithEffects(effects with { PotionPool = potionPool.Append(
            potionPool[0] with { PotionKey = new("POTION", "mod.example:|CacheExtra"), PoolOrder = 100 }).ToArray() }));
        Miss(WithRelic(potion, "WHITE_BEAST_STATUE"), "forced potion profile");
        var noDrop = potion with { CombatPotionRewards = new(1, CombatRewardSequenceOrderMode.Ordered,
            [new(CombatPotionSlotRequirement.NoDrop, null)]) };
        Miss(noDrop, "potion requirement");
        check(Math.Abs(Estimate(noDrop) - .6) < 1e-14 && Estimate(WithRelic(noDrop, "WHITE_BEAST_STATUE")) == 0,
            "Cached potion math retains known no-drop vectors");
        Miss(c2 with { CombatPotionRewards = new(1, CombatRewardSequenceOrderMode.Unordered,
            [new(CombatPotionSlotRequirement.DropSpecific, potionKey)]) }, "independent potion window 1");
        Miss(c2 with { CombatPotionRewards = new(2, CombatRewardSequenceOrderMode.Unordered,
            [new(CombatPotionSlotRequirement.DropSpecific, potionKey), new(CombatPotionSlotRequirement.Neutral, null)]) }, "independent potion window 2");

        foreach (var filter in new[] { new ModelKeySetFilter([targets[0], targets[1]], [], []),
            new ModelKeySetFilter([], [targets[0], targets[1]], []), new ModelKeySetFilter([], [], [targets[0], targets[1]]) })
        foreach (int battle in new[] { 0, 1 })
            Miss(SearchQuery.Empty with { LegacyCombatRewardConstraints = [new(battle, filter,
                NormalCombatPotionRequirement.Any, empty, null, null)] }, "legacy any/all/ban and battle window");

        // Warm concurrent readers must neither mutate retained keys nor rerun DP.
        double parallelExpected = Estimate(c1);
        var parallelInput = SearchSelectivityInput.From(SearchCompiler.Compile(c1, context));
        long parallelBefore = Computations();
        var concurrent = new double?[16];
        Parallel.For(0, concurrent.Length, i => concurrent[i] = CombatRewardProbabilityEstimator.Estimate(parallelInput).Probability);
        check(concurrent.All(p => p == parallelExpected) && Computations() == parallelBefore, "Concurrent scalar cache reads");

        ModelKey hugeKey = new("CARD", new string('X', 20_000));
        var hugeContext = WithEffects(effects with { CharacterRewardPool = effects.CharacterRewardPool!.Select((c, i) =>
            i == 0 ? c with { CardKey = hugeKey } : c).ToArray() });
        var hugeQuery = c1 with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [hugeKey]) };
        long hugeBefore = Computations();
        check(Estimate(hugeQuery, hugeContext) == parallelExpected && Estimate(hugeQuery, hugeContext) == parallelExpected &&
            Computations() == hugeBefore + 2, "Oversized keys bypass retention without changing numerical truth");
        for (int gold = 40; gold < 76; gold++)
            Miss(SearchQuery.Empty with { LegacyCombatRewardConstraints = [new(1, empty,
                NormalCombatPotionRequirement.Any, empty, gold, null)] }, "gold bound " + gold);
        check(CombatRewardProbabilityEstimator.ProfileProbabilityCacheDiagnostics.Entries == 32, "Scalar cache retains at most 32 entries");
        Miss(c1, "evicted input recomputes correctly");
    }
}
