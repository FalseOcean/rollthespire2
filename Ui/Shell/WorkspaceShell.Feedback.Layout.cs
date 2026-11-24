using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private void EnsureFeedback()
    {
        EnsurePageViewSynchronously(4);
        RefreshFeedback();
    }

    private IEnumerable<object?> BuildFeedback()
    {
        // Complete each authored group off-tree. Its actual control text and
        // effective fonts are prepared before mounting; hidden details stay cold.
        _feedbackPage = new Control { Name = "FeedbackPage", Visible = false };
        _content.AddChild(_feedbackPage); _feedbackPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var page = new VBoxContainer(); page.AddThemeConstantOverride("separation", 24);
        _feedbackPage.AddChild(page); page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Label Text(string key, int size = 18, bool muted = false)
        {
            var label = _palette.Label("", size, muted); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.Text = JsonUiTextProvider.CreateUi13(_languageCode).Get(key);
            _feedbackLabels.Add((label, key)); return label;
        }
        Button Action(string key, System.Action action, bool primary = false)
        {
            var button = _palette.Button(JsonUiTextProvider.CreateUi13(_languageCode).Get(key), primary); button.CustomMinimumSize = new(112, 44);
            button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            button.AddThemeFontSizeOverride("font_size", 18); button.Pressed += action;
            _feedbackButtons.Add((button, key)); return button;
        }
        PanelContainer Panel(string fill, int padding = 24)
        {
            var panel = new PanelContainer(); var style = _palette.Box(fill);
            style.ContentMarginLeft = style.ContentMarginRight = padding;
            style.ContentMarginTop = style.ContentMarginBottom = padding;
            panel.AddThemeStyleboxOverride("panel", style); return panel;
        }
        HFlowContainer Actions(VBoxContainer parent)
        {
            var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 12);
            row.AddThemeConstantOverride("v_separation", 8); AttachPageBuildGroup(parent, row); return row;
        }
        // Reserve the first layout position before deferred font preparation
        // mounts the complete heading group.
        var headingHost = new VBoxContainer { Name = "FeedbackHeading" }; page.AddChild(headingHost);
        var heading = new VBoxContainer(); heading.AddThemeConstantOverride("separation", 14); AttachPageBuildGroup(headingHost, heading);
        var titleRow = new HBoxContainer(); heading.AddChild(titleRow);
        var title = Text("feedback.title", 30); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; titleRow.AddChild(title);
        var tag = Text("feedback.tag", 15, true); tag.AutowrapMode = TextServer.AutowrapMode.Off;
        tag.VerticalAlignment = VerticalAlignment.Center; titleRow.AddChild(tag);
        heading.AddChild(Text("feedback.intro", 18, true));

        // Keep the normal page compact; retain scrolling for expanded details and smaller windows.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        page.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16); scroll.AddChild(body);
        var cards = new HBoxContainer { Name = "FeedbackSteps" };
        cards.AddThemeConstantOverride("separation", 16); body.AddChild(cards);
        yield return "chrome";
        VBoxContainer Card(string step, string titleKey, out HBoxContainer header)
        {
            var panel = Panel(_palette.Surface); panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var style = (StyleBoxFlat)panel.GetThemeStylebox("panel");
            style.ContentMarginTop = style.ContentMarginBottom = 22;
            panel.SizeFlagsStretchRatio = 1; AttachPageBuildGroup(cards, panel);
            var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 12); panel.AddChild(column);
            header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 12); column.AddChild(header);
            var badge = Panel(_palette.Canvas, 6); badge.CustomMinimumSize = new(36, 36);
            badge.SizeFlagsVertical = SizeFlags.ShrinkCenter; header.AddChild(badge);
            var number = _palette.Label(step, 15);
            MainPanelTheme.SetColor(number, "font_color", _palette.Active);
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.VerticalAlignment = VerticalAlignment.Center; badge.AddChild(number);
            var label = Text(titleKey, 22); label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.VerticalAlignment = VerticalAlignment.Center; header.AddChild(label);
            return column;
        }
        var export = Card("01", "feedback.export.title", out var exportHeader);
        _feedbackState = _palette.Label("", 15, true);
        _feedbackState.VerticalAlignment = VerticalAlignment.Center; exportHeader.AddChild(_feedbackState);
        var exportHelp = Text("feedback.export.help", 17, true); exportHelp.CustomMinimumSize = new(0, 48); export.AddChild(exportHelp);
        yield return "export-header";
        var chips = Actions(export);
        foreach (string key in new[] { "feedback.chip.rt2", "feedback.chip.game", "feedback.chip.context" })
        {
            var chip = Panel(_palette.Canvas, 8); var label = Text(key, 14, true);
            label.AutowrapMode = TextServer.AutowrapMode.Off; chip.AddChild(label); chips.AddChild(chip);
        }
        var exportActions = Actions(export);
        _feedbackExport = Action("feedback.export", StartFeedbackExport, true); _feedbackExport.Name = "FeedbackExport";
        exportActions.AddChild(_feedbackExport);
        _feedbackStatus = _palette.Label("", 16, true); _feedbackStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; AttachPageBuildGroup(export, _feedbackStatus);
        yield return "export-actions";
        _feedbackArtifact = Panel(_palette.Canvas, 16); _feedbackArtifact.Visible = _feedbackResult is not null; AttachPageBuildGroup(export, _feedbackArtifact);
        var artifact = new VBoxContainer(); artifact.AddThemeConstantOverride("separation", 10); _feedbackArtifact.AddChild(artifact);
        var fileRow = new HBoxContainer(); fileRow.AddThemeConstantOverride("separation", 12); artifact.AddChild(fileRow);
        var zip = _palette.Label("ZIP", 14); MainPanelTheme.SetColor(zip, "font_color", _palette.Active); fileRow.AddChild(zip);
        _feedbackFileName = _palette.Label("", 16, true); _feedbackFileName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _feedbackFileName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; _feedbackFileName.ClipText = true;
        fileRow.AddChild(_feedbackFileName);
        var fileActions = Actions(artifact);
        _feedbackFolder = Action("feedback.folder", OpenFeedbackFolder); fileActions.AddChild(_feedbackFolder);
        _feedbackCopy = Action("feedback.copy", () =>
        {
            if (_feedbackResult is null) return;
            DisplayServer.ClipboardSet(_feedbackResult.Path); _feedbackMessage = "feedback.copied"; RefreshFeedback();
        }); fileActions.AddChild(_feedbackCopy);
        _feedbackWarnings = _palette.Label("", 16); _feedbackWarnings.AddThemeColorOverride("font_color", _palette.Warning);
        _feedbackWarnings.AutowrapMode = TextServer.AutowrapMode.WordSmart; AttachPageBuildGroup(export, _feedbackWarnings);
        yield return "export-artifact";

        var submit = Card("02", "feedback.submit.title", out _);
        var submitHelp = Text("feedback.submit.help", 17, true); submitHelp.CustomMinimumSize = new(0, 48); submit.AddChild(submitHelp);
        var checklist = new VBoxContainer(); checklist.AddThemeConstantOverride("separation", 8); submit.AddChild(checklist);
        foreach (string key in new[] { "feedback.submit.describe", "feedback.submit.attach" }) checklist.AddChild(Text(key, 16, true));
        var submitActions = Actions(submit);
        _feedbackIssue = Action("feedback.github", () => RequestFeedbackIssue("github"), true); submitActions.AddChild(_feedbackIssue);
        _feedbackCopyIssue = Action("feedback.copy_issue", () => RequestFeedbackIssue("copy")); submitActions.AddChild(_feedbackCopyIssue);
        var emailRow = Actions(submit);
        var email = _palette.Label(FeedbackEmail, 16, true); email.CustomMinimumSize = new(0, 44);
        email.VerticalAlignment = VerticalAlignment.Center; emailRow.AddChild(email);
        emailRow.AddChild(Action("feedback.email", () =>
        { DisplayServer.ClipboardSet(FeedbackEmail); _feedbackMessage = "feedback.email_copied"; RefreshFeedback(); }));
        submit.AddChild(Text("feedback.submit.public", 15, true));
        yield return "submit";

        _feedbackNotice = Panel(_palette.ContentSurface, 16); _feedbackNotice.Visible = _feedbackMessage.Length > 0; AttachPageBuildGroup(body, _feedbackNotice);
        _feedbackReceipt = _palette.Label("", 17); _feedbackReceipt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _feedbackNotice.AddChild(_feedbackReceipt);
        yield return "notice";
        var rule = new ColorRect { CustomMinimumSize = new(0, 1), MouseFilter = MouseFilterEnum.Ignore };
        MainPanelTheme.SetBackground(rule, _palette.Line);
        body.AddChild(rule);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 40); body.AddChild(footer);
        var support = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        support.AddThemeConstantOverride("separation", 10); AttachPageBuildGroup(footer, support);
        support.AddChild(Text("feedback.boundary", 16, true));
        _feedbackDetailsToggle = Action("feedback.details", () =>
        { _feedbackDetails.Visible = !_feedbackDetails.Visible; RefreshFeedback(); });
        _feedbackDetailsToggle.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); support.AddChild(_feedbackDetailsToggle);
        _feedbackDetails = Panel(_palette.Surface, 20); _feedbackDetails.Visible = false; AttachPageBuildGroup(body, _feedbackDetails);
        var details = new VBoxContainer(); details.AddThemeConstantOverride("separation", 12); _feedbackDetails.AddChild(details);
        details.AddChild(Text("feedback.privacy", 16, true));
        details.AddChild(Text("feedback.details.help", 16, true));
        _feedbackPath = new LineEdit { Editable = false, ExpandToTextLength = false, CustomMinimumSize = new(0, 38) };
        StyleSettingsInput(_feedbackPath); _feedbackPath.AddThemeFontSizeOverride("font_size", 15); details.AddChild(_feedbackPath);
        yield return "details";
        var versions = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        versions.AddThemeConstantOverride("separation", 8); AttachPageBuildGroup(footer, versions);
        versions.AddChild(Text("feedback.versions.title", 18));
        versions.AddChild(Text("feedback.versions.help", 16, true));
        versions.AddChild(Action("feedback.versions.open", () =>
        {
            _feedbackMessage = TryOpenExternalUrl("https://github.com/FalseOcean/rollthespire2/releases")
                ? "" : "feedback.versions.open_failed";
            RefreshFeedback();
        }));
        yield return "versions";
    }

    private void RefreshFeedback()
    {
        IUiTextProvider text = JsonUiTextProvider.CreateUi13(_languageCode);
        foreach (var (label, key) in _feedbackLabels) label.Text = text.Get(key);
        foreach (var (button, key) in _feedbackButtons) button.Text = text.Get(key);
        bool busy = _feedbackTask is not null;
        if (_feedbackExport is not null)
        {
            _feedbackExport.Disabled = busy;
            _feedbackExport.Text = text.Get(_feedbackResult is not null ? "feedback.export.again" : "feedback.export");
        }
        if (_feedbackIssue is not null) _feedbackIssue.Disabled = busy;
        if (_feedbackCopyIssue is not null) _feedbackCopyIssue.Disabled = busy;
        if (_feedbackFolder is not null) _feedbackFolder.Disabled = _feedbackResult is null;
        if (_feedbackCopy is not null) _feedbackCopy.Disabled = _feedbackResult is null;
        if (_feedbackArtifact is not null) _feedbackArtifact.Visible = _feedbackResult is not null;
        if (_feedbackPath is not null)
        {
            _feedbackPath.Visible = _feedbackResult is not null;
            _feedbackPath.Text = _feedbackResult?.Path ?? "";
            _feedbackPath.TooltipText = _feedbackPath.Text;
        }
        if (_feedbackFileName is not null)
        {
            _feedbackFileName.Text = _feedbackResult is { } file ? Path.GetFileName(file.Path) : "";
            _feedbackFileName.TooltipText = _feedbackResult?.Path ?? "";
        }
        if (_feedbackState is not null)
        {
            _feedbackState.Text = text.Get(busy ? "feedback.state.busy" : _feedbackResult is not null ? "feedback.state.ready" : "feedback.state.idle");
            MainPanelTheme.SetColor(_feedbackState, "font_color", _feedbackResult is not null && !busy ? _palette.Active : _palette.Secondary);
        }
        if (_feedbackStatus is not null) _feedbackStatus.Text = busy ? text.Get("feedback.working") : _feedbackResult is { } result
            ? text.Format("feedback.ready", result.Logs, (result.Bytes / 1048576d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
            : text.Get("feedback.idle");
        var warnings = new List<string>();
        if (!busy && _feedbackResult is { Warnings: > 0 } partial) warnings.Add(text.Format("feedback.partial", partial.Warnings));
        if (!busy && _feedbackResult is { Oversized: true }) warnings.Add(text.Get("feedback.oversized"));
        if (_feedbackWarnings is not null)
        {
            _feedbackWarnings.Text = string.Join("\n", warnings); _feedbackWarnings.Visible = warnings.Count > 0;
        }
        if (_feedbackNotice is not null) _feedbackNotice.Visible = _feedbackMessage.Length > 0;
        if (_feedbackReceipt is not null)
        {
            _feedbackReceipt.Text = _feedbackMessage.Length > 0 ? text.Get(_feedbackMessage) : "";
            if (_feedbackError.Length > 0) _feedbackReceipt.Text += " (" + _feedbackError + ")";
            _feedbackReceipt.AddThemeColorOverride("font_color", _feedbackMessage.Contains("failed", StringComparison.Ordinal) ? _palette.Warning : _palette.Color(_palette.Text));
        }
        if (_feedbackDetailsToggle is not null) _feedbackDetailsToggle.Text = (_feedbackDetails?.Visible == true ? "⌄  " : "›  ") + text.Get("feedback.details");
    }
}
