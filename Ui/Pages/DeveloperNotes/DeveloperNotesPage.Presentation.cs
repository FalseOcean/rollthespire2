using System.Diagnostics;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.DeveloperNotes;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.DeveloperNotes;

internal sealed partial class DeveloperNotesPage
{
    private DeveloperNotesChapter? _buildingChapter;
    private int _nextBlock, _restorePosition, _restoreStage;
    private bool _articleComplete, _presentationReady, _usefulContentReady, _retired, _readerMoved;
    private bool _hasReadableBlock;
    private bool _presentationActive = true;
    private Exception? _presentationFailure;
    private string _presentationFont = string.Empty;
    private long _presentationRevision;
    private Action? _articlePreDraw, _articlePostDraw;

    internal bool IsPresentationReady => !_retired && _presentationActive && _presentationFailure is null && _presentationReady &&
        _presentationFont == MainPanelTheme.FontChoice;
    internal bool IsUsefulContentReady => !_retired && _presentationActive && _usefulContentReady &&
        _presentationFont == MainPanelTheme.FontChoice;
    internal bool HasPresentationFailure => _presentationFailure is not null;
    internal Exception? PresentationFailure => _presentationFailure;

    private void BeginArticlePresentation()
    {
        DisconnectArticleDraw();
        _presentationRevision++;
        _presentationFont = MainPanelTheme.FontChoice;
        _buildingChapter = null;
        _nextBlock = _restoreStage = 0;
        _restorePosition = _readingPositions.GetValueOrDefault(ReadingKey);
        _restoringPosition = true;
        _articleComplete = _presentationReady = _usefulContentReady = _readerMoved = _hasReadableBlock = false;
        _presentationFailure = null;
        _renderer.ClearBlocks();
        _acknowledge.Disabled = true;
        _articlePreparation.Text = Local("本篇内容正在准备，已显示的段落可以阅读。", "Preparing this chapter. Completed paragraphs are available to read.");
        _articlePreparation.Show();
        _progress.Text = Local("内容准备中", "Preparing content");
        ResetArticleScroll();
    }

    public override void _Process(double delta)
    {
        if (_retired || !_presentationActive || !IsVisibleInTree()) return;
        try
        {
            if (_presentationFont != MainPanelTheme.FontChoice)
            {
                // Font replacement keeps the article and its unfinished block task.
                // Its native layout and restored position need a new draw receipt.
                SaveReadingPosition();
                DisconnectArticleDraw();
                _presentationRevision++;
                _presentationFont = MainPanelTheme.FontChoice;
                _restorePosition = _readingPositions.GetValueOrDefault(ReadingKey);
                _restoringPosition = true;
                _restoreStage = 0;
                _presentationReady = _usefulContentReady = false;
            }
            if (_presentationFailure is not null) return;
            AdvanceArticleBuild();
            if (!_presentationReady) AwaitArticleDraw();
        }
        catch (Exception ex) { FailArticlePresentation(ex); }
    }

    private void AdvanceArticleBuild()
    {
        if (_buildingChapter is not { } chapter) return;
        long started = Stopwatch.GetTimestamp();
        // A block is the smallest authored unit: never expose half a paragraph
        // or bullet list. One native block may exceed this soft time budget.
        do
        {
            if (_nextBlock >= chapter.Blocks.Count) break;
            DeveloperNotesBlock block = chapter.Blocks[_nextBlock];
            _renderer.AppendBlock(block);
            _nextBlock++;
            if (block.Type == DeveloperNotesBlockKinds.Heading)
                AppendReadingSection(_renderer.Sections[^1]);
            else _hasReadableBlock = true;
        }
        while (_nextBlock < chapter.Blocks.Count && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 6);
        if (_nextBlock == chapter.Blocks.Count) CompleteArticleBuild();
    }

    private void CompleteArticleBuild()
    {
        _buildingChapter = null;
        _articleComplete = true;
        _articlePreparation.Hide();
        RefreshAcknowledgements();
        _chapterFooter.Show();
        RuntimeLog.Detail($"developerNotesChapterRendered=true;chapter={_selectedChapterId};locale={_resolvedLocale}");
    }

    private bool OwnsArticleDraw(long revision, string font) => !_retired && _presentationActive && IsInsideTree() && IsVisibleInTree() &&
        revision == _presentationRevision && font == _presentationFont && font == MainPanelTheme.FontChoice &&
        _presentationFailure is null;

    private void AwaitArticleDraw()
    {
        if (_articlePreDraw is not null || !IsInsideTree() || !IsVisibleInTree()) return;
        long revision = _presentationRevision;
        string font = _presentationFont;
        bool drawn = false;
        _articlePreDraw = () =>
        {
            drawn = OwnsArticleDraw(revision, font);
            if (drawn && _articleComplete && _restoreStage == 1) _acknowledge.Disabled = false;
        };
        _articlePostDraw = () =>
        {
            if (!drawn || !OwnsArticleDraw(revision, font))
            {
                // A font can change while processing is paused. Drop this
                // receipt without disconnecting a newer revision's callbacks.
                if (revision == _presentationRevision) DisconnectArticleDraw();
                return;
            }
            DisconnectArticleDraw();
            try
            {
                // A saved deep reading target is not served by showing the top.
                // It becomes useful only after final layout and restoration.
                if (_restorePosition == 0 && _hasReadableBlock) _usefulContentReady = true;
                if (!_articleComplete) return;
                if (_restoreStage == 0)
                {
                    _articleScroll.ScrollVertical = _readerMoved ? _articleScroll.ScrollVertical : _restorePosition;
                    _restoringPosition = false;
                    UpdateReadingProgress();
                    _restoreStage = 1;
                    // The restored viewport must itself pass a native draw.
                    AwaitArticleDraw();
                    return;
                }
                _presentationReady = _usefulContentReady = true;
            }
            catch (Exception ex) { FailArticlePresentation(ex); }
        };
        RenderingServer.FramePreDraw += _articlePreDraw;
        RenderingServer.FramePostDraw += _articlePostDraw;
    }

    private void DisconnectArticleDraw()
    {
        if (_articlePreDraw is not null) RenderingServer.FramePreDraw -= _articlePreDraw;
        if (_articlePostDraw is not null) RenderingServer.FramePostDraw -= _articlePostDraw;
        _articlePreDraw = _articlePostDraw = null;
    }

    private void FailArticlePresentation(Exception ex)
    {
        DisconnectArticleDraw();
        _buildingChapter = null;
        _presentationFailure = ex;
        _articleComplete = _presentationReady = false;
        _acknowledge.Disabled = true;
        _restoringPosition = false;
        _acknowledgement.Hide();
        _chapterFooter.Hide();
        RuntimeLog.Fault($"developerNotesPresentationFailed=true;chapter={_selectedChapterId};locale={_resolvedLocale};block={_nextBlock};revision={_presentationRevision}", ex);
        _articlePreparation.Text = JsonUiTextProvider.CreateUi13(_uiText?.LanguageCode ?? "en").Get("shell.page_failed") + "\n" + ex.Message;
        _articlePreparation.Show();
    }

    private void TrackReadingInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true } or InputEventKey { Pressed: true } or
            InputEventPanGesture or InputEventScreenDrag) _readerMoved = true;
    }

    private void HandlePresentationVisibility()
    {
        if (!IsVisibleInTree()) PausePresentation();
    }

    internal void PausePresentation()
    {
        SaveReadingPosition();
        DisconnectArticleDraw();
        _presentationRevision++;
    }

    internal void SetPresentationActive(bool active)
    {
        if (_presentationActive == active) return;
        _presentationActive = active;
        if (!active) PausePresentation();
    }

    internal void RetirePresentation()
    {
        if (_retired) return;
        PausePresentation();
        _retired = true;
        _buildingChapter = null;
        _presentationReady = _usefulContentReady = false;
    }

    public override void _ExitTree() => RetirePresentation();
}
