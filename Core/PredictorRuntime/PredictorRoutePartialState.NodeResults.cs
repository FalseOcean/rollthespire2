using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed partial class RoutePartialState
    {
        // Read published DTO observations, never Working. An unresolved mask is
        // checked before formatting payloads; an omitted collection is not a
        // proven empty inventory or complete candidate population.
        internal void RecordRoomResults(PredictorRouteNodeKnowledge node)
        {
            string id = node.Position.NodeId;
            foreach (var storage in node.PaelsToothStorage)
            {
                string key = "result.paelsTooth." + storage.SourceId;
                RoomValue(id, key + ".identitiesKnown", false);
                RoomNumber(id, key + ".initialCount", storage.InitialCount, storage.PendingCauses);
                RoomNumber(id, key + ".remainingCount", storage.RemainingCount, storage.PendingCauses);
            }
            foreach (var effect in node.ObtainHealthEffects)
            {
                string key = "result.obtainHealth." + effect.Address.Occurrence;
                RoomValue(id, key + ".source", effect.Source.Serialized);
                RoomValue(id, key + ".kind", effect.Kind);
                RoomValue(id, key + ".amount", effect.Amount);
                RoomUnknown(id, key + ".settlement", effect.PendingCauses);
            }
            if (node.Room == PredictorRoomKind.RestSite) RecordRestResults(id, node);
            if (node.Shop is { } shop)
            {
                RoomValue(id, "result.shop.forecastAdvanced", shop.Advanced);
                RoomNumber(id, "result.shop.goldBalance", shop.GoldBalance, shop.GoldPendingCauses);
                foreach (var offer in shop.Offers)
                {
                    string key = "result.shop.offer." + offer.Slot;
                    var stockReads = ShopResultReads(id, offer.Slot);
                    RoomValue(id, key + ".kind", offer.Kind);
                    if (offer.IsKnown)
                    {
                        var value = offer.Value!;
                        RoomValue(id, key + ".identity", value.Card == null ? value.Key?.Serialized ?? "card removal" : RoomCardIdentity(value.Card), reads: stockReads);
                        RoomValue(id, key + ".price", value.Cost, reads: stockReads);
                        RoomValue(id, key + ".basePrice", value.BaseCost, reads: stockReads);
                        RoomValue(id, key + ".stocked", value.Stocked, reads: stockReads);
                        RoomValue(id, key + ".stockRevision", value.StockRevision, reads: stockReads);
                        RoomValue(id, key + ".sale", value.IsOnSale, reads: stockReads);
                    }
                    else foreach (string field in new[] { "identity", "price", "basePrice", "stocked", "stockRevision", "sale" })
                        RoomUnknown(id, key + "." + field, offer.PendingCauses, stockReads);
                    RoomBoolean(id, key + ".affordable", offer.Affordable, offer.AffordabilityPendingCauses);
                }
                RecordObtainResults(id, "result.shop.child", shop.RequiredChoices);
            }
            if (node.Ancient is { } ancient)
            {
                RoomValue(id, "result.ancient.identity", ancient.Ancient.Serialized);
                RoomValue(id, "result.ancient.forecastAdvanced", ancient.Advanced);
                foreach (var offer in ancient.Offers)
                {
                    string key = "result.ancient.offer." + offer.Index;
                    ImmutableArray<PredictorKnowledgeFactId> OfferReads(string field) => AncientResultReads(id, offer.Index, field);
                    if (offer.Value is { } value && offer.IdentityPendingCauses.IsEmpty)
                    {
                        RoomValue(id, key + ".identity", value.Relic.Serialized, reads: OfferReads("identity"));
                        RoomValue(id, key + ".variant", value.Variant ?? "none", reads: OfferReads("variant"));
                        if (offer.CharacterTargetPendingCauses.IsEmpty)
                            RoomValue(id, key + ".characterTarget", value.CharacterTarget?.Serialized ?? "none", reads: OfferReads("characterTarget"));
                        else RoomUnknown(id, key + ".characterTarget", offer.CharacterTargetPendingCauses, OfferReads("characterTarget"));
                        if (offer.PreparedCardPendingCauses.IsEmpty) RoomValue(id, key + ".preparedCard", value.PreparedCard?.Serialized ?? "none", reads: OfferReads("preparedCard"));
                        else RoomUnknown(id, key + ".preparedCard", offer.PreparedCardPendingCauses, OfferReads("preparedCard"));
                        if (offer.TargetPendingCauses.IsEmpty)
                        {
                            RoomValue(id, key + ".starterCardTarget", value.StarterCardId?.ToString() ?? "none", reads: OfferReads("starterCardTarget"));
                            RoomValue(id, key + ".starterRelicTarget", value.StarterRelicId?.ToString() ?? "none", reads: OfferReads("starterRelicTarget"));
                        }
                        else
                        {
                            RoomUnknown(id, key + ".starterCardTarget", offer.TargetPendingCauses, OfferReads("starterCardTarget"));
                            RoomUnknown(id, key + ".starterRelicTarget", offer.TargetPendingCauses, OfferReads("starterRelicTarget"));
                        }
                        RoomValue(id, key + ".selectable", value.Selectable, offer.PendingCauses, reads: OfferReads("selectable"));
                    }
                    else foreach (string field in new[] { "identity", "variant", "characterTarget", "preparedCard", "starterCardTarget", "starterRelicTarget", "selectable" })
                        RoomUnknown(id, key + "." + field, offer.PendingCauses, OfferReads(field));
                }
                RecordObtainResults(id, "result.ancient.child", ancient.RequiredChoices);
                RecordRoomRewards(id, "result.ancient.reward", ancient.Rewards);
            }
            if (node.Treasure is { } treasure)
            {
                // The producer masks eligibility only with the actual relic
                // inventory guard; Treasure RNG independently masks identity.
                RoomBoolean(id, "result.treasure.generationAllowed", treasure.GenerationAllowed,
                    treasure.RelicPendingCauses.Where(cause => (cause.AffectedDomains & PredictorCombatDomain.Relics) != 0 &&
                        cause.Code != "FirstPlayerTurn").ToImmutableArray(), TreasureResultReads(id, "generationAllowed"));
                if (treasure.RelicPendingCauses.IsEmpty && treasure.OfferedRelic.HasValue)
                    RoomValue(id, "result.treasure.relic", treasure.OfferedRelic.Value.Serialized, reads: TreasureResultReads(id, "relic"));
                else if (treasure.RelicPendingCauses.IsEmpty && treasure.GenerationAllowed == false)
                    RoomValue(id, "result.treasure.relic", "none (generation suppressed)");
                else RoomUnknown(id, "result.treasure.relic", treasure.RelicPendingCauses, TreasureResultReads(id, "relic"));
                RoomNumber(id, "result.treasure.normalGold", treasure.NormalGold, treasure.NormalGoldPendingCauses, TreasureResultReads(id, "normalGold"));
                RoomNumber(id, "result.treasure.spoilsGold", treasure.SpoilsGold, treasure.SpoilsPendingCauses, TreasureResultReads(id, "spoilsGold"));
                RoomNumber(id, "result.treasure.goldBalance", treasure.GoldBalance, treasure.GoldPendingCauses, TreasureResultReads(id, "goldBalance"));
                RoomValue(id, "result.treasure.opened", treasure.Opened);
                RoomValue(id, "result.treasure.relicTaken", treasure.RelicTaken);
                RoomValue(id, "result.treasure.forecastAdvanced", treasure.Advanced);
                RecordObtainResults(id, "result.treasure.child", treasure.RequiredChoices);
                RecordRoomRewards(id, "result.treasure.reward", treasure.Rewards);
            }
            if (node.Event is { } eventNode) RecordEventResults(id, eventNode);
            if (node.Room == PredictorRoomKind.Shop && node.Shop == null) RoomUnknown(id, "result.shop.observation", []);
            if (node.Room is PredictorRoomKind.Ancient or PredictorRoomKind.Opening && node.Ancient == null)
                RoomUnknown(id, "result.ancient.observation", []);
            if (node.Room == PredictorRoomKind.Treasure && node.Treasure == null) RoomUnknown(id, "result.treasure.observation", []);
            if (node.Room == PredictorRoomKind.Event && node.Event == null) RoomUnknown(id, "result.event.observation", []);
        }

        private void RecordRestResults(string id, PredictorRouteNodeKnowledge node)
        {
            RoomValue(id, "result.rest.forecastAdvanced", node.Advanced);
            // RestOfferPendingCauses masks Hatch membership. Retained other
            // actions are the source's guaranteed option subset.
            foreach (var pair in node.RestOffers.Select((value, index) => (value, index)))
            {
                string key = "result.rest.offer." + pair.index;
                RoomValue(id, key + ".action", pair.value.Action);
                var cards = node.RestCards.IsEmpty ? node.Cards : node.RestCards;
                bool known = pair.value.Action switch
                {
                    PredictorRestAction.Smith => node.DeckPendingCauses.IsEmpty ||
                        cards.Any(card => card.IsKnown && card.Value.UpgradeLevel < card.Value.MaxUpgradeLevel),
                    PredictorRestAction.Cook => node.DeckPendingCauses.IsEmpty ||
                        cards.Count(card => card.IsKnown && card.Value.Removable) >= 2,
                    PredictorRestAction.Hatch => node.RestOfferPendingCauses.IsEmpty,
                    _ => true
                };
                if (known) RoomValue(id, key + ".enabled", pair.value.Enabled, reads: RestResultReads(id, "offer." + pair.value.Action + ".enabled"));
                else RoomUnknown(id, key + ".enabled", pair.value.Action == PredictorRestAction.Hatch
                    ? node.RestOfferPendingCauses : node.DeckPendingCauses, RestResultReads(id, "offer." + pair.value.Action + ".enabled"));
            }
            if (!node.RestOfferPendingCauses.IsEmpty) RoomUnknown(id, "result.rest.hatchMembership", node.RestOfferPendingCauses);
            RecordRoomCards(id, "result.rest.candidate", node.RestCards);
            if (node.RestHeal is { } heal)
            {
                // Nominal healing metadata does not claim a resulting HP value.
                RoomValue(id, "result.rest.heal.baseMaxHpFraction", heal.BaseMaxHpFraction, reads: RestResultReads(id, "heal.baseMaxHpFraction"));
                RoomValue(id, "result.rest.heal.flatBonus", heal.FlatHealBonus, reads: RestResultReads(id, "heal.flatBonus"));
                RoomValue(id, "result.rest.heal.maxHpDelta", heal.MaxHpDelta, reads: RestResultReads(id, "heal.maxHpDelta"));
                RoomValue(id, "result.rest.heal.maxHpLoss", heal.MaxHpLoss, reads: RestResultReads(id, "heal.maxHpLoss"));
                RoomValue(id, "result.rest.heal.nominalOverride", heal.OverrideHealAmount?.ToString() ?? "none", reads: RestResultReads(id, "heal.nominalOverride"));
            }
        }

        private void RecordEventResults(string id, PredictorEventNodeKnowledge value)
        {
            if (value.Event is { } model) RoomValue(id, "result.event.identity", model.Serialized, value.OccurrencePendingCauses, reads: EventResultReads(id, value.TraceIdentity, "occurrence"));
            else RoomUnknown(id, "result.event.identity", value.OccurrencePendingCauses.IsEmpty ? value.PendingCauses : value.OccurrencePendingCauses, EventResultReads(id, value.TraceIdentity, "occurrence"));
            RoomValue(id, "result.event.neededInput", value.NeededInput, reads: EventResultReads(id, value.TraceIdentity));
            RoomValue(id, "result.event.healthRequired", value.HealthRequired);
            RoomValue(id, "result.event.cardsRequired", value.CardsRequired);
            RoomValue(id, "result.event.forecastAdvanced", value.Advanced);
            RoomNumber(id, "result.event.goldBalance", value.GoldBalance, value.GoldPendingCauses, EventResultReads(id, value.TraceIdentity, "gold"));
            RecordEventOptions(id, "result.event.option", value.Options);
            RecordEventOptions(id, "result.event.entryOption", value.EntryOptions);
            RecordRoomCards(id, "result.event.deckCandidate", value.DeckCardCandidates);
            RecordOfferedCards(id, "result.event.offeredCard", value.OfferedCards, value.OfferedCardPendingCauses);
            RecordObtainResults(id, "result.event.child", value.RequiredChoices);
            RecordRoomRewards(id, "result.event.reward", value.Rewards);
            RecordHealthCommands(id, "result.event.healthConsequence", value.HealthConsequences);
            RecordHealthCommands(id, "result.event.pendingHealthCommand", value.PendingHealthCommands);
            if (value.Occurrence is { } occurrence)
            {
                RoomValue(id, "result.event.occurrence.cursor", occurrence.Cursor, reads: EventResultReads(id, value.TraceIdentity, "occurrence"));
                for (int i = 0; i < occurrence.Candidates.Length; i++)
                {
                    var candidate = occurrence.Candidates[i];
                    string key = "result.event.occurrence.candidate." + i;
                    RoomValue(id, key + ".event", candidate.Event.Serialized, value.OccurrencePendingCauses);
                    RoomValue(id, key + ".queueEvent", candidate.QueueEvent.Serialized);
                    RoomValue(id, key + ".rawOrdinal", candidate.RawOrdinal);
                    RoomValue(id, key + ".cursor", candidate.Cursor);
                    RoomValue(id, key + ".eligibility", candidate.EligibilityKind, candidate.EligibilityPendingCauses);
                    RoomValue(id, key + ".healthAssumed", candidate.HealthEligibilityAssumed,
                        origin: candidate.HealthEligibilityAssumed ? PredictorKnowledgeOrigin.ApprovedDefault : PredictorKnowledgeOrigin.Derived);
                    RoomValue(id, key + ".deckAssumed", candidate.DeckEligibilityAssumed,
                        origin: candidate.DeckEligibilityAssumed ? PredictorKnowledgeOrigin.ApprovedDefault : PredictorKnowledgeOrigin.Derived);
                }
            }
            if (value.Crystal is { } crystal)
            {
                RoomValue(id, "result.event.crystal.remaining", crystal.Remaining);
                RoomValue(id, "result.event.crystal.tool", crystal.Tool);
                foreach (var cell in crystal.Cells)
                {
                    string key = "result.event.crystal.cell." + cell.X + "." + cell.Y;
                    RoomValue(id, key + ".hidden", cell.Hidden);
                    if (cell.Hidden) RoomUnknown(id, key + ".item", []);
                    else RoomValue(id, key + ".item", cell.RevealedItem ?? "empty");
                }
            }
            if (value.Combat is { } combat) RecordRoomRewards(id, "result.event.combat.reward", combat.Rewards);
        }

        private void RecordEventOptions(string id, string prefix, ImmutableArray<PredictorEventOptionKnowledge> options)
        {
            foreach (var pair in options.Select((value, index) => (value, index)))
            {
                var option = pair.value;
                string key = prefix + "." + pair.index;
                RoomValue(id, key + ".key", option.Key);
                if (option.Value is { } value && option.IdentityPendingCauses.IsEmpty)
                {
                    RoomValue(id, key + ".identity", value.Key, reads: EventResultReads(id, option.TraceIdentity, "identity"));
                    RoomValue(id, key + ".goldCost", value.GoldCost?.ToString() ?? "none");
                    RoomValue(id, key + ".goldGain", value.GoldGain?.ToString() ?? "none");
                    RoomValue(id, key + ".cardTarget", value.CardTargetId?.ToString() ?? "none");
                    RoomValue(id, key + ".potionTarget", value.PotionId?.ToString() ?? "none");
                    RoomValue(id, key + ".relicTarget", value.RelicId?.ToString() ?? "none");
                    RoomValue(id, key + ".offeredRelic", value.OfferedRelic?.Serialized ?? "none");
                    RoomValue(id, key + ".cardType", value.CardType?.ToString() ?? "none");
                    RoomValue(id, key + ".nominalHpDamage", value.HealthDamage?.ToString() ?? "none");
                    RoomValue(id, key + ".nominalMaxHpCost", value.MaxHpCost?.ToString() ?? "none");
                    RoomValue(id, key + ".nominalHpHeal", value.HealthHeal?.ToString() ?? "none");
                    if (value.WillKill.HasValue) RoomValue(id, key + ".willKill", value.WillKill.Value);
                }
                else foreach (string field in new[] { "identity", "goldCost", "goldGain", "cardTarget", "potionTarget", "relicTarget", "offeredRelic", "cardType", "nominalHpDamage", "nominalMaxHpCost", "nominalHpHeal", "willKill" })
                    RoomUnknown(id, key + "." + field, option.IdentityPendingCauses.IsEmpty ? option.PendingCauses : option.IdentityPendingCauses, EventResultReads(id, option.TraceIdentity, "identity"));
                RoomBoolean(id, key + ".enabled", option.Enabled,
                    option.AvailabilityPendingCauses.AddRange(option.IdentityPendingCauses), EventResultReads(id, option.TraceIdentity, "guard"));
                if (option.ResultPreview is not { } result)
                {
                    if (option.Enabled == false && option.AvailabilityPendingCauses.IsEmpty && option.IdentityPendingCauses.IsEmpty)
                        RoomValue(id, key + ".branch.applicability", "disabled option");
                    else RoomUnknown(id, key + ".branch", option.PendingCauses);
                    continue;
                }
                string branch = key + ".branch";
                RoomValue(id, branch + ".neededInput", result.NeededInput, reads: EventResultReads(id, result.TraceIdentity));
                RoomValue(id, branch + ".healthRequired", result.HealthRequired);
                RoomValue(id, branch + ".cardsRequired", result.CardsRequired);
                RoomValue(id, branch + ".forecastAdvanced", result.Advanced);
                RoomNumber(id, branch + ".goldBalance", result.GoldBalance,
                    result.PendingCauses.Where(cause => (cause.AffectedDomains & PredictorCombatDomain.Gold) != 0).ToImmutableArray(), EventResultReads(id, result.TraceIdentity, "gold"));
                // Branch arrays are published guaranteed subsets. No complete
                // deck/cardinality claim follows from their current length.
                RecordRoomCards(id, branch + ".generatedCard", result.GeneratedCards);
                RecordRoomCards(id, branch + ".deckCandidate", result.DeckCardCandidates);
                RecordOfferedCards(id, branch + ".offeredCard", result.OfferedCards, []);
                if (result.OfferedCards.IsEmpty && result.DeckCardCandidates.IsEmpty && result.NeededInput == PredictorEventNeededInput.Cards)
                    RoomUnknown(id, branch + ".cardSelection", result.PendingCauses);
                if (!result.RelicPendingCauses.IsEmpty) RoomUnknown(id, branch + ".obtainedRelics", result.RelicPendingCauses);
                else for (int i = 0; i < result.ObtainedRelics.Length; i++)
                    RoomValue(id, branch + ".obtainedRelic." + i, result.ObtainedRelics[i].Key.Serialized);
                RecordObtainResults(id, branch + ".child", result.RequiredChoices);
                RecordRoomRewards(id, branch + ".reward", result.Rewards);
                RecordHealthCommands(id, branch + ".healthConsequence", result.HealthConsequences);
                RecordHealthCommands(id, branch + ".pendingHealthCommand", result.PendingHealthCommands);
            }
        }

        private void RecordObtainResults(string id, string prefix, ImmutableArray<PredictorObtainChoiceKnowledge> children)
        {
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                string key = prefix + "." + i;
                RoomValue(id, key + ".phase", child.Phase);
                RoomValue(id, key + ".source", child.Source.Serialized);
                RoomValue(id, key + ".minimumCards", child.MinimumCards);
                RoomValue(id, key + ".maximumCards", child.MaximumCards);
                RecordRoomCards(id, key + ".deckCandidate", child.DeckCards);
                if (child.Phase is PredictorPhase.ObtainChooseCard or PredictorPhase.ObtainChooseManyCards)
                {
                    RecordOfferedCards(id, key + ".offeredCard", child.Cards, child.PendingCauses);
                    if (child.Cards.IsEmpty) RoomUnknown(id, key + ".offeredCards", child.PendingCauses);
                }
                if (child.Phase == PredictorPhase.ChooseBundle)
                {
                    if (!child.PendingCauses.IsEmpty || child.Bundles.IsEmpty) RoomUnknown(id, key + ".bundles", child.PendingCauses);
                    else for (int bundle = 0; bundle < child.Bundles.Length; bundle++)
                        RecordOfferedCards(id, key + ".bundle." + bundle + ".card", child.Bundles[bundle], []);
                }
                // This DTO observes an open child boundary, not its selected
                // successor. It exposes no invented completion payload.
                RoomUnknown(id, key + ".completion", child.PendingCauses);
                RecordRoomRewards(id, key + ".reward", child.Rewards);
            }
        }

        private void RecordRoomCards(string id, string prefix, ImmutableArray<PredictorRouteCardKnowledge> cards)
        {
            for (int i = 0; i < cards.Length; i++)
                if (cards[i].IsKnown) RoomValue(id, prefix + "." + i + ".identity", RoomCardIdentity(cards[i].Value));
                else RoomUnknown(id, prefix + "." + i + ".identity", cards[i].PendingCauses);
        }
        private void RecordOfferedCards(string id, string prefix, ImmutableArray<PredictorCard> cards,
            ImmutableArray<PredictorCombatPendingCause> causes)
        {
            if (!causes.IsEmpty) { RoomUnknown(id, prefix + ".offers", causes); return; }
            for (int i = 0; i < cards.Length; i++) RoomValue(id, prefix + "." + i + ".identity", RoomCardIdentity(cards[i]));
        }
        private void RecordRoomRewards(string id, string prefix, ImmutableArray<PredictorCombatRewardKnowledge> rewards)
        {
            for (int i = 0; i < rewards.Length; i++)
                if (rewards[i].IsKnown) Facts.RecordResult(prefix + "." + i, id, rewards[i].Value!, safeFormatter: RewardText);
                else RoomUnknown(id, prefix + "." + i, rewards[i].PendingCauses);
        }
        private void RecordHealthCommands(string id, string prefix, ImmutableArray<PredictorEventHealthDelta> commands)
        {
            for (int i = 0; i < commands.Length; i++)
                RoomValue(id, prefix + "." + i, "nominal HP delta=" + commands[i].HpDelta + "; nominal max HP delta=" + commands[i].MaxHpDelta);
        }
        private static string RoomCardIdentity(PredictorCard card) => CardDiagnostic(card) +
            (card.Id > 0 ? " #" + card.Id : "");
        private void RoomNumber(string id, string key, int? value, ImmutableArray<PredictorCombatPendingCause> causes,
            ImmutableArray<PredictorKnowledgeFactId> reads = default)
        { if (value.HasValue) RoomValue(id, key, value.Value, causes, reads: reads); else RoomUnknown(id, key, causes, reads); }
        private void RoomBoolean(string id, string key, bool? value, ImmutableArray<PredictorCombatPendingCause> causes,
            ImmutableArray<PredictorKnowledgeFactId> reads = default)
        { if (value.HasValue) RoomValue(id, key, value.Value, causes, reads: reads); else RoomUnknown(id, key, causes, reads); }
        private void RoomValue<T>(string id, string key, T value, ImmutableArray<PredictorCombatPendingCause> causes = default,
            PredictorKnowledgeOrigin origin = PredictorKnowledgeOrigin.Derived,
            ImmutableArray<PredictorKnowledgeFactId> reads = default)
            => Facts.RecordResult(key, id, value, causes, origin, "room DTO observation", allReadFactIds: reads,
                derivation: "The published observation uses its producer's field-specific determinacy, not the determinacy of the entire action.");
        private void RoomUnknown(string id, string key, ImmutableArray<PredictorCombatPendingCause> causes,
            ImmutableArray<PredictorKnowledgeFactId> reads = default)
        {
            if (causes.IsDefaultOrEmpty) causes = [PredictorRouteCauseEvidence.CreateExplicit("RoomResultDerivationMissing", id,
                PredictorCombatDomain.None, PredictorPendingReasonKind.ImplementationGap,
                "The room DTO exposes no established value or dependency for " + key + ".", [key], occurrenceKey: key)];
            Facts.RecordUnknown(key, id, PredictorRouteFactPhase.Results, causes, operation: "room DTO observation", allReadFactIds: reads,
                unresolvedReadFactIds: reads.IsDefault ? [] : reads.Where(fact => Facts.Fact(fact).Status != PredictorKnowledgeStatus.Known).ToImmutableArray());
        }
    }
}
