#!/usr/bin/env python3
from pathlib import Path
import hashlib

ROOT = Path(__file__).resolve().parents[2]

def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding='utf-8')

def require(ok: bool, code: str):
    if not ok:
        raise AssertionError(code)
    print(f'{code}=true')

authority = read('Core/World/Snapshots/Beta109AcceptedRuntimeValidationAuthority.cs')
compat = read('Compatibility/RuntimeVersionCompatibility.cs')
beta111 = read('Compatibility/Beta111ValidationAuthority.cs')
snapshot = read('Core/World/Snapshots/Beta109WorldGenerationSnapshot.cs')
provider = read('Core/World/Beta109/Beta109AncientOptionProvider.cs')

require('RuntimeVersionCompatibility.IsBeta111(normalized)' in authority,
        'beta111ExactVersionBranchPresent')
require('Beta111ValidationAuthority.RuntimeAccepted' in authority and
        'Beta111ValidationAuthority.WorldAncientSourceAccepted' in authority and
        'Beta110ValidationAuthority.BossAncientAccepted' in authority,
        'beta111ReuseRequiresAcceptedAuditAuthorities')
require('AcceptedBeta110FixtureIds' in authority.split('RuntimeVersionCompatibility.IsBeta111(normalized)', 1)[1],
        'beta111ReusesAcceptedBeta110WorldFixtureIds')
require('Beta109FixtureValidationStatus.AssumedCompatibleLatestKnown' in authority.split('RuntimeVersionCompatibility.IsBeta111(normalized)', 1)[1],
        'beta111FixtureStatusAssumedCompatibleLatestKnown')
require('beta111-world-ancient-no-semantic-delta-audited-compatible-v1:' in authority,
        'beta111EvidenceCodeSpecific')
require('public const string Beta111Version = "0.111.0";' in compat and
        'public static bool IsBeta111' in compat,
        'beta111IdentityRemainsExact01110')
require('public const bool RuntimeAccepted = true;' in beta111 and
        'public const bool WorldAncientSourceAccepted = true;' in beta111,
        'beta111CompatibilityAuthorityAccepted')
require('modern-world-ancient-fixture-version-not-applicable:' in authority,
        'unknownVersionPendingBranchPreserved')
require('AllowsAncientOptionProductionExact(string ancientEntry)' in snapshot and
        'HasVerifiedFixturePrefix("ancient-option:" + NormalizeFixtureEntry(ancientEntry) + ":")' in snapshot,
        'ancientOptionProductionExactGateNotWeakened')
require('PendingRealGameAncientOptionFixture:' in provider,
        'ancientProviderFailClosedIssuePreserved')
require('CandidateFromSharedAncientAllocation' not in authority,
        'fixtureHotfixDoesNotConflateSharedAllocationWithEventSharing')

print('beta111AncientOptionFixtureAuthorityHotfix=true')
