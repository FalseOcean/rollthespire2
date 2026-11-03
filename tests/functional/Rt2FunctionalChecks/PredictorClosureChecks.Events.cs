using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using NativeEvents = MegaCrit.Sts2.Core.Models.Events;
using NativeCards = MegaCrit.Sts2.Core.Models.Cards;
using NativeRelics = MegaCrit.Sts2.Core.Models.Relics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rng;
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
        var repository = new DirectoryInfo(Environment.CurrentDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "RolltheSpire2.csproj")))
            repository = repository.Parent;
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository?.FullName ?? throw new DirectoryNotFoundException("RT2 repository root unavailable"),
            "docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json")));
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
        checks += CheckSharedEventRuleContinuations();
        checks += CheckSharedEventOptionEffects();
        checks += CheckStableEventOptionResults();
        Console.WriteLine($"EventPrediction checks={checks};PASS;nativeRng+TakeRandom+CalculateVars+StableShuffle;solo+fourSlots;full+restrictedPools;Trial+Tinker+stableOptionResults;zh+en");
        return 0;
    }

    private static int CheckSharedEventRuleContinuations()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
        void CheckState(Xoshiro256StarStar actual, Rng native, string operation)
        {
            var state = native.ToSerializable();
            Check(actual.CallCount == state.counter &&
                actual.State == (state.state0, state.state1, state.state2, state.state3),
                "Shared event rule retains full native RNG continuation: " + operation);
        }
        foreach (ulong seed in new[] { 0UL, ulong.MaxValue, 1UL << 63, 12345UL })
        foreach (int consumed in new[] { 0, 5 })
        {
            (Xoshiro256StarStar Actual, Rng Native) Checkpoint()
            {
                var actual = new Xoshiro256StarStar(seed);
                var native = new Rng(seed);
                for (int i = 0; i < consumed; i++) { actual.NextDouble(); native.NextDouble(); }
                return (actual, native);
            }
            void Compare<T>(string operation, Func<Xoshiro256StarStar, IEnumerable<T>> actualRule,
                Func<Rng, IEnumerable<T>> nativeRule)
            {
                var (actual, native) = Checkpoint();
                Check(actualRule(actual).SequenceEqual(nativeRule(native)),
                    "Shared event rule output/order at current checkpoint: " + operation);
                CheckState(actual, native, operation);
            }
            Compare("Trash Grab", rng => new[] { Beta111EventRules.TrashHeapGrab(rng) },
                rng => new[] { rng.NextItem(Beta111EventResultCatalog.TrashHeapGrabCards)! });
            Compare("Trash Dive", rng => new[] { Beta111EventRules.TrashHeapDive(rng) },
                rng => new[] { rng.NextItem(Beta111EventResultCatalog.TrashHeapDiveRelics)! });
            Compare("Fake inventory", Beta111EventRules.FakeMerchantInventory,
                rng => Beta111EventResultCatalog.FakeMerchantRelics.ToList().UnstableShuffle(rng).Take(6));
            Compare("Trial", rng => new[] { Beta111EventRules.TrialCase(rng) }, rng => new[] { rng.NextInt(3) });

            // Reversed/duplicate unlock input must not replace source declaration order.
            foreach (int unlockedCount in new[] { 0, 3, 5 })
            foreach (ModelKey owner in new[] { BaseGameModelKeys.Characters.Ironclad, default(ModelKey) })
            {
                var unlocked = Beta111EventResultCatalog.ColorfulCharacterOrder.Take(unlockedCount).Reverse()
                    .Concat(Beta111EventResultCatalog.ColorfulCharacterOrder.Take(unlockedCount)).ToArray();
                Compare("Colorful candidates=" + unlockedCount, rng => Beta111EventRules.ColorfulOffers(rng, owner, unlocked),
                    rng =>
                    {
                        var options = Beta111EventResultCatalog.ColorfulCharacterOrder
                            .Where(key => key != owner && unlocked.Contains(key)).ToList();
                        while (options.Count > 3) options.RemoveAt(rng.NextInt(options.Count));
                        return options;
                    });
            }

            var (tinker, nativeTinker) = Checkpoint();
            var types = Beta111EventRules.TinkerTypeOffers(tinker);
            Check(types.SequenceEqual(new[] { 0, 1, 2 }.TakeRandom(2, nativeTinker)), "Tinker current type order");
            CheckState(tinker, nativeTinker, "Tinker types");
            foreach (int type in types)
            {
                var branch = tinker.Clone();
                var nativeBranch = new Rng(nativeTinker.ToSerializable());
                Check(Beta111EventRules.TinkerRiderOffers(branch, type).SequenceEqual(
                    new[] { type * 3, type * 3 + 1, type * 3 + 2 }.TakeRandom(2, nativeBranch)),
                    "Tinker alternative uses the independent post-type checkpoint");
                CheckState(branch, nativeBranch, "Tinker rider branch");
            }
            CheckState(tinker, nativeTinker, "Tinker parent unchanged by rider alternatives");

            var (doll, nativeDoll) = Checkpoint();
            ModelId[] authored = [new("RELIC", "DAUGHTER_OF_THE_WIND"), new("RELIC", "MR_STRUGGLES"), new("RELIC", "BING_BONG")];
            ModelKey Key(ModelId id) => new(id.Category, id.Entry);
            var randomBranch = doll.Clone();
            var nativeRandom = new Rng(nativeDoll.ToSerializable());
            Check(Beta111EventRules.RandomDoll(randomBranch) == Key(nativeRandom.NextItem(authored)!), "Doll random authored order");
            CheckState(randomBranch, nativeRandom, "Doll random");
            foreach (int count in new[] { 2, 3 })
            {
                var branch = doll.Clone();
                var nativeBranch = new Rng(nativeDoll.ToSerializable());
                Check(Beta111EventRules.DollOffers(branch, count).SequenceEqual(
                    authored.ToList().StableShuffle(nativeBranch).Take(count).Select(Key)), "Doll sorted offer order");
                CheckState(branch, nativeBranch, "Doll paid branch");
            }
            CheckState(doll, nativeDoll, "Doll parent unchanged by alternatives");
            Compare("Jungle decimal before settlement", rng => new[]
                { Beta111EventRules.JungleGold(rng, 150m), Beta111EventRules.JungleGold(rng, 50m) },
                rng => new[] { 150m + (decimal)rng.NextFloat(-15f, 15f), 50m + (decimal)rng.NextFloat(-15f, 15f) });
        }
        return checks;
    }

    private static int CheckSharedEventOptionEffects()
    {
        // CanonicalVars for product-bearing events resolve native ModelDb IDs.
        // Reuse the existing isolated native fixture, including its mock saves.
        NativeWorkbenchCheckEnvironment.Initialize("SharedEventOptionEffects");
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
        Dictionary<string, DynamicVar> Vars(Type type)
        {
            var instance = RuntimeHelpers.GetUninitializedObject(type);
            var property = type.GetProperty("CanonicalVars", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return ((IEnumerable<DynamicVar>)property.GetValue(instance)!).ToDictionary(value => value.Name);
        }
        var beacon = Vars(typeof(NativeEvents.DrowningBeacon));
        var unrest = Vars(typeof(NativeEvents.UnrestSite));
        var dolls = Vars(typeof(NativeEvents.DollRoom));
        var tablet = Vars(typeof(NativeEvents.TabletOfTruth));
        var scriptorium = Vars(typeof(NativeEvents.WaterloggedScriptorium));
        var tea = Vars(typeof(NativeEvents.TeaMaster));
        var zen = Vars(typeof(NativeEvents.ZenWeaver));
        EventHealthEffect Health(string entry, string option) => Beta111EventOptionRules.FixedHealth(entry, option)!.Value;
        Check(-Beta111EventOptionRules.BeaconClimb.Health.MaxHpDelta == beacon["HpLoss"].IntValue &&
            -Health("UNREST_SITE", "KILL").MaxHpDelta == unrest["MaxHpLoss"].IntValue &&
            Health("UNREST_SITE", "REST").FullHeal,
            "Beacon/Unrest shared health commands agree with native vars and full-heal operation");
        Check(-Health("DOLL_ROOM", "TAKE_SOME_TIME").HpDelta == dolls["TakeTimeHpLoss"].IntValue &&
            -Health("DOLL_ROOM", "EXAMINE").HpDelta == dolls["ExamineHpLoss"].IntValue &&
            Health("TABLET_OF_TRUTH", "SMASH").HpDelta == tablet["SmashHPGain"].IntValue &&
            Health("WATERLOGGED_SCRIPTORIUM", "BLOODY_INK") ==
                new EventHealthEffect(scriptorium["MaxHp"].IntValue, scriptorium["MaxHp"].IntValue),
            "Doll/Tablet/Scriptorium effect amounts agree with native canonical vars");
        var fixedHealthFixtures = new (Type Type, string Entry, string Option, string Var, int HpSign, int MaxHpSign)[]
        {
            (typeof(NativeEvents.TheLegendsWereTrue), "THE_LEGENDS_WERE_TRUE", "SLOWLY_FIND_AN_EXIT", "Damage", -1, 0),
            (typeof(NativeEvents.BrainLeech), "BRAIN_LEECH", "RIP", "RipHpLoss", -1, 0),
            (typeof(NativeEvents.ByrdonisNest), "BYRDONIS_NEST", "EAT", "MaxHp", 1, 1),
            (typeof(NativeEvents.DenseVegetation), "DENSE_VEGETATION", "TRUDGE_ON", "HpLoss", -1, 0),
            (typeof(NativeEvents.RoundTeaParty), "ROUND_TEA_PARTY", "CONTINUE_FIGHT", "Damage", -1, 0),
            (typeof(NativeEvents.SunkenStatue), "SUNKEN_STATUE", "DIVE_INTO_WATER", "HpLoss", -1, 0),
            (typeof(NativeEvents.RoomFullOfCheese), "ROOM_FULL_OF_CHEESE", "SEARCH", "Damage", -1, 0),
            (typeof(NativeEvents.SpiritGrafter), "SPIRIT_GRAFTER", "LET_IT_IN", "LetItInHealAmount", 1, 0),
            (typeof(NativeEvents.SpiritGrafter), "SPIRIT_GRAFTER", "REJECTION", "RejectionHpLoss", -1, 0),
            (typeof(NativeEvents.MorphicGrove), "MORPHIC_GROVE", "LONER", "MaxHp", 1, 1),
            (typeof(NativeEvents.TrashHeap), "TRASH_HEAP", "DIVE_IN", "HpLoss", -1, 0),
            (typeof(NativeEvents.ThisOrThat), "THIS_OR_THAT", "PLAIN", "HpLoss", -1, 0),
            (typeof(NativeEvents.SapphireSeed), "SAPPHIRE_SEED", "EAT", "Heal", 1, 0),
            (typeof(NativeEvents.StoneOfAllTime), "STONE_OF_ALL_TIME", "LIFT", "DrinkMaxHpGain", 1, 1),
            (typeof(NativeEvents.StoneOfAllTime), "STONE_OF_ALL_TIME", "PUSH", "PushHpLoss", -1, 0),
            (typeof(NativeEvents.JungleMazeAdventure), "JUNGLE_MAZE_ADVENTURE", "SOLO_QUEST", "SoloHp", -1, 0),
            (typeof(NativeEvents.WhisperingHollow), "WHISPERING_HOLLOW", "HUG", "HpLoss", -1, 0)
        };
        foreach (var fixture in fixedHealthFixtures)
        {
            int amount = Vars(fixture.Type)[fixture.Var].IntValue;
            var effect = Health(fixture.Entry, fixture.Option);
            Check(effect.HpDelta == fixture.HpSign * amount && effect.MaxHpDelta == fixture.MaxHpSign * amount &&
                Beta111EventOptionRules.RequiresHealthFact(fixture.Entry, fixture.Option),
                "Shared chosen effect/input agrees with native vars: " + fixture.Entry + "/" + fixture.Option);
        }
        Check(Health("SPIRIT_GRAFTER", "REJECTION").AfterCardSelection &&
            Health("WHISPERING_HOLLOW", "HUG").AfterCardSelection &&
            !Health("STONE_OF_ALL_TIME", "PUSH").AfterCardSelection,
            "Deferred rejection/transform damage remains distinct from damage before selection");
        foreach (var (option, variable) in new[] { ("BONE_TEA", "BoneTeaCost"), ("EMBER_TEA", "EmberTeaCost") })
            Check(Beta111EventOptionRules.TeaMaster(option).Cost.Amount == tea[variable].IntValue,
                "Tea purchase amount from native canonical vars: " + option);
        foreach (var (option, variable) in new[] { ("BREATHING_TECHNIQUES", "BreathingTechniquesCost"),
            ("EMOTIONAL_AWARENESS", "EmotionalAwarenessCost"), ("ARACHNID_ACUPUNCTURE", "ArachnidAcupunctureCost") })
            Check(Beta111EventOptionRules.ZenWeaver(option).Cost.Amount == zen[variable].IntValue,
                "Zen purchase amount from native canonical vars: " + option);
        Check(Beta111EventOptionRules.Scriptorium("TENTACLE_QUILL").Cost.Amount == scriptorium["Gold"].IntValue &&
            Beta111EventOptionRules.Scriptorium("PRICKLY_SPONGE").Cost.Amount == scriptorium["PricklySpongeGold"].IntValue &&
            Beta111EventOptionRules.Scriptorium("PRICKLY_SPONGE").TargetCount == scriptorium["Cards"].IntValue,
            "Scriptorium payment and target count agree with native vars");
        ModelKey Key(AbstractModel model) => new(model.Id.Category, model.Id.Entry);
        Check(Beta111EventOptionRules.BeaconClimb.Relic == Key(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.FresnelLens>()) &&
            Beta111EventOptionRules.BeaconBottlePotion == Key(ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.GlowwaterPotion>()) &&
            Beta111EventOptionRules.UnrestRestCurse == Key(ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.PoorSleep>()) &&
            Beta111EventOptionRules.ZenWeaver("BREATHING_TECHNIQUES").AddedCard == Key(ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.Enlightenment>()) &&
            Beta111EventOptionRules.Scriptorium("TENTACLE_QUILL").Enchantment == Key(ModelDb.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Steady>()),
            "Shared fixed products bind actual native model identities");
        var relicProducts = new (string Entry, string Option, RelicModel Native)[]
        {
            ("SUNKEN_STATUE", "GRAB_SWORD", ModelDb.Relic<NativeRelics.SwordOfStone>()),
            ("ROUND_TEA_PARTY", "ENJOY_TEA", ModelDb.Relic<NativeRelics.RoyalPoison>()),
            ("LOST_WISP", "CLAIM", ModelDb.Relic<NativeRelics.LostWisp>()),
            ("COLOSSAL_FLOWER", "POLLINOUS_CORE", ModelDb.Relic<NativeRelics.PollinousCore>()),
            ("GRAVE_OF_THE_FORGOTTEN", "ACCEPT", ModelDb.Relic<NativeRelics.ForgottenSoul>()),
            ("ROOM_FULL_OF_CHEESE", "SEARCH", ModelDb.Relic<NativeRelics.ChosenCheese>()),
            ("WELCOME_TO_WONGOS", "MYSTERY_BOX", ModelDb.Relic<NativeRelics.WongosMysteryTicket>()),
            ("WAR_HISTORIAN_REPY", "INITIAL_UNLOCK_CAGE", ModelDb.Relic<NativeRelics.HistoryCourse>()),
            ("WAR_HISTORIAN_REPY", "SECOND_UNLOCK_CAGE", ModelDb.Relic<NativeRelics.HistoryCourse>()),
            ("TEA_MASTER", "BONE_TEA", ModelDb.Relic<NativeRelics.BoneTea>()),
            ("TEA_MASTER", "EMBER_TEA", ModelDb.Relic<NativeRelics.EmberTea>()),
            ("TEA_MASTER", "TEA_OF_DISCOURTESY", ModelDb.Relic<NativeRelics.TeaOfDiscourtesy>())
        };
        foreach (var fixture in relicProducts)
            Check(Beta111EventOptionRules.FixedRelicProduct(fixture.Entry, fixture.Option) == Key(fixture.Native),
                "Shared fixed relic identity vs native product: " + fixture.Entry + "/" + fixture.Option);
        var cardProducts = new (string Entry, string Option, CardModel Native)[]
        {
            ("BYRDONIS_NEST", "TAKE", ModelDb.Card<NativeCards.ByrdonisEgg>()),
            ("THE_LEGENDS_WERE_TRUE", "NAB_THE_MAP", ModelDb.Card<NativeCards.SpoilsMap>()),
            ("SPIRIT_GRAFTER", "LET_IT_IN", ModelDb.Card<NativeCards.Metamorphosis>()),
            ("BUGSLAYER", "EXTERMINATION", ModelDb.Card<NativeCards.Exterminate>()),
            ("BUGSLAYER", "SQUASH", ModelDb.Card<NativeCards.Squash>()),
            ("CRYSTAL_SPHERE", "PAYMENT_PLAN", ModelDb.Card<NativeCards.Debt>()),
            ("ZEN_WEAVER", "BREATHING_TECHNIQUES", ModelDb.Card<NativeCards.Enlightenment>()),
            ("UNREST_SITE", "REST", ModelDb.Card<NativeCards.PoorSleep>()),
            ("LOST_WISP", "CLAIM", ModelDb.Card<NativeCards.Decay>()),
            ("GRAVE_OF_THE_FORGOTTEN", "CONFRONT", ModelDb.Card<NativeCards.Decay>()),
            ("PUNCH_OFF", "NAB", ModelDb.Card<NativeCards.Injury>()),
            ("TRIAL", "MERCHANT_INNOCENT", ModelDb.Card<NativeCards.Shame>()),
            ("TRIAL", "MERCHANT_GUILTY", ModelDb.Card<NativeCards.Regret>()),
            ("TRIAL", "NOBLE_INNOCENT", ModelDb.Card<NativeCards.Regret>()),
            ("TRIAL", "NONDESCRIPT_INNOCENT", ModelDb.Card<NativeCards.Doubt>()),
            ("TRIAL", "NONDESCRIPT_GUILTY", ModelDb.Card<NativeCards.Doubt>()),
            ("WELLSPRING", "BATHE", ModelDb.Card<NativeCards.Guilty>()),
            ("FIELD_OF_MAN_SIZED_HOLES", "RESIST", ModelDb.Card<NativeCards.Normality>()),
            ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH", ModelDb.Card<NativeCards.SporeMind>()),
            ("AMALGAMATOR", "COMBINE_STRIKES", ModelDb.Card<NativeCards.UltimateStrike>()),
            ("AMALGAMATOR", "COMBINE_DEFENDS", ModelDb.Card<NativeCards.UltimateDefend>())
        };
        foreach (var fixture in cardProducts)
            Check(Beta111EventOptionRules.FixedCardProduct(fixture.Entry, fixture.Option) == Key(fixture.Native),
                "Shared fixed card identity vs native product: " + fixture.Entry + "/" + fixture.Option);
        foreach (var fixture in cardProducts)
        {
            bool after = fixture.Entry is "WELLSPRING" or "FIELD_OF_MAN_SIZED_HOLES" or "LUMINOUS_CHOIR" or "AMALGAMATOR";
            Check(Beta111EventOptionRules.CardProductAfterSelection(fixture.Entry, fixture.Option) ==
                (after ? Key(fixture.Native) : (ModelKey?)null),
                "Post-selector product scope excludes already-added Trial and other pre-selector products: " + fixture.Entry + "/" + fixture.Option);
        }
        Check(Beta111EventOptionRules.FixedRelicProduct("DOLL_ROOM", "RANDOM") is null &&
            Beta111EventOptionRules.FixedRelicProduct("WELCOME_TO_WONGOS", "BARGAIN_BIN") is null &&
            Beta111EventOptionRules.FixedCardProduct("ROOM_FULL_OF_CHEESE", "GORGE") is null,
            "Dynamic pool, selected and generated products remain context-owned");
        Check(Beta111EventOptionRules.TabletMaxHpCost(0, null) == tablet["DecipherMaxHpLoss"].IntValue,
            "Tablet initial offered cost agrees with native vars before health is known");
        var nativeTablet = (NativeEvents.TabletOfTruth)RuntimeHelpers.GetUninitializedObject(typeof(NativeEvents.TabletOfTruth));
        var countField = typeof(NativeEvents.TabletOfTruth).GetField("_decipherCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int count = 1; count <= 3; count++)
        {
            countField.SetValue(nativeTablet, count);
            Check(Beta111EventOptionRules.TabletMaxHpCost(count, 80) == nativeTablet.GetDecipherCost(),
                "Tablet continuation cost agrees with native GetDecipherCost: " + count);
        }
        Check(Beta111EventOptionRules.TabletDecipher(4, 31) is { MaxHpCost: 30, UpgradeAll: true, ForcedKill: false } &&
            Beta111EventOptionRules.TabletDecipher(0, 3) is { ForcedKill: true, UpgradeAll: false } &&
            Beta111EventOptionRules.TabletDecipher(0, 3).HealthBeforeKill(3).MaxHpDelta == -2,
            "Tablet final all-upgrade and explicit low-maxHP Kill commands stay distinct");
        Check(!Beta111EventOptionRules.RequiresHealthFact("DROWNING_BEACON", "BOTTLE") &&
            Beta111EventOptionRules.RequiresHealthFact("DROWNING_BEACON", "CLIMB") &&
            Beta111EventOptionRules.RequiresHealthFact("TABLET_OF_TRUTH", "DECIPHER") &&
            !Beta111EventOptionRules.RequiresHealthFact("DOLL_ROOM", "RANDOM"),
            "Health input follows the chosen effect rather than the whole event");
        return checks;
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
