using Godot;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private Button _seeds = null!;
    private SeedLibraryCanvas? _seedLibrary;
    private readonly Dictionary<BaseButton, bool> _seedModalBlocked = [];
    private string? _pendingLibraryIssue;
    private CardCollectionStore? _cardCollections;

    private CardCollectionStore CardCollections => _cardCollections ??= new(
        Path.Combine(_persistenceDirectory, "RolltheSpire2", "state"));

    private void InitializeLibraryNavigation()
    {
        _seeds.Pressed += () => SelectTask(Workspace.Seeds);
        if (_references is null) return;
        _references.ConfigureCardCollections(CardCollections);
        _references.CardCollectionEditRequested += id =>
        {
            try
            {
                EnsureSeedLibrary();
                SelectTask(Workspace.Seeds);
                _seedLibrary!.OpenCardCollection(id, returnToSearch: true);
            }
            catch (Exception ex) { _references.ShowLibraryReceipt(SeedLibraryIssue(ex)); }
        };
        _references.PresetsRequested += () =>
        {
            try
            {
                EnsureSeedLibrary();
                SelectTask(Workspace.Seeds);
                _seedLibrary!.ShowSection(SeedLibraryCanvas.LibrarySection.Presets);
            }
            catch (Exception ex) { _references.ShowLibraryReceipt(SeedLibraryIssue(ex)); }
        };
        _references.FavoriteSeedRequested += action =>
            FavoriteSeed(action.Result.Seed, action.Context!, action.Association, action.Provenance);
        _references.PresetsChanged += () => _seedLibrary?.RefreshEntries();
        _references.PresetIssueReported += message =>
        {
            if (_seedLibrary is { } library) library.ShowIssue(message);
            else _pendingLibraryIssue = message;
        };
    }

    private void EnsureSeedLibrary()
    {
        if (_seedLibrary is not null) return;
        PrepareDeferredPage();
        InitializeSeedLibrary(_persistenceDirectory);
        if (_pendingLibraryIssue is { } issue) { _seedLibrary!.ShowIssue(issue); _pendingLibraryIssue = null; }
    }

    private void InitializeSeedLibrary(string userDataDirectory)
    {
        var store = new SeedLibraryStore(Path.Combine(userDataDirectory, "RolltheSpire2", "state"));
        _seedLibrary = new(store, _references is null ? null : _persistence, _languageCode, CardCollections) { Visible = false };
        _content.AddChild(_seedLibrary); _seedLibrary.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _seedLibrary.AttachOverlay(this);
        _seedLibrary.CardCollectionReturnRequested += () => SelectTask(Workspace.Search);
        _seedLibrary.ModalChanged += open =>
        {
            if (open)
            {
                // The overlay owns pointer input and a closed keyboard-focus loop.
                // Search may finish behind it, so do not snapshot/restore mutable
                // content-button states over the session's newer state.
                foreach (var button in new[] { _search, _analysis, _currentPrediction, _seeds, _encyclopedia, _status, _notes, _settings, _feedback, _close })
                { _seedModalBlocked.TryAdd(button, button.Disabled); button.Disabled = true; }
            }
            else
            {
                foreach (var (button, disabled) in _seedModalBlocked)
                    if (IsInstanceValid(button)) button.Disabled = disabled;
                _seedModalBlocked.Clear();
            }
        };
        _seedLibrary.ReceiptShown += ShowSeedLibraryReceipt;
        _seedLibrary.OpenRequested += entry =>
        {
            try
            {
                if (_predictor is null) CreatePredictor();
                _predictorController!.OpenSeedLibraryEntry(entry);
                SelectTask(Workspace.Analysis);
                _predictor!.ShowLibraryReceipt(entry.Source == SeedLibrarySource.Developer
                    ? (_languageCode == "zh" ? "已按推荐配置重新预测；游玩说明见资料库。" : "Predicted using the recommended configuration. See the library for play instructions.")
                    : (_languageCode == "zh" ? "已按保存的上下文重新预测；收藏备注仅供参考。" : "Predicted again using the saved context. Saved notes are historical."));
            }
            catch (Exception ex) { _seedLibrary.ShowIssue(SeedLibraryIssue(ex)); }
        };
        if (_references is not null)
        {
            _seedLibrary.SearchBusy = () => _references.HasActiveSearch;
            _seedLibrary.PresetUseRequested += preset =>
            {
                try { _references.ApplyPreset(preset); SelectTask(Workspace.Search); }
                catch (Exception ex) { _seedLibrary.ShowIssue(ex.Message); }
            };
            _seedLibrary.QueryLoadRequested += (record, title) =>
            {
                try { _references!.LoadQueryHistory(record, title); SelectTask(Workspace.Search); }
                catch (Exception ex) { _seedLibrary.ShowIssue(SeedLibraryIssue(ex)); }
            };
            _seedLibrary.PresetHistoryRequested += preset =>
            {
                try { _seedLibrary.ShowPresetResults(preset); }
                catch (Exception ex) { _seedLibrary.ShowIssue(ex.Message); }
            };
            _seedLibrary.PresetEditRequested += (preset, metadata) =>
            {
                try { _references.EditLibraryPreset(preset, metadata); }
                catch (Exception ex) { _seedLibrary.ShowIssue(ex.Message); }
            };
            _seedLibrary.PresetEnvironmentRequested += preset =>
            {
                try { _references.RefreshPresetEnvironment(preset); }
                catch (Exception ex) { _seedLibrary.ShowIssue(ex.Message); }
            };
        }
        _seedLibrary.ResultOpenRequested += OpenSavedSearchResult;
    }

    private void FavoriteSeed(string seed, SeedLibraryContext context, SeedQueryAssociation? association = null,
        SeedLibraryResultProvenance? provenance = null)
    {
        try { EnsureSeedLibrary(); _seedLibrary!.OpenSave(seed, context, queryKey: association?.QueryKey, association: association, resultProvenance: provenance); }
        catch (Exception ex) { ShowSeedLibraryReceipt(SeedLibraryIssue(ex)); }
    }

    private void FavoriteCurrentPrediction()
    {
        try
        {
            var context = _predictorController!.CaptureSeedLibraryContext();
            var origin = _predictorController.CaptureResultOrigin(context);
            FavoriteSeed(_predictor!.LastDocument!.CanonicalSeed, context, origin.Association, origin.Provenance);
        }
        catch (Exception ex) { ShowSeedLibraryReceipt(SeedLibraryIssue(ex)); }
    }

    private void ShowSeedLibraryReceipt(string message)
    {
        if (_workspace == Workspace.Search) _references?.ShowLibraryReceipt(message);
        else if (_workspace == Workspace.Analysis) _predictor?.ShowLibraryReceipt(message);
    }

    private string SeedLibraryIssue(Exception ex) => (_languageCode == "zh"
        ? "无法使用此上下文，原收藏已保留。请使用兼容版本，或复制种子后重新配置预测。\n"
        : "This context could not be used. The original favorite is preserved. Use a compatible version, or copy the seed and configure a new prediction.\n") + ex.Message;
}
