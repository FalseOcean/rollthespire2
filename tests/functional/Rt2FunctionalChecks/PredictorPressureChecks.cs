using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;
using RolltheSpire2.Infrastructure.Snapshots;

internal static class PredictorPressureChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Predictor pressure: " + message);
    }

    private static void Send(PredictorRun run, PredictorInput input)
    {
        Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "Input " + input);
    }

    private static PredictorRun Enter(PredictorContext context, PredictorState boundary)
    {
        var run = new PredictorRun(context, boundary);
        run.EnterCombatRewards(PredictorCombatKind.Elite, 1, 2,
            facts: new() { Potions = PredictorCombatPotionFacts.NoAdditional });
        return run;
    }

    internal static void Run(PredictorContext context,
        RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
    {
        CombatPhaseSeams(context, authority);
        PredictorContext? foundContext = null;
        PredictorState? foundBoundary = null;
        PredictorRun? cardFirst = null;
        long chosenCard = 0;
        long whetstoneReward = 0;
        int offeredUpgrade = 0;
        string? foundSeed = null;
        for (int ordinal = 0; ordinal < 512; ordinal++)
        {
            string seed = RolltheSpire2.Search.FamilyExecution.VisibleSeedCandidateCodec.FormatOrdinal(
                RolltheSpire2.Compatibility.Beta111Profile.Instance, (ulong)ordinal);
            var initialized = PredictorSeedCapture.CreateRun(context with { Seed = seed }, authority);
            var boundary = initialized.Working with { OpeningCompleted = true };
            foreach (string entry in new[] { "PRISMATIC_GEM", "DINGY_RUG", "WHITE_STAR", "BLACK_STAR", "WONGOS_MYSTERY_TICKET" })
                boundary = PredictorSettlementEffects.ObtainRelic(boundary, new ModelKey("RELIC", entry));
            boundary = boundary with { Relics = boundary.Relics.Select(r => r.Key.Entry == "WONGOS_MYSTERY_TICKET"
                ? r with { CombatsFinished = 4 } : r).ToImmutableArray() };
            var run = Enter(initialized.Context, boundary);
            var cards = run.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).ToArray();
            var relics = run.Rewards.Where(r => r.Kind == PredictorRewardKind.Relic).ToArray();
            if (cards.Length < 2 || relics.Length < 5) continue;
            var whetstone = relics.FirstOrDefault(r => r.Key.Entry == "WHETSTONE");
            if (whetstone == null) continue;
            var choice = cards.SelectMany(r => r.Cards.Select(c => (Reward: r, Card: c)))
                .FirstOrDefault(x => x.Card != null && x.Card.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack &&
                    x.Card.UpgradeLevel < x.Card.MaxUpgradeLevel && !PredictorCombatConsequences.IsTriggeredSource(x.Card));
            if (choice.Card == null) continue;
            Send(run, new EnterCardSelection(choice.Reward.Id));
            Send(run, new TakeReward(choice.Reward.Id, choice.Card.Id));
            Send(run, new TakeReward(whetstone.Id));
            if (run.Working.Card(choice.Card.Id).UpgradeLevel <= choice.Card.UpgradeLevel) continue;
            foundContext = initialized.Context;
            foundBoundary = boundary;
            cardFirst = run;
            chosenCard = choice.Card.Id;
            whetstoneReward = whetstone.Id;
            offeredUpgrade = choice.Card.UpgradeLevel;
            foundSeed = seed;
            break;
        }
        Check(cardFirst != null && foundContext != null && foundBoundary != null,
            "bounded 512-seed search found no real elite reward set with Whetstone and an affected Attack choice");
        var relicFirst = Enter(foundContext!, foundBoundary!);
        Check(relicFirst.Rewards.Count(r => r.Kind == PredictorRewardKind.Card) == 2 &&
            relicFirst.Rewards.Count(r => r.Kind == PredictorRewardKind.Relic) >= 5 &&
            relicFirst.Rewards.Single(r => r.Id == whetstoneReward).Key.Entry == "WHETSTONE",
            "same captured seed did not regenerate pressure reward identities");
        var relicFirstCard = relicFirst.Rewards.Where(r => r.Kind == PredictorRewardKind.Card)
            .Single(r => r.Cards.Any(c => c.Id == chosenCard));
        Send(relicFirst, new TakeReward(whetstoneReward));
        Send(relicFirst, new EnterCardSelection(relicFirstCard.Id));
        Send(relicFirst, new TakeReward(relicFirstCard.Id, chosenCard));
        Check(cardFirst!.Working.Card(chosenCard).UpgradeLevel > offeredUpgrade &&
            relicFirst.Working.Card(chosenCard).UpgradeLevel == offeredUpgrade &&
            cardFirst.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls >
                relicFirst.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls,
            "card/Whetstone take order did not change master upgrade and Niche consumption");
        foreach (var run in new[] { cardFirst!, relicFirst })
        {
            Send(run, new LeaveRewards());
            Check(run.Working.Relics.Single(r => r.Key.Entry == "WONGOS_MYSTERY_TICKET") is
                { CombatsFinished: 5, GaveRelic: true } && run.Phase == PredictorPhase.Complete,
                "Wongo fifth-combat triple reward did not finish at node boundary");
            var reloaded = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run),
                foundContext!.CatalogFingerprint, foundContext.UnlockFingerprint);
            Check(reloaded.Working.Card(chosenCard).UpgradeLevel == run.Working.Card(chosenCard).UpgradeLevel,
                "pressure order master card failed save/reload");
            var firstBoundary = reloaded.Working;
            reloaded.EnterCombatRewards(PredictorCombatKind.Monster, 1, 3,
                facts: new() { Potions = PredictorCombatPotionFacts.NoAdditional });
            var nextRewards = reloaded.Rewards.Select(r => (r.Kind, r.Key,
                Cards: string.Join(',', r.Cards.Select(c => c.Key.Entry)))).ToArray();
            Send(reloaded, new LeaveRewards());
            reloaded.Rewind(1);
            Check(reloaded.Working.Card(chosenCard).UpgradeLevel == firstBoundary.Card(chosenCard).UpgradeLevel &&
                reloaded.Working.Streams.Single(s => s.Stream == PredictorStream.Niche) ==
                    firstBoundary.Streams.Single(s => s.Stream == PredictorStream.Niche),
                "rewind after next combat did not restore pressure choice boundary");
            reloaded.EnterCombatRewards(PredictorCombatKind.Monster, 1, 3,
                facts: new() { Potions = PredictorCombatPotionFacts.NoAdditional });
            Check(reloaded.Rewards.Select(r => (r.Kind, r.Key,
                Cards: string.Join(',', r.Cards.Select(c => c.Key.Entry)))).SequenceEqual(nextRewards),
                "next-node generation diverged after save/rewind");
        }
        Console.WriteLine($"RUNTIME_PRESSURE_REWARD {foundSeed} captured seed + controlled inventory: Prismatic/Dingy/WhiteStar/BlackStar/Wongo fifth Elite -> 2 CardRewards + >=5 relics incl Whetstone -> opposite take orders -> next combat/reload/rewind PASS");
    }

    private static void CombatPhaseSeams(PredictorContext context,
        RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
    {
        string seed = RolltheSpire2.Search.FamilyExecution.VisibleSeedCandidateCodec.FormatOrdinal(
            RolltheSpire2.Compatibility.Beta111Profile.Instance, 1UL);
        var captured = PredictorSeedCapture.CreateRun(context with { Seed = seed }, authority);
        var entry = captured.Working with { OpeningCompleted = true };
        foreach (string victim in new[] { "WAR_HAMMER", "MEAT_ON_THE_BONE" })
        {
            var state = PredictorSettlementEffects.ObtainRelic(entry, new("RELIC", "TOY_BOX"));
            state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", victim));
            state = state with { Relics = state.Relics.Select(r => r.Key.Entry switch
            {
                "TOY_BOX" => r with { ToyBoxCombatsSeen = 2 },
                var key when key == victim => r with { IsWax = true },
                _ => r
            }).ToImmutableArray() };
            var beforeDeck = state.Deck.Select(state.Card).ToArray();
            var noVictoryRelic = state with
            {
                Relics = state.Relics.Where(r => r.Key.Entry != victim).ToImmutableArray()
            };
            var baselineRun = new PredictorRun(captured.Context, noVictoryRelic);
            baselineRun.EnterCombatRewards(PredictorCombatKind.Elite, 1, 2,
                facts: new() { Potions = PredictorCombatPotionFacts.NoAdditional });
            var run = new PredictorRun(captured.Context, state);
            run.EnterCombatRewards(PredictorCombatKind.Elite, 1, 2,
                facts: new() { Potions = PredictorCombatPotionFacts.NoAdditional });
            Check(run.Working.Relics.Single(r => r.Key.Entry == victim) is { IsWax: true, Melted: true } &&
                  run.Working.Relics.Single(r => r.Key.Entry == "TOY_BOX").ToyBoxCombatsSeen == 3,
                "ToyBox third-combat melt must precede victory listeners for " + victim);
            Check(run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche) ==
                    baselineRun.Working.Streams.Single(s => s.Stream == PredictorStream.Niche) &&
                  beforeDeck.All(c => run.Working.Card(c.Id).UpgradeLevel == c.UpgradeLevel),
                "melted wax " + victim + " still ran its Victory callback");
            Check(!run.Working.CurrentHealthEffects.Any(h => h.SourceKey.Entry == "MEAT_ON_THE_BONE"),
                "melted wax Meat ran VictoryEarly heal");
        }

        // Pael's AfterCombatEnd return adds an upgraded master card before
        // WarHammer's Victory candidate scan. The returned level-one card is
        // already max-upgraded, so WarHammer must not select it or consume a
        // different Niche sequence because of it.
        var paelState = PredictorSettlementEffects.ObtainRelic(entry, new("RELIC", "PAELS_TOOTH"));
        paelState = PredictorSettlementEffects.ObtainRelic(paelState, new("RELIC", "WAR_HAMMER"));
        var stored = paelState.Deck.Select(paelState.Card).First(c => c.MaxUpgradeLevel == 1 && c.UpgradeLevel == 0);
        paelState = paelState with
        {
            Deck = paelState.Deck.Remove(stored.Id),
            Relics = paelState.Relics.Select(r => r.Key.Entry == "PAELS_TOOTH"
                ? r with { StoredCards = [stored] } : r).ToImmutableArray()
        };
        var baseline = paelState with { Relics = paelState.Relics.Where(r => r.Key.Entry != "PAELS_TOOTH").ToImmutableArray() };
        var baselineRunAfterPael = new PredictorRun(captured.Context, baseline);
        baselineRunAfterPael.EnterCombatRewards(PredictorCombatKind.Elite, 1, 2,
            facts: new() { Potions = PredictorCombatPotionFacts.NoAdditional });
        var paelRun = new PredictorRun(captured.Context, paelState);
        paelRun.EnterCombatRewards(PredictorCombatKind.Elite, 1, 2,
            facts: new() { Potions = PredictorCombatPotionFacts.NoAdditional });
        var returned = paelRun.Working.Deck.Select(paelRun.Working.Card)
            .Single(c => c.Key == stored.Key && c.UpgradeLevel == 1 &&
                !paelState.Cards.Any(existing => existing.Id == c.Id));
        Check(paelRun.Working.Relics.Single(r => r.Key.Entry == "PAELS_TOOTH").StoredCards.IsEmpty,
            "Pael's card was not upgraded and returned before victory");
        Check(paelRun.Working.Streams.Single(s => s.Stream == PredictorStream.Niche) ==
              baselineRunAfterPael.Working.Streams.Single(s => s.Stream == PredictorStream.Niche),
            "Pael's already-upgraded return changed WarHammer candidate shuffle");
        Console.WriteLine("RUNTIME_PRESSURE_PHASE ToyBox wax melt before WarHammer/Meat victory; Pael return before WarHammer Niche selection PASS");
    }
}
