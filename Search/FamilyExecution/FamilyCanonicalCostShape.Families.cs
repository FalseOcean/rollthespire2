using System.Numerics;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class FamilyCanonicalCostShape
{
    // Structure only. ExpectedWork/reach/warp activity and observed population
    // are model inputs, not exact-key fields. No fallback to query fingerprints.
    private static string? TryDescribeFamily(ExactSearchExecutionRequest request, IFamilyInvocation owner, IFamilyInvocation actual)
    {
        object? data = owner switch {
            NeowFamily neow when neow.Coverage.Count == 1 => NeowStructure(neow.PricingReplayPlan,
                actual.ConditionPerformance.UsesGpu ? neow.GpuPlan : null),
            MerchantShopColorlessFamily => ShopStructure(request),
            RelicFamily relic => RelicStructure(relic.PricingPlan),
            // Distinct owner identity: no ordinary-R or old conditional-call
            // measurement is silently admitted as a Capsule invocation.
            CapsuleRelicFamily capsule => new {
                Owner = "CapsuleRelicFamily",
                Arrival = NeowStructure(capsule.PricingReplayPlan, null),
                Sequence = capsule.GpuPlan is { } full ? RelicStructure(full.SequencePlan) : null,
                BagLengths = capsule.PricingPool.BucketLengths,
                Targets = request.Evaluation.StructuredNeowEffects.Where(NeowReplayPlan.IsCapsule)
                    .Select(c => new { c.SourceRelicKey, c.Scope, Count = c.OutputKeys.Count }).ToArray()
            },
            CombatRewardFamily combat => CombatStructure(combat, actual.ConditionPerformance.UsesGpu),
            WorldFamily world => WorldStructure(world, actual.ConditionPerformance.UsesGpu),
            AncientOptionFamily ancient => AncientStructure(request, ancient.Plan),
            TransformationAggregateFamily transform => TransformStructure(transform, actual.ConditionPerformance.UsesGpu),
            _ => null
        };
        if (data is null) return null;
        // Concrete W and Gold-sensitive C bodies version their own projections.
        string version = owner is WorldFamily worldOwner
            ? WorldUsesLocalEventCostRoles(worldOwner.Replay.Plan) ? "20261021.v3" : "20261021.v2"
            : owner is CombatRewardFamily combatOwner ? combatOwner.Replay.UsesQueryLiteralPartyOpening
                ? "20261027.v4" : CombatCostStructureVersion(combatOwner.Replay.Plan)
            : "20261021.v1";
        string shape = owner.FamilyId + ".CanonicalStructure." + version + ":" + JsonSerializer.Serialize(data);
        if (owner is not NeowFamily { Coverage.Count: 1 } singleton ||
            singleton.PricingReplayPlan.AuthoredUpgrades is not { } authored) return shape;
        // This is an operation-count vector indexed by Small/Large source and
        // prior Neow opcode, not target dense IDs. -1 is the actual conservative
        // keep branch; nonnegative entries count Niche.NextInt(2) advances.
        // A conditional suffix preserves every non-authored legacy shape byte.
        if (authored.Advances.Length != 2 * NeowAuthoredUpgradeContinuation.PriorCount ||
            authored.Advances.Any(advance => advance < -1)) return null;
        return shape + ";AuthoredUpgradeContinuation.v1:" + JsonSerializer.Serialize(new {
            NeowAuthoredUpgradeContinuation.PriorCount,
            SmallSourceOpcode = Beta110FastRelicCatalog.SmallCapsule,
            LargeSourceOpcode = Beta110FastRelicCatalog.LargeCapsule,
            NoPriorOpcode = byte.MaxValue,
            authored.Advances,
            UpperBound = singleton.PricingReplayPlan.CapsuleUpgradeUpperBound
        });
    }

    // IDs below are alpha-renamed operand roles, not catalog IDs or pool ranks.
    // Equal targets keep one role across predicates; different identities with
    // the same rarity/membership/equality geometry produce the same descriptor.
    private sealed class DenseRoles
    {
        private readonly Dictionary<ushort, int> _roles = [];
        internal int Role(ushort id)
        {
            if (id == ushort.MaxValue) return -1;
            if (!_roles.TryGetValue(id, out int role)) _roles.Add(id, role = _roles.Count);
            return role;
        }
        internal int[] Pattern(IEnumerable<ushort> ids) => ids.Select(Role).ToArray();
        internal object Pool(IEnumerable<ushort> source)
        {
            var entries = source.ToArray();
            return new { Count = entries.Length,
                Multiplicities = entries.GroupBy(x => x).Select(g => g.Count()).Order().ToArray(),
                TargetMembership = _roles.OrderBy(p => p.Value).Select(p => entries.Count(id => id == p.Key)).ToArray() };
        }
        internal object PoolForLocalTargets(IEnumerable<ushort> source, IEnumerable<ushort> localTargets)
        {
            var entries = source.ToArray();
            return new { Count = entries.Length,
                Multiplicities = entries.GroupBy(x => x).Select(g => g.Count()).Order().ToArray(),
                // Global role numbers preserve target equality across Acts and
                // predicate kinds. Local absent targets remain explicit zeros.
                TargetMembership = localTargets.Where(id => id != ushort.MaxValue).Distinct()
                    .Select(id => new { Id = id, Role = Role(id) }).OrderBy(p => p.Role)
                    .Select(p => new { p.Role, Count = entries.Count(id => id == p.Id) }).ToArray() };
        }
        internal object Rarities(Beta110FastEffectCatalog catalog) => _roles.OrderBy(p => p.Value)
            .Select(p => p.Key < catalog.CardRarityByDenseId.Length ? (int)catalog.CardRarityByDenseId[p.Key] : -1).ToArray();
    }
    private sealed class KeyRoles
    {
        private readonly Dictionary<ModelKey, int> _roles = new(ModelKeyComparer.Instance);
        internal int Role(ModelKey? key)
        {
            if (!key.HasValue) return -1;
            if (!_roles.TryGetValue(key.Value, out int role)) _roles.Add(key.Value, role = _roles.Count);
            return role;
        }
        internal object Membership(IReadOnlyList<ModelKey> pool) => new {
            Count = pool.Count, Multiplicities = pool.GroupBy(k => k).Select(g => g.Count()).Order().ToArray(),
            TargetMembership = _roles.OrderBy(p => p.Value).Select(p => pool.Count(k => k == p.Key)).ToArray() };
    }
    private static object MaskStructure(ulong any, ulong all, ulong ban) => new {
        Any = BitOperations.PopCount(any), All = BitOperations.PopCount(all), Ban = BitOperations.PopCount(ban),
        AnyAll = BitOperations.PopCount(any & all), AnyBan = BitOperations.PopCount(any & ban), AllBan = BitOperations.PopCount(all & ban) };

    private static object NeowStructure(NeowReplayPlan plan, NeowFamilyGpuPlan? gpu)
    {
        var roles = new DenseRoles();
        var structured = plan.StructuredConditions.Select(c => new {
            // SourceRelicId is a finite dispatch opcode (K/Leafy/etc.), never an
            // output target. It selects genuinely different replay algorithms.
            SourceOpcode = c.SourceRelicId, c.Kind, c.TargetCount, c.AllowDuplicateOutputs,
            c.OrderedKaleidoscope, Targets = roles.Pattern(Enumerable.Range(0, c.TargetCount).Select(c.TargetAt)),
            OrderedTargets = roles.Pattern(new[] { c.KaleidoscopeFirstTarget, c.KaleidoscopeSecondTarget }) }).ToArray();
        int[] finalCurse = roles.Pattern(plan.RequiredFinalCurseIds), bannedCurse = roles.Pattern(plan.BannedFinalCurseIds);
        var catalog = plan.Authority.EffectCatalog;
        return new {
            plan.EnabledDomains, plan.Bones, plan.RequireBones, SelectedSourceOpcode = plan.Selected,
            FirstSourceOpcode = plan.First, SecondSourceOpcode = plan.Second,
            plan.LeafyBonesInitialTransformInvariant, plan.SharedNicheDraws, plan.SharedPotionDraws,
            SharedArrivals = plan.SharedArrivals.Select(a => new { a.Niche, a.Potions }).ToArray(),
            plan.CapsuleUpgradeUpperBound, HasAuthoredUpgrades = plan.AuthoredUpgrades is not null,
            Top = MaskStructure(plan.TopAny, plan.TopAll, plan.TopBan),
            BonesGate = MaskStructure(plan.BonesAny, plan.BonesAll, plan.BonesBan),
            CurseCandidates = plan.Authority.EligibleCurseRelicIds.Length,
            BonesCandidates = plan.Authority.BonesEligibleRelicIds.Length,
            Structured = structured, FinalCurse = finalCurse, BannedCurse = bannedCurse,
            ExactOnlyRemainder = plan.ExactOnly.Length > 0, TargetRarities = roles.Rarities(catalog),
            Character = CardPools(roles, catalog.CharacterRewardPool), Colorless = CardPools(roles, catalog.ColorlessRewardPool),
            OtherCharacters = catalog.OtherCharacterPools.Select(p => CardPools(roles, p)).ToArray(),
            Multiplayer = CardPools(roles, catalog.MultiplayerRewardPool),
            Strike = roles.Pool(catalog.LeafyStrikeTransformPool), Defend = roles.Pool(catalog.LeafyDefendTransformPool),
            NewLeaf = roles.Pool(catalog.NewLeafTransformPool), Potions = PotionPools(roles, catalog.PotionPool),
            Gpu = gpu is null ? null : new { gpu.DirectNestedMode, gpu.UsesStagedDense, gpu.UsesLeafyPreGate,
                gpu.HasAuthoredUpgrades, JointTransform = gpu.JointTransformMetaOffset >= 0,
                ResolvedFirstDrawAccepts = gpu.Meta.Length > 69 ? BitOperations.PopCount(gpu.Meta[68]) : 0,
                ResolvedFirstDrawRejects = gpu.Meta.Length > 69 ? BitOperations.PopCount(gpu.Meta[69]) : 0 }
        };
    }
    private static object CardPools(DenseRoles roles, Beta110FastCardPool pool) => new {
        Common = roles.Pool(pool.Common), Uncommon = roles.Pool(pool.Uncommon), Rare = roles.Pool(pool.Rare) };
    private static object PotionPools(DenseRoles roles, Beta110FastPotionPool pool) => new {
        Common = roles.Pool(pool.Common), Uncommon = roles.Pool(pool.Uncommon), Rare = roles.Pool(pool.Rare), All = roles.Pool(pool.AllAllowed) };

    private static object? ShopStructure(ExactSearchExecutionRequest request)
    {
        var authority = Beta111MerchantColorlessAuthority.From(request.Authority);
        if (!authority.HasExactV1Inputs) return null;
        var roles = new KeyRoles();
        var fixedSlots = request.Evaluation.MerchantColorlessConditions.OrderBy(c => c.MerchantOrdinal).ThenBy(c => c.Slot)
            .Select(c => new { c.MerchantOrdinal, c.Slot, Target = roles.Role(c.TargetCardKey) }).ToArray();
        var sequences = request.Evaluation.MerchantColorlessSequenceConditions.Select(c => new {
            c.Count, c.Slot, c.OrderMode, Targets = c.Slots.Select(k => roles.Role(k)).ToArray() }).ToArray();
        return new { Fixed = fixedSlots, Sequences = sequences,
            Uncommon = roles.Membership(authority.UncommonPool), Rare = roles.Membership(authority.RarePool),
            CallsPerMerchant = Beta111NormalMerchantShopsContinuation.CallsPerMerchant,
            UncommonCall = Beta111NormalMerchantShopsContinuation.UncommonCallWithinMerchant,
            RareCall = Beta111NormalMerchantShopsContinuation.RareCallWithinMerchant };
    }

    private static object RelicStructure(RelicFamilyPlan plan)
    {
        var roles = new DenseRoles();
        // Non-Shop predicate indexes are already lane-local alpha-renamed tracked
        // operands. Initial absolute ranks are deliberately excluded.
        ushort[] Slice(int offset, int count) => plan.PredicateTargetIndexes.Skip(offset).Take(count).ToArray();
        var predicates = plan.Predicates.Select(p => new { p.Lane, p.RangeMode, p.RangeValue,
            Any = p.Lane == 3 ? roles.Pattern(Slice(p.AnyOffset, p.AnyCount)) : Slice(p.AnyOffset, p.AnyCount).Select(i => (int)i).ToArray(),
            All = p.Lane == 3 ? roles.Pattern(Slice(p.AllOffset, p.AllCount)) : Slice(p.AllOffset, p.AllCount).Select(i => (int)i).ToArray(),
            Ban = p.Lane == 3 ? roles.Pattern(Slice(p.BanOffset, p.BanCount)) : Slice(p.BanOffset, p.BanCount).Select(i => (int)i).ToArray() }).ToArray();
        var shops = plan.ShopPredicates.Select(p => new { p.Count, p.OrderMode, Targets = roles.Pattern(p.TargetIds) }).ToArray();
        return new { plan.LastRequiredBucket, plan.AlwaysReject, plan.LocalStateCapacity,
            plan.PositiveDepthByLane, plan.ExclusionDepthByLane, plan.TrackedCountsByLane,
            Predicates = predicates, Shops = shops, Pool = RelicPoolStructure(plan.Pool),
            TargetBucketMembership = plan.Pool.BucketOffsets.Select((offset, index) => roles.Pool(
                plan.Pool.DenseRelicIds.Skip(offset).Take(plan.Pool.BucketLengths[index]))).ToArray() };
    }
    private static object RelicPoolStructure(RelicFamilyPool pool) => new {
        pool.BucketLengths, pool.BucketScopes, pool.BucketKinds, pool.DrawDirections, pool.MaxBucketLength };

    private static object CombatStructure(CombatRewardFamily family, bool usesGpu)
    {
        var replay = family.Replay; var plan = replay.Plan; var roles = new DenseRoles();
        var predicates = plan.Predicates.Select(p => new { p.BattleOrdinal,
            CardAny = roles.Pattern(p.CardAny), CardAll = roles.Pattern(p.CardAll), CardBan = roles.Pattern(p.CardBan),
            p.PotionRequirement, PotionAny = roles.Pattern(p.PotionAny), PotionAll = roles.Pattern(p.PotionAll), PotionBan = roles.Pattern(p.PotionBan),
            p.HasMinimumGold, p.HasMaximumGold }).ToArray();
        int[] cards = roles.Pattern(plan.CardAssignmentTargets), potions = roles.Pattern(plan.PotionAssignmentTargets);
        var structure = new { replay.RouteCount, replay.PrecedingNicheDraws, replay.CapsuleNicheAdvances,
            replay.KaleidoscopeCountOnly,
            // Preserve the existing solo measurement shape bytes. These are
            // retired measurement-key fields, not runtime replay state.
            ReplayActualBonesPair = false, DynamicCapsuleNicheUnknown = false, CapsuleHeldReplay = false,
            replay.ConservativelyKeeps, plan.MaximumBattleOrdinal, plan.RoutePolicy,
            Opening = new { plan.OpeningConsumption.ReplayBonesOffer, SourceOpcodes = plan.OpeningConsumption.OrderedRelicIds },
            Influence = new { plan.ExplicitContext.InfluenceFlags, plan.ExplicitContext.AdditionalCardRewardCount,
                FixedGold = plan.ExplicitContext.FixedGoldAmount != 0 },
            Predicates = predicates, plan.CardAssignmentWindow, CardTargets = cards,
            plan.PotionAssignmentWindow, plan.PotionAssignmentRequirements, PotionTargets = potions,
            TargetRarities = roles.Rarities(replay.Catalog), CardPools = CardPools(roles, replay.Catalog.CombatRewardCardPool),
            PowerPools = CardPools(roles, replay.Catalog.CombatRewardPowerPool),
            Potions = PotionPools(roles, replay.Catalog.CombatRewardPotionPool),
            Gpu = !usesGpu || family.GpuPlan is not { } gpu ? null : new { gpu.UsesHotLoop, gpu.Routes, gpu.BagCapacity,
                gpu.UsesTerminalCardGate } };
        if (replay.UsesQueryLiteralPartyOpening)
            return CombatGoldCostStructure(new { SourceWork = structure, replay.UsesQueryLiteralPartyOpening,
                PredicateWindows = plan.Predicates.Select(p => p.AnyBattleWindow).ToArray() }, plan);
        if (CombatCostStructureVersion(plan) == "20261026.v3")
            return CombatGoldCostStructure(new { SourceWork = structure, plan.NeedsCompleteMatchingReference,
                PredicateWindows = plan.Predicates.Select(p => p.AnyBattleWindow).ToArray() }, plan);
        return CombatGoldCostStructure(structure, plan);
    }

    internal static string CombatCostStructureVersion(Beta110CombatRewardFastPlan plan) =>
        plan.NeedsCompleteMatchingReference || plan.Predicates.Any(p => p.IsAnyBattle && p.AnyBattleWindow > 0 && p.AnyBattleWindow != plan.MaximumBattleOrdinal)
            ? "20261026.v3" : plan.Predicates.Any(p => p.HasGoldPredicate) || plan.ExplicitContext.FixedGoldAmount != 0
            ? "20261021.v2" : "20261021.v1";

    // The generic GPU tests Gold before the other fields of an ordered predicate;
    // rejection can skip subsequent battles. Keep the actual operands, preserving
    // the v1 bytes for bodies that have no Gold predicate or fixed-Gold impact.
    internal static object CombatGoldCostStructure(object sourceWork, Beta110CombatRewardFastPlan plan)
    {
        if (CombatCostStructureVersion(plan) == "20261021.v1") return sourceWork;
        return new { SourceWork = sourceWork,
            Gold = plan.Predicates.Select(p => new {
                MinimumGold = p.HasMinimumGold ? (int?)p.MinimumGold : null,
                MaximumGold = p.HasMaximumGold ? (int?)p.MaximumGold : null }).ToArray(),
            plan.ExplicitContext.FixedGoldAmount };
    }

    private static object WorldStructure(WorldFamily family, bool usesGpu)
    {
        return WorldStructure(family.Replay, usesGpu ? family.PricingGpuPlan : null);
    }

    private static bool WorldUsesLocalEventCostRoles(Beta110WorldFastPlan plan) =>
        !plan.RequiresSecondBoss && plan.Acts.Any(a => a.EventPredicates.Length > 0) &&
        plan.Acts.All(a => !a.HasBossPredicate && !a.HasAncientPredicate &&
            a.FamilyBossBranches.Length == 0 && a.AncientOptionPlans.Length == 0 &&
            a.AncientBranchOptionPredicates.Length == 0);

    // Pure cost projection seam, also used by the focused source contract checks.
    internal static object WorldStructure(WorldFamilyReplay replay, WorldFamilyGpuPlan? gpu)
    {
        var p = replay.Plan;
        var roles = new DenseRoles();
        bool localEvents = WorldUsesLocalEventCostRoles(p);
        var eventRoles = p.Acts.Select(_ => localEvents ? new DenseRoles() : roles).ToArray();
        object Sets(IEnumerable<WorldFastDenseSetPredicate> rows, DenseRoles? scope = null) => rows.Select(s => new {
            Any = (scope ?? roles).Pattern(s.Any), All = (scope ?? roles).Pattern(s.All),
            Ban = (scope ?? roles).Pattern(s.Ban), s.AlwaysReject }).ToArray();
        static IEnumerable<ushort> Targets(IEnumerable<WorldFastDenseSetPredicate> rows) =>
            rows.SelectMany(s => s.Any.Concat(s.All).Concat(s.Ban));
        var acts = p.Acts.Select((a, index) => new { a.Act, a.FamilyVariantAllowed,
                a.WeakEncounterSlots, a.RegularEncounterSlots, a.EliteEncounterSlots,
                Weak = a.WeakEncounters.Length, Regular = a.RegularEncounters.Length, Elite = a.EliteEncounters.Length,
                Conflicts = a.WeakEncounters.Concat(a.RegularEncounters).Concat(a.EliteEncounters).Select(e => e.ConflictCount).ToArray(),
                a.EligibleEventCount, EventPool = a.EventCandidates.Length,
                Bosses = a.Bosses.Length, Ancients = a.Ancients.Length,
                Boss = Sets(a.BossActPredicates), Boss1 = Sets(a.BossOrdinalOnePredicates), Boss2 = Sets(a.BossOrdinalTwoPredicates),
                Ancient = Sets(a.AncientIdentityPredicates), AncientBranches = Sets(a.AncientBranchIdentityPredicates),
                Branches = a.FamilyBossBranches.Select(b => new { First = Sets(new[] { b.First }), Second = Sets(new[] { b.Second }) }).ToArray(),
                Events = a.EventPredicates.Select(e => new { e.RangeMode, e.RangeValue, e.SourceFilter,
                    Keys = Sets(new[] { e.Keys }, eventRoles[index]) }).ToArray() }).ToArray();
        return new { replay.PricingSignature, replay.BucketLengths, p.MaxRequiredAct, p.RequiredFarthestStage, p.RequiresSecondBoss,
            Acts = acts, PoolMembership = p.Acts.Select((a, index) => new {
                // Beta110WorldFastStage.EvaluateForFamily/EvaluateEventPredicates
                // read these same-Act/same-kind operands. WorldFamilyGpuPlan
                // and WorldFamilyGpuPacking pack the same local predicate sets.
                // Foreign coordinates are not read; size/multiplicity remains.
                // Pure-event bodies compare only this Act's event IDs. Their
                // cross-Act spelling/equality never enters source work. Retain
                // ordered within-Act equality/membership; this is a statistical
                // cost class, not seedwise result or survivor equivalence.
                // Mixed/identity bodies retain their exact v2 projection.
                Events = eventRoles[index].PoolForLocalTargets(a.EventCandidates.Select(e => e.EventId), Targets(a.EventPredicates.Select(e => e.Keys))),
                Bosses = roles.PoolForLocalTargets(a.Bosses, Targets(a.BossActPredicates.Concat(a.BossOrdinalOnePredicates)
                    .Concat(a.BossOrdinalTwoPredicates).Concat(a.FamilyBossBranches.SelectMany(b => new[] { b.First, b.Second })))),
                Ancients = roles.PoolForLocalTargets(a.Ancients, Targets(a.AncientIdentityPredicates.Concat(a.AncientBranchIdentityPredicates))) }).ToArray(),
            Gpu = gpu is null ? null : new { gpu.VariantOnly, gpu.ScratchWords, gpu.Capacity, gpu.GroupingK } };
    }

    private static object? AncientStructure(ExactSearchExecutionRequest request, AncientOptionFamilyPlan plan)
    {
        if (plan.OptionPlans.Count == 0) return null;
        // Legacy hypothetical-row unions with repeated/overlapping bindings need
        // joint OR-row geometry beyond counts. Do not alias them to a modern row.
        var branches = request.Evaluation.AncientBranchConditions;
        if (request.Evaluation.AncientOptionFilters.Count > 0 || request.Evaluation.AncientSeaGlassTargetFilters.Count > 0 ||
            branches.GroupBy(b => (b.Act, b.AncientKey)).Any(g => g.Count() > 1) ||
            branches.Any(b => !plan.UsesNumericalAct(b.Act))) return null;
        var roles = new DenseRoles();
        var rows = branches.Select(b => new { b.Act,
            Binding = roles.Role(plan.PricingDenseId(b.AncientKey)),
            Options = roles.Pattern(b.OptionAny.Select(plan.PricingDenseId)),
            SeaGlass = roles.Pattern(b.SeaGlassTargetAny.Select(plan.PricingDenseId)),
            Unconstrained = b.OptionAny.Count == 0 && b.SeaGlassTargetAny.Count == 0 }).ToArray();
        // Register every binding/visibility role before describing any pool so
        // each row has the same target-coordinate system, with no dense IDs.
        foreach (var p in plan.OptionPlans) { roles.Role(p.AncientId); roles.Role(p.SeaGlassOptionId); }
        return new { plan.PricingSignature, plan.ScratchWords, plan.Capacity,
            Rows = rows,
            Generators = plan.OptionPlans.Select(p => new { p.Generator, p.PlayerSlot, p.IsShared, p.MaxScratchCount,
                Binding = roles.Role(p.AncientId), SeaGlassOption = roles.Role(p.SeaGlassOptionId),
                OtherCharacters = roles.Pool(p.OtherUnlockedCharacters), p.SeaGlassTargetAuthorityExact,
                Pools = p.Pools.Select(pool => new { pool.Role, pool.SourceOrdinal, Shape = roles.Pool(pool.Values) }).ToArray() }).ToArray() };
    }

    private static object? TransformStructure(TransformationAggregateFamily family, bool usesGpu)
    {
        var p = family.Plan; var c = p.Condition;
        if (!p.Closed) return null;
        return new { c.Opening, c.PickupOrder, c.Predicate, c.MinimumRareCount, c.OpportunityCount,
            c.MorphicGrove, c.AromaOfChaos, c.WhisperingHollow, c.Symbiote, c.TrialNondescript,
            Targets = c.TargetMultiset.Count, Multiplicities = c.TargetMultiset.GroupBy(k => k).Select(g => g.Count()).Order().ToArray(),
            p.UsesStreaming, Gpu = !usesGpu ? null : new { family.GpuPlan.BonesOptimized, family.GpuPlan.FullTarget },
            Neow = p.Neow is null ? null : NeowStructure(p.Neow, null),
            Draws = p.DrawGroups.Select((g, index) => new { Index = index, g.Prefix,
                // The group index is the authored stream role; the hash is a
                // constant stream name, but no hash value is persisted as a key.
                Pools = g.Pools.Select(pool => new { Count = pool.Length,
                    Membership = pool.GroupBy(id => (Rare: p.IsRare(id), Bits: p.TargetBits(id)))
                        .OrderBy(row => row.Key.Rare).ThenBy(row => row.Key.Bits)
                        .Select(row => new { row.Key.Rare, row.Key.Bits, Count = row.Count() }).ToArray() }).ToArray() }).ToArray() };
    }
}
