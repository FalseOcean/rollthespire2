using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodeDetails
{
    private void BuildCardRewardHeading(Node column, string nodeId, InRunMapDetailSection section,
        string heading, long generation)
    {
        var row = new HBoxContainer { Name = "CardRewardHeading", MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8); column.AddChild(row);
        var title = Text(heading, 14, true); title.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(title);
        var offered = section.Choices.Where(choice => choice.Object?.Kind == GameContentKind.Card)
            .Select(choice => choice.Object!).ToArray();
        var items = offered.Concat(section.Objects.Where(item => item.Kind == GameContentKind.Card && !offered.Contains(item))).ToArray();
        var group = section.LargeCardResults ? RegisterRewardCardGroup(section, items) : null;
        row.MouseEntered += () =>
        {
            if (generation != _hoverGeneration || _data?.NodeId != nodeId || _data.IsCurrent?.Invoke() == false) return;
            CloseOptionPreview();
            LayoutHover();
            FocusRewardCardGroup(group);
        };
        row.MouseExited += () => UnfocusRewardCardGroup(group);
    }

    private void BuildCardRewardRow(Node column, string nodeId, InRunMapDetailSection section,
        IReadOnlyList<InRunMapDetailObject> displayObjects, long generation)
    {
        // The outer result area owns wheel scrolling over cards. The inner
        // scrollbar remains interactive for horizontal navigation.
        var scroll = new ScrollContainer { Name = "CardRewardScroll", MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        // Navigating the main results retires the old secondary anchor. It may
        // otherwise cover another group's newly exposed action after scrolling.
        scroll.GetHScrollBar().ValueChanged += _ =>
        { if (generation == _hoverGeneration) CloseOptionPreview(); };
        column.AddChild(scroll);
        var row = new HBoxContainer { Name = "CardRewardRow", MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        if (section.LargeCardResults) row.SetMeta("map_comparable_reward", true);
        row.AddThemeConstantOverride("separation", 6); scroll.AddChild(row); _rewardRows.Add(row);
        foreach (var item in displayObjects.Where(item => item.Kind == GameContentKind.Card))
            row.AddChild(CardRewardItem(item));
        foreach (var choice in section.Choices.Where(choice => choice.Object?.Kind == GameContentKind.Card))
            AddChoice(row, nodeId, choice, generation, editorAction: false, editor: false, inline: true);
        foreach (var choice in section.Choices.Where(choice => choice.Object?.Kind != GameContentKind.Card))
            AddChoice(row, nodeId, choice, generation, editorAction: false, editor: false, inline: true);
    }

    private Control CardRewardItem(InRunMapDetailObject item)
    {
        string name = item.DisplayName ?? _names.Resolve(item.Key, item.Kind);
        if (item.UpgradeLevel > 0) name += "+" + (item.UpgradeLevel > 1 ? item.UpgradeLevel.ToString() : "");
        // Keep the existing native portrait and atomic name together. The group
        // scrolls at its natural width instead of clipping names or candidates.
        var row = new IconWithLabel(24, Ui1TextRole.Meta) { Name = "CardRewardItem",
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin, MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 5);
        row.Bind(_icons.Resolve(item.Key, item.Kind, IconVariant.Small), name, string.Empty, string.Empty);
        row.TooltipText = string.Empty;
        foreach (Control child in row.GetChildren().OfType<Control>())
        {
            child.MouseFilter = MouseFilterEnum.Ignore;
            if (child is Label label)
            { MainPanelTheme.ApplyFont(label); label.AddThemeFontSizeOverride("font_size", 15); }
        }
        return row;
    }
}
