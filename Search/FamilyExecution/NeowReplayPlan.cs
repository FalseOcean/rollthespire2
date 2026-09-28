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
    internal int SharedNicheDraws { get; init; }
    internal int SharedPotionDraws { get; init; }
    internal (int Niche, int Potions)[] SharedArrivals { get; init; } = [];
    internal int CapsuleUpgradeUpperBound { get; init; } = -1;
    internal NeowAuthoredUpgradeContinuation? AuthoredUpgrades { get; init; }
    internal bool HasFinalCurseFastProjection => (EnabledDomains & Beta110FastDomain.FinalCurse) != 0;

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
    internal static bool HasCapsule(ExactSearchExecutionRequest request) =>
        request.Evaluation.StructuredNeowEffects.Any(condition => !condition.IsEmpty && IsCapsule(condition)) ||
        request.Evaluation.EffectOutputConditions.Any(condition => !condition.IsEmpty && IsCapsule(condition.SourceRelicKey)) ||
        !request.Evaluation.CapsuleContainedRelics.IsEmpty || request.Evaluation.RequireWhetstone || request.Evaluation.RequireWarPaint;

    internal static NeowSearchFilter NeowFilter(ExactSearchExecutionRequest request, bool capsule)
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

    internal static NeowReplayPlan Compile(ExactSearchExecutionRequest request, bool capsule)
    {
        NeowSearchFilter filter = NeowFilter(request, capsule);
        if (!capsule) filter = filter with { StructuredNeowEffects = filter.StructuredNeowEffects
            .Concat(NeowChoiceCommitment.CombinedConditions(filter.StructuredNeowEffects)).ToArray() };
        if (capsule && filter.NeowRoute is null)
            throw new InvalidOperationException("RFamilyUnboundCapsuleRoute_ExactOnly");
        var exactOnly = new List<string>();
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
        Beta110FastEffectCatalog catalog = Beta110FastEffectCatalogCompiler.Compile(request.Authority.EffectAuthority,
            filter.StructuredNeowEffects.SelectMany(c => c.OutputKeys)
                .Concat(NeowNumericCompilation.EnumerateFinalCurseRequiredKeys(filter)).Concat(filter.BannedFinalCurses)
                .Concat(filter.CapsuleContainedRelics.Any).Concat(filter.CapsuleContainedRelics.All).Concat(filter.CapsuleContainedRelics.Ban));
        Beta110FastDomain domains = Beta110FastDomain.None;
        foreach (var condition in filter.StructuredNeowEffects)
        {
            if (condition.Scope == NeowStructuredEffectScope.FinalCurse) { supported.Add(condition); continue; }
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
            filter.RequiredBonesCombination.Count != 0 || !filter.BonesRelics.IsEmpty || finalCurse || filter.RequiredBonesAcquisitionOrder.Count != 0;
        if (!capsule && !bones && selected == Beta110FastRelicCatalog.InvalidId && conditions.Count > 0)
        {
            // Current UI authors result conditions only under a selected route.
            // Imported unbound rows remain Exact-owned, without a route-OR kernel.
            exactOnly.Add("UnboundStructuredRoute");
            conditions.Clear();
            domains = Beta110FastDomain.None;
        }
        var requestDto = new SearchExecutionRequest(request.CompiledSearch, request.RunOptions, request.CanonicalStartSeed,
            request.ResolvedScanCount, filter, request.CombatRewardRoutePolicy, request.SnapshotFingerprint);
        if (!capsule) catalog = catalog with { OrdinaryRelics = [], SharedRelicConsumeShuffleLengths = [], PlayerRelicBuckets = [], RelicBagAuthorityExact = false };
        var authority = NeowNumericCompilation.BuildAuthority(requestDto, catalog, bones);
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
                filter.BonesRelics.All.Concat(sources).Concat(new[] {
                    filter.RequireSmallCapsule ? BaseGameModelKeys.Relics.SmallCapsule : default,
                    filter.RequireLargeCapsule ? BaseGameModelKeys.Relics.LargeCapsule : default }.Where(k => k.IsValid))
                    .Distinct().ToArray(), filter.BonesRelics.Ban), RequireSmallCapsule = false, RequireLargeCapsule = false };
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
        return new(authority, filter, domains, conditions.ToArray(), required, banned,
            Mask(filter.NeowRelics.Any), Mask(filter.NeowRelics.All) | (!bones ? requiredCapsules : 0), Mask(filter.NeowRelics.Ban),
            Mask(filter.BonesRelics.Any), Mask(filter.BonesRelics.All.Concat(filter.RequiredBonesCombination)) | sourceMask | (bones ? requiredCapsules : 0),
            Mask(filter.BonesRelics.Ban), selected,
            Id(filter.RequiredBonesAcquisitionOrder.ElementAtOrDefault(0)), Id(filter.RequiredBonesAcquisitionOrder.ElementAtOrDefault(1)),
            bones, filter.RequireNeowsBones, leafyBonesInitialTransformInvariant, exactOnly.ToArray())
        {
            AuthoredUpgrades = capsule ? null : NeowAuthoredUpgradeContinuation.Compile(request),
            CapsuleUpgradeUpperBound = request.Authority.PlayersCount > 1 && effectAuthority?.HasExactDeck == true
                ? checked(2 * (effectAuthority.OrderedDeck!.Count + 6)) : -1,
            DirectNestedVanilla111 = request.Authority.CanUseCurrentModel && request.Authority.NoRunModifiers == true &&
                request.Authority.IsAuditedBeta111SourceContext && effectAuthority?.CapturedProfileId == RuntimeProfileId.Beta111
        };

        static bool SameBasicSource(NeowEffectCardSnapshot[] cards, ModelKey expectedKey) =>
            cards.Length >= 3 && cards.All(card => card.CardKey == expectedKey &&
                string.Equals(card.PoolId, cards[0].PoolId, StringComparison.Ordinal));
    }
}
