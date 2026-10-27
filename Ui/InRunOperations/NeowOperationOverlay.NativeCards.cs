using Godot;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationOverlay
{
    internal double NativeCardCreateMilliseconds { get; private set; }
    internal double NativeCardReadyMilliseconds { get; private set; }
    internal int NativeCardCount { get; private set; }
    internal int NativeCardReuseCount { get; private set; }
    private OperationCardDisplayReuse? _cardReuse;
    private readonly List<(OperationCardDisplayReuse.Key Key, Control Tile)> _nativeTiles = [];

    internal void UseCardDisplays(OperationCardDisplayReuse reuse) => _cardReuse = reuse;

    internal void ReleaseCardDisplays()
    {
        if (_cardReuse == null || _nativeTiles.Count == 0) return;
        _cardReuse.Clear();
        foreach (var (key, tile) in _nativeTiles)
            if (GodotObject.IsInstanceValid(tile) && !tile.IsQueuedForDeletion()) _cardReuse.Keep(key, tile);
        _nativeTiles.Clear();
    }
    // Native frame is centred at the origin; reserve room for its protruding cost gem.
    private Control NativeCard(PredictorCard snapshot, float scale)
    {
        var createWatch = System.Diagnostics.Stopwatch.StartNew();
        var key = OperationCardDisplayReuse.Key.For(snapshot);
        if (_cardReuse?.Take(key) is { } retained)
        {
            _nativeTiles.Add((key, retained));
            NativeCardCount++; NativeCardReuseCount++;
            NativeCardCreateMilliseconds += createWatch.Elapsed.TotalMilliseconds;
            return retained;
        }
        var tile = OperationNativeCardDisplay.Create(snapshot, scale,
            readyTile => _nativeTiles.Add((key, readyTile)),
            elapsed => NativeCardReadyMilliseconds += elapsed,
            "neowOperationNativeCard");
        NativeCardCreateMilliseconds += createWatch.Elapsed.TotalMilliseconds;
        NativeCardCount++;
        return tile;
    }
}
