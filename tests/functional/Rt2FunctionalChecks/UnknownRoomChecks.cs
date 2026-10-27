using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Presentation.Ui1;
using GameMap = MegaCrit.Sts2.Core.Map;

internal static class UnknownRoomChecks
{
    private static int _rolls;
    internal static int Run()
    {
        CheckProbability();
        // UnlockState.all eagerly enumerates every encounter. This isolated fixture only
        // needs NumberOfRuns, so bootstrap that unused cache empty instead of booting Godot.
        typeof(ModelDb).GetField("_allEncounters",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,Array.Empty<EncounterModel>());
        var run = DispatchProxy.Create<IRunState, OrdinaryUnknownRunProxy>();
        CheckAct1Filter(run);
        foreach (string seed in new[] { "000000000000", "000000000006", "000000067C0S", "SKDNXGNU2A16" })
        foreach (int skip in new[] { 0, 1, 7, 23 })
        {
            var baseline = OrdinaryUnknownRoomPredictor.Baseline(seed, skip, 32);
            var rng = new RunRngSet(seed).UnknownMapPoint;
            for (int i = 0; i < skip; i++) rng.NextFloat();
            var odds = new UnknownMapPointOdds(rng);
            foreach (var step in baseline.Steps) Compare(step, odds, rng, Array.Empty<RoomType>(), run);
            int before = rng.ToSerializable().counter;
            odds.ResetToBase();
            Require(Snapshot(odds) == new UnknownOddsSnapshot(.1f,-1f,.02f,.03f) && rng.ToSerializable().counter == before,"Act reset changed RNG");
        }
        object? divergence = null;
        bool differentCounts = false, sawPreviousShop = false, sawAllChildrenShop = false;
        for (ulong ordinal = 0; ordinal < 16; ordinal++)
        {
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            var map = Beta111MapPredictor.Predict(seed,RuntimeProfileId.Beta111,new ModelKey("ACT","OVERGROWTH"),0,1,1,false,default);
            var set = new MapRouteSet(map);
            var records = new List<(MapRouteCommitment Route, UnknownRoomSequence Sequence)>();
            var nodes = map.Points.ToDictionary(p=>p.Position);
            for (int i = 0; i < (int)System.Numerics.BigInteger.Min(set.Count,256); i++)
            {
                var route = new MapRouteCommitment(map,set.At(i));
                var sequence = OrdinaryUnknownRoomPredictor.ForRoute(route,Array.Empty<MapRouteCommitment>());
                var rng = new RunRngSet(seed).UnknownMapPoint;
                var odds = new UnknownMapPointOdds(rng);
                RoomType previous = RoomType.Event; int observed = 0;
                foreach (var coord in route.Coordinates)
                {
                    var point = nodes[coord];
                    if (point.Type == MapPointType.Unknown)
                    {
                        var history = new MapPointHistoryEntry { Rooms = new() { new MapPointRoomHistoryEntry { RoomType = previous } } };
                        var children = point.Children.Select(p=>new GameMap.MapPoint(p.Column,p.Row) { PointType=(GameMap.MapPointType)(int)nodes[p].Type }).ToArray();
                        var blocked = RunManager.BuildRoomTypeBlacklist(history,children);
                        sawPreviousShop |= previous == RoomType.Shop;
                        sawAllChildrenShop |= children.Length>0 && children.All(p=>p.PointType==GameMap.MapPointType.Shop);
                        var step = sequence.Steps[observed++];
                        Require(step.Position==coord && step.ShopBlacklisted==blocked.Contains(RoomType.Shop),"Wrong route blacklist context");
                        previous = Compare(step,odds,rng,blocked,run);
                    }
                    else previous = point.Type==MapPointType.Shop ? RoomType.Shop : RoomType.Event;
                }
                records.Add((route,sequence));
            }
            differentCounts |= records.Select(p=>p.Sequence.Steps.Count).Distinct().Count()>1;
            if (divergence is null)
                for(int a=0;a<records.Count && divergence is null;a++)
                for(int b=a+1;b<records.Count && divergence is null;b++)
                {
                    var x=records[a];var y=records[b];
                    int n=Math.Min(x.Sequence.Steps.Count,y.Sequence.Steps.Count);
                    for(int j=0;j<n;j++)
                        if(x.Sequence.Steps[j].Room!=y.Sequence.Steps[j].Room)
                        {
                            divergence = new { Seed=seed, UnknownOrdinal=j+1,
                                A=new { Route=x.Route.Coordinates, Steps=x.Sequence.Steps }, B=new { Route=y.Route.Coordinates, Steps=y.Sequence.Steps } };
                            break;
                        }
                }
        }
        Require(divergence is not null && differentCounts && sawPreviousShop && sawAllChildrenShop,"Missing natural route/Shop divergence witnesses");
        // Cross-Act RNG advances by prior committed Unknown visits, while odds start fresh.
        var first = Beta111MapPredictor.Predict("000000067C0S",RuntimeProfileId.Beta111,new("ACT","OVERGROWTH"),0,1,1,false,default);
        var second = Beta111MapPredictor.Predict(first.Seed,RuntimeProfileId.Beta111,new("ACT","HIVE"),1,1,1,false,default);
        var firstRoute = new MapRouteCommitment(first,new MapRouteSet(first).At(0));
        var secondRoute = new MapRouteCommitment(second,new MapRouteSet(second).At(0));
        var cross = OrdinaryUnknownRoomPredictor.ForRoute(secondRoute,new[]{firstRoute});
        Require(cross.PriorDraws == firstRoute.Coordinates.Count(p=>first.Points.Single(n=>n.Position==p).Type==MapPointType.Unknown),"Cross-Act cursor");
        try { OrdinaryUnknownRoomPredictor.ForRoute(secondRoute,Array.Empty<MapRouteCommitment>()); throw new Exception("Missing prior route accepted"); }
        catch(ArgumentException) { }
        var crossRng = new RunRngSet(first.Seed).UnknownMapPoint;
        for(int i=0;i<cross.PriorDraws;i++) crossRng.NextFloat();
        var crossOdds = new UnknownMapPointOdds(crossRng);
        foreach(var step in cross.Steps) Compare(step,crossOdds,crossRng,step.ShopBlacklisted ? new[]{RoomType.Shop}:Array.Empty<RoomType>(),run);
        var proxy=(OrdinaryUnknownRunProxy)(object)run;
        proxy.Tutorial=true;
        var tutorialRng=new RunRngSet(first.Seed).UnknownMapPoint;
        var tutorialOdds=new UnknownMapPointOdds(tutorialRng);
        for(int i=0;i<3;i++)
        {
            var actual=tutorialOdds.Roll(Array.Empty<RoomType>(),run);
            Require(actual==(i<2?RoomType.Event:RoomType.Monster) && tutorialRng.ToSerializable().counter==0 && Snapshot(tutorialOdds)==new UnknownOddsSnapshot(.1f,-1f,.02f,.03f),"Tutorial contract boundary");
            proxy.History[0].Add(new MapPointHistoryEntry{MapPointType=GameMap.MapPointType.Unknown});
        }
        proxy.Tutorial=false;
        string directory=Path.GetFullPath(".build-evidence/unknown-room-parity");Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{ Passed=true, Rolls=_rolls, differentCounts,sawPreviousShop,sawAllChildrenShop,divergence },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"UnknownRoom PASS: {_rolls} actual DLL rolls/float odds/counters; conditional prefix/Joint/presentation/ETA probability; Act 1 filter/Juzu; natural route divergence; previous/all-child Shop guards; Act reset/cursor. {directory}");
        return 0;
    }
    private static void CheckProbability()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        SearchSelectivityInput Input(SearchQuery query) => SearchSelectivityInput.From(SearchCompiler.Compile(query, context));
        var expected = new[] { .85d, .595d, .32725d };
        var withJuzu = new[] { .95d, .855d, .72675d };
        for (int count = 1; count <= 3; count++)
        {
            var input = Input(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = count });
            var joint = JointSelectivityEstimator.EstimateQuery(input);
            Require(joint.JointlyPriced && Math.Abs(joint.Probability!.Value - expected[count - 1]) < 1e-7,
                "Ordinary prefix must use successive conditional odds, with Shop still participating");
            Require(Math.Abs(FirstActUnknownEventProbability.ConditionalPrefix(count, true) - withJuzu[count - 1]) < 1e-7,
                "Juzu removes Monster and its growth, preserving Treasure and Shop growth");
            var view = SearchProbabilityPresentationBuilder.Build(input.CompiledSearch, joint);
            Require(view.TotalProbability == joint.Probability && view.Rows.Single(r =>
                r.Kind == SearchProbabilityRowKind.FirstActUnknownEventPrefix).Probability == joint.Probability,
                "Prefix family row and total consume the same probability authority");
            Require(input.CompiledSearch.NormalizedQuery.FirstActUnknownEventPrefixCount == count &&
                CompiledSearchEvaluationProjector.Project(input.CompiledSearch).Evaluation.FirstActUnknownEventPrefixCount == count,
                "Pricing retains the authored Exact obligation");
        }
        Require(Math.Abs(FirstActUnknownEventProbability.ConditionalPrefix(3, false) - Math.Pow(.85, 3)) > .2,
            "Independent constant-probability shortcut must not replace state recurrence");
        var prefix = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 };
        var offered = prefix with { LegacyNeow = prefix.LegacyNeow with {
            NeowRelics = new([BaseGameModelKeys.Relics.SmallCapsule], [], []) } };
        Require(FirstActUnknownEventProbability.EstimateComponent(Input(offered)).Probability ==
            FirstActUnknownEventProbability.ConditionalPrefix(3, false), "An offered Capsule is not acquired Juzu");
        var capsule = prefix with { OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule) };
        Require(FirstActUnknownEventProbability.EstimateComponent(Input(capsule)).Probability ==
            FirstActUnknownEventProbability.ConditionalPrefix(3, false) &&
            JointSelectivityEstimator.EstimateQuery(Input(capsule)).JointlyPriced,
            "Unspecified acquired Capsule output has no modeled Juzu influence");
        var juzu = capsule with { StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.SmallCapsule,
            NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
            NeowStructuredOutputKind.Relic, [new("RELIC", "JUZU_BRACELET")])] };
        Require(FirstActUnknownEventProbability.EstimateComponent(Input(juzu)).Probability ==
            FirstActUnknownEventProbability.ConditionalPrefix(3, true), "Authored acquired Juzu conditions the prefix");
        var plainOpening = prefix with { OpeningRoute = new(BaseGameModelKeys.Relics.SilverCrucible) };
        var baseOpening = JointSelectivityEstimator.EstimateQuery(Input(plainOpening with { FirstActUnknownEventPrefixCount = 0 }));
        var combined = JointSelectivityEstimator.EstimateQuery(Input(plainOpening));
        Require(baseOpening.Probability is > 0 && combined.Probability is > 0 && Math.Abs(combined.Probability.Value -
            baseOpening.Probability.Value * FirstActUnknownEventProbability.ConditionalPrefix(3, false)) < 1e-12,
            "The whole query pays both the opening predicate and conditional prefix");
        var treasure = prefix with { TreasureRelics = new(1, 1, TreasureRelicOrderMode.Ordered,
            [new ModelKey("RELIC", "JUZU_BRACELET"), null]) };
        var treasureComponent = TreasureRelicProbability.Estimate(Input(treasure));
        var treasureJoint = JointSelectivityEstimator.EstimateQuery(Input(treasure));
        Require(treasureJoint.JointlyPriced && treasureComponent.Probability.HasValue &&
            Math.Abs(treasureJoint.Probability!.Value - treasureComponent.Probability.Value *
                FirstActUnknownEventProbability.ConditionalPrefix(3, false)) < 1e-12,
            "The priced prefix and declared treasure factor both participate once");
        var compiled = Input(prefix).CompiledSearch;
        var request = ExactSearchExecutionRequestFactory.Compile(compiled, new("000000000000", 100, 1, 1));
        Require(request.Success && request.Plan is not null, "Standalone prefix compiles for ETA");
        var eta = FamilySearchEtaProjectorV1.Project(request.Plan!, FamilyExecutionCoordinator.Plan(request.Plan!, false),
            liveExactMsPerAttempt: .01);
        Require(eta.AcceptedResultProbability == FirstActUnknownEventProbability.ConditionalPrefix(3, false) &&
            eta.FirstMeanRootHorizon == 4, "ETA consumes the priced whole-query probability");
    }
    private static void CheckAct1Filter(IRunState run)
    {
        var proxy = (OrdinaryUnknownRunProxy)(object)run;
        // This native hook reads no model state; avoid registering models or
        // requiring a Godot bootstrap just to exercise its category removal.
        var juzuHook = (JuzuBracelet)RuntimeHelpers.GetUninitializedObject(typeof(JuzuBracelet));
        bool sawJuzuDivergence = false, sawJuzuNonEventReplacement = false;
        bool sawJuzuShop = false, sawJuzuTreasure = false;
        foreach (int count in new[] { 0, 4 })
        {
            try { OrdinaryUnknownRoomPredictor.ForAct1Filter(0, count, false); throw new Exception("Invalid filter count accepted"); }
            catch (ArgumentOutOfRangeException) { }
        }
        for (ulong ordinal = 0; ordinal < 256; ordinal++)
        {
            string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
            var baseline = OrdinaryUnknownRoomPredictor.Baseline(seed, 0, 3);
            var ordinary = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, 3, false);
            var juzu = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, 3, true);
            Require(ordinary.Steps.SequenceEqual(baseline.Steps), "No-Juzu filter changed baseline");
            foreach (bool hasJuzu in new[] { false, true })
            {
                proxy.HookListeners = hasJuzu ? new AbstractModel[] { juzuHook } : Array.Empty<AbstractModel>();
                var full = hasJuzu ? juzu : ordinary;
                foreach (int count in new[] { 1, 2, 3 })
                {
                    var sequence = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, count, hasJuzu);
                    Require(!sequence.RouteBound && sequence.PriorDraws == 0 && sequence.Steps.Count == count &&
                        sequence.Steps.SequenceEqual(full.Steps.Take(count)), "Filter fresh-state prefix contract");
                    var rng = new RunRngSet(seed).UnknownMapPoint;
                    var odds = new UnknownMapPointOdds(rng);
                    foreach (var step in sequence.Steps)
                    {
                        Require(step.Position is null && !step.ShopBlacklisted && step.RngCounter == step.Ordinal,
                            "Filter ordinal/cursor/Shop assumptions");
                        Compare(step, odds, rng, Array.Empty<RoomType>(), run);
                        if (hasJuzu)
                            Require(step.Room != UnknownRoomCategory.Monster && step.Before.Monster == .1f && step.After.Monster == .1f,
                                "Juzu must exclude Monster and freeze its odds");
                    }
                }
            }
            for (int i = 0; i < 3; i++)
            {
                sawJuzuDivergence |= ordinary.Steps[i].Room != juzu.Steps[i].Room;
                sawJuzuNonEventReplacement |= ordinary.Steps[i].Room == UnknownRoomCategory.Monster &&
                    juzu.Steps[i].Room is UnknownRoomCategory.Shop or UnknownRoomCategory.Treasure;
                sawJuzuShop |= juzu.Steps[i].Room == UnknownRoomCategory.Shop;
                sawJuzuTreasure |= juzu.Steps[i].Room == UnknownRoomCategory.Treasure;
            }
        }
        proxy.HookListeners = Array.Empty<AbstractModel>();
        Require(sawJuzuDivergence && sawJuzuNonEventReplacement && sawJuzuShop && sawJuzuTreasure,
            "Missing Juzu reroll/Shop/Treasure parity witnesses");
    }
    private static RoomType Compare(UnknownRoomObservation step, UnknownMapPointOdds odds, MegaCrit.Sts2.Core.Random.Rng rng, IEnumerable<RoomType> blocked, IRunState run)
    {
        Require(step.Before==Snapshot(odds),"Odds before");
        var room=odds.Roll(blocked,run);
        Require(room.ToString()==step.Room.ToString() && step.After==Snapshot(odds) && step.RngCounter==rng.ToSerializable().counter,"DLL outcome/odds/counter mismatch");
        _rolls++;return room;
    }
    private static UnknownOddsSnapshot Snapshot(UnknownMapPointOdds odds)=>new(odds.MonsterOdds,odds.EliteOdds,odds.TreasureOdds,odds.ShopOdds);
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
}

// Test-only IRunState: real DLL Roll/Hook entrypoints with no active hook listeners.
// Never constructs, replaces or mutates a live RunManager/RunState.
public class OrdinaryUnknownRunProxy : DispatchProxy
{
    private readonly UnlockState _unlock = NewUnlock();
    private readonly UnlockState _tutorialUnlock = new(Array.Empty<string>(),Array.Empty<ModelId>(),0);
    public bool Tutorial { get; set; }
    public IReadOnlyList<AbstractModel> HookListeners { get; set; } = Array.Empty<AbstractModel>();
    public List<List<MapPointHistoryEntry>> History { get; } = new(){new()};
    private static UnlockState NewUnlock()
    {
        return new UnlockState(Array.Empty<string>(),Array.Empty<ModelId>(),1);
    }
    protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>targetMethod?.Name switch
    {
        "get_UnlockState"=>Tutorial ? _tutorialUnlock : _unlock,
        "get_MapPointHistory"=>History,
        "IterateHookListeners"=>HookListeners,
        _=>throw new NotSupportedException(targetMethod?.Name)
    };
}
