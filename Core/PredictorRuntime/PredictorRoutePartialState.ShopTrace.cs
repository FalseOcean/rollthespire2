using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed partial class RoutePartialState
    {
        private int _shopTraceOccurrence;
        private readonly Dictionary<(string Node, int Slot), ImmutableArray<PredictorKnowledgeFactId>> _shopResultReads = [];

        internal ImmutableArray<PredictorKnowledgeFactId> ShopResultReads(string nodeId, int slot) =>
            _shopResultReads.GetValueOrDefault((nodeId, slot), []);

        private ImmutableArray<PredictorKnowledgeFactId> ShopLatest(params string[] keys) =>
            keys.Where(_latest.ContainsKey).Select(key => _latest[key]).Distinct().ToImmutableArray();

        // This observes the already generated stock. It is not an audit of all
        // EnterShop/RestockShop generator internals or the automatic Parasol loop.
        internal void RecordShopStockObservation(PredictorRun run,
            ImmutableArray<PredictorShopOfferKnowledge> offers)
        {
            string node = run.Working.Position.NodeId;
            foreach (var offer in offers)
            {
                var reads = offer.Kind == PredictorShopEntryKind.CardRemoval ?
                    ShopLatest("relics.orderedInventory") : ShopLatest("rng.Shops.state", "relics.orderedInventory");
                if (offer.Kind is PredictorShopEntryKind.CharacterCard or PredictorShopEntryKind.ColorlessCard or PredictorShopEntryKind.Relic)
                    reads = reads.AddRange(ShopLatest("rng.Rewards.state"));
                if (offer.Kind == PredictorShopEntryKind.CharacterCard)
                    reads = reads.AddRange(ShopLatest("odds.cardRarity"));
                if (offer.Kind == PredictorShopEntryKind.Relic)
                    reads = reads.AddRange(ShopLatest("bags.personal", "bags.shared"));
                if (offer.Kind == PredictorShopEntryKind.CardRemoval)
                {
                    reads = reads.Add(Facts.RecordResult("result.shop.removalPriceInput.used", node,
                        run.Working.CardShopRemovalsUsed, operation: "shop removal price input"));
                    reads = reads.Add(Facts.RecordResult("result.shop.removalPriceInput.ascension", node,
                        run.Context.Ascension, origin: PredictorKnowledgeOrigin.Captured, operation: "shop removal price input"));
                    reads = reads.Add(Facts.RecordResult("result.shop.removalStockInput.hoarder", node,
                        run.Context.HasRunModifier("HOARDER"), origin: PredictorKnowledgeOrigin.Captured, operation: "shop removal stock input"));
                }
                string key = "result.shop.stockObservation." + offer.Slot;
                var id = offer.IsKnown ? Facts.RecordResult(key, node,
                    OfferIdentity(offer.Value!), allReadFactIds: reads,
                    operation: "shop generated-stock snapshot observation",
                    derivation: "Observed after room entry; generator internals are not instrumented here.") :
                    Facts.RecordUnknown(key, node, PredictorRouteFactPhase.Results,
                        ShopUnknown(offer.PendingCauses, node, key), allReadFactIds: reads,
                        operation: "shop generated-stock snapshot observation");
                _shopResultReads[(node, offer.Slot)] = [id];
            }
        }

        internal ShopActionTrace BeginShopAction(PredictorRun run, PredictorPlannedShopAction action, bool authored) =>
            new(this, run, action, ++_shopTraceOccurrence, authored);

        private static string OfferIdentity(PredictorShopOfferSnapshot offer) =>
            offer.Card?.Key.Serialized ?? offer.Key?.Serialized ?? "card removal";

        private static ImmutableArray<PredictorCombatPendingCause> ShopUnknown(
            ImmutableArray<PredictorCombatPendingCause> causes, string node, string property) =>
            !causes.IsDefaultOrEmpty ? causes :
            [PredictorRouteCauseEvidence.CreateExplicit("ShopReadDerivationMissing", node, PredictorCombatDomain.None,
                PredictorPendingReasonKind.ImplementationGap,
                "This shop read has no established value or source dependency.", [property], occurrenceKey: property)];

        internal sealed class ShopActionTrace
        {
            private readonly RoutePartialState _owner;
            private readonly string _node;
            private readonly string _prefix;
            private readonly List<PredictorKnowledgeFactId> _reads = [];
            private readonly Dictionary<int, PredictorKnowledgeFactId> _costReads = [];
            private readonly PredictorPlannedShopAction _action;

            internal ShopActionTrace(RoutePartialState owner, PredictorRun run, PredictorPlannedShopAction action,
                int occurrence, bool authored)
            {
                _owner = owner; _node = run.Working.Position.NodeId; _action = action;
                _prefix = "result.shop.action." + occurrence;
                var origin = authored ? PredictorKnowledgeOrigin.PlayerInput : PredictorKnowledgeOrigin.ApprovedDefault;
                _reads.Add(owner.Facts.RecordResult(_prefix + ".input.action", _node, action.Action,
                    origin: origin, operation: "shop action selection"));
                if (action.ExpectedOffer is { } expected)
                {
                    Input("slot", expected.Slot); Input("stockRevision", expected.StockRevision);
                    Input("identity", OfferIdentity(expected)); Input("quotedCost", expected.Cost);
                    Input("bindingSnapshot", System.Text.Json.JsonSerializer.Serialize(expected));
                }
                if (action.Card is { } target)
                {
                    Input("target.instanceId", target.InstanceId); Input("target.model", target.Key.Serialized);
                    Input("target.floorAdded", target.FloorAdded);
                }
                void Input<T>(string field, T value) => _reads.Add(owner.Facts.RecordResult(_prefix + ".input." + field,
                    _node, value, origin: origin, operation: "shop authored binding"));
            }

            private PredictorKnowledgeFactId Read<T>(string field, T value,
                ImmutableArray<PredictorCombatPendingCause> causes = default,
                ImmutableArray<PredictorKnowledgeFactId> sourceReads = default, string? derivation = null)
            {
                var id = _owner.Facts.RecordResult(_prefix + ".read." + field, _node, value, causes,
                    operation: "shop " + _action.Action + " read " + field, allReadFactIds: sourceReads,
                    derivation: derivation);
                _reads.Add(id); return id;
            }

            private void Unknown(string field, ImmutableArray<PredictorCombatPendingCause> causes,
                ImmutableArray<PredictorKnowledgeFactId> sourceReads = default)
            {
                string key = _prefix + ".read." + field;
                _reads.Add(_owner.Facts.RecordUnknown(key, _node, PredictorRouteFactPhase.Results,
                    ShopUnknown(causes, _node, key), operation: "shop " + _action.Action + " required " + field,
                    allReadFactIds: sourceReads));
            }

            internal void ReadFlow(PredictorRun run, bool removal = false)
            {
                Read("phase", run.Phase);
                if (removal)
                {
                    Read("automaticPurchaseActive", run._parasolActive);
                    Read("removalPickerActive", run._shopRemovalEntry.HasValue);
                }
            }

            internal void ReadInventoryOpen(PredictorRun run) => Read("inventoryOpen", run._shopInventoryOpen);

            internal PredictorKnowledgeFactId ReadGold(PredictorRun run) => Read("gold.balance", run.Working.GoldBudget,
                _owner.Domain(PredictorCombatDomain.Gold), _owner.ShopLatest("gold.balance"));

            internal void ReadOffer(PredictorRun run, PredictorShopOfferKnowledge offer)
            {
                var stock = _owner.ShopResultReads(_node, offer.Slot);
                if (!offer.IsKnown)
                {
                    foreach (string field in new[] { "identity", "bindingSnapshot", "stockRevision", "stocked", "baseCost", "cost" })
                        Unknown("slot." + offer.Slot + "." + field, offer.PendingCauses, stock);
                    return;
                }
                var value = offer.Value!;
                Read("slot." + offer.Slot + ".identity", OfferIdentity(value), sourceReads: stock);
                Read("slot." + offer.Slot + ".bindingSnapshot", System.Text.Json.JsonSerializer.Serialize(value), sourceReads: stock);
                Read("slot." + offer.Slot + ".stockRevision", value.StockRevision, sourceReads: stock);
                Read("slot." + offer.Slot + ".stocked", value.Stocked, sourceReads: stock);
                var baseCost = Read("slot." + offer.Slot + ".baseCost", value.BaseCost, sourceReads: stock);
                var membership = Read("relics.shopDiscounts.membershipCard", PredictorSettlementEffects.Has(run.Working, "MEMBERSHIP_CARD"),
                    _owner.Domain(PredictorCombatDomain.Relics), _owner.ShopLatest("relics.orderedInventory"));
                var courier = Read("relics.shopDiscounts.courier", PredictorSettlementEffects.Has(run.Working, "THE_COURIER"),
                    _owner.Domain(PredictorCombatDomain.Relics), _owner.ShopLatest("relics.orderedInventory"));
                _costReads[offer.Slot] = Read("slot." + offer.Slot + ".cost", value.Cost,
                    sourceReads: [baseCost, membership, courier]);
            }

            internal void ReadAffordability(PredictorRun run, PredictorShopOfferKnowledge offer)
            {
                var gold = ReadGold(run);
                var reads = _costReads.TryGetValue(offer.Slot, out var cost) ? ImmutableArray.Create(cost, gold) :
                    ImmutableArray.Create(gold);
                if (offer.Affordable is { } known && offer.AffordabilityPendingCauses.IsEmpty)
                    Read("slot." + offer.Slot + ".affordable", known, sourceReads: reads);
                else Unknown("slot." + offer.Slot + ".affordable", offer.AffordabilityPendingCauses,
                    reads);
            }

            internal void ReadGate(string field, ImmutableArray<PredictorCombatPendingCause> causes)
            {
                if (causes.IsDefaultOrEmpty) Read(field, true);
                else Unknown(field, causes);
            }

            internal void ReadRemovalTarget(PredictorRun run, PredictorPlannedCard? target, bool known,
                ImmutableArray<PredictorCombatPendingCause> causes)
            {
                var sources = _owner.ShopLatest("deck.Removable.candidates", "deck.All.candidates");
                if (!known || target == null)
                {
                    Unknown("removal.target.identity", causes, sources); return;
                }
                var card = run.Working.Card(target.InstanceId);
                Read("removal.target.identity", card.Id + ":" + card.Key.Serialized + ":" + card.FloorAdded,
                    sourceReads: sources,
                    derivation: "RouteShopKnownCard verified deck membership, the authored instance/model/floor binding, established instance identity, and absence of an unresolved existing-card mutation covering this card. Optional additions to the candidate population cannot change this fixed target.");
                if (run._shopRemovalEntry.HasValue)
                    Read("removal.target.removable", card.Removable, sourceReads: sources,
                        derivation: "The verified fixed target retains its observed Removable flag: RouteShopKnownCard excludes unresolved existing-card changes covering it. Optional reward additions may change the full picker population without changing this card's qualification.");
            }

            internal void ReadRemovalQuote(PredictorRun run)
            {
                var causes = _owner.Domain(PredictorCombatDomain.Relics);
                Read("removal.quotedCost", run._shopRemovalQuotedCost, causes);
                ReadGold(run);
            }

            internal void ReadRemovalCandidates(PredictorRun run)
            {
                var sources = _owner.ShopLatest("deck.Removable.count", "deck.Removable.candidates");
                // Actions within this room may have changed the ordinary deck
                // since the owner's last boundary synchronization. Do not use
                // a stale typed projection as proof of candidate existence.
                if (!run.Working.Deck.Select(run.Working.Card).SequenceEqual(_owner._observed.Deck.Select(_owner._observed.Card)))
                {
                    Read("removal.candidateExistence", run.Working.Deck.Any(id => run.Working.Card(id).Removable),
                        _owner.Domain(PredictorCombatDomain.Deck), sources,
                        derivation: "The deck changed after the recorded boundary. Existence was recomputed from the current deck's Removable flags, with all current Deck-domain dependencies retained as the output mask; earlier population facts are historical reads, not the current count proof.");
                    return;
                }
                var projection = _owner.Deck.Project(PredictorDeckProjectionScope.Removable);
                var lower = projection.Count?.Bounds.Lower;
                if (lower is > 0)
                    Read("removal.candidateExistence", true, sourceReads: sources,
                        derivation: "Deck.Project(Removable).Count has a conservative lower bound of " + lower.Value +
                            " > 0 across every retained reward alternative. At least one removable candidate therefore exists even while the population/count remains partial.");
                else if (projection.KnownCount == 0)
                    Read("removal.candidateExistence", false, sourceReads: sources,
                        derivation: "Deck.Project(Removable).KnownCount is exactly zero across all retained alternatives. No removable candidate exists, independently of any unresolved candidate identities.");
                else Unknown("removal.candidateExistence", _owner.ProjectionCauses(projection, count: true), sources);
            }

            internal void Complete(PredictorRun run, bool applied, ImmutableArray<PredictorCombatPendingCause> causes,
                ImmutableArray<PredictorShopOfferKnowledge> offers)
            {
                var reads = _reads.Distinct().ToImmutableArray();
                _owner.Facts.RecordResult(_prefix + ".projectionDisposition", _node, applied ? "Applied" : "Blocked",
                    operation: "shop action projection return", allReadFactIds: reads,
                    derivation: "Return from ApplyRouteShopAction; Blocked is the forecast gate, not a concrete-game rejection.");
                if (!applied) _owner.Facts.RecordUnknown(_prefix + ".outcome", _node, PredictorRouteFactPhase.Results,
                    ShopUnknown(causes, _node, _prefix + ".outcome"), allReadFactIds: reads,
                    operation: "shop blocked action");
                else
                {
                    var gold = _owner.Facts.RecordResult(_prefix + ".write.gold.balance", _node, run.Working.GoldBudget,
                        _owner.Domain(PredictorCombatDomain.Gold), operation: "shop applied action gold",
                        allReadFactIds: reads);
                    _owner.RecordStateWrite(_node, "gold.balance", gold);
                    if (_action.Action == PredictorShopActionKind.ChooseRemoval && _action.Card is { } removed)
                    {
                        _owner.Facts.RecordResult(_prefix + ".write.removal.selectedId", _node, removed.InstanceId,
                            operation: "shop accepted removal target", allReadFactIds: reads);
                        _owner.Facts.RecordResult(_prefix + ".write.removal.removalsUsed", _node, run.Working.CardShopRemovalsUsed,
                            operation: "shop accepted removal counter", allReadFactIds: reads);
                    }
                    if (_action.Action is PredictorShopActionKind.Buy or PredictorShopActionKind.ChooseRemoval)
                    {
                        foreach (var offer in offers)
                        {
                            string key = _prefix + ".write.slot." + offer.Slot;
                            var id = offer.IsKnown ? _owner.Facts.RecordResult(key, _node,
                                OfferIdentity(offer.Value!) + ";cost=" + offer.Value!.Cost + ";stocked=" + offer.Value!.Stocked,
                                operation: "shop post-purchase stock observation", allReadFactIds: reads,
                                derivation: "Published stock mask; restock generator internals are not instrumented here.") :
                                _owner.Facts.RecordUnknown(key, _node, PredictorRouteFactPhase.Results,
                                    ShopUnknown(offer.PendingCauses, _node, key), allReadFactIds: reads,
                                    operation: "shop post-purchase stock observation");
                            _owner._shopResultReads[(_node, offer.Slot)] = [id];
                        }
                    }
                }
            }
        }
    }
}
