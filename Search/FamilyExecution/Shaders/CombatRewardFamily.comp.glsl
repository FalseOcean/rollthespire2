#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require
layout(local_size_x=64,local_size_y=1,local_size_z=1) in;
layout(set=0,binding=0,std430) readonly restrict buffer B0 {uint values[];} batch_meta;
layout(set=0,binding=1,std430) readonly restrict buffer B1 {uint values[];} plan_meta;
layout(set=0,binding=2,std430) readonly restrict buffer B2 {uint ids[];} bones_ids;
layout(set=0,binding=3,std430) readonly restrict buffer B3 {uint values[];} pool_meta;
layout(set=0,binding=4,std430) readonly restrict buffer B4 {uint ids[];} dense_ids;
layout(set=0,binding=5,std430) readonly restrict buffer B5 {uint values[];} capability_meta;
layout(set=0,binding=6,std430) readonly restrict buffer B6 {uint values[];} predicate_meta;
layout(set=0,binding=7,std430) readonly restrict buffer B7 {uint ids[];} predicate_targets;
layout(set=0,binding=8,std430) restrict buffer B8 {uint values[];} header;
layout(set=0,binding=9,std430) writeonly restrict buffer B9 {uint values[];} output_ordinals;
layout(set=0,binding=10,std430) readonly restrict buffer B10 {uint values[];} input_ordinals;
shared uint pass_count;
shared uint output_base;
bool cr_family_numeric_failure(){atomicOr(header.values[3],1u);return false;}
#define RT2_CR_FAMILY 1
#define RT2_CR_NUMERIC_FAILURE() cr_family_numeric_failure()
/*__C_CAPSULE_DEFINES__*/
/*__C_HOT_DEFINES__*/
#define cr_evaluate_rewards cr_unused_baseline
/*__C_COMMON__*/
#undef cr_evaluate_rewards
/*__C_TERMINAL_CARD_GATE__*/
/*__C_STREAMING__*/
/*__C_OBSERVED_OPENING__*/

// Query-literal opening: no latent Bones identity or Capsule bag is observed.
bool c_route(uint64_t root,uint route){
    RngState unused=cr_rng_initialize(uint64_t(0));
    RewardRouteState state=cr_initialize_route(root,false,unused);
#if RT2_C_MULTIPLAYER
    bool niche_known=plan_meta.values[44]!=0xffffffffu;
    if(niche_known)for(uint draw=0u;draw<plan_meta.values[44];++draw)cr_next_double(state.niche);
#else
    bool niche_known=true;
#endif
    if(plan_meta.values[40]!=0u){
        uint count=plan_meta.values[7];
        if(count<2u)return cr_family_numeric_failure();
        for(uint n=count;n>1u;--n)cr_next_int(state.rewards,n);
    }
    uint count=plan_meta.values[41];
    if(count>2u)return cr_family_numeric_failure();
    for(uint i=0u;i<count;++i){
        uint at=route==0u?i:count-1u-i;
        uint relic=plan_meta.values[42u+at];
        if(relic==14u&&plan_meta.values[49]!=0u){
            for(uint group=0u;group<2u;++group){
                if(niche_known)for(uint n=pool_meta.values[40];n>1u;--n)cr_next_double(state.niche);
                for(uint draw=0u;draw<9u;++draw)cr_next_double(state.rewards);
            }
        }else{
            if(!cr_replay_query_literal_relic_consumption(relic,root,state))return cr_family_numeric_failure();
            if(relic==19u&&niche_known)cr_next_double(state.niche);
        }
        uint advance=plan_meta.values[45u+route*2u+i];
        if(advance==0xffffffffu)niche_known=false;
        else if(niche_known)for(uint draw=0u;draw<advance;++draw)cr_next_double(state.niche);
    }
    if(plan_meta.values[40]!=0u&&niche_known)cr_next_double(state.niche);
    cr_apply_explicit_query_reward_context(state);
#if RT2_CR_HOT_ENABLED == 1 && RT2_C_HOT_OPENING_PREFIX == 1
    return cr_evaluate_rewards_unpinned_hot_state(state);
#else
    return cr_evaluate_rewards(state);
#endif
}

/*__C_DENSE_HELPERS__*/

bool c_keep_root(uint64_t root){
    if(plan_meta.values[50]!=0u){
#if RT2_C_OBSERVED_HOT == 1
        atomicAdd(header.values[7],1u);atomicOr(header.values[7],4u<<28u);
#endif
        return true;
    }
#if RT2_C_OBSERVED_OPENING == 1
    uint reasons;
    bool keep=c_observed_route(root,0u,reasons);
    if(batch_meta.values[7]==2u){
        uint other_reasons;
        bool other=c_observed_route(root,1u,other_reasons);
        keep=keep||other;reasons|=other_reasons;
    }
#if RT2_C_OBSERVED_HOT == 1
    // Count one retained root, including unions whose other route hot-passed.
    // The low 28 bits count roots; the upper four retain the reason union.
    if(keep&&reasons!=0u){atomicAdd(header.values[7],1u);atomicOr(header.values[7],reasons<<28u);}
#endif
    return keep;
#else
#if RT2_CR_HOT_ENABLED == 1 && RT2_C_HOT_OPENING_PREFIX == 0
    return cr_evaluate_rewards_unpinned_hot(root);
#else
    bool keep=c_route(root,0u);
    if(batch_meta.values[7]==2u){bool other=c_route(root,1u);keep=keep||other;}
    return keep;
#endif
#endif
}/*__C_NEUTRAL_WITNESS__*/

#if RT2_C_DENSE_CARRY8
// Adapt the existing N/S continuous-seed preparation method, keeping every
// reward simulation independent. Eight candidates per Dense invocation do not
// change Compact ABI1 or the authored opening/reward semantics.
void c_dense_carry8(){
    uint lid=gl_LocalInvocationID.x,first=gl_GlobalInvocationID.x*8u;
    uint keep_mask=0u;
    if(first<batch_meta.values[3]){
        uint ordinal=batch_meta.values[6]+first;
        uint64_t first8;uint tail4;
        encode_seed12_packed(cr_make_u64(batch_meta.values[0],batch_meta.values[1])+uint64_t(ordinal),first8,tail4);
        for(uint offset=0u;offset<8u;++offset){
            if(first+offset>=batch_meta.values[3])break;
            if(ordinal+offset>=batch_meta.values[2]||plan_meta.values[1]!=header.values[5]||batch_meta.values[7]<1u||batch_meta.values[7]>2u){
                atomicOr(header.values[3],2u);
            }else if(c_keep_root(root_hash_for_seed12_packed(first8,tail4)))keep_mask|=1u<<offset;
            if(offset+1u<8u)advance_seed12_packed(first8,tail4);
        }
    }
    if(lid==0u){pass_count=0u;output_base=0u;}
    barrier();
    uint count=uint(bitCount(keep_mask)),rank=0u;
    if(count>0u)rank=atomicAdd(pass_count,count);
    barrier();
    if(lid==0u){
        atomicAdd(header.values[4],min(512u,batch_meta.values[3]-gl_WorkGroupID.x*512u));
        if(pass_count>0u){
            output_base=atomicAdd(header.values[2],pass_count);
            atomicAdd(header.values[6],1u);
            if(output_base+pass_count>batch_meta.values[5])atomicOr(header.values[3],4u);
        }
    }
    barrier();
    for(uint offset=0u;offset<8u;++offset){
        if((keep_mask&(1u<<offset))==0u)continue;
        if(output_base+rank<batch_meta.values[5])output_ordinals.values[output_base+rank]=batch_meta.values[6]+first+offset;
        ++rank;
    }
}
#endif

void main(){
#if RT2_C_DENSE_CARRY8
    // Batch input mode is uniform, so all lanes take the same barrier path.
    if(batch_meta.values[4]==0u){c_dense_carry8();return;}
#endif
    uint index=gl_GlobalInvocationID.x,lid=gl_LocalInvocationID.x;
    bool valid=index<batch_meta.values[3],keep=false;
    uint ordinal=0u;
    if(valid){
        ordinal=batch_meta.values[4]!=0u?input_ordinals.values[index]:batch_meta.values[6]+index;
        if(ordinal>=batch_meta.values[2]||plan_meta.values[1]!=header.values[5]||batch_meta.values[7]<1u||batch_meta.values[7]>2u){
            atomicOr(header.values[3],2u);
        }else{
            uint64_t root=cr_hash_seed12(cr_make_u64(batch_meta.values[0],batch_meta.values[1])+uint64_t(ordinal));
            keep=c_keep_root(root);
        }
    }
    // Every lane, including invalid/tail/failed lanes, reaches every barrier.
    if(lid==0u){pass_count=0u;output_base=0u;}
    barrier();
    uint rank=0u;
    if(keep)rank=atomicAdd(pass_count,1u);
    barrier();
    if(lid==0u){
        atomicAdd(header.values[4],min(64u,batch_meta.values[3]-gl_WorkGroupID.x*64u));
        if(pass_count>0u){
            output_base=atomicAdd(header.values[2],pass_count);
            atomicAdd(header.values[6],1u);
            if(output_base+pass_count>batch_meta.values[5])atomicOr(header.values[3],4u);
        }
    }
    barrier();
    if(keep && output_base+rank<batch_meta.values[5])output_ordinals.values[output_base+rank]=ordinal;
}
