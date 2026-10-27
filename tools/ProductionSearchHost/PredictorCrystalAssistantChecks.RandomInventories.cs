using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private const string RandomInventoryOrder="FRESNEL_LENS+WING_CHARM+DINGY_RUG+GLITTER";
    private sealed record RandomInventoryFixture(string Seed,PredictorCrystalSnapshot Source,
        ImmutableArray<PredictorCrystalStep> Path,ImmutableArray<CrystalRewardOption> Goals,
        ImmutableArray<RewardOp> WitnessActions);
    private sealed record RandomInventoryFocus(string Seed,bool Exact,int SelectionCount,CrystalRewardOption? Target,string Construction);
    private sealed record RandomInventoryCorpus(int Schema,int SeedRandom,string Alphabet,string SourceHead,
        string OrderedRelics,ImmutableArray<string> Seeds,ImmutableArray<RandomInventoryFixture> Fixtures,
        ImmutableArray<RandomInventoryFocus> FocusTargets)
    {
        public ImmutableArray<RandomInventoryFocus> FocusPolicyHistory { get; init; } = [];
    }

    // The saved input corpus owns seed/path/goal/action order across A/B runs.
    // Geometry comes from the native generator; ordered inventory is injected.
    // Search cutoffs remain pending, while native checks establish positives.
    private static int CheckRandomRelicInventories(string output,Func<string,string,PredictorCrystalSnapshot> create,
        Action<PredictorCrystalSnapshot,ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode,string,bool> native)
    {
        output=Path.GetFullPath(Environment.GetEnvironmentVariable("RT2_CRYSTAL_RANDOM_OUTPUT_DIR")??output);
        Directory.CreateDirectory(output);
        string corpusPath=Path.GetFullPath(Environment.GetEnvironmentVariable("RT2_CRYSTAL_RANDOM_CORPUS_PATH")??
            Path.Combine(output,"random-relic-corpus.json"));
        string receiptPath=Path.Combine(output,"random-relic-queries.json");
        string sourceHead=Environment.GetEnvironmentVariable("RT2_CRYSTAL_RANDOM_SOURCE_HEAD")??"Unspecified";
        bool profile=Environment.GetEnvironmentVariable("RT2_CRYSTAL_COMPILER_PROFILE")=="1";
        bool focusOnly=Environment.GetEnvironmentVariable("RT2_CRYSTAL_RANDOM_FOCUS_ONLY")=="1";
        int seedCount=ReadCount("RT2_CRYSTAL_RANDOM_COUNT",profile?1:focusOnly?3:10,1,profile?2:focusOnly?3:10);
        int coldCount=focusOnly?0:ReadCount("RT2_CRYSTAL_RANDOM_COLD_COUNT",Math.Min(3,seedCount),0,seedCount);
        int focusCount=focusOnly?seedCount:ReadCount("RT2_CRYSTAL_RANDOM_FOCUS_COUNT",Math.Min(3,seedCount),0,seedCount);
        int questionMs=ReadCount("RT2_CRYSTAL_RANDOM_QUERY_MS",2000,1,10000);
        var roundWatch=Stopwatch.StartNew();using var round=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var receipts=new List<object>();var preparations=new List<object>();
        int questions=0,nativeWitnesses=0,focusedNativeWitnesses=0;
        bool roundCutoff=false;string stage="corpus";string? currentSeed=null;
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
        var tableField=typeof(PredictorCrystalReachability).GetField("_table",fields)!;
        var queriesField=typeof(PredictorCrystalReachability).GetField("_languageQueries",fields)!;
        bool loaded=File.Exists(corpusPath);
        var corpus=loaded?ProductionSearchReplay.Read<RandomInventoryCorpus>(corpusPath):CreateCorpus();
        Check(corpus.Schema==1 && corpus.OrderedRelics=="DRIFTWOOD+"+RandomInventoryOrder && corpus.Seeds.Length==10 &&
            corpus.Seeds.Distinct().Count()==10,"Random corpus retains declared inventory and ten ordered unique seeds");
        Check(corpus.Fixtures.All(f=>corpus.Seeds.Contains(f.Seed)) && corpus.Fixtures.Select(f=>f.Seed).Distinct().Count()==corpus.Fixtures.Length &&
            corpus.FocusTargets.All(f=>corpus.Seeds.Contains(f.Seed) && f.SelectionCount is 1 or 2) &&
            corpus.FocusTargets.Select(f=>(f.Seed,f.Exact)).Distinct().Count()==corpus.FocusTargets.Length,
            "Random corpus fixture and focus records have unique declared seed/mode ownership");
        if(focusOnly)
            Check(loaded && corpus.Seeds.Take(seedCount).All(seed=>corpus.Fixtures.Any(f=>f.Seed==seed) &&
                new[]{false,true}.All(exact=>corpus.FocusTargets.Any(f=>f.Seed==seed && f.Exact==exact))),
                "Focus-only follow-up requires the existing saved fixtures and focus records for every requested seed/mode");
        SaveCorpus();WriteSummary();
        try
        {
            for(int index=0;index<seedCount;index++)
            {
                if(round.IsCancellationRequested) {roundCutoff=true;break;}
                string seed=corpus.Seeds[index];currentSeed=seed;stage="native-generation-and-fixture";
                var generation=Stopwatch.StartNew();var generated=create(seed,RandomInventoryOrder);
                double generationMs=generation.Elapsed.TotalMilliseconds;
                var preparation=Stopwatch.StartNew();
                var fixture=corpus.Fixtures.FirstOrDefault(f=>f.Seed==seed);
                bool fixtureLoaded=fixture!=null;
                if(fixture==null)
                {
                    // A construction cutoff is an error in this positive-fixture
                    // schedule, never an absence proof for the generated seed.
                    var path=RequiredItemPath(generated,i=>i.Kind.StartsWith("CARD_",StringComparison.Ordinal),200_000);
                    var rootForFixture=PredictorCrystalExplorer.Replay(generated,path);
                    Check(rootForFixture.Rewards.Count(r=>r.Kind==PredictorRewardKind.Card)==3,
                        "Random generated fixture has three card reward instances");
                    var witness=ExtremeCardWitness(PredictorCrystalRerolls.Prefixes(rootForFixture).ToArray());
                    var taken=PredictorCrystalRerolls.Take(witness.Node,witness.Goals);
                    Check(taken!=null && EnchantmentObtainedSupports(rootForFixture,taken.Run,witness.Goals),
                        "Random three-target fixture survives obtain hooks");
                    fixture=new(seed,generated,path,witness.Goals,Operations(rootForFixture,taken!).ToImmutableArray());
                    corpus=corpus with {Fixtures=corpus.Fixtures.Add(fixture)};SaveCorpus();
                }
                var source=fixture.Source;
                Check(generated.Items.SequenceEqual(source.Items) && generated.EventRng==source.EventRng &&
                    Niche(generated.State)==Niche(source.State) && generated.Context.Ascension==source.Context.Ascension &&
                    generated.Context.Character==source.Context.Character,"Native random generation reproduces saved fixture context and RNG");
                string frozen=JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions);
                var root=PredictorCrystalExplorer.Replay(source,fixture.Path);
                var prefixes=PredictorCrystalRerolls.Prefixes(root).ToArray();
                Check(prefixes.Length==16,"Random three-reward fixture retains sixteen ordered reroll subsets");
                var witnessNode=ReplayRandomOperations(root,fixture.WitnessActions);
                Check(EnchantmentObtainedSupports(root,witnessNode.Run,fixture.Goals),
                    "Saved typed random witness still obtains all three exact targets");
                double preparationMs=preparation.Elapsed.TotalMilliseconds;
                stage="native-offer-reroll-take-verification";
                var nativeTimer=Stopwatch.StartNew();native(source,fixture.Path,witnessNode,RandomInventoryOrder,true);
                double nativeMs=nativeTimer.Elapsed.TotalMilliseconds;nativeWitnesses++;
                preparations.Add(new {index,seed,fixtureLoaded,generationMs,preparationMs,nativeMs,
                    path=fixture.Path,witnessGoals=fixture.Goals,goalKeys=fixture.Goals.Select(g=>g.CacheKey).ToArray(),
                    witnessActions=fixture.WitnessActions,sourceEvent=source.EventRng,initialEvent=root.ExportCrystal().EventRng,
                    finalEvent=witnessNode.Run.ExportCrystal().EventRng,sourceNiche=Niche(source.State),initialNiche=Niche(root.Working),
                    finalNiche=Niche(witnessNode.Run.Working),maximumObservedNiche=prefixes.Max(p=>Niche(p.Run.Working).Calls),
                    sourceRelics=source.State.Relics,initialRelics=root.Working.Relics,finalRelics=witnessNode.Run.Working.Relics,
                    nativeVerified="Initial offers, open offers, reroll/Enter/Exit/Take lifecycle, acquired identity/level/enchantment/amount, event and Niche RNG",
                    scope="Native generated six-reveal geometry; controlled injected inventory; not a normal-play inventory acquisition claim"});
                WriteSummary();
                foreach(bool exact in new[]{false,true})
                {
                    if(round.IsCancellationRequested) {roundCutoff=true;break;}
                    if(index<coldCount)
                    {
                        stage="cold-no-selection";var cold=NewRandomSession(source,exact);
                        RunQuestion(cold,[],exact,"cold-no-selection",null,false);
                    }
                    if(round.IsCancellationRequested) {roundCutoff=true;break;}
                    var session=NewRandomSession(source,exact);
                    var seedTimer=Stopwatch.StartNew();
                    session.AddWitnesses([new(fixture.Path,root.Rewards) {EnchantmentTargets=exact,
                        RerollVariants=prefixes.Select(p=>new CrystalRerollVariant(p.Run.Rewards,p.Actions))
                            .OrderBy(p=>p.Actions.Length).ToImmutableArray()}]);
                    double routeSeedMs=seedTimer.Elapsed.TotalMilliseconds;
                    var targets=fixture.Goals.Select(g=>exact?g:g with {Enchantment=null}).ToImmutableArray();
                    Check(targets.Distinct().Count()==3,"Random witness retains three distinct identity/level targets in each mode");
                    RandomInventoryFocus? storedFocus=corpus.FocusTargets.FirstOrDefault(f=>f.Seed==seed && f.Exact==exact);
                    CrystalRewardOption? pendingAfterOne=null;
                    foreach(int count in focusOnly?new[]{1}:new[]{1,2,3})
                    {
                        if(round.IsCancellationRequested) {roundCutoff=true;break;}
                        stage="seeded-selection-"+count;
                        var last=RunQuestion(session,targets.Take(count).ToImmutableArray(),exact,"seeded-selection",null,true,routeSeedMs);
                        if(index<focusCount && count<=2 && storedFocus?.Target==null)
                        {
                            var selected=targets.Take(count).ToImmutableArray();
                            var excluded=last.ExcludedCandidates.ToHashSet();
                            var table=(CrystalReachabilityTable)tableField.GetValue(session)!;
                            var pending=table.Candidates.Where(c=>!selected.Contains(c) &&
                                !last.Projection.Available.ContainsKey(c) && !excluded.Contains(c))
                                .OrderBy(c=>c.CacheKey,StringComparer.Ordinal).ToArray();
                            if(count==1) pendingAfterOne=pending.FirstOrDefault();
                            if(count==2 || focusOnly)
                            {
                                var target=pending.FirstOrDefault()??pendingAfterOne;
                                int selectionCount=pending.Length>0?count:pendingAfterOne!=null?1:2;
                                var revised=new RandomInventoryFocus(seed,exact,selectionCount,target,target==null?
                                    "Policy2NoPendingAfterSelected1Or2":$"Policy2FirstSortedActualPendingAfterSelected{selectionCount}");
                                if(storedFocus==null) corpus=corpus with {FocusTargets=corpus.FocusTargets.Add(revised)};
                                else if(revised!=storedFocus) corpus=corpus with {FocusTargets=corpus.FocusTargets.SetItem(corpus.FocusTargets.IndexOf(storedFocus),revised),
                                    FocusPolicyHistory=corpus.FocusPolicyHistory.Add(storedFocus)};
                                storedFocus=revised;SaveCorpus();
                            }
                        }
                    }
                    if(index<focusCount && storedFocus!=null && !round.IsCancellationRequested)
                    {
                        stage="focused-pending-target";
                        var focusSelected=targets.Take(storedFocus.SelectionCount).ToImmutableArray();
                        if(storedFocus.Target is {} focusTarget)
                            RunQuestion(session,focusSelected,exact,"focused-pending-target",focusTarget,true,routeSeedMs,
                                validateFocusedNative:focusedNativeWitnesses==0);
                        else
                        {
                            receipts.Add(new {seed,exactEnchantments=exact,kind="focused-pending-target",queried=false,
                                reason=storedFocus.Construction,selectionCount=storedFocus.SelectionCount,
                                goalKeys=focusSelected.Select(g=>g.CacheKey).ToArray()});
                            ProductionSearchReplay.Write(receiptPath,receipts);
                        }
                    }
                }
                Check(frozen==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Random inventory survey preserves frozen source");
                WriteSummary();

                CrystalReachabilityUpdate RunQuestion(PredictorCrystalReachability session,ImmutableArray<CrystalRewardOption> selected,
                    bool exact,string kind,CrystalRewardOption? target,bool requireSelected,double routeSeedMs=0,bool validateFocusedNative=false)
                {
                    var costs=new ConcurrentQueue<ExtremeQueryCost>();
                    session.LanguageResultObserved=(goals,focus,result)=>costs.Enqueue(new(
                        goals.Select(g=>g.CacheKey).ToImmutableArray(),focus?.CacheKey,result));
                    var setupTimer=Stopwatch.StartNew();
                    if(target==null) session.Advance(selected,TimeSpan.Zero);
                    else session.AdvanceFocused(selected,target,TimeSpan.Zero);
                    double setupMs=setupTimer.Elapsed.TotalMilliseconds;
                    var table=(CrystalReachabilityTable)tableField.GetValue(session)!;
                    Check(table.IncludesEnchantments==exact && table.IncludesRerolls,"Random query retains declared target/reroll domain");
                    var knownTimer=Stopwatch.StartNew();var known=session.ProjectKnown(selected);
                    double knownProjectionMs=knownTimer.Elapsed.TotalMilliseconds;
                    if(requireSelected) Check(known.SelectedPlan!=null,"Random seeded query retains native-confirmed selected witness");
                    var before=EnchantmentBoundCounters(session);long replays=session.ProofReplays;
                    var searchTimer=Stopwatch.StartNew();bool cutoff=false;
                    CrystalReachabilityUpdate? update=null;CrystalFocusUpdate? focused=null;
                    using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(round.Token))
                    {
                        cancel.CancelAfter(questionMs);
                        try
                        {
                            if(target==null) update=session.Advance(selected,TimeSpan.FromMilliseconds(questionMs),cancel.Token);
                            else focused=session.AdvanceFocused(selected,target,TimeSpan.FromMilliseconds(questionMs),cancel.Token);
                        }
                        catch(OperationCanceledException) when(cancel.IsCancellationRequested) {cutoff=true;}
                        finally {cancel.Cancel();}
                        double searchMs=searchTimer.Elapsed.TotalMilliseconds;
                        var stopTimer=Stopwatch.StartNew();JoinRandomWorker(session);
                        if(target==null) update=session.Advance(selected,TimeSpan.Zero);
                        else focused=session.AdvanceFocused(selected,target,TimeSpan.Zero);
                        double stopMs=stopTimer.Elapsed.TotalMilliseconds;
                        var projection=update?.Projection??focused!.Projection;
                        var excluded=update?.ExcludedCandidates.ToHashSet()??new HashSet<CrystalRewardOption>();
                        if(focused?.Verdict==CrystalFocusVerdict.Unreachable) excluded.Add(target!);
                        var candidates=target==null?table.Candidates.Where(c=>!selected.Contains(c)).ToArray():[target];
                        var pending=candidates.Where(c=>!projection.Available.ContainsKey(c) && !excluded.Contains(c)).ToArray();
                        bool complete=update?.Complete??(focused!.Verdict!=CrystalFocusVerdict.Checking);
                        Check(!complete || pending.Length==0,"Random completed question leaves no unresolved candidate in its declared scope");
                        Check(!excluded.Any(projection.Available.ContainsKey),"Random candidates have no conflicting positive/excluded conclusions");
                        if(requireSelected) Check(projection.SelectedPlan!=null,"Random seeded witness remains available after query");
                        double additionalNativeMs=0;ImmutableArray<RewardOp> focusedActions=[];
                        if(validateFocusedNative && target?.Kind==PredictorRewardKind.Card && projection.Available.TryGetValue(target,out var plan))
                        {
                            var typed=ReplayProof(source,plan);
                            var typedRoot=PredictorCrystalExplorer.Replay(source,plan.Steps);
                            Check(EnchantmentObtainedSupports(typedRoot,typed.Run,selected.Add(target)),
                                "Focused random positive replays every selected and focused target");
                            focusedActions=Operations(typedRoot,typed).ToImmutableArray();
                            var verifyTimer=Stopwatch.StartNew();native(source,plan.Steps,typed,RandomInventoryOrder,true);
                            additionalNativeMs=verifyTimer.Elapsed.TotalMilliseconds;focusedNativeWitnesses++;
                        }
                        var solved=costs.ToArray();var after=EnchantmentBoundCounters(session);
                        receipts.Add(new {seed,index,kind,exactEnchantments=exact,selected,selectionCount=selected.Length,
                            goalKeys=selected.Select(g=>g.CacheKey).ToArray(),targetKey=target?.CacheKey,
                            orderedRelics=corpus.OrderedRelics,act=source.State.Position.Act,ascension=source.Context.Ascension,
                            sourceEvent=source.EventRng,sourceNiche=Niche(source.State),profileCompilation=profile,
                            routeSeedMs,setupMs,knownProjectionMs,knownPositive=known.Available.Count,searchBudgetMs=questionMs,
                            searchMs,stopMs,totalQueryMs=setupMs+knownProjectionMs+searchMs+stopMs,cutoff,
                            complete,selectedWitness=projection.SelectedPlan!=null,candidateCount=candidates.Length,
                            positive=candidates.Count(projection.Available.ContainsKey),excluded=excluded.Count,pending=pending.Length,
                            pendingKeys=pending.Select(c=>c.CacheKey).Order(StringComparer.Ordinal).ToArray(),
                            excludedKeys=excluded.Select(c=>c.CacheKey).Order(StringComparer.Ordinal).ToArray(),
                            availableKeys=candidates.Where(projection.Available.ContainsKey).Select(c=>c.CacheKey).Order(StringComparer.Ordinal).ToArray(),
                            focusVerdict=focused?.Verdict,focusProofSource=focused?.ProofSource,
                            progress=update?.Progress??focused!.Progress,probeSlices=update?.ProbeSlices,
                            proofReplays=session.ProofReplays-replays,solveMs=solved.Sum(c=>c.Result.Seconds*1000),
                            compileMs=solved.Sum(c=>c.Result.CompileSeconds*1000),rewardStates=solved.Sum(c=>(long)c.Result.RewardStates),
                            languageNodes=solved.Sum(c=>(long)c.Result.LanguageNodes),languageRoots=solved.Sum(c=>(long)c.Result.LanguageRoots),
                            geometryNodes=solved.Sum(c=>c.Result.GeometryNodes),verifiedWords=solved.Sum(c=>c.Result.VerifiedWords),solveCosts=solved,
                            solveCostScope="Observed completed language results only; a cancelled call may not emit costs; searchMs includes interrupted work",
                            coverSearchNodes=after.CoverSearchNodes-before.CoverSearchNodes,packingChecks=after.PackingChecks-before.PackingChecks,
                            packingRejected=after.PackingRejected-before.PackingRejected,additionalNativeMs,focusedActions,
                            focusedNativeVerification=target==null?"NotFocused":!validateFocusedNative?"AlreadyVerifiedEarlierFocus":
                                target.Kind!=PredictorRewardKind.Card?"ComparatorSupportsCardTargetsOnly":
                                additionalNativeMs>0?"Verified":"NoReachablePlanWithinBudget",
                            focusedPlan=additionalNativeMs>0?projection.Available[target!]:null,
                            scope=kind=="cold-no-selection"?"No preseeded route; cold table/bound setup and empty-selection production query; cutoff pending":
                                target==null?"Legal path and all ordered reroll variants preseeded; warm ordered selection frontier; cutoff pending":
                                "Single saved target, originally pending after its saved selection prefix; cached conclusions may differ across A/B runs; cutoff pending"});
                        questions++;ProductionSearchReplay.Write(receiptPath,receipts);
                        Console.WriteLine($"CRYSTAL_RANDOM_RELIC_QUERY seed={seed};exact={exact};kind={kind};selected={selected.Length};target={target?.CacheKey};setupMs={setupMs:F3};searchMs={searchMs:F3};complete={complete};positive={candidates.Count(projection.Available.ContainsKey)};excluded={excluded.Count};pending={pending.Length};profile={profile};budgetMs={questionMs}");
                        WriteSummary();
                        return update??new(projection,focused!.Progress,complete?CrystalDiscoveryPhase.Complete:CrystalDiscoveryPhase.Exhaustive,
                            complete,ExcludedCandidates:excluded.ToImmutableArray());
                    }
                }
            }
            roundCutoff|=round.IsCancellationRequested;stage="complete";WriteSummary();
            Console.WriteLine($"CRYSTAL_RANDOM_RELIC_SURVEY_PASS questions={questions};nativeWitnesses={nativeWitnesses};focusedNativeWitnesses={focusedNativeWitnesses};roundCutoff={roundCutoff};seedsRequested={seedCount};corpus={corpusPath};controlledInventory=true;cutoffPending=true;goldOptimumClaim=false");
            return 0;
        }
        catch(Exception ex)
        {
            ProductionSearchReplay.Write(Path.Combine(output,"random-relic-error.json"),new {seed=currentSeed,stage,error=ex.ToString(),questions,nativeWitnesses,
                corpusPath,sourceHead,elapsedMs=roundWatch.Elapsed.TotalMilliseconds});
            throw;
        }

        PredictorCrystalReachability NewRandomSession(PredictorCrystalSnapshot source,bool exact)=>
            new(source,true,includeRerolls:true,goalLanguageProof:true,includeEnchantments:exact) {ProfileLanguageCompilation=profile};
        void JoinRandomWorker(PredictorCrystalReachability session)
        {
            // Completion can clear _activeLanguage while its cancelled worker
            // still unwinds. Join every retained query before taking receipts.
            var queries=(System.Collections.IDictionary)queriesField.GetValue(session)!;
            foreach(var owner in queries.Values)
            {
                var worker=owner!.GetType().GetField("Worker",fields)?.GetValue(owner) as Task;
                if(worker==null) continue;
                Check(Task.WaitAny([worker],TimeSpan.FromSeconds(5))==0,"Random survey worker exits after cancellation");
                try {worker.GetAwaiter().GetResult();} catch(OperationCanceledException) { }
            }
        }
        RandomInventoryCorpus CreateCorpus()
        {
            const int seedRandom=20261013;const string alphabet="0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
            var random=new Random(seedRandom);var seeds=new List<string>();
            while(seeds.Count<10)
            {
                string seed=new(Enumerable.Range(0,12).Select(_=>alphabet[random.Next(alphabet.Length)]).ToArray());
                if(!seeds.Contains(seed)) seeds.Add(seed);
            }
            return new(1,seedRandom,alphabet,sourceHead,"DRIFTWOOD+"+RandomInventoryOrder,seeds.ToImmutableArray(),[],[]);
        }
        void SaveCorpus()=>ProductionSearchReplay.Write(corpusPath,corpus);
        void WriteSummary()=>ProductionSearchReplay.Write(Path.Combine(output,"random-relic-summary.json"),new {schema=1,sourceHead,
            corpusPath,corpusLoaded=loaded,corpusSourceHead=corpus.SourceHead,corpus.SeedRandom,corpus.Alphabet,seeds=corpus.Seeds,
            seedCount,coldCount,focusCount,questionMs,profileCompilation=profile,focusOnly,
            focusPolicy="2: first sorted pending after selected2, otherwise captured selected1; existing concrete targets remain frozen",
            corpus.FocusTargets,corpus.FocusPolicyHistory,maximumQueries=focusOnly?seedCount*4:seedCount*6+(coldCount+focusCount)*2,
            questions,nativeWitnesses,focusedNativeWitnesses,roundCutoff,stage,elapsedMs=roundWatch.Elapsed.TotalMilliseconds,
            preparations,scope="Bounded random native-model candidate survey; generated geometry + injected ordered inventory; timeout is pending; no Production Search E2E, normal acquisition, universal coverage, or gold optimum claim"});
        static int ReadCount(string name,int defaultValue,int min,int max)
        {
            int value=int.Parse(Environment.GetEnvironmentVariable(name)??defaultValue.ToString());
            if(value<min || value>max) throw new ArgumentOutOfRangeException(name,$"Expected {min}..{max}");
            return value;
        }
    }

    private static CrystalSuffixNode ReplayRandomOperations(PredictorRun root,ImmutableArray<RewardOp> operations)
    {
        var copy=root.ForkCrystalRewards();var actions=ImmutableArray.CreateBuilder<PredictorInput>();
        foreach(var operation in operations)
        {
            var reward=copy.Rewards[operation.Slot];
            PredictorInput action=operation.Action switch {
                "Enter"=>new EnterCardSelection(reward.Id),"Reroll"=>new RerollCardReward(reward.Id),"Exit"=>new ExitCardSelection(),
                "Take"=>new TakeReward(reward.Id,reward.Cards.First(c=>c.Key.Entry==operation.Card &&
                    c.UpgradeLevel==operation.Upgrade && (operation.Enchantment==null || operation.Enchantment.Value.Matches(c))).Id),
                _=>throw new InvalidOperationException("Random witness operation invalid:"+operation.Action)};
            Check(copy.Submit(copy.Request!,action)==PredictorInputResult.Accepted,"Saved random reward operation remains legal");
            actions.Add(action);
        }
        return new(copy,actions.ToImmutable());
    }
}
