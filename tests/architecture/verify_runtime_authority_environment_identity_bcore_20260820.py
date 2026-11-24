#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f'{name}=true')

contracts = read('Core/Authority/Runtime/RuntimeAuthorityContracts.cs')
fingerprint = read('Core/Authority/Runtime/RuntimeSemanticFingerprint.cs')
baseline = read('Core/Authority/Runtime/VerifiedVanillaBaselineRegistry.cs')
beta111 = read('Core/Authority/Runtime/VerifiedVanillaBaselineBeta111Schema1.cs')
capture = read('Infrastructure/Snapshots/RuntimeAuthority/RuntimeAuthoritySnapshotCapture.cs')
env = read('Bootstrap/RuntimeAuthorityEnvironment.cs')
thread_guard = read('Infrastructure/Snapshots/RuntimeSnapshotThreadGuard.cs')
main_menu = read('Bootstrap/MainMenuPatchInstaller.cs')
host = read('Ui/Shell/AppShellHost.cs')
save_status = read('Ui/Pages/SaveStatus/SaveStatusPage.cs')
warning = read('Ui/Components/RuntimeAuthorityWarningComposer.cs')
controller = read('Ui/Controllers/SearchPageController.cs')
preset = read('Ui/Persistence/SearchPresetPersistence.cs')

# Immutable DTO boundary and authority/state separation.
req('runtimeAuthoritySnapshotExists', 'record RuntimeAuthoritySnapshot' in contracts)
req('semanticUnlockCurrentSplit', all(token in contracts for token in [
    'IReadOnlyList<RuntimeSemanticDomainSnapshot> SemanticUniverse',
    'IReadOnlyList<RuntimeUnlockAuthorityDomainSnapshot> UnlockAuthority',
    'IReadOnlyList<RuntimeCurrentUnlockDomainSnapshot> CurrentUnlockState']))
req('noLiveGameObjectsInContracts', all(token not in contracts for token in ['Godot.', 'Node ', 'Control ', 'ModelDb', 'SaveManager']))
req('schema1Explicit', 'public const int Current = 1;' in contracts)
for domain in ['Characters','Cards','Relics','Ancients','Potions','Events']:
    req('schema1Domain_' + domain, f'public const string {domain} = "{domain}";' in contracts)

# Main-thread-only live capture, then copied-data consumers.
req('captureRequiresMainThread', 'RuntimeSnapshotThreadGuard.RequireMainThread();' in capture)
req('threadGuardBoundAtMainBoundary', 'RuntimeSnapshotThreadGuard.BindCurrentThread();' in env and 'RuntimeSnapshotThreadGuard.RequireMainThread();' in env)
req('mainMenuCapturesAuthority', 'RuntimeAuthorityEnvironment.CaptureOnMainThread(ModRuntime.Snapshot);' in main_menu)
req('rt2EntryRefreshesAuthority', 'RuntimeAuthorityEnvironment.CaptureOnMainThread(_snapshot);' in host)
req('backgroundConsumersDoNotReadModelDb', 'ModelDb' not in fingerprint and 'SaveManager' not in fingerprint and 'Godot' not in fingerprint)

# Canonical SHA-256 contract: stable identity sort, fixed token framing, schema, no presentation data.
req('sha256Used', 'SHA256.HashData' in fingerprint)
req('canonicalLengthPrefix', 'BinaryPrimitives.WriteInt32BigEndian' in fingerprint and 'Int(bytes.Length);' in fingerprint)
req('semanticItemsStableSorted', 'OrderBy(item => item.StableIdentity, StringComparer.Ordinal)' in fingerprint)
req('unlockUniverseStableSorted', '.OrderBy(value => value, StringComparer.Ordinal)' in fingerprint)
req('overallIncludesSchemaAndDomains', 'builder.Token("rt2-runtime-semantic-overall")' in fingerprint and 'builder.Int(schemaVersion);' in fingerprint and 'UnlockUniverse' in fingerprint)
for forbidden in ['localized', 'description=', 'texture=', 'icon=', 'animation=', 'tooltip=', 'displayColor=']:
    req('fingerprintExcludesPresentation_' + forbidden.replace('=',''), forbidden.lower() not in fingerprint.lower())

# Source-audited semantic order is retained explicitly without trusting container iteration order.
req('rngMeaningfulPoolOrdinalCaptured', 'pool={poolKey.Serialized};ordinal={ordinal:D8}' in capture)
req('domainItemsCanonicalSortedAfterCapture', 'items.OrderBy(item => item.StableIdentity, StringComparer.Ordinal).ToArray()' in capture)
req('missingAuthorityFailsClosed', 'SemanticDomainIncomplete:' in fingerprint and 'UnlockUniverseIncomplete:' in fingerprint)
req('cardMultiplayerApplicabilityFailsClosed', capture.count('bool? multiplayerOnly = TryReadMultiplayerOnly(model);') == 1 and '!multiplayerOnly.HasValue' in capture[capture.index('private static RuntimeSemanticDomainSnapshot CaptureCards'):capture.index('private static RuntimeSemanticDomainSnapshot CaptureRelics')] and 'return null;' in capture[capture.index('private static bool? TryReadMultiplayerOnly'):])
req('relicPotionDoNotInventMultiplayerField', 'multiplayerOnly=' not in capture[capture.index('private static RuntimeSemanticDomainSnapshot CaptureRelics'):capture.index('private static RuntimeSemanticDomainSnapshot CaptureAncients')])
req('saveStatusFixedHeadersSurviveRefresh', 'CreateSectionWithHeader' in save_status and 'return body;' in save_status and 'InsertSectionHeader' not in save_status)

# No generic mod/DLL/assembly-count detector is used for environment classification.
joined = '\n'.join([contracts, fingerprint, baseline, capture, env])
for forbidden in ['loaded mod count','dll count','assembly count','mods folder','IsModded']:
    req('noForbiddenEnvironmentHeuristic_' + forbidden.replace(' ','_'), forbidden.lower() not in joined.lower())

# Baseline registry contains the Owner-verified Beta111 Schema 1 trust anchor.
req('verifiedBaselineRegistryExists', 'class VerifiedVanillaBaseline' in baseline and 'class VerifiedVanillaBaselineRegistry' in baseline)
req('baselineRegistryHasBeta111Schema1', 'VerifiedVanillaBaselineBeta111Schema1.Value' in baseline and '27f92e36a782cca447e32d4321e36f4539b783a1bf6a705b5be231f1724a713b' in beta111)
req('semanticStatusNotBool', all(token in contracts for token in [
    'VerifiedVanillaMatch', 'KnownBaselineMatchButGameVersionUnverified', 'SemanticMismatch', 'Unverified', 'Unknown']))
req('crossVersionHashMatchSupported', 'KnownBaselineMatchButGameVersionUnverified' in baseline and 'exactSemanticMatch' in baseline)
req('vanillaUnlockUsesBaselineUniverse', 'baseline.VanillaUnlockUniverse' in baseline and 'snapshot.FindCurrentUnlockDomain' in baseline)

# Fingerprint is warning/provenance only, never a Search admission condition.
req('warningComposerConsumesInterpretation', 'RuntimeAuthorityEnvironment.Current' in warning and 'SemanticEnvironmentStatus.SemanticMismatch' in warning)
req('saveStatusConsumesAuthority', 'RuntimeAuthorityEnvironment.Current' in save_status and 'DomainComparisons' in save_status)
req('presetProvenanceConsumesAuthority', 'BuildPresetProvenance' in controller and 'RuntimeAuthorityEnvironment.Current' in controller)
req('presetStoresHistoricalFingerprintFacts', all(token in preset for token in [
    'FingerprintSchemaVersion', 'SemanticFingerprint', 'MatchedBaselineGameVersion']))
start_i = controller.index('private void Start(SearchDraft draft')
end_i = controller.index('private bool TryStartWrapContinuation', start_i)
start_block = controller[start_i:end_i]
req('fingerprintNotSearchAdmissionGate', 'SemanticEnvironmentStatus.SemanticMismatch' not in start_block and 'RuntimeAuthorityEnvironment.Current' not in start_block)

# Diagnostic candidate export is the only first-baseline bootstrap path before Owner verification.
req('baselineCandidateWriterExists', 'runtime_authority_baseline_candidate.json' in env)
for field in ['GameVersion','FingerprintSchemaVersion','DomainHashes','UnlockUniverseHash','OverallSemanticHash','RuntimeUnlockUniverse']:
    req('baselineCandidate_' + field, field in env)

print('runtimeAuthorityEnvironmentIdentityBCore20260820=true')
