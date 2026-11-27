using System.Diagnostics;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Theme;

internal static class NodeHoverChecks
{
    internal static async Task CheckMapHoverStart(Node root, string output)
    {
        var results = new List<object>();
        bool passed = true;
        // The real map initializes its collection picker and these fonts before
        // it accepts pointer input. Keep that cost out of the opening interval.
        using var pickerTheme = MainPanelTheme.Create();
        foreach (var (language, width) in new[] { ("zh", 1280), ("en", 900) })
        {
            var screen = new SubViewport { Size = new(width, 720) };
            root.AddChild(screen);
            var view = new InRunMapNodeDetails(new ReflectionGameIconResolver("map-hover-start"),
                RuntimeGameContentNameResolver.Create(language));
            screen.AddChild(view);
            var relic = new InRunMapDetailObject(new("RELIC", "ANCHOR"), GameContentKind.Relic,
                DisplayName: language == "zh" ? "船锚" : "Anchor");
            string action = language == "zh" ? "领取" : "Take";
            InRunMapDetailSection treasure = new("", "", [], [new("take", action, relic)]) { BranchRow = true };
            var cards = new[] { "FEED", "BLUDGEON", "BASH" }.Select((key, i) =>
                new InRunMapDetailObject(new ModelKey("CARD", key), GameContentKind.Card)
                { CardSnapshot = new(i + 1, new("CARD", key), EffectCardType.Attack, 0, 1, true, 1) }).ToArray();
            InRunMapDetailSection group = new("Boss", "", cards, []) { LargeCardResults = true };
            var viewport = view.GetViewportRect();
            var anchor = new Rect2(view.GetGlobalMousePosition() - new Vector2(10, 10), new Vector2(20, 20));

            // Exercise the actual initial hover call separately from subsequent engine frames.
            var start = Stopwatch.StartNew();
            view.ShowNode(new("boss", "Boss", "", [], [group], []), anchor, viewport);
            double openMs = start.Elapsed.TotalMilliseconds;
            var stack = (NeowOperationOverlay)view.FindChild("MapRewardCardStack_0", true, false);
            int synchronousFaces = stack.NativeCardCount;
            bool emptyDockInterceptsPointer = view.RewardCardDockGlobalRegion.HasArea();
            var items = view.FindChild("CardRewardRow", true, false).GetChildren();
            int initialNames = items.Count(row => row.FindChildren("*", "Label", true, false)
                .OfType<Label>().Any(label => label.IsVisibleInTree() && label.Text.Length != 0));
            int frames = 0;
            while (stack.HasPendingCardFaces && frames < 30)
            {
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                frames++;
            }
            int completed = stack.NativeCardCount;
            var faces = stack.FindChildren("*", "", true, false).OfType<NCard>()
                .Where(card => card.IsVisibleInTree()).Select(card => card.Model?.Id.Entry).ToArray();
            view.ShowNode(new("chest", "Treasure", "", [], [treasure], []), anchor, viewport);
            for (int i = 0; i < 4; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            var button = (Button)((HBoxContainer)view.FindChild("EventBranchRow", true, false)).GetChild(0);
            var labels = button.FindChildren("*", "Label", true, false).OfType<Label>()
                .Where(label => label.IsVisibleInTree() && !string.IsNullOrWhiteSpace(label.Text)).ToArray();
            bool overlapping = labels.Any(a => labels.Any(b => a != b && a.GetGlobalRect().Intersects(b.GetGlobalRect())));
            string text = string.Join(" ", labels.Select(label => label.Text));
            bool correct = viewport.Size.X == width && view.HoverOpen && !overlapping &&
                text.Contains(action) && text.Contains(relic.DisplayName!) && synchronousFaces == 0 && !emptyDockInterceptsPointer &&
                initialNames == cards.Length && faces.SequenceEqual(cards.Select(card => card.Key.Entry));
            results.Add(new { language, width, openMs, synchronousFaces, initialNames, emptyDockInterceptsPointer, completed, frames, faces, text, overlapping, correct });
            passed &= correct;
            root.RemoveChild(screen);
            screen.QueueFree();
        }
        File.WriteAllText(Path.Combine(output, "map-hover-start.json"), JsonSerializer.Serialize(new { passed, results }));
        if (!passed) throw new InvalidDataException("Map node hover layout or staged card display failed; see map-hover-start.json");
    }
}
