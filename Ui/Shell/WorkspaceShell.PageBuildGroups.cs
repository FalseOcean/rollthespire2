using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    // A view owns complete authored groups, including ones not mounted yet.
    // Navigation can borrow this work without losing it on a quick switch.
    private sealed class PageBuildGroup(Control parent, Control view, string authoredContext)
    {
        internal readonly Control Parent = parent, View = view;
        internal MainPanelFontPreparation? Text;
        internal string Context = "";
        internal string AuthoredContext = authoredContext;
    }
    private readonly Dictionary<int, Queue<PageBuildGroup>> _pendingPageGroups = [];
    private readonly HashSet<int> _finishedPageBuilds = [];

    private Font PageControlFont(Control control)
    {
        if (control.HasThemeFontOverride("font")) return control.GetThemeFont("font");
        // Detached descendants do not yet have Godot's inherited theme owner.
        // Ask the mounted owner using the control's real theme type, rather
        // than warming the engine default font returned by a detached child.
        string type = control.ThemeTypeVariation.ToString();
        return GetThemeFont("font", type.Length > 0 ? type : control.GetClass());
    }

    private IEnumerable<(Font Font, string Text)> PageGroupText(Control group, Node? exclude = null)
    {
        if (group == exclude) yield break;
        switch (group)
        {
            case Label label:
                yield return (PageControlFont(label), label.Text);
                break;
            case Button button:
                yield return (PageControlFont(button), button.Text);
                break;
            case LineEdit input:
                yield return (PageControlFont(input), input.Text.Length > 0 ? input.Text : input.PlaceholderText);
                break;
        }
        foreach (Node child in group.GetChildren())
            if (child is Control { Visible: true } control)
                foreach (var text in PageGroupText(control, exclude)) yield return text;
    }

    private IEnumerable<(Font Font, string Text)> PageViewText(int page)
    {
        if (page == 4)
        {
            foreach (var run in PageGroupText(_feedbackPage)) yield return run;
            yield break;
        }
        foreach (var run in PageGroupText(_settingsPage, _settingsPages)) yield return run;
        if (_settingsCategories[page].Page is { } category)
            foreach (var run in PageGroupText(category)) yield return run;
    }

    private void BindPageBuildText(int page)
    {
        if (page == 4) RefreshFeedback();
        else LocalizeSettings(RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(_languageCode));
    }

    private bool AdvancePageBuildGroups(int page)
    {
        if (!_pendingPageGroups.TryGetValue(page, out var groups) || groups.Count == 0) return true;
        PageBuildGroup group = groups.Peek();
        if (group.Context != _pageTextContext)
        {
            group.Text?.Dispose();
            group.Text = null;
            group.Context = _pageTextContext;
            // Fresh controls already use the selected fonts. Only a retained
            // group authored under an earlier context needs a font refresh.
            if (group.AuthoredContext != _pageTextContext)
            {
                MainPanelTheme.RefreshFonts(group.View);
                group.AuthoredContext = _pageTextContext;
            }
            try { group.Text = new(group.View.Visible || group.View == _settingsPage ? PageGroupText(group.View) : []); }
            catch (Exception ex) { RuntimeLog.Warn("pageGroupFontPreparationUnavailable=true;page=" + page + ";detail=" + ex); }
        }
        try
        {
            if (group.Text is not null && !group.Text.Advance()) return false;
        }
        catch (Exception ex) { RuntimeLog.Warn("pageGroupFontPreparationFailed=true;page=" + page + ";detail=" + ex); }
        group.Text?.Dispose();
        group.Text = null;
        group.Parent.AddChild(group.View);
        if (group.View.Visible) _ = group.View.GetCombinedMinimumSize();
        groups.Dequeue();
        if (groups.Count == 0) _pendingPageGroups.Remove(page);
        return groups.Count == 0;
    }

    private void RetirePageBuildGroups()
    {
        foreach (var groups in _pendingPageGroups.Values)
            foreach (var group in groups)
            {
                group.Text?.Dispose();
                if (GodotObject.IsInstanceValid(group.View) && group.View.GetParent() is null) group.View.Free();
            }
        _pendingPageGroups.Clear();
        _finishedPageBuilds.Clear();
    }

    private IEnumerable<Control> PendingPageBuildViews() => _pendingPageGroups.Values.SelectMany(groups => groups)
        .Select(group => group.View).Where(view => GodotObject.IsInstanceValid(view) && view.GetParent() is null);
}
