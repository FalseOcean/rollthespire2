using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

internal enum NeowEffectCardTemplate
{
    None,
    Generic,
    ScrollBoxes,
    Bones
}

internal sealed record NeowEffectComponentDefinition(
    string ComponentId,
    string ShortDescription,
    NeowStructuredConditionKind ConditionKind,
    NeowStructuredEffectScope Scope,
    NeowStructuredOutputKind OutputKind,
    NeowCandidatePoolKind CandidatePool,
    int SlotCount,
    bool Unordered = false,
    bool AllowDuplicateOutputs = false,
    bool SupportsOrderSelection = false);

internal sealed record NeowEffectCardDefinition(
    ModelKey RouteRelicKey,
    NeowEffectCardTemplate Template,
    IReadOnlyList<NeowEffectComponentDefinition> Components,
    string AuditStatus,
    string EvidenceCode)
{
    public bool HasEffectCard => Template != NeowEffectCardTemplate.None;
}
