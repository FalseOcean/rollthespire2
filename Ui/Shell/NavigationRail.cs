using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Capabilities;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class NavigationRail : PanelContainer
{
    private sealed record NavigationItem(AppPageKey Key, string Symbol, string TextKey);

    private static readonly NavigationItem[] Items =
    {
        new(AppPageKey.Search, "⌕", Ui1TextKey.PageSearch),
        new(AppPageKey.Analysis, "◆", Ui1TextKey.PageAnalysis),
        new(AppPageKey.SaveStatus, "▣", Ui1TextKey.PageSaveStatus),
        new(AppPageKey.Advanced, "◇", Ui1TextKey.PageAdvanced),
        new(AppPageKey.DeveloperNotes, "N", Ui1TextKey.PageDeveloperNotes),
        new(AppPageKey.Settings, "⚙", Ui1TextKey.PageSettings)
    };

    private readonly VBoxContainer _column;
    private readonly Dictionary<AppPageKey, Button> _buttons = new();
    private AppPageKey _selected = AppPageKey.Analysis;
    private bool _compact;
    private IUiTextProvider? _uiText;

    public NavigationRail()
    {
        CustomMinimumSize = new Vector2(Ui1Metrics.NavigationWidth, 0);
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Navigation, 0f, 0, 10f);
        _column = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _column.AddThemeConstantOverride("separation", 7);
        AddChild(_column);
        BuildButtons();
    }

    public event Action<AppPageKey>? NavigateRequested;

    public void ApplyLocalization(IUiTextProvider uiText)
    {
        _uiText = uiText;
        RefreshButtonText();
    }

    public void Select(AppPageKey key)
    {
        _selected = key;
        RefreshStyles();
    }

    public void SetCompact(bool compact)
    {
        if (_compact == compact)
        {
            return;
        }
        _compact = compact;
        CustomMinimumSize = new Vector2(compact ? Ui1Metrics.NavigationCompactWidth : Ui1Metrics.NavigationWidth, 0);
        RefreshButtonText();
    }

    private void BuildButtons()
    {
        foreach (NavigationItem item in Items)
        {
            if (item.Key == AppPageKey.Settings)
            {
                var spacer = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
                _column.AddChild(spacer);
                var separator = new HSeparator();
                Ui1Theme.ApplySeparator(separator);
                _column.AddChild(separator);
            }

            var button = new Button
            {
                CustomMinimumSize = new Vector2(0, 48),
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.All
            };
            AppPageKey captured = item.Key;
            button.Pressed += () => NavigateRequested?.Invoke(captured);
            _buttons[item.Key] = button;
            _column.AddChild(button);
        }
        RefreshStyles();
    }

    private void RefreshButtonText()
    {
        if (_uiText is null)
        {
            return;
        }
        foreach (NavigationItem item in Items)
        {
            Button button = _buttons[item.Key];
            string title = _uiText.Get(item.TextKey);
            Ui1CapabilityModel capability = Ui1CapabilityCatalog.ForPage(item.Key);
            string badge = capability.State == Ui1CapabilityState.Supported || _compact
                ? string.Empty
                : $"  {Ui1CapabilityCatalog.BadgeSymbol(capability.State)}";
            button.Text = _compact ? item.Symbol : $"{item.Symbol}  {title}{badge}";
            button.TooltipText = capability.State == Ui1CapabilityState.Supported
                ? (_compact ? _uiText.Format(Ui1TextKey.NavigationCompactTooltip, title) : title)
                : $"{title} — {_uiText.Get(capability.TitleKey)}\n{_uiText.Get(capability.MessageKey)}";
            button.Alignment = _compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }
    }

    private void RefreshStyles()
    {
        foreach ((AppPageKey key, Button button) in _buttons)
        {
            Ui1Theme.ApplyButton(button, key == _selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Navigation);
        }
    }

    public static string TextKeyFor(AppPageKey key) => key switch
    {
        AppPageKey.Search => Ui1TextKey.PageSearch,
        AppPageKey.Analysis => Ui1TextKey.PageAnalysis,
        AppPageKey.Events => Ui1TextKey.PageEvents,
        AppPageKey.SaveStatus => Ui1TextKey.PageSaveStatus,
        AppPageKey.Advanced => Ui1TextKey.PageAdvanced,
        AppPageKey.DeveloperNotes => Ui1TextKey.PageDeveloperNotes,
        _ => Ui1TextKey.PageSettings
    };
}
