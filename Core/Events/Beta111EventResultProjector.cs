using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

[Flags]
public enum EventResultCatalogParts
{
    None = 0, TrashGrab = 1, TrashDive = 2, FakeMerchant = 4,
    All = TrashGrab | TrashDive | FakeMerchant
}

public sealed record Beta111EventResultAuthority(
    ModelKey OwnerCharacterKey,
    IReadOnlyList<ModelKey> UnlockedCharacterCardPoolKeys,
    bool StaticCatalogAuthorityExact,
    bool ColorfulPoolAuthorityExact,
    RuntimeProfileId ProfileId,
    string AuthorityFingerprint)
{
    public EventResultCatalogParts CatalogParts { get; init; }
    internal IReadOnlyList<ModelKey>? CapturedColors { get; init; }
    internal ModelKey[] ColorfulCandidates() => CapturedColors?.ToArray() ??
        Beta111EventRules.ColorfulCandidates(OwnerCharacterKey, UnlockedCharacterCardPoolKeys, RngSemantics);
    public bool HasCatalog(EventResultCatalogParts part) => StaticCatalogAuthorityExact || CatalogParts.HasFlag(part);
    public bool IsBeta111 => RuntimeProfilePolicies.SupportsEventResultModel(ProfileId);
    public RngSemanticProfileId RngSemantics { get; init; } = RngSemanticProfileId.Modern110;
    internal bool UsesChildShuffle => RuntimeProfilePolicies.UsesChildStreamShuffle(RngSemantics);
    internal IReadOnlyList<ModelKey> ColorfulOrder => Beta111EventResultCatalog.ColorfulOrder(RngSemantics);

    public static Beta111EventResultAuthority From(
        RuntimeContextAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        NeowEffectAuthoritySnapshot? effects = authority.EffectAuthority;
        return new Beta111EventResultAuthority(
            authority.Character.CharacterKey,
            effects?.UnlockedCharacterCardPoolKeys ?? Array.Empty<ModelKey>(),
            effects?.EventResultStaticCatalogExact == true,
            effects?.EventColorfulCharacterPoolsExact == true,
            authority.ProfileId,
            effects?.SnapshotFingerprint ?? authority.CatalogFingerprint)
        { RngSemantics = authority.VersionResolution.SemanticProfileId,
            CapturedColors = effects?.ColorfulCandidateColors,
            CatalogParts = effects?.EventCatalogParts ?? EventResultCatalogParts.None };
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
    public PredictionPrecision TrashGrabPrecision { get; init; } = TrashHeapPrecision;
    public PredictionPrecision TrashDivePrecision { get; init; } = TrashHeapPrecision;
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
    internal const string StaticCatalogIncomplete = "EventResultV1StaticCatalogAuthorityIncomplete";

    public static Beta111EventResultProjection Project(
        ulong rootHash,
        int playerSlotIndex,
        Beta111EventResultAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.IsBeta111)
            return Beta111EventResultProjection.Unsupported("EventResultV1RequiresBeta111");
        ModelKey grab = default, dive = default;
        ModelKey[] fakeSix = [];
        bool canGrab = authority.HasCatalog(EventResultCatalogParts.TrashGrab);
        bool canDive = authority.HasCatalog(EventResultCatalogParts.TrashDive);
        bool canOffer = authority.HasCatalog(EventResultCatalogParts.FakeMerchant);
        if (canGrab || canDive)
        {
            var trashRng = EventRng(rootHash, playerSlotIndex, false, Beta111EventResultCatalog.TrashHeapEventEntry);
            if (canGrab) grab = Beta111EventRules.TrashHeapGrab(trashRng.Clone());
            if (canDive) dive = Beta111EventRules.TrashHeapDive(trashRng);
        }
        if (canOffer)
            fakeSix = Beta111EventRules.FakeMerchantInventory(EventRng(rootHash, playerSlotIndex, true,
                Beta111EventResultCatalog.FakeMerchantEventEntry), authority.RngSemantics);

        ModelKey[] colorful = Array.Empty<ModelKey>();
        PredictionPrecision colorfulPrecision = PredictionPrecision.Unknown;
        if (authority.ColorfulPoolAuthorityExact && authority.OwnerCharacterKey.IsValid)
        {
            colorful = Beta111EventRules.ColorfulOffers(EventRng(rootHash, playerSlotIndex, false,
                Beta111EventResultCatalog.ColorfulPhilosophersEventEntry),
                authority.ColorfulCandidates());
            colorfulPrecision = PredictionPrecision.Exact;
        }

        return new Beta111EventResultProjection(
            grab,
            dive,
            colorful,
            fakeSix,
            canGrab && canDive ? PredictionPrecision.Exact : PredictionPrecision.Unsupported,
            colorfulPrecision,
            canOffer ? PredictionPrecision.Exact : PredictionPrecision.Unsupported,
            authority.UsesChildShuffle ? "beta113.event-result.source-audited" : "beta111.event-result-v1.source-audited")
        {
            TrashGrabPrecision = canGrab ? PredictionPrecision.Exact : PredictionPrecision.Unsupported,
            TrashDivePrecision = canDive ? PredictionPrecision.Exact : PredictionPrecision.Unsupported
        };
    }

    private static Xoshiro256StarStar EventRng(ulong rootHash, int playerSlotIndex,
        bool isShared, string eventEntry) => new(Beta109WorldRng.DeriveEventLocalSeed(
            rootHash, playerSlotIndex, isShared, eventEntry));
}
