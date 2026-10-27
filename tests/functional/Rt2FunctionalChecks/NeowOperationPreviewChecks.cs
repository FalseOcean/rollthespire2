using System.Collections.Immutable;
using System.Text.Json;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static class NeowOperationPreviewChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("NEOW_PREVIEW " + message); }
    private static ModelKey Relic(string entry) => new("RELIC", entry);
    private static PredictorOperationSnapshot Snapshot(PredictorRun baseline, string source)
    {
        long offerId = baseline.Working.NextInstanceId;
        return new("neow-preview:" + source,
            baseline.Context with { Operation = new(1, 0, 1, []) },
            baseline.Working with { Relics = [], NextInstanceId = offerId + 1, OpeningCompleted = false },
            new PredictorOperationAncientScene(new("EVENT", "NEOW"), [new(offerId, Relic(source), true, null, null, null, null, null)]), 60, 80);
    }
    private static long OfferId(PredictorOperationSnapshot snapshot) => ((PredictorOperationAncientScene)snapshot.Scene).Offers.Single().Id;
    private static IEnumerable<OperationPreviewRow> Rows(OperationPreview preview) => preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows);

    // Also accepts a real Neow scene captured by the existing native fixture.
    // Keep the actual offered ID; neither this check nor Build regenerates it.
    internal static void RunCaptured(PredictorOperationSnapshot captured, long actualOfferId)
    {
        var scene = (PredictorOperationAncientScene)captured.Scene;
        string before = JsonSerializer.Serialize(new { captured.State, captured.Context, captured.SelectedCardIds, Scene = scene });
        var first = RelicOperationPreview.Build(captured, actualOfferId);
        var second = RelicOperationPreview.Build(captured, actualOfferId);
        Check(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second) &&
            JsonSerializer.Serialize(new { captured.State, captured.Context, captured.SelectedCardIds, Scene = scene }) == before,
            "repeat projection changed captured present state/RNG or selected a real offer");
        if (first.Source.Entry == "NEOWS_BONES" && first.ContainedRelics.Select(r => r.Entry).Order()
            .SequenceEqual(new[] { "KALEIDOSCOPE", "LARGE_CAPSULE" }.Order()))
            Check(first.Faults.IsEmpty && first.Branches.All(branch =>
                branch.Sections.Where(s => s.Source.Entry == "KALEIDOSCOPE").Sum(s => s.CardGroups.Length) == 2 &&
                branch.Sections.Any(s => s.Source.Entry == "LARGE_CAPSULE" && s.Rows.Any(r => r.Action == OperationPreviewAction.ObtainRelic))),
                "each retained native Kaleidoscope/Capsule order must obtain both and expose both actual Kaleidoscope groups");
    }

    internal static void Run(PredictorRun baseline)
    {
        var simple = Snapshot(baseline, "WHETSTONE");
        RunCaptured(simple, OfferId(simple));
        var preview = RelicOperationPreview.Build(simple, OfferId(simple));
        var native = Native(simple.State.Streams.Single(s => s.Stream == PredictorStream.Niche));
        var candidates = simple.State.Deck.Select(simple.State.Card).Where(c => c.Type == EffectCardType.Attack && c.UpgradeLevel < c.MaxUpgradeLevel)
            .Select(c => new SortCard(c)).ToList();
        candidates.StableShuffle(native);
        var upgraded = Rows(preview).Where(r => r.Action == OperationPreviewAction.UpgradeCard).SelectMany(r => r.Cards).ToArray();
        Check(preview.Branches is [{ FullyEnumerated: true }] && upgraded.Select(c => c.Id).Order()
            .SequenceEqual(candidates.Take(2).Select(c => c.Card.Id).Order()) &&
            upgraded.All(c => c.UpgradeLevel == simple.State.Card(c.Id).UpgradeLevel + 1),
            "Whetstone displayed targets differ from the existing native stable-shuffle fixture");

        var fixedGain = Snapshot(baseline, "OLD_COIN");
        Check(!Rows(RelicOperationPreview.Build(fixedGain, OfferId(fixedGain))).Any(), "fixed native gold was presented as a forecast");
        var kaleido = Snapshot(baseline, "KALEIDOSCOPE");
        var grouped = RelicOperationPreview.Build(kaleido, OfferId(kaleido));
        var groups = grouped.Branches.Single().Sections.SelectMany(s => s.CardGroups).ToArray();
        Check(groups.Length == 2 && groups.Select(g => g.Id).Distinct().Count() == 2 && groups.All(g =>
            g.Cards.Length == 3 && g.MinimumSelect == 1 && g.MaximumSelect == 1 && g.CanSkip && !g.Possible) &&
            groups.SelectMany(g => g.Cards).Select(c => c.Id).Distinct().Count() == 6 &&
            grouped.Branches.Single().PendingReasons.Contains("AwaitingPlayerChoice:Rewards"),
            "Kaleidoscope lost two actual three-card reward groups or silently chose a card");

        CapsuleAutomaticNewCardUpgrade(baseline);
        BonesBothOrders(baseline);
        BonesDifferentAutomaticResultsKeepOrders(baseline);
        BonesCapsuleOrderCurseAttribution(baseline);
        BonesEquivalentCurseInstances(baseline);
        BonesCapsuleArchiveDoesNotCompleteOpenSibling(baseline);
        BonesConditionalTargets(baseline);
        KnownInputsAreNotForecasts(baseline);
        TransformGroupsAndGenericAncient(baseline);
        PotionProjection(baseline);
        PhialHolsterGenerationTrace(baseline);
        HoveredCardTargets(baseline);
        ExistingRewardContentIsNotForecast(baseline);
        FailedContinuationKeepsIndependentResults(baseline);
        Console.WriteLine("NEOW_PREVIEW native Whetstone targets; immutable actual-offer input; Kaleidoscope two groups; Capsule automatic chain; Bones two orders/conditional targets/final curse PASS");
    }

    private static void CapsuleAutomaticNewCardUpgrade(PredictorRun baseline)
    {
        var snapshot = Snapshot(baseline, "LARGE_CAPSULE");
        // Reuse the controlled bag technique from OperationSceneChecks. All
        // rarity draws fall through to this prepared sequence, isolating the
        // real automatic Arcane Scroll -> Whetstone continuation.
        var bag = new PredictorRelicBag(false, [new(Relic("ARCANE_SCROLL"), "Rare"), new(Relic("WHETSTONE"), "Rare")],
            [new("Rare", [Relic("ARCANE_SCROLL"), Relic("WHETSTONE")])], []);
        var onlySkill = baseline.Working.Deck.First(id => baseline.Working.Card(id).Type == EffectCardType.Skill);
        snapshot = snapshot with { State = snapshot.State with { Deck = [onlySkill], PersonalBag = bag } };
        // Force the scroll's actual rare pool to one attack from the captured
        // catalog. This makes the new, previously absent instance the only
        // legal Whetstone target; final-deck-only guessing cannot pass.
        var catalog = snapshot.Context.Catalog!;
        var attack = catalog.CharacterCards.First(c => c.Rarity == EffectCardRarity.Rare && c.CardType == EffectCardType.Attack);
        snapshot = snapshot with { Context = snapshot.Context with { Catalog = catalog with { CharacterCards = [attack] } } };
        var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
        var scroll = preview.Branches.Single().Sections.Single(s => s.Source.Entry == "ARCANE_SCROLL");
        var whetstone = preview.Branches.Single().Sections.Single(s => s.Source.Entry == "WHETSTONE");
        var gained = scroll.Rows.Single(r => r.Action == OperationPreviewAction.AddCard).Cards.Single();
        var upgraded = whetstone.Rows.Single(r => r.Action == OperationPreviewAction.UpgradeCard).Cards.Single();
        Check(preview.ContainedRelics.IsEmpty && Rows(preview).Where(r => r.Action == OperationPreviewAction.ObtainRelic)
            .SelectMany(r => r.Models).SequenceEqual([Relic("ARCANE_SCROLL"), Relic("WHETSTONE")]) &&
            gained.Id == upgraded.Id && gained.UpgradeLevel == 0 && upgraded.Key == attack.CardKey && upgraded.UpgradeLevel == 1 &&
            !snapshot.State.Cards.Any(c => c.Id == upgraded.Id) && preview.Branches.Single().FullyEnumerated &&
            Rows(preview).Where(r => r.Action == OperationPreviewAction.AddCard).SelectMany(r => r.Cards).All(c =>
                c.Key != catalog.CharacterStrikeKey && c.Key != catalog.CharacterDefendKey),
            "Capsule lost automatic upgrade of a newly generated card or displayed fixed basic gains");
    }

    private static void BonesBothOrders(PredictorRun baseline)
    {
        var snapshot = Snapshot(baseline, "NEOWS_BONES");
        snapshot = snapshot with { Context = snapshot.Context with { OpeningEligibleRelics = [Relic("WHETSTONE"), Relic("WAR_PAINT")] } };
        // Several instance targets are interchangeable visible cards. The two
        // source shuffles still execute, but swapping their order does not
        // change either visible upgrade group or the final curse cursor.
        snapshot = snapshot with { State = snapshot.State with { Deck = snapshot.State.Deck.Where(id =>
            snapshot.State.Card(id).Key == snapshot.Context.Catalog!.CharacterStrikeKey ||
            snapshot.State.Card(id).Key == snapshot.Context.Catalog!.CharacterDefendKey).ToImmutableArray() } };
        var generatedOrder = PredictorOperation.Preview(snapshot, new ChooseAncientOffer(OfferId(snapshot))).Rewards.Select(r => r.Key);
        var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
        Check(preview.Branches.Length == 1 && preview.Branches[0].Order.SequenceEqual(generatedOrder) &&
            preview.Branches.All(b => b.FullyEnumerated && b.PathsExamined == 1 &&
                b.Sections.Any(s => s.Source.Entry == "WHETSTONE" && s.Rows.Any(r => r.Action == OperationPreviewAction.UpgradeCard)) &&
                b.Sections.Any(s => s.Source.Entry == "WAR_PAINT" && s.Rows.Any(r => r.Action == OperationPreviewAction.UpgradeCard)) &&
                b.Sections.Any(s => s.Source.Entry == "NEOWS_BONES" && s.Rows.Any(r => r.Action == OperationPreviewAction.AddCard && r.Cards.Any(c => c.Type == EffectCardType.Curse)))) &&
            preview.CurseCandidates.Length == 1 && !preview.CursesPossible,
            "Bones failed to fold equivalent visible results or retain one certain shared root curse");
        RunCaptured(snapshot, OfferId(snapshot));
    }

    private static void BonesDifferentAutomaticResultsKeepOrders(PredictorRun baseline)
    {
        var snapshot = Snapshot(baseline, "NEOWS_BONES");
        var attack = snapshot.Context.Catalog!.CharacterCards.First(c => c.Rarity == EffectCardRarity.Rare && c.CardType == EffectCardType.Attack);
        snapshot = snapshot with { Context = snapshot.Context with { OpeningEligibleRelics = [Relic("ARCANE_SCROLL"), Relic("WHETSTONE")],
            Catalog = snapshot.Context.Catalog! with { CharacterCards = [attack] } },
            State = snapshot.State with { Deck = [snapshot.State.Deck.First(id => snapshot.State.Card(id).Type == EffectCardType.Skill)] } };
        var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
        Check(preview.Faults.IsEmpty && preview.Branches.Length == 2 && preview.Branches.All(b => b.FullyEnumerated) &&
            preview.CurseCandidates.Length == 1 && !preview.CursesPossible &&
            preview.Branches.Count(b => b.Sections.Any(s => s.Source.Entry == "WHETSTONE" &&
                s.Rows.Any(r => r.Action == OperationPreviewAction.UpgradeCard))) == 1,
            "Bones collapsed different upgrade orders or duplicated their identical certain root curse");
    }

    private static void BonesCapsuleArchiveDoesNotCompleteOpenSibling(PredictorRun baseline)
    {
        var snapshot = Snapshot(baseline, "NEOWS_BONES");
        snapshot = snapshot with
        {
            Context = snapshot.Context with { OpeningEligibleRelics = [Relic("KALEIDOSCOPE"), Relic("LARGE_CAPSULE")] },
            State = snapshot.State with { PersonalBag = new(false,
                [new(Relic("ARCANE_SCROLL"), "Rare"), new(Relic("WHETSTONE"), "Rare")],
                [new("Rare", [Relic("ARCANE_SCROLL"), Relic("WHETSTONE")])], []) }
        };
        var root = ImmutableArray.Create<PredictorInput>(new ChooseAncientOffer(OfferId(snapshot)));
        var initial = PredictorOperation.Preview(snapshot, root);
        var capsule = initial.Rewards.Single(r => r.Key.Entry == "LARGE_CAPSULE");
        var kaleido = initial.Rewards.Single(r => r.Key.Entry == "KALEIDOSCOPE");
        var capsuleFirst = PredictorOperation.Preview(snapshot, root.Add(new TakeReward(capsule.Id)));
        Check(capsuleFirst.Rewards.Any(r => r.Id == kaleido.Id && r.Status == PredictorRewardStatus.Open) &&
            initial.Rewards.All(b => capsuleFirst.CompletedChildRewards.Any(r => r.Id == b.Id && r.Status == PredictorRewardStatus.Open)),
            "controlled Capsule fixture did not reproduce archived-but-open parent Bones rewards");
        var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
        Check(preview.Faults.IsEmpty && preview.Branches.All(branch => branch.FullyEnumerated &&
            branch.Sections.Any(s => s.Source.Entry == "LARGE_CAPSULE") &&
            branch.Sections.Where(s => s.Source.Entry == "KALEIDOSCOPE").Sum(s => s.CardGroups.Length) == 2 &&
            branch.Sections.Any(s => s.Source.Entry == "NEOWS_BONES" &&
                s.Rows.Any(r => r.Action == OperationPreviewAction.AddCard && r.Cards.Any(c => c.Type == EffectCardType.Curse)))),
            "archived open siblings prematurely ended a retained Bones order or hid Kaleidoscope/final curse");
        foreach (var branch in preview.Branches)
        {
            var inputs = root;
            PredictorOperationResult observed = initial;
            foreach (var key in branch.Order)
            {
                var reward = initial.Rewards.Single(r => r.Key == key);
                inputs = inputs.Add(new TakeReward(reward.Id));
                observed = PredictorOperation.Preview(snapshot, inputs);
                if (key.Entry == "KALEIDOSCOPE")
                {
                    var actualGroups = observed.Rewards.Where(r => r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open).ToArray();
                    var displayed = branch.Sections.Where(s => s.Source == key).SelectMany(s => s.CardGroups).ToArray();
                    Check(actualGroups.Length == 2 && displayed.Length == 2 && actualGroups.All(actual =>
                        displayed.Any(group => group.Id == actual.Id && group.Cards.SequenceEqual(actual.Cards))),
                        "retained order displayed different Kaleidoscope candidates from its explicit obtain sequence");
                    inputs = inputs.Add(new LeaveRewards());
                    observed = PredictorOperation.Preview(snapshot, inputs);
                }
            }
            Check(observed.Waiting.Phase == PredictorPhase.Complete &&
                new[] { "KALEIDOSCOPE", "LARGE_CAPSULE" }.All(key => observed.State.Relics.Any(r => r.Key.Entry == key)),
                "retained Bones order failed to obtain both explicit relic rewards");
        }
        RunCaptured(snapshot, OfferId(snapshot));
    }

    private static void BonesCapsuleOrderCurseAttribution(PredictorRun baseline)
    {
        foreach (string upgrade in new[] { "WHETSTONE", "WAR_PAINT" })
        {
            var snapshot = Snapshot(baseline, "NEOWS_BONES");
            snapshot = snapshot with
            {
                Context = snapshot.Context with { OpeningEligibleRelics = [Relic(upgrade), Relic("LARGE_CAPSULE")] },
                State = snapshot.State with { PersonalBag = new(false,
                    [new(Relic("OLD_COIN"), "Rare"), new(Relic("ANCHOR"), "Rare")],
                    [new("Rare", [Relic("OLD_COIN"), Relic("ANCHOR")])], []) }
            };
            var root = ImmutableArray.Create<PredictorInput>(new ChooseAncientOffer(OfferId(snapshot)));
            PredictorReward[] rewards = [];
            PredictorOperationResult[] outcomes = [];
            // Select a controlled cursor at which the extra basic card in a
            // Capsule-first shuffle changes the final curse. This is fixture
            // construction, not a claim about every seed's visible identity.
            for (int cursor = 0; cursor < 16; cursor++)
            {
                rewards = PredictorOperation.Preview(snapshot, root).Rewards.ToArray();
                outcomes = [PredictorOperation.Preview(snapshot, root.Add(new TakeReward(rewards[0].Id)).Add(new TakeReward(rewards[1].Id))),
                    PredictorOperation.Preview(snapshot, root.Add(new TakeReward(rewards[1].Id)).Add(new TakeReward(rewards[0].Id)))];
                if (outcomes[0].State.Card(outcomes[0].State.Deck[^1]).Key !=
                    outcomes[1].State.Card(outcomes[1].State.Deck[^1]).Key) break;
                snapshot = snapshot with { State = snapshot.State.Draw(PredictorStream.Niche, 0, 17).State };
            }
            Check(outcomes.All(o => o.Disposition == PredictorInputResult.Accepted && o.Waiting.Phase == PredictorPhase.Complete) &&
                outcomes[0].State.Card(outcomes[0].State.Deck[^1]).Key != outcomes[1].State.Card(outcomes[1].State.Deck[^1]).Key,
                upgrade + "/Capsule fixture did not produce two computed order-dependent curses");
            var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
            Check(preview.Faults.IsEmpty && preview.Branches.Length == 2 && preview.CurseCandidates.IsEmpty &&
                preview.Branches.All(b => b.FullyEnumerated), upgrade + "/Capsule curses were incorrectly lifted to a root union");
            foreach (var branch in preview.Branches)
            {
                var outcome = outcomes[branch.Order[0] == rewards[0].Key ? 0 : 1];
                var row = branch.Sections.Single(s => s.Source.Entry == "NEOWS_BONES").Rows
                    .Single(r => r.Action == OperationPreviewAction.AddCard);
                Check(!row.Possible && row.Cards is [var curse] &&
                    (curse with { Id = 0 }) == (outcome.State.Card(outcome.State.Deck[^1]) with { Id = 0 }),
                    upgrade + "/Capsule known niche consumption lost its certain branch curse");
            }
        }
    }

    private static void BonesEquivalentCurseInstances(PredictorRun baseline)
    {
        // A small constructed Claws choice allocates zero, one or two fresh
        // cards before the final draw. One curse identity isolates instance-ID
        // differences from genuine visible uncertainty.
        var snapshot = Snapshot(baseline, "NEOWS_BONES");
        var curseKey = snapshot.Context.Catalog!.UnlockedCurseCards.First();
        snapshot = snapshot with
        {
            Context = snapshot.Context with { OpeningEligibleRelics = [Relic("CLAWS"), Relic("OLD_COIN")],
                Catalog = snapshot.Context.Catalog! with { UnlockedCurseCards = [curseKey] } },
            State = snapshot.State with { Deck = snapshot.State.Deck.Take(2).ToImmutableArray() }
        };
        var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
        Check(preview.Faults.IsEmpty && preview.Branches.All(b => b.FullyEnumerated && b.PathsExamined == 4) &&
            preview.CurseCandidates is [var curse] && curse.Key == curseKey && !preview.CursesPossible &&
            preview.Branches.SelectMany(b => b.Sections).Where(s => s.Source.Entry == "NEOWS_BONES")
                .SelectMany(s => s.Rows).All(r => !r.Possible && r.Cards.Length == 1),
            "identical Bones curses became possible because earlier choices allocated different instance IDs");
    }

    private static void BonesConditionalTargets(PredictorRun baseline)
    {
        var snapshot = Snapshot(baseline, "NEOWS_BONES");
        snapshot = snapshot with { Context = snapshot.Context with { OpeningEligibleRelics = [Relic("NEW_LEAF"), Relic("WHETSTONE")] } };
        var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
        var candidates = PredictorCardChanges.TransformCandidates(snapshot.Context, snapshot.State);
        Check(preview.Branches.Length == 2 && preview.Branches.All(b => b.FullyEnumerated && b.PathsExamined == candidates.Length) &&
            preview.Branches.SelectMany(b => b.Sections).Where(s => s.Source.Entry == "NEW_LEAF")
                .SelectMany(s => s.Rows).Any(r => r.Action == OperationPreviewAction.TransformCard) &&
            preview.Branches.SelectMany(b => b.Sections).Where(s => s.Source.Entry == "NEW_LEAF")
                .SelectMany(s => s.CardGroups).All(g => !g.Cards.Any(c => candidates.Contains(c.Id))) &&
            preview.Branches.Any(b => b.Sections.Any(s => s.Source.Entry == "WHETSTONE" && s.Rows.Any(r => r.Possible))),
            "Bones used an implicit transform target or claimed an unenumerated target set was complete");
        var root = ImmutableArray.Create<PredictorInput>(new ChooseAncientOffer(OfferId(snapshot)));
        var rewards = PredictorOperation.Preview(snapshot, root).Rewards;
        foreach (var branch in preview.Branches)
        {
            var curses = new HashSet<PredictorCard>();
            foreach (long target in candidates)
            {
                var inputs = root;
                foreach (var key in branch.Order)
                {
                    inputs = inputs.Add(new TakeReward(rewards.Single(r => r.Key == key).Id));
                    if (key.Entry == "NEW_LEAF") inputs = inputs.Add(new SelectTransformationCard(target));
                }
                var result = PredictorOperation.Preview(snapshot, inputs);
                curses.Add(result.State.Card(result.State.Deck[^1]) with { Id = 0 });
            }
            var row = branch.Sections.Single(s => s.Source.Entry == "NEOWS_BONES").Rows
                .Single(r => r.Action == OperationPreviewAction.AddCard);
            Check(row.Cards.Select(c => c with { Id = 0 }).ToHashSet().SetEquals(curses) &&
                row.Possible == (curses.Count > 1),
                "Bones branch curse identities/certainty disagree with its enumerated explicit transform inputs");
        }
    }

    private static void KnownInputsAreNotForecasts(PredictorRun baseline)
    {
        // A controlled public prepared-card offer verifies the projection
        // rule; this does not assert Dusty Tome belongs to vanilla Neow's pool.
        var dusty = Snapshot(baseline, "DUSTY_TOME");
        var ancient = (PredictorOperationAncientScene)dusty.Scene;
        dusty = dusty with { Scene = ancient with { Offers = [ancient.Offers.Single() with
            { PreparedCard = dusty.Context.Catalog!.DustyTomeCards.First() }] } };
        var knownCard = RelicOperationPreview.Build(dusty, OfferId(dusty));
        Check(knownCard.Faults.IsEmpty && knownCard.Branches.Single().FullyEnumerated &&
            knownCard.Branches.Single().Sections.IsEmpty,
            "Dusty Tome's publicly prepared card was presented as a random forecast");
    }

    private static void TransformGroupsAndGenericAncient(PredictorRun baseline)
    {
        foreach (string source in new[] { "NEW_LEAF", "ASTROLABE" })
        {
            var snapshot = Snapshot(baseline, source);
            // Distinct transformation pools make later-slot tuple-order bugs
            // observable; repeated native copies still exercise query reduction.
            var catalog = snapshot.Context.Catalog!;
            var originalClasses = snapshot.State.Deck.DistinctBy(id => snapshot.State.Card(id).Key).Take(3).ToImmutableArray();
            var starterPool = PredictorCardChanges.Definition(snapshot.Context,
                snapshot.State.Card(snapshot.State.Deck[0]).Key).TransformationPool;
            var extraPools = catalog.CardDefinitions.Where(definition => definition.Prototype.Removable && !definition.IsQuest &&
                definition.TransformationPool != starterPool && catalog.TransformationPools.Any(pool =>
                    pool.Key == definition.TransformationPool && pool.Cards.Count(key => key != definition.Prototype.Key) > 0))
                .DistinctBy(definition => definition.TransformationPool).Take(2).ToArray();
            Check(extraPools.Length == 2, "rank-order fixture requires two additional native transform pools");
            var extraIds = new List<long>();
            foreach (var definition in extraPools)
            {
                snapshot = snapshot with { State = PredictorObtainSources.AddCard(snapshot.Context, snapshot.State, definition.Prototype) };
                extraIds.Add(snapshot.State.Deck[^1]);
            }
            long duplicateId = snapshot.State.NextInstanceId;
            var duplicate = snapshot.State.Card(originalClasses[0]) with { Id = duplicateId };
            var deck = originalClasses.Concat(extraIds).Append(duplicateId).Reverse().ToImmutableArray();
            snapshot = snapshot with { State = snapshot.State with
                { Cards = snapshot.State.Cards.Add(duplicate), NextInstanceId = duplicateId + 1, Deck = deck } };
            var ancient = (PredictorOperationAncientScene)snapshot.Scene;
            snapshot = snapshot with { Scene = ancient with { Ancient = new("EVENT", "DARV") } };
            var root = ImmutableArray.Create<PredictorInput>(new ChooseAncientOffer(OfferId(snapshot)));
            var initial = PredictorOperation.Preview(snapshot, root);
            var projectionTime = System.Diagnostics.Stopwatch.StartNew();
            var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
            projectionTime.Stop();
            var groups = preview.Branches.Single().Sections.Where(s => s.Source.Entry == source)
                .SelectMany(s => s.CardGroups).ToArray();
            var candidates = initial.State.Deck.Where(initial.Waiting.Selection.CandidateCardIds.Contains).ToImmutableArray();
            var expected = new Dictionary<int, List<(long Original, PredictorCard Card)>>();
            var equivalentInputs = new HashSet<string>();
            int paths = 0;
            var exhaustiveTime = System.Diagnostics.Stopwatch.StartNew();
            foreach (var ids in source == "NEW_LEAF" ? candidates.Select(id => ImmutableArray.Create(id)) :
                candidates.SelectMany(a => candidates.Where(b => b != a).SelectMany(b => candidates.Where(c => c != a && c != b)
                    .Select(c => ImmutableArray.Create(a, b, c)))))
            {
                PredictorInput input = source == "NEW_LEAF" ? new SelectTransformationCard(ids[0]) : new ChooseObtainDeckCards(ids);
                var result = PredictorOperation.Preview(snapshot, root.Add(input));
                Check(result.Disposition == PredictorInputResult.Accepted, "explicit transform reference input rejected");
                var changes = result.Effects.SelectMany(e => e.Transformations).ToArray();
                for (int i = 0; i < changes.Length; i++)
                {
                    if (!expected.TryGetValue(i, out var cards)) expected.Add(i, cards = []);
                    var card = result.State.Card(changes.Single(change => change.OriginalId == ids[i]).ReplacementId) with { Id = 0 };
                    cards.Add((ids[i], card));
                }
                paths++;
                equivalentInputs.Add(string.Join("|", ids.Select(id => initial.State.Card(id) with { Id = 0 })));
            }
            exhaustiveTime.Stop();
            if (source == "ASTROLABE")
                Check(new[] { 1, 2 }.All(slot => !expected[slot].Select(item => item.Card).Distinct().SequenceEqual(
                    expected[slot].OrderBy(item => initial.State.Deck.IndexOf(item.Original)).Select(item => item.Card).Distinct())),
                    "rank-order fixture must expose tuple traversal's incorrect second and third slot candidate order");
            Check(preview.Faults.IsEmpty && preview.Branches.Single().FullyEnumerated, source + " projection has faults or incomplete paths");
            Check(preview.Branches.Single().PathsExamined == equivalentInputs.Count,
                source + $" query reduction differs: actual={preview.Branches.Single().PathsExamined}; expected={equivalentInputs.Count}; exhaustive={paths}");
            Check(equivalentInputs.Count < paths, source + " fixture did not retain semantically equivalent native copies");
            Check(groups.Length == (source == "NEW_LEAF" ? 1 : 3), source + " lost transform ordinal groups");
            foreach (var (group, slot) in groups.Select((group, slot) => (group, slot)))
                Check(group.Cards.Select(card => card with { Id = 0 }).SequenceEqual(expected[slot]
                    .OrderBy(item => initial.State.Deck.IndexOf(item.Original)).Select(item => item.Card).Distinct()),
                    source + $" slot {slot + 1} candidates differ from independent source-ranked reference");
            Check(groups.All(group => group.Cards.Select(card => card with { Id = 0 }).Distinct().Count() == group.Cards.Length),
                source + " retained semantic duplicate candidates");
            Check(groups.SelectMany(group => group.Cards).All(card => !snapshot.State.Deck.Contains(card.Id)),
                source + " displayed an original instance as a transform result");
            Console.WriteLine($"TRANSFORM_PREVIEW {source}; exhaustiveInputs={paths}; modelExecutions={preview.Branches.Single().PathsExamined}; exhaustiveMs={exhaustiveTime.Elapsed.TotalMilliseconds:F2}; projectionMs={projectionTime.Elapsed.TotalMilliseconds:F2}; ordinalCandidateParity=true");
            if (source == "ASTROLABE")
            {
                long hookId = snapshot.State.NextInstanceId;
                var withHook = snapshot with { State = snapshot.State with
                    { Relics = [new(hookId, Relic("BING_BONG"), 0)], NextInstanceId = hookId + 1 } };
                var hookPreview = RelicOperationPreview.Build(withHook, OfferId(withHook));
                Check(hookPreview.Faults.IsEmpty && hookPreview.Branches.Single().FullyEnumerated &&
                    hookPreview.Branches.Single().PathsExamined == paths,
                    "Astrolabe merged target instances across a card-add listener context");
            }
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try { RelicOperationPreview.Build(snapshot, root, Relic(source), cancellation.Token); throw new InvalidOperationException("canceled transform completed"); }
            catch (OperationCanceledException) { }
        }
    }

    private static void PotionProjection(PredictorRun baseline)
    {
        var snapshot = Snapshot(baseline, "OLD_COIN");
        long id = snapshot.State.NextInstanceId;
        var brew = new ModelKey("POTION", "ENTROPIC_BREW");
        snapshot = snapshot with
        {
            Context = snapshot.Context with { PotionPool =
                [new(new("POTION", "BLOOD_POTION"), PredictorPotionRarity.Common),
                 new(new("POTION", "FRUIT_JUICE"), PredictorPotionRarity.Uncommon),
                 new(brew, PredictorPotionRarity.Rare)] },
            State = snapshot.State with { NextInstanceId = id + 2,
                PotionSlots = [new(id, brew), new(id + 1, new("POTION", "BLOOD_POTION")), null] }
        };
        var before = snapshot.State;
        var preview = PotionOperationPreview.Build(snapshot, brew, 0);
        Check(preview.Faults.IsEmpty && Rows(preview).Single().Models.Length == 3 && Rows(preview).Single().Label == "EntropicOtherSlotsEmpty" &&
            preview.Notices.IsEmpty &&
            snapshot.State == before && preview.Branches.Single().FullyEnumerated,
            "Entropic hypothetical did not clear all other detached slots or lost its premise");
        Check(snapshot.Scene is PredictorOperationAncientScene &&
            PredictorOperation.Preview(snapshot, new UseOutOfCombatPotion(0, id)).Disposition != PredictorInputResult.Accepted,
            "Brew hypothetical fixture did not independently cover a screen that rejects actual use");
        var sozu = snapshot with { State = snapshot.State with { Relics = [new(id + 2, Relic("SOZU"), 0)], NextInstanceId = id + 3 } };
        var blocked = PotionOperationPreview.Build(sozu, brew, 0);
        Check(Rows(blocked).Single().Models.Length == 1 && Rows(blocked).Single().Label == "EntropicOtherSlotsEmpty:1:0" &&
            blocked.Notices.IsEmpty,
            "Entropic Sozu generation was mistaken for successful acquisition");

        var catalog = snapshot.Context.Catalog!;
        PredictorOperationPotionCard Copy(ModelKey key)
        {
            var model = ModelDb.GetById<CardModel>(new(key.Category, key.Entry));
            return new(catalog.CardDefinitions.Single(d => d.Prototype.Key == key).Prototype,
                model.Rarity.ToString(), model.CanBeGeneratedInCombat, model.MultiplayerConstraint.ToString());
        }
        var stream = snapshot.State.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var context = snapshot.Context with { Operation = snapshot.Context.Operation! with
        {
            CombatCardRng = new(stream.S0, stream.S1, stream.S2, stream.S3, stream.Calls),
            CombatCardPools = new(catalog.CharacterCards.Select(c => Copy(c.CardKey)).ToImmutableArray(),
                catalog.ColorlessCards.Select(c => Copy(c.CardKey)).ToImmutableArray())
        } };
        foreach (string entry in new[] { "ATTACK_POTION", "SKILL_POTION", "POWER_POTION", "COLORLESS_POTION", "COSMIC_CONCOCTION", "OROBIC_ACID" })
        {
            var potion = new ModelKey("POTION", entry);
            var captured = snapshot with { Context = context, State = snapshot.State with { PotionSlots = [new(id, potion)] } };
            var result = PotionOperationPreview.Build(context, potion);
            var expected = PredictorOperationPotionQueries.Query(context, potion);
            Check(result.Branches.Single().Sections.SelectMany(s => s.CardGroups).SelectMany(g => g.Cards)
                .SequenceEqual(expected.Cards.Select(c => c.Card)) && result.Notices.IsEmpty &&
                result.Branches.Single().Sections.Single().Label == "PotionCombatUse" && captured.State.Streams == snapshot.State.Streams,
                "potion display lost ordered identity/upgrade metadata or conditional premise: " + entry);
        }

        PredictorOperationPotionObservation Observe(ImmutableArray<PredictorPotion?> inventory, int? held = 0,
            PredictorPotion? offered = null, bool inCombat = true, bool hasSozu = false) =>
            new(context, new(offered ?? inventory[held!.Value]!, held, offered == null ? null : 90001), inventory,
                snapshot.State.Streams.Single(s => s.Stream == PredictorStream.CombatPotionGeneration), inCombat, hasSozu, "detached-potion-fixture");
        static string CardsKey(IEnumerable<PredictorCard> cards) => string.Join("|", cards.Select(c => c.Key.Entry + ":" + c.UpgradeLevel));
        static IEnumerable<OperationPreviewCardGroup> Groups(OperationPreview result) =>
            result.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups);
        var attack = new ModelKey("POTION", "ATTACK_POTION");
        var skill = new ModelKey("POTION", "SKILL_POTION");
        var acid = new ModelKey("POTION", "OROBIC_ACID");
        var colorless = new ModelKey("POTION", "COLORLESS_POTION");
        var blood = new ModelKey("POTION", "BLOOD_POTION");
        ImmutableArray<PredictorPotion?> mixed = [new(id, attack), new(id + 1, skill), new(id + 2, acid), new(id + 3, colorless)];
        var observation = Observe(mixed);
        string frozen = JsonSerializer.Serialize(observation);
        var sequence = PotionOperationPreview.Build(observation);
        var expectedByOrdinal = Enumerable.Range(1, mixed.Length).ToDictionary(i => i, _ => new HashSet<string>());
        // Independent exhaustive order oracle for this tiny actual inventory;
        // production uses deduplicated predecessor cursors, not permutations.
        void Orders(PredictorContext current, ImmutableArray<int> remaining, int ordinal)
        {
            foreach (int index in remaining)
            {
                var next = PredictorOperationPotionQueries.Query(current, mixed[index]!.Key);
                if (index == 0) expectedByOrdinal[ordinal].Add(CardsKey(next.Cards.Select(c => c.Card)));
                Orders(current with { Operation = current.Operation! with { CombatCardRng = next.RngAfter } }, remaining.Remove(index), ordinal + 1);
            }
        }
        Orders(context, [0, 1, 2, 3], 1);
        foreach (var (ordinal, outcomes) in expectedByOrdinal)
        {
            var actual = Groups(sequence).Where(group => group.Label == "PotionUseStep:" + ordinal).ToArray();
            Check(actual.Select(group => CardsKey(group.Cards)).ToHashSet().SetEquals(outcomes) &&
                actual.All(group => group.EarliestFirst && group.Possible == (outcomes.Count > 1)),
                "mixed actual potion order lost a distinct prefix cursor or invented slot order: " + ordinal);
        }
        Check(sequence.Style!.Cards == OperationCardLayout.Groups && sequence.Faults.IsEmpty &&
            JsonSerializer.Serialize(observation) == frozen &&
            JsonSerializer.Serialize(PotionOperationPreview.Build(observation with { InCombat = false })) == JsonSerializer.Serialize(sequence),
            "potion sequence changed its detached observation or differed across combat/ordinary hover surfaces");

        var duplicates = PotionOperationPreview.Build(Observe([new(id, attack), new(id + 1, attack), new(id + 2, attack)]));
        Check(Groups(duplicates).Count() == 3 && Groups(duplicates).All(group => !group.Possible),
            "equivalent actual bottles were expanded into duplicate order alternatives");
        var single = PotionOperationPreview.Build(Observe([new(id, attack), new(id + 1, blood)]));
        Check(single.Style!.Cards == OperationCardLayout.Parallel && Groups(single).Single().Label == null,
            "unrelated potion created a fictional card-generation ordinal");
        var reward = new PredictorPotion(id + 99, acid);
        var loot = PotionOperationPreview.Build(Observe([new(id, attack), new(id + 1, skill), new(id + 2, blood)], null, reward));
        Check(loot.Notices.Contains("PotionAfterAcquisition") && loot.Notices.Contains("PotionAcquisitionRequiresSlot") &&
            Groups(loot).All(group => group.Label != "PotionUseStep:4") &&
            Groups(loot).Any(group => group.Label == "PotionUseStep:3") &&
            Groups(loot).Where(group => group.Label == "PotionUseStep:3").All(group => group.Possible),
            "full-inventory offered potion invented capacity or concealed the required replacement");

        var actualBrew = PotionOperationPreview.Build(Observe(snapshot.State.PotionSlots));
        var actualExpected = PredictorSettlementEffects.UseEntropicBrew(snapshot.State, snapshot.Context.PotionPool, 0);
        Check(Rows(actualBrew).Single().Models.SequenceEqual(actualExpected.Generated.Select(p => p.Key)) &&
            Rows(actualBrew).Single().Label == "EntropicCurrentSlots:2:2",
            "live Entropic preview cleared actual occupied slots or diverged from settlement");
        var twoBrews = Observe([new(id, brew), new(id + 1, brew), null]);
        var brewSequence = PotionOperationPreview.Build(twoBrews);
        var firstBrew = PredictorSettlementEffects.UseEntropicBrew(snapshot.State with { PotionSlots = twoBrews.Inventory }, context.PotionPool, 0);
        var secondBrew = PredictorSettlementEffects.UseEntropicBrew(firstBrew.State, context.PotionPool, 1);
        var brewRows = Rows(brewSequence).ToArray();
        Check(brewRows.Length == 2 && brewRows[0].Label == "EntropicUseStep:1:2:2" && brewRows[1].Label == "EntropicUseStep:2:1:1" &&
            brewRows[0].Models.SequenceEqual(firstBrew.Generated.Select(p => p.Key)) &&
            brewRows[1].Models.SequenceEqual(secondBrew.Generated.Select(p => p.Key)),
            "Entropic sequence lost occupancy/RNG continuation or included newly generated brews");
        var blockedSequence = PotionOperationPreview.Build(twoBrews with { HasSozu = true });
        Check(Rows(blockedSequence).Select(row => row.Label).SequenceEqual(new[] { "EntropicUseStep:1:1:0", "EntropicUseStep:2:1:0" }),
            "Sozu preview claimed generated potions were obtained");
        var onlyBrewPool = twoBrews with { Context = context with { PotionPool =
            [new(brew, PredictorPotionRarity.Common), new(brew, PredictorPotionRarity.Uncommon), new(brew, PredictorPotionRarity.Rare)] } };
        Check(Rows(PotionOperationPreview.Build(onlyBrewPool)).Count() == 2,
            "newly generated Entropic bottles extended the actual original-bottle sequence");
        using var canceledPotion = new CancellationTokenSource();
        canceledPotion.Cancel();
        try { PotionOperationPreview.Build(observation, canceledPotion.Token); throw new InvalidOperationException("canceled potion sequence completed"); }
        catch (OperationCanceledException) { }
    }

    private static void PhialHolsterGenerationTrace(PredictorRun baseline)
    {
        foreach (bool sozu in new[] { false, true })
        {
            var snapshot = Snapshot(baseline, "PHIAL_HOLSTER");
            long id = snapshot.State.NextInstanceId;
            var potion = new ModelKey("POTION", "BLOOD_POTION");
            snapshot = snapshot with
            {
                Context = snapshot.Context with { PotionPool =
                    [new(potion, PredictorPotionRarity.Common), new(potion, PredictorPotionRarity.Uncommon), new(potion, PredictorPotionRarity.Rare)] },
                State = snapshot.State with
                {
                    Relics = sozu ? [new(id + 2, Relic("SOZU"), 0)] : [],
                    PotionSlots = [new(id, potion), new(id + 1, potion)], NextInstanceId = id + 3
                }
            };
            var expectedRng = snapshot.State;
            var generatedKeys = PredictorRewardGeneration.GeneratePotions(snapshot.Context, ref expectedRng, 2,
                PredictorStream.CombatPotionGeneration);
            var result = PredictorOperation.Preview(snapshot, new ChooseAncientOffer(OfferId(snapshot)));
            var trace = result.Effects.Single(e => e.Action == "PhialHolster").GeneratedPotions;
            var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
            Check(trace.Select(p => p.Key).SequenceEqual(generatedKeys) && trace.Length == 2 && trace[0].Id != trace[1].Id &&
                result.State.Streams.SequenceEqual(expectedRng.Streams) && result.State.NextInstanceId == snapshot.State.NextInstanceId + 3 &&
                result.State.PotionSlots.Length == 3 && result.State.PotionSlots.Count(p => p != null) == (sozu ? 2 : 3) &&
                Rows(preview).Single(r => r.Action == OperationPreviewAction.GeneratePotion).Models.Length == 2,
                "Holster origin trace lost blocked/full-slot identities, duplicates, or changed numerical behavior");
        }
    }

    private static void HoveredCardTargets(PredictorRun baseline)
    {
        var original = Snapshot(baseline, "OLD_COIN");
        var colorless = original.Context.Catalog!.CardDefinitions.First(definition => definition.Prototype.Removable &&
            definition.TransformationPool.Entry.Contains("COLORLESS", StringComparison.Ordinal) &&
            original.Context.Catalog.TransformationPools.Any(pool => pool.Key == definition.TransformationPool &&
                pool.Cards.Count(key => key != definition.Prototype.Key) > 0));
        original = original with { State = PredictorObtainSources.AddCard(original.Context, original.State, colorless.Prototype) };
        long colorlessId = original.State.Deck[^1];
        var legal = PredictorCardChanges.TransformCandidates(original.Context, original.State);
        var deck = legal.Where(id => id != colorlessId).DistinctBy(id => original.State.Card(id).Key)
            .Take(3).Append(colorlessId).ToImmutableArray();
        long entryId = original.State.NextInstanceId;
        var source = Relic("ASTROLABE");
        var entry = new PredictorShopEntry(entryId, PredictorShopEntryKind.Relic, source, null, 100, false, true, 0);
        var shop = original with { State = original.State with { Deck = deck, GoldBudget = 500, NextInstanceId = entryId + 1 },
            Scene = new PredictorOperationShopScene([entry], true) };
        var paid = PredictorOperation.Preview(shop, new BuyShopEntry(entryId, 0));
        Check(paid.Disposition == PredictorInputResult.Accepted && paid.Waiting.Phase == PredictorPhase.ObtainDeckCards,
            "card-target paid Astrolabe fixture did not enter native target boundary");
        var candidates = paid.Waiting.Selection.CandidateCardIds;
        var choice = new PredictorOperationDeckObtain(paid.State.Relics.Single(r => r.Key == source).Id,
            source, PredictorObtainDeckOperation.Astrolabe, 3, candidates);
        var captured = shop with { State = paid.State,
            Scene = new PredictorOperationShopScene([entry], true, PendingRelic: new(entryId, 500, false, choice)),
            SelectedCardIds = [candidates[1]] };
        var before = captured.State;
        var incomplete = CardTargetOperationPreview.Build(captured, candidates[0]);
        var groups = incomplete.Branches.Single().Sections.Where(s => s.Source == source).SelectMany(s => s.CardGroups).ToArray();
        // The same hovered original appears at every hypothetical ordinal.
        // The current (second) ordinal respects the already selected first card.
        var expected = Enumerable.Range(0, 3).Select(_ => new HashSet<PredictorCard>()).ToArray();
        foreach (long third in candidates.Where(id => id != candidates[1] && id != candidates[0]))
            foreach (var ids in new[] {
                ImmutableArray.Create(candidates[0], candidates[1], third),
                ImmutableArray.Create(candidates[1], candidates[0], third),
                ImmutableArray.Create(candidates[1], third, candidates[0]) })
            {
                var exact = PredictorOperation.Preview(captured, new ChooseObtainDeckCards(ids));
                Check(exact.State.GoldBudget == paid.State.GoldBudget && exact.State.Relics.Count(r => r.Key == source) == 1,
                    "paid target continuation replayed payment or acquisition");
                var replacement = exact.Effects.SelectMany(effect => effect.Transformations)
                    .Single(change => change.OriginalId == candidates[0]);
                expected[ids.IndexOf(candidates[0])].Add(exact.State.Card(replacement.ReplacementId) with { Id = 0 });
            }
        Check(incomplete.Faults.IsEmpty && incomplete.Branches.Single().FullyEnumerated && groups.Length == 3 &&
            groups.Select((group, slot) => group.Highlighted == (slot == 1) &&
                group.Cards.Select(card => card with { Id = 0 }).ToHashSet().SetEquals(expected[slot])).All(value => value) &&
            incomplete.Style is { Text: OperationTextMode.None, Cards: OperationCardLayout.Parallel } &&
            captured.State == before && captured.SelectedCardIds.SequenceEqual([candidates[1]]),
            "hovered Astrolabe mixed other originals into its flat per-ordinal forecasts or changed selected order/state");
        var alreadySelected = CardTargetOperationPreview.Build(captured, candidates[1]);
        Check(alreadySelected.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups)
                .Where(group => group.Highlighted).Single().Label == "1",
            "hovering a selected card changed its actual transform ordinal");
        var fullIds = ImmutableArray.Create(candidates[1], candidates[0], candidates[2]);
        var full = CardTargetOperationPreview.Build(captured with { SelectedCardIds = fullIds }, candidates[2]);
        var fullExact = PredictorOperation.Preview(captured, new ChooseObtainDeckCards(fullIds));
        var highlighted = full.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups)
            .Single(group => group.Highlighted);
        var fullChange = fullExact.Effects.SelectMany(effect => effect.Transformations)
            .Single(change => change.OriginalId == candidates[2]);
        Check(full.Faults.IsEmpty && highlighted.Label == "3" && highlighted.Cards is [var finalCard] &&
            (finalCard with { Id = 0 }) == (fullExact.State.Card(fullChange.ReplacementId) with { Id = 0 }),
            "completed Astrolabe selection lost the hovered original's actual final upgraded result");
        VerifyConfirmation(captured, fullIds);
        VerifyConfirmation(captured, fullIds.Reverse().ToImmutableArray());
        var unselected = captured with { SelectedCardIds = [] };
        var mixedPreview = CardTargetOperationPreview.Build(unselected, colorlessId);
        var mixedExpected = Enumerable.Range(0, 3).Select(_ => new HashSet<PredictorCard>()).ToArray();
        foreach (long first in candidates)
            foreach (long second in candidates.Where(id => id != first))
                foreach (long third in candidates.Where(id => id != first && id != second))
                {
                    var ids = ImmutableArray.Create(first, second, third);
                    if (!ids.Contains(colorlessId)) continue;
                    var result = PredictorOperation.Preview(unselected, new ChooseObtainDeckCards(ids));
                    var change = result.Effects.SelectMany(effect => effect.Transformations).Single(change => change.OriginalId == colorlessId);
                    mixedExpected[ids.IndexOf(colorlessId)].Add(result.State.Card(change.ReplacementId) with { Id = 0 });
                }
        Check(mixedPreview.Faults.IsEmpty && mixedPreview.Branches.Single().PathsExamined == 3 &&
            mixedPreview.Branches.Single().Sections.SelectMany(section => section.CardGroups).Select((group, slot) =>
                group.Cards.Select(card => card with { Id = 0 }).ToHashSet().SetEquals(mixedExpected[slot])).All(value => value),
            "three representative Astrolabe inputs differ from independent exhaustive mixed-pool target tuples");
        long listenerId = unselected.State.NextInstanceId;
        var withListener = unselected with { State = unselected.State with
            { Relics = unselected.State.Relics.Add(new(listenerId, Relic("DARKSTONE_PERIAPT"), 0)), NextInstanceId = listenerId + 1 } };
        var listenerPreview = CardTargetOperationPreview.Build(withListener, colorlessId);
        Check(listenerPreview.Faults.IsEmpty && listenerPreview.Branches.Single().PathsExamined == 3 * (candidates.Length - 1) * (candidates.Length - 2) &&
            listenerPreview.Branches.Single().Sections.SelectMany(section => section.CardGroups).Select((group, slot) =>
                group.Cards.Select(card => card with { Id = 0 }).ToHashSet().SetEquals(mixedExpected[slot])).All(value => value),
            "an active card-add listener incorrectly entered representative-filler projection or changed exact target results");
        var fixedChoice = choice with { Operation = PredictorObtainDeckOperation.Remove, Source = Relic("EMPTY_CAGE") };
        var fixedSnapshot = captured with { Scene = new PredictorOperationShopScene([entry], true,
            PendingRelic: new(entryId, 500, false, fixedChoice)) };
        Check(CardTargetOperationPreview.Build(fixedSnapshot, candidates[0]).Branches.IsEmpty,
            "fixed remove selector manufactured random target forecasts");

        var leaf = Snapshot(baseline, "NEW_LEAF");
        var leafStarted = PredictorOperation.Preview(leaf, new ChooseAncientOffer(OfferId(leaf)));
        var leafId = leafStarted.State.Relics.Single(r => r.Key == Relic("NEW_LEAF")).Id;
        var leafPicker = leaf with { State = leafStarted.State, Scene = new PredictorOperationObtainScene(leaf.Scene,
            PredictorObtainReturn.Ancient, OfferId(leaf), leafId, Relic("NEW_LEAF"), PredictorOperationObtainChildKind.NewLeaf)
            { NewLeafCandidates = leafStarted.Waiting.Selection.CandidateCardIds } };
        var leafPreview = CardTargetOperationPreview.Build(leafPicker, leafStarted.Waiting.Selection.CandidateCardIds[0]);
        Check(leafPreview.Faults.IsEmpty && leafPreview.Branches.Single().PathsExamined == 1 &&
            leafPreview.Branches.Single().Sections.SelectMany(s => s.CardGroups).Single() is { Possible: false, Highlighted: true },
            "hovered New Leaf did not use its actual suspended picker");

        var stream = original.State.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var ev = new PredictorOperationEventScene(new("EVENT", "MORPHIC_GROVE"), stream, [], new(60, 80))
        { Branch = "GROUP", AwaitingCards = true, CardsRequired = 2, CardCandidates = deck };
        var eventSnapshot = original with { State = original.State with { Deck = deck, GoldBudget = 37 }, Scene = ev };
        var eventPreview = CardTargetOperationPreview.Build(eventSnapshot, deck[1]);
        var eventGroups = eventPreview.Branches.Single().Sections.SelectMany(s => s.CardGroups).ToArray();
        var eventExpected = new[] { new HashSet<PredictorCard>(), new HashSet<PredictorCard>() };
        foreach (long other in deck.Where(id => id != deck[1]))
            foreach (var targets in new[] { ImmutableArray.Create(deck[1], other), ImmutableArray.Create(other, deck[1]) })
            {
                var exact = PredictorOperation.Preview(eventSnapshot, new SelectEventCards(targets));
                var change = exact.Effects.SelectMany(effect => effect.Transformations).Single(change => change.OriginalId == deck[1]);
                eventExpected[targets.IndexOf(deck[1])].Add(exact.State.Card(change.ReplacementId) with { Id = 0 });
            }
        Check(eventPreview.Faults.IsEmpty && eventPreview.Branches is [{ FullyEnumerated: true }] && eventGroups.Length == 2 &&
            eventGroups.Select((group, slot) => group.Highlighted == (slot == 0) &&
                group.Cards.Select(card => card with { Id = 0 }).ToHashSet().SetEquals(eventExpected[slot])).All(value => value) &&
            eventSnapshot.State.GoldBudget == 37,
            "event target hover displayed another original instead of the hovered card at each transform ordinal");
        var eventSelected = eventSnapshot with { SelectedCardIds = [deck[1]] };
        var fullTargets = ImmutableArray.Create(deck[1], deck[0]);
        var eventFull = CardTargetOperationPreview.Build(eventSelected, deck[0]);
        var eventHighlighted = eventFull.Branches.Single().Sections.SelectMany(section => section.CardGroups).Single(group => group.Highlighted);
        var fullEvent = TransformSelectionForecast.Build(eventSelected, fullTargets);
        Check(eventFull.Faults.IsEmpty && eventHighlighted.Label == "2" && eventHighlighted.Cards is [var eventCard] &&
            (eventCard with { Id = 0 }) == (fullEvent.Single(item => item.OriginalId == deck[0]).Card with { Id = 0 }) &&
            eventSelected.SelectedCardIds.SequenceEqual([deck[1]]),
            "event picker lost the hovered original's current selected ordinal");
        VerifyConfirmation(eventSelected, fullTargets);
        VerifyConfirmation(eventSelected, fullTargets.Reverse().ToImmutableArray());
    }

    private static void VerifyConfirmation(PredictorOperationSnapshot snapshot, ImmutableArray<long> ordered)
    {
        string before = JsonSerializer.Serialize(new { snapshot.State, snapshot.Context, snapshot.SelectedCardIds });
        var forecast = TransformSelectionForecast.Build(snapshot, ordered);
        var exact = PredictorOperation.Preview(snapshot, TransformSelectionForecast.Input(snapshot, ordered));
        var changes = exact.Effects.SelectMany(effect => effect.Transformations).ToArray();
        Check(forecast.Select(item => item.OriginalId).SequenceEqual(ordered) &&
            forecast.All(item => item.Card == exact.State.Card(changes.Single(change => change.OriginalId == item.OriginalId).ReplacementId)) &&
            forecast.SequenceEqual(TransformSelectionForecast.Build(snapshot, ordered)) &&
            JsonSerializer.Serialize(new { snapshot.State, snapshot.Context, snapshot.SelectedCardIds }) == before,
            "confirmation projection changed state/RNG, lost selected order, or displayed a prototype instead of its final state");
    }

    private static void ExistingRewardContentIsNotForecast(PredictorRun baseline)
    {
        var snapshot = Snapshot(baseline, "WHETSTONE");
        long id = snapshot.State.NextInstanceId;
        var publicCard = snapshot.State.Deck.Select(snapshot.State.Card).First(c => PredictorObtainSources.CanEnchant(snapshot.Context, c))
            with { Id = id + 3 };
        var rewards = ImmutableArray.Create(
            new PredictorReward(id, PredictorRewardKind.Relic, PredictorRewardOrigin.Combat, Relic("WHETSTONE"), [], ContentInstanceId: id + 1),
            new PredictorReward(id + 2, PredictorRewardKind.Card, PredictorRewardOrigin.Combat, default, [publicCard]),
            new PredictorReward(id + 4, PredictorRewardKind.Potion, PredictorRewardOrigin.Combat, new("POTION", "BLOOD_POTION"), [], ContentInstanceId: id + 5),
            new PredictorReward(id + 6, PredictorRewardKind.Relic, PredictorRewardOrigin.Combat, Relic("OLD_COIN"), [], ContentInstanceId: id + 7));
        snapshot = snapshot with { State = snapshot.State with { NextInstanceId = id + 8 },
            Scene = new PredictorOperationRewardsScene(rewards) };
        var preview = RelicOperationPreview.Build(snapshot, [new TakeReward(id)], Relic("WHETSTONE"));
        Check(preview.Faults.IsEmpty && preview.Branches.Single().Sections.SelectMany(s => s.CardGroups).Count() == 0 &&
            Rows(preview).Any(r => r.Action == OperationPreviewAction.UpgradeCard) &&
            Rows(preview).All(r => r.Action == OperationPreviewAction.UpgradeCard),
            "a picked relic forecast redisplayed unrelated public card/potion/relic sibling rewards");

        // The same reward ID is not enough to suppress a real candidate change
        // from an obtain subscriber such as Silken Tress.
        var silkenRewards = rewards.SetItem(0, rewards[0] with { Key = Relic("SILKEN_TRESS") });
        var silken = snapshot with { Scene = new PredictorOperationRewardsScene(silkenRewards) };
        var changed = RelicOperationPreview.Build(silken, [new TakeReward(id)], Relic("SILKEN_TRESS"));
        var changedGroups = changed.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups).ToArray();
        Check(changed.Faults.IsEmpty && changedGroups.Length == 1 && changedGroups[0].Id == id + 2 &&
            changedGroups[0].Cards.Any(c => c.Enchantment?.Entry == "GLAM") &&
            Rows(changed).All(r => r.Action != OperationPreviewAction.GeneratePotion && !r.Models.Contains(Relic("OLD_COIN"))),
            "same-ID modified card rewards were hidden or unrelated sibling identities were exposed");

        foreach (string source in new[] { "HEFTY_TABLET", "SCROLL_BOXES" })
        {
            var offered = Snapshot(baseline, source);
            var waiting = PredictorOperation.Preview(offered, new ChooseAncientOffer(OfferId(offered)));
            Check(!waiting.Waiting.Cards.IsEmpty || !waiting.Waiting.Bundles.IsEmpty,
                "existing selection fixture did not prepare public candidates");
            Check(RelicOperationPreview.ObserveResult(offered.Context, waiting.State, waiting, waiting, Relic(source)).IsEmpty,
                "an unchanged suspended picker was redisplayed as a future random outcome");
        }
    }

    private static void FailedContinuationKeepsIndependentResults(PredictorRun baseline)
    {
        var snapshot = Snapshot(baseline, "NEOWS_BONES");
        snapshot = snapshot with { Context = snapshot.Context with { OpeningEligibleRelics = [Relic("ARCANE_SCROLL"), Relic("WHETSTONE")],
            Catalog = snapshot.Context.Catalog! with { CharacterCards = [] } } };
        var preview = RelicOperationPreview.Build(snapshot, OfferId(snapshot));
        Check(preview.ContainedRelics.Length == 2 && preview.Faults.Length == 2 && preview.Faults.All(f =>
            f.Stage == "Continue:Rewards" && f.InputPath[0] is ChooseAncientOffer && f.Exception.StackTrace != null) &&
            preview.Branches.All(b => b.Faulted && !b.FullyEnumerated && b.PendingReasons.IsEmpty) &&
            Rows(preview).Any(r => r.Action == OperationPreviewAction.UpgradeCard),
            "a failed child lost its original exception or hid the independent Whetstone outcome");
        // Both orders fail before any visible result. Empty sections are not
        // evidence that the uncomputed orders are semantically equivalent.
        var bothFail = RelicOperationPreview.Build(snapshot with { Context = snapshot.Context with
            { OpeningEligibleRelics = [Relic("ARCANE_SCROLL"), Relic("HEFTY_TABLET")] } }, OfferId(snapshot));
        Check(bothFail.Branches.Length == 2 && bothFail.Faults.Length == 2 &&
            bothFail.Branches.All(b => b.Faulted && b.Sections.IsEmpty && !b.FullyEnumerated),
            "identical missing results incorrectly collapsed two failed Bones orders");
    }

    private static Rng Native(PredictorStreamState state) => new(new SerializableRng
    { state0 = state.S0, state1 = state.S1, state2 = state.S2, state3 = state.S3, counter = state.Calls });
    private readonly record struct SortCard(PredictorCard Card) : IComparable<SortCard>
    {
        public int CompareTo(SortCard other)
        {
            int key = new ModelId(Card.Key.Category, Card.Key.Entry).CompareTo(new ModelId(other.Card.Key.Category, other.Card.Key.Entry));
            return key != 0 ? key : Card.UpgradeLevel.CompareTo(other.Card.UpgradeLevel);
        }
    }
}
