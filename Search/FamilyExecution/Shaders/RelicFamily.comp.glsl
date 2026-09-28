#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

const uint LANE_COUNT = 4u;
const uint PREDICATE_STRIDE = 9u;
const uint SHOP_PREDICATE_STRIDE = 4u;
const uint INVALID_ID = 65535u;
const uint SEEDS_PER_INVOCATION = 8u;
const uint CANDIDATES_PER_WORKGROUP = 64u * SEEDS_PER_INVOCATION;
const int MAX_LOCAL_STATE = 64;

// batch: base low/high, batch count, physical input count, dense/compact mode, output capacity
layout(set = 0, binding = 0, std430) readonly restrict buffer BatchMeta { uint values[]; } batch_meta;
layout(set = 0, binding = 1, std430) readonly restrict buffer InputOrdinals { uint values[]; } input_ordinals;
layout(set = 0, binding = 2, std430) readonly restrict buffer PoolIds { uint values[]; } pool_ids;
layout(set = 0, binding = 3, std430) readonly restrict buffer EntryFlags { uint values[]; } entry_flags;
layout(set = 0, binding = 4, std430) readonly restrict buffer BucketMeta { uvec4 values[]; } bucket_meta;
// bucket count, last required bucket, always reject, predicate count, shop predicate count,
// output capacity, local-state capacity, seeds/invocation, positive depths[4], exclusion depths[4],
// tracked-position offsets[4], tracked-position counts[4]
layout(set = 0, binding = 5, std430) readonly restrict buffer PlanMeta { uint values[]; } plan_meta;
layout(set = 0, binding = 6, std430) readonly restrict buffer Predicates { uint values[]; } predicates;
layout(set = 0, binding = 7, std430) readonly restrict buffer PredicateTargetIndexes { uint values[]; } predicate_targets;
layout(set = 0, binding = 8, std430) readonly restrict buffer TrackedInitialPositions { uint values[]; } tracked_initial_positions;
// magic, ABI, survivors, overflow, processed, plan tag, reserved, reserved
layout(set = 0, binding = 9, std430) restrict buffer OutputHeader { uint values[]; } output_header;
layout(set = 0, binding = 10, std430) restrict buffer OutputOrdinals { uint values[]; } output_ordinals;
layout(set = 0, binding = 11, std430) readonly restrict buffer ShopPredicates { uint values[]; } shop_predicates;
layout(set = 0, binding = 12, std430) readonly restrict buffer ShopTargets { uint values[]; } shop_targets;
/*__RT2_RFULL_BINDING__*/

uint64_t s0;
uint64_t s1;
uint64_t s2;
uint64_t s3;

uint64_t make_u64(uint low, uint high) { return uint64_t(low) | (uint64_t(high) << 32u); }
uint64_t rotl64(uint64_t value, uint amount) { return (value << amount) | (value >> (64u - amount)); }
uint64_t splitmix_next(inout uint64_t state) {
    state += make_u64(0x7f4a7c15u, 0x9e3779b9u);
    uint64_t value = state;
    value = (value ^ (value >> 30u)) * make_u64(0x1ce4e5b9u, 0xbf58476du);
    value = (value ^ (value >> 27u)) * make_u64(0x133111ebu, 0x94d049bbu);
    return value ^ (value >> 31u);
}
void rng_initialize(uint64_t seed) {
    uint64_t state = seed;
    s0 = splitmix_next(state); s1 = splitmix_next(state); s2 = splitmix_next(state); s3 = splitmix_next(state);
}
uint64_t next_u64() {
    uint64_t result = rotl64(s1 * uint64_t(5u), 7u) * uint64_t(9u);
    uint64_t temporary = s1 << 17u;
    s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= temporary; s3 = rotl64(s3, 45u);
    return result;
}
uint next_int(uint bound) {
    // Owner-approved Family Fast primitive: floor((NextUInt64 >> 11) * bound / 2^53).
    // Production Exact remains responsible for the game's binary64 round-to-nearest behavior.
    uint64_t mantissa = next_u64() >> 11u;
    uint mantissa_low = uint(mantissa);
    uint mantissa_high = uint(mantissa >> 32u);
    uint64_t low_product = uint64_t(mantissa_low) * uint64_t(bound);
    uint64_t upper_limb = uint64_t(mantissa_high) * uint64_t(bound) + (low_product >> 32u);
    return uint(upper_limb >> 21u);
}
void consume_shuffle(uint length) { for (uint count = length; count > 1u; --count) next_int(count); }

/*__RT2_RFULL_FUNCTIONS__*/

/*__RT2_VISIBLE_SEED_ROOT_HASH__*/

uint lane_from_kind(uint kind) { return kind >= 1u && kind <= 4u ? kind - 1u : 0xffffffffu; }
bool position_matches(uint position, uint range_mode, uint range_value) {
    return range_mode == 0u ? position < range_value : position + 1u == range_value;
}
bool evaluate_tracked_lane(uint lane, uint state[MAX_LOCAL_STATE]) {
    for (uint predicate_index = 0u; predicate_index < plan_meta.values[3u]; ++predicate_index) {
        uint b = predicate_index * PREDICATE_STRIDE;
        if (predicates.values[b] != lane) continue;
        uint range_mode = predicates.values[b + 1u];
        uint range_value = predicates.values[b + 2u];
        uint any_offset = predicates.values[b + 3u]; uint any_count = predicates.values[b + 4u];
        uint all_offset = predicates.values[b + 5u]; uint all_count = predicates.values[b + 6u];
        uint ban_offset = predicates.values[b + 7u]; uint ban_count = predicates.values[b + 8u];
        bool found_any = any_count == 0u;
        for (uint index = 0u; index < any_count; ++index)
            if (position_matches(state[predicate_targets.values[any_offset + index]], range_mode, range_value))
                found_any = true;
        if (!found_any) return false;
        for (uint index = 0u; index < all_count; ++index)
            if (!position_matches(state[predicate_targets.values[all_offset + index]], range_mode, range_value))
                return false;
        for (uint index = 0u; index < ban_count; ++index)
            if (position_matches(state[predicate_targets.values[ban_offset + index]], range_mode, range_value))
                return false;
    }
    return true;
}
uint shop_draw_id(uint state[MAX_LOCAL_STATE], uint remaining, uint length, uint draw_index) {
    uint observed = 0u;
    for (uint cursor = length; cursor > remaining; --cursor) {
        uint entry_index = state[cursor - 1u];
        if ((entry_flags.values[entry_index] & 1u) == 0u) continue;
        if (observed == draw_index) return pool_ids.values[entry_index];
        observed++;
    }
    return INVALID_ID;
}
bool shop_contains(uint state[MAX_LOCAL_STATE], uint remaining, uint length, uint range_mode, uint range_value, uint target) {
    if (range_mode == 1u) return shop_draw_id(state, remaining, length, range_value - 1u) == target;
    for (uint index = 0u; index < range_value; ++index)
        if (shop_draw_id(state, remaining, length, index) == target) return true;
    return false;
}
bool evaluate_shop_lane(uint state[MAX_LOCAL_STATE], uint remaining, uint length) {
    for (uint predicate_index = 0u; predicate_index < plan_meta.values[3u]; ++predicate_index) {
        uint b = predicate_index * PREDICATE_STRIDE;
        if (predicates.values[b] != 3u) continue;
        uint range_mode = predicates.values[b + 1u];
        uint range_value = predicates.values[b + 2u];
        uint any_offset = predicates.values[b + 3u]; uint any_count = predicates.values[b + 4u];
        uint all_offset = predicates.values[b + 5u]; uint all_count = predicates.values[b + 6u];
        uint ban_offset = predicates.values[b + 7u]; uint ban_count = predicates.values[b + 8u];
        bool found_any = any_count == 0u;
        for (uint index = 0u; index < any_count; ++index)
            if (shop_contains(state, remaining, length, range_mode, range_value, predicate_targets.values[any_offset + index]))
                found_any = true;
        if (!found_any) return false;
        for (uint index = 0u; index < all_count; ++index)
            if (!shop_contains(state, remaining, length, range_mode, range_value, predicate_targets.values[all_offset + index]))
                return false;
        for (uint index = 0u; index < ban_count; ++index)
            if (shop_contains(state, remaining, length, range_mode, range_value, predicate_targets.values[ban_offset + index]))
                return false;
    }
    return true;
}
bool evaluate_shop_sequences(uint state[MAX_LOCAL_STATE], uint remaining, uint length) {
    for (uint predicate_index = 0u; predicate_index < plan_meta.values[4u]; ++predicate_index) {
        uint b = predicate_index * SHOP_PREDICATE_STRIDE;
        uint count = shop_predicates.values[b];
        uint mode = shop_predicates.values[b + 1u];
        uint target_offset = shop_predicates.values[b + 2u];
        uint target_count = shop_predicates.values[b + 3u];
        if (mode == 0u) {
            for (uint index = 0u; index < target_count; ++index) {
                uint target = shop_targets.values[target_offset + index];
                if (target == INVALID_ID) continue;
                if (shop_draw_id(state, remaining, length, index) != target) return false;
            }
        } else {
            for (uint index = 0u; index < target_count; ++index) {
                uint target = shop_targets.values[target_offset + index];
                if (target == INVALID_ID) continue;
                uint requested = 0u;
                uint actual = 0u;
                for (uint probe = 0u; probe < target_count; ++probe)
                    if (shop_targets.values[target_offset + probe] == target) requested++;
                for (uint probe = 0u; probe < count; ++probe)
                    if (shop_draw_id(state, remaining, length, probe) == target) actual++;
                if (actual < requested) return false;
            }
        }
    }
    return true;
}
void append_output(uint logical_ordinal) {
    uint slot = atomicAdd(output_header.values[2u], 1u);
    if (slot >= plan_meta.values[5u]) {
        atomicExchange(output_header.values[3u], 1u);
        atomicAdd(output_header.values[6u], 1u);
        return;
    }
    output_ordinals.values[slot] = logical_ordinal;
}

void process_candidate_with_root(uint logical, uint64_t root) {
    /*__RT2_RFULL_SEQUENCE_BEGIN__*/
    rng_initialize(root + make_u64(0xda243e80u, 0x1a8a7b60u));

    uint state[MAX_LOCAL_STATE];
    for (uint bucket_index = 0u; bucket_index < plan_meta.values[0u]; ++bucket_index) {
        uvec4 meta = bucket_meta.values[bucket_index];
        uint offset = meta.x;
        uint length = meta.y;
        uint packed = meta.z;
        uint scope = packed & 0xffu;
        uint kind = (packed >> 8u) & 0xffu;
        if (scope == 0u) { consume_shuffle(length); continue; }
        if (bucket_index > plan_meta.values[1u]) break;
        uint lane = lane_from_kind(kind);
        uint positive_depth = lane == 0xffffffffu ? 0u : plan_meta.values[8u + lane];
        uint exclusion_depth = lane == 0xffffffffu ? 0u : plan_meta.values[12u + lane];
        uint target_depth = max(positive_depth, exclusion_depth);
        if (lane == 0xffffffffu || target_depth == 0u) { consume_shuffle(length); continue; }

        if (lane == 3u) {
            for (uint index = 0u; index < length; ++index) state[index] = offset + index;
            uint remaining = length;
            uint observed = 0u;
            while (remaining > 1u && observed < target_depth) {
                uint selected = next_int(remaining);
                uint tail = remaining - 1u;
                uint temporary = state[selected];
                state[selected] = state[tail];
                state[tail] = temporary;
                remaining--;
                uint entry_index = state[tail];
                if ((entry_flags.values[entry_index] & 1u) == 0u) continue;
                observed++;
            }
            if (remaining == 1u && observed < target_depth) {
                uint entry_index = state[0];
                if ((entry_flags.values[entry_index] & 1u) != 0u) {
                    observed++;
                    remaining = 0u;
                }
            }
            if (observed < target_depth) return;
            if (!evaluate_shop_lane(state, remaining, length)) return;
            if (!evaluate_shop_sequences(state, remaining, length)) return;
            if (bucket_index < plan_meta.values[1u]) consume_shuffle(remaining);
        } else {
            uint tracked_offset = plan_meta.values[16u + lane];
            uint tracked_count = plan_meta.values[20u + lane];
            for (uint index = 0u; index < tracked_count; ++index)
                state[index] = tracked_initial_positions.values[tracked_offset + index];
            for (uint count = length; count > 1u; --count) {
                uint selected = next_int(count);
                uint tail = count - 1u;
                for (uint index = 0u; index < tracked_count; ++index) {
                    uint position = state[index];
                    if (position == selected) state[index] = tail;
                    else if (position == tail) state[index] = selected;
                }
            }
            if (!evaluate_tracked_lane(lane, state)) return;
        }
    }
    /*__RT2_RFULL_SEQUENCE_END__*/
    /*__RT2_RFULL_FILTER__*/
    append_output(logical);
}

void process_candidate(uint invocation) {
    uint input_count = batch_meta.values[3u];
    if (invocation >= input_count) return;
    if (plan_meta.values[2u] != 0u) return;

    uint logical = batch_meta.values[4u] == 0u ? invocation : input_ordinals.values[invocation];
    if (logical >= batch_meta.values[2u]) return;
    uint64_t global_ordinal = make_u64(batch_meta.values[0u], batch_meta.values[1u]) + uint64_t(logical);
    process_candidate_with_root(logical, root_hash_for_ordinal(global_ordinal));
}

void record_processed_workgroup() {
    if (gl_LocalInvocationIndex != 0u) return;
    uint first_input = gl_WorkGroupID.x * CANDIDATES_PER_WORKGROUP;
    uint input_count = batch_meta.values[3u];
    if (first_input >= input_count) return;
    uint admitted = min(CANDIDATES_PER_WORKGROUP, input_count - first_input);
    atomicAdd(output_header.values[4u], admitted);
}

void main() {
    record_processed_workgroup();
    uint first_input = gl_GlobalInvocationID.x * SEEDS_PER_INVOCATION;
    if (first_input >= batch_meta.values[3u]) return;
    if (batch_meta.values[4u] == 0u) {
        if (plan_meta.values[2u] != 0u) return;
        uint64_t global_ordinal = make_u64(batch_meta.values[0u], batch_meta.values[1u]) + uint64_t(first_input);
        uint64_t seed_first8;
        uint seed_tail4;
        encode_seed12_packed(global_ordinal, seed_first8, seed_tail4);
        for (uint offset = 0u; offset < SEEDS_PER_INVOCATION; ++offset) {
            uint input_index = first_input + offset;
            if (input_index >= batch_meta.values[3u] || input_index >= batch_meta.values[2u]) break;
            process_candidate_with_root(input_index, root_hash_for_seed12_packed(seed_first8, seed_tail4));
            if (offset + 1u < SEEDS_PER_INVOCATION) advance_seed12_packed(seed_first8, seed_tail4);
        }
        return;
    }
    for (uint offset = 0u; offset < SEEDS_PER_INVOCATION; ++offset) {
        uint input_index = first_input + offset;
        if (input_index >= batch_meta.values[3u]) break;
        process_candidate(input_index);
    }
}
