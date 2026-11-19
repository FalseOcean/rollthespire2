using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;

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
            results = Beta111EventOptionResultProjector.Project(root, LastRequest.PlayerSlotIndex, entry,
                LastRequest.Authority.VersionResolution.SemanticProfileId);
            _stableEventResults[entry] = results;
        }
        if (results.Count == 0) return false;

        var options = WResponsiveGrid(detail, "StableEventOptions", 240, 2);
        foreach (var result in results)
        {
            var rarityBranch = result.RarityBranch;
            var branch = WCard(options, rarityBranch is { } choice
                ? EventOpportunityPresentation.Branch(_uiText!, choice)
                : Text($"predictor.event.stable.{entry}.{result.OptionKey}"));
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
                string? premise = entry switch
                {
                    EventOpportunityCatalog.MuseumOfTorture => "query.event.opportunity.premise.MuseumOfTortureOfferedRelic",
                    EventOpportunityCatalog.ThermaeNonupeipe => "query.event.opportunity.premise.ThermaeNonupeipeDrinkRelic",
                    _ => null
                };
                if (premise is not null) branch.AddChild(WNote(Text(premise)));
            }
            if (rarityBranch is not { } rolledBranch) continue;
            for (int i = 0; i < result.RelicRarityRolls.Count; i++)
            {
                string rarity = EventOpportunityPresentation.Rarity(_uiText!, result.RelicRarityRolls[i]);
                string label = result.RelicRarityRolls.Count == 1
                    ? Text("query.event.opportunity.rolls")
                    : EventOpportunityPresentation.RollSelection(_uiText!, rolledBranch,
                        (EventRelicRaritySelection)i);
                var roll = WLabel(_uiText!.Format("predictor.event.rarity_roll", label, rarity), true);
                roll.Name = "RelicRarityRoll" + (i + 1);
                branch.AddChild(roll);
            }
        }
        if (results.Any(result => result.RarityBranch.HasValue))
            detail.AddChild(WNote(Text("predictor.event.rarity_scope")));
        return true;
    }
}
