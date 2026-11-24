using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.SaveStatus;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Tests.FastSearch;

internal static class SaveStatusVersionChecks
{
    internal static int Run()
    {
        var baseline = NativeWorkbenchCheckEnvironment.Initialize("SaveStatusVersion");
        var scene = ((SceneTree)Engine.GetMainLoop()).CurrentScene;
        const string stable = RuntimeVersionCompatibility.SupportedStableVersion;
        const string beta = RuntimeVersionCompatibility.SupportedBetaVersion;
        var cases = new[]
        {
            (Game: stable, LauncherEn: $"Adapted for {stable} (stable)", LauncherZh: $"适配 {stable}（正式版）",
                RulesEn: $"Adapted through {stable}", RulesZh: $"适配至 {stable}"),
            (Game: "0.111.0", LauncherEn: $"Adapted for {stable} (stable)", LauncherZh: $"适配 {stable}（正式版）",
                RulesEn: $"Adapted through {stable} (older game version)", RulesZh: $"适配至 {stable}（当前游戏为旧版）"),
            (Game: beta, LauncherEn: $"Adapted for {beta} (beta)", LauncherZh: $"适配 {beta}（测试版）",
                RulesEn: $"Adapted through {beta}", RulesZh: $"适配至 {beta}"),
            (Game: "0.114.0", LauncherEn: $"Adapted for {beta} (beta)",
                LauncherZh: $"适配 {beta}（测试版）",
                RulesEn: $"Reusing rules adapted through {beta} (unverified)", RulesZh: $"沿用适配至 {beta} 的规则（未验证）")
        };
        var surface = new Control { Size = new Vector2(1600, 900) };
        scene.AddChild(surface);
        int checks = 0;
        try
        {
            var preferences = SearchWorkspace.Open(OS.GetUserDataDir());
            foreach (var fixture in cases)
            {
                var detection = new GameVersionDetection(fixture.Game, fixture.Game, string.Empty,
                    "SaveStatusVersionFixture", true, string.Empty);
                var compatibility = RuntimeProfileRegistry.Resolve(detection);
                var runtime = baseline with
                {
                    Detection = detection,
                    Profile = RuntimeProfileRegistry.Select(compatibility),
                    Compatibility = compatibility
                };
                var page = new SaveStatusPage(runtime);
                surface.AddChild(page);
                page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                try
                {
                    foreach (string language in new[] { "en", "zh" })
                    {
                        preferences.SetLanguageOverride(language);
                        var launcher = new AppShellHostRoot();
                        surface.AddChild(launcher);
                        try
                        {
                            launcher.Initialize(runtime);
                            var label = (Label)launcher.FindChild("RolltheSpire2_LauncherVersion", owned: false);
                            string[] lines = label.Text.Split('\n');
                            if (lines.Length != 2 || !lines[0].StartsWith("RT2 v", StringComparison.Ordinal) ||
                                lines[1] != (language == "zh" ? fixture.LauncherZh : fixture.LauncherEn))
                                throw new InvalidOperationException($"Launcher version ({fixture.Game}/{language}): {label.Text}");
                            checks++;
                        }
                        finally { launcher.Free(); }
                        page.ApplyLocalization(JsonUiTextProvider.Create(language),
                            RuntimeGameContentNameResolver.Create(language));
                        var rows = page.FindChildren("*", nameof(HBoxContainer), owned: false).OfType<HBoxContainer>()
                            .Where(row => row.IsVisibleInTree() && row.GetChildCount() == 2 &&
                                row.GetChild(0) is Label && row.GetChild(1) is Label).ToArray();
                        void CheckRow(string key, string expected)
                        {
                            string[] values = rows.Where(row => row.GetChild<Label>(0).Text == key)
                                .Select(row => row.GetChild<Label>(1).Text).ToArray();
                            if (values.Length != 1 || values[0] != expected)
                                throw new InvalidOperationException(
                                    $"SaveStatusVersionUi {fixture.Game}/{language}: '{key}' expected '{expected}', " +
                                    $"got [{string.Join(", ", values)}].");
                            checks++;
                        }

                        // Exact values also reject an internal family name or the original reference patch.
                        if (language == "en")
                        {
                            CheckRow("Game version", fixture.Game);
                            CheckRow("RT2 supports", $"{stable} (stable) / {beta} (beta)");
                            CheckRow("Active prediction rules", fixture.RulesEn);
                        }
                        else
                        {
                            CheckRow("游戏版本", fixture.Game);
                            CheckRow("RT2 适配版本", $"{stable}（正式版）/ {beta}（测试版）");
                            CheckRow("当前预测规则", fixture.RulesZh);
                        }
                        Console.WriteLine($"SAVE_STATUS_VERSION_UI_CASE game={fixture.Game};language={language};" +
                            $"rules={(language == "en" ? fixture.RulesEn : fixture.RulesZh)}");
                    }
                }
                finally
                {
                    page.Free();
                }
            }
            checks += CheckLauncherInput(scene, baseline);
            checks += CheckLauncherToggle(scene, baseline);
        }
        finally
        {
            surface.Free();
        }
        Console.WriteLine($"SAVE_STATUS_VERSION_UI_PASS checks={checks}");
        return 0;
    }

    private static int _openCalls;
    private static void RecordOpenRequest() => _openCalls++;

    // New hosts usually share a live store. Drop the fixture's own weak cache
    // entry so reopening must read position and visibility from disk.
    private static void ForgetFixtureWorkspace()
    {
        var stores = (Dictionary<string, WeakReference<SearchWorkspace>>)AccessTools
            .Field(typeof(SearchWorkspace), "OpenStores").GetValue(null)!;
        stores.Remove(Path.GetFullPath(OS.GetUserDataDir()));
    }

    private static int CheckLauncherInput(Node scene, ModRuntimeSnapshot baseline)
    {
        var viewport = new SubViewport { Size = new(960, 540), HandleInputLocally = true };
        scene.AddChild(viewport);
        var open = AccessTools.Method(typeof(AppShellHostRoot), nameof(AppShellHostRoot.ShowShell));
        var observer = new Harmony("rt2.tests.menu-launcher-input");
        _openCalls = 0;
        observer.Patch(open, prefix: new HarmonyMethod(typeof(SaveStatusVersionChecks), nameof(RecordOpenRequest)));
        int checks = 0;
        void Require(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException("Launcher input: " + what);
            checks++;
        }

        (AppShellHostRoot Host, Button Button, Label Label) Attach(ModRuntimeSnapshot runtime)
        {
            var host = new AppShellHostRoot { Size = viewport.Size };
            viewport.AddChild(host);
            host.Initialize(runtime);
            var label = (Label)host.FindChild("RolltheSpire2_LauncherVersion", owned: false);
            return (host, (Button)label.GetParent(), label);
        }

        void Move(Vector2 point, Vector2 delta, bool held)
        {
            viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point,
                Relative = delta, ButtonMask = held ? MouseButtonMask.Left : 0 }, true);
        }

        void Left(Vector2 point, bool pressed)
        {
            viewport.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
                Pressed = pressed, ButtonIndex = MouseButton.Left, ButtonMask = pressed ? MouseButtonMask.Left : 0 }, true);
        }

        try
        {
            var (host, button, label) = Attach(baseline);
            Vector2 start = button.Position;
            Vector2 point = button.GetGlobalRect().GetCenter();
            Move(point, Vector2.Zero, false);
            Left(point, true);
            Left(point, false);
            Require(_openCalls == 1 && button.Position == start, "ordinary click must activate once");

            var jitter = new Vector2(3, 2);
            Left(point, true);
            Move(point + jitter, jitter, true);
            Left(point + jitter, false);
            Require(_openCalls == 2 && button.Position == start, "small pointer jitter should still click");

            Move(point, -jitter, false);
            Left(point, true);
            // A repeated check can start at the previous run's lower-right drop.
            var delta = new Vector2(start.X < viewport.Size.X / 2 ? 120 : -120,
                start.Y < viewport.Size.Y / 2 ? 70 : -70);
            Move(point + delta, delta, true);
            Left(point + delta, false);
            Require(_openCalls == 2 && button.Position.IsEqualApprox(start + delta),
                $"drag must move without activation: opens={_openCalls}; position={button.Position}; start={start}");
            Left(point + delta, true);
            Left(point + delta, false);
            Require(_openCalls == 3, "the first click after a drag should activate");
            Vector2 saved = button.Position;
            var disk = SearchWorkspace.ReadFeaturePreferences(OS.GetUserDataDir());
            Require(disk.MainMenuLauncherX == saved.X && disk.MainMenuLauncherY == saved.Y, "dropped position on disk");

            host.Free();
            ForgetFixtureWorkspace();
            var restored = Attach(baseline);
            Require(restored.Button.Position.IsEqualApprox(saved), "recreated launcher position");
            var origin = restored.Button.GetGlobalRect().GetCenter();
            Vector2 edge = new(viewport.Size.X - 1, viewport.Size.Y - 1);
            Move(origin, Vector2.Zero, false);
            Left(origin, true);
            Move(edge, edge - origin, true);
            Left(edge, false);
            Vector2 extent = restored.Button.GetGlobalRect().End.Max(restored.Label.GetGlobalRect().End);
            Require(_openCalls == 3 && extent.IsEqualApprox(viewport.Size),
                "icon and version label should fit at bottom-right");

            restored.Host.Free();
            var missing = Attach(ModRuntimeSnapshot.NotInitialized);
            Require(!missing.Label.Text.Contains('\n') && missing.Label.Text.StartsWith("RT2 v", StringComparison.Ordinal),
                "missing detected version should show only RT2");
        }
        finally
        {
            observer.Unpatch(open, HarmonyPatchType.Prefix, observer.Id);
            viewport.Free();
        }
        return checks;
    }

    private static int CheckLauncherToggle(Node scene, ModRuntimeSnapshot baseline)
    {
        var viewport = new SubViewport { Size = new(960, 540), HandleInputLocally = true };
        scene.AddChild(viewport);
        var menu = new LauncherMenuFixture();
        var stack = new NMainMenuSubmenuStack();
        var notes = new NPatchNotesScreen();
        var submenu = new LauncherSubmenuFixture();
        var modal = new NModalContainer();
        var oldModal = NModalContainer.Instance;
        int checks = 0;

        void Require(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException("Launcher visibility: " + what);
            checks++;
        }

        (AppShellHostRoot Host, Button Button) Attach()
        {
            AppShellHost.EnsureAttached(menu, baseline);
            var host = menu.GetNode<AppShellHostRoot>("RolltheSpire2_WorkspaceLauncherRoot");
            var label = (Label)host.FindChild("RolltheSpire2_LauncherVersion", owned: false);
            return (host, (Button)label.GetParent());
        }

        void Send(Key code = Key.F7, bool pressed = true, bool ctrl = true, bool echo = false,
            bool alt = false, bool shift = false, bool meta = false)
        {
            using var key = new InputEventKey
            {
                Keycode = code, PhysicalKeycode = code, Pressed = pressed, CtrlPressed = ctrl,
                Echo = echo, ShiftPressed = shift, AltPressed = alt, MetaPressed = meta
            };
            viewport.PushInput(key, true);
        }

        bool SavedHidden() => SearchWorkspace.ReadFeaturePreferences(OS.GetUserDataDir()).MainMenuLauncherHidden;
        const Key original = Key.F7 | (Key)KeyModifierMask.MaskCtrl;
        const Key rebound = Key.F6 | (Key)KeyModifierMask.MaskShift;
        Key SavedKey() => SearchWorkspace.ReadFeaturePreferences(OS.GetUserDataDir()).MainMenuLauncherShortcut;
        void Activate(Button button)
        {
            button.GrabFocus();
            Send(Key.Space, ctrl: false);
            Send(Key.Space, pressed: false, ctrl: false);
        }
        (WorkspaceShell Shell, Button Shortcut, Button Reset) Settings()
        {
            var shell = new WorkspaceShell { Size = new(1600, 900) };
            shell.InitializeWithPersistence(baseline, OS.GetUserDataDir());
            menu.AddChild(shell);
            shell.Show();
            // Use the synchronous navigation path in this single-frame check.
            // The graphical page preparation pipeline has separate coverage.
            var prepared = AccessTools.Field(typeof(WorkspaceShell), "_applyingPreparedPage");
            prepared.SetValue(shell, true);
            var settingsButton = (Button)AccessTools.Field(typeof(WorkspaceShell), "_settings").GetValue(shell)!;
            try
            {
                settingsButton.EmitSignal(Button.SignalName.Pressed);
                var general = (Button)shell.FindChild("SettingsCategory1", owned: false);
                general.EmitSignal(Button.SignalName.Pressed);
            }
            finally { prepared.SetValue(shell, false); }
            var change = (Button)shell.FindChild("SettingsLauncherShortcut", owned: false);
            var reset = (Button)shell.FindChild("SettingsLauncherShortcutReset", owned: false);
            Require(change.IsVisibleInTree() && reset.IsVisibleInTree(), "settings/general exposes shortcut controls");
            return (shell, change, reset);
        }
        try
        {
            AccessTools.Property(typeof(NMainMenu), nameof(NMainMenu.SubmenuStack)).SetValue(menu, stack);
            AccessTools.Property(typeof(NMainMenu), nameof(NMainMenu.PatchNotesScreen)).SetValue(menu, notes);
            viewport.AddChild(menu);
            menu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var entry = Attach();
            Require(entry.Button.IsVisibleInTree() && !SavedHidden(), "initially available");
            Send(pressed: false);
            Require(entry.Button.Visible, "key release");
            Send(echo: true);
            Require(entry.Button.Visible, "key repeat");
            Send(ctrl: false);
            Require(entry.Button.Visible, "missing modifier");
            Send(alt: true);
            Require(entry.Button.Visible, "additional Alt");
            Send(shift: true);
            Require(entry.Button.Visible, "additional Shift");
            Send(meta: true);
            Require(entry.Button.Visible, "additional Meta");

            Send();
            Require(!entry.Button.IsVisibleInTree() && SavedHidden(), "first press should hide and persist");
            entry.Host.Free();
            ForgetFixtureWorkspace();
            entry = Attach();
            Require(!entry.Button.IsVisibleInTree() && SavedHidden(), "restored hidden after host recreation");
            Send(echo: true);
            Require(SavedHidden(), "holding the shortcut after hiding");
            Send(pressed: false);
            Require(SavedHidden(), "releasing the shortcut after hiding");

            menu.Hide();
            Send();
            Require(SavedHidden(), "inactive main menu");
            menu.Show();
            stack.Push(submenu);
            Send();
            Require(SavedHidden(), "native submenu stack has an open screen");
            stack.Pop();
            var notesOpen = AccessTools.Property(typeof(NPatchNotesScreen), nameof(NPatchNotesScreen.IsOpen));
            notesOpen.SetValue(notes, true);
            Send();
            Require(SavedHidden(), "patch notes open");
            notesOpen.SetValue(notes, false);
            var text = new LineEdit();
            menu.AddChild(text);
            text.GrabFocus();
            Require(viewport.GuiGetFocusOwner() == text, "input field has native GUI focus");
            Send();
            Require(SavedHidden(), "typing in a focused input");
            text.Free();
            AccessTools.Property(typeof(NModalContainer), nameof(NModalContainer.Instance)).SetValue(null, modal);
            AccessTools.Property(typeof(NModalContainer), nameof(NModalContainer.OpenModal)).SetValue(modal, submenu);
            Send();
            Require(SavedHidden(), "modal open");
            AccessTools.Property(typeof(NModalContainer), nameof(NModalContainer.Instance)).SetValue(null, oldModal);

            Send();
            Require(entry.Button.IsVisibleInTree() && !SavedHidden(), "same shortcut restores the hidden launcher");
            entry.Host.Free();
            ForgetFixtureWorkspace();
            entry = Attach();
            Require(entry.Button.IsVisibleInTree(), "restored visible after host recreation");

            var settings = Settings();
            string keyLabel = OS.GetKeycodeString(original);
            Require(SavedKey() == original && settings.Shortcut.Text.Contains(keyLabel, StringComparison.Ordinal),
                "default Ctrl+F7 displayed in general settings");
            Activate(settings.Shortcut);
            Require(!settings.Shortcut.Text.Contains(keyLabel, StringComparison.Ordinal), "recording prompt");
            Send(Key.Escape, ctrl: false);
            Require(SavedKey() == original && settings.Shortcut.Text.Contains(keyLabel, StringComparison.Ordinal),
                "Escape cancels recording and retains the displayed shortcut");
            Activate(settings.Shortcut);
            Send(Key.F6, ctrl: false, shift: true);
            Require(SavedKey() == rebound &&
                settings.Shortcut.Text.Contains(OS.GetKeycodeString(rebound), StringComparison.Ordinal),
                "captured Shift+F6 on disk and in settings");
            settings.Shell.Free();
            Send();
            Require(entry.Button.IsVisibleInTree() && !SavedHidden(), "old binding no longer toggles");
            Send(Key.F6, ctrl: false, shift: true);
            Require(!entry.Button.IsVisibleInTree() && SavedHidden(), "replacement applies to the existing host");
            entry.Host.Free();
            ForgetFixtureWorkspace();
            entry = Attach();
            Send(Key.F6, ctrl: false, shift: true);
            Require(entry.Button.IsVisibleInTree() && !SavedHidden(), "replacement restores after reloading from disk");
            settings = Settings();
            Activate(settings.Reset);
            Require(SavedKey() == original && settings.Shortcut.Text.Contains(keyLabel, StringComparison.Ordinal),
                "reset restores the default shortcut and its label");
            settings.Shell.Free();
            Send(Key.F6, ctrl: false, shift: true);
            Require(entry.Button.IsVisibleInTree() && !SavedHidden(), "replacement no longer acts after reset");
            Send();
            Require(!entry.Button.IsVisibleInTree() && SavedHidden(), "default immediately acts after reset");
            Send();
            Require(entry.Button.IsVisibleInTree() && !SavedHidden(), "default restores after reset");
        }
        finally
        {
            AccessTools.Property(typeof(NModalContainer), nameof(NModalContainer.Instance)).SetValue(null, oldModal);
            viewport.Free();
            stack.Free();
            notes.Free();
            submenu.Free();
            modal.Free();
        }
        return checks;
    }
}

internal partial class LauncherMenuFixture : NMainMenu
{
    public override void _Ready() { }
}

internal partial class LauncherSubmenuFixture : NSubmenu
{
    protected override Control? InitialFocusedControl => null;
}
