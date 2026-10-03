using Godot;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private UnlockState? _eventTransformUnlocks;
    private readonly Dictionary<string, IReadOnlyList<MorphicGroveCard>?> _eventTransformResults = [];

    private string ConditionalEventResultTooltipText(ModelKey eventKey)
    {
        if (LastRequest?.Authority.ProfileId != RuntimeProfileId.Beta111 ||
            !TryGetProjectionRoot(out ulong root)) return string.Empty;
        string entry = eventKey.Entry;
        int slot = LastRequest.PlayerSlotIndex;
        if (entry == "TINKER_TIME")
        {
            return string.Join("\n", Beta111TrialTinkerProjector.TinkerOptions(root, slot).Select(option =>
                _uiText!.Format("predictor.event.tooltip.tinker",
                    Text($"query.event.results.card_type_{option.CardType + 1}"),
                    string.Join(" / ", option.Riders.Select(rider =>
                        Text("query.event.results.rider." + TinkerRiderKey(rider)))))));
        }

        string caseText = string.Empty;
        if (entry == "TRIAL")
        {
            int trial = Beta111TrialTinkerProjector.TrialCase(root, slot);
            caseText = _uiText!.Format("predictor.event.tooltip.trial", Text($"query.event.results.case_{trial + 1}"));
            if (trial != 2) return caseText;
        }
        if (entry is not ("MORPHIC_GROVE" or "SYMBIOTE" or "AROMA_OF_CHAOS" or "WHISPERING_HOLLOW" or "TRIAL"))
            return string.Empty;

        // Hover and click use the same cached projection, even before opening details.
        var cards = GetEventTransformResults(root, entry);
        string transforms = cards is null
            ? Text("predictor.event.tooltip.unavailable")
            : _uiText!.Format("predictor.event.tooltip.transform",
                Text("predictor.event.tooltip.choice." + entry),
                string.Join(" · ", cards.Select(card => _contentNames?.Resolve(card.CardKey, GameContentKind.Card) ?? card.CardKey.Entry)));
        return string.IsNullOrEmpty(caseText) ? transforms : caseText + "\n" + transforms;
    }

    // These are option-conditioned observations of one event, not a route or
    // current-deck simulation. Rendering never constructs a Search Query/plan.
    private bool RenderConditionalEventResults(VBoxContainer detail, string entry)
    {
        if (LastRequest?.Authority.ProfileId != RuntimeProfileId.Beta111 ||
            !TryGetProjectionRoot(out ulong root)) return false;
        int slot = LastRequest.PlayerSlotIndex;
        if (entry == "TINKER_TIME")
        {
            detail.AddChild(WNote(Text("predictor.event.tinker_scope")));
            var alternatives = WResponsiveGrid(detail, "TinkerOptions", 240, 2);
            foreach (var option in Beta111TrialTinkerProjector.TinkerOptions(root, slot))
            {
                var branch = WCard(alternatives, Text($"query.event.results.card_type_{option.CardType + 1}"));
                branch.AddChild(WNote(Text("predictor.event.choose_rider")));
                foreach (int rider in option.Riders)
                    branch.AddChild(WLabel(Text("query.event.results.rider." + TinkerRiderKey(rider))));
            }
            return true;
        }
        if (entry == "TRIAL")
        {
            int trial = Beta111TrialTinkerProjector.TrialCase(root, slot);
            detail.AddChild(WNote(Text("predictor.event.trial_scope")));
            detail.AddChild(WLabel(Text($"query.event.results.case_{trial + 1}"), true));
            if (trial != 2) return true;
        }
        if (entry is not ("MORPHIC_GROVE" or "SYMBIOTE" or "AROMA_OF_CHAOS" or "WHISPERING_HOLLOW" or "TRIAL"))
            return false;

        detail.AddChild(WNote(Text("integration.event.premise." + entry)));
        var cards = GetEventTransformResults(root, entry);
        if (cards is null)
        {
            detail.AddChild(WNote(Text("predictor.event.transform_unavailable")));
            return true;
        }
        var grid = WResponsiveGrid(detail, "EventTransformResults", 240, 2);
        for (int i = 0; i < cards.Count; i++)
        {
            var column = WColumn(); grid.AddChild(column);
            column.AddChild(WNote(_uiText!.Format("predictor.event.transform_number", i + 1)));
            var key = cards[i].CardKey;
            var card = WObject(key, GameContentKind.Card, size: 64, iconVariant: IconVariant.CardPickerLarge);
            BindWorkbenchTooltip(card, key, GameContentKind.Card, _contentNames!.Resolve(key, GameContentKind.Card));
            column.AddChild(card);
        }
        detail.AddChild(WNote(Text("predictor.event.transform_scope")));
        return true;
    }

    private IReadOnlyList<MorphicGroveCard>? GetEventTransformResults(ulong root, string entry)
    {
        if (_eventTransformResults.TryGetValue(entry, out var cached)) return cached;
        IReadOnlyList<MorphicGroveCard>? result = null;
        if (_eventTransformUnlocks is not null && LastRequest is { } request)
        {
            try
            {
                // Main-thread, read-only pool capture; no live deck or RNG is touched.
                var scenario = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredBasics(
                    request.Authority, Beta111EventTransformProjector.DrawCount(entry), _eventTransformUnlocks);
                result = Beta111EventTransformProjector.Project(root, request.PlayerSlotIndex,
                    entry, scenario.Targets[0].OrderedSourceCandidates!);
            }
            catch (Exception ex)
            {
                // A mod character without the agreed Basic targets must not break
                // the rest of the predictor or acquire an invented replacement pool.
                RuntimeLog.Warn($"predictorEventTransformUnavailable={entry};slot={request.PlayerSlotIndex};issue={ex.GetType().Name}:{ex.Message}");
            }
        }
        _eventTransformResults[entry] = result;
        return result;
    }

    private static string TinkerRiderKey(int rider) => rider switch
    {
        0 => "sapping", 1 => "violence", 2 => "choking",
        3 => "energized", 4 => "wisdom", 5 => "chaos",
        6 => "expertise", 7 => "curious", 8 => "improvement",
        _ => throw new ArgumentOutOfRangeException(nameof(rider))
    };
}
