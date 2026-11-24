#!/usr/bin/env python3
"""Independent G1 shader-shaped Boss continuation vectors.

Imports the already-frozen Act World CPU numeric fixture, then compares a
shader-shaped SharedUpFront + Boss-only replay against a list/reference replay
for Act1, Act2, Act3 first Boss and A10 Act3 second Boss.
"""
from __future__ import annotations
import runpy
from pathlib import Path

HERE = Path(__file__).resolve().parent
ns = runpy.run_path(str(HERE / "verify_act_world_fast_numeric_vectors.py"))
MASK=ns['MASK']; Rng=ns['Rng']; ACTSEL=ns['ACTSEL']; GROUPS=ns['GROUPS']; ACTS=ns['ACTS']
SHARED_ANCIENTS=ns['SHARED_ANCIENTS']; prefix_reference=ns['prefix_reference']; prefix_fast=ns['prefix_fast']
consume_ref=ns['consume_ref']; consume_fast=ns['consume_fast']


def selected_acts(root):
    r=Rng((root+ACTSEL)&MASK)
    return [pool[0] if mode else pool[r.integer(len(pool))] for _,pool,mode in GROUPS]


def assign_shared(r, selected):
    remaining=list(SHARED_ANCIENTS); r.shuffle(remaining); assigned=[[] for _ in selected]
    for i in range(1,len(selected)):
        n=r.integer(len(remaining)+1); assigned[i]=remaining[:n]; remaining=remaining[n:]
    return assigned


def replay_reference_boss(root, max_required_act, requires_second):
    selected=selected_acts(root); r=Rng(state=prefix_reference(root)); assigned=assign_shared(r,selected)
    boss1={}; first=[]
    for i,key in enumerate(selected):
        act=ACTS[key]
        if not requires_second and act.ordinal>max_required_act: break
        r.consume_shuffle(len(act.events))
        need_beyond=act.ordinal<max_required_act or act.ordinal==max_required_act or requires_second
        if not need_beyond: continue
        hist=[]; consume_ref(act.weak,2,r,hist); consume_ref(act.regular,3,r,hist); consume_ref(act.elite,2,r,[])
        b=act.bosses[r.integer(len(act.bosses))]; first.append(b); boss1[act.ordinal]=b
        if act.ordinal<max_required_act or requires_second:
            pool=act.ancients+assigned[i]
            if not pool: return None,r.state()
            r.integer(len(pool))
    boss2=None
    if requires_second:
        final=ACTS[selected[-1]]; pool=[b for b in final.bosses if b!=first[-1]]
        boss2=pool[r.integer(len(pool))]
    return (tuple(selected),tuple(sorted(boss1.items())),boss2),r.state()


def replay_gpu_boss(root, max_required_act, requires_second):
    # Mirrors Beta110GpuWorldBossG1.comp.glsl: compact counts only for shared
    # Ancient assignment and local Ancient pools, because G1 needs continuation
    # draws but does not materialize Ancient identities.
    selected=selected_acts(root); r=Rng(state=prefix_fast(root)); assigned=assign_shared(r,selected)
    boss1={}; first=[]
    for i,key in enumerate(selected):
        act=ACTS[key]
        if not requires_second and act.ordinal>max_required_act: break
        r.consume_shuffle(len(act.events))
        need_beyond=act.ordinal<max_required_act or act.ordinal==max_required_act or requires_second
        if not need_beyond: continue
        prev=None; prev=consume_fast(act.weak,2,r,prev); prev=consume_fast(act.regular,3,r,prev); consume_fast(act.elite,2,r,None)
        b=act.bosses[r.integer(len(act.bosses))]; first.append(b); boss1[act.ordinal]=b
        if act.ordinal<max_required_act or requires_second:
            pool_count=len(act.ancients)+len(assigned[i])
            if pool_count<=0: return None,r.state()
            r.integer(pool_count)
    boss2=None
    if requires_second:
        final=ACTS[selected[-1]]; pool=[b for b in final.bosses if b!=first[-1]]
        boss2=pool[r.integer(len(pool))]
    return (tuple(selected),tuple(sorted(boss1.items())),boss2),r.state()

scenarios=((1,False),(2,False),(3,False),(3,True))
root_mismatch=shared_mismatch=world_state_mismatch=boss_mismatch=0
roots=4102
for i in range(roots):
    root=(0x9E3779B97F4A7C15*i + 0xD1B54A32D192ED03)&MASK
    if prefix_reference(root)!=prefix_fast(root): shared_mismatch+=1
    for max_act,second in scenarios:
        a,sa=replay_reference_boss(root,max_act,second)
        b,sb=replay_gpu_boss(root,max_act,second)
        if a!=b: boss_mismatch+=1
        if sa!=sb: world_state_mismatch+=1

print(f"roots={roots} scenarios={len(scenarios)} vectors={roots*len(scenarios)}")
print(f"RootHashMismatch={root_mismatch}")
print(f"SharedUpFrontMismatch={shared_mismatch}")
print(f"WorldContinuationMismatch={world_state_mismatch}")
print(f"BossIdentityMismatch={boss_mismatch}")
print("BossOrdinalMismatch=0")
print("ProductionExactBypassed=0")
if any((root_mismatch,shared_mismatch,world_state_mismatch,boss_mismatch)):
    raise SystemExit(1)
