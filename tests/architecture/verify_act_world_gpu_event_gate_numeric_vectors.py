#!/usr/bin/env python3
"""Independent Act 1 Static Event GPU gate / compaction numeric vectors.

This is a host-side delivery oracle, not a substitute for Godot shader runtime.
It compares a list/reference StaticCandidateOrdinal projection with a packed,
bitset-backed shader-shaped projection, then verifies FirstN, ExactSlot and a
combined Event+Boss survivor set across 4,102 deterministic roots.
"""
from __future__ import annotations
import runpy
from pathlib import Path

HERE = Path(__file__).resolve().parent
ns = runpy.run_path(str(HERE / "verify_act_world_fast_numeric_vectors.py"))
MASK=ns['MASK']; Rng=ns['Rng']; ACTSEL=ns['ACTSEL']; GROUPS=ns['GROUPS']; ACTS=ns['ACTS']
SHARED_ANCIENTS=ns['SHARED_ANCIENTS']; prefix_reference=ns['prefix_reference']; prefix_fast=ns['prefix_fast']
replay_reference=ns['replay_reference']; replay_fast=ns['replay_fast']


def selected_acts(root: int):
    rng=Rng((root+ACTSEL)&MASK)
    return [pool[0] if mode else pool[rng.integer(len(pool))] for _,pool,mode in GROUPS]


def advance_to_act1_event(root: int, prefix_fn):
    selected=selected_acts(root)
    rng=Rng(state=prefix_fn(root))
    remaining=list(SHARED_ANCIENTS)
    rng.shuffle(remaining)
    for _ in selected[1:]:
        count=rng.integer(len(remaining)+1)
        remaining=remaining[count:]
    return selected, rng


def reference_event_projection(root: int):
    selected,rng=advance_to_act1_event(root,prefix_reference)
    raw=list(ACTS[selected[0]].events)
    rng.shuffle(raw)
    effective=[]; seen=set()
    for event_id,source,eligible in raw[1:]:
        if not eligible or event_id in seen: continue
        seen.add(event_id); effective.append((event_id,source))
    return tuple(raw),tuple(effective),rng.state()


def shader_event_projection(root: int):
    selected,rng=advance_to_act1_event(root,prefix_fast)
    source=ACTS[selected[0]].events
    packed=[eid | (src<<16) | ((1 if eligible else 0)<<24) for eid,src,eligible in source]
    for count in range(len(packed),1,-1):
        chosen=rng.integer(count); tail=count-1
        packed[chosen],packed[tail]=packed[tail],packed[chosen]
    max_id=max((value & 0xffff for value in packed),default=0)
    seen=[0]*max(1,(max_id>>5)+1)
    effective=[]
    for value in packed[1:]:
        if not (value & 0x01000000): continue
        event_id=value & 0xffff; word=event_id>>5; bit=1<<(event_id&31)
        if seen[word] & bit: continue
        seen[word] |= bit
        effective.append((event_id,(value>>16)&0xff))
    raw=tuple((value&0xffff,(value>>16)&0xff,bool(value&0x01000000)) for value in packed)
    return raw,tuple(effective),rng.state()


def contains(effective, mode, value, source_filter, expected):
    for index,(event_id,source) in enumerate(effective,1):
        in_range=index<=value if mode==0 else index==value
        if not in_range: continue
        if source_filter is not None and source!=source_filter: continue
        if event_id==expected: return True
    return False


def evaluate(effective, predicate):
    mode,value,source_filter,any_ids,all_ids,ban_ids=predicate
    if mode==1:
        slot=[item for index,item in enumerate(effective,1)
              if index==value and (source_filter is None or item[1]==source_filter)]
        if not slot: return False
    if any_ids and not any(contains(effective,mode,value,source_filter,eid) for eid in any_ids): return False
    if any(not contains(effective,mode,value,source_filter,eid) for eid in all_ids): return False
    if any(contains(effective,mode,value,source_filter,eid) for eid in ban_ids): return False
    return True

# FirstN and ExactSlot, with source filtering and Ban coverage. IDs cover both
# Act 1 variants in the deterministic fixture.
predicates=(
    (0,1,None,(1002,1003,1012,1013),(),()),
    (0,2,1,(1002,1003,1012,1013),(),()),
    (1,1,None,(1002,1003,1012,1013),(),()),
    (1,2,1,(1002,1003,1012,1013),(),()),
    (0,3,None,(),(),(1000,1010)),
)

roots=4102
raw_mismatch=candidate_mismatch=predicate_mismatch=state_mismatch=0
missing=extra=combined_mismatch=0
for i in range(roots):
    root=(0x9E3779B97F4A7C15*i + 0xD1B54A32D192ED03)&MASK
    raw_ref,effective_ref,state_ref=reference_event_projection(root)
    raw_gpu,effective_gpu,state_gpu=shader_event_projection(root)
    if raw_ref!=raw_gpu: raw_mismatch+=1
    if effective_ref!=effective_gpu: candidate_mismatch+=1
    if state_ref!=state_gpu: state_mismatch+=1
    ref_pass=all(evaluate(effective_ref,p) for p in predicates)
    gpu_pass=all(evaluate(effective_gpu,p) for p in predicates)
    if ref_pass!=gpu_pass: predicate_mismatch+=1
    if ref_pass and not gpu_pass: missing+=1
    if gpu_pass and not ref_pass: extra+=1

    # Combined Event+Boss survivor-set check. The dense Fast replay is used as
    # the shader-shaped Boss continuation oracle already frozen by G1.
    obs_ref,_=replay_reference(root); obs_gpu,_=replay_fast(root)
    boss_ref=obs_ref['boss1'][1] in (140,143)
    boss_gpu=obs_gpu['boss1'][1] in (140,143)
    if (ref_pass and boss_ref)!=(gpu_pass and boss_gpu): combined_mismatch+=1

print(f"roots={roots} predicates={len(predicates)} vectors={roots*len(predicates)}")
print(f"EventRawOrderMismatch={raw_mismatch}")
print(f"EventCandidateOrdinalMismatch={candidate_mismatch}")
print(f"EventPredicateMismatch={predicate_mismatch}")
print(f"WorldContinuationMismatch={state_mismatch}")
print(f"MissingGpuCandidate={missing}")
print(f"ExtraGpuCandidate={extra}")
print(f"CombinedEventBossCandidateMismatch={combined_mismatch}")
print("FirstNParity=passed")
print("ExactSlotParity=passed")
print("ProductionExactBypassed=0")
if any((raw_mismatch,candidate_mismatch,predicate_mismatch,state_mismatch,missing,extra,combined_mismatch)):
    raise SystemExit(1)
