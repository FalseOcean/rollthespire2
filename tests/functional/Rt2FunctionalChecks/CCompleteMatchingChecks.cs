using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static class CCompleteMatchingChecks
{
    internal sealed record MatchingCase(string Name, ExactSearchExecutionRequest Request);

    // Reusable by the existing Host: supply its captured solo/personal context.
    internal static IReadOnlyList<MatchingCase> Cases(SearchContext context)
    {
        var pool = context.Authority.EffectAuthority!.CharacterRewardPool!;
        var common = pool.Where(c => c.Rarity == EffectCardRarity.Common && (context.Authority.PlayersCount > 1 || !c.IsMultiplayerOnly)).Select(c => c.CardKey).Distinct().Take(3).ToArray();
        if (common.Length != 3) throw new InvalidOperationException("C checks require three captured common cards");
        ModelKey a = common[0], b = common[1], third = common[2];
        ModelKey potion = context.Authority.EffectAuthority!.PotionPool!.First(p => context.Authority.PlayersCount > 1 || !p.IsMultiplayerOnly).PotionKey;
        var noDrop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null);
        var neutral = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null);
        SearchQuery Cards(int h, CombatRewardSequenceOrderMode mode, params ModelKey?[] slots) =>
            SearchQuery.Empty with { CombatCardRewards = new(h, mode, slots) };
        var queries = new (string Name, SearchQuery Query)[]
        {
            ("Ordered", Cards(3, CombatRewardSequenceOrderMode.Ordered, a, null, b)),
            ("Distinct", Cards(3, CombatRewardSequenceOrderMode.Unordered, a, b, third)),
            ("Repeated", Cards(3, CombatRewardSequenceOrderMode.Unordered, a, a, a)),
            ("Short2", Cards(2, CombatRewardSequenceOrderMode.Unordered, a, null)),
            ("Card2Potion6", Cards(2, CombatRewardSequenceOrderMode.Unordered, a, null) with
                { CombatPotionRewards = new(6, CombatRewardSequenceOrderMode.Ordered, [neutral, neutral, neutral, neutral, neutral, noDrop]) }),
            ("Card6Potion2", Cards(6, CombatRewardSequenceOrderMode.Ordered, null, null, null, null, null, a) with
                { CombatPotionRewards = new(2, CombatRewardSequenceOrderMode.Unordered, [noDrop, noDrop]) }),
            ("MixedPotionIdentity", Cards(3, CombatRewardSequenceOrderMode.Unordered, a, b, null) with
                { CombatPotionRewards = new(2, CombatRewardSequenceOrderMode.Unordered,
                    [new(CombatPotionSlotRequirement.DropAny, null), new(CombatPotionSlotRequirement.DropSpecific, potion)]) }),
            ("SixRepeated", Cards(6, CombatRewardSequenceOrderMode.Unordered, a, a, a, a, a, a))
        };
        return queries.Select(item => new MatchingCase(item.Name, Request(item.Query, context))).ToArray();
    }

    internal static ExactSearchExecutionRequest Request(SearchQuery query, SearchContext context)
    {
        var compiled = context.Authority.PlayersCount > 1
            ? SearchCompiler.CompilePlayer(query, context)
            : SearchCompiler.Compile(query, context);
        return new(compiled, new("000000000000", 1024, 1, 1), "000000000000", 1024,
            CompiledSearchEvaluationProjector.Project(compiled).Evaluation, CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint);
    }

    internal static int Run(int count = 128, bool exportGpu = false)
    {
        int checks = 0, strictWitnesses = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException(label); }
        // A direct old-subset enumeration is an independent oracle for the bit
        // operator. Singletons plus mixed states cover the OR-linear transition.
        for (int r = 1; r <= 6; r++)
        {
            ulong valid = r == 6 ? ulong.MaxValue : (1UL << (1 << r)) - 1;
            var states = Enumerable.Range(0, 1 << r).Select(s => 1UL << s).Append(valid).Append(0xA55AA55AA55AA55AUL & valid);
            foreach (ulong prior in states)
            for (uint hits = 0; hits < (1u << r); hits++)
            {
                ulong expected = prior;
                for (int subset = 0; subset < (1 << r); subset++)
                for (int j = 0; j < r; j++)
                    if ((prior & (1UL << subset)) != 0 && (subset & (1 << j)) == 0 && (hits & (1u << j)) != 0)
                        expected |= 1UL << (subset | (1 << j));
                Check(Beta110CombatRewardFastStage.AdvanceAssignmentStates(prior, hits, r) == expected, "Old-state bit transition");
            }
        }
        var exports = new List<object>();
        foreach (int slot in new[] { -1, 0, 3 })
        {
            var context = Fixture(slot);
            var cases = Cases(context);
            ModelKey single = context.Authority.EffectAuthority!.CharacterRewardPool![0].CardKey;
            foreach (int horizon in new[] { 3, 6 })
            {
                var slots = Enumerable.Repeat<ModelKey?>(null, horizon).ToArray(); slots[0] = single;
                var equivalent = new CombatRewardReplay(Request(SearchQuery.Empty with { CombatCardRewards =
                    new(horizon, CombatRewardSequenceOrderMode.Unordered, slots) }, context));
                Check(equivalent.Plan.CardAssignmentTargets.Length == (slot < 0 ? 0 : 1), "Solo scalar elision preserves party single-target arrays");
                Check(!equivalent.Plan.NeedsCompleteMatchingReference && equivalent.Plan.Predicates.All(p => p.AnyBattleWindow == 0),
                    "Equivalent single-target full-window body preserves reference and predicate encoding");
                Check(FamilyCanonicalCostShape.CombatCostStructureVersion(equivalent.Plan) == "20261021.v1",
                    "Equivalent single-target full-window canonical structure remains v1");
            }
            foreach (var fixture in cases)
            {
                var request = fixture.Request;
                var replay = new CombatRewardReplay(request);
                var gpu = CombatRewardGpuPlan.TryCreate(request, replay, out string issue) ?? throw new Exception(issue);
                var q = request.CompiledSearch.NormalizedQuery;
                Check(replay.Plan.MaximumBattleOrdinal == Math.Max(q.CombatCardRewards?.Count ?? 0, q.CombatPotionRewards?.Count ?? 0), "Typed horizon " + fixture.Name);
                if (fixture.Name != "MixedPotionIdentity") Check(gpu.UsesHotLoop, "Complete assignment retains hot loop " + fixture.Name);
                else Check(!gpu.UsesHotLoop, "Previously unvalidated unordered potion identity stays generic");
                Check(gpu.ShaderSource().Contains("#define RT2_C_ASSIGNMENT " + (replay.Plan.HasDistinctBattleAssignments ? "1" : "0"), StringComparison.Ordinal),
                    "Only active assignment domains compile the integer matching state");
                if (slot < 0 && fixture.Name == "Short2")
                    Check(!replay.Plan.HasDistinctBattleAssignments && replay.Plan.NeedsCompleteMatchingReference,
                        "Short scalar window needs a new reference without unnecessary assignment state");
                var expectedOrdinals = new List<uint>();
                var oldOrdinals = new List<uint>();
                for (uint ordinal = 0; ordinal < count; ordinal++)
                {
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                    var battles = CoreBattles(context, root, ordinal);
                    bool expected = ExactPositiveOracle(q, battles);
                    bool actual = replay.Matches(root);
                    bool old = OldExistentialOracle(q, battles);
                    Check(actual == expected, $"Independent Core + injective matcher slot={slot};case={fixture.Name};ordinal={ordinal}");
                    if (old && !expected) strictWitnesses++;
                    if (expected) expectedOrdinals.Add(ordinal);
                    if (old) oldOrdinals.Add(ordinal);
                }
                if (slot == -1)
                {
                    var banned = q with { CombatCardExclusion = new(6, [q.CombatCardRewards!.Slots.First(k => k.HasValue)!.Value]) };
                    var withExclusion = new CombatRewardReplay(Request(banned, context));
                    Check(withExclusion.Plan.Fingerprint == replay.Plan.Fingerprint, "Exact-only exclusion does not alter optimized C");
                    Check(withExclusion.Plan.MaximumBattleOrdinal == replay.Plan.MaximumBattleOrdinal, "Exclusion does not extend generation");
                }
                exports.Add(new { name = $"P{slot}.{fixture.Name}", query = q, expected = expectedOrdinals, oldExistentialExpected = oldOrdinals, beforeExpected = slot < 0 ? oldOrdinals : expectedOrdinals,
                    gpu.Meta, gpu.Pools, gpu.Ids, gpu.Predicates, gpu.Targets, gpu.HotLoop, routes = gpu.Routes,
                    shader = gpu.ShaderSource(), integerShader = (gpu with { NumericalVariant = GpuNumericalVariant.IntegerRne }).ShaderSource(),
                    forcedGenericShader = gpu.ShaderSource(forceGeneric: true) });
            }
            var onlyNegative = Request(SearchQuery.Empty with { CombatCardExclusion = new(6,
                [context.Authority.EffectAuthority!.CharacterRewardPool![0].CardKey]) }, context);
            Check(!(slot < 0 ? CombatRewardFamily.TryCreate(onlyNegative, out _) : CombatRewardFamily.TryCreateParty(onlyNegative, 0, 0, out _)),
                "Negative-only remains Exact-only");
        }
        Check(strictWitnesses > 0, "Fixtures include real histories rejected by complete matching but accepted by old existential rows");
        if (exportGpu)
        {
            string directory = Path.GetFullPath(".validation/c-complete-matching");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "vectors.json"), System.Text.Json.JsonSerializer.Serialize(exports));
            Console.WriteLine("C_COMPLETE_MATCHING_VECTORS " + directory);
        }
        Console.WriteLine($"C_COMPLETE_MATCHING_PASS checks={checks};strictWitnesses={strictWitnesses};cases={exports.Count};coreOracle=true;nativeRun=false");
        return 0;
    }

    private static SearchContext Fixture(int slot)
    {
        int players = slot < 0 ? 1 : 4, owner = Math.Max(0, slot);
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect, 10, owner, players);
        authority = authority.WithEffectAuthority(authority.EffectAuthority! with { CharacterRewardIdentityPoolExact = true,
            CharacterRewardPool = authority.EffectAuthority!.CharacterRewardPool!.Select(c => c with { CatalogProfileId = RuntimeProfileId.Beta111 }).ToArray() });
        var world = Beta109WorldFixtureFactory.Create();
        authority = authority.WithWorldAuthority(world with { CapturedProfileId = RuntimeProfileId.Beta111,
            Beta109Generation = world.Beta109Generation! with { Profile = RuntimeProfileId.Beta111,
                IsMultiplayer = players > 1, PlayerCount = players, PersonalPlayerSlot = owner,
                GameMode = players > 1 ? WorldGameMode.Multiplayer : WorldGameMode.Singleplayer } });
        return new(RuntimeProfileId.Beta111, authority.Character.CharacterKey, 10, authority,
            new("0.111.0", "0.111.0", "beta", "C-complete-fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
    }

    private static IReadOnlyList<NormalCombatRewardBattleResult> CoreBattles(SearchContext context, ulong root, uint ordinal)
    {
        // Lava Rock is the existing independently generated neutral continuation;
        // generation comes from Core, not from the fast loop under test.
        var projection = Beta111NeowEffectProjector.ProjectFromRootHash(BaseGameModelKeys.Relics.LavaRock,
            PredictionPrecision.Exact, context.Authority, root);
        var choice = new NeowChoiceResult(0, BaseGameModelKeys.Relics.LavaRock, PredictionPrecision.Exact,
            projection.Capability, projection.Precision, projection.EffectGroups, projection.Warnings, "fixture", projection.EvidenceCode,
            new(true, null, "sts2", SourceAuthority.AuditedStaticExact, IdentityResolutionStatus.Exact),
            OpeningRewardContinuations: projection.OpeningRewardContinuations);
        return NormalCombatRewardSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, root,
            Beta110SeedCodec.FormatOrdinal(ordinal), context.Authority.PlayerSlotIndex, 10, [choice],
            context.Authority.EffectAuthority, context.Authority.WorldAuthority,
            NormalCombatRewardProjectionRequest.RichAnalysis with { BattleCount = 6 }).Routes.Single().Battles;
    }

    internal static bool ExactPositiveOracle(SearchQuery query, IReadOnlyList<NormalCombatRewardBattleResult> battles)
    {
        if (query.CombatCardRewards is { IsEmpty: false } cards && !Match(cards.Slots.Where(k => k.HasValue).Select(k => k!.Value).ToArray(),
            cards.OrderMode, cards.Count, (key, battle) => CardKeys(battle).Contains(key),
            cards.Slots.Select((key, i) => !key.HasValue || CardKeys(battles[i]).Contains(key.Value)).All(x => x), battles)) return false;
        if (query.CombatPotionRewards is { IsEmpty: false } potions && !Match(potions.Slots.Where(p => !p.IsNeutral).ToArray(),
            potions.OrderMode, potions.Count, PotionMatches,
            potions.Slots.Select((p, i) => p.IsNeutral || PotionMatches(p, battles[i])).All(x => x), battles)) return false;
        return true;
    }

    private static bool Match<T>(IReadOnlyList<T> requirements, CombatRewardSequenceOrderMode mode, int window,
        Func<T, NormalCombatRewardBattleResult, bool> matches, bool ordered,
        IReadOnlyList<NormalCombatRewardBattleResult> battles)
    {
        if (mode == CombatRewardSequenceOrderMode.Ordered) return ordered;
        bool Assign(int target, int used)
        {
            if (target == requirements.Count) return true;
            for (int b = 0; b < window; b++)
                if ((used & (1 << b)) == 0 && matches(requirements[target], battles[b]) && Assign(target + 1, used | (1 << b))) return true;
            return false;
        }
        return Assign(0, 0);
    }

    private static IEnumerable<ModelKey> CardKeys(NormalCombatRewardBattleResult battle) =>
        battle.CardRewards.Count > 0 ? battle.CardRewards.SelectMany(r => r.Cards).Select(c => c.CardKey) : battle.Cards.Select(c => c.CardKey);
    private static bool PotionMatches(CombatPotionRewardSlotSearchCondition slot, NormalCombatRewardBattleResult battle) => slot.Requirement switch
    {
        CombatPotionSlotRequirement.Neutral => true,
        CombatPotionSlotRequirement.NoDrop => !battle.Potion.Generated,
        CombatPotionSlotRequirement.DropAny => battle.Potion.Generated,
        CombatPotionSlotRequirement.DropSpecific => battle.Potion.Generated && battle.Potion.PotionKey == slot.PotionKey,
        _ => false
    };

    private static bool OldExistentialOracle(SearchQuery query, IReadOnlyList<NormalCombatRewardBattleResult> battles)
    {
        int window = Math.Max(query.CombatCardRewards?.Count ?? 0, query.CombatPotionRewards?.Count ?? 0);
        if (query.CombatCardRewards is { Count: > 1, OrderMode: CombatRewardSequenceOrderMode.Unordered } ||
            query.CombatPotionRewards is { Count: > 1, OrderMode: CombatRewardSequenceOrderMode.Unordered }) window = Math.Max(3, window);
        if (query.CombatCardRewards is { IsEmpty: false } cards && cards.Slots.Select((key, i) => !key.HasValue ||
            (cards.OrderMode == CombatRewardSequenceOrderMode.Ordered || cards.Count == 1 ? CardKeys(battles[i]).Contains(key.Value) :
                battles.Take(window).Any(b => CardKeys(b).Contains(key.Value)))).Any(x => !x)) return false;
        if (query.CombatPotionRewards is { IsEmpty: false } potions && potions.Slots.Select((slot, i) => slot.IsNeutral ||
            (potions.OrderMode == CombatRewardSequenceOrderMode.Ordered || potions.Count == 1 ? PotionMatches(slot, battles[i]) :
                battles.Take(window).Any(b => PotionMatches(slot, b)))).Any(x => !x)) return false;
        return true;
    }
}
