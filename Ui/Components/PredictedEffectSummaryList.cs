using Godot;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// UI1-A compact rendering for already-structured ordered effect groups.
/// It does not infer effects, identity, selection policy, or precision.
/// </summary>
internal sealed partial class PredictedEffectSummaryList : VBoxContainer
{
    private readonly IGameIconResolver _icons;

    public PredictedEffectSummaryList(IGameIconResolver icons)
    {
        _icons = icons;
        AddThemeConstantOverride("separation", 8);
        Visible = false;
    }

    public void Bind(
        IReadOnlyList<PredictedEffectGroupViewModel> groups,
        string missingIconTooltip,
        bool showAll = false)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        PredictedEffectGroupViewModel[] visibleGroups = groups
            .OrderBy(group => group.GroupOrder)
            .Where(group => showAll ? group.OrderedItems.Count > 0 : group.ShowInNormalMode)
            .ToArray();

        Visible = visibleGroups.Length > 0;
        if (!Visible)
        {
            return;
        }

        foreach (PredictedEffectGroupViewModel group in visibleGroups)
        {
            AddChild(new HorizontalOfferGroup(group, _icons, missingIconTooltip, showAll));
        }
    }
}
