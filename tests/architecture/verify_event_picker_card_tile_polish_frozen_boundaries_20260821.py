#!/usr/bin/env python3
from pathlib import Path
import hashlib, sys
ROOT=Path(__file__).resolve().parents[2]

EXPECTED={
    'Search/Semantics/SearchCompiler.cs': '8728ba5ae35f29f94974cbc1fdcd5b8039c0be03e983b6105abfc79a5a174c9c',
    'Search/Contracts/SearchContracts.cs': 'e4c5b052f1a6b33bc0557dfb8941cb09604cc66618bd9a02fed617a32ad13347',
    'Search/Runtime/ProductionQueryValidator.cs': 'fac219676c78fb6e7a7f7d2e9b10b371a8c4bd9bcb5f5a729effb53eacd9531e',
    'Search/Runtime/ProductionExactSearchEvaluator.cs': 'c4ed1194b4347b07ef8529d6e7c8b14267e702dcd82a3c072cd0bdccaa4e331d',
    'Core/Effects/NeowEffectProjectionEngine.cs': 'a95d98786728ea02ce66ce93aba702865b6970fd3d8d369d57b54c2d67fa8f31',
    'Core/Events/EventStaticEligibilityCatalog.cs': 'ea4cfd4732bbcd6125150a82f0cbb32e8b58d4dda2a13d32fcffda84147a4959',
    'Ui/Controllers/SearchPageController.cs': '8ecbf387cac5b62b91c0d346276a58f1ea5c72e586b2e11dceb96996026bba85',
    'Ui/Persistence/SearchPresetCompatibility.cs': 'c2e52208802444bd3b6208fff728f1010f5f58900e4321c2c2b4705b91b9cdac',
}

def req(name, ok):
    if not ok: print('FAIL:', name); sys.exit(1)
    print('PASS:', name)

for rel, expected in EXPECTED.items():
    got=hashlib.sha256((ROOT/rel).read_bytes()).hexdigest()
    req('frozen:'+rel, got==expected)

print('EventPickerCardTilePolishFrozenBoundaries20260821=PASS')
