#ifndef MC_LOCAL_NEOW
#define MC_LOCAL_NEOW 0
#endif
layout(set = 0, binding = MC_META_BINDING, std430) readonly restrict buffer ModernCapsuleMeta { uint values[]; } capsule_meta;

const uint MC_NONE = 255u;
const uint MC_CIRCLET = 65535u;
const uint MC_UNTRACKED = 65534u;
uint mc_value(uint index) { return capsule_meta.values[index]; }
uint64_t mc_hash(uint index) { return make_u64(mc_value(index), mc_value(index + 1u)); }
uint mc_slots(uint source) { return source == 28u ? 1u : source == 3u ? 2u : 0u; }
uint64_t mc_bit(uint source) { return source < 64u ? uint64_t(1u) << source : uint64_t(0u); }
bool mc_contains(uint start, uint count, uint key) {
    for (uint index = 0u; index < count; ++index)
        if (mc_value(start + index) == key) return true;
    return false;
}

#if !MC_LOCAL_NEOW
bool mc_top_contains(uint64_t player_root, uint source) {
    rng_initialize(player_root + mc_hash(8u));
    uint ordinal = mc_next_int(mc_value(23u));
    if (mc_value(mc_value(22u) + ordinal) == source) return true;
#if !MC_POSITIVE
    return false;
#else
    if (source == 5u) return false;
    uint at = mc_value(46u) + ordinal * 25u;
    uint length = mc_value(at), choices = mc_value(at + 1u);
    // The visit only observes membership in the first two shuffled slots.
    // Track every position carrying this source, including duplicate entries.
    uint positions = 0u;
    for (uint i = 0u; i < length; ++i)
        if (mc_value(at + 2u + i) == source) positions |= 1u << i;
    for (uint i = 0u; i < choices; ++i)
        if (mc_value(at + 2u + length + i * 2u + mc_next_int(2u)) == source)
            positions |= 1u << (length + i);
    if (positions == 0u) return false;
    length += choices;
    uint64_t seed = next_u64();
    rng_initialize(seed);
    for (uint n = length; n > 1u; --n) {
        uint selected = mc_next_int(n);
        uint tail = n - 1u;
        uint different = ((positions >> selected) ^ (positions >> tail)) & 1u;
        positions ^= (different << selected) | (different << tail);
    }
    return (positions & 3u) != 0u;
#endif
}

#if MC_BONES
bool mc_pair(uint64_t player_root, out uint first, out uint second) {
    rng_initialize(player_root + mc_hash(10u));
    uint64_t seed = next_u64();
    rng_initialize(seed);
    uint length = mc_value(25u), at = mc_value(24u);
    uint pool[64];
    for (uint i = 0u; i < length; ++i) pool[i] = mc_value(at + i);
    for (uint n = length; n > 1u; --n) {
        uint selected = mc_next_int(n);
        uint tail = n - 1u, old = pool[tail];
        pool[tail] = pool[selected];
        pool[selected] = old;
    }
    first = pool[0];
    second = pool[1];
    uint64_t offered = mc_bit(first) | mc_bit(second);
    uint64_t any = mc_hash(16u), required = mc_hash(18u), banned = mc_hash(20u);
    if ((any != uint64_t(0u) && (offered & any) == uint64_t(0u)) ||
        (offered & required) != required || (offered & banned) != uint64_t(0u)) return false;
    if (mc_value(3u) != MC_NONE && mc_value(4u) != MC_NONE) {
        first = mc_value(3u);
        second = mc_value(4u);
        return offered == (mc_bit(first) | mc_bit(second));
    }
    return true;
}
#endif
#endif

// Seed each visited raw bucket exactly once. Only drawn personal lanes
// instantiate the permutation. Their heads are reusable across both routes.
void mc_prepare_lanes(uint64_t root, out uint64_t lane_seeds[3]) {
#if MC_LOCAL_NEOW
    RngState parent = rng_initialize(root + mc_hash(14u));
#else
    rng_initialize(root + mc_hash(14u));
#endif
    for (uint bucket = 0u; bucket <= mc_value(6u); ++bucket) {
#if MC_LOCAL_NEOW
        uint64_t seed = next_u64(parent);
#else
        uint64_t seed = next_u64();
#endif
        for (uint lane = 0u; lane < 3u; ++lane)
            if (bucket == mc_value(37u + lane * 3u)) lane_seeds[lane] = seed;
    }
}

void mc_lane_head(uint lane, uint64_t seed, inout uint heads[9]) {
#if MC_LOCAL_NEOW
    RngState child = rng_initialize(seed);
#else
    rng_initialize(seed);
#endif
    uint at = 37u + lane * 3u;
    uint length = mc_value(at + 1u);
#if MC_TRACKED
    uint row = 47u + lane * 3u;
    uint offset = mc_value(row), count = mc_value(row + 1u);
    uint ineligible = mc_value(row + 2u);
    uint positions[4];
    for (uint i = 0u; i < count; ++i) positions[i] = mc_value(offset + i * 2u);
    for (uint draw = length; draw > 1u; --draw) {
        uint selected = MC_CHILD_INDEX(draw);
        uint tail = draw - 1u;
        if (ineligible == selected) ineligible = tail;
        else if (ineligible == tail) ineligible = selected;
        for (uint i = 0u; i < count; ++i) {
            if (positions[i] == selected) positions[i] = tail;
            else if (positions[i] == tail) positions[i] = selected;
        }
    }
    for (uint i = 0u; i < count; ++i) {
        uint effective = positions[i] - uint(ineligible < positions[i]);
        if (effective < mc_value(7u)) heads[lane * 3u + effective] = mc_value(offset + i * 2u + 1u);
    }
#else
    uint entries[MC_MAX_LANE];
#if MC_LOCAL_NEOW
    uint offset = mc_value(60u + lane);
    for (uint i = 0u; i < length; ++i) entries[i] = mc_value(offset + i);
#else
    uint offset = bucket_meta.values[mc_value(at)].x;
    for (uint i = 0u; i < length; ++i) entries[i] = pool_ids.values[offset + i];
#endif
    for (uint n = length; n > 1u; --n) {
        uint selected = MC_CHILD_INDEX(n);
        uint tail = n - 1u, old = entries[tail];
        entries[tail] = entries[selected];
        entries[selected] = old;
    }
    uint count = 0u;
    for (uint i = 0u; i < length && count < mc_value(7u); ++i)
        if (entries[i] != mc_value(5u)) heads[lane * 3u + count++] = entries[i];
#endif
}

bool mc_observed_contains(uint observed[3], uint draws, uint key) {
    for (uint index = 0u; index < draws; ++index)
        if (observed[index] == key) return true;
    return false;
}

bool mc_sources_present(uint first, uint second, uint draws) {
    uint64_t offered = mc_bit(first) | mc_bit(second);
    uint64_t any = mc_hash(16u), required = mc_hash(18u), banned = mc_hash(20u);
    if ((any != uint64_t(0u) && (offered & any) == uint64_t(0u)) ||
        (offered & required) != required || (offered & banned) != uint64_t(0u)) return false;
    for (uint i = 0u; i < mc_value(36u); ++i) {
        uint source = mc_value(mc_value(35u) + i * 3u);
        if (source == MC_NONE ? draws != 3u : source != first && source != second) return false;
    }
    return true;
}

uint mc_target_lane(uint key) {
    if (key == MC_CIRCLET) return 3u;
    return key < mc_value(57u) ? mc_value(mc_value(56u) + key) : 0xffffffffu;
}

bool mc_matches_targets(uint seen[3], uint draws, uint first, bool rarity) {
    bool any = mc_value(30u) == 0u;
    for (uint i = 0u; i < mc_value(30u); ++i) {
        uint key = mc_value(mc_value(29u) + i);
        any = any || mc_observed_contains(seen, draws, rarity ? mc_target_lane(key) : key);
    }
    if (!any) return false;
    for (uint i = 0u; i < mc_value(32u); ++i) {
        uint key = mc_value(mc_value(31u) + i);
        if (!mc_observed_contains(seen, draws, rarity ? mc_target_lane(key) : key)) return false;
    }
    if (!rarity)
        for (uint i = 0u; i < mc_value(34u); ++i)
            if (mc_observed_contains(seen, draws, mc_value(mc_value(33u) + i))) return false;
    for (uint i = 0u; i < mc_value(36u); ++i) {
        uint row = mc_value(35u) + i * 3u;
        uint source = mc_value(row);
        uint start = source == MC_NONE || source == first ? 0u : mc_slots(first);
        uint finish = source == first ? mc_slots(first) : draws;
        uint used = 0u;
        for (uint t = 0u; t < mc_value(row + 2u); ++t) {
            uint key = mc_value(mc_value(row + 1u) + t);
            if (rarity) key = mc_target_lane(key);
            uint index = start;
            while (index < finish && (seen[index] != key || (used & (1u << index)) != 0u)) ++index;
            if (index == finish) return false;
            used |= 1u << index;
        }
    }
    return true;
}

uint mc_choose_lane(float roll, inout uint consumed[3], out uint rank) {
    uint lane = roll < .5f ? 0u : roll < .83f ? 1u : 2u;
    while (lane < 3u && consumed[lane] >= mc_value(39u + lane * 3u)) ++lane;
    rank = lane < 3u ? consumed[lane]++ : 0u;
    return lane;
}

uint mc_draw_key(uint lane, uint rank, inout uint64_t lane_seeds[3],
    inout bool initialized[3], inout uint heads[9]) {
    if (lane == 3u) return MC_CIRCLET;
    if (!initialized[lane]) {
        mc_lane_head(lane, lane_seeds[lane], heads);
        initialized[lane] = true;
    }
    return heads[lane * 3u + rank];
}

#if MC_LOCAL_NEOW
void mc_advance_companion(uint burn, inout RngState rewards) {
#else
void mc_advance_companion(uint burn) {
#endif
    if (burn == 0xfffffffeu) {
        for (uint bundle = 0u; bundle < 2u; ++bundle)
            if (MC_COMPANION_INDEX(100u) != 0u)
                for (uint card = 0u; card < 3u; ++card) MC_COMPANION_WORD();
    } else for (uint i = 0u; i < burn; ++i) MC_COMPANION_WORD();
}

#if !MC_LOCAL_NEOW
bool mc_route(uint64_t root, uint64_t player_root, uint first, uint second,
    inout bool ready, inout uint64_t lane_seeds[3], inout bool initialized[3], inout uint heads[9]) {
#if MC_DIRECT_SINGLE
    // The plan fixes the route to Small Capsule and proves one draw.
    const uint draws = 1u;
#else
    uint first_count = mc_slots(first), draws = first_count + mc_slots(second);
#endif
    if (!mc_sources_present(first, second, draws)) return false;
#if MC_DIRECT_TARGET
    uint target = mc_value(mc_value(mc_value(30u) != 0u ? 29u : 31u));
    rng_initialize(player_root + mc_hash(12u));
    uint consumed[3] = uint[3](0u, 0u, 0u);
    uint rank;
    uint lane = mc_choose_lane(mc_next_float(), consumed, rank);
    if (lane != mc_target_lane(target)) return false;
    if (lane == 3u) return target == MC_CIRCLET;
    // A single draw observes only this bucket's child seed. The other raw
    // buckets still contribute one parent word each before the selected lane.
    rng_initialize(root + mc_hash(14u));
    uint bucket = mc_value(37u + lane * 3u);
    for (uint i = 0u; i < bucket; ++i) next_u64();
    uint64_t child_seed = next_u64();
    mc_lane_head(lane, child_seed, heads);
    return heads[lane * 3u + rank] == target;
#else
    uint observed[3] = uint[3](MC_CIRCLET, MC_CIRCLET, MC_CIRCLET);
#if !MC_DIRECT_SINGLE
    if (draws == 0u) return mc_matches_targets(observed, draws, first, false);
    uint burn = 0u;
    if (first_count == 0u) {
        if (first >= 30u) return true;
        burn = mc_value(mc_value(26u) + first);
        if (burn == 0xffffffffu) return true;
    }
    rng_initialize(player_root + mc_hash(12u));
    mc_advance_companion(burn);
#else
    rng_initialize(player_root + mc_hash(12u));
#endif
    uint consumed[3] = uint[3](0u, 0u, 0u);
    uint lanes[3], ranks[3];
    for (uint i = 0u; i < draws; ++i) lanes[i] = mc_choose_lane(mc_next_float(), consumed, ranks[i]);
#if MC_TRACKED
    // Only a complete draw-continuation proof enables this rejection.
    if (!mc_matches_targets(lanes, draws, first, true)) return false;
#endif
    if (!ready) {
        mc_prepare_lanes(root, lane_seeds);
        ready = true;
    }
    for (uint i = 0u; i < draws; ++i) {
        uint key = mc_draw_key(lanes[i], ranks[i], lane_seeds, initialized, heads);
        if (i + 1u < draws && mc_contains(mc_value(27u), mc_value(28u), key)) return true;
        observed[i] = key;
    }
    return mc_matches_targets(observed, draws, first, false);
#endif
}

bool mc_matches(uint64_t root) {
    uint64_t player_root = root + uint64_t(mc_value(0u));
    uint selected = mc_value(2u);
    uint first = selected, second = MC_NONE;
#if MC_BONES
    if (!mc_top_contains(player_root, 5u)) return false;
    if (selected != MC_NONE && selected != 5u && !mc_top_contains(player_root, selected)) return false;
    if (!mc_pair(player_root, first, second)) return false;
#else
    if (!mc_top_contains(player_root, selected)) return false;
#endif
    uint64_t lane_seeds[3] = uint64_t[3](uint64_t(0u), uint64_t(0u), uint64_t(0u));
    bool initialized[3] = bool[3](false, false, false);
    uint heads[9] = uint[9](MC_UNTRACKED, MC_UNTRACKED, MC_UNTRACKED,
        MC_UNTRACKED, MC_UNTRACKED, MC_UNTRACKED, MC_UNTRACKED, MC_UNTRACKED, MC_UNTRACKED);
    bool ready = false;
    bool forward = mc_route(root, player_root, first, second, ready, lane_seeds, initialized, heads);
#if MC_BONES
    return forward || (mc_value(3u) == MC_NONE &&
        mc_route(root, player_root, second, first, ready, lane_seeds, initialized, heads));
#else
    return forward;
#endif
}
#endif
