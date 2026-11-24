#!/usr/bin/env python3
"""P10-A host numeric oracle for streaming-vs-materialized Reward evaluation.

This validates the selected donor's semantic shape independently of GLSL:
- identical battle generation/RNG order for every battle that must be visited;
- FixedBattle and AnyOfFirstThree condition parity;
- failure-only early reject;
- full RNG/influence checkpoint parity for accepted routes.
It intentionally does not model a Raw RNG Burn because P10-A does not select one.
"""
from __future__ import annotations
from dataclasses import dataclass
import runpy
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
ns=runpy.run_path(str(ROOT/'tests/architecture/verify_combat_reward_fast_numeric_vectors.py'))
Rng=ns['Rng']; State=ns['State']; Influence=ns['Influence']; fast_group=ns['fast_group']; POTIONS=ns['POTIONS']; REWARDS_HASH=ns['REWARDS_HASH']; MASK=ns['MASK']

@dataclass(frozen=True)
class Cond:
    battle:int # 0 = AnyOfFirstThree
    cards_any:tuple[int,...]=()
    cards_all:tuple[int,...]=()
    cards_ban:tuple[int,...]=()
    drop:bool|None=None
    potion_any:tuple[int,...]=()

def match(b, c:Cond):
    cards=[x for group in b[0] for x in group]
    if c.cards_any and not any(x in cards for x in c.cards_any): return False
    if any(x not in cards for x in c.cards_all): return False
    if any(x in cards for x in c.cards_ban): return False
    if c.drop is True and not b[1]: return False
    if c.drop is False and b[1]: return False
    if c.potion_any and (not b[1] or b[2] not in c.potion_any): return False
    return True

def generate_battle(st:State, impact:Influence, asc:int):
    if impact.white: drop=True
    else:
        drop=st.rng.f32()<st.potion
        st.potion += -.1 if drop else .1
    low,high=(7,15) if asc>=3 else (10,20)
    _gold=low+st.rng.integer(high-low+1)+impact.aubergine
    potion=None
    if drop:
        v=st.rng.f32(); rr=3 if v<=.1 else (2 if v<=.35 else 1)
        pool=POTIONS[rr]; potion=pool[st.rng.integer(len(pool))]
    groups=[fast_group(st,asc,impact.candy and st.candy_counter%2==1)]
    for _ in range(impact.prayer): groups.append(fast_group(st,asc,False))
    if impact.candy: st.candy_counter+=1
    return (tuple(tuple(g) for g in groups),drop,potion,_gold)

def baseline(root:int, impact:Influence, conds:list[Cond], asc=10):
    max_b=max((3 if c.battle==0 else c.battle) for c in conds)
    st=State(Rng((root+REWARDS_HASH)&MASK)); battles=[]
    for _ in range(max_b): battles.append(generate_battle(st,impact,asc))
    ok=True
    for c in conds:
        if c.battle==0: ok &= any(match(b,c) for b in battles[:3])
        else: ok &= c.battle<=len(battles) and match(battles[c.battle-1],c)
        if not ok: break
    checkpoint=(st.rng.sig(),round(st.potion,7),round(st.offset,7),st.candy_counter)
    return bool(ok), checkpoint

def streaming(root:int, impact:Influence, conds:list[Cond], asc=10):
    max_b=max((3 if c.battle==0 else c.battle) for c in conds)
    st=State(Rng((root+REWARDS_HASH)&MASK)); any_hit=[False]*len(conds)
    for battle in range(1,max_b+1):
        b=generate_battle(st,impact,asc)
        for i,c in enumerate(conds):
            if c.battle==0:
                if not any_hit[i] and match(b,c): any_hit[i]=True
            elif c.battle==battle:
                if not match(b,c):
                    return False, None, battle
                any_hit[i]=True
    ok=all(any_hit)
    checkpoint=(st.rng.sig(),round(st.potion,7),round(st.offset,7),st.candy_counter)
    return ok, checkpoint, max_b

# Stable targets from the independent fixture pools.
common_target=100
rare_target=300
potion_target=401
cases=[
    [Cond(1,cards_all=(rare_target,),drop=True,potion_any=(potion_target,)), Cond(3,drop=True)],
    [Cond(0,cards_any=(common_target,))],
    [Cond(0,potion_any=(potion_target,))],
    [Cond(2,cards_ban=(rare_target,))],
    [Cond(1,drop=True),Cond(3,cards_any=(common_target,))],
]
impacts=[Influence(),Influence(white=True),Influence(prayer=1),Influence(candy=True),Influence(white=True,prayer=1,candy=True,aubergine=15)]
roots=[0,1,2,3,0x123456789ABCDEF0,0xFFFFFFFFFFFFFFFF]
roots += [((i*0x9E3779B97F4A7C15)^0xD1B54A32D192ED03)&MASK for i in range(1,2049)]

decision_mismatch=accepted_checkpoint_mismatch=0
early_rejects=0
for root in roots:
    for impact in impacts:
        for conds in cases:
            b_ok,b_cp=baseline(root,impact,conds)
            s_ok,s_cp,visited=streaming(root,impact,conds)
            if b_ok!=s_ok: decision_mismatch+=1
            if s_ok and b_cp!=s_cp: accepted_checkpoint_mismatch+=1
            max_b=max((3 if c.battle==0 else c.battle) for c in conds)
            if not s_ok and visited<max_b: early_rejects+=1

print(f'P10ANumericRoots={len(roots)} impacts={len(impacts)} predicateShapes={len(cases)} vectors={len(roots)*len(impacts)*len(cases)}')
print(f'P10AStreamingDecisionMismatch={decision_mismatch}')
print(f'P10AAcceptedCheckpointMismatch={accepted_checkpoint_mismatch}')
print(f'P10AFailureOnlyEarlyRejectObserved={early_rejects}')
print('P10ARawBurnSelected=false RNGCheckpointParityPolicy=NotApplicable_NoRawBurn')
if decision_mismatch or accepted_checkpoint_mismatch or early_rejects==0:
    raise SystemExit(1)
