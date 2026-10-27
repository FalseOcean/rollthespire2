using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Shell;

internal static partial class UiSurfaceChecks
{
    // Diagnostic rendering fixture for H-LOGO-01, not a full game screenshot.
    private static async Task RunLauncherIconChecks(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const string resource = "RolltheSpire2.Assets.Icons.Rt2Launcher.png";
        const string acceptedHash = "BC2A03B39A28F8CAB5369F23AE68A7948E4692F4D42F8BC414F6176A1C780D87";
        const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
        int checks = 0;
        void Check(bool condition, string reason)
        { checks++; if (!condition) throw new InvalidOperationException("LauncherIconUi: " + reason); }
        using Stream embedded = typeof(AppShellHostRoot).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("LauncherIconUi: missing embedded resource " + resource);
        using var bytes = new MemoryStream();
        embedded.CopyTo(bytes);
        byte[] approvedPng = bytes.ToArray();
        string actualHash = Convert.ToHexString(SHA256.HashData(approvedPng));
        Check(actualHash == acceptedHash, "embedded PNG is the exact approved asset");
        using var approved = new Image();
        Check(approved.LoadPngFromBuffer(approvedPng) == Error.Ok, "approved PNG decodes in Godot");
        Check(approved.GetWidth() == 512 && approved.GetHeight() == 512, "approved image is 512 square");
        Check(new[] { new Vector2I(0, 0), new Vector2I(511, 0), new Vector2I(0, 511), new Vector2I(511, 511) }
            .All(point => approved.GetPixel(point.X, point.Y).A == 0), "approved corners retain transparency");
        approved.Convert(Image.Format.Rgba8);
        byte[] approvedPixels = approved.GetData();
        Check(Enumerable.Range(0, 512 * 512).Any(index => approvedPixels[index * 4 + 3] > 0),
            "approved image contains visible art");

        var viewport = new SubViewport { Size = new(900, 420), Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        root.AddChild(viewport);
        try
        {
            viewport.AddChild(new ColorRect { Color = new("111927"), Size = new(900, 420) });
            void Caption(string text, Vector2 position, int fontSize = 18)
            {
                var label = new Label { Text = text, Position = position };
                label.AddThemeFontSizeOverride("font_size", fontSize);
                viewport.AddChild(label);
            }
            Caption("Native diagnostic fixture - not a full game screenshot", new(24, 20), 22);
            Caption("Main menu: 56px", new(24, 80));
            Caption("In-run: native art", new(190, 80));
            Caption("Approved art enlarged to 256px (diagnostic only)", new(390, 80), 16);
            Caption("Normal scale / 38px icon region / existing button style", new(24, 340), 17);
            var launcher = new Button { Name = "DiagnosticMainMenuLauncher", Text = "",
                Position = new(48, 120), Size = new(56, 56), CustomMinimumSize = new(56, 56),
                FocusMode = Control.FocusModeEnum.None, ClipContents = false };
            typeof(AppShellHostRoot).GetMethod("StyleMainMenuLauncher", methods)!.Invoke(null, [launcher]);
            viewport.AddChild(launcher);
            var inRun = new Button { Name = "DiagnosticInRunLauncher", Text = "",
                Position = new(210, 120), Size = new(56, 56), CustomMinimumSize = new(56, 56),
                FocusMode = Control.FocusModeEnum.None, ClipContents = false };
            typeof(AppShellHostRoot).GetMethod("StyleNeowsBonesLauncher", methods)!.Invoke(null, [inRun, runtime]);
            viewport.AddChild(inRun);
            Check(launcher.GetNodeOrNull<Label>("RolltheSpire2_LauncherFallbackLabel") is null,
                "main menu rendered art without the text fallback");
            Check(inRun.GetNodeOrNull<Label>("RolltheSpire2_LauncherFallbackLabel") is null,
                "in-run native art rendered without the text fallback");
            var icon = launcher.GetNode<TextureRect>("RolltheSpire2_LauncherIcon");
            var nativeIcon = inRun.GetNode<TextureRect>("RolltheSpire2_NeowsBonesLauncherIcon");
            Check(icon.Texture is ImageTexture && icon.Texture.GetWidth() == 512 && icon.Texture.GetHeight() == 512,
                "main-menu texture comes from the embedded decoded image");
            using (Image rendered = icon.Texture!.GetImage())
            {
                rendered.Convert(Image.Format.Rgba8);
                Check(rendered.GetData().AsSpan().SequenceEqual(approvedPixels),
                    "actual main-menu texture preserves every decoded PNG pixel including alpha");
            }
            Texture2D expectedNative = ModelDb.Relic<NeowsBones>().Icon;
            Check(nativeIcon.Texture is not null && nativeIcon.Texture.GetInstanceId() == expectedNative.GetInstanceId(),
                "in-run styling still uses actual ModelDb NeowsBones.Icon");
            Check(nativeIcon.Texture != icon.Texture, "in-run art was not replaced by the main-menu asset");
            var enlarged = new TextureRect { Texture = icon.Texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Position = new(430, 110), Size = new(256, 256), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
            viewport.AddChild(enlarged);
            for (int frame = 0; frame < 4; frame++)
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(enlarged.Size == new Vector2(256, 256), "diagnostic enlarged art stays within its declared bounds");
            foreach (var pair in new[] { (Button: launcher, Icon: icon), (Button: inRun, Icon: nativeIcon) })
            {
                Check(pair.Button.Size == new Vector2(56, 56) && pair.Button.CustomMinimumSize == new Vector2(56, 56),
                    "actual launcher button stays 56 square: " + pair.Button.Name);
                Check(pair.Icon.OffsetLeft == 9 && pair.Icon.OffsetTop == 9 && pair.Icon.OffsetRight == -9 && pair.Icon.OffsetBottom == -9,
                    "actual icon retains 9px insets: " + pair.Button.Name);
                Check(pair.Icon.Size == new Vector2(38, 38) && pair.Icon.Position == new Vector2(9, 9),
                    "actual icon drawing region is 38 square: " + pair.Button.Name);
                Check(pair.Icon.StretchMode == TextureRect.StretchModeEnum.KeepAspectCentered &&
                    pair.Icon.ExpandMode == TextureRect.ExpandModeEnum.IgnoreSize,
                    "actual icon preserves aspect and ignores source minimum size: " + pair.Button.Name);
                Check(pair.Button.Scale == Vector2.One && pair.Icon.Scale == Vector2.One,
                    "fixture renders actual button and icon at normal scale: " + pair.Button.Name);
                Check(pair.Button.GetNodeOrNull<Panel>("RolltheSpire2_IconGlow") is not null,
                    "existing glow is preserved: " + pair.Button.Name);
            }
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            const string screenshot = "launcher-icon-native-fixture.png";
            using (Image image = viewport.GetTexture().GetImage())
                Check(image.SavePng(Path.Combine(output, screenshot)) == Error.Ok, "native diagnostic fixture screenshot saved");
            File.WriteAllText(Path.Combine(output, "launcher-icon.json"), JsonSerializer.Serialize(new
            {
                Passed = checks, Resource = resource, Sha256 = actualHash, Width = 512, Height = 512,
                ButtonSize = 56, IconRegion = 38, Inset = 9, NativeRelicIconPreserved = true,
                Screenshot = screenshot, ScreenshotScope = "Native diagnostic fixture; not a full game screenshot"
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"LAUNCHER_ICON_PASS checks={checks};asset={actualHash};button=56;icon=38;inRun=NeowsBones;fixtureOnly=true");
        }
        finally { viewport.QueueFree(); }
    }
}
