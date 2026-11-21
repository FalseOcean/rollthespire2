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
        CheckScopedWorldAuthority();
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
        // Exercise the actual W GPU/CPU transport with both affected inputs, including a
        // zero-draw Act 1 and Modern113 shuffles whose event identities cannot be inspected.
        Add("FreshModern113Pael", empty with { AncientIdentityFilters = [new(2, Any(g.OrderedActCatalog[1].OrderedAncients[0]))] });
        foreach(var (name,q) in cases)
        {
            if (name == "FreshModern113Pael")
            {
                g = g with
                {
                    RngSemantics = RngSemanticProfileId.Modern113,
                    SharedEventCatalogExact = false, EventAuthority = g.EventAuthority with { SharedRawOrderExact = false },
                    OrderedActCatalog = g.OrderedActCatalog.Select(a => a with
                    {
                        OrderedAncients = a.Act == 1 ? [] : a.OrderedAncients,
                        OrderedEligibleEvents = [], EligibleEventOrderExact = false
                    }).ToArray()
                };
                world = world with { Beta109Generation = g };
                authority = authority.WithWorldAuthority(world);
                context = context with { Authority = authority };
            }
            var compiled=SearchCompiler.Compile(q,context);
            var request=new PreparedSearch(new SearchModel(compiled, CompiledSearchEvaluationProjector.Project(compiled).Evaluation, CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint), new("000000000000",10000,30,1), "000000000000", 10000, compiled.SemanticFingerprint);
            if (!WorldFamilyReplay.HasConditions(request.Model))
            {
                bool created = VariantOpeningFamily.TryCreate(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var variant, false);
                if (WorldFamily.TryCreate(request.Model, FilterExecutionPreferences.From(request.RunOptions), out _) ||
                    created != VariantOpeningPreparation.Prepare(compiled).HasConditions ||
                    created && variant?.FamilyId != VariantOpeningFamily.Id)
                    throw new Exception("Pure variants migrate to V without an empty W");
                checks++;
                continue; // V numerical oracle/transport is checked by its focused entry.
            }
            var replay=new WorldFamilyReplay(request.Model);var gpu=new WorldFamilyGpuPlan(replay, specializeSolo: true);
            if (name is "Boss1" or "Mixed") checks += CheckModernEventScratch(request.Model, replay);
            if (name == "Boss1") checks += CheckEffectiveEventPair(request.Model, replay);
            if(!WorldFamily.TryCreate(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var family)||family!.Coverage.Single()!="W.World")throw new Exception("W registration");
            var executionPlan=FamilyExecutionCoordinator.Plan(request);
            if(!executionPlan.ExecutionOrderUnits.Any(f=>f.FamilyId=="W.World") || executionPlan.ExecutionOrderUnits.Any(f=>f.FamilyId is not ("W.World" or "V.VariantOpening")))throw new Exception("Production residual W/V registration");
            if (gpu.GroupingK != 4 || !gpu.Revision(false).Contains(".Dense.K4.") || !gpu.Revision(true).Contains(".CompactAbi1.K4."))
                throw new Exception("W Production physical default/identity");
            if(name=="Boss1")
            {
                var observations = new FilterCostObserver();
                int capacity = gpu.Capacity * 4;
                var batch = new SearchBatch(0, capacity);
                var fullCompact = FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, capacity).Select(ordinal => (ulong)ordinal).ToArray());
                void Observe(bool compact, int population)
                {
                    var input = compact ? population == capacity ? fullCompact : FamilyCandidateSet.FromSortedAbi1(batch, [0]) :
                        FamilyCandidateSet.Dense(new SearchBatch(0, population));
                    observations.ObserveCompleted(family, input, 0, 1, 1, 1, 32);
                }
                Observe(false, capacity);
                if(observations.Capture(family, false).CalibrationEligible)throw new Exception("Cold evidence accepted");
                Observe(false,1);
                if(observations.Capture(family, false).CalibrationEligible)throw new Exception("Underfill evidence accepted");
                Observe(false,capacity);
                if(!observations.Capture(family, false).CalibrationEligible)throw new Exception("Full-group evidence missing");
                if(observations.Capture(family, true).PhysicalBatchCount!=0)throw new Exception("Dense evidence leaked into Compact");
                Observe(true,1);Observe(true,1);
                if(observations.Capture(family, true).CalibrationEligible||observations.Live(family, true) is null)
                    throw new Exception("Compact underfill must remain live-only");
                Observe(true,capacity);
                if(!observations.Capture(family, true).CalibrationEligible)throw new Exception("Compact comparable evidence missing");
                observations.Invalidate(family);
                if(observations.Capture(family, true).CalibrationEligible||observations.Live(family, true) is not null)
                    throw new Exception("Faulted evidence accepted");
                var mixedQuery=SearchCompiler.Compile(q with {OpeningRoute=new(new ModelKey("RELIC","ARCANE_SCROLL"))},context);
                var mixedRequest=new PreparedSearch(new SearchModel(mixedQuery, CompiledSearchEvaluationProjector.Project(mixedQuery).Evaluation, CombatRewardRoutePolicyContract.None, mixedQuery.SemanticFingerprint), request.RunOptions, "000000000000", 10000, mixedQuery.SemanticFingerprint);
                var mixed=FamilyExecutionCoordinator.Plan(mixedRequest);
                if(!mixed.ExecutionOrderUnits.Any(f=>f.FamilyId=="N.Neow")||!mixed.ExecutionOrderUnits.Any(f=>f.FamilyId=="W.World")||
                    FamilyExecutionCoordinator.ResolveExecutionWindowSize(mixedRequest,mixed.OrderedFamilies)!=
                        (mixed.OrderedFamilies.Any(f=>f.ConditionPerformance.UsesGpu) ? 1<<24 : 65536))
                    throw new Exception("N/W registration or selected CPU/GPU root geometry");
            }
            if(name=="OversizedFirstN")
            {
                // Current bounded-pool contract: FirstN beyond the pool observes
                // the entire pool. The frozen pre-optimization binary also uses GPU.
                if(!family.ConditionPerformance.UsesGpu)throw new Exception("Oversized FirstN remains bounded-pool GPU work");
                var cpu=family.CpuAlternatives.Single(body => body.CpuWorkers == request.WorkerCount);
                var execution=new FilterRuntimeServices(false);var batch=new SearchBatch(0,67);
                var running=cpu.CreateExecution!(cpu,execution);
                var input=FamilyCandidateSet.FromAbi1(batch,[0,3,9,37,66]);
                var actual=running.FilterAsync(execution,input,default).GetAwaiter().GetResult();
                var expected=input.EnumerateLogicalOrdinals().Where(i=>replay.Matches(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i))));
                if(!actual.EnumerateLogicalOrdinals().SequenceEqual(expected))throw new Exception("CPU physical subset");
                using var cts=new CancellationTokenSource();cts.Cancel();
                try{running.FilterAsync(execution,input,cts.Token).GetAwaiter().GetResult();throw new Exception("CPU cancellation missing");}
                catch(OperationCanceledException){}
                running.DisposeAsync(execution).GetAwaiter().GetResult();execution.DisposeAsync().GetAwaiter().GetResult();
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
    private static void CheckScopedWorldAuthority()
    {
        var fixture = Beta109WorldFixtureFactory.Create();
        var source = fixture.Beta109Generation!;
        var generation = source with
        {
            Profile = RuntimeProfileId.Beta111, RngSemantics = RngSemanticProfileId.Modern113,
            WorldGenerationHooksExact = true,
            FixtureValidationStatus = RolltheSpire2.Core.World.Snapshots.Beta109FixtureValidationStatus.VerifiedRealGame,
            VerifiedRealGameFixtureIds = ["ancient-identity:synthetic", "ancient-option:PAEL:synthetic", "boss:synthetic"],
            OrderedActCatalog = source.OrderedActCatalog.Select(a => a.Act == 1 ? a with { OrderedAncients = [] } : a).ToArray()
        };
        const string roomHook = "RoomGenerationMethodPatched:RunManager.GenerateRooms";
        const string startHook = "RelicInitializationMethodPatched:RunManager.InitializeNewRun";
        foreach (var rules in new[] { RngSemanticProfileId.Modern110, RngSemanticProfileId.Modern113 })
        foreach (bool beforeBags in new[] { false, true })
        {
            var captured = fixture with
            {
                CapturedProfileId = RuntimeProfileId.Beta111,
                Beta109Generation = generation with
                {
                    RngSemantics = rules,
                    WorldGenerationHooksExact = false,
                    RoomReplayAssumption = beforeBags ? string.Empty : roomHook,
                    InitialRelicReplayAssumption = beforeBags ? startHook : string.Empty,
                    RelicInitializationExact = !beforeBags,
                    UpFrontPrefix = beforeBags
                        ? RolltheSpire2.Core.World.Snapshots.Beta109UpFrontPrefixSnapshot.Missing("MissingRelicInitialization")
                        : generation.UpFrontPrefix
                }
            };
            if (!WorldReplayAssumptions.TryCreate(captured, out var modeled, out var issue) ||
                issue != (beforeBags ? startHook : roomHook) ||
                modeled.Beta109Generation!.InitialRelicReplayAssumption != (beforeBags ? startHook : string.Empty) ||
                captured.Beta109Generation.WorldGenerationHooksExact)
                throw new Exception("Conditional world replay lost the original hook or modified strict inputs");
            WorldPredictionResult Project(RolltheSpire2.Core.World.Snapshots.WorldAuthoritySnapshot world) =>
                Beta109WorldPredictionProvider.PredictFromRootHash(0, "root-hash:0", 10, 0,
                    source.CharacterKey, world, AncientOptionConditionProfile.BroadDefault);
            if (Project(captured).Ancients.Count != 0 || Project(modeled).Ancients.Count != 3)
                throw new Exception("Normal room replay did not distinguish observed and conditional identities");
            var missingRows = captured with { Beta109Generation = captured.Beta109Generation with
                { UnlockFactsExact = false } };
            if (!WorldReplayAssumptions.TryCreate(missingRows, out var missingModel, out _) ||
                Project(missingModel).Ancients.Count != 0)
                throw new Exception("A hook assumption supplied missing unlock data");
        }
        foreach (var rules in new[] { RngSemanticProfileId.Modern110, RngSemanticProfileId.Modern113 })
        {
            var hooked = fixture with
            {
                CapturedProfileId = RuntimeProfileId.Beta111,
                Beta109Generation = generation with
                {
                    RngSemantics = rules,
                    EventAuthority = generation.EventAuthority with { LockedEventsAfterShuffle = null },
                    AncientEventContexts = generation.AncientEventContexts.Select(c => c.AncientKey.Entry == "PAEL" ? c with
                    {
                        HookDecision = RolltheSpire2.Core.World.Snapshots.Beta109HookDecision.Unknown,
                        EventIsSharedAuthorityExact = true, OptionReplayAssumption = "StandardAncientOptionGenerator:PAEL"
                    } : c).ToArray()
                }
            };
            if (!WorldReplayAssumptions.TryCreate(hooked, out var normal, out string reason))
                throw new Exception("Post-shuffle filter and registered option rules had no conditional model");
            WorldPredictionResult ReplayLeaves(RolltheSpire2.Core.World.Snapshots.WorldAuthoritySnapshot world) =>
                Beta109WorldPredictionProvider.PredictFromRootHash(0, "root-hash:0", 10, 0, source.CharacterKey, world,
                    AncientOptionConditionProfile.BroadDefault);
            var missingOptions = hooked with { Beta109Generation = hooked.Beta109Generation with
            {
                AncientEventContexts = hooked.Beta109Generation.AncientEventContexts.Select(c => c.AncientKey.Entry == "PAEL"
                    ? c with { Catalog = null } : c).ToArray()
            } };
            if (!WorldReplayAssumptions.TryCreate(missingOptions, out var queueOnly, out string queueReason) ||
                queueReason != "NoAdditionalEventRemovals" ||
                ReplayLeaves(queueOnly).Ancients.Single(a => a.Act == 2).Options.Count != 0)
                throw new Exception("A post-shuffle event assumption supplied a missing option catalog");
            var strict = ReplayLeaves(hooked);
            var estimated = ReplayLeaves(normal);
            var expected = ReplayLeaves(fixture with { Beta109Generation = generation with { RngSemantics = rules } });
            SeedPredictionFacts Facts(WorldPredictionResult w) => new() { World = w, PredictorId = "test",
                OriginalSeed = "0", CanonicalSeed = "0", Context = null! };
            var combined = SeedAnalysisModelResults.Combine(Facts(strict), Facts(estimated),
                new Dictionary<SeedPredictionDomainSelection, string> { [SeedPredictionDomainSelection.World] = reason });
            var merged = combined.World!;
            var paelRow = merged.Ancients.Single(a => a.Act == 2);
            if (strict.EventPoolSequencePrediction?.Acts.Count != 0 || strict.Ancients.Single(a => a.Act == 2).Options.Count != 0 ||
                combined.ModelAssumptions.Count != 0 || merged.EventSequenceReplayAssumption != "NoAdditionalEventRemovals" ||
                merged.EventPoolSequencePrediction?.RawEventOrderPrecision != PredictionPrecision.Partial ||
                !merged.EventPoolSequencePrediction.Acts.SelectMany(a => a.RawEntries.Select(e => (a.Act, e.EventKey)))
                    .SequenceEqual(expected.EventPoolSequencePrediction!.Acts.SelectMany(a => a.RawEntries.Select(e => (a.Act, e.EventKey)))) ||
                !merged.Bosses.SequenceEqual(strict.Bosses) || !merged.EncounterSequences.SequenceEqual(strict.EncounterSequences) ||
                paelRow.IdentityPrecision != PredictionPrecision.Exact || paelRow.OptionReplayAssumption != "StandardAncientOptionGenerator:PAEL" ||
                paelRow.Options.Count == 0 || paelRow.Options.Any(o => o.OptionPrecision == PredictionPrecision.Exact) ||
                !paelRow.Options.Select(o => o.OptionKey).SequenceEqual(expected.Ancients.Single(a => a.Act == 2).Options.Select(o => o.OptionKey)))
                throw new Exception($"Conditional world {rules}: strict events={strict.EventPoolSequencePrediction?.Acts.Count}, " +
                    $"strict options={strict.Ancients.Single(a => a.Act == 2).Options.Count}, domains={combined.ModelAssumptions.Count}, " +
                    $"events={merged.EventSequenceReplayAssumption}/{merged.EventPoolSequencePrediction?.Precision}, " +
                    $"bosses={merged.Bosses.SequenceEqual(strict.Bosses)}, encounters={merged.EncounterSequences.SequenceEqual(strict.EncounterSequences)}, " +
                    $"options={paelRow.AncientKey}/{paelRow.IdentityPrecision}/{paelRow.OptionReplayAssumption}/{paelRow.Options.Count}, " +
                    $"strict option issue={strict.Ancients.Single(a => a.Act == 2).OptionIssueCode}, estimated option issue={estimated.Ancients.Single(a => a.Act == 2).OptionIssueCode}, " +
                    $"expected={string.Join(',', expected.Ancients.Single(a => a.Act == 2).Options.Select(o => o.OptionKey))}");
        }
        var missingEvents = generation with
        {
            SharedEventCatalogExact = false,
            EventAuthority = generation.EventAuthority with { SharedRawOrderExact = false },
            OrderedActCatalog = generation.OrderedActCatalog.Select(a => a with
                { OrderedEligibleEvents = [], EligibleEventOrderExact = false }).ToArray()
        };
        var lateMissing = missingEvents with
        {
            OrderedActCatalog = missingEvents.OrderedActCatalog.Select(a => a.Act == 3
                ? a with { EncounterTagComparerExact = false } : a).ToArray()
        };
        var pael = generation.AncientEventContexts.Single(c => c.Act == 2 && c.AncientKey.Entry == "PAEL");
        var partialCatalog = pael.Catalog! with
        {
            Pools = pael.Catalog!.Pools.Select(p => p.PoolId == "pael.pool3.no-event-pet"
                ? p with { FilterResultExact = false } : p).ToArray()
        };
        var partial = lateMissing with { AncientEventContexts = [pael with { Catalog = partialCatalog }] };
        var emptyThird = pael.Catalog! with
        {
            Pools = pael.Catalog!.Pools.Select(p => p.PoolId.StartsWith("pael.pool3.", StringComparison.Ordinal)
                ? p with { OrderedOptions = [] } : p).ToArray()
        };
        var refill = generation with { AncientEventContexts = [pael with { Catalog = emptyThird }] };
        var countOnly = lateMissing with
        {
            InitialRelicShuffleCount = generation.SharedRelicBuckets.Count + generation.PlayerRelicBuckets.Count,
            RelicInitializationExact = false, SharedRelicPoolOrderExact = false, PlayerRelicPoolCompositionExact = false,
            PlayerRelicBuckets = [], SharedRelicBuckets = []
        };
        var moved = new ModelKey("EVENT", "MOVED_OR_NEW_ANCIENT");
        var picker = RolltheSpire2.Ui.Pages.Search.Ancient.AncientSearchUiCatalog.FromAuthority(RuntimeProfileId.Beta111,
            source.CharacterKey, fixture with { Beta109Generation = generation with { OrderedActCatalog =
                generation.OrderedActCatalog.Select(a => a.Act == 2 ? a with { OrderedAncients = [moved] } : a).ToArray() } });
        if (picker.Sections.Single(section => section.Act == 2).Rows.Single() is not
            { IdentityAvailable: true, Options.Count: 0 } row || row.AncientKey != moved)
            throw new Exception("Runtime Ancient identity unavailable without a modeled option generator");

        for (ulong root = 0; root < 16; root++)
        {
            WorldPredictionResult Replay(RolltheSpire2.Core.World.Snapshots.Beta109WorldGenerationSnapshot snapshot) =>
                Beta109WorldPredictionProvider.PredictFromRootHash(root, "root-hash:" + root, 10, 0, source.CharacterKey,
                    fixture with { CapturedProfileId = RuntimeProfileId.Beta111, Beta109Generation = snapshot },
                    AncientOptionConditionProfile.BroadDefault);
            var baseline = Replay(generation).Ancients.Single(a => a.Act == 2);
            foreach (var variant in new[] { missingEvents, lateMissing, countOnly, partial })
            {
                var observed = Replay(variant);
                var actual = observed.Ancients.Single(a => a.Act == 2);
                if (actual.IdentityPrecision != PredictionPrecision.Exact || actual.AncientKey != baseline.AncientKey ||
                    actual.IdentityRngCallCount != baseline.IdentityRngCallCount ||
                    !actual.Options.Take(2).Select(o => (o.OptionKey, o.OptionPrecision))
                        .SequenceEqual(baseline.Options.Take(2).Select(o => (o.OptionKey, PredictionPrecision.Exact))))
                    throw new Exception("Act2 Pael identity, option prefix or parent cursor changed with independent missing observations");
                if (variant == lateMissing && observed.Ancients.Any(a => a.Act == 3))
                    throw new Exception("Act3 encounter gap acquired downstream identity authority");
                if (variant == partial && actual.Options[2].OptionPrecision == PredictionPrecision.Exact)
                    throw new Exception("Unknown pet-pool eligibility confirmed the third option");
            }
            var filled = Replay(refill).Ancients.Single(a => a.Act == 2);
            if (!filled.Options.Take(2).Select(o => o.OptionKey).SequenceEqual(baseline.Options.Take(2).Select(o => o.OptionKey)) ||
                filled.Options.Count != 3 || filled.Options.Any(o => o.OptionPrecision != PredictionPrecision.Exact) ||
                filled.OptionRngTrace.Last().SourceStage != "pael:empty-pool-refill" ||
                !emptyThird.Pools.SelectMany(p => p.OrderedOptions).Contains(filled.Options[2].OptionKey))
                throw new Exception("Pael's empty third pool must refill after keeping the independent first two draws");
            var offers = Replay(partial).Ancients.Single(a => a.Act == 2);
            var growth = new ModelKey("RELIC", "PAELS_GROWTH");
            var disposition = QueryKeySetPredicate.MatchVisibleAncientOptions(offers.OptionsEvaluationStatus, offers.Options,
                new([growth], [], []));
            var expected = baseline.Options.Any(o => o.OptionKey == growth) ? SearchDisposition.Match : SearchDisposition.NoMatch;
            if (disposition != expected)
                throw new Exception("Unknown third-pool eligibility changed a decided second-pool condition");
            var legacy = generation with { RngSemantics = RngSemanticProfileId.Modern110 };
            var unfiltered = Replay(legacy);
            var unknown = Replay(legacy with { EventAuthority = legacy.EventAuthority with { LockedEventsAfterShuffle = null } });
            if (unknown.EventPoolSequencePrediction is not { Precision: PredictionPrecision.Unknown,
                    IssueCode: "UnknownGeneratedRoomEventFilter", Acts.Count: 0 } ||
                !unfiltered.EncounterSequences.Select(a => a.Act).SequenceEqual([1, 2, 3]) ||
                !unknown.EncounterSequences.Select(a => a.Act).SequenceEqual([1, 2, 3]) ||
                !unknown.Ancients.Any(a => a.Act == 3 && a.IdentityPrecision == PredictionPrecision.Exact) ||
                !unknown.Ancients.Select(a => (a.Act, a.AncientKey, a.IdentityPrecision, a.IdentityRngCallCount))
                    .SequenceEqual(unfiltered.Ancients.Select(a => (a.Act, a.AncientKey, a.IdentityPrecision, a.IdentityRngCallCount))) ||
                !unknown.Bosses.Select(b => (b.Act, b.BossKey, b.IdentityPrecision, b.RngCallCount))
                    .SequenceEqual(unfiltered.Bosses.Select(b => (b.Act, b.BossKey, b.IdentityPrecision, b.RngCallCount))) ||
                unknown.EncounterSequences.Zip(unfiltered.EncounterSequences).Any(pair =>
                    !pair.First.Normal.SequenceEqual(pair.Second.Normal) || !pair.First.Elite.SequenceEqual(pair.Second.Elite)))
                throw new Exception("Unknown post-shuffle event removals changed the legacy world RNG or claimed an event queue");
            var locked = legacy.EventAuthority.OrderedSharedEventsRaw[0];
            var filtered = Replay(legacy with { EventAuthority = legacy.EventAuthority with { LockedEventsAfterShuffle = [locked] } });
            foreach (var before in unfiltered.EventPoolSequencePrediction!.Acts)
            {
                var after = filtered.EventPoolSequencePrediction!.Acts.Single(a => a.Act == before.Act);
                var keys = before.RawEntries.Select(e => e.EventKey).ToArray();
                if (!keys.Contains(locked) || keys.All(k => k == locked) ||
                    !after.RawEntries.Select(e => e.EventKey).SequenceEqual(keys.Where(k => k != locked)) ||
                    (before.RngCallCountBefore, before.RngCallCountAfter) != (after.RngCallCountBefore, after.RngCallCountAfter))
                    throw new Exception("A registered event was filtered before its legacy RNG shuffle");
            }
            if (Replay(missingEvents with { RngSemantics = RngSemanticProfileId.Modern110 }).Ancients.Count != 0)
                throw new Exception("Pre-113 parent cursor advanced with unavailable event count");
        }
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

    private static int CheckModernEventScratch(SearchModel model, WorldFamilyReplay replay)
    {
        var acts = replay.Plan.Acts.Select(act => act.HasEventPredicate ? act : act with
        {
            EventCandidates = Enumerable.Repeat(act.EventCandidates, 8).SelectMany(entries => entries).ToArray()
        }).ToArray();
        var modern = new WorldFamilyReplay(model, replay.Plan with { Acts = acts, RngSemantics = RngSemanticProfileId.Modern113 });
        var plan = new WorldFamilyGpuPlan(modern, specializeSolo: true, encounterRankMask: false);
        int observedEventStride = replay.Plan.Acts.Where(act => act.HasEventPredicate)
            .Select(act => act.EventCandidates.Length).DefaultIfEmpty(0).Max();
        uint encounterStride = new WorldFamilyGpuPlan(replay, encounterRankMask: false).Buffers[2][8];
        bool trimsEvents = acts.Max(act => act.EventCandidates.Length) > observedEventStride;
        if (plan.Buffers[2][8] != encounterStride || plan.Buffers[2][13] != (uint)observedEventStride ||
            plan.ScratchWords != encounterStride + observedEventStride ||
            plan.Revision(false).Contains(".ObservedEventScratch.", StringComparison.Ordinal) != trimsEvents)
            throw new Exception($"W Modern113 scratch retains encounters and only observed raw event pools: encounter={plan.Buffers[2][8]}/{encounterStride};events={plan.Buffers[2][13]}/{observedEventStride};words={plan.ScratchWords};revision={plan.Revision(false)}");
        var selected = new WorldFamilyGpuPlan(modern, specializeSolo: true);
        bool rankMask = modern.Plan.MaxEncounterPoolCount is > 0 and <= 32 &&
            !modern.Plan.Acts.Any(act => act.HasEncounterPredicate);
        if (selected.UsesEncounterRankMask != rankMask ||
            selected.Buffers[2][8] != (rankMask ? 0u : encounterStride) ||
            selected.ScratchWords != (rankMask ? 0 : encounterStride) + observedEventStride)
            throw new Exception("W bounded unobserved encounter mask replaces only encounter scratch");
        return 2;
    }

    private static int CheckEffectiveEventPair(SearchModel model, WorldFamilyReplay replay)
    {
        var first = new WorldFastEventPredicate(0, 1, byte.MaxValue, new([11], [], [], false));
        var second = new WorldFastEventPredicate(0, 2, byte.MaxValue, new([], [12], [], false));
        WorldFastEventCandidate[] pool = [new(12, 0, false), new(11, 0, true), new(13, 0, true), new(12, 1, true)];
        var sample = replay.Plan.Acts[0] with { EventCandidates = pool, EventPredicates = [second, first] };
        int checks = 0;
        void Check(WorldFastActPlan act, bool enabled, bool tagged, uint[]? expected = null)
        {
            var modern = new WorldFamilyReplay(model, replay.Plan with { Acts = [act], RngSemantics = RngSemanticProfileId.Modern113 });
            var gpu = new WorldFamilyGpuPlan(modern, trimEventScratch: true, effectiveEventPair: enabled);
            uint offset = gpu.Buffers[13][1];
            if (((offset & 0x80000000u) != 0) != tagged ||
                gpu.Buffers[2][13] != (tagged ? 0u : (uint)act.EventCandidates.Length) ||
                gpu.Revision(false).Contains(".EffectiveEventPair.", StringComparison.Ordinal) != tagged ||
                (expected != null && !gpu.Buffers[14].AsSpan((int)(offset & 0x7fffffffu), expected.Length).SequenceEqual(expected)))
                throw new Exception("W effective pair admission, descriptor or event scratch");
            checks++;
        }
        Check(sample, true, true, [14, 1, 3]);
        Check(sample, false, false);
        Check(sample with { EventCandidates = [.. pool, new(11, 1, true)] }, true, false);
        Check(sample with { EventPredicates = [first with { SourceFilter = 0 }, second] }, true, false);
        Check(sample with { EventPredicates = [first, second with { RangeMode = 1 }] }, true, false);
        Check(sample with { EventCandidates = [.. pool[..3]] }, true, true, [6, 1, uint.MaxValue]);
        return checks;
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
