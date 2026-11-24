using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Runs;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Bootstrap;
using RoomType = MegaCrit.Sts2.Core.Rooms.RoomType;

namespace RolltheSpire2.Ui.Persistence;

internal sealed record RouteBookKey(string Seed, int Ascension, int Players, string Room)
{
    private static readonly FieldInfo? StartTime = typeof(RunManager).GetField("_startTime", BindingFlags.Instance | BindingFlags.NonPublic);
    internal static RouteBookKey ForRun(IRunState run)
    {
        var started = StartTime?.GetValue(RunManager.Instance);
        var members = string.Join(",", run.Players.Select(p => $"{p.NetId}/{p.Character.Id.Entry}"));
        return new(run.Rng.StringSeed, run.AscensionLevel, run.Players.Count, $"{started}:{members}");
    }
}

internal sealed record SeenRoom(MapPosition Position, RoomType Type);
internal sealed record SeenAct(string Act, int Columns, IReadOnlyList<MapPredictionPoint> Points,
    MapPosition[] Walked, SeenRoom[] Rooms)
{
    internal MapPrediction Map(string seed, int act) => new(seed,
        ModelKey.TryParseExact(Act, out var key) ? key : default, act - 1, Columns, Points, 0);

    internal static SeenAct Capture(IRunState run)
    {
        var map = InRunMapSnapshot.Actual(run);
        var visited = run.VisitedMapCoords.Select(InRunMapSnapshot.Position).ToArray();
        var history = run.MapPointHistory.ElementAtOrDefault(run.CurrentActIndex);
        var rooms = new List<SeenRoom>();
        if (history is not null)
        {
            // Debug travel or a replaced map can truncate the coordinates independently of history.
            var pairs = history.Count == visited.Length ? history.Zip(visited) :
                history.TakeLast(1).Zip(visited.TakeLast(1));
            foreach (var (entry, coord) in pairs)
                if (entry.MapPointType == MegaCrit.Sts2.Core.Map.MapPointType.Unknown && entry.Rooms.FirstOrDefault() is { } room)
                    rooms.Add(new(coord, room.RoomType));
        }
        return new(map.Act.Serialized, map.Columns, map.Points, visited, rooms.ToArray());
    }
}

internal sealed record RouteBook(RouteBookKey Key)
{
    public bool? Spoils { get; set; }
    public Dictionary<int, SeenAct> Seen { get; init; } = [];
    public Dictionary<string, MapPosition[]> Drawn { get; init; } = [];
    public Dictionary<string, string> MapShapes { get; init; } = [];
    public Dictionary<int, MapPosition[]> NativeDrawn { get; init; } = [];
}

internal sealed class MapRouteNotebook(string path)
{
    internal static MapRouteNotebook Local() => new(Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state", "map-routes.json"));
    private List<RouteBook> Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<List<RouteBook>>(File.ReadAllBytes(path)) ?? [] : [];

    private static RouteBook Find(List<RouteBook> books, RouteBookKey key)
    {
        if (books.FirstOrDefault(b => b.Key == key) is { } existing) return existing;
        if (key.Room.Length > 0 && books.FirstOrDefault(b => b.Key == (key with { Room = "" })) is { } preview)
            return new(key) { Spoils = preview.Spoils, Drawn = new(preview.Drawn), MapShapes = new(preview.MapShapes) };
        return new(key);
    }

    internal RouteBook Read(RouteBookKey key)
    {
        try { return Find(Load(), key); }
        catch (Exception ex) { RuntimeLog.WarnException("mapRoutesReadFailed", ex); return new(key); }
    }

    internal void Edit(RouteBookKey key, Action<RouteBook> change)
    {
        try
        {
            // Separate game processes may save different multiplayer rooms concurrently.
            using var mutex = new System.Threading.Mutex(false, "RolltheSpire2.MapRouteNotebook");
            try { if (!mutex.WaitOne(1000)) throw new IOException("Map route notebook is busy"); }
            catch (AbandonedMutexException) { }
            try
            {
                var books = Load();
                var book = Find(books, key);
                books.Remove(book);
                change(book);
                books.Insert(0, book);
                bool solo = key.Players == 1;
                foreach (var expired in books.Where(b => (b.Key.Players == 1) == solo).Skip(solo ? 1 : 3).ToArray())
                    books.Remove(expired);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(books);
                if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                    SearchPersistenceFile.WriteAtomic(path, bytes);
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception ex) { RuntimeLog.WarnException("mapRoutesSaveFailed", ex); }
    }
}
