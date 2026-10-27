from hashlib import sha256
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
source = (ROOT / "Ui/Shell/AppShellHost.cs").read_text(encoding="utf-8")
in_run = (ROOT / "Ui/Shell/InRunPredictionLauncher.cs").read_text(encoding="utf-8")
project = ET.parse(ROOT / "RolltheSpire2.csproj").getroot()
png = (ROOT / "Assets/Icons/Rt2Launcher.png").read_bytes()
svg = (ROOT / "Assets/Icons/Rt2Launcher.svg").read_bytes()
initialize = source.split("public void Initialize(", 1)[1].split("private static void StyleMainMenuLauncher", 1)[0]
main_style = source.split("private static void StyleMainMenuLauncher", 1)[1].split("private static Texture2D? ResolveRt2LauncherIcon", 1)[0]
main_loader = source.split("private static Texture2D? ResolveRt2LauncherIcon", 1)[1].split("internal static void StyleNeowsBonesLauncher", 1)[0]
native_style = source.split("internal static void StyleNeowsBonesLauncher", 1)[1].split("private static Texture2D? ResolveStable107NeowsBonesLauncherIcon", 1)[0]
resource = "RolltheSpire2.Assets.Icons.Rt2Launcher.png"

checks = {
    "approvedPngIntegrity": sha256(png).hexdigest().upper() == "BC2A03B39A28F8CAB5369F23AE68A7948E4692F4D42F8BC414F6176A1C780D87",
    "approvedSvgSourceIntegrity": sha256(svg).hexdigest().upper() == "AEE11191ED0412FA4073FA0E75FFC754664D7C25C5DF5A52D921F940EA9DD2D4",
    "approvedSvgIsPreserved": ET.fromstring(svg).tag == "{http://www.w3.org/2000/svg}svg",
    "approvedPngIs512Rgba": (
        png[:8] == b"\x89PNG\r\n\x1a\n" and png[12:16] == b"IHDR"
        and struct.unpack(">II", png[16:24]) == (512, 512) and png[25] == 6
    ),
    "pngEmbeddedInDeployableAssembly": any(
        item.attrib.get("Include", "").replace("\\", "/") == "Assets/Icons/Rt2Launcher.png"
        and item.attrib.get("LogicalName") == resource for item in project.iter("EmbeddedResource")
    ),
    "mainMenuUsesApprovedIconOnly": (
        "StyleMainMenuLauncher(_launcher);" in initialize and "StyleNeowsBonesLauncher(_launcher" not in initialize
        and "ApplyStable107EntryButtonStyle(button);" in main_style
        and 'AddStable107LauncherContents(button, ResolveRt2LauncherIcon(), "RolltheSpire2_LauncherIcon")' in main_style
    ),
    "runtimeDecodesEmbeddedPng": (
        resource in source and "GetManifestResourceStream(Rt2LauncherIconResource)" in main_loader
        and "image.LoadPngFromBuffer" in main_loader and "ImageTexture.CreateFromImage(image)" in main_loader
        and "LoadSvg" not in main_loader and "outputs/" not in main_loader and "outputs\\" not in main_loader
        and "ResolveStable107NeowsBonesLauncherIcon(" not in main_loader
    ),
    "iconFailureRetainsOriginalCause": "RuntimeLog.WarnException(" in main_loader and "display=RT2-label" in main_loader,
    "inRunKeepsNativeBonesIcon": (
        "AppShellHostRoot.StyleNeowsBonesLauncher(entry, runtime);" in in_run
        and "ResolveStable107NeowsBonesLauncherIcon(snapshot)" in native_style
        and "ResolveRt2LauncherIcon" not in native_style
        and "BaseGameModelKeys.Relics.NeowsBones" in source
    ),
    "existingButtonStyleReused": all(token in source for token in (
        "ApplyStable107EntryButtonStyle", "CreateStable107EntryButtonBox",
        "CreateStable107EntryGlowBox", "RolltheSpire2_IconGlow",
    )),
    "launcherIconKeepsAspect": "TextureRect.StretchModeEnum.KeepAspectCentered" in source,
    "launcherIconInsetRemainsNine": all(token in source for token in (
        "icon.OffsetLeft = 9", "icon.OffsetTop = 9", "icon.OffsetRight = -9", "icon.OffsetBottom = -9",
    )),
    "launcherGeometryPreserved": all(token in initialize for token in (
        "Position = new Vector2(28, 96)", "Size = new Vector2(56, 56)",
        "CustomMinimumSize = new Vector2(56, 56)", "Position = new Vector2(68, 5)",
    )),
    "launcherClickPreserved": "_launcher.Pressed += ShowShell" in initialize,
    "launcherRemainsOwnedByMenuSurface": (
        "_launcher.Visible = visible;" in source and "_launcher.Disabled = !visible || _pushScheduled;" in source
        and "!_mainMenuSurfaceVisible" in source
        and "AppShellHost.SyncMainMenuSurfaceVisibility(_mainMenu);" in source
    ),
}

for name, passed in checks.items():
    print(f"{name}={str(passed).lower()}")

if not all(checks.values()):
    raise SystemExit(1)
