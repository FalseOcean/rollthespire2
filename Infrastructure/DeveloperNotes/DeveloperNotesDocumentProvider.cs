using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Presentation.DeveloperNotes;

namespace RolltheSpire2.Infrastructure.DeveloperNotes;

public sealed record DeveloperNotesResolution(
    DeveloperNotesDocument? Document,
    string RequestedLocale,
    string ResolvedLocale,
    bool UsedEnglishFallback,
    string Issue)
{
    public bool IsAvailable => Document is not null;
}

public sealed class DeveloperNotesDocumentProvider
{
    private readonly Func<string, Stream?> _documentStreamFactory;

    public const int SupportedSchemaVersion = 2;
    public const string EnglishLocale = "en-US";
    public const string ChineseLocale = "zh-CN";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public DeveloperNotesDocumentProvider(Func<string, Stream?>? documentStreamFactory = null)
    {
        _documentStreamFactory = documentStreamFactory ?? OpenDocumentStream;
    }

    public DeveloperNotesResolution Resolve(string locale)
    {
        string requested = string.IsNullOrWhiteSpace(locale) ? EnglishLocale : locale.Trim();
        string preferred = ResolveBundledLocale(requested);
        bool localeFallback = !IsEnglishLocale(requested) && !IsChineseLocale(requested);

        DeveloperNotesDocument? preferredDocument = null;
        string preferredIssue = string.Empty;
        bool preferredLoaded = !localeFallback &&
            TryLoadValidated(preferred, out preferredDocument, out preferredIssue);
        if (preferredLoaded)
        {
            return new DeveloperNotesResolution(
                preferredDocument,
                requested,
                preferred,
                UsedEnglishFallback: false,
                Issue: string.Empty);
        }

        if (!localeFallback && string.Equals(preferred, EnglishLocale, StringComparison.Ordinal))
        {
            return new DeveloperNotesResolution(null, requested, EnglishLocale, false, preferredIssue);
        }

        string primaryIssue = localeFallback ? "UnsupportedLocale" : preferredIssue;
        if (TryLoadValidated(EnglishLocale, out DeveloperNotesDocument? englishDocument, out string englishIssue))
        {
            return new DeveloperNotesResolution(
                englishDocument,
                requested,
                EnglishLocale,
                UsedEnglishFallback: true,
                Issue: primaryIssue);
        }

        string issue = $"Primary={primaryIssue};English={englishIssue}";
        return new DeveloperNotesResolution(null, requested, EnglishLocale, true, issue);
    }

    public static string ResolveBundledLocale(string locale) =>
        IsChineseLocale(locale) ? ChineseLocale : EnglishLocale;

    private static bool IsChineseLocale(string locale) =>
        string.Equals(locale, "zh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(locale, ChineseLocale, StringComparison.OrdinalIgnoreCase);

    private static bool IsEnglishLocale(string locale) =>
        string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(locale, EnglishLocale, StringComparison.OrdinalIgnoreCase);

    private bool TryLoadValidated(
        string locale,
        out DeveloperNotesDocument? document,
        out string issue)
    {
        document = null;
        issue = string.Empty;

        try
        {
            using Stream? stream = _documentStreamFactory("index");
            if (stream is null)
            {
                issue = "MissingBundledIndex";
                return false;
            }

            var index = JsonSerializer.Deserialize<DeveloperNotesIndex>(stream, JsonOptions);
            if (index is null || index.Chapters is null)
            {
                issue = "EmptyIndex";
                return false;
            }

            var chapters = new List<DeveloperNotesChapter>();
            foreach (var entry in index.Chapters)
            {
                if (entry is null || !DeveloperNotesChapterIds.All.Contains(entry.Id) ||
                    string.IsNullOrWhiteSpace(entry.AcknowledgementVersion))
                { issue = "InvalidChapterEntry"; return false; }
                using Stream? article = _documentStreamFactory($"{entry.Id}.{locale}");
                if (article is null) { issue = $"MissingChapter:{entry.Id}:{locale}"; return false; }
                var chapter = JsonSerializer.Deserialize<DeveloperNotesChapter>(article, JsonOptions);
                if (chapter is null || chapter.Id != entry.Id)
                { issue = $"InvalidChapterIdentity:{entry.Id}:{locale}"; return false; }
                chapters.Add(chapter with { AcknowledgementVersion = entry.AcknowledgementVersion,
                    RequiresAcknowledgement = entry.RequiresAcknowledgement });
            }
            var parsed = new DeveloperNotesDocument(index.SchemaVersion, index.ContentVersion,
                index.TargetModVersion, index.UpdatedAt, chapters);

            if (!TryValidate(parsed, out issue))
            {
                issue = $"InvalidDocument:{locale}:{issue}";
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            issue = $"MalformedJson:{locale}:{ex.GetType().Name}";
            return false;
        }
        catch (IOException ex)
        {
            issue = $"IoFailure:{locale}:{ex.GetType().Name}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            issue = $"AccessFailure:{locale}:{ex.GetType().Name}";
            return false;
        }
        catch (Exception ex)
        {
            issue = $"LoadFailure:{locale}:{ex.GetType().Name}";
            return false;
        }
    }

    private static Stream? OpenDocumentStream(string locale)
    {
        Assembly assembly = typeof(DeveloperNotesDocumentProvider).Assembly;
        string resourceName = $"RolltheSpire2.DeveloperNotes.{locale}.json";
        Stream? embedded = assembly.GetManifestResourceStream(resourceName);
        if (embedded is not null)
        {
            return embedded;
        }

        string assemblyDirectory = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
        string loosePath = Path.Combine(assemblyDirectory, "DeveloperNotes", locale + ".json");
        return File.Exists(loosePath) ? File.OpenRead(loosePath) : null;
    }

    private static bool TryValidate(DeveloperNotesDocument document, out string issue)
    {
        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            issue = $"UnsupportedSchema:{document.SchemaVersion}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(document.ContentVersion) ||
            string.IsNullOrWhiteSpace(document.TargetModVersion) ||
            string.IsNullOrWhiteSpace(document.UpdatedAt))
        {
            issue = "MissingDocumentMetadata";
            return false;
        }
        if (document.Chapters is null || document.Chapters.Count != DeveloperNotesChapterIds.All.Count)
        {
            issue = "ChapterCountMismatch";
            return false;
        }

        var expected = new HashSet<string>(DeveloperNotesChapterIds.All, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int chapterIndex = 0; chapterIndex < document.Chapters.Count; chapterIndex++)
        {
            DeveloperNotesChapter chapter = document.Chapters[chapterIndex];
            if (chapter is null || string.IsNullOrWhiteSpace(chapter.Id) || !expected.Contains(chapter.Id))
            {
                issue = $"UnknownChapter:{chapter?.Id ?? "<null>"}";
                return false;
            }
            if (!seen.Add(chapter.Id))
            {
                issue = $"DuplicateChapter:{chapter.Id}";
                return false;
            }
            if (string.IsNullOrWhiteSpace(chapter.Title) || chapter.Blocks is null)
            {
                issue = $"InvalidChapter:{chapter.Id}";
                return false;
            }
            foreach (DeveloperNotesBlock block in chapter.Blocks)
            {
                if (!TryValidateBlock(block, out issue))
                {
                    issue = $"Chapter={chapter.Id}:{issue}";
                    return false;
                }
            }
        }

        if (!expected.SetEquals(seen))
        {
            issue = "MissingStableChapter";
            return false;
        }

        issue = string.Empty;
        return true;
    }

    private static bool TryValidateBlock(DeveloperNotesBlock block, out string issue)
    {
        if (block is null || string.IsNullOrWhiteSpace(block.Type))
        {
            issue = "MissingBlockType";
            return false;
        }

        switch (block.Type)
        {
            case DeveloperNotesBlockKinds.Heading:
            case DeveloperNotesBlockKinds.Paragraph:
            case DeveloperNotesBlockKinds.Callout:
                if (string.IsNullOrWhiteSpace(block.Text))
                {
                    issue = $"MissingText:{block.Type}";
                    return false;
                }
                break;
            case DeveloperNotesBlockKinds.BulletList:
                if (block.Items is null || block.Items.Count == 0 || block.Items.Any(string.IsNullOrWhiteSpace))
                {
                    issue = "InvalidBulletList";
                    return false;
                }
                break;
            default:
                issue = $"UnsupportedBlockType:{block.Type}";
                return false;
        }

        issue = string.Empty;
        return true;
    }
}
