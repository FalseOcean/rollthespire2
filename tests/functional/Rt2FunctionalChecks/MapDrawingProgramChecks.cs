using RolltheSpire2.Core.Prediction.Maps;

/// <summary>Bounded deterministic CPU checks; no GPU work or real game state.</summary>
internal static class MapDrawingProgramChecks
{
    private static readonly MapPointType[] Rooms =
        [MapPointType.Monster, MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop, MapPointType.Unknown];

    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("MapDrawingProgram check failed: " + label);
        }
        void Throws<T>(Action action, string label) where T : Exception
        {
            checks++;
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("MapDrawingProgram expected " + typeof(T).Name + ": " + label);
        }
        bool OnPath(MapRouteSequenceQuery query, params MapPointType[] path)
        {
            var fixture = Fixture.Path(path);
            return fixture.Match(MapDrawingProgram.Compile(query, 12));
        }

        var grouped = Query([MapPointType.Elite, MapPointType.RestSite], [MapPointType.Shop, MapPointType.Unknown]);
        Check(OnPath(grouped, MapPointType.RestSite, MapPointType.Monster, MapPointType.Elite,
            MapPointType.Unknown, MapPointType.Shop), "unordered groups and gaps across five selectable types");
        Check(!OnPath(grouped, MapPointType.Shop, MapPointType.Elite, MapPointType.RestSite,
            MapPointType.Unknown), "ordered groups do not interleave");
        Check(OnPath(Query([MapPointType.Elite, MapPointType.Elite]),
            MapPointType.Elite, MapPointType.Monster, MapPointType.Elite), "repeated room counts");
        Check(!OnPath(Query([MapPointType.Elite], [MapPointType.Elite]), MapPointType.Elite),
            "a room cannot complete two groups");
        Check(OnPath(Query([], [MapPointType.Unknown], []), MapPointType.Unknown), "empty groups are skipped");

        var fixedFixture = Fixture.Path([MapPointType.Elite]);
        fixedFixture.Modifiable = 0;
        Check(!fixedFixture.Match(MapDrawingProgram.Compile(Query([MapPointType.Elite]), 12)), "fixed room excluded");
        fixedFixture.Types[3] = (byte)MapPointType.Elite;
        fixedFixture.Types[fixedFixture.Boss] = (byte)MapPointType.Elite;
        fixedFixture.Modifiable = UInt128.MaxValue;
        Check(!fixedFixture.Match(MapDrawingProgram.Compile(Query([MapPointType.Elite, MapPointType.Elite]), 12)),
            "start and boss excluded even if modifiable");

        var diamond = new Fixture(31);
        diamond.Add(3, 7); diamond.Add(3, 8); diamond.Add(7, 14); diamond.Add(8, 14); diamond.Add(14, 31);
        diamond.Types[7] = (byte)MapPointType.Elite;
        diamond.Types[8] = (byte)MapPointType.RestSite;
        diamond.Types[14] = (byte)MapPointType.Unknown;
        Check(!diamond.Match(MapDrawingProgram.Compile(Query([MapPointType.Elite, MapPointType.RestSite]), 12)),
            "merge preserves distinct tuples rather than combining branches");
        diamond.Types[14] = (byte)MapPointType.RestSite;
        Check(diamond.Match(MapDrawingProgram.Compile(Query([MapPointType.Elite, MapPointType.RestSite]), 12)),
            "one actual route can complete a group after merge");

        var deadEnd = new Fixture(31);
        deadEnd.Add(3, 7); deadEnd.Add(3, 8); deadEnd.Add(8, 31);
        deadEnd.Types[7] = (byte)MapPointType.Elite;
        deadEnd.Types[8] = (byte)MapPointType.Monster;
        Check(!deadEnd.Match(MapDrawingProgram.Compile(Query([MapPointType.Elite]), 12)),
            "accepting state at a dead end does not count");
        Check(deadEnd.Match(MapDrawingProgram.Compile(Query(), 12)), "empty query still requires a boss route");
        var disconnected = new Fixture(31); disconnected.Add(3, 7);
        Check(!disconnected.Match(MapDrawingProgram.Compile(Query(), 12)), "empty query rejects a disconnected boss");

        var unassigned = Fixture.Path([MapPointType.Unassigned]);
        var monster = MapDrawingProgram.Compile(Query([MapPointType.Monster]), 12);
        Check(!unassigned.Match(monster), "full assignment does not silently project unassigned");
        Check(unassigned.Match(monster, true), "explicit Round1 projection treats unassigned as monster");
        Check(unassigned.Types[7] == 0, "projection does not mutate input assignment");
        unassigned.Modifiable = 0;
        Check(!unassigned.Match(monster, true), "projection cannot count a fixed room");

        var sourceNodes = new List<MapPointType> { MapPointType.Shop };
        var sourceGroups = new List<MapRouteGroup> { new(sourceNodes) };
        var copied = MapDrawingProgram.Compile(MapRouteSequenceQuery.FromGroups(sourceGroups), 12);
        sourceNodes[0] = MapPointType.Elite;
        sourceGroups.Clear();
        var exported = copied.ExportTransitions();
        Array.Fill(exported, 0u);
        Check(Fixture.Path([MapPointType.Shop]).Match(copied), "compiled request and exported table are immutable");
        Check(!Fixture.Path([MapPointType.Elite]).Match(copied), "source mutation does not rewrite obligation");

        // The authored request is large, but its table would have an enormous
        // five-dimensional product. The row-bound proof must run first.
        var enormous = Query(Rooms.SelectMany(room => Enumerable.Repeat(room, 1000)).ToArray());
        var impossible = MapDrawingProgram.Compile(enormous, 12);
        Check(impossible.AlwaysReject && impossible.StateCount == 1 && impossible.ExportTransitions().Length == 9,
            "impossible huge request avoids product allocation");
        Check(!Fixture.Path([]).Match(impossible), "always-reject program rejects");
        Throws<NotSupportedException>(() => MapDrawingProgram.Compile(MapRouteSequenceQuery.FromGroups([],
            [new MapRouteCountBounds(MapPointType.Elite)]), 12), "additional count obligation stays explicit");
        Throws<ArgumentOutOfRangeException>(() => MapDrawingProgram.Compile(Query(), -1), "negative row budget");
        Throws<NotSupportedException>(() => MapDrawingProgram.Compile(enormous, 100000), "product overflow rejected explicitly");

        // Maximize each group's product by balancing its five counts; a small
        // integer DP then checks all ordered group-size partitions of 0..12.
        var maxIncomplete = new int[13];
        for (int budget = 1; budget <= 12; budget++)
            for (int size = 1; size <= budget; size++)
            {
                int product = 1;
                for (int dimension = 0; dimension < 5; dimension++)
                    product *= size / 5 + (dimension < size % 5 ? 1 : 0) + 1;
                maxIncomplete[budget] = Math.Max(maxIncomplete[budget], maxIncomplete[budget - size] + product - 1);
            }
        Check(maxIncomplete[12] + 1 == 432, "all grouping partitions under 12 rooms fit 432 states");
        var twelve = Rooms.SelectMany((room, index) => Enumerable.Repeat(room, index < 2 ? 3 : 2)).ToArray();
        var wide = MapDrawingProgram.Compile(Query(twelve), 12);
        Check(wide.StateCount == 432 && wide.WordCount == 14 && wide.AcceptState == 431,
            "432-state program crosses eight words");
        Check(Fixture.Path(twelve.AsEnumerable().Reverse().ToArray()).Match(wide), "wide state bitsets reach accepting word");
        Check(!Fixture.Path(twelve.Skip(1).ToArray()).Match(wide), "wide states preserve missing repeated count");
        var table = wide.ExportTransitions();
        for (int room = 0; room < 9; room++)
            Check(table[wide.AcceptState * 9 + room] == wide.AcceptState, "accepting state is absorbing " + room);
        Check(Enumerable.Range(0, wide.StateCount).All(state => table[state * 9] == state), "unassigned transitions are identity");

        // A few distinct merge/dead-end shapes reuse the independent complete-route
        // oracle. The research already owns exhaustive/random corpus evidence.
        MapRouteSequenceQuery[] tinyQueries =
        [Query(), Query([MapPointType.Elite, MapPointType.RestSite]),
            Query([MapPointType.RestSite], [MapPointType.Elite]), Query([MapPointType.Monster], [MapPointType.Unknown])];
        (int From, int To)[] tinyEdges = [(3, 7), (3, 8), (7, 14), (7, 15), (8, 14), (8, 15), (14, 24), (15, 24)];
        foreach (int mask in new[] { 0, 7, 37, 85, 127, 170, 239, 255 })
        {
            var fixture = new Fixture(24);
            fixture.Types[7] = fixture.Types[15] = (byte)MapPointType.Elite;
            fixture.Types[8] = fixture.Types[14] = (byte)MapPointType.RestSite;
            for (int edge = 0; edge < tinyEdges.Length; edge++)
                if ((mask & (1 << edge)) != 0) fixture.Add(tinyEdges[edge].From, tinyEdges[edge].To);
            foreach (var query in tinyQueries)
                Check(fixture.Match(MapDrawingProgram.Compile(query, 2)) == Oracle(fixture, query, false),
                    "representative DAG keeps whole-route state " + mask);
        }
        return checks;
    }

    private static MapRouteSequenceQuery Query(params MapPointType[][] groups) =>
        MapRouteSequenceQuery.FromGroups(groups.Select(nodes => new MapRouteGroup(nodes)));

    // This oracle knows nothing about compiled state numbers or transition tables.
    // It enumerates whole routes; on each one it greedily removes required room
    // occurrences from the current group, finishing each group as early as possible.
    private static bool Oracle(Fixture fixture, MapRouteSequenceQuery query, bool project)
    {
        var path = new List<MapPointType>();
        bool MatchesPath()
        {
            int cursor = 0;
            foreach (var group in query.Groups)
            {
                var remaining = group.Nodes.ToList();
                while (remaining.Count != 0 && cursor < path.Count)
                {
                    int found = remaining.IndexOf(path[cursor++]);
                    if (found >= 0) remaining.RemoveAt(found);
                }
                if (remaining.Count != 0) return false;
            }
            return true;
        }
        bool Visit(int node)
        {
            if (node == fixture.Boss) return MatchesPath();
            bool countable = node != 3 && (fixture.Modifiable & ((UInt128)1 << node)) != 0;
            if (countable)
            {
                var room = (MapPointType)fixture.Types[node];
                path.Add(project && room == MapPointType.Unassigned ? MapPointType.Monster : room);
            }
            for (int lane = 0; lane < fixture.Degree[node]; lane++)
                if (Visit(fixture.Children[node * 7 + lane]))
                {
                    if (countable) path.RemoveAt(path.Count - 1);
                    return true;
                }
            if (countable) path.RemoveAt(path.Count - 1);
            return false;
        }
        return Visit(3);
    }

    private sealed class Fixture(int boss)
    {
        public readonly byte[] Types = new byte[119], Degree = new byte[119], Children = new byte[119 * 7];
        public UInt128 Modifiable = UInt128.MaxValue;
        public int Boss { get; } = boss;

        public void Add(int from, int to) => Children[from * 7 + Degree[from]++] = (byte)to;

        public bool Match(MapDrawingProgram program, bool project = false) =>
            program.Match(Types, Degree, Children, Modifiable, Boss, new uint[119 * program.WordCount], project);

        public static Fixture Path(MapPointType[] rooms)
        {
            var fixture = new Fixture((rooms.Length + 1) * 7 + 3);
            int previous = 3;
            for (int i = 0; i < rooms.Length; i++)
            {
                int node = (i + 1) * 7;
                fixture.Types[node] = (byte)rooms[i];
                fixture.Add(previous, node);
                previous = node;
            }
            fixture.Add(previous, fixture.Boss);
            return fixture;
        }
    }
}
