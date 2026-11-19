using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

internal static class EventOpportunitySemantics
{
    internal const string EvidenceCode = "EventResult.EligibleEntryAndBranch";
    internal const string CrystalFilterRemainder = "CrystalSphereRewards:ProductionExactSameBoardPath:ProductionExactOnly";

    internal static bool IsEventLocalOpportunity(EventResultConditionKind kind) => kind is
        EventResultConditionKind.DollRoomRandomRelic or EventResultConditionKind.DollRoomTakeSomeTimeRelic or
        EventResultConditionKind.MuseumOfTortureOfferedRelic or EventResultConditionKind.ThermaeNonupeipeDrinkRelic or
        EventResultConditionKind.EventRelicRarityRoll;

    internal static IReadOnlyList<ModelKey> RelicTargets(EventResultConditionKind kind) => kind switch
    {
        EventResultConditionKind.DollRoomRandomRelic or EventResultConditionKind.DollRoomTakeSomeTimeRelic =>
            EventOpportunityCatalog.Dolls,
        EventResultConditionKind.MuseumOfTortureOfferedRelic => EventOpportunityCatalog.MuseumRelics,
        EventResultConditionKind.ThermaeNonupeipeDrinkRelic => EventOpportunityCatalog.ThermaeRelics,
        _ => []
    };

    internal static bool TryGetEventKey(EventResultSearchCondition condition, out ModelKey eventKey)
    {
        string? entry = condition.Kind switch
        {
            EventResultConditionKind.DollRoomRandomRelic or EventResultConditionKind.DollRoomTakeSomeTimeRelic =>
                EventOpportunityCatalog.DollRoom,
            EventResultConditionKind.MuseumOfTortureOfferedRelic => EventOpportunityCatalog.MuseumOfTorture,
            EventResultConditionKind.ThermaeNonupeipeDrinkRelic => EventOpportunityCatalog.ThermaeNonupeipe,
            EventResultConditionKind.EventRelicRarityRoll or EventResultConditionKind.CrystalSphereRewards =>
                condition.TargetKey.Entry,
            _ => null
        };
        eventKey = entry is null ? default : new ModelKey(BaseGameModelKeys.Categories.Event, entry);
        return eventKey.IsValid;
    }

    internal static bool SupportsProfile(EventResultConditionKind kind, RuntimeProfileId profile,
        RngSemanticProfileId semantics) => RuntimeProfilePolicies.SupportsEventResultModel(profile) && (kind switch
        {
            EventResultConditionKind.DollRoomRandomRelic or EventResultConditionKind.DollRoomTakeSomeTimeRelic or
                EventResultConditionKind.CrystalSphereRewards =>
                semantics is RngSemanticProfileId.Modern110 or RngSemanticProfileId.Modern113,
            EventResultConditionKind.MuseumOfTortureOfferedRelic or EventResultConditionKind.ThermaeNonupeipeDrinkRelic or
                EventResultConditionKind.EventRelicRarityRoll =>
                semantics == RngSemanticProfileId.Modern113,
            _ => true
        });

    internal static string ProfileUnavailable(EventResultConditionKind kind) =>
        "EventResult.SourceModelUnavailableForProfile:" + kind;

    internal static string? FilterRemainder(EventResultConditionKind kind, RuntimeProfileId profile,
        RngSemanticProfileId semantics)
    {
        if ((IsEventLocalOpportunity(kind) || kind == EventResultConditionKind.CrystalSphereRewards) &&
            !SupportsProfile(kind, profile, semantics)) return ProfileUnavailable(kind);
        return kind == EventResultConditionKind.CrystalSphereRewards ? CrystalFilterRemainder : null;
    }

    internal static bool IsValidRarityRoll(EventResultSearchCondition condition) =>
        IsRolledRarity(condition.RelicRarity) &&
        FindRarityOpportunity(condition) is { } opportunity &&
        (condition.RelicRaritySelection switch
        {
            EventRelicRaritySelection.First => true,
            EventRelicRaritySelection.Second or EventRelicRaritySelection.Any or EventRelicRaritySelection.All =>
                opportunity.RollCount >= 2,
            EventRelicRaritySelection.Third => opportunity.RollCount >= 3,
            _ => false
        });

    internal static bool IsValidCrystalTarget(EventResultSearchCondition condition)
    {
        const CrystalSphereSeedCards allCards = CrystalSphereSeedCards.Common | CrystalSphereSeedCards.Uncommon |
            CrystalSphereSeedCards.Rare;
        var cards = condition.CrystalCards.GetValueOrDefault();
        return condition.TargetKey == new ModelKey(BaseGameModelKeys.Categories.Event, EventOpportunityCatalog.CrystalSphere) &&
            condition.CrystalDivinations is 3 or 6 && (cards & ~allCards) == 0 &&
            (condition.CrystalRelics.GetValueOrDefault() & ~(CrystalSphereSearchRelics.Driftwood |
                CrystalSphereSearchRelics.PrismaticGem | CrystalSphereSearchRelics.DingyRug)) == 0 &&
            (condition.CrystalTargetCards is null || condition.CrystalTargetCards.All(key =>
                key.IsValid && key.Category == BaseGameModelKeys.Categories.Card)) &&
            (condition.RelicRarity is null || IsRolledRarity(condition.RelicRarity)) &&
            (cards != CrystalSphereSeedCards.None || condition.RelicRarity is not null || condition.CrystalTargetCards is { Count: > 0 });
    }

    private static bool IsRolledRarity(EffectRelicRarity? rarity) => rarity is
        EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare;

    private static EventRelicRarityOpportunity? FindRarityOpportunity(EventResultSearchCondition condition)
    {
        if (condition.RelicRarityBranch is not { } branch) return null;
        foreach (var opportunity in EventOpportunityCatalog.RarityBranches)
            if (opportunity.Branch == branch && opportunity.EventKey == condition.TargetKey)
                return opportunity;
        return null;
    }

    internal static bool Matches(ulong root, int slot, RngSemanticProfileId semantics,
        EventResultSearchCondition condition)
    {
        if (condition.Kind == EventResultConditionKind.EventRelicRarityRoll)
            return MatchesRarity(root, slot, condition);

        if (!TryGetEventKey(condition, out var eventKey))
            throw new ArgumentOutOfRangeException(nameof(condition));
        var rng = new Xoshiro256StarStar(Beta109WorldRng.DeriveEventLocalSeed(root, slot, false, eventKey.Entry));
        return condition.Kind switch
        {
            EventResultConditionKind.DollRoomRandomRelic => Beta111EventRules.RandomDoll(rng) == condition.TargetKey,
            EventResultConditionKind.DollRoomTakeSomeTimeRelic =>
                Beta111EventRules.DollOffers(rng, 2, semantics).Contains(condition.TargetKey),
            EventResultConditionKind.MuseumOfTortureOfferedRelic => Beta111EventRules.MuseumOfferedRelic(rng) == condition.TargetKey,
            _ => Beta111EventRules.ThermaeDrinkRelic(rng) == condition.TargetKey
        };
    }

    private static bool MatchesRarity(ulong root, int slot, EventResultSearchCondition condition)
    {
        var opportunity = FindRarityOpportunity(condition) ??
            throw new ArgumentOutOfRangeException(nameof(condition));
        Span<EffectRelicRarity> draws = stackalloc EffectRelicRarity[opportunity.RollCount];
        if (!EventRelicRarityProjector.Project(root, slot, opportunity, draws)) return false;
        var target = condition.RelicRarity!.Value;
        return condition.RelicRaritySelection switch
        {
            EventRelicRaritySelection.Any => AnyRarityIs(draws, target),
            EventRelicRaritySelection.All => AllRaritiesAre(draws, target),
            EventRelicRaritySelection.First => draws[0] == target,
            EventRelicRaritySelection.Second => draws[1] == target,
            EventRelicRaritySelection.Third => draws[2] == target,
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };
    }

    private static bool AnyRarityIs(ReadOnlySpan<EffectRelicRarity> draws, EffectRelicRarity target)
    {
        foreach (var draw in draws)
            if (draw == target) return true;
        return false;
    }

    private static bool AllRaritiesAre(ReadOnlySpan<EffectRelicRarity> draws, EffectRelicRarity target)
    {
        foreach (var draw in draws)
            if (draw != target) return false;
        return true;
    }
}
