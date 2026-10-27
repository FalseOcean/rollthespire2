#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_EXT_shader_explicit_arithmetic_types_float64 : require
layout(local_size_x=64) in;
const uint SEED_LENGTH=12u, RADIX=34u;
layout(set=0,binding=0,std430) readonly buffer Batch {uint v[];} batch;
layout(set=0,binding=1,std430) readonly buffer Plan {uint v[];} plan;
layout(set=0,binding=2,std430) readonly buffer Reserved {uint v[];} reserved;
layout(set=0,binding=3,std430) readonly buffer Input {uint v[];} input_ids;
layout(set=0,binding=4,std430) buffer Header {uint v[];} header;
layout(set=0,binding=5,std430) buffer Output {uint v[];} output_ids;
/*__RNG_HASH__*/
float next_float(){return float(double(next_u64()>>11u)*1.1102230246251565e-16lf);}
uint swap_position(uint p,uint selected,uint tail){return p==selected?tail:p==tail?selected:p;}
bool matches(uint64_t root){
 if(plan.v[0]>0u){
  rng_initialize(root+make_u64(plan.v[8],plan.v[9]));
  precise float monster=.1,treasure=.02,shop=.03;
  for(uint i=0u;i<plan.v[0];i++){
   precise float total=monster+treasure;total+=shop;
   if(next_float()<=total)return false;
   monster+=.1;treasure+=.02;shop+=.03;
  }
 }
 if(plan.v[1]==0u)return true;
 uint first=plan.v[2]-1u,end=first+plan.v[3],mask=plan.v[5];
 uint lanes[2],ranks[2],taken[3];for(uint i=0u;i<3u;i++)taken[i]=0u;
 rng_initialize(root+make_u64(plan.v[10],plan.v[11]));
 for(uint i=0u;i<end;i++){
  float roll=next_float();uint lane=roll<.5?0u:roll<.83?1u:2u;
  while(lane<3u&&taken[lane]==plan.v[14u+lane])lane++;
  lanes[i]=lane;ranks[i]=lane==3u?0u:taken[lane]++;
 }
 for(uint slot=0u;slot<plan.v[3];slot++){
  if((mask&(1u<<slot))==0u)continue;
  bool possible=false;
  for(uint i=first;i<end;i++)
   if((plan.v[4]==1u||i==first+slot)&&
    (lanes[i]==plan.v[17u+slot]||(lanes[i]==3u&&(plan.v[25]&(1u<<slot))!=0u)))possible=true;
  if(!possible)return false;
 }
 uint positions[2];positions[0]=plan.v[19];positions[1]=plan.v[20];
 rng_initialize(root+make_u64(plan.v[12],plan.v[13]));
 // UINT_MAX encodes no observed shuffle bucket (e.g. only exhausted Circlet).
 for(uint bucket=0u;bucket<plan.v[6]&&bucket<=plan.v[7]&&plan.v[7]!=0xffffffffu;bucket++){
  uint length=plan.v[32u+bucket*2u],lane=plan.v[33u+bucket*2u];
  uint removal=lane<3u?plan.v[21u+lane]:0xffffffffu;
  bool tracks=lane<3u&&(plan.v[17]==lane||plan.v[18]==lane);
  for(uint count=length;count>1u;count--){
   uint selected=next_int(count),tail=count-1u;
   if(tracks){
    for(uint slot=0u;slot<2u;slot++)if(plan.v[17u+slot]==lane)positions[slot]=swap_position(positions[slot],selected,tail);
    removal=swap_position(removal,selected,tail);
   }
  }
  if(tracks)for(uint slot=0u;slot<2u;slot++)
   if(plan.v[17u+slot]==lane&&removal!=0xffffffffu&&removal<positions[slot])positions[slot]--;
 }
 uint used=0u;
 for(uint slot=0u;slot<plan.v[3];slot++){
  if((mask&(1u<<slot))==0u)continue;
  bool found=false;
  for(uint i=first;i<end;i++)if((used&(1u<<i))==0u&&(plan.v[4]==1u||i==first+slot)&&
   (lanes[i]==3u?(plan.v[25]&(1u<<slot))!=0u:lanes[i]==plan.v[17u+slot]&&ranks[i]==positions[slot])){
    used|=1u<<i;found=true;break;
   }
  if(!found)return false;
 }
 return true;
}
void main(){
 uint i=gl_GlobalInvocationID.x;if(i>=batch.v[3])return;
 atomicAdd(header.v[4],1u);
 uint ordinal=batch.v[4]==0u?batch.v[6]+i:input_ids.v[i];
 if(ordinal>=batch.v[2]){atomicOr(header.v[3],1u);return;}
 if(matches(root_hash_for_ordinal(make_u64(batch.v[0],batch.v[1])+uint64_t(ordinal)))){
  uint p=atomicAdd(header.v[2],1u);
  if(p>=batch.v[5]){atomicOr(header.v[3],2u);return;}output_ids.v[p]=ordinal;
 }
}
