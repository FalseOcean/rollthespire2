using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Detached semantic outcomes, independent of controls, preferences and display layout.
internal enum OperationOutcomeAction { ObtainRelic, AddCard, UpgradeCard, TransformCard, GeneratePotion, EnchantCard, RemoveCard, DowngradeCard, Restock, Choose }
internal enum OperationForecastActivity { Obtain, Event, ShopPurchase, RestAction, Reroll, Sacrifice, ProposedTargets, PotionUse }
internal enum OperationOutcomeCategory { CardTransform, ShopRestock, RewardReroll, RewardSacrifice }
internal sealed record OperationOutcome(OperationOutcomeAction Action, ImmutableArray<ModelKey> Models,
    ImmutableArray<PredictorCard> Cards, bool Possible)
{
    internal string? Kind { get; init; }
    internal int? Price { get; init; }
}
internal sealed record OperationOutcomeChoices(long Id, ImmutableArray<PredictorCard> Cards,
    int MinimumSelect, int MaximumSelect, bool CanSkip, bool Possible)
{
    internal string? Kind { get; init; }
    internal bool OrderedAlternatives { get; init; }
    internal bool ProposedTarget { get; init; }
    internal int? SequenceOrdinal { get; init; }
    internal ImmutableArray<OperationIconPath> IconPaths { get; init; } = [];
}
// Segments are ordered. Multiple icons inside one segment can be exchanged in
// every distinct order at this exact prefix/suffix; the target remains last.
internal sealed record OperationIconPath(ImmutableArray<ImmutableArray<ModelKey>> Segments);
internal sealed record OperationOutcomeSource(ModelKey Source, ImmutableArray<OperationOutcome> Rows,
    ImmutableArray<OperationOutcomeChoices> CardGroups)
{
    internal OperationOutcomeCategory? Category { get; init; }
    internal string? FeatureId { get; init; }
    internal string? EmptyReason { get; init; }
    internal string? Kind { get; init; }
}
// Exact, already evaluated Hunt-count settlements. Presentation may share
// identical prefixes; the independent paths remain the result authority.
internal sealed record OperationForecastHuntOutcome(int Triggers, ImmutableArray<OperationOutcomeSource> Sections);
internal sealed record OperationForecastBranch(ImmutableArray<ModelKey> Order, ImmutableArray<OperationOutcomeSource> Sections,
    ImmutableArray<string> PendingReasons, int PathsExamined, bool FullyEnumerated)
{
    internal bool Faulted { get; init; }
    internal string? Kind { get; init; }
    internal ImmutableArray<OperationForecastHuntOutcome> HuntOutcomes { get; init; } = [];
}
internal sealed record OperationForecastFault(string Stage, ImmutableArray<PredictorInput> InputPath, Exception Exception);
// Retain each evaluated explicit path and its immediate execution receipts. These
// include random-upgrade observations made before later removals/transforms;
// consumers must not reconstruct them from the terminal state.
internal sealed record OperationForecastEvaluation(ImmutableArray<PredictorInput> InputPath,
    PredictorInputResult Disposition, PredictorPhase Phase, ImmutableArray<PredictorAcceptedInput> Effects,
    ImmutableArray<PredictorOperationRandomUpgrade> RandomUpgrades, ImmutableArray<PredictorReward> Rewards)
{
    internal string Fingerprint { get; init; } = "";
    internal string? AdditionalPremise { get; init; }
}
internal sealed record OperationForecastCombatEvaluation(ImmutableArray<PredictorInput> InputPath, PredictorCombatPreview Rewards);
internal sealed record OperationForecast(ModelKey Source, ImmutableArray<ModelKey> ContainedRelics,
    ImmutableArray<PredictorCard> CurseCandidates, bool CursesPossible,
    ImmutableArray<OperationForecastBranch> Branches, ImmutableArray<string> PendingReasons)
{
    internal string Fingerprint { get; init; } = "";
    internal string ModelPremise => "CapturedPresentState;VanillaRules;ThirdPartyAdditionalEffectsOmitted";
    internal OperationForecastActivity Activity { get; init; }
    internal PredictorShopEntryKind? PurchasedEntryKind { get; init; }
    internal PredictorRestAction? RestAction { get; init; }
    internal int? ProposedTargetOrdinal { get; init; }
    internal bool TargetTransformation { get; init; }
    internal ImmutableArray<OperationForecastFault> Faults { get; init; } = [];
    internal ImmutableArray<OperationForecastEvaluation> Evaluations { get; init; } = [];
    internal ImmutableArray<OperationForecastCombatEvaluation> CombatEvaluations { get; init; } = [];
    internal string? OptionKey { get; init; }
    internal ImmutableArray<string> Notices { get; init; } = [];
}
internal sealed class OperationForecastExecution(PredictorOperationSnapshot snapshot)
{
    private readonly List<OperationForecastEvaluation> _evaluations = [];
    internal ImmutableArray<OperationForecastEvaluation> Evaluations => _evaluations.ToImmutableArray();
    internal PredictorOperationResult Evaluate(ImmutableArray<PredictorInput> inputs, CancellationToken token)
        => Evaluate(snapshot, inputs, token);
    internal PredictorOperationResult Evaluate(PredictorOperationSnapshot source, ImmutableArray<PredictorInput> inputs,
        CancellationToken token, string? additionalPremise = null)
    {
        var result = PredictorOperation.Preview(source, inputs, token);
        _evaluations.Add(new(inputs, result.Disposition, result.Waiting.Phase, result.Effects,
            result.RandomUpgrades, result.Rewards) { Fingerprint=source.Fingerprint, AdditionalPremise=additionalPremise });
        return result;
    }
}
