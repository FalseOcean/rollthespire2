// Keep the xoshiro state update explicit in two uint32 words to avoid the
// observed native-rotate failure in complete shader contexts.
// Composed families can carry both their own RNG and a shared reward donor.
#ifndef RT2_XOSHIRO_ROTATE45_SPLIT32
#define RT2_XOSHIRO_ROTATE45_SPLIT32
uint64_t rt2_rotate_left45_split32(uint64_t value) {
    uint lo = uint(value);
    uint hi = uint(value >> 32u);
    uint new_lo = (hi << 13u) | (lo >> 19u);
    uint new_hi = (lo << 13u) | (hi >> 19u);
    return uint64_t(new_lo) | (uint64_t(new_hi) << 32u);
}
#endif
