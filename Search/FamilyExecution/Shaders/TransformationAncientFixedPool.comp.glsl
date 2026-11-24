#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require
/*__SCOPED_COUNT__*/
#define COUNTER_COUNT /*__COUNTERS__*/
layout(local_size_x = 64) in;
layout(set=0,binding=0,std430) readonly restrict buffer Batch { uint v[]; } batch;
layout(set=0,binding=1,std430) readonly restrict buffer Plan { uint v[]; } plan;
layout(set=0,binding=2,std430) readonly restrict buffer Pool { uint v[]; } pool;
layout(set=0,binding=3,std430) readonly restrict buffer Input { uint v[]; } input_ids;
layout(set=0,binding=4,std430) restrict buffer Header { uint v[]; } header;
layout(set=0,binding=5,std430) writeonly restrict buffer Output { uint v[]; } output_ids;
layout(set=0,binding=6,std430) readonly restrict buffer Prefix { uint v[]; } prefix_table;

/*__RNG__*/
uint prefix_hash_cache_value(uint index) { return prefix_table.v[index]; }
/*__PREFIX__*/

#ifdef SCOPED_COUNT
bool matches(uint64_t root) {
    uint missing = plan.v[0], remaining = plan.v[1], cursor = 3u;
    for (uint stream = 0u; stream < plan.v[2]; stream++) {
        rng_initialize(root + make_u64(plan.v[cursor], plan.v[cursor + 1u]));
        uint blocks = plan.v[cursor + 2u];
        cursor += 3u;
#ifdef EVENT_PREFIX
        uint range = plan.v[cursor], requiredPlusOne = plan.v[cursor + 1u];
        cursor += 2u;
        if (range != 0u) {
            uint roll = next_int(range);
            if (requiredPlusOne != 0u && roll + 1u != requiredPlusOne) return false;
        }
#endif
        for (uint block = 0u; block < blocks; block++) {
            uint start = plan.v[cursor], size = plan.v[cursor + 1u];
            uint draws = plan.v[cursor + 2u], counted = plan.v[cursor + 3u];
            cursor += 4u;
            for (uint draw = 0u; draw < draws; draw++) {
                if (missing > remaining) return false;
                uint index = next_int(size);
                missing -= counted * pool.v[start + index];
                remaining -= counted;
                if (missing == 0u) return true;
            }
        }
    }
    return false;
}
#else
bool matches(uint64_t root) {
    uint draws = plan.v[2];
    if (draws == 0u) return false;
    rng_initialize(root + make_u64(plan.v[0], plan.v[1]));
    uint missing = plan.v[4];
    int rare = int(plan.v[5]);
#if COUNTER_COUNT > 0
    uint seen[COUNTER_COUNT];
    for (uint j = 0u; j < COUNTER_COUNT; j++) seen[j] = 0u;
#endif
    for (uint left = draws; left != 0u; left--) {
        if (missing > left || rare > int(left)) return false;
        if (missing == 0u && rare <= 0 && plan.v[6] == 0u) return true;
        uint rule = pool.v[next_int(plan.v[3])];
        if ((rule & 0x40000000u) != 0u) return false;
        if ((rule & 0x80000000u) != 0u) rare--;
#if COUNTER_COUNT > 0
        uint id = rule & 0x3fffffffu;
        if (id != 0u) {
            uint counter = id - 1u;
            uint count = ++seen[counter];
            if (count > plan.v[9u + 2u * counter]) return false;
            if (count <= plan.v[8u + 2u * counter]) missing--;
        }
#endif
    }
    return missing == 0u && rare <= 0;
}

#endif

void main() {
    uint i = gl_GlobalInvocationID.x;
    if (i >= batch.v[3]) return;
    if (gl_LocalInvocationIndex == 0u) atomicAdd(header.v[4], min(64u, batch.v[3] - i));
    uint ordinal = batch.v[4] == 0u ? batch.v[6] + i : input_ids.v[i];
    if (ordinal >= batch.v[2]) { atomicOr(header.v[3], 1u); return; }
    uint64_t root;
    if (batch.v[7] != 0u) {
        uint row, suffix, tail;
        uint64_t prefix;
        if (!prefix_hash_cache_initialize(ordinal, row, suffix, tail, prefix)) {
            atomicOr(header.v[3], 4u);
            return;
        }
        uint64_t hash = prefix ^ (uint64_t(tail) * prime1());
        hash = rotl64(hash, 23u) * prime2() + prime3();
        hash ^= hash >> 33u; hash *= prime2(); hash ^= hash >> 29u; hash *= prime3();
        root = hash ^ (hash >> 32u);
    } else {
        root = root_hash_for_ordinal(make_u64(batch.v[0], batch.v[1]) + uint64_t(ordinal));
    }
    if (matches(root)) {
        uint slot = atomicAdd(header.v[2], 1u);
        if (slot >= batch.v[5]) { atomicOr(header.v[3], 2u); return; }
        output_ids.v[slot] = ordinal;
    }
}
