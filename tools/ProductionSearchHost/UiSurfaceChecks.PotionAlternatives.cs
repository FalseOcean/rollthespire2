using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task RunPotionAlternativeDisplayChecks(Node root, string output, SubViewport viewport,
        Control hoverRoot, PredictorOperationSnapshot basis, Action<bool, string> check)
    {
        ModelKey P(string key) => new("POTION", key);
        var c = P("COLORLESS_POTION"); var u = P("COSMIC_CONCOCTION"); var s = P("SKILL_POTION"); var a = P("ATTACK_POTION");
        var t = P("POWER_POTION");
        (string Name, ImmutableArray<OperationIconPath> Paths, bool ExpectedPair)[] cases =
        [
            ("choice-bracket", [new([[c, s], [t]]), new([[u, s], [t]])], true),
            ("required-both", [new([[c, u], [t]])], false),
            ("correlated-positions", [new([[c], [c], [t]]), new([[u], [u], [t]])], false),
            ("different-continuations", [new([[c], [s], [t]]), new([[u], [a], [t]])], false),
            ("safe-local-subset", [new([[c], [s], [t]]), new([[u], [s], [t]]), new([[u], [a], [t]])], true),
            ("two-required-copies", [new([[c, c], [t]]), new([[u, u], [t]])], false)
        ];
        NeowOperationOverlay? view = null; var evidence = new List<object>();
        async Task Frames() { for (int i = 0; i < 4; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        try
        {
            foreach (var test in cases)
            {
                var card = basis.State.Card(basis.State.Deck[0]);
                var group = new OperationPreviewCardGroup(1, [card], 1, 1, false, false) { IconPaths = test.Paths };
                var preview = new OperationPreview(t, [], [], false, [new([], [new(t, [], [group])], [], 1, true)], [])
                    { Style = new(OperationTextMode.None, OperationCardLayout.Groups) };
                view?.Free(); view = new NeowOperationOverlay(); hoverRoot.AddChild(view); view.SetPreview(preview); await Frames();
                view.SetLayout(new(), new(new Vector2(80, 180), new Vector2(1150, 760))); await Frames();
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-potion-alternative-" + test.Name + "-zh.png"));
                CheckOperationIconPaths(view, preview, check);
                int pairs = Descendants(view).Count(node => node.HasMeta("rt2_alternatives"));
                check(test.ExpectedPair ? pairs > 0 : pairs == 0, test.Name + " uses an either-bottle glyph only for a proven single local identity choice");
                evidence.Add(new { test.Name, pairs, test.Paths });
            }
            File.WriteAllText(Path.Combine(output, "potion-alternative-display-result.json"), JsonSerializer.Serialize(new
                { passed = true, displayOnly = true, realNativePotionArtwork = true, evidence }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { view?.Free(); }
    }
}
