using Godot;
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
using RolltheSpire2.Search.Semantics;

internal static partial class PartyPhase1Checks
{
    private static async Task CheckPartyCombatOpenings(GameVersionDetection detection)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, "000000000000",
            [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent],
            [UnlockState.all.ToSerializable(), UnlockState.all.ToSerializable()], 10, "0.111.0");
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
            10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Party C openings: " + message);
            checks++;
        }
        var hostArgs = OS.GetCmdlineUserArgs();
        int outputAt = Array.IndexOf(hostArgs, "--output");
        Check(outputAt >= 0 && outputAt + 1 < hostArgs.Length, "output directory supplied");
        string evidenceDirectory = Path.Combine(Path.GetFullPath(hostArgs[outputAt + 1]), "party-c-openings");
        Directory.CreateDirectory(evidenceDirectory);
        SearchQuery Table(SearchQuery personal, bool priorNewLeaf = false) => SearchQuery.Empty with
        {
            Players = [new(0, ModelKeySetFilter.Empty) { Conditions = priorNewLeaf
                ? SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.NewLeaf) } : SearchQuery.Empty },
                new(1, ModelKeySetFilter.Empty) { Conditions = personal }]
        };
        PreparedSearch Compile(SearchQuery query, string seed)
        {
            var result = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context), new(seed, 129, 1, 1));
            Check(result.Success, "compile: " + result.Issue);
            return result.Plan!;
        }
        SeedPredictionRequest SourceRequest(TrustedRootHashInput input, int slot)
        {
            var owner = party.Players[slot];
            Check(SeedPredictionRequest.TryCreateFromRootHash(input, owner.Character, 10, 2, slot, owner,
                AncientOptionConditionProfile.BroadDefault, SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards,
                10, false, out var result, out var error), "source request: " + error);
            return result!;
        }
        IReadOnlyList<NeowChoiceResult> Choices(TrustedRootHashInput input, SearchQuery personal, bool priorNewLeaf)
        {
            var rng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, input.RootHash, 0);
            if (priorNewLeaf)
            {
                var before = PartyNeowProjection.ProjectChoices(SourceRequest(input, 0), input.RootHash,
                    rng.Niche, rng.CombatPotionGeneration, authoredCapsuleEffects: PartyInitialQuery.CapsuleEffectPremise(SearchQuery.Empty));
                var leaf = before.Single(c => c.RelicKey == BaseGameModelKeys.Relics.NewLeaf);
                var continuation = leaf.OpeningRewardContinuations!.Routes.Single();
                Check(continuation.NicheState is not null && continuation.CombatPotionGenerationState is not null,
                    "prior NewLeaf has exact shared continuation");
                rng = rng.WithShared(continuation.NicheState!.Restore(), continuation.CombatPotionGenerationState!.Restore());
            }
            var choices = PartyNeowProjection.ProjectChoices(SourceRequest(input, 1), input.RootHash,
                rng.Niche, rng.CombatPotionGeneration, authoredCapsuleEffects: PartyInitialQuery.CapsuleEffectPremise(personal));
            if (personal.StructuredOpeningEffects.Count == 0 && personal.OpeningRoute is { } selected &&
                (selected.RouteRelicKey == BaseGameModelKeys.Relics.SmallCapsule ||
                 selected.RouteRelicKey == BaseGameModelKeys.Relics.LargeCapsule ||
                 selected.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones))
            {
                var continuations = choices.Where(c => c.RelicKey == selected.RouteRelicKey)
                    .SelectMany(c => c.OpeningRewardContinuations?.Routes ?? []);
                Check(continuations.All(c => !c.UnknownReasonCodes.Any(reason =>
                    reason.StartsWith("NestedOpeningRewardImpactNotImplemented:", StringComparison.Ordinal))),
                    "unlisted nested obtains do not create a false unsupported-obtain continuation");
            }
            return choices;
        }
        NormalCombatRewardSequencePredictionResult Rewards(TrustedRootHashInput input, IReadOnlyList<NeowChoiceResult> choices,
            NormalCombatRewardProjectionRequest selection) => NormalCombatRewardSequencePredictor.PredictFromRootHash(
                Beta111Profile.Instance, input.RootHash, input.CanonicalSeed, 1, 10, choices,
                party.Players[1].EffectAuthority, party.Players[1].WorldAuthority, selection);
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var small = BaseGameModelKeys.Relics.SmallCapsule;
        var large = BaseGameModelKeys.Relics.LargeCapsule;
        var leafKey = BaseGameModelKeys.Relics.NewLeaf;
        var kaleidoscope = BaseGameModelKeys.Relics.Kaleidoscope;
        // These immutable-prefix checks need no fortunate offered root. Counts
        // come from the captured deck and the mandatory Neow's Fury addition.
        (int? Niche, int? Potions) Advance(SearchQuery personal)
        {
            var parent = Compile(Table(personal), "000000000000");
            var child = SearchPreparationCompiler.ForPlayer(parent.Model, parent.Model.CompiledSearch.PlayerSearches[1]);
            return NeowFamily.PartyInitial.SharedAdvance(child, NeowReplayPlan.Compile(child, false));
        }
        var deck = party.Players[1].EffectAuthority!.OrderedDeck!;
        int attacks = deck.Count(c => c.CanUpgrade && c.CardType == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack);
        int skills = deck.Count(c => c.CanUpgrade && c.CardType == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill);
        Check(attacks > 0 && skills > 0, "prefix fixture has upgradeable attacks and skills");
        var whetstone = BaseGameModelKeys.OrdinaryRelics.Whetstone;
        var warPaint = BaseGameModelKeys.OrdinaryRelics.WarPaint;
        var torment = BaseGameModelKeys.Relics.NeowsTorment;
        NeowStructuredEffectSearchCondition Nested(ModelKey source, params ModelKey[] targets) => new(source,
            source == bones ? NeowStructuredConditionKind.ExactGroupedCapsuleMultiset : source == small
                ? NeowStructuredConditionKind.ExactSingle : NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, targets);
        SearchQuery OrderedCapsule(ModelKey capsule, ModelKey nested, bool capsuleFirst, bool parentScoped) =>
            SearchQuery.Empty with
            {
                OpeningRoute = new(bones),
                OpeningRouteRelicRequirement = new(bones, capsuleFirst ? [capsule, torment] : [torment, capsule], BonesRouteOrderMode.ExactOrder),
                StructuredOpeningEffects = [Nested(parentScoped ? bones : capsule, nested)]
            };
        foreach (var capsule in new[] { small, large })
        foreach (var nested in new[] { whetstone, warPaint })
        {
            int baseEligible = nested == whetstone ? attacks : skills;
            var direct = Advance(SearchQuery.Empty with { OpeningRoute = new(capsule), StructuredOpeningEffects = [Nested(capsule, nested)] });
            Check(direct == (Math.Max(0, baseEligible - 1), 0), "direct authored upgrade shared shuffle count");
            var before = Advance(OrderedCapsule(capsule, nested, true, false));
            var after = Advance(OrderedCapsule(capsule, nested, false, false));
            Check(before == (Math.Max(0, baseEligible - 1) + 1, 0), "Capsule first includes one final Bones curse draw");
            Check(after == (Math.Max(0, baseEligible + (nested == whetstone ? 1 : 0) - 1) + 1, 0),
                "Torment first changes only Whetstone upgrade eligibility");
            Check(before == Advance(OrderedCapsule(capsule, nested, true, true)) &&
                after == Advance(OrderedCapsule(capsule, nested, false, true)), "Bones-parent upgrade maps to its only Capsule in either order");
            var anyOrder = Advance(OrderedCapsule(capsule, nested, true, true) with
            { OpeningRouteRelicRequirement = new(bones, [capsule, torment], BonesRouteOrderMode.AnyOrder) });
            Check(nested == whetstone ? anyOrder.Niche is null && anyOrder.Potions == 0 : anyOrder == before,
                "differing Bones order counts remain unknown; equal counts remain known");
        }
        var bothUpgrades = Advance(SearchQuery.Empty with { OpeningRoute = new(large),
            StructuredOpeningEffects = [Nested(large, whetstone, warPaint)] });
        Check(bothUpgrades == (Math.Max(0, attacks - 1) + Math.Max(0, skills - 1), 0), "large Capsule sums separately authored W/WP shuffles");
        var ambiguous = Advance(SearchQuery.Empty with { OpeningRoute = new(bones),
            OpeningRouteRelicRequirement = new(bones, [small, large], BonesRouteOrderMode.ExactOrder),
            StructuredOpeningEffects = [Nested(bones, whetstone)] });
        Check(ambiguous.Niche is null && ambiguous.Potions == 0, "two Capsules do not invent ownership of a Bones-parent upgrade");
        Check(Advance(SearchQuery.Empty) == (0, 0), "unselected N consumes no shared prefix");
        Check(Advance(SearchQuery.Empty with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered,
            [party.Players[1].EffectAuthority!.CharacterRewardPool![0].CardKey]) }) == (0, 0),
            "no-N C preserves the neutral zero shared prefix");
        GD.Print("PARTY_COMBAT_SHARED_ADVANCE_PASS parentCapsuleMapping=true;orderedWhetstoneTorment=true;warPaint=true;noNZero=true");
        var fixtures = new (string Name, ModelKey? Selected, ModelKey[] Pair, BonesRouteOrderMode Order, bool PriorNewLeaf)[]
        {
            ("NoNNeutral", null, [], BonesRouteOrderMode.AnyOrder, false),
            ("SmallCapsuleAfterNewLeaf", small, [], BonesRouteOrderMode.AnyOrder, true),
            ("LargeCapsuleAfterNewLeaf", large, [], BonesRouteOrderMode.AnyOrder, true),
            ("BonesSmallThenLeaf", bones, [small, leafKey], BonesRouteOrderMode.ExactOrder, false),
            ("BonesLeafThenSmall", bones, [leafKey, small], BonesRouteOrderMode.ExactOrder, false),
            ("BonesLeafKaleidoscopeAnyOrder", bones, [leafKey, kaleidoscope], BonesRouteOrderMode.AnyOrder, false),
            ("BonesUnbound", bones, [], BonesRouteOrderMode.AnyOrder, false)
        };
        await using var execution = new FilterRuntimeServices(requiresGpu: true, failOnPhysicalRecovery: true);
        if (OS.GetCmdlineUserArgs().Contains("--party-nrc-gpu"))
        {
            async Task CheckGpu(SearchQuery table, string familyId, string name, ulong start, bool exactPositive)
            {
                var q = Compile(table, VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, start));
                var gpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(q, true);
                var cpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(q, false);
                try
                {
                    var gpu = gpuFamilies.Single(f => f.FamilyId == familyId);
                    Check(gpu.ConditionPerformance.UsesGpu, name + " GPU registered");
                    var personal = q.Model.CompiledSearch.PlayerSearches.Take(2).Select(p => SearchPreparationCompiler.ForPlayer(q.Model, p)).ToArray();
                    var ns = personal.Select(p => NeowReplayPlan.Compile(p, false)).ToArray();
                    ns[1] = ns[1] with { SharedArrivals = NeowFamily.PartyInitial.SharedAdvanceOptions(personal[0], ns[0])! };
                    var batch = new SearchBatch(start, 1027);
                    foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                        Enumerable.Range(0, 514).Select(i => (ulong)i * 2).ToArray()) })
                    {
                        ulong[] expected;
                        if (familyId == "N.Neow") expected = input.EnumerateLogicalOrdinals().Where(i => ns.All(n =>
                            NeowFamilyReplay.Matches(Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, start + i)), n))).ToArray();
                        else expected = (await cpuFamilies.Single(f => f.FamilyId == familyId).ExecuteAsync(execution, input, default)).EnumerateLogicalOrdinals().ToArray();
                        var actual = await gpu.ExecuteAsync(execution, input, default);
                        Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), name + " complete CPU/GPU parity");
                        Check(actual.Count < input.Count, name + " effective sieve");
                        if (exactPositive) Check(actual.EnumerateLogicalOrdinals().Contains(0UL), name + " source positive retained");
                    }
                    if (exactPositive) Check(ProductionExactSearchEvaluator.Evaluate(q.Model, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, q.CanonicalStartSeed)).IsMatch, name + " whole-table Exact positive");
                    GD.Print($"PARTY_NRC_CASE name={name};family={familyId};positive={start};exactPositive={exactPositive}");
                }
                finally { foreach (var f in gpuFamilies.Concat(cpuFamilies)) await f.DisposeAsync(execution); }
            }
            var pairQuery = SearchQuery.Empty with { OpeningRoute = new(bones),
                OpeningRouteRelicRequirement = new(bones, [small, leafKey], BonesRouteOrderMode.AnyOrder) };
            await CheckGpu(Table(pairQuery), "N.Neow", "BonesPairOnly", 927, true);
            var witnessInput = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,
                VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, 927));
            var curse = Choices(witnessInput, pairQuery, false).Single(c => c.RelicKey == bones)
                .BonesOutcome!.OriginalRoutes[0].SharedContinuation.FinalCurseKey!.Value;
            await CheckGpu(Table(pairQuery with { LegacyNeow = pairQuery.LegacyNeow with { RequiredFinalCurse = curse } }),
                "N.Neow", "CapsuleBonesCurse", 927, true);
            var previousBones = SearchQuery.Empty with { OpeningRoute = new(bones) };
            var laterCurse = previousBones with { LegacyNeow = previousBones.LegacyNeow with { RequiredFinalCurse = curse } };
            await CheckGpu(SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = previousBones },
                new(1, ModelKeySetFilter.Empty) { Conditions = laterCurse }] }, "N.Neow", "RootDependentPreviousBonesArrivals", 0, false);
            foreach (bool partial in new[] { false, true })
            {
                var rQuery = SearchQuery.Empty with { OpeningRoute = new(bones), OpeningRouteRelicRequirement =
                    new(bones, partial ? [large] : [BaseGameModelKeys.Relics.ArcaneScroll, large], BonesRouteOrderMode.AnyOrder) };
                var search = Compile(Table(rQuery), "000000000000");
                var n = NeowReplayPlan.Compile(SearchPreparationCompiler.ForPlayer(search.Model, search.Model.CompiledSearch.PlayerSearches[1]), false);
                bool found = false;
                for (ulong ordinal = 0; ordinal < 65536; ordinal++)
                {
                    var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal));
                    if (!NeowFamilyReplay.Matches(input.RootHash, n)) continue;
                    var choice = Choices(input, rQuery, false).Single(c => c.RelicKey == bones);
                    var branch = choice.BonesOutcome?.OriginalRoutes.FirstOrDefault(r => r.RelicScopedResults.Any(s => s.SourceRelicKey == large));
                    var target = branch?.RelicScopedResults.Where(s => s.SourceRelicKey == large).SelectMany(s => s.EffectGroups)
                        .SelectMany(g => g.OrderedItems).FirstOrDefault(e => e.Kind == PredictedEffectKind.AddRelic && e.Relation == PredictedEffectRelation.NestedRelic)?.TargetKey;
                    if (target is null) continue;
                    var q = rQuery with { StructuredOpeningEffects = [Nested(large, target.Value)] };
                    await CheckGpu(Table(q), "R.Relic", partial ? "PartialBonesCapsule" : "ArcaneBeforeCapsule", ordinal, true);
                    found = true; break;
                }
                Check(found, "bounded Capsule source witness");
            }
            await CheckGpu(Table(SearchQuery.Empty with { LegacyCombatRewardConstraints = [new(3, ModelKeySetFilter.Empty,
                NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, 9, 12)] }), "C.CombatReward", "GoldRange", 0, false);
            foreach (var (unordered, specific) in new[] { (false, false), (false, true), (true, true) })
            {
                var slots = Enumerable.Range(0, 6).Select(_ => new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null)).ToArray();
                slots[unordered ? 0 : 5] = specific
                    ? new(CombatPotionSlotRequirement.DropSpecific, party.Players[1].EffectAuthority!.PotionPool!.First().PotionKey)
                    : new(CombatPotionSlotRequirement.NoDrop, null);
                var table = Table(SearchQuery.Empty with { CombatPotionRewards = new(6,
                    unordered ? CombatRewardSequenceOrderMode.Unordered : CombatRewardSequenceOrderMode.Ordered, slots) });
                var probe = Compile(table, "000000000000");
                var child = SearchPreparationCompiler.ForPlayer(probe.Model, probe.Model.CompiledSearch.PlayerSearches[1]);
                var potionReplay = new CombatRewardReplay(child);
                Check(potionReplay.Plan.MaximumBattleOrdinal == 6, "six-battle potion fast horizon");
                ulong? witness = null;
                for (ulong ordinal = 0; ordinal < 256; ordinal++)
                {
                    var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal));
                    if (ProductionExactSearchEvaluator.Evaluate(probe.Model, input).IsMatch) { witness = ordinal; break; }
                }
                Check(witness.HasValue, "six-battle potion Exact witness");
                await CheckGpu(table, "C.CombatReward", unordered ? "PotionSixUnordered" : specific ? "PotionSixOrderedSpecific" : "PotionSixOrderedNoDrop", witness!.Value, true);
            }
            GD.Print($"PARTY_NRC_GPU_PASS checks={checks};sourcePositives=true;denseCompact=true;noThroughputClaim=true");
            return;
        }
        int originalRouteOnlyUnknown = 0, strengthenedMatches = 0;
        var failures = new List<Exception>();
        var fixtureResults = new List<object>();
        async Task Independent(string name, Func<Task> run)
        {
            int failuresBefore = failures.Count;
            try
            {
                await run();
                fixtureResults.Add(new { Name = name, Status = failures.Count == failuresBefore ? "Passed" : "FailedChildCases" });
            }
            catch (Exception error)
            {
                failures.Add(error);
                fixtureResults.Add(new { Name = name, Status = "Failed", Exception = error.ToString(),
                    RemainingChecks = "Blocked within this case after the original exception; no pass inferred" });
                Console.Error.WriteLine("PARTY_COMBAT_OPENING_FAILURE name=" + name + System.Environment.NewLine + error);
            }
            try
            {
                CompatibilityChecks.WriteEvidence(Path.Combine(evidenceDirectory, "independent-fixture-results.json"), fixtureResults);
            }
            catch (Exception captureError)
            {
                failures.Add(new IOException("Party C fixture-result evidence write failed after " + name, captureError));
                fixtureResults.Add(new { Name = name, Status = "EvidenceCaptureFailed", Exception = captureError.ToString() });
                Console.Error.WriteLine("PARTY_COMBAT_OPENING_EVIDENCE_FAILURE name=" + name + System.Environment.NewLine + captureError);
            }
        }
        foreach (var fixture in fixtures)
        {
            await Independent("Fixture:" + fixture.Name, async () =>
            {
            var personal = SearchQuery.Empty with
            {
                OpeningRoute = fixture.Selected is { } selected ? new(selected) : null,
                OpeningRouteRelicRequirement = fixture.Pair.Length == 0 ? null : new(bones, fixture.Pair, fixture.Order)
            };
            var identityRequest = Compile(Table(personal, fixture.PriorNewLeaf), "000000000000");
            var identityPlans = identityRequest.Model.CompiledSearch.PlayerSearches.Take(2).Select(p =>
                NeowReplayPlan.Compile(SearchPreparationCompiler.ForPlayer(identityRequest.Model, p), false)).ToArray();
            var selection = fixture.Selected is { } selectedKey
                ? new NormalCombatRewardProjectionRequest(NormalCombatRewardProjectionScope.SearchExact,
                    NormalCombatRewardRouteSelectionMode.PinnedRealRoute, selectedKey,
                    fixture.Order == BonesRouteOrderMode.ExactOrder ? fixture.Pair : [])
                : new NormalCombatRewardProjectionRequest(NormalCombatRewardProjectionScope.SearchExact,
                    NormalCombatRewardRouteSelectionMode.ProvablyNeutralRealRoutes);
            ulong? positive = null;
            ModelKey target = default;
            string routeId = "";
            for (ulong ordinal = 0; ordinal < 65536; ordinal++)
            {
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
                if (!identityPlans.All(p => NeowFamilyReplay.Matches(input.RootHash, p))) continue;
                var prediction = Rewards(input, Choices(input, personal, fixture.PriorNewLeaf), selection);
                // Reward routes are explicitly conditional and publish Partial;
                // their audited card identities still admit Search predicates.
                var route = prediction.Routes.FirstOrDefault(r => r.Status == SeedDomainEvaluationStatus.Evaluated &&
                    r.Precision is not (PredictionPrecision.Unknown or PredictionPrecision.Unsupported) &&
                    r.Battles.Count > 0 && r.Battles[0].Cards.Count > 0 &&
                    r.Battles[0].Cards.All(c => c.Precision is not (PredictionPrecision.Unknown or PredictionPrecision.Unsupported)));
                if (route is null) continue;
                positive = ordinal;
                target = route.Battles[0].Cards[0].CardKey;
                routeId = route.RepresentativeContinuation.Route.RouteId;
                break;
            }
            Check(positive.HasValue, "bounded real offered witness: " + fixture.Name);
            string positiveSeed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, positive!.Value);
            var query = Table(personal with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [target]) }, fixture.PriorNewLeaf);
            var cases = new List<(string Name, SearchQuery Query, bool Strengthened)> {
                (fixture.Name + (fixture.PriorNewLeaf ? "-original-route-only" : ""), query, false) };
            if (fixture.PriorNewLeaf)
            {
                // This additional authored transform condition is a separate
                // stronger query, not a repair or rewrite of the original.
                ulong prefixRoot = Beta111Profile.Instance.ComputeRootSeed(positiveSeed);
                var prefixRng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, prefixRoot, 0);
                var prefix = new NeowEffectProjectionEngine(Beta111Profile.Instance, "party-c-prefix-source",
                    prefixRoot, party.Players[0]).Project(leafKey, prefixRng);
                var transform = prefix.EffectGroups.SelectMany(g => g.OrderedItems)
                    .Single(e => e.Kind == PredictedEffectKind.TransformCard);
                Check(prefixRng.Niche.CallCount == 1 && prefixRng.CombatPotionGeneration.CallCount == 0 &&
                    transform.TargetKey.HasValue && transform.Precision == PredictionPrecision.Exact,
                    fixture.Name + " independently projected NewLeaf transform and shared continuation");
                var prefixCondition = new NeowStructuredEffectSearchCondition(leafKey, NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card, [transform.TargetKey!.Value]);
                var stronger = query with { Players = query.Players.Select(p => p.Slot == 0 ? p with {
                    Conditions = p.Conditions with { StructuredOpeningEffects = [prefixCondition] } } : p).ToArray() };
                Check(stronger.Players.Skip(1).SequenceEqual(query.Players.Skip(1)), fixture.Name + " later-player obligations retained");
                cases.Add((fixture.Name + "-explicit-prefix-transform", stronger, true));
            }
            foreach (var testCase in cases)
            {
            await Independent(testCase.Name, async () =>
            {
            var request = Compile(testCase.Query, positiveSeed);
            var exact = ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, positiveSeed));
            ProductionSearchReplay.Write(Path.Combine(evidenceDirectory, testCase.Name + "-workload.json"),
                new ProductionSearchReplayWorkload(ProductionSearchReplay.SchemaVersion, request.Model.CompiledSearch.Query, request.RunOptions));
            ProductionSearchReplay.Write(Path.Combine(evidenceDirectory, testCase.Name + "-context.json"),
                new ProductionSearchReplayContext(ProductionSearchReplay.SchemaVersion,
                    testCase.Strengthened ? "Separate stronger query with explicit captured NewLeaf transform target" : "Original party C query; authored conditions retained",
                    request.Model.CompiledSearch.Context));
            CompatibilityChecks.WriteEvidence(Path.Combine(evidenceDirectory, testCase.Name + "-exact.json"), new {
                testCase.Name, positiveSeed, testCase.Strengthened, OriginalQueryMatchClaimed = !fixture.PriorNewLeaf && exact.IsMatch,
                exact.FailureCode, exact.Evaluation, exact.Facts });
            bool originalUnresolved = fixture.PriorNewLeaf && !testCase.Strengthened;
            if (originalUnresolved)
            {
                string detail = MpNExactFailureDetails(testCase.Name, request, exact, evidenceDirectory, positiveSeed);
                Check(exact.Evaluation.Disposition == SearchDisposition.Unknown && exact.Evaluation.Witnesses.Count == 0 &&
                    exact.Facts?.Neow?.Choices.Any(c =>
                        c.EffectEvidenceCode.Value == "party.earlier-player-shared-opening-unresolved") == true,
                    testCase.Name + " original shared-prefix Unknown retained: " + detail);
                originalRouteOnlyUnknown++;
            }
            else
            {
                Check(exact.IsMatch, testCase.Name + " whole-table Exact source positive: " + exact.FailureCode);
                if (testCase.Strengthened) strengthenedMatches++;
            }
            var cpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false);
            var gpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true, true);
            var integerFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true, false);
            var parity = new List<object>();
            try
            {
                var cpu = cpuFamilies.Single(f => f.FamilyId == "C.CombatReward");
                var gpu = gpuFamilies.Single(f => f.FamilyId == "C.CombatReward");
                var integer = integerFamilies.Single(f => f.FamilyId == "C.CombatReward");
                Check(!cpu.ConditionPerformance.UsesGpu && gpu.ConditionPerformance.UsesGpu && integer.ConditionPerformance.UsesGpu &&
                    (integer.GpuNumericRequirements & (GpuNumericRequirements.Float64 | GpuNumericRequirements.Unspecified)) == 0,
                    testCase.Name + " registered native and integer C GPU");
                var batch = new SearchBatch(positive.Value, 129);
                foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, 65).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var expected = await cpu.ExecuteAsync(execution, input, default);
                    var actual = await gpu.ExecuteAsync(execution, input, default);
                    var compatible = await integer.ExecuteAsync(execution, input, default);
                    var cpuOrdinals = expected.EnumerateLogicalOrdinals().ToArray();
                    var nativeOrdinals = actual.EnumerateLogicalOrdinals().ToArray();
                    var integerOrdinals = compatible.EnumerateLogicalOrdinals().ToArray();
                    parity.Add(new { input.IsDense, InputCount = input.Count, Cpu = cpuOrdinals, Native = nativeOrdinals, Integer = integerOrdinals });
                    CompatibilityChecks.WriteEvidence(Path.Combine(evidenceDirectory, testCase.Name + "-parity.json"), parity);
                    Check(nativeOrdinals.SequenceEqual(cpuOrdinals) && integerOrdinals.SequenceEqual(cpuOrdinals),
                        testCase.Name + " complete C CPU/native/integer population parity");
                    Check(nativeOrdinals.Contains(0UL), testCase.Name + " retains independently projected source positive");
                    Check(actual.Count < input.Count, testCase.Name + " actually filters C");
                }
                GD.Print($"PARTY_COMBAT_OPENING name={testCase.Name};slot=1;positive={positive};route={routeId};denseRoots=129;compactRoots=65;cpuNativeIntegerParity=true;wholeExact={exact.Evaluation.Disposition};strengthenedQuery={testCase.Strengthened};originalQueryMatchClaimed={!fixture.PriorNewLeaf}");
            }
            finally
            {
                foreach (var family in gpuFamilies.Concat(integerFamilies).Concat(cpuFamilies)) await family.DisposeAsync(execution);
            }
            });
            }
            });
        }
        await Independent("RouteOnlyUnknownAndStrengthenedCoverage", () =>
        {
            Check(originalRouteOnlyUnknown == fixtures.Count(f => f.PriorNewLeaf) && strengthenedMatches == originalRouteOnlyUnknown,
                "each original route-only Unknown has a separate explicitly authored whole-table Match");
            return Task.CompletedTask;
        });

        // The probe locates one real Prayer Wheel pull. Its target is removed
        // from the tested query: unspecified obtains still retain held effects.
        await Independent("UnlistedCapsuleHeldPrayerWheel", async () =>
        {
            var prayerWheel = new ModelKey(BaseGameModelKeys.Categories.Relic, "PRAYER_WHEEL");
            var probePersonal = SearchQuery.Empty with { OpeningRoute = new(large),
                StructuredOpeningEffects = [Nested(large, prayerWheel)] };
            var probeParent = Compile(Table(probePersonal), "000000000000");
            var probe = SearchPreparationCompiler.ForPlayer(probeParent.Model, probeParent.Model.CompiledSearch.PlayerSearches[1]);
            var n = NeowReplayPlan.Compile(probe, false);
            var r = new CapsuleRelicReplay(probe, NeowReplayPlan.Compile(probe, true));
            var unlisted = SearchQuery.Empty with { OpeningRoute = new(large) };
            ulong? positive = null;
            ModelKey target = default;
            for (ulong ordinal = 0; ordinal < 8192; ordinal++)
            {
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
                if (!NeowFamilyReplay.Matches(input.RootHash, n) || !r.Matches(input.RootHash)) continue;
                var source = Rewards(input, Choices(input, unlisted, false), new(NormalCombatRewardProjectionScope.SearchExact,
                    NormalCombatRewardRouteSelectionMode.PinnedRealRoute, large));
                var route = source.Routes.FirstOrDefault(route => route.Status == SeedDomainEvaluationStatus.Evaluated &&
                    route.Battles.Count > 0 && route.Battles[0].CardRewards.Count > 1 &&
                    route.Battles[0].AppliedRewardImpactSources.Any(s => s.ModelKey == prayerWheel));
                if (route is null) continue;
                var primary = route.Battles[0].Cards.Select(c => c.CardKey).ToHashSet();
                var extra = route.Battles[0].CardRewards.Skip(1).SelectMany(reward => reward.Cards)
                    .FirstOrDefault(card => !primary.Contains(card.CardKey));
                if (extra is null) continue;
                positive = ordinal; target = extra.CardKey; break;
            }
            Check(positive.HasValue, "bounded unlisted held PrayerWheel source witness");
            string seedText = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, positive!.Value);
            var request = Compile(Table(unlisted with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [target]) }), seedText);
            Check(request.Model.CompiledSearch.PlayerSearches[1].NormalizedQuery.StructuredOpeningEffects.Count == 0,
                "PrayerWheel is absent from the authored query");
            Check(ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seedText)).IsMatch,
                "Exact keeps the unlisted PrayerWheel extra-reward target");
            GD.Print($"PARTY_COMBAT_UNLISTED_HELD_WITNESS positive={positive};target={target};authoredNestedTargets=0");
            var cpu = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false);
            var gpu = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true);
            try
            {
                var cCpu = cpu.Single(f => f.FamilyId == "C.CombatReward");
                var cGpu = gpu.Single(f => f.FamilyId == "C.CombatReward");
                Check(cGpu.ConditionPerformance.UsesGpu, "unlisted Capsule held effects retain C GPU admission");
                var batch = new SearchBatch(positive.Value, 33);
                foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, 17).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var expected = await cCpu.ExecuteAsync(execution, input, default);
                    var actual = await cGpu.ExecuteAsync(execution, input, default);
                    Check(expected.EnumerateLogicalOrdinals().SequenceEqual(actual.EnumerateLogicalOrdinals()),
                        "unlisted PrayerWheel complete C CPU/GPU population parity");
                    Check(actual.EnumerateLogicalOrdinals().Contains(0UL), "GPU retains a card unique to the extra PrayerWheel reward");
                }
            }
            finally { foreach (var family in gpu.Concat(cpu)) await family.DisposeAsync(execution); }
            GD.Print($"PARTY_COMBAT_UNLISTED_HELD_PASS positive={positive};held={prayerWheel};extraRewardTarget={target};authoredNestedTargets=0;denseCompact=true");
        });

        // A card that only occurs on an actually offered perturbing route cannot
        // acquire an implicit N commitment from an otherwise unselected C query.
        await Independent("NoNPerturbingRouteRejection", () =>
        {
        bool neutralRejection = false;
        for (ulong ordinal = 0; ordinal < 64 && !neutralRejection; ordinal++)
        {
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
            var choices = Choices(input, SearchQuery.Empty, false);
            var all = Rewards(input, choices, NormalCombatRewardProjectionRequest.RichAnalysis);
            var neutral = Rewards(input, choices, new(NormalCombatRewardProjectionScope.SearchExact,
                NormalCombatRewardRouteSelectionMode.ProvablyNeutralRealRoutes));
            var neutralCards = neutral.Routes.Where(r => r.Battles.Count > 0).SelectMany(r => r.Battles[0].Cards)
                .Select(c => c.CardKey).ToHashSet();
            foreach (var route in all.Routes.Where(r => r.Status == SeedDomainEvaluationStatus.Evaluated && r.Battles.Count > 0 &&
                OpeningRewardNeutralityClassifier.Classify(r.RepresentativeContinuation) == OpeningRewardNeutrality.Perturbing))
            {
                var card = route.Battles[0].Cards.FirstOrDefault(c => !neutralCards.Contains(c.CardKey));
                if (card is null) continue;
                var combat = SearchQuery.Empty with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [card.CardKey]) };
                var pinned = combat with { OpeningRoute = new(route.RepresentativeContinuation.Route.RootRelicKey) };
                var order = route.RepresentativeContinuation.Route.AcquisitionOrder;
                if (pinned.OpeningRoute.RouteRelicKey == bones)
                    pinned = pinned with { OpeningRouteRelicRequirement = new(bones, order, BonesRouteOrderMode.ExactOrder) };
                var selectedRequest = Compile(Table(pinned), seed);
                var selected = ProductionExactSearchEvaluator.Evaluate(selectedRequest.Model, input);
                if (!selected.IsMatch) continue;
                var unselectedRequest = Compile(Table(combat), seed);
                var unselected = ProductionExactSearchEvaluator.Evaluate(unselectedRequest.Model, input);
                foreach (var item in new[] { (Name: "selected", Request: selectedRequest), (Name: "unselected", Request: unselectedRequest) })
                {
                    ProductionSearchReplay.Write(Path.Combine(evidenceDirectory, "NoNPerturbingRouteRejection-" + item.Name + "-workload.json"),
                        new ProductionSearchReplayWorkload(ProductionSearchReplay.SchemaVersion, item.Request.Model.CompiledSearch.Query, item.Request.RunOptions));
                    ProductionSearchReplay.Write(Path.Combine(evidenceDirectory, "NoNPerturbingRouteRejection-" + item.Name + "-context.json"),
                        new ProductionSearchReplayContext(ProductionSearchReplay.SchemaVersion,
                            "Original no-N comparison; " + item.Name + " opening intent and captured authority retained", item.Request.Model.CompiledSearch.Context));
                }
                CompatibilityChecks.WriteEvidence(Path.Combine(evidenceDirectory, "NoNPerturbingRouteRejection-exact.json"), new {
                    seed, ordinal, Target = card.CardKey, PerturbingRoute = route.RepresentativeContinuation.Route, selected, unselected });
                // An empty proven-neutral policy subset cannot disprove every
                // actual opening. Exact preserves that specific uncertainty;
                // it must never promote the selected perturbing route to Match.
                bool unverifiedNeutralSubset = unselected.Evaluation.Disposition == SearchDisposition.Unknown &&
                    unselected.FailureCode == "NoEligibleUnpinnedRewardRoute" &&
                    unselected.Evaluation.FailureCode == unselected.FailureCode &&
                    unselected.Request?.Authority is { PlayerSlotIndex: 1 } childAuthority &&
                    unselected.Evaluation.Evidence.Count == 1 && unselected.Evaluation.Evidence.All(e =>
                        e.Code == "Party.ChildEvaluationScope" && e.EvidenceCode.Value == "Party.NonMatch.SourceContext" &&
                        childAuthority.PlayerSlotIndex == 1 &&
                        e.RelatedKey == childAuthority.Character.CharacterKey && e.Ordinal == 2 &&
                        e.RouteId is null && e.AcquisitionOrder is null && e.ChoicePolicyId is null &&
                        e.OutcomeId is null && e.ConditionId is null && e.OrderedOptionKeys is null &&
                        e.ProfileId == childAuthority.ProfileId && e.Authority == childAuthority.SourceAuthority &&
                        e.AuthorityFingerprint == string.Join("|", childAuthority.CatalogFingerprint,
                            childAuthority.UnlockSnapshotFingerprint, childAuthority.EffectSnapshotFingerprint,
                            childAuthority.WorldSnapshotFingerprint)) &&
                    unselected.Evaluation.MatchedRouteIds.Count == 0 &&
                    unselected.Evaluation.Witnesses.Count == 0;
                Check(!unselected.IsMatch && (unselected.Evaluation.Disposition == SearchDisposition.NoMatch || unverifiedNeutralSubset),
                    "no-N does not silently select perturbing route " + pinned.OpeningRoute.RouteRelicKey +
                    ";actual=" + unselected.Evaluation.Disposition + ";failure=" + unselected.FailureCode +
                    ";evaluationFailure=" + unselected.Evaluation.FailureCode + ";evidence/routes/witness=" +
                    unselected.Evaluation.Evidence.Count + "/" + unselected.Evaluation.MatchedRouteIds.Count + "/" + unselected.Evaluation.Witnesses.Count);
                neutralRejection = true;
                GD.Print($"PARTY_COMBAT_NO_N_{(unverifiedNeutralSubset ? "UNCONFIRMED" : "REJECTION")} positive={ordinal};perturbing={pinned.OpeningRoute.RouteRelicKey};target={card.CardKey};selectedMatch=true;unselectedMatch=false;unselectedDisposition={unselected.Evaluation.Disposition};failure={unselected.FailureCode}");
                break;
            }
        }
        Check(neutralRejection, "bounded no-N perturbing-route non-confirmation evidence");
        return Task.CompletedTask;
        });
        if (failures.Count != 0)
            throw new AggregateException("Party C openings failed; independent fixtures continued; see party-c-openings/independent-fixture-results.json", failures);
        GD.Print($"PARTY_COMBAT_OPENINGS_PASS cases={fixtures.Length};checks={checks};originalRouteOnlyUnknown={originalRouteOnlyUnknown};separateStrengthenedMatches={strengthenedMatches};originalLimitationFixed=false;noThroughputClaim=true");
    }
}
