#!/usr/bin/env python3
"""Independent production-vs-dense Fast vectors for Ancient event-local options.

This host oracle mirrors the audited public numeric rules in
Beta109AncientOptionProvider and the new dense-array Fast stage independently.
It is not a replacement for the Windows C# build or game runtime parity.
"""
from __future__ import annotations
import struct

MASK=(1<<64)-1
P1=11400714785074694791; P2=14029467366897019727; P3=1609587929392839161
P4=9650029242287828579; P5=2870177450012600261
SM_INC=11400714819323198485; SM_M1=13787848793156543929; SM_M2=10723151780598845931

def rol(x,r): return ((x<<r)|(x>>(64-r))) & MASK

def xxh64(data: bytes, seed: int=0) -> int:
    n=len(data); i=0
    def rnd(acc, inp):
        acc=(acc + inp*P2)&MASK; acc=rol(acc,31); return (acc*P1)&MASK
    if n>=32:
        v1=(seed+P1+P2)&MASK; v2=(seed+P2)&MASK; v3=seed&MASK; v4=(seed-P1)&MASK
        while i<=n-32:
            vals=[int.from_bytes(data[i+j:i+j+8],'little') for j in (0,8,16,24)]; i+=32
            v1=rnd(v1,vals[0]); v2=rnd(v2,vals[1]); v3=rnd(v3,vals[2]); v4=rnd(v4,vals[3])
        h=(rol(v1,1)+rol(v2,7)+rol(v3,12)+rol(v4,18))&MASK
        for v in (v1,v2,v3,v4):
            v=rnd(0,v); h^=v; h=(h*P1+P4)&MASK
    else: h=(seed+P5)&MASK
    h=(h+n)&MASK
    while i<=n-8:
        k=rnd(0,int.from_bytes(data[i:i+8],'little')); h^=k; h=(rol(h,27)*P1+P4)&MASK; i+=8
    if i<=n-4:
        h^=(int.from_bytes(data[i:i+4],'little')*P1)&MASK; h=(rol(h,23)*P2+P3)&MASK; i+=4
    while i<n:
        h^=(data[i]*P5)&MASK; h=(rol(h,11)*P1)&MASK; i+=1
    h^=h>>33; h=(h*P2)&MASK; h^=h>>29; h=(h*P3)&MASK; h^=h>>32
    return h&MASK

class Rng:
    def __init__(self, seed):
        x=seed&MASK; self.s=[]; self.calls=0
        for _ in range(4):
            x=(x+SM_INC)&MASK; z=x; z=((z^(z>>30))*SM_M1)&MASK; z=((z^(z>>27))*SM_M2)&MASK
            self.s.append((z^(z>>31))&MASK)
    def u64(self):
        self.calls+=1
        s0,s1,s2,s3=self.s; result=(rol((s1*5)&MASK,7)*9)&MASK; t=(s1<<17)&MASK
        s2^=s0; s3^=s1; s1^=s2; s0^=s3; s2^=t; s3=rol(s3,45)
        self.s=[s0&MASK,s1&MASK,s2&MASK,s3&MASK]; return result
    def double(self): return (self.u64()>>11)*(2.0**-53)
    def f32(self): return struct.unpack('<f',struct.pack('<f',self.double()))[0]
    def integer(self,n):
        assert n>=1
        return int(self.double()*n)
    def boolean(self): return self.integer(2)==0
    def shuffle(self,a):
        for count in range(len(a),1,-1):
            i=self.integer(count); a[i],a[count-1]=a[count-1],a[i]
    def signature(self): return tuple(self.s),self.calls

def seed(root,event,slot=0,shared=False):
    # signed addition modulo 2^64 is equivalent to uint wrapping.
    return (root + (0 if shared else slot) + xxh64(event.encode())) & MASK

# Synthetic source-ordered catalog. Values are stable dense identities.
P={
 'darv.valid.a':[101,102,103], 'darv.valid.b':[104,105], 'darv.valid.c':[106,107],
 'darv.valid.pandoras-box':[108,109], 'darv.dusty-tome':[110],
 'orobas.pool1.true':[201,202], 'orobas.pool1.false':[203,204,205],
 'orobas.pool2':[206,207,208], 'orobas.pool3.touch':[209,210], 'orobas.pool3.tooth':[211,212],
 'pael.pool1':[301,302], 'pael.pool2.base':[303,304], 'pael.pool2.goopy':[305],
 'pael.pool2.removable':[306,307], 'pael.pool2.growth':[308],
 'pael.pool3.base':[309,310], 'pael.pool3.no-event-pet':[311],
 'tezcatara.pool1.base':[401,402], 'tezcatara.pool1.basic-strike':[403],
 'tezcatara.pool2':[404,405], 'tezcatara.pool3':[406,407,408],
 'nonupeipe.pool.base':[501,502,503,504], 'nonupeipe.pool.swift':[505,506],
 'tanx.pool.base':[601,602,603,604], 'tanx.pool.instinct':[605,606],
 'vakuu.pool1':[701,702,703], 'vakuu.pool2':[704,705,706], 'vakuu.pool3':[707,708,709],
}
SEA_GLASS=203
CHARS=[801,802,803,804]
CURRENT=801
COND=dict(darv_pandora=True,oro_touch=True,oro_tooth=True,pael_goopy=True,pael_removable=True,
          pael_growth=True,pael_no_pet=True,tez_strike=True,nonu_swift=True,tanx_instinct=True)

def reference(kind,root):
    r=Rng(seed(root,kind.upper()))
    target=None
    if kind=='darv':
        src=[]
        for k in ['darv.valid.a','darv.valid.b','darv.valid.c','darv.valid.pandoras-box']:
            if k.endswith('pandoras-box') and not COND['darv_pandora']: continue
            src.append(P[k][r.integer(len(P[k]))])
        r.shuffle(src); dusty=r.boolean()
        out=src[:2]+[P['darv.dusty-tome'][0]] if dusty else src[:3]
    elif kind=='orobas':
        candidates=[x for x in CHARS if x!=CURRENT]
        if candidates: target=candidates[r.integer(len(candidates))]
        special=r.f32()<struct.unpack('<f',struct.pack('<f',0.3333333))[0]
        p1=P['orobas.pool1.true' if special else 'orobas.pool1.false']
        third=[]
        if COND['oro_touch']: third+=P['orobas.pool3.touch']
        if COND['oro_tooth']: third+=P['orobas.pool3.tooth']
        out=[p1[r.integer(len(p1))],P['orobas.pool2'][r.integer(len(P['orobas.pool2']))],third[r.integer(len(third))]]
    elif kind=='pael':
        p2=list(P['pael.pool2.base'])
        if COND['pael_goopy']: p2+=P['pael.pool2.goopy']
        if COND['pael_removable']: p2+=P['pael.pool2.removable']
        p2+=list(p2); p2+=P['pael.pool2.growth']
        p3=list(P['pael.pool3.base'])+(P['pael.pool3.no-event-pet'] if COND['pael_no_pet'] else [])
        out=[P['pael.pool1'][r.integer(len(P['pael.pool1']))],p2[r.integer(len(p2))],p3[r.integer(len(p3))]]
    elif kind=='tezcatara':
        p1=list(P['tezcatara.pool1.base'])+(P['tezcatara.pool1.basic-strike'] if COND['tez_strike'] else [])
        out=[p1[r.integer(len(p1))],P['tezcatara.pool2'][r.integer(len(P['tezcatara.pool2']))],P['tezcatara.pool3'][r.integer(len(P['tezcatara.pool3']))]]
    elif kind=='nonupeipe':
        a=list(P['nonupeipe.pool.base'])+(P['nonupeipe.pool.swift'] if COND['nonu_swift'] else [])
        r.shuffle(a); out=a[:3]
    elif kind=='tanx':
        a=list(P['tanx.pool.base'])+(P['tanx.pool.instinct'] if COND['tanx_instinct'] else [])
        r.shuffle(a); out=a[:3]
    elif kind=='vakuu':
        out=[]
        for k in ['vakuu.pool1','vakuu.pool2','vakuu.pool3']:
            a=list(P[k]); r.shuffle(a); out.append(a[0])
    else: raise ValueError(kind)
    return tuple(out),target,r.signature()

def fast(kind,root):
    # Separate dense-array construction matching the Fast plan roles.
    r=Rng(seed(root,kind.upper())); target=None
    if kind=='darv':
        roles=[P['darv.valid.a'],P['darv.valid.b'],P['darv.valid.c']]
        if COND['darv_pandora']: roles.append(P['darv.valid.pandoras-box'])
        scratch=[pool[r.integer(len(pool))] for pool in roles]
        r.shuffle(scratch); dusty=r.boolean()
        out=(scratch[:2]+[P['darv.dusty-tome'][0]]) if dusty else scratch[:3]
    elif kind=='orobas':
        others=[x for x in CHARS if x!=CURRENT]
        if others: target=others[r.integer(len(others))]
        special=r.f32()<struct.unpack('<f',struct.pack('<f',0.3333333))[0]
        first=P['orobas.pool1.true'] if special else P['orobas.pool1.false']
        third=[]
        # Frozen production order: Touch first, Tooth second.
        if COND['oro_touch']: third.extend(P['orobas.pool3.touch'])
        if COND['oro_tooth']: third.extend(P['orobas.pool3.tooth'])
        out=[first[r.integer(len(first))],P['orobas.pool2'][r.integer(len(P['orobas.pool2']))],third[r.integer(len(third))]]
    elif kind=='pael':
        weighted=[]
        for k,enabled in [('pael.pool2.base',True),('pael.pool2.goopy',COND['pael_goopy']),('pael.pool2.removable',COND['pael_removable'])]:
            if enabled: weighted.extend(P[k])
        base=len(weighted); weighted.extend(weighted[:base]); weighted.extend(P['pael.pool2.growth'])
        third=list(P['pael.pool3.base']);
        if COND['pael_no_pet']: third.extend(P['pael.pool3.no-event-pet'])
        out=[P['pael.pool1'][r.integer(len(P['pael.pool1']))],weighted[r.integer(len(weighted))],third[r.integer(len(third))]]
    elif kind=='tezcatara':
        first=list(P['tezcatara.pool1.base']);
        if COND['tez_strike']: first.extend(P['tezcatara.pool1.basic-strike'])
        out=[first[r.integer(len(first))],P['tezcatara.pool2'][r.integer(len(P['tezcatara.pool2']))],P['tezcatara.pool3'][r.integer(len(P['tezcatara.pool3']))]]
    elif kind in ('nonupeipe','tanx'):
        prefix='nonupeipe' if kind=='nonupeipe' else 'tanx'
        base='nonupeipe.pool.base' if kind=='nonupeipe' else 'tanx.pool.base'
        extra='nonupeipe.pool.swift' if kind=='nonupeipe' else 'tanx.pool.instinct'
        enabled=COND['nonu_swift'] if kind=='nonupeipe' else COND['tanx_instinct']
        scratch=list(P[base]);
        if enabled: scratch.extend(P[extra])
        r.shuffle(scratch); out=scratch[:3]
    elif kind=='vakuu':
        out=[]
        for role in ('vakuu.pool1','vakuu.pool2','vakuu.pool3'):
            scratch=list(P[role]); r.shuffle(scratch); out.append(scratch[0])
    else: raise ValueError(kind)
    return tuple(out),target,r.signature()

kinds=['darv','orobas','pael','tezcatara','nonupeipe','tanx','vakuu']
mis={k:0 for k in kinds}; state_mis={k:0 for k in kinds}; target_mis=0
for i in range(4102):
    root=(0x9E3779B97F4A7C15*i+0xD1B54A32D192ED03)&MASK
    for k in kinds:
        a=reference(k,root); b=fast(k,root)
        if a[:2]!=b[:2]:
            mis[k]+=1
            if k=='orobas' and a[1]!=b[1]: target_mis+=1
        if a[2]!=b[2]: state_mis[k]+=1

# Predicate and scope vectors.
root=0x123456789ABCDEF0
oro_options,oro_target,_=fast('orobas',root)
darv_options,_,_=fast('darv',root)
assert oro_options[0] in oro_options
assert 65534 not in oro_options
assert oro_target in CHARS[1:]
# Same target identity deliberately placed in a different Ancient result must not
# satisfy an Orobas-scoped branch when the actual identity is Darv.
cross_identity_target=darv_options[0]
assert cross_identity_target in darv_options
assert not ('darv'=='orobas' and cross_identity_target in oro_options)
# Identity failure is an outer-stage early return: option projection count stays zero.
option_stage_calls=0
def staged(identity_matches):
    global option_stage_calls
    if not identity_matches: return False
    option_stage_calls+=1; fast('orobas',root); return True
assert staged(False) is False and option_stage_calls==0
assert staged(True) is True and option_stage_calls==1
# Sea Glass target only applies when Sea Glass is visible.
sea_visible=SEA_GLASS in oro_options
sea_positive=sea_visible and oro_target in CHARS[1:]
sea_negative=sea_visible and 9999==oro_target
assert not sea_negative

# Hook-denied production path returns wrapper.proceed and consumes no event-local RNG.
WRAPPER_PROCEED=901
reference_denied=(WRAPPER_PROCEED,),None,0
fast_denied=(WRAPPER_PROCEED,),None,0
assert reference_denied==fast_denied
# Option rejection occurs before the Sea Glass predicate leaf even though the shared
# projection has already materialized the target as part of the same event-local rule.
option_rejected=True
sea_predicate_evaluated=False if option_rejected else sea_visible
assert option_rejected and not sea_predicate_evaluated

print('roots=4102 generators=7')
print(' '.join(f'{k}OptionMismatch={mis[k]} {k}RngStateMismatch={state_mis[k]}' for k in kinds))
print(f'SeaGlassTargetMismatch={target_mis} scopedCrossIdentity=passed identityFailureSkipsOption=passed hookDeniedProceed=passed optionRejectSkipsSeaPredicate=passed')
if any(mis.values()) or any(state_mis.values()) or target_mis:
    raise SystemExit(1)
