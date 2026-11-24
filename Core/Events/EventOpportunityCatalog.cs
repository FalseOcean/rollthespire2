using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Events;

public enum EventRelicRarityBranch
{
    Setting3,
    OfferTribute,
    GivePotion,
    GiveGold,
    GiveRelic,
    TradeOffers,
    ContinueFight,
    Ornate,
    Kill,
    MerchantGuilty,
    BeatUp
}

public enum EventRelicRaritySelection { First, Second, Third, Any, All }

public sealed record EventRelicRarityOpportunity(EventRelicRarityBranch Branch, ModelKey EventKey, int RollCount);

public static class EventOpportunityCatalog
{
    public const string DollRoom = "DOLL_ROOM";
    public const string MuseumOfTorture = "MUSEUM_OF_TORTURE";
    public const string ThermaeNonupeipe = "THERMAE_NONUPEIPE";
    public const string CrystalSphere = "CRYSTAL_SPHERE";

    public static IReadOnlyList<ModelKey> Dolls { get; } =
        Relics("DAUGHTER_OF_THE_WIND", "MR_STRUGGLES", "BING_BONG");

    public static IReadOnlyList<ModelKey> MuseumRelics { get; } =
        Relics("OBSIDIAN_VICE", "THUMBSCREW", "TORMENTOUS_COLLAR");

    // Nonupeipe.AllPossibleOptions in source order, filtered by the bath's predicate.
    public static IReadOnlyList<ModelKey> ThermaeRelics { get; } =
        Relics("BLESSED_ANTLER", "BRILLIANT_SCARF", "DELICATE_FROND", "DIAMOND_DIADEM", "FUR_COAT",
            "GLITTER", "JEWELRY_BOX", "SIGNET_RING", "BEAUTIFUL_BRACELET");

    public static IReadOnlyList<EventRelicRarityOpportunity> RarityBranches { get; } =
        Array.AsReadOnly<EventRelicRarityOpportunity>(
        [
            Rarity(EventRelicRarityBranch.Setting3, "BATTLEWORN_DUMMY"),
            Rarity(EventRelicRarityBranch.OfferTribute, "LUMINOUS_CHOIR"),
            Rarity(EventRelicRarityBranch.GivePotion, "RANWID_THE_ELDER"),
            Rarity(EventRelicRarityBranch.GiveGold, "RANWID_THE_ELDER"),
            Rarity(EventRelicRarityBranch.GiveRelic, "RANWID_THE_ELDER", 2),
            Rarity(EventRelicRarityBranch.TradeOffers, "RELIC_TRADER", 3),
            Rarity(EventRelicRarityBranch.ContinueFight, "ROUND_TEA_PARTY"),
            Rarity(EventRelicRarityBranch.Ornate, "THIS_OR_THAT"),
            Rarity(EventRelicRarityBranch.Kill, "UNREST_SITE"),
            Rarity(EventRelicRarityBranch.MerchantGuilty, "TRIAL", 2),
            Rarity(EventRelicRarityBranch.BeatUp, "WATERFALL_OF_OBVIOUS_SECRETS")
        ]);

    private static EventRelicRarityOpportunity Rarity(EventRelicRarityBranch branch, string entry, int count = 1) =>
        new(branch, new ModelKey(BaseGameModelKeys.Categories.Event, entry), count);

    private static IReadOnlyList<ModelKey> Relics(params string[] entries) =>
        Array.AsReadOnly(entries.Select(entry => new ModelKey(BaseGameModelKeys.Categories.Relic, entry)).ToArray());
}
