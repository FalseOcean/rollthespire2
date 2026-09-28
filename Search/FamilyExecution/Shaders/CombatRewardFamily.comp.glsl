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
/*__C_STREAMING__*/

#if RT2_C_CAPSULE_HELD == 1
uint c_initialize_bag(uint64_t root,out uint bag[512]){
    RngState upfront=cr_rng_initialize(root+cr_plan_u64(18u));
    uint prefix=pool_meta.values[41u];
    for(uint b=0u;b<pool_meta.values[42u];++b)
        for(uint n=dense_ids.ids[prefix+b];n>1u;--n)cr_next_double(upfront);
    uint total=0u,bucket_base=pool_meta.values[45u];
    for(uint b=0u;b<pool_meta.values[43u];++b){
        uint source=pool_meta.values[bucket_base+b*2u],count=pool_meta.values[bucket_base+b*2u+1u];
        if(total+count>512u)return 0xffffffffu;
        for(uint i=0u;i<count;++i)bag[total+i]=dense_ids.ids[source+i];
        for(uint n=count;n>1u;--n){uint at=total+cr_next_int(upfront,n),last=total+n-1u;
            uint v=bag[at];bag[at]=bag[last];bag[last]=v;}
        total+=count;
    }
    return total;
}
uint c_pull_relic(inout RewardRouteState state,inout uint bag[512],uint count){
    float roll=cr_next_float(state.rewards);uint rarity=roll<0.5f?1u:(roll<0.83f?2u:3u);
    uint base=pool_meta.values[46u];
    for(;rarity<=3u;++rarity)for(uint i=0u;i<count;++i){uint index=bag[i];
        if(index!=0xffffffffu&&pool_meta.values[base+index*4u+1u]==rarity){bag[i]=0xffffffffu;return index;}}
    return 0xffffffffu;
}
#endif

bool c_route(uint64_t root,uint route){
    RngState unused=cr_rng_initialize(uint64_t(0));
    RewardRouteState state=cr_initialize_route(root,false,unused);
#if RT2_C_MULTIPLAYER
    bool niche_known=plan_meta.values[44]!=0xffffffffu;
    if(niche_known)for(uint draw=0u;draw<plan_meta.values[44];++draw)cr_next_double(state.niche);
    uint bones_pool[32];
#else
    bool niche_known=true;
#endif
#if RT2_C_CAPSULE_HELD == 1
    uint bag[512],bag_count=0xffffffffu;
#endif
#if RT2_C_MULTIPLAYER
    bool actual_bones=plan_meta.values[51]!=0u;
#endif
    if(plan_meta.values[40]!=0u){
        uint count=plan_meta.values[7];
        if(count<2u)return cr_family_numeric_failure();
#if RT2_C_MULTIPLAYER
        if(actual_bones){
            if(count>32u)return cr_family_numeric_failure();
            for(uint i=0u;i<count;++i)bones_pool[i]=plan_meta.values[plan_meta.values[52]+i];
            for(uint n=count;n>1u;--n){uint at=cr_next_int(state.rewards,n),last=n-1u;
                uint v=bones_pool[at];bones_pool[at]=bones_pool[last];bones_pool[last]=v;}
        }else
#endif
        for(uint n=count;n>1u;--n)cr_next_int(state.rewards,n);
    }
#if RT2_C_MULTIPLAYER
    uint count=actual_bones?2u:plan_meta.values[41];
#else
    uint count=plan_meta.values[41];
#endif
    if(count>2u)return cr_family_numeric_failure();
    for(uint i=0u;i<count;++i){
        uint at=route==0u?i:count-1u-i;
#if RT2_C_MULTIPLAYER
        uint relic=actual_bones?bones_pool[at]:plan_meta.values[42u+at];
#else
        uint relic=plan_meta.values[42u+at];
#endif
#if RT2_C_CAPSULE_HELD == 1
        if(relic==3u||relic==28u){
            if(bag_count==0xffffffffu){bag_count=c_initialize_bag(root,bag);if(bag_count==0xffffffffu)return true;}
            uint pulls=relic==3u?2u:1u;
            for(uint pull=0u;pull<pulls;++pull){
                uint index=c_pull_relic(state,bag,bag_count);
                if(index==0xffffffffu)continue;
                uint capability=pool_meta.values[pool_meta.values[46u]+index*4u+2u];
                // Only held impact matters for unlisted relics; obtain hooks are
                // authored separately. Unsupported held draws conservatively keep this root.
                if((capability&2u)==0u)return true;
                if((capability&4u)!=0u)continue;
                state.influence_flags|=((capability>>8u)&0xffffu)&~128u;
                state.additional_card_rewards+=capability>>24u;
                state.fixed_gold+=pool_meta.values[pool_meta.values[46u]+index*4u+3u];
            }
        }else
#endif
        if(relic==14u&&plan_meta.values[49]!=0u){
            for(uint group=0u;group<2u;++group){
                if(niche_known)for(uint n=pool_meta.values[40];n>1u;--n)cr_next_double(state.niche);
                for(uint draw=0u;draw<9u;++draw)cr_next_double(state.rewards);
            }
        }else{
            if(!cr_replay_query_literal_relic_consumption(relic,root,state))return cr_family_numeric_failure();
            if(relic==19u&&niche_known)cr_next_double(state.niche);
        }
#if RT2_C_MULTIPLAYER
        uint advance=actual_bones?(plan_meta.values[53]!=0u&&(relic==3u||relic==28u)?0xffffffffu:0u):plan_meta.values[45u+route*2u+i];
#else
        uint advance=plan_meta.values[45u+route*2u+i];
#endif
        if(advance==0xffffffffu)niche_known=false;
        else if(niche_known)for(uint draw=0u;draw<advance;++draw)cr_next_double(state.niche);
    }
    if(plan_meta.values[40]!=0u&&niche_known)cr_next_double(state.niche);
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
            if(plan_meta.values[50]!=0u)keep=true;
            else{
#if RT2_CR_HOT_ENABLED == 1
            keep=cr_evaluate_rewards_unpinned_hot(root);
#else
            keep=c_route(root,0u);
            if(batch_meta.values[7]==2u){bool other=c_route(root,1u);keep=keep||other;}
#endif
            }
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
