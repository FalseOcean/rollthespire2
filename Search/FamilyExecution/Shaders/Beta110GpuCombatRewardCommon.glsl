// Combat Reward GPU R1/R2 shared numeric library.
struct RngState { uint64_t s0; uint64_t s1; uint64_t s2; uint64_t s3; };
struct RewardRouteState {
    RngState rewards;
    RngState niche;
    float potion_odds;
    float card_offset;
    uint influence_flags;
    uint additional_card_rewards;
    uint fixed_gold;
    uint lasting_candy_counter;
    bool continuation_exact;
    bool influence_exact;
};

uint64_t cr_make_u64(uint low, uint high) { return uint64_t(low) | (uint64_t(high) << 32u); }
uint64_t cr_rotl64(uint64_t value, uint amount) { return (value << amount) | (value >> (64u - amount)); }
uint64_t cr_splitmix_next(inout uint64_t state) {
    state += cr_make_u64(0x7f4a7c15u, 0x9e3779b9u);
    uint64_t value = state;
    value = (value ^ (value >> 30u)) * cr_make_u64(0x1ce4e5b9u, 0xbf58476du);
    value = (value ^ (value >> 27u)) * cr_make_u64(0x133111ebu, 0x94d049bbu);
    return value ^ (value >> 31u);
}
RngState cr_rng_initialize(uint64_t seed) {
    uint64_t state = seed; RngState result;
    result.s0 = cr_splitmix_next(state); result.s1 = cr_splitmix_next(state);
    result.s2 = cr_splitmix_next(state); result.s3 = cr_splitmix_next(state);
    return result;
}
uint64_t cr_next_u64(inout RngState rng) {
    uint64_t result = cr_rotl64(rng.s1 * uint64_t(5u), 7u) * uint64_t(9u);
    uint64_t temporary = rng.s1 << 17u;
    rng.s2 ^= rng.s0; rng.s3 ^= rng.s1; rng.s1 ^= rng.s2; rng.s0 ^= rng.s3;
    rng.s2 ^= temporary; rng.s3 = cr_rotl64(rng.s3, 45u); return result;
}
uint cr_next_int_current_shortcut_from_mantissa(uint64_t x, uint bound) {
    uint low = uint(x); uint high = uint(x >> 32u);
    uint64_t low_product = uint64_t(low) * uint64_t(bound);
    uint64_t upper_limb = uint64_t(high) * uint64_t(bound) + (low_product >> 32u);
    return uint(upper_limb >> 21u);
}
uint cr_next_int_direct_fp64_from_mantissa(uint64_t x, uint bound) {
    double unit_value = double(x) * 1.1102230246251565e-16lf;
    return uint(unit_value * double(bound));
}
uint cr_next_int_exact_rne_small_from_mantissa(uint64_t x, uint bound) {
    uint64_t product = x * uint64_t(bound); uint shift = 0u;
    if      (product >= (uint64_t(1u) << 63u)) shift = 11u;
    else if (product >= (uint64_t(1u) << 62u)) shift = 10u;
    else if (product >= (uint64_t(1u) << 61u)) shift = 9u;
    else if (product >= (uint64_t(1u) << 60u)) shift = 8u;
    else if (product >= (uint64_t(1u) << 59u)) shift = 7u;
    else if (product >= (uint64_t(1u) << 58u)) shift = 6u;
    else if (product >= (uint64_t(1u) << 57u)) shift = 5u;
    else if (product >= (uint64_t(1u) << 56u)) shift = 4u;
    else if (product >= (uint64_t(1u) << 55u)) shift = 3u;
    else if (product >= (uint64_t(1u) << 54u)) shift = 2u;
    else if (product >= (uint64_t(1u) << 53u)) shift = 1u;
    uint64_t rounded = product;
    if (shift != 0u) {
        uint64_t high = product >> shift;
        uint64_t mask = (uint64_t(1u) << shift) - uint64_t(1u);
        uint64_t remainder = product & mask; uint64_t halfway_threshold = uint64_t(1u) << (shift - 1u);
        if (remainder > halfway_threshold || (remainder == halfway_threshold && (high & uint64_t(1u)) != uint64_t(0u))) high += uint64_t(1u);
        rounded = high;
    }
    return uint(rounded >> (53u - shift));
}
uint cr_next_int_exact_rne_from_mantissa(uint64_t x, uint bound) {
    return bound <= 2048u ? cr_next_int_exact_rne_small_from_mantissa(x, bound) : cr_next_int_direct_fp64_from_mantissa(x, bound);
}
#ifndef RT2_NEXT_INT_VARIANT
#define RT2_NEXT_INT_VARIANT 1
#endif
uint cr_next_int(inout RngState rng, uint bound) {
    uint64_t x = cr_next_u64(rng) >> 11u;
#if RT2_NEXT_INT_VARIANT == 0
    return cr_next_int_current_shortcut_from_mantissa(x, bound);
#elif RT2_NEXT_INT_VARIANT == 2
    return cr_next_int_exact_rne_from_mantissa(x, bound);
#else
    return cr_next_int_direct_fp64_from_mantissa(x, bound);
#endif
}
float cr_next_float(inout RngState rng) { return float(double(cr_next_u64(rng) >> 11u) * 1.1102230246251565e-16lf); }
double cr_next_double(inout RngState rng) { return double(cr_next_u64(rng) >> 11u) * 1.1102230246251565e-16lf; }
void cr_consume_double(inout RngState rng) { cr_next_u64(rng); }

uint cr_alphabet_byte(uint digit) {
    if (digit < 10u) return 48u + digit; if (digit < 18u) return 65u + (digit - 10u);
    if (digit < 23u) return 74u + (digit - 18u); return 80u + (digit - 23u);
}
void cr_encode_seed12(uint64_t ordinal, out uint64_t first8, out uint tail4) {
    uint d11=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u); uint d10=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u);
    uint d9=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u); uint d8=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u);
    uint d7=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u); uint d6=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u);
    uint d5=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u); uint d4=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u);
    uint d3=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u); uint d2=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u);
    uint d1=uint(ordinal%uint64_t(34u)); ordinal/=uint64_t(34u); uint d0=uint(ordinal);
    first8=uint64_t(cr_alphabet_byte(d0))|(uint64_t(cr_alphabet_byte(d1))<<8u)|
      (uint64_t(cr_alphabet_byte(d2))<<16u)|(uint64_t(cr_alphabet_byte(d3))<<24u)|
      (uint64_t(cr_alphabet_byte(d4))<<32u)|(uint64_t(cr_alphabet_byte(d5))<<40u)|
      (uint64_t(cr_alphabet_byte(d6))<<48u)|(uint64_t(cr_alphabet_byte(d7))<<56u);
    tail4=cr_alphabet_byte(d8)|(cr_alphabet_byte(d9)<<8u)|(cr_alphabet_byte(d10)<<16u)|(cr_alphabet_byte(d11)<<24u);
}
uint64_t cr_prime1(){return cr_make_u64(0x85ebca87u,0x9e3779b1u);} uint64_t cr_prime2(){return cr_make_u64(0x27d4eb4fu,0xc2b2ae3du);}
uint64_t cr_prime3(){return cr_make_u64(0x9e3779f9u,0x165667b1u);} uint64_t cr_prime4(){return cr_make_u64(0xc2b2ae63u,0x85ebca77u);}
uint64_t cr_prime5(){return cr_make_u64(0x165667c5u,0x27d4eb2fu);}
uint64_t cr_xxh_round(uint64_t a,uint64_t v){a+=v*cr_prime2();a=cr_rotl64(a,31u);return a*cr_prime1();}
uint64_t cr_hash_seed12(uint64_t ordinal){uint64_t f;uint t;cr_encode_seed12(ordinal,f,t);uint64_t h=cr_prime5()+uint64_t(12u);h^=cr_xxh_round(uint64_t(0u),f);h=cr_rotl64(h,27u)*cr_prime1()+cr_prime4();h^=uint64_t(t)*cr_prime1();h=cr_rotl64(h,23u)*cr_prime2()+cr_prime3();h^=h>>33u;h*=cr_prime2();h^=h>>29u;h*=cr_prime3();h^=h>>32u;return h;}

uint64_t cr_plan_u64(uint index){return uint64_t(plan_meta.values[index])|(uint64_t(plan_meta.values[index+1u])<<32u);}
void cr_consume_shuffle(uint length, inout RngState rng){for(uint count=length;count>1u;--count)cr_next_int(rng,count);}
bool cr_positive_allowed(uint id,uint curse,uint players,uint flags){
    if(id==17u&&players<=1u)return false; if(id==14u&&(flags&1u)==0u)return false;
    if(id==22u&&(flags&2u)==0u)return false; if(id==23u&&players!=1u)return false;
    if(curse==0u&&id==13u)return false; if(curse==2u&&id==10u)return false;
    if(curse==4u&&id==19u)return false; if(curse==7u&&id==21u)return false;
    if(curse==6u&&(id==20u||id==16u))return false; return true;
}
bool cr_replay_opening(uint64_t root_hash,out uint first,out uint second,out uint curse){
    uint slot=plan_meta.values[2u],players=plan_meta.values[3u],flags=plan_meta.values[6u];
    RngState event_rng=cr_rng_initialize(root_hash+uint64_t(slot)+cr_plan_u64(12u));
    curse=cr_next_int(event_rng,players==1u?10u:9u); uint positives[17];uint n=0u;
    for(uint id=10u;id<=23u;++id)if(cr_positive_allowed(id,curse,players,flags))positives[n++]=id;
    if(curse!=3u)positives[n++]=cr_next_int(event_rng,2u)==0u?24u:28u;
    positives[n++]=cr_next_int(event_rng,2u)==0u?26u:29u;
    positives[n++]=cr_next_int(event_rng,2u)==0u?25u:27u;
    if(n<2u)return false;
    for(uint count=n;count>1u;--count){uint s=cr_next_int(event_rng,count),tail=count-1u,tmp=positives[s];positives[s]=positives[tail];positives[tail]=tmp;}
    first=positives[0u];second=positives[1u];return true;
}
bool cr_replay_bones(uint64_t root_hash,out uint first,out uint second,out RngState rewards_after){
    uint count=plan_meta.values[7u]; if(count<2u||count>64u)return false;
    uint pool[64];for(uint i=0u;i<count;++i)pool[i]=bones_ids.ids[i];
    rewards_after=cr_rng_initialize(root_hash+uint64_t(plan_meta.values[2u])+cr_plan_u64(14u));
    for(uint n=count;n>1u;--n){uint selected=cr_next_int(rewards_after,n),tail=n-1u,tmp=pool[selected];pool[selected]=pool[tail];pool[tail]=tmp;}
    first=pool[0u];second=pool[1u];return true;
}

bool cr_used3(uint value,uint a,uint b,uint c,uint count){return(count>0u&&value==a)||(count>1u&&value==b)||(count>2u&&value==c);}
uint cr_select_unused(uint meta_index,uint a,uint b,uint c,uint count,inout RngState rng){
    uint offset=pool_meta.values[meta_index],length=pool_meta.values[meta_index+1u],available=0u;
    for(uint i=0u;i<length;++i)if(!cr_used3(dense_ids.ids[offset+i],a,b,c,count))available++;
    if(available==0u)return 0xffffffffu;uint pick=cr_next_int(rng,available);
    for(uint i=0u;i<length;++i){uint value=dense_ids.ids[offset+i];if(cr_used3(value,a,b,c,count))continue;if(pick==0u)return value;pick--;}
    return 0xffffffffu;
}
uint cr_rarity_meta(uint base,uint requested,uint fallback){uint rarity=requested==1u?(fallback==0u?1u:(fallback==1u?2u:3u)):requested==2u?(fallback==0u?2u:(fallback==1u?3u:1u)):(fallback==0u?3u:(fallback==1u?1u:2u));return base+(rarity-1u)*2u;}
uint cr_roll_opening_card(uint base,uint asc,uint a,uint b,uint c,uint used,inout RngState rng){
    float rare=asc>=7u?0.0149f:0.03f,roll=cr_next_float(rng);uint requested=roll<rare?3u:(roll<rare+0.37f?2u:1u);
    for(uint f=0u;f<3u;++f){uint value=cr_select_unused(cr_rarity_meta(base,requested,f),a,b,c,used,rng);if(value==0xffffffffu)continue;cr_next_float(rng);return value;}return 0xffffffffu;
}
uint cr_roll_opening_potion(uint a,uint used,inout RngState rng){float roll=cr_next_float(rng);uint meta=roll<=0.1f?16u:(roll<=0.35f?14u:12u);return cr_select_unused(meta,a,0xffffffffu,0xffffffffu,used,rng);}

uint64_t cr_identity_permutation(uint length){uint64_t p=uint64_t(0u);for(uint i=0u;i<length;++i)p|=uint64_t(i)<<(i*4u);return p;}
uint cr_perm_value(uint64_t p,uint i){return uint((p>>(i*4u))&uint64_t(15u));} void cr_perm_swap(inout uint64_t p,uint a,uint b){uint sa=a*4u,sb=b*4u;uint64_t va=(p>>sa)&uint64_t(15u),vb=(p>>sb)&uint64_t(15u),mask=~((uint64_t(15u)<<sa)|(uint64_t(15u)<<sb));p=(p&mask)|(va<<sb)|(vb<<sa);}
uint64_t cr_shuffle_small(uint count,inout RngState rng){uint64_t p=cr_identity_permutation(count);for(uint n=count;n>1u;--n)cr_perm_swap(p,cr_next_int(rng,n),n-1u);return p;}

void cr_apply_capability(uint packed,inout RewardRouteState state){
    state.continuation_exact=state.continuation_exact&&((packed&1u)!=0u);
    state.influence_exact=state.influence_exact&&((packed&2u)!=0u);
    state.influence_flags|=(packed>>8u)&0xffffu;
    state.additional_card_rewards+=packed>>24u;
}
uint cr_top_cap(uint relic){return relic<30u?capability_meta.values[relic*2u]:0u;}
bool cr_is_capsule(uint relic){return relic==3u||relic==28u;}

RewardRouteState cr_initialize_route(uint64_t root_hash,bool captured,RngState captured_rewards){
    RewardRouteState state;state.rewards=captured?captured_rewards:cr_rng_initialize(root_hash+uint64_t(plan_meta.values[2u])+cr_plan_u64(14u));
    state.niche=cr_rng_initialize(root_hash+cr_plan_u64(16u));state.potion_odds=0.4f;state.card_offset=-0.05f;
    state.influence_flags=0u;state.additional_card_rewards=0u;state.fixed_gold=0u;state.lasting_candy_counter=0u;
    state.continuation_exact=true;state.influence_exact=true;return state;
}

void cr_apply_explicit_query_reward_context(inout RewardRouteState state){
    state.influence_flags|=plan_meta.values[37u]&0xffffu;
    state.additional_card_rewards+=plan_meta.values[38u];
    state.fixed_gold+=plan_meta.values[39u];
}

uint cr_select_pool_excluding(uint meta,uint excluded[12],uint excluded_count,inout RngState rng);

bool cr_replay_query_literal_relic_consumption(uint relic,uint64_t root_hash,inout RewardRouteState state){
    uint asc=plan_meta.values[4u];
    // Capsule nested outputs are deliberately not executed. The authored Capsule
    // itself consumes one Rewards rarity roll per nested relic; hidden nested relic
    // behavior belongs to Exact unless explicitly represented by Reward context.
    if(relic==3u){cr_next_float(state.rewards);cr_next_float(state.rewards);return true;}
    if(relic==28u){cr_next_float(state.rewards);return true;}
    if(relic==17u&&plan_meta.values[3u]>1u){uint base=pool_meta.values[47u];uint a=cr_roll_opening_card(base,asc,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards);uint b=cr_roll_opening_card(base,asc,a,0xffffffffu,0xffffffffu,1u,state.rewards);uint c=cr_roll_opening_card(base,asc,a,b,0xffffffffu,2u,state.rewards);return a!=0xffffffffu&&b!=0xffffffffu&&c!=0xffffffffu;}
    if(relic==10u){return cr_select_unused(4u,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards)!=0xffffffffu;}
    if(relic==2u){uint a=cr_select_unused(4u,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards);uint b=cr_select_unused(4u,a,0xffffffffu,0xffffffffu,1u,state.rewards);uint c=cr_select_unused(4u,a,b,0xffffffffu,2u,state.rewards);return a!=0xffffffffu&&b!=0xffffffffu&&c!=0xffffffffu;}
    if(relic==15u){uint a=cr_roll_opening_card(6u,asc,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards);uint b=cr_roll_opening_card(6u,asc,a,0xffffffffu,0xffffffffu,1u,state.rewards);return a!=0xffffffffu&&b!=0xffffffffu;}
    if(relic==16u){uint a=cr_roll_opening_card(0u,asc,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards);uint b=cr_roll_opening_card(0u,asc,a,0xffffffffu,0xffffffffu,1u,state.rewards);uint c=cr_roll_opening_card(0u,asc,a,b,0xffffffffu,2u,state.rewards);uint p=cr_roll_opening_potion(0xffffffffu,0u,state.rewards);return a!=0xffffffffu&&b!=0xffffffffu&&c!=0xffffffffu&&p!=0xffffffffu;}
    if(relic==14u){uint count=pool_meta.values[40u];if(count<3u||count>15u)return false;uint other_base=48u;for(uint group=0u;group<2u;++group){uint64_t order=cr_shuffle_small(count,state.niche);for(uint item=0u;item<3u;++item){uint pool=cr_perm_value(order,item);if(cr_roll_opening_card(other_base+pool*6u,asc,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards)==0xffffffffu)return false;}}return true;}
    if(relic==22u){uint used_values[12];for(uint i=0u;i<12u;++i)used_values[i]=0xffffffffu;uint used=0u;bool defect=(plan_meta.values[6u]&4u)!=0u;for(uint bundle=0u;bundle<2u;++bundle){if(defect&&cr_next_int(state.rewards,100u)<1u)continue;uint x=cr_select_pool_excluding(0u,used_values,used,state.rewards);if(x==0xffffffffu)return false;used_values[used++]=x;uint y=cr_select_pool_excluding(0u,used_values,used,state.rewards);if(y==0xffffffffu)return false;used_values[used++]=y;uint z=cr_select_pool_excluding(2u,used_values,used,state.rewards);if(z==0xffffffffu)return false;used_values[used++]=z;}return true;}
    return true;
}

bool cr_replay_query_opening_consumption(uint64_t root_hash,inout RewardRouteState state){
    if(plan_meta.values[40u]!=0u){
        uint count=plan_meta.values[7u];
        if(count<2u)return false;
        for(uint n=count;n>1u;--n)cr_next_int(state.rewards,n);
    }
    uint count=min(plan_meta.values[41u],2u);
    for(uint i=0u;i<count;++i){
        uint relic=plan_meta.values[42u+i];
        if(relic!=0xffu&&!cr_replay_query_literal_relic_consumption(relic,root_hash,state))return false;
    }
    return true;
}

uint cr_select_pool_excluding(uint meta,uint excluded[12],uint excluded_count,inout RngState rng){uint off=pool_meta.values[meta],len=pool_meta.values[meta+1u],available=0u;for(uint i=0u;i<len;++i){uint v=dense_ids.ids[off+i];bool hit=false;for(uint j=0u;j<excluded_count;++j)hit=hit||excluded[j]==v;if(!hit)available++;}if(available==0u)return 0xffffffffu;uint pick=cr_next_int(rng,available);for(uint i=0u;i<len;++i){uint v=dense_ids.ids[off+i];bool hit=false;for(uint j=0u;j<excluded_count;++j)hit=hit||excluded[j]==v;if(hit)continue;if(pick==0u)return v;pick--;}return 0xffffffffu;}

bool cr_execute_non_capsule_relic(uint relic,uint64_t root_hash,inout RewardRouteState state){
    uint asc=plan_meta.values[4u];
    if(relic==10u){if(cr_select_unused(4u,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards)==0xffffffffu)return false;}
    else if(relic==2u){uint a=cr_select_unused(4u,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards);uint b=cr_select_unused(4u,a,0xffffffffu,0xffffffffu,1u,state.rewards);uint c=cr_select_unused(4u,a,b,0xffffffffu,2u,state.rewards);if(a==0xffffffffu||b==0xffffffffu||c==0xffffffffu)return false;}
    else if(relic==15u){uint a=cr_roll_opening_card(6u,asc,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards);uint b=cr_roll_opening_card(6u,asc,a,0xffffffffu,0xffffffffu,1u,state.rewards);if(a==0xffffffffu||b==0xffffffffu)return false;}
    else if(relic==16u){uint a=cr_roll_opening_card(0u,asc,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards);uint b=cr_roll_opening_card(0u,asc,a,0xffffffffu,0xffffffffu,1u,state.rewards);uint c=cr_roll_opening_card(0u,asc,a,b,0xffffffffu,2u,state.rewards);uint p=cr_roll_opening_potion(0xffffffffu,0u,state.rewards);if(a==0xffffffffu||b==0xffffffffu||c==0xffffffffu||p==0xffffffffu)return false;}
    else if(relic==14u){uint count=pool_meta.values[40u];if(count<3u||count>15u)return false;uint other_base=48u;for(uint group=0u;group<2u;++group){uint64_t order=cr_shuffle_small(count,state.niche);for(uint item=0u;item<3u;++item){uint pool=cr_perm_value(order,item);if(cr_roll_opening_card(other_base+pool*6u,asc,0xffffffffu,0xffffffffu,0xffffffffu,0u,state.rewards)==0xffffffffu)return false;}}}
    else if(relic==22u){uint used_values[12];for(uint i=0u;i<12u;++i)used_values[i]=0xffffffffu;uint used=0u;bool defect=(plan_meta.values[6u]&4u)!=0u;for(uint bundle=0u;bundle<2u;++bundle){if(defect&&cr_next_int(state.rewards,100u)<1u)continue;uint x=cr_select_pool_excluding(0u,used_values,used,state.rewards);if(x==0xffffffffu)return false;used_values[used++]=x;uint y=cr_select_pool_excluding(0u,used_values,used,state.rewards);if(y==0xffffffffu)return false;used_values[used++]=y;uint z=cr_select_pool_excluding(2u,used_values,used,state.rewards);if(z==0xffffffffu)return false;used_values[used++]=z;}}
    cr_apply_capability(cr_top_cap(relic),state);return state.continuation_exact&&state.influence_exact;
}

uint cr_combat_card_meta(uint rarity){return 20u+(rarity-1u)*2u;} uint cr_power_meta(uint rarity){return 26u+(rarity-1u)*2u;} uint cr_combat_potion_meta(uint rarity){return 32u+(rarity-1u)*2u;}
uint cr_roll_combat_rarity(float value,uint asc,inout float offset){float baseRare=asc>=7u?0.0149f:0.03f,rareThreshold=baseRare+offset,uncommonThreshold=rareThreshold+0.37f;if(value<rareThreshold){offset=-0.05f;return 3u;}offset=min(0.4f,offset+(asc>=7u?0.005f:0.01f));return value<uncommonThreshold?2u:1u;}
uint cr_generate_combat_card(bool power,uint asc,uint selected[12],uint selected_count,inout RewardRouteState state){uint exclusions=selected_count;
if(power){
    uint total=0u,available=0u;
    for(uint r=1u;r<=3u;r++){uint m=cr_power_meta(r),off=pool_meta.values[m],len=pool_meta.values[m+1u];total+=len;
        for(uint i=0u;i<len;i++){bool used=false;for(uint j=0u;j<selected_count;j++)used=used||selected[j]==dense_ids.ids[off+i];if(!used)available++;}}
    if(total==0u)return 0xffffffffu;
    if(available==0u)exclusions=0u;
}
float roll=cr_next_float(state.rewards),baseRare=asc>=7u?0.0149f:0.03f;
uint requested=power?(roll<baseRare?3u:(roll<baseRare+0.37f?2u:1u)):cr_roll_combat_rarity(roll,asc,state.card_offset);
for(uint f=0u;f<3u;++f){uint rarity=requested==1u?(f==0u?1u:(f==1u?2u:3u)):requested==2u?(f==0u?2u:(f==1u?3u:1u)):(f==0u?3u:(f==1u?1u:2u));uint value=cr_select_pool_excluding(power?cr_power_meta(rarity):cr_combat_card_meta(rarity),selected,exclusions,state.rewards);if(value==0xffffffffu)continue;cr_next_float(state.rewards);return value;}return 0xffffffffu;}

bool cr_contains(uint values[72],uint offset,uint count,uint target){for(uint i=0u;i<count;++i)if(values[offset+i]==target)return true;return false;}
bool cr_target_set(uint values[72],uint offset,uint count,uint target_offset,uint target_count,uint mode){if(target_count==0u)return true;if(mode==0u){for(uint i=0u;i<target_count;++i)if(cr_contains(values,offset,count,predicate_targets.ids[target_offset+i]))return true;return false;}if(mode==1u){for(uint i=0u;i<target_count;++i)if(!cr_contains(values,offset,count,predicate_targets.ids[target_offset+i]))return false;return true;}for(uint i=0u;i<target_count;++i)if(cr_contains(values,offset,count,predicate_targets.ids[target_offset+i]))return false;return true;}

bool cr_evaluate_rewards(inout RewardRouteState state){
    if(!state.continuation_exact||!state.influence_exact||((state.influence_flags&(64u|128u))!=0u))return true;
    uint asc=plan_meta.values[4u],maxBattle=clamp(plan_meta.values[5u],1u,6u);
    uint cards[72];uint counts[6];uint potions[6];uint drops[6];for(uint i=0u;i<72u;++i)cards[i]=0xffffffffu;for(uint i=0u;i<6u;++i){counts[i]=0u;potions[i]=0xffffffffu;drops[i]=0u;}
    for(uint battle=0u;battle<maxBattle;++battle){bool drop;if((state.influence_flags&1u)!=0u)drop=true;else{drop=cr_next_float(state.rewards)<state.potion_odds;state.potion_odds+=drop?-0.1f:0.1f;}drops[battle]=drop?1u:0u;
        uint minGold=asc>=3u?7u:10u,maxGold=asc>=3u?15u:20u;cr_next_int(state.rewards,maxGold-minGold+1u);
        if(drop){float p=cr_next_float(state.rewards);uint rarity=p<=0.1f?3u:(p<=0.35f?2u:1u);uint off=pool_meta.values[cr_combat_potion_meta(rarity)],len=pool_meta.values[cr_combat_potion_meta(rarity)+1u];if(len==0u)return true;potions[battle]=dense_ids.ids[off+cr_next_int(state.rewards,len)];}
        uint group_selected[12];for(uint i=0u;i<12u;++i)group_selected[i]=0xffffffffu;uint group_count=0u;
        for(uint i=0u;i<3u;++i){uint v=cr_generate_combat_card(false,asc,group_selected,group_count,state);if(v==0xffffffffu)return true;group_selected[group_count++]=v;cards[battle*12u+counts[battle]++]=v;}
        if((state.influence_flags&4u)!=0u&&(state.lasting_candy_counter&1u)==1u){uint excluded[12];for(uint i=0u;i<12u;++i)excluded[i]=i<counts[battle]?cards[battle*12u+i]:0xffffffffu;uint v=cr_generate_combat_card(true,asc,excluded,counts[battle],state);if(v!=0xffffffffu)cards[battle*12u+counts[battle]++]=v;}
        for(uint reward=0u;reward<state.additional_card_rewards;++reward){for(uint i=0u;i<12u;++i)group_selected[i]=0xffffffffu;group_count=0u;for(uint i=0u;i<3u;++i){uint v=cr_generate_combat_card(false,asc,group_selected,group_count,state);if(v==0xffffffffu||counts[battle]>=12u)return true;group_selected[group_count++]=v;cards[battle*12u+counts[battle]++]=v;}}
        if((state.influence_flags&4u)!=0u)state.lasting_candy_counter++;
    }
    uint predicate_count=plan_meta.values[11u];
    for(uint pi=0u;pi<predicate_count;++pi){uint base=pi*16u,battleOrd=predicate_meta.values[base],first=battleOrd==0u?1u:battleOrd,last=battleOrd==0u?maxBattle:battleOrd;bool matched=false;
        for(uint bo=first;bo<=last&&bo<=maxBattle;++bo){uint bi=bo-1u,off=bi*12u,count=counts[bi];
            if(!cr_target_set(cards,off,count,predicate_meta.values[base+2u],predicate_meta.values[base+3u],0u))continue;
            if(!cr_target_set(cards,off,count,predicate_meta.values[base+4u],predicate_meta.values[base+5u],1u))continue;
            if(!cr_target_set(cards,off,count,predicate_meta.values[base+6u],predicate_meta.values[base+7u],2u))continue;
            uint req=predicate_meta.values[base+1u];if(req==1u&&drops[bi]==0u)continue;if(req==2u&&drops[bi]!=0u)continue;
            uint pao=predicate_meta.values[base+8u],pac=predicate_meta.values[base+9u],plo=predicate_meta.values[base+10u],plc=predicate_meta.values[base+11u],pbo=predicate_meta.values[base+12u],pbc=predicate_meta.values[base+13u];
            if((pac+plc+pbc)>0u){if(drops[bi]==0u||potions[bi]==0xffffffffu)continue;bool any=pac==0u;for(uint i=0u;i<pac;++i)any=any||predicate_targets.ids[pao+i]==potions[bi];if(!any)continue;bool all=true;for(uint i=0u;i<plc;++i)all=all&&predicate_targets.ids[plo+i]==potions[bi];if(!all)continue;bool ban=false;for(uint i=0u;i<pbc;++i)ban=ban||predicate_targets.ids[pbo+i]==potions[bi];if(ban)continue;}
            matched=true;break;}
        if(!matched)return false;}
    return true;
}
