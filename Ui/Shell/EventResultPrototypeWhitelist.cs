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
    TinkerTime,
    DollRoom,
    MuseumOfTorture,
    ThermaeNonupeipe,
    CrystalSphere,
    RelicRarity
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
        Entry("DOLL_ROOM", EventResultPrototypeSource.Shared, EventResultPrototypeEditorKind.DollRoom),
        Entry("CRYSTAL_SPHERE", EventResultPrototypeSource.Shared, EventResultPrototypeEditorKind.CrystalSphere),
        Entry("MUSEUM_OF_TORTURE", EventResultPrototypeSource.Shared, EventResultPrototypeEditorKind.MuseumOfTorture),
        Entry("RANWID_THE_ELDER", EventResultPrototypeSource.Shared, EventResultPrototypeEditorKind.RelicRarity),
        Entry("RELIC_TRADER", EventResultPrototypeSource.Shared, EventResultPrototypeEditorKind.RelicRarity),
        Entry("THIS_OR_THAT", EventResultPrototypeSource.Shared, EventResultPrototypeEditorKind.RelicRarity),
        Entry("AROMA_OF_CHAOS", EventResultPrototypeSource.Act1VariantA, EventResultPrototypeEditorKind.SingleTransform, "OVERGROWTH"),
        Entry("MORPHIC_GROVE", EventResultPrototypeSource.Act1VariantA, EventResultPrototypeEditorKind.DoubleTransform, "OVERGROWTH"),
        Entry("WHISPERING_HOLLOW", EventResultPrototypeSource.Act1VariantA, EventResultPrototypeEditorKind.SingleTransform, "OVERGROWTH"),
        Entry("LUMINOUS_CHOIR", EventResultPrototypeSource.Act1VariantA, EventResultPrototypeEditorKind.RelicRarity, "OVERGROWTH"),
        Entry("UNREST_SITE", EventResultPrototypeSource.Act1VariantA, EventResultPrototypeEditorKind.RelicRarity, "OVERGROWTH"),
        Entry("TRASH_HEAP", EventResultPrototypeSource.Act1VariantB, EventResultPrototypeEditorKind.TrashHeap, "UNDERDOCKS"),
        Entry("WATERFALL_OF_OBVIOUS_SECRETS", EventResultPrototypeSource.Act1VariantB, EventResultPrototypeEditorKind.RelicRarity, "UNDERDOCKS"),
        Entry("COLORFUL_PHILOSOPHERS", EventResultPrototypeSource.Act2, EventResultPrototypeEditorKind.CharacterColor),
        Entry("TRIAL", EventResultPrototypeSource.Act3, EventResultPrototypeEditorKind.Trial),
        Entry("TINKER_TIME", EventResultPrototypeSource.Act3, EventResultPrototypeEditorKind.TinkerTime),
        Entry("BATTLEWORN_DUMMY", EventResultPrototypeSource.Act3, EventResultPrototypeEditorKind.RelicRarity),
        Entry("ROUND_TEA_PARTY", EventResultPrototypeSource.Act3, EventResultPrototypeEditorKind.RelicRarity),
        Entry("THERMAE_NONUPEIPE", EventResultPrototypeSource.Act3, EventResultPrototypeEditorKind.ThermaeNonupeipe)
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
