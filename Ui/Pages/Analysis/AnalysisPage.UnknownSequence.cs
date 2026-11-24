using Godot;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Ui.Icons;
using GamePointType = MegaCrit.Sts2.Core.Map.MapPointType;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    // An ordinal belongs to an Act. Shortening a hypothetical route hides its
    // later marks; expanding it restores them without moving them to another Act.
    private string _unknownSequenceSeed = "";
    private readonly HashSet<int>[] _unknownShopMarks = [[], [], []];
    private readonly int?[] _unknownChosenCounts = new int?[3];
    private readonly int?[] _unknownMaximums = new int?[3];
    private VBoxContainer? _unknownSequenceRows;

    private void BuildUnknownSequenceCard()
    {
        string seed = LastFacts!.CanonicalSeed;
        if (seed != _unknownSequenceSeed)
        {
            _unknownSequenceSeed = seed;
            foreach (var marks in _unknownShopMarks) marks.Clear();
            Array.Clear(_unknownChosenCounts);
        }

        var section = WCard(_wbCenter, Text("predictor.unknown_series.title"));
        section.Name = "UnknownSequenceCard";
        section.AddChild(WNote(Text("predictor.unknown_series.premise")));
        var legend = new HFlowContainer();
        legend.AddThemeConstantOverride("h_separation", 16);
        section.AddChild(legend);
        foreach (var room in Enum.GetValues<UnknownRoomCategory>())
        {
            if (UnknownResultIcon(room) is not { } icon) continue;
            var item = new HBoxContainer();
            legend.AddChild(item);
            item.AddChild(new TextureRect
            {
                Texture = icon, CustomMinimumSize = new(24, 24),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            });
            item.AddChild(_palette.Label(Text("ui1.map.room." + room), 14, secondary: true));
        }
        var scroll = new ScrollContainer
        {
            Name = "UnknownSequenceScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        section.AddChild(scroll);
        var rows = WColumn();
        scroll.AddChild(rows);
        _unknownSequenceRows = rows;
        RefreshUnknownSequenceRows();
        var token = (_overviewLifetime ??= new CancellationTokenSource()).Token;
        LoadUnknownSequenceLengths(rows, seed, token);
    }

    private static Texture2D? UnknownResultIcon(UnknownRoomCategory room)
    {
        var point = room == UnknownRoomCategory.Event ? GamePointType.Unknown : Enum.Parse<GamePointType>(room.ToString());
        return NativeMapRoomIcons.ResolveHistoryIcon(point, Enum.Parse<MegaCrit.Sts2.Core.Rooms.RoomType>(room.ToString()));
    }

    private async void LoadUnknownSequenceLengths(VBoxContainer rows, string seed, CancellationToken token)
    {
        for (int act = 1; act <= 3; act++)
        {
            if (!IsCurrentUnknownSequence(rows, token)) return;
            if (_unknownMaximums[act - 1].HasValue) continue;
            var key = OverviewActKey(act);
            try
            {
                if (!key.IsValid) _unknownMaximums[act - 1] = -1;
                else
                {
                    var map = await GetOverviewMap(act, key, token);
                    if (!IsCurrentUnknownSequence(rows, token)) return;
                    RestoreDrawnRoute(map);
                    _unknownMaximums[act - 1] = new MapRouteSet(map, MapPointType.Unknown).Value;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                if (!IsCurrentUnknownSequence(rows, token)) return;
                _unknownMaximums[act - 1] = -1;
                RuntimeIssueLedger.Current.Record(ex, "UnknownSequenceMapFailed", nameof(AnalysisPage),
                    "UnknownSequenceMap", "issues.capture_failed", correlationId: $"seed={seed};act={act}");
            }
        }
        if (IsCurrentUnknownSequence(rows, token)) RefreshUnknownSequenceRows();
    }

    private bool IsCurrentUnknownSequence(Control rows, CancellationToken token) =>
        !token.IsCancellationRequested && ReferenceEquals(rows, _unknownSequenceRows) &&
        IsInstanceValid(rows) && rows.IsInsideTree();

    private void RefreshUnknownSequenceRows()
    {
        var rows = _unknownSequenceRows;
        if (rows is null || !IsInstanceValid(rows) || !rows.IsInsideTree()) return;
        WClear(rows);
        int? priorDraws = 0;
        for (int index = 0; index < 3; index++)
        {
            int act = index + 1;
            if (index <= _playedUnknownCounts.Length) priorDraws = _playedUnknownCounts.Take(index).Sum();
            var row = new HBoxContainer { Name = $"UnknownAct{act}" };
            row.AddThemeConstantOverride("separation", 6);
            rows.AddChild(row);
            var title = WLabel(Text(PageKeys[index]), fontSize: 16);
            title.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            title.AutowrapMode = TextServer.AutowrapMode.Off;
            row.AddChild(title);
            if (_unknownMaximums[index] is not { } maximum || maximum < 0)
            {
                row.AddChild(WNote(Text(_unknownMaximums[index].HasValue ? "ui1.map.unavailable" : "predictor.unknown_series.loading")));
                priorDraws = null;
                continue;
            }

            var route = _overviewRoutes.GetValueOrDefault(act);
            int autoCount = maximum;
            if (route is not null)
            {
                var visited = route.Coordinates.ToHashSet();
                autoCount = route.Map.Points.Count(p => p.Type == MapPointType.Unknown && visited.Contains(p.Position));
            }
            bool completed = index < _playedUnknownCounts.Length;
            if (completed) autoCount = _playedUnknownCounts[index];
            int count = completed ? autoCount : Math.Min(_unknownChosenCounts[index] ?? autoCount, maximum);
            var picker = new OptionButton { Name = $"UnknownCount{act}" };
            picker.AddThemeFontSizeOverride("font_size", 15);
            picker.AddItem(_uiText!.Format(completed ? "predictor.map.visits" :
                route is null ? "predictor.unknown_series.maximum" : "predictor.unknown_series.route", autoCount));
            for (int n = 0; n <= maximum; n++) picker.AddItem(_uiText.Format("predictor.unknown_series.count", n));
            picker.Disabled = completed;
            picker.Select(!completed && _unknownChosenCounts[index].HasValue ? count + 1 : 0);
            picker.ItemSelected += item =>
            {
                _unknownChosenCounts[act - 1] = item == 0 ? null : (int)item - 1;
                RefreshUnknownSequenceRows();
            };
            row.AddChild(picker);
            if (priorDraws is null)
            {
                row.AddChild(WNote(Text("predictor.unknown_series.missing_prior")));
                continue;
            }
            var marks = _unknownShopMarks[index];
            int offset = priorDraws.Value;
            var buttons = new List<Button>();
            for (int ordinal = 1; ordinal <= count; ordinal++)
            {
                int position = ordinal;
                var button = _palette.CompactButton("", 34, 15);
                button.ClipText = false;
                button.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
                button.Name = $"Unknown{act}_{position}";
                button.ToggleMode = true;
                button.AddThemeConstantOverride("icon_max_width", 30);
                button.CustomMinimumSize = new(60, 38);
                var mark = new ColorRect
                {
                    Name = "ShopMark", Color = _palette.Confirmed, MouseFilter = Control.MouseFilterEnum.Ignore
                };
                button.AddChild(mark);
                mark.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
                mark.OffsetLeft = 6;
                mark.OffsetRight = -6;
                mark.OffsetTop = -4;
                mark.OffsetBottom = -2;
                row.AddChild(button);
                buttons.Add(button);
                button.Pressed += () =>
                {
                    if (!marks.Add(position)) marks.Remove(position);
                    ShowResults();
                };
            }
            void ShowResults()
            {
                // Act entry resets the odds, while every entered ordinary question
                // consumes one draw even when shops are blocked.
                var steps = OrdinaryUnknownRoomPredictor.Baseline(_unknownSequenceSeed, offset, count, marks).Steps;
                foreach (var step in steps)
                {
                    var button = buttons[step.Ordinal - 1];
                    string room = Text("ui1.map.room." + step.Room);
                    button.Icon = UnknownResultIcon(step.Room);
                    button.Text = button.Icon is null ? $"{step.Ordinal} {room}" : step.Ordinal.ToString();
                    button.AccessibilityName = $"{step.Ordinal} {room}" +
                        (step.ShopBlacklisted ? " · " + Text("predictor.unknown_series.blocked") : "");
                    button.GetNode<ColorRect>("ShopMark").Visible = step.ShopBlacklisted;
                    button.SetPressedNoSignal(step.ShopBlacklisted);
                    _palette.SetModeActive(button, step.ShopBlacklisted, horizontalPadding: 8);
                }
            }
            ShowResults();
            priorDraws += count;
        }
    }
}
