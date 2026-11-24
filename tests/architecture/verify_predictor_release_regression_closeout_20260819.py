#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(rel): return (ROOT / rel).read_text(encoding='utf-8')
def req(name, ok):
    if not ok: raise AssertionError(name)
    print(f'{name}=true')

page = read('Ui/Pages/Analysis/AnalysisPage.cs')
bar = read('Ui/Components/AnalysisRequestBar.cs')
shell = read('Ui/Shell/AppShell.cs')
persist = read('Ui/Persistence/SearchWorkspacePersistence.cs')
reward = read('Ui/Components/NormalCombatRewardSequenceSummaryList.cs')

# Reactive context must affect the actual request, not only presentation state.
req('characterReactiveRequest', '_characterPicker.SelectionChanged += _ => ContextChanged?.Invoke(CurrentReactiveDraft)' in bar)
req('ascensionReactiveRequest', '_ascension.ValueChanged += _ => ContextChanged?.Invoke(CurrentReactiveDraft)' in bar)
req('ancientReactiveRequest', '_ancientGroupCard.ConditionsChanged' in page and 'CurrentReactiveDraft.WithAncientConditions(conditions)' in page and 'SubmitReactiveDraft(draft)' in page)
req('currentDraftCarriesAncient', 'CurrentReactiveDraft.WithAncientConditions(_ancientGroupCard.CurrentConditions)' in page)
req('manualSeedExplicitLegalCommitBoundary', '_seedInput.TextChanged += HandleSeedTextChanged' in bar and 'TryCanonicalizeSeed' in bar and '_committedSeed' in bar)
req('randomSeedImmediateAnalyze', '_page.SetSeedText(seed, commit: true)' in read('Ui/Controllers/AnalysisPageController.cs'))

# One-way Searcher -> Predictor handoff; Witness opening route drives Reward projection.
req('handoffCanonicalAnalysisRequest', '_analysisPage.ImportSearchContext(request, preferredOpeningRoute)' in shell)
req('handoffAncientFromCandidateRequest', 'candidate.PredictionRequest.AncientOptionConditions' in shell)
req('handoffOpeningFromWitness', 'candidate.PrimaryWitness?.OpeningRouteId' in shell)
req('predictorDoesNotMutateSearcher', 'SearchPage' not in read('Ui/Controllers/AnalysisPageController.cs'))
req('openingRouteDrivesRewardGroup', 'FindRewardRouteGroupId' in page and 'EquivalentRouteBindings.Any' in page and '_preferredRewardRouteGroupId = FindRewardRouteGroupId(requestedRoute);' in page)
req('ordinaryRewardSelectorHidden', 'showRouteSelector: false' in page and 'selectedRouteGroupId: _preferredRewardRouteGroupId' in page)
req('equivalentRouteLabelsPreserved', 'EquivalentRouteLabels' in reward)

# Ambiguous Bones cannot silently collapse to an arbitrary route.
reconcile = page[page.index('private void ReconcileOpeningSelection'):page.index('private void ApplyOpeningSelection')]
req('ambiguousBonesClearsPreferredRoute', '_preferredOpeningRouteId = string.Empty;' in reconcile and 'Never silently choose route A' in page)
req('bonesPrimaryUsesCanonicalImpactProof', '!NeowChoiceStrip.IsBonesPrimarySelectable(choice)' in page)

# Predictor-owned persistence remains independent of Search workspace.
for field in ['CharacterKey','Ascension','TezcataraHasBasicStrike','DarvAllowPandorasBoxRelicSet','PreferredOpeningRouteId','PreferredRewardRouteGroupId']:
    req('persisted_' + field, field in persist[persist.index('internal sealed class PredictorContextDocument'):persist.index('internal sealed class SearchWorkspaceDocument')])
req('predictorContextSeparateFile', 'predictor_context.json' in persist)

print('predictorReleaseRegressionCloseout20260819=true')
