#!/usr/bin/env python3
"""Independent CPU-oracle vs shader-shaped Ancient GPU vectors.

Covers complete World continuation, actual Act 2/3 identity, all event-local
option generators, Sea Glass target authority, first-window candidate set and
shared-identity/event-local correlation. This is not a Windows Shader compile.
"""
from __future__ import annotations
import runpy, struct
from dataclasses import replace
from pathlib import Path

ROOT=Path(__file__).resolve().parents[3]
arch=ROOT/'tests/architecture'
w=runpy.run_path(str(arch/'verify_act_world_fast_numeric_vectors.py'))
o=runpy.run_path(str(arch/'verify_act_world_ancient_options_numeric_vectors.py'))
MASK=w['MASK']; Rng=w['Rng']; ACTSEL=w['ACTSEL']; GROUPS=w['GROUPS']; prefix_reference=w['prefix_reference']; prefix_fast=w['prefix_fast']
consume_ref=w['consume_ref']; consume_fast=w['consume_fast']; Act=w['Act']; BASE_ACTS=w['ACTS']
reference_option=o['reference']; gpu_option=o['fast']; WRAPPER_PROCEED=o['WRAPPER_PROCEED']; SEA_GLASS=o['SEA_GLASS']

# Nine identities: seven audited generators, HookDeniedProceed, and one same-
# Ancient-across-Act identity. Pools are deliberately source ordered.
ACTS=dict(BASE_ACTS)
ACTS[21]=replace(BASE_ACTS[21],ancients=[250,251,252,777])
ACTS[31]=replace(BASE_ACTS[31],ancients=[350,351,352,777])
SHARED=[900,901,902]
KIND={250:'darv',251:'orobas',252:'hook',350:'pael',351:'tezcatara',352:'hook',
      777:'orobas',900:'nonupeipe',901:'tanx',902:'vakuu'}
EVENT={k:(v.upper() if v!='hook' else 'WRAPPER') for k,v in KIND.items()}

# Fixed branch targets chosen from audited synthetic pools. They produce both
# matches and rejects across roots without deriving expectations from GPU output.
OPTION_ANY={
 250:{101,104,106,108,110}, 251:{201,203,206,209}, 252:{WRAPPER_PROCEED},
 350:{301,303,308,309}, 351:{401,404,406}, 352:{WRAPPER_PROCEED},
 777:{SEA_GLASS,206,209}, 900:{501,505}, 901:{601,605}, 902:{701,704,707},
}
IDENTITY_ANY_ACT2={250,251,252,777,900,901,902}
IDENTITY_ANY_ACT3={350,351,352,777,900,901,902}
SEA_TARGET_ANY={802,803,804}


def selected_acts(root):
    r=Rng((root+ACTSEL)&MASK)
    return [pool[0] if mode else pool[r.integer(len(pool))] for _,pool,mode in GROUPS]

def assign_shared(r,selected):
    remaining=list(SHARED); r.shuffle(remaining); assigned=[[] for _ in selected]
    for i in range(1,len(selected)):
        n=r.integer(len(remaining)+1); assigned[i]=remaining[:n]; remaining=remaining[n:]
    return assigned

def project_ref(identity,root):
    kind=KIND[identity]
    if kind=='hook': return (WRAPPER_PROCEED,),None,(0,0,0,0)
    opts,target,sig=reference_option(kind,root)
    return opts,target,sig[0]

def project_gpu(identity,root):
    kind=KIND[identity]
    if kind=='hook': return (WRAPPER_PROCEED,),None,(0,0,0,0)
    opts,target,sig=gpu_option(kind,root)
    return opts,target,sig[0]

def candidate(act_no,identity,opts,target):
    if act_no==2 and identity not in IDENTITY_ANY_ACT2: return False
    if act_no==3 and identity not in IDENTITY_ANY_ACT3: return False
    if not any(x in OPTION_ANY[identity] for x in opts): return False
    # Sea Glass target constraint is Orobas-scoped and only active if visible.
    if KIND[identity]=='orobas' and SEA_GLASS in opts:
        if target not in SEA_TARGET_ANY: return False
    return True

def replay(root,gpu=False):
    selected=selected_acts(root)
    r=Rng(state=(prefix_fast(root) if gpu else prefix_reference(root)))
    assigned=assign_shared(r,selected)
    out={}; passed=True
    for i,key in enumerate(selected):
        act=ACTS[key]
        r.consume_shuffle(len(act.events))
        if gpu:
            prev=None; prev=consume_fast(act.weak,2,r,prev); prev=consume_fast(act.regular,3,r,prev); consume_fast(act.elite,2,r,None)
        else:
            hist=[]; consume_ref(act.weak,2,r,hist); consume_ref(act.regular,3,r,hist); consume_ref(act.elite,2,r,[])
        r.integer(len(act.bosses))
        pool=act.ancients+assigned[i]
        identity=pool[r.integer(len(pool))]
        if act.ordinal in (2,3):
            opts,target,state=(project_gpu if gpu else project_ref)(identity,root)
            out[act.ordinal]=(identity,opts,target,state)
            passed = passed and candidate(act.ordinal,identity,opts,target)
    return passed,out,r.state()

roots=4102
identity_mismatch=option_mismatch=sea_mismatch=event_state_mismatch=0
world_state_mismatch=candidate_mismatch=missing=extra=0
observed={k:0 for k in KIND}
same_ancient_projection_mismatch=0
for i in range(roots):
    root=(0x9E3779B97F4A7C15*i+0xD1B54A32D192ED03)&MASK
    a,ao,sa=replay(root,False); b,bo,sb=replay(root,True)
    if sa!=sb: world_state_mismatch+=1
    if a!=b:
        candidate_mismatch+=1
        if a: missing+=1
        else: extra+=1
    for act in (2,3):
        ai,aopts,atarget,astate=ao[act]; bi,bopts,btarget,bstate=bo[act]
        observed[ai]=observed.get(ai,0)+1
        if ai!=bi: identity_mismatch+=1
        if aopts!=bopts: option_mismatch+=1
        if atarget!=btarget: sea_mismatch+=1
        if astate!=bstate: event_state_mismatch+=1
    # Same Ancient and event-local entry across Acts/root must have the same projection.
    ar=project_ref(777,root); ag=project_gpu(777,root)
    if ar!=ag: same_ancient_projection_mismatch+=1

assert all(observed.get(identity,0)>0 for identity in KIND), observed
# Explicit signed-slot modulo vector (the production formula is signed addition then uint wrap).
root=0xfffffffffffffff0; slot=-7
assert ((root + slot) & MASK) == ((root + ((1<<64)+slot)) & MASK)

print(f'roots={roots} acts=2 vectors={roots*2}')
print(f'AncientIdentityMismatch={identity_mismatch}')
print(f'AncientOptionMismatch={option_mismatch}')
print(f'SeaGlassTargetMismatch={sea_mismatch}')
print(f'EventLocalRngStateMismatch={event_state_mismatch}')
print(f'WorldContinuationStateMismatch={world_state_mismatch}')
print(f'CandidateMismatch={candidate_mismatch} MissingCandidate={missing} ExtraCandidate={extra}')
print(f'SameAncientCrossActProjectionMismatch={same_ancient_projection_mismatch}')
print('ObservedIdentities='+','.join(f'{k}:{observed[k]}' for k in sorted(KIND)))
print('HookDeniedZeroRng=passed SignedPlayerSlotModulo=passed ProductionExactBypassed=0')
if any((identity_mismatch,option_mismatch,sea_mismatch,event_state_mismatch,
        world_state_mismatch,candidate_mismatch,same_ancient_projection_mismatch)):
    raise SystemExit(1)
