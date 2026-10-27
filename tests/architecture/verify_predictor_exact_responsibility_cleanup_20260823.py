from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(rel):
    return (ROOT / rel).read_text(encoding='utf-8')

def require(label, condition):
    if not condition:
        raise AssertionError(label)

exact_path = ROOT / 'Search/Runtime/ProductionExactSearchEvaluator.cs'
document_path = ROOT / 'Search/Runtime/ProductionQueryValidator.cs'
require('production exact file renamed', exact_path.is_file())
require('production document evaluator file renamed', document_path.is_file())
require('old exact file removed', not (ROOT / 'Search/Runtime/Beta110ProductionExactSearchEvaluator.cs').exists())
require('old document evaluator file removed', not (ROOT / 'Search/Runtime/LegacySearchExecutionFilterEvaluator.cs').exists())

exact = read('Search/Runtime/ProductionExactSearchEvaluator.cs')
document = read('Search/Runtime/ProductionQueryValidator.cs')
seed_document = read('Core/Prediction/SeedPredictionDocument.cs')
factory = read('Core/Prediction/Profiles/SeedPredictionDocumentFactory.cs')
contracts = read('Search/Contracts/SearchContracts.cs')
warnings = read('Presentation/Ui1/PlayerWarningPresentationPolicy.cs')
canonical = read('Search/Runtime/Beta111CanonicalEventShopSearchEvaluator.cs')
fast = read('Search/FamilyExecution/EventResultNumericPredicate.cs')

require('exact responsibility name', 'ProductionExactSearchResult' in exact and 'ProductionExactSearchEvaluator' in exact)
require('document responsibility name', 'ProductionQueryValidator' in document)
require('document viability validation name', 'ValidateDocumentContextAndViability' in document)
require('overall status contract is explicit', all(token in seed_document + factory for token in [
    'viability plus the Neow product-facing aggregate',
    'not a summary of every optional World',
    'not all-domain health']
))
require('query-local section validation retained', 'ValidateSection' in document and 'section.DomainStatus' in document)
require('completed warnings remain admitted', 'CompletedWithWarnings' not in document or 'DocumentNotReliable' in document)
require('failure code responsibility is documented', 'not an' in contracts and 'PredictionWarning' in contracts and 'Planner fallback' in contracts)
require('unknown warnings default developer diagnostic', '_ => PlayerWarningAudience.DeveloperDiagnostic' in warnings)
require('compatibility warning is separate audience', 'PlayerWarningAudience.PlayerCompatibility' in warnings)
require('canonical exact remains truth source', 'Beta111CanonicalEventShopSearchEvaluator.Evaluate' in exact)
require('fast remains candidate-only', 'Production' in fast and 'Exact remains final truth' in fast and 'Gpu' not in fast)
require('canonical-only bypass remains', 'RequiresCanonicalRootLocalDomain' in exact and 'CheapPredictionDomains == SeedPredictionDomainSelection.None' in exact)
require('witness remains exact output', 'SearchMatchWitness' in document and 'AttachCanonicalGlobalWitness' in exact)

print('PASS: Predictor/Production Exact responsibility and diagnostic boundaries')
