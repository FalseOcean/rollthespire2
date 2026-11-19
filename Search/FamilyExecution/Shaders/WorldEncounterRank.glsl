uint encounter_index_at_rank(uint live, uint rank) {
    uint offset = 0u;
    for (uint half_width = 16u; half_width > 0u; half_width >>= 1u) {
        uint low_count = uint(bitCount(live & ((1u << half_width) - 1u)));
        if (rank >= low_count) {
            rank -= low_count;
            live >>= half_width;
            offset += half_width;
        }
    }
    return offset;
}

// The predecessor uses the packed entry index in this implementation. The
// caller still carries it from weak to regular and resets it before elite.
void consume_encounter_queue(
    uint invocation,
    uint source_offset,
    uint source_count,
    uint slots,
    inout bool previous_valid,
    inout uint previous_entry,
    inout uint previous_reference_plus_one)
{
    if (slots == 0u) return;
    if (source_count == 0u) {
        for (uint slot = 0u; slot < slots; ++slot) next_u64();
        return;
    }
    uint full = 0xffffffffu >> (32u - source_count);
    uint live = full;
    uint count = source_count;
    uint row = reserved.v[source_offset];
    uint predecessor_base = reserved.v[row];
    for (uint slot = 0u; slot < slots; ++slot) {
        if (count == 0u) {
            live = full;
            count = source_count;
        }
        uint allowed = previous_valid ? reserved.v[row + 1u + (previous_entry - predecessor_base)] : full;
        // Sample from the full live bag, preserving every rejected attempt.
        // If no eligible item remains, the game accepts one ordinary draw.
        if ((allowed & live) == 0u) allowed = full;
        uint selected;
        do {
            uint rank = next_int(count);
            selected = count == source_count ? rank : encounter_index_at_rank(live, rank);
        } while ((allowed & (1u << selected)) == 0u);
        live &= ~(1u << selected);
        --count;
        previous_entry = source_offset + selected;
        previous_valid = true;
    }
}
