using System.Collections.Immutable;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

internal static class HextechRuneRerollOperationPreview
{
    internal const string FeatureId = "hextech:selection-reroll";

    internal static OperationPreview Build(HextechRuneRerollSnapshot snapshot, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = HextechRuneRerollPrediction.Predict(snapshot);
        var sequence = HextechRuneRerollSequence.Predict(snapshot, token);
        token.ThrowIfCancellationRequested();
        var notices = ImmutableArray.CreateBuilder<string>();
        if (result.Outcome == HextechRuneRerollOutcome.Unavailable)
            notices.Add("UiText:operation.hextechRune.unavailable");
        if (sequence.Unknown || result.Outcome == HextechRuneRerollOutcome.Unknown)
            notices.Add(result.Issue == "HextechRuneRerollChaosUnknown"
                ? "UiText:operation.hextechRune.chaosUnknown" : "UiText:operation.hextechRune.sequenceUnknown");
        if (sequence.Truncated) notices.Add("UiText:operation.hextechRune.sequenceTruncated");
        if (snapshot.FullState?.NoiseSlot != null && sequence.Groups.Any(group => group.Depth > 1))
            notices.Add("UiText:operation.hextechRune.noiseStep");
        var sections = ImmutableArray.CreateBuilder<OperationPreviewSection>();
        foreach (var depth in sequence.Groups.GroupBy(group => group.Depth).OrderBy(group => group.Key))
        {
            var rows = ImmutableArray.CreateBuilder<OperationPreviewRow>();
            foreach (var group in depth)
            {
                token.ThrowIfCancellationRequested();
                var outcome = group.Result;
                if (outcome.Outcome is HextechRuneRerollOutcome.Unknown or HextechRuneRerollOutcome.Unavailable ||
                    outcome.NextPanel.IsDefaultOrEmpty || outcome.Slot < 0 || outcome.Slot >= outcome.NextPanel.Length) continue;
                rows.Add(new(OperationPreviewAction.Result, [outcome.NextPanel[outcome.Slot]], [], false)
                { Label = RouteLabel(group.Orders[0], outcome.Outcome) });
            }
            if (rows.Count == 0) continue;
            sections.Add(new(snapshot.Source, rows.ToImmutable(), [])
            {
                FeatureId = FeatureId, Category = OperationPredictionCategory.RelicEffect,
                Label = "UiText:operation.hextechRune.depth" + depth.Key
            });
        }
        if (sections.Count > 0) notices.Insert(0, "UiText:operation.hextechRune.routeMeaning");
        return new(snapshot.Source, [], [], false,
            sections.Count == 0 ? [] : [new([], sections.ToImmutable(), [],
                sequence.Groups.Sum(group => group.Orders.Length), !sequence.Truncated && !sequence.Unknown)], [])
        { Style = OperationDisplayPolicy.A1, Notices = notices.ToImmutable() };
    }

    private static string RouteLabel(ImmutableArray<int> order, HextechRuneRerollOutcome outcome)
    {
        // One complete witnessed route is sufficient for the display. All
        // alternatives remain in the sequence model for independent continuation.
        string Slot(int slot) => slot is >= 0 and < 10
            ? char.ConvertFromUtf32(0x2460 + slot) : "(" + (slot + 1) + ")";
        string zh = string.Join("→", order.Select(Slot)), en = zh;
        if (outcome == HextechRuneRerollOutcome.NoiseRestored)
        { zh += "（恢复原选项）"; en += " (restore original option)"; }
        else if (outcome == HextechRuneRerollOutcome.Unchanged)
        { zh += "（不变）"; en += " (unchanged)"; }
        // Existing overlay localization selects this authored bilingual label
        // on the main thread; detached calculation never reads live language.
        return zh == en ? zh : zh + " / " + en;
    }
}
