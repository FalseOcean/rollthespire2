#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
advanced = (root / 'Ui/Pages/Advanced/AdvancedPage.cs').read_text(encoding='utf-8')
shell = (root / 'Ui/Shell/AppShell.cs').read_text(encoding='utf-8')
persistence = (root / 'Ui/Persistence/SearchWorkspacePersistence.cs').read_text(encoding='utf-8')
en = (root / 'Localization/rt2_ui/en.json').read_text(encoding='utf-8')
zh = (root / 'Localization/rt2_ui/zh.json').read_text(encoding='utf-8')

checks = {}
def check(name, condition):
    checks[name] = bool(condition)

check('release diagnostics gate is hard false', 'private const bool ExposeWorkshopDeveloperDiagnostics = false;' in advanced)
for name in (
    'gpuPanel',
    'gpuNeowDiagnosticsPanel',
    'performanceMatrixPanel',
    'compositionBenchmarkPanel',
    'ancientOptionPreGateBenchmarkPanel',
    'selectivityDiagnosticPanel',
    'pipelineBottleneckPanel',
    'gpuStartupP7Panel',
    'gpuExecutionContextP8Panel',
    'neowGpuCapabilityP9Panel',
    'ancientOptionDensityBridgeP91Panel',
    'combatRewardP10APanel',
    'combatRewardP10RP1Panel',
    'productionPlannerPanel',
    'diagnosticsPanel',
):
    check(f'diagnostic panel hidden:{name}', f'{name}.Visible = ExposeWorkshopDeveloperDiagnostics;' in advanced)

check('long-lived developer tools polling remains active', 'SetProcess(true);' in advanced and 'RefreshNeowPerformanceMatrixV1State();' in advanced)
check('gpu validation controller cannot auto-enable from hidden release UI', 'ExposeWorkshopDeveloperDiagnostics && _settings.EnableExperimentalGpuRelicComputeValidation' in advanced)
check('player performance settings remain visible', 'column.AddChild(performancePanel);' in advanced and 'performancePanel.Visible = ExposeWorkshopDeveloperDiagnostics' not in advanced)
check('player search range settings remain visible', 'column.AddChild(searchRangePanel);' in advanced and 'searchRangePanel.Visible = ExposeWorkshopDeveloperDiagnostics' not in advanced)
check('complex prediction is hidden on release player surface', 'var complexPanel = new PanelContainer { Visible = false };' in advanced)
check('obsolete analysis raw-queue display removed', 'analysisDisplayPanel' not in advanced and 'ShowRawEventPoolSequence' not in advanced)
check('advanced subtitle no longer advertises diagnostics en', 'diagnostic capabilities' not in en and 'presentation options for the Workshop release' in en)
check('advanced subtitle no longer advertises diagnostics zh', '诊断能力' not in zh and 'Workshop 发布版中低频使用的搜索、预测与显示选项' in zh)

# Live game locale is the only UI-language authority.
init_marker = '_languageCode = ResolveUiLanguageCode();'
check('startup language follows TranslationServer', init_marker in shell)
check('persisted language no longer startup authority', '_searchPersistence.Preferences.Language' not in shell)
check('language poll field exists', 'private double _languagePollRemaining;' in shell)
check('language poll runs on main thread process', '_languagePollRemaining -= delta;' in shell and 'RefreshLanguageFromGameLocale();' in shell)
check('language poll bounded to quarter second', '_languagePollRemaining = 0.25d;' in shell)
check('open path refreshes locale immediately', 'public void Open()\n    {\n        RefreshLanguageFromGameLocale();' in shell)
check('hot reload compares normalized locale', 'string liveLanguage = ResolveUiLanguageCode();' in shell and 'string.Equals(liveLanguage, _languageCode, StringComparison.Ordinal)' in shell)
check('hot reload rebuilds localization providers', 'SetLanguage(liveLanguage);' in shell and '_uiText = JsonUiTextProvider.Create(_languageCode);' in shell and '_contentNames = RuntimeGameContentNameResolver.Create(_languageCode);' in shell)
check('hot reload reapplies all cached pages', '_pageHost!.ApplyLocalization(_uiText, _contentNames);' in shell)
check('hot reload reapplies shell chrome', '_header!.ApplyLocalization(_uiText);' in shell and '_navigation!.ApplyLocalization(_uiText);' in shell)
check('hot reload emits runtime marker', 'ui1LanguageHotReloaded=true' in shell)
check('persisted language documented as non-authority', 'Last observed Workshop UI language only.' in persistence and 'does not use this' in persistence)

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ': ' + name)
if failed:
    raise SystemExit('FAILED: ' + ', '.join(failed))
print(f'WorkshopReleaseAdvancedDiagnosticsHiddenLocaleHotReload={len(checks)}/{len(checks)}_PASS')
