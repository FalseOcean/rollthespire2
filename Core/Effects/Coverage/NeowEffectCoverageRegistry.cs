using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects.Coverage;

public enum NeowEffectImplementationStatus
{
    Implemented,
    NotImplemented,
    UnsupportedEffectType,
    NotApplicable
}



[Flags]
public enum NeowEffectTraits
{
    None = 0,
    IdentityOnly = 1 << 0,
    DeterministicOpeningEffect = 1 << 1,
    RandomOffer = 1 << 2,
    DeckMutation = 1 << 3,
    RelevantRngConsumer = 1 << 4,
    ProducesPlayerChoiceDeckMutation = 1 << 5,
    HasCardOfferSelection = 1 << 6,
    HasOptionalSkip = 1 << 7,
    HasMultipleIndependentSelectionSteps = 1 << 8,
    WritesShadowDeck = 1 << 9,
    AffectsAutomaticUpgradeCandidatePool = 1 << 10,
    NestedRelicSource = 1 << 11,
    FinalCurseRelevant = 1 << 12
}

public enum NeowEffectFamily
{
    DeterministicImmediate,
    SimpleRngOffer,
    OrderedOffer,
    ShadowDeck,
    FinitePlayerChoice,
    NestedObtain,
    Bones,
    FutureRuntime,
    MultiplayerOnly
}

public sealed record NeowEffectCoverageEntry(
    ModelKey RelicKey,
    bool Stable107Applicable,
    bool Beta109Applicable,
    NeowEffectFamily Family,
    NeowEffectImplementationStatus Stable107Implementation,
    NeowEffectImplementationStatus Beta109Implementation,
    string Stable107EvidenceCode,
    string Beta109EvidenceCode,
    string RemainingLimitation,
    NeowEffectTraits ProductCapabilities = NeowEffectTraits.None)
{
    public bool HasProductCapability(NeowEffectTraits capability) =>
        (ProductCapabilities & capability) == capability;
}

/// <summary>
/// Complete vanilla Neow identity/effect registry. This registry classifies
/// implementation capability only; execution precision is returned by the
/// production projector for each request.
/// </summary>
public static class NeowEffectCoverageRegistry
{
    private static readonly IReadOnlyList<NeowEffectCoverageEntry> EntriesValue = BuildEntries();
    private static readonly IReadOnlyDictionary<ModelKey, NeowEffectCoverageEntry> ByKey =
        EntriesValue.ToDictionary(entry => entry.RelicKey);

    public static IReadOnlyList<NeowEffectCoverageEntry> Entries => EntriesValue;

    public static IReadOnlyList<ModelKey> GetApplicableKeys(RuntimeProfileId profileId) => EntriesValue
        .Where(entry => profileId switch
        {
            RuntimeProfileId.Stable107 => entry.Stable107Applicable,
            RuntimeProfileId.Beta109 or RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 => entry.Beta109Applicable,
            _ => false
        })
        .Select(entry => entry.RelicKey)
        .ToArray();

    public static bool TryGet(ModelKey key, out NeowEffectCoverageEntry entry) =>
        ByKey.TryGetValue(key, out entry!);

    public static NeowEffectTraits GetProductCapabilities(ModelKey key) =>
        TryGet(key, out NeowEffectCoverageEntry entry)
            ? entry.ProductCapabilities
            : NeowEffectTraits.None;

    public static bool HasProductCapability(
        ModelKey key,
        NeowEffectTraits capability) =>
        (GetProductCapabilities(key) & capability) == capability;

    public static NeowEffectImplementationStatus GetImplementation(
        RuntimeProfileId profileId,
        ModelKey key)
    {
        if (!TryGet(key, out NeowEffectCoverageEntry entry))
        {
            return NeowEffectImplementationStatus.NotImplemented;
        }

        return profileId switch
        {
            RuntimeProfileId.Stable107 when !entry.Stable107Applicable => NeowEffectImplementationStatus.NotApplicable,
            RuntimeProfileId.Stable107 => entry.Stable107Implementation,
            RuntimeProfileId.Beta109 or RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 when !entry.Beta109Applicable => NeowEffectImplementationStatus.NotApplicable,
            RuntimeProfileId.Beta109 or RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 => entry.Beta109Implementation,
            _ => NeowEffectImplementationStatus.NotApplicable
        };
    }

    private static IReadOnlyList<NeowEffectCoverageEntry> BuildEntries()
    {
        var legacy = new HashSet<ModelKey>(Stable107NeowCatalog.All);
        var modern = new HashSet<ModelKey>(BaseGameModelKeys.Relics.AllNeow);

        NeowEffectFamily Family(ModelKey key)
        {
            if (key == BaseGameModelKeys.Relics.CursedPearl ||
                key == BaseGameModelKeys.Relics.DowsingRod ||
                key == BaseGameModelKeys.Relics.GoldenPearl ||
                key == BaseGameModelKeys.Relics.NeowsSacrifice ||
                key == BaseGameModelKeys.Relics.NeowsTorment ||
                key == BaseGameModelKeys.Relics.NutritiousOyster)
            {
                return NeowEffectFamily.DeterministicImmediate;
            }

            if (key == BaseGameModelKeys.Relics.ArcaneScroll ||
                key == BaseGameModelKeys.Relics.PhialHolster ||
                key == BaseGameModelKeys.Relics.LeafyPoultice)
            {
                return key == BaseGameModelKeys.Relics.LeafyPoultice
                    ? NeowEffectFamily.ShadowDeck
                    : NeowEffectFamily.SimpleRngOffer;
            }

            if (key == BaseGameModelKeys.Relics.HeftyTablet ||
                key == BaseGameModelKeys.Relics.Kaleidoscope ||
                key == BaseGameModelKeys.Relics.LeadPaperweight ||
                key == BaseGameModelKeys.Relics.LostCoffer ||
                key == BaseGameModelKeys.Relics.ScrollBoxes)
            {
                return NeowEffectFamily.OrderedOffer;
            }

            if (key == BaseGameModelKeys.Relics.NewLeaf ||
                key == BaseGameModelKeys.Relics.Pomander ||
                key == BaseGameModelKeys.Relics.PreciseScissors ||
                key == BaseGameModelKeys.Relics.PrecariousShears)
            {
                return NeowEffectFamily.FinitePlayerChoice;
            }

            if (key == BaseGameModelKeys.Relics.NeowsTalisman)
            {
                return NeowEffectFamily.ShadowDeck;
            }

            if (key == BaseGameModelKeys.Relics.LargeCapsule || key == BaseGameModelKeys.Relics.SmallCapsule)
            {
                return NeowEffectFamily.NestedObtain;
            }

            if (key == BaseGameModelKeys.Relics.NeowsBones)
            {
                return NeowEffectFamily.Bones;
            }

            if (key == BaseGameModelKeys.Relics.MassiveScroll)
            {
                return NeowEffectFamily.MultiplayerOnly;
            }

            return NeowEffectFamily.FutureRuntime;
        }

        NeowEffectTraits ProductCapabilities(ModelKey key, NeowEffectFamily family)
        {
            NeowEffectTraits capabilities = family switch
            {
                NeowEffectFamily.DeterministicImmediate =>
                    NeowEffectTraits.DeterministicOpeningEffect |
                    NeowEffectTraits.FinalCurseRelevant,
                NeowEffectFamily.SimpleRngOffer =>
                    NeowEffectTraits.RandomOffer |
                    NeowEffectTraits.RelevantRngConsumer |
                    NeowEffectTraits.FinalCurseRelevant,
                NeowEffectFamily.OrderedOffer =>
                    NeowEffectTraits.RandomOffer |
                    NeowEffectTraits.RelevantRngConsumer |
                    NeowEffectTraits.DeckMutation |
                    NeowEffectTraits.ProducesPlayerChoiceDeckMutation |
                    NeowEffectTraits.HasCardOfferSelection |
                    NeowEffectTraits.WritesShadowDeck |
                    NeowEffectTraits.AffectsAutomaticUpgradeCandidatePool |
                    NeowEffectTraits.FinalCurseRelevant,
                NeowEffectFamily.ShadowDeck =>
                    NeowEffectTraits.DeckMutation |
                    NeowEffectTraits.WritesShadowDeck |
                    NeowEffectTraits.AffectsAutomaticUpgradeCandidatePool |
                    NeowEffectTraits.FinalCurseRelevant,
                NeowEffectFamily.FinitePlayerChoice =>
                    NeowEffectTraits.DeckMutation |
                    NeowEffectTraits.ProducesPlayerChoiceDeckMutation |
                    NeowEffectTraits.WritesShadowDeck |
                    NeowEffectTraits.AffectsAutomaticUpgradeCandidatePool |
                    NeowEffectTraits.FinalCurseRelevant,
                NeowEffectFamily.NestedObtain =>
                    NeowEffectTraits.NestedRelicSource |
                    NeowEffectTraits.RelevantRngConsumer |
                    NeowEffectTraits.DeckMutation |
                    NeowEffectTraits.WritesShadowDeck |
                    NeowEffectTraits.AffectsAutomaticUpgradeCandidatePool |
                    NeowEffectTraits.FinalCurseRelevant,
                NeowEffectFamily.Bones =>
                    NeowEffectTraits.NestedRelicSource |
                    NeowEffectTraits.RelevantRngConsumer |
                    NeowEffectTraits.FinalCurseRelevant,
                _ => NeowEffectTraits.IdentityOnly
            };

            if (key == BaseGameModelKeys.Relics.CursedPearl ||
                key == BaseGameModelKeys.Relics.DowsingRod ||
                key == BaseGameModelKeys.Relics.NeowsTorment ||
                key == BaseGameModelKeys.Relics.NeowsSacrifice ||
                key == BaseGameModelKeys.Relics.ArcaneScroll ||
                key == BaseGameModelKeys.Relics.LeafyPoultice ||
                key == BaseGameModelKeys.Relics.NeowsTalisman)
            {
                capabilities |=
                    NeowEffectTraits.DeckMutation |
                    NeowEffectTraits.WritesShadowDeck |
                    NeowEffectTraits.AffectsAutomaticUpgradeCandidatePool;
            }

            if (key == BaseGameModelKeys.Relics.HeftyTablet ||
                key == BaseGameModelKeys.Relics.LeadPaperweight ||
                key == BaseGameModelKeys.Relics.LostCoffer ||
                key == BaseGameModelKeys.Relics.Kaleidoscope)
            {
                capabilities |= NeowEffectTraits.HasOptionalSkip;
            }

            if (key == BaseGameModelKeys.Relics.Kaleidoscope)
            {
                capabilities |= NeowEffectTraits.HasMultipleIndependentSelectionSteps;
            }

            // These later passive relics have no audited opening-deck/RNG/final-curse
            // impact. Their full semantics may remain partial without poisoning
            // the product-relevant Bones result.
            if (key == BaseGameModelKeys.Relics.GoldenPearl ||
                key == BaseGameModelKeys.Relics.LavaRock ||
                key == BaseGameModelKeys.Relics.StoneHumidifier)
            {
                capabilities = NeowEffectTraits.IdentityOnly;
            }

            return capabilities;
        }

        return BaseGameModelKeys.Relics.AllNeow.Select(key =>
        {
            bool l = legacy.Contains(key);
            bool m = modern.Contains(key);
            NeowEffectFamily family = Family(key);
            NeowEffectImplementationStatus lStatus = l
                ? (family == NeowEffectFamily.MultiplayerOnly
                    ? NeowEffectImplementationStatus.NotApplicable
                    : NeowEffectImplementationStatus.Implemented)
                : NeowEffectImplementationStatus.NotApplicable;
            NeowEffectImplementationStatus mStatus = m
                ? NeowEffectImplementationStatus.Implemented
                : NeowEffectImplementationStatus.NotApplicable;

            string limitation = family switch
            {
                NeowEffectFamily.SimpleRngOffer => "Exact execution requires an exact ordered reward/potion snapshot and no-op hook authority.",
                NeowEffectFamily.OrderedOffer => "Exact execution requires exact ordered pools and reward hook authority.",
                NeowEffectFamily.ShadowDeck => "Exact execution requires an exact ordered deck and transform pools.",
                NeowEffectFamily.FinitePlayerChoice => "All finite legal routes are emitted only when exact deck/pool authority is present.",
                NeowEffectFamily.NestedObtain => "Unknown ordinary relic hooks downgrade the route to Partial/Unknown.",
                NeowEffectFamily.Bones => "Exact branch output requires exact eligible relic/curse pools and nested effect authority.",
                NeowEffectFamily.FutureRuntime => "Immediate projection is description-only; future route state is not executed.",
                NeowEffectFamily.MultiplayerOnly => "Modern-profile multiplayer-only; single-player requests are NotApplicable.",
                _ => string.Empty
            };

            return new NeowEffectCoverageEntry(
                key,
                l,
                m,
                family,
                lStatus,
                mStatus,
                l ? $"stable107.neow-effect.{key.Entry.ToLowerInvariant().Replace('_', '-')}" : "stable107.not-applicable",
                m ? $"modern-shared.neow-effect.{key.Entry.ToLowerInvariant().Replace('_', '-')}" : "modern-shared.not-applicable",
                limitation,
                ProductCapabilities(key, family));
        }).ToArray();
    }
}

internal static class Stable107NeowCatalog
{
    public static IReadOnlyList<ModelKey> All { get; } = new[]
    {
        BaseGameModelKeys.Relics.CursedPearl,
        BaseGameModelKeys.Relics.HeftyTablet,
        BaseGameModelKeys.Relics.LargeCapsule,
        BaseGameModelKeys.Relics.LeafyPoultice,
        BaseGameModelKeys.Relics.NeowsBones,
        BaseGameModelKeys.Relics.PrecariousShears,
        BaseGameModelKeys.Relics.SilkenTress,
        BaseGameModelKeys.Relics.SilverCrucible,
        BaseGameModelKeys.Relics.ArcaneScroll,
        BaseGameModelKeys.Relics.BoomingConch,
        BaseGameModelKeys.Relics.FishingRod,
        BaseGameModelKeys.Relics.GoldenPearl,
        BaseGameModelKeys.Relics.Kaleidoscope,
        BaseGameModelKeys.Relics.LeadPaperweight,
        BaseGameModelKeys.Relics.LostCoffer,
        BaseGameModelKeys.Relics.NeowsTorment,
        BaseGameModelKeys.Relics.NewLeaf,
        BaseGameModelKeys.Relics.PhialHolster,
        BaseGameModelKeys.Relics.PreciseScissors,
        BaseGameModelKeys.Relics.ScrollBoxes,
        BaseGameModelKeys.Relics.WingedBoots,
        BaseGameModelKeys.Relics.LavaRock,
        BaseGameModelKeys.Relics.SmallCapsule,
        BaseGameModelKeys.Relics.NutritiousOyster,
        BaseGameModelKeys.Relics.StoneHumidifier,
        BaseGameModelKeys.Relics.NeowsTalisman,
        BaseGameModelKeys.Relics.Pomander
    };
}
