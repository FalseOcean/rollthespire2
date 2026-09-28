using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

/// <summary>
/// Detail-side renderer for the active Neow route. Per-relic effect editor state
/// is retained in UI-only history while inactive; only editors created for the
/// current normal route or current Bones pair are compiled into Search.
/// </summary>
internal sealed partial class NeowRouteEffectCardHost : VBoxContainer
{
    private readonly IGameIconResolver _icons;
    private readonly Action<RelicPickerRequest> _openPicker;
    private readonly Dictionary<ModelKey, RelicEffectDraftState> _history = new(ModelKeyComparer.Instance);
    private readonly List<ActiveEditorBinding> _activeEditors = new();
    private readonly List<Button> _effectClearButtons = new();
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private NeowSearchUiCatalog _catalog = NeowSearchUiCatalog.Empty(
        RuntimeProfileId.Unsupported,
        BaseGameModelKeys.Characters.Silent,
        "not-bound");
    private ModelKey? _activeRouteRelic;
    private ModelKey[] _activeBonesRelics = Array.Empty<ModelKey>();
    private BonesRouteOrderMode _activeBonesOrderMode = BonesRouteOrderMode.AnyOrder;
    private NeowModelKeySlot? _finalCurseSlot;
    private ModelKey? _bonesFinalCurse;
    private CheckButton? _bonesOrderToggle;
    private Button? _bonesSwapOrder;
    private bool _enabled = true;
    private bool _combinedCapsules, _capsuleRestoreConflict, _restoringCapsules;
    private Button[] _capsuleModeButtons = [];
    private bool HasDualCapsules => _activeRouteRelic == BaseGameModelKeys.Relics.NeowsBones &&
        _activeBonesRelics.Length == 2 && _activeBonesRelics.Contains(BaseGameModelKeys.Relics.SmallCapsule) &&
        _activeBonesRelics.Contains(BaseGameModelKeys.Relics.LargeCapsule);

    private void SelectCapsuleMode(bool combined)
    {
        CaptureActiveHistory();
        _combinedCapsules = combined;
        _capsuleRestoreConflict = false;
        Rebuild();
        Changed?.Invoke();
    }

    public NeowRouteEffectCardHost(IGameIconResolver icons, Action<RelicPickerRequest> openPicker)
    {
        _icons = icons;
        _openPicker = openPicker;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
    }

    public event Action? Changed;
    public event Action<BonesRouteOrderMode>? BonesOrderModeChanged;
    public event Action? BonesSwapRequested;

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        CaptureActiveHistory();
        _text = text;
        _names = names;
        Rebuild();
    }

    /// <summary>
    /// Returns true when a character-specific special UI state had to be
    /// explicitly cleared because the new production catalog cannot express it.
    /// </summary>
    public bool BindCatalog(NeowSearchUiCatalog catalog)
    {
        CaptureActiveHistory();
        bool clearedInvalidState = catalog.EffectAuthorityExact && SanitizeHistoryForCatalog(catalog);
        _catalog = catalog;
        Rebuild();
        return clearedInvalidState;
    }

    private bool SanitizeHistoryForCatalog(NeowSearchUiCatalog catalog)
    {
        bool changed = false;
        foreach (ModelKey source in _history.Keys.ToArray())
        {
            if (!catalog.RouteRelics.Contains(source, ModelKeyComparer.Instance))
            {
                _history.Remove(source);
                changed = true;
                continue;
            }

            RelicEffectDraftState previous = _history[source];
            NeowEffectCardDefinition definition = NeowEffectCardRegistry.Get(source);
            var sanitizedEditors = new List<NeowEditorState>(previous.Editors.Count);
            for (int i = 0; i < previous.Editors.Count; i++)
            {
                NeowEditorState state = previous.Editors[i];
                if (state is GenericEditorState generic && i < definition.Components.Count)
                {
                    IReadOnlyList<ModelKey> allowed = catalog.Candidates(definition.Components[i].CandidatePool);
                    ModelKey?[] slots = generic.Slots
                        .Select(key => key.HasValue && allowed.Contains(key.Value, ModelKeyComparer.Instance)
                            ? key
                            : null)
                        .ToArray();
                    if (!NullableKeysEqual(generic.Slots, slots)) changed = true;
                    sanitizedEditors.Add(new GenericEditorState(slots, generic.KaleidoscopeOrder));
                    continue;
                }

                if (state is ScrollBoxesEditorState scroll)
                {
                    int mode = scroll.Mode == 1 && catalog.CharacterKey == BaseGameModelKeys.Characters.Defect ? 1 : 0;
                    ModelKey? uncommon = KeepIfAllowed(scroll.Uncommon, catalog.UncommonCharacterCards);
                    ModelKey? commonA = KeepIfAllowed(scroll.CommonA, catalog.CommonCharacterCards);
                    ModelKey? commonB = KeepIfAllowed(scroll.CommonB, catalog.CommonCharacterCards);
                    if (mode != scroll.Mode || uncommon != scroll.Uncommon || commonA != scroll.CommonA || commonB != scroll.CommonB)
                        changed = true;
                    sanitizedEditors.Add(new ScrollBoxesEditorState(mode, uncommon, commonA, commonB));
                    continue;
                }

                sanitizedEditors.Add(state);
            }

            var sanitized = new RelicEffectDraftState(sanitizedEditors);
            if (sanitized.HasAnyValue)
                _history[source] = sanitized;
            else
                _history.Remove(source);
        }

        if (_bonesFinalCurse.HasValue && !catalog.Curses.Contains(_bonesFinalCurse.Value, ModelKeyComparer.Instance))
        {
            _bonesFinalCurse = null;
            changed = true;
        }
        return changed;
    }

    private static ModelKey? KeepIfAllowed(ModelKey? key, IReadOnlyList<ModelKey> allowed) =>
        key.HasValue && allowed.Contains(key.Value, ModelKeyComparer.Instance) ? key : null;

    private static bool NullableKeysEqual(IReadOnlyList<ModelKey?> left, IReadOnlyList<ModelKey?> right)
    {
        if (left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i]) return false;
        }
        return true;
    }

    public void SetActiveContext(
        ModelKey? routeRelic,
        IReadOnlyList<ModelKey> bonesRelics,
        BonesRouteOrderMode bonesOrderMode)
    {
        ModelKey[] normalizedBones = bonesRelics
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .Take(2)
            .ToArray();
        if (_activeRouteRelic == routeRelic &&
            _activeBonesRelics.SequenceEqual(normalizedBones, ModelKeyComparer.Instance) &&
            _activeBonesOrderMode == bonesOrderMode)
        {
            return;
        }
        CaptureActiveHistory();
        _activeRouteRelic = routeRelic is { IsValid: true } ? routeRelic : null;
        _activeBonesRelics = normalizedBones;
        _activeBonesOrderMode = bonesOrderMode;
        if (!HasDualCapsules) { _combinedCapsules = false; _capsuleRestoreConflict = false; }
        Rebuild();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Modulate = enabled ? Colors.White : new Color(1f, 1f, 1f, 0.58f);
        foreach (ActiveEditorBinding binding in _activeEditors) binding.Editor.SetEnabled(enabled);
        _finalCurseSlot?.SetEnabled(enabled);
        if (_bonesOrderToggle is not null) _bonesOrderToggle.Disabled = !enabled || _activeBonesRelics.Length != 2;
        if (_bonesSwapOrder is not null)
        {
            _bonesSwapOrder.Disabled = !enabled ||
                _activeBonesOrderMode != BonesRouteOrderMode.ExactOrder ||
                _activeBonesRelics.Length != 2;
        }
        foreach (Button button in _effectClearButtons.Concat(_capsuleModeButtons)) button.Disabled = !enabled;
    }

    public bool TryBuildConditions(
        out IReadOnlyList<NeowStructuredEffectSearchCondition> conditions,
        out string issue,
        bool focusInvalid)
    {
        var output = new List<NeowStructuredEffectSearchCondition>();
        if (HasDualCapsules && _capsuleRestoreConflict) {
            conditions = []; issue = _text!.Get(Ui1TextKey.SearchNeowCapsuleModeConflict); return false;
        }
        if (_activeRouteRelic == BaseGameModelKeys.Relics.NeowsBones &&
            _activeBonesRelics.Any(key => !_catalog.BonesNeowRelics.Contains(key, ModelKeyComparer.Instance)))
        {
            conditions = Array.Empty<NeowStructuredEffectSearchCondition>();
            issue = _text?.Get(Ui1TextKey.SearchNeowConditionUnavailable) ?? "Condition unavailable.";
            return false;
        }

        foreach (ActiveEditorBinding binding in _activeEditors)
        {
            if (!binding.Editor.TryBuild(out NeowStructuredEffectSearchCondition? condition, out issue))
            {
                if (focusInvalid) binding.Editor.FocusInvalid();
                conditions = Array.Empty<NeowStructuredEffectSearchCondition>();
                return false;
            }
            if (condition is not null) output.Add(condition);
        }

        if (_activeRouteRelic == BaseGameModelKeys.Relics.NeowsBones && _finalCurseSlot?.SelectedKey is { } curse)
        {
            if (!_catalog.Curses.Contains(curse, ModelKeyComparer.Instance))
            {
                conditions = Array.Empty<NeowStructuredEffectSearchCondition>();
                issue = _text?.Get(Ui1TextKey.SearchNeowConditionUnavailable) ?? "Condition unavailable.";
                if (focusInvalid) _finalCurseSlot.GrabSlotFocus();
                return false;
            }
            output.Add(new NeowStructuredEffectSearchCondition(
                BaseGameModelKeys.Relics.NeowsBones,
                NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.FinalCurse,
                NeowStructuredOutputKind.Curse,
                new[] { curse }));
        }

        conditions = output;
        issue = string.Empty;
        return true;
    }

    public void RestoreConditions(IReadOnlyList<NeowStructuredEffectSearchCondition>? conditions, bool notify)
    {
        foreach (ActiveEditorBinding binding in _activeEditors) binding.Editor.Clear();
        _history.Clear();
        _bonesFinalCurse = null;
        _finalCurseSlot?.Clear(notify: false);

        bool grouped = conditions?.Any(c => c.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset && !c.IsEmpty) == true;
        bool separate = conditions?.Any(c => !c.IsEmpty && c.Scope == NeowStructuredEffectScope.NestedRelics &&
            (c.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule || c.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule)) == true;
        _combinedCapsules = grouped;
        _capsuleRestoreConflict = grouped && separate;
        _restoringCapsules = HasDualCapsules;
        Rebuild();
        foreach (NeowStructuredEffectSearchCondition condition in conditions ?? Array.Empty<NeowStructuredEffectSearchCondition>())
        {
            if (condition.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
                condition.Scope == NeowStructuredEffectScope.FinalCurse &&
                condition.OutputKind == NeowStructuredOutputKind.Curse &&
                condition.OutputKeys.Count > 0)
            {
                ModelKey curse = condition.OutputKeys[0];
                if (_catalog.Curses.Contains(curse, ModelKeyComparer.Instance))
                {
                    _bonesFinalCurse = curse;
                    _finalCurseSlot?.Select(curse, notify: false);
                }
                continue;
            }

            foreach (ActiveEditorBinding item in _activeEditors)
            {
                if (item.SourceRelicKey != condition.SourceRelicKey)
                    continue;
                if (item.Editor.TryRestoreCondition(condition))
                    break;
            }
        }
        CaptureActiveHistory();
        _restoringCapsules = false;
        Rebuild();
        if (notify) Changed?.Invoke();
    }

    public int ConditionCount
    {
        get
        {
            return TryBuildConditions(out IReadOnlyList<NeowStructuredEffectSearchCondition> conditions, out _, false)
                ? conditions.Count
                : 0;
        }
    }

    public bool HasSavedEffects
    {
        get
        {
            CaptureActiveHistory();
            return _bonesFinalCurse.HasValue || _history.Values.Any(state => state.HasAnyValue);
        }
    }

    public void ClearAllEffects()
    {
        _capsuleRestoreConflict = false;
        foreach (ActiveEditorBinding binding in _activeEditors) binding.Editor.Clear();
        _history.Clear();
        _bonesFinalCurse = null;
        _finalCurseSlot?.Clear(notify: false);
        Changed?.Invoke();
    }

    private void Rebuild()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _activeEditors.Clear();
        _effectClearButtons.Clear();
        _finalCurseSlot = null;
        _bonesOrderToggle = null;
        _bonesSwapOrder = null;
        _capsuleModeButtons = [];

        if (_text is null || _names is null) return;
        if (!_activeRouteRelic.HasValue)
        {
            AddChild(BuildUnconstrainedState());
            return;
        }
        if (_activeRouteRelic.Value == BaseGameModelKeys.Relics.NeowsBones)
        {
            AddChild(BuildBonesCard());
        }
        else
        {
            AddChild(BuildRelicCard(_activeRouteRelic.Value, childCard: false));
        }
        SetEnabled(_enabled);
    }

    private Control BuildUnconstrainedState()
    {
        var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 4);
        root.AddChild(Ui1Theme.Label(_text!.Get(Ui1TextKey.SearchNeowUnconstrainedTitle), Ui1TextRole.SectionTitle));
        root.AddChild(Ui1Theme.Label(_text.Get(Ui1TextKey.SearchNeowUnconstrainedBody), Ui1TextRole.Muted, true));
        return root;
    }

    private Control BuildBonesCard()
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(card, Ui1SurfaceRole.CardElevated, 4f, 1, 14f);
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        content.AddChild(BuildHeader(
            BaseGameModelKeys.Relics.NeowsBones,
            string.Format(_text!.Get(Ui1TextKey.SearchNeowBonesDetailSummary), _activeBonesRelics.Length),
            clearAction: null,
            BuildBonesHeaderControls()));

        if (_activeBonesRelics.Length == 0)
        {
            content.AddChild(Ui1Theme.Label(_text.Get(Ui1TextKey.SearchNeowBonesChooseHint), Ui1TextRole.Muted, true));
        }
        else
        {
            var children = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            children.AddThemeConstantOverride("separation", 10);
            for (int i = 0; i < _activeBonesRelics.Length && (!HasDualCapsules || !_combinedCapsules || _restoringCapsules); i++)
            {
                int? resultOrdinal = _activeBonesOrderMode == BonesRouteOrderMode.ExactOrder ? i + 1 : null;
                children.AddChild(BuildRelicCard(_activeBonesRelics[i], childCard: true, resultOrdinal));
            }
            content.AddChild(children);
            if (HasDualCapsules && _capsuleRestoreConflict)
                content.AddChild(Ui1Theme.Label(_text.Get(Ui1TextKey.SearchNeowCapsuleModeConflict), Ui1TextRole.Muted, true));

            if (HasDualCapsules && (_combinedCapsules || _restoringCapsules))
            {
                var grouped = new GenericConditionEditor(
                    _icons,
                    _openPicker,
                    BaseGameModelKeys.Relics.NeowsBones,
                    new NeowEffectComponentDefinition(
                        "grouped-capsule-outputs",
                        "Small + Large Capsule grouped outputs",
                        NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
                        NeowStructuredEffectScope.NestedRelics,
                        NeowStructuredOutputKind.Relic,
                        NeowCandidatePoolKind.OrdinaryRelics,
                        3,
                        Unordered: true,
                        AllowDuplicateOutputs: true),
                    _catalog,
                    _names!,
                    _text);
                RegisterEditor(BaseGameModelKeys.Relics.NeowsBones, grouped);
                content.AddChild(grouped.View);
                RestoreRelicState(BaseGameModelKeys.Relics.NeowsBones);
            }
        }

        var separator = new HSeparator();
        Ui1Theme.ApplySeparator(separator);
        content.AddChild(separator);
        _finalCurseSlot = new NeowModelKeySlot(_icons, _openPicker);
        _finalCurseSlot.Configure(
            _text.Get(Ui1TextKey.SearchNeowFinalCurseOptional),
            _catalog.Curses,
            GameContentKind.Card,
            IconVariant.Small,
            _names!,
            _text.Get(Ui1TextKey.SearchNeowPickerTitleCurse),
            string.Empty,
            pickerTitle: _text.Get(Ui1TextKey.SearchNeowPickerTitleCurse));
        _finalCurseSlot.Select(_bonesFinalCurse, notify: false);
        _finalCurseSlot.Changed += () =>
        {
            _bonesFinalCurse = _finalCurseSlot.SelectedKey;
            Changed?.Invoke();
        };
        content.AddChild(_finalCurseSlot);
        card.AddChild(content);
        return card;
    }

    private IReadOnlyList<Control> BuildBonesHeaderControls()
    {
        _bonesOrderToggle = new CheckButton
        {
            Text = _text!.Get(Ui1TextKey.SearchNeowBonesOrderedPickup),
            ButtonPressed = _activeBonesOrderMode == BonesRouteOrderMode.ExactOrder,
            Disabled = !_enabled || _activeBonesRelics.Length != 2
        };
        _bonesOrderToggle.Toggled += pressed =>
            BonesOrderModeChanged?.Invoke(pressed ? BonesRouteOrderMode.ExactOrder : BonesRouteOrderMode.AnyOrder);

        _bonesSwapOrder = new Button
        {
            Text = _text.Get(Ui1TextKey.SearchNeowSwapOrder),
            CustomMinimumSize = new Vector2(86, 34),
            Disabled = !_enabled ||
                _activeBonesOrderMode != BonesRouteOrderMode.ExactOrder ||
                _activeBonesRelics.Length != 2
        };
        Ui1Theme.ApplyButton(_bonesSwapOrder, Ui1ButtonRole.Secondary);
        _bonesSwapOrder.Pressed += () => BonesSwapRequested?.Invoke();
        if (!HasDualCapsules) return new Control[] { _bonesOrderToggle, _bonesSwapOrder };
        var modes = new HBoxContainer();
        modes.AddThemeConstantOverride("separation", 2);
        _capsuleModeButtons = new[] { false, true }.Select(combined => {
            var button = new Button {
                Text = _text.Get(combined ? Ui1TextKey.SearchNeowCapsuleCombined : Ui1TextKey.SearchNeowCapsuleSeparate),
                CustomMinimumSize = new Vector2(86, 34), Disabled = !_enabled
            };
            Ui1Theme.ApplyButton(button, !_capsuleRestoreConflict && _combinedCapsules == combined ? Ui1ButtonRole.Primary : Ui1ButtonRole.Secondary);
            button.Pressed += () => SelectCapsuleMode(combined);
            modes.AddChild(button);
            return button;
        }).ToArray();
        return new Control[] { modes, _bonesOrderToggle, _bonesSwapOrder };
    }

    private Control BuildRelicCard(ModelKey relic, bool childCard, int? resultOrdinal = null)
    {
        NeowEffectCardDefinition definition = NeowEffectCardRegistry.Get(relic);
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(
            card,
            childCard ? Ui1SurfaceRole.Card : Ui1SurfaceRole.CardElevated,
            4f,
            1,
            childCard ? 11f : 14f);
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);

        var editors = new List<INeowConditionEditor>();
        if (definition.HasEffectCard)
        {
            if (definition.Template == NeowEffectCardTemplate.ScrollBoxes)
            {
                editors.Add(new ScrollBoxesConditionEditor(_icons, _openPicker, relic, _catalog, _names!, _text!));
            }
            else
            {
                foreach (NeowEffectComponentDefinition component in definition.Components)
                {
                    editors.Add(new GenericConditionEditor(
                        _icons,
                        _openPicker,
                        relic,
                        component,
                        _catalog,
                        _names!,
                        _text!));
                }
            }
        }

        string summary = ResolveCardSummary(definition);
        if (!definition.HasEffectCard)
        {
            summary = childCard
                ? resultOrdinal.HasValue
                    ? string.Format(_text!.Get(Ui1TextKey.SearchNeowBonesResultOrdinal), resultOrdinal.Value)
                    : _text!.Get(Ui1TextKey.SearchNeowBonesResultSelected)
                : _text!.Get(Ui1TextKey.SearchNeowSelectedResult);
        }
        Control[] headerControls = editors.SelectMany(editor => editor.HeaderControls).ToArray();
        content.AddChild(BuildHeader(
            relic,
            summary,
            definition.HasEffectCard ? () => ClearRelicEffect(relic) : null,
            headerControls,
            resultOrdinal));

        foreach (INeowConditionEditor editor in editors) RegisterEditor(relic, editor);

        if (!definition.HasEffectCard)
        {
            content.AddChild(Ui1Theme.Label(_text!.Get(Ui1TextKey.SearchNeowIdentityOnly), Ui1TextRole.Muted, true));
        }
        else if (relic == BaseGameModelKeys.Relics.LostCoffer)
        {
            var resultRow = new HFlowContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
            };
            resultRow.AddThemeConstantOverride("h_separation", 12);
            resultRow.AddThemeConstantOverride("v_separation", 8);
            foreach (INeowConditionEditor editor in editors) resultRow.AddChild(editor.View);
            content.AddChild(resultRow);
        }
        else
        {
            foreach (INeowConditionEditor editor in editors) content.AddChild(editor.View);
        }

        RestoreRelicState(relic);
        card.AddChild(content);
        return card;
    }

    private Control BuildHeader(
        ModelKey relic,
        string summary,
        Action? clearAction,
        IReadOnlyList<Control>? primaryControls = null,
        int? resultOrdinal = null)
    {
        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 9);
        if (resultOrdinal.HasValue)
        {
            var ordinal = Ui1Theme.Label(resultOrdinal.Value == 1 ? "①" : "②", Ui1TextRole.Accent);
            ordinal.CustomMinimumSize = new Vector2(22, 48);
            ordinal.HorizontalAlignment = HorizontalAlignment.Center;
            ordinal.VerticalAlignment = VerticalAlignment.Center;
            header.AddChild(ordinal);
        }
        IconDescriptor icon = _icons.Resolve(relic, GameContentKind.Relic, IconVariant.RelicLarge);
        var iconHost = new Control { CustomMinimumSize = new Vector2(48, 48) };
        var texture = new TextureRect
        {
            Texture = icon.Texture,
            Visible = icon.Texture is not null,
            CustomMinimumSize = new Vector2(48, 48),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        texture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var missing = Ui1Theme.Label(icon.Texture is null ? "?" : string.Empty, Ui1TextRole.Muted);
        missing.HorizontalAlignment = HorizontalAlignment.Center;
        missing.VerticalAlignment = VerticalAlignment.Center;
        missing.MouseFilter = Control.MouseFilterEnum.Ignore;
        missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        iconHost.AddChild(texture);
        iconHost.AddChild(missing);

        var titles = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(110, 0)
        };
        titles.AddThemeConstantOverride("separation", 2);
        string relicName = _names!.Resolve(relic, GameContentKind.Relic);
        Label title = Ui1Theme.Label(relicName, Ui1TextRole.CardTitle);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        title.ClipText = true;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.TooltipText = relicName;
        titles.AddChild(title);
        titles.AddChild(Ui1Theme.Label(summary, Ui1TextRole.Muted, true));
        header.AddChild(iconHost);
        header.AddChild(titles);

        if ((primaryControls?.Count ?? 0) > 0 || clearAction is not null)
        {
            var actions = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                Alignment = BoxContainer.AlignmentMode.End
            };
            actions.AddThemeConstantOverride("separation", 6);
            foreach (Control control in primaryControls ?? Array.Empty<Control>()) actions.AddChild(control);
            if (clearAction is not null)
            {
                var clear = new Button
                {
                    Text = _text!.Get(Ui1TextKey.SearchNeowClearEffect),
                    CustomMinimumSize = new Vector2(86, 34)
                };
                Ui1Theme.ApplyButton(clear, Ui1ButtonRole.Ghost);
                clear.Pressed += clearAction;
                _effectClearButtons.Add(clear);
                actions.AddChild(clear);
            }
            header.AddChild(actions);
        }
        return header;
    }

    private void AddEditor(ModelKey source, INeowConditionEditor editor, VBoxContainer content)
    {
        RegisterEditor(source, editor);
        content.AddChild(editor.View);
    }

    private void RegisterEditor(ModelKey source, INeowConditionEditor editor)
    {
        editor.Changed += () =>
        {
            SaveRelicState(source);
            Changed?.Invoke();
        };
        editor.SetEnabled(_enabled);
        _activeEditors.Add(new ActiveEditorBinding(source, editor));
    }

    private void RestoreRelicState(ModelKey relic)
    {
        if (!_history.TryGetValue(relic, out RelicEffectDraftState? state)) return;
        INeowConditionEditor[] editors = _activeEditors
            .Where(binding => binding.SourceRelicKey == relic)
            .Select(binding => binding.Editor)
            .ToArray();
        for (int i = 0; i < Math.Min(editors.Length, state.Editors.Count); i++)
        {
            editors[i].RestoreState(state.Editors[i]);
        }
    }

    private void CaptureActiveHistory()
    {
        foreach (ModelKey source in _activeEditors
                     .Select(binding => binding.SourceRelicKey)
                     .Distinct(ModelKeyComparer.Instance))
        {
            SaveRelicState(source);
        }
        if (_finalCurseSlot is not null) _bonesFinalCurse = _finalCurseSlot.SelectedKey;
    }

    private void SaveRelicState(ModelKey source)
    {
        INeowConditionEditor[] editors = _activeEditors
            .Where(binding => binding.SourceRelicKey == source)
            .Select(binding => binding.Editor)
            .ToArray();
        if (editors.Length == 0) return;
        var state = new RelicEffectDraftState(editors.Select(editor => editor.CaptureState()).ToArray());
        if (state.HasAnyValue) _history[source] = state;
        else _history.Remove(source);
    }

    private void ClearRelicEffect(ModelKey source)
    {
        foreach (INeowConditionEditor editor in _activeEditors
                     .Where(binding => binding.SourceRelicKey == source)
                     .Select(binding => binding.Editor))
        {
            editor.Clear();
        }
        _history.Remove(source);
        Changed?.Invoke();
    }

    private string ResolveCardSummary(NeowEffectCardDefinition definition) => definition.Template switch
    {
        NeowEffectCardTemplate.Bones => _text!.Get(Ui1TextKey.SearchNeowEffectBones),
        NeowEffectCardTemplate.ScrollBoxes => _text!.Get(Ui1TextKey.SearchNeowEffectScrollBoxes),
        _ when definition.RouteRelicKey == BaseGameModelKeys.Relics.LargeCapsule => _text!.Get(Ui1TextKey.SearchNeowEffectRelicPair),
        _ when definition.RouteRelicKey == BaseGameModelKeys.Relics.Kaleidoscope => _text!.Get(Ui1TextKey.SearchNeowEffectIndependentOffers),
        _ when definition.RouteRelicKey == BaseGameModelKeys.Relics.LeafyPoultice => _text!.Get(Ui1TextKey.SearchNeowEffectTransformPair),
        _ => _text!.Get(Ui1TextKey.SearchNeowEffectOutput)
    };

    private interface INeowConditionEditor
    {
        Control View { get; }
        IReadOnlyList<Control> HeaderControls { get; }
        event Action? Changed;
        bool TryBuild(out NeowStructuredEffectSearchCondition? condition, out string issue);
        bool TryRestoreCondition(NeowStructuredEffectSearchCondition condition);
        void FocusInvalid();
        void Clear();
        void SetEnabled(bool enabled);
        NeowEditorState CaptureState();
        void RestoreState(NeowEditorState state);
    }

    private abstract record NeowEditorState
    {
        public abstract bool HasAnyValue { get; }
    }

    private sealed record GenericEditorState(
        IReadOnlyList<ModelKey?> Slots,
        KaleidoscopeGroupOrderMode KaleidoscopeOrder) : NeowEditorState
    {
        public override bool HasAnyValue => Slots.Any(key => key.HasValue);
    }

    private sealed record ScrollBoxesEditorState(
        int Mode,
        ModelKey? Uncommon,
        ModelKey? CommonA,
        ModelKey? CommonB) : NeowEditorState
    {
        public override bool HasAnyValue => Mode == 1 || Uncommon.HasValue || CommonA.HasValue || CommonB.HasValue;
    }

    private sealed record RelicEffectDraftState(IReadOnlyList<NeowEditorState> Editors)
    {
        public bool HasAnyValue => Editors.Any(editor => editor.HasAnyValue);
    }

    private sealed record ActiveEditorBinding(ModelKey SourceRelicKey, INeowConditionEditor Editor);

    private sealed class GenericConditionEditor : INeowConditionEditor
    {
        private readonly ModelKey _source;
        private readonly NeowEffectComponentDefinition _definition;
        private readonly IReadOnlyList<ModelKey> _candidates;
        private readonly List<INeowResultSlot> _slots = new();
        private readonly List<Label> _orderLabels = new();
        private readonly IUiTextProvider _text;
        private readonly CheckButton? _orderedToggle;
        private readonly Button? _swapOrder;
        private KaleidoscopeGroupOrderMode _kaleidoscopeOrder = KaleidoscopeGroupOrderMode.AnyOrder;
        private bool _enabled = true;
        public GenericConditionEditor(
            IGameIconResolver icons,
            Action<RelicPickerRequest> openPicker,
            ModelKey source,
            NeowEffectComponentDefinition definition,
            NeowSearchUiCatalog catalog,
            IGameContentNameResolver names,
            IUiTextProvider text)
        {
            _source = source;
            _definition = definition;
            _candidates = catalog.Candidates(definition.CandidatePool);
            _text = text;
            var root = new VBoxContainer
            {
                SizeFlagsHorizontal = definition.OutputKind is NeowStructuredOutputKind.Card or NeowStructuredOutputKind.Potion
                    ? Control.SizeFlags.ShrinkBegin
                    : Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
            };
            root.AddThemeConstantOverride("separation", 6);
            if (!definition.SupportsOrderSelection)
            {
                root.AddChild(Ui1Theme.Label(LocalizeDescription(definition, text), Ui1TextRole.Meta));
            }

            if (definition.SupportsOrderSelection)
            {
                _orderedToggle = new CheckButton
                {
                    Text = text.Get(Ui1TextKey.SearchNeowKaleidoscopeOrderedResults),
                    ButtonPressed = false
                };
                _orderedToggle.Toggled += pressed => SetKaleidoscopeOrder(
                    pressed ? KaleidoscopeGroupOrderMode.ExactOrder : KaleidoscopeGroupOrderMode.AnyOrder);
                _swapOrder = new Button
                {
                    Text = text.Get(Ui1TextKey.SearchNeowSwapOrder),
                    CustomMinimumSize = new Vector2(86, 34),
                    Disabled = true
                };
                Ui1Theme.ApplyButton(_swapOrder, Ui1ButtonRole.Secondary);
                _swapOrder.Pressed += SwapTargets;
                HeaderControls = new Control[] { _orderedToggle, _swapOrder };
            }
            else
            {
                HeaderControls = Array.Empty<Control>();
            }

            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = definition.OutputKind is NeowStructuredOutputKind.Card or NeowStructuredOutputKind.Potion
                    ? Control.SizeFlags.ShrinkBegin
                    : Control.SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 8);
            for (int i = 0; i < definition.SlotCount; i++)
            {
                INeowResultSlot slot = definition.OutputKind switch
                {
                    NeowStructuredOutputKind.Card => new SearchCardResultSlot(icons, openPicker),
                    NeowStructuredOutputKind.Potion => new SearchPotionResultSlot(icons, openPicker),
                    _ => new NeowModelKeySlot(icons, openPicker)
                };
                IReadOnlyDictionary<ModelKey, RelicPickerCategory>? categories = definition.OutputKind switch
                {
                    NeowStructuredOutputKind.Relic => _candidates.ToDictionary(
                        key => key,
                        key => PickerCategory(catalog.RelicRarity(key)),
                        ModelKeyComparer.Instance),
                    NeowStructuredOutputKind.Potion => _candidates.ToDictionary(
                        key => key,
                        key => PickerCategory(catalog.PotionRarity(key)),
                        ModelKeyComparer.Instance),
                    _ => null
                };
                slot.Configure(
                    string.Empty,
                    _candidates,
                    Kind(definition.OutputKind),
                    IconVariant.Small,
                    names,
                    definition.OutputKind switch
                    {
                        NeowStructuredOutputKind.Relic => text.Get(Ui1TextKey.SearchNeowChooseRelic),
                        NeowStructuredOutputKind.Potion => text.Get(Ui1TextKey.SearchNeowPickerTitlePotion),
                        _ => text.Get(Ui1TextKey.SearchNeowChooseCard)
                    },
                    definition.Unordered && !definition.SupportsOrderSelection
                        ? text.Get(Ui1TextKey.SearchNeowUnorderedTooltip)
                        : string.Empty,
                    categories,
                    PickerTitle(definition.OutputKind, text),
                    definition.OutputKind == NeowStructuredOutputKind.Card
                        ? catalog.CreateCardPickerContext(
                            _candidates,
                            definition.CandidatePool == NeowCandidatePoolKind.OtherCharacterCards,
                            $"neow:{source.Serialized}:{definition.CandidatePool}")
                        : null);
                slot.Changed += OnSlotChanged;
                _slots.Add(slot);

                if (definition.SupportsOrderSelection)
                {
                    var target = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
                    target.AddThemeConstantOverride("separation", 4);
                    var ordinal = Ui1Theme.Label(i == 0 ? "①" : "②", Ui1TextRole.Accent);
                    ordinal.CustomMinimumSize = new Vector2(22, 0);
                    ordinal.VerticalAlignment = VerticalAlignment.Center;
                    ordinal.Visible = false;
                    _orderLabels.Add(ordinal);
                    target.AddChild(ordinal);
                    target.AddChild(slot.View);
                    row.AddChild(target);
                }
                else
                {
                    row.AddChild(slot.View);
                }
            }
            root.AddChild(row);
            View = root;
            RefreshDuplicateExclusions();
            RefreshOrderControls();
        }

        public Control View { get; }
        public IReadOnlyList<Control> HeaderControls { get; }
        public event Action? Changed;

        public bool TryBuild(out NeowStructuredEffectSearchCondition? condition, out string issue)
        {
            ModelKey?[] values = _slots.Select(slot => slot.SelectedKey).ToArray();
            if (values.All(value => !value.HasValue))
            {
                condition = null;
                issue = string.Empty;
                return true;
            }
            bool allowsPartialTargets = _definition.SlotCount > 1;
            if (!allowsPartialTargets && values.Any(value => !value.HasValue))
            {
                condition = null;
                issue = _text.Get(Ui1TextKey.SearchNeowConditionIncomplete);
                return false;
            }
            ModelKey[] keys = values
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            if (!_definition.AllowDuplicateOutputs &&
                keys.Distinct(ModelKeyComparer.Instance).Count() != keys.Length)
            {
                condition = null;
                issue = _text.Get(Ui1TextKey.SearchNeowDuplicateNotAllowed);
                return false;
            }
            if (keys.Any(key => !_candidates.Contains(key, ModelKeyComparer.Instance)))
            {
                condition = null;
                issue = _text.Get(Ui1TextKey.SearchNeowConditionUnavailable);
                return false;
            }
            condition = new NeowStructuredEffectSearchCondition(
                _source,
                _definition.ConditionKind,
                _definition.Scope,
                _definition.OutputKind,
                keys,
                AllowDuplicateOutputs: _definition.AllowDuplicateOutputs,
                KaleidoscopeGroupOrder: _definition.SupportsOrderSelection
                    ? _kaleidoscopeOrder
                    : KaleidoscopeGroupOrderMode.AnyOrder)
            {
                KaleidoscopePositionalSlots = _definition.SupportsOrderSelection &&
                    _kaleidoscopeOrder == KaleidoscopeGroupOrderMode.ExactOrder
                    ? values
                    : Array.Empty<ModelKey?>()
            };
            issue = string.Empty;
            return true;
        }

        public void FocusInvalid() => _slots.FirstOrDefault(slot => slot.IsEmpty)?.GrabSlotFocus();

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            foreach (INeowResultSlot slot in _slots) slot.SetEnabled(enabled);
            RefreshOrderControls();
        }

        public void Clear()
        {
            foreach (INeowResultSlot slot in _slots) slot.Clear(notify: false);
            _kaleidoscopeOrder = KaleidoscopeGroupOrderMode.AnyOrder;
            RefreshDuplicateExclusions();
            RefreshOrderControls();
        }

        public bool TryRestoreCondition(NeowStructuredEffectSearchCondition condition)
        {
            if (condition.SourceRelicKey != _source || condition.Kind != _definition.ConditionKind ||
                condition.Scope != _definition.Scope || condition.OutputKind != _definition.OutputKind) return false;
            _kaleidoscopeOrder = _definition.SupportsOrderSelection
                ? condition.KaleidoscopeGroupOrder
                : KaleidoscopeGroupOrderMode.AnyOrder;
            foreach (INeowResultSlot slot in _slots) slot.Clear(notify: false);
            if (_definition.SupportsOrderSelection &&
                condition.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder &&
                condition.KaleidoscopePositionalSlots.Count == _slots.Count)
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    ModelKey? key = condition.KaleidoscopePositionalSlots[i];
                    if (key.HasValue && _candidates.Contains(key.Value, ModelKeyComparer.Instance))
                        _slots[i].Select(key.Value, notify: false);
                }
            }
            else
            {
                int slotIndex = 0;
                foreach (ModelKey key in condition.OutputKeys)
                {
                    if (slotIndex >= _slots.Count) break;
                    if (_candidates.Contains(key, ModelKeyComparer.Instance))
                        _slots[slotIndex++].Select(key, notify: false);
                }
            }
            RefreshDuplicateExclusions();
            RefreshOrderControls();
            return true;
        }

        public NeowEditorState CaptureState() => new GenericEditorState(
            _slots.Select(slot => slot.SelectedKey).ToArray(),
            _kaleidoscopeOrder);

        public void RestoreState(NeowEditorState state)
        {
            if (state is not GenericEditorState generic) return;
            _kaleidoscopeOrder = _definition.SupportsOrderSelection
                ? generic.KaleidoscopeOrder
                : KaleidoscopeGroupOrderMode.AnyOrder;
            for (int i = 0; i < _slots.Count; i++)
            {
                ModelKey? key = i < generic.Slots.Count ? generic.Slots[i] : null;
                _slots[i].Select(key, notify: false);
            }
            RefreshDuplicateExclusions();
            RefreshOrderControls();
        }

        private void SetKaleidoscopeOrder(KaleidoscopeGroupOrderMode mode)
        {
            if (!_definition.SupportsOrderSelection || _kaleidoscopeOrder == mode) return;
            _kaleidoscopeOrder = mode;
            RefreshOrderControls();
            Changed?.Invoke();
        }

        private void SwapTargets()
        {
            if (!_enabled || !_definition.SupportsOrderSelection ||
                _kaleidoscopeOrder != KaleidoscopeGroupOrderMode.ExactOrder || _slots.Count != 2) return;
            ModelKey? first = _slots[0].SelectedKey;
            ModelKey? second = _slots[1].SelectedKey;
            _slots[0].Select(second, notify: false);
            _slots[1].Select(first, notify: false);
            RefreshDuplicateExclusions();
            Changed?.Invoke();
        }

        private void RefreshOrderControls()
        {
            if (_orderedToggle is null || _swapOrder is null) return;
            bool ordered = _kaleidoscopeOrder == KaleidoscopeGroupOrderMode.ExactOrder;
            _orderedToggle.SetPressedNoSignal(ordered);
            _orderedToggle.Disabled = !_enabled;
            _swapOrder.Disabled = !_enabled || !ordered;
            foreach (Label label in _orderLabels) label.Visible = ordered;
        }

        private void OnSlotChanged()
        {
            RefreshDuplicateExclusions();
            Changed?.Invoke();
        }

        private void RefreshDuplicateExclusions()
        {
            ModelKey[] selected = _slots
                .Where(slot => slot.SelectedKey.HasValue)
                .Select(slot => slot.SelectedKey!.Value)
                .ToArray();
            foreach (INeowResultSlot slot in _slots)
            {
                slot.SetDisabledKeys(_definition.AllowDuplicateOutputs
                    ? Array.Empty<ModelKey>()
                    : selected.Where(key => slot.SelectedKey != key));
            }
        }

        private static RelicPickerCategory PickerCategory(EffectRelicRarity rarity) => rarity switch
        {
            EffectRelicRarity.Common => RelicPickerCategory.Common,
            EffectRelicRarity.Uncommon => RelicPickerCategory.Uncommon,
            EffectRelicRarity.Rare => RelicPickerCategory.Rare,
            EffectRelicRarity.Shop => RelicPickerCategory.Shop,
            _ => RelicPickerCategory.Other
        };

        private static RelicPickerCategory PickerCategory(EffectPotionRarity rarity) => rarity switch
        {
            EffectPotionRarity.Common => RelicPickerCategory.Common,
            EffectPotionRarity.Uncommon => RelicPickerCategory.Uncommon,
            EffectPotionRarity.Rare => RelicPickerCategory.Rare,
            _ => RelicPickerCategory.Other
        };

        private static GameContentKind Kind(NeowStructuredOutputKind kind) => kind switch
        {
            NeowStructuredOutputKind.Relic => GameContentKind.Relic,
            NeowStructuredOutputKind.Potion => GameContentKind.Potion,
            _ => GameContentKind.Card
        };

        private static string PickerTitle(NeowStructuredOutputKind kind, IUiTextProvider text) => kind switch
        {
            NeowStructuredOutputKind.Relic => text.Get(Ui1TextKey.SearchNeowPickerTitleOrdinaryRelic),
            NeowStructuredOutputKind.Potion => text.Get(Ui1TextKey.SearchNeowPickerTitlePotion),
            NeowStructuredOutputKind.Curse => text.Get(Ui1TextKey.SearchNeowPickerTitleCurse),
            _ => text.Get(Ui1TextKey.SearchNeowPickerTitleCard)
        };

        private static string LocalizeDescription(NeowEffectComponentDefinition definition, IUiTextProvider text) =>
            definition.OutputKind switch
            {
                NeowStructuredOutputKind.Relic when definition.SlotCount == 2 => text.Get(Ui1TextKey.SearchNeowTwoRelicOutputs),
                NeowStructuredOutputKind.Relic when definition.ConditionKind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset =>
                    text.Get(Ui1TextKey.SearchNeowGroupedCapsuleOutputs),
                NeowStructuredOutputKind.Relic => text.Get(Ui1TextKey.SearchNeowRelicOutput),
                NeowStructuredOutputKind.Potion when definition.SlotCount == 2 => text.Get(Ui1TextKey.SearchNeowTwoPotionOutputs),
                NeowStructuredOutputKind.Potion => text.Get(Ui1TextKey.SearchNeowPotionOutput),
                NeowStructuredOutputKind.Card when definition.SlotCount == 2 => text.Get(Ui1TextKey.SearchNeowTwoCardOutputs),
                _ => text.Get(Ui1TextKey.SearchNeowCardOutput)
            };
    }

    private sealed class ScrollBoxesConditionEditor : INeowConditionEditor
    {
        private readonly ModelKey _source;
        private readonly NeowSearchUiCatalog _catalog;
        private readonly IUiTextProvider _text;
        private readonly Button _normalMode;
        private readonly Button? _tripleMode;
        private readonly VBoxContainer _normalBody;
        private readonly Label _specialBody;
        private readonly SearchCardResultSlot _uncommon;
        private readonly SearchCardResultSlot _commonA;
        private readonly SearchCardResultSlot _commonB;
        private int _mode;

        public ScrollBoxesConditionEditor(
            IGameIconResolver icons,
            Action<RelicPickerRequest> openPicker,
            ModelKey source,
            NeowSearchUiCatalog catalog,
            IGameContentNameResolver names,
            IUiTextProvider text)
        {
            _source = source;
            _catalog = catalog;
            _text = text;
            var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            root.AddThemeConstantOverride("separation", 8);

            _normalMode = new Button
            {
                Text = text.Get(Ui1TextKey.SearchNeowScrollNormal),
                CustomMinimumSize = new Vector2(118, 34)
            };
            _normalMode.Pressed += () => SelectMode(0, notify: true);
            if (catalog.CharacterKey == BaseGameModelKeys.Characters.Defect)
            {
                _tripleMode = new Button
                {
                    Text = text.Get(Ui1TextKey.SearchNeowScrollTripleClaw),
                    CustomMinimumSize = new Vector2(82, 34)
                };
                _tripleMode.Pressed += () => SelectMode(1, notify: true);
                HeaderControls = new Control[] { _normalMode, _tripleMode };
            }
            else
            {
                HeaderControls = new Control[] { _normalMode };
            }

            _normalBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _normalBody.AddThemeConstantOverride("separation", 6);
            _uncommon = new SearchCardResultSlot(icons, openPicker);
            _uncommon.Configure(
                text.Get(Ui1TextKey.SearchNeowUncommonSlot),
                catalog.UncommonCharacterCards,
                GameContentKind.Card,
                IconVariant.Small,
                names,
                text.Get(Ui1TextKey.SearchNeowChooseCard),
                string.Empty,
                pickerTitle: text.Get(Ui1TextKey.SearchNeowPickerTitleUncommonCard),
                cardPickerContext: catalog.CreateCardPickerContext(
                    catalog.UncommonCharacterCards,
                    allowCharacterFilter: false,
                    sourceId: "neow:scroll-boxes:uncommon"));
            var commonRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
            commonRow.AddThemeConstantOverride("separation", 8);
            _commonA = new SearchCardResultSlot(icons, openPicker);
            _commonB = new SearchCardResultSlot(icons, openPicker);
            foreach (SearchCardResultSlot slot in new[] { _commonA, _commonB })
            {
                slot.Configure(
                    string.Empty,
                    catalog.CommonCharacterCards,
                    GameContentKind.Card,
                    IconVariant.Small,
                    names,
                    text.Get(Ui1TextKey.SearchNeowChooseCard),
                    text.Get(Ui1TextKey.SearchNeowUnorderedTooltip),
                    pickerTitle: text.Get(Ui1TextKey.SearchNeowPickerTitleCommonCard),
                    cardPickerContext: catalog.CreateCardPickerContext(
                        catalog.CommonCharacterCards,
                        allowCharacterFilter: false,
                        sourceId: "neow:scroll-boxes:common"));
                slot.Changed += OnCommonChanged;
                commonRow.AddChild(slot);
            }
            _uncommon.Changed += () => Changed?.Invoke();
            _normalBody.AddChild(_uncommon);
            _normalBody.AddChild(Ui1Theme.Label(text.Get(Ui1TextKey.SearchNeowCommonSlots), Ui1TextRole.Meta));
            _normalBody.AddChild(commonRow);
            root.AddChild(_normalBody);

            _specialBody = Ui1Theme.Label(text.Get(Ui1TextKey.SearchNeowScrollTripleClawDescription), Ui1TextRole.Muted, true);
            _specialBody.Visible = false;
            root.AddChild(_specialBody);

            View = root;
            SelectMode(0, notify: false);
            RefreshDuplicates();
        }

        public Control View { get; }
        public IReadOnlyList<Control> HeaderControls { get; }
        public event Action? Changed;

        public bool TryBuild(out NeowStructuredEffectSearchCondition? condition, out string issue)
        {
            if (_mode == 1)
            {
                if (_catalog.CharacterKey != BaseGameModelKeys.Characters.Defect)
                {
                    condition = null;
                    issue = _text.Get(Ui1TextKey.SearchNeowConditionUnavailable);
                    return false;
                }
                condition = new NeowStructuredEffectSearchCondition(
                    _source,
                    NeowStructuredConditionKind.SpecialOffer,
                    NeowStructuredEffectScope.SelectableOfferGroups,
                    NeowStructuredOutputKind.Card,
                    Array.Empty<ModelKey>(),
                    NeowSpecialOfferKind.ScrollBoxesTripleClaw);
                issue = string.Empty;
                return true;
            }

            ModelKey?[] values = { _uncommon.SelectedKey, _commonA.SelectedKey, _commonB.SelectedKey };
            if (values.All(value => !value.HasValue))
            {
                condition = null;
                issue = string.Empty;
                return true;
            }

            bool uncommonUnavailable = _uncommon.SelectedKey is { } uncommon &&
                !_catalog.UncommonCharacterCards.Contains(uncommon, ModelKeyComparer.Instance);
            bool commonAUnavailable = _commonA.SelectedKey is { } commonA &&
                !_catalog.CommonCharacterCards.Contains(commonA, ModelKeyComparer.Instance);
            bool commonBUnavailable = _commonB.SelectedKey is { } commonB &&
                !_catalog.CommonCharacterCards.Contains(commonB, ModelKeyComparer.Instance);
            if (uncommonUnavailable || commonAUnavailable || commonBUnavailable)
            {
                condition = null;
                issue = _text.Get(Ui1TextKey.SearchNeowConditionUnavailable);
                return false;
            }

            ModelKey[] keys = values
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            if (keys.Distinct(ModelKeyComparer.Instance).Count() != keys.Length)
            {
                condition = null;
                issue = _text.Get(Ui1TextKey.SearchNeowDuplicateNotAllowed);
                return false;
            }
            condition = new NeowStructuredEffectSearchCondition(
                _source,
                NeowStructuredConditionKind.StructuredCardComposition,
                NeowStructuredEffectScope.SelectableOfferGroups,
                NeowStructuredOutputKind.Card,
                keys);
            issue = string.Empty;
            return true;
        }

        public void FocusInvalid()
        {
            if (_mode == 1) (_tripleMode ?? _normalMode).GrabFocus();
            else (_uncommon.IsEmpty ? _uncommon : _commonA.IsEmpty ? _commonA : _commonB).GrabSlotFocus();
        }

        public void SetEnabled(bool enabled)
        {
            _normalMode.Disabled = !enabled;
            if (_tripleMode is not null) _tripleMode.Disabled = !enabled;
            _uncommon.SetEnabled(enabled);
            _commonA.SetEnabled(enabled);
            _commonB.SetEnabled(enabled);
        }

        public void Clear()
        {
            SelectMode(0, notify: false);
            _uncommon.Clear(false);
            _commonA.Clear(false);
            _commonB.Clear(false);
            RefreshDuplicates();
        }

        public bool TryRestoreCondition(NeowStructuredEffectSearchCondition condition)
        {
            if (condition.SourceRelicKey != _source) return false;
            Clear();
            if (condition.Kind == NeowStructuredConditionKind.SpecialOffer &&
                condition.SpecialOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw && _tripleMode is not null)
            {
                SelectMode(1, notify: false);
                return true;
            }
            if (condition.Kind != NeowStructuredConditionKind.StructuredCardComposition ||
                condition.Scope != NeowStructuredEffectScope.SelectableOfferGroups ||
                condition.OutputKind != NeowStructuredOutputKind.Card) return false;
            ModelKey[] keys = condition.OutputKeys.Where(key => key.IsValid).ToArray();
            ModelKey? uncommon = keys.FirstOrDefault(key => _catalog.UncommonCharacterCards.Contains(key, ModelKeyComparer.Instance));
            if (uncommon.HasValue && uncommon.Value.IsValid) _uncommon.Select(uncommon, notify: false);
            ModelKey[] commons = keys.Where(key => _catalog.CommonCharacterCards.Contains(key, ModelKeyComparer.Instance)).Take(2).ToArray();
            if (commons.Length > 0) _commonA.Select(commons[0], notify: false);
            if (commons.Length > 1) _commonB.Select(commons[1], notify: false);
            SelectMode(0, notify: false);
            RefreshDuplicates();
            return true;
        }

        public NeowEditorState CaptureState() => new ScrollBoxesEditorState(
            _mode,
            _uncommon.SelectedKey,
            _commonA.SelectedKey,
            _commonB.SelectedKey);

        public void RestoreState(NeowEditorState state)
        {
            if (state is not ScrollBoxesEditorState scroll) return;
            _uncommon.Select(scroll.Uncommon, notify: false);
            _commonA.Select(scroll.CommonA, notify: false);
            _commonB.Select(scroll.CommonB, notify: false);
            SelectMode(scroll.Mode == 1 && _tripleMode is not null ? 1 : 0, notify: false);
            RefreshDuplicates();
        }

        private void SelectMode(int mode, bool notify)
        {
            _mode = mode == 1 && _tripleMode is not null ? 1 : 0;
            _normalBody.Visible = _mode == 0;
            _specialBody.Visible = _mode == 1;
            Ui1Theme.ApplyButton(_normalMode, _mode == 0 ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
            if (_tripleMode is not null)
            {
                Ui1Theme.ApplyButton(_tripleMode, _mode == 1 ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
            }
            if (notify) Changed?.Invoke();
        }

        private void OnCommonChanged()
        {
            RefreshDuplicates();
            Changed?.Invoke();
        }

        private void RefreshDuplicates()
        {
            ModelKey[] selected = new[] { _commonA.SelectedKey, _commonB.SelectedKey }
                .Where(key => key.HasValue)
                .Select(key => key!.Value)
                .ToArray();
            _commonA.SetDisabledKeys(selected.Where(key => _commonA.SelectedKey != key));
            _commonB.SetDisabledKeys(selected.Where(key => _commonB.SelectedKey != key));
        }
    }

}
