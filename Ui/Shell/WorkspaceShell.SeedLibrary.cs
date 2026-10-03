using Godot;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private readonly Button _seeds = MakeButton();
    private SeedLibraryCanvas? _seedLibrary;
    private readonly Dictionary<BaseButton, bool> _seedModalBlocked = [];

    private void InitializeSeedLibrary(string userDataDirectory)
    {
        var store = new SeedLibraryStore(Path.Combine(userDataDirectory, "RolltheSpire2", "state"));
        _seedLibrary = new(store, _references is null ? null : _persistence) { Visible = false };
        _content.AddChild(_seedLibrary); _seedLibrary.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _seedLibrary.AttachOverlay(this);
        _seedLibrary.ModalChanged += open =>
        {
            if (open)
            {
                // The overlay owns pointer input and a closed keyboard-focus loop.
                // Search may finish behind it, so do not snapshot/restore mutable
                // content-button states over the session's newer state.
                foreach (var button in new[] { _search, _analysis, _currentPrediction, _seeds, _encyclopedia, _status, _notes, _settings, _close })
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
                _partyExpected = null; _partyResultDraft = null;
                SelectTask(Workspace.Analysis);
                _predictor!.ShowLibraryReceipt(entry.Source == SeedLibrarySource.Developer
                    ? (_languageCode == "zh" ? "已按推荐配置重新预测；游玩说明见种子库。" : "Predicted using the recommended configuration. See the seed library for play instructions.")
                    : (_languageCode == "zh" ? "已按保存的上下文重新预测；收藏备注仅供参考。" : "Predicted again using the saved context. Saved notes are historical."));
            }
            catch (Exception ex) { _seedLibrary.ShowIssue(SeedLibraryIssue(ex)); }
        };
        if (_references is not null)
        {
            _seedLibrary.SearchBusy = () => _references.HasActiveSearch;
            _seedLibrary.PresetUseRequested += preset =>
            {
                try { _references.ApplyLibraryPreset(preset); SelectTask(Workspace.Search); }
                catch (Exception ex) { _seedLibrary.ShowIssue(ex.Message); }
            };
            _seedLibrary.QueryLoadRequested += (record, title) =>
            {
                try { _references!.LoadQueryHistory(record, title); SelectTask(Workspace.Search); }
                catch (Exception ex) { _seedLibrary.ShowIssue(SeedLibraryIssue(ex)); }
            };
            _seedLibrary.PresetHistoryRequested += preset =>
            {
                try { _seedLibrary.ShowPresetResults(_references.PreparePresetHistory(preset)); }
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
            _references.PresetsChanged += _seedLibrary.RefreshEntries;
            _references.PresetIssueReported += _seedLibrary.ShowIssue;
            _references.PresetsRequested += () =>
            { SelectTask(Workspace.Seeds); _seedLibrary.ShowSection(SeedLibraryCanvas.LibrarySection.Presets); };
        }
        _seedLibrary.ResultOpenRequested += OpenSavedSearchResult;
        _seeds.Pressed += () => SelectTask(Workspace.Seeds);
        if(_references!=null) _references.FavoriteSeedRequested += FavoriteSeed;
    }

    private void FavoriteSeed(string seed, SeedLibraryContext context, SeedQueryAssociation? association = null)
    {
        try { _seedLibrary!.OpenSave(seed, context, queryKey: association?.QueryKey, association: association); }
        catch (Exception ex) { ShowSeedLibraryReceipt(SeedLibraryIssue(ex)); }
    }

    private void FavoriteCurrentPrediction()
    {
        try
        {
            var context = _predictorController!.CaptureSeedLibraryContext();
            FavoriteSeed(_predictor!.LastDocument!.CanonicalSeed, context);
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
