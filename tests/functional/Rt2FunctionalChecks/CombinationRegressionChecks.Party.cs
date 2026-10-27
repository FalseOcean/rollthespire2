using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class CombinationRegressionChecks
{
    private static void CheckPartyModIdentity(SearchContext captured)
    {
        CheckPartyWorldAlternativeFixture(captured);
        // Controlled identity-binding regression, not a claim that these donor
        // pools describe a particular installed Mod or certify its hooks.
        var identity = CharacterIdentity.FromKey(new("CHARACTER", "RT2_MOD_IDENTITY_FIXTURE"));
        var source = captured.Authority;
        var mod = new RuntimeContextAuthoritySnapshot(identity, source.Ascension, 0, 1,
            source.NoRunModifiers, source.VanillaNeowCatalogExact, source.AllCharacterCardPoolsUnlocked,
            source.UnlockedCommonCards, source.UnlockedUncommonCards, source.GameVersion, source.ProfileId,
            false, "fixture-mod", "fixture-mod", SourceAuthority.ModdedRuntimeBestEffort,
            source.Completeness, IdentityResolutionStatus.Unknown,
            source.EffectAuthority! with { SourceAuthority = SourceAuthority.ModdedRuntimeBestEffort },
            source.WorldAuthority! with { Beta109Generation = source.WorldAuthority.Beta109Generation! with
                { CharacterKey = identity.CharacterKey, IsVanilla = false } },
            source.PredictionGameMode, source.PredictionGameModeAuthority);
        var context = PartyContext(captured with { Authority = mod, CharacterKey = identity.CharacterKey }, 2);
        var worldContext = PartyContext(captured, 2);
        var observation = PartySeedInformation.Project("000000000000", worldContext.Party!);
        var act = worldContext.Party!.World.Beta109Generation!.OrderedActCatalog.First(a => a.Act == 1);
        var actual = observation.Bosses.First(b => b.Act == 1 && b.Ordinal == 1).BossKey;
        var other = act.Bosses.FirstOrDefault(k => k != actual);
        if (!other.IsValid) other = new("ENCOUNTER", "IMPOSSIBLE_PARTY_BOSS");
        VariantScopedBossBranch Row(ModelKey boss) => new(1, observation.Acts[0], new([boss], [], []), ModelKeySetFilter.Empty, false);
        var alternativeQuery = Table(2) with { VariantBossBranches = [Row(other), Row(actual)] };
        var alternative = SearchCompiler.Compile(alternativeQuery, worldContext);
        Check(alternative.Status == QueryNormalizationStatus.Legal && PartyInitialQuery.MatchesWorld(alternative.NormalizedQuery, observation),
            "Party rows sharing one Variant remain OR alternatives");
        var conflictingSeats = alternativeQuery with { VariantBossBranches = [], Players = alternativeQuery.Players.Select((p, i) => p with
            { Conditions = SearchQuery.Empty with { VariantBossBranches = [Row(i == 0 ? actual : other)] } }).ToArray() };
        var conflicting = SearchCompiler.Compile(conflictingSeats, worldContext);
        Check(conflicting.Status == QueryNormalizationStatus.Impossible && !PartyInitialQuery.MatchesWorld(conflicting.NormalizedQuery, observation),
            "Distinct seat declarations remain a conjunction");
        Check(context.Party!.Players.All(p => !p.Character.IsKnownVanilla && p.UsesBestEffortModel),
            "Mod party retains origin and best-effort provenance");
        const string seed = "000000000000";
        for (int slot = 0; slot < 2; slot++)
        {
            var choices = Choices(context, seed, slot, SearchQuery.Empty);
            Check(choices.Count == 3, "Mod party opening choices remain available for P" + (slot + 1));
            Check(context.Party.Players[slot].PlayerSlotIndex == slot &&
                context.Party.World.Beta109Generation!.LobbyPlayers[slot].CharacterKey == identity.CharacterKey,
                "Mod party preserves fixed RNG owner and shared roster");
        }
        void Reject(RuntimeContextAuthoritySnapshot[] players, string label)
        {
            bool rejected = false;
            try { _ = new OrderedPartyAuthority(players, context.Party.UnlockProvenance, context.Party.World); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, label);
        }
        var valid = context.Party.Players;
        Reject([valid[0], valid[0]], "Duplicate slot authority is still rejected for Mods");
        var malformed = new RuntimeContextAuthoritySnapshot(CharacterIdentity.FromKey(new("CARD", "NOT_A_CHARACTER")),
            source.Ascension, 0, 2, true, true, true, 4, 2, source.GameVersion, source.ProfileId,
            false, "fixture-mod", "fixture-mod", SourceAuthority.ModdedRuntimeBestEffort,
            source.Completeness, IdentityResolutionStatus.Unknown, mod.EffectAuthority, valid[0].WorldAuthority,
            WorldGameMode.Multiplayer, PredictionGameModeAuthority.ExplicitRequest);
        Reject([malformed, valid[1]], "Malformed character category is still rejected");
        var missing = new RuntimeContextAuthoritySnapshot(identity, source.Ascension, 0, 2, true, true,
            null, null, null, source.GameVersion, source.ProfileId, false, "fixture-mod", "fixture-mod",
            SourceAuthority.Incomplete, SnapshotCompleteness.Missing, IdentityResolutionStatus.Unknown,
            null, valid[0].WorldAuthority, WorldGameMode.Multiplayer, PredictionGameModeAuthority.ExplicitRequest);
        var partialParty = new OrderedPartyAuthority([missing, valid[1]], ["CapturedProfile", "CapturedProfile"], valid[0].WorldAuthority!);
        Check(PartySeedInformation.Project("000000000000", partialParty).Players[0].IdentityPrecision == PredictionPrecision.Unknown,
            "Missing captured Neow eligibility stays an unknown observation within a valid roster");
    }

    private static void CheckPartyWorldAlternativeFixture(SearchContext captured)
    {
        var context = PartyContext(captured, 2);
        const int count = 32;
        var initial = PartySeedInformation.Project("000000000000", context.Party!);
        ModelKey actual = initial.Bosses.First(b => b.Act == 1 && b.Ordinal == 1).BossKey;
        VariantScopedBossBranch Row(ModelKey boss) => new(1, initial.Acts[0], new([boss], [], []), ModelKeySetFilter.Empty, false);
        var query = Table(2) with { VariantBossBranches = [Row(new("ENCOUNTER", "ABSENT_OR_ALTERNATIVE")), Row(actual)] };
        var compiled = SearchCompiler.Compile(query, context);
        var whole = RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.Compile(compiled, new("000000000000", count, count, 1)).Plan!;
        var shared = RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.ForPlayer(whole, compiled.PlayerSearches.Last());
        var replay = new WorldFamilyReplay(shared);
        Check(replay.Plan.Acts.All(a => !a.FamilyBossBranchesConjunctive), "MP W consumes composed OR rows");
        var gpu = new WorldFamilyGpuPlan(replay, specializeSolo: false);
        var expected = new List<uint>();
        for (uint ordinal = 0; ordinal < count; ordinal++)
        {
            string seed = Beta110SeedCodec.FormatOrdinal(ordinal);
            var observation = PartySeedInformation.Project(seed, context.Party!);
            bool truth = PartyInitialQuery.MatchesWorld(compiled.NormalizedQuery, observation);
            Check(replay.Matches(Beta111Profile.Instance.ComputeRootSeed(seed)) == truth, "MP W same-Variant OR matches source observation");
            if (truth) expected.Add(ordinal);
        }
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "w-gpu-party-or");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "party-or.glsl"), gpu.ShaderSource());
        var fixtures = new[] { false, true }.Select(compact => new {
            name = "PartySameVariantOr", shader = "party-or.glsl", buffers = gpu.Buffers, scratchWords = gpu.ScratchWords,
            batchMeta = new uint[] { 0, 0, count, compact ? 11u : count, compact ? 1u : 0u, count, 0, 0 },
            ordinals = Enumerable.Range(0, count).Where(i => !compact || i % 3 == 0).Select(i => (uint)i).ToArray(),
            expected = expected.Where(i => !compact || i % 3 == 0).ToArray()
        }).ToArray();
        File.WriteAllText(Path.Combine(directory, "fixtures.json"), System.Text.Json.JsonSerializer.Serialize(fixtures));
        File.WriteAllText(Path.Combine(directory, "project.godot"), "config_version=5\n[application]\nconfig/name=\"Party W OR parity\"\n[rendering]\nrenderer/rendering_method=\"mobile\"\n");
        File.Copy("tests/functional/Rt2FunctionalChecks/WorldGpuSanity.gd", Path.Combine(directory, "check.gd"), true);
    }

    private static void CheckCapsuleJointWitness(SearchContext captured)
    {
        const string seed = "0000000001FA";
        var arcane = BaseGameModelKeys.Relics.ArcaneScroll;
        // Deliberately tiny synthetic lanes: one Whetstone / Prayer Wheel /
        // White Beast Statue. Prayer Wheel's fixture rarity is not game data.
        NeowEffectRelicSnapshot[] pool = [
            new(BaseGameModelKeys.OrdinaryRelics.Whetstone, 0, EffectRelicRarity.Common, NestedRelicEffectKind.Whetstone),
            new(new("RELIC", "PRAYER_WHEEL"), 1, EffectRelicRarity.Uncommon, NestedRelicEffectKind.NoTrackedImmediateEffect),
            new(new("RELIC", "WHITE_BEAST_STATUE"), 2, EffectRelicRarity.Rare, NestedRelicEffectKind.NoTrackedImmediateEffect)
        ];
        var context = PartyContext(captured, 2, [Large, arcane], pool);
        var first = SearchQuery.Empty with { OpeningRoute = new(Bones),
            OpeningRouteRelicRequirement = new(Bones, [Large, arcane], BonesRouteOrderMode.AnyOrder) };
        CheckPartyProjectionIsolation(context, seed, first);
        var choices = PartyNeowProjection.ConcreteRoutes(Choices(context, seed, 0, first).Single(c => c.RelicKey == Bones)).ToArray();
        Check(choices.Length == 2, "controlled Bones fixture contains exactly both acquisition orders");
        foreach (var choice in choices)
        {
            var continuation = choice.OpeningRewardContinuations!.Routes.Single();
            var reward = Rewards(context, seed, 0, choice);
            bool largeFirst = continuation.Route.AcquisitionOrder[0] == Large;
            ModelKey card = new("CARD", largeFirst ? "PYRE" : "UNMOVABLE");
            var outputs = choice.BonesOutcome!.OriginalRoutes.Single().RelicScopedResults.Single(r => r.SourceRelicKey == arcane)
                .EffectGroups.SelectMany(g => g.OrderedItems).Where(i => i.Kind == PredictedEffectKind.AddCard).Select(i => i.TargetKey!.Value);
            Check(outputs.SequenceEqual(new[] { card }), "frozen Arcane output distinguishes the two orders");
            Check(continuation.NicheState!.CallCount == 1, "unlisted Whetstone skips obtain hook; only final Bones curse consumes Niche");
            var held = new ModelKey("RELIC", largeFirst ? "WHITE_BEAST_STATUE" : "PRAYER_WHEEL");
            Check(reward.Battles[0].AppliedRewardImpactSources.Any(s => s.ModelKey == held), "unlisted nested relic retains its held reward effect");
            Check(largeFirst ? reward.Battles.All(b => b.Potion.Generated) : reward.Battles[0].CardRewards.Count == 2 && !reward.Battles[0].Potion.Generated,
                "WBS forces drops; Prayer Wheel adds a reward without executing an unlisted obtain hook");
        }
        NeowStructuredEffectSearchCondition ArcaneTarget(string card) => new(arcane, NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.ProductRelevantEffects, NeowStructuredOutputKind.Card, [new("CARD", card)]);
        var n = first with { StructuredOpeningEffects = [ArcaneTarget("PYRE")] };
        var c = first with { CombatPotionRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [new(CombatPotionSlotRequirement.NoDrop, null)]) };
        var joint = n with { CombatPotionRewards = c.CombatPotionRewards };
        Expect(context, Table(2, (0, n)), seed, true, "N has a Large-then-Arcane witness");
        Expect(context, Table(2, (0, c)), seed, true, "C has an Arcane-then-Large witness");
        Expect(context, Table(2, (0, joint)), seed, false, "independent N and C witnesses cannot form a joint result");
        CheckCpuFamilies(context, Table(2, (0, joint)), seed, ["N.Neow", "C.CombatReward"]);
        foreach (var order in new[] { new[] { Large, arcane }, new[] { arcane, Large } })
        {
            var pinned = joint with { OpeningRouteRelicRequirement = new(Bones, order, BonesRouteOrderMode.ExactOrder) };
            Expect(context, Table(2, (0, pinned)), seed, false, "neither explicit order satisfies both families");
        }
        var aligned = joint with { StructuredOpeningEffects = [ArcaneTarget("UNMOVABLE")],
            CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [new("CARD", "BLUDGEON")]) };
        var prayer = Rewards(context, seed, 0, choices.Single(c => c.OpeningRewardContinuations!.Routes.Single().Route.AcquisitionOrder[0] == arcane));
        Check(!prayer.Battles[0].Cards.Any(card => card.CardKey.Entry == "BLUDGEON") &&
            prayer.Battles[0].CardRewards.Skip(1).SelectMany(r => r.Cards).Any(card => card.CardKey.Entry == "BLUDGEON"),
            "aligned positive uses a card exclusive to the unlisted Prayer Wheel's extra reward");
        Expect(context, Table(2, (0, aligned)), seed, true, "Exact same-order N/C positive retains the real extra reward");
        // C deliberately omits an unspecified Capsule held effect. This fixed
        // source-derived positive is therefore outside the sieve's coverage;
        // Exact truth and its joint route witness remain unchanged.
        CheckCpuFamilies(context, Table(2, (0, aligned)), seed, ["N.Neow", "C.CombatReward"],
            projectedOutFamilies: ["C.CombatReward"]);
        var explicitPrayer = aligned with { StructuredOpeningEffects = [ArcaneTarget("UNMOVABLE"),
            new(Large, NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [new("RELIC", "PRAYER_WHEEL")])] };
        Expect(context, Table(2, (0, explicitPrayer)), seed, true,
            "explicit Prayer Wheel and Arcane output share the same real route with the extra C reward");
        CheckCpuFamilies(context, Table(2, (0, explicitPrayer)), seed, ["N.Neow", "R.Relic", "C.CombatReward"]);
        var incompatibleHeld = explicitPrayer with { StructuredOpeningEffects = [ArcaneTarget("UNMOVABLE"),
            new(Large, NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [new("RELIC", "WHITE_BEAST_STATUE")])] };
        Expect(context, Table(2, (0, incompatibleHeld)), seed, false,
            "explicit held relic from the opposite order cannot replace the required joint route witness");
        CheckCapsuleHookAcrossGap(captured, seed, pool, first);
        Console.WriteLine($"COMBINATION_JOINT_WITNESS seed={seed};N=PYRE;C=NoDrop;NFast=true;CFast=true;Exact=NoMatch;acceptedFalsePositive=true;unlistedPrayerPositive=ExactOnly;explicitPrayerPositive=FastRetained");
    }

    private static void CheckPartyProjectionIsolation(SearchContext context, string seed, SearchQuery personal)
    {
        var owner = context.Party!.Players[0];
        var input = Input(seed);
        Check(SeedPredictionRequest.TryCreateFromRootHash(input, owner.Character, owner.Ascension,
            owner.PlayersCount, owner.PlayerSlotIndex, owner, AncientOptionConditionProfile.BroadDefault,
            SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards, 10, false,
            out var request, out var error), "isolated party projection request " + error);
        var shared = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, input.RootHash, 0);
        var sharedBefore = (shared.Niche.State, shared.Niche.CallCount,
            shared.CombatPotionGeneration.State, shared.CombatPotionGeneration.CallCount);
        string authorityBefore = System.Text.Json.JsonSerializer.Serialize(new { owner.EffectAuthority, owner.WorldAuthority });
        var premise = PartyInitialQuery.CapsuleEffectPremise(personal);
        var full = PartyNeowProjection.ProjectChoices(request!, input.RootHash, shared.Niche,
            shared.CombatPotionGeneration, authoredCapsuleEffects: premise);
        string fullBefore = System.Text.Json.JsonSerializer.Serialize(full);
        string bonesBefore = System.Text.Json.JsonSerializer.Serialize(full.Single(c => c.RelicKey == Bones));

        request!.NeowEffectRoots = new HashSet<ModelKey> { Bones };
        var narrowed = PartyNeowProjection.ProjectChoices(request, input.RootHash, shared.Niche,
            shared.CombatPotionGeneration, authoredCapsuleEffects: premise);
        Check(narrowed.Select(c => (c.SlotIndex, c.RelicKey, c.IdentityPrecision))
            .SequenceEqual(full.Select(c => (c.SlotIndex, c.RelicKey, c.IdentityPrecision))),
            "Skipping unrelated party effects retains every offer identity and slot");
        Check(System.Text.Json.JsonSerializer.Serialize(narrowed.Single(c => c.RelicKey == Bones)) == bonesBefore,
            "Narrowed Bones retains all route fingerprints, branch payloads and RNG continuations verbatim");

        int bagBuilds = 0;
        var preparedBag = new Lazy<IReadOnlyList<NeowEffectRelicSnapshot>>(() =>
        {
            bagBuilds++;
            return NeowRewardGenerator.BuildOrderedRelicBag(Beta111Profile.Instance, input.RootHash, owner);
        });
        var prepared = PartyNeowProjection.ProjectChoices(request, input.RootHash, shared.Niche,
            shared.CombatPotionGeneration, authoredCapsuleEffects: premise, preparedRelicBag: preparedBag);
        Check(bagBuilds == 1 && System.Text.Json.JsonSerializer.Serialize(prepared) == System.Text.Json.JsonSerializer.Serialize(narrowed),
            "Prepared slot bag preserves the entire opening payload and builds only when consumed");
        var parent = Compile(context, Table(context.Party.Players.Count, (0, personal)), seed);
        var child = RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.ForPlayer(
            parent, parent.CompiledSearch.PlayerSearches[0]);
        Check(SearchAuthorityProjector.TryProjectForRootHash(child, input.RootHash, input.SeedIdentity,
            out var baselineAuthority, out var baselineDisposition, out var baselineIssue), "baseline slot authority projection");
        Check(SearchAuthorityProjector.TryProjectForRootHash(child, input.RootHash, input.SeedIdentity,
            out var preparedAuthority, out var preparedDisposition, out var preparedIssue, preparedBag), "prepared slot authority projection");
        Check(bagBuilds == 1 && baselineDisposition == preparedDisposition && baselineIssue == preparedIssue &&
            System.Text.Json.JsonSerializer.Serialize(baselineAuthority) == System.Text.Json.JsonSerializer.Serialize(preparedAuthority),
            "Recursive authority reuses the slot bag while retaining every original authority field and fingerprint");
        request.NeowEffectRoots = new HashSet<ModelKey>();
        var forbiddenBag = new Lazy<IReadOnlyList<NeowEffectRelicSnapshot>>(() =>
            throw new InvalidOperationException("UnrequestedPartyBagMustNotBePrepared"));
        _ = PartyNeowProjection.ProjectChoices(request, input.RootHash, shared.Niche,
            shared.CombatPotionGeneration, authoredCapsuleEffects: premise, preparedRelicBag: forbiddenBag);
        Check(!forbiddenBag.IsValueCreated, "Unrequested party effects do not evaluate the prepared bag");
        var noBagChild = RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.ForPlayer(
            parent, parent.CompiledSearch.PlayerSearches[^1]);
        Check(SearchAuthorityProjector.TryProjectForRootHash(noBagChild, input.RootHash, input.SeedIdentity,
            out _, out _, out _, forbiddenBag) && !forbiddenBag.IsValueCreated,
            "Authority without a bag obligation does not evaluate the prepared bag");
        Check(System.Text.Json.JsonSerializer.Serialize(full) == fullBefore,
            "Later party projections do not mutate previously returned branches");
        Check(sharedBefore == (shared.Niche.State, shared.Niche.CallCount,
            shared.CombatPotionGeneration.State, shared.CombatPotionGeneration.CallCount),
            "Full and narrowed party projections leave incoming shared RNG state and call counts unchanged");
        Check(System.Text.Json.JsonSerializer.Serialize(new { owner.EffectAuthority, owner.WorldAuthority }) == authorityBefore,
            "Party branch projection leaves captured deck, bags and source authority unchanged");
    }

    private static void CheckCapsuleHookAcrossGap(SearchContext captured, string seed, NeowEffectRelicSnapshot[] pool, SearchQuery first)
    {
        var context = PartyContext(captured, 4, first.OpeningRouteRelicRequirement!.RequiredRelicKeys.ToArray(), pool);
        var whetstone = BaseGameModelKeys.OrdinaryRelics.Whetstone;
        first = first with { OpeningRouteRelicRequirement = first.OpeningRouteRelicRequirement with { OrderMode = BonesRouteOrderMode.ExactOrder } };
        var authored = first with { StructuredOpeningEffects = [new(Large, NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [whetstone])] };
        NeowChoiceResult Source(SearchQuery query) => PartyNeowProjection.ConcreteRoutes(Choices(context, seed, 0, query).Single(c => c.RelicKey == Bones))
            .Single(c => c.OpeningRewardContinuations!.Routes.Single().Route.AcquisitionOrder[0] == Large);
        var skipped = Source(first).OpeningRewardContinuations!.Routes.Single();
        var executed = Source(authored).OpeningRewardContinuations!.Routes.Single();
        Check(skipped.RewardsRngState == executed.RewardsRngState, "authored Whetstone does not change personal Capsule extraction draws");
        Check(executed.NicheState!.CallCount > skipped.NicheState!.CallCount, "only explicitly listed Whetstone executes its shared shuffle");
        var skippedChoice = Choices(context, seed, 3, SearchQuery.Empty, After(seed, skipped)).Single(c => c.RelicKey == Leaf);
        var executedChoice = Choices(context, seed, 3, SearchQuery.Empty, After(seed, executed)).Single(c => c.RelicKey == Leaf);
        var skippedTarget = TransformedCard(skippedChoice);
        var executedTarget = TransformedCard(executedChoice);
        Check(skippedTarget == new ModelKey("CARD", "BRAND") && executedTarget == new ModelKey("CARD", "CRIMSON_MANTLE"),
            "frozen skipped/authored hook outputs distinguish the downstream shared observation");
        var combatTarget = Rewards(context, seed, 3, skippedChoice).Battles[0].Cards[0].CardKey;
        SearchQuery Last(ModelKey target) => SearchQuery.Empty with { OpeningRoute = new(Leaf), StructuredOpeningEffects = [LeafTarget(target)],
            CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [combatTarget]) };
        foreach (var (prefix, target, wrong) in new[] { (first, skippedTarget, executedTarget), (authored, executedTarget, skippedTarget) })
        {
            var positive = Table(4, (0, prefix), (3, Last(target)));
            Expect(context, positive, seed, true, "Capsule obtain premise reaches P4 through two unselected slots");
            Expect(context, Table(4, (0, prefix), (3, Last(wrong))), seed, false, "wrong obtain premise cannot witness downstream N/C");
            CheckCpuFamilies(context, positive, seed, prefix == authored ? ["N.Neow", "R.Relic", "C.CombatReward"] : ["N.Neow", "C.CombatReward"]);
        }
        Console.WriteLine($"COMBINATION_CAPSULE_HOOK seed={seed};skipped={skippedTarget};authored={executedTarget};exactHeldEffectsRetained=true;authoredSharedHookPreserved=true");
    }

    private static NeowStructuredEffectSearchCondition LeafTarget(ModelKey target) => new(Leaf,
        NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card, [target]);

    private static ModelKey TransformedCard(NeowChoiceResult choice) => choice.EffectGroups.SelectMany(g => g.OrderedItems)
        .Single(i => i.Kind == PredictedEffectKind.TransformCard).TargetKey!.Value;

    private static NeowEffectRngContext After(string seed, OpeningRewardContinuation continuation)
    {
        Check(continuation.NicheState is not null && continuation.CombatPotionGenerationState is not null,
            "selected opening supplies exact shared continuation");
        return NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, Input(seed).RootHash, 0)
            .WithShared(continuation.NicheState!.Restore(), continuation.CombatPotionGenerationState!.Restore());
    }

    private static NormalCombatRewardRoutePredictionResult Rewards(SearchContext context, string seed, int slot,
        NeowChoiceResult choice) => NormalCombatRewardSequencePredictor.PredictFromRootHash(Beta111Profile.Instance,
            Input(seed).RootHash, seed, slot, context.Ascension, [choice], context.Party!.Players[slot].EffectAuthority,
            context.Party.Players[slot].WorldAuthority, new(NormalCombatRewardProjectionScope.SearchExact,
                NormalCombatRewardRouteSelectionMode.PinnedRealRoute, choice.RelicKey)).Routes.Single();

    private static void CheckPartyGaps(SearchContext captured)
    {
        const string seed = "0000000001FA";
        var context = PartyContext(captured, 4);
        var leafy = BaseGameModelKeys.Relics.LeafyPoultice;
        var observation = TransformedCard(Choices(context, seed, 1, SearchQuery.Empty).Single(c => c.RelicKey == Leaf));
        Check(observation == new ModelKey("CARD", "BODY_SLAM"), "frozen observation-only result uses the initial shared cursor");
        var observer = SearchQuery.Empty with { StructuredOpeningEffects = [LeafTarget(observation)] };
        var neutralLater = TransformedCard(Choices(context, seed, 3, SearchQuery.Empty).Single(c => c.RelicKey == Leaf));
        foreach (ModelKey[] order in new[] { new[] { leafy, Leaf }, new[] { Leaf, leafy } })
        {
            var first = SearchQuery.Empty with { OpeningRoute = new(Bones),
                OpeningRouteRelicRequirement = new(Bones, order, BonesRouteOrderMode.ExactOrder) };
            var source = PartyNeowProjection.ConcreteRoutes(Choices(context, seed, 0, first).Single(c => c.RelicKey == Bones))
                .Single(c => c.OpeningRewardContinuations!.Routes.Single().Route.AcquisitionOrder.SequenceEqual(order));
            var arrival = After(seed, source.OpeningRewardContinuations!.Routes.Single());
            var lastChoice = Choices(context, seed, 3, SearchQuery.Empty, arrival).Single(c => c.RelicKey == Leaf);
            var target = TransformedCard(lastChoice);
            Check(target == new ModelKey("CARD", "IMPERVIOUS") && target != neutralLater,
                "frozen selected prefix changes P4's shared transform witness");
            var reward = Rewards(context, seed, 3, lastChoice);
            Check(reward.Status == SeedDomainEvaluationStatus.Evaluated && reward.Battles[0].Cards.Count > 0,
                "P4 has a source-derived personal C witness");
            Check(reward.Battles[0].Cards[0].CardKey == new ModelKey("CARD", "FORGOTTEN_RITUAL"),
                "frozen personal reward remains bound to actual slot 3 despite skipped slots");
            var last = SearchQuery.Empty with { OpeningRoute = new(Leaf), StructuredOpeningEffects = [LeafTarget(target)],
                CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [reward.Battles[0].Cards[0].CardKey]) };
            var table = Table(4, (0, first), (1, observer), (3, last));
            Expect(context, table, seed, true, "selected P1 -> observation-only P2 -> empty P3 -> selected P4+C");
            Expect(context, Table(4, (1, observer), (3, last)), seed, false, "unselected predecessor must not obtain the displayed opening");
            Expect(context, Table(4, (0, first), (1, observer), (3, last with { StructuredOpeningEffects = [LeafTarget(neutralLater)] })),
                seed, false, "intermediate empty slots must not reset the accumulated shared cursor");
            var advancedObserver = TransformedCard(Choices(context, seed, 1, SearchQuery.Empty, arrival).Single(c => c.RelicKey == Leaf));
            Check(advancedObserver != observation, "observation-only and selected arrivals are distinguishable");
            Expect(context, Table(4, (0, first), (1, observer with { StructuredOpeningEffects = [LeafTarget(advancedObserver)] }), (3, last)),
                seed, false, "observation-only N must not inherit a selected predecessor's cursor");
            var result = Exact(context, table, seed);
            Check(result.Document!.Party!.Transactions.Select(t => t.Slot).SequenceEqual(new[] { 0, 3 }),
                "only explicitly selected slots own opening transactions");
            Check(result.Document.Party.Transactions[0].OpeningRouteId == source.OpeningRewardContinuations.Routes.Single().Route.RouteId,
                "Exact publishes the selected Bones acquisition order");
            CheckCpuFamilies(context, table, seed, ["N.Neow", "C.CombatReward"]);
            Console.WriteLine($"COMBINATION_PARTY_GAP seed={seed};order={string.Join(',', order)};observer={observation};P4={target};C={reward.Battles[0].Cards[0].CardKey}");
        }
    }

    private static void CheckCpuFamilies(SearchContext context, SearchQuery query, string seed, string[] expectedFamilies,
        string[]? projectedOutFamilies = null)
    {
        var request = Compile(context, query, seed);
        var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false);
        var execution = new FamilyExecutionContext(requiresGpu: false, failOnPhysicalRecovery: true);
        try
        {
            Check(expectedFamilies.All(id => families.Any(f => f.FamilyId == id)), "required CPU families are registered");
            Check(projectedOutFamilies is null || projectedOutFamilies.All(expectedFamilies.Contains),
                "every explicitly projected-out family remains a registered obligation");
            Check(VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, seed, out ulong ordinal, out _, out _), "fixed seed ordinal");
            var batch = new SearchBatch(ordinal, 1);
            foreach (var family in families)
            {
                Check(!family.ConditionPerformance.UsesGpu, "CPU-only regression execution");
                var result = family.InvokeAsync(execution, new(batch, request), FamilyCandidateSet.Dense(batch), default).AsTask().GetAwaiter().GetResult();
                bool projectedOut = projectedOutFamilies?.Contains(family.FamilyId) == true;
                Check(result.EnumerateLogicalOrdinals().SequenceEqual(projectedOut ? Array.Empty<ulong>() : new[] { 0UL }),
                    family.FamilyId + (projectedOut ? " rejects the fixed root under the declared neutral projection" : " retains the fixed root"));
            }
        }
        finally
        {
            foreach (var family in families) family.DisposeAsync(execution).AsTask().GetAwaiter().GetResult();
            execution.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void CheckPartyWorkerBudgets(SearchContext captured)
    {
        var context = PartyContext(captured, 2);
        SearchQuery[] personalQueries = [
            SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with {
                NeowRelics = new([], [BaseGameModelKeys.Relics.GoldenPearl], []) } },
            SearchQuery.Empty with { StructuredOpeningEffects = [LeafTarget(new("CARD", "BODY_SLAM"))] }
        ];
        foreach (var personal in personalQueries)
        foreach (int workers in new[] { 1, 32, 33, 64 })
        foreach (bool gpuAvailable in new[] { false, true })
        {
            var request = Compile(context, Table(2, (0, personal)), "0000000001FA", workers);
            // Registration plans both CPU and Auto realizations without invoking
            // any family, allocating GPU resources, or starting worker tasks.
            var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable);
            var execution = new FamilyExecutionContext(requiresGpu: false, failOnPhysicalRecovery: true);
            IFamilyInvocation? cpu = null;
            try
            {
                var neow = families.Single(f => f.FamilyId == "N.Neow");
                Check(neow is NeowFamily.PartyInitial, "worker budget exercises the registered party N realization");
                cpu = neow.CpuRealizations.Single();
                int expectedCpuWorkers = workers == 1 ? 1 : 32;
                Check(!cpu.ConditionPerformance.UsesGpu && cpu.ConditionPerformance.PhysicalImplementationRevision
                    .Contains($".P{expectedCpuWorkers}.B", StringComparison.Ordinal),
                    $"party N CPU budget is bounded for public P{workers};gpuAvailable={gpuAvailable}");
                Check(request.WorkerCount == workers, "party N physical budget leaves the public Exact worker budget unchanged");
            }
            finally
            {
                if (cpu is not null) cpu.DisposeAsync(execution).AsTask().GetAwaiter().GetResult();
                foreach (var family in families) family.DisposeAsync(execution).AsTask().GetAwaiter().GetResult();
                execution.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }
}
