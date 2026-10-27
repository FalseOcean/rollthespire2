using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    // Explicit display data tests only the native renderer. Numerical/native
    // operation acceptance remains in the existing operation checks.
    private static async Task RunOperationPolishDisplayChecks(Node root, string output, SubViewport viewport,
        Control hoverRoot, PredictorOperationSnapshot basis, Action<bool, string> check)
    {
        var card = basis.State.Deck.Select(basis.State.Card).First() with { Id = 100001 };
        var nextCard = card with { Id = 100002 };
        var evidence = new List<object>();
        NeowOperationOverlay? view = null;
        static ModelKey Relic(string entry) => new("RELIC", entry);
        static ModelKey Event(string entry) => new("EVENT", entry);
        static string RelicName(string entry) => new LocString("relics", entry + ".title").GetFormattedText();
        static OperationPreview Preview(ModelKey source, params OperationPreviewSection[] sections) =>
            new(source, [], [], false, [new([], sections.ToImmutableArray(), [], 1, true)], []);
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Render(OperationPreview preview)
        {
            view?.Free();
            view = new NeowOperationOverlay(); hoverRoot.AddChild(view);
            view.SetPreview(preview);
            await Frames();
            Vector2 summary = view.SummaryDesiredSize;
            view.SetLayout(new(new Vector2(80, 180), summary), new(new Vector2(560, 180), new Vector2(1250, 780)));
            await Frames();
            check(ReferenceEquals(PredictorOperationCapture.ReadNullable(view, "_preview"), preview),
                "polish display uses the exact supplied immutable projection");
            check(Descendants(view).OfType<NCard>().All(native => native.Model is { RunState: null, CombatState: null } &&
                native.MouseFilter == Control.MouseFilterEnum.Ignore), "polish native card faces remain detached and passive");
        }
        async Task Capture(string name)
        {
            check(view!.VisibleRegions.All(region => view.GetViewportRect().Grow(.5f).Encloses(region)),
                name + " stays in its actual native viewport");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-polish-" + name + "-zh.png")) == Error.Ok,
                name + " native screenshot is saved");
            evidence.Add(new { name, summary = view.SummaryDesiredSize.ToString(), nativeCards = Descendants(view).OfType<NCard>().Count(),
                labels = Descendants(view).OfType<Label>().Where(label => label.IsVisibleInTree()).Select(label => label.Text).ToArray() });
        }
        try
        {
            var cauldron = Relic("CAULDRON");
            var potion = new ModelKey("POTION", "ATTACK_POTION");
            await Render(Preview(cauldron, new OperationPreviewSection(cauldron,
                [new(OperationPreviewAction.GeneratePotion, [potion, potion], [], false)], [])));
            var obtainLabels = Descendants(view!).OfType<Label>().Where(label => label.Text == "获得").ToArray();
            string potionName = new LocString("potions", "ATTACK_POTION.title").GetFormattedText();
            check(obtainLabels.Length == 2 && obtainLabels.Select(label => label.GetParent()).Distinct().Count() == 2 &&
                obtainLabels[0].GlobalPosition.Y < obtainLabels[1].GlobalPosition.Y &&
                Descendants(view!).OfType<Label>().Count(label => label.Text == potionName) == 2,
                "two identical generated potions retain two obtain rows with their two real identities");
            check(Math.Abs(view!.SummaryDesiredSize.X - 360) <= 1,
                "compact two-potion summary matches the native 360px tooltip width");
            await Capture("duplicate-potions");

            var courier = Relic("THE_COURIER");
            await Render(Preview(courier, new OperationPreviewSection(courier,
                [new OperationPreviewRow(OperationPreviewAction.Restock, [], [card], false) { Price = 123 }], [])));
            var price = Descendants(view!).OfType<HBoxContainer>().Single(node => node.Name == "Rt2OperationPrice");
            var gold = price.GetChildren().OfType<TextureRect>().Single();
            var amount = price.GetChildren().OfType<Label>().Single();
            var nativeCard = Descendants(view!).OfType<NCard>().Single();
            var tile = nativeCard.GetParent<Control>();
            check(gold.Texture != null && gold.Texture.GetWidth() > 0 &&
                gold.Texture.ResourcePath == "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_gold.tres" && amount.Text == "123" &&
                amount.GetThemeColor("font_color") == new Color("f4d477") &&
                price.GetGlobalRect().Position.Y >= tile.GetGlobalRect().End.Y,
                "Courier card price uses the loaded native gold icon and gold number below the actual card");
            var summaryLabels = Descendants(view!.GetNode<Control>("Rt2NeowSummary")).OfType<Label>().ToArray();
            check(summaryLabels.Any(label => label.Text == RelicName("THE_COURIER")) &&
                summaryLabels.All(label => !label.Text.Contains("123", StringComparison.Ordinal)) &&
                price.GetParent<Control>().GetChildren().OfType<Label>().All(label => !label.Text.Contains("123", StringComparison.Ordinal)),
                "pure restock panel is owned by Courier and keeps card price out of title and caption");
            await Capture("courier-card-price");

            var morphic = Event("MORPHIC_GROVE");
            var failure = new OperationPreviewFault("DisplayFixture", [], new InvalidOperationException("explicit display fixture"));
            var transform = Preview(morphic, new OperationPreviewSection(morphic,
                [new(OperationPreviewAction.TransformCard, [], [card, nextCard], false)], [])) with
            { OptionKey = "GROUP", PendingReasons = ["DisplayFixture:MissingInput"], Faults = [failure] };
            await Render(transform);
            var caption = view!.GetNode<Control>("Rt2NeowCardDock").GetChildren().OfType<Control>()
                .SelectMany(stack => stack.GetChildren().OfType<Label>()).Single();
            check(view.GetNodeOrNull<Control>("Rt2NeowSummary") == null && view.SummaryDesiredSize == Vector2.Zero &&
                caption.Text.Contains("部分结果预测失败", StringComparison.Ordinal) &&
                caption.Text.Contains("部分结果需要尚未确定的输入", StringComparison.Ordinal) &&
                Descendants(view).OfType<NCard>().Count() == 2 &&
                transform.PendingReasons.SequenceEqual(["DisplayFixture:MissingInput"]) && transform.Faults.Single() == failure,
                "Morphic keeps real pending/failure information beside native cards without a left summary or DTO loss");
            await Capture("morphic-pending");

            var trash = Event("TRASH_HEAP");
            await Render(Preview(trash, new OperationPreviewSection(trash, [new(OperationPreviewAction.AddCard, [], [card], false)], [])) with { OptionKey = "GRAB" });
            check(view!.GetNodeOrNull<Control>("Rt2NeowSummary") == null && view.SummaryDesiredSize == Vector2.Zero &&
                Descendants(view).OfType<NCard>().Count() == 1,
                "Trash Heap GRAB displays its native card face without a duplicate left summary");
            await Capture("trash-grab");

            var doll = Event("DOLL_ROOM");
            string[] candidates = ["WHETSTONE", "WAR_PAINT", "ANCHOR"];
            await Render(Preview(doll, new OperationPreviewSection(doll, candidates.Select(entry =>
                new OperationPreviewRow(OperationPreviewAction.Choose, [Relic(entry)], [], true)).ToImmutableArray(), [])));
            var choices = Descendants(view!).OfType<Label>().Where(label => label.Text.Contains("选择", StringComparison.Ordinal)).ToArray();
            check(choices.Length == candidates.Length && choices.Select(label => label.GetParent()).Distinct().Count() == candidates.Length &&
                choices.Select(label => label.GlobalPosition.Y).SequenceEqual(choices.Select(label => label.GlobalPosition.Y).Order()) &&
                choices.Select(label => label.GlobalPosition.Y).Distinct().Count() == candidates.Length &&
                candidates.All(entry => Descendants(view!).OfType<Label>().Count(label => label.Text == RelicName(entry)) == 1),
                "Doll Room offers one Choose row per candidate with no missing or merged identities");
            await Capture("doll-choose");

            var whetstone = Relic("WHETSTONE");
            await Render(Preview(courier,
                new OperationPreviewSection(courier, [new OperationPreviewRow(OperationPreviewAction.Restock, [whetstone], [], false) { Price = 146 }], []),
                new OperationPreviewSection(whetstone, [], []) { EmptyReason = "WhetstoneNoUpgradeTargets" }));
            var effect = view!.GetNode<Control>("Rt2NeowSummary/Rt2OperationEffectPanel_WHETSTONE");
            check(Descendants(effect).OfType<Label>().Any(label => label.Text == RelicName("WHETSTONE")) &&
                Descendants(effect).OfType<Label>().Any(label => label.Text == "无牌可升") &&
                view.GetNode<Control>("Rt2NeowSummary/Rt2OperationPrimaryPanel") != effect,
                "Whetstone empty result has its own titled effect panel below the Courier restock panel");
            await Capture("whetstone-empty");
            File.WriteAllText(Path.Combine(output, "operation-polish-display-result.json"), JsonSerializer.Serialize(new
            { passed = true, displayOnly = true, nativeRendererAndCardScenes = true, ownerRuntimeAcceptance = false, evidence },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { view?.Free(); }
    }
}
