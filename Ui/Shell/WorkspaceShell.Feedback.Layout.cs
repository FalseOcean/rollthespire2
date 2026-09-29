using Godot;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private void BuildFeedback()
    {
        _feedbackPage = new Control { Name = "FeedbackPage", Visible = false };
        _content.AddChild(_feedbackPage); _feedbackPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margins = new MarginContainer(); _feedbackPage.AddChild(margins);
        margins.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margins.AddThemeConstantOverride("margin_left", 64); margins.AddThemeConstantOverride("margin_right", 64);
        margins.AddThemeConstantOverride("margin_top", 12);
        var page = new VBoxContainer(); page.AddThemeConstantOverride("separation", 16); margins.AddChild(page);
        Label Text(string key, int size = 18, bool muted = false)
        {
            var label = _palette.Label("", size, muted); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _feedbackLabels.Add((label, key)); return label;
        }
        Button Action(string key, System.Action action, bool primary = false)
        {
            var button = _palette.Button("", primary); button.CustomMinimumSize = new(0, primary ? 48 : 40);
            button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            button.AddThemeFontSizeOverride("font_size", 18); button.Pressed += action;
            if (!primary) button.AddThemeStyleboxOverride("normal", _palette.Box(_palette.Canvas, _palette.Line, 1));
            _feedbackButtons.Add((button, key)); return button;
        }
        PanelContainer Panel(string fill, int padding = 24)
        {
            var panel = new PanelContainer(); var style = _palette.Box(fill);
            style.ContentMarginLeft = style.ContentMarginRight = padding;
            style.ContentMarginTop = style.ContentMarginBottom = padding;
            style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 10;
            panel.AddThemeStyleboxOverride("panel", style); return panel;
        }
        HFlowContainer Actions(VBoxContainer parent)
        {
            var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 10);
            row.AddThemeConstantOverride("v_separation", 8); parent.AddChild(row); return row;
        }
        var heading = new VBoxContainer(); heading.AddThemeConstantOverride("separation", 10); page.AddChild(heading);
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
        body.AddThemeConstantOverride("separation", 14); scroll.AddChild(body);
        var cards = new HBoxContainer(); cards.AddThemeConstantOverride("separation", 20); body.AddChild(cards);
        VBoxContainer Card(string step, string titleKey)
        {
            var panel = Panel(_palette.Surface, 20); panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            panel.SizeFlagsStretchRatio = 1; cards.AddChild(panel);
            var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); panel.AddChild(column);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); column.AddChild(row);
            var number = _palette.Label(step, 17); number.AddThemeColorOverride("font_color", _palette.Color(_palette.Active));
            number.VerticalAlignment = VerticalAlignment.Center; row.AddChild(number);
            var label = Text(titleKey, 24); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(label);
            return column;
        }
        var export = Card("01", "feedback.export.title");
        var exportHelp = Text("feedback.export.help", 18, true); exportHelp.CustomMinimumSize = new(0, 48); export.AddChild(exportHelp);
        var chips = Actions(export);
        foreach (string key in new[] { "feedback.chip.rt2", "feedback.chip.game", "feedback.chip.context" })
        {
            var chip = Panel(_palette.Canvas, 10); var label = Text(key, 15, true);
            label.AutowrapMode = TextServer.AutowrapMode.Off; chip.AddChild(label); chips.AddChild(chip);
        }
        var exportActions = new HBoxContainer(); exportActions.AddThemeConstantOverride("separation", 18); export.AddChild(exportActions);
        _feedbackExport = Action("feedback.export", StartFeedbackExport, true); _feedbackExport.Name = "FeedbackExport";
        exportActions.AddChild(_feedbackExport);
        _feedbackState = _palette.Label("", 16, true); _feedbackState.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _feedbackState.HorizontalAlignment = HorizontalAlignment.Right; _feedbackState.VerticalAlignment = VerticalAlignment.Center;
        exportActions.AddChild(_feedbackState);
        _feedbackStatus = _palette.Label("", 17, true); _feedbackStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; export.AddChild(_feedbackStatus);
        _feedbackArtifact = Panel(_palette.Canvas, 16); export.AddChild(_feedbackArtifact);
        var artifact = new VBoxContainer(); artifact.AddThemeConstantOverride("separation", 12); _feedbackArtifact.AddChild(artifact);
        var fileRow = new HBoxContainer(); fileRow.AddThemeConstantOverride("separation", 12); artifact.AddChild(fileRow);
        var zip = _palette.Label("ZIP", 14); zip.AddThemeColorOverride("font_color", _palette.Color(_palette.Active)); fileRow.AddChild(zip);
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
        _feedbackWarnings.AutowrapMode = TextServer.AutowrapMode.WordSmart; export.AddChild(_feedbackWarnings);

        var submit = Card("02", "feedback.submit.title");
        var submitHelp = Text("feedback.submit.help", 18, true); submitHelp.CustomMinimumSize = new(0, 48); submit.AddChild(submitHelp);
        var checklist = new VBoxContainer(); checklist.AddThemeConstantOverride("separation", 7); submit.AddChild(checklist);
        foreach (string key in new[] { "feedback.submit.describe", "feedback.submit.attach" }) checklist.AddChild(Text(key, 17, true));
        var submitActions = Actions(submit);
        _feedbackIssue = Action("feedback.github", () => RequestFeedbackIssue("github"), true); submitActions.AddChild(_feedbackIssue);
        _feedbackCopyIssue = Action("feedback.copy_issue", () => RequestFeedbackIssue("copy")); submitActions.AddChild(_feedbackCopyIssue);
        var emailRow = Actions(submit);
        var email = _palette.Label(FeedbackEmail, 16, true); email.CustomMinimumSize = new(0, 40);
        email.VerticalAlignment = VerticalAlignment.Center; emailRow.AddChild(email);
        emailRow.AddChild(Action("feedback.email", () =>
        { DisplayServer.ClipboardSet(FeedbackEmail); _feedbackMessage = "feedback.email_copied"; RefreshFeedback(); }));
        submit.AddChild(Text("feedback.submit.public", 15, true));

        _feedbackNotice = Panel("182433", 16); body.AddChild(_feedbackNotice);
        _feedbackReceipt = _palette.Label("", 17); _feedbackReceipt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _feedbackNotice.AddChild(_feedbackReceipt);
        var rule = new ColorRect { Color = _palette.Color(_palette.Line), CustomMinimumSize = new(0, 1), MouseFilter = MouseFilterEnum.Ignore };
        body.AddChild(rule);
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 32); body.AddChild(footer);
        var support = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        support.AddThemeConstantOverride("separation", 10); footer.AddChild(support);
        support.AddChild(Text("feedback.boundary", 16, true));
        _feedbackDetailsToggle = Action("feedback.details", () =>
        { _feedbackDetails.Visible = !_feedbackDetails.Visible; RefreshFeedback(); });
        _feedbackDetailsToggle.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); support.AddChild(_feedbackDetailsToggle);
        _feedbackDetails = new VBoxContainer { Visible = false }; ((VBoxContainer)_feedbackDetails).AddThemeConstantOverride("separation", 12);
        body.AddChild(_feedbackDetails);
        _feedbackDetails.AddChild(Text("feedback.privacy", 16, true));
        _feedbackDetails.AddChild(Text("feedback.details.help", 16, true));
        _feedbackPath = new LineEdit { Editable = false, ExpandToTextLength = false, CustomMinimumSize = new(0, 38) };
        StyleSettingsInput(_feedbackPath); _feedbackPath.AddThemeFontSizeOverride("font_size", 15); _feedbackDetails.AddChild(_feedbackPath);
        var versions = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        versions.AddThemeConstantOverride("separation", 8); footer.AddChild(versions);
        versions.AddChild(Text("feedback.versions.title", 20));
        versions.AddChild(Text("feedback.versions.help", 16, true));
        versions.AddChild(Action("feedback.versions.open", () =>
        {
            _feedbackMessage = TryOpenFeedbackUrl("https://github.com/FalseOcean/rollthespire2/releases")
                ? "" : "feedback.versions.open_failed";
            RefreshFeedback();
        }));
    }

    private void RefreshFeedback()
    {
        if (_feedbackPage is null) return;
        IUiTextProvider text = JsonUiTextProvider.CreateUi13(_languageCode);
        foreach (var (label, key) in _feedbackLabels) label.Text = text.Get(key);
        foreach (var (button, key) in _feedbackButtons) button.Text = text.Get(key);
        bool busy = _feedbackTask is not null;
        _feedbackExport.Disabled = busy;
        _feedbackIssue.Disabled = _feedbackCopyIssue.Disabled = busy;
        _feedbackFolder.Disabled = _feedbackCopy.Disabled = _feedbackResult is null;
        _feedbackArtifact.Visible = _feedbackPath.Visible = _feedbackResult is not null;
        _feedbackPath.Text = _feedbackResult?.Path ?? ""; _feedbackPath.TooltipText = _feedbackPath.Text;
        _feedbackFileName.Text = _feedbackResult is { } file ? Path.GetFileName(file.Path) : "";
        _feedbackFileName.TooltipText = _feedbackPath.Text;
        _feedbackState.Text = text.Get(busy ? "feedback.state.busy" : _feedbackResult is not null ? "feedback.state.ready" : "feedback.state.idle");
        _feedbackState.AddThemeColorOverride("font_color", _palette.Color(_feedbackResult is not null && !busy ? _palette.Active : _palette.Secondary));
        _feedbackExport.Text = text.Get(_feedbackResult is not null ? "feedback.export.again" : "feedback.export");
        _feedbackStatus.Text = busy ? text.Get("feedback.working") : _feedbackResult is { } result
            ? text.Format("feedback.ready", result.Logs, (result.Bytes / 1048576d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
            : text.Get("feedback.idle");
        var warnings = new List<string>();
        if (!busy && _feedbackResult is { Warnings: > 0 } partial) warnings.Add(text.Format("feedback.partial", partial.Warnings));
        if (!busy && _feedbackResult is { Oversized: true }) warnings.Add(text.Get("feedback.oversized"));
        _feedbackWarnings.Text = string.Join("\n", warnings); _feedbackWarnings.Visible = warnings.Count > 0;
        _feedbackNotice.Visible = _feedbackMessage.Length > 0;
        _feedbackReceipt.Text = _feedbackMessage.Length > 0 ? text.Get(_feedbackMessage) : "";
        if (_feedbackError.Length > 0) _feedbackReceipt.Text += " (" + _feedbackError + ")";
        _feedbackReceipt.AddThemeColorOverride("font_color", _feedbackMessage.Contains("failed", StringComparison.Ordinal) ? _palette.Warning : _palette.Color(_palette.Text));
        _feedbackDetailsToggle.Text = (_feedbackDetails.Visible ? "⌄  " : "›  ") + text.Get("feedback.details");
    }
}
