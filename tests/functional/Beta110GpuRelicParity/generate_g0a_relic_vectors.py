#!/usr/bin/env python3
import json
from pathlib import Path
from generate_g0a_cpu_vectors import MASK, UP_FRONT, roots, init, next_u64

POOLS={
 'shared0':[10,11,12,13,14],
 'shared1':[20,21,22],
 'common':[100,101,900,102,103,104,105],
 'uncommon':[200,201,202,203,204,205],
 'rare':[300,301,302,303,304],
 'shop':[400,401,402,403,404,405,406,407],
}
SHOP_ALLOWED={400,402,404,405,407}

def next_int(state,bound):
    return int(((next_u64(state)>>11)*(2**-53))*bound)

def shuffle(state,values):
    values=list(values)
    for count in range(len(values),1,-1):
        selected=next_int(state,count)
        values[selected],values[count-1]=values[count-1],values[selected]
    return values

vectors=[]
for root in roots():
    state=init((root+UP_FRONT)&MASK)
    shuffle(state,POOLS['shared0'])
    shuffle(state,POOLS['shared1'])
    common=shuffle(state,POOLS['common'])[:2]
    uncommon=shuffle(state,POOLS['uncommon'])[:2]
    rare=shuffle(state,POOLS['rare'])[:2]
    shop_shuffled=shuffle(state,POOLS['shop'])
    shop=[x for x in reversed(shop_shuffled) if x in SHOP_ALLOWED][:2]
    vectors.append({
      'rootHashHex':f'{root:016x}',
      'commonPrefix':common,
      'uncommonPrefix':uncommon,
      'rarePrefix':rare,
      'shopPrefix':shop,
      'finalRngStateHex':[f'{x:016x}' for x in state],
    })

payload={
 'schema':'rollthespire2.beta110.g0a-relic-vectors.v1',
 'fixture':'SyntheticAllBucketPrefixes',
 'virtualModRelicDenseId':900,
 'poolTruncation':False,
 'rootCount':len(vectors),
 'vectors':vectors,
}
out=Path(__file__).with_name('Beta110GpuRelicG0A_RELIC_VECTORS.json')
out.write_text(json.dumps(payload,indent=2)+'\n',encoding='utf-8')
print(out)
