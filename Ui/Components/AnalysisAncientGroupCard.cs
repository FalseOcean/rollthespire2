using Godot;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Player-facing Ancient report group. Presentation-only: one outer card, one collapsible
/// conditions subcard, and two side-by-side Act result subcards.
/// </summary>
internal sealed partial class AnalysisAncientGroupCard : PanelContainer
{
    public const float CardPadding = 14f;
    public const float CardRadius = 4f;
    public const int CardBorderWidth = 1;
    public const int OuterGap = 10;
    public const int ActSubcardGap = 12;
    public const float ActSubcardPadding = 10f;
    public const float ActSubcardRadius = 3f;
    public const int ActSubcardBorderWidth = 1;
    public const float ActSubcardMinimumHeight = 138f;
    public const float AncientIdentityIconSize = 56f;
    public const float OptionIconSize = 34f;
    public const float TargetIconSize = 28f;
    public const float ActLabelWidth = 46f;
    public const float IdentityColumnWidth = 148f;
    public const int IdentityGap = 10;
    public const int OptionGap = 8;

    private readonly AnalysisAncientConditionsEditor _conditions;
    private readonly AnalysisAncientActSubcard _act2Card;
    private readonly AnalysisAncientActSubcard _act3Card;
    private readonly Label _header;
    private IUiTextProvider? _uiText;

    public AnalysisAncientGroupCard(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(tooltipHost);

        Name = "AnalysisAncientGroupCard";
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, CardRadius, CardBorderWidth, CardPadding);

        var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", OuterGap);

        _header = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _header.Name = "AncientCardHeader";
        root.AddChild(_header);

        _conditions = new AnalysisAncientConditionsEditor(icons, tooltipHost)
        {
            Name = "AncientConditions"
        };
        root.AddChild(_conditions);

        var resultRow = new HBoxContainer
        {
            Name = "AncientResultColumns",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        resultRow.AddThemeConstantOverride("separation", ActSubcardGap);

        _act2Card = new AnalysisAncientActSubcard(
            icons,
            tooltipHost,
            act: 2,
            AncientIdentityIconSize,
            OptionIconSize,
            TargetIconSize,
            ActLabelWidth,
            IdentityColumnWidth,
            IdentityGap,
            OptionGap,
            ActSubcardMinimumHeight)
        {
            Name = "Act2Subcard",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1f
        };
        Ui1Theme.ApplyPanel(_act2Card, Ui1SurfaceRole.Input, ActSubcardRadius, ActSubcardBorderWidth, ActSubcardPadding);

        _act3Card = new AnalysisAncientActSubcard(
            icons,
            tooltipHost,
            act: 3,
            AncientIdentityIconSize,
            OptionIconSize,
            TargetIconSize,
            ActLabelWidth,
            IdentityColumnWidth,
            IdentityGap,
            OptionGap,
            ActSubcardMinimumHeight)
        {
            Name = "Act3Subcard",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1f
        };
        Ui1Theme.ApplyPanel(_act3Card, Ui1SurfaceRole.Input, ActSubcardRadius, ActSubcardBorderWidth, ActSubcardPadding);

        resultRow.AddChild(_act2Card);
        resultRow.AddChild(_act3Card);
        root.AddChild(resultRow);
        AddChild(root);

        _conditions.ConditionsChanged += conditions => ConditionsChanged?.Invoke(conditions);
    }

    public event Action<AncientOptionConditionProfile>? ConditionsChanged;

    public AncientOptionConditionProfile CurrentConditions => _conditions.CurrentConditions;

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _uiText = uiText ?? throw new ArgumentNullException(nameof(uiText));
        _header.Text = uiText.Get(Ui1TextKey.AnalysisSectionAncient);
        _conditions.ApplyLocalization(uiText, contentNames);
        _act2Card.SetActLabel(uiText.Get(Ui1TextKey.AnalysisSectionAct2));
        _act3Card.SetActLabel(uiText.Get(Ui1TextKey.AnalysisSectionAct3));
    }

    public void SetConditions(AncientOptionConditionProfile conditions, bool notify) =>
        _conditions.SetConditions(conditions, notify);

    public void SetBusy(bool busy) => _conditions.SetBusy(busy);

    public void Bind(
        SeedDomainViewModel<AncientPredictionViewModel> domain,
        string unavailableFormat,
        string missingIconText)
    {
        if (_uiText is null)
        {
            return;
        }

        int optionSlotCount = Math.Max(
            3,
            domain.Items
                .Where(item => item.Act is 2 or 3)
                .Select(item => item.Options.Count)
                .DefaultIfEmpty(0)
                .Max());
        _act2Card.Bind(domain, unavailableFormat, missingIconText, optionSlotCount);
        _act3Card.Bind(domain, unavailableFormat, missingIconText, optionSlotCount);
    }
}

internal sealed partial class AnalysisAncientActSubcard : PanelContainer
{
    private const float OptionMinimumItemWidth = 96f;
    private const int LayerGap = 7;

    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly int _act;
    private readonly float _identityIconSize;
    private readonly float _optionIconSize;
    private readonly float _minimumHeight;
    private readonly int _optionGap;
    private readonly Label _actLabel;
    private readonly MarginContainer _identityHost;
    private readonly HBoxContainer _optionsHost;
    private readonly List<Control> _optionCells = new();
    private readonly List<Label?> _optionNames = new();
    private readonly List<string> _optionTexts = new();
    private readonly List<float> _optionDesiredWidths = new();
    private readonly Label _optionMeasureLabel;
    private bool _reflowingOptions;

    public AnalysisAncientActSubcard(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost,
        int act,
        float identityIconSize,
        float optionIconSize,
        float targetIconSize,
        float actLabelWidth,
        float identityColumnWidth,
        int identityGap,
        int optionGap,
        float minimumHeight)
    {
        _icons = icons;
        _tooltipHost = tooltipHost;
        _act = act;
        _identityIconSize = identityIconSize;
        _optionIconSize = optionIconSize;
        _minimumHeight = minimumHeight;
        _optionGap = optionGap;

        CustomMinimumSize = new Vector2(0f, minimumHeight);
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

        var root = new VBoxContainer
        {
            Name = "AncientTwoLayerResult",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        root.AddThemeConstantOverride("separation", LayerGap);

        _actLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _actLabel.Name = "ActLabel";
        _actLabel.CustomMinimumSize = new Vector2(actLabelWidth, 20f);
        _actLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _actLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

        _identityHost = new MarginContainer
        {
            Name = "AncientIdentity",
            CustomMinimumSize = new Vector2(identityColumnWidth, identityIconSize),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        _optionsHost = new HBoxContainer
        {
            Name = "OptionSlots",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(0f, optionIconSize),
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        _optionsHost.AddThemeConstantOverride("separation", optionGap);
        _optionsHost.Resized += ReflowOptionWidths;

        _optionMeasureLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, wrap: false);
        _optionMeasureLabel.Visible = false;
        _optionMeasureLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

        root.AddChild(_actLabel);
        root.AddChild(_identityHost);
        root.AddChild(_optionsHost);
        root.AddChild(_optionMeasureLabel);
        AddChild(root);
    }

    public void SetActLabel(string text) => _actLabel.Text = text;

    public void Bind(
        SeedDomainViewModel<AncientPredictionViewModel> domain,
        string unavailableFormat,
        string missingIconText,
        int optionSlotCount)
    {
        ClearHost(_identityHost);
        ClearHost(_optionsHost);
        _optionCells.Clear();
        _optionNames.Clear();
        _optionTexts.Clear();
        _optionDesiredWidths.Clear();

        if (domain.Status != SeedDomainEvaluationStatus.Evaluated)
        {
            AddUnavailable(string.Format(unavailableFormat, domain.Status, domain.IssueCode), optionSlotCount);
            return;
        }

        AncientPredictionViewModel? ancient = domain.Items
            .Where(item => item.Act == _act)
            .OrderBy(item => item.Act)
            .FirstOrDefault();
        if (ancient is null)
        {
            AddUnavailable(string.Format(
                unavailableFormat,
                SeedDomainEvaluationStatus.Unknown,
                "ActProjectionMissing"), optionSlotCount);
            return;
        }

        _identityHost.AddChild(BuildIdentity(ancient, missingIconText));

        AncientOptionPredictionViewModel[] options = ancient.Options
            .OrderBy(item => item.Ordinal)
            .ToArray();
        int slots = Math.Max(optionSlotCount, options.Length);
        for (int index = 0; index < slots; index++)
        {
            Control cell;
            Label? optionName = null;
            string optionText = string.Empty;
            float desiredWidth = 1f;
            if (index < options.Length)
            {
                AncientOptionPredictionViewModel option = options[index];
                optionText = OptionCompositeText(option);
                cell = BuildOption(option, optionText, missingIconText, out optionName);
                desiredWidth = CalculateOptionDesiredWidth(optionText);
            }
            else
            {
                cell = new Control
                {
                    CustomMinimumSize = new Vector2(1f, _optionIconSize)
                };
            }

            cell.Name = $"Option{index + 1}";
            cell.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            _optionCells.Add(cell);
            _optionNames.Add(optionName);
            _optionTexts.Add(optionText);
            _optionDesiredWidths.Add(desiredWidth);
            _optionsHost.AddChild(cell);
        }
        ReflowOptionWidths();
    }

    private Control BuildIdentity(AncientPredictionViewModel ancient, string missingIconText)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        row.AddThemeConstantOverride("separation", 8);

        PanelContainer icon = BuildIcon(
            _icons.Resolve(
                ancient.AncientDisplay.ModelKey,
                GameContentKind.Ancient,
                IconVariant.WorldCompendiumAncientIcon),
            _identityIconSize,
            missingIconText,
            interactive: false);
        row.AddChild(icon);

        Label name = Ui1Theme.Label(ancient.AncientDisplay.DisplayName, Ui1TextRole.CardTitle, wrap: false);
        name.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        name.CustomMinimumSize = new Vector2(120f, _identityIconSize);
        name.ClipText = false;
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(name);
        return row;
    }

    private Control BuildOption(
        AncientOptionPredictionViewModel option,
        string compositeText,
        string missingIconText,
        out Label optionName)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        row.AddThemeConstantOverride("separation", 5);

        PanelContainer optionIcon = BuildIcon(
            _icons.Resolve(option.OptionDisplay.ModelKey, GameContentKind.Relic, IconVariant.Small),
            _optionIconSize,
            missingIconText,
            interactive: true);
        optionIcon.Name = "OptionIcon";
        optionIcon.MouseEntered += () =>
            _tooltipHost.ShowFor(
                optionIcon,
                option.OptionDisplay.ModelKey,
                GameContentKind.Relic,
                option.OptionDisplay.DisplayName);
        optionIcon.MouseExited += () => _tooltipHost.Dismiss(optionIcon);
        optionIcon.TreeExiting += () => _tooltipHost.Dismiss(optionIcon);
        row.AddChild(optionIcon);

        Ui1TextRole textRole = option.CharacterTargetPrecision == PredictionPrecision.Unknown &&
                               option.CharacterTargetDisplay is null &&
                               !string.IsNullOrWhiteSpace(option.CharacterTargetLabel)
            ? Ui1TextRole.Warning
            : Ui1TextRole.Body;
        Label name = Ui1Theme.Label(compositeText, textRole, wrap: false);
        name.Name = "OptionName";
        name.CustomMinimumSize = new Vector2(0f, _optionIconSize);
        name.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.MouseFilter = Control.MouseFilterEnum.Ignore;

        row.AddChild(name);
        optionName = name;
        return row;
    }

    private static string OptionCompositeText(AncientOptionPredictionViewModel option)
    {
        string target = option.CharacterTargetDisplay?.DisplayName
            ?? option.CharacterTargetLabel
            ?? string.Empty;
        return string.IsNullOrWhiteSpace(target)
            ? option.OptionDisplay.DisplayName
            : $"{option.OptionDisplay.DisplayName} · {target}";
    }

    private float CalculateOptionDesiredWidth(string text)
    {
        _optionMeasureLabel.Text = text;
        float measured = MathF.Ceiling(_optionMeasureLabel.GetMinimumSize().X);
        if (measured <= 1f)
        {
            measured = EstimateTextWidth(text);
        }
        return Math.Max(1f, _optionIconSize + 5f + measured + 4f);
    }

    private void ReflowOptionWidths()
    {
        if (_reflowingOptions || _optionCells.Count == 0)
        {
            return;
        }

        float hostWidth = _optionsHost.Size.X;
        if (hostWidth <= 1f)
        {
            return;
        }

        _reflowingOptions = true;
        try
        {
            int count = _optionCells.Count;
            float usableWidth = Math.Max(1f, hostWidth - _optionGap * Math.Max(0, count - 1));
            float[] minimumWidths = Enumerable.Repeat(OptionMinimumItemWidth, count).ToArray();
            float[] widths = AdaptiveInlineWidthAllocator.Allocate(
                usableWidth,
                _optionDesiredWidths,
                minimumWidths);

            for (int index = 0; index < count; index++)
            {
                float width = widths[index];
                _optionCells[index].CustomMinimumSize = new Vector2(width, _optionIconSize);

                Label? name = _optionNames[index];
                if (name is null || string.IsNullOrWhiteSpace(_optionTexts[index]))
                {
                    continue;
                }

                bool clipped = width + 0.5f < _optionDesiredWidths[index];
                float availableTextWidth = Math.Max(1f, width - _optionIconSize - 5f - 4f);
                float naturalTextWidth = Math.Max(1f, _optionDesiredWidths[index] - _optionIconSize - 5f - 4f);
                name.RemoveThemeFontSizeOverride("font_size");
                name.CustomMinimumSize = new Vector2(clipped ? availableTextWidth : naturalTextWidth, _optionIconSize);
                name.ClipText = clipped;
                name.TextOverrunBehavior = clipped
                    ? TextServer.OverrunBehavior.TrimEllipsis
                    : TextServer.OverrunBehavior.NoTrimming;
            }
        }
        finally
        {
            _reflowingOptions = false;
        }
    }

    private static float EstimateTextWidth(string text)
    {
        float width = 0f;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                width += 4f;
            }
            else if (c <= 0x7f)
            {
                width += char.IsUpper(c) ? 8.5f : 7.5f;
            }
            else
            {
                width += 14.5f;
            }
        }
        return width;
    }

    private static PanelContainer BuildIcon(
        IconDescriptor descriptor,
        float outerSize,
        string missingIconText,
        bool interactive)
    {
        var surface = new PanelContainer
        {
            CustomMinimumSize = new Vector2(outerSize, outerSize),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = interactive ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore
        };
        Ui1Theme.ApplyPanel(surface, Ui1SurfaceRole.Input, 3f, 1);

        var texture = new TextureRect
        {
            Texture = descriptor.Texture,
            Visible = !descriptor.IsMissing && descriptor.Texture is not null,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        texture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        texture.OffsetLeft = 4f;
        texture.OffsetTop = 4f;
        texture.OffsetRight = -4f;
        texture.OffsetBottom = -4f;

        Label missing = Ui1Theme.Label(texture.Visible ? string.Empty : "?", Ui1TextRole.Muted);
        missing.HorizontalAlignment = HorizontalAlignment.Center;
        missing.VerticalAlignment = VerticalAlignment.Center;
        missing.MouseFilter = Control.MouseFilterEnum.Ignore;
        missing.TooltipText = texture.Visible ? string.Empty : missingIconText;
        missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        surface.AddChild(texture);
        surface.AddChild(missing);
        return surface;
    }

    private void AddUnavailable(string text, int optionSlotCount)
    {
        var unavailable = Ui1Theme.Label(text, Ui1TextRole.Warning, true);
        unavailable.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        unavailable.VerticalAlignment = VerticalAlignment.Center;
        _identityHost.AddChild(unavailable);
        for (int index = 0; index < Math.Max(3, optionSlotCount); index++)
        {
            var spacer = new Control
            {
                CustomMinimumSize = new Vector2(OptionMinimumItemWidth, _optionIconSize),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 1f
            };
            _optionsHost.AddChild(spacer);
        }
    }

    private static void ClearHost(Node host)
    {
        foreach (Node child in host.GetChildren())
        {
            host.RemoveChild(child);
            child.QueueFree();
        }
    }
}
