using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// Only already-generated board facts are imported. Item kinds describe board
// objects; card/relic/potion identities do not exist until reward generation.
internal sealed record PredictorOperationCrystalScene(PredictorStreamState EventRng, int Remaining,
    PredictorCrystalTool Tool, bool PlacedAllItems, ImmutableArray<bool> Hidden,
    ImmutableArray<int> Occupancy, ImmutableArray<PredictorCrystalItem> Items,
    ImmutableArray<int> Revealed) : PredictorOperationScene
{
    internal static PredictorOperationCrystalScene FromSnapshot(PredictorCrystalSnapshot snapshot) =>
        new(snapshot.EventRng, snapshot.Remaining, snapshot.Tool, snapshot.PlacedAllItems,
            snapshot.Hidden, snapshot.Occupancy, snapshot.Items, snapshot.Revealed);

    internal override void Restore(PredictorRun run) => run.RestoreOperationCrystal(this);
}

internal sealed partial class PredictorRun
{
    internal void RestoreOperationCrystal(PredictorOperationCrystalScene scene)
    {
        RequireEmptyOperationScene();
        if (Context.Operation == null || scene.EventRng == null || scene.Remaining < 1 ||
            !Enum.IsDefined(scene.Tool) || scene.Hidden.IsDefault || scene.Hidden.Length != 121 ||
            scene.Occupancy.IsDefault || scene.Occupancy.Length != 121 || scene.Items.IsDefault || scene.Revealed.IsDefault ||
            scene.EventRng.Calls < 0 || (scene.EventRng.S0 | scene.EventRng.S1 | scene.EventRng.S2 | scene.EventRng.S3) == 0 ||
            scene.Items.Any(i => i == null || i.Width < 1 || i.Height < 1 || i.Subscriptions < 0 ||
                i.Kind is not ("RELIC" or "POTION_COMMON" or "POTION_RARE" or "CARD_COMMON" or "CARD_UNCOMMON" or "CARD_RARE" or "CURSE" or "GOLD_SMALL" or "GOLD_BIG") ||
                (i.X < 0 || i.Y < 0 ? i.X != -1 || i.Y != -1 : i.X + i.Width > 11 || i.Y + i.Height > 11)) ||
            scene.Occupancy.Any(i => i < -1 || i >= scene.Items.Length) ||
            scene.Revealed.Any(i => i < 0 || i >= scene.Items.Length || !scene.Items[i].Revealed))
            throw new InvalidDataException("PredictorOperationCrystalSceneInvalid");
        for (int cell = 0; cell < 121; cell++)
        {
            int itemIndex = scene.Occupancy[cell];
            if (itemIndex < 0) continue;
            var item = scene.Items[itemIndex];
            int x = cell / 11, y = cell % 11;
            if (item.X < 0 || x < item.X || x >= item.X + item.Width || y < item.Y || y >= item.Y + item.Height ||
                item.Revealed && scene.Hidden[cell])
                throw new InvalidDataException("PredictorOperationCrystalOccupancyInvalid");
        }
        foreach (var item in scene.Items.Select((value, index) => (value, index)).Where(i => i.value.X >= 0))
            for (int x = item.value.X; x < item.value.X + item.value.Width; x++)
                for (int y = item.value.Y; y < item.value.Y + item.value.Height; y++)
                    if (scene.Occupancy[x * 11 + y] != item.index)
                        throw new InvalidDataException("PredictorOperationCrystalItemFootprintInvalid");

        _eventKey = new("EVENT", "CRYSTAL_SPHERE");
        _eventRng = scene.EventRng.Restore();
        _crystalActive = true;
        _crystalRemaining = scene.Remaining;
        _crystalTool = scene.Tool;
        _crystalPlacedAllItems = scene.PlacedAllItems;
        scene.Hidden.CopyTo(_crystalHidden);
        scene.Occupancy.CopyTo(_crystalOccupancy);
        _crystalItems.Clear();
        _crystalItems.AddRange(scene.Items.Select(i => new CrystalItem(i.Kind, i.Width, i.Height)
            { X = i.X, Y = i.Y, Subscriptions = i.Subscriptions, Revealed = i.Revealed }));
        _crystalRevealed.Clear();
        _crystalRevealed.AddRange(scene.Revealed);
        EventOptions = [];
        Phase = PredictorPhase.EventChoice;
        _requestId++;
    }

    internal PredictorOperationCrystalScene? ExportOperationCrystalBoard() =>
        Context.Operation == null || !_crystalActive || _eventRng == null ? null :
        new(PredictorStreamState.Capture(PredictorStream.Rewards, _eventRng), _crystalRemaining,
            _crystalTool, _crystalPlacedAllItems, _crystalHidden.ToImmutableArray(), _crystalOccupancy.ToImmutableArray(),
            _crystalItems.Select(i => new PredictorCrystalItem(i.Kind, i.Width, i.Height, i.X, i.Y, i.Subscriptions, i.Revealed)).ToImmutableArray(),
            _crystalRevealed.ToImmutableArray());
}
