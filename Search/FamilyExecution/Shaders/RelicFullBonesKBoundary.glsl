// Explicit K/Large R: independent union or bounded mask-restricted routes.
// No N replay or new numerical algorithm in this receiver.
// Vanilla K consumes six (rarity, index, upgrade) triples from Rewards;
// pool shuffles use Niche, and neither K nor Leafy consumes UpFront.
if (!rfull_prepare_rewards(root)) return false;
uint64_t q0=s0, q1=s1, q2=s2, q3=s3;
nr_heavy_entries=0u;
bool early=false;
#if BONES_K_MASK
if((nr_route_mask&2u)!=0u)
#endif
{ atomicAdd(output_header.values[11u],1u); early=rfull_capsule_after_rewards(root); }
atomicAdd(output_header.values[6u], nr_heavy_entries);
// Restore Q before the late arrival; baseline keeps both existential routes.
s0=q0; s1=q1; s2=q2; s3=q3;
for(uint draw=0u;draw<18u;draw++) rfull_next_u64();
nr_heavy_entries=0u;
bool late=false;
#if BONES_K_MASK
if((nr_route_mask&1u)!=0u && !early)
#endif
{ atomicAdd(output_header.values[10u],1u); late=rfull_capsule_after_rewards(root); }
atomicAdd(output_header.values[7u], nr_heavy_entries);
if(early) atomicAdd(output_header.values[8u], 1u);
if(late) atomicAdd(output_header.values[9u], 1u);
return early || late;
