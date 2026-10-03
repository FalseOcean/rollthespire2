using System.Collections.Immutable;
using System.Numerics;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

// A row is exact for its generation coordinates, not a reachable board claim.
internal sealed record CrystalEnchantmentRow(ImmutableArray<PredictorCard> Cards,int NicheConsumed);

internal sealed partial class CrystalReachabilityTable
{
    private sealed record EnchantmentRows(
        ImmutableDictionary<(string Kind,int Offset,int Niche,int Groups),CrystalEnchantmentRow> Rows,
        ImmutableDictionary<(string Kind,CrystalRewardOption Option),BigInteger> Offsets,
        ImmutableArray<CrystalRewardOption> Candidates);
    private Lazy<EnchantmentRows> _enchantmentRows=null!;
    private int _cardGroupCounterLimit;
    internal int MaximumNicheCalls { get; private set; }
    internal int CanonicalCardGroups(int groups)=>Math.Min(groups,_cardGroupCounterLimit);

    private void ConfigureEnchantmentRows(PredictorCrystalSnapshot source,CancellationToken token)
    {
        var active=source.State.Relics.Where(r=>!r.Melted).ToArray();
        _cardGroupCounterLimit=active.Any(r=>r.Key.Entry=="SILVER_CRUCIBLE" && r.RewardUpgradeUses<3)?3:
            active.Any(r=>r.Key.Entry=="SILKEN_TRESS" && !r.SilkenUsed)?1:0;
        int instances=source.Revealed.Count(i=>source.Items[i].Kind.StartsWith("CARD_",StringComparison.Ordinal))+
            source.Items.Select((item,index)=>(item,index)).Where(p=>p.item.X>=0 && !source.Revealed.Contains(p.index) &&
                p.item.Kind.StartsWith("CARD_",StringComparison.Ordinal)).Sum(p=>p.item.Subscriptions);
        // Multiple Wing instances compete for the three legal card positions.
        // Revealed entries count callbacks; future subscriptions count instances.
        MaximumNicheCalls=checked(instances*(IncludesRerolls?2:1)*Math.Min(3,active.Count(r=>r.Key.Entry=="WING_CHARM")));
        // Relevant inventories materialize this during construction. A plain
        // inventory may first receive an explicit-none query in a later slice;
        // do not retain that earlier slice's cancelled token in its lazy table.
        var buildToken=IncludesEnchantments?token:CancellationToken.None;
        _enchantmentRows=new(()=>BuildEnchantmentRows(source,buildToken),LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private EnchantmentRows BuildEnchantmentRows(PredictorCrystalSnapshot source,CancellationToken token)
    {
        var rows=ImmutableDictionary.CreateBuilder<(string Kind,int Offset,int Niche,int Groups),CrystalEnchantmentRow>();
        var offsets=ImmutableDictionary.CreateBuilder<(string Kind,CrystalRewardOption Option),BigInteger>();
        var candidates=new HashSet<CrystalRewardOption>();
        var initialNiche=source.State.Streams.Single(s=>s.Stream==PredictorStream.Niche);
        foreach(string kind in source.Items.Where(i=>i.X>=0 && i.Kind.StartsWith("CARD_",StringComparison.Ordinal)).Select(i=>i.Kind).Distinct())
        {
            var plan=new PredictorRewardPlan(PredictorRewardKind.Card,CardCount:3,Odds:PredictorCardOdds.Uniform,
                Rarity:Enum.Parse<EffectCardRarity>(kind[5..],ignoreCase:true),UseEventRng:true);
            var cursor=source.EventRng.Restore();
            for(int offset=0;offset+6<=MaximumDraws;offset++,cursor.Advance(1))
                for(int groups=0;groups<=_cardGroupCounterLimit;groups++)
                {
                    var niche=initialNiche.Restore();
                    for(int layer=0;layer<=MaximumNicheCalls;layer++,niche.Advance(1))
                    {
                        token.ThrowIfCancellationRequested();
                        var rng=cursor.Clone();var state=source.State.WithRng(PredictorStream.Niche,niche);
                        state=state with { Relics=state.Relics.Select(r=>r.Key.Entry switch {
                            "SILVER_CRUCIBLE"=>r with { RewardUpgradeUses=Math.Min(3,r.RewardUpgradeUses+groups) },
                            "SILKEN_TRESS"=>r with { SilkenUsed=r.SilkenUsed || groups>0 },_=>r }).ToImmutableArray() };
                        var cards=PredictorRewardGeneration.GenerateCards(source.Context,ref state,plan,true,rng);
                        if(rng.CallCount-cursor.CallCount!=6) throw new InvalidOperationException("CrystalCardOffsetContractChanged");
                        int consumed=state.Streams.Single(s=>s.Stream==PredictorStream.Niche).Calls-niche.CallCount;
                        rows.Add((kind,offset,layer,groups),new(cards,consumed));
                        foreach(var card in cards)
                        {
                            var option=CrystalRewardOption.ForCard(card,true);candidates.Add(option);
                            var key=(kind,option);offsets[key]=offsets.GetValueOrDefault(key)|(BigInteger.One<<offset);
                        }
                        // No draw means no row property depends on this stream.
                        // The layer-zero row represents every deterministic layer.
                        if(consumed==0) break;
                    }
                }
        }
        return new(rows.ToImmutable(),offsets.ToImmutable(),candidates.ToImmutableArray());
    }

    internal bool TryCardRow(string kind,int offset,int nicheCalls,int generatedGroups,out CrystalEnchantmentRow row)
    {
        row=null!;
        if(nicheCalls<0 || nicheCalls>MaximumNicheCalls) return false;
        var rows=_enchantmentRows.Value.Rows;int groups=CanonicalCardGroups(generatedGroups);
        if(rows.TryGetValue((kind,offset,nicheCalls,groups),out row!)) return true;
        return rows.TryGetValue((kind,offset,0,groups),out row!) && row.NicheConsumed==0;
    }

    private BigInteger EnchantmentOffsets(string kind,CrystalRewardOption option)
    {
        var offsets=_enchantmentRows.Value.Offsets;
        if(option.UpgradeLevel!=null) return offsets.GetValueOrDefault((kind,option));
        BigInteger result=0;
        foreach(var pair in offsets)
            if(pair.Key.Kind==kind && option.Accepts(pair.Key.Option)) result|=pair.Value;
        return result;
    }
}
