// Bounded K+Large experiment only. A=K->Large, B=Large->K.
// N and R retain their existing numerical implementations and authority.
uint bones_k_route_mask;
#if BONES_K_MODE == 2
layout(set=0,binding=13,std430) readonly restrict buffer BonesKProjection { uint values[]; } rfull_capsule;
RngState rfull_checkpoint_rng;
uint nr_heavy_entries;
void rfull_rng_initialize(uint64_t seed) { rfull_checkpoint_rng=rng_initialize(seed); }
uint rfull_next_int(uint bound) { return next_int(rfull_checkpoint_rng,bound); }
uint64_t rfull_next_u64() { return next_u64(rfull_checkpoint_rng); }
#define RFULL_CHECKPOINT_ONLY
#define RFULL_TRACK_HEAVY_ENTRY
/*__RT2_RFULL_CHECKPOINT_MATCHER__*/
#endif
bool bones_k_matches(uint64_t root, RngState rewards, uint a, uint b) {
    if (!((a==14u && b==3u) || (a==3u && b==14u))) {
        atomicExchange(header.values[3u],1u); return false;
    }
    // Execute the same N route operators; never derive pickup from offer order.
    bool na=route(root,rewards,14u,3u);
    bool nb=route(root,rewards,3u,14u);
    bones_k_route_mask=(na ? 1u : 0u) | (nb ? 2u : 0u);
    if(bones_k_route_mask==0u) return false;
    atomicAdd(header.values[7u+bones_k_route_mask],1u); // A-only, B-only, both
#if BONES_K_MODE == 1
    return true;
#else
    bool ra=false,rb=false;
    if(na) {
        atomicAdd(header.values[11u],1u);
        rfull_checkpoint_rng=rewards;
        for(uint i=0u;i<18u;i++) rfull_next_u64();
        nr_heavy_entries=0u;
        ra=rfull_capsule_after_rewards(root);
        atomicAdd(header.values[13u],nr_heavy_entries);
    }
    // Short-circuit only within this authorized same-route disjunction.
    if(nb && !ra) {
        atomicAdd(header.values[12u],1u);
        rfull_checkpoint_rng=rewards;
        nr_heavy_entries=0u;
        rb=rfull_capsule_after_rewards(root);
        atomicAdd(header.values[14u],nr_heavy_entries);
    }
    if(ra || rb) atomicAdd(header.values[15u],1u);
    return ra || rb;
#endif
}
