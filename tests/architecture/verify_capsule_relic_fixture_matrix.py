from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[2]
text=(ROOT/'tests/fixtures/CAPSULE_RELIC_COMPATIBILITY_FIXTURES_2026-08-14.md').read_text(encoding='utf-8')
checks=[]
def c(n,v): checks.append((n,bool(v))); print(('PASS' if v else 'FAIL')+': '+n)
for label in 'ABCDEFGHIJ': c('fixture:'+label, f'**{label} ' in text)
c('initial-coordinate', 'post-Capsule positions' in text and 'Initial RelicGrabBag' in text)
c('direct-no-fake-resident', 'Direct Capsule GPU resident scratch topology is not fabricated' in text)
c('bones-resident-covered', 'PreBones resident Capsule scratch is producer-internal' in text)
c('route-ambiguity-safe', 'ABI1 carries no route mask' in text and 'full explicit Initial RelicGrabBag predicates' in text)
c('exact-witness-fallback', 'Production Exact + SameRoute Witness' in text and 'Whole Recovery Batch' in text)
passed=sum(v for _,v in checks); print(f'RESULT {passed}/{len(checks)}'); sys.exit(0 if passed==len(checks) else 1)
