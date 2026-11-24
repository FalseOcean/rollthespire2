using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Runtime;

internal sealed record BonesCapsuleDiagnosticOutcome(bool IsMatch, SearchDisposition Disposition,
    string FailureCode, string EvaluationJson, bool ExcludedMatchedPickup);

// Only the explicit Bones verification arrangement creates this service. Its
// full-request Exact observation and known-divergence interpretation are not
// filter truth, prediction input or part of a numerical timing measurement.
internal sealed class BonesCapsuleDiagnosticVerification(SearchModel model)
{
    internal BonesCapsuleDiagnosticOutcome Verify(TrustedRootHashInput input)
    {
        var exact = ProductionExactSearchEvaluator.Evaluate(model, input);
        return new(exact.IsMatch, exact.Evaluation.Disposition, exact.FailureCode,
            System.Text.Json.JsonSerializer.Serialize(exact.Evaluation), HasExcludedMatchedPickup(exact, model));
    }

    internal static bool HasExcludedMatchedPickup(ProductionExactSearchResult exact, SearchModel model)
    {
        if (!exact.IsMatch || exact.Facts?.Neow is not { } neow) return false;
        bool Authored(ModelKey key) => model.Evaluation.CapsuleContainedRelics.All.Contains(key) ||
            model.Evaluation.CapsuleContainedRelics.Any.Contains(key) ||
            (key == BaseGameModelKeys.OrdinaryRelics.Whetstone ? model.Evaluation.RequireWhetstone : model.Evaluation.RequireWarPaint) ||
            model.Evaluation.StructuredNeowEffects.Any(c => !c.IsEmpty && NeowReplayPlan.IsCapsule(c) && c.OutputKeys.Contains(key));
        foreach (var choice in neow.Choices) {
            if (choice.BonesOutcome is null) continue;
            foreach (var route in choice.BonesOutcome.OriginalRoutes) {
                if (route.AcquisitionOrder.Count != 2 || route.AcquisitionOrder[0] != BaseGameModelKeys.Relics.LargeCapsule ||
                    route.AcquisitionOrder[1] != BaseGameModelKeys.Relics.Kaleidoscope) continue;
                string routeId = $"choice.{choice.SlotIndex}.{route.OpeningRewardContinuation?.Route.RouteId ?? route.RouteId}";
                if (!exact.Evaluation.MatchedRouteIds.Contains(routeId)) continue;
                if (route.RelicScopedResults.Where(result => result.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule)
                    .SelectMany(result => result.EffectGroups).SelectMany(group => group.OrderedItems)
                    .Any(item => item.EvidenceCode.Value == "beta111.neow-effect.war-paint.nested-automatic-effect" && !Authored(BaseGameModelKeys.OrdinaryRelics.WarPaint) ||
                        item.EvidenceCode.Value == "beta111.neow-effect.whetstone.nested-automatic-effect" && !Authored(BaseGameModelKeys.OrdinaryRelics.Whetstone))) return true;
            }
        }
        return false;
    }
}
