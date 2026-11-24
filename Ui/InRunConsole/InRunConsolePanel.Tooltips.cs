using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.InRunConsole;

internal sealed partial class InRunConsolePanel
{
    private readonly RuntimeCardTooltipResolver _contentCardTooltipResolver = new();
    private AnchoredTooltipHost? _contentTooltipHost;

    // The overlay owns this host alongside the scaled panel, in its CanvasLayer.
    // Keeping it outside every clipped tile/scroll also keeps viewport positions
    // in the same coordinate system used by AnchoredTooltipHost.
    internal void AttachContentTooltips(Control surface)
    {
        _contentTooltipHost = new AnchoredTooltipHost(new RuntimeRelicTooltipResolver(),
            new RuntimePotionTooltipResolver(), _contentCardTooltipResolver);
        surface.AddChild(_contentTooltipHost);
        _contentTooltipHost.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        VisibilityChanged += () => { if (!IsVisibleInTree()) DismissContentTooltip(); };
        TreeExiting += DismissContentTooltip;
    }

    internal void DismissContentTooltip() => _contentTooltipHost?.Dismiss();

    private void BindContentTooltip(Control tile, AbstractModel model, string title)
    {
        GameContentKind? kind = model switch
        {
            RelicModel => GameContentKind.Relic,
            PotionModel => GameContentKind.Potion,
            CardModel => GameContentKind.Card,
            EventModel and not AncientEventModel => GameContentKind.Event,
            EncounterModel => GameContentKind.Encounter,
            _ => null
        };
        if (kind is null) return;
        bool heldCard = model is CardModel && _holdings.ButtonPressed;
        tile.TooltipText = string.Empty;
        tile.MouseEntered += () =>
        {
            if (_contentTooltipHost is null) return;
            if (model is EncounterModel || model is EventModel and not AncientEventModel)
            {
                _contentTooltipHost.ShowText(tile, title);
            }
            else if (heldCard && model is CardModel instance)
                ShowHeldCardTooltip(tile, instance, title);
            else
                _contentTooltipHost.ShowFor(tile, KeyOf(model), kind.Value, title);
        };
        tile.MouseExited += () => _contentTooltipHost?.Dismiss(tile);
        tile.TreeExiting += () => _contentTooltipHost?.Dismiss(tile);
    }

    private void ShowHeldCardTooltip(Control tile, CardModel card, string fallbackTitle)
    {
        string title = fallbackTitle;
        string metadata = string.Empty;
        string rules = string.Empty;
        try
        {
            metadata = CardInstanceDetails(card);
            title = card.Title;
            // The shared resolver accepts only ModelKey and therefore describes
            // the canonical card. Read the bound instance's official formatter
            // for upgraded/enchantment rules; never substitute canonical rules
            // if instance formatting fails. Preserve the resolver's mod-hook scope.
            if (card.GetType().Assembly == typeof(CardModel).Assembly)
                rules = PlainInstanceCardRules(card.GetDescriptionForPile(PileType.None, null));
        }
        catch (Exception ex)
        {
            RuntimeLog.WarnException($"inRunConsoleCardTooltipFailed=true;card={card.Id}", ex);
        }
        _contentTooltipHost!.ShowStructuredText(tile, title, metadata, rules);
    }

    // Match the existing runtime resolver's plain official rules presentation.
    private static string PlainInstanceCardRules(string value)
    {
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;
        string text = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = Regex.Replace(text, @"\[(?:br|p)\s*/?\]", "\n", options);
        text = Regex.Replace(text, @"\[img(?:=[^\]]*)?\].*?\[/img\]", string.Empty, options);
        text = Regex.Replace(text, @"\[img(?:=[^\]]*)?\]", string.Empty, options);
        text = Regex.Replace(text, @"\[(?:/?)[a-z][^\]]*\]", string.Empty, options);
        return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }
}
