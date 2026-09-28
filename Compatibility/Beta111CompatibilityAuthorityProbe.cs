using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Compatibility;

/// <summary>
/// Read-only Beta111 compatibility smoke over the immutable runtime authority
/// snapshot. This is not a Predictor and cannot mutate game state. Its purpose is
/// to prove that v0.111 live authority was captured under the explicit Beta111
/// identity, including the Salvo/Splash rarity swap audited on 2026-08-14.
/// </summary>
public static class Beta111CompatibilityAuthorityProbe
{
    public sealed record Result(
        bool Applicable,
        bool Passed,
        RuntimeProfileId ProfileId,
        bool EffectAuthorityPresent,
        bool EffectFoundationExact,
        bool CardPoolsExact,
        bool PotionPoolExact,
        bool WorldAuthorityPresent,
        string SalvoPool,
        EffectCardRarity? SalvoRarity,
        RuntimeProfileId SalvoCatalogProfile,
        string SplashPool,
        EffectCardRarity? SplashRarity,
        RuntimeProfileId SplashCatalogProfile,
        string AuditFingerprint,
        string Issue)
    {
        public string ToLogFields() => string.Join(";", new[]
        {
            "beta111CompatibilityAuthoritySmoke=true",
            $"applicable={Applicable.ToString().ToLowerInvariant()}",
            $"passed={Passed.ToString().ToLowerInvariant()}",
            $"profile={ProfileId}",
            $"effectAuthorityPresent={EffectAuthorityPresent.ToString().ToLowerInvariant()}",
            $"effectFoundationExact={EffectFoundationExact.ToString().ToLowerInvariant()}",
            $"cardPoolsExact={CardPoolsExact.ToString().ToLowerInvariant()}",
            $"potionPoolExact={PotionPoolExact.ToString().ToLowerInvariant()}",
            $"worldAuthorityPresent={WorldAuthorityPresent.ToString().ToLowerInvariant()}",
            $"salvoPool={SalvoPool}",
            $"salvoRarity={SalvoRarity?.ToString() ?? "Missing"}",
            $"salvoCatalogProfile={SalvoCatalogProfile}",
            $"splashPool={SplashPool}",
            $"splashRarity={SplashRarity?.ToString() ?? "Missing"}",
            $"splashCatalogProfile={SplashCatalogProfile}",
            $"auditFingerprint={AuditFingerprint}",
            $"issue={Issue}"
        });
    }

    private sealed record CardHit(string Pool, NeowEffectCardSnapshot Card);

    public static Result Evaluate(RuntimeContextAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.ProfileId != RuntimeProfileId.Beta111 ||
            !RuntimeVersionCompatibility.IsBeta111(authority.GameVersion))
        {
            return new Result(
                false, false, authority.ProfileId, authority.EffectAuthority is not null,
                authority.EffectAuthority?.HasExactFoundation == true,
                authority.EffectAuthority?.HasExactCardPools == true,
                authority.EffectAuthority?.HasExactPotions == true,
                authority.WorldAuthority is not null,
                string.Empty, null, RuntimeProfileId.Unsupported,
                string.Empty, null, RuntimeProfileId.Unsupported,
                authority.RuntimeAuditFingerprint,
                authority.ProfileId != RuntimeProfileId.Beta111 ? "NotBeta111" : "NotExactBeta111GameVersion");
        }

        NeowEffectAuthoritySnapshot? effect = authority.EffectAuthority;
        CardHit? salvo = FindCard(effect, "SALVO");
        CardHit? splash = FindCard(effect, "SPLASH");
        bool foundationExact = effect?.HasExactFoundation == true;
        bool cardPoolsExact = effect?.HasExactCardPools == true;
        bool potionsExact = effect?.HasExactPotions == true;
        bool raritySwap = salvo?.Card.Rarity == EffectCardRarity.Uncommon &&
                          splash?.Card.Rarity == EffectCardRarity.Rare;
        bool catalogProfiles = salvo?.Card.CatalogProfileId == RuntimeProfileId.Beta111 &&
                               splash?.Card.CatalogProfileId == RuntimeProfileId.Beta111;
        bool auditIdentity = string.Equals(
            authority.RuntimeAuditFingerprint,
            RuntimeProfilePolicies.Beta111AuditFingerprint,
            StringComparison.Ordinal);
        bool capturedProfile = effect?.CapturedProfileId == RuntimeProfileId.Beta111;

        var issues = new List<string>();
        if (effect is null) issues.Add("EffectAuthorityMissing");
        if (!foundationExact) issues.Add("EffectFoundationNotExact");
        if (!cardPoolsExact) issues.Add("CardPoolsNotExact");
        if (!potionsExact) issues.Add("PotionPoolNotExact");
        if (salvo is null) issues.Add("SalvoMissing");
        if (splash is null) issues.Add("SplashMissing");
        if (salvo is not null && salvo.Card.Rarity != EffectCardRarity.Uncommon) issues.Add("SalvoNotUncommon");
        if (splash is not null && splash.Card.Rarity != EffectCardRarity.Rare) issues.Add("SplashNotRare");
        if (!catalogProfiles) issues.Add("CardCatalogProfileNotBeta111");
        if (!capturedProfile) issues.Add("EffectCapturedProfileNotBeta111");
        if (!auditIdentity) issues.Add("AuditFingerprintMismatch");

        bool passed = foundationExact && cardPoolsExact && potionsExact && raritySwap &&
                      catalogProfiles && capturedProfile && auditIdentity;
        return new Result(
            true,
            passed,
            authority.ProfileId,
            effect is not null,
            foundationExact,
            cardPoolsExact,
            potionsExact,
            authority.WorldAuthority is not null,
            salvo?.Pool ?? string.Empty,
            salvo?.Card.Rarity,
            salvo?.Card.CatalogProfileId ?? RuntimeProfileId.Unsupported,
            splash?.Pool ?? string.Empty,
            splash?.Card.Rarity,
            splash?.Card.CatalogProfileId ?? RuntimeProfileId.Unsupported,
            authority.RuntimeAuditFingerprint,
            issues.Count == 0 ? "" : string.Join(",", issues));
    }

    private static CardHit? FindCard(NeowEffectAuthoritySnapshot? effect, string entry)
    {
        if (effect is null) return null;
        CardHit? hit = Find(effect.CharacterRewardPool, "CharacterReward", entry);
        if (hit is not null) return hit;
        hit = Find(effect.ColorlessRewardPool, "ColorlessReward", entry);
        if (hit is not null) return hit;
        if (effect.OtherCharacterPools is not null)
        {
            foreach (CharacterCardPoolSnapshot pool in effect.OtherCharacterPools)
            {
                hit = Find(pool.Cards, "OtherCharacter:" + pool.PoolId, entry);
                if (hit is not null) return hit;
            }
        }
        return Find(effect.TransformPool, "Transform", entry);
    }

    private static CardHit? Find(IReadOnlyList<NeowEffectCardSnapshot>? cards, string pool, string entry)
    {
        if (cards is null) return null;
        NeowEffectCardSnapshot? match = cards.FirstOrDefault(card =>
            string.Equals(card.CardKey.Entry, entry, StringComparison.Ordinal));
        return match is null ? null : new CardHit(pool, match);
    }
}
