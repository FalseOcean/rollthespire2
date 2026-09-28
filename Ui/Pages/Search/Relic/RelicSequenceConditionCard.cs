using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Relic;

internal sealed partial class RelicSequenceConditionCard : PanelContainer
{
    public RelicSequenceConditionCard(
        RelicSequenceUiCondition condition,
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost,
        IGameContentNameResolver names,
        string detailText,
        string removeTooltip,
        bool running,
        Action remove)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(0f, 68f);
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, 10f);

        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        row.AddThemeConstantOverride("separation", 10);

        string displayName = names.Resolve(condition.RelicKey, GameContentKind.Relic);
        var iconHost = new Control
        {
            CustomMinimumSize = new Vector2(44f, 44f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            ClipContents = true
        };
        IconDescriptor descriptor = icons.Resolve(
            condition.RelicKey,
            GameContentKind.Relic,
            IconVariant.Small);
        var icon = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Texture = descriptor.Texture,
            Visible = descriptor.Texture is not null,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var missing = Ui1Theme.Label(descriptor.Texture is null ? "?" : string.Empty, Ui1TextRole.Muted);
        missing.MouseFilter = MouseFilterEnum.Ignore;
        missing.HorizontalAlignment = HorizontalAlignment.Center;
        missing.VerticalAlignment = VerticalAlignment.Center;
        missing.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        iconHost.AddChild(icon);
        iconHost.AddChild(missing);
        iconHost.MouseEntered += () => tooltipHost.ShowFor(iconHost, condition.RelicKey, displayName);
        iconHost.MouseExited += () => tooltipHost.Dismiss(iconHost);
        iconHost.TreeExiting += () => tooltipHost.Dismiss(iconHost);

        var textColumn = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        textColumn.AddThemeConstantOverride("separation", 2);
        Label name = Ui1Theme.Label(displayName, Ui1TextRole.CardTitle);
        name.MouseFilter = MouseFilterEnum.Ignore;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Label detail = Ui1Theme.Label(detailText, Ui1TextRole.Meta);
        detail.MouseFilter = MouseFilterEnum.Ignore;
        detail.ClipText = true;
        detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        textColumn.AddChild(name);
        textColumn.AddChild(detail);

        var removeButton = new Button
        {
            Text = "×",
            TooltipText = removeTooltip,
            CustomMinimumSize = new Vector2(34f, 34f),
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.All
        };
        Ui1Theme.ApplyButton(removeButton, Ui1ButtonRole.Ghost);
        removeButton.Disabled = running;
        removeButton.Pressed += remove;

        row.AddChild(iconHost);
        row.AddChild(textColumn);
        row.AddChild(removeButton);
        AddChild(row);
    }
}
