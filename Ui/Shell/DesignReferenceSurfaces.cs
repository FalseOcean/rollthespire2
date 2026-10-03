using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

/// <summary>Hosts the production query workbench and its authored-preset transactions.</summary>
internal sealed partial class DesignReferenceSurfaces : Control
{
    private readonly QueryWorkbenchFrame _workbench;
    public event Action<bool>? ModalChanged;
    public event Action<string?>? EncyclopediaRequested;
    internal WorkbenchSearchDraft? PartyInformationDraft => _workbench.PartyInformationDraft;
    internal bool HasActiveSearch => _workbench.HasActiveSearch;
    internal WorkbenchSearchDraft? ResultDraft => _workbench.ResultDraft;
    internal SeedLibraryContext? ResultSeedContext => _workbench.ResultSeedContext;
    internal int ResultCount => _workbench.ResultCount;
    internal void ShowLastResults() => _workbench.ShowLastResults();
    internal void EditLibraryPreset(SearchPresetDefinition? preset, bool metadata) => _workbench.EditLibraryPreset(preset, metadata);
    internal void ApplyLibraryPreset(SearchPresetDefinition preset) => _workbench.ApplyPreset(preset);
    internal void RefreshPresetEnvironment(SearchPresetDefinition preset) => _workbench.RefreshPresetEnvironment(preset);
    internal SearchPresetDefinition PreparePresetHistory(SearchPresetDefinition preset) => _workbench.PreparePresetHistory(preset);
    internal void LoadQueryHistory(QueryHistoryEntry entry, string title) => _workbench.LoadQueryHistory(entry, title);
    internal event Action? PresetsRequested;
    internal event Action? PresetsChanged;
    internal event Action<string>? PresetIssueReported;
    internal event Action? ResultsChanged;
    internal event Action<PersistedSearchResult>? OpenSavedResultRequested;
    internal WorkbenchSearchDraft CaptureFeedbackDraft() => _workbench.CaptureDraft().WithoutCapturedAuthority();
    internal void SyncSearchPreferences() => _workbench.SyncSearchPreferences();
    internal object? CaptureSearchDiagnostics() => _workbench.CaptureSearchDiagnostics();
    internal void ShowLibraryReceipt(string message) => _workbench.ShowLibraryReceipt(message);
    internal event Action<string, SeedLibraryContext, SeedQueryAssociation?>? FavoriteSeedRequested;
    public event Action<RolltheSpire2.Search.Contracts.SearchCandidate, WorkbenchSearchDraft>? OpenPartyInformation;
    public event Action<RolltheSpire2.Search.Contracts.SearchCandidate>? OpenSeedInformation;

    public DesignReferenceSurfaces(ModRuntimeSnapshot runtime, SearchWorkspacePersistence persistence)
    {
        _workbench = new QueryWorkbenchFrame(runtime, persistence) { Visible = false };
        _workbench.Navigate += Navigate;
        _workbench.OpenPartyInformation += (candidate, draft) => OpenPartyInformation?.Invoke(candidate, draft);
        _workbench.OpenSeedInformation += candidate => OpenSeedInformation?.Invoke(candidate);
        _workbench.FavoriteSeedRequested += (seed, context, key) => FavoriteSeedRequested?.Invoke(seed, context, key);
        _workbench.PresetsRequested += () => PresetsRequested?.Invoke();
        _workbench.PresetsChanged += () => PresetsChanged?.Invoke();
        _workbench.PresetIssueReported += issue => PresetIssueReported?.Invoke(issue);
        _workbench.ResultsChanged += () => ResultsChanged?.Invoke();
        _workbench.OpenSavedResultRequested += result => OpenSavedResultRequested?.Invoke(result);
        _workbench.ModalChanged += open => ModalChanged?.Invoke(open);
        Name = "DesignReferenceSurfaces";
        AddChild(_workbench); _workbench.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
    public void AttachOverlay(Control shell)
    {
        _workbench.AttachOverlay(shell);
    }
    public void Refresh(WorkspacePalette palette, string language, IUiTextProvider text)
    {
        CloseModal(); _workbench.Visible = true;
        _workbench.Refresh(language, text);
    }
    internal void CloseSearch() => _workbench.CloseSearch();
    public void Home() { CloseModal(); _workbench.Visible = true; }
    private void Navigate(string page)
    {
        CloseModal();
        if (page == "encyclopedia" || page.StartsWith("encyclopedia:", StringComparison.Ordinal))
        {
            EncyclopediaRequested?.Invoke(page == "encyclopedia" ? null : page["encyclopedia:".Length..]);
            return;
        }
        _workbench.Visible = true;
    }
    public bool CloseModal() => _workbench.CloseModal();

}
