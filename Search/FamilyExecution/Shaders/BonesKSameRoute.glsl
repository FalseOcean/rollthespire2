// Bounded K+Large experiment only. A=K->Large, B=Large->K.
// N and R retain their existing numerical implementations and authority.
uint bones_k_route_mask;
#if BONES_K_MODE == 2
layout(set=0,binding=13,std430) readonly restrict buffer BonesKProjection { uint values[]; } rfull_capsule;
RngState rfull_checkpoint_rng;
uint nr_heavy_entries;
#if BONES_HIDDEN_UPGRADES == 1 || BONES_HIDDEN_UPGRADES == 3
#define RFULL_HIDDEN_UPGRADES
bool bones_track_hidden;
uint rfull_upgrade_mask;
#endif
void rfull_rng_initialize(uint64_t seed) { rfull_checkpoint_rng=rng_initialize(seed); }
uint rfull_next_int(uint bound) { return next_int(rfull_checkpoint_rng,bound); }
uint64_t rfull_next_u64() { return next_u64(rfull_checkpoint_rng); }
#define RFULL_CHECKPOINT_ONLY
#define RFULL_TRACK_HEAVY_ENTRY
/*__RT2_RFULL_CHECKPOINT_MATCHER__*/
#endif
#if BONES_HIDDEN_UPGRADES != 0 && BONES_HIDDEN_UPGRADES != 4
uint bones_hidden_advance(uint mask) {
    return ((mask&1u)!=0u ? rfull_meta(BONES_HIDDEN_OFFSET) : 0u) +
           ((mask&2u)!=0u ? rfull_meta(BONES_HIDDEN_OFFSET+1u) : 0u);
}
bool bones_hidden_route(uint64_t root,RngState rewards) {
#if defined(N_CAPSULE_NICHE_OFFSET) && N_CAPSULE_NICHE_OFFSET >= 0
    return route(root,rewards,3u,14u);
#else
    if(rfull_meta(BONES_HIDDEN_OFFSET)==0xffffffffu) return true;
    uint authored=rfull_meta(BONES_HIDDEN_OFFSET+2u), slots=rfull_meta(BONES_HIDDEN_OFFSET+3u);
    for(uint mask=0u;mask<4u;mask++) {
        if((mask&authored)!=authored || uint(bitCount(mask&~authored))>slots) continue;
        if(route_at_arrival(root,rewards,3u,14u,bones_hidden_advance(mask),0u)) return true;
    }
    return false;
#endif
}
#endif
bool bones_k_matches(uint64_t root, RngState rewards, uint a, uint b) {
    if (!((a==14u && b==3u) || (a==3u && b==14u))) {
        atomicExchange(header.values[3u],1u); return false;
    }
    // Execute the same N route operators; never derive pickup from offer order.
#if BONES_HIDDEN_UPGRADES != 0
    bool allow_a=rfull_meta(29u)==0u || rfull_meta(30u)==14u;
    bool allow_b=rfull_meta(29u)==0u || rfull_meta(30u)==3u;
    bool na=allow_a && route(root,rewards,14u,3u);
#else
    bool na=route(root,rewards,14u,3u);
#endif
#if BONES_HIDDEN_UPGRADES == 4
    // Ready, fully authored Capsule: its two distinct targets leave no hidden
    // slot. Reuse the single N-authored W/WP shift (zero for other relics).
    bool nb=allow_b && route_authored(root,rewards,3u,14u);
#elif BONES_HIDDEN_UPGRADES == 3
    // Ordering control for the same actual detector: with an unknown slot,
    // let R reject first, then evaluate only its actual Niche continuation.
    // Fully authored Capsules keep the single known-shift N gate.
    bool nb=allow_b && (rfull_meta(BONES_HIDDEN_OFFSET+3u)>0u || bones_hidden_route(root,rewards));
#elif BONES_HIDDEN_UPGRADES != 0
    // This cheap union is also the direct detector's necessary-condition gate.
    // It never depends on the old zero/authored-only N result.
    bool nb=allow_b && bones_hidden_route(root,rewards);
#else
    bool nb=route(root,rewards,3u,14u);
#endif
    bones_k_route_mask=(na ? 1u : 0u) | (nb ? 2u : 0u);
    if(bones_k_route_mask==0u) return false;
    atomicAdd(header.values[7u+bones_k_route_mask],1u); // A-only, B-only, both
#if BONES_K_MODE == 1
    return true;
#else
    bool ra=false,rb=false;
    if(na) {
#if BONES_HIDDEN_UPGRADES == 1 || BONES_HIDDEN_UPGRADES == 3
        bones_track_hidden=false;
#endif
        atomicAdd(header.values[11u],1u);
        rfull_checkpoint_rng=rewards;
        for(uint i=0u;i<18u;i++) rfull_next_u64();
        nr_heavy_entries=0u;
        ra=rfull_capsule_after_rewards(root);
        atomicAdd(header.values[13u],nr_heavy_entries);
    }
    // Short-circuit only within this authorized same-route disjunction.
    if(nb && !ra) {
#if BONES_HIDDEN_UPGRADES == 1 || BONES_HIDDEN_UPGRADES == 3
        bones_track_hidden=rfull_meta(BONES_HIDDEN_OFFSET)!=0xffffffffu && rfull_meta(BONES_HIDDEN_OFFSET+3u)>0u;
#endif
        atomicAdd(header.values[12u],1u);
        rfull_checkpoint_rng=rewards;
        nr_heavy_entries=0u;
        rb=rfull_capsule_after_rewards(root);
#if BONES_HIDDEN_UPGRADES == 1 || BONES_HIDDEN_UPGRADES == 3
        if(rb && bones_track_hidden)
            rb=route_at_arrival(root,rewards,3u,14u,bones_hidden_advance(rfull_upgrade_mask),0u);
#endif
        atomicAdd(header.values[14u],nr_heavy_entries);
    }
    if(ra || rb) atomicAdd(header.values[15u],1u);
    return ra || rb;
#endif
}
