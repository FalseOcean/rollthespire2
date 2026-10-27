using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Presentation.Localization;

internal static class PartyPhase1BChecks
{
    private sealed class FirstCard : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect) => Task.FromResult(options.Take(1));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) => new() { card = options[0].Card };
    }
    internal static async Task RunAsync(Node host, string output)
    {
        int checks = 0;
        void Check(bool test, string name) { if (!test) throw new Exception("Phase1B:" + name); checks++; }
        void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidOperationException) { checks++; return; } throw new Exception("Expected reject: " + name); }
        var evidence = new List<object>();
        var profile = Beta111Profile.Instance;
        var detection = new GameVersionDetection("0.111.0", "0.111.0", "beta", "PartyPhase1B", true, "");
        // Fixed bounded seed search is for correctness fixtures, not unbiased performance evidence.
        foreach (var option in PartyNeowAdmission.Options)
        foreach (int count in option == BaseGameModelKeys.Relics.PhialHolster || option == BaseGameModelKeys.Relics.Kaleidoscope ? new[] { 2, 4 } : new[] { 2 })
        foreach (int ascension in option == BaseGameModelKeys.Relics.PhialHolster && count == 2 ? new[] { 0, 4, 10 } : new[] { 10 })
        {
            var characters = BaseGameModelKeys.Characters.All.Take(count).ToArray();
            TestMode.IsOn = false;
            var party = PartyRuntimeAuthorityCapture.Capture(profile, "000000000000", characters,
                characters.Select(_ => UnlockState.all.ToSerializable()).ToArray(), ascension, "0.111.0");
            var slots = option == BaseGameModelKeys.Relics.PhialHolster || option == BaseGameModelKeys.Relics.Kaleidoscope
                ? new[] { 0, count - 1 } : new[] { 0 };
            string? seed = null;
            for (ulong ordinal = 0; ordinal < 20000; ordinal++)
            {
                var candidateSeed = VisibleSeedCandidateCodec.FormatOrdinal(profile, ordinal);
                if (slots.All(slot => ModernNeowIdentityPredictor.PredictModernCore(profile.ComputeRootSeed(candidateSeed), party.Players[slot], profile, true).RelicKeys.Contains(option)))
                { seed = candidateSeed; break; }
            }
            Check(seed is not null, "legal fixture seed:" + option);
            var initial = PartySeedInformation.Project(seed!, party);
            var plans = slots.Select(slot => new PartyNeowParticipant(slot, new(option,
                slot == slots[0] && slots.Length > 1 ? PartyNeowIntent.PremiseOnly : PartyNeowIntent.ResultTarget, []))).ToArray();
            var witness = PartyNeowProjection.Find(initial, party, plans, values => values.All(v => v.Choices.All(c => c.Index == 0)))!;
            Check(witness is not null, "complete pure transaction:" + option);
            plans = witness!.Select(w => new PartyNeowParticipant(w.Slot, new(w.Option, w.Intent, w.Choices))).ToArray();
            var query = SearchQuery.Empty with { Players = Enumerable.Range(0, count).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)
                { SelectedOption = plans.SingleOrDefault(p => p.Slot == slot)?.Plan }).ToArray() };
            var targetSlot = slots.Last();
            var targeted = witness!.Single(w => w.Slot == targetSlot);
            var targets = new List<NeowStructuredEffectSearchCondition>();
            foreach (var group in targeted.Results)
            {
                var cards = group.OrderedItems.Where(e => e.Kind == PredictedEffectKind.AddCard && e.TargetKey.HasValue).SelectMany(e => Enumerable.Repeat(e.TargetKey!.Value, e.Multiplicity)).ToArray();
                var potions = group.OrderedItems.Where(e => e.Kind is PredictedEffectKind.AddPotion or PredictedEffectKind.AttemptAddPotion).Select(e => e.TargetKey!.Value).ToArray();
                if (option == BaseGameModelKeys.Relics.ArcaneScroll) targets.Add(new(option, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.ProductRelevantEffects, NeowStructuredOutputKind.Card, cards));
                else if (option == BaseGameModelKeys.Relics.PhialHolster) targets.Add(new(option, NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.GeneratedPotions, NeowStructuredOutputKind.Potion, potions));
                else if (option == BaseGameModelKeys.Relics.ScrollBoxes) targets.Add(cards.Distinct().Count() == 1 ? new(option, NeowStructuredConditionKind.SpecialOffer, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [], NeowSpecialOfferKind.ScrollBoxesTripleClaw) : new(option, NeowStructuredConditionKind.StructuredCardComposition, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, cards));
                else if (option == BaseGameModelKeys.Relics.Kaleidoscope) { }
                else if (group.SelectionPolicy == EffectSelectionPolicy.ChooseOneOrSkip) targets.Add(new(option, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, cards));
                else if (group.SelectionPolicy == EffectSelectionPolicy.OptionalClaim) targets.Add(new(option, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Potion, potions));
            }
            if (option == BaseGameModelKeys.Relics.Kaleidoscope)
            {
                var keys = targeted.Results.SelectMany(g => g.OrderedItems.Where(e => e.Kind == PredictedEffectKind.AddCard)).Select(e => e.TargetKey!.Value).ToArray();
                targets.Add(new(option, NeowStructuredConditionKind.IndependentOfferGroupTargets, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, keys, AllowDuplicateOutputs: true, KaleidoscopeGroupOrder: KaleidoscopeGroupOrderMode.ExactOrder) { KaleidoscopePositionalSlots = keys.Select(k => (ModelKey?)k).ToArray() });
            }
            query = query with { Players = query.Players.Select(p => p.Slot == targetSlot ? p with { Results = targets, SelectedOption = p.SelectedOption! with { Choices = [] } } : p).ToArray() };
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, characters[0], ascension, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
            var compiled = SearchCompiler.Compile(query, context);
            foreach (var bad in new[] { BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf, BaseGameModelKeys.Relics.NeowsBones, BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule })
                Reject(() => SearchCompiler.Compile(query with { Players = query.Players.Select(p => p.Slot == targetSlot ? p with { SelectedOption = new(bad, PartyNeowIntent.ResultTarget, []) } : p).ToArray() }, context), "unsupported transaction " + bad);
            Reject(() => SearchCompiler.Compile(query with { Players = query.Players.Select(p => p.Slot == targetSlot ? p with { SelectedOption = p.SelectedOption! with { Choices = [new("invalid-target", 99)] } } : p).ToArray() }, context), "illegal target");
            var missing = PartyNeowAdmission.Options.First(k => !initial.Players[targetSlot].Offers.Contains(k));
            Check(PartyNeowProjection.Find(initial, party, [new(targetSlot, new(missing, PartyNeowIntent.PremiseOnly, []))], _ => true) is null, "unoffered option cannot be injected");
            Reject(() => PartyNeowProjection.Find(initial with { Root = initial.Root + 1 }, party, plans, _ => true), "wrong root");
            Reject(() => PartyNeowProjection.Find(initial, party, [plans[0], plans[0]], _ => true), "duplicate execution");
            if (plans.Length > 1) Reject(() => PartyNeowProjection.Find(initial, party, plans.AsEnumerable().Reverse().ToArray(), _ => true), "illegal prefix");
            var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = profile, Detection = detection, Compatibility = RuntimeProfileRegistry.Resolve(detection) };
            var draft = new WorkbenchSearchDraft(characters[0], ascension, query, AncientOptionConditionProfile.BroadDefault)
            { Players = characters.Select((c, i) => new WorkbenchPlayerDraft(i, c, UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")).ToArray(),
                Mode = RolltheSpire2.Core.World.Snapshots.WorldGameMode.Multiplayer, GameVersion = "0.111.0", Profile = RuntimeProfileId.Beta111,
                ObservationVersion = PartyNeowAdmission.ObservationVersion };
            var persistence = new SearchWorkspace(Path.Combine(output, "results-" + count), false);
            persistence.SaveWorkbench(draft);
            Check(persistence.LoadWorkbench()!.Compile(runtime, out _).SemanticFingerprint == draft.Compile(runtime, out _).SemanticFingerprint, "v3 transaction persistence");
            var legacy = draft with { Version = 2, ObservationVersion = RolltheSpire2.Core.Authority.OrderedPartyAuthority.ObservationVersion,
                Query = query with { Players = query.Players.Select(p => p with { SelectedOption = null, Results = [] }).ToArray() } };
            persistence.SaveWorkbench(legacy);
            Check(!PartyNeowQuery.HasTransactions(persistence.LoadWorkbench()!.Compile(runtime, out _).NormalizedQuery), "v2 offer-only never guesses pickup");
            Reject(() => (draft with { Version = 2 }).Compile(runtime, out _), "v2 cannot carry hidden result plan");
            var predicted = PartyNeowQuery.Project(seed!, compiled)!;
            Check(predicted is not null, "joint result predicate witness");
            Check(predicted!.Transactions.Select(t => t.Slot).SequenceEqual(slots), "participant order excludes waiting slots");
            predicted.RequireSameObservation(PartyNeowQuery.Project(seed!, compiled)!); checks++;
            if (option == BaseGameModelKeys.Relics.HeftyTablet)
            {
                var first = PartyNeowProjection.Find(initial, party, [new(0, new(option, PartyNeowIntent.ResultTarget, []))], _ => true)!;
                Check(first[0].Choices.Single().Index == -1 && !targets.All(c => PartyNeowQuery.Matches(c, first[0])), "first identity-local witness fails full predicate");
                Check(predicted.Transactions[0].Choices.Single().Index == 0, "Exact retains later concrete witness");
                var skipped = query with { Players = query.Players.Select(p => p.Slot == 0 ? p with { SelectedOption = p.SelectedOption! with { Choices = [new("hefty-tablet-offer", -1)] } } : p).ToArray() };
                Check(PartyNeowQuery.Project(seed!, SearchCompiler.Compile(skipped, context)) is null, "explicit Skip cannot satisfy taken card predicate");
                predicted.RequireSameObservation(PartyNeowQuery.Project(seed!, compiled)!); checks++;
            }
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { PartyNeowProjection.Find(initial, party, plans, _ => true, cancelled.Token); throw new Exception("cancel not observed"); }
                catch (OperationCanceledException) { checks++; }
            }
            predicted.RequireSameObservation(PartyNeowQuery.Project(seed!, compiled)!); checks++;
            TestMode.IsOn = true;
            var players = characters.Select((key, slot) => Player.CreateForNewRun(ModelDb.AllCharacters.Single(c => c.Id.Entry == key.Entry), UnlockState.all, (ulong)(1000 + slot * 17))).ToArray();
            var acts = ActModel.GetRandomList(new Rng(MegaCrit.Sts2.Core.Helpers.StringHelper.GetDeterministicHashCode(seed!), "act_selection"), UnlockState.all, true).Select(a => a.ToMutable()).ToArray();
            var run = RunState.CreateForNewRun(players, acts, [], GameMode.Standard, ascension, seed!);
            LocalContext.NetId = players[0].NetId;
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), false);
            run.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Ancient, MegaCrit.Sts2.Core.Rooms.RoomType.Event, ModelDb.Event<Neow>().Id);
            var events = players.Select(_ => ModelDb.Event<Neow>().ToMutable()).ToArray();
            try
            {
                TestMode.IsOn = false; RunManager.Instance.GenerateRooms(); TestMode.IsOn = true;
                foreach (int slot in Enumerable.Range(0, count)) await events[slot].BeginEvent(players[slot], null, false);
                using var selector = CardSelectCmd.UseSelector(new FirstCard());
                var pureShared = NeowEffectRngContext.CreateFromRootHash(profile, initial.Root, 0);
                var pureOwners = slots.ToDictionary(slot => slot, slot => NeowEffectRngContext.CreateFromRootHash(profile, initial.Root, slot).WithShared(pureShared.Niche, pureShared.CombatPotionGeneration));
                foreach (int slot in slots) new NeowEffectProjectionEngine(profile, "phase1b-counter", initial.Root, party.Players[slot]).ProjectPartyOption(option, pureOwners[slot]);
                foreach (var w in witness!)
                {
                    var player = players[w.Slot];
                    LocalContext.NetId = player.NetId;
                    typeof(RewardsSetSynchronizer).GetField("_localPlayerId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(RunManager.Instance.RewardsSetSynchronizer, player.NetId);
                    var beforeCards = player.Deck.Cards.ToArray();
                    int beforeGold = player.Gold, beforeCapacity = player.MaxPotionCount;
                    int beforeHp = player.Creature.MaxHp;
                    await events[w.Slot].CurrentOptions.Single(o => o.Relic?.Id.Entry == option.Entry).Chosen();
                    var actualCards = player.Deck.Cards.Where(c => !beforeCards.Contains(c)).Select(c => c.Id.Entry).Order().ToArray();
                    var effects = w.Results.SelectMany(g => g.OrderedItems).ToArray();
                    var expectedCards = effects.Where(e => e.Kind == PredictedEffectKind.AddCard).SelectMany(e => Enumerable.Repeat(e.TargetKey!.Value.Entry, e.Multiplicity)).Order().ToArray();
                    Check(actualCards.SequenceEqual(expectedCards), option + " card additions P" + w.Slot + ":" + string.Join(",", actualCards) + " expected " + string.Join(",", expectedCards));
                    Check(player.Potions.Select(p => p.Id.Entry).Order().SequenceEqual(effects.Where(e => e.Kind is PredictedEffectKind.AddPotion or PredictedEffectKind.AttemptAddPotion).Select(e => e.TargetKey!.Value.Entry).Order()), option + " potions P" + w.Slot);
                    Check(player.Gold - beforeGold == effects.Where(e => e.Kind == PredictedEffectKind.ChangeGold).Sum(e => e.Amount ?? 0), "gold mutation");
                    Check(player.MaxPotionCount - beforeCapacity == effects.Where(e => e.Kind == PredictedEffectKind.ChangePotionCapacity).Sum(e => e.Amount ?? 0), "capacity mutation");
                    Check(player.Creature.MaxHp - beforeHp == effects.Where(e => e.Kind == PredictedEffectKind.ChangeMaxHp).Sum(e => e.Amount ?? 0), "HP mutation");
                    Check(player.Relics.Any(r => r.Id.Entry == option.Entry) && events[w.Slot].IsFinished, "Chosen / Obtain / complete");
                }
                Check(run.Rng.Niche.ToSerializable().counter == pureShared.Niche.CallCount && new Rng(run.Rng.Niche.ToSerializable()).NextUnsignedLong() == pureShared.Niche.Clone().NextUInt64(), "shared Niche terminal parity");
                Check(run.Rng.CombatPotionGeneration.ToSerializable().counter == pureShared.CombatPotionGeneration.CallCount && new Rng(run.Rng.CombatPotionGeneration.ToSerializable()).NextUnsignedLong() == pureShared.CombatPotionGeneration.Clone().NextUInt64(), "shared potion terminal parity");
                foreach (int slot in slots) Check(players[slot].PlayerRng.Rewards.ToSerializable().counter == pureOwners[slot].Rewards.CallCount && new Rng(players[slot].PlayerRng.Rewards.ToSerializable()).NextUnsignedLong() == pureOwners[slot].Rewards.Clone().NextUInt64(), "owner Rewards terminal parity");
                Check(Enumerable.Range(0, count).Except(slots).All(slot => !players[slot].Relics.Any(r => PartyNeowAdmission.Options.Any(k => k.Entry == r.Id.Entry))), "nonparticipants wait in full party");
                evidence.Add(new { seed, option = option.Serialized, count, ascension, witness, niche = run.Rng.Niche.ToSerializable(), potions = run.Rng.CombatPotionGeneration.ToSerializable(), rewards = players.Select(p => p.PlayerRng.Rewards.ToSerializable()).ToArray() });
            }
            finally { RunManager.Instance.CleanUp(); LocalContext.NetId = null; TestMode.IsOn = false; }
            var request = ExactSearchExecutionRequestFactory.Compile(compiled, new(seed!, 1, 1, 1));
            Check(request.Success, "production compile:" + request.Issue);
            await using var session = FamilyExecutionCoordinator.Start(request.Plan!, gpuAvailable: false);
            await session.Completion;
            Check(session.TryReadCandidate(out var candidate) && candidate?.Document?.Party is not null, "production result:" + session.GetProgress().FailureCode);
            predicted.RequireSameObservation(candidate!.Document!.Party!); checks++;
            if (option == BaseGameModelKeys.Relics.HeftyTablet)
            {
                T Field<T>(object value, string key) => (T)value.GetType().GetField(key, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
                void Set(object value, string key, object fieldValue) => value.GetType().GetField(key, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, fieldValue);
                var canvas = new Control { Size = new(1600, 900) }; host.AddChild(canvas);
                host.GetWindow().Size = new(1600, 900);
                var shell = new WorkspaceShell(); canvas.AddChild(shell); shell.InitializeWithPersistence(runtime, Path.Combine(output, "ui-result"));
                var frame = Field<QueryWorkbenchFrame>(shell, "_references");
                frame.RestoreDraft(draft); frame.Refresh("en", JsonUiTextProvider.CreateUi13("en"));
                // Current editors migrate the old SelectedOption/Results DTO into
                // player Conditions. Compare the migrated predicate, not schema fingerprints.
                var modernDraft = draft with { ObservationVersion = RolltheSpire2.Core.Authority.OrderedPartyAuthority.ObservationVersion, Query = draft.Query with {
                    Players = draft.Query.Players.Select(p => p.SelectedOption is { } selected ? p with {
                        Conditions = p.Conditions with { OpeningRoute = new(selected.Option), StructuredOpeningEffects = p.Results },
                        SelectedOption = null, Results = []
                    } : p).ToArray()
                } };
                Check(frame.CaptureDraft().Compile(runtime, out _).SemanticFingerprint == modernDraft.Compile(runtime, out _).SemanticFingerprint,
                    "SP template preserves selected result through current query migration");
                if (DisplayServer.GetName() != "headless")
                {
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "multiplayer-n-editor.png"));
                }
                Set(frame, "_target", 1);
                VisibleSeedCandidateCodec.TryParseOrdinal(profile, seed!, out var ordinal, out _, out _);
                var liveStore = Field<SearchWorkspace>(frame, "_persistence");
                var cursor = Field<SearchCursorDocument>(liveStore, "_cursor");
                cursor.NextCursor = ordinal; cursor.Initialized = true;
                Field<Button>(frame, "_start").EmitSignal(Button.SignalName.Pressed);
                Check(liveStore.Runtime.Receiving, "actual Workbench Start installs execution in the shared owner");
                while (liveStore.Runtime.Receiving) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                frame._Process(1);
                var hits = liveStore.DisplayResults.Select(row => liveStore.ResultAction(row)).ToArray();
                Check(hits.Length == 1 && hits[0].Live?.Candidate.Document?.Party?.Transactions.Count == 1, "actual Workbench N result hit");
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                var open = Field<VBoxContainer>(frame, "_resultRows").FindChildren("*", "Button", true, false)
                    .OfType<Button>().Single(b => b.Text == JsonUiTextProvider.CreateUi13("en").Get("integration.analyze"));
                open.EmitSignal(Button.SignalName.Pressed);
                var information = Field<RolltheSpire2.Ui.Pages.Analysis.AnalysisPage>(shell, "_predictor");
                Check(information.FindChildren("*", "", true, false).OfType<RolltheSpire2.Ui.Components.NeowChoiceDetailPanel>().Any(),
                    "current shared information detail template reused");
                Check(information.LastDocument?.CanonicalSeed == hits[0].Result.Seed && information.LastRequest?.PlayersCount == count &&
                    information.LastRequest?.PlayerSlotIndex == targetSlot, "result opens current information page with party context");
                // The current page projects the selected player's document, not the
                // retired whole-table Party DTO. Verify its actual selected reward.
                var displayed = information.LastRequest!.PartyOpeningChoices!.Single(c => c.RelicKey == option);
                Check(Field<int?>(information, "_preferredOpeningChoiceSlotIndex") == displayed.SlotIndex,
                    "result navigation selects the witnessed opening");
                var displayedCards = displayed.EffectGroups.SelectMany(g => g.OrderedItems)
                    .Where(e => e.Kind == PredictedEffectKind.AddCard).Select(e => e.TargetKey!.Value).ToHashSet();
                Check(targets.SelectMany(t => t.OutputKeys).All(displayedCards.Contains),
                    "selected player's information preserves the exact search card target");
                if (DisplayServer.GetName() != "headless")
                {
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "multiplayer-n-information.png"));
                }
                canvas.QueueFree(); await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        File.WriteAllText(Path.Combine(output, "party-phase1b.json"), JsonSerializer.Serialize(new { checks, evidence }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        GD.Print($"PARTY_PHASE1B_PASS checks={checks}");
    }
}
