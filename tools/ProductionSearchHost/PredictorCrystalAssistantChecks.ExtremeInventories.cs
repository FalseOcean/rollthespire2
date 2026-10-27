using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private sealed record ExtremeQueryCost(ImmutableArray<string> GoalKeys,string? TargetKey,CrystalGoalSearchResult Result);

    // Generated board geometry, injected ordered inventories. Native checks
    // establish witnessed positives; a two-second cutoff establishes no negative.
    private static int CheckExtremeRelicInventories(string output,Func<string,string,PredictorCrystalSnapshot> create,
        Action<PredictorCrystalSnapshot,ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode,string,bool> native)
    {
        var receipts=new List<object>();
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
        var tableField=typeof(PredictorCrystalReachability).GetField("_table",fields)!;
        var activeField=typeof(PredictorCrystalReachability).GetField("_activeLanguage",fields)!;
        string receiptPath=Path.Combine(output,"extreme-relic-inventories.json");
        int questions=0,nativeWitnesses=0;
        foreach(string seed in new[]{"000000000000","9TW7Y8N6663H"})
        foreach(var inventory in new[]{
            (Name:"full",Order:"FRESNEL_LENS+WING_CHARM+DINGY_RUG+GLITTER"),
            (Name:"wing-off",Order:"FRESNEL_LENS+DINGY_RUG+GLITTER"),
            (Name:"rug-off",Order:"FRESNEL_LENS+WING_CHARM+GLITTER")})
        {
            var source=create(seed,inventory.Order);
            string frozen=JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions);
            var preparation=Stopwatch.StartNew();
            var path=CardPath(source);var root=PredictorCrystalExplorer.Replay(source,path);
            Check(root.Rewards.Count(r=>r.Kind==PredictorRewardKind.Card)==3,"Extreme inventory fixture has three card reward instances");
            var prefixes=PredictorCrystalRerolls.Prefixes(root).ToArray();
            Check(prefixes.Length==16,"Extreme inventory retains sixteen ordered reroll subsets");
            var witness=ExtremeCardWitness(prefixes);
            var taken=PredictorCrystalRerolls.Take(witness.Node,witness.Goals);
            Check(taken!=null && EnchantmentObtainedSupports(root,taken.Run,witness.Goals),
                "Extreme exact three-target witness survives obtain hooks");
            double witnessPreparationMs=preparation.Elapsed.TotalMilliseconds;
            var nativeTimer=Stopwatch.StartNew();native(source,path,taken!,inventory.Order,true);
            double nativeWitnessMs=nativeTimer.Elapsed.TotalMilliseconds;nativeWitnesses++;

            foreach(bool exact in new[]{false,true})
            {
                var row=new CrystalRewardRoute(path,root.Rewards) {
                    EnchantmentTargets=exact,
                    RerollVariants=prefixes.Select(p=>new CrystalRerollVariant(p.Run.Rewards,p.Actions))
                        .OrderBy(p=>p.Actions.Length).ToImmutableArray() };
                var session=new PredictorCrystalReachability(source,true,includeRerolls:true,goalLanguageProof:true,includeEnchantments:exact) {
                    ProfileLanguageCompilation=true };
                var costs=new ConcurrentQueue<ExtremeQueryCost>();
                session.LanguageResultObserved=(selection,target,result)=>costs.Enqueue(new(
                    selection.Select(g=>g.CacheKey).ToImmutableArray(),target?.CacheKey,result));
                session.AddWitnesses([row]);
                var targets=witness.Goals.Select(g=>exact?g:g with {Enchantment=null}).ToImmutableArray();
                Check(targets.Distinct().Count()==3,"Extreme witness preserves three distinct identity/level targets in both modes");
                // No search worker starts at zero budget. This isolates actual
                // language table/bound setup plus a known single-goal projection.
                var setup=Stopwatch.StartNew();session.Advance([targets[0]],TimeSpan.Zero);
                double setupMs=setup.Elapsed.TotalMilliseconds;
                var table=(CrystalReachabilityTable)tableField.GetValue(session)!;
                Check(table.IncludesEnchantments==exact && table.IncludesRerolls,"Extreme survey uses declared target and reroll domains");
                foreach(int count in new[]{1,2,3})
                {
                    var selected=targets.Take(count).ToImmutableArray();
                    var projectionTimer=Stopwatch.StartNew();var known=session.ProjectKnown(selected);
                    double knownProjectionMs=projectionTimer.Elapsed.TotalMilliseconds;
                    Check(known.SelectedPlan!=null,"Extreme product query starts from a native-confirmed legal selection");
                    Check(selected.All(table.Candidates.Contains),"Extreme observed targets belong to the published candidate domain");
                    costs.Clear();long replaysBefore=session.ProofReplays;
                    var countersBefore=EnchantmentBoundCounters(session);
                    CrystalReachabilityUpdate update;double searchMs,stopMs;
                    using(var cancel=new CancellationTokenSource())
                    {
                        var search=Stopwatch.StartNew();
                        try {update=session.Advance(selected,TimeSpan.FromSeconds(2),cancel.Token);}
                        finally {cancel.Cancel();}
                        searchMs=search.Elapsed.TotalMilliseconds;
                        // Join the actual worker before changing goals or taking
                        // diagnostic snapshots; cancellation is not a verdict.
                        var active=activeField.GetValue(session);
                        var worker=active?.GetType().GetField("Worker",fields)?.GetValue(active) as Task;
                        var stop=Stopwatch.StartNew();
                        if(worker!=null)
                        {
                            Check(Task.WaitAny([worker],TimeSpan.FromSeconds(5))==0,"Extreme survey worker exits after cancellation");
                            try {worker.GetAwaiter().GetResult();} catch(OperationCanceledException) { }
                        }
                        update=session.Advance(selected,TimeSpan.Zero);
                        stopMs=stop.Elapsed.TotalMilliseconds;
                    }
                    var candidates=table.Candidates.Where(c=>!selected.Contains(c)).ToArray();
                    var excluded=update.ExcludedCandidates.ToHashSet();
                    var pending=candidates.Where(c=>!update.Projection.Available.ContainsKey(c) && !excluded.Contains(c)).ToArray();
                    Check(update.Projection.SelectedPlan!=null,"Extreme native-confirmed witness remains available after candidate search");
                    Check(!update.Complete || pending.Length==0,"Extreme completed frontier leaves no unresolved actual table candidate");
                    Check(!excluded.Any(update.Projection.Available.ContainsKey),"Extreme candidates cannot have conflicting conclusions");
                    var solved=costs.ToArray();var countersAfter=EnchantmentBoundCounters(session);
                    receipts.Add(new {seed,inventory=inventory.Name,orderedRelics="DRIFTWOOD+"+inventory.Order,exactEnchantments=exact,
                        includeRerolls=true,act=3,ascension=10,path,sourceEvent=source.EventRng,sourceNiche=Niche(source.State),
                        initialNiche=Niche(root.Working),maximumObservedNiche=prefixes.Max(p=>Niche(p.Run.Working).Calls),
                        witnessActions=Operations(root,taken!),witnessGoals=witness.Goals,witnessPreparationMs,nativeWitnessMs,
                        setupMs,setupScope="Once per seed/inventory/mode: zero-budget production language table/bound and known single-goal projection; not pure compilation timing",
                        selectionCount=count,selected,goalKeys=selected.Select(g=>g.CacheKey).ToArray(),knownProjectionMs,knownPositive=known.Available.Count,
                        searchBudgetMs=2000,searchMs,stopMs,update.Complete,selectedWitness=update.Projection.SelectedPlan!=null,
                        candidateCount=candidates.Length,positive=update.Projection.Available.Count,negative=excluded.Count,pending=pending.Length,
                        pendingKeys=pending.Select(c=>c.CacheKey).ToArray(),excludedKeys=excluded.Select(c=>c.CacheKey).Order(StringComparer.Ordinal).ToArray(),
                        update.Progress,update.ProbeSlices,proofReplays=session.ProofReplays-replaysBefore,
                        solveMs=solved.Sum(c=>c.Result.Seconds*1000),compileMs=solved.Sum(c=>c.Result.CompileSeconds*1000),solveCosts=solved,
                        coverSearchNodes=countersAfter.CoverSearchNodes-countersBefore.CoverSearchNodes,
                        packingChecks=countersAfter.PackingChecks-countersBefore.PackingChecks,
                        packingRejected=countersAfter.PackingRejected-countersBefore.PackingRejected,
                        scope="Generated board, controlled ordered inventory, native-confirmed card-only targets; legal CardPath/reroll variants preseeded; warm selection frontier; timeout remains pending; no cold UI or gold optimum claim"});
                    ProductionSearchReplay.Write(receiptPath,receipts);questions++;
                    Console.WriteLine($"CRYSTAL_EXTREME_QUERY seed={seed};inventory={inventory.Name};exact={exact};selected={count};setupMs={setupMs:F3};searchMs={searchMs:F3};complete={update.Complete};positive={update.Projection.Available.Count};negative={excluded.Count};pending={pending.Length};nativeWitness=true;budgetMs=2000");
                }
            }
            Check(frozen==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Extreme inventory survey preserves frozen source");
        }
        Console.WriteLine($"CRYSTAL_EXTREME_RELIC_FOCUS_PASS questions={questions};nativeWitnesses={nativeWitnesses};generatedBoards=true;controlledInventories=true;incompleteIsReported=true");
        return 0;
    }

    private static (CrystalSuffixNode Node,ImmutableArray<CrystalRewardOption> Goals) ExtremeCardWitness(CrystalSuffixNode[] prefixes)
    {
        CrystalSuffixNode? best=null;ImmutableArray<CrystalRewardOption> goals=[];int bestScore=-1;
        foreach(var node in prefixes)
        {
            var slots=node.Run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).ToArray();
            foreach(var a in slots[0].Cards) foreach(var b in slots[1].Cards) foreach(var c in slots[2].Cards)
            {
                var options=new[]{a,b,c}.Select(card=>CrystalRewardOption.ForCard(card,true)).ToImmutableArray();
                if(options.Select(o=>(o.Key,o.UpgradeLevel)).Distinct().Count()!=3) continue;
                int score=10*options.Select(o=>o.Enchantment).Distinct().Count()+node.Actions.OfType<RerollCardReward>().Count();
                if(score<=bestScore) continue;
                best=node;goals=options;bestScore=score;
            }
        }
        Check(best!=null,"Extreme inventory has a jointly witnessed distinct three-card selection");
        return (best!,goals);
    }
}
