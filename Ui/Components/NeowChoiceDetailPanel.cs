using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Flat detail content for the currently selected Neow world line. Ordinary
/// presentation shows only seed/route-dependent observables. A compact source
/// header identifies where results came from; result type/group structure carries
/// the player-readable hierarchy without restoring report/debug narration.
/// </summary>
internal sealed partial class NeowChoiceDetailPanel : VBoxContainer
{
    public const float DirectSourceRelicSize = 32f;
    public const float PrimaryEffectObjectSize = 32f;
    public const float BonesRelicSize = 32f;
    public const float BonesEffectObjectSize = 32f;
    public const float CurseObjectSize = 40f; // compatibility geometry token; concrete curse now uses mini-card.
    public const float BonesResultSubcardPadding = 8f;
    public const float BonesResultSubcardRadius = 3f;
    public const int BonesResultSubcardBorderWidth = 1;
    public const int BonesResultSubcardGap = 10;
    public const int BonesPickupHeaderGap = 10;
    public const float BonesPickupStatusMinimumWidth = 96f;
    public const string BonesCurseCategoryTexturePath = "res://images/images/events/crystal_sphere/crystal_sphere_curse.png";
    public const float BonesCurseCategoryIconSize = 32f;

    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly VBoxContainer _content;
    private string _finalCurseLabel = "Final Curse";
    private string _cardsLabel = "Cards";
    private string _potionsLabel = "Potions";
    private string _relicsLabel = "Relics";
    private string _resultsLabel = "Results";
    private string _groupShortFormat = "Group {0}";
    private string _pickFirstLabel = "Pick first";
    private string _pickSecondLabel = "Pick second";
    private string _predictionUnavailable = "Prediction unavailable";
    private string _predictionFailed = "Prediction failed.";
    private float _firstTrackWeight = 1f;
    private float _secondTrackWeight = 1f;
    private float _thirdTrackWeight = 1f;

    public NeowChoiceDetailPanel(IGameIconResolver icons, AnchoredTooltipHost tooltipHost)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 8);
        AddChild(_content);
        Visible = false;
    }

    public void ApplyLocalization(IUiTextProvider uiText)
    {
        ArgumentNullException.ThrowIfNull(uiText);
        _finalCurseLabel = uiText.Get(Ui1TextKey.AnalysisNeowFinalCurse);
        _cardsLabel = uiText.Get(Ui1TextKey.AnalysisNeowCards);
        _potionsLabel = uiText.Get(Ui1TextKey.AnalysisNeowPotions);
        _relicsLabel = uiText.Get(Ui1TextKey.AnalysisNeowRelics);
        _resultsLabel = uiText.Get(Ui1TextKey.AnalysisNeowResults);
        _groupShortFormat = uiText.Get(Ui1TextKey.AnalysisNeowGroupShort);
        _pickFirstLabel = uiText.Get(Ui1TextKey.AnalysisNeowPickFirst);
        _pickSecondLabel = uiText.Get(Ui1TextKey.AnalysisNeowPickSecond);
        _predictionUnavailable = uiText.Get(Ui1TextKey.AnalysisPredictionUnavailable);
        _predictionFailed = uiText.Get(Ui1TextKey.AnalysisPredictionFailed);
    }

    public void SetColumnTrackWeights(float first, float second, float third)
    {
        _firstTrackWeight = NormalizeTrackWeight(first);
        _secondTrackWeight = NormalizeTrackWeight(second);
        _thirdTrackWeight = NormalizeTrackWeight(third);
    }

    public void Bind(
        NeowChoiceViewModel? choice,
        string selectedOpeningRouteId,
        string missingIconTooltip)
    {
        Clear();
        if (choice is null)
        {
            Visible = false;
            return;
        }

        if (choice.BonesOutcome is { } bones)
        {
            BonesOutcomeGroupViewModel? group = bones.OutcomeGroups.FirstOrDefault(candidate =>
                candidate.EquivalentRouteIds.Contains(selectedOpeningRouteId, StringComparer.Ordinal));
            // Ambiguous Bones without a selected child route must not silently render
            // the first branch. A proven-equivalent one-group Bones can use that sole
            // canonical group even when restored context did not yet carry its route ID.
            if (group is null && bones.OutcomeGroups.Count == 1)
            {
                group = bones.OutcomeGroups[0];
            }
            if (group is not null)
            {
                _content.AddChild(BuildBones(choice, group, missingIconTooltip));
                if (bones.PlayerChoiceImpact is not null)
                {
                    var fallback = new PlayerChoiceImpactSummaryList(_icons);
                    fallback.Bind(bones.PlayerChoiceImpact, missingIconTooltip);
                    _content.AddChild(fallback);
                }
                Visible = true;
                return;
            }

            _content.AddChild(Ui1Theme.Label(_predictionFailed, Ui1TextRole.Warning, true));
            Visible = true;
            return;
        }

        IReadOnlyList<PredictedEffectGroupViewModel> predictedGroups =
            NeowPredictedResultPresentationPolicy.Filter(choice.RelicKey, choice.EffectGroups);
        if (predictedGroups.Count == 0)
        {
            if (choice.PredictionState == NeowPredictionPresentationState.PredictionUnavailable)
            {
                _content.AddChild(Ui1Theme.Label(_predictionFailed, Ui1TextRole.Warning, true));
                Visible = true;
                return;
            }
            Visible = false;
            return;
        }

        _content.AddChild(BuildDirect(choice, predictedGroups, missingIconTooltip));
        Visible = true;
    }

    private Control BuildDirect(
        NeowChoiceViewModel choice,
        IReadOnlyList<PredictedEffectGroupViewModel> predictedGroups,
        string missingIconTooltip)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        column.AddThemeConstantOverride("separation", 7);
        column.AddChild(BuildSourceHeader(choice.RelicDisplay, DirectSourceRelicSize, missingIconTooltip));

        NeowCompactEffectList effects = NewEffectList(PrimaryEffectObjectSize, Ui1TextRole.Body);
        effects.Bind(
            predictedGroups,
            missingIconTooltip,
            showGroupLabels: ShouldShowOfferGroupLabels(predictedGroups));
        column.AddChild(effects);
        return column;
    }

    private Control BuildBones(
        NeowChoiceViewModel choice,
        BonesOutcomeGroupViewModel group,
        string missingIconTooltip)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        column.AddThemeConstantOverride("separation", BonesResultSubcardGap);
        column.AddChild(BuildSourceHeader(choice.RelicDisplay, BonesRelicSize, missingIconTooltip));

        foreach (BonesRelicScopedResultViewModel relic in group.RelicResults)
        {
            column.AddChild(WrapBonesResult(BuildRelicColumn(relic, missingIconTooltip)));
        }

        Control? trailingFact = BuildTrailingFact(group, missingIconTooltip);
        if (trailingFact is not null)
        {
            column.AddChild(WrapBonesResult(trailingFact));
        }

        if (group.RouteWarningLabels.Count > 0)
        {
            var wrapper = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            wrapper.AddThemeConstantOverride("separation", 8);
            wrapper.AddChild(column);
            wrapper.AddChild(Ui1Theme.Label("△ " + group.RouteWarningLabels[0], Ui1TextRole.Warning, true));
            return wrapper;
        }
        return column;
    }

    private Control? BuildTrailingFact(BonesOutcomeGroupViewModel group, string missingIconTooltip)
    {
        if (group.FinalCurseDisplay is { } curse)
        {
            var curseColumn = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
            };
            curseColumn.AddThemeConstantOverride("separation", 6);
            curseColumn.AddChild(Ui1Theme.Label(_finalCurseLabel, Ui1TextRole.Muted, wrap: false));
            curseColumn.AddChild(BuildSourceHeader(curse, BonesEffectObjectSize, missingIconTooltip));
            return curseColumn;
        }

        IReadOnlyList<PredictedEffectGroupViewModel> continuation =
            NeowPredictedResultPresentationPolicy.Filter(default, group.SharedContinuationGroups);
        if (continuation.Count > 0)
        {
            NeowCompactEffectList effects = NewEffectList(BonesEffectObjectSize, Ui1TextRole.Body);
            effects.Bind(
                continuation,
                missingIconTooltip,
                showGroupLabels: ShouldShowOfferGroupLabels(continuation));
            return effects;
        }

        return null;
    }

    private Control BuildCurseCategoryHeader()
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        row.AddThemeConstantOverride("separation", 6);

        if (ResourceLoader.Exists(BonesCurseCategoryTexturePath))
        {
            Texture2D? texture = ResourceLoader.Load<Texture2D>(
                BonesCurseCategoryTexturePath,
                null,
                ResourceLoader.CacheMode.Reuse);
            if (texture is not null)
            {
                var icon = new TextureRect
                {
                    Texture = texture,
                    CustomMinimumSize = new Vector2(BonesCurseCategoryIconSize, BonesCurseCategoryIconSize),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                row.AddChild(icon);
            }
        }

        Label title = Ui1Theme.Label(_finalCurseLabel, Ui1TextRole.Muted, wrap: false);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        title.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(title);
        return row;
    }

    private static PanelContainer WrapBonesResult(Control content)
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyPanel(
            panel,
            Ui1SurfaceRole.Input,
            BonesResultSubcardRadius,
            BonesResultSubcardBorderWidth,
            BonesResultSubcardPadding);
        panel.AddChild(content);
        return panel;
    }

    private Control BuildRelicColumn(
        BonesRelicScopedResultViewModel relic,
        string missingIconTooltip)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);

        column.AddChild(BuildSourceHeader(relic.RelicDisplay, BonesRelicSize, missingIconTooltip));

        IReadOnlyList<PredictedEffectGroupViewModel> predictedGroups =
            NeowPredictedResultPresentationPolicy.Filter(relic.SourceRelicKey, relic.EffectGroups);
        if (predictedGroups.Count > 0)
        {
            // The result list begins at the same content origin as the source relic icon.
            // Pickup order is a right-aligned header state, not a left-side indentation token.
            NeowCompactEffectList effects = NewEffectList(BonesEffectObjectSize, Ui1TextRole.Body);
            effects.Bind(
                predictedGroups,
                missingIconTooltip,
                showGroupLabels: ShouldShowOfferGroupLabels(predictedGroups));
            column.AddChild(effects);
        }
        return column;
    }

    private Control BuildSourceHeader(
        GameContentDisplayViewModel source,
        float iconSize,
        string missingIconTooltip)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        row.AddThemeConstantOverride("separation", 7);

        IconDescriptor descriptor = _icons.Resolve(source.ModelKey, source.ContentKind, IconVariant.Small);
        var iconHost = new Control
        {
            CustomMinimumSize = new Vector2(iconSize, iconSize),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        var texture = new TextureRect
        {
            Texture = descriptor.Texture,
            Visible = !descriptor.IsMissing && descriptor.Texture is not null,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        texture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        texture.OffsetLeft = 3f;
        texture.OffsetTop = 3f;
        texture.OffsetRight = -3f;
        texture.OffsetBottom = -3f;
        Label missing = Ui1Theme.Label(texture.Visible ? string.Empty : "?", Ui1TextRole.Muted, wrap: false);
        missing.HorizontalAlignment = HorizontalAlignment.Center;
        missing.VerticalAlignment = VerticalAlignment.Center;
        missing.MouseFilter = Control.MouseFilterEnum.Ignore;
        missing.TooltipText = texture.Visible ? string.Empty : missingIconTooltip;
        missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        iconHost.AddChild(texture);
        iconHost.AddChild(missing);
        iconHost.MouseEntered += () =>
            _tooltipHost.ShowFor(iconHost, source.ModelKey, source.ContentKind, source.DisplayName);
        iconHost.MouseExited += () => _tooltipHost.Dismiss(iconHost);
        iconHost.TreeExiting += () => _tooltipHost.Dismiss(iconHost);

        Label name = Ui1Theme.Label(source.DisplayName, Ui1TextRole.Body, wrap: false);
        name.CustomMinimumSize = new Vector2(120f, iconSize);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(iconHost);
        row.AddChild(name);
        return row;
    }

    private float TrackWeight(int zeroBasedIndex) => zeroBasedIndex switch
    {
        0 => _firstTrackWeight,
        1 => _secondTrackWeight,
        2 => _thirdTrackWeight,
        _ => 1f
    };

    private static float NormalizeTrackWeight(float value) =>
        float.IsFinite(value) && value > 0f ? value : 1f;

    private NeowCompactEffectList NewEffectList(float iconSize, Ui1TextRole role) => new(
        _icons,
        _tooltipHost,
        iconSize,
        role,
        _cardsLabel,
        _potionsLabel,
        _relicsLabel,
        _resultsLabel,
        _groupShortFormat)
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
    };

    private static bool ShouldShowOfferGroupLabels(IReadOnlyList<PredictedEffectGroupViewModel> groups) =>
        groups.Count > 1 && groups.All(group =>
            group.GroupId.StartsWith("kaleidoscope-group-", StringComparison.Ordinal));

    private void Clear()
    {
        foreach (Node child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
    }
}
