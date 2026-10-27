using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private readonly Dictionary<string, IReadOnlyList<Beta111EventOptionResult>> _stableEventResults = [];

    private bool RenderStableEventResults(VBoxContainer detail, string entry)
    {
        if (LastRequest?.Authority.ProfileId != RuntimeProfileId.Beta111 ||
            !TryGetProjectionRoot(out ulong root)) return false;
        if (!_stableEventResults.TryGetValue(entry, out var results))
        {
            results = Beta111EventOptionResultProjector.Project(root, LastRequest.PlayerSlotIndex, entry);
            _stableEventResults[entry] = results;
        }
        if (results.Count == 0) return false;

        var options = WResponsiveGrid(detail, "StableEventOptions", 240, 2);
        foreach (var result in results)
        {
            var branch = WCard(options, Text($"predictor.event.stable.{entry}.{result.OptionKey}"));
            branch.Name = "StableEventOption_" + result.OptionKey;
            if (result.GoldAmount is { } gold)
            {
                bool cost = entry is "LUMINOUS_CHOIR" or "CRYSTAL_SPHERE" or "WHISPERING_HOLLOW";
                branch.AddChild(WLabel(_uiText!.Format(cost ? "predictor.event.stable.cost" :
                    "predictor.event.stable.gold", gold), true));
            }
            else
            {
                if (result.Relics.Count > 1) branch.AddChild(WNote(Text("predictor.event.stable.choose_one")));
                foreach (var relic in result.Relics)
                    branch.AddChild(WObject(relic, GameContentKind.Relic, size: 40));
            }
        }
        return true;
    }
}
