#!/usr/bin/env python3
import json
from pathlib import Path
import sys

MASK = (1 << 64) - 1
P1 = 11400714785074694791
P2 = 14029467366897019727
P3 = 1609587929392839161
P4 = 9650029242287828579
P5 = 2870177450012600261
SM_INCREMENT = 11400714819323198485
SM_MULTIPLIER_1 = 13787848793156543929
SM_MULTIPLIER_2 = 10723151780598845931


def rotate_left(value: int, count: int) -> int:
    return ((value << count) | (value >> (64 - count))) & MASK


def xxhash64(data: bytes, seed: int = 0) -> int:
    length = len(data)
    index = 0
    if length >= 32:
        states = [
            (seed + P1 + P2) & MASK,
            (seed + P2) & MASK,
            seed & MASK,
            (seed - P1) & MASK,
        ]
        while index <= length - 32:
            for state_index in range(4):
                lane = int.from_bytes(data[index:index + 8], "little")
                index += 8
                states[state_index] = rotate_left(
                    (states[state_index] + lane * P2) & MASK, 31)
                states[state_index] = (states[state_index] * P1) & MASK
        result = (rotate_left(states[0], 1) + rotate_left(states[1], 7) +
                  rotate_left(states[2], 12) + rotate_left(states[3], 18)) & MASK
        for state in states:
            mixed = (rotate_left((state * P2) & MASK, 31) * P1) & MASK
            result ^= mixed
            result = (result * P1 + P4) & MASK
    else:
        result = (seed + P5) & MASK
    result = (result + length) & MASK
    while index <= length - 8:
        lane = int.from_bytes(data[index:index + 8], "little")
        index += 8
        lane = (rotate_left((lane * P2) & MASK, 31) * P1) & MASK
        result ^= lane
        result = (rotate_left(result, 27) * P1 + P4) & MASK
    if index <= length - 4:
        lane = int.from_bytes(data[index:index + 4], "little")
        index += 4
        result ^= (lane * P1) & MASK
        result = (rotate_left(result, 23) * P2 + P3) & MASK
    while index < length:
        result ^= (data[index] * P5) & MASK
        index += 1
        result = (rotate_left(result, 11) * P1) & MASK
    result ^= result >> 33
    result = (result * P2) & MASK
    result ^= result >> 29
    result = (result * P3) & MASK
    result ^= result >> 32
    return result & MASK


def splitmix_next(state: int) -> tuple[int, int]:
    state = (state + SM_INCREMENT) & MASK
    value = state
    value = ((value ^ (value >> 30)) * SM_MULTIPLIER_1) & MASK
    value = ((value ^ (value >> 27)) * SM_MULTIPLIER_2) & MASK
    return state, (value ^ (value >> 31)) & MASK


class Xoshiro256StarStar:
    def __init__(self, seed: int):
        state = seed
        self.state = []
        for _ in range(4):
            state, value = splitmix_next(state)
            self.state.append(value)

    def next_uint64(self) -> int:
        s0, s1, s2, s3 = self.state
        result = (rotate_left((s1 * 5) & MASK, 7) * 9) & MASK
        temporary = (s1 << 17) & MASK
        s2 ^= s0
        s3 ^= s1
        s1 ^= s2
        s0 ^= s3
        s2 ^= temporary
        s3 = rotate_left(s3, 45)
        self.state = [s0 & MASK, s1 & MASK, s2 & MASK, s3 & MASK]
        return result

    def next_int(self, maximum: int) -> int:
        return int(((self.next_uint64() >> 11) * (2 ** -53)) * maximum)

    def shuffle(self, values: list) -> None:
        for count in range(len(values), 1, -1):
            selected = self.next_int(count)
            tail = count - 1
            values[selected], values[tail] = values[tail], values[selected]


def verify_vector(vector: dict) -> None:
    seed = vector["seed"]
    root = xxhash64(seed.encode("ascii"))
    stream_hash = xxhash64(b"up_front")
    stream_seed = (root + stream_hash) & MASK
    assert f"{root:016x}" == vector["rootHashHex"]
    assert f"{stream_hash:016x}" == vector["streamHashHex"]
    assert f"{stream_seed:016x}" == vector["streamSeedHex"]

    rng = Xoshiro256StarStar(stream_seed)
    for source in vector["sharedBuckets"]:
        values = list(source)
        rng.shuffle(values)

    actual = {}
    for lane in ("Common", "Uncommon", "Rare", "Shop"):
        values = list(vector["playerBuckets"][lane])
        rng.shuffle(values)
        if lane == "Shop":
            allowed = vector["shopAllowed"]
            allowed_by_entry = dict(zip(vector["playerBuckets"][lane], allowed))
            values = [entry for entry in reversed(values) if allowed_by_entry[entry]]
        actual[lane] = values
    assert actual == vector["expectedLanes"], (vector["id"], actual)
    checkpoint = vector["expectedCheckpoint"]
    assert rng.state == [checkpoint["s0"], checkpoint["s1"], checkpoint["s2"], checkpoint["s3"]]
    print(f"relicBagVector={vector['id']};passed=true;commonFirst={actual['Common'][0]};shopFirst={actual['Shop'][0]};")


def main() -> int:
    root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
    data = json.loads((root / "tests/fixtures/beta110_relic_bag_fast_c2_vectors.json").read_text(encoding="utf-8"))
    assert data["schema"] == "rollthespire2.beta110.relic-bag-fast-c2.v1"
    for vector in data["vectors"]:
        verify_vector(vector)
    print("beta110RelicBagC2SharedCpuGpuVectors=Passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
