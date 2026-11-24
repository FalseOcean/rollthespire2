#!/usr/bin/env python3
from __future__ import annotations
import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EXPECTED = {
    'Core': 'ea79e652959b975494da44b7534d7f085302caf287d6f64ef3ce13cad88ef3be',
    'Search': 'd50bb0d4de8d51077388ed5363df441fdbc9fb01ac4a2738f7652827448b4e25',
    'Compatibility': '304c40bb4913212d5a423b1d717dbe26dca343affedaab0fe26853e760be08c4',
    'Infrastructure/Snapshots': 'd1a70501d4dd7205c04c7478cdd17a922316af43895742f8218bfd32957c39c9',
    'Ui/Controllers': '8c3e7c2d4f99c1f204ddbad3c211ceedf124a2029f6f5299b6af1882125d61bf',
    'Ui/Pages/Search': '82e969af224487dc699fc1336f25074917c62e72001493177d0129f1f9e63b96',
    'Ui/Components': '894f85ea719a3988367127f411a7f4531474e8b962d692f47988908b2e2d96e6',
    'Ui/Pages/Analysis': 'c41a666ccbb0a54f3bb57d7f2e2bf53bbf5dc03049bbafcf999ca35562a9779e',
    'Bootstrap': 'afcce19ee3fbd36bcd8864a5acc8ae923b72336e4b1a2d7aa407c6635cfef9e6',
    'Presentation': 'a454317aadf5c2ffaa588ee8783b4262f01e95d3ce2e4a76fc5765fa71cade9a',
}

def tree_sha(relative: str) -> str:
    base = ROOT / relative
    digest = hashlib.sha256()
    for path in sorted(p for p in base.rglob('*') if p.is_file()):
        rel = path.relative_to(base).as_posix().encode('utf-8')
        data = path.read_bytes()
        digest.update(len(rel).to_bytes(4, 'big'))
        digest.update(rel)
        digest.update(len(data).to_bytes(8, 'big'))
        digest.update(data)
    return digest.hexdigest()

for relative, expected in EXPECTED.items():
    actual = tree_sha(relative)
    if actual != expected:
        raise SystemExit(f'FROZEN BOUNDARY FAIL {relative}: expected={expected} actual={actual}')
    print(f'PASS frozen boundary: {relative}')

print(f'Official Preset bundled-content frozen-boundary gate PASS ({len(EXPECTED)}/{len(EXPECTED)})')
