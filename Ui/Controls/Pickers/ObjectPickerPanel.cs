using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Controls.Pickers;

internal sealed partial class ObjectPickerPanel : Control
{
    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly Dictionary<BaseButton, bool> _blocked = [];
    private IGameContentNameResolver _names = null!;
    private IUiTextProvider _text = null!;
    private IGameIconResolver _icons = null!;
    public event Action<bool>? ModalChanged;

    public ObjectPickerPanel()
    { Name = "WorkbenchObjectPicker"; Visible = false; Theme = MainPanelTheme.Create(); }

    public void AttachOverlay(Control shell, Control tooltipScope)
    { Reparent(shell); SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); SearchTooltip.AttachScope(this, tooltipScope); }

    public void Open(ObjectPickerRequest request)
    {
        ClosePicker();
        _names = request.Names; _text = request.Text; _icons = request.Icons;
        var pool = request.Candidates;
        var selected = request.Selected;
        _blocked.Clear();
        foreach (var node in GetParent().FindChildren("*", "BaseButton", true, false))
            if (node is BaseButton button && !IsAncestorOf(button))
            { _blocked[button] = button.Disabled; button.Disabled = true; }
        Clear(this); Show(); MoveToFront(); ModalChanged?.Invoke(true);
        var shade = new ColorRect { Color = new Color(0, 0, 0, .68f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var modal = new Panel { Position = new(160, 96), Size = new(1280, 708) };
        modal.AddThemeStyleboxOverride("panel", _p.Box(_p.Canvas, _p.Line, 1)); AddChild(modal);
        Text(modal, _text.Get("picker.title"), 24, 22, 220, 24);
        Button(modal, "×", 1208, 12, 48, () => ClosePicker());
        bool cards = request.CardContext is not null;
        var cardContext = request.CardContext;
        var filters = new CardPickerFilterState();
        var colorlessCards = pool.Where(key => cardContext?.IsColorlessPoolMember(key) == true)
            .ToHashSet(ModelKeyComparer.Instance);
        var curseCards = pool.Where(key => request.Curses?.Contains(key, ModelKeyComparer.Instance) == true ||
            cardContext is not null && cardContext.TryGet(key, out CardPickerCandidateMetadata metadata) && metadata.Rarity == EffectCardRarity.Curse)
            .ToHashSet(ModelKeyComparer.Instance);
        var ancientCards = pool.Where(key => cardContext is not null && cardContext.TryGet(key, out CardPickerCandidateMetadata metadata) &&
            metadata.Rarity == EffectCardRarity.Ancient).ToHashSet(ModelKeyComparer.Instance);
        var sourceSets = new Dictionary<string, HashSet<ModelKey>>(StringComparer.Ordinal);
        if (cardContext is not null)
            foreach (ModelKey character in cardContext.AvailableCharacterKeys)
                sourceSets["character:" + character.Serialized] = pool.Where(key =>
                    cardContext.TryGet(key, out CardPickerCandidateMetadata metadata) && metadata.CharacterKeys.Contains(character, ModelKeyComparer.Instance))
                    .ToHashSet(ModelKeyComparer.Instance);
        if (colorlessCards.Count > 0) sourceSets["colorless"] = colorlessCards;
        if (curseCards.Count > 0) sourceSets["curse"] = curseCards;
        if (ancientCards.Count > 0) sourceSets["ancient"] = ancientCards;
        if (cards)
        {
            ModelKey[] classified = sourceSets.Values.SelectMany(keys => keys).Distinct(ModelKeyComparer.Instance).ToArray();
            HashSet<ModelKey> other = pool.Where(key => !classified.Contains(key, ModelKeyComparer.Instance)).ToHashSet(ModelKeyComparer.Instance);
            if (other.Count > 0) sourceSets["other"] = other;
        }
        IReadOnlySet<ModelKey> multiplayerOnlyCards = request.MultiplayerOnlyCards ?? new HashSet<ModelKey>(ModelKeyComparer.Instance);
        int pickerPlayers = request.Players;
        string? selectedSource = null;
        bool includeMultiplayer = pickerPlayers > 1;
        string? selectedRarity = null;
        string ObjectRarity(ModelKey key)
        {
            try
            {
                if (key.Category == "RELIC") return ModelDb.GetById<RelicModel>(new ModelId(key.Category, key.Entry)).Rarity.ToString();
                if (key.Category == "POTION") return ModelDb.GetById<PotionModel>(new ModelId(key.Category, key.Entry)).Rarity.ToString();
            }
            catch { }
            return request.ObjectRarity(key);
        }
        string RarityName(string rarity) => rarity switch
        { "Common" => _text.Get("rarity.common"), "Uncommon" => _text.Get("rarity.uncommon"), "Rare" => _text.Get("rarity.rare"),
          "Ancient" => _text.Get("rarity.ancient"), "Event" => _text.Get("rarity.event"),
          "Shop" => _text.Get("rarity.shop"), _ => _text.Get("common.other") };
        const float filterRailWidth = 220, filterRailGap = 24;
        float browserLeft = 24 + filterRailWidth + filterRailGap;
        float browserWidth = 1256 - browserLeft;
        var search = new LineEdit { Position = new(browserLeft, 14), Size = new(browserWidth - 64, 44), PlaceholderText = _text.Get("picker.search_name") };
        search.AddThemeStyleboxOverride("normal", _p.Box(_p.Surface)); search.AddThemeStyleboxOverride("focus", _p.FocusRing()); modal.AddChild(search);
        float gridTop = 76;
        const int cardLeftOverhang = 16;
        int leftInset = cards ? cardLeftOverhang : 0;
        var scroll = new ScrollContainer { Position = new(browserLeft - leftInset, gridTop), Size = new(browserWidth + leftInset, 676 - gridTop), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; modal.AddChild(scroll);
        // Keep frame centers/grid density unchanged; give protruding star costs room
        // inside the scroll clip, rather than offsetting the native card itself.
        var gridInset = new MarginContainer();
        gridInset.AddThemeConstantOverride("margin_left", leftInset);
        scroll.AddChild(gridInset);
        var grid = new GridContainer { Columns = cards ? 5 : 6 }; grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 12); gridInset.AddChild(grid);
        var nativeTiles = new List<(Button Tile, ModelKey Key)>();
        var nativeViews = new Dictionary<Button, Control>();
        void RefreshNativeCards()
        {
            if (!cards || !GodotObject.IsInstanceValid(scroll) || !scroll.IsInsideTree() || !Visible) return;
            var viewport = scroll.GetGlobalRect().Grow(80);
            foreach (var (tile, key) in nativeTiles)
            {
                bool visible = tile.GetGlobalRect().Intersects(viewport);
                if (visible && !nativeViews.ContainsKey(tile)) nativeViews[tile] = AddNativePickerCard(tile, key);
                else if (!visible && nativeViews.Remove(tile, out var view)) { tile.RemoveChild(view); view.QueueFree(); }
            }
        }
        scroll.GetVScrollBar().ValueChanged += _ => RefreshNativeCards();
        void Filter(string text)
        {
            nativeTiles.Clear(); nativeViews.Clear();
            Clear(grid);
            grid.Columns = cards ? 5 : 6;
            foreach (var key in pool.Distinct().Where(k => NameOf(k).Contains(text, StringComparison.OrdinalIgnoreCase))
                         .Where(k => cardContext is null || filters.Matches(cardContext, k))
                         .Where(k => selectedSource is null || sourceSets[selectedSource].Contains(k))
                         .Where(k => !cards || includeMultiplayer || !multiplayerOnlyCards.Contains(k))
                         .Where(k => selectedRarity is null || ObjectRarity(k) == selectedRarity))
            {
                float tileWidth = cards ? FullCardTileWidth : 146;
                var b = _p.Button(""); b.CustomMinimumSize = new(tileWidth, cards ? FullCardTileHeight : 140); grid.AddChild(b);
                if (cards)
                {
                    b.AccessibilityName = NameOf(key);
                    b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
                    b.AddThemeStyleboxOverride("hover", _p.Box(_p.Hover));
                    nativeTiles.Add((b, key));
                }
                else
                {
                    string objectName = NameOf(key);
                    var label = Text(b, objectName, 6, 6, tileWidth - 12, 16);
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                    Picture(b, key, 28, 36, 90);
                }
                b.Pressed += () =>
                {
                    if (!pool.Contains(key) || (cardContext is not null && !cardContext.IsAllowed(key))) return;
                    ClosePicker(); selected(key);
                };
            }
            Callable.From(RefreshNativeCards).CallDeferred();
        }
        {
            var railScroll = new ScrollContainer { Position = new(24, 76), Size = new(filterRailWidth, cards ? 550 : 600), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            modal.AddChild(railScroll);
            var rail = new VBoxContainer { CustomMinimumSize = new(filterRailWidth - 16, 0) };
            rail.AddThemeConstantOverride("separation", 4); railScroll.AddChild(rail);
            var characterIcons = new NativeCharacterPoolIconProvider(_icons);
            var filterIcons = new CardPickerFilterIconProvider();
            var selectedStates = new List<(Button Button, Func<bool> Selected)>();
            void StyleChoice(Button button, bool active) => _p.SetModeActive(button, active);
            void RefreshFilters()
            {
                foreach (var item in selectedStates) StyleChoice(item.Button, item.Selected());
                Filter(search.Text);
            }
            void Heading(string title)
            {
                if (rail.GetChildCount() > 0) rail.AddChild(new Control { CustomMinimumSize = new(0, 12) });
                rail.AddChild(_p.Label(title, 18, true));
            }
            void Choice(string title, Texture2D? texture, Action toggle, Func<bool> active, bool disabled = false)
            {
                var button = _p.Button("");
                _p.SetModeActive(button, active());
                button.CustomMinimumSize = new(filterRailWidth - 16, 32); button.Disabled = disabled;
                StyleChoice(button, active());
                rail.AddChild(button);
                if (texture is not null)
                {
                    // Scale explicitly: assigning TextureRect.Size before its expand mode
                    // let the texture's native minimum size enlarge the rail icons.
                    var dimensions = texture.GetSize();
                    button.AddChild(new Sprite2D { Texture = texture, Position = new(18, 16),
                        Scale = Vector2.One * (24f / Math.Max(1f, Math.Max(dimensions.X, dimensions.Y))) });
                }
                Text(button, title, texture is null ? 10 : 40, 2, texture is null ? 182 : 152, 16);
                selectedStates.Add((button, active));
                button.Pressed += () => { toggle(); RefreshFilters(); };
            }
            if (cardContext is not null)
            {
            Heading(_text.Get("picker.card.filter.pool"));
            var sources = new List<(string Id, string Title, Texture2D? Texture)>();
            foreach (ModelKey character in cardContext.AvailableCharacterKeys)
            {
                string id = "character:" + character.Serialized;
                if (sourceSets.ContainsKey(id))
                    sources.Add((id, _names.Resolve(character, GameContentKind.Character), characterIcons.Resolve(character).Texture));
            }
            if (sourceSets.ContainsKey("colorless"))
                sources.Add(("colorless", _text.Get("picker.card.filter.colorless"), characterIcons.ResolveColorlessPoolVisual()));
            if (sourceSets.ContainsKey("curse"))
                sources.Add(("curse", _text.Get("picker.card.filter.curse"), null));
            if (sourceSets.ContainsKey("ancient"))
                sources.Add(("ancient", _text.Get("picker.card.filter.ancient"), null));
            if (sourceSets.ContainsKey("other"))
                sources.Add(("other", _text.Get("common.other"), null));
            if (sources.Count > 1)
                Choice(_text.Get("common.all"), null, () => { selectedSource = null; filters.SelectAllCharacters(); }, () => selectedSource is null);
            foreach (var source in sources)
                Choice(source.Title, source.Texture,
                    () => { selectedSource = selectedSource == source.Id ? null : source.Id; filters.SelectAllCharacters(); },
                    () => sources.Count == 1 || selectedSource == source.Id,
                    sources.Count == 1);
            Heading(_text.Get("picker.card.filter.rarity"));
            foreach (var rarity in cardContext.AvailableRarities)
                Choice(rarity switch { EffectCardRarity.Common => _text.Get("rarity.common"), EffectCardRarity.Uncommon => _text.Get("rarity.uncommon"), _ => _text.Get("rarity.rare") },
                    filterIcons.ResolveRarity(rarity), () => filters.ToggleRarity(rarity),
                    () => filters.Rarities.Contains(rarity) || cardContext.AvailableRarities.Count == 1,
                    cardContext.AvailableRarities.Count <= 1);
            Heading(_text.Get("picker.card.filter.type"));
            foreach (var type in cardContext.AvailableTypes)
                Choice(type switch { EffectCardType.Attack => _text.Get("card.type.attack"), EffectCardType.Skill => _text.Get("card.type.skill"), _ => _text.Get("card.type.power") },
                    filterIcons.ResolveType(type), () => filters.ToggleType(type),
                    () => filters.Types.Contains(type) || cardContext.AvailableTypes.Count == 1,
                    cardContext.AvailableTypes.Count <= 1);
            if (pickerPlayers > 1)
            {
            var multiplayer = _p.Button("");
            multiplayer.ToggleMode = true;
            multiplayer.Position = new(24, 640); multiplayer.Size = new(filterRailWidth, 36);
            multiplayer.CustomMinimumSize = new(0, 36);
            multiplayer.ButtonPressed = includeMultiplayer;
            multiplayer.Disabled = !pool.Any(multiplayerOnlyCards.Contains);
            multiplayer.AccessibilityName = _text.Get("picker.card.filter.multiplayer_only");
            multiplayer.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            multiplayer.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            modal.AddChild(multiplayer);
            var box = new Panel { Position = new(6, 8), Size = new(20, 20), MouseFilter = MouseFilterEnum.Ignore };
            var border = _p.Box(_p.Canvas, _p.Text, 2);
            border.CornerRadiusTopLeft = border.CornerRadiusTopRight = border.CornerRadiusBottomLeft = border.CornerRadiusBottomRight = 3;
            box.AddThemeStyleboxOverride("panel", border);
            multiplayer.AddChild(box);
            var mark = Text(box, "✓", 0, -4, 20, 19);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            var caption = Text(multiplayer, multiplayer.AccessibilityName, 36, 4, filterRailWidth - 36, 16);
            void UpdateCheck(bool enabled)
            {
                mark.Visible = enabled;
                box.Modulate = multiplayer.Disabled ? new Color(1, 1, 1, .45f) : Colors.White;
                caption.AddThemeColorOverride("font_color", _p.Color(multiplayer.Disabled ? _p.Disabled : _p.Text));
            }
            UpdateCheck(includeMultiplayer);
            multiplayer.Toggled += enabled => { includeMultiplayer = enabled; UpdateCheck(enabled); Filter(search.Text); };
            }
            }
            else
            {
                Heading(_text.Get("picker.object.filter.rarity"));
                Choice(_text.Get("common.all"), null, () => selectedRarity = null, () => selectedRarity is null);
                HashSet<string> presentRarities = pool.Select(ObjectRarity).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string rarity in new[] { "Common", "Uncommon", "Rare", "Ancient", "Event" }.Where(presentRarities.Contains))
                    Choice(RarityName(rarity), null, () => selectedRarity = selectedRarity == rarity ? null : rarity,
                        () => selectedRarity == rarity);
            }
        }
        search.TextChanged += Filter; Filter(""); search.GrabFocus();
    }
    public bool ClosePicker()
    {
        if (!Visible) return false;
        Hide();
        foreach (var (button, disabled) in _blocked)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = disabled;
        _blocked.Clear(); ModalChanged?.Invoke(false); return true;
    }
    private static void Clear(Node parent)
    { foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); } }
    private string NameOf(ModelKey key) => _names.Resolve(key, Kind(key));
    private static GameContentKind Kind(ModelKey key) => key.Category switch
    { "CARD" => GameContentKind.Card, "POTION" => GameContentKind.Potion, _ => GameContentKind.Relic };
    private Button Button(Control parent, string text, float x, float y, float width, Action click)
    {
        var button = _p.CompactButton(text, 40, 18);
        button.Position = new(x, y); button.Size = new(width, 40);
        parent.AddChild(button); button.Pressed += click; return button;
    }
    private Label Text(Control parent, string text, float x, float y, float width, int size = 20)
    { var label = _p.Label(text, size); label.Position = new(x, y); label.Size = new(width, 28); label.ClipText = true; parent.AddChild(label); return label; }
    private void Picture(Control parent, ModelKey key, float x, float y, float size)
    {
        GameContentKind kind = Kind(key);
        var texture = _icons.Resolve(key, kind, kind == GameContentKind.Card ? IconVariant.CardPickerLarge : IconVariant.RelicLarge).Texture;
        if (texture is null) return;
        Vector2 dimensions = texture.GetSize();
        parent.AddChild(new Sprite2D { Texture = texture, Position = new(x + size / 2, y + size / 2),
            Scale = Vector2.One * (size / Math.Max(1f, Math.Max(dimensions.X, dimensions.Y))) });
    }
    public override void _ExitTree()
    { ClosePicker(); SearchTooltip.DetachScope(this); }
}
