using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.InRunOperations;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private bool EventOptionSection(Context c, EditorParts parts, PredictorEventNodeKnowledge node,
        PredictorEventOptionKnowledge option, bool initial, bool detailed = true, bool showHeader = true)
    {
        var objects = new List<InRunMapDetailObject>();
        var lines = EventOptionTransactionLines(option).ToList();
        bool selected = EventOptionSelected(c, node, option, initial);
        if (option.Value?.LocalPotionKey is { } localPotion && node.Event?.Entry is "STONE_OF_ALL_TIME" or "RANWID_THE_ELDER")
            objects.Add(Object(localPotion));
        if (option.ResultPreview is { } result)
        {
            if (detailed && !(selected && node.AppliedResult != null))
            {
                var direct = InRunMapOptionPreview.WithoutTakenRewardAcquisitions(result, result.Rewards);
                objects.AddRange(direct.GeneratedCards.Where(card => card.IsKnown).Select(card => Object(card.Value)));
                objects.AddRange(direct.ObtainedRelics.Select(relic => Object(relic.Key)));
            }
            // Fixed HP settlement belongs to diagnostics. The option's actual
            // gold payload above remains visible, including generated prices.
            if (detailed) lines.AddRange(EventLocalPendingLines(result.LocalPendingCauses));
        }
        if (option.Enabled != true) lines.AddRange(EventLocalPendingLines(option.AvailabilityPendingCauses));
        bool enabled = option.Value != null && option.Enabled != false && option.IdentityPendingCauses.IsEmpty;
        var choose = Choice(c, EventOptionLabel(c, node, option), context => SetEventOption(context, node, option, initial),
            enabled: enabled, selected: selected);
        choose = choose with { Preview = EventOptionHover(c, node, option) };
        if (showHeader || lines.Count != 0 || objects.Count != 0)
            parts.Sections.Add(new("", string.Join("\n", lines), objects, [])
            { HeaderChoice = showHeader ? choose : null, CardColumns = 4 });
        if (option.ResultPreview is not { } preview) return false;

        // In the ordinary surface the option owns a read-only nested preview.
        // Only the adopted option exposes its actual required child controls;
        // the complete branch editing surface remains available in the editor.
        if (!detailed && !selected) return false;

        // The immediate live child can refine the entry projection, but later
        // authored steps must retain their own sequence and current input.
        bool currentChild = initial && selected && node.AppliedActions ==
            c.Plan.Event!.Actions.FindIndex(action => action.Action == PredictorEventActionKind.Option) + 1;
        var acceptedChild = EventAcceptedOptionChild(c, node, option, initial);
        if (selected && !node.Rewards.IsEmpty)
        {
            // The ordinary node surface owns these actual reward controls.
            // Premises retain branch inputs and alternate branches, without
            // publishing a second copy of the adopted branch's live rewards.
            if (!detailed) parts.Sections.AddRange(RewardSections(c, node.Rewards, RewardWriter(c)));
        }
        // Hypothetical combat offers stay in branch hover, including when a
        // different branch is adopted with the premises panel already open.
        // Only the current child may publish resident combat rewards.
        else if (preview.Combat == null)
            EventOptionRewardSections(c, parts, node, option, initial, preview, enabled, detailed);
        if (preview.NeededInput == PredictorEventNeededInput.Cards)
        {
            // The branch can already continue independently. Keep optional
            // concrete targets in the detailed editor while ordinary option
            // hover still exposes their conditional results.
            if (!detailed && node.Advanced && !node.InputPolicy.SpecificCardTargetRequired)
                return currentChild && node.NeededInput == PredictorEventNeededInput.Cards;
            var offered = currentChild && node.NeededInput == PredictorEventNeededInput.Cards ? node.OfferedCards : preview.OfferedCards;
            var candidates = currentChild && node.NeededInput == PredictorEventNeededInput.Cards ? node.DeckCardCandidates : preview.DeckCardCandidates;
            int required = currentChild && node.NeededInput == PredictorEventNeededInput.Cards ? node.CardsRequired : preview.CardsRequired;
            EventInlineCards(c, parts, "event-option-" + initial + "-" + option.Key, offered, candidates, required,
                (context, action) => SetEventOption(context, node, option, initial, action), enabled, acceptedChild);
            return currentChild && node.NeededInput == PredictorEventNeededInput.Cards;
        }
        if (preview.NeededInput == PredictorEventNeededInput.Obtain)
        {
            var children = currentChild && node.NeededInput == PredictorEventNeededInput.Obtain ? node.RequiredChoices : preview.RequiredChoices;
            bool handled = false;
            foreach (var child in children)
                handled |= EventInlineObtain(c, parts, "event-option-" + initial + "-" + option.Key, child,
                    (context, action) => SetEventOption(context, node, option, initial, action), enabled, acceptedChild?.Obtain);
            return handled && currentChild && node.NeededInput == PredictorEventNeededInput.Obtain;
        }
        return false;
    }

    private static bool EventOptionSelected(Context c, PredictorEventNodeKnowledge node,
        PredictorEventOptionKnowledge option, bool initial) => initial && EventPlanApplies(c, node) &&
        option.Value is { } value && c.Plan.Event!.Actions.Take(node.AppliedActions)
            .FirstOrDefault(action => action.Action == PredictorEventActionKind.Option)?.ExpectedOption == value;

    private static IEnumerable<PredictorEventOptionKnowledge> SelectedEventBranchPath(Context c,
        PredictorEventNodeKnowledge node, PredictorEventOptionKnowledge option)
    {
        if (option.ResultPreview == null) yield break;
        yield return option;
        int parent = c.Plan.Event!.Actions.FindIndex(action => action.Action == PredictorEventActionKind.Option);
        // Consecutive option pages use the producer's actual nested branches.
        // An intervening card/health/obtain operation requires a new boundary;
        // never apply an independent pre-operation preview across that input.
        for (int index = parent + 1; index < node.AppliedActions && index < c.Plan.Event.Actions.Length; index++)
        {
            var action = c.Plan.Event.Actions[index];
            if (action.Action != PredictorEventActionKind.Option) yield break;
            var child = option.ResultPreview!.Options.FirstOrDefault(candidate => candidate.Value == action.ExpectedOption);
            if (child?.ResultPreview == null) yield break;
            option = child;
            yield return option;
        }
    }

    private OperationPreview? EventOptionHover(Context c, PredictorEventNodeKnowledge node,
        PredictorEventOptionKnowledge option)
    {
        if (node.Event is not { } key) return null;
        var causes = option.IdentityPendingCauses.Concat(option.AvailabilityPendingCauses)
            .Concat(option.ResultPreview?.LocalPendingCauses ?? []);
        if (option.ResultPreview?.Combat is { } combat)
            // The real child already supplies its results and missing facts.
            // Its coarse declaration gate adds no separate player obligation.
            // Expand first so genuine parent causes keep their original scope.
            causes = PredictorRouteCauseEvidence.OriginalCauses(causes.Concat(combat.PendingCauses))
                .Where(cause => cause.Code != "EventCombatChoice");
        var notices = EventLocalPendingLines(causes).Concat(EventOptionTransactionLines(option)).ToList();
        if (option.ResultPreview?.Combat is { } battle)
        {
            var gold = battle.Rewards.Where(reward => reward.IsKnown && reward.Kind == PredictorRewardKind.Gold)
                .Select(reward => reward.Value!.GoldAmount).ToArray();
            if (gold.Length != 0) notices.Add(_text.Format("predictor.map.combat_gold_known", gold.Sum()));
            if (battle.Rewards.Any(reward => !reward.IsKnown && !reward.CardOptions.IsEmpty))
                notices.Add(M("card_enchantments_unresolved"));
        }
        if (option.Value is { } value)
        {
            if (value.HealthDamage is { } damage) notices.Add(_text.Format("predictor.map.event_hp_damage", damage));
            if (value.HealthHeal is { } heal) notices.Add(_text.Format("predictor.map.event_hp_heal", heal));
            if (value.MaxHpCost is { } maxHp) notices.Add(_text.Format("predictor.map.event_max_hp_cost", maxHp));
            if (value.WillKill == true) notices.Add(M("event_will_kill"));
            if (value.LocalPotionKey is { } potion) notices.Add(_names.Resolve(potion, GameContentKind.Potion));
        }
        var preview = option.ResultPreview is { } result
            ? InRunMapOptionPreview.Build(key, option.Key, result, [], child => string.Join(" · ",
                new[] { EventOptionLabel(c, node, child) }.Concat(EventOptionTransactionLines(child))))
            : new OperationPreview(key, [], [], false, [], []) { OptionKey = option.Key };
        bool pureCards = preview.Branches.SelectMany(branch => branch.Sections).Any(section =>
                section.CardGroups.Any(group => !group.Cards.IsEmpty) || section.Rows.Any(row => !row.Cards.IsEmpty)) &&
            preview.Branches.SelectMany(branch => branch.Sections).All(section => section.Rows.All(row => row.Models.IsEmpty));
        // Explicit D-level card-only layouts keep their frameless treatment.
        // Other branches still have an informative hover when no card exists.
        string? heading = notices.Count != 0 || !pureCards || OperationDisplayPolicy.Resolve(preview).Text != OperationTextMode.None
            ? EventOptionLabel(c, node, option) : null;
        return preview with { Heading = heading, Notices = notices.Distinct().ToImmutableArray(), NoticesAreLocalized = true };
    }

    private IEnumerable<string> EventOptionTransactionLines(PredictorEventOptionKnowledge option)
    {
        if (option.ResultPreview?.AllGoldLost == true)
            yield return M("event_gold_all_lost");
        else
        {
            if (option.Value?.GoldGain is { } gain) yield return _text.Format("predictor.map.gold_gain", gain);
            if (option.Value?.GoldCost is { } cost) yield return _text.Format("predictor.map.gold_cost", cost);
            // The actual branch result fills payloads omitted by old option
            // metadata. Unknown balances never become an estimated amount.
            if (option.Value is { GoldGain: null, GoldCost: null } && option.ResultPreview?.GoldChange is { } change && change != 0)
                yield return _text.Format(change > 0 ? "predictor.map.gold_gain" : "predictor.map.gold_cost", Math.Abs(change));
        }
        if (option.LostRelic is { } relic)
            yield return _text.Format("predictor.map.event_relic_lost", _names.Resolve(relic, GameContentKind.Relic));
    }

    private OperationPreview? EventHistoricalOptionHover(Context c, PredictorEventNodeKnowledge node,
        PredictorEventOptionKnowledge option) => node.Event is { } key
        ? new OperationPreview(key, [], [], false, [], [])
            { OptionKey = option.Key, Heading = EventOptionLabel(c, node, option), Notices = [M("event_stage_history_hover")] }
        : null;

    private void EventOptionRewardSections(Context c, EditorParts parts, PredictorEventNodeKnowledge node,
        PredictorEventOptionKnowledge option, bool initial, PredictorEventOptionResultKnowledge preview, bool enabled, bool detailed)
    {
        for (int index = 0; index < preview.Rewards.Length; index++)
        {
            var knowledge = preview.Rewards[index];
            int occurrence = knowledge.Occurrence >= 0 ? knowledge.Occurrence : preview.Rewards.Take(index)
                .Count(prior => prior.Value is { } value && knowledge.Value is { } reward && SameReward(value, reward));
            bool defaultCardChoice = knowledge.IsKnown && knowledge.DefaultSkipped &&
                knowledge.Value is { Kind: PredictorRewardKind.Card or PredictorRewardKind.SpecialCard, CanSkip: true, Mandatory: false };
            // Only a real reward picker completed by the approved default is editable
            // here. Automatically generated cards above have no reward action.
            var display = knowledge with { Occurrence = occurrence, CompletedEdit = null };
            parts.Sections.AddRange(RewardSections(c, [display], enabled &&
                (preview.NeededInput == PredictorEventNeededInput.Rewards || defaultCardChoice) ?
                    (context, choice) => SetEventOptionReward(context, node, option, initial, knowledge, choice) : null, detailed));
        }
    }

    private bool SetEventOptionReward(Context c, PredictorEventNodeKnowledge node, PredictorEventOptionKnowledge option,
        bool initial, PredictorCombatRewardKnowledge knowledge, PredictorPlannedRewardChoice choice)
    {
        if (EventOptionPlan(c, node, option, initial) is not { Event: { } parent } authored) return false;
        if (knowledge.CompletedEdit is not { } edit)
            return Set(c, authored with { Event = parent with { Actions = parent.Actions.Add(
                new(PredictorEventActionKind.Reward) { RewardChoice = choice }) } });
        var rebased = RebaseEventOptionRewardEdit(authored, option, edit);
        return rebased != null && Set(c, PredictorRun.ApplyCompletedRewardChoice(authored, rebased, choice));
    }

    private static PredictorCompletedRewardEdit? RebaseEventOptionRewardEdit(PredictorPlannedNode authored,
        PredictorEventOptionKnowledge option, PredictorCompletedRewardEdit edit)
    {
        if (authored.Event is not { } parent) return null;
        if (edit.Owner != PredictorRewardEditOwner.Event || edit.Prefix.Event is not { } recorded || recorded.ExpectedEvent != parent.ExpectedEvent)
            return null;
        int recordedParent = recorded.Actions.FindIndex(action => action.Action == PredictorEventActionKind.Option && action.ExpectedOption == option.Value);
        int offset = edit.InsertionIndex - recordedParent - 1;
        if (recordedParent < 0 || offset < 0) return null;
        var prefix = authored with { Event = parent with { Actions = parent.Actions.AddRange(recorded.Actions.Skip(recordedParent + 1)) } };
        // The receipt owns nested reward/source binding. Rebase its synthetic
        // option prefix onto the actual authored prefix (including Conveyor
        // HP or earlier event steps), then save parent and choice together.
        return edit with { Prefix = prefix, InsertionIndex = parent.Actions.Length + offset };
    }

    private static PredictorPlannedEventAction? EventAcceptedOptionChild(Context c, PredictorEventNodeKnowledge node,
        PredictorEventOptionKnowledge option, bool initial)
    {
        if (!EventPlanApplies(c, node) || c.Plan.Event is not { } plan) return null;
        int parent = initial ? plan.Actions.FindIndex(action => action.Action == PredictorEventActionKind.Option) : node.AppliedActions;
        int child = parent + 1;
        return parent >= 0 && child < plan.Actions.Length && node.AppliedActions > child &&
            plan.Actions[parent].ExpectedOption == option.Value ? plan.Actions[child] : null;
    }

    private static ImmutableArray<int> EventSelectedOffers(ImmutableArray<PredictorCard> displayed,
        ImmutableArray<PredictorCard> expected, ImmutableArray<int> indices) =>
        displayed.SequenceEqual(expected) && indices.All(index => index >= 0 && index < displayed.Length) ? indices : default;

    private static ImmutableArray<int> EventSelectedDeckTargets(ImmutableArray<PredictorCard> displayed,
        ImmutableArray<PredictorPlannedCard> targets)
    {
        var indices = displayed.Select((card, index) => (Card: Target(card), Index: index))
            .Where(item => targets.Contains(item.Card)).Select(item => item.Index).ToImmutableArray();
        return indices.Length == targets.Length ? indices : default;
    }

    private bool SetEventOption(Context c, PredictorEventNodeKnowledge node, PredictorEventOptionKnowledge option,
        bool initial, PredictorPlannedEventAction? child = null)
    {
        if (EventOptionPlan(c, node, option, initial) is not { Event: { } plan } authored) return false;
        return Set(c, child == null ? authored : authored with { Event = plan with { Actions = plan.Actions.Add(child) } });
    }

    private static PredictorPlannedNode? EventOptionPlan(Context c, PredictorEventNodeKnowledge node, PredictorEventOptionKnowledge option, bool initial)
    {
        if (node.Event is not { } key || option.Value == null) return null;
        var plan = EventPlanApplies(c, node) ? c.Plan.Event! : new PredictorPlannedEvent(key);
        // Conveyor's entry HP declaration precedes its first option. Preserve
        // that accepted input when a result authors the parent and child.
        var prefix = initial ? plan.Actions.TakeWhile(action => action.Action != PredictorEventActionKind.Option).ToImmutableArray() :
            plan.Actions.Take(node.AppliedActions).ToImmutableArray();
        return c.Plan with { Event = plan with { Actions = prefix.Add(new(PredictorEventActionKind.Option) { ExpectedOption = option.Value }) } };
    }

    private static bool EventPlanApplies(Context context, PredictorEventNodeKnowledge node) =>
        !node.AuthoredActionsNotApplied && context.Plan.Event is { } plan && node.Event == plan.ExpectedEvent;

    private string EventOptionLabel(Context c, PredictorEventNodeKnowledge node, PredictorEventOptionKnowledge option)
    {
        if (node.Event?.Entry == "COLORFUL_PHILOSOPHERS")
            return _names.Resolve(new ModelKey("CHARACTER", option.Key), GameContentKind.Character);
        if ((node.Event?.Entry == "FAKE_MERCHANT" && option.Key.StartsWith("BUY_", StringComparison.Ordinal) ||
            node.Event?.Entry == "DOLL_ROOM" && option.Key.StartsWith("DOLL_", StringComparison.Ordinal)) && option.Value?.OfferedRelic is { } relic)
            return R("plan.take") + " · " + _names.Resolve(relic, GameContentKind.Relic);
        if (node.Event?.Entry == "THE_FUTURE_OF_POTIONS" && option.Key.StartsWith("POTION:", StringComparison.Ordinal))
        {
            var potion = c.Node.PotionBoundaries.SelectMany(boundary => boundary.Before.PotionSlots)
                .Concat(c.Node.Inventory?.PotionSlots ?? []).FirstOrDefault(slot => slot.IsKnown && slot.Value?.Id == option.Value?.PotionId)?.Value;
            string label = option.Value?.LocalPotionKey is { } local ? _names.Resolve(local, GameContentKind.Potion) :
                potion == null ? R("option.POTION") : _names.Resolve(potion.Key, GameContentKind.Potion);
            return option.Value?.CardType is { } type ? label + " · " + R("option." + type.ToString().ToUpperInvariant()) : label;
        }
        return R("option." + option.Key);
    }

    private void EventInlineCards(Context c, EditorParts parts, string role, ImmutableArray<PredictorCard> offered,
        ImmutableArray<PredictorRouteCardKnowledge> candidates, int required,
        Func<Context, PredictorPlannedEventAction, bool> apply, bool enabled, PredictorPlannedEventAction? accepted = null)
    {
        // No accepted child is an observed empty saved selection. In
        // particular, reselecting the parent removes its prior child while
        // retaining the same candidates and picker identity.
        if (!offered.IsEmpty)
            CardSelection(c, parts, role, offered, required, required, (context, indices) => apply(context,
                new(PredictorEventActionKind.Cards) { ExpectedCards = offered, CardIndices = indices }), enabled,
                selectedIndices: accepted?.Action == PredictorEventActionKind.Cards && accepted.CardIndices.Length == required ?
                    EventSelectedOffers(offered, accepted.ExpectedCards, accepted.CardIndices) : [], largeCardResults: true);
        else
        {
            var cards = candidates.Where(card => card.IsKnown && card.Value.Id > 0).Select(card => card.Value).ToImmutableArray();
            CardSelection(c, parts, role, cards, required, required, (context, indices) => apply(context,
                new(PredictorEventActionKind.Cards) { DeckCards = indices.Select(i => Target(cards[i])).ToImmutableArray() }), enabled && !cards.IsEmpty,
                selectedIndices: accepted?.Action == PredictorEventActionKind.Cards && accepted.DeckCards.Length == required ?
                    EventSelectedDeckTargets(cards, accepted.DeckCards) : []);
        }
    }

    private bool EventInlineObtain(Context c, EditorParts parts, string role, PredictorObtainChoiceKnowledge child,
        Func<Context, PredictorPlannedEventAction, bool> apply, bool enabled, PredictorPlannedObtainChild? accepted = null)
    {
        if (accepted?.ExpectedSource != child.Source) accepted = null;
        bool Save(Context context, PredictorPlannedObtainChild choice) => apply(context,
            new(PredictorEventActionKind.Obtain) { Obtain = choice });
        role += "-" + child.Source + "-" + child.Phase;
        switch (child.Phase)
        {
            case PredictorPhase.ObtainChooseCard:
            case PredictorPhase.ObtainChooseManyCards:
                bool one = child.Phase == PredictorPhase.ObtainChooseCard;
                CardSelection(c, parts, role, child.Cards, one ? 0 : child.MinimumCards, one ? 1 : child.MaximumCards,
                    (context, indices) => Save(context, new(one ? PredictorObtainChildAction.ChooseCard : PredictorObtainChildAction.ChooseManyCards, child.Source)
                    { ExpectedCards = child.Cards, CardIndices = indices }), enabled && child.PendingCauses.IsEmpty && !child.Cards.IsEmpty,
                    selectedIndices: accepted?.Action == (one ? PredictorObtainChildAction.ChooseCard : PredictorObtainChildAction.ChooseManyCards) ?
                        EventSelectedOffers(child.Cards, accepted.ExpectedCards, accepted.CardIndices) : [], largeCardResults: true);
                return true;
            case PredictorPhase.ObtainDeckCards:
            case PredictorPhase.TransformCards:
            case PredictorPhase.RewardRemoveCards:
                var cards = child.DeckCards.Where(card => card.IsKnown && card.Value.Id > 0).Select(card => card.Value).ToImmutableArray();
                bool single = child.Phase is PredictorPhase.TransformCards or PredictorPhase.RewardRemoveCards;
                var action = child.Phase == PredictorPhase.TransformCards ? PredictorObtainChildAction.TransformCard :
                    child.Phase == PredictorPhase.RewardRemoveCards ? PredictorObtainChildAction.RemoveRewardCards : PredictorObtainChildAction.ChooseDeckCards;
                CardSelection(c, parts, role, cards, single ? 1 : child.MinimumCards, single ? 1 : child.MaximumCards,
                    (context, indices) => Save(context, new(action, child.Source) { DeckCards = indices.Select(i => Target(cards[i])).ToImmutableArray() }), enabled && !cards.IsEmpty,
                    selectedIndices: accepted?.Action == action ? EventSelectedDeckTargets(cards, accepted.DeckCards) : []);
                return true;
            case PredictorPhase.ChooseBundle:
                for (int index = 0; index < child.Bundles.Length; index++)
                {
                    int selected = index;
                    parts.Sections.Add(new(_text.Format("predictor.map.bundle", index + 1), "", child.Bundles[index].Select(Object).ToArray(),
                        [Choice(c, R("plan.take"), context => Save(context, new(PredictorObtainChildAction.ChooseBundle, child.Source)
                        { ExpectedBundles = child.Bundles, BundleIndex = selected }), enabled: enabled && child.PendingCauses.IsEmpty && child.Bundles.Length == 2,
                        selected: accepted?.Action == PredictorObtainChildAction.ChooseBundle && accepted.BundleIndex == selected &&
                            accepted.ExpectedBundles.Length == child.Bundles.Length && accepted.ExpectedBundles.Zip(child.Bundles).All(pair => pair.First.SequenceEqual(pair.Second)))])
                    { CardColumns = 4, LargeCardResults = true });
                }
                return true;
            default:
                // A health/combat/reward transaction cannot be collapsed into
                // a pretend card choice. Preserve its current-step editor.
                return false;
        }
    }
    private IEnumerable<string> EventLocalPendingLines(IEnumerable<PredictorCombatPendingCause> causes) =>
        PendingLines(causes.Where(cause => cause.Code is not ("EventHealthOutcomePending" or "EventCardsChoice" or
            "EventObtainChoice" or "EventRewardsChoice" or "EventOptionChoice" or "EventTrialAbandonChoice" or "EventCrystalChoice"))).Distinct();
}
