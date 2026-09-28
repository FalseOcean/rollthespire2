#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

const uint INVALID_ID = 0xffffffffu;
const uint ACT_META_STRIDE = 24u;
const uint ENCOUNTER_STRIDE = 4u;
const uint PREDICATE_STRIDE = 7u;
const uint SHARED_STATE_STRIDE = 8u;

layout(set = 0, binding = 0, std430) readonly restrict buffer RootBuffer {
    uint64_t roots[];
} root_buffer;
layout(set = 0, binding = 1, std430) readonly restrict buffer RelicBucketLengthBuffer {
    uint lengths[];
} relic_bucket_length_buffer;
// rootCount, relicBucketCount, selectionGroupCount, actIndexMapLength,
// actCount, sharedAncientCount, maxRequiredAct, requiresSecondBoss,
// maxEncounterPoolCount, predicateCount, alwaysReject, act1OverrideId,
// ascension, shaderAbi, planAbi, inputMode, parityCount.
layout(set = 0, binding = 2, std430) readonly restrict buffer PlanMetaBuffer {
    uint values[];
} plan_meta_buffer;
layout(set = 0, binding = 3, std430) readonly restrict buffer SelectionGroupMetaBuffer {
    uvec4 groups[];
} selection_group_meta_buffer;
layout(set = 0, binding = 4, std430) readonly restrict buffer SelectionIdBuffer {
    uint ids[];
} selection_id_buffer;
layout(set = 0, binding = 5, std430) readonly restrict buffer ActIndexMapBuffer {
    uint indices[];
} act_index_map_buffer;
layout(set = 0, binding = 6, std430) readonly restrict buffer ActMetaBuffer {
    uint values[];
} act_meta_buffer;
layout(set = 0, binding = 7, std430) readonly restrict buffer EncounterBuffer {
    uint values[];
} encounter_buffer;
layout(set = 0, binding = 8, std430) readonly restrict buffer ConflictBuffer {
    uint ordinals[];
} conflict_buffer;
layout(set = 0, binding = 9, std430) readonly restrict buffer BossIdBuffer {
    uint ids[];
} boss_id_buffer;
layout(set = 0, binding = 10, std430) readonly restrict buffer PredicateBuffer {
    uint values[];
} predicate_buffer;
layout(set = 0, binding = 11, std430) readonly restrict buffer PredicateIdBuffer {
    uint ids[];
} predicate_id_buffer;
layout(set = 0, binding = 12, std430) restrict buffer EncounterScratchBuffer {
    uint entries[];
} encounter_scratch_buffer;
// First parity window only.
layout(set = 0, binding = 13, std430) restrict buffer SharedUpFrontStateBuffer {
    uint values[];
} shared_up_front_state_buffer;
layout(set = 0, binding = 14, std430) restrict buffer FinalPassFlagBuffer {
    uint values[];
} final_pass_flag_buffer;
layout(set = 0, binding = 15, std430) readonly restrict buffer InputCandidateCountBuffer {
    uint value;
} input_candidate_count_buffer;
layout(set = 0, binding = 16, std430) readonly restrict buffer InputCandidateIndexBuffer {
    uint indices[];
} input_candidate_index_buffer;
layout(set = 0, binding = 17, std430) restrict buffer FinalCandidateCountBuffer {
    uint value;
} final_candidate_count_buffer;
layout(set = 0, binding = 18, std430) restrict buffer FinalCandidateIndexBuffer {
    uint indices[];
} final_candidate_index_buffer;

uint64_t s0;
uint64_t s1;
uint64_t s2;
uint64_t s3;

uint64_t make_u64(uint low, uint high) {
    return uint64_t(low) | (uint64_t(high) << 32u);
}
uint low_u32(uint64_t value) { return uint(value & uint64_t(0xffffffffu)); }
uint high_u32(uint64_t value) { return uint(value >> 32u); }
uint64_t rotl64(uint64_t value, uint amount) {
    return (value << amount) | (value >> (64u - amount));
}
uint64_t splitmix_next(inout uint64_t state) {
    state += make_u64(0x7f4a7c15u, 0x9e3779b9u);
    uint64_t value = state;
    value = (value ^ (value >> 30u)) * make_u64(0x1ce4e5b9u, 0xbf58476du);
    value = (value ^ (value >> 27u)) * make_u64(0x133111ebu, 0x94d049bbu);
    return value ^ (value >> 31u);
}
void rng_initialize(uint64_t seed) {
    uint64_t state = seed;
    s0 = splitmix_next(state);
    s1 = splitmix_next(state);
    s2 = splitmix_next(state);
    s3 = splitmix_next(state);
}
uint64_t next_u64() {
    uint64_t result = rotl64(s1 * uint64_t(5u), 7u) * uint64_t(9u);
    uint64_t temporary = s1 << 17u;
    s2 ^= s0;
    s3 ^= s1;
    s1 ^= s2;
    s0 ^= s3;
    s2 ^= temporary;
    s3 = rotl64(s3, 45u);
    return result;
}
double next_double() {
    return double(next_u64() >> 11u) * 1.1102230246251565e-16lf;
}
uint next_int(uint bound) {
    return uint(next_double() * double(bound));
}
void consume_shuffle(uint length) {
    for (uint count = length; count > 1u; --count) next_int(count);
}

void write_shared_state(uint logical_index) {
    uint base = logical_index * SHARED_STATE_STRIDE;
    shared_up_front_state_buffer.values[base] = low_u32(s0);
    shared_up_front_state_buffer.values[base + 1u] = high_u32(s0);
    shared_up_front_state_buffer.values[base + 2u] = low_u32(s1);
    shared_up_front_state_buffer.values[base + 3u] = high_u32(s1);
    shared_up_front_state_buffer.values[base + 4u] = low_u32(s2);
    shared_up_front_state_buffer.values[base + 5u] = high_u32(s2);
    shared_up_front_state_buffer.values[base + 6u] = low_u32(s3);
    shared_up_front_state_buffer.values[base + 7u] = high_u32(s3);
}

bool encounter_eligible(uint entry_index, bool previous_valid, uint previous_source, uint previous_reference_plus_one) {
    if (!previous_valid) return true;
    uint base = entry_index * ENCOUNTER_STRIDE;
    uint reference_plus_one = encounter_buffer.values[base + 1u];
    if (reference_plus_one != 0u && previous_reference_plus_one != 0u &&
        reference_plus_one == previous_reference_plus_one) return false;
    uint conflict_offset = encounter_buffer.values[base + 2u];
    uint conflict_count = encounter_buffer.values[base + 3u];
    for (uint index = 0u; index < conflict_count; ++index) {
        if (conflict_buffer.ordinals[conflict_offset + index] == previous_source) return false;
    }
    return true;
}

void consume_encounter_queue(
    uint invocation,
    uint source_offset,
    uint source_count,
    uint slots,
    inout bool previous_valid,
    inout uint previous_source,
    inout uint previous_reference_plus_one)
{
    if (slots == 0u) return;
    uint scratch_stride = plan_meta_buffer.values[8u];
    uint scratch_base = invocation * scratch_stride;
    uint count = 0u;
    for (uint slot = 0u; slot < slots; ++slot) {
        if (count == 0u && source_count > 0u) {
            for (uint index = 0u; index < source_count; ++index)
                encounter_scratch_buffer.entries[scratch_base + index] = source_offset + index;
            count = source_count;
        }

        int selected_bag_index = -1;
        bool has_eligible = false;
        for (uint index = 0u; index < count; ++index) {
            uint entry_index = encounter_scratch_buffer.entries[scratch_base + index];
            if (encounter_eligible(entry_index, previous_valid, previous_source, previous_reference_plus_one)) {
                has_eligible = true;
                break;
            }
        }
        if (has_eligible) {
            while (true) {
                uint candidate_index = uint(next_double() * double(count));
                uint entry_index = encounter_scratch_buffer.entries[scratch_base + candidate_index];
                if (encounter_eligible(entry_index, previous_valid, previous_source, previous_reference_plus_one)) {
                    selected_bag_index = int(candidate_index);
                    break;
                }
            }
        } else {
            double draw_value = next_double();
            if (count > 0u) selected_bag_index = int(draw_value * double(count));
        }

        if (selected_bag_index < 0) continue;
        uint selected_entry = encounter_scratch_buffer.entries[scratch_base + uint(selected_bag_index)];
        for (uint index = uint(selected_bag_index); index + 1u < count; ++index)
            encounter_scratch_buffer.entries[scratch_base + index] =
                encounter_scratch_buffer.entries[scratch_base + index + 1u];
        count--;
        uint selected_base = selected_entry * ENCOUNTER_STRIDE;
        previous_source = encounter_buffer.values[selected_base];
        previous_reference_plus_one = encounter_buffer.values[selected_base + 1u];
        previous_valid = true;
    }
}

bool values_contain(uint first_value, uint second_value, uint value_count, uint expected) {
    return (value_count > 0u && first_value == expected) ||
           (value_count > 1u && second_value == expected);
}

bool evaluate_predicate(uint predicate_index, uint first_value, uint second_value, uint value_count) {
    uint base = predicate_index * PREDICATE_STRIDE;
    if (predicate_buffer.values[base] != 0u) return false;
    uint any_offset = predicate_buffer.values[base + 1u];
    uint any_count = predicate_buffer.values[base + 2u];
    uint all_offset = predicate_buffer.values[base + 3u];
    uint all_count = predicate_buffer.values[base + 4u];
    uint ban_offset = predicate_buffer.values[base + 5u];
    uint ban_count = predicate_buffer.values[base + 6u];
    if (any_count > 0u) {
        bool found = false;
        for (uint index = 0u; index < any_count; ++index) {
            if (values_contain(first_value, second_value, value_count,
                    predicate_id_buffer.ids[any_offset + index])) {
                found = true;
                break;
            }
        }
        if (!found) return false;
    }
    for (uint index = 0u; index < all_count; ++index)
        if (!values_contain(first_value, second_value, value_count,
                predicate_id_buffer.ids[all_offset + index])) return false;
    for (uint index = 0u; index < ban_count; ++index)
        if (values_contain(first_value, second_value, value_count,
                predicate_id_buffer.ids[ban_offset + index])) return false;
    return true;
}

bool evaluate_predicate_range(uint offset, uint count, uint first_value, uint second_value, uint value_count) {
    for (uint index = 0u; index < count; ++index)
        if (!evaluate_predicate(offset + index, first_value, second_value, value_count)) return false;
    return true;
}

void main() {
    uint invocation = gl_GlobalInvocationID.x;
    uint root_count = plan_meta_buffer.values[0u];
    uint input_mode = plan_meta_buffer.values[15u];
    uint input_count = input_mode == 0u ? root_count : input_candidate_count_buffer.value;
    if (invocation >= input_count) return;
    uint logical_index = input_mode == 0u ? invocation : input_candidate_index_buffer.indices[invocation];
    if (logical_index >= root_count) return;
    uint parity_count = plan_meta_buffer.values[16u];
    if (logical_index < parity_count) final_pass_flag_buffer.values[logical_index] = 0u;
    if (plan_meta_buffer.values[10u] != 0u) return;

    uint64_t root_hash = root_buffer.roots[logical_index];
    uint64_t up_front_hash = make_u64(0xda243e80u, 0x1a8a7b60u);
    rng_initialize(root_hash + up_front_hash);
    uint relic_bucket_count = plan_meta_buffer.values[1u];
    for (uint bucket = 0u; bucket < relic_bucket_count; ++bucket)
        consume_shuffle(relic_bucket_length_buffer.lengths[bucket]);
    if (logical_index < parity_count) write_shared_state(logical_index);

    uint selection_group_count = plan_meta_buffer.values[2u];
    uint selected_acts[8] = uint[8](INVALID_ID, INVALID_ID, INVALID_ID, INVALID_ID,
                                    INVALID_ID, INVALID_ID, INVALID_ID, INVALID_ID);
    uint64_t saved_s0 = s0;
    uint64_t saved_s1 = s1;
    uint64_t saved_s2 = s2;
    uint64_t saved_s3 = s3;
    uint64_t act_selection_hash = make_u64(0x9dc62d50u, 0x79a5d529u);
    rng_initialize(root_hash + act_selection_hash);
    for (uint index = 0u; index < selection_group_count; ++index) {
        uvec4 group = selection_group_meta_buffer.groups[index];
        selected_acts[index] = group.z == 1u
            ? selection_id_buffer.ids[group.x]
            : selection_id_buffer.ids[group.x + next_int(group.y)];
    }
    uint act1_override = plan_meta_buffer.values[11u];
    if (selection_group_count > 0u && act1_override != INVALID_ID) selected_acts[0] = act1_override;
    s0 = saved_s0;
    s1 = saved_s1;
    s2 = saved_s2;
    s3 = saved_s3;

    uint shared_ancient_count = plan_meta_buffer.values[5u];
    consume_shuffle(shared_ancient_count);
    uint assigned_shared_count[8] = uint[8](0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u);
    uint remaining_shared = shared_ancient_count;
    for (uint selected_index = 1u; selected_index < selection_group_count; ++selected_index) {
        uint assigned = next_int(remaining_shared + 1u);
        assigned_shared_count[selected_index] = assigned;
        remaining_shared -= assigned;
    }

    uint first_boss_by_selected_act[8] = uint[8](INVALID_ID, INVALID_ID, INVALID_ID, INVALID_ID,
                                                  INVALID_ID, INVALID_ID, INVALID_ID, INVALID_ID);
    uint max_required_act = plan_meta_buffer.values[6u];
    bool requires_second_boss = plan_meta_buffer.values[7u] != 0u;
    uint ascension = plan_meta_buffer.values[12u];
    uint act_index_map_length = plan_meta_buffer.values[3u];

    for (uint selected_index = 0u; selected_index < selection_group_count; ++selected_index) {
        uint selected_act_id = selected_acts[selected_index];
        if (selected_act_id >= act_index_map_length) return;
        uint act_index = act_index_map_buffer.indices[selected_act_id];
        if (act_index == INVALID_ID || act_index >= plan_meta_buffer.values[4u]) return;
        uint base = act_index * ACT_META_STRIDE;
        uint act_number = act_meta_buffer.values[base];
        if (!requires_second_boss && act_number > max_required_act) break;

        consume_shuffle(act_meta_buffer.values[base + 2u]);
        uint boss_act_count = act_meta_buffer.values[base + 15u];
        uint boss_one_count = act_meta_buffer.values[base + 17u];
        uint boss_two_count = act_meta_buffer.values[base + 19u];
        bool has_boss_predicate = boss_act_count + boss_one_count + boss_two_count > 0u;
        bool need_beyond_events = act_number < max_required_act || has_boss_predicate || requires_second_boss;
        if (!need_beyond_events) continue;

        bool previous_valid = false;
        uint previous_source = 0u;
        uint previous_reference_plus_one = 0u;
        consume_encounter_queue(invocation,
            act_meta_buffer.values[base + 3u], act_meta_buffer.values[base + 4u], act_meta_buffer.values[base + 5u],
            previous_valid, previous_source, previous_reference_plus_one);
        consume_encounter_queue(invocation,
            act_meta_buffer.values[base + 6u], act_meta_buffer.values[base + 7u], act_meta_buffer.values[base + 8u],
            previous_valid, previous_source, previous_reference_plus_one);
        bool elite_previous_valid = false;
        uint elite_previous_source = 0u;
        uint elite_previous_reference_plus_one = 0u;
        consume_encounter_queue(invocation,
            act_meta_buffer.values[base + 9u], act_meta_buffer.values[base + 10u], act_meta_buffer.values[base + 11u],
            elite_previous_valid, elite_previous_source, elite_previous_reference_plus_one);

        uint boss_offset = act_meta_buffer.values[base + 12u];
        uint boss_count = act_meta_buffer.values[base + 13u];
        if (boss_count == 0u) return;
        uint first_boss = boss_id_buffer.ids[boss_offset + next_int(boss_count)];
        first_boss_by_selected_act[selected_index] = first_boss;
        if (has_boss_predicate) {
            if (!evaluate_predicate_range(act_meta_buffer.values[base + 16u], boss_one_count,
                    first_boss, INVALID_ID, 1u)) return;
            bool defer_act_predicate = ascension >= 10u && act_number == 3u && boss_act_count > 0u;
            if (!defer_act_predicate && !evaluate_predicate_range(
                    act_meta_buffer.values[base + 14u], boss_act_count,
                    first_boss, INVALID_ID, 1u)) return;
        }

        bool need_ancient_continuation = act_number < max_required_act || requires_second_boss;
        if (need_ancient_continuation) {
            uint ancient_pool_count = act_meta_buffer.values[base + 20u] + assigned_shared_count[selected_index];
            if (ancient_pool_count == 0u) return;
            next_int(ancient_pool_count);
        }
    }

    if (requires_second_boss && selection_group_count > 0u) {
        uint final_index = selection_group_count - 1u;
        uint final_act_id = selected_acts[final_index];
        if (final_act_id >= act_index_map_length) return;
        uint final_act_index = act_index_map_buffer.indices[final_act_id];
        if (final_act_index == INVALID_ID) return;
        uint base = final_act_index * ACT_META_STRIDE;
        uint boss_offset = act_meta_buffer.values[base + 12u];
        uint boss_count = act_meta_buffer.values[base + 13u];
        uint first_boss = first_boss_by_selected_act[final_index];
        if (boss_count <= 1u || first_boss == INVALID_ID) return;
        uint selected_rank = next_int(boss_count - 1u);
        uint second_boss = INVALID_ID;
        uint rank = 0u;
        for (uint index = 0u; index < boss_count; ++index) {
            uint candidate = boss_id_buffer.ids[boss_offset + index];
            if (candidate == first_boss) continue;
            if (rank == selected_rank) {
                second_boss = candidate;
                break;
            }
            rank++;
        }
        if (second_boss == INVALID_ID) return;
        if (!evaluate_predicate_range(act_meta_buffer.values[base + 18u], act_meta_buffer.values[base + 19u],
                second_boss, INVALID_ID, 1u)) return;
        if (!evaluate_predicate_range(act_meta_buffer.values[base + 14u], act_meta_buffer.values[base + 15u],
                first_boss, second_boss, 2u)) return;
    }

    if (logical_index < parity_count) final_pass_flag_buffer.values[logical_index] = 1u;
    uint compact_index = atomicAdd(final_candidate_count_buffer.value, 1u);
    final_candidate_index_buffer.indices[compact_index] = logical_index;
}
