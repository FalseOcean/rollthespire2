#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
page = (root / 'Ui/Pages/Search/SearchPage.cs').read_text(encoding='utf-8')
integrated = (root / 'Ui/Pages/Search/SearchIntegratedFilterEditor.cs').read_text(encoding='utf-8')

checks = {}

def check(name, condition):
    checks[name] = bool(condition)

summary_start = page.index('    private void UpdateConditionSummary()')
summary_end = page.index('    private void ResetMetrics()', summary_start)
summary = page[summary_start:summary_end]

check('visible summary counts Neow typed page', '_categoryHost.NeowPage.EnabledConditionCount' in summary)
check('visible summary counts BossMap typed page', '_categoryHost.BossMapPage.EnabledConditionCount' in summary)
check('visible summary counts Ancient typed page', '_categoryHost.AncientPage.EnabledConditionCount' in summary)
check('visible summary counts Relic typed page', '_categoryHost.RelicPage.EnabledConditionCount' in summary)
check('visible summary counts CombatReward typed page', '_categoryHost.CombatRewardPage.EnabledConditionCount' in summary)
check('visible summary counts Event typed page', '_categoryHost.EventPage.EnabledConditionCount' in summary)
check('visible summary binds typed count directly', '_categoryNavigation.SetEnabledConditionCount(count);' in summary)
check('hidden legacy integrated count cannot inflate nav summary', '_categoryNavigation.SetEnabledConditionCount(_integratedFilters.EnabledConditionCount);' not in summary)
check('legacy editor remains hidden compatibility storage', 'Visible = false' in page and 'legacyDraftStorage' in page)
check('legacy ExtendedConditionCount still isolated from player summary', 'ExtendedConditionCount()' in integrated)

persisted_start = page.index('    private void AddPersistedResultCard')
persisted_end = page.index('    public override void _Process', persisted_start)
persisted = page[persisted_start:persisted_end]
live_start = page.index('    public void AddCandidate(')
live_end = page.index('    private string BuildCandidateTitle', live_start)
live = page[live_start:live_end]

check('persisted result card has no query fingerprint tooltip', 'Query: ' not in persisted and 'panel.TooltipText = string.Empty;' in persisted)
check('live result card has no evidence tooltip', 'candidate.MatchEvidence' not in live and 'panel.TooltipText = string.Empty;' in live)
check('large candidate summary tooltip helper removed', 'BuildCandidateSummary(' not in page)
check('copy action retained', 'CandidateCopyRequested?.Invoke(candidate.Seed)' in live)
check('analyze action retained', 'CandidateOpenRequested?.Invoke(candidate)' in live)

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ': ' + name)
if failed:
    raise SystemExit('FAILED: ' + ', '.join(failed))
print(f'WorkshopReleaseUiConditionCountResultTooltipHotfix1={len(checks)}/{len(checks)}_PASS')
