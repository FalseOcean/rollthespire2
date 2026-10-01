using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Persistence;
using System.Text.Json;

namespace RolltheSpire2.Ui.Shell;

// Read-only projection over the native cells. Mouse input passes through to
// vanilla; this control never invokes CellClicked, changes tools or takes rewards.
internal sealed partial class CrystalSphereGuidance : Control
{
    // Session-local presentation preference, controlled by the prediction panel.
    internal static bool DisplayEnabled { get; set; } = true;
    private IRunState _run = null!;
    private ModRuntimeSnapshot _runtime = null!;
    private EventModel _event = null!;
    private PredictorCrystalSolution _solution = null!;
    private readonly List<PredictorCrystalSnapshot> _prefixes = [];
    private readonly List<(Rect2 Rect, int X, int Y)> _cells = [];
    private Label _label = null!;
    private HBoxContainer _banner = null!;
    private IUiTextProvider _text = null!;
    private NCrystalSphereScreen? _screen;
    private int _progress;
    private bool _started, _stopped;
    private double _poll;
    private string? _lastFingerprint;
    private string T(string key) => _text.Get("predictor.crystal." + key);

    internal void Initialize(IRunState run, ModRuntimeSnapshot runtime, CrystalSphereLiveSnapshot snapshot,
        string mode, PredictorCrystalSolution solution)
    {
        Name = "CrystalGuidance"; MouseFilter = MouseFilterEnum.Ignore;
        _run = run; _runtime = runtime; _event = ((EventRoom)run.CurrentRoom!).LocalMutableEvent;
        _solution = solution;
        var source = snapshot.Branches.Single(b => b.Mode == mode).Snapshot;
        _prefixes.Add(source);
        var replay = PredictorRun.FromCrystal(source);
        foreach (var step in solution.Steps)
        {
            if (replay.Submit(replay.Request!, new SelectCrystalTool(step.Tool)) != PredictorInputResult.Accepted ||
                replay.Submit(replay.Request!, new RevealCrystalCell(step.X, step.Y)) != PredictorInputResult.Accepted)
                throw new InvalidOperationException("CrystalGuideInvalidWitness");
            _prefixes.Add(replay.ExportCrystal());
        }
        var preferences = new SearchWorkspacePersistence(OS.GetUserDataDir(), runtime.Profile.ProfileId, false).Preferences;
        string language = preferences.LanguageOverride is "zh" or "en" ? preferences.LanguageOverride :
            TranslationServer.GetLocale().StartsWith("zh") ? "zh" : "en";
        _text = JsonUiTextProvider.CreatePredictorUi13(language);
        _banner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_banner);
        _label = new Label { Text = string.Format(T("guide_wait"), T(mode)), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 20);
        _label.AddThemeColorOverride("font_color", new Color("ffe5a0"));
        _label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _label.AddThemeConstantOverride("shadow_outline_size", 5);
        _banner.AddChild(_label);
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren()) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    // Capture IDs are newly assigned; compare the ordered actual instances, not
    // those capture-local IDs. Tool selection is freely changeable by the player.
    internal static bool Matches(PredictorCrystalSnapshot expected, PredictorCrystalSnapshot actual, bool terminal)
    {
        if (expected.Remaining != actual.Remaining || expected.PlacedAllItems != actual.PlacedAllItems ||
            !expected.Hidden.SequenceEqual(actual.Hidden) || !expected.Occupancy.SequenceEqual(actual.Occupancy) ||
            !expected.Items.SequenceEqual(actual.Items) || !expected.Revealed.SequenceEqual(actual.Revealed)) return false;
        if (terminal) return true; // native reward materialization follows its completion delay
        static string State(PredictorState state) => JsonSerializer.Serialize(new {
            state.GoldBudget, cards = state.Deck.Select(id => state.Card(id) with { Id = 0 }),
            relics = state.Relics.Select(r => r with { Id = 0 }),
            potions = state.PotionSlots.Select(p => p == null ? null : p with { Id = 0 }),
            state.Streams, state.Odds, state.PersonalBag, state.SharedBag });
        return expected.EventRng == actual.EventRng && State(expected.State) == State(actual.State);
    }
    public override void _Process(double delta)
    {
        if (_run.CurrentRoom is not EventRoom room || !ReferenceEquals(room.LocalMutableEvent, _event)) { QueueFree(); return; }
        var top = NOverlayStack.Instance?.Peek();
        bool sceneVisible = top == null || top is NCrystalSphereScreen;
        Visible = DisplayEnabled && sceneVisible;
        if (!sceneVisible) return; // hidden guidance still tracks manual reveals
        _poll -= delta; if (_poll > 0) return; _poll = .2;
        if (_screen == null || !IsInstanceValid(_screen))
            _screen = Descendants(GetTree().Root).OfType<NCrystalSphereScreen>().FirstOrDefault();
        var screen = _screen;
        _banner.Position = new(24, Math.Max(24, Size.Y - 96));
        _banner.Size = new(Math.Clamp(Size.X - 48, 100, 620), 72);
        _cells.Clear();
        if (screen != null && screen.IsVisibleInTree() && !_stopped)
            foreach (var cell in Descendants(screen).OfType<NCrystalSphereCell>())
            {
                var rect = cell.GetGlobalRect();
                var inv = GetGlobalTransform().AffineInverse();
                _cells.Add((new Rect2(inv * rect.Position, inv * rect.End - inv * rect.Position), cell.Entity.X, cell.Entity.Y));
            }
        QueueRedraw();
        if (_stopped || _progress == _solution.Steps.Length) return;
        var game = CrystalSphereLiveCapture.FindGame(GetTree().Root);
        if (game == null) { if (_event.IsFinished) QueueFree(); else if (_started) Stop(); return; }
        if (CrystalSphereLiveCapture.IsClickPending(game)) return;
        int progress = _prefixes[0].Remaining - game.DivinationCount;
        if (progress < _progress || progress < 0 || progress >= _prefixes.Count) { Stop(); return; }
        try
        {
            var fingerprint = CrystalSphereLiveCapture.SceneFingerprint(_run, _run.Players[0], _event, game);
            if (fingerprint == _lastFingerprint) return;
            var actual = CrystalSphereLiveCapture.CaptureScene(_run, _run.Players[0], _event, game, _runtime.Detection.DisplayVersion).Branches[0].Snapshot;
            if (!Matches(_prefixes[progress], actual, progress == _solution.Steps.Length)) { Stop(); return; }
            _lastFingerprint = fingerprint; _started = true; _progress = progress;
            _label.Text = progress == _solution.Steps.Length ? T("guide_done") :
                string.Format(T("guide_step"), progress + 1, _solution.Steps.Length, T(_solution.Steps[progress].Tool.ToString()));
        }
        catch (Exception ex) { RuntimeLog.WarnException("crystalGuideStopped=true", ex); Stop(); }
    }
    private void Stop() { _stopped = true; _cells.Clear(); _label.Text = T("guide_stale"); QueueRedraw(); }
    public override void _Draw()
    {
        if (!_started || _stopped || _progress >= _solution.Steps.Length) return;
        var next = _solution.Steps[_progress];
        var affected = PredictorCrystalSearch.Cells(next.X, next.Y, next.Tool);
        foreach (var cell in _cells)
        {
            if (!_prefixes[0].Hidden[cell.X * 11 + cell.Y]) continue;
            DrawRect(cell.Rect, new Color(1, 1, 1, .22f), false, 1);
            if (affected.Contains((cell.X, cell.Y))) DrawRect(cell.Rect, new Color(1, .8f, .2f, .18f));
            int index = Enumerable.Range(_progress, _solution.Steps.Length - _progress)
                .FirstOrDefault(i => _solution.Steps[i].X == cell.X && _solution.Steps[i].Y == cell.Y, -1);
            if (index < 0) continue;
            DrawRect(cell.Rect.Grow(-2), index == _progress ? Colors.Gold : new Color(.4f, .9f, 1, .6f), false, index == _progress ? 3 : 1);
            DrawString(ThemeDB.FallbackFont, cell.Rect.Position + new Vector2(8, 24), (index + 1).ToString(), fontSize: 24,
                modulate: index == _progress ? Colors.Gold : Colors.White);
        }
    }
}
