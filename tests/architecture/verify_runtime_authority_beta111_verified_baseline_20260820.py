#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
text = (ROOT / 'Core/Authority/Runtime/VerifiedVanillaBaselineBeta111Schema1.cs').read_text(encoding='utf-8')
registry = (ROOT / 'Core/Authority/Runtime/VerifiedVanillaBaselineRegistry.cs').read_text(encoding='utf-8')

def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f'{name}=true')

req('beta111Registered', 'VerifiedVanillaBaselineBeta111Schema1.Value' in registry)
req('gameVersion', 'GameVersion: "0.111.0"' in text)
req('schema1', 'FingerprintSchemaVersion: 1' in text)
expected = {
    'Characters':'90ad6989f2fc830d75d43e6cc6b46cd02641cbcd3b544f69a311957c3bac5714',
    'Cards':'fc526e043d90a46d3e04104df92d7ce5dd84161c519ffaa7b1f6a2444c4e30e1',
    'Relics':'f370779c0ad1bb0c945dd790a3d2615323a9944f61f51b7a9b623d40f23ace60',
    'Ancients':'756dccc7e5b5f3f377312e37442bb30cb22783d0590f6a7ccfad2dfba31ed0db',
    'Potions':'5082faf5fa7c100494692b3d34dfdd87a7dfbe7047bad223b5b447b1e9df9764',
    'Events':'890f4c94981aac9306cce1c3688d1e09b80a36dd545649eb9c65bf3514e6a226',
    'Bosses':'a2eb6ac0811cd87670839d5fc4a84be0aa0777d707c07b3feeaadbd39f66aec3',
}
for domain, sha in expected.items():
    req('domain_' + domain, f'["{domain}"] = "{sha}"' in text)
req('unlockUniverseHash', '7b3f92de73bbbc6f6a3ad18f56a7e26f9c8d1d95026cedc12d2c2bb6cb1a885b' in text)
req('overallHash', '27f92e36a782cca447e32d4321e36f4539b783a1bf6a705b5be231f1724a713b' in text)
# Count exact stable identities embedded for vanilla unlock authority.
for domain, expected_count, prefix in [
    ('Characters', 5, 'CHARACTER:'),
    ('Cards', 559, 'CARD:'),
    ('Relics', 299, 'RELIC:'),
    ('Ancients', 8, 'EVENT:'),
    ('Potions', 66, 'POTION:'),
]:
    # Scope by dictionary block to avoid other strings influencing counts.
    marker = f'["{domain}"] = new[]'
    start = text.index(marker)
    next_candidates = [text.find('["', start + len(marker)), text.find('        },', start + len(marker))]
    ends = [x for x in next_candidates if x >= 0]
    end = min(ends) if ends else len(text)
    block = text[start:end]
    req('unlockCount_' + domain, block.count('"' + prefix) == expected_count)
req('ownerEvidencePinned', 'candidate-sha256=46eb3f455934a8460bacb9b4016d7f97b4758604ba529f8cc9a319ad9c839a48' in text)
print('runtimeAuthorityBeta111VerifiedBaseline20260820=true')
