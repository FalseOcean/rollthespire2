using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Rewards;

public sealed record OpeningRewardSupportEvaluation(
    OpeningRewardSupportFlags Capabilities,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<EvidenceCode> EvidenceCodes,
    bool RewardDomainResolved)
{
    public IReadOnlyList<RewardImpactSourceKey> SupportedImpactSources { get; init; } =
        Array.Empty<RewardImpactSourceKey>();

    public string RewardImpactFingerprint { get; init; } = string.Empty;
}

/// <summary>
/// Domain-specific normal-combat-reward classification for relics that are
/// actually present in one completed opening route. It deliberately does not
/// use the Neow-effect coverage registry: Neow effect coverage, on-obtain continuation,
/// and held normal-Monster reward impact are independent contracts.
/// </summary>
public static class OpeningRewardSupport
{
    public static OpeningRewardSupportFlags Resolve(
        RuntimeProfileId profileId,
        ModelKey relicKey)
    {
        if (!VanillaRelicRewardEffects.TryGet(profileId, relicKey, out VanillaRelicRewardEffect record))
        {
            return OpeningRewardSupportFlags.VanillaCapabilityNotAudited;
        }

        return ResolveHeldCapability(profileId, record);
    }

    // Compatibility overload for existing profile-invariant fixture assertions.
    public static OpeningRewardSupportFlags Resolve(ModelKey relicKey) =>
        Resolve(RuntimeProfileId.Beta110, relicKey);

    public static OpeningRewardSupportFlags Merge(
        RuntimeProfileId profileId,
        IEnumerable<ModelKey> relicKeys)
    {
        OpeningRewardSupportFlags result = OpeningRewardSupportFlags.None;
        foreach (ModelKey relicKey in relicKeys)
        {
            result |= Resolve(profileId, relicKey);
        }
        return result;
    }

    public static OpeningRewardSupportEvaluation EvaluateRoute(
        RuntimeProfileId profileId,
        IEnumerable<ModelKey> obtainedRelicKeys,
        IEnumerable<ModelKey> nestedRelicKeys,
        Func<ModelKey, bool?> isKnownModdedRelic)
    {
        ArgumentNullException.ThrowIfNull(obtainedRelicKeys);
        ArgumentNullException.ThrowIfNull(nestedRelicKeys);
        ArgumentNullException.ThrowIfNull(isKnownModdedRelic);

        ModelKey[] obtained = obtainedRelicKeys
            .Where(key => key.IsValid)
            .Distinct()
            .OrderBy(key => key.Serialized, StringComparer.Ordinal)
            .ToArray();
        HashSet<ModelKey> nested = nestedRelicKeys
            .Where(key => key.IsValid)
            .ToHashSet(ModelKeyComparer.Instance);

        OpeningRewardSupportFlags capabilities = OpeningRewardSupportFlags.None;
        var reasons = new List<string>();
        var evidence = new List<EvidenceCode>();
        var supportedImpactSources = new List<RewardImpactSourceKey>();
        bool rewardDomainResolved = true;

        foreach (ModelKey relicKey in obtained)
        {
            if (!VanillaRelicRewardEffects.TryGet(
                    profileId,
                    relicKey,
                    out VanillaRelicRewardEffect record))
            {
                rewardDomainResolved = false;
                bool knownModded = isKnownModdedRelic(relicKey) == true;
                if (knownModded)
                {
                    capabilities |= OpeningRewardSupportFlags.UnknownModRewardImpact |
                                    OpeningRewardSupportFlags.IntroducesUnknownRewardHook;
                    reasons.Add($"UnknownModRewardImpact:{relicKey.Serialized}");
                    evidence.Add(new EvidenceCode(
                        $"opening-reward-capability.unknown-mod:{relicKey.Serialized}"));
                }
                else
                {
                    capabilities |= OpeningRewardSupportFlags.VanillaCapabilityNotAudited |
                                    OpeningRewardSupportFlags.IntroducesUnknownRewardHook;
                    reasons.Add($"VanillaRewardCapabilityNotAudited:{relicKey.Serialized}");
                    evidence.Add(new EvidenceCode(
                        $"opening-reward-capability.vanilla-not-audited:{relicKey.Serialized}"));
                }
                continue;
            }

            evidence.Add(new EvidenceCode(record.EvidenceCode));

            OpeningRewardSupportFlags heldCapability = ResolveHeldCapability(profileId, record);
            capabilities |= heldCapability;
            if (!record.IsHeldNeutralForNormalCombatReward)
            {
                var source = new RewardImpactSourceKey(RewardImpactSourceKind.Relic, relicKey);
                if (OpeningCombatRewardImpactAdapterRegistry.TryResolve(profileId, source, out OpeningCombatRewardImpactAdapterDescriptor adapter))
                {
                    supportedImpactSources.Add(source);
                    evidence.Add(adapter.EvidenceCode);
                }
                else
                {
                    rewardDomainResolved = false;
                    if (record.HeldImpactClass == HeldNormalCombatRewardImpactClass.KnownRuntimeConditionalImpact)
                    {
                        capabilities |= OpeningRewardSupportFlags.ConditionalRewardImpactNotModeled;
                        string dependencySuffix = record.RuntimeDependencies.Count == 0
                            ? string.Empty
                            : ":" + string.Join(",", record.RuntimeDependencies.OrderBy(item => item, StringComparer.Ordinal));
                        reasons.Add($"ConditionalRewardImpactNotModeled:{relicKey.Serialized}{dependencySuffix}");
                    }
                    else
                    {
                        capabilities |= OpeningRewardSupportFlags.KnownSpecialRewardImpactNotImplemented;
                        reasons.Add($"SpecialRewardImpactNotImplemented:{relicKey.Serialized}");
                    }
                }
            }

            // The completed direct Neow/Bones route already owns its copied RNG
            // state. Only an ordinary nested obtain that the generic nested
            // projector did not replay may still invalidate that state.
            if (nested.Contains(relicKey) && !record.NestedOnObtainPreservesRewardContinuation)
            {
                rewardDomainResolved = false;
                capabilities |= OpeningRewardSupportFlags.NestedOpeningRewardImpactNotImplemented;
                reasons.Add(
                    $"NestedOpeningRewardImpactNotImplemented:{relicKey.Serialized}:{record.OnObtainCapabilities}");
            }
        }

        RewardImpactSourceKey[] supportedSources = supportedImpactSources
            .Distinct()
            .OrderBy(source => source.Serialized, StringComparer.Ordinal)
            .ToArray();
        string impactFingerprint = OpeningCombatRewardImpactAdapterRegistry.BuildImpactFingerprint(
            profileId,
            supportedSources);
        return new OpeningRewardSupportEvaluation(
            capabilities,
            reasons.Distinct(StringComparer.Ordinal).ToArray(),
            evidence.Where(code => code.IsValid).Distinct().ToArray(),
            rewardDomainResolved)
        {
            SupportedImpactSources = supportedSources,
            RewardImpactFingerprint = impactFingerprint
        };
    }

    private static OpeningRewardSupportFlags ResolveHeldCapability(
        RuntimeProfileId profileId,
        VanillaRelicRewardEffect record)
    {
        if (!record.IsHeldNeutralForNormalCombatReward &&
            OpeningCombatRewardImpactAdapterRegistry.TryResolve(profileId, record.RelicKey, out _))
        {
            return OpeningRewardSupportFlags.ChangesRewardGenerationContext |
                   OpeningRewardSupportFlags.SupportedSpecialRewardImpact;
        }

        return record.HeldImpactClass switch
        {
            HeldNormalCombatRewardImpactClass.Neutral => OpeningRewardSupportFlags.None,
            HeldNormalCombatRewardImpactClass.OtherRewardDomainOnly => OpeningRewardSupportFlags.None,
            HeldNormalCombatRewardImpactClass.KnownRuntimeConditionalImpact =>
                OpeningRewardSupportFlags.ChangesRewardGenerationContext |
                OpeningRewardSupportFlags.ConditionalRewardImpactNotModeled,
            HeldNormalCombatRewardImpactClass.KnownDeterministicImpact =>
                OpeningRewardSupportFlags.ChangesRewardGenerationContext |
                OpeningRewardSupportFlags.KnownSpecialRewardImpactNotImplemented,
            HeldNormalCombatRewardImpactClass.KnownRewardsRngImpact =>
                OpeningRewardSupportFlags.ChangesRewardGenerationContext |
                OpeningRewardSupportFlags.KnownSpecialRewardImpactNotImplemented,
            _ => OpeningRewardSupportFlags.IntroducesUnknownRewardHook
        };
    }
}
