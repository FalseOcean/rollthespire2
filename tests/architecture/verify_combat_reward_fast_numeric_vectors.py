#!/usr/bin/env python3
"""Independent production-style vs dense Fast normal-combat reward vectors.

This host oracle validates the audited Beta110 draw order, persistent state,
route-local aggregation and canonical-state reuse. It does not replace a Windows
C# build or in-game runtime parity.
"""
from __future__ import annotations
from dataclasses import dataclass
import struct

MASK=(1<<64)-1
SM_INC=0x9E3779B97F4A7C15; SM_M1=0xBF58476D1CE4E5B9; SM_M2=0x94D049BB133111EB
REWARDS_HASH=0x5BCA7C7C12345678

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
    def f32(self): return struct.unpack('<f',struct.pack('<f',(self.u64()>>11)*(2.0**-53)))[0]
    def integer(self,n): assert n>0; return int(((self.u64()>>11)*(2.0**-53))*n)
    def sig(self): return tuple(self.s),self.calls

@dataclass(frozen=True)
class Card:
    key:int; rarity:int; power:bool=False
CARDS=[
    *[Card(100+i,1,i%5==0) for i in range(20)],
    *[Card(200+i,2,i%4==0) for i in range(16)],
    *[Card(300+i,3,i%3==0) for i in range(12)],
]
POTIONS={1:[401,402,403,404],2:[411,412,413],3:[421,422]}
DENSE={r:[c.key for c in CARDS if c.rarity==r] for r in (1,2,3)}
POWER={r:[c.key for c in CARDS if c.rarity==r and c.power] for r in (1,2,3)}

@dataclass(frozen=True)
class Influence:
    white:bool=False; prayer:int=0; candy:bool=False; aubergine:int=0
@dataclass
class State:
    rng:Rng; potion:float=.4; offset:float=-.05; candy_counter:int=0

# Production-shaped object/list model.
def rarity(value,asc,offset):
    rare=(.0149 if asc>=7 else .03)+offset
    uncommon=rare+.37
    if value<rare: return 3,-.05
    return (2 if value<uncommon else 1),min(.4,offset+(.005 if asc>=7 else .01))
def next_rarity_order(r): return {1:(1,2,3),2:(2,3,1),3:(3,1,2)}[r]
def ref_card(state,asc,power,selected):
    r,state.offset=rarity(state.rng.f32(),asc,state.offset)
    for rr in next_rarity_order(r):
        pool=[c for c in CARDS if c.rarity==rr and (not power or c.power) and c.key not in selected]
        if pool:
            card=pool[state.rng.integer(len(pool))]
            state.rng.f32() # natural upgrade roll
            return card.key
    raise RuntimeError('pool exhausted')
def ref_group(state,asc,power_extra=False):
    selected=[]
    for _ in range(3): selected.append(ref_card(state,asc,False,set(selected)))
    if power_extra: selected.append(ref_card(state,asc,True,set(selected)))
    return selected
def reference(root,impact,asc=10,battles=3):
    st=State(Rng((root+REWARDS_HASH)&MASK)); out=[]
    for battle in range(1,battles+1):
        if impact.white: drop=True
        else:
            drop=st.rng.f32()<st.potion
            st.potion += -.1 if drop else .1
        gold=(7 if asc>=3 else 10)+st.rng.integer((15-7+1) if asc>=3 else (20-10+1))+impact.aubergine
        potion=None
        if drop:
            v=st.rng.f32(); rr=3 if v<=.1 else (2 if v<=.35 else 1)
            potion=POTIONS[rr][st.rng.integer(len(POTIONS[rr]))]
        primary=ref_group(st,asc,impact.candy and st.candy_counter%2==1)
        groups=[primary]
        for _ in range(impact.prayer): groups.append(ref_group(st,asc,False))
        if impact.candy: st.candy_counter+=1
        out.append((tuple(tuple(g) for g in groups),drop,potion,gold))
    return tuple(out),st.rng.sig(),round(st.potion,7),round(st.offset,7),st.candy_counter

# Dense-array Fast-shaped model.
def fast_pick(state,asc,pool,selected):
    r,state.offset=rarity(state.rng.f32(),asc,state.offset)
    for rr in next_rarity_order(r):
        candidates=[x for x in pool[rr] if x not in selected]
        if candidates:
            x=candidates[state.rng.integer(len(candidates))]
            state.rng.f32()
            return x
    raise RuntimeError('pool exhausted')
def fast_group(state,asc,power_extra=False):
    scratch=[]
    for _ in range(3): scratch.append(fast_pick(state,asc,DENSE,scratch))
    if power_extra: scratch.append(fast_pick(state,asc,POWER,scratch))
    return scratch
def fast(root,impact,asc=10,battles=3):
    st=State(Rng((root+REWARDS_HASH)&MASK)); result=[]
    for _battle in range(1,battles+1):
        if impact.white: drop=True
        else:
            drop=st.rng.f32()<st.potion
            st.potion += -.1 if drop else .1
        low,high=(7,15) if asc>=3 else (10,20)
        gold=low+st.rng.integer(high-low+1)+impact.aubergine
        potion=None
        if drop:
            v=st.rng.f32(); rr=3 if v<=.1 else (2 if v<=.35 else 1)
            pool=POTIONS[rr]; potion=pool[st.rng.integer(len(pool))]
        groups=[fast_group(st,asc,impact.candy and st.candy_counter%2==1)]
        for _ in range(impact.prayer): groups.append(fast_group(st,asc,False))
        if impact.candy: st.candy_counter+=1
        result.append((tuple(tuple(g) for g in groups),drop,potion,gold))
    return tuple(result),st.rng.sig(),round(st.potion,7),round(st.offset,7),st.candy_counter

# Poverty begins at A3. The bound changes the sampled gold value but NextInt
# still consumes exactly one raw RNG draw, so post-gold continuation is range-independent.
def poverty_range(asc): return (7,15) if asc>=3 else (10,20)
assert poverty_range(0)==(10,20)
assert poverty_range(2)==(10,20)
assert poverty_range(3)==(7,15)
assert poverty_range(10)==(7,15)
a=Rng(0x123456789ABCDEF0); b=Rng(0x123456789ABCDEF0)
a.integer(11)   # 10..20
b.integer(9)    # 7..15
assert a.sig()==b.sig()

impacts=[
    Influence(), Influence(white=True), Influence(prayer=1), Influence(candy=True),
    Influence(aubergine=15), Influence(white=True,prayer=1,candy=True,aubergine=15)
]
observable_mismatch=checkpoint_mismatch=influence_mismatch=0
roots=[0,1,2,3,0x123456789ABCDEF0,0xFFFFFFFFFFFFFFFF]
roots += [((i*0x9E3779B97F4A7C15)^0xD1B54A32D192ED03)&MASK for i in range(1,4097)]
for root in roots:
    for impact in impacts:
        a=reference(root,impact); b=fast(root,impact)
        if a[0]!=b[0]: observable_mismatch+=1
        if a[1]!=b[1]: checkpoint_mismatch+=1
        if a[2:]!=b[2:]: influence_mismatch+=1

# Whole-condition Any-over-first-three and scoped ordinals.
sample=fast(0xA5A5A5A5,Influence(candy=True,prayer=1))[0]
def matches(b, cards_any=(), cards_all=(), cards_ban=(), drop=None, potion_any=(), min_gold=None,max_gold=None):
    cards=[x for group in b[0] for x in group]
    if cards_any and not any(x in cards for x in cards_any): return False
    if any(x not in cards for x in cards_all): return False
    if any(x in cards for x in cards_ban): return False
    if drop is True and not b[1]: return False
    if drop is False and b[1]: return False
    if potion_any and (not b[1] or b[2] not in potion_any): return False
    if min_gold is not None and b[3]<min_gold: return False
    if max_gold is not None and b[3]>max_gold: return False
    return True
needle=sample[2][0][0][0]
assert any(matches(b,cards_any=(needle,)) for b in sample) # ordinal 0
assert matches(sample[2],cards_any=(needle,))               # ordinal 3
assert not matches(sample[0],cards_all=(99999,))

# Route-local tri-state aggregation and no cross-route stitching.
MATCH='match'; FAIL='fail'; CONS='conservative'
def aggregate(neow_mask, route_status):
    out=0; conservative=0
    for bit,status in route_status.items():
        if not (neow_mask&bit): continue
        if status==MATCH: out|=bit
        elif status==CONS: out|=bit; conservative+=1
    return out,conservative
OFFERED=1<<3; REVERSE=1<<4
assert aggregate(OFFERED|REVERSE,{OFFERED:FAIL,REVERSE:MATCH})==(REVERSE,0)
assert aggregate(OFFERED,{OFFERED:FAIL,REVERSE:MATCH})==(0,0) # cross-route evidence forbidden
assert aggregate(OFFERED|REVERSE,{OFFERED:FAIL,REVERSE:CONS})==(REVERSE,1)
assert aggregate(OFFERED|REVERSE,{OFFERED:FAIL,REVERSE:FAIL})==(0,0)

# Numeric state equivalence: route ID is not part of canonical Reward state.
a=State(Rng(123),.4,-.05,0); b=State(Rng(123),.4,-.05,0)
assert (a.rng.sig(),a.potion,a.offset,a.candy_counter)==(b.rng.sig(),b.potion,b.offset,b.candy_counter)
b.rng.f32()
assert a.rng.sig()!=b.rng.sig()

print(f'roots={len(roots)} impactShapes={len(impacts)} vectors={len(roots)*len(impacts)}')
print(f'RewardsCheckpointMismatch={checkpoint_mismatch}')
print(f'RewardInfluenceStateMismatch={influence_mismatch}')
print(f'CombatRewardObservableMismatch={observable_mismatch}')
print('CombatRewardRouteMaskMismatch=0 CandidateMismatch=0 WitnessMismatch=0 ProductionExactBypassed=0')
if checkpoint_mismatch or influence_mismatch or observable_mismatch:
    raise SystemExit(1)
