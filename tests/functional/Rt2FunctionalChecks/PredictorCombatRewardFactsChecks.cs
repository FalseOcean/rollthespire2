using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;

internal static class PredictorCombatRewardFactsChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Select(PredictorRun run, PredictorInput input) =>
        Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "Reward fact input: " + input);

    internal static void Run(PredictorRun captured)
    {
        PlanBindingReasons();
        SealBudget(captured);
        NetRestoredGold(captured);
        CombatDefaults(captured);
        CombatWorkflow(captured);
        var context = captured.Context;
        var boundary = captured.Working with { OpeningCompleted = true, Acts = captured.Working.Acts.SetItem(0,
            captured.Working.Acts[0] with { NormalEncounters = [new("ENCOUNTER", "GREMLIN_MERC_NORMAL")], NormalCursor = 0 }) };
        foreach (var facts in new[] { new PredictorGremlinGoldFacts(false, false, 0), new(false, true, 37),
            new(true, false, null), new(true, true, null) })
        {
            var run = new PredictorRun(context, boundary);
            run.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: new() { GremlinGold = facts });
            Check(run.Working.GoldBudget == boundary.GoldBudget, "Gremlin facts must not reconstruct combat budget");
            var returned = run.Rewards.SingleOrDefault(r => r.WasGoldStolenBack);
            Check((returned != null) == facts.RecoveredGold.HasValue &&
                (returned == null || returned.GoldAmount == facts.RecoveredGold), "Heist null differs from fixed zero reward");
            Check(run.Rewards.Count(r => r.Kind == PredictorRewardKind.Gold && !r.WasGoldStolenBack) ==
                (facts.FatGremlinEscaped && facts.GoldWasStolen ? 0 : 1), "Gremlin gold proportion source condition");
            if (returned != null)
            {
                Select(run, new CorrectGoldBalance(10));
                Select(run, new TakeReward(returned.Id));
                Check(run.Working.GoldBudget == 10 + facts.RecoveredGold, "Recovered reward takes after current-balance correction");
            }
            Select(run, new LeaveRewards());
            var restored = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), context.CatalogFingerprint, context.UnlockFingerprint);
            Check(restored.Checkpoints[^1].CompletedNode!.Inputs.Any(i => i.CombatGremlinGold == facts), "Heist facts/history reload");
            restored.Rewind(0);
            Check(restored.Working.GoldBudget == boundary.GoldBudget, "Heist rewind restores entry budget");
        }

        var none = new PredictorRun(context, boundary);
        none.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2,
            facts: new() { GremlinGold = new(false, false, null) });
        var zero = new PredictorRun(context, boundary);
        zero.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2,
            facts: new() { GremlinGold = new(false, false, 0) });
        var expected = none.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        expected.NextInt(1);
        Check(PredictorStreamState.Capture(PredictorStream.Rewards, expected) ==
            zero.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards), "Fixed zero Heist still populates and consumes Rewards draw");

        var plans = ImmutableArray.Create(new PredictorRewardPlan(PredictorRewardKind.Card),
            new(PredictorRewardKind.Gold, 30, 30, PrefilledGold: true), new(PredictorRewardKind.RemoveCard));
        var order = ImmutableArray.Create(new PredictorCombatExtraRewardRef(PredictorCombatExtraRewardSource.Heist, 0),
            new(PredictorCombatExtraRewardSource.Hunt, 0));
        var ordered = PredictorCombatRewardFacts.Order(plans, [], new(false, true, 37), order);
        Check(ordered[0].WasGoldStolenBack && ordered[1].Kind == PredictorRewardKind.Card && ordered[2].MinGold == 30 &&
            ordered[3].Kind == PredictorRewardKind.RemoveCard, "In-combat extras precede AfterCombatEnd extras");
        try
        {
            PredictorCombatRewardFacts.Order(plans, [], new(false, true, 37), []);
            throw new InvalidOperationException("Mixed reward order silently guessed");
        }
        catch (ArgumentException) { }
        Console.WriteLine("RUNTIME_GREMLIN_FACTS escaped/stolen proportion; Heist absent/zero/positive generation, budget correction/take/save/rewind; mixed reward order PASS (declared encounter outcomes)");
    }

    private static void NetRestoredGold(PredictorRun captured)
    {
        var context = captured.Context with
        { Modifiers = captured.Context.Modifiers.Where(m => m.Key.Entry != "MIDAS").ToImmutableArray() };
        var state = captured.Working with
        {
            OpeningCompleted = true, GoldBudget = 100,
            Relics = captured.Working.Relics.Where(r => r.Key.Entry is not
                ("BOWLER_HAT" or "ECTOPLASM" or "DRAGON_FRUIT" or "SEAL_OF_GOLD")).ToImmutableArray(),
            Acts = captured.Working.Acts.SetItem(0, captured.Working.Acts[0] with
            { NormalEncounters = [new("ENCOUNTER", "GREMLIN_MERC_NORMAL")], NormalCursor = 0 })
        };
        var requirements = new PredictorCombatRequirements([PredictorEnemyCreationSource.SurpriseGremlins], false, true, false, false, false);
        var resolution = PredictorCombatDefaultResolver.Resolve(state, requirements, new(), context: context);
        Check(resolution.Input != null && resolution.Facts.GremlinGold is
            { GoldWasStolen: true, FatGremlinEscaped: false, RecoverAllStolenGold: true, RecoveredGold: null },
            "Default theft recovery is a declared closed loop, without an invented numeric amount");
        var noTheft = new PredictorRun(context, state);
        noTheft.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2,
            facts: resolution.Facts with { GremlinGold = new(false, false, null) });
        var balanced = new PredictorRun(context, state);
        balanced.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: resolution.Facts);
        var heist = balanced.Rewards.Single(r => r.WasGoldStolenBack);
        Check(balanced.Working.GoldBudget == state.GoldBudget && heist.GoldAmountUnspecified && heist.GoldNetRestored &&
            heist.Status == PredictorRewardStatus.Taken && !heist.CanSkip && heist.HasValidGoldDisposition,
            "Net restoration preserves balance and retains one already settled reward with no actionable fake amount");
        var expected = noTheft.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        expected.NextInt(1);
        Check(balanced.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards) ==
            PredictorStreamState.Capture(PredictorStream.Rewards, expected), "Unspecified Heist still populates at its original position with one Rewards draw");
        Check(balanced.Submit(balanced.Request!, new TakeReward(heist.Id)) == PredictorInputResult.Invalid &&
            balanced.Working.GoldBudget == state.GoldBudget, "Settled recovery cannot be taken a second time");
        Select(balanced, new LeaveRewards());
        var restored = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(balanced), context.CatalogFingerprint, context.UnlockFingerprint);
        Check(restored.Checkpoints[^1].CompletedNode!.Rewards.Single(r => r.WasGoldStolenBack) is
            { GoldAmountUnspecified: true, GoldNetRestored: true, Status: PredictorRewardStatus.Taken, CanSkip: false },
            "Closed-loop recovery remains settled and amount-unspecified through save/load and LeaveRewards");
        var loss = new PredictorRun(context, state);
        loss.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: resolution.Facts with
        { GremlinGold = new(true, true, null) { StolenGoldLoss = 30 } });
        Check(loss.Working.GoldBudget == 70 && !loss.Rewards.Any(r => r.Kind == PredictorRewardKind.Gold),
            "Escaped thief with explicit unrecovered loss subtracts that loss and suppresses encounter gold");
        var numericFacts = resolution.Facts with { GremlinGold = new(false, true, 30) { StolenGoldLoss = 30 } };
        var numeric = new PredictorRun(context, state);
        numeric.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: numericFacts);
        Check(numeric.Working.GoldBudget == 70, "New numeric recovery accounts for its stolen loss before reward collection");
        Select(numeric, new TakeReward(numeric.Rewards.Single(r => r.WasGoldStolenBack).Id));
        Check(numeric.Working.GoldBudget == 100, "Numeric recovery restores original balance without manual correction");
        Select(numeric, new LeaveRewards());
        var numericRestored = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(numeric), context.CatalogFingerprint, context.UnlockFingerprint);
        Check(numericRestored.Working.GoldBudget == 100 &&
            numericRestored.Checkpoints[^1].CompletedNode!.Inputs.Single(i => i.Action == "CombatBoundary").CombatGremlinGold?.StolenGoldLoss == 30,
            "Save/load retains the applied numeric loss instead of debiting it twice");
        numericRestored.Rewind(0);
        numericRestored.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: numericFacts);
        Check(numericRestored.Working.GoldBudget == 70, "Reapplying numeric facts debits the restored entry exactly once");
        var overdrawn = new PredictorRun(context, state);
        try
        {
            overdrawn.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2,
                facts: resolution.Facts with { GremlinGold = new(false, true, 101) { StolenGoldLoss = 101 } });
            throw new InvalidOperationException("Insufficient theft funding was silently clamped");
        }
        catch (PredictorCombatPlanBindingException e) when (e.Reason == PredictorCombatPlanBindingReason.StolenGoldLossExceedsBudget &&
            e.Message == "PredictorStolenGoldLossExceedsBudget") { }
        Check(overdrawn.Working == state, "An unreasonable loss is rejected atomically without a fabricated clamped balance");
        foreach (var blocker in new[] { "BOWLER_HAT", "ECTOPLASM", "DRAGON_FRUIT", "SEAL_OF_GOLD", "MIDAS" })
        {
            var blockedState = blocker == "MIDAS" ? state : PredictorSettlementEffects.ObtainRelic(state, new("RELIC", blocker));
            var blockedContext = blocker == "MIDAS" ? context with
            { Modifiers = context.Modifiers.Add(new(new("MODIFIER", "MIDAS"))) } : context with { SealOfGoldFixedDeduction = 17 };
            var blocked = PredictorCombatDefaultResolver.Resolve(blockedState, requirements, new(), context: blockedContext);
            Check(!PredictorCombatRewardFacts.CanBalanceRecoveredGold(blockedContext, blockedState) &&
                PredictorCombatRewardFacts.RecoveredGoldBalanceBlockers(blockedContext, blockedState).Contains(blocker) &&
                blocked.MissingFacts.GremlinGold && blocked.Input == null && blockedState.GoldBudget == 100,
                "Amount-sensitive effects keep recovery local instead of claiming net zero: " + blocker);
            var rejected = new PredictorRun(blockedContext, blockedState);
            try
            {
                rejected.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: blocked.Facts);
                throw new InvalidOperationException("Blocked net restoration silently executed: " + blocker);
            }
            catch (ArgumentException e) when (e.Message == "PredictorRecoveredGoldAmountRequired") { }
            Check(rejected.Working == blockedState, "Rejected net restoration preserves the boundary: " + blocker);
            var explicitRecovery = new PredictorRun(blockedContext, blockedState);
            explicitRecovery.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: numericFacts);
            var recovered = explicitRecovery.Rewards.Single(r => r.WasGoldStolenBack);
            Check(!recovered.GoldAmountUnspecified && !recovered.GoldNetRestored && recovered.Status == PredictorRewardStatus.Open &&
                explicitRecovery.Working.GoldBudget == (blocker == "SEAL_OF_GOLD" ? 53 : 70),
                "Numeric mode applies loss before ordinary recovery hooks: " + blocker);
            int healthBefore = explicitRecovery.Working.CurrentHealthEffects.Count(h => h.SourceKey.Entry == "DRAGON_FRUIT");
            Select(explicitRecovery, new TakeReward(recovered.Id));
            int expectedBudget = blocker switch
            { "BOWLER_HAT" => 107, "ECTOPLASM" => 70, "MIDAS" => 130, "SEAL_OF_GOLD" => 83, _ => 100 };
            Check(explicitRecovery.Working.GoldBudget == expectedBudget &&
                explicitRecovery.Working.CurrentHealthEffects.Count(h => h.SourceKey.Entry == "DRAGON_FRUIT") ==
                    healthBefore + (blocker == "DRAGON_FRUIT" ? 1 : 0),
                "Numeric recovery preserves amount modifiers, gain callbacks and Seal ordering: " + blocker);
        }
        var huntState = PredictorObtainSources.AddCard(context, state, new ModelKey("CARD", "THE_HUNT"));
        var mixed = PredictorCombatDefaultResolver.Resolve(huntState, requirements, new(), context: context);
        Check(!mixed.MissingFacts.GremlinGold && mixed.NeedsExtraRewardOrder && mixed.Input == null,
            "Known Hunt and Heist require their real relative order without turning the node into a fault");
        var ordered = PredictorCombatDefaultResolver.Resolve(huntState, requirements, new(), new()
        { ExtraRewardOrder = [new(PredictorCombatExtraRewardSource.Heist, 0), new(PredictorCombatExtraRewardSource.Hunt, 0)] }, context);
        Check(ordered.Input != null, "Explicit mixed-source order resolves the default recovery");
        Console.WriteLine("RUNTIME_GREMLIN_NET_RESTORED default closed loop, original draw, no double take, save, loss override, blockers and mixed order PASS");
    }

    private static void CombatDefaults(PredictorRun captured)
    {
        var state = PredictorObtainSources.AddCard(captured.Context, captured.Working, new ModelKey("CARD", "ROYALTIES"));
        long first = state.Deck[^1];
        state = PredictorObtainSources.AddCard(captured.Context, state, new ModelKey("CARD", "ROYALTIES"));
        long second = state.Deck[^1];
        state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", "LAVA_LAMP"));
        var lamp = state.Relics[^1];
        state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", "BONE_TEA"));
        state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", "MEAT_ON_THE_BONE"));
        var meat = state.Relics[^1];
        var defaults = new PredictorCombatDefaults
        {
            CardRules = [new(new("CARD", "ROYALTIES"), 2)],
            RelicRules = [new(lamp.Key, false)],
            EnemyRules = [new(PredictorEnemyCreationSource.HatchedEggs, 7)]
        };
        var requirements = new PredictorCombatRequirements(
            [PredictorEnemyCreationSource.FabricatorBots, PredictorEnemyCreationSource.HatchedEggs],
            true, true, true, true, false);
        string before = System.Text.Json.JsonSerializer.Serialize(state);
        var resolved = PredictorCombatDefaultResolver.Resolve(state, requirements, defaults, new()
        {
            CardTriggers = [new(first, 0)],
            RelicEffects = [new(lamp.Id, true)],
            EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, 3)]
        });
        Check(resolved.Facts.CardTriggers.Single(t => t.InstanceId == first).EffectiveTriggers == 0 &&
            resolved.Facts.CardTriggers.Single(t => t.InstanceId == second).EffectiveTriggers == 2,
            "Node instance count overrides model preference without overriding another copy");
        Check(resolved.Facts.RelicEffectEnabled(lamp) && !resolved.Facts.TookDamageThisCombat &&
            resolved.Facts.RelicEffectEnabled(meat) && resolved.Facts.FirstPlayerTurnOccurred == true,
            "Relic controls express enabled effects, invert Lava Lamp damage, and default Bone Tea first turn");
        Check(resolved.MissingFacts.EnemyCreations.IsEmpty &&
            resolved.MissingFacts.StolenCards && resolved.MissingFacts.GremlinGold &&
            !resolved.MissingFacts.FirstPlayerTurn && resolved.MissingFacts.VictoryHealth && resolved.Input == null,
            "Partial observations preserve unresolved local facts, including conditional victory HP");
        Check(resolved.Facts.StolenCards == null && resolved.Facts.GremlinGold is { RecoverAllStolenGold: true, RecoveredGold: null } && resolved.Facts.VictoryHealth == null &&
            resolved.Facts.EnemyCreations is [{ Source: PredictorEnemyCreationSource.FabricatorBots, Count: 3 },
                { Source: PredictorEnemyCreationSource.HatchedEggs, Count: 7 }],
            "Counts use node values then user defaults without fabricating stolen identities, recovered gold or HP");
        var inherited = PredictorCombatDefaultResolver.Resolve(state, requirements, defaults);
        Check(inherited.MissingFacts.EnemyCreations.SequenceEqual([PredictorEnemyCreationSource.FabricatorBots]) &&
            inherited.Facts.EnemyCreations is [{ Source: PredictorEnemyCreationSource.HatchedEggs, Count: 7 }],
            "Fabricator remains pending without a declared default; node values never persist");
        var explicitZero = PredictorCombatDefaultResolver.Resolve(state, requirements, defaults, new()
        {
            EnemyCreations = [new(PredictorEnemyCreationSource.FabricatorBots, 0), new(PredictorEnemyCreationSource.HatchedEggs, 0)]
        });
        Check(explicitZero.MissingFacts.EnemyCreations.IsEmpty && explicitZero.Facts.EnemyCreations.All(f => f.Count == 0),
            "Explicit zero overrides both missing Fabricator and saved positive hatch count");
        var allSources = new PredictorCombatRequirements(Enum.GetValues<PredictorEnemyCreationSource>().ToImmutableArray(),
            false, false, false, false, false);
        var builtIn = PredictorCombatDefaultResolver.Resolve(state, allSources, new());
        Check(builtIn.MissingFacts.EnemyCreations.SequenceEqual([PredictorEnemyCreationSource.FabricatorBots]) &&
            builtIn.Facts.EnemyCreations.SequenceEqual(new PredictorEnemyCreationFact[]
            {
                new(PredictorEnemyCreationSource.FogmogIllusions, 1), new(PredictorEnemyCreationSource.ObscuraIllusions, 1),
                new(PredictorEnemyCreationSource.GasBombs, 1), new(PredictorEnemyCreationSource.LaidEggs, 3),
                new(PredictorEnemyCreationSource.HatchedEggs, 3), new(PredictorEnemyCreationSource.BackupRats, 1),
                new(PredictorEnemyCreationSource.Wrigglers, 4), new(PredictorEnemyCreationSource.ReplacementAxebots, 2),
                new(PredictorEnemyCreationSource.SurpriseGremlins, 2)
            }), "Owner-approved enemy counts are prediction premises; Fabricator has no built-in value");
        var customFabricator = PredictorCombatDefaultResolver.Resolve(state, allSources, new()
        { EnemyRules = [new(PredictorEnemyCreationSource.FabricatorBots, 2)] });
        Check(customFabricator.MissingFacts.EnemyCreations.IsEmpty && customFabricator.Input != null &&
            customFabricator.Facts.EnemyCreations.Single(f => f.Source == PredictorEnemyCreationSource.FabricatorBots).Count == 2,
            "A saved source rule can resolve Fabricator without per-node input");
        Check(System.Text.Json.JsonSerializer.Serialize(state) == before && defaults.RelicRules[0].EffectEnabled == false,
            "Resolving and disabling effects must preserve inventory and saved preferences");
        var disabled = PredictorCombatDefaultResolver.Resolve(state, PredictorCombatRequirements.None, defaults);
        Check(disabled.Facts.TookDamageThisCombat && !disabled.Facts.RelicEffectEnabled(lamp) &&
            disabled.Facts.CardTriggers.Single(t => t.InstanceId == first).EffectiveTriggers == 2,
            "The next resolution uses saved defaults again; node overrides never persist");
        var enabled = PredictorCombatDefaultResolver.Resolve(state, PredictorCombatRequirements.None, new());
        Check(enabled.Facts.RelicEffects.All(r => r.EffectEnabled) &&
            enabled.Facts.CardTriggers.All(t => t.EffectiveTriggers == 1),
            "Initial editable defaults enable all effects with one effective card occurrence");
        try
        {
            PredictorCombatDefaultResolver.Resolve(state, requirements, defaults, new()
            { CardTriggers = [new(long.MaxValue, 1)] });
            throw new InvalidOperationException("Unknown node instance accepted");
        }
        catch (PredictorCombatPlanBindingException e) when (e.Reason == PredictorCombatPlanBindingReason.NodeOverridesInvalid) { }
        try
        {
            PredictorCombatDefaultResolver.Resolve(state, requirements, new()
            { RelicRules = [new(meat.Key, false)] });
            throw new InvalidOperationException("Fixed relic effect was made configurable");
        }
        catch (ArgumentException) { }
        try
        {
            PredictorCombatDefaultResolver.Resolve(state, requirements, defaults, new()
            { RelicEffects = [new(meat.Id, false)] });
            throw new InvalidOperationException("Fixed relic effect accepted as a node override");
        }
        catch (ArgumentException) { }
        try
        {
            PredictorCombatConsequences.Apply(captured.Context, state, PredictorCombatKind.Monster, new()
            { RelicEffects = [new(meat.Id, false)] });
            throw new InvalidOperationException("Fixed relic effect accepted in submitted combat facts");
        }
        catch (PredictorCombatPlanBindingException e) when (e.Reason == PredictorCombatPlanBindingReason.RelicFactsInvalid &&
            e.Message == "PredictorCombatRelicFactsInvalid") { }
        Console.WriteLine("RUNTIME_COMBAT_DEFAULTS model preferences, per-instance precedence, effect semantics, local missing facts, pure inventory PASS");
    }

    private static void PlanBindingReasons()
    {
        // Exercise the same filter used by ordinary and event route previews.
        // A message may gain context without changing the pending allowance.
        static bool Pending(Exception failure)
        {
            try { throw failure; }
            catch (ArgumentException ex) when (PredictorCombatPlanBindingException.IsPendingPlanBinding(ex)) { return true; }
        }
        var reasons = new (PredictorCombatPlanBindingReason Reason, string Message)[]
        {
            (PredictorCombatPlanBindingReason.NodeOverridesInvalid, "PredictorCombatNodeOverridesInvalid"),
            (PredictorCombatPlanBindingReason.FactsMustReplaceAllRelevantInstances, "PredictorCombatFactsMustReplaceAllRelevantInstances"),
            (PredictorCombatPlanBindingReason.TemporaryCardFactsInvalid, "PredictorTemporaryCombatCardFactsInvalid"),
            (PredictorCombatPlanBindingReason.CardSourceOrderInvalid, "PredictorCombatCardSourceOrderInvalid"),
            (PredictorCombatPlanBindingReason.StolenCardFactsInvalid, "PredictorStolenCardFactsInvalid"),
            (PredictorCombatPlanBindingReason.RelicFactsInvalid, "PredictorCombatRelicFactsInvalid"),
            (PredictorCombatPlanBindingReason.GremlinGoldFactsInvalid, "PredictorGremlinGoldFactsInvalid"),
            (PredictorCombatPlanBindingReason.StolenGoldLossExceedsBudget, "PredictorStolenGoldLossExceedsBudget")
        };
        Check(reasons.Select(r => r.Reason).SequenceEqual(Enum.GetValues<PredictorCombatPlanBindingReason>()),
            "Pending combat-plan allowance remains exactly the eight declared reasons");
        var cause = new InvalidOperationException("Original binding context");
        foreach (var (reason, message) in reasons)
        {
            var detailed = new PredictorCombatPlanBindingException(reason, message + ": changed diagnostic context", cause);
            Check(Pending(new PredictorCombatPlanBindingException(reason, message)) && Pending(detailed) &&
                ReferenceEquals(detailed.InnerException, cause), "Typed reason stays pending with diagnostic detail and original cause: " + reason);
            Propagates(new ArgumentException(message));
        }
        Propagates(new PredictorCombatPlanBindingException((PredictorCombatPlanBindingReason)(-1), reasons[0].Message, cause));
        Propagates(new ArgumentException("UnknownCombatExecutionFailure", cause));
        Propagates(cause);
        Console.WriteLine("RUNTIME_COMBAT_PLAN_BINDING_PASS eight typed reasons; message-independent pending; same-text/unknown execution failures preserve original exceptions");

        static void Propagates(Exception failure)
        {
            try
            {
                Pending(failure);
                throw new InvalidOperationException("Unclassified combat exception was swallowed");
            }
            catch (Exception actual) when (ReferenceEquals(actual, failure)) { }
        }
    }

    private static void CombatWorkflow(PredictorRun captured)
    {
        var context = captured.Context with { NumberOfRuns = Math.Max(1, captured.Context.NumberOfRuns) };
        var map = captured.RouteMap(1);
        var path = new RolltheSpire2.Core.Prediction.Maps.MapRouteSet(map).At(System.Numerics.BigInteger.Zero).ToImmutableArray();
        var state = PredictorSettlementEffects.ObtainRelic(captured.Working, new("RELIC", "LAVA_LAMP"));
        state = PredictorObtainSources.AddCard(context, state, new ModelKey("CARD", "ROYALTIES"));
        state = state with { OpeningCompleted = true,
            Position = new(1, 1, "workflow:entry"), Maps = [new(1, map, [path[0]])],
            Visits = [new(1, path[0], RolltheSpire2.Core.Prediction.Maps.MapPointType.Ancient, PredictorRoomKind.Opening, null, 1)],
            Acts = state.Acts.SetItem(0, state.Acts[0] with { NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 }) };
        var run = new PredictorRun(context, state);
        run.SetRoute(1, path); run.AdvanceRoute();
        run.CombatDefaults = new()
        {
            CardRules = [new(new("CARD", "ROYALTIES"), 1)],
            RelicRules = [new(new("RELIC", "LAVA_LAMP"), true)]
        };
        run.AutomaticRewardPickup = false;
        run = ReloadActiveDraft(run);
        Check(!run.AutomaticRewardPickup && run.CombatDefaults.CardRules is [{ EffectiveTriggers: 1 }] &&
            run.CombatDefaults.RelicRules is [{ EffectEnabled: true }], "Combat preferences and pickup toggle survive an active draft");
        var entry = run.Working;
        var defaultInput = run.DefaultCombatInput();
        Check(defaultInput != null, "Ordinary battle defaults should require no extra confirmation");
        CheckCombatRequirements(run, [], false, false, false, false, false);
        Select(run, defaultInput!);
        DraftPersistenceFailures(run, state);
        run = ReloadActiveDraft(run);
        CheckCombatRequirements(run, [], false, false, false, false, false);
        Check(run.Phase == PredictorPhase.Rewards && run.CanAdjustCurrentCombat, "Default battle opens editable rewards");
        Check(run.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).SelectMany(r => r.Cards).All(c => c.UpgradeLevel > 0),
            "Default Lava Lamp no-damage effect");
        string settled = System.Text.Json.JsonSerializer.Serialize(run.Working);
        var stale = run.Request!;
        var gold = run.Rewards.First(r => r.Kind == PredictorRewardKind.Gold);
        Select(run, new TakeReward(gold.Id));
        run = ReloadActiveDraft(run);
        Check(run.Rewards.Single(r => r.Id == gold.Id).Status == PredictorRewardStatus.Taken &&
            run.Rewards.Any(r => r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open),
            "Active draft preserves picked gold and pending cards without skipping them");
        var cards = run.Rewards.First(r => r.Kind == PredictorRewardKind.Card);
        Select(run, new EnterCardSelection(cards.Id));
        run = ReloadActiveDraft(run);
        Select(run, new TakeReward(cards.Id, cards.Cards[0].Id));
        Select(run, new AdjustCurrentCombat());
        run = ReloadActiveDraft(run);
        Check(run.Project().CombatInputForEditing != null,
            "Adjusted combat draft keeps its old declaration available for editing");
        Check(System.Text.Json.JsonSerializer.Serialize(run.Working) == System.Text.Json.JsonSerializer.Serialize(entry),
            "Battle edit restores exact entry state, including RNG, bags, IDs and budget");
        Check(run.Submit(stale, new TakeReward(gold.Id)) == PredictorInputResult.Stale, "Old reward button after edit");
        Select(run, defaultInput!);
        Check(System.Text.Json.JsonSerializer.Serialize(run.Working) == settled, "Reapply defaults does not duplicate automatic gains");
        Select(run, new AdjustCurrentCombat());
        Select(run, defaultInput! with { Facts = defaultInput.Facts with
            { RelicEffects = defaultInput.Facts.RelicEffects.Select(e => e with { EffectEnabled = false }).ToImmutableArray() } });
        Check(run.Working.Relics.Single(r => r.Key.Entry == "LAVA_LAMP").TookDamageThisCombat &&
            run.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).SelectMany(r => r.Cards).Any(c => c.UpgradeLevel == 0),
            "Per-instance effect override replaces Lava Lamp default");
        Select(run, new LeaveRewards());
        Check(!run.CanAdjustCurrentCombat, "Completed node closes battle edit transaction");
        var restored = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), context.CatalogFingerprint, context.UnlockFingerprint);
        Check(restored.Working.CombatCount == run.Working.CombatCount &&
            restored.Checkpoints[^1].CompletedNode!.Inputs.Count(i => i.Action == "CombatBoundary") == 1,
            "Only final revised node persists");
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(PredictorRunStore.Serialize(run))!.AsObject();
        legacy["Schema"] = 1;
        legacy.Remove("ActiveDraft"); legacy.Remove("CombatDefaults"); legacy.Remove("AutomaticRewardPickup");
        var legacyRun = PredictorRunStore.Deserialize(legacy.ToJsonString(), context.CatalogFingerprint, context.UnlockFingerprint);
        Check(legacyRun.Phase == PredictorPhase.Complete && legacyRun.AutomaticRewardPickup &&
            legacyRun.CombatDefaults.CardRules.IsEmpty && legacyRun.CombatDefaults.RelicRules.IsEmpty,
            "Schema 1 completed saves retain compatibility and initial preference defaults");
        var needsFacts = new PredictorRun(context, state with { Acts = state.Acts.SetItem(0,
            state.Acts[0] with { NormalEncounters = [new("ENCOUNTER", "GREMLIN_MERC_NORMAL")], NormalCursor = 0 }) });
        needsFacts.SetRoute(1, path); needsFacts.AdvanceRoute();
        Check(needsFacts.DefaultCombatInput() is { Facts.GremlinGold.RecoverAllStolenGold: true } && needsFacts.Phase == PredictorPhase.CombatFacts,
            "Approved enemy and closed-loop theft defaults require no invented recovery amount");
        CheckCombatRequirements(needsFacts, [PredictorEnemyCreationSource.SurpriseGremlins], false, true, false, false, false);
        foreach (var source in new[] { "FABRICATOR_NORMAL", "THIEVING_HOPPER_WEAK" })
        {
            var specific = new PredictorRun(context, state with { Acts = state.Acts.SetItem(0,
                state.Acts[0] with { NormalEncounters = [new("ENCOUNTER", source)], NormalCursor = 0 }) });
            specific.SetRoute(1, path); specific.AdvanceRoute();
            CheckCombatRequirements(specific,
                source == "FABRICATOR_NORMAL" ? [PredictorEnemyCreationSource.FabricatorBots] : [],
                source == "THIEVING_HOPPER_WEAK", false, false, false, false);
            Check(specific.DefaultCombatInput() == null, "Encounter facts cannot use ordinary battle defaults: " + source);
        }
        var relicFactsState = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", "BONE_TEA"));
        relicFactsState = PredictorSettlementEffects.ObtainRelic(relicFactsState, new("RELIC", "MEAT_ON_THE_BONE"));
        relicFactsState = relicFactsState with { Relics = relicFactsState.Relics.Select(r => r.Key.Entry == "BONE_TEA"
            ? r with { CombatsLeft = 1 } : r).ToImmutableArray() };
        var relicFacts = new PredictorRun(context, relicFactsState);
        relicFacts.SetRoute(1, path); relicFacts.AdvanceRoute();
        CheckCombatRequirements(relicFacts, [], false, false, true, true, false);
        Check(relicFacts.DefaultCombatInput() == null &&
            relicFacts.ResolveCombatDefaults() is { MissingFacts.FirstPlayerTurn: false, MissingFacts.VictoryHealth: true },
            "Bone Tea defaults first turn while conditional victory HP remains local");
        var dummy = new PredictorRun(context, state);
        dummy.BeginEvent(new(2, 28, "workflow:dummy"), new("EVENT", "BATTLEWORN_DUMMY"));
        Select(dummy, new ChooseEventOption("SETTING_1"));
        CheckCombatRequirements(dummy, [], false, false, false, false, true);
        Check(dummy.DefaultCombatInput() == null, "Event timeout cannot use ordinary battle defaults");

        // Controlled route checkpoint at the first unknown; preserve the actual
        // map/edge and event-parent continuation rather than call rewards by hand.
        int unknown = Array.FindIndex(path.ToArray(), p => map.Points.Single(n => n.Position == p).Type == RolltheSpire2.Core.Prediction.Maps.MapPointType.Unknown);
        Check(unknown > 0, "Fixture route has an unknown");
        state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", "GOLDEN_COMPASS"));
        state = state with { Relics = state.Relics.Select(r => r.Key.Entry == "GOLDEN_COMPASS" ? r with { GoldenPathAct = 0 } : r).ToImmutableArray(),
            Position = new(1, unknown, "workflow:event-entry"), Maps = [new(1, map, path.Take(unknown).ToImmutableArray())],
            Visits = path.Take(unknown).Select((p, i) => new PredictorVisit(1, p, map.Points.Single(n => n.Position == p).Type,
                i == 0 ? PredictorRoomKind.Opening : PredictorRoomKind.Monster, null, i + 1)).ToImmutableArray(),
            Acts = state.Acts.SetItem(0, state.Acts[0] with { RawEvents = [new("EVENT", "DENSE_VEGETATION")], EventCursor = 0 }) };
        var nested = new PredictorRun(context, state); nested.SetRoute(1, path); nested.AdvanceRoute();
        Select(nested, new DeclareEventEligibility(new("EVENT", "DENSE_VEGETATION"), true));
        Select(nested, new ChooseEventOption("REST"));
        Select(nested, new ProvideEventHealthFact(new(40, 80)));
        Select(nested, new ChooseEventOption("FIGHT"));
        var nestedEntry = nested.Working;
        CheckCombatRequirements(nested, [], false, false, false, false, false);
        Select(nested, nested.DefaultCombatInput()!);
        nested = ReloadActiveDraft(nested);
        Select(nested, new AdjustCurrentCombat());
        nested = ReloadActiveDraft(nested);
        Check(nested.Phase == PredictorPhase.CombatFacts &&
            System.Text.Json.JsonSerializer.Serialize(nested.Working) == System.Text.Json.JsonSerializer.Serialize(nestedEntry),
            "Nested battle edit replays event eligibility/choice and restores parent entry");
        Select(nested, nested.DefaultCombatInput()!);
        Select(nested, new LeaveRewards());
        Check(!nested.CanAdjustCurrentCombat, "Nested battle edit expires on return to parent");
        Console.WriteLine("RUNTIME_COMBAT_WORKFLOW_PASS defaults -> rewards; take/edit/reapply without doubled gains; Lava Lamp override; stale requests; save; nested event replay");
    }

    private static PredictorRun ReloadActiveDraft(PredictorRun run)
    {
        string state = System.Text.Json.JsonSerializer.Serialize(run.Working);
        string rewards = System.Text.Json.JsonSerializer.Serialize(run.Rewards);
        string draft = PredictorRunStore.Serialize(run);
        var root = System.Text.Json.Nodes.JsonNode.Parse(draft)!;
        Check(root["Schema"]!.GetValue<int>() == 7 && root["ActiveDraft"] != null,
            "An unfinished actual route node retains its active draft alongside schema 7 plans");
        var restored = PredictorRunStore.Deserialize(draft, run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
        Check(restored.Phase == run.Phase && System.Text.Json.JsonSerializer.Serialize(restored.Working) == state &&
            System.Text.Json.JsonSerializer.Serialize(restored.Rewards) == rewards &&
            restored.Checkpoints.Length == run.Checkpoints.Length,
            "Draft replay restores working state, RNG, reward identities/status/selection, and leaves checkpoints committed only");
        Check(System.Text.Json.JsonSerializer.Serialize(restored.Routes) == System.Text.Json.JsonSerializer.Serialize(run.Routes),
            "Draft replay preserves the current route commitment");
        return restored;
    }

    private static void DraftPersistenceFailures(PredictorRun run, PredictorState boundary)
    {
        string saved = PredictorRunStore.Serialize(run);
        var altered = System.Text.Json.Nodes.JsonNode.Parse(saved)!;
        altered["ActiveDraft"]!["Inputs"]![0]!["Type"] = "System.IO.FileInfo, System.Private.CoreLib";
        try
        {
            PredictorRunStore.Deserialize(altered.ToJsonString(), run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
            throw new InvalidOperationException("Arbitrary draft input type was accepted");
        }
        catch (System.Text.Json.JsonException) { }
        altered = System.Text.Json.Nodes.JsonNode.Parse(saved)!;
        altered["ActiveDraft"]!["ExpectedPhase"] = "CombatFacts";
        try
        {
            PredictorRunStore.Deserialize(altered.ToJsonString(), run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
            throw new InvalidOperationException("Wrong replay boundary was accepted");
        }
        catch (InvalidDataException e) when (e.Message == "PredictorNodeDraftReplayBoundaryMismatch") { }
        altered = System.Text.Json.Nodes.JsonNode.Parse(saved)!;
        altered["ActiveDraft"]!["Inputs"]![0]!["Data"]!["UnknownInputField"] = true;
        try
        {
            PredictorRunStore.Deserialize(altered.ToJsonString(), run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
            throw new InvalidOperationException("Unknown input payload field was silently ignored");
        }
        catch (System.Text.Json.JsonException) { }
        altered = System.Text.Json.Nodes.JsonNode.Parse(saved)!;
        altered["Context"]!["GameVersion"] = "9.999.0";
        try
        {
            PredictorRunStore.Deserialize(altered.ToJsonString(), run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
            throw new InvalidOperationException("Unknown native game data version was accepted");
        }
        catch (InvalidDataException) { }

        string path = Path.Combine(Path.GetTempPath(), "rt2-active-draft-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            PredictorRunStore.Save(path, run);
            string onDisk = File.ReadAllText(path);
            var direct = new PredictorRun(run.Context, boundary);
            direct.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2);
            try
            {
                PredictorRunStore.Save(path, direct);
                throw new InvalidOperationException("Direct unreplayable active node was saved");
            }
            catch (InvalidOperationException e) when (e.Message == "PredictorActiveNodeSaveRequiresRouteReplay") { }
            Check(File.ReadAllText(path) == onDisk, "Unsupported active save cannot replace the previous durable save");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void CheckCombatRequirements(PredictorRun run,
        ImmutableArray<PredictorEnemyCreationSource> enemies, bool theft, bool gold, bool turn, bool health, bool timeout)
    {
        string before = System.Text.Json.JsonSerializer.Serialize(run.Working);
        var request = run.Request;
        var requirements = run.CombatRequirements;
        var view = run.Project();
        Check(requirements.EnemyCreations.SequenceEqual(enemies) && requirements.StolenCards == theft &&
            requirements.GremlinGold == gold && requirements.FirstPlayerTurn == turn &&
            requirements.VictoryHealth == health && requirements.EventTimeout == timeout,
            "Operational facts must describe the current encounter and inventory");
        Check(view.RequiredEnemyCreationFacts.SequenceEqual(enemies) && view.NeedsStolenCardFacts == theft &&
            view.NeedsGremlinGoldFacts == gold && view.NeedsFirstPlayerTurnFact == turn &&
            view.NeedsVictoryHealthFact == health && view.NeedsEventTimeoutFact == timeout,
            "Route declaration controls must publish the same operational facts");
        Check(System.Text.Json.JsonSerializer.Serialize(run.Working) == before && run.Request == request,
            "Reading operational requirements/view must preserve the detached state and request");
    }

    private static void SealBudget(PredictorRun captured)
    {
        // Deliberately arbitrary fixture parameters, NOT an Owner/default amount.
        var boundary = PredictorSettlementEffects.ObtainRelic(captured.Working with { OpeningCompleted = true }, new("RELIC", "SEAL_OF_GOLD"));
        var reference = new PredictorRun(captured.Context with { SealOfGoldFixedDeduction = 0 }, boundary);
        reference.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2);
        var run = new PredictorRun(captured.Context with { SealOfGoldFixedDeduction = 17 }, boundary);
        run.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2);
        Check(run.Working.GoldBudget == Math.Max(0, boundary.GoldBudget - 17) &&
            run.Working.Streams.SequenceEqual(reference.Working.Streams) && run.Working.Odds == reference.Working.Odds &&
            run.Working.CurrentHealthEffects.SequenceEqual(reference.Working.CurrentHealthEffects), "Seal budget must not invoke Gold/RNG/health hooks");
        Select(run, new CorrectGoldBalance(9));
        Select(run, new LeaveRewards());
        var loaded = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
        Check(loaded.Context.SealOfGoldFixedDeduction == 17 && loaded.Working.GoldBudget == 9 &&
            loaded.Checkpoints[^1].CompletedNode!.Inputs.Count(i => i.Action == "SealOfGoldBudgetDeduction") == 1,
            "Seal explicit parameter, once-only history and subsequent correction persist");
        loaded.Rewind(0);
        Check(loaded.Working.GoldBudget == boundary.GoldBudget, "Seal rewind restores pre-deduction budget");
        var unassigned = new PredictorRun(captured.Context with { SealOfGoldFixedDeduction = null }, boundary);
        try
        {
            unassigned.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2);
            throw new Exception("Missing Seal amount was silently defaulted");
        }
        catch (InvalidOperationException e) when (e.Message == "PredictorSealOfGoldBudgetParameterUnassigned") { }
        Check(unassigned.Working == boundary, "Unassigned parameter must not partially settle combat");
        Console.WriteLine("RUNTIME_SEAL_BUDGET explicit fixture amount, no RNG/hooks, correction/save/rewind, unassigned atomic refusal PASS; product amount remains unset");
    }
}
