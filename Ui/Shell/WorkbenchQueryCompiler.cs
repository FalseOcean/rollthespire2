using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.BossMap;

namespace RolltheSpire2.Ui.Shell;

// Current query compilation and saved legacy draft import. No display/controller state.
internal static class WorkbenchQueryCompiler
{
    internal static CompiledSearch CompileAuthoredDraft(SearchDraft draft, ModRuntimeSnapshot runtime,
        ModelKey characterKey, int ascension, out RuntimeContextAuthoritySnapshot authority)
    {
        BuildSearchQuery(draft, out SearchQuery query, out AncientOptionConditionProfile ancientAssumptions);
        query = query with { TransformationAggregate = draft.TransformationAggregate };
        if ((draft.MorphicGroveContainsCard ?? draft.MorphicGroveSecondCard) is { } card)
            query = query with { EventResultConditions = query.EventResultConditions.Append(new EventResultSearchCondition(
                EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, card) {
                MorphicGroveSecondCard = draft.MorphicGroveContainsCard.HasValue ? draft.MorphicGroveSecondCard : null
            }).ToArray() };
        return CompileAuthoredQuery(query, ancientAssumptions, runtime, characterKey, ascension, out authority);
    }

    // Capture immutable simulation authority; never modify the real run.
    internal static CompiledSearch CompileAuthoredQuery(SearchQuery query, AncientOptionConditionProfile ancientAssumptions,
        ModRuntimeSnapshot runtime, ModelKey characterKey, int ascension, out RuntimeContextAuthoritySnapshot authority)
    {
        CharacterIdentity character = CharacterIdentity.FromKey(characterKey);
        string contextAuthoritySeed = ProfileSeedGenerator.CreateProbeSeed(runtime.Profile);
        authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
            runtime.Profile, contextAuthoritySeed, character, ascension, runtime.Detection.DisplayVersion,
            playersCount: 1, playerSlotIndex: 0,
            predictionGameMode: WorldGameMode.Singleplayer,
            predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
        SearchContext context = SearchContextFactory.From(
            runtime.Profile.ProfileId, characterKey, ascension, authority, runtime.Detection, ancientAssumptions);
        var capturedAuthority = authority;
        query = query with { EventResultConditions = query.EventResultConditions.Select(c =>
            EventResultTransformSemantics.IsTransform(c.Kind)
                ? c with { MorphicGroveScenario = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredEventResult(capturedAuthority, c.Kind) }
                : c).ToArray() };
        if (query.TransformationAggregate is { } aggregate)
            query = query with { TransformationAggregate = aggregate with { EventScenario = aggregate.UsesEvents
                ? CaptureAggregateEventPremises(authority, aggregate) : null } };
        RolltheSpire2.Infrastructure.Snapshots.ProductionSearchReplay.ExportIfRequested(context, query);
        return SearchCompiler.Compile(query, context);
    }

    private static RolltheSpire2.Core.Prediction.MorphicGroveScenario CaptureAggregateEventPremises(
        RuntimeContextAuthoritySnapshot authority, TransformationAggregateCondition aggregate)
    {
        var source = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredBasics(authority, aggregate.MorphicGrove || aggregate.TrialNondescript ? 2 : 1);
        return new(source.Authority, source.Premises with { EventOccurrenceBasis =
            "Authored transformation events: " + (aggregate.MorphicGrove ? "Morphic Grove / Group; " : "") +
            (aggregate.AromaOfChaos ? "Aroma of Chaos / LetGo; " : "") + (aggregate.WhisperingHollow ? "Whispering Hollow / Hug; " : "") +
            (aggregate.Symbiote ? "Symbiote / transform; " : "") + (aggregate.TrialNondescript ? "Trial / require Nondescript case and two transforms; " : "") +
            "legal initial Strike/Defend targets and unchanged local pool/RNG premises at each entry; occurrence and deck history not proven." }, source.Targets);
    }

    private static void BuildSearchQuery(
        SearchDraft draft,
        out SearchQuery query,
        out AncientOptionConditionProfile ancientAssumptions)
    {
        NeowRouteSearchCondition? openingRoute = draft.NeowRouteDraft.RouteRelicKey is { } routeKey
            ? new NeowRouteSearchCondition(routeKey)
            : null;
        OpeningRouteRelicRequirement? openingRouteRelics =
            openingRoute is { IsValid: true } route && draft.NeowRouteDraft.RequiredBonesRelics.Count > 0
                ? new OpeningRouteRelicRequirement(
                    route.RouteRelicKey,
                    draft.NeowRouteDraft.RequiredBonesRelics.ToArray(),
                    draft.NeowRouteDraft.BonesOrderMode)
                : null;

        IReadOnlyList<VariantScopedBossBranch> variantBranches = draft.BossMapDraft.CatalogBound
            ? BuildVariantScopedBossBranches(draft.BossMapDraft)
            : Array.Empty<VariantScopedBossBranch>();

        IReadOnlyList<ActModelKeySetFilter> legacyBossFilters = draft.BossMapDraft.CatalogBound
            ? Array.Empty<ActModelKeySetFilter>()
            : BuildActFilter(
                draft.BossAct,
                draft.BossAny,
                draft.BossAll,
                draft.BossBan,
                BaseGameModelKeys.Categories.Encounter);
        IReadOnlyList<ActOrdinalModelKeySetFilter> legacyBossOrdinalFilters = draft.BossMapDraft.CatalogBound
            ? BuildBossOrdinalFilters(draft.BossMapDraft)
            : BuildActOrdinalFilter(
                draft.BossAct,
                draft.BossOrdinal,
                draft.BossOrdinalAny,
                draft.BossOrdinalAll,
                draft.BossOrdinalBan,
                BaseGameModelKeys.Categories.Encounter);

        LegacyWorldSemanticConstraints legacyWorld = draft.BossMapDraft.CatalogBound
            ? LegacyWorldSemanticConstraints.Empty
            : new LegacyWorldSemanticConstraints(
                legacyBossFilters,
                legacyBossOrdinalFilters,
                Array.Empty<ActModelKeySetFilter>(),
                Array.Empty<ActModelKeySetFilter>(),
                Array.Empty<ActModelKeySetFilter>());

        AncientSearchBranchCondition[] ancientBranches = draft.AncientMatrixDraft.Rows
            .Where(row => row.IsActive)
            .Select(row => new AncientSearchBranchCondition(
                row.Act,
                row.AncientKey,
                row.SelectedOptionKeys,
                row.SelectedOptionKeys.Any(key => string.Equals(key.Entry, "SEA_GLASS", StringComparison.Ordinal))
                    ? row.SeaGlassTargetKeys
                    : Array.Empty<ModelKey>()))
            .ToArray();

        IReadOnlyList<RelicSequenceSearchCondition> relicSequence =
            draft.RelicSequenceDraft.Count > 0 || string.IsNullOrWhiteSpace(draft.RelicSequenceConditions)
                ? draft.RelicSequenceDraft
                : ParseRelicSequenceConditions(draft.RelicSequenceConditions);
        IReadOnlyList<EventSequenceSearchCondition> eventSequence =
            draft.EventSequenceDraft.Count > 0 || string.IsNullOrWhiteSpace(draft.EventSequenceConditions)
                ? draft.EventSequenceDraft
                : ParseEventSequenceConditions(draft.EventSequenceConditions);

        CombatCardRewardSequenceSearchCondition? cardRewardSequence = null;
        if (draft.CombatRewardDraft.Cards.HasAnyValue)
        {
            ModelKey?[] cardSlots = draft.CombatRewardDraft.Cards.Slots
                .Take(Math.Clamp(draft.CombatRewardDraft.Cards.Count, 1, 3))
                .ToArray();
            cardRewardSequence = new CombatCardRewardSequenceSearchCondition(
                draft.CombatRewardDraft.Cards.Count,
                draft.CombatRewardDraft.Cards.OrderMode,
                cardSlots);
        }

        CombatPotionRewardSequenceSearchCondition? potionRewardSequence = null;
        if (draft.CombatRewardDraft.Potions.HasAnyValue)
        {
            CombatPotionRewardSlotSearchCondition[] potionSlots = draft.CombatRewardDraft.Potions.Slots
                .Take(Math.Clamp(draft.CombatRewardDraft.Potions.Count, 1, 3))
                .Select(slot => new CombatPotionRewardSlotSearchCondition(
                    slot.Requirement ?? CombatPotionSlotRequirement.Neutral,
                    slot.Requirement == CombatPotionSlotRequirement.DropSpecific ? slot.PotionKey : null))
                .ToArray();
            potionRewardSequence = new CombatPotionRewardSequenceSearchCondition(
                draft.CombatRewardDraft.Potions.Count,
                draft.CombatRewardDraft.Potions.OrderMode,
                potionSlots);
        }

        ancientAssumptions = new AncientOptionConditionProfile(
            TezcataraHasBasicStrike: draft.TezcataraHasBasicStrike,
            NonupeipeSwiftEnchantableAtLeast4: draft.NonupeipeSwiftEnchantableAtLeast4,
            TanxInstinctEnchantableAtLeast3: draft.TanxInstinctEnchantableAtLeast3,
            PaelGoopyDefendCardsAtLeast3: draft.PaelGoopyDefendCardsAtLeast3,
            PaelAllowLegionNoEventPet: draft.PaelAllowLegionNoEventPet,
            PaelRemovableCardsAtLeast5: draft.PaelRemovableCardsAtLeast5,
            OrobasArchaicToothConditionMet: draft.OrobasArchaicToothConditionMet,
            OrobasTouchOfOrobasConditionMet: draft.OrobasTouchOfOrobasConditionMet,
            DarvAllowPandorasBoxRelicSet: draft.DarvAllowPandorasBoxRelicSet);

        query = new SearchQuery(
            openingRoute,
            openingRouteRelics,
            draft.NeowRouteDraft.EffectConditions,
            variantBranches,
            ancientBranches,
            relicSequence,
            eventSequence,
            Array.Empty<NormalCombatRewardSearchCondition>(),
            LegacyNeowSemanticConstraints.Empty,
            legacyWorld)
        {
            CombatCardRewards = cardRewardSequence,
            CombatPotionRewards = potionRewardSequence,
            EventResultConditions = draft.EventResultDraft,
            MerchantColorlessConditions = draft.MerchantColorlessDraft,
            MerchantColorlessSequenceConditions = draft.MerchantColorlessSequenceDraft,
            RelicShopSequenceConditions = draft.RelicShopSequenceDraft
        };
    }

    private static IReadOnlyList<VariantScopedBossBranch> BuildVariantScopedBossBranches(
        BossMapSearchDraft draft)
    {
        var output = new List<VariantScopedBossBranch>();
        foreach (IGrouping<int, BossMapVariantSearchDraft> actRows in draft.Rows
                     .GroupBy(row => row.Act)
                     .OrderBy(group => group.Key))
        {
            BossMapVariantSearchDraft[] rows = actRows.ToArray();
            bool selectableVariants = rows.Any(row => row.HasSelectableVariant);
            BossMapVariantSearchDraft[] activeRows = selectableVariants
                ? rows.Where(row => row.IsVariantActive).ToArray()
                : rows;

            foreach (BossMapVariantSearchDraft row in activeRows)
            {
                bool hasFirstBossConstraint = row.FirstBossAny.Count > 0;
                bool hasSecondBossConstraint =
                    row.Act == 3 &&
                    draft.IncludeSecondAct3Boss &&
                    row.SecondBossAny.Count > 0;
                bool hasExplicitVariantConstraint = selectableVariants && row.IsVariantActive;

                // A non-selectable Variant is runtime/UI context, not player Query
                // truth by itself. Preserve Variant-only semantics only when the
                // UI actually offered a Variant choice and the player activated it.
                // Otherwise emit the parent branch only when a Boss child predicate
                // needs that Variant attribution. This keeps Canonical active domains
                // aligned with the Legacy execution filter for empty/Neow-only queries.
                if (!hasExplicitVariantConstraint &&
                    !hasFirstBossConstraint &&
                    !hasSecondBossConstraint)
                {
                    continue;
                }

                ModelKeySetFilter first = hasFirstBossConstraint
                    ? new ModelKeySetFilter(row.FirstBossAny, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())
                    : ModelKeySetFilter.Empty;
                ModelKeySetFilter second = hasSecondBossConstraint
                    ? new ModelKeySetFilter(row.SecondBossAny, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())
                    : ModelKeySetFilter.Empty;

                output.Add(new VariantScopedBossBranch(
                    row.Act,
                    row.ActKey,
                    first,
                    second,
                    row.Act == 3 && draft.IncludeSecondAct3Boss));
            }
        }
        return output;
    }

    private static IReadOnlyList<ActOrdinalModelKeySetFilter> BuildBossOrdinalFilters(
        BossMapSearchDraft draft)
    {
        var output = new List<ActOrdinalModelKeySetFilter>();
        foreach (IGrouping<int, BossMapVariantSearchDraft> actRows in draft.Rows
                     .GroupBy(row => row.Act)
                     .OrderBy(group => group.Key))
        {
            BossMapVariantSearchDraft[] rows = actRows.ToArray();
            bool selectableVariants = rows.Any(row => row.HasSelectableVariant);
            BossMapVariantSearchDraft[] activeRows = selectableVariants
                ? rows.Where(row => row.IsVariantActive).ToArray()
                : rows;
            if (activeRows.Length == 0)
            {
                continue;
            }

            ModelKey[] first = activeRows
                .SelectMany(row => selectableVariants && row.FirstBossAny.Count == 0
                    ? row.AllBossKeys
                    : row.FirstBossAny)
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            if (first.Length > 0)
            {
                output.Add(new ActOrdinalModelKeySetFilter(
                    actRows.Key,
                    1,
                    new ModelKeySetFilter(first, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())));
            }

            if (actRows.Key != 3 || !draft.IncludeSecondAct3Boss)
            {
                continue;
            }

            ModelKey[] second = activeRows
                .SelectMany(row => row.SecondBossAny)
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            if (second.Length > 0)
            {
                output.Add(new ActOrdinalModelKeySetFilter(
                    actRows.Key,
                    2,
                    new ModelKeySetFilter(second, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())));
            }
        }
        return output;
    }

    private static ModelKeySetFilter BuildKeySet(string any, string all, string ban, string? category) => new(
        ParseKeys(any, category),
        ParseKeys(all, category),
        ParseKeys(ban, category));

    private static IReadOnlyList<ActModelKeySetFilter> BuildActFilter(
        int act,
        string any,
        string all,
        string ban,
        string? category)
    {
        ModelKeySetFilter keys = BuildKeySet(any, all, ban, category);
        return keys.IsEmpty
            ? Array.Empty<ActModelKeySetFilter>()
            : new[] { new ActModelKeySetFilter(act, keys) };
    }

    private static IReadOnlyList<ActOrdinalModelKeySetFilter> BuildActOrdinalFilter(
        int act,
        int ordinal,
        string any,
        string all,
        string ban,
        string? category)
    {
        ModelKeySetFilter keys = BuildKeySet(any, all, ban, category);
        if (keys.IsEmpty)
        {
            return Array.Empty<ActOrdinalModelKeySetFilter>();
        }
        if (ordinal <= 0)
        {
            throw new InvalidOperationException("An exact Boss ordinal is required when an ordinal Boss filter is enabled.");
        }
        return new[] { new ActOrdinalModelKeySetFilter(act, ordinal, keys) };
    }

    private static IReadOnlyList<RelicSequenceSearchCondition> ParseRelicSequenceConditions(string text)
    {
        var output = new List<RelicSequenceSearchCondition>();
        foreach (string condition in SplitConditions(text))
        {
            string[] parts = condition.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 5 ||
                !Enum.TryParse(parts[0], ignoreCase: true, out RelicSequenceKind lane) ||
                !TryParseRangeMode(parts[1], out SearchSequenceRangeMode rangeMode) ||
                !int.TryParse(parts[2], out int rangeValue) || rangeValue <= 0 ||
                !TryParseSetMode(parts[3], out string setMode))
            {
                throw new InvalidOperationException(
                    "Invalid relic sequence condition. Expected: COMMON FIRST 5 ANY RELIC:KEY or RARE SLOT 2 BAN RELIC:KEY.");
            }
            string keysText = string.Join(' ', parts.Skip(4));
            ModelKeySetFilter keys = BuildSingleModeKeySet(setMode, keysText, BaseGameModelKeys.Categories.Relic);
            output.Add(new RelicSequenceSearchCondition(lane, rangeMode, rangeValue, keys));
        }
        return output;
    }

    private static IReadOnlyList<EventSequenceSearchCondition> ParseEventSequenceConditions(string text)
    {
        var output = new List<EventSequenceSearchCondition>();
        foreach (string condition in SplitConditions(text))
        {
            string[] parts = condition.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 6 || !TryParseAct(parts[0], out int act) ||
                !TryParseEventSource(parts[1], out EventPoolSourceKind? source) ||
                !TryParseRangeMode(parts[2], out SearchSequenceRangeMode rangeMode) ||
                !int.TryParse(parts[3], out int rangeValue) || rangeValue <= 0 ||
                !TryParseSetMode(parts[4], out string setMode))
            {
                throw new InvalidOperationException(
                    "Invalid event sequence condition. Expected: A2 SHARED FIRST 5 ANY EVENT:KEY or A3 ANY SLOT 1 BAN EVENT:KEY.");
            }
            string keysText = string.Join(' ', parts.Skip(5));
            ModelKeySetFilter keys = BuildSingleModeKeySet(setMode, keysText, BaseGameModelKeys.Categories.Event);
            output.Add(new EventSequenceSearchCondition(act, source, rangeMode, rangeValue, keys));
        }
        return output;
    }

    private static IEnumerable<string> SplitConditions(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryParseAct(string token, out int act)
    {
        string value = token.Trim().ToUpperInvariant();
        if (value.StartsWith('A')) value = value[1..];
        return int.TryParse(value, out act) && act is >= 1 and <= 3;
    }

    private static bool TryParseEventSource(string token, out EventPoolSourceKind? source)
    {
        switch (token.Trim().ToUpperInvariant())
        {
            case "ANY": source = null; return true;
            case "SHARED": source = EventPoolSourceKind.Shared; return true;
            case "LOCAL":
            case "ACTLOCAL":
            case "ACT-LOCAL": source = EventPoolSourceKind.ActLocal; return true;
            default: source = null; return false;
        }
    }

    private static bool TryParseRangeMode(string token, out SearchSequenceRangeMode mode)
    {
        switch (token.Trim().ToUpperInvariant())
        {
            case "FIRST":
            case "FIRSTN": mode = SearchSequenceRangeMode.FirstN; return true;
            case "SLOT":
            case "EXACT": mode = SearchSequenceRangeMode.ExactSlot; return true;
            default: mode = default; return false;
        }
    }

    private static bool TryParseSetMode(string token, out string mode)
    {
        mode = token.Trim().ToUpperInvariant();
        return mode is "ANY" or "ALL" or "BAN";
    }

    private static ModelKeySetFilter BuildSingleModeKeySet(string mode, string keysText, string category)
    {
        IReadOnlyList<ModelKey> keys = ParseKeys(keysText, category);
        return mode switch
        {
            "ANY" => new ModelKeySetFilter(keys, Array.Empty<ModelKey>(), Array.Empty<ModelKey>()),
            "ALL" => new ModelKeySetFilter(Array.Empty<ModelKey>(), keys, Array.Empty<ModelKey>()),
            "BAN" => new ModelKeySetFilter(Array.Empty<ModelKey>(), Array.Empty<ModelKey>(), keys),
            _ => throw new InvalidOperationException("Unknown set mode: " + mode)
        };
    }

    private static IReadOnlyList<ModelKey> ParseKeys(string text, string? category)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<ModelKey>();
        }

        var output = new List<ModelKey>();
        foreach (string token in text.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            output.Add(ParseExactKey(token, category));
        }
        return output.Distinct(ModelKeyComparer.Instance).ToArray();
    }

    private static ModelKey ParseExactKey(string token, string? category)
    {
        string exact = token.Contains(':', StringComparison.Ordinal)
            ? token.Trim().ToUpperInvariant()
            : category is not null
                ? category + ":" + token.Trim().ToUpperInvariant()
                : throw new InvalidOperationException("An exact CATEGORY:ID ModelKey is required: " + token);
        if (!ModelKey.TryParseExact(exact, out ModelKey key) ||
            (category is not null && !string.Equals(key.Category, category, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Invalid exact ModelKey: " + token);
        }
        return key;
    }
}
