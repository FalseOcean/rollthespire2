// Historical Capsule rarity/target-rank donor, extended with a fixed-Bones
// arrival checkpoint and a three-draw grouped Small+Large matcher.
uint rfull_meta(uint index) { return rfull_capsule.values[index]; }
uint64_t rfull_u64(uint index) { return make_u64(rfull_meta(index), rfull_meta(index + 1u)); }

#ifndef RFULL_CHECKPOINT_ONLY
void rfull_rng_initialize(uint64_t seed) { rng_initialize(seed); }
uint rfull_next_int(uint bound) { return next_int(bound); }
uint64_t rfull_next_u64() { return next_u64(); }
#endif

float rfull_next_float() {
    return float(double(rfull_next_u64() >> 11u) * 1.1102230246251565e-16lf);
}

#ifndef RFULL_CHECKPOINT_ONLY
bool rfull_positive_allowed(uint id, uint curse) {
    uint players = rfull_meta(15u);
    uint flags = rfull_meta(16u);
    if (id == 17u && players <= 1u) return false;
    if (id == 14u && (flags & 4u) == 0u) return false;
    if (id == 22u && (flags & 8u) == 0u) return false;
    if (id == 23u && players != 1u) return false;
    if (curse == 0u && id == 13u) return false;
    if (curse == 2u && id == 10u) return false;
    if (curse == 4u && id == 19u) return false;
    if (curse == 7u && id == 21u) return false;
    if (curse == 6u && (id == 20u || id == 16u)) return false;
    return true;
}

bool rfull_top_contains(uint64_t root, uint required) {
    uint curse_count = rfull_meta(17u);
    if (curse_count == 0u || curse_count > 10u) return false;
    rng_initialize(root + uint64_t(rfull_meta(14u)) + rfull_u64(18u));
    uint curse = rfull_meta(32u + next_int(curse_count));
    uint positives[17];
    uint count = 0u;
    for (uint id = 10u; id <= 23u; ++id)
        if (rfull_positive_allowed(id, curse)) positives[count++] = id;
    if (curse != 3u) positives[count++] = next_int(2u) == 0u ? 24u : 28u;
    positives[count++] = next_int(2u) == 0u ? 26u : 29u;
    positives[count++] = next_int(2u) == 0u ? 25u : 27u;
    for (uint remaining = count; remaining > 1u; --remaining) {
        uint selected = next_int(remaining);
        uint tail = remaining - 1u;
        uint temporary = positives[selected];
        positives[selected] = positives[tail];
        positives[tail] = temporary;
    }
    return curse == required || positives[0u] == required || positives[1u] == required;
}
#endif

void rfull_swap_tracked(inout uint position, uint selected, uint tail) {
    if (position == selected) position = tail;
    else if (position == tail) position = selected;
}

// Leaves the global RNG at Direct Rewards or RewardsAfterBones. The Bones pool
// shuffle is the arrival checkpoint; rejected identity roots never touch UpFront.
#ifndef RFULL_CHECKPOINT_ONLY
bool rfull_prepare_rewards(uint64_t root) {
    uint arrival = rfull_meta(0u);
    uint required_top = arrival == 0u ? rfull_meta(1u) : 5u;
    if (!rfull_top_contains(root, required_top)) return false;
    rng_initialize(root + uint64_t(rfull_meta(14u)) + rfull_u64(20u));
    if (arrival == 0u) return true;

    uint bones_count = rfull_meta(24u);
    if (bones_count < 2u || bones_count > 64u) return false;
    uint position0 = rfull_meta(26u);
    uint position1 = rfull_meta(28u);
    if (position0 >= bones_count || position1 >= bones_count || position0 == position1) return false;
    for (uint remaining = bones_count; remaining > 1u; --remaining) {
        uint selected = next_int(remaining);
        uint tail = remaining - 1u;
        rfull_swap_tracked(position0, selected, tail);
        rfull_swap_tracked(position1, selected, tail);
    }
    // Bones shuffle positions describe offer order only. Required acquisition
    // order is a route constraint and must never reject the reverse offer layout.
    return (position0 == 0u && position1 == 1u) || (position0 == 1u && position1 == 0u);
}
#endif

uint rfull_draw_count() {
    if (rfull_meta(0u) == 2u || rfull_meta(0u) == 3u) return 3u;
    return rfull_meta(1u) == 3u ? 2u : 1u;
}

bool rfull_distinct_assignment(
    bool m00, bool m01, bool m02,
    bool m10, bool m11, bool m12,
    bool m20, bool m21, bool m22,
    uint draw_count, uint target_count)
{
    if (target_count == 1u) return m00 || (draw_count > 1u && m10) || (draw_count > 2u && m20);
    if (target_count == 2u) {
        return (m00 && m11) || (m01 && m10) ||
               (draw_count > 2u && ((m00 && m21) || (m01 && m20) || (m10 && m21) || (m11 && m20)));
    }
    return draw_count == 3u && target_count == 3u &&
           ((m00 && m11 && m22) || (m00 && m12 && m21) ||
            (m01 && m10 && m22) || (m01 && m12 && m20) ||
            (m02 && m10 && m21) || (m02 && m11 && m20));
}

// Three chronological draws share rarity rolls and lane consumption. Only the
// source partition changes with pickup order: S|LL or LL|S. Target0 belongs to
// Small and target1/2 to Large. Never turn source constraints into grouped Any.
bool rfull_observation_assignment(
    bool m00, bool m01, bool m02,
    bool m10, bool m11, bool m12,
    bool m20, bool m21, bool m22,
    uint draw_count, uint target_count)
{
    if (rfull_meta(0u) != 3u)
        return rfull_distinct_assignment(m00,m01,m02,m10,m11,m12,m20,m21,m22,draw_count,target_count);
    bool any_order = rfull_meta(29u) == 0u;
    bool small_first = any_order || rfull_meta(30u) == rfull_meta(1u);
    bool large_first = any_order || rfull_meta(30u) == rfull_meta(2u);
    // An unobserved source still consumes its actual draws and rarity lanes.
    // Flag16 means all packed targets belong to Large; otherwise target0 is Small.
    if ((rfull_meta(16u) & 16u) != 0u) {
        bool a = target_count == 1u ? (m10 || m20) : ((m10 && m21) || (m11 && m20));
        bool b = target_count == 1u ? (m00 || m10) : ((m00 && m11) || (m01 && m10));
        return (small_first && a) || (large_first && b);
    }
    if (target_count == 1u) return (small_first && m00) || (large_first && m20);
    bool a = m00 && (target_count == 2u ? (m11 || m21) : ((m11 && m22) || (m12 && m21)));
    bool b = m20 && (target_count == 2u ? (m01 || m11) : ((m01 && m12) || (m02 && m11)));
    return (small_first && a) || (large_first && b);
}

bool rfull_rarity_compatible(uint rarities[3], uint draw_count, uint target_count) {
    uint t0 = rfull_meta(6u), t1 = rfull_meta(9u), t2 = rfull_meta(12u);
    return rfull_observation_assignment(
        rarities[0] == t0, rarities[0] == t1, rarities[0] == t2,
        rarities[1] == t0, rarities[1] == t1, rarities[1] == t2,
        rarities[2] == t0, rarities[2] == t1, rarities[2] == t2,
        draw_count, target_count);
}

// Entry requires the actual Capsule-arrival Rewards state. Both standalone Rfull
// replay and the private N pair checkpoint use this single R-owned matcher.
bool rfull_capsule_after_rewards(uint64_t root) {
    uint draw_count = rfull_draw_count();
    uint target_count = rfull_meta(3u);
    uint rarities[3];
    rarities[0] = 0u; rarities[1] = 0u; rarities[2] = 0u;
    for (uint draw = 0u; draw < draw_count; ++draw) {
        float roll = rfull_next_float();
        rarities[draw] = roll < 0.5f ? 1u : (roll < 0.83f ? 2u : 3u);
    }

    // Strong specialization gate: no UpFront initialization or shuffle work has
    // happened before this exact rarity-composition rejection.
    if (!rfull_rarity_compatible(rarities, draw_count, target_count)) return false;
#ifdef RFULL_TRACK_HEAVY_ENTRY
    ++nr_heavy_entries;
#endif

    uint ranks[3];
    ranks[0] = rfull_meta(5u); ranks[1] = rfull_meta(8u); ranks[2] = rfull_meta(11u);
    rfull_rng_initialize(root + rfull_u64(22u));
    // No later consumer uses this private UpFront state. Keep all prerequisite
    // buckets, including shared ones, but stop after the last observed target.
    uint last_target_bucket = rfull_meta(4u);
    if (target_count > 1u) last_target_bucket = max(last_target_bucket, rfull_meta(7u));
    if (target_count > 2u) last_target_bucket = max(last_target_bucket, rfull_meta(10u));
    for (uint bucket = 0u; bucket <= last_target_bucket; ++bucket) {
        for (uint remaining = rfull_meta(106u + bucket); remaining > 1u; --remaining) {
            uint selected = rfull_next_int(remaining);
            uint tail = remaining - 1u;
            if (bucket == rfull_meta(4u)) rfull_swap_tracked(ranks[0], selected, tail);
            if (target_count > 1u && bucket == rfull_meta(7u)) rfull_swap_tracked(ranks[1], selected, tail);
            if (target_count > 2u && bucket == rfull_meta(10u)) rfull_swap_tracked(ranks[2], selected, tail);
        }
    }

    uint consumed[3];
    consumed[0] = 0u; consumed[1] = 0u; consumed[2] = 0u;
    uint ordinals[3];
    ordinals[0] = 0u; ordinals[1] = 0u; ordinals[2] = 0u;
    for (uint draw = 0u; draw < draw_count; ++draw) {
        uint lane = rarities[draw] - 1u;
        ordinals[draw] = consumed[lane]++;
    }

    bool m00 = rarities[0] == rfull_meta(6u) && ranks[0] == ordinals[0];
    bool m01 = target_count > 1u && rarities[0] == rfull_meta(9u) && ranks[1] == ordinals[0];
    bool m02 = target_count > 2u && rarities[0] == rfull_meta(12u) && ranks[2] == ordinals[0];
    bool m10 = draw_count > 1u && rarities[1] == rfull_meta(6u) && ranks[0] == ordinals[1];
    bool m11 = draw_count > 1u && target_count > 1u && rarities[1] == rfull_meta(9u) && ranks[1] == ordinals[1];
    bool m12 = draw_count > 1u && target_count > 2u && rarities[1] == rfull_meta(12u) && ranks[2] == ordinals[1];
    bool m20 = draw_count > 2u && rarities[2] == rfull_meta(6u) && ranks[0] == ordinals[2];
    bool m21 = draw_count > 2u && target_count > 1u && rarities[2] == rfull_meta(9u) && ranks[1] == ordinals[2];
    bool m22 = draw_count > 2u && target_count > 2u && rarities[2] == rfull_meta(12u) && ranks[2] == ordinals[2];
    return rfull_observation_assignment(m00, m01, m02, m10, m11, m12, m20, m21, m22, draw_count, target_count);
}
#ifndef RFULL_CHECKPOINT_ONLY
bool rfull_capsule_matches(uint64_t root) {
    return rfull_prepare_rewards(root) && rfull_capsule_after_rewards(root);
}
#endif
