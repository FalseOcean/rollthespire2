// Pure Leafy+NewLeaf aggregate over closed, independent captured streams.
// A target-instance bit is consumed once, including duplicate target identities.
bool joint_transform_pre_gate(uint64_t root) {
    uint base=uint(N_TRANSFORM_META_OFFSET), predicate=plan_value(base);
    uint targets=plan_value(base+1u), goal=plan_value(base+2u);
    uint matched=0u,rare=0u,consumed=0u;
    for(uint g=0u;g<2u;g++) {
        uint at=base+4u+g*7u;
        RngState state=rng_initialize(root+plan_u64(at));
        for(uint i=0u;i<plan_value(at+2u);i++) {
            uint pi=at+3u+i*2u;
            uint value=other_card_ids.ids[plan_value(pi)+next_int(state,plan_value(pi+1u))];
            consumed++;
            if(predicate==0u) {
                rare+=value;
                if(rare+3u-consumed<goal)return false;
            } else {
                uint available=(value&0x7fffffffu)&~matched;
                if(available!=0u)matched|=1u<<uint(findLSB(available));
                else if(predicate==2u && (value&0x80000000u)==0u)return false;
                if(uint(bitCount(matched))+3u-consumed<targets)return false;
            }
        }
    }
    return predicate==0u ? rare>=goal : uint(bitCount(matched))>=targets;
}
