using Godot;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Event;

internal sealed partial class EventSequenceConditionCard : PanelContainer
{
    public EventSequenceConditionCard(
        EventThumbnailDescriptor thumbnail,
        string eventName,
        string detailText,
        string tooltipText,
        string removeTooltip,
        bool running,
        Action remove,
        Control? effectEditor = null)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(0f, 74f);
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, 9f);

        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new EventThumbnailView(
            thumbnail,
            new Vector2(66f, 48f),
            EventThumbnailPresentation.ConditionSquareCrop));

        var textColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(230f, 0f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        textColumn.AddThemeConstantOverride("separation", 3);

        Label name = Ui1Theme.Label(eventName, Ui1TextRole.CardTitle);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.TooltipText = tooltipText;
        Label detail = Ui1Theme.Label(detailText, Ui1TextRole.Meta);
        detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        detail.ClipText = true;
        detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        detail.TooltipText = detailText;
        textColumn.AddChild(name);
        textColumn.AddChild(detail);

        var removeButton = new Button
        {
            Text = "×",
            TooltipText = removeTooltip,
            CustomMinimumSize = new Vector2(34f, 34f),
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.All,
            Disabled = running
        };
        Ui1Theme.ApplyButton(removeButton, Ui1ButtonRole.Ghost);
        removeButton.Pressed += remove;

        row.AddChild(textColumn);
        if (effectEditor is not null)
        {
            effectEditor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            effectEditor.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(effectEditor);
        }
        else
        {
            row.AddChild(new Control
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore
            });
        }
        row.AddChild(removeButton);
        AddChild(row);
    }
}
