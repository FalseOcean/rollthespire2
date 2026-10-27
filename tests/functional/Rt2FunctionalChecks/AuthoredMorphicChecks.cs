using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;

namespace RolltheSpire2.Tests.FastSearch;

internal static class AuthoredMorphicChecks
{
    private static int _checks;
    private static int _cases;
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(message);
    }

    internal static int Run()
    {
        if (Environment.GetEnvironmentVariable("RT2_AUTHORED_GODOT_RUNNER") != "1")
            throw new InvalidOperationException("Run this source-reference entry through tools/AuthoredEventChecks (Godot headless); model bootstrap requires native Godot.");
        TestMode.IsOn = true;
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        // Standalone vanilla fixture: no mod loading or game/UI startup.
        typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!.SetValue(null,
            MegaCrit.Sts2.Core.Modding.ModManagerState.Initialized);
        ModelDb.Init();
        MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init();
        ModelDb.InitIds();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language = "eng";
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        var evidence = new List<object>();
        var chars = new CharacterModel[] { ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(),
            ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>(), ModelDb.Character<Defect>() };
        if (Environment.GetEnvironmentVariable("RT2_SINGLE_TRANSFORM_EVENTS") == "1")
        {
            foreach (var character in chars)
                foreach (string seed in new[] { "000000000000", "000000000001", "0000000007NE" })
                    foreach (var source in Enum.GetValues<SingleBasicTransformEvent>()) RunSingleTransformEvent(character, seed, source);
            Console.WriteLine($"SINGLE_TRANSFORM_SOURCE_PASS cases=30;checks={_checks};actual=BeginEvent+LetGo/Hug;characters=5;gameMvid={typeof(MorphicGrove).Module.ModuleVersionId}");
            return 0;
        }
        foreach (var character in chars)
        {
            var uiAuthority = Beta110FastSearchFixtureAuthority.CreateBeta111(new("CHARACTER", character.Id.Entry), 10, 0, 1);
            var authored = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredInitialBasics(uiAuthority);
            var entryPlayer = Player.CreateForNewRun(character, MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress(), 1);
            var entryRun = RunState.CreateForTest([entryPlayer], seed: "000000000000", ascensionLevel: 10);
            var actualEntry = Beta111MorphicGroveAuthorityCapture.CaptureInitialBasics(uiAuthority, authored.Premises, entryPlayer);
            Check(authored.Targets.SelectMany(t => t.OrderedSourceCandidates!).SequenceEqual(
                actualEntry.Targets.SelectMany(t => t.OrderedSourceCandidates!)), "UI authored pool / real CardFactory capture parity");
            Check(!authored.Premises.NoDeckMutationHooks && entryRun.Rng.Seed != 0, "UI preserves final-hook uncertainty");
            var basicNames = character.StartingDeck.Where(c => c.Rarity == CardRarity.Basic &&
                (c.Tags.Contains(CardTag.Strike) || c.Tags.Contains(CardTag.Defend))).Select(c => c.Id.Entry).Distinct().ToArray();
            foreach (var names in new[] { new[] { basicNames[0], basicNames[0] },
                new[] { basicNames[1], basicNames[1] }, new[] { basicNames[0], basicNames[1] } })
                RunCase("0000000007NE", character, false, targetEntries: names);
        }
        foreach (var character in chars)
        foreach (bool reverse in new[] { false, true })
        for (int i = 0; i < 16; i++)
        {
            var result = RunCase(i == 0 ? "0000000007NE" : i.ToString("D12"), character, reverse);
            if (i == 0) evidence.Add(result);
        }
        evidence.Add(RunCase("0000000007NE", chars[0], false, unlocks: UnlockState.none));
        evidence.Add(RunCase("0000000007NE", chars[0], false, multiplayer: true));
        evidence.Add(RunCase("0000000007NE", chars[0], false, advanceOtherStreams: true));
        evidence.Add(RunCase("0000000007NE", chars[0], false, relic: ModelDb.Relic<ToxicEgg>()));
        evidence.Add(RunCase("0000000007NE", chars[0], false, relic: ModelDb.Relic<MoltenEgg>()));
        evidence.Add(RunCase("0000000007NE", chars[0], false, relic: ModelDb.Relic<FresnelLens>()));
        evidence.Add(RunCase("0000000007NE", chars[0], false, relic: ModelDb.Relic<LuckyFysh>()));
        evidence.Add(RunCase("0000000007NE", chars[0], false, targetEntries: ["APPARITION", "FINESSE"]));
        evidence.Add(RunCase("0000000007NE", chars[0], false, targetEntries: ["DECAY", "SOOT"]));
        evidence.Add(RunCase("0000000007NE", chars[0], true, targetEntries: ["APPARITION", "FINESSE"]));
        evidence.Add(RunCase("0000000007NE", chars[0], true, targetEntries: ["DECAY", "SOOT"]));

        // Conditional root enumeration: ordinals alone are retained; no state payload.
        var accepted = new List<int>();
        var enumerator = new RolltheSpire2.Search.Enumeration.Base34SeedEnumerator(
            RolltheSpire2.Compatibility.Beta111Profile.Instance, "000000000000");
        for (int i = 0; i < 128; i++)
        {
            var result = RunCase(enumerator.SeedAt(i), chars[0], false);
            if (result.Predicate == MorphicGrovePredicateResult.Match) accepted.Add(i);
        }
        Check(accepted.Count > 0 && accepted.Count < 128, "Conditional filter must accept and reject.");
        Check(accepted.SequenceEqual(accepted.Order()), "Ordinal order changed.");
        string evidencePath = Path.GetFullPath("../../.build-evidence/morphic-main/runtime.evidence.json");
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        var jsonOptions = new JsonSerializerOptions(ProductionSearchReplay.JsonOptions) { WriteIndented = false };
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new {
            Reference = "Unpatched installed Beta111 assembly: BeginEvent + real private Group + source ICardSelector seam",
            AssemblyMvid = typeof(MorphicGrove).Module.ModuleVersionId,
            AssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(".godot/mono/temp/bin/Debug/sts2.dll"))),
            EntryAssumptions = "Gold=100 for every player; floor=0; starting deck with committed Strike upgraded; no route/combat history proof",
            Checks = _checks, Cases = _cases, ConditionalRootCount = 128, AcceptedLogicalOrdinals = accepted, Evidence = evidence
        }, jsonOptions));
        Console.WriteLine($"AuthoredMorphic source cases={_cases} checks={_checks}; accepted={string.Join(',', accepted)}; PASS; {evidencePath}");
        return 0;
    }

    private sealed record CaseEvidence(string Seed, ulong RootHash, string Character, int Ascension,
        string Unlocks, int PlayerCount, int OwnerSlot, string? Relic, bool AdvancedUnrelatedStreams,
        MorphicGroveTarget[] Targets, MorphicGroveProjection Projection, MorphicGroveCard[] ActualFinal,
        int ActualEventRngCalls, int GoldAfter, MorphicGrovePredicateResult Predicate);

    internal static int RunCaptured()
    {
        // Pure Production Core projection from saved source authority: no game or
        // native Godot startup, no source model initialization, no RunState.
        var options = ProductionSearchReplay.JsonOptions;
        using var document = JsonDocument.Parse(File.ReadAllText("docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json"));
        var cases = document.RootElement.GetProperty("Evidence").EnumerateArray()
            .Select(e => e.Deserialize<CaseEvidence>(options)!).ToArray();
        foreach (var c in cases)
        {
            var projected = Beta111MorphicGroveProjector.Project(c.RootHash, c.Projection.Commitment, c.Projection.Premises, c.Targets);
            Check(JsonSerializer.Serialize(projected, options) == JsonSerializer.Serialize(c.Projection, options), "Saved source / pure replay parity.");
        }
        var first = cases[0];
        var enumerator = new RolltheSpire2.Search.Enumeration.Base34SeedEnumerator(
            RolltheSpire2.Compatibility.Beta111Profile.Instance, "000000000000");
        var accepted = new List<int>();
        var needle = new ModelKey("CARD", "THUNDERCLAP");
        for (int i = 0; i < 128; i++)
        {
            ulong root = RolltheSpire2.Compatibility.Beta111Profile.Instance.ComputeRootSeed(enumerator.SeedAt(i));
            var projected = Beta111MorphicGroveProjector.Project(root, first.Projection.Commitment, first.Projection.Premises, first.Targets);
            var result = Beta111MorphicGroveProjector.Contains(projected, needle, true);
            Check(result != MorphicGrovePredicateResult.Unknown, "Closed conditional query became Unknown.");
            if (result == MorphicGrovePredicateResult.Match) accepted.Add(i);
        }
        Check(accepted.SequenceEqual(document.RootElement.GetProperty("AcceptedLogicalOrdinals").EnumerateArray().Select(e => e.GetInt32())), "Conditional filter / actual source root population parity.");
        var partial = Beta111MorphicGroveProjector.Project(first.RootHash, first.Projection.Commitment,
            first.Projection.Premises with { NoDeckMutationHooks = false }, first.Targets);
        Check(Beta111MorphicGroveProjector.Contains(partial, needle, true) == MorphicGrovePredicateResult.Unknown, "Open final hooks rejected a candidate.");
        var unknown = Beta111MorphicGroveProjector.Project(first.RootHash, first.Projection.Commitment,
            first.Projection.Premises with { VanillaLocalDependenciesBound = false }, first.Targets);
        Check(unknown.Transforms.All(t => t.RawPrecision == PredictionPrecision.Unknown), "Unbound effect assumed vanilla.");
        bool failed = false;
        try { Beta111MorphicGroveProjector.Project(first.RootHash, new("deck:0", "deck:0"), first.Projection.Premises, first.Targets); }
        catch (ArgumentException) { failed = true; }
        Check(failed, "Duplicate commitment did not fail fast.");
        failed = false;
        try { Beta111MorphicGroveProjector.Project(first.RootHash, first.Projection.Commitment, first.Projection.Premises,
            [first.Targets[0] with { OrderedSourceCandidates = [] }, first.Targets[1]]); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed, "Empty authoritative pool did not fail fast.");
        CheckMainBoundary(first);
        Console.WriteLine($"AuthoredMorphic captured cases={cases.Length} checks={_checks}; conditional roots=128 accepted={string.Join(',', accepted)}; PASS");
        return 0;
    }

    private static void CheckMainBoundary(CaseEvidence c)
    {
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(new("CHARACTER", c.Character), 10, 0, 1);
        Check(SeedPredictionRequest.TryCreate(c.Seed, authority.Character, 10, 1, 0, authority, true,
            out var request, out _), "Boundary request");
        var detection = new GameVersionDetection("0.111.0", "0.111.0", "beta", "CapturedFixture", true, "");
        var scenario = new MorphicGroveScenario(authority, c.Projection.Premises, c.Targets);
        var prediction = RuntimeProfileRegistry.PredictMorphicGrove(detection, request!, scenario, c.Projection.Commitment);
        Check(prediction.RootHash == c.RootHash && prediction.Precision == PredictionPrecision.Exact, "Main concrete-root prediction");
        Check(SeedPredictionRequest.TryCreateFromRootHash(TrustedRootHashInput.RootOnly(c.RootHash), authority.Character,
            10, 1, 0, authority, null, SeedPredictionDomainSelection.None, 1, false, out var rootRequest, out _), "Trusted root request");
        Check(RuntimeProfileRegistry.PredictMorphicGrove(detection, rootRequest!, scenario, c.Projection.Commitment)
            .Projection!.Transforms.SequenceEqual(prediction.Projection!.Transforms), "Concrete root does not require Search or a display seed");
        var replayedScenario = JsonSerializer.Deserialize<MorphicGroveScenario>(JsonSerializer.Serialize(scenario,
            ProductionSearchReplay.JsonOptions), ProductionSearchReplay.JsonOptions)!;
        Check(RuntimeProfileRegistry.PredictMorphicGrove(detection, request!, replayedScenario, c.Projection.Commitment)
            .Projection!.Transforms.SequenceEqual(prediction.Projection!.Transforms), "Immutable scenario JSON replay through Main");
        Check(RuntimeProfileRegistry.PredictMorphicGrove(detection, request!, scenario, null).Evidence ==
            "MorphicGrove.MandatoryChoiceNotAuthored", "Mandatory choice cannot silently Skip or choose Loner");
        Check(RuntimeProfileRegistry.PredictMorphicGrove(detection, request!, new(authority,
            c.Projection.Premises with { EventOccurrenceBasis = "" }, c.Targets), c.Projection.Commitment).Precision ==
            PredictionPrecision.Unknown, "Missing event premise");
        Check(RuntimeProfileRegistry.PredictMorphicGrove(detection, request!, new(authority,
            c.Projection.Premises with { NoDeckMutationHooks = false }, c.Targets), c.Projection.Commitment).Precision ==
            PredictionPrecision.Partial, "Raw facts survive open final hooks");
        bool fault = false;
        try { RuntimeProfileRegistry.PredictMorphicGrove(detection, request!, new(authority, c.Projection.Premises,
            [c.Targets[0] with { OrderedSourceCandidates = [] }, c.Targets[1]]), c.Projection.Commitment); }
        catch (InvalidOperationException) { fault = true; }
        Check(fault, "Main entry does not swallow a corrupt authoritative pool");
        var mutableTargets = c.Targets.ToArray();
        var mutablePool = c.Targets[0].OrderedSourceCandidates!.ToArray();
        mutableTargets[0] = mutableTargets[0] with { OrderedSourceCandidates = mutablePool };
        var immutableScenario = new MorphicGroveScenario(authority, c.Projection.Premises, mutableTargets);
        mutablePool[0] = mutablePool[0] with { CardKey = new("CARD", "INVALID") };
        mutableTargets[0] = mutableTargets[0] with { InstanceId = "changed" };
        Check(immutableScenario.Targets[0].InstanceId == c.Targets[0].InstanceId &&
            immutableScenario.Targets[0].OrderedSourceCandidates![0].CardKey == c.Targets[0].OrderedSourceCandidates![0].CardKey,
            "Scenario copies externally mutable buffers");
        var modAuthority = authority.WithEffectAuthority(authority.EffectAuthority! with {
            SourceAuthority = SourceAuthority.ModdedRuntimeBestEffort });
        SeedPredictionRequest.TryCreate(c.Seed, modAuthority.Character, 10, 1, 0, modAuthority, true, out var modRequest, out _);
        var modResult = RuntimeProfileRegistry.PredictMorphicGrove(detection, modRequest!,
            new(modAuthority, c.Projection.Premises, c.Targets), c.Projection.Commitment);
        Check(modResult.Authority.EffectAuthority!.SourceAuthority == SourceAuthority.ModdedRuntimeBestEffort &&
            modResult.Projection!.Transforms.SequenceEqual(prediction.Projection!.Transforms), "Provenance preserved, no origin-only admission");
    }

    private static CaseEvidence RunCase(string seed, CharacterModel character, bool reverse,
        UnlockState? unlocks = null, bool multiplayer = false, bool advanceOtherStreams = false, RelicModel? relic = null,
        string[]? targetEntries = null)
    {
        _cases++;
        var player = Player.CreateForNewRun(character, unlocks ?? UnlockState.all, 1);
        Player[] players = multiplayer ? [Player.CreateForNewRun<Ironclad>(UnlockState.all, 99), player] : [player];
        var run = RunState.CreateForTest(players, seed: seed, ascensionLevel: 10);
        // Explicit entry assumption, not a prediction of rewards from prior combat.
        foreach (var p in players) p.Gold = 100;
        if (relic != null) player.AddRelicInternal(relic.ToMutable(), silent: true);
        var selected = new[] { player.Deck.Cards.First(c => c.Id.Entry.StartsWith("STRIKE_")),
            player.Deck.Cards.First(c => c.Id.Entry.StartsWith("DEFEND_")) };
        if (targetEntries != null)
            selected = targetEntries.Select(entry => {
                var card = run.CreateCard(ModelDb.GetById<CardModel>(new ModelId("CARD", entry)), player);
                player.Deck.AddInternal(card);
                return card;
            }).ToArray();
        if (selected[0].IsUpgradable)
            CardCmd.Upgrade(selected[0], MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.None);
        if (reverse) Array.Reverse(selected);
        var beforeDeck = player.Deck.Cards.ToArray();
        int goldBefore = player.Gold;
        var targets = selected.Select((c, i) => Beta111MorphicGroveAuthorityCapture.Capture($"deck:{i}", c)).ToArray();
        var commitment = new MorphicGroveCommitment("deck:0", "deck:1");
        var premises = new MorphicGrovePremises("Assume MorphicGrove occurs at entry; no path proof", true, relic == null, run.TotalFloor);
        var projection = Beta111MorphicGroveProjector.Project(run.Rng.Seed, commitment, premises, targets);
        if (!multiplayer)
        {
            var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(new("CHARACTER", character.Id.Entry), 10, 0, 1);
            Check(SeedPredictionRequest.TryCreate(seed, authority.Character, 10, 1, 0, authority, true,
                out var request, out _), "Main request creation");
            var detection = new GameVersionDetection("0.111.0", "0.111.0", "beta", "MorphicRuntimeFixture", true, "");
            var scenario = new MorphicGroveScenario(authority, premises, targets);
            var main = RuntimeProfileRegistry.PredictMorphicGrove(detection, request!, scenario, commitment);
            Check(JsonSerializer.Serialize(main.Projection, ProductionSearchReplay.JsonOptions) ==
                JsonSerializer.Serialize(projection, ProductionSearchReplay.JsonOptions), "Main Predictor / donor parity");
            Check(main.Precision == (relic == null ? PredictionPrecision.Exact : PredictionPrecision.Partial), "Main conditional precision");
            if (targetEntries is null || selected.All(c => c.Rarity == CardRarity.Basic))
            {
                var basicScenario = Beta111MorphicGroveAuthorityCapture.CaptureInitialBasics(authority, premises, player);
                var basic = RuntimeProfileRegistry.PredictMorphicGrove(detection, request!, basicScenario, MorphicGroveCommitment.InitialBasics);
                Check(basic.Projection!.Transforms.Select(t => t.RawReplacement).SequenceEqual(projection.Transforms.Select(t => t.RawReplacement)),
                    "Default two basics equivalent to Strike/Defend split and delivered order");
            }
        }
        Check(player.Deck.Cards.SequenceEqual(beforeDeck) && player.Gold == goldBefore, "Predictor mutated source entry state.");
        Check(run.Rng.Seed == RolltheSpire2.Core.Seed.XxHash64.HashUtf8(seed, 0), "Root seed hash parity.");
        if (advanceOtherStreams)
            for (int i = 0; i < 999; i++)
            {
                run.Rng.CombatCardGeneration.NextInt(50);
                run.Rng.Shuffle.NextInt(50);
                player.PlayerRng.Rewards.NextInt(50);
            }
        var grove = (MorphicGrove)ModelDb.Event<MorphicGrove>().ToMutable();
        Check(grove.IsAllowed(run), "Authored fixture must satisfy actual event eligibility.");
        grove.BeginEvent(player, null, false).GetAwaiter().GetResult();
        Check(grove.Rng.ToSerializable().counter == 0, "Event entry unexpectedly consumes local RNG.");
        var probe = new MegaCrit.Sts2.Core.Random.Rng(grove.Rng.ToSerializable());
        for (int i = 0; i < 2; i++)
        {
            var actualRaw = CardFactory.CreateRandomCardForTransform(selected[i], false, probe);
            Check(Beta111MorphicGroveAuthorityCapture.Snapshot(actualRaw) == projection.Transforms[i].RawReplacement, "Source CardFactory raw metadata parity.");
        }
        using var selector = CardSelectCmd.UseSelector(new CommittedSelector(selected));
        ((Task)typeof(MorphicGrove).GetMethod("Group", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(grove, null)!).GetAwaiter().GetResult();
        var actual = player.Deck.Cards.TakeLast(2).Select(Beta111MorphicGroveAuthorityCapture.Snapshot).ToArray();
        Check(player.Gold == (relic is LuckyFysh ? 30 : 0) && grove.Rng.ToSerializable().counter == 2, "Post-hook gold / two sequential local draws parity.");
        Check(beforeDeck.Except(selected).SequenceEqual(player.Deck.Cards.SkipLast(2)), "Deck append order / original removal parity.");
        for (int i = 0; i < 2; i++)
        {
            Check(actual[i].CardKey == projection.Transforms[i].RawReplacement!.CardKey, "Raw identity / actual Group parity.");
            if (relic == null)
            {
                Check(actual[i] == projection.Transforms[i].FinalReplacement, "Final replacement metadata parity.");
                Check(player.Deck.Cards.TakeLast(2).ElementAt(i).FloorAddedToDeck == projection.Transforms[i].FloorAddedToDeck, "Entry floor parity.");
            }
            else Check(projection.Transforms[i].FinalPrecision == PredictionPrecision.Unknown, "Hook uncertainty became final truth.");
        }
        if (relic is ToxicEgg) Check(actual[1].UpgradeLevel == 1, "ToxicEgg must upgrade FlameBarrier.");
        if (relic is MoltenEgg) Check(actual[0].UpgradeLevel == 1, "MoltenEgg must upgrade Thunderclap.");
        if (relic is FresnelLens) Check(actual.Any(c => c.EnchantmentId != null), "FresnelLens must alter final metadata.");
        Check(Beta111MorphicGroveProjector.ContainsPair(projection, actual[0].CardKey, actual[1].CardKey, false) == MorphicGrovePredicateResult.Match,
            "Pair predicate / actual Group output multiset");
        Check(Beta111MorphicGroveProjector.ContainsPair(projection, actual[1].CardKey, actual[0].CardKey, false) == MorphicGrovePredicateResult.Match,
            "Pair predicate order irrelevant");
        Check((Beta111MorphicGroveProjector.ContainsPair(projection, actual[0].CardKey, actual[0].CardKey, false) == MorphicGrovePredicateResult.Match) ==
            (actual[0].CardKey == actual[1].CardKey), "Two copies require two actual copies");

        var needle = new ModelKey("CARD", "THUNDERCLAP");
        var predicate = Beta111MorphicGroveProjector.Contains(projection, needle, finalDeckCard: relic == null);
        Check((predicate == MorphicGrovePredicateResult.Match) == actual.Any(c => c.CardKey == needle), "Conditional Exact predicate / source accept-reject parity.");
        var partial = Beta111MorphicGroveProjector.Project(run.Rng.Seed, commitment, premises,
            [targets[0] with { OrderedSourceCandidates = null }, targets[1]]);
        Check(partial.Transforms[0].RawPrecision == PredictionPrecision.Unknown &&
            partial.Transforms[1].RawReplacement == projection.Transforms[1].RawReplacement, "Unrelated closed second output was lost.");
        Check(Beta111MorphicGroveProjector.Contains(partial, new ModelKey("CARD", "NOT_A_CARD"), false) == MorphicGrovePredicateResult.Unknown,
            "Unknown was converted to rejection.");
        return new(seed, run.Rng.Seed, character.Id.Entry, 10, unlocks == UnlockState.none ? "none" : "all",
            players.Length, multiplayer ? 1 : 0, relic?.Id.Entry, advanceOtherStreams, targets, projection, actual,
            grove.Rng.ToSerializable().counter, player.Gold, predicate);
    }


    private static void RunSingleTransformEvent(CharacterModel character, string seed, SingleBasicTransformEvent source)
    {
        var player = Player.CreateForNewRun(character, UnlockState.all, 1);
        var run = RunState.CreateForTest([player], seed: seed, ascensionLevel: 10);
        player.Gold = 100;
        var selected = player.Deck.Cards.First(c => c.Rarity == CardRarity.Basic && c.IsTransformable);
        var target = Beta111MorphicGroveAuthorityCapture.Capture("basic:0", selected);
        var before = player.Deck.Cards.ToArray();
        var premise = new MorphicGrovePremises("Authored single-Basic event choice; occurrence assumed", true, true, run.TotalFloor);
        var prediction = Beta111SingleBasicTransformProjector.Project(run.Rng.Seed, 0, source, premise, target);
        Check(before.SequenceEqual(player.Deck.Cards), "Single event predictor mutated entry deck");
        EventModel ev = source == SingleBasicTransformEvent.AromaOfChaos
            ? (EventModel)ModelDb.Event<AromaOfChaos>().ToMutable() : (EventModel)ModelDb.Event<WhisperingHollow>().ToMutable();
        Check(ev.IsAllowed(run), "Real event fixture eligible");
        ev.BeginEvent(player, null, false).GetAwaiter().GetResult();
        int prefix = source == SingleBasicTransformEvent.WhisperingHollow ? 1 : 0;
        Check(ev.Rng.ToSerializable().counter == prefix, "Actual event startup prefix");
        var rng = new MegaCrit.Sts2.Core.Random.Rng(ev.Rng.ToSerializable());
        var raw = CardFactory.CreateRandomCardForTransform(selected, false, rng);
        Check(Beta111MorphicGroveAuthorityCapture.Snapshot(raw) == prediction.RawReplacement, "Actual CardFactory / single event raw projection");
        using var selector = CardSelectCmd.UseSelector(new CommittedSelector([selected]));
        string method = source == SingleBasicTransformEvent.AromaOfChaos ? "LetGo" : "Hug";
        ((Task)ev.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ev, null)!).GetAwaiter().GetResult();
        var replacement = player.Deck.Cards.Last();
        Check(new ModelKey("CARD", replacement.Id.Entry) == prediction.RawReplacement!.CardKey, "Actual option / predicted raw identity");
        Check(ev.Rng.ToSerializable().counter == prefix + 1, "Actual single transform draw count");
        Check(before.Except([selected]).SequenceEqual(player.Deck.Cards.SkipLast(1)), "Actual target removal / replacement append");
    }

    private sealed class CommittedSelector(CardModel[] selected) : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            if (selected.Length != minSelect || selected.Length > maxSelect || selected.Except(options).Any())
                throw new InvalidOperationException("Illegal authored selection.");
            return Task.FromResult<IEnumerable<CardModel>>(selected);
        }
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<CardRewardAlternative> alternatives) => throw new InvalidOperationException("Unexpected reward choice.");
    }
}
