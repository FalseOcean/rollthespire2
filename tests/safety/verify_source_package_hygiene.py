#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import glob
import re
import xml.etree.ElementTree as ET
from urllib.parse import unquote
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[2]

FORBIDDEN_DIR_NAMES = {
    'bin', 'obj', '.godot', '.git', '.vs', '.idea', 'TestResults', '__pycache__', 'artifacts', '.artifacts',
    '.sourcecandidate-staging', '.sourcecandidate-output',
}
FORBIDDEN_FILE_SUFFIXES = {'.pyc', '.pdb', '.tmp', '.bak', '.dmp', '.dump', '.7z', '.user', '.suo', '.log', '.dll', '.exe', '.pck'}
FORBIDDEN_FILE_NAMES = {'.git', 'local.props'}
REQUIRED_FILES = {
    'RolltheSpire2.csproj', 'RolltheSpire2.json', 'project.godot', 'BUILDING.md',
    'Search/Runtime/ProductionExactSearchEvaluator.cs',
    'Search/Runtime/ProductionSearchSession.cs',
    'tests/shared/Beta109WorldFixtureFactory.cs',
    'tests/shared/RelicSequenceFixtureFactory.cs',
    'tests/functional/Rt2FunctionalChecks/Rt2FunctionalChecks.csproj',
    'Infrastructure/Snapshots/ProductionSearchReplay.cs',
    'tools/ProductionSearchHost/RequestCapture.ps1',
    'tools/ProductionSearchHost/Main.cs',
    'Search/Selectivity/MapEvidence/provenance.json',
    'Search/Selectivity/MapEvidence/manifest.json',
    'Search/Selectivity/MapEvidence/README.md',
    'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md', 'README.zh-CN.md',
}
INTERNAL_REQUIRED_FILES = {
    'CURRENT_STATUS.md', 'DEBT_REGISTER.md', 'PRODUCT_STRATEGY.md',
    'PROJECT_ARCHITECTURE.md', 'PROJECT_MENTAL_MODEL.md',
    'DOCUMENT_OWNERSHIP_AND_INFORMATION_SYNC_POLICY.md',
}
PUBLIC_DOC_INPUTS = {'docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json'}
OBSOLETE_CURRENT_TEST_NAMES = {
    'verify_clean_development_package.py',
    'verify_beta110_clean_baseline.py',
    'verify_beta110_clean_baseline.ps1',
    'verify_beta110_profile_compatibility.py',
    'verify_beta110_profile_compatibility.ps1',
}

parser = argparse.ArgumentParser(description='Check an extracted source package before building it.')
parser.add_argument('--package-kind', choices=('Internal', 'PublicSource'))
parser.add_argument('--project-root', type=Path, help='Extracted source root (defaults to the checker source root).')
args = parser.parse_args()
if args.project_root:
    ROOT = args.project_root.resolve()
failures: list[str] = []


def read_json(path: Path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


metadata_path = ROOT / 'SOURCE_PACKAGE.json'
metadata = read_json(metadata_path) if metadata_path.is_file() else {}
package_kind = args.package_kind or metadata.get('PackageKind', 'Internal')
if package_kind not in ('Internal', 'PublicSource'):
    failures.append(f'unknown source package kind: {package_kind}')
if metadata and (metadata.get('SchemaVersion') != 1 or metadata.get('PackageKind') != package_kind):
    failures.append('source package metadata does not match the selected package kind')
if package_kind == 'PublicSource' and not metadata:
    failures.append('PublicSource package metadata missing')

for path in ROOT.rglob('*'):
    rel = path.relative_to(ROOT).as_posix()
    if path.is_dir():
        if path.name in FORBIDDEN_DIR_NAMES or path.name.startswith(('.rewrite-build-evidence', '.build-evidence')):
            failures.append(f'generated directory present: {rel}')
        continue
    if path.suffix.lower() in FORBIDDEN_FILE_SUFFIXES:
        failures.append(f'generated/transient file present: {rel}')
    if path.name in FORBIDDEN_FILE_NAMES:
        failures.append(f'repository/machine-local file present: {rel}')
    if path.suffix.lower() == '.zip':
        failures.append(f'nested archive present: {rel}')
    if path.name in OBSOLETE_CURRENT_TEST_NAMES:
        failures.append(f'obsolete current-baseline test present: {rel}')
    if path.is_file() and 'compile_hotfix' in path.name.lower() and rel.startswith('tests/'):
        failures.append(f'one-off compile-hotfix validator retained in current baseline: {rel}')

required_files = REQUIRED_FILES | (INTERNAL_REQUIRED_FILES if package_kind == 'Internal' else {'DEVELOPMENT.md', 'tools/source/Export-PublicSource.ps1'})
for rel in sorted(required_files):
    if not (ROOT / rel).is_file():
        failures.append(f'required file missing: {rel}')

# Explicit compile/resource/project references must survive the selection. Default
# compile globs retain the product directories; hash checks below retain numerical truth.
for project in ROOT.rglob('*.csproj'):
    for node in ET.parse(project).iter():
        if node.tag not in {'Compile', 'EmbeddedResource', 'None', 'Content', 'ProjectReference', 'Import'}:
            continue
        value = node.get('Include', node.get('Project', ''))
        if not value or '$(' in value or '@(' in value:
            continue
        for pattern in value.split(';'):
            matches = [Path(p) for p in glob.glob(str(project.parent / pattern.replace('\\', '/')), recursive=True) if Path(p).is_file()]
            if not matches:
                failures.append(f'project input missing: {project.relative_to(ROOT)} -> {pattern}')

if package_kind == 'PublicSource':
    for path in (ROOT / 'docs').rglob('*'):
        if path.is_file() and path.relative_to(ROOT).as_posix() not in PUBLIC_DOC_INPUTS:
            failures.append(f'internal document in PublicSource: {path.relative_to(ROOT)}')
    for rel in INTERNAL_REQUIRED_FILES | {'AGENTS.md', 'FUTURE_GOALS_AND_DIRECTIONS.md', 'tools/Sync-SourceMain.ps1', 'tests/safety/verify_source_main_sync.py', 'outputs'}:
        if (ROOT / rel).exists():
            failures.append(f'internal workflow/authority in PublicSource: {rel}')
    # Check real Markdown links in retained development entry points, not plain
    # provenance paths or historical documents that are not part of this product.
    public_guides = ['README.md', 'README.zh-CN.md', 'BUILDING.md', 'DEVELOPMENT.md',
                     'THIRD_PARTY_NOTICES.md', 'Search/README.md',
                     'tools/ProductionSearchHost/README.md', 'tools/ProductionSearchHost/Corpus/README.md']
    for rel in public_guides:
        path = ROOT / rel
        if not path.is_file():
            failures.append(f'public guide missing: {rel}')
            continue
        for target in re.findall(r'(?<!!)\[[^\]]+\]\(([^)]+)\)', path.read_text(encoding='utf-8-sig')):
            target = unquote(target.split('#', 1)[0].strip('<>'))
            if not target or re.match(r'[a-zA-Z][a-zA-Z0-9+.-]*:', target):
                continue
            if not (path.parent / target).exists():
                failures.append(f'public guide link missing: {rel} -> {target}')

manifest = json.loads((ROOT / 'RolltheSpire2.json').read_text(encoding='utf-8'))
for field in ('version', 'min_game_version'):
    value = manifest.get(field)
    if not isinstance(value, str) or not value.strip():
        failures.append(f'manifest field missing/empty: {field}')

# Current handoff packages intentionally omit historical delivery/implementation evidence.
for rel in ('docs/delivery_evidence', 'docs/implementation_reports'):
    if (ROOT / rel).exists():
        failures.append(f'historical evidence unexpectedly retained in current baseline: {rel}')

if (ROOT / 'tools/AuthoredEventChecks/localization').exists():
    failures.append('generated game localization retained: tools/AuthoredEventChecks/localization')

# Public statistical resources must remain accompanied by their actual source/hash records.
evidence = ROOT / 'Search/Selectivity/MapEvidence'
if (evidence / 'provenance.json').is_file() and (evidence / 'manifest.json').is_file():
    provenance = read_json(evidence / 'provenance.json')
    paired = read_json(evidence / 'manifest.json')
    records = [(entry['file'], entry['finalOnlySha256'], entry.get('bytes')) for entry in provenance['files']]
    records += [(entry['Name'], entry['Sha256'], entry['Bytes']) for entry in paired['Files']]
    if len(records) != 12 or len({name for name, _, _ in records}) != 12:
        failures.append('MapEvidence source records must identify the twelve frozen resources')
    for name, expected_hash, expected_bytes in records:
        path = evidence / name
        if not path.is_file():
            failures.append(f'MapEvidence resource missing: {name}')
        elif hashlib.sha256(path.read_bytes()).hexdigest().lower() != expected_hash.lower():
            failures.append(f'MapEvidence hash mismatch: {name}')
        elif expected_bytes is not None and path.stat().st_size != expected_bytes:
            failures.append(f'MapEvidence size mismatch: {name}')

corpus = ROOT / 'tools/ProductionSearchHost/Corpus'
corpus_manifest = corpus / 'v1/manifest.json'
if package_kind == 'PublicSource':
    if not corpus_manifest.is_file():
        failures.append('public minimal Corpus manifest missing')
    else:
        minimal = {'README.md', 'v1/manifest.json', 'v1/initial-results.json', 'v1/initial-build.json'}
        for case in read_json(corpus_manifest)['Cases']:
            workload = PurePosixPath(case['Workload'])
            if workload.is_absolute() or '..' in workload.parts:
                failures.append(f'public workload escapes Corpus: {workload}')
                continue
            rel = 'v1/' + workload.as_posix()
            minimal.add(rel)
            path = corpus / rel
            if path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest().upper() != case['Sha256'].upper():
                failures.append(f'public workload hash mismatch: {rel}')
        actual = {p.relative_to(corpus).as_posix() for p in corpus.rglob('*') if p.is_file()}
        for rel in sorted(actual - minimal):
            failures.append(f'internal/unselected Corpus member in PublicSource: {rel}')
        for rel in sorted(minimal - actual):
            failures.append(f'public minimal Corpus member missing: {rel}')
        for path in corpus.rglob('*.json'):
            document = read_json(path)
            if isinstance(document, dict) and isinstance(document.get('Context'), dict):
                failures.append(f'full runtime context in PublicSource: {path.relative_to(corpus)}')

if failures:
    for failure in failures:
        print('FAIL:', failure)
    raise SystemExit(1)

file_count = sum(1 for p in ROOT.rglob('*') if p.is_file())
print('sourcePackageHygiene=PASS')
print(f'sourcePackageKind={package_kind}')
print(f'fileCount={file_count}')
print('generatedArtifacts=0')
print('nestedArchives=0')
print('historicalDeliveryEvidenceInBaseline=0')
print('obsoleteValidationResidue=0')
