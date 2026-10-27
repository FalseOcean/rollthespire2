#!/usr/bin/env python3
from __future__ import annotations

import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def sha(relative: str) -> str:
    return hashlib.sha256((ROOT / relative).read_bytes()).hexdigest()


def require(value: bool, message: str, code: str) -> None:
    if not value:
        raise AssertionError(message)
    print(f"{code}=true")


catalog = read("Ui/Pages/Search/Ancient/AncientSearchUiCatalog.cs")
contracts = read("Search/Contracts/SearchContracts.cs")
compiler = read("Search/Compilation/SearchExecutionRequestFactory.cs")
page = read("Ui/Pages/Search/Ancient/AncientFilterPage.cs")
fixture = read("tests/shared/Beta109WorldFixtureFactory.cs")
name_resolver = read("Infrastructure/ContentNames/RuntimeGameContentNameResolver.cs")

require(
    "key.Category == BaseGameModelKeys.Categories.Event" in catalog
    and "new ModelKey(BaseGameModelKeys.Categories.Event, entry)" in catalog
    and "key.Category == BaseGameModelKeys.Categories.Ancient" not in catalog,
    "Ancient UI still binds a synthetic ANCIENT:* identity instead of the production EVENT:* key.",
    "searchAncientUiBindsRuntimeEventIdentity",
)
require(
    "AncientKey.Category == BaseGameModelKeys.Categories.Event" in contracts,
    "The structured Ancient branch contract does not require the production EVENT identity.",
    "searchAncientBranchUsesEventIdentity",
)
require(
    "NormalizeAncientIdentity" in compiler
    and "key.Category == BaseGameModelKeys.Categories.Ancient" in compiler
    and "new ModelKey(BaseGameModelKeys.Categories.Event, key.Entry)" in compiler,
    "Legacy ANCIENT:* draft inputs are not canonicalized to production EVENT:* identities.",
    "searchAncientLegacyIdentityCanonicalized",
)
require(
    "AncientIdentityFilters = NormalizeAncientIdentityFilters" in compiler
    and "BaseGameModelKeys.Categories.Event" in compiler,
    "The source-compatible legacy Ancient identity filter is not normalized to EVENT:*.",
    "searchAncientLegacyFilterUsesEventIdentity",
)
require(
    "pendingRows == 0" in page
    and "matrixComplete={completeMatrix" in page,
    "Ancient matrix completeness still ignores pending identity/catalog rows.",
    "searchAncientMatrixCompletenessAuthorityAware",
)
require(
    'Key(BaseGameModelKeys.Categories.Event, "OROBAS")' in fixture
    and 'Key(BaseGameModelKeys.Categories.Event, "DARV")' in fixture
    and "Categories.Ancient" not in fixture,
    "Beta109 fixtures still model Ancients in the synthetic ANCIENT category.",
    "searchAncientFixtureMatchesRuntimeEventIdentity",
)
require(
    "[GameContentKind.Ancient]" in name_resolver
    and '"MegaCrit.Sts2.Core.Models.AncientEventModel"' in name_resolver
    and "key.Category, key.Entry" in name_resolver,
    "Ancient runtime presentation does not resolve registered models by their production identity.",
    "searchAncientEventIdentityRuntimePresentation",
)

frozen = {
    "Core/World/Stable107AncientOptionPredictor.cs": "50884abac6534e4fc628a2b54140c38eb7236f24f5938350d083bea3eb7103bf",
    "Core/World/Beta109/Beta109AncientOptionProvider.cs": "1b2daad07b6e725d3ba7ada857f0f53f9106d08dc324eb8b465359038261423b",
    "Core/World/Beta109/Beta109WorldPredictionProvider.cs": "33a537006baf4d6f01b870df4efe8f07c22a82652c97c3c950251968413b2de3",
    "Search/Runtime/ProductionQueryValidator.cs": "89ed8426341479a0991ce8068d4a530c61b5a2d01f435b1547ba11977b6c714c",
}
for path, expected in frozen.items():
    require(sha(path) == expected, f"Frozen production file changed: {path}", "frozen_" + Path(path).stem)

print("searchAncientRuntimeEventIdentityHotfixValidation=true")
