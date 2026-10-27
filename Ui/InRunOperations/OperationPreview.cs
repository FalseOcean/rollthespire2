using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunOperations;

internal enum OperationTextMode { GroupRows, CardRows, Complex, None }
internal enum OperationCardLayout { Parallel, Stack, Groups, Branches, Spread }
internal sealed record OperationDisplayStyle(OperationTextMode Text, OperationCardLayout Cards);

internal enum OperationPreviewAction { ObtainRelic, AddCard, UpgradeCard, TransformCard, GeneratePotion, EnchantCard, RemoveCard, DowngradeCard, Restock, Choose }
internal sealed record OperationPreviewRow(OperationPreviewAction Action, ImmutableArray<ModelKey> Models,
    ImmutableArray<PredictorCard> Cards, bool Possible)
{
    internal string? Label { get; init; }
    internal int? Price { get; init; }
    internal bool ShowCards { get; init; } = true;
}
internal sealed record OperationPreviewCardGroup(long Id, ImmutableArray<PredictorCard> Cards,
    int MinimumSelect, int MaximumSelect, bool CanSkip, bool Possible)
{
    internal string? Label { get; init; }
    internal bool Highlighted { get; init; }
    internal bool EarliestFirst { get; init; }
    internal bool ForceSingleRow { get; init; }
    internal bool SuppressPossiblePrefix { get; init; }
}
internal sealed record OperationPreviewSection(ModelKey Source, ImmutableArray<OperationPreviewRow> Rows,
    ImmutableArray<OperationPreviewCardGroup> CardGroups)
{
    // Explicit ownership for linked effects (e.g. Courier on a sacrifice button).
    internal OperationPredictionCategory? Category { get; init; }
    internal string? FeatureId { get; init; }
    internal string? Label { get; init; }
    internal string? EmptyReason { get; init; }
    internal OperationDisplayStyle? Style { get; init; }
}
internal sealed record OperationPreviewBranch(ImmutableArray<ModelKey> Order, ImmutableArray<OperationPreviewSection> Sections,
    ImmutableArray<string> PendingReasons, int PathsExamined, bool FullyEnumerated)
{
    internal bool Faulted { get; init; }
    internal string? Label { get; init; }
}
internal sealed record OperationPreviewFault(string Stage, ImmutableArray<PredictorInput> InputPath, Exception Exception);
// Bones lifts a curse here only when every order has the same certain visible
// result. Order-dependent or conditional curses stay in their branch rows.
internal sealed record OperationPreview(ModelKey Source, ImmutableArray<ModelKey> ContainedRelics,
    ImmutableArray<PredictorCard> CurseCandidates, bool CursesPossible,
    ImmutableArray<OperationPreviewBranch> Branches, ImmutableArray<string> PendingReasons)
{
    internal ImmutableArray<OperationPreviewFault> Faults { get; init; } = [];
    internal OperationDisplayStyle? Style { get; init; }
    internal string? OptionKey { get; init; }
    internal string? Heading { get; init; }
    internal ImmutableArray<string> Notices { get; init; } = [];
    // Map presenters already supply localized sentences. They are display
    // text, including English colons, rather than operation pending codes.
    internal bool NoticesAreLocalized { get; init; }
}
