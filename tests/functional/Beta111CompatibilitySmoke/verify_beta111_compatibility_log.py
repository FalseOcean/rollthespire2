from pathlib import Path
import argparse, re, sys
p=argparse.ArgumentParser()
p.add_argument('log')
a=p.parse_args()
t=Path(a.log).read_text(encoding='utf-8', errors='replace')
checks={
 'runtime-profile-beta111': re.search(r'Runtime initialized: game=0\.111\.0; profile=Beta111; compatibility=AcceptedCompatible; reference=0\.111\.0;',t) is not None,
 'rng-vectors': re.search(r'rngVectors=Passed\(\d+\)',t) is not None,
 'mainmenu-ready': 'minimalUiPatchTarget=MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu._Ready' in t,
 'mainmenu-stack': 'mainMenuVisibilityPatchTarget=MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu.OnSubmenuStackChanged' in t,
 'beta111-authority-probe': 'beta111CompatibilityAuthoritySmoke=true' in t,
 'salvo-uncommon': re.search(r'beta111CompatibilityAuthoritySmoke=true;[^\n]*salvoRarity=Uncommon',t) is not None,
 'splash-rare': re.search(r'beta111CompatibilityAuthoritySmoke=true;[^\n]*splashRarity=Rare',t) is not None,
 'authority-probe-passed': re.search(r'beta111CompatibilityAuthoritySmoke=true;[^\n]*passed=true',t) is not None,
 'accepted-not-beta110-fallback': 'latest-known-fallback:0.111.0->0.110.1' not in t and 'profile=Beta110; compatibility=LatestKnownFallback' not in t,
 'production-search-enabled-policy': 'Beta111PendingOwnerSmokeSearchFailClosed' not in t and 'ProductionSearch=FailClosed' not in t,
}
failed=[]
for k,v in checks.items():
    print(f'{k}={"PASS" if v else "FAIL"}')
    if not v: failed.append(k)
print(f'beta111CompatibilityRuntimeLog={len(checks)-len(failed)}/{len(checks)} PASS' + ('' if not failed else ';failed='+','.join(failed)))
sys.exit(1 if failed else 0)
