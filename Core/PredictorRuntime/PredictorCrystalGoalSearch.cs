using System.Collections.Immutable;
using System.Diagnostics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum CrystalLanguageNegativeKind { None, RewardDomain, Geometry, OrderedQuery }
internal sealed record CrystalGoalSearchResult(CrystalOffsetVerdict Verdict,PredictorCrystalSolution? Plan,
    int RewardStates,long Recipes,long GeometryNodes,long VerifiedWords,long PaddingUnknown,double Seconds,int Batches=0,
    int LanguageNodes=0,int LanguageRoots=0,double CompileSeconds=0,int LanguagePruned=0,
    CrystalLanguageNegativeKind NegativeKind=CrystalLanguageNegativeKind.None,bool NegativeCacheHit=false,
    bool RootCardSlotsRejected=false)
{
    public CrystalLanguageCompilationProfile? CompilationProfile { get; init; }
}

// Independent experiment: reward-word enumeration drives targeted geometry.
// It never invokes Explorer's broad/beam/exhaustive search. Shared table and
// detached replay are semantic donors, not competing returned-result authorities.
internal static class PredictorCrystalGoalSearch
{
    internal static CrystalGoalSearchResult Solve(PredictorCrystalSnapshot source,CrystalReachabilityTable table,
        ImmutableArray<CrystalRewardOption> goals,bool avoidCurse,TimeSpan budget,int stateLimit=100_000,
        CancellationToken token=default)
    {
        var watch=Stopwatch.StartNew();
        // A finished reward picker needs its captured open rewards, not a new
        // board-generation query against the already advanced event stream.
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
        long recipes=0,nodes=0,verified=0,paddingUnknown=0;bool interrupted=false;
        PredictorCrystalSolution? plan=null;
        bool Continue() {token.ThrowIfCancellationRequested();if(watch.Elapsed<budget) return true;interrupted=true;return false;}
        var words=new HashSet<string>();
        var receipt=bound.VisitTerminalWords(goals,recipe=>{
            if(!Continue()) return false;
            string key=string.Join(',',recipe.Potions)+"/"+string.Join(',',recipe.Core);
            if(!words.Add(key)) return false;
            recipes++;bool settled=false;
            var failed=new HashSet<(UInt128 Fog,int P,int C,int Left,UInt128 Centers)>();
            bool Visit(UInt128 fog,int p,int c,int left,UInt128 centers,ImmutableArray<PredictorCrystalStep> path)
            {
                if(!Continue() || settled) return false;
                if(p==recipe.Potions.Length && c==recipe.Core.Length)
                {
                    var padded=Pad(path,fog,centers);
                    if(padded.IsDefault) {paddingUnknown++;return false;}
                    // Every physical interleaving of these fixed phase words has
                    // the same detached settlement under this proof's domain.
                    settled=true;verified++;
                    var run=PredictorCrystalExplorer.Replay(source,padded);
                    var actual=run.ExportCrystal().Revealed.Skip(source.Revealed.Length).ToArray();
                    bool Potion(int i)=>source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal);
                    if(!actual.Where(Potion).SequenceEqual(recipe.Potions) || !actual.Where(i=>!Potion(i)).SequenceEqual(recipe.Core))
                        throw new InvalidOperationException("CrystalGoalWordWitnessMismatch");
                    var row=new CrystalRewardRoute(padded,run.Rewards) { EnchantmentTargets=CrystalRewardOption.UsesEnchantments(source.State) };
                    if(table.IncludesRerolls) row=PredictorCrystalExplorer.WithRerolls(row,run);
                    plan=PredictorCrystalExplorer.VerifySelection(source,row,goals);
                    return plan!=null;
                }
                if(left==0 || !failed.Add((fog,p,c,left,centers))) return false;
                nodes++;UInt128 need=0;
                for(int i=p;i<recipe.Potions.Length;i++) need|=items[recipe.Potions[i]];
                for(int i=c;i<recipe.Core.Length;i++) need|=items[recipe.Core[i]];
                need &= fog;
                if(!bound.MayCover(need,left)) return false;
                foreach(var move in moves)
                {
                    if((fog&((UInt128)1<<move.Center))==0 || (move.Mask&need)==0) continue;
                    UInt128 next=fog;int pp=p,cc=c;bool invalid=false;
                    foreach(int cell in move.Cells)
                    {
                        UInt128 bit=(UInt128)1<<cell;if((next&bit)==0) continue;
                        next &= ~bit;int item=source.Occupancy[cell];
                        if(item<0 || (next&items[item])!=0) continue;
                        var spec=source.Items[item];
                        if(spec.Kind=="CURSE") {invalid=true;break;}
                        bool potion=spec.Kind.StartsWith("POTION_",StringComparison.Ordinal);
                        var word=potion?recipe.Potions:recipe.Core;int index=potion?pp:cc;
                        for(int n=0;n<spec.Subscriptions;n++)
                        {if(index==word.Length || word[index++]!=item) {invalid=true;break;}}
                        if(invalid) break;
                        if(potion) pp=index;else cc=index;
                    }
                    if(!invalid && Visit(next,pp,cc,left-1,centers|((UInt128)1<<move.Center),path.Add(move.Step))) return true;
                    if(interrupted || settled) return false;
                }
                return false;
            }
            return Visit(initial,0,0,source.Remaining,0,[]);
        },Continue,stateLimit);
        var verdict=plan!=null?CrystalOffsetVerdict.Possible:
            interrupted || paddingUnknown>0?CrystalOffsetVerdict.Incomplete:receipt.Verdict;
        return new(verdict,plan,receipt.States,recipes,nodes,verified,paddingUnknown,watch.Elapsed.TotalSeconds);

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
