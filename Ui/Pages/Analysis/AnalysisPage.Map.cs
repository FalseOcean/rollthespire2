using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private CancellationTokenSource? _mapLoad;
    private readonly MapWorkspace _mapWorkspace;
    private void ResetMaps() { CancelMapLoad(); _mapWitnessRequest = null; _mapWorkspace.Close(); _mapWorkspace.ResetRouteContext(); }
    private void CancelMapLoad() { _mapLoad?.Cancel(); _mapLoad = null; }

    private void OnMapRequested(int act, Control opener)
    {
        if (LastRequest is null || LastDocument is null || _viewModel is null || _uiText is null) return;
        _mapWitnessRequest = null;
        _workbenchRoot!.Hide();
        _mapWorkspace.Open(act, LastDocument.CanonicalSeed, _uiText, opener);
    }

    private async void LoadMapAct(int act)
    {
        CancelMapLoad();
        if (LastRequest is null || LastDocument is null || _viewModel is null || _uiText is null) return;
        var request = LastRequest;
        if (ReferenceEquals(_mapWitnessRequest, request))
        {
            await LoadWitnessMapAct(act, request);
            return;
        }
        var actInfo = _viewModel.EventPoolSequenceDomain.Items.FirstOrDefault(item => item.Act == act);
        if (actInfo is null) { _mapWorkspace.SetStatus(_uiText.Get("ui1.map.unavailable")); return; }
        var bosses = _viewModel.BossDomain.Items.Where(item => item.Act == act).OrderBy(item => item.Ordinal).ToArray();
        var bossIcons = bosses.Select(b => _icons.Resolve(b.BossDisplay.ModelKey, GameContentKind.Encounter,
            IconVariant.WorldCompendiumBossIcon).Texture).ToArray();
        var ancient = _viewModel.AncientDomain.Items.FirstOrDefault(item => item.Act == act);
        Texture2D? ancientIcon = ancient is null ? null : _icons.Resolve(ancient.AncientDisplay.ModelKey,
            GameContentKind.Event, IconVariant.WorldCompendiumAncientIcon).Texture;
        using var cancel = new CancellationTokenSource();
        _mapLoad = cancel;
        _mapWorkspace.SetStatus(_uiText.Get("ui1.map.loading"));
        try
        {
            var lifetime = (_overviewLifetime ??= new CancellationTokenSource()).Token;
            var map = await GetOverviewMap(act, actInfo.ActKey, bosses.Length > 1, lifetime).WaitAsync(cancel.Token);
            // Resumes on Godot's UI synchronization context. Never publish a stale request's map.
            if (!cancel.IsCancellationRequested && IsInstanceValid(_mapWorkspace) && _mapWorkspace.IsInsideTree() && _mapWorkspace.Visible && _mapWorkspace.SelectedAct == act && LastRequest == request)
                _mapWorkspace.ShowMap(map, bossIcons, ancientIcon);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex)
        {
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                "PredictionMapFailed", nameof(AnalysisPage), "MapWorker", "issues.capture_failed", correlationId: "act=" + act);
            if (!cancel.IsCancellationRequested && IsInstanceValid(_mapWorkspace) && _mapWorkspace.IsInsideTree() && _mapWorkspace.Visible && _mapWorkspace.SelectedAct == act)
                _mapWorkspace.SetStatus(_uiText.Get("ui1.map.unavailable") + " · " + ex.Message);
        }
        finally { if (ReferenceEquals(_mapLoad, cancel)) _mapLoad = null; }
    }

    private async Task LoadWitnessMapAct(int act, RolltheSpire2.Core.Prediction.SeedPredictionRequest request)
    {
        using var cancel = new CancellationTokenSource();
        _mapLoad = cancel;
        _mapWorkspace.SetStatus(_uiText!.Get("ui1.map.loading"));
        string seed = LastDocument!.CanonicalSeed;
        try
        {
            var map = await Task.Run(() => MapRouteWitnessProjection.CreateMap(seed, act, request.Ascension, request.PlayersCount, cancel.Token), cancel.Token);
            if (!cancel.IsCancellationRequested && IsInstanceValid(_mapWorkspace) && _mapWorkspace.IsInsideTree() &&
                _mapWorkspace.Visible && _mapWorkspace.SelectedAct == act && ReferenceEquals(LastRequest, request) && ReferenceEquals(_mapWitnessRequest, request))
                _mapWorkspace.ShowMap(map, [], null);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception exception)
        {
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception,
                "MapWitnessGraphFailed", nameof(AnalysisPage), "ResultMapWitness", "issues.capture_failed", correlationId: $"seed={seed};act={act}");
            if (!cancel.IsCancellationRequested && IsInstanceValid(_mapWorkspace) && _mapWorkspace.Visible && _mapWorkspace.SelectedAct == act)
                _mapWorkspace.SetStatus(_uiText!.Get("ui1.map.witness.failed") + " · " + exception.Message);
        }
        finally { if (ReferenceEquals(_mapLoad, cancel)) _mapLoad = null; }
    }
}
