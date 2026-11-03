using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.Ui1;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private SeedAncientPremiseVariants? _ancientPremiseVariants;

    private void PrecomputeAncientPremises(SeedPredictionRequest request, SeedPredictionFacts facts)
    {
        _ancientPremiseVariants = SeedAncientPremiseVariants.Prepare(request, facts);
        foreach (var failure in _ancientPremiseVariants.Failures)
            RuntimeLog.WarnException($"predictorAncientPremisesUnavailable={failure.Ancient.Serialized};act={failure.Act}", failure.Error);
    }

    private void ApplyAncientPremises(AncientOptionConditionProfile conditions)
    {
        if (!_wbHasResult || LastRequest is not { } originalRequest || LastFacts is null ||
            _ancientPremiseVariants is not { } variants) return;
        if (!variants.CanRevise(conditions))
        {
            _ancientGroupCard.SetConditions(originalRequest.AncientOptionConditions, notify: false);
            ReformatDocumentOnly();
            return;
        }
        var revised = variants.Revise(conditions);
        LastRequest = revised.Request;
        LastFacts = revised.Facts;
        LastDocument = SeedPredictionDocument.FromFacts(revised.Facts);
        // Maps, drawn routes and opening selection retain the same independent
        // observations; changing Ancient premises never rebinds route state.
        _viewModel = SeedAnalysisPresentationBuilder.Build(LastDocument, _uiText!, _contentNames!, false);
        if (_ancientSurface is { } surface && Godot.GodotObject.IsInstanceValid(surface) && surface.IsInsideTree() &&
            _viewModel.AncientDomain.Items.FirstOrDefault(a => a.Act == _wbPage + 1) is { } visibleAncient)
            RenderAncientInspector(visibleAncient, surface);
        PredictorContextChanged?.Invoke(CurrentReactiveDraft, _preferredOpeningRouteId,
            _preferredOpeningChoiceSlotIndex, _preferredRewardRouteGroupId);
    }
}
