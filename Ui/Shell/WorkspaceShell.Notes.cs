using Godot;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.DeveloperNotes;
using RolltheSpire2.Presentation.DeveloperNotes;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.DeveloperNotes;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private DeveloperNotesPage? _notesPage;

    private void OpenNotes()
    {
        if (_notesPage is not null) return;
        _notesPage = new DeveloperNotesPage(new DeveloperNotesDocumentProvider()) { Name = "DeveloperNotesPage" };
        _content.AddChild(_notesPage);
        _notesPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        RefreshNotes();
        _notesPage.OpenChapter(DeveloperNotesChapterIds.Release);
    }

    private void RefreshNotes() => _notesPage?.ApplyLocalization(JsonUiTextProvider.Create(_languageCode),
        RuntimeGameContentNameResolver.Create(_languageCode));
}
