#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require
layout(local_size_x=64) in;
const uint INVALID_DENSE_ID=65535u,OPTION_PLAN_STRIDE=16u,OPTION_POOL_STRIDE=4u,BRANCH_STRIDE=8u,GATE_STRIDE=4u;
layout(set=0,binding=0,std430) readonly buffer Batch{uint v[];} batch;
layout(set=0,binding=1,std430) readonly buffer Meta{uint values[];} plan_meta;
layout(set=0,binding=2,std430) readonly buffer Gates{uint values[];} gate_meta;
layout(set=0,binding=3,std430) readonly buffer Plans{uint values[];} option_plans;
layout(set=0,binding=4,std430) readonly buffer Pools{uint values[];} option_pools;
layout(set=0,binding=5,std430) readonly buffer PoolIds{uint values[];} option_pool_ids;
layout(set=0,binding=6,std430) readonly buffer Others{uint values[];} other_character_ids;
layout(set=0,binding=7,std430) readonly buffer Branches{uint values[];} branch_predicates;
layout(set=0,binding=8,std430) readonly buffer BranchIds{uint values[];} branch_predicate_ids;
layout(set=0,binding=9,std430) buffer Scratch{uint values[];} option_scratch;
layout(set=0,binding=10,std430) readonly buffer Input{uint v[];} input_ids;
layout(set=0,binding=11,std430) buffer Header{uint v[];} header;
layout(set=0,binding=12,std430) buffer Output{uint v[];} output_ids;
/*__DONOR__*/
bool row_match(uint i,uint64_t root,uint gate){
 uint gb=gate*4u,p=gate_meta.values[gb+2u],bb=gate_meta.values[gb+3u]*8u;
 bool has_option=branch_predicates.values[bb+6u]!=0u,has_sea=branch_predicates.values[bb+7u]!=0u;
 if(!has_option&&!has_sea)return true;
 uint o0,o1,o2,count,target,flags;
 if(!project_options(i,root,p,o0,o1,o2,count,target,flags)){atomicOr(header.v[3],4u);return false;}
 uint f=branch_predicates.values[bb+5u];
 if(has_option){
  if((f&4u)!=0u)return false;
  bool found=false;uint off=branch_predicates.values[bb+1u],n=branch_predicates.values[bb+2u];
  for(uint j=0u;j<n;++j)found=found||options_contains(o0,o1,o2,count,branch_predicate_ids.values[off+j]);
  if(!found)return false;
 }
 if(has_sea){
  if((f&8u)!=0u||(flags&8u)==0u)return false;
  if(!branch_any_contains(branch_predicates.values[bb+3u],branch_predicates.values[bb+4u],target))return false;
 }
 return true;
}
bool matches(uint i,uint64_t root){
 uint g=0u,total=plan_meta.values[2u];
 while(g<total){
  uint act=gate_meta.values[g*4u];bool pass=false;
  do {if(!pass)pass=row_match(i,root,g);g++;} while(g<total&&gate_meta.values[g*4u]==act);
  if(!pass)return false;
 }
 return true;
}
void main(){
 uint i=gl_GlobalInvocationID.x;if(i>=batch.v[3])return;
 atomicAdd(header.v[4],1u);
 uint ordinal=batch.v[4]==0u?batch.v[6]+i:input_ids.v[i];
 if(ordinal>=batch.v[2]){atomicOr(header.v[3],1u);return;}
 if(matches(i,hash_seed12(make_u64(batch.v[0],batch.v[1])+uint64_t(ordinal)))){
  uint p=atomicAdd(header.v[2],1u);
  if(p>=batch.v[5]){atomicOr(header.v[3],2u);return;}output_ids.v[p]=ordinal;
 }
}
