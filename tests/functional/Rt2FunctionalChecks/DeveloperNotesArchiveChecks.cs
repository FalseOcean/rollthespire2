using RolltheSpire2.Infrastructure.DeveloperNotes;
using RolltheSpire2.Presentation.DeveloperNotes;

// Content/resource checks against the actual product assembly; no player-data writes.
internal static class DeveloperNotesArchiveChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidOperationException("NOTES_ARCHIVE " + message);
        }

        var assembly = typeof(DeveloperNotesDocumentProvider).Assembly;
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "RolltheSpire2.Infrastructure.DeveloperNotes.index.json"
        };
        foreach (string language in new[] { "zh", "en" })
            foreach (string chapter in DeveloperNotesChapterIds.All)
                expected.Add($"RolltheSpire2.Texts.{language}.DeveloperNotes.{chapter}.json");
        var actual = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".DeveloperNotes.", StringComparison.Ordinal)).ToArray();
        Check(expected.SetEquals(actual),
            "Only the current index and bilingual chapters are embedded; historical archives are excluded");

        var provider = new DeveloperNotesDocumentProvider();
        foreach (string language in new[] { "zh", "en" })
        {
            var result = provider.Resolve(language);
            Check(result.IsAvailable && !result.UsedEnglishFallback && result.Issue.Length == 0,
                $"The actual {language} bundled document loads without fallback: {result.Issue}");
            var release = result.Document!.Chapters.Single(chapter => chapter.Id == DeveloperNotesChapterIds.Release);
            Check(!release.Blocks.Any(block => block.Type == DeveloperNotesBlockKinds.Heading &&
                    (block.Text?.StartsWith("RolltheSpire2 1.3.", StringComparison.Ordinal) ?? false)),
                $"The {language} current release article excludes archived 1.3.x sections");
            var contributors = result.Document.Chapters.Single(chapter => chapter.Id == DeveloperNotesChapterIds.Contributors);
            Check(contributors.Blocks.Any(block => block.Text?.Contains(
                    "https://github.com/shion0528/rollthespire2/releases/tag/v1.3.5-resource.17",
                    StringComparison.Ordinal) == true),
                $"The {language} contributor's valid historical-version source link remains available");
        }

        Console.WriteLine($"NOTES_ARCHIVE_PASS checks={checks}");
        return 0;
    }
}
