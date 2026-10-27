#!/usr/bin/env python3
"""Host-side N1-C vectors for flattened pool metadata and RNG consumption.

This compares CPU-style direct catalog algorithms with the packed GPU-plan
metadata algorithms. It does not claim Godot shader compilation or execution.
"""
from __future__ import annotations
from dataclasses import dataclass

MASK=(1<<64)-1
INVALID=0xffffffff
REWARDS_HASH=0x5bca7c7c12345678
NICHE_HASH=0x47dd82a0a0b0c0d0
UPFRONT_HASH=0x96f58d4b3c2a1908
PLAYER_SLOT=0
BONES_COUNT=28
KALEIDOSCOPE=14
NEW_LEAF=19


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
        return int(((self.next_u64()>>11)*(2.0**-53))*bound)
    def next_double(self): return (self.next_u64()>>11)*(2.0**-53)
    def shuffle(self, values):
        for count in range(len(values),1,-1):
            selected=self.next_int(count);tail=count-1
            values[selected],values[tail]=values[tail],values[selected]

# Ordinary relic index -> (dense ID, rarity 1/2/3/4)
ORDINARY=[
    (1001,1),(1002,1),(1003,1),(1004,1),
    (1101,2),(1102,2),(1103,2),
    (1201,3),(1202,3),(1203,3),
    (1301,4),
]
BUCKETS=[[0,1,2,3],[4,5,6],[7,8,9],[10]]
SHARED_LENGTHS=[4,3,5]
COMMON=[201,202,203,204,205]
UNCOMMON=[301,302,303,304]
CURSES=[401,402,403,404,405,406,407,408]
NEW_LEAF_POOL=[501,502,503,504]
OTHER_POOL_COUNT=5
OTHER_POOL_CARD_COUNT=12

# Build a faithful N1-C trailer over the same flattened value buffer layout.
META=[0]*20
DENSE=[]
def append(values,meta_index):
    META[meta_index]=len(DENSE);META[meta_index+1]=len(values);DENSE.extend(values)
append(COMMON,0);append(UNCOMMON,2);append([],4)
trailer_base=20
META += [0]*(14+len(BUCKETS)*2+len(ORDINARY)*2)
shared_offset=len(DENSE);DENSE.extend(SHARED_LENGTHS)
bucket_values_offset=len(DENSE)
bucket_desc=trailer_base+14
for i,bucket in enumerate(BUCKETS):
    META[bucket_desc+i*2]=len(DENSE);META[bucket_desc+i*2+1]=len(bucket);DENSE.extend(bucket)
curse_offset=len(DENSE);DENSE.extend(CURSES)
required_offset=len(DENSE);DENSE.extend([403])
banned_offset=len(DENSE);DENSE.extend([408])
META[trailer_base:trailer_base+14]=[
    0x43314e47,len(SHARED_LENGTHS),len(BUCKETS),len(ORDINARY),
    shared_offset,bucket_values_offset,required_offset,1,banned_offset,1,
    UPFRONT_HASH & 0xffffffff,(UPFRONT_HASH>>32)&0xffffffff,curse_offset,len(CURSES)
]
ordinary_desc=bucket_desc+len(BUCKETS)*2
for i,(dense,rarity) in enumerate(ORDINARY):
    META[ordinary_desc+i*2]=dense;META[ordinary_desc+i*2+1]=rarity


def rewards_checkpoint(root):
    r=Rng.init((root+PLAYER_SLOT+REWARDS_HASH)&MASK)
    for count in range(BONES_COUNT,1,-1): r.next_int(count)
    return r

def direct_bag(root):
    r=Rng.init((root+UPFRONT_HASH)&MASK)
    for length in SHARED_LENGTHS:
        for count in range(length,1,-1): r.next_int(count)
    bag=[]
    for bucket in BUCKETS:
        part=list(bucket);r.shuffle(part);bag.extend(part)
    return bag

def meta_bag(root):
    base=trailer_base
    r=Rng.init((root+(META[base+10]|(META[base+11]<<32)))&MASK)
    for i in range(META[base+1]):
        length=DENSE[META[base+4]+i]
        for count in range(length,1,-1): r.next_int(count)
    bag=[]
    for i in range(META[base+2]):
        off,n=META[bucket_desc+i*2],META[bucket_desc+i*2+1]
        part=DENSE[off:off+n];r.shuffle(part);bag.extend(part)
    return bag

def pull_capsule(bag,removed,rewards,ordinary_lookup):
    roll=rewards.next_double();requested=1 if roll<.5 else (2 if roll<.83 else 3)
    order={1:(1,2,3),2:(2,3,1),3:(3,1,2)}[requested]
    for rarity in order:
        for pos,relic_index in enumerate(bag):
            if pos in removed: continue
            dense,actual=ordinary_lookup(relic_index)
            if actual==rarity:
                removed.add(pos);return dense
    return INVALID

def capsule_direct(root,count):
    bag=direct_bag(root);r=rewards_checkpoint(root);removed=set()
    return tuple(pull_capsule(bag,removed,r,lambda idx: ORDINARY[idx]) for _ in range(count))

def capsule_meta(root,count):
    bag=meta_bag(root);r=rewards_checkpoint(root);removed=set()
    def lookup(idx):
        return META[ordinary_desc+idx*2],META[ordinary_desc+idx*2+1]
    return tuple(pull_capsule(bag,removed,r,lookup) for _ in range(count))


def select_unused(source,used,r):
    available=[x for x in source if x not in used]
    return INVALID if not available else available[r.next_int(len(available))]

def select_meta(meta_index,used,r):
    off,n=META[meta_index],META[meta_index+1]
    available=[DENSE[off+i] for i in range(n) if DENSE[off+i] not in used]
    return INVALID if not available else available[r.next_int(len(available))]

def scroll_direct(root):
    r=rewards_checkpoint(root);used=[];bundles=[]
    for _ in range(2):
        if r.next_int(100)<1:
            bundles.append(("claw",));continue
        a=select_unused(COMMON,used,r);used.append(a)
        b=select_unused(COMMON,used,r);used.append(b)
        c=select_unused(UNCOMMON,used,r);used.append(c)
        bundles.append((a,b,c))
    return tuple(bundles)

def scroll_meta(root):
    r=rewards_checkpoint(root);used=[];bundles=[]
    for _ in range(2):
        if r.next_int(100)<1:
            bundles.append(("claw",));continue
        a=select_meta(0,used,r);used.append(a)
        b=select_meta(0,used,r);used.append(b)
        c=select_meta(2,used,r);used.append(c)
        bundles.append((a,b,c))
    return tuple(bundles)


def consume_shuffle(length,r):
    for count in range(length,1,-1): r.next_int(count)

def advance_niche(relic,r):
    if relic==KALEIDOSCOPE:
        consume_shuffle(OTHER_POOL_COUNT,r);consume_shuffle(OTHER_POOL_COUNT,r)
    elif relic==NEW_LEAF:
        r.next_int(len(NEW_LEAF_POOL))

def final_curse_direct(root,first,second):
    r=Rng.init((root+NICHE_HASH)&MASK);advance_niche(first,r);advance_niche(second,r)
    return CURSES[r.next_int(len(CURSES))]

def final_curse_meta(root,first,second):
    r=Rng.init((root+NICHE_HASH)&MASK);advance_niche(first,r);advance_niche(second,r)
    off,n=META[trailer_base+12],META[trailer_base+13]
    return DENSE[off+r.next_int(n)]

roots=[0,1,2,3,0x123456789abcdef0,0xffffffffffffffff]
roots += [((i*0x9e3779b97f4a7c15)^0xd1b54a32d192ed03)&MASK for i in range(1,4097)]
for root in roots:
    direct=(capsule_direct(root,1),capsule_direct(root,2),scroll_direct(root),
            final_curse_direct(root,KALEIDOSCOPE,NEW_LEAF),
            final_curse_direct(root,NEW_LEAF,KALEIDOSCOPE))
    packed=(capsule_meta(root,1),capsule_meta(root,2),scroll_meta(root),
            final_curse_meta(root,KALEIDOSCOPE,NEW_LEAF),
            final_curse_meta(root,NEW_LEAF,KALEIDOSCOPE))
    if direct!=packed:
        raise SystemExit(f'n1cNumericReferenceVectors=Failed;root={root:016x};cpu={direct};gpuPlan={packed}')
print(f'n1cNumericReferenceVectors=Passed;roots={len(roots)};operators=4')
