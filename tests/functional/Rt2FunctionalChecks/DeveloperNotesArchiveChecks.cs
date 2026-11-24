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
        string[]? releaseVersions = null;
        foreach (string language in new[] { "zh", "en" })
        {
            var result = provider.Resolve(language);
            Check(result.IsAvailable && !result.UsedEnglishFallback && result.Issue.Length == 0,
                $"The actual {language} bundled document loads without fallback: {result.Issue}");
            var release = result.Document!.Chapters.Single(chapter => chapter.Id == DeveloperNotesChapterIds.Release);
            var versions = release.Blocks.Where(block => block.ReleaseVersion is not null).ToArray();
            Check(versions.Length > 0 && versions.All(block => block.Type == DeveloperNotesBlockKinds.Heading) &&
                    versions[0].ReleaseVersion == release.AcknowledgementVersion,
                $"The {language} release headings start with the current acknowledgement version");
            var sequence = versions.Select(block => block.ReleaseVersion!).ToArray();
            Check(releaseVersions is null || releaseVersions.SequenceEqual(sequence),
                "Both languages identify the same release entries in reading order");
            releaseVersions = sequence;
            Check(!release.Blocks.Any(block => block.Type == DeveloperNotesBlockKinds.Heading &&
                    (block.Text?.StartsWith("RolltheSpire2 1.3.", StringComparison.Ordinal) ?? false)),
                $"The {language} current release article excludes archived 1.3.x sections");
            var contributors = result.Document.Chapters.Single(chapter => chapter.Id == DeveloperNotesChapterIds.Contributors);
            Check(contributors.Blocks.Any(block => block.Text?.Contains(
                    "https://github.com/shion0528/rollthespire2/releases/tag/v1.3.5-resource.17",
                    StringComparison.Ordinal) == true),
                $"The {language} contributor's valid historical-version source link remains available");
        }

        var history = new DeveloperNotesChapter("release", "Updates", null,
        [
            new("Heading", "November update", null) { ReleaseVersion = "1.10.0" },
            new("Heading", "Highlights", null),
            new("Paragraph", "New features", null),
            new("Heading", "October update", null) { ReleaseVersion = "1.9.9" },
            new("Paragraph", "Fixes", null),
            new("Heading", "September update", null) { ReleaseVersion = "1.9.7" },
            new("Paragraph", "Earlier features", null)
        ], "1.10.0", true);
        Check(history.NeedsAcknowledgement("1.9.9") && history.AcknowledgementBlockIndex("1.9.9") == 3,
            "A one-release update places one acknowledgement after the complete new entry");
        Check(history.AcknowledgementBlockIndex("1.9.8") == 5,
            "A skipped release can be absent from the retained headings");
        Check(history.AcknowledgementBlockIndex(null) == history.Blocks.Count &&
                history.AcknowledgementBlockIndex("1.0.0") == history.Blocks.Count,
            "New readers and readers of archived releases confirm below all retained updates");
        Check(!history.NeedsAcknowledgement("1.10.0") && !history.NeedsAcknowledgement("1.11.0"),
            "A release that was confirmed or predates the saved confirmation stays read");
        var positioning = history with { Id = DeveloperNotesChapterIds.Positioning };
        Check(positioning.AcknowledgementBlockIndex("1.9.9") == positioning.Blocks.Count &&
                positioning.NeedsAcknowledgement("1.11.0"),
            "Other chapters keep their own explicit revision and article-end confirmation");
        Console.WriteLine($"NOTES_ARCHIVE_PASS checks={checks}");
        return 0;
    }
}
