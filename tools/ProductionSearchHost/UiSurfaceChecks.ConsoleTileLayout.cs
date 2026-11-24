using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Ui.InRunConsole;
using RolltheSpire2.Ui.Pages.Search.Event;

internal static partial class UiSurfaceChecks
{
    private static async Task CheckConsoleTileLayout(InRunConsolePanel panel,
        Action<bool, string> check, Func<int, Task> frames, Func<string, Task> savePicture, string label)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object Field(string name) => panel.GetType().GetField(name, members)!.GetValue(panel)!;
        var grid = (HFlowContainer)Field("_grid");
        var tiles = grid.GetChildren().OfType<Button>().ToArray();
        bool holdings = ((CheckButton)Field("_holdings")).ButtonPressed;
        await frames(4);
        if (!holdings)
        {
            check(tiles.Length >= 6, "native catalog supplies at least six text tiles " + label);
            float firstY = tiles[0].Position.Y;
            check(tiles.Take(5).All(tile => Mathf.IsEqualApprox(tile.Position.Y, firstY)) &&
                tiles[5].Position.Y > firstY,
                "text catalog lays out exactly five tiles per row " + label + ";width=" + grid.Size.X);
        }

        void CheckTitleAndArt(Button tile)
        {
            var title = tile.GetChildren().OfType<Label>().First(item => item.Text != "?" && item.Name != "ConsoleCardInstanceDetails");
            var art = tile.GetChildren().OfType<Control>().First(control => control is TextureRect or EventThumbnailView ||
                control is Label missing && missing.Text == "?");
            check(title.MaxLinesVisible == 2 && title.Size.Y >= 2 * title.GetLineHeight() + title.GetThemeConstant("line_spacing"),
                "card/event title reserves two actual font lines " + label + " " + tile.AccessibilityName + ";height=" + title.Size.Y + ";lineHeight=" + title.GetLineHeight() + ";spacing=" + title.GetThemeConstant("line_spacing"));
            check(tile.GetGlobalRect().Grow(1).Encloses(title.GetGlobalRect()) &&
                title.GetRect().End.Y <= art.Position.Y && tile.GetGlobalRect().Grow(1).Encloses(art.GetGlobalRect()),
                "two-line title and art fit separately inside text tile " + label + " " + tile.AccessibilityName);
            if (art is TextureRect image)
                check(image.StretchMode == TextureRect.StretchModeEnum.KeepAspectCentered,
                    "smaller card preserves its complete image " + label);
            var details = tile.GetChildren().OfType<Label>().FirstOrDefault(item => item.Name == "ConsoleCardInstanceDetails");
            if (details is not null)
                check(details.Position.Y >= art.GetRect().End.Y && tile.GetGlobalRect().Grow(1).Encloses(details.GetGlobalRect()),
                    "held card instance details remain below the complete image " + label);
        }
        foreach (var tile in tiles) CheckTitleAndArt(tile);

        // Exercise wrapping with a deliberately long English title through the
        // production tile factory, without changing a catalog model or selection.
        var candidates = (IEnumerable<AbstractModel>)panel.GetType().GetMethod("Candidates", members)!.Invoke(panel, null)!;
        var model = candidates.First();
        string longTitle = model is CardModel ? "A Surprisingly Long English Card Name" : "A Surprisingly Long English Event Name";
        var generated = (Button)panel.GetType().GetMethod("CreateTile", members)!.Invoke(panel, [model, longTitle, false])!;
        try
        {
            grid.MoveChild(generated, 0);
            await frames(4);
            CheckTitleAndArt(generated);
            check(generated.GetChildren().OfType<Label>().First(item => item.Text == longTitle).GetLineCount() >= 2,
                "long English card/event fixture actually wraps " + label);
            await savePicture(label + "-two-line-title");
        }
        finally
        {
            grid.RemoveChild(generated); generated.QueueFree();
            await frames(3);
        }
    }
}
