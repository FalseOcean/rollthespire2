namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalExplorer
{
    internal long ReverseChecks { get; private set; }
    internal long ReverseRejected { get; private set; }
    internal long ReverseAlternatives { get; private set; }
    internal long ReverseGeometryNodes { get; private set; }

    private bool ReverseMaySupport(Position node)
    {
        // Called only with the existing blank-padding guarantee. Non-progress
        // moves cannot create a legal center; ignoring them is safe here, while
        // Small/partial progress on a required item must remain available.
        ReverseChecks++;
        int left=_source.Remaining-node.Path.Length;
        var proof=_offsetBound!.Requirements(_selected,node.Order,node.Fog,left);
        if(proof.Receipt.Verdict==CrystalOffsetVerdict.Incomplete) return true;
        foreach(var requirement in proof.Alternatives)
        {
            ReverseAlternatives++;
            var failed=new HashSet<(UInt128,int,int,int)>();
            bool Visit(UInt128 fog,int p,int c,int remaining)
            {
                if(p==requirement.Potions.Length && c==requirement.Core.Length) return true;
                if(remaining==0 || !failed.Add((fog,p,c,remaining))) return false;
                ReverseGeometryNodes++;
                UInt128 need=0;
                for(int i=p;i<requirement.Potions.Length;i++) need|=_items[requirement.Potions[i]];
                for(int i=c;i<requirement.Core.Length;i++) need|=_items[requirement.Core[i]];
                need &= fog;
                if(!CanCover(need,remaining)) return false;
                foreach(var move in _moves)
                {
                    int center=move.Step.X*11+move.Step.Y;
                    if(node.Path.IsEmpty && remaining==left && (center+(move.Step.Tool==PredictorCrystalTool.Big?1:0))%_rootPartitions!=_rootPartition) continue;
                    if((fog & ((UInt128)1<<center))==0 || (move.Mask&need)==0) continue;
                    UInt128 next=fog;int pp=p,cc=c;bool invalid=false;
                    foreach(int cell in move.Cells)
                    {
                        UInt128 bit=(UInt128)1<<cell;if((next&bit)==0) continue;
                        next &= ~bit;int item=_source.Occupancy[cell];
                        if(item<0 || (next&_items[item])!=0) continue;
                        var source=_source.Items[item];
                        if(_avoidCurse && source.Kind=="CURSE") {invalid=true;break;}
                        bool potion=source.Kind.StartsWith("POTION_",StringComparison.Ordinal);
                        var word=potion?requirement.Potions:requirement.Core;
                        int index=potion?pp:cc;
                        // Once a phase's required prefix is satisfied, extra
                        // rewards in that phase are allowed by this relaxation.
                        for(int n=0;n<source.Subscriptions && index<word.Length;n++)
                        {if(word[index++]!=item) {invalid=true;break;}}
                        if(invalid) break;
                        if(potion) pp=index;else cc=index;
                    }
                    if(!invalid && Visit(next,pp,cc,remaining-1)) return true;
                }
                return false;
            }
            if(Visit(node.Fog,0,0,left)) return true;
        }
        ReverseRejected++;return false;
    }
}
