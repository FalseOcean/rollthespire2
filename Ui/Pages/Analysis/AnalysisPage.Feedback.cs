using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Diagnostics;
using System.Text.Json;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private string? _lastLoggedFeedback;

    internal void RecordViewedAnalysis()
    {
        try
        {
            if (CaptureFeedbackEvidence() is not { } view) return;
            string json = JsonSerializer.Serialize(view);
            if (json == _lastLoggedFeedback) return;
            RuntimeLog.Info("analysisViewObserved=true;data=" + json);
            _lastLoggedFeedback = json;
        }
        catch (Exception ex)
        {
            RuntimeIssueLedger.Current.Record(ex, "FeedbackMetadataCaptureFailed", "AnalysisPage",
                "RecordViewedAnalysis", "runtime.issue.feedback_metadata_failed", RuntimeIssueSeverity.Warning);
        }
    }

    // Read the already projected view. Exporting must never choose a route or rerun a prediction.
    internal object? CaptureFeedbackEvidence()
    {
        if (LastFacts is not { } facts) return null;
        return new
        {
            Source = "SeedAnalysis.LastFacts",
            RequestId = facts.Context.RequestId.Serialized,
            Seed = facts.CanonicalSeed, GameVersion = facts.DetectedGameVersion,
            Rules = LastRequest?.Authority.VersionResolution.SemanticProfileId.ToString(),
            Character = facts.Context.CharacterKey.Serialized, facts.Context.Ascension, facts.Context.PlayersCount,
            facts.Context.PlayerSlotIndex, facts.Context.UnlockSnapshotFingerprint, facts.Context.CatalogFingerprint,
            InputStatus = facts.InputStatus.ToString(), facts.InputIssue, facts.ModelAssumptions,
            ViewSelection = new { Opening = _selectedNeowRelicKey.IsValid ? _selectedNeowRelicKey.Serialized : null,
                PreferredOpeningChoiceSlotIndex, PreferredOpeningRouteId, PreferredRewardRouteGroupId,
                AncientPremises = LastRequest?.AncientOptionConditions },
            Neow = facts.Neow is { } neow ? new
            {
                Status = neow.Status.ToString(), neow.IssueCode,
                Choices = neow.Choices.Select(c => new
                {
                    c.SlotIndex, Key = c.RelicKey.Serialized, IdentityPrecision = c.IdentityPrecision.ToString(),
                    EffectPrecision = c.EffectPrecision.ToString(), Evidence = c.EffectEvidenceCode.ToString(),
                    Kaleidoscope = c.RelicKey == BaseGameModelKeys.Relics.Kaleidoscope ? CardOffers(c.EffectGroups) : null,
                    Bones = c.BonesOutcome is { } bones ? new
                    {
                        AvailableRouteCount = bones.OriginalRoutes.Count,
                        Routes = bones.OriginalRoutes.OrderByDescending(r => r.RouteId == PreferredOpeningRouteId ||
                                r.OpeningRewardContinuation?.Route.RouteId == PreferredOpeningRouteId)
                            .DistinctBy(r => string.Join("|", r.AcquisitionOrder.Select(k => k.Serialized)))
                            .Take(2).Select(r => new { r.RouteId, Order = r.AcquisitionOrder.Select(k => k.Serialized),
                                Curse = r.SharedContinuation.FinalCurseKey?.Serialized,
                                Kaleidoscope = r.RelicScopedResults.Where(v => v.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope)
                                    .Select(v => CardOffers(v.EffectGroups)) })
                    } : null
                })
            } : null,
            Ancients = facts.World?.Ancients.Select(a => new { a.Act, Key = a.AncientKey.Serialized,
                IdentityPrecision = a.IdentityPrecision.ToString(), OptionPrecision = a.OptionPrecision.ToString(),
                Status = a.OptionsEvaluationStatus.ToString(), a.OptionIssueCode, a.OptionReplayAssumption,
                Options = a.Options.Select(o => o.OptionKey.Serialized) }),
            WorldIssue = facts.World?.AncientIssueCode
        };
    }

    private static object[] CardOffers(IReadOnlyList<PredictedEffectGroup> groups) =>
        groups.Select(g => new { g.GroupId, g.GroupOrder,
            Cards = g.OrderedItems.Where(i => i.Kind == PredictedEffectKind.AddCard)
                .Select(i => new { i.ItemOrder, Key = i.TargetKey?.Serialized, i.HasGlam }).ToArray() })
            .Where(g => g.Cards.Length > 0).ToArray();
}
