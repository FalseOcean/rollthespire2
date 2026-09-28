using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction;

public enum PredictionSectionKind
{
    NeowIdentity,
    BossIdentity,
    AncientIdentityAndOptions,
    EventPoolSequences,
    RelicSequences,
    NormalCombatRewardSequence,
    EncounterSequences
}

public enum PredictionDomain
{
    NeowOpening,
    Boss,
    Ancient,
    EventPoolSequence,
    RelicSequence,
    NormalCombatRewardSequence,
    EncounterSequence
}

public enum PredictionScope
{
    NeowChoiceIdentity,
    NeowChoiceIdentityAndMigratedImmediateEffects,
    BossIdentity,
    AncientIdentityAndSeedDeterminedOptions,
    InitialEventCandidateQueues,
    InitialRelicGrabBagSequences,
    OpeningNormalCombatRewardSequence,
    InitialEncounterQueues
}

public enum PredictionSourceState
{
    SeedAndAuthoritySnapshot
}

public sealed record PredictionSection(
    PredictionSectionKind Kind,
    PredictionDomain Domain,
    PredictionScope Scope,
    PredictionSourceState SourceState,
    IReadOnlyList<NeowChoiceResult> NeowChoices)
{
    public IReadOnlyList<BossPredictionResult> Bosses { get; init; } = Array.Empty<BossPredictionResult>();
    public IReadOnlyList<AncientPredictionResult> Ancients { get; init; } = Array.Empty<AncientPredictionResult>();
    public IReadOnlyList<ActEncounterSequenceResult> EncounterSequences { get; init; } = [];
    public EventPoolSequencePredictionResult? EventPoolSequencePrediction { get; init; }
    public RelicSequencePredictionResult? RelicSequencePrediction { get; init; }
    public NormalCombatRewardSequencePredictionResult? NormalCombatRewardSequencePrediction { get; init; }
    public SeedDomainEvaluationStatus DomainStatus { get; init; } = SeedDomainEvaluationStatus.Evaluated;
    public string IssueCode { get; init; } = string.Empty;
    public string AuthoritySnapshotFingerprint { get; init; } = string.Empty;
}
