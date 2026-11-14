// Pure Leafy+NewLeaf aggregate over closed captured streams.
// A target-instance bit is consumed once, including duplicate target identities.
bool joint_shared_order(uint base, uint predicate, uint targets, uint goal, RngState state, bool new_leaf_first) {
    uint missing = (1u << targets) - 1u, rare = 0u;
    for (uint i = 0u; i < 3u; ++i) {
        uint pool = new_leaf_first ? (i + 2u) % 3u : i;
        uint pi = base + 4u + pool * 2u;
        uint value = other_card_ids.ids[plan_value(pi) + next_int(state, plan_value(pi+1u))];
        uint remaining = 2u - i;
        if (predicate == 0u) {
            rare += value >> 31u;
            if (rare >= goal) return true;
            if (rare + remaining < goal) return false;
        } else {
            uint available = value & missing;
            if (available != 0u) missing &= ~(1u << uint(findLSB(available)));
            else if (predicate == 2u && (value & 0x80000000u) == 0u) return false;
            if (predicate == 1u && missing == 0u) return true;
            if (uint(bitCount(missing)) > remaining) return false;
        }
    }
    return predicate == 0u ? rare >= goal : missing == 0u;
}

bool joint_transform_pre_gate(uint64_t root) {
    uint base=uint(N_TRANSFORM_META_OFFSET), predicate=plan_value(base);
    uint targets=plan_value(base+1u), goal=plan_value(base+2u);
    if (uses_child_shuffle()) {
        uint orders = plan_value(base+3u);
        RngState state = rng_initialize(root + plan_u64(37u));
        return ((orders & 1u) != 0u && joint_shared_order(base,predicate,targets,goal,state,false)) ||
            ((orders & 2u) != 0u && joint_shared_order(base,predicate,targets,goal,state,true));
    }
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
