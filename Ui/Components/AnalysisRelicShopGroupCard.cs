using Godot;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Player-facing Shop & Relics report group. Shop is a small finite result set and
/// always shows identity names. Icons/Names mode belongs only to the long relic-bag
/// sequences; both modes consume the same immutable RelicSequence projection.
/// </summary>
internal sealed partial class AnalysisRelicShopGroupCard : PanelContainer
{
    public const float CardPadding = 14f;
    public const float CardRadius = 4f;
    public const int CardBorderWidth = 1;
    public const int OuterGap = 10;
    public const int SubcardGap = 8;
    public const float SubcardPadding = 8f;
    public const float SubcardRadius = 3f;
    public const int SubcardBorderWidth = 1;
    public const float ShopRowMinimumHeight = 56f;
    public const float RelicRowMinimumHeight = 56f;
    public const float RelicIconHostSize = 44f;
    public const float RelicTextureInset = 2f;
    public const int IconGap = 6;
    public const int RowLabelWidth = 72;
    public const int RowContentGap = 12;
    public const int HeadTailLabelGap = 8;
    public const int CenterBreakGap = 18;
    public const float CenterBreakWidth = 18f;
    public const int ShopRowLabelGap = 20;
    public const int ShopIconsGap = 10;
    public const int ShopNamesGap = 14;
    public const int ShopItemContentGap = 6;
    public const float ShopMinimumNameWidth = 42f;
    public const int ShopResultCount = 5;
    public const int BagEndPreviewCount = 10;
    public const float NamesRelicIconSize = 40f;
    public const int NamesItemGap = 8;
    public const int NamesIconNameGap = 6;
    public const float NamesBagEndWidth = 66f;
    public const float NamesDividerWidth = 70f;
    public const float NamesFallbackFontSize = 12f;

    private enum RelicDisplayMode
    {
        Icons,
        Names
    }

    private readonly IGameIconResolver _icons;
    private IGameContentNameResolver? _names;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly Label _header;
    private readonly Label _relicHeader;
    private readonly Button _iconsModeButton;
    private readonly Button _namesModeButton;
    private readonly MerchantShopGridSubcard _shopCard;
    private readonly RelicBagSubcard[] _relicCards;
    private RelicDisplayMode _displayMode = RelicDisplayMode.Icons;

    public AnalysisRelicShopGroupCard(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        Name = "AnalysisRelicShopGroupCard";
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, CardRadius, CardBorderWidth, CardPadding);

        var root = new VBoxContainer
        {
            Name = "RelicShopCardBody",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", OuterGap);

        _header = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _header.Name = "RelicShopCardHeader";
        _header.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_header);

        var subcards = new VBoxContainer
        {
            Name = "RelicShopSubcards",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        subcards.AddThemeConstantOverride("separation", SubcardGap);

        _shopCard = new MerchantShopGridSubcard(_icons, _tooltipHost, () => _names);
        Ui1Theme.ApplyPanel(_shopCard, Ui1SurfaceRole.Input, SubcardRadius, SubcardBorderWidth, SubcardPadding);
        subcards.AddChild(_shopCard);

        var relicsSubcard = new PanelContainer
        {
            Name = "RelicsSubcard",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyPanel(relicsSubcard, Ui1SurfaceRole.Input, SubcardRadius, SubcardBorderWidth, SubcardPadding);
        var relicsRoot = new VBoxContainer
        {
            Name = "RelicsSubcardBody",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        relicsRoot.AddThemeConstantOverride("separation", 6);

        var relicHeaderRow = new HBoxContainer
        {
            Name = "RelicsSubcardHeaderRow",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        relicHeaderRow.AddThemeConstantOverride("separation", 8);
        _relicHeader = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, wrap: false);
        _relicHeader.Name = "RelicsSubcardHeader";
        _relicHeader.MouseFilter = Control.MouseFilterEnum.Ignore;
        relicHeaderRow.AddChild(_relicHeader);
        relicHeaderRow.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });

        _iconsModeButton = NewModeButton("IconsMode");
        _namesModeButton = NewModeButton("NamesMode");
        _iconsModeButton.Pressed += () => SetDisplayMode(RelicDisplayMode.Icons);
        _namesModeButton.Pressed += () => SetDisplayMode(RelicDisplayMode.Names);
        var modeRow = new HBoxContainer
        {
            Name = "RelicDisplayModeControl",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        modeRow.AddThemeConstantOverride("separation", 4);
        modeRow.AddChild(_iconsModeButton);
        modeRow.AddChild(_namesModeButton);
        relicHeaderRow.AddChild(modeRow);
        relicsRoot.AddChild(relicHeaderRow);

        _relicCards = new[]
        {
            NewRelicBagSubcard(RelicSequenceKind.Common),
            NewRelicBagSubcard(RelicSequenceKind.Uncommon),
            NewRelicBagSubcard(RelicSequenceKind.Rare)
        };
        foreach (RelicBagSubcard card in _relicCards)
        {
            relicsRoot.AddChild(card);
        }
        relicsSubcard.AddChild(relicsRoot);
        subcards.AddChild(relicsSubcard);

        root.AddChild(subcards);
        AddChild(root);
        RefreshModePresentation();
    }

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(uiText);
        _names = names;
        _header.Text = uiText.Get(Ui1TextKey.AnalysisSectionRelicShop);
        _iconsModeButton.Text = uiText.Get(Ui1TextKey.AnalysisRelicDisplayIcons);
        _namesModeButton.Text = uiText.Get(Ui1TextKey.AnalysisRelicDisplayNames);
        _shopCard.ApplyLocalization(
            uiText.Get(Ui1TextKey.AnalysisRelicShop),
            uiText.Get(Ui1TextKey.AnalysisRelicShopHint),
            uiText.Get(Ui1TextKey.AnalysisRelicShopRow),
            uiText.Get(Ui1TextKey.AnalysisRelicShopItemRelicLabel),
            uiText.Get(Ui1TextKey.AnalysisRelicShopItemCardLabel));
        _relicHeader.Text = uiText.Get(Ui1TextKey.AnalysisRelicShopRelics);

        string bagTooltipTitle = uiText.Get(Ui1TextKey.AnalysisRelicBagTooltipTitle);
        string bagTooltip = uiText.Get(Ui1TextKey.AnalysisRelicBagTooltip);
        foreach (RelicBagSubcard card in _relicCards)
        {
            card.ApplyLocalization(
                uiText.Get(RowLabelKey(card.Kind)),
                uiText.Get(Ui1TextKey.AnalysisRelicBagHead),
                uiText.Get(Ui1TextKey.AnalysisRelicBagTail),
                bagTooltipTitle,
                bagTooltip);
        }
    }

    public void Bind(
        SeedDomainViewModel<RelicSequenceLaneViewModel> domain,
        Beta111ShopColorlessProjection? colorless,
        string unavailableFormat,
        string missingIconText)
    {
        string unavailable = domain.Status == SeedDomainEvaluationStatus.Evaluated
            ? string.Empty
            : string.Format(unavailableFormat, domain.Status, domain.IssueCode);

        RelicSequenceLaneViewModel? shop = domain.Status == SeedDomainEvaluationStatus.Evaluated
            ? domain.Items.FirstOrDefault(lane => lane.Kind == RelicSequenceKind.Shop)
            : null;
        _shopCard.Bind(shop, colorless, unavailable, missingIconText);

        foreach (RelicBagSubcard card in _relicCards)
        {
            RelicSequenceLaneViewModel? lane = domain.Status == SeedDomainEvaluationStatus.Evaluated
                ? domain.Items.FirstOrDefault(item => item.Kind == card.Kind)
                : null;
            card.Bind(lane, unavailable, missingIconText);
        }
    }

    private void SetDisplayMode(RelicDisplayMode mode)
    {
        if (_displayMode == mode)
        {
            RefreshModePresentation();
            return;
        }

        _displayMode = mode;
        RefreshModePresentation();
    }

    private void RefreshModePresentation()
    {
        bool names = _displayMode == RelicDisplayMode.Names;
        ApplyModeButtonStyle(_iconsModeButton, !names);
        ApplyModeButtonStyle(_namesModeButton, names);
        foreach (RelicBagSubcard card in _relicCards)
        {
            card.SetDisplayMode(names);
        }
    }

    private RelicBagSubcard NewRelicBagSubcard(RelicSequenceKind kind)
    {
        var card = new RelicBagSubcard(
            _icons,
            _tooltipHost,
            kind,
            RowLabelWidth,
            RelicIconHostSize,
            RelicTextureInset,
            IconGap,
            RowContentGap,
            HeadTailLabelGap,
            CenterBreakGap,
            CenterBreakWidth,
            RelicRowMinimumHeight);
        return card;
    }

    private static Button NewModeButton(string name)
    {
        var button = new Button
        {
            Name = name,
            CustomMinimumSize = new Vector2(68f, 30f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All
        };
        Ui1Theme.ApplyButton(button, Ui1ButtonRole.Ghost);
        button.AddThemeFontSizeOverride("font_size", 13);
        return button;
    }

    private static void ApplyModeButtonStyle(Button button, bool selected)
    {
        Ui1Theme.ApplyButton(button, selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
        button.AddThemeFontSizeOverride("font_size", 13);
    }

    private static string RowLabelKey(RelicSequenceKind kind) => kind switch
    {
        RelicSequenceKind.Common => Ui1TextKey.AnalysisRelicCommon,
        RelicSequenceKind.Uncommon => Ui1TextKey.AnalysisRelicUncommon,
        RelicSequenceKind.Rare => Ui1TextKey.AnalysisRelicRare,
        _ => Ui1TextKey.AnalysisRelicCommon
    };

    private sealed partial class MerchantShopGridSubcard : PanelContainer
    {
        private const int MerchantShopCardGap = 8;
        private const float MerchantShopCardHeight = 240f;
        private const float MerchantRelicIconSize = 48f;
        private const float MerchantColorlessIconSize = 44f;
        private readonly IGameIconResolver _icons;
        private readonly AnchoredTooltipHost _tooltipHost;
        private readonly Func<IGameContentNameResolver?> _getNames;
        private readonly Label _title;
        private readonly Label _hint;
        private readonly HBoxContainer _row;
        private readonly Label[] _shopNumbers = new Label[ShopResultCount];
        private readonly Label[] _relicLabels = new Label[ShopResultCount];
        private readonly Label[] _cardLabels = new Label[ShopResultCount];
        private readonly VBoxContainer[] _relicCells = new VBoxContainer[ShopResultCount];
        private readonly VBoxContainer[] _uncommonCells = new VBoxContainer[ShopResultCount];
        private readonly VBoxContainer[] _rareCells = new VBoxContainer[ShopResultCount];
        private RelicSequenceLaneViewModel? _shop;
        private Beta111ShopColorlessProjection? _colorless;
        private string _unavailable = string.Empty;
        private string _missing = string.Empty;
        private string _shopOrdinalFormat = "Shop {0}";
        private string _relicLabel = "Relic:";
        private string _cardLabel = "Card:";

        public MerchantShopGridSubcard(IGameIconResolver icons, AnchoredTooltipHost tooltipHost, Func<IGameContentNameResolver?> getNames)
        {
            _icons = icons;
            _tooltipHost = tooltipHost;
            _getNames = getNames;
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            root.AddThemeConstantOverride("separation", 4);
            _title = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
            _hint = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
            _row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipContents = true
            };
            _row.AddThemeConstantOverride("separation", MerchantShopCardGap);
            for (int i = 0; i < ShopResultCount; i++)
            {
                var card = new PanelContainer
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsStretchRatio = 1f,
                    CustomMinimumSize = new Vector2(0f, MerchantShopCardHeight),
                    ClipContents = true
                };
                Ui1Theme.ApplyPanel(card, Ui1SurfaceRole.Input, 3f, 1, 5f);
                var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                body.AddThemeConstantOverride("separation", 6);
                _shopNumbers[i] = Ui1Theme.Label(string.Empty, Ui1TextRole.Accent);
                _shopNumbers[i].SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
                _shopNumbers[i].MouseFilter = Control.MouseFilterEnum.Ignore;
                _relicCells[i] = NewCell(); _uncommonCells[i] = NewCell(); _rareCells[i] = NewCell();
                var topRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                topRow.AddChild(_shopNumbers[i]);
                var relicLabel = Ui1Theme.Label(_relicLabel, Ui1TextRole.Muted);
                relicLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
                _relicLabels[i] = relicLabel;
                var cardLabel = Ui1Theme.Label(_cardLabel, Ui1TextRole.Muted);
                cardLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
                _cardLabels[i] = cardLabel;
                body.AddChild(topRow);
                body.AddChild(relicLabel);
                body.AddChild(_relicCells[i]);
                body.AddChild(cardLabel);
                body.AddChild(_uncommonCells[i]);
                body.AddChild(_rareCells[i]);
                card.AddChild(body); _row.AddChild(card);
            }
            root.AddChild(_title); root.AddChild(_hint); root.AddChild(_row); AddChild(root);
        }

        public void ApplyLocalization(string title, string hint, string shopOrdinalFormat, string relicLabel, string cardLabel)
        {
            _title.Text = title;
            _hint.Text = hint;
            _shopOrdinalFormat = shopOrdinalFormat;
            _relicLabel = relicLabel;
            _cardLabel = cardLabel;
            foreach (Label label in _relicLabels)
                if (label is not null) label.Text = _relicLabel;
            foreach (Label label in _cardLabels)
                if (label is not null) label.Text = _cardLabel;
        }

        public void Bind(RelicSequenceLaneViewModel? shop, Beta111ShopColorlessProjection? colorless, string unavailable, string missing)
        {
            _shop = shop; _colorless = colorless; _unavailable = unavailable; _missing = missing; Render();
        }

        private void Render()
        {
            for (int i = 0; i < ShopResultCount; i++)
            {
                Clear(_relicCells[i]); Clear(_uncommonCells[i]); Clear(_rareCells[i]);
                _shopNumbers[i].Text = string.Format(_shopOrdinalFormat, i + 1);
                if (!string.IsNullOrWhiteSpace(_unavailable))
                {
                    AddUnknown(_relicCells[i]); AddUnknown(_uncommonCells[i]); AddUnknown(_rareCells[i]); continue;
                }
                RelicSequenceEntryViewModel? relic = _shop?.Entries.OrderBy(item => item.Position).ElementAtOrDefault(i);
                if (relic is null) AddUnknown(_relicCells[i]);
                else AddValue(_relicCells[i], relic.RelicDisplay.ModelKey, GameContentKind.Relic, relic.RelicDisplay.DisplayName, IconVariant.Small, string.Empty, MerchantRelicIconSize);
                Beta111NormalMerchantProjection? merchant = _colorless?.Merchants.ElementAtOrDefault(i);
                if (merchant is null) { AddUnknown(_uncommonCells[i]); AddUnknown(_rareCells[i]); }
                else
                {
                    IGameContentNameResolver? names = _getNames();
                    if (names is null) { AddUnknown(_uncommonCells[i]); AddUnknown(_rareCells[i]); }
                    else
                    {
                        AddValue(_uncommonCells[i], merchant.UncommonColorless, GameContentKind.Card, names.Resolve(merchant.UncommonColorless, GameContentKind.Card), IconVariant.CardPickerLarge, string.Empty, MerchantColorlessIconSize);
                        AddValue(_rareCells[i], merchant.RareColorless, GameContentKind.Card, names.Resolve(merchant.RareColorless, GameContentKind.Card), IconVariant.CardPickerLarge, string.Empty, MerchantColorlessIconSize);
                    }
                }
            }
        }

        private void AddValue(VBoxContainer cell, ModelKey key, GameContentKind kind, string name, IconVariant variant, string prefix, float iconSize)
        {
            var item = new IconWithLabel(iconSize, Ui1TextRole.Meta)
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Alignment = BoxContainer.AlignmentMode.Begin
            };
            item.UseClippedName();
            item.Bind(_icons.Resolve(key, kind, variant), name, string.Empty, _missing);
            item.IconAnchor.MouseEntered += () => _tooltipHost.ShowFor(item.IconAnchor, key, kind, name);
            item.IconAnchor.MouseExited += () => _tooltipHost.Dismiss(item.IconAnchor);
            var itemHost = new Control
            {
                // The host deliberately contributes no horizontal content minimum:
                // the five merchant cards own the available width, while the
                // IconWithLabel name clips inside the width allocated to its cell.
                CustomMinimumSize = new Vector2(0f, iconSize),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            item.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            itemHost.AddChild(item);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                cell.AddChild(itemHost);
                return;
            }
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 4);
            var label = Ui1Theme.Label(prefix, Ui1TextRole.Muted);
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(label);
            row.AddChild(itemHost);
            cell.AddChild(row);
        }
        private static VBoxContainer NewCell() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        private static void AddUnknown(VBoxContainer cell) => cell.AddChild(Ui1Theme.Label("—", Ui1TextRole.Muted));
        private static void Clear(Node parent) { foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); } }
    }

    private sealed partial class ShopSequenceSubcard : PanelContainer
    {
        private readonly IGameIconResolver _icons;
        private readonly AnchoredTooltipHost _tooltipHost;
        private readonly float _iconSize;
        private readonly float _textureInset;
        private readonly Label _rowLabel;
        private readonly Label _hint;
        private readonly HBoxContainer _sequence;
        private readonly Label _measureLabel;
        private readonly HBoxContainer[] _items;
        private readonly Control[] _iconHosts;
        private readonly Label[] _nameLabels;
        private readonly Label _status;
        private RelicSequenceLaneViewModel? _lane;
        private string _unavailable = string.Empty;
        private string _missingIconText = string.Empty;

        public ShopSequenceSubcard(
            IGameIconResolver icons,
            AnchoredTooltipHost tooltipHost,
            float labelWidth,
            float iconSize,
            float textureInset,
            float minimumHeight,
            int shopItemContentGap)
        {
            _icons = icons;
            _tooltipHost = tooltipHost;
            _iconSize = iconSize;
            _textureInset = textureInset;
            Name = "ShopSequenceSubcard";
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            var root = new VBoxContainer
            {
                Name = "ShopSequenceBody",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            root.AddThemeConstantOverride("separation", 4);

            _rowLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
            _rowLabel.Name = "RowLabel";
            _rowLabel.CustomMinimumSize = new Vector2(0f, 20f);
            _rowLabel.HorizontalAlignment = HorizontalAlignment.Left;
            _rowLabel.VerticalAlignment = VerticalAlignment.Center;
            _rowLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

            _hint = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, wrap: false);
            _hint.Name = "ShopSequenceHint";
            _hint.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            _hint.MouseFilter = Control.MouseFilterEnum.Ignore;

            _sequence = new HBoxContainer
            {
                Name = "ShopSequence",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                CustomMinimumSize = new Vector2(0f, minimumHeight),
                Alignment = BoxContainer.AlignmentMode.Begin
            };
            _sequence.AddThemeConstantOverride("separation", ShopNamesGap);
            _sequence.Resized += ReflowShopWidths;

            _items = new HBoxContainer[ShopResultCount];
            _iconHosts = new Control[ShopResultCount];
            _nameLabels = new Label[ShopResultCount];
            for (int index = 0; index < ShopResultCount; index++)
            {
                var item = new HBoxContainer
                {
                    Name = $"ShopItem{index + 1}",
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    Alignment = BoxContainer.AlignmentMode.Center,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                item.AddThemeConstantOverride("separation", shopItemContentGap);
                _items[index] = item;

                var iconHost = new Control
                {
                    Name = "ShopRelicHost",
                    CustomMinimumSize = new Vector2(iconSize, iconSize),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _iconHosts[index] = iconHost;

                Label name = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, wrap: false);
                name.Name = "ShopRelicName";
                name.CustomMinimumSize = new Vector2(0f, iconSize);
                name.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
                name.VerticalAlignment = VerticalAlignment.Center;
                name.ClipText = false;
                name.MouseFilter = Control.MouseFilterEnum.Ignore;
                name.Visible = true;
                _nameLabels[index] = name;

                item.AddChild(iconHost);
                item.AddChild(name);
                _sequence.AddChild(item);
            }

            _measureLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, wrap: false);
            _measureLabel.Visible = false;
            _measureLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

            _status = NewStatus(string.Empty, warning: false);
            _status.Name = "ShopSequenceStatus";
            _status.Visible = false;

            root.AddChild(_rowLabel);
            root.AddChild(_hint);
            root.AddChild(_sequence);
            root.AddChild(_measureLabel);
            root.AddChild(_status);
            AddChild(root);
        }

        public void ApplyLocalization(string rowLabel, string hintText)
        {
            _rowLabel.Text = rowLabel;
            _hint.Text = hintText;
            ReflowShopWidths();
        }

        public void Bind(
            RelicSequenceLaneViewModel? shop,
            string unavailable,
            string missingIconText)
        {
            _lane = shop;
            _unavailable = unavailable;
            _missingIconText = missingIconText;
            Render();
        }

        private void Render()
        {
            bool isUnavailable = !string.IsNullOrWhiteSpace(_unavailable);
            _sequence.Visible = !isUnavailable;
            _status.Visible = isUnavailable;
            _status.Text = isUnavailable ? _unavailable : string.Empty;
            _status.AddThemeColorOverride("font_color", isUnavailable ? Ui1Theme.Palette.Warning : Ui1Theme.Palette.TextMuted);
            if (isUnavailable)
            {
                foreach (Control host in _iconHosts) Clear(host);
                foreach (Label name in _nameLabels) name.Text = string.Empty;
                return;
            }

            RelicSequenceEntryViewModel[] entries = _lane?.Entries
                .OrderBy(item => item.Position)
                .Take(ShopResultCount)
                .ToArray() ?? Array.Empty<RelicSequenceEntryViewModel>();

            for (int index = 0; index < _iconHosts.Length; index++)
            {
                Clear(_iconHosts[index]);
                RelicSequenceEntryViewModel? entry = entries.ElementAtOrDefault(index);
                _items[index].Visible = true;
                if (entry is null)
                {
                    _nameLabels[index].Text = string.Empty;
                    _iconHosts[index].AddChild(NewStatus("—", warning: false));
                    continue;
                }

                _nameLabels[index].Text = entry.RelicDisplay.DisplayName;
                _nameLabels[index].Visible = true;
                _iconHosts[index].AddChild(BuildRelicIcon(
                    _icons,
                    _tooltipHost,
                    entry,
                    _iconSize,
                    _textureInset,
                    _missingIconText));
            }
            ReflowShopWidths();
        }

        private void ReflowShopWidths()
        {
            RelicSequenceEntryViewModel[] entries = _lane?.Entries
                .OrderBy(item => item.Position)
                .Take(ShopResultCount)
                .ToArray() ?? Array.Empty<RelicSequenceEntryViewModel>();
            int count = Math.Min(entries.Length, ShopResultCount);
            if (count == 0 || _sequence.Size.X <= 1f)
            {
                return;
            }

            float usable = Math.Max(1f, _sequence.Size.X - ShopNamesGap * Math.Max(0, count - 1));
            float[] desired = new float[count];
            float[] minimum = new float[count];
            float[] measuredText = new float[count];
            for (int index = 0; index < count; index++)
            {
                _measureLabel.Text = entries[index].RelicDisplay.DisplayName;
                measuredText[index] = MathF.Ceiling(_measureLabel.GetMinimumSize().X);
                desired[index] = _iconSize + ShopItemContentGap + measuredText[index];
                minimum[index] = _iconSize + ShopItemContentGap + ShopMinimumNameWidth;
            }

            // Same presentation rule as Ancient options: natural content widths are used
            // first, the entire row remains left aligned, and short names donate only real
            // spare space to longer neighbors. Ellipsis appears only when the whole row
            // genuinely cannot contain all five complete names.
            float[] widths = AdaptiveInlineWidthAllocator.Allocate(usable, desired, minimum);

            for (int index = 0; index < count; index++)
            {
                bool clipped = widths[index] + 0.5f < desired[index];
                float availableTextWidth = Math.Max(
                    ShopMinimumNameWidth,
                    widths[index] - _iconSize - ShopItemContentGap);

                _items[index].CustomMinimumSize = new Vector2(widths[index], _iconSize);
                _nameLabels[index].CustomMinimumSize = new Vector2(
                    clipped ? availableTextWidth : measuredText[index],
                    _iconSize);
                _nameLabels[index].ClipText = clipped;
                if (clipped)
                {
                    _nameLabels[index].TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                }
            }
        }
    }

    private sealed partial class RelicBagSubcard : MarginContainer
    {
        private readonly IGameIconResolver _icons;
        private readonly AnchoredTooltipHost _tooltipHost;
        private readonly float _iconSize;
        private readonly float _textureInset;
        private readonly int _iconGap;
        private readonly int _headTailGap;
        private readonly int _centerBreakGap;
        private readonly float _centerBreakWidth;
        private readonly HBoxContainer _iconModeRow;
        private readonly Label _iconModeLabel;
        private readonly HBoxContainer _iconContent;
        private readonly HBoxContainer _namesModeRow;
        private readonly Label _namesModeLabel;
        private readonly HBoxContainer _namesQueue;
        private readonly Control _namesHeadBudget;
        private readonly HBoxContainer _namesHeadItems;
        private readonly HBoxContainer _namesDivider;
        private readonly Label _namesLeftEllipsis;
        private readonly Label _namesStarHandle;
        private readonly Label _namesRightEllipsis;
        private readonly Control _namesTailBudget;
        private readonly HBoxContainer _namesTailItems;
        private readonly Label _namesMeasureLabel;
        private readonly Label _namesFallbackMeasureLabel;
        private readonly Label _status;
        private string _headText = string.Empty;
        private string _tailText = string.Empty;
        private string _bagTooltipTitle = string.Empty;
        private string _bagTooltip = string.Empty;
        private RelicSequenceLaneViewModel? _lane;
        private string _unavailable = string.Empty;
        private string _missingIconText = string.Empty;
        private bool _draggingDivider;
        private bool _renderingNames;
        private bool _namesMode;
        private float _dragStartMouseX;
        private double _dragStartAllocation = 0.5d;
        private double _headAllocation = 0.5d;

        public RelicBagSubcard(
            IGameIconResolver icons,
            AnchoredTooltipHost tooltipHost,
            RelicSequenceKind kind,
            float labelWidth,
            float iconSize,
            float textureInset,
            int iconGap,
            int rowContentGap,
            int headTailGap,
            int centerBreakGap,
            float centerBreakWidth,
            float minimumHeight)
        {
            _icons = icons;
            _tooltipHost = tooltipHost;
            Kind = kind;
            _iconSize = iconSize;
            _textureInset = textureInset;
            _iconGap = iconGap;
            _headTailGap = headTailGap;
            _centerBreakGap = centerBreakGap;
            _centerBreakWidth = centerBreakWidth;
            Name = $"{kind}RelicSubcard";
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            var root = new VBoxContainer
            {
                Name = $"{kind}RelicPresentation",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            root.AddThemeConstantOverride("separation", 4);

            _iconModeRow = new HBoxContainer
            {
                Name = $"{kind}RelicIconRow",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0f, minimumHeight),
                Alignment = BoxContainer.AlignmentMode.Center
            };
            _iconModeRow.AddThemeConstantOverride("separation", rowContentGap);

            _iconModeLabel = NewRowLabel("RowLabel", labelWidth, minimumHeight);
            _iconContent = new HBoxContainer
            {
                Name = "DualEndedSequence",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                Alignment = BoxContainer.AlignmentMode.Center
            };
            _iconContent.AddThemeConstantOverride("separation", headTailGap);
            _iconModeRow.AddChild(_iconModeLabel);
            _iconModeRow.AddChild(_iconContent);

            _namesModeRow = new HBoxContainer
            {
                Name = $"{kind}RelicNamesRow",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0f, minimumHeight),
                Alignment = BoxContainer.AlignmentMode.Begin,
                Visible = false
            };
            _namesModeRow.AddThemeConstantOverride("separation", rowContentGap);
            _namesModeLabel = NewRowLabel("NamesRowLabel", labelWidth, minimumHeight);
            _namesModeRow.AddChild(_namesModeLabel);

            _namesQueue = new HBoxContainer
            {
                Name = $"{kind}RelicNamesQueue",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                Alignment = BoxContainer.AlignmentMode.Begin
            };
            _namesQueue.AddThemeConstantOverride("separation", 4);

            _namesHeadBudget = NewNamesBudget("BagHeadBudget");
            _namesHeadItems = NewNamesItems("BagHeadItems", BoxContainer.AlignmentMode.Begin);
            _namesHeadItems.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _namesHeadBudget.AddChild(_namesHeadItems);

            _namesDivider = new HBoxContainer
            {
                Name = "BagHeadTailDivider",
                CustomMinimumSize = new Vector2(NamesDividerWidth, NamesRelicIconSize),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = Control.MouseFilterEnum.Stop,
                MouseDefaultCursorShape = Control.CursorShape.Hsize
            };
            _namesDivider.AddThemeConstantOverride("separation", 2);
            _namesLeftEllipsis = NewDividerEllipsis("LeftOmitted");
            _namesStarHandle = Ui1Theme.Label("✦", Ui1TextRole.Accent, wrap: false);
            _namesStarHandle.Name = "BagAllocationStarHandle";
            _namesStarHandle.CustomMinimumSize = new Vector2(26f, NamesRelicIconSize);
            _namesStarHandle.HorizontalAlignment = HorizontalAlignment.Center;
            _namesStarHandle.VerticalAlignment = VerticalAlignment.Center;
            _namesStarHandle.MouseFilter = Control.MouseFilterEnum.Ignore;
            _namesStarHandle.AddThemeFontSizeOverride("font_size", 18);
            _namesRightEllipsis = NewDividerEllipsis("RightOmitted");
            _namesDivider.AddChild(_namesLeftEllipsis);
            _namesDivider.AddChild(_namesStarHandle);
            _namesDivider.AddChild(_namesRightEllipsis);

            _namesTailBudget = NewNamesBudget("BagTailBudget");
            _namesTailItems = NewNamesItems("BagTailItems", BoxContainer.AlignmentMode.End);
            _namesTailItems.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _namesTailBudget.AddChild(_namesTailItems);

            _namesQueue.AddChild(_namesHeadBudget);
            _namesQueue.AddChild(_namesDivider);
            _namesQueue.AddChild(_namesTailBudget);
            _namesModeRow.AddChild(_namesQueue);

            _namesMeasureLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, wrap: false);
            _namesMeasureLabel.Visible = false;
            _namesMeasureLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            _namesFallbackMeasureLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, wrap: false);
            _namesFallbackMeasureLabel.AddThemeFontSizeOverride("font_size", (int)NamesFallbackFontSize);
            _namesFallbackMeasureLabel.Visible = false;
            _namesFallbackMeasureLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

            _namesHeadBudget.Resized += RenderNamesPacking;
            _namesTailBudget.Resized += RenderNamesPacking;
            _namesQueue.Resized += () =>
            {
                ClampAllocationToCurrentMinimums();
                RenderNamesPacking();
            };
            _namesDivider.GuiInput += HandleDividerInput;

            _status = NewStatus(string.Empty, warning: false);
            _status.Name = "RelicSequenceStatus";
            _status.Visible = false;

            root.AddChild(_iconModeRow);
            root.AddChild(_namesModeRow);
            root.AddChild(_namesMeasureLabel);
            root.AddChild(_namesFallbackMeasureLabel);
            root.AddChild(_status);
            AddChild(root);
            SetProcess(false);
            UpdateNamesAllocationGeometry();
        }

        public RelicSequenceKind Kind { get; }

        public override void _Process(double delta)
        {
            if (!_draggingDivider)
            {
                SetProcess(false);
                return;
            }

            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _draggingDivider = false;
                SetProcess(false);
                return;
            }

            float flexibleWidth = GetFlexibleNamesWidth();
            if (flexibleWidth <= 1f)
            {
                return;
            }

            float currentX = GetViewport().GetMousePosition().X;
            (double min, double max) = ResolveAllocationBounds();
            _headAllocation = Math.Clamp(
                _dragStartAllocation + (currentX - _dragStartMouseX) / flexibleWidth,
                min,
                max);
            UpdateNamesAllocationGeometry();
            RenderNamesPacking();
        }

        public void ApplyLocalization(
            string label,
            string headText,
            string tailText,
            string tooltipTitle,
            string tooltip)
        {
            _iconModeLabel.Text = label;
            _namesModeLabel.Text = label;
            _headText = headText;
            _tailText = tailText;
            _bagTooltipTitle = tooltipTitle;
            _bagTooltip = tooltip;
            RenderActive();
        }

        public void Bind(
            RelicSequenceLaneViewModel? lane,
            string unavailable,
            string missingIconText)
        {
            _lane = lane;
            _unavailable = unavailable;
            _missingIconText = missingIconText;
            RenderActive();
        }

        public void SetDisplayMode(bool namesMode)
        {
            _namesMode = namesMode;
            RenderActive();
        }

        private void RenderActive()
        {
            bool hasError = !string.IsNullOrWhiteSpace(_unavailable) || _lane is null;
            _status.Visible = hasError;
            _iconModeRow.Visible = !hasError && !_namesMode;
            _namesModeRow.Visible = !hasError && _namesMode;

            if (hasError)
            {
                _status.Text = !string.IsNullOrWhiteSpace(_unavailable) ? _unavailable : "—";
                _status.AddThemeColorOverride(
                    "font_color",
                    !string.IsNullOrWhiteSpace(_unavailable) ? Ui1Theme.Palette.Warning : Ui1Theme.Palette.TextMuted);
                Clear(_iconContent);
                Clear(_namesHeadItems);
                Clear(_namesTailItems);
                return;
            }

            _status.Visible = false;
            if (_namesMode)
            {
                ClampAllocationToCurrentMinimums();
                UpdateNamesAllocationGeometry();
                RenderNamesPacking();
            }
            else
            {
                RenderIcons(_lane!);
            }
        }

        private void RenderIcons(RelicSequenceLaneViewModel lane)
        {
            Clear(_iconContent);
            Label head = NewBagEndLabel(_headText, "BagHead", _iconSize, 54f);
            WireBagTooltip(head);
            _iconContent.AddChild(head);

            HBoxContainer headIcons = NewIconRun("HeadIcons");
            foreach (RelicSequenceEntryViewModel entry in lane.Entries
                         .OrderBy(item => item.Position)
                         .Take(BagEndPreviewCount))
            {
                headIcons.AddChild(BuildRelicIcon(
                    _icons, _tooltipHost, entry, _iconSize, _textureInset, _missingIconText));
            }
            _iconContent.AddChild(headIcons);

            int shownCount = Math.Min(BagEndPreviewCount, lane.Entries.Count) +
                             Math.Min(BagEndPreviewCount, lane.TailEntries.Count);
            if (lane.TotalCount > shownCount)
            {
                _iconContent.AddChild(NewFixedGap(_centerBreakGap));
                Label ellipsis = Ui1Theme.Label("…", Ui1TextRole.Muted);
                ellipsis.Name = "BagMiddleBreak";
                ellipsis.CustomMinimumSize = new Vector2(_centerBreakWidth, _iconSize);
                ellipsis.HorizontalAlignment = HorizontalAlignment.Center;
                ellipsis.VerticalAlignment = VerticalAlignment.Center;
                ellipsis.MouseFilter = Control.MouseFilterEnum.Ignore;
                _iconContent.AddChild(ellipsis);
                _iconContent.AddChild(NewFixedGap(_centerBreakGap));
            }

            HBoxContainer tailIcons = NewIconRun("TailIcons");
            foreach (RelicSequenceEntryViewModel entry in lane.TailEntries
                         .OrderByDescending(item => item.Position)
                         .Take(BagEndPreviewCount))
            {
                tailIcons.AddChild(BuildRelicIcon(
                    _icons, _tooltipHost, entry, _iconSize, _textureInset, _missingIconText));
            }
            _iconContent.AddChild(tailIcons);

            Label tail = NewBagEndLabel(_tailText, "BagTail", _iconSize, 54f);
            WireBagTooltip(tail);
            _iconContent.AddChild(tail);
        }

        private void RenderNamesPacking()
        {
            if (_renderingNames || !_namesMode || _lane is null)
            {
                return;
            }

            _renderingNames = true;
            try
            {
                Clear(_namesHeadItems);
                Clear(_namesTailItems);

                RelicSequenceEntryViewModel[] headEntries = _lane.Entries
                    .OrderBy(item => item.Position)
                    .Take(BagEndPreviewCount)
                    .ToArray();
                // TailEntries Position 1 is the physical last relic in the bag.
                // Pack from that fixed tail anchor outward, then reverse only the
                // selected visible slice for left-to-right screen presentation.
                RelicSequenceEntryViewModel[] tailEntries = _lane.TailEntries
                    .OrderBy(item => item.Position)
                    .Take(BagEndPreviewCount)
                    .ToArray();

                int headCount = PackHead(headEntries, _namesHeadBudget.Size.X);
                int tailCount = PackTail(tailEntries, _namesTailBudget.Size.X);
                bool omitted = _lane.TotalCount > headCount + tailCount;
                _namesLeftEllipsis.Visible = omitted;
                _namesRightEllipsis.Visible = omitted;
            }
            finally
            {
                _renderingNames = false;
            }
        }

        private int PackHead(IReadOnlyList<RelicSequenceEntryViewModel> entries, float budgetWidth)
        {
            Label head = NewBagEndLabel(_headText, "BagHead", NamesRelicIconSize, NamesBagEndWidth);
            WireBagTooltip(head);
            _namesHeadItems.AddChild(head);
            if (entries.Count == 0)
            {
                return 0;
            }

            float used = NamesBagEndWidth;
            int visible = 0;
            foreach (RelicSequenceEntryViewModel entry in entries)
            {
                float itemWidth = MeasureDetailedRelicItemWidth(entry);
                float required = NamesItemGap + itemWidth;
                if (used + required <= budgetWidth || visible == 0)
                {
                    float maxWidth = visible == 0 && used + required > budgetWidth
                        ? Math.Max(NamesRelicIconSize + NamesIconNameGap + 28f, budgetWidth - used - NamesItemGap)
                        : 0f;
                    _namesHeadItems.AddChild(NewDetailedRelicItem(entry, maxWidth));
                    used += required;
                    visible++;
                    continue;
                }
                break;
            }
            return visible;
        }

        private int PackTail(IReadOnlyList<RelicSequenceEntryViewModel> entries, float budgetWidth)
        {
            if (entries.Count == 0)
            {
                Label tailOnly = NewBagEndLabel(_tailText, "BagTail", NamesRelicIconSize, NamesBagEndWidth);
                WireBagTooltip(tailOnly);
                _namesTailItems.AddChild(tailOnly);
                return 0;
            }

            var visibleEntries = new List<RelicSequenceEntryViewModel>();
            float used = NamesBagEndWidth;
            foreach (RelicSequenceEntryViewModel entry in entries)
            {
                float itemWidth = MeasureDetailedRelicItemWidth(entry);
                float required = NamesItemGap + itemWidth;
                if (used + required <= budgetWidth || visibleEntries.Count == 0)
                {
                    visibleEntries.Add(entry);
                    used += required;
                    continue;
                }
                break;
            }

            // Selection is anchored from the physical bag tail (Position 1), but
            // screen order expands leftward: third-last, second-last, last, ← Bag tail.
            foreach (RelicSequenceEntryViewModel entry in visibleEntries.AsEnumerable().Reverse())
            {
                float itemWidth = MeasureDetailedRelicItemWidth(entry);
                float maxWidth = visibleEntries.Count == 1 && NamesBagEndWidth + NamesItemGap + itemWidth > budgetWidth
                    ? Math.Max(NamesRelicIconSize + NamesIconNameGap + 28f, budgetWidth - NamesBagEndWidth - NamesItemGap)
                    : 0f;
                _namesTailItems.AddChild(NewDetailedRelicItem(entry, maxWidth));
            }
            Label tail = NewBagEndLabel(_tailText, "BagTail", NamesRelicIconSize, NamesBagEndWidth);
            WireBagTooltip(tail);
            _namesTailItems.AddChild(tail);
            return visibleEntries.Count;
        }

        private void HandleDividerInput(InputEvent inputEvent)
        {
            if (inputEvent is not InputEventMouseButton button || button.ButtonIndex != MouseButton.Left)
            {
                return;
            }

            if (button.Pressed)
            {
                _draggingDivider = true;
                _dragStartMouseX = GetViewport().GetMousePosition().X;
                _dragStartAllocation = _headAllocation;
                SetProcess(true);
            }
            else
            {
                _draggingDivider = false;
                SetProcess(false);
            }
            _namesDivider.AcceptEvent();
        }

        private void ClampAllocationToCurrentMinimums()
        {
            (double min, double max) = ResolveAllocationBounds();
            _headAllocation = Math.Clamp(_headAllocation, min, max);
            UpdateNamesAllocationGeometry();
        }

        private (double Min, double Max) ResolveAllocationBounds()
        {
            if (_lane is null)
            {
                return (0.5d, 0.5d);
            }

            float flexibleWidth = GetFlexibleNamesWidth();
            if (flexibleWidth <= 1f)
            {
                return (0.5d, 0.5d);
            }

            RelicSequenceEntryViewModel? headFirst = _lane.Entries.OrderBy(item => item.Position).FirstOrDefault();
            RelicSequenceEntryViewModel? tailFirst = _lane.TailEntries.OrderBy(item => item.Position).FirstOrDefault();
            float headMin = NamesBagEndWidth + NamesItemGap +
                            (headFirst is null ? 0f : MeasureDetailedRelicItemWidth(headFirst, allowFallback: true));
            float tailMin = NamesBagEndWidth + NamesItemGap +
                            (tailFirst is null ? 0f : MeasureDetailedRelicItemWidth(tailFirst, allowFallback: true));

            double min = Math.Clamp(headMin / flexibleWidth, 0.08d, 0.48d);
            double max = Math.Clamp(1d - tailMin / flexibleWidth, 0.52d, 0.92d);
            if (min >= max)
            {
                return (0.5d, 0.5d);
            }
            return (min, max);
        }

        private float GetFlexibleNamesWidth()
        {
            float queueWidth = _namesQueue.Size.X;
            if (queueWidth <= 1f)
            {
                // Source-level fallback before the first Godot layout pass. Runtime
                // Resized callbacks immediately replace this with the actual width.
                queueWidth = 1180f;
            }
            return Math.Max(1f, queueWidth - NamesDividerWidth - 8f);
        }

        private void UpdateNamesAllocationGeometry()
        {
            _namesHeadBudget.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _namesTailBudget.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _namesHeadBudget.SizeFlagsStretchRatio = (float)_headAllocation;
            _namesTailBudget.SizeFlagsStretchRatio = (float)(1d - _headAllocation);
            _namesQueue.QueueSort();
        }

        private float MeasureDetailedRelicItemWidth(
            RelicSequenceEntryViewModel entry,
            bool allowFallback = false)
        {
            Label measure = allowFallback ? _namesFallbackMeasureLabel : _namesMeasureLabel;
            measure.Text = entry.RelicDisplay.DisplayName;
            float textWidth = MathF.Ceiling(measure.GetMinimumSize().X);
            return NamesRelicIconSize + NamesIconNameGap + textWidth;
        }

        private Control NewDetailedRelicItem(RelicSequenceEntryViewModel entry, float constrainedWidth = 0f)
        {
            var item = new HBoxContainer
            {
                Name = $"DetailedRelic{entry.Position}",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                Alignment = BoxContainer.AlignmentMode.Begin,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            item.AddThemeConstantOverride("separation", NamesIconNameGap);
            item.AddChild(BuildRelicIcon(
                _icons,
                _tooltipHost,
                entry,
                NamesRelicIconSize,
                _textureInset,
                _missingIconText));

            Label name = Ui1Theme.Label(entry.RelicDisplay.DisplayName, Ui1TextRole.Body, wrap: false);
            name.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            name.ClipText = false;
            if (constrainedWidth > 0f)
            {
                float textWidth = Math.Max(28f, constrainedWidth - NamesRelicIconSize - NamesIconNameGap);
                name.CustomMinimumSize = new Vector2(textWidth, NamesRelicIconSize);
                name.AddThemeFontSizeOverride("font_size", (int)NamesFallbackFontSize);
                name.ClipText = true;
                name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                item.CustomMinimumSize = new Vector2(constrainedWidth, NamesRelicIconSize);
            }
            else
            {
                name.CustomMinimumSize = new Vector2(0f, NamesRelicIconSize);
            }
            item.AddChild(name);
            return item;
        }

        private HBoxContainer NewIconRun(string name)
        {
            var run = new HBoxContainer
            {
                Name = name,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                Alignment = BoxContainer.AlignmentMode.Center
            };
            run.AddThemeConstantOverride("separation", _iconGap);
            return run;
        }

        private static Control NewNamesBudget(string name) => new Control
        {
            Name = name,
            CustomMinimumSize = new Vector2(0f, NamesRelicIconSize),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        private static HBoxContainer NewNamesItems(string name, BoxContainer.AlignmentMode alignment)
        {
            var lane = new HBoxContainer
            {
                Name = name,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                Alignment = alignment,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            lane.AddThemeConstantOverride("separation", NamesItemGap);
            return lane;
        }

        private static Label NewDividerEllipsis(string name)
        {
            Label label = Ui1Theme.Label("…", Ui1TextRole.Muted, wrap: false);
            label.Name = name;
            label.CustomMinimumSize = new Vector2(18f, NamesRelicIconSize);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            return label;
        }

        private static Label NewRowLabel(string name, float width, float height)
        {
            Label label = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
            label.Name = name;
            label.CustomMinimumSize = new Vector2(width, height);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            return label;
        }

        private Label NewBagEndLabel(string text, string name, float height, float width)
        {
            Label label = Ui1Theme.Label(text, Ui1TextRole.Muted);
            label.Name = name;
            label.CustomMinimumSize = new Vector2(width, height);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MouseFilter = Control.MouseFilterEnum.Stop;
            return label;
        }

        private void WireBagTooltip(Control anchor)
        {
            anchor.MouseEntered += () =>
                _tooltipHost.ShowStructuredText(anchor, _bagTooltipTitle, string.Empty, _bagTooltip);
            anchor.MouseExited += () => _tooltipHost.Dismiss(anchor);
            anchor.TreeExiting += () => _tooltipHost.Dismiss(anchor);
        }
    }

    private static Control BuildRelicIcon(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost,
        RelicSequenceEntryViewModel entry,
        float outerSize,
        float textureInset,
        string missingIconText)
    {
        IconDescriptor descriptor = icons.Resolve(
            entry.RelicDisplay.ModelKey,
            GameContentKind.Relic,
            IconVariant.Small);
        var host = new Control
        {
            Name = $"RelicIcon{entry.Position}",
            CustomMinimumSize = new Vector2(outerSize, outerSize),
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
        texture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        texture.OffsetLeft = textureInset;
        texture.OffsetTop = textureInset;
        texture.OffsetRight = -textureInset;
        texture.OffsetBottom = -textureInset;

        Label missing = Ui1Theme.Label(texture.Visible ? string.Empty : "?", Ui1TextRole.Muted);
        missing.HorizontalAlignment = HorizontalAlignment.Center;
        missing.VerticalAlignment = VerticalAlignment.Center;
        missing.MouseFilter = Control.MouseFilterEnum.Ignore;
        missing.TooltipText = texture.Visible ? string.Empty : missingIconText;
        missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        host.AddChild(texture);
        host.AddChild(missing);
        host.MouseEntered += () =>
            tooltipHost.ShowFor(
                host,
                entry.RelicDisplay.ModelKey,
                GameContentKind.Relic,
                entry.RelicDisplay.DisplayName);
        host.MouseExited += () => tooltipHost.Dismiss(host);
        host.TreeExiting += () => tooltipHost.Dismiss(host);
        return host;
    }

    private static Label NewStatus(string text, bool warning)
    {
        Label label = Ui1Theme.Label(text, warning ? Ui1TextRole.Warning : Ui1TextRole.Muted, true);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    private static Control NewFixedGap(int width) => new Control
    {
        CustomMinimumSize = new Vector2(width, 0f),
        MouseFilter = Control.MouseFilterEnum.Ignore
    };

    private static void Clear(Node host)
    {
        foreach (Node child in host.GetChildren())
        {
            host.RemoveChild(child);
            child.QueueFree();
        }
    }
}
