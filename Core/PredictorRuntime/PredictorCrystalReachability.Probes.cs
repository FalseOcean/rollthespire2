using System.Collections.Immutable;
using System.Diagnostics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalReachability
{
    private sealed class Probe(CrystalRewardOption target,PredictorCrystalExplorer explorer)
    {
        internal readonly CrystalRewardOption Target=target;
        internal readonly PredictorCrystalExplorer Explorer=explorer;
        internal TimeSpan TurnTime;
    }

    // After selection, ask S+X independently. Every job can rearrange the whole
    // legal path; no chosen positive witness is imposed as a required prefix.
    // Jobs share only immutable tables and the thread-safe take-proof cache.
    private void AdvanceCandidates(Query query,ImmutableArray<CrystalRewardOption> selected,TimeSpan budget,CancellationToken token)
    {
        var resolved=PeekKnown(selected).Available.Keys.Concat(query.Lanes.SelectMany(l=>l.Explorer.QueryProjection.Available.Keys))
            .Concat(query.Lanes.SelectMany(l=>l.Explorer.ExcludedCandidates)).Concat(query.ExhaustedProbes).ToHashSet();
        foreach(var target in query.Probes.Keys.Where(resolved.Contains).ToArray()) query.Probes.Remove(target);
        foreach(var job in query.ActiveCandidates.ToArray())
        {
            if(resolved.Contains(job.Target)) { query.ActiveCandidates.Remove(job);query.Probes.Remove(job.Target); }
            else if(job.TurnTime>=TimeSpan.FromSeconds(1)) query.ActiveCandidates.Remove(job);
        }
        for(int n=0;n<query.ProbeOrder.Length && query.ActiveCandidates.Count<workers;n++)
        {
            var target=query.ProbeOrder[query.ProbeCursor];query.ProbeCursor=(query.ProbeCursor+1)%query.ProbeOrder.Length;
            if(resolved.Contains(target) || query.ActiveCandidates.Any(p=>p.Target==target)) continue;
            if(!query.Probes.TryGetValue(target,out var job))
            {
                // Rotate resident frontiers without throwing away unfinished
                // proof work. Broad discovery still covers nonresident targets;
                // an explicit focus can prioritize any of them immediately.
                if(query.Probes.Count>=2*workers) continue;
                job=new(target,new(source,avoidCurse,reachability:_table,selected:selected.Add(target),
                    potionScenarioBound:true,potionScenarioLimit:potionScenarioLimit,targetDirected:true,
                    verifySelection:VerifyCached,stopAfterPlan:true,offsetBound:_offsetBound));
                query.Probes.Add(target,job);
            }
            job.TurnTime=TimeSpan.Zero;query.ActiveCandidates.Add(job);
        }
        var jobs=query.ActiveCandidates.ToArray();
        var batches=new ImmutableArray<CrystalRewardRoute>[jobs.Length];
        var before=jobs.Select(j=>j.Explorer.Progress).ToArray();
        void Advance(int i)
        {
            var watch=Stopwatch.StartNew();batches[i]=jobs[i].Explorer.Advance(budget,token);jobs[i].TurnTime+=watch.Elapsed;
        }
        if(jobs.Length==1) Advance(0);
        else if(jobs.Length>1) Parallel.For(0,jobs.Length,new ParallelOptions { MaxDegreeOfParallelism=workers },Advance);
        for(int i=0;i<jobs.Length;i++)
        {
            var job=jobs[i];var after=job.Explorer.Progress;query.ProbeSlices++;
            query.ProbeExamined+=after.Examined-before[i].Examined;query.ProbePruned+=after.Pruned-before[i].Pruned;query.ProbeMerged+=after.Merged-before[i].Merged;
            foreach(var row in batches[i])
                if(_paths.Add(string.Join(';',row.Steps.Select(s=>$"{s.X},{s.Y},{s.Tool}")))) _witnesses.Add(row);
            // Harvest all incidental compatible rewards, not only X.
            foreach(var lane in query.Lanes) lane.Explorer.SeedQuery(batches[i]);
            if(job.Explorer.QueryProjection.SelectedPlan!=null)
            { query.ProbePlans++;query.Probes.Remove(job.Target);query.ActiveCandidates.Remove(job); }
            else if(job.Explorer.Complete)
            { query.ExhaustedProbes.Add(job.Target);query.Probes.Remove(job.Target);query.ActiveCandidates.Remove(job); }
        }
    }

    private CrystalRewardOption[] ProbeOrder(ImmutableArray<CrystalRewardOption> candidates)
    {
        int Rarity(string? rarity)=>rarity switch { "Rare"=>0,"Uncommon"=>1,_=>2 };
        int Group(CrystalRewardOption option)=>option.Kind switch {
            PredictorRewardKind.Relic=>0,
            PredictorRewardKind.Card=>1+Rarity(source.Context.Catalog!.CardDefinitions.First(c=>c.Prototype.Key==option.Key).Rarity),
            _=>4 };
        int RelicRank(CrystalRewardOption option)=>option.Kind!=PredictorRewardKind.Relic?0:
            Rarity(source.State.PersonalBag.Buckets.FirstOrDefault(b=>b.Entries.Contains(option.Key))?.Rarity);
        var groups=candidates.Where(c=>c!=CrystalRewardOption.AnyRelic).GroupBy(Group).OrderBy(g=>g.Key)
            .Select(g=>new Queue<CrystalRewardOption>(g.OrderBy(RelicRank).ThenBy(c=>c.Key.Serialized,StringComparer.Ordinal).ThenBy(c=>c.UpgradeLevel))).ToArray();
        var order=new List<CrystalRewardOption>();
        while(groups.Any(g=>g.Count>0)) foreach(var group in groups) if(group.TryDequeue(out var option)) order.Add(option);
        return order.ToArray();
    }

    // A bounded positive-discovery pass. Each target retains a cursor across
    // slices; at most four resident cursors rotate without losing progress.
    // The original lanes alone retain candidate-completeness authority.
    private void AdvanceProbe(Query query,ImmutableArray<CrystalRewardOption> selected,TimeSpan budget,CancellationToken token)
    {
        var known=PeekKnown(selected).Available.Keys.Concat(query.Lanes.SelectMany(l=>l.Explorer.QueryProjection.Available.Keys)).ToHashSet();
        known.UnionWith(query.Lanes.SelectMany(l=>l.Explorer.ExcludedCandidates));
        known.UnionWith(query.ExhaustedProbes);
        bool Resolved(CrystalRewardOption option)=>known.Contains(option);
        foreach(var target in query.Probes.Keys.Where(Resolved).ToArray()) query.Probes.Remove(target);
        if(query.ActiveProbe is {} active && (Resolved(active.Target) || active.Explorer.Complete || active.Explorer.QueryProjection.SelectedPlan!=null))
        {
            query.Probes.Remove(active.Target);query.ActiveProbe=null;
        }
        if(query.ActiveProbe==null)
        {
            for(int n=0;n<query.ProbeOrder.Length;n++)
            {
                var target=query.ProbeOrder[query.ProbeCursor];
                query.ProbeCursor=(query.ProbeCursor+1)%query.ProbeOrder.Length;
                if(Resolved(target) || query.ExhaustedProbes.Contains(target)) continue;
                if(!query.Probes.TryGetValue(target,out var probe))
                {
                    if(query.Probes.Count>=4) continue;
                    probe=new(target,new(source,avoidCurse,reachability:_table,selected:selected.Add(target),
                        potionScenarioBound:true,potionScenarioLimit:potionScenarioLimit,targetDirected:true,
                        verifySelection:VerifyCached,stopAfterPlan:true,offsetBound:_offsetBound));
                    query.Probes.Add(target,probe);
                }
                probe.TurnTime=TimeSpan.Zero;query.ActiveProbe=probe;break;
            }
        }
        if(query.ActiveProbe is not {} job || token.IsCancellationRequested) return;
        var before=job.Explorer.Progress;var watch=Stopwatch.StartNew();
        var rows=job.Explorer.Advance(budget,token);
        job.TurnTime+=watch.Elapsed;query.ProbeSlices++;
        var after=job.Explorer.Progress;
        query.ProbeExamined+=after.Examined-before.Examined;query.ProbePruned+=after.Pruned-before.Pruned;query.ProbeMerged+=after.Merged-before.Merged;
        foreach(var row in rows)
            if(_paths.Add(string.Join(';',row.Steps.Select(s=>$"{s.X},{s.Y},{s.Tool}")))) _witnesses.Add(row);
        // Reuse every byproduct under the player's actual selected goals, not
        // just the probe target. Each lane validates its own candidate partition.
        foreach(var lane in query.Lanes) lane.Explorer.SeedQuery(rows);
        if(job.Explorer.QueryProjection.SelectedPlan!=null) { query.ProbePlans++;query.Probes.Remove(job.Target);query.ActiveProbe=null; }
        else if(job.Explorer.Complete)
        {
            // Full exhaustion without a plan proves this one extra target
            // impossible under the current selection, not parent completion.
            query.ExhaustedProbes.Add(job.Target);query.Probes.Remove(job.Target);query.ActiveProbe=null;
        }
        else if(job.TurnTime>=TimeSpan.FromSeconds(1)) query.ActiveProbe=null;
    }
}
