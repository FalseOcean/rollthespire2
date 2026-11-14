using Godot;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private LiveRunPredictionPremises? _liveRunPremises;
    private Label _liveRunNotice = null!;

    internal void SetLiveRunPredictionPremises(LiveRunPredictionPremises? premises)
    {
        _liveRunPremises = premises;
        RefreshLiveRunNotice();
        RefreshFavoriteSeed();
    }

    private string LiveRunNotice()
    {
        if (_liveRunPremises is not { } premises || _uiText is null) return "";
        if (!premises.IsStandardReference) return Text("predictor.live.standard_current");
        string mode = premises.GameMode switch
        {
            "Standard" when premises.IsCustomGame => Text("predictor.live.mode.custom_standard"),
            "Standard" => Text("predictor.live.mode.standard"),
            "Custom" => Text("predictor.live.mode.custom"),
            "Daily" => Text("predictor.live.mode.daily"),
            "Dread" => Text("predictor.live.mode.dread"),
            "Blitz" => Text("predictor.live.mode.blitz"),
            _ => premises.GameMode
        };
        if (!premises.Modifiers.IsEmpty) mode += " · " + Text("predictor.live.with_modifiers");
        if (!premises.StartedWithNeow) mode += " · " + Text("predictor.live.without_neow");
        return _uiText.Format("predictor.live.standard_reference", mode);
    }

    private void RefreshLiveRunNotice()
    {
        _liveRunNotice.Text = LiveRunNotice();
        _liveRunNotice.Visible = _liveRunNotice.Text.Length > 0;
    }
}
