#!/usr/bin/env python3
"""Independent numeric vectors for the Act World CPU Fast foundation.

This is a delivery-container numeric oracle, not a substitute for the Windows
production build. It compares list/model-key reference replay with dense-array
Fast replay across 4,102 deterministic roots.
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import Callable

MASK=(1<<64)-1
P1=11400714785074694791; P2=14029467366897019727; P3=1609587929392839161
P4=9650029242287828579; P5=2870177450012600261

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

SM_INC=11400714819323198485; SM_M1=13787848793156543929; SM_M2=10723151780598845931
class Rng:
    def __init__(self, seed=None, state=None):
        if state is not None: self.s=list(state); return
        x=seed&MASK; out=[]
        for _ in range(4):
            x=(x+SM_INC)&MASK; z=x; z=((z^(z>>30))*SM_M1)&MASK; z=((z^(z>>27))*SM_M2)&MASK; out.append((z^(z>>31))&MASK)
        self.s=out
    def state(self): return tuple(self.s)
    def u64(self):
        s0,s1,s2,s3=self.s; result=(rol((s1*5)&MASK,7)*9)&MASK; t=(s1<<17)&MASK
        s2^=s0; s3^=s1; s1^=s2; s0^=s3; s2^=t; s3=rol(s3,45); self.s=[s0&MASK,s1&MASK,s2&MASK,s3&MASK]; return result
    def double(self): return (self.u64()>>11)*(2.0**-53)
    def integer(self,n): return int(self.double()*n)
    def shuffle(self,a):
        for count in range(len(a),1,-1):
            j=self.integer(count); a[j],a[count-1]=a[count-1],a[j]
    def consume_shuffle(self,n):
        for count in range(n,1,-1): self.integer(count)

UP=xxh64(b'up_front'); ACTSEL=xxh64(b'act_selection')
RELIC_BUCKETS=[[101,102,103,104,105],[201,202,203],[301,302,303,304,305,306,307],[401,402,403,404],[501,502,503,504,505,506],[601,602]]

def prefix_reference(root):
    r=Rng((root+UP)&MASK)
    for src in RELIC_BUCKETS:
        a=list(src); r.shuffle(a)
    return r.state()

def prefix_fast(root):
    r=Rng((root+UP)&MASK)
    for src in RELIC_BUCKETS: r.consume_shuffle(len(src))
    return r.state()

@dataclass(frozen=True)
class Enc:
    key:int; source:int; ref:int; conflicts:tuple[int,...]
@dataclass
class Act:
    ordinal:int; key:int; events:list[tuple[int,int,bool]]; weak:list[Enc]; regular:list[Enc]; elite:list[Enc]; bosses:list[int]; ancients:list[int]

# source: 0 local, 1 shared. First raw entry is consumed by Opening Ancient cursor.
ACTS={
  11:Act(1,11,[(1000,0,True),(1001,1,False),(1002,1,True),(1003,1,True)],
         [Enc(110,0,10,()),Enc(111,1,11,())],[Enc(120,2,20,(3,)),Enc(121,3,21,(2,)),Enc(122,4,22,())],[Enc(130,5,30,()),Enc(131,6,31,())],[140,141,142],[150,151]),
  12:Act(1,12,[(1010,0,True),(1011,1,False),(1012,1,True),(1013,1,True)],
         [Enc(112,0,12,()),Enc(113,1,13,())],[Enc(123,2,23,()),Enc(124,3,24,())],[Enc(132,4,32,()),Enc(133,5,33,())],[143,144,145],[152,153]),
  21:Act(2,21,[(2000,0,True),(2001,1,True),(2002,1,False),(2003,1,True)],
         [Enc(210,0,40,()),Enc(211,1,41,())],[Enc(220,2,50,(3,)),Enc(221,3,51,(2,)),Enc(222,4,52,())],[Enc(230,5,60,()),Enc(231,6,61,())],[240,241,242],[250,251]),
  31:Act(3,31,[(3000,0,True),(3001,1,True),(3002,1,True),(3003,1,False)],
         [Enc(310,0,70,()),Enc(311,1,71,())],[Enc(320,2,80,(3,)),Enc(321,3,81,(2,)),Enc(322,4,82,())],[Enc(330,5,90,()),Enc(331,6,91,())],[340,341,342],[350,351]),
}
GROUPS=[(1,[11,12],0),(2,[21],1),(3,[31],1)]
SHARED_ANCIENTS=[900,901,902]

def allowed(c:Enc, prev:Enc|None):
    if prev is None:return True
    if c.ref>=0 and prev.ref>=0 and c.ref==prev.ref:return False
    return prev.source not in c.conflicts

def consume_ref(source:list[Enc],slots:int,r:Rng,history:list[Enc]):
    bag=list(source)
    for _ in range(slots):
        if not bag and source: bag=list(source)
        prev=history[-1] if history else None
        eligible=[x for x in bag if allowed(x,prev)]
        chosen=None
        if eligible:
            while chosen is None:
                idx=int(r.double()*len(bag)); c=bag[idx]
                if allowed(c,prev): chosen=bag.pop(idx)
        else:
            sample=r.double()
            if bag: chosen=bag.pop(int(sample*len(bag)))
        if chosen is not None: history.append(chosen)

def consume_fast(source:list[Enc],slots:int,r:Rng,prev:Enc|None):
    bag=list(range(len(source)))
    for _ in range(slots):
        if not bag and source: bag=list(range(len(source)))
        ok=any(allowed(source[i],prev) for i in bag)
        sel=-1
        if ok:
            while True:
                pos=int(r.double()*len(bag))
                if allowed(source[bag[pos]],prev): sel=pos; break
        else:
            sample=r.double()
            if bag: sel=int(sample*len(bag))
        if sel>=0:
            prev=source[bag.pop(sel)]
    return prev

def static_candidates(events):
    out=[]; seen=set()
    for key,src,eligible in events[1:]:
        if not eligible or key in seen: continue
        seen.add(key); out.append((key,src))
    return out

def replay_reference(root):
    selrng=Rng((root+ACTSEL)&MASK); selected=[]
    for _,pool,mode in GROUPS: selected.append(pool[0] if mode else pool[selrng.integer(len(pool))])
    r=Rng(state=prefix_reference(root))
    remaining=list(SHARED_ANCIENTS); r.shuffle(remaining); assigned={}
    for key in selected[1:]:
        n=r.integer(len(remaining)+1); assigned[key]=remaining[:n]; del remaining[:n]
    obs={'acts':tuple(selected),'events':{},'boss1':{},'ancient':{}}
    first={}
    for key in selected:
        act=ACTS[key]; ev=list(act.events); r.shuffle(ev); obs['events'][act.ordinal]=tuple(static_candidates(ev))
        hist=[]; consume_ref(act.weak,2,r,hist); consume_ref(act.regular,3,r,hist); consume_ref(act.elite,2,r,[])
        b=act.bosses[r.integer(len(act.bosses))]; first[act.ordinal]=b; obs['boss1'][act.ordinal]=b
        pool=act.ancients+assigned.get(key,[]); obs['ancient'][act.ordinal]=pool[r.integer(len(pool))]
    final=ACTS[selected[-1]]; pool=[b for b in final.bosses if b!=first[3]]; obs['boss2']=pool[r.integer(len(pool))]
    return obs,r.state()

def replay_fast(root):
    selrng=Rng((root+ACTSEL)&MASK); selected=[]
    for _,pool,mode in GROUPS: selected.append(pool[0] if mode else pool[selrng.integer(len(pool))])
    r=Rng(state=prefix_fast(root))
    remaining=list(SHARED_ANCIENTS); r.shuffle(remaining); assigned=[[] for _ in selected]
    for i in range(1,len(selected)):
        n=r.integer(len(remaining)+1); assigned[i]=remaining[:n]; remaining=remaining[n:]
    obs={'acts':tuple(selected),'events':{},'boss1':{},'ancient':{}}; first=[]
    for i,key in enumerate(selected):
        act=ACTS[key]; ev=list(act.events); r.shuffle(ev); obs['events'][act.ordinal]=tuple(static_candidates(ev))
        prev=None; prev=consume_fast(act.weak,2,r,prev); prev=consume_fast(act.regular,3,r,prev); consume_fast(act.elite,2,r,None)
        b=act.bosses[r.integer(len(act.bosses))]; first.append(b); obs['boss1'][act.ordinal]=b
        pool=act.ancients+assigned[i]; obs['ancient'][act.ordinal]=pool[r.integer(len(pool))]
    final=ACTS[selected[-1]]; pool=[b for b in final.bosses if b!=first[-1]]; obs['boss2']=pool[r.integer(len(pool))]
    return obs,r.state()

prefix_mismatch=world_mismatch=state_mismatch=0
boss_mismatch=ancient_mismatch=event_mismatch=0
for i in range(4102):
    root=(0x9E3779B97F4A7C15*i + 0xD1B54A32D192ED03)&MASK
    if prefix_reference(root)!=prefix_fast(root): prefix_mismatch+=1
    a,sa=replay_reference(root); b,sb=replay_fast(root)
    if a!=b: world_mismatch+=1
    if sa!=sb: state_mismatch+=1
    if a['boss1']!=b['boss1'] or a['boss2']!=b['boss2']: boss_mismatch+=1
    if a['ancient']!=b['ancient']: ancient_mismatch+=1
    if a['events']!=b['events']: event_mismatch+=1

# Frozen event ordinal examples. The first raw entry is the opening Ancient cursor.
act1_case=[(999,0,True),(1,1,False),(2,1,False),(3,1,True),(4,1,True)]
case1=static_candidates(act1_case)
assert case1[0][0]==3, case1
runtime_case=[(999,0,True),(10,1,True),(3,1,True)]
case2=static_candidates(runtime_case)
assert case2[0][0]==10 and not any(key==3 for key,_ in case2[:1]), case2
replacement_case=[(999,0,True),(77,1,False),(3,1,True)]
assert static_candidates(replacement_case)[0][0]==3

print(f"roots=4102 prefixMismatch={prefix_mismatch} worldMismatch={world_mismatch} finalStateMismatch={state_mismatch}")
print(f"bossIdentityMismatch={boss_mismatch} ancientIdentityMismatch={ancient_mismatch} eventCandidateOrdinalMismatch={event_mismatch}")
print("eventActCleaningCase=passed runtimeConditionalRetained=passed replacementOnlyAbsent=passed")
if any((prefix_mismatch,world_mismatch,state_mismatch,boss_mismatch,ancient_mismatch,event_mismatch)):
    raise SystemExit(1)
