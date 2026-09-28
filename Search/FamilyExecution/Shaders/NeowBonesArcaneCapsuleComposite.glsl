// This bounded physical owns no route ABI: both arrivals are invocation locals.
layout(set=0,binding=13,std430) readonly restrict buffer BonesCapsuleProjection { uint values[]; } rfull_capsule;
RngState rfull_checkpoint_rng;
uint nr_heavy_entries;
void rfull_rng_initialize(uint64_t seed) { rfull_checkpoint_rng=rng_initialize(seed); }
uint rfull_next_int(uint bound) { return next_int(rfull_checkpoint_rng,bound); }
uint64_t rfull_next_u64() { return next_u64(rfull_checkpoint_rng); }
#define RFULL_CHECKPOINT_ONLY
#define RFULL_TRACK_HEAVY_ENTRY
/*__RT2_RFULL_CHECKPOINT_MATCHER__*/

bool local_operator(uint source, inout RouteRngState state);
bool bones_capsule_composite(uint a, uint b, RngState rewards, uint64_t root) {
    if (!((a==3u && b==10u) || (a==10u && b==3u))) {
        atomicExchange(header.values[3u],1u);
        return false;
    }
    atomicAdd(header.values[8u],1u); // Pair admitted, including fused/Compact.
    bool allow_a = plan_value(12u)==255u || plan_value(12u)==3u;
    bool allow_b = plan_value(12u)==255u || plan_value(12u)==10u;

    // A: Large consumes two Rewards rolls before Arcane.
    // B: Arcane consumes one roll, leaving the actual Capsule arrival Q1.
    // Reuse the existing N operators, including their predicate/continuation.
    RouteRngState state = initialize_route_rng_state(root,rewards);
    bool n_b = false;
    RngState q1 = rewards;
    if (allow_b) { n_b = local_operator(10u,state); q1 = state.rewards; }
    bool n_a = false;
    if (allow_a) {
        state = initialize_route_rng_state(root,rewards);
        local_operator(3u,state);
        n_a = local_operator(10u,state);
    }
    if (!(n_a || n_b)) return false;
    atomicAdd(header.values[9u],1u);

    // R has its OWN existential route union. In particular, n_b==false must
    // not suppress R_B: the cross-route witness belongs to Exact to reject.
    nr_heavy_entries = 0u;
    bool r = false;
    if (allow_a) { rfull_checkpoint_rng = rewards; r = rfull_capsule_after_rewards(root); }
    if (!r && allow_b) { rfull_checkpoint_rng = q1; r = rfull_capsule_after_rewards(root); }
    if (nr_heavy_entries != 0u) {
        atomicAdd(header.values[10u],1u); // Roots passing at least one rarity gate.
        atomicAdd(header.values[11u],nr_heavy_entries); // Actual projection calls.
    }
    return r;
}
