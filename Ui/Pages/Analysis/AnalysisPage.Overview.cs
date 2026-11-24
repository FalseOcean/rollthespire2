using Godot;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Pages.Search.Event;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private readonly EventThumbnailProvider _eventThumbnails = new();
    private readonly EncounterPortraitProvider _encounterPortraits = new();

    private string WorldIssueText(string? issue, string fallback)
    {
        string key = issue?.Split(':', 2)[0] switch
        {
            "AnalysisDomainFailed" => "predictor.analysis.domain_failed",
            "UnknownWorldGenerationHookOrModifier" or "UnsupportedModdedWorldGeneration" => "predictor.world.rules_unknown",
            "MissingActSelectionAuthority" or "MissingSelectedActs" or "MissingActSelectionReplayInputs" or
                "MissingActsByIndexOrder" or "MissingDiscoveredActs" or "ActSelectionGroupIncomplete" => "predictor.world.act_unknown",
            "MissingUnlockedSharedAncientCatalog" or "MissingSharedAncientReferenceIdentity" or
                "MissingUnlockedAncientPool" => "predictor.world.ancient_pool_unknown",
            "MissingRelicInitialization" or "MissingSharedRelicPoolOrder" or "MissingCharacterRelicPoolOrder" or
                "MissingPriorUpFront" => "predictor.world.start_rng_unknown",
            "EncounterDiscoveryContextMissing" => "predictor.encounter_context_missing",
            "MissingEventEpochAuthority" or "MissingEventEpochMembership" => "predictor.world.event_unlock_unknown",
            "UnknownGeneratedRoomEventFilter" => "predictor.world.event_filter_unknown",
            "MissingOpeningAncientCursorAuthority" => "predictor.world.event_start_unknown",
            "MissingActEventSequenceAuthority" or "MissingEventCatalog" or "MissingSharedEventCatalog" or
                "MissingActEventCatalogOrder" or "EventEligibleOrderAlignmentMismatch" or
                "SharedEventCatalogAlignmentMismatch" => "predictor.world.event_order_unknown",
            _ => fallback
        };
        return Text(key);
    }

    private string? SharedRoomIssue => LastFacts?.World?.RoomGenerationIssueCode;

    private ModelKey OverviewActKey(int act) =>
        SeenMap(act) is { } observed && ModelKey.TryParseExact(observed.Act, out var liveAct) ? liveAct :
        LastFacts?.World?.SelectedActs.ElementAtOrDefault(act - 1) is { IsValid: true } selected ? selected :
        _viewModel?.EncounterSequences.FirstOrDefault(a => a.Act == act)?.ActKey ??
        _viewModel?.EventPoolSequenceDomain.Items.FirstOrDefault(a => a.Act == act)?.ActKey ?? default;

    private void RenderActTab(int index)
    {
        var button = _wbTabs[index]; WClear(button); button.Text = "";
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 5); button.AddChild(row);
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 9; row.OffsetRight = -9;
        string title = Text(PageKeys[index]);
        if (index < 3 && _wbHasResult && OverviewActKey(index + 1) is { IsValid: true } actKey)
            title += " · " + _contentNames!.Resolve(actKey, GameContentKind.Act);
        var caption = _palette.Label(title, 16);
        caption.ClipText = true;
        caption.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        caption.SizeFlagsHorizontal = SizeFlags.Fill;
        caption.VerticalAlignment = VerticalAlignment.Center;
        caption.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(caption);
        // Keep the title and previews together; only trim a title when the
        // complete group would exceed its existing full-width tab target.
        void FitCaption()
        {
            if (!IsInstanceValid(caption) || !IsInstanceValid(row)) return;
            float textWidth = Mathf.Ceil(caption.GetThemeFont("font").GetStringSize(title,
                fontSize: caption.GetThemeFontSize("font_size")).X);
            float previewWidth = (row.GetChildCount() - 1) * 25;
            caption.CustomMinimumSize = new Vector2(Math.Min(textWidth, Math.Max(0, row.Size.X - previewWidth)), 0);
        }
        row.Resized += FitCaption;
        caption.ThemeChanged += FitCaption;
        Callable.From(FitCaption).CallDeferred();
        button.TooltipText = title;
        void Icon(ModelKey key, GameContentKind kind, IconVariant variant)
        {
            row.AddChild(new TextureRect { Texture = _icons.Resolve(key, kind, variant).Texture,
                CustomMinimumSize = new Vector2(20, 20), SizeFlagsVertical = SizeFlags.ShrinkCenter,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore });
        }
        if (!_wbHasResult || _viewModel is null) return;
        if (index == 3)
        {
            foreach (var entry in _viewModel.RelicSequenceDomain.Items.FirstOrDefault(l => l.Kind == RelicSequenceKind.Shop)?.Entries.OrderBy(e => e.Position).Take(3) ?? [])
                Icon(entry.RelicDisplay.ModelKey, GameContentKind.Relic, IconVariant.Small);
            return;
        }
        if (index == 0)
            foreach (var choice in _viewModel.NeowChoices.OrderBy(c => c.SlotIndex)) Icon(choice.RelicKey, GameContentKind.Relic, IconVariant.Small);
        else
        {
            var ancient = _viewModel.AncientDomain.Items.FirstOrDefault(a => a.Act == index + 1);
            if (ancient?.AncientDisplay.ModelKey is { IsValid: true } key) Icon(key, GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon);
        }
        foreach (var boss in _viewModel.BossDomain.Items.Where(b => b.Act == index + 1).OrderBy(b => b.Ordinal))
            Icon(boss.BossDisplay.ModelKey, GameContentKind.Encounter, IconVariant.WorldCompendiumBossIcon);
    }

    private const int EncounterTileWidth = 136, EncounterTileHeight = 116, EncounterGap = 8;
    private readonly HashSet<(int Act, bool Elite)> _expandedEncounters = [];

    private Control WQueueTile(ModelKey encounter, bool elite, int ordinal, string name)
    {
        var tile = _palette.Button("");
        tile.Name = "Encounter" + ordinal;
        tile.CustomMinimumSize = new Vector2(EncounterTileWidth, EncounterTileHeight);
        tile.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        var artwork = new TextureRect { Texture = VerticalMapView.ResolveRoomIcon(elite ? MapPointType.Elite : MapPointType.Monster),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore };
        tile.AddChild(artwork); artwork.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        artwork.OffsetLeft = 5; artwork.OffsetRight = -5; artwork.OffsetTop = 21; artwork.OffsetBottom = -4;
        var number = new Label { Text = ordinal.ToString(), Position = new Vector2(6, 1),
            Size = new Vector2(28, 20), MouseFilter = MouseFilterEnum.Ignore };
        number.AddThemeFontSizeOverride("font_size", 15);
        tile.AddChild(number);
        if (elite)
        {
            tile.MouseEntered += () => _tooltipHost.ShowText(tile, $"{ordinal}. {name}");
            tile.MouseExited += () => _tooltipHost.Dismiss(tile);
            tile.TreeExiting += () => _tooltipHost.Dismiss(tile);
        }
        else tile.TooltipText = $"{ordinal}. {name}";
        tile.Ready += async () =>
        {
            var texture = await _encounterPortraits.Get(encounter);
            if (texture is not null && IsInstanceValid(artwork) && artwork.IsInsideTree()) artwork.Texture = texture;
        };
        return tile;
    }

    private Button WEventTile(EventPoolSequenceEntryViewModel entry, AnalysisActPredictionCard tooltipSource)
    {
        var tile = _palette.Button("");
        tile.CustomMinimumSize = new Vector2(88, 110);
        tile.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 4); margin.AddThemeConstantOverride("margin_right", 4);
        margin.AddThemeConstantOverride("margin_top", 4); margin.AddThemeConstantOverride("margin_bottom", 4);
        tile.AddChild(margin); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); margin.AddChild(column);
        var artwork = new EventThumbnailView(_eventThumbnails.Resolve(entry.EventDisplay.ModelKey),
            new Vector2(80, 80), EventThumbnailPresentation.PickerSquareCrop)
        { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        column.AddChild(artwork);
        var ordinal = new Label { Text = entry.Ordinal.ToString("D2"), HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore };
        ordinal.AddThemeFontSizeOverride("font_size", 16); column.AddChild(ordinal);
        tile.MouseEntered += () => tooltipSource.ShowEventTooltipFor(tile, entry);
        tile.MouseExited += () => tooltipSource.DismissEventTooltipFor(tile);
        tile.TreeExiting += () => tooltipSource.DismissEventTooltipFor(tile);
        return tile;
    }

    private VBoxContainer? _combatSurface;
    private Control? _eliteQueueSurface, _normalQueueSurface;

    private void UpdateCombatQueueVisibility(int act)
    {
        if (_combatSurface is null || !IsInstanceValid(_combatSurface)) return;
        bool visible = !_inspectedEvents[act - 1].IsValid;
        if (IsInstanceValid(_eliteQueueSurface)) _eliteQueueSurface!.Visible = visible;
        if (IsInstanceValid(_normalQueueSurface)) _normalQueueSurface!.Visible = visible;
    }

    private void PopulateCombatDetails(int act, MapPrediction? map)
    {
        if (_combatSurface is null || !IsInstanceValid(_combatSurface) || !_combatSurface.IsInsideTree()) return;
        var body = _combatSurface; WClear(body);
        var bosses = new HBoxContainer(); bosses.AddThemeConstantOverride("separation", 16); body.AddChild(bosses);
        var actBosses = _viewModel!.BossDomain.Items.Where(b => b.Act == act).OrderBy(b => b.Ordinal).ToArray();
        var bossTitle = WLabel(Text("predictor.boss"), true);
        bossTitle.AutowrapMode = TextServer.AutowrapMode.Off;
        bossTitle.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        bosses.AddChild(bossTitle);
        if (actBosses.Length == 0 || LastRequest?.Ascension >= 10 && LastFacts?.World?.SelectedActs.Count == act && actBosses.Length == 1)
            bosses.AddChild(WNote(WorldIssueText(_viewModel.BossDomain.IssueCode, actBosses.Length == 0
                ? "predictor.world.boss_unknown" : "predictor.world.second_boss_unknown")));
        var bossIdentities = new HBoxContainer { Name = "BossIdentities", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bossIdentities.AddThemeConstantOverride("separation", 16); bosses.AddChild(bossIdentities);
        for (int index = 0; index < actBosses.Length; index++)
        {
            var boss = actBosses[index];
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 6); bossIdentities.AddChild(row);
            if (actBosses.Length > 1)
            {
                var number = WLabel($"{index + 1}.");
                number.AutowrapMode = TextServer.AutowrapMode.Off;
                number.SizeFlagsHorizontal = SizeFlags.Fill;
                row.AddChild(number);
            }
            row.AddChild(WObject(boss.BossDisplay.ModelKey, GameContentKind.Encounter,
                boss.BossDisplay.DisplayName, 38));
        }
        var eliteColumn = WColumn(); eliteColumn.Name = "EliteQueueSection"; body.AddChild(eliteColumn);
        var normalColumn = WColumn(); normalColumn.Name = "NormalQueueSection"; body.AddChild(normalColumn);
        _eliteQueueSurface = eliteColumn; _normalQueueSurface = normalColumn;
        UpdateCombatQueueVisibility(act);
        var sequence = _viewModel.EncounterSequences.FirstOrDefault(a => a.Act == act);
        if (sequence is null || sequence.Normal.Count == 0 && sequence.Elite.Count == 0 &&
            !string.IsNullOrEmpty(sequence.IssueCode))
        {
            WTitle(normalColumn, "predictor.world.encounters");
            normalColumn.AddChild(WNote(WorldIssueText(sequence?.IssueCode ?? SharedRoomIssue, "predictor.world.encounters_unknown")));
            return;
        }
        if (sequence.Precision != RolltheSpire2.Core.Prediction.PredictionPrecision.Exact)
            normalColumn.AddChild(WLabel(Text("predictor.encounter_partial")));
        int elites = map is null ? sequence.Elite.Count : new MapRouteSet(map, MapPointType.Elite).Value + 1;
        int monsters = map is null ? sequence.Normal.Count : new MapRouteSet(map, MapPointType.Monster).Value;
        AddEncounterQueue(eliteColumn, sequence.Elite, act, true, Math.Min(5, elites));
        AddEncounterQueue(normalColumn, sequence.Normal, act, false, monsters);
    }

    private void AddEncounterQueue(VBoxContainer column, IReadOnlyList<EncounterSequenceEntryViewModel> entries,
        int act, bool elite, int previewLimit)
    {
        var header = new HBoxContainer();
        column.AddChild(header);
        WTitle(header, elite ? "predictor.elite_sequence" : "predictor.combat_sequence");
        if (entries.Count == 0)
        {
            column.AddChild(WNote(Text("predictor.world.queue_empty")));
            return;
        }
        string prefix = elite ? "Elite" : "Normal";
        var expand = WAction("predictor.encounters.show_all");
        expand.Name = prefix + "QueueExpand";
        header.AddChild(expand);
        var tiles = new HFlowContainer { Name = prefix + "Preview", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tiles.AddThemeConstantOverride("h_separation", EncounterGap);
        tiles.AddThemeConstantOverride("v_separation", EncounterGap);
        column.AddChild(tiles);
        var key = (act, elite);
        void Layout()
        {
            int fits = Math.Max(1, (int)((tiles.Size.X + EncounterGap) / (EncounterTileWidth + EncounterGap)));
            int preview = Math.Min(fits, previewLimit);
            bool expanded = _expandedEncounters.Contains(key);
            expand.Visible = entries.Count > preview;
            expand.Text = Text(expanded ? "predictor.collapse" : "predictor.encounters.show_all");
            int shown = expanded ? entries.Count : Math.Min(entries.Count, preview);
            for (int i = tiles.GetChildCount(); i < shown; i++)
            {
                var entry = entries[i];
                tiles.AddChild(WQueueTile(entry.EncounterDisplay.ModelKey, elite, entry.Ordinal, entry.EncounterDisplay.DisplayName));
            }
            for (int i = 0; i < tiles.GetChildCount(); i++) tiles.GetChild<Control>(i).Visible = i < shown;
        }
        expand.Pressed += () => { if (!_expandedEncounters.Remove(key)) _expandedEncounters.Add(key); Layout(); };
        tiles.Resized += Layout;
        Layout();
    }
}
