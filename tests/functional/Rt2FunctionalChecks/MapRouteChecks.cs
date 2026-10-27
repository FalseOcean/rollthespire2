using RolltheSpire2.Ui.Pages.Search;
using System.Numerics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;

internal static class MapRouteChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static int MeasureCost()
    {
        var authority = RolltheSpire2.Tests.FastSearch.Beta110FastSearchFixtureAuthority.CreateBeta111(
            BaseGameModelKeys.Characters.Ironclad, 10, 0, 1);
        var context = RolltheSpire2.Search.Semantics.SearchContextFactory.From(RuntimeProfileId.Beta111,
            BaseGameModelKeys.Characters.Ironclad, 10, authority,
            new("0.111.0", "0.111.0", "beta", "MapCost", true, ""),
            RolltheSpire2.Core.World.AncientOptionConditionProfile.BroadDefault);
        foreach (int acts in new[] { 1, 3 })
        {
            var conditions = Enumerable.Range(1, acts).Select(act =>
                new RolltheSpire2.Search.Semantics.StandardMapSearchCondition(act,
                    RolltheSpire2.Search.Semantics.StandardMapMetric.GuaranteedMonster,
                    RolltheSpire2.Search.Semantics.StandardMapComparison.AtLeast, 0)).ToArray();
            var compiled = RolltheSpire2.Search.Semantics.SearchCompiler.Compile(
                RolltheSpire2.Search.Semantics.SearchQuery.Empty with { StandardMaps = conditions }, context);
            var request = RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.Compile(compiled,
                new("000000000000", 1, 1, 1)).Plan!;
            var fast = new RolltheSpire2.Search.FamilyExecution.StandardMapFamily(request);
            var allocation = RolltheSpire2.Search.FamilyExecution.FamilyExecutionCoordinator.Plan(request, false);
            var eta = RolltheSpire2.Search.FamilyExecution.FamilySearchEtaProjectorV1.Project(request, allocation);
            Check(eta.Status == RolltheSpire2.Search.FamilyExecution.FamilySearchEtaStatus.Unavailable &&
                eta.TargetFamilyPipelineMs is null && fast.Survival.SurvivalProbability == 1,
                "Map pricing remains deferred; explicit research timing does not restore a production quote");
            Console.WriteLine($"SOLO_MAP_ETA acts={acts};targetMs={eta.TargetSearchMeanMs};" +
                $"familyMs={eta.TargetFamilyPipelineMs};exactMs={eta.TargetExactTailMs}");
            foreach (string boundary in new[] { "FastFinalCpu", "WholeProductionExact" })
            {
                ulong offset = (ulong)((acts - 1) * 4096 + (boundary == "WholeProductionExact" ? 2048 : 0));
                bool Evaluate(ulong ordinal)
                {
                    string seed = RolltheSpire2.Search.FamilyExecution.VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                    return boundary == "FastFinalCpu"
                        ? fast.MatchesRoot(Beta111Profile.Instance.ComputeRootSeed(seed))
                        : RolltheSpire2.Search.Runtime.ProductionExactSearchEvaluator.Evaluate(request,
                            RolltheSpire2.Core.Prediction.TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch;
                }
                for (ulong i = 0; i < 64; i++) _ = Evaluate(offset + i);
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    int hits = 0;
                    for (ulong i = 0; i < 64; i++) if (Evaluate(offset + (ulong)(repeat + 1) * 64 + i)) hits++;
                    Console.WriteLine($"SOLO_MAP_COST acts={acts};boundary={boundary};repeat={repeat};" +
                        $"roots=64;start={offset + (ulong)(repeat + 1) * 64};elapsedMs={watch.Elapsed.TotalMilliseconds:F4};" +
                        $"hits={hits};warmup=64DisjointRoots;compileExcluded=true");
                }
            }
            foreach (int workers in new[] { 1, 4, 8 })
            {
                var parallelRequest = RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.Compile(compiled,
                    new("000000000000", 1024, 1, workers)).Plan!;
                var parallelFamily = new RolltheSpire2.Search.FamilyExecution.StandardMapFamily(parallelRequest);
                var batch = new RolltheSpire2.Search.FamilyExecution.SearchBatch(2048, 1024);
                var input = RolltheSpire2.Search.FamilyExecution.FamilyCandidateSet.Dense(batch);
                var window = new RolltheSpire2.Search.FamilyExecution.FamilyObservationWindow(batch, parallelRequest);
                var execution = new RolltheSpire2.Search.FamilyExecution.FamilyExecutionContext(requiresGpu: false);
                try
                {
                    _ = parallelFamily.InvokeAsync(execution, window, input, default).AsTask().GetAwaiter().GetResult();
                    for (int repeat = 0; repeat < 3; repeat++)
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        var result = parallelFamily.InvokeAsync(execution, window, input, default).AsTask().GetAwaiter().GetResult();
                        Console.WriteLine($"SOLO_MAP_WINDOW acts={acts};workers={workers};repeat={repeat};" +
                            $"roots=1024;start=2048;elapsedMs={watch.Elapsed.TotalMilliseconds:F4};" +
                            $"survivors={result.Count};boundary=CpuCanonicalAbi1Ready;warmup=1");
                    }
                }
                finally
                {
                    parallelFamily.DisposeAsync(execution).AsTask().GetAwaiter().GetResult();
                    execution.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
        }
        return 0;
    }
    private static void CheckProbabilityCorpus()
    {
        var authority=RolltheSpire2.Tests.FastSearch.Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad,10,0,1);
        var context=RolltheSpire2.Search.Semantics.SearchContextFactory.From(RuntimeProfileId.Beta111,BaseGameModelKeys.Characters.Ironclad,10,authority,
            new("0.111.0","0.111.0","beta","MapProbability",true,""),RolltheSpire2.Core.World.AncientOptionConditionProfile.BroadDefault);
        RolltheSpire2.Search.Selectivity.JointSelectivityResult Price(params RolltheSpire2.Search.Semantics.StandardMapSearchCondition[] conditions) =>
            RolltheSpire2.Search.Selectivity.JointSelectivityEstimator.EstimateQuery(RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(
                RolltheSpire2.Search.Semantics.SearchCompiler.Compile(RolltheSpire2.Search.Semantics.SearchQuery.Empty with {StandardMaps=conditions},context)));
        var above=new RolltheSpire2.Search.Semantics.StandardMapSearchCondition(1,RolltheSpire2.Search.Semantics.StandardMapMetric.GuaranteedElite,RolltheSpire2.Search.Semantics.StandardMapComparison.AtLeast,1);
        var below=above with {Comparison=RolltheSpire2.Search.Semantics.StandardMapComparison.AtMost,Value=0};
        Check(Math.Abs(Price(above).Probability!.Value+Price(below).Probability!.Value-1)<1e-12,"Map empirical complementary counts");
        Check(Price(above).Confidence==RolltheSpire2.Search.Selectivity.SearchSelectivityConfidence.Low,"Map is labeled empirical");
        Check(Price(above with {Value=100}).Probability is null && !Price(above with {Value=100}).ExactlyImpossible,"Zero sample count is not a proof");
        var total=above with {Scope=0,RouteObjective=true,Metric=RolltheSpire2.Search.Semantics.StandardMapMetric.ReachableMaxElite,Value=0};
        Check(Math.Abs(Price(total).Probability!.Value-1)<1e-12 && Price(total).Assumptions.Any(a=>a.Contains("NotPairedSeedJointData")),"Total map convolution is explicitly modeled");
        Check(Math.Abs(Price(total,above).Probability!.Value-Price(above).Probability!.Value)<1e-12,"Act property paid once inside total histogram model");
        var max=above with {Metric=RolltheSpire2.Search.Semantics.StandardMapMetric.ReachableMaxElite,Value=2};
        Check(Price(above,max).Probability is {} joint && joint<=Price(above).Probability && joint<=Price(max).Probability,"Same-map joint bounded by marginals");
        Check(Price(max,max with {Metric=RolltheSpire2.Search.Semantics.StandardMapMetric.ReachableMaxRest}).Probability is null,"Missing multiple-max projection stays unknown");
        var unseen=Price(above with {Value=100});
        Check(RolltheSpire2.Ui.Pages.Search.QueryWorkbenchFrame.MapProbabilityExplanation(unseen.EvidenceCode,false,true).Contains("没有命中"),"No-sample explanation reaches UI");
        Check(RolltheSpire2.Ui.Pages.Search.QueryWorkbenchFrame.MapProbabilityExplanation(unseen.EvidenceCode,false,false).Contains("does not prove impossibility"),"No-sample is not impossible in English UI");
        Console.WriteLine("MAP_PROBABILITY_CORPUS_PASS");
    }
    private static string Key(IEnumerable<MapPosition> path) => string.Join(";", path.Select(p => $"{p.Row:D2},{p.Column:D2}"));
    internal static int Run()
    {
        CheckProbabilityCorpus();
        int routesChecked = 0;
        // First four MapResearchGolden vectors, plus a full second-Boss graph.
        foreach (var (seed, level, act, index) in new[] {
            ("000000000000",0,"OVERGROWTH",0), ("000000000006",0,"OVERGROWTH",0),
            ("000000000000",1,"OVERGROWTH",0), ("000000000004",1,"OVERGROWTH",0),
            ("000000067C0S",10,"GLORY",2) })
        {
            var map = Beta111MapPredictor.Predict(seed, RuntimeProfileId.Beta111, new ModelKey("ACT",act), index, level, 1, index == 2, default);
            routesChecked += Verify(map);
        }
        // Research adversary: separate Elite/Rest branches, and a Shop dead end.
        MapPredictionPoint Node(int x, int y, MapPointType type, params MapPosition[] next) => new(new(x,y),type,true,next);
        var adversary = new MapPrediction("fixture",new("ACT","OVERGROWTH"),0,7,new[] {
            Node(3,0,MapPointType.Ancient,new(1,1),new(5,1),new(0,1)), Node(1,1,MapPointType.Elite,new MapPosition(3,3)),
            Node(5,1,MapPointType.RestSite,new MapPosition(3,3)), Node(0,1,MapPointType.Shop), Node(3,3,MapPointType.Boss) },0);
        Verify(adversary);
        Check(new MapRouteSet(adversary,MapPointType.Shop).Value == 0,"Dead-end counted as route");
        var elite = new MapRouteSet(adversary,MapPointType.Elite).At(0);
        Check(!elite.Contains(new MapPosition(5,1)),"Independent extrema spliced");
        var commitment = new MapRouteCommitment(adversary,elite); elite[1] = new(0,1);
        Check(commitment.Coordinates[1] == new MapPosition(1,1),"Commitment not immutable");
        var crossing = adversary with { Points = new[] {
            Node(3,0,MapPointType.Ancient,new(1,1),new(5,1)),
            Node(1,1,MapPointType.Monster,new MapPosition(5,2)), Node(5,1,MapPointType.Unknown,new MapPosition(1,2)),
            Node(5,2,MapPointType.Monster,new MapPosition(3,3)), Node(1,2,MapPointType.Unknown,new MapPosition(3,3)), Node(3,3,MapPointType.Boss) } };
        var crossingSet = new MapRouteSet(crossing);
        Check(!crossingSet.IsLegal(new[]{new MapPosition(3,0),new MapPosition(1,1),new MapPosition(1,2),new MapPosition(3,3)}),"Greedy invalid path fixture");
        Check(crossingSet.IsLegal(crossingSet.Match(new[]{new MapStrokePoint(1,0),new MapStrokePoint(1,3)})),"Crossing-neighbor stroke failed");
        // 2^80 equally optimal routes: count/rank must not overflow or enumerate the set.
        var many = new List<MapPredictionPoint> { Node(3,0,MapPointType.Ancient,new(1,1),new(5,1)) };
        for(int row=1; row<=80;row++) foreach(int col in new[]{1,5})
            many.Add(Node(col,row,MapPointType.Unknown,row==80 ? new[]{new MapPosition(3,81)} : new[]{new MapPosition(1,row+1),new MapPosition(5,row+1)}));
        many.Add(Node(3,81,MapPointType.Boss));
        var huge = new MapRouteSet(adversary with { Points=many },MapPointType.Unknown);
        Check(huge.Count == BigInteger.One << 80,"Big route count");
        Check(huge.Rank(huge.At(huge.Count-1)) == huge.Count-1,"Big route rank");
        Console.WriteLine($"MapRoute PASS: 5 research/current graphs; {routesChecked} exhaustive paths; scalar extrema, all optimal ranks, matching/reversal, dead ends, immutable commitment, 2^80 count.");
        return 0;
    }

    private static int Verify(MapPrediction map)
    {
        var nodes = map.Points.ToDictionary(p=>p.Position);
        var start = map.Points.MinBy(p=>p.Position.Row)!.Position;
        var end = map.Points.Where(p=>p.Type==MapPointType.Boss).MaxBy(p=>p.Position.Row)!.Position;
        var paths = new List<MapPosition[]>(); var scratch = new List<MapPosition>();
        void Walk(MapPosition p)
        {
            scratch.Add(p);
            if(p==end) paths.Add(scratch.ToArray()); else foreach(var next in nodes[p].Children) Walk(next);
            scratch.RemoveAt(scratch.Count-1);
        }
        Walk(start); paths = paths.OrderBy(Key,StringComparer.Ordinal).ToList();
        var all = new MapRouteSet(map);
        Check(all.Count==paths.Count,"All path count vs enumeration");
        foreach(var metric in new[]{MapPointType.Unknown,MapPointType.Elite,MapPointType.RestSite,MapPointType.Shop,MapPointType.Monster})
        {
            int Count(MapPosition[] path) => path.Count(p=>nodes[p].Type==metric);
            int target = metric==MapPointType.Monster ? paths.Min(Count) : paths.Max(Count);
            var expected = paths.Where(p=>Count(p)==target).ToArray();
            var optimal = new MapRouteSet(map,metric,metric==MapPointType.Monster);
            Check(optimal.Value==target && optimal.Count==expected.Length,"Extrema/count vs exhaustive");
            for(int i=0;i<expected.Length;i++)
                Check(optimal.IsLegal(optimal.At(i)) && Key(optimal.At(i))==Key(expected[i]) && optimal.Rank(expected[i])==i,"Optimal lexicographic rank");
            var restricted = optimal.Match(new[]{new MapStrokePoint(-3,0),new MapStrokePoint(9,end.Row)});
            Check(optimal.IsLegal(restricted) && Count(restricted)==target,"Optimal-set stroke restriction");
        }
        var strokes = new List<MapStrokePoint[]>();
        foreach(double x in new double[]{-5,0,3,6,11}) strokes.Add(new[]{new MapStrokePoint(x,0),new MapStrokePoint(x,end.Row)});
        strokes.Add(new[]{new MapStrokePoint(-5,0),new MapStrokePoint(10,end.Row)});
        strokes.Add(map.Points.GroupBy(p=>p.Position.Row).OrderBy(g=>g.Key).Select(g=>new MapStrokePoint(g.Key%2==0 ? g.Min(p=>p.Position.Column):g.Max(p=>p.Position.Column),g.Key)).ToArray());
        strokes.Add(new[]{new MapStrokePoint(2,4),new MapStrokePoint(5,4)});
        foreach(var stroke in strokes)
        {
            var actual = all.Match(stroke);
            double Cost(MapPosition[] path)=>path.Sum(p=>Math.Pow(p.Column-MapRouteSet.DesiredColumn(stroke,p.Row),2));
            Check(all.IsLegal(actual),"Illegal global match");
            Check(Math.Abs(Cost(actual)-paths.Min(Cost))<1e-7,"Not globally minimal");
            Check(Key(actual)==Key(all.Match(stroke.AsEnumerable().Reverse().ToArray())),"Drawing direction changed route");
        }
        return paths.Count;
    }
}

