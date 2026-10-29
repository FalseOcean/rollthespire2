#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require
layout(local_size_x=64) in;
const uint SEED_LENGTH=12u,RADIX=34u;
layout(set=0,binding=0,std430) readonly buffer Batch {uint v[];} batch;
layout(set=0,binding=1,std430) readonly buffer Reserved1 {uint v[];} reserved1;
layout(set=0,binding=2,std430) readonly buffer Meta {uint v[];} plan;
layout(set=0,binding=3,std430) readonly buffer Groups {uvec4 v[];} groups;
layout(set=0,binding=4,std430) readonly buffer Allowed {uint v[];} allowed;
layout(set=0,binding=5,std430) readonly buffer Reserved5 {uint v[];} reserved5;
layout(set=0,binding=6,std430) readonly buffer Reserved6 {uint v[];} reserved6;
layout(set=0,binding=7,std430) readonly buffer Reserved7 {uint v[];} reserved7;
layout(set=0,binding=8,std430) readonly buffer Reserved8 {uint v[];} reserved8;
layout(set=0,binding=9,std430) readonly buffer Reserved9 {uint v[];} reserved9;
layout(set=0,binding=10,std430) readonly buffer Reserved10 {uint v[];} reserved10;
layout(set=0,binding=11,std430) readonly buffer Reserved11 {uint v[];} reserved11;
layout(set=0,binding=12,std430) buffer Reserved12 {uint v[];} reserved12;
layout(set=0,binding=13,std430) readonly buffer Reserved13 {uint v[];} reserved13;
layout(set=0,binding=14,std430) readonly buffer Reserved14 {uint v[];} reserved14;
layout(set=0,binding=15,std430) buffer Reserved15 {uint v[];} reserved15;
layout(set=0,binding=16,std430) buffer Header {uint v[];} header;
layout(set=0,binding=17,std430) buffer Output {uint v[];} output_ids;
layout(set=0,binding=18,std430) readonly buffer Input {uint v[];} input_ids;
/*__RNG_HASH__*/
float next_float(){return float(double(next_u64()>>11u)*1.1102230246251565e-16lf);}
bool unknown_match(uint64_t root){
 if(plan.v[1]==0u)return true;
 rng_initialize(root+make_u64(plan.v[5],plan.v[6]));
 precise float monster=.1,treasure=.02,shop=.03;
 for(uint i=0u;i<plan.v[1];i++){
  precise float total=monster+treasure;total+=shop;
  if(next_float()<=total)return false;
  monster+=.1;treasure+=.02;shop+=.03;
 }
 return true;
}
bool variant_match(uint64_t root){
 if(plan.v[0]==0u)return true;
 rng_initialize(root+make_u64(plan.v[3],plan.v[4]));
 for(uint i=0u;i<plan.v[0];i++){
  uvec4 group=groups.v[i];
  uint selected=group.z==1u?0u:next_int(group.y);
  // Override is applied after the draw, including one-member random groups.
  bool pass=group.w==0u?allowed.v[group.x+selected]!=0u:group.w==2u;
  if(!pass)return false;
 }
 return true;
}
bool matches(uint64_t root){
 if(plan.v[2]!=0u)return unknown_match(root)&&variant_match(root);
 return variant_match(root)&&unknown_match(root);
}
void main(){
 uint i=gl_GlobalInvocationID.x;if(i>=batch.v[3])return;
 atomicAdd(header.v[4],1u);
 uint ordinal=batch.v[4]==0u?batch.v[6]+i:input_ids.v[i];
 if(ordinal>=batch.v[2]){atomicOr(header.v[3],128u);return;}
 if(matches(root_hash_for_ordinal(make_u64(batch.v[0],batch.v[1])+uint64_t(ordinal)))){
  uint p=atomicAdd(header.v[2],1u);
  if(p>=batch.v[5]){atomicOr(header.v[3],256u);return;}output_ids.v[p]=ordinal;
 }
}
