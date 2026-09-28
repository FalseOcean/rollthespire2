using RolltheSpire2.Core.Authority;
using System.Globalization;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

public static class SearchContextFactory
{
    public static SearchContext From(
        RuntimeProfileId profileId,
        ModelKey characterKey,
        int ascension,
        RuntimeContextAuthoritySnapshot authority,
        GameVersionDetection detection,
        AncientOptionConditionProfile ancientAssumptions)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(detection);
        return new SearchContext(
            profileId,
            characterKey,
            ascension,
            authority,
            detection,
            new SearchEvaluationAssumptions(ancientAssumptions));
    }
}

/// <summary>
/// The only pre-plan semantic compiler. It accepts business SearchQuery plus immutable
/// SearchContext and produces semantic-only CompiledSearch. No legacy execution DTO,
/// FastPlan, physical capability, runtime health, backend preference or cost evidence is
/// constructed or consulted here.
/// </summary>
public static class SearchCompiler
{
    public static CompiledSearch Compile(
        SearchQuery query,
        SearchContext context,
        ProductSemanticPolicy? productPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        ProductSemanticPolicy policy = productPolicy ?? ProductSemanticPolicy.Current;
        QueryNormalizationResult normalized = SearchQueryNormalizer.Normalize(query);
        ResolvedRouteSemantics routeSemantics = ResolveRouteSemantics(normalized.NormalizedQuery, policy);
        bool requiresComplexBonesDeckInteractionEvaluation = RequiresComplexBonesDeckInteractionEvaluation(normalized.NormalizedQuery);
        string fingerprint = BuildSemanticFingerprint(
            context,
            normalized.NormalizedQuery,
            routeSemantics,
            requiresComplexBonesDeckInteractionEvaluation);

        return new CompiledSearch(
            normalized.NormalizedQuery,
            context,
            normalized,
            routeSemantics,
            requiresComplexBonesDeckInteractionEvaluation,
            fingerprint);
    }

    private static ResolvedRouteSemantics ResolveRouteSemantics(
        SearchQuery query,
        ProductSemanticPolicy policy)
    {
        ResolvedCombatRewardRouteSemantics reward = !query.HasCombatRewardConstraints
            ? new ResolvedCombatRewardRouteSemantics(
                ResolvedCombatRewardRouteKind.NotApplicable,
                null,
                query.OpeningRoute is { IsValid: true },
                policy.PolicyId)
            : query.OpeningRoute is { IsValid: true } route
                ? new ResolvedCombatRewardRouteSemantics(
                    ResolvedCombatRewardRouteKind.PinnedOpeningRoute,
                    route.RouteRelicKey,
                    true,
                    policy.PolicyId)
                : new ResolvedCombatRewardRouteSemantics(
                    policy.UnpinnedCombatRewardRoute,
                    null,
                    false,
                    policy.PolicyId);

        return new ResolvedRouteSemantics(reward);
    }

    private static bool RequiresComplexBonesDeckInteractionEvaluation(SearchQuery query) =>
        query.LegacyNeow.RequireWhetstone || query.LegacyNeow.RequireWarPaint ||
        query.LegacyNeow.RequiredFinalCurse.HasValue ||
        query.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
        query.StructuredOpeningEffects.Count > 0;

    private static string BuildSemanticFingerprint(
        SearchContext context,
        SearchQuery query,
        ResolvedRouteSemantics routes,
        bool requiresComplexBonesDeckInteractionEvaluation)
    {
        static IEnumerable<ModelKey> Stable(IEnumerable<ModelKey> keys) => keys
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .OrderBy(key => key.Serialized, StringComparer.Ordinal);
        static string StableKeys(IEnumerable<ModelKey> keys) =>
            string.Join("+", Stable(keys).Select(key => key.Serialized));
        static string Keys(ModelKeySetFilter set) => string.Join(",", new[]
        {
            "any=" + StableKeys(set.Any),
            "all=" + StableKeys(set.All),
            "ban=" + StableKeys(set.Ban)
        });
        static string Sorted(IEnumerable<string> values) =>
            string.Join(";", values.OrderBy(value => value, StringComparer.Ordinal));

        IEnumerable<string> parts = new[]
        {
            "profile=" + context.ProfileId,
            "game=" + context.Detection.DisplayVersion,
            "character=" + context.CharacterKey.Serialized,
            "ascension=" + context.Ascension.ToString(CultureInfo.InvariantCulture),
            "unlock=" + context.UnlockSnapshotFingerprint,
            "catalog=" + context.CatalogFingerprint,
            "effect=" + context.EffectSnapshotFingerprint,
            "world=" + context.WorldSnapshotFingerprint,
            "ancientAssumptions=" + context.EvaluationAssumptions.AncientEligibilityAssumptions.Fingerprint,
            "routePolicy=" + routes.CombatReward.ProductPolicyId,
            "optionalChoicePolicy=" + NeowChoiceCommitment.PolicyId,
            "complexBonesEvaluation=" + requiresComplexBonesDeckInteractionEvaluation.ToString().ToLowerInvariant(),
            "rewardRoute=" + routes.CombatReward.Kind,
            "openingRoute=" + query.OpeningRoute?.RouteRelicKey.Serialized,
            "openingRouteRelics=" + (query.OpeningRouteRelicRequirement is { IsEmpty: false } routeRelics
                ? routeRelics.ParentRouteRelicKey.Serialized + ":" + routeRelics.OrderMode + ":" +
                  (routeRelics.OrderMode == BonesRouteOrderMode.ExactOrder
                      ? string.Join(">", routeRelics.RequiredRelicKeys.Select(key => key.Serialized))
                      : StableKeys(routeRelics.RequiredRelicKeys))
                : string.Empty),
            "structured=" + Sorted(query.StructuredOpeningEffects.Select(item =>
                $"{item.SourceRelicKey.Serialized}:{item.Kind}:{item.Scope}:{item.OutputKind}:" +
                $"{(item.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope && item.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder
                    ? string.Join(">", item.KaleidoscopePositionalSlots.Select(key => key?.Serialized ?? "*"))
                    : string.Join("+", item.OutputKeys.Select(key => key.Serialized)))}:" +
                $"{item.SpecialOffer}:{item.AllowDuplicateOutputs}:kaleOrder={item.KaleidoscopeGroupOrder}")),
            "variantBoss=" + Sorted(query.VariantBossBranches.Select(branch =>
                $"{branch.Act}:{branch.VariantKey.Serialized}:b1={Keys(branch.FirstBoss)}:b2={Keys(branch.SecondBoss)}:has2={branch.IncludesSecondBoss}")),
            "ancient=" + Sorted(query.AncientBranches.Select(branch =>
                $"{branch.Act}:{branch.AncientKey.Serialized}:opt={StableKeys(branch.OptionAny)}:sea={StableKeys(branch.SeaGlassTargetAny)}")),
            "relic=" + Sorted(query.RelicSequenceConstraints.Select(item =>
                $"{item.Lane}:{item.RangeMode}:{item.RangeValue}:{Keys(item.Keys)}")),
            "relicShopSequence=" + Sorted(query.RelicShopSequenceConditions.Select(item =>
                $"{item.Count}:{item.OrderMode}:{string.Join(">", item.Slots.Select(key => key?.Serialized ?? "*"))}")),
            "event=" + Sorted(query.EventSequenceConstraints.Select(item =>
                $"{item.Act}:{item.Source}:{item.RangeMode}:{item.RangeValue}:{Keys(item.Keys)}")),
            "eventResult=" + Sorted(query.EventResultConditions.Select(item =>
                $"{item.Kind}:{item.TargetKey.Serialized}")),
            "merchantColorless=" + Sorted(query.MerchantColorlessConditions.Select(item =>
                $"{item.MerchantOrdinal}:{item.Slot}:{item.TargetCardKey.Serialized}")),
            "merchantColorlessSequence=" + Sorted(query.MerchantColorlessSequenceConditions.Select(item =>
                $"{item.Count}:{item.OrderMode}:{item.Slot}:{string.Join(">", item.Slots.Select(key => key?.Serialized ?? "*"))}")),
            "combatCards=" + (query.CombatCardRewards is { IsEmpty: false } cards
                ? $"count={cards.Count}:order={cards.OrderMode}:slots={string.Join(">", cards.Slots.Select(key => key?.Serialized ?? "*"))}"
                : string.Empty),
            "combatPotions=" + (query.CombatPotionRewards is { IsEmpty: false } potions
                ? $"count={potions.Count}:order={potions.OrderMode}:slots={string.Join(">", potions.Slots.Select(slot => slot.Requirement + ":" + slot.PotionKey?.Serialized))}"
                : string.Empty),
            "legacyReward=" + Sorted(query.LegacyCombatRewardConstraints.Select(item =>
                $"{item.BattleOrdinal}:{Keys(item.Cards)}:{item.PotionRequirement}:{Keys(item.Potions)}:{item.MinimumGold}:{item.MaximumGold}")),
            "legacyNeow=" + Keys(query.LegacyNeow.NeowRelics),
            "legacyRequireBones=" + query.LegacyNeow.RequireNeowsBones,
            "legacyBones=" + Keys(query.LegacyNeow.BonesRelics),
            "legacyBonesCombination=" + StableKeys(query.LegacyNeow.RequiredBonesCombination),
            "legacyBonesOrder=" + string.Join(">", query.LegacyNeow.RequiredBonesAcquisitionOrder.Select(key => key.Serialized)),
            "legacySmallCapsule=" + query.LegacyNeow.RequireSmallCapsule,
            "legacyLargeCapsule=" + query.LegacyNeow.RequireLargeCapsule,
            "legacyCapsule=" + Keys(query.LegacyNeow.CapsuleContainedRelics),
            "legacyWhetstone=" + query.LegacyNeow.RequireWhetstone,
            "legacyWarPaint=" + query.LegacyNeow.RequireWarPaint,
            "legacyFinalCurse=" + query.LegacyNeow.RequiredFinalCurse?.Serialized,
            "legacyBanCurses=" + StableKeys(query.LegacyNeow.BannedFinalCurses),
            "legacyPreset=" + query.LegacyNeow.Preset,
            "legacyEffects=" + Sorted(query.LegacyNeow.EffectOutputConditions.Select(item => item.SourceRelicKey.Serialized + "=" + Keys(item.OutputKeys))),
            "legacyBoss=" + Sorted(query.LegacyWorld.BossFilters.Select(item => $"{item.Act}:{Keys(item.Keys)}")),
            "legacyBossOrdinal=" + Sorted(query.LegacyWorld.BossOrdinalFilters.Select(item => $"{item.Act}.{item.Ordinal}:{Keys(item.Keys)}")),
            "legacyAncient=" + Sorted(query.LegacyWorld.AncientIdentityFilters.Select(item => $"{item.Act}:{Keys(item.Keys)}")),
            "legacyAncientOption=" + Sorted(query.LegacyWorld.AncientOptionFilters.Select(item => $"{item.Act}:{Keys(item.Keys)}")),
            "legacySeaGlass=" + Sorted(query.LegacyWorld.AncientSeaGlassTargetFilters.Select(item => $"{item.Act}:{Keys(item.Keys)}"))
        };

        return string.Join("|", parts);
    }
}

internal static class SearchQueryNormalizer
{
    private static readonly ModelKey SeaGlassOptionKey = new(BaseGameModelKeys.Categories.Relic, "SEA_GLASS");
    private static readonly ModelKey PrayerWheel = new(BaseGameModelKeys.Categories.Relic, "PRAYER_WHEEL");
    private static readonly ModelKey WhiteBeastStatue = new(BaseGameModelKeys.Categories.Relic, "WHITE_BEAST_STATUE");
    private static readonly ModelKey LastingCandy = new(BaseGameModelKeys.Categories.Relic, "LASTING_CANDY");
    private static readonly ModelKey AmethystAubergine = new(BaseGameModelKeys.Categories.Relic, "AMETHYST_AUBERGINE");

    public static QueryNormalizationResult Normalize(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var removed = new List<string>();
        var implied = new List<string>();
        var diagnostics = new List<string>();
        var relations = new List<SemanticRelation>();
        bool impossible = false;

        ModelKeySetFilter NormalizeSet(ModelKeySetFilter input, string path, bool singleValue)
        {
            ModelKey[] any = StableKeys(input.Any);
            ModelKey[] all = StableKeys(input.All);
            ModelKey[] ban = StableKeys(input.Ban);

            if (all.Any(key => ban.Contains(key, ModelKeyComparer.Instance)))
            {
                impossible = true;
                diagnostics.Add(path + ":AllConflictsWithBan");
            }

            if (singleValue && all.Length > 1)
            {
                impossible = true;
                diagnostics.Add(path + ":SingleValueAllCardinalityConflict");
            }

            ModelKey[] permittedAny = any
                .Where(key => !ban.Contains(key, ModelKeyComparer.Instance))
                .ToArray();
            if (permittedAny.Length != any.Length)
            {
                removed.Add(path + ":AnyMembersBanned");
            }
            if (any.Length > 0 && permittedAny.Length == 0)
            {
                impossible = true;
                diagnostics.Add(path + ":AnyExhaustedByBan");
            }

            if (all.Length > 0 && permittedAny.Any(key => all.Contains(key, ModelKeyComparer.Instance)))
            {
                permittedAny = Array.Empty<ModelKey>();
                removed.Add(path + ":AnyImpliedByAll");
            }

            if (singleValue && all.Length == 1 && permittedAny.Length > 0 &&
                !permittedAny.Contains(all[0], ModelKeyComparer.Instance))
            {
                impossible = true;
                diagnostics.Add(path + ":SingleValueAnyConflictsWithAll");
            }

            return new ModelKeySetFilter(permittedAny, all, ban);
        }

        OpeningRouteRelicRequirement? openingRouteRelics = query.OpeningRouteRelicRequirement is { } routeRelics
            ? routeRelics with
            {
                RequiredRelicKeys = routeRelics.OrderMode == BonesRouteOrderMode.ExactOrder
                    ? routeRelics.RequiredRelicKeys.Where(key => key.IsValid).ToArray()
                    : StableKeys(routeRelics.RequiredRelicKeys)
            }
            : null;
        if (openingRouteRelics is { IsEmpty: false })
        {
            if (query.OpeningRoute is not { IsValid: true } opening ||
                opening.RouteRelicKey != openingRouteRelics.ParentRouteRelicKey)
            {
                impossible = true;
                diagnostics.Add("OpeningRouteRelicRequirementParentMismatch");
            }
        }
        if (openingRouteRelics is { IsEmpty: false, OrderMode: BonesRouteOrderMode.ExactOrder } exactBones)
        {
            if (exactBones.ParentRouteRelicKey != BaseGameModelKeys.Relics.NeowsBones ||
                exactBones.RequiredRelicKeys.Count != 2 ||
                exactBones.RequiredRelicKeys.Distinct(ModelKeyComparer.Instance).Count() != 2)
            {
                impossible = true;
                diagnostics.Add("BonesExactOrderRequiresTwoDistinctRouteRelics");
            }
        }

        NeowStructuredEffectSearchCondition[] structured = query.StructuredOpeningEffects
            .Where(item => item.SourceRelicKey.IsValid)
            .Select(item => NormalizeStructuredCondition(item))
            .Where(item => !item.IsEmpty)
            .Distinct()
            .OrderBy(item => item.SourceRelicKey.Serialized, StringComparer.Ordinal)
            .ThenBy(item => item.Scope)
            .ThenBy(item => item.Kind)
            .ToArray();

        foreach (NeowStructuredEffectSearchCondition item in structured)
        {
            if (item.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope &&
                item.Kind == NeowStructuredConditionKind.IndependentOfferGroupTargets &&
                item.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder)
            {
                if (item.KaleidoscopePositionalSlots.Count != 2 ||
                    !item.KaleidoscopePositionalSlots.Any(key => key.HasValue) ||
                    item.KaleidoscopePositionalSlots.Any(key => key.HasValue &&
                        (!key.Value.IsValid || key.Value.Category != BaseGameModelKeys.Categories.Card)))
                {
                    impossible = true;
                    diagnostics.Add("KaleidoscopeExactOrderRequiresTwoPositionalSlotsWithAtLeastOneTarget");
                }
            }
        }

        VariantScopedBossBranch[] variantBranches = query.VariantBossBranches
            .Where(branch => branch.IsValid)
            .Select(branch => branch with
            {
                FirstBoss = NormalizeSet(branch.FirstBoss, $"Variant[{branch.Act}:{branch.VariantKey.Serialized}].Boss1", singleValue: true),
                SecondBoss = NormalizeSet(branch.SecondBoss, $"Variant[{branch.Act}:{branch.VariantKey.Serialized}].Boss2", singleValue: true)
            })
            .Distinct()
            .OrderBy(branch => branch.Act)
            .ThenBy(branch => branch.VariantKey.Serialized, StringComparer.Ordinal)
            .ToArray();

        AncientSearchBranchCondition[] ancientBranches = query.AncientBranches
            .Where(branch => branch.IsValid)
            .Select(branch => branch with
            {
                OptionAny = StableKeys(branch.OptionAny),
                SeaGlassTargetAny = StableKeys(branch.SeaGlassTargetAny)
            })
            .Distinct()
            .OrderBy(branch => branch.Act)
            .ThenBy(branch => branch.AncientKey.Serialized, StringComparer.Ordinal)
            .ToArray();

        foreach (AncientSearchBranchCondition branch in ancientBranches)
        {
            if (branch.SeaGlassTargetAny.Count > 0 && !branch.OptionAny.Contains(SeaGlassOptionKey, ModelKeyComparer.Instance))
            {
                impossible = true;
                diagnostics.Add($"Ancient[{branch.Act}:{branch.AncientKey.Serialized}]:SeaGlassTargetWithoutSeaGlassOption");
            }
        }

        RelicSequenceSearchCondition[] relics = query.RelicSequenceConstraints
            .Where(item => !item.IsEmpty)
            .Select(item => item with
            {
                Keys = NormalizeSet(item.Keys, $"Relic[{item.Lane}:{item.RangeMode}:{item.RangeValue}]", item.RangeMode == SearchSequenceRangeMode.ExactSlot)
            })
            .Distinct()
            .OrderBy(item => item.Lane)
            .ThenBy(item => item.RangeMode)
            .ThenBy(item => item.RangeValue)
            .ToArray();

        EventSequenceSearchCondition[] events = query.EventSequenceConstraints
            .Where(item => !item.IsEmpty)
            .Select(item => item with
            {
                Keys = NormalizeSet(item.Keys, $"Event[{item.Act}:{item.Source}:{item.RangeMode}:{item.RangeValue}]", item.RangeMode == SearchSequenceRangeMode.ExactSlot)
            })
            .Distinct()
            .OrderBy(item => item.Act)
            .ThenBy(item => item.Source)
            .ThenBy(item => item.RangeMode)
            .ThenBy(item => item.RangeValue)
            .ToArray();

        NormalizeSequenceImplications(relics, "Relic", removed, implied, relations, ref impossible, diagnostics);
        NormalizeSequenceImplications(events, "Event", removed, implied, relations, ref impossible, diagnostics);
        relics = ReduceRelicSequenceRedundancies(relics, removed, implied);
        events = ReduceEventSequenceRedundancies(events, removed, implied);
        RelicShopSequenceSearchCondition[] relicShopSequences =
            NormalizeRelicShopSequences(query.RelicShopSequenceConditions, ref impossible, diagnostics);

        EventResultSearchCondition[] eventResults = query.EventResultConditions
            .Where(item => item.IsValid)
            .Distinct()
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.TargetKey.Serialized, StringComparer.Ordinal)
            .ToArray();
        if (query.EventResultConditions.Any(item => !item.IsValid))
        {
            impossible = true;
            diagnostics.Add("EventResultConditionInvalid");
        }

        var merchantBySlot = query.MerchantColorlessConditions
            .Where(item => item.IsValid)
            .GroupBy(item => (item.MerchantOrdinal, item.Slot))
            .OrderBy(group => group.Key.MerchantOrdinal)
            .ThenBy(group => group.Key.Slot)
            .ToArray();
        if (query.MerchantColorlessConditions.Any(item => !item.IsValid))
        {
            impossible = true;
            diagnostics.Add("MerchantColorlessConditionInvalid");
        }
        var merchantColorless = new List<MerchantColorlessSlotCondition>();
        foreach (IGrouping<(int MerchantOrdinal, MerchantColorlessSlot Slot), MerchantColorlessSlotCondition> group in merchantBySlot)
        {
            ModelKey[] targets = group.Select(item => item.TargetCardKey)
                .Distinct(ModelKeyComparer.Instance)
                .OrderBy(key => key.Serialized, StringComparer.Ordinal)
                .ToArray();
            if (targets.Length > 1)
            {
                impossible = true;
                diagnostics.Add($"MerchantColorlessSlotConflict:{group.Key.MerchantOrdinal}:{group.Key.Slot}");
                continue;
            }
            merchantColorless.Add(new MerchantColorlessSlotCondition(
                group.Key.MerchantOrdinal,
                group.Key.Slot,
                targets[0]));
        }

        MerchantColorlessSequenceSearchCondition[] merchantColorlessSequences =
            NormalizeMerchantColorlessSequences(query.MerchantColorlessSequenceConditions, ref impossible, diagnostics);

        CombatCardRewardSequenceSearchCondition? combatCards = NormalizeCombatCards(query.CombatCardRewards, ref impossible, diagnostics);
        CombatPotionRewardSequenceSearchCondition? combatPotions = NormalizeCombatPotions(query.CombatPotionRewards, ref impossible, diagnostics);

        NormalCombatRewardSearchCondition[] legacyRewards = query.LegacyCombatRewardConstraints
            .Where(item => !item.IsEmpty)
            .Select(item => item with
            {
                Cards = NormalizeSet(item.Cards, $"LegacyReward[{item.BattleOrdinal}].Cards", singleValue: false),
                Potions = NormalizeSet(item.Potions, $"LegacyReward[{item.BattleOrdinal}].Potions", singleValue: false)
            })
            .Distinct()
            .OrderBy(item => item.BattleOrdinal)
            .ToArray();

        EmitStructuralRelations(query.OpeningRoute, openingRouteRelics, structured, variantBranches, ancientBranches, query.LegacyNeow, query.LegacyWorld, relics, events, combatCards, combatPotions, legacyRewards, relations, ref impossible, diagnostics);

        var normalized = query with
        {
            OpeningRouteRelicRequirement = openingRouteRelics,
            StructuredOpeningEffects = structured,
            VariantBossBranches = variantBranches,
            AncientBranches = ancientBranches,
            RelicSequenceConstraints = relics,
            RelicShopSequenceConditions = relicShopSequences,
            EventSequenceConstraints = events,
            EventResultConditions = eventResults,
            MerchantColorlessConditions = merchantColorless.ToArray(),
            MerchantColorlessSequenceConditions = merchantColorlessSequences,
            LegacyCombatRewardConstraints = legacyRewards,
            CombatCardRewards = combatCards,
            CombatPotionRewards = combatPotions
        };

        return new QueryNormalizationResult(
            normalized,
            impossible ? QueryNormalizationStatus.Impossible : QueryNormalizationStatus.Legal,
            removed,
            implied,
            relations.Distinct().ToArray(),
            diagnostics);
    }

    private static RelicShopSequenceSearchCondition[] NormalizeRelicShopSequences(
        IReadOnlyList<RelicShopSequenceSearchCondition> source,
        ref bool impossible,
        List<string> diagnostics)
    {
        var output = new List<RelicShopSequenceSearchCondition>();
        foreach (RelicShopSequenceSearchCondition item in source)
        {
            if (item.IsEmpty) continue;
            if (item.Count is < 1 or > 5 || item.Slots.Count < item.Count ||
                item.OrderMode is not (CombatRewardSequenceOrderMode.Ordered or CombatRewardSequenceOrderMode.Unordered))
            {
                impossible = true;
                diagnostics.Add("RelicShopSequenceInvalid");
                continue;
            }
            ModelKey?[] slots = item.Slots.Take(item.Count).ToArray();
            if (slots.Any(key => key.HasValue &&
                                 (!key.Value.IsValid || key.Value.Category != BaseGameModelKeys.Categories.Relic)))
            {
                impossible = true;
                diagnostics.Add("RelicShopSequenceRelicCategoryInvalid");
                continue;
            }
            if (item.OrderMode == CombatRewardSequenceOrderMode.Unordered)
            {
                // Only Unordered discards authored slot positions. Ordered must
                // preserve nulls because they are positional Merchant wildcards.
                slots = slots.OrderBy(key => key?.Serialized ?? string.Empty, StringComparer.Ordinal).ToArray();
            }
            output.Add(item with { Slots = slots });
        }
        return output.Distinct().ToArray();
    }

    private static MerchantColorlessSequenceSearchCondition[] NormalizeMerchantColorlessSequences(
        IReadOnlyList<MerchantColorlessSequenceSearchCondition> source,
        ref bool impossible,
        List<string> diagnostics)
    {
        var output = new List<MerchantColorlessSequenceSearchCondition>();
        foreach (MerchantColorlessSequenceSearchCondition item in source)
        {
            if (item.IsEmpty) continue;
            if (item.Count is < 1 or > 5 || item.Slots.Count < item.Count ||
                item.Slot is not (MerchantColorlessSlot.Uncommon or MerchantColorlessSlot.Rare) ||
                item.OrderMode is not (CombatRewardSequenceOrderMode.Ordered or CombatRewardSequenceOrderMode.Unordered))
            {
                impossible = true;
                diagnostics.Add("MerchantColorlessSequenceInvalid");
                continue;
            }
            ModelKey?[] slots = item.Slots.Take(item.Count).ToArray();
            if (slots.Any(key => key.HasValue &&
                                 (!key.Value.IsValid || key.Value.Category != BaseGameModelKeys.Categories.Card)))
            {
                impossible = true;
                diagnostics.Add("MerchantColorlessSequenceCardCategoryInvalid");
                continue;
            }
            if (item.OrderMode == CombatRewardSequenceOrderMode.Unordered)
            {
                // Only Unordered discards authored slot positions. Ordered must
                // preserve nulls because they are positional Merchant wildcards.
                slots = slots.OrderBy(key => key?.Serialized ?? string.Empty, StringComparer.Ordinal).ToArray();
            }
            output.Add(item with { Slots = slots });
        }
        return output.Distinct().ToArray();
    }

    private static NeowStructuredEffectSearchCondition NormalizeStructuredCondition(NeowStructuredEffectSearchCondition item)
    {
        ModelKey[] outputs = NormalizeStructuredOutputs(item);
        if (item.SourceRelicKey != BaseGameModelKeys.Relics.Kaleidoscope ||
            item.Kind != NeowStructuredConditionKind.IndependentOfferGroupTargets ||
            item.KaleidoscopeGroupOrder != KaleidoscopeGroupOrderMode.ExactOrder)
        {
            return item with
            {
                OutputKeys = outputs,
                KaleidoscopePositionalSlots = Array.Empty<ModelKey?>()
            };
        }

        ModelKey?[] slots = item.KaleidoscopePositionalSlots.Count > 0
            ? item.KaleidoscopePositionalSlots.Take(2)
                .Select(key => key is { IsValid: true } ? key : null)
                .Concat(Enumerable.Repeat<ModelKey?>(null, Math.Max(0, 2 - item.KaleidoscopePositionalSlots.Count)))
                .Take(2)
                .ToArray()
            : outputs.Take(2).Select(key => (ModelKey?)key)
                .Concat(Enumerable.Repeat<ModelKey?>(null, Math.Max(0, 2 - outputs.Length)))
                .Take(2)
                .ToArray();
        ModelKey[] concrete = slots.Where(key => key.HasValue).Select(key => key!.Value).ToArray();
        return item with
        {
            OutputKeys = concrete,
            KaleidoscopePositionalSlots = slots
        };
    }

    private static ModelKey[] NormalizeStructuredOutputs(NeowStructuredEffectSearchCondition item)
    {
        ModelKey[] valid = item.OutputKeys.Where(key => key.IsValid).ToArray();
        if (item.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope)
        {
            return item.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder
                ? valid
                : valid.OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();
        }
        if (item.AllowDuplicateOutputs)
            return valid.OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();
        return StableKeys(valid);
    }

    private static CombatCardRewardSequenceSearchCondition? NormalizeCombatCards(
        CombatCardRewardSequenceSearchCondition? input,
        ref bool impossible,
        ICollection<string> diagnostics)
    {
        if (input is null || input.IsEmpty) return null;
        ModelKey?[] slots = input.Slots.Take(Math.Max(0, input.Count)).ToArray();
        if (input.Count is < 1 or > 3 || slots.Length != input.Count ||
            slots.Any(key => key.HasValue && (!key.Value.IsValid || key.Value.Category != BaseGameModelKeys.Categories.Card)))
        {
            impossible = true;
            diagnostics.Add("CombatCardSequenceInvalid");
            return input;
        }
        if (input.OrderMode == CombatRewardSequenceOrderMode.Unordered)
        {
            ModelKey?[] concrete = slots.Where(key => key.HasValue)
                .OrderBy(key => key!.Value.Serialized, StringComparer.Ordinal)
                .Concat(Enumerable.Repeat<ModelKey?>(null, slots.Count(key => !key.HasValue)))
                .ToArray();
            slots = concrete;
        }
        return input with { Slots = slots };
    }

    private static CombatPotionRewardSequenceSearchCondition? NormalizeCombatPotions(
        CombatPotionRewardSequenceSearchCondition? input,
        ref bool impossible,
        ICollection<string> diagnostics)
    {
        if (input is null || input.IsEmpty) return null;
        CombatPotionRewardSlotSearchCondition[] slots = input.Slots.Take(Math.Max(0, input.Count)).ToArray();
        if (input.Count is < 1 or > 3 || slots.Length != input.Count || slots.Any(slot => !slot.IsValid))
        {
            impossible = true;
            diagnostics.Add("CombatPotionSequenceInvalid");
            return input;
        }
        if (input.OrderMode == CombatRewardSequenceOrderMode.Unordered)
        {
            slots = slots.Where(slot => !slot.IsNeutral)
                .OrderBy(slot => slot.Requirement)
                .ThenBy(slot => slot.PotionKey?.Serialized ?? string.Empty, StringComparer.Ordinal)
                .Concat(Enumerable.Repeat(
                    new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null),
                    slots.Count(slot => slot.IsNeutral)))
                .ToArray();
        }
        return input with { Slots = slots };
    }

    private static void EmitStructuralRelations(
        NeowRouteSearchCondition? openingRoute,
        OpeningRouteRelicRequirement? openingRouteRelics,
        IReadOnlyList<NeowStructuredEffectSearchCondition> structured,
        IReadOnlyList<VariantScopedBossBranch> variantBranches,
        IReadOnlyList<AncientSearchBranchCondition> ancientBranches,
        LegacyNeowSemanticConstraints legacyNeow,
        LegacyWorldSemanticConstraints legacyWorld,
        IReadOnlyList<RelicSequenceSearchCondition> relics,
        IReadOnlyList<EventSequenceSearchCondition> events,
        CombatCardRewardSequenceSearchCondition? combatCards,
        CombatPotionRewardSequenceSearchCondition? combatPotions,
        IReadOnlyList<NormalCombatRewardSearchCondition> rewards,
        ICollection<SemanticRelation> relations,
        ref bool impossible,
        ICollection<string> diagnostics)
    {
        if (openingRoute is { IsValid: true } route && openingRouteRelics is { IsEmpty: false })
        {
            relations.Add(new SemanticRelation(
                SemanticRelationKind.ParentScoped,
                new SemanticFactRef("Opening.Route:" + route.RouteRelicKey.Serialized, SemanticFactKind.OpeningRoute, Key: route.RouteRelicKey),
                new SemanticFactRef("Opening.RouteRelics:" + route.RouteRelicKey.Serialized, SemanticFactKind.OpeningRouteRelicRequirement, Key: route.RouteRelicKey),
                "Route-local relic requirements are interpreted only under the selected opening route."));
        }

        foreach (RelicSequenceSearchCondition relic in relics)
        {
            relations.Add(new SemanticRelation(
                SemanticRelationKind.SequenceMembership,
                new SemanticFactRef($"Relic.{relic.Lane}.{relic.RangeMode}.{relic.RangeValue}", SemanticFactKind.RelicGrabBag, Ordinal: relic.RangeValue),
                new SemanticFactRef($"Relic.Sequence.{relic.Lane}", SemanticFactKind.RelicGrabBag),
                "Relic sequence predicates constrain the same ordered rarity-lane RelicGrabBag sequence."));
        }

        foreach (EventSequenceSearchCondition evt in events)
        {
            relations.Add(new SemanticRelation(
                SemanticRelationKind.SequenceMembership,
                new SemanticFactRef($"Event.Act{evt.Act}.{evt.Source}.{evt.RangeMode}.{evt.RangeValue}", SemanticFactKind.EventSequence, evt.Act, evt.RangeValue),
                new SemanticFactRef($"Event.Act{evt.Act}.EffectiveQueue", SemanticFactKind.EventSequence, evt.Act),
                "Event predicates use absolute ordinals in the effective cleaned Event Queue."));
        }

        foreach (VariantScopedBossBranch branch in variantBranches)
        {
            var parent = new SemanticFactRef($"Act{branch.Act}.Variant:{branch.VariantKey.Serialized}", SemanticFactKind.ActVariant, branch.Act, null, branch.VariantKey);
            var boss1 = new SemanticFactRef($"Act{branch.Act}.Boss.1", SemanticFactKind.Boss, branch.Act, 1);
            relations.Add(new SemanticRelation(SemanticRelationKind.ParentScoped, parent, boss1, "Boss predicate is interpreted under the selected Act Variant branch."));
            if (branch.IncludesSecondBoss)
            {
                relations.Add(new SemanticRelation(
                    SemanticRelationKind.ParentScoped,
                    parent,
                    new SemanticFactRef($"Act{branch.Act}.Boss.2", SemanticFactKind.Boss, branch.Act, 2),
                    "Second Boss predicate is interpreted under the same selected Act Variant branch."));
            }

            if (events.Any(item => item.Act == branch.Act))
            {
                relations.Add(new SemanticRelation(
                    SemanticRelationKind.SharedParent,
                    parent,
                    new SemanticFactRef($"Act{branch.Act}.EventQueue", SemanticFactKind.EventSequence, branch.Act),
                    "Boss and Event facts share the selected Act Variant parent."));
            }
        }

        int[] legacyBossEventActs = legacyWorld.BossFilters
            .Where(item => !item.IsEmpty)
            .Select(item => item.Act)
            .Concat(legacyWorld.BossOrdinalFilters.Where(item => !item.IsEmpty).Select(item => item.Act))
            .Intersect(events.Select(item => item.Act))
            .Where(act => !variantBranches.Any(branch => branch.Act == act))
            .Distinct()
            .OrderBy(act => act)
            .ToArray();
        foreach (int act in legacyBossEventActs)
        {
            relations.Add(new SemanticRelation(
                SemanticRelationKind.SharedParent,
                new SemanticFactRef($"Act{act}.LegacyBossConstraint", SemanticFactKind.LegacyFlatBossConstraint, act),
                new SemanticFactRef($"Act{act}.EventQueue", SemanticFactKind.EventSequence, act),
                "Legacy flat Boss and Event constraints still share the runtime Act Variant parent; no lost Variant branch attribution is reconstructed."));
        }

        foreach (AncientSearchBranchCondition branch in ancientBranches)
        {
            var parent = new SemanticFactRef($"Act{branch.Act}.Ancient:{branch.AncientKey.Serialized}", SemanticFactKind.AncientIdentity, branch.Act, null, branch.AncientKey);
            if (branch.OptionAny.Count > 0)
            {
                var option = new SemanticFactRef($"Act{branch.Act}.AncientOption:{branch.AncientKey.Serialized}", SemanticFactKind.AncientOption, branch.Act, null, branch.AncientKey);
                relations.Add(new SemanticRelation(SemanticRelationKind.ParentScoped, parent, option, "Ancient option predicates are scoped to their Ancient identity."));
                if (branch.SeaGlassTargetAny.Count > 0)
                {
                    relations.Add(new SemanticRelation(
                        SemanticRelationKind.ParentScoped,
                        option,
                        new SemanticFactRef($"Act{branch.Act}.SeaGlassTarget:{branch.AncientKey.Serialized}", SemanticFactKind.SeaGlassTarget, branch.Act),
                        "Sea Glass target predicates are scoped to the Sea Glass option under the selected Ancient."));
                }
            }
        }

        AncientSearchBranchCondition[] act2 = ancientBranches.Where(item => item.Act == 2).ToArray();
        AncientSearchBranchCondition[] act3 = ancientBranches.Where(item => item.Act == 3).ToArray();
        bool act2Darv = act2.Any(item => string.Equals(item.AncientKey.Entry, "DARV", StringComparison.Ordinal));
        bool act3Darv = act3.Any(item => string.Equals(item.AncientKey.Entry, "DARV", StringComparison.Ordinal));
        if (act2Darv && act3Darv)
        {
            relations.Add(new SemanticRelation(
                SemanticRelationKind.SharedState,
                new SemanticFactRef("Act2.Ancient.DARV", SemanticFactKind.AncientIdentity, 2),
                new SemanticFactRef("Act3.Ancient.DARV", SemanticFactKind.AncientIdentity, 3),
                "Act2 and Act3 Ancient identities share the Darv assignment state."));

            if (act2.Length > 0 && act2.All(item => string.Equals(item.AncientKey.Entry, "DARV", StringComparison.Ordinal)) &&
                act3.Length > 0 && act3.All(item => string.Equals(item.AncientKey.Entry, "DARV", StringComparison.Ordinal)))
            {
                impossible = true;
                diagnostics.Add("DarvCannotBeRequiredAsBothAct2AndAct3Ancient");
                relations.Add(new SemanticRelation(
                    SemanticRelationKind.Conflicts,
                    new SemanticFactRef("Act2.Ancient.DARV", SemanticFactKind.AncientIdentity, 2),
                    new SemanticFactRef("Act3.Ancient.DARV", SemanticFactKind.AncientIdentity, 3),
                    "The shared Darv assignment cannot satisfy both Act2 and Act3 simultaneously."));
            }
        }

        bool capsuleNested = structured.Any(item =>
            item.Scope == NeowStructuredEffectScope.NestedRelics &&
            item.OutputKind == NeowStructuredOutputKind.Relic &&
            (item.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule || item.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule ||
             item.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones && item.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset));
        bool hasCapsuleConsumedRelicLane = relics.Any(condition =>
            condition.Lane is RelicSequenceKind.Common or RelicSequenceKind.Uncommon or RelicSequenceKind.Rare);
        if (capsuleNested && hasCapsuleConsumedRelicLane)
        {
            var capsule = new SemanticFactRef("Opening.CapsuleNestedRelic", SemanticFactKind.CapsuleNestedRelic);
            var bag = new SemanticFactRef("Initial.RelicGrabBag.CUR", SemanticFactKind.RelicGrabBag);
            relations.Add(new SemanticRelation(
                SemanticRelationKind.SameFact,
                capsule,
                bag,
                "Capsule nested relics and explicit Common/Uncommon/Rare Relic Queue filters observe the same runtime-consumed initial player RelicGrabBag lanes; Shop is not runtime-consumed by Capsule."));
            relations.Add(new SemanticRelation(
                SemanticRelationKind.SequenceMembership,
                capsule,
                bag,
                "Capsule pulls occupy positions only in the Common/Uncommon/Rare lane sequences exposed by overlapping Relic Queue constraints; Shop remains an independent runtime lane remainder."));
        }

        if (combatCards is { IsEmpty: false } || combatPotions is { IsEmpty: false } || rewards.Count > 0)
        {
            var reward = new SemanticFactRef("CombatReward.Opening3", SemanticFactKind.CombatReward);
            relations.Add(new SemanticRelation(
                SemanticRelationKind.RouteScoped,
                reward,
                new SemanticFactRef("Resolved.CombatRewardRoute", SemanticFactKind.ResolvedRewardRoute),
                "Combat Reward facts are interpreted on the resolved Product reward route."));

            foreach (ModelKey influence in EnumerateExplicitRewardInfluenceRelics(openingRoute, openingRouteRelics, structured, legacyNeow))
            {
                relations.Add(new SemanticRelation(
                    SemanticRelationKind.StateInfluence,
                    new SemanticFactRef("Relic:" + influence.Serialized, SemanticFactKind.RewardInfluenceRelic, Key: influence),
                    reward,
                    "This opening relic deterministically changes Reward topology or Reward state on an explicitly selected route."));
            }
        }
    }

    private static IEnumerable<ModelKey> EnumerateExplicitRewardInfluenceRelics(
        NeowRouteSearchCondition? openingRoute,
        OpeningRouteRelicRequirement? openingRouteRelics,
        IEnumerable<NeowStructuredEffectSearchCondition> structured,
        LegacyNeowSemanticConstraints legacyNeow)
    {
        ModelKey[] influence = { PrayerWheel, WhiteBeastStatue, LastingCandy, AmethystAubergine };
        var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);

        IEnumerable<ModelKey> candidates =
            (openingRoute is { IsValid: true } route ? new[] { route.RouteRelicKey } : Array.Empty<ModelKey>())
            .Concat(openingRouteRelics is { IsEmpty: false } routeRelics
                ? routeRelics.RequiredRelicKeys
                : Array.Empty<ModelKey>())
            .Concat(structured.SelectMany(condition => condition.OutputKeys))
            // Compatibility-only exact Bones facts retain provenance as legacy,
            // but their deterministic Reward influence is still a valid relation.
            .Concat(legacyNeow.RequiredBonesCombination)
            .Concat(legacyNeow.RequiredBonesAcquisitionOrder);

        foreach (ModelKey key in candidates)
        {
            if (influence.Contains(key, ModelKeyComparer.Instance) && seen.Add(key))
                yield return key;
        }
    }

    private static RelicSequenceSearchCondition[] ReduceRelicSequenceRedundancies(
        IReadOnlyList<RelicSequenceSearchCondition> conditions,
        ICollection<string> removed,
        ICollection<string> implied)
    {
        var output = new List<RelicSequenceSearchCondition>(conditions.Count);
        foreach (RelicSequenceSearchCondition condition in conditions)
        {
            ModelKeySetFilter keys = condition.Keys;
            if (condition.RangeMode == SearchSequenceRangeMode.FirstN)
            {
                ModelKey[] any = keys.Any.ToArray();
                ModelKey[] all = keys.All.ToArray();
                ModelKey[] ban = keys.Ban.ToArray();

                foreach (RelicSequenceSearchCondition slot in conditions.Where(item =>
                             item.Lane == condition.Lane &&
                             item.RangeMode == SearchSequenceRangeMode.ExactSlot &&
                             item.RangeValue <= condition.RangeValue))
                {
                    ModelKey? exact = ExactSingleValue(slot.Keys);
                    if (!exact.HasValue) continue;
                    if (any.Contains(exact.Value, ModelKeyComparer.Instance))
                    {
                        any = Array.Empty<ModelKey>();
                        removed.Add($"Relic:Relic.{condition.Lane}:Prefix{condition.RangeValue}.Any redundant under Slot{slot.RangeValue}={exact.Value.Serialized}");
                    }
                    if (all.Contains(exact.Value, ModelKeyComparer.Instance))
                    {
                        all = all.Where(key => key != exact.Value).ToArray();
                        removed.Add($"Relic:Relic.{condition.Lane}:Prefix{condition.RangeValue}.All({exact.Value.Serialized}) redundant under Slot{slot.RangeValue}");
                        implied.Add($"Relic:Relic.{condition.Lane}:Slot{slot.RangeValue}={exact.Value.Serialized} implies Prefix{condition.RangeValue}.All({exact.Value.Serialized})");
                    }
                }

                foreach (RelicSequenceSearchCondition larger in conditions.Where(item =>
                             item.Lane == condition.Lane &&
                             item.RangeMode == SearchSequenceRangeMode.FirstN &&
                             item.RangeValue > condition.RangeValue))
                {
                    ban = ban.Where(key => !larger.Keys.Ban.Contains(key, ModelKeyComparer.Instance)).ToArray();
                }
                keys = new ModelKeySetFilter(any, all, ban);
            }

            if (!keys.IsEmpty)
                output.Add(condition with { Keys = keys });
        }
        return output.Distinct().OrderBy(item => item.Lane).ThenBy(item => item.RangeMode).ThenBy(item => item.RangeValue).ToArray();
    }

    private static EventSequenceSearchCondition[] ReduceEventSequenceRedundancies(
        IReadOnlyList<EventSequenceSearchCondition> conditions,
        ICollection<string> removed,
        ICollection<string> implied)
    {
        var output = new List<EventSequenceSearchCondition>(conditions.Count);
        foreach (EventSequenceSearchCondition condition in conditions)
        {
            ModelKeySetFilter keys = condition.Keys;
            if (condition.RangeMode == SearchSequenceRangeMode.FirstN)
            {
                ModelKey[] any = keys.Any.ToArray();
                ModelKey[] all = keys.All.ToArray();
                ModelKey[] ban = keys.Ban.ToArray();

                foreach (EventSequenceSearchCondition slot in conditions.Where(item =>
                             item.Act == condition.Act &&
                             item.Source == condition.Source &&
                             item.RangeMode == SearchSequenceRangeMode.ExactSlot &&
                             item.RangeValue <= condition.RangeValue))
                {
                    ModelKey? exact = ExactSingleValue(slot.Keys);
                    if (!exact.HasValue) continue;
                    if (any.Contains(exact.Value, ModelKeyComparer.Instance))
                    {
                        any = Array.Empty<ModelKey>();
                        removed.Add($"Event:Event.Act{condition.Act}.{condition.Source?.ToString() ?? "AnySource"}:Prefix{condition.RangeValue}.Any redundant under Slot{slot.RangeValue}={exact.Value.Serialized}");
                    }
                    if (all.Contains(exact.Value, ModelKeyComparer.Instance))
                    {
                        all = all.Where(key => key != exact.Value).ToArray();
                        removed.Add($"Event:Event.Act{condition.Act}.{condition.Source?.ToString() ?? "AnySource"}:Prefix{condition.RangeValue}.All({exact.Value.Serialized}) redundant under Slot{slot.RangeValue}");
                        implied.Add($"Event:Event.Act{condition.Act}.{condition.Source?.ToString() ?? "AnySource"}:Slot{slot.RangeValue}={exact.Value.Serialized} implies Prefix{condition.RangeValue}.All({exact.Value.Serialized})");
                    }
                }

                foreach (EventSequenceSearchCondition larger in conditions.Where(item =>
                             item.Act == condition.Act &&
                             item.Source == condition.Source &&
                             item.RangeMode == SearchSequenceRangeMode.FirstN &&
                             item.RangeValue > condition.RangeValue))
                {
                    ban = ban.Where(key => !larger.Keys.Ban.Contains(key, ModelKeyComparer.Instance)).ToArray();
                }
                keys = new ModelKeySetFilter(any, all, ban);
            }

            if (!keys.IsEmpty)
                output.Add(condition with { Keys = keys });
        }
        return output.Distinct().OrderBy(item => item.Act).ThenBy(item => item.Source).ThenBy(item => item.RangeMode).ThenBy(item => item.RangeValue).ToArray();
    }

    private static void NormalizeSequenceImplications<T>(
        IReadOnlyList<T> conditions,
        string family,
        ICollection<string> removed,
        ICollection<string> implied,
        ICollection<SemanticRelation> relations,
        ref bool impossible,
        ICollection<string> diagnostics)
    {
        SemanticFactKind sequenceFactKind = string.Equals(family, "Event", StringComparison.Ordinal)
            ? SemanticFactKind.EventSequence
            : SemanticFactKind.RelicGrabBag;
        bool sequenceImpossible = false;

        for (int i = 0; i < conditions.Count; i++)
        {
            (string sequenceIdA, SearchSequenceRangeMode modeA, int rangeA, ModelKeySetFilter keysA) = SequenceView(conditions[i]);
            for (int j = i + 1; j < conditions.Count; j++)
            {
                (string sequenceIdB, SearchSequenceRangeMode modeB, int rangeB, ModelKeySetFilter keysB) = SequenceView(conditions[j]);
                if (!string.Equals(sequenceIdA, sequenceIdB, StringComparison.Ordinal)) continue;

                if (modeA == SearchSequenceRangeMode.ExactSlot && modeB == SearchSequenceRangeMode.FirstN && rangeA <= rangeB)
                {
                    CheckSlotVsPrefix(sequenceIdA, rangeA, keysA, rangeB, keysB);
                }
                else if (modeB == SearchSequenceRangeMode.ExactSlot && modeA == SearchSequenceRangeMode.FirstN && rangeB <= rangeA)
                {
                    CheckSlotVsPrefix(sequenceIdA, rangeB, keysB, rangeA, keysA);
                }

                if (modeA == SearchSequenceRangeMode.FirstN && modeB == SearchSequenceRangeMode.FirstN)
                {
                    CheckPrefixBanImplication(sequenceIdA, rangeA, keysA, rangeB, keysB);
                }
            }
        }

        if (sequenceImpossible)
            impossible = true;

        void CheckSlotVsPrefix(string sequenceId, int slot, ModelKeySetFilter slotKeys, int prefix, ModelKeySetFilter prefixKeys)
        {
            ModelKey? exact = ExactSingleValue(slotKeys);
            if (!exact.HasValue) return;

            if (prefixKeys.Ban.Contains(exact.Value, ModelKeyComparer.Instance))
            {
                sequenceImpossible = true;
                diagnostics.Add($"{family}:{sequenceId}:Slot{slot}ConflictsWithPrefix{prefix}Ban:{exact.Value.Serialized}");
                relations.Add(new SemanticRelation(
                    SemanticRelationKind.Conflicts,
                    new SemanticFactRef($"{family}:{sequenceId}:Slot{slot}", sequenceFactKind, Ordinal: slot, Key: exact),
                    new SemanticFactRef($"{family}:{sequenceId}:Prefix{prefix}", sequenceFactKind, Ordinal: prefix),
                    "Exact slot identity is banned by an enclosing prefix."));
            }
            if (prefixKeys.Any.Contains(exact.Value, ModelKeyComparer.Instance))
            {
                implied.Add($"{family}:{sequenceId}:Slot{slot}={exact.Value.Serialized} implies Prefix{prefix}.Any");
                relations.Add(new SemanticRelation(
                    SemanticRelationKind.Implies,
                    new SemanticFactRef($"{family}:{sequenceId}:Slot{slot}", sequenceFactKind, Ordinal: slot, Key: exact),
                    new SemanticFactRef($"{family}:{sequenceId}:Prefix{prefix}.Any", sequenceFactKind, Ordinal: prefix),
                    "An exact slot inside the prefix satisfies the prefix Any predicate."));
            }
        }

        void CheckPrefixBanImplication(string sequenceId, int rangeA, ModelKeySetFilter keysA, int rangeB, ModelKeySetFilter keysB)
        {
            if (rangeA == rangeB) return;
            int larger = Math.Max(rangeA, rangeB);
            int smaller = Math.Min(rangeA, rangeB);
            ModelKeySetFilter largerKeys = rangeA == larger ? keysA : keysB;
            ModelKeySetFilter smallerKeys = rangeA == smaller ? keysA : keysB;
            foreach (ModelKey key in smallerKeys.Ban)
            {
                if (!largerKeys.Ban.Contains(key, ModelKeyComparer.Instance)) continue;
                removed.Add($"{family}:{sequenceId}:Prefix{smaller}.Ban({key.Serialized}) redundant under Prefix{larger}.Ban");
                implied.Add($"{family}:{sequenceId}:Prefix{larger}.Ban({key.Serialized}) implies Prefix{smaller}.Ban");
            }
        }
    }

    private static (string SequenceId, SearchSequenceRangeMode Mode, int Range, ModelKeySetFilter Keys) SequenceView<T>(T condition) => condition switch
    {
        RelicSequenceSearchCondition relic => ("Relic." + relic.Lane, relic.RangeMode, relic.RangeValue, relic.Keys),
        EventSequenceSearchCondition evt => ($"Event.Act{evt.Act}.{evt.Source?.ToString() ?? "AnySource"}", evt.RangeMode, evt.RangeValue, evt.Keys),
        _ => throw new ArgumentOutOfRangeException(nameof(condition))
    };

    private static ModelKey? ExactSingleValue(ModelKeySetFilter keys)
    {
        if (keys.All.Count == 1) return keys.All[0];
        if (keys.All.Count == 0 && keys.Any.Count == 1) return keys.Any[0];
        return null;
    }

    private static ModelKey[] StableKeys(IEnumerable<ModelKey> keys) => keys
        .Where(key => key.IsValid)
        .Distinct(ModelKeyComparer.Instance)
        .OrderBy(key => key.Serialized, StringComparer.Ordinal)
        .ToArray();
}
