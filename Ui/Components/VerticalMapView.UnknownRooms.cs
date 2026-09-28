using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class VerticalMapView
{
    private readonly Dictionary<MapPosition, UnknownRoomCategory> _resolvedRooms = new();
    internal int ResolvedUnknownCount => _resolvedRooms.Count;
    public void SetUnknownRooms(IReadOnlyList<UnknownRoomObservation>? observations)
    {
        _resolvedRooms.Clear();
        if (_map is not null && observations is not null)
        {
            var unknowns = _map.Points.Where(p => p.Type == MapPointType.Unknown).Select(p => p.Position).ToHashSet();
            foreach (var step in observations)
                if (step.Position is { } position && unknowns.Contains(position)) _resolvedRooms.Add(position, step.Room);
        }
        DrawMap();
    }
}
