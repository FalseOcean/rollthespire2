using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class VerticalMapView
{
    private readonly Dictionary<MapPosition, MegaCrit.Sts2.Core.Rooms.RoomType> _resolvedRooms = new();
    private readonly HashSet<MapPosition> _previewRooms = new();
    internal int ResolvedUnknownCount => _resolvedRooms.Count;
    public void SetUnknownRooms(IReadOnlyList<UnknownRoomObservation>? observations)
    {
        _resolvedRooms.Clear();
        _previewRooms.Clear();
        if (_map is not null && observations is not null)
        {
            var unknowns = _map.Points.Where(p => p.Type == MapPointType.Unknown).Select(p => p.Position).ToHashSet();
            foreach (var step in observations)
                if (step.Position is { } position && unknowns.Contains(position))
                    _resolvedRooms.Add(position, Enum.Parse<MegaCrit.Sts2.Core.Rooms.RoomType>(step.Room.ToString()));
        }
        DrawMap();
    }
    internal void SetRouteRooms(IEnumerable<(MapPosition Position, MegaCrit.Sts2.Core.Rooms.RoomType Room, bool Preview)> rooms)
    {
        _resolvedRooms.Clear(); _previewRooms.Clear();
        foreach (var room in rooms)
        {
            _resolvedRooms[room.Position] = room.Room;
            if (room.Preview) _previewRooms.Add(room.Position);
        }
        DrawMap();
    }
}
