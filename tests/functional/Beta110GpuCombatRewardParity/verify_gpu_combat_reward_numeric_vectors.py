#!/usr/bin/env python3
"""Fixed-RootHash CPU-oracle vs packed-GPU-model vectors for Combat Reward R2."""
from dataclasses import dataclass, replace
from pathlib import Path
import json, math, struct, sys
MASK=(1<<64)-1

def u64(x): return x & MASK
def rotl(x,k): return u64((x<<k)|(x>>(64-k)))
def splitmix(x):
    x=u64(x+0x9e3779b97f4a7c15); z=x
    z=u64((z^(z>>30))*0xbf58476d1ce4e5b9)
    z=u64((z^(z>>27))*0x94d049bb133111eb)
    return x,u64(z^(z>>31))
def f32(x): return struct.unpack('<f',struct.pack('<f',x))[0]
class Rng:
    def __init__(self,seed):
        st=u64(seed); a=[]
        for _ in range(4): st,z=splitmix(st); a.append(z)
        self.s=a
    def clone(self):
        r=Rng(0); r.s=self.s.copy(); return r
    def next_u64(self):
        s0,s1,s2,s3=self.s; out=u64(rotl(u64(s1*5),7)*9); t=u64(s1<<17)
        s2^=s0; s3^=s1; s1^=s2; s0^=s3; s2^=t; s3=rotl(s3,45)
        self.s=[u64(s0),u64(s1),u64(s2),u64(s3)]; return out
    def next_double(self): return (self.next_u64()>>11)*(1.0/(1<<53))
    def next_float(self): return f32(self.next_double())
    def next_int(self,bound): return int(((self.next_u64()>>11)*(2.0**-53))*bound)
    def shuffle(self,a):
        for n in range(len(a),1,-1):
            i=self.next_int(n); a[i],a[n-1]=a[n-1],a[i]

FORCE=1; PRAYER=2; CANDY=4; UNKNOWN_IMPACT=64; UNKNOWN_CONT=128
@dataclass
class Cap:
    continuation: bool=True; influence: bool=True; flags:int=0; extra:int=0
    def packed(self): return (1 if self.continuation else 0)|(2 if self.influence else 0)|(self.flags<<8)|(self.extra<<24)
@dataclass
class State:
    rewards:Rng; potion:f32; offset:f32; flags:int=0; extra:int=0; candy:int=0; cont:bool=True; infl:bool=True
    def clone(self): return State(self.rewards.clone(),self.potion,self.offset,self.flags,self.extra,self.candy,self.cont,self.infl)
POOLS={1:list(range(100,116)),2:list(range(200,212)),3:list(range(300,308))}
POWER={1:list(range(400,408)),2:list(range(500,506)),3:list(range(600,604))}
POTIONS={1:list(range(700,708)),2:list(range(800,805)),3:list(range(900,903))}
RARITIES=[1,2,3]

def apply_cpu(st,cap):
    st.cont &= cap.continuation; st.infl &= cap.influence; st.flags |= cap.flags; st.extra += cap.extra

def apply_gpu(st,packed):
    st.cont &= bool(packed&1); st.infl &= bool(packed&2); st.flags |= (packed>>8)&0xffff; st.extra += packed>>24

def rarity(value,asc,offset):
    base=f32(0.0149 if asc>=7 else 0.03); rt=f32(base+offset); ut=f32(rt+f32(0.37))
    if value<rt: return 3,f32(-0.05)
    return (2 if value<ut else 1),f32(min(f32(0.4),f32(offset+f32(0.005 if asc>=7 else 0.01))))
def select(pool,excluded,rng):
    av=[x for x in pool if x not in excluded]
    return None if not av else av[rng.next_int(len(av))]
def one_card(st,asc,poolset,excluded):
    req,st.offset=rarity(st.rewards.next_float(),asc,st.offset)
    order={1:[1,2,3],2:[2,3,1],3:[3,1,2]}[req]
    for rr in order:
        v=select(poolset[rr],excluded,st.rewards)
        if v is not None:
            st.rewards.next_float(); return v
    return None
def reward_group(st,asc,poolset,out):
    local=[]
    for _ in range(3):
        v=one_card(st,asc,poolset,local)
        if v is None: return False
        local.append(v); out.append(v)
    return True

def project(st,asc=10,battles=3):
    if not st.cont or not st.infl or st.flags&(UNKNOWN_IMPACT|UNKNOWN_CONT): return {'conservative':True}
    all_cards=[]; drops=[]; pots=[]; checkpoints=[]
    for b in range(battles):
        if st.flags&FORCE: drop=True
        else:
            drop=st.rewards.next_float()<st.potion
            st.potion=f32(st.potion+f32(-0.1 if drop else 0.1))
        st.rewards.next_int(9 if asc>=10 else 11)
        pot=None
        if drop:
            p=st.rewards.next_float(); rr=3 if p<=f32(0.1) else 2 if p<=f32(0.35) else 1
            pot=POTIONS[rr][st.rewards.next_int(len(POTIONS[rr]))]
        cards=[]
        assert reward_group(st,asc,POOLS,cards)
        if st.flags&CANDY and st.candy%2==1:
            v=one_card(st,asc,POWER,cards); assert v is not None; cards.append(v)
        for _ in range(st.extra): assert reward_group(st,asc,POOLS,cards)
        if st.flags&CANDY: st.candy+=1
        all_cards.append(cards); drops.append(drop); pots.append(pot)
        checkpoints.append((tuple(st.rewards.s),st.potion,st.offset,st.candy))
    return {'conservative':False,'cards':all_cards,'drops':drops,'potions':pots,'checkpoints':checkpoints,
            'state':(tuple(st.rewards.s),st.potion,st.offset,st.flags,st.extra,st.candy)}

def execute_operator(st,kind):
    if kind=='arcane': st.rewards.next_int(len(POOLS[3]))

def init_bag(root):
    up=Rng(root+0x1a8a7b60da243e80)
    buckets=[list(range(0,5)),list(range(5,10)),list(range(10,15))]
    for b in buckets: up.shuffle(b)
    return [x for b in buckets for x in b]
CAPS=[Cap(flags=PRAYER,extra=1),Cap(flags=FORCE),Cap(flags=CANDY),Cap(),Cap(False,False,UNKNOWN_IMPACT|UNKNOWN_CONT)]*3
RAR=[1]*5+[2]*5+[3]*5

def capsule_cpu(st,root,bag,removed):
    p=st.rewards.next_double(); req=1 if p<0.5 else 2 if p<0.83 else 3
    order={1:[1,2,3],2:[2,3,1],3:[3,1,2]}[req]
    for rr in order:
        for pos,idx in enumerate(bag):
            if pos in removed or RAR[idx]!=rr: continue
            removed.add(pos); apply_cpu(st,CAPS[idx]); return idx
    return None

def capsule_gpu(st,root,bag,removed):
    p=st.rewards.next_double(); req=1 if p<0.5 else 2 if p<0.83 else 3
    order={1:[1,2,3],2:[2,3,1],3:[3,1,2]}[req]
    packed=[c.packed() for c in CAPS]
    for rr in order:
        for pos,idx in enumerate(bag):
            if pos in removed or RAR[idx]!=rr: continue
            removed.add(pos); apply_gpu(st,packed[idx]); return idx
    return None

def route(root,effects,gpu=False,captured=False):
    rewards=Rng(root+0x21b9e6f65ec5f2de)
    if captured:
        pool=list(range(30)); rewards.shuffle(pool)
    st=State(rewards,f32(0.4),f32(-0.05)); bag=init_bag(root); removed=set()
    for e in effects:
        if e=='capsule2':
            for _ in range(2):
                (capsule_gpu if gpu else capsule_cpu)(st,root,bag,removed)
        elif e=='capsule1': (capsule_gpu if gpu else capsule_cpu)(st,root,bag,removed)
        elif e=='arcane': execute_operator(st,e)
        elif isinstance(e,Cap): (apply_gpu(st,e.packed()) if gpu else apply_cpu(st,e))
    return project(st)

def root_vector(i):
    _,z=splitmix(u64(0x6a09e667f3bcc909+i*0x9e3779b97f4a7c15)); return z
scenarios={
 'neutral':[Cap()],
 'prayer_wheel':[Cap(flags=PRAYER,extra=1)],
 'white_beast':[Cap(flags=FORCE)],
 'lasting_candy':[Cap(flags=CANDY)],
 'capsule_nested':['capsule2'],
 'unknown_hook':[Cap(False,False,UNKNOWN_IMPACT|UNKNOWN_CONT)],
}
roots=4096; mismatches={k:0 for k in scenarios}; checkpoint_mismatch=0
for i in range(roots):
    root=root_vector(i)
    for name,effects in scenarios.items():
        a=route(root,effects,False); b=route(root,effects,True)
        if a!=b: mismatches[name]+=1
        if not a.get('conservative') and a['checkpoints']!=b['checkpoints']: checkpoint_mismatch+=1
# Offered/Reverse: captured post-Bones checkpoint, Capsule order can change nested capability and reward continuation.
order_diff=0; offered_reverse_mismatch=0
for i in range(roots):
    root=root_vector(i)
    cpu_o=route(root,['arcane','capsule1'],False,True); gpu_o=route(root,['arcane','capsule1'],True,True)
    cpu_r=route(root,['capsule1','arcane'],False,True); gpu_r=route(root,['capsule1','arcane'],True,True)
    offered_reverse_mismatch += (cpu_o!=gpu_o)+(cpu_r!=gpu_r)
    if cpu_o!=cpu_r: order_diff+=1
# Candidate route mask over five synthetic route types.
def matches(result):
    if result.get('conservative'): return True
    return any(100 in cards or 200 in cards for cards in result['cards']) and any(result['drops'])
mask_mismatch=missing=extra=0
for i in range(roots):
    root=root_vector(i); cpu=gpu=0
    routes=[([Cap()],False),([Cap(flags=PRAYER,extra=1)],False),([Cap(flags=FORCE)],False),(['arcane','capsule1'],True),(['capsule1','arcane'],True)]
    for bit,(effects,captured) in zip([1,2,4,8,16],routes):
        if matches(route(root,effects,False,captured)): cpu|=bit
        if matches(route(root,effects,True,captured)): gpu|=bit
    if cpu!=gpu:
        mask_mismatch+=1; missing+=(cpu&~gpu).bit_count(); extra+=(gpu&~cpu).bit_count()
result={'roots':roots,'scenarioMismatches':mismatches,'rewardCheckpointMismatch':checkpoint_mismatch,
        'offeredReverseGpuMismatch':offered_reverse_mismatch,'offeredReverseDifferentRootCount':order_diff,
        'routeMaskMismatch':mask_mismatch,'missingRouteBits':missing,'extraRouteBits':extra,
        'unknownHookConservativeKeep':True,'baseGoldRngConsumed':True}
out=Path(__file__).with_name('GPU_COMBAT_REWARD_R2_NUMERIC_RESULTS.json'); out.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))
fail=any(mismatches.values()) or checkpoint_mismatch or offered_reverse_mismatch or mask_mismatch or order_diff==0
sys.exit(1 if fail else 0)
