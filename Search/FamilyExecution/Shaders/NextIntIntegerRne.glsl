// Audited conversion: tools/experiments/fp64-compat-20261025/next_int.md.
// Positive uint bound; exact trunc(RN64((raw>>11)*2^-53*bound)). No RNG draw here.
uint rt2_next_int_rne_raw32(uint raw_low, uint raw_high, uint bound) {
    uint m_low = (raw_low >> 11u) | (raw_high << 21u);
    uint m_high = raw_high >> 11u;
    uint low_high, low_low, high_high, high_low;
    umulExtended(m_low, bound, low_high, low_low);
    umulExtended(m_high, bound, high_high, high_low);
    uint middle = high_low + low_high;
    uint upper = high_high + uint(middle < high_low);
    uint integral = (upper << 11u) | (middle >> 21u);
    // P=m*bound has at most 85 bits. If integral=0 then P<2^53
    // is exact in binary64. Otherwise the only truncation-changing round is
    // up to the next integer; its halfway point is 2^floor(log2(integral))
    // below that integer in P units. At a tie, the next integer is even.
    if (integral != 0u && (middle & 0x1fffffu) == 0x1fffffu) {
        uint halfway = 1u << uint(findMSB(integral));
        if (low_low >= 0u - halfway) integral += 1u;
    }
    return integral;
}
