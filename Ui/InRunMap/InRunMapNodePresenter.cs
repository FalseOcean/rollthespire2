using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Ui.InRunMap;

// Semantic observations come exclusively from the detached session. These
// callbacks author plans; they cannot submit an input to the native game.
internal sealed partial class InRunMapNodePresenter
{
    private sealed record Context((int ActIndex, MapPosition Point) Location,
        PredictorRouteNodeKnowledge Node, PredictorPlannedNode Plan, long Revision);
    private sealed record ChoiceBinding(Context Context, Func<Context, bool> Apply);
    private sealed record FieldBinding(Context Context, Func<Context, InRunMapDetailFieldValue, bool> Apply);
    private sealed class EditorParts
    {
        internal readonly List<InRunMapDetailSection> Sections = [];
        internal readonly List<InRunMapDetailNumber> Numbers = [];
        internal readonly List<InRunMapDetailToggle> Toggles = [];
        internal readonly List<InRunMapDetailSelect> Selections = [];
        internal long ScrollToTopRequest;
    }
    private sealed class HealthDraft { internal int Current, Maximum; }
    private sealed class CardSelectionDraft(ImmutableArray<int> saved)
    {
        internal readonly ImmutableArray<int> Saved = saved;
        internal readonly HashSet<int> Selected = saved.IsDefault ? [] : saved.ToHashSet();
    }
    private readonly InRunMapPredictionSession _session;
    private IGameContentNameResolver _names;
    private IUiTextProvider _text;
    private readonly Dictionary<string, ChoiceBinding> _choices = [];
    private readonly Dictionary<string, FieldBinding> _fields = [];
    private readonly Dictionary<(int, MapPosition), InRunMapNodeDetailData> _details = [];
    private readonly Dictionary<(int, MapPosition), InRunMapNodeEditorData> _editors = [];
    private readonly Dictionary<string, CardSelectionDraft> _selections = [];
    private readonly Dictionary<string, HealthDraft> _health = [];
    private long _revision = -1, _nextId;
    private long _localizationRevision;

    internal InRunMapNodePresenter(InRunMapPredictionSession session,
        IGameContentNameResolver names, IUiTextProvider text)
    { _session = session; _names = names; _text = text; }

    internal void RebindLocalization(IGameContentNameResolver names, IUiTextProvider text)
    {
        _names = names; _text = text;
        _localizationRevision++;
        // Only translated projections and their callbacks expire. Local card,
        // health, potion and diagnostic drafts belong to this same presenter.
        _rewardPreviewCache.Clear();
        Invalidate();
    }

    internal InRunMapNodeDetailData? Detail((int ActIndex, MapPosition Point) location)
    {
        Synchronize();
        if (_details.TryGetValue(location, out var cached)) return cached;
        if (Current(location) is not { } context) return FaultDetail(location) ?? TerminalDetail(location);
        var node = context.Node;
        var sections = node.Event != null ? new List<InRunMapDetailSection>() :
            RewardSections(context, node.Rewards, RewardWriter(context)).ToList();
        var influences = CombatRewardInfluences(node);
        if (influences.Length != 0) sections.Insert(0, new(M("key_relics"), "", influences, []));
        if (_session.TerminalBoundaries.TryGetValue(location, out var terminal))
            sections.Add(new(D("entry_blocked"), D("entry_not_entered") + "\n" +
                string.Join("\n", PendingLines(terminal.PendingCauses)), [], []));
        if (node.Event is { } eventNode)
        {
            var eventParts = new EditorParts(); EventEditor(context, eventParts, eventNode, detailed: false);
            sections.AddRange(eventParts.Sections);
        }
        if (node.Ancient is { } ancient)
        {
            var ancientParts = new EditorParts(); AncientEditor(context, ancientParts, ancient, detailed: false);
            sections.AddRange(ancientParts.Sections);
        }
        if (node.Treasure is { } treasure)
        {
            var treasureParts = new EditorParts(); TreasureEditor(context, treasureParts, treasure, detailed: false);
            sections.AddRange(treasureParts.Sections);
        }
        if (node.Room == PredictorRoomKind.RestSite || node.Shop != null)
        {
            var roomParts = new EditorParts();
            if (node.Room == PredictorRoomKind.RestSite) RestEditor(context, roomParts, detailed: false);
            if (node.Shop is { } shop) ShopEditor(context, roomParts, shop, detailed: false);
            sections.AddRange(roomParts.Sections);
        }
        if (node.Event == null && RewardObtainParent(node.Rewards) != null)
        {
            var obtainParts = new EditorParts(); RewardObtainSections(context, obtainParts, node.Rewards, mainSurface: true);
            sections.AddRange(obtainParts.Sections);
        }
        foreach (var storage in node.PaelsToothStorage)
            sections.Add(new(_names.Resolve(new ModelKey("RELIC", "PAELS_TOOTH"), GameContentKind.Relic),
                storage.InitialCount is { } count ? _text.Format("predictor.map.paels_tooth.storage_pending", count) :
                    M("paels_tooth.storage_count_pending"), [], []));
        foreach (var effect in node.ObtainHealthEffects)
        {
            string key = effect.Kind switch
            {
                PredictorObtainHealthEffectKind.LoseMaxHp => "lose_max_hp",
                PredictorObtainHealthEffectKind.Damage => "damage",
                PredictorObtainHealthEffectKind.GainMaxHpAndFullHeal => "full_heal",
                PredictorObtainHealthEffectKind.HealMaxHpFraction => "heal_fraction",
                _ => throw new ArgumentOutOfRangeException(nameof(effect.Kind))
            };
            sections.Add(new(_names.Resolve(effect.Source, GameContentKind.Relic),
                _text.Format("predictor.map.obtain_health." + key, effect.Amount), [], []));
        }
        // Show premises only when this node has supplemental input controls.
        // Main branch choices and read-only feedback already have a surface.
        var premiseInputs = BuildEditor(context, includeDiagnostics: false);
        var premises = HasEditorInputs(premiseInputs) ? premiseInputs : null;
        string subtitle = node.Shop is { } shopBalance ? ShopGoldBalance(shopBalance) : "";
        if (node.Event?.SelectedOccurrence != null && context.Plan.Event?.Occurrence == null)
            subtitle = M("default_event");
        // Actual missing inputs name their source and affected results here;
        // optional precision inputs remain in the premise panel below.
        var prerequisites = PrerequisiteViews(node);
        var information = prerequisites.Select(row => row.Text).ToArray();
        var sources = prerequisites.Select(row => row.Summary ?? row.Source).Where(value => value != null)
            .Select(value => value!).Distinct().ToArray();
        long localization = _localizationRevision;
        var data = new InRunMapNodeDetailData(node.Position.NodeId, Title(node), subtitle, information,
            sections, [],
            Premises: premises, PremisesLabel: M("reward_influences"))
        { PrerequisiteSummary = sources.Length == 0 ? null : _text.Format("predictor.map.prerequisite.summary", string.Join(" · ", sources)),
            IsCurrent = () => localization == _localizationRevision && Valid(context, context.Node.Position.NodeId) };
        _details[location] = data;
        return data;
    }

    internal InRunMapNodeEditorData? Editor((int ActIndex, MapPosition Point) location)
    {
        Synchronize();
        if (_editors.TryGetValue(location, out var cached)) return cached;
        if (Current(location) is not { } context) return TerminalEditor(location);
        var data = BuildEditor(context, includeDiagnostics: true);
        _editors[location] = data;
        return data;
    }

    private InRunMapNodeEditorData BuildEditor(Context context, bool includeDiagnostics)
    {
        var parts = new EditorParts();
        if (includeDiagnostics) DiagnosticsEditor(context, parts);
        if (context.Node.CombatRequirements != null) CombatEditor(context, parts, includeDiagnostics);
        if (!includeDiagnostics && context.Node.Event is { } eventNode)
            EventStageRows(context, parts, eventNode, premisesOnly: true);
        else RoomEditor(context, parts, includeRestRemoval: includeDiagnostics, premisesOnly: !includeDiagnostics);
        // Current event-combat results already have their own main surface.
        // Premises retain input controls, without registering those same
        // read-only rewards and native card groups for a second time.
        if (!includeDiagnostics) parts.Sections.RemoveAll(section => section.CombatPanel);
        foreach (var optional in context.Node.OptionalObtainInputs)
        {
            int existing = OptionalObtainInputIndex(context, optional);
            parts.Sections.Add(new(_names.Resolve(optional.Choice.Source, GameContentKind.Relic), M("optional_obtain_target"), [],
                existing < 0 ? [] : [Choice(context, M("clear_optional_input"), current => Set(current,
                    current.Plan with { ObtainInputs = current.Plan.ObtainInputs.RemoveAt(existing) }))]));
            ObtainEditor(context, parts, optional.Choice, optional: optional);
        }
        if (includeDiagnostics && context.Node.Event == null)
            parts.Sections.AddRange(RewardSections(context, context.Node.Rewards, RewardWriter(context), detailed: true));
        var pending = includeDiagnostics ? PendingLines(context.Node.PendingCauses).ToArray() :
            PrerequisiteLines(context.Node).Concat(PendingLines(context.Node.PendingCauses.Where(cause =>
                cause.Code is "PlannedOptionalObtainSourcePending" or "PaelsToothPlannedTargetsPending"))).Distinct().ToArray();
        if (pending.Length != 0)
            parts.Sections.Add(new(R("combat_pending"), string.Join("\n", pending), [], []));
        if (includeDiagnostics || parts.Numbers.Count != 0 || parts.Toggles.Count != 0 || parts.Selections.Count != 0 ||
            parts.Sections.Any(HasSectionInputs))
            parts.Sections.Add(new("", "", [], [Choice(context, R("plan.clear"), c =>
            {
                DiscardLocal(c.Node.Position.NodeId);
                return _session.RemovePlan(c.Location.ActIndex, c.Location.Point, c.Revision);
            })]));
        return new InRunMapNodeEditorData(context.Node.Position.NodeId, Title(context.Node),
            parts.Sections, parts.Numbers, parts.Toggles, parts.Selections, M("close_editor"))
        { ScrollToTopRequest = parts.ScrollToTopRequest };
    }

    private static bool HasEditorInputs(InRunMapNodeEditorData editor) =>
        editor.Numbers.Count != 0 || editor.Toggles.Count != 0 || editor.Selections.Count != 0 ||
        editor.Sections.Any(HasSectionInputs);

    private static bool HasSectionInputs(InRunMapDetailSection section) =>
        section.Choices.Count != 0 || section.HeaderChoice != null ||
        section.EmbeddedEditor is { } editor && HasEditorInputs(editor);

    internal InRunMapNodeEditorData? DiagnosticView((int ActIndex, MapPosition Point) location)
    {
        Synchronize();
        if (Current(location) is not { } context) return TerminalEditor(location);
        var parts = new EditorParts(); DiagnosticsEditor(context, parts);
        if (_session.TerminalBoundaries.TryGetValue(location, out var terminal))
            TerminalDiagnostics(location, terminal, context.Node.Position.NodeId, parts);
        return new(context.Node.Position.NodeId, Title(context.Node), parts.Sections,
            parts.Numbers, parts.Toggles, parts.Selections, M("close_editor")) { ScrollToTopRequest = parts.ScrollToTopRequest };
    }

    internal bool ApplyChoice(string nodeId, string choiceId)
    {
        if (_faultChoices.ContainsKey(choiceId)) return ApplyFaultChoice(nodeId, choiceId);
        if (_terminalDiagnosticChoices.TryGetValue(choiceId, out var terminal))
        {
            if (terminal.Revision != _session.Revision || terminal.RequestNodeId != nodeId ||
                !_session.TerminalBoundaries.TryGetValue(terminal.Location, out var current) ||
                current.TargetPosition.NodeId != terminal.TargetNodeId) return false;
            bool updated = terminal.Apply();
            if (updated) Invalidate();
            return updated;
        }
        if (!_choices.TryGetValue(choiceId, out var binding) || !Valid(binding.Context, nodeId)) return false;
        bool changed = binding.Apply(binding.Context);
        if (changed) Invalidate();
        return changed;
    }
    internal bool ApplyField(string nodeId, string fieldId, InRunMapDetailFieldValue value)
    {
        if (!_fields.TryGetValue(fieldId, out var binding) || !Valid(binding.Context, nodeId)) return false;
        bool changed = binding.Apply(binding.Context, value);
        if (changed) Invalidate();
        return changed;
    }
    private bool Valid(Context context, string nodeId) => context.Revision == _session.Revision && context.Node.Position.NodeId == nodeId &&
        _session.Nodes.TryGetValue(context.Location, out var current) && current.Position.NodeId == nodeId;
    private Context? Current((int ActIndex, MapPosition Point) location)
    {
        if (!_session.Nodes.TryGetValue(location, out var node)) return null;
        var plan = _session.GetPlan(location.ActIndex, location.Point);
        return new(location, node, plan?.NodeId == node.Position.NodeId ? plan : new(node.Position.NodeId), _session.Revision);
    }
    private void Synchronize()
    {
        if (_revision == _session.Revision) return;
        _revision = _session.Revision; Invalidate();
        SynchronizeDiagnostics();
        SynchronizeEventStages();
        var liveIds = _session.Nodes.Values.Select(n => n.Position.NodeId + "|").ToArray();
        foreach (string key in _selections.Keys.Where(k => !liveIds.Any(k.StartsWith)).ToArray()) _selections.Remove(key);
        foreach (string key in _health.Keys.Where(k => !liveIds.Any(k.StartsWith)).ToArray()) _health.Remove(key);
        string seed = _session.Snapshot?.Run.Context.Seed ?? "";
        foreach (var key in _eventPotionDrafts.Keys.Where(k => k.Seed != seed || !liveIds.Contains(k.NodeId + "|")).ToArray())
            _eventPotionDrafts.Remove(key);
    }
    private void Invalidate() { _details.Clear(); _editors.Clear(); _choices.Clear(); _fields.Clear(); _terminalDiagnosticChoices.Clear(); _faultChoices.Clear(); }
    internal void DiscardNodeProjection((int ActIndex, MapPosition Point) location)
    {
        _details.Remove(location); _editors.Remove(location);
        foreach (var key in _choices.Where(pair => pair.Value.Context.Location == location).Select(pair => pair.Key).ToArray())
            _choices.Remove(key);
        foreach (var key in _fields.Where(pair => pair.Value.Context.Location == location).Select(pair => pair.Key).ToArray())
            _fields.Remove(key);
        foreach (var key in _terminalDiagnosticChoices.Where(pair => pair.Value.Location == location).Select(pair => pair.Key).ToArray())
            _terminalDiagnosticChoices.Remove(key);
    }
    private void DiscardLocal(string nodeId)
    {
        DiscardEventStages(nodeId);
        string prefix = nodeId + "|";
        foreach (string key in _selections.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) _selections.Remove(key);
        foreach (string key in _health.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) _health.Remove(key);
        foreach (var key in _eventPotionDrafts.Keys.Where(k => k.NodeId == nodeId).ToArray()) _eventPotionDrafts.Remove(key);
    }
    private string Id() => "map-choice-" + ++_nextId;
    private bool Set(Context c, PredictorPlannedNode plan) => _session.SetPlan(c.Location.ActIndex, c.Location.Point, plan, c.Revision);
    private InRunMapDetailChoice Choice(Context c, string label, Func<Context, bool> apply,
        InRunMapDetailObject? item = null, bool enabled = true, bool selected = false)
    {
        string id = Id(); if (enabled) _choices[id] = new(c, apply);
        return new(id, label, item, enabled, selected);
    }
    private string Field(Context c, Func<Context, InRunMapDetailFieldValue, bool> apply)
    { string id = Id(); _fields[id] = new(c, apply); return id; }
    private string R(string key) => _text.Get("predictor.runtime." + key);
    private string M(string key) => _text.Get("predictor.map." + key);
    private string Title(PredictorRouteNodeKnowledge node) => node.Model is { } key ?
        _names.Resolve(key, Kind(key)) : R("plan.room." + node.Room);
    private static GameContentKind Kind(ModelKey key) => key.Category switch
    {
        "CARD" => GameContentKind.Card, "RELIC" => GameContentKind.Relic, "POTION" => GameContentKind.Potion,
        "CHARACTER" => GameContentKind.Character, "ENCOUNTER" => GameContentKind.Encounter,
        "ANCIENT" => GameContentKind.Ancient, "ACT" => GameContentKind.Act, _ => GameContentKind.Event
    };
    private static InRunMapDetailObject Object(ModelKey key) => new(key, Kind(key));
    private static InRunMapDetailObject Object(PredictorCard card) => new(card.Key, GameContentKind.Card, card.UpgradeLevel)
    { CardSnapshot = card };
    private static PredictorPlannedCard Target(PredictorCard card) => new(card.Id, card.Key, card.FloorAdded);
    private static bool Integer(double? value, out int result, int minimum = 0)
    {
        result = 0;
        if (value is not { } number || !double.IsFinite(number) || number < minimum || number > int.MaxValue || Math.Truncate(number) != number) return false;
        result = (int)number; return true;
    }
    private static bool SameReward(PredictorReward a, PredictorReward b) => a.Kind == b.Kind && a.Origin == b.Origin && a.Key == b.Key;
    // An offer binding proves the opportunity and its card identities. It does
    // not promote its explicitly masked enchantments to complete card facts.
    private static PredictorReward? RewardOffer(PredictorCombatRewardKnowledge knowledge) =>
        knowledge.OfferBinding?.Value ?? (knowledge.IsKnown ? knowledge.Value : null);
    private InRunMapDetailObject RewardCardObject(PredictorCombatRewardKnowledge knowledge, PredictorCard card, int index) =>
        knowledge.CardOptions.ElementAtOrDefault(index) is { IsKnown: false }
            ? new(card.Key, GameContentKind.Card, card.UpgradeLevel,
                _names.Resolve(card.Key, GameContentKind.Card) + " · " + M("card_enchantments_unresolved"))
            : Object(card);
    private static bool SameRewardChoice(PredictorPlannedRewardChoice? saved, PredictorPlannedRewardChoice current) =>
        saved != null && SameReward(saved.ExpectedReward, current.ExpectedReward) && saved.Occurrence == current.Occurrence;
    private static PredictorPlannedNode WithReward(PredictorPlannedNode plan, PredictorPlannedRewardChoice choice)
    {
        int index = plan.RewardChoices.FindIndex(c => SameReward(c.ExpectedReward, choice.ExpectedReward) && c.Occurrence == choice.Occurrence);
        return plan with { RewardChoices = index < 0 ? plan.RewardChoices.Add(choice) : plan.RewardChoices.SetItem(index, choice) };
    }
    private Func<Context, PredictorPlannedRewardChoice, bool>? RewardWriter(Context context)
    {
        if (context.Node.Room is PredictorRoomKind.Monster or PredictorRoomKind.Elite or PredictorRoomKind.Boss or PredictorRoomKind.RestSite)
            return (c, choice) => Set(c, WithReward(c.Plan, choice));
        if (context.Node.Event is { } eventNode && EventPlanApplies(context, eventNode) && context.Plan.Event is { } eventPlan &&
            (eventPlan.Actions.Any(action => action.RewardChoice != null || action.Obtain?.Reward != null) ||
             eventNode.Rewards.Any(reward => reward.DefaultSkipped || reward.AppliedChoice != null)))
            return (c, choice) =>
            {
                var plan = c.Plan.Event!;
                int index = plan.Actions.FindIndex(action => SameRewardChoice(action.RewardChoice ?? action.Obtain?.Reward, choice));
                if (index >= 0 || c.Node.Event?.NeededInput == PredictorEventNeededInput.Rewards ||
                    c.Node.Event?.Rewards.Any(reward => RewardOffer(reward) is { } value && SameReward(value, choice.ExpectedReward) &&
                        reward.Occurrence == choice.Occurrence && (reward.DefaultSkipped || reward.AppliedChoice != null)) == true)
                    return Set(c, EventRewardPlan(c, choice));
                return RequiredRoomChild(c.Node)?.Phase == PredictorPhase.Rewards && AddObtain(c,
                    new(PredictorObtainChildAction.Reward, RequiredRoomChild(c.Node)!.Source) { Reward = choice });
            };
        if (context.Node.Event is { NeededInput: PredictorEventNeededInput.Rewards })
            return (c, choice) => AddEvent(c, new(PredictorEventActionKind.Reward) { RewardChoice = choice });
        if (context.Plan.Ancient is { } ancient && ancient.Children.Any(child => child.Reward != null))
            return (c, choice) =>
            {
                var plan = c.Plan.Ancient!;
                int index = plan.Children.FindIndex(child => SameRewardChoice(child.Reward, choice));
                return index < 0 ? RequiredRoomChild(c.Node)?.Phase == PredictorPhase.Rewards && AddObtain(c,
                    new(PredictorObtainChildAction.Reward, RequiredRoomChild(c.Node)!.Source) { Reward = choice }) : Set(c, c.Plan with { Ancient = plan with
                { Children = plan.Children.SetItem(index, plan.Children[index] with { Reward = choice }) } });
            };
        if (context.Plan.Shop is { } shop && shop.Actions.Any(action => action.Obtain?.Reward != null))
            return (c, choice) =>
            {
                var plan = c.Plan.Shop!;
                int index = plan.Actions.FindIndex(action => SameRewardChoice(action.Obtain?.Reward, choice));
                return index < 0 ? RequiredRoomChild(c.Node)?.Phase == PredictorPhase.Rewards && AddObtain(c,
                    new(PredictorObtainChildAction.Reward, RequiredRoomChild(c.Node)!.Source) { Reward = choice }) : Set(c, c.Plan with { Shop = plan with
                { Actions = plan.Actions.SetItem(index, plan.Actions[index] with { Obtain = plan.Actions[index].Obtain! with { Reward = choice } }) } });
            };
        if (context.Plan.Treasure is { } treasure && treasure.Actions.Any(action => action.Obtain?.Reward != null))
            return (c, choice) =>
            {
                var plan = c.Plan.Treasure!;
                int index = plan.Actions.FindIndex(action => SameRewardChoice(action.Obtain?.Reward, choice));
                return index < 0 ? RequiredRoomChild(c.Node)?.Phase == PredictorPhase.Rewards && AddObtain(c,
                    new(PredictorObtainChildAction.Reward, RequiredRoomChild(c.Node)!.Source) { Reward = choice }) : Set(c, c.Plan with { Treasure = plan with
                { Actions = plan.Actions.SetItem(index, plan.Actions[index] with { Obtain = plan.Actions[index].Obtain! with { Reward = choice } }) } });
            };
        var child = RequiredRoomChild(context.Node);
        return child?.Phase == PredictorPhase.Rewards ? (c, choice) => AddObtain(c,
            new(PredictorObtainChildAction.Reward, child.Source) { Reward = choice }) : null;
    }
    private IEnumerable<InRunMapDetailSection> RewardSections(Context context,
        ImmutableArray<PredictorCombatRewardKnowledge> rewards,
        Func<Context, PredictorPlannedRewardChoice, bool>? write, bool detailed = false)
    {
        foreach (int r in Enumerable.Range(0, rewards.Length).OrderBy(index =>
            rewards[index].Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard ? 1 : 0))
        {
            var knowledge = rewards[r];
            Func<Context, PredictorPlannedRewardChoice, bool>? writeReward = knowledge.CompletedEdit is { } completed
                ? (c, choice) => Set(c, PredictorRun.ApplyCompletedRewardChoice(c.Plan, completed, choice)) : write;
            if (RewardOffer(knowledge) is not { } reward)
            {
                if (!knowledge.CardOptions.IsEmpty)
                {
                    string pending = M("card_enchantments_unresolved");
                    if (detailed) pending += "\n" + string.Join("\n", DiagnosticPendingLines(knowledge.PendingCauses));
                    yield return new(R("reward." + knowledge.Kind), pending,
                        knowledge.CardOptions.Select((option, index) => RewardCardObject(knowledge, option.Value, index)).ToArray(), [])
                    { StableId = RewardSectionId(context, knowledge, r), LargeCardResults = knowledge.Kind == PredictorRewardKind.Card };
                    continue;
                }
                yield return new(R("reward." + knowledge.Kind), string.Join("\n", (detailed ? DiagnosticPendingLines(knowledge.PendingCauses) : [])
                    .Prepend(knowledge.DefaultSkipped ? R("default_skip") + " · " + R("combat_pending") : R("combat_pending"))), [], [])
                { StableId = RewardSectionId(context, knowledge, r),
                    GainReward = knowledge.Kind is PredictorRewardKind.Potion or PredictorRewardKind.Relic };
                continue;
            }
            int occurrence = knowledge.Occurrence >= 0 ? knowledge.Occurrence :
                rewards.Take(r).Count(k => RewardOffer(k) is { } prior && SameReward(prior, reward));
            // Only the core's replay receipt can style a choice as accepted.
            var old = knowledge.AppliedChoice;
            var objects = reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard ?
                reward.Cards.Select((card, index) => RewardCardObject(knowledge, card, index)).ToArray() :
                reward.Kind is PredictorRewardKind.Relic or PredictorRewardKind.Potion ? new[] { Object(reward.Key) } : [];
            string text = reward.Kind == PredictorRewardKind.Gold ? "+" +
                (reward.GoldAmountUnspecified ? R("gold_recovered_unexpanded") : reward.GoldAmount.ToString()) : "";
            if (old?.SacrificeRelicId != null)
                text += " · " + R("alternative.SACRIFICE") + (knowledge.SacrificeCount is { } count
                    ? " · " + _text.Format("predictor.runtime.alternative.sacrifice_count", count) : "");
            else if (knowledge.DefaultSkipped) text += " · " + R("default_skip");
            else if (reward.Status == PredictorRewardStatus.Taken && reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard)
                text += " · " + M("planned_choice");
            else if (reward.Status == PredictorRewardStatus.Skipped) text += " · " + R("skip");
            else if (old == null && reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) text += " · " + M("unselected");
            if (!knowledge.IsKnown && !knowledge.CardOptions.IsEmpty)
                text += "\n" + M("card_enchantments_unresolved");
            var alternativeCauses = PendingLines(knowledge.Alternatives.SelectMany(alternative => alternative.PendingCauses)).ToArray();
            if (alternativeCauses.Length != 0) text += "\n" + string.Join("\n", alternativeCauses);
            var choices = new List<InRunMapDetailChoice>();
            // Taken/skipped forecasts are still pure authored intent. A saved
            // root reward choice can be revised without clearing the node.
            if (writeReward != null && reward.Kind != PredictorRewardKind.Potion &&
                (reward.Status == PredictorRewardStatus.Open || old != null || knowledge.DefaultSkipped || reward.Kind == PredictorRewardKind.Relic))
            {
                if (reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard)
                    for (int card = 0; card < reward.Cards.Length; card++)
                    {
                        int index = card;
                        bool selected = old is { Skip: false, SacrificeRelicId: null } && old.CardIndex == index;
                        bool cancelTake = selected && reward.CanSkip && !reward.Mandatory;
                        choices.Add(RewardChoice(context, R(cancelTake ? "cancel" : "plan.take"), knowledge,
                            CardRewardChoice(reward, occurrence, old, knowledge.OfferBinding, index), writeReward, cancelTake ? "CANCEL_TAKE" : "TAKE",
                            RewardCardObject(knowledge, reward.Cards[card], card),
                            enabled: cancelTake || knowledge.IsKnown || knowledge.CanChooseCard(index), selected: selected, cancelTake: cancelTake));
                    }
                else if (detailed && reward.Kind == PredictorRewardKind.Relic)
                    choices.Add(Choice(context, M("take_reward"), c => writeReward(c, new(reward, occurrence)), selected: old is { Skip: false }));
                // Optional card offers return to the live default by clicking
                // the selected card again; only non-card explicit skips remain.
                bool rootReward = context.Node.Room is PredictorRoomKind.Monster or PredictorRoomKind.Elite or PredictorRoomKind.Boss or PredictorRoomKind.RestSite;
                if (detailed && reward.Kind is not (PredictorRewardKind.Gold or PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) &&
                    reward.CanSkip && !reward.Mandatory && rootReward)
                    choices.Add(RewardChoice(context, R("plan.skip"), knowledge,
                        CardRewardChoice(reward, occurrence, old, knowledge.OfferBinding, skip: true), writeReward, "SKIP", selected: old?.Skip == true));
                foreach (var alternative in knowledge.Alternatives.Where(alternative => alternative.Key is "REROLL" or "SACRIFICE"))
                {
                    bool reroll = alternative.Key == "REROLL";
                    bool chosen = reroll ? old?.Reroll == true : old?.SacrificeRelicId == alternative.RelicId;
                    choices.Add(RewardChoice(context, R("alternative." + alternative.Key), knowledge,
                        chosen ? new(old!.ExpectedReward, occurrence) : new(old?.ExpectedReward ?? reward, occurrence)
                        { Reroll = reroll, SacrificeRelicId = reroll ? null : alternative.RelicId,
                            UnresolvedEnchantmentIndices = old?.UnresolvedEnchantmentIndices ?? knowledge.OfferBinding?.UnresolvedEnchantmentIndices ?? [] }, writeReward, alternative.Key,
                        enabled: alternative.IsKnown, selected: chosen, cancelAlternative: chosen));
                }
                if (old?.Reroll == true && !knowledge.Alternatives.Any(alternative => alternative.Key == "REROLL"))
                    choices.Add(RewardChoice(context, R("alternative.REROLL"), knowledge,
                        new(old.ExpectedReward, occurrence), writeReward, "REROLL", selected: true, cancelAlternative: true));
            }
            yield return new(R("reward." + reward.Kind), text.Trim(' ', '·'), objects, choices)
            { RewardActionsRow = reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard,
                LargeCardResults = reward.Kind == PredictorRewardKind.Card,
                StableId = RewardSectionId(context, knowledge, occurrence),
                GainReward = reward.Kind is PredictorRewardKind.Potion or PredictorRewardKind.Relic };
            if (knowledge.SacrificeRelic is { } sacrificedRelic)
                yield return new(R("alternative.SACRIFICE"), "", [Object(sacrificedRelic)], []);
        }
    }
    private static string RewardSectionId(Context context, PredictorCombatRewardKnowledge knowledge, int occurrence) =>
        context.Node.Position.NodeId + "|reward|" + knowledge.Kind + "|" + RewardOffer(knowledge)?.Origin + "|" +
        RewardOffer(knowledge)?.Key + "|" + occurrence + "|" + knowledge.CompletedEdit?.Source;
    private static PredictorPlannedRewardChoice CardRewardChoice(PredictorReward displayed, int occurrence,
        PredictorPlannedRewardChoice? applied, PredictorRewardOfferBinding? binding, int? card = null, bool skip = false) =>
        applied?.Reroll == true ? new(applied.ExpectedReward, occurrence, card, skip)
        { Reroll = true, ExpectedRerolledReward = displayed,
            UnresolvedEnchantmentIndices = applied.UnresolvedEnchantmentIndices } :
        new(applied?.ExpectedReward ?? displayed, occurrence, card, skip)
        { UnresolvedEnchantmentIndices = applied?.UnresolvedEnchantmentIndices ?? binding?.UnresolvedEnchantmentIndices ?? [] };
    private HealthDraft Health(Context c, string role, PredictorEventHealthFact? initial = null)
    {
        string key = c.Node.Position.NodeId + "|" + role + "|" + c.Node.Model;
        if (!_health.TryGetValue(key, out var value)) _health[key] = value = new() { Current = initial?.CurrentHp ?? 0, Maximum = initial?.MaxHp ?? 0 };
        return value;
    }
    private void HealthFields(Context c, EditorParts parts, string role, Func<Context, PredictorEventHealthFact, bool> apply, PredictorEventHealthFact? initial = null)
    {
        var health = Health(c, role, initial);
        parts.Numbers.Add(new(Field(c, (_, v) => { if (!Integer(v.Number, out int value)) return false; health.Current = value; return true; }), R("current_hp"), health.Current, 0, int.MaxValue));
        parts.Numbers.Add(new(Field(c, (_, v) => { if (!Integer(v.Number, out int value)) return false; health.Maximum = value; return true; }), R("maximum_hp"), health.Maximum, 0, int.MaxValue));
        parts.Sections.Add(new(R("health_untracked"), R("local_health"), [], [Choice(c, R("confirm"), context => apply(context, new(health.Current, health.Maximum)),
            enabled: health.Current > 0 && health.Maximum >= health.Current)]));
    }
    private void CardSelection(Context c, EditorParts parts, string role, ImmutableArray<PredictorCard> cards,
        int minimum, int maximum, Func<Context, ImmutableArray<int>, bool> apply, bool enabled = true,
        ImmutableArray<int> selectedIndices = default, bool largeCardResults = false,
        Func<PredictorCard, InRunMapDetailObject>? display = null)
    {
        if (maximum == 1)
        {
            var direct = cards.Select((card, index) => Choice(c, R("plan.take"), context => apply(context, [index]),
                display?.Invoke(card) ?? Object(card), enabled, !selectedIndices.IsDefault && selectedIndices.Contains(index))).ToList();
            if (minimum == 0) direct.Add(Choice(c, R("plan.skip"), context => apply(context, []), enabled: enabled,
                selected: !selectedIndices.IsDefault && selectedIndices.IsEmpty));
            parts.Sections.Add(new(minimum == 1 ? M("select_one_card") : _text.Format("predictor.runtime.select_cards", minimum, maximum), "", [], direct)
                { CardColumns = 4, LargeCardResults = largeCardResults });
            return;
        }
        string key = c.Node.Position.NodeId + "|" + role + "|" + JsonSerializer.Serialize(cards);
        // Keep unconfirmed edits while their saved source is unchanged. A
        // confirmed replacement or clear reseeds only this picker; translated
        // redraws and unrelated plan edits keep the player's current draft.
        if (!_selections.TryGetValue(key, out var draft) || !selectedIndices.IsDefault &&
            (draft.Saved.IsDefault || !draft.Saved.SequenceEqual(selectedIndices)))
            _selections[key] = draft = new(selectedIndices);
        var selected = draft.Selected;
        var options = cards.Select((card, index) => Choice(c, "", _ => { if (!selected.Add(index)) selected.Remove(index); return true; },
            display?.Invoke(card) ?? Object(card), enabled, selected.Contains(index))).ToList();
        options.Add(Choice(c, R("confirm"), context => apply(context, selected.Order().ToImmutableArray()),
            enabled: enabled && selected.Count >= minimum && selected.Count <= maximum));
        parts.Sections.Add(new(_text.Format("predictor.runtime.select_cards", minimum, maximum), "", [], options)
            { CardColumns = 4, LargeCardResults = largeCardResults });
    }
}
