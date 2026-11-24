using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// One forecast supplies both the result evidence and the developer view. Fact
// identities refer to its immutable snapshot, never to a live game object.
internal sealed record PredictorNodeDiagnostics(PredictorKnowledgeSnapshot Snapshot,
    ImmutableArray<PredictorKnowledgeFactId> Before,
    ImmutableArray<PredictorKnowledgeFactId> After,
    ImmutableArray<PredictorKnowledgeFactId> Results);
