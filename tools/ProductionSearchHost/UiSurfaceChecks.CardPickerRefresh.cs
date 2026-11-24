using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;

internal static partial class UiSurfaceChecks
{
    private static async Task RunCardPickerRefreshChecks(Node root, string output)
    {
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(NCard).Assembly);
        var names = RuntimeGameContentNameResolver.Create("en");
        var metadata = new Dictionary<ModelKey, CardPickerCandidateMetadata>();
        foreach (var character in ModelDb.AllCharacters)
        foreach (var card in character.CardPool.AllCards)
        {
            if (!Enum.TryParse<EffectCardRarity>(card.Rarity.ToString(), out var rarity) ||
                rarity is not (EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare) ||
                !Enum.TryParse<EffectCardType>(card.Type.ToString(), out var type)) continue;
            var key = new ModelKey(card.Id.Category, card.Id.Entry);
            var owner = new ModelKey(character.Id.Category, character.Id.Entry);
            metadata[key] = new(key, [owner], rarity, type);
        }
        var keys = metadata.Keys.ToArray();
        var context = new CardPickerContext(keys, metadata, true, "native-filter-refresh-regression");
        var host = new Control { Size = new(1600, 900) }; root.AddChild(host);
        var picker = new ObjectPickerPanel(); host.AddChild(picker);
        var evidence = new List<object>(); var failures = new List<string>(); int checks = 0;
        void Require(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException("CardPickerRefresh: " + why); }
        async Task Frames(int n = 5) { for (int i = 0; i < n; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Receipt(bool passed) => File.WriteAllText(Path.Combine(output, "card-picker-refresh-result.json"),
            JsonSerializer.Serialize(new { passed, checks, failures, evidence }, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            var request = new ObjectPickerRequest(keys, _ => { }, names, JsonUiTextProvider.CreateUi13("en"), new ReflectionGameIconResolver("picker-refresh-check"), _ => "", context);
            picker.Open(request);
            await Frames();
            var grid = Descendants(picker).OfType<GridContainer>().Single();
            var scroll = grid.GetParent().GetParent<ScrollContainer>();
            var search = Descendants(picker).OfType<LineEdit>().Single();
            async Task Observe(string stage, bool expectMatches = true)
            {
                await Frames();
                var tiles = grid.GetChildren().OfType<Button>().ToArray();
                var visible = tiles.Where(tile => scroll.GetGlobalRect().Intersects(tile.GetGlobalRect()) && tile.Size.X > 1).ToArray();
                var missing = visible.Where(tile => !Descendants(tile).OfType<NCard>().Any(card => card.IsVisibleInTree() && card.Model != null)).ToArray();
                int loaded = Descendants(grid).OfType<NCard>().Count();
                int nearby = tiles.Count(tile => scroll.GetGlobalRect().Grow(80).Intersects(tile.GetGlobalRect()));
                evidence.Add(new { stage, matches = tiles.Length, scroll = scroll.ScrollVertical, visible = visible.Length,
                    loaded, nearby,
                    missing = missing.Select(tile => new { tile.AccessibilityName, rect = tile.GetGlobalRect().ToString() }).ToArray() });
                Receipt(false);
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                root.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "card-picker-refresh-" + stage + ".png"));
                checks++;
                if (!(expectMatches ? tiles.Length > 0 && visible.Length > 0 && missing.Length == 0 : tiles.Length == 0))
                    failures.Add(stage + ": every on-screen matching tile has its real native card face; missing=" + missing.Length);
                if (loaded > nearby) failures.Add(stage + ": only settled nearby tiles load native card scenes; loaded=" + loaded + ";nearby=" + nearby);
                Receipt(false);
            }
            async Task PressFilter(string title)
            {
                var button = Descendants(picker).OfType<Button>().Single(candidate => candidate.GetChildren().OfType<Label>().Any(label => label.Text == title));
                button.EmitSignal(BaseButton.SignalName.Pressed); await Frames();
            }
            await Observe("initial");
            scroll.ScrollVertical = 1; await Observe("initial-after-small-scroll");
            scroll.ScrollVertical = 100000; await Observe("all-scrolled");
            await PressFilter("Rare"); await Observe("rare-from-bottom");
            scroll.ScrollVertical = 100000; await Observe("rare-scrolled");
            int retainedMiddle = -1;
            foreach (var entry in new[] { "IRONCLAD", "SILENT", "REGENT", "NECROBINDER" })
            {
                var character = ModelDb.AllCharacters.Single(model => model.Id.Entry == entry);
                await PressFilter(names.Resolve(new(character.Id.Category, character.Id.Entry), GameContentKind.Character));
                await Observe("rare-" + entry.ToLowerInvariant());
                if (entry == "IRONCLAD")
                {
                    int middle = (int)scroll.Size.Y + 25;
                    Require(scroll.GetVScrollBar().MaxValue - scroll.GetVScrollBar().Page > middle,
                        "Rare character fixture has a valid offset more than one viewport below its top");
                    retainedMiddle = middle;
                    scroll.ScrollVertical = middle; await Observe("rare-ironclad-middle");
                }
                else Require(scroll.ScrollVertical == retainedMiddle,
                    "Changing between equal-sized character results preserves the existing valid middle scroll offset");
            }
            search.Text = "__no_matching_picker_card__"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
            await Observe("empty", false);
            search.Text = ""; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
            await Observe("restored");
            await PressFilter("Common"); await Observe("common-restored");
            scroll.ScrollVertical = 100000; await Observe("common-scrolled");
            await PressFilter("Uncommon"); await Observe("uncommon-from-bottom");
            picker.ClosePicker(); await Frames(); picker.Open(request);
            scroll = Descendants(picker).OfType<GridContainer>().Single().GetParent().GetParent<ScrollContainer>();
            grid = Descendants(picker).OfType<GridContainer>().Single();
            await Observe("reopened");
            scroll.ScrollVertical = (int)scroll.Size.Y + 25; await Observe("reopened-middle");
            Receipt(false);
            Require(failures.Count == 0, string.Join(";", failures));
            Receipt(true);
            GD.Print($"CARD_PICKER_REFRESH_PASS checks={checks}");
        }
        finally { picker.ClosePicker(); host.Free(); }
    }
}
