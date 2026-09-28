#ifndef RT2_CR_NUMERIC_FAILURE
#define RT2_CR_NUMERIC_FAILURE() true
#endif
// P10-A Runtime-Accepted donor: streaming predicate evaluation + current-battle-only materialization.
// P10-RP1 production GPU Reward uses this as the retained single-route evaluator;
// the historical BaselineCurrent variant remains diagnostic-only.
// RNG generation order is intentionally identical to Beta110GpuCombatRewardCommon.cr_evaluate_rewards.
bool cr_p10a_contains(uint values[12], uint count, uint target){for(uint i=0u;i<count;++i)if(values[i]==target)return true;return false;}
bool cr_p10a_target_set(uint values[12],uint count,uint target_offset,uint target_count,uint mode){
    if(target_count==0u)return true;
    if(mode==0u){for(uint i=0u;i<target_count;++i)if(cr_p10a_contains(values,count,predicate_targets.ids[target_offset+i]))return true;return false;}
    if(mode==1u){for(uint i=0u;i<target_count;++i)if(!cr_p10a_contains(values,count,predicate_targets.ids[target_offset+i]))return false;return true;}
    for(uint i=0u;i<target_count;++i)if(cr_p10a_contains(values,count,predicate_targets.ids[target_offset+i]))return false;
    return true;
}
bool cr_p10a_match_predicate(uint cards[12],uint card_count,bool drop,uint potion,uint base){
    if(!cr_p10a_target_set(cards,card_count,predicate_meta.values[base+2u],predicate_meta.values[base+3u],0u))return false;
    if(!cr_p10a_target_set(cards,card_count,predicate_meta.values[base+4u],predicate_meta.values[base+5u],1u))return false;
    if(!cr_p10a_target_set(cards,card_count,predicate_meta.values[base+6u],predicate_meta.values[base+7u],2u))return false;
    uint req=predicate_meta.values[base+1u];if(req==1u&&!drop)return false;if(req==2u&&drop)return false;
    uint pao=predicate_meta.values[base+8u],pac=predicate_meta.values[base+9u],plo=predicate_meta.values[base+10u],plc=predicate_meta.values[base+11u],pbo=predicate_meta.values[base+12u],pbc=predicate_meta.values[base+13u];
    if((pac+plc+pbc)>0u){
        if(!drop||potion==0xffffffffu)return false;
        bool any=pac==0u;for(uint i=0u;i<pac;++i)any=any||predicate_targets.ids[pao+i]==potion;if(!any)return false;
        bool all=true;for(uint i=0u;i<plc;++i)all=all&&predicate_targets.ids[plo+i]==potion;if(!all)return false;
        bool ban=false;for(uint i=0u;i<pbc;++i)ban=ban||predicate_targets.ids[pbo+i]==potion;if(ban)return false;
    }
    return true;
}

bool cr_evaluate_rewards(inout RewardRouteState state){
#ifndef RT2_CR_FAMILY
    if(!state.continuation_exact||!state.influence_exact||((state.influence_flags&(64u|128u))!=0u))return true;
#endif
    uint asc=plan_meta.values[4u],maxBattle=clamp(plan_meta.values[5u],1u,3u),predicate_count=plan_meta.values[11u];
    uint64_t matched_mask=uint64_t(0u);
    for(uint battle=0u;battle<maxBattle;++battle){
        bool drop;
        if((state.influence_flags&1u)!=0u)drop=true;
        else{drop=cr_next_float(state.rewards)<state.potion_odds;state.potion_odds+=drop?-0.1f:0.1f;}
        uint minGold=asc>=3u?7u:10u,maxGold=asc>=3u?15u:20u;cr_next_int(state.rewards,maxGold-minGold+1u);
        uint potion=0xffffffffu;
        if(drop){
            float p=cr_next_float(state.rewards);uint rarity=p<=0.1f?3u:(p<=0.35f?2u:1u);
            uint off=pool_meta.values[cr_combat_potion_meta(rarity)],len=pool_meta.values[cr_combat_potion_meta(rarity)+1u];if(len==0u)return RT2_CR_NUMERIC_FAILURE();
            potion=dense_ids.ids[off+cr_next_int(state.rewards,len)];
        }
        uint cards[12];for(uint i=0u;i<12u;++i)cards[i]=0xffffffffu;uint count=0u;
        uint group_selected[12];for(uint i=0u;i<12u;++i)group_selected[i]=0xffffffffu;uint group_count=0u;
        for(uint i=0u;i<3u;++i){uint v=cr_generate_combat_card(false,asc,group_selected,group_count,state);if(v==0xffffffffu)return RT2_CR_NUMERIC_FAILURE();group_selected[group_count++]=v;cards[count++]=v;}
        if((state.influence_flags&4u)!=0u&&(state.lasting_candy_counter&1u)==1u){
            uint excluded[12];for(uint i=0u;i<12u;++i)excluded[i]=i<count?cards[i]:0xffffffffu;
            uint v=cr_generate_combat_card(true,asc,excluded,count,state);if(v==0xffffffffu)return RT2_CR_NUMERIC_FAILURE();cards[count++]=v;
        }
        for(uint reward=0u;reward<state.additional_card_rewards;++reward){
            for(uint i=0u;i<12u;++i)group_selected[i]=0xffffffffu;group_count=0u;
            for(uint i=0u;i<3u;++i){uint v=cr_generate_combat_card(false,asc,group_selected,group_count,state);if(v==0xffffffffu||count>=12u)return RT2_CR_NUMERIC_FAILURE();group_selected[group_count++]=v;cards[count++]=v;}
        }
        if((state.influence_flags&4u)!=0u)state.lasting_candy_counter++;

        uint battle_ordinal=battle+1u;
        for(uint pi=0u;pi<predicate_count;++pi){
            uint base=pi*14u,predicate_battle=predicate_meta.values[base];uint64_t bit=uint64_t(1u)<<pi;
            if(predicate_battle==0u){
                if((matched_mask&bit)==uint64_t(0u)&&cr_p10a_match_predicate(cards,count,drop,potion,base))matched_mask|=bit;
            }else if(predicate_battle==battle_ordinal){
                if(!cr_p10a_match_predicate(cards,count,drop,potion,base))return false;
                matched_mask|=bit;
            }
        }
    }
    uint64_t required_mask=predicate_count>=64u?~uint64_t(0u):((uint64_t(1u)<<predicate_count)-uint64_t(1u));
    return (matched_mask&required_mask)==required_mask;
}

// P10 single-route hot-loop donor family. These paths are only entered for the
// RP1 unpinned synthetic continuation. Pinned/legacy routes stay on the accepted
// generic P10-A evaluator above.
#ifndef RT2_CR_HOT_ENABLED
#define RT2_CR_HOT_ENABLED 0
#endif
#ifndef RT2_CR_HOT_DIRECT_POOL
#define RT2_CR_HOT_DIRECT_POOL 0
#endif
#ifndef RT2_CR_HOT_RAW_BURN
#define RT2_CR_HOT_RAW_BURN 0
#endif
#ifndef RT2_CR_HOT_INTEGER_PROB
#define RT2_CR_HOT_INTEGER_PROB 0
#endif
#ifndef RT2_CR_HOT_POTION_ID_RAW_BURN
#define RT2_CR_HOT_POTION_ID_RAW_BURN 0
#endif
#ifndef RT2_CR_HOT_POTION_ID_REQUIRED
#define RT2_CR_HOT_POTION_ID_REQUIRED 0
#endif
#ifndef RT2_CR_HOT_MAX_BATTLE
#define RT2_CR_HOT_MAX_BATTLE 1u
#endif
#ifndef RT2_CR_HOT_PREDICATE_COUNT
#define RT2_CR_HOT_PREDICATE_COUNT 1u
#endif

uint64_t cr_hot_next_mantissa(inout RngState rng){return cr_next_u64(rng)>>11u;}
bool cr_hot_contains3(uint a,uint b,uint c,uint target){return a==target||b==target||c==target;}
bool cr_hot_target_set3(uint a,uint b,uint c,uint target_offset,uint target_count,uint mode){
    if(target_count==0u)return true;
    if(mode==0u){for(uint i=0u;i<target_count;++i)if(cr_hot_contains3(a,b,c,predicate_targets.ids[target_offset+i]))return true;return false;}
    if(mode==1u){for(uint i=0u;i<target_count;++i)if(!cr_hot_contains3(a,b,c,predicate_targets.ids[target_offset+i]))return false;return true;}
    for(uint i=0u;i<target_count;++i)if(cr_hot_contains3(a,b,c,predicate_targets.ids[target_offset+i]))return false;
    return true;
}
bool cr_hot_match_predicate3(uint a,uint b,uint c,bool drop,uint potion,uint base){
    if(!cr_hot_target_set3(a,b,c,predicate_meta.values[base+2u],predicate_meta.values[base+3u],0u))return false;
    if(!cr_hot_target_set3(a,b,c,predicate_meta.values[base+4u],predicate_meta.values[base+5u],1u))return false;
    if(!cr_hot_target_set3(a,b,c,predicate_meta.values[base+6u],predicate_meta.values[base+7u],2u))return false;
    uint req=predicate_meta.values[base+1u];if(req==1u&&!drop)return false;if(req==2u&&drop)return false;
    uint pao=predicate_meta.values[base+8u],pac=predicate_meta.values[base+9u],plo=predicate_meta.values[base+10u],plc=predicate_meta.values[base+11u],pbo=predicate_meta.values[base+12u],pbc=predicate_meta.values[base+13u];
    if((pac+plc+pbc)>0u){
        if(!drop||potion==0xffffffffu)return false;
        bool any=pac==0u;for(uint i=0u;i<pac;++i)any=any||predicate_targets.ids[pao+i]==potion;if(!any)return false;
        bool all=true;for(uint i=0u;i<plc;++i)all=all&&predicate_targets.ids[plo+i]==potion;if(!all)return false;
        bool ban=false;for(uint i=0u;i<pbc;++i)ban=ban||predicate_targets.ids[pbo+i]==potion;if(ban)return false;
    }
    return true;
}

uint cr_hot_roll_card_rarity(inout RngState rewards,uint asc,inout float card_offset,inout uint card_state){
#if RT2_CR_HOT_INTEGER_PROB == 1
    uint64_t x=cr_hot_next_mantissa(rewards);
    uint state_count=max(plan_meta.values[31u],1u);
    uint state=min(card_state,state_count-1u);
    uint table=plan_meta.values[30u]+state*4u;
    if(x<cr_plan_u64(table)){card_state=0u;return 3u;}
    bool uncommon=x<cr_plan_u64(table+2u);
    card_state=min(state+1u,state_count-1u);
    return uncommon?2u:1u;
#else
    return cr_roll_combat_rarity(cr_next_float(rewards),asc,card_offset);
#endif
}

bool cr_hot_select_card_scan(uint meta,uint s0,uint s1,uint selected_count,inout RngState rewards,out uint value,out uint pool_index){
    uint off=pool_meta.values[meta],len=pool_meta.values[meta+1u],available=0u;
    for(uint i=0u;i<len;++i){uint v=dense_ids.ids[off+i];bool excluded=(selected_count>0u&&v==s0)||(selected_count>1u&&v==s1);if(!excluded)available++;}
    if(available==0u){value=0xffffffffu;pool_index=0xffffffffu;return false;}
    uint pick=cr_next_int(rewards,available);
    for(uint i=0u;i<len;++i){uint v=dense_ids.ids[off+i];bool excluded=(selected_count>0u&&v==s0)||(selected_count>1u&&v==s1);if(excluded)continue;if(pick==0u){value=v;pool_index=i;return true;}pick--;}
    value=0xffffffffu;pool_index=0xffffffffu;return false;
}

bool cr_hot_select_card_direct(uint meta,uint sm0,uint si0,uint sm1,uint si1,uint selected_count,inout RngState rewards,out uint value,out uint pool_index){
    uint off=pool_meta.values[meta],len=pool_meta.values[meta+1u];
    uint e0=0xffffffffu,e1=0xffffffffu,excluded=0u;
    if(selected_count>0u&&sm0==meta){e0=si0;excluded++;}
    if(selected_count>1u&&sm1==meta){if(e0==0xffffffffu)e0=si1;else{e1=si1;if(e1<e0){uint t=e0;e0=e1;e1=t;}}excluded++;}
    if(len<=excluded){value=0xffffffffu;pool_index=0xffffffffu;return false;}
    uint actual=cr_next_int(rewards,len-excluded);
    if(e0!=0xffffffffu&&actual>=e0)actual++;
    if(e1!=0xffffffffu&&actual>=e1)actual++;
    if(actual>=len){value=0xffffffffu;pool_index=0xffffffffu;return false;}
    pool_index=actual;value=dense_ids.ids[off+actual];return true;
}

bool cr_hot_generate_card(inout RngState rewards,uint asc,inout float card_offset,inout uint card_state,
    uint s0,uint sm0,uint si0,uint s1,uint sm1,uint si1,uint selected_count,
    out uint value,out uint used_meta,out uint used_index){
    uint requested=cr_hot_roll_card_rarity(rewards,asc,card_offset,card_state);
#if RT2_CR_HOT_DIRECT_POOL == 1
    used_meta=cr_combat_card_meta(requested);
    if(!cr_hot_select_card_direct(used_meta,sm0,si0,sm1,si1,selected_count,rewards,value,used_index))return false;
#else
    bool selected=false;value=0xffffffffu;used_meta=0xffffffffu;used_index=0xffffffffu;
    for(uint f=0u;f<3u;++f){
        uint rarity=requested==1u?(f==0u?1u:(f==1u?2u:3u)):requested==2u?(f==0u?2u:(f==1u?3u:1u)):(f==0u?3u:(f==1u?1u:2u));
        uint meta=cr_combat_card_meta(rarity);
        if(cr_hot_select_card_scan(meta,s0,s1,selected_count,rewards,value,used_index)){used_meta=meta;selected=true;break;}
    }
    if(!selected)return false;
#endif
#if RT2_CR_HOT_RAW_BURN == 1
    cr_next_u64(rewards); // natural upgrade result is Search-unobserved; one fixed draw
#else
    cr_next_float(rewards);
#endif
    return value!=0xffffffffu;
}

bool cr_evaluate_rewards_unpinned_hot(uint64_t root){
    uint asc=plan_meta.values[4u];
    RngState rewards=cr_rng_initialize(root+uint64_t(plan_meta.values[2u])+cr_plan_u64(14u));
    float potion_odds=0.4f;
    float card_offset=-0.05f;
    uint potion_node=0u;
    uint card_state=0u;
    uint64_t matched_mask=uint64_t(0u);

    for(uint battle=0u;battle<RT2_CR_HOT_MAX_BATTLE;++battle){
        bool drop;
#if RT2_CR_HOT_INTEGER_PROB == 1
        uint64_t potion_x=cr_hot_next_mantissa(rewards);
        uint drop_table=plan_meta.values[32u]+potion_node*2u;
        drop=potion_x<cr_plan_u64(drop_table);
        potion_node=potion_node*2u+(drop?1u:2u);
#else
        drop=cr_next_float(rewards)<potion_odds;
        potion_odds+=drop?-0.1f:0.1f;
#endif

#if RT2_CR_HOT_RAW_BURN == 1
        cr_next_u64(rewards); // gold result is unobserved; GPU plans with gold predicates are rejected
#else
        uint minGold=asc>=3u?7u:10u,maxGold=asc>=3u?15u:20u;cr_next_int(rewards,maxGold-minGold+1u);
#endif

        uint potion=0xffffffffu;
        if(drop){
#if RT2_CR_HOT_POTION_ID_RAW_BURN == 1
            cr_next_u64(rewards); // potion rarity draw: branch result irrelevant when every rarity pool is nonempty
            cr_next_u64(rewards); // potion pool pick: identity unobserved
#else
            uint rarity;
#if RT2_CR_HOT_INTEGER_PROB == 1
            uint64_t potion_rarity_x=cr_hot_next_mantissa(rewards);
            uint rarity_table=plan_meta.values[34u];
            rarity=potion_rarity_x<cr_plan_u64(rarity_table)?3u:(potion_rarity_x<cr_plan_u64(rarity_table+2u)?2u:1u);
#else
            float p=cr_next_float(rewards);rarity=p<=0.1f?3u:(p<=0.35f?2u:1u);
#endif
            uint meta=cr_combat_potion_meta(rarity),off=pool_meta.values[meta],len=pool_meta.values[meta+1u];if(len==0u)return RT2_CR_NUMERIC_FAILURE();
            uint picked=cr_next_int(rewards,len);
#if RT2_CR_HOT_POTION_ID_REQUIRED == 1
            potion=dense_ids.ids[off+picked];
#endif
#endif
        }

        uint c0,m0,i0,c1,m1,i1,c2,m2,i2;
        if(!cr_hot_generate_card(rewards,asc,card_offset,card_state,0xffffffffu,0xffffffffu,0xffffffffu,0xffffffffu,0xffffffffu,0xffffffffu,0u,c0,m0,i0))return RT2_CR_NUMERIC_FAILURE();
        if(!cr_hot_generate_card(rewards,asc,card_offset,card_state,c0,m0,i0,0xffffffffu,0xffffffffu,0xffffffffu,1u,c1,m1,i1))return RT2_CR_NUMERIC_FAILURE();
        if(!cr_hot_generate_card(rewards,asc,card_offset,card_state,c0,m0,i0,c1,m1,i1,2u,c2,m2,i2))return RT2_CR_NUMERIC_FAILURE();

        uint battle_ordinal=battle+1u;
        for(uint pi=0u;pi<RT2_CR_HOT_PREDICATE_COUNT;++pi){
            uint base=pi*14u,predicate_battle=predicate_meta.values[base];uint64_t bit=uint64_t(1u)<<pi;
            if(predicate_battle==0u){
                if((matched_mask&bit)==uint64_t(0u)&&cr_hot_match_predicate3(c0,c1,c2,drop,potion,base))matched_mask|=bit;
            }else if(predicate_battle==battle_ordinal){
                if(!cr_hot_match_predicate3(c0,c1,c2,drop,potion,base))return false;
                matched_mask|=bit;
            }
        }
    }
    uint64_t required_mask=RT2_CR_HOT_PREDICATE_COUNT>=64u?~uint64_t(0u):((uint64_t(1u)<<RT2_CR_HOT_PREDICATE_COUNT)-uint64_t(1u));
    return (matched_mask&required_mask)==required_mask;
}
