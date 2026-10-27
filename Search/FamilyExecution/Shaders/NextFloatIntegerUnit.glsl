// Audited unit NextFloat conversion; see tools/experiments/fp64-compat-20261025/next_float.md.
// Input is low/high words of one unchanged RNG draw. Round binary32 ties to even.
uint rt2_next_float_bits_u32(uvec2 raw) {
    uint lo = (raw.x >> 11u) | (raw.y << 21u);
    uint hi = raw.y >> 11u;
    if ((lo | hi) == 0u) return 0u;
    uint k = hi != 0u ? 32u + uint(findMSB(hi)) : uint(findMSB(lo));
    uint significand;
    if (k <= 23u) {
        significand = lo << (23u - k);
    } else {
        uint shift = k - 23u; // 1..29, so neither shift below is by 32.
        significand = (lo >> shift) | (hi << (32u - shift));
        uint remainder = lo & ((1u << shift) - 1u);
        uint halfway = 1u << (shift - 1u);
        if (remainder > halfway || (remainder == halfway && (significand & 1u) != 0u))
            significand += 1u;
    }
    // Adding the fraction handles significand==2^24 by carrying into exponent.
    // In particular, the largest inputs may correctly round to +1.0f.
    return ((k + 74u) << 23u) + (significand - 0x00800000u);
}

float rt2_next_float_from_words_u32(uvec2 raw) {
    return uintBitsToFloat(rt2_next_float_bits_u32(raw));
}
