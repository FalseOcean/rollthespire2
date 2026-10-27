using System.Collections.Immutable;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    // A presentation receipt for an uncomputed drawn suffix. It is not a
    // simulated room, encounter, RNG state or actionable node plan.
    internal ImmutableDictionary<(int ActIndex, MapPosition Point), InRunMapNodeDetailData> DeferredDetails(
        int actIndex, ImmutableArray<MapPosition> readyPrefix)
    {
        var forecast = _session.Forecast;
        var result = ImmutableDictionary.CreateBuilder<(int, MapPosition), InRunMapNodeDetailData>();
        if (forecast == null || readyPrefix.IsEmpty || forecast.ReachedLimit) return result.ToImmutable();
        var last = forecast.Nodes.LastOrDefault();
        var terminal = forecast.TerminalBoundary;
        if (terminal == null && (last == null || last.Advanced)) return result.ToImmutable();
        var source = terminal?.DeparturePosition ?? last!.Position;
        if (source.Act != actIndex + 1) return result.ToImmutable();
        int after = -1;
        if (PredictorMapNodeIdentity.TryParse(source.NodeId, out var sourceIdentity))
            after = readyPrefix.IndexOf(sourceIdentity.Point);
        var pending = terminal?.PendingCauses ?? last!.PendingCauses.Where(cause =>
            (cause.AffectedDomains & PredictorCombatDomain.Continuation) != 0).ToImmutableArray();
        var reasons = PendingLines(pending).Distinct().ToArray();
        string sourceLabel = Source(source.NodeId);
        string waiting = _text.Format("predictor.map.deferred_source", sourceLabel);
        long revision = _session.Revision, localization = _localizationRevision;
        foreach (var point in readyPrefix.Skip(after + 1))
        {
            var location = (actIndex, point);
            if (_session.Nodes.ContainsKey(location) || _session.TerminalBoundaries.ContainsKey(location)) continue;
            result[location] = new($"deferred-view:{actIndex}:{point.Column}:{point.Row}:{revision}",
                M("deferred_title"), "", [waiting, .. reasons], [], [])
            {
                PrerequisiteSummary = waiting,
                IsCurrent = () => revision == _session.Revision && localization == _localizationRevision &&
                    ReferenceEquals(forecast, _session.Forecast)
            };
        }
        return result.ToImmutable();
    }
}
