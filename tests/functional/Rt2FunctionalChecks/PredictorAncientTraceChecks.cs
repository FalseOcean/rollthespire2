using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Search.FamilyExecution;

internal static class PredictorAncientTraceChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("ANCIENT_TRACE " + message); }
    private static ModelKey K(string entry) => new("RELIC", entry);

    internal static void Run(PredictorRun captured)
    {
        FutureGenerationAndIndependentSlots(captured);
        DustyPreparedCardAndBinding(captured);
        ManualBundleChild(captured);
        PreexistingSnapshotIsNotGeneration(captured);
        UnknownSeaGlassTarget(captured);
        Console.WriteLine("PREDICTOR_ANCIENT_TRACE_PASS native generator; per-slot profile reads; event-local isolation; Dusty setup split; authored binding/child facts; snapshot boundary; SeaGlass missing target admission/classification; preview purity");
    }

    // Native catalogs/provider/RNG are retained. Geometry places a real Ancient
    // ahead of the current Complete boundary so the recorder observes entry.
    private static PredictorRun Future(PredictorRun captured, string ancient, int ordinal = 0,
        PredictorCombatPendingCause? missing = null)
    {
        string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, (ulong)ordinal);
        var context = captured.Context with
        {
            Seed = seed, AncientAuthority = Beta109WorldSnapshotProjector.ProjectForSeed(captured.Context.AncientAuthority!, seed),
            Live = missing == null ? null : new(1, true, [missing])
        };
        var points = new[]
        {
            new MapPredictionPoint(new(3, 0), MapPointType.Ancient, true, [new(3, 1)]),
            new MapPredictionPoint(new(3, 1), MapPointType.Monster, true, [new(3, 2)]),
            new MapPredictionPoint(new(3, 2), MapPointType.Boss, true, [])
        };
        var state = captured.Checkpoints[0].State with
        {
            OpeningCompleted = true, StartedWithNeow = true, Visits = [],
            Position = new(2, 0, "ancient-trace:entry"),
            Maps = [new(2, captured.RouteMap(2) with { Points = points }, [])],
            Acts = captured.Checkpoints[0].State.Acts.Select(a => a.Act == 2 ? a with
            { Ancient = new("EVENT", ancient), NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 } : a).ToImmutableArray()
        };
        var run = new PredictorRun(context, state);
        run.SetRoute(2, points.Select(p => p.Position).ToImmutableArray());
        return run;
    }

    private static PredictorRouteNodeKnowledge Preview(PredictorRun run, PredictorPlannedNode? plan = null)
    {
        string state = JsonSerializer.Serialize(run.Working), checkpoints = JsonSerializer.Serialize(run.Checkpoints);
        var request = run.Request; var phase = run.Phase;
        var result = run.PreviewRouteContinuation(plan == null ? [] : [plan], 1,
            scope: PredictorRoutePreviewScope.AllModeledNodes).Nodes.Single();
        Check(state == JsonSerializer.Serialize(run.Working) && checkpoints == JsonSerializer.Serialize(run.Checkpoints) &&
            request == run.Request && phase == run.Phase, "Tracing mutated the caller or its checkpoints.");
        return result;
    }
    private static PredictorKnowledgeFact Fact(PredictorRouteNodeKnowledge node, string key) =>
        node.Diagnostics!.Snapshot.Facts.Last(fact => fact.NodeId == node.Position.NodeId && fact.PropertyKey == key);
    // Explain follows unresolved causal reads. Established provenance is in
    // AllReadFactIds and must not be mislabeled as unresolved merely to inspect it.
    private static ImmutableArray<PredictorKnowledgeFact> ReadClosure(PredictorRouteNodeKnowledge node, PredictorKnowledgeFact result)
    {
        var snapshot = node.Diagnostics!.Snapshot;
        var seen = new HashSet<PredictorKnowledgeFactId>();
        var pending = new Stack<PredictorKnowledgeFactId>();
        pending.Push(result.Id);
        while (pending.TryPop(out var id))
        {
            if (!seen.Add(id)) continue;
            foreach (var input in snapshot.Fact(id).AllReadFactIds) pending.Push(input);
        }
        return seen.Select(snapshot.Fact).ToImmutableArray();
    }
    private static PredictorPlannedNode Plan(PredictorRouteNodeKnowledge node, PredictorAncientOffer offer) =>
        new(node.Position.NodeId) { Ancient = new(node.Ancient!.Ancient, offer) };
    private static PredictorCombatPendingCause Missing(string key, PredictorCombatDomain domain) =>
        PredictorRouteCauseEvidence.CreateExplicit("AncientTraceMissing", "capture:ancient-trace", domain,
            PredictorPendingReasonKind.MissingObservation, "Controlled missing Ancient source input.", [key], occurrenceKey: key);

    private static void FutureGenerationAndIndependentSlots(PredictorRun captured)
    {
        var missing = Missing("deck.All.candidates", PredictorCombatDomain.Deck);
        var node = Preview(Future(captured, "TEZCATARA", missing: missing));
        var dynamic = Fact(node, "ancient.generate.offer.0.identity");
        var fixedSlot = Fact(node, "ancient.generate.offer.1.identity");
        var snapshot = node.Diagnostics!.Snapshot;
        Check(dynamic.Status == PredictorKnowledgeStatus.Unknown && dynamic.DisplayValue == null &&
            snapshot.Explain(dynamic.Id).Roots.Any(root => root.RootKey == missing.Identity),
            "Dynamic first slot lost the original deck root or leaked its representative identity.");
        Check(Fact(node, "ancient.compute.offer.0.eventLocal.readEvidence").Status == PredictorKnowledgeStatus.Unknown &&
            Fact(node, "ancient.compute.offer.0.eventLocal.readEvidence").DisplayValue == null,
            "A representative dynamic pool bound was exported as established provider evidence.");
        Check(fixedSlot.Status == PredictorKnowledgeStatus.Known && snapshot.Explain(fixedSlot.Id).Roots.IsEmpty &&
            !ReadClosure(node, fixedSlot).Any(f => f.PropertyKey.StartsWith("ancient.compute.profile.", StringComparison.Ordinal)),
            "Fixed second slot inherited a profile it never reads.");
        Check(Fact(node, "ancient.compute.eventLocal.calls").Status == PredictorKnowledgeStatus.Known &&
            !ReadClosure(node, fixedSlot).Any(f => f.PropertyKey.StartsWith("rng.Niche", StringComparison.Ordinal) ||
                f.PropertyKey.StartsWith("rng.UpFront", StringComparison.Ordinal)),
            "Ancient event-local generation incorrectly acquired a general state stream.");
        var published = Fact(node, "result.ancient.offer.1.identity");
        Check(published.AllReadFactIds.Contains(fixedSlot.Id), "Published independent identity is detached from its producer fact.");
    }

    private static (PredictorRun Run, PredictorRouteNodeKnowledge Node, int Ordinal) FindDusty(PredictorRun captured)
    {
        for (int ordinal = 0; ordinal < 128; ordinal++)
        {
            var run = Future(captured, "DARV", ordinal);
            var node = Preview(run);
            if (node.Ancient!.Offers.Any(o => o.Value?.Relic.Entry == "DUSTY_TOME")) return (run, node, ordinal);
        }
        throw new InvalidOperationException("ANCIENT_TRACE Native Darv Dusty Tome fixture not found.");
    }

    private static void DustyPreparedCardAndBinding(PredictorRun captured)
    {
        var known = FindDusty(captured);
        var offer = known.Node.Ancient!.Offers.Single(o => o.Value?.Relic.Entry == "DUSTY_TOME");
        var missing = Missing("rng.Rewards.state", PredictorCombatDomain.RewardsRng);
        var run = Future(captured, "DARV", known.Ordinal, missing);
        var node = Preview(run, Plan(known.Node, offer.Value!));
        var identity = Fact(node, "ancient.generate.offer." + offer.Index + ".identity");
        var prepared = Fact(node, "ancient.generate.offer." + offer.Index + ".preparedCard");
        var snapshot = node.Diagnostics!.Snapshot;
        Check(identity.Status == PredictorKnowledgeStatus.Known && snapshot.Explain(identity.Id).Roots.IsEmpty &&
            prepared.Status == PredictorKnowledgeStatus.Unknown && prepared.DisplayValue == null &&
            snapshot.Explain(prepared.Id).Roots.Any(root => root.RootKey == missing.Identity),
            "Dusty Tome's Rewards-dependent prepared card contaminated the offered relic or lost its source.");
        Check(Fact(node, "ancient.compute.rng.Rewards.consumed").DisplayValue == "1" &&
            Fact(node, "ancient.write.rng.Rewards.after").Status == PredictorKnowledgeStatus.Unknown,
            "Fixed draw count was conflated with the incoming/outgoing Rewards cursor.");
        var blocked = Fact(node, "ancient.binding.1.acceptedSelection");
        Check(blocked.Status == PredictorKnowledgeStatus.Unknown && snapshot.Explain(blocked.Id).Roots.Any(root => root.RootKey == missing.Identity),
            "Blocked authored setup binding did not retain its actual prepared-card read root.");
        var selected = Preview(known.Run, Plan(known.Node, offer.Value!));
        var write = Fact(selected, "ancient.selection.prepared.preparedCard");
        var authored = Fact(selected, "ancient.binding.1.input.expectedOffer");
        Check(write.Status == PredictorKnowledgeStatus.Known && write.DisplayValue == offer.Value!.PreparedCard!.Value.Serialized &&
            authored.Origin == PredictorKnowledgeOrigin.PlayerInput && ReadClosure(selected, write).Any(f => f.Id == authored.Id),
            "Prepared obtain entry lost the player's semantic selection source.");
        Check(Fact(selected, "ancient.selection.resolved.offerId").Origin == PredictorKnowledgeOrigin.Derived,
            "Route-mapped temporary offer ID was mislabeled as direct player input.");
    }

    private static PredictorRun Opening(PredictorRun captured)
    {
        var future = Future(captured, "TEZCATARA");
        var state = future.Working with { OpeningCompleted = false, StartedWithNeow = true,
            Position = new(1, 0, "ancient-trace:opening"), OpeningOptions = [K("SCROLL_BOXES"), K("GOLDEN_PEARL"), K("STONE_HUMIDIFIER")],
            Maps = [new(1, captured.RouteMap(1) with { Points = future.Working.Maps[0].Map.Points }, [])] };
        var run = new PredictorRun(future.Context, state);
        run.SetRoute(1, state.Maps[0].Map.Points.Select(p => p.Position).ToImmutableArray());
        run.AdvanceRoute();
        return run;
    }

    private static void ManualBundleChild(PredictorRun captured)
    {
        var run = Opening(captured);
        var initial = Preview(run);
        var plan = Plan(initial, initial.Ancient!.Offers.Single(o => o.Value?.Relic.Entry == "SCROLL_BOXES").Value!);
        var child = Preview(run, plan).Ancient!.RequiredChoices.Single();
        var input = new PredictorPlannedObtainChild(PredictorObtainChildAction.ChooseBundle, K("SCROLL_BOXES"))
        { BundleIndex = 0, ExpectedBundles = child.Bundles };
        var node = Preview(run, plan with { Ancient = plan.Ancient! with { Children = [input] } });
        Check(node.Advanced && Fact(node, "ancient.child.1.input.bundleIndex").Origin == PredictorKnowledgeOrigin.PlayerInput &&
            Fact(node, "ancient.child.1.write.phase").DisplayValue == PredictorPhase.Complete.ToString(),
            "Native Scroll Boxes child selection did not preserve player provenance or actual accepted phase.");
        var reads = ReadClosure(node, Fact(node, "ancient.child.1.write.phase"));
        Check(reads.Any(f => f.PropertyKey == "ancient.child.1.read.actualCardOffer") &&
            reads.Any(f => f.PropertyKey == "ancient.child.1.read.dependency.RewardsRng") &&
            !reads.Any(f => f.PropertyKey.Contains("Niche", StringComparison.Ordinal)),
            "Bundle child did not record its actual pool binding/Rewards gate or acquired an unrelated stream.");
        var stale = Preview(run, plan with { Ancient = plan.Ancient! with { Children = [input with { ExpectedSource = K("NEW_LEAF") }] } });
        Check(Fact(stale, "ancient.child.1.resolvedSelection").Status == PredictorKnowledgeStatus.Unknown &&
            !stale.Diagnostics!.Snapshot.Facts.Any(f => f.PropertyKey == "ancient.child.1.input.bundleIndex"),
            "A source mismatch must stop before reading later bundle inputs.");
    }

    private static void PreexistingSnapshotIsNotGeneration(PredictorRun captured)
    {
        var run = Future(captured, "TEZCATARA"); run.AdvanceRoute();
        var node = Preview(run);
        var observation = Fact(node, "ancient.observe.offer.1.identity");
        Check(observation.Status == PredictorKnowledgeStatus.Known &&
            !node.Diagnostics!.Snapshot.Facts.Any(f => f.PropertyKey.StartsWith("ancient.generate.", StringComparison.Ordinal)),
            "Already entered Ancient was falsely presented as an observed generator execution.");
        Check(Fact(node, "result.ancient.offer.1.identity").AllReadFactIds.Contains(observation.Id),
            "Published snapshot identity is detached from its real observation fact.");
    }

    private static PredictorRun SeaGlassTargetFixture(PredictorRun captured, int ordinal, bool noOtherCandidate, bool sourceOrderKnown)
    {
        var original = Future(captured, "OROBAS", ordinal);
        var authority = original.Context.AncientAuthority!;
        var context = original.Context with { AncientAuthority = authority with
        {
            AncientEventContexts = authority.AncientEventContexts.Select(c => c.Act == 2 && c.AncientKey.Entry == "OROBAS" ? c with
            {
                UnlockedCharacterSourceOrderExact = sourceOrderKnown,
                UnlockedCharacters = noOtherCandidate ? [c.CharacterKey] : c.UnlockedCharacters
            } : c).ToArray()
        } };
        var run = new PredictorRun(context, original.Working);
        run.SetRoute(2, original.Routes.Single(r => r.Act == 2).Points);
        return run;
    }

    private static void UnknownSeaGlassTarget(PredictorRun captured)
    {
        var legacy = new PredictorAncientOffer(0, K("SEA_GLASS"), true, captured.Context.Character, null, null, null, null);
        var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(legacy))!.AsObject();
        legacyJson.Remove(nameof(PredictorAncientOffer.CharacterTargetKnown));
        legacyJson.Remove(nameof(PredictorAncientOffer.CharacterTargetSourceOrderKnown));
        legacyJson.Remove(nameof(PredictorAncientOffer.CharacterTargetIssueCode));
        Check(legacyJson.Deserialize<PredictorAncientOffer>() is { CharacterTargetKnown: true, CharacterTargetSourceOrderKnown: true },
            "An old complete offer payload without knowledge fields did not retain its default known-target contract.");
        foreach (var limitation in new[] { (NoOther: false, OrderKnown: false), (NoOther: true, OrderKnown: true), (NoOther: true, OrderKnown: false) })
        {
            bool noOtherCandidate = limitation.NoOther;
            bool unavailable = noOtherCandidate && limitation.OrderKnown;
            PredictorRun? chosen = null;
            PredictorRouteNodeKnowledge? selected = null;
            int chosenOrdinal = 0;
            for (int ordinal = 0; ordinal < 128; ordinal++)
            {
                var candidate = SeaGlassTargetFixture(captured, ordinal, noOtherCandidate, limitation.OrderKnown);
                var node = Preview(candidate);
                if (!node.Ancient!.Offers.Any(o => o.Value?.Relic.Entry == "SEA_GLASS")) continue;
                chosen = candidate; selected = node; chosenOrdinal = ordinal; break;
            }
            Check(chosen != null && selected != null, "Native Orobas Sea Glass target fixture not found.");
            var knowledge = selected!.Ancient!.Offers.Single(o => o.Value?.Relic.Entry == "SEA_GLASS");
            var cause = knowledge.CharacterTargetPendingCauses.Single();
            Check(knowledge.Value is { CharacterTargetKnown: false, CharacterTarget: null } && !knowledge.IsKnown &&
                knowledge.IdentityPendingCauses.IsEmpty && knowledge.PreparedCardPendingCauses.IsEmpty && knowledge.TargetPendingCauses.IsEmpty &&
                knowledge.PendingCauses.Any(c => c.Identity == cause.Identity),
                "Unknown provider target was flattened into known absence or contaminated independent setup fields.");
            var kind = unavailable ? PredictorPendingReasonKind.KnownUnavailable : PredictorPendingReasonKind.MissingObservation;
            Check(cause.ReasonKind == kind && cause.ChoiceId == null &&
                (unavailable ? cause.RequiredPropertyKeys.IsEmpty : cause.RequiredPropertyKeys.SequenceEqual(["ancient.unlockedCharacters.sourceOrder"])),
                "Target limitation was misclassified or replaced with a fictitious player choice.");
            string prefix = "result.ancient.offer." + knowledge.Index;
            Check(Fact(selected, prefix + ".identity").Status == PredictorKnowledgeStatus.Known &&
                Fact(selected, prefix + ".characterTarget").Status == PredictorKnowledgeStatus.Unknown &&
                Fact(selected, prefix + ".characterTarget").DisplayValue == null &&
                Fact(selected, prefix + ".starterCardTarget").Status == PredictorKnowledgeStatus.Known &&
                Fact(selected, prefix + ".starterRelicTarget").Status == PredictorKnowledgeStatus.Known,
                "Published target mask hid independent identity/starter absence or exported target none as a fact.");
            var roots = selected.Diagnostics!.Snapshot.Explain(Fact(selected, prefix + ".characterTarget").Id).Roots;
            Check(roots.Any(root => root.RootKey == cause.Identity && root.Kind ==
                (unavailable ? PredictorKnowledgeRootKind.KnownUnavailable : PredictorKnowledgeRootKind.MissingObservation)) &&
                roots.All(root => root.Kind != PredictorKnowledgeRootKind.UserChoice), "Character target explanation lost its classified provider root.");
            Check(selected.Ancient.Offers.Where(o => o.Index != knowledge.Index && o.Value?.Selectable == true).Any(o => o.IsKnown),
                "One missing Sea Glass setup blocked every independent Orobas offer.");

            var planned = Preview(chosen!, Plan(selected, knowledge.Value!));
            Check(planned.Phase == PredictorPhase.AncientChoice && planned.Ancient!.RequiredChoices.IsEmpty &&
                planned.PendingCauses.SelectMany(c => c.Parents).Any(c => c.Identity == cause.Identity),
                "An unresolved target was accepted and used to generate a card pool.");
            if (!noOtherCandidate)
            {
                var knownNode = Preview(Future(captured, "OROBAS", chosenOrdinal));
                var knownOffer = knownNode.Ancient!.Offers.Single(o => o.Value?.Relic.Entry == "SEA_GLASS").Value!;
                Check(knownOffer.CharacterTargetKnown && knownOffer.CharacterTarget != null,
                    "The corresponding native known-target control was not established.");
                var staleKnownPlan = Preview(chosen!, Plan(knownNode, knownOffer));
                Check(staleKnownPlan.PendingCauses.SelectMany(c => c.Parents).Any(c => c.Identity == cause.Identity),
                    "A previously complete semantic binding lost the new missing-target source when it no longer matched.");
            }

            chosen!.AdvanceRoute();
            var offer = chosen.AncientOffers.Single(o => o.Relic.Entry == "SEA_GLASS");
            string before = JsonSerializer.Serialize(chosen.Working), checkpoints = JsonSerializer.Serialize(chosen.Checkpoints),
                draft = JsonSerializer.Serialize(chosen.CaptureNodeDraft());
            var request = chosen.Request;
            Check(chosen.Submit(request!, new ChooseAncientOffer(offer.Id)) == PredictorInputResult.Invalid &&
                chosen.Phase == PredictorPhase.AncientChoice && chosen.Request == request && before == JsonSerializer.Serialize(chosen.Working) &&
                checkpoints == JsonSerializer.Serialize(chosen.Checkpoints) && draft == JsonSerializer.Serialize(chosen.CaptureNodeDraft()),
                "Direct unresolved-target submission mutated state, replay prefix or the local choice boundary.");
            var existing = Preview(chosen);
            Check(Fact(existing, "ancient.observe.offer." + knowledge.Index + ".characterTarget").Status == PredictorKnowledgeStatus.Unknown,
                "Already entered unknown provider target became known none during snapshot observation.");
        }
    }
}
