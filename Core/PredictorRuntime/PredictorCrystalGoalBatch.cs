using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace RolltheSpire2.Core.PredictorRuntime;

// Test-only alternative: a small language of target-compatible final reward
// words shares one geometry traversal. No broad board search is invoked.
internal static class PredictorCrystalGoalBatch
{
    internal static CrystalGoalSearchResult Solve(PredictorCrystalSnapshot source,CrystalReachabilityTable table,
        ImmutableArray<CrystalRewardOption> goals,bool avoidCurse,TimeSpan budget,int stateLimit=100_000,
        CancellationToken token=default,int batchSize=64)
    {
        if(batchSize is <1 or >64) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var watch=Stopwatch.StartNew();
        if(source.Remaining<=0 || !source.PlacedAllItems)
            return new(CrystalOffsetVerdict.Incomplete,null,0,0,0,0,0,watch.Elapsed.TotalSeconds);
        var bound=new PredictorCrystalOffsetBound(source,table,avoidCurse);
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
        long recipes=0,nodes=0,verified=0,paddingUnknown=0;int batches=0;bool interrupted=false;
        PredictorCrystalSolution? plan=null;
        bool Continue() {token.ThrowIfCancellationRequested();if(watch.Elapsed<budget) return true;interrupted=true;return false;}
        var words=new HashSet<string>();var pending=new List<CrystalRewardRequirement>();int nextBatch=1;
        var receipt=bound.VisitTerminalWords(goals,recipe=>{
            if(!Continue()) return false;
            if(!words.Add(string.Join(',',recipe.Potions)+"/"+string.Join(',',recipe.Core))) return false;
            recipes++;pending.Add(recipe);
            // Do not delay an easy first witness to compile a full language.
            // Grow only after earlier compatible words have failed geometry.
            if(pending.Count<nextBatch) return false;
            bool found=Flush();nextBatch=Math.Min(batchSize,nextBatch*4);return found;
        },Continue,stateLimit);
        // A partial semantic enumeration may still supply a positive witness;
        // it must never become a negative certificate.
        if(plan==null && pending.Count>0 && Continue()) Flush();
        var verdict=plan!=null?CrystalOffsetVerdict.Possible:
            interrupted || paddingUnknown>0?CrystalOffsetVerdict.Incomplete:receipt.Verdict;
        return new(verdict,plan,receipt.States,recipes,nodes,verified,paddingUnknown,watch.Elapsed.TotalSeconds,batches);

        bool Flush()
        {
            batches++;var batch=pending.ToArray();pending.Clear();
            int maxP=batch.Max(r=>r.Potions.Length),maxC=batch.Max(r=>r.Core.Length);
            var pMasks=new ulong[maxP,items.Length];var cMasks=new ulong[maxC,items.Length];
            var pEnds=new ulong[maxP+1];var cEnds=new ulong[maxC+1];
            var pNeeds=new UInt128[batch.Length][];var cNeeds=new UInt128[batch.Length][];
            UInt128[] Prepare(ImmutableArray<int> word,ulong[,] masks,ulong[] ends,int index)
            {
                ulong bit=1UL<<index;ends[word.Length]|=bit;var needs=new UInt128[word.Length+1];
                for(int i=word.Length-1;i>=0;i--) {masks[i,word[i]]|=bit;needs[i]=needs[i+1]|items[word[i]];}
                return needs;
            }
            for(int i=0;i<batch.Length;i++)
            {pNeeds[i]=Prepare(batch[i].Potions,pMasks,pEnds,i);cNeeds[i]=Prepare(batch[i].Core,cMasks,cEnds,i);}
            ulong settled=0;
            var visited=new HashSet<(UInt128 Fog,int P,int C,int Left,UInt128 Centers,ulong Words)>();
            bool Visit(UInt128 fog,int p,int c,int left,UInt128 centers,ImmutableArray<PredictorCrystalStep> path,ulong live)
            {
                if(!Continue()) return false;
                live &= ~settled;if(live==0) return false;
                ulong complete=live&pEnds[p]&cEnds[c];
                if(complete!=0)
                {
                    var padded=Pad(path,fog,centers);
                    if(padded.IsDefault) paddingUnknown++;
                    else
                    {
                        // Duplicate words were removed before batching: the
                        // completed phase history identifies one exact word.
                        int i=BitOperations.TrailingZeroCount(complete);settled|=complete;verified++;
                        var run=PredictorCrystalExplorer.Replay(source,padded);
                        var actual=run.ExportCrystal().Revealed.Skip(source.Revealed.Length).ToArray();
                        bool Potion(int item)=>source.Items[item].Kind.StartsWith("POTION_",StringComparison.Ordinal);
                        if(!actual.Where(Potion).SequenceEqual(batch[i].Potions) || !actual.Where(item=>!Potion(item)).SequenceEqual(batch[i].Core))
                            throw new InvalidOperationException("CrystalGoalBatchWitnessMismatch");
                        var row=new CrystalRewardRoute(padded,run.Rewards) { EnchantmentTargets=CrystalRewardOption.UsesEnchantments(source.State) };
                        if(table.IncludesRerolls) row=PredictorCrystalExplorer.WithRerolls(row,run);
                        plan=PredictorCrystalExplorer.VerifySelection(source,row,goals);
                        if(plan!=null) return true;
                    }
                    // Longer compatible words still need checking even after
                    // an accepting prefix fails padding or target verification.
                    live &= ~complete;
                }
                if(live==0 || left==0) return false;
                var state=(fog,p,c,left,centers,live);
                if(visited.Contains(state)) return false;
                // Capacity only drops reuse; it never rejects an unseen state.
                if(visited.Count<100_000) visited.Add(state);
                nodes++;
                UInt128 need=0;
                for(ulong bits=live;bits!=0;bits &= bits-1)
                {
                    int i=BitOperations.TrailingZeroCount(bits);UInt128 required=(pNeeds[i][p]|cNeeds[i][c])&fog;
                    if(!bound.MayCover(required,left)) live &= ~(1UL<<i);
                    else need|=required;
                }
                if(live==0) return false;
                foreach(var move in moves)
                {
                    if((fog&((UInt128)1<<move.Center))==0 || (move.Mask&need)==0) continue;
                    UInt128 next=fog;int pp=p,cc=c;ulong candidates=live;
                    foreach(int cell in move.Cells)
                    {
                        UInt128 bit=(UInt128)1<<cell;if((next&bit)==0) continue;
                        next &= ~bit;int item=source.Occupancy[cell];
                        if(item<0 || (next&items[item])!=0) continue;
                        var spec=source.Items[item];
                        if(spec.Kind=="CURSE") {candidates=0;break;}
                        bool potion=spec.Kind.StartsWith("POTION_",StringComparison.Ordinal);
                        for(int n=0;n<spec.Subscriptions;n++)
                        {
                            if(potion) {if(pp>=maxP) {candidates=0;break;} candidates &= pMasks[pp++,item];}
                            else {if(cc>=maxC) {candidates=0;break;} candidates &= cMasks[cc++,item];}
                            if(candidates==0) break;
                        }
                        if(candidates==0) break;
                    }
                    if(candidates!=0 && Visit(next,pp,cc,left-1,centers|((UInt128)1<<move.Center),path.Add(move.Step),candidates)) return true;
                    if(interrupted) return false;
                }
                return false;
            }
            return Visit(initial,0,0,source.Remaining,0,[],batch.Length==64?ulong.MaxValue:(1UL<<batch.Length)-1);
        }

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
