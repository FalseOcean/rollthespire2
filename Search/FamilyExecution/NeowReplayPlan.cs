using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// Immutable numeric donor data. No Fast execution plan or backend is constructed.
internal sealed record NeowReplayPlan(
    Beta110FastNeowAuthority Authority,
    NeowSearchFilter Filter,
    Beta110FastDomain EnabledDomains,
    Beta110FastStructuredCondition[] StructuredConditions,
    ushort[] RequiredFinalCurseIds,
    ushort[] BannedFinalCurseIds,
    ulong TopAny, ulong TopAll, ulong TopBan,
    ulong BonesAny, ulong BonesAll, ulong BonesBan,
    byte Selected, byte First, byte Second,
    bool Bones, bool RequireBones,
    bool LeafyBonesInitialTransformInvariant,
    string[] ExactOnly)
{
    // Physical applicability fact from existing immutable authority, not condition ownership.
    internal bool DirectNestedVanilla111 { get; init; }
    internal bool AuditedModern113IdentityReplay { get; init; }
    internal bool AuditedIdentityReplay => DirectNestedVanilla111 || AuditedModern113IdentityReplay;
    internal int[] UnobservedRewardsDraws { get; init; } = [];
    internal NeowModernTransformGate? ModernTransformGate { get; init; }
    internal bool HasCapsuleRarityGate { get; init; }
    internal string CapsuleRarityRevision => HasCapsuleRarityGate ? NeowCapsuleRarityGate.Revision : "";
    internal int SharedNicheDraws { get; init; }
    internal int SharedPotionDraws { get; init; }
    internal (int Niche, int Potions)[] SharedArrivals { get; init; } = [];
    internal int CapsuleUpgradeUpperBound { get; init; } = -1;
    internal NeowAuthoredUpgradeContinuation? AuthoredUpgrades { get; init; }
    internal NeowCapsuleNicheContinuation? CapsuleNicheContinuation { get; init; }
    internal uint CapsulesWithKnownZeroNonCombatAdvance { get; init; }
    internal uint SmallCapsuleNonCombatAdvances { get; init; }
    internal uint LargeCapsuleNonCombatAdvances { get; init; }
    internal uint DualCapsuleNonCombatAdvances { get; init; }
    internal bool HasCapsuleNonCombatAlternatives =>
        (SmallCapsuleNonCombatAdvances | LargeCapsuleNonCombatAdvances | DualCapsuleNonCombatAdvances) != 0;
    internal string CapsuleNonCombatRevision => HasCapsuleNonCombatAlternatives
        ? NeowCapsuleNonCombatContinuation.AlternativesRevision
        : CapsulesWithKnownZeroNonCombatAdvance == 0 ? "" : NeowCapsuleNonCombatContinuation.Revision;
    internal string[] FilterRemainders => ExactOnly.Concat(CapsuleNicheContinuation?.Remainders ?? [])
        .Select(cause => "N.Neow:" + cause).ToArray();
    internal bool HasFinalCurseFastProjection => (EnabledDomains & Beta110FastDomain.FinalCurse) != 0;
    internal bool NeedsRewards => (EnabledDomains & ~(Beta110FastDomain.FinalCurse | Beta110FastDomain.NewLeafTransform |
        Beta110FastDomain.LeafyPoulticeTransforms | Beta110FastDomain.PhialHolsterPotions)) != 0;
    internal bool NeedsNicheOrKaleidoscopePools => NeedsRewards || HasFinalCurseFastProjection ||
        (EnabledDomains & (Authority.UsesChildShuffle ? Beta110FastDomain.PhialHolsterPotions : Beta110FastDomain.NewLeafTransform)) != 0;
    internal bool NeedsTransformations => (EnabledDomains & (Beta110FastDomain.LeafyPoulticeTransforms |
        (Authority.UsesChildShuffle ? Beta110FastDomain.NewLeafTransform : 0))) != 0;
    internal bool NeedsPotions => (EnabledDomains & Beta110FastDomain.PhialHolsterPotions) != 0 ||
        Authority.UsesChildShuffle && (HasFinalCurseFastProjection || (EnabledDomains & Beta110FastDomain.KaleidoscopeOffers) != 0);

    internal string ContinuationIssue(bool capsule = false)
    {
        bool rewards = capsule || NeedsRewards;
        bool niche = capsule || NeedsNicheOrKaleidoscopePools;
        bool transform = !capsule && NeedsTransformations;
        bool potion = !capsule && NeedsPotions;
        var c = Authority.EffectCatalog;
        byte[] sources = !Bones ? [Selected] : First != 255 && Second != 255 ? [First, Second] : Authority.BonesEligibleRelicIds.Where(id =>
            (BonesBan & Beta110FastRelicCatalog.Bit(id)) == 0 && (System.Numerics.BitOperations.PopCount(BonesAll) != 2 ||
                (BonesAll & Beta110FastRelicCatalog.Bit(id)) != 0)).ToArray();
        foreach (byte source in sources)
        {
            bool ready = source switch
            {
                Beta110FastRelicCatalog.ArcaneScroll when rewards => c.CharacterRewardAuthorityExact && c.CharacterRewardPool.Rare.Length > 0,
                Beta110FastRelicCatalog.MassiveScroll when rewards && Authority.PlayersCount > 1 => c.CharacterRewardAuthorityExact && c.ColorlessRewardAuthorityExact && c.MultiplayerRewardPool.TotalCount >= 3,
                Beta110FastRelicCatalog.HeftyTablet when rewards => c.CharacterRewardAuthorityExact && c.CharacterRewardPool.Rare.Length >= 3,
                Beta110FastRelicCatalog.LeadPaperweight when rewards => c.ColorlessRewardAuthorityExact && c.ColorlessRewardPool.TotalCount >= 2,
                Beta110FastRelicCatalog.LostCoffer when rewards => c.CharacterRewardAuthorityExact && c.CharacterRewardPool.TotalCount >= 3 && c.PotionAuthorityExact && c.PotionPool.HasAtLeastPerRarity(1),
                Beta110FastRelicCatalog.ScrollBoxes when rewards => c.CharacterCardAuthorityExact && c.CharacterRewardPool.Common.Length >= 4 && c.CharacterRewardPool.Uncommon.Length >= 2,
                Beta110FastRelicCatalog.Kaleidoscope when rewards || niche => c.OtherCharacterCardAuthorityExact && c.OtherCharacterPools.Length >= 3 && (!rewards || c.OtherCharacterPools.All(pool => pool.TotalCount > 0)),
                Beta110FastRelicCatalog.NewLeaf when (Authority.UsesChildShuffle ? transform : niche) => c.NewLeafTransformAuthorityExact && c.NewLeafTransformPool.Length > 0,
                Beta110FastRelicCatalog.LeafyPoultice when transform => c.LeafyTransformAuthorityExact && c.LeafyStrikeTransformPool.Length > 0 && c.LeafyDefendTransformPool.Length > 0,
                Beta110FastRelicCatalog.PhialHolster when potion => c.PotionAuthorityExact && c.PotionPool.HasAtLeastPerRarity(2),
                _ => true
            };
            if (!ready) return "DonorContinuationAuthorityUnavailable:Source" + source;
        }
        return "";
    }

    internal void ValidateExecutionScope()
    {
        if (Authority.PlayersCount == 1 && (Authority.PlayerSlotIndex != 0 || SharedNicheDraws != 0 ||
            SharedPotionDraws != 0 || SharedArrivals.Length != 0 || CapsuleUpgradeUpperBound >= 0))
            throw new InvalidOperationException("N.SingleplayerContainsPartyContinuation");
    }

    internal static bool IsCapsule(ModelKey key) => key == BaseGameModelKeys.Relics.SmallCapsule || key == BaseGameModelKeys.Relics.LargeCapsule;
    internal static bool IsCapsule(NeowStructuredEffectSearchCondition condition) =>
        condition.Scope == NeowStructuredEffectScope.NestedRelics &&
        condition.OutputKind == NeowStructuredOutputKind.Relic &&
        (IsCapsule(condition.SourceRelicKey) || IsGroupedCapsule(condition));
    internal static bool IsGroupedCapsule(NeowStructuredEffectSearchCondition condition) =>
        condition.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
        condition.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset &&
        condition.OutputKeys.Count is >= 1 and <= 3;
    internal static bool HasCapsule(SearchModel request) =>
        request.Evaluation.StructuredNeowEffects.Any(condition => !condition.IsEmpty && IsCapsule(condition)) ||
        request.Evaluation.EffectOutputConditions.Any(condition => !condition.IsEmpty && IsCapsule(condition.SourceRelicKey)) ||
        !request.Evaluation.CapsuleContainedRelics.IsEmpty || request.Evaluation.RequireWhetstone || request.Evaluation.RequireWarPaint;

    internal static NeowSearchFilter NeowFilter(SearchModel request, bool capsule)
    {
        ExactSearchEvaluationProjection e = request.Evaluation;
        bool legacyBonesContext = e.RequireNeowsBones || !e.BonesRelics.IsEmpty ||
            e.RequiredBonesCombination.Count > 0 || e.RequiredBonesAcquisitionOrder.Count > 0;
        // R retains only the authored route prerequisites, never unrelated N predicates.
        return new NeowSearchFilter(
            capsule ? ModelKeySetFilter.Empty : e.NeowRelics,
            capsule ? false : e.RequireNeowsBones,
            capsule ? ModelKeySetFilter.Empty : e.BonesRelics,
            e.RequiredBonesCombination,
            !capsule && e.RequireSmallCapsule, !capsule && e.RequireLargeCapsule,
            capsule ? e.CapsuleContainedRelics : ModelKeySetFilter.Empty,
            capsule && e.RequireWhetstone, capsule && e.RequireWarPaint,
            capsule ? null : e.RequiredFinalCurse,
            capsule ? [] : e.BannedFinalCurses, NeowSearchPreset.None)
        {
            NeowRoute = e.NeowRoute ?? (capsule && legacyBonesContext ? new(BaseGameModelKeys.Relics.NeowsBones) : null),
            RequiredBonesAcquisitionOrder = e.RequiredBonesAcquisitionOrder,
            StructuredNeowEffects = e.StructuredNeowEffects.Where(c => !c.IsEmpty && IsCapsule(c) == capsule).ToArray()
        };
    }

    internal static NeowReplayPlan Compile(SearchModel request, bool capsule)
    {
        NeowSearchFilter filter = NeowFilter(request, capsule);
        bool newOpeningStreams = request.Authority.VersionResolution.SemanticProfileId == RngSemanticProfileId.Modern113;
        if (!capsule) filter = filter with { StructuredNeowEffects = filter.StructuredNeowEffects
            .Concat(NeowChoiceCommitment.CombinedConditions(filter.StructuredNeowEffects)).ToArray() };
        if (capsule && filter.NeowRoute is null)
            throw new InvalidOperationException("RFamilyUnboundCapsuleRoute_ExactOnly");
        var exactOnly = new List<string>();
        if (!capsule && !NeowOfferWhitelist.TryProject(request.Evaluation.NeowOfferWhitelist,
                request.Authority, filter.NeowRelics, out _))
            exactOnly.Add("OfferWhitelistAuthorityUnavailable");
        if (request.Evaluation.Preset != NeowSearchPreset.None) exactOnly.Add("LegacyPreset");
        if (request.Evaluation.EffectOutputConditions.Any(c => !c.IsEmpty)) exactOnly.Add("LegacyEffectOutputConditions");
        // Legacy unbound Capsule presence can be satisfied by a Bones route too;
        // it is not equivalent to requiring a top-level Capsule offer.
        if (!capsule && filter.NeowRoute is null && filter.RequiredBonesCombination.Count == 0 &&
            filter.RequiredBonesAcquisitionOrder.Count == 0 && !filter.RequireNeowsBones && filter.BonesRelics.IsEmpty &&
            (filter.RequireSmallCapsule || filter.RequireLargeCapsule))
        {
            exactOnly.Add("UnboundCapsulePresence");
            filter = filter with { RequireSmallCapsule = false, RequireLargeCapsule = false };
        }
        var conditions = new List<Beta110FastStructuredCondition>();
        var supported = new List<NeowStructuredEffectSearchCondition>();
        var offeredBones = new List<ModelKey>();
        Beta110FastEffectCatalog catalog = Beta110FastEffectCatalogCompiler.Compile(request.Authority.EffectAuthority,
            filter.StructuredNeowEffects.SelectMany(c => c.OutputKeys)
                .Concat(NeowNumericCompilation.EnumerateFinalCurseRequiredKeys(filter)).Concat(filter.BannedFinalCurses)
                .Concat(filter.CapsuleContainedRelics.Any).Concat(filter.CapsuleContainedRelics.All).Concat(filter.CapsuleContainedRelics.Ban));
        Beta110FastDomain domains = Beta110FastDomain.None;
        foreach (var condition in filter.StructuredNeowEffects)
        {
            if (condition.Scope == NeowStructuredEffectScope.FinalCurse) { supported.Add(condition); continue; }
            // Exact checks this scope as membership of the two offered relics
            // before interpreting the row's Kind. A unique target set is the
            // same identity observation as OpeningRouteRelicRequirement.
            if (!capsule && condition.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
                condition.Scope == NeowStructuredEffectScope.BonesOfferedRelics &&
                condition.OutputKind == NeowStructuredOutputKind.Relic && condition.OutputKeys.Count is 1 or 2 &&
                condition.OutputKeys.Distinct(ModelKeyComparer.Instance).Count() == condition.OutputKeys.Count &&
                condition.OutputKeys.All(key => Beta110FastRelicCatalog.TryGetId(key, out _)))
            {
                offeredBones.AddRange(condition.OutputKeys);
                supported.Add(condition);
                continue;
            }
            if (capsule && IsGroupedCapsule(condition))
            {
                supported.Add(condition);
                domains |= Beta110FastDomain.CapsuleNestedRelics;
                continue;
            }
            // R's bag authority is the current World pool, not the historical
            // Neow Effect catalog's duplicate bag. Numeric metadata here serves
            // source presence only; R compares actual ModelKeys from its own bag.
            if (capsule && IsCapsule(condition) &&
                (condition.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule
                    ? condition.Kind == NeowStructuredConditionKind.ExactSingle && condition.OutputKeys.Count == 1
                    : condition.Kind == NeowStructuredConditionKind.ExactUnorderedPair && condition.OutputKeys.Count is 1 or 2))
            {
                Beta110FastRelicCatalog.TryGetId(condition.SourceRelicKey, out byte source);
                ushort Target(int index) => index < condition.OutputKeys.Count && catalog.TryGetDenseId(condition.OutputKeys[index], out ushort id)
                    ? id : Beta110FastDenseId.Invalid;
                conditions.Add(new(source, source == Beta110FastRelicCatalog.SmallCapsule
                    ? Beta110FastStructuredConditionKind.SmallCapsuleNestedRelic : Beta110FastStructuredConditionKind.LargeCapsuleNestedRelics,
                    Target(0), Target(1), Target(2), (byte)condition.OutputKeys.Count, condition.AllowDuplicateOutputs));
                supported.Add(condition); domains |= Beta110FastDomain.CapsuleNestedRelics;
                continue;
            }
            if (NeowNumericCompilation.TryCompileStructuredCondition(condition, catalog, out var numeric, out var domain))
            { conditions.Add(numeric); supported.Add(condition); domains |= domain; }
            else exactOnly.Add("Structured:" + condition.SourceRelicKey.Serialized + ":" + condition.Kind);
        }
        ushort[] required = [], banned = [];
        bool finalCurse = NeowNumericCompilation.EnumerateFinalCurseRequiredKeys(filter).Any() || filter.BannedFinalCurses.Count != 0;
        if (finalCurse && catalog.CursePoolAuthorityExact &&
            NeowNumericCompilation.TryCompileCurseIds(NeowNumericCompilation.EnumerateFinalCurseRequiredKeys(filter), catalog, out required) &&
            NeowNumericCompilation.TryCompileCurseIds(filter.BannedFinalCurses, catalog, out banned)) domains |= Beta110FastDomain.FinalCurse;
        else if (finalCurse)
        {
            exactOnly.Add("FinalCurseAuthorityUnavailable");
            supported.RemoveAll(c => c.Scope == NeowStructuredEffectScope.FinalCurse);
            filter = filter with { RequiredFinalCurse = null, BannedFinalCurses = [] };
        }
        filter = filter with { StructuredNeowEffects = supported.ToArray() };
        if (capsule && exactOnly.Any(issue => issue.StartsWith("Structured:", StringComparison.Ordinal)))
            throw new InvalidOperationException("RFamilyCapsulePredicateUnsupported_ExactOnly");
        byte Id(ModelKey? key) => key.HasValue && Beta110FastRelicCatalog.TryGetId(key.Value, out byte id) ? id : Beta110FastRelicCatalog.InvalidId;
        ulong Mask(IEnumerable<ModelKey> keys)
        {
            if (!NeowNumericCompilation.TryBuildMask(keys, out ulong mask))
                throw new InvalidOperationException("NeowFamilyUnmappedIdentity");
            return mask;
        }
        byte selected = Id(filter.NeowRoute?.RouteRelicKey);
        bool bones = selected == Beta110FastRelicCatalog.NeowsBones || filter.RequireNeowsBones ||
            filter.RequiredBonesCombination.Count != 0 || !filter.BonesRelics.IsEmpty || finalCurse ||
            filter.RequiredBonesAcquisitionOrder.Count != 0 || offeredBones.Count != 0;
        if (!capsule && !bones && selected == Beta110FastRelicCatalog.InvalidId && conditions.Count > 0)
        {
            // Current UI authors result conditions only under a selected route.
            // Imported unbound rows remain Exact-owned, without a route-OR kernel.
            exactOnly.Add("UnboundStructuredRoute");
            conditions.Clear();
            domains = Beta110FastDomain.None;
        }
        if (!capsule) catalog = catalog with { OrdinaryRelics = [], SharedRelicConsumeShuffleLengths = [], PlayerRelicBuckets = [], RelicBagAuthorityExact = false };
        var authority = NeowNumericCompilation.BuildAuthority(request.CompiledSearch.Context, catalog, bones);
        if (!authority.IdentityAuthorityExact || bones && !authority.BonesAuthorityExact)
            throw new InvalidOperationException("NeowFamilyReplayAuthorityUnavailable");
        ModelKey[] sources = request.Evaluation.StructuredNeowEffects.Where(c => !c.IsEmpty)
            .Select(c => c.SourceRelicKey).Where(k => k != BaseGameModelKeys.Relics.NeowsBones).Distinct().ToArray();
        // N owns source identity even when the corresponding nested observation is R-owned.
        ulong sourceMask = !capsule && bones ? Mask(sources) : 0;
        ulong requiredCapsules = !capsule ? Mask(new[] {
            filter.RequireSmallCapsule ? BaseGameModelKeys.Relics.SmallCapsule : default,
            filter.RequireLargeCapsule ? BaseGameModelKeys.Relics.LargeCapsule : default }.Where(k => k.IsValid)) : 0;
        if (!capsule && bones)
            filter = filter with { BonesRelics = new ModelKeySetFilter(filter.BonesRelics.Any,
                filter.BonesRelics.All.Concat(sources).Concat(offeredBones).Concat(new[] {
                    filter.RequireSmallCapsule ? BaseGameModelKeys.Relics.SmallCapsule : default,
                    filter.RequireLargeCapsule ? BaseGameModelKeys.Relics.LargeCapsule : default }.Where(k => k.IsValid))
                    .Distinct().ToArray(), filter.BonesRelics.Ban), RequireSmallCapsule = false, RequireLargeCapsule = false };
        ulong requiredBones = Mask(filter.BonesRelics.All.Concat(filter.RequiredBonesCombination)) | sourceMask | (bones ? requiredCapsules : 0);
        ulong bannedBones = Mask(filter.BonesRelics.Ban);
        bool leafyBonesInitialTransformInvariant = false;
        var effectAuthority = request.Authority.EffectAuthority;
        bool hasLeafyPredicate = conditions.Any(condition =>
            condition.SourceRelicId == Beta110FastRelicCatalog.LeafyPoultice &&
            condition.Kind == Beta110FastStructuredConditionKind.LeafyPoulticeTransforms);
        // In audited 0.111 vanilla Bones there is one possible predecessor. Its
        // largest basic-card removal is two; other effects either preserve keys/
        // pool identity or use streams separate from Leafy's Transformations RNG.
        if (hasLeafyPredicate && bones && request.Authority.CanUseCurrentModel && request.Authority.NoRunModifiers == true &&
            request.Authority.IsAuditedBeta111SourceContext && request.Authority.IsBeta111NeowIdentityAuthorityExact &&
            authority.BonesAuthorityExact && effectAuthority?.CapturedProfileId == RuntimeProfileId.Beta111 &&
            effectAuthority.HasExactDeck && effectAuthority.OrderedDeck is not null &&
            effectAuthority.CharacterStrikeKey is { } strikeKey && effectAuthority.CharacterDefendKey is { } defendKey &&
            authority.BonesEligibleRelicIds.All(id => id <= Beta110FastRelicCatalog.StoneHumidifier))
        {
            NeowEffectCardSnapshot[] strikes = effectAuthority.OrderedDeck.Where(card => card.IsBasic && card.IsStrike).ToArray();
            NeowEffectCardSnapshot[] defends = effectAuthority.OrderedDeck.Where(card => card.IsBasic && card.IsDefend).ToArray();
            leafyBonesInitialTransformInvariant = SameBasicSource(strikes, strikeKey) && SameBasicSource(defends, defendKey);
        }
        uint knownCapsules = !capsule && newOpeningStreams && bones
            ? NeowCapsuleNonCombatContinuation.Compile(request) : 0;
        if (!leafyBonesInitialTransformInvariant && hasLeafyPredicate && newOpeningStreams && bones &&
            request.Authority.PlayersCount == 1 && request.Authority.IsVanilla && !request.Authority.UsesBestEffortModel &&
            request.Authority.NoRunModifiers == true && request.Authority.IsBeta111NeowIdentityAuthorityExact &&
            authority.BonesAuthorityExact &&
            effectAuthority?.CapturedProfileId == RuntimeProfileId.Beta111 && effectAuthority.HasExactDeck &&
            effectAuthority.OrderedDeck is not null && effectAuthority.CharacterStrikeKey is { } modernStrike &&
            effectAuthority.CharacterDefendKey is { } modernDefend && PreservesModernLeafyInput())
        {
            var strikes = effectAuthority.OrderedDeck.Where(card => card.IsBasic && card.IsStrike).ToArray();
            var defends = effectAuthority.OrderedDeck.Where(card => card.IsBasic && card.IsDefend).ToArray();
            leafyBonesInitialTransformInvariant = SameBasicSource(strikes, modernStrike) && SameBasicSource(defends, modernDefend);
        }
        bool vanillaCapture = request.Authority.CanUseCurrentModel && request.Authority.NoRunModifiers == true &&
            effectAuthority?.CapturedProfileId == RuntimeProfileId.Beta111;
        bool directNestedVanilla111 = vanillaCapture && request.Authority.IsAuditedBeta111SourceContext;
        bool modernBones = !capsule && newOpeningStreams && bones;
        ulong capsules = Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.SmallCapsule) |
            Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.LargeCapsule);
        bool capsuleRoutePossible = (bannedBones & capsules) != capsules &&
            (System.Numerics.BitOperations.PopCount(requiredBones) < 2 || (requiredBones & capsules) != 0);
        bool observesNonCombat = required.Length != 0 || banned.Length != 0 ||
            (domains & (Beta110FastDomain.KaleidoscopeOffers | Beta110FastDomain.PhialHolsterPotions)) != 0;
        var (smallAdvances, largeAdvances, dualAdvances) = modernBones && capsuleRoutePossible && observesNonCombat
            ? NeowCapsuleNonCombatContinuation.CompileSoloAlternatives(request) : (0u, 0u, 0u);
        var plan = new NeowReplayPlan(authority, filter, domains, conditions.ToArray(), required, banned,
            Mask(filter.NeowRelics.Any), Mask(filter.NeowRelics.All) | (!bones ? requiredCapsules : 0), Mask(filter.NeowRelics.Ban),
            Mask(filter.BonesRelics.Any), requiredBones, bannedBones, selected,
            Id(filter.RequiredBonesAcquisitionOrder.ElementAtOrDefault(0)), Id(filter.RequiredBonesAcquisitionOrder.ElementAtOrDefault(1)),
            bones, filter.RequireNeowsBones, leafyBonesInitialTransformInvariant, exactOnly.ToArray())
        {
            AuthoredUpgrades = capsule || newOpeningStreams ? null : NeowAuthoredUpgradeContinuation.Compile(request),
            CapsulesWithKnownZeroNonCombatAdvance = modernBones ? knownCapsules : 0,
            SmallCapsuleNonCombatAdvances = smallAdvances,
            LargeCapsuleNonCombatAdvances = largeAdvances,
            DualCapsuleNonCombatAdvances = dualAdvances,
            CapsuleNicheContinuation = capsule || newOpeningStreams ? null : NeowCapsuleNicheContinuation.Compile(request, authority, bones, selected,
                Id(filter.RequiredBonesAcquisitionOrder.ElementAtOrDefault(0)), Id(filter.RequiredBonesAcquisitionOrder.ElementAtOrDefault(1)),
                requiredBones, bannedBones, conditions, finalCurse),
            CapsuleUpgradeUpperBound = request.Authority.PlayersCount > 1 && effectAuthority?.HasExactDeck == true
                ? checked(2 * (effectAuthority.OrderedDeck!.Count + 6)) : -1,
            DirectNestedVanilla111 = directNestedVanilla111,
            AuditedModern113IdentityReplay = vanillaCapture && newOpeningStreams
        };
        if (modernBones && NeowCapsuleNonCombatContinuation.NeedsRemainder(plan))
            plan = plan with { ExactOnly = [..plan.ExactOnly, NeowCapsuleNonCombatContinuation.Remainder] };
        if (modernBones && NeowModernTransformGate.TryCreate(plan, request.Authority) is { } transformGate)
            plan = plan with { ModernTransformGate = transformGate };
        if (!capsule && NeowCapsuleRarityGate.CanApply(request, plan))
            plan = plan with { HasCapsuleRarityGate = true };
        if (!capsule) plan = plan with { UnobservedRewardsDraws = NeowUnobservedRewards.Compile(plan) };
        return plan;

        bool PreservesModernLeafyInput()
        {
            ulong leafy = Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.LeafyPoultice);
            ulong other = requiredBones & ~leafy;
            if ((requiredBones & leafy) == 0 || !System.Numerics.BitOperations.IsPow2(other)) return false;
            byte companion = (byte)System.Numerics.BitOperations.TrailingZeroCount(other);
            if (companion is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule)
            {
                var snapshots = (effectAuthority!.SharedRelicPoolSource ?? [])
                    .Concat(effectAuthority.CharacterRelicPoolSource ?? []).ToLookup(relic => relic.RelicKey);
                int slots = companion == Beta110FastRelicCatalog.SmallCapsule ? 1 : 2;
                // Full bindings prove identity separately from the NC count.
                // These hooks leave the initial Basic sources and pool intact.
                return (knownCapsules & (uint)other) != 0 &&
                    request.Evaluation.StructuredNeowEffects.Any(condition =>
                    !IsGroupedCapsule(condition) && condition.SourceRelicKey == Beta110FastRelicCatalog.KeyOf(companion) &&
                    condition.Kind == (slots == 1 ? NeowStructuredConditionKind.ExactSingle : NeowStructuredConditionKind.ExactUnorderedPair) &&
                    condition.OutputKeys.Count == slots && condition.OutputKeys.Distinct().Count() == slots &&
                    condition.OutputKeys.All(key => snapshots.Contains(key) &&
                        snapshots[key].All(relic => NeowCapsuleNonCombatContinuation.NativeAdvance(relic) == 0)));
            }
            // 0.113 pickup code: deterministic resources, non-Basic card
            // additions, or in-place upgrades. None uses Transformations.
            // Removals and New Leaf keep their respective route models.
            return companion is Beta110FastRelicCatalog.CursedPearl or Beta110FastRelicCatalog.DowsingRod or
                Beta110FastRelicCatalog.HeftyTablet or Beta110FastRelicCatalog.NeowsSacrifice or
                Beta110FastRelicCatalog.SilkenTress or Beta110FastRelicCatalog.SilverCrucible or
                Beta110FastRelicCatalog.ArcaneScroll or Beta110FastRelicCatalog.BoomingConch or
                Beta110FastRelicCatalog.FishingRod or Beta110FastRelicCatalog.GoldenPearl or
                Beta110FastRelicCatalog.Kaleidoscope or Beta110FastRelicCatalog.LeadPaperweight or
                Beta110FastRelicCatalog.LostCoffer or Beta110FastRelicCatalog.NeowsTorment or
                Beta110FastRelicCatalog.PhialHolster or Beta110FastRelicCatalog.ScrollBoxes or
                Beta110FastRelicCatalog.WingedBoots or Beta110FastRelicCatalog.LavaRock or
                Beta110FastRelicCatalog.NeowsTalisman or Beta110FastRelicCatalog.NutritiousOyster or
                Beta110FastRelicCatalog.Pomander or Beta110FastRelicCatalog.StoneHumidifier;
        }

        static bool SameBasicSource(NeowEffectCardSnapshot[] cards, ModelKey expectedKey) =>
            cards.Length >= 3 && cards.All(card => card.CardKey == expectedKey &&
                string.Equals(card.PoolId, cards[0].PoolId, StringComparison.Ordinal));
    }
}
