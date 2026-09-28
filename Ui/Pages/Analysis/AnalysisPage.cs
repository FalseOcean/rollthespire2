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
/// Act navigation, and presentation.
/// </summary>
internal sealed partial class AnalysisPage : MarginContainer, IAppPage, IResponsiveAppPage
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly AnalysisRequestBar _requestBar;
    private readonly Control _openingDonors = new() { Visible = false };
    private readonly NeowChoiceStrip _neowChoiceStrip;
    private readonly NeowChoiceDetailPanel _neowChoiceDetail;
    // These donors still own Ancient premise editing and native event tooltips.
    private readonly AnalysisAncientGroupCard _ancientGroupCard;
    private readonly AnalysisActPredictionCard[] _actCards;
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
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        _runtime = runtime;
        _partyCharacters = characterKeys.ToArray();
        PageKey = AppPageKey.Analysis;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        string initialSeed = runtime.Profile.ProfileId == RuntimeProfileId.Stable107
            ? "H0T0Z0S23C" : "VADSEV23455G";
        _requestBar = new AnalysisRequestBar(initialSeed, runtime.Profile, characterPoolIcons, characterKeys);
        AddChild(_requestBar);
        AddChild(_openingDonors);
        _neowChoiceStrip = new NeowChoiceStrip(icons);
        _neowChoiceDetail = new NeowChoiceDetailPanel(icons, tooltipHost);
        _openingDonors.AddChild(_neowChoiceStrip);
        _openingDonors.AddChild(_neowChoiceDetail);
        _ancientGroupCard = new AnalysisAncientGroupCard(icons, tooltipHost);
        _openingDonors.AddChild(_ancientGroupCard);
        var eventThumbnails = new EventThumbnailProvider();
        _actCards = Enumerable.Range(1, 3).Select(act =>
            new AnalysisActPredictionCard(act, icons, eventThumbnails, tooltipHost, () => _contentNames)).ToArray();
        foreach (var card in _actCards) _openingDonors.AddChild(card);

        _requestBar.SubmitRequested += SubmitSeedDraft;
        _requestBar.ContextChanged += SubmitReactiveDraft;
        _requestBar.CopySeedRequested += seed => CopySeedRequested?.Invoke(seed);
        _requestBar.RandomSeedRequested += () => RandomSeedRequested?.Invoke();
        _neowChoiceStrip.SelectionRequested += ApplyOpeningSelection;
        _ancientGroupCard.ConditionsChanged += ApplyAncientPremises;
        BuildWorkbench(characterKeys);
        _requestBar.InvalidSeedEdited += () => ClearWorkbenchResults("predictor.invalid_seed");
        _requestBar.CommittedSeedRestored += () =>
        {
            if (LastDocument?.CanonicalSeed == _requestBar.CommittedSeed) ReformatDocumentOnly();
        };
        _mapWorkspace = new MapWorkspace();
        _mapWorkspace.Closed += () => { CancelMapLoad(); _workbenchRoot!.Show(); };
        _mapWorkspace.ActRequested += LoadMapAct;
        AddChild(_mapWorkspace);
        VisibilityChanged += () => { if (!IsVisibleInTree()) { ResetMaps(); ClosePartyConfiguration(); } };
        TreeExiting += ResetMaps;
        ShowEmpty();
    }

    public event Action<AnalysisRequestDraft>? AnalyzeRequested;
    public event Action<string>? CopySeedRequested;
    public event Action? RandomSeedRequested;
    public event Action<AnalysisRequestDraft, string, int?, string>? PredictorContextChanged;
    internal event Action<int>? OpeningExplicitlySelected;
    internal event Action<int>? PartyPlayerSelected
    {
        add => _requestBar.PartyPlayerSelected += value;
        remove => _requestBar.PartyPlayerSelected -= value;
    }
    internal event Action<bool>? PartyModeSelected
    {
        add => _requestBar.PartyModeSelected += value;
        remove => _requestBar.PartyModeSelected -= value;
    }
    internal void ConfigureParty(int count) => _requestBar.ConfigureParty(count);
    internal event Action? PartyConfigurationRequested
    {
        add => _requestBar.PartyConfigurationRequested += value;
        remove => _requestBar.PartyConfigurationRequested -= value;
    }

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
        _requestBar.ApplyLocalization(uiText, contentNames, uiText.Get(Ui1TextKey.MissingIconTooltip));
        _neowChoiceDetail.ApplyLocalization(uiText);
        _ancientGroupCard.ApplyLocalization(uiText, contentNames);
        foreach (var card in _actCards) card.ApplyLocalization(uiText);
        _mapWorkspace.ApplyLocalization(uiText);
        RefreshWorkbenchLabels();

        if (_workbenchRoot is not null && !_wbHasResult)
        {
            ClearWorkbenchResults(_wbStatusKey);
            return;
        }

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

    public void ApplyDisplayMode(AppDisplayMode mode)
    {
        // Ordinary Predictor presentation intentionally no longer exposes the historical
        // module diagnostic mode. Operational logs keep diagnostic evidence.
        _ = mode;
    }

    public void SetCompact(bool compact) { }

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
        PredictorContextChanged?.Invoke(CurrentReactiveDraft, _preferredOpeningRouteId, _preferredOpeningChoiceSlotIndex, _preferredRewardRouteGroupId);
    }

    public void ShowLoading(AnalysisRequestDraft draft)
    {
        ClearWorkbenchResults(Ui1TextKey.Analyzing);
        ResetMaps();
        _requestBar.SetBusy(true);
        _ancientGroupCard.SetBusy(true);
    }

    public void ShowRequestError(SeedPredictionRequestError error)
    {
        ClearWorkbenchResults(UiTextKey.RequestError(error));
        _requestBar.SetBusy(false);
        _ancientGroupCard.SetBusy(false);
    }

    public void ShowUnhandledError()
    {
        ClearWorkbenchResults(Ui1TextKey.AnalysisFailed);
        _requestBar.SetBusy(false);
        _ancientGroupCard.SetBusy(false);
    }

    public void ShowDocument(
        SeedPredictionRequest request,
        SeedPredictionDocument document)
    {
        if (!ReferenceEquals(LastRequest, request) || !ReferenceEquals(LastDocument, document))
            ClearWorkbenchProjections();
        bool isNewAnalysisContext = LastRequest is null || LastRequest.RequestId != request.RequestId;
        if (isNewAnalysisContext) ResetMaps();
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
        PrecomputeAncientPremises(request, document);
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
        ReformatDocumentOnly();
        PredictorContextChanged?.Invoke(CurrentReactiveDraft, _preferredOpeningRouteId, _preferredOpeningChoiceSlotIndex, _preferredRewardRouteGroupId);
    }

    public void ShowEmpty()
    {
        ClearWorkbenchResults("predictor.empty");
    }

    private void ReformatDocumentOnly()
    {
        if (LastDocument is null || _uiText is null || _contentNames is null) return;

        _viewModel = SeedAnalysisPresentationBuilder.Build(LastDocument, _uiText, _contentNames, false);
        _wbHasResult = true;
        RenderWorkbench();
    }

    private Beta111ShopColorlessProjection? BuildMerchantColorlessProjection()
    {
        if (_merchantProjectionReady) return _merchantProjection;
        if (LastRequest?.Authority.ProfileId != RuntimeProfileId.Beta111 ||
            !TryGetProjectionRoot(out ulong rootHash))
            return null;
        _merchantProjection = Beta111NormalMerchantColorlessSequenceProjector.Project(
            rootHash,
            Beta111MerchantColorlessAuthority.From(LastRequest.Authority));
        _merchantProjectionReady = true;
        return _merchantProjection;
    }

    private Beta111EventResultProjection? BuildEventResultProjection()
    {
        if (_eventProjectionReady) return _eventProjection;
        if (LastRequest?.Authority.ProfileId != RuntimeProfileId.Beta111 ||
            !TryGetProjectionRoot(out ulong rootHash))
            return null;
        _eventProjection = Beta111EventResultProjector.Project(
            rootHash,
            LastRequest.PlayerSlotIndex,
            Beta111EventResultAuthority.From(LastRequest.Authority));
        _eventProjectionReady = true;
        return _eventProjection;
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
            // Information prediction always has a concrete displayed default in a party.
            if (CurrentReactiveDraft.PlayersCount > 1)
                requestedRoute ??= choice.OpeningRoutes.OrderBy(route => route.RouteOrder)
                    .ThenBy(route => route.RouteId, StringComparer.Ordinal).FirstOrDefault();
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

        OpeningExplicitlySelected?.Invoke(CurrentReactiveDraft.PlayerSlotIndex);
        _requestBar.SetCurrentPartyOpeningChoice(_contentNames!.Resolve(rootRelicKey, GameContentKind.Relic));
        PredictorContextChanged?.Invoke(
            CurrentReactiveDraft,
            _preferredOpeningRouteId,
            _preferredOpeningChoiceSlotIndex,
            _preferredRewardRouteGroupId);
        // Selection reads another route from the same immutable document.
        RenderWorkbench();
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

}
