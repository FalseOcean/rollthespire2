// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// Prefix math adapted from shion0528 (Egoistic今宵), resource.17,
// da82f7f9023c3d3598d50b8642e93f2801c37ccc and its existing RT2 N adaptation.
// James Zhou's earlier queued readback/batching remains a separate contribution.
namespace RolltheSpire2.Search.FamilyExecution;

internal static class TransformationAggregateGpuPrefixSource
{
    internal static string Apply(string source)
    {
        const string main = "void main(){";
        const string evaluate = "if(aggregate(root_hash_for_ordinal(make_u64(batch.v[0],batch.v[1])+uint64_t(ordinal)))){";
        if (source.Split(main, StringSplitOptions.None).Length != 2 ||
            source.Split(evaluate, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("T.PrefixReuseShaderSeam");
        const string helpers = """
layout(set=0,binding=6,std430) readonly restrict buffer TPrefixHash { uint v[]; } t_prefix;
uint rt2_t_encode_tail(uint ordinal) {
 uint d3=ordinal%34u; ordinal/=34u;
 uint d2=ordinal%34u; ordinal/=34u;
 uint d1=ordinal%34u; ordinal/=34u;
 return alphabet_byte(ordinal)|(alphabet_byte(d1)<<8u)|(alphabet_byte(d2)<<16u)|(alphabet_byte(d3)<<24u);
}
bool rt2_t_root(uint i,uint ordinal,out uint64_t root) {
 if(batch.v[4]!=0u) {
  root=root_hash_for_ordinal(make_u64(batch.v[0],batch.v[1])+uint64_t(ordinal)); return true;
 }
 // At most 2^24 roots plus an initial suffix <34^4 fits uint.
 uint position=t_prefix.v[0]+batch.v[6]+i;
 uint row=position/1336336u;
 root=uint64_t(0u);
 if(row>=t_prefix.v[1])return false;
 uint at=4u+row*4u;
 uint64_t hash=make_u64(t_prefix.v[at],t_prefix.v[at+1u]);
 hash^=uint64_t(rt2_t_encode_tail(position-row*1336336u))*prime1();
 hash=rotl64(hash,23u)*prime2()+prime3();
 hash^=hash>>33u; hash*=prime2(); hash^=hash>>29u; hash*=prime3();
 root=hash^(hash>>32u); return true;
}

""";
        return source.Replace(main, helpers + main, StringComparison.Ordinal)
            .Replace(evaluate, "uint64_t root; if(!rt2_t_root(i,ordinal,root)){atomicOr(header.v[3],4u);return;}\n if(aggregate(root)){", StringComparison.Ordinal);
    }
}
