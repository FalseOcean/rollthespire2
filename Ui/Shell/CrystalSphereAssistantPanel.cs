using RolltheSpire2.Ui.Workspace;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Shell;

/// <summary>Configures the live crystal assistant; layout and frozen-session execution have separate owners.</summary>
internal sealed partial class CrystalSphereAssistantPanel : VBoxContainer
{
    private IRunState _run = null!;
    private ModRuntimeSnapshot _runtime = null!;
    private IUiTextProvider _text = null!;
    private IGameContentNameResolver _names = null!;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("crystal-options");
    private string _language = "zh";
    private SearchWorkspace _preferences = null!;
    internal event Action<CrystalSphereLiveSnapshot,string,PredictorCrystalSolution>? GuideRequested;
    private string T(string key) => _text.Get("predictor.crystal."+key);
    private PredictorCrystalSnapshot Source => _snapshot!.Branches[_branch.Selected].Snapshot;
    private string Mode => _snapshot!.Branches[_branch.Selected].Mode;
    internal void ApplyLanguage(string language)
    {
        if(_language==language) return;
        _language=language;_text=JsonUiTextProvider.CreatePredictorUi13(language);
        _names=RuntimeGameContentNameResolver.Create(language);
        foreach(var (control,key) in _labels) control.Text=T(key);
        foreach(var (control,key) in _buttons) control.Text=T(key);
        ApplyOptionText();
        if(_snapshot!=null) for(int i=0;i<_snapshot.Branches.Length;i++)
        {
            var branch=_snapshot.Branches[i];_branch.SetItemText(i,T(branch.Mode)+(branch.Mode=="Remaining"?" · "+string.Format(T("remaining"),branch.Snapshot.Remaining):""));
        }
        _contentRevision++;_renderedTargets=default;
        Render();
    }
    internal void Initialize(IRunState run, ModRuntimeSnapshot runtime)
    {
        _run=run; _runtime=runtime;
        _preferences=SearchWorkspace.Open(OS.GetUserDataDir());
        var preferences=_preferences.Preferences;
        MainPanelTheme.SelectFont(preferences.FontChoice);
        UiAppearance.Initialize(preferences.Appearance);
        Theme=MainPanelTheme.Create();
        _language=RolltheSpire2.Ui.Settings.UiLanguagePreferences.Resolve(preferences.LanguageOverride);
        _text=JsonUiTextProvider.CreatePredictorUi13(_language); _names=RuntimeGameContentNameResolver.Create(_language);
        _avoidCurse.SetPressedNoSignal(preferences.CrystalSphereAvoidCurse);
        _includeDriftwood.SetPressedNoSignal(preferences.CrystalSphereIncludeDriftwood);
        _includeEnchantments.SetPressedNoSignal(preferences.CrystalSphereIncludeEnchantments);
        _transparentMask.SetPressedNoSignal(CrystalSphereMaskAppearance.Enabled);
        BuildLayout();
        Render();
    }

    private void ApplyOptionText()
    {
        foreach(var (control,key) in new[] { (_avoidCurse,"avoid_curse"), (_includeDriftwood,"include_driftwood"),
            (_includeEnchantments,"include_enchantments"), (_transparentMask,"transparent_mask") })
        {
            control.Text=T(key);
            SearchTooltip.Set(control,T(key+"_hint"));
        }
    }
}
