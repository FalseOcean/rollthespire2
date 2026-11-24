#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")

def req(name, ok):
    if not ok:
        raise AssertionError(name)
    print(f"{name}=true")

page = read("Ui/Pages/Analysis/AnalysisPage.cs")
group = read("Ui/Components/AnalysisAncientGroupCard.cs")
builder = read("Presentation/Ui1/SeedAnalysisPresentationBuilder.cs")

# Hard Cut 4 semantic/presentation removals remain frozen even though Final Convergence 6
# intentionally replaces the old full-width three-row geometry.
req("singleAncientGroupCard", "_ancientGroupCard = new AnalysisAncientGroupCard" in page)
req("oldActAncientHeadingsGone", "_act2AncientHeading" not in page and "_act3AncientHeading" not in page)
req("oldActAncientListsGone", "_act2AncientList" not in page and "_act3AncientList" not in page)
for forbidden in ["IdentityPrecision", "OptionPrecisionLabel", "Completeness", "Authority", "canonicalNeowOptionsNote"]:
    req("ordinaryAncientNo_" + forbidden, forbidden not in group)
req("generatedOptionsHeadingGone", "WorldAncientOptions" not in page and "生成选项" not in group)
req("targetIdentityPreserved", "OptionCompositeText" in group and "option.CharacterTargetDisplay?.DisplayName" in group and "CharacterPortrait" not in group)
req("optionDisplayNameNoTargetDuplication", "DisplayName = uiText.Format(" not in builder[builder.index("private static AncientOptionPredictionViewModel BuildAncientOptionViewModel"):builder.index("return new AncientOptionPredictionViewModel")])
req("searchAncientStillExists", (ROOT / "Ui/Pages/Search/Ancient/AncientOptionConditionEditor.cs").exists())
print("predictorAncientGroupCardHardCut4Invariant=true")
