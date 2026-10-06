using Godot;
using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class QueryWorkbenchFrame
{
    private Control? _presetRoot;
    private SearchPresetSaveTransactionOverlay? _presetSave;
    private SearchConfirmationTransactionOverlay? _presetConfirm;
    private RelicPickerPanel? _presetIconPicker;
    private readonly Dictionary<BaseButton, bool> _presetBlocked = [];
    private SearchPresetSaveIntentKind _presetIntent;
    private SearchPresetDefinition? _presetSource;
    private SearchPresetCapture? _presetCapture;
    private Action? _presetCommit;
    private Button? _presetReturnFocus;
    private bool _closingPresets;
    private SearchPresetDefinition? _historyPreset;
    internal event Action? PresetsRequested;
    internal event Action? PresetsChanged;
    internal event Action<string>? PresetIssueReported;
    private bool TryResolvePreset(string id, out SearchPresetDefinition preset)
    {
        if (_historyPreset is { } h && h.Id == id) { preset = h; return true; }
        return _persistence.Presets.TryGet(id, out preset);
    }
    internal void EditLibraryPreset(SearchPresetDefinition? preset, bool metadata)
    {
        if (HasActiveSearch) throw new InvalidOperationException(_language == "zh" ? "请先停止当前搜索。" : "Stop the current search first.");
        _historyPreset = preset;
        OpenPresetSave(preset is null ? SearchPresetSaveIntentKind.CreateCurrentQuery : metadata
            ? SearchPresetSaveIntentKind.EditMetadata : SearchPresetSaveIntentKind.CreateFromExistingSnapshot, preset?.Id ?? "");
    }
    internal void RefreshPresetEnvironment(SearchPresetDefinition preset)
    {
        if (HasActiveSearch) throw new InvalidOperationException("Stop the current search first.");
        _persistence.EnsureEnvironment(SearchEnvironmentSignatureBuilder.Capture(_runtime));
        if (_persistence.CurrentEnvironmentFingerprint.Length == 0)
            throw new InvalidOperationException(_language == "zh" ? "当前无法读取运行环境，原预设已保留。" : "The current environment could not be read. The original preset is preserved.");
        _persistence.Presets.UpdateUserEnvironment(preset.Id, _persistence.CurrentEnvironmentFingerprint);
        PresetsChanged?.Invoke();
    }
    internal void LoadQueryHistory(QueryHistoryEntry record, string title) => ApplyPreset(record.AsPreset(title));
    internal SearchPresetDefinition PreparePresetHistory(SearchPresetDefinition preset)
    {
        if (preset.Workbench is not null || _persistence.QueryKeyForPreset(preset).Length > 0) return preset;
        var draft = ResolvePresetDraft(preset);
        _persistence.BindLegacyPresetQuery(preset, draft);
        return preset with { Workbench = draft.ToPresetIntent() };
    }

    internal void SetSearchMode(bool multiplayer, bool render = true)
    {
        if (_session is not null) return;
        _multiplayer = multiplayer;
        _seat = Math.Min(_seat, multiplayer ? _playerCount - 1 : 0);
        if (render) Refresh(_language, _text); else RefreshEditorContexts(false);
    }

    private void AttachPresetOverlays(Control shell)
    {
        _presetRoot = new Control { Name = "WorkbenchPresets", MouseFilter = MouseFilterEnum.Ignore };
        shell.AddChild(_presetRoot); _presetRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var tooltip = new AnchoredTooltipHost(new RuntimeRelicTooltipResolver(), new RuntimePotionTooltipResolver(), new RuntimeCardTooltipResolver());
        _presetSave = new(_icons, tooltip);
        _presetConfirm = new();
        _presetIconPicker = new(_icons, tooltip);
        foreach (var child in new Control[] { _presetSave, _presetConfirm, _presetIconPicker, tooltip })
        { _presetRoot.AddChild(child); child.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
        _presetSave.SaveRequested += CommitPresetSave;
        _presetSave.Cancelled += FinishPresetModal;
        _presetConfirm.Confirmed += () =>
        {
            var commit = _presetCommit; _presetCommit = null;
            try { commit?.Invoke(); }
            catch (Exception ex) { if (_presetSave.IsOpen) _presetSave.ShowValidationError(PresetIssue(ex)); else PresetIssueReported?.Invoke(PresetIssue(ex)); }
            FinishPresetModal();
        };
        _presetConfirm.Cancelled += () => { _presetCommit = null; FinishPresetModal(); };
        _presetIconPicker.Closed += () => { if (!_closingPresets) _presetSave.FocusAfterChildPicker(0); };
        _presetSave.VisualIconRequested += slot =>
        {
            var candidates = MegaCrit.Sts2.Core.Models.ModelDb.AllRelics.Select(r => new ModelKey(r.Id.Category, r.Id.Entry)).ToArray();
            _presetIconPicker.Open(new RelicPickerRequest(_language == "zh" ? "选择标记" : "Choose icon", candidates,
                _presetSave.GetVisualIcon(slot), [], new Dictionary<ModelKey, RelicPickerCategory>(), true,
                GameContentKind.Relic, IconVariant.Small, key => _presetSave.SetVisualIcon(slot, key)));
        };
    }

    private void LocalizePresets()
    {
        var text = JsonUiTextProvider.CreatePredictorUi13(_language);
        var names = RuntimeGameContentNameResolver.Create(_language);
        _presetSave!.ApplyLocalization(text, names);
        _presetIconPicker!.ApplyLocalization(text, names);
    }

    private void BeginPresetModal()
    {
        if (_presetBlocked.Count == 0)
        {
            _presetReturnFocus = GetViewport().GuiGetFocusOwner() as Button;
            foreach (var node in FindChildren("*", "BaseButton", true, false))
                if (node is BaseButton button) { _presetBlocked[button] = button.Disabled; button.Disabled = true; }
        }
        ModalChanged?.Invoke(true);
    }

    private void FinishPresetModal()
    {
        if (_closingPresets || _presetSave?.IsOpen == true || _presetConfirm?.IsOpen == true) return;
        foreach (var (button, disabled) in _presetBlocked)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = disabled;
        _presetBlocked.Clear(); ModalChanged?.Invoke(false);
        if (_presetReturnFocus is not null && GodotObject.IsInstanceValid(_presetReturnFocus)) _presetReturnFocus.GrabFocus();
    }

    private void OpenPresets()
    {
        PresetsRequested?.Invoke();
    }

    private void OpenPresetSave(SearchPresetSaveIntentKind intent, string sourceId)
    {
        if (_session is not null || _presetSave is null) return;
        try
        {
            _presetIntent = intent;
            _presetSource = TryResolvePreset(sourceId, out var source) ? source : null;
            _presetCapture = intent == SearchPresetSaveIntentKind.CreateCurrentQuery ? CapturePreset() : null;
            LocalizePresets(); BeginPresetModal();
            _presetSave.OpenCreate(_language == "zh" ? "保存预设" : "Save preset",
                // Editable/save-by-name text stays literal across UI language changes.
                _presetSource?.Title ?? "",
                _presetSource?.Description ?? "", _presetSource?.VisualIcons.Where(i => i.TryGetModelKey(out _))
                    .Select(i => { i.TryGetModelKey(out var key); return key; }).ToArray());
        }
        catch (Exception ex) { ReceiptText(PresetIssue(ex)); PresetIssueReported?.Invoke(PresetIssue(ex)); }
    }

    private void CommitPresetSave()
    {
        string title = _presetSave!.TitleText.Trim(), description = _presetSave.DescriptionText.Trim();
        var icons = _presetSave.SelectedRelicIcons.Select(SearchPresetVisualIconRef.FromRelic).ToArray();
        if (title.Length == 0) return;
        bool duplicate = _persistence.Presets.TryGetUserByName(title, out var sameName);
        if (_presetIntent == SearchPresetSaveIntentKind.EditMetadata && duplicate && sameName.Id != _presetSource?.Id)
        { _presetSave.ShowValidationError(_language == "zh" ? "此名称已被另一预设使用。" : "Another preset already uses this name."); return; }
        void Save()
        {
            var catalog = _persistence.Presets;
            var saved = _presetIntent switch
            {
                SearchPresetSaveIntentKind.EditMetadata => catalog.UpdateUserMetadata(_presetSource!.Id, title, description, icons),
                SearchPresetSaveIntentKind.CreateFromExistingSnapshot => catalog.SaveUserPresetFromExisting(title, description, icons, _presetSource!),
                _ => catalog.SaveUserPreset(title, description, icons, _presetCapture!)
            };
            if (saved.Workbench is null) _persistence.BindLegacyPresetQuery(saved, ResolvePresetDraft(saved));
            else _persistence.PreservePresetQuery(saved);
            if (_presetIntent == SearchPresetSaveIntentKind.CreateCurrentQuery && _persistence.CurrentEnvironmentFingerprint.Length > 0)
                catalog.UpdateUserEnvironment(saved.Id, _persistence.CurrentEnvironmentFingerprint);
            _presetSave.CloseCommitted(); FinishPresetModal(); PresetsChanged?.Invoke();
            ReceiptText((_language == "zh" ? "已保存预设：" : "Saved preset: ") + saved.Title);
        }
        if (duplicate && _presetIntent != SearchPresetSaveIntentKind.EditMetadata)
            ConfirmPreset(_language == "zh" ? "更新已有预设" : "Update existing preset", title, Save);
        else try { Save(); } catch (Exception ex) { _presetSave.ShowValidationError(PresetIssue(ex)); }
    }

    private void ConfirmPreset(string title, string name, Action commit)
    {
        _presetCommit = commit;
        _presetConfirm!.Open(title, name, _language == "zh" ? "确认" : "Confirm", _language == "zh" ? "取消" : "Cancel");
    }

    private bool ClosePresetModal()
    {
        if (_presetIconPicker?.IsOpen == true) { _presetIconPicker.Cancel(); return true; }
        if (_presetConfirm?.IsOpen == true) { _presetConfirm.Cancel(); return true; }
        if (_presetSave?.IsOpen == true) { _presetSave.Cancel(); return true; }
        return false;
    }

    private void CloseAllPresetModals()
    {
        _closingPresets = true;
        while (ClosePresetModal()) { }
        _closingPresets = false; FinishPresetModal();
    }

    private void ReceiptText(string text)
    {
        if (_status is not null) _status.Text = text;
        _receiptRemaining = 5;
    }

    private string PresetIssue(Exception ex) => (_language == "zh"
        ? "原预设已保留。请检查所需模组与角色是否已加载；不会自动替换角色或删除条件。\n"
        : "The original preset is preserved. Check that its required mods and characters are loaded. Characters and conditions are not replaced automatically.\n") + Explain(ex);

    internal SearchPresetCapture CapturePreset(WorkbenchSearchDraft? authored = null)
    {
        if (_loadFailed) throw new InvalidOperationException("integration.load_shape");
        var draft = (authored ?? CaptureDraft()).WithoutCapturedAuthority();
        var time = DateTimeOffset.UtcNow;
        var probability = SearchPresetProbabilitySnapshot.Unavailable(time);
        var provenance = SearchPresetProvenance.Unknown(_runtime.Detection.NormalizedVersion, time);
        try
        {
            var environment = RuntimeAuthorityEnvironment.Current;
            var authority = environment.Authority;
            var interpretation = environment.Interpretation;
            // The environment's unlock coverage describes the local profile,
            // not a whole party. Per-seat unlock snapshots remain in the draft.
            string unlock = draft.Players.Count > 0 ? SearchPresetUnlockKinds.Unknown : interpretation.VanillaUnlockStatus switch
            {
                VanillaUnlockOverallStatus.Full => SearchPresetUnlockKinds.Full,
                VanillaUnlockOverallStatus.Partial => SearchPresetUnlockKinds.Partial,
                _ => SearchPresetUnlockKinds.Unknown
            };
            provenance = new SearchPresetProvenance(
                string.IsNullOrWhiteSpace(authority.GameVersion) ? _runtime.Detection.DisplayVersion : authority.GameVersion,
                authority.FingerprintSchemaVersion,
                authority.Fingerprint.OverallSemanticHash ?? string.Empty,
                interpretation.EnvironmentStatus.ToString(),
                interpretation.MatchedBaselineGameVersion ?? string.Empty,
                interpretation.ComparisonBaselineGameVersion ?? string.Empty,
                interpretation.EnvironmentReason ?? string.Empty,
                unlock, time);
        }
        catch (Exception ex)
        {
            RuntimeLog.Info("workbenchPresetProvenanceUnavailable=" + ex.Message);
        }
        try
        {
            var compiled = draft.Compile(_runtime, out _);
            if (compiled.Context.Party is null)
                probability = SearchPresetProbabilitySnapshot.From(SearchProbabilityPresentationBuilder.Build(compiled), time);
            else
            {
                // The same whole-table estimator used by the workbench already
                // accounts for shared facts and personal-condition approximations.
                var joint = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(compiled));
                var status = joint.ExactlyImpossible ? SearchProbabilityQuickViewStatus.Impossible
                    : joint.JointlyPriced ? SearchProbabilityQuickViewStatus.Complete
                    : joint.PartiallyPriced ? SearchProbabilityQuickViewStatus.Partial
                    : SearchProbabilityQuickViewStatus.Unavailable;
                probability = new SearchPresetProbabilitySnapshot(status.ToString(),
                    status == SearchProbabilityQuickViewStatus.Impossible ? 0d
                        : status == SearchProbabilityQuickViewStatus.Complete ? joint.Probability : null, time);
            }
        }
        catch (Exception ex)
        {
            // Historical metadata must never prevent saving authored intent.
            RuntimeLog.Info("workbenchPresetProbabilityUnavailable=" + ex.Message);
        }
        return new(draft.Character, draft.Ascension, null, CountPresetConditions(draft.Query),
            probability, provenance, time)
            { Workbench = draft.ToPresetIntent() };
    }

    private void RecordTemporaryPreset(WorkbenchSearchDraft draft)
    {
        try { _persistence.Presets.RecordTemporary(CapturePreset(draft)); }
        catch (Exception ex) { RuntimeLog.Warn("workbenchTemporaryPreset=" + ex.Message); }
    }

    internal static int CountPresetConditions(SearchQuery q) =>
        (q.OpeningRoute is null ? 0 : 1) + (q.OpeningRouteRelicRequirement is null ? 0 : 1) + q.StructuredOpeningEffects.Count +
        q.RelicSequenceConstraints.Count + (q.CombatCardRewards is null ? 0 : 1) + (q.CombatPotionRewards is null ? 0 : 1) +
        q.AncientBranches.Count + q.LegacyNeow.NeowRelics.Any.Count + q.LegacyNeow.NeowRelics.All.Count +
        q.LegacyWorld.AncientIdentityFilters.Count + q.LegacyWorld.AncientOptionFilters.Count + q.EventResultConditions.Count +
        q.EventSequenceConstraints.Count + q.VariantBossBranches.Count + q.StandardMaps.Count + q.MerchantColorlessSequenceConditions.Count +
        q.RelicShopSequenceConditions.Count + (q.TransformationAggregate is null ? 0 : 1) +
        q.Players.Sum(p => CountPresetConditions(p.Conditions) + (p.Offers.IsEmpty ? 0 : 1));

    private WorkbenchSearchDraft ResolvePresetDraft(SearchPresetDefinition preset)
    {
        WorkbenchSearchDraft draft;
        if (preset.Workbench is not null || !string.IsNullOrWhiteSpace(preset.RawWorkbenchJson))
        {
            var resolution = SearchPresetCompatibilityResolver.ResolveWorkbench(preset, RuntimeAuthorityEnvironment.Current.Authority);
            draft = resolution.CanLoad ? resolution.Draft! : throw new InvalidOperationException(resolution.Issue +
                "\n" + string.Join("\n", resolution.Unresolved.Select(r => $"{r.Path}: {r.StableIdentity}")));
        }
        else
        {
            var resolution = SearchPresetCompatibilityResolver.Resolve(preset, RuntimeAuthorityEnvironment.Current.Authority);
            if (!resolution.CanLoad || resolution.Kind == SearchPresetLoadResolutionKind.Partial)
                throw new InvalidOperationException(resolution.Issue);
            var compiled = WorkbenchQueryCompiler.CompileAuthoredDraft(resolution.Draft!, _runtime,
                preset.CharacterKey, preset.Ascension, out _);
            draft = new(preset.CharacterKey, preset.Ascension, MigrateLegacyPresetQuery(compiled.Query), compiled.Context.EvaluationAssumptions.AncientEligibilityAssumptions);
        }
        return BindPresetEnvironment(draft);
    }

    private WorkbenchSearchDraft BindPresetEnvironment(WorkbenchSearchDraft draft)
    {
        var lobby = IsInsideTree() ? LobbyUnlockReadout.Find(GetTree().Root) : null;
        return draft.BindPresetIntent(_runtime, slot =>
        {
            if (lobby is null || lobby.Players.Count != draft.Players.Count)
                throw new InvalidOperationException(_language == "zh"
                    ? "此预设需要当前多人解锁信息。请进入对应人数的大厅后重试；原预设已保留。"
                    : "This preset needs current party unlock data. Open a lobby with the matching player count and retry. The original preset is preserved.");
            return lobby.Players.Single(p => p.slotId == slot).unlockState;
        });
    }

    internal void ApplyPreset(SearchPresetDefinition preset, bool render = true)
    {
        if (_session is not null) throw new InvalidOperationException("Stop the current search before applying a preset.");
        if (_loadFailed) throw new InvalidOperationException("integration.load_shape");
        var draft = ResolvePresetDraft(preset);
        var expected = draft.Compile(_runtime, out _);
        // A disposable editor transaction proves the entire roster and grammar can
        // survive current editor legality checks before touching the user's drafts.
        var probe = new QueryWorkbenchFrame(_runtime, _persistence);
        try
        {
            probe.RestoreDraft(draft, render: false);
            var restored = probe.CaptureDraft().WithoutCapturedAuthority();
            if (!SamePresetSemantics(expected, restored.Compile(_runtime, out _)) ||
                draft.AncientEditor is not null && JsonSerializer.Serialize(draft.AncientEditor) != JsonSerializer.Serialize(restored.AncientEditor) ||
                draft.PartyAncientEditor is not null && JsonSerializer.Serialize(draft.PartyAncientEditor) != JsonSerializer.Serialize(restored.PartyAncientEditor) ||
                draft.Players.Any(p => p.AncientEditor is not null && JsonSerializer.Serialize(p.AncientEditor) != JsonSerializer.Serialize(restored.Players[p.Slot].AncientEditor)))
                throw new InvalidOperationException("integration.load_shape");
        }
        finally { probe._characterIcons.Dispose(); probe.Free(); }
        _persistence.SaveWorkbench(draft); _lastSaved = JsonSerializer.Serialize(draft);
        _persistence.BindLegacyPresetQuery(preset, draft);
        RestoreDraft(draft, render: false);
        if (draft.Players.Any(p => p.UnlockSource == "CurrentContext"))
        {
            _readLobby = LobbyUnlockReadout.Find(GetTree().Root);
            _readRoster = _readLobby is null ? "" : LobbyUnlockReadout.Roster(_readLobby);
        }
        _showResults = false; _analysisKey = "";
        if (render) Refresh(_language, _text);
    }

    private static bool SamePresetSemantics(CompiledSearch expected, CompiledSearch actual)
    {
        // Event scenario identity includes capture timestamps. Compare both fresh
        // pool/target contents under the same authority object; keep all predicates,
        // premises and actual context fingerprints. No production fingerprint or
        // pool authority changes, and no runtime pool is written into the preset.
        Core.Prediction.MorphicGroveScenario? Comparable(Core.Prediction.MorphicGroveScenario? value,
            Core.Prediction.MorphicGroveScenario? reference)
        {
            if (value is null || reference is null) return value;
            if (value.Authority.UnlockSnapshotFingerprint != reference.Authority.UnlockSnapshotFingerprint ||
                value.Authority.CatalogFingerprint != reference.Authority.CatalogFingerprint ||
                value.Authority.EffectSnapshotFingerprint != reference.Authority.EffectSnapshotFingerprint ||
                value.Authority.WorldSnapshotFingerprint != reference.Authority.WorldSnapshotFingerprint) return value;
            return new(reference.Authority, value.Premises, value.Targets);
        }
        SearchQuery Bind(SearchQuery value, SearchQuery reference) => value with
        {
            EventResultConditions = value.EventResultConditions.Select(c => c with { MorphicGroveScenario = Comparable(c.MorphicGroveScenario,
                reference.EventResultConditions.FirstOrDefault(r => r.Kind == c.Kind)?.MorphicGroveScenario) }).ToArray(),
            TransformationAggregate = value.TransformationAggregate is { } t ? t with
                { EventScenario = Comparable(t.EventScenario, reference.TransformationAggregate?.EventScenario) } : null,
            Players = value.Players.Select(p => p with { Conditions = Bind(p.Conditions,
                reference.Players.FirstOrDefault(r => r.Slot == p.Slot)?.Conditions ?? SearchQuery.Empty) }).ToArray()
        };
        return expected.SemanticFingerprint == SearchCompiler.Compile(Bind(actual.Query, expected.Query), actual.Context).SemanticFingerprint;
    }

    private static SearchQuery MigrateLegacyPresetQuery(SearchQuery query)
    {
        // Both predicates read position one of the same pristine Shop relic lane
        // in ProductionQueryValidator. Only this exact legacy shape is migrated;
        // arbitrary Any/All/Ban ranges keep their original compatibility boundary.
        var shop = query.RelicSequenceConstraints.Where(c => c.Lane == Core.Relics.RelicSequenceKind.Shop).ToArray();
        if (shop is [var first] && first.RangeValue == 1 &&
            first.RangeMode is Search.Contracts.SearchSequenceRangeMode.FirstN or Search.Contracts.SearchSequenceRangeMode.ExactSlot &&
            first.Keys.Any.Count == 1 && first.Keys.All.Count == 0 && first.Keys.Ban.Count == 0 &&
            query.RelicShopSequenceConditions.Count == 0)
            return query with
            {
                RelicSequenceConstraints = query.RelicSequenceConstraints.Where(c => c.Lane != Core.Relics.RelicSequenceKind.Shop).ToArray(),
                RelicShopSequenceConditions = [new(1, Search.Contracts.CombatRewardSequenceOrderMode.Ordered, [first.Keys.Any[0]])]
            };
        return query;
    }
}
