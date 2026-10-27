#!/usr/bin/env python3
"""Host-side numeric vectors for N1-B pool packing and RNG consumption.

This does not claim Godot shader execution. It compares the CPU-style direct
pool algorithms against the flattened GPU-plan metadata algorithms over fixed
roots so source delivery can catch pool-offset or RNG-consumption regressions
before the Windows runtime parity gate.
"""
from __future__ import annotations
from dataclasses import dataclass
import struct

MASK=(1<<64)-1
INVALID=0xffffffff
REWARDS_HASH=0x5bca7c7c12345678
NICHE_HASH=0x47dd82a0a0b0c0d0
POTION_HASH=0x8e7f6a5b4c3d2e1f
PLAYER_SLOT=0
ASCENSION=10

def rotl(x,k): return ((x<<k)|(x>>(64-k))) & MASK

def splitmix_next(state):
    state=(state+0x9e3779b97f4a7c15)&MASK
    z=state
    z=((z^(z>>30))*0xbf58476d1ce4e5b9)&MASK
    z=((z^(z>>27))*0x94d049bb133111eb)&MASK
    return state,(z^(z>>31))&MASK

@dataclass
class Rng:
    s0:int; s1:int; s2:int; s3:int
    @classmethod
    def init(cls,seed):
        vals=[];state=seed&MASK
        for _ in range(4): state,v=splitmix_next(state);vals.append(v)
        return cls(*vals)
    def next_u64(self):
        result=(rotl((self.s1*5)&MASK,7)*9)&MASK
        temporary=(self.s1<<17)&MASK
        self.s2^=self.s0;self.s3^=self.s1;self.s1^=self.s2;self.s0^=self.s3
        self.s2^=temporary;self.s3=rotl(self.s3,45)
        self.s0&=MASK;self.s1&=MASK;self.s2&=MASK;self.s3&=MASK
        return result
    def next_int(self,bound):
        assert bound>0
        x=self.next_u64()>>11
        return int((x*(2.0**-53))*bound)
    def next_double(self): return (self.next_u64()>>11)*(2.0**-53)
    def next_float(self): return struct.unpack('f',struct.pack('f',self.next_double()))[0]

CHAR={1:[101,102,103,104,105],2:[201,202,203,204],3:[301,302,303,304,305]}
COLOR={1:[401,402,403],2:[451,452,453],3:[481,482,483]}
POT={1:[501,502,503,504],2:[521,522,523],3:[541,542],0:[501,502,503,504,521,522,523,541,542]}
NEW=[601,602,603,604]

META=[0]*20;DENSE=[]
def append(values,idx):
    META[idx]=len(DENSE);META[idx+1]=len(values);DENSE.extend(values)
for rarity,idx in [(1,0),(2,2),(3,4)]: append(CHAR[rarity],idx)
for rarity,idx in [(1,6),(2,8),(3,10)]: append(COLOR[rarity],idx)
for rarity,idx in [(1,12),(2,14),(3,16),(0,18)]: append(POT[rarity],idx)

def select_direct(source,used,rng):
    avail=[x for x in source if x not in used]
    return INVALID if not avail else avail[rng.next_int(len(avail))]

def select_meta(meta_idx,used,rng):
    off,n=META[meta_idx],META[meta_idx+1]
    avail=[DENSE[off+i] for i in range(n) if DENSE[off+i] not in used]
    return INVALID if not avail else avail[rng.next_int(len(avail))]

def rarity_order(requested):
    return {1:(1,2,3),2:(2,3,1),3:(3,1,2)}[requested]

def roll_card_direct(pool,used,rng):
    rare=struct.unpack('f',struct.pack('f',0.0149 if ASCENSION>=7 else 0.03))[0]
    roll=rng.next_float();requested=3 if roll<rare else (2 if roll<rare+0.37 else 1)
    for rarity in rarity_order(requested):
        v=select_direct(pool[rarity],used,rng)
        if v!=INVALID:
            rng.next_double();return v
    return INVALID

def roll_card_meta(base,used,rng):
    rare=struct.unpack('f',struct.pack('f',0.0149 if ASCENSION>=7 else 0.03))[0]
    roll=rng.next_float();requested=3 if roll<rare else (2 if roll<rare+0.37 else 1)
    for rarity in rarity_order(requested):
        v=select_meta(base+(rarity-1)*2,used,rng)
        if v!=INVALID:
            rng.next_double();return v
    return INVALID

def roll_potion_direct(used,rng):
    roll=rng.next_double();requested=POT[3] if roll<=.1 else (POT[2] if roll<=.35 else POT[1])
    v=select_direct(requested,used,rng)
    return v if v!=INVALID else select_direct(POT[0],used,rng)

def roll_potion_meta(used,rng):
    roll=rng.next_double();idx=16 if roll<=.1 else (14 if roll<=.35 else 12)
    v=select_meta(idx,used,rng)
    return v if v!=INVALID else select_meta(18,used,rng)

def rewards_checkpoint(root):
    rng=Rng.init((root+PLAYER_SLOT+REWARDS_HASH)&MASK)
    for count in range(28,1,-1): rng.next_int(count)
    return rng

def outputs_direct(root):
    result={}
    r=rewards_checkpoint(root); result['Arcane']=(select_direct(CHAR[3],[],r),)
    r=rewards_checkpoint(root);used=[]
    for _ in range(3): used.append(select_direct(CHAR[3],used,r))
    result['Hefty']=tuple(used)
    r=rewards_checkpoint(root);used=[]
    for _ in range(2): used.append(roll_card_direct(COLOR,used,r))
    result['Lead']=tuple(used)
    r=rewards_checkpoint(root);used=[]
    for _ in range(3): used.append(roll_card_direct(CHAR,used,r))
    result['Coffer']=tuple(used)+(roll_potion_direct([],r),)
    r=Rng.init((root+POTION_HASH)&MASK);p0=roll_potion_direct([],r);p1=roll_potion_direct([p0],r)
    result['Phial']=(p0,p1)
    r=Rng.init((root+NICHE_HASH)&MASK);result['NewLeaf']=(NEW[r.next_int(len(NEW))],)
    return result

def outputs_meta(root):
    result={}
    r=rewards_checkpoint(root); result['Arcane']=(select_meta(4,[],r),)
    r=rewards_checkpoint(root);used=[]
    for _ in range(3): used.append(select_meta(4,used,r))
    result['Hefty']=tuple(used)
    r=rewards_checkpoint(root);used=[]
    for _ in range(2): used.append(roll_card_meta(6,used,r))
    result['Lead']=tuple(used)
    r=rewards_checkpoint(root);used=[]
    for _ in range(3): used.append(roll_card_meta(0,used,r))
    result['Coffer']=tuple(used)+(roll_potion_meta([],r),)
    r=Rng.init((root+POTION_HASH)&MASK);p0=roll_potion_meta([],r);p1=roll_potion_meta([p0],r)
    result['Phial']=(p0,p1)
    r=Rng.init((root+NICHE_HASH)&MASK);offset=0;result['NewLeaf']=(NEW[offset+r.next_int(len(NEW))],)
    return result

roots=[0,1,2,3,0x123456789abcdef0,0xffffffffffffffff]
roots += [((i*0x9e3779b97f4a7c15)^0xd1b54a32d192ed03)&MASK for i in range(1,4097)]
for root in roots:
    a=outputs_direct(root);b=outputs_meta(root)
    if a!=b: raise SystemExit(f'n1bNumericReferenceVectors=Failed;root={root:016x};cpu={a};gpuPlan={b}')
print(f'n1bNumericReferenceVectors=Passed;roots={len(roots)};operators=6')
