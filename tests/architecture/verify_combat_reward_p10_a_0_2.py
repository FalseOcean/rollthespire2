from pathlib import Path
import re, sys
root=Path(__file__).resolve().parents[2]
h=(root/'Search/Performance/CombatRewardHotPathP10ADiagnosticHarness.cs').read_text()
checks=[]
def check(name, cond): checks.append((name,bool(cond)))
check('common_battle1_fixture','c.Rarity == EffectCardRarity.Common' in h and 'P10ACommonRewardCardUnavailable' in h)
check('rare_battle1_fixture_removed','P10ARareRewardCardUnavailable' not in h)
check('exact_potion_identity_retained','new ModelKeySetFilter(Array.Empty<ModelKey>(), new[] { potion.PotionKey }, Array.Empty<ModelKey>())' in h)
check('battle3_mustdrop_retained','new NormalCombatRewardSearchCondition(3,' in h and 'NormalCombatPotionRequirement.MustDrop' in h)
check('admission_budget','private const long FixtureAdmissionRoots = 1L << 20;' in h)
check('admission_before_cases',h.index('ValidateStrictFixtureAdmission(') < h.index('Fixture[] fixtures'))
check('admission_baseline_only','CombatRewardP10A:FixtureAdmission' in h and 'Beta110GpuCombatRewardAlgorithmVariant.BaselineCurrent' in h)
check('admission_target','++observed < TargetMatches' in h and 'observed >= TargetMatches' in h)
check('admission_parity','metrics.Parity.Passed' in h and 'metrics.Parity.RouteMaskMismatch == 0' in h and 'metrics.Parity.MissingRouteBits == 0' in h and 'metrics.Parity.ExtraRouteBits == 0' in h)
check('admission_failclosed','P10AStrictFixtureAdmissionFailed' in h)
check('runtime_telemetry','combatRewardP10AFixtureAdmission=true' in h and 'observedCandidates=' in h and 'gpuSeeds=' in h)
check('donor_unchanged','StreamingPredicateEvaluation_CurrentBattleOnlyMaterialization' in h and 'StreamingMinimalObservableP10A' in h)
failed=[n for n,v in checks if not v]
for n,v in checks: print(f"{'PASS' if v else 'FAIL'} {n}")
print(f"P10-A.0.2 fixture reachability checks: {len(checks)-len(failed)}/{len(checks)} PASS")
sys.exit(1 if failed else 0)
