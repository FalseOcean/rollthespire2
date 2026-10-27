using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private void RoomEditor(Context c, EditorParts parts, bool includeRestRemoval = true, bool premisesOnly = false)
    {
        if (c.Node.Room == PredictorRoomKind.RestSite)
            RestEditor(c, parts, includeRemoval: includeRestRemoval, premisesOnly: premisesOnly);
        if (!premisesOnly && c.Node.Shop is { } shop) ShopEditor(c, parts, shop);
        if (!premisesOnly && c.Node.Treasure is { } treasure) TreasureEditor(c, parts, treasure);
        if (c.Node.Ancient is { } ancient) AncientEditor(c, parts, ancient, premisesOnly: premisesOnly);
        // Main owns these rooms' required children and reward controls. Their
        // premises add only optional refinements, never another copy.
        if (premisesOnly && (c.Node.Ancient != null || c.Node.Shop != null || c.Node.Treasure != null ||
            c.Node.Room == PredictorRoomKind.RestSite)) return;
        bool eventChild = c.Node.Event is { } occurrence && EventEditor(c, parts, occurrence);
        if (RewardObtainParent(c.Node.Rewards) != null) RewardObtainSections(c, parts, c.Node.Rewards);
        else if (!eventChild && RequiredChild(c.Node) is { } child &&
            !(c.Node.Ancient?.BundleChoice != null && child.Phase == PredictorPhase.ChooseBundle)) ObtainEditor(c, parts, child);
    }
    private void RestEditor(Context c, EditorParts parts, bool detailed = true, bool includeRemoval = true, bool premisesOnly = false)
    {
        int applied = c.Node.AppliedRestActions;
        var actions = c.Plan.RestActions;
        var pending = actions.ElementAtOrDefault(applied);
        if (detailed && !premisesOnly && c.Node.RestDefaultApplied)
            parts.Sections.Add(new("", c.Node.PendingCauses.Any(cause => cause.Code == "RestDefaultTargetPending")
                ? M("rest_default_pending") : c.Node.DefaultRestAction?.SmithCard is { } upgraded
                ? _text.Format("predictor.map.rest_default_smith", _names.Resolve(upgraded.Key, GameContentKind.Card))
                : M("rest_default_none"), [], []));
        if (!detailed && !actions.IsEmpty)
        {
            parts.Sections.Add(new(R("plan.room.RestSite"),
                _text.Format("predictor.map.rest_plan_progress", Math.Min(applied, actions.Length), Math.Max(0, actions.Length - applied)), [], []));
            foreach (var (step, index) in actions.Select((step, index) => (step, index)))
            {
                var targets = step.SmithCard is { } smith
                    ? c.Node.RestCards.Where(card => card.IsKnown && card.Value.Id == smith.InstanceId).Select(card => Object(card.Value)).ToArray()
                    : step.CookCards.Select(target => c.Node.RestCards.FirstOrDefault(card => card.IsKnown && card.Value.Id == target.InstanceId))
                        .Where(card => card != null).Select(card => Object(card!.Value)).ToArray();
                parts.Sections.Add(new(R("rest." + step.Action), M(index < applied ? "rest_applied" : "rest_pending"), targets,
                    [Choice(c, R("plan.remove_action"), context => Set(context,
                        context.Plan with { RestActions = context.Plan.RestActions.RemoveAt(index), LeaveRest = false }), selected: index < applied)])
                    { PlannedActionsRow = true });
            }
        }
        else if (!premisesOnly && includeRemoval && !actions.IsEmpty)
            parts.Sections.Add(new(R("plan.room.RestSite"), "", [], actions.Select((step, index) =>
                Choice(c, R("plan.remove_action") + " · " + R("rest." + step.Action), context => Set(context,
                    context.Plan with { RestActions = context.Plan.RestActions.RemoveAt(index), LeaveRest = false }))).ToArray()));
        if (detailed && c.Node.RestDefaultApplied && c.Node.DefaultRestAction is { Action: PredictorRestAction.Smith, SmithCard: null } defaultSmith)
        {
            var targets = RestTargets(c).Where(card => card.UpgradeLevel < card.MaxUpgradeLevel);
            parts.Sections.Add(new(R("rest.Smith"), "", [], targets.Select(card => Choice(c, R("plan.take"), context =>
                Set(context, context.Plan with { RestActions = [defaultSmith with { SmithCard = Target(card) }], LeaveRest = true }), Object(card))).ToArray()));
        }
        foreach (var (step, index) in actions.Select((step, index) => (step, index)).Where(item => detailed &&
            item.step.Action is PredictorRestAction.Smith or PredictorRestAction.Cook &&
            (item.index < applied || item.index == applied &&
                !c.Node.PendingCauses.Any(cause => cause.NodeId == c.Node.Position.NodeId && cause.Code == "PlannedRestActionUnavailable"))))
        {
            // A drawing may already have continued past this targetless
            // action. Its optional editor still owns that action's index.
            if (step.Action == PredictorRestAction.Smith)
            {
                var targets = RestTargets(c).Where(card => card.UpgradeLevel < card.MaxUpgradeLevel).ToArray();
                parts.Sections.Add(new(R("rest.Smith"), "", [],
                    targets.Select(card => Choice(c, R("plan.take"), context =>
                        Set(context, context.Plan with { RestActions = context.Plan.RestActions.SetItem(index, step with { SmithCard = Target(card) }) }), Object(card),
                        selected: step.SmithCard == Target(card))).ToArray()));
            }
            else
            {
                var targets = RestTargets(c).Where(card => card.Removable).ToImmutableArray();
                CardSelection(c, parts, "rest-cook-" + index, targets, 2, 2, (context, indices) => Set(context,
                    context.Plan with { RestActions = context.Plan.RestActions.SetItem(index, step with {
                        CookCards = indices.Select(i => Target(targets[i])).ToImmutableArray() }) }),
                    selectedIndices: targets.Select((card, targetIndex) => (card, targetIndex))
                        .Where(item => step.CookCards.Contains(Target(item.card))).Select(item => item.targetIndex).ToImmutableArray());
            }
        }
        foreach (var (step, index) in actions.Select((step, index) => (step, index)).Where(item => detailed &&
            item.step.Action == PredictorRestAction.Heal && c.Node.PendingCauses.Any(p => p.Code == "RestMaximumHp")))
        {
            var health = Health(c, "rest-max-" + index);
            parts.Numbers.Add(new(Field(c, (_, value) => { if (!Integer(value.Number, out int amount)) return false; health.Maximum = amount; return true; }),
                R("maximum_hp"), health.Maximum, 0, int.MaxValue));
            parts.Sections.Add(new(R("local_health"), "", [], [Choice(c, R("confirm"), context => Set(context,
                context.Plan with { RestActions = context.Plan.RestActions.SetItem(index, step with { MaximumHp = health.Maximum }) }), enabled: health.Maximum > 0)]));
        }
        if (pending == null)
        {
            var availability = new List<InRunMapDetailSection>();
            bool healRewards = c.Node.Inventory?.Relics.Any(relic => !relic.IsKnown || !relic.Value.Melted &&
                relic.Value.Key.Entry is "DREAM_CATCHER" or "TINY_MAILBOX") == true;
            var mainOffers = c.Node.RestOffers.Where(offer =>
                offer.Action is PredictorRestAction.Dig or PredictorRestAction.Hatch ||
                offer.Action == PredictorRestAction.Heal && healRewards);
            var offers = detailed ? c.Node.RestOffers.AsEnumerable() : mainOffers;
            if (premisesOnly) offers = offers.Where(offer => !mainOffers.Any(main => main.Action == offer.Action));
            var choices = offers.Select(offer =>
            {
                if (!premisesOnly) return RestOfferChoice(c, offer, availability);
                var reasons = new List<InRunMapDetailSection>();
                var choice = RestOfferChoice(c, offer, reasons);
                if (choice.Enabled) availability.AddRange(reasons);
                return choice;
            }).Where(choice => !premisesOnly || choice.Enabled).ToArray();
            if (choices.Length != 0) parts.Sections.Add(new(R("plan.room.RestSite"), detailed && c.Node.RestHeal is { } heal ? heal.OverrideHealAmount is { } flat ?
                _text.Format("predictor.runtime.plan.heal_fixed", flat, heal.MaxHpDelta) :
                _text.Format("predictor.runtime.plan.heal", heal.BaseMaxHpFraction * 100, heal.FlatHealBonus, heal.MaxHpDelta) : "", [],
                choices) { BranchRow = !detailed, StableId = "rest-options" });
            parts.Sections.AddRange(availability);
        }
        if (detailed && !premisesOnly) parts.Sections.Add(new("", "", [], [Choice(c, R("plan.leave_rest"), context => Set(context, context.Plan with { LeaveRest = true }),
            enabled: pending == null, selected: c.Plan.LeaveRest)]));
    }
    private static IEnumerable<PredictorCard> RestTargets(Context c) => c.Node.RestCards
        .Where(card => card.IsKnown && card.Value.Id > 0 && !card.Value.Removed).Select(card => card.Value);

    private bool AddShop(Context c, PredictorPlannedShopAction action)
        => Set(c, ShopActionPlan(c, action));
    private static PredictorPlannedNode ShopActionPlan(Context c, PredictorPlannedShopAction action)
    {
        var prefix = EditableShopActions(c).Take(c.Node.Shop!.AppliedActions).ToImmutableArray();
        if (prefix.LastOrDefault()?.Action == PredictorShopActionKind.Leave) prefix = prefix.RemoveAt(prefix.Length - 1);
        return c.Plan with { Shop = new() { Actions = prefix.Add(action) } };
    }
    private static ImmutableArray<PredictorPlannedShopAction> EditableShopActions(Context c)
    {
        var authored = c.Plan.Shop?.Actions ?? [];
        var resolved = c.Node.Shop?.ResolvedActions ?? [];
        int applied = Math.Min(c.Node.Shop?.AppliedActions ?? 0, authored.Length);
        // A successful replay can resolve old global-revision snapshots to
        // their actual stock occurrence. Use that evidence only when the user
        // edits; simply viewing/recomputing never rewrites saved intent.
        return resolved.Length == applied && applied > 0
            ? resolved.AddRange(authored.Skip(applied)) : authored;
    }
    private string ShopGoldBalance(PredictorShopNodeKnowledge shop) => shop.GoldBalance is { } gold
        ? _text.Format("predictor.map.shop_gold_balance", gold) : M("shop_gold_unknown");

    private void ShopEditor(Context c, EditorParts parts, PredictorShopNodeKnowledge shop, bool detailed = true)
    {
        if (detailed) parts.Sections.Add(new("", ShopGoldBalance(shop), [], []));
        ShopPlanEditor(c, parts, detailed);
        // The balance and its source are explained once at node level. An
        // offer keeps only causes specific to its stock/price or operation.
        var goldCauses = PredictorRouteCauseEvidence.OriginalCauses(shop.GoldPendingCauses)
            .Select(cause => cause.Identity).ToHashSet(StringComparer.Ordinal);
        IEnumerable<PredictorCombatPendingCause> OfferCauses(PredictorShopOfferKnowledge offer) =>
            PredictorRouteCauseEvidence.OriginalCauses(offer.PendingCauses).Concat(
                PredictorRouteCauseEvidence.OriginalCauses(offer.AffordabilityPendingCauses)
                    .Where(cause => !goldCauses.Contains(cause.Identity)));
        bool removing = shop.PendingCauses.Any(p => p.Code == "ShopRemovalChoice");
        bool child = !shop.RequiredChoices.IsEmpty;
        var items = new List<InRunMapDetailChoice>();
        foreach (var offer in shop.Offers)
        {
            var item = offer.Value;
            var content = item?.Card is { } card ? Object(card) : item?.Key is { } key ? Object(key) : null;
            string label = item == null ? R("combat_pending") : R("gold") + ": " + item.Cost;
            var pending = PendingLines(OfferCauses(offer)).Distinct().ToArray();
            if (pending.Length > 0) label += "\n" + string.Join("\n", pending);
            var choice = Choice(c,
                offer.Kind == PredictorShopEntryKind.CardRemoval ? R("remove_card") : R("plan.take"), context => AddShop(context,
                    new(offer.Kind == PredictorShopEntryKind.CardRemoval ? PredictorShopActionKind.BeginRemoval : PredictorShopActionKind.Buy) { ExpectedOffer = item }),
                detailed ? null : content, enabled: offer.IsKnown && item!.Stocked && offer.Affordable == true && !removing && !child);
            if (detailed)
                parts.Sections.Add(new("", label, content == null ? [] : [content], [choice]));
            else
            {
                var status = pending.ToList();
                if (item == null && status.Count == 0) status.Add(R("combat_pending"));
                if (item is { Stocked: false }) status.Add(M("shop_sold"));
                else if (offer.Affordable == false) status.Add(M("shop_insufficient_gold"));
                if (removing || child) status.Add(M("shop_child_pending"));
                choice = choice with { Price = item?.Cost, Status = string.Join("\n", status.Distinct()) };
                if (choice.Enabled && offer.Kind != PredictorShopEntryKind.CardRemoval && item != null)
                    choice = choice with { PreviewFactory = () => ShopPurchasePreview(c, item) };
                items.Add(choice);
            }
        }
        if (!detailed && items.Count > 0)
            parts.Sections.Add(new("", "", [], items) { ItemFlow = true });
        if (!detailed && RewardObtainParent(c.Node.Rewards) == null)
            foreach (var required in shop.RequiredChoices) MainObtainEditor(c, parts, required);
        if (removing)
        {
            var candidates = c.Node.Cards.Where(card => card.IsKnown && card.Value.Id > 0 && card.Value.Removable).Select(card => card.Value);
            parts.Sections.Add(new(R("remove_card"), "", [], candidates.Select(card => Choice(c, R("remove_card"), context =>
                AddShop(context, new(PredictorShopActionKind.ChooseRemoval) { Card = Target(card) }), Object(card)))
                .Append(Choice(c, R("cancel"), context => AddShop(context, new(PredictorShopActionKind.CancelRemoval)))).ToArray()));
        }
    }
    private void ShopPlanEditor(Context c, EditorParts parts, bool detailed = true)
    {
        // A purchase/removal and its required child choices move together.
        // The implicit end of shopping is not an authored action or edit lock.
        var groups = new List<ImmutableArray<PredictorPlannedShopAction>>();
        foreach (var action in EditableShopActions(c))
        {
            if (action.Action == PredictorShopActionKind.Leave) continue;
            if (groups.Count == 0 || action.Action is PredictorShopActionKind.Buy or PredictorShopActionKind.BeginRemoval)
                groups.Add([]);
            groups[^1] = groups[^1].Add(action);
        }
        for (int index = 0; index < groups.Count; index++)
        {
            int step = index;
            var first = groups[index][0];
            var item = first.ExpectedOffer?.Card is { } card ? Object(card) :
                first.ExpectedOffer?.Key is { } key ? Object(key) : null;
            bool Edit(Context context, int move, bool remove = false)
            {
                var edited = groups.ToList();
                var group = edited[step]; edited.RemoveAt(step);
                if (!remove) edited.Insert(step + move, group);
                return Set(context, context.Plan with { Shop = new() { Actions = edited.SelectMany(actions => actions).ToImmutableArray() } });
            }
            parts.Sections.Add(new(index == 0 ? M("shop_plan") : "",
                _text.Format("predictor.map.shop_step", index + 1) +
                    (first.Action == PredictorShopActionKind.BeginRemoval ? " · " + R("remove_card") : ""),
                item == null ? [] : [item],
                [Choice(c, M("move_earlier"), context => Edit(context, -1), enabled: step > 0),
                 Choice(c, M("move_later"), context => Edit(context, 1), enabled: step + 1 < groups.Count),
                 Choice(c, M("remove_shop_step"), context => Edit(context, 0, remove: true))]) { RewardActionsRow = detailed, PlannedActionsRow = !detailed });
        }
    }
    private void TreasureEditor(Context c, EditorParts parts, PredictorTreasureNodeKnowledge treasure, bool detailed = true)
    {
        var choices = new List<InRunMapDetailChoice>();
        if (treasure.OfferedRelic is { } relic && treasure.RelicPendingCauses.IsEmpty)
            choices.Add(Choice(c, R("plan.take"), context => Set(context, context.Plan with {
                Treasure = new() { Actions = [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = relic }] } }), Object(relic),
                selected: c.Plan.Treasure == null && treasure.RelicTaken ||
                    c.Plan.Treasure?.Actions.Any(a => a.Action == PredictorTreasureActionKind.TakeRelic && a.ExpectedRelic == relic) == true));
        choices.Add(Choice(c, R("plan.skip"), context => Set(context, context.Plan with {
            // Opening retains the chest's gold and quest consequences.
            Treasure = new() { Actions = [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.Leave)] } }),
            selected: c.Plan.Treasure?.Actions.LastOrDefault()?.Action == PredictorTreasureActionKind.Leave &&
                !c.Plan.Treasure.Actions.Any(a => a.Action == PredictorTreasureActionKind.TakeRelic)));
        string gold = treasure.NormalGold is { } normal ? R("gold") + ": " + normal : R("combat_pending");
        if (treasure.SpoilsGold is > 0) gold += " + " + treasure.SpoilsGold;
        parts.Sections.Add(new(R("plan.room.Treasure"), gold, [], choices) { BranchRow = !detailed });
        if (!detailed && RewardObtainParent(c.Node.Rewards) == null)
            foreach (var required in treasure.RequiredChoices) MainObtainEditor(c, parts, required);
    }
    private void AncientEditor(Context c, EditorParts parts, PredictorAncientNodeKnowledge ancient, bool detailed = true, bool premisesOnly = false)
    {
        string CardName(PredictorCard card) => _names.Resolve(card.Key, GameContentKind.Card) +
            (card.UpgradeLevel > 0 ? "+" + (card.UpgradeLevel > 1 ? card.UpgradeLevel.ToString() : "") : "");
        foreach (var offer in premisesOnly ? [] : ancient.Offers)
        {
            var value = offer.Value;
            var details = new List<string>();
            if (value?.PreparedCard is { } prepared)
                details.Add(M("ancient_card") + ": " + _names.Resolve(prepared, GameContentKind.Card));
            if (value?.CharacterTarget is { } character)
                details.Add(M("ancient_card_character") + ": " + _names.Resolve(character, GameContentKind.Character));
            if (offer.StarterCard is { } card)
                details.Add(M("ancient_transform_target") + ": " + CardName(card));
            if (offer.ReplacementCard is { } replacement)
                details.Add(M("ancient_replacement_result") + ": " + CardName(replacement));
            if (offer.StarterRelic is { } relic)
                details.Add(M("ancient_relic_target") + ": " + _names.Resolve(relic, GameContentKind.Relic));
            if (offer.ReplacementRelic is { } replacementRelic)
                details.Add(M("ancient_replacement_result") + ": " + _names.Resolve(replacementRelic, GameContentKind.Relic));
            details.AddRange(PendingLines(offer.PendingCauses));
            details.AddRange(PendingLines(offer.ReplacementPendingCauses));
            bool locked = value is { Selectable: false } && !value.Relic.IsValid;
            var choose = Choice(c, locked ? M("ancient_locked") : R("plan.take"), context => Set(context, context.Plan with
                { Ancient = !detailed && context.Plan.Ancient?.ExpectedOffer == value ? null : new(ancient.Ancient, value!) }),
                value == null || locked ? null : Object(value.Relic), enabled: offer.IsKnown && value!.Selectable,
                selected: c.Plan.Ancient?.ExpectedOffer == value);
            parts.Sections.Add(new("", string.Join("\n", details), [], []) { HeaderChoice = choose });
        }
        if (!detailed && ancient.Outcome is { } outcome)
        {
            var added = outcome.AddedCards.ToList();
            // The retained picker owns each selected candidate once. Extra
            // acquisitions from real copy hooks remain actual result rows.
            if (ancient.BundleChoice is { SelectedBundle: { } selectedBundle } retained)
                foreach (var selectedCard in retained.Choice.Bundles[selectedBundle])
                {
                    int index = added.IndexOf(selectedCard);
                    if (index >= 0) added.RemoveAt(index);
                }
            var objects = added.Select(Object).Concat(outcome.ObtainedRelics.Select(Object)).ToArray();
            if (objects.Length != 0)
                parts.Sections.Add(new(M("ancient_result"), "", objects, [])
                { StableId = "ancient-acquisition-result", LargeCardResults = true });
            foreach (var change in outcome.ChangedCards)
                parts.Sections.Add(new(M("ancient_change." + change.Action),
                    CardName(change.Before) + (change.After == null ? "" : " → " + CardName(change.After)),
                    change.After == null ? [Object(change.Before)] : [Object(change.Before), Object(change.After)], [])
                { StableId = "ancient-change-" + change.Action, CardColumns = 2 });
            if (!outcome.RemovedRelics.IsEmpty)
                parts.Sections.Add(new(M("ancient_change.Remove"), "", outcome.RemovedRelics.Select(Object).ToArray(), [])
                { StableId = "ancient-removed-relics" });
            foreach (var potion in outcome.Potions)
                parts.Sections.Add(new(M(potion.Result switch
                {
                    PredictorInputResult.Accepted => "ancient_potion_accepted",
                    PredictorInputResult.TooFull => "ancient_potion_full",
                    PredictorInputResult.NotAllowed => "ancient_potion_blocked",
                    _ => "ancient_potion_generated"
                }), "", [Object(potion.Key)], []) { StableId = "ancient-generated-potion" });
        }
        if (!premisesOnly && ancient.BundleChoice is { } bundle)
        {
            bool SaveBundle(Context context, int? index)
            {
                var parent = context.Plan.Ancient ?? new PredictorPlannedAncient(ancient.Ancient, bundle.AcceptedOffer);
                var children = parent.Children;
                if (index is { } selected)
                {
                    var child = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseBundle, bundle.Choice.Source)
                    { ExpectedBundles = bundle.Choice.Bundles, BundleIndex = selected };
                    children = bundle.ChildIndex < children.Length ? children.SetItem(bundle.ChildIndex, child) : children.Add(child);
                }
                else if (bundle.ChildIndex < children.Length)
                    children = children.RemoveAt(bundle.ChildIndex);
                return Set(context, context.Plan with { Ancient = parent with { Children = children } });
            }
            for (int index = 0; index < bundle.Choice.Bundles.Length; index++)
            {
                int selected = index;
                parts.Sections.Add(new(_text.Format("predictor.map.bundle", index + 1), "",
                    detailed ? [] : bundle.Choice.Bundles[index].Select(Object).ToArray(),
                    [Choice(c, _text.Format("predictor.map.bundle", index + 1), context => SaveBundle(context, selected),
                        enabled: bundle.Choice.PendingCauses.IsEmpty, selected: bundle.SelectedBundle == index)])
                { StableId = "ancient-bundle-" + index, LargeCardResults = !detailed });
            }
            if (bundle.SelectedBundle != null)
                parts.Sections.Add(new("", "", [], [Choice(c, M("clear_optional_input"), context => SaveBundle(context, null))])
                { StableId = "ancient-bundle-clear" });
        }
        if (detailed && ancient.OptionalCards is { } optional)
        {
            if (optional.DefaultSkipped) parts.Sections.Add(new("", R("default_skip"), [], []));
            if (!optional.Choice.PendingCauses.IsEmpty)
                parts.Sections.Add(new("", string.Join("\n", PendingLines(optional.Choice.PendingCauses)), [], []));
            bool deckTargets = optional.Choice.Phase == PredictorPhase.ObtainDeckCards;
            var cards = deckTargets ? optional.Choice.DeckCards.Where(card => card.IsKnown).Select(card => card.Value).ToImmutableArray()
                : optional.Choice.Cards;
            bool Save(Context context, ImmutableArray<int> indices)
            {
                var parent = context.Plan.Ancient ?? new PredictorPlannedAncient(ancient.Ancient, optional.AcceptedOffer);
                var action = optional.Choice.Phase switch
                {
                    PredictorPhase.ObtainChooseCard => PredictorObtainChildAction.ChooseCard,
                    PredictorPhase.ObtainChooseManyCards => PredictorObtainChildAction.ChooseManyCards,
                    _ => PredictorObtainChildAction.ChooseDeckCards
                };
                var child = deckTargets ? new PredictorPlannedObtainChild(action, optional.Choice.Source)
                    { DeckCards = indices.Select(index => Target(cards[index])).ToImmutableArray() } :
                    new(action, optional.Choice.Source) { ExpectedCards = cards, CardIndices = indices };
                var children = parent.Children;
                return Set(context, context.Plan with { Ancient = parent with { Children = optional.ChildIndex < children.Length
                    ? children.SetItem(optional.ChildIndex, child) : children.Add(child) } });
            }
            if (optional.Choice.Phase == PredictorPhase.ObtainChooseCard)
                parts.Sections.Add(new(M("select_one_card"), "", [], cards.Select((card, index) =>
                {
                    bool selected = optional.SelectedIndices.Contains(index);
                    return Choice(c, R(selected ? "cancel" : "plan.take"), context => Save(context, selected ? [] : [index]),
                        Object(card), selected: selected);
                }).ToArray()));
            else
                CardSelection(c, parts, "ancient-optional-cards-" + optional.ChildIndex, cards,
                    optional.Choice.MinimumCards, optional.Choice.MaximumCards, Save, selectedIndices: optional.SelectedIndices);
        }
        if (!detailed)
            foreach (var child in ancient.RequiredChoices.Where(child => ancient.BundleChoice == null || child.Phase != PredictorPhase.ChooseBundle))
                MainObtainEditor(c, parts, child);
    }
}
