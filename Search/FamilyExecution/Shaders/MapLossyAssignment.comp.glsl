#version 450
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require
#extension GL_ARB_gpu_shader_fp64 : require
layout(local_size_x=32) in;
layout(set=0,binding=0,std430) readonly buffer Inputs { uint64_t inputWords[]; };
layout(set=0,binding=1,std430) buffer Outputs { uint results[]; };
layout(set=0,binding=2,std430) readonly buffer Config {
    uint configWords[];
};
layout(set=0,binding=3,std430) readonly buffer Transitions { uint transitions[]; };
// Transport specializes only storage dimensions; query semantics stay in uploaded transitions.
#define STATE_WORDS 16
// Source-matched topology + Assignment Round1. CPU provides the exact target-draw RNG prefix.
// Six vanilla SP Act1/2/3 x ordinary/Swarming laws; Full Final and Exact own truth.
uint kind[119], kids[119], parents[119], exists[119], candidate[119], queue[64];
// Edges join consecutive rows. Keep joint route states for only two rows.
uint routeStates[14 * STATE_WORDS];
int scalar[119], propertyValues[27];
uint head, queued, restTarget, unknownTarget, eliteTarget, height, boss;
uint64_t s0,s1,s2,s3;
/*__RT2_XOSHIRO_ROTATE45__*/
uint64_t rol(uint64_t x,uint k){return (x<<k)|(x>>(64u-k));}
uint draw(uint upper){
    uint64_t result=rol(s1*uint64_t(5),7u)*uint64_t(9),t=s1<<17u;
    s2^=s0;s3^=s1;s1^=s2;s0^=s3;s2^=t;s3=rt2_rotate_left45_split32(s3);
    return uint(double(result>>11u)*double(1.1102230246251565e-16)*double(upper));
}
void edge(uint p,uint c){kids[p]|=1u<<(c%7u);parents[c]|=1u<<(p%7u);}
uint create(uint col,uint row){uint n=row*7u+col;exists[n]=1u;return n;}
uint nextColumn(uint n){
    int d[3]=int[3](-1,0,1);
    for(int i=2;i>0;i--){uint j=draw(uint(i+1));int t=d[i];d[i]=d[j];d[j]=t;}
    int col=int(n%7u),row=int(n/7u);
    for(int i=0;i<3;i++){
        int target=clamp(col+d[i],0,6),actual=target-col;bool bad=false;
        if(actual!=0 && exists[uint(row*7+target)]!=0u){
            int crossing=target-actual;
            if(crossing>=0&&crossing<7&&(kids[uint(row*7+target)]&(1u<<uint(crossing)))!=0u)bad=true;
        }
        if(!bad)return uint(target);
    }return 999u;
}
void topology(){
    uint first=99u;
    for(uint attempt=0u;attempt<7u;attempt++){
        uint col;do{col=draw(7u);}while(attempt==1u&&col==first);
        if(attempt==0u)first=col;
        uint current=create(col,1u);
        for(uint row=1u;row<height;row++){
            uint next=nextColumn(current),child=create(next,row+1u);edge(current,child);current=child;
        }
    }
    for(uint col=0u;col<7u;col++){
        if(exists[height*7u+col]!=0u)edge(height*7u+col,boss);
        if(exists[7u+col]!=0u)edge(3u,7u+col);
    }
}
bool valid(uint node,uint type){
    uint row=node/7u;
    if(type==4u&&row>=height-2u)return false;
    if((type==4u||type==6u)&&row<6u)return false;
    bool adjacent=type==2u||type==3u||type==4u||type==6u;
    if(adjacent){
        uint bits=parents[node];while(bits!=0u){uint col=uint(findLSB(bits));bits&=bits-1u;if(kind[(row-1u)*7u+col]==type)return false;}
        bits=kids[node];while(bits!=0u){uint col=uint(findLSB(bits));bits&=bits-1u;if(kind[(row+1u)*7u+col]==type)return false;}
    }
    if(type==1u||type==2u||type==4u||type==5u||type==6u){
        uint bits=parents[node];while(bits!=0u){
            uint col=uint(findLSB(bits));bits&=bits-1u;
            uint sib=kids[(row-1u)*7u+col]&~(1u<<(node%7u));
            while(sib!=0u){uint sc=uint(findLSB(sib));sib&=sib-1u;if(kind[row*7u+sc]==type)return false;}
        }
    }return true;
}
uint candidates(uint type,bool modify){
    uint c=0u;
    for(uint col=0u;col<7u;col++)for(uint row=1u;row<=height;row++){
        uint n=row*7u+col;
        if(exists[n]!=0u&&kind[n]==type&&(!modify||(row!=1u&&row!=height&&row!=height-6u)))candidate[c++]=n;
    }
    for(int i=int(c)-1;i>0;i--){uint j=draw(uint(i+1));uint t=candidate[i];candidate[i]=candidate[j];candidate[j]=t;}
    return c;
}
void enqueue(uint type){queue[(head+queued)&63u]=type;queued++;}
void assign(){
    for(uint col=0u;col<7u;col++){
        if(exists[height*7u+col]!=0u)kind[height*7u+col]=4u;
        if(exists[(height-6u)*7u+col]!=0u)kind[(height-6u)*7u+col]=3u;
        if(exists[7u+col]!=0u)kind[7u+col]=5u;
    }
    head=0u;queued=0u;
    for(uint i=0u;i<restTarget;i++)enqueue(4u);
    for(uint i=0u;i<3u;i++)enqueue(2u);
    for(uint i=0u;i<eliteTarget;i++)enqueue(6u);
    for(uint i=0u;i<unknownTarget;i++)enqueue(1u);
    for(uint pass=0u;pass<1u&&queued>0u;pass++){
        uint c=candidates(0u,false);
        for(uint i=0u;i<c&&queued>0u;i++){
            uint node=candidate[i],attempts=queued;
            for(uint j=0u;j<attempts;j++){
                uint type=queue[head];head=(head+1u)&63u;queued--;
                if(valid(node,type)){kind[node]=type;break;}
                enqueue(type);
            }
        }
    }
    // The checkpoint skips fill and Start/Boss stamping; RNG remains Round1.
}
// Each bit represents one joint automaton state reached by a concrete route.
// Union at merges retains alternative routes without combining their progress.
uint routeMatch(uint transitionOffset,uint wordCount,uint acceptState){
    const uint rowWords=7u*STATE_WORDS;
    for(uint n=0u;n<rowWords;n++)routeStates[n]=0u;
    routeStates[3u*STATE_WORDS]=1u;
    uint currentBase=0u,nextBase=rowWords;
    for(uint row=0u;row<=height;row++){
        // Clear the destination once per row, before merging all parents.
        // This also resets wider retained specializations between drawings.
        for(uint n=0u;n<rowWords;n++)routeStates[nextBase+n]=0u;
        for(uint parentCol=0u;parentCol<7u;parentCol++){
            uint node=row*7u+parentCol,edges=kids[node];
            while(edges!=0u){
                uint col=uint(findLSB(edges));edges&=edges-1u;
                uint childRow=row+1u,child=childRow*7u+col;
                bool countable=childRow!=1u&&childRow!=height-6u&&childRow!=height&&child!=boss;
                uint room=kind[child];
                // Fill projection is candidate-only: never mutate kind[] or RNG.
                if(room==0u)room=5u;
                for(uint word=0u;word<wordCount;word++){
                    uint remaining=routeStates[currentBase+parentCol*STATE_WORDS+word];
                    while(remaining!=0u){
                        uint bit=uint(findLSB(remaining));remaining&=remaining-1u;
                        uint state=word*32u+bit;
                        uint next=countable?transitions[transitionOffset+state*9u+room]:state;
                        routeStates[nextBase+col*STATE_WORDS+(next>>5u)]|=1u<<(next&31u);
                    }
                }
            }
        }
        uint previousBase=currentBase;currentBase=nextBase;nextBase=previousBase;
    }
    // Completion must have a Start-to-Boss witness, including absorbing acceptance.
    return uint((routeStates[currentBase+(boss%7u)*STATE_WORDS+(acceptState>>5u)]&(1u<<(acceptState&31u)))!=0u);
}
// Independent MIN/MAX facts over complete Start-to-Boss routes. Each metric
// owns its own DP; extrema never borrow a drawing witness or each other's path.
// Fixed Monster/Rest participate here, while routeMatch excludes fixed rooms.
int propertyValue(uint metric){
    for(uint n=0u;n<119u;n++)scalar[n]=-1;
    scalar[boss]=0;
    bool maximum=metric>=4u&&metric<8u;
    uint roomTypes[4]=uint[4](5u,6u,4u,1u);
    uint target=roomTypes[metric%4u];
    for(int position=int(boss)-1;position>=3;position--){
        uint node=uint(position);
        if(exists[node]==0u)continue;
        uint edges=kids[node];int aggregate=-1;
        while(edges!=0u){
            uint col=uint(findLSB(edges));edges&=edges-1u;
            uint child=(node/7u+1u)*7u+col;
            int suffix=scalar[child];if(suffix<0)continue;
            aggregate=aggregate<0?suffix:maximum?max(aggregate,suffix):min(aggregate,suffix);
        }
        if(aggregate<0)continue;
        uint room=kind[node];
        if(room==0u)room=5u; // read-only candidate projection, no RNG/type mutation
        if(node==3u)scalar[node]=aggregate;
        else if(metric==8u)scalar[node]=room==5u?aggregate+1:0;
        else scalar[node]=aggregate+int(room==target);
    }
    return scalar[3];
}
void main(){
    uint i=gl_GlobalInvocationID.x;
    uint count=configWords[0],actCount=configWords[1],propertyCount=configWords[2];
    if(i>=count)return;
    uint propertyBase=4u+actCount*4u,drawingBase=propertyBase+propertyCount*4u;
    for(uint act=0u;act<actCount;act++){
        // Same root, separately derived act-specific prefixes. Assignment runs
        // once per needed Act for every property and independent drawing there.
        uint inputBase=(i*actCount+act)*5u;
        s0=inputWords[inputBase];s1=inputWords[inputBase+1u];s2=inputWords[inputBase+2u];s3=inputWords[inputBase+3u];
        uint targets=uint(inputWords[inputBase+4u]);
        restTarget=targets&15u;unknownTarget=targets>>4u&15u;eliteTarget=targets>>8u&15u;
        height=targets>>16u&255u;boss=(height+1u)*7u+3u;
        for(uint n=0u;n<119u;n++){kind[n]=0u;kids[n]=0u;parents[n]=0u;exists[n]=0u;}
        exists[3]=1u;exists[boss]=1u;topology();assign();
        uint descriptor=4u+act*4u,metrics=configWords[descriptor];
        while(metrics!=0u){
            uint metric=uint(findLSB(metrics));metrics&=metrics-1u;
            int value=propertyValue(metric);
            if(value<0)return;
            propertyValues[act*9u+metric]=value;
        }
        uint firstDrawing=configWords[descriptor+1u],drawingCount=configWords[descriptor+2u];
        for(uint drawing=0u;drawing<drawingCount;drawing++){
            uint entry=drawingBase+(firstDrawing+drawing)*4u;
            if(routeMatch(configWords[entry],configWords[entry+2u],configWords[entry+3u])==0u)return;
        }
        // A failed single-Act clause stops before generating the next Act.
        // Aggregate Scope 0 clauses retain their one final sum comparison.
        for(uint property=0u;property<propertyCount;property++){
            uint clause=propertyBase+property*4u;
            if(configWords[clause]!=(1u<<act))continue;
            uint metric=configWords[clause+1u];
            int value=propertyValues[act*9u+metric],threshold=int(configWords[clause+3u]);
            if(configWords[clause+2u]!=0u?value<threshold:value>threshold)return;
        }
    }
    for(uint property=0u;property<propertyCount;property++){
        uint clause=propertyBase+property*4u,acts=configWords[clause],metric=configWords[clause+1u];
        if((acts&(acts-1u))==0u)continue;
        int value=0;
        while(acts!=0u){uint act=uint(findLSB(acts));acts&=acts-1u;value+=propertyValues[act*9u+metric];}
        int threshold=int(configWords[clause+3u]);
        if(configWords[clause+2u]!=0u?value<threshold:value>threshold)return;
    }
    atomicOr(results[i>>5u],1u<<(i&31u));
}
