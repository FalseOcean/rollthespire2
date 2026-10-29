using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{

    private PredictorOptionalObtainAddress RegisterLiveObtainAddress(ModelKey source, PredictorObtainReturn destination, long sourceId)
    {
        string node = _routeNode?.NodeId ?? Working.Position.NodeId;
        var parents = _obtainFrames.Select(frame => frame.SourceId).ToImmutableArray();
        var existing = _preview.Obtains.Addresses.LastOrDefault(value => value.Node == node && value.Source == source &&
            value.Address.Destination == destination && value.Address.SourceId == sourceId &&
            value.Address.ParentSourceIds.SequenceEqual(parents));
        if (existing.Address != null) return existing.Address;
        int occurrence = _preview.Obtains.ReceiptCounts.GetValueOrDefault(node);
        _preview.Obtains.ReceiptCounts[node] = occurrence + 1;
        var address = new PredictorOptionalObtainAddress(destination, sourceId, occurrence, parents);
        _preview.Obtains.Addresses.Add((node, source, address));
        return address;
    }

    private void RecordLiveObtainHealth(ModelKey source, PredictorOptionalObtainAddress address)
    {
        string node = _routeNode?.NodeId ?? Working.Position.NodeId;
        if (_preview.Obtains.HealthEffects.Any(value => value.Node == node && value.Effect.Source == source &&
            SameOptionalObtainAddress(value.Effect.Address, address))) return;
        var (kind, amount) = source.Entry switch
        {
            "LEAFY_POULTICE" => (PredictorObtainHealthEffectKind.LoseMaxHp, 12),
            "SERE_TALON" => (PredictorObtainHealthEffectKind.LoseMaxHp, 9),
            "FRAGRANT_MUSHROOM" => (PredictorObtainHealthEffectKind.Damage, 15),
            "PRECARIOUS_SHEARS" => (PredictorObtainHealthEffectKind.Damage, 16),
            "LEES_WAFFLE" => (PredictorObtainHealthEffectKind.GainMaxHpAndFullHeal, 7),
            "FAKE_LEES_WAFFLE" => (PredictorObtainHealthEffectKind.HealMaxHpFraction, 10),
            _ => throw new InvalidOperationException("PredictorLiveObtainHealthSourceInvalid")
        };
        bool loss = kind is PredictorObtainHealthEffectKind.LoseMaxHp or PredictorObtainHealthEffectKind.Damage;
        var domains = PredictorCombatDomain.Health;
        if (loss && Working.PotionSlots.Any(potion => potion?.Key.Entry == "FAIRY_IN_A_BOTTLE"))
            domains |= PredictorCombatDomain.PotionInventory;
        if (loss && Working.Relics.Any(relic => !relic.Melted && relic.Key.Entry == "LIZARD_TAIL" && !relic.LizardTailUsed))
            domains |= PredictorCombatDomain.Relics;
        var cause = PredictorRouteCauseEvidence.CreateExplicit("ObtainHealthOutcomePending", node, domains,
            PredictorPendingReasonKind.MissingObservation,
            "The acquired relic's health command is displayed without inventing HP, survival or death-preventer consumption. Independent effects continue.",
            ["health.obtain.outcome"], occurrenceKey: PredictorRouteCauseEvidence.Key("obtain-health", source.Serialized,
                address.Destination.ToString(), address.SourceId.ToString(), address.Occurrence.ToString()));
        _preview.Partial!.RecordLiveHealthDependency(cause, address.Occurrence, source, kind, amount);
        _preview.Obtains.HealthEffects.Add((node, new(source, address, kind, amount, [cause])));
    }

    // Called after authored children, or before a following non-health action.
    // It completes the real obtain destination without a synthetic HP input.
    private static bool CompleteLiveObtainHealth(PredictorRun run, List<RouteDependency> dependencies, CancellationToken token)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || run._preview.Partial == null ||
            run.Phase != PredictorPhase.ObtainHealth || CurrentRouteObtainSource(run) is not { } source) return false;
        var pending = run._leafyPending ?? run._wafflePending ?? run._fakeWafflePending ?? run._fragrantPending ??
            run._serePending ?? run._precariousPending;
        if (pending == null) return false;
        run.RecordLiveObtainHealth(source, run.RegisterLiveObtainAddress(source, pending.Destination, pending.SourceId));
        bool transformLeafy = false;
        if (source.Entry == "LEAFY_POULTICE")
        {
            run._preview.Partial.Synchronize(run.Working);
            var strike = run._preview.Partial.Deck.Project(PredictorDeckProjectionScope.BasicStrike);
            var defend = run._preview.Partial.Deck.Project(PredictorDeckProjectionScope.BasicDefend);
            if (strike.KnownCount == 0 && defend.KnownCount == 0) { }
            else if (strike.OrderedCandidatesKnown && defend.OrderedCandidatesKnown &&
                run._preview.Partial.StreamKnown(PredictorStream.Transformations))
                transformLeafy = true;
            else run._preview.Partial.RecordLiveLeafyPending(run.Working, strike, defend);
        }
        else if (source.Entry == "FRAGRANT_MUSHROOM")
            run._preview.Partial.RecordLiveDeckRandomObtain(run.Working, source);
        run.RecordRouteEventObtainHealthProjection(source, pending, transformLeafy);
        FinalizeProjectedObtainHealth(run, pending, source, transformLeafy, token);
        return true;
    }

    // Shared with exact-position observation replay. Knowledge decisions and
    // health causes belong to the original projection owner; this successor
    // uses only the reconstructed before-state and its already passed decision.
    private static void FinalizeProjectedObtainHealth(PredictorRun run, PredictorObtainCompletion pending,
        ModelKey source, bool transformLeafy, CancellationToken token)
    {
        if (source.Entry == "LEAFY_POULTICE" && transformLeafy)
        {
            var changed = PredictorObtainSources.Leafy(run.Context, run.Working, token);
            run.Working = changed.State;
            run.Accept(new("LeafyPoulticeTransform", pending.SourceId,
                changed.Changes.Select(change => change.ReplacementId).ToImmutableArray(), null, null)
                { Transformations = changed.Changes });
        }
        else if (source.Entry == "SERE_TALON")
        {
            var wishes = ImmutableArray.CreateBuilder<long>();
            for (int index = 0; index < 3; index++)
            {
                wishes.Add(run.Working.NextInstanceId);
                run.Working = PredictorObtainSources.AddCard(run.Context, run.Working, new ModelKey("CARD", "WISH"));
            }
            run.Accept(new("SereTalonWishes", pending.SourceId, wishes.ToImmutable(), null, null));
        }
        run._leafyPending = run._wafflePending = run._fakeWafflePending = run._fragrantPending = run._serePending = run._precariousPending = null;
        run.Phase = PredictorPhase.Rewards;
        run._requestId++;
        run.CompleteObtainDestination(pending);
        run.ContinueObtainChildIfComplete(token);
        run.DrainObtainCompletions(token);
    }

    private sealed partial class RoutePartialState
    {
        internal void RecordLiveHealthDependency(PredictorCombatPendingCause cause, int occurrence, ModelKey source,
            PredictorObtainHealthEffectKind kind, int amount)
        {
            AddDependency(_dependencies, new(cause)
            { ChangedRelicEntries = [], ChangedRelicStateEntries = (cause.AffectedDomains & PredictorCombatDomain.Relics) != 0 ? ["LIZARD_TAIL"] : [],
                ChangesPersonalRelicBag = false, ChangesSharedRelicBag = false });
            Facts.RecordUnknown("health.obtain.outcome." + occurrence, cause.NodeId!, PredictorRouteFactPhase.After,
                [cause], origin: PredictorKnowledgeOrigin.ApprovedDefault, operation: source.Serialized + ":" + kind + ":" + amount);
        }

        internal void RecordLiveLeafyPending(PredictorState state, PredictorDeckProjection strike, PredictorDeckProjection defend)
        {
            string node = state.Position.NodeId;
            // Capture all actual read origins before registering this derived
            // effect. A known basic population can still have an unknown
            // Transformations cursor; the deck projections own no such root.
            var parents = PredictorRouteCauseEvidence.Normalize(ProjectionCauses(strike, false)
                .AddRange(ProjectionCauses(defend, false)).AddRange(StreamCauses(PredictorStream.Transformations)));
            var domains = PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds | PredictorCombatDomain.TransformationsRng;
            if (PredictorSettlementEffects.Has(state, "LUCKY_FYSH")) domains |= PredictorCombatDomain.Gold;
            if (PredictorSettlementEffects.Has(state, "BOOK_OF_FIVE_RINGS")) domains |= PredictorCombatDomain.Relics;
            var cause = PredictorRouteCauseEvidence.CreateExplicit("LeafyTransformOutcomePending", node, domains,
                PredictorPendingReasonKind.DerivedDependency,
                "The real Leafy transformation depends on the first basic Strike/Defend and Transformations stream; no replacement is invented.",
                ["deck.BasicStrike.first", "deck.BasicDefend.first", "rng.Transformations.state"], parents,
                occurrenceKey: "leafy-transform:" + state.Relics.Last(relic => relic.Key.Entry == "LEAFY_POULTICE").Id);
            AddDependency(_dependencies, new(cause, MayChangeExistingCards: true, AnyExistingCard: true)
                { ChangedRelicEntries = [], ChangedRelicStateEntries = ["BOOK_OF_FIVE_RINGS"] });
            AddStreamGap(PredictorStream.Transformations, node, "LeafyTransformConsumptionPending", parents, cause.Identity);
            Synchronize(state);
        }
    }
}
