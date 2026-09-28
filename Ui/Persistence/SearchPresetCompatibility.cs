using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.BossMap;
using RolltheSpire2.Ui.Pages.Search.CombatReward;

namespace RolltheSpire2.Ui.Persistence;

internal enum SearchPresetLoadResolutionKind : byte
{
    Unavailable = 0,
    Full = 1,
    Partial = 2
}

internal sealed record SearchPresetUnresolvedReference(
    string Path,
    string StableIdentity,
    string Reason);

internal sealed record SearchPresetLoadResolution(
    SearchPresetLoadResolutionKind Kind,
    SearchDraft? Draft,
    int AuthoredConditionCount,
    int LoadedConditionCount,
    IReadOnlyList<SearchPresetUnresolvedReference> Unresolved,
    string Issue)
{
    public bool CanLoad => Draft is not null && Kind != SearchPresetLoadResolutionKind.Unavailable;
    public int UnresolvedConditionCount => Math.Max(0, AuthoredConditionCount - LoadedConditionCount);

    public static SearchPresetLoadResolution Unavailable(
        int authoredConditionCount,
        string issue,
        IReadOnlyList<SearchPresetUnresolvedReference>? unresolved = null) =>
        new(
            SearchPresetLoadResolutionKind.Unavailable,
            null,
            Math.Max(0, authoredConditionCount),
            0,
            unresolved ?? Array.Empty<SearchPresetUnresolvedReference>(),
            issue ?? "PresetLoadUnavailable");
}

internal enum SearchPresetSemanticCompatibilityKind : byte
{
    Unknown = 0,
    SameRuntimeSemantics = 1,
    DifferentRuntimeSemantics = 2,
    SchemaIncomparable = 3
}

internal sealed record SearchPresetCompatibilityAssessment(
    SearchPresetSemanticCompatibilityKind SemanticCompatibility,
    SemanticEnvironmentStatus HistoricalEnvironmentStatus,
    SemanticEnvironmentStatus CurrentEnvironmentStatus,
    string HistoricalMatchedBaseline,
    string CurrentMatchedBaseline,
    bool SameGameVersion,
    VanillaUnlockOverallStatus CurrentVanillaUnlockStatus,
    bool CurrentUnlockMayBeNarrower,
    string ReasonCode);

/// <summary>
/// B-Compatibility only resolves whether persisted stable content identities still
/// exist in the current RT2-relevant SemanticUniverse. It deliberately does not
/// inspect CurrentUnlockState and therefore never becomes Search admission authority.
/// If one authored condition contains an unresolved reference, that whole condition
/// is omitted from the load projection instead of pruning a member and silently
/// changing Any/All/Ban semantics. The original Preset asset / RawQueryJson is untouched.
/// </summary>
internal static class SearchPresetCompatibilityResolver
{
    public static SearchPresetLoadResolution Resolve(
        SearchPresetDefinition preset,
        RuntimeAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(authority);

        int authored = Math.Max(0, preset.ConditionCount);
        if (preset.Draft is null)
            return SearchPresetLoadResolution.Unavailable(authored, "PresetQueryShapeUnresolved");

        var universe = new SemanticUniverseIndex(authority);
        var unresolved = new List<SearchPresetUnresolvedReference>();
        if (!universe.TryResolve(RuntimeAuthorityDomains.Characters, preset.CharacterKey, "Character", unresolved))
        {
            string issue = HasAuthorityIncomplete(unresolved)
                ? "CurrentSemanticAuthorityIncomplete"
                : "PresetCharacterUnavailable";
            return SearchPresetLoadResolution.Unavailable(authored, issue, unresolved);
        }

        SearchDraft source = preset.Draft;
        int droppedConditions = 0;

        NeowRouteFilterDraft neow = ResolveNeow(source.NeowRouteDraft, universe, unresolved, ref droppedConditions);
        AncientSearchMatrixDraft ancient = ResolveAncients(source.AncientMatrixDraft, universe, unresolved, ref droppedConditions);
        BossMapSearchDraft boss = ResolveBosses(source.BossMapDraft, universe, unresolved, ref droppedConditions);
        IReadOnlyList<RelicSequenceSearchCondition> relicSequence = ResolveRelicSequence(source.RelicSequenceDraft, universe, unresolved, ref droppedConditions);
        IReadOnlyList<EventSequenceSearchCondition> eventSequence = ResolveEventSequence(source.EventSequenceDraft, universe, unresolved, ref droppedConditions);
        IReadOnlyList<EventResultSearchCondition> eventResults = ResolveEventResults(source.EventResultDraft, universe, unresolved, ref droppedConditions);
        IReadOnlyList<MerchantColorlessSlotCondition> merchantColorless = ResolveMerchantColorless(source.MerchantColorlessDraft, universe, unresolved, ref droppedConditions);
        CombatRewardSearchDraft rewards = ResolveCombatRewards(source.CombatRewardDraft, universe, unresolved, ref droppedConditions);

        (string neowAny, string neowAll, string neowBan) = ResolveLegacySetGroup(
            source.NeowAny, source.NeowAll, source.NeowBan,
            RuntimeAuthorityDomains.Relics, "Legacy.Neow", universe, unresolved, ref droppedConditions);
        (string bonesAny, string bonesAll, string bonesBan) = ResolveLegacySetGroup(
            source.BonesAny, source.BonesAll, source.BonesBan,
            RuntimeAuthorityDomains.Relics, "Legacy.Bones", universe, unresolved, ref droppedConditions);
        (string capsuleAny, string capsuleAll, string capsuleBan) = ResolveLegacySetGroup(
            source.CapsuleRelicAny, source.CapsuleRelicAll, source.CapsuleRelicBan,
            RuntimeAuthorityDomains.Relics, "Legacy.Capsule", universe, unresolved, ref droppedConditions);
        string finalCurse = ResolveLegacySingle(source.RequiredFinalCurse, RuntimeAuthorityDomains.Cards, "Legacy.FinalCurse", universe, unresolved, ref droppedConditions);
        string bannedCurses = ResolveLegacySet(source.BannedFinalCurses, RuntimeAuthorityDomains.Cards, "Legacy.BannedCurses", universe, unresolved, ref droppedConditions);
        string bonesOrder = ResolveLegacyOrdered(source.BonesAcquisitionOrder, RuntimeAuthorityDomains.Relics, "Legacy.BonesOrder", universe, unresolved, ref droppedConditions);
        (string effectSource, string effectAny, string effectAll, string effectBan) = ResolveLegacyEffectGroup(
            source.EffectOutputSource, source.EffectOutputAny, source.EffectOutputAll, source.EffectOutputBan,
            universe, unresolved, ref droppedConditions);
        (string bossAny, string bossAll, string bossBan) = ResolveLegacySetGroup(
            source.BossAny, source.BossAll, source.BossBan,
            RuntimeAuthorityDomains.Bosses, "Legacy.Boss", universe, unresolved, ref droppedConditions);
        (string bossOrdinalAny, string bossOrdinalAll, string bossOrdinalBan) = ResolveLegacySetGroup(
            source.BossOrdinalAny, source.BossOrdinalAll, source.BossOrdinalBan,
            RuntimeAuthorityDomains.Bosses, "Legacy.BossOrdinal", universe, unresolved, ref droppedConditions);
        (string ancientAny, string ancientAll, string ancientBan) = ResolveLegacySetGroup(
            source.AncientAny, source.AncientAll, source.AncientBan,
            RuntimeAuthorityDomains.Ancients, "Legacy.Ancient", universe, unresolved, ref droppedConditions);
        (string ancientOptionAny, string ancientOptionAll, string ancientOptionBan) = ResolveLegacySetGroup(
            source.AncientOptionAny, source.AncientOptionAll, source.AncientOptionBan,
            RuntimeAuthorityDomains.Relics, "Legacy.AncientOption", universe, unresolved, ref droppedConditions);
        (string seaGlassAny, string seaGlassAll, string seaGlassBan) = ResolveLegacySetGroup(
            source.SeaGlassTargetAny, source.SeaGlassTargetAll, source.SeaGlassTargetBan,
            RuntimeAuthorityDomains.Characters, "Legacy.SeaGlass", universe, unresolved, ref droppedConditions);
        bool requireBones = ResolveLegacyImplicitRelicFlag(source.RequireBones, BaseGameModelKeys.Relics.NeowsBones, "Legacy.RequireBones", universe, unresolved, ref droppedConditions);
        bool requireSmallCapsule = ResolveLegacyImplicitRelicFlag(source.RequireSmallCapsule, BaseGameModelKeys.Relics.SmallCapsule, "Legacy.RequireSmallCapsule", universe, unresolved, ref droppedConditions);
        bool requireLargeCapsule = ResolveLegacyImplicitRelicFlag(source.RequireLargeCapsule, BaseGameModelKeys.Relics.LargeCapsule, "Legacy.RequireLargeCapsule", universe, unresolved, ref droppedConditions);
        bool requireWhetstone = ResolveLegacyImplicitRelicFlag(source.RequireWhetstone, BaseGameModelKeys.OrdinaryRelics.Whetstone, "Legacy.RequireWhetstone", universe, unresolved, ref droppedConditions);
        bool requireWarPaint = ResolveLegacyImplicitRelicFlag(source.RequireWarPaint, BaseGameModelKeys.OrdinaryRelics.WarPaint, "Legacy.RequireWarPaint", universe, unresolved, ref droppedConditions);

        SearchDraft resolved = source with
        {
            NeowAny = neowAny,
            NeowAll = neowAll,
            NeowBan = neowBan,
            RequireBones = requireBones,
            BonesAny = bonesAny,
            BonesAll = bonesAll,
            BonesBan = bonesBan,
            RequireSmallCapsule = requireSmallCapsule,
            RequireLargeCapsule = requireLargeCapsule,
            CapsuleRelicAny = capsuleAny,
            RequireWhetstone = requireWhetstone,
            RequireWarPaint = requireWarPaint,
            RequiredFinalCurse = finalCurse,
            BannedFinalCurses = bannedCurses,
            NeowRouteDraft = neow,
            AncientMatrixDraft = ancient,
            BossMapDraft = boss,
            CombatRewardDraft = rewards,
            CapsuleRelicAll = capsuleAll,
            CapsuleRelicBan = capsuleBan,
            BonesAcquisitionOrder = bonesOrder,
            EffectOutputSource = effectSource,
            EffectOutputAny = effectAny,
            EffectOutputAll = effectAll,
            EffectOutputBan = effectBan,
            BossAny = bossAny,
            BossAll = bossAll,
            BossBan = bossBan,
            BossOrdinalAny = bossOrdinalAny,
            BossOrdinalAll = bossOrdinalAll,
            BossOrdinalBan = bossOrdinalBan,
            AncientAny = ancientAny,
            AncientAll = ancientAll,
            AncientBan = ancientBan,
            AncientOptionAny = ancientOptionAny,
            AncientOptionAll = ancientOptionAll,
            AncientOptionBan = ancientOptionBan,
            SeaGlassTargetAny = seaGlassAny,
            SeaGlassTargetAll = seaGlassAll,
            SeaGlassTargetBan = seaGlassBan,
            RelicSequenceDraft = relicSequence,
            EventSequenceDraft = eventSequence,
            EventResultDraft = eventResults,
            MerchantColorlessDraft = merchantColorless,
            // When typed sequence authority exists in the asset, keep compatibility
            // strings inert. If the asset only has historical string conditions they
            // remain preserved above and Search's existing compatibility parser owns them.
            RelicSequenceConditions = relicSequence.Count > 0 ? string.Empty : source.RelicSequenceConditions,
            EventSequenceConditions = eventSequence.Count > 0 ? string.Empty : source.EventSequenceConditions
        };

        if (source.RelicSequenceDraft.Count == 0 && !string.IsNullOrWhiteSpace(source.RelicSequenceConditions))
        {
            string filtered = ResolveLegacySequenceText(source.RelicSequenceConditions, RuntimeAuthorityDomains.Relics, "Legacy.RelicSequence", universe, unresolved, ref droppedConditions);
            resolved = resolved with { RelicSequenceConditions = filtered };
        }
        if (source.EventSequenceDraft.Count == 0 && !string.IsNullOrWhiteSpace(source.EventSequenceConditions))
        {
            string filtered = ResolveLegacySequenceText(source.EventSequenceConditions, RuntimeAuthorityDomains.Events, "Legacy.EventSequence", universe, unresolved, ref droppedConditions);
            resolved = resolved with { EventSequenceConditions = filtered };
        }

        int originalCount = authored > 0 ? authored : CountConditions(source);
        if (HasAuthorityIncomplete(unresolved))
        {
            return SearchPresetLoadResolution.Unavailable(
                originalCount,
                "CurrentSemanticAuthorityIncomplete",
                unresolved);
        }

        int loadedCount = unresolved.Count == 0
            ? originalCount
            : Math.Max(0, originalCount - Math.Min(originalCount, droppedConditions));
        if (unresolved.Count > 0 && originalCount > 0 && loadedCount == 0)
        {
            return SearchPresetLoadResolution.Unavailable(
                originalCount,
                "NoResolvablePresetConditions",
                unresolved);
        }

        SearchPresetLoadResolutionKind kind = unresolved.Count == 0
            ? SearchPresetLoadResolutionKind.Full
            : SearchPresetLoadResolutionKind.Partial;

        return new SearchPresetLoadResolution(
            kind,
            resolved,
            originalCount,
            loadedCount,
            unresolved,
            kind == SearchPresetLoadResolutionKind.Full ? string.Empty : "PresetReferencesPartiallyUnavailable");
    }

    public static SearchPresetCompatibilityAssessment Assess(
        SearchPresetDefinition preset,
        RuntimeAuthorityEnvironmentSnapshot current)
    {
        SemanticEnvironmentStatus historical = RuntimeAuthorityInterpreter.InterpretHistoricalFingerprint(
            preset.Provenance.GameVersion,
            preset.Provenance.FingerprintSchemaVersion,
            preset.Provenance.SemanticFingerprint,
            out string historicalBaseline);

        RuntimeAuthoritySnapshot authority = current.Authority;
        bool sameVersion = !string.IsNullOrWhiteSpace(preset.Provenance.GameVersion) &&
            string.Equals(preset.Provenance.GameVersion, authority.GameVersion, StringComparison.Ordinal);
        string storedHash = preset.Provenance.SemanticFingerprint ?? string.Empty;
        string currentHash = authority.Fingerprint.OverallSemanticHash ?? string.Empty;
        SearchPresetSemanticCompatibilityKind semantic;
        string reason;
        if (preset.Provenance.FingerprintSchemaVersion <= 0 ||
            authority.FingerprintSchemaVersion <= 0 ||
            preset.Provenance.FingerprintSchemaVersion != authority.FingerprintSchemaVersion)
        {
            semantic = SearchPresetSemanticCompatibilityKind.SchemaIncomparable;
            reason = "FingerprintSchemaIncomparable";
        }
        else if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(currentHash))
        {
            semantic = SearchPresetSemanticCompatibilityKind.Unknown;
            reason = "SemanticFingerprintUnavailable";
        }
        else if (string.Equals(storedHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            semantic = SearchPresetSemanticCompatibilityKind.SameRuntimeSemantics;
            reason = sameVersion ? "SameVersionSameSemanticFingerprint" : "CrossVersionSameSemanticFingerprint";
        }
        else
        {
            semantic = SearchPresetSemanticCompatibilityKind.DifferentRuntimeSemantics;
            reason = "PresetAndCurrentSemanticFingerprintDiffer";
        }

        bool currentUnlockMayBeNarrower =
            string.Equals(preset.Provenance.UnlockKind, SearchPresetUnlockKinds.Full, StringComparison.Ordinal) &&
            current.Interpretation.VanillaUnlockStatus == VanillaUnlockOverallStatus.Partial;
        return new SearchPresetCompatibilityAssessment(
            semantic,
            historical,
            current.Interpretation.EnvironmentStatus,
            historicalBaseline,
            current.Interpretation.MatchedBaselineGameVersion ?? string.Empty,
            sameVersion,
            current.Interpretation.VanillaUnlockStatus,
            currentUnlockMayBeNarrower,
            reason);
    }

    private static NeowRouteFilterDraft ResolveNeow(
        NeowRouteFilterDraft source,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        if (source.RouteRelicKey is not { IsValid: true } route)
            return source;
        int originalCount = 1 + (source.RequiredBonesRelics.Count > 0 ? 1 : 0) + source.EffectConditions.Count;
        if (!universe.TryResolve(RuntimeAuthorityDomains.Relics, route, "Neow.RouteRelic", unresolved))
        {
            dropped += originalCount;
            return NeowRouteFilterDraft.Empty;
        }

        IReadOnlyList<ModelKey> bones = source.RequiredBonesRelics;
        if (bones.Count > 0 && !AllResolved(bones, RuntimeAuthorityDomains.Relics, "Neow.RequiredBonesRelics", universe, unresolved))
        {
            bones = Array.Empty<ModelKey>();
            dropped++;
        }

        var effects = new List<NeowStructuredEffectSearchCondition>();
        for (int i = 0; i < source.EffectConditions.Count; i++)
        {
            NeowStructuredEffectSearchCondition effect = source.EffectConditions[i];
            string path = $"Neow.Effect[{i}]";
            string outputDomain = effect.OutputKind switch
            {
                NeowStructuredOutputKind.Relic => RuntimeAuthorityDomains.Relics,
                NeowStructuredOutputKind.Potion => RuntimeAuthorityDomains.Potions,
                _ => RuntimeAuthorityDomains.Cards
            };
            bool ok = universe.TryResolve(RuntimeAuthorityDomains.Relics, effect.SourceRelicKey, path + ".SourceRelic", unresolved) &&
                      AllResolved(effect.OutputKeys, outputDomain, path + ".Outputs", universe, unresolved) &&
                      AllResolved(effect.KaleidoscopePositionalSlots.Where(key => key.HasValue).Select(key => key!.Value), outputDomain, path + ".Slots", universe, unresolved);
            if (ok) effects.Add(effect);
            else dropped++;
        }
        return new NeowRouteFilterDraft(route, bones, source.BonesOrderMode, effects);
    }

    private static AncientSearchMatrixDraft ResolveAncients(
        AncientSearchMatrixDraft source,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        var rows = new List<AncientSearchRowDraft>();
        for (int i = 0; i < source.Rows.Count; i++)
        {
            AncientSearchRowDraft row = source.Rows[i];
            if (!row.IsActive)
            {
                rows.Add(row);
                continue;
            }
            string path = $"Ancient[{i}]";
            bool ok = universe.TryResolve(RuntimeAuthorityDomains.Ancients, row.AncientKey, path + ".Identity", unresolved) &&
                      AllResolved(row.SelectedOptionKeys, RuntimeAuthorityDomains.Relics, path + ".Options", universe, unresolved) &&
                      AllResolved(row.SeaGlassTargetKeys, RuntimeAuthorityDomains.Characters, path + ".SeaGlassTargets", universe, unresolved);
            if (ok) rows.Add(row);
            else
            {
                dropped++;
                rows.Add(row with
                {
                    IsActive = false,
                    SelectedOptionKeys = Array.Empty<ModelKey>(),
                    SeaGlassTargetKeys = Array.Empty<ModelKey>()
                });
            }
        }
        return new AncientSearchMatrixDraft(rows);
    }

    private static BossMapSearchDraft ResolveBosses(
        BossMapSearchDraft source,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        var rows = new List<BossMapVariantSearchDraft>();
        for (int i = 0; i < source.Rows.Count; i++)
        {
            BossMapVariantSearchDraft row = source.Rows[i];
            string path = $"BossMap[{i}]";
            if (row.HasSelectableVariant)
            {
                if (!row.IsVariantActive)
                {
                    rows.Add(row);
                    continue;
                }
                bool ok = AllResolved(row.FirstBossAny, RuntimeAuthorityDomains.Bosses, path + ".First", universe, unresolved) &&
                          AllResolved(row.SecondBossAny, RuntimeAuthorityDomains.Bosses, path + ".Second", universe, unresolved);
                if (ok) rows.Add(row);
                else
                {
                    dropped++;
                    rows.Add(row with
                    {
                        IsVariantActive = false,
                        FirstBossAny = Array.Empty<ModelKey>(),
                        SecondBossAny = Array.Empty<ModelKey>()
                    });
                }
                continue;
            }

            IReadOnlyList<ModelKey> first = row.FirstBossAny;
            IReadOnlyList<ModelKey> second = row.SecondBossAny;
            if (first.Count > 0 && !AllResolved(first, RuntimeAuthorityDomains.Bosses, path + ".First", universe, unresolved))
            {
                first = Array.Empty<ModelKey>();
                dropped++;
            }
            if (second.Count > 0 && !AllResolved(second, RuntimeAuthorityDomains.Bosses, path + ".Second", universe, unresolved))
            {
                second = Array.Empty<ModelKey>();
                dropped++;
            }
            rows.Add(row with { FirstBossAny = first, SecondBossAny = second });
        }
        return source with { Rows = rows };
    }

    private static IReadOnlyList<RelicSequenceSearchCondition> ResolveRelicSequence(
        IReadOnlyList<RelicSequenceSearchCondition> source,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        var output = new List<RelicSequenceSearchCondition>();
        for (int i = 0; i < source.Count; i++)
        {
            RelicSequenceSearchCondition condition = source[i];
            if (SetResolved(condition.Keys, RuntimeAuthorityDomains.Relics, $"RelicSequence[{i}]", universe, unresolved))
                output.Add(condition);
            else
                dropped++;
        }
        return output;
    }

    private static IReadOnlyList<EventSequenceSearchCondition> ResolveEventSequence(
        IReadOnlyList<EventSequenceSearchCondition> source,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        var output = new List<EventSequenceSearchCondition>();
        for (int i = 0; i < source.Count; i++)
        {
            EventSequenceSearchCondition condition = source[i];
            if (SetResolved(condition.Keys, RuntimeAuthorityDomains.Events, $"EventSequence[{i}]", universe, unresolved))
                output.Add(condition);
            else
                dropped++;
        }
        return output;
    }

    private static IReadOnlyList<EventResultSearchCondition> ResolveEventResults(
        IReadOnlyList<EventResultSearchCondition> source,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        var output = new List<EventResultSearchCondition>();
        for (int i = 0; i < source.Count; i++)
        {
            EventResultSearchCondition condition = source[i];
            string domain = condition.Kind switch
            {
                EventResultConditionKind.TrashHeapGrabCard => RuntimeAuthorityDomains.Cards,
                EventResultConditionKind.TrashHeapDiveRelic => RuntimeAuthorityDomains.Relics,
                EventResultConditionKind.FakeMerchantOfferedFakeRelic => RuntimeAuthorityDomains.Relics,
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => RuntimeAuthorityDomains.Characters,
                _ => string.Empty
            };
            string path = $"EventResult[{i}]";
            bool ok = condition.IsValid &&
                      domain.Length > 0 &&
                      universe.TryResolve(domain, condition.TargetKey, path + ".Target", unresolved);
            if (ok)
            {
                output.Add(condition);
            }
            else
            {
                if (!condition.IsValid)
                    unresolved.Add(new SearchPresetUnresolvedReference(path, condition.TargetKey.Serialized, "StableIdentityInvalid"));
                dropped++;
            }
        }
        return output;
    }

    private static IReadOnlyList<MerchantColorlessSlotCondition> ResolveMerchantColorless(
        IReadOnlyList<MerchantColorlessSlotCondition> source,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        var output = new List<MerchantColorlessSlotCondition>();
        for (int i = 0; i < source.Count; i++)
        {
            MerchantColorlessSlotCondition condition = source[i];
            string path = $"MerchantColorless[{i}]";
            bool ok = condition.IsValid &&
                      universe.TryResolve(RuntimeAuthorityDomains.Cards, condition.TargetCardKey, path + ".Target", unresolved);
            if (ok)
            {
                output.Add(condition);
            }
            else
            {
                if (!condition.IsValid)
                    unresolved.Add(new SearchPresetUnresolvedReference(path, condition.TargetCardKey.Serialized, "StableIdentityInvalid"));
                dropped++;
            }
        }
        return output;
    }

    private static CombatRewardSearchDraft ResolveCombatRewards(
        CombatRewardSearchDraft source,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        CombatRewardCardSequenceDraft cards = source.Cards;
        if (cards.HasAnyValue)
        {
            ModelKey[] refs = cards.Slots.Where(key => key.HasValue).Select(key => key!.Value).ToArray();
            if (!AllResolved(refs, RuntimeAuthorityDomains.Cards, "CombatReward.Cards", universe, unresolved))
            {
                dropped += Math.Max(1, cards.Count);
                cards = CombatRewardSearchDraft.Empty.Cards;
            }
        }

        CombatRewardPotionSequenceDraft potions = source.Potions;
        if (potions.HasAnyValue)
        {
            ModelKey[] refs = potions.Slots
                .Where(slot => slot.Requirement == CombatPotionSlotRequirement.DropSpecific && slot.PotionKey.HasValue)
                .Select(slot => slot.PotionKey!.Value)
                .ToArray();
            if (!AllResolved(refs, RuntimeAuthorityDomains.Potions, "CombatReward.Potions", universe, unresolved))
            {
                dropped += Math.Max(1, potions.Count);
                potions = CombatRewardSearchDraft.Empty.Potions;
            }
        }
        return new CombatRewardSearchDraft(cards, potions);
    }

    private static bool SetResolved(
        ModelKeySetFilter filter,
        string domain,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved) =>
        AllResolved(filter.Any, domain, path + ".Any", universe, unresolved) &&
        AllResolved(filter.All, domain, path + ".All", universe, unresolved) &&
        AllResolved(filter.Ban, domain, path + ".Ban", universe, unresolved);

    private static bool AllResolved(
        IEnumerable<ModelKey> keys,
        string domain,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved)
    {
        bool ok = true;
        foreach (ModelKey key in keys.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance))
            ok &= universe.TryResolve(domain, key, path, unresolved);
        return ok;
    }

    private static bool ResolveLegacyImplicitRelicFlag(
        bool enabled,
        ModelKey relicKey,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        if (!enabled) return false;
        if (universe.TryResolve(RuntimeAuthorityDomains.Relics, relicKey, path, unresolved)) return true;
        dropped++;
        return false;
    }

    private static string ResolveLegacySingle(
        string text,
        string domain,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (!TryParseToken(text.Trim(), out ModelKey key) || !universe.TryResolve(domain, key, path, unresolved))
        {
            dropped++;
            return string.Empty;
        }
        return text;
    }

    private static string ResolveLegacySet(
        string text,
        string domain,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        ModelKey[] keys = ParseTokens(text, new[] { ',', '\n', '\r' });
        if (keys.Length == 0 || !AllResolved(keys, domain, path, universe, unresolved))
        {
            dropped++;
            return string.Empty;
        }
        return text;
    }

    private static (string Any, string All, string Ban) ResolveLegacySetGroup(
        string any,
        string all,
        string ban,
        string domain,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        if (string.IsNullOrWhiteSpace(any) && string.IsNullOrWhiteSpace(all) && string.IsNullOrWhiteSpace(ban))
            return (string.Empty, string.Empty, string.Empty);

        bool ok = LegacySetTextResolved(any, domain, path + ".Any", universe, unresolved) &
                  LegacySetTextResolved(all, domain, path + ".All", universe, unresolved) &
                  LegacySetTextResolved(ban, domain, path + ".Ban", universe, unresolved);
        if (ok) return (any, all, ban);

        // Any/All/Ban together form one historical authored condition. If any member
        // becomes unresolved, drop the whole logical condition rather than weakening
        // its meaning by pruning only the missing branch.
        dropped++;
        return (string.Empty, string.Empty, string.Empty);
    }

    private static (string Source, string Any, string All, string Ban) ResolveLegacyEffectGroup(
        string source,
        string any,
        string all,
        string ban,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(any) &&
            string.IsNullOrWhiteSpace(all) && string.IsNullOrWhiteSpace(ban))
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }

        bool sourceOk = string.IsNullOrWhiteSpace(source) ||
            (TryParseToken(source.Trim(), out ModelKey sourceKey) &&
             universe.TryResolve(RuntimeAuthorityDomains.Relics, sourceKey, "Legacy.Effect.Source", unresolved));
        if (!sourceOk && !string.IsNullOrWhiteSpace(source) && !TryParseToken(source.Trim(), out _))
            unresolved.Add(new SearchPresetUnresolvedReference("Legacy.Effect.Source", source, "StableIdentityInvalid"));

        bool ok = sourceOk &
                  LegacyCategoryAgnosticTextResolved(any, "Legacy.Effect.Any", universe, unresolved) &
                  LegacyCategoryAgnosticTextResolved(all, "Legacy.Effect.All", universe, unresolved) &
                  LegacyCategoryAgnosticTextResolved(ban, "Legacy.Effect.Ban", universe, unresolved);
        if (ok) return (source, any, all, ban);

        dropped++;
        return (string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private static bool LegacySetTextResolved(
        string text,
        string domain,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string[] tokens = text.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return true;
        bool ok = true;
        foreach (string token in tokens)
        {
            if (!TryParseToken(token, out ModelKey key))
            {
                unresolved.Add(new SearchPresetUnresolvedReference(path, token.Trim(), "StableIdentityInvalid"));
                ok = false;
                continue;
            }
            ok &= universe.TryResolve(domain, key, path, unresolved);
        }
        return ok;
    }

    private static bool LegacyCategoryAgnosticTextResolved(
        string text,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string[] tokens = text.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return true;
        bool ok = true;
        foreach (string token in tokens)
        {
            if (!TryParseToken(token, out ModelKey key))
            {
                unresolved.Add(new SearchPresetUnresolvedReference(path, token.Trim(), "StableIdentityInvalid"));
                ok = false;
                continue;
            }
            ok &= universe.TryResolveAny(key, path, unresolved);
        }
        return ok;
    }

    private static string ResolveLegacyOrdered(
        string text,
        string domain,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        ModelKey[] keys = ParseTokens(text, new[] { '>', ',', '\n', '\r' });
        if (keys.Length == 0 || !AllResolved(keys, domain, path, universe, unresolved))
        {
            dropped++;
            return string.Empty;
        }
        return text;
    }

    private static string ResolveLegacyCategoryAgnosticSet(
        string text,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        ModelKey[] keys = ParseTokens(text, new[] { ',', '\n', '\r' });
        bool ok = keys.Length > 0;
        foreach (ModelKey key in keys)
            ok &= universe.TryResolveAny(key, path, unresolved);
        if (!ok)
        {
            dropped++;
            return string.Empty;
        }
        return text;
    }

    private static string ResolveLegacySequenceText(
        string text,
        string domain,
        string path,
        SemanticUniverseIndex universe,
        List<SearchPresetUnresolvedReference> unresolved,
        ref int dropped)
    {
        var kept = new List<string>();
        string[] conditions = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < conditions.Length; i++)
        {
            string condition = conditions[i];
            string[] tokens = condition.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
                continue;
            ModelKey[] keys = condition
                .Split(new[] { ' ', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token.Contains(':', StringComparison.Ordinal))
                .Select(token => TryParseToken(token, out ModelKey key) ? key : default)
                .Where(key => key.IsValid)
                .ToArray();
            if (keys.Length > 0 && AllResolved(keys, domain, $"{path}[{i}]", universe, unresolved))
                kept.Add(condition);
            else
                dropped++;
        }
        return string.Join("; ", kept);
    }

    private static ModelKey[] ParseTokens(string text, char[] separators) =>
        text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => TryParseToken(token, out ModelKey key) ? key : default)
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();

    private static bool TryParseToken(string token, out ModelKey key) =>
        ModelKey.TryParseExact(token.Trim(), out key) && key.IsValid;

    private static bool HasAuthorityIncomplete(IEnumerable<SearchPresetUnresolvedReference> unresolved) =>
        unresolved.Any(item => item.Reason.StartsWith("CurrentSemanticDomainAuthorityIncomplete:", StringComparison.Ordinal));

    private static int CountConditions(SearchDraft draft)
    {
        int count = 0;
        if (draft.NeowRouteDraft.RouteRelicKey.HasValue)
        {
            count++;
            if (draft.NeowRouteDraft.RequiredBonesRelics.Count > 0) count++;
            count += draft.NeowRouteDraft.EffectConditions.Count;
        }
        foreach (BossMapVariantSearchDraft row in draft.BossMapDraft.Rows)
        {
            if (row.HasSelectableVariant)
                count += row.IsVariantActive ? 1 : 0;
            else
            {
                if (row.FirstBossAny.Count > 0) count++;
                if (row.Act == 3 && draft.BossMapDraft.IncludeSecondAct3Boss && row.SecondBossAny.Count > 0) count++;
            }
        }
        count += draft.AncientMatrixDraft.Rows.Count(row => row.IsActive);
        count += draft.RelicSequenceDraft.Count(condition => !condition.IsEmpty);
        if (draft.RelicSequenceDraft.Count == 0 && !string.IsNullOrWhiteSpace(draft.RelicSequenceConditions))
            count += draft.RelicSequenceConditions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        count += draft.EventSequenceDraft.Count(condition => !condition.IsEmpty);
        if (draft.EventSequenceDraft.Count == 0 && !string.IsNullOrWhiteSpace(draft.EventSequenceConditions))
            count += draft.EventSequenceConditions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        count += draft.EventResultDraft.Count(condition => condition.IsValid);
        count += draft.MerchantColorlessDraft.Count(condition => condition.IsValid);
        if (draft.CombatRewardDraft.Cards.HasAnyValue) count += Math.Max(1, draft.CombatRewardDraft.Cards.Count);
        if (draft.CombatRewardDraft.Potions.HasAnyValue) count += Math.Max(1, draft.CombatRewardDraft.Potions.Count);

        // Narrow historical compatibility fields. These are counted only when no
        // modern typed equivalent is active, avoiding double-counting current assets.
        if (!draft.NeowRouteDraft.RouteRelicKey.HasValue)
        {
            if (AnyText(draft.NeowAny, draft.NeowAll, draft.NeowBan)) count++;
            if (AnyText(draft.BonesAny, draft.BonesAll, draft.BonesBan)) count++;
            if (draft.RequireBones) count++;
            if (draft.RequireSmallCapsule) count++;
            if (draft.RequireLargeCapsule) count++;
            if (draft.RequireWhetstone) count++;
            if (draft.RequireWarPaint) count++;
            if (AnyText(draft.CapsuleRelicAny, draft.CapsuleRelicAll, draft.CapsuleRelicBan)) count++;
            if (!string.IsNullOrWhiteSpace(draft.RequiredFinalCurse)) count++;
            if (!string.IsNullOrWhiteSpace(draft.BannedFinalCurses)) count++;
            if (!string.IsNullOrWhiteSpace(draft.BonesAcquisitionOrder)) count++;
            if (AnyText(draft.EffectOutputSource, draft.EffectOutputAny, draft.EffectOutputAll, draft.EffectOutputBan)) count++;
        }
        if (!draft.BossMapDraft.CatalogBound)
        {
            if (AnyText(draft.BossAny, draft.BossAll, draft.BossBan)) count++;
            if (AnyText(draft.BossOrdinalAny, draft.BossOrdinalAll, draft.BossOrdinalBan)) count++;
        }
        if (draft.AncientMatrixDraft.Rows.Count == 0)
        {
            if (AnyText(draft.AncientAny, draft.AncientAll, draft.AncientBan)) count++;
            if (AnyText(draft.AncientOptionAny, draft.AncientOptionAll, draft.AncientOptionBan)) count++;
            if (AnyText(draft.SeaGlassTargetAny, draft.SeaGlassTargetAll, draft.SeaGlassTargetBan)) count++;
        }
        return count;

        static bool AnyText(params string[] values) => values.Any(value => !string.IsNullOrWhiteSpace(value));
    }

    private sealed class SemanticUniverseIndex
    {
        private readonly Dictionary<string, HashSet<string>> _domains = new(StringComparer.Ordinal);
        private readonly HashSet<string> _all = new(StringComparer.Ordinal);
        private readonly HashSet<string> _complete = new(StringComparer.Ordinal);

        public SemanticUniverseIndex(RuntimeAuthoritySnapshot authority)
        {
            foreach (RuntimeSemanticDomainSnapshot domain in authority.SemanticUniverse)
            {
                var set = new HashSet<string>(
                    domain.Items.Select(item => item.StableIdentity).Where(value => !string.IsNullOrWhiteSpace(value)),
                    StringComparer.Ordinal);
                _domains[domain.Domain] = set;
                _all.UnionWith(set);
                if (domain.AuthorityComplete) _complete.Add(domain.Domain);
            }
        }

        public bool TryResolve(
            string domain,
            ModelKey key,
            string path,
            List<SearchPresetUnresolvedReference> unresolved)
        {
            if (!_complete.Contains(domain) || !_domains.TryGetValue(domain, out HashSet<string>? values))
            {
                unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "CurrentSemanticDomainAuthorityIncomplete:" + domain));
                return false;
            }
            if (values.Contains(key.Serialized)) return true;
            unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "StableIdentityMissingFromCurrentSemanticUniverse:" + domain));
            return false;
        }

        public bool TryResolveAny(
            ModelKey key,
            string path,
            List<SearchPresetUnresolvedReference> unresolved)
        {
            string[] candidateDomains = key.Category switch
            {
                BaseGameModelKeys.Categories.Character => new[] { RuntimeAuthorityDomains.Characters },
                BaseGameModelKeys.Categories.Card => new[] { RuntimeAuthorityDomains.Cards },
                BaseGameModelKeys.Categories.Relic => new[] { RuntimeAuthorityDomains.Relics },
                BaseGameModelKeys.Categories.Potion => new[] { RuntimeAuthorityDomains.Potions },
                BaseGameModelKeys.Categories.Encounter => new[] { RuntimeAuthorityDomains.Bosses },
                // Ancient identities are EVENT:* in the current Beta111 runtime authority,
                // so EVENT references may legitimately live in either domain.
                BaseGameModelKeys.Categories.Event => new[] { RuntimeAuthorityDomains.Events, RuntimeAuthorityDomains.Ancients },
                _ => Array.Empty<string>()
            };

            if (candidateDomains.Length > 0)
            {
                foreach (string domain in candidateDomains)
                {
                    if (_domains.TryGetValue(domain, out HashSet<string>? values) && values.Contains(key.Serialized))
                        return true;
                }
                foreach (string domain in candidateDomains)
                {
                    if (!_complete.Contains(domain))
                    {
                        unresolved.Add(new SearchPresetUnresolvedReference(
                            path,
                            key.Serialized,
                            "CurrentSemanticDomainAuthorityIncomplete:" + domain));
                        return false;
                    }
                }
                unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "StableIdentityMissingFromCurrentSemanticUniverse"));
                return false;
            }

            if (_all.Contains(key.Serialized)) return true;
            if (_complete.Count != _domains.Count)
            {
                unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "CurrentSemanticDomainAuthorityIncomplete:UnknownCategory"));
                return false;
            }
            unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "StableIdentityMissingFromCurrentSemanticUniverse"));
            return false;
        }
    }
}
