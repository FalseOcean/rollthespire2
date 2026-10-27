using System.Collections.Immutable;
using System.Globalization;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed record RoutePropertyObservation(string Key, string? Value,
        ImmutableArray<PredictorCombatPendingCause> Causes, string? Partial = null);

    private sealed partial class RoutePartialState
    {
        // Capture before entering the next room. Publishing it after entry
        // binds the frame to that room without reading its post-entry witness.
        internal ImmutableArray<RoutePropertyObservation> Capture(PredictorState state)
        {
            Synchronize(state);
            var values = ImmutableArray.CreateBuilder<RoutePropertyObservation>();
            void Value(string key, string value, PredictorCombatDomain reads = PredictorCombatDomain.None) =>
                values.Add(new(key, value, Domain(reads)));
            var gold = EventGoldBalance(state);
            values.Add(new("gold.balance", gold?.KnownValue?.ToString(CultureInfo.InvariantCulture) ??
                (Domain(PredictorCombatDomain.Gold).IsEmpty ? state.GoldBudget.ToString(CultureInfo.InvariantCulture) : null),
                gold?.KnownValue != null ? [] : Domain(PredictorCombatDomain.Gold),
                gold is { KnownValue: null } ? Range(gold) : null));
            values.Add(new("odds.cardRarity", state.Odds.CardRarityOffset.ToString(CultureInfo.InvariantCulture), CombatOddsCauses("odds.cardRarity")));
            values.Add(new("odds.potionDrop", state.Odds.PotionDropChance.ToString(CultureInfo.InvariantCulture), CombatOddsCauses("odds.potionDrop")));
            Value("odds.unknownRoom", FormattableString.Invariant($"monster={state.UnknownOdds.Monster}; elite={state.UnknownOdds.Elite}; treasure={state.UnknownOdds.Treasure}; shop={state.UnknownOdds.Shop}"), PredictorCombatDomain.Map);
            Value("potions.capacity", state.PotionSlots.Length.ToString(CultureInfo.InvariantCulture), PredictorCombatDomain.Relics);
            Value("potions.orderedSlots", string.Join(" | ", state.PotionSlots.Select((p, i) => i + ":" + (p?.Key.Serialized ?? "empty"))),
                PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Relics);
            Value("relics.orderedInventory", string.Join(" | ", state.Relics.Where(r => !r.Melted).Select(r => r.Key.Serialized)), PredictorCombatDomain.Relics);
            var counterCauses = Domain(PredictorCombatDomain.Relics).Where(c => c.Code != "FirstPlayerTurn").ToImmutableArray();
            values.Add(new("relics.girya.timesLifted", state.Relics.FirstOrDefault(r => !r.Melted && r.Key.Entry == "GIRYA")?.TimesLifted
                .ToString(CultureInfo.InvariantCulture) ?? "none", counterCauses));
            values.Add(new("relics.pumpkinCandle.kindleCount", state.Relics.FirstOrDefault(r => !r.Melted && r.Key.Entry == "PUMPKIN_CANDLE")?.KindleCount
                .ToString(CultureInfo.InvariantCulture) ?? "none", counterCauses));
            values.Add(new("relics.silverCrucible.orderedTreasureRoomCounters", string.Join(" | ", state.Relics
                .Where(r => !r.Melted && r.Key.Entry == "SILVER_CRUCIBLE").Select(r => r.TreasureRoomsEntered.ToString(CultureInfo.InvariantCulture))), counterCauses));
            foreach (var relic in state.Relics.Where(r => !r.Melted))
            {
                // Counts are distinct facts; e.g. a pending first player turn
                // changes Bone Tea, not an unrelated relic's counter.
                var causes = Domain(PredictorCombatDomain.Relics).Where(c => c.Code != "FirstPlayerTurn" || relic.Key.Entry == "BONE_TEA").ToImmutableArray();
                values.Add(new("relic." + relic.Id + ".combatsFinished", relic.CombatsFinished.ToString(CultureInfo.InvariantCulture),
                    causes.Where(c => c.Code != "FirstPlayerTurn").ToImmutableArray()));
                values.Add(new("relic." + relic.Id + ".combatsLeft", relic.CombatsLeft.ToString(CultureInfo.InvariantCulture), causes));
                if (relic.Key.Entry == "BOOK_OF_FIVE_RINGS")
                {
                    var added = Deck.ProjectBookCounterDelta(relic.Id);
                    var bookCauses = causes.AddRange(Deck.Choices.Where(c => c.Effects.Any(e =>
                        !e.AdditionsKnown || e.BookCounterDeltas.Any(d => d.RelicId == relic.Id && d.CardsAdded != 0)))
                        .Where(c => _choices.ContainsKey(c.ChoiceId)).Select(c => _choices[c.ChoiceId]));
                    var count = added == null ? null : added + relic.BookOfFiveRingsCardsAdded;
                    values.Add(new("relic." + relic.Id + ".cardsAdded", count?.KnownValue?.ToString(CultureInfo.InvariantCulture),
                        count?.KnownValue != null ? causes : bookCauses,
                        count is { KnownValue: null } ? Range(count) : null));
                }
                if (relic.Key.Entry == "PAELS_WING")
                    values.Add(new("relic." + relic.Id + ".rewardsSacrificed", relic.RewardsSacrificed.ToString(CultureInfo.InvariantCulture),
                        causes.AddRange(Domain(PredictorCombatDomain.RewardAlternativeState))));
                if (relic.Key.Entry == "SILVER_CRUCIBLE")
                    values.Add(new("relic." + relic.Id + ".rewardUpgradeUses", relic.RewardUpgradeUses.ToString(CultureInfo.InvariantCulture),
                        causes.AddRange(Domain(PredictorCombatDomain.CardRewardHookState))));
                if (relic.Key.Entry == "SILKEN_TRESS")
                    values.Add(new("relic." + relic.Id + ".silkenUsed", relic.SilkenUsed.ToString(),
                        causes.AddRange(Domain(PredictorCombatDomain.CardRewardHookState))));
                if (relic.Key.Entry == "FISHING_ROD")
                    values.Add(new("relic." + relic.Id + ".fishingCombats", relic.FishingRodCombatsSeen.ToString(CultureInfo.InvariantCulture), causes));
            }
            values.Add(new("bags.personal", Bag(state.PersonalBag), RouteRelicBagCauses(_dependencies, shared: false)));
            values.Add(new("bags.shared", Bag(state.SharedBag), RouteRelicBagCauses(_dependencies, shared: true)));
            foreach (var act in state.Acts)
            {
                Value($"queue.act{act.Act}.normal", act.NormalCursor + ": " + Keys(act.NormalEncounters));
                Value($"queue.act{act.Act}.elite", act.EliteCursor + ": " + Keys(act.EliteEncounters));
                Value($"queue.act{act.Act}.boss", act.BossCursor + ": " + Keys(act.Bosses));
                Value($"queue.act{act.Act}.events", act.EventCursor + ": " + Keys(act.RawEvents), PredictorCombatDomain.Map);
            }
            foreach (var scope in Enum.GetValues<PredictorDeckProjectionScope>())
            {
                var projection = Deck.Project(scope);
                string key = DeckKey(scope);
                var countCauses = ProjectionCauses(projection, count: true);
                var candidateCauses = ProjectionCauses(projection, count: false);
                values.Add(new(key + ".count", projection.KnownCount?.ToString(CultureInfo.InvariantCulture),
                    projection.KnownCount.HasValue ? [] : countCauses,
                    projection.Count is { KnownValue: null } count ? Range(count) : null));
                values.Add(new(key + ".candidates", projection.OrderedCandidatesKnown ?
                    string.Join(" | ", projection.KnownCandidates.Select(CardDiagnostic)) : null,
                    projection.OrderedCandidatesKnown ? [] : candidateCauses,
                    !projection.OrderedCandidatesKnown && !projection.KnownCandidates.IsEmpty ?
                        string.Join(" | ", projection.KnownCandidates.Select(CardDiagnostic)) : null));
            }
            foreach (var stream in state.Streams)
            {
                var causes = StreamCauses(stream.Stream);
                var progress = _streamOffsets[stream.Stream] + _streamBases[stream.Stream].Calls;
                string key = "rng." + stream.Stream;
                values.Add(new(key + ".calls", progress.KnownValue?.ToString(CultureInfo.InvariantCulture), causes,
                    progress.KnownValue == null && _streamCauses.GetValueOrDefault(stream.Stream, []).IsEmpty &&
                        ExternalStreamCauses(stream.Stream).IsEmpty ? Range(progress) : null));
                Value(key + ".capturedBaseCalls", _streamBases[stream.Stream].Calls.ToString(CultureInfo.InvariantCulture));
                values.Add(new(key + ".state", StreamKnown(stream.Stream) ?
                    $"{stream.S0:X16}:{stream.S1:X16}:{stream.S2:X16}:{stream.S3:X16}" : null, causes));
            }
            return values.ToImmutable();
        }

        internal void RecordBoundary(string nodeId, PredictorRouteFactPhase phase,
            ImmutableArray<RoutePropertyObservation> observations, PredictorKnowledgeOrigin origin)
        {
            foreach (var observation in observations)
            {
                var reads = _latest.TryGetValue(observation.Key, out var previous) ? ImmutableArray.Create(previous) : [];
                var writes = phase == PredictorRouteFactPhase.After && _stateWrites.TryGetValue((nodeId, observation.Key), out var written)
                    ? ImmutableArray.Create(written) : [];
                reads = reads.AddRange(writes).Distinct().ToImmutableArray();
                var unresolvedWrites = writes.Where(factId => Facts.Fact(factId).Status != PredictorKnowledgeStatus.Known).ToImmutableArray();
                PredictorKnowledgeFactId id;
                if (observation.Partial != null && !observation.Causes.IsEmpty)
                    id = Facts.RecordPartial(observation.Key, nodeId, phase, observation.Partial, observation.Causes,
                        [new("interpretation", observation.Key.EndsWith(".candidates", StringComparison.Ordinal)
                            ? "Guaranteed subset in deck order; not a complete candidate set."
                            : "Conservative enclosure; endpoints are not necessarily reachable together.")],
                        origin: origin, operation: "observe " + observation.Key, allReadFactIds: reads,
                        unresolvedReadFactIds: unresolvedWrites);
                else if (observation.Value != null)
                    id = Facts.RecordValue(observation.Key, nodeId, phase, observation.Value, observation.Causes,
                        origin, "observe " + observation.Key, reads,
                        unresolvedReadFactIds: observation.Causes.IsEmpty ? [] : unresolvedWrites,
                        derivation: "The property owner recomputed this projection from the retained correlated choices.");
                else
                {
                    var causes = observation.Causes;
                    if (causes.IsEmpty)
                        causes = [PredictorRouteCauseEvidence.CreateExplicit("MissingPropertyDerivation", nodeId,
                            PredictorCombatDomain.None, PredictorPendingReasonKind.ImplementationGap,
                            "The property owner supplied neither a proven value nor a dependency.", [observation.Key])];
                    id = Facts.RecordUnknown(observation.Key, nodeId, phase, causes,
                        origin: origin, operation: "observe " + observation.Key, allReadFactIds: reads,
                        unresolvedReadFactIds: unresolvedWrites);
                }
                _latest[observation.Key] = id;
            }
        }

        internal void RecordResults(PredictorRouteNodeKnowledge node)
        {
            Facts.RecordResult("result.room.kind", node.Position.NodeId, node.Room,
                operation: "published room identity", allReadFactIds: RoomResolutionReads(node.Position.NodeId));
            RecordRoomResults(node);
            for (int i = 0; i < node.Rewards.Length; i++)
            {
                var reward = node.Rewards[i];
                var value = reward.IsKnown ? RewardText(reward.Value!) : null;
                var reads = CombatResultReads(node.Position.NodeId, reward);
                if (value != null)
                    Facts.RecordResult("result.reward." + i, node.Position.NodeId, value, reward.PendingCauses,
                        operation: "reward observation", allReadFactIds: reads,
                        derivation: "Final route observation applies the source-specific reward mask.");
                else Facts.RecordUnknown("result.reward." + i, node.Position.NodeId, PredictorRouteFactPhase.Results,
                    reward.PendingCauses.IsEmpty ? [PredictorRouteCauseEvidence.CreateExplicit("RewardObservationMissing",
                        node.Position.NodeId, PredictorCombatDomain.None, PredictorPendingReasonKind.ImplementationGap,
                        "No established reward observation was returned.", ["reward." + reward.Kind])] : reward.PendingCauses,
                    allReadFactIds: reads, unresolvedReadFactIds: CombatResultUnresolvedReads(node.Position.NodeId, reward));
            }
            foreach (var cause in PredictorRouteCauseEvidence.Normalize(node.PendingCauses))
                Facts.RecordUnknown("gap." + cause.Identity, node.Position.NodeId, PredictorRouteFactPhase.Results,
                    [cause], operation: "route dependency");
        }

        private static string RewardText(PredictorReward reward) => reward.Kind switch
        {
            PredictorRewardKind.Card or PredictorRewardKind.SpecialCard => string.Join(" | ", reward.Cards.Select(CardDiagnostic)),
            PredictorRewardKind.Gold => reward.GoldAmountUnspecified ? "Recovered gold: exact amount not observed" : reward.GoldAmount.ToString(CultureInfo.InvariantCulture),
            _ => reward.Key.Serialized
        };
        private static string Range(PredictorChoiceInt expression) =>
            expression.Bounds.Lower.ToString(CultureInfo.InvariantCulture) + " … " + expression.Bounds.Upper.ToString(CultureInfo.InvariantCulture);
        private static string Keys(IEnumerable<ModelKey> keys) => string.Join(" | ", keys.Select(k => k.Serialized));
        private static string Bag(PredictorRelicBag bag) => string.Join("; ", bag.Buckets.Select(b => b.Rarity + ": " + Keys(b.Entries)));
    }

    private static string CardDiagnostic(PredictorCard card) =>
        card.Key.Serialized;
}
