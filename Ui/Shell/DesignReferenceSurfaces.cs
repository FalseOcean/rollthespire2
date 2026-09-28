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
    internal WorkbenchSearchDraft CaptureFeedbackDraft() => _workbench.CaptureDraft().WithoutCapturedAuthority();
    internal void SyncSearchPreferences() => _workbench.SyncSearchPreferences();
    internal void ShowLibraryReceipt(string message) => _workbench.ShowLibraryReceipt(message);
    internal event Action<string, SeedLibraryContext>? FavoriteSeedRequested;
    public event Action<RolltheSpire2.Search.Contracts.SearchCandidate, WorkbenchSearchDraft>? OpenPartyInformation;
    public event Action<RolltheSpire2.Search.Contracts.SearchCandidate>? OpenSeedInformation;

    public DesignReferenceSurfaces(ModRuntimeSnapshot runtime, SearchWorkspacePersistence persistence)
    {
        _workbench = new QueryWorkbenchFrame(runtime, persistence) { Visible = false };
        _workbench.Navigate += Navigate;
        _workbench.OpenPartyInformation += (candidate, draft) => OpenPartyInformation?.Invoke(candidate, draft);
        _workbench.OpenSeedInformation += candidate => OpenSeedInformation?.Invoke(candidate);
        _workbench.FavoriteSeedRequested += (seed, context) => FavoriteSeedRequested?.Invoke(seed, context);
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
