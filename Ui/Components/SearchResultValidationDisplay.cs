using RolltheSpire2.Presentation.Ui1;
using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal static class SearchResultValidationDisplay
{
    internal static string StatusKey(PersistedSearchResult result) =>
        EstimateStatus(result.ReplayEstimate) ?? StatusKey(result.IsUnverified, result.IsUnfiltered, result.EffectiveValidationDisposition);

    internal static string StatusKey(SearchDisposition disposition) =>
        StatusKey(disposition != SearchDisposition.Match, false, disposition);

    internal static string StatusKey(SeedLibraryResultProvenance origin) =>
        EstimateStatus(origin.ReplayEstimate) ?? StatusKey(origin.IsUnverified, origin.IsUnfiltered, origin.ValidationDisposition);

    private static string? EstimateStatus(SearchReplayEstimate? estimate) => estimate?.Disposition switch
    {
        SearchDisposition.Match => "workflow.replay_hit",
        SearchDisposition.NoMatch => "workflow.replay_non_hit",
        _ => null
    };

    internal static string StatusKey(bool unverified, bool unfiltered, SearchDisposition disposition) => unfiltered ? "workflow.unfiltered" :
        disposition switch
        {
            SearchDisposition.Unknown => "workflow.validation_unknown",
            SearchDisposition.Unsupported => "workflow.validation_unsupported",
            SearchDisposition.NoMatch => "workflow.validation_mismatch",
            SearchDisposition.Cancelled => "workflow.validation_cancelled",
            _ => unverified ? "workflow.unverified" : "workflow.verified"
        };

    internal static void AddReason(VBoxContainer content, PersistedSearchResult result,
        WorkspacePalette palette, IUiTextProvider text)
    {
        MapSearchProvenanceDisplay.Add(content, result.MapSearch, palette, text);
        AddReason(content, result.IsUnverified, result.EffectiveValidationDisposition, result.ValidationIssue, result.ReplayEstimate, palette, text);
    }

    internal static void AddReason(VBoxContainer content, SeedLibraryResultProvenance origin,
        WorkspacePalette palette, IUiTextProvider text)
    {
        MapSearchProvenanceDisplay.Add(content, origin.MapSearch, palette, text);
        AddReason(content, origin.IsUnverified, origin.ValidationDisposition, origin.ValidationIssue, origin.ReplayEstimate, palette, text);
    }

    private static void AddReason(VBoxContainer content, bool unverified, SearchDisposition disposition, string issue, SearchReplayEstimate? estimate,
        WorkspacePalette palette, IUiTextProvider text)
    {
        bool incomplete = disposition is SearchDisposition.Unknown or SearchDisposition.Unsupported;
        if (!incomplete && (!unverified || string.IsNullOrWhiteSpace(issue))) return;
        if (estimate is not null)
        {
            if (estimate.Assumptions.Length > 0)
                issue += "\n" + text.Format("workflow.replay_assumptions", ReplayAssumptionPresentation.Describe(text, estimate.Assumptions)) +
                    "\n" + estimate.Assumptions;
            issue += (estimate.Issue.Length > 0 ? "\n" + estimate.Issue : "") +
                (estimate.Routes.Count > 0 ? "\n" + string.Join(", ", estimate.Routes) : "");
            foreach (var fact in estimate.Evidence.Where(e => e.RelatedKey.HasValue || e.OrderedOptionKeys?.Count > 0))
                issue += "\n" + string.Join(" · ", new[] { fact.RouteId, fact.RelatedKey?.Serialized,
                    string.Join(", ", fact.OrderedOptionKeys?.Select(key => key.Serialized) ?? []) }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        var detail = palette.Label((incomplete ? text.Get("workflow.incomplete_detail") + "\n" : "") + issue, 15, true);
        detail.Visible = false; detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var button = palette.CompactButton(text.Get("workflow.validation_reason"), 32, 14);
        button.CustomMinimumSize = new Vector2(128, 32);
        button.ClipText = false;
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        button.Pressed += () => detail.Visible = !detail.Visible;
        content.AddChild(button); content.AddChild(detail);
    }
}
