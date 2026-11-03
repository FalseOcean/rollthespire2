using System.Numerics;

namespace RolltheSpire2.Search.FamilyExecution;

// Immutable R-private consumption proof. Rules describe one pickup, never a
// named pair. They neither carry state from N nor change initial-bag ownership.
internal sealed record RelicOpeningPrefixPlan(uint[] Rules)
{
    internal const uint BranchRule = 0x80000000u;

    internal bool TryFixedRewardsDraws(byte source, out uint draws)
    {
        uint rule = source < Rules.Length ? Rules[source] : 0;
        draws = rule is > 0 and < BranchRule ? rule - 1 : 0;
        return rule is > 0 and < BranchRule;
    }

    internal static bool TryCompile(NeowReplayPlan route, out RelicOpeningPrefixPlan? plan, out string issue)
    {
        plan = null;
        issue = "";
        // This experiment extends solo only. Party retains its existing donor,
        // shared-stream and nested-obtain policy without importing it into solo.
        if (route.Authority.PlayersCount != 1 || !route.DirectNestedVanilla111 || route.ExactOnly.Length != 0)
        {
            issue = "R.OpeningPrefixSourceScope";
            return false;
        }
        var rules = new uint[30];
        var sources = new HashSet<byte>();
        if (!route.Bones) sources.Add(route.Selected);
        else
        {
            ulong required = route.BonesAll;
            // Mirror the reference's source-presence rejection before replay.
            // Grouped rows are checked after replay, so they cannot suppress a
            // potentially failing donor merely because its outputs later miss.
            foreach (var condition in route.StructuredConditions)
                required |= Beta110FastRelicCatalog.Bit(condition.SourceRelicId);
            if (route.First != 255 && route.Second != 255)
                required |= Beta110FastRelicCatalog.Bit(route.First) | Beta110FastRelicCatalog.Bit(route.Second);
            byte[] pool = route.Authority.BonesEligibleRelicIds;
            // Only pairs that can satisfy the actual R observation own
            // continuation dependencies. An unrelated missing card pool cannot
            // disable, for example, a pinned Arcane/Capsule request.
            if (BitOperations.PopCount(required) <= 2)
                for (int i = 0; i < pool.Length; i++)
                    for (int j = i + 1; j < pool.Length; j++)
                    {
                        ulong pair = Beta110FastRelicCatalog.Bit(pool[i]) | Beta110FastRelicCatalog.Bit(pool[j]);
                        if ((pair & required) != required || (pair & route.BonesBan) != 0 ||
                            route.BonesAny != 0 && (pair & route.BonesAny) == 0) continue;
                        sources.Add(pool[i]); sources.Add(pool[j]);
                    }
        }
        foreach (byte source in sources)
        {
            if (source >= rules.Length || !TryRule(route, source, out uint rule))
            {
                issue = "R.OpeningPrefixContinuationUnavailable:Source" + source;
                return false;
            }
            rules[source] = rule;
        }
        plan = new(rules);
        return true;
    }

    private static bool TryRule(NeowReplayPlan route, byte source, out uint rule)
    {
        var c = route.Authority.EffectCatalog;
        rule = 0;
        // Every fixed rule proves successful original replay as well as its
        // draw count, including pickups after the observed Capsule. Empty or
        // invalid pools must retain their CPU failure rather than appear checked.
        switch (source)
        {
            case Beta110FastRelicCatalog.SmallCapsule: rule = 2; return true;
            case Beta110FastRelicCatalog.LargeCapsule: rule = 3; return true;
            case Beta110FastRelicCatalog.ArcaneScroll:
                if (!c.CharacterRewardAuthorityExact || !Valid(c.CharacterRewardPool.Rare, 1)) return false;
                rule = 2; return true;
            case Beta110FastRelicCatalog.HeftyTablet:
                if (!c.CharacterRewardAuthorityExact || !Valid(c.CharacterRewardPool.Rare, 3)) return false;
                rule = 4; return true;
            case Beta110FastRelicCatalog.LeadPaperweight:
                if (!c.ColorlessRewardAuthorityExact || !Valid(c.ColorlessRewardPool, 2)) return false;
                rule = 7; return true;
            case Beta110FastRelicCatalog.LostCoffer:
                if (!c.CharacterRewardAuthorityExact || !Valid(c.CharacterRewardPool, 3) || !c.PotionAuthorityExact ||
                    !Valid(c.PotionPool.Common, 1) || !Valid(c.PotionPool.Uncommon, 1) || !Valid(c.PotionPool.Rare, 1)) return false;
                rule = 12; return true;
            case Beta110FastRelicCatalog.Kaleidoscope:
                // Independent cards have no exclusions across offers. Any
                // nonempty valid rarity union guarantees one index and upgrade
                // draw after its rarity roll, regardless of Niche permutation.
                if (!c.OtherCharacterCardAuthorityExact || c.OtherCharacterPools.Length < 3 ||
                    c.OtherCharacterPools.Any(pool => !Valid(pool, 1))) return false;
                rule = 19; return true;
            case Beta110FastRelicCatalog.ScrollBoxes:
                if (!c.CharacterCardAuthorityExact || !Valid(c.CharacterRewardPool.Common, 4) ||
                    !Valid(c.CharacterRewardPool.Uncommon, 2) ||
                    c.CharacterRewardPool.Common.Intersect(c.CharacterRewardPool.Uncommon).Any()) return false;
                // Defect's special bundle branch changes consumption. Keep the
                // existing bounded donor instead of assigning a fixed burn.
                rule = route.Authority.UsesDefectScrollBoxesRule ? BranchRule : 7;
                return true;
            case Beta110FastRelicCatalog.NewLeaf:
                if (!c.NewLeafTransformAuthorityExact || c.NewLeafTransformPool.Length == 0) return false;
                rule = 1; return true;
            default:
                if (!RelicFullGpuPlan.CapsuleContinuationNeutralCompanion(source)) return false;
                rule = 1; return true;
        }
    }

    private static bool Valid(IEnumerable<ushort> values, int required)
    {
        ushort[] ids = values.ToArray();
        return !ids.Contains(Beta110FastDenseId.Invalid) && ids.Distinct().Take(required).Count() == required;
    }
    private static bool Valid(Beta110FastCardPool pool, int required) =>
        Valid(pool.Common.Concat(pool.Uncommon).Concat(pool.Rare), required);
}
