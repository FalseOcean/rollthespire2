using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
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
    private readonly Queue<(PredictorCard Snapshot, Control Slot)> _pendingFaces = [];
    private NCard? _preparedFace;
    private bool _faceSetupReady;
    private static bool _nativeFaceInstantiated;
    internal bool StageCardFaces { get; set; }
    internal bool HasPendingCardFaces => _pendingFaces.Count > 0;

    internal void BuildCardFaceBatch()
    {
        // The first packed-scene instantiation and the first native Ready each
        // have cold costs. Give them separate frames without exposing a
        // half-populated result or preparing cards elsewhere in the game.
        if (!_faceSetupReady)
        {
            try
            {
                if (!OperationNativeCardDisplay.HasCachedFaceScene)
                {
                    OperationNativeCardDisplay.LoadFaceScene();
                    return;
                }
                _faceSetupReady = true;
                if (!_nativeFaceInstantiated)
                {
                    _preparedFace = OperationNativeCardDisplay.InstantiateFace();
                    _nativeFaceInstantiated = true;
                    return;
                }
            }
            catch (Exception error)
            {
                _faceSetupReady = true;
                RolltheSpire2.Bootstrap.RuntimeLog.WarnException("operationCardScenePreparationFailed=true", error);
            }
        }
        long started = Stopwatch.GetTimestamp();
        int added = 0;
        while (_pendingFaces.TryDequeue(out var pending))
        {
            PopulateNativeCard(pending.Snapshot, pending.Slot, 1);
            if (++added >= 3 || Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 3) break;
        }
    }

    internal void UseCardDisplays(OperationCardDisplayReuse reuse) => _cardReuse = reuse;

    internal void ReleaseCardDisplays()
    {
        _preparedFace?.QueueFree();
        _preparedFace = null;
        if (_cardReuse == null || _nativeTiles.Count == 0) return;
        _cardReuse.Clear();
        foreach (var (key, tile) in _nativeTiles)
            if (GodotObject.IsInstanceValid(tile) && !tile.IsQueuedForDeletion()) _cardReuse.Keep(key, tile);
        _nativeTiles.Clear();
    }
    // Native frame is centred at the origin; reserve room for its protruding cost gem.
    private Control CardSlot(PredictorCard snapshot, bool stage)
    {
        if (TakeRetainedCard(snapshot) is { } retained) return retained;
        var slot = OperationNativeCardDisplay.NewTile(1);
        // Slot geometry is known before creating any native face. It is never
        // revealed until every group has its original cards and order.
        if (stage) _pendingFaces.Enqueue((snapshot, slot));
        else PopulateNativeCard(snapshot, slot, 1);
        return slot;
    }

    private Control? TakeRetainedCard(PredictorCard snapshot)
    {
        if (_cardReuse == null) return null;
        long started = Stopwatch.GetTimestamp();
        var key = OperationCardDisplayReuse.Key.For(snapshot);
        if (_cardReuse.Take(key) is not { } retained) return null;
        _nativeTiles.Add((key, retained));
        NativeCardCount++; NativeCardReuseCount++;
        NativeCardCreateMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return retained;
    }

    private void PopulateNativeCard(PredictorCard snapshot, Control tile, float scale)
    {
        long started = Stopwatch.GetTimestamp();
        var key = OperationCardDisplayReuse.Key.For(snapshot);
        var prepared = _preparedFace;
        _preparedFace = null;
        OperationNativeCardDisplay.Populate(tile, snapshot, scale,
            readyTile =>
            {
                _nativeFaceInstantiated = true;
                _nativeTiles.Add((key, readyTile));
            },
            elapsed => NativeCardReadyMilliseconds += elapsed,
            "neowOperationNativeCard", prepared);
        NativeCardCreateMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        NativeCardCount++;
    }

    public override void _ExitTree()
    {
        _preparedFace?.QueueFree();
        _preparedFace = null;
    }
}
