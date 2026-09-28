using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Report-style section: typography + spacing + separator, deliberately not a nested panel.
/// </summary>
internal sealed partial class AnalysisReportSection : VBoxContainer
{
    private readonly string _titleKey;
    private readonly Label _title;
    private readonly HSeparator _separator;
    private readonly VBoxContainer _content;

    public AnalysisReportSection(string titleKey)
    {
        _titleKey = titleKey;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.PageTitle);
        _separator = new HSeparator();
        Ui1Theme.ApplySeparator(_separator);
        _content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 10);
        AddChild(_title);
        AddChild(_separator);
        AddChild(_content);
    }

    public VBoxContainer Content => _content;

    public void SetHeaderVisible(bool visible)
    {
        _title.Visible = visible;
        _separator.Visible = visible;
    }

    public void ApplyLocalization(IUiTextProvider uiText) => _title.Text = uiText.Get(_titleKey);
}
