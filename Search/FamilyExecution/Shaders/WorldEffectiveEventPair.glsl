#define RT2_W_EFFECTIVE_EVENT_PAIR

uint eligible_through(uint mask, uint position) {
    // position is 0..31, including the last slot of a 32-entry pool.
    return uint(bitCount(mask & (0xffffffffu >> (31u - position))));
}

uint moved_event_position(uint position, uint selected, uint tail) {
    if (position == selected) return tail;
    if (position == tail) return selected;
    return position;
}

bool shuffle_event_pair(uint count, uint eligible, uint first, uint second) {
    bool second_locked = false;
    for (uint tail = count - 1u; tail > 0u; --tail) {
        uint selected = next_int(tail + 1u);
        // Only this selected entry is fixed forever. Its raw eligible rank can
        // lose one when the native opening cursor skips raw slot zero.
        if (selected == first) {
            if (!second_locked || eligible_through(eligible, tail) > 2u) return false;
        } else if (selected == second) {
            uint rank = eligible_through(eligible, tail);
            if (rank < 2u || rank > 3u) return false;
            second_locked = true;
        }
        if ((((eligible >> selected) ^ (eligible >> tail)) & 1u) != 0u)
            eligible ^= (1u << selected) | (1u << tail);
        first = moved_event_position(first, selected, tail);
        second = moved_event_position(second, selected, tail);
    }
    uint after_cursor = eligible & ~1u;
    return first > 0u && first < second &&
        eligible_through(after_cursor, first) == 1u && eligible_through(after_cursor, second) == 2u;
}

bool effective_event_pair(uint offset, uint count) {
    // Preserve the single parent draw and restore its entire post-fork state
    // on both success and failure. The world loop owns subsequent observations.
    u64vec4 parent = fork_world_shuffle();
    uint first = data.v[offset + 1u], second = data.v[offset + 2u];
    bool matched = false;
    if (first != INVALID_ID && second != INVALID_ID)
        matched = shuffle_event_pair(count, data.v[offset], first, second);
    restore_world_parent(parent);
    return matched;
}
