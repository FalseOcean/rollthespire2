#!/usr/bin/env python3
"""Independent Act 2 Static Event GPU replay/gate/compaction numeric vectors.

The reference path materializes list objects. The shader-shaped path uses packed
candidate words, a first-occurrence bitset and the same variable-retry encounter
continuation. Both replay the full Act 1 World sequence before Act 2 Event.
"""
from __future__ import annotations
import runpy
from pathlib import Path

HERE=Path(__file__).resolve().parent
ns=runpy.run_path(str(HERE/'verify_act_world_fast_numeric_vectors.py'))
MASK=ns['MASK']; Rng=ns['Rng']; ACTSEL=ns['ACTSEL']; GROUPS=ns['GROUPS']; ACTS=ns['ACTS']
SHARED_ANCIENTS=ns['SHARED_ANCIENTS']; prefix_reference=ns['prefix_reference']; prefix_fast=ns['prefix_fast']
consume_ref=ns['consume_ref']; consume_fast=ns['consume_fast']


def selected_acts(root:int):
    rng=Rng((root+ACTSEL)&MASK)
    return [pool[0] if mode else pool[rng.integer(len(pool))] for _,pool,mode in GROUPS]


def prepare(root:int,prefix_fn,fast:bool):
    selected=selected_acts(root)
    rng=Rng(state=prefix_fn(root))
    remaining=list(SHARED_ANCIENTS); rng.shuffle(remaining)
    assigned=[[] for _ in selected]
    for i in range(1,len(selected)):
        n=rng.integer(len(remaining)+1); assigned[i]=remaining[:n]; remaining=remaining[n:]
    for i,key in enumerate(selected):
        act=ACTS[key]
        if act.ordinal==2:
            return selected,rng,act
        # Full prior-Act continuation: Event -> encounters -> Boss -> Ancient.
        event=list(act.events); rng.shuffle(event)
        if fast:
            prev=None
            prev=consume_fast(act.weak,2,rng,prev)
            prev=consume_fast(act.regular,3,rng,prev)
            consume_fast(act.elite,2,rng,None)
        else:
            history=[]
            consume_ref(act.weak,2,rng,history)
            consume_ref(act.regular,3,rng,history)
            consume_ref(act.elite,2,rng,[])
        rng.integer(len(act.bosses))
        rng.integer(len(act.ancients)+len(assigned[i]))
    raise AssertionError('Act 2 not selected')


def reference(root:int):
    _,rng,act=prepare(root,prefix_reference,False)
    raw=list(act.events); rng.shuffle(raw); post=rng.state()
    effective=[]; seen=set()
    for event_id,source,eligible in raw[1:]:
        if not eligible or event_id in seen: continue
        seen.add(event_id); effective.append((event_id,source))
    return tuple(raw),tuple(effective),post


def shader(root:int):
    _,rng,act=prepare(root,prefix_fast,True)
    packed=[eid|(src<<16)|((1 if eligible else 0)<<24) for eid,src,eligible in act.events]
    for count in range(len(packed),1,-1):
        chosen=rng.integer(count); tail=count-1
        packed[chosen],packed[tail]=packed[tail],packed[chosen]
    post=rng.state()
    max_id=max((v&0xffff for v in packed),default=0)
    seen=[0]*max(1,(max_id>>5)+1); effective=[]
    for value in packed[1:]:
        if not value&0x01000000: continue
        event_id=value&0xffff; word=event_id>>5; bit=1<<(event_id&31)
        if seen[word]&bit: continue
        seen[word]|=bit; effective.append((event_id,(value>>16)&0xff))
    raw=tuple((v&0xffff,(v>>16)&0xff,bool(v&0x01000000)) for v in packed)
    return raw,tuple(effective),post


def contains(effective,mode,value,source_filter,expected):
    for ordinal,(event_id,source) in enumerate(effective,1):
        in_range=ordinal<=value if mode==0 else ordinal==value
        if in_range and (source_filter is None or source==source_filter) and event_id==expected:return True
    return False


def evaluate(effective,predicate):
    mode,value,source_filter,any_ids,all_ids,ban_ids=predicate
    if mode==1 and not any(ordinal==value and (source_filter is None or source==source_filter)
                           for ordinal,(_,source) in enumerate(effective,1)): return False
    if any_ids and not any(contains(effective,mode,value,source_filter,eid) for eid in any_ids):return False
    if any(not contains(effective,mode,value,source_filter,eid) for eid in all_ids):return False
    if any(contains(effective,mode,value,source_filter,eid) for eid in ban_ids):return False
    return True

# Selective Act 1 gate: prove the compact Act 1 survivor count becomes the exact Act 2 input.
act1_predicates=((1,1,None,(1002,1012),(),()),)
act2_predicates=(
    (0,1,None,(2000,2001,2003),(),()),
    (0,2,1,(2000,2001,2003),(),()),
    (1,1,None,(2000,2001,2003),(),()),
    (1,2,1,(2000,2001,2003),(),()),
    (0,3,None,(),(),(2002,)),
)

def act1_effective(root:int,fast:bool):
    selected=selected_acts(root); rng=Rng(state=(prefix_fast if fast else prefix_reference)(root))
    remaining=list(SHARED_ANCIENTS);rng.shuffle(remaining)
    for _ in selected[1:]:
        n=rng.integer(len(remaining)+1);remaining=remaining[n:]
    raw=list(ACTS[selected[0]].events);rng.shuffle(raw)
    out=[];seen=set()
    for eid,src,eligible in raw[1:]:
        if eligible and eid not in seen:seen.add(eid);out.append((eid,src))
    return tuple(out)

roots=4102
raw_mismatch=ordinal_mismatch=state_mismatch=predicate_mismatch=missing=extra=0
act1_input=roots; act1_reference_passed=act1_gpu_passed=0
act2_reference_input=act2_gpu_input=act2_reference_passed=act2_gpu_passed=0
for i in range(roots):
    root=(0x9E3779B97F4A7C15*i+0xD1B54A32D192ED03)&MASK
    act1_ref=all(evaluate(act1_effective(root,False),p) for p in act1_predicates)
    act1_gpu=all(evaluate(act1_effective(root,True),p) for p in act1_predicates)
    act1_reference_passed+=act1_ref;act1_gpu_passed+=act1_gpu
    if act1_ref: act2_reference_input+=1
    if act1_gpu: act2_gpu_input+=1
    raw_ref,eff_ref,state_ref=reference(root)
    raw_gpu,eff_gpu,state_gpu=shader(root)
    raw_mismatch+=raw_ref!=raw_gpu
    ordinal_mismatch+=eff_ref!=eff_gpu
    state_mismatch+=state_ref!=state_gpu
    ref_pass=act1_ref and all(evaluate(eff_ref,p) for p in act2_predicates)
    gpu_pass=act1_gpu and all(evaluate(eff_gpu,p) for p in act2_predicates)
    act2_reference_passed+=ref_pass;act2_gpu_passed+=gpu_pass
    predicate_mismatch+=ref_pass!=gpu_pass
    missing+=ref_pass and not gpu_pass
    extra+=gpu_pass and not ref_pass

print(f'roots={roots} predicates={len(act2_predicates)} vectors={roots*len(act2_predicates)}')
print(f'Act1EventStageInput={act1_input} Act1ReferencePassed={act1_reference_passed} Act1GpuPassed={act1_gpu_passed}')
print(f'Act2StageReferenceInput={act2_reference_input} Act2StageGpuInput={act2_gpu_input}')
print(f'Act2ReferencePassed={act2_reference_passed} Act2GpuPassed={act2_gpu_passed}')
print(f'Act2EventRawOrderMismatch={raw_mismatch}')
print(f'Act2EventCandidateOrdinalMismatch={ordinal_mismatch}')
print(f'Act2ContinuationStateMismatch={state_mismatch}')
print(f'Act2EventPredicateMismatch={predicate_mismatch}')
print(f'MissingGpuCandidate={missing}')
print(f'ExtraGpuCandidate={extra}')
print(f'Act2StageInputMatchesPriorSurvivors={str(act2_gpu_input==act1_gpu_passed).lower()}')
print(f'CompactionInputOutputConsistent={str(act2_gpu_passed<=act2_gpu_input<=act1_input).lower()}')
print('ProductionExactBypassed=0')
if any((raw_mismatch,ordinal_mismatch,state_mismatch,predicate_mismatch,missing,extra)) or act2_gpu_input!=act1_gpu_passed:
    raise SystemExit(1)
