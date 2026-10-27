using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorOperationOpaqueAction(string Id, string SourceType, string SourceAssembly);

// Explicit local-operation premise. Never inferred from a route or Search request.
internal sealed record PredictorOperationContext(ulong PlayerId, int PlayerSlot, int PlayerCount,
    ImmutableArray<ModelKey> OpaqueModels)
{
    internal PredictorOperationEventRng? CombatCardRng { get; init; }
    internal PredictorOperationPotionPools? CombatCardPools { get; init; }
    internal ImmutableArray<PredictorVisitFact> PreviousVisits { get; init; } = [];
    internal bool IgnoresBehavior(ModelKey key) => OpaqueModels.Contains(key);
}

// Scenes contain detached, already-generated values. Restore must not replay
// room entry, payment, reward population or the beginning of a suspended obtain.
internal abstract record PredictorOperationScene
{
    internal abstract void Restore(PredictorRun run);
}

internal sealed record PredictorOperationSnapshot(string Fingerprint, PredictorContext Context,
    PredictorState State, PredictorOperationScene Scene, int CurrentHp, int MaximumHp)
{
    internal ImmutableArray<long> SelectedCardIds { get; init; } = [];
}

// Capture the automatic upgrade at its actual point in the obtain chain. Later
// transforms/removals may replace these instances, so final-state diff is not
// sufficient evidence for displaying what the earlier relic affected.
internal sealed record PredictorOperationRandomUpgrade(long SourceRelicId, ModelKey Source,
    ImmutableArray<PredictorCard> Before, ImmutableArray<PredictorCard> After);

internal sealed record PredictorOperationSelection(PredictorPhase Phase, PredictorSelection Selection,
    ImmutableArray<PredictorEventOption> EventOptions, ImmutableArray<PredictorCard> Cards,
    ImmutableArray<ImmutableArray<PredictorCard>> Bundles)
{
    internal bool Cancelable { get; init; }
    internal bool TrialAbandonConfirmationPending { get; init; }
    internal ImmutableArray<PredictorAncientOffer> AncientOffers { get; init; } = [];
    internal ImmutableArray<PredictorRestOffer> RestOffers { get; init; } = [];
    internal ImmutableArray<long> SelectedCardIds { get; init; } = [];
    internal bool MendTargetPending { get; init; }
    internal ImmutableArray<PredictorOperationMendTarget> MendTargets { get; init; } = [];
    internal ImmutableArray<PredictorOperationOpaqueAction> OpaqueRestOptions { get; init; } = [];
}

internal sealed record PredictorOperationResult(string Fingerprint, PredictorInputResult Disposition,
    PredictorState State, int CurrentHp, int MaximumHp, PredictorOperationSelection Waiting,
    ImmutableArray<PredictorReward> Rewards, ImmutableArray<PredictorReward> CompletedChildRewards,
    ImmutableArray<PredictorAcceptedInput> Effects, ImmutableArray<PredictorShopOffer> ShopOffers)
{
    internal ImmutableArray<PredictorInput> Inputs { get; init; } = [];
    internal string ModelPremise => "CapturedPresentState;VanillaRules;ThirdPartyAdditionalEffectsOmitted";
    internal PredictorOperationCrystalScene? CrystalBoard { get; init; }
    internal PredictorOperationTreasurePartyScene? TreasureParty { get; init; }
    internal bool HealthKnown { get; init; } = true;
    internal ImmutableArray<PredictorOperationLinkedRewardGroup> LinkedRewardGroups { get; init; } = [];
    internal PredictorOperationMendOutcome? MendOutcome { get; init; }
    internal ImmutableArray<PredictorOperationRandomUpgrade> RandomUpgrades { get; init; } = [];
}
