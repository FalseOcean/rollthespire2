#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require
layout(local_size_x=64) in;
const uint SEED_LENGTH=12u,RADIX=34u;
layout(set=0,binding=0,std430) readonly buffer Batch {uint v[];} batch;
layout(set=0,binding=1,std430) readonly buffer Plan {uint v[];} plan;
layout(set=0,binding=2,std430) readonly buffer Pools {uint v[];} pools;
layout(set=0,binding=3,std430) readonly buffer Input {uint v[];} input_ids;
layout(set=0,binding=4,std430) buffer Header {uint v[];} header;
layout(set=0,binding=5,std430) buffer Output {uint v[];} output_ids;
/*__RNG__*/
// N-owned offer/pair replay. Only the bounded identity obligation is used here.
// The accepted transform streams start independently; no state crosses ABI1.
bool opening(uint64_t root){
 uint mode=plan.v[0];if(mode==0u)return true;
 rng_initialize(root+make_u64(plan.v[13],plan.v[14]));
 uint ci=next_int(plan.v[7]),curse=pools.v[plan.v[8]+ci];
#ifdef T_FULL_TARGET
 return curse==plan.v[18];
#else
 if(mode==1u)return curse==plan.v[18];
 uint a[32];uint count;
 if(mode==3u){
  if(curse!=plan.v[19])return false;
  rng_initialize(root+make_u64(plan.v[15],plan.v[16]));
  count=plan.v[9];for(uint i=0u;i<count;i++)a[i]=pools.v[plan.v[10]+i];
 }else{
  uint at=plan.v[11]+ci*2u;count=plan.v[at+1u];
  for(uint i=0u;i<count;i++)a[i]=pools.v[plan.v[at]+i];
  if(curse!=plan.v[17])a[count++]=next_int(2u)==0u?plan.v[21]:plan.v[22];
  a[count++]=next_int(2u)==0u?plan.v[23]:plan.v[24];
  a[count++]=next_int(2u)==0u?plan.v[25]:plan.v[26];
 }
 for(uint n=count;n>1u;n--){uint j=next_int(n),t=a[j];a[j]=a[n-1u];a[n-1u]=t;}
 return mode==3u ? ((a[0]==plan.v[18]&&a[1]==plan.v[20])||(a[1]==plan.v[18]&&a[0]==plan.v[20]))
                 : a[0]==plan.v[20]||a[1]==plan.v[20];
#endif
}
bool aggregate(uint64_t root){
#ifdef T_FULL_TARGET
 // Full multiset: each draw must fill one unused target slot. No assignment
 // of a specific pair to Leafy; any target identities and duplicates are legal.
 uint matched=0u;
 for(uint g=0u;g<plan.v[5];g++){
  uint at=plan.v[6]+g*8u;rng_initialize(root+make_u64(plan.v[at],plan.v[at+1u]));
  if(plan.v[at+2u]==1u)next_int(19u);
  if(plan.v[at+2u]==2u && next_int(3u)!=2u)return false;
  for(uint j=0u;j<plan.v[at+3u];j++){
   uint pi=at+4u+j*2u;
   uint available=pools.v[plan.v[pi]+next_int(plan.v[pi+1u])]&~matched;
   if(available==0u)return false;
   matched|=1u<<uint(findLSB(available));
  }
  if(g==0u && !opening(root))return false;
 }
 return true;
#else
 if(!opening(root))return false;
 uint rare=0u,matched=0u,consumed=0u,total=plan.v[3];
 for(uint g=0u;g<plan.v[5];g++){
  uint at=plan.v[6]+g*8u;rng_initialize(root+make_u64(plan.v[at],plan.v[at+1u]));
  if(plan.v[at+2u]==1u)next_int(19u);
  if(plan.v[at+2u]==2u && next_int(3u)!=2u)return false;
  for(uint j=0u;j<plan.v[at+3u];j++){
   uint pi=at+4u+j*2u;uint value=pools.v[plan.v[pi]+next_int(plan.v[pi+1u])];consumed++;
   if(plan.v[1]==0u){rare+=value;if(rare+total-consumed<plan.v[2])return false;}
   else{uint available=(value&0x7fffffffu)&~matched;if(available!=0u)matched|=1u<<uint(findLSB(available));
        else if(plan.v[1]==2u && (value&0x80000000u)==0u)return false;
        if(uint(bitCount(matched))+total-consumed<plan.v[4])return false;}
  }
 }
 return plan.v[1]==0u?rare>=plan.v[2]:uint(bitCount(matched))>=plan.v[4];
#endif
}
uint ordinal_at(uint i){return batch.v[4]==0u?batch.v[6]+i:input_ids.v[i];}
void main(){
#ifdef T_FULL_TARGET
 uint first=gl_GlobalInvocationID.x*8u;
 if(first>=batch.v[3])return;
 if(gl_LocalInvocationIndex==0u)atomicAdd(header.v[4],min(512u,batch.v[3]-first));
 uint64_t base=make_u64(batch.v[0],batch.v[1]);
 uint64_t seed8;uint tail;
 if(batch.v[4]==0u)encode_seed12_packed(base+uint64_t(batch.v[6]+first),seed8,tail);
 for(uint lane=0u;lane<8u && first+lane<batch.v[3];lane++){
  uint i=first+lane;
  uint ordinal=ordinal_at(i);
  if(ordinal>=batch.v[2]){atomicOr(header.v[3],1u);return;}
  uint64_t root=batch.v[4]==0u?xxhash64_seed12_packed(seed8,tail):root_hash_for_ordinal(base+uint64_t(ordinal));
  if(aggregate(root)){
   uint p=atomicAdd(header.v[2],1u);if(p>=batch.v[5]){atomicOr(header.v[3],2u);return;}output_ids.v[p]=ordinal;
  }
  if(batch.v[4]==0u && lane<7u)advance_seed12_packed(seed8,tail);
 }
#else
 uint i=gl_GlobalInvocationID.x;if(i>=batch.v[3])return;
 atomicAdd(header.v[4],1u);
 uint ordinal=ordinal_at(i);
 if(ordinal>=batch.v[2]){atomicOr(header.v[3],1u);return;}
 if(aggregate(root_hash_for_ordinal(make_u64(batch.v[0],batch.v[1])+uint64_t(ordinal)))){
  uint p=atomicAdd(header.v[2],1u);if(p>=batch.v[5]){atomicOr(header.v[3],2u);return;}output_ids.v[p]=ordinal;
 }
#endif
}
