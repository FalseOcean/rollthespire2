using System.Collections.Immutable;
using System.Text.Json.Serialization;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Slot and stock revision bind an authored choice to that occurrence. Entry
// IDs belong to the detached execution and are never an offer's identity.
internal sealed record PredictorShopOfferSnapshot(int Slot, int StockRevision,
    PredictorShopEntryKind Kind, ModelKey? Key, PredictorCard? Card,
    int BaseCost, int Cost, bool IsOnSale, bool Stocked)
{
    // Missing/false identifies saved plans from before stock occurrence
    // revisions were separated from unchanged-card inventory refreshes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StockRevisionIsOccurrence { get; init; }
}

internal enum PredictorShopActionKind { Buy, BeginRemoval, ChooseRemoval, CancelRemoval, Obtain, Leave }
internal sealed record PredictorPlannedShopAction(PredictorShopActionKind Action)
{
    public PredictorShopOfferSnapshot? ExpectedOffer { get; init; }
    public PredictorPlannedCard? Card { get; init; }
    public PredictorPlannedObtainChild? Obtain { get; init; }
}
internal sealed record PredictorPlannedShop
{
    public ImmutableArray<PredictorPlannedShopAction> Actions { get; init; } = [];
}

// Gold has no authority over an offered item's identity or price. Its absent
// balance masks only affordability; false is reserved for a known shortfall.
internal sealed record PredictorShopOfferKnowledge(int Slot, PredictorShopEntryKind Kind,
    PredictorShopOfferSnapshot? Value, ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    internal bool IsKnown => Value != null && PendingCauses.IsEmpty;
    public bool? Affordable { get; init; }
    public ImmutableArray<PredictorCombatPendingCause> AffordabilityPendingCauses { get; init; } = [];
}

internal sealed record PredictorShopNodeKnowledge(ImmutableArray<PredictorShopOfferKnowledge> Offers,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    public bool Advanced { get; init; }
    public int AppliedActions { get; init; }
    // Successfully replayed authored prefix, with purchase/removal offers
    // bound to stable stock occurrences. Consumers may adopt these bindings
    // on an explicit edit; observing a legacy plan never rewrites saved intent.
    public ImmutableArray<PredictorPlannedShopAction> ResolvedActions { get; init; } = [];
    public bool AutomaticPurchasesActive { get; init; }
    public ImmutableArray<PredictorObtainChoiceKnowledge> RequiredChoices { get; init; } = [];
    public ImmutableArray<PredictorRouteCardKnowledge> RemovalCards { get; init; } = [];
    public int? GoldBalance { get; init; }
    public ImmutableArray<PredictorCombatPendingCause> GoldPendingCauses { get; init; } = [];
}
