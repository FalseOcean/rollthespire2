using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class CombinationRegressionChecks
{
    // Run from repository root: --combination-regressions. Fixed seed fixtures;
    // no GPU, discovery scan, native host, timing loop or live RunState.
    private static int _checks;
    private static readonly ModelKey Bones = BaseGameModelKeys.Relics.NeowsBones;
    private static readonly ModelKey Leaf = BaseGameModelKeys.Relics.NewLeaf;
    private static readonly ModelKey Large = BaseGameModelKeys.Relics.LargeCapsule;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Combination regression: " + message);
        _checks++;
    }

    internal static int Run()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            Path.Combine(Environment.CurrentDirectory, "tools/ProductionSearchHost/Corpus/v1/context.json")).Context;
        CheckCombatAssignments(context);
        CheckPartyGaps(context);
        CheckCapsuleJointWitness(context);
        CheckPartyWorkerBudgets(context);
        CheckPartyModIdentity(context);
        CheckPartyRequestedObservationScope(context);
        CheckCapsuleObservationScope(context);
        CheckSoloOpeningAlternatives(context);
        Console.WriteLine($"COMBINATION_REGRESSIONS_PASS checks={_checks};gpu=false;seedScan=false");
        return 0;
    }

    private static ExactSearchExecutionRequest Compile(SearchContext context, SearchQuery query, string seed, int workers = 1)
    {
        var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(seed, 1, 1, workers));
        Check(result.Success, "compile " + result.Issue);
        return result.Plan!;
    }

    private static ProductionExactSearchResult Exact(SearchContext context, SearchQuery query, string seed) =>
        ProductionExactSearchEvaluator.Evaluate(Compile(context, query, seed), Input(seed));

    private static TrustedRootHashInput Input(string seed) => TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);

    private static void CheckPartyRequestedObservationScope(SearchContext captured)
    {
        const string seed = "0000000001FA";
        var context = PartyContext(captured, 4);
        var party = context.Party!;
        var full = PartySeedInformation.Project(seed, party);
        Check(full.ActObservationRequested && full.BossObservationRequested &&
            full.Players.All(p => p.ObservationRequested), "Public party information still requests the complete observation");
        full.RequireSameObservation(PartySeedInformation.Project(seed, party, true, true,
            Enumerable.Range(0, 4).ToHashSet()));
        Check(true, "Explicit full party projection preserves the public observation");

        var variantQuery = Table(4) with { VariantBossBranches =
            [new(1, full.Acts[0], ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] };
        var variant = Exact(context, variantQuery, seed);
        Check(variant.IsMatch && variant.Document!.Party is { ActObservationRequested: true,
            BossObservationRequested: false, Bosses.Count: 0 } actsOnly &&
            actsOnly.Acts.SequenceEqual(full.Acts) && actsOnly.Players.All(p => !p.ObservationRequested),
            "Variant-only Exact observes Acts without manufacturing Boss or N observations");

        // An intentionally faulting copied input proves that the unrequested
        // producer is not invoked; this is not missing-authority/Unknown data.
        var failure = new InvalidOperationException("PartyUnrequestedWorldProducerFault");
        var world = party.World with { Beta109Generation = party.World.Beta109Generation! with
        {
            AncientEventContexts = new FaultingObservationList<Beta109AncientEventContextSnapshot>(failure)
        } };
        var faultParty = new OrderedPartyAuthority(party.Players, party.UnlockProvenance, world, party.UnlockSources);
        var faultContext = context with { Party = faultParty };
        try
        {
            PartySeedInformation.Project(seed, faultParty);
            throw new Exception("Full party projection must reach its requested World producer");
        }
        catch (InvalidOperationException ex) when (ReferenceEquals(ex, failure))
        {
            Check(true, "Full party projection preserves the original requested-producer exception");
        }

        var target = full.Players[0].Offers[0];
        var offerQuery = Table(4) with { Players = Table(4).Players.Select(p => p.Slot == 0
            ? p with { Offers = new([target], [], []) } : p).ToArray() };
        var offers = Exact(faultContext, offerQuery, seed);
        Check(offers.IsMatch && offers.Document!.Party is { ActObservationRequested: false,
            BossObservationRequested: false } initial && initial.Players[0].ObservationRequested &&
            initial.Players[0].Offers.SequenceEqual(full.Players[0].Offers) &&
            initial.Players.Skip(1).All(p => !p.ObservationRequested && p.Offers.Count == 0 &&
                p.IdentityPrecision != PredictionPrecision.Exact && p.IssueCode == "Party.NeowObservationNotRequested"),
            "Offer-only Exact skips the faulting World producer and marks unused seats as unrequested");
        Check(offers.Document!.Party is { Acts.Count: 0, Bosses.Count: 0,
            ActIdentityPrecision: PredictionPrecision.DescriptionOnly,
            BossIssueCode: "Party.BossObservationNotRequested" },
            "Unrequested World envelope carries no Exact observation claim");

        var personalIdentity = SearchQuery.Empty with { LegacyNeow = LegacyNeowSemanticConstraints.Empty with
            { NeowRelics = new([target], [], []) } };
        var familyOffers = Exact(faultContext, Table(4, (0, personalIdentity)), seed);
        Check(familyOffers.IsMatch && familyOffers.Document!.Party is { IncludesFamilyInformation: true,
            ActObservationRequested: false, BossObservationRequested: false } family &&
            family.Players.Where(p => p.ObservationRequested).Select(p => p.Slot).SequenceEqual(new[] { 0 }),
            "Family Exact keeps the requested N fact with empty shared/other-seat queries and no World production");

        var eventProjection = Beta111EventResultProjector.Project(Input(seed).RootHash, 2,
            Beta111EventResultAuthority.From(party.Players[2]));
        var eventQuery = SearchQuery.Empty with { EventResultConditions =
            [new(EventResultConditionKind.TrashHeapGrabCard, eventProjection.TrashHeapGrabCard)] };
        var canonical = Exact(faultContext, Table(4, (2, eventQuery)), seed);
        Check(canonical.IsMatch && canonical.Document!.Party is { ActObservationRequested: false,
            BossObservationRequested: false } canonicalParty && canonicalParty.Players.All(p =>
                !p.ObservationRequested && p.IdentityPrecision != PredictionPrecision.Exact),
            "Canonical-only party Exact retains its personal Event obligation without World or N observations");

        var boss = full.Bosses.First(b => b.Act == 1 && b.Ordinal == 1);
        var bossQuery = Table(4) with { LegacyWorld = LegacyWorldSemanticConstraints.Empty with
            { BossFilters = [new(1, new([boss.BossKey], [], []))] } };
        try
        {
            Exact(faultContext, bossQuery, seed);
            throw new Exception("Requested party World fault must not become a semantic result");
        }
        catch (InvalidOperationException ex) when (ReferenceEquals(ex, failure))
        {
            Check(true, "Requested party World exception escapes Exact unchanged for Session pending ownership");
        }
        Console.WriteLine("COMBINATION_PARTY_OBSERVATION_SCOPE requested slots; Acts-only; canonical-only; original producer fault retained");
    }

    private sealed class FaultingObservationList<T>(Exception failure) : IReadOnlyList<T>
    {
        public int Count => throw failure;
        public T this[int index] => throw failure;
        public IEnumerator<T> GetEnumerator() => throw failure;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void CheckCapsuleObservationScope(SearchContext context)
    {
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(Environment.CurrentDirectory,
            "tools/ProductionSearchHost/Corpus/classic-product-v1/B-SourceCapsules-positive.json"));
        string seed = workload.Options.StartSeed;
        var individual = workload.Query;
        var grouped = individual with { StructuredOpeningEffects = [new(Bones,
            NeowStructuredConditionKind.ExactGroupedCapsuleMultiset, NeowStructuredEffectScope.NestedRelics,
            NeowStructuredOutputKind.Relic, individual.StructuredOpeningEffects.SelectMany(c => c.OutputKeys).ToArray(),
            AllowDuplicateOutputs: false)] };
        Check(Exact(context, grouped, seed).IsMatch, "Full-authority joint Capsule positive");
        // Isolate unavailable starter identities; retain the actual ordered relic pools,
        // deck, seed and all three nested identities. This is a controlled DTO, not a Mod capture.
        var partial = context with { Authority = context.Authority.WithEffectAuthority(context.Authority.EffectAuthority! with
            { CharacterStrikeKey = null, CharacterDefendKey = null }) };
        var separate = Exact(partial, individual, seed);
        var joint = Exact(partial, grouped, seed);
        Console.WriteLine($"CAPSULE_SCOPE seed={seed};sourceSeparated={separate.Evaluation.Disposition};grouped={joint.Evaluation.Disposition};reason={joint.Evaluation.FailureCode};routes={string.Join(',', joint.Evaluation.RouteRejections.Select(r => r.Reason))}");
        Check(separate.IsMatch, "Missing fixed starter identities do not erase known Capsule relics");
        Check(joint.IsMatch, "Grouped identity-only query agrees with separate-source identity truth on the same authority and seed");
        Check(joint.Evaluation.Witnesses.Count > 0, "Grouped match retains a real same-route witness");
        var groupedPlan = Compile(partial, grouped, seed);
        foreach (string damaged in new[] { "unknown-item", "missing-item", "unknown-source", "not-evaluated" })
        {
            BonesRelicScopedResult Damage(BonesRelicScopedResult source)
            {
                if (source.SourceRelicKey != Large) return source;
                return damaged switch
                {
                    "unknown-source" => source with { ProductRelevantProjectionPrecision = PredictionPrecision.Unknown },
                    "not-evaluated" => source with { ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.NotEvaluatedByPolicy },
                    _ => source with { EffectGroups = source.EffectGroups.Select(g => g with {
                        OrderedItems = damaged == "missing-item"
                            ? g.OrderedItems.Where(i => i.Kind != PredictedEffectKind.AddRelic).ToArray()
                            : g.OrderedItems.Select(i => i.Kind == PredictedEffectKind.AddRelic
                                ? i with { Precision = PredictionPrecision.Unknown } : i).ToArray() }).ToArray() }
                };
            }
            var original = joint.Document!;
            var document = new SeedPredictionDocument {
                Context = original.Context, PredictorId = original.PredictorId, OriginalSeed = original.OriginalSeed,
                CanonicalSeed = original.CanonicalSeed, Warnings = original.Warnings, Diagnostics = original.Diagnostics,
                OverallStatus = original.OverallStatus, ProductRelevantProjectionPrecision = original.ProductRelevantProjectionPrecision,
                ProductRelevantProjectionStatus = original.ProductRelevantProjectionStatus, FullEffectSemanticsCompleteness = original.FullEffectSemanticsCompleteness,
                Sections = original.Sections.Select(s => s with {
                NeowChoices = s.NeowChoices.Select(c => c.BonesOutcome is not { } bones ? c : c with {
                    BonesOutcome = bones with { OriginalRoutes = bones.OriginalRoutes.Select(r => r with {
                        RelicScopedResults = r.RelicScopedResults.Select(Damage).ToArray() }).ToArray() } }).ToArray()
            }).ToArray() };
            var rejected = ProductionQueryValidator.Evaluate(groupedPlan, document, joint.Authority);
            Check(rejected.Disposition is SearchDisposition.Unknown or SearchDisposition.NotEvaluatedByPolicy,
                "Grouped Capsules still refuse incomplete required observations: " + damaged);
        }
        ModelKey absent = partial.Authority.EffectAuthority!.OrderedRelicBag!.Select(r => r.RelicKey)
            .First(k => !grouped.StructuredOpeningEffects[0].OutputKeys.Contains(k));
        var wrongTarget = grouped with { StructuredOpeningEffects = [grouped.StructuredOpeningEffects[0] with { OutputKeys = [absent] }] };
        Check(!Exact(partial, wrongTarget, seed).IsMatch, "Known different nested identities cannot pass a grouped query");
    }

    private static void CheckSoloOpeningAlternatives(SearchContext context)
    {
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(Environment.CurrentDirectory,
            "tools/ProductionSearchHost/Corpus/classic-product-v1/A-positive-tail.json"));
        var query = SearchQuery.Empty with { OpeningRoute = workload.Query.OpeningRoute,
            OpeningRouteRelicRequirement = workload.Query.OpeningRouteRelicRequirement,
            StructuredOpeningEffects = workload.Query.StructuredOpeningEffects };
        string seed = workload.Options.StartSeed;
        Check(Exact(context, query, seed).IsMatch, "Classic solo N fixture is a real Exact positive");
        var pairs = query.OpeningRouteRelicRequirement!.RequiredRelicKeys;
        foreach (var order in new[] { query.OpeningRouteRelicRequirement,
            query.OpeningRouteRelicRequirement with { OrderMode = BonesRouteOrderMode.ExactOrder },
            query.OpeningRouteRelicRequirement with { OrderMode = BonesRouteOrderMode.ExactOrder, RequiredRelicKeys = pairs.Reverse().ToArray() } })
        {
            var plan = Compile(context, query with { OpeningRouteRelicRequirement = order }, seed);
            var n = NeowReplayPlan.Compile(plan, false);
            Check(n.HasFinalCurseFastProjection, "Alternative check actually exercises route-local N predicates");
            Check(NeowSingleplayerReplay.Matches(Input(seed).RootHash, n) == ProductionExactSearchEvaluator.Evaluate(plan, Input(seed)).IsMatch,
                "Solo OR and both pinned acquisition orders agree with independent Production Exact");
        }
    }

    private static void Expect(SearchContext context, SearchQuery query, string seed, bool match, string name)
    {
        var result = Exact(context, query, seed);
        Check(result.Evaluation.Disposition == (match ? SearchDisposition.Match : SearchDisposition.NoMatch),
            $"{name}: {result.Evaluation.Disposition} {result.FailureCode}");
        Check(result.IsMatch == match, name + " complete Exact result");
        if (result.Evaluation.FailureCode is "NoNormalCombatRewardRouteMatched" or "NoReliableNormalCombatRewardRouteMatched")
            Check(result.Evaluation.RouteRejections.Count > 0 && result.Evaluation.RouteRejections.All(r => r.Reason.Length > 0),
                name + " retains the reward route rejection detail");
    }

    private static void CheckCombatAssignments(SearchContext context)
    {
        const string seed = "000000000000";
        // Request three battles without constraining the opening. Expected facts
        // come from Core's real neutral continuation, not the Fast C donor.
        var probe = Exact(context, SearchQuery.Empty with { LegacyCombatRewardConstraints = [
            new(3, ModelKeySetFilter.Empty, NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, 0, null)] }, seed);
        Check(probe.IsMatch, "neutral reward fixture is an Exact positive");
        var routes = probe.Document!.Sections.Single(s => s.NormalCombatRewardSequencePrediction is not null)
            .NormalCombatRewardSequencePrediction!.Routes;
        Check(routes.Count > 0 && routes.All(r => r.Battles.Count >= 3), "three neutral battles are available");
        var battles = routes[0].Battles.Take(3).ToArray();
        string Signature(NormalCombatRewardRoutePredictionResult r) => string.Join(";", r.Battles.Take(3)
            .Select(b => string.Join(",", b.Cards.Select(c => c.CardKey.Serialized)) + "/" + b.Potion.PotionKey));
        Check(routes.All(r => Signature(r) == Signature(routes[0])), "all neutral witnesses share the tested reward facts");
        var cards = battles.Select(b => b.Cards.Select(c => c.CardKey).ToHashSet()).ToArray();
        ModelKey[] unique = [new("CARD", "HEADBUTT"), new("CARD", "SHRUG_IT_OFF"), new("CARD", "ANGER")];
        Check(unique.Select((key, index) => cards[index].Contains(key) && cards.Where((_, other) => other != index).All(set => !set.Contains(key))).All(v => v),
            "frozen single-battle witnesses retain their exact positions");
        var a = unique[0];
        var b = unique[1];
        NormalCombatRewardSearchCondition Row(ModelKeySetFilter keys) => new(0, keys,
            NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, null, null);
        SearchQuery Legacy(params NormalCombatRewardSearchCondition[] rows) => SearchQuery.Empty with { LegacyCombatRewardConstraints = rows };
        var rowA = Row(new([], [a], []));
        var rowB = Row(new([], [b], []));
        Expect(context, Legacy(rowA, rowA), seed, true, "duplicate existential rows may reuse one battle");
        Expect(context, Legacy(rowA, rowB), seed, true, "separate rows can use separate battles");
        Expect(context, Legacy(Row(new([], [a, b], []))), seed, false, "one All row cannot splice two battles");
        Expect(context, Legacy(Row(new([a, b], [], [a]))), seed, true, "Any plus Ban chooses the other battle");
        Expect(context, Legacy(Row(new([a, a, b, b], [b, b], [a, a]))), seed, true, "set duplicates are idempotent");
        Expect(context, Legacy(Row(new([], [a], [b]))), seed, true, "Ban applies to the witnessing battle, not the full window");

        var duplicate = SearchQuery.Empty with { CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Unordered, [a, a, null]) };
        Expect(context, duplicate, seed, false, "two typed copies require distinct battles");
        Check(!new CombatRewardReplay(Compile(context, duplicate, seed)).Matches(Input(seed).RootHash),
            "complete solo C rejects the same distinct-battle violation as Exact");
        Expect(context, duplicate with { CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Unordered, [a, b, null]) },
            seed, true, "two distinct battle witnesses satisfy typed slots");

        var six = NormalCombatRewardSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, Input(seed).RootHash, seed,
            0, context.Ascension, probe.Document.Sections.SelectMany(s => s.NeowChoices).ToArray(),
            probe.Authority.EffectAuthority, probe.Authority.WorldAuthority,
            new(NormalCombatRewardProjectionScope.SearchExact, NormalCombatRewardRouteSelectionMode.ProvablyNeutralRealRoutes) { BattleCount = 6 });
        Check(six.Routes.Count > 0 && six.Routes.All(r => r.Status == SeedDomainEvaluationStatus.Evaluated && r.Battles.Count == 6),
            "six-battle source projection is complete");
        var potions = six.Routes[0].Battles.Select(battle => battle.Potion.Generated
            ? new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.DropSpecific, battle.Potion.PotionKey)
            : new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null)).ToArray();
        var independent = SearchQuery.Empty with {
            CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Unordered, [unique[2], unique[0], unique[1]]),
            CombatPotionRewards = new(6, CombatRewardSequenceOrderMode.Unordered, potions)
        };
        Check(potions.Select(p => (p.Requirement, p.PotionKey)).Distinct().Count() > 1, "potion permutation control is nontrivial");
        Expect(context, independent, seed, true, "card and potion sequences retain their separate three/six-battle windows");
        Expect(context, independent with { CombatCardRewards = independent.CombatCardRewards! with { OrderMode = CombatRewardSequenceOrderMode.Ordered } },
            seed, false, "ordered card control rejects the rotated targets");
        var latePotion = six.Routes[0].Battles.Skip(3).First(battle => battle.Potion.Generated).Potion.PotionKey;
        Check(battles.All(battle => !battle.Potion.Generated), "first three battles cannot witness the late potion");
        Expect(context, SearchQuery.Empty with {
            CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Unordered, [a, null, null]),
            CombatPotionRewards = new(6, CombatRewardSequenceOrderMode.Unordered,
                [new(CombatPotionSlotRequirement.DropSpecific, latePotion), .. Enumerable.Repeat(new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null), 5)])
        }, seed, true, "card and potion first targets require different battle witnesses");
        var lateCard = six.Routes[0].Battles.Skip(3).SelectMany(battle => battle.Cards).Select(card => card.CardKey)
            .First(key => cards.All(set => !set.Contains(key)));
        var narrowCard = independent with { CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Unordered, [lateCard, null, null]) };
        Expect(context, narrowCard, seed, false, "six-battle potion window cannot widen the card window");
        Expect(context, narrowCard with { CombatCardRewards = new(6, CombatRewardSequenceOrderMode.Unordered, [lateCard, null, null, null, null, null]) },
            seed, true, "late card becomes valid only when its own window widens");
        Console.WriteLine($"COMBINATION_COMBAT seed={seed};singleBattleCards={string.Join(',', unique)};completePositiveMatching=true;distinctBattlesAndDomainWindows=true");
    }

    private static SearchContext PartyContext(SearchContext captured, int count, ModelKey[]? bonesPair = null,
        NeowEffectRelicSnapshot[]? relicPool = null)
    {
        // A controlled slot-binding fixture, not a captured multiplayer game.
        // The default keeps captured solo pools. Optional Bones/bag overrides
        // deliberately isolate order/held-effect semantics, not actual party pool
        // eligibility or in-game rarity. Core supplies facts, Exact joint truth.
        var source = captured.Authority;
        var world = source.WorldAuthority!;
        var generation = world.Beta109Generation!;
        var effects = source.EffectAuthority!;
        if (bonesPair is not null) effects = effects with { BonesEligibleRelics = bonesPair,
            SnapshotFingerprint = effects.SnapshotFingerprint + ":fixed-bones-pair" };
        if (relicPool is not null)
        {
            effects = effects with { SharedRelicPoolSource = relicPool, CharacterRelicPoolSource = [],
                SnapshotFingerprint = effects.SnapshotFingerprint + ":controlled-relic-bag" };
            Beta109RelicBucketSnapshot[] buckets = [.. relicPool.GroupBy(r => r.Rarity).Select(g =>
                new Beta109RelicBucketSnapshot(g.Key.ToString(), g.Select(r => r.RelicKey).ToArray(), true) {
                    OrderedEntries = g.Select(r => new Beta109RelicBucketEntrySnapshot(r.RelicKey, r.IsAllowedInShops, true)).ToArray()
                }), new("Shop", [], true) { OrderedEntries = [] }];
            generation = generation with { SharedRelicBuckets = buckets, PlayerRelicBuckets = buckets };
        }
        var players = Enumerable.Range(0, count).Select(slot => new RuntimeContextAuthoritySnapshot(
            source.Character, source.Ascension, slot, count, source.NoRunModifiers, source.VanillaNeowCatalogExact,
            source.AllCharacterCardPoolsUnlocked, source.UnlockedCommonCards, source.UnlockedUncommonCards,
            source.GameVersion, source.ProfileId, source.IsVanilla, source.SourceModId, source.SourceAssembly,
            source.SourceAuthority, source.Completeness, source.ResolutionStatus, effects,
            world with { Beta109Generation = generation with {
                GameMode = WorldGameMode.Multiplayer, IsMultiplayer = true, PlayerCount = count, PersonalPlayerSlot = slot,
                PartyBossDiscoveryExact = true,
                LobbyPlayers = Enumerable.Range(0, count).Select(i => new Beta109LobbyPlayerSnapshot(i, source.Character.CharacterKey, false, true)).ToArray(),
                PartyRelicBuckets = Enumerable.Repeat(generation.PlayerRelicBuckets, count).ToArray()
            } }, WorldGameMode.Multiplayer, PredictionGameModeAuthority.ExplicitRequest)).ToArray();
        var party = new OrderedPartyAuthority(players, Enumerable.Repeat("CapturedSoloPools.FixedSlotFixture", count).ToArray(), players[0].WorldAuthority!);
        return captured with { Authority = players[0], Party = party };
    }

    private static SearchQuery Table(int count, params (int Slot, SearchQuery Query)[] authored) => SearchQuery.Empty with {
        Players = Enumerable.Range(0, count).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) {
            Conditions = authored.FirstOrDefault(a => a.Slot == slot).Query ?? SearchQuery.Empty }).ToArray()
    };

    private static IReadOnlyList<NeowChoiceResult> Choices(SearchContext context, string seed, int slot,
        SearchQuery personal, NeowEffectRngContext? shared = null)
    {
        var owner = context.Party!.Players[slot];
        var input = Input(seed);
        Check(SeedPredictionRequest.TryCreateFromRootHash(input, owner.Character, owner.Ascension,
            owner.PlayersCount, slot, owner, AncientOptionConditionProfile.BroadDefault,
            SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards, 10, false,
            out var request, out var error), "source request " + error);
        shared ??= NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, input.RootHash, 0);
        return PartyNeowProjection.ProjectChoices(request!, input.RootHash, shared.Niche, shared.CombatPotionGeneration,
            authoredCapsuleEffects: PartyInitialQuery.CapsuleEffectPremise(personal));
    }

}
