namespace RolltheSpire2.Presentation.DeveloperNotes;

public static class DeveloperNotesChapterIds
{
    public const string Release = "release";
    public const string Next = "next";
    public const string Positioning = "positioning";
    public const string Devlog = "devlog";
    public const string Faq = "faq";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        Release,
        Next,
        Positioning,
        Devlog,
        Faq
    };
}

public static class DeveloperNotesBlockKinds
{
    public const string Heading = "Heading";
    public const string Paragraph = "Paragraph";
    public const string BulletList = "BulletList";
    public const string Callout = "Callout";
}

public sealed record DeveloperNotesDocument(
    int SchemaVersion,
    string ContentVersion,
    string TargetModVersion,
    string UpdatedAt,
    IReadOnlyList<DeveloperNotesChapter> Chapters);

// Shared by both languages. Only an explicit acknowledgement-version change
// asks readers to confirm again; copy edits and releases need not change it.
public sealed record DeveloperNotesIndex(
    int SchemaVersion,
    string ContentVersion,
    string TargetModVersion,
    string UpdatedAt,
    IReadOnlyList<DeveloperNotesChapterEntry> Chapters);

public sealed record DeveloperNotesChapterEntry(
    string Id,
    string AcknowledgementVersion,
    bool RequiresAcknowledgement);

public sealed record DeveloperNotesChapter(
    string Id,
    string Title,
    string? Summary,
    IReadOnlyList<DeveloperNotesBlock> Blocks,
    string AcknowledgementVersion = "",
    bool RequiresAcknowledgement = false);

public sealed record DeveloperNotesBlock(
    string Type,
    string? Text,
    IReadOnlyList<string>? Items);
