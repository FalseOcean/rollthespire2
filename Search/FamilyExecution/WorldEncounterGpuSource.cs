namespace RolltheSpire2.Search.FamilyExecution;

// Only encounter requests select this extension. Ordinary W retains the existing source and buffer shape.
internal static class WorldEncounterGpuSource
{
    internal static uint[] Pack(Beta110WorldFastPlan plan)
    {
        var words = Enumerable.Repeat(0u, plan.Acts.Length * 8).ToList();
        uint Store(IEnumerable<uint> values) { uint offset = (uint)words.Count; words.AddRange(values); return offset; }
        for (int index = 0; index < plan.Acts.Length; index++)
        {
            var act = plan.Acts[index];
            var predicates = new List<uint>();
            foreach (var p in act.EncounterPredicates)
            {
                uint allowed = Store(p.Allowed.Select(id => (uint)id));
                predicates.AddRange([(uint)p.Kind, (uint)p.RangeMode, (uint)p.RangeValue, allowed, (uint)p.Allowed.Length, p.VariantMismatch ? 1u : 0u]);
            }
            int at = index * 8;
            words[at] = Store(predicates); words[at + 1] = (uint)act.EncounterPredicates.Length;
            words[at + 2] = (uint)act.EncounterDiscoveryMode;
            words[at + 3] = Store(act.FirstRunNormalPrefix.Select(id => (uint)id)); words[at + 4] = (uint)act.FirstRunNormalPrefix.Length;
            words[at + 5] = Store(act.FirstRunElitePrefix.Select(id => (uint)id)); words[at + 6] = (uint)act.FirstRunElitePrefix.Length;
        }
        return words.ToArray();
    }

    internal static string Extend(string body, string numerical, int normalSlots, int eliteSlots)
    {
        string consumer = numerical[numerical.IndexOf("void consume_encounter_queue(", StringComparison.Ordinal)..numerical.IndexOf("bool values_contain(", StringComparison.Ordinal)];
        consumer = consumer.Replace("void consume_encounter_queue(", "bool consume_checked_queue(", StringComparison.Ordinal)
            .Replace("uint source_offset,", "uint kind, uint source_offset,", StringComparison.Ordinal)
            .Replace("if (slots == 0u) return;", "if (slots == 0u) return true;", StringComparison.Ordinal)
            .Replace("        previous_valid = true;", """
                    previous_valid = true;
                    uint identity=encounter_buffer.values[selected_base+4u];
                    if(kind==2u) encounter_elites[slot]=identity;
                    else encounter_normal[(kind==0u?0u:encounter_weak_slots)+slot]=identity;
                    if(!encounter_deferred&&!encounter_position_match(kind,slot,identity))return false;
                """, StringComparison.Ordinal);
        int closing = consumer.LastIndexOf('}');
        consumer = consumer.Insert(closing, "    return true;\n");
        string functions = "uint encounter_normal[" + normalSlots + "],encounter_elites[" + eliteSlots + "];\n" + """
            uint encounter_act,encounter_weak_slots;
            bool encounter_deferred;
            bool encounter_position_match(uint kind,uint slot,uint identity){
                uint m=encounter_act*8u;
                for(uint n=0u;n<reserved.v[m+1u];++n){uint p=reserved.v[m]+n*6u;
                    if(reserved.v[p]!=kind)continue;
                    bool observes=reserved.v[p+1u]==0u?slot<reserved.v[p+2u]:slot+1u==reserved.v[p+2u];
                    if(!observes)continue;
                    bool hit=false;
                    for(uint j=0u;j<reserved.v[p+4u];++j)hit=hit||reserved.v[reserved.v[p+3u]+j]==identity;
                    if(!hit)return false;
                }return true;
            }
            """ + consumer + """
            bool encounter_queues_match(uint invocation,uint a,uint b,bool downstream){
                encounter_act=a;encounter_weak_slots=act_meta_buffer.values[b+5u];uint m=a*8u;
                if(reserved.v[m+2u]==2u){
                    if(downstream){bool previous=false;uint source=0u,reference=0u;
                        consume_encounter_queue(invocation,act_meta_buffer.values[b+3u],act_meta_buffer.values[b+4u],act_meta_buffer.values[b+5u],previous,source,reference);
                        consume_encounter_queue(invocation,act_meta_buffer.values[b+6u],act_meta_buffer.values[b+7u],act_meta_buffer.values[b+8u],previous,source,reference);
                        previous=false;source=0u;reference=0u;
                        consume_encounter_queue(invocation,act_meta_buffer.values[b+9u],act_meta_buffer.values[b+10u],act_meta_buffer.values[b+11u],previous,source,reference);
                    }return true;
                }
                uint slots[3]=uint[3](encounter_weak_slots,act_meta_buffer.values[b+8u],act_meta_buffer.values[b+11u]);
                uint horizon[3]=uint[3](0u,0u,0u);
                for(uint n=0u;n<reserved.v[m+1u];++n){uint p=reserved.v[m]+n*6u,kind=reserved.v[p],range=reserved.v[p+2u];
                    if(reserved.v[p+5u]!=0u||range>slots[kind])return false;
                    horizon[kind]=max(horizon[kind],range);
                }
                encounter_deferred=reserved.v[m+2u]!=0u;
                if(downstream||horizon[2]>0u||(encounter_deferred&&(horizon[0]>0u||horizon[1]>0u))){horizon[0]=slots[0];horizon[1]=slots[1];}
                else if(horizon[1]>0u)horizon[0]=slots[0];
                if(downstream||(encounter_deferred&&horizon[2]>0u))horizon[2]=slots[2];
                bool previous=false;uint source=0u,reference=0u;
                if(!consume_checked_queue(invocation,0u,act_meta_buffer.values[b+3u],act_meta_buffer.values[b+4u],horizon[0],previous,source,reference))return false;
                if(!consume_checked_queue(invocation,1u,act_meta_buffer.values[b+6u],act_meta_buffer.values[b+7u],horizon[1],previous,source,reference))return false;
                previous=false;source=0u;reference=0u;
                if(!consume_checked_queue(invocation,2u,act_meta_buffer.values[b+9u],act_meta_buffer.values[b+10u],horizon[2],previous,source,reference))return false;
                if(encounter_deferred){
                    for(uint lane=0u;lane<2u;++lane){uint count=lane==0u?horizon[0]+horizon[1]:horizon[2],offset=reserved.v[m+(lane==0u?3u:5u)],prefix=reserved.v[m+(lane==0u?4u:6u)];
                        for(uint i=0u;i<min(count,prefix);++i){uint target=reserved.v[offset+i],found=INVALID_ID;
                            for(uint j=0u;j<count;++j)if((lane==0u?encounter_normal[j]:encounter_elites[j])==target){found=j;break;}
                            if(lane==0u){if(found!=INVALID_ID)encounter_normal[found]=encounter_normal[i];encounter_normal[i]=target;}
                            else{if(found!=INVALID_ID)encounter_elites[found]=encounter_elites[i];encounter_elites[i]=target;}
                        }
                    }
                    for(uint kind=0u;kind<3u;++kind)for(uint i=0u;i<horizon[kind];++i)
                        if(!encounter_position_match(kind,i,kind==2u?encounter_elites[i]:encounter_normal[(kind==0u?0u:encounter_weak_slots)+i]))return false;
                }return true;
            }
            """;
        body = body.Replace("ENCOUNTER_STRIDE=4u", "ENCOUNTER_STRIDE=5u", StringComparison.Ordinal);
        body = body.Replace("bool world_match(uint invocation,uint64_t root){", functions + "\nbool world_match(uint invocation,uint64_t root){", StringComparison.Ordinal);
        const string start = "        bool previous=false;uint source=0u,reference=0u;";
        int first = body.IndexOf(start, body.IndexOf("bool world_match(", StringComparison.Ordinal), StringComparison.Ordinal);
        int last = body.IndexOf("        uint bossCount=", first, StringComparison.Ordinal);
        string ordinary = body[first..last];
        body = body[..first] + """
                    bool downstream=act<maxAct||hasBoss||hasAncient||second;
                    if(reserved.v[a*8u+1u]>0u){
                        if(!encounter_queues_match(invocation,a,b,downstream))return false;
                        if(!downstream)continue;
                    }else{
                """ + ordinary + "}\n" + body[last..];
        return body.Replace("if(!(act<maxAct||hasBoss||hasAncient||second))continue;",
            "if(!(reserved.v[a*8u+1u]>0u||act<maxAct||hasBoss||hasAncient||second))continue;", StringComparison.Ordinal);
    }
}
