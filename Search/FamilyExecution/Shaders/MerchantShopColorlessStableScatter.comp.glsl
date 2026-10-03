#version 450

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

const uint SEEDS_PER_INVOCATION = 8u;
const uint CANDIDATES_PER_WORKGROUP = 64u * SEEDS_PER_INVOCATION;

// Same batch metadata used by S Phase 1. Stable scatter is Dense-only.
layout(set = 0, binding = 0, std430) readonly restrict buffer BatchMetaBuffer { uint values[]; } batch_meta;
layout(set = 0, binding = 1, std430) readonly restrict buffer KeepMaskBuffer { uint values[]; } keep_masks;
layout(set = 0, binding = 2, std430) readonly restrict buffer BlockOffsetBuffer { uint values[]; } block_offsets;
// [2] survivor count and [3] overflow were established/cleared by Phase 1.
layout(set = 0, binding = 3, std430) restrict buffer OutputHeaderBuffer { uint values[]; } output_header;
layout(set = 0, binding = 4, std430) restrict buffer CompactOutputBuffer { uint values[]; } compact_output;

shared uint invocation_counts[64];

void main() {
    uint invocation = gl_GlobalInvocationID.x;
    uint first_input = invocation * SEEDS_PER_INVOCATION;
    uint mask = first_input < batch_meta.values[3u] ? keep_masks.values[invocation] : 0u;
    invocation_counts[gl_LocalInvocationIndex] = uint(bitCount(mask));
    barrier();

    uint local_base = 0u;
    for (uint preceding = 0u; preceding < gl_LocalInvocationIndex; ++preceding)
        local_base += invocation_counts[preceding];

    uint local_rank = local_base;
    uint block_base = block_offsets.values[gl_WorkGroupID.x];
    for (uint bit = 0u; bit < SEEDS_PER_INVOCATION; ++bit) {
        if ((mask & (1u << bit)) == 0u) continue;
        uint logical_ordinal = first_input + bit;
        uint slot = block_base + local_rank;
        if (logical_ordinal >= batch_meta.values[2u] || slot >= batch_meta.values[5u]) {
            atomicExchange(output_header.values[3u], 1u);
            continue;
        }
        compact_output.values[slot * 2u] = logical_ordinal;
        compact_output.values[slot * 2u + 1u] = 0u;
        local_rank++;
    }
}
