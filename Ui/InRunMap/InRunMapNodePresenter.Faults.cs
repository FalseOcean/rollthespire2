using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private sealed record FaultChoiceBinding(string NodeId, long Revision, long Localization,
        PredictorRoutePredictionFault Fault, Func<bool> Apply);
    private readonly Dictionary<string, FaultChoiceBinding> _faultChoices = [];

    private InRunMapNodeDetailData? FaultDetail((int ActIndex, MapPosition Point) location)
    {
        if (_session.FaultLocation != location || _session.Forecast?.Fault is not { } fault) return null;
        long revision = _session.Revision, localization = _localizationRevision;
        bool Current() => revision == _session.Revision && localization == _localizationRevision &&
            ReferenceEquals(_session.Forecast?.Fault, fault);
        InRunMapDetailChoice Action(string label, Func<bool> apply)
        {
            string id = Id();
            _faultChoices[id] = new(fault.Position.NodeId, revision, localization, fault, apply);
            return new(id, label);
        }
        var choices = new List<InRunMapDetailChoice>();
        if (_session.GetPlan(location.ActIndex, location.Point) != null)
            choices.Add(Action(R("plan.clear"), () => _session.RemovePlan(location.ActIndex, location.Point, revision)));
        choices.Add(Action(M("retry_prediction"), () => { _session.Recompute(); return true; }));
        string message = M(fault.Error is PredictorDrawingWorkLimitException
            ? "node_prediction_work_limit" : "node_prediction_failed");
        var data = new InRunMapNodeDetailData(fault.Position.NodeId, M("information_failed"),
            _text.Format("predictor.runtime.position", fault.Position.Act, fault.Position.Floor),
            [message], [new("", "", [], choices)], [])
        { PrerequisiteSummary = message, IsCurrent = Current };
        _details[location] = data;
        return data;
    }

    private bool ApplyFaultChoice(string nodeId, string choiceId)
    {
        if (!_faultChoices.TryGetValue(choiceId, out var choice) || choice.NodeId != nodeId ||
            choice.Revision != _session.Revision || choice.Localization != _localizationRevision ||
            !ReferenceEquals(_session.Forecast?.Fault, choice.Fault)) return false;
        bool changed = choice.Apply();
        if (changed) Invalidate();
        return changed;
    }
}
