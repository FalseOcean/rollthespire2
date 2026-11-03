// C observes only the bounded opening needed for sound reward rejection. These
// are private replay facts; neither a kept root nor this replay is Exact truth.
#if RT2_C_OBSERVED_OPENING == 1
const uint C_OPENING_EXACT=0u,C_OPENING_UNKNOWN=1u,C_OPENING_FAULT=2u;

bool c_observed_span(uint offset,uint count,uint length){return offset<=length&&count<=length-offset;}
uint c_observed_cap_count(){return(capability_meta.values[6]-capability_meta.values[5])/2u;}

bool c_observed_metadata_valid(){
    uint length=uint(capability_meta.values.length());
    if(length<10u||capability_meta.values[0]!=1u||capability_meta.values[1]>1u)return false;
    uint cap=capability_meta.values[5],top=capability_meta.values[6];
    if(cap<10u||top<cap||((top-cap)&1u)!=0u||!c_observed_span(top,90u,length))return false;
    if(!c_observed_span(capability_meta.values[7],31u*31u,length))return false;
    uint buckets=capability_meta.values[2],table=capability_meta.values[3];
    if(table>length||buckets>(length-table)/4u)return false;
    if(capability_meta.values[8]>uint(RT2_C_BAG_CAPACITY)||capability_meta.values[9]>=c_observed_cap_count())return false;
    return true;
}

uint c_observed_apply(uint identity,uint packed,uint gold,inout uint seen[6],inout uint seen_count,
    inout bool niche_known,inout RewardRouteState state){
    if(identity>=c_observed_cap_count())return C_OPENING_FAULT;
    if((packed&4u)!=0u)niche_known=false;
    // An unsupported obtain cannot establish a continuation for rejecting this
    // root, even when the same identity was already acquired elsewhere.
    if((packed&3u)!=3u||(((packed>>8u)&(64u|128u))!=0u))return C_OPENING_UNKNOWN;
    for(uint i=0u;i<seen_count;++i)if(seen[i]==identity)return C_OPENING_EXACT;
    if(seen_count>=6u)return C_OPENING_FAULT;
    seen[seen_count++]=identity;
    cr_apply_capability(packed,state);
    state.fixed_gold+=gold;
    return C_OPENING_EXACT;
}

uint c_observed_apply_cap(uint identity,inout uint seen[6],inout uint seen_count,
    inout bool niche_known,inout RewardRouteState state){
    if(identity>=c_observed_cap_count())return C_OPENING_FAULT;
    uint at=capability_meta.values[5]+identity*2u;
    return c_observed_apply(identity,capability_meta.values[at],capability_meta.values[at+1u],seen,seen_count,niche_known,state);
}

uint c_observed_initialize_bag(uint64_t root,out uint prefixes[12],out uint lengths[3]){
    for(uint i=0u;i<12u;++i)prefixes[i]=0xffffffffu;
    for(uint i=0u;i<3u;++i)lengths[i]=0u;
    RngState rng=cr_rng_initialize(root+cr_plan_u64(18u));
    uint table=capability_meta.values[3],length=uint(capability_meta.values.length()),lanes=0u;
    uint permutation[RT2_C_BAG_CAPACITY];
    for(uint bucket=0u;bucket<capability_meta.values[2];++bucket){
        uint at=table+bucket*4u,scope=capability_meta.values[at],kind=capability_meta.values[at+1u];
        uint entries=capability_meta.values[at+2u],count=capability_meta.values[at+3u];
        if(scope>1u||!c_observed_span(entries,count,length))return C_OPENING_FAULT;
        // Prefix/shared buckets and the personal Shop lane still consume their
        // complete shuffle, but no Capsule can draw an identity from them.
        // Shared Event/Ancient buckets have kind Other=0 and still own draws.
        if(scope==0u||kind==4u){cr_consume_shuffle(count,rng);continue;}
        if(kind<1u||kind>3u)return C_OPENING_FAULT;
        uint lane=kind-1u;
        if((lanes&(1u<<lane))!=0u||count>uint(RT2_C_BAG_CAPACITY)||count>capability_meta.values[8])return C_OPENING_FAULT;
        lanes|=1u<<lane;lengths[lane]=count;
        for(uint i=0u;i<count;++i){
            uint identity=capability_meta.values[entries+i];
            if(identity>=c_observed_cap_count())return C_OPENING_FAULT;
            permutation[i]=identity;
        }
        for(uint n=count;n>1u;--n){
            uint selected=cr_next_int(rng,n),tail=n-1u,value=permutation[selected];
            permutation[selected]=permutation[tail];permutation[tail]=value;
        }
        for(uint i=0u;i<min(count,4u);++i)prefixes[lane*4u+i]=permutation[i];
    }
    return C_OPENING_EXACT;
}

// Validate resources before the shared numeric donor reads them. A missing
// observation for an unlisted Bones companion is an explicit replay remainder;
// actual selection exhaustion in a present pool remains a numerical failure.
uint c_observed_pool(uint meta,uint pairs,bool unlisted){
    uint length=uint(pool_meta.values.length());
    if(meta>length||pairs>(length-meta)/2u)return C_OPENING_FAULT;
    uint total=0u;
    for(uint i=0u;i<pairs;++i){
        uint at=meta+i*2u,offset=pool_meta.values[at],count=pool_meta.values[at+1u];
        if(!c_observed_span(offset,count,uint(dense_ids.ids.length())))return C_OPENING_FAULT;
        total+=count;
    }
    return total==0u&&unlisted?C_OPENING_UNKNOWN:C_OPENING_EXACT;
}

uint c_observed_non_capsule(uint relic,uint64_t root,bool unlisted,bool niche_known,inout RewardRouteState state){
    uint status=C_OPENING_EXACT;
    if(relic==14u){
        if(uint(pool_meta.values.length())<=40u)return C_OPENING_FAULT;
        uint count=pool_meta.values[40];
        if(count<3u)return unlisted?C_OPENING_UNKNOWN:C_OPENING_FAULT;
        if(plan_meta.values[49]!=0u){
            for(uint group=0u;group<2u;++group){
                if(niche_known)for(uint n=count;n>1u;--n)cr_next_double(state.niche);
                for(uint draw=0u;draw<9u;++draw)cr_next_double(state.rewards);
            }
            return C_OPENING_EXACT;
        }
        if(!niche_known||count>15u)return C_OPENING_UNKNOWN;
        for(uint i=0u;i<count;++i){status=c_observed_pool(48u+i*6u,3u,unlisted);if(status!=C_OPENING_EXACT)return status;}
    }else if(relic==17u&&plan_meta.values[3]>1u){
        if(uint(pool_meta.values.length())<=47u)return C_OPENING_FAULT;
        status=c_observed_pool(pool_meta.values[47],3u,unlisted);
    }else if(relic==2u||relic==10u){
        status=c_observed_pool(4u,1u,unlisted);
    }else if(relic==15u){
        status=c_observed_pool(6u,3u,unlisted);
    }else if(relic==16u){
        status=c_observed_pool(0u,3u,unlisted);
        if(status==C_OPENING_EXACT)status=c_observed_pool(12u,3u,unlisted);
    }else if(relic==22u){
        status=c_observed_pool(0u,1u,unlisted);
        if(status==C_OPENING_EXACT)status=c_observed_pool(2u,1u,unlisted);
    }
    if(status!=C_OPENING_EXACT)return status;
    if(!cr_replay_query_literal_relic_consumption(relic,root,state))return C_OPENING_FAULT;
    if(relic==19u&&niche_known)cr_next_double(state.niche);
    return C_OPENING_EXACT;
}

bool c_observed_route(uint64_t root,uint route,out uint keep_reason){
    keep_reason=0u;
    if(!c_observed_metadata_valid())return cr_family_numeric_failure();
    RngState unused=cr_rng_initialize(uint64_t(0));
    RewardRouteState state=cr_initialize_route(root,false,unused);
    bool niche_known=plan_meta.values[44]!=0xffffffffu;
    if(niche_known)for(uint draw=0u;draw<plan_meta.values[44];++draw)cr_next_double(state.niche);
    uint first=plan_meta.values[42],second=plan_meta.values[43],count=plan_meta.values[41];
    bool dynamic_bones=capability_meta.values[1]!=0u;
    if(count>2u||dynamic_bones&&plan_meta.values[40]==0u)return cr_family_numeric_failure();
    if(plan_meta.values[40]!=0u){
        uint bones_count=plan_meta.values[7];
        if(bones_count<2u||bones_count>64u||bones_count>uint(bones_ids.ids.length()))return cr_family_numeric_failure();
        if(dynamic_bones){
            if(!cr_replay_bones(root,first,second,state.rewards))return cr_family_numeric_failure();
            count=2u;
        }else cr_consume_shuffle(bones_count,state.rewards);
    }
    uint prefixes[12],lengths[3],consumed[3],seen[6],seen_count=0u;
    for(uint i=0u;i<3u;++i)consumed[i]=0u;
    bool bag_ready=false;uint previous=30u;
    for(uint i=0u;i<count;++i){
        uint at=route==0u?i:count-1u-i,relic=at==0u?first:second;
        if(relic==255u){keep_reason=1u;return true;}
        if(relic>=30u)return cr_family_numeric_failure();
        uint top=capability_meta.values[6]+relic*3u;
        uint status=c_observed_apply(capability_meta.values[top+2u],capability_meta.values[top],capability_meta.values[top+1u],
            seen,seen_count,niche_known,state);
        if(status==C_OPENING_UNKNOWN){keep_reason=1u;return true;}
        if(status==C_OPENING_FAULT)return cr_family_numeric_failure();
        if(cr_is_capsule(relic)){
            if(!bag_ready){
                if(c_observed_initialize_bag(root,prefixes,lengths)!=C_OPENING_EXACT)return cr_family_numeric_failure();
                bag_ready=true;
            }
            uint draws=relic==3u?2u:1u;
            for(uint draw=0u;draw<draws;++draw){
                float roll=cr_next_float(state.rewards);uint lane=roll<0.5f?0u:(roll<0.83f?1u:2u);
                while(lane<3u&&consumed[lane]>=lengths[lane])++lane;
                uint identity=capability_meta.values[9];
                if(lane<3u){
                    if(consumed[lane]>=4u)return cr_family_numeric_failure();
                    identity=prefixes[lane*4u+consumed[lane]++];
                }
                status=c_observed_apply_cap(identity,seen,seen_count,niche_known,state);
                if(status==C_OPENING_UNKNOWN){keep_reason=1u;return true;}
                if(status==C_OPENING_FAULT)return cr_family_numeric_failure();
            }
        }else{
            bool unlisted=dynamic_bones&&relic!=plan_meta.values[42]&&relic!=plan_meta.values[43];
            status=c_observed_non_capsule(relic,root,unlisted,niche_known,state);
            if(status==C_OPENING_UNKNOWN){keep_reason=1u;return true;}
            if(status==C_OPENING_FAULT)return cr_family_numeric_failure();
        }
        uint advance=capability_meta.values[capability_meta.values[7]+relic*31u+previous];
        if(advance==0xffffffffu)niche_known=false;
        else if(niche_known)for(uint draw=0u;draw<advance;++draw)cr_next_double(state.niche);
        previous=relic;
    }
    if(plan_meta.values[40]!=0u&&niche_known)cr_next_double(state.niche);
    if(!state.continuation_exact||!state.influence_exact){keep_reason=1u;return true;}
#if RT2_C_OBSERVED_HOT == 1
    // Static admission proves the existing hot suffix's pools and predicates.
    // Only this actual prefix can prove its unchanged three-card/potion shape.
    if(state.additional_card_rewards!=0u||(state.influence_flags&~(8u|16u|32u))!=0u||
        state.potion_odds!=0.4f||state.card_offset!=-0.05f||state.lasting_candy_counter!=0u){
        keep_reason=2u;return true;
    }
    return cr_evaluate_rewards_unpinned_hot_state(state);
#else
    return cr_evaluate_rewards(state);
#endif
}
#endif
