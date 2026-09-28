using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Event;

internal sealed partial class EventSequencePickerSlot : Button
{
    private readonly Label _name;
    private IGameContentNameResolver? _names;
    private EventSearchUiCandidate? _candidate;
    private string _emptyText = string.Empty;
    private Func<EventSearchUiCandidate, string>? _tooltipBuilder;

    public EventSequencePickerSlot()
    {
        CustomMinimumSize = new Vector2(224f, 42f);
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
        Ui1Theme.ApplyButton(this, Ui1ButtonRole.Secondary);

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _name = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _name.MouseFilter = MouseFilterEnum.Ignore;
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        margin.AddChild(_name);
        AddChild(margin);
        RefreshVisual();
    }

    public event Action? SelectionChanged;

    public ModelKey? SelectedKey => _candidate?.EventKey;

    public void BindText(
        IGameContentNameResolver names,
        string emptyText,
        Func<EventSearchUiCandidate, string> tooltipBuilder)
    {
        _names = names;
        _emptyText = emptyText;
        _tooltipBuilder = tooltipBuilder;
        RefreshVisual();
    }

    public void SetSelection(EventSearchUiCandidate? candidate, bool notify)
    {
        _candidate = candidate;
        RefreshVisual();
        if (notify)
        {
            SelectionChanged?.Invoke();
        }
    }

    private void RefreshVisual()
    {
        if (_candidate is null)
        {
            _name.Text = "＋  " + _emptyText;
            TooltipText = string.Empty;
            return;
        }

        string displayName = _names?.Resolve(_candidate.EventKey, GameContentKind.Event) ?? _candidate.EventKey.Entry;
        _name.Text = displayName;
        TooltipText = _tooltipBuilder?.Invoke(_candidate) ?? displayName;
    }
}
