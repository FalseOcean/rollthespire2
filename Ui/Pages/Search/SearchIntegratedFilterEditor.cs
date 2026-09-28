using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed record IntegratedSearchDraft(
    string BonesAcquisitionOrder,
    string EffectOutputSource,
    string EffectOutputAny,
    string EffectOutputAll,
    string EffectOutputBan,
    string BossAll,
    int BossOrdinal,
    string BossOrdinalAny,
    string BossOrdinalAll,
    string BossOrdinalBan,
    string AncientAll,
    string AncientOptionAll,
    string SeaGlassTargetAny,
    string SeaGlassTargetAll,
    string SeaGlassTargetBan,
    string RelicSequenceConditions,
    string EventSequenceConditions);

/// <summary>
/// Minimum integrated editor for restored Search domains. It captures only typed
/// ModelKey filters and sequence/range syntax; it deliberately contains no player
/// choice target controls.
/// </summary>
internal sealed partial class SearchIntegratedFilterEditor : PanelContainer
{
    private readonly Label _title;
    private readonly Label _hint;
    private readonly Label _summary;
    private readonly Button _clear;
    private readonly LineEdit _bonesOrder;
    private readonly LineEdit _effectSource;
    private readonly LineEdit _effectAny;
    private readonly LineEdit _effectAll;
    private readonly LineEdit _effectBan;
    private readonly LineEdit _bossAll;
    private readonly SpinBox _bossOrdinal;
    private readonly LineEdit _bossOrdinalAny;
    private readonly LineEdit _bossOrdinalAll;
    private readonly LineEdit _bossOrdinalBan;
    private readonly LineEdit _ancientAll;
    private readonly LineEdit _ancientOptionAll;
    private readonly LineEdit _seaGlassAny;
    private readonly LineEdit _seaGlassAll;
    private readonly LineEdit _seaGlassBan;
    private readonly LineEdit _relicSequence;
    private readonly LineEdit _eventSequence;
    private readonly Label[] _labels;
    private int _baseConditionCount;
    private IUiTextProvider? _uiText;

    public SearchIntegratedFilterEditor()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, 12f);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 9);

        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _clear = new Button { CustomMinimumSize = new Vector2(92, 34) };
        Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Ghost);
        header.AddChild(_title);
        header.AddChild(_clear);
        column.AddChild(header);
        _hint = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _summary = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        column.AddChild(_hint);
        column.AddChild(_summary);

        _bonesOrder = Field(260);
        _effectSource = Field(240);
        _effectAny = Field(260);
        _effectAll = Field(260);
        _effectBan = Field(260);
        Label bonesOrderLabel = Label();
        Label effectSourceLabel = Label();
        Label anyLabel1 = Label();
        Label allLabel1 = Label();
        Label banLabel1 = Label();
        column.AddChild(Row(
            Labeled(bonesOrderLabel, _bonesOrder),
            Labeled(effectSourceLabel, _effectSource)));
        column.AddChild(Row(
            Labeled(anyLabel1, _effectAny),
            Labeled(allLabel1, _effectAll),
            Labeled(banLabel1, _effectBan)));

        _bossAll = Field(260);
        _bossOrdinal = Number(0, 2, 0, 92);
        _bossOrdinalAny = Field(250);
        _bossOrdinalAll = Field(250);
        _bossOrdinalBan = Field(250);
        Label bossAllLabel = Label();
        Label bossOrdinalLabel = Label();
        Label bossOrdinalAnyLabel = Label();
        Label bossOrdinalAllLabel = Label();
        Label bossOrdinalBanLabel = Label();
        column.AddChild(Row(
            Labeled(bossAllLabel, _bossAll),
            Labeled(bossOrdinalLabel, _bossOrdinal),
            Labeled(bossOrdinalAnyLabel, _bossOrdinalAny)));
        column.AddChild(Row(
            Labeled(bossOrdinalAllLabel, _bossOrdinalAll),
            Labeled(bossOrdinalBanLabel, _bossOrdinalBan)));

        _ancientAll = Field(260);
        _ancientOptionAll = Field(280);
        _seaGlassAny = Field(230);
        _seaGlassAll = Field(230);
        _seaGlassBan = Field(230);
        Label ancientAllLabel = Label();
        Label optionAllLabel = Label();
        Label seaAnyLabel = Label();
        Label seaAllLabel = Label();
        Label seaBanLabel = Label();
        column.AddChild(Row(
            Labeled(ancientAllLabel, _ancientAll),
            Labeled(optionAllLabel, _ancientOptionAll)));
        column.AddChild(Row(
            Labeled(seaAnyLabel, _seaGlassAny),
            Labeled(seaAllLabel, _seaGlassAll),
            Labeled(seaBanLabel, _seaGlassBan)));

        _relicSequence = Field(720);
        _eventSequence = Field(720);
        Label relicSequenceLabel = Label();
        Label eventSequenceLabel = Label();
        column.AddChild(Labeled(relicSequenceLabel, _relicSequence));
        column.AddChild(Labeled(eventSequenceLabel, _eventSequence));

        _labels = new[]
        {
            bonesOrderLabel, effectSourceLabel, anyLabel1, allLabel1, banLabel1,
            bossAllLabel, bossOrdinalLabel, bossOrdinalAnyLabel, bossOrdinalAllLabel, bossOrdinalBanLabel,
            ancientAllLabel, optionAllLabel, seaAnyLabel, seaAllLabel, seaBanLabel,
            relicSequenceLabel, eventSequenceLabel
        };

        foreach (LineEdit field in TextFields())
        {
            field.TextChanged += _ => OnChanged();
        }
        _bossOrdinal.ValueChanged += _ => OnChanged();
        _clear.Pressed += () => ClearRequested?.Invoke();
        AddChild(column);
        UpdateSummary();
    }

    public event Action? Changed;
    public event Action? ClearRequested;

    public int EnabledConditionCount => _baseConditionCount + ExtendedConditionCount();

    public IntegratedSearchDraft CurrentDraft => new(
        _bonesOrder.Text,
        _effectSource.Text,
        _effectAny.Text,
        _effectAll.Text,
        _effectBan.Text,
        _bossAll.Text,
        (int)_bossOrdinal.Value,
        _bossOrdinalAny.Text,
        _bossOrdinalAll.Text,
        _bossOrdinalBan.Text,
        _ancientAll.Text,
        _ancientOptionAll.Text,
        _seaGlassAny.Text,
        _seaGlassAll.Text,
        _seaGlassBan.Text,
        _relicSequence.Text,
        _eventSequence.Text);

    public void ApplyLocalization(IUiTextProvider text)
    {
        _uiText = text;
        _title.Text = text.Get(Ui1TextKey.SearchIntegratedDomains);
        _hint.Text = text.Get(Ui1TextKey.SearchIntegratedDomainsHint);
        _clear.Text = text.Get(Ui1TextKey.SearchClearDraft);
        string[] keys =
        {
            Ui1TextKey.SearchBonesOrder, Ui1TextKey.SearchEffectOutputSource,
            Ui1TextKey.SearchAny, Ui1TextKey.SearchAll, Ui1TextKey.SearchBan,
            Ui1TextKey.SearchBossAll, Ui1TextKey.SearchBossOrdinal,
            Ui1TextKey.SearchBossOrdinalAny, Ui1TextKey.SearchBossOrdinalAll, Ui1TextKey.SearchBossOrdinalBan,
            Ui1TextKey.SearchAncientAll, Ui1TextKey.SearchAncientOptionAll,
            Ui1TextKey.SearchSeaGlassTargetAny, Ui1TextKey.SearchSeaGlassTargetAll, Ui1TextKey.SearchSeaGlassTargetBan,
            Ui1TextKey.SearchRelicSequenceSyntax, Ui1TextKey.SearchEventSequenceSyntax
        };
        for (int index = 0; index < _labels.Length; index++)
        {
            _labels[index].Text = text.Get(keys[index]);
        }
        UpdateSummary();
    }

    public void SetBaseConditionCount(int count)
    {
        _baseConditionCount = Math.Max(0, count);
        UpdateSummary();
    }

    public void SetRunning(bool running)
    {
        foreach (LineEdit field in TextFields())
        {
            field.Editable = !running;
        }
        _bossOrdinal.Editable = !running;
        _clear.Disabled = running;
    }

    public void ApplyDraft(IntegratedSearchDraft draft, bool notify)
    {
        _bonesOrder.Text = draft.BonesAcquisitionOrder ?? string.Empty;
        _effectSource.Text = draft.EffectOutputSource ?? string.Empty;
        _effectAny.Text = draft.EffectOutputAny ?? string.Empty;
        _effectAll.Text = draft.EffectOutputAll ?? string.Empty;
        _effectBan.Text = draft.EffectOutputBan ?? string.Empty;
        _bossAll.Text = draft.BossAll ?? string.Empty;
        _bossOrdinal.Value = Math.Max(0, draft.BossOrdinal);
        _bossOrdinalAny.Text = draft.BossOrdinalAny ?? string.Empty;
        _bossOrdinalAll.Text = draft.BossOrdinalAll ?? string.Empty;
        _bossOrdinalBan.Text = draft.BossOrdinalBan ?? string.Empty;
        _ancientAll.Text = draft.AncientAll ?? string.Empty;
        _ancientOptionAll.Text = draft.AncientOptionAll ?? string.Empty;
        _seaGlassAny.Text = draft.SeaGlassTargetAny ?? string.Empty;
        _seaGlassAll.Text = draft.SeaGlassTargetAll ?? string.Empty;
        _seaGlassBan.Text = draft.SeaGlassTargetBan ?? string.Empty;
        _relicSequence.Text = draft.RelicSequenceConditions ?? string.Empty;
        _eventSequence.Text = draft.EventSequenceConditions ?? string.Empty;
        UpdateSummary();
        if (notify) Changed?.Invoke();
    }

    public void ClearFields()
    {
        ApplyDraft(new IntegratedSearchDraft(
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, 0, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty), notify: true);
    }

    private void OnChanged()
    {
        UpdateSummary();
        Changed?.Invoke();
    }

    private void UpdateSummary()
    {
        int enabled = EnabledConditionCount;
        string format = _uiText?.Get(Ui1TextKey.SearchEnabledConditionSummary) ?? "Enabled conditions: {0}";
        _summary.Text = string.Format(format, enabled);
    }

    private int ExtendedConditionCount()
    {
        IntegratedSearchDraft draft = CurrentDraft;
        int count = 0;
        // Bones order and the generic Neow effect-output fields are retained
        // only for source compatibility. The current controller never compiles
        // them, so they must not inflate the visible condition count.
        if (!string.IsNullOrWhiteSpace(draft.BossAll)) count++;
        if (draft.BossOrdinal > 0 &&
            (!string.IsNullOrWhiteSpace(draft.BossOrdinalAny) || !string.IsNullOrWhiteSpace(draft.BossOrdinalAll) || !string.IsNullOrWhiteSpace(draft.BossOrdinalBan))) count++;
        if (!string.IsNullOrWhiteSpace(draft.AncientAll)) count++;
        if (!string.IsNullOrWhiteSpace(draft.AncientOptionAll)) count++;
        if (!string.IsNullOrWhiteSpace(draft.SeaGlassTargetAny) || !string.IsNullOrWhiteSpace(draft.SeaGlassTargetAll) || !string.IsNullOrWhiteSpace(draft.SeaGlassTargetBan)) count++;
        if (!string.IsNullOrWhiteSpace(draft.EventSequenceConditions)) count += SplitConditions(draft.EventSequenceConditions);
        return count;
    }

    private static int SplitConditions(string text) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private IEnumerable<LineEdit> TextFields()
    {
        yield return _bonesOrder;
        yield return _effectSource;
        yield return _effectAny;
        yield return _effectAll;
        yield return _effectBan;
        yield return _bossAll;
        yield return _bossOrdinalAny;
        yield return _bossOrdinalAll;
        yield return _bossOrdinalBan;
        yield return _ancientAll;
        yield return _ancientOptionAll;
        yield return _seaGlassAny;
        yield return _seaGlassAll;
        yield return _seaGlassBan;
        yield return _relicSequence;
        yield return _eventSequence;
    }

    private static Label Label() => Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
    private static LineEdit Field(float width) => new()
    {
        CustomMinimumSize = new Vector2(width, 36),
        ClearButtonEnabled = true
    };
    private static SpinBox Number(double min, double max, double value, float width) => new()
    {
        MinValue = min,
        MaxValue = max,
        Value = value,
        Step = 1,
        CustomMinimumSize = new Vector2(width, 36),
        AllowGreater = false,
        AllowLesser = false
    };
    private static Control Labeled(Label label, Control control)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 3);
        column.AddChild(label);
        column.AddChild(control);
        return column;
    }
    private static Control Row(params Control[] controls)
    {
        var row = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("h_separation", 10);
        row.AddThemeConstantOverride("v_separation", 8);
        foreach (Control control in controls) row.AddChild(control);
        return row;
    }
}
