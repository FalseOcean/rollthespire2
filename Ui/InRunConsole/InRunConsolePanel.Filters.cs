using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Controls.Pickers;

namespace RolltheSpire2.Ui.InRunConsole;

internal sealed partial class InRunConsolePanel
{
    private bool _colorlessOnly;
    private VBoxContainer _actChoices = null!;
    private readonly List<(Button Button, int Index)> _actButtons = [];
    private VBoxContainer _roomChoices = null!;
    private readonly List<(Button Button, int Index)> _roomButtons = [];
    private static ModelKey KeyOf(AbstractModel model) => new(model.Id.Category, model.Id.Entry);

    private void BuildCardContext()
    {
        var player = ConsoleActions.GetLocalPlayer();
        var models = ModelDb.AllCards.Concat(player?.Deck.Cards ?? [])
            .Concat(player?.PlayerCombatState?.Hand.Cards ?? []).DistinctBy(card => card.Id).ToArray();
        var characters = ModelDb.AllCharacters.Select(character => (Key: KeyOf(character),
            Cards: character.CardPool.AllCards.Concat(character.StartingDeck).Select(card => card.Id).ToHashSet())).ToArray();
        var metadata = models.ToDictionary(KeyOf, card => new CardPickerCandidateMetadata(KeyOf(card),
            characters.Where(character => character.Cards.Contains(card.Id)).Select(character => character.Key).ToArray(),
            Enum.TryParse<EffectCardRarity>(card.Rarity.ToString(), out var rarity) ? rarity : EffectCardRarity.Special,
            Enum.TryParse<EffectCardType>(card.Type.ToString(), out var type) ? type : EffectCardType.Other)
            { IsColorlessPoolMember = card.Pool.IsColorless }, ModelKeyComparer.Instance);
        _cardContext = new(models.Select(KeyOf).ToArray(), metadata, true, "live-console");
    }

    private void BuildCardFilterButtons()
    {
        _cardFilterRail.AddChild(_p.Label(T("console.character.pool"), 17, true));
        CardFilterChoice(T("console.all"), null, "ConsoleFilterCharacter_All",
            () => { _colorlessOnly = false; _cardFilters.SelectAllCharacters(); }, () => _cardFilters.AllCharacters && !_colorlessOnly);
        foreach (var character in _cardContext.AvailableCharacterKeys)
            CardFilterChoice(_names.Resolve(character, GameContentKind.Character), _characterIcons.Resolve(character).Texture,
                "ConsoleFilterCharacter_" + character.Entry,
                () => { _colorlessOnly = false; _cardFilters.ToggleCharacter(character); }, () => !_cardFilters.AllCharacters && _cardFilters.Characters.Contains(character));
        CardFilterChoice(T("console.colorless"), _characterIcons.ResolveColorlessPoolVisual(), "ConsoleFilterCharacter_Colorless",
            () => { _colorlessOnly = !_colorlessOnly; _cardFilters.SelectAllCharacters(); }, () => _colorlessOnly);
        _cardFilterRail.AddChild(_p.Label(T("console.rarity"), 17, true));
        CardFilterChoice(T("console.all"), null, "ConsoleFilterRarity_All", () => _cardFilters.Rarities.Clear(), () => _cardFilters.Rarities.Count == 0);
        foreach (var rarity in _cardContext.CandidateMetadata.Values.Select(item => item.Rarity).Distinct().Order())
            CardFilterChoice(FilterName(rarity.ToString()), _filterIcons.ResolveRarity(rarity), "ConsoleFilterRarity_" + rarity,
                () => _cardFilters.ToggleRarity(rarity), () => _cardFilters.Rarities.Contains(rarity));
        _cardFilterRail.AddChild(_p.Label(T("console.type"), 17, true));
        CardFilterChoice(T("console.all"), null, "ConsoleFilterType_All", () => _cardFilters.Types.Clear(), () => _cardFilters.Types.Count == 0);
        foreach (var type in _cardContext.CandidateMetadata.Values.Select(item => item.CardType).Distinct().Order())
            CardFilterChoice(FilterName(type.ToString()), _filterIcons.ResolveType(type), "ConsoleFilterType_" + type,
                () => _cardFilters.ToggleType(type), () => _cardFilters.Types.Contains(type));
    }

    private void CardFilterChoice(string title, Texture2D? icon, string name, Action change, Func<bool> active)
    {
        var button = Button(_cardFilterRail, "", () =>
        {
            change(); _page = 0; RefreshFilterButtons(); RenderGrid(true);
        }, name);
        button.CustomMinimumSize = new(0, 34);
        if (icon is not null) button.AddChild(new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = icon, Position = new(6, 5), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore, Size = new(24, 24) });
        var label = _p.Label(title, 16); label.Position = new(icon is null ? 8 : 36, 2);
        label.VerticalAlignment = VerticalAlignment.Center; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.Size = new(icon is null ? 184 : 156, 30); button.AddChild(label);
        _filterButtons.Add((button, active));
    }
    private void RefreshFilterButtons()
    {
        foreach (var choice in _actButtons)
            _p.SetModeActive(choice.Button, choice.Index == _actFilter.Selected);
        foreach (var choice in _roomButtons)
            _p.SetModeActive(choice.Button, choice.Index == _roomFilter.Selected);
        foreach (var choice in _filterButtons) _p.SetModeActive(choice.Button, choice.Active());
        foreach (var choice in _rarityButtons)
        {
            choice.Button.SetPressedNoSignal(choice.Rarity == _selectedRarity);
            _p.SetModeActive(choice.Button, choice.Rarity == _selectedRarity);
        }
    }

    private void RarityChoice(string title, string? rarity)
    {
        var button = Button(_rarityFilter, title, () =>
        {
            _selectedRarity = rarity; _page = 0; RefreshFilterButtons(); RenderGrid(true);
        }, "ConsoleRarity_" + (rarity ?? "All"));
        button.ToggleMode = true;
        button.Alignment = HorizontalAlignment.Left;
        button.CustomMinimumSize = new(0, 34);
        button.AddThemeFontSizeOverride("font_size", 16);
        // Toggle buttons use hover_pressed while the pointer remains over the
        // selected item. Its inherited padding must match every other state.
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            var style = (StyleBox)button.GetThemeStylebox(state).Duplicate();
            style.ContentMarginTop = style.ContentMarginBottom = 4;
            button.AddThemeStyleboxOverride(state, style);
        }
        _rarityButtons.Add((button, rarity));
    }

    private void PrepareCategoryFilters()
    {
        _cardFilters.SelectAllCharacters(); _cardFilters.Rarities.Clear(); _cardFilters.Types.Clear(); _colorlessOnly = false;
        _selectedRarity = null; _rarityButtons.Clear();
        foreach (Node child in _rarityFilter.GetChildren()) { _rarityFilter.RemoveChild(child); child.QueueFree(); }
        _rarityFilter.AddChild(_p.Label(T("console.rarity"), 17, true));
        RarityChoice(T("console.all"), null);
        IEnumerable<string> rarities = _tab switch
        { 1 => ModelDb.AllRelics.Select(model => model.Rarity.ToString()), 2 => ModelDb.AllPotions.Select(model => model.Rarity.ToString()), _ => [] };
        foreach (string rarity in rarities.Distinct().OrderBy(RarityDisplayOrder).ThenBy(value => value, StringComparer.Ordinal))
        RarityChoice(FilterName(rarity), rarity);
        _actFilter.Clear(); _acts.Clear(); _actFilter.AddItem(T("console.all.act.pools")); _acts.Add(null);
        int index = 0;
        foreach (var act in ModelDb.Acts)
        { _actFilter.AddItem(_names.Resolve(KeyOf(act), GameContentKind.Act)); _acts.Add(index++); }
        _actFilter.AddItem(T("console.shared.outside.act.pools")); _acts.Add(-1);
        BuildFilterChoices(_actChoices, _actFilter, _actButtons, "ConsoleAct_");
        _roomFilter.Clear(); _roomTypes.Clear(); _roomFilter.AddItem(T("console.all.fight.types")); _roomTypes.Add(null);
        foreach (string room in ModelDb.AllEncounters.Select(encounter => encounter.RoomType.ToString()).Distinct()
            .OrderBy(room => room switch { "Monster" => 0, "Elite" => 1, "Boss" => 2, _ => 3 }).ThenBy(room => room, StringComparer.Ordinal))
        { _roomFilter.AddItem(FilterName(room)); _roomTypes.Add(room); }
        BuildFilterChoices(_roomChoices, _roomFilter, _roomButtons, "ConsoleFightType_");
        RefreshFilterButtons();
    }

    private void BuildFilterChoices(VBoxContainer container, OptionButton selection,
        List<(Button Button, int Index)> buttons, string prefix)
    {
        buttons.Clear();
        foreach (var child in container.GetChildren()) { container.RemoveChild(child); child.QueueFree(); }
        for (int i = 0; i < selection.ItemCount; i++)
        {
            int index = i;
            var button = Button(container, selection.GetItemText(i), () =>
            {
                selection.Select(index); _page = 0; RefreshFilterButtons(); RenderGrid(true);
            }, prefix + i);
            button.Alignment = HorizontalAlignment.Left;
            button.ClipText = true;
            button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            button.AddThemeFontSizeOverride("font_size", 16);
            buttons.Add((button, i));
        }
    }

    private void ClearFilters()
    {
        _cardFilters.SelectAllCharacters(); _cardFilters.Rarities.Clear(); _cardFilters.Types.Clear(); _colorlessOnly = false;
        _selectedRarity = null; _actFilter.Select(0); _roomFilter.Select(0); _page = 0;
        RefreshFilterButtons(); RenderGrid(true);
    }

    private bool MatchesQuickFilters(AbstractModel model)
    {
        if (model is CardModel)
            return _cardFilters.Matches(_cardContext, KeyOf(model)) && (!_colorlessOnly || _cardContext.IsColorlessPoolMember(KeyOf(model)));
        if (model is RelicModel relic && _selectedRarity is { } relicRarity && relic.Rarity.ToString() != relicRarity) return false;
        if (model is PotionModel potion && _selectedRarity is { } potionRarity && potion.Rarity.ToString() != potionRarity) return false;
        if (model is EncounterModel encounter && _roomTypes.ElementAtOrDefault(_roomFilter.Selected) is { } room && encounter.RoomType.ToString() != room) return false;
        if (_tab is not (3 or 4 or 5) || _acts.ElementAtOrDefault(_actFilter.Selected) is not { } actIndex) return true;
        var acts = ModelDb.Acts.ToArray();
        if (actIndex < 0) return !acts.Any(act => ActPool(act).Any(candidate => candidate.Id == model.Id));
        return ActPool(acts[actIndex]).Any(candidate => candidate.Id == model.Id);
    }

    private IEnumerable<AbstractModel> ActPool(ActModel act) => _tab switch
    {
        3 => act.AllAncients.Cast<AbstractModel>(), 5 => act.AllEvents.Cast<AbstractModel>(), _ => act.AllEncounters.Cast<AbstractModel>()
    };

    // Native NRelicCollection / NPotionLab section order; non-encyclopedia values follow.
    private static int RarityDisplayOrder(string rarity) => rarity switch
    {
        "Starter" => 0, "Common" => 1, "Uncommon" => 2, "Rare" => 3,
        "Shop" => 4, "Ancient" => 5, "Event" => 6, "Token" => 7,
        "None" => 8, _ => 9
    };

    private string FilterName(string value) => value switch
    {
        "Basic" => T("console.basic"), "Common" => T("console.common"), "Uncommon" => T("console.uncommon"), "Rare" => T("console.rare"),
        "Ancient" => T("console.ancient"), "Event" => T("console.event"), "Shop" => T("console.shop"), "Starter" => T("console.starter"),
        "Attack" => T("console.attack"), "Skill" => T("console.skill"), "Power" => T("console.power"), "Curse" => T("console.curse"),
        "Status" => T("console.status"), "Monster" => T("console.normal"), "Elite" => T("console.elite"), "Boss" => T("console.boss"),
        "Special" => T("console.special"), "Other" => T("console.other"),
        "None" => T("console.none"), "Token" => T("console.token"), _ => value
    };

    public override void _ExitTree()
    {
        CaptureConsoleState();
        _characterIcons?.Dispose();
    }
}
