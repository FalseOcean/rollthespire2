using System.Text.Json;
using MegaCrit.Sts2.Core.Extensions;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Persistence;

internal static class MerchantRelicSearchChecks
{
    internal static int Run()
    {
        const string version = "0.113.1";
        const string seed = "6Q8LL102Y4CF";
        var profile = Beta111Profile.Instance;
        var world = RelicSequenceFixtureFactory.CreateModern();
        var source = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect, 10, 0, 1, version);
        var authority = source.WithEffectAuthority(source.EffectAuthority! with { MerchantInitialInventoryShapeExact = true })
            .WithWorldAuthority(world with { CapturedProfileId = profile.ProfileId, Beta109Generation = world.Beta109Generation! with
                { Profile = profile.ProfileId, RngSemantics = RngSemanticProfileId.Modern113 } });
        SearchContext Context(RuntimeContextAuthoritySnapshot input) => SearchContextFactory.From(input.ProfileId,
            input.Character.CharacterKey, input.Ascension, input,
            new(version, version, "beta", "fixture", true, ""), AncientOptionConditionProfile.BroadDefault);
        var context = Context(authority);
        var root = TrustedRootHashInput.FromCanonicalSeed(profile, seed);
        var bags = RelicSequencePredictor.PredictFromRootHash(profile, root.RootHash, seed, authority.WorldAuthority);
        var offers = NormalMerchantRelicProjector.Project(root.RootHash, authority, bags, merchantCount: 2);
        if (offers.Merchants.Count < 2 || offers.Merchants[0].Left.RelicKey is not { } left ||
            offers.Merchants[0].Middle.RelicKey is not { } middle || offers.Merchants[1].Middle.RelicKey is not { } later)
            throw new InvalidDataException("Missing test offers: " + offers.IssueCode);
        var query = SearchQuery.Empty with { MerchantRelicConditions = [new(1, 1, left), new(1, 2, later)] };
        PreparedSearch Prepare(SearchQuery q, bool skip = false, SearchContext? ctx = null) =>
            SearchPreparationCompiler.Compile(SearchCompiler.Compile(q, ctx ?? context),
                new(seed, 1, 1, 1, SkipExactValidation: skip)).Plan ?? throw new InvalidDataException("Missing prepared plan");
        SearchQueryEvaluation Evaluate(SearchQuery q, SearchContext? ctx = null) =>
            ProductionExactSearchEvaluator.Evaluate(Prepare(q, ctx:ctx).Model, root).Evaluation;
        void Check(bool ok, string name) { if (!ok) throw new InvalidDataException("MerchantRelic: " + name); }
        Check113Bags(authority, root.RootHash);
        CheckRelicPoolCapture();
        Check113PotionStock();
        CheckColorlessPrefix(authority);
        var positive = Evaluate(query);
        Check(positive.Disposition == SearchDisposition.Match &&
            positive.Evidence.Any(e => e.RelatedKey == left && e.Ordinal == 1 && e.OutcomeId == "Left") &&
            positive.Evidence.Any(e => e.RelatedKey == later && e.Ordinal == 2 && e.OutcomeId == "Middle"), "either slot and window evidence");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, middle)] }).Disposition == SearchDisposition.Match,
            "right boundary inclusive");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, later)] }).Disposition == SearchDisposition.NoMatch,
            "outside window rejected");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, middle), new(1, 1, left)] }).Disposition == SearchDisposition.Match,
            "shelf accepts both targets at one shop in either slot order");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 2, later), new(1, 2, left)] }).Disposition == SearchDisposition.Match,
            "shelf accepts targets from separate shops in either visit order");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, left), new(1, 1, later)] }).Disposition == SearchDisposition.NoMatch,
            "shelf requires all targets inside the shared window");
        var missing = Context(authority.WithWorldAuthority(null));
        Check(Evaluate(query, missing).Disposition == SearchDisposition.Unknown, "incomplete projection cannot confirm");
        Check(Evaluate(query with { FishingRodDrawResearch = true }).Disposition == SearchDisposition.Match, "combined exact");
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 1, later)], FishingRodDrawResearch = true }).Disposition ==
            SearchDisposition.NoMatch, "another matching domain cannot bypass missing target");
        var partial = authority.WithEffectAuthority(authority.EffectAuthority! with { MerchantInitialInventoryShapeExact = false });
        Check(Evaluate(query with { MerchantRelicConditions = [new(1, 2, left)] }, Context(partial)).Disposition == SearchDisposition.Match &&
            Evaluate(query with { MerchantRelicConditions = [new(1, 2, later)] }, Context(partial)).Disposition == SearchDisposition.Unknown &&
            Evaluate(query with { MerchantRelicConditions = [new(1, 2, later), new(1, 1, later)] }, Context(partial)).Disposition == SearchDisposition.NoMatch,
            "partial window retains witnesses and independent rejections");
        var options = SearchPresetStore.CreateJsonOptions();
        var restored = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(query, options), options)!;
        Check(Prepare(restored).Model.CompiledSearch.SemanticFingerprint == Prepare(query).Model.CompiledSearch.SemanticFingerprint &&
            SearchContinuationIdentity.For(Prepare(query)) != SearchContinuationIdentity.For(Prepare(SearchQuery.Empty)), "preset and continuation identity");
        try
        {
            _ = Prepare(query with { MerchantRelicConditions = [new(4, 3, left)] });
            throw new InvalidDataException("Invalid window accepted");
        }
        catch (ArgumentException ex) when (ex.Message == "MerchantRelic:InvalidWindow") { }
        foreach (bool skip in new[] { false, true })
        {
            var plan = Prepare(query, skip);
            var physical = FamilyExecutionCoordinator.Plan(plan, gpuAvailable: false);
            Check(physical.FilterRemainders.Contains("MerchantRelic:ProductionExactOnly"), "coverage identifies exact-only duty");
            Scan().GetAwaiter().GetResult();
            async Task Scan()
            {
                await using var session = FamilyExecutionCoordinator.Start(plan, gpuAvailable:false);
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Check(session.TryReadCandidate(out var hit) && hit!.Seed == seed && hit.IsUnfiltered == skip &&
                    hit.ValidationDisposition == (skip ? SearchDisposition.NotEvaluatedByPolicy : SearchDisposition.Match), "production candidate validation");
            }
        }
        Console.WriteLine($"MERCHANT_RELIC_SEARCH=PASS;seed={seed};left={left};middle={middle};laterMiddle={later};partialWindowsAndSkipVerified=true");
        return 0;
    }

    private sealed class ObservedPool(object? result)
    {
        public object? GetUnlockedRelics(object unlockState) => result;
    }

    private sealed class PrefixClonePotion : MegaCrit.Sts2.Core.Models.PotionModel
    {
        public override MegaCrit.Sts2.Core.Entities.Potions.PotionRarity Rarity => MegaCrit.Sts2.Core.Entities.Potions.PotionRarity.Rare;
        public override MegaCrit.Sts2.Core.Entities.Potions.PotionUsage Usage => MegaCrit.Sts2.Core.Entities.Potions.PotionUsage.AnyTime;
        public override MegaCrit.Sts2.Core.Entities.Cards.TargetType TargetType => MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyPlayer;
        protected override void AfterCloned() => base.AfterCloned();
    }

    private static void CheckColorlessPrefix(RuntimeContextAuthoritySnapshot source)
    {
        // A custom potion may clone successfully in-game while capture cannot
        // certify its override. Keep every rarity stocked; this is not the
        // different case where the native merchant itself cannot be created.
        var effects = source.EffectAuthority!;
        var nativePotion = new PrefixClonePotion();
        if (!nativePotion.ToMutable().IsMutable) throw new Exception("Native custom potion must clone successfully");
        var capturedPotion = (NeowEffectPotionSnapshot)typeof(ReflectionNeowEffectSnapshotAdapter)
            .GetMethod("ConvertPotionModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [nativePotion, 0, "potion-prefix-clone"])!;
        if (capturedPotion.MerchantCloneRuleExact || capturedPotion.Rarity != EffectPotionRarity.Rare)
            throw new Exception("Normal custom clone must be captured as unverified, not impossible");
        var capturedPool = effects.MerchantColorlessOrderedPool ?? effects.ColorlessRewardPool!;
        // The compact shared fixture has only one rare. Two real candidate
        // positions are needed to distinguish a wrong offered identity.
        var pool = capturedPool.Concat([capturedPool.First(c => c.Rarity == EffectCardRarity.Rare) with {
            CardKey = new("CARD", "PREFIX_OTHER_RARE"), InstanceId = "prefix-other-rare", PoolOrder = capturedPool.Count
        }]).ToArray();
        var stock = Enum.GetValues<EffectPotionRarity>()
            .Where(r => r is EffectPotionRarity.Common or EffectPotionRarity.Uncommon or EffectPotionRarity.Rare)
            .SelectMany(r => Enumerable.Range(0, 3).Select(i => new NeowEffectPotionSnapshot(
                new("POTION", r + "_PREFIX_" + i), (int)r * 3 + i, r)
                { MerchantCloneRuleExact = r != EffectPotionRarity.Rare || capturedPotion.MerchantCloneRuleExact })).ToArray();
        var inputAuthority = source.WithEffectAuthority(effects with {
            MerchantColorlessOrderedPool = pool, MerchantColorlessOrderedPoolExact = true,
            MerchantColorlessPoolIdentityHooksExact = true, MerchantColorlessRarityIdentityHooksExact = true,
            MerchantInitialInventoryShapeExact = false, MerchantPotionStock = stock
        });
        var context = SearchContextFactory.From(source.ProfileId, source.Character.CharacterKey, source.Ascension,
            inputAuthority, new("0.113.1", "0.113.1", "beta", "fixture", true, ""), AncientOptionConditionProfile.BroadDefault);
        var coveredPrefixes = new HashSet<int>();
        for (ulong ordinal = 0; ordinal < 128; ordinal++)
        {
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            var root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
            var projection = Beta111NormalMerchantColorlessSequenceProjector.Project(root.RootHash,
                Beta111MerchantColorlessAuthority.From(inputAuthority));
            if (projection.Merchants.Count is < 1 or >= 5) continue;
            int prefix = projection.Merchants.Count;
            if (!coveredPrefixes.Add(prefix)) continue;
            foreach (var slot in new[] { MerchantColorlessSlot.Uncommon, MerchantColorlessSlot.Rare })
            {
                var known = projection.Merchants.Select(m => slot == MerchantColorlessSlot.Uncommon
                    ? m.UncommonColorless : m.RareColorless).ToArray();
                SearchQuery Query(int count, CombatRewardSequenceOrderMode mode, params ModelKey?[] targets) =>
                    SearchQuery.Empty with { MerchantColorlessSequenceConditions = [new(count, mode, slot, targets)] };
                PreparedSearch Prepare(SearchQuery query) => SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context),
                    new(seed, 1, 2, 1)).Plan ?? throw new Exception("Colorless prefix compile");
                SearchDisposition Evaluate(SearchQuery query) => ProductionExactSearchEvaluator.Evaluate(Prepare(query).Model, root).Evaluation.Disposition;
                for (int count = prefix + 1; count <= 5; count++)
                {
                    var targets = new ModelKey?[count]; targets[0] = known[0];
                    var ordered = Query(count, CombatRewardSequenceOrderMode.Ordered, targets);
                    if (Evaluate(ordered) != SearchDisposition.Match ||
                        Evaluate(Query(count, CombatRewardSequenceOrderMode.Unordered, targets)) != SearchDisposition.Match)
                        throw new Exception("Known colorless prefix lost to unrelated later shop");
                    targets = new ModelKey?[count]; targets[^1] = known[0];
                    if (Evaluate(Query(count, CombatRewardSequenceOrderMode.Ordered, targets)) != SearchDisposition.Unknown)
                        throw new Exception("Specified later shop requires its observation");
                    var mismatch = pool.First(c => c.Rarity == (slot == MerchantColorlessSlot.Uncommon
                        ? EffectCardRarity.Uncommon : EffectCardRarity.Rare) && c.CardKey != known[0]).CardKey;
                    targets[0] = mismatch;
                    if (Evaluate(Query(count, CombatRewardSequenceOrderMode.Ordered, targets)) != SearchDisposition.NoMatch)
                        throw new Exception("Known ordered mismatch must reject despite unknown suffix");
                    var repeated = Enumerable.Repeat<ModelKey?>(mismatch, count).ToArray();
                    if (Evaluate(Query(count, CombatRewardSequenceOrderMode.Unordered, repeated)) != SearchDisposition.NoMatch)
                        throw new Exception("Unknown shops cannot fill more targets than remaining positions");
                    using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    Scan().GetAwaiter().GetResult();
                    async Task Scan()
                    {
                        var options = SearchPresetStore.CreateJsonOptions();
                        var restored = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(ordered, options), options)!;
                        if (SearchContinuationIdentity.For(Prepare(restored)) != SearchContinuationIdentity.For(Prepare(ordered)))
                            throw new Exception("Colorless partial-window preset changed its continuation identity");
                        await using var session = FamilyExecutionCoordinator.Start(Prepare(restored), gpuAvailable: false);
                        await session.Completion.WaitAsync(completion.Token).ConfigureAwait(false);
                        if (!session.TryReadCandidate(out var hit) || hit!.Seed != seed || hit.ValidationDisposition != SearchDisposition.Match)
                            throw new Exception("Colorless prefix full search lost known positive");
                    }
                }
            }
            Console.WriteLine($"COLORLESS_PREFIX_PASS seed={seed};knownShops={prefix};bothSlots=true;orderedAndUnordered=true;fullSearch=true");
            if (coveredPrefixes.Count == 4) return;
        }
        throw new Exception("Colorless prefix fixture needs each successful prefix of one through four shops");
    }

    private static void CheckRelicPoolCapture()
    {
        var method = typeof(RolltheSpire2.Infrastructure.Snapshots.ReflectionNeowEffectSnapshotAdapter)
            .GetMethod("CaptureBeta109WorldRelicPool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        void Check(object? collection, bool expectedExact, int? expectedRarities)
        {
            var observation = method.Invoke(null, [new ObservedPool(collection), new object(), "fixture"])!;
            var type = observation.GetType();
            bool exact = (bool)type.GetProperty("Exact")!.GetValue(observation)!;
            var rarities = (IReadOnlySet<string>?)type.GetProperty("ObservedRarities")!.GetValue(observation);
            if (exact != expectedExact || rarities?.Count != expectedRarities)
                throw new Exception("Missing, empty and unreadable relic captures need distinct authority");
        }
        Check(Array.Empty<object>(), true, 0);
        Check(null, false, null);
        Check(new object?[] { null }, false, null);
        Check(new[] { new { Rarity = "Common" } }, false, 1);
    }

    private static void Check113Bags(RuntimeContextAuthoritySnapshot authority, ulong root)
    {
        var world = authority.WorldAuthority!;
        var inputs = world.Beta109Generation!;
        RelicSequencePredictionResult Project(Beta109WorldGenerationSnapshot generation) =>
            RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, root, "bag-reference",
                world with { Beta109Generation = generation });
        var absent = inputs with
        {
            SharedRelicBuckets = inputs.SharedRelicBuckets.Where(b => !b.BucketId.EndsWith(":Shop")).ToArray(),
            PlayerRelicBuckets = inputs.PlayerRelicBuckets.Where(b => !b.BucketId.EndsWith(":Shop")).ToArray()
        };
        var actual = Project(absent);
        var native = new MegaCrit.Sts2.Core.Random.Rng(root, "up_front");
        foreach (var bucket in absent.SharedRelicBuckets.Concat(absent.PlayerRelicBuckets))
        {
            var ordered = bucket.OrderedRelics.ToList().UnstableShuffle(native);
            var lanes = bucket.BucketId.StartsWith("shared:") ? actual.TreasureRoomLanes : actual.Lanes;
            var observed = lanes.Single(l => bucket.BucketId.EndsWith(":" + l.RarityCode));
            if (!ordered.SequenceEqual(observed.FullEntries.Select(e => e.RelicKey))) throw new Exception("Native missing-bucket shuffle order");
        }
        if (actual.RngCallCount != native.ToSerializable().counter || actual.Lanes.Single(l => l.Kind == RelicSequenceKind.Shop).TotalCount != 0)
            throw new Exception("Missing Shop deque consumed a shuffle or gained stock");
        var common = actual.GetPlayerLane(RelicSequenceKind.Common)!.FullEntries[0].RelicKey;
        var detached = NormalMerchantRelicProjector.Project(root, authority.WithWorldAuthority(null), actual, 3);
        var attached = NormalMerchantRelicProjector.Project(root, authority.WithWorldAuthority(world with { Beta109Generation = absent }), actual, 3);
        if (!detached.Merchants.SequenceEqual(attached.Merchants) || detached.Merchants.Any(m => m.Left.RelicKey is null || m.Middle.RelicKey is null))
            throw new Exception("Replaying an already projected bag required the source catalog again");
        var absentAuthority = authority.WithWorldAuthority(world with { Beta109Generation = absent });
        var context = SearchContextFactory.From(absentAuthority.ProfileId, absentAuthority.Character.CharacterKey, absentAuthority.Ascension, absentAuthority,
            new("0.113.1", "0.113.1", "beta", "fixture", true, ""), AncientOptionConditionProfile.BroadDefault);
        var ordinaryQuery = SearchQuery.Empty with { RelicSequenceConstraints =
            [new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1, new([common], [], []))] };
        var preparation = SearchPreparationCompiler.Compile(SearchCompiler.Compile(ordinaryQuery, context), new("6Q8LL102Y4CF", 1, 1, 1));
        var prepared = preparation.Plan ?? throw new Exception(preparation.Issue);
        if (!RelicFamilyPlanCompiler.TryCompile(prepared.Model, out _, out var reason)) throw new Exception(reason);
        var empty = absent with { SharedRelicBuckets = [], PlayerRelicBuckets = [] };
        var zero = Project(empty);
        var emptyAuthorities = authority.WithWorldAuthority(world with { Beta109Generation = empty });
        if (zero.RngCallCount != 0 || zero.TreasureRoomLanes.Count != 3 ||
            NormalMerchantRelicProjector.Project(root, emptyAuthorities, zero, 1).Merchants[0].Left.RelicKey?.Entry != "CIRCLET" ||
            Project(empty with { RelicInitializationExact = false }).Status != SeedDomainEvaluationStatus.Unknown)
            throw new Exception("Known empty vs missing bag source");
        var before = Project(inputs);
        var shop = before.Lanes.Single(l => l.Kind == RelicSequenceKind.Shop).FullEntries;
        var partial = inputs with { RelicShopEligibilityAuthorityExact = false,
            PlayerRelicBuckets = inputs.PlayerRelicBuckets.Select(b => b with { OrderedEntries = b.OrderedEntries.Select(e =>
                e.RelicKey == shop[1].RelicKey ? e with { ShopEligibilityExact = false } : e).ToArray() }).ToArray() };
        var ordinaryTarget = Project(partial).GetPlayerLane(RelicSequenceKind.Common)!.Entries[0].RelicKey;
        var firstOrdinary = ExactSearchEvaluationProjection.Empty with { RelicSequenceConditions =
            [new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1, new([ordinaryTarget], [], []))] };
        var partialWorld = world with { Beta109Generation = partial };
        var ordinaryReference = new RelicReferencePlan(firstOrdinary, partialWorld, Beta111Profile.Instance);
        if (!ordinaryReference.Reference(root) || !ordinaryReference.Matches(root, code => throw new Exception(code)))
            throw new Exception("Unrelated Shop eligibility blocked the ordinary reference filter");
        var absentReference = new RelicReferencePlan(firstOrdinary with { RelicSequenceConditions =
            [new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1, new([common], [], []))] },
            world with { Beta109Generation = absent }, Beta111Profile.Instance);
        if (!absentReference.Reference(root)) throw new Exception("Absent Shop bucket blocked Common");
        var prefix = Project(partial).Lanes.Single(l => l.Kind == RelicSequenceKind.Shop);
        if (prefix.Precision != PredictionPrecision.Partial || prefix.FullEntries.Count != 1 || prefix.Entries[0].RelicKey != shop[0].RelicKey)
            throw new Exception("Uncertain eligibility erased or extended proven shop prefix");
        var ordinaryOnly = inputs with { RelicShopEligibilityAuthorityExact = false,
            PlayerRelicBuckets = inputs.PlayerRelicBuckets.Select(b => b.BucketId.EndsWith(":Common") ?
                b with { OrderedEntries = b.OrderedEntries.Select(e => e with { ShopEligibilityExact = false }).ToArray() } : b).ToArray() };
        if (!Project(ordinaryOnly).Lanes.Single(l => l.Kind == RelicSequenceKind.Shop).FullEntries.SequenceEqual(shop))
            throw new Exception("Ordinary shelf eligibility changed stocked Shop deque");
    }

    private static void Check113PotionStock()
    {
        var stock = new List<NeowEffectPotionSnapshot>();
        foreach (var rarity in new[] { EffectPotionRarity.Common, EffectPotionRarity.Uncommon, EffectPotionRarity.Rare })
            for (int i = 0; i < 3; i++)
                stock.Add(new(new("POTION", rarity + "_" + i), stock.Count, rarity) { MerchantCloneRuleExact = true });
        stock.Add(new(new("POTION", "CUSTOM_COMMON"), stock.Count, EffectPotionRarity.Common, SourceModId: "test")
            { MerchantCloneRuleExact = true });
        if (!Beta111NormalMerchantShopsContinuation.HasUnconditionalPotionStock(stock))
            throw new Exception("Inherited stock clone with custom potion should preserve fixed schedule");
        var authority = new Beta111MerchantColorlessAuthority(
            [new("CARD", "U1"), new("CARD", "U2")], [new("CARD", "R1"), new("CARD", "R2")],
            true, true, true, false, RuntimeProfileId.Beta111, 0, 1, "controlled-potion-stock")
            { UsesModern113 = true, PotionStock = stock };
        var sparse = stock.Where(p => p.Rarity != EffectPotionRarity.Rare).ToArray();
        bool sawLaterFailure = false;
        for (ulong root = 0; root < 32; root++)
        {
            var native = new MegaCrit.Sts2.Core.Random.Rng(root, "shops");
            var five = Beta111NormalMerchantColorlessSequenceProjector.Project(root, authority, 5);
            int firstIncomplete = 6;
            for (int ordinal = 1; ordinal <= 5; ordinal++)
            {
                for (int i = 0; i < 5; i++) native.NextUnsignedLong();
                for (int slot = 0; slot < 3; slot++)
                {
                    if (native.NextFloat() <= .1f) firstIncomplete = Math.Min(firstIncomplete, ordinal);
                    native.NextUnsignedLong();
                }
                // Three potion prices and five colored cards. Then U/upgrade/price, R/upgrade/price, sale/price.
                for (int i = 0; i < 23; i++) native.NextUnsignedLong();
                var uncommon = authority.UncommonPool[native.NextInt(authority.UncommonPool.Count)];
                native.NextUnsignedLong(); native.NextUnsignedLong();
                var rare = authority.RarePool[native.NextInt(authority.RarePool.Count)];
                for (int i = 0; i < 4; i++) native.NextUnsignedLong();
                var shop = five.Merchants[ordinal - 1];
                if (shop.UncommonColorless != uncommon || shop.RareColorless != rare ||
                    shop.OutgoingShopsState.CallCount != native.ToSerializable().counter)
                    throw new Exception("Native colorless identity or outgoing Shops stream disagrees");
            }
            var prefix = Beta111NormalMerchantColorlessSequenceProjector.Project(root, authority with { PotionStock = sparse }, 5);
            if (prefix.Merchants.Count != firstIncomplete - 1) throw new Exception("Seed-dependent potion stock prefix");
            sawLaterFailure |= firstIncomplete is >= 2 and <= 5;
        }
        if (!sawLaterFailure) throw new Exception("Fixture must cover successful first shop and failed later continuation");
    }
}
