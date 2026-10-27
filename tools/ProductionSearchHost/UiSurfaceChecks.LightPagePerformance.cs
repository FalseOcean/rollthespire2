using System.Diagnostics;
using System.Reflection;
using Godot;
using HarmonyLib;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static string _lightPagePhase = "ordinary-navigation";
    private sealed record LightPageVisit(Control Target, object Observation, double UsableMs, double EntryToReadyMs);

    private static object? LightPageField(object owner, string name)
    {
        for (Type? type = owner.GetType(); type is not null; type = type.BaseType)
            if (type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } field)
                return field.GetValue(owner);
        return null;
    }

    private static IEnumerable<Node> LightPageNodes(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in LightPageNodes(child)) yield return descendant;
        }
    }

    private static bool LightPageUseful(WorkspaceShell shell, string name, Control? target)
    {
        if (target?.IsVisibleInTree() != true || !target.IsNodeReady()) return false;
        if (LightPageField(shell, "_pagePreparationNotice") is Control notice && notice.IsVisibleInTree()) return false;
        if (name == "About")
            return LightPageField(shell, "_notesPage") is { } notes &&
                (notes.GetType().GetProperty("IsUsefulContentReady", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(notes) is not bool useful || useful) &&
                LightPageField(notes, "_renderer") is Control renderer && renderer.IsVisibleInTree() && renderer.GetChildCount() > 0;
        if (name == "Settings")
            return LightPageField(shell, "_settingsPages") is Control pages &&
                LightPageNodes(pages).OfType<Control>().Any(control => control.IsVisibleInTree() &&
                    control is BaseButton or LineEdit or SpinBox);
        if (name == "Feedback")
            return LightPageField(shell, "_feedbackExport") is Control export && export.IsVisibleInTree();
        return true; // Existing Library navigation keeps its existing page-specific ready contract.
    }

    private static bool LightPageComplete(WorkspaceShell shell, string name)
    {
        if (!PanelOpenActivePageReady(shell)) return false;
        if (name != "About") return true;
        object notes = LightPageField(shell, "_notesPage") ?? throw new InvalidOperationException("About notes page missing");
        object restoring = LightPageField(notes, "_restoringPosition") ??
            throw new MissingFieldException(notes.GetType().FullName, "_restoringPosition");
        return restoring is false;
    }

    private static async Task<LightPageVisit> MeasureLightPageVisitAsync(Node root, SubViewport viewport,
        WorkspaceShell shell, string name, string buttonField, string pageField, bool settle, long entryClicked,
        Dictionary<string, ulong> pageInstances)
    {
        var button = (Button)PanelOpenField(shell, buttonField)!;
        if (!button.IsVisibleInTree() || button.Disabled) throw new InvalidOperationException(name + " navigation unavailable");
        var intervals = new List<double>();
        var beforeReady = new List<double>();
        var afterReady = new List<double>();
        using var process = Process.GetCurrentProcess();
        TimeSpan cpuAtClick = process.TotalProcessorTime;
        var pendingDrawIntervals = new List<double>();
        var readyDrawIntervals = new List<double>();
        long clicked = Stopwatch.GetTimestamp(), lastFrame = clicked, usefulAt = 0, readyAt = 0;
        long lastDraw = clicked;
        void OnFrame()
        {
            long now = Stopwatch.GetTimestamp();
            double ms = Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds;
            intervals.Add(ms);
            if (readyAt == 0 || lastFrame < readyAt) beforeReady.Add(ms);
            else afterReady.Add(ms);
            lastFrame = now;
        }
        root.GetTree().ProcessFrame += OnFrame;
        try
        {
            PanelOpenClick(viewport, button);
            double dispatchMs = Stopwatch.GetElapsedTime(clicked).TotalMilliseconds;
            Control? target;
            int pendingRenderedFrames = 0;
            do
            {
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                long drawn = Stopwatch.GetTimestamp();
                pendingDrawIntervals.Add(Stopwatch.GetElapsedTime(lastDraw, drawn).TotalMilliseconds);
                lastDraw = drawn;
                pendingRenderedFrames++;
                target = LightPageField(shell, pageField) as Control;
                if (name == "About") target ??= LightPageField(shell, "_notesPage") as Control;
                // Query failure even while the covering notice is visible.
                bool complete = PanelOpenActivePageReady(shell);
                bool useful = LightPageUseful(shell, name, target);
                if (useful && usefulAt == 0) usefulAt = Stopwatch.GetTimestamp();
                if (useful && complete && LightPageComplete(shell, name)) readyAt = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(clicked).TotalSeconds > 30) throw new TimeoutException(name + " page not usable");
            } while (readyAt == 0);
            double usableMs = Stopwatch.GetElapsedTime(clicked, readyAt).TotalMilliseconds;
            double pageReadyProcessCpuMilliseconds = (process.TotalProcessorTime - cpuAtClick).TotalMilliseconds;
            ulong id = target!.GetInstanceId();
            bool firstVisit = !pageInstances.TryGetValue(name, out ulong previousId);
            if (!firstVisit && previousId != id) throw new InvalidOperationException(name + " page instance was not retained");
            pageInstances[name] = id;
            int settledDraws = 0;
            if (settle)
                for (; settledDraws < 12; settledDraws++)
                {
                    await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    long drawn = Stopwatch.GetTimestamp();
                    readyDrawIntervals.Add(Stopwatch.GetElapsedTime(lastDraw, drawn).TotalMilliseconds);
                    lastDraw = drawn;
                }
            return new(target, new {
                name, phase = _lightPagePhase, firstVisit, instanceId = id, sameInstanceOnRevisit = !firstVisit,
                afterEntryClickMs = Stopwatch.GetElapsedTime(entryClicked, clicked).TotalMilliseconds,
                dispatchMs, firstUsefulContentDrawMs = Stopwatch.GetElapsedTime(clicked, usefulAt).TotalMilliseconds,
                pageReadyProcessCpuMilliseconds,
                cpuDefinition = "Total isolated-host process CPU from before pointer dispatch through ready, all threads including render/runtime; not glyph-exclusive CPU or GPU time",
                usableMs, completeReadyDrawMs = usableMs, pendingRenderedFrames, readyPostDraws = settledDraws,
                longestProcessFrameIntervalMs = intervals.DefaultIfEmpty().Max(), frameIntervalsMs = intervals,
                beforeReadyFrameIntervalsMs = beforeReady, readyAfter12DrawFrameIntervalsMs = afterReady,
                pendingDrawIntervalsMs = pendingDrawIntervals, readyAfter12DrawIntervalsMs = readyDrawIntervals,
                pendingLongestDrawIntervalMs = pendingDrawIntervals.DefaultIfEmpty().Max(),
                readyAfter12DrawLongestIntervalMs = readyDrawIntervals.DefaultIfEmpty().Max(),
                beforeReadyLongestFrameIntervalMs = beforeReady.DefaultIfEmpty().Max(),
                readyAfter12DrawLongestFrameIntervalMs = afterReady.DefaultIfEmpty().Max(),
                usefulContentDefinition = name == "About" ? "visible article blocks after FramePostDraw; when available, page IsUsefulContentReady also confirms its actual saved reading target" :
                    "visible target with substantive content/controls after FramePostDraw and no covering page-preparation notice",
                readinessDefinition = name == "About" ? "useful article drawn; shell ready; _restoringPosition=false" : "useful content drawn; actual shell page readiness",
                intervalDefinition = "ProcessFrame wall intervals start before pointer dispatch; boundary-crossing interval stays before-ready. Settled visits observe 12 real PostDraw boundaries."
            }, usableMs, Stopwatch.GetElapsedTime(entryClicked, readyAt).TotalMilliseconds);
        }
        finally { root.GetTree().ProcessFrame -= OnFrame; }
    }

    private static async Task MeasureLightPageInvalidationAsync(WorkspaceShell shell, SearchWorkspace workspace,
        Func<string, string, string, bool, Task<Control>> visit, List<object> observations)
    {
        string language = workspace.Preferences.LanguageOverride, font = workspace.Preferences.FontChoice;
        void Refresh(string method) => (AccessTools.Method(shell.GetType(), method) ??
            throw new MissingMethodException(shell.GetType().FullName, method)).Invoke(shell, null);
        try
        {
            _lightPagePhase = "language-invalidated";
            long languageChangedAt = Stopwatch.GetTimestamp();
            workspace.SetLanguageOverride((string?)LightPageField(shell, "_languageCode") == "zh" ? "en" : "zh");
            Refresh("RefreshLanguage");
            observations.Add(new { invalidation = "language", synchronousChangeMs = Stopwatch.GetElapsedTime(languageChangedAt).TotalMilliseconds, method = "isolated workspace.SetLanguageOverride then actual shell.RefreshLanguage chain; subsequent visits use real pointer gestures" });
            await visit("Settings", "_settings", "_settingsPage", true);
            await visit("Feedback", "_feedback", "_feedbackPage", true);
            await visit("About", "_notes", "_notesHost", true);
            _lightPagePhase = "font-invalidated";
            long fontChangedAt = Stopwatch.GetTimestamp();
            string changedFont = font == "game-sans" ? "game-serif" : "game-sans";
            RolltheSpire2.Ui.Theme.MainPanelTheme.SelectFont(changedFont);
            workspace.SetFontChoice(changedFont);
            Refresh("RefreshWorkspaceFont");
            observations.Add(new { invalidation = "font", synchronousChangeMs = Stopwatch.GetElapsedTime(fontChangedAt).TotalMilliseconds, method = "same SelectFont/SetFontChoice/RefreshWorkspaceFont chain used by actual settings font handler; subsequent visits use real pointer gestures" });
            await visit("Settings", "_settings", "_settingsPage", true);
            await visit("Feedback", "_feedback", "_feedbackPage", true);
            await visit("About", "_notes", "_notesHost", true);
        }
        finally
        {
            _lightPagePhase = "invalidation-restore";
            workspace.SetLanguageOverride(language);
            RolltheSpire2.Ui.Theme.MainPanelTheme.SelectFont(font);
            workspace.SetFontChoice(font);
            Refresh("RefreshWorkspaceFont"); Refresh("RefreshLanguage");
            _lightPagePhase = "ordinary-navigation";
        }
    }

    // Host-only probes: main-thread nested spans in memory, serialized once at
    // the end. No glyph/per-node hook, cache census, or path-time file I/O.
    private sealed class LightPageProbe : IDisposable
    {
        private static LightPageProbe? _active;
        private readonly Harmony _patch = new("rt2.h09.light-page.host-probe");
        private readonly int _thread = System.Environment.CurrentManagedThreadId;
        private readonly List<Span> _spans = [];
        private readonly Stack<Span> _stack = [];
        private readonly List<string> _patched = [], _skipped = [];
        private PropertyInfo? _renderedGlyphCount;
        private int _sample = -1;
        private string _page = "Setup";
        private sealed class Span
        {
            public required string Method, Page, Phase;
            public int Sample, Depth;
            public long Start, End, Children;
            public string? Exception;
            public int? WorkPage, GlyphCountBefore, RenderedGlyphDelta;
            public string? ViewGroup;
        }
        internal static LightPageProbe? Start()
        {
            if (System.Environment.GetEnvironmentVariable("H09_PROBE") != "1") return null;
            var probe = new LightPageProbe();
            _active = probe;
            try { probe.Install(); return probe; }
            catch { probe.Dispose(); throw; }
        }
        internal void SetContext(int sample, string page) { _sample = sample; _page = page; }
        private void Install()
        {
            var assembly = typeof(WorkspaceShell).Assembly;
            _renderedGlyphCount = assembly.GetType("RolltheSpire2.Ui.Theme.MainPanelFontPreparation")?.GetProperty("RenderedGlyphCount",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            void Patch(string typeName, params string[] methods)
            {
                Type? type = assembly.GetType(typeName);
                foreach (string method in methods)
                {
                    MethodBase[] found = type is null ? [] : method == ".ctor" ? type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) :
                        type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                            .Where(candidate => candidate.Name == method).Cast<MethodBase>().ToArray();
                    if (found.Length == 0) { _skipped.Add(typeName + "." + method); continue; }
                    foreach (var target in found)
                    {
                        _patch.Patch(target, prefix: new HarmonyMethod(typeof(LightPageProbe), nameof(Begin)),
                            finalizer: new HarmonyMethod(typeof(LightPageProbe), nameof(End)));
                        _patched.Add(typeName + "." + target.Name);
                    }
                }
            }
            Patch("RolltheSpire2.Ui.Shell.WorkspaceShell", "DeferPage", "DeferWorkspacePage", "DeferSettingsCategory", "AdvancePageText", "AdvancePageView", "AdvancePageBuildGroups", "TickPagePreparation", "OpenNotes", "RefreshNotes", "RefreshLanguage", "LocalizeSettings", "RefreshSettings", "RefreshFeedback");
            Patch("RolltheSpire2.Ui.Theme.MainPanelFontPreparation", ".ctor", "Advance");
            Patch("RolltheSpire2.Infrastructure.DeveloperNotes.DeveloperNotesDocumentProvider", "Resolve");
            Patch("RolltheSpire2.Ui.Pages.DeveloperNotes.DeveloperNotesPage", "ApplyLocalization", "RenderSelectedChapter", "RestoreReadingPosition", "AdvanceArticleBuild");
            Patch("RolltheSpire2.Ui.Pages.DeveloperNotes.DeveloperNotesArticleRenderer", "Render", "AppendBlock");
        }
        private static void Begin(MethodBase __originalMethod, object? __instance, object[] __args, out Span? __state)
        {
            __state = null;
            var probe = _active;
            if (probe is null || System.Environment.CurrentManagedThreadId != probe._thread) return;
            __state = new Span { Method = __originalMethod.DeclaringType!.Name + "." + __originalMethod.Name,
                Page = probe._page, Phase = _lightPagePhase, Sample = probe._sample,
                Depth = probe._stack.Count, Start = Stopwatch.GetTimestamp() };
            if (__originalMethod.Name is "AdvancePageText" or "AdvancePageView" or "AdvancePageBuildGroups" or "DeferPage" && __args.FirstOrDefault() is int page)
                __state.WorkPage = page;
            else if (probe._stack.TryPeek(out var parent)) __state.WorkPage = parent.WorkPage;
            if (__originalMethod.DeclaringType?.Name == "MainPanelFontPreparation" && __originalMethod.Name == "Advance")
                __state.GlyphCountBefore = probe._renderedGlyphCount?.GetValue(__instance) as int?;
            probe._stack.Push(__state);
            probe._spans.Add(__state);
        }
        private static Exception? End(Exception? __exception, object? __instance, Span? __state)
        {
            if (__state is null) return __exception;
            var probe = _active!;
            __state.End = Stopwatch.GetTimestamp();
            __state.Exception = __exception?.GetType().FullName;
            if (__state.GlyphCountBefore is int before)
                __state.RenderedGlyphDelta = (probe._renderedGlyphCount?.GetValue(__instance) as int?) - before;
            if (__state.Method == "WorkspaceShell.AdvancePageView" && __state.WorkPage is int page && __instance is not null)
                __state.ViewGroup = LightPageField(__instance, "_pageViewBuilds") is System.Collections.IDictionary builds &&
                    builds[page] is System.Collections.IEnumerator build ? build.Current?.ToString() ?? "unnamed" : "complete";
            if (!ReferenceEquals(probe._stack.Pop(), __state)) throw new InvalidOperationException("H09 probe span nesting corrupted");
            if (probe._stack.TryPeek(out var parent)) parent.Children += __state.End - __state.Start;
            return __exception;
        }
        internal object Snapshot() => new {
            enabled = true, patched = _patched, skippedMissingMethods = _skipped,
            note = "Diagnostic run adds Harmony/span overhead. Inclusive nested spans overlap; selfMs excludes instrumented child calls. Async RestoreReadingPosition span covers synchronous dispatch only; visit completion separately waits for _restoringPosition=false and PostDraw. No GPU-exclusive timing.",
            spans = _spans.Select(span => new { span.Sample, span.Page, span.Phase, span.Method, span.Depth,
                span.WorkPage, span.RenderedGlyphDelta, span.ViewGroup,
                elapsedMs = Stopwatch.GetElapsedTime(span.Start, span.End).TotalMilliseconds,
                selfMs = (span.End - span.Start - span.Children) * 1000d / Stopwatch.Frequency, span.Exception }).ToArray()
        };
        public void Dispose()
        {
            _patch.UnpatchAll(_patch.Id);
            if (ReferenceEquals(_active, this)) _active = null;
        }
    }
}
