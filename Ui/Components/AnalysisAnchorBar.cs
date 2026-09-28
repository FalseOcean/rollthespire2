using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal enum AnalysisReportAnchor
{
    Opening,
    Ancient,
    RelicShop,
    Act1,
    Act2,
    Act3
}

internal sealed record AnalysisAnchorPreviewItem(
    ModelKey Key,
    GameContentKind Kind,
    IconVariant Variant,
    string DisplayName);

internal sealed partial class AnalysisAnchorBar : PanelContainer
{
    private readonly Dictionary<AnalysisReportAnchor, Button> _buttons = new();
    private readonly Dictionary<AnalysisReportAnchor, Label> _labels = new();
    private readonly Dictionary<AnalysisReportAnchor, HBoxContainer> _previews = new();
    private readonly IGameIconResolver _icons;
    private AnalysisReportAnchor _selected = AnalysisReportAnchor.Opening;

    public AnalysisAnchorBar(IGameIconResolver icons)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Input, 3f, 1, 5f);
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 4);
        foreach (AnalysisReportAnchor anchor in Enum.GetValues<AnalysisReportAnchor>())
        {
            var button = new Button
            {
                Flat = false,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 32),
                Text = string.Empty
            };
            var content = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            content.OffsetLeft = 10f;
            content.OffsetRight = -10f;
            content.AddThemeConstantOverride("separation", 8);
            var label = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, wrap: false);
            label.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            var preview = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            preview.AddThemeConstantOverride("separation", 4);
            content.AddChild(label);
            content.AddChild(preview);
            button.AddChild(content);
            AnalysisReportAnchor captured = anchor;
            button.Pressed += () =>
            {
                Select(captured);
                AnchorRequested?.Invoke(captured);
            };
            _buttons[anchor] = button;
            _labels[anchor] = label;
            _previews[anchor] = preview;
            row.AddChild(button);
        }
        AddChild(row);
        RefreshStyles();
    }

    public event Action<AnalysisReportAnchor>? AnchorRequested;

    public void ApplyLocalization(IUiTextProvider uiText)
    {
        _labels[AnalysisReportAnchor.Opening].Text = uiText.Get(Ui1TextKey.AnalysisAnchorOpening);
        _labels[AnalysisReportAnchor.Ancient].Text = uiText.Get(Ui1TextKey.AnalysisAnchorAncient);
        _labels[AnalysisReportAnchor.RelicShop].Text = uiText.Get(Ui1TextKey.AnalysisAnchorRelicShop);
        _labels[AnalysisReportAnchor.Act1].Text = uiText.Get(Ui1TextKey.AnalysisAnchorAct1);
        _labels[AnalysisReportAnchor.Act2].Text = uiText.Get(Ui1TextKey.AnalysisAnchorAct2);
        _labels[AnalysisReportAnchor.Act3].Text = uiText.Get(Ui1TextKey.AnalysisAnchorAct3);
        RefreshStyles();
    }

    public void BindPreviews(
        IReadOnlyDictionary<AnalysisReportAnchor, IReadOnlyList<AnalysisAnchorPreviewItem>> previews)
    {
        foreach (AnalysisReportAnchor anchor in Enum.GetValues<AnalysisReportAnchor>())
        {
            HBoxContainer host = _previews[anchor];
            ClearPreview(host);
            if (!previews.TryGetValue(anchor, out IReadOnlyList<AnalysisAnchorPreviewItem>? items))
            {
                continue;
            }

            foreach (AnalysisAnchorPreviewItem item in items)
            {
                IconDescriptor descriptor = _icons.Resolve(item.Key, item.Kind, item.Variant);
                if (descriptor.Texture is null || descriptor.IsMissing ||
                    !GodotObject.IsInstanceValid(descriptor.Texture))
                {
                    continue;
                }

                var icon = new TextureRect
                {
                    Name = "PreviewIcon",
                    Texture = descriptor.Texture,
                    CustomMinimumSize = new Vector2(22f, 22f),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                host.AddChild(icon);
            }
        }
    }

    public void Select(AnalysisReportAnchor anchor)
    {
        _selected = anchor;
        RefreshStyles();
    }

    private void RefreshStyles()
    {
        foreach ((AnalysisReportAnchor anchor, Button button) in _buttons)
        {
            Ui1Theme.ApplyButton(
                button,
                anchor == _selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
        }
    }

    private static void ClearPreview(Node host)
    {
        foreach (Node child in host.GetChildren())
        {
            host.RemoveChild(child);
            child.QueueFree();
        }
    }
}
