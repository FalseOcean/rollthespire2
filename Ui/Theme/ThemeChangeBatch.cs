using Godot;

namespace RolltheSpire2.Ui.Theme;

/// <summary>Coalesce RT2 theme writes on the UI thread, including exception unwinding.</summary>
internal sealed class ThemeChangeBatch : IDisposable
{
    private readonly List<(Godot.Theme Theme, bool Blocked)> _themes = [];
    private bool _changed, _disposed;

    internal ThemeChangeBatch(IEnumerable<Godot.Theme> themes)
    {
        try
        {
            foreach (var theme in themes.DistinctBy(theme => theme.GetInstanceId()))
            {
                _themes.Add((theme, theme.IsBlockingSignals()));
                theme.SetBlockSignals(true);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void MarkChanged() => _changed = true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Restore every resource before notifying consumers. A caller that
        // already blocked signals retains ownership of the eventual notice.
        foreach (var (theme, blocked) in _themes)
            theme.SetBlockSignals(blocked);
        if (_changed)
            foreach (var (theme, blocked) in _themes)
                if (!blocked) theme.EmitChanged();
    }
}
