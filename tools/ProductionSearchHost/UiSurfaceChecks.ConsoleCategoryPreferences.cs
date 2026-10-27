using System.Reflection;
using Godot;
using RolltheSpire2.Ui.InRunConsole;

internal static partial class UiSurfaceChecks
{
    private static void CheckConsoleCategoryPreferenceStore(string output, Action<bool, string> check)
    {
        string fixture = Path.Combine(output, "category-preference-roundtrip");
        string path = Path.Combine(fixture, "RolltheSpire2", "state", "console_preferences.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);
        var store = new ConsolePreferencesStore(fixture);
        for (int category = 0; category < 6; category++)
            check(store.LoadCompactIcons(category), $"category {category} defaults to compact");
        for (int category = 0; category < 6; category++)
            store.SaveCompactIcons(category, category % 2 == 0);
        var recreated = new ConsolePreferencesStore(fixture);
        for (int category = 0; category < 6; category++)
            check(recreated.LoadCompactIcons(category) == (category % 2 == 0),
                $"category {category} independently survives store recreation");

        foreach (bool legacy in new[] { false, true })
        {
            File.WriteAllText(path, "{\"version\":1,\"compactIcons\":" + (legacy ? "true" : "false") + "}");
            var migrated = new ConsolePreferencesStore(fixture);
            for (int category = 0; category < 6; category++)
                check(migrated.LoadCompactIcons(category) == legacy, $"legacy {legacy} applies to category {category}");
            migrated.SaveCompactIcons(4, !legacy);
            var afterMigration = new ConsolePreferencesStore(fixture);
            for (int category = 0; category < 6; category++)
                check(afterMigration.LoadCompactIcons(category) == (category == 4 ? !legacy : legacy),
                    $"legacy migration preserves untouched category {category}");
        }

        foreach (string invalid in new[]
        {
            "{broken", "{\"version\":99,\"compactIcons\":false}",
            "{\"version\":2,\"compactIconsByCategory\":{\"cards\":false}}",
            "{\"version\":2,\"compactIconsByCategory\":{\"cards\":false,\"relics\":true,\"potions\":true,\"ancients\":true,\"fights\":true,\"unknown\":true}}"
        })
        {
            File.WriteAllText(path, invalid);
            var unreadable = new ConsolePreferencesStore(fixture);
            check(unreadable.LoadCompactIcons(4), "unreadable category preference uses compact default");
            unreadable.SaveCompactIcons(4, false);
            check(File.ReadAllText(path) == invalid, "unreadable category document survives preference edit");
            // Saving without first loading must also preserve unfamiliar documents.
            new ConsolePreferencesStore(fixture).SaveCompactIcons(0, false);
            check(File.ReadAllText(path) == invalid, "save revalidates unknown category preference documents");
        }
    }

    // Invoke after browsing assertions: this intentionally visits every category.
    private static void CheckConsoleCategoryPreferenceSwitching(InRunConsolePanel panel, Action<bool, string> check)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var display = (OptionButton)typeof(InRunConsolePanel).GetField("_viewMode", members)!.GetValue(panel)!;
        int originalCategory = (int)typeof(InRunConsolePanel).GetField("_tab", members)!.GetValue(panel)!;
        int[] originalModes = new int[6];
        for (int category = 0; category < 6; category++)
        {
            panel.OpenCategory(category);
            originalModes[category] = display.Selected;
            display.Select(category % 2);
            display.EmitSignal(OptionButton.SignalName.ItemSelected, (long)(category % 2));
        }
        for (int category = 5; category >= 0; category--)
        {
            panel.OpenCategory(category);
            check(display.Selected == category % 2, $"switching restores category {category}'s independent display mode");
            check(new ConsolePreferencesStore(OS.GetUserDataDir()).LoadCompactIcons(category) == (category % 2 == 1),
                $"actual category {category} display control persists its own mode");
        }
        for (int category = 0; category < 6; category++)
        {
            panel.OpenCategory(category);
            display.Select(originalModes[category]);
            display.EmitSignal(OptionButton.SignalName.ItemSelected, (long)originalModes[category]);
        }
        panel.OpenCategory(originalCategory);
    }
}
