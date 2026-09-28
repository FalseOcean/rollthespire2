#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require
layout(local_size_x=64) in;
const uint INVALID_ID=0xffffffffu, ENCOUNTER_STRIDE=4u, PREDICATE_STRIDE=7u;
layout(set=0,binding=0,std430) readonly buffer Batch {uint v[];} batch;
layout(set=0,binding=1,std430) readonly buffer Buckets {uint lengths[];} relic_bucket_length_buffer;
layout(set=0,binding=2,std430) readonly buffer Meta {uint values[];} plan_meta_buffer;
layout(set=0,binding=3,std430) readonly buffer Selection {uvec4 groups[];} selection_group_meta_buffer;
layout(set=0,binding=4,std430) readonly buffer SelectionIds {uint ids[];} selection_id_buffer;
layout(set=0,binding=5,std430) readonly buffer Map {uint indices[];} act_index_map_buffer;
layout(set=0,binding=6,std430) readonly buffer Acts {uint values[];} act_meta_buffer;
layout(set=0,binding=7,std430) readonly buffer Encounters {uint values[];} encounter_buffer;
layout(set=0,binding=8,std430) readonly buffer Conflicts {uint ordinals[];} conflict_buffer;
layout(set=0,binding=9,std430) readonly buffer Bosses {uint ids[];} boss_id_buffer;
layout(set=0,binding=10,std430) readonly buffer Predicates {uint values[];} predicate_buffer;
layout(set=0,binding=11,std430) readonly buffer Targets {uint ids[];} predicate_id_buffer;
layout(set=0,binding=12,std430) buffer Scratch {uint entries[];} encounter_scratch_buffer;
layout(set=0,binding=13,std430) readonly buffer Extra {uint v[];} extra;
layout(set=0,binding=14,std430) readonly buffer Data {uint v[];} data;
layout(set=0,binding=15,std430) buffer Reserved {uint v[];} reserved;
layout(set=0,binding=16,std430) buffer Header {uint v[];} header;
layout(set=0,binding=17,std430) buffer Output {uint v[];} output_ids;
layout(set=0,binding=18,std430) readonly buffer Input {uint v[];} input_ids;
/*__W_DONOR__*/
/*__RT2_VISIBLE_SEED_ROOT_HASH__*/

bool fault(uint code){atomicOr(header.v[3],code);return false;}
bool branch_match(uint x,uint first,uint second,bool both){
    uint count=extra.v[x+10u]; if(count==0u)return true;
    uint offset=extra.v[x+9u];
    bool conjunction=act_meta_buffer.values[(x/12u)*24u+22u]!=0u;
    for(uint i=0u;i<count;++i){
        bool matched=evaluate_predicate(data.v[offset+i*2u],first,INVALID_ID,1u)&&
            (!both||evaluate_predicate(data.v[offset+i*2u+1u],second,INVALID_ID,1u));
        if(conjunction&&!matched)return false;
        if(!conjunction&&matched)return true;
    }
    return conjunction;
}
bool event_contains(uint base,uint count,uint mode,uint limit,uint source,uint target){
    for(uint i=0u;i<count;++i){
        uint value=encounter_scratch_buffer.entries[base+i];
        if((mode==0u?i<limit:i+1u==limit)&&(source==255u||((value>>16u)&255u)==source)&&
            (value&65535u)==target)return true;
    }return false;
}
bool events_match(uint invocation,uint x){
    uint count=extra.v[x+2u],offset=extra.v[x+1u],horizon=extra.v[x+3u];
    // Separate, bounded raw scratch after the reusable encounter scratch.
    uint base=batch.v[5]*plan_meta_buffer.values[8u]+invocation*plan_meta_buffer.values[13u];
    for(uint i=0u;i<count;++i)encounter_scratch_buffer.entries[base+i]=data.v[offset+i];
    for(uint n=count;n>1u;--n){uint i=next_int(n),a=encounter_scratch_buffer.entries[base+i];
        encounter_scratch_buffer.entries[base+i]=encounter_scratch_buffer.entries[base+n-1u];
        encounter_scratch_buffer.entries[base+n-1u]=a;}
    uint effective=0u;
    // RAW skip precedes cleaning. In-place compaction cannot overwrite unread raw entries.
    for(uint i=1u;i<count&&effective<horizon;++i){uint v=encounter_scratch_buffer.entries[base+i];
        if((v&(1u<<24u))!=0u)encounter_scratch_buffer.entries[base+effective++]=v;}
    for(uint p=0u;p<extra.v[x+5u];++p){
        uint e=extra.v[x+4u]+p*4u,mode=data.v[e],limit=data.v[e+1u],source=data.v[e+2u],b=data.v[e+3u]*7u;
        if(predicate_buffer.values[b]!=0u)return false;
        if(mode==1u&&(effective<limit||(source!=255u&&((encounter_scratch_buffer.entries[base+limit-1u]>>16u)&255u)!=source)))return false;
        for(uint lane=0u;lane<3u;++lane){uint off=predicate_buffer.values[b+1u+lane*2u],n=predicate_buffer.values[b+2u+lane*2u];bool found=false;
            for(uint i=0u;i<n;++i){bool hit=event_contains(base,effective,mode,limit,source,predicate_id_buffer.ids[off+i]);
                found=found||hit;if(lane==1u&&!hit)return false;if(lane==2u&&hit)return false;}
            if(lane==0u&&n>0u&&!found)return false;}
    }return true;
}
bool world_match(uint invocation,uint64_t root){
    uint selected[8],assignedOffset[8],assignedCount[8],firstBoss[8];
    uint groups=plan_meta_buffer.values[2u];
    rng_initialize(root+make_u64(0x9dc62d50u,0x79a5d529u));
    for(uint i=0u;i<groups;++i){uvec4 g=selection_group_meta_buffer.groups[i];
        uint id=selection_id_buffer.ids[g.x+(g.z==1u?0u:next_int(g.y))];
        if(i==0u&&plan_meta_buffer.values[11u]!=INVALID_ID)id=plan_meta_buffer.values[11u];
        if(id>=plan_meta_buffer.values[3u])return fault(1u);
        uint a=act_index_map_buffer.indices[id];
        if(a>=plan_meta_buffer.values[4u])return fault(2u);
        selected[i]=a;assignedOffset[i]=0u;assignedCount[i]=0u;firstBoss[i]=INVALID_ID;
        if(extra.v[a*12u]==0u)return false;
    }
    uint maxAct=plan_meta_buffer.values[6u];
    if(maxAct==0u)return true;
    rng_initialize(root+make_u64(0xda243e80u,0x1a8a7b60u));
    for(uint i=0u;i<plan_meta_buffer.values[1u];++i)consume_shuffle(relic_bucket_length_buffer.lengths[i]);
    uint sharedIds[512],sharedCount=plan_meta_buffer.values[5u];
    for(uint i=0u;i<sharedCount;++i)sharedIds[i]=data.v[plan_meta_buffer.values[14u]+i];
    for(uint n=sharedCount;n>1u;--n){uint i=next_int(n),v=sharedIds[i];sharedIds[i]=sharedIds[n-1u];sharedIds[n-1u]=v;}
    uint used=0u;
    for(uint i=1u;i<groups;++i){uint n=next_int(sharedCount-used+1u);assignedOffset[i]=used;assignedCount[i]=n;used+=n;}
    bool second=plan_meta_buffer.values[7u]!=0u;
    for(uint i=0u;i<groups;++i){uint a=selected[i],b=a*24u,x=a*12u,act=act_meta_buffer.values[b];
        if(!second&&act>maxAct)break;
        if(extra.v[x+5u]>0u){
            if(act_meta_buffer.values[b+20u]+assignedCount[i]==0u)return fault(4u);
            if(!events_match(invocation,x))return false;
        }else consume_shuffle(act_meta_buffer.values[b+2u]);
        bool hasBoss=extra.v[x+11u]!=0u,hasAncient=extra.v[x+8u]>0u;
        if(!(act<maxAct||hasBoss||hasAncient||second))continue;
        bool previous=false;uint source=0u,reference=0u;
        consume_encounter_queue(invocation,act_meta_buffer.values[b+3u],act_meta_buffer.values[b+4u],act_meta_buffer.values[b+5u],previous,source,reference);
        consume_encounter_queue(invocation,act_meta_buffer.values[b+6u],act_meta_buffer.values[b+7u],act_meta_buffer.values[b+8u],previous,source,reference);
        previous=false;source=0u;reference=0u;
        consume_encounter_queue(invocation,act_meta_buffer.values[b+9u],act_meta_buffer.values[b+10u],act_meta_buffer.values[b+11u],previous,source,reference);
        uint bossCount=act_meta_buffer.values[b+13u];if(bossCount==0u)return fault(8u);
        uint first=boss_id_buffer.ids[act_meta_buffer.values[b+12u]+next_int(bossCount)];
        if(act_meta_buffer.values[b+21u]!=INVALID_ID)first=act_meta_buffer.values[b+21u];firstBoss[i]=first;
        if(!branch_match(x,first,INVALID_ID,false))return false;
        if(!evaluate_predicate_range(act_meta_buffer.values[b+16u],act_meta_buffer.values[b+17u],first,INVALID_ID,1u))return false;
        if(!(plan_meta_buffer.values[12u]>=10u&&act==3u)&&!evaluate_predicate_range(act_meta_buffer.values[b+14u],act_meta_buffer.values[b+15u],first,INVALID_ID,1u))return false;
        if(!(act<maxAct||hasAncient||second))continue;
        uint localCount=act_meta_buffer.values[b+20u],total=localCount+assignedCount[i];if(total==0u)return fault(16u);
        uint rank=next_int(total),ancient=rank<localCount?data.v[extra.v[x+6u]+rank]:sharedIds[assignedOffset[i]+rank-localCount];
        if(!evaluate_predicate_range(extra.v[x+7u],extra.v[x+8u],ancient,INVALID_ID,1u))return false;
    }
    if(second){uint i=groups-1u,a=selected[i],b=a*24u,x=a*12u,count=act_meta_buffer.values[b+13u],first=firstBoss[i];
        if(first==INVALID_ID)return fault(32u);
        uint secondCount=0u;
        for(uint j=0u;j<count;++j)if(boss_id_buffer.ids[act_meta_buffer.values[b+12u]+j]!=first)secondCount++;
        if(secondCount==0u)return fault(32u);
        uint rank=next_int(secondCount),secondBoss=INVALID_ID;
        for(uint j=0u;j<count;++j){uint v=boss_id_buffer.ids[act_meta_buffer.values[b+12u]+j];if(v==first)continue;if(rank==0u){secondBoss=v;break;}rank--;}
        if(secondBoss==INVALID_ID)return fault(64u);
        if(!branch_match(x,first,secondBoss,true))return false;
        if(!evaluate_predicate_range(act_meta_buffer.values[b+18u],act_meta_buffer.values[b+19u],secondBoss,INVALID_ID,1u)||
            !evaluate_predicate_range(act_meta_buffer.values[b+14u],act_meta_buffer.values[b+15u],first,secondBoss,2u))return false;
    }return true;
}
void main(){
    uint i=gl_GlobalInvocationID.x;if(i>=batch.v[3])return;
    atomicAdd(header.v[4],1u);
    uint ordinal=batch.v[4]==0u?batch.v[6]+i:input_ids.v[i];
    if(ordinal>=batch.v[2]){fault(128u);return;}
    if(world_match(i,root_hash_for_ordinal(make_u64(batch.v[0],batch.v[1])+uint64_t(ordinal)))){
        uint p=atomicAdd(header.v[2],1u);if(p>=batch.v[5]){fault(256u);return;}output_ids.v[p]=ordinal;
    }
}
