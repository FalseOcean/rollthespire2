using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private readonly HashSet<int> _preparedPageText = [], _builtPageViews = [], _presentedPageViews = [];
    // Views own their unfinished construction. Navigation only borrows it.
    private readonly Dictionary<int, IEnumerator<object?>> _pageViewBuilds = [];
    private readonly Dictionary<int, Exception> _pageBuildFailures = [];
    private readonly List<(Control Parent, Control Group)> _pageBuildGroups = [];
    private MainPanelFontPreparation? _pageTextPreparation;
    private int _preparingPageText = -1, _requestedPage = -1;
    private string _pageTextContext = "";
    private Action? _pendingPageNavigation;
    private Workspace _preparationReturn;
    private int _preparationCategoryReturn;
    private PanelContainer? _pagePreparationNotice;
    private Label? _pagePreparationLabel;
    private bool _preparingSettingsCategory, _pageNavigationApplied, _buildingPageView;
    private Action? _pageLayoutPreDraw, _pageLayoutPostDraw;
    private ulong _lastPageBuildFrame = ulong.MaxValue;
    private long _pageNavigationVersion;
    private long _pagePreparationIdleAt = System.Diagnostics.Stopwatch.GetTimestamp();
    private bool _applyingPreparedPage;

    // A notice, partial view or pending native layout is not a usable page.
    internal bool IsCurrentPageReady => _pendingPageNavigation is null &&
        (!IsVisibleInTree() || ((_workspace != Workspace.Seeds || _seedLibrary?.IsPresentationReady != false) &&
            (_workspace != Workspace.CurrentPrediction || _crystalScene?.IsPresentationReady != false) &&
            (_workspace != Workspace.Notes || _notesPage?.IsPresentationReady != false)));
    internal bool HasCurrentPageFailure => (_requestedPage >= 0 && _pageBuildFailures.ContainsKey(_requestedPage)) ||
        (_workspace == Workspace.Seeds && _seedLibrary?.HasPresentationFailure == true) ||
        (_workspace == Workspace.Notes && _notesPage?.HasPresentationFailure == true);

    private bool DeferWorkspacePage(Workspace workspace, Action navigate)
    {
        if (_applyingPreparedPage) return false;
        CancelPageNavigation();
        RefreshPageTextContext();
        int page = workspace switch { Workspace.Settings => _settingsCategory, Workspace.Feedback => 4, _ => -1 };
        return page >= 0 && !_presentedPageViews.Contains(page) && DeferPage(page, workspace, navigate);
    }

    private bool DeferSettingsCategory(int index)
    {
        if (_applyingPreparedPage) return false;
        CancelPageNavigation();
        RefreshPageTextContext();
        return !_presentedPageViews.Contains(index) &&
            DeferPage(index, Workspace.Settings, () => SelectSettingsCategory(index), category: true);
    }

    private bool DeferPage(int page, Workspace workspace, Action navigate, bool category = false)
    {
        // Explicit pre-tree construction has no frame pump. Player navigation
        // reaches this pipeline after tree attachment.
        if (!IsInsideTree()) return false;
        RefreshPageTextContext();
        _preparationReturn = _workspace;
        _preparationCategoryReturn = _settingsCategory;
        _preparingSettingsCategory = category;
        _requestedPage = page;
        _notesPage?.SetPresentationActive(false);
        _pendingPageNavigation = navigate;
        _workspace = workspace;
        _references?.CloseModal();
        ShowPagePreparationNotice();
        SetWorkspaceActive();
        AwaitPageLayout();
        TickPagePreparation();
        return true;
    }

    private void ShowPagePreparationNotice()
    {
        if (_pagePreparationNotice is null)
        {
            _pagePreparationNotice = new PanelContainer { Name = "PagePreparation",
                MouseFilter = MouseFilterEnum.Stop, FocusMode = FocusModeEnum.All };
            _pagePreparationNotice.AddThemeStyleboxOverride("panel", _palette.ContentBox());
            _pagePreparationLabel = _palette.Label("", 18, true);
            _pagePreparationLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _pagePreparationLabel.VerticalAlignment = VerticalAlignment.Center;
            _pagePreparationNotice.AddChild(_pagePreparationLabel);
            _content.AddChild(_pagePreparationNotice);
        }
        var text = JsonUiTextProvider.CreateUi13(_languageCode);
        _pagePreparationLabel!.Text = _pageBuildFailures.TryGetValue(_requestedPage, out var failure)
            ? text.Get("shell.page_failed") + "\n" + failure.Message : text.Get("shell.preparing_page");
        if (_preparingSettingsCategory && _settingsPage is not null)
        {
            _pagePreparationNotice.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            Transform2D transform = _content.GetGlobalTransformWithCanvas().AffineInverse() * _settingsPages.GetGlobalTransformWithCanvas();
            _pagePreparationNotice.Position = transform.Origin;
            _pagePreparationNotice.Size = _settingsPages.Size * transform.Scale;
        }
        else _pagePreparationNotice.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _content.MoveChild(_pagePreparationNotice, _content.GetChildCount() - 1);
        _pagePreparationNotice.Show();
        _pagePreparationNotice.GrabFocus();
    }

    private void CancelPageNavigation()
    {
        _pageNavigationVersion++;
        DisconnectPageLayout();
        bool restore = _pendingPageNavigation is not null && _pageNavigationApplied;
        if (_pendingPageNavigation is not null) _workspace = _preparationReturn;
        _pendingPageNavigation = null;
        _requestedPage = -1;
        _pageNavigationApplied = false;
        _pagePreparationNotice?.Hide();
        if (restore)
        {
            _applyingPreparedPage = true;
            try
            {
                if (_preparingSettingsCategory && _builtPageViews.Contains(_preparationCategoryReturn))
                    SelectSettingsCategory(_preparationCategoryReturn);
                SelectTask(_preparationReturn);
            }
            finally { _applyingPreparedPage = false; }
        }
        if (_preparingSettingsCategory)
            for (int i = 0; i < _settingsCategories.Count; i++)
                _settingsCategories[i].Button.SetPressedNoSignal(i == _settingsCategory);
        _preparingSettingsCategory = false;
    }

    private void StopPagePreparation(bool retire = false)
    {
        if (retire) _notesPage?.RetirePresentation();
        else _notesPage?.PausePresentation();
        CancelPageNavigation();
        _pageTextPreparation?.Dispose();
        _pageTextPreparation = null;
        _preparingPageText = -1;
        DeferIdlePagePreparation();
        if (!retire) return;
        foreach (var build in _pageViewBuilds.Values) build.Dispose();
        _pageViewBuilds.Clear();
        // Unmounted groups are not children of the shell.
        foreach (var (_, group) in _pageBuildGroups) group.Free();
        _pageBuildGroups.Clear();
        _pageBuildFailures.Clear();
        RetirePageBuildGroups();
    }

    private void RefreshPageTextContext()
    {
        string context = MainPanelTheme.FontChoice + ":" + _languageCode;
        if (_pageTextContext == context) return;
        _pageTextPreparation?.Dispose();
        _pageTextPreparation = null;
        _preparingPageText = -1;
        _preparedPageText.Clear();
        _presentedPageViews.Clear();
        _pageTextContext = context;
        // A font/language change after mounting belongs to a new preparation
        // pass. A draw from the previous text context cannot deliver this page.
        if (_pageNavigationApplied)
        {
            _pageNavigationApplied = false;
            ShowPagePreparationNotice();
        }
    }

    private bool AdvancePageText(int page)
    {
        if (_preparedPageText.Contains(page)) return true;
        if (_preparingPageText != page || _pageTextPreparation is null)
        {
            _pageTextPreparation?.Dispose();
            _pageTextPreparation = null;
            _preparingPageText = page;
            try { _pageTextPreparation = new(PageViewText(page)); }
            catch (Exception ex) { RuntimeLog.Warn("pageFontPreparationUnavailable=true;page=" + page + ";detail=" + ex); }
        }
        long fontStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (_pageTextPreparation is not null && !_pageTextPreparation.Advance()) return false;
        }
        catch (Exception ex)
        {
            // Optional warming never owns page admission or success.
            RuntimeLog.Warn("pageFontPreparationFailed=true;page=" + page + ";detail=" + ex);
        }
        finally
        {
            double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(fontStarted).TotalMilliseconds;
            if (RuntimeLog.DetailEnabled && elapsed >= 8)
                RuntimeLog.Detail(FormattableString.Invariant($"pageFontBatch=true;page={page};elapsedMs={elapsed:F3};requested={_pendingPageNavigation is not null}"));
        }
        _pageTextPreparation?.Dispose();
        _pageTextPreparation = null;
        _preparingPageText = -1;
        _preparedPageText.Add(page);
        return true;
    }

    private IEnumerable<object?> BuildPageView(int page)
    {
        PrepareDeferredPage();
        if (page < 4)
        {
            _settingsText = JsonUiTextProvider.CreateUi13(_languageCode);
            if (_settingsPage is null) { BuildSettings(); yield return null; }
            foreach (var step in BuildSettingsCategory(page)) yield return step;
        }
        else foreach (var step in BuildFeedback()) yield return step;
    }

    private void AttachPageBuildGroup(Control parent, Control group)
    {
        if (_buildingPageView) _pageBuildGroups.Add((parent, group));
        else parent.AddChild(group);
    }

    private void AdvancePageView(int page)
    {
        if (_builtPageViews.Contains(page)) return;
        if (!AdvancePageBuildGroups(page)) return;
        if (!_finishedPageBuilds.Contains(page))
        {
            if (!_pageViewBuilds.TryGetValue(page, out var build))
                _pageViewBuilds[page] = build = BuildPageView(page).GetEnumerator();
            _buildingPageView = true;
            bool more;
            try { more = build.MoveNext(); }
            finally
            {
                _buildingPageView = false;
                foreach (var (parent, group) in _pageBuildGroups)
                {
                    if (!_pendingPageGroups.TryGetValue(page, out var groups))
                        _pendingPageGroups[page] = groups = new();
                    groups.Enqueue(new(parent, group, _pageTextContext));
                }
                _pageBuildGroups.Clear();
            }
            BindPageBuildText(page);
            if (!more) _finishedPageBuilds.Add(page);
            if (!AdvancePageBuildGroups(page)) return;
        }
        if (!_finishedPageBuilds.Contains(page)) return;
        if (_pageViewBuilds.Remove(page, out var finished)) finished.Dispose();
        _finishedPageBuilds.Remove(page);
        _builtPageViews.Add(page);
        _preparedPageText.Add(page);
    }

    private void EnsurePageViewSynchronously(int page)
    {
        // Live navigation has already completed its view before Ensure runs.
        while (!_builtPageViews.Contains(page)) AdvancePageView(page);
    }

    private void DeferIdlePagePreparation() => _pagePreparationIdleAt = System.Diagnostics.Stopwatch.GetTimestamp();

    private void DisconnectPageLayout()
    {
        if (_pageLayoutPreDraw is not null) RenderingServer.FramePreDraw -= _pageLayoutPreDraw;
        if (_pageLayoutPostDraw is not null) RenderingServer.FramePostDraw -= _pageLayoutPostDraw;
        _pageLayoutPreDraw = _pageLayoutPostDraw = null;
    }

    private bool OwnsPageLayout(long version, string context)
    {
        if (version != _pageNavigationVersion || _pendingPageNavigation is null) return false;
        if (!IsInsideTree() || !IsVisibleInTree()) { StopPagePreparation(); return false; }
        if (!_pageNavigationApplied) return false;
        if (context != MainPanelTheme.FontChoice + ":" + _languageCode)
        {
            RefreshPageTextContext();
            return false;
        }
        return true;
    }

    private void AwaitPageLayout()
    {
        DisconnectPageLayout();
        long version = _pageNavigationVersion;
        string? drawnContext = null;
        // Native Container sorts drain after SceneTree processing and before
        // drawing. PreDraw removes the covering notice; PostDraw confirms that
        // this request's fully mounted page has actually passed that boundary.
        _pageLayoutPreDraw = () =>
        {
            drawnContext = null;
            if (!OwnsPageLayout(version, _pageTextContext)) return;
            drawnContext = _pageTextContext;
            _pagePreparationNotice?.Hide();
        };
        _pageLayoutPostDraw = () =>
        {
            if (drawnContext is null || !OwnsPageLayout(version, drawnContext)) return;
            DisconnectPageLayout();
            _presentedPageViews.Add(_requestedPage);
            _pendingPageNavigation = null;
            _requestedPage = -1;
            _pageNavigationApplied = false;
            DeferIdlePagePreparation();
            if (_workspace == Workspace.Settings) _settingsCategories[_settingsCategory].Button.GrabFocus();
            _preparingSettingsCategory = false;
        };
        // Register at admission, before an awaiting caller can subscribe to the
        // same boundary. Completion is not delayed by that observer's ordering.
        RenderingServer.FramePreDraw += _pageLayoutPreDraw;
        RenderingServer.FramePostDraw += _pageLayoutPostDraw;
    }

    private void TickPagePreparation()
    {
        if (!IsVisibleInTree()) { StopPagePreparation(); return; }
        RefreshPageTextContext();
        if (_pendingPageNavigation is null)
        {
            // Idle means a usable current view and a real quiet interval, not
            // the global frame delta that also contains this shell's startup.
            if (!IsCurrentPageReady) { DeferIdlePagePreparation(); return; }
            if (System.Diagnostics.Stopwatch.GetElapsedTime(_pagePreparationIdleAt).TotalSeconds < 1) return;
            int next = new[] { 0, 4, 1, 2, 3 }.FirstOrDefault(x => _builtPageViews.Contains(x) && !_preparedPageText.Contains(x), -1);
            if (next >= 0) AdvancePageText(next);
            return;
        }
        if (_pageNavigationApplied) return;
        if (_pageBuildFailures.ContainsKey(_requestedPage)) return;
        // Navigation may start between Process callbacks. Use the current slice
        // immediately, without applying another full slice in the same frame.
        ulong frame = Engine.GetProcessFrames();
        if (_lastPageBuildFrame == frame) return;
        _lastPageBuildFrame = frame;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_builtPageViews.Contains(_requestedPage) && !AdvancePageText(_requestedPage)) return;
        if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 6) return;
        try
        {
            // Cheap warm groups share the same slice instead of paying an
            // unconditional frame each. Include native minimum-size work and
            // leave deferred layout a frame boundary between expensive batches.
            while (!_builtPageViews.Contains(_requestedPage))
            {
                AdvancePageView(_requestedPage);
                if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 6) break;
            }
            if (!_builtPageViews.Contains(_requestedPage)) return;
            var navigate = _pendingPageNavigation;
            _applyingPreparedPage = true;
            try { navigate(); }
            finally { _applyingPreparedPage = false; }
            _pageNavigationApplied = true;
            ShowPagePreparationNotice();
        }
        catch (Exception ex)
        {
            DisconnectPageLayout();
            _pageBuildFailures[_requestedPage] = ex;
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                "PageConstructionFailed", nameof(WorkspaceShell), "BuildPage:" + _requestedPage, "shell.page_failed");
            if (_pageViewBuilds.Remove(_requestedPage, out var failed)) failed.Dispose();
            ShowPagePreparationNotice();
        }
    }
}
