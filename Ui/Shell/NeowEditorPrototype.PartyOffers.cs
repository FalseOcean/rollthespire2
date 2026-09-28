using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class NeowEditorPrototype
{
    internal static string PartyUnsupportedText(ModelKey option, bool zh) => Core.Effects.PartyNeowAdmission.UnsupportedReason(option) switch
    {
        "Party.N.DeferredToT" => zh ? "变牌结果归 T；本轮未接入多人变牌或其领取前提。" : "Transformation belongs to T; multiplayer transform results/premises are not supported yet.",
        "Party.N.NestedTransactionAndBagPropagationUnclosed" => zh ? "内部遗物的完整领取效果与多人遗物池变化尚未闭合。" : "Nested pickup effects and multiplayer relic-bag changes are not fully modeled.",
        "Party.N.MultiplayerOnlyCombinedPoolNotCaptured" => zh ? "尚未接入该选项的多人专属混合卡池。" : "This option's multiplayer-only combined card pool is not captured yet.",
        "Party.N.ConcreteDeckChoiceOrLaterParticipantHooksUnclosed" => zh ? "缺少具体牌组选择，或尚未验证它对后续参与玩家的影响。" : "Concrete deck choices or effects on later participants are not modeled yet.",
        _ => ""
    };
    private readonly Dictionary<int, HashSet<ModelKey>> _partyOffers = [];
    private readonly Dictionary<int, int> _partyModes = [];
    private readonly Dictionary<int, IReadOnlyList<Core.Effects.PartyNeowChoice>> _partyChoices = [];
    private int PartyMode => _partyModes.GetValueOrDefault(_activeSeat);
    internal Search.Semantics.PlayerOfferQuery ExportPartyQuery(int slot)
    {
        if (_partyModes.GetValueOrDefault(slot) == 0) return new(slot, ExportPartyOffers(slot));
        var old = _draft;
        try
        {
            _draft = _partyCatalogDrafts[slot];
            var q = ExportQuery();
            int mode = _partyModes.GetValueOrDefault(slot);
            return new(slot, ExportPartyOffers(slot))
            {
                SelectedOption = mode == 0 || q.OpeningRoute is null ? null : new(q.OpeningRoute.RouteRelicKey,
                    mode == 2 ? Core.Effects.PartyNeowIntent.PremiseOnly : Core.Effects.PartyNeowIntent.ResultTarget,
                    _partyChoices.GetValueOrDefault(slot, [])),
                Results = mode == 1 ? q.StructuredOpeningEffects : []
            };
        }
        finally { _draft = old; }
    }
    internal void ImportPartyQuery(Search.Semantics.PlayerOfferQuery player)
    {
        var old = _draft;
        try
        {
            _draft = _partyCatalogDrafts[player.Slot];
            ImportQuery(Search.Semantics.SearchQuery.Empty with { OpeningRoute = player.SelectedOption is { } plan ? new(plan.Option) : null, StructuredOpeningEffects = player.Results });
            _partyModes[player.Slot] = player.SelectedOption is null ? 0 : player.SelectedOption.Intent == Core.Effects.PartyNeowIntent.PremiseOnly ? 2 : 1;
            _partyChoices[player.Slot] = player.SelectedOption?.Choices ?? [];
        }
        finally { _draft = old; }
    }
    internal ModelKeySetFilter ExportPartyOffers(int slot) => new([], _partyOffers.GetValueOrDefault(slot, []).OrderBy(k => k.Serialized).ToArray(), []);
    internal void ImportPartyOffers(int slot, ModelKeySetFilter condition)
    {
        if (condition.Any.Count != 0 || condition.Ban.Count != 0) throw new InvalidOperationException("Party.OfferEditorSupportsContainsAll");
        _partyOffers[slot] = condition.All.ToHashSet();
    }
    private void RenderPartyOffers()
    {
        Clear(_body); _body.Size = Size;
        var header = new VBoxContainer { Size = new(Size.X - 20, 0) };
        _body.AddChild(header);
        var modes = new HBoxContainer(); header.AddChild(modes);
        string[] labels = _language == "zh" ? new[] { "初始选项", "选择并筛选结果", "仅作为领取前提" } : new[] { "Initial offers", "Selected result", "Premise only" };
        for (int i = 0; i < labels.Length; i++)
        {
            int mode = i;
            var b = new Godot.Button { Text = labels[i], ToggleMode = true, ButtonPressed = PartyMode == i };
            b.Pressed += () => { _partyModes[_activeSeat] = mode; RenderPartyOffers(); };
            modes.AddChild(b);
        }
        header.AddChild(_p.Label(_language == "zh" ? "参与玩家按 P1 → Pn 完整领取；其他玩家等待。" : "Participants complete pickups in slot order; others wait.", 16));
        if (PartyMode > 0)
        {
            if (_source is { } selectedOption)
            foreach (var domain in Core.Effects.PartyNeowAdmission.ChoiceDomains(selectedOption))
            {
                var picker = new OptionButton(); header.AddChild(picker);
                picker.AddItem(_language == "zh" ? "自动寻找共同选择" : "Find a joint choice", 0);
                if (domain.Skip) picker.AddItem(_language == "zh" ? "跳过" : "Skip", 1);
                for (int i = 0; i < domain.Count; i++) picker.AddItem((_language == "zh" ? "领取第 " : "Take #") + (i + 1), i + 2);
                var choice = _partyChoices.GetValueOrDefault(_activeSeat, []).SingleOrDefault(c => c.GroupId == domain.Id);
                picker.Select(picker.GetItemIndex(choice is null ? 0 : choice.Index + 2));
                picker.TooltipText = domain.Id;
                picker.ItemSelected += index =>
                {
                    var choices = _partyChoices.GetValueOrDefault(_activeSeat, []).Where(c => c.GroupId != domain.Id).ToList();
                    int id = picker.GetItemId((int)index);
                    if (id != 0) choices.Add(new(domain.Id, id - 2));
                    _partyChoices[_activeSeat] = choices;
                };
            }
            Source(_body, Size.X - 20, header);
            return;
        }

        Text(_body, _language == "zh" ? $"P{_activeSeat + 1} · 初始 Neow 选项包含" : $"P{_activeSeat + 1} · Initial Neow offers contain", 0, 80, Size.X, 22, true);
        Text(_body, _language == "zh" ? "仅观察领取前选项；不选择或领取遗物。" : "Before pickup; no choice or acquisition is performed.", 0, 116, Size.X, 17);
        if (_catalog is null) { Text(_body, _problem, 0, 160, Size.X, 17); return; }
        if (!_partyOffers.TryGetValue(_activeSeat, out var selected)) _partyOffers[_activeSeat] = selected = [];
        var keys = _catalog.RouteRelics.Distinct().ToArray();
        for (int i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            var button = Button(_body, NameOf(key), (i % 3) * 224, 160 + (i / 3) * 48, 216,
                () => { if (!selected.Add(key)) selected.Remove(key); RenderPartyOffers(); }, selected.Contains(key), 42);
            button.Disabled = !IdentityAllowed(key);
        }
    }
}
