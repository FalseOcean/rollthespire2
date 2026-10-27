#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f'{name}=true')

capture = read('Infrastructure/Snapshots/RuntimeAuthority/RuntimeAuthoritySnapshotCapture.cs')
preset = read('Ui/Persistence/SearchPresetPersistence.cs')

def block(start, end):
    return capture[capture.index(start):capture.index(end)]

cards = block('private static RuntimeSemanticDomainSnapshot CaptureCards', 'private static RuntimeSemanticDomainSnapshot CaptureRelics')
relics = block('private static RuntimeSemanticDomainSnapshot CaptureRelics', 'private static RuntimeSemanticDomainSnapshot CapturePotions')
potions = block('private static RuntimeSemanticDomainSnapshot CapturePotions', 'private static RuntimeSemanticDomainSnapshot CaptureAncients')

# Direct source audits prove player-count/multiplayer filtering for Cards, but not for
# RelicPoolModel.GetUnlockedRelics or PotionPoolModel.GetUnlockedPotions. Schema 1 must
# therefore fail-closed only where the semantic field is actually source-backed.
req('cardsKeepMultiplayerConstraint', 'TryReadMultiplayerOnly(model)' in cards and 'multiplayerOnly=' in cards)
req('relicsDoNotRequireUnprovenMultiplayerConstraint', 'TryReadMultiplayerOnly(model)' not in relics and 'multiplayerOnly=' not in relics)
req('potionsDoNotRequireUnprovenMultiplayerConstraint', 'TryReadMultiplayerOnly(model)' not in potions and 'multiplayerOnly=' not in potions)
req('relicEvidenceMatchesAuditedFields', 'RelicModel.Rarity/IsAllowedInShops+ordered AllRelicPools membership' in relics)
req('potionEvidenceMatchesAuditedFields', 'PotionModel.Rarity+ordered pool membership' in potions)

# PersistedSearchPreset is a dual read/write DTO. New v2 writes leave legacy Draft at
# JsonElement Undefined; it must be omitted on write or System.Text.Json throws
# InvalidOperationException instead of persisting the Temporary history.
req('jsonIgnoreNamespaceImported', 'using System.Text.Json.Serialization;' in preset)
legacy = preset[preset.index('// v1 narrow compatibility fields'):preset.index('// shared', preset.index('// v1 narrow compatibility fields'))]
req('legacyDraftIgnoredWhenUndefined', '[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]' in legacy and 'public JsonElement Draft { get; set; }' in legacy)
req('temporaryWriteStillAtomic', 'File.WriteAllText(temp, JsonSerializer.Serialize(persisted, _json));' in preset and 'File.Move(temp, _path, overwrite: true);' in preset)

print('runtimeAuthorityOwnerCaptureHotfix1_20260820=true')
