using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private ModRuntimeSnapshot _runtime = null!;
    private SearchWorkspace _persistence = null!;
    internal object? CaptureSearchDiagnostics() => _persistence.Runtime.CaptureDiagnostics();
    internal bool HasActiveSearch => _persistence.Runtime.HasSearchInFlight;
    internal void SyncSearchPreferences()
    {
        _planSelector?.Select(_persistence.Preferences.SearchMode == "CPU" ? 1 : 0);
        _analysisKey = "";
        UpdateDraftStatus();
        if (!_persistence.Runtime.Receiving) Refresh(_language, _text);
    }
    private Button? _start;
    private Label? _status;
    private OptionButton? _planSelector;
    private SpinBox? _targetCount;
    private ScrollContainer? _resultSurface;
    private VBoxContainer? _resultRows;
    private string _lastSaved = "", _lastPreferences = "", _lastIssue = "";
    private Dictionary<string, bool>? _savedUiFlags;
    private string _savedUiDomain = "";
    private bool _restored, _showResults;
    private bool _loadFailed;
    private string _restoreFailure = "";
    private bool _restoreAdjusted;
    private string? _restoredCaptureStamp;
    private double _receiptRemaining;
    private readonly Dictionary<BaseButton, bool> _runBlocked = [];
    private int _target = 30;
    private double _saveTick;

    internal WorkbenchSearchDraft CaptureDraft()
    {
        if (_multiplayer) return CapturePartyDraft();
        SearchQuery q = _neowEditor.ExportQuery();
        q = q with { RelicSequenceConstraints = _relicEditor.BuildConditions(), TreasureRelics = _relicEditor.BuildTreasureCondition(), CombatCardRewards = _combatEditor.ExportCondition(), CombatPotionRewards = _combatEditor.ExportPotionCondition(), CombatCardExclusion = _combatEditor.ExportExclusionCondition() };
        q = _ancientEditor.ExportQuery(q);
        q = _eventEditor.ExportQuery(q);
        q = _actInformationEditor.ExportQuery(q);
        q = _shopEditor.ExportQuery(q);
        q = q with { TransformationAggregate = _transformationEditor.ExportCondition(q) };
        return new(_soloCharacter, _soloAscension, q, _ancientEditor.OptionConditions)
        { AncientEditor = _ancientEditor.ExportEditorState(), NeowEditor = _neowEditor.ExportEditorState(),
            EventEditor = _eventEditor.ExportEditorState(), TransformationEditor = _transformationEditor.ExportEditorState(),
            CombatRewardEditor = _combatEditor.ExportEditorState() };
    }

    internal void RestoreDraft(WorkbenchSearchDraft draft, bool render = true)
    {
        using var observation = _editorInputs.Observe(_runtime);
        if (draft.Version is not (1 or 2 or 3 or 4)) throw new InvalidOperationException("integration.load_version");
        if (draft.Players.Count > 0) { RestorePartyDraft(draft, render); return; }
        ValidateRepresentable(draft.Query);
        _multiplayer = false; _seat = 0; _soloCharacter = draft.Character; _soloAscension = draft.Ascension;
        RefreshEditorContexts(render: false);
        _neowEditor.ImportQuery(draft.Query);
        if (draft.NeowEditor is { } neowState) _neowEditor.ImportEditorState(neowState);
        _relicEditor.ImportQuery(draft.Query);
        _combatEditor.ImportQuery(draft.Query);
        if (draft.CombatRewardEditor is { } combatState) _combatEditor.ImportEditorState(combatState);
        _ancientEditor.ImportQuery(draft.Query, draft.AncientPremises, draft.AncientEditor);
        _eventEditor.ImportQuery(draft.Query);
        if (draft.EventEditor is { } eventState) _eventEditor.ImportEditorState(eventState);
        _actInformationEditor.ImportQuery(draft.Query);
        _shopEditor.ImportQuery(draft.Query);
        _transformationEditor.ImportQuery(draft.Query);
        if (draft.TransformationEditor is { } transformState) _transformationEditor.ImportEditorState(transformState);
        RefreshEditorContexts(render);
        _restoreAdjusted=JsonSerializer.Serialize(draft.WithoutCapturedAuthority().Query)!=
            JsonSerializer.Serialize(CaptureDraft().WithoutCapturedAuthority().Query);
        _restoredCaptureStamp = _restoreAdjusted ? JsonSerializer.Serialize(CaptureDraft().WithoutCapturedAuthority()) : null;
    }

    // This UI has a narrower grammar than the backend. Never truncate foreign/older drafts.
    private static void ValidateRepresentable(SearchQuery q, bool allowSharedAncientIdentity = false)
    {
        bool unsupported = q.StandardMaps.Any(c=>!c.IsValid) || q.StandardMaps.Any(c=>c.RouteObjective && c.Scope==0) && q.StandardMaps.Any(c=>c.RouteObjective && c.Scope>0) || q.StandardMaps.Where(c=>c.RouteObjective).GroupBy(c=>c.Scope).Any(g=>g.Count()>1) || q.CombatCardRewards is { Count: > 6 or < 1 } || q.CombatPotionRewards is { Count: > 6 or < 1 } ||
            q.CombatCardExclusion is { } exclusion && (exclusion.BattleCount is < 1 or > 6 || exclusion.CardKeys is not { Count: > 0 } || exclusion.CardKeys.Any(k => !k.IsValid || k.Category != "CARD")) ||
            q.CombatPotionRewards is { } potionSequence && (potionSequence.Slots.Count != potionSequence.Count || potionSequence.Slots.Any(p=>!p.IsValid)) ||
            q.CombatCardRewards is { } cardSequence && cardSequence.Slots.Count != cardSequence.Count ||
            q.LegacyCombatRewardConstraints.Count != 0 || q.LegacyWorld.BossFilters.Count != 0 ||
            !Pages.Search.Editors.ActInformationSearchEditor.CanImportBossOrdinals(q) ||
            (!allowSharedAncientIdentity && q.LegacyWorld.AncientIdentityFilters.Count != 0) ||
            (allowSharedAncientIdentity && (q.LegacyWorld.AncientIdentityFilters
                .Any(c => c.Act is not (2 or 3) || c.Keys.Any.Count is < 1 or > 4 || c.Keys.All.Count != 0 || c.Keys.Ban.Count != 0 ||
                    c.Keys.Any.Distinct(ModelKeyComparer.Instance).Count() != c.Keys.Any.Count) ||
                q.LegacyWorld.AncientIdentityFilters.Select(c => c.Act).Distinct().Count() != q.LegacyWorld.AncientIdentityFilters.Count)) ||
            q.LegacyWorld.AncientSeaGlassTargetFilters.Count != 0 || q.MerchantColorlessConditions.Count != 0 ||
            q.AncientBranches.Any(b => b.Act is not (2 or 3) || b.SeaGlassTargetAny.Count > 1) ||
            !Pages.Search.Editors.ActInformationSearchEditor.CanImportVariantBossBranches(q) ||
            q.RelicSequenceConstraints.Any(c => c.Keys.Any.Count > 1) ||
            q.EventSequenceConstraints.Any(c => c.Source is not null || c.Keys.Any.Count > 1) ||
            q.TransformationAggregate is { UsesNeow: true, Opening: TransformationOpening.None };
        unsupported |= q.TreasureRelics is { IsValid: false } || q.FirstActUnknownEventPrefixCount is < 0 or > 3 ||
            q.AncientBranches.Any(b => !b.IsValid) ||
            q.VariantBossBranches.Any(b => !b.IsValid) ||
            q.EventSequenceConstraints.Any(c => c.Act is < 1 or > 3 || c.RangeValue < 1) ||
            q.StructuredOpeningEffects.Any(c => c.OutputKeys.Count > (c.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset ? 3 :
                c.SourceRelicKey == BaseGameModelKeys.Relics.ScrollBoxes ? 3 : c.Kind is NeowStructuredConditionKind.ExactUnorderedPair or NeowStructuredConditionKind.IndependentOfferGroupTargets ? 2 : 1)) ||
            q.OpeningRouteRelicRequirement is { RequiredRelicKeys.Count: > 2 } ||
            q.LegacyWorld.AncientOptionFilters.Any(c => c.Act is not (2 or 3) || c.Keys.Any.Count > 0 || c.Keys.Ban.Count > 0 ||
                q.AncientBranches.Count(b => b.Act == c.Act) != 1) ||
            q.LegacyNeow.NeowRelics.Any.Count > 0 && q.LegacyNeow.NeowRelics.All.Count > 0;
        unsupported |= q.EventResultConditions.GroupBy(c=>c.Kind).Any(g=>g.Count()>1) ||
            q.EventResultConditions.Any(c=>!Enum.IsDefined(c.Kind));
        unsupported |= q.EventResultConditions.Any(c => c.Kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains) &&
            q.EventResultConditions.Any(c => c.Kind == EventResultConditionKind.TrialCase && c.TrialCase != TrialCaseTarget.Nondescript);
        foreach(var effect in q.StructuredOpeningEffects)
        {
            bool special=effect.SourceRelicKey==BaseGameModelKeys.Relics.ScrollBoxes ||
                effect.SourceRelicKey==BaseGameModelKeys.Relics.Kaleidoscope ||
                effect.SourceRelicKey==BaseGameModelKeys.Relics.NeowsBones &&
                (effect.Scope==NeowStructuredEffectScope.FinalCurse || effect.Kind==NeowStructuredConditionKind.ExactGroupedCapsuleMultiset);
            if(!special && !Pages.Search.Neow.NeowEffectCardRegistry.Get(effect.SourceRelicKey).Components.Any(c=>
                c.Scope==effect.Scope && c.OutputKind==effect.OutputKind && c.ConditionKind==effect.Kind)) unsupported=true;
        }
        var horizons=q.MerchantColorlessSequenceConditions.Select(c=>c.Count).Concat(q.RelicShopSequenceConditions.Select(c=>c.Count)).Distinct().ToArray();
        unsupported |= horizons.Any(n=>n is <1 or >5) || q.RelicShopSequenceConditions.Count>1 ||
            q.MerchantColorlessSequenceConditions.GroupBy(c=>c.Slot).Any(g=>g.Count()>1);
        if (q.TransformationAggregate is { } t)
        {
            unsupported |= !Enum.IsDefined(t.Opening) || !Enum.IsDefined(t.Predicate) || !Enum.IsDefined(t.PickupOrder) ||
                t.TargetMultiset.Count>t.OpportunityCount || t.MinimumRareCount>t.OpportunityCount;
            unsupported |= t.UsesNeow && q.OpeningRoute is null;
            bool Queued(string id) => q.EventSequenceConstraints.Any(c=>c.Keys.Any.Concat(c.Keys.All).Any(k=>k.Entry==id));
            unsupported |= t.MorphicGrove && !Queued("MORPHIC_GROVE") || t.AromaOfChaos && !Queued("AROMA_OF_CHAOS") || t.WhisperingHollow && !Queued("WHISPERING_HOLLOW") || t.Symbiote && !Queued("SYMBIOTE") || t.TrialNondescript && !Queued("TRIAL");
        }
        // Legacy Neow sidecar is only used for A's Neow offer set in this editor.
        var n = q.LegacyNeow;
        unsupported |= n.NeowRelics.Ban.Count > 0 || n.RequireNeowsBones || !n.BonesRelics.IsEmpty || n.RequiredBonesCombination.Count > 0 ||
            n.RequireSmallCapsule || n.RequireLargeCapsule || !n.CapsuleContainedRelics.IsEmpty || n.RequireWhetstone || n.RequireWarPaint ||
            n.RequiredFinalCurse.HasValue || n.BannedFinalCurses.Count > 0 || n.RequiredBonesAcquisitionOrder.Count > 0 || n.EffectOutputConditions.Count > 0 || n.Preset != NeowSearchPreset.None;
        if (unsupported) throw new InvalidOperationException("integration.load_shape");
    }

    private Dictionary<string, bool> CaptureUiPreferences() => new()
    {
        ["n.guide"]=_neowEditor.GuideOpen,["c.guide"]=_combatEditor.GuideOpen,["r.guide"]=_relicEditor.GuideOpen,
        ["a.guide"]=_ancientEditor.GuideOpen,["e.guide"]=_eventEditor.GuideOpen,["t.guide"]=_transformationEditor.GuideOpen,
        ["s.guide"]=_shopEditor.GuideOpen,["r.advanced"]=_relicEditor.AdvancedOpen,["a.advanced"]=_ancientEditor.AdvancedOpen,
        ["e.advanced"]=_eventEditor.AdvancedOpen,["m.advanced"]=_actInformationEditor.AdvancedOpen
    };
    private void RestoreUiPreferences()
    {
        var f = _persistence.Preferences.WorkbenchFlags;
        _neowEditor.GuideOpen=f.GetValueOrDefault("n.guide"); _combatEditor.GuideOpen=f.GetValueOrDefault("c.guide");
        _relicEditor.GuideOpen=f.GetValueOrDefault("r.guide"); _ancientEditor.GuideOpen=f.GetValueOrDefault("a.guide");
        _eventEditor.GuideOpen=f.GetValueOrDefault("e.guide"); _transformationEditor.GuideOpen=f.GetValueOrDefault("t.guide");
        _shopEditor.GuideOpen=f.GetValueOrDefault("s.guide"); _relicEditor.AdvancedOpen=f.GetValueOrDefault("r.advanced");
        // Ancient mode is restored with the query/editor state, not a display preference.
        _eventEditor.AdvancedOpen=f.GetValueOrDefault("e.advanced");
        _actInformationEditor.AdvancedOpen=f.GetValueOrDefault("m.advanced");
        string page = _persistence.Preferences.WorkbenchPage;
        if (page == "act") page = "map"; // Older Workbench preference used one combined entry.
        if (_domainButtons.ContainsKey(page)) _selectedDomain=page;
    }

    private void BuildProductionDock()
    {
        BuildSearchSidebar();
        _analysisKey="";
        var statusScroll = new ScrollContainer { Name = "SearchStatus", Position = new(CenterLeft + 8, DockTop + 6),
            Size = new(CenterWidth - 16, DockHeight - 6), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(statusScroll);
        var statusColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        statusScroll.AddChild(statusColumn);
        _status=_p.Label("",15,true); _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.AutowrapMode=TextServer.AutowrapMode.WordSmart; statusColumn.AddChild(_status);
        if (_loadFailed)
        {
            var recover = _p.Button(_text.Get("integration.recover_draft"));
            recover.Name = "RecoverWorkbenchDraft";
            SearchTooltip.Set(recover, _text.Get("integration.recover_draft_hint"));
            recover.Pressed += RecoverDraft;
            statusColumn.AddChild(recover);
        }
        BuildRuntimeSurface();
        if(_showResults) RenderResults();
        UpdateDraftStatus();
        if (_loadFailed && _status is not null) _status.Text=Explain(new InvalidOperationException(_restoreFailure.Length == 0 ? "integration.load_shape" : _restoreFailure));
        else if (_restoreAdjusted) Receipt("integration.revalidated");
        LockForRun(_persistence.Runtime.Receiving);
    }

    private string Explain(Exception ex)
    {
        // An unfinished local transformation condition is actionable editor
        // state. It must still block export, but is not a system/save failure.
        if (!IsUnfinishedTransformation(ex)) _persistence.RecordPresentationFailure(ex, "QueryAdmission");
        if (ex.Message.StartsWith(RolltheSpire2.Compatibility.ProfileSeedGenerator.UnavailableCode, StringComparison.Ordinal))
            return SeedProfileUnavailableMessage();
        if (TransformationContinuationIssueKey(ex.Message) is { } continuationKey) return _text.Get(continuationKey);
        if (ex.Message.StartsWith("GpuFloat64UnavailableWithExactSkipped", StringComparison.Ordinal))
            return _text.Get("integration.gpu_numeric_requires_exact");
        if (ex.Message.StartsWith("GpuFloat64PhysicalSelectionUnavailable", StringComparison.Ordinal))
            return _text.Get("integration.gpu_numeric_trial_unavailable");
        if (ex.Message is "FamilyGpuInitializationRetryRequired" or "FamilyGpuInitializationBusy")
            return RolltheSpire2.Presentation.Localization.JsonUiTextProvider.Create(_language).Get(
                ex.Message == "FamilyGpuInitializationBusy" ? "ui1.devices.creating" : "ui1.devices.creation_failed_help");
        return ex.Message.StartsWith("integration.",StringComparison.Ordinal) ? _text.Get(ex.Message) : _text.Get("integration.rejected")+"\n"+ex.Message;
    }
    private static bool IsUnfinishedTransformation(Exception exception) =>
        exception is InvalidOperationException { Message: "integration.transform.capacity" };
    private string SeedProfileUnavailableMessage() => _runtime.Detection.IsExact
        ? _text.Format("integration.seed_profile_unavailable", _runtime.Detection.DisplayVersion, _runtime.Profile.ProfileId)
        : _text.Get("integration.game_version_unavailable");
    internal static string? TransformationContinuationIssueKey(string issue) =>
        issue.Contains("NeowDependentRelicContinuationNotSupported", StringComparison.Ordinal) ? "integration.transform.relic_continuation_conflict" :
        issue.Contains("NeowDependentCombatRewardContinuationNotSupported", StringComparison.Ordinal) ? "integration.transform.combat_continuation_conflict" :
        issue.Contains("NeowDependentRewardRelicContinuationNotSupportedInV1", StringComparison.Ordinal) ? "integration.transform.continuation_conflict" : null;
    private void SaveDraft()
    {
        if (_loadFailed) return;
        try
        {
            // Unavailable catalogs must not replace the saved intent on panel closure.
            _ = RolltheSpire2.Compatibility.ProfileSeedGenerator.CreateProbeSeed(_runtime.Profile);
            var draft=CaptureDraft().WithoutCapturedAuthority();
            string serialized = JsonSerializer.Serialize(draft);
            if (_restoredCaptureStamp == serialized || _lastSaved == serialized) return;
            _persistence.SaveWorkbench(draft); _lastSaved = serialized;
            _loadFailed=false;
            TryAuxiliary(UpdateResultNavigation, "ResultNavigation");
        }
        catch (Exception ex) when (IsUnfinishedTransformation(ex))
        { if (_status is not null) _status.Text = Explain(ex); }
        catch(Exception ex) { AuxiliaryFailure(ex, "DraftSave"); }
    }
    private void RecoverDraft()
    {
        if (!_loadFailed || _persistence.Runtime.Receiving) return;
        try
        {
            var fresh = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Silent, 0, SearchQuery.Empty,
                RolltheSpire2.Core.World.AncientOptionConditionProfile.BroadDefault);
            _persistence.RecoverWorkbench(fresh);
            RestoreDraft(fresh, render: false);
            _loadFailed = false; _restoreFailure = ""; _lastIssue = ""; _analysisKey = "";
            _lastSaved = JsonSerializer.Serialize(fresh.WithoutCapturedAuthority());
            Refresh(_language, _text);
            Receipt("integration.recovered_draft");
        }
        catch (Exception ex) { if (_status is not null) _status.Text = Explain(ex); }
    }
    private void UpdateDraftStatus()
    {
        if (!IsVisibleInTree() || _persistence.Runtime.Receiving) return;
        WorkbenchSearchDraft draft;
        try
        {
            // An unreadable saved request is not a failed optional save. The
            // fallback editor must not silently stand in for its unknown intent.
            if (_loadFailed) throw new InvalidOperationException(_restoreFailure.Length == 0 ? "integration.load_shape" : _restoreFailure);
            _ = RolltheSpire2.Compatibility.ProfileSeedGenerator.CreateProbeSeed(_runtime.Profile);
            draft = CaptureDraft().WithoutCapturedAuthority();
            if (_editorContextIssue.Length > 0) throw new InvalidOperationException(_editorContextIssue);
        }
        catch (Exception exception)
        {
            _lastIssue = exception.Message;
            if (_start is not null) _start.Disabled = true;
            if (_status is not null) _status.Text = Explain(exception);
            return;
        }
        if (_start is not null) _start.Disabled = _presetBlocked.Count > 0 || _persistence.Runtime.HasSearchInFlight;
        TryAuxiliary(() =>
        {
            string serialized = JsonSerializer.Serialize(draft);
            UpdateProbabilityPanel(draft, serialized);
            if (serialized != _lastSaved && !_loadFailed && serialized != _restoredCaptureStamp)
            {
                _restoredCaptureStamp = null;
                TryAuxiliary(() => { _persistence.SaveWorkbench(draft); _lastSaved = serialized; }, "DraftSave");
                TryAuxiliary(UpdateResultNavigation, "ResultNavigation");
            }
        }, "DraftPresentation");
        if (_persistence.Workspace.AuxiliaryIssues.Count > 0 || _lastIssue.Length > 0) PresentSearchStatus(null);
        _lastIssue = "";
    }

    private void StartSearch()
    {
        if (_presetBlocked.Count > 0 || _persistence.Runtime.HasSearchInFlight) return;
        try
        {
            if (_loadFailed) throw new InvalidOperationException(_restoreFailure.Length == 0 ? "integration.load_shape" : _restoreFailure);
            if (_editorContextIssue.Length > 0) throw new InvalidOperationException(_editorContextIssue);
            var feasibility = _persistence.Runtime.Start(CaptureDraft(), _runtime, _target, SearchWorkers, IsInsideTree() ? GetTree() : null);
            if (feasibility.IsImpossible)
            {
                ReceiptText(RolltheSpire2.Presentation.Ui1.SearchImpossibilityPresentation.Format(feasibility.Proof,
                    RolltheSpire2.Presentation.Localization.JsonUiTextProvider.Create(_language)));
                return;
            }
        }
        catch (Exception exception)
        {
            if (_status is not null) _status.Text = Explain(exception);
            return;
        }
        _restoredCaptureStamp = null;
        _probabilityPreview.Invalidate(); _probabilityPending = false;
        _observedResultBatchId = _persistence.Workspace.ResultBatchId;
        _observedResultsRevision = _persistence.ResultsRevision;
        _showResults = true;
        TryAuxiliary(() => ObserveWorkspaceRuntime(_persistence.Runtime.Snapshot), "StartProgress");
        TryAuxiliary(() => LockForRun(true), "StartControls");
        TryAuxiliary(RenderResults, "StartResults");
    }

    private void LockForRun(bool running)
    {
        if (_currentDomain is not null) _currentDomain.Visible = !_showResults;
        if (!running)
        {
            foreach(var (button,disabled) in _runBlocked) if(GodotObject.IsInstanceValid(button)) button.Disabled=disabled;
            _runBlocked.Clear();
        }
        else
        {
            foreach(var child in FindChildren("*", "BaseButton", true, false))
                if(child is BaseButton button && button != _start && button != _savedResultsEntry && !(_runtimeSurface?.IsAncestorOf(button) ?? false) && !(_familyRows?.IsAncestorOf(button) ?? false))
                { _runBlocked.TryAdd(button,button.Disabled); button.Disabled=true; }
        }
        if(_targetCount is not null) _targetCount.Editable=!running;
        if(_planSelector is not null) _planSelector.Disabled=running;
        if(_start is not null) { if(running) _start.Disabled=false; _start.Text=_text.Get(running?"integration.stop":"query.action.start"); }
        if(_runtimeSurface is not null) _runtimeSurface.Visible=_showResults;
        if(_showResults) foreach(var editor in new Control[] {_neowEditor,_combatEditor,_relicEditor,_ancientEditor,_eventEditor,_actInformationEditor,_transformationEditor,_shopEditor}) editor.Hide();
        UpdateResultNavigation();
    }

    private void UpdateResultNavigation()
    {
        UpdateConditionActions();
        if (_savedResultsEntry is not null && GodotObject.IsInstanceValid(_savedResultsEntry))
        {
            int count = ResultCount;
            if (!_showResults && !_persistence.Runtime.Receiving)
            {
                try { count = _persistence.ResultCountForQuery(CaptureDraft()); }
                catch { count = 0; }
            }
            _savedResultsEntry.Text = _text.Format("workflow.results.view_last", count);
            SearchTooltip.Set(_savedResultsEntry, _text.Get("workflow.results.saved_hint"));
        }
        if (_familyHeading is not null && GodotObject.IsInstanceValid(_familyHeading))
            _familyHeading.Text = _text.Get("workflow.family_estimates");
    }

    internal void ShowLastResults()
    {
        if (!_persistence.Runtime.Receiving)
        {
            try
            {
                _persistence.EnsureEnvironment(SearchEnvironmentSignatureBuilder.Capture(_runtime));
                _persistence.ActivateQueryResults(CaptureDraft());
            }
            catch (Exception ex) { ReceiptText(Explain(ex)); return; }
        }
        SyncSavedResults();
        _showResults = true;
        _expectationContext = "";
        RenderResults();
        LockForRun(_persistence.Runtime.Receiving);
    }
    private void RenderResults()
    {
        if(_resultRows is null) return;
        if (_start is not null && !_persistence.Runtime.Receiving) _start.Disabled = _persistence.Runtime.HasManualCandidateValidation;
        foreach(var child in _resultRows.GetChildren()) { _resultRows.RemoveChild(child); child.QueueFree(); }
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        bool unverifiedMode = _persistence.Workspace.ResultsSkipExactValidation || _persistence.DisplayResults.Any(r => r.IsUnverified);
        int unverifiedCount = _persistence.DisplayResults.Count(r => r.IsUnverified);
        var title = _p.Label(unverifiedMode
            ? _text.Format("workflow.mixed_results_count", ResultCount - unverifiedCount, unverifiedCount)
            : _text.Format("workflow.results_count", ResultCount), 22);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        if(!_persistence.Runtime.Receiving)
        {
            var back=_p.Button(_text.Get("integration.edit"));
            back.CustomMinimumSize = new Vector2(136, 34);
            back.AddThemeFontSizeOverride("font_size", 15);
            back.Pressed+=()=> { _showResults=false; Refresh(_language,_text); };
            header.AddChild(back);
        }
        _resultRows.AddChild(header);
        if (!_persistence.Runtime.Receiving)
        {
            var retained = _p.Label(_text.Get("workflow.results.retained"), 15, true);
            retained.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _resultRows.AddChild(retained);
        }
        void AddWarning(string key)
        {
            var warning = _p.Label(_text.Get(key), 16, true);
            warning.AutowrapMode = TextServer.AutowrapMode.WordSmart; _resultRows.AddChild(warning);
        }
        if (_persistence.DisplayResults.Any(r => r.IsUnfiltered)) AddWarning("workflow.unfiltered_warning");
        if (_persistence.DisplayResults.Any(r => r.HasIncompleteValidation)) AddWarning("workflow.incomplete_warning");
        if (unverifiedMode && !_persistence.DisplayResults.Any(r => r.IsUnfiltered || r.HasIncompleteValidation))
            AddWarning("workflow.candidates_warning");
        RenderPendingCandidates(_resultRows);
        if (ResultCount == 0)
        {
            var empty = _p.Label(_text.Get(!_persistence.Runtime.Receiving ? "workflow.results.empty" : "workflow.results.waiting"), 17, true);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; _resultRows.AddChild(empty);
        }
        foreach(var saved in _persistence.DisplayResults)
        {
            var action = _persistence.ResultAction(saved);
            string seed=saved.Seed;
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(0, 42) };
            var panelStyle = _p.ContentBox(0);
            panelStyle.ContentMarginLeft = panelStyle.ContentMarginRight = 12;
            panelStyle.ContentMarginTop = panelStyle.ContentMarginBottom = 3;
            panel.AddThemeStyleboxOverride("panel", panelStyle);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var seedLabel = _p.Label(seed + (unverifiedMode ? " · " + _text.Get(
                RolltheSpire2.Ui.Components.SearchResultValidationDisplay.StatusKey(saved)) : ""), 17);
            seedLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            seedLabel.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(seedLabel);
            var copy = _p.Button(_text.Get("integration.copy"));
            copy.CustomMinimumSize = new Vector2(72, 32);
            copy.AddThemeFontSizeOverride("font_size", 14);
            copy.Pressed += () => { DisplayServer.ClipboardSet(seed); if(_status is not null) _status.Text=_text.Get("integration.copied"); };
            row.AddChild(copy);
            row.AddChild(FavoriteResultButton(action));
            var analyze = _p.Button(_text.Get("integration.analyze"));
            analyze.CustomMinimumSize = new Vector2(72, 32);
            analyze.AddThemeFontSizeOverride("font_size", 14);
            analyze.Disabled = action.Live is null && action.Context is null;
            if (analyze.Disabled) SearchTooltip.Set(analyze, _text.Get("workflow.results.missing_context"));
            analyze.Pressed += () => OpenResultRequested?.Invoke(action);
            row.AddChild(analyze);
            if (saved.IsUnverified)
            {
                var validate = _p.Button(_text.Get("workflow.validate"));
                validate.CustomMinimumSize = new(72, 32); validate.AddThemeFontSizeOverride("font_size", 14);
                validate.Disabled = _persistence.Runtime.Receiving || _persistence.Runtime.HasManualCandidateValidation ||
                    _persistence.Runtime.HasSearchInFlight || _persistence.Runtime.HasManualCandidateDelivery(seed) ||
                    action.Live is null && (action.Draft is null || action.Context is null);
                if (action.Live is null && validate.Disabled) SearchTooltip.Set(validate, _text.Get("workflow.results.validation_unavailable"));
                validate.Pressed += () => ValidateCandidate(action); row.AddChild(validate);
            }
            var resultColumn = new VBoxContainer(); resultColumn.AddChild(row);
            foreach (var source in saved.TreasureSources)
            {
                if (!ModelKey.TryParseExact(source.RelicKey, out ModelKey relic) || !source.ObtainedAtOpening && source.EncounterOrdinal is null) continue;
                string name = RolltheSpire2.Infrastructure.ContentNames.RuntimeGameContentNameResolver.Create(_language)
                    .Resolve(relic, RolltheSpire2.Presentation.ContentNames.GameContentKind.Relic);
                string origin = source.ObtainedAtOpening ? _text.Get("query.treasure.source.opening") :
                    _text.Format("query.treasure.encounter", source.EncounterOrdinal);
                resultColumn.AddChild(_p.Label(_text.Format("query.treasure.source.result", source.TargetSlot, name, origin), 14, true));
            }
            RolltheSpire2.Ui.Components.SearchResultValidationDisplay.AddReason(resultColumn, saved, _p, _text);
            if (saved.ExecutionWarnings.Count > 0)
                AddFailureDetails(resultColumn, _text.Get("workflow.execution_warning"), string.Join("\n", saved.ExecutionWarnings));
            panel.AddChild(resultColumn);
            _resultRows.AddChild(panel);
        }
        if(_runtimeSurface is not null) _runtimeSurface.Visible=_showResults;
        UpdateRuntimeStats();
        UpdateResultNavigation();
    }
    private long _observedRunRevision = -1;
    private bool _observedReceiving;
    private void ObserveWorkspaceRuntime(SearchWorkspaceRuntimeSnapshot snapshot)
    {
        if (_observedRunRevision != snapshot.RunRevision)
        {
            _observedRunRevision = snapshot.RunRevision;
            if (snapshot.Eta is { } eta && snapshot.Progress is { } initial) BeginRuntimeProgress(eta, initial);
        }
        if (snapshot.Progress is { } progress) ObserveRuntimeProgress(progress);
        if (_observedReceiving != snapshot.Receiving)
        { _observedReceiving = snapshot.Receiving; LockForRun(snapshot.Receiving); }
    }
    private void PollProduction(double delta)
    {
        // Work is already received/checkpointed by the shared owner. This page
        // reads only metadata and renders; its lifetime never owns execution.
        if (!IsVisibleInTree()) return;
        var snapshot = _persistence.Runtime.Snapshot;
        TryAuxiliary(() => ObserveWorkspaceRuntime(snapshot), "ProgressPresentation");
        if (snapshot.Receiving || _showResults)
        {
            TryAuxiliary(UpdateRuntimeStats, "RuntimeStats");
            if (snapshot.Receiving || _receiptRemaining <= 0)
                TryAuxiliary(() => PresentSearchStatus(snapshot.Progress), "RunStatus");
        }
        if (_observedResultsRevision != _persistence.ResultsRevision)
            TryAuxiliary(() =>
            {
                SyncSavedResults();
                if (_showResults) RenderResults();
                _observedResultsRevision = _persistence.ResultsRevision;
            }, "ResultPresentation");
        TryAuxiliary(PollCandidateValidation, "ManualValidationPresentation");
        if (_receiptRemaining > 0 && !snapshot.Receiving)
        {
            _receiptRemaining -= delta;
            if (_receiptRemaining <= 0 && string.IsNullOrEmpty(_lastIssue))
                TryAuxiliary(() => PresentSearchStatus(_lastProgress), "ReceiptPresentation");
        }
        _saveTick += delta;
        if (_saveTick < .75 || !_restored) return;
        _saveTick = 0;
        TryAuxiliary(() =>
        {
            if (!snapshot.Receiving && !_showResults) UpdateDraftStatus();
            PollProbabilityPreview();
            var flags = CaptureUiPreferences();
            if (_savedUiDomain != _selectedDomain || _savedUiFlags is null ||
                flags.Count != _savedUiFlags.Count || flags.Any(pair => !_savedUiFlags.TryGetValue(pair.Key, out bool value) || value != pair.Value))
            {
                _persistence.SaveWorkbenchPreferences(flags, _selectedDomain);
                _savedUiFlags = flags; _savedUiDomain = _selectedDomain;
                _lastPreferences = JsonSerializer.Serialize(flags) + _selectedDomain;
            }
            if (_showResults && _receiptRemaining <= 0) PresentSearchStatus(_lastProgress);
        }, "WorkbenchRefresh");
    }
    internal void CloseSearch()
    {
        SuspendProbabilityPreview();
        TryAuxiliary(CloseAllPresetModals, "ClosePresetPresentation");
        if (!_persistence.Runtime.Receiving && !_loadFailed) SaveDraft();
        _persistence.Runtime.Close(IsInsideTree() ? GetTree() : null);
    }

    private void Receipt(string key)
    { if(_status is not null) _status.Text=_text.Get(key); _receiptRemaining=5; }
}
