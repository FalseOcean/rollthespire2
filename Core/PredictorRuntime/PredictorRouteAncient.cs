using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private static PredictorAncientNodeKnowledge ProjectRouteAncient(PredictorRun run,
        PredictorPlannedAncient? plan, List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        var ancient = run.Working.Visits.LastOrDefault()?.Room == PredictorRoomKind.Opening
            ? new ModelKey("EVENT", "NEOW") : run.CurrentAncient ??
                run.Working.Acts.Single(a => a.Act == run.Working.Position.Act).Ancient;
        var offers = ObserveRouteAncientOffers(run, ancient, dependencies);
        var bindingTrace = run._preview.Partial?.BeginAncientBinding(run, ancient, plan, offers);
        int bindingCauseStart = local.Count;
        ImmutableArray<PredictorCombatPendingCause> EntryCauses()
        {
            var causes = ObtainCauses(run, plan!.ExpectedOffer.Relic, dependencies, entryOnly: true);
            bindingTrace?.Read("obtainEntryAllowed", causes.IsEmpty, causes);
            return causes;
        }
        int applied = 0;
        if (plan != null && plan.ExpectedAncient != ancient)
            local.Add(RouteCause("PlannedAncientChanged", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
        else if (run.Phase is PredictorPhase.OpeningChoice or PredictorPhase.AncientChoice)
        {
            if (plan == null)
                local.Add(RouteCause("AncientChoice", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
            else
            {
                var matches = offers.Where(o => o.Value != null && SameAncientOffer(o.Value, plan.ExpectedOffer)).ToArray();
                if (matches.Length != 1 || !matches[0].IsKnown || !matches[0].Value!.Selectable)
                    local.Add(RouteCause("PlannedAncientOfferPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                        matches.Length == 1 ? matches[0].PendingCauses : offers.Where(o => o.Value == null ||
                                o.Value.Relic == plan.ExpectedOffer.Relic)
                            .SelectMany(o => o.Value == null ? o.IdentityPendingCauses : o.PendingCauses).ToImmutableArray()));
                else if (!EntryCauses().IsEmpty)
                    local.Add(RouteCause("PlannedAncientObtainDependent", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                        ObtainCauses(run, plan.ExpectedOffer.Relic, dependencies, entryOnly: true)));
                else
                {
                    PredictorInput input = run.Phase == PredictorPhase.OpeningChoice
                        ? new ChooseOpeningRelic(plan.ExpectedOffer.Relic)
                        : new ChooseAncientOffer(run.AncientOffers[matches[0].Index].Id);
                    if (run.Submit(run.Request!, input, token) != PredictorInputResult.Accepted)
                        local.Add(RouteCause("PlannedAncientOfferPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
                }
            }
        }
        else if (plan != null)
        {
            // The accepted active-node choice is authoritative. A saved plan
            // for a different relic cannot be rebound to its current child.
            var selected = run._nodeReplayInputs.LastOrDefault(i => i is ChooseOpeningRelic or ChooseAncientOffer);
            bool same = selected switch
            {
                ChooseOpeningRelic opening => opening.Relic == plan.ExpectedOffer.Relic,
                ChooseAncientOffer choice => run.AncientOffers.Any(o => o.Id == choice.OfferId && SameAncientOffer(o, plan.ExpectedOffer)),
                _ => false
            };
            if (!same) local.Add(RouteCause("PlannedAncientOfferPending", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
            else applied = ValidateRouteAncientAcceptedPrefix(run, plan, dependencies, knownInstanceIds, local, token);
        }

        bindingTrace?.Complete(plan != null && local.Count == bindingCauseStart,
            local.Skip(bindingCauseStart).ToImmutableArray());

        var selectedParent = run._nodeReplayInputs.LastOrDefault(input => input is ChooseOpeningRelic or ChooseAncientOffer);
        var actualOffer = selectedParent switch
        {
            ChooseOpeningRelic opening => offers.FirstOrDefault(offer => offer.Value?.Relic == opening.Relic)?.Value,
            ChooseAncientOffer chosen => run.AncientOffers.FirstOrDefault(offer => offer.Id == chosen.OfferId),
            _ => null
        };
        PredictorAncientOptionalCards? optionalCards = null;
        ImmutableArray<PredictorCard> optionalActualCards = [];
        long? optionalSourceId = null;
        string? optionalReceipt = null;
        // Only these native pickers admit a real empty input. Mandatory deck,
        // bundle and health choices keep their ordinary execution boundary.
        bool emptyAllowed = run.Phase switch
        {
            PredictorPhase.ObtainChooseManyCards => CurrentRouteObtainSource(run)?.Entry == "SEA_GLASS",
            PredictorPhase.ObtainChooseCard => run._obtainCardSource.Entry is "LEAD_PAPERWEIGHT" or "HEFTY_TABLET" or "MASSIVE_SCROLL",
            PredictorPhase.ObtainDeckCards => run._obtainDeckSource.Entry == "CLAWS" && run._obtainDeckOperation == PredictorObtainDeckOperation.Claws,
            _ => false
        };
        if (local.Count == 0 && run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
            emptyAllowed &&
            ObserveRouteObtainChoice(run, dependencies, knownInstanceIds) is
                { MinimumCards: 0 } optional)
        {
            optionalCards = new(optional, applied, [], false,
                actualOffer ?? throw new InvalidOperationException("PredictorAncientOptionalParentMissing"));
            (optionalActualCards, optionalSourceId, optionalReceipt) = run.Phase switch
            {
                PredictorPhase.ObtainChooseCard => (run.ObtainCardChoices, run._obtainCardPending?.SourceId, "ChooseObtainCard"),
                PredictorPhase.ObtainChooseManyCards => (run.ObtainManyCardChoices, run._obtainManyPending?.SourceId, "SeaGlassChooseCards"),
                _ => (optional.DeckCards.Where(card => card.IsKnown).Select(card => card.Value).ToImmutableArray(),
                    run._obtainDeckPending?.SourceId, "ClawsTransform")
            };
        }
        if (local.Count == 0 && plan != null)
        {
            foreach (var child in plan.Children.Skip(applied))
            {
                ConfigureCompletedRewardEdits(run, new(run.Working.Position.NodeId) { Ancient = plan }, PredictorRewardEditOwner.Ancient, applied);
                if (run.Phase == PredictorPhase.Complete || !ApplyRouteObtainChild(run, child, dependencies, knownInstanceIds, local, token, traceAncient: true)) break;
                applied++;
            }
        }
        if (local.Count == 0 && optionalCards != null)
        {
            bool defaultSkipped = run.Phase == optionalCards.Choice.Phase &&
                CurrentRouteObtainSource(run) == optionalCards.Choice.Source;
            if (defaultSkipped)
            {
                PredictorInput skip = run.Phase switch
                {
                    PredictorPhase.ObtainChooseCard => new ChooseObtainCard(null),
                    PredictorPhase.ObtainChooseManyCards => new ChooseObtainManyCards([]),
                    _ => new ChooseObtainDeckCards([])
                };
                if (run.Submit(run.Request!, skip, token) != PredictorInputResult.Accepted)
                    throw new InvalidOperationException("PredictorDrawingAncientCardDefaultRejected");
                run._preview.Partial?.Facts.RecordResult("ancient.default.optionalCards", run.Working.Position.NodeId, "none",
                    origin: PredictorKnowledgeOrigin.ApprovedDefault,
                    operation: "LiveMapDefaults: complete the real optional " + optionalCards.Choice.Source.Serialized +
                        " picker with its native empty input; retain all candidates for editing.");
            }
            var acceptedCards = run._inputs.LastOrDefault(input => input.Action == optionalReceipt &&
                input.ObjectId == optionalSourceId)?.Targets ?? [];
            optionalCards = optionalCards with { DefaultSkipped = defaultSkipped,
                SelectedIndices = optionalActualCards.Select((card, index) => (card, index))
                    .Where(item => acceptedCards.Contains(item.card.Id)).Select(item => item.index).ToImmutableArray() };
        }
        if (local.Count == 0)
        {
            CompleteLiveAncientBundle(run, dependencies, token);
            var editPlan = plan ?? (actualOffer == null ? null : new PredictorPlannedAncient(ancient, actualOffer));
            if (editPlan != null)
                ConfigureCompletedRewardEdits(run, new(run.Working.Position.NodeId) { Ancient = editPlan }, PredictorRewardEditOwner.Ancient, applied);
            CaptureCompletedCardRewardEdits(run, dependencies);
            ContinueLiveDefaultCardRewardChildren(run, dependencies, token);
        }
        if (run.Phase != PredictorPhase.Complete && local.Count == 0)
            local.Add(RouteCause("AncientChildChoice", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
        var rewards = ObserveRouteRewards(run, dependencies);
        var required = ObserveRouteObtainChoice(run, dependencies, knownInstanceIds);
        return new(ancient, offers, required == null ? [] : [required], rewards,
            dependencies.Select(d => d.Cause).Concat(local).Distinct().ToImmutableArray())
        { Advanced = run.Phase == PredictorPhase.Complete, AppliedChildren = applied, OptionalCards = optionalCards,
            BundleChoice = run.ObserveAncientBundle(actualOffer, dependencies), Outcome = run.ObserveAncientOutcome(dependencies, rewards) };
    }

    private static bool SameAncientOffer(PredictorAncientOffer actual, PredictorAncientOffer expected)
        => actual with { Id = 0 } == expected with { Id = 0 };

    private static int ValidateRouteAncientAcceptedPrefix(PredictorRun run, PredictorPlannedAncient plan,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        var prefix = run.CreateEntryReplay(PreviewReplayPurpose.RoomObservation);
        prefix.RestoreRoutes(run._nodeEntryRoutes, run._preview.Settings.AcceptsPrefixes);
        prefix.AdvanceRoute(token);
        int applied = 0;
        foreach (var actual in run._nodeReplayInputs)
        {
            if (IsRouteObtainChildInput(actual))
            {
                if (applied < plan.Children.Length)
                {
                    var expected = ResolveRouteObtainChildInput(prefix, plan.Children[applied], dependencies, knownInstanceIds, local);
                    if (expected == null) return Math.Min(applied, plan.Children.Length);
                    if (!SameRouteObtainInput(expected, actual))
                    {
                        local.Add(RouteCause("PlannedObtainAcceptedPrefixChanged", run.Working.Position.NodeId, PredictorCombatDomain.Continuation));
                        return Math.Min(applied, plan.Children.Length);
                    }
                }
                applied++;
            }
            if (prefix.Request is not { } request || prefix.Submit(request, actual, token) is PredictorInputResult.Invalid or PredictorInputResult.Stale)
                throw new InvalidOperationException("PredictorAncientAcceptedPrefixReplayMismatch");
        }
        return Math.Min(applied, plan.Children.Length);
    }

    private static ImmutableArray<PredictorAncientOfferKnowledge> ObserveRouteAncientOffers(PredictorRun run,
        ModelKey ancient, List<RouteDependency> dependencies)
    {
        var offers = ancient.Entry == "NEOW"
            ? run.Working.OpeningOptions.Select(k => new PredictorAncientOffer(0, k, true, null, null, null, null, null)).ToImmutableArray()
            : run.AncientOffers;
        var observed = offers.Select((offer, index) =>
        {
            // These are the dynamic pools in Beta109AncientOptionProvider.
            // Each fixed-pool NextItem consumes one draw, so Tezcatara's later
            // slots and Orobas/Pael's first slot remain independent. A changed
            // shuffle pool (Nonupeipe/Tanx) changes every offered identity.
            var identityDomains = ancient.Entry switch
            {
                "OROBAS" when index == 2 => PredictorCombatDomain.Deck | PredictorCombatDomain.Relics,
                "PAEL" when index == 1 => PredictorCombatDomain.Deck,
                "PAEL" when index == 2 => PredictorCombatDomain.Deck | PredictorCombatDomain.Relics,
                "TEZCATARA" when index == 0 => PredictorCombatDomain.Deck,
                "NONUPEIPE" or "TANX" => PredictorCombatDomain.Deck,
                _ => PredictorCombatDomain.None
            };
            var identity = CausesFor(dependencies, identityDomains);
            var prepared = offer.Relic.Entry == "DUSTY_TOME" ? CausesFor(dependencies, PredictorCombatDomain.RewardsRng) : [];
            ImmutableArray<PredictorCombatPendingCause> character = offer.CharacterTargetKnown ? [] :
                [AncientCharacterTargetCause(run.Working.Position.NodeId, offer)];
            var target = offer.Relic.Entry == "ARCHAIC_TOOTH" ? CausesFor(dependencies, PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds) :
                offer.Relic.Entry == "TOUCH_OF_OROBAS" ? CausesFor(dependencies, PredictorCombatDomain.Relics | PredictorCombatDomain.InstanceIds) : [];
            var entry = run._ancientOutcomeBefore?.Position.NodeId == run.Working.Position.NodeId
                ? run._ancientOutcomeBefore : run.Working;
            var starterCard = target.IsEmpty && offer.StarterCardId is { } cardId
                ? entry.Cards.FirstOrDefault(card => card.Id == cardId) : null;
            var starterRelic = target.IsEmpty && offer.StarterRelicId is { } relicId
                ? entry.Relics.FirstOrDefault(relic => relic.Id == relicId) : null;
            var replacementPending = starterCard == null ? [] : CausesFor(dependencies, PredictorCombatDomain.Relics);
            return new PredictorAncientOfferKnowledge(index, identity.IsEmpty ? offer with
            { Id = 0, PreparedCard = prepared.IsEmpty ? offer.PreparedCard : null,
                CharacterTarget = character.IsEmpty ? offer.CharacterTarget : null,
                StarterCardId = target.IsEmpty ? offer.StarterCardId : null,
                StarterRelicId = target.IsEmpty ? offer.StarterRelicId : null } : null,
                identity.AddRange(prepared).AddRange(character).AddRange(target).Distinct().ToImmutableArray())
            { IdentityPendingCauses = identity, PreparedCardPendingCauses = prepared, CharacterTargetPendingCauses = character,
                TargetPendingCauses = target,
                StarterCard = starterCard == null ? null : DisplayObtainCard(starterCard),
                ReplacementCard = starterCard == null || !replacementPending.IsEmpty ? null :
                    DisplayObtainCard(PredictorObtainSources.PrepareAddedCard(run.Context, entry,
                        PredictorObtainSources.ArchaicToothReplacement(run.Context, starterCard) with { FloorAdded = entry.Position.Floor })),
                ReplacementPendingCauses = replacementPending,
                StarterRelic = starterRelic?.Key,
                ReplacementRelic = starterRelic == null ? null : PredictorObtainSources.TouchUpgrade(starterRelic.Key) };
        }).ToImmutableArray();
        run._preview.Partial?.RecordAncientOfferObservation(run, ancient, observed);
        return observed;
    }

    private static void ValidatePlannedAncient(PredictorPlannedAncient plan)
    {
        RequirePlan(plan.ExpectedAncient.IsValid && plan.ExpectedAncient.Category == "EVENT" &&
            plan.ExpectedOffer != null && !plan.Children.IsDefault);
        var offer = plan.ExpectedOffer;
        RequirePlan(offer.Id >= 0 && offer.Relic.IsValid && offer.Relic.Category == "RELIC" &&
            offer.StarterCardId is not < 1 && offer.StarterRelicId is not < 1 &&
            (offer.CharacterTarget == null || offer.CharacterTarget.Value.IsValid && offer.CharacterTarget.Value.Category == "CHARACTER") &&
            (offer.PreparedCard == null || offer.PreparedCard.Value.IsValid && offer.PreparedCard.Value.Category == "CARD"));
        foreach (var child in plan.Children) ValidatePlannedObtainChild(child);
    }

}
