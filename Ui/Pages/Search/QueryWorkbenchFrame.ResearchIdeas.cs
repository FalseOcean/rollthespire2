using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private const string ResearchIdeasDomain = "ideas";
    private ScrollContainer? _researchIdeasPanel;
    private CheckButton? _fishingRodResearchToggle;
    private VBoxContainer? _fishingRodRows;
    private Button? _addFishingRodDraw;
    private readonly List<SpinBox> _fishingRodInputs = [];
    private readonly List<Button> _removeFishingRodDraw = [];
    private bool _fishingRodDrawResearch;
    private FishingRodDrawRequirement[]? _fishingRodDrawRequirements;

    private IReadOnlyList<FishingRodDrawRequirement> FishingRodRows =>
        _fishingRodDrawRequirements ?? FishingRodDrawResearch.DefaultRequirements;
    private bool HasFishingRodResearchDraft => _fishingRodDrawResearch || _fishingRodDrawRequirements is not null;

    private bool SupportsFishingRodDrawResearch =>
        _runtime.Profile.ProfileId == RuntimeProfileId.Beta111 &&
        RuntimeVersionCompatibility.Resolve(_runtime.Detection.DisplayVersion).SemanticProfileId == RngSemanticProfileId.Modern113;

    private void BuildResearchIdeas()
    {
        _researchIdeasPanel = new ScrollContainer
        {
            Name = "SearchResearchIdeas", Visible = false, FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        PositionDomainEditor(_researchIdeasPanel, research: false);
        AddChild(_researchIdeasPanel);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        _researchIdeasPanel.AddChild(content);

        _fishingRodResearchToggle = new CheckButton
        {
            Name = "FishingRodDrawResearchEnabled",
            Text = _text.Get("query.ideas.fishing_rod.title")
        };
        _fishingRodResearchToggle.AddThemeFontSizeOverride("font_size", 18);
        _fishingRodResearchToggle.Toggled += enabled =>
        {
            _fishingRodDrawResearch = enabled;
            FishingRodResearchChanged();
        };
        content.AddChild(_fishingRodResearchToggle);

        var headings = new HBoxContainer();
        headings.AddThemeConstantOverride("separation", 12);
        AddFishingRodHeading(headings, "query.ideas.fishing_rod.draw_ordinal");
        AddFishingRodHeading(headings, "query.ideas.fishing_rod.pool_size");
        content.AddChild(headings);
        _fishingRodRows = new VBoxContainer();
        _fishingRodRows.AddThemeConstantOverride("separation", 6);
        content.AddChild(_fishingRodRows);
        _addFishingRodDraw = _p.CompactButton(_text.Get("query.ideas.fishing_rod.add"), 32, 15);
        _addFishingRodDraw.Name = "AddFishingRodDraw";
        _addFishingRodDraw.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _addFishingRodDraw.Pressed += () =>
        {
            var last = FishingRodRows.LastOrDefault();
            int ordinal = last is null ? 1 : (int)Math.Min((long)last.DrawOrdinal + 1, int.MaxValue);
            SetFishingRodDraws([.. FishingRodRows, new(ordinal, last?.PoolSize ?? 5)]);
            RenderFishingRodDrawRequirements();
        };
        content.AddChild(_addFishingRodDraw);

        AddResearchIdeasLabel(content, "query.ideas.fishing_rod.premises");
        if (!SupportsFishingRodDrawResearch)
            AddResearchIdeasLabel(content, "query.ideas.fishing_rod.version");
        RenderFishingRodDrawRequirements();
    }

    private const float FishingRodInputWidth = 136;

    private void AddFishingRodHeading(HBoxContainer headings, string key)
    {
        var label = _p.Label(_text.Get(key), 15, secondary: true);
        label.CustomMinimumSize = new(FishingRodInputWidth, 0);
        headings.AddChild(label);
    }

    private void AddResearchIdeasLabel(VBoxContainer content, string key)
    {
        var label = _p.Label(_text.Get(key), 16, secondary: true);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(label);
    }

    private void RenderFishingRodDrawRequirements()
    {
        if (_fishingRodRows is null) return;
        foreach (var child in _fishingRodRows.GetChildren())
        {
            _fishingRodRows.RemoveChild(child);
            child.QueueFree();
        }
        _fishingRodInputs.Clear();
        _removeFishingRodDraw.Clear();
        for (int i = 0; i < FishingRodRows.Count; i++)
        {
            int index = i;
            var requirement = FishingRodRows[index];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            _fishingRodRows.AddChild(row);
            AddFishingRodInput(row, $"FishingRodDrawOrdinal{index}", requirement.DrawOrdinal,
                value => EditFishingRodDraw(index, FishingRodRows[index] with { DrawOrdinal = value }));
            AddFishingRodInput(row, $"FishingRodPoolSize{index}", requirement.PoolSize,
                value => EditFishingRodDraw(index, FishingRodRows[index] with { PoolSize = value }));
            var remove = _p.CompactButton(_text.Get("query.ideas.fishing_rod.remove"), 32, 15);
            remove.Name = $"RemoveFishingRodDraw{index}";
            remove.Pressed += () =>
            {
                SetFishingRodDraws(FishingRodRows.Where((_, position) => position != index).ToArray());
                RenderFishingRodDrawRequirements();
            };
            row.AddChild(remove);
            _removeFishingRodDraw.Add(remove);
        }
        UpdateResearchIdeasToggle();
    }

    private void AddFishingRodInput(HBoxContainer row, string name, int value, Action<int> changed)
    {
        var input = new SpinBox
        {
            Name = name, MinValue = 1, MaxValue = int.MaxValue, Step = 1, Value = value,
            CustomMinimumSize = new(FishingRodInputWidth, 34)
        };
        MainPanelTheme.ApplyFont(input.GetLineEdit());
        input.ValueChanged += next => changed((int)next);
        row.AddChild(input);
        _fishingRodInputs.Add(input);
    }

    private void EditFishingRodDraw(int index, FishingRodDrawRequirement requirement)
    {
        var rows = FishingRodRows.ToArray();
        rows[index] = requirement;
        SetFishingRodDraws(rows);
    }

    private void SetFishingRodDraws(FishingRodDrawRequirement[] rows)
    {
        _fishingRodDrawRequirements = rows.SequenceEqual(FishingRodDrawResearch.DefaultRequirements) ? null : rows;
        FishingRodResearchChanged();
    }

    private void FishingRodResearchChanged()
    {
        UpdateDraftStatus();
        SaveDraft();
    }

    private void UpdateResearchIdeasToggle()
    {
        if (_fishingRodResearchToggle is null) return;
        _fishingRodResearchToggle.SetPressedNoSignal(_fishingRodDrawResearch);
        bool canEdit = CanChangeConditions && SupportsFishingRodDrawResearch;
        _fishingRodResearchToggle.Disabled = !CanChangeConditions ||
            !_fishingRodDrawResearch && (!canEdit || FishingRodRows.Count == 0);
        _addFishingRodDraw!.Disabled = !canEdit;
        foreach (var input in _fishingRodInputs) input.Editable = canEdit;
        foreach (var remove in _removeFishingRodDraw) remove.Disabled = !canEdit || FishingRodRows.Count <= 1;
    }

    private Action? ClearResearchIdeasCondition()
    {
        if (!HasFishingRodResearchDraft) return null;
        bool wasEnabled = _fishingRodDrawResearch;
        var requirements = _fishingRodDrawRequirements;
        _fishingRodDrawResearch = false;
        _fishingRodDrawRequirements = null;
        return () =>
        {
            _fishingRodDrawResearch = wasEnabled;
            _fishingRodDrawRequirements = requirements;
        };
    }

    internal static string FishingRodDrawSummary(IUiTextProvider text, SearchQuery query) =>
        string.Join(" · ", (query.FishingRodDrawRequirements ?? FishingRodDrawResearch.DefaultRequirements)
            .Select(row => text.Format("query.ideas.fishing_rod.draw", row.DrawOrdinal, row.PoolSize)));
}
