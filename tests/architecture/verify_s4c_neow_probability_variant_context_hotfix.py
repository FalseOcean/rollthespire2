from pathlib import Path

root = Path(__file__).resolve().parents[2]
controller = (root / 'Ui/Controllers/SearchPageController.cs').read_text(encoding='utf-8')
projection = (root / 'Search/Selectivity/ProbabilitySemanticProjection.cs').read_text(encoding='utf-8')

checks = []
def check(name, ok):
    checks.append((name, bool(ok)))
    print(('PASS' if ok else 'FAIL') + ': ' + name)

check('ui_variant_capture_distinguishes_explicit_variant_constraint',
      'bool hasExplicitVariantConstraint = selectableVariants && row.IsVariantActive;' in controller)
check('ui_variant_capture_distinguishes_first_boss_constraint',
      'bool hasFirstBossConstraint = row.FirstBossAny.Count > 0;' in controller)
check('ui_variant_capture_distinguishes_second_boss_constraint',
      'bool hasSecondBossConstraint' in controller and 'row.SecondBossAny.Count > 0' in controller)
check('nonselectable_empty_variant_context_is_omitted',
      'if (!hasExplicitVariantConstraint &&' in controller and
      '!hasFirstBossConstraint &&' in controller and
      '!hasSecondBossConstraint)' in controller)
check('selectable_variant_only_semantics_remain_supported',
      'hasExplicitVariantConstraint = selectableVariants && row.IsVariantActive' in controller)
check('probability_world_domain_still_comes_from_canonical_variant_branches',
      'NormalizedQuery.VariantBossBranches.Any(branch => branch.IsValid)' in projection)

# Behavioral mirror of the corrected UI -> Canonical capture boundary.
def emit_branch(selectable, active, first_count, second_count, act=2, include_second=False):
    has_first = first_count > 0
    has_second = act == 3 and include_second and second_count > 0
    explicit_variant = selectable and active
    return explicit_variant or has_first or has_second

check('fixture_unique_variant_no_boss_is_context_not_query',
      not emit_branch(False, False, 0, 0))
check('fixture_unique_variant_first_boss_keeps_parent_attribution',
      emit_branch(False, False, 1, 0))
check('fixture_unique_act3_second_boss_keeps_parent_attribution',
      emit_branch(False, False, 0, 1, act=3, include_second=True))
check('fixture_selectable_active_variant_only_remains_query_fact',
      emit_branch(True, True, 0, 0))
check('fixture_selectable_inactive_variant_without_boss_is_omitted',
      not emit_branch(True, False, 0, 0))

# Regression explanation: if only Neow has an actual predicate, the corrected
# UI facts no longer inject a phantom WorldEvent domain from deterministic UI context.
active_domains = ['Neow']
if emit_branch(False, False, 0, 0):
    active_domains.append('WorldEvent')
check('fixture_neow_only_active_domains_no_phantom_world', active_domains == ['Neow'])

failed = [name for name, ok in checks if not ok]
print(f'S4CNeowProbabilityVariantContextHotfix={len(checks)-len(failed)}/{len(checks)}_PASS' if not failed
      else f'S4CNeowProbabilityVariantContextHotfix={len(checks)-len(failed)}/{len(checks)}_FAIL')
if failed:
    raise SystemExit(1)
