using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class QueryWorkbenchFrame
{
    private Button? _clearConditions, _undoClearConditions;
    private HBoxContainer? _clearConditionsNotice;
    private Action? _restoreClearedConditions;
    private string _clearConditionsContext = "";
    private string? _clearUndoStamp;
    private Button? _clearCategory;
    private string ConditionUndoStamp() { try { return System.Text.Json.JsonSerializer.Serialize(CaptureDraft()); } catch { return "incomplete"; } }

    private string ClearConditionsContext() => _multiplayer
        ? $"party:{_playerCount}:{_partyAscension}:{string.Join(",", _seatCharacters.Take(_playerCount))}:{_editorPartyKey}"
        : $"solo:{_soloCharacter}:{_soloAscension}";

    private bool HasAuthoredConditions() => _neowEditor.HasConditions(_multiplayer) ||
        _combatEditor.HasConditions(_multiplayer) || _shopEditor.HasConditions(_multiplayer) ||
        _relicEditor.HasConditions(_multiplayer) || _ancientEditor.HasConditions(_multiplayer) ||
        _eventEditor.HasConditions(_multiplayer) || _actInformationEditor.HasConditions(_multiplayer) ||
        !_multiplayer && _transformationEditor.HasConditions();

    private bool CanChangeConditions => _session is null && _presetBlocked.Count == 0 && _configBlocked.Count == 0;

    private void BuildConditionActions()
    {
        _clearConditions = new ClearConditionsButton(_text.Get("workflow.conditions.clear"));
        _clearConditions.Name = "ClearAllConditions";
        _clearConditions.CustomMinimumSize = new(88, ConditionActionsHeight);
        _clearConditions.TooltipText = _text.Get(_multiplayer ? "workflow.conditions.clear.party_hint" : "workflow.conditions.clear.hint");
        _clearConditions.Pressed += ClearAllConditions;
        Place(_clearConditions, 0, ConditionActionsTop, LeftRailWidth - 8, ConditionActionsHeight);
        _clearConditionsNotice = new HBoxContainer { Name = "ConditionsClearedNotice" };
        _clearConditionsNotice.AddThemeConstantOverride("separation", 8);
        Place(_clearConditionsNotice, 0, ConditionActionsTop, LeftRailWidth - 8, ConditionActionsHeight);
        var notice = _p.Label(_text.Get("workflow.conditions.cleared"), 14, true);
        notice.SizeFlagsHorizontal = SizeFlags.ExpandFill; notice.VerticalAlignment = VerticalAlignment.Center;
        notice.ClipText = true; notice.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _clearConditionsNotice.AddChild(notice);
        _undoClearConditions = _p.CompactButton(_text.Get("workflow.conditions.undo"), ConditionActionsHeight, 14);
        _undoClearConditions.Name = "UndoClearConditions";
        _undoClearConditions.TooltipText = "";
        _undoClearConditions.CustomMinimumSize = new(72, ConditionActionsHeight);
        _undoClearConditions.Pressed += UndoClearConditions; _clearConditionsNotice.AddChild(_undoClearConditions);
        UpdateConditionActions();
    }

    // Keep Godot's hover delay, placement and dismissal, with a bounded workspace-styled body.
    private sealed partial class ClearConditionsButton : Button
    {
        public ClearConditionsButton(string text)
        {
            var palette = WorkspacePalette.Canonical;
            Text = text; ClipText = true;
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            using var donor = palette.CompactButton(text, ConditionActionsHeight, 16);
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
                AddThemeStyleboxOverride(state, donor.GetThemeStylebox(state));
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_disabled_color" })
                AddThemeColorOverride(state, donor.GetThemeColor(state));
            AddThemeFontSizeOverride("font_size", 16);
            // Style the native wrapper itself; a second PanelContainer leaves its black backing visible.
            var box = palette.Box(palette.Surface, palette.Line, 1);
            box.ContentMarginLeft = box.ContentMarginRight = 12;
            box.ContentMarginTop = box.ContentMarginBottom = 10;
            Theme = new Godot.Theme();
            Theme.SetStylebox("panel", "TooltipPanel", box);
        }

        public override GodotObject _MakeCustomTooltip(string forText)
        {
            if (string.IsNullOrEmpty(forText)) return null!;
            var label = WorkspacePalette.Canonical.Label(forText, 14);
            label.CustomMinimumSize = new(252, 0);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            return label;
        }
    }

    private void UpdateConditionActions()
    {
        bool hasConditions = HasAuthoredConditions();
        if (_clearCategory is not null)
        {
            _clearCategory.Visible = !_showResults;
            _clearCategory.Disabled = !CanChangeConditions;
            _clearCategory.TooltipText = _text.Get(_multiplayer && _selectedDomain is "ancient" or "events" or "boss" or "map"
                ? "workflow.conditions.clear_category.shared" : "workflow.conditions.clear_category.personal");
        }
        if (_restoreClearedConditions is not null &&
            (_clearConditionsContext != ClearConditionsContext() || _session is not null || _clearUndoStamp is not null && _clearUndoStamp != ConditionUndoStamp()))
            _restoreClearedConditions = null;
        if (_clearConditions is not null)
        {
            _clearConditions.Disabled = !CanChangeConditions || !hasConditions;
            _clearConditions.Visible = _restoreClearedConditions is null;
        }
        if (_clearConditionsNotice is not null) _clearConditionsNotice.Visible = _restoreClearedConditions is not null;
        if (_undoClearConditions is not null) _undoClearConditions.Disabled = !CanChangeConditions || _restoreClearedConditions is null;
    }

    internal void ClearAllConditions()
    {
        if (!CanChangeConditions || !HasAuthoredConditions()) return;
        var restores = new List<Action>
        {
            _neowEditor.ClearConditions(_multiplayer), _combatEditor.ClearConditions(_multiplayer),
            _shopEditor.ClearConditions(_multiplayer), _relicEditor.ClearConditions(_multiplayer),
            _ancientEditor.ClearConditions(_multiplayer), _eventEditor.ClearConditions(_multiplayer),
            _actInformationEditor.ClearConditions(_multiplayer)
        };
        if (!_multiplayer) restores.Add(_transformationEditor.ClearConditions());
        _restoreClearedConditions = () => { foreach (var restore in restores) restore(); };
        _clearConditionsContext = ClearConditionsContext();
        _clearUndoStamp = null;
        RefreshAfterConditionChange();
    }

    internal void UndoClearConditions()
    {
        UpdateConditionActions();
        if (!CanChangeConditions || _restoreClearedConditions is not { } restore) return;
        _restoreClearedConditions = null;
        restore();
        RefreshAfterConditionChange();
        Receipt("workflow.conditions.restored");
    }

    internal void ClearCategoryConditions()
    {
        if (!CanChangeConditions) return;
        var restoreTransformation = !_multiplayer && _selectedDomain is "neow" or "events"
            ? _transformationEditor.PreserveConditionsForUndo() : null;
        int slot = _multiplayer ? _seat : 0;
        _restoreClearedConditions = _selectedDomain switch
        {
            "neow" => _neowEditor.ClearConditions(_multiplayer, slot),
            "combat" => _combatEditor.ClearConditions(_multiplayer, slot),
            "shop" => _shopEditor.ClearConditions(_multiplayer, slot),
            "relics" => _relicEditor.ClearConditions(_multiplayer, slot),
            "ancient" => _ancientEditor.ClearConditions(_multiplayer),
            "events" => _eventEditor.ClearConditions(_multiplayer),
            "map" => _actInformationEditor.ClearPageConditions(_multiplayer, true),
            "boss" => _actInformationEditor.ClearPageConditions(_multiplayer, false),
            "transform" => _transformationEditor.ClearConditions(),
            _ => null
        };
        if (restoreTransformation is not null && _restoreClearedConditions is { } restorePage)
            _restoreClearedConditions = () => { restorePage(); restoreTransformation(); };
        _clearConditionsContext = ClearConditionsContext(); _clearUndoStamp = null;
        RefreshAfterConditionChange();
    }

    private void RefreshAfterConditionChange()
    {
        _probabilityPreview.Invalidate(); _probabilityPending = false; _analysisKey = "";
        _familyEntries.Clear(); _draftCompileIssue = ""; _lastIssue = ""; _restoreAdjusted = false;
        _showResults = false;
        Refresh(_language, _text);
        SaveDraft();
        _persistence.FlushAll();
        if (_restoreClearedConditions is not null) _clearUndoStamp = ConditionUndoStamp();
        UpdateConditionActions();
    }
}
