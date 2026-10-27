using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal static class AppShellHost
{
    private const string RootName = "RolltheSpire2_WorkspaceLauncherRoot";

    public static void EnsureAttached(NMainMenu mainMenu, ModRuntimeSnapshot snapshot)
    {
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        if (mainMenu.GetNodeOrNull<Node>(new NodePath(RootName)) is AppShellHostRoot existing)
        {
            existing.BindMainMenu(mainMenu);
            SyncMainMenuSurfaceVisibility(mainMenu);
            return;
        }

        var host = new AppShellHostRoot { Name = RootName };
        host.Initialize(snapshot);
        host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        mainMenu.AddChild(host);
        host.BindMainMenu(mainMenu);
        SyncMainMenuSurfaceVisibility(mainMenu);
        RuntimeLog.Info("workspaceShellHostAttached=true;surface=QueryWorkbenchFrame;paletteCanonical=BlueInk;topLevelOwnership=NSubmenuStack");
    }

    public static void SyncMainMenuSurfaceVisibility(NMainMenu mainMenu)
    {
        if (mainMenu.GetNodeOrNull<Node>(new NodePath(RootName)) is not AppShellHostRoot host)
        {
            return;
        }

        bool submenuOpen = mainMenu.SubmenuStack.SubmenusOpen;
        bool patchNotesOpen = mainMenu.PatchNotesScreen.IsOpen;
        var lobbyScreen = mainMenu.SubmenuStack.Peek();
        if (lobbyScreen is not null && !LobbyUnlockReadout.IsCustomMultiplayerLobby(lobbyScreen)) lobbyScreen = null;
        host.SetLauncherParent(lobbyScreen);
        bool mainMenuRootVisible = (!submenuOpen || lobbyScreen is not null) && !patchNotesOpen;
        string reason = lobbyScreen is not null ? "custom-multiplayer-lobby" : submenuOpen
            ? "submenu-open"
            : patchNotesOpen
                ? "patch-notes-open"
                : "main-menu-root";
        host.SetMainMenuSurfaceVisible(mainMenuRootVisible, reason);
    }
}

internal sealed partial class AppShellHostRoot : Control
{
    private const string Rt2LauncherIconResource = "RolltheSpire2.Assets.Icons.Rt2Launcher.png";
    private const string Stable107NeowsBonesIconPath = "res://images/atlases/relic_atlas.sprites/neowsbones.tres";

    private ModRuntimeSnapshot _snapshot = ModRuntimeSnapshot.NotInitialized;
    private Button? _launcher;
    private NMainMenu? _mainMenu;
    private Rt2TopLevelSubmenu? _topLevelSurface;
    private bool _pushScheduled;
    private bool _mainMenuSurfaceVisible = true;
    private bool? _lastLoggedSurfaceVisible;
    private bool _patchNotesVisibilityHooked;
    private NSubmenu? _entrySubmenu;
    private double _lobbyEntryPoll;
    private SearchWorkspace? _menuWorkspace;
    private MenuWorkspacePreparation? _menuPreparation;
    private bool _preparationFailed;
    private long _lastMenuInput = System.Diagnostics.Stopwatch.GetTimestamp();
    internal object? MenuPreparationStatus => _menuPreparation?.Status;

    public override void _Input(InputEvent input)
    {
        _lastMenuInput = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public override void _ExitTree()
    {
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.ProfileIdChanged -= HandleProfileChanged;
        _menuPreparation?.Dispose(); _menuPreparation = null; _menuWorkspace = null;
    }

    private void HandleProfileChanged(int profile)
    {
        _menuPreparation?.Dispose(); _menuPreparation = null;
        _preparationFailed = false;
        _lastMenuInput = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private void AdvanceMenuPreparation()
    {
        if (!_mainMenuSurfaceVisible || _pushScheduled || _preparationFailed ||
            _snapshot.Profile.ProfileId != RuntimeProfileId.Beta111 ||
            _mainMenu is null || !GodotObject.IsInstanceValid(_mainMenu) || !_mainMenu.IsVisibleInTree() ||
            NModalContainer.Instance?.OpenModal is not null ||
            System.Diagnostics.Stopwatch.GetElapsedTime(_lastMenuInput).TotalMilliseconds < 350) return;
        // StackModified can be delivered after this process turn. Read the native
        // top before working instead of granting a slice from cached visibility.
        var currentSubmenu = _mainMenu.SubmenuStack.Peek();
        if (currentSubmenu is not null && !LobbyUnlockReadout.IsCustomMultiplayerLobby(currentSubmenu)) return;
        try
        {
            _menuPreparation ??= new(_snapshot, _menuWorkspace!);
            _menuPreparation.Advance(TimeSpan.FromMilliseconds(3));
        }
        catch (Exception ex)
        {
            RecordPreparationFailure(ex, "IdleObservation");
            _preparationFailed = true;
            _menuPreparation?.Dispose(); _menuPreparation = null;
        }
    }

    private static void RecordPreparationFailure(Exception ex, string stage)
    {
        RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
            "MenuPreparationFailed", nameof(AppShellHostRoot), stage, "issues.prediction_failed");
        RuntimeLog.WarnException($"menuPreparationFailed=true;stage={stage};freshEntryStillAvailable=true", ex);
    }

    public override void _Process(double delta)
    {
        AdvanceMenuPreparation();
        // Lobby initialization/connect can finish after StackModified, so refresh availability as well.
        _lobbyEntryPoll -= delta;
        if (_lobbyEntryPoll > 0 || _mainMenu is null || !GodotObject.IsInstanceValid(_mainMenu)) return;
        _lobbyEntryPoll = .25;
        AppShellHost.SyncMainMenuSurfaceVisibility(_mainMenu);
    }

    public void SetLauncherParent(Control? lobby)
    {
        if (_launcher is null) return;
        Node parent = lobby ?? this;
        if (_launcher.GetParent() != parent)
        {
            _launcher.Reparent(parent, false);
            _launcher.Position = new Vector2(28, 96);
        }
    }

    public void BindMainMenu(NMainMenu mainMenu)
    {
        if (ReferenceEquals(_mainMenu, mainMenu) && _patchNotesVisibilityHooked)
        {
            return;
        }

        _mainMenu = mainMenu;
        if (!_patchNotesVisibilityHooked)
        {
            mainMenu.PatchNotesScreen.Connect(
                CanvasItem.SignalName.VisibilityChanged,
                Callable.From(HandlePatchNotesVisibilityChanged));
            _patchNotesVisibilityHooked = true;
        }
    }

    private void HandlePatchNotesVisibilityChanged()
    {
        if (_mainMenu is null || !GodotObject.IsInstanceValid(_mainMenu))
        {
            return;
        }

        AppShellHost.SyncMainMenuSurfaceVisibility(_mainMenu);
    }

    public void Initialize(ModRuntimeSnapshot snapshot)
    {
        _snapshot = snapshot;
        _menuWorkspace = SearchWorkspace.Open(OS.GetUserDataDir());
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.ProfileIdChanged += HandleProfileChanged;
        var preferences = _menuWorkspace.Preferences;
        string language = preferences.LanguageOverride is "zh" or "en"
            ? preferences.LanguageOverride
            : TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        IUiTextProvider text = JsonUiTextProvider.CreateUi13(language);
        MouseFilter = MouseFilterEnum.Ignore;
        // This node owns only the main-menu launcher. Stay in the menu's base
        // draw order so the game's later ModalContainer backstop covers it.
        // The actual RT2 surface is separately parented to SubmenuStack.
        ZIndex = 0;
        _launcher = new Button
        {
            Text = string.Empty,
            Position = new Vector2(28, 96),
            Size = new Vector2(56, 56),
            CustomMinimumSize = new Vector2(56, 56),
            TooltipText = text.Get("shell.open"),
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.None,
            ClipContents = false,
            ZIndex = 0
        };
        StyleMainMenuLauncher(_launcher);
        var versionLabel = new Label
        {
            Name = "RolltheSpire2_LauncherVersion",
            Text = $"RT2 v{typeof(ModRuntime).Assembly.GetName().Version?.ToString(3)}\ncompat {RuntimeVersionCompatibility.Beta111Version}",
            Position = new Vector2(68, 5),
            MouseFilter = MouseFilterEnum.Ignore
        };
        versionLabel.AddThemeFontSizeOverride("font_size", 16);
        versionLabel.AddThemeColorOverride("font_color", new Color(0.94f, 0.94f, 0.90f));
        versionLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        versionLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        versionLabel.AddThemeConstantOverride("shadow_offset_y", 2);
        // Inherit launcher visibility when another main-menu surface opens.
        _launcher.AddChild(versionLabel);
        _launcher.Pressed += ShowShell;
        AddChild(_launcher);
        RuntimeLog.Info("ui1AppShellAutoOpenSuppressed=true");
    }

    private static void StyleMainMenuLauncher(Button button)
    {
        ApplyStable107EntryButtonStyle(button);
        AddStable107LauncherContents(button, ResolveRt2LauncherIcon(), "RolltheSpire2_LauncherIcon");
    }

    private static Texture2D? ResolveRt2LauncherIcon()
    {
        try
        {
            using Stream stream = typeof(AppShellHostRoot).Assembly.GetManifestResourceStream(Rt2LauncherIconResource)
                ?? throw new InvalidOperationException($"Embedded launcher icon is missing: {Rt2LauncherIconResource}");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            using var image = new Image();
            // The approved SVG uses masks/filters; its verified PNG preserves
            // the accepted art without depending on Godot's SVG renderer.
            Error error = image.LoadPngFromBuffer(buffer.ToArray());
            if (error != Error.Ok)
                throw new InvalidOperationException($"Embedded launcher icon could not decode: {Rt2LauncherIconResource};error={error}");
            Texture2D texture = ImageTexture.CreateFromImage(image);
            RuntimeLog.Info($"mainMenuLauncherIconResolved=Rt2Launcher;missing=false;evidence=embedded-png:{Rt2LauncherIconResource}");
            return texture;
        }
        catch (Exception ex)
        {
            RuntimeLog.WarnException($"mainMenuLauncherIconResolved=Rt2Launcher;missing=true;resource={Rt2LauncherIconResource};display=RT2-label", ex);
            return null;
        }
    }

    // The in-run prediction entry still uses native Neow's Bones art.
    internal static void StyleNeowsBonesLauncher(Button button, ModRuntimeSnapshot snapshot)
    {
        ApplyStable107EntryButtonStyle(button);
        AddStable107LauncherContents(button, ResolveStable107NeowsBonesLauncherIcon(snapshot));
    }

    private static Texture2D? ResolveStable107NeowsBonesLauncherIcon(ModRuntimeSnapshot snapshot)
    {
        try
        {
            // Match the accepted Stable107 donor order: ask the active game's
            // ModelDb-backed icon authority first. The historical resource path
            // still resolves in Beta110, but it resolves to Godot's red NOPE
            // placeholder rather than the relic art.
            var resolver = new ReflectionGameIconResolver(
                $"{snapshot.Profile.ProfileId}:{snapshot.Detection.DisplayVersion}:main-menu-launcher-primary");
            IconDescriptor descriptor = resolver.Resolve(
                BaseGameModelKeys.Relics.NeowsBones,
                GameContentKind.Relic,
                IconVariant.RelicLarge);
            if (!descriptor.IsMissing &&
                descriptor.Texture is { } runtimeTexture &&
                IsUsableLauncherTexture(runtimeTexture))
            {
                RuntimeLog.Info(
                    $"mainMenuLauncherIconResolved=NeowsBones;missing=false;evidence=runtime-model-primary:{descriptor.EvidenceCode};textureType={runtimeTexture.GetType().Name}");
                return runtimeTexture;
            }

            // Keep the exact 0.107 donor path only as a Stable107 fallback. Never
            // use it for Beta110, where the path is a misleading placeholder.
            if (snapshot.Profile.ProfileId == RuntimeProfileId.Stable107)
            {
                Texture2D? donorTexture = ResourceLoader.Load<Texture2D>(
                    Stable107NeowsBonesIconPath,
                    string.Empty,
                    ResourceLoader.CacheMode.Reuse);
                if (donorTexture is not null && IsUsableLauncherTexture(donorTexture))
                {
                    RuntimeLog.Info(
                        $"mainMenuLauncherIconResolved=NeowsBones;missing=false;evidence=stable107-resource-fallback:{Stable107NeowsBonesIconPath};textureType={donorTexture.GetType().Name}");
                    return donorTexture;
                }

                RuntimeLog.Info(
                    $"mainMenuLauncherLegacyResourceRejected=true;path={Stable107NeowsBonesIconPath};textureType={donorTexture?.GetType().Name ?? "null"}");
            }

            RuntimeLog.Info(
                $"mainMenuLauncherIconResolved=NeowsBones;missing=true;evidence=runtime-model-missing:{descriptor.EvidenceCode};profile={snapshot.Profile.ProfileId}");
        }
        catch (Exception ex)
        {
            RuntimeLog.Info($"mainMenuLauncherIconResolved=NeowsBones;missing=true;evidence={ex.GetType().Name}");
        }

        return null;
    }

    private static bool IsUsableLauncherTexture(Texture2D texture)
    {
        if (!GodotObject.IsInstanceValid(texture))
        {
            return false;
        }

        return !texture.GetType().Name.Contains("Placeholder", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddStable107LauncherContents(Button launcher, Texture2D? iconTexture,
        string iconNodeName = "RolltheSpire2_NeowsBonesLauncherIcon")
    {
        if (iconTexture is null)
        {
            var fallback = new Label
            {
                Name = "RolltheSpire2_LauncherFallbackLabel",
                Text = "RT2",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            fallback.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            fallback.AddThemeColorOverride("font_color", new Color(0.98f, 0.91f, 0.62f, 1f));
            fallback.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.74f));
            fallback.AddThemeConstantOverride("shadow_offset_x", 2);
            fallback.AddThemeConstantOverride("shadow_offset_y", 2);
            fallback.AddThemeFontSizeOverride("font_size", 16);
            launcher.AddChild(fallback);
            return;
        }

        var glow = new Panel
        {
            Name = "RolltheSpire2_IconGlow",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        glow.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        glow.OffsetLeft = 7;
        glow.OffsetTop = 7;
        glow.OffsetRight = -7;
        glow.OffsetBottom = -7;
        glow.AddThemeStyleboxOverride("panel", CreateStable107EntryGlowBox());
        launcher.AddChild(glow);

        var icon = new TextureRect
        {
            Name = iconNodeName,
            Texture = iconTexture,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        icon.OffsetLeft = 9;
        icon.OffsetTop = 9;
        icon.OffsetRight = -9;
        icon.OffsetBottom = -9;
        launcher.AddChild(icon);
    }

    private static void ApplyStable107EntryButtonStyle(Button button)
    {
        button.AddThemeStyleboxOverride(
            "normal",
            CreateStable107EntryButtonBox(
                new Color(0.015f, 0.022f, 0.045f, 0.28f),
                new Color(0.78f, 0.54f, 0.18f, 0.50f)));
        button.AddThemeStyleboxOverride(
            "hover",
            CreateStable107EntryButtonBox(
                new Color(0.04f, 0.08f, 0.13f, 0.44f),
                new Color(0.43f, 0.91f, 0.95f, 0.92f)));
        button.AddThemeStyleboxOverride(
            "pressed",
            CreateStable107EntryButtonBox(
                new Color(0.01f, 0.03f, 0.05f, 0.55f),
                new Color(0.92f, 0.26f, 0.32f, 0.86f)));
        button.AddThemeStyleboxOverride(
            "disabled",
            CreateStable107EntryButtonBox(
                new Color(0.02f, 0.02f, 0.03f, 0.22f),
                new Color(0.36f, 0.34f, 0.30f, 0.30f)));
        button.AddThemeStyleboxOverride(
            "focus",
            CreateStable107EntryButtonBox(
                new Color(0f, 0f, 0f, 0f),
                new Color(0.43f, 0.91f, 0.95f, 0.86f)));
        button.AddThemeColorOverride("font_color", new Color(0f, 0f, 0f, 0f));
        button.AddThemeColorOverride("font_hover_color", new Color(0f, 0f, 0f, 0f));
        button.AddThemeColorOverride("font_pressed_color", new Color(0f, 0f, 0f, 0f));
    }

    private static StyleBoxFlat CreateStable107EntryButtonBox(Color background, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ShadowColor = new Color(0f, 0f, 0f, 0.48f),
            ShadowSize = 5,
            ContentMarginLeft = 4f,
            ContentMarginTop = 4f,
            ContentMarginRight = 4f,
            ContentMarginBottom = 4f
        };
    }

    private static StyleBoxFlat CreateStable107EntryGlowBox()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.20f, 0.75f, 0.86f, 0.10f),
            BorderColor = new Color(0.88f, 0.58f, 0.20f, 0.24f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14
        };
    }


    public void SetMainMenuSurfaceVisible(bool visible, string reason)
    {
        _mainMenuSurfaceVisible = visible;
        Visible = visible;

        if (_launcher is not null)
        {
            _launcher.Visible = visible;
            _launcher.Disabled = !visible || _pushScheduled;
        }

        if (_lastLoggedSurfaceVisible != visible)
        {
            _lastLoggedSurfaceVisible = visible;
            RuntimeLog.Info($"mainMenuLauncherSurfaceVisible={visible.ToString().ToLowerInvariant()};reason={reason}");
        }
    }

    public void ShowShell()
    {
        if (_mainMenu is null ||
            !GodotObject.IsInstanceValid(_mainMenu) ||
            !_mainMenuSurfaceVisible ||
            NModalContainer.Instance?.OpenModal is not null ||
            _pushScheduled)
        {
            return;
        }

        NMainMenuSubmenuStack stack = _mainMenu.SubmenuStack;
        if (stack.SubmenusOpen && !(stack.Peek() is { } lobby && LobbyUnlockReadout.IsCustomMultiplayerLobby(lobby)))
        {
            AppShellHost.SyncMainMenuSurfaceVisibility(_mainMenu);
            return;
        }

        long entryStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        bool reusedSurface = _topLevelSurface is not null && GodotObject.IsInstanceValid(_topLevelSurface);
        MenuWorkspaceEntryInputs? prepared = null;
        if (_snapshot.Profile.ProfileId == RuntimeProfileId.Beta111)
        {
            try
            {
                _menuPreparation ??= new(_snapshot, _menuWorkspace!);
                prepared = _menuPreparation.CompleteForEntry();
            }
            catch (Exception ex)
            {
                RecordPreparationFailure(ex, "EntryValidation");
                _menuPreparation?.Dispose(); _menuPreparation = null; _preparationFailed = true;
            }
        }
        // Refresh the shared runtime authority at the actual RT2 entry boundary.
        // The same menu can survive progress changes. Prepared inputs were just
        // fully reobserved; other profiles and failed preparation use the existing
        // fresh main-thread boundary without gating the surface on diagnostics.
        try
        {
            if (prepared is null) RuntimeAuthorityEnvironment.CaptureOnMainThread(_snapshot);
        }
        catch (Exception ex)
        {
            RuntimeLog.WarnException($"runtimeAuthorityRefreshFailed=true;source=rt2-launcher;failSoft=true;issue={ex.GetType().Name}:{ex.Message}", ex);
        }

        long authorityFinished = System.Diagnostics.Stopwatch.GetTimestamp();
        Rt2TopLevelSubmenu surface = EnsureTopLevelSurface(stack, prepared);
        long surfaceFinished = System.Diagnostics.Stopwatch.GetTimestamp();
        RuntimeLog.Detail(FormattableString.Invariant($"panelOpenTiming=true;kind=main-menu;reusedSurface={reusedSurface};authorityMs={System.Diagnostics.Stopwatch.GetElapsedTime(entryStarted, authorityFinished).TotalMilliseconds:F3};surfaceMs={System.Diagnostics.Stopwatch.GetElapsedTime(authorityFinished, surfaceFinished).TotalMilliseconds:F3};phase=before-push"));
        if (ReferenceEquals(stack.Peek(), surface))
        {
            return;
        }

        _pushScheduled = true;
        _entrySubmenu = stack.Peek();
        if (_launcher is not null)
        {
            _launcher.Visible = false;
            _launcher.Disabled = true;
        }

        // A0 source audit established that NMainMenu._Ready postfix runs after
        // SubmenuStack initialization, but a dynamically AddChild'ed custom
        // NSubmenu's own Godot _Ready timing is an engine lifecycle fact. Defer
        // Push by one turn so the new surface can enter the SceneTree first.
        Callable.From(PushTopLevelDeferred).CallDeferred();
    }

    private Rt2TopLevelSubmenu EnsureTopLevelSurface(NMainMenuSubmenuStack stack, MenuWorkspaceEntryInputs? prepared)
    {
        if (_topLevelSurface is not null && GodotObject.IsInstanceValid(_topLevelSurface))
        {
            _topLevelSurface.ApplyPreparedEntry(prepared);
            return _topLevelSurface;
        }

        var surface = new Rt2TopLevelSubmenu();
        surface.Initialize(_snapshot, prepared);
        surface.TopLevelClosed += HandleTopLevelClosed;
        stack.AddChild(surface);
        _topLevelSurface = surface;
        RuntimeLog.Info("rt2TopLevelSurfaceAttached=true;parent=NMainMenuSubmenuStack;deferredPush=true");
        return surface;
    }

    private void PushTopLevelDeferred()
    {
        _pushScheduled = false;
        if (_mainMenu is null ||
            !GodotObject.IsInstanceValid(_mainMenu) ||
            _topLevelSurface is null ||
            !GodotObject.IsInstanceValid(_topLevelSurface))
        {
            return;
        }

        NMainMenuSubmenuStack stack = _mainMenu.SubmenuStack;
        if (!ReferenceEquals(_topLevelSurface.GetParent(), stack))
        {
            RuntimeLog.Error("rt2TopLevelPushRejected=true;reason=surface-parent-mismatch");
            AppShellHost.SyncMainMenuSurfaceVisibility(_mainMenu);
            return;
        }

        if (ReferenceEquals(stack.Peek(), _topLevelSurface))
        {
            return;
        }

        if (!ReferenceEquals(stack.Peek(), _entrySubmenu))
        {
            RuntimeLog.Info("rt2TopLevelPushRejected=true;reason=another-submenu-became-active");
            AppShellHost.SyncMainMenuSurfaceVisibility(_mainMenu);
            return;
        }

        stack.Push(_topLevelSurface);
        RuntimeLog.Info("rt2TopLevelPush=true;source=main-menu-bone-dice;typedStack=true");
    }

    private void HandleTopLevelClosed()
    {
        // NSubmenuStack.Pop emits/restores the remaining game surface after
        // OnSubmenuClosed returns. Defer launcher synchronization until that
        // Pop lifecycle has completed.
        Callable.From(SyncLauncherAfterTopLevelClose).CallDeferred();
    }

    private void SyncLauncherAfterTopLevelClose()
    {
        if (_mainMenu is null || !GodotObject.IsInstanceValid(_mainMenu))
        {
            return;
        }

        AppShellHost.SyncMainMenuSurfaceVisibility(_mainMenu);
    }
}
