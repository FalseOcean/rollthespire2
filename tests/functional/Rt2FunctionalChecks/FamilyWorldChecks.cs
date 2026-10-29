using RolltheSpire2.Core.World;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static class FamilyWorldChecks
{
    internal static int Run(int count)
    {
        CheckScalarBossBranches();
        CheckSharedAncientSlices();
        var world = Beta109WorldFixtureFactory.Create();
        world = world with { CapturedProfileId=RuntimeProfileId.Beta111,
            Beta109Generation=world.Beta109Generation! with { Profile=RuntimeProfileId.Beta111, WorldGenerationHooksExact=true, FixtureValidationStatus=RolltheSpire2.Core.World.Snapshots.Beta109FixtureValidationStatus.VerifiedRealGame, VerifiedRealGameFixtureIds=["ancient-identity:synthetic","boss:synthetic"], SharedAncients=[new("EVENT","DARV"),new("EVENT","OROBAS")], AllSharedAncients=[new("EVENT","DARV"),new("EVENT","OROBAS")] } };
        // Synthetic authority flags exercise the accepted-authority path; these
        // fixtures are NOT represented as Owner/game runtime verification.
        var gen=world.Beta109Generation!;
        var changed=gen.OrderedActCatalog.Select(a=>a with {
            OrderedGenerateAllEncounters=[..a.OrderedGenerateAllEncounters,a.RegularEncounters[0] with {
                EncounterKey=new("ENCOUNTER","W_REGULAR_EXTRA_"+a.Act),SourceOrdinal=99,ReferenceIdentityId=99,
                SharesTagsWithSourceOrdinals=[a.RegularEncounters[0].SourceOrdinal]}],
            TotalNormalRooms=9,
            OrderedRawEvents=[a.OrderedRawEvents[0],a.OrderedRawEvents[0],new("EVENT","COLORFUL_PHILOSOPHERS")],
            OrderedEligibleEvents=[a.OrderedRawEvents[0],a.OrderedRawEvents[0],new("EVENT","COLORFUL_PHILOSOPHERS"),gen.EventAuthority.OrderedSharedEventsRaw[0]] }).ToArray();
        var alternate=changed[0] with {ActKey=new("EVENT","SYNTH_ACT_1_ALT")};
        gen=gen with { OrderedActCatalog=[..changed,alternate],
            ActSelectionGroups=gen.ActSelectionGroups.Select(a=>a.Act==1?a with {EligibleActsInSourceOrder=[changed[0].ActKey,alternate.ActKey]}:a).ToArray(),
            EventAuthority=gen.EventAuthority with { CharacterCardPoolCount=1, CharacterCardPoolCountExact=true } };
        world=world with {Beta109Generation=gen};
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect,10,0,1).WithWorldAuthority(world);
        var context=new SearchContext(RuntimeProfileId.Beta111,authority.Character.CharacterKey,10,authority,
            new("0.111.0","0.111.0","beta","W-fixture",true,""),SearchEvaluationAssumptions.BroadDefault);
        var g=world.Beta109Generation!;
        var cases=new List<(string Name,SearchQuery Query)>();
        ModelKeySetFilter Any(ModelKey key)=>new([key],[],[]);
        void Add(string name,NeowSearchFilter f)=>cases.Add((name,LegacySearchQueryAdapter.FromFilter(f)));
        var empty=NeowSearchFilter.Empty;
        for(int act=1;act<=3;act++)
        {
            var a=g.OrderedActCatalog.First(a=>a.Act==act);
            Add("Boss"+act,empty with {BossOrdinalFilters=[new(act,1,Any(a.Bosses[0]))]});
            if(act==3)Add("BossSecond",empty with {BossOrdinalFilters=[new(act,2,Any(a.Bosses[1]))]});
            foreach(int h in new[]{1,10})Add($"Event{act}First{h}",empty with {EventSequenceConditions=[new(act,null,SearchSequenceRangeMode.FirstN,h,Any(a.OrderedRawEvents[0]))]});
            Add("ExactSlot"+act,empty with {EventSequenceConditions=[new(act,null,SearchSequenceRangeMode.ExactSlot,2,Any(a.OrderedRawEvents[0]))]});
            if(act>1){Add("LocalAncient"+act,empty with {AncientIdentityFilters=[new(act,Any(a.OrderedAncients[0]))]});
                Add("SharedAncient"+act,empty with {AncientIdentityFilters=[new(act,Any(g.SharedAncients[0]))]});}
            cases.Add(("Variant"+act,SearchQuery.Empty with {VariantBossBranches=[new(act,a.ActKey,ModelKeySetFilter.Empty,ModelKeySetFilter.Empty,false)]}));
        }
        Add("Mixed",empty with {BossOrdinalFilters=[new(1,1,Any(g.OrderedActCatalog[0].Bosses[0]))],
            EventSequenceConditions=[new(2,null,SearchSequenceRangeMode.FirstN,10,Any(g.OrderedActCatalog[1].OrderedRawEvents[0]))],
            AncientIdentityFilters=[new(3,Any(g.OrderedActCatalog[2].OrderedAncients[0]))]});
        Add("Zero",empty with {BossFilters=[new(1,Any(new("ENCOUNTER","NEVER")))]});
        var actOne=g.OrderedActCatalog[0];
        cases.Add(("VariantUnion",SearchQuery.Empty with {VariantBossBranches=[
            new(1,actOne.ActKey,Any(new("ENCOUNTER","NEVER")),ModelKeySetFilter.Empty,false),
            new(1,actOne.ActKey,Any(actOne.Bosses[0]),ModelKeySetFilter.Empty,false),
            new(1,alternate.ActKey,Any(alternate.Bosses[0]),ModelKeySetFilter.Empty,false)]}));
        foreach(var source in Enum.GetValues<RolltheSpire2.Core.Events.EventPoolSourceKind>())
            Add("Source"+source,empty with {EventSequenceConditions=[new(1,source,SearchSequenceRangeMode.ExactSlot,2,Any(actOne.OrderedRawEvents[0]))]});
        Add("BanAll",empty with {EventSequenceConditions=[new(2,null,SearchSequenceRangeMode.FirstN,10,
            new([], [g.OrderedActCatalog[1].OrderedRawEvents[0]], [new("EVENT","COLORFUL_PHILOSOPHERS")]))]});
        Add("OversizedFirstN",empty with {EventSequenceConditions=[new(1,null,SearchSequenceRangeMode.FirstN,300,Any(actOne.OrderedRawEvents[0]))]});
        int checks=0;var fixtures=new List<object>();
        string dir=Path.Combine(Environment.CurrentDirectory,".build-evidence","w-gpu");Directory.CreateDirectory(dir);
        foreach(var (name,q) in cases)
        {
            var compiled=SearchCompiler.Compile(q,context);
            var request=new ExactSearchExecutionRequest(compiled,new("000000000000",10000,30,1),"000000000000",10000,
                CompiledSearchEvaluationProjector.Project(compiled).Evaluation,CombatRewardRoutePolicyContract.None,compiled.SemanticFingerprint);
            if (!WorldFamilyReplay.HasConditions(request))
            {
                bool created = VariantOpeningFamily.TryCreate(request, out var variant, false);
                if (WorldFamily.TryCreate(request, out _) ||
                    created != VariantOpeningPreparation.Prepare(compiled).HasConditions ||
                    created && variant is not VariantOpeningFamily)
                    throw new Exception("Pure variants migrate to V without an empty W");
                checks++;
                continue; // V numerical oracle/transport is checked by its focused entry.
            }
            var replay=new WorldFamilyReplay(request);var gpu=new WorldFamilyGpuPlan(replay, specializeSolo: true);
            if(!WorldFamily.TryCreate(request,out var family)||family!.Coverage.Single()!="W.World")throw new Exception("W registration");
            var executionPlan=FamilyExecutionCoordinator.Plan(request);
            if(!executionPlan.ExecutionOrderUnits.Any(f=>f.FamilyId=="W.World") || executionPlan.ExecutionOrderUnits.Any(f=>f.FamilyId is not ("W.World" or "V.VariantOpening")))throw new Exception("Production residual W/V registration");
            if (gpu.GroupingK != 4 || !gpu.Revision(false).Contains(".Dense.K4.") || !gpu.Revision(true).Contains(".CompactAbi1.K4."))
                throw new Exception("W Production physical default/identity");
            if(name=="Boss1")
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var observations = (Array)typeof(WorldFamily).GetField("_observations",flags)!.GetValue(family)!;
                void Set(string field,object value)=>typeof(WorldFamily).GetField(field,flags)!.SetValue(family,value);
                void Observe(int mode,int population){
                    object o=observations.GetValue(mode)!;
                    var m=new WorldFamilyGpuMetrics(population,0,1,1,1,32,0,0,1,1,10);
                    o.GetType().GetMethod("Record",flags)!.Invoke(o,[m,gpu.Capacity*4]);
                }
                Set("_parity",true);
                Observe(0,gpu.Capacity*4);
                if(family.CapturePerformanceObservation().CalibrationEligible)throw new Exception("Cold evidence accepted");
                Observe(0,1);
                if(family.CapturePerformanceObservation().CalibrationEligible)throw new Exception("Underfill evidence accepted");
                Observe(0,gpu.Capacity*4);
                if(!family.CapturePerformanceObservation().CalibrationEligible)throw new Exception("Full-group evidence missing");
                Set("_compact",true);
                if(family.CapturePerformanceObservation().PhysicalBatchCount!=0)throw new Exception("Dense evidence leaked into Compact");
                Observe(1,1);Observe(1,1);
                if(family.CapturePerformanceObservation().CalibrationEligible||family.CaptureLivePerformanceSnapshot() is null)
                    throw new Exception("Compact underfill must remain live-only");
                Observe(1,gpu.Capacity*4);
                if(!family.CapturePerformanceObservation().CalibrationEligible)throw new Exception("Compact comparable evidence missing");
                Set("_faulted",true);
                if(family.CapturePerformanceObservation().CalibrationEligible||family.CaptureLivePerformanceSnapshot() is not null)
                    throw new Exception("Faulted evidence accepted");
                var mixedQuery=SearchCompiler.Compile(q with {OpeningRoute=new(new ModelKey("RELIC","ARCANE_SCROLL"))},context);
                var mixedRequest=new ExactSearchExecutionRequest(mixedQuery,request.RunOptions,"000000000000",10000,
                    CompiledSearchEvaluationProjector.Project(mixedQuery).Evaluation,CombatRewardRoutePolicyContract.None,mixedQuery.SemanticFingerprint);
                var mixed=FamilyExecutionCoordinator.Plan(mixedRequest);
                if(!mixed.ExecutionOrderUnits.Any(f=>f.FamilyId=="N.Neow")||!mixed.ExecutionOrderUnits.Any(f=>f.FamilyId=="W.World")||
                    FamilyExecutionCoordinator.ResolveExecutionWindowSize(mixedRequest,mixed.OrderedFamilies)!=
                        (mixed.OrderedFamilies.Any(f=>f.ConditionPerformance.UsesGpu) ? 1<<24 : FamilyCpuExecution.Capacity))
                    throw new Exception("N/W registration or selected CPU/GPU root geometry");
            }
            if(name=="OversizedFirstN")
            {
                // Current bounded-pool contract: FirstN beyond the pool observes
                // the entire pool. The frozen pre-optimization binary also uses GPU.
                if(!family.ConditionPerformance.UsesGpu)throw new Exception("Oversized FirstN remains bounded-pool GPU work");
                var cpu=((IFamilyInvocation)family).CpuRealizations.Single();
                var execution=new FamilyExecutionContext(false);var batch=new SearchBatch(0,67);
                var input=FamilyCandidateSet.FromAbi1(batch,[0,3,9,37,66]);
                var actual=cpu.InvokeAsync(execution,new(batch,request),input,default).GetAwaiter().GetResult();
                var expected=input.EnumerateLogicalOrdinals().Where(i=>replay.Matches(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i))));
                if(!actual.EnumerateLogicalOrdinals().SequenceEqual(expected))throw new Exception("CPU physical subset");
                using var cts=new CancellationTokenSource();cts.Cancel();
                try{cpu.InvokeAsync(execution,new(batch,request),input,cts.Token).GetAwaiter().GetResult();throw new Exception("CPU cancellation missing");}
                catch(OperationCanceledException){}
                cpu.DisposeAsync(execution).GetAwaiter().GetResult();execution.DisposeAsync().GetAwaiter().GetResult();
            }
            string shader=name+".glsl";File.WriteAllText(Path.Combine(dir,shader),gpu.ShaderSource());
            foreach(ulong start in new[]{0UL,1564445371644820290UL})
            {
                int size=Math.Max(67,Math.Min(count,1025));
                var expected=new List<uint>();
                for(uint i=0;i<size;i++)
                {
                    string seed=Beta110SeedCodec.FormatOrdinal(start+i);ulong root=Beta111Profile.Instance.ComputeRootSeed(seed);
                    var core=Beta109WorldPredictionProvider.Predict(seed,10,0,authority.Character.CharacterKey,world,AncientOptionConditionProfile.BroadDefault);
                    bool Matches(IEnumerable<ModelKey> values,ModelKeySetFilter p){var v=values.ToArray();return (p.Any.Count==0||p.Any.Any(v.Contains))&&p.All.All(v.Contains)&&!p.Ban.Any(v.Contains);}
                    bool truth=q.LegacyWorld.BossFilters.All(p=>Matches(core.Bosses.Where(b=>b.Act==p.Act).Select(b=>b.BossKey),p.Keys))&&
                        q.LegacyWorld.BossOrdinalFilters.All(p=>Matches(core.Bosses.Where(b=>b.Act==p.Act&&b.Ordinal==p.Ordinal).Select(b=>b.BossKey),p.Keys))&&
                        q.LegacyWorld.AncientIdentityFilters.All(p=>Matches(core.Ancients.Where(b=>b.Act==p.Act).Select(b=>b.AncientKey),p.Keys));
                    foreach(var p in q.EventSequenceConstraints){var entries=core.EventPoolSequencePrediction!.Acts.Single(a=>a.Act==p.Act).Entries.Where(e=>(p.RangeMode==SearchSequenceRangeMode.FirstN?e.Ordinal<=p.RangeValue:e.Ordinal==p.RangeValue)&&(!p.Source.HasValue||p.Source==e.Source)).ToArray();truth&=(p.RangeMode!=SearchSequenceRangeMode.ExactSlot||entries.Length>0)&&Matches(entries.Select(e=>e.EventKey),p.Keys);}
                    var projected=Beta109WorldSnapshotProjector.ProjectForSeed(g,seed);
                    foreach(var group in q.VariantBossBranches.GroupBy(b=>b.Act))truth&=!group.Any(b=>projected.SelectedActs[b.Act-1]==b.VariantKey)||group.Any(b=>projected.SelectedActs[b.Act-1]==b.VariantKey&&
                        Matches(core.Bosses.Where(p=>p.Act==b.Act&&p.Ordinal==1).Select(p=>p.BossKey),b.FirstBoss)&&
                        Matches(core.Bosses.Where(p=>p.Act==b.Act&&p.Ordinal==2).Select(p=>p.BossKey),b.SecondBoss));
                    bool actual=replay.Matches(root);if(truth!=actual)throw new Exception($"W CPU/Core parity {name} {seed} core={truth} W={actual}");
                    if(actual)expected.Add(i);checks++;
                }
                foreach(bool compact in new[]{false,true})
                {
                    var ordinals=Enumerable.Range(0,size).Where(i=>!compact||i%3==0).Select(i=>(uint)i).ToArray();
                    fixtures.Add(new{name,shader,buffers=gpu.Buffers,scratchWords=gpu.ScratchWords,
                        batchMeta=new[]{(uint)start,(uint)(start>>32),(uint)size,(uint)ordinals.Length,compact?1u:0u,(uint)size,0u,0u},
                        ordinals,expected=expected.Where(i=>!compact||i%3==0).ToArray()});
                }
            }
        }
        File.WriteAllText(Path.Combine(dir,"fixtures.json"),System.Text.Json.JsonSerializer.Serialize(fixtures));
        File.WriteAllText(Path.Combine(dir,"project.godot"),"config_version=5\n[application]\nconfig/name=\"W Family GPU parity\"\n[rendering]\nrenderer/rendering_method=\"mobile\"\n");
        File.Copy("tests/functional/Rt2FunctionalChecks/WorldGpuSanity.gd",Path.Combine(dir,"check.gd"),true);
        Console.WriteLine($"W_CPU_CORE_PASS checks={checks}; GPU fixtures={fixtures.Count}");return 0;
    }
    private static void CheckSharedAncientSlices()
    {
        int checks = 0;
        for (int poolSize = 0; poolSize <= 16; poolSize++)
        for (int acts = 1; acts <= 5; acts++)
        for (ulong root = 0; root < 128; root++)
        {
            var original = Enumerable.Range(0, poolSize).Select(i => (ushort)i).ToArray();
            var frozen = original.ToArray();
            var oldRng = new Beta110FastRng(root); oldRng.UnstableShuffle(original.AsSpan());
            var slicedRng = new Beta110FastRng(root); slicedRng.UnstableShuffle(frozen.AsSpan());
            if (!original.AsSpan().SequenceEqual(frozen) || oldRng.CaptureCheckpoint() != slicedRng.CaptureCheckpoint())
                throw new Exception($"W shared Ancient initial shuffle parity pool={poolSize};acts={acts};root={root}");
            int[] offsets = new int[acts], counts = new int[acts];
            Beta110WorldFastStage.AssignSharedAncients(poolSize, offsets, counts, ref slicedRng);
            int remaining = poolSize;
            for (int act = 1; act < acts; act++)
            {
                int count = oldRng.NextInt(remaining + 1);
                if (count != counts[act] || !original.AsSpan(0, count).SequenceEqual(frozen.AsSpan(offsets[act], count)))
                    throw new Exception($"W shared Ancient slice allocation parity pool={poolSize};acts={acts};root={root};act={act};expectedCount={count};actualCount={counts[act]};offset={offsets[act]}");
                original.AsSpan(count, remaining - count).CopyTo(original);
                remaining -= count;
            }
            if (oldRng.CaptureCheckpoint() != slicedRng.CaptureCheckpoint() || oldRng.NextDouble() != slicedRng.NextDouble())
                throw new Exception("W shared Ancient continuation checkpoint parity");
            checks++;
        }
        Console.WriteLine($"W_SHARED_ANCIENT_SLICES_PASS checks={checks}");
    }

    private static void CheckScalarBossBranches()
    {
        WorldFastDenseSetPredicate[] predicates = [new([], [], [], false), new([1, 2], [], [], false),
            new([], [1, 1], [], false), new([], [1, 2], [], false), new([], [], [2], false), new([], [], [], true)];
        bool Old(ushort value, WorldFastDenseSetPredicate p) => !p.AlwaysReject &&
            (p.Any.Length == 0 || p.Any.Contains(value)) && p.All.All(t => t == value) && !p.Ban.Contains(value);
        var empty = new WorldFastActPlan(3, 0, 0, 0, 0, [], 0, [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);
        int checks = 0;
        foreach (var first in predicates)
        foreach (var second in predicates)
        foreach (bool conjunction in new[] { false, true })
        {
            // The crossed alternative specifically prevents stitching the first
            // Boss of one branch to the second Boss of another branch.
            var plan = empty with { FamilyBossBranches = [new(first, second), new(second, first)], FamilyBossBranchesConjunctive = conjunction };
            if (plan.HasBossPredicate != plan.FamilyBossBranches.Any(b => !b.First.IsEmpty || b.First.AlwaysReject || !b.Second.IsEmpty || b.Second.AlwaysReject))
                throw new Exception("W cached Boss requirement");
            for (ushort one = 0; one < 4; one++)
            foreach (ushort? two in new ushort?[] { null, 0, 1, 2, 3 })
            {
                bool Row(WorldFamilyBossBranch b) => Old(one, b.First) && (!two.HasValue || Old(two.Value, b.Second));
                bool expected = conjunction ? plan.FamilyBossBranches.All(Row) : plan.FamilyBossBranches.Any(Row);
                if (Beta110WorldFastStage.MatchesBossBranches(plan, one, two) != expected) throw new Exception("W scalar Boss branch parity");
                checks++;
            }
        }
        Console.WriteLine($"W_SCALAR_BOSS_PASS checks={checks}");
    }
}
