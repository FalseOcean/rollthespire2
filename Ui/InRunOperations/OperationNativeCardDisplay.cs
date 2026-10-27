using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunOperations;

// Main-thread presentation only. Models come exclusively from detached snapshots;
// the returned native card ignores input so its owner can provide the interaction.
internal static class OperationNativeCardDisplay
{
    private static IUiTextProvider UiText => JsonUiTextProvider.CreateUi13(UiLanguagePreferences.LanguageCode);

    internal static float ScaleForViewport(Vector2 viewport) =>
        .6f * Math.Min(viewport.X / 1920f, viewport.Y / 1080f);

    // Native frame is centred at the origin; reserve room for its protruding cost gem.
    internal static Control Create(PredictorCard snapshot, float scale,
        Action<Control>? onNativeReady = null, Action<double>? onReadyMeasured = null,
        string diagnosticPrefix = "operationNativeCard")
    {
        var tile = new Control
        {
            CustomMinimumSize = new Vector2(350, 470) * scale,
            Size = new Vector2(350, 470) * scale,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        try
        {
            var model = OperationPreviewCardModel.Create(snapshot);
            var card = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
            card.Model = model;
            card.Position = new Vector2(180, 240) * scale;
            card.Scale = Vector2.One * scale;
            card.Ready += OnReady;
            void OnReady()
            {
                // Reused cards must not retain their preceding owner's callbacks.
                card.Ready -= OnReady;
                var readyWatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                    card.GetNode<Control>("%Highlight").Hide();
                    IgnoreInput(card);
                    onNativeReady?.Invoke(tile);
                }
                catch (Exception exception)
                {
                    RuntimeLog.WarnException($"{diagnosticPrefix}ReadyFailed;card={snapshot.Key.Serialized}", exception);
                    card.Hide();
                    AddCardTextFallback(tile, snapshot);
                }
                finally { onReadyMeasured?.Invoke(readyWatch.Elapsed.TotalMilliseconds); }
            }
            tile.AddChild(card);
        }
        catch (Exception exception)
        {
            RuntimeLog.WarnException($"{diagnosticPrefix}Failed;card={snapshot.Key.Serialized}", exception);
            AddCardTextFallback(tile, snapshot);
        }
        return tile;
    }

    private static void AddCardTextFallback(Control tile, PredictorCard snapshot)
    {
        string attributes = (snapshot.IncreasedDamage != 0 ? "\n" + UiText.Get("operation.damage.change") + $"{snapshot.IncreasedDamage:+#;-#;0}" : "") +
            (snapshot.IncreasedBlock != 0 ? "\n" + UiText.Get("operation.block.change") + $"{snapshot.IncreasedBlock:+#;-#;0}" : "") +
            (snapshot.PermanentCostOverride is { } cost ? "\n" + UiText.Get("operation.cost") + cost : "");
        var keywords = new List<string>();
        if (snapshot.HasEternalKeyword) keywords.Add(UiText.Get("operation.eternal"));
        if (snapshot.HasExhaustKeyword) keywords.Add(UiText.Get("operation.exhaust"));
        if (snapshot.HasInnateKeyword) keywords.Add(UiText.Get("operation.innate"));
        if (snapshot.HasRetainKeyword) keywords.Add(UiText.Get("operation.retain"));
        if (keywords.Count > 0) attributes += "\n" + string.Join(" · ", keywords);
        if (snapshot.RoomsEntered != 0) attributes += "\n" + UiText.Get("operation.rooms.entered") + snapshot.RoomsEntered;
        if (snapshot.CombatsSeen != 0) attributes += "\n" + UiText.Get("operation.combats.seen") + snapshot.CombatsSeen;
        if (snapshot.TinkerTimeRider != PredictorMadScienceRider.None || snapshot.SpoilsActIndex >= 0 || snapshot.SpoilsCoord != null ||
            (snapshot.Key.Entry == "MAD_SCIENCE" && snapshot.TinkerTimeType != EffectCardType.Skill))
            attributes += "\n" + UiText.Get("operation.card.recordOnlyProperties");
        var label = new Label
        {
            Text = CardName(snapshot) + attributes + "\n" + UiText.Get("operation.card.artwork.unavailable"),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Size = tile.CustomMinimumSize
        };
        label.AddThemeFontOverride("font", MainPanelTheme.Regular);
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", new Color("ece9df"));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .65f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        tile.AddChild(label);
    }

    private static string CardName(PredictorCard card) => NameOf(card.Key) +
        (card.UpgradeLevel > 0 ? "+" + (card.UpgradeLevel > 1 ? card.UpgradeLevel.ToString() : "") : "") +
        (card.Enchantment is { } enchantment ? " · " + NameOf(enchantment) + " " + card.EnchantmentAmount : "");

    private static string NameOf(ModelKey key) => string.IsNullOrEmpty(key.Entry)
        ? UiText.Get("operation.operation.result")
        : new LocString(key.Category == "ENCHANTMENT" ? "enchantments" : "cards", key.Entry + ".title").GetFormattedText();

    private static void IgnoreInput(Node node)
    {
        node.SetProcessInput(false);
        node.SetProcessUnhandledInput(false);
        node.SetProcessUnhandledKeyInput(false);
        if (node is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
            control.FocusMode = Control.FocusModeEnum.None;
        }
        foreach (Node child in node.GetChildren()) IgnoreInput(child);
    }
}
