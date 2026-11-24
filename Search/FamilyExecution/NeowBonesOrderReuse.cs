using static RolltheSpire2.Search.FamilyExecution.Beta110FastRelicCatalog;
using Kind = RolltheSpire2.Search.FamilyExecution.Beta110FastStructuredConditionKind;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class NeowBonesOrderReuse
{
    [Flags]
    private enum Streams { None = 0, Rewards = 1, NonCombat = 2, Transformations = 4, Unknown = 8 }

    // The solo GPU only projects the generated identities in these rows. Any
    // additional pickup, deck or enchantment condition retains both orders.
    internal static bool CanShare(NeowReplayPlan plan, byte[] pair)
    {
        if (!plan.VanillaBonesOrderAuthority || !plan.Authority.UsesChildShuffle || !plan.Bones ||
            plan.First != InvalidId || plan.Second != InvalidId || pair.Length != 2 || pair[0] == pair[1] ||
            plan.ExactOnly.Length != 0 || plan.HasCapsuleRarityGate || plan.ModernTransformGate is not null ||
            (plan.StructuredConditions.Length == 0 && !plan.HasFinalCurseFastProjection) ||
            Environment.GetEnvironmentVariable("RT2_DISABLE_NEOW_BONES_ORDER_REUSE") == "1") return false;

        Streams a = PickupStreams(pair[0]), b = PickupStreams(pair[1]);
        if (a == Streams.Unknown || b == Streams.Unknown) return false;
        foreach (var row in plan.StructuredConditions)
        {
            bool first = row.SourceRelicId == pair[0];
            if (!first && row.SourceRelicId != pair[1]) return false;
            var observed = row.Kind switch
            {
                Kind.ArcaneScrollGeneratedCard or Kind.HeftyTabletRareOffer or Kind.LeadPaperweightColorlessOffer or
                    Kind.LostCofferPotion or Kind.LostCofferCardOffer or Kind.ScrollBoxesCardComposition or
                    Kind.ScrollBoxesTripleClaw => Streams.Rewards,
                Kind.KaleidoscopeIndependentOfferTargets => Streams.Rewards | Streams.NonCombat,
                Kind.PhialHolsterPotions => Streams.NonCombat,
                Kind.NewLeafTransform or Kind.LeafyPoulticeTransforms => Streams.Transformations,
                _ => Streams.Unknown
            };
            if (observed != (first ? a : b) || (observed & (first ? b : a)) != 0) return false;
            if (observed == Streams.Transformations)
            {
                int removed = (first ? pair[1] : pair[0]) switch { PreciseScissors => 1, PrecariousShears => 2, _ => 0 };
                if (removed > plan.RemovableBasicStrikes ||
                    row.Kind == Kind.LeafyPoulticeTransforms && removed > plan.RemovableBasicDefends) return false;
            }
        }

        if (!plan.HasFinalCurseFastProjection)
        {
            // Omit an unobserved pickup only if its active operator cannot reject
            // because its pool is exhausted or missing a generated identity.
            foreach (byte source in pair)
            {
                if (plan.StructuredConditions.Any(row => row.SourceRelicId == source)) continue;
                Streams used = PickupStreams(source);
                if (plan.NeedsRewards && (used & Streams.Rewards) != 0 &&
                    !RelicOpeningPrefixPlan.TryProveCompanionRewardsDraws(plan, source, out _)) return false;
                if (source == PhialHolster && plan.NeedsPotions &&
                    !NeowSoloResultObservation.ValidPotions(plan.Authority.EffectCatalog.PotionPool, 2)) return false;
            }
        }
        // Kaleidoscope always forks two child shuffles. Phial's two bottles
        // each consume rarity + identity only if the second eligible draw exists.
        // Every other admitted pickup consumes zero words of the final curse stream.
        return !plan.HasFinalCurseFastProjection || !pair.Contains(PhialHolster) ||
            NeowSoloResultObservation.ValidPotions(plan.Authority.EffectCatalog.PotionPool, 2);
    }

    internal static string TerminalShader(NeowFamilyGpuPlan plan, int stage)
    {
        if (plan.Meta[83] != 0)
            return FormattableString.Invariant($"return route_at_arrival(root,rewards,{plan.Meta[27]}u,{plan.Meta[28]}u,0u,0u);");

        var code = new System.Text.StringBuilder("RouteRngState state;\n");
        // Stage 0 already verified the entire Leafy row before the pair checkpoint.
        // Compile only active observations. Multiple rows for Coffer/Boxes share one operator.
        foreach (byte source in plan.Meta.Skip(85).Take((int)plan.Meta[84]).Distinct().Select(id => (byte)id))
        {
            if (source == LeafyPoultice && stage == 2 && plan.UsesLeafyPreGate) continue;
            Streams streams = PickupStreams(source);
            if ((streams & Streams.Rewards) != 0) code.AppendLine("state.rewards = rewards;");
            if ((streams & Streams.NonCombat) != 0)
                code.AppendLine("state.non_combat = non_combat_rng(root); next_u64(state.non_combat);");
            if ((streams & Streams.Transformations) != 0)
                code.AppendLine("state.transformations = rng_initialize(root + plan_u64(37u));");

            int at = source * 5;
            string Target(int slot) => FormattableString.Invariant($"conditions.values[{at + slot}u]");
            string count = FormattableString.Invariant($"{plan.Conditions[at]}u");
            uint flags = plan.Conditions[at + 4] & 0x7fffffffu;
            string call = source switch
            {
                ArcaneScroll => $"execute_arcane_scroll_route({Target(1)},true,state)",
                HeftyTablet => $"execute_hefty_tablet_route({Target(1)},true,state)",
                LeadPaperweight => $"execute_lead_paperweight_route({Target(1)},true,state)",
                LostCoffer => $"execute_lost_coffer_route({count},{Target(1)},{Target(2)},true,state)",
                ScrollBoxes => FormattableString.Invariant($"execute_scroll_boxes_route({count},{Target(1)},{Target(2)},{Target(3)},{flags}u,true,state)"),
                Kaleidoscope when (flags & 4u) != 0 => $"execute_kaleidoscope_ordered_route({Target(1)},{Target(2)},state)",
                Kaleidoscope => $"execute_kaleidoscope_route({count},{Target(1)},{Target(2)},true,state)",
                PhialHolster => $"execute_phial_holster_route({count},{Target(1)},{Target(2)},true,state)",
                LeafyPoultice => $"execute_leafy_route({count},{Target(1)},{Target(2)},true,state)",
                NewLeaf => $"execute_new_leaf_route({Target(1)},true,state)",
                _ => throw new InvalidOperationException("N.UnknownIndependentBonesObservation")
            };
            code.Append("if (!").Append(call).AppendLine(") return false;");
        }
        return code.Append("return true;").ToString();
    }

    // These vanilla pickups advance none of Rewards, NonCombatGeneration or Transformations.
    // Their deck, enchantment or resource changes can still matter to other observations.
    internal static bool IsStreamNeutralPickup(byte relic) => relic is
        CursedPearl or
        DowsingRod or
        NeowsSacrifice or
        PrecariousShears or
        SilkenTress or
        SilverCrucible or
        BoomingConch or
        FishingRod or
        GoldenPearl or
        NeowsTorment or
        PreciseScissors or
        WingedBoots or
        LavaRock or
        NeowsTalisman or
        NutritiousOyster or
        Pomander or
        StoneHumidifier;

    private static Streams PickupStreams(byte source) => source switch
    {
        ArcaneScroll or HeftyTablet or LeadPaperweight or LostCoffer or ScrollBoxes => Streams.Rewards,
        Kaleidoscope => Streams.Rewards | Streams.NonCombat,
        PhialHolster => Streams.NonCombat,
        LeafyPoultice or NewLeaf => Streams.Transformations,
        _ when IsStreamNeutralPickup(source) => Streams.None,
        _ => Streams.Unknown
    };
}
