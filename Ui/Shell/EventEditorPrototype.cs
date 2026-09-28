using Godot;
using RolltheSpire2.Search.Contracts;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class EventEditorPrototype : Control
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly WorkspacePalette _p = WorkspacePalette.Canonical;
    private readonly EventThumbnailProvider _thumbnails = new();
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("event-editor-prototype");
    private readonly Control _body = new();
    private ScrollContainer? _editorScroll;
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private readonly Dictionary<int, SeatDraft> _partyDrafts = [];
    private SeatDraft _draft = new();
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private bool _guideExpanded;
    private int _activeSeat;

    private sealed record QueueCondition(int Act, int Position, bool Exact, bool Excluded, ModelKey Event);

    private sealed class SeatDraft
    {
        public string Context = string.Empty;
        public EventSequenceSearchUiCatalog Catalog = EventSequenceSearchUiCatalog.Empty(
            RuntimeProfileId.Unsupported, "event-template-uninitialized");
        public int ExpandedCondition = -1;
        public int QueueAct = 2;
        public int QueuePosition = 3;
        public bool QueueAdvanced;
        public bool QueueExact;
        public bool QueueExcluded;
        public List<QueueCondition> QueueConditions { get; } = [];
        public ModelKey? SelectedEvent;
        public int CharacterColor = -1;
        public int TrialCase = -1;
        public int TinkerType = -1;
        public string? TinkerEffect;
        public HashSet<string> PrototypeTargets { get; } = [];
        public Dictionary<EventResultConditionKind, ModelKey?[]> Results { get; } = [];
        public IReadOnlyList<ModelKey> TransformCards = [];
    }

    public EventEditorPrototype(ModRuntimeSnapshot runtime)
    {
        _runtime = runtime;
        AddChild(_body);
        AddChild(_queuePickerOverlay);
        SetProcessUnhandledKeyInput(true);
    }

    public event Action? GuideRequested;
    public event Action? QueueChanged;
    public event Action<string>? TransformationRequested;
    internal Func<int, string, bool>? TransformTakenOver { get; set; }
    internal IReadOnlyList<TransformationPrototypeSource> TransformationSources(int seat)
    {
        if (!_drafts.TryGetValue(seat, out var draft)) return [];
        string[] supported = ["MORPHIC_GROVE", "AROMA_OF_CHAOS", "WHISPERING_HOLLOW", "SYMBIOTE", "TRIAL"];
        return draft.QueueConditions.Where(condition => !condition.Excluded &&
                supported.Contains(condition.Event.Entry, StringComparer.Ordinal) &&
                draft.Catalog.CandidatesForAct(condition.Act).Any(candidate => candidate.EventKey == condition.Event))
            .Select(condition => new TransformationPrototypeSource("E." + condition.Event.Entry,
                condition.Event, condition.Event.Entry is "MORPHIC_GROVE" or "TRIAL" ? 2 : 1))
            .DistinctBy(source => source.Id).ToArray();
    }
    internal void ClearTakenOverTransformTargets(int seat, string sourceId)
    {
        if (!_drafts.TryGetValue(seat, out var draft)) return;
        string? entry = sourceId.StartsWith("E.", StringComparison.Ordinal) ? sourceId[2..] : null;
        if (entry is null) return;
        if (entry == "MORPHIC_GROVE") draft.Results.Remove(EventResultConditionKind.MorphicGroveGroupInitialBasicsContains);
        if (entry == "AROMA_OF_CHAOS") draft.Results.Remove(EventResultConditionKind.AromaOfChaosInitialBasicTransform);
        if (entry == "WHISPERING_HOLLOW") draft.Results.Remove(EventResultConditionKind.WhisperingHollowInitialBasicTransform);
        if (entry == "SYMBIOTE") draft.Results.Remove(EventResultConditionKind.SymbioteInitialBasicTransform);
        if (entry == "TRIAL") { draft.Results.Remove(EventResultConditionKind.TrialNondescriptInitialBasicsContains); draft.TrialCase = -1; }
        draft.PrototypeTargets.RemoveWhere(key => key.StartsWith($"EVENT:{entry}:transform:", StringComparison.Ordinal));
    }

    public void Refresh(string language, IUiTextProvider text, ModelKey character, int ascension,
        int players, int seat, SerializableUnlockState? unlocks = null, bool render = true,
        Core.Authority.RuntimeContextAuthoritySnapshot? partyAuthority = null)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        _queuePickerPlayers = players;
        _activeSeat = seat;
        var drafts = players > 1 ? _partyDrafts : _drafts;
        foreach (int removedSeat in drafts.Keys.Where(index => index >= players).ToArray()) drafts.Remove(removedSeat);
        if (!drafts.TryGetValue(seat, out SeatDraft? draft)) drafts[seat] = draft = new SeatDraft();
        _draft = draft;
        string unlockKey = players == 1
            ? System.Text.Json.JsonSerializer.Serialize(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks);
        string context = $"{character}/{ascension}/{players}/{seat}/{unlockKey}/{partyAuthority?.WorldSnapshotFingerprint}";
        if (_draft.Context != context)
        {
            _draft.Context = context;
            try
            {
                if (players > 1 && unlocks is null)
                    _draft.Catalog = EventSequenceSearchUiCatalog.Empty(
                        _runtime.Profile.ProfileId, "event-template-unlocks-unread");
                else
                {
                    UnlockState resolved = players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    string seed = new(_runtime.Profile.SeedAlphabet[0], _runtime.Profile.SeedLength);
                    var effects = ReflectionNeowEffectSnapshotAdapter.Capture(_runtime.Profile, seed,
                        CharacterIdentity.FromKey(character), ascension, players, seat, _runtime.Profile.ProfileId,
                        _runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), resolved);
                    var world = partyAuthority?.WorldAuthority ?? ReflectionNeowEffectSnapshotAdapter.CaptureWorld(_runtime.Profile, seed,
                        CharacterIdentity.FromKey(character), ascension, players, seat, noRunModifiers: true,
                        effects.EffectAuthority, gameVersion: _runtime.Detection.DisplayVersion);
                    _draft.Catalog = EventSequenceSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, world);
                }
                CaptureResultPools(character, ascension, players, partyAuthority,
                    unlocks is null ? null : UnlockState.FromSerializable(unlocks));
                Revalidate();
            }
            catch (Exception exception)
            {
                _draft.Catalog = EventSequenceSearchUiCatalog.Empty(
                    _runtime.Profile.ProfileId, "event-template-catalog-unavailable");
                RuntimeLog.Warn("eventPrototypeCatalog=" + exception.Message);
            }
        }
        if (render) Render();
    }

    private void Revalidate()
    {
        _draft.QueueConditions.RemoveAll(condition =>
            !_draft.Catalog.CandidatesForAct(condition.Act).Any(candidate => candidate.EventKey == condition.Event));
        if (_draft.SelectedEvent is { } selected && EventResultPrototypeWhitelist.Find(selected) is null)
            _draft.SelectedEvent = null;
    }

    private void Render()
    {
        int scrollTop = _editorScroll is not null && GodotObject.IsInstanceValid(_editorScroll) ? _editorScroll.ScrollVertical : 0;
        Clear(_body);
        _body.Size = Size;
        VBoxContainer guide = FamilyGuide(_body, Size.X - 20);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        void Layout()
        {
            float top = guide.Size.Y + 10;
            scroll.Position = new(0, top);
            scroll.Size = new(Size.X, Math.Max(0, Size.Y - top));
        }
        guide.Resized += Layout;
        Layout();
        _body.AddChild(scroll);
        var content = new Control { CustomMinimumSize = new(Size.X - 20, 1) };
        scroll.AddChild(content);
        float width = Size.X - 28;

        float bottom = RenderQueue(content, width, 0);
        content.CustomMinimumSize = new(width, bottom + 12);
        _editorScroll = scroll;
        if (scrollTop > 0) scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scrollTop);
    }

    private VBoxContainer FamilyGuide(Control host, float width) =>
        SearchEditorGuide.Build(host, _p, width, _text.Get("query.event.guide.title"),
            _text.Get("query.event.guide.summary"),
            [_text.Get("query.event.guide.usage"), _text.Get("query.event.guide.boundary")],
            _guideExpanded, () => { _guideExpanded = !_guideExpanded; Render(); }, () => GuideRequested?.Invoke());

    private float RenderResultEditor(Control content, float width, float top, bool inline = false)
    {
        if (_draft.SelectedEvent is not { } selected)
        {
            Text(content, _text.Get("query.event.results.none"), 0, top, width, 16, true);
            return top + 36;
        }

        const float identityWidth = 216;
        float editorLeft = inline ? 16 : 248;
        if (!inline)
        {
            Text(content, _names.Resolve(selected, GameContentKind.Event), 0, top, identityWidth, 19);
            var portrait = new EventThumbnailView(_thumbnails.Resolve(selected), new Vector2(196, 132),
                EventThumbnailPresentation.Contain) { Position = new Vector2(0, top + 34) };
            content.AddChild(portrait);
        }

        float editorTop = top + 2;
        EventResultPrototypeDescriptor? descriptor = EventResultPrototypeWhitelist.Find(selected);
        float editorBottom = descriptor?.EditorKind switch
        {
            EventResultPrototypeEditorKind.FakeMerchantRelic => RenderSingleObjectEditor(content, editorLeft, editorTop,
                "query.event.results.fake_relic"),
            EventResultPrototypeEditorKind.SingleTransform => RenderTransformEditor(content, editorLeft, editorTop, 1),
            EventResultPrototypeEditorKind.DoubleTransform => RenderTransformEditor(content, editorLeft, editorTop, 2),
            EventResultPrototypeEditorKind.TrashHeap => RenderTrashHeapEditor(content, editorLeft, editorTop),
            EventResultPrototypeEditorKind.CharacterColor => RenderCharacterColorEditor(content, editorLeft, editorTop),
            EventResultPrototypeEditorKind.Trial => RenderTrialEditor(content, editorLeft, editorTop),
            EventResultPrototypeEditorKind.TinkerTime => RenderTinkerTimeEditor(content, editorLeft, editorTop),
            _ => editorTop + 32
        };
        float bottom = Math.Max(top + (inline ? 32 : 178), editorBottom);
        if (!inline) AddLine(content, identityWidth + 12, top, 1, bottom - top);
        return bottom + 8;
    }

    private float RenderSingleObjectEditor(Control content, float x, float y, string labelKey)
    {
        Text(content, _text.Get(labelKey), x, y, 220, 16, true);
        ResultTile(content, x, y + 28, EventResultConditionKind.FakeMerchantOfferedFakeRelic);
        return y + 164;
    }

    private float RenderTransformEditor(Control content, float x, float y, int slots)
    {
        Text(content, _text.Get("query.event.results.transform_targets"), x, y, 220, 16, true);
        if (_draft.SelectedEvent is { } selected &&
            TransformTakenOver?.Invoke(_activeSeat, "E." + selected.Entry) == true)
        {
            Text(content, _text.Get("query.transform.managed"), x, y + 32, 320, 16, true);
            return y + 82;
        }
        var kind = _draft.SelectedEvent?.Entry switch {
            "MORPHIC_GROVE" => EventResultConditionKind.MorphicGroveGroupInitialBasicsContains,
            "SYMBIOTE" => EventResultConditionKind.SymbioteInitialBasicTransform,
            "AROMA_OF_CHAOS" => EventResultConditionKind.AromaOfChaosInitialBasicTransform,
            "WHISPERING_HOLLOW" => EventResultConditionKind.WhisperingHollowInitialBasicTransform,
            _ => throw new InvalidOperationException("EventResult.TransformOutsideWhitelist") };
        Text(content, _text.Get("integration.event.premise." + _draft.SelectedEvent!.Value.Entry), x, y + 26, 400, 14, true);
        ResultTile(content, x, y + 72, kind);
        if (slots == 2) ResultTile(content, x + WorkspaceResultTile.TileWidth + 14, y + 72, kind, 1);
        return y + 210;
    }

    private float RenderTrashHeapEditor(Control content, float x, float y)
    {
        Text(content, _text.Get("query.event.results.card_target"), x, y, 120, 16, true);
        ResultTile(content, x, y + 28, EventResultConditionKind.TrashHeapGrabCard);
        float relicLeft = x + WorkspaceResultTile.TileWidth + 20;
        Text(content, _text.Get("query.event.results.relic_target"), relicLeft, y, 120, 16, true);
        ResultTile(content, relicLeft, y + 28, EventResultConditionKind.TrashHeapDiveRelic);
        return y + 164;
    }

    private float RenderCharacterColorEditor(Control content, float x, float y)
    {
        Text(content, _text.Get("query.event.results.character_color"), x, y, 180, 16, true);
        ModelKey[] characters =
        [
            BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent,
            BaseGameModelKeys.Characters.Defect, BaseGameModelKeys.Characters.Necrobinder,
            BaseGameModelKeys.Characters.Regent
        ];
        for (int index = 0; index < characters.Length; index++)
        {
            int captured = index;
            ModelKey key = characters[index];
            var button = Button(content, string.Empty, x + index * 62, y + 32, 54,
                () => { _draft.CharacterColor = _draft.CharacterColor == captured ? -1 : captured; Render(); },
                _draft.CharacterColor == index, 54);
            AddPicture(button, key, GameContentKind.Character, IconVariant.CharacterPortrait, 7, 7, 40);
        }
        return y + 100;
    }

    private float RenderTrialEditor(Control content, float x, float y)
    {
        if (TransformTakenOver?.Invoke(_activeSeat, "E.TRIAL") == true)
        {
            Text(content, _text.Get("query.transform.managed") + " · " + _text.Get("query.event.results.case_3"), x, y, 440, 16, true);
            return y + 82;
        }
        Text(content, _text.Get("query.event.results.trial_case"), x, y, 130, 16, true);
        float[] caseWidths = [142, 142, 190];
        float caseLeft = x;
        for (int index = 0; index < 3; index++)
        {
            int captured = index;
            Button(content, _text.Get($"query.event.results.case_{index + 1}"), caseLeft, y + 30, caseWidths[index], () =>
            {
                _draft.TrialCase = _draft.TrialCase == captured ? -1 : captured;
                if (_draft.TrialCase != 2)
                {
                    _draft.Results.Remove(EventResultConditionKind.TrialNondescriptInitialBasicsContains);
                }
                Render();
            }, _draft.TrialCase == index, 38);
            caseLeft += caseWidths[index] + 10;
        }
        if (_draft.TrialCase != 2) return y + 76;
        float transformTop = y + 86;
        Text(content, _text.Get("query.event.results.transform_targets"), x, transformTop, 180, 16, true);
        Text(content, _text.Get("integration.event.premise.TRIAL"), x, transformTop + 26, 400, 14, true);
        ResultTile(content, x, transformTop + 72, EventResultConditionKind.TrialNondescriptInitialBasicsContains);
        ResultTile(content, x + WorkspaceResultTile.TileWidth + 14, transformTop + 72, EventResultConditionKind.TrialNondescriptInitialBasicsContains, 1);
        return transformTop + 210;
    }

    private float RenderTinkerTimeEditor(Control content, float x, float y)
    {
        Text(content, _text.Get("query.event.results.target_type"), x, y, 160, 16, true);
        for (int index = 0; index < 3; index++)
        {
            int captured = index;
            Button(content, _text.Get($"query.event.results.card_type_{index + 1}"), x + index * 104, y + 30, 94, () =>
            {
                _draft.TinkerType = _draft.TinkerType == captured ? -1 : captured;
                IReadOnlyList<string> legal = EventResultPrototypeWhitelist.TinkerEffects(_draft.TinkerType);
                if (_draft.TinkerEffect is not null && !legal.Contains(_draft.TinkerEffect, StringComparer.Ordinal))
                    _draft.TinkerEffect = null;
                Render();
            }, _draft.TinkerType == index, 38);
        }
        if (_draft.TinkerType < 0) return y + 76;
        float effectTop = y + 86;
        Text(content, _text.Get("query.event.results.target_effect"), x, effectTop, 160, 16, true);
        IReadOnlyList<string> effects = EventResultPrototypeWhitelist.TinkerEffects(_draft.TinkerType);
        for (int index = 0; index < effects.Count; index++)
        {
            string effect = effects[index];
            Button(content, _text.Get($"query.event.results.rider.{effect}"), x + index * 128, effectTop + 30, 118,
                () => { _draft.TinkerEffect = _draft.TinkerEffect == effect ? null : effect; Render(); },
                _draft.TinkerEffect == effect, 38);
        }
        return effectTop + 76;
    }

    private void AddEventTile(Control content, ModelKey key, int index, float x, float y, int columns,
        bool selected, Action action)
    {
        const float width = 96, height = 80, gap = 8;
        var button = Button(content, string.Empty, x + index % columns * (width + gap),
            y + index / columns * (height + gap), width, action, selected, height);
        Label label = Text(button, _names.Resolve(key, GameContentKind.Event), 4, 2, width - 8, 12);
        label.Size = new(width - 8, 18);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.MouseFilter = MouseFilterEnum.Ignore;
        button.AddChild(new EventThumbnailView(_thumbnails.Resolve(key), new Vector2(62, 52),
            EventThumbnailPresentation.PickerSquareCrop) { Position = new Vector2(17, 24) });
    }

    private void PrototypeResultTile(Control content, float x, float y, string stateKey)
    {
        bool selected = _draft.PrototypeTargets.Contains(stateKey);
        PrototypeResultTile(content, x, y, selected, () =>
        {
            if (!_draft.PrototypeTargets.Add(stateKey)) _draft.PrototypeTargets.Remove(stateKey);
            Render();
        });
    }

    private void PrototypeResultTile(Control content, float x, float y, bool selected, Action action)
    {
        Button tile = Button(content, selected ? "✓" : "+", x, y, WorkspaceResultTile.TileWidth,
            action, selected, WorkspaceResultTile.TileHeight);
        tile.AddThemeFontSizeOverride("font_size", selected ? 24 : 30);
    }

    private void AddPicture(Control parent, ModelKey key, GameContentKind kind, IconVariant variant,
        float x, float y, float size)
    {
        Texture2D? texture = _icons.Resolve(key, kind, variant).Texture;
        if (texture is null) return;
        parent.AddChild(new TextureRect
        {
            Position = new(x, y), Size = new(size, size), Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        });
    }

    private Button Button(Control parent, string title, float x, float y, float width, Action action,
        bool selected, float height)
    {
        var button = _p.CompactButton(title, height, height < 32 ? 16 : 18, selected);
        button.Position = new(x, y);
        button.Size = new(width, height);
        button.CustomMinimumSize = new(0, height);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private Label Text(Control parent, string value, float x, float y, float width, int size, bool secondary = false)
    {
        var label = _p.Label(value, size, secondary);
        label.Position = new(x, y);
        label.Size = new(width, 28);
        label.ClipText = true;
        parent.AddChild(label);
        return label;
    }

    private void AddLine(Control parent, float x, float y, float width)
    {
        Color color = _p.Color(_p.Line);
        color.A *= .45f;
        parent.AddChild(new ColorRect { Position = new(x, y), Size = new(width, 1), Color = color, MouseFilter = MouseFilterEnum.Ignore });
    }

    private void AddLine(Control parent, float x, float y, float width, float height)
    {
        Color color = _p.Color(_p.Line);
        color.A *= .45f;
        parent.AddChild(new ColorRect { Position = new(x, y), Size = new(width, height), Color = color, MouseFilter = MouseFilterEnum.Ignore });
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
}
