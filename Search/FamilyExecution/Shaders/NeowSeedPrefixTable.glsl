// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// Adapted from shion0528, resource.17, da82f7f9023c3d3598d50b8642e93f2801c37ccc.
// https://github.com/shion0528/rollthespire2
const uint PREFIX_HASH_BLOCK_SIZE = 1336336u;
uint encode_seed4_packed(uint ordinal) {
    uint d3=ordinal%34u; ordinal/=34u;
    uint d2=ordinal%34u; ordinal/=34u;
    uint d1=ordinal%34u; ordinal/=34u;
    return alphabet_byte(ordinal) | (alphabet_byte(d1)<<8u) |
           (alphabet_byte(d2)<<16u) | (alphabet_byte(d3)<<24u);
}
uint64_t prefix_hash_cache_load(uint row) {
    uint at=4u+row*4u;
    return make_u64(prefix_hash_cache_value(at),prefix_hash_cache_value(at+1u));
}
bool prefix_hash_cache_initialize(uint first, out uint row, out uint suffix_ordinal,
    out uint tail4, out uint64_t prefix_hash) {
    // The host limits each workspace to 2^24 roots: this sum fits uint. One
    // 32-bit constant division per eight roots replaces full 64-bit decoding.
    uint position=prefix_hash_cache_value(0u)+first;
    row=position/PREFIX_HASH_BLOCK_SIZE;
    suffix_ordinal=position-row*PREFIX_HASH_BLOCK_SIZE;
    tail4=encode_seed4_packed(suffix_ordinal);
    prefix_hash=uint64_t(0u);
    if(row>=prefix_hash_cache_value(1u))return false;
    prefix_hash=prefix_hash_cache_load(row);
    return true;
}
bool prefix_hash_cache_advance(inout uint row, inout uint suffix_ordinal,
    inout uint tail4, inout uint64_t prefix_hash) {
    suffix_ordinal++;
    if(suffix_ordinal==PREFIX_HASH_BLOCK_SIZE) {
        suffix_ordinal=0u; row++;
        if(row>=prefix_hash_cache_value(1u))return false;
        prefix_hash=prefix_hash_cache_load(row);
        tail4=0x30303030u;
        return true;
    }
    if(!advance_seed_byte32(tail4,24u))return true;
    if(!advance_seed_byte32(tail4,16u))return true;
    if(!advance_seed_byte32(tail4,8u))return true;
    advance_seed_byte32(tail4,0u);
    return true;
}
