using System.Collections.Immutable;
using Godot;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private CheckButton RCheck(Node parent, string key, bool initial = false)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; parent.AddChild(row);
        var b = new CheckButton { ButtonPressed = initial }; row.AddChild(b);
        row.AddChild(WLabel(RText(key))); return b;
    }
    private void ROrder<T>(Node parent, List<T> order, Func<T, string> name)
    {
        var list = WColumn(); parent.AddChild(list);
        void Render()
        {
            WClear(list);
            for (int i = 0; i < order.Count; i++)
            {
                int index = i; var row = RFlow(list); row.AddChild(WLabel((i + 1) + " · " + name(order[i])));
                RButton(row, "↑", () => { (order[index - 1], order[index]) = (order[index], order[index - 1]); Render(); }, i > 0);
                RButton(row, "↓", () => { (order[index + 1], order[index]) = (order[index], order[index + 1]); Render(); }, i < order.Count - 1);
            }
        }
        Render();
    }
    private void RenderRouteCombatFacts(VBoxContainer area, PredictorRuntimeView view)
    {
        var request = view.Request!;
        var renderedRun = _routeRuntime!;
        long renderedRevision = renderedRun.Revision;
        string renderedContext = _routeAnalysisKey;
        bool Current() => RouteCombatEditorIsCurrent(renderedRun, request, renderedRevision, renderedContext);
        var defaults = renderedRun.ResolveCombatDefaults()!.Facts;
        var draft = view.CombatInputForEditing?.Facts ?? defaults;
        area.AddChild(WLabel(RText("combat_defaults_short")));
        var advancedToggle = RCheck(area, "extra_combat_facts");
        var advanced = WColumn(); area.AddChild(advanced); advanced.Visible = false;
        advancedToggle.Toggled += on => advanced.Visible = on;
        var triggerArea = WColumn(); advanced.AddChild(triggerArea);
        var triggers = new Dictionary<long, SpinBox>();
        foreach (long id in view.DefaultCombatCardInstances)
        {
            triggerArea.AddChild(WLabel(RCard(view.Inventory.Deck.Single(c => c.Id == id))));
            int defaultCount = defaults.CardTriggers.Single(t => t.InstanceId == id).EffectiveTriggers;
            int initial = draft.AutomaticCardEffects ? 1 : draft.CardTriggers.FirstOrDefault(t => t.InstanceId == id)?.EffectiveTriggers ?? defaultCount;
            var count = RNumber(triggerArea, "triggers", initial, Math.Max(10000, initial));
            triggers[id] = count;
            var origin = WLabel(RText(initial == defaultCount ? "combat_using_default" : "combat_node_override"));
            triggerArea.AddChild(origin);
            count.ValueChanged += value => origin.Text = RText((int)value == defaultCount ? "combat_using_default" : "combat_node_override");
        }
        RButton(triggerArea, RText("combat_reset_defaults"), () =>
        {
            if (!Current()) return;
            foreach (var pair in triggers) pair.Value.Value = defaults.CardTriggers.Single(t => t.InstanceId == pair.Key).EffectiveTriggers;
        }).TooltipText = "";
        var temporary = draft.TemporaryCardTriggers.ToList() ?? new List<PredictorTemporaryCardTrigger>();
        var tempRows = WColumn(); advanced.AddChild(tempRows);
        foreach (var item in temporary) tempRows.AddChild(_palette.Label(RName(item.Card) + " ×" + item.EffectiveTriggers, 16));
        if (view.TemporaryCombatCardSources.Length > 0)
        {
            var tempCard = new OptionButton(); advanced.AddChild(tempCard);
            foreach (var key in view.TemporaryCombatCardSources) tempCard.AddItem(RName(key));
            var upgraded = RNumber(advanced, "upgrade_level", 0, 10000);
            var times = RNumber(advanced, "triggers", 1, 10000);
            RButton(advanced, RText("add_temporary"), () =>
            {
                var item = new PredictorTemporaryCardTrigger(view.TemporaryCombatCardSources[tempCard.Selected], (int)upgraded.Value, (int)times.Value);
                temporary.Add(item); tempRows.AddChild(WLabel("#" + temporary.Count + " · " + RName(item.Card) + " +" + item.UpgradeLevel + " ×" + item.EffectiveTriggers));
            });
            RButton(advanced, RText("clear_temporary"), () => { temporary.Clear(); WClear(tempRows); });
        }
        var lamps = new Dictionary<long, CheckButton>();
        foreach (var lamp in view.Inventory.Relics.Where(r => !r.Melted && PredictorCombatDefaults.IsConfigurableRelic(r.Key)))
        {
            area.AddChild(WLabel(RName(lamp.Key) + " · #" + lamp.Id));
            bool initial = draft.RelicEffects.FirstOrDefault(r => r.RelicId == lamp.Id)?.EffectEnabled ?? !draft.TookDamageThisCombat;
            var enabled = RCheck(area, "lava_active", initial);
            lamps[lamp.Id] = enabled;
            bool defaultEnabled = defaults.RelicEffectEnabled(lamp);
            var origin = WLabel(RText(initial == defaultEnabled ? "combat_using_default" : "combat_node_override"));
            area.AddChild(origin);
            enabled.Toggled += value => origin.Text = RText(value == defaultEnabled ? "combat_using_default" : "combat_node_override");
        }
        CheckButton? turn = view.NeedsFirstPlayerTurnFact ? RCheck(area, "first_turn", draft.FirstPlayerTurnOccurred ?? true) : null;
        SpinBox? victoryHp = null, victoryMax = null;
        if (view.NeedsVictoryHealthFact)
        {
            area.AddChild(WLabel(RText("victory_health")));
            victoryHp = RNumber(area, "current_hp", draft.VictoryHealth?.CurrentHp ?? 0); victoryMax = RNumber(area, "maximum_hp", draft.VictoryHealth?.MaxHp ?? 0);
        }
        var enemyCounts = new Dictionary<PredictorEnemyCreationSource, (SpinBox Count, CheckButton Confirmed)>();
        foreach (var source in view.RequiredEnemyCreationFacts)
        {
            var fact = draft.EnemyCreations.FirstOrDefault(f => f.Source == source);
            var count = RNumber(area, "enemy." + source, fact?.Count ?? 0, Math.Max(100000, fact?.Count ?? 0));
            var confirmed = RCheck(area, "combat_amount_confirmed", fact != null);
            count.ValueChanged += _ => confirmed.ButtonPressed = true;
            enemyCounts[source] = (count, confirmed);
        }
        CheckButton? timeout = view.NeedsEventTimeoutFact ? RCheck(area, "dummy_timeout", view.CombatInputForEditing?.EventRanOutOfTime ?? false) : null;
        var stolen = new Dictionary<long, (CheckButton Selected, CheckButton Returned)>();
        CheckButton? stolenConfirmed = null;
        if (view.NeedsStolenCardFacts)
        {
            area.AddChild(WLabel(RText("stolen_cards")));
            stolenConfirmed = RCheck(area, "combat_stolen_confirmed", draft.StolenCards != null);
            foreach (var card in view.Inventory.Deck)
            {
                var row = RFlow(area); var selected = new CheckButton { Text = RCard(card), ButtonPressed = draft.StolenCards?.Cards.Any(c => c.CardId == card.Id) ?? false }; row.AddChild(selected);
                var returned = RCheck(row, "returned_reward", draft.StolenCards?.Cards.FirstOrDefault(c => c.CardId == card.Id)?.ReturnedAsReward ?? true); stolen[card.Id] = (selected, returned);
                selected.Toggled += _ => stolenConfirmed.ButtonPressed = true;
            }
        }
        CheckButton? escaped = null, goldStolen = null, returnedGold = null, goldConfirmed = null,
            recoverAll = null, lossConfirmed = null;
        SpinBox? goldAmount = null, goldLoss = null;
        bool UsesLossMode() => goldStolen?.ButtonPressed == true && returnedGold?.ButtonPressed == false;
        string BalanceBlockers() => string.Join(", ", PredictorCombatRewardFacts.RecoveredGoldBalanceBlockers(renderedRun.Context, renderedRun.Working)
            .Select(c => RText("gold_balance_blocker." + c)));
        if (view.NeedsGremlinGoldFacts)
        {
            escaped = RCheck(area, "gremlin_escaped", draft.GremlinGold?.FatGremlinEscaped ?? false);
            goldStolen = RCheck(area, "gold_stolen", draft.GremlinGold?.GoldWasStolen ?? true);
            returnedGold = RCheck(area, "gold_return_reward", draft.GremlinGold == null || PredictorCombatRewardFacts.HasHeistReward(draft.GremlinGold));
            recoverAll = RCheck(area, "gold_recover_all", draft.GremlinGold?.RecoverAllStolenGold ?? true);
            var amountArea = WColumn(); area.AddChild(amountArea);
            goldAmount = RNumber(amountArea, "gold_return_amount", draft.GremlinGold?.RecoveredGold ?? 0);
            goldConfirmed = RCheck(amountArea, "combat_amount_confirmed", draft.GremlinGold?.RecoveredGold != null);
            goldAmount.ValueChanged += _ => goldConfirmed.ButtonPressed = true;
            amountArea.AddChild(WLabel(RText("gold_specific_amount_hint")));
            var lossArea = WColumn(); area.AddChild(lossArea);
            goldLoss = RNumber(lossArea, "gold_unrecovered_loss", draft.GremlinGold?.StolenGoldLoss ?? 0);
            lossConfirmed = RCheck(lossArea, "combat_amount_confirmed", draft.GremlinGold?.StolenGoldLoss != null);
            goldLoss.ValueChanged += _ => lossConfirmed.ButtonPressed = true;
            var blocker = WLabel(""); area.AddChild(blocker);
            void RefreshGoldMode()
            {
                amountArea.Visible = returnedGold.ButtonPressed && !recoverAll.ButtonPressed;
                lossArea.Visible = UsesLossMode();
                blocker.Visible = recoverAll.ButtonPressed && !PredictorCombatRewardFacts.CanBalanceRecoveredGold(renderedRun.Context, renderedRun.Working);
                if (blocker.Visible) blocker.Text = RText("gold_net_restore_blocked") + " " + BalanceBlockers();
            }
            escaped.Toggled += value =>
            {
                if (value) { returnedGold.ButtonPressed = false; recoverAll.ButtonPressed = false; }
                RefreshGoldMode();
            };
            returnedGold.Toggled += value =>
            {
                if (value) escaped.ButtonPressed = false;
                else recoverAll.ButtonPressed = false;
                RefreshGoldMode();
            };
            goldStolen.Toggled += value =>
            {
                if (!value) { returnedGold.ButtonPressed = false; recoverAll.ButtonPressed = false; }
                RefreshGoldMode();
            };
            recoverAll.Toggled += value =>
            {
                if (value) { goldStolen.ButtonPressed = true; escaped.ButtonPressed = false; returnedGold.ButtonPressed = true; }
                RefreshGoldMode();
            };
            RefreshGoldMode();
            RButton(area, RText("correct_gold"), () =>
            {
                if (!Current()) return;
                RoutePopup(RText("correct_gold"), (body, popup) =>
                {
                    body.AddChild(WLabel(RText("gold_funding_premise_hint")));
                    var amount = RNumber(body, "gold", renderedRun.Working.GoldBudget);
                    RButton(body, RText("correct_gold"), () =>
                    {
                        if (!Current()) return;
                        popup.QueueFree(); RouteSubmit(request, new CorrectGoldBalance((int)amount.Value));
                    }).TooltipText = "";
                });
            }).TooltipText = "";
        }
        var changes = draft.Potions?.Changes.ToList() ?? new List<PredictorCombatPotionChange>();
        var potionRows = WColumn(); advanced.AddChild(potionRows);
        foreach (var change in changes) potionRows.AddChild(_palette.Label(RText("potion_action." + change.Action), 16));
        var action = new OptionButton(); advanced.AddChild(action);
        foreach (var kind in Enum.GetValues<PredictorCombatPotionAction>()) action.AddItem(RText("potion_action." + kind));
        var reference = new OptionButton(); advanced.AddChild(reference);
        reference.AddItem(RText("generated_potion"));
        var heldPotions = view.Inventory.PotionSlots.OfType<PredictorPotion>().ToArray();
        foreach (var potion in heldPotions) reference.AddItem(RName(potion.Key) + " · #" + potion.Id);
        var generated = RNumber(advanced, "generated_ordinal", 1, 100000);
        var tail = new OptionButton(); advanced.AddChild(tail);
        foreach (long id in view.CombatLizardTailCandidates) tail.AddItem(RText("tail_instance") + " #" + id);
        var placement = new OptionButton(); advanced.AddChild(placement);
        placement.AddItem(RText("after_effects"));
        foreach (long id in view.DefaultCombatCardInstances) placement.AddItem(RText("before_effect") + " " + RCard(view.Inventory.Deck.Single(c => c.Id == id)));
        placement.AddItem(RText("before_temporary"));
        var temporaryOrdinal = RNumber(advanced, "temporary_ordinal", 1, 100000);
        var occurrence = RNumber(advanced, "effect_occurrence", 1, 100000);
        RButton(advanced, RText("add_inventory_change"), () =>
        {
            var kind = (PredictorCombatPotionAction)action.Selected;
            bool beforeTemp = placement.Selected == placement.ItemCount - 1;
            long? beforeCard = placement.Selected > 0 && !beforeTemp ? view.DefaultCombatCardInstances[placement.Selected - 1] : null;
            var potionRef = kind is PredictorCombatPotionAction.AdditionalAlchemize or PredictorCombatPotionAction.LizardTailTriggered ? null :
                reference.Selected == 0 ? new PredictorPotionReference(GeneratedOrdinal: (int)generated.Value - 1) : new PredictorPotionReference(heldPotions[reference.Selected - 1].Id);
            if (kind == PredictorCombatPotionAction.LizardTailTriggered && tail.Selected < 0) return;
            changes.Add(new(kind, potionRef, beforeCard, beforeCard != null || beforeTemp ? (int)occurrence.Value - 1 : 0,
                kind == PredictorCombatPotionAction.LizardTailTriggered ? view.CombatLizardTailCandidates[tail.Selected] : null,
                beforeTemp ? (int)temporaryOrdinal.Value - 1 : null));
            potionRows.AddChild(WLabel(changes.Count + " · " + action.GetItemText(action.Selected) + " · " + reference.GetItemText(reference.Selected)));
        });
        RButton(advanced, RText("clear_inventory_changes"), () => { changes.Clear(); WClear(potionRows); });
        var review = WColumn(); area.AddChild(review);
        RButton(area, RText("apply_combat"), () =>
        {
            if (!Current()) return;
            WClear(review);
            if (enemyCounts.Values.Any(p => !p.Confirmed.ButtonPressed) ||
                stolenConfirmed is { ButtonPressed: false } ||
                returnedGold is { ButtonPressed: true } && recoverAll is { ButtonPressed: false } && goldConfirmed is { ButtonPressed: false } ||
                UsesLossMode() && lossConfirmed is { ButtonPressed: false } ||
                victoryHp != null && (victoryHp.Value < 1 || victoryMax!.Value < victoryHp.Value))
            {
                review.AddChild(WLabel(RText("combat_local_facts_pending")));
                return;
            }
            if (recoverAll is { ButtonPressed: true } &&
                !PredictorCombatRewardFacts.CanBalanceRecoveredGold(renderedRun.Context, renderedRun.Working))
            {
                review.AddChild(WLabel(RText("gold_net_restore_blocked") + " " + BalanceBlockers()));
                return;
            }
            var facts = new PredictorCombatFacts(AutomaticCardEffects: false)
            {
                CardTriggers = triggers.Select(p => new PredictorCardTrigger(p.Key, (int)p.Value.Value)).ToImmutableArray(),
                RelicEffects = lamps.Select(p => new PredictorCombatRelicEffect(p.Key, p.Value.ButtonPressed)).ToImmutableArray(),
                TemporaryCardTriggers = temporary.ToImmutableArray(),
                Potions = new(changes.Count == 0, changes.ToImmutableArray()),
                TookDamageThisCombat = lamps.Count > 0 && lamps.Values.All(l => !l.ButtonPressed),
                FirstPlayerTurnOccurred = turn?.ButtonPressed ?? draft.FirstPlayerTurnOccurred ?? true,
                VictoryHealth = victoryHp == null ? null : new((int)victoryHp.Value, (int)victoryMax!.Value),
                EnemyCreations = enemyCounts.Select(p => new PredictorEnemyCreationFact(p.Key, (int)p.Value.Count.Value)).ToImmutableArray(),
                StolenCards = view.NeedsStolenCardFacts ? new(stolen.Where(p => p.Value.Selected.ButtonPressed).Select(p => new PredictorStolenCardFact(p.Key, p.Value.Returned.ButtonPressed)).ToImmutableArray()) : null,
                GremlinGold = escaped == null ? null : new PredictorGremlinGoldFacts(escaped.ButtonPressed, goldStolen!.ButtonPressed,
                    returnedGold!.ButtonPressed && recoverAll is not { ButtonPressed: true } ? (int)goldAmount!.Value : null)
                {
                    RecoverAllStolenGold = recoverAll?.ButtonPressed == true,
                    StolenGoldLoss = UsesLossMode() ? (int)goldLoss!.Value :
                        returnedGold!.ButtonPressed && recoverAll is not { ButtonPressed: true } && goldStolen!.ButtonPressed
                            ? (int)goldAmount!.Value : null
                }
            };
            // An authored order of effective occurrences, never simulated plays.
            var sourceOrder = new List<PredictorCombatCardSourceRef>();
            if (temporary.Count > 0)
            {
                foreach (var trigger in facts.CardTriggers) for (int i = 0; i < trigger.EffectiveTriggers; i++) sourceOrder.Add(new(trigger.InstanceId));
                for (int t = 0; t < temporary.Count; t++) for (int i = 0; i < temporary[t].EffectiveTriggers; i++) sourceOrder.Add(new(TemporarySourceIndex: t));
                if (draft.CombatCardSourceOrder.Length == sourceOrder.Count &&
                    draft.CombatCardSourceOrder.GroupBy(r => r).All(g => sourceOrder.Count(r => r == g.Key) == g.Count()))
                    sourceOrder = draft.CombatCardSourceOrder.ToList();
                review.AddChild(WLabel(RText("effect_order")));
                ROrder(review, sourceOrder, r => r.DeckCardId is { } id ? RCard(view.Inventory.Deck.Single(c => c.Id == id)) :
                    RName(temporary[r.TemporarySourceIndex!.Value].Card) + " · " + RText("temporary"));
            }
            void Settle()
            {
                if (!Current()) return;
                var orderedFacts = facts with { CombatCardSourceOrder = sourceOrder.ToImmutableArray() };
                try
                {
                    var rewardOrder = renderedRun.DescribeCombatRewardOrder(orderedFacts).ToList();
                    if (draft.ExtraRewardOrder.Length == rewardOrder.Count &&
                        draft.ExtraRewardOrder.Distinct().Count() == rewardOrder.Count &&
                        draft.ExtraRewardOrder.All(rewardOrder.Contains)) rewardOrder = draft.ExtraRewardOrder.ToList();
                    void Submit()
                    {
                        if (Current()) RouteSubmit(request, new ResolveCombat(orderedFacts with
                            { ExtraRewardOrder = rewardOrder.ToImmutableArray() }, timeout?.ButtonPressed));
                    }
                    if (rewardOrder.Select(r => r.Source).Distinct().Count() > 1)
                    {
                        var orderArea = WColumn(); review.AddChild(orderArea);
                        orderArea.AddChild(WLabel(RText("extra_reward_order")));
                        ROrder(orderArea, rewardOrder, r => RText("extra." + r.Source) + " " + (r.Index + 1));
                        RButton(orderArea, RText("apply_combat"), Submit);
                    }
                    else Submit();
                }
                catch (ArgumentException e) when (e.Message == "PredictorStolenGoldLossExceedsBudget")
                { review.AddChild(WLabel(RText("gold_stolen_loss_exceeds_budget"))); }
                catch (ArgumentException) { review.AddChild(WLabel(RText("facts_invalid"))); }
            }
            if (temporary.Count > 0) RButton(review, RText("apply_combat"), Settle);
            else Settle();
        });
        if (view.CombatInputForEditing is { } previous)
            RButton(area, RText("cancel"), () => { if (Current()) RouteSubmit(request, previous); });
    }
}
