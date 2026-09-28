// Binding 11/12 remain the existing N staged checkpoints. R-owned immutable
// target metadata uses 13 in both fused and staged variants of this physical.
layout(set=0,binding=13,std430) readonly restrict buffer BonesCapsuleProjection { uint values[]; } rfull_capsule;
RngState rfull_checkpoint_rng;
void rfull_rng_initialize(uint64_t seed) { rfull_checkpoint_rng=rng_initialize(seed); }
uint rfull_next_int(uint bound) { return next_int(rfull_checkpoint_rng,bound); }
uint64_t rfull_next_u64() { return next_u64(rfull_checkpoint_rng); }
#define RFULL_CHECKPOINT_ONLY
/*__RT2_RFULL_CHECKPOINT_MATCHER__*/

bool bones_capsule_composite(uint a, uint b, RngState rewards, uint64_t root) {
    // Offer order is not pickup order. The grouped three-result predicate sees
    // the same contiguous three draws for either Small/Large acquisition order.
    if (!((a==3u && b==28u) || (a==28u && b==3u))) {
        // Admission guarantees this pair. A violated private checkpoint is an
        // execution fault, never a silent physical fallback or partial output.
        atomicExchange(header.values[3u],1u);
        return false;
    }
    rfull_checkpoint_rng=rewards;
    return rfull_capsule_after_rewards(root);
}
