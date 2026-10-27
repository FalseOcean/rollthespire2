using System.Collections;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using RolltheSpire2.Ui.InRunConsole;

internal static partial class UiSurfaceChecks
{
    // Uses the real detached NMapScreen's IsOpen state and real overlay/context
    // methods. The underlying button is deliberately a map-input proxy: this
    // fixture does not initialize or render the game's full map scene.
    private static async Task RunConsoleMapPresenceChecks(Node root, InRunConsoleLauncher launcher,
        NOverlayStack stack, NMapScreen map, Action<bool, string> Check, Func<int, Task> Frames)
    {
        const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var mapOpen = typeof(NMapScreen).GetProperty(nameof(NMapScreen.IsOpen), members)!;
        bool oldMapOpen = map.IsOpen;
        var modalInstance = typeof(NModalContainer).GetProperty(nameof(NModalContainer.Instance),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldModal = NModalContainer.Instance;
        var modal = new NModalContainer();
        var modalScreen = new ConsoleMapPriorityOverlay();
        var modalOpen = typeof(NModalContainer).GetProperty(nameof(NModalContainer.OpenModal), members)!;
        var proxyLayer = new CanvasLayer { Name = "MapInputProxyLayer", Layer = 0 };
        var proxy = new Button
        {
            Name = "MapInputProxy_NotActualMapScene", Text = "Map input proxy (fixture only)",
            Position = Vector2.Zero, Size = root.GetViewport().GetVisibleRect().Size,
            MouseFilter = Control.MouseFilterEnum.Stop, FocusMode = Control.FocusModeEnum.None
        };
        int underlyingClicks = 0;
        proxy.Pressed += () => underlyingClicks++;
        proxyLayer.AddChild(proxy);
        root.AddChild(proxyLayer);
        var viewport = root.GetViewport();
        var inputStates = new List<object>();
        string inputStage = "fixture-start";
        void RecordInput(string stage, Vector2? point = null)
        {
            inputStage = stage;
            var hovered = viewport.GuiGetHoveredControl();
            var current = ActiveScreenContext.Instance.GetCurrentScreen();
            var top = stack.Peek();
            var retained = typeof(InRunConsoleLauncher).GetField("_surface", members)!.GetValue(launcher) as Node;
            bool validRetained = retained != null && GodotObject.IsInstanceValid(retained);
            inputStates.Add(new
            {
                stage, point = point?.ToString(), pointer = viewport.GetMousePosition().ToString(),
                hovered = hovered?.GetPath().ToString(), hoveredType = hovered?.GetType().Name,
                launcherVisible = launcher.IsVisibleInTree(), launcher.Disabled,
                launcherRect = launcher.GetGlobalRect().ToString(), launcherPressed = launcher.ButtonPressed,
                retainedConsole = validRetained ? retained!.GetPath().ToString() : retained == null ? "null" : "freed",
                stack.ScreenCount, top = top?.GetType().Name, current = current?.GetType().Name,
                map.IsOpen, underlyingClicks
            });
        }
        async Task Click(Func<Vector2> locate, string target)
        {
            // ProcessFrame resumes before control layout and deferred deletion
            // have settled. Sample the target only after a completed draw.
            await Frames(1);
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Vector2 point = locate();
            RecordInput(target + ":before-motion", point);
            // Keep one injected gesture synchronous. Yielding between its
            // motion/press/release lets unrelated desktop pointer events alter
            // this main Window's GUI hover state during the synthetic click.
            viewport.PushInput(new InputEventMouseMotion
            { Position = point, GlobalPosition = point, ButtonMask = 0 }, true);
            RecordInput(target + ":after-motion", point);
            viewport.PushInput(new InputEventMouseButton
            { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left,
                Pressed = true, ButtonMask = MouseButtonMask.Left }, true);
            RecordInput(target + ":after-press", point);
            viewport.PushInput(new InputEventMouseButton
            { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false, ButtonMask = 0 }, true);
            RecordInput(target + ":after-release", point);
            await Frames(2);
            RecordInput(target + ":settled", point);
        }
        InRunConsolePanel Panel(InRunConsoleOverlay screen) => Descendants(screen).OfType<InRunConsolePanel>().Single();
        CanvasLayer? Layer(Node node)
        {
            for (Node? current = node; current != null; current = current.GetParent())
                if (current is CanvasLayer layer) return layer;
            return null;
        }
        void Escape()
        {
            viewport.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true }, true);
            viewport.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = false }, true);
        }
        try
        {
            Check(stack.ScreenCount == 0, "map presence checks start with the native overlay stack empty");
            mapOpen.SetValue(map, false);
            InRunConsoleLauncher.ApplyPreferences(root.GetTree(), true, 50, 100);
            launcher._Process(1);
            await Frames(3);
            // Prove the synthetic recipient can receive real viewport mouse events
            // before using its zero count as evidence against clickthrough.
            Vector2 outside = viewport.GetVisibleRect().Size - new Vector2(30, 30);
            await Click(() => outside, "proxy-before-open");
            Check(underlyingClicks == 1, "native viewport mouse reaches the underlying map-input proxy before console opens");
            underlyingClicks = 0;
            await Click(() => launcher.GetGlobalRect().GetCenter(), "entry-before-map");
            Check(stack.Peek() is InRunConsoleOverlay && underlyingClicks == 0,
                "real mouse click opens the console entry above an overlapping lower-layer button");
            var overlay = (InRunConsoleOverlay)stack.Peek()!;
            Check(Layer(launcher) is { Layer: > 0 } && Layer(Panel(overlay)) is { Layer: > 0 },
                "entry and console content have explicit canvas layers above the native map-input proxy");
            // NMapScreen.Open emits Opened after setting IsOpen; the stock stack's
            // Opened subscription calls this exact native HideOverlays path.
            mapOpen.SetValue(map, true);
            stack.HideOverlays();
            await Frames(3);
            Check(map.IsOpen && overlay.IsVisibleInTree() && Panel(overlay).IsVisibleInTree(),
                "native map-open HideOverlays retains the top console and its rendered content");
            Check(ActiveScreenContext.Instance.IsCurrent(overlay),
                "the visible top console owns active input context over the open native map");
            var blocking = (IDictionary)typeof(NHotkeyManager).GetField("_blockingScreens", members)!
                .GetValue(NHotkeyManager.Instance)!;
            Check(blocking.Contains(overlay), "console over map retains native hotkey blocking");
            await Click(() => outside, "proxy-behind-console");
            Check(underlyingClicks == 0 && ReferenceEquals(stack.Peek(), overlay),
                "mouse outside console content is consumed without activating the underlying map-input proxy");
            await Click(() => Panel(overlay).CloseButton.GetGlobalRect().GetCenter(), "close-over-map");
            Check(stack.ScreenCount == 0 && underlyingClicks == 0 && map.IsOpen &&
                ActiveScreenContext.Instance.IsCurrent(map),
                "real close click works over map, does not click through, and restores native map context");
            launcher._Process(1);
            Check(launcher.IsVisibleInTree() && !launcher.Disabled,
                "console entry remains visible and enabled while native map is already open");
            await Click(() => launcher.GetGlobalRect().GetCenter(), "entry-map-open");
            Check(stack.Peek() is InRunConsoleOverlay && underlyingClicks == 0,
                "real entry click opens a console while native map is already open");
            overlay = (InRunConsoleOverlay)stack.Peek()!;
            Check(overlay.IsVisibleInTree() && ActiveScreenContext.Instance.IsCurrent(overlay),
                "native Push covered-by-map branch leaves the new console visible and current");
            Panel(overlay).CollapseContent();
            await Frames(2);
            Escape();
            await Frames(2);
            RecordInput("after-escape");
            Check(stack.ScreenCount == 0 && map.IsOpen && ActiveScreenContext.Instance.IsCurrent(map),
                "viewport Escape closes the console rail and restores map context without closing the native map");
            launcher._Process(1);
            await Click(() => launcher.GetGlobalRect().GetCenter(), "entry-after-escape");
            Check(stack.Peek() is InRunConsoleOverlay, "map entry can reopen after viewport Escape dismissal");
            overlay = (InRunConsoleOverlay)stack.Peek()!;
            var other = new ConsoleMapPriorityOverlay();
            stack.Push(other);
            await Frames(2);
            Check(!overlay.IsVisibleInTree() && !ActiveScreenContext.Instance.IsCurrent(overlay) &&
                ActiveScreenContext.Instance.IsCurrent(map),
                "a different top overlay does not promote the buried console above native map context");
            stack.HideOverlays();
            Check(!overlay.IsVisibleInTree() && !other.Visible,
                "native HideOverlays exception is limited to the top console, not unrelated overlays");
            stack.Remove(other);
            await Frames(2);
            Check(overlay.IsVisibleInTree() && ActiveScreenContext.Instance.IsCurrent(overlay),
                "removing the covering overlay restores console visibility and input context over map");
            // A real native modal container with a bounded screen-context sentinel
            // checks priority without starting a game selection or mutating RunState.
            modalOpen.SetValue(modal, modalScreen);
            modalInstance.SetValue(null, modal);
            Check(ActiveScreenContext.Instance.IsCurrent(modalScreen),
                "native modal context retains priority over console and map");
            Escape();
            await Frames(2);
            Check(ReferenceEquals(stack.Peek(), overlay) && !Panel(overlay).IsVisibleInTree() && !blocking.Contains(overlay),
                "native modal priority hides console content, releases its hotkey block, and preserves it on viewport Escape");
            modalOpen.SetValue(modal, null);
            modalInstance.SetValue(null, oldModal);
            await Frames(2);
            Check(ActiveScreenContext.Instance.IsCurrent(overlay) && Panel(overlay).IsVisibleInTree() && blocking.Contains(overlay),
                "clearing the modal restores top console visibility, input context and hotkey blocking");
            await Click(() => Panel(overlay).CloseButton.GetGlobalRect().GetCenter(), "close-after-modal");
            Check(stack.ScreenCount == 0 && ActiveScreenContext.Instance.IsCurrent(map),
                "closing the console after modal priority checks leaves the native map open and current");
            await Click(() => outside, "proxy-after-close");
            Check(underlyingClicks == 1,
                "underlying map-input proxy receives mouse again after console closes");
        }
        catch (Exception ex)
        {
            // Preserve the first failure and its actual hit-routing evidence;
            // no retry or direct Pressed signal substitutes for a failed click.
            try
            {
                RecordInput("failure-after:" + inputStage);
                var args = OS.GetCmdlineUserArgs();
                int outputAt = Array.IndexOf(args, "--output");
                string evidence = outputAt >= 0 && outputAt + 1 < args.Length
                    ? Path.GetFullPath(args[outputAt + 1]) : OS.GetUserDataDir();
                Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "console-map-presence-failure.json"),
                    JsonSerializer.Serialize(new { failure = ex.ToString(), inputStates }, new JsonSerializerOptions { WriteIndented = true }));
                using var image = viewport.GetTexture().GetImage();
                var saved = image.SavePng(Path.Combine(evidence, "console-map-presence-failure.png"));
                GD.Print($"CONSOLE_MAP_PRESENCE_FAILURE_EVIDENCE path={evidence};screenshot={saved}");
            }
            catch (Exception diagnostic) { GD.PrintErr("Console map failure evidence: " + diagnostic); }
            throw;
        }
        finally
        {
            modalInstance.SetValue(null, oldModal);
            if (stack.Peek() is InRunConsoleOverlay console) console.Close();
            mapOpen.SetValue(map, oldMapOpen);
            InRunConsoleLauncher.ApplyPreferences(root.GetTree(), false, 50, 100);
            proxyLayer.Free();
            modalScreen.Free();
            modal.Free();
            await Frames(2);
        }
    }
}

internal partial class ConsoleMapPriorityOverlay : Control, IOverlayScreen
{
    public NetScreenType ScreenType => NetScreenType.None;
    public bool UseSharedBackstop => false;
    public Control? DefaultFocusedControl => null;
    public void AfterOverlayOpened() { }
    public void AfterOverlayClosed() => QueueFree();
    public void AfterOverlayShown() => Show();
    public void AfterOverlayHidden() => Hide();
}
