using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Analysis;

// Layout adapter over the existing document, opening selection and context controller.
// No Query draft, route simulation or alternate prediction authority lives here.
internal sealed partial class AnalysisPage
{
    private HBoxContainer? _workbenchRoot;
    private VBoxContainer _wbCenter = null!, _wbRight = null!;
    private VSeparator _wbSeparator = null!;
    private readonly List<Button> _wbTabs = [];
    private int _wbPage;
    private bool _openingSubview = true;
    private bool _shopFullQueue;
    private bool _wbHasResult;
    private string _wbStatusKey = "predictor.empty";
    private Label? _searchOriginNotice;
    private string _unverifiedSearchSeed = "";
    private bool _searchOpeningUnavailable;
    internal void SetSearchOrigin(string seed, bool unverified, bool openingUnavailable = false)
    {
        _unverifiedSearchSeed = unverified ? seed : "";
        _searchOpeningUnavailable = openingUnavailable;
        RefreshSearchOrigin();
    }
    private void RefreshSearchOrigin()
    {
        if (_searchOriginNotice is null || _uiText is null) return;
        _searchOriginNotice.Visible = _unverifiedSearchSeed.Length > 0 &&
            string.Equals(CurrentDraft.RawSeed.Trim(), _unverifiedSearchSeed, StringComparison.OrdinalIgnoreCase);
        _searchOriginNotice.Text = Text(_searchOpeningUnavailable
            ? "workflow.predictor_unverified_opening_unavailable" : "workflow.predictor_unverified");
    }
    private readonly WorkspacePalette _palette = WorkspacePalette.Canonical;
    private static readonly string[] PageKeys = ["predictor.act1", "predictor.act2", "predictor.act3", "predictor.shops"];

    private void BuildWorkbench(IReadOnlyList<ModelKey> characters)
    {
        AddChild(_encounterPortraits);
        _neowChoiceStrip.HorizontalChoices = true;
        _workbenchRoot = new HBoxContainer { Name = "PredictorWorkbench" };
        _workbenchRoot.AddThemeConstantOverride("separation", 12); AddChild(_workbenchRoot);
        var rail = new VBoxContainer { Name = "PredictorContextRail", CustomMinimumSize = new Vector2(224, 0) };
        var railScroll = WScroll(); railScroll.SizeFlagsHorizontal = SizeFlags.Fill; railScroll.CustomMinimumSize = new Vector2(224, 0);
        _workbenchRoot.AddChild(railScroll); railScroll.AddChild(rail);
        _requestBar.Reparent(rail);
        _requestBar.UseRail(characters);
        BuildFavoriteSeed(rail);
        var contextSeparatorInset = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        contextSeparatorInset.AddThemeConstantOverride("margin_top", 68);
        var contextSeparator = new VSeparator();
        contextSeparator.AddThemeStyleboxOverride("separator", new StyleBoxLine
        {
            Color = _palette.Color(_palette.Line), Vertical = true, Thickness = 1
        });
        contextSeparatorInset.AddChild(contextSeparator);
        _workbenchRoot.AddChild(contextSeparatorInset);
        var workspace = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        workspace.AddThemeConstantOverride("separation", 16); _workbenchRoot.AddChild(workspace);
        var tabs = new HBoxContainer { Name = "PredictorPages" }; workspace.AddChild(tabs);
        tabs.Resized += () => contextSeparatorInset.AddThemeConstantOverride(
            "margin_top", Mathf.RoundToInt(tabs.Size.Y) + 24);
        for (int i = 0; i < PageKeys.Length; i++)
        {
            int page = i;
            var button = _palette.Button(""); button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.CustomMinimumSize = new Vector2(0, 44);
            button.Pressed += () => { if (_wbPage != page) { _wbPage = page; RenderWorkbench(); } };
            tabs.AddChild(button); _wbTabs.Add(button);
        }
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _searchOriginNotice = WLabel("");
        _searchOriginNotice.Name = "UnverifiedSearchOrigin";
        _searchOriginNotice.Visible = false;
        workspace.AddChild(_searchOriginNotice);
        body.AddThemeConstantOverride("separation", 16); workspace.AddChild(body);
        var centerScroll = WScroll(); centerScroll.Name = "PredictorMainScroll";
        body.AddChild(centerScroll); _wbCenter = WColumn(); _wbCenter.AddThemeConstantOverride("separation", 14); centerScroll.AddChild(_wbCenter);
        _wbSeparator = new VSeparator(); _wbSeparator.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = _palette.Color(_palette.Line), Vertical = true, Thickness = 1 });
        body.AddChild(_wbSeparator);
        _wbRight = WColumn(); _wbRight.Name = "PredictorAuxiliaryRail";
        _wbRight.SizeFlagsHorizontal = SizeFlags.Fill; _wbRight.CustomMinimumSize = new Vector2(244, 0); body.AddChild(_wbRight);
        TreeExiting += ClearWorkbenchProjections;
        // Wrapped labels settle after Godot's deferred container layout. Refit to
        // the shell canvas when their transient minimum shrinks after binding.
        MinimumSizeChanged += () => Callable.From(() =>
        {
            if (IsInsideTree() && GetParent() is Control) SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }).CallDeferred();
    }

    private static VBoxContainer WColumn()
    {
        var c = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        c.AddThemeConstantOverride("separation", 14); return c;
    }
    private static ScrollContainer WScroll() => new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private Label WLabel(string text, bool title = false) => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        Theme = new Godot.Theme { DefaultFontSize = title ? 20 : 17 },
        Modulate = _palette.Color(title ? _palette.Text : _palette.Secondary)
    };
    private void WTitle(Node parent, string key) => parent.AddChild(WLabel(Text(key), true));
    private void WEmpty(Node parent) => parent.AddChild(WLabel(Text("predictor.unavailable")));
    private static void WClear(Node parent)
    {
        foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
    private void ClearWorkbenchResults(string status)
    {
        if (_workbenchRoot is null) return;
        _wbHasResult = false; ClearWorkbenchProjections();
        RefreshWorkbenchLabels();
        _wbStatusKey = status;
        ShowMapRail(false);
        DetachOpeningDonors(); WClear(_wbCenter); WClear(_wbRight);
        _wbCenter.AddChild(WLabel(Text(status)));
    }
    private void DetachOpeningDonors()
    {
        // Keep reusable selector/detail nodes alive while replacing page contents.
        if (_neowChoiceStrip.GetParent() != _openingDonors) _neowChoiceStrip.Reparent(_openingDonors);
        if (_neowChoiceDetail.GetParent() != _openingDonors) _neowChoiceDetail.Reparent(_openingDonors);
    }
    private void RefreshWorkbenchLabels()
    {
        RefreshSearchOrigin();
        RefreshFavoriteSeed();
        for (int i = 0; i < _wbTabs.Count; i++)
        { RenderActTab(i); _palette.SetActive(_wbTabs[i], _wbPage == i); }
    }
    private void RenderWorkbench()
    {
        if (_workbenchRoot is null || _uiText is null) return;
        RefreshWorkbenchLabels();
        if (!_wbHasResult || _viewModel is null) return;
        _wbStatusKey = "";
        _combatSurface = null; _ancientSurface = null; DetachOpeningDonors(); WClear(_wbCenter); WClear(_wbRight);
        ShowMapRail(true);
        // Partial/Unknown domains do not erase other known domains.
        if (_wbPage == 3) RenderShopOverview();
        else RenderActOverview(_wbPage + 1);
    }
    private Control WObject(ModelKey key, GameContentKind kind, string? name = null, int size = 36, IconVariant? iconVariant = null)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        var variant = iconVariant ?? (kind == GameContentKind.Encounter ? IconVariant.WorldCompendiumBossIcon :
            kind == GameContentKind.Ancient ? IconVariant.WorldCompendiumAncientIcon : IconVariant.Small);
        var descriptor = _icons.Resolve(key, kind, variant);
        row.AddChild(new TextureRect { Texture = descriptor.Texture, CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore });
        row.AddChild(WLabel(name ?? _contentNames!.Resolve(key, kind), true)); return row;
    }
    private Control WCompactIdentity(ModelKey key, GameContentKind kind, string name, int iconSize = 30)
    {
        var tile = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.Fill,
            CustomMinimumSize = new Vector2(0, 42),
            MouseFilter = MouseFilterEnum.Ignore };
        tile.AddThemeConstantOverride("separation", 6);
        var variant = kind == GameContentKind.Ancient ? IconVariant.WorldCompendiumAncientIcon : IconVariant.Small;
        tile.AddChild(new TextureRect { Texture = _icons.Resolve(key, kind, variant).Texture,
            CustomMinimumSize = new Vector2(iconSize, iconSize),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore });
        var label = _palette.Label(name, 15);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.SizeFlagsHorizontal = SizeFlags.Fill;
        label.VerticalAlignment = VerticalAlignment.Center;
        tile.AddChild(label);
        return tile;
    }
    private static string? AncientOptionPremiseTextKey(ModelKey option) => option.Entry switch
    {
        "PAELS_CLAW" => Ui1TextKey.AncientConditionPaelGoopyDefend,
        "PAELS_LEGION" => Ui1TextKey.AncientConditionPaelNoEventPet,
        "PAELS_TOOTH" => Ui1TextKey.AncientConditionPaelRemovable,
        "ARCHAIC_TOOTH" => Ui1TextKey.AncientConditionOrobasArchaicTooth,
        "TOUCH_OF_OROBAS" => Ui1TextKey.AncientConditionOrobasTouch,
        "NUTRITIOUS_SOUP" => Ui1TextKey.AncientConditionTezcataraBasicStrike,
        "BEAUTIFUL_BRACELET" => Ui1TextKey.AncientConditionNonupeipeSwift,
        "TRI_BOOMERANG" => Ui1TextKey.AncientConditionTanxInstinct,
        "PANDORAS_BOX" => Ui1TextKey.AncientConditionDarvPandorasBox,
        _ => null
    };

    private static AncientOptionConditionProfile ResetAncientPremises(
        AncientOptionConditionProfile profile, ModelKey ancient) => ancient.Entry switch
    {
        "PAEL" => profile with { PaelGoopyDefendCardsAtLeast3 = true,
            PaelAllowLegionNoEventPet = true, PaelRemovableCardsAtLeast5 = true },
        "OROBAS" => profile with { OrobasArchaicToothConditionMet = true,
            OrobasTouchOfOrobasConditionMet = true },
        "TEZCATARA" => profile with { TezcataraHasBasicStrike = true },
        "NONUPEIPE" => profile with { NonupeipeSwiftEnchantableAtLeast4 = true },
        "TANX" => profile with { TanxInstinctEnchantableAtLeast3 = true },
        "DARV" => profile with { DarvAllowPandorasBoxRelicSet = true },
        _ => profile
    };

    private Control WAncientOption(ModelKey key, string name)
    {
        var identity = WCompactIdentity(key, GameContentKind.Relic, name, 32);
        BindWorkbenchTooltip(identity, key, GameContentKind.Relic, name);
        return identity;
    }
    private void RenderOpeningOverview() => RenderOpeningInspector();
    private Control WTile(ModelKey key, GameContentKind kind, string? name = null)
    {
        var tile = new HBoxContainer { CustomMinimumSize = new Vector2(0, 58),
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tile.AddThemeConstantOverride("separation", 8);
        string label = name ?? _contentNames!.Resolve(key, kind);
        var icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(44, 44),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Texture = _icons.Resolve(key, kind, IconVariant.Small).Texture,
        };
        BindWorkbenchTooltip(icon, key, kind, label);
        tile.AddChild(icon);
        tile.AddChild(new Label
        {
            Text = label, CustomMinimumSize = new Vector2(0, 50),
            Theme = new Godot.Theme { DefaultFontSize = 16 },
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        });
        return tile;
    }
    private Control WShopCard(ModelKey key)
    {
        const float width = 100f, height = 142f, scale = .29f;
        var tile = new Control { CustomMinimumSize = new Vector2(width, height),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        BindWorkbenchTooltip(tile, key, GameContentKind.Card, _contentNames!.Resolve(key, GameContentKind.Card));

        // Use the same read-only native card scene as the Neow card picker.
        var model = ModelDb.GetById<CardModel>(new ModelId(key.Category, key.Entry)).ToMutable();
        var card = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
        card.Model = model;
        card.Position = new Vector2(width / 2f, height / 2f);
        card.Scale = Vector2.One * scale;
        card.Ready += () =>
        {
            card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            card.GetNode<Control>("%Highlight").Hide();
            IgnoreShopCardInput(card);
        };
        tile.AddChild(card);
        return tile;
    }
    private static void IgnoreShopCardInput(Node node)
    {
        node.SetProcessInput(false);
        node.SetProcessUnhandledInput(false);
        node.SetProcessUnhandledKeyInput(false);
        if (node is Control control)
        {
            control.MouseFilter = MouseFilterEnum.Ignore;
            control.FocusMode = FocusModeEnum.None;
        }
        foreach (var child in node.GetChildren()) IgnoreShopCardInput(child);
    }
    private void RenderShopOverview()
    {
        ShowMapRail(false);
        var subpages = new HBoxContainer();
        subpages.AddThemeConstantOverride("separation", 12);
        _wbCenter.AddChild(subpages);
        var shops = _palette.Button(Text("predictor.shop_first_three"));
        var fullQueue = _palette.Button(Text("predictor.relic_full_queue"));
        shops.Name = "ShopPreviewTab"; fullQueue.Name = "FullRelicQueueTab";
        _palette.SetActive(shops, !_shopFullQueue);
        _palette.SetActive(fullQueue, _shopFullQueue);
        subpages.AddChild(shops);
        subpages.AddChild(fullQueue);
        shops.Pressed += () => { if (_shopFullQueue) { _shopFullQueue = false; RenderWorkbench(); } };
        fullQueue.Pressed += () => { if (!_shopFullQueue) { _shopFullQueue = true; RenderWorkbench(); } };

        if (_shopFullQueue)
        {
            RenderFullRelicQueues();
            return;
        }

        _wbCenter.AddChild(WNote(Text("predictor.design.shop_scope")));
        var shop = _viewModel!.RelicSequenceDomain.Items.FirstOrDefault(l => l.Kind == RelicSequenceKind.Shop);
        var colorless = BuildMerchantColorlessProjection();
        var grid = WResponsiveGrid(_wbCenter, "ShopPreviews", 238, 5);
        for (int i = 1; i <= 5; i++)
        {
            var col = WCard(grid, ""); col.Name = "ShopPreview" + i;
            col.AddThemeConstantOverride("separation", 8);
            var heading = WLabel(_uiText!.Format("predictor.shop_number", i), true);
            heading.AddThemeFontSizeOverride("font_size", 17);
            heading.Modulate = _palette.Color(_palette.Selected);
            col.AddChild(heading);
            var relic = shop?.Entries.OrderBy(e => e.Position).Skip(i - 1).FirstOrDefault();
            if (relic is not null)
                col.AddChild(WRelicPreviewTile(relic, showPosition: false));
            else WEmpty(col);
            var cards = colorless?.Merchants.FirstOrDefault(m => m.MerchantOrdinal == i);
            if (cards is not null)
            {
                var cardRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    Alignment = BoxContainer.AlignmentMode.Center };
                cardRow.AddThemeConstantOverride("separation", 6);
                cardRow.AddChild(WShopCard(cards.UncommonColorless));
                cardRow.AddChild(WShopCard(cards.RareColorless));
                col.AddChild(cardRow);
            }
            else WEmpty(col);
        }
        var bags = WResponsiveGrid(_wbCenter, "RelicBagPreviews", 560, 2);
        RenderRelicPreviewRow(bags, "predictor.player_relic_bag", _viewModel.RelicSequenceDomain.Items);
        RenderRelicPreviewRow(bags, "predictor.chest_shared_bag", _viewModel.TreasureRoomRelicSequenceDomain.Items);
    }
    private static GridContainer WResponsiveGrid(Node parent, string name, int itemWidth, int maximumColumns)
    {
        var grid = new GridContainer { Name = name, Columns = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        grid.Resized += () => grid.Columns = Math.Clamp((int)((grid.Size.X + 10) / (itemWidth + 10)), 1, maximumColumns);
        parent.AddChild(grid);
        return grid;
    }

    private Color WRelicRarityColor(RelicSequenceKind kind) => kind switch
    {
        RelicSequenceKind.Uncommon => new Color("AFC9E5"),
        RelicSequenceKind.Rare => _palette.Color(_palette.Selected),
        _ => _palette.Color(_palette.Secondary)
    };

    private void RenderRelicPreviewRow(Node parent, string titleKey, IReadOnlyList<RelicSequenceLaneViewModel> lanes)
    {
        var section = WCard(parent, "");
        var title = WLabel(Text(titleKey), true); title.AddThemeFontSizeOverride("font_size", 18);
        section.AddChild(title);
        section.AddChild(WNote(Text("predictor.design.relic_preview_scope")));
        var row = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("h_separation", 10);
        section.AddChild(row);

        foreach (var (kind, rarityKey) in new[]
                 {
                     (RelicSequenceKind.Common, "rarity.common"),
                     (RelicSequenceKind.Uncommon, "rarity.uncommon"),
                     (RelicSequenceKind.Rare, "rarity.rare")
                 })
        {
            var group = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            group.AddThemeConstantOverride("separation", 4);
            row.AddChild(group);
            var rarity = WLabel(Text(rarityKey)); rarity.Modulate = WRelicRarityColor(kind);
            group.AddChild(rarity);
            var entries = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            entries.AddThemeConstantOverride("separation", 4);
            group.AddChild(entries);
            var lane = lanes.FirstOrDefault(item => item.Kind == kind);
            if (lane is null) { WEmpty(entries); continue; }
            foreach (var entry in lane.FullEntries.OrderBy(item => item.Position).Take(3))
                entries.AddChild(WRelicPreviewTile(entry));
            if (entries.GetChildCount() == 0) WEmpty(entries);
        }
    }
    private Control WRelicPreviewTile(RelicSequenceEntryViewModel entry, bool showPosition = true)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var box = _palette.Box(_palette.Canvas);
        box.ContentMarginLeft = box.ContentMarginRight = 6;
        box.ContentMarginTop = box.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", box);
        BindWorkbenchTooltip(panel, entry.RelicDisplay.ModelKey, GameContentKind.Relic, entry.RelicDisplay.DisplayName);
        var tile = new HBoxContainer { CustomMinimumSize = new Vector2(0, 40),
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddChild(tile);
        tile.AddThemeConstantOverride("separation", 6);
        if (showPosition)
        {
            var number = _palette.Label(entry.Position.ToString("D2"), 12, true);
            number.CustomMinimumSize = new Vector2(18, 0);
            number.HorizontalAlignment = HorizontalAlignment.Right;
            tile.AddChild(number);
        }
        var icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(36, 36),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
            Texture = _icons.Resolve(entry.RelicDisplay.ModelKey, GameContentKind.Relic, IconVariant.Small).Texture,
        };
        tile.AddChild(icon);
        tile.AddChild(new Label
        {
            Text = entry.RelicDisplay.DisplayName,
            Theme = new Godot.Theme { DefaultFontSize = 15 },
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        });
        return panel;
    }
    private void RenderFullRelicQueues() => RenderRelicInspector();
    private void ShowMapRail(bool visible)
    {
        _wbRight.Visible = _wbSeparator.Visible = visible;
    }
    private Control WSequenceIcon(RelicSequenceEntryViewModel entry)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(54, 66) };
        var box = _palette.Box(_palette.Canvas);
        box.ContentMarginLeft = box.ContentMarginRight = 7;
        box.ContentMarginTop = box.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", box);
        BindWorkbenchTooltip(panel, entry.RelicDisplay.ModelKey, GameContentKind.Relic, entry.RelicDisplay.DisplayName);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2); panel.AddChild(col);
        col.AddChild(new TextureRect { Texture = _icons.Resolve(entry.RelicDisplay.ModelKey, GameContentKind.Relic, IconVariant.Small).Texture,
            CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
        var number = _palette.Label(entry.Position.ToString("D2"), 12, true);
        number.HorizontalAlignment = HorizontalAlignment.Center; col.AddChild(number);
        return panel;
    }
    private void BindWorkbenchTooltip(Control anchor, ModelKey key, GameContentKind kind, string name)
    {
        anchor.MouseFilter = MouseFilterEnum.Stop;
        anchor.MouseEntered += () => _tooltipHost.ShowFor(anchor, key, kind, name);
        anchor.MouseExited += () => _tooltipHost.Dismiss(anchor);
        anchor.TreeExiting += () => _tooltipHost.Dismiss(anchor);
    }
    private void RenderActOverview(int act)
    {
        bool openingPage = act == 1 && _openingSubview;
        var modes = new HBoxContainer(); modes.AddThemeConstantOverride("separation", 10); _wbCenter.AddChild(modes);
        if (act == 1)
        {
            var opening = WAction("predictor.opening_prediction"); opening.Name = "OpeningPredictionTab";
            _palette.SetActive(opening, _openingSubview); modes.AddChild(opening);
            opening.Pressed += () => { if (!_openingSubview) { _openingSubview = true; RenderWorkbench(); } };
        }
        var overview = WAction("predictor.overview"); overview.Name = "OverviewTab";
        _palette.SetActive(overview, !openingPage); modes.AddChild(overview);
        overview.Pressed += () => { if (openingPage) { _openingSubview = false; RenderWorkbench(); } };
        modes.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var actInfo = _viewModel!.EventPoolSequenceDomain.Items.FirstOrDefault(a => a.Act == act);
        if (openingPage) RenderOpeningOverview();
        else
        {
            if (act > 1)
            {
                var ancient = _viewModel.AncientDomain.Items.FirstOrDefault(a => a.Act == act);
                if (ancient is not null) RenderAncientInspector(ancient);
                else WEmpty(_wbCenter);
            }
            RenderEventInspector(act, actInfo);
            RenderCombatDetails(act);
        }
        ShowMapRail(true);
        var mapHeader = new HBoxContainer(); _wbRight.AddChild(mapHeader);
        mapHeader.AddChild(WLabel(_uiText!.Format("predictor.map", act), true));
        var clearRoute = WAction("ui1.map.route.clear"); clearRoute.Name = "ClearMapRoute"; clearRoute.Disabled = true;
        mapHeader.AddChild(clearRoute);
        if (actInfo is null || LastRequest is null || LastRequest.Authority.ProfileId != RolltheSpire2.Compatibility.RuntimeProfileId.Beta111)
        { WEmpty(_wbRight); return; }
        var routeStatus = WNote(Text("predictor.design.map_draw")); routeStatus.Name = "MapRouteStatus";
        routeStatus.TooltipText = Text("predictor.design.map_ordinary"); _wbRight.AddChild(routeStatus);
        var mapStats = new GridContainer { Name = "MapMaximums", Columns = 4, CustomMinimumSize = new Vector2(0, 34) };
        mapStats.AddThemeConstantOverride("h_separation", 4);
        mapStats.AddThemeConstantOverride("v_separation", 4);
        _wbRight.AddChild(mapStats);
        var routeBrowser = new HBoxContainer { Name = "MapRouteBrowser", Visible = false };
        _wbRight.AddChild(routeBrowser);
        var actModel = ModelDb.GetById<ActModel>(new ModelId(actInfo.ActKey.Category, actInfo.ActKey.Entry));
        var mapPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        mapPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = actModel.MapBgColor,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
        _wbRight.AddChild(mapPanel);
        var surface = new VerticalMapView { Name = $"Act{act}OverviewMap" };
        surface.SetBackgroundColor(actModel.MapBgColor);
        mapPanel.AddChild(surface); LoadOverviewMap(act, actInfo.ActKey, surface, mapStats, clearRoute, routeStatus, routeBrowser);
    }
    private async void LoadOverviewMap(int act, ModelKey actKey, VerticalMapView surface, GridContainer mapStats, Button clearRoute, Label routeStatus, HBoxContainer routeBrowser)
    {
        var request = LastRequest!;
        var cancellation = (_overviewLifetime ??= new CancellationTokenSource()).Token;
        var bosses = _viewModel!.BossDomain.Items.Where(b => b.Act == act).OrderBy(b => b.Ordinal).ToArray();
        var ancient = _viewModel.AncientDomain.Items.FirstOrDefault(a => a.Act == act);
        try
        {
            var map = await GetOverviewMap(act, actKey, bosses.Length > 1, cancellation);
            // A premise-only request revision shares this map's authority and
            // lifetime. A new seed/context cancels the lifetime in ShowDocument.
            if (cancellation.IsCancellationRequested || !IsInstanceValid(surface) || !surface.IsInsideTree() ||
                !ReferenceEquals(LastRequest?.Authority, request.Authority) || _wbPage != act - 1) return;
            surface.Bind(map, bosses.Select(b => _icons.Resolve(b.BossDisplay.ModelKey, GameContentKind.Encounter, IconVariant.WorldCompendiumBossIcon).Texture).ToArray(),
                ancient is null ? null : _icons.Resolve(ancient.AncientDisplay.ModelKey, GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon).Texture, _uiText!);
            RenderMapStats(map, surface, mapStats, clearRoute, routeStatus, routeBrowser);
            PopulateCombatDetails(act, map);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            RolltheSpire2.Bootstrap.RuntimeLog.Warn("predictorOverviewMapUnavailable=" + ex.GetType().Name);
            if (!cancellation.IsCancellationRequested && IsInstanceValid(surface) && surface.IsInsideTree())
            { _wbRight.AddChild(WLabel(Text("ui1.map.unavailable"))); }
        }
    }
    private void RenderMapStats(MapPrediction map, VerticalMapView surface, GridContainer row, Button clearRoute, Label routeStatus, HBoxContainer routeBrowser)
    {
        int act = map.ActIndex + 1;
        var allRoutes = new MapRouteSet(map);
        MapPointType? selected = _overviewRouteMetrics.TryGetValue(act, out var metric) ? metric : null;
        MapRouteSet? optimalRoutes = null;
        System.Numerics.BigInteger rank = 0;
        var extrema = new Dictionary<MapPointType, MapRouteSet>();
        var buttons = new List<(MapPointType Type, Button Button)>();
        var previous = WAction("ui1.map.route.previous"); previous.Name = "PreviousMapRoute";
        previous.Text = "‹"; previous.TooltipText = Text("ui1.map.route.previous");
        var routeCount = WNote(""); routeCount.Name = "MapRouteCount";
        routeCount.HorizontalAlignment = HorizontalAlignment.Center;
        routeCount.AutowrapMode = TextServer.AutowrapMode.Off;
        routeCount.ClipText = true; routeCount.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var next = WAction("ui1.map.route.next"); next.Name = "NextMapRoute";
        next.Text = "›"; next.TooltipText = Text("ui1.map.route.next");
        routeBrowser.AddChild(previous); routeBrowser.AddChild(routeCount); routeBrowser.AddChild(next);
        void PresentRoute()
        {
            var route = _overviewRoutes.GetValueOrDefault(act);
            surface.SetPointTypeHighlight(selected);
            surface.SetRoute(route?.Coordinates, committed: route is not null, clearStroke: true);
            surface.SetUnknownRooms(null);
            routeStatus.Text = Text("predictor.design.map_draw");
            clearRoute.Disabled = route is null;
            routeBrowser.Visible = optimalRoutes is not null;
            previous.Disabled = next.Disabled = optimalRoutes is null || optimalRoutes.Count <= 1;
            if (optimalRoutes is not null)
            {
                rank = optimalRoutes.Rank(route!.Coordinates);
                routeCount.Text = _uiText!.Format("predictor.design.map_route_count", rank + 1, optimalRoutes.Count);
                routeCount.TooltipText = routeCount.Text;
            }
            foreach (var item in buttons) _palette.SetActive(item.Button, item.Type == selected);
            if (route is null) return;
            var prior = Enumerable.Range(1, act - 1).Select(a => _overviewRoutes.GetValueOrDefault(a)).ToArray();
            int missing = Array.FindIndex(prior, p => p is null);
            if (missing >= 0)
            {
                routeStatus.Text = _uiText!.Format("predictor.design.map_prior", missing + 1);
                return;
            }
            // Reuse the existing ordinary-room contract. This is local map annotation,
            // not a played-route simulation or a change to the rewards/event queues.
            var sequence = OrdinaryUnknownRoomPredictor.ForRoute(route, prior.Cast<MapRouteCommitment>().ToArray());
            surface.SetUnknownRooms(sequence.Steps);
            routeStatus.Text = Text("predictor.design.map_drawn");
        }
        void SelectMetric(MapPointType? type)
        {
            selected = type;
            optimalRoutes = type is { } value ? extrema[value] : null;
            if (type is { } saved) _overviewRouteMetrics[act] = saved;
            else _overviewRouteMetrics.Remove(act);
            if (optimalRoutes is not null && (!_overviewRoutes.TryGetValue(act, out var current) || !optimalRoutes.IsLegal(current.Coordinates)))
                _overviewRoutes[act] = new MapRouteCommitment(map, optimalRoutes.At(0));
            PresentRoute();
        }
        void Browse(int delta)
        {
            if (optimalRoutes is null) return;
            rank = (rank + delta + optimalRoutes.Count) % optimalRoutes.Count;
            _overviewRoutes[act] = new MapRouteCommitment(map, optimalRoutes.At(rank));
            PresentRoute();
        }
        previous.Pressed += () => Browse(-1);
        next.Pressed += () => Browse(1);
        surface.StrokeFinished += stroke =>
        {
            _overviewRoutes[act] = new MapRouteCommitment(map, allRoutes.Match(stroke));
            SelectMetric(null);
        };
        clearRoute.Pressed += () => { _overviewRoutes.Remove(act); SelectMetric(null); };
        foreach (var (type, textKey) in new[]
                 {
                     (MapPointType.RestSite, "query.map.node.rest"),
                     (MapPointType.Elite, "query.map.node.elite"),
                     (MapPointType.Unknown, "query.map.node.unknown"),
                     (MapPointType.Monster, "query.map.node.monster")
                 })
        {
            var routes = new MapRouteSet(map, type);
            extrema[type] = routes;
            int maximum = routes.Value;
            var button = _palette.Button("");
            button.Name = "MapMaximum" + type;
            var content = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            content.AddThemeConstantOverride("separation", 5);
            button.AddChild(content); content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            content.AddChild(new TextureRect { Texture = RoomIcon(type.ToString()), CustomMinimumSize = new Vector2(24, 24),
                SizeFlagsVertical = SizeFlags.ShrinkCenter, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
            var valueLabel = _palette.Label(maximum.ToString(), 16); valueLabel.Name = "MaximumValue";
            valueLabel.VerticalAlignment = VerticalAlignment.Center; valueLabel.MouseFilter = MouseFilterEnum.Ignore;
            content.AddChild(valueLabel);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.AddThemeFontSizeOverride("font_size", 15);
            button.TooltipText = _uiText!.Format("ui1.map.route.max", Text(textKey), maximum) + "\n" + Text("predictor.design.map_scope");
            button.CustomMinimumSize = new Vector2(54, 34);
            button.Pressed += () => SelectMetric(selected == type ? null : type);
            row.AddChild(button);
            buttons.Add((type, button));
        }
        SelectMetric(selected);
    }
}
