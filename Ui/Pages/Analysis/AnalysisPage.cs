using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Analysis;

/// <summary>
/// Player-facing Seed Report. Prediction semantics remain owned by the existing
/// SeedPredictionDocument; this page only owns Predictor context editing, report grouping,
/// anchor navigation, and presentation.
/// </summary>
internal sealed partial class AnalysisPage : MarginContainer, IAppPage, IResponsiveAppPage
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly IGameIconResolver _icons;
    private readonly Label _pageTitle;
    private readonly WarningCallout _runtimeCompatibilityWarning;
    private readonly AnalysisRequestBar _requestBar;
    private readonly Label _transientStatus;
    private readonly WarningCallout _requestError;
    private readonly AnalysisAnchorBar _anchorBar;
    private readonly ScrollContainer _reportScroll;
    private readonly VBoxContainer _reportArea;
    private readonly WarningCallout _globalWarnings;

    private readonly AnalysisReportSection _openingSection;
    private readonly AnalysisOpeningGroupCard _openingGroupCard;
    private readonly WarningCallout _openingWarnings;
    private readonly NeowChoiceStrip _neowChoiceStrip;
    private readonly NeowChoiceDetailPanel _neowChoiceDetail;
    private readonly NormalCombatRewardSequenceSummaryList _normalCombatRewardList;

    private readonly AnalysisReportSection _ancientSection;
    private readonly AnalysisAncientGroupCard _ancientGroupCard;

    private readonly AnalysisReportSection _relicShopSection;
    private readonly AnalysisRelicShopGroupCard _relicShopGroupCard;

    private readonly AnalysisReportSection _act1Section;
    private readonly AnalysisReportSection _act2Section;
    private readonly AnalysisReportSection _act3Section;
    private readonly AnalysisActPredictionCard[] _actCards;

    private readonly Dictionary<AnalysisReportAnchor, Control> _anchorTargets;
    private IUiTextProvider? _uiText;
    private IGameContentNameResolver? _contentNames;
    private SeedAnalysisViewModel? _viewModel;
    private ModelKey _selectedNeowRelicKey;
    private string _preferredOpeningRouteId = string.Empty;
    private int? _preferredOpeningChoiceSlotIndex;
    private string _preferredRewardRouteGroupId = string.Empty;
    private bool _openingPreselectionProvided;

    public AnalysisPage(
        ModRuntimeSnapshot runtime,
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterPoolIcons,
        IReadOnlyList<ModelKey> characterKeys,
        AnchoredTooltipHost tooltipHost)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _runtime = runtime;
        PageKey = AppPageKey.Analysis;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 8);

        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.PageTitle);
        _runtimeCompatibilityWarning = new WarningCallout();
        string initialSeed = runtime.Profile.ProfileId == RuntimeProfileId.Stable107
            ? "H0T0Z0S23C"
            : "VADSEV23455G";
        _requestBar = new AnalysisRequestBar(initialSeed, runtime.Profile, characterPoolIcons, characterKeys);
        _transientStatus = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _transientStatus.Visible = false;
        _requestError = new WarningCallout();
        _anchorBar = new AnalysisAnchorBar(icons);

        root.AddChild(_pageTitle);
        root.AddChild(_runtimeCompatibilityWarning);
        root.AddChild(_requestBar);
        root.AddChild(_transientStatus);
        root.AddChild(_requestError);
        root.AddChild(_anchorBar);

        _reportScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _reportArea = new VBoxContainer
        {
            Visible = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _reportArea.AddThemeConstantOverride("separation", 28);
        _globalWarnings = new WarningCallout();
        _reportArea.AddChild(_globalWarnings);

        _openingSection = new AnalysisReportSection(Ui1TextKey.AnalysisSectionOpening);
        _openingSection.SetHeaderVisible(false);
        _openingGroupCard = new AnalysisOpeningGroupCard(icons, tooltipHost);
        _openingWarnings = _openingGroupCard.OpeningWarnings;
        _neowChoiceStrip = _openingGroupCard.ChoiceStrip;
        _neowChoiceDetail = _openingGroupCard.ChoiceDetail;
        _normalCombatRewardList = _openingGroupCard.CombatRewardList;
        _openingSection.Content.AddChild(_openingGroupCard);

        _ancientSection = new AnalysisReportSection(Ui1TextKey.AnalysisSectionAncient);
        _ancientSection.SetHeaderVisible(false);
        _ancientGroupCard = new AnalysisAncientGroupCard(icons, tooltipHost);
        _ancientSection.Content.AddChild(_ancientGroupCard);

        _relicShopSection = new AnalysisReportSection(Ui1TextKey.AnalysisSectionRelicShop);
        _relicShopSection.SetHeaderVisible(false);
        _relicShopGroupCard = new AnalysisRelicShopGroupCard(icons, tooltipHost);
        _relicShopSection.Content.AddChild(_relicShopGroupCard);

        _act1Section = new AnalysisReportSection(Ui1TextKey.AnalysisSectionAct1);
        _act2Section = new AnalysisReportSection(Ui1TextKey.AnalysisSectionAct2);
        _act3Section = new AnalysisReportSection(Ui1TextKey.AnalysisSectionAct3);
        AnalysisReportSection[] acts = { _act1Section, _act2Section, _act3Section };
        var eventThumbnails = new EventThumbnailProvider();
        _actCards = new AnalysisActPredictionCard[3];
        for (int index = 0; index < acts.Length; index++)
        {
            acts[index].SetHeaderVisible(false);
            _actCards[index] = new AnalysisActPredictionCard(index + 1, icons, eventThumbnails, tooltipHost, () => _contentNames);
            acts[index].Content.AddChild(_actCards[index]);
        }

        _reportArea.AddChild(_openingSection);
        _reportArea.AddChild(_ancientSection);
        _reportArea.AddChild(_relicShopSection);
        _reportArea.AddChild(_act1Section);
        _reportArea.AddChild(_act2Section);
        _reportArea.AddChild(_act3Section);
        _reportScroll.AddChild(_reportArea);
        root.AddChild(_reportScroll);
        AddChild(root);

        _anchorTargets = new Dictionary<AnalysisReportAnchor, Control>
        {
            [AnalysisReportAnchor.Opening] = _openingSection,
            [AnalysisReportAnchor.Ancient] = _ancientSection,
            [AnalysisReportAnchor.RelicShop] = _relicShopSection,
            [AnalysisReportAnchor.Act1] = _act1Section,
            [AnalysisReportAnchor.Act2] = _act2Section,
            [AnalysisReportAnchor.Act3] = _act3Section
        };

        _requestBar.SubmitRequested += SubmitSeedDraft;
        _requestBar.ContextChanged += SubmitReactiveDraft;
        _requestBar.CopySeedRequested += seed => CopySeedRequested?.Invoke(seed);
        _requestBar.RandomSeedRequested += () => RandomSeedRequested?.Invoke();
        _neowChoiceStrip.SelectionRequested += ApplyOpeningSelection;
        _ancientGroupCard.ConditionsChanged += conditions =>
        {
            AnalysisRequestDraft draft = _requestBar.CurrentReactiveDraft.WithAncientConditions(conditions);
            SubmitReactiveDraft(draft);
        };
        _anchorBar.AnchorRequested += ScrollToAnchor;
        ShowEmpty();
    }

    public event Action<AnalysisRequestDraft>? AnalyzeRequested;
    public event Action<string>? CopySeedRequested;
    public event Action? RandomSeedRequested;
    public event Action<AnalysisRequestDraft, string, int?, string>? PredictorContextChanged;

    public AppPageKey PageKey { get; }
    public Control View => this;
    public SeedPredictionRequest? LastRequest { get; private set; }
    public SeedPredictionDocument? LastDocument { get; private set; }
    public AnalysisRequestDraft CurrentDraft => _requestBar.CurrentDraft.WithAncientConditions(_ancientGroupCard.CurrentConditions);
    public AnalysisRequestDraft CurrentReactiveDraft => _requestBar.CurrentReactiveDraft.WithAncientConditions(_ancientGroupCard.CurrentConditions);
    public string PreferredOpeningRouteId => _preferredOpeningRouteId;
    public int? PreferredOpeningChoiceSlotIndex => _preferredOpeningChoiceSlotIndex;
    public string PreferredRewardRouteGroupId => _preferredRewardRouteGroupId;
    public bool? CurrentUnlockState => _requestBar.AllCharacterCardPoolsUnlocked;

    public string Text(string key) => _uiText?.Get(key) ?? key;

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _uiText = uiText;
        _contentNames = contentNames;
        _pageTitle.Text = uiText.Get(Ui1TextKey.AnalysisTitle);
        RefreshRuntimeCompatibilityWarning();
        _requestBar.ApplyLocalization(uiText, contentNames, uiText.Get(Ui1TextKey.MissingIconTooltip));
        _anchorBar.ApplyLocalization(uiText);
        _openingGroupCard.ApplyLocalization(uiText);
        _ancientGroupCard.ApplyLocalization(uiText, contentNames);
        _relicShopGroupCard.ApplyLocalization(uiText, contentNames);
        _openingSection.ApplyLocalization(uiText);
        _ancientSection.ApplyLocalization(uiText);
        _relicShopSection.ApplyLocalization(uiText);
        _act1Section.ApplyLocalization(uiText);
        _act2Section.ApplyLocalization(uiText);
        _act3Section.ApplyLocalization(uiText);
        foreach (AnalysisActPredictionCard actCard in _actCards) actCard.ApplyLocalization(uiText);

        if (LastDocument is null)
        {
            ShowEmpty();
        }
        else
        {
            ReformatDocumentOnly();
            RuntimeLog.Detail($"ui1LanguageReformatted=true;requestId={LastDocument.Context.RequestId.Serialized};language={contentNames.LanguageCode}");
        }
    }

    private void RefreshRuntimeCompatibilityWarning()
    {
        if (_uiText is null)
        {
            _runtimeCompatibilityWarning.Bind(Array.Empty<string>());
            return;
        }
        _runtimeCompatibilityWarning.Bind(RuntimeAuthorityWarningComposer.Build(_uiText, _runtime));
    }

    public void ApplyDisplayMode(AppDisplayMode mode)
    {
        // Ordinary Predictor presentation intentionally no longer exposes the historical
        // module diagnostic mode. Operational logs keep diagnostic evidence.
        _ = mode;
    }

    public void SetCompact(bool compact)
    {
        _reportArea.AddThemeConstantOverride("separation", compact ? 22 : 28);
    }

    public void SetSeedText(string seed, bool commit = false) => _requestBar.SetSeedText(seed, commit);

    /// <summary>
    /// Restores the last-known Predictor unlock summary for UI continuity only.
    /// Runtime prediction still captures fresh immutable authority before analysis.
    /// </summary>
    public void SetUnlockState(bool? allCharacterCardPoolsUnlocked) =>
        _requestBar.SetUnlockState(allCharacterCardPoolsUnlocked);

    public void SetPredictorContext(
        ModelKey characterKey,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        AncientOptionConditionProfile ancientConditions,
        string preferredOpeningRouteId,
        int? preferredOpeningChoiceSlotIndex,
        string preferredRewardRouteGroupId,
        bool notify)
    {
        _requestBar.SetContext(characterKey, ascension, playersCount, playerSlotIndex, notify: false);
        _ancientGroupCard.SetConditions(ancientConditions, notify: false);
        _preferredOpeningRouteId = preferredOpeningRouteId?.Trim() ?? string.Empty;
        _preferredOpeningChoiceSlotIndex = preferredOpeningChoiceSlotIndex;
        _openingPreselectionProvided = _preferredOpeningChoiceSlotIndex.HasValue || !string.IsNullOrWhiteSpace(_preferredOpeningRouteId);
        // Reward route is a derived compatibility mirror. Persisted legacy reward-group
        // state is never allowed to choose an Opening branch.
        _ = preferredRewardRouteGroupId;
        _preferredRewardRouteGroupId = string.Empty;
        if (notify)
        {
            SubmitReactiveDraft(CurrentReactiveDraft);
        }
    }

    public void ImportSearchContext(
        SeedPredictionRequest request,
        string preferredOpeningRouteId,
        int? preferredOpeningChoiceSlotIndex)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requestBar.SetSeedText(request.OriginalSeed, commit: true);
        _requestBar.SetContext(
            request.Character.CharacterKey,
            request.Ascension,
            request.PlayersCount,
            request.PlayerSlotIndex,
            notify: false);
        _requestBar.SetUnlockState(request.Authority.AllCharacterCardPoolsUnlocked);
        _ancientGroupCard.SetConditions(request.AncientOptionConditions, notify: false);
        _preferredOpeningRouteId = preferredOpeningRouteId?.Trim() ?? string.Empty;
        _preferredOpeningChoiceSlotIndex = preferredOpeningChoiceSlotIndex;
        _openingPreselectionProvided = _preferredOpeningChoiceSlotIndex.HasValue || !string.IsNullOrWhiteSpace(_preferredOpeningRouteId);
        _preferredRewardRouteGroupId = string.Empty;
        _anchorBar.Select(AnalysisReportAnchor.Opening);
        _reportScroll.ScrollVertical = 0;
        PredictorContextChanged?.Invoke(CurrentReactiveDraft, _preferredOpeningRouteId, _preferredOpeningChoiceSlotIndex, _preferredRewardRouteGroupId);
    }

    public void ShowLoading(AnalysisRequestDraft draft)
    {
        _anchorBar.BindPreviews(EmptyAnchorPreviews());
        _requestBar.SetBusy(true);
        _ancientGroupCard.SetBusy(true);
        _requestError.Bind(Array.Empty<string>());
        _transientStatus.Visible = true;
        _transientStatus.Text = $"{Text(Ui1TextKey.Analyzing)} · {draft.RawSeed.Trim()}";
    }

    public void ShowRequestError(SeedPredictionRequestError error)
    {
        _anchorBar.BindPreviews(EmptyAnchorPreviews());
        _requestBar.SetBusy(false);
        _ancientGroupCard.SetBusy(false);
        _transientStatus.Visible = false;
        _requestError.Bind(new[] { Text(UiTextKey.RequestError(error)) });
    }

    public void ShowUnhandledError()
    {
        _anchorBar.BindPreviews(EmptyAnchorPreviews());
        _requestBar.SetBusy(false);
        _ancientGroupCard.SetBusy(false);
        _transientStatus.Visible = false;
        _requestError.Bind(new[] { Text(Ui1TextKey.AnalysisFailed) });
    }

    public void ShowDocument(
        SeedPredictionRequest request,
        SeedPredictionDocument document)
    {
        bool isNewAnalysisContext = LastRequest is null || LastRequest.RequestId != request.RequestId;
        if (isNewAnalysisContext && !_openingPreselectionProvided)
        {
            _selectedNeowRelicKey = default;
            _preferredOpeningRouteId = string.Empty;
            _preferredOpeningChoiceSlotIndex = null;
            _preferredRewardRouteGroupId = string.Empty;
        }
        _openingPreselectionProvided = false;
        LastRequest = request;
        LastDocument = document;
        string committedSeed = string.IsNullOrWhiteSpace(document.CanonicalSeed)
            ? request.OriginalSeed
            : document.CanonicalSeed;
        _requestBar.CommitSeed(committedSeed);
        _requestBar.SetContext(
            request.Character.CharacterKey,
            request.Ascension,
            request.PlayersCount,
            request.PlayerSlotIndex,
            notify: false);
        _requestBar.SetUnlockState(request.Authority.AllCharacterCardPoolsUnlocked);
        _ancientGroupCard.SetConditions(request.AncientOptionConditions, notify: false);
        _requestBar.SetBusy(false);
        _ancientGroupCard.SetBusy(false);
        _transientStatus.Visible = false;
        _requestError.Bind(Array.Empty<string>());
        ReformatDocumentOnly();
        PredictorContextChanged?.Invoke(CurrentReactiveDraft, _preferredOpeningRouteId, _preferredOpeningChoiceSlotIndex, _preferredRewardRouteGroupId);
    }

    public void ShowEmpty()
    {
        _anchorBar.BindPreviews(EmptyAnchorPreviews());
        _reportArea.Visible = false;
        _requestError.Bind(Array.Empty<string>());
        _transientStatus.Visible = true;
        _transientStatus.Text = Text(Ui1TextKey.EmptyMessage);
    }

    private IReadOnlyDictionary<AnalysisReportAnchor, IReadOnlyList<AnalysisAnchorPreviewItem>> BuildAnchorPreviews()
    {
        if (_viewModel is null)
        {
            return EmptyAnchorPreviews();
        }

        Dictionary<AnalysisReportAnchor, IReadOnlyList<AnalysisAnchorPreviewItem>> previews =
            EmptyAnchorPreviews().ToDictionary(
                pair => pair.Key,
                pair => pair.Value);

        previews[AnalysisReportAnchor.Opening] = _viewModel.NeowChoices
            .OrderBy(choice => choice.SlotIndex)
            .Take(3)
            .Select(choice => TryPreview(
                choice.RelicDisplay.ModelKey,
                GameContentKind.Relic,
                choice.DisplayName))
            .Where(item => item is not null)
            .Cast<AnalysisAnchorPreviewItem>()
            .ToArray();

        if (_viewModel.AncientDomain.Status == SeedDomainEvaluationStatus.Evaluated)
        {
            previews[AnalysisReportAnchor.Ancient] = _viewModel.AncientDomain.Items
                .Where(item => item.Act is 2 or 3)
                .OrderBy(item => item.Act)
                .Select(item => TryPreview(
                    item.AncientDisplay.ModelKey,
                    GameContentKind.Ancient,
                    item.AncientDisplay.DisplayName,
                    IconVariant.WorldCompendiumAncientIcon))
                .Where(item => item is not null)
                .Cast<AnalysisAnchorPreviewItem>()
                .ToArray();
        }

        if (_viewModel.RelicSequenceDomain.Status == SeedDomainEvaluationStatus.Evaluated)
        {
            RelicSequenceLaneViewModel? shop = _viewModel.RelicSequenceDomain.Items
                .FirstOrDefault(item => item.Kind == RelicSequenceKind.Shop);
            previews[AnalysisReportAnchor.RelicShop] = shop?.Entries
                .OrderBy(item => item.Position)
                .Take(3)
                .Select(item => TryPreview(
                    item.RelicDisplay.ModelKey,
                    GameContentKind.Relic,
                    item.RelicDisplay.DisplayName))
                .Where(item => item is not null)
                .Cast<AnalysisAnchorPreviewItem>()
                .ToArray() ?? Array.Empty<AnalysisAnchorPreviewItem>();
        }

        if (_viewModel.BossDomain.Status == SeedDomainEvaluationStatus.Evaluated)
        {
            foreach (AnalysisReportAnchor anchor in new[]
                     { AnalysisReportAnchor.Act1, AnalysisReportAnchor.Act2, AnalysisReportAnchor.Act3 })
            {
                int act = anchor switch
                {
                    AnalysisReportAnchor.Act1 => 1,
                    AnalysisReportAnchor.Act2 => 2,
                    _ => 3
                };
                previews[anchor] = _viewModel.BossDomain.Items
                    .Where(item => item.Act == act)
                    .OrderBy(item => item.Ordinal)
                    .Select(item => TryPreview(
                        item.BossDisplay.ModelKey,
                        GameContentKind.Encounter,
                        item.BossDisplay.DisplayName,
                        IconVariant.WorldCompendiumBossIcon))
                    .Where(item => item is not null)
                    .Cast<AnalysisAnchorPreviewItem>()
                    .ToArray();
            }
        }

        return previews;
    }

    private AnalysisAnchorPreviewItem? TryPreview(
        ModelKey key,
        GameContentKind kind,
        string displayName,
        IconVariant variant = IconVariant.Small)
    {
        IconDescriptor descriptor = _icons.Resolve(key, kind, variant);
        return descriptor.Texture is null || descriptor.IsMissing
            ? null
            : new AnalysisAnchorPreviewItem(key, kind, variant, displayName);
    }

    private static bool ShouldShowNeowEffectColumn(NeowChoiceViewModel? choice) =>
        choice is null || NeowPredictedResultPresentationPolicy.HasVisibleResults(choice);

    private static IReadOnlyDictionary<AnalysisReportAnchor, IReadOnlyList<AnalysisAnchorPreviewItem>> EmptyAnchorPreviews() =>
        Enum.GetValues<AnalysisReportAnchor>()
            .ToDictionary(
                anchor => anchor,
                _ => (IReadOnlyList<AnalysisAnchorPreviewItem>)Array.Empty<AnalysisAnchorPreviewItem>());

    private void ReformatDocumentOnly()
    {
        if (LastDocument is null || _uiText is null || _contentNames is null) return;

        _viewModel = SeedAnalysisPresentationBuilder.Build(LastDocument, _uiText, _contentNames, false);
        if (_viewModel.State is Ui1AnalysisState.Unsupported or Ui1AnalysisState.Error or Ui1AnalysisState.Unknown)
        {
            _anchorBar.BindPreviews(EmptyAnchorPreviews());
            _reportArea.Visible = false;
            _requestError.Bind(new[]
            {
                _viewModel.State == Ui1AnalysisState.Unsupported
                    ? _uiText.Get(Ui1TextKey.UnsupportedMessage)
                    : _viewModel.UserWarnings.FirstOrDefault() ?? _viewModel.StatusLabel
            });
            return;
        }

        _reportArea.Visible = true;
        _anchorBar.BindPreviews(BuildAnchorPreviews());
        _globalWarnings.Bind(_viewModel.UserWarnings);
        _openingWarnings.Bind(_viewModel.OpeningWarnings);

        ReconcileOpeningSelection();
        _neowChoiceStrip.Bind(
            _viewModel.NeowChoices,
            _selectedNeowRelicKey,
            _preferredOpeningRouteId,
            _uiText.Get(Ui1TextKey.MissingIconTooltip),
            _uiText.Get(Ui1TextKey.AnalysisNeowPickFirst));
        NeowChoiceViewModel? selectedChoice = _viewModel.NeowChoices.FirstOrDefault(choice =>
            choice.RelicKey == _selectedNeowRelicKey);
        _openingGroupCard.SetSharedColumnTracks(SelectedRewardRouteUsesWideMiddleTrack());
        _neowChoiceDetail.Bind(
            selectedChoice,
            _preferredOpeningRouteId,
            _uiText.Get(Ui1TextKey.MissingIconTooltip));
        _openingGroupCard.SyncEffectVisibility(
            selectedChoice is null
                ? _uiText.Get(Ui1TextKey.AnalysisOpeningSelectRelicPrompt)
                : selectedChoice.EffectCapability == NeowEffectImplementationStatus.NotApplicable ||
                  selectedChoice.PredictionState == NeowPredictionPresentationState.NoAdditionalPredictionNeeded
                    ? _uiText.Get(Ui1TextKey.AnalysisNoEffectsToPredict)
                    : selectedChoice.PredictionState == NeowPredictionPresentationState.Predicted
                        ? string.Empty
                        : _uiText.Get(Ui1TextKey.AnalysisPredictionFailed),
            ShouldShowNeowEffectColumn(selectedChoice));

        string rewardRoutePrompt = selectedChoice is not null &&
                                   NeowChoiceStrip.HasAmbiguousBonesRoutes(selectedChoice) &&
                                   string.IsNullOrWhiteSpace(_preferredOpeningRouteId)
            ? _uiText.Get(Ui1TextKey.AnalysisOpeningBonesRoutePrompt)
            : selectedChoice is null
                ? _uiText.Get(Ui1TextKey.AnalysisOpeningSelectRelicPrompt)
                : _uiText.Get(Ui1TextKey.NormalCombatRewardRoutePrompt);

        _normalCombatRewardList.Bind(
            _viewModel.NormalCombatRewardDomain,
            _uiText.Get(Ui1TextKey.AnalysisPredictionUnavailable),
            _uiText.Get(Ui1TextKey.AnalysisCombatRewardReportNote),
            rewardRoutePrompt,
            _uiText.Get(Ui1TextKey.NormalCombatRewardCurrentRoute),
            _uiText.Get(Ui1TextKey.NormalCombatRewardEquivalentRoutes),
            _uiText.Get(Ui1TextKey.NormalCombatRewardCards),
            _uiText.Get(Ui1TextKey.NormalCombatRewardPotionNone),
            _uiText.Get(Ui1TextKey.MissingIconTooltip),
            advanced: false,
            showRouteSelector: false,
            selectedRouteGroupId: _preferredRewardRouteGroupId,
            battleShortFormat: _uiText.Get(Ui1TextKey.AnalysisCombatRewardBattleShort),
            rewardGroupShortFormat: _uiText.Get(Ui1TextKey.AnalysisCombatRewardGroupShort),
            goldShortFormat: _uiText.Get(Ui1TextKey.AnalysisCombatRewardGoldShort),
            goldExtraShortFormat: _uiText.Get(Ui1TextKey.AnalysisCombatRewardGoldExtraShort),
            potionNoneShortLabel: _uiText.Get(Ui1TextKey.AnalysisCombatRewardPotionNoneShort));
        // Bind may clear a persisted/preferred route that does not exist for this Seed.
        // Keep Predictor Current Context aligned with the branch the player can actually see.
        _preferredRewardRouteGroupId = _normalCombatRewardList.SelectedRouteGroupId;

        _ancientGroupCard.Bind(
            _viewModel.AncientDomain,
            _uiText.Get(Ui1TextKey.AnalysisPredictionUnavailable),
            _uiText.Get(Ui1TextKey.MissingIconTooltip));

        _relicShopGroupCard.Bind(
            _viewModel.RelicSequenceDomain,
            BuildMerchantColorlessProjection(),
            _uiText.Get(Ui1TextKey.AnalysisPredictionUnavailable),
            _uiText.Get(Ui1TextKey.MissingIconTooltip));

        for (int index = 0; index < _actCards.Length; index++)
        {
            _actCards[index].Bind(
                _viewModel.BossDomain,
                _viewModel.EventPoolSequenceDomain,
                _viewModel.ProfileId,
                _viewModel.PlayersCount,
                BuildEventResultProjection(),
                _uiText.Get(Ui1TextKey.AnalysisPredictionUnavailable),
                _uiText.Get(Ui1TextKey.MissingIconTooltip));
        }
    }

    private Beta111ShopColorlessProjection? BuildMerchantColorlessProjection()
    {
        if (LastRequest?.Authority.ProfileId != RuntimeProfileId.Beta111 ||
            !TryGetProjectionRoot(out ulong rootHash))
            return null;
        return Beta111NormalMerchantColorlessSequenceProjector.Project(
            rootHash,
            Beta111MerchantColorlessAuthority.From(LastRequest.Authority));
    }

    private Beta111EventResultProjection? BuildEventResultProjection()
    {
        if (LastRequest?.Authority.ProfileId != RuntimeProfileId.Beta111 ||
            !TryGetProjectionRoot(out ulong rootHash))
            return null;
        return Beta111EventResultProjector.Project(
            rootHash,
            LastRequest.PlayerSlotIndex,
            Beta111EventResultAuthority.From(LastRequest.Authority));
    }

    private bool TryGetProjectionRoot(out ulong rootHash)
    {
        rootHash = 0UL;
        if (LastRequest is null) return false;
        if (LastRequest.TrustedRootHashInput is { } trusted)
        {
            rootHash = trusted.RootHash;
            return true;
        }

        string canonicalSeed = LastDocument is not null && !string.IsNullOrWhiteSpace(LastDocument.CanonicalSeed)
            ? LastDocument.CanonicalSeed
            : LastRequest.OriginalSeed;
        IRuntimeProfile profile = LastRequest.Authority.ProfileId == RuntimeProfileId.Beta111
            ? Beta111Profile.Instance
            : _runtime.Profile;
        if (!profile.TryCanonicalizeSeed(canonicalSeed, out string normalized, out _)) return false;
        rootHash = profile.ComputeRootSeed(normalized);
        return true;
    }


    private bool SelectedRewardRouteUsesWideMiddleTrack()
    {
        if (_viewModel is null ||
            _viewModel.NormalCombatRewardDomain.Status != SeedDomainEvaluationStatus.Evaluated ||
            string.IsNullOrWhiteSpace(_preferredRewardRouteGroupId))
        {
            return false;
        }

        NormalCombatRewardRouteViewModel? route = _viewModel.NormalCombatRewardDomain.Items
            .FirstOrDefault(candidate => string.Equals(
                candidate.RouteGroupId,
                _preferredRewardRouteGroupId,
                StringComparison.Ordinal));
        return route is not null && NormalCombatRewardSequenceSummaryList.UsesWideMiddleTrack(route);
    }

    private void ReconcileOpeningSelection()
    {
        if (_viewModel is null || _viewModel.NeowChoices.Count == 0)
        {
            _selectedNeowRelicKey = default;
            _preferredOpeningRouteId = string.Empty;
            _preferredOpeningChoiceSlotIndex = null;
            _preferredRewardRouteGroupId = string.Empty;
            return;
        }

        OpeningRoutePresentationViewModel? selectedRoute = FindOpeningRoute(
            _preferredOpeningRouteId,
            _preferredOpeningChoiceSlotIndex);

        NeowChoiceViewModel? selectedChoice = _preferredOpeningChoiceSlotIndex.HasValue
            ? _viewModel.NeowChoices.FirstOrDefault(choice => choice.SlotIndex == _preferredOpeningChoiceSlotIndex.Value)
            : _selectedNeowRelicKey.IsValid
                ? _viewModel.NeowChoices.FirstOrDefault(choice => choice.RelicKey == _selectedNeowRelicKey)
                : null;

        // A fresh analysis context always has a concrete opening selection. A route
        // supplied by Search remains authoritative; otherwise use the first top-level
        // choice and, for Bones, its first canonical acquisition route. This keeps the
        // UI out of the RNG path while avoiding a half-selected opening report.
        if (selectedRoute is null && selectedChoice is null)
        {
            selectedChoice = _viewModel.NeowChoices.OrderBy(choice => choice.SlotIndex).FirstOrDefault();
            _selectedNeowRelicKey = selectedChoice?.RelicKey ?? default;
        }

        if (selectedRoute is null && selectedChoice is not null)
        {
            selectedRoute = ResolveUniqueSelectableRoute(selectedChoice);
            if (selectedRoute is null)
            {
                selectedRoute = selectedChoice.OpeningRoutes
                    .OrderBy(route => route.RouteOrder)
                    .ThenBy(route => route.RouteId, StringComparer.Ordinal)
                    .FirstOrDefault();
            }
        }

        if (selectedRoute is not null)
        {
            _preferredOpeningRouteId = selectedRoute.RouteId;
            _selectedNeowRelicKey = selectedRoute.RootRelicKey;
            _preferredOpeningChoiceSlotIndex = _viewModel.NeowChoices
                .FirstOrDefault(choice => choice.RelicKey == selectedRoute.RootRelicKey &&
                                          choice.OpeningRoutes.Any(route => ReferenceEquals(route, selectedRoute)))?.SlotIndex;
            _preferredRewardRouteGroupId = FindRewardRouteGroupId(selectedRoute);
            return;
        }

        // No route is available in this projection. Keep the selected identity for the
        // effect panel, but clear derived continuation state so rewards fail closed.
        _preferredOpeningRouteId = string.Empty;
        _preferredOpeningChoiceSlotIndex = selectedChoice?.SlotIndex;
        _preferredRewardRouteGroupId = string.Empty;
    }

    private void ApplyOpeningSelection(ModelKey rootRelicKey, string openingRouteId)
    {
        if (_viewModel is null)
        {
            return;
        }

        NeowChoiceViewModel? choice = _viewModel.NeowChoices
            .FirstOrDefault(candidate => candidate.RelicKey == rootRelicKey);
        if (choice is null)
        {
            return;
        }

        _selectedNeowRelicKey = rootRelicKey;
        _preferredOpeningChoiceSlotIndex = choice.SlotIndex;
        _openingPreselectionProvided = false;
        OpeningRoutePresentationViewModel? requestedRoute = choice.OpeningRoutes
            .FirstOrDefault(route => string.Equals(
                route.RouteId,
                openingRouteId?.Trim() ?? string.Empty,
                StringComparison.Ordinal));

        if (requestedRoute is null)
        {
            requestedRoute = ResolveUniqueSelectableRoute(choice);
        }

        if (requestedRoute is null)
        {
            // Split/indeterminate Bones primary cannot become a fake route selection.
            _preferredOpeningRouteId = string.Empty;
            _preferredRewardRouteGroupId = string.Empty;
        }
        else
        {
            _preferredOpeningRouteId = requestedRoute.RouteId;
            _preferredRewardRouteGroupId = FindRewardRouteGroupId(requestedRoute);
        }

        PredictorContextChanged?.Invoke(
            CurrentReactiveDraft,
            _preferredOpeningRouteId,
            _preferredOpeningChoiceSlotIndex,
            _preferredRewardRouteGroupId);
        ReformatDocumentOnly();
    }

    private static OpeningRoutePresentationViewModel? ResolveUniqueSelectableRoute(NeowChoiceViewModel choice)
    {
        if (!NeowChoiceStrip.IsBonesPrimarySelectable(choice))
        {
            return null;
        }

        if (choice.BonesOutcome is { OutcomeGroups.Count: 1 } bones)
        {
            string representativeRouteId = bones.OutcomeGroups[0].RepresentativeRouteId;
            return choice.OpeningRoutes.FirstOrDefault(route =>
                       string.Equals(route.RouteId, representativeRouteId, StringComparison.Ordinal))
                   ?? choice.OpeningRoutes.OrderBy(route => route.RouteOrder).FirstOrDefault();
        }

        return choice.OpeningRoutes.OrderBy(route => route.RouteOrder).FirstOrDefault();
    }

    private OpeningRoutePresentationViewModel? FindOpeningRoute(string? routeId, int? choiceSlotIndex)
    {
        if (_viewModel is null || string.IsNullOrWhiteSpace(routeId))
        {
            return null;
        }

        IEnumerable<NeowChoiceViewModel> choices = choiceSlotIndex.HasValue
            ? _viewModel.NeowChoices.Where(choice => choice.SlotIndex == choiceSlotIndex.Value)
            : _viewModel.NeowChoices;
        return choices
            .SelectMany(choice => choice.OpeningRoutes)
            .FirstOrDefault(route => string.Equals(route.RouteId, routeId, StringComparison.Ordinal));
    }

    private string FindRewardRouteGroupId(OpeningRoutePresentationViewModel? openingRoute)
    {
        if (_viewModel is null || openingRoute is null || string.IsNullOrWhiteSpace(openingRoute.RouteId))
        {
            return string.Empty;
        }

        // NormalCombatRewardSequencePredictor prefixes each already-produced opening
        // continuation with `choice.{slot}.` so route IDs remain globally unique across
        // the three Neow choices. The Neow presentation intentionally keeps the original
        // canonical route ID from the choice-local continuation. Match those two immutable
        // identities here instead of requiring string equality between the two projections.
        return _viewModel.NormalCombatRewardDomain.Items
            .FirstOrDefault(group => group.EquivalentRouteBindings.Any(binding =>
                binding.RootRelicKey == openingRoute.RootRelicKey &&
                RewardBindingMatchesOpeningRoute(binding.RouteId, openingRoute.RouteId)))?
            .RouteGroupId ?? string.Empty;
    }

    private static bool RewardBindingMatchesOpeningRoute(string rewardBindingRouteId, string openingRouteId)
    {
        if (string.Equals(rewardBindingRouteId, openingRouteId, StringComparison.Ordinal))
        {
            return true;
        }

        const string prefix = "choice.";
        if (!rewardBindingRouteId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Expected Reward projection shape: choice.{slot}.{original opening route ID}.
        int originalRouteSeparator = rewardBindingRouteId.IndexOf('.', prefix.Length);
        if (originalRouteSeparator < 0 || originalRouteSeparator + 1 >= rewardBindingRouteId.Length)
        {
            return false;
        }

        return string.Equals(
            rewardBindingRouteId[(originalRouteSeparator + 1)..],
            openingRouteId,
            StringComparison.Ordinal);
    }

    private void SubmitSeedDraft(AnalysisRequestDraft requestBarDraft)
    {
        // AnalysisRequestBar emits only after the active runtime profile has accepted
        // a complete legal Seed. Intermediate edit states never reach prediction.
        AnalysisRequestDraft draft = requestBarDraft.WithAncientConditions(_ancientGroupCard.CurrentConditions);
        SubmitDraft(draft);
    }

    private void SubmitReactiveDraft(AnalysisRequestDraft requestBarDraft)
    {
        AnalysisRequestDraft draft = requestBarDraft.WithAncientConditions(_ancientGroupCard.CurrentConditions);
        PredictorContextChanged?.Invoke(draft, _preferredOpeningRouteId, _preferredOpeningChoiceSlotIndex, _preferredRewardRouteGroupId);
        SubmitDraft(draft);
    }

    private void SubmitDraft(AnalysisRequestDraft draft)
    {
        if (!draft.CharacterKey.IsValid)
        {
            ShowRequestError(SeedPredictionRequestError.MissingCharacter);
            return;
        }
        AnalyzeRequested?.Invoke(draft);
    }

    private void ScrollToAnchor(AnalysisReportAnchor anchor)
    {
        if (!_anchorTargets.TryGetValue(anchor, out Control? target)) return;
        Callable.From(() =>
        {
            _reportScroll.ScrollVertical = Math.Max(0, (int)Math.Round(target.Position.Y));
        }).CallDeferred();
    }

}
