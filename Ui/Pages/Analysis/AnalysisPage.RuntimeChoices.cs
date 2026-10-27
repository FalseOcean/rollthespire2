using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private void RenderRouteChoices(VBoxContainer area, PredictorRuntimeView view)
    {
        if (view.Outcome != null) { area.AddChild(WLabel(RText("outcome." + view.Outcome), true)); return; }
        if (view.Phase == PredictorPhase.Complete)
        { RButton(area, RText("advance"), () => RouteAction(() => _routeRuntime!.AdvanceRoute()), view.CanAdvance); return; }
        var request = view.Request!;
        void Send(PredictorInput input) => RouteSubmit(request, input);
        if (view.NeedsHealthFact)
        {
            RenderLocalHealth(area, view.Phase == PredictorPhase.RestHealFact,
                (hp, max) => Send(view.Phase == PredictorPhase.RestHealFact ? new ProvideRestMaxHpFact(max) : new ProvideEventHealthFact(new(hp, max))));
            return;
        }
        if (view.NeedsRoomMaximumHp)
        { RenderLocalHealth(area, true, (_, max) => Send(new ProvideRoomMaximumHp(max))); return; }
        switch (view.Phase)
        {
            case PredictorPhase.OpeningChoice:
                foreach (var key in view.OpeningChoices) RButton(area, RName(key), () => Send(new ChooseOpeningRelic(key)));
                break;
            case PredictorPhase.AncientChoice:
                foreach (var offer in view.AncientChoices)
                    RButton(area, RName(offer.Relic) + (offer.CharacterTarget is { } target ? " · " + RName(target) : ""),
                        () => Send(new ChooseAncientOffer(offer.Id)), offer.Selectable);
                break;
            case PredictorPhase.EventEligibility:
                area.AddChild(WLabel(RName(view.EventEligibilityCandidate!.Value)));
                area.AddChild(WLabel(RText("event_eligibility")));
                RButton(area, RText("yes"), () => Send(new DeclareEventEligibility(view.EventEligibilityCandidate.Value, true)));
                RButton(area, RText("no"), () => Send(new DeclareEventEligibility(view.EventEligibilityCandidate.Value, false)));
                break;
            case PredictorPhase.CombatFacts: RenderRouteCombatFacts(area, view); break;
            case PredictorPhase.Rewards: RenderRouteRewards(area, view); break;
            case PredictorPhase.ReplacePotion:
                area.AddChild(WLabel(RText("replace_potion")));
                foreach (var slot in view.Potions.Where(p => p.Potion != null))
                    RButton(area, RName(slot.Potion!.Key), () => Send(new ReplaceRewardPotion(slot.Slot, slot.Potion.Id)));
                RButton(area, RText("cancel"), () => Send(new CancelPotionReplacement()));
                break;
            case PredictorPhase.ObtainChooseCard:
                foreach (var card in view.ObtainCardChoices) RButton(area, RCard(card), () => Send(new ChooseObtainCard(card.Id)));
                if (view.CanSkipObtainCard) RButton(area, RText("skip"), () => Send(new ChooseObtainCard(null)));
                break;
            case PredictorPhase.ObtainChooseManyCards:
                RouteCardSelection(area, view.ObtainManyCardChoices, view.ObtainManyMinimum, view.ObtainManyMaximum,
                    ids => Send(new ChooseObtainManyCards(ids)));
                break;
            case PredictorPhase.ChooseBundle:
                for (int i = 0; i < view.BundleChoices.Length; i++)
                { int bundle = i; RButton(area, string.Join(" · ", view.BundleChoices[i].Select(RCard)), () => Send(new ChooseScrollBoxBundle(bundle))); }
                break;
            case PredictorPhase.TransformCards:
            case PredictorPhase.SmithCards:
            case PredictorPhase.RestCookCards:
            case PredictorPhase.ObtainDeckCards:
            case PredictorPhase.RewardRemoveCards:
                var selection = view.DeckSelection!;
                RouteCardSelection(area, selection.CandidateCardIds.Select(id => view.Inventory.Deck.Single(c => c.Id == id)),
                    selection.MinimumCardCount, selection.MaximumCardCount, ids => Send(view.Phase switch
                    {
                        PredictorPhase.TransformCards => new SelectTransformationCard(ids[0]),
                        PredictorPhase.SmithCards => new SelectSmithCard(ids[0]),
                        PredictorPhase.RestCookCards => new SelectCookCards(ids),
                        PredictorPhase.RewardRemoveCards => new SelectRemovalCards(ids),
                        _ => new ChooseObtainDeckCards(ids)
                    }));
                if (view.CanCancelTargetSelection) RButton(area, RText("cancel"), () => Send(view.Phase switch
                { PredictorPhase.SmithCards => new CancelSmithSelection(), PredictorPhase.RestCookCards => new CancelCookSelection(), _ => new CancelRewardCardRemoval() }));
                break;
            case PredictorPhase.EventCards:
                var candidates = view.EventCardCandidates.Select(id => view.EventOfferedCards.FirstOrDefault(c => c.Id == id)
                    ?? view.Inventory.Deck.Single(c => c.Id == id));
                RouteCardSelection(area, candidates, view.EventCardsRequired, view.EventCardsRequired, ids => Send(new SelectEventCards(ids)));
                break;
            case PredictorPhase.EventChoice:
                if (view.NeedsAbandonConfirmation)
                {
                    area.AddChild(WLabel(RText("abandon_confirm")));
                    RButton(area, RText("yes"), () => Send(new ConfirmTrialAbandon(true)));
                    RButton(area, RText("cancel"), () => Send(new ConfirmTrialAbandon(false)));
                }
                else if (view.Crystal is { } crystal) RenderRouteCrystal(area, crystal, request);
                else foreach (var option in view.EventChoices)
                    RButton(area, RouteEventTitle(view, option), () => Send(new ChooseEventOption(option.Key)), option.Enabled);
                break;
            case PredictorPhase.Shop:
                if (view.ShopRemovalCandidates.Length > 0)
                {
                    foreach (long id in view.ShopRemovalCandidates)
                        RButton(area, RCard(view.Inventory.Deck.Single(c => c.Id == id)), () => Send(new ChooseShopRemovalCard(id)));
                    if (view.CanCancelTargetSelection) RButton(area, RText("cancel"), () => Send(new CancelShopCardRemoval()));
                }
                else if (!view.ShopInventoryOpen)
                    RButton(area, RText("open_shop"), () => Send(new OpenShopInventory()), !view.ShopAutomaticPurchasesActive);
                else
                {
                    foreach (var offer in view.ShopOffers.Where(o => o.Stocked))
                        RButton(area, (offer.Key is { } key ? RName(key) : RText("remove_card")) + " · " + offer.Cost,
                            () => Send(offer.Kind == PredictorShopEntryKind.CardRemoval ? new BeginShopCardRemoval(offer.EntryId, view.ShopRevision) :
                                new BuyShopEntry(offer.EntryId, view.ShopRevision)), offer.Selectable);
                    RButton(area, RText("close_shop"), () => Send(new CloseShopInventory()), !view.ShopAutomaticPurchasesActive);
                }
                if (view.CanLeave) RButton(area, RText("leave"), () => Send(new LeaveShop()));
                break;
            case PredictorPhase.RestSite:
                foreach (var offer in view.RestOffers) RButton(area, RText("rest." + offer.Action), () => Send(new SelectRestAction(offer.Action)), offer.Enabled);
                if (view.CanLeave) RButton(area, RText("leave"), () => Send(new LeaveRestSite()));
                break;
            case PredictorPhase.TreasureClosed:
                RButton(area, RText("open_treasure"), () => Send(new OpenTreasure()));
                if (view.CanLeave) RButton(area, RText("leave"), () => Send(new LeaveTreasure()));
                break;
            case PredictorPhase.TreasureRelic:
                if (view.TreasureRelic is { } relic) RButton(area, RName(relic.Key), () => Send(new TakeTreasureRelic(relic.Id)));
                if (view.CanLeave) RButton(area, RText("leave"), () => Send(new LeaveTreasure()));
                break;
            case PredictorPhase.Victory: RButton(area, RText("continue"), () => Send(new ContinueVictory())); break;
            case PredictorPhase.OpeningModifier:
                RButton(area, RText("continue"), () => Send(new ActivateOpeningModifier(view.OpeningModifierOrdinal!.Value))); break;
            case PredictorPhase.OpeningModifierCards:
                RouteCardSelection(area, view.OpeningModifierCards, view.OpeningModifierCardsRequired, view.OpeningModifierCardsRequired,
                    ids => Send(new SelectOpeningModifierCards(ids))); break;
            default: throw new InvalidOperationException("PredictorUiPhaseUnmapped:" + view.Phase);
        }
    }
    private void RouteCardSelection(Node area, IEnumerable<PredictorCard> cards, int minimum, int maximum, Action<ImmutableArray<long>> submit)
    {
        area.AddChild(WLabel(string.Format(RText("select_cards"), minimum, maximum)));
        var chosen = new List<long>(); var choices = RFlow(area);
        var confirm = RButton(area, RText("confirm"), () => submit(chosen.ToImmutableArray()), minimum == 0);
        foreach (var card in cards)
        {
            var button = new CheckButton { Text = RCard(card) }; choices.AddChild(button);
            button.Toggled += value => { if (value) chosen.Add(card.Id); else chosen.Remove(card.Id); confirm.Disabled = chosen.Count < minimum || chosen.Count > maximum; };
        }
    }
    private void RenderRouteRewards(Node area, PredictorRuntimeView view)
    {
        var request = view.Request!;
        RenderRouteRewardPolicy(area, view);
        if (view.CanAdjustCombat)
        {
            var adjust = RButton(area, RText("adjust_combat"), () => RouteSubmit(request, new AdjustCurrentCombat()));
            adjust.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; adjust.Flat = true;
            adjust.TooltipText = RText("combat_defaults_short") + "\n" + RText("adjust_combat_hint");
        }
        foreach (var item in view.Rewards.Where(r => r.Reward.Status == PredictorRewardStatus.Open))
        {
            var reward = item.Reward;
            string label = reward.Key.IsValid ? RName(reward.Key) : RText("reward." + reward.Kind);
            if (reward.Kind == PredictorRewardKind.Gold) label = RGold(reward);
            if (item.CanOpen) RButton(area, label, () => RouteSubmit(request, new EnterCardSelection(reward.Id)));
            else if (reward.Kind == PredictorRewardKind.Card && item.CanTake)
            {
                foreach (var card in reward.Cards) RButton(area, RCard(card), () => RouteSubmit(request, new TakeReward(reward.Id, card.Id)));
                foreach (var alt in view.CardRewardAlternatives)
                    RButton(area, RText("alternative." + alt.Key), () => RouteSubmit(request, alt.Key switch
                    { "SKIP" => new SkipCardReward(), "REROLL" => new RerollCardReward(reward.Id), _ => new SacrificeCardReward(reward.Id, alt.RelicId) }));
            }
            else RButton(area, label, () => RouteSubmit(request, new TakeReward(reward.Id)), item.CanTake);
            if (item.CanExitCardSelection) RButton(area, RText("back_rewards"), () => RouteSubmit(request, new ExitCardSelection()));
        }
        if (view.CanLeave) RButton(area, RText("leave_rewards"), () => RouteSubmit(request, new LeaveRewards()));
    }
    private void RenderLocalHealth(Node area, bool maxOnly, Action<int, int> submit)
    {
        area.AddChild(WLabel(RText("local_health")));
        SpinBox? hp = maxOnly ? null : RNumber(area, "current_hp", 0);
        var max = RNumber(area, "maximum_hp", 0);
        RButton(area, RText("confirm"), () => submit((int)(hp?.Value ?? max.Value), (int)max.Value));
    }
    private SpinBox RNumber(Node area, string key, int initial, int max = int.MaxValue)
    {
        var row = new HBoxContainer(); area.AddChild(row); row.AddChild(WLabel(RText(key)));
        var value = new SpinBox { MinValue = 0, MaxValue = max, Value = initial, CustomMinimumSize = new(120, 36) }; row.AddChild(value); return value;
    }
    private void RenderRouteCrystal(Node area, PredictorCrystalView crystal, PredictorRequest request)
    {
        area.AddChild(WLabel(string.Format(RText("crystal_remaining"), crystal.Remaining)));
        var tools = RFlow(area);
        foreach (var tool in Enum.GetValues<PredictorCrystalTool>())
            RButton(tools, RText("crystal." + tool), () => RouteSubmit(request, new SelectCrystalTool(tool)));
        var grid = new GridContainer { Columns = 11 }; area.AddChild(grid);
        foreach (var cell in crystal.Cells.OrderBy(c => c.Y).ThenBy(c => c.X))
            RButton(grid, cell.Hidden ? "?" : "·", () => RouteSubmit(request, new RevealCrystalCell(cell.X, cell.Y)), cell.Hidden && crystal.Remaining > 0)
                .CustomMinimumSize = new(26, 26);
    }
    private string RouteEventTitle(PredictorRuntimeView view, PredictorEventOption option)
    {
        string lookup = option.Key.StartsWith("POTION:", StringComparison.Ordinal) ? "POTION" :
            option.Key.StartsWith("BUY_", StringComparison.Ordinal) ? "BUY" :
            option.Key.StartsWith("DOLL_", StringComparison.Ordinal) ? "DOLL" :
            option.Key;
        string title = RText("option." + lookup);
        if (view.CurrentEvent?.Entry == "COLORFUL_PHILOSOPHERS") title = RName(new("CHARACTER", option.Key));
        // Existing vanilla localization supplies source-authored option titles;
        // no event execution/model mutation is used for presentation.
        if (view.CurrentEvent is { } source)
        {
            var table = LocManager.Instance.GetTable("events");
            var candidate = table.GetLocStringsWithPrefix(source.Entry + ".pages.")
                .FirstOrDefault(s => s.LocEntryKey.EndsWith(".options." + option.Key + ".title", StringComparison.Ordinal));
            if (candidate != null && !candidate.GetRawText().Contains('{')) title = candidate.GetFormattedText();
            if (source.Entry == "ENDLESS_CONVEYOR" && option.Key.StartsWith("GRAB_", StringComparison.Ordinal))
            {
                string dishKey = "ENDLESS_CONVEYOR.DISHES." + option.Key[5..] + ".title";
                if (table.HasEntry(dishKey)) title = RText("option.GRAB") + " · " + table.GetLocString(dishKey).GetFormattedText();
            }
        }
        if (option.CardTargetId is { } cardId) title += " · " + RCard(view.Inventory.Deck.Single(c => c.Id == cardId));
        if (option.PotionId is { } potionId) title += " · " + RName(view.Inventory.PotionSlots.OfType<PredictorPotion>().Single(p => p.Id == potionId).Key);
        if (option.RelicId is { } relicId) title += " · " + RName(view.Inventory.Relics.Single(r => r.Id == relicId).Key);
        if (option.OfferedRelic is { } relic) title += (option.RelicId != null ? " → " : " · ") + RName(relic);
        if (option.CardType is { } type) title += " · " + RText("option." + type.ToString().ToUpperInvariant());
        if (option.GoldCost is { } cost) title += " · −" + cost + " " + RText("gold");
        if (option.GoldGain is { } gain) title += " · +" + gain + " " + RText("gold");
        return title;
    }
}
