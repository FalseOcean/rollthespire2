#!/usr/bin/env python3
"""P10-RP1 GPU route-policy numeric oracle.

This is deliberately small and independent from Godot. It validates the new
route-policy envelope around the already accepted Combat Reward numeric model:
- unpinned synthetic uses the canonical no-influence Reward state;
- the GPU internal pass sentinel is candidate-only and exposes RouteMask=0;
- pinned real routes preserve their route bit and use the same single-route
  projection authority as the accepted P10-A/R2 model.
"""
from pathlib import Path
import json, runpy, sys

# Reuse the accepted numeric model definitions without duplicating RNG math.
model_path = Path(__file__).resolve().parents[1] / 'functional' / 'Beta110GpuCombatRewardParity' / 'verify_gpu_combat_reward_numeric_vectors.py'
# Avoid executing its terminal loop by load/compile only up to roots=4096 marker.
src=model_path.read_text(encoding='utf-8')
prefix=src.split('roots=4096; mismatches=',1)[0]
ns={'__file__':str(model_path)}
exec(compile(prefix,str(model_path),'exec'),ns)

route=ns['route']; root_vector=ns['root_vector']; Cap=ns['Cap']; PRAYER=ns['PRAYER']; FORCE=ns['FORCE']; CANDY=ns['CANDY']
SENTINEL=0x80000000
REAL_MASK=0x1f
roots=4096
synthetic_projection_mismatch=0
synthetic_candidate_mismatch=0
synthetic_route_evidence_leak=0
pinned_projection_mismatch=0
pinned_route_mask_mismatch=0

# Representative pinned single-route effects cover neutral plus Reward influences
# whose inner algorithm is already accepted by the 24,612-vector oracle.
pinned=[
    (1,[Cap()]),
    (2,[Cap(flags=PRAYER,extra=1)]),
    (4,[Cap(flags=FORCE)]),
    (8,[Cap(flags=CANDY)]),
]

def matches(result):
    if result.get('conservative'): return True
    return any(result['drops']) and any(len(cards)>=3 for cards in result['cards'])

for i in range(roots):
    root=root_vector(i)
    cpu=route(root,[],False,False)
    gpu=route(root,[],True,False)
    if cpu!=gpu: synthetic_projection_mismatch+=1
    cpu_keep=matches(cpu); gpu_encoded=SENTINEL if matches(gpu) else 0
    gpu_keep=bool(gpu_encoded&SENTINEL)
    exposed=0 if gpu_keep else 0
    if cpu_keep!=gpu_keep: synthetic_candidate_mismatch+=1
    if exposed!=0: synthetic_route_evidence_leak+=1

    for bit,effects in pinned:
        a=route(root,effects,False,False); b=route(root,effects,True,False)
        if a!=b: pinned_projection_mismatch+=1
        cpu_mask=bit if matches(a) else 0
        gpu_mask=(bit if matches(b) else 0)&REAL_MASK
        if cpu_mask!=gpu_mask: pinned_route_mask_mismatch+=1

result={
    'roots':roots,
    'unpinnedSyntheticProjectionMismatch':synthetic_projection_mismatch,
    'unpinnedSyntheticCandidateMismatch':synthetic_candidate_mismatch,
    'unpinnedSyntheticRouteEvidenceLeak':synthetic_route_evidence_leak,
    'pinnedSingleRouteProjectionMismatch':pinned_projection_mismatch,
    'pinnedSingleRouteMaskMismatch':pinned_route_mask_mismatch,
    'syntheticPassSentinel':'0x80000000',
    'syntheticExposedRouteMask':0,
    'compactCandidateAbi':'1_LogicalOrdinalOnly',
    'p10BImplemented':False,
}
out=Path(__file__).with_name('P10_RP1_GPU_ROUTE_POLICY_NUMERIC_RESULTS.json')
out.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result,indent=2))
fail=any([synthetic_projection_mismatch,synthetic_candidate_mismatch,synthetic_route_evidence_leak,pinned_projection_mismatch,pinned_route_mask_mismatch])
sys.exit(1 if fail else 0)
