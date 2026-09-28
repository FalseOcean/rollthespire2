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
/*__C_HOT_DEFINES__*/
#define cr_evaluate_rewards cr_unused_baseline
/*__C_COMMON__*/
#undef cr_evaluate_rewards
/*__C_STREAMING__*/

bool c_route(uint64_t root,uint route){
    RngState unused=cr_rng_initialize(uint64_t(0));
    RewardRouteState state=cr_initialize_route(root,false,unused);
    if(plan_meta.values[40]!=0u){
        uint count=plan_meta.values[7];
        if(count<2u)return cr_family_numeric_failure();
        for(uint n=count;n>1u;--n)cr_next_int(state.rewards,n);
    }
    uint count=plan_meta.values[41];
    if(count>2u)return cr_family_numeric_failure();
    for(uint i=0u;i<count;++i){
        uint relic=plan_meta.values[42u+(route==0u?i:count-1u-i)];
        if(!cr_replay_query_literal_relic_consumption(relic,root,state))return cr_family_numeric_failure();
    }
    cr_apply_explicit_query_reward_context(state);
    return cr_evaluate_rewards(state);
}

void main(){
    uint index=gl_GlobalInvocationID.x,lid=gl_LocalInvocationID.x;
    bool valid=index<batch_meta.values[3],keep=false;
    uint ordinal=0u;
    if(valid){
        ordinal=batch_meta.values[4]!=0u?input_ordinals.values[index]:batch_meta.values[6]+index;
        if(ordinal>=batch_meta.values[2]||plan_meta.values[1]!=header.values[5]||batch_meta.values[7]<1u||batch_meta.values[7]>2u){
            atomicOr(header.values[3],2u);
        }else{
            uint64_t root=cr_hash_seed12(cr_make_u64(batch_meta.values[0],batch_meta.values[1])+uint64_t(ordinal));
#if RT2_CR_HOT_ENABLED == 1
            keep=cr_evaluate_rewards_unpinned_hot(root);
#else
            keep=c_route(root,0u);
            if(batch_meta.values[7]==2u){bool other=c_route(root,1u);keep=keep||other;}
#endif
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
