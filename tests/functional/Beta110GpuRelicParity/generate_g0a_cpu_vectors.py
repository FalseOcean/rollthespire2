#!/usr/bin/env python3
import json
from pathlib import Path

MASK=(1<<64)-1
SM_INC=0x9e3779b97f4a7c15
SM1=0xbf58476d1ce4e5b9
SM2=0x94d049bb133111eb
UP_FRONT=0x1a8a7b60da243e80
BOUNDS=[2,3,5,7,16,31,205,4096]

def rotl(x,n): return ((x<<n)|(x>>(64-n)))&MASK

def sm_next(state):
    state=(state+SM_INC)&MASK
    x=state
    x=((x^(x>>30))*SM1)&MASK
    x=((x^(x>>27))*SM2)&MASK
    return state,(x^(x>>31))&MASK

def init(seed):
    state=seed; out=[]
    for _ in range(4): state,x=sm_next(state); out.append(x)
    return out

def next_u64(s):
    s0,s1,s2,s3=s
    result=(rotl((s1*5)&MASK,7)*9)&MASK
    t=(s1<<17)&MASK
    s2^=s0; s3^=s1; s1^=s2; s0^=s3; s2^=t; s3=rotl(s3,45)
    s[:]=[s0&MASK,s1&MASK,s2&MASK,s3&MASK]
    return result

def roots():
    out=[0,1,2,3,MASK,MASK-1,0x0123456789abcdef,0xfedcba9876543210,
         0x8b4cc2b2e5f3a3d1,0x226b1feabe43cba6,0x8000000000000000,0x7fffffffffffffff,
         0xaaaaaaaaaaaaaaaa,0x5555555555555555,0x00000000ffffffff,0xffffffff00000000,
         0x1020304050607080,0x8877665544332211,0x9e3779b97f4a7c15,0xbf58476d1ce4e5b9,
         0x94d049bb133111eb,0x1a8a7b60da243e80,0xdeadbeefcafebabe,0xc001d00d5eed1234,
         0x1111111111111111,0x2222222222222222,0x3333333333333333,0x4444444444444444,
         0x6666666666666666,0x7777777777777777,0x9999999999999999,0xbbbbbbbbbbbbbbbb]
    state=0xd1b54a32d192ed03
    while len(out)<64:
        state=(state+SM_INC)&MASK
        x=state
        x=((x^(x>>30))*SM1)&MASK
        x=((x^(x>>27))*SM2)&MASK
        out.append((x^(x>>31))&MASK)
    return out

def build_payload():
    vectors=[]
    for root in roots():
        seed=(root+UP_FRONT)&MASK
        initial=init(seed)
        state=initial.copy(); raw=[next_u64(state) for _ in range(8)]
        state=initial.copy(); ints=[]
        for bound in BOUNDS:
            sample=(next_u64(state)>>11)*(2**-53)
            ints.append(int(sample*bound))
        vectors.append({
            'rootHashHex':f'{root:016x}',
            'upFrontSeedHex':f'{seed:016x}',
            'splitMixStateHex':[f'{x:016x}' for x in initial],
            'xoshiroOutputsHex':[f'{x:016x}' for x in raw],
            'nextIntBounds':BOUNDS,
            'nextIntOutputs':ints,
        })
    return {
        'schema':'rollthespire2.beta110.g0a-rng-vectors.v1',
        'upFrontStreamHashHex':f'{UP_FRONT:016x}',
        'rootCount':len(vectors),
        'vectors':vectors,
    }

def main():
    out=Path(__file__).with_name('Beta110GpuRelicG0A_CPU_VECTORS.json')
    out.write_text(json.dumps(build_payload(),indent=2)+'\n',encoding='utf-8')
    print(out)

if __name__ == '__main__':
    main()
