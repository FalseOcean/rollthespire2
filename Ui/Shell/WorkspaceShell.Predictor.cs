using Godot;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private void CreatePredictor(string? initialSeed = null)
    {
        var icons = new ReflectionGameIconResolver("predictor-workbench");
        var tooltip = new AnchoredTooltipHost(new RuntimeRelicTooltipResolver(), new RuntimePotionTooltipResolver(), new RuntimeCardTooltipResolver());
        AddChild(tooltip); tooltip.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _predictor = new AnalysisPage(_runtime, icons, new NativeCharacterPoolIconProvider(icons),
            RuntimeCharacterCatalogCapture.Capture().EffectiveCharacters, tooltip);
        _content.AddChild(_predictor); _predictor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _predictor.CopySeedRequested += DisplayServer.ClipboardSet;
        _predictor.FavoriteSeedRequested += FavoriteCurrentPrediction;
        var settings = new RuntimePredictionSettings(); _persistence!.ApplyPreferencesToRuntimeSettings(settings);
        _predictorController = new AnalysisPageController(_predictor, _runtime, settings,
            new NullRuntimePredictionDiagnosticSink(), _persistence, (_, _, _) => { });
        var saved = _persistence.PredictorContext;
        var character = ModelKey.TryParseExact(saved.CharacterKey, out var key) && key.IsValid ? key : BaseGameModelKeys.Characters.Silent;
        _predictor.SetUnlockState(saved.AllCharacterCardPoolsUnlocked);
        _predictor.SetPredictorContext(character, saved.Ascension, 1, 0,
            saved.AncientOptionConditions, saved.PreferredOpeningRouteId,
            saved.PreferredOpeningChoiceSlotIndex >= 0 ? saved.PreferredOpeningChoiceSlotIndex : null,
            saved.PreferredRewardRouteGroupId, notify: false);
        _predictor.SetSeedText(initialSeed ?? saved.Seed ?? "", commit: true);
        _predictor.ApplyLocalization(JsonUiTextProvider.CreatePredictorUi13(_languageCode), RuntimeGameContentNameResolver.Create(_languageCode));
        _predictorController.RestorePartyConfiguration(saved, initialSeed is null);
        // Restore a valid saved seed directly into the existing reactive prediction path.
        if (initialSeed is not null ||
            _runtime.Profile.TryCanonicalizeSeed(_predictor.CurrentDraft.RawSeed, out _, out _))
            _predictorController.AnalyzeCurrentDraft();
    }
}
