using System.Collections.Immutable;
using System.Text.Json.Serialization;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorEventPotionRequirement { None, EmptySlots, OrderedSlots }
// An ordered event-local observation can come from capture or a player hypothesis.
// Default OrderedSlots means identities were not supplied; [] proves empty.
internal sealed record PredictorEventPotionInput(int? EmptySlots = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<ModelKey?> OrderedSlots { get; init; }

    // Actual combat offers that could support an explicit local holding. A
    // known slot checkpoint or a later use rules out an older unclaimed drop.
    internal static (string NodeId, ModelKey Potion)[] RecentCombatDrops(
        IEnumerable<PredictorRouteNodeKnowledge> precedingNodes, Func<string, PredictorPlannedNode?> planFor, int required)
    {
        var drops = new List<(string NodeId, ModelKey Potion)>();
        foreach (var node in precedingNodes.OrderBy(n => n.Position.Floor))
        {
            if (node.PotionBoundaries.Any(boundary => boundary.Before.PotionPendingCauses.IsEmpty)) drops.Clear();
            var plan = planFor(node.Position.NodeId);
            if (plan != null && (!plan.PotionActions.IsEmpty || plan.Event?.Actions.Any(a =>
                    a.ExpectedOption?.PotionId != null || a.ExpectedOption?.LocalPotionKey != null) == true))
            {
                drops.Clear();
                continue;
            }
            if (node.Room is not (PredictorRoomKind.Monster or PredictorRoomKind.Elite or PredictorRoomKind.Boss)) continue;
            foreach (var reward in node.Rewards)
                if (reward.Kind == PredictorRewardKind.Potion && reward.IsKnown && !reward.Value!.PotionReceiptDenied && reward.AppliedChoice?.Skip != true)
                    drops.Add((node.Position.NodeId, reward.Value!.Key));
        }
        return drops.TakeLast(required).ToArray();
    }
}

internal sealed partial class PredictorRun
{
    private PredictorEventPotionInput? _eventPotionInput;
    private ImmutableArray<PredictorPotion?> _eventLocalPotionSlots;
    private static PredictorEventPotionRequirement EventPotionRequirement(ModelKey? key) => key?.Entry switch
    {
        "ENDLESS_CONVEYOR" => PredictorEventPotionRequirement.EmptySlots,
        "THE_FUTURE_OF_POTIONS" or "STONE_OF_ALL_TIME" or "RANWID_THE_ELDER" or "FAKE_MERCHANT" => PredictorEventPotionRequirement.OrderedSlots,
        _ => PredictorEventPotionRequirement.None
    };
    private bool UsesCapturedEventPotions(ModelKey? key) =>
        key?.Entry == "THE_FUTURE_OF_POTIONS" && _preview.Settings.CapturedPotions != null;

    private PredictorEventPotionInput? RouteEventPotionInput(ModelKey key)
    {
        if (UsesCapturedEventPotions(key)) return _preview.Settings.CapturedPotions;
        if (key.Entry == "ENDLESS_CONVEYOR" && _eventKey == key && _conveyorInput != null)
            return _eventPotionInput;
        if (!_preview.Settings.PotionPlans.TryGetValue(Working.Position.NodeId, out var plan) || plan.ExpectedEvent != key) return null;
        if (plan.Occurrence == null && plan.ExpectedOccurrence is { } receipt)
        {
            var actual = _nodeReplayInputs.OfType<ChooseEventOccurrence>().LastOrDefault();
            if (actual != null) { if (actual != receipt) return null; }
            else
            {
                var observation = ObserveEventOccurrence();
                if (observation != null)
                {
                    if (observation.Cursor != receipt.ExpectedCursor || !observation.Candidates.Any(c =>
                        c.Event == receipt.Event && c.QueueEvent == receipt.QueueEvent && c.RawOrdinal == receipt.RawOrdinal &&
                        c.Cursor == receipt.SelectedCursor)) return null;
                }
                else
                {
                    // FinishEventOccurrence advances the queue immediately
                    // before BeginEvent; Submit records its replay input after
                    // setup returns. Verify that in-flight receipt here too.
                    var act = Working.Acts.Single(a => a.Act == Working.Position.Act);
                    if (act.EventCursor != receipt.SelectedCursor + 1 || act.RawEvents.IsEmpty ||
                        act.RawEvents[receipt.SelectedCursor % act.RawEvents.Length] != receipt.QueueEvent ||
                        Working.Visits.LastOrDefault()?.Model != receipt.Event) return null;
                }
            }
        }
        return plan.Potions;
    }
    private void BeginEventPotionInput(ModelKey key)
    {
        _eventPotionInput = RouteEventPotionInput(key);
        _eventLocalPotionSlots = _eventPotionInput is { OrderedSlots.IsDefault: false } input
            ? input.OrderedSlots.Select((potion, ordinal) => potion is { } model
                ? new PredictorPotion(-(ordinal + 1L), model) : null).ToImmutableArray() : default;
    }
    private bool HasLocalEventPotionIdentities => !_eventLocalPotionSlots.IsDefault;
    private ImmutableArray<PredictorPotion?> EventPotionSlots =>
        HasLocalEventPotionIdentities ? _eventLocalPotionSlots : Working.PotionSlots;
    private bool EventHasEmptyPotionSlot => _eventPotionInput?.EmptySlots is { } count ? count > 0 :
        EventPotionSlots.Any(potion => potion == null);
    private void SetEventPotionSlot(int slot, PredictorPotion? potion)
    {
        if (HasLocalEventPotionIdentities) _eventLocalPotionSlots = _eventLocalPotionSlots.SetItem(slot, potion);
        else Working = Working with { PotionSlots = Working.PotionSlots.SetItem(slot, potion) };
    }
    private static PredictorEventOption EventPotionOption(string key, PredictorPotion potion, EffectCardType? type = null) =>
        potion.Id > 0 ? new(key, PotionId: potion.Id, CardType: type) :
            new(key, CardType: type, LocalPotionOrdinal: checked((int)(-potion.Id - 1)), LocalPotionKey: potion.Key);
    private static long EventPotionToken(PredictorEventOption option) => option.LocalPotionOrdinal is { } ordinal
        ? -(ordinal + 1L) : option.PotionId ?? throw new InvalidOperationException("PredictorEventPotionTargetMissing");
    private static bool EventLocalPotionReadKnown(PredictorRun run, ModelKey key, bool emptySlotOnly) =>
        run.RouteEventPotionInput(key) is { } input &&
        (!input.OrderedSlots.IsDefault || emptySlotOnly && key.Entry == "ENDLESS_CONVEYOR" && input.EmptySlots.HasValue);
    // A count proves only the empty-slot predicate at an explicitly identified
    // pool read. It cannot establish bottle membership or death protection.
    private static PredictorCombatDomain RefineEventPotionReads(PredictorRun run, PredictorCombatDomain reads,
        bool emptySlotOnly = false) =>
        run._eventKey is { } key && EventLocalPotionReadKnown(run, key, emptySlotOnly)
            ? reads & ~PredictorCombatDomain.PotionInventory : reads;
    private PredictorPotionRarity? FuturePotionRarity(ModelKey key)
    {
        var catalog = Context.PotionPool.FirstOrDefault(potion => potion.Key == key);
        if (catalog != null) return catalog.Rarity;
        // The event accepts held event/token bottles as well as ordinary drops.
        // These vanilla identities live outside the normal potion factory pool.
        return key.Entry switch
        {
            "AMBERGRIS" or "FOUL_POTION" or "GLOWWATER_POTION" => PredictorPotionRarity.Event,
            "POTION_SHAPED_ROCK" => PredictorPotionRarity.Token,
            _ => null
        };
    }

    private ImmutableArray<ModelKey> MissingLocalEventPotionRarities(ModelKey key)
    {
        if (key.Entry != "THE_FUTURE_OF_POTIONS") return [];
        var potions = RouteEventPotionInput(key) is { OrderedSlots.IsDefault: false } input
            ? input.OrderedSlots.OfType<ModelKey>() : EventPotionSlots.OfType<PredictorPotion>().Select(potion => potion.Key);
        return potions.Where(model => FuturePotionRarity(model) is null or PredictorPotionRarity.None).Distinct().ToImmutableArray();
    }
    private bool RequireLocalEventPotionRarities(ModelKey key, List<PredictorCombatPendingCause> local)
    {
        var missing = MissingLocalEventPotionRarities(key);
        if (missing.IsEmpty) return true;
        local.Add(PredictorRouteCauseEvidence.CreateExplicit("EventLocalPotionRarityUnavailable", Working.Position.NodeId,
            PredictorCombatDomain.Continuation, PredictorPendingReasonKind.ImplementationGap,
            "Future of Potions reads each held or declared bottle's rarity before its event-local type draw. The current potion catalog has no rarity for: " +
                string.Join(" | ", missing.Select(model => model.Serialized)) + ". The bottle identities are retained.",
            missing.Select(model => "event.localPotion.rarity." + model.Serialized).ToImmutableArray(),
            occurrenceKey: key.Serialized));
        return false;
    }
    private static void ValidateEventPotionInput(PredictorPlannedEvent plan)
    {
        if (plan.Potions is not { } input) return;
        RequirePlan(EventPotionRequirement(plan.ExpectedEvent) != PredictorEventPotionRequirement.None &&
            input.EmptySlots is not < 0 && (!input.OrderedSlots.IsDefault || input.EmptySlots.HasValue) &&
            (input.OrderedSlots.IsDefault || input.OrderedSlots.All(key => key == null || key.Value.IsValid && key.Value.Category == "POTION")) &&
            (input.OrderedSlots.IsDefault || input.EmptySlots == null || input.EmptySlots == input.OrderedSlots.Count(key => key == null)));
        RequirePlan(EventPotionRequirement(plan.ExpectedEvent) != PredictorEventPotionRequirement.OrderedSlots || !input.OrderedSlots.IsDefault);
    }
}
