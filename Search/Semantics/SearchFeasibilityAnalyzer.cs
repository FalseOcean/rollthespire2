using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

/// <summary>
/// Narrow authoritative preflight proof layer. It may reject a query only when
/// canonical Search semantics or immutable runtime authority proves that the
/// satisfying outcome set is empty. Missing/partial authority returns Possible
/// (unknown feasibility), never Impossible.
///
/// Probability consumes this proof; this analyzer never consumes Probability.
/// </summary>
internal static class SearchFeasibilityAnalyzer
{
    public static SearchFeasibilityResult Analyze(CompiledSearch compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);

        if (compiled.Status == QueryNormalizationStatus.Impossible)
        {
            return SearchFeasibilityResult.Impossible(NormalizationProof(compiled.Normalization.Diagnostics));
        }

        var query = compiled.NormalizedQuery;
        if (compiled.Context.Authority.PlayersCount == 1 && !query.LegacyNeow.NeowRelics.IsEmpty)
        {
            bool accepted = false;
            var filter = query.LegacyNeow.NeowRelics;
            var selected = query.OpeningRoute?.RouteRelicKey;
            bool exact = Core.Neow.ModernNeowIdentityPredictor.TryVisitOfferSpace(compiled.Context.Authority, (a,b,c,_) =>
            {
                bool Has(ModelKey key) => key == a || key == b || key == c;
                accepted |= (!selected.HasValue || Has(selected.Value)) &&
                    (filter.Any.Count == 0 || filter.Any.Any(Has)) && filter.All.All(Has) && !filter.Ban.Any(Has);
            });
            if (exact && !accepted) return SearchFeasibilityResult.Impossible(new(
                SearchImpossibilityReasonCode.IncludeExcludeConflict, Diagnostic: "NeowOfferedOptionsConflictWithSelectedOpening"));
        }

        if (TryProveBonesGrantImpossible(compiled, out SearchImpossibilityProof? bonesProof))
            return SearchFeasibilityResult.Impossible(bonesProof!);

        if (TryProveCapsuleRelicSharedConstraintImpossible(compiled, out SearchImpossibilityProof? capsuleRelicProof))
            return SearchFeasibilityResult.Impossible(capsuleRelicProof!);

        if (TryProveVariantBossImpossible(compiled, out SearchImpossibilityProof? worldProof))
            return SearchFeasibilityResult.Impossible(worldProof!);

        if (TryProveCombatRewardIdentityImpossible(compiled, out SearchImpossibilityProof? rewardProof))
            return SearchFeasibilityResult.Impossible(rewardProof!);

        return SearchFeasibilityResult.Possible;
    }

    private static SearchImpossibilityProof NormalizationProof(IReadOnlyList<string> diagnostics)
    {
        string diagnostic = diagnostics.FirstOrDefault() ?? "SearchCompilerNormalizationImpossible";
        SearchImpossibilityReasonCode code = diagnostic switch
        {
            "BonesExactOrderRequiresTwoDistinctRouteRelics" => SearchImpossibilityReasonCode.BonesExactOrderRequiresTwoRelics,
            "KaleidoscopeExactOrderRequiresTwoPositionalSlotsWithAtLeastOneTarget" => SearchImpossibilityReasonCode.KaleidoscopeOrderedSlotsInvalid,
            "CombatCardSequenceInvalid" => SearchImpossibilityReasonCode.CombatCardSequenceInvalid,
            "CombatPotionSequenceInvalid" => SearchImpossibilityReasonCode.CombatPotionSequenceInvalid,
            "DarvCannotBeRequiredAsBothAct2AndAct3Ancient" => SearchImpossibilityReasonCode.DarvActConflict,
            "OpeningRouteRelicRequirementParentMismatch" => SearchImpossibilityReasonCode.OpeningRouteResultParentMismatch,
            _ when diagnostic.Contains("SeaGlassTargetWithoutSeaGlassOption", StringComparison.Ordinal) => SearchImpossibilityReasonCode.SeaGlassTargetWithoutOption,
            _ when diagnostic.Contains("ConflictsWithPrefix", StringComparison.Ordinal) => SearchImpossibilityReasonCode.SequencePositionPrefixConflict,
            _ when diagnostic.Contains("AllConflictsWithBan", StringComparison.Ordinal) ||
                   diagnostic.Contains("AnyExhaustedByBan", StringComparison.Ordinal) ||
                   diagnostic.Contains("AnyConflictsWithAll", StringComparison.Ordinal) => SearchImpossibilityReasonCode.IncludeExcludeConflict,
            _ => SearchImpossibilityReasonCode.CanonicalSemanticConflict
        };
        return new SearchImpossibilityProof(code, Diagnostic: diagnostic);
    }


    private static bool TryProveBonesGrantImpossible(
        CompiledSearch compiled,
        out SearchImpossibilityProof? proof)
    {
        proof = null;
        OpeningRouteRelicRequirement? requirement = compiled.NormalizedQuery.OpeningRouteRelicRequirement;
        if (requirement is null || requirement.IsEmpty ||
            requirement.ParentRouteRelicKey != BaseGameModelKeys.Relics.NeowsBones)
            return false;

        NeowEffectAuthoritySnapshot? authority = compiled.Context.Authority.EffectAuthority;
        if (authority is null || !authority.HasExactBonesPools || authority.BonesEligibleRelics is null)
            return false;

        HashSet<ModelKey> legal = authority.BonesEligibleRelics
            .Where(key => key.IsValid && key != BaseGameModelKeys.Relics.NeowsBones)
            .ToHashSet(ModelKeyComparer.Instance);
        if (requirement.RequiredRelicKeys.Any(key => key.IsValid && !legal.Contains(key)))
        {
            proof = new SearchImpossibilityProof(
                SearchImpossibilityReasonCode.BonesGrantTargetUnavailable,
                Diagnostic: "ExactBonesEligibleRelicPoolDoesNotContainTarget");
            return true;
        }
        return false;
    }

    private readonly record struct CapsuleDrawPosition(int Lane, int Ordinal);

    /// <summary>
    /// Exact feasibility proof for the direct Small/Large Capsule + initial
    /// Common/Uncommon/Rare Relic-sequence shared fact. Shop-lane constraints are
    /// deliberately excluded from this proof because Capsule does not runtime-consume
    /// the Shop lane. Mixed queries therefore prove only the overlapping C/U/R block;
    /// Shop remains an independent remainder.
    /// </summary>
    private static bool TryProveCapsuleRelicSharedConstraintImpossible(
        CompiledSearch compiled,
        out SearchImpossibilityProof? proof)
    {
        proof = null;
        if (compiled.NormalizedQuery.OpeningRoute is not { IsValid: true } route ||
            (route.RouteRelicKey != BaseGameModelKeys.Relics.SmallCapsule &&
             route.RouteRelicKey != BaseGameModelKeys.Relics.LargeCapsule))
            return false;

        NeowStructuredEffectSearchCondition[] capsuleConditions = compiled.NormalizedQuery.StructuredOpeningEffects
            .Where(condition => !condition.IsEmpty &&
                                condition.SourceRelicKey == route.RouteRelicKey &&
                                condition.Scope == NeowStructuredEffectScope.NestedRelics &&
                                condition.OutputKind == NeowStructuredOutputKind.Relic)
            .ToArray();
        if (capsuleConditions.Length != 1) return false;

        NeowStructuredEffectSearchCondition capsule = capsuleConditions[0];
        int drawCount = route.RouteRelicKey == BaseGameModelKeys.Relics.SmallCapsule ? 1 : 2;
        ModelKey[] targets = capsule.OutputKeys
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (targets.Length == 0 || targets.Length > drawCount ||
            (drawCount == 1 && capsule.Kind != NeowStructuredConditionKind.ExactSingle) ||
            (drawCount == 2 && capsule.Kind != NeowStructuredConditionKind.ExactUnorderedPair))
            return false;

        RelicSequenceSearchCondition[] explicitConditions = compiled.NormalizedQuery.RelicSequenceConstraints
            .Where(condition => !condition.IsEmpty &&
                                condition.Lane is RelicSequenceKind.Common or RelicSequenceKind.Uncommon or RelicSequenceKind.Rare)
            .ToArray();

        if (!TryBuildExactPlayerRelicLanes(compiled, out ModelKey[][] lanes))
            return false;

        var laneByKey = new Dictionary<ModelKey, int>(ModelKeyComparer.Instance);
        for (int lane = 0; lane < lanes.Length; lane++)
        {
            foreach (ModelKey key in lanes[lane])
            {
                if (laneByKey.TryGetValue(key, out int existing) && existing != lane)
                    return false;
                laneByKey[key] = lane;
            }
        }
        if (targets.Any(target => !laneByKey.ContainsKey(target)))
        {
            proof = new SearchImpossibilityProof(
                SearchImpossibilityReasonCode.CapsuleRelicQueueConflict,
                RelatedKey: targets.FirstOrDefault(target => !laneByKey.ContainsKey(target)),
                Diagnostic: "ExactCapsuleTargetAbsentFromCommonUncommonRareRelicBag");
            return true;
        }

        // Preserve the established Product boundary: an exact Relic slot beyond
        // the captured finite lane remains Unknown instead of being promoted to a
        // new semantic rejection by this batch.
        foreach (RelicSequenceSearchCondition condition in explicitConditions
                     .Where(condition => condition.RangeMode == SearchSequenceRangeMode.ExactSlot))
        {
            int lane = RelicLaneIndex(condition.Lane);
            if (lane < 0 || condition.RangeValue > lanes[lane].Length)
                return false;
        }

        bool encounteredUnknown = false;
        foreach (int[] preferredLanes in EnumerateCapsuleRarityPatterns(drawCount))
        {
            CapsuleDrawPosition?[] positions = ProjectCapsuleDrawPositions(preferredLanes, lanes.Select(lane => lane.Length).ToArray());
            IReadOnlyList<IReadOnlyList<(CapsuleDrawPosition Position, ModelKey Target)>> scenarios =
                BuildCapsuleTargetScenarios(positions, targets, laneByKey);
            foreach (IReadOnlyList<(CapsuleDrawPosition Position, ModelKey Target)> scenario in scenarios)
            {
                bool scenarioPossible = true;
                for (int lane = 0; lane < lanes.Length; lane++)
                {
                    RelicSequenceKind kind = RelicLaneKind(lane);
                    RelicSequenceSearchCondition[] laneConditions = explicitConditions
                        .Where(condition => condition.Lane == kind)
                        .ToArray();
                    (CapsuleDrawPosition Position, ModelKey Target)[] laneFacts = scenario
                        .Where(fact => fact.Position.Lane == lane)
                        .ToArray();
                    if (laneConditions.Length == 0 && laneFacts.Length == 0) continue;

                    if (!TryHasSatisfyingLanePermutation(lanes[lane], laneConditions, laneFacts, out bool possible))
                    {
                        encounteredUnknown = true;
                        scenarioPossible = false;
                        break;
                    }
                    if (!possible)
                    {
                        scenarioPossible = false;
                        break;
                    }
                }
                if (scenarioPossible) return false;
            }
        }

        if (encounteredUnknown) return false;
        proof = new SearchImpossibilityProof(
            SearchImpossibilityReasonCode.CapsuleRelicQueueConflict,
            Diagnostic: "ExactCapsuleRelicInitialBagConstraintHasNoSatisfyingPermutation");
        return true;
    }

    private static bool TryBuildExactPlayerRelicLanes(CompiledSearch compiled, out ModelKey[][] lanes)
    {
        lanes = Array.Empty<ModelKey[]>();
        WorldAuthoritySnapshot? world = compiled.Context.Authority.WorldAuthority;
        Beta109WorldGenerationSnapshot? generation = world?.Beta109Generation;
        if (world is null || generation is null ||
            !RuntimeProfilePolicies.UsesBeta110SharedAlgorithms(compiled.Context.ProfileId) ||
            world.CapturedProfileId != compiled.Context.ProfileId ||
            generation.Profile != compiled.Context.ProfileId ||
            world.Completeness != SnapshotCompleteness.Complete ||
            !SourceAuthorityRules.SupportsExactIdentity(world.SourceAuthority) ||
            !generation.DirectSourceAudited || generation.NoUnknownHooksOrModifiers == false ||
            !generation.RelicInitializationExact ||
            !generation.SharedRelicPoolOrderExact ||
            !generation.CharacterRelicPoolOrderExact ||
            !generation.RelicRarityAuthorityExact ||
            !generation.PlayerRelicPoolCompositionExact ||
            generation.PlayerRelicBuckets.Count == 0 ||
            generation.PlayerRelicBuckets.Any(bucket => !bucket.OrderExact))
            return false;

        var output = new ModelKey[3][];
        for (int lane = 0; lane < 3; lane++)
        {
            string rarity = lane switch { 0 => "COMMON", 1 => "UNCOMMON", _ => "RARE" };
            Beta109RelicBucketSnapshot[] matches = generation.PlayerRelicBuckets
                .Where(bucket => RelicBucketSuffix(bucket.BucketId) == rarity)
                .ToArray();
            if (matches.Length != 1) return false;
            ModelKey[] keys = matches[0].OrderedRelics.Where(key => key.IsValid).ToArray();
            if (keys.Length != matches[0].OrderedRelics.Count ||
                keys.Distinct(ModelKeyComparer.Instance).Count() != keys.Length)
                return false;
            output[lane] = keys;
        }
        lanes = output;
        return true;
    }

    private static string RelicBucketSuffix(string bucketId)
    {
        int separator = bucketId.LastIndexOf(':');
        string rarity = separator >= 0 && separator < bucketId.Length - 1
            ? bucketId[(separator + 1)..]
            : bucketId;
        return rarity.ToUpperInvariant();
    }

    private static IEnumerable<int[]> EnumerateCapsuleRarityPatterns(int drawCount)
    {
        if (drawCount <= 0)
        {
            yield return Array.Empty<int>();
            yield break;
        }
        var buffer = new int[drawCount];
        foreach (int[] value in Enumerate(0)) yield return value;

        IEnumerable<int[]> Enumerate(int index)
        {
            if (index == buffer.Length)
            {
                yield return (int[])buffer.Clone();
                yield break;
            }
            for (int lane = 0; lane < 3; lane++)
            {
                buffer[index] = lane;
                foreach (int[] value in Enumerate(index + 1)) yield return value;
            }
        }
    }

    private static CapsuleDrawPosition?[] ProjectCapsuleDrawPositions(IReadOnlyList<int> preferredLanes, int[] initialCounts)
    {
        int[] remaining = (int[])initialCounts.Clone();
        int[] consumed = new int[remaining.Length];
        var output = new CapsuleDrawPosition?[preferredLanes.Count];
        for (int draw = 0; draw < preferredLanes.Count; draw++)
        {
            int selectedLane = -1;
            for (int lane = preferredLanes[draw]; lane < 3; lane++)
            {
                if (remaining[lane] <= 0) continue;
                selectedLane = lane;
                break;
            }
            if (selectedLane < 0) continue;
            consumed[selectedLane]++;
            remaining[selectedLane]--;
            output[draw] = new CapsuleDrawPosition(selectedLane, consumed[selectedLane]);
        }
        return output;
    }

    private static IReadOnlyList<IReadOnlyList<(CapsuleDrawPosition Position, ModelKey Target)>> BuildCapsuleTargetScenarios(
        IReadOnlyList<CapsuleDrawPosition?> positions,
        IReadOnlyList<ModelKey> targets,
        IReadOnlyDictionary<ModelKey, int> laneByKey)
    {
        var scenarios = new List<IReadOnlyList<(CapsuleDrawPosition Position, ModelKey Target)>>();
        if (targets.Count == 1)
        {
            for (int draw = 0; draw < positions.Count; draw++)
            {
                if (!positions[draw].HasValue) continue;
                CapsuleDrawPosition position = positions[draw]!.Value;
                if (laneByKey.TryGetValue(targets[0], out int lane) && lane == position.Lane)
                    scenarios.Add(new[] { (position, targets[0]) });
            }
            return scenarios;
        }
        if (targets.Count == 2 && positions.Count == 2 && positions[0].HasValue && positions[1].HasValue)
        {
            CapsuleDrawPosition first = positions[0]!.Value;
            CapsuleDrawPosition second = positions[1]!.Value;
            Add(targets[0], targets[1]);
            Add(targets[1], targets[0]);

            void Add(ModelKey a, ModelKey b)
            {
                if (!laneByKey.TryGetValue(a, out int laneA) || laneA != first.Lane) return;
                if (!laneByKey.TryGetValue(b, out int laneB) || laneB != second.Lane) return;
                scenarios.Add(new[] { (first, a), (second, b) });
            }
        }
        return scenarios;
    }

    private static bool TryHasSatisfyingLanePermutation(
        IReadOnlyList<ModelKey> pool,
        IReadOnlyList<RelicSequenceSearchCondition> explicitConditions,
        IReadOnlyList<(CapsuleDrawPosition Position, ModelKey Target)> implicitFacts,
        out bool possible)
    {
        possible = false;
        if (pool.Any(key => !key.IsValid) || pool.Distinct(ModelKeyComparer.Instance).Count() != pool.Count)
            return false;

        var constraints = new List<(SearchSequenceRangeMode Mode, int Range, ModelKeySetFilter Keys)>();
        constraints.AddRange(explicitConditions.Select(condition => (condition.RangeMode, condition.RangeValue, condition.Keys)));
        constraints.AddRange(implicitFacts.Select(fact => (
            SearchSequenceRangeMode.ExactSlot,
            fact.Position.Ordinal,
            new ModelKeySetFilter(new[] { fact.Target }, Array.Empty<ModelKey>(), Array.Empty<ModelKey>()))));
        constraints = constraints.Where(item => item.Range > 0 && !item.Keys.IsEmpty).ToList();
        if (constraints.Count == 0)
        {
            possible = true;
            return true;
        }
        if (constraints.Any(item => item.Mode == SearchSequenceRangeMode.ExactSlot && item.Range > pool.Count))
            return false;

        ModelKey[] relevantKeys = constraints
            .SelectMany(item => item.Keys.Any.Concat(item.Keys.All).Concat(item.Keys.Ban))
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (relevantKeys.Length > 60) return false;
        var poolSet = new HashSet<ModelKey>(pool, ModelKeyComparer.Instance);
        foreach (var constraint in constraints)
        {
            ModelKeySetFilter keys = constraint.Keys;
            ModelKey[] any = keys.Any.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray();
            ModelKey[] all = keys.All.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray();
            if (all.Any(key => !poolSet.Contains(key)) || (any.Length > 0 && any.All(key => !poolSet.Contains(key))))
            {
                possible = false;
                return true;
            }
        }

        var bitByKey = new Dictionary<ModelKey, ulong>(ModelKeyComparer.Instance);
        for (int i = 0; i < relevantKeys.Length; i++) bitByKey[relevantKeys[i]] = 1UL << i;
        ulong initialMask = 0UL;
        int otherCount = 0;
        foreach (ModelKey key in pool)
        {
            if (bitByKey.TryGetValue(key, out ulong bit)) initialMask |= bit;
            else otherCount++;
        }
        ulong Mask(IEnumerable<ModelKey> keys)
        {
            ulong mask = 0UL;
            foreach (ModelKey key in keys.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance))
                if (bitByKey.TryGetValue(key, out ulong bit)) mask |= bit;
            return mask;
        }

        var compiled = constraints.Select(item => (
            item.Mode,
            Trigger: item.Mode == SearchSequenceRangeMode.FirstN ? Math.Min(item.Range, pool.Count) : item.Range,
            Any: Mask(item.Keys.Any),
            All: Mask(item.Keys.All),
            Ban: Mask(item.Keys.Ban))).ToArray();
        foreach (var condition in compiled.Where(item => item.Trigger == 0))
        {
            if (condition.Mode == SearchSequenceRangeMode.ExactSlot ||
                condition.Any != 0UL || condition.All != 0UL)
            {
                possible = false;
                return true;
            }
        }
        int maxPosition = compiled.Max(item => item.Trigger);
        var memo = new Dictionary<(int Position, ulong Remaining, int Others), bool>();

        bool Matches(ulong observed, ulong any, ulong all, ulong ban) =>
            (any == 0UL || (observed & any) != 0UL) &&
            (all == 0UL || (observed & all) == all) &&
            (ban == 0UL || (observed & ban) == 0UL);

        bool Accept(int position, ulong current, ulong remainingAfter)
        {
            ulong seen = initialMask ^ remainingAfter;
            foreach (var condition in compiled)
            {
                if (condition.Trigger != position) continue;
                ulong observed = condition.Mode == SearchSequenceRangeMode.ExactSlot ? current : seen;
                if (!Matches(observed, condition.Any, condition.All, condition.Ban)) return false;
            }
            return true;
        }

        bool Solve(int position, ulong remaining, int others)
        {
            var state = (position, remaining, others);
            if (memo.TryGetValue(state, out bool cached)) return cached;
            int remainingTotal = System.Numerics.BitOperations.PopCount(remaining) + others;
            if (position >= maxPosition || remainingTotal == 0)
                return memo[state] = true;

            int nextPosition = position + 1;
            ulong bits = remaining;
            while (bits != 0UL)
            {
                int index = System.Numerics.BitOperations.TrailingZeroCount(bits);
                ulong bit = 1UL << index;
                bits &= ~bit;
                ulong nextRemaining = remaining & ~bit;
                if (Accept(nextPosition, bit, nextRemaining) && Solve(nextPosition, nextRemaining, others))
                    return memo[state] = true;
            }
            if (others > 0 && Accept(nextPosition, 0UL, remaining) && Solve(nextPosition, remaining, others - 1))
                return memo[state] = true;
            return memo[state] = false;
        }

        possible = Solve(0, initialMask, otherCount);
        return true;
    }

    private static int RelicLaneIndex(RelicSequenceKind kind) => kind switch
    {
        RelicSequenceKind.Common => 0,
        RelicSequenceKind.Uncommon => 1,
        RelicSequenceKind.Rare => 2,
        _ => -1
    };

    private static RelicSequenceKind RelicLaneKind(int lane) => lane switch
    {
        0 => RelicSequenceKind.Common,
        1 => RelicSequenceKind.Uncommon,
        _ => RelicSequenceKind.Rare
    };

    private static bool TryProveVariantBossImpossible(
        CompiledSearch compiled,
        out SearchImpossibilityProof? proof)
    {
        proof = null;
        VariantScopedBossBranch[] branches = compiled.NormalizedQuery.VariantBossBranches
            .Where(branch => branch.IsValid)
            .ToArray();
        if (branches.Length == 0) return false;

        Beta109WorldGenerationSnapshot? generation = compiled.Context.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null || !generation.ActSelectionAuthorityExact) return false;

        foreach (IGrouping<int, VariantScopedBossBranch> actGroup in branches.GroupBy(branch => branch.Act).OrderBy(group => group.Key))
        {
            Beta109ActSelectionGroupSnapshot? selection = generation.ActSelectionGroups.SingleOrDefault(group => group.Act == actGroup.Key);
            if (selection is null || !selection.EligibilityAndOrderExact || selection.EligibleActsInSourceOrder.Count == 0)
                continue;

            bool allBranchAuthorityExact = true;
            bool anyBranchPossible = false;
            foreach (VariantScopedBossBranch branch in actGroup)
            {
                if (!selection.EligibleActsInSourceOrder.Contains(branch.VariantKey, ModelKeyComparer.Instance))
                    continue;

                Beta109ActGenerationSnapshot? variant = generation.OrderedActCatalog.FirstOrDefault(item =>
                    item.Act == branch.Act && item.ActKey == branch.VariantKey);
                if (variant is null || !variant.HasExactGenerationInputs)
                {
                    allBranchAuthorityExact = false;
                    continue;
                }

                if (BranchHasLegalBossOutcome(compiled.Context.Ascension, variant, branch))
                {
                    anyBranchPossible = true;
                    break;
                }
            }

            if (anyBranchPossible) continue;
            if (!allBranchAuthorityExact) continue;

            // Every player-authored branch for this Act was checked against exact
            // runtime Variant/Boss authority and none has a legal outcome.
            proof = new SearchImpossibilityProof(
                SearchImpossibilityReasonCode.ActVariantBossCombinationImpossible,
                Act: actGroup.Key,
                Diagnostic: "ExactRuntimeVariantBossOutcomeSetEmpty");
            return true;
        }

        return false;
    }

    private static bool BranchHasLegalBossOutcome(
        int ascension,
        Beta109ActGenerationSnapshot variant,
        VariantScopedBossBranch branch)
    {
        ModelKey[] pool = variant.Bosses
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (pool.Length == 0) return false;

        int bossCount = variant.Act == 3 && ascension >= 10 ? 2 : 1;
        if (branch.IncludesSecondBoss && !branch.SecondBoss.IsEmpty && bossCount < 2)
            return false;

        if (bossCount == 1)
        {
            return pool.Any(first => MatchesPositionFilter(first, branch.FirstBoss));
        }

        if (pool.Length < 2) return false;
        foreach (ModelKey first in pool)
        foreach (ModelKey second in pool)
        {
            if (first == second) continue;
            if (!MatchesPositionFilter(first, branch.FirstBoss)) continue;
            if (branch.IncludesSecondBoss && !MatchesPositionFilter(second, branch.SecondBoss)) continue;
            return true;
        }
        return false;
    }

    private static bool MatchesPositionFilter(ModelKey key, ModelKeySetFilter filter)
    {
        if (filter.IsEmpty) return true;
        if (filter.Any.Count > 0 && !filter.Any.Contains(key, ModelKeyComparer.Instance)) return false;
        if (filter.All.Count > 0 && filter.All.Any(required => required != key)) return false;
        if (filter.Ban.Contains(key, ModelKeyComparer.Instance)) return false;
        return true;
    }

    private static bool TryProveCombatRewardIdentityImpossible(
        CompiledSearch compiled,
        out SearchImpossibilityProof? proof)
    {
        proof = null;
        NeowEffectAuthoritySnapshot? authority = compiled.Context.Authority.EffectAuthority;
        if (authority is null) return false;

        CombatCardRewardSequenceSearchCondition? cards = compiled.NormalizedQuery.CombatCardRewards;
        if (cards is { IsEmpty: false } && authority.HasExactCharacterRewardPool)
        {
            HashSet<ModelKey> legal = authority.CharacterRewardPool!
                .Where(card => compiled.Context.Authority.PlayersCount > 1 || !card.IsMultiplayerOnly)
                .Where(card => card.EligibleForPostCombatRewardByPoolMembership)
                .Where(card => card.IsUnlockedInCapturedPool)
                .Where(card => card.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
                .Select(card => card.CardKey)
                .Where(key => key.IsValid)
                .ToHashSet(ModelKeyComparer.Instance);

            for (int index = 0; index < cards.Count; index++)
            {
                ModelKey? target = cards.Slots[index];
                if (target.HasValue && !legal.Contains(target.Value))
                {
                    proof = new SearchImpossibilityProof(
                        SearchImpossibilityReasonCode.CombatCardTargetUnavailable,
                        Slot: cards.OrderMode == CombatRewardSequenceOrderMode.Ordered ? index + 1 : null,
                        RelatedKey: target,
                        Diagnostic: "ExactRuntimeCharacterRewardPoolDoesNotContainTarget");
                    return true;
                }
            }
        }

        CombatPotionRewardSequenceSearchCondition? potions = compiled.NormalizedQuery.CombatPotionRewards;
        if (potions is { IsEmpty: false } && authority.HasExactPotions)
        {
            HashSet<ModelKey> legal = authority.PotionPool!
                .Where(potion => compiled.Context.Authority.PlayersCount > 1 || !potion.IsMultiplayerOnly)
                .Where(potion => potion.Rarity is EffectPotionRarity.Common or EffectPotionRarity.Uncommon or EffectPotionRarity.Rare)
                .Select(potion => potion.PotionKey)
                .Where(key => key.IsValid)
                .ToHashSet(ModelKeyComparer.Instance);

            for (int index = 0; index < potions.Count; index++)
            {
                CombatPotionRewardSlotSearchCondition slot = potions.Slots[index];
                if (slot.Requirement == CombatPotionSlotRequirement.DropSpecific &&
                    slot.PotionKey.HasValue &&
                    !legal.Contains(slot.PotionKey.Value))
                {
                    proof = new SearchImpossibilityProof(
                        SearchImpossibilityReasonCode.CombatPotionTargetUnavailable,
                        Slot: potions.OrderMode == CombatRewardSequenceOrderMode.Ordered ? index + 1 : null,
                        RelatedKey: slot.PotionKey,
                        Diagnostic: "ExactRuntimePotionPoolDoesNotContainTarget");
                    return true;
                }
            }
        }

        return false;
    }
}

internal enum SearchImpossibilityReasonCode : byte
{
    CanonicalSemanticConflict = 0,
    BonesExactOrderRequiresTwoRelics = 1,
    KaleidoscopeOrderedSlotsInvalid = 2,
    CombatCardSequenceInvalid = 3,
    CombatPotionSequenceInvalid = 4,
    ActVariantBossCombinationImpossible = 5,
    CombatCardTargetUnavailable = 6,
    CombatPotionTargetUnavailable = 7,
    BonesGrantTargetUnavailable = 8,
    DarvActConflict = 9,
    SeaGlassTargetWithoutOption = 10,
    SequencePositionPrefixConflict = 11,
    IncludeExcludeConflict = 12,
    OpeningRouteResultParentMismatch = 13,
    CapsuleRelicQueueConflict = 14
}

internal sealed record SearchImpossibilityProof(
    SearchImpossibilityReasonCode ReasonCode,
    int? Act = null,
    int? Slot = null,
    ModelKey? RelatedKey = null,
    string Diagnostic = "");

internal sealed record SearchFeasibilityResult(
    bool IsImpossible,
    SearchImpossibilityProof? Proof)
{
    public static SearchFeasibilityResult Possible { get; } = new(false, null);
    public static SearchFeasibilityResult Impossible(SearchImpossibilityProof proof) => new(true, proof);
}
