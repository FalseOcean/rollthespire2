using System.Collections.Immutable;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed partial class RoutePartialState
    {
        private readonly Dictionary<string, PredictorKnowledgeFactId> _roomResolution = new(StringComparer.Ordinal);

        internal MapRoomTrace BeginRoomResolution(PredictorRun run, MapPredictionPoint point, PredictorActMap map) =>
            new(this, run, point, map);

        internal ImmutableArray<PredictorKnowledgeFactId> RoomResolutionReads(string node) =>
            _roomResolution.TryGetValue(node, out var id) ? [id] : [];

        internal sealed class MapRoomTrace
        {
            private readonly RoutePartialState _owner;
            private readonly PredictorRun _run;
            private readonly string _node;
            private readonly List<PredictorKnowledgeFactId> _reads = [];
            private readonly PredictorStreamState _before;
            private PredictorKnowledgeFactId? _incomingCursor;
            private bool _drew;
            private readonly int _act;

            internal MapRoomTrace(RoutePartialState owner, PredictorRun run, MapPredictionPoint point, PredictorActMap map)
            {
                _owner = owner; _run = run;
                _act = map.Act;
                _node = $"map:{map.Act}:{point.Position.Row}:{point.Position.Column}:{run.Working.Position.Floor + 1}";
                _before = run.Working.Streams.Single(s => s.Stream == PredictorStream.UnknownMapPoint);
                Read("pointType", point.Type);
            }

            private PredictorKnowledgeFactId Read<T>(string key, T value,
                ImmutableArray<PredictorCombatPendingCause> causes = default,
                ImmutableArray<PredictorKnowledgeFactId> reads = default)
            {
                var id = _owner.Facts.RecordResult("map.resolve.read." + key, _node, value, causes,
                    operation: "ResolveRoom read " + key, allReadFactIds: reads);
                _reads.Add(id); return id;
            }

            internal void ReadTutorial(int priorUnknowns)
            {
                Read("numberOfRuns", _run.Context.NumberOfRuns);
                Read("priorUnknownRoomCount", priorUnknowns);
            }

            internal void ReadForced(bool forced)
            {
                var inputs = ImmutableArray.CreateBuilder<PredictorKnowledgeFactId>();
                bool lantern = _act == 3 && _run.Working.Deck.Any(id => _run.Working.Card(id).Key.Entry == "LANTERN_KEY");
                if (_act == 3)
                    inputs.Add(Read("cards.LANTERN_KEY.membership", lantern, PredictorRouteCauseEvidence.Normalize(
                        _owner._dependencies.Where(d => RouteCardMayVary(_run, [d], "LANTERN_KEY")).Select(d => d.Cause))));
                // A proven Lantern fixes this OR predicate without reading Compass.
                if (!lantern)
                {
                    bool compass = PredictorSettlementEffects.Has(_run.Working, "GOLDEN_COMPASS");
                    inputs.Add(Read("relics.GOLDEN_COMPASS.membership", compass,
                        RouteRelicMembershipCauses(_run, _owner._dependencies, "GOLDEN_COMPASS")));
                    if (compass)
                        inputs.Add(Read("relics.GOLDEN_COMPASS.goldenPathAct", string.Join(" | ", _run.Working.Relics
                            .Where(r => !r.Melted && r.Key.Entry == "GOLDEN_COMPASS").Select(r => r.GoldenPathAct)),
                            RouteRelicStateCauses(_run, _owner._dependencies, "GOLDEN_COMPASS")));
                }
                Read("forcedEventPredicate", forced, reads: inputs.ToImmutable());
            }

            internal void ReadRandom(bool blockShop, bool blockMonster, PredictorUnknownOdds odds)
            {
                Read("shopBlockedByGeometryOrPreviousRoom", blockShop);
                Read("juzuBlocksMonster", blockMonster,
                    RouteRelicMembershipCauses(_run, _owner._dependencies, "JUZU_BRACELET"));
                Read("odds.unknownRoom", FormattableString.Invariant($"monster={odds.Monster}; elite={odds.Elite}; treasure={odds.Treasure}; shop={odds.Shop}"),
                    _owner.Domain(PredictorCombatDomain.Map));
                Read("deadlyEventsModifier", _run.Context.HasRunModifier("DEADLY_EVENTS"));
            }

            internal void ReadCursor()
            {
                _drew = true;
                _incomingCursor = Read("rng.UnknownMapPoint.calls", _before.Calls,
                    _owner.StreamCauses(PredictorStream.UnknownMapPoint));
            }

            internal PredictorRoomKind Complete(PredictorRoomKind room, string branch)
            {
                var reads = _reads.ToImmutableArray();
                var id = _owner.Facts.RecordResult("map.resolve.room", _node, room,
                    operation: "ResolveRoom / " + branch, allReadFactIds: reads,
                    derivation: branch == "forced Event"
                        ? "The proven forced-event predicate fixes the room; the draw is consumed but does not select the room."
                        : "The route entry guard established the inputs used by this executed branch.");
                _owner._roomResolution[_node] = id;
                int calls = _run.Working.Streams.Single(s => s.Stream == PredictorStream.UnknownMapPoint).Calls;
                var consumed = _owner.Facts.RecordResult("map.resolve.rng.UnknownMapPoint.consumed", _node,
                    calls - _before.Calls, operation: "ResolveRoom draw count", allReadFactIds: reads,
                    derivation: _drew ? "This executed branch makes exactly one NextFloat call regardless of its value." :
                        "This typed/tutorial branch returns before any RNG draw.");
                if (_incomingCursor is { } cursor)
                {
                    var finalCursor = _owner.Facts.RecordResult("map.resolve.rng.UnknownMapPoint.callsAfter", _node, calls,
                        _owner.StreamCauses(PredictorStream.UnknownMapPoint), operation: "ResolveRoom cursor write",
                        allReadFactIds: [cursor, consumed]);
                    _owner.RecordStateWrite(_node, "rng.UnknownMapPoint.calls", finalCursor);
                }
                return room;
            }
        }
    }
}
