using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Pages.Search.Event;

namespace RolltheSpire2.Ui.Shell;

internal enum EventResultPrototypeSource
{
    Shared,
    Act1VariantA,
    Act1VariantB,
    Act2,
    Act3
}

internal enum EventResultPrototypeEditorKind
{
    FakeMerchantRelic,
    SingleTransform,
    DoubleTransform,
    TrashHeap,
    CharacterColor,
    Trial,
    TinkerTime
}

internal sealed record EventResultPrototypeDescriptor(
    ModelKey EventKey,
    EventResultPrototypeSource Source,
    EventResultPrototypeEditorKind EditorKind,
    string? ActVariantEntry = null);

internal static class EventResultPrototypeWhitelist
{
    public static IReadOnlyList<EventResultPrototypeDescriptor> Entries { get; } = new[]
    {
        Entry("FAKE_MERCHANT", EventResultPrototypeSource.Shared, EventResultPrototypeEditorKind.FakeMerchantRelic),
        Entry("SYMBIOTE", EventResultPrototypeSource.Shared, EventResultPrototypeEditorKind.SingleTransform),
        Entry("AROMA_OF_CHAOS", EventResultPrototypeSource.Act1VariantA, EventResultPrototypeEditorKind.SingleTransform, "OVERGROWTH"),
        Entry("MORPHIC_GROVE", EventResultPrototypeSource.Act1VariantA, EventResultPrototypeEditorKind.DoubleTransform, "OVERGROWTH"),
        Entry("WHISPERING_HOLLOW", EventResultPrototypeSource.Act1VariantA, EventResultPrototypeEditorKind.SingleTransform, "OVERGROWTH"),
        Entry("TRASH_HEAP", EventResultPrototypeSource.Act1VariantB, EventResultPrototypeEditorKind.TrashHeap, "UNDERDOCKS"),
        Entry("COLORFUL_PHILOSOPHERS", EventResultPrototypeSource.Act2, EventResultPrototypeEditorKind.CharacterColor),
        Entry("TRIAL", EventResultPrototypeSource.Act3, EventResultPrototypeEditorKind.Trial),
        Entry("TINKER_TIME", EventResultPrototypeSource.Act3, EventResultPrototypeEditorKind.TinkerTime)
    };

    public static IReadOnlyList<string> TinkerEffects(int cardType) => cardType switch
    {
        0 => ["sapping", "violence", "choking"],
        1 => ["energized", "wisdom", "chaos"],
        2 => ["expertise", "curious", "improvement"],
        _ => []
    };

    public static IReadOnlyList<EventResultPrototypeDescriptor> ForSource(
        EventResultPrototypeSource source,
        EventSequenceSearchUiCatalog catalog) => Entries
        .Where(entry => entry.Source == source && MatchesRuntimeCatalog(entry, catalog))
        .ToArray();

    public static EventResultPrototypeDescriptor? Find(ModelKey key) =>
        Entries.FirstOrDefault(entry => entry.EventKey == key);

    private static bool MatchesRuntimeCatalog(
        EventResultPrototypeDescriptor descriptor,
        EventSequenceSearchUiCatalog catalog)
    {
        EventSearchUiCandidate[] candidates = catalog.CandidatesByAct.Values
            .SelectMany(value => value)
            .Where(candidate => candidate.EventKey == descriptor.EventKey)
            .ToArray();
        return descriptor.Source switch
        {
            EventResultPrototypeSource.Shared => candidates.Any(candidate => candidate.IsShared),
            EventResultPrototypeSource.Act1VariantA or EventResultPrototypeSource.Act1VariantB =>
                candidates.Any(candidate => candidate.VariantActKeys.Any(key =>
                    string.Equals(key.Entry, descriptor.ActVariantEntry, StringComparison.Ordinal))),
            EventResultPrototypeSource.Act2 => catalog.CandidatesForAct(2).Any(candidate =>
                candidate.EventKey == descriptor.EventKey && !candidate.IsShared),
            EventResultPrototypeSource.Act3 => catalog.CandidatesForAct(3).Any(candidate =>
                candidate.EventKey == descriptor.EventKey && !candidate.IsShared),
            _ => false
        };
    }

    private static EventResultPrototypeDescriptor Entry(
        string eventEntry,
        EventResultPrototypeSource source,
        EventResultPrototypeEditorKind editorKind,
        string? variant = null) => new(
        new ModelKey(BaseGameModelKeys.Categories.Event, eventEntry), source, editorKind, variant);
}
