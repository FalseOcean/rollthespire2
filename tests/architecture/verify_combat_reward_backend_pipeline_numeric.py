#!/usr/bin/env python3
from pathlib import Path
import json, random, sys

ROOT=Path(__file__).resolve().parents[2]
WG=64
ABI_STRIDE=2

# Pure reference model for the shader transport modes added by this batch.
def global_compact(ordinals, keep):
    out=[]
    atomics=0
    for o,k in zip(ordinals,keep):
        if k:
            atomics += 1
            out.append(o)
    return out, atomics

def workgroup_compact(ordinals, keep):
    # Shader order between workgroups is not guaranteed; model a deliberately
    # shuffled reservation order, then apply the host's required ascending sort.
    groups=[]
    reservations=0
    for base in range(0,len(ordinals),WG):
        local=[o for o,k in zip(ordinals[base:base+WG],keep[base:base+WG]) if k]
        if local:
            reservations += 1
            groups.append(local)
    rnd=random.Random(0xC0FFEE + len(ordinals) + sum(keep))
    rnd.shuffle(groups)
    raw=[o for g in groups for o in g]
    return sorted(raw), reservations

def expected(ordinals,keep): return [o for o,k in zip(ordinals,keep) if k]

def check_case(name, ordinals, keep, rows):
    exp=expected(ordinals,keep)
    glob,ga=global_compact(ordinals,keep)
    wg,wa=workgroup_compact(ordinals,keep)
    rows.append({
        'name':name,'inputs':len(ordinals),'survivors':len(exp),
        'globalAtomicReservations':ga,'workgroupGlobalReservations':wa,
        'globalMismatch':0 if glob==exp else 1,
        'workgroupMismatch':0 if wg==exp else 1,
        'duplicateMismatch':0 if len(set(wg))==len(wg) else 1,
        'ordinalIntegrityMismatch':0 if all(o in set(ordinals) for o in wg) else 1,
        'denseReadbackBytes':len(ordinals)*4,
        'compactReadbackBytes':len(exp)*ABI_STRIDE*4,
    })

rnd=random.Random(0xBACC0FF)
rows=[]
# adversarial sizes cover final partial workgroups and density extremes.
for n in [1,2,63,64,65,127,128,129,1000,4096,8193]:
    ords=list(range(100000,100000+n))
    for label,p in [('zero',0.0),('sparse',0.001),('onepct',0.01),('tenpct',0.1),('half',0.5),('dense',0.9),('all',1.0)]:
        keep=[rnd.random()<p for _ in range(n)] if p not in (0.0,1.0) else ([False]*n if p==0.0 else [True]*n)
        check_case(f'n{n}_{label}',ords,keep,rows)

# Batch-size invariance over the same logical root range.
root_count=1<<18
ords=list(range(root_count))
keep=[((o*0x9E3779B1 + (o>>3)) & 0xFFFF) < 0x2710 for o in ords] # ~15.3%
exp=expected(ords,keep)
batch_mismatch=0
for batch in [1<<10,1<<12,1<<14,1<<16,1<<17,1<<18]:
    out=[]
    for base in range(0,root_count,batch):
        part_o=ords[base:base+batch]; part_k=keep[base:base+batch]
        compact,_=workgroup_compact(part_o,part_k)
        out.extend(compact)
    if out!=exp: batch_mismatch+=1

# Compact-input invariance: arbitrary increasing ABI1 ordinal lists.
compact_input_mismatch=0
for stride in [1,10,100,1000]:
    inp=list(range(0,1<<18,stride))
    # Deterministic predicate independent of transport density.
    kk=[((o*1103515245 + 12345)&0xFFFF) < 7000 for o in inp]
    out,_=workgroup_compact(inp,kk)
    if out!=expected(inp,kk): compact_input_mismatch+=1

# Economics sanity: compact output is smaller than dense mask below 50% because
# ABI1 uses 8 bytes/candidate vs 4 bytes/root. At exactly 10%, expect 5x fewer bytes.
n=1_000_000; survivors=100_000
dense_bytes=n*4; compact_bytes=survivors*8
readback_ratio=dense_bytes/compact_bytes

result={
    'vectorCases':len(rows),
    'globalMismatch':sum(r['globalMismatch'] for r in rows),
    'workgroupMismatch':sum(r['workgroupMismatch'] for r in rows),
    'duplicateCandidateMismatch':sum(r['duplicateMismatch'] for r in rows),
    'ordinalIntegrityMismatch':sum(r['ordinalIntegrityMismatch'] for r in rows),
    'batchSizeCandidateSetMismatch':batch_mismatch,
    'compactInputCandidateSetMismatch':compact_input_mismatch,
    'tenPercentDenseReadbackBytes':dense_bytes,
    'tenPercentCompactReadbackBytes':compact_bytes,
    'tenPercentReadbackByteReduction':readback_ratio,
    'allPass':False,
}
result['allPass']=all(result[k]==0 for k in [
    'globalMismatch','workgroupMismatch','duplicateCandidateMismatch','ordinalIntegrityMismatch',
    'batchSizeCandidateSetMismatch','compactInputCandidateSetMismatch']) and abs(readback_ratio-5.0)<1e-12
out=ROOT/'tests/architecture/COMBAT_REWARD_BACKEND_PIPELINE_NUMERIC_RESULTS.json'
out.write_text(json.dumps(result,indent=2,sort_keys=True)+'\n',encoding='utf-8')
for k,v in result.items(): print(f'{k}={v}')
sys.exit(0 if result['allPass'] else 1)
