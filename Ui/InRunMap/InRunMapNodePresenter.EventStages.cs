using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    // Only observations live here. Editing rebinds semantic option values to a
    // current Context and replay prefix; callbacks and predicted outcomes expire.
    private sealed record EventStageSnapshot(int ActionIndex,
        ImmutableArray<PredictorPlannedEventAction> Prefix,
        ImmutableArray<PredictorEventOptionKnowledge> Options);
    private sealed record EventDisplayStage(int ActionIndex,
        ImmutableArray<PredictorEventOptionKnowledge> Options, bool CurrentSource, bool MissingHistory = false);
    private readonly Dictionary<(string Seed, string NodeId, ModelKey Event, ChooseEventOccurrence? Occurrence, string Capture, string Context),
        List<EventStageSnapshot>> _eventStageSnapshots = [];

    private void SynchronizeEventStages()
    {
        SynchronizeConveyorDrafts();
        string seed = _session.Snapshot?.Run.Context.Seed ?? "";
        string capture = _session.Snapshot?.Fingerprint ?? "";
        var liveIds = _session.Nodes.Values.Select(value => value.Position.NodeId).ToHashSet();
        foreach (var stale in _eventStageSnapshots.Keys.Where(value => value.Seed != seed || value.Capture != capture ||
            !liveIds.Contains(value.NodeId)).ToArray()) _eventStageSnapshots.Remove(stale);
    }

    private void DiscardEventStages(string nodeId)
    {
        foreach (var key in _conveyorDrafts.Keys.Where(key => key.NodeId == nodeId).ToArray()) _conveyorDrafts.Remove(key);
        foreach (var key in _eventStageSnapshots.Keys.Where(value => value.NodeId == nodeId).ToArray())
            _eventStageSnapshots.Remove(key);
    }

    private bool EventStageRows(Context c, EditorParts parts, PredictorEventNodeKnowledge node, bool premisesOnly = false)
    {
        if (node.Event is not { } key) return false;
        var actions = EventPlanApplies(c, node) ? c.Plan.Event!.Actions : [];
        int applied = Math.Min(node.AppliedActions, actions.Length);
        // Optionality governs continuation, not whether the branch is visible.
        // Unselected acquisitions remain conditional; concrete targets belong
        // only to the selected branch's supplemental inputs.
        if (!premisesOnly && actions.IsEmpty && !node.BusinessChoiceRequired)
        {
            // An independent branch execution is not an adopted result. Keep
            // useful conditional acquisitions, without promoting its optional
            // deck edits (including random upgrade targets) to main results.
            foreach (var option in node.EntryOptions)
            {
                if (option.Enabled == false || option.ResultPreview is not { } preview) continue;
                var conditional = option with { ResultPreview = EventConditionalAcquisitions(preview) };
                if (!HasEventOutcome(conditional.ResultPreview!) && !EventOptionTransactionLines(conditional).Any()) continue;
                var outcome = EventOptionHover(c, node, conditional);
                if (outcome != null) outcome = outcome with
                    { Heading = string.Join(" · ", M("unselected"), M("branch_preview"), EventOptionLabel(c, node, option)) };
                parts.Sections.Add(new("", "", [], [])
                { StableId = c.Node.Position.NodeId + "|event-outcome|" + option.Key, OutcomePreview = outcome, LargeCardResults = true });
            }
        }
        string seed = _session.Snapshot?.Run.Context.Seed ?? "";
        ChooseEventOccurrence? occurrence = node.Occurrence is { } observed && node.SelectedOccurrence is { } selected
            ? new(selected.Event, selected.QueueEvent, selected.RawOrdinal, observed.Cursor, selected.Cursor) : null;
        // Local event actions append without changing their incoming boundary.
        // Captures, other plans, route prefixes and non-action local premises do.
        string contextKey = JsonSerializer.Serialize(new
        {
            OtherPlans = _session.Plans.Where(plan => plan.NodeId != c.Plan.NodeId).ToArray(),
            Local = c.Plan with { Event = (c.Plan.Event ?? new PredictorPlannedEvent(key)) with { Actions = [], ExpectedOccurrence = null } },
            Route = _session.Nodes.Where(pair => pair.Key.ActIndex == c.Location.ActIndex &&
                pair.Value.Position.Floor < c.Node.Position.Floor).OrderBy(pair => pair.Value.Position.Floor)
                .Select(pair => pair.Value.Position.NodeId).ToArray()
        });
        var cacheKey = (seed, c.Node.Position.NodeId, key, occurrence, _session.Snapshot?.Fingerprint ?? "", contextKey);
        SynchronizeEventStages();
        foreach (var stale in _eventStageSnapshots.Keys.Where(value => value.NodeId == cacheKey.Item2 && value != cacheKey).ToArray())
            _eventStageSnapshots.Remove(stale);
        if (!_eventStageSnapshots.TryGetValue(cacheKey, out var snapshots))
            _eventStageSnapshots[cacheKey] = snapshots = [];
        snapshots.RemoveAll(snapshot => snapshot.ActionIndex > applied ||
            !snapshot.Prefix.SequenceEqual(actions.Take(snapshot.ActionIndex)));

        var stages = new List<EventDisplayStage>();
        int first = actions.FindIndex(action => action.Action == PredictorEventActionKind.Option);
        int initialIndex = first >= 0 && first <= applied ? first : applied;
        if (!node.EntryOptions.IsEmpty) stages.Add(new(initialIndex, node.EntryOptions, true));
        PredictorEventOptionKnowledge? priorOption = null;
        int priorIndex = -2;
        for (int index = 0; index < applied; index++)
        {
            if (actions[index].Action != PredictorEventActionKind.Option) continue;
            var stage = stages.FirstOrDefault(value => value.ActionIndex == index);
            if (stage == null)
            {
                var nested = priorIndex + 1 == index ? priorOption?.ResultPreview?.Options ?? [] : [];
                if (nested.Any(option => option.Value == actions[index].ExpectedOption)) stage = new(index, nested, true);
                else if (snapshots.FirstOrDefault(snapshot => snapshot.ActionIndex == index &&
                    snapshot.Options.Any(option => option.Value == actions[index].ExpectedOption)) is { } saved)
                    stage = new(index, saved.Options, false);
                else if (actions[index].ExpectedOption is { } known)
                    stage = new(index, [new(known.Key, known, true, [])], false, MissingHistory: true);
                if (stage != null) stages.Add(stage);
            }
            priorOption = stage?.CurrentSource == true
                ? stage.Options.FirstOrDefault(option => option.Value == actions[index].ExpectedOption) : null;
            priorIndex = index;
        }
        if (node.NeededInput == PredictorEventNeededInput.Option && (applied > 0 || node.EntryOptions.IsEmpty))
        {
            stages.RemoveAll(stage => stage.ActionIndex == applied);
            if (!node.Options.IsEmpty) stages.Add(new(applied, node.Options, true));
        }
        stages.Sort((left, right) => left.ActionIndex.CompareTo(right.ActionIndex));
        foreach (var stage in stages.Where(stage => stage.CurrentSource))
        {
            snapshots.RemoveAll(snapshot => snapshot.ActionIndex == stage.ActionIndex);
            snapshots.Add(new(stage.ActionIndex, actions.Take(stage.ActionIndex).ToImmutableArray(),
                stage.Options.Select(option => option with { ResultPreview = null }).ToImmutableArray()));
        }

        int latest = stages.Where(stage => stage.ActionIndex < applied).Select(stage => stage.ActionIndex).DefaultIfEmpty(-1).Max();
        bool inlineChild = false;
        var rewardsByStage = node.Rewards.GroupBy(reward => EventRewardStage(reward, actions, stages, latest))
            .ToDictionary(group => group.Key, group => group.ToImmutableArray());
        foreach (var stage in stages)
        {
            int actionIndex = stage.ActionIndex;
            var expected = actionIndex < applied ? actions[actionIndex].ExpectedOption : null;
            if (!premisesOnly)
            {
                var choices = stage.Options.Select(option =>
                {
                    var choice = Choice(c, EventOptionLabel(c, node, option),
                        context => SetEventStageOption(context, node, option, actionIndex),
                        enabled: !stage.MissingHistory && option.Value != null && option.Enabled != false && option.IdentityPendingCauses.IsEmpty,
                        selected: expected != null && option.Value == expected);
                    return choice with { PreviewKey = c.Node.Position.NodeId + "|event-stage|" + actionIndex + "|" + option.Key,
                        Preview = stage.CurrentSource ? EventOptionHover(c, node, option) :
                        !stage.MissingHistory ? EventHistoricalOptionHover(c, node, option) : null };
                }).ToArray();
                parts.Sections.Add(new("", stage.MissingHistory ? M("event_stage_history_unavailable") : "", [], choices)
                { BranchRow = true, StableId = c.Node.Position.NodeId + "|event-stage|" + actionIndex });
            }
            var liveRewards = rewardsByStage.Remove(actionIndex, out var rewards) ? rewards : [];
            if (!premisesOnly && !liveRewards.IsEmpty) parts.Sections.AddRange(RewardSections(c, liveRewards, RewardWriter(c)));
            var accepted = actionIndex + 1 < applied ? actions[actionIndex + 1] : null;
            if (!premisesOnly && !stage.CurrentSource && accepted?.Action == PredictorEventActionKind.Cards)
            {
                var selectedKeys = !accepted.ExpectedCards.IsEmpty ? accepted.ExpectedCards.Select(card => card.Key) :
                    accepted.DeckCards.Select(card => card.Key);
                string selectedNames = string.Join(" · ", selectedKeys.Select(card => _names.Resolve(card, GameContentKind.Card)));
                if (selectedNames.Length > 0) parts.Sections.Add(new("", _text.Format("predictor.map.event_stage_selected_cards", selectedNames), [], []));
            }
            if (!stage.CurrentSource || expected == null ||
                stage.Options.FirstOrDefault(option => option.Value == expected) is not { ResultPreview: { } preview } adopted) continue;
            if (!premisesOnly) EventStagePreviewRewards(c, parts, node, adopted, preview, actionIndex, liveRewards);
            bool currentChild = applied == actionIndex + 1;
            bool mainCardInput = !node.Advanced || node.InputPolicy.SpecificCardTargetRequired || accepted != null;
            if (preview.NeededInput == PredictorEventNeededInput.Cards &&
                (accepted?.Action == PredictorEventActionKind.Cards || currentChild &&
                    (premisesOnly || node.NeededInput == PredictorEventNeededInput.Cards)) &&
                (premisesOnly ? !mainCardInput : mainCardInput))
            {
                bool liveCards = currentChild && node.NeededInput == PredictorEventNeededInput.Cards;
                var offered = liveCards ? node.OfferedCards : preview.OfferedCards;
                var candidates = liveCards ? node.DeckCardCandidates : preview.DeckCardCandidates;
                int required = liveCards ? node.CardsRequired : preview.CardsRequired;
                EventInlineCards(c, parts, "event-stage-cards-" + actionIndex, offered, candidates, required,
                    (context, child) => SetEventStageOption(context, node, adopted, actionIndex, child),
                    adopted.Enabled != false && adopted.IdentityPendingCauses.IsEmpty, accepted);
                inlineChild |= currentChild;
            }
            if (!premisesOnly && preview.NeededInput == PredictorEventNeededInput.Obtain &&
                (accepted?.Action == PredictorEventActionKind.Obtain || currentChild && node.NeededInput == PredictorEventNeededInput.Obtain))
            {
                foreach (var child in currentChild ? node.RequiredChoices : preview.RequiredChoices)
                    inlineChild |= EventInlineObtain(c, parts, "event-stage-obtain-" + actionIndex, child,
                        (context, choice) => SetEventStageOption(context, node, adopted, actionIndex, choice),
                        adopted.Enabled != false && adopted.IdentityPendingCauses.IsEmpty, accepted?.Obtain) && currentChild;
            }
        }
        // Unbound observations remain real opportunities, without assigning a
        // fabricated historical owner or repeating them under every page.
        if (!premisesOnly)
        {
            foreach (var rewards in rewardsByStage.Values)
                parts.Sections.AddRange(RewardSections(c, rewards, RewardWriter(c)));
            EventAppliedResultSection(c, parts, node);
        }
        return inlineChild;
    }

    private static bool HasEventOutcome(PredictorEventOptionResultKnowledge result) =>
        !result.Rewards.IsEmpty || !result.GeneratedCards.IsEmpty || !result.ObtainedRelics.IsEmpty ||
        !result.OfferedCards.IsEmpty || !result.ChangedCards.IsEmpty ||
        result.RequiredChoices.Any(child => !child.Cards.IsEmpty || !child.Bundles.IsEmpty || !child.Rewards.IsEmpty) ||
        result.Options.Any(option => option.Enabled != false && option.ResultPreview is { } child && HasEventOutcome(child));

    private static PredictorEventOptionResultKnowledge EventConditionalAcquisitions(PredictorEventOptionResultKnowledge result) =>
        result with
        {
            ChangedCards = [],
            // Transform outputs are another observation of the deck edit, not
            // an additional reward. Real direct additions and offers survive.
            GeneratedCards = result.GeneratedCards.Where(card => !result.ChangedCards.Any(change =>
                change.Action == "Transform" && change.After == card.Value)).ToImmutableArray(),
            Options = result.Options.Select(option => option.ResultPreview is { } child
                ? option with { ResultPreview = EventConditionalAcquisitions(child) } : option).ToImmutableArray()
        };

    private void EventAppliedResultSection(Context c, EditorParts parts, PredictorEventNodeKnowledge node)
    {
        if (node.Event is not { } key || node.AppliedResult is not { } applied) return;
        // The frozen actual boundary owns only its acquisitions. The real
        // receipts at that same boundary own the overlapping reward display;
        // later combat rewards must not subtract an unrelated earlier item.
        var direct = InRunMapOptionPreview.WithoutTakenRewardAcquisitions(applied, applied.Rewards) with { Rewards = [] };
        var notices = new List<string>();
        if (direct.AllGoldLost) notices.Add(M("event_gold_all_lost"));
        else if (direct.GoldChange is { } change)
        {
            int rewardGold = applied.Rewards.Where(reward => reward.IsKnown &&
                    reward.Value is { Kind: PredictorRewardKind.Gold, Status: PredictorRewardStatus.Taken })
                .Sum(reward => reward.Value!.GoldAmount);
            change -= rewardGold;
            if (change != 0) notices.Add(_text.Format(change > 0 ? "predictor.map.gold_gain" : "predictor.map.gold_cost", Math.Abs(change)));
        }
        notices.AddRange(direct.LostRelics.Select(relic => _text.Format("predictor.map.event_relic_lost", _names.Resolve(relic, GameContentKind.Relic))));
        notices.AddRange(EventLocalPendingLines(direct.LocalPendingCauses));
        if (!HasEventOutcome(direct) && notices.Count == 0) return;
        parts.Sections.Add(new("", "", [], [])
        {
            StableId = c.Node.Position.NodeId + "|event-applied-outcome",
            OutcomePreview = InRunMapOptionPreview.Build(key, "APPLIED", direct, notices.Distinct().ToImmutableArray()),
            LargeCardResults = true
        });
    }

    private static int EventRewardStage(PredictorCombatRewardKnowledge reward,
        ImmutableArray<PredictorPlannedEventAction> actions, List<EventDisplayStage> stages, int latest)
    {
        int boundary = reward.CompletedEdit is { Owner: PredictorRewardEditOwner.Event } receipt ? receipt.InsertionIndex : -1;
        if (boundary < 0 && reward.AppliedChoice is { } selected)
            boundary = actions.FindIndex(action => SameRewardChoice(action.RewardChoice ?? action.Obtain?.Reward, selected));
        if (boundary >= 0)
            return stages.Where(stage => stage.ActionIndex < boundary).Select(stage => stage.ActionIndex).DefaultIfEmpty(latest).Max();
        if (reward.Value is { } value)
            foreach (var stage in stages.Where(stage => stage.ActionIndex <= latest && stage.CurrentSource))
                if (stage.Options.Where(option => option.Value == actions[stage.ActionIndex].ExpectedOption)
                    .Any(option => option.ResultPreview?.Rewards.Any(candidate => candidate.Value is { } other &&
                    SameReward(value, other) && (reward.Occurrence < 0 || candidate.Occurrence == reward.Occurrence)) == true))
                    return stage.ActionIndex;
        return latest;
    }

    private bool SetEventStageOption(Context c, PredictorEventNodeKnowledge node,
        PredictorEventOptionKnowledge option, int actionIndex, PredictorPlannedEventAction? child = null)
    {
        // Re-clicking the adopted branch removes that assumption and its
        // dependent suffix. No separate premise panel is needed just to reset it.
        if (child == null && EventPlanApplies(c, node) &&
            c.Plan.Event!.Actions.ElementAtOrDefault(actionIndex) is { Action: PredictorEventActionKind.Option } selected &&
            selected.ExpectedOption == option.Value)
            return Set(c, c.Plan with { Event = c.Plan.Event with { Actions = c.Plan.Event.Actions.Take(actionIndex).ToImmutableArray() } });
        if (EventStageOptionPlan(c, node, option, actionIndex) is not { Event: { } plan } authored) return false;
        return Set(c, child == null ? authored : authored with { Event = plan with { Actions = plan.Actions.Add(child) } });
    }

    private static PredictorPlannedNode? EventStageOptionPlan(Context c, PredictorEventNodeKnowledge node,
        PredictorEventOptionKnowledge option, int actionIndex)
    {
        if (node.Event is not { } key || option.Value == null || actionIndex < 0 || actionIndex > node.AppliedActions) return null;
        var plan = EventPlanApplies(c, node) ? c.Plan.Event! : new PredictorPlannedEvent(key);
        if (actionIndex > plan.Actions.Length) return null;
        var actions = plan.Actions.Take(actionIndex).ToImmutableArray()
            .Add(new(PredictorEventActionKind.Option) { ExpectedOption = option.Value });
        return c.Plan with { Event = plan with { Actions = actions } };
    }

    private void EventStagePreviewRewards(Context c, EditorParts parts, PredictorEventNodeKnowledge node,
        PredictorEventOptionKnowledge option, PredictorEventOptionResultKnowledge preview, int actionIndex,
        ImmutableArray<PredictorCombatRewardKnowledge> liveStageRewards)
    {
        // A branch's hypothetical combat offer belongs to its hover. After
        // adoption the current combat (or its completed event receipts) owns
        // the results; an entry projection must not become a second offer or
        // resurrect the pre-combat result after later inputs.
        if (preview.Combat != null) return;
        if (EventStageOptionPlan(c, node, option, actionIndex) is not { } authored) return;
        var stageContext = c with { Plan = authored };
        for (int index = 0; index < preview.Rewards.Length; index++)
        {
            var knowledge = preview.Rewards[index];
            int occurrence = knowledge.Occurrence >= 0 ? knowledge.Occurrence : preview.Rewards.Take(index)
                .Count(prior => prior.Value is { } value && knowledge.Value is { } reward && SameReward(value, reward));
            bool liveMatch = node.Rewards.Select((live, ordinal) => (live, ordinal)).Any(item =>
            {
                if (!liveStageRewards.Contains(item.live)) return false;
                int liveOccurrence = item.live.Occurrence >= 0 ? item.live.Occurrence : node.Rewards.Take(item.ordinal)
                    .Count(prior => prior.Value is { } value && item.live.Value is { } reward && SameReward(value, reward));
                return item.live.Kind == knowledge.Kind && liveOccurrence == occurrence &&
                    (item.live.Value == null || knowledge.Value == null || SameReward(item.live.Value, knowledge.Value));
            });
            if (liveMatch) continue;
            bool defaultCardChoice = knowledge.IsKnown && knowledge.DefaultSkipped &&
                knowledge.Value is { Kind: PredictorRewardKind.Card or PredictorRewardKind.SpecialCard, CanSkip: true, Mandatory: false };
            bool editable = option.Enabled != false && option.IdentityPendingCauses.IsEmpty &&
                (preview.NeededInput == PredictorEventNeededInput.Rewards || defaultCardChoice);
            var rebased = knowledge.CompletedEdit is { } edit ? RebaseEventOptionRewardEdit(authored, option, edit) : null;
            if (knowledge.CompletedEdit != null && rebased == null) editable = false;
            var display = knowledge with { Occurrence = occurrence, CompletedEdit = editable ? rebased : null };
            // The same authored stage prefix owns hover and click. Nested
            // receipts keep their existing source/parent binding after rebasing.
            parts.Sections.AddRange(RewardSections(stageContext, [display], editable ?
                (context, choice) =>
                {
                    var parent = context.Plan.Event!;
                    return Set(context, context.Plan with { Event = parent with
                        { Actions = parent.Actions.Add(new(PredictorEventActionKind.Reward) { RewardChoice = choice }) } });
                } : null));
        }
    }

    private bool AddEvent(Context c, PredictorPlannedEventAction action)
    {
        var key = c.Node.Event?.Event;
        if (key == null) return false;
        var plan = EventPlanApplies(c, c.Node.Event!) ? c.Plan.Event! : new PredictorPlannedEvent(key.Value);
        var prefix = plan.Actions.Take(c.Node.Event!.AppliedActions).ToImmutableArray();
        return Set(c, c.Plan with { Event = plan with { Actions = prefix.Add(action) } });
    }
    private bool EventEditor(Context c, EditorParts parts, PredictorEventNodeKnowledge node, bool detailed = true)
    {
        EventPotionInputs(c, parts, node);
        EventOccurrenceEditor(c, parts, node);
        bool inlineChild = false;
        if (detailed)
            foreach (var option in node.EntryOptions)
                inlineChild |= EventOptionSection(c, parts, node, option, initial: true, detailed);
        else inlineChild = EventStageRows(c, parts, node);
        if (detailed && node.NeededInput == PredictorEventNeededInput.Option && (node.AppliedActions > 0 || node.EntryOptions.IsEmpty))
        {
            foreach (var option in node.Options)
                EventOptionSection(c, parts, node, option, initial: false, detailed);
        }
        string? selectedOption = EventPlanApplies(c, node) && c.Plan.Event is { } activePlan
            ? activePlan.Actions.Take(node.AppliedActions).FirstOrDefault(action => action.Action == PredictorEventActionKind.Option)?.ExpectedOption?.Key : null;
        if (detailed && !node.Rewards.IsEmpty && !node.EntryOptions.Any(option => option.Value?.Key == selectedOption && option.ResultPreview != null))
            parts.Sections.AddRange(RewardSections(c, node.Rewards, RewardWriter(c)));
        if (!inlineChild && node.NeededInput == PredictorEventNeededInput.Cards &&
            (detailed || !node.Advanced || node.InputPolicy.SpecificCardTargetRequired))
        {
            EventInlineCards(c, parts, "event-cards-" + node.AppliedActions, node.OfferedCards, node.DeckCardCandidates,
                node.CardsRequired, AddEvent, node.OfferedCardPendingCauses.IsEmpty);
        }
        if (!inlineChild && node.NeededInput == PredictorEventNeededInput.Obtain)
            foreach (var child in node.RequiredChoices) ObtainEditor(c, parts, child);
        if (node.Event?.Entry != "ENDLESS_CONVEYOR" && node.NeededInput == PredictorEventNeededInput.Health && (detailed || !node.Advanced))
        {
            var healthParts = new EditorParts();
            HealthFields(c, healthParts, "event-health-" + node.AppliedActions, (context, health) => AddEvent(context,
                new(PredictorEventActionKind.Health) { Health = health }));
            parts.Sections.Add(new("", "", [], [])
            { EmbeddedEditor = new(c.Node.Position.NodeId, R("local_health"), healthParts.Sections,
                healthParts.Numbers, healthParts.Toggles, healthParts.Selections, "") });
        }
        if (node.NeededInput == PredictorEventNeededInput.TrialAbandon)
            parts.Sections.Add(new(R("abandon_confirm"), "", [], new[] { false, true }.Select(abandon =>
                Choice(c, R(abandon ? "yes" : "no"), context => AddEvent(context,
                    new(PredictorEventActionKind.ConfirmAbandon) { ConfirmAbandon = abandon }))).ToArray()));
        if (node.NeededInput == PredictorEventNeededInput.Rewards)
            parts.Sections.Add(new("", "", [], [Choice(c, R("leave_rewards"), context => AddEvent(context,
                new(PredictorEventActionKind.LeaveRewards)), enabled: node.Rewards.All(r => r.IsKnown && r.Value is { } reward &&
                    (reward.Status != PredictorRewardStatus.Open || reward.CanSkip && !reward.Mandatory)))]));
        if (node.NeededInput == PredictorEventNeededInput.Combat && node.Combat is { } combat)
        {
            parts.Sections.AddRange(RewardSections(c, combat.Rewards, EventCombatRewardWriter(c), detailed)
                .Select(section => section with { CombatPanel = true }));
        }
        if (node.NeededInput == PredictorEventNeededInput.Crystal && node.Crystal is { } crystal)
        {
            var choices = Enum.GetValues<PredictorCrystalTool>().Select(tool => Choice(c, R("crystal." + tool), context => AddEvent(context,
                new(PredictorEventActionKind.CrystalTool) { ExpectedCrystal = crystal, Tool = tool }), enabled: crystal.Remaining > 0, selected: crystal.Tool == tool)).ToList();
            choices.AddRange(crystal.Cells.Where(cell => cell.Hidden).Select(cell => Choice(c, (cell.X + 1) + ", " + (cell.Y + 1), context => AddEvent(context,
                new(PredictorEventActionKind.CrystalCell) { ExpectedCrystal = crystal, X = cell.X, Y = cell.Y }), enabled: crystal.Remaining > 0)));
            parts.Sections.Add(new(_text.Format("predictor.runtime.crystal_remaining", crystal.Remaining), "", [], choices));
        }
        return node.NeededInput == PredictorEventNeededInput.Obtain;
    }
}
