using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    // Original native mouse callbacks and automatic controller processing. Read
    // each rendered frame before accepting a settled screenshot as evidence.
    private static async Task RunShopOrreryTemporalChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player,
        NMerchantRelic slot, MerchantRelicEntry entry, long entryId, Func<string> signature,
        Action<bool, string> check)
    {
        string evidence = Path.Combine(output, "shop-orrery-temporal");
        Directory.CreateDirectory(evidence);
        var frames = new List<object>(); var money = new List<object>(); var violations = new List<string>();
        var watch = Stopwatch.StartNew();
        NeowOperationHoverController? Controller() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        NeowOperationOverlay? View() => Controller()?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
        bool Painted(NeowOperationOverlay? view) => view is { Visible: true } && view.IsVisibleInTree() &&
            view.Modulate.A > .001f;
        OperationPreview? Preview() => View() is { } view ? PredictorOperationCapture.ReadNullable(view, "_preview") as OperationPreview : null;
        async Task Frame() => await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        void Move(Vector2 point) => viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        async Task Leave()
        {
            Move(new(4, viewport.Size.Y - 4));
            for (int frame = 0; frame < 3; frame++) await Frame();
            check(View() == null, "shop temporal native unfocus removes RT2 overlay before refocus");
        }
        void Observe(string phase, int frame)
        {
            var view = View(); var preview = Preview();
            var stacks = view == null ? [] : Descendants(view).OfType<Control>().Where(node =>
                node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
            var visibleCards = Descendants(game).OfType<NCard>().Where(card => card.IsVisibleInTree()).ToArray();
            var nativeTips = Descendants(hoverRoot).OfType<NHoverTipSet>().Where(tip => tip.IsVisibleInTree()).ToArray();
            bool shown = Painted(view);
            if (shown && preview is { Source.Entry: "ORRERY" } && !preview.Notices.Contains("Purchase:InsufficientGold"))
            {
                if (stacks.Length != 5 || stacks.Any(stack => !stack.GetMeta("rt2_stacked").AsBool() ||
                    Descendants(stack).OfType<NCard>().Count() != 3)) violations.Add(phase + "/" + frame + ": metadata/card counts not five stacks of three");
                foreach (var stack in stacks)
                {
                    var tiles = Descendants(stack).OfType<NCard>().Select(card => card.GetParent<Control>()).ToArray();
                    if (tiles.Length == 3 && (tiles.Select(tile => tile.Position.Y).Distinct().Count() != 3 ||
                        tiles.Select(tile => tile.Position.X).Distinct().Count() != 3))
                        violations.Add(phase + "/" + frame + ": stack native tile positions briefly flatten");
                }
            }
            frames.Add(new { phase, frame, milliseconds = watch.Elapsed.TotalMilliseconds, shown,
                treeVisible = view?.IsVisibleInTree(), opacity = view?.Modulate.A,
                previewSource = preview?.Source.Serialized, notices = preview?.Notices.ToArray(),
                summaryDesired = view?.SummaryDesiredSize.ToString(), cardsDesired = view?.CardsDesiredSize.ToString(),
                summaryBounds = view == null ? null : PredictorOperationCapture.ReadNullable(view, "_summaryBounds")?.ToString(),
                cardsBounds = view == null ? null : PredictorOperationCapture.ReadNullable(view, "_cardsBounds")?.ToString(),
                summaryRect = view?.GetNodeOrNull<Control>("Rt2NeowSummary")?.GetGlobalRect().ToString(),
                ownerRect = slot.GetGlobalRect().ToString(), hitboxRect = slot.Hitbox.GetGlobalRect().ToString(),
                viewId = view?.GetInstanceId(), nativeReuseCount = view?.NativeCardReuseCount,
                groups = stacks.Select(stack => new { name = stack.Name.ToString(), rect = stack.GetGlobalRect().ToString(),
                    visible = stack.IsVisibleInTree(), stacked = stack.GetMeta("rt2_stacked").AsBool(),
                    cards = Descendants(stack).OfType<NCard>().Select(card => new { id = card.GetInstanceId(),
                        key = card.Model?.Id.Entry, position = card.GlobalPosition.ToString(),
                        tilePosition = card.GetParent<Control>().Position.ToString(), tileScale = card.GetParent<Control>().Scale.ToString() }).ToArray() }).ToArray(),
                nativeTips = nativeTips.Select(tip => new { path = tip.GetPath().ToString(),
                    rect = tip.GetGlobalRect().ToString(), text = tip.GetNodeOrNull<Control>("textHoverTipContainer")?.GetGlobalRect().ToString(),
                    cards = tip.GetNodeOrNull<Control>("cardHoverTipContainer")?.GetGlobalRect().ToString() }).ToArray(),
                allVisibleNativeCards = visibleCards.Select(card => new { path = card.GetPath().ToString(), key = card.Model?.Id.Entry,
                    globalPosition = card.GlobalPosition.ToString(), globalScale = card.GetGlobalTransform().Scale.ToString() }).ToArray() });
        }
        async Task WaitFor(string phase, Func<OperationPreview, bool> predicate)
        {
            var limit = Stopwatch.StartNew();
            int frame = 0;
            while (limit.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Frame(); Observe(phase, ++frame);
                if (Painted(View()) && Preview() is { } preview && predicate(preview)) return;
            }
            check(false, "shop temporal " + phase + " automatic controller reaches expected current preview");
        }
        int savedGold = player.Gold;
        string original = signature();
        bool completed = false;
        string? failure = null;
        try
        {
            for (int round = 0; round < 4; round++)
            {
                await Leave(); Move(slot.Hitbox.GetGlobalRect().GetCenter());
                int published = 0, frame = 0;
                var visibleLayouts = new List<string>();
                var limit = Stopwatch.StartNew();
                while (published < 7 && limit.Elapsed < TimeSpan.FromSeconds(5))
                {
                    await Frame(); Observe("repeat-" + round, ++frame);
                    if (!Painted(View())) continue;
                    published++;
                    visibleLayouts.Add(string.Join(";", Descendants(View()!).OfType<Control>().Where(node =>
                        node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).Select(stack => stack.GetGlobalRect().ToString())));
                    if (published <= 3)
                        viewport.GetTexture().GetImage().SavePng(Path.Combine(evidence, $"repeat-{round}-visible-{published}.png"));
                }
                check(published == 7, "shop repeated native hover automatically publishes and retains Orrery result");
                if (visibleLayouts.Distinct().Count() > 1)
                    violations.Add("repeat-" + round + ": first rendered group layout changes after publication");
            }
            await Leave();
            player.Gold = 0;
            var snapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
            var shop = (PredictorOperationShopScene)snapshot.Scene;
            var rejected = PredictorOperation.Preview(snapshot, new BuyShopEntry(entryId, shop.Revision));
            var quote = rejected.ShopOffers.Single(offer => offer.EntryId == entryId);
            check(quote.Stocked && !quote.Affordable && quote.Cost > 0 && quote.Cost == entry.Cost,
                "shop gold checks use exact current quoted offer including Courier discount");
            foreach (int gold in new[] { 0, quote.Cost - 1, quote.Cost })
            {
                await Leave(); player.Gold = gold;
                string before = signature();
                Move(slot.Hitbox.GetGlobalRect().GetCenter());
                bool affordable = gold >= quote.Cost;
                await WaitFor("gold-" + gold, preview => affordable
                    ? preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).Count() == 5
                    : preview.Notices.Contains("Purchase:InsufficientGold"));
                var preview = Preview()!;
                check(affordable ? !preview.Notices.Contains("Purchase:InsufficientGold") :
                    Descendants(View()!).OfType<Label>().Any(label => label.Text == "金币不足") &&
                    !preview.Notices.Contains("Purchase:Invalid") && !Descendants(View()!).OfType<NCard>().Any(),
                    "shop current gold displays explicit insufficient-gold notice or all five affordable reward groups");
                check(signature() == before && ReferenceEquals(slot.Entry, entry), "shop gold hover does not purchase or mutate run/RNG/inventory");
                money.Add(new { gold, quotedCost = quote.Cost, nativeCost = entry.Cost, affordable,
                    notices = preview.Notices.ToArray(), groups = preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).Count() });
            }
            await Leave(); player.Gold = 0; Move(slot.Hitbox.GetGlobalRect().GetCenter());
            await WaitFor("live-gold-before", preview => preview.Notices.Contains("Purchase:InsufficientGold"));
            var beforeRecovery = View();
            player.Gold = quote.Cost;
            await WaitFor("live-gold-recovered", preview => preview.Branches.SelectMany(branch => branch.Sections)
                .SelectMany(section => section.CardGroups).Count() == 5 && !preview.Notices.Contains("Purchase:InsufficientGold"));
            check(!ReferenceEquals(View(), beforeRecovery), "shop gold recovery without leaving refreshes actual native-hover preview");
            money.Add(new { name = "live-gold-recovery-without-refocus", passed = true, quotedCost = quote.Cost });
            await Leave(); player.Gold = savedGold;
            check(signature() == original && ReferenceEquals(slot.Entry, entry), "shop temporal checks restore fixture gold and preserve original run/RNG/slot");
            check(violations.Count == 0, "every captured visible native Orrery frame retains the first five-by-three group geometry without a publication jump");
            completed = true;
        }
        catch (Exception error)
        {
            failure = error.ToString();
            throw;
        }
        finally
        {
            player.Gold = savedGold;
            if (Controller() is { } controller) controller.SetProcess(true);
            File.WriteAllText(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new { passed = completed,
                frames, money, violations, failure,
                limitations = "Isolated original merchant slot with actual native hover; no third-party overlays or full merchant Spine scene. FramePostDraw geometry/first-three-published PNGs capture rendered intervals; screenshot encoding can affect wall duration. No Owner runtime acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
