using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal static class MapSearchProvenanceDisplay
{
    internal static string Describe(MapSearchProvenance scan, IUiTextProvider text)
    {
        string status = !scan.RequestedOptions.Enabled ? text.Get("map.scan.full") :
            !scan.Applied ? text.Format("map.scan.not_applied", Reason(scan.Reason, text)) :
            scan.ObservedProposalCalls == 0 ? text.Get("map.scan.selected") :
            text.Format("map.scan.applied", scan.Backend);
        string range = text.Format("map.scan.range", scan.StartOrdinal, scan.EndOrdinalExclusive);
        string scope = scan.Scope.Length == 0 ? "" : " · " + text.Format("map.scan.scope", scan.Scope);
        string remainder = scan.FullFinalRemainders.Count == 0 ? "" : "\n" +
            text.Format("map.scan.remainders", string.Join("; ", scan.FullFinalRemainders.Select(value => Reason(value, text))));
        return status + "\n" + range + scope + remainder;
    }

    internal static void Add(VBoxContainer content, MapSearchProvenance? scan, WorkspacePalette palette, IUiTextProvider text)
    {
        if (scan is null) return;
        var label = palette.Label(Describe(scan, text), 14, true);
        label.Name = "MapSearchProvenance";
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(label);
    }

    private static string Reason(string reason, IUiTextProvider text)
    {
        string[] parts = reason.Split(':', 2);
        string token = parts[^1];
        string? key = token switch
        {
            "Disabled" => "disabled", "Beta111StandardMapsOnly" => "profile", "SingleplayerOnly" => "solo",
            "NoSupportedMapConditions" => "conditions", "OnlyPositiveEliteRestGroupsSupported" => "rooms",
            "AutomatonExceeds32States" => "states", "NoSelectiveRequirement" => "empty",
            "DrawingStateTableCapacityExceeded" or "DrawingProgramExceeds512States" => "states",
            "AdditionalCountsFullFinalOnly" or "UnprovenCountableRoomBudget" => "full_only",
            "FullFinalOnly" => "full_only", "NoProposalInvocationsObserved" => "unobserved",
            "NoMapPrefilterSelected" => "final_direct", _ => null
        };
        return key is null ? reason : (parts.Length > 1 ? parts[0] + ": " : "") + text.Get("map.scan.reason." + key);
    }
}
