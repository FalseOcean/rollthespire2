using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private Button? _sidebarSettingsEntry;

    private Action BuildRunSettings(VBoxContainer settings)
    {
        var controls = new HBoxContainer { Name = "SearchSettingsFields" }; controls.AddThemeConstantOverride("separation", 12); settings.AddChild(controls);
        VBoxContainer Field(Container parent, string title)
        {
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 5);
            parent.AddChild(column);
            column.AddChild(_p.Label(title, 15, true));
            return column;
        }
        Button ActionButton(string key)
        {
            var button = _p.CompactButton(_text.Get(key), 34, 16);
            button.ClipText = false;
            button.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            button.TooltipText = "";
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.AddThemeColorOverride("font_disabled_color", _p.Color(_p.Secondary));
            return button;
        }
        var target = Field(controls, _persistence.Preferences.SkipExactValidation ? _text.Get("workflow.candidate_target") : (_language == "zh" ? "结果数量" : "Results"));
        _targetCount = new SpinBox { MinValue = 1, MaxValue = 1000, Value = _target,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 38) };
        _targetCount.AddThemeFontSizeOverride("font_size", 17);
        SearchTooltip.Set(_targetCount, _text.Get("workflow.result_target_hint"));
        SearchTooltip.Set(_targetCount.GetLineEdit(), _text.Get("workflow.result_target_hint"));
        _targetCount.ValueChanged += v => { _target = (int)v; _analysisKey = ""; }; target.AddChild(_targetCount);
        var plan = Field(controls, _text.Get("workflow.compute_mode")); plan.SizeFlagsStretchRatio = 1.4f;
        _planSelector = ContextOptions(0, 0, 144); _planSelector.Reparent(plan, false);
        _planSelector.SizeFlagsHorizontal = SizeFlags.ExpandFill; _planSelector.CustomMinimumSize = new(0, 38);
        _planSelector.AddItem(_text.Get("workflow.plan.auto")); _planSelector.AddItem(_text.Get("workflow.plan.cpu"));
        _planSelector.Select(_persistence.Preferences.SearchMode == "CPU" ? 1 : 0);

        BuildExecutionOrder(settings);

        settings.AddChild(new HSeparator { Modulate = new Color(1, 1, 1, 0.35f) });
        var gpu = Field(settings, _text.Get("settings.gpu_root_batch.title"));
        var batch = ContextOptions(0, 0, 144);
        batch.Reparent(gpu, false);
        batch.Name = "RunGpuBatch";
        batch.CustomMinimumSize = new(0, 36);
        foreach (int size in GpuRootBatchSizes.Supported)
            batch.AddItem(_text.Get(size == 0 ? "workflow.plan.auto" : $"settings.gpu_root_batch.limit_{size}"), size);
        batch.ItemSelected += index =>
        {
            _persistence.SetGpuRootBatchSize(batch.GetItemId((int)index));
            _analysisKey = "";
        };
        _planSelector.ItemSelected += index =>
        {
            _persistence.SetWorkbenchSearchMode(index == 1 ? "CPU" : "Auto");
            gpu.Visible = index != 1;
            _analysisKey = "";
        };

        var workerField = Field(settings, _text.Get("settings.workers.title"));
        var workers = new HBoxContainer();
        workers.AddThemeConstantOverride("separation", 8);
        workerField.AddChild(workers);
        var budget = new SpinBox { Name = "RunWorkerBudget", MinValue = 1, MaxValue = 64, Step = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        workers.AddChild(budget);
        var automatic = ActionButton("workflow.plan.auto");
        automatic.ToggleMode = true;
        automatic.Name = "RunWorkerAuto";
        workers.AddChild(automatic);
        void UpdateBudget(int? value)
        {
            _persistence.SetSearchWorkerBudget(value);
            budget.SetValueNoSignal(SearchWorkers);
            automatic.SetPressedNoSignal(value is null);
            _analysisKey = "";
        }
        budget.ValueChanged += value => UpdateBudget((int)value);
        automatic.Pressed += () => UpdateBudget(null);

        settings.AddChild(new HSeparator { Modulate = new Color(1, 1, 1, 0.35f) });
        var origin = Field(settings, _text.Get("settings.origin.title"));
        var seed = new LineEdit { Name = "RunOrigin", MaxLength = Beta110Profile.Instance.SeedLength,
            PlaceholderText = "000000000000" };
        seed.CustomMinimumSize = new(0, 36);
        origin.AddChild(seed);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        origin.AddChild(actions);
        var apply = ActionButton("settings.origin.apply");
        var random = ActionButton("settings.origin.random");
        apply.Name = "RunOriginApply";
        random.Name = "RunOriginRandom";
        actions.AddChild(apply);
        actions.AddChild(random);
        var status = _p.Label("", 14, true);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        origin.AddChild(status);
        void ShowOrigin()
        {
            seed.Text = _persistence.Cursor.Initialized ? _persistence.CurrentPersistentOriginSeed : "";
            status.Text = _persistence.Cursor.Initialized
                ? _text.Format("workflow.next_search_seed", _persistence.CurrentNextCursorSeed)
                : _text.Get("settings.origin.unset");
            apply.Disabled = true;
        }
        void ApplyOrigin()
        {
            if (apply.Disabled || HasActiveSearch) return;
            if (_persistence.TrySetPersistentOrigin(seed.Text, out _, out _)) ShowOrigin();
            else status.Text = _text.Get("settings.origin.invalid");
        }
        apply.Pressed += ApplyOrigin;
        seed.TextSubmitted += _ => ApplyOrigin();
        seed.TextChanged += value =>
        {
            bool valid = Beta110Profile.Instance.TryCanonicalizeSeed(value, out _, out _);
            apply.Disabled = !valid;
            status.Text = _text.Get(valid ? "settings.origin.pending" : "settings.origin.invalid");
        };
        random.Pressed += () =>
        {
            if (HasActiveSearch) return;
            _persistence.RandomizePersistentOrigin();
            ShowOrigin();
        };
        return () =>
        {
            gpu.Visible = _persistence.Preferences.SearchMode != "CPU";
            batch.Select(batch.GetItemIndex(_persistence.Preferences.GpuRootBatchSize));
            budget.SetValueNoSignal(SearchWorkers);
            automatic.SetPressedNoSignal(_persistence.Preferences.SearchWorkerBudget is null);
            ShowOrigin();
        };
    }
}
