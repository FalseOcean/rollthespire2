#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require
layout(local_size_x=64,local_size_y=1,local_size_z=1) in;

const uint INVALID_DENSE_ID=0xffffu;
const uint OPTION_PLAN_STRIDE=16u;
const uint OPTION_POOL_STRIDE=4u;
const uint BRANCH_STRIDE=8u;
const uint GATE_STRIDE=4u;
const uint PARITY_STRIDE=12u;
const uint INPUT_ROOT_RANGE=0u;

layout(set=0,binding=0,std430) readonly restrict buffer BatchMeta{uint values[];} batch_meta;
layout(set=0,binding=1,std430) readonly restrict buffer PlanMeta{uint values[];} plan_meta;
layout(set=0,binding=2,std430) readonly restrict buffer GateMeta{uint values[];} gate_meta;
layout(set=0,binding=3,std430) readonly restrict buffer OptionPlans{uint values[];} option_plans;
layout(set=0,binding=4,std430) readonly restrict buffer OptionPools{uint values[];} option_pools;
layout(set=0,binding=5,std430) readonly restrict buffer OptionPoolIds{uint values[];} option_pool_ids;
layout(set=0,binding=6,std430) readonly restrict buffer OtherCharacterIds{uint values[];} other_character_ids;
layout(set=0,binding=7,std430) readonly restrict buffer BranchPredicates{uint values[];} branch_predicates;
layout(set=0,binding=8,std430) readonly restrict buffer BranchPredicateIds{uint values[];} branch_predicate_ids;
layout(set=0,binding=9,std430) readonly restrict buffer InputHeader{uint values[];} input_header;
layout(set=0,binding=10,std430) readonly restrict buffer InputOrdinals{uint values[];} input_ordinals;
layout(set=0,binding=11,std430) restrict buffer OptionScratch{uint values[];} option_scratch;
layout(set=0,binding=12,std430) restrict buffer OutputHeader{uint values[];} output_header;
layout(set=0,binding=13,std430) restrict buffer OutputOrdinals{uint values[];} output_ordinals;
layout(set=0,binding=14,std430) restrict buffer Parity{uint values[];} parity;

struct RngState{uint64_t s0;uint64_t s1;uint64_t s2;uint64_t s3;};
uint64_t make_u64(uint low,uint high){return uint64_t(low)|(uint64_t(high)<<32u);} 
uint64_t rotl64(uint64_t v,uint a){return(v<<a)|(v>>(64u-a));}
uint64_t splitmix_next(inout uint64_t state){state+=make_u64(0x7f4a7c15u,0x9e3779b9u);uint64_t v=state;v=(v^(v>>30u))*make_u64(0x1ce4e5b9u,0xbf58476du);v=(v^(v>>27u))*make_u64(0x133111ebu,0x94d049bbu);return v^(v>>31u);} 
RngState rng_initialize(uint64_t seed){uint64_t state=seed;RngState r;r.s0=splitmix_next(state);r.s1=splitmix_next(state);r.s2=splitmix_next(state);r.s3=splitmix_next(state);return r;}
uint64_t next_u64(inout RngState r){uint64_t outv=rotl64(r.s1*uint64_t(5u),7u)*uint64_t(9u);uint64_t t=r.s1<<17u;r.s2^=r.s0;r.s3^=r.s1;r.s1^=r.s2;r.s0^=r.s3;r.s2^=t;r.s3=rotl64(r.s3,45u);return outv;}
double next_double(inout RngState r){return double(next_u64(r)>>11u)*1.1102230246251565e-16lf;}
float next_float(inout RngState r){return float(next_double(r));}
uint next_int(inout RngState r,uint bound){return uint(next_double(r)*double(bound));}

uint alphabet_byte(uint digit){if(digit<10u)return 48u+digit;if(digit<18u)return 65u+(digit-10u);if(digit<23u)return 74u+(digit-18u);return 80u+(digit-23u);} 
void encode_seed12(uint64_t ordinal,out uint64_t first8,out uint tail4){uint d11=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d10=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d9=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d8=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d7=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d6=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d5=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d4=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d3=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d2=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d1=uint(ordinal%uint64_t(34u));ordinal/=uint64_t(34u);uint d0=uint(ordinal);first8=uint64_t(alphabet_byte(d0))|(uint64_t(alphabet_byte(d1))<<8u)|(uint64_t(alphabet_byte(d2))<<16u)|(uint64_t(alphabet_byte(d3))<<24u)|(uint64_t(alphabet_byte(d4))<<32u)|(uint64_t(alphabet_byte(d5))<<40u)|(uint64_t(alphabet_byte(d6))<<48u)|(uint64_t(alphabet_byte(d7))<<56u);tail4=alphabet_byte(d8)|(alphabet_byte(d9)<<8u)|(alphabet_byte(d10)<<16u)|(alphabet_byte(d11)<<24u);} 
uint64_t p1(){return make_u64(0x85ebca87u,0x9e3779b1u);}uint64_t p2(){return make_u64(0x27d4eb4fu,0xc2b2ae3du);}uint64_t p3(){return make_u64(0x9e3779f9u,0x165667b1u);}uint64_t p4(){return make_u64(0xc2b2ae63u,0x85ebca77u);}uint64_t p5(){return make_u64(0x165667c5u,0x27d4eb2fu);} 
uint64_t xxh_round(uint64_t a,uint64_t v){a+=v*p2();a=rotl64(a,31u);return a*p1();}
uint64_t hash_seed12(uint64_t ordinal){uint64_t f;uint t;encode_seed12(ordinal,f,t);uint64_t h=p5()+uint64_t(12u);h^=xxh_round(uint64_t(0u),f);h=rotl64(h,27u)*p1()+p4();h^=uint64_t(t)*p1();h=rotl64(h,23u)*p2()+p3();h^=h>>33u;h*=p2();h^=h>>29u;h*=p3();h^=h>>32u;return h;}

bool options_contains(uint o0,uint o1,uint o2,uint count,uint value){return(count>0u&&o0==value)||(count>1u&&o1==value)||(count>2u&&o2==value);} 
bool find_pool(uint plan_index,uint role,out uint value_offset,out uint value_count){uint pb=plan_index*OPTION_PLAN_STRIDE,pool_off=option_plans.values[pb+6u],pool_count=option_plans.values[pb+7u];for(uint i=0u;i<pool_count;++i){uint b=(pool_off+i)*OPTION_POOL_STRIDE;if(option_pools.values[b]!=role)continue;value_offset=option_pools.values[b+1u];value_count=option_pools.values[b+2u];return true;}value_offset=0u;value_count=0u;return false;}
uint append_pool(uint invocation,uint plan_index,uint role,uint write){uint off,count;if(!find_pool(plan_index,role,off,count))return write;uint base=invocation*plan_meta.values[3u];for(uint i=0u;i<count;++i)option_scratch.values[base+write+i]=option_pool_ids.values[off+i];return write+count;}
void shuffle_scratch(uint invocation,uint count,inout RngState rng){uint base=invocation*plan_meta.values[3u];for(uint n=count;n>1u;--n){uint s=next_int(rng,n),t=n-1u,tmp=option_scratch.values[base+s];option_scratch.values[base+s]=option_scratch.values[base+t];option_scratch.values[base+t]=tmp;}}
bool shuffle_take_one(uint invocation,uint plan_index,uint role,inout RngState rng,out uint value){uint off,count;if(!find_pool(plan_index,role,off,count)||count==0u){value=INVALID_DENSE_ID;return false;}uint base=invocation*plan_meta.values[3u];for(uint i=0u;i<count;++i)option_scratch.values[base+i]=option_pool_ids.values[off+i];shuffle_scratch(invocation,count,rng);value=option_scratch.values[base];return true;}

bool project_options(uint invocation,uint64_t root_hash,uint plan_index,out uint o0,out uint o1,out uint o2,out uint option_count,out uint sea_target,out uint flags){o0=INVALID_DENSE_ID;o1=INVALID_DENSE_ID;o2=INVALID_DENSE_ID;option_count=0u;sea_target=INVALID_DENSE_ID;flags=0u;if(plan_index==0xffffffffu)return false;uint pb=plan_index*OPTION_PLAN_STRIDE;uint generator=option_plans.values[pb+1u];if(generator==1u){uint off,count;if(!find_pool(plan_index,0u,off,count)||count!=1u)return false;o0=option_pool_ids.values[off];option_count=1u;flags=1u;return true;}uint64_t event_hash=make_u64(option_plans.values[pb+4u],option_plans.values[pb+5u]);uint raw_slot=option_plans.values[pb+2u];uint64_t slot=option_plans.values[pb+3u]!=0u?uint64_t(0u):((raw_slot&0x80000000u)!=0u?make_u64(raw_slot,0xffffffffu):uint64_t(raw_slot));RngState rng=rng_initialize(root_hash+slot+event_hash);uint base=invocation*plan_meta.values[3u];if(generator==2u){uint pool_off=option_plans.values[pb+6u],pool_count=option_plans.values[pb+7u],count=0u;for(uint i=0u;i<pool_count;++i){uint b=(pool_off+i)*OPTION_POOL_STRIDE;if(option_pools.values[b]!=1u)continue;uint off=option_pools.values[b+1u],len=option_pools.values[b+2u];if(len==0u)return false;option_scratch.values[base+count++]=option_pool_ids.values[off+next_int(rng,len)];}shuffle_scratch(invocation,count,rng);bool dusty=next_int(rng,2u)==0u;uint take=min(dusty?2u:3u,count);if(take>0u)o0=option_scratch.values[base];if(take>1u)o1=option_scratch.values[base+1u];if(take>2u)o2=option_scratch.values[base+2u];option_count=take;if(dusty){uint off,len;if(!find_pool(plan_index,2u,off,len)||len!=1u||option_count>=3u)return false;if(option_count==0u)o0=option_pool_ids.values[off];else if(option_count==1u)o1=option_pool_ids.values[off];else o2=option_pool_ids.values[off];option_count++;}}
else if(generator==3u){uint other_off=option_plans.values[pb+8u],other_count=option_plans.values[pb+9u];bool target_exact=false;if(other_count>0u){sea_target=other_character_ids.values[other_off+next_int(rng,other_count)];target_exact=option_plans.values[pb+11u]!=0u;}bool special=next_float(rng)<0.3333333f;uint off1,len1,off2,len2;if(!find_pool(plan_index,special?3u:4u,off1,len1)||!find_pool(plan_index,5u,off2,len2)||len1==0u||len2==0u)return false;uint third=0u;third=append_pool(invocation,plan_index,6u,third);third=append_pool(invocation,plan_index,7u,third);if(third==0u)return false;o0=option_pool_ids.values[off1+next_int(rng,len1)];o1=option_pool_ids.values[off2+next_int(rng,len2)];o2=option_scratch.values[base+next_int(rng,third)];option_count=3u;if(target_exact)flags|=4u;}
else if(generator==4u){uint off1,len1;if(!find_pool(plan_index,8u,off1,len1)||len1==0u)return false;o0=option_pool_ids.values[off1+next_int(rng,len1)];uint count=0u;count=append_pool(invocation,plan_index,9u,count);count=append_pool(invocation,plan_index,10u,count);count=append_pool(invocation,plan_index,11u,count);if(count==0u||count*2u>plan_meta.values[3u])return false;for(uint i=0u;i<count;++i)option_scratch.values[base+count+i]=option_scratch.values[base+i];uint second=count*2u;second=append_pool(invocation,plan_index,12u,second);if(second==0u)return false;o1=option_scratch.values[base+next_int(rng,second)];uint third=0u;third=append_pool(invocation,plan_index,13u,third);third=append_pool(invocation,plan_index,14u,third);if(third==0u)return false;o2=option_scratch.values[base+next_int(rng,third)];option_count=3u;}
else if(generator==5u){uint first=0u;first=append_pool(invocation,plan_index,15u,first);first=append_pool(invocation,plan_index,16u,first);uint off2,len2,off3,len3;if(first==0u||!find_pool(plan_index,17u,off2,len2)||!find_pool(plan_index,18u,off3,len3)||len2==0u||len3==0u)return false;o0=option_scratch.values[base+next_int(rng,first)];o1=option_pool_ids.values[off2+next_int(rng,len2)];o2=option_pool_ids.values[off3+next_int(rng,len3)];option_count=3u;}
else if(generator==6u||generator==7u){uint count=0u;count=append_pool(invocation,plan_index,generator==6u?19u:21u,count);count=append_pool(invocation,plan_index,generator==6u?20u:22u,count);if(count==0u)return false;shuffle_scratch(invocation,count,rng);option_count=min(3u,count);if(option_count>0u)o0=option_scratch.values[base];if(option_count>1u)o1=option_scratch.values[base+1u];if(option_count>2u)o2=option_scratch.values[base+2u];}
else if(generator==8u){if(!shuffle_take_one(invocation,plan_index,23u,rng,o0)||!shuffle_take_one(invocation,plan_index,24u,rng,o1)||!shuffle_take_one(invocation,plan_index,25u,rng,o2))return false;option_count=3u;}
else return false;uint sea_id=option_plans.values[pb+10u];bool visible=options_contains(o0,o1,o2,option_count,sea_id);flags|=1u;if(visible)flags|=2u;if(visible&&(flags&4u)!=0u)flags|=8u;return true;}

bool branch_any_contains(uint offset,uint count,uint value){for(uint i=0u;i<count;++i)if(branch_predicate_ids.values[offset+i]==value)return true;return false;}

void main(){
uint invocation=gl_GlobalInvocationID.x;
uint input_mode=batch_meta.values[3u];
uint count=input_mode==INPUT_ROOT_RANGE?batch_meta.values[2u]:min(input_header.values[batch_meta.values[5u]],batch_meta.values[4u]);
if(invocation>=count)return;
uint64_t ordinal=input_mode==INPUT_ROOT_RANGE?(make_u64(batch_meta.values[0u],batch_meta.values[1u])+uint64_t(invocation)):make_u64(input_ordinals.values[invocation*2u],input_ordinals.values[invocation*2u+1u]);
uint64_t root_hash=hash_seed12(ordinal);
bool keep=true;
for(uint gate=0u;gate<plan_meta.values[2u];++gate){
    uint gb=gate*GATE_STRIDE;
    uint act=gate_meta.values[gb];
    uint plan_index=gate_meta.values[gb+2u];
    uint branch_index=gate_meta.values[gb+3u];
    uint o0=INVALID_DENSE_ID,o1=INVALID_DENSE_ID,o2=INVALID_DENSE_ID,oc=0u,target=INVALID_DENSE_ID,pflags=0u;
    bool gate_keep=true;
    if(plan_index!=0xffffffffu){
        bool projection=project_options(invocation,root_hash,plan_index,o0,o1,o2,oc,target,pflags);
        if(projection){
            uint bb=branch_index*BRANCH_STRIDE;
            uint flags=branch_predicates.values[bb+5u];
            bool has_option=branch_predicates.values[bb+6u]!=0u;
            bool has_sea=branch_predicates.values[bb+7u]!=0u;
            if(has_option&&(flags&1u)!=0u){
                uint off=branch_predicates.values[bb+1u],n=branch_predicates.values[bb+2u];
                if((flags&4u)!=0u)gate_keep=false;
                else{
                    bool found=false;
                    for(uint i=0u;i<n;++i)found=found||options_contains(o0,o1,o2,oc,branch_predicate_ids.values[off+i]);
                    if(!found)gate_keep=false;
                }
            }
            if(gate_keep&&has_sea&&(flags&2u)!=0u){
                uint off=branch_predicates.values[bb+3u],n=branch_predicates.values[bb+4u];
                bool visible=(pflags&2u)!=0u,exact=(pflags&8u)!=0u;
                if((flags&8u)!=0u||!visible||!exact||!branch_any_contains(off,n,target))gate_keep=false;
            }
        }
    }
    if(invocation<plan_meta.values[5u]){
        uint parity_base=invocation*PARITY_STRIDE;
        if(act==2u){
            parity.values[parity_base]=o0;
            parity.values[parity_base+1u]=o1;
            parity.values[parity_base+2u]=o2;
            parity.values[parity_base+3u]=oc;
            parity.values[parity_base+4u]=target;
            parity.values[parity_base+5u]=pflags;
        }
        if(act==3u){
            uint act3_base=parity_base+6u;
            parity.values[act3_base]=o0;
            parity.values[act3_base+1u]=o1;
            parity.values[act3_base+2u]=o2;
            parity.values[act3_base+3u]=oc;
            parity.values[act3_base+4u]=target;
            parity.values[act3_base+5u]=pflags;
        }
    }
    if(!gate_keep){keep=false;break;}
    if(act==2u)atomicAdd(output_header.values[8u],1u);
    else if(act==3u)atomicAdd(output_header.values[9u],1u);
}
atomicAdd(output_header.values[7u],1u);
if(!keep)return;
uint slot=atomicAdd(output_header.values[4u],1u);
if(slot>=plan_meta.values[4u]){atomicExchange(output_header.values[5u],1u);atomicAdd(output_header.values[6u],1u);return;}
output_ordinals.values[slot*2u]=uint(ordinal&uint64_t(0xffffffffu));
output_ordinals.values[slot*2u+1u]=uint(ordinal>>32u);
}
