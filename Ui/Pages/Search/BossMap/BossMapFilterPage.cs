using System.Diagnostics;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.BossMap;

internal sealed partial class BossMapFilterPage : MarginContainer
{
    private readonly IGameIconResolver _icons;
    private readonly VBoxContainer _sectionsHost;
    private readonly Label _hint;
    private readonly Label _catalogNotice;
    private readonly Label _validationError;
    private readonly Button _clear;
    private readonly List<BossMapActSection> _sections = new();
    private readonly Dictionary<(int Act, ModelKey ActKey), BossMapVariantUiState> _history = new();
    private BossMapSearchUiCatalog _catalog = BossMapSearchUiCatalog.Empty(
        RuntimeProfileId.Unsupported,
        "boss-map-search-ui-not-bound");
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private bool _running;

    public BossMapFilterPage(IGameIconResolver icons)
    {
        _icons = icons;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("margin_left", 2);
        AddThemeConstantOverride("margin_top", 2);
        AddThemeConstantOverride("margin_right", 2);
        AddThemeConstantOverride("margin_bottom", 2);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 8);

        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _hint = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _clear = new Button { CustomMinimumSize = new Vector2(112, 34) };
        Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Ghost);
        _clear.Pressed += ClearDraft;
        header.AddChild(_hint);
        header.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
        header.AddChild(_clear);
        root.AddChild(header);

        _validationError = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _validationError.Visible = false;
        root.AddChild(_validationError);

        _catalogNotice = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _catalogNotice.Visible = false;
        root.AddChild(_catalogNotice);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _sectionsHost = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        _sectionsHost.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_sectionsHost);
        root.AddChild(scroll);
        AddChild(root);
    }

    public event Action? Changed;

    public int EnabledConditionCount => _sections.Sum(section => section.EnabledConditionCount);

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        CaptureHistory();
        _text = text;
        _names = names;
        _hint.Text = text.Get(Ui1TextKey.SearchBossMapPageHint);
        _hint.Visible = true;
        _clear.Text = text.Get(Ui1TextKey.SearchBossMapClearConditions);
        _catalogNotice.Text = text.Get(Ui1TextKey.SearchBossMapCatalogPending);
        Rebuild();
    }

    public void BindCatalog(BossMapSearchUiCatalog catalog)
    {
        CaptureHistory();
        _catalog = catalog;
        Rebuild();
    }

    public BossMapSearchDraft BuildDraft()
    {
        CaptureHistory();
        BossMapVariantSearchDraft[] rows = _sections
            .SelectMany(section => section.Rows)
            .Select(row => row.BuildDraft())
            .ToArray();
        bool includeSecond = _catalog.Sections.Any(section => section.Act == 3 && section.ShowSecondBoss);
        return new BossMapSearchDraft(rows, _catalog.Sections.Count > 0, includeSecond);
    }

    public bool TryBuildDraft(out BossMapSearchDraft draft, out string issue, bool focusInvalid)
    {
        draft = BuildDraft();
        issue = string.Empty;
        _validationError.Visible = false;

        if (!draft.IncludeSecondAct3Boss)
        {
            return true;
        }

        BossMapVariantSearchDraft[] act3Rows = draft.Rows.Where(row => row.Act == 3).ToArray();
        BossMapVariantSearchDraft[] considered = act3Rows
            .Where(row => !row.HasSelectableVariant || row.IsVariantActive)
            .ToArray();
        if (considered.Length == 0)
        {
            return true;
        }

        bool hasLegalRoute = considered.Any(HasLegalDoubleBossRoute);
        if (hasLegalRoute)
        {
            return true;
        }

        issue = _text?.Get(Ui1TextKey.SearchBossMapInvalidSameBoss)
            ?? "第一位与第二位首领不能同时只限定为同一首领。";
        _validationError.Text = issue;
        _validationError.Visible = true;
        if (focusInvalid)
        {
            _sections.FirstOrDefault(section => section.Act == 3)?.Rows.FirstOrDefault()?.FocusFirstBoss();
        }
        return false;
    }

    public void RestoreDraft(BossMapSearchDraft draft, bool notify)
    {
        _history.Clear();
        foreach (BossMapVariantSearchDraft row in draft?.Rows ?? Array.Empty<BossMapVariantSearchDraft>())
        {
            if (row.Act is < 1 or > 3 || !row.ActKey.IsValid) continue;
            _history[(row.Act, row.ActKey)] = new BossMapVariantUiState(
                row.Act,
                row.ActKey,
                row.IsVariantActive,
                row.FirstBossAny?.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray() ?? Array.Empty<ModelKey>(),
                row.SecondBossAny?.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray() ?? Array.Empty<ModelKey>());
        }
        _validationError.Visible = false;
        Rebuild();
        RefreshClearState();
        if (notify) Changed?.Invoke();
    }

    public void ClearDraft()
    {
        foreach (BossMapActSection section in _sections)
        {
            section.Clear(notify: false);
        }
        _history.Clear();
        _validationError.Visible = false;
        RefreshClearState();
        Changed?.Invoke();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        foreach (BossMapActSection section in _sections)
        {
            section.SetEnabled(!running);
        }
        RefreshClearState();
    }

    public void SetCompact(bool compact)
    {
        // Rows use the same fixed-height icon matrix at both supported logical sizes.
    }

    private static bool HasLegalDoubleBossRoute(BossMapVariantSearchDraft row)
    {
        IReadOnlyList<ModelKey> first = row.FirstBossAny.Count > 0 ? row.FirstBossAny : row.AllBossKeys;
        IReadOnlyList<ModelKey> second = row.SecondBossAny.Count > 0 ? row.SecondBossAny : row.AllBossKeys;
        return first.Any(firstBoss => second.Any(secondBoss => secondBoss != firstBoss));
    }

    private void Rebuild()
    {
        var stopwatch = Stopwatch.StartNew();
        foreach (Node child in _sectionsHost.GetChildren())
        {
            _sectionsHost.RemoveChild(child);
            child.QueueFree();
        }
        _sections.Clear();
        _validationError.Visible = false;
        if (_text is null || _names is null)
        {
            return;
        }

        string missingIcon = _text.Get(Ui1TextKey.MissingIconTooltip);
        foreach (BossMapActSectionDefinition definition in _catalog.Sections.OrderBy(section => section.Act))
        {
            var section = new BossMapActSection(definition, _icons, _names, _text, missingIcon);
            foreach (BossMapVariantEditor row in section.Rows)
            {
                if (_history.TryGetValue((row.Act, row.ActKey), out BossMapVariantUiState? state) && state is not null)
                {
                    row.RestoreState(state);
                }
            }
            section.Changed += OnSectionChanged;
            section.SetEnabled(!_running);
            _sections.Add(section);
            _sectionsHost.AddChild(section);
        }

        BossMapVariantDefinition[] variants = _catalog.Sections.SelectMany(section => section.Variants).ToArray();
        int pending = variants.Count(variant => !variant.IdentityAvailable || variant.Bosses.Count == 0);
        bool complete = _catalog.Sections.Count > 0 && pending == 0;
        _catalogNotice.Visible = !complete;
        RefreshClearState();
        RuntimeLog.Ui(
            $"Boss/map page built: profile={_catalog.ProfileId}; sections={_sections.Count}; " +
            $"variants={variants.Length}; bosses={variants.Sum(variant => variant.Bosses.Count)}; " +
            $"act3DoubleBoss={_catalog.Sections.Any(section => section.Act == 3 && section.ShowSecondBoss).ToString().ToLowerInvariant()}; " +
            $"took={stopwatch.Elapsed.TotalMilliseconds:0.0}ms");
    }

    private void OnSectionChanged()
    {
        CaptureHistory();
        _validationError.Visible = false;
        RefreshClearState();
        Changed?.Invoke();
    }

    private void CaptureHistory()
    {
        foreach (BossMapActSection section in _sections)
        foreach (BossMapVariantEditor row in section.Rows)
        {
            BossMapVariantUiState state = row.CaptureState();
            _history[(state.Act, state.ActKey)] = state;
        }
    }

    private void RefreshClearState()
    {
        CaptureHistory();
        bool hasAny = _history.Values.Any(state =>
            state.IsVariantActive || state.FirstBosses.Count > 0 || state.SecondBosses.Count > 0);
        _clear.Disabled = _running || !hasAny;
    }
}
