#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require
layout(local_size_x=64) in;
const uint INVALID_ID=65535u, SEED_LENGTH=12u, RADIX=34u;
layout(set=0,binding=0,std430) readonly buffer Batch {uint v[];} batch;
layout(set=0,binding=1,std430) readonly buffer Plan {uint values[];} event_plan_buffer;
layout(set=0,binding=2,std430) readonly buffer Colors {uint ids[];} colorful_pool_buffer;
layout(set=0,binding=3,std430) readonly buffer Input {uint v[];} input_ids;
layout(set=0,binding=4,std430) buffer Header {uint v[];} header;
layout(set=0,binding=5,std430) buffer Output {uint v[];} output_ids;
/*__DONOR__*/
void main(){
 uint i=gl_GlobalInvocationID.x;if(i>=batch.v[3])return;
 atomicAdd(header.v[4],1u);
 uint ordinal=batch.v[4]==0u?batch.v[6]+i:input_ids.v[i];
 if(ordinal>=batch.v[2]){atomicOr(header.v[3],1u);return;}
 if(evaluate_event_results(root_hash_for_ordinal(make_u64(batch.v[0],batch.v[1])+uint64_t(ordinal)))){
  uint p=atomicAdd(header.v[2],1u);
  if(p>=batch.v[5]){atomicOr(header.v[3],2u);return;}output_ids.v[p]=ordinal;
 }
}
