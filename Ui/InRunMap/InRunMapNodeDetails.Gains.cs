using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodeDetails
{
    private readonly List<(Control Item, float NaturalWidth)> _gainItems = [];

    private void BuildGainRewards(Node parent, string nodeId, IReadOnlyList<InRunMapDetailSection> gains, long generation, bool compactCards)
    {
        var area = Column(parent, 4); area.Name = "NodeGainRewards";
        area.AddChild(Text(JsonUiTextProvider.CreatePredictorUi13(UiLanguagePreferences.LanguageCode)
            .Get("predictor.map.gains"), 15, true));
        // Each chunk has at most three opportunities. Native flow can wrap
        // earlier for a long localized name without losing a repeated item.
        for (int start = 0; start < gains.Count; start += 3)
        {
            var row = new HFlowContainer { Name = "NodeGainRow_" + start, MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("h_separation", 14); row.AddThemeConstantOverride("v_separation", 6);
            area.AddChild(row);
            foreach (var gain in gains.Skip(start).Take(3))
            {
                var item = Column(row, 2); item.Name = "NodeGainItem_" + _gainItems.Count;
                item.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                if (gain.StableId != null) item.SetMeta("reward_group", gain.StableId);
                float natural = 80;
                foreach (var reward in gain.Objects)
                {
                    string name = reward.DisplayName ?? _names.Resolve(reward.Key, reward.Kind);
                    var identity = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                    identity.AddThemeConstantOverride("separation", 6); item.AddChild(identity);
                    identity.AddChild(new TextureRect { Texture = _icons.Resolve(reward.Key, reward.Kind, IconVariant.Small).Texture,
                        CustomMinimumSize = new(22, 22), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        SizeFlagsVertical = SizeFlags.ShrinkBegin,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
                    var label = Text(name, 15); label.Name = "NodeGainName"; label.MouseFilter = MouseFilterEnum.Ignore;
                    identity.AddChild(label);
                    natural = Math.Max(natural, MainPanelTheme.Regular.GetStringSize(name, fontSize: 15).X + 28);
                }
                string state = gain.Objects.Count == 0 ? string.Join(" · ", new[] { gain.Title, gain.Text }
                    .Where(value => !string.IsNullOrWhiteSpace(value))) : gain.Text;
                if (!string.IsNullOrWhiteSpace(state))
                {
                    item.AddChild(Text(state, 14, true));
                    natural = Math.Max(natural, state.Split('\n').Max(line =>
                        MainPanelTheme.Regular.GetStringSize(line, fontSize: 14).X));
                }
                foreach (var choice in gain.Choices) AddChoice(item, nodeId, choice, generation, editorAction: false, editor: false);
                if (gain.EmbeddedEditor is { } editor)
                {
                    BuildFields(item, editor, generation, editor: false);
                    BuildSections(item, nodeId, editor.Sections, generation, editor: false, compactCards);
                }
                _gainItems.Add((item, natural));
            }
        }
    }

    private void LayoutGainRewards(float width)
    {
        foreach (var (item, natural) in _gainItems)
            item.CustomMinimumSize = new(Math.Min(natural, Math.Max(1, width)), 0);
    }
}
