MASK=(1<<64)-1
P1=11400714785074694791; P2=14029467366897019727; P3=1609587929392839161; P4=9650029242287828579; P5=2870177450012600261
INC=11400714819323198485; M1=13787848793156543929; M2=10723151780598845931

def rol(x,r): return ((x<<r)|(x>>(64-r))) & MASK

def xxh64(data: bytes, seed=0):
    n=len(data); off=0
    def r64(o): return int.from_bytes(data[o:o+8], 'little')
    def r32(o): return int.from_bytes(data[o:o+4], 'little')
    def rnd(a, x):
        a=(a+x*P2)&MASK; a=rol(a,31); return (a*P1)&MASK
    def merge(a,v): return ((a ^ rnd(0,v))*P1 + P4)&MASK
    if n>=32:
        v1=(seed+P1+P2)&MASK; v2=(seed+P2)&MASK; v3=seed&MASK; v4=(seed-P1)&MASK
        limit=n-32
        while off<=limit:
            v1=rnd(v1,r64(off)); off+=8; v2=rnd(v2,r64(off)); off+=8
            v3=rnd(v3,r64(off)); off+=8; v4=rnd(v4,r64(off)); off+=8
        h=(rol(v1,1)+rol(v2,7)+rol(v3,12)+rol(v4,18))&MASK
        for v in (v1,v2,v3,v4): h=merge(h,v)
    else: h=(seed+P5)&MASK
    h=(h+n)&MASK
    while off<=n-8:
        h ^= rnd(0,r64(off)); h=(rol(h,27)*P1+P4)&MASK; off+=8
    if off<=n-4:
        h ^= (r32(off)*P1)&MASK; h=(rol(h,23)*P2+P3)&MASK; off+=4
    while off<n:
        h ^= (data[off]*P5)&MASK; h=(rol(h,11)*P1)&MASK; off+=1
    h ^= h>>33; h=(h*P2)&MASK; h ^= h>>29; h=(h*P3)&MASK; h ^= h>>32
    return h&MASK

def sm(state):
    state=(state+INC)&MASK; z=state
    z=((z^(z>>30))*M1)&MASK; z=((z^(z>>27))*M2)&MASK; z^=z>>31
    return state,z&MASK

class Rng:
    def __init__(self, seed):
        st=seed; vals=[]
        for _ in range(4): st,z=sm(st); vals.append(z)
        self.s0,self.s1,self.s2,self.s3=vals; self.calls=0
    def u64(self):
        self.calls+=1; out=(rol((self.s1*5)&MASK,7)*9)&MASK; t=(self.s1<<17)&MASK
        self.s2^=self.s0; self.s3^=self.s1; self.s1^=self.s2; self.s0^=self.s3; self.s2^=t; self.s3=rol(self.s3,45)
        self.s0&=MASK; self.s1&=MASK; self.s2&=MASK; self.s3&=MASK
        return out
    def dbl(self): return (self.u64()>>11) * (2**-53)
    def ni(self,n): return int(self.dbl()*n)
    def state(self): return self.s0,self.s1,self.s2,self.s3,self.calls

def event_seed(root, slot, shared, entry):
    base=(root + (0 if shared else slot)) & MASK
    return (base + xxh64(entry.encode())) & MASK

root=0xB5CEE4C943D43C7E
assert xxh64(b'TRASH_HEAP') == 0xDDB3F371A68F3A0E
assert xxh64(b'FAKE_MERCHANT') == 0x05BA11ADB38CB7D6
assert xxh64(b'COLORFUL_PHILOSOPHERS') == 0xB0EA387CB39D0A7C

# Handoff/corrigendum golden vectors.
for slot, g, d in [(0,3,1),(1,6,3)]:
    seed=event_seed(root,slot,False,'TRASH_HEAP')
    assert seed == 0x9382D83AEA63768C + slot
    assert Rng(seed).ni(10)==g
    assert Rng(seed).ni(5)==d

fake_seed=event_seed(root,0,True,'FAKE_MERCHANT')
assert fake_seed == 0xBB88F676F760F454
assert fake_seed == event_seed(root,1,True,'FAKE_MERCHANT')
r=Rng(fake_seed); order=list(range(9)); draws=[]
for count in range(9,1,-1):
    j=r.ni(count); draws.append(j); order[j],order[count-1]=order[count-1],order[j]
assert draws == [1,1,2,2,2,1,1,1]
assert order[:6] == [0,4,3,7,5,6]

color_seed=event_seed(root,0,False,'COLORFUL_PHILOSOPHERS')
assert color_seed == 0x66B91D45F77146FA
assert Rng(color_seed).ni(4)==3  # filtered Ironclad candidate set removes DEFECT.

# Synthetic deterministic Shops checkpoint vector: U pool size 7, R pool size 5.
shops=Rng((root + xxh64(b'shops')) & MASK)
expected_slots=[(1,6,2,28),(2,5,1,56),(3,6,2,84),(4,1,4,112),(5,4,3,140)]
expected_states=[
(0xD009FAA69E4C734F,0xB252E9DB8D53A502,0x4429E0DD699507CD,0x1CABCFC83CBDBD3D,28),
(0x41451722E9ECCB0C,0x47C9789ECEEF417C,0xF04A239D4180C89F,0xC65A4B5B03F0E4ED,56),
(0xBA4E531F3539FA3F,0x8DCC0F321A0AF0B9,0xB0380D0334307BFA,0x940EA4090BC50BAB,84),
(0xB0E22AB796DD3BF3,0x85485F2036781380,0xEC54076CBDA30EB2,0xF1087F6FDB5F2398,112),
(0x25E0B9E3193A2569,0x9D63F1F45502C2E3,0xA48043D39115D63C,0xE8E9BD652256AAC1,140),
]
for idx in range(5):
    before=shops.calls
    for _ in range(12): shops.dbl()
    u=shops.ni(7); assert shops.calls-before==13
    shops.dbl()
    r=shops.ni(5); assert shops.calls-before==15
    for _ in range(13): shops.dbl()
    assert shops.calls-before==28
    assert (idx+1,u,r,shops.calls)==expected_slots[idx]
    assert shops.state()==expected_states[idx]

print('PASS: Event/Shop v1 Beta111 golden + synthetic Shops checkpoint vectors')
