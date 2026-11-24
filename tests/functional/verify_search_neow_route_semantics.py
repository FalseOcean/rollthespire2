#!/usr/bin/env python3
"""Independent semantic mirror for route-oriented Neow Search conditions."""

from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from typing import Sequence


@dataclass(frozen=True)
class Route:
    route_relic: str
    acquisition_order: tuple[str, ...]
    grouped_outputs: dict[str, tuple[tuple[str, ...], ...]]
    final_curse: str | None = None
    exact: bool = True


def unordered_equal(left: Sequence[str], right: Sequence[str]) -> bool:
    return Counter(left) == Counter(right)


def independent_group_targets(groups: Sequence[Sequence[str]], targets: Sequence[str]) -> bool:
    if len(targets) != 2:
        return False
    return any(
        i != j and targets[0] in left and targets[1] in right
        for i, left in enumerate(groups)
        for j, right in enumerate(groups)
    )


def route_matches(route: Route, selected_relic: str | None) -> bool:
    return selected_relic is None or route.route_relic == selected_relic


def require(condition: bool, code: str) -> None:
    if not condition:
        raise AssertionError(code)
    print(f"{code}=true")


def main() -> None:
    require(route_matches(Route("A", ("A",), {}), None), "neowUnselectedDoesNotFilter")
    require(route_matches(Route("A", ("A",), {}), "A"), "neowSelectedRouteMatches")
    require(not route_matches(Route("B", ("B",), {}), "A"), "neowUnselectedChoicesRejected")

    require(unordered_equal(("R1", "R2"), ("R2", "R1")), "largeCapsuleUnorderedPair")
    require(not unordered_equal(("R1", "R2"), ("R1", "R1")), "largeCapsuleDuplicateRejected")
    require(unordered_equal(("CARD", "CARD"), ("CARD", "CARD")), "treeSalveDuplicateMultiplicityPreserved")

    groups = (("A", "B", "C"), ("D", "E", "F"))
    require(independent_group_targets(groups, ("A", "E")), "kaleidoscopeUsesIndependentGroups")
    require(independent_group_targets(groups, ("E", "A")), "kaleidoscopeGroupOrderIgnored")
    require(not independent_group_targets(groups, ("A", "B")), "kaleidoscopeOneGroupCannotFillTwoSlots")
    duplicate_groups = (("A", "B", "C"), ("A", "E", "F"))
    require(independent_group_targets(duplicate_groups, ("A", "A")), "kaleidoscopeDuplicateAllowedWhenProduced")

    scroll_groups = (("C1", "C2", "U1"), ("C3", "C4", "U2"))
    require(any(unordered_equal(group, ("U1", "C2", "C1")) for group in scroll_groups),
            "scrollBoxesWholeBundleMatch")
    require(not any(unordered_equal(group, ("C1", "C3", "U1")) for group in scroll_groups),
            "scrollBoxesCannotStitchBundles")
    require(unordered_equal(("CLAW", "CLAW", "CLAW"), ("CLAW", "CLAW", "CLAW")),
            "scrollBoxesTripleClawMultiplicity")

    route_ab = Route("BONES", ("A", "B"), {"nested": (("X", "Y"),)}, "CURSE_1")
    route_ba = Route("BONES", ("B", "A"), {"nested": (("X", "Z"),)}, "CURSE_2")
    require(
        any(
            route_matches(route, "BONES")
            and unordered_equal(route.acquisition_order, ("A", "B"))
            and any(unordered_equal(group, ("X", "Y")) for group in route.grouped_outputs["nested"])
            and route.final_curse == "CURSE_1"
            for route in (route_ab, route_ba)
        ),
        "bonesSameRouteWitness",
    )
    require(
        not any(
            any(unordered_equal(group, ("X", "Y")) for group in route.grouped_outputs["nested"])
            and route.final_curse == "CURSE_2"
            for route in (route_ab, route_ba)
        ),
        "bonesCrossOrderStitchingRejected",
    )

    unknown = Route("A", ("A",), {}, exact=False)
    require(not (unknown.exact and route_matches(unknown, "A")), "unknownNeverMatchesStructuredNeow")
    print("searchNeowRouteSemanticFixture=true")


if __name__ == "__main__":
    main()
