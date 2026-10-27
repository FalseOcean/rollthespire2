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
        SearchQuery Table(SearchQuery personal, bool priorNewLeaf = false) => SearchQuery.Empty with
        {
            Players = [new(0, ModelKeySetFilter.Empty) { Conditions = priorNewLeaf
                ? SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.NewLeaf) } : SearchQuery.Empty },
                new(1, ModelKeySetFilter.Empty) { Conditions = personal }]
        };
        ExactSearchExecutionRequest Compile(SearchQuery query, string seed)
        {
            var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(seed, 129, 1, 1));
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
            var child = ExactSearchExecutionRequestFactory.ForPlayer(parent, parent.CompiledSearch.PlayerSearches[1]);
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
        await using var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
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
                    var personal = q.CompiledSearch.PlayerSearches.Take(2).Select(p => ExactSearchExecutionRequestFactory.ForPlayer(q, p)).ToArray();
                    var ns = personal.Select(p => NeowReplayPlan.Compile(p, false)).ToArray();
                    ns[1] = ns[1] with { SharedArrivals = NeowFamily.PartyInitial.SharedAdvanceOptions(personal[0], ns[0])! };
                    var batch = new SearchBatch(start, 1027);
                    foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                        Enumerable.Range(0, 514).Select(i => (ulong)i * 2).ToArray()) })
                    {
                        ulong[] expected;
                        if (familyId == "N.Neow") expected = input.EnumerateLogicalOrdinals().Where(i => ns.All(n =>
                            NeowFamilyReplay.Matches(Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, start + i)), n))).ToArray();
                        else expected = (await cpuFamilies.Single(f => f.FamilyId == familyId).InvokeAsync(execution, new(batch, q), input, default)).EnumerateLogicalOrdinals().ToArray();
                        var actual = await gpu.InvokeAsync(execution, new(batch, q), input, default);
                        Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), name + " complete CPU/GPU parity");
                        Check(actual.Count < input.Count, name + " effective sieve");
                        if (exactPositive) Check(actual.EnumerateLogicalOrdinals().Contains(0UL), name + " source positive retained");
                    }
                    if (exactPositive) Check(ProductionExactSearchEvaluator.Evaluate(q,
                        TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, q.CanonicalStartSeed)).IsMatch, name + " whole-table Exact positive");
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
                var n = NeowReplayPlan.Compile(ExactSearchExecutionRequestFactory.ForPlayer(search, search.CompiledSearch.PlayerSearches[1]), false);
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
                var child = ExactSearchExecutionRequestFactory.ForPlayer(probe, probe.CompiledSearch.PlayerSearches[1]);
                var potionReplay = new CombatRewardReplay(child);
                Check(potionReplay.Plan.MaximumBattleOrdinal == 6, "six-battle potion fast horizon");
                ulong? witness = null;
                for (ulong ordinal = 0; ordinal < 256; ordinal++)
                {
                    var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal));
                    if (ProductionExactSearchEvaluator.Evaluate(probe, input).IsMatch) { witness = ordinal; break; }
                }
                Check(witness.HasValue, "six-battle potion Exact witness");
                await CheckGpu(table, "C.CombatReward", unordered ? "PotionSixUnordered" : specific ? "PotionSixOrderedSpecific" : "PotionSixOrderedNoDrop", witness!.Value, true);
            }
            GD.Print($"PARTY_NRC_GPU_PASS checks={checks};sourcePositives=true;denseCompact=true;noThroughputClaim=true");
            return;
        }
        foreach (var fixture in fixtures)
        {
            var personal = SearchQuery.Empty with
            {
                OpeningRoute = fixture.Selected is { } selected ? new(selected) : null,
                OpeningRouteRelicRequirement = fixture.Pair.Length == 0 ? null : new(bones, fixture.Pair, fixture.Order)
            };
            var identityRequest = Compile(Table(personal, fixture.PriorNewLeaf), "000000000000");
            var identityPlans = identityRequest.CompiledSearch.PlayerSearches.Take(2).Select(p =>
                NeowReplayPlan.Compile(ExactSearchExecutionRequestFactory.ForPlayer(identityRequest, p), false)).ToArray();
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
            var request = Compile(query, positiveSeed);
            var exact = ProductionExactSearchEvaluator.Evaluate(request,
                TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, positiveSeed));
            Check(exact.IsMatch, fixture.Name + " whole-table Exact source positive: " + exact.FailureCode);
            var cpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false);
            var gpuFamilies = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true);
            try
            {
                var cpu = cpuFamilies.Single(f => f.FamilyId == "C.CombatReward");
                var gpu = gpuFamilies.Single(f => f.FamilyId == "C.CombatReward");
                Check(!cpu.ConditionPerformance.UsesGpu && gpu.ConditionPerformance.UsesGpu, fixture.Name + " registered C GPU");
                var batch = new SearchBatch(positive.Value, 129);
                foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, 65).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var window = new FamilyObservationWindow(batch, request);
                    var expected = await cpu.InvokeAsync(execution, window, input, default);
                    var actual = await gpu.InvokeAsync(execution, window, input, default);
                    Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected.EnumerateLogicalOrdinals()),
                        fixture.Name + " complete C CPU/GPU population parity");
                    Check(actual.EnumerateLogicalOrdinals().Contains(0UL), fixture.Name + " retains source positive");
                    Check(actual.Count < input.Count, fixture.Name + " actually filters C");
                }
                GD.Print($"PARTY_COMBAT_OPENING name={fixture.Name};slot=1;positive={positive};route={routeId};denseRoots=129;compactRoots=65;exactPositive=true");
            }
            finally
            {
                foreach (var family in gpuFamilies.Concat(cpuFamilies)) await family.DisposeAsync(execution);
            }
        }

        // The probe locates one real Prayer Wheel pull. Its target is removed
        // from the tested query: unspecified obtains still retain held effects.
        {
            var prayerWheel = new ModelKey(BaseGameModelKeys.Categories.Relic, "PRAYER_WHEEL");
            var probePersonal = SearchQuery.Empty with { OpeningRoute = new(large),
                StructuredOpeningEffects = [Nested(large, prayerWheel)] };
            var probeParent = Compile(Table(probePersonal), "000000000000");
            var probe = ExactSearchExecutionRequestFactory.ForPlayer(probeParent, probeParent.CompiledSearch.PlayerSearches[1]);
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
            Check(request.CompiledSearch.PlayerSearches[1].NormalizedQuery.StructuredOpeningEffects.Count == 0,
                "PrayerWheel is absent from the authored query");
            Check(ProductionExactSearchEvaluator.Evaluate(request,
                TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seedText)).IsMatch,
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
                    var expected = await cCpu.InvokeAsync(execution, new(batch, request), input, default);
                    var actual = await cGpu.InvokeAsync(execution, new(batch, request), input, default);
                    Check(expected.EnumerateLogicalOrdinals().SequenceEqual(actual.EnumerateLogicalOrdinals()),
                        "unlisted PrayerWheel complete C CPU/GPU population parity");
                    Check(actual.EnumerateLogicalOrdinals().Contains(0UL), "GPU retains a card unique to the extra PrayerWheel reward");
                }
            }
            finally { foreach (var family in gpu.Concat(cpu)) await family.DisposeAsync(execution); }
            GD.Print($"PARTY_COMBAT_UNLISTED_HELD_PASS positive={positive};held={prayerWheel};extraRewardTarget={target};authoredNestedTargets=0;denseCompact=true");
        }

        // A card that only occurs on an actually offered perturbing route cannot
        // acquire an implicit N commitment from an otherwise unselected C query.
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
                var selected = ProductionExactSearchEvaluator.Evaluate(Compile(Table(pinned), seed), input);
                if (!selected.IsMatch) continue;
                var unselected = ProductionExactSearchEvaluator.Evaluate(Compile(Table(combat), seed), input);
                Check(!unselected.IsMatch && unselected.Evaluation.Disposition == SearchDisposition.NoMatch,
                    "no-N does not silently select perturbing route " + pinned.OpeningRoute.RouteRelicKey);
                neutralRejection = true;
                GD.Print($"PARTY_COMBAT_NO_N_REJECTION positive={ordinal};perturbing={pinned.OpeningRoute.RouteRelicKey};target={card.CardKey};selectedMatch=true;unselectedMatch=false");
                break;
            }
        }
        Check(neutralRejection, "bounded no-N perturbing-route rejection evidence");
        GD.Print($"PARTY_COMBAT_OPENINGS_PASS cases={fixtures.Length};checks={checks};noThroughputClaim=true");
    }
}
