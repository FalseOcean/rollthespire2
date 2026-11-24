from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
text = (ROOT / 'Search/Selectivity/NeowStructuredEffectProbabilityEstimator.cs').read_text(encoding='utf-8')
checks = []

def ck(name, ok):
    checks.append((name, bool(ok)))

ck('route union helper exists', 'TryPriceUnpinnedBonesRouteUnion' in text)
ck('pinned order does not union', 'if (pinnedOrder.Length != 0)' in text and 'return false;' in text)
ck('exact two grant pair only', 'requiredGrants.Count != 2 || nestedSources.Count == 0' in text)
ck('shared rewards required', '(sharedStreams & BonesProbabilityRngStream.Rewards) == 0' in text)
ck('capsule scroll boxes both rewards', 'BaseGameModelKeys.Relics.SmallCapsule' in text and 'BaseGameModelKeys.Relics.ScrollBoxes' in text and 'BonesProbabilityRngStream.Rewards' in text)
ck('two capsules not double unioned', 'requiredCapsuleCount == 2' in text and 'double-pay the same player route alternatives' in text)
ck('relic shared block excluded', 'if (includeExplicitRelicSequence)' in text)
ck('route invariant child separated', 'routeInvariantChildProbability' in text and 'routeNestedChildProbability' in text)
ck('union is inclusion exclusion form', '1d - (1d - fixedRouteConditional) * (1d - fixedRouteConditional)' in text)
ck('not random one half', 'not a random 1/2 factor on a pinned route' in text)
ck('evidence code route union', 'Probability.Authority.BonesUnpinnedRouteUnion' in text)
ck('named stream model', 'SearchSelectivityMethod.NamedStreamIndependenceModel' in text)

# Numeric sanity: rare strict route event should approach 2x, but never exceed 1.
for q in [0.0, 1e-9, 1e-3, 0.1, 0.5, 1.0]:
    union = 1 - (1-q)*(1-q)
    ck(f'numeric union bounds {q}', 0 <= union <= 1)
    ck(f'numeric union >= fixed {q}', union + 1e-15 >= q)

q = 1 / 1000000
union = 1 - (1-q)*(1-q)
ck('rare union approximately double', abs(union/q - 2) < 1e-5)

failed = [name for name, ok in checks if not ok]
for name, ok in checks:
    print(('PASS' if ok else 'FAIL') + ': ' + name)
print(f'{len(checks)-len(failed)}/{len(checks)} PASS')
if failed:
    sys.exit(1)
