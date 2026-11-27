using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunOperations;

// Frozen before capture; workers never read live preferences or another mod.
internal sealed record OperationDemand(OperationPredictionPreferences Preferences, string PrimaryFeature,
    bool MayPredict, OperationSurfaceDemand Surface, string RfSignature)
{
    internal ImmutableHashSet<string> VisibleFeatures { get; init; } = [];
    internal OperationPotionDemand Potion { get; init; } = new();
    internal bool ShowPrimary { get; init; } = true;
    internal bool ChainOnly { get; init; }
    internal bool HideBonesRelics { get; init; }
    internal bool HideSharedBonesCurse { get; init; }
    internal bool IncludeChain { get; init; } = true;
    internal bool IncludeTransformEffects { get; init; } = true;
    internal bool IncludeEventContinuation { get; init; } = true;
    internal bool IncludeEventCombat { get; init; } = true;
    internal bool TransformSource { get; init; }
    internal bool HasParentChain { get; init; }
    internal int? PublicSacrificeProgress { get; init; }
    internal bool Enabled(string feature) => Preferences.Get(feature) != OperationPredictionMode.Off;
    internal bool SamePolicy(OperationDemand other) => Preferences == other.Preferences &&
        PrimaryFeature == other.PrimaryFeature && MayPredict == other.MayPredict && Surface == other.Surface &&
        RfSignature == other.RfSignature && Potion == other.Potion && ShowPrimary == other.ShowPrimary &&
        ChainOnly == other.ChainOnly && HideBonesRelics == other.HideBonesRelics &&
        HideSharedBonesCurse == other.HideSharedBonesCurse && IncludeChain == other.IncludeChain &&
        IncludeTransformEffects == other.IncludeTransformEffects &&
        IncludeEventContinuation == other.IncludeEventContinuation && IncludeEventCombat == other.IncludeEventCombat &&
        TransformSource == other.TransformSource && HasParentChain == other.HasParentChain &&
        PublicSacrificeProgress == other.PublicSacrificeProgress &&
        VisibleFeatures.SetEquals(other.VisibleFeatures);
}

internal static class OperationPredictionPolicy
{
    internal static bool Wants(OperationPredictionMode mode, bool duplicateEnabled, bool supplement) =>
        mode != OperationPredictionMode.Off && (mode == OperationPredictionMode.On || !duplicateEnabled || supplement);

    internal static string SourceFeature(ModelKey source) => source.Category switch
    {
        "RELIC" => "relic:" + source.Entry,
        "EVENT" => "event:" + source.Entry,
        "POTION" => "potion:" + source.Entry,
        _ => ""
    };

    internal static bool IsRelicTransform(string? source) => source is
        "NEW_LEAF" or "ASTROLABE" or "PANDORAS_BOX" or "LEAFY_POULTICE";

    internal static bool IsEventTransform(string? source, string option) => (source, option) is
        ("AROMA_OF_CHAOS", "LET_GO") or ("MORPHIC_GROVE", "GROUP") or
        ("SYMBIOTE", "KILL_WITH_FIRE") or ("WHISPERING_HOLLOW", "HUG") or
        ("ENDLESS_CONVEYOR", "GRAB_JELLY_LIVER") or ("TRIAL", "NONDESCRIPT_INNOCENT");

    internal static OperationPreview Filter(OperationPreview preview, OperationDemand demand)
    {
        // A random relic needs settlement to discover pickup consequences.
        // A plain identity alone does not justify an Auto duplicate panel.
        bool hasChain = preview.Branches.Any(branch => branch.Sections.Any(section =>
            section.FeatureId == null && section.Source != preview.Source &&
            demand.Enabled(SourceFeature(section.Source)) &&
            (!section.Rows.IsEmpty || !section.CardGroups.IsEmpty || section.EmptyReason != null)));
        bool branchCurse = demand.HideBonesRelics && preview.CurseCandidates.IsEmpty && preview.Branches.Any(branch =>
            branch.Sections.Any(section => section.Source == preview.Source && section.Rows.Any(row => row.Action == OperationPreviewAction.AddCard)));
        bool showPrimary = demand.ShowPrimary && (!demand.ChainOnly || hasChain || branchCurse);
        bool Keep(OperationPreviewSection section)
        {
            if (section.FeatureId is { } feature) return demand.VisibleFeatures.Contains(feature);
            if (!showPrimary) return false;
            string own = SourceFeature(section.Source);
            if (!string.IsNullOrEmpty(own) && !demand.Enabled(own)) return false;
            return true;
        }
        bool DuplicateBonesRow(OperationPreviewRow row)
        {
            if (row.Action == OperationPreviewAction.ObtainRelic)
                return !row.Models.IsEmpty && row.Models.All(preview.ContainedRelics.Contains);
            if (!demand.HideSharedBonesCurse || row.Action != OperationPreviewAction.AddCard || row.Cards.IsEmpty)
                return false;
            return row.Cards.All(card => preview.CurseCandidates.Any(c => (c with { Id = 0 }) == (card with { Id = 0 })));
        }
        var branches = preview.Branches.Select(branch =>
        {
            var sections = branch.Sections.Where(Keep).Select(section =>
            {
                if (demand.HideBonesRelics && section.Source == preview.Source)
                    section = section with { Rows = section.Rows.Where(row => !DuplicateBonesRow(row)).ToImmutableArray() };
                if (!demand.TransformSource || demand.IncludeTransformEffects ||
                    demand.HasParentChain && demand.IncludeChain || section.FeatureId != null ||
                    section.Category == OperationPredictionCategory.CardTransform) return section;
                return section with
                {
                    Rows = section.Rows.Where(row => row.Action == OperationPreviewAction.TransformCard).ToImmutableArray(),
                    CardGroups = [], EmptyReason = null, Label = null
                };
            }).Where(section => !section.Rows.IsEmpty || !section.CardGroups.IsEmpty || section.EmptyReason != null).ToImmutableArray();
            return branch with
            {
                Sections = sections,
                HuntOutcomes = showPrimary ? branch.HuntOutcomes.Select(outcome => outcome with
                    { Sections = outcome.Sections.Where(Keep).ToImmutableArray() }).ToImmutableArray() : [],
                PendingReasons = showPrimary || !sections.IsEmpty ? branch.PendingReasons : [],
                Label = showPrimary || !sections.IsEmpty ? branch.Label : null
            };
        }).ToImmutableArray();
        bool visible = showPrimary || branches.Any(branch => !branch.Sections.IsEmpty);
        // BuildShop's purchase/continuation conditions also belong to the
        // requested replacement, even when there is no origin effect row.
        bool shopRestock = demand.VisibleFeatures.Contains("shop:restock") ||
            demand.VisibleFeatures.Contains("extra:restock-whetstone");
        return preview with
        {
            Branches = branches,
            ContainedRelics = showPrimary && !demand.HideBonesRelics ? preview.ContainedRelics : [],
            CurseCandidates = showPrimary && !demand.HideSharedBonesCurse ? preview.CurseCandidates : [],
            Notices = visible ? preview.Notices : preview.Notices.Where(notice =>
                shopRestock && notice.StartsWith("Purchase:", StringComparison.Ordinal)).ToImmutableArray(),
            PendingReasons = visible ? preview.PendingReasons : preview.PendingReasons.Where(reason =>
                shopRestock && reason.StartsWith("CourierRestockAwaitingPlayerChoice:", StringComparison.Ordinal)).ToImmutableArray(),
            Faults = demand.MayPredict ? preview.Faults : []
        };
    }
}
