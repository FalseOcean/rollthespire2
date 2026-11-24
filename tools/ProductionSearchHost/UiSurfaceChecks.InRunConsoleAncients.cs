using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.InRunConsole;

internal static partial class UiSurfaceChecks
{
    private static async Task CheckConsoleAncientIcons(InRunConsolePanel panel,
        Action<bool, string> check, Func<int, Task> frames, Func<string, Task> savePicture, string label)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object? Field(string name) => panel.GetType().GetField(name, members)!.GetValue(panel);
        void Invoke(string name) => panel.GetType().GetMethod(name, members)!.Invoke(panel, null);
        var display = (OptionButton)Field("_viewMode")!;
        display.Select(1); display.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        var grid = (HFlowContainer)Field("_grid")!;
        var ancient = ModelDb.AllAncients.First(a => a.AllPossibleOptions.Any(o => o.Relic is not null));
        var search = (LineEdit)Field("_search")!;
        search.Text = ancient.Id.ToString(); search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        grid.GetChildren().OfType<Button>().Single().EmitSignal(Button.SignalName.Pressed);
        search.Text = ""; search.EmitSignal(LineEdit.SignalName.TextChanged, "");
        var optionGrid = (HFlowContainer)Field("_ancientOptionGrid")!;
        var options = ancient.AllPossibleOptions.DistinctBy(o => o.TextKey).ToArray();
        check(optionGrid.GetChildCount() == options.Length &&
            !Descendants(panel).OfType<OptionButton>().Any(b => b.Name == "ConsoleAncientOption"),
            "selected native Ancient expands its actual TextKey choices as icons without a dropdown " + label);
        var resolver = (IGameIconResolver)Field("_icons")!;
        EventOption? iconOption = null;
        foreach (var option in options)
        {
            var tile = optionGrid.GetChildren().OfType<Button>()
                .Single(b => b.GetMeta("ancient_option_text_key").AsString() == option.TextKey);
            if (option.Relic is { } relic)
            {
                var expected = resolver.Resolve(new ModelKey(relic.Id.Category, relic.Id.Entry), GameContentKind.Relic, IconVariant.RelicLarge).Texture;
                if (expected is not null)
                {
                    check(tile.GetChildren().OfType<TextureRect>().Single().Texture == expected,
                        "Ancient option art is its native EventOption.Relic association " + label + " " + option.TextKey);
                    iconOption ??= option;
                }
            }
            check(tile.AccessibilityName.Length > 0 && tile.TooltipText.Length == 0,
                "Ancient choices retain readable accessible identity without a native plain-text tooltip " + label);
        }
        check(iconOption is not null, "native Ancient fixture has a rendered associated relic icon " + label);
        string key = iconOption!.TextKey;
        optionGrid.GetChildren().OfType<Button>().Single(b => b.GetMeta("ancient_option_text_key").AsString() == key)
            .EmitSignal(Button.SignalName.Pressed);
        check((string?)Field("_selectedAncientOptionKey") == key &&
            ((Label)Field("_ancientOptionSummary")!).Text.Contains(iconOption.Title.GetFormattedText(), StringComparison.Ordinal),
            "icon selection preserves full original native TextKey and displays its title " + label);
        panel.CollapseContent(); panel.OpenCategory(3);
        check((string?)Field("_selectedAncientOptionKey") == key,
            "Ancient drawer collapse and reopen retain selected exact option " + label);
        await frames(4);
        foreach (var tile in optionGrid.GetChildren().OfType<Button>())
            foreach (var image in tile.GetChildren().OfType<TextureRect>())
                check(tile.GetGlobalRect().Grow(1).Encloses(image.GetGlobalRect()),
                    "Ancient option icon fits its compact tile " + label);
        await savePicture(label + "-selected-option-icons");
        var hoverTile = optionGrid.GetChildren().OfType<Button>().Single(b => b.GetMeta("ancient_option_text_key").AsString() == key);
        var hoverRelic = iconOption.Relic!;
        var tip = new RolltheSpire2.Infrastructure.ContentNames.RuntimeRelicTooltipResolver()
            .Resolve(new(hoverRelic.Id.Category, hoverRelic.Id.Entry), hoverTile.AccessibilityName);
        await CheckConsoleTooltip(panel, panel, hoverTile, tip.Title, tip.Description, check, frames, savePicture, label + "-relic");

        // Construct a native EventOption copy without its relic association (the
        // game's copy constructor omits Relic); do not mutate a catalog model.
        var choices = (List<EventOption>)Field("_ancientChoices")!;
        choices.Clear(); choices.Add(new EventOption(iconOption));
        Invoke("RenderAncientOptions"); await frames(4);
        var fallback = optionGrid.GetChildren().OfType<Button>().Single();
        var fallbackTitle = fallback.GetChildren().OfType<Label>().Single();
        check(!fallback.GetChildren().OfType<TextureRect>().Any() &&
            fallbackTitle.Text.Contains(iconOption.Title.GetFormattedText(), StringComparison.Ordinal) &&
            fallback.GetGlobalRect().Grow(1).Encloses(fallbackTitle.GetGlobalRect()),
            "constructed unassociated native option uses its readable title within fallback bounds " + label);
        Invoke("ResetAncientOptions");
        var normal = (Button)Field("_normalAncientOption")!;
        normal.EmitSignal(Button.SignalName.Pressed);
        check(Field("_selectedAncientOptionKey") is null && ((StyleBoxFlat)normal.GetThemeStylebox("normal")).BorderWidthLeft > 0,
            "normal generation explicitly clears forced Ancient option " + label);
        optionGrid.GetChildren().OfType<Button>().Single(b => b.GetMeta("ancient_option_text_key").AsString() == key)
            .EmitSignal(Button.SignalName.Pressed);
        var different = ModelDb.AllAncients.First(a => a.Id != ancient.Id);
        search.Text = different.Id.ToString(); search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        grid.GetChildren().OfType<Button>().Single().EmitSignal(Button.SignalName.Pressed);
        check(Field("_selectedAncientOptionKey") is null,
            "selecting a different native Ancient clears the previous identity's forced TextKey " + label);
        search.Text = ancient.Id.ToString(); search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        grid.GetChildren().OfType<Button>().Single().EmitSignal(Button.SignalName.Pressed);
        optionGrid.GetChildren().OfType<Button>().Single(b => b.GetMeta("ancient_option_text_key").AsString() == key)
            .EmitSignal(Button.SignalName.Pressed);
        search.Text = ""; search.EmitSignal(LineEdit.SignalName.TextChanged, "");
        display.Select(0); display.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
        await frames(4);
        var identityScroll = (ScrollContainer)Field("_scroll")!;
        check(grid.GetChildren().OfType<Button>().Count() == ModelDb.AllAncients.Count() &&
            grid.GetChildren().OfType<Button>().All(tile => identityScroll.GetGlobalRect().Grow(1).Encloses(tile.GetGlobalRect())),
            "all native Ancient identities fit fully in text-mode identity area " + label);
        await savePicture(label + "-text-identities");
        display.Select(1); display.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
    }
}
