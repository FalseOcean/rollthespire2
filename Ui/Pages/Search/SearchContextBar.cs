using Godot;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class SearchContextBar : PanelContainer
{
    private const int CharacterOverflowThreshold = 10;
    private readonly Label _characterLabel;
    private readonly CharacterPoolIconSelector _characters;
    private readonly Label _ascensionLabel;
    private readonly AscensionStepper _ascension;

    public SearchContextBar(
        ICharacterPoolIconProvider characterPoolIcons,
        IReadOnlyList<ModelKey> characterKeys)
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, 8f);

        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        row.AddThemeConstantOverride("separation", 10);

        _characterLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _characterLabel.CustomMinimumSize = new Vector2(54, 0);
        _characterLabel.VerticalAlignment = VerticalAlignment.Center;

        _characters = new CharacterPoolIconSelector(
            characterKeys,
            characterPoolIcons);
        _characters.Select(BaseGameModelKeys.Characters.Silent, notify: false);
        Control characterHost;
        if (_characters.RequiresHorizontalOverflow(CharacterOverflowThreshold))
        {
            var characterScroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(
                    _characters.PreferredViewportWidth(CharacterOverflowThreshold),
                    48),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                VerticalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            characterScroll.AddChild(_characters);
            characterHost = characterScroll;
        }
        else
        {
            // 1..10 characters retain the native fixed-size icon row and simply
            // extend to the right. Scrolling is a true overflow fallback only.
            characterHost = _characters;
        }

        _ascensionLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _ascensionLabel.VerticalAlignment = VerticalAlignment.Center;
        _ascension = new AscensionStepper();
        _ascension.Configure(
            SeedPredictionInputLimits.MinimumAscension,
            SeedPredictionInputLimits.MaximumAscension,
            SeedPredictionInputLimits.MaximumAscension);

        // Character + Ascension are the Query-global authored context. Preset actions
        // live in the Search page title row, not inside this context card.
        row.AddChild(_characterLabel);
        row.AddChild(characterHost);
        row.AddChild(_ascensionLabel);
        row.AddChild(_ascension);
        AddChild(row);

        _characters.SelectionChanged += _ => DraftChanged?.Invoke();
        _ascension.ValueChanged += _ => DraftChanged?.Invoke();
    }

    public event Action? DraftChanged;

    public ModelKey CharacterKey => _characters.SelectedKey;
    public int Ascension => _ascension.Value;

    public void SetRunning(bool running)
    {
        // Query-global authored context is frozen while a Search runs. Preset actions
        // remain available independently in the Search page title row.
        _characters.SetEnabled(!running);
        _ascension.SetEnabled(!running);
    }

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames, string missingIconTooltip)
    {
        _characterLabel.Text = uiText.Get(Ui1TextKey.Character);
        _ascensionLabel.Text = uiText.Get(Ui1TextKey.Ascension);
        _characters.Build(_characters.SelectedKey, contentNames, missingIconTooltip);
    }

    public void RestoreDraft(ModelKey characterKey, int ascension, bool notify)
    {
        ModelKey resolvedCharacter = _characters.ResolveAvailableSelection(characterKey);
        int resolvedAscension = Math.Clamp(ascension, SeedPredictionInputLimits.MinimumAscension, SeedPredictionInputLimits.MaximumAscension);
        _characters.Select(resolvedCharacter, notify: false);
        _ascension.SetValue(resolvedAscension, notify: false);
        if (notify) DraftChanged?.Invoke();
    }

    public void ClearDraft()
    {
        RestoreDraft(_characters.ResolveAvailableSelection(BaseGameModelKeys.Characters.Silent), SeedPredictionInputLimits.MaximumAscension, notify: true);
    }
}
