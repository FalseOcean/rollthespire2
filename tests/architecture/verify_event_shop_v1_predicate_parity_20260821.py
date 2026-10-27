from pathlib import Path
import runpy, io, contextlib

ROOT = Path(__file__).resolve().parent
# Reuse the independently encoded Beta111 RNG/vector substrate without duplicating constants.
with contextlib.redirect_stdout(io.StringIO()):
    ns = runpy.run_path(str(ROOT / 'verify_event_shop_v1_numeric_vectors_20260821.py'))
Rng=ns['Rng']; xxh64=ns['xxh64']; event_seed=ns['event_seed']; MASK=ns['MASK']

TRASH_GRAB=list(range(10)); TRASH_DIVE=list(range(5)); FAKE=list(range(9)); COLORS=list(range(5))
H_TRASH=xxh64(b'TRASH_HEAP'); H_FAKE=xxh64(b'FAKE_MERCHANT'); H_COLOR=xxh64(b'COLORFUL_PHILOSOPHERS'); H_SHOPS=xxh64(b'shops')

def exact_event(root, slot, owner, unlocked):
    grab=TRASH_GRAB[Rng((root+slot+H_TRASH)&MASK).ni(10)]
    dive=TRASH_DIVE[Rng((root+slot+H_TRASH)&MASK).ni(5)]
    r=Rng((root+H_FAKE)&MASK); fake=FAKE[:]
    for count in range(9,1,-1):
        j=r.ni(count); fake[j],fake[count-1]=fake[count-1],fake[j]
    cand=[x for x in COLORS if x != owner and x in unlocked]
    r=Rng((root+slot+H_COLOR)&MASK)
    while len(cand)>min(3,len(cand)):
        cand.pop(r.ni(len(cand)))
    return grab,dive,tuple(fake[:6]),tuple(cand)

def fast_event_reject(root, slot, owner, unlocked, conds, colorful_authority=True):
    grab=dive=None; fake=None; colors=None
    kinds={c[0] for c in conds}
    if 'grab' in kinds: grab=TRASH_GRAB[Rng((root+slot+H_TRASH)&MASK).ni(10)]
    if 'dive' in kinds: dive=TRASH_DIVE[Rng((root+slot+H_TRASH)&MASK).ni(5)]
    if 'fake' in kinds:
        r=Rng((root+H_FAKE)&MASK); fake=FAKE[:]
        for count in range(9,1,-1):
            j=r.ni(count); fake[j],fake[count-1]=fake[count-1],fake[j]
        fake=fake[:6]
    if 'color' in kinds and colorful_authority:
        colors=[x for x in COLORS if x != owner and x in unlocked]
        r=Rng((root+slot+H_COLOR)&MASK)
        target=min(3,len(colors))
        while len(colors)>target: colors.pop(r.ni(len(colors)))
    for kind,target in conds:
        if kind=='grab' and grab != target: return True
        if kind=='dive' and dive != target: return True
        if kind=='fake' and target not in fake: return True
        if kind=='color' and colorful_authority and target not in colors: return True
    return False

def exact_shop(root, max_ordinal, u_count=7, r_count=5):
    shops=Rng((root+H_SHOPS)&MASK); result=[]
    for ordinal in range(1,max_ordinal+1):
        incoming=shops.state()
        for _ in range(12): shops.dbl()
        u=shops.ni(u_count); assert shops.calls-(ordinal-1)*28 == 13
        shops.dbl(); r=shops.ni(r_count); assert shops.calls-(ordinal-1)*28 == 15
        for _ in range(13): shops.dbl()
        assert shops.calls == ordinal*28
        result.append((ordinal,u,r,incoming,shops.state()))
    return result

def fast_shop_reject(root, conds, authority=True, u_count=7, r_count=5):
    if not authority: return False
    max_ord=max(o for o,_,_ in conds)
    projected=exact_shop(root,max_ord,u_count,r_count)
    by_ord={o:(u,r) for o,u,r,_,_ in projected}
    for o,slot,target in conds:
        actual=by_ord[o][0 if slot=='U' else 1]
        if actual != target: return True
    return False

def matches_sequence(actual, slots, ordered):
    if ordered:
        return all(target is None or actual[index] == target
                   for index, target in enumerate(slots))
    for target in set(target for target in slots if target is not None):
        if actual.count(target) < slots.count(target):
            return False
    return True

def fast_shop_sequence_reject(root, slot, count, slots, ordered, authority=True, u_count=7, r_count=5):
    # Missing authority remains ConservativeKeep. With authority, this mirrors
    # the Fast candidate predicate and is checked against the canonical result.
    if not authority:
        return False
    actual = [u if slot == 'U' else r for _, u, r, _, _ in exact_shop(root, count, u_count, r_count)]
    return not matches_sequence(actual, slots[:count], ordered)

# Event predicate matrix: true/false, shared invariant, Colorful 2/3/4 eligible candidate behavior.
root=0xB5CEE4C943D43C7E
for slot in (0,1):
    grab,dive,fake,colors=exact_event(root,slot,owner=1,unlocked=set(COLORS))
    assert not fast_event_reject(root,slot,1,set(COLORS),[('grab',grab),('dive',dive),('fake',fake[0]),('color',colors[0])])
    assert fast_event_reject(root,slot,1,set(COLORS),[('grab',(grab+1)%10)])
    assert fast_event_reject(root,slot,1,set(COLORS),[('dive',(dive+1)%5)])
    assert fast_event_reject(root,slot,1,set(COLORS),[('fake',next(x for x in FAKE if x not in fake))])
    assert fast_event_reject(root,slot,1,set(COLORS),[('color',1)])  # owner cannot be offered
# Missing Colorful authority is ConservativeKeep, never a Fast false negative.
assert not fast_event_reject(root,0,1,set(COLORS),[('color',4)],colorful_authority=False)
# Candidate counts <=3 consume no removal and return all; four candidates remove exactly one.
for eligible in ({0,2},{0,2,3},{0,2,3,4}):
    _,_,_,colors=exact_event(root,0,owner=1,unlocked=set(eligible)|{1})
    if len(eligible)<=3: assert set(colors)==set(eligible)
    else: assert len(colors)==3 and set(colors).issubset(eligible)
# Fake is shared: inventory must be slot invariant.
assert exact_event(root,0,1,set(COLORS))[2] == exact_event(root,1,1,set(COLORS))[2]

# Shop predicate matrix: #1 U/R, #2, #5, rare-only, sparse and multi-merchant.
proj=exact_shop(root,5)
by={o:(u,r) for o,u,r,_,_ in proj}
for o in (1,2,5):
    u,r=by[o]
    assert not fast_shop_reject(root,[(o,'U',u),(o,'R',r)])
    assert fast_shop_reject(root,[(o,'U',(u+1)%7)])
assert not fast_shop_reject(root,[(5,'R',by[5][1])])
assert not fast_shop_reject(root,[(1,'U',by[1][0]),(5,'R',by[5][1])])
# Missing Exact authority is ConservativeKeep.
assert not fast_shop_reject(root,[(1,'U',999)],authority=False)

# Sequence parity: ordered positional slots, unordered multiplicities, sparse N-shapes,
# both U/R lanes, and a negative target must agree with canonical Exact. These are
# candidate-only rejection checks; a conservative keep remains valid for Exact.
for slot in ('U', 'R'):
    actual = [u if slot == 'U' else r for _, u, r, _, _ in proj]
    ordered_sparse = [actual[0], None, actual[2], None, actual[4]]
    unordered_sparse = [None, actual[3], actual[1], None, actual[4]]
    assert matches_sequence(actual, ordered_sparse, True)
    assert matches_sequence(actual, unordered_sparse, False)
    assert not fast_shop_sequence_reject(root, slot, 5, ordered_sparse, True)
    assert not fast_shop_sequence_reject(root, slot, 5, unordered_sparse, False)
    assert fast_shop_sequence_reject(root, slot, 5, [actual[0], None, (actual[2] + 1) % (7 if slot == 'U' else 5), None, actual[4]], True)
    assert not fast_shop_sequence_reject(root, slot, 5, [999, None, None, None, None], True, authority=False)

    # Counterexample that the old relative-order/subsequence matcher accepted:
    # X,A,B against [A,*,B]. Positional semantics must reject when Shop #1 != A.
    counter_actual = [999, actual[1], actual[2], actual[3], actual[4]]
    counter_query = [actual[1], None, actual[2], None, None]
    assert not matches_sequence(counter_actual, counter_query, True)

# N=1, N=2 and N=5 all use the same canonical prefix, including a one-slot
# ordered shape and an unordered duplicate-count shape when the data permits it.
for count in (1, 2, 5):
    for slot in ('U', 'R'):
        actual = [u if slot == 'U' else r for _, u, r, _, _ in proj[:count]]
        slots = [None] * count
        slots[0] = actual[0]
        assert not fast_shop_sequence_reject(root, slot, count, slots, True)
# Every outgoing state is exactly 28 calls after incoming and feeds the next merchant.
for idx,item in enumerate(proj):
    o,u,r,incoming,outgoing=item
    assert outgoing[4] == o*28
    if idx+1 < len(proj): assert outgoing == proj[idx+1][3]

# Range parity: for exact authority, Fast rejection iff exact predicate does not match.
for seed in range(0,10000,37):
    e=exact_event(seed,0,1,set(COLORS))
    cond=[('grab',e[0]),('fake',e[2][0]),('color',e[3][0])]
    assert fast_event_reject(seed,0,1,set(COLORS),cond) is False
    sp=exact_shop(seed,5); sb={o:(u,r) for o,u,r,_,_ in sp}
    scond=[(1,'U',sb[1][0]),(3,'R',sb[3][1]),(5,'U',sb[5][0])]
    assert fast_shop_reject(seed,scond) is False

print('PASS: Event/Shop v1 predicate matrix + CPU Fast conservative-keep/range parity')
