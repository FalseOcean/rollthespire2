using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Workspace;

// Persisted authored declarations, including inactive and incomplete choices.
// Property names, enum values and positional indices retain the existing save contract;
// controls map these snapshots to their own mutable drafts.
internal sealed record NeowEditorStateSnapshot(ModelKey? Source, IReadOnlyList<ModelKey> Bones, bool Ordered,
    KaleidoscopeGroupOrderMode KaleidoscopeOrder, NeowSpecialOfferKind ScrollOffer,
    bool JointTransforms, bool JointCapsules, IReadOnlyList<ModelKey?> TransformTargets,
    IReadOnlyList<ModelKey?> CapsuleTargets, ModelKey? Curse, IReadOnlyDictionary<string, ModelKey?> Slots,
    bool TransformSeparateInitialized, bool TransformJointInitialized,
    bool CapsuleSeparateInitialized, bool CapsuleJointInitialized);

internal sealed record EventEditorStateSnapshot(
    Dictionary<EventResultConditionKind, ModelKey?[]> Results,
    int CharacterColor,
    int TrialCase,
    int TinkerType,
    string? TinkerEffect,
    IReadOnlyList<string> InactiveResultEvents);

internal sealed record TransformationSourceSnapshot(string Id, ModelKey Identity, int ResultCount,
    ModelKey? OtherRelic = null);

internal sealed record TransformationEditorStateSnapshot(IReadOnlyList<string> TakenOver, int Objective, int RareCount,
    IReadOnlyList<ModelKey> Cards, IReadOnlyList<TransformationSourceSnapshot>? KnownSources = null);
