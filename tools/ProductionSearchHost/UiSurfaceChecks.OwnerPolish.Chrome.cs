using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    // Run inside the existing initialized native workspace and isolated saves.
    internal static async Task RunOwnerChromeChecksAsync(Node root, WorkspaceShell shell, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, flags)!.GetValue(owner)!;
        void Call(object owner, string name) => owner.GetType().GetMethod(name, flags)!.Invoke(owner, null);
        int checks = 0;
        var screenshots = new List<string>();
        void Check(bool ok, string reason)
        { checks++; if (!ok) throw new InvalidOperationException("OWNER_CHROME " + reason); }
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Ready()
        { await AwaitPageReady(root, shell); await Frames(); }
        async Task Shot(string name, Control? hover = null)
        {
            await Frames();
            if (hover is not null)
            {
                Vector2 position = hover.GetGlobalTransformWithCanvas() * (hover.Size / 2);
                root.GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
            }
            RenderingServer.ForceDraw();
            string path = Path.Combine(output, "owner-chrome-" + name + ".png");
            Check(root.GetViewport().GetTexture().GetImage().SavePng(path) == Error.Ok, "save " + name);
            screenshots.Add(path);
        }
        async Task Hover(Control control)
        {
            root.GetViewport().NotifyMouseEntered();
            // The host shell uses a logical canvas when its native window is smaller.
            Vector2 position = control.GetGlobalTransformWithCanvas() * (control.Size / 2);
            root.GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
            await Frames();
        }
        async Task Navigate(string field)
        { Field<Button>(shell, field).EmitSignal(Button.SignalName.Pressed); await Ready(); }
        IEnumerable<Node> Descendants(Node node)
        {
            foreach (Node child in node.GetChildren())
            { yield return child; foreach (Node nested in Descendants(child)) yield return nested; }
        }
        void StableMargins(Button button)
        {
            var normal = button.GetThemeStylebox("normal");
            foreach (string state in new[] { "hover", "pressed", "hover_pressed", "disabled" })
                foreach (Side side in Enum.GetValues<Side>())
                    Check(Math.Abs(normal.GetContentMargin(side) - button.GetThemeStylebox(state).GetContentMargin(side)) < .01f,
                        button.Name + " stable content margin " + state + "/" + side);
        }

        Directory.CreateDirectory(output);
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var store = Field<SearchWorkspace>(shell, "_persistence");
        var empty = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        var saved = store.Presets.SaveUserPreset("界面示例 · UI fixture", "", [], frame.CapturePreset(empty));
        foreach (string language in new[] { "zh", "en" })
        {
            MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(language == "zh" ? "zhs" : "eng");
            store.SetLanguageOverride(language);
            shell.GetType().GetField("_languageCode", flags)!.SetValue(shell, "");
            Call(shell, "RefreshLanguage");
            await Navigate("_search");
            frame.RestoreDraft(empty);
            frame.Refresh(language, JsonUiTextProvider.CreateUi13(language));
            await Frames();
            foreach (string name in new[] { "OpenPresets", "SavePreset" })
            {
                var action = frame.GetNode<Button>(name);
                Check(action.Position.Y + action.Size.Y <= frame.Size.Y + .01f &&
                    action.GetCombinedMinimumSize().Y <= action.Size.Y + .01f, "preset action fits canvas " + name);
                StableMargins(action);
            }
            await Hover(frame.GetNode<Button>("OpenPresets"));
            await Shot("preset-hover-" + language);

            var picker = frame.GetNode<OptionButton>("SearchCharacter");
            // Exercise the mod-portrait shape through the actual dropdown, without
            // loading a mod or changing the authored character selection.
            using var portraitImage = Image.CreateEmpty(30, 180, false, Image.Format.Rgba8);
            portraitImage.Fill(new Color("C7B07B"));
            using var portrait = ImageTexture.CreateFromImage(portraitImage);
            int probeIndex = picker.ItemCount;
            picker.AddIconItem(portrait, language == "zh" ? "长图限幅示例" : "Portrait bounds probe");
            picker.ShowPopup(); await Frames();
            var popup = picker.GetPopup();
            for (int i = 0; i < popup.ItemCount; i++)
            {
                var icon = popup.GetItemIcon(i);
                if (icon is null) continue;
                float width = Math.Min(icon.GetWidth(), popup.GetItemIconMaxWidth(i));
                Check(width <= 28 && width * icon.GetHeight() / icon.GetWidth() <= 28.01f,
                    "character dropdown bounds both dimensions " + i);
            }
            Check(popup.Size.Y <= picker.ItemCount * 48 + 24, "compact character row height");
            await Shot("characters-" + language);
            string popupPath = Path.Combine(output, "owner-chrome-characters-popup-" + language + ".png");
            Check(popup.GetTexture().GetImage().SavePng(popupPath) == Error.Ok, "save actual popup viewport");
            screenshots.Add(popupPath);
            popup.Hide(); picker.RemoveItem(probeIndex); await Frames();

            // Actual shared product tiles, in the current search canvas. The clear
            // callbacks below are fixture-local and never touch stored conditions.
            var probe = new Control { Position = new(520, 110), Size = new(300, 90) };
            frame.AddChild(probe);
            var icons = new ReflectionGameIconResolver("owner-polish-tiles");
            var names = RuntimeGameContentNameResolver.Create(language);
            int removed = 0;
            var tiles = new[]
            {
                new WorkspaceResultTile(RolltheSpire2.Ui.Theme.WorkspacePalette.MainPanel, icons, names,
                    JsonUiTextProvider.CreateUi13(language), new ModelKey("CARD", "BASH"), GameContentKind.Card,
                    () => { }, () => removed++, compact: true, compactWidth: 120),
                new WorkspaceResultTile(RolltheSpire2.Ui.Theme.WorkspacePalette.MainPanel, icons, names,
                    JsonUiTextProvider.CreateUi13(language), BaseGameModelKeys.Relics.GoldenPearl, GameContentKind.Relic,
                    () => { }, () => removed++, compact: true, compactWidth: 120)
            };
            for (int i = 0; i < tiles.Length; i++)
            {
                var tile = tiles[i]; tile.Position = new(i * 140, 0); probe.AddChild(tile); await Frames();
                Button clear = Descendants(tile).OfType<Button>().Single(b => b.Text == "×");
                Button body = tile.GetChildren().OfType<Button>().Single();
                // The standalone host injects native hover events while the OS
                // pointer may remain in another window. Keyboard focus is also
                // an authored visibility path for the same delete control.
                body.GrabFocus(); await Frames();
                await Hover(body); await Hover(clear);
                await Shot("result-clear-" + i + "-" + language, clear);
                Check(clear.IsVisibleInTree() && clear.IsHovered() && tile.GetGlobalRect().Encloses(clear.GetGlobalRect()) &&
                    clear.Size.X >= 24 && clear.Size.Y >= 24 && clear.GetParent<Control>().GetRect().Size == clear.Size,
                    $"complete clear button and hit area {i}: visible={clear.IsVisibleInTree()};tile={tile.GetGlobalRect()};clear={clear.GetGlobalRect()};host={clear.GetParent<Control>().GetRect()};mouse={tile.GetGlobalMousePosition()};hover={body.IsHovered()}");
                StableMargins(clear);
                clear.EmitSignal(Button.SignalName.Pressed);
            }
            Check(removed == 2, "card and relic clear callbacks retain behavior");
            probe.QueueFree(); await Frames();

            await Navigate("_seeds");
            var library = Field<SeedLibraryCanvas>(shell, "_seedLibrary");
            library.ShowSection(SeedLibraryCanvas.LibrarySection.Presets); await Ready();
            var entry = library.FindChildren("LibraryItem*", "Button", true, false).OfType<Button>()
                .Single(b => b.GetMeta("entry_id").AsString() == saved.Id);
            entry.EmitSignal(Button.SignalName.Pressed); await Ready();
            await Hover(entry);
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            {
                var box = (StyleBoxFlat)entry.GetThemeStylebox(state);
                Check(box.BorderWidthLeft == 2 && box.BorderWidthBottom == 0, "library left selection " + state);
            }
            await Shot("library-left-selection-" + language);

            await Navigate("_feedback");
            var feedback = Field<Control>(shell, "_feedbackPage");
            var heading = feedback.FindChild("FeedbackHeading", true, false) as Control;
            var steps = feedback.FindChild("FeedbackSteps", true, false) as Control;
            Check(heading is not null && steps is not null && heading.GetGlobalRect().End.Y < steps.GetGlobalRect().Position.Y &&
                heading.GetGlobalRect().Position.X == steps.GetGlobalRect().Position.X, "feedback title above steps at left");
            await Shot("feedback-" + language);

            await Navigate("_settings");
            var category = shell.FindChild("SettingsCategory0", true, false) as Button;
            category!.EmitSignal(Button.SignalName.Pressed); await Ready();
            StableMargins(category);
            Rect2 before = category.GetGlobalRect();
            await Hover(category);
            Check(category.ButtonPressed && category.GetGlobalRect() == before, "settings selected hover geometry stable");
            await Shot("settings-selected-hover-" + language);
            var general = (Button)shell.FindChild("SettingsCategory1", true, false);
            general.EmitSignal(Button.SignalName.Pressed); await Ready();
            await Hover(category);
            Check(!category.ButtonPressed && category.GetGlobalRect() == before, "settings deselected geometry stable");
            StableMargins(general);
            await Shot("settings-general-" + language);
        }
        await Navigate("_search");
        File.WriteAllText(Path.Combine(output, "owner-chrome-checks.json"), JsonSerializer.Serialize(new
        { Passed = checks, Items = new[] { 1, 7, 10, 12, 13, 14 }, Screenshots = screenshots }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"OWNER_CHROME_PASS checks={checks};items=1,7,10,12,13,14;languages=zh,en");
    }
}
