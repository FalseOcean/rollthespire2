#!/usr/bin/env python3
"""Pure business-semantic fixtures for sparse ordered/unordered Search constraints."""
from itertools import permutations

checks=[]
def ck(name, ok): checks.append((name,bool(ok)))

def kale_ordered(slots, groups):
    return all(target is None or target in groups[i] for i,target in enumerate(slots))

def card_ordered(slots, battles):
    return all(target is None or target in battles[i] for i,target in enumerate(slots))

def assign_unordered(reqs, battles):
    reqs=[r for r in reqs if r is not None]
    if not reqs: return True
    if len(reqs)>len(battles): return False
    for battle_indexes in permutations(range(len(battles)), len(reqs)):
        if all(reqs[i] in battles[battle_indexes[i]] for i in range(len(reqs))): return True
    return False

def potion_match(req, outcome):
    # outcome None=no drop, string=drop identity
    kind, key = req
    if kind=='Neutral': return True
    if kind=='NoDrop': return outcome is None
    if kind=='DropAny': return outcome is not None
    if kind=='DropSpecific': return outcome==key
    return False

def potion_ordered(slots, battles):
    return all(potion_match(req,battles[i]) for i,req in enumerate(slots))

def potion_unordered(slots, battles):
    reqs=[r for r in slots if r[0] != 'Neutral']
    if len(reqs)>len(battles): return False
    for idxs in permutations(range(len(battles)),len(reqs)):
        if all(potion_match(reqs[i],battles[idxs[i]]) for i in range(len(reqs))): return True
    return False

# Kale sparse positional slots.
ck('kale_null_A_matches_group2', kale_ordered([None,'A'], [{'X'},{'A'}]))
ck('kale_null_A_rejects_A_group1', not kale_ordered([None,'A'], [{'A'},{'X'}]))
ck('kale_A_null_matches_group1', kale_ordered(['A',None], [{'A'},{'X'}]))
ck('kale_A_B_order_distinct', kale_ordered(['A','B'],[{'A'},{'B'}]) and not kale_ordered(['A','B'],[{'B'},{'A'}]))

# Combat Card sparse ordered and unordered.
ck('card_slot2_only_ordered', card_ordered([None,'A',None],[{'X'},{'A'},{'Y'}]))
ck('card_slot2_not_compacted', not card_ordered([None,'A',None],[{'A'},{'X'},{'Y'}]))
ck('card_sparse_A_null_B', card_ordered(['A',None,'B'],[{'A'},{'X'},{'B'}]))
ck('card_slot3_only', card_ordered([None,None,'A'],[{'X'},{'Y'},{'A'}]))
ck('card_unordered_sparse_any_distinct_battle', assign_unordered([None,'A',None],[{'X'},{'A'},{'Y'}]))
ck('card_unordered_duplicate_requires_two_battles', assign_unordered(['A','A'],[{'A'},{'A'},{'X'}]) and not assign_unordered(['A','A'],[{'A'},{'X'},{'Y'}]))
ck('card_count_window_matters', not assign_unordered(['A'],[{'X'}]) and assign_unordered(['A'],[{'X'},{'A'}]))

# Potion Neutral / NoDrop / DropAny / Specific.
N=('Neutral',None); ND=('NoDrop',None); DA=('DropAny',None); DS=('DropSpecific','Strength')
ck('potion_neutral', potion_ordered([N],[None]) and potion_ordered([N],['Any']))
ck('potion_nodrop', potion_ordered([ND],[None]) and not potion_ordered([ND],['Any']))
ck('potion_drop_any', potion_ordered([DA],['Any']) and not potion_ordered([DA],[None]))
ck('potion_drop_specific', potion_ordered([DS],['Strength']) and not potion_ordered([DS],['Dexterity']))
ck('potion_sparse_ordered', potion_ordered([N,ND,DS],['Other',None,'Strength']))
ck('potion_unordered_assignment', potion_unordered([N,ND,DS],['Strength','Other',None]))

# Card and Potion unordered assignment families are independent.
card_battles=[{'A'},{'B'},{'X'}]
potion_battles=['Strength',None,'Other']
ck('independent_card_potion_assignments',
   assign_unordered(['A','B'], card_battles) and
   potion_unordered([ND,DS,N], potion_battles))

# Bones route order is a selected route identity, not an extra Bernoulli event.
def bones_offer_prob(pool_size): return 2/(pool_size*(pool_size-1))
ck('bones_route_order_no_half_probability', bones_offer_prob(10) == 2/(10*9))

failed=[n for n,o in checks if not o]
for n,o in checks: print(('PASS' if o else 'FAIL')+': '+n)
print(f'OrderedSparseSemanticFixtures={len(checks)-len(failed)}/{len(checks)}_PASS')
if failed:
    print('FAILED='+','.join(failed)); raise SystemExit(1)
