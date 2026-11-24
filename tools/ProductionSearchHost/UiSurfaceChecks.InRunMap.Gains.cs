using System.Reflection;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.InRunMap;

internal static partial class UiSurfaceChecks
{
    private static async Task CheckInRunMapGainsLayout(Node root, string output, InRunMapNodeDetails existing,
        Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var icons = (IGameIconResolver)typeof(InRunMapNodeDetails).GetField("_icons", flags)!.GetValue(existing)!;
        var names = (IGameContentNameResolver)typeof(InRunMapNodeDetails).GetField("_names", flags)!.GetValue(existing)!;
        var view = new InRunMapNodeDetails(icons, names); root.AddChild(view);
        // Display-only geometry/word-wrap fixture. Actual pointer continuity is
        // exercised separately in LayeredLayout's injected-pointer viewport.
        view.SetProcess(false);
        var repeated = new InRunMapDetailObject(new ModelKey("POTION", "STRENGTH_POTION"), GameContentKind.Potion,
            DisplayName: "Repeated potion");
        var sections = Enumerable.Range(0, 7).Select(index => new InRunMapDetailSection("old reward category",
            index == 1 ? "×2 · retained state" : "", index == 5 ? [] : [index == 4 ? repeated with {
                DisplayName = string.Join(" ", Enumerable.Repeat("Long localized reward name", 8)) } : repeated], [])
            { GainReward = true, StableId = "gain-opportunity-" + index }).ToArray();
        sections[5] = sections[5] with { Title = "Unknown relic", Text = "Independent unresolved reward" };
        var data = new InRunMapNodeDetailData("gain-fixture", "Compact gain geometry fixture", "", [], sections, []);
        async Task Frames()
        { for (int i = 0; i < 7; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        foreach (float width in new[] { 1680f, 800f })
        {
            var bounds = new Rect2(0, 0, width, 720);
            var anchor = new Rect2(90, 130, 56, 56);
            view.ShowNode(data, anchor, bounds); await Frames(); view.UpdateAnchor(anchor, bounds); await Frames();
            var items = Descendants(view).OfType<Control>().Where(node => node.Name.ToString().StartsWith("NodeGainItem_", StringComparison.Ordinal)).ToArray();
            check(items.Length == 7 && items.Select(item => item.GetMeta("reward_group").AsString())
                .SequenceEqual(sections.Select(section => section.StableId)),
                "gain layout preserves repeated entries and opportunity order at width " + width);
            var rows = Descendants(view).OfType<HFlowContainer>().Where(node => node.Name.ToString().StartsWith("NodeGainRow_", StringComparison.Ordinal)).ToArray();
            check(rows.Length == 3 && rows.All(row => row.GetChildCount() <= 3),
                "gain area has at most three opportunities per native flow row");
            check(items.All(item => item.GetGlobalRect().End.X <= view.HoverGlobalRegion.End.X + 1) &&
                items[3].GlobalPosition.Y > items[0].GlobalPosition.Y,
                "gain rows wrap within the actual panel width");
            var labels = Descendants(view).OfType<Label>().ToArray();
            check(labels.Any(label => label.Text == "×2 · retained state") &&
                labels.Any(label => label.Text.Contains("Independent unresolved reward", StringComparison.Ordinal)) &&
                labels.Count(label => label.Name == "NodeGainName" && label.Text == repeated.DisplayName) == 5,
                "gain display retains quantity/state, unknown scope and duplicate names");
            var longName = labels.Single(label => label.Name == "NodeGainName" && label.Text.StartsWith("Long localized", StringComparison.Ordinal));
            check(!longName.ClipText && longName.AutowrapMode == TextServer.AutowrapMode.WordSmart && longName.GetLineCount() > 1,
                "long gain names wrap fully instead of clipping or ellipsis");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var picture = root.GetViewport().GetTexture().GetImage();
            picture.SavePng(Path.Combine(output, "in-run-map-gains-" + width + ".png"));
        }
        view.CloseHover(); root.RemoveChild(view); view.QueueFree();
    }
}
