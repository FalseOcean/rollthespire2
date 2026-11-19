using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

internal static partial class PartyPhase1Checks
{
    // Export the existing native runtime capture for ordinary Production replay.
    // Capturing, finding a positive fixture and compilation are outside timed runs.
    private static void ExportCpuCorpus(string output, GameVersionDetection detection)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        var diagnostics = new List<object>();
        foreach (int count in new[] { 2, 3, 4 })
        {
            var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, "000000000000",
                BaseGameModelKeys.Characters.All.Take(count).ToArray(),
                Enumerable.Range(0, count).Select(_ => UnlockState.all.ToSerializable()).ToArray(), 10, "0.111.0");
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
                10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
            string directory = Path.Combine(output, count + "p");
            Directory.CreateDirectory(directory);
            ProductionSearchReplay.Write(Path.Combine(directory, "context.json"),
                new ProductionSearchReplayContext(1, "NativeRuntimePartyCapture.AllUnlocks.FixedOrderedRoster", context));
            foreach (int slot in new[] { 0, count - 1 })
            {
                ulong ordinal = Enumerable.Range(0, 4096).Select(i => (ulong)i).First(i =>
                    ModernNeowIdentityPredictor.PredictModernCore(
                        VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, i), party.Players[slot],
                        Beta111Profile.Instance, true).RelicKeys.Contains(BaseGameModelKeys.Relics.LeafyPoultice));
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
                var projection = Beta111NeowEffectProjector.ProjectFromRootHash(BaseGameModelKeys.Relics.LeafyPoultice,
                    PredictionPrecision.Exact, party.Players[slot], root);
                var targets = projection.EffectGroups.SelectMany(g => g.OrderedItems)
                    .Where(e => e.Kind == PredictedEffectKind.TransformCard && e.TargetKey.HasValue)
                    .Select(e => e.TargetKey!.Value).ToArray();
                if (targets.Length != 2) throw new InvalidDataException("PartyCpuCorpus.LeafyOutputs");
                var players = party.Players.Select((owner, index) =>
                {
                    var offers = ModernNeowIdentityPredictor.PredictModernCore(seed, owner, Beta111Profile.Instance, true).RelicKeys;
                    var local = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with {
                        NeowRelics = new([offers[1], BaseGameModelKeys.Relics.NewLeaf], [], []) } };
                    if (index == slot) local = local with {
                        // Selected-child Exact projects its one acquired choice.
                        // Keep the active legacy gate true in that child witness;
                        // the independent whole-table offer gate remains separate.
                        LegacyNeow = local.LegacyNeow with { NeowRelics = new(
                            [BaseGameModelKeys.Relics.LeafyPoultice, offers[1]], [], []) },
                        OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice),
                        StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.LeafyPoultice,
                            NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.TransformResults,
                            NeowStructuredOutputKind.Card, targets, AllowDuplicateOutputs: true)] };
                    return new PlayerOfferQuery(index, new([offers[0], BaseGameModelKeys.Relics.ArcaneScroll], [], []))
                        { Conditions = local };
                }).ToArray();
                var query = SearchQuery.Empty with { Players = players };
                if (count == 2)
                    CheckDirectLeafyPairSurvival(context, slot, players[slot].Conditions, seed);
                Godot.GD.Print($"PARTY_CPU_N_FIXTURE players={count};slot={slot};seed={seed};producerBaselineAdjusted=true;activeLegacyAny=SelectedLeafyOrObservedPositive1;wholeTableAny=ObservedPositive0OrArcane;passiveAnyGatesUnchanged=true;selectedChildProjectsOneAcquiredChoice=true");
                var compiled = SearchCompiler.Compile(query, context);
                var request = SearchPreparationCompiler.Compile(compiled, new(seed, 1, 1, 16)).Plan
                    ?? throw new InvalidDataException("PartyCpuCorpus.Compile");
                if (!TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance, root, seed, out var bound, out var issue))
                    throw new InvalidDataException(issue);
                // Preserve the exact authored query/seed before acceptance, so a
                // rejected producer fixture remains directly replayable.
                ProductionSearchReplay.Write(Path.Combine(directory, "N-slot" + slot + "-positive-request.json"),
                    new ProductionSearchReplayWorkload(1, query, new(seed, 1, 1, 16, true)));
                var exact = ProductionExactSearchEvaluator.Evaluate(request.Model, bound);
                if (!exact.IsMatch)
                {
                    var shared = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, 0);
                    var children = new List<object>();
                    foreach (var personal in compiled.PlayerSearches.Take(count))
                    {
                        var owner = personal.Context.Authority;
                        var child = SearchPreparationCompiler.ForPlayer(request.Model, personal);
                        if (!SeedPredictionRequest.TryCreateFromRootHash(bound, owner.Character, owner.Ascension,
                            owner.PlayersCount, owner.PlayerSlotIndex, owner, personal.Context.EvaluationAssumptions.AncientEligibilityAssumptions,
                            SeedPredictionDomainSelection.Neow, 10, false, out var childPrediction, out var childError))
                            throw new InvalidDataException("PartyCpuCorpus.DiagnosticChild:" + childError);
                        var choices = PartyNeowProjection.ProjectChoices(childPrediction!, root, shared.Niche, shared.CombatPotionGeneration,
                            true, PartyInitialQuery.CapsuleEffectPremise(personal.Query));
                        var selected = personal.Query.OpeningRoute?.RouteRelicKey;
                        var pinnedChoices = selected is null ? choices : choices.Where(c => c.RelicKey == selected).ToArray();
                        var childExact = ProductionExactSearchEvaluator.Evaluate(child with { PartyOpeningChoices = pinnedChoices }, bound);
                        children.Add(new { Slot = owner.PlayerSlotIndex, personal.Query, personal.NormalizedQuery,
                            childExact.IsMatch, childExact.FailureCode, childExact.Evaluation,
                            Choices = pinnedChoices.Select(c => new { c.RelicKey, c.EffectGroups }).ToArray() });
                    }
                    File.WriteAllText(Path.Combine(directory, "N-slot" + slot + "-positive-failure.json"),
                        System.Text.Json.JsonSerializer.Serialize(new { Players = count, ActiveSlot = slot, Seed = seed, Root = root,
                            Targets = targets, Query = query, compiled.NormalizedQuery, OriginalProjection = projection,
                            exact.IsMatch, exact.FailureCode, exact.Evaluation, Children = children }, ProductionSearchReplay.JsonOptions));
                    throw new InvalidDataException("PartyCpuCorpus.PositiveExact:" + count + ":" + slot + ":" + exact.FailureCode);
                }
                ProductionSearchReplay.Write(Path.Combine(directory, "N-slot" + slot + ".json"),
                    new ProductionSearchReplayWorkload(1, query, new("000000000000", 1L << 34, int.MaxValue, 16, false)));
                diagnostics.Add(new { Players = count, ActiveSlot = slot, Positive = seed,
                    ProducerBaselineAdjusted = true,
                    BaselineAdjustment = "Generated benchmark only: active LegacyAny changed from ObservedPositive1OrNewLeaf to SelectedLeafyOrObservedPositive1; selected-child Exact projects one acquired choice. Passive independent Any gates and whole-table offer gate are unchanged.",
                    SharedArrival = "UnselectedCompanions;LeafyIndependentTransformStream", Context = context.Party!.Fingerprint });
            }
            ExportCpuCombatCorpus(directory, context, party, detection);
            foreach (var owner in party.Players)
            {
                var buckets = owner.WorldAuthority!.Beta109Generation!.PlayerRelicBuckets
                    .Where(b => new[] { "Common", "Uncommon", "Rare" }.Contains(
                        b.BucketId.Split(':')[^1], StringComparer.OrdinalIgnoreCase)).ToArray();
                var unresolved = buckets.SelectMany(b => b.OrderedRelics).Distinct().Where(key =>
                    !VanillaRelicRewardEffects.TryGet(RuntimeProfileId.Beta111, key, out var capability) ||
                    !capability.NestedOnObtainPreservesRewardContinuation).ToArray();
                diagnostics.Add(new { Players = count, Slot = owner.PlayerSlotIndex,
                    RarityBuckets = buckets.Select(b => b.BucketId).ToArray(),
                    RarityPoolEntries = buckets.Sum(b => b.OrderedRelics.Count),
                    CapsuleContinuationBlockingOrUnknownKeys = unresolved });
            }
        }
        File.WriteAllText(Path.Combine(output, "capture-evidence.json"),
            System.Text.Json.JsonSerializer.Serialize(diagnostics, ProductionSearchReplay.JsonOptions));
        Godot.GD.Print("PARTY_CPU_CORPUS_PASS players=2,3,4;earlyLateSlots=true;nativeCaptured=true;positiveExact=true");
    }

    private static void CheckDirectLeafyPairSurvival(SearchContext context, int slot, SearchQuery local, string seed)
    {
        var pair = local.StructuredOpeningEffects.Single();
        var single = pair with { OutputKeys = [pair.OutputKeys[0]] };
        var otherSlot = context.Party!.Players.Single(player => player.PlayerSlotIndex != slot);
        var otherTargets = Beta111NeowEffectProjector.ProjectFromRootHash(BaseGameModelKeys.Relics.LeafyPoultice,
                PredictionPrecision.Exact, otherSlot, Beta111Profile.Instance.ComputeRootSeed(seed)).EffectGroups
            .SelectMany(group => group.OrderedItems).Where(item => item.Kind == PredictedEffectKind.TransformCard && item.TargetKey.HasValue)
            .Select(item => item.TargetKey!.Value).ToArray();
        if (otherTargets.Length != 2) throw new InvalidDataException("PartyCpuCorpus.OtherLeafyOutputs");
        var otherSingle = pair with { OutputKeys = [otherTargets[0]] };

        CompiledSearch Compile(NeowStructuredEffectSearchCondition[] rows, bool other = false, bool bindRoute = true)
        {
            var active = SearchQuery.Empty with { OpeningRoute = bindRoute ? local.OpeningRoute : null,
                StructuredOpeningEffects = rows };
            var companion = !other ? SearchQuery.Empty : SearchQuery.Empty with { OpeningRoute = local.OpeningRoute,
                StructuredOpeningEffects = rows.Length > 0 ? [otherSingle] : [] };
            var players = context.Party!.Players.Select(p => new PlayerOfferQuery(p.PlayerSlotIndex, ModelKeySetFilter.Empty)
                { Conditions = p.PlayerSlotIndex == slot ? active : companion }).ToArray();
            return SearchCompiler.Compile(SearchQuery.Empty with { Players = players }, context);
        }
        FamilySurvivalProjection Survival(CompiledSearch search, bool gpu)
        {
            var request = SearchPreparationCompiler.Compile(search, new(seed, 1, 1, 1)).Plan!;
            return FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: gpu, gpuFloat64Available: true)
                .Single(f => f.FamilyId == "N.Neow").Survival;
        }

        var duplicate = pair with { OutputKeys = [pair.OutputKeys[0], pair.OutputKeys[0]] };
        foreach (var (rows, other) in new[] { (new[] { pair }, false), (new[] { duplicate }, false),
            (new[] { single }, false), (new[] { single }, true), (new[] { pair }, true) })
        {
            var search = Compile(rows, other);
            var expected = PartyQueryProbability.Estimate(search).Probability;
            foreach (bool gpu in new[] { false, true })
            {
                var result = Survival(search, gpu);
                if (expected is null || result.SurvivalProbability != expected ||
                    !(expected < Survival(Compile([], other), gpu).SurvivalProbability) ||
                    !result.Evidence.Contains($"CheckedLeafySeats={(other ? 2 : 1)};", StringComparison.Ordinal))
                    throw new InvalidDataException($"PartyCpuCorpus.DirectLeafySurvival:slot={slot};other={other};gpu={gpu}");
            }
        }
        var remainder = pair with { OutputKeys = [new("CARD", "NO_SUCH_CARD")] };
        foreach (var search in new[] { Compile([pair], bindRoute: false), Compile([pair, remainder], other: true) })
        foreach (bool gpu in new[] { false, true })
            if (!Survival(search, gpu).Evidence.StartsWith("PartyN.OfferGateUpperEnvelope;", StringComparison.Ordinal))
                throw new InvalidDataException($"PartyCpuCorpus.DirectLeafyRemainder:slot={slot};gpu={gpu}");
    }
}
