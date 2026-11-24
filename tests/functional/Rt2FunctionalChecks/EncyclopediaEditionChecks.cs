using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Shell;

internal static class EncyclopediaEditionChecks
{
    internal static int Run()
    {
        var detection = NativeWorkbenchCheckEnvironment.Initialize("EncyclopediaEditions").Detection;
        var modern = detection with { RawVersion = "0.113.1", NormalizedVersion = "0.113.1" };
        var canvas = new EncyclopediaCanvas();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(canvas);
        int checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidDataException("EncyclopediaEditions." + name);
        }
        OptionButton Picker() => canvas.GetNode<OptionButton>("EncyclopediaEdition");
        RichTextLabel[] Labels() => canvas.FindChildren("*", "RichTextLabel", true, false).Cast<RichTextLabel>().ToArray();
        bool Shows(string version) => Labels().Any(l => l.GetParsedText().EndsWith(" /   BETA " + version, StringComparison.Ordinal));
        string Body() => string.Join('\n', Labels().Select(l => l.GetParsedText()));
        void Select(string label)
        {
            var picker = Picker();
            int index = Enumerable.Range(0, picker.ItemCount).Single(i => picker.GetItemText(i) == label);
            picker.Select(index);
            picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        }

        try
        {
            foreach (string language in new[] { "zh", "en" })
            {
                canvas.Refresh(language, modern);
                canvas.Open("neow-bones-curse");
                string auto = Picker().GetItemText(0);
                string current = Body();
                Check(Picker().Selected == 0 && auto.Contains("0.113.1") && Shows("0.113") &&
                    current.Contains("NonCombatGeneration"), "Automatic1131." + language);
                Select("0.111");
                Check(Shows("0.111.0") && Body().Contains("Niche") && !Body().Contains("NonCombatGeneration"),
                    "CompareOnSameTopic." + language);
                Labels()[0].EmitSignal(RichTextLabel.SignalName.MetaClicked, "article:multiplayer");
                Check(Picker().Text == "0.111" && Shows("0.111.0"), "ManualEditionAcrossLink." + language);
                Select(auto);
                Check(Shows("0.113"), "RestoreAuto." + language);
                canvas.Open("neow-bones-curse");
                Check(Body() == current, "RestoredDocument." + language);
                canvas.Open(null);
                var search = canvas.GetChildren().OfType<LineEdit>().Single();
                search.Text = "Hash";
                search.EmitSignal(LineEdit.SignalName.TextChanged, "Hash");
                Select("0.113");
                Check(search.Text == "Hash", "EditionChangeKeepsSearch." + language);
                search.EmitSignal(LineEdit.SignalName.TextSubmitted, "Hash");
                Check(Picker().Text == "0.113" && Body().Contains("0.109.0"), "SearchOpensSeedHistory." + language);
                bool hasModernHistory = typeof(EncyclopediaCanvas).Assembly.GetManifestResourceNames()
                    .Contains($"RolltheSpire2.Texts.{language}.Encyclopedia.v113.seed_history.md");
                Check(Shows(hasModernHistory ? "0.113" : "0.111.0"), "ArticleShowsActualSource." + language);
                Select(auto);
                canvas.Refresh(language, detection);
                canvas.Open("neow-bones-curse");
                Check(Shows("0.111.0") && Picker().Text.Contains("0.111.0"), "Automatic111." + language);
                canvas.Refresh(language, GameVersionDetection.NotDetected);
                Check(Shows("0.111.0") && !Picker().Text.Contains("0.111"), "UnmatchedAuto." + language);
                Select("0.113");
                Check(Body() == current, "ManualEditionWithoutDetection." + language);
                Select(Picker().GetItemText(0));
            }
        }
        finally { canvas.QueueFree(); }
        Console.WriteLine($"EncyclopediaEditionChecks PASS ({checks})");
        return 0;
    }
}
