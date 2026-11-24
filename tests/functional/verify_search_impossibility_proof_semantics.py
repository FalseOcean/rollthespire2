#!/usr/bin/env python3
"""Small semantic fixtures for authoritative Search-impossibility proof behavior."""
from dataclasses import dataclass

checks=[]
def ck(name, cond): checks.append((name,bool(cond)))

# Minimal unique-lane existence oracle mirroring the shared-fact invariant used by
# the C# preflight: a Capsule identity fixed at one ordinal cannot simultaneously
# satisfy a different exact identity at that same ordinal.
def capsule_exact_slot_possible(capsule_target, capsule_slot, relic_target, relic_slot):
    if capsule_slot == relic_slot:
        return capsule_target == relic_target
    # Unique sequence identity cannot occupy two distinct exact positions.
    if capsule_target == relic_target:
        return False
    return True

ck('capsule_relic_same_slot_same_identity_possible',
   capsule_exact_slot_possible('A',1,'A',1))
ck('capsule_relic_same_slot_different_identity_impossible',
   not capsule_exact_slot_possible('A',1,'B',1))
ck('capsule_relic_same_identity_two_slots_impossible',
   not capsule_exact_slot_possible('A',1,'A',2))
ck('capsule_relic_distinct_slots_distinct_identity_possible',
   capsule_exact_slot_possible('A',1,'B',2))

# Missing/partial authority must never be promoted into an impossible proof.
def preflight(authority_exact, structural_conflict):
    if not authority_exact:
        return 'PossibleOrUnknown'
    return 'Impossible' if structural_conflict else 'PossibleOrUnknown'

ck('unknown_authority_fail_open', preflight(False, True) == 'PossibleOrUnknown')
ck('exact_authority_conflict_rejects', preflight(True, True) == 'Impossible')
ck('exact_authority_possible_stays_runnable', preflight(True, False) == 'PossibleOrUnknown')

# Product boundary: Capsule×Shop is deliberately not promoted by this closeout.
def capsule_shop_proof(has_shop_condition):
    return 'NotProven' if has_shop_condition else 'EvaluateCURSharedFact'
ck('capsule_shop_out_of_scope_fail_open', capsule_shop_proof(True) == 'NotProven')

failed=[n for n,o in checks if not o]
for n,o in checks: print(('PASS' if o else 'FAIL')+': '+n)
print(f'SearchImpossibilityProofFixtures={len(checks)-len(failed)}/{len(checks)}_PASS')
if failed:
    print('FAILED='+','.join(failed)); raise SystemExit(1)
