using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private enum ResearchPage { Neow, Elite, Encounters }

    private const string ResearchDomain = "research";
    private const string ResearchNeowDomain = "research:neow";
    private const string ResearchEliteDomain = "research:elite";
    private const string ResearchEncounterDomain = "research:encounters";
    private const float ResearchEntrySpace = 38;
    private const float ResearchTabsHeight = 48;
    private ResearchPage _researchPage;
    private string _lastRegularDomain = "neow";
    private Button? _researchEntry;
    private HBoxContainer? _researchTabs;
    private ScrollContainer? _savedResearchSummary;
    private readonly List<Button> _researchTabButtons = [];

    private bool HasResearchDraft => _ancientEditor.HasResearchDraft(_multiplayer) || _actInformationEditor.HasResearchDraft;
    private bool ResearchVisible => _persistence.Preferences.ShowDeadSeedResearch || HasResearchDraft;
    private float CategoryHeight => PresetActionsTop - 48 - DomainTop;
    private string ResearchCacheKey => _researchPage switch
    {
        ResearchPage.Elite => ResearchEliteDomain,
        ResearchPage.Encounters => ResearchEncounterDomain,
        _ => ResearchNeowDomain
    };

    private void BuildResearchNavigation()
    {
        _researchEntry = _p.CompactButton("", 32, 15);
        _researchEntry.Hide();
        _researchEntry.Name = "OpenDeadSeedResearch";
        _researchEntry.Pressed += () =>
        {
            if (!ResearchVisible || !CanChangeConditions) return;
            _showResults = false;
            _runtimeSurface?.Hide();
            SelectDomain(ResearchDomain);
            UpdateResultNavigation();
        };
        Place(_researchEntry, ColumnInset, DomainTop + CategoryHeight - 32, RailContentWidth, 32);

        _researchTabs = new HBoxContainer { Name = "DeadSeedResearchTabs", Visible = false };
        _savedResearchSummary = new ScrollContainer
        {
            Name = "SavedDeadSeedResearch", Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _researchTabs.AddThemeConstantOverride("separation", 8);
        Place(_researchTabs, CenterLeft + ColumnInset, WorkspaceLayout.HeaderHeight, CenterWidth - ColumnInset * 2, 36);
        Place(_savedResearchSummary, CenterLeft + ColumnInset, WorkspaceLayout.HeaderHeight,
            CenterWidth - ColumnInset * 2, DockTop - WorkspaceLayout.HeaderHeight - ColumnInset);
        _researchTabButtons.Clear();
        foreach (ResearchPage page in Enum.GetValues<ResearchPage>())
        {
            var tab = _p.CompactButton("", 36, 16);
            tab.Name = "DeadSeedResearch" + page;
            tab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            tab.Pressed += () =>
            {
                if (!CanChangeConditions) return;
                _researchPage = page;
                ApplyDomainSelection(ResearchDomain);
            };
            _researchTabs.AddChild(tab);
            _researchTabButtons.Add(tab);
        }
        UpdateResearchNavigation();
    }

    internal void RefreshResearchVisibility()
    {
        if (_researchEntry is null || !GodotObject.IsInstanceValid(_researchEntry)) return;
        if (_selectedDomain == ResearchDomain && !_showResults) ApplyDomainSelection(ResearchDomain);
        UpdateResearchNavigation();
    }

    private void UpdateResearchNavigation()
    {
        if (_researchEntry is null || !GodotObject.IsInstanceValid(_researchEntry)) return;
        bool visible = ResearchVisible;
        if (!visible && _selectedDomain == ResearchDomain)
        {
            ApplyDomainSelection(_lastRegularDomain);
            return;
        }
        var categoriesSize = new Vector2(RailContentWidth, CategoryHeight - (visible ? ResearchEntrySpace : 0));
        if (_domainScroll is not null && _domainScroll.Size != categoriesSize)
            _domainScroll.Size = categoriesSize;
        _researchEntry.Visible = visible;
        int neow = _ancientEditor.NeowResearchConditionCount(_multiplayer);
        int elite = _actInformationEditor.EliteResearchConditionCount;
        int encounters = _actInformationEditor.EncounterResearchConditionCount;
        int total = neow + elite + encounters;
        bool enabled = _persistence.Preferences.ShowDeadSeedResearch;
        string label = enabled
            ? total > 0 ? _text.Format("query.dead_seed.entry.count", total) : _text.Get("query.dead_seed.title")
            : total > 0 ? _text.Format("query.dead_seed.saved.count", total) : _text.Get("query.dead_seed.saved");
        if (_researchEntry.Text != label) _researchEntry.Text = label;
        if (_selectedDomain == ResearchDomain && _currentDomain is not null)
        {
            string title = _text.Get(enabled ? "query.dead_seed.title" : "query.dead_seed.saved");
            if (_currentDomain.Text != title) _currentDomain.Text = title;
        }
        bool research = _selectedDomain == ResearchDomain && !_showResults;
        _researchTabs!.Visible = research && enabled;
        _savedResearchSummary!.Visible = research && !enabled;
        UpdateResearchTab(ResearchPage.Neow, "query.dead_seed.neow", neow);
        UpdateResearchTab(ResearchPage.Elite, "query.dead_seed.elite", elite);
        UpdateResearchTab(ResearchPage.Encounters, "query.dead_seed.encounters", encounters);
    }

    private void UpdateResearchTab(ResearchPage page, string title, int count)
    {
        var button = _researchTabButtons[(int)page];
        string label = _text.Get(title);
        if (count > 0) label = _text.Format("query.dead_seed.tab.count", label, count);
        if (page == ResearchPage.Neow && _multiplayer)
            label = _text.Format("query.dead_seed.tab.player", label, _seat + 1);
        if (button.Text != label) button.Text = label;
    }

    private void StyleResearchTabs()
    {
        if (_researchEntry is null) return;
        _p.SetActive(_researchEntry, _selectedDomain == ResearchDomain, horizontalPadding: 8);
        for (int i = 0; i < _researchTabButtons.Count; i++)
            _p.SetActive(_researchTabButtons[i], (int)_researchPage == i, horizontalPadding: 8);
    }

    private void PositionDomainEditor(Control editor, bool research)
    {
        float top = WorkspaceLayout.HeaderHeight + (research ? ResearchTabsHeight : 0);
        editor.Position = new(CenterLeft + ColumnInset, top);
        editor.Size = new(CenterWidth - ColumnInset * 2, DockTop - top - ColumnInset);
    }

    private void BuildSavedResearchSummary()
    {
        if (_savedResearchSummary is not { } panel) return;
        foreach (Node old in panel.GetChildren())
        {
            panel.RemoveChild(old);
            old.QueueFree();
        }
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 14);
        panel.AddChild(content);
        var settings = _p.CompactButton(_text.Get("query.dead_seed.settings"), 36, 16);
        settings.Name = "OpenDeadSeedResearchSettings";
        settings.ClipText = false;
        settings.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
        settings.TooltipText = "";
        settings.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        settings.Pressed += () => Navigate?.Invoke("settings:features");
        content.AddChild(settings);

        var names = RuntimeGameContentNameResolver.Create(_language);
        string Names(IEnumerable<ModelKey> keys, GameContentKind kind) => string.Join(" · ", keys.Select(key => names.Resolve(key, kind)));
        string OfferLine(string key, IReadOnlyList<ModelKey> keys) =>
            _text.Get("query.ancient.neow." + key) + " · " + Names(keys, GameContentKind.Relic);
        void Line(string text, bool heading = false)
        {
            var label = _p.Label(text, heading ? 18 : 16, secondary: !heading, heading: heading);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            content.AddChild(label);
        }

        Line(_text.Get("query.dead_seed.saved.help"));
        if (_ancientEditor.HasResearchDraft(_multiplayer))
        {
            Line(_text.Get("query.dead_seed.neow"), heading: true);
            foreach (var (slot, relation, targets, active) in _ancientEditor.NeowResearchSummary(_multiplayer))
            {
                string description = targets.Count == 0
                    ? _text.Get("query.ancient.neow.empty_only")
                    : OfferLine(AncientSearchEditor.NeowRelationKeys[relation], targets);
                string prefix = _multiplayer ? $"P{slot + 1} · " : "";
                Line(prefix + (active ? description : _text.Format("query.dead_seed.inactive_detail", description)));
            }
        }
        if (_actInformationEditor.EliteResearchDraft is { } elite)
        {
            Line(_text.Get("query.dead_seed.elite"), heading: true);
            string description = elite.Mode == ElitePrefixResearchMode.FloorSeven
                ? _text.Get("query.map.elite_research.floor_seven.summary")
                : _text.Format("query.map.elite_research.summary", elite.ExtraMonsters, elite.Unknowns);
            Line(_actInformationEditor.EliteResearchConditionCount > 0 ? description :
                _text.Format("query.dead_seed.inactive_detail", description));
        }
        var queues = _actInformationEditor.EncounterResearchDraft;
        if (queues.Count == 0) return;
        Line(_text.Get("query.dead_seed.encounters"), heading: true);
        foreach (var queue in queues)
        {
            string kind = _text.Get("query.encounter.kind." + queue.Kind);
            string range = _text.Format(queue.RangeMode == SearchSequenceRangeMode.FirstN
                ? "query.dead_seed.encounters.first" : "query.dead_seed.encounters.position", queue.RangeValue);
            string text = _text.Format("query.ancient.act", queue.Act) + " · " + kind + " · " + range;
            if (queue.VariantKey is { } variant) text += " · " + names.Resolve(variant, GameContentKind.Act);
            Line(queue.AllowedKeys.Count == 0 ? _text.Format("query.dead_seed.inactive_detail", text) :
                text + " · " + Names(queue.AllowedKeys, GameContentKind.Encounter));
        }
    }
}
