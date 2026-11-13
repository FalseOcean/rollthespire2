using Godot;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class ActInformationSearchEditor
{
    private float RenderEliteResearch(Control content, float width, float top)
    {
        bool legacy = _draft.EliteResearchCounts.Mode == ElitePrefixResearchMode.FixedCounts;
        var enabled = new CheckButton
        {
            Name = "ElitePrefixResearchEnabled",
            Text = _text.Get(legacy ? "query.map.elite_research.enable" : "query.map.elite_research.floor_seven.enable"),
            Position = new(0, top), Size = new(width, 36),
            ButtonPressed = _draft.EliteResearchEnabled
        };
        enabled.AddThemeFontSizeOverride("font_size", 17);
        enabled.Toggled += value =>
        {
            _draft.EliteResearchEnabled = value;
            Render();
        };
        content.AddChild(enabled);
        top += 40;
        if (!_draft.EliteResearchEnabled) return top + 8;

        if (!legacy)
            return top + MapHint(content, _text.Get("query.map.elite_research.floor_seven.hint"), 0, top, width) + 18;

        top += MapHint(content, _text.Get("query.map.elite_research.hint"), 0, top, width) + 10;
        CountRow("query.map.elite_research.monsters", "ElitePrefixExtraMonsters", _draft.EliteResearchCounts.ExtraMonsters,
            value => _draft.EliteResearchCounts = _draft.EliteResearchCounts with { ExtraMonsters = value });
        CountRow("query.map.elite_research.unknowns", "ElitePrefixUnknowns", _draft.EliteResearchCounts.Unknowns,
            value => _draft.EliteResearchCounts = _draft.EliteResearchCounts with { Unknowns = value });
        var replace = new Button
        {
            Name = "ElitePrefixUseFloorSeven",
            Text = _text.Get("query.map.elite_research.floor_seven.replace"),
            Position = new(0, top), Size = new(width, 36)
        };
        replace.AddThemeFontSizeOverride("font_size", 15);
        replace.Pressed += () =>
        {
            _draft.EliteResearchCounts = ElitePrefixResearchCondition.FloorSeven;
            Render();
        };
        content.AddChild(replace);
        top += 42;
        return top + 8;

        void CountRow(string label, string name, int value, Action<int> changed)
        {
            var input = Number(content, name, value, 0, top, 0, true, changed);
            float inputWidth = input.GetCombinedMinimumSize().X;
            input.Position = new(width - inputWidth, top);
            Text(content, _text.Get(label), 4, top + 6, Math.Max(1, width - inputWidth - 12), 15);
            top += 46;
        }
    }
}
