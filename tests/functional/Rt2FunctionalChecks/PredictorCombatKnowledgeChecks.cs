using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Prediction.Maps;

internal static class PredictorCombatKnowledgeChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    internal static void Run(PredictorRun captured)
    {
        MissingNiche(captured);
        PredictorCardRewardNicheKnowledgeChecks.Run(captured);
        DefaultGoldRecovery(captured);
        UnknownGoldAmount(captured);
        UnknownUnrecoveredGoldLoss(captured);
        UnknownTheft(captured);
        HealthAndBudget(captured);
        Console.WriteLine("RUNTIME_COMBAT_KNOWLEDGE_PASS missing Niche preserves independent gold/potion/cards; Wing Charm masks only uncertain enchantments; Heist draw retained; theft local; no state/history/request mutation");
    }

    private static PredictorRun Combat(PredictorRun captured, string encounter, params string[] relics)
        => Combat(captured, encounter, relics, null);

    private static PredictorRun Combat(PredictorRun captured, string encounter, string[] relics, string? card)
    {
        var context = captured.Context with { NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns), SealOfGoldFixedDeduction = null };
        var map = captured.RouteMap(1);
        var path = new MapRouteSet(map).At(System.Numerics.BigInteger.Zero).ToImmutableArray();
        var state = captured.Working with
        {
            OpeningCompleted = true, Relics = [],
            Position = new(1, 1, "knowledge:entry"), Maps = [new(1, map, [path[0]])],
            Visits = [new(1, path[0], MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
            Acts = captured.Working.Acts.SetItem(0, captured.Working.Acts[0] with
            { NormalEncounters = [new("ENCOUNTER", encounter)], NormalCursor = 0 })
        };
        foreach (string relic in relics) state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", relic));
        if (card != null) state = PredictorObtainSources.AddCard(context, state, new ModelKey("CARD", card));
        var run = new PredictorRun(context, state);
        run.SetRoute(1, path); run.AdvanceRoute();
        Check(run.Phase == PredictorPhase.CombatFacts, "Knowledge fixture must use the actual current combat boundary");
        return run;
    }

    private static PredictorCombatPreview PurePreview(PredictorRun run, PredictorCombatNodeOverrides? node = null)
    {
        string before = JsonSerializer.Serialize(run.Working);
        string checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var request = run.Request;
        var rewards = run.Rewards;
        var result = run.PreviewCurrentCombat(node)!;
        Check(result != null && JsonSerializer.Serialize(run.Working) == before &&
            JsonSerializer.Serialize(run.Checkpoints) == checkpoints && run.Request == request && run.Rewards == rewards &&
            run.Phase == PredictorPhase.CombatFacts, "Combat preview must preserve real detached state, history, rewards and request");
        Check(result!.Rewards.Where(r => r.Value != null).All(r => r.Value!.Id == 0 &&
            r.Value.ContentInstanceId == null && r.Value.Cards.All(c => c.Id == 0)), "Preview rewards must never carry actionable allocated IDs");
        return result;
    }

    private static void Submit(PredictorRun run, PredictorCombatNodeOverrides facts)
    {
        var input = run.ResolveCombatDefaults(facts)!.Input;
        Check(input != null && run.Submit(run.Request!, input!) == PredictorInputResult.Accepted,
            "Concrete witness must execute through production combat settlement");
    }

    private static void SameIndependentRewards(PredictorCombatPreview preview, PredictorRun witness)
    {
        foreach (var known in preview.Rewards.Where(r => r.IsKnown))
        {
            var reward = known.Value!;
            Check(witness.Rewards.Any(r => r.Kind == reward.Kind && r.WasGoldStolenBack == reward.WasGoldStolenBack &&
                r.GoldAmountUnspecified == reward.GoldAmountUnspecified && r.GoldNetRestored == reward.GoldNetRestored &&
                r.GoldAmount == reward.GoldAmount && r.Key == reward.Key &&
                r.Cards.Select(c => c with { Id = 0 }).SequenceEqual(reward.Cards)),
                "Known reward must match a concrete source witness: " + reward.Kind);
        }
    }

    private static void MissingNiche(PredictorRun captured)
    {
        var run = Combat(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE");
        var partial = PurePreview(run);
        Check(partial.MissingFacts.EnemyCreations.SequenceEqual([PredictorEnemyCreationSource.FabricatorBots]) &&
            partial.PendingCauses.Single(c => c.Code == "EnemyCreationCount").EnemySource == PredictorEnemyCreationSource.FabricatorBots,
            "Fabricator has no approved default; preserve its actual missing count source");
        Check(partial.Rewards.Any(r => r.Kind == PredictorRewardKind.Gold && r.IsKnown) &&
            partial.Rewards.Any(r => r.Kind == PredictorRewardKind.Potion && r.IsKnown) &&
            partial.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).All(r => r.IsKnown),
            "Missing Niche must preserve independent normal gold, potion and unhooked card rewards");
        Check(partial.Streams.Single(s => s.Stream == PredictorStream.Niche).Value == null &&
            partial.Streams.Single(s => s.Stream == PredictorStream.Niche).KnownBase == run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche) &&
            partial.Streams.Single(s => s.Stream == PredictorStream.Rewards).Value != null,
            "Only the actual Niche cursor has an unknown offset");
        foreach (int bots in new[] { 0, 4 })
        {
            var witness = Combat(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE");
            Submit(witness, new() { EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, bots)] });
            SameIndependentRewards(partial, witness);
            Check(partial.Streams.Single(s => s.Stream == PredictorStream.Rewards).Value == witness.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards),
                "Independent Rewards cursor must equal concrete witnesses with different Fabricator counts");
        }
        var hooked = PurePreview(Combat(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE", "WING_CHARM"));
        Check(hooked.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).Any(r => !r.IsKnown && r.Value == null &&
                r.PendingCauses.Any(c => c.Code == "EnemyCreationCount") && r.CardOptions.Length >= 2 &&
                r.CardOptions.All(card => card.Value.Id == 0 && card.WingCharmEligible == true &&
                    card.Value.Enchantment == null && card.EnchantmentPendingCauses.Any(c => c.Code == "EnemyCreationCount"))) &&
            hooked.Rewards.Where(r => r.Kind is PredictorRewardKind.Gold or PredictorRewardKind.Potion).All(r => r.IsKnown),
            "Wing Charm's Niche enchantment must be unknown without erasing known card identities or independent rewards");
        var tress = PurePreview(Combat(captured, "FABRICATOR_NORMAL", "WHITE_BEAST_STATUE", "SILKEN_TRESS", "WING_CHARM"));
        Check(tress.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).All(r => r.IsKnown &&
                r.CardOptions.All(card => card.WingCharmEligible == false && card.IsKnown)) &&
            tress.Streams.Single(stream => stream.Stream == PredictorStream.Niche).Value == null,
            "Silken Tress before Wing Charm proves zero eligible targets while the original unknown Niche cursor remains pending");
    }

    private static void UnknownGoldAmount(PredictorRun captured)
    {
        var blocked = Combat(captured, "GREMLIN_MERC_NORMAL", "WHITE_BEAST_STATUE", "BOWLER_HAT");
        var partial = PurePreview(blocked);
        Check(partial.Gold.Value == null && partial.PendingCauses.Any(c => c.Code == "RecoveredGoldAmount") &&
            partial.Gold.KnownBase == blocked.Working.GoldBudget && blocked.DefaultCombatInput() == null &&
            partial.Rewards.Count(r => r.Kind == PredictorRewardKind.Gold && r.Value == null) == 1 &&
            partial.Rewards.Any(r => r.Kind == PredictorRewardKind.Gold && r.IsKnown) &&
            partial.Rewards.Any(r => r.Kind == PredictorRewardKind.Potion && r.IsKnown),
            "Unknown recovered amount must remain unknown while ordinary rewards remain visible");
        foreach (int amount in new[] { 0, 37 })
        {
            var witness = Combat(captured, "GREMLIN_MERC_NORMAL", "WHITE_BEAST_STATUE", "BOWLER_HAT");
            Submit(witness, new()
            {
                EnemyCreations = [new(PredictorEnemyCreationSource.SurpriseGremlins, 0)],
                GremlinGold = new(false, true, amount)
            });
            Check(partial.Streams.Single(s => s.Stream == PredictorStream.Rewards).Value == witness.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards),
                "Unknown fixed-range recovered amount preserves the known-existing Rewards draw, including zero");
            SameIndependentRewards(partial, witness);
        }
        var absent = Combat(captured, "GREMLIN_MERC_NORMAL", "WHITE_BEAST_STATUE", "BOWLER_HAT");
        Submit(absent, new() { EnemyCreations = [new(PredictorEnemyCreationSource.SurpriseGremlins, 0)],
            GremlinGold = new(false, true, null) { StolenGoldLoss = 0 } });
        var after = absent.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        after.NextInt(1);
        Check(PredictorStreamState.Capture(PredictorStream.Rewards, after) == partial.Streams.Single(s => s.Stream == PredictorStream.Rewards).Value,
            "Unknown amount must not accidentally model an absent Heist object");
        var numeric = Combat(captured, "GREMLIN_MERC_NORMAL", "WHITE_BEAST_STATUE", "BOWLER_HAT");
        int loss = Math.Min(20, numeric.Working.GoldBudget);
        var declared = new PredictorCombatNodeOverrides { GremlinGold = new(false, true, loss) { StolenGoldLoss = loss } };
        var exact = PurePreview(numeric, declared);
        Check(exact.Gold.Value == numeric.Working.GoldBudget - loss && exact.PendingCauses.IsEmpty,
            "An explicitly declared theft ledger uses its actual known loss without an unknown Gold placeholder");
        Submit(numeric, declared);
        SameIndependentRewards(exact, numeric);
    }

    private static void DefaultGoldRecovery(PredictorRun captured)
    {
        var run = Combat(captured, "GREMLIN_MERC_NORMAL", "WHITE_BEAST_STATUE");
        var partial = PurePreview(run);
        var returned = partial.Rewards.Single(r => r.Value?.WasGoldStolenBack == true);
        Check(returned.IsKnown && returned.Value!.GoldAmountUnspecified && returned.Value.GoldNetRestored &&
            returned.Value.Status == PredictorRewardStatus.Taken && !returned.Value.CanSkip &&
            partial.Gold.Value == run.Working.GoldBudget && partial.PendingCauses.IsEmpty && run.DefaultCombatInput() != null,
            "Ordinary default recovery is a known net-restored disposition with an unexpanded amount, never an unknown or zero-valued reward");
        int before = run.Working.GoldBudget;
        Submit(run, new());
        Check(run.Working.GoldBudget == before, "Default recovery must not add a fabricated amount to the balance");
        SameIndependentRewards(partial, run);

        var mixed = Combat(captured, "GREMLIN_MERC_NORMAL", ["WHITE_BEAST_STATUE"], "THE_HUNT");
        var unordered = PurePreview(mixed);
        Check(mixed.ResolveCombatDefaults()!.NeedsExtraRewardOrder && mixed.DefaultCombatInput() == null &&
            unordered.PendingCauses.Any(c => c.Code == "ExtraRewardOrder") &&
            unordered.Rewards.Single(r => r.Value?.GoldNetRestored == true).IsKnown &&
            unordered.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).Count(r => r.IsKnown) == 1 &&
            unordered.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).Count(r => !r.IsKnown) == 1,
            "Net-restored Heist still participates in mixed order; normal cards remain known while Hunt cards depend on that order");
        var ordered = PurePreview(mixed, new() { ExtraRewardOrder =
            [new(PredictorCombatExtraRewardSource.Heist, 0), new(PredictorCombatExtraRewardSource.Hunt, 0)] });
        Check(ordered.PendingCauses.IsEmpty && ordered.Rewards.All(r => r.IsKnown),
            "Declaring the net-restored Heist/Hunt order removes the local remainder");
    }

    private static void UnknownUnrecoveredGoldLoss(PredictorRun captured)
    {
        var run = Combat(captured, "GREMLIN_MERC_NORMAL", "WHITE_BEAST_STATUE");
        var declared = new PredictorCombatNodeOverrides { GremlinGold = new(true, true, null) };
        var partial = PurePreview(run, declared);
        Check(run.ResolveCombatDefaults(declared)!.Input == null && partial.MissingFacts.GremlinGold &&
            partial.Gold.Value == null && partial.Gold.KnownBase == run.Working.GoldBudget &&
            partial.PendingCauses.Any(c => c.Code == "StolenGoldLoss" && c.AffectedDomains == PredictorCombatDomain.Gold) &&
            !partial.Rewards.Any(r => r.Kind == PredictorRewardKind.Gold) &&
            partial.Rewards.Any(r => r.Kind == PredictorRewardKind.Potion && r.IsKnown) &&
            partial.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).All(r => r.IsKnown),
            "An escaped thief with unknown unrecovered loss must not invent normal gold or Heist rewards");
        var witness = Combat(captured, "GREMLIN_MERC_NORMAL", "WHITE_BEAST_STATUE");
        int loss = Math.Min(7, witness.Working.GoldBudget);
        Submit(witness, declared with { GremlinGold = declared.GremlinGold! with { StolenGoldLoss = loss } });
        SameIndependentRewards(partial, witness);
        Check(partial.Streams.Single(s => s.Stream == PredictorStream.Rewards).Value == witness.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards),
            "Unknown loss preserves the escaped/stolen reward branches and concrete Rewards draw count");
    }

    private static void UnknownTheft(PredictorRun captured)
    {
        var run = Combat(captured, "THIEVING_HOPPER_WEAK", "WHITE_BEAST_STATUE");
        var partial = PurePreview(run);
        Check(partial.Rewards.Any(r => r.Kind == PredictorRewardKind.SpecialCard && r.UnknownMultiplicity && r.Value == null) &&
            partial.Rewards.Where(r => r.Kind is PredictorRewardKind.Gold or PredictorRewardKind.Potion or PredictorRewardKind.Card).All(r => r.IsKnown) &&
            partial.PendingCauses.Single(c => c.Code == "StolenCardIdentity").AffectedDomains.HasFlag(PredictorCombatDomain.Deck),
            "Unknown theft identity is local to deck/returned rewards and does not erase independent offers");
        var witness = Combat(captured, "THIEVING_HOPPER_WEAK", "WHITE_BEAST_STATUE");
        Submit(witness, new() { StolenCards = new([new(witness.Working.Deck[0], true)]) });
        SameIndependentRewards(partial, witness);
    }

    private static void HealthAndBudget(PredictorRun captured)
    {
        var partial = PurePreview(Combat(captured, "FUZZY_WURM_CRAWLER_WEAK", "WHITE_BEAST_STATUE", "MEAT_ON_THE_BONE", "SEAL_OF_GOLD"));
        Check(partial.Rewards.All(r => r.IsKnown) && partial.Gold.Value == null &&
            partial.PendingCauses.Any(c => c.Code == "VictoryHealth" && c.AffectedDomains == PredictorCombatDomain.Health) &&
            partial.PendingCauses.Any(c => c.Code == "SealOfGoldDeduction" && c.AffectedDomains == PredictorCombatDomain.Gold),
            "Missing conditional HP and Seal amount must preserve rewards and keep their own unknown domains");
    }
}
