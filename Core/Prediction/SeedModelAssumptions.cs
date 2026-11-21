using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction;

/// <summary>Inputs to a conditional replay. Captured authority remains the only input to strict validation.</summary>
internal static class SeedModelAssumptions
{
    internal static bool TryCreate(RuntimeContextAuthoritySnapshot captured, out RuntimeContextAuthoritySnapshot modeled,
        out IReadOnlyDictionary<SeedPredictionDomainSelection, string> reasons,
        SeedPredictionDomainSelection requestedDomains = SeedPredictionDomainSelection.All | SeedPredictionDomainSelection.MerchantColorless)
    {
        modeled = captured;
        reasons = ImmutableDictionary<SeedPredictionDomainSelection, string>.Empty;
        if (captured.ProfileId != RuntimeProfileId.Beta111 || captured.UsesStandardOpeningAssumption) return false;

        var changes = ImmutableDictionary.CreateBuilder<SeedPredictionDomainSelection, string>();
        if ((requestedDomains & (SeedPredictionDomainSelection.World | SeedPredictionDomainSelection.RelicSequence)) != 0 &&
            WorldReplayAssumptions.TryCreate(captured.WorldAuthority, out var world, out string roomIssue))
        {
            modeled = modeled.WithWorldAuthority(world);
            changes.Add(SeedPredictionDomainSelection.World, roomIssue);
            string relicIssue = world.Beta109Generation!.InitialRelicReplayAssumption;
            if (relicIssue.Length > 0) changes.Add(SeedPredictionDomainSelection.RelicSequence, relicIssue);
        }

        if ((requestedDomains & (SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards)) != 0 &&
            !captured.HasExactModernNeowIdentityInputs)
        {
            // PrepareCatalog preserves runtime order and admits unresolved eligibility.
            // With no eligible curse there is no native opening draw to replay.
            var pool = captured.NeowPreparation;
            if (!pool.EligibleCurses.IsEmpty)
            {
                string issue = captured.VanillaNeowCatalogExact != true && captured.CapturedNeowOffers == null
                    ? "StandardNeowOfferPool" : "StandardNeowOfferRules";
                if (pool.UnknownEligibilityAffectedPool) issue += "|UnresolvedEligibilityIncluded";
                modeled = modeled.AssumingStandardOpening();
                changes.Add(SeedPredictionDomainSelection.Neow, issue);
                changes.Add(SeedPredictionDomainSelection.NormalCombatRewards, issue);
            }
        }

        if ((requestedDomains & SeedPredictionDomainSelection.MerchantColorless) != 0 &&
            Beta111MerchantColorlessAuthority.TryAssumeStandardRules(captured, out var merchantEffects, out var merchantAssumptions))
        {
            modeled = modeled.WithEffectAuthority(merchantEffects);
            changes.Add(SeedPredictionDomainSelection.MerchantColorless, merchantAssumptions);
        }

        if (captured.NoRunModifiers == false)
        {
            modeled = modeled.AssumingStandardOpening();
            foreach (var domain in new[] { SeedPredictionDomainSelection.World, SeedPredictionDomainSelection.RelicSequence,
                         SeedPredictionDomainSelection.Neow, SeedPredictionDomainSelection.NormalCombatRewards })
                changes[domain] = changes.TryGetValue(domain, out var other) ? other + "|StandardRunRules" : "StandardRunRules";
        }
        reasons = changes.ToImmutable();
        return changes.Count != 0;
    }
}
