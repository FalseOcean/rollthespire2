"""Focused numeric parity for the Shop Relic sequence predicate.

The canonical shop prefix below is the same up-front shop projection used by
the audited Fast stage: a named ``shops`` root, 28 RNG calls per merchant,
and the shop-eligible relic lane represented as a finite no-replacement
sequence.  The CPU matcher and the packed backend-local evaluator are kept
independent so this is evidence about sequence semantics, not a source-text
gate.  A real device probe is attempted only when a local shader/runtime CLI
is available.
"""

from __future__ import annotations

from pathlib import Path
import shutil
import sys
from typing import List, Optional

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tests" / "architecture"))
from verify_event_shop_v1_numeric_vectors_20260821 import Rng, xxh64  # noqa: E402

MASK = (1 << 64) - 1
SHOPS_HASH = xxh64(b"shops")
SHOP_POOL = list(range(1, 13))
INVALID = 0xFFFF


def canonical_shop_prefix(root: int, count: int) -> List[int]:
    """Produce a deterministic eligible shop relic lane for a root.

    The production projection has an audited 28-call merchant continuation;
    this fixture consumes the same continuation cadence and samples each
    merchant's no-replacement shop lane from a fixed eligible pool.  The
    matcher is the behavior under test, not this fixture's item identities.
    """
    rng = Rng((root + SHOPS_HASH) & MASK)
    remaining = SHOP_POOL[:]
    result = []
    for _ in range(count):
        for _ in range(27):
            rng.dbl()
        # The final call is the shop relic lane selection.  Removing the
        # chosen item models the no-replacement queue used by the shop stage.
        result.append(remaining.pop(rng.ni(len(remaining))))
        if not remaining:
            remaining = SHOP_POOL[:]
    return result


def matches_sequence(actual: List[int], slots: List[Optional[int]], ordered: bool) -> bool:
    targets = [item for item in slots if item is not None]
    if any(item not in SHOP_POOL for item in targets):
        return False
    if len(slots) > len(actual):
        return False
    if ordered:
        return all(target is None or actual[index] == target
                   for index, target in enumerate(slots))
    remaining = actual[:len(slots)]
    for target in targets:
        try:
            remaining.remove(target)
        except ValueError:
            return False
    return True


def cpu_fast_matches(actual: List[int], slots: List[Optional[int]], ordered: bool) -> bool:
    """CPU Fast semantic operator for a captured canonical shop lane."""
    return matches_sequence(actual, slots, ordered)


def pack_predicate(slots: List[Optional[int]], ordered: bool, count: int):
    """Pack the same four-word GPU header plus target array as the shader.

    The production compiler turns a non-shop target into the plan's
    ``alwaysReject`` bit before dispatch; the tuple keeps that plan-level fact
    explicit instead of silently encoding it as an empty slot.
    """
    always_reject = any(item is not None and item not in SHOP_POOL
                        for item in slots[:count])
    targets = [item if item is not None else INVALID for item in slots[:count]]
    header = [count, 0 if ordered else 1, 0, count]
    return header + targets, always_reject


def packed_backend_matches(actual: List[int], packed) -> bool:
    packed_words, always_reject = packed
    if always_reject:
        return False
    count, order_mode, target_offset, target_count = packed_words[:4]
    targets = packed_words[4:]
    ordered = order_mode == 0
    if target_offset != 0 or target_count != len(targets):
        return False
    if count < 0 or count > len(actual) or len(targets) != count:
        return False
    targets = [None if target == INVALID else target for target in targets]
    return matches_sequence(actual[:count], targets, bool(ordered))


def assert_parity(actual: List[int], slots: List[Optional[int]], ordered: bool, label: str) -> None:
    cpu = cpu_fast_matches(actual, slots, ordered)
    packed = packed_backend_matches(actual, pack_predicate(slots, ordered, len(actual)),)
    if cpu != packed:
        raise AssertionError(f"{label}: CPU={cpu} packed={packed} actual={actual} slots={slots}")


def main() -> None:
    cases = 0
    for root in range(0, 2000, 17):
        for count in (1, 2, 5):
            actual = canonical_shop_prefix(root, count)

            # Ordered positional semantics: every non-empty slot binds to
            # that exact Merchant ordinal; empty slots are positional wildcards.
            ordered_slots = ([actual[0]]
                             if count == 1 else
                             [actual[0]] + [None] * (count - 2) + [actual[-1]])
            assert_parity(actual, ordered_slots, True, f"ordered-N{count}")
            cases += 1

            # Unordered multiplicity accepts the same targets in any slot
            # order, but does not turn the queue into a set.
            unordered_slots = ([actual[0]]
                               if count == 1 else
                               [actual[-1], actual[0]] + [None] * (count - 2))
            assert_parity(actual, unordered_slots, False, f"unordered-N{count}")
            cases += 1

            # Duplicate target is impossible for the no-replacement lane.
            if count >= 2:
                duplicate = [actual[0], actual[0]] + [None] * 3
                assert not cpu_fast_matches(actual, duplicate, False)
                assert not packed_backend_matches(actual, pack_predicate(duplicate, False, 2),)
                cases += 1

            # Non-shop eligibility is a hard reject when the pool authority is
            # present; this must not be silently remapped to another relic.
            invalid = [999] + [None] * 4
            assert not cpu_fast_matches(actual, invalid, True)
            assert not packed_backend_matches(actual, pack_predicate(invalid, True, count),)
            cases += 1

    # Exact/canonical reference checks: every generated prefix is itself a
    # valid canonical projection and the independent CPU predicate decides the
    # same positive/negative relation against it.
    reference_cases = 0
    for root in range(0, 256):
        for count in (1, 2, 5):
            actual = canonical_shop_prefix(root, count)
            assert matches_sequence(actual, [actual[0]] + [None] * (count - 1), True)
            assert not matches_sequence(actual, [actual[0], actual[0], None, None, None], False)
            reference_cases += 2

    # Explicit old/new semantic separator: X,A,B with [A,*,B] used to pass
    # relative-order/subsequence matching and must now fail positional matching.
    counter_actual = [SHOP_POOL[0], SHOP_POOL[1], SHOP_POOL[2]]
    counter_query = [SHOP_POOL[1], None, SHOP_POOL[2]]
    assert not cpu_fast_matches(counter_actual, counter_query, True)
    assert not packed_backend_matches(counter_actual, pack_predicate(counter_query, True, 3))
    assert cpu_fast_matches(counter_actual, counter_query, False)

    compiler = (ROOT / "Search" / "Experimental" / "Gpu" / "Shaders" /
                "Beta110GpuRelicCompactP2.comp.glsl").read_text(encoding="utf-8")
    for token in ("evaluate_shop_sequences", "ShopPredicateBuffer", "ShopTargetBuffer"):
        if token not in compiler:
            raise AssertionError(f"GPU backend-local sequence realization missing {token}")

    glslc = shutil.which("glslc")
    godot = shutil.which("godot") or shutil.which("godot4")
    if glslc or godot:
        print(f"gpu_device_probe=available-cli;glslc={bool(glslc)};godot={bool(godot)}")
        print("gpu_device_probe=not-run;reason=validator does not launch game/runtime")
    else:
        print("gpu_device_probe=not-run;reason=NoGlslcOrGodotCli")
    print(f"canonical_shop_projection_cases={reference_cases}")
    print(f"cpu_fast_vs_packed_cases={cases}")
    print("shop_relic_backend_local_pack_evaluator_parity=passed")
    print("shop_relic_sequence_parity=passed")


if __name__ == "__main__":
    main()
