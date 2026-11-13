using RolltheSpire2.Presentation.ContentNames;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.BossMap;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class ActInformationSearchEditor
{
    private float RenderEncounterResearch(Control content, float width)
    {
        var hint = Text(content, _text.Get("query.dead_seed.encounters.help"), 16, 0, width - 32, 15, true);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.Size = new(width - 32, hint.GetMinimumSize().Y);
        float y = hint.Size.Y + 16;
        for (int act = 1; act <= 3; act++)
        {
            Text(content, _text.Format("query.ancient.act", act), 16, y, width - 32, 20);
            y = RenderEncounterQueues(content, width, y + 32, act) + 16;
        }
        return y;
    }

    private float RenderEncounterQueues(Control content, float width, float y, int act)
    {
        for (int index = 0; index < 3; index++)
        {
            var kind = (EncounterQueueKind)index;
            Button(content, "+ " + _text.Get("query.encounter.kind." + kind), 16 + index * 132, y, 124,
                () => { _draft.EncounterQueues.Add(new(act, kind, SearchSequenceRangeMode.FirstN, 1, [])); Render(); }, false, 34)
                .Name = $"AddEncounterQueue{act}_{kind}";
        }
        y += 44;
        var rows = _draft.EncounterQueues.Where(row => row.Act == act).ToArray();
        foreach (var row in rows)
        {
            int rowIndex = _draft.EncounterQueues.IndexOf(row);
            void Set(EncounterQueueSearchCondition updated) { _draft.EncounterQueues[rowIndex] = updated; Render(); }
            Text(content, _text.Get("query.encounter.kind." + row.Kind), 16, y + 5, 104, 16);
            Options(content, [_text.Get("query.encounter.first_n"), _text.Get("query.encounter.exact_slot")],
                (int)row.RangeMode, 128, y, 190, value => Set(row with { RangeMode = (SearchSequenceRangeMode)value }));
            var count = new SpinBox { Position = new(326, y), Size = new(84, 38), MinValue = 1, MaxValue = int.MaxValue,
                Step = 1, Value = row.RangeValue, Rounded = true };
            count.ValueChanged += value => Set(row with { RangeValue = (int)value });
            content.AddChild(count);
            Button(content, _text.Get("query.encounter.remove"), Math.Max(420, width - 102), y, 86,
                () => { _draft.EncounterQueues.RemoveAt(rowIndex); Render(); }, false, 34);
            y += 44;
            var variants = Section(act)?.Variants.ToArray() ?? [];
            var variantKeys = variants.Select(v => v.ActKey).Concat(row.VariantKey is { } pinned ? [pinned] : [])
                .Distinct(ModelKeyComparer.Instance).ToArray();
            var variantTitles = new[] { _text.Get("query.encounter.all_variants") }
                .Concat(variantKeys.Select(key => _names.Resolve(key, GameContentKind.Act))).ToArray();
            Options(content, variantTitles, row.VariantKey is { } key ? Array.IndexOf(variantKeys, key) + 1 : 0,
                128, y, Math.Max(220, width - 144), value => Set(row with { VariantKey = value == 0 ? null : variantKeys[value - 1] }));
            y += 44;
            var keys = variants.Where(v => row.VariantKey is null || v.ActKey == row.VariantKey)
                .SelectMany(v => v.EncounterPools.GetValueOrDefault(row.Kind) ?? []).Concat(row.AllowedKeys)
                .Distinct(ModelKeyComparer.Instance).ToArray();
            const float tileWidth = 144, tileGap = 8;
            int columns = Math.Max(1, (int)((width - 144 + tileGap) / (tileWidth + tileGap)));
            var candidates = keys.Select(member =>
            {
                string caption = _names.Resolve(member, GameContentKind.Encounter);
                var texture = _icons.Resolve(member, GameContentKind.Encounter, IconVariant.WorldCompendiumBossIcon).Texture;
                float captionHeight = Mathf.Ceil(MainPanelTheme.Regular.GetMultilineStringSize(caption,
                    HorizontalAlignment.Center, tileWidth - 16, 15, -1,
                    TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive).Y) + 4;
                return (Member: member, Caption: caption, Texture: texture, CaptionHeight: captionHeight);
            }).ToArray();
            float tileHeight = candidates.Length == 0 ? 76 : Math.Max(76,
                candidates.Max(candidate => candidate.CaptionHeight + (candidate.Texture is null ? 16 : 48)));
            if (keys.Length == 0) { Text(content, _text.Get("query.encounter.catalog_unavailable"), 128, y, width - 144, 14, true); y += 34; }
            for (int index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                ModelKey member = candidate.Member;
                EncounterIdentityButton(content, candidate.Caption, candidate.Texture,
                    128 + index % columns * (tileWidth + tileGap), y + index / columns * (tileHeight + tileGap), tileWidth, tileHeight,
                    row.AllowedKeys.Contains(member), () => Set(row with { AllowedKeys = row.AllowedKeys.Contains(member)
                        ? row.AllowedKeys.Where(k => k != member).ToArray() : row.AllowedKeys.Append(member).ToArray() }))
                    .Name = $"EncounterQueueTarget{rowIndex}_{index}";
            }
            y += ((keys.Length + columns - 1) / columns) * (tileHeight + tileGap) + 8;
            AddLine(content, 16, y, width - 32); y += 12;
        }
        return y;
    }
    private Button EncounterIdentityButton(Control parent, string caption, Texture2D? texture,
        float x, float y, float width, float height, bool selected, Action action)
    {
        Button button = Button(parent, string.Empty, x, y, width, action, selected, height);
        _p.ApplyCandidateSurface(button, selected);
        // Ordinary encounters often have no compendium icon. Give their names
        // the entire tile, and size rows to retain the complete localized caption.
        float nameTop = texture is null ? 8 : 40;
        Label name = Text(button, caption, 8, nameTop, width - 16, 15);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.MaxLinesVisible = -1;
        name.ClipText = false;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.MouseFilter = MouseFilterEnum.Ignore;
        name.Size = new(width - 16, name.Size.Y);
        _ = name.GetMinimumSize();
        name.Size = new(width - 16, height - nameTop - 8);
        if (texture is null) return button;
        button.AddChild(new TextureRect
        {
            Texture = texture, Position = new((width - 30) / 2, 6), Size = new(30, 30),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        });
        return button;
    }

}
