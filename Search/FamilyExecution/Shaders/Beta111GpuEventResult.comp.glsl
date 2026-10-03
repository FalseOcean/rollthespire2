#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

const uint INVALID_ID = 65535u;
const uint SEED_LENGTH = 12u;
const uint RADIX = 34u;

#ifdef RT2_EVENT_RESULT_ROOT_RANGE_INPUT
// [0] batchStartLow, [1] batchStartHigh, [2] batchCount.
layout(set = 0, binding = 0, std430) readonly restrict buffer RootBatchMetaBuffer { uint values[]; } root_batch_meta;
#else
layout(set = 0, binding = 0, std430) readonly restrict buffer UpstreamHeaderBuffer { uint values[]; } upstream_header;
#endif
// ABI1: uint64 LogicalOrdinal only. RootRange binds a valid unused dummy buffer.
layout(set = 0, binding = 1, std430) readonly restrict buffer CompactInputBuffer { uint values[]; } compact_input;
// [0] invocation capacity, [1] upstream candidate-count index, [2] output capacity.
layout(set = 0, binding = 2, std430) readonly restrict buffer StageMetaBuffer { uint values[]; } stage_meta;
layout(set = 0, binding = 3, std430) readonly restrict buffer EventPlanBuffer { uint values[]; } event_plan_buffer;
layout(set = 0, binding = 4, std430) readonly restrict buffer ColorfulPoolBuffer { uint ids[]; } colorful_pool_buffer;
// [0] magic, [1] ABI, [2] session tag, [3] plan tag,
// [4] candidate count, [5] overflow, [6] dropped, [7] processed.
layout(set = 0, binding = 5, std430) restrict buffer OutputHeaderBuffer { uint values[]; } output_header;
layout(set = 0, binding = 6, std430) restrict buffer CompactOutputBuffer { uint values[]; } compact_output;

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
    const double unit = 1.1102230246251565e-16lf;
    double unit_sample = double(next_u64() >> 11u) * unit;
    return uint(unit_sample * double(bound));
}

uint64_t prime1() { return make_u64(0x85ebca87u, 0x9e3779b1u); }
uint64_t prime2() { return make_u64(0x27d4eb4fu, 0xc2b2ae3du); }
uint64_t prime3() { return make_u64(0x9e3779f9u, 0x165667b1u); }
uint64_t prime4() { return make_u64(0xc2b2ae63u, 0x85ebca77u); }
uint64_t prime5() { return make_u64(0x165667c5u, 0x27d4eb2fu); }
uint alphabet_byte(uint digit) {
    if (digit < 10u) return 48u + digit;
    if (digit < 18u) return 65u + (digit - 10u);
    if (digit < 23u) return 74u + (digit - 18u);
    return 80u + (digit - 23u);
}
void encode_seed(uint64_t ordinal, out uint seed_bytes[12]) {
    for (int index = 11; index >= 0; --index) {
        uint digit = uint(ordinal % uint64_t(RADIX));
        seed_bytes[index] = alphabet_byte(digit);
        ordinal /= uint64_t(RADIX);
    }
}
uint64_t read_u64_le(uint seed_bytes[12], uint offset) {
    uint64_t value = uint64_t(0u);
    for (uint index = 0u; index < 8u; ++index) value |= uint64_t(seed_bytes[offset + index]) << (index * 8u);
    return value;
}
uint read_u32_le(uint seed_bytes[12], uint offset) {
    return seed_bytes[offset] | (seed_bytes[offset + 1u] << 8u) |
           (seed_bytes[offset + 2u] << 16u) | (seed_bytes[offset + 3u] << 24u);
}
uint64_t xxh_round(uint64_t accumulator, uint64_t input_value) {
    accumulator += input_value * prime2();
    accumulator = rotl64(accumulator, 31u);
    return accumulator * prime1();
}
uint64_t root_hash_for_ordinal(uint64_t ordinal) {
    uint seed_bytes[12];
    encode_seed(ordinal, seed_bytes);
    uint64_t hash = prime5() + uint64_t(SEED_LENGTH);
    hash ^= xxh_round(uint64_t(0u), read_u64_le(seed_bytes, 0u));
    hash = rotl64(hash, 27u) * prime1() + prime4();
    hash ^= uint64_t(read_u32_le(seed_bytes, 8u)) * prime1();
    hash = rotl64(hash, 23u) * prime2() + prime3();
    hash ^= hash >> 33u; hash *= prime2(); hash ^= hash >> 29u; hash *= prime3(); hash ^= hash >> 32u;
    return hash;
}

bool evaluate_event_results(uint64_t root) {
    uint condition_count = event_plan_buffer.values[0u];
    if (condition_count == 0u) return true;
    uint player_slot = event_plan_buffer.values[2u];
    uint64_t trash_hash = make_u64(event_plan_buffer.values[3u], event_plan_buffer.values[4u]);
    uint64_t fake_hash = make_u64(event_plan_buffer.values[5u], event_plan_buffer.values[6u]);
    uint64_t colorful_hash = make_u64(event_plan_buffer.values[7u], event_plan_buffer.values[8u]);
    int grab = -1;
    int dive = -1;
    uint fake_order[9];
    uint colorful_order[5];
    uint colorful_count = event_plan_buffer.values[1u];
    for (uint index = 0u; index < 9u; ++index) fake_order[index] = index;
    bool need_grab = false;
    bool need_dive = false;
    bool need_fake = false;
    bool need_colorful = false;
    for (uint index = 0u; index < condition_count; ++index) {
        uint kind = event_plan_buffer.values[9u + index * 2u];
        need_grab = need_grab || kind == 0u;
        need_dive = need_dive || kind == 1u;
        need_fake = need_fake || kind == 2u;
        need_colorful = need_colorful || kind == 3u;
    }
    if (need_grab || need_dive) {
        rng_initialize(root + uint64_t(player_slot) + trash_hash);
        uint grab_index = next_int(10u);
        grab = int(grab_index);
        dive = int(grab_index / 2u);
    }
    if (need_fake) {
        rng_initialize(root + fake_hash);
        for (uint count = 9u; count > 1u; --count) {
            uint selected = next_int(count);
            uint tail = count - 1u;
            uint temporary = fake_order[selected];
            fake_order[selected] = fake_order[tail];
            fake_order[tail] = temporary;
        }
    }
    if (need_colorful) {
        rng_initialize(root + uint64_t(player_slot) + colorful_hash);
        for (uint index = 0u; index < colorful_count; ++index)
            colorful_order[index] = colorful_pool_buffer.ids[index];
        uint target_count = min(3u, colorful_count);
        while (colorful_count > target_count) {
            uint remove = next_int(colorful_count);
            for (uint index = remove; index + 1u < colorful_count; ++index)
                colorful_order[index] = colorful_order[index + 1u];
            colorful_count--;
        }
    }
    for (uint index = 0u; index < condition_count; ++index) {
        uint kind = event_plan_buffer.values[9u + index * 2u];
        uint target = event_plan_buffer.values[10u + index * 2u];
        if (kind == 7u || kind == 8u) {
            rng_initialize(root + uint64_t(player_slot) + make_u64(event_plan_buffer.values[target], event_plan_buffer.values[target + 1u]));
            uint value = event_plan_buffer.values[target + 2u];
            if (kind == 7u) { if (next_int(3u) != value) return false; continue; }
            uint types[3]; for (uint j = 0u; j < 3u; j++) types[j] = j;
            for (uint j = 2u; j > 0u; j--) { uint k = next_int(j + 1u); uint tmp = types[j]; types[j] = types[k]; types[k] = tmp; }
            if (types[0] != value / 4u && types[1] != value / 4u) return false;
            if (value % 4u != 0u) {
                uint riders[3]; for (uint j = 0u; j < 3u; j++) riders[j] = j;
                for (uint j = 2u; j > 0u; j--) { uint k = next_int(j + 1u); uint tmp = riders[j]; riders[j] = riders[k]; riders[k] = tmp; }
                if (riders[0] != value % 4u - 1u && riders[1] != value % 4u - 1u) return false;
            }
            continue;
        }
        if (kind == 4u || kind == 5u || kind == 6u) {
            uint64_t event_hash = make_u64(event_plan_buffer.values[target], event_plan_buffer.values[target + 1u]);
            uint count = event_plan_buffer.values[target + 2u];
            rng_initialize(root + event_hash);
            uint prefix = event_plan_buffer.values[target + 3u];
            if (prefix == 1u) next_int(19u);
            if (prefix == 2u && next_int(3u) != 2u) return false;
            uint first = next_int(count);
            if (kind == 6u) { if (event_plan_buffer.values[target + 4u + first] == 0u) return false; continue; }
            if (kind == 5u) {
                uint a = event_plan_buffer.values[target + 4u + first];
                if (a == 0u) return false;
                uint second = next_int(count);
                uint b = event_plan_buffer.values[target + 4u + second];
                if (!(((a & 1u) != 0u && (b & 2u) != 0u) || ((a & 2u) != 0u && (b & 1u) != 0u))) return false;
                continue;
            }
            bool found = event_plan_buffer.values[target + 4u + first] != 0u;
            if (!found) {
                uint second = next_int(count);
                found = event_plan_buffer.values[target + 4u + second] != 0u;
            }
            if (!found) return false;
            continue;
        }
        if (target == INVALID_ID) return false;
        if (kind == 0u && uint(grab) != target) return false;
        if (kind == 1u && uint(dive) != target) return false;
        if (kind == 2u) {
            bool found = false;
            for (uint probe = 0u; probe < 6u; ++probe) found = found || fake_order[probe] == target;
            if (!found) return false;
        }
        if (kind == 3u) {
            bool found = false;
            for (uint probe = 0u; probe < colorful_count; ++probe) found = found || colorful_order[probe] == target;
            if (!found) return false;
        }
    }
    return true;
}

void append_output(uint64_t ordinal) {
    uint slot = atomicAdd(output_header.values[4u], 1u);
    if (slot >= stage_meta.values[2u]) {
        atomicExchange(output_header.values[5u], 1u);
        atomicAdd(output_header.values[6u], 1u);
        return;
    }
    uint base = slot * 2u;
    compact_output.values[base] = uint(ordinal);
    compact_output.values[base + 1u] = uint(ordinal >> 32u);
}

void main() {
    uint invocation = gl_GlobalInvocationID.x;
    uint invocation_capacity = stage_meta.values[0u];
    if (invocation >= invocation_capacity) return;
#ifdef RT2_EVENT_RESULT_ROOT_RANGE_INPUT
    uint actual_count = min(root_batch_meta.values[2u], invocation_capacity);
#else
    uint actual_count = min(upstream_header.values[stage_meta.values[1u]], invocation_capacity);
#endif
    if (invocation >= actual_count) return;
    atomicAdd(output_header.values[7u], 1u);
#ifdef RT2_EVENT_RESULT_ROOT_RANGE_INPUT
    uint64_t ordinal = make_u64(root_batch_meta.values[0u], root_batch_meta.values[1u]) + uint64_t(invocation);
#else
    uint base = invocation * 2u;
    uint64_t ordinal = uint64_t(compact_input.values[base]) |
                       (uint64_t(compact_input.values[base + 1u]) << 32u);
#endif
    uint64_t root_hash = root_hash_for_ordinal(ordinal);
    if (evaluate_event_results(root_hash)) append_output(ordinal);
}
