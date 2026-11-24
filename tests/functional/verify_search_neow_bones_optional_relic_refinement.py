#!/usr/bin/env python3
"""Focused semantic mirror for optional Neow's Bones relic refinement."""

from __future__ import annotations


def matches(offered: tuple[str, str], required: tuple[str, ...]) -> bool:
    return all(key in offered for key in dict.fromkeys(required))


def require(value: bool, code: str) -> None:
    if not value:
        raise AssertionError(code)
    print(f"{code}=true")


def main() -> None:
    offered = ("KALEIDOSCOPE", "LOST_COFFER")
    require(matches(offered, ()), "bonesZeroRelicRefinementAcceptsAnyPair")
    require(matches(offered, ("KALEIDOSCOPE",)), "bonesOneRelicRefinementMatchesContainingPair")
    require(not matches(offered, ("NEW_LEAF",)), "bonesOneRelicRefinementRejectsMissingRelic")
    require(matches(offered, ("LOST_COFFER", "KALEIDOSCOPE")), "bonesTwoRelicRefinementMatchesUnorderedPair")
    require(not matches(offered, ("KALEIDOSCOPE", "NEW_LEAF")), "bonesTwoRelicRefinementRejectsDifferentPair")
    print("searchNeowBonesOptionalRelicRefinementFixture=true")


if __name__ == "__main__":
    main()
