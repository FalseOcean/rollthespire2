using Godot;
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
            (Game: stable, RulesEn: $"Adapted through {stable}", RulesZh: $"适配至 {stable}"),
            (Game: beta, RulesEn: $"Adapted through {beta}", RulesZh: $"适配至 {beta}"),
            (Game: "0.114.0", RulesEn: $"Reusing rules adapted through {beta} (unverified)",
                RulesZh: $"沿用适配至 {beta} 的规则（未验证）")
        };
        var surface = new Control { Size = new Vector2(1600, 900) };
        scene.AddChild(surface);
        int checks = 0;
        try
        {
            var preferences = SearchWorkspace.Open(OS.GetUserDataDir());
            foreach (string language in new[] { "en", "zh" })
            {
                preferences.SetLanguageOverride(language);
                var launcher = new AppShellHostRoot();
                surface.AddChild(launcher);
                try
                {
                    launcher.Initialize(baseline);
                    var label = (Label)launcher.FindChild("RolltheSpire2_LauncherVersion", owned: false);
                    string expected = language == "zh" ? $"适配 {stable}（正式版）/ {beta}（测试版）" :
                        $"Supports {stable} (stable) / {beta} (beta)";
                    if (label.Text.Split('\n').Last() != expected)
                        throw new InvalidOperationException($"Launcher version label ({language}): {label.Text}");
                    checks++;
                }
                finally { launcher.Free(); }
            }

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
        }
        finally
        {
            surface.Free();
        }
        Console.WriteLine($"SAVE_STATUS_VERSION_UI_PASS checks={checks}");
        return 0;
    }
}
