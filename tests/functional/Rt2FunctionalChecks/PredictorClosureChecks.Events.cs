using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using NativeEvents = MegaCrit.Sts2.Core.Models.Events;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static partial class PredictorClosureChecks
{
    internal static int CheckEventResults()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
        using var captured = JsonDocument.Parse(File.ReadAllText("docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json"));
        var targets = captured.RootElement.GetProperty("Evidence")[0].GetProperty("Targets")
            .Deserialize<MorphicGroveTarget[]>(ProductionSearchReplay.JsonOptions)!
            .Select((t, i) => t with { InstanceId = "basic:" + i }).ToArray();
        var premises = new MorphicGrovePremises("Authored initial Basics at event entry", true, false, null);
        EventResultConditionKind[] kinds = [EventResultConditionKind.MorphicGroveGroupInitialBasicsContains,
            EventResultConditionKind.SymbioteInitialBasicTransform, EventResultConditionKind.AromaOfChaosInitialBasicTransform,
            EventResultConditionKind.WhisperingHollowInitialBasicTransform, EventResultConditionKind.TrialNondescriptInitialBasicsContains];
        var trialCases = new HashSet<int>();
        foreach (int players in new[] { 1, 4 })
        for (int slot = 0; slot < players; slot++)
        {
            var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 10, slot, players);
            for (int seed = 0; seed < 32; seed++)
            {
                ulong root = Beta111Profile.Instance.ComputeRootSeed("EVENT" + seed);
                Rng Native(string entry, bool shared = false) => new(unchecked(root +
                    (shared ? 0UL : (ulong)slot) + StringHelper.GetDeterministicHashCode(entry)));
                foreach (var kind in kinds)
                {
                    string entry = EventResultTransformSemantics.Entry(kind);
                    int count = kind is EventResultConditionKind.MorphicGroveGroupInitialBasicsContains or
                        EventResultConditionKind.TrialNondescriptInitialBasicsContains ? 2 : 1;
                    Check(EventResultTransformSemantics.DrawCount(kind) == count, "Committed event draw count: " + entry);
                    foreach (int poolSize in new[] { 1, 7, targets[0].OrderedSourceCandidates!.Count })
                    {
                        var pool = targets[0].OrderedSourceCandidates!.Take(poolSize).ToArray();
                        var scenario = new MorphicGroveScenario(authority, premises, targets.Take(count)
                            .Select(t => t with { OrderedSourceCandidates = pool }).ToArray());
                        var query = new EventResultSearchCondition(kind, pool[0].CardKey) { MorphicGroveScenario = scenario };
                        var observed = Beta111EventTransformProjector.Project(root, slot, entry, pool);
                        var rng = Native(entry, entry == "MORPHIC_GROVE");
                        if (entry == "WHISPERING_HOLLOW") rng.NextInt(-9, 10);
                        bool transform = entry != "TRIAL" || rng.NextInt(3) == 2;
                        var expected = transform ? Enumerable.Range(0, count).Select(_ => pool[rng.NextInt(pool.Length)]).ToArray() : [];
                        Check(observed.SequenceEqual(expected), "Information transforms vs native RNG: " + entry);
                        if (slot == 0 && kind is EventResultConditionKind.AromaOfChaosInitialBasicTransform or
                            EventResultConditionKind.WhisperingHollowInitialBasicTransform)
                        {
                            var source = kind == EventResultConditionKind.AromaOfChaosInitialBasicTransform
                                ? SingleBasicTransformEvent.AromaOfChaos : SingleBasicTransformEvent.WhisperingHollow;
                            var target = targets[0] with { OrderedSourceCandidates = pool };
                            var raw = Beta111SingleBasicTransformProjector.Project(root, slot, source, premises, target);
                            Check(raw.RawReplacement == expected[0] && raw.RawPrecision == PredictionPrecision.Exact &&
                                raw.FinalReplacement is null && raw.FloorAddedToDeck is null && raw.FinalPrecision == PredictionPrecision.Unknown,
                                "Single transform raw/native RNG with final hook uncertainty: " + entry);
                            var final = Beta111SingleBasicTransformProjector.Project(root, slot, source,
                                premises with { NoDeckMutationHooks = true, EntryFloor = 17 }, target);
                            Check(final.FinalReplacement == expected[0] && final.FinalPrecision == PredictionPrecision.Exact &&
                                final.FloorAddedToDeck == 17, "Single transform final metadata with closed hooks: " + entry);
                        }
                        Check(observed.SequenceEqual(EventResultTransformSemantics.Project(root, query)!), "E/information observations: " + entry);
                        foreach (var target in pool.Select(c => c.CardKey))
                            Check(EventResultTransformSemantics.Evaluate(root, query with { TargetKey = target }) ==
                                (observed.Any(c => c.CardKey == target) ? MorphicGrovePredicateResult.Match : MorphicGrovePredicateResult.NoMatch),
                                "E containment agrees with displayed replacements");
                    }
                }
                int trial = Beta111TrialTinkerProjector.TrialCase(root, slot);
                trialCases.Add(trial);
                Check(trial == Native("TRIAL").NextInt(3), "Displayed Trial case vs native RNG");
                var options = Beta111TrialTinkerProjector.TinkerOptions(root, slot);
                var typeRng = Native("TINKER_TIME");
                var nativeTypes = new[] { 0, 1, 2 }.TakeRandom(2, typeRng).ToArray();
                Check(options.Select(o => o.CardType).SequenceEqual(nativeTypes), "Tinker offered types/order vs native TakeRandom");
                foreach (var option in options)
                {
                    var branch = Native("TINKER_TIME");
                    _ = new[] { 0, 1, 2 }.TakeRandom(2, branch).ToArray();
                    int type = option.CardType;
                    var nativeRiders = new[] { type * 3, type * 3 + 1, type * 3 + 2 }.TakeRandom(2, branch).ToArray();
                    Check(option.Riders.SequenceEqual(nativeRiders), "Each Tinker branch resumes at post-type RNG");
                }
                for (int type = 0; type < 3; type++)
                {
                    var option = options.SingleOrDefault(o => o.CardType == type);
                    Check(Beta111TrialTinkerProjector.TinkerContains(root, slot, type, null) == (option is not null), "E Tinker type matches display");
                    for (int rider = type * 3; rider < type * 3 + 3; rider++)
                        Check(Beta111TrialTinkerProjector.TinkerContains(root, slot, type, rider) == (option?.Riders.Contains(rider) == true),
                            "E Tinker rider matches displayed alternative");
                }
            }
        }
        Check(trialCases.Count == 3, "All Trial branches exercised");
        foreach (var source in Enum.GetValues<SingleBasicTransformEvent>())
        {
            foreach (bool missingPool in new[] { false, true })
            {
                var unknown = Beta111SingleBasicTransformProjector.Project(0, 0, source,
                    premises with { VanillaLocalDependenciesBound = missingPool, NoDeckMutationHooks = true, EntryFloor = 17 },
                    missingPool ? targets[0] with { OrderedSourceCandidates = null } : targets[0]);
                Check(unknown.RawReplacement is null && unknown.FinalReplacement is null && unknown.FloorAddedToDeck is null &&
                    unknown.RawPrecision == PredictionPrecision.Unknown && unknown.FinalPrecision == PredictionPrecision.Unknown,
                    "Single transform missing pool/local dependencies remain Unknown: " + source);
            }
            bool emptyRejected = false;
            try
            {
                Beta111SingleBasicTransformProjector.Project(0, 0, source, premises,
                    targets[0] with { OrderedSourceCandidates = [] });
            }
            catch (InvalidOperationException ex) when (ex.Message == "SingleBasicTransform.EmptySourcePool") { emptyRejected = true; }
            Check(emptyRejected, "Single transform retains its empty-pool failure: " + source);
        }
        foreach (string language in new[] { "zh", "en" })
        {
            var text = JsonUiTextProvider.CreatePredictorUi13(language);
            foreach (string suffix in new[] { "trial_scope", "tinker_scope", "choose_rider", "transform_number", "transform_scope", "transform_unavailable" })
            {
                string key = "predictor.event." + suffix;
                Check(text.Get(key) != text.Get("__missing_event_key__") && text.Get(key).Length > 0, "Localized event result: " + language + "/" + suffix);
            }
        }
        checks += CheckStableEventOptionResults();
        Console.WriteLine($"EventPrediction checks={checks};PASS;nativeRng+TakeRandom+CalculateVars+StableShuffle;solo+fourSlots;full+restrictedPools;Trial+Tinker+stableOptionResults;zh+en");
        return 0;
    }

    private static int CheckStableEventOptionResults()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
        // Only numeric vars are injected into detached native objects, so the real
        // CalculateVars methods run without ModelDb, localization, or a live RunState.
        var fixtures = new (string Entry, Type NativeType, bool Shared, DynamicVar[] Vars,
            (string Option, string Var, int Min, int Max)[] Expected)[]
        {
            ("DENSE_VEGETATION", typeof(NativeEvents.DenseVegetation), true,
                [new GoldVar(0), new HealVar(0m)], [("TRUDGE_ON", "Gold", 61, 99)]),
            ("JUNGLE_MAZE_ADVENTURE", typeof(NativeEvents.JungleMazeAdventure), true,
                [new DynamicVar("SoloGold", 150m), new DynamicVar("JoinForcesGold", 50m)],
                [("SOLO_QUEST", "SoloGold", 135, 165), ("JOIN_FORCES", "JoinForcesGold", 35, 65)]),
            ("LOST_WISP", typeof(NativeEvents.LostWisp), false,
                [new GoldVar(60)], [("SEARCH", "Gold", 45, 75)]),
            ("LUMINOUS_CHOIR", typeof(NativeEvents.LuminousChoir), false,
                [new GoldVar(149)], [("OFFER_TRIBUTE", "Gold", 100, 149)]),
            ("CRYSTAL_SPHERE", typeof(NativeEvents.CrystalSphere), false,
                [new DynamicVar("UncoverFutureCost", 50m)], [("UNCOVER_FUTURE", "UncoverFutureCost", 51, 99)]),
            ("SUNKEN_TREASURY", typeof(NativeEvents.SunkenTreasury), false,
                [new DynamicVar("SmallChestGold", 60m), new DynamicVar("LargeChestGold", 333m)],
                [("FIRST_CHEST", "SmallChestGold", 52, 67), ("SECOND_CHEST", "LargeChestGold", 303, 363)]),
            ("SUNKEN_STATUE", typeof(NativeEvents.SunkenStatue), false,
                [new GoldVar(111)], [("DIVE_INTO_WATER", "Gold", 101, 121)]),
            ("THIS_OR_THAT", typeof(NativeEvents.ThisOrThat), false,
                [new GoldVar(0)], [("PLAIN", "Gold", 41, 68)]),
            ("WHISPERING_HOLLOW", typeof(NativeEvents.WhisperingHollow), false,
                [new GoldVar(35)], [("GOLD", "Gold", 26, 44)])
        };
        FieldInfo varsField = typeof(EventModel).GetField("_dynamicVars", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo rngField = typeof(EventModel).GetField("<Rng>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ulong[] roots = Enumerable.Range(0, 32).Select(seed => Beta111Profile.Instance.ComputeRootSeed("EVENT" + seed))
            .Concat([0UL, ulong.MaxValue, 1UL << 63]).ToArray();
        foreach (var fixture in fixtures)
        {
            var native = (EventModel)RuntimeHelpers.GetUninitializedObject(fixture.NativeType);
            Check(native.IsShared == fixture.Shared, "Native event shared contract: " + fixture.Entry);
            foreach (ulong root in roots)
            for (int slot = 0; slot < 4; slot++)
            {
                // Fresh numeric vars preserve native GoldVar casts, while avoiding
                // CanonicalVars entries that resolve localized cards/relics.
                DynamicVar[] vars = fixture.Vars.Select(v => v switch
                {
                    GoldVar => (DynamicVar)new GoldVar(v.IntValue),
                    HealVar => new HealVar(v.BaseValue),
                    _ => new DynamicVar(v.Name, v.BaseValue)
                }).ToArray();
                varsField.SetValue(native, new DynamicVarSet(vars));
                var rng = new Rng(unchecked(root + (native.IsShared ? 0UL : (ulong)slot) +
                    StringHelper.GetDeterministicHashCode(fixture.Entry)));
                rngField.SetValue(native, rng);
                native.CalculateVars();
                string rngBefore = JsonSerializer.Serialize(rng.ToSerializable());
                decimal[] varsBefore = vars.Select(v => v.BaseValue).ToArray();
                var actual = Beta111EventOptionResultProjector.Project(root, slot, fixture.Entry);
                Check(actual.Count == fixture.Expected.Length, "Stable event option count: " + fixture.Entry);
                for (int option = 0; option < actual.Count; option++)
                {
                    var expected = fixture.Expected[option];
                    Check(actual[option].OptionKey == expected.Option && actual[option].Relics.Count == 0 &&
                        actual[option].GoldAmount == native.DynamicVars[expected.Var].IntValue,
                        "Stable amount vs native CalculateVars: " + fixture.Entry + "/" + expected.Option);
                    Check(actual[option].GoldAmount >= expected.Min && actual[option].GoldAmount <= expected.Max,
                        "Stable base amount bounds: " + fixture.Entry);
                }
                Check(vars.Select(v => v.BaseValue).SequenceEqual(varsBefore) &&
                    JsonSerializer.Serialize(rng.ToSerializable()) == rngBefore,
                    "Projection preserves source event vars and RNG: " + fixture.Entry);
                if (fixture.Shared)
                    Check(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(
                        Beta111EventOptionResultProjector.Project(root, 0, fixture.Entry)),
                        "Shared amount ignores player slot: " + fixture.Entry);
            }
        }

        // Native DollChoice.CompareTo delegates to RelicModel/ModelId ordinal
        // comparison. RANDOM retains the authored order; each alternative gets
        // a fresh event-local RNG and TAKE_SOME_TIME uses native StableShuffle.
        ModelId[] authored = [new("RELIC", "DAUGHTER_OF_THE_WIND"), new("RELIC", "MR_STRUGGLES"), new("RELIC", "BING_BONG")];
        string[] sourceBefore = authored.Select(id => id.ToString()).ToArray();
        var randomSeen = new HashSet<ModelKey>();
        var pairsSeen = new HashSet<string>();
        foreach (ulong root in roots)
        for (int slot = 0; slot < 4; slot++)
        {
            Rng Native() => new(unchecked(root + (ulong)slot + StringHelper.GetDeterministicHashCode("DOLL_ROOM")));
            ModelId random = Native().NextItem(authored)!;
            ModelId[] offered = authored.ToList().StableShuffle(Native()).Take(2).ToArray();
            var actual = Beta111EventOptionResultProjector.Project(root, slot, "DOLL_ROOM");
            ModelKey Key(ModelId id) => new(id.Category, id.Entry);
            Check(actual.Count == 2 && actual[0].OptionKey == "RANDOM" && actual[0].GoldAmount is null &&
                actual[0].Relics.SequenceEqual([Key(random)]), "Doll free branch vs native NextItem from fresh RNG");
            Check(actual[1].OptionKey == "TAKE_SOME_TIME" && actual[1].GoldAmount is null &&
                actual[1].Relics.SequenceEqual(offered.Select(Key)), "Doll paid pair/order vs native StableShuffle from fresh RNG");
            Check(actual[1].Relics.Distinct().Count() == 2 && authored.Select(id => id.ToString()).SequenceEqual(sourceBefore),
                "Doll pair unique and source pool unchanged");
            Check(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(
                Beta111EventOptionResultProjector.Project(root, slot, "DOLL_ROOM")), "Stable projection repeatability");
            randomSeen.Add(actual[0].Relics[0]);
            pairsSeen.Add(string.Join(",", actual[1].Relics));
        }
        Check(randomSeen.Count == 3 && pairsSeen.Count == 6, "Doll fixtures exercise all free rewards and ordered pairs");
        foreach (string unsupported in new[] { "", "NOT_AN_EVENT", "MORPHIC_GROVE", "TRIAL", "doll_room" })
            Check(Beta111EventOptionResultProjector.Project(0, 0, unsupported).Count == 0,
                "Unsupported event has no stable option projection: " + unsupported);
        return checks;
    }
}
