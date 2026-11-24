using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // This drawing default keeps Smith as an action, without choosing any
    // representative card. Explicit inputs and concrete simulation stay separate.
    private static bool ApplyRouteRestDefault(PredictorRun run, PredictorPlannedNode plan,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token,
        out PredictorPlannedRestAction? action)
    {
        action = null;
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || local.Count != 0 ||
            run.Phase != PredictorPhase.RestSite || !plan.RestActions.IsEmpty || plan.LeaveRest ||
            run._nodeReplayInputs.Any(input => input is SelectRestAction or SelectSmithCard or
                SelectCookCards or ProvideRestMaxHpFact or LeaveRestSite)) return false;
        run._preview.RestDefaultInProgress = true;
        try
        {
            if (run.RestOptions.Contains(PredictorRestAction.Smith) &&
                run._preview.Partial!.Deck.Project(PredictorDeckProjectionScope.Upgradable).KnownCount != 0)
            {
                action = new(PredictorRestAction.Smith);
                if (!ApplyRouteRestAction(run, action, dependencies, knownInstanceIds, local, token, authored: false))
                    throw new InvalidOperationException("PredictorApprovedRestDefaultRejected");
            }
            // One upgrade, including with Miniature Tent. Never invent a heal,
            // Dig, or another action when Smith is unavailable or no card can upgrade.
            if (run.Submit(run.Request!, new LeaveRestSite(), token) != PredictorInputResult.Accepted)
                throw new InvalidOperationException("PredictorApprovedRestDefaultContinuationRejected");
            return true;
        }
        finally { run._preview.RestDefaultInProgress = false; }
    }

    private static bool ContinuePendingRestRewardWitness(PredictorRun run, List<RouteDependency> dependencies, HashSet<long> skipped)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults ||
            run.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && r.Mandatory && !skipped.Contains(r.Id))) return false;
        foreach (var reward in run.Rewards.Where(r => r.Status == PredictorRewardStatus.Open && !skipped.Contains(r.Id)))
            AddPendingReward(run, reward, dependencies);
        return dependencies.All(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Continuation) == 0);
    }

    // Omit an unresolved rest action in a private witness, retaining every
    // implemented effect that could differ. This is never an authored Leave,
    // Heal, Smith, or selected target. Child obtains still require their input.
    private static bool ContinueUnselectedRouteRest(PredictorRun run, PredictorPlannedNode plan, int applied,
        List<RouteDependency> dependencies, List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (local.Count != 1) return false;
        var cause = local[0];
        ImmutableArray<PredictorRestAction> actions;
        if (cause.Code == "RestChoice" && run.Phase == PredictorPhase.RestSite)
            actions = run.RestOffers.Where(o => o.Enabled).Select(o => o.Action).Distinct().ToImmutableArray();
        else if (cause.Code == "PlannedRestTargetPending" && applied < plan.RestActions.Length &&
            plan.RestActions[applied].Action is PredictorRestAction.Smith or PredictorRestAction.Cook)
            actions = plan.RestActions.Skip(applied).Select(s => s.Action).Distinct().ToImmutableArray();
        else if (cause.Code == "RestTargetChoice" && run.Phase == PredictorPhase.SmithCards)
            actions = [PredictorRestAction.Smith];
        else if (cause.Code == "RestTargetChoice" && run.Phase == PredictorPhase.RestCookCards)
            actions = [PredictorRestAction.Cook];
        else return false;

        var domains = PredictorCombatDomain.None;
        var cards = run.Working.Deck.Select(run.Working.Card).ToImmutableArray();
        bool editsExisting = false;
        foreach (var action in actions)
        {
            switch (action)
            {
                case PredictorRestAction.Smith:
                    domains |= PredictorCombatDomain.Deck;
                    editsExisting = true;
                    break;
                case PredictorRestAction.Heal:
                    domains |= PredictorCombatDomain.Health;
                    if (PredictorSettlementEffects.Has(run.Working, "DREAM_CATCHER"))
                    {
                        domains |= PredictorCombatDomain.RewardsRng | PredictorCombatDomain.Odds |
                            PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds;
                        // The optional reward is not generated in this witness;
                        // its possible cards still participate in later readers.
                        var recipe = new PredictorRewardPlan(PredictorRewardKind.Card, EncounterSource: true, IsFromCombat: false);
                        var keys = PredictorRewardGeneration.ResolveCardPool(run.Context, run.Working, ref recipe, isCardReward: true)
                            .Select(c => c.CardKey).ToHashSet();
                        cards = cards.AddRange(run.Context.Catalog!.CardDefinitions.Where(d => keys.Contains(d.Prototype.Key))
                            .Select(d => d.Prototype)).Distinct().ToImmutableArray();
                    }
                    if (PredictorSettlementEffects.Has(run.Working, "TINY_MAILBOX"))
                        domains |= PredictorCombatDomain.RewardsRng | PredictorCombatDomain.PotionInventory | PredictorCombatDomain.InstanceIds;
                    break;
                case PredictorRestAction.Cook:
                    domains |= PredictorCombatDomain.Deck | PredictorCombatDomain.Health;
                    editsExisting = true;
                    if (RouteCardMayVary(run, dependencies, "SPOILS_MAP") || cards.Any(c => c.Key.Entry == "SPOILS_MAP"))
                        domains |= PredictorCombatDomain.Map;
                    break;
                case PredictorRestAction.Clone:
                    domains |= PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds;
                    break;
                case PredictorRestAction.Hatch:
                    domains |= PredictorCombatDomain.Deck | PredictorCombatDomain.Relics | PredictorCombatDomain.InstanceIds;
                    editsExisting = true;
                    break;
                case PredictorRestAction.Dig:
                    // A random optional obtain can change inventory, bags and
                    // all its nested RNG readers. Do not treat witness Circlet
                    // or any particular bag draw as the chosen obtain.
                    domains |= PredictorCombatDomain.Deck | PredictorCombatDomain.Relics | PredictorCombatDomain.InstanceIds |
                        PredictorCombatDomain.Health | PredictorCombatDomain.Gold | PredictorCombatDomain.Map |
                        PredictorCombatDomain.RewardsRng | PredictorCombatDomain.NicheRng | PredictorCombatDomain.Odds |
                        PredictorCombatDomain.CombatPotionRng | PredictorCombatDomain.PotionInventory |
                        PredictorCombatDomain.CardRewards | PredictorCombatDomain.GoldRewards | PredictorCombatDomain.PotionRewards |
                        PredictorCombatDomain.RelicRewards | PredictorCombatDomain.ExtraCardRewards |
                        PredictorCombatDomain.ExtraGoldRewards | PredictorCombatDomain.RemoveCardRewards;
                    cards = run.Context.Catalog!.CardDefinitions.Select(d => d.Prototype).ToImmutableArray();
                    editsExisting = true;
                    break;
                case PredictorRestAction.Lift:
                case PredictorRestAction.Kindle:
                    domains |= PredictorCombatDomain.Relics;
                    break;
            }
        }
        local[0] = cause with { AffectedDomains = domains };
        bool simpleDeckEdits = actions.All(action => action is PredictorRestAction.Smith or PredictorRestAction.Cook or PredictorRestAction.Heal or
            PredictorRestAction.Lift or PredictorRestAction.Kindle) &&
            !(actions.Contains(PredictorRestAction.Heal) && PredictorSettlementEffects.Has(run.Working, "DREAM_CATCHER"));
        var deckFacts = PredictorDeckUncertainFact.All;
        if (simpleDeckEdits)
        {
            deckFacts = actions.Contains(PredictorRestAction.Cook) ? PredictorDeckUncertainFact.Membership : PredictorDeckUncertainFact.None;
            if (actions.Contains(PredictorRestAction.Smith))
            {
                deckFacts |= PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Other;
                if (cards.Any(card => PredictorCardChanges.Definition(run.Context, card.Key).Levels
                    .Select(level => level.Eternal).Distinct().Count() > 1)) deckFacts |= PredictorDeckUncertainFact.Removable;
            }
        }
        bool smithOnlyDeck = simpleDeckEdits && !actions.Contains(PredictorRestAction.Cook);
        if (smithOnlyDeck) cards = cards.Where(card => card.MaxUpgradeLevel > 0).ToImmutableArray();
        bool scopedRelicState = actions.All(action => action is PredictorRestAction.Smith or PredictorRestAction.Cook or
            PredictorRestAction.Heal or PredictorRestAction.Lift or PredictorRestAction.Kindle) &&
            !(actions.Contains(PredictorRestAction.Heal) && (PredictorSettlementEffects.Has(run.Working, "DREAM_CATCHER") ||
                PredictorSettlementEffects.Has(run.Working, "TINY_MAILBOX")));
        AddDependency(dependencies, new(local[0], cards, editsExisting)
        {
            DeckFacts = deckFacts,
            AffectedCardIds = smithOnlyDeck ? cards.Select(card => card.Id).ToImmutableArray() : default,
            ChangedCardMembershipEntries = smithOnlyDeck ? [] : default,
            ChangedCardStateEntries = smithOnlyDeck ? [] : default,
            ChangedRelicEntries = scopedRelicState ? [] : default,
            ChangedRelicStateEntries = scopedRelicState ? actions.Select(action => action switch
                { PredictorRestAction.Lift => "GIRYA", PredictorRestAction.Kindle => "PUMPKIN_CANDLE", _ => null })
                .Where(entry => entry != null).Select(entry => entry!).ToImmutableArray() : default,
            ChangesPersonalRelicBag = scopedRelicState ? false : null,
            ChangesSharedRelicBag = scopedRelicState ? false : null
        });
        // The retained dependency now owns the effect scope. Republishing the
        // same local cause as an unscoped dependency would erase that contract.
        local.Clear();
        // A previously entered Smith picker can only occur on an active draft.
        // Cancellation here is private witness navigation, never a user choice.
        if (run.Phase == PredictorPhase.SmithCards)
            run.Submit(run.Request!, new CancelSmithSelection(), token);
        if (run.Phase == PredictorPhase.RestCookCards)
            run.Submit(run.Request!, new CancelCookSelection(), token);
        return run.Phase == PredictorPhase.RestSite &&
            run.Submit(run.Request!, new LeaveRestSite(), token) == PredictorInputResult.Accepted;
    }
}
