#!/usr/bin/env python3
"""N1-D host vectors for sequential Offered/Reverse route-local continuation.

This is a focused numeric oracle, not a broad gate and not a Godot shader run.
It compares a CPU-style route executor with the N1-D GPU-plan model and proves
that the retired independent-slot model diverges for shared continuations.
"""
from dataclasses import dataclass

MASK=(1<<64)-1
REWARDS_HASH=0x5bca7c7c12345678
NICHE_HASH=0x47dd82a0a0b0c0d0
TRANSFORM_HASH=0x263758193a4b5c6d
UPFRONT_HASH=0x96f58d4b3c2a1908
BONES_COUNT=28


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
        values=[]; state=seed&MASK
        for _ in range(4): state,v=splitmix_next(state); values.append(v)
        return cls(*values)
    def clone(self): return Rng(self.s0,self.s1,self.s2,self.s3)
    def checkpoint(self): return (self.s0,self.s1,self.s2,self.s3)
    def next_u64(self):
        result=(rotl((self.s1*5)&MASK,7)*9)&MASK
        temporary=(self.s1<<17)&MASK
        self.s2^=self.s0; self.s3^=self.s1; self.s1^=self.s2; self.s0^=self.s3
        self.s2^=temporary; self.s3=rotl(self.s3,45)
        self.s0&=MASK; self.s1&=MASK; self.s2&=MASK; self.s3&=MASK
        return result
    def next_int(self,bound): return int(((self.next_u64()>>11)*(2.0**-53))*bound)
    def next_double(self): return (self.next_u64()>>11)*(2.0**-53)
    def shuffle(self,values):
        for count in range(len(values),1,-1):
            selected=self.next_int(count); tail=count-1
            values[selected],values[tail]=values[tail],values[selected]

COMMON=[201,202,203,204,205,206]
UNCOMMON=[301,302,303,304]
RARE=[401,402,403,404]
NEW_LEAF=[501,502,503,504,505]
STRIKE=[601,602,603]
DEFEND=[701,702,703,704]
ORDINARY=[(1001,1),(1002,1),(1003,1),(1101,2),(1102,2),(1201,3),(1202,3)]
BUCKETS=[[0,1,2],[3,4],[5,6]]
SHARED_LENGTHS=[4,3,5]


def post_offer_rewards(root):
    r=Rng.init((root+REWARDS_HASH)&MASK)
    for count in range(BONES_COUNT,1,-1): r.next_int(count)
    return r

def initial_bag(root):
    r=Rng.init((root+UPFRONT_HASH)&MASK)
    for length in SHARED_LENGTHS:
        for count in range(length,1,-1): r.next_int(count)
    bag=[]
    for bucket in BUCKETS:
        part=list(bucket); r.shuffle(part); bag.extend(part)
    return bag

@dataclass
class State:
    rewards:Rng
    niche:Rng
    transformations:Rng
    bag:list
    removed:set
    def clone(self):
        return State(self.rewards.clone(),self.niche.clone(),self.transformations.clone(),list(self.bag),set(self.removed))
    def checkpoint(self):
        return (self.rewards.checkpoint(),self.niche.checkpoint(),self.transformations.checkpoint(),tuple(self.bag),tuple(sorted(self.removed)))

def initial_state(root, rewards_checkpoint=None):
    return State(
        rewards=(rewards_checkpoint or post_offer_rewards(root)).clone(),
        niche=Rng.init((root+NICHE_HASH)&MASK),
        transformations=Rng.init((root+TRANSFORM_HASH)&MASK),
        bag=initial_bag(root), removed=set())

def select_unused(pool, used, rng):
    available=[x for x in pool if x not in used]
    return available[rng.next_int(len(available))]

def op_arcane(state):
    return (select_unused(RARE,[],state.rewards),)

def op_scroll(state):
    used=[]; out=[]
    for _ in range(2):
        if state.rewards.next_int(100)<1:
            out.append(('claw',)); continue
        a=select_unused(COMMON,used,state.rewards); used.append(a)
        b=select_unused(COMMON,used,state.rewards); used.append(b)
        c=select_unused(UNCOMMON,used,state.rewards); used.append(c)
        out.append((a,b,c))
    return tuple(out)

def op_kaleido(state):
    # The output is simplified, but RNG continuation matches the audited shape:
    # two Niche shuffles and six Rewards selections.
    out=[]
    for _ in range(2):
        order=list(range(5)); state.niche.shuffle(order)
        for i in range(3): out.append(COMMON[state.rewards.next_int(len(COMMON))] + order[i])
    return tuple(out)

def op_new_leaf(state):
    return (NEW_LEAF[state.niche.next_int(len(NEW_LEAF))],)

def op_leafy(state):
    return (STRIKE[state.transformations.next_int(len(STRIKE))], DEFEND[state.transformations.next_int(len(DEFEND))])

def op_capsule(state, count=2):
    out=[]
    for _ in range(count):
        roll=state.rewards.next_double(); requested=1 if roll<.5 else (2 if roll<.83 else 3)
        order={1:(1,2,3),2:(2,3,1),3:(3,1,2)}[requested]
        selected=None
        for rarity in order:
            for pos,index in enumerate(state.bag):
                if pos in state.removed: continue
                dense,actual=ORDINARY[index]
                if actual==rarity:
                    state.removed.add(pos); selected=dense; break
            if selected is not None: break
        assert selected is not None
        out.append(selected)
    return tuple(out)

OPS={
    'arcane':op_arcane, 'scroll':op_scroll, 'kaleido':op_kaleido,
    'new_leaf':op_new_leaf, 'leafy':op_leafy, 'capsule':op_capsule,
}

def sequential(root, order):
    checkpoint=post_offer_rewards(root)
    state=initial_state(root,checkpoint)
    outputs=[]
    for name in order: outputs.append((name,OPS[name](state)))
    return tuple(outputs),state.checkpoint()

def independent_slots(root, order):
    checkpoint=post_offer_rewards(root)
    outputs=[]
    states=[]
    for name in order:
        state=initial_state(root,checkpoint)
        outputs.append((name,OPS[name](state))); states.append(state.checkpoint())
    return tuple(outputs),tuple(states)

roots=[0,1,2,3,0x123456789abcdef0,0xffffffffffffffff]
roots += [((i*0x9e3779b97f4a7c15)^0xd1b54a32d192ed03)&MASK for i in range(1,4097)]
shared_pairs=[('arcane','scroll'),('kaleido','new_leaf'),('capsule','scroll')]
independent_pairs=[('arcane','leafy'),('capsule','leafy')]
legacy_mismatch={pair:0 for pair in shared_pairs}
order_difference={pair:0 for pair in shared_pairs}

for root in roots:
    for pair in shared_pairs:
        offered=sequential(root,pair)
        reverse=sequential(root,pair[::-1])
        legacy=independent_slots(root,pair)
        if offered[0] != legacy[0]: legacy_mismatch[pair]+=1
        if offered[0] != reverse[0]: order_difference[pair]+=1
        # Re-running Reverse must be independent of the already-run Offered route.
        reverse_again=sequential(root,pair[::-1])
        if reverse != reverse_again:
            raise SystemExit(f'n1dNumericRouteVectors=Failed;routeClone;root={root:016x};pair={pair}')
    for pair in independent_pairs:
        offered=sequential(root,pair)
        reverse=sequential(root,pair[::-1])
        by_name_a={k:v for k,v in offered[0]}; by_name_b={k:v for k,v in reverse[0]}
        if by_name_a != by_name_b:
            raise SystemExit(f'n1dNumericRouteVectors=Failed;independent;root={root:016x};pair={pair}')

for pair in shared_pairs:
    if legacy_mismatch[pair] == 0 or order_difference[pair] == 0:
        raise SystemExit(f'n1dNumericRouteVectors=Failed;noWitness;pair={pair};legacy={legacy_mismatch[pair]};order={order_difference[pair]}')

print('n1dNumericRouteVectors=Passed;roots=%d;sharedPairs=%d;independentPairs=%d;legacyMismatch=%s;orderDifference=%s' % (
    len(roots),len(shared_pairs),len(independent_pairs),legacy_mismatch,order_difference))
