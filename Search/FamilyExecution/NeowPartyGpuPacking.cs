namespace RolltheSpire2.Search.FamilyExecution;

// Reuse the N matcher, with a private per-invocation plan index. All buffers remain
// immutable; fixed RNG owner and shared-prefix metadata are copied without changes.
internal static class NeowPartyGpuPacking
{
    internal static (NeowFamilyGpuPlan Plan, string Source) Pack(NeowFamilyGpuPlan[] plans)
    {
        if (plans.Length < 2) throw new ArgumentException("PartyN.FusedPlanCount");
        if (plans.Any(p => !p.IsMultiplayer)) throw new ArgumentException("PartyN.SingleplayerPlan");
        string source = NeowFamilyGpuExecutor.ShaderSource(leafyPreGate: plans.Any(p => p.UsesLeafyPreGate),
            authoredUpgrades: plans.Any(p => p.HasAuthoredUpgrades), localResults: plans.Any(p => p.HasLocalResults), multiplayer: true);
        source = source.Replace("uint plan_value(uint i)", "uint party_plan = 0u;\nuint plan_value(uint i)", StringComparison.Ordinal);
        uint[] Join(Func<NeowFamilyGpuPlan, uint[]> select, string access, int minimum = 1)
        {
            int stride = Math.Max(minimum, plans.Max(p => select(p).Length));
            var data = new uint[checked(stride * plans.Length)];
            for (int i = 0; i < plans.Length; i++) select(plans[i]).CopyTo(data, i * stride);
            source = source.Replace(access + "[", access + "[party_plan * " + stride + "u + ", StringComparison.Ordinal);
            return data;
        }
        // Specialize common structural values only (never card targets). This lets
        // the compiler remove unused effect operators from homogeneous party queries.
        string constants = string.Join("", new[] { 56, 70, 81, 82, 83, 84 }
            .Where(i => plans.All(p => p.Meta[i] == plans[0].Meta[i]))
            .Select(i => $"if(i=={i}u) return {plans[0].Meta[i]}u;"));
        source = source.Replace("uint plan_value(uint i) {", "uint plan_value(uint i) {" + constants, StringComparison.Ordinal);
        var meta = Join(p => p.Meta, "plan_meta.values");
        var packed = new NeowFamilyGpuPlan(meta, Join(p => p.PoolMeta, "other_pool_meta.values"),
            Join(p => p.Cards, "other_card_ids.ids"), Join(p => p.Strike, "leafy_strike.ids"),
            Join(p => p.Defend, "leafy_defend.ids"), Join(p => p.Bones, "bones.values"),
            Join(p => p.Conditions, "conditions.values", 226));
        // Host always dispatches the complete matcher, never the single-player staged path.
        // The shader retains every original plan's metadata, including its Leafy pre-gate.
        source = source.Replace("if (!leafy_pre_gate(root))", "if (plan_value(70u) != 0u && !leafy_pre_gate(root))", StringComparison.Ordinal);
        const string entry = "if (matches(root)) append_ordinal(ordinal);";
        if (!source.Contains(entry, StringComparison.Ordinal)) throw new InvalidOperationException("PartyN.FusedEntryChanged");
        source = source.Replace(entry, "bool accepted=true;\nfor(party_plan=0u;party_plan<" + plans.Length +
            "u;party_plan++) { if(!matches(root)) { accepted=false; break; } }\nif(accepted) append_ordinal(ordinal);", StringComparison.Ordinal);
        var hostMeta = (uint[])meta.Clone(); hostMeta[66] = 0;
        return (packed with { Meta = hostMeta }, source);
    }
}
