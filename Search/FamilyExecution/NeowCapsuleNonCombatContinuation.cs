using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class NeowCapsuleNonCombatContinuation
{
    internal const string Revision = ".CapsuleZeroNonCombat.20261113.v1";
    internal const string Remainder = "CapsuleNonCombatContinuation";
    internal const int GpuMetadataIndex = 57;

    internal static uint Compile(SearchModel request)
    {
        var authority = request.Authority;
        // Match the current-root bag prerequisites of the opening effect replay.
        // A runtime bag captured for a different root cannot establish this premise.
        if (authority.EffectAuthority is not { HasExactRelicBagSourcePools: true } ||
            (authority.PlayersCount > 1 &&
             (authority.WorldAuthority?.Beta109Generation is not { HasExactPartyRelicBags: true } party ||
              party.PersonalPlayerSlot != authority.PlayerSlotIndex))) return 0;

        uint sources = 0;
        foreach (var condition in request.Evaluation.StructuredNeowEffects)
        {
            if (!NeowReplayPlan.IsCapsule(condition) || NeowReplayPlan.IsGroupedCapsule(condition)) continue;
            bool small = condition.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule;
            int capacity = small ? 1 : 2;
            var kind = small ? NeowStructuredConditionKind.ExactSingle : NeowStructuredConditionKind.ExactUnorderedPair;
            // Every Exact match must obtain all these relics from this Capsule.
            // Only the downstream RNG is projected here. Exact still checks the bag.
            if (condition.Kind == kind && condition.OutputKeys.Count == capacity &&
                condition.OutputKeys.Distinct().Count() == capacity &&
                condition.OutputKeys.All(key => HasZeroAdvance(authority, key)))
                sources |= (uint)Beta110FastRelicCatalog.Bit(small ? Beta110FastRelicCatalog.SmallCapsule : Beta110FastRelicCatalog.LargeCapsule);
        }
        return sources;
    }

    internal static bool HasZeroAdvance(RuntimeContextAuthoritySnapshot authority, ModelKey key)
    {
        if (!authority.IsVanilla || authority.NoRunModifiers != true || authority.EffectAuthority is not { } effects)
            return false;
        var snapshots = (effects.OrderedRelicBag ?? []).Concat(effects.SharedRelicPoolSource ?? [])
            .Concat(effects.CharacterRelicPoolSource ?? []).Where(relic => relic.RelicKey == key).ToArray();
        return snapshots.Length != 0 && snapshots.All(relic => relic.NestedClassificationExact &&
            (relic.NestedEffectKind == NestedRelicEffectKind.NoTrackedImmediateEffect ||
             // In 0.113 the official Old Coin pickup hook only grants gold.
             key == BaseGameModelKeys.OrdinaryRelics.OldCoin &&
             string.Equals(relic.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase)));
    }

    internal static bool IsUnknown(NeowReplayPlan plan, byte source) =>
        plan.Authority.UsesChildShuffle &&
        (source is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule) &&
        (plan.CapsulesWithKnownZeroNonCombatAdvance & Beta110FastRelicCatalog.Bit(source)) == 0;

    internal static bool NeedsRemainder(NeowReplayPlan plan)
    {
        var domains = plan.EnabledDomains;
        bool finalCurse = plan.HasFinalCurseFastProjection;
        bool kaleidoscope = (domains & Beta110FastDomain.KaleidoscopeOffers) != 0;
        bool phial = (domains & Beta110FastDomain.PhialHolsterPotions) != 0;
        if (!finalCurse && !kaleidoscope && !phial) return false;

        foreach (byte first in plan.Authority.BonesEligibleRelicIds)
        foreach (byte second in plan.Authority.BonesEligibleRelicIds)
        {
            if (first == second || (plan.First != 255 && plan.Second != 255 &&
                (first != plan.First || second != plan.Second))) continue;
            ulong pair = Beta110FastRelicCatalog.Bit(first) | Beta110FastRelicCatalog.Bit(second);
            if ((pair & plan.BonesAll) != plan.BonesAll || (pair & plan.BonesBan) != 0 ||
                (plan.BonesAny != 0 && (pair & plan.BonesAny) == 0)) continue;

            if (finalCurse && (IsUnknown(plan, first) || IsUnknown(plan, second))) return true;
            if (IsUnknown(plan, first) && (kaleidoscope && second == Beta110FastRelicCatalog.Kaleidoscope ||
                phial && second == Beta110FastRelicCatalog.PhialHolster)) return true;
        }
        return false;
    }
}
