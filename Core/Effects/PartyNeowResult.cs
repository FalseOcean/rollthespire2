using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects;

public enum PartyNeowIntent { ResultTarget, PremiseOnly }
// An omitted choice is searched jointly. -1 means explicit Skip; other values are stable option indices.
public sealed record PartyNeowChoice(string GroupId, int Index);
public sealed record PartyNeowPlan(ModelKey Option, PartyNeowIntent Intent, IReadOnlyList<PartyNeowChoice> Choices);
public sealed record PartyNeowParticipant(int Slot, PartyNeowPlan Plan);
public sealed record PartyNeowResult(int Slot, ModelKey Option, PartyNeowIntent Intent,
    IReadOnlyList<PartyNeowChoice> Choices, IReadOnlyList<PredictedEffectGroup> Offers,
    IReadOnlyList<PredictedEffectGroup> Results)
{
    public string OpeningRouteId { get; init; } = "";
}

public static class PartyNeowAdmission
{
    public const string ObservationVersion = "Party.NeowSelectedOptionResult.FixedParticipantOrder.v1";
    public static readonly IReadOnlyList<ModelKey> Options = Array.AsReadOnly(new[] {
        BaseGameModelKeys.Relics.ArcaneScroll, BaseGameModelKeys.Relics.HeftyTablet,
        BaseGameModelKeys.Relics.LeadPaperweight, BaseGameModelKeys.Relics.LostCoffer,
        BaseGameModelKeys.Relics.ScrollBoxes, BaseGameModelKeys.Relics.Kaleidoscope,
        BaseGameModelKeys.Relics.PhialHolster, BaseGameModelKeys.Relics.CursedPearl,
        BaseGameModelKeys.Relics.GoldenPearl, BaseGameModelKeys.Relics.DowsingRod,
        BaseGameModelKeys.Relics.NeowsTorment, BaseGameModelKeys.Relics.NutritiousOyster,
        BaseGameModelKeys.Relics.NeowsSacrifice });
    public static string? UnsupportedReason(ModelKey option) => Options.Contains(option) ? null : option.Entry switch
    {
        "LEAFY_POULTICE" or "NEW_LEAF" => "Party.N.DeferredToT",
        "NEOWS_BONES" or "SMALL_CAPSULE" or "LARGE_CAPSULE" => "Party.N.NestedTransactionAndBagPropagationUnclosed",
        "MASSIVE_SCROLL" => "Party.N.MultiplayerOnlyCombinedPoolNotCaptured",
        _ => "Party.N.ConcreteDeckChoiceOrLaterParticipantHooksUnclosed"
    };
    public static IReadOnlyList<(string Id, int Count, bool Skip)> ChoiceDomains(ModelKey option) => option.Entry switch
    {
        "HEFTY_TABLET" => [("hefty-tablet-offer", 3, true)],
        "LEAD_PAPERWEIGHT" => [("lead-paperweight-offer", 2, true)],
        "LOST_COFFER" => [("lost-coffer-cards", 3, true), ("lost-coffer-potion", 1, true)],
        "KALEIDOSCOPE" => [("kaleidoscope-group-1", 3, true), ("kaleidoscope-group-2", 3, true)],
        "SCROLL_BOXES" => [("scroll-boxes.bundle-choice", 2, false)],
        _ => []
    };
}
