from pathlib import Path

root = Path(__file__).resolve().parents[2]
path = root / 'Ui/Components/SearchPresetLibraryOverlay.cs'
text = path.read_text(encoding='utf-8')

checks = {
    'importsCompactNumberFormatterNamespace': 'using RolltheSpire2.Presentation.Ui1;' in text,
    'importsSearchDraftNamespace': 'using RolltheSpire2.Ui.Pages.Search;' in text,
    'usesCompactNumberFormatter': 'CompactNumberFormatter.FormatRarity' in text,
    'usesTypedSearchDraft': 'SearchDraft draft = preset.Draft;' in text,
}

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('PASS' if ok else 'FAIL') + ': ' + name)
if failed:
    raise SystemExit('failed: ' + ', '.join(failed))
print(f'PASS {len(checks)}/{len(checks)}')
