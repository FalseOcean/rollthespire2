#!/usr/bin/env python3
from __future__ import annotations

import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EXPECTED = {
    "Core": "ea79e652959b975494da44b7534d7f085302caf287d6f64ef3ce13cad88ef3be",
    "Search": "d50bb0d4de8d51077388ed5363df441fdbc9fb01ac4a2738f7652827448b4e25",
    "Compatibility": "304c40bb4913212d5a423b1d717dbe26dca343affedaab0fe26853e760be08c4",
    "Infrastructure/Snapshots": "d1a70501d4dd7205c04c7478cdd17a922316af43895742f8218bfd32957c39c9",
    "Ui/Persistence": "225a6952c5986aa2921ee2d1ffe656a1ae51bcb22a0d75a16ad5fa13e5a9fe67",
    "Ui/Controllers": "8c3e7c2d4f99c1f204ddbad3c211ceedf124a2029f6f5299b6af1882125d61bf",
    "Ui/Pages/Search": "82e969af224487dc699fc1336f25074917c62e72001493177d0129f1f9e63b96",
    "Ui/Pages/Analysis": "c41a666ccbb0a54f3bb57d7f2e2bf53bbf5dc03049bbafcf999ca35562a9779e",
}


def tree_sha(relative: str) -> str:
    root = ROOT / relative
    digest = hashlib.sha256()
    for path in sorted(p for p in root.rglob("*") if p.is_file()):
        rel = path.relative_to(root).as_posix().encode("utf-8")
        data = path.read_bytes()
        digest.update(len(rel).to_bytes(4, "big"))
        digest.update(rel)
        digest.update(len(data).to_bytes(8, "big"))
        digest.update(data)
    return digest.hexdigest()


for relative, expected in EXPECTED.items():
    actual = tree_sha(relative)
    if actual != expected:
        raise SystemExit(f"FROZEN BOUNDARY FAIL {relative}: expected={expected} actual={actual}")
    print(f"PASS frozen boundary: {relative}")

print(f"Developer Notes v1 frozen-boundary gate PASS ({len(EXPECTED)}/{len(EXPECTED)})")
