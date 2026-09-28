// Family-local fixed 12-byte visible-seed reconstruction and xxHash64(seed=0).
// Dense callers decode the first ordinal in their natural eight-candidate invocation
// and advance the encoded seed in-place. Compact ABI1 callers retain full decode.
uint alphabet_byte(uint digit) {
    if (digit < 10u) return 48u + digit;
    if (digit < 18u) return 65u + (digit - 10u);
    if (digit < 23u) return 74u + (digit - 18u);
    return 80u + (digit - 23u);
}

void encode_seed12_packed(uint64_t ordinal, out uint64_t first8, out uint tail4) {
    uint d11 = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d10 = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d9  = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d8  = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d7  = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d6  = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d5  = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d4  = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d3  = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d2  = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d1  = uint(ordinal % uint64_t(34u)); ordinal /= uint64_t(34u);
    uint d0  = uint(ordinal);

    first8 = uint64_t(alphabet_byte(d0)) |
             (uint64_t(alphabet_byte(d1)) << 8u) |
             (uint64_t(alphabet_byte(d2)) << 16u) |
             (uint64_t(alphabet_byte(d3)) << 24u) |
             (uint64_t(alphabet_byte(d4)) << 32u) |
             (uint64_t(alphabet_byte(d5)) << 40u) |
             (uint64_t(alphabet_byte(d6)) << 48u) |
             (uint64_t(alphabet_byte(d7)) << 56u);
    tail4 = alphabet_byte(d8) |
            (alphabet_byte(d9) << 8u) |
            (alphabet_byte(d10) << 16u) |
            (alphabet_byte(d11) << 24u);
}

uint64_t prime1() { return make_u64(0x85ebca87u, 0x9e3779b1u); }
uint64_t prime2() { return make_u64(0x27d4eb4fu, 0xc2b2ae3du); }
uint64_t prime3() { return make_u64(0x9e3779f9u, 0x165667b1u); }
uint64_t prime4() { return make_u64(0xc2b2ae63u, 0x85ebca77u); }
uint64_t prime5() { return make_u64(0x165667c5u, 0x27d4eb2fu); }

uint64_t xxh_round(uint64_t accumulator, uint64_t input_value) {
    accumulator += input_value * prime2();
    accumulator = rotl64(accumulator, 31u);
    return accumulator * prime1();
}

uint next_seed_ascii(uint value, out bool carry) {
    carry = false;
    if (value >= 48u && value < 57u) return value + 1u;
    if (value == 57u) return 65u;
    if (value >= 65u && value < 72u) return value + 1u;
    if (value == 72u) return 74u;
    if (value >= 74u && value < 78u) return value + 1u;
    if (value == 78u) return 80u;
    if (value >= 80u && value < 90u) return value + 1u;
    carry = true;
    return 48u;
}

bool advance_seed_byte32(inout uint packed, uint shift) {
    bool carry;
    uint next = next_seed_ascii((packed >> shift) & 0xffu, carry);
    packed = (packed & ~(0xffu << shift)) | (next << shift);
    return carry;
}

bool advance_seed_byte64(inout uint64_t packed, uint shift) {
    bool carry;
    uint next = next_seed_ascii(uint((packed >> shift) & uint64_t(0xffu)), carry);
    packed = (packed & ~(uint64_t(0xffu) << shift)) | (uint64_t(next) << shift);
    return carry;
}

void advance_seed12_packed(inout uint64_t first8, inout uint tail4) {
    if (!advance_seed_byte32(tail4, 24u)) return;
    if (!advance_seed_byte32(tail4, 16u)) return;
    if (!advance_seed_byte32(tail4, 8u)) return;
    if (!advance_seed_byte32(tail4, 0u)) return;
    if (!advance_seed_byte64(first8, 56u)) return;
    if (!advance_seed_byte64(first8, 48u)) return;
    if (!advance_seed_byte64(first8, 40u)) return;
    if (!advance_seed_byte64(first8, 32u)) return;
    if (!advance_seed_byte64(first8, 24u)) return;
    if (!advance_seed_byte64(first8, 16u)) return;
    if (!advance_seed_byte64(first8, 8u)) return;
    advance_seed_byte64(first8, 0u);
}

uint64_t root_hash_for_seed12_packed(uint64_t first8, uint tail4) {
    uint64_t hash = prime5() + uint64_t(12u);
    hash ^= xxh_round(uint64_t(0u), first8);
    hash = rotl64(hash, 27u) * prime1() + prime4();
    hash ^= uint64_t(tail4) * prime1();
    hash = rotl64(hash, 23u) * prime2() + prime3();
    hash ^= hash >> 33u;
    hash *= prime2();
    hash ^= hash >> 29u;
    hash *= prime3();
    hash ^= hash >> 32u;
    return hash;
}

uint64_t root_hash_for_ordinal(uint64_t ordinal) {
    uint64_t first8;
    uint tail4;
    encode_seed12_packed(ordinal, first8, tail4);
    return root_hash_for_seed12_packed(first8, tail4);
}
