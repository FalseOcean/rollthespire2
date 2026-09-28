using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Player-facing Act presentation only: one outer Act card containing a compact Boss
/// summary and a horizontal strip of up to ten run-start Event candidate identities.
/// It consumes immutable Predictor view models and never reconstructs Event RNG,
/// eligibility, Search state, or Event-local outcomes.
/// </summary>
internal sealed partial class AnalysisActPredictionCard : PanelContainer
{
    internal const float OuterPadding = 14f;
    internal const float OuterRadius = 4f;
    internal const int OuterBorderWidth = 1;
    internal const float SubcardPadding = 10f;
    internal const float SubcardRadius = 3f;
    internal const int SubcardBorderWidth = 1;
    internal const int SubcardGap = 10;
    internal const float BossSubcardMinimumHeight = 72f;
    internal const float BossIconSize = 44f;
    internal const int BossIdentityExtraLeftInset = 56;
    internal const float EventArtworkSize = 112f;
    internal const float EventTileWidth = 112f;
    internal const float EventTitleRegionHeight = 44f;
    internal const float EventFooterHeight = 22f;
    internal const int EventTileGap = 8;
    internal const int EventPreviewCount = 10;

    private readonly int _act;
    private readonly IGameIconResolver _icons;
    private readonly EventThumbnailProvider _thumbnails;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly Func<IGameContentNameResolver?> _getNames;
    private readonly Label _header;
    private readonly Label _bossTitle;
    private readonly HBoxContainer _bossContent;
    private readonly Label _bossStatus;
    private readonly Label _eventTitle;
    private readonly HBoxContainer _eventStrip;
    private readonly Label _eventStatus;
    private IUiTextProvider? _uiText;
    private string _firstBossLabel = "First Boss";
    private string _secondBossLabel = "Second Boss";
    private RuntimeProfileId _profileId;
    private int _playersCount = 1;
    private Beta111EventResultProjection? _eventResults;

    public AnalysisActPredictionCard(
        int act,
        IGameIconResolver icons,
        EventThumbnailProvider thumbnails,
        AnchoredTooltipHost tooltipHost,
        Func<IGameContentNameResolver?> getNames)
    {
        _act = act;
        _icons = icons;
        _thumbnails = thumbnails;
        _tooltipHost = tooltipHost;
        _getNames = getNames;
        Name = $"Act{act}PredictionCard";
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, OuterRadius, OuterBorderWidth, OuterPadding);

        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", SubcardGap);

        _header = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _header.Name = "ActCardHeader";
        _header.MouseFilter = MouseFilterEnum.Ignore;
        body.AddChild(_header);

        PanelContainer bossSubcard = NewSubcard("BossSubcard");
        var bossColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        bossColumn.AddThemeConstantOverride("separation", 6);
        _bossTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _bossTitle.MouseFilter = MouseFilterEnum.Ignore;
        _bossContent = new HBoxContainer
        {
            Name = "BossIdentityStrip",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, BossSubcardMinimumHeight - SubcardPadding * 2f),
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        _bossContent.AddThemeConstantOverride("separation", 14);
        _bossStatus = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _bossStatus.Visible = false;
        bossColumn.AddChild(_bossTitle);
        var bossIdentityInset = new MarginContainer
        {
            Name = "BossIdentityInset",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        bossIdentityInset.AddThemeConstantOverride("margin_left", BossIdentityExtraLeftInset);
        bossIdentityInset.AddChild(_bossContent);
        bossColumn.AddChild(bossIdentityInset);
        bossColumn.AddChild(_bossStatus);
        bossSubcard.AddChild(bossColumn);
        body.AddChild(bossSubcard);

        PanelContainer eventSubcard = NewSubcard("EventSubcard");
        var eventColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        eventColumn.AddThemeConstantOverride("separation", 8);
        _eventTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _eventTitle.MouseFilter = MouseFilterEnum.Ignore;
        _eventStrip = new HBoxContainer
        {
            Name = "EventCandidateStrip",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
            ClipContents = true
        };
        _eventStrip.AddThemeConstantOverride("separation", EventTileGap);
        _eventStatus = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _eventStatus.Visible = false;
        eventColumn.AddChild(_eventTitle);
        eventColumn.AddChild(_eventStrip);
        eventColumn.AddChild(_eventStatus);
        eventSubcard.AddChild(eventColumn);
        body.AddChild(eventSubcard);

        AddChild(body);
    }

    public void ApplyLocalization(IUiTextProvider uiText)
    {
        _uiText = uiText;
        _header.Text = uiText.Get(_act switch
        {
            1 => Ui1TextKey.AnalysisSectionAct1,
            2 => Ui1TextKey.AnalysisSectionAct2,
            3 => Ui1TextKey.AnalysisSectionAct3,
            _ => Ui1TextKey.AnalysisSectionAct1
        });
        _bossTitle.Text = uiText.Get(Ui1TextKey.AnalysisSubsectionBoss);
        _firstBossLabel = uiText.Get(Ui1TextKey.AnalysisBossFirst);
        _secondBossLabel = uiText.Get(Ui1TextKey.AnalysisBossSecond);
        _eventTitle.Text = uiText.Get(Ui1TextKey.AnalysisSubsectionEvents);
    }

    public void Bind(
        SeedDomainViewModel<BossPredictionViewModel> bossDomain,
        SeedDomainViewModel<EventPoolActSequenceViewModel> eventDomain,
        RuntimeProfileId profileId,
        int playersCount,
        Beta111EventResultProjection? eventResults,
        string unavailableFormat,
        string missingIconText)
    {
        _profileId = profileId;
        _playersCount = Math.Max(1, playersCount);
        _eventResults = eventResults;
        BindBosses(bossDomain, unavailableFormat, missingIconText);
        BindEvents(eventDomain, unavailableFormat);
    }

    private void BindBosses(
        SeedDomainViewModel<BossPredictionViewModel> domain,
        string unavailableFormat,
        string missingIconText)
    {
        Clear(_bossContent);
        if (domain.Status != SeedDomainEvaluationStatus.Evaluated)
        {
            _bossStatus.Visible = true;
            _bossStatus.Text = string.Format(unavailableFormat, domain.Status, domain.IssueCode);
            return;
        }

        BossPredictionViewModel[] bosses = domain.Items
            .Where(item => item.Act == _act)
            .OrderBy(item => item.Ordinal)
            .ToArray();
        if (bosses.Length == 0)
        {
            _bossStatus.Visible = true;
            _bossStatus.Text = string.Format(unavailableFormat, SeedDomainEvaluationStatus.Unknown, "ActProjectionMissing");
            return;
        }

        _bossStatus.Visible = false;
        foreach (BossPredictionViewModel boss in bosses)
        {
            var group = new VBoxContainer
            {
                Name = $"BossOrder{boss.Ordinal}",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
            };
            group.AddThemeConstantOverride("separation", 3);

            Label order = Ui1Theme.Label(BossOrderLabel(boss.Ordinal), Ui1TextRole.Muted, wrap: false);
            order.Name = "BossOrderLabel";
            order.MouseFilter = MouseFilterEnum.Ignore;
            group.AddChild(order);

            var identity = new IconWithLabel(BossIconSize, Ui1TextRole.CardTitle)
            {
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
            };
            identity.Bind(
                _icons.Resolve(boss.BossDisplay.ModelKey, GameContentKind.Encounter, IconVariant.WorldCompendiumBossIcon),
                boss.BossDisplay.DisplayName,
                boss.BossDisplay.Tooltip,
                missingIconText);
            group.AddChild(identity);
            _bossContent.AddChild(group);
        }
    }

    private string BossOrderLabel(int ordinal) => ordinal switch
    {
        1 => _firstBossLabel,
        2 => _secondBossLabel,
        _ => _bossTitle.Text
    };

    private void BindEvents(
        SeedDomainViewModel<EventPoolActSequenceViewModel> domain,
        string unavailableFormat)
    {
        Clear(_eventStrip);
        if (domain.Status != SeedDomainEvaluationStatus.Evaluated)
        {
            _eventStatus.Visible = true;
            _eventStatus.Text = string.Format(unavailableFormat, domain.Status, domain.IssueCode);
            return;
        }

        EventPoolActSequenceViewModel? act = domain.Items.FirstOrDefault(item => item.Act == _act);
        if (act is null)
        {
            _eventStatus.Visible = true;
            _eventStatus.Text = string.Format(unavailableFormat, SeedDomainEvaluationStatus.Unknown, "ActProjectionMissing");
            return;
        }

        _eventStatus.Visible = false;
        EventPoolSequenceEntryViewModel[] entries = act.Entries
            .OrderBy(item => item.Ordinal)
            .Take(EventPreviewCount)
            .ToArray();
        if (entries.Length == 0)
        {
            Label empty = Ui1Theme.Label("—", Ui1TextRole.Muted);
            empty.MouseFilter = MouseFilterEnum.Ignore;
            _eventStrip.AddChild(empty);
            return;
        }

        foreach (EventPoolSequenceEntryViewModel entry in entries)
        {
            _eventStrip.AddChild(BuildEventTile(entry));
        }
    }

    private Control BuildEventTile(EventPoolSequenceEntryViewModel entry)
    {
        var tile = new VBoxContainer
        {
            Name = $"EventTile{entry.Ordinal}",
            CustomMinimumSize = new Vector2(EventTileWidth, EventArtworkSize + EventTitleRegionHeight + EventFooterHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            MouseFilter = MouseFilterEnum.Ignore
        };
        tile.AddThemeConstantOverride("separation", 4);

        Label identity = Ui1Theme.Label(
            $"{OrdinalLabel(entry.Ordinal)} {entry.EventDisplay.DisplayName}",
            Ui1TextRole.Meta,
            wrap: true);
        identity.Name = "EventIdentityLabel";
        identity.CustomMinimumSize = new Vector2(EventTileWidth, EventTitleRegionHeight);
        identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        identity.HorizontalAlignment = HorizontalAlignment.Center;
        identity.VerticalAlignment = VerticalAlignment.Center;
        identity.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        identity.MaxLinesVisible = 2;
        identity.ClipText = true;
        identity.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        identity.MouseFilter = MouseFilterEnum.Ignore;
        tile.AddChild(identity);

        var artworkHost = new CenterContainer
        {
            Name = "EventArtworkTooltipHost",
            CustomMinimumSize = new Vector2(EventArtworkSize, EventArtworkSize),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            MouseFilter = MouseFilterEnum.Stop
        };
        EventThumbnailDescriptor descriptor = _thumbnails.Resolve(entry.EventDisplay.ModelKey);
        artworkHost.AddChild(new EventThumbnailView(
            descriptor,
            new Vector2(EventArtworkSize, EventArtworkSize),
            EventThumbnailPresentation.PickerSquareCrop));
        artworkHost.MouseEntered += () => ShowEventTooltip(artworkHost, entry);
        artworkHost.MouseExited += () => _tooltipHost.Dismiss(artworkHost);
        artworkHost.TreeExiting += () => _tooltipHost.Dismiss(artworkHost);
        tile.AddChild(artworkHost);

        var footer = new HBoxContainer
        {
            Name = "EventTileFooter",
            CustomMinimumSize = new Vector2(EventTileWidth, EventFooterHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };

        if (Beta111EventPresentationKnowledge.ShouldShowRuntimeConditionMarker(
                _profileId,
                entry.EventDisplay.ModelKey,
                _playersCount))
        {
            Label conditional = Ui1Theme.Label(
                _uiText?.Get(Ui1TextKey.SearchEventConditionalBadge) ?? string.Empty,
                Ui1TextRole.Meta);
            conditional.Name = "ConditionalMarker";
            conditional.MouseFilter = MouseFilterEnum.Ignore;
            conditional.HorizontalAlignment = HorizontalAlignment.Center;
            conditional.AddThemeColorOverride("font_color", Ui1Theme.Palette.AccentFocus);
            footer.AddChild(conditional);
        }

        tile.AddChild(footer);
        return tile;
    }

    private void ShowEventTooltip(Control artworkHost, EventPoolSequenceEntryViewModel entry)
    {
        string title = entry.EventDisplay.DisplayName;
        string conditionKey = Beta111EventPresentationKnowledge.RuntimeConditionLocalizationKey(
            _profileId,
            entry.EventDisplay.ModelKey,
            _playersCount);
        Control? effects = BuildEventResultContent(entry.EventDisplay.ModelKey);
        if (_uiText is null || string.IsNullOrWhiteSpace(conditionKey))
        {
            if (effects is null) _tooltipHost.ShowText(artworkHost, title);
            else _tooltipHost.ShowStructuredContent(artworkHost, title, _uiText?.Get(Ui1TextKey.SearchEventResultTitle) ?? string.Empty, string.Empty, effects);
            return;
        }

        string condition = _uiText.Get(conditionKey);
        if (string.IsNullOrWhiteSpace(condition))
        {
            if (effects is null) _tooltipHost.ShowText(artworkHost, title);
            else _tooltipHost.ShowStructuredContent(artworkHost, title, _uiText.Get(Ui1TextKey.SearchEventResultTitle), string.Empty, effects);
            return;
        }

        // Full all-Event option-title authority is deliberately absent from the current
        // Beta111 evidence. Omit that section rather than fabricating or labeling TODO data.
        if (effects is null)
            _tooltipHost.ShowStructuredText(artworkHost, title, _uiText.Get(Ui1TextKey.SearchEventTooltipConditionsTitle), condition);
        else
            _tooltipHost.ShowStructuredContent(artworkHost, title, _uiText.Get(Ui1TextKey.SearchEventTooltipConditionsTitle), condition, effects);
    }

    private Control? BuildEventResultContent(ModelKey eventKey)
    {
        if (_eventResults is null || _uiText is null) return null;
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddChild(Ui1Theme.Label(_uiText.Get(Ui1TextKey.SearchEventResultTitle), Ui1TextRole.Meta));
        bool added = false;
        if (eventKey.Entry == Beta111EventResultCatalog.TrashHeapEventEntry && _eventResults.TrashHeapPrecision == PredictionPrecision.Exact)
        {
            AddResult(content, _eventResults.TrashHeapGrabCard, GameContentKind.Card); AddResult(content, _eventResults.TrashHeapDiveRelic, GameContentKind.Relic); added = true;
        }
        else if (eventKey.Entry == Beta111EventResultCatalog.FakeMerchantEventEntry && _eventResults.FakeMerchantPrecision == PredictionPrecision.Exact)
        {
            foreach (ModelKey key in _eventResults.FakeMerchantInventory) AddResult(content, key, GameContentKind.Relic); added = _eventResults.FakeMerchantInventory.Count > 0;
        }
        else if (eventKey.Entry == Beta111EventResultCatalog.ColorfulPhilosophersEventEntry && _eventResults.ColorfulPrecision == PredictionPrecision.Exact)
        {
            foreach (ModelKey key in _eventResults.ColorfulOfferedColors) AddResult(content, key, GameContentKind.Character, IconVariant.CharacterPortrait); added = _eventResults.ColorfulOfferedColors.Count > 0;
        }
        if (!added) { content.QueueFree(); return null; }
        return content;
    }

    private void AddResult(VBoxContainer parent, ModelKey key, GameContentKind kind, IconVariant variant = IconVariant.Small)
    {
        var item = new IconWithLabel(28, Ui1TextRole.Meta);
        item.Bind(_icons.Resolve(key, kind, variant), _contentName(key, kind), string.Empty, _uiText?.Get(Ui1TextKey.MissingIconTooltip) ?? string.Empty);
        parent.AddChild(item);
    }

    private string _contentName(ModelKey key, GameContentKind kind) => _getNames()?.Resolve(key, kind) ?? key.Entry;

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

    private static string OrdinalLabel(int ordinal) => ordinal switch
    {
        1 => "①",
        2 => "②",
        3 => "③",
        4 => "④",
        5 => "⑤",
        6 => "⑥",
        7 => "⑦",
        8 => "⑧",
        9 => "⑨",
        10 => "⑩",
        _ => ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
