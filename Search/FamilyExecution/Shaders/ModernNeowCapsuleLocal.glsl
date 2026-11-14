bool mc_local_route(uint64_t root, RngState rewards, uint first, uint second,
    inout bool ready, inout uint64_t lane_seeds[3], inout bool initialized[3], inout uint heads[9]) {
    uint first_count = mc_slots(first), draws = first_count + mc_slots(second);
    if (!mc_sources_present(first, second, draws)) return false;
    bool checks_second = false;
    for (uint i = 0u; i < plan_value(84u); ++i) {
        uint source = plan_value(85u + i);
        if (source != first && source != second) return false;
        checks_second = checks_second || source == second;
    }
    bool final_curse = plan_value(83u) != 0u;
    RouteRngState state = initialize_route_rng_state(root, rewards);
    if (first_count == 0u && !local_operator(first, state)) return false;
    if (draws != 0u && first_count == 0u) {
        uint burn = first < 30u ? mc_value(mc_value(26u) + first) : 0xffffffffu;
        if (burn == 0xffffffffu) return true;
        // When N observes only another stream, its local operator leaves
        // Rewards untouched. The following Capsule still needs that arrival.
        if ((plan_value(82u) & 1u) == 0u) mc_advance_companion(burn, state.rewards);
    }
    if (draws == 0u) {
        uint empty[3];
        if (!mc_matches_targets(empty, 0u, first, false) || !local_operator(second, state)) return false;
    } else {
        // The N operator and R rarity share the actual Rewards checkpoint.
        // With one Capsule all its pulls are consecutive within this route.
        // The only two-Capsule route contains no intervening N operator.
        uint consumed[3] = uint[3](0u, 0u, 0u);
        uint lanes[3], ranks[3];
        for (uint i = 0u; i < draws; ++i)
            lanes[i] = mc_choose_lane(next_float(state.rewards), consumed, ranks[i]);
#if MC_TRACKED
        if (!mc_matches_targets(lanes, draws, first, true)) return false;
#endif
        if (!ready) {
            mc_prepare_lanes(root, lane_seeds);
            ready = true;
        }
        uint seen[3], first_advance = 0u, total_advance = 0u;
        bool later_local_unknown = false;
        for (uint i = 0u; i < draws; ++i) {
            uint key = mc_draw_key(lanes[i], ranks[i], lane_seeds, initialized, heads);
            if (mc_contains(mc_value(27u), mc_value(28u), key)) {
                // A later draw depends on this earlier pickup's bag/Rewards
                // effects. Conservatively retain this route for Exact.
                if (i + 1u < draws) return true;
                if (final_curse || (i < first_count && checks_second)) later_local_unknown = true;
            }
            if (mc_contains(mc_value(58u), mc_value(59u), key)) {
                ++total_advance;
                if (i < first_count) ++first_advance;
            }
            seen[i] = key;
        }
        if (!mc_matches_targets(seen, draws, first, false)) return false;
        if (later_local_unknown) return true;
        if (mc_slots(second) == 0u) {
            for (uint i = 0u; i < first_advance; ++i) next_u64(state.non_combat);
            if (!local_operator(second, state)) return false;
            total_advance -= first_advance;
        }
        if (final_curse)
            for (uint i = 0u; i < total_advance; ++i) next_u64(state.non_combat);
    }
    if (!final_curse) return true;
    uint count = n1c_meta(13u);
    if (count == 0u) return false;
    return final_curse_matches(other_card_ids.ids[n1c_meta(12u) + next_int(state.non_combat, count)]);
}

bool modern_local_capsule_matches(uint64_t root, RngState rewards, uint first, uint second) {
    uint64_t lane_seeds[3];
    bool initialized[3] = bool[3](false, false, false);
    uint heads[9] = uint[9](MC_UNTRACKED, MC_UNTRACKED, MC_UNTRACKED,
        MC_UNTRACKED, MC_UNTRACKED, MC_UNTRACKED,
        MC_UNTRACKED, MC_UNTRACKED, MC_UNTRACKED);
    bool ready = false;
    if (mc_local_route(root, rewards, first, second, ready, lane_seeds, initialized, heads)) return true;
    return plan_value(12u) == 255u &&
        mc_local_route(root, rewards, second, first, ready, lane_seeds, initialized, heads);
}
