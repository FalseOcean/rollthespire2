using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Events;

/// <summary>
/// Beta111 source-declaration-order reconstruction data for the bounded Event Result v1
/// observables. These lists are versioned source truth; they must never be rebuilt from
/// runtime Dictionary/reflection/UI ordering.
/// </summary>
public static class Beta111EventResultCatalog
{
    public const string TrashHeapEventEntry = "TRASH_HEAP";
    public const string FakeMerchantEventEntry = "FAKE_MERCHANT";
    public const string ColorfulPhilosophersEventEntry = "COLORFUL_PHILOSOPHERS";

    public static IReadOnlyList<ModelKey> TrashHeapGrabCards { get; } = new[]
    {
        Card("CALTROPS"),
        Card("CLASH"),
        Card("DISTRACTION"),
        Card("DUAL_WIELD"),
        Card("ENTRENCH"),
        Card("HELLO_WORLD"),
        Card("OUTMANEUVER"),
        Card("REBOUND"),
        Card("RIP_AND_TEAR"),
        Card("STACK")
    };

    public static IReadOnlyList<ModelKey> TrashHeapDiveRelics { get; } = new[]
    {
        Relic("DARKSTONE_PERIAPT"),
        Relic("DREAM_CATCHER"),
        Relic("HAND_DRILL"),
        Relic("MAW_BANK"),
        Relic("THE_BOOT")
    };

    public static IReadOnlyList<ModelKey> FakeMerchantRelics { get; } = new[]
    {
        Relic("FAKE_ANCHOR"),
        Relic("FAKE_BLOOD_VIAL"),
        Relic("FAKE_HAPPY_FLOWER"),
        Relic("FAKE_LEES_WAFFLE"),
        Relic("FAKE_MANGO"),
        Relic("FAKE_ORICHALCUM"),
        Relic("FAKE_SNECKO_EYE"),
        Relic("FAKE_STRIKE_DUMMY"),
        Relic("FAKE_VENERABLE_TEA_SET")
    };

    // Event source declaration order is card-pool/character identity order.
    public static IReadOnlyList<ModelKey> ColorfulCharacterOrder { get; } = new[]
    {
        BaseGameModelKeys.Characters.Necrobinder,
        BaseGameModelKeys.Characters.Ironclad,
        BaseGameModelKeys.Characters.Regent,
        BaseGameModelKeys.Characters.Silent,
        BaseGameModelKeys.Characters.Defect
    };

    private static ModelKey Card(string entry) => new(BaseGameModelKeys.Categories.Card, entry);
    private static ModelKey Relic(string entry) => new(BaseGameModelKeys.Categories.Relic, entry);
}
