using Godot;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class EventSearchEditor
{
    private const string OpportunityPrefix = EventOpportunityPresentation.Prefix;
    private static readonly EventResultConditionKind[] DollModes =
        [EventResultConditionKind.DollRoomRandomRelic, EventResultConditionKind.DollRoomTakeSomeTimeRelic];
    private static readonly int[] CrystalChoices = [3, 6];
    private static readonly EventRelicRaritySelection[] RaritySelections =
        [EventRelicRaritySelection.First, EventRelicRaritySelection.Second, EventRelicRaritySelection.Third];

    private IEnumerable<EventResultSearchCondition> OpportunitiesFor(ModelKey eventKey) =>
        _draft.OpportunityResults.Where(condition =>
            EventOpportunitySemantics.TryGetEventKey(condition, out var key) && key == eventKey);

    private bool SupportsOpportunity(EventResultConditionKind kind) =>
        EventOpportunitySemantics.SupportsProfile(kind, _runtime.Profile.ProfileId, SemanticProfile);

    private EventResultPrototypeDescriptor? ResultDescriptor(ModelKey eventKey)
    {
        var descriptor = EventResultPrototypeWhitelist.Find(eventKey);
        EventResultConditionKind? opportunityKind = descriptor?.EditorKind switch
        {
            EventResultPrototypeEditorKind.DollRoom => EventResultConditionKind.DollRoomRandomRelic,
            EventResultPrototypeEditorKind.MuseumOfTorture => EventResultConditionKind.MuseumOfTortureOfferedRelic,
            EventResultPrototypeEditorKind.ThermaeNonupeipe => EventResultConditionKind.ThermaeNonupeipeDrinkRelic,
            EventResultPrototypeEditorKind.CrystalSphere => EventResultConditionKind.CrystalSphereRewards,
            EventResultPrototypeEditorKind.RelicRarity => EventResultConditionKind.EventRelicRarityRoll,
            _ => null
        };
        return opportunityKind is { } kind && !SupportsOpportunity(kind) ? null : descriptor;
    }

    private float RenderDollResults(Control host, float x, float y, float width)
    {
        var authored = _draft.OpportunityResults.Where(condition => DollModes.Contains(condition.Kind)).ToArray();
        var mode = _draft.DollMode ?? authored.FirstOrDefault()?.Kind ?? DollModes[0];
        OpportunityButtons(host, x, y, Math.Min(width, 504), DollModes,
            kind => EventOpportunityPresentation.RelicChoice(_text, kind), kind => kind == mode,
            kind =>
            {
                _draft.DollMode = kind;
                if (kind == mode || authored.Select(condition => condition.Kind).Distinct().Count() > 1) return;
                foreach (var condition in authored)
                {
                    int index = _draft.OpportunityResults.IndexOf(condition);
                    _draft.OpportunityResults[index] = condition with { Kind = kind };
                }
            });
        return RenderOpportunityRelics(host, x, y + 48, width, mode, showBranch: false);
    }

    private float RenderOpportunityRelics(Control host, float x, float y, float width, EventResultConditionKind kind,
        bool showBranch = true)
    {
        if (showBranch)
        {
            Text(host, EventOpportunityPresentation.RelicChoice(_text, kind), x, y, width, 16, true);
            y = OpportunityNote(host, "premise." + kind, x, y + 28, width);
        }
        var authored = _draft.OpportunityResults.Where(condition => condition.Kind == kind).ToArray();
        var pool = EventOpportunitySemantics.RelicTargets(kind);
        ModelKey[] choices = pool.Concat(authored.Select(condition => condition.TargetKey)).Distinct().ToArray();
        const float tileWidth = 148, tileHeight = 84, gap = 8;
        int columns = Math.Clamp((int)((width + gap) / (tileWidth + gap)), 1, 5);
        for (int index = 0; index < choices.Length; index++)
        {
            ModelKey key = choices[index];
            string name = _names.Resolve(key, GameContentKind.Relic);
            bool selected = authored.Any(condition => condition.TargetKey == key);
            var tile = Button(host, string.Empty, x + index % columns * (tileWidth + gap), y + index / columns * (tileHeight + gap),
                tileWidth, () =>
                {
                    _draft.OpportunityResults.RemoveAll(condition => condition.Kind == kind &&
                        (!selected || condition.TargetKey == key));
                    if (!selected) _draft.OpportunityResults.Add(new(kind, key));
                    Render();
                }, selected, tileHeight);
            tile.Name = "EventRelicChoice_" + key.Entry;
            tile.AccessibilityName = name;
            AddPicture(tile, key, GameContentKind.Relic, IconVariant.RelicLarge, (tileWidth - 42) / 2, 3, 42);
            var caption = Text(tile, name, 4, 45, tileWidth - 8, 14);
            caption.Size = new(tileWidth - 8, 35);
            caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.MouseFilter = MouseFilterEnum.Ignore;
        }
        return y + (choices.Length + columns - 1) / columns * (tileHeight + gap);
    }

    private float RenderTrialResults(Control host, ModelKey eventKey, float x, float y, float width)
    {
        float bottom = RenderTrialEditor(host, x, y);
        bool retainedRolls = OpportunitiesFor(eventKey).Any();
        if (!SupportsOpportunity(EventResultConditionKind.EventRelicRarityRoll))
            return retainedRolls ? RenderRetainedOpportunities(host, eventKey, x, bottom, width) : bottom;
        if (!retainedRolls && (_draft.TrialCase is 1 or 2 || TransformTakenOver?.Invoke(_activeSeat, "E.TRIAL") == true))
            return bottom;
        return RenderRarityResults(host, eventKey, x, bottom + 8, width);
    }

    private float RenderRarityResults(Control host, ModelKey eventKey, float x, float y, float width)
    {
        Text(host, _text.Get(OpportunityPrefix + "rolls"), x, y, width, 16, true);
        y += 26;
        foreach (var opportunity in EventOpportunityCatalog.RarityBranches.Where(opportunity => opportunity.EventKey == eventKey))
        {
            Text(host, EventOpportunityPresentation.Branch(_text, opportunity.Branch), x, y, width, 16);
            y += 28;
            if (opportunity.RollCount == 1)
            {
                RenderRarityRow(host, opportunity, EventRelicRaritySelection.First, x, y, Math.Min(width, 384));
                y += 46;
                continue;
            }

            foreach (var selection in RaritySelections.Take(opportunity.RollCount)
                .Concat([EventRelicRaritySelection.Any, EventRelicRaritySelection.All]))
            {
                const float labelWidth = 168;
                Text(host, EventOpportunityPresentation.RollSelection(_text, opportunity.Branch, selection),
                    x, y + 6, labelWidth - 8, 14, true);
                RenderRarityRow(host, opportunity, selection, x + labelWidth, y, Math.Min(width - labelWidth, 384));
                y += 42;
            }
            y += 4;
        }
        return y;
    }

    private void RenderRarityRow(Control host, EventRelicRarityOpportunity opportunity, EventRelicRaritySelection selection,
        float x, float y, float width)
    {
        var conditions = OpportunitiesFor(opportunity.EventKey)
            .Where(condition => condition.RelicRarityBranch == opportunity.Branch && condition.RelicRaritySelection == selection)
            .ToArray();
        OpportunityButtons(host, x, y, width, EventOpportunityPresentation.RelicRarities,
            rarity => EventOpportunityPresentation.Rarity(_text, rarity),
            rarity => conditions.Any(condition => condition.RelicRarity == rarity),
            rarity =>
            {
                bool selected = conditions.Any(condition => condition.RelicRarity == rarity);
                foreach (var condition in conditions)
                    if (condition.RelicRarity == rarity || !selected && selection != EventRelicRaritySelection.Any)
                        _draft.OpportunityResults.Remove(condition);
                if (!selected)
                    _draft.OpportunityResults.Add(new(EventResultConditionKind.EventRelicRarityRoll, opportunity.EventKey)
                    { RelicRarityBranch = opportunity.Branch, RelicRaritySelection = selection, RelicRarity = rarity });
            });
    }

    private float RenderRetainedOpportunities(Control host, ModelKey eventKey, float x, float y, float width)
    {
        var conditions = OpportunitiesFor(eventKey).ToArray();
        if (conditions.Length == 0) return y + 32;
        y = OpportunityNote(host, "unavailable", x, y, width);
        return RenderOpportunitySummaries(host, conditions, x, y, width);
    }

    private float RenderOpportunitySummaries(Control host, IReadOnlyList<EventResultSearchCondition> conditions,
        float x, float y, float width)
    {
        foreach (var condition in conditions)
        {
            if (!EventOpportunityPresentation.TryDescribe(_text, _names, condition, out string description)) continue;
            Text(host, description, x, y + 3, width - 42, 14, true);
            Button(host, "×", x + width - 36, y, 36,
                () => { _draft.OpportunityResults.Remove(condition); Render(); }, false, 32);
            y += 38;
        }
        return y;
    }

    private float OpportunityNote(Control host, string key, float x, float y, float width)
    {
        Label label = Text(host, _text.Get(OpportunityPrefix + key), x, y, width, 14, true);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        float height = Math.Max(28, label.GetMinimumSize().Y + 8);
        label.Size = new(width, height);
        return y + height;
    }

    private void OpportunityButtons<T>(Control host, float x, float y, float width, IReadOnlyList<T> values,
        Func<T, string> title, Func<T, bool> selected, Action<T> select)
    {
        const float gap = 8;
        float buttonWidth = (width - gap * (values.Count - 1)) / values.Count;
        foreach (T value in values)
        {
            var button = Button(host, title(value), x, y, buttonWidth, () => { select(value); Render(); }, selected(value), 36);
            button.AddThemeFontSizeOverride("font_size", 16);
            x += buttonWidth + gap;
        }
    }
}
