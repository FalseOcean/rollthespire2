// R-owned mode-2 donor strategy: rarity gate before tracked target ranks.
// Canonical World pool descriptors replace the historical duplicate bag catalog.
layout(set=0,binding=11,std430) readonly restrict buffer CapsuleProjection { uint values[]; } capsule_projection;
bool capsule_rarity_projection(uint source, inout RngState rewards, out uint rarity0, out uint rarity1) {
    if (source != capsule_projection.values[1u]) return false;
    uint count=capsule_projection.values[2u];
    float first_roll=next_float(rewards);
    rarity0=first_roll<0.5f ? 1u : first_roll<0.83f ? 2u : 3u;
    rarity1=0u;
    if (source==3u) {
        float second_roll=next_float(rewards);
        rarity1=second_roll<0.5f ? 1u : second_roll<0.83f ? 2u : 3u;
    }
    uint target0_rarity=capsule_projection.values[5u], target1_rarity=capsule_projection.values[8u];
    if (count==1u && rarity0!=target0_rarity && rarity1!=target0_rarity) return false;
    if (count==2u && !((rarity0==target0_rarity && rarity1==target1_rarity) || (rarity1==target0_rarity && rarity0==target1_rarity))) return false;
    return true;
}
bool capsule_composite(uint source, inout RngState rewards, uint64_t root) {
    uint rarity0,rarity1;
    if(!capsule_rarity_projection(source,rewards,rarity0,rarity1)) return false;
    uint count=capsule_projection.values[2u];
    uint target0_rarity=capsule_projection.values[5u],target1_rarity=capsule_projection.values[8u];
    uint rank0=capsule_projection.values[4u],rank1=capsule_projection.values[7u];
    // Same tracked-position traversal as project_capsule_target_ranks (P1 mode 2),
    // including every prerequisite shared/player bucket in canonical order.
    RngState up_front=rng_initialize(root+make_u64(0x__RT2_UP_FRONT_LOW__u,0x__RT2_UP_FRONT_HIGH__u));
    // UpFront is private and has no consumer after this terminal target matcher.
    uint last_target_bucket=capsule_projection.values[3u];
    if(count==2u) last_target_bucket=max(last_target_bucket,capsule_projection.values[6u]);
    for(uint bucket=0u;bucket<=last_target_bucket;bucket++) {
        for(uint length=capsule_projection.values[9u+bucket];length>1u;length--) {
            uint selected=next_int(up_front,length);
            if(bucket==capsule_projection.values[3u]) swap_tracked_position(rank0,selected,length-1u);
            if(count==2u && bucket==capsule_projection.values[6u]) swap_tracked_position(rank1,selected,length-1u);
        }
    }
    uint second_position=rarity0==rarity1 ? 1u : 0u;
    bool first0=rarity0==target0_rarity && rank0==0u;
    bool second0=rarity1==target0_rarity && rank0==second_position;
    if(count==1u) return first0 || second0;
    bool first1=rarity0==target1_rarity && rank1==0u;
    bool second1=rarity1==target1_rarity && rank1==second_position;
    return (first0 && second1) || (first1 && second0);
}
