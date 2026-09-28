using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class GlobalHeader : PanelContainer
{
    private readonly Label _product;
    private readonly Label _pageTitle;
    private readonly Label _runtime;
    private readonly Button _close;
    private readonly Control _dragHandle;
    private ModRuntimeSnapshot _snapshot = ModRuntimeSnapshot.NotInitialized;
    private IUiTextProvider? _uiText;
    private string _currentPageTextKey = Ui1TextKey.PageAnalysis;

    public GlobalHeader()
    {
        CustomMinimumSize = new Vector2(0, Ui1Metrics.HeaderHeight);
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Header, 0f, 0, 10f);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _product = Ui1Theme.Label("RolltheSpire2", Ui1TextRole.AppTitle);
        _product.CustomMinimumSize = new Vector2(210, 0);
        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _pageTitle.CustomMinimumSize = new Vector2(170, 0);
        _runtime = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _runtime.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _runtime.HorizontalAlignment = HorizontalAlignment.Right;

        _close = new Button { Text = "×", CustomMinimumSize = new Vector2(42, 36), TooltipText = "Close" };
        Ui1Theme.ApplyButton(_close, Ui1ButtonRole.WindowControl);

        _dragHandle = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _dragHandle.GuiInput += input => DragInput?.Invoke(input);
        var titleRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        titleRow.AddThemeConstantOverride("separation", 10);
        titleRow.AddChild(_product);
        titleRow.AddChild(_pageTitle);
        titleRow.AddChild(_runtime);
        _dragHandle.AddChild(titleRow);
        titleRow.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        row.AddChild(_dragHandle);
        row.AddChild(_close);
        AddChild(row);
        _close.Pressed += () => CloseRequested?.Invoke();
    }

    public event Action? CloseRequested;
    public event Action<InputEvent>? DragInput;

    public void BindRuntime(ModRuntimeSnapshot snapshot)
    {
        _snapshot = snapshot;
        RefreshText();
    }

    public void SetCurrentPage(AppPageKey pageKey)
    {
        _currentPageTextKey = NavigationRail.TextKeyFor(pageKey);
        RefreshText();
    }

    public void ApplyLocalization(IUiTextProvider uiText)
    {
        _uiText = uiText;
        RefreshText();
    }

    private void RefreshText()
    {
        if (_uiText is null)
        {
            return;
        }
        _product.Text = _uiText.Get(Ui1TextKey.ProductTitle);
        _pageTitle.Text = _uiText.Get(_currentPageTextKey);
        _runtime.Text = _snapshot.RequiresCompatibilityWarning
            ? $"{_snapshot.Detection.DisplayVersion} · ⚠"
            : _snapshot.Detection.DisplayVersion;
        _runtime.TooltipText = _snapshot.IsCompatibilityFallback
            ? _uiText.Format(
                Ui1TextKey.RuntimeFallbackBadgeTooltip,
                _snapshot.Detection.DisplayVersion)
            : _snapshot.IsPendingRuntimeValidation
                ? _uiText.Format(
                    Ui1TextKey.RuntimePendingValidationBadgeTooltip,
                    _snapshot.Detection.DisplayVersion)
                : _uiText.Format(
                    Ui1TextKey.RuntimeBadgeTooltip,
                    _snapshot.Detection.DisplayVersion);
        _runtime.AddThemeColorOverride(
            "font_color",
            _snapshot.RequiresCompatibilityWarning
                ? Ui1Theme.Palette.Warning
                : Ui1Theme.Palette.TextSecondary);
        _close.TooltipText = _uiText.Get(Ui1TextKey.Close);
    }
}
