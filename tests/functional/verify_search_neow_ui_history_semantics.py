#!/usr/bin/env python3
"""Small semantic mirror for UI-only Neow route/effect history activation."""

from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class UiDraft:
    normal_route: str | None = None
    bones_mode: bool = False
    bones_pair: list[str] = field(default_factory=list)
    effects: dict[str, tuple[str, ...]] = field(default_factory=dict)
    final_curse: str | None = None

    def active_route(self) -> str | None:
        return "BONES" if self.bones_mode else self.normal_route

    def active_effects(self) -> dict[str, tuple[str, ...]]:
        if self.bones_mode:
            return {key: self.effects[key] for key in self.bones_pair if key in self.effects}
        if self.normal_route and self.normal_route in self.effects:
            return {self.normal_route: self.effects[self.normal_route]}
        return {}

    def clear_all_effects(self) -> None:
        self.effects.clear()
        self.final_curse = None


def require(value: bool, code: str) -> None:
    if not value:
        raise AssertionError(code)
    print(f"{code}=true")


def main() -> None:
    draft = UiDraft(normal_route="LARGE", effects={"LARGE": ("A", "B")})
    require(draft.active_effects() == {"LARGE": ("A", "B")}, "normalRouteEffectActive")

    draft.normal_route = "KALEIDOSCOPE"
    draft.effects["KALEIDOSCOPE"] = ("C1", "C2")
    require(draft.effects["LARGE"] == ("A", "B"), "normalRouteHistoryRetained")
    require(draft.active_effects() == {"KALEIDOSCOPE": ("C1", "C2")}, "inactiveNormalHistoryNotCompiled")

    draft.bones_mode = True
    draft.normal_route = None
    draft.bones_pair = ["LARGE", "KALEIDOSCOPE"]
    draft.final_curse = "CURSE"
    require(set(draft.active_effects()) == {"LARGE", "KALEIDOSCOPE"}, "bonesOnlySelectedRelicEffectsActive")

    draft.bones_pair.remove("LARGE")
    require("LARGE" in draft.effects, "removedBonesRelicHistoryRetained")
    require("LARGE" not in draft.active_effects(), "removedBonesRelicHistoryNotCompiled")

    draft.bones_mode = False
    require(draft.active_route() is None, "leavingBonesRestoresUnconstrainedNeow")
    require(draft.bones_pair == ["KALEIDOSCOPE"], "bonesPairRetainedWhileInactive")
    require(draft.final_curse == "CURSE", "bonesCurseRetainedWhileInactive")

    draft.clear_all_effects()
    require(draft.effects == {} and draft.final_curse is None, "clearAllEffectsClearsVisibleAndHidden")
    require(draft.bones_pair == ["KALEIDOSCOPE"] and not draft.bones_mode,
            "clearAllEffectsPreservesRouteSelections")
    print("searchNeowUiHistorySemanticFixture=true")


if __name__ == "__main__":
    main()
