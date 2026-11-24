from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def text(rel):
    return (ROOT / rel).read_text(encoding='utf-8')

def require(name, cond):
    if not cond:
        raise AssertionError(name)
    print(f'PASS {name}')

capture = text('Infrastructure/Snapshots/RuntimeContextAuthorityCapture.cs')
auth = text('Core/Authority/RuntimeContextAuthoritySnapshot.cs')
profile111 = text('Core/Prediction/Profiles/Beta111PredictionProfile.cs')
profile110 = text('Core/Prediction/Profiles/Beta110PredictionProfile.cs')
effects = text('Core/Effects/Snapshots/NeowEffectAuthoritySnapshot.cs')
adapter = text('Infrastructure/Snapshots/ReflectionNeowEffectSnapshotAdapter.cs')
probe = text('Infrastructure/Snapshots/ReflectionNeowEffectSnapshotAdapter.NeowCatalog.cs')
neow = text('Core/Neow/ModernNeowIdentityPredictor.cs')
engine = text('Core/Effects/NeowEffectProjectionEngine.cs')
bones = text('Core/Effects/NeowEffectProjectionEngine.BonesOrderImpact.cs')
projector111 = text('Core/Neow/Beta111NeowEffectProjector.cs')
projector110 = text('Core/Neow/Beta110NeowEffectProjector.cs')
search_projector = text('Search/Runtime/SearchAuthorityProjector.cs')
controller = text('Ui/Controllers/SearchPageController.cs')

require('runtime Neow catalog probe exists', 'CaptureVanillaNeowCatalogExact' in probe)
require('Neow catalog probe compares positive and curse source-order sequences',
        'SequenceEqual(expectedPositives)' in probe and 'SequenceEqual(expectedCurses)' in probe)
require('Mod character no longer forces Neow catalog unknown by character provenance',
        'vanillaNeowCatalogExact: vanilla ? true : null' not in capture and
        'runtimeNeowCatalogExact' in capture)
require('vanilla fallback remains fail-soft if probe unavailable',
        'runtimeNeowCatalogExact ?? (vanilla ? true : null)' in capture)
require('Modern Neow identity authority is domain-local',
        'HasExactModernNeowIdentityInputs' in auth and
        'ResolutionStatus == IdentityResolutionStatus.Exact' not in auth.split('HasExactModernNeowIdentityInputs =>',1)[1].split(';',1)[0] and
        'HasExactCharacterRewardIdentityPool' not in auth.split('HasExactModernNeowIdentityInputs =>',1)[1].split(';',1)[0])
require('Beta111 uses domain-local Neow exact authority',
        'IsBeta111NeowIdentityAuthorityExact' in profile111 and
        'HasExactModernNeowIdentityInputs' in profile111)
require('Beta110 shared-modern path uses domain-local Neow exact authority',
        'IsBeta110NeowIdentityAuthorityExact' in profile110 and
        'HasExactModernNeowIdentityInputs' in profile110)
require('card identity pool exactness separated from behavioral hooks',
        'CharacterRewardIdentityPoolExact' in effects and
        'HasExactCharacterRewardIdentityPool' in effects)
require('card capture preserves identity when non-generation metadata is partial',
        'BehaviorMetadataExact' in effects and
        'identityExact' in adapter and
        'Card identity generation only needs copied key/rarity/source order' in adapter)
require('Arcane Scroll consumes identity-pool authority, not full Mod card behavior authority',
        'HasExactCharacterRewardIdentityPool' in engine and
        'character-reward-identity-pool-required' in engine)
require('Modern effect projector delegates dependency exactness to individual effects',
        'IsBeta111NeowIdentityAuthorityExact' in projector111 and
        'IsBeta111ProjectionAuthorityExact' not in projector111 and
        'IsBeta110NeowIdentityAuthorityExact' in projector110 and
        'IsBeta110ProjectionAuthorityExact' not in projector110)
require('generated-card-only exact projection does not require RelicGrabBag authority',
        'RequiresPerSeedRelicBagProjection' in search_projector and
        'StructuredNeowEffects.Any(condition =>' in search_projector and
        'SmallCapsule' in search_projector and 'LargeCapsule' in search_projector)
require('relic bag identity order can stay exact with opaque Mod relic behavior',
        'HasCapturedIdentityFoundation && RelicBagExact' in effects and
        'HasCapturedIdentityFoundation &&\n        RelicBagSourcePoolsExact' in effects)
require('Bones authority no longer globally depends on Mod character provenance',
        '_analysisAuthority.HasExactModernNeowIdentityInputs' in bones and
        '!state.Authority.HasExactFoundation' not in bones)
require('Neow identity authority diagnostics expose the remaining runtime bits',
        'characterRewardIdentityPoolExact=' in controller and
        'modernNeowIdentityInputsExact=' in controller and
        'beta111NeowIdentityAuthorityExact=' in controller)
require('no bespoke Watcher/Mod id branch',
        'WATCHER' not in probe and 'Ritsu' not in probe and 'modId ==' not in probe)
print('OK 16/16')
