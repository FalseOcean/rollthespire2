using Godot;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.DeveloperNotes;
using RolltheSpire2.Presentation.DeveloperNotes;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.DeveloperNotes;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private readonly DeveloperNotesDocumentProvider _notesProvider = new();
    private DeveloperNotesDocument? _notesDocument;
    private DeveloperNotesPage? _notesPage;
    private VBoxContainer? _notesHost;
    private bool _notesPending;
    private const double NotesPulseDuration = 4.8;
    private double _notesPulseElapsed = NotesPulseDuration;

    private void OpenNotes()
    {
        if (_notesPage is not null) { RefreshNotesReminder(); return; }
        _notesHost = new VBoxContainer { Name = "DeveloperNotesHost" };
        _content.AddChild(_notesHost);
        _notesHost.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _notesPage = new DeveloperNotesPage(_notesProvider, chapter => _persistence!.NeedsNotesAcknowledgement(chapter))
            { Name = "DeveloperNotesPage" };
        _notesPage.AcknowledgementRequested += chapter =>
        {
            _persistence!.AcknowledgeNotesChapter(chapter);
            RefreshNotesReminder();
        };
        _notesHost.AddChild(_notesPage);
        RefreshNotes();
        _notesPage.OpenChapter(DeveloperNotesChapterIds.Release);
    }

    private void RefreshNotes()
    {
        _notesDocument = _notesProvider.Resolve(_languageCode).Document;
        _notesPage?.ApplyLocalization(JsonUiTextProvider.Create(_languageCode), RuntimeGameContentNameResolver.Create(_languageCode));
        RefreshNotesReminder();
    }

    private void RefreshNotesReminder()
    {
        _notesPending = _notesDocument?.Chapters.Any(chapter => _persistence!.NeedsNotesAcknowledgement(chapter)) == true;
        var text = JsonUiTextProvider.CreateUi13(_languageCode);
        _notes.Text = text.Get("shell.about") + (_notesPending ? "  •" : "");
        _notes.TooltipText = text.Get(_notesPending ? "notes.unread" : "shell.about_hint");
        if (!_notesPending) _notesPulseElapsed = NotesPulseDuration;
        ApplyNotesReminderColor();
        _notesPage?.RefreshAcknowledgements();
    }

    private void BeginNotesReminder()
    {
        RefreshNotesReminder();
        _notesPulseElapsed = _notesPending ? 0 : NotesPulseDuration;
        ApplyNotesReminderColor();
    }

    private void TickNotesReminder(double delta)
    {
        if (!_notesPending || _notesPulseElapsed >= NotesPulseDuration || !IsVisibleInTree()) return;
        _notesPulseElapsed = Math.Min(NotesPulseDuration, _notesPulseElapsed + delta);
        ApplyNotesReminderColor();
    }

    private void ApplyNotesReminderColor()
    {
        // Three soft pulses on opening, then steady gold. Never hide the label
        // or restart the animation merely because the reader switches pages.
        float pulse = _notesPulseElapsed < NotesPulseDuration
            ? (float)(.5 - .5 * Math.Cos(_notesPulseElapsed * Math.Tau / 1.6)) : 0;
        Color normal = _notesPending ? new Color("DCC58D").Lerp(new Color("FFE6AD"), pulse)
            : _palette.Color(_workspace == Workspace.Notes ? _palette.Text : _palette.Secondary);
        _notes.AddThemeColorOverride("font_color", normal);
        foreach (string state in new[] { "font_hover_color", "font_pressed_color", "font_focus_color" })
            _notes.AddThemeColorOverride(state, _notesPending ? normal : _palette.Color(_palette.Text));
    }
}
