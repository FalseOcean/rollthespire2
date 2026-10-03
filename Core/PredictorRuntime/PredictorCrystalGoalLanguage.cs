using System.Collections.Immutable;
using System.Diagnostics;

namespace RolltheSpire2.Core.PredictorRuntime;

// Product of a compressed reward language and legal reveal geometry.
// Only complete absence certifies a negative; every positive uses typed replay.
internal static class PredictorCrystalGoalLanguage
{
    internal static CrystalGoalSearchResult Solve(PredictorCrystalSnapshot source,CrystalReachabilityTable table,
        ImmutableArray<CrystalRewardOption> goals,bool avoidCurse,TimeSpan budget,int stateLimit=100_000,
        CancellationToken token=default,PredictorCrystalOffsetBound? sharedBound=null,bool rootCardSlots=false,bool profileCompilation=false)
    {
        // Identity checks reject stale boards and changed table/configuration.
        // Completed negatives live on the optional frozen bound. Language node
        // ids and local dead states still belong to this one compilation.
        if(sharedBound!=null && !sharedBound.MatchesLanguageContext(source,table,avoidCurse))
            throw new ArgumentException("Crystal language bound context mismatch",nameof(sharedBound));
        var watch=Stopwatch.StartNew();bool interrupted=false;
        bool Continue() {token.ThrowIfCancellationRequested();if(watch.Elapsed<budget) return true;interrupted=true;return false;}
        if(source.Remaining<=0 || !source.PlacedAllItems)
            return new(CrystalOffsetVerdict.Incomplete,null,0,0,0,0,0,watch.Elapsed.TotalSeconds);
        var bound=sharedBound??new PredictorCrystalOffsetBound(source,table,avoidCurse);
        token.ThrowIfCancellationRequested();
        var known=bound.KnownLanguageNegative(goals);
        if(known!=CrystalLanguageNegativeKind.None)
            return new(CrystalOffsetVerdict.Impossible,null,0,0,0,0,0,watch.Elapsed.TotalSeconds,
                NegativeKind:known,NegativeCacheHit:true);
        if(rootCardSlots && stateLimit>0 && Continue() && bound.RejectRootCardSlots(goals))
            return new(CrystalOffsetVerdict.Impossible,null,0,0,0,0,0,watch.Elapsed.TotalSeconds,
                NegativeKind:CrystalLanguageNegativeKind.RewardDomain,RootCardSlotsRejected:true);
        var language=new CrystalRewardLanguage([],[],false,0);
        var items=source.Items.Select(item=>{
            UInt128 mask=0;if(item.X<0) return mask;
            for(int x=item.X;x<item.X+item.Width;x++) for(int y=item.Y;y<item.Y+item.Height;y++) mask|=(UInt128)1<<(x*11+y);
            return mask;
        }).ToArray();
        UInt128 initial=0;for(int i=0;i<121;i++) if(source.Hidden[i]) initial|=(UInt128)1<<i;
        UInt128 occupied=items.Aggregate((UInt128)0,(m,i)=>m|i);
        var moves=(from i in Enumerable.Range(0,121) where source.Hidden[i]
            from tool in new[]{PredictorCrystalTool.Big,PredictorCrystalTool.Small}
            let cells=PredictorCrystalSearch.Cells(i/11,i%11,tool).Select(c=>c.X*11+c.Y).ToArray()
            select (Center:i,Step:new PredictorCrystalStep(i/11,i%11,tool),Cells:cells,
                Mask:cells.Aggregate((UInt128)0,(m,c)=>m|((UInt128)1<<c)))).ToArray();
        long nodes=0,endpoints=0,verified=0,paddingUnknown=0;PredictorCrystalSolution? plan=null;
        var dead=new Dictionary<(UInt128 Fog,int P,int C),int>();
        bool Visit(UInt128 fog,int p,int c,int left,UInt128 centers,ImmutableArray<PredictorCrystalStep> path)
        {
            if(!Continue()) return false;
            var potion=language.Nodes[p];var core=language.Nodes[c];long before=endpoints;
            if(potion.Terminal && core.Terminal)
            {
                endpoints++;
                var padded=Pad(path,fog,centers);
                if(padded.IsDefault) paddingUnknown++;
                else
                {
                    verified++;var run=PredictorCrystalExplorer.Replay(source,padded);
                    var row=new CrystalRewardRoute(padded,run.Rewards) { EnchantmentTargets=CrystalRewardOption.UsesEnchantments(source.State) };
                    if(table.IncludesRerolls) row=PredictorCrystalExplorer.WithRerolls(row,run);
                    plan=PredictorCrystalExplorer.VerifySelection(source,row,goals);
                    if(plan!=null) return true;
                }
            }
            if(left==0) return false;
            var key=(fog,p,c);
            if(dead.TryGetValue(key,out int known) && left<=known) return false;
            nodes++;
            void Reject()
            {
                // Semantic merging does not establish replay equivalence. Only
                // subtrees with NO accepting geometric endpoint are memoized.
                // Neither failed takes nor padding nor cutoff proves absence.
                if(interrupted || endpoints!=before) return;
                if(dead.TryGetValue(key,out int prior)) dead[key]=Math.Max(prior,left);
                else if(dead.Count<100_000) dead.Add(key,left);
            }
            if(!core.Completions.Any(cells=>bound.MayCover((potion.Required|cells)&fog,left))) {Reject();return false;}
            UInt128 relevant=(potion.Future|core.Future)&fog;
            foreach(var move in moves)
            {
                if((fog&((UInt128)1<<move.Center))==0 || (move.Mask&relevant)==0) continue;
                UInt128 next=fog;bool invalid=false;var completed=new List<int>();
                foreach(int cell in move.Cells)
                {
                    UInt128 bit=(UInt128)1<<cell;if((next&bit)==0) continue;
                    next &= ~bit;int item=source.Occupancy[cell];
                    if(item<0 || (next&items[item])!=0) continue;
                    if(source.Items[item].Kind=="CURSE") {invalid=true;break;}
                    if(source.Items[item].Subscriptions>0) completed.Add(item);
                }
                if(invalid) continue;
                bool Advance(int index,int pp,int cc)
                {
                    if(index==completed.Count) return Visit(next,pp,cc,left-1,centers|((UInt128)1<<move.Center),path.Add(move.Step));
                    int item=completed[index];bool isPotion=source.Items[item].Kind.StartsWith("POTION_",StringComparison.Ordinal);
                    foreach(var edge in language.Nodes[isPotion?pp:cc].Edges)
                        if(edge.Item==item && Advance(index+1,isPotion?edge.Next:pp,isPotion?cc:edge.Next)) return true;
                    return false;
                }
                if(Advance(0,p,c)) return true;
                if(interrupted) return false;
            }
            Reject();return false;
        }
        int processed=0;double geometrySeconds=0;
        var compilationProfile=profileCompilation?new PredictorCrystalOffsetBound.CompilationTimer():null;
        language=bound.CompileLanguage(goals,Continue,stateLimit,part=>{
            language=part;var timer=Stopwatch.StartNew();
            while(processed<part.Roots.Length && !interrupted)
            {
                var root=part.Roots[processed++];
                if(Visit(initial,root.Potion,root.Core,source.Remaining,0,[])) break;
            }
            geometrySeconds+=timer.Elapsed.TotalSeconds;return plan!=null;
        },compilationProfile);
        double compiled=watch.Elapsed.TotalSeconds-geometrySeconds;
        var verdict=plan!=null?CrystalOffsetVerdict.Possible:
            interrupted || paddingUnknown>0 || !language.Complete?CrystalOffsetVerdict.Incomplete:CrystalOffsetVerdict.Impossible;
        var negative=verdict!=CrystalOffsetVerdict.Impossible?CrystalLanguageNegativeKind.None:
            language.Roots.IsEmpty?CrystalLanguageNegativeKind.RewardDomain:
            endpoints==0?CrystalLanguageNegativeKind.Geometry:CrystalLanguageNegativeKind.OrderedQuery;
        if(negative!=CrystalLanguageNegativeKind.None) bound.RememberLanguageNegative(goals,negative);
        return new(verdict,plan,language.States,0,nodes,verified,paddingUnknown,watch.Elapsed.TotalSeconds,
            LanguageNodes:language.Nodes.Length,LanguageRoots:language.Roots.Length,CompileSeconds:compiled,LanguagePruned:language.Pruned,
            NegativeKind:negative) {CompilationProfile=compilationProfile?.Snapshot()};

        ImmutableArray<PredictorCrystalStep> Pad(ImmutableArray<PredictorCrystalStep> path,UInt128 fog,UInt128 centers)
        {
            var result=path;UInt128 remaining=fog;
            while(result.Length<source.Remaining)
            {
                int cell=-1;
                for(int i=0;i<121;i++) if((remaining&((UInt128)1<<i))!=0)
                {
                    int item=source.Occupancy[i];UInt128 need=item<0?0:remaining&items[item];
                    if(item<0 || (need&(need-1))!=0) {cell=i;break;}
                }
                if(cell<0) break;
                remaining &= ~((UInt128)1<<cell);result=result.Add(new(cell/11,cell%11,PredictorCrystalTool.Small));
            }
            if(result.Length==source.Remaining) return result;
            UInt128 allowed=initial&~occupied;
            foreach(var item in items)
            {UInt128 need=fog&item;if(need!=0) allowed|=(initial&item)&~(need&(~need+1));}
            allowed &= ~centers;var prefix=ImmutableArray.CreateBuilder<PredictorCrystalStep>();
            for(int i=0;i<121 && prefix.Count+path.Length<source.Remaining;i++)
                if((allowed&((UInt128)1<<i))!=0) prefix.Add(new(i/11,i%11,PredictorCrystalTool.Small));
            return prefix.Count+path.Length==source.Remaining?prefix.ToImmutable().AddRange(path):default;
        }
    }
}
