using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Rewards;

/// <summary>
/// Source-kind space for reward-impact adapters. Production registration remains
/// relic-only. Card/Modifier/Power are reserved so later sparse special-card
/// support does not require a full deck fingerprint or a new route contract.
/// </summary>
public enum RewardImpactSourceKind
{
    Relic,
    Card,
    Modifier,
    Power
}

public readonly record struct RewardImpactSourceKey(
    RewardImpactSourceKind Kind,
    ModelKey ModelKey)
{
    public bool IsValid => ModelKey.IsValid;

    public string Serialized => $"{Kind}:{ModelKey.Serialized}";

    public override string ToString() => Serialized;
}

public enum OpeningCombatRewardImpactOperationKind
{
    ForcePotionReward,
    AddCardReward,
    ForceUpgradeCardType,
    AddFixedGoldReward,
    AddPowerCardEveryOtherCombat,
    EnchantFirstCardRewardWithGlam,
    UpgradeNextCardRewards
}

/// <summary>
/// One audited, pure operation contributed by a dedicated opening-relic
/// adapter. Operations describe reward-domain behavior only; no real Hook,
/// Reward, RunState, Player or Godot object is ever invoked.
/// </summary>
public sealed record OpeningCombatRewardImpactOperation(
    OpeningCombatRewardImpactOperationKind Kind,
    int Amount = 0,
    EffectCardType? CardType = null,
    bool AddedCardRewardIsFromCombat = false,
    bool RequiresIsFromCombat = false);

public sealed record OpeningCombatRewardImpactAdapterDescriptor(
    RewardImpactSourceKey Source,
    string AdapterId,
    IReadOnlyList<OpeningCombatRewardImpactOperation> Operations,
    EvidenceCode EvidenceCode)
{
    public string StateFingerprint => OpeningCombatRewardImpactAdapterRegistry.FingerprintParts(
        "opening-combat-reward-impact-adapter-state-v2",
        AdapterId,
        Source.Serialized,
        string.Join(",", Operations.Select(operation =>
            $"{operation.Kind}:{operation.Amount}:{operation.CardType}:{operation.AddedCardRewardIsFromCombat}:{operation.RequiresIsFromCombat}")));
}

/// <summary>
/// Dedicated, explicit registration for opening-reachable vanilla relics whose
/// held effect changes the normal-Monster reward pipeline. Shared operations
/// avoid duplicating the reward algorithm, while every relic remains registered
/// by exact ModelKey and direct-source evidence.
/// </summary>
public static class OpeningCombatRewardImpactAdapterRegistry
{
    public const string RegistryVersion = "opening-relic-reward-impact-adapters-batch-b-v1";

    private static readonly IReadOnlyDictionary<string, Func<RuntimeProfileId, OpeningCombatRewardImpactAdapterDescriptor>> RelicAdapters =
        new Dictionary<string, Func<RuntimeProfileId, OpeningCombatRewardImpactAdapterDescriptor>>(StringComparer.Ordinal)
        {
            ["AMETHYST_AUBERGINE"] = profileId => Descriptor(
                profileId,
                "AMETHYST_AUBERGINE",
                "amethyst-aubergine",
                "vanilla-relic-reward-adapter.amethyst-aubergine.fixed-gold-entry",
                new OpeningCombatRewardImpactOperation(
                    OpeningCombatRewardImpactOperationKind.AddFixedGoldReward,
                    Amount: 15)),
            ["FROZEN_EGG"] = profileId => Descriptor(
                profileId,
                "FROZEN_EGG",
                "frozen-egg",
                "vanilla-relic-reward-adapter.frozen-egg.power-upgrade",
                new OpeningCombatRewardImpactOperation(
                    OpeningCombatRewardImpactOperationKind.ForceUpgradeCardType,
                    CardType: EffectCardType.Power)),
            ["LASTING_CANDY"] = profileId => Descriptor(
                profileId,
                "LASTING_CANDY",
                "lasting-candy",
                RuntimeProfilePolicies.UsesBeta110SharedAlgorithms(profileId)
                    ? $"vanilla-relic-reward-adapter.lasting-candy.{profileId.ToString().ToLowerInvariant()}.every-other-combat-power"
                    : profileId == RuntimeProfileId.Beta109
                        ? "vanilla-relic-reward-adapter.lasting-candy.beta109-historical-donor.every-other-combat-power"
                        : "vanilla-relic-reward-adapter.lasting-candy.stable107.every-other-combat-power",
                new OpeningCombatRewardImpactOperation(
                    OpeningCombatRewardImpactOperationKind.AddPowerCardEveryOtherCombat,
                    Amount: 1,
                    CardType: EffectCardType.Power,
                    RequiresIsFromCombat: RuntimeProfilePolicies.IsModernCore(profileId))),
            ["MOLTEN_EGG"] = profileId => Descriptor(
                profileId,
                "MOLTEN_EGG",
                "molten-egg",
                "vanilla-relic-reward-adapter.molten-egg.attack-upgrade",
                new OpeningCombatRewardImpactOperation(
                    OpeningCombatRewardImpactOperationKind.ForceUpgradeCardType,
                    CardType: EffectCardType.Attack)),
            ["PRAYER_WHEEL"] = profileId => Descriptor(
                profileId,
                "PRAYER_WHEEL",
                "prayer-wheel",
                "vanilla-relic-reward-adapter.prayer-wheel.extra-card-reward",
                new OpeningCombatRewardImpactOperation(
                    OpeningCombatRewardImpactOperationKind.AddCardReward,
                    Amount: 1,
                    AddedCardRewardIsFromCombat: false)),
            ["SILKEN_TRESS"] = profileId => Descriptor(
                profileId,
                "SILKEN_TRESS",
                "silken-tress",
                "vanilla-relic-reward-adapter.silken-tress.first-card-reward-glam",
                new OpeningCombatRewardImpactOperation(
                    OpeningCombatRewardImpactOperationKind.EnchantFirstCardRewardWithGlam,
                    Amount: 1)),
            ["SILVER_CRUCIBLE"] = profileId => Descriptor(
                profileId,
                "SILVER_CRUCIBLE",
                "silver-crucible",
                "vanilla-relic-reward-adapter.silver-crucible.first-three-combat-card-rewards",
                new OpeningCombatRewardImpactOperation(
                    OpeningCombatRewardImpactOperationKind.UpgradeNextCardRewards,
                    Amount: 3)),
            ["TOXIC_EGG"] = profileId => Descriptor(
                profileId,
                "TOXIC_EGG",
                "toxic-egg",
                "vanilla-relic-reward-adapter.toxic-egg.skill-upgrade",
                new OpeningCombatRewardImpactOperation(
                    OpeningCombatRewardImpactOperationKind.ForceUpgradeCardType,
                    CardType: EffectCardType.Skill)),
            ["WHITE_BEAST_STATUE"] = profileId => Descriptor(
                profileId,
                "WHITE_BEAST_STATUE",
                "white-beast-statue",
                "vanilla-relic-reward-adapter.white-beast-statue.force-potion",
                new OpeningCombatRewardImpactOperation(
                    OpeningCombatRewardImpactOperationKind.ForcePotionReward))
        };

    public static IReadOnlyList<string> SupportedRelicEntries { get; } =
        RelicAdapters.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

    public static bool TryResolve(
        RuntimeProfileId profileId,
        RewardImpactSourceKey source,
        out OpeningCombatRewardImpactAdapterDescriptor descriptor)
    {
        descriptor = default!;
        if (!source.IsValid || source.Kind != RewardImpactSourceKind.Relic)
        {
            return false;
        }

        if (profileId != RuntimeProfileId.Stable107 && !RuntimeProfilePolicies.IsModernCore(profileId) ||
            source.ModelKey.Category != BaseGameModelKeys.Categories.Relic ||
            !RelicAdapters.TryGetValue(source.ModelKey.Entry, out Func<RuntimeProfileId, OpeningCombatRewardImpactAdapterDescriptor>? factory))
        {
            return false;
        }

        descriptor = factory(profileId);
        return true;
    }

    public static bool TryResolve(
        RuntimeProfileId profileId,
        ModelKey relicKey,
        out OpeningCombatRewardImpactAdapterDescriptor descriptor) =>
        TryResolve(
            profileId,
            new RewardImpactSourceKey(RewardImpactSourceKind.Relic, relicKey),
            out descriptor);

    public static string BuildImpactFingerprint(
        RuntimeProfileId profileId,
        IEnumerable<RewardImpactSourceKey> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        string[] states = sources
            .Where(source => source.IsValid)
            .Distinct()
            .OrderBy(source => source.Serialized, StringComparer.Ordinal)
            .Select(source => TryResolve(profileId, source, out OpeningCombatRewardImpactAdapterDescriptor descriptor)
                ? descriptor.StateFingerprint
                : $"unsupported:{source.Serialized}")
            .ToArray();
        return FingerprintParts(
            "opening-combat-reward-impact-fingerprint-v2",
            profileId.ToString(),
            RegistryVersion,
            string.Join("|", states));
    }

    internal static string FingerprintParts(params string[] values)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join("|", values));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static OpeningCombatRewardImpactAdapterDescriptor Descriptor(
        RuntimeProfileId profileId,
        string relicEntry,
        string adapterId,
        string evidenceCode,
        params OpeningCombatRewardImpactOperation[] operations)
    {
        return new OpeningCombatRewardImpactAdapterDescriptor(
            new RewardImpactSourceKey(
                RewardImpactSourceKind.Relic,
                new ModelKey(BaseGameModelKeys.Categories.Relic, relicEntry)),
            $"{profileId}.{adapterId}",
            operations,
            new EvidenceCode(evidenceCode));
    }
}
