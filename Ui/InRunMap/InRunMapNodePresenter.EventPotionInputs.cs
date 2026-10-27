using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private sealed class EventPotionDraft
    {
        internal int EmptySlots;
        internal readonly List<ModelKey?> Bottles = [];
        internal HashSet<int> RetainedEmptySlots = [];
    }
    private readonly Dictionary<(string Seed, string NodeId, ModelKey Event, ChooseEventOccurrence? Occurrence), EventPotionDraft> _eventPotionDrafts = [];
    private sealed class ConveyorDraft { internal int EmptySlots, LostHp; }
    private readonly Dictionary<(string Seed, string NodeId, ChooseEventOccurrence? Occurrence, string Capture), ConveyorDraft> _conveyorDrafts = [];

    private void SynchronizeConveyorDrafts()
    {
        string seed = _session.Snapshot?.Run.Context.Seed ?? "", capture = _session.Snapshot?.Fingerprint ?? "";
        var nodes = _session.Nodes.Values.Select(node => node.Position.NodeId).ToHashSet();
        foreach (var key in _conveyorDrafts.Keys.Where(key => key.Seed != seed || key.Capture != capture || !nodes.Contains(key.NodeId)).ToArray())
            _conveyorDrafts.Remove(key);
    }

    private void ConveyorInputs(Context c, EditorParts parts, PredictorEventNodeKnowledge node)
    {
        SynchronizeConveyorDrafts();
        ChooseEventOccurrence? occurrence = node.Occurrence is { } observed && node.SelectedOccurrence is { } selected
            ? new(selected.Event, selected.QueueEvent, selected.RawOrdinal, observed.Cursor, selected.Cursor) :
            EventPlanApplies(c, node) ? c.Plan.Event!.ExpectedOccurrence ?? c.Plan.Event.Occurrence : null;
        var draftKey = (_session.Snapshot?.Run.Context.Seed ?? "", c.Node.Position.NodeId, occurrence, _session.Snapshot?.Fingerprint ?? "");
        foreach (var stale in _conveyorDrafts.Keys.Where(key => key.NodeId == draftKey.Item2 && key != draftKey).ToArray())
            _conveyorDrafts.Remove(stale);
        var supplied = node.ConveyorInput;
        var inventory = IncomingPotionInventory(c.Node);
        int? capacity = IncomingPotionCapacity(inventory);
        if (!_conveyorDrafts.TryGetValue(draftKey, out var draft))
        {
            var legacyHealth = EventPlanApplies(c, node) ? c.Plan.Event!.Actions.TakeWhile(action =>
                action.Action != PredictorEventActionKind.Option).FirstOrDefault(action => action.Action == PredictorEventActionKind.Health)?.Health : null;
            var legacyPotions = EventPlanApplies(c, node) ? c.Plan.Event!.Potions : node.PotionInput;
            _conveyorDrafts[draftKey] = draft = new()
            {
                EmptySlots = supplied?.EmptyPotionSlots ?? legacyPotions?.EmptySlots ??
                    (inventory is { PotionPendingCauses.IsEmpty: true } ? inventory.PotionSlots.Count(slot => slot.Value == null) : 0),
                LostHp = supplied?.LostHp ?? (legacyHealth == null ? 0 : legacyHealth.MaxHp - legacyHealth.CurrentHp)
            };
        }
        var input = new PredictorConveyorInput(draft.EmptySlots, draft.LostHp);
        bool adopted = supplied == input;
        bool valid = draft.EmptySlots >= 0 && draft.LostHp >= 0 && (capacity == null || draft.EmptySlots <= capacity);
        var local = new EditorParts();
        local.Numbers.Add(new(Field(c, (_, value) =>
        {
            if (!Integer(value.Number, out int count) || count < 0 || capacity is { } limit && count > limit) return false;
            if (draft.EmptySlots == count) return false;
            draft.EmptySlots = count; return true;
        }), M("event_potions.empty_count"), draft.EmptySlots, 0, Math.Max(draft.EmptySlots, capacity ?? int.MaxValue)));
        local.Numbers.Add(new(Field(c, (_, value) =>
        {
            if (!Integer(value.Number, out int lost) || lost < 0 || draft.LostHp == lost) return false;
            draft.LostHp = lost; return true;
        }), M("conveyor.lost_hp"), draft.LostHp, 0, int.MaxValue));
        string status = supplied == null ? M("conveyor.pending") :
            _text.Format("predictor.map.conveyor.applied", supplied.EmptyPotionSlots, supplied.LostHp) +
                (adopted ? "" : "\n" + M("conveyor.edited"));
        if (!valid && capacity is { } maximum)
            status += "\n" + _text.Format("predictor.map.event_potions.capacity_mismatch", draft.EmptySlots, maximum);
        local.Sections.Add(new("", status, [], [Choice(c, R("confirm"), context =>
        {
            if (!valid || supplied == input) return false;
            var key = new ModelKey("EVENT", "ENDLESS_CONVEYOR");
            var plan = EventPlanApplies(context, node) ? context.Plan.Event! : new PredictorPlannedEvent(key);
            int firstOption = plan.Actions.FindIndex(action => action.Action == PredictorEventActionKind.Option);
            int boundary = firstOption < 0 ? plan.Actions.Length : firstOption;
            var actions = plan.Actions.Where((action, index) => index >= boundary ||
                action.Action is not (PredictorEventActionKind.Health or PredictorEventActionKind.ConveyorInputs)).ToImmutableArray();
            int entry = actions.TakeWhile(action => action.Action != PredictorEventActionKind.Option).Count();
            actions = actions.Insert(entry, new(PredictorEventActionKind.ConveyorInputs) { Conveyor = input });
            return Set(context, context.Plan with { Event = plan with { Potions = null, Actions = actions } });
        }, enabled: valid && !adopted, selected: adopted)]));
        parts.Sections.Add(new(M("conveyor.title"), M("conveyor.hint"), [], [])
        { EmbeddedEditor = new(c.Node.Position.NodeId, M("conveyor.title"), local.Sections,
            local.Numbers, local.Toggles, local.Selections, "") });
    }

    private void EventPotionInputs(Context c, EditorParts parts, PredictorEventNodeKnowledge node)
    {
        if (node.Event?.Entry == "ENDLESS_CONVEYOR") { ConveyorInputs(c, parts, node); return; }
        if (node.PotionInputRequirement == PredictorEventPotionRequirement.None) return;
        // The requirement may come from a possible occurrence, before an event
        // can actually be entered. A candidate is not an authored event identity.
        if (node.Event is not { } key)
        {
            parts.Sections.Add(new(M("event_potions.title"), M("event_potions.event_required"), [], []));
            return;
        }
        bool authored = node.PotionInput != null || EventPlanApplies(c, node) && c.Plan.Event!.Potions != null;
        bool unresolved = node.PendingCauses.Concat(node.OccurrencePendingCauses)
            .Concat(node.Options.SelectMany(option => option.PendingCauses.Concat(option.IdentityPendingCauses).Concat(option.AvailabilityPendingCauses)))
            .Concat(c.Node.Inventory?.PotionPendingCauses ?? [])
            .Any(cause => (cause.AffectedDomains & PredictorCombatDomain.PotionInventory) != 0);
        if (!authored && !unresolved) return;
        ChooseEventOccurrence? occurrence = node.Occurrence is { } observed && node.SelectedOccurrence is { } selected
            ? new(selected.Event, selected.QueueEvent, selected.RawOrdinal, observed.Cursor, selected.Cursor) : null;
        var draftKey = (_session.Snapshot?.Run.Context.Seed ?? "", c.Node.Position.NodeId, key, occurrence);
        var inventory = IncomingPotionInventory(c.Node);
        int? capacity = IncomingPotionCapacity(inventory);
        if (!_eventPotionDrafts.TryGetValue(draftKey, out var draft))
        {
            draft = new();
            var supplied = EventPlanApplies(c, node) ? c.Plan.Event!.Potions : node.PotionInput;
            if (supplied != null)
            {
                draft.EmptySlots = supplied.EmptySlots ?? 0;
                if (!supplied.OrderedSlots.IsDefault)
                {
                    draft.Bottles.AddRange(supplied.OrderedSlots);
                    draft.RetainedEmptySlots = supplied.OrderedSlots.Select((potion, index) => (potion, index))
                        .Where(slot => slot.potion == null).Select(slot => slot.index).ToHashSet();
                }
            }
            else if (inventory is { PotionPendingCauses.IsEmpty: true } && inventory.PotionSlots.All(slot => slot.IsKnown))
            {
                draft.EmptySlots = inventory.PotionSlots.Count(slot => slot.Value == null);
                draft.Bottles.AddRange(inventory.PotionSlots.Where(slot => slot.Value != null).Select(slot => (ModelKey?)slot.Value!.Key));
            }
            _eventPotionDrafts[draftKey] = draft;
        }
        var local = new EditorParts();
        bool countOnly = node.PotionInputRequirement == PredictorEventPotionRequirement.EmptySlots;
        bool overCapacity = capacity is { } maximum && (countOnly ? draft.EmptySlots : draft.Bottles.Count) > maximum;
        if (overCapacity)
            local.Sections.Add(new("", _text.Format("predictor.map.event_potions.capacity_mismatch",
                countOnly ? draft.EmptySlots : draft.Bottles.Count, capacity), [], countOnly ?
                [Choice(c, M("event_potions.use_capacity"), _ => { draft.EmptySlots = capacity!.Value; return true; })] : []));
        if (countOnly)
            local.Numbers.Add(new(Field(c, (_, value) =>
            {
                if (!Integer(value.Number, out int count) || count < 0 || capacity is { } limit && count > limit) return false;
                draft.EmptySlots = count; return true;
            }), M("event_potions.empty_count"), Math.Min(draft.EmptySlots, capacity ?? int.MaxValue), 0, capacity ?? int.MaxValue));
        else
        {
            var obtainable = EventIncomingPotionCandidates(c, inventory).ToHashSet();
            var pool = obtainable
                .Concat(draft.Bottles.OfType<ModelKey>()).Distinct().OrderBy(potion => _names.Resolve(potion, GameContentKind.Potion), StringComparer.Ordinal).ToArray();
            for (int index = 0; index < draft.Bottles.Count; index++)
            {
                int row = index;
                local.Selections.Add(new(Field(c, (_, value) =>
                {
                    var chosen = pool.FirstOrDefault(potion => potion.Serialized == value.Selected);
                    if (!chosen.IsValid) return false;
                    draft.Bottles[row] = chosen; draft.RetainedEmptySlots.Remove(row); return true;
                }), _text.Format("predictor.map.event_potions.bottle", row + 1), draft.Bottles[row]?.Serialized ??
                    (draft.RetainedEmptySlots.Contains(row) ? "retained-empty" : null),
                    pool.Select(potion => new InRunMapDetailSelectOption(potion.Serialized, obtainable.Contains(potion)
                        ? _names.Resolve(potion, GameContentKind.Potion) : _text.Format("predictor.map.event_potions.saved_hypothesis",
                            _names.Resolve(potion, GameContentKind.Potion))))
                        .Concat(draft.RetainedEmptySlots.Contains(row) ? [new InRunMapDetailSelectOption("retained-empty", M("event_potions.retained_empty"))] : []).ToArray()));
            }
            var edits = new List<InRunMapDetailChoice>
            {
                Choice(c, M("event_potions.add"), _ =>
                {
                    if (capacity is { } limit && draft.Bottles.Count >= limit) return false;
                    draft.Bottles.Add(null); return true;
                }, enabled: pool.Length > 0 && (capacity is not { } knownCapacity || draft.Bottles.Count < knownCapacity))
            };
            edits.AddRange(draft.Bottles.Select((_, index) => Choice(c, _text.Format("predictor.map.event_potions.remove", index + 1),
                _ =>
                {
                    draft.Bottles.RemoveAt(index);
                    draft.RetainedEmptySlots = draft.RetainedEmptySlots.Where(slot => slot != index)
                        .Select(slot => slot > index ? slot - 1 : slot).ToHashSet();
                    return true;
                })));
            local.Sections.Add(new("", draft.Bottles.Count == 0 ? M("event_potions.no_bottles") : "", [], edits));
        }
        local.Sections.Add(new("", "", [], [Choice(c, R("confirm"), context =>
        {
            var plan = EventPlanApplies(context, node) ? context.Plan.Event! : new PredictorPlannedEvent(key);
            var input = countOnly ? new PredictorEventPotionInput(draft.EmptySlots) :
                new PredictorEventPotionInput { OrderedSlots = draft.Bottles.ToImmutableArray() };
            return Set(context, context.Plan with { Event = plan with { Potions = input } });
        }, enabled: !overCapacity && (countOnly || draft.Bottles.Select((potion, index) => potion != null || draft.RetainedEmptySlots.Contains(index)).All(valid => valid)))]));
        parts.Sections.Add(new(M("event_potions.title"), M(countOnly ? "event_potions.empty_hint" : "event_potions.ordered_hint"), [], [])
        { EmbeddedEditor = new(c.Node.Position.NodeId, M("event_potions.title"), local.Sections,
            local.Numbers, local.Toggles, local.Selections, "") });
    }

    private static PredictorRouteInventoryKnowledge? IncomingPotionInventory(PredictorRouteNodeKnowledge node) =>
        node.PotionBoundaries.FirstOrDefault(boundary => boundary.Stage == PredictorRoutePotionStage.BeforeChoices)?.Before ??
        (node.Event?.AppliedActions == 0 && node.Inventory?.Phase == PredictorPhase.EventChoice ? node.Inventory : null);

    private int? IncomingPotionCapacity(PredictorRouteInventoryKnowledge? incoming)
    {
        if (incoming?.PotionCapacity is { } known) return known;
        // A captured capacity includes existing native/Mod slot effects. Only
        // a fully known incoming relic inventory can establish the additional
        // vanilla capacity change; an unresolved belt is not a fixed maximum.
        if (incoming is not { RelicPendingCauses.IsEmpty: true } || _session.Snapshot?.Run.Working is not { } captured) return null;
        int AddedCapacity(IEnumerable<PredictorRelic> relics) => relics.Where(relic => !relic.Melted)
            .Sum(relic => relic.Key.Entry switch { "POTION_BELT" => 2, "PHIAL_HOLSTER" => 1, _ => 0 });
        return Math.Max(0, captured.PotionSlots.Length + AddedCapacity(incoming.Relics.Where(relic => relic.IsKnown).Select(relic => relic.Value)) - AddedCapacity(captured.Relics));
    }

    private IEnumerable<ModelKey> EventIncomingPotionCandidates(Context context, PredictorRouteInventoryKnowledge? incoming)
    {
        foreach (var potion in _session.Snapshot?.Run.Working.PotionSlots.OfType<PredictorPotion>() ?? []) yield return potion.Key;
        foreach (var slot in incoming?.PotionSlots ?? [])
            if (slot.IsKnown && slot.Value is { } potion) yield return potion.Key;
        // These are possible prior receipts, not one simultaneously owned
        // inventory. The local explicit list remains the player's hypothesis.
        foreach (var prior in _session.Nodes.Where(pair => pair.Key.ActIndex == context.Location.ActIndex &&
            pair.Value.Position.Floor < context.Node.Position.Floor).Select(pair => pair.Value))
        {
            foreach (var slot in prior.PotionBoundaries.SelectMany(boundary => boundary.Before.PotionSlots.Concat(boundary.After.PotionSlots)))
                if (slot.IsKnown && slot.Value is { } potion) yield return potion.Key;
            foreach (var potion in RewardPotions(prior.Rewards)) yield return potion;
            foreach (var child in prior.Ancient?.RequiredChoices ?? [])
                foreach (var potion in ChildPotions(child)) yield return potion;
            foreach (var child in prior.Treasure?.RequiredChoices ?? [])
                foreach (var potion in ChildPotions(child)) yield return potion;
            if (prior.Shop is { } shop)
            {
                foreach (var offer in shop.Offers)
                    if (offer.IsKnown && offer.Affordable != false && offer.Value is
                        { Kind: PredictorShopEntryKind.Potion, Stocked: true, Key: { } potion }) yield return potion;
                foreach (var child in shop.RequiredChoices)
                    foreach (var potion in ChildPotions(child)) yield return potion;
            }
            if (prior.Event is not { } eventNode) continue;
            foreach (var potion in RewardPotions(eventNode.Rewards)) yield return potion;
            foreach (var child in eventNode.RequiredChoices)
                foreach (var potion in ChildPotions(child)) yield return potion;
            foreach (var option in eventNode.EntryOptions.Concat(eventNode.Options))
                foreach (var potion in OptionPotions(option)) yield return potion;
        }

        static IEnumerable<ModelKey> RewardPotions(IEnumerable<PredictorCombatRewardKnowledge> rewards)
        {
            foreach (var reward in rewards)
            {
                if (reward.IsKnown && reward.Value is { Kind: PredictorRewardKind.Potion } potion) yield return potion.Key;
                if (reward.RequiredChoices is { } child)
                    foreach (var key in ChildPotions(child)) yield return key;
            }
        }
        static IEnumerable<ModelKey> ChildPotions(PredictorObtainChoiceKnowledge child) => RewardPotions(child.Rewards);
        static IEnumerable<ModelKey> OptionPotions(PredictorEventOptionKnowledge option)
        {
            if (option.Enabled == false || !option.IdentityPendingCauses.IsEmpty || option.ResultPreview is not { } result) yield break;
            foreach (var key in RewardPotions(result.Rewards)) yield return key;
            foreach (var child in result.RequiredChoices)
                foreach (var key in ChildPotions(child)) yield return key;
            foreach (var nested in result.Options)
                foreach (var key in OptionPotions(nested)) yield return key;
        }
    }
}
