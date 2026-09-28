using Godot;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Compact single-column Neow result item. The class name is retained for
/// compatibility, but the former equal-height three-card layout is removed.
/// </summary>
internal sealed partial class NeowChoiceCard : PanelContainer
{
    private readonly IGameIconResolver _icons;
    private readonly HBoxContainer _header;
    private readonly HBoxContainer _badges;
    private readonly Label _slot;
    private readonly IconWithLabel _icon;
    private readonly PrecisionBadge _identity;
    private readonly PrecisionBadge _effect;
    private readonly PredictedEffectSummaryList _effectItems;
    private readonly BonesOutcomeSummaryList _bonesOutcome;
    private readonly PlayerChoiceImpactSummaryList _playerChoiceImpact;
    private readonly Label _warning;

    public NeowChoiceCard(IGameIconResolver icons)
    {
        _icons = icons;
        CustomMinimumSize = new Vector2(0, 64);
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, 10f);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);

        _header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _header.AddThemeConstantOverride("separation", 10);
        _slot = Ui1Theme.Label(string.Empty, Ui1TextRole.Accent);
        _slot.CustomMinimumSize = new Vector2(24, 44);
        _slot.HorizontalAlignment = HorizontalAlignment.Center;
        _slot.VerticalAlignment = VerticalAlignment.Center;
        _icon = new IconWithLabel(44f, Ui1TextRole.CardTitle);

        _badges = new HBoxContainer();
        _badges.AddThemeConstantOverride("separation", 6);
        _identity = new PrecisionBadge();
        _effect = new PrecisionBadge();
        _badges.AddChild(_identity);
        _badges.AddChild(_effect);

        _header.AddChild(_slot);
        _header.AddChild(_icon);
        _header.AddChild(_badges);

        _effectItems = new PredictedEffectSummaryList(icons);
        _bonesOutcome = new BonesOutcomeSummaryList(icons);
        _playerChoiceImpact = new PlayerChoiceImpactSummaryList(icons);

        _warning = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _warning.Visible = false;
        _warning.ClipText = true;

        column.AddChild(_header);
        column.AddChild(_effectItems);
        column.AddChild(_bonesOutcome);
        column.AddChild(_playerChoiceImpact);
        column.AddChild(_warning);
        AddChild(column);
    }

    public void Bind(
        NeowChoiceViewModel model,
        string identityDimension,
        string effectDimension,
        string missingIconTooltip,
        AppDisplayMode displayMode)
    {
        bool showAll = displayMode == AppDisplayMode.Advanced;
        _slot.Text = model.SlotIndex.ToString();
        IconDescriptor icon = _icons.Resolve(
            model.RelicDisplay.ModelKey,
            model.RelicDisplay.ContentKind,
            IconVariant.RelicLarge);
        _icon.Bind(
            icon,
            model.RelicDisplay.DisplayName,
            model.RelicDisplay.Tooltip,
            missingIconTooltip);

        _identity.Visible = showAll || model.IdentityPrecision != Core.Prediction.PredictionPrecision.Exact;
        _identity.Bind(
            identityDimension,
            model.IdentityLabel,
            model.IdentityPrecision,
            compact: !showAll);

        bool notMigrated = model.EffectCapability == Core.Effects.Coverage.NeowEffectImplementationStatus.NotImplemented;
        _effect.Visible = showAll ||
            notMigrated ||
            model.ProductRelevantProjectionPrecision != Core.Prediction.PredictionPrecision.Exact;
        _effect.Bind(
            effectDimension,
            model.ProductProjectionLabel,
            model.ProductRelevantProjectionPrecision,
            notMigrated,
            compact: !showAll);

        _bonesOutcome.Bind(model.BonesOutcome, missingIconTooltip, showAll);
        _playerChoiceImpact.Bind(model.BonesOutcome?.PlayerChoiceImpact, missingIconTooltip);
        _effectItems.Bind(
            model.BonesOutcome is null
                ? model.EffectGroups
                : Array.Empty<PredictedEffectGroupViewModel>(),
            missingIconTooltip,
            showAll);

        _warning.Visible = model.UserWarnings.Count > 0;
        _warning.Text = model.UserWarnings.Count > 0 ? $"△ {model.UserWarnings[0]}" : string.Empty;
        TooltipText = model.Tooltip;
    }

    public void SetCompact(bool compact)
    {
        _header.AddThemeConstantOverride("separation", compact ? 6 : 10);
        _badges.AddThemeConstantOverride("separation", compact ? 4 : 6);
    }
}
