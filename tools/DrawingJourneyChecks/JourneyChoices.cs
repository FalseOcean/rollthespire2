using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

public partial class Runner
{
    private static bool TryChoose(PredictorRouteNodeKnowledge node, PredictorPlannedNode? current, Random random, AncientReplay? replay, out PredictorPlannedNode plan)
    {
        plan = current ?? new(node.Position.NodeId);
        if (plan.Ancient == null && node.Ancient is {} ancient)
        {
            var requested = replay?.NodeId == node.Position.NodeId ? replay.Relic : null;
            var offers = ancient.Offers.Where(o => o.IsKnown && o.Value!.Selectable && (requested==null || o.Value.Relic.Entry==requested)).ToArray();
            if (offers.Length > 0)
                plan = plan with { Ancient = new(ancient.Ancient, offers[random.Next(offers.Length)].Value!) };
            else
            {
                var conditional = ancient.Offers.Where(o=>o.ConditionalValue is { Selectable:true } value && (requested==null || value.Relic.Entry==requested)).ToArray();
                if (conditional.Length == 0) return false;
                plan = plan with { Ancient = new(ancient.Ancient,conditional[random.Next(conditional.Length)].ConditionalValue!) { AssumeProjectedOffer=true } };
            }
            return true;
        }
        if (plan.Ancient is {} accepted && node.Ancient is { RequiredChoices.Length: > 0 } pending &&
            pending.AppliedChildren == accepted.Children.Length && TryChild(pending.RequiredChoices[0],random) is {} child)
        {
            plan = plan with { Ancient = accepted with { Children = accepted.Children.Add(child) } };
            return true;
        }
        if (node.Event is { Event: {} key } evt &&
            evt.AppliedActions == (plan.Event?.Actions.Length ?? 0) && evt.AppliedActions < 30)
        {
            var previous = plan.Event ?? new(key);
            var available = evt.Options.Where(o => o.IsKnown && o.Enabled == true).ToArray();
            if (available.Length > 1 && previous.Actions.Any(a=>a.ConfirmAbandon == false))
                available = available.Where(o=>o.Key != "DOUBLE_DOWN").ToArray();
            var cells = evt.Crystal?.Cells.Where(c => c.Hidden).ToArray();
            var action = evt.NeededInput switch
            {
                PredictorEventNeededInput.Health when key.Entry == "ENDLESS_CONVEYOR" && available.FirstOrDefault(o => o.Key == "OBSERVE_CHEF") is {} chef =>
                    new PredictorPlannedEventAction(PredictorEventActionKind.Option) { ExpectedOption = chef.Value },
                PredictorEventNeededInput.Option when available.Length > 0 => new PredictorPlannedEventAction(PredictorEventActionKind.Option)
                    { ExpectedOption = available[random.Next(available.Length)].Value },
                PredictorEventNeededInput.Crystal when cells is {Length: > 0} => CrystalAction(evt.Crystal!, cells[random.Next(cells.Length)]),
                PredictorEventNeededInput.TrialAbandon => new(PredictorEventActionKind.ConfirmAbandon) { ConfirmAbandon = false },
                PredictorEventNeededInput.Combat when evt.CombatEncounter != null => new(PredictorEventActionKind.Combat) { ExpectedEncounter = evt.CombatEncounter,
                    EventRanOutOfTime = evt.CombatEncounter.Value.Entry.StartsWith("BATTLEWORN_DUMMY_",StringComparison.Ordinal) ? random.Next(2) == 0 : null },
                PredictorEventNeededInput.Obtain when evt.RequiredChoices.Length > 0 && TryChild(evt.RequiredChoices[0],random) is {} input =>
                    new(PredictorEventActionKind.Obtain) { Obtain = input },
                PredictorEventNeededInput.Rewards when evt.Rewards.All(r => r.IsKnown && r.Value!.CanSkip) => new(PredictorEventActionKind.LeaveRewards),
                PredictorEventNeededInput.Cards => EventCards(evt,random),
                _ => null
            };
            if (action != null)
            {
                plan = plan with { Event = previous with { Actions = previous.Actions.Add(action) } };
                return true;
            }
        }
        return false;
    }

    private static PredictorPlannedCard Target(PredictorCard value) => new(value.Id,value.Key,value.FloorAdded);
    private static bool HasBossCardOffers(PredictorRouteContinuation forecast) => new[]{1,2}.All(act=>
        forecast.Nodes.Any(n => n.Position.Act==act && n.Room==PredictorRoomKind.Boss && n.Rewards.Any(r => r.Kind==PredictorRewardKind.Card &&
            (r.Value?.Cards.Length >= 3 || r.CardOptions.Length >= 3))));

    private static bool TrySupplement(PredictorRouteContinuation forecast, ImmutableDictionary<string,PredictorPlannedNode> plans,
        HashSet<string> tried, Random random, List<object> log, out PredictorPlannedNode change)
    {
        change = null!;
        var last = forecast.Nodes.LastOrDefault();
        if (last == null) return false;
        var roots = forecast.TerminalBoundary?.PendingCauses ?? last.PendingCauses.Where(c=>c.NodeId==last.Position.NodeId).ToImmutableArray();
        if (last.Event is {NeededInput:PredictorEventNeededInput.Option} pendingEvent &&
            !pendingEvent.Options.Any(o=>o.IsKnown && o.Enabled==true))
            roots=roots.AddRange(pendingEvent.Options.SelectMany(o=>o.PendingCauses));
        if (last.Advanced && forecast.TerminalBoundary == null)
            roots = forecast.Nodes.Where(n=>n.Room==PredictorRoomKind.Boss).SelectMany(n=>n.Rewards)
                .Where(r=>r.Kind==PredictorRewardKind.Card && r.Value==null && r.CardOptions.IsEmpty).SelectMany(r=>r.PendingCauses).ToImmutableArray();
        var pending = new Stack<PredictorCombatPendingCause>(roots);
        var causes = new Dictionary<string,PredictorCombatPendingCause>();
        while (pending.TryPop(out var cause))
        {
            if (!causes.TryAdd(cause.Identity,cause)) continue;
            foreach(var parent in cause.Parents) pending.Push(parent);
        }
        var current = plans.GetValueOrDefault(last.Position.NodeId) ?? new(last.Position.NodeId);
        if (last.Shop is { RequiredChoices.Length: > 0 } merchant &&
            causes.Values.Any(c => c.NodeId == last.Position.NodeId && c.Code == "ShopObtainChoice"))
        {
            var shop = current.Shop ?? new();
            var child = merchant.RequiredChoices[0];
            if (merchant.AppliedActions <= shop.Actions.Length &&
                tried.Add($"{last.Position.NodeId}.shop-obtain.{merchant.AppliedActions}.{child.Source}.{child.Phase}") &&
                TryChild(child, random) is { } choice)
            {
                var action = new PredictorPlannedShopAction(PredictorShopActionKind.Obtain) { Obtain = choice };
                change = current with { Shop = shop with { Actions = shop.Actions.Insert(merchant.AppliedActions, action) } };
                log.Add(new { stage = "supplement", blocker = last.Position.NodeId, source = last.Position.NodeId,
                    reason = "ShopObtainChoice", relic = child.Source.Entry, phase = child.Phase.ToString(), action = choice.Action.ToString() });
                return true;
            }
        }
        if (last.Shop != null && !current.AssumePriorPotionDropsSkippedAtShop &&
            causes.Values.Any(c=>c.Code=="ShopAutomaticPurchasePending") &&
            causes.Values.Any(c=>c.Code=="MapPotionInventoryNotProjected") &&
            tried.Add(last.Position.NodeId+".automatic-shop-potions"))
        {
            change=current with {AssumePriorPotionDropsSkippedAtShop=true};
            log.Add(new {stage="supplement",blocker=last.Position.NodeId,source=last.Position.NodeId,reason="ShopLocalPotionInventory",
                premise="automatic purchases assume earlier unconfirmed drops were left on the reward screen"});
            return true;
        }
        if (last.Shop is {} removal && causes.Values.Any(c=>c.NodeId==last.Position.NodeId && c.Code=="ShopRemovalChoice") &&
            removal.RemovalCards.FirstOrDefault(c=>c.IsKnown && c.Value.Id>0) is {} removalTarget &&
            tried.Add(last.Position.NodeId+".removal"))
        {
            var shop = current.Shop ?? new();
            var action = new PredictorPlannedShopAction(PredictorShopActionKind.ChooseRemoval) {Card=Target(removalTarget.Value)};
            change=current with {Shop=shop with {Actions=shop.Actions.Insert(removal.AppliedActions,action)}};
            log.Add(new {stage="supplement",blocker=last.Position.NodeId,source=last.Position.NodeId,reason="ShopRemovalChoice",action});
            return true;
        }
        var eventKey = current.Event?.ExpectedEvent ?? last.Event?.Event;
        if (eventKey?.Entry is "STONE_OF_ALL_TIME" or "RANWID_THE_ELDER" &&
            (current.Event == null || current.Event is { Potions:null, Actions.IsEmpty:true }) &&
            causes.Values.Any(c=>c.Code=="MapPotionInventoryNotProjected"))
        {
            const int required = 1;
            var incoming = last.PotionBoundaries.FirstOrDefault(p=>p.Stage==PredictorRoutePotionStage.BeforeChoices)?.Before ?? last.Inventory;
            var available = PredictorEventPotionInput.RecentCombatDrops(
                forecast.Nodes.TakeWhile(n=>n.Position.Floor<last.Position.Floor), id=>plans.GetValueOrDefault(id), required);
            if (available.Length==required && incoming?.PotionCapacity>=required &&
                tried.Add(last.Position.NodeId+".local-potions."+eventKey.Value.Entry))
            {
                var slots = available.Select(b=>(ModelKey?)b.Potion).ToImmutableArray();
                var eventPlan = current.Event ?? new(eventKey.Value);
                change=current with {Event=eventPlan with {Potions=new(){OrderedSlots=slots}}};
                log.Add(new {stage="supplement",blocker=last.Position.NodeId,source=last.Position.NodeId,reason="EventLocalPotionInventory",
                    premise="retained these previously offered bottles in this order",receipts=available.Select(b=>new {b.NodeId,b.Potion}),slots});
                return true;
            }
        }
        foreach (var n in forecast.Nodes)
        {
            var codes = causes.Values.Where(c=>c.NodeId == n.Position.NodeId).Select(c=>c.Code).ToHashSet();
            if (codes.Count == 0) continue;
            var plan = plans.GetValueOrDefault(n.Position.NodeId) ?? new(n.Position.NodeId);
            if (codes.Contains("EventCardsChoice") && n.Event is { NeededInput: PredictorEventNeededInput.Cards } evt &&
                plan.Event is {} eventPlanCards && EventCards(evt,random) is {} selectedCards &&
                tried.Add(n.Position.NodeId+".event-cards."+eventPlanCards.Actions.Length))
            {
                change = plan with { Event = eventPlanCards with { Actions=eventPlanCards.Actions.Add(selectedCards) } };
                log.Add(new {stage="supplement",blocker=last.Position.NodeId,source=n.Position.NodeId,reason="EventCardsChoice",
                    selectedCards.DeckCards,selectedCards.CardIndices});
                return true;
            }
            if (codes.Contains("AncientBundleUnselected") && n.Ancient?.BundleChoice is { SelectedBundle:null } bundle &&
                plan.Ancient is {} parent && bundle.ChildIndex <= parent.Children.Length && TryChild(bundle.Choice,random) is {} selection &&
                tried.Add(n.Position.NodeId+".bundle"))
            {
                change = plan with {Ancient=parent with {Children=parent.Children.Insert(bundle.ChildIndex,selection)}};
                log.Add(new {stage="supplement",blocker=last.Position.NodeId,source=n.Position.NodeId,reason="AncientBundleUnselected",selection.BundleIndex});
                return true;
            }
            if (codes.Contains("ShopRemovalChoice") && n.Shop is {} blockedShop && tried.Add(n.Position.NodeId+".removal"))
            {
                var shop = plan.Shop ?? new();
                var target = blockedShop.RemovalCards.Where(c=>c.IsKnown && c.Value.Id>0).OrderBy(_=>random.Next()).FirstOrDefault();
                var action = target == null ? new PredictorPlannedShopAction(PredictorShopActionKind.CancelRemoval) :
                    new PredictorPlannedShopAction(PredictorShopActionKind.ChooseRemoval){Card=Target(target.Value)};
                change=plan with {Shop=shop with {Actions=shop.Actions.Insert(blockedShop.AppliedActions,action)}};
                log.Add(new {stage="supplement",blocker=last.Position.NodeId,source=n.Position.NodeId,reason="ShopRemovalChoice",action});
                return true;
            }
            var optional = n.OptionalObtainInputs.FirstOrDefault(o =>
                !plan.ObtainInputs.Any(p => p.Address == o.Address) &&
                (codes.Contains("RouteObtainTargetPending") || o.Choice.Source.Entry == "PAELS_TOOTH" && codes.Contains("PaelsToothStorageTargetsPending")));
            if (optional != null && TryChild(optional.Choice,random) is {} selectedOptional &&
                tried.Add(n.Position.NodeId+".obtain."+optional.Address.SourceId))
            {
                change=plan with {ObtainInputs=plan.ObtainInputs.Add(new(optional.Address,selectedOptional))};
                string reason = optional.Choice.Source.Entry == "PAELS_TOOTH" ? "PaelsToothStorageTargetsPending" : "RouteObtainTargetPending";
                log.Add(new {stage="supplement",blocker=last.Position.NodeId,source=n.Position.NodeId,reason,selectedOptional.DeckCards});
                return true;
            }
            if (codes.Contains("StolenCardIdentity") && plan.Combat?.StolenCards == null && tried.Add(n.Position.NodeId+".recover"))
            {
                change = plan with { Combat=(plan.Combat ?? new()) with {StolenCards=new([])} };
                log.Add(new {stage="supplement",blocker=last.Position.NodeId,source=n.Position.NodeId,reason="StolenCardIdentity",premise="win before any card is stolen"});
                return true;
            }
            if (codes.Contains("RestDefaultTargetPending") && plan.RestActions.IsEmpty && tried.Add(n.Position.NodeId + ".smith"))
            {
                var cards = n.RestCards.Where(c=>c.IsKnown && c.Value.UpgradeLevel<c.Value.MaxUpgradeLevel).ToArray();
                if (cards.Length == 0) continue;
                var target = Target(cards[random.Next(cards.Length)].Value);
                change = plan with { RestActions=[new(PredictorRestAction.Smith){SmithCard=target}] };
                log.Add(new {stage="supplement", blocker=last.Position.NodeId, source=n.Position.NodeId, reason="RestDefaultTargetPending",target});
                return true;
            }
            if (codes.Contains("MapPotionInventoryNotProjected") && tried.Add(n.Position.NodeId+".potion"))
            {
                var potions = n.Rewards.Where(r=>r.Kind==PredictorRewardKind.Potion && r.IsKnown && r.Value!.CanSkip &&
                    !plan.RewardChoices.Any(p=>p.ExpectedReward.Kind==PredictorRewardKind.Potion && p.Occurrence==r.Occurrence)).ToArray();
                if (potions.Length == 0) continue;
                var extra = potions.Select(r=>new PredictorPlannedRewardChoice(r.Value!,r.Occurrence,Skip:true)).ToArray();
                if (n.Event is { Event: {} key })
                {
                    var eventPlan = plan.Event ?? new(key);
                    if (eventPlan.Actions.Any(a=>a.Action==PredictorEventActionKind.LeaveRewards)) continue;
                    change = plan with {Event=eventPlan with {Actions=eventPlan.Actions.Add(new(PredictorEventActionKind.LeaveRewards))}};
                }
                else if (plan.Ancient is {} ancient)
                {
                    if (ancient.Children.Any(a=>a.Action==PredictorObtainChildAction.LeaveRewards)) continue;
                    change = plan with {Ancient=ancient with {Children=ancient.Children.Add(new(PredictorObtainChildAction.LeaveRewards,ancient.ExpectedOffer.Relic))}};
                }
                else change = plan with { RewardChoices=plan.RewardChoices.AddRange(extra) };
                log.Add(new {stage="supplement", blocker=last.Position.NodeId, source=n.Position.NodeId, reason="MapPotionInventoryNotProjected",explicitlySkipped=potions.Select(r=>r.Value!.Key.Entry)});
                return true;
            }
        }
        return false;
    }
    private static PredictorPlannedEventAction CrystalAction(PredictorCrystalView crystal, PredictorCrystalCellView cell) =>
        new(PredictorEventActionKind.CrystalCell) { ExpectedCrystal = crystal, X = cell.X, Y = cell.Y };
    private static PredictorPlannedEventAction? EventCards(PredictorEventNodeKnowledge evt, Random random)
    {
        if (evt.OfferedCardBinding is {} binding && binding.Value.Cards.Length >= evt.CardsRequired)
            return new(PredictorEventActionKind.Cards) { ExpectedCards = binding.Value.Cards, UnresolvedEnchantmentIndices = binding.UnresolvedEnchantmentIndices,
                CardIndices = Enumerable.Range(0,binding.Value.Cards.Length).OrderBy(_ => random.Next()).Take(evt.CardsRequired).ToImmutableArray() };
        var cards = evt.DeckCardCandidates.Where(c => c.IsKnown).OrderBy(_ => random.Next()).Take(evt.CardsRequired).ToArray();
        return cards.Length > 0 && cards.Length == evt.CardsRequired ? new(PredictorEventActionKind.Cards) { DeckCards = cards.Select(c => Target(c.Value)).ToImmutableArray() } : null;
    }

    private static PredictorPlannedObtainChild? TryChild(PredictorObtainChoiceKnowledge child, Random random)
    {
        var eligible = child.DeckCards.Where(c => c.IsKnown && c.Value.Id > 0).OrderBy(_ => random.Next()).ToArray();
        int count = child.Phase is PredictorPhase.TransformCards or PredictorPhase.RewardRemoveCards ? 1 : child.MinimumCards;
        var targets = eligible.Take(count).Select(c => Target(c.Value)).ToImmutableArray();
        var required = child.Rewards.FirstOrDefault(r => r.IsKnown && r.Value!.Status == PredictorRewardStatus.Open && (r.Value.Mandatory || !r.Value.CanSkip));
        return child.Phase switch {
            PredictorPhase.Rewards when required != null => new(PredictorObtainChildAction.Reward,child.Source){ Reward = new(required.Value!,required.Occurrence) },
            PredictorPhase.Rewards when child.Rewards.All(r=>r.IsKnown && (r.Value!.Status != PredictorRewardStatus.Open || r.Value.CanSkip && !r.Value.Mandatory)) => new(PredictorObtainChildAction.LeaveRewards,child.Source),
            PredictorPhase.ChooseBundle when child.Bundles.Length > 0 && child.PendingCauses.IsEmpty => new(PredictorObtainChildAction.ChooseBundle,child.Source){ ExpectedBundles = child.Bundles, BundleIndex = random.Next(child.Bundles.Length) },
            PredictorPhase.TransformCards or PredictorPhase.RewardRemoveCards or PredictorPhase.ObtainDeckCards when targets.Length == count =>
                new(child.Phase == PredictorPhase.TransformCards ? PredictorObtainChildAction.TransformCard : child.Phase == PredictorPhase.RewardRemoveCards ? PredictorObtainChildAction.RemoveRewardCards : PredictorObtainChildAction.ChooseDeckCards,child.Source){DeckCards = targets},
            PredictorPhase.ReplacePotion => new(PredictorObtainChildAction.CancelPotionReplacement,child.Source),
            PredictorPhase.ObtainChooseCard when child.Cards.Length > 0 && child.PendingCauses.IsEmpty => new(PredictorObtainChildAction.ChooseCard,child.Source){ ExpectedCards = child.Cards, CardIndices = [random.Next(child.Cards.Length)] },
            PredictorPhase.ObtainChooseManyCards when child.Cards.Length >= count && child.PendingCauses.IsEmpty => new(PredictorObtainChildAction.ChooseManyCards,child.Source){ ExpectedCards = child.Cards, CardIndices = Enumerable.Range(0,child.Cards.Length).OrderBy(_=>random.Next()).Take(count).ToImmutableArray() },
            _ => null
        };
    }
}
