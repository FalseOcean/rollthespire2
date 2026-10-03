using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Page-local Preset metadata transaction. Query semantics are never edited here.
/// Visual slots are currently backed by Relic picker UI, while persistence stores
/// generic VisualIconRef values so the asset contract is not Relic-specific.
/// </summary>
internal sealed partial class SearchPresetSaveTransactionOverlay : Control
{
    private readonly Label _title;
    private readonly Label _titleLabel;
    private readonly LineEdit _titleInput;
    private readonly Label _descriptionLabel;
    private readonly TextEdit _descriptionInput;
    private readonly Label _visualMarkLabel;
    private readonly Label _validation;
    private readonly RelicSequencePickerSlot[] _visualIconSlots;
    private readonly Button[] _visualIconClear;
    private readonly Button _cancel;
    private readonly Button _save;
    private readonly AnchoredTooltipHost _tooltipHost;

    public SearchPresetSaveTransactionOverlay(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost)
    {
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ZIndex = UiZLayers.TransactionModal;
        SetProcessUnhandledKeyInput(true);

        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.62f),
            MouseFilter = MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Cancel();
        };
        AddChild(backdrop);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var dialog = new PanelContainer
        {
            CustomMinimumSize = new Vector2(700f, 560f),
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplyPanel(dialog, Ui1SurfaceRole.Drawer, 10f, 1, 24f);
        center.AddChild(dialog);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 14);

        var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _title.AddThemeFontSizeOverride("font_size", 26);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var close = new Button { Text = "×", CustomMinimumSize = new Vector2(42f, 44f) };
        Ui1Theme.ApplyButton(close, Ui1ButtonRole.Ghost);
        close.Pressed += Cancel;
        header.AddChild(_title);
        header.AddChild(close);
        root.AddChild(header);

        _titleLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        root.AddChild(_titleLabel);
        _titleInput = new LineEdit
        {
            CustomMinimumSize = new Vector2(420f, 38f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClearButtonEnabled = true
        };
        Ui1Theme.ApplyLineEdit(_titleInput);
        _titleInput.TextChanged += _ => RefreshSaveEnabled();
        root.AddChild(_titleInput);

        _descriptionLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        root.AddChild(_descriptionLabel);
        _descriptionInput = new TextEdit
        {
            CustomMinimumSize = new Vector2(0f, 110f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyTextEdit(_descriptionInput);
        root.AddChild(_descriptionInput);

        _visualMarkLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        root.AddChild(_visualMarkLabel);

        var iconRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        iconRow.AddThemeConstantOverride("separation", 8);
        _visualIconSlots = new RelicSequencePickerSlot[3];
        _visualIconClear = new Button[3];
        for (int index = 0; index < 3; index++)
        {
            int captured = index;
            var slotColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            slotColumn.AddThemeConstantOverride("separation", 4);
            var slot = new RelicSequencePickerSlot(icons, tooltipHost)
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            slot.Pressed += () => VisualIconRequested?.Invoke(captured);
            _visualIconSlots[index] = slot;
            slotColumn.AddChild(slot);

            var clear = new Button { CustomMinimumSize = new Vector2(0f, 28f) };
            Ui1Theme.ApplyButton(clear, Ui1ButtonRole.Ghost);
            clear.Pressed += () => slot.SetSelection(null, notify: false);
            _visualIconClear[index] = clear;
            slotColumn.AddChild(clear);
            iconRow.AddChild(slotColumn);
        }
        root.AddChild(iconRow);

        _validation = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _validation.Visible = false;
        root.AddChild(_validation);
        root.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var footer = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        footer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _cancel = new Button { CustomMinimumSize = new Vector2(92f, 44f) };
        _save = new Button { CustomMinimumSize = new Vector2(104f, 44f), Disabled = true };
        Ui1Theme.ApplyButton(_cancel, Ui1ButtonRole.Ghost);
        Ui1Theme.ApplyButton(_save, Ui1ButtonRole.Primary);
        _cancel.Pressed += Cancel;
        _save.Pressed += () =>
        {
            if (!_save.Disabled) SaveRequested?.Invoke();
        };
        footer.AddChild(_cancel);
        footer.AddChild(_save);
        root.AddChild(footer);

        dialog.AddChild(root);
    }

    public event Action<int>? VisualIconRequested;
    public event Action? SaveRequested;
    public event Action? Cancelled;

    public bool IsOpen => Visible;
    public string TitleText => _titleInput.Text;
    public string DescriptionText => _descriptionInput.Text;
    public IReadOnlyList<ModelKey> SelectedRelicIcons =>
        _visualIconSlots
            .Select(slot => slot.SelectedKey)
            .Where(key => key is { IsValid: true })
            .Select(key => key!.Value)
            .ToArray();

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(names);
        _titleLabel.Text = text.Get(Ui1TextKey.SearchPresetTitleLabel);
        _titleInput.PlaceholderText = text.Get(Ui1TextKey.SearchPresetNamePrompt);
        _descriptionLabel.Text = text.Get(Ui1TextKey.SearchPresetDescriptionLabel);
        _descriptionInput.PlaceholderText = text.Get(Ui1TextKey.SearchPresetDescriptionPlaceholder);
        _visualMarkLabel.Text = text.Get(Ui1TextKey.SearchPresetVisualMark);
        for (int index = 0; index < _visualIconSlots.Length; index++)
        {
            _visualIconSlots[index].BindText(names, text.Get(Ui1TextKey.SearchPresetChooseIcon));
            _visualIconClear[index].Text = text.Get(Ui1TextKey.SearchPresetClearIcon);
        }
        _cancel.Text = text.Get(Ui1TextKey.SearchPresetCancel);
        _save.Text = text.Get(Ui1TextKey.SearchPresetSaveConfirm);
    }

    public void OpenCreate(
        string dialogTitle,
        string initialTitle = "",
        string initialDescription = "",
        IReadOnlyList<ModelKey>? relicIcons = null)
    {
        _tooltipHost.Dismiss();
        _title.Text = dialogTitle ?? string.Empty;
        _titleInput.Text = initialTitle ?? string.Empty;
        _descriptionInput.Text = initialDescription ?? string.Empty;
        _validation.Text = string.Empty;
        _validation.Visible = false;
        ApplyRelicIcons(relicIcons);
        Visible = true;
        RefreshSaveEnabled();
        _titleInput.GrabFocus();
    }

    public void Cancel()
    {
        if (!Visible) return;
        _tooltipHost.Dismiss();
        Visible = false;
        Cancelled?.Invoke();
    }

    public void CloseCommitted()
    {
        if (!Visible) return;
        _tooltipHost.Dismiss();
        Visible = false;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (IsVisibleInTree() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
    }

    public void ShowValidationError(string message)
    {
        _validation.Text = message ?? string.Empty;
        _validation.Visible = !string.IsNullOrWhiteSpace(_validation.Text);
    }

    public void SetVisualIcon(int slotIndex, ModelKey? icon)
    {
        if (slotIndex < 0 || slotIndex >= _visualIconSlots.Length) return;
        _visualIconSlots[slotIndex].SetSelection(icon, notify: false);
    }

    public ModelKey? GetVisualIcon(int slotIndex) =>
        slotIndex >= 0 && slotIndex < _visualIconSlots.Length
            ? _visualIconSlots[slotIndex].SelectedKey
            : null;

    public void FocusAfterChildPicker(int slotIndex)
    {
        if (!Visible) return;
        if (slotIndex >= 0 && slotIndex < _visualIconSlots.Length)
            _visualIconSlots[slotIndex].GrabFocus();
        else
            _titleInput.GrabFocus();
    }

    private void ApplyRelicIcons(IReadOnlyList<ModelKey>? relicIcons)
    {
        ModelKey[] icons = (relicIcons ?? Array.Empty<ModelKey>())
            .Where(key => key.IsValid)
            .Take(3)
            .ToArray();
        for (int index = 0; index < _visualIconSlots.Length; index++)
            _visualIconSlots[index].SetSelection(index < icons.Length ? icons[index] : null, notify: false);
    }

    private void RefreshSaveEnabled()
    {
        _save.Disabled = string.IsNullOrWhiteSpace(_titleInput.Text);
    }
}
