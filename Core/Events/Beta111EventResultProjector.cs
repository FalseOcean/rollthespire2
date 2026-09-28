using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

public sealed record Beta111EventResultAuthority(
    ModelKey OwnerCharacterKey,
    IReadOnlyList<ModelKey> UnlockedCharacterCardPoolKeys,
    bool StaticCatalogAuthorityExact,
    bool ColorfulPoolAuthorityExact,
    RuntimeProfileId ProfileId,
    string AuthorityFingerprint)
{
    public bool IsBeta111 => ProfileId == RuntimeProfileId.Beta111;

    public static Beta111EventResultAuthority From(
        RuntimeContextAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        NeowEffectAuthoritySnapshot? effects = authority.EffectAuthority;
        return new Beta111EventResultAuthority(
            authority.Character.CharacterKey,
            effects?.UnlockedCharacterCardPoolKeys ?? Array.Empty<ModelKey>(),
            effects?.EventResultStaticCatalogExact == true,
            effects?.EventColorfulCharacterPoolsExact == true || authority.UsesBestEffortModel && effects?.UnlockedCharacterCardPoolKeys is { Count: > 0 },
            authority.ProfileId,
            effects?.SnapshotFingerprint ?? authority.CatalogFingerprint);
    }
}

public sealed record Beta111EventResultProjection(
    ModelKey TrashHeapGrabCard,
    ModelKey TrashHeapDiveRelic,
    IReadOnlyList<ModelKey> ColorfulOfferedColors,
    IReadOnlyList<ModelKey> FakeMerchantInventory,
    PredictionPrecision TrashHeapPrecision,
    PredictionPrecision ColorfulPrecision,
    PredictionPrecision FakeMerchantPrecision,
    string EvidenceCode)
{
    public static Beta111EventResultProjection Unsupported(string code) => new(
        default,
        default,
        Array.Empty<ModelKey>(),
        Array.Empty<ModelKey>(),
        PredictionPrecision.Unsupported,
        PredictionPrecision.Unsupported,
        PredictionPrecision.Unsupported,
        code);
}

/// <summary>
/// Pure Beta111 reconstruction of the conditional event-local observables frozen for
/// Event Result v1. This class never proves event occurrence/reachability and never reads
/// or advances UpFront/Rewards/Shops continuation state.
/// </summary>
public static class Beta111EventResultProjector
{
    public static Beta111EventResultProjection Project(
        ulong rootHash,
        int playerSlotIndex,
        Beta111EventResultAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.IsBeta111)
            return Beta111EventResultProjection.Unsupported("EventResultV1RequiresBeta111");
        if (!authority.StaticCatalogAuthorityExact)
            return Beta111EventResultProjection.Unsupported("EventResultV1StaticCatalogAuthorityIncomplete");

        ModelKey grab = ProjectFreshRootSingle(
            rootHash,
            playerSlotIndex,
            isShared: false,
            Beta111EventResultCatalog.TrashHeapEventEntry,
            Beta111EventResultCatalog.TrashHeapGrabCards,
            "trash-grab");
        ModelKey dive = ProjectFreshRootSingle(
            rootHash,
            playerSlotIndex,
            isShared: false,
            Beta111EventResultCatalog.TrashHeapEventEntry,
            Beta111EventResultCatalog.TrashHeapDiveRelics,
            "trash-dive");

        var fake = Beta111EventResultCatalog.FakeMerchantRelics.ToList();
        Beta109WorldRng fakeRng = Beta109WorldRng.CreateEventLocal(
            rootHash,
            playerSlotIndex,
            isShared: true,
            Beta111EventResultCatalog.FakeMerchantEventEntry);
        fakeRng.UnstableShuffle(fake, "fake-merchant-inventory");
        ModelKey[] fakeSix = fake.Take(6).ToArray();

        ModelKey[] colorful = Array.Empty<ModelKey>();
        PredictionPrecision colorfulPrecision = PredictionPrecision.Unknown;
        if (authority.ColorfulPoolAuthorityExact && authority.OwnerCharacterKey.IsValid)
        {
            var unlocked = new HashSet<ModelKey>(authority.UnlockedCharacterCardPoolKeys, ModelKeyComparer.Instance);
            var candidates = Beta111EventResultCatalog.ColorfulCharacterOrder
                .Where(key => key != authority.OwnerCharacterKey && unlocked.Contains(key))
                .ToList();
            Beta109WorldRng colorfulRng = Beta109WorldRng.CreateEventLocal(
                rootHash,
                playerSlotIndex,
                isShared: false,
                Beta111EventResultCatalog.ColorfulPhilosophersEventEntry);
            int targetCount = Math.Min(3, candidates.Count);
            while (candidates.Count > targetCount)
            {
                int remove = colorfulRng.NextInt(candidates.Count, "colorful-remove");
                candidates.RemoveAt(remove);
            }
            colorful = candidates.ToArray();
            colorfulPrecision = PredictionPrecision.Exact;
        }

        return new Beta111EventResultProjection(
            grab,
            dive,
            colorful,
            fakeSix,
            PredictionPrecision.Exact,
            colorfulPrecision,
            PredictionPrecision.Exact,
            "beta111.event-result-v1.source-audited");
    }

    private static ModelKey ProjectFreshRootSingle(
        ulong rootHash,
        int playerSlotIndex,
        bool isShared,
        string eventEntry,
        IReadOnlyList<ModelKey> source,
        string stage)
    {
        Beta109WorldRng rng = Beta109WorldRng.CreateEventLocal(
            rootHash,
            playerSlotIndex,
            isShared,
            eventEntry);
        return rng.NextModelKey(source, stage);
    }
}
