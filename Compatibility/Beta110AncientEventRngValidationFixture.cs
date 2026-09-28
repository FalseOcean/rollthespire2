using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Compatibility;

/// <summary>
/// Validation-only, immutable Ancient event RNG fixture. It never invokes Obtain,
/// AfterObtained or any live RunState mutation. It exists to make the Beta110
/// SharedAncients-vs-EventModel.IsShared distinction directly auditable.
/// </summary>
internal static class Beta110AncientEventRngValidationFixture
{
    internal sealed record Row(
        int Act,
        string AncientId,
        bool CandidateFromSharedAncientAllocation,
        int PlayerSlot,
        bool EventModelIsShared,
        bool EventModelIsSharedAuthorityExact,
        ulong EventRngRoot,
        bool EventRngRootExact,
        string EventModelIsSharedEvidence,
        int OptionRngCallCount,
        IReadOnlyList<WorldRngTraceEntry> OptionRngTrace,
        string PredictionEvidence,
        string PredictionIssue);

    internal static IReadOnlyList<Row> Capture(
        Beta109WorldGenerationSnapshot snapshot,
        ModelKey characterKey,
        bool runtimeAuthorityExact)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rows = new List<Row>();
        foreach (Beta109AncientEventContextSnapshot context in snapshot.AncientEventContexts
                     .OrderBy(item => item.Act)
                     .ThenBy(item => item.AncientKey.Serialized, StringComparer.Ordinal)
                     .ThenBy(item => item.PlayerSlot))
        {
            Beta109AncientOptionPrediction prediction = Beta109AncientOptionProvider.Predict(
                snapshot,
                context.Act,
                context.AncientKey,
                context.PlayerSlot,
                characterKey,
                runtimeAuthorityExact);
            rows.Add(new Row(
                context.Act,
                context.AncientKey.Serialized,
                context.CandidateFromSharedAncientAllocation,
                context.PlayerSlot,
                context.IsShared,
                context.EventIsSharedAuthorityExact,
                context.EventRngRoot,
                context.EventRngRootExact,
                context.EventIsSharedAuthorityEvidence,
                prediction.RngCallCount,
                prediction.Trace.ToArray(),
                prediction.EvidenceCode.Value,
                prediction.IssueCode));
        }
        return rows;
    }
}
