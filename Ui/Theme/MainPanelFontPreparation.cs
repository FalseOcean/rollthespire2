using System.Diagnostics;
using System.Text;
using Godot;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.Theme;

/// <summary>
/// Prepares fixed-text MSDF glyphs between main-thread frames, using the exact
/// current native variations. The owning page advances or cancels this work.
/// This does not prepare shaped ligatures, system fallback fonts, or UI layout.
/// </summary>
internal sealed class MainPanelFontPreparation : IDisposable
{
    private static readonly TimeSpan DefaultBudget = TimeSpan.FromMilliseconds(3);
    private readonly TextServer _server;
    // Keep resources, and therefore their native RIDs, alive until this page
    // finishes or cancels preparation. There is no worker or global font cache.
    private Font[] _fonts;
    private (Rid Rid, Vector2I Size, (int Codepoint, int Selector)[] Characters)[] _caches;
    private int _cacheIndex, _characterIndex;

    internal bool IsCompleted { get; private set; }
    internal bool IsCancelled { get; private set; }
    internal int RenderedGlyphCount { get; private set; }

    internal MainPanelFontPreparation(Font regular, Font medium, IEnumerable<string> fixedTexts)
        : this(WithFonts(regular, medium, fixedTexts)) { }

    // Authored controls need their own font, rather than every character in
    // every page font. RIDs shared by two roles still have one native cache.
    internal MainPanelFontPreparation(IEnumerable<(Font Font, string Text)> texts)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ArgumentNullException.ThrowIfNull(texts);
        _server = TextServerManager.GetPrimaryInterface();
        var runs = texts.ToArray();
        _fonts = runs.Select(run => run.Font).DistinctBy(font => font.GetInstanceId()).ToArray();
        _caches = runs.GroupBy(run => run.Font.GetInstanceId()).SelectMany(group =>
        {
            var characters = Characters(group.Select(run => run.Text)).Distinct().ToArray();
            return group.First().Font.GetRids().Select(rid => (Rid: rid, Characters: characters));
        }).GroupBy(run => run.Rid.Id).Select(group =>
        {
            Rid rid = group.First().Rid;
            // Do not guess another size/outline cache for a different font mode.
            // Both supported main-panel choices use native MSDF resources.
            if (!_server.FontIsMultichannelSignedDistanceField(rid))
                throw new NotSupportedException("MainPanelFontPreparationRequiresNativeMsdfFont");
            int size = checked((int)_server.FontGetMsdfSize(rid));
            if (size <= 0) throw new InvalidOperationException("MainPanelFontPreparationInvalidMsdfSize");
            // TextServer normalizes all MSDF display sizes/outlines to this
            // source-size cache. GetRids already encodes transform/baseline and
            // variation coordinates; never look up a FontFile cache by ordinal.
            return (rid, new Vector2I(size, 0), group.SelectMany(run => run.Characters).Distinct().ToArray());
        }).Where(cache => cache.Item3.Length > 0).ToArray();
        IsCompleted = _caches.Length == 0;
        if (IsCompleted) ReleaseInputs();
    }

    private static IEnumerable<(Font Font, string Text)> WithFonts(Font regular, Font medium, IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(regular);
        ArgumentNullException.ThrowIfNull(medium);
        ArgumentNullException.ThrowIfNull(texts);
        foreach (string text in texts)
        {
            yield return (regular, text);
            if (regular.GetInstanceId() != medium.GetInstanceId()) yield return (medium, text);
        }
    }

    /// <summary>
    /// Returns true when no work remains, including cancellation. The budget is
    /// checked between native calls: a single cold glyph cannot be interrupted
    /// and can exceed it. No string is shaped or rendered as one unbounded job.
    /// </summary>
    internal bool Advance(TimeSpan? budget = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (IsCompleted || IsCancelled) return true;
        TimeSpan allowance = budget ?? DefaultBudget;
        if (allowance < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));
        if (allowance == TimeSpan.Zero) return false;

        long started = Stopwatch.GetTimestamp();
        int examined = 0;
        var cacheSnapshots = new Dictionary<ulong, HashSet<long>>();
        // Both discovery and actual cache misses use the same deadline. Warm
        // lookups do not manufacture a frame boundary just because a fixed
        // number of characters was examined. Retain the exact cursor on yield.
        while (_cacheIndex < _caches.Length &&
            (examined == 0 || Stopwatch.GetElapsedTime(started) < allowance))
        {
            var cache = _caches[_cacheIndex];
            if (!cacheSnapshots.TryGetValue(cache.Rid.Id, out var glyphs))
            {
                // Refresh on every Advance: another main-thread page may have
                // filled this shared native cache since our preceding frame or
                // construction. Do not retain a speculative global warm set.
                glyphs = _server.FontGetGlyphList(cache.Rid, cache.Size).Select(glyph => (long)glyph).ToHashSet();
                cacheSnapshots.Add(cache.Rid.Id, glyphs);
                if (examined > 0 && Stopwatch.GetElapsedTime(started) >= allowance) break;
            }
            var character = cache.Characters[_characterIndex];
            long glyph = _server.FontGetGlyphIndex(cache.Rid, cache.Size.X, character.Codepoint, character.Selector);
            examined++;
            if (glyph != 0 && !glyphs.Contains(glyph))
            {
                // Always make at least one unit of progress, even if reading a
                // large native cache alone consumed this frame's soft budget.
                if (examined > 1 && Stopwatch.GetElapsedTime(started) >= allowance) break;
                // All consumers and this preparer run on main. Rendering an
                // already live native cache here therefore has no worker race.
                _server.FontRenderGlyph(cache.Rid, cache.Size, glyph);
                glyphs.Add(glyph);
                RenderedGlyphCount++;
            }
            if (++_characterIndex == cache.Characters.Length)
            {
                _characterIndex = 0;
                _cacheIndex++;
            }
        }
        IsCompleted = _cacheIndex == _caches.Length;
        if (IsCompleted) ReleaseInputs();
        return IsCompleted;
    }

    internal void Cancel()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (IsCompleted || IsCancelled) return;
        IsCancelled = true;
        ReleaseInputs();
    }

    public void Dispose() => Cancel();

    private void ReleaseInputs()
    {
        _fonts = [];
        _caches = [];
    }

    private static IEnumerable<(int Codepoint, int Selector)> Characters(IEnumerable<string> texts)
    {
        foreach (string text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            Rune? previous = null;
            foreach (Rune rune in text.EnumerateRunes())
            {
                bool selector = rune.Value is >= 0xfe00 and <= 0xfe0f or >= 0xe0100 and <= 0xe01ef;
                if (selector)
                {
                    if (previous is { } selected) yield return (selected.Value, rune.Value);
                    previous = null;
                    continue;
                }
                if (previous is { } ordinary) yield return (ordinary.Value, 0);
                previous = rune;
            }
            if (previous is { } last) yield return (last.Value, 0);
        }
    }
}
