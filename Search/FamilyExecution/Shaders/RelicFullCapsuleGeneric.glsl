// R-private complete initial bag and actual opening pair. Shared Niche is not
// observed: K's nonempty-pool Rewards draw count is invariant to its permutation.
bool r_generic_route(uint64_t root,RngState rewards,uint first,uint second){
    uint bag[512],rarities[512],total=0u;
    rng_initialize(root+rfull_u64(22u));
    for(uint b=0u;b<plan_meta.values[0u];++b){
        uvec4 bucket=bucket_meta.values[b]; uint scope=bucket.z&255u,kind=(bucket.z>>8u)&255u;
        if(scope==0u||kind<1u||kind>3u){consume_shuffle(bucket.y);continue;}
        if(total+bucket.y>512u)return true;
        for(uint i=0u;i<bucket.y;++i){bag[total+i]=pool_ids.values[bucket.x+i];rarities[total+i]=kind;}
        for(uint n=bucket.y;n>1u;--n){uint a=total+next_int(n),z=total+n-1u,v=bag[a];bag[a]=bag[z];bag[z]=v;}
        total+=bucket.y;
    }
    uint outputs[3],sources[3],out_count=0u;
    RewardRouteState state=cr_initialize_route(root,true,rewards);
    for(uint step=0u;step<2u;++step){
        uint relic=step==0u?first:second;
        if(relic==255u)continue;
        if(relic==3u||relic==28u){
            uint pulls=relic==3u?2u:1u;
            for(uint p=0u;p<pulls;++p){
                float roll=cr_next_float(state.rewards);uint rarity=roll<0.5f?1u:(roll<0.83f?2u:3u),key=rfull_meta(111u);
                bool found=false;
                for(uint r=rarity;r<=3u&&!found;++r)for(uint i=0u;i<total;++i)
                    if(bag[i]!=0xffffffffu&&rarities[i]==r){key=bag[i];bag[i]=0xffffffffu;found=true;break;}
                outputs[out_count]=key;sources[out_count++]=relic;
                for(uint i=0u;i<rfull_meta(121u);++i)
                    if(key==rfull_meta(rfull_meta(120u)+i))return true;
            }
        }else if(relic==14u){for(uint i=0u;i<18u;++i)cr_next_double(state.rewards);}
        else if(!cr_replay_query_literal_relic_consumption(relic,root,state))return true;
    }
    for(uint row=0u;row<rfull_meta(110u);++row){
        uint at=rfull_meta(109u)+row*4u,source=rfull_meta(at),mode=rfull_meta(at+1u),count=rfull_meta(at+2u),targets=rfull_meta(at+3u);
        if(source==255u&&out_count!=3u)return false;
        uint used=0u;bool any=false;
        for(uint t=0u;t<count;++t){
            uint target=rfull_meta(targets+t);bool found=false;
            for(uint i=0u;i<out_count;++i)if((source>=254u||sources[i]==source)&&outputs[i]==target&&
                (mode!=0u||(used&(1u<<i))==0u)){found=true;used|=1u<<i;break;}
            if(mode==0u&&!found)return false;
            if(mode==2u&&found)return false;
            any=any||found;
        }
        if(mode==1u&&!any)return false;
    }
    return true;
}

bool rfull_capsule_matches(uint64_t root){
    bool bones=rfull_meta(24u)>0u;
    if(!rfull_top_contains(root,bones?5u:rfull_meta(1u)))return false;
    RngState rewards=cr_rng_initialize(root+uint64_t(rfull_meta(14u))+rfull_u64(20u));
    uint first=rfull_meta(1u),second=255u;
    if(bones){
        uint pool[64],count=rfull_meta(24u);
        for(uint i=0u;i<count;++i)pool[i]=rfull_meta(42u+i);
        for(uint n=count;n>1u;--n){uint at=cr_next_int(rewards,n),v=pool[at];pool[at]=pool[n-1u];pool[n-1u]=v;}
        first=pool[0];second=pool[1];uint64_t pair=(uint64_t(1u)<<first)|(uint64_t(1u)<<second);
        if((rfull_u64(114u)!=uint64_t(0u)&&(pair&rfull_u64(114u))==uint64_t(0u))||
            (pair&rfull_u64(116u))!=rfull_u64(116u)||(pair&rfull_u64(118u))!=uint64_t(0u))return false;
        if(rfull_meta(29u)!=0u){
            uint a=rfull_meta(30u),b=rfull_meta(31u);
            if(pair!=((uint64_t(1u)<<a)|(uint64_t(1u)<<b)))return false;
            first=a;second=b;
        }
    }
    bool forward=r_generic_route(root,rewards,first,second);
    return forward || bones&&rfull_meta(29u)==0u&&r_generic_route(root,rewards,second,first);
}
