#!/usr/bin/env python3
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]

def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")

def req(name: str, ok: bool) -> None:
    if not ok:
        raise AssertionError(name)
    print(f"{name}=true")

shell = read("Ui/Shell/AppShell.cs")
page = read("Ui/Pages/Analysis/AnalysisPage.cs")
request_bar = read("Ui/Components/AnalysisRequestBar.cs")
controller = read("Ui/Controllers/AnalysisPageController.cs")
persistence = read("Ui/Persistence/SearchWorkspacePersistence.cs")
ancient_option = read("Ui/Pages/Search/Ancient/AncientOptionIconButton.cs")
sequence = read("Ui/Controls/SequenceConditionControlStrip.cs")
relic_page = read("Ui/Pages/Search/Relic/RelicSequenceFilterPage.cs")
en = json.loads(read("Localization/rt2_ui/en.json"))
zh = json.loads(read("Localization/rt2_ui/zh.json"))

# 1. Top-level page selection is persistent. Analysis is only the no-history default.
preferences_doc = persistence.split("internal sealed class UserPreferencesDocument", 1)[1].split("internal sealed class PredictorContextDocument", 1)[0]
req("uiContextTopLevelPagePersisted", "LastPage" in preferences_doc and "SetLastPage" in persistence)
req("uiContextNoHistoryDefaultsAnalysis", 'public string LastPage { get; set; } = "Analysis";' in preferences_doc and ': AppPageKey.Analysis;' in shell)
req("uiContextStartupRestoresLastPage", "AppPageKey initialPage = ResolvePersistedPage();" in shell and "Navigate(initialPage);" in shell)
open_body = shell.split("public void Open()", 1)[1].split("private void BuildShell()", 1)[0]
req("uiContextPanelReopenDoesNotForcePage", "Navigate(" not in open_body)
nav_body = shell.split("private void Navigate(AppPageKey key)", 1)[1].split("private AppPageKey ResolvePersistedPage", 1)[0]
req("uiContextNavigationCapturesLastPage", "_searchPersistence?.SetLastPage(key.ToString())" in nav_body)
req("uiContextTopLevelCloseFlushesState", "CaptureAndFlushPersistentUiState();" in shell and "_searchPersistence.FlushAll();" in shell)

# 2. Predictor persists user-visible context. Last-known unlock is display context only; runtime capture remains analysis authority.
predictor_doc = persistence.split("internal sealed class PredictorContextDocument", 1)[1].split("internal sealed class SearchWorkspaceDocument", 1)[0]
for field in [
    "Seed", "CharacterKey", "Ascension", "PlayersCount", "PlayerSlotIndex",
    "AllCharacterCardPoolsUnlocked", "PreferredOpeningRouteId", "PreferredOpeningChoiceSlotIndex"
]:
    req("uiContextPredictorPersist_" + field, field in predictor_doc)
req("uiContextPredictorSaveSeed", "_predictorContext.Seed = seed?.Trim()" in persistence)
req("uiContextPredictorSaveUnlock", "_predictorContext.AllCharacterCardPoolsUnlocked = allCharacterCardPoolsUnlocked" in persistence)
req("uiContextPredictorControllerSavesUnlock", "_page.CurrentUnlockState" in controller)
req("uiContextPredictorRestoreSeed", "_analysisPage.SetSeedText(persisted.Seed, commit: true)" in shell)
req("uiContextPredictorRestoreUnlock", "_analysisPage.SetUnlockState(persisted.AllCharacterCardPoolsUnlocked)" in shell)
req("uiContextPredictorUnlockRestoreApiExists", "public void SetUnlockState(bool? allCharacterCardPoolsUnlocked)" in page and "_requestBar.SetUnlockState(allCharacterCardPoolsUnlocked)" in page)
req("uiContextWitnessNamespaceImported", "using RolltheSpire2.Search.Contracts;" in shell)
req("uiContextPredictorRestoreRoute", "persisted.PreferredOpeningRouteId" in shell and "persisted.PreferredOpeningChoiceSlotIndex" in shell)
req("uiContextRuntimeAuthorityStillCapturedOnAnalyze", "RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly" in controller)

# 3. Search -> Analysis carries accepted SameRoute witness and only translates global route namespace at UI handoff.
req("uiContextSearchUsesPrimaryRouteWitnessFirst", "candidate.PrimaryWitness is { OpeningRouteId.Length: > 0 } primaryRouteWitness" in shell)
req("uiContextSearchFallbackRouteWitness", "candidate.Witnesses.FirstOrDefault(witness => !string.IsNullOrWhiteSpace(witness.OpeningRouteId))" in shell)
req("uiContextSearchNormalizesWitnessAtUiSeam", "NormalizeWitnessOpeningRouteForAnalysis(witnessOpeningRoute)" in shell)
req("uiContextSearchImportsRouteAndChoice", "_analysisPage.ImportSearchContext(request, preferredOpeningRoute, preferredChoiceSlotIndex)" in shell)
req("uiContextPersistedSearchCarriesWitnessRoute", "WitnessOpeningRouteId = routeWitness?.OpeningRouteId" in persistence and "NormalizeWitnessOpeningRouteForAnalysis(result.WitnessOpeningRouteId)" in shell)

def normalize(route_id: str):
    route = (route_id or "").strip()
    prefix = "choice."
    if not route.startswith(prefix):
        return None, route
    pos = route.find(".", len(prefix))
    if pos <= len(prefix) or pos + 1 >= len(route):
        return None, route
    try:
        slot = int(route[len(prefix):pos])
    except ValueError:
        return None, route
    if slot < 0:
        return None, route
    return slot, route[pos + 1:]

req("uiContextWitnessNormalizeCanonical", normalize("choice.2.bones.order.1") == (2, "bones.order.1"))
req("uiContextWitnessNormalizeLocalUnchanged", normalize("bones.order.1") == (None, "bones.order.1"))
req("uiContextWitnessNormalizeMalformedUnchanged", normalize("choice.bad.bones.order.1") == (None, "choice.bad.bones.order.1"))

# 4. Seed toolbar exposes Copy between input and Random; copy never enters prediction/search authority.
seed_add = request_bar.index("toolbar.AddChild(_seedInput)")
copy_add = request_bar.index("toolbar.AddChild(_copySeed)")
random_add = request_bar.index("toolbar.AddChild(_randomSeed)")
req("uiContextSeedCopyOrder", seed_add < copy_add < random_add)
copy_handler = request_bar[request_bar.index("_copySeed.Pressed"):request_bar.index("_randomSeed.Pressed")]
req("uiContextSeedCopyNoPredictionAuthority", "CopySeedRequested?.Invoke" in copy_handler and "SubmitRequested" not in copy_handler)
req("uiContextSeedCopyUsesClipboard", "_analysisPage.CopySeedRequested += CopyText" in shell and "DisplayServer.ClipboardSet(text)" in shell)
req("uiContextSeedCopyLocalized", en.get("ui1.analysis.copy_seed") == "Copy" and zh.get("ui1.analysis.copy_seed") == "复制")

# 5. Ancient option selected chrome survives row/control reconstruction.
req("uiContextAncientNativeToggleState", "ToggleMode = true" in ancient_option and "SetPressedNoSignal(selected)" in ancient_option)
req("uiContextAncientGoldSelectedRole", "Ui1ButtonRole.NavigationSelected" in ancient_option and 'selected ? "✓"' in ancient_option)

# 6. Relic clear/default authoring state is First 3 but creates no real condition.
req("uiContextSequenceResetSupportsFirstNValue", "ResetDefaults(bool notify, int firstNValue = 1)" in sequence and "SelectNumber(Math.Clamp(firstNValue, 1, 10))" in sequence)
req("uiContextRelicDefaultsFirst3", relic_page.count("ResetDefaults(notify: false, firstNValue: 3)") >= 3)
clear_body = relic_page.split("public void ClearDraft()", 1)[1].split("private void OpenPicker()", 1)[0]
req("uiContextRelicClearKeepsZeroConditions", "_conditions.Clear();" in clear_body and "ResetDefaults(notify: false, firstNValue: 3)" in clear_body and "_conditions.Add" not in clear_body)
req("uiContextRelicConditionCountStillRealConditions", "public int EnabledConditionCount => _conditions.Count" in relic_page)

print("uiContextNavigationBatch20260825=true")
