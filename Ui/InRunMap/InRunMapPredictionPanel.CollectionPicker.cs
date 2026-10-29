using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapPredictionPanel
{
    private OptionButton _collectionPicker = null!;
    private Label _collectionPickerIssue = null!;
    private DrawingCardCollectionPreferenceStore _collectionPreferenceStore = null!;
    private CardCollectionStore _collectionLibrary = null!;
    private readonly List<CardCollectionSnapshot?> _collectionChoices = [];
    private string? _collectionPreferenceIssue;

    private string CollectionText(string key) => _text.Get("predictor.map.card_collection." + key);
    private string OverviewCollectionName => _overviewCardCollection?.Name ?? CollectionText("all_rare");

    private void InitializeCollectionPicker()
    {
        string state = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state");
        _collectionPreferenceStore = new(state);
        _collectionLibrary = new(state);
        _collectionPicker = new OptionButton { Name = "OverviewCardCollectionPicker", FitToLongestItem = false,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Stop, Theme = MainPanelTheme.Create() };
        MainPanelTheme.ApplyOptionButton(_collectionPicker);
        MainPanelTheme.ApplyFont(_collectionPicker);
        _collectionPicker.GetPopup().Theme = _collectionPicker.Theme;
        AddChild(_collectionPicker);
        _collectionPickerIssue = new Label { Name = "OverviewCardCollectionIssue", Visible = false,
            MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        MainPanelTheme.ApplyFont(_collectionPickerIssue);
        _collectionPickerIssue.AddThemeFontSizeOverride("font_size", 14);
        _collectionPickerIssue.AddThemeColorOverride("font_color", new Color("f1b08a"));
        AddChild(_collectionPickerIssue);
        _collectionPicker.GetPopup().AboutToPopup += () =>
        {
            CancelStroke();
            _details.CloseHover();
            PopulateOverviewCollections();
        };
        _collectionPicker.ItemSelected += SelectOverviewCollection;
        ReloadOverviewCollectionPreference();
        PopulateOverviewCollections();
        UpdateCollectionPicker();
    }

    private void ReloadOverviewCollectionPreference()
    {
        try
        {
            _overviewCardCollection = _collectionPreferenceStore.Load()?.Copy();
            _collectionPreferenceIssue = null;
        }
        catch (Exception error)
        {
            // Preserve the current choice and the unreadable file. On a first
            // opening the explicit message discloses the all-rare default.
            _collectionPreferenceIssue = "load_failed";
            RuntimeLog.WarnException("inRunMapCollectionPreferenceFailed=true;stage=load", error);
        }
        RefreshOverviewCardCollection();
    }

    private static bool SameOverviewCollection(CardCollectionSnapshot? left, CardCollectionSnapshot? right) =>
        left == null || right == null ? left == right : left.Id == right.Id && left.Name == right.Name &&
            left.Revision == right.Revision && left.Cards.SequenceEqual(right.Cards);

    private void PopulateOverviewCollections()
    {
        _collectionPicker.Clear();
        _collectionChoices.Clear();
        _collectionChoices.Add(null);
        _collectionPicker.AddItem(CollectionText("all_rare"), 0);
        _collectionLibrary.Refresh();
        foreach (var collection in _collectionLibrary.GetAll().OrderBy(value => value.Name, StringComparer.CurrentCulture))
        {
            _collectionChoices.Add(collection);
            _collectionPicker.AddItem(collection.Name, _collectionChoices.Count - 1);
        }
        int selected = _collectionChoices.FindIndex(value => SameOverviewCollection(value, _overviewCardCollection));
        if (selected < 0)
        {
            selected = _collectionChoices.Count;
            _collectionChoices.Add(_overviewCardCollection!.Copy());
            _collectionPicker.AddItem(_text.Format("predictor.map.card_collection.saved_version", OverviewCollectionName), selected);
        }
        if (_collectionLibrary.LoadIssues.Count > 0)
        {
            _collectionPicker.AddItem(CollectionText("library_failed"));
            _collectionPicker.SetItemDisabled(_collectionPicker.ItemCount - 1, true);
        }
        _collectionPicker.Select(selected);
        UpdateCollectionPicker();
    }

    private void SelectOverviewCollection(long index)
    {
        if (index < 0 || index >= _collectionChoices.Count) return;
        var selection = _collectionChoices[(int)index]?.Copy();
        try
        {
            // A failed save must not pretend the new selection was remembered.
            _collectionPreferenceStore.Save(selection);
            _collectionPreferenceIssue = null;
            if (selection == null) UseDefaultOverviewCardCollection();
            else UseOverviewCardCollection(selection);
        }
        catch (Exception error)
        {
            _collectionPreferenceIssue = "save_failed";
            RuntimeLog.WarnException("inRunMapCollectionPreferenceFailed=true;stage=save", error);
            int previous = _collectionChoices.FindIndex(value => SameOverviewCollection(value, _overviewCardCollection));
            if (previous >= 0) _collectionPicker.Select(previous);
        }
        UpdateCollectionPicker();
    }

    private void UpdateCollectionPicker()
    {
        if (_collectionPicker == null) return;
        _collectionPickerIssue.Text = _collectionPreferenceIssue == "load_failed"
            ? _text.Format("predictor.map.card_collection.load_failed", OverviewCollectionName)
            : _collectionPreferenceIssue is { } issue ? CollectionText(issue) : "";
        bool visible = _featureEnabled && _screen.IsOpen && IsVisibleInTree() &&
            ActiveScreenContext.Instance.IsCurrent(_screen);
        _collectionPicker.Visible = visible;
        _collectionPickerIssue.Visible = visible && _collectionPickerIssue.Text.Length > 0;
        if (!visible) { _collectionPicker.GetPopup().Hide(); return; }
        _collectionPicker.Text = _text.Format("predictor.map.card_collection.current", OverviewCollectionName);
        var inverse = GetGlobalTransform().AffineInverse();
        var canvas = GetCanvasTransform().AffineInverse() * GetViewportRect();
        var bounds = inverse * _screen.GetGlobalRect().Intersection(canvas);
        var tools = _screen.GetNodeOrNull<Control>("%DrawingTools");
        var area = tools == null ? new Rect2(bounds.Position.X + 12, bounds.End.Y - 50, 0, 38) : inverse * tools.GetGlobalRect();
        float width = Math.Max(1, Math.Min(280, bounds.Size.X - 24));
        _collectionPicker.CustomMinimumSize = new(width, 38);
        _collectionPicker.Size = new(width, Math.Max(38, _collectionPicker.GetCombinedMinimumSize().Y));
        var position = new Vector2(area.End.X + 12, area.GetCenter().Y - _collectionPicker.Size.Y * .5f);
        if (position.X + width > bounds.End.X - 12)
            position = new(area.Position.X, area.Position.Y - _collectionPicker.Size.Y - 8);
        position.X = Math.Clamp(position.X, bounds.Position.X + 12, Math.Max(bounds.Position.X + 12, bounds.End.X - width - 12));
        position.Y = Math.Clamp(position.Y, bounds.Position.Y + 12,
            Math.Max(bounds.Position.Y + 12, bounds.End.Y - _collectionPicker.Size.Y - 12));
        _collectionPicker.Position = position;
        _collectionPickerIssue.CustomMinimumSize = new(width, 0);
        _collectionPickerIssue.Size = new(width, _collectionPickerIssue.GetMinimumSize().Y);
        _collectionPickerIssue.Position = position - new Vector2(0, _collectionPickerIssue.Size.Y + 6);
        _collectionPicker.GetPopup().MaxSize = new((int)Math.Max(1, bounds.Size.X - 24), (int)Math.Max(1, bounds.Size.Y * .7f));
    }

    private bool CollectionPickerBlocksMap(Vector2 global) => _collectionPicker != null &&
        (_collectionPicker.GetPopup().Visible || _collectionPicker.IsVisibleInTree() && _collectionPicker.GetGlobalRect().HasPoint(global));
}
