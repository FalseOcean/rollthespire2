#!/usr/bin/env python3
"""Independent numeric oracle for the single-route Combat Reward hot-loop donors.

Validates only Search Fast physical execution for the already-frozen RP1
unpinned synthetic continuation. Production Predictor / Exact / route policy are
not modeled or changed here.
"""
from __future__ import annotations
from dataclasses import dataclass
import json, math, random, struct
from pathlib import Path

MASK=(1<<64)-1
SM_INC=0x9E3779B97F4A7C15; SM_M1=0xBF58476D1CE4E5B9; SM_M2=0x94D049BB133111EB
UNIT=2.0**-53
MANT_CARD=1<<53

def f32(x: float) -> float:
    return struct.unpack('<f', struct.pack('<f', x))[0]

def fadd(a: float,b: float)->float: return f32(f32(a)+f32(b))

def rol(x,r): return ((x<<r)|(x>>(64-r)))&MASK
class Rng:
    def __init__(self,seed):
        x=seed&MASK; self.s=[]; self.calls=0
        for _ in range(4):
            x=(x+SM_INC)&MASK; z=x
            z=((z^(z>>30))*SM_M1)&MASK; z=((z^(z>>27))*SM_M2)&MASK
            self.s.append((z^(z>>31))&MASK)
    def u64(self):
        self.calls+=1
        s0,s1,s2,s3=self.s
        out=(rol((s1*5)&MASK,7)*9)&MASK; t=(s1<<17)&MASK
        s2^=s0; s3^=s1; s1^=s2; s0^=s3; s2^=t; s3=rol(s3,45)
        self.s=[s0&MASK,s1&MASK,s2&MASK,s3&MASK]
        return out
    def mantissa(self): return self.u64()>>11
    def f32(self): return f32(self.mantissa()*UNIT)
    def integer(self,n): assert n>0; return int((self.mantissa()*UNIT)*n)
    def burn(self): self.u64()
    def sig(self): return tuple(self.s),self.calls

COMMON=[100+i for i in range(24)]
UNCOMMON=[200+i for i in range(18)]
RARE=[300+i for i in range(12)]
POOLS={1:COMMON,2:UNCOMMON,3:RARE}
POTIONS={1:[401,402,403,404,405],2:[411,412,413],3:[421,422]}

@dataclass
class Ops:
    cards:int=0; count_scans:int=0; mapping_scans:int=0; exclusion_checks:int=0
    fp64_probability_ops:int=0; integer_threshold_compares:int=0; raw_burns:int=0
    predicate_compares:int=0

def cutoff(threshold:float,inclusive:bool)->int:
    threshold=f32(threshold); lo=0; hi=MANT_CARD
    while lo<hi:
        mid=lo+((hi-lo)>>1); value=f32(mid*UNIT)
        passes=value<=threshold if inclusive else value<threshold
        if passes: lo=mid+1
        else: hi=mid
    return lo

def rarity_thresholds(asc:int,max_battle:int):
    base=f32(0.0149 if asc>=7 else 0.03); growth=f32(0.005 if asc>=7 else 0.01)
    off=f32(-0.05); rare=[]; unc=[]
    for _ in range(max_battle*3+1):
        r=fadd(base,off); u=fadd(r,0.37)
        rare.append(cutoff(r,False)); unc.append(cutoff(u,False))
        off=f32(min(f32(0.4),fadd(off,growth)))
    return rare,unc

def potion_tree(max_battle:int):
    n=(1<<max_battle)-1; vals=[f32(0.0)]*n; vals[0]=f32(0.4); cuts=[0]*n
    internal=(1<<(max_battle-1))-1
    for i in range(n):
        cuts[i]=cutoff(vals[i],False)
        if i<internal:
            vals[2*i+1]=fadd(vals[i],-0.1); vals[2*i+2]=fadd(vals[i],0.1)
    return vals,cuts

def rarity_baseline(rng:Rng,asc:int,off:float,ops:Ops):
    ops.fp64_probability_ops+=1
    v=rng.f32(); base=f32(0.0149 if asc>=7 else 0.03); rare=fadd(base,off); unc=fadd(rare,0.37)
    if v<rare: return 3,f32(-0.05)
    return (2 if v<unc else 1),f32(min(f32(0.4),fadd(off,0.005 if asc>=7 else 0.01)))

def rarity_hot(rng:Rng,asc:int,state:int,rare,unc,ops:Ops):
    m=rng.mantissa(); ops.integer_threshold_compares+=2
    if m<rare[state]: return 3,0
    return (2 if m<unc[state] else 1),min(state+1,len(rare)-1)

def scan_pick(rng:Rng,rarity:int,selected:list[int],ops:Ops):
    pool=POOLS[rarity]; available=[]
    ops.count_scans+=len(pool)
    for v in pool:
        hit=False
        for s in selected:
            ops.exclusion_checks+=1; hit=hit or v==s
        if not hit: available.append(v)
    if not available: return None
    ops.fp64_probability_ops+=1
    pick=rng.integer(len(available))
    ops.mapping_scans+=len(pool)
    seen=0
    for v in pool:
        hit=False
        for s in selected:
            ops.exclusion_checks+=1; hit=hit or v==s
        if hit: continue
        if seen==pick: return v
        seen+=1
    raise AssertionError

def direct_pick(rng:Rng,rarity:int,selected:list[tuple[int,int]],ops:Ops):
    pool=POOLS[rarity]; excluded=sorted(idx for rr,idx in selected if rr==rarity)
    assert len(excluded)<=2 and len(pool)>len(excluded)
    ops.fp64_probability_ops+=1
    actual=rng.integer(len(pool)-len(excluded))
    for e in excluded:
        if actual>=e: actual+=1
    return pool[actual],actual

def baseline(root:int,asc:int,battles:int,potion_identity:bool):
    rng=Rng(root); off=f32(-0.05); potion=f32(0.4); out=[]; ops=Ops()
    for _ in range(battles):
        ops.fp64_probability_ops+=1; drop=rng.f32()<potion; potion=fadd(potion,-0.1 if drop else 0.1)
        ops.fp64_probability_ops+=1; rng.integer(9 if asc>=3 else 11) # gold
        pot=None
        if drop:
            ops.fp64_probability_ops+=1; p=rng.f32(); rr=3 if p<=f32(.1) else (2 if p<=f32(.35) else 1)
            ops.fp64_probability_ops+=1; pot=POTIONS[rr][rng.integer(len(POTIONS[rr]))]
        cards=[]
        for _c in range(3):
            rr,off=rarity_baseline(rng,asc,off,ops)
            value=None
            for fallback in {1:(1,2,3),2:(2,3,1),3:(3,1,2)}[rr]:
                value=scan_pick(rng,fallback,cards,ops)
                if value is not None: break
            if value is None: raise AssertionError
            cards.append(value); ops.cards+=1
            ops.fp64_probability_ops+=1; rng.f32() # natural upgrade
        out.append((tuple(cards),drop,pot if potion_identity else None))
    return tuple(out),rng.sig(),potion,off,ops

def hot(root:int,asc:int,battles:int,potion_identity:bool,thresholds:bool=True):
    rng=Rng(root); card_state=0; potion_node=0; out=[]; ops=Ops()
    rare,unc=rarity_thresholds(asc,battles); potion_vals,potion_cuts=potion_tree(battles)
    rare_p=cutoff(.1,True); unc_p=cutoff(.35,True)
    selected_meta=[]
    for _b in range(battles):
        if thresholds:
            pm=rng.mantissa(); ops.integer_threshold_compares+=1; drop=pm<potion_cuts[potion_node]
        else:
            ops.fp64_probability_ops+=1; drop=rng.f32()<potion_vals[potion_node]
        potion_node=potion_node*2+(1 if drop else 2)
        rng.burn(); ops.raw_burns+=1 # gold
        pot=None
        if drop:
            if potion_identity:
                if thresholds:
                    m=rng.mantissa(); ops.integer_threshold_compares+=2
                    rr=3 if m<rare_p else (2 if m<unc_p else 1)
                else:
                    ops.fp64_probability_ops+=1; pv=rng.f32(); rr=3 if pv<=f32(.1) else (2 if pv<=f32(.35) else 1)
                ops.fp64_probability_ops+=1; pot=POTIONS[rr][rng.integer(len(POTIONS[rr]))]
            else:
                rng.burn(); rng.burn(); ops.raw_burns+=2
        cards=[]; selected_meta=[]
        for _c in range(3):
            if thresholds:
                rr,card_state=rarity_hot(rng,asc,card_state,rare,unc,ops)
            else:
                growth=f32(.005 if asc>=7 else .01)
                off=f32(-.05)
                for _ in range(card_state): off=f32(min(f32(.4),fadd(off,growth)))
                rr,new_off=rarity_baseline(rng,asc,off,ops)
                card_state=0 if rr==3 else min(card_state+1,len(rare)-1)
            v,idx=direct_pick(rng,rr,selected_meta,ops)
            cards.append(v); selected_meta.append((rr,idx)); ops.cards+=1
            rng.burn(); ops.raw_burns+=1
        out.append((tuple(cards),drop,pot if potion_identity else None))
    # state index maps consecutive nonrare draws since last rare to exact offset
    growth=f32(.005 if asc>=7 else .01)
    implied=f32(-.05)
    for _ in range(card_state): implied=f32(min(f32(.4),fadd(implied,growth)))
    # potion node is a leaf-ish index; derive final odds from actual output path independently
    p=f32(.4)
    for _,drop,_ in out: p=fadd(p,-.1 if drop else .1)
    return tuple(out),rng.sig(),p,implied,ops

# Exact boundary oracle for all threshold tables, including adversarial adjacent mantissas.
boundary_mismatch=0; boundary_vectors=0
for asc in (0,6,7,10):
    rare,unc=rarity_thresholds(asc,3)
    thresholds=[]
    base=f32(.0149 if asc>=7 else .03); growth=f32(.005 if asc>=7 else .01); off=f32(-.05)
    for _ in range(10):
        r=fadd(base,off); u=fadd(r,.37); thresholds += [(r,False,rare[_]),(u,False,unc[_])]
        off=f32(min(f32(.4),fadd(off,growth)))
    for th,inc,c in thresholds:
        for m in {0,max(0,c-2),max(0,c-1),c,min(MANT_CARD-1,c+1),MANT_CARD-1}:
            if m>=MANT_CARD: continue
            expected=f32(m*UNIT)<=th if inc else f32(m*UNIT)<th
            actual=m<c
            boundary_vectors+=1; boundary_mismatch+=expected!=actual
for th,inc in [(f32(.1),True),(f32(.35),True)]:
    c=cutoff(th,inc)
    for m in {0,max(0,c-2),max(0,c-1),c,min(MANT_CARD-1,c+1),MANT_CARD-1}:
        expected=f32(m*UNIT)<=th; actual=m<c
        boundary_vectors+=1; boundary_mismatch+=expected!=actual
for battles in (1,2,3):
    vals,cuts=potion_tree(battles)
    for th,c in zip(vals,cuts):
        for m in {0,max(0,c-2),max(0,c-1),c,min(MANT_CARD-1,c+1),MANT_CARD-1}:
            expected=f32(m*UNIT)<th; actual=m<c
            boundary_vectors+=1; boundary_mismatch+=expected!=actual

# Pool mapping exactness independent from the full RNG walk.
pool_mapping_mismatch=0; pool_vectors=0
for rarity,pool in POOLS.items():
    selections=[[],[(rarity,0)],[(rarity,len(pool)//2)],[(rarity,0),(rarity,len(pool)-1)]]
    for sel_meta in selections:
        selected=[pool[idx] for rr,idx in sel_meta]
        for seed in range(257):
            a=Rng(seed+rarity*10000); b=Rng(seed+rarity*10000); oa=Ops(); ob=Ops()
            x=scan_pick(a,rarity,selected,oa); y,_=direct_pick(b,rarity,sel_meta,ob)
            pool_vectors+=1
            if x!=y or a.sig()!=b.sig(): pool_mapping_mismatch+=1

# Full continuation parity over representative root set and all three product shapes.
roots=[0,1,2,3,0x123456789ABCDEF0,0xffffffffffffffff]
roots += [((i*0x9E3779B97F4A7C15)^0xD1B54A32D192ED03)&MASK for i in range(1,2049)]
shapes=[('MinimalCard',1,False),('CardPotion',1,True),('FirstThreeProduct',3,True),('FirstThreeNoPotionIdentity',3,False)]
retained_observable=retained_checkpoint=retained_state_mismatch=0
experimental_observable=experimental_checkpoint=experimental_state_mismatch=0; vectors=0
agg={name:{'baseline':Ops(),'retainedStructural':Ops(),'fullExperimental':Ops()} for name,_,_ in shapes}
for root in roots:
    for name,battles,pid in shapes:
        a=baseline(root,10,battles,pid); r=hot(root,10,battles,pid,thresholds=False); e=hot(root,10,battles,pid,thresholds=True); vectors+=1
        if a[0]!=r[0]: retained_observable+=1
        if a[1]!=r[1]: retained_checkpoint+=1
        if a[2]!=r[2] or a[3]!=r[3]: retained_state_mismatch+=1
        if a[0]!=e[0]: experimental_observable+=1
        if a[1]!=e[1]: experimental_checkpoint+=1
        if a[2]!=e[2] or a[3]!=e[3]: experimental_state_mismatch+=1
        for side,ops in [('baseline',a[4]),('retainedStructural',r[4]),('fullExperimental',e[4])]:
            dst=agg[name][side]
            for field in Ops.__dataclass_fields__: setattr(dst,field,getattr(dst,field)+getattr(ops,field))

# Fixed-draw raw-burn primitive parity: conversions and burns must leave identical RNG state.
raw_burn_state_mismatch=0; raw_burn_vectors=0
for seed in range(4096):
    a=Rng(seed*0x9e3779b97f4a7c15); b=Rng(seed*0x9e3779b97f4a7c15)
    a.integer(9); b.burn(); raw_burn_vectors+=1; raw_burn_state_mismatch += a.sig()!=b.sig()
    a.f32(); b.burn(); raw_burn_vectors+=1; raw_burn_state_mismatch += a.sig()!=b.sig()
    a.f32(); a.integer(5); b.burn(); b.burn(); raw_burn_vectors+=1; raw_burn_state_mismatch += a.sig()!=b.sig()

result={
 'numericVectors':vectors,
 'thresholdBoundaryVectors':boundary_vectors,
 'boundaryMismatch':boundary_mismatch,
 'poolMappingVectors':pool_vectors,
 'selectedCardMismatch':pool_mapping_mismatch,
 'rawBurnVectors':raw_burn_vectors,
 'rawBurnRngStateMismatch':raw_burn_state_mismatch,
 'retainedStructuralObservableMismatch':retained_observable,
 'retainedStructuralCheckpointMismatch':retained_checkpoint,
 'retainedStructuralPersistentStateMismatch':retained_state_mismatch,
 'fullExperimentalObservableMismatch':experimental_observable,
 'fullExperimentalCheckpointMismatch':experimental_checkpoint,
 'fullExperimentalPersistentStateMismatch':experimental_state_mismatch,
 'workloadOperationAverages':{}
}
for name,_,_ in shapes:
    result['workloadOperationAverages'][name]={}
    for side in ('baseline','retainedStructural','fullExperimental'):
        o=agg[name][side]
        result['workloadOperationAverages'][name][side]={f:round(getattr(o,f)/len(roots),4) for f in Ops.__dataclass_fields__}

out=Path(__file__).with_name('COMBAT_REWARD_SINGLE_ROUTE_HOTLOOP_NUMERIC_RESULTS.json')
out.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))
if any((boundary_mismatch,pool_mapping_mismatch,raw_burn_state_mismatch,retained_observable,retained_checkpoint,retained_state_mismatch,experimental_observable,experimental_checkpoint,experimental_state_mismatch)):
    raise SystemExit(1)
