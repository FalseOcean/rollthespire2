using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed class AncientGenerationTrace(string node)
    {
        internal readonly string Node = node;
        internal readonly List<PredictorKnowledgeFactId> Common = [];
        internal readonly Dictionary<string, PredictorKnowledgeFactId> Conditions = new(StringComparer.Ordinal);
        internal readonly List<PredictorKnowledgeFactId> Character = [];
        internal PredictorKnowledgeFactId StarterCard;
        internal PredictorKnowledgeFactId StarterRelic;
        internal PredictorKnowledgeFactId RewardsCursor;
        internal PredictorKnowledgeFactId DustyPool;
        internal int RewardsCalls;
        internal Beta109AncientEventContextSnapshot? Context;
        internal AncientOptionConditionProfile Profile = null!;
        internal PredictorKnowledgeFactId NextInstanceId;
    }

    private sealed partial class RoutePartialState
    {
        private readonly Dictionary<(string Node, int Index, string Field), PredictorKnowledgeFactId> _ancientResultFacts = [];
        private readonly Dictionary<string, ImmutableArray<PredictorKnowledgeFactId>> _ancientChildResultFacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PredictorKnowledgeFactId> _ancientSelectionPlans = new(StringComparer.Ordinal);
        private int _ancientBindingOccurrence;
        private int _ancientChildOccurrence;

        internal ImmutableArray<PredictorKnowledgeFactId> AncientResultReads(string node, int index, string field) =>
            _ancientResultFacts.TryGetValue((node, index, field), out var id) ? [id] : [];
        internal ImmutableArray<PredictorKnowledgeFactId> AncientResultUnresolvedReads(string node, int index, string field) =>
            Unresolved(AncientResultReads(node, index, field));
        internal ImmutableArray<PredictorKnowledgeFactId> AncientChildResultReads(string node) =>
            _ancientChildResultFacts.GetValueOrDefault(node, []);
        internal ImmutableArray<PredictorKnowledgeFactId> AncientChildResultUnresolvedReads(string node) =>
            Unresolved(AncientChildResultReads(node));

        private ImmutableArray<PredictorKnowledgeFactId> AncientLatest(params string[] keys) =>
            keys.Where(_latest.ContainsKey).Select(key => _latest[key]).Distinct().ToImmutableArray();

        internal AncientGenerationTrace BeginAncientGeneration(PredictorRun run, PredictorPosition position,
            ModelKey ancient, AncientOptionConditionProfile profile, Beta109WorldGenerationSnapshot authority)
        {
            Synchronize(run.Working);
            var trace = new AncientGenerationTrace(position.NodeId);
            trace.Profile = profile;
            var origin = run.Context.Live == null ? PredictorKnowledgeOrigin.Derived : PredictorKnowledgeOrigin.Captured;
            PredictorKnowledgeFactId Input<T>(string key, T value, ImmutableArray<PredictorCombatPendingCause> causes = default,
                ImmutableArray<PredictorKnowledgeFactId> reads = default) =>
                Facts.RecordResult("ancient.input." + key, trace.Node, value, causes, origin,
                    operation: "EnterAncient input", allReadFactIds: reads);
            trace.Common.Add(Input("act", position.Act));
            trace.Common.Add(Input("ancient", ancient.Serialized));
            trace.Common.Add(Input("character", run.Context.Character.Serialized));
            trace.NextInstanceId = Input("nextInstanceId", run.Working.NextInstanceId, Domain(PredictorCombatDomain.InstanceIds));
            trace.Common.Add(Input("eventLocal.runSeedRoot", authority.RunSeedRoot));
            var context = authority.AncientEventContexts.FirstOrDefault(c => c.Act == position.Act && c.AncientKey == ancient && c.PlayerSlot == 0)
                ?? authority.AncientEventContexts.FirstOrDefault(c => c.Act == position.Act && c.AncientKey == ancient && c.IsShared);
            trace.Context = context;
            if (context != null)
            {
                trace.Common.Add(Input("eventLocal.context", context.EventIdEntry + ":slot=" + context.PlayerSlot + ":shared=" + context.IsShared));
                trace.Common.Add(Input("eventLocal.root", context.EventRngRoot));
                trace.Common.Add(Input("eventLocal.rootExact", context.EventRngRootExact));
                trace.Common.Add(Facts.RecordResult("ancient.input.hookDecision", trace.Node, context.HookDecision,
                    operation: "EnterAncient replaces donor HookDecision with Allow", origin: PredictorKnowledgeOrigin.Derived));
                trace.Common.Add(Input("authority", "context=" + context.EventContextExact + "; modifiers=" + context.ModifierFactsExact +
                    "; dynamic=" + context.DynamicFactsExact + "; catalog=" + context.Catalog?.CatalogExact + "; capability=" + context.Catalog?.Capability));
                trace.Common.Add(Facts.RecordResult("ancient.input.authority.eventLocalOptionsAllowed", trace.Node,
                    authority.AllowsAncientOptionFastSearch(ancient.Entry) || authority.AllowsAncientOptionProductionExact(ancient.Entry),
                    operation: "provider event-local option authority gate"));
                // The provider's exactness check reads every pool's ordering/filter
                // authority, even when that pool is not selected by this branch.
                trace.Common.Add(Input("catalog.poolAuthority", string.Join(" | ", context.Catalog?.Pools.Select(p =>
                    p.PoolId + ":ordinal=" + p.SourceOrdinal + ":order=" + p.OrderExact + ":filter=" + p.FilterResultExact) ?? [])));
                if (ancient.Entry == "OROBAS")
                {
                    trace.Character.Add(Input("unlockedCharacters.ordered", string.Join(" | ", context.UnlockedCharacters.Select(k => k.Serialized))));
                    trace.Character.Add(Input("unlockedCharacters.orderExact", context.UnlockedCharacterSourceOrderExact));
                    // The other-character draw precedes all Orobas identity draws.
                    trace.Common.AddRange(trace.Character);
                }
            }

            // AncientConditions eagerly computes this profile. Only the flags
            // actually consumed by each generator are attached to its outputs.
            void CountCondition(string name, PredictorDeckProjectionScope scope, int threshold, bool value)
            {
                var projection = Deck.Project(scope);
                var count = RecordInteger("ancient.input.profile." + name + ".count", trace.Node, projection.Count,
                    ProjectionCauses(projection, count: true), "AncientConditions " + scope + " count");
                bool boundsProve = projection.Count is { } expression &&
                    (expression.Bounds.Lower >= threshold || expression.Bounds.Upper < threshold);
                bool mismatch = boundsProve && value != (projection.Count!.Bounds.Lower >= threshold);
                bool proven = boundsProve && !mismatch;
                ImmutableArray<PredictorCombatPendingCause> gap = mismatch ?
                    [PredictorRouteCauseEvidence.CreateExplicit("AncientProfileWitnessMismatch", trace.Node, PredictorCombatDomain.Deck,
                        PredictorPendingReasonKind.ImplementationGap,
                        "The generator's concrete profile disagrees with the retained deck threshold proof.",
                        ["ancient.profile." + name], occurrenceKey: name)] : [];
                trace.Conditions[name] = Facts.RecordResult("ancient.compute.profile." + name, trace.Node, value,
                    gap, operation: "AncientConditions threshold " + threshold, allReadFactIds: [count],
                    unresolvedReadFactIds: proven ? [] : Unresolved([count]),
                    derivation: proven ? "The typed count enclosure proves this threshold across every retained alternative." : null);
            }
            CountCondition("basicStrike", PredictorDeckProjectionScope.BasicStrike, 1, profile.TezcataraHasBasicStrike);
            CountCondition("swift", PredictorDeckProjectionScope.Enchantable, 4, profile.NonupeipeSwiftEnchantableAtLeast4);
            CountCondition("instinct", PredictorDeckProjectionScope.EnchantableAttack, 3, profile.TanxInstinctEnchantableAtLeast3);
            CountCondition("goopy", PredictorDeckProjectionScope.EnchantableDefend, 3, profile.PaelGoopyDefendCardsAtLeast3);
            CountCondition("nonEternal", PredictorDeckProjectionScope.NonEternal, 5, profile.PaelRemovableCardsAtLeast5);
            var deckReads = AncientLatest("deck.All.candidates");
            var all = Deck.Project(PredictorDeckProjectionScope.All);
            var deckCauses = ProjectionCauses(all, count: false);
            var relicCauses = Domain(PredictorCombatDomain.Relics).Where(c => c.Code != "FirstPlayerTurn").ToImmutableArray();
            var starterCatalog = Input("catalog.starterRelics", string.Join(" | ", run.Context.Catalog!.StarterRelics.Select(k => k.Serialized)));
            var petCatalog = Input("catalog.eventPetRelics", string.Join(" | ", run.Context.Catalog.EventPetRelics.Select(k => k.Serialized)));
            var relics = Input("relics.allMembership", string.Join(" | ", run.Working.Relics.Select(r => r.Key.Serialized)),
                relicCauses, AncientLatest("relics.orderedInventory"));
            var egg = Input("deck.containsByrdonisEgg", run.Working.Deck.Any(id => run.Working.Card(id).Key.Entry == "BYRDONIS_EGG"),
                deckCauses, deckReads);
            trace.Conditions["legion"] = Facts.RecordResult("ancient.compute.profile.legion", trace.Node, profile.PaelAllowLegionNoEventPet,
                operation: "AncientConditions no event-pet relic and no Byrdonis Egg", allReadFactIds: [relics, egg, petCatalog],
                unresolvedReadFactIds: Unresolved([relics, egg]));
            var starter = Input("deck.containsStarterModel", profile.OrobasArchaicToothConditionMet, deckCauses, deckReads);
            trace.Conditions["tooth"] = starter;
            trace.Conditions["touch"] = Facts.RecordResult("ancient.compute.profile.touch", trace.Node, profile.OrobasTouchOfOrobasConditionMet,
                operation: "AncientConditions starter-relic membership including melted instances", allReadFactIds: [relics, starterCatalog],
                unresolvedReadFactIds: Unresolved([relics]));
            var modifiers = Input("modifiers", string.Join(" | ", run.Context.Modifiers.Select(m => m.Key.Serialized)));
            trace.Conditions["pandora"] = Facts.RecordResult("ancient.compute.profile.pandora", trace.Node, profile.DarvAllowPandorasBoxRelicSet,
                operation: "AncientConditions Draft/Insanity/SealedDeck exclusion", allReadFactIds: [modifiers]);
            trace.StarterCard = Input("starterCard.firstId", run.Working.Deck.Select(run.Working.Card).FirstOrDefault(c =>
                c.Key.Entry is "BASH" or "NEUTRALIZE" or "UNLEASH" or "FALLING_STAR" or "DUALCAST")?.Id.ToString() ?? "none",
                deckCauses.AddRange(Domain(PredictorCombatDomain.InstanceIds)), deckReads);
            trace.StarterRelic = Input("starterRelic.firstId", run.Working.Relics.FirstOrDefault(r =>
                run.Context.Catalog!.StarterRelics.Contains(r.Key))?.Id.ToString() ?? "none",
                relicCauses.AddRange(Domain(PredictorCombatDomain.InstanceIds)), [relics, starterCatalog]);
            trace.RewardsCalls = run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls;
            return trace;
        }

        private static string[] AncientSlotConditions(string ancient, int index) => ancient switch
        {
            "DARV" => ["pandora"], "OROBAS" when index == 2 => ["touch", "tooth"],
            "PAEL" when index == 1 => ["goopy", "nonEternal"], "PAEL" when index == 2 => ["legion"],
            "TEZCATARA" when index == 0 => ["basicStrike"], "NONUPEIPE" => ["swift"], "TANX" => ["instinct"], _ => []
        };

        internal void CompleteAncientGeneration(PredictorRun run, AncientGenerationTrace trace, Beta109AncientOptionPrediction generated)
        {
            var optionCatalog = trace.Context!.Catalog!;
            bool fixedCount = run.CurrentAncient!.Value.Entry switch
            {
                "NONUPEIPE" => optionCatalog.Pool("nonupeipe.pool.base").Count >= 3,
                "TANX" => optionCatalog.Pool("tanx.pool.base").Count >= 3,
                "DARV" => optionCatalog.PoolsWithPrefix("darv.valid.").Count(p =>
                    p.PoolId != "darv.valid.pandoras-box" && p.OrderedOptions.Count > 0) >= 3,
                _ => true
            };
            var allocationReads = trace.Common.Concat(AncientSlotConditions(run.CurrentAncient.Value.Entry, 2)
                .Select(key => trace.Conditions[key])).ToImmutableArray();
            var count = Facts.RecordResult("ancient.compute.offerCount", trace.Node, run.AncientOffers.Length,
                operation: "EnterAncient option allocation count", allReadFactIds: allocationReads,
                unresolvedReadFactIds: fixedCount ? [] : Unresolved(allocationReads),
                derivation: fixedCount ? "The generator emits three slots even when its retained condition changes the candidate pool." : null);
            Facts.RecordResult("ancient.write.nextInstanceId", trace.Node, run.Working.NextInstanceId,
                operation: "EnterAncient allocated offer IDs", allReadFactIds: [trace.NextInstanceId, count],
                unresolvedReadFactIds: Unresolved([trace.NextInstanceId, count]));
            Facts.RecordResult("ancient.write.phase", trace.Node, run.Phase, operation: "EnterAncient actual choice phase");
            // This stream is private to the event provider. It is not Niche,
            // UpFront or another PredictorState cursor.
            var countReads = trace.Common.Concat(AncientSlotConditions(run.CurrentAncient.Value.Entry, 2)
                .Select(key => trace.Conditions[key])).ToImmutableArray();
            if (run.CurrentAncient.Value.Entry is "NONUPEIPE" or "TANX" or "DARV")
                Facts.RecordResult("ancient.compute.eventLocal.calls", trace.Node, generated.RngCallCount,
                    operation: "PredictEventLocal draw count", allReadFactIds: countReads, unresolvedReadFactIds: Unresolved(countReads));
            else Facts.RecordResult("ancient.compute.eventLocal.calls", trace.Node, generated.RngCallCount,
                operation: "PredictEventLocal fixed draw count", allReadFactIds: trace.Common.ToImmutableArray());
            foreach (var pair in run.AncientOffers.Select((offer, index) => (offer, index)))
            {
                var offer = pair.offer;
                var reads = trace.Common.Concat(AncientSlotConditions(run.CurrentAncient.Value.Entry, pair.index)
                    .Select(key => trace.Conditions[key])).ToImmutableArray();
                var catalog = trace.Context!.Catalog!;
                void Pool(string id)
                {
                    reads = reads.Add(Facts.RecordResult("ancient.input.catalog.pool." + id, trace.Node,
                        string.Join(" | ", catalog.Pool(id).Select(k => k.Serialized)),
                        origin: run.Context.Live == null ? PredictorKnowledgeOrigin.Derived : PredictorKnowledgeOrigin.Captured,
                        operation: "PredictEventLocal immutable catalog subpool " + id,
                        derivation: "Immutable metadata read in the provider's representative branch. This is not a proof of the complete dynamic candidate population; the output separately retains its condition dependencies."));
                }
                // This table names metadata reads only. The provider owns pool
                // weighting, shuffling, selection and its event-local trace.
                switch (run.CurrentAncient.Value.Entry)
                {
                    case "DARV":
                        foreach (var draw in generated.Trace.Where(t => t.SourceStage.StartsWith("darv:valid-set:", StringComparison.Ordinal)))
                            Pool(draw.SourceStage["darv:valid-set:".Length..]);
                        if (generated.Trace.Single(t => t.SourceStage == "darv:dusty-tome-branch").IntResult == 0) Pool("darv.dusty-tome");
                        break;
                    case "OROBAS":
                        if (pair.index == 0) Pool(generated.Trace.Single(t => t.SourceStage == "orobas:sea-glass-branch").DoubleResult < .3333333f
                            ? "orobas.pool1.true" : "orobas.pool1.false");
                        else if (pair.index == 1) Pool("orobas.pool2");
                        else
                        {
                            if (trace.Profile.OrobasTouchOfOrobasConditionMet) Pool("orobas.pool3.touch");
                            if (trace.Profile.OrobasArchaicToothConditionMet) Pool("orobas.pool3.tooth");
                        }
                        break;
                    case "PAEL":
                        if (pair.index == 0) Pool("pael.pool1");
                        else if (pair.index == 1)
                        {
                            Pool("pael.pool2.base"); Pool("pael.pool2.growth");
                            if (trace.Profile.PaelGoopyDefendCardsAtLeast3) Pool("pael.pool2.goopy");
                            if (trace.Profile.PaelRemovableCardsAtLeast5) Pool("pael.pool2.removable");
                        }
                        else { Pool("pael.pool3.base"); if (trace.Profile.PaelAllowLegionNoEventPet) Pool("pael.pool3.no-event-pet"); }
                        break;
                    case "TEZCATARA":
                        if (pair.index == 0) { Pool("tezcatara.pool1.base"); if (trace.Profile.TezcataraHasBasicStrike) Pool("tezcatara.pool1.basic-strike"); }
                        else Pool("tezcatara.pool" + (pair.index + 1));
                        break;
                    case "NONUPEIPE": Pool("nonupeipe.pool.base"); if (trace.Profile.NonupeipeSwiftEnchantableAtLeast4) Pool("nonupeipe.pool.swift"); break;
                    case "TANX": Pool("tanx.pool.base"); if (trace.Profile.TanxInstinctEnchantableAtLeast3) Pool("tanx.pool.instinct"); break;
                    case "VAKUU": Pool("vakuu.pool" + (pair.index + 1)); break;
                }
                bool DrawForSlot(WorldRngTraceEntry draw) => run.CurrentAncient.Value.Entry switch
                {
                    "OROBAS" => draw.SourceStage == "orobas:pool" + (pair.index + 1),
                    "PAEL" => draw.SourceStage == (pair.index == 1 ? "pael:pool2-weighted" : "pael:pool" + (pair.index + 1)),
                    "TEZCATARA" => draw.SourceStage == "tezcatara:pool" + (pair.index + 1),
                    "VAKUU" => draw.SourceStage.StartsWith("vakuu.pool" + (pair.index + 1) + ":shuffle:", StringComparison.Ordinal),
                    _ => true
                };
                var drawEvidence = Facts.RecordResult("ancient.compute.offer." + pair.index + ".eventLocal.readEvidence", trace.Node,
                    generated.Trace.Where(DrawForSlot).ToImmutableArray(), operation: "provider actual draw-bound evidence",
                    allReadFactIds: reads, unresolvedReadFactIds: Unresolved(reads),
                    safeFormatter: draws => string.Join(" | ", draws.Select(draw => draw.SourceStage + ":" + draw.Operation +
                        ":bound=" + draw.Bound + ":callsAfter=" + draw.CallCountAfter)),
                    derivation: "Actual provider trace. Bounds from a representative dynamic pool are hidden unless this slot's input dependencies are established; no weighted pool or shuffle is reconstructed here.");
                reads = reads.Add(drawEvidence);
                void Output<T>(string field, T value, ImmutableArray<PredictorKnowledgeFactId> dependencies)
                {
                    var id = Facts.RecordResult("ancient.generate.offer." + pair.index + "." + field, trace.Node, value,
                        operation: "EnterAncient generate " + field, allReadFactIds: dependencies,
                        unresolvedReadFactIds: Unresolved(dependencies));
                    _ancientResultFacts[(trace.Node, pair.index, field)] = id;
                }
                Output("identity", offer.Relic.Serialized, reads);
                Output("selectable", offer.Selectable, reads);
                var variantReads = reads;
                if (run.CurrentAncient.Value.Entry == "DARV" && offer.Relic.Entry == "DUSTY_TOME")
                    variantReads = variantReads.Add(Facts.RecordResult("ancient.input.catalog.dustyTome.variant", trace.Node,
                        catalog.VariantData.TryGetValue("darv.dusty-tome.variant", out var variant) ? variant : "rewards-projection-pending",
                        origin: run.Context.Live == null ? PredictorKnowledgeOrigin.Derived : PredictorKnowledgeOrigin.Captured,
                        operation: "provider DustyTome variant metadata/default read"));
                Output("variant", offer.Variant ?? "none", variantReads);
                var characterReads = offer.Relic.Entry == "SEA_GLASS" ? reads.AddRange(trace.Character) : reads;
                if (!offer.CharacterTargetKnown)
                {
                    var cause = AncientCharacterTargetCause(trace.Node, offer);
                    _ancientResultFacts[(trace.Node, pair.index, "characterTarget")] = Facts.RecordUnknown(
                        "ancient.generate.offer." + pair.index + ".characterTarget", trace.Node, PredictorRouteFactPhase.Results,
                        [cause], operation: "EnterAncient unresolved character setup", allReadFactIds: characterReads);
                }
                else Output("characterTarget", offer.CharacterTarget?.Serialized ?? "none", characterReads);
                Output("starterCardTarget", offer.StarterCardId?.ToString() ?? "none",
                    offer.Relic.Entry == "ARCHAIC_TOOTH" ? reads.Add(trace.StarterCard) : reads);
                Output("starterRelicTarget", offer.StarterRelicId?.ToString() ?? "none",
                    offer.Relic.Entry == "TOUCH_OF_OROBAS" ? reads.Add(trace.StarterRelic) : reads);
                if (offer.Relic.Entry == "DUSTY_TOME")
                {
                    trace.RewardsCursor = Facts.RecordResult("ancient.input.rng.Rewards.before", trace.Node, trace.RewardsCalls,
                        StreamCauses(PredictorStream.Rewards), operation: "DustyTome prepared-card cursor",
                        allReadFactIds: AncientLatest("rng.Rewards.state"));
                    trace.DustyPool = Facts.RecordResult("ancient.input.dustyTome.orderedPool", trace.Node,
                        string.Join(" | ", run.Context.Catalog!.DustyTomeCards.Select(k => k.Serialized)),
                        origin: run.Context.Live == null ? PredictorKnowledgeOrigin.Derived : PredictorKnowledgeOrigin.Captured,
                        operation: "DustyTome prepared-card pool");
                    Output("preparedCard", offer.PreparedCard?.Serialized ?? "none",
                        reads.AddRange(ImmutableArray.Create(trace.RewardsCursor, trace.DustyPool)));
                    Facts.RecordResult("ancient.compute.rng.Rewards.consumed", trace.Node, 1,
                        operation: "DustyTome fixed prepared-card draw", allReadFactIds: [trace.DustyPool]);
                    var after = Facts.RecordResult("ancient.write.rng.Rewards.after", trace.Node,
                        run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls,
                        StreamCauses(PredictorStream.Rewards), operation: "DustyTome prepared-card cursor write",
                        allReadFactIds: [trace.RewardsCursor, trace.DustyPool]);
                    RecordStateWrite(trace.Node, "rng.Rewards.calls", after);
                }
                else Output("preparedCard", "none", reads);
            }
        }

        internal void RecordAncientOfferObservation(PredictorRun run, ModelKey ancient,
            ImmutableArray<PredictorAncientOfferKnowledge> offers)
        {
            foreach (var offer in offers)
            {
                string node = run.Working.Position.NodeId;
                if (_ancientResultFacts.ContainsKey((node, offer.Index, "identity"))) continue;
                var value = offer.Value;
                void Observe(string field, string display, ImmutableArray<PredictorCombatPendingCause> causes)
                {
                    var id = Facts.RecordResult("ancient.observe.offer." + offer.Index + "." + field, node, display, causes,
                        origin: run.Context.Live == null ? PredictorKnowledgeOrigin.Derived : PredictorKnowledgeOrigin.Captured,
                        operation: "already generated Ancient offer snapshot",
                        derivation: "The generator ran before this forecast recorder was attached; this is a field observation, not a generator read audit.");
                    _ancientResultFacts[(node, offer.Index, field)] = id;
                }
                Observe("identity", value?.Relic.Serialized ?? "unresolved", offer.IdentityPendingCauses);
                Observe("variant", value?.Variant ?? "none", offer.IdentityPendingCauses);
                Observe("characterTarget", value?.CharacterTarget?.Serialized ?? "none", offer.IdentityPendingCauses.AddRange(offer.CharacterTargetPendingCauses));
                Observe("selectable", value?.Selectable.ToString() ?? "unresolved", offer.IdentityPendingCauses);
                Observe("preparedCard", value?.PreparedCard?.Serialized ?? "none", offer.IdentityPendingCauses.AddRange(offer.PreparedCardPendingCauses));
                Observe("starterCardTarget", value?.StarterCardId?.ToString() ?? "none", offer.IdentityPendingCauses.AddRange(offer.TargetPendingCauses));
                Observe("starterRelicTarget", value?.StarterRelicId?.ToString() ?? "none", offer.IdentityPendingCauses.AddRange(offer.TargetPendingCauses));
            }
        }

        internal void RecordAncientSelection(PredictorRun run, ChooseAncientOffer input, PredictorAncientOffer offer, PredictorRelic prepared)
        {
            string node = run.Working.Position.NodeId;
            int index = run.AncientOffers.IndexOf(offer);
            var authored = _ancientSelectionPlans.TryGetValue(node, out var plan) ? ImmutableArray.Create(plan) : [];
            var choice = Facts.RecordResult("ancient.selection.resolved.offerIndex", node, index,
                operation: "RouteAncient authored semantic offer resolved to this slot", allReadFactIds: authored);
            Facts.RecordResult("ancient.selection.resolved.offerId", node, input.OfferId,
                Domain(PredictorCombatDomain.InstanceIds), operation: "RouteAncient derived submit ID", allReadFactIds: [choice]);
            void Write<T>(string field, T value)
            {
                var reads = AncientResultReads(node, index, field).Add(choice);
                Facts.RecordResult("ancient.selection.prepared." + field, node, value,
                    operation: "SubmitAncient prepared relic before BeginObtainRelic", allReadFactIds: reads,
                    unresolvedReadFactIds: Unresolved(reads));
            }
            Write("identity", prepared.Key.Serialized); Write("characterTarget", prepared.CharacterTarget?.Serialized ?? "none");
            Write("starterCardTarget", prepared.StarterCardId?.ToString() ?? "none");
            Write("starterRelicTarget", prepared.StarterRelicId?.ToString() ?? "none");
            Write("preparedCard", prepared.PreparedCard?.Serialized ?? "none"); Write("variant", prepared.VariantId ?? "none");
            Facts.RecordResult("ancient.selection.prepared.floorAdded", node, prepared.FloorAdded,
                operation: "SubmitAncient prepared relic floor", allReadFactIds: [choice]);
        }

        internal AncientBindingTrace BeginAncientBinding(PredictorRun run, ModelKey ancient, PredictorPlannedAncient? plan,
            ImmutableArray<PredictorAncientOfferKnowledge> offers) => new(this, run, ancient, plan, offers, ++_ancientBindingOccurrence);

        internal sealed class AncientBindingTrace
        {
            private readonly RoutePartialState _owner;
            private readonly string _node, _prefix;
            private readonly List<PredictorKnowledgeFactId> _reads = [];
            private readonly bool _authored;
            internal AncientBindingTrace(RoutePartialState owner, PredictorRun run, ModelKey ancient, PredictorPlannedAncient? plan,
                ImmutableArray<PredictorAncientOfferKnowledge> offers, int occurrence)
            {
                _owner = owner; _node = run.Working.Position.NodeId; _prefix = "ancient.binding." + occurrence; _authored = plan != null;
                Read("actualAncient", ancient.Serialized);
                Read("planPresent", plan != null);
                if (plan == null) { Read("phase", run.Phase); return; }
                Input("expectedAncient", plan.ExpectedAncient.Serialized);
                if (plan.ExpectedAncient != ancient) return;
                Read("phase", run.Phase);
                owner._ancientSelectionPlans[_node] = Input("expectedOffer", JsonSerializer.Serialize(plan.ExpectedOffer with { Id = 0 }));
                if (run.Phase is PredictorPhase.OpeningChoice or PredictorPhase.AncientChoice)
                    foreach (var offer in offers)
                    {
                        var sources = new[] { "identity", "variant", "characterTarget", "starterCardTarget", "starterRelicTarget", "preparedCard", "selectable" }
                            .SelectMany(field => owner.AncientResultReads(_node, offer.Index, field)).ToImmutableArray();
                        _reads.Add(owner.Facts.RecordResult(_prefix + ".read.offer." + offer.Index, _node,
                            offer.Value == null ? "unresolved" : JsonSerializer.Serialize(offer.Value with { Id = 0 }), offer.PendingCauses,
                            operation: "SameAncientOffer semantic binding", allReadFactIds: sources,
                            unresolvedReadFactIds: owner.Unresolved(sources)));
                    }
                else Read("acceptedSelection", JsonSerializer.Serialize(run._nodeReplayInputs.LastOrDefault(i => i is ChooseOpeningRelic or ChooseAncientOffer)));
                PredictorKnowledgeFactId Input<T>(string field, T value)
                {
                    var id = owner.Facts.RecordResult(_prefix + ".input." + field, _node, value,
                        origin: PredictorKnowledgeOrigin.PlayerInput, operation: "Ancient authored binding");
                    _reads.Add(id); return id;
                }
            }
            internal void Read<T>(string field, T value, ImmutableArray<PredictorCombatPendingCause> causes = default) =>
                _reads.Add(_owner.Facts.RecordResult(_prefix + ".read." + field, _node, value, causes, operation: "Ancient plan binding read"));
            internal void Complete(bool accepted, ImmutableArray<PredictorCombatPendingCause> causes)
            {
                _owner.Facts.RecordResult(_prefix + ".projectionDisposition", _node, !_authored ? "NotAuthored" : accepted ? "Bound" : "Blocked",
                    operation: "Ancient binding return", allReadFactIds: _reads.ToImmutableArray(),
                    derivation: "The forecast binding returned this disposition after checking its known selection/guards. This records its control-flow return; it does not establish masked offer fields or a concrete-game outcome.");
                if (!accepted && _authored) _owner.Facts.RecordUnknown(_prefix + ".acceptedSelection", _node, PredictorRouteFactPhase.Results,
                    causes, operation: "Ancient unresolved selection", allReadFactIds: _reads.ToImmutableArray(),
                    unresolvedReadFactIds: _owner.Unresolved(_reads));
            }
        }

        internal AncientChildTrace BeginAncientChild(PredictorRun run, PredictorPlannedObtainChild step) =>
            new(this, run, step, ++_ancientChildOccurrence);
        internal sealed class AncientChildTrace
        {
            private readonly RoutePartialState _owner;
            private readonly string _node, _prefix;
            private readonly List<PredictorKnowledgeFactId> _reads = [];
            internal AncientChildTrace(RoutePartialState owner, PredictorRun run, PredictorPlannedObtainChild step, int occurrence)
            {
                _owner = owner; _node = run.Working.Position.NodeId; _prefix = "ancient.child." + occurrence;
                Input("action", step.Action); Input("expectedSource", step.ExpectedSource.Serialized);
                Read("phase", run.Phase); Read("actualSource", CurrentRouteObtainSource(run)?.Serialized ?? "none");
            }
            internal void Input<T>(string field, T value) => _reads.Add(_owner.Facts.RecordResult(_prefix + ".input." + field, _node, value,
                origin: PredictorKnowledgeOrigin.PlayerInput, operation: "Ancient authored child input"));
            internal void Read<T>(string field, T value, ImmutableArray<PredictorCombatPendingCause> causes = default) =>
                _reads.Add(_owner.Facts.RecordResult(_prefix + ".read." + field, _node, value, causes, operation: "Ancient child resolver read"));
            internal void Gate(string field, ImmutableArray<PredictorCombatPendingCause> causes) => Read(field, causes.IsEmpty, causes);
            internal void Complete(PredictorRun run, PredictorInput? input, bool applied, ImmutableArray<PredictorCombatPendingCause> causes)
            {
                if (input != null) Read("resolvedInput", JsonSerializer.Serialize(input, input.GetType()));
                _owner.Facts.RecordResult(_prefix + ".projectionDisposition", _node, applied ? "Applied" : "Blocked",
                    operation: "Ancient child input projection return", allReadFactIds: _reads.ToImmutableArray(),
                    derivation: "This is the actual ApplyRouteObtainChild return. A missing dependency returns null before Submit and therefore proves Blocked for this forecast; it does not establish that dependency or a concrete-game rejection.");
                var output = !applied ? _owner.Facts.RecordUnknown(_prefix + ".resolvedSelection", _node, PredictorRouteFactPhase.Results, causes,
                    operation: "Ancient child unresolved binding", allReadFactIds: _reads.ToImmutableArray(),
                    unresolvedReadFactIds: _owner.Unresolved(_reads)) : _owner.Facts.RecordResult(_prefix + ".write.phase", _node, run.Phase,
                    operation: "Ancient accepted child phase; obtain internals recorded by their owners", allReadFactIds: _reads.ToImmutableArray(),
                    unresolvedReadFactIds: _owner.Unresolved(_reads));
                _owner._ancientChildResultFacts[_node] = [output];
            }
        }
    }
}
