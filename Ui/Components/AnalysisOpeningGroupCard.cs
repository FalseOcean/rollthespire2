using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Player-facing Opening report group. The selected canonical Neow opening route is
/// the single presentation authority for both Neow predicted detail and the already-
/// produced Combat Reward branch. This component only owns Godot presentation.
/// </summary>
internal sealed partial class AnalysisOpeningGroupCard : PanelContainer
{
    public const float CardPadding = 14f;
    public const float CardRadius = 4f;
    public const int CardBorderWidth = 1;
    public const int OuterGap = 10;
    public const float ChoiceColumnRatio = 0.20f;
    public const float EffectColumnRatio = 0.37f;
    public const float RewardColumnRatio = 0.43f;
    public const float SubcardPadding = 10f;
    public const float SubcardRadius = 3f;
    public const int SubcardBorderWidth = 1;
    public const int SubcardGap = 10;

    private readonly Label _header;
    private readonly Label _choiceTitle;
    private readonly Label _choiceHint;
    private readonly Label _effectTitle;
    private readonly Label _rewardTitle;
    private readonly PanelContainer _choiceSubcard;
    private readonly PanelContainer _effectSubcard;
    private readonly PanelContainer _rewardSubcard;
    private readonly Label _emptyEffectLabel;

    public AnalysisOpeningGroupCard(IGameIconResolver icons, AnchoredTooltipHost tooltipHost)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(tooltipHost);

        Name = "AnalysisOpeningGroupCard";
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, CardRadius, CardBorderWidth, CardPadding);

        var root = new VBoxContainer
        {
            Name = "OpeningCardBody",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", OuterGap);

        _header = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _header.Name = "OpeningCardHeader";
        _header.MouseFilter = MouseFilterEnum.Ignore;
        root.AddChild(_header);

        OpeningWarnings = new WarningCallout { Name = "OpeningWarnings" };
        root.AddChild(OpeningWarnings);

        _choiceSubcard = NewSubcard("NeowChoiceSubcard");
        var choiceColumn = NewSubcardColumn();
        _choiceTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _choiceTitle.MouseFilter = MouseFilterEnum.Ignore;
        _choiceHint = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _choiceHint.MouseFilter = MouseFilterEnum.Ignore;
        _choiceHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ChoiceStrip = new NeowChoiceStrip(icons) { Name = "NeowChoiceStrip" };
        choiceColumn.AddChild(_choiceTitle);
        choiceColumn.AddChild(_choiceHint);
        choiceColumn.AddChild(ChoiceStrip);
        _choiceSubcard.AddChild(choiceColumn);

        _effectSubcard = NewSubcard("NeowEffectSubcard");
        var effectColumn = NewSubcardColumn();
        _effectTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _effectTitle.MouseFilter = MouseFilterEnum.Ignore;
        ChoiceDetail = new NeowChoiceDetailPanel(icons, tooltipHost) { Name = "NeowChoiceDetail" };
        _emptyEffectLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _emptyEffectLabel.Visible = true;
        _emptyEffectLabel.VerticalAlignment = VerticalAlignment.Center;
        _emptyEffectLabel.MouseFilter = MouseFilterEnum.Ignore;
        effectColumn.AddChild(_effectTitle);
        effectColumn.AddChild(ChoiceDetail);
        effectColumn.AddChild(_emptyEffectLabel);
        _effectSubcard.AddChild(effectColumn);

        _rewardSubcard = NewSubcard("CombatRewardSubcard");
        var rewardColumn = NewSubcardColumn();
        _rewardTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _rewardTitle.MouseFilter = MouseFilterEnum.Ignore;
        CombatRewardList = new NormalCombatRewardSequenceSummaryList(icons, tooltipHost)
        {
            Name = "OpeningCombatReward"
        };
        rewardColumn.AddChild(_rewardTitle);
        rewardColumn.AddChild(CombatRewardList);
        _rewardSubcard.AddChild(rewardColumn);

        var columns = new HBoxContainer
        {
            Name = "OpeningColumnStrip",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        columns.AddThemeConstantOverride("separation", OuterGap);
        _choiceSubcard.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _choiceSubcard.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _choiceSubcard.SizeFlagsStretchRatio = ChoiceColumnRatio;
        _effectSubcard.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _effectSubcard.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _effectSubcard.SizeFlagsStretchRatio = EffectColumnRatio;
        _rewardSubcard.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _rewardSubcard.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _rewardSubcard.SizeFlagsStretchRatio = RewardColumnRatio;
        columns.AddChild(_choiceSubcard);
        columns.AddChild(_effectSubcard);
        columns.AddChild(_rewardSubcard);
        root.AddChild(columns);

        AddChild(root);
    }

    public WarningCallout OpeningWarnings { get; }
    public NeowChoiceStrip ChoiceStrip { get; }
    public NeowChoiceDetailPanel ChoiceDetail { get; }
    public NormalCombatRewardSequenceSummaryList CombatRewardList { get; }

    public void ApplyLocalization(IUiTextProvider uiText)
    {
        ArgumentNullException.ThrowIfNull(uiText);
        _header.Text = uiText.Get(Ui1TextKey.AnalysisSectionOpening);
        _choiceTitle.Text = uiText.Get(Ui1TextKey.AnalysisOpeningNeowChoice);
        _choiceHint.Text = uiText.Get(Ui1TextKey.AnalysisOpeningChoiceHint);
        _effectTitle.Text = uiText.Get(Ui1TextKey.AnalysisOpeningNeowEffect);
        _rewardTitle.Text = uiText.Get(Ui1TextKey.AnalysisSubsectionCombatReward);
        ChoiceDetail.ApplyLocalization(uiText);
    }

    public void SyncEffectVisibility(string emptyPrompt, bool showEffectColumn)
    {
        _effectSubcard.Visible = showEffectColumn;
        _emptyEffectLabel.Text = emptyPrompt;
        _emptyEffectLabel.Visible = showEffectColumn && !ChoiceDetail.Visible;
        _choiceSubcard.SizeFlagsStretchRatio = ChoiceColumnRatio;
        _effectSubcard.SizeFlagsStretchRatio = EffectColumnRatio;
        _rewardSubcard.SizeFlagsStretchRatio = showEffectColumn
            ? RewardColumnRatio
            : 1f - ChoiceColumnRatio;
    }

    public void SetSharedColumnTracks(bool wideMiddle)
    {
        float first = wideMiddle ? NormalCombatRewardSequenceSummaryList.CandyOuterBattleNormalWeight : 1f;
        float second = wideMiddle ? NormalCombatRewardSequenceSummaryList.CandyOuterBattleWideWeight : 1f;
        float third = wideMiddle ? NormalCombatRewardSequenceSummaryList.CandyOuterBattleNormalWeight : 1f;
        ChoiceDetail.SetColumnTrackWeights(first, second, third);
        CombatRewardList.SetColumnTrackWeights(first, second, third);
    }

    private static VBoxContainer NewSubcardColumn()
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        return column;
    }

    private static PanelContainer NewSubcard(string name)
    {
        var panel = new PanelContainer
        {
            Name = name,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Input, SubcardRadius, SubcardBorderWidth, SubcardPadding);
        return panel;
    }
}
