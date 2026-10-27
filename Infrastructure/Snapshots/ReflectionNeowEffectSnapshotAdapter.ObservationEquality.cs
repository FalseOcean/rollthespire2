using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    // Records preserve all scalar fields; collection fields are compared in
    // source order rather than by the backing array/list reference.
    private static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
        ReferenceEquals(a, b) || a is not null && b is not null && a.SequenceEqual(b);
    private static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b, Func<T, T, bool> equal)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!equal(a[i], b[i])) return false;
        return true;
    }
    private static bool PoolEqual(PoolCapture a, PoolCapture b) =>
        a with { Items = b.Items } == b && Same(a.Cards, b.Cards) &&
        Same(a.Relics, b.Relics) && Same(a.Potions, b.Potions);
    internal static bool EffectInputsEqual(EffectObservedInputs a, EffectObservedInputs b) =>
        a.Failure is null && b.Failure is null &&
        a with { ObservedAtUtc=b.ObservedAtUtc, SelectedCards=b.SelectedCards, ColorlessCards=b.ColorlessCards, CurseCards=b.CurseCards,
            CharacterPotions=b.CharacterPotions, SharedPotions=b.SharedPotions, CharacterRelics=b.CharacterRelics,
            SharedRelics=b.SharedRelics, OtherCharacterPools=b.OtherCharacterPools, Deck=b.Deck,
            UnlockDescriptors=b.UnlockDescriptors } == b &&
        PoolEqual(a.SelectedCards,b.SelectedCards) && PoolEqual(a.ColorlessCards,b.ColorlessCards) &&
        PoolEqual(a.CurseCards,b.CurseCards) && PoolEqual(a.CharacterPotions,b.CharacterPotions) &&
        PoolEqual(a.SharedPotions,b.SharedPotions) && PoolEqual(a.CharacterRelics,b.CharacterRelics) &&
        PoolEqual(a.SharedRelics,b.SharedRelics) && Same(a.Deck,b.Deck) && Same(a.UnlockDescriptors,b.UnlockDescriptors) &&
        Same(a.OtherCharacterPools,b.OtherCharacterPools,(x,y) => x with { Cards=y.Cards } == y && Same(x.Cards,y.Cards));

    internal static bool WorldInputsEqual(WorldObservedInputs a, WorldObservedInputs b) =>
        a.Failure is null && b.Failure is null &&
        a with { ObservedAtUtc=b.ObservedAtUtc, Groups=b.Groups, SharedEvents=b.SharedEvents, EventAuthority=b.EventAuthority,
            SharedAncients=b.SharedAncients, UnlockedCharacters=b.UnlockedCharacters,
            RelicCatalog=b.RelicCatalog, ModernAuthority=b.ModernAuthority } == b &&
        ModelsEqual(a.SharedEvents,b.SharedEvents) && ModelsEqual(a.SharedAncients,b.SharedAncients) &&
        ModelsEqual(a.UnlockedCharacters,b.UnlockedCharacters) && ModelsEqual(a.RelicCatalog,b.RelicCatalog) &&
        EventsEqual(a.EventAuthority,b.EventAuthority) && ModernEqual(a.ModernAuthority,b.ModernAuthority) &&
        Same(a.Groups,b.Groups,(x,y) => x.Act == y.Act && Same(x.Acts,y.Acts,ActEqual));
    private static bool ModelsEqual(ModelListCapture a, ModelListCapture b) => a.Exact == b.Exact && Same(a.Keys,b.Keys);
    private static bool ActEqual(WorldActSnapshot a, WorldActSnapshot b) =>
        a with { Events=b.Events, WeakEncounters=b.WeakEncounters, RegularEncounters=b.RegularEncounters,
            EliteEncounters=b.EliteEncounters, Bosses=b.Bosses, Ancients=b.Ancients,
            OrderedRawEvents=b.OrderedRawEvents, OrderedEligibleEvents=b.OrderedEligibleEvents } == b &&
        Same(a.Events,b.Events) && Same(a.Bosses,b.Bosses) && Same(a.Ancients,b.Ancients) &&
        Same(a.OrderedRawEvents,b.OrderedRawEvents) && Same(a.OrderedEligibleEvents,b.OrderedEligibleEvents) &&
        Same(a.WeakEncounters,b.WeakEncounters,LegacyEncounterEqual) &&
        Same(a.RegularEncounters,b.RegularEncounters,LegacyEncounterEqual) &&
        Same(a.EliteEncounters,b.EliteEncounters,LegacyEncounterEqual);
    private static bool LegacyEncounterEqual(WorldEncounterSnapshot a, WorldEncounterSnapshot b) =>
        a.EncounterKey == b.EncounterKey && Same(a.Tags,b.Tags);
    private static bool EventsEqual(Beta109EventCatalogAuthoritySnapshot a, Beta109EventCatalogAuthoritySnapshot b) =>
        a with { OrderedSharedEventsRaw=b.OrderedSharedEventsRaw, EpochsInFilterOrder=b.EpochsInFilterOrder } == b &&
        Same(a.OrderedSharedEventsRaw,b.OrderedSharedEventsRaw) && Same(a.EpochsInFilterOrder,b.EpochsInFilterOrder,
            (x,y) => x with { OrderedMemberKeys=y.OrderedMemberKeys } == y && Same(x.OrderedMemberKeys,y.OrderedMemberKeys));
    private static bool ModernEqual(Beta109StaticAuthorityCapture? a, Beta109StaticAuthorityCapture? b)
    {
        if (ReferenceEquals(a,b)) return true;
        if (a is null || b is null) return false;
        return a with { ActSelectionGroups=b.ActSelectionGroups, LobbyPlayers=b.LobbyPlayers,
            AllCharactersInSourceOrder=b.AllCharactersInSourceOrder, OrderedActs=b.OrderedActs,
            SharedEvents=b.SharedEvents, EventAuthority=b.EventAuthority, AllSharedAncients=b.AllSharedAncients,
            UnlockedSharedAncients=b.UnlockedSharedAncients, SharedRelicBuckets=b.SharedRelicBuckets,
            PlayerRelicBuckets=b.PlayerRelicBuckets } == b &&
            Same(a.LobbyPlayers,b.LobbyPlayers) && Same(a.AllCharactersInSourceOrder,b.AllCharactersInSourceOrder) &&
            Same(a.SharedEvents,b.SharedEvents) && EventsEqual(a.EventAuthority,b.EventAuthority) &&
            Same(a.AllSharedAncients,b.AllSharedAncients) && Same(a.UnlockedSharedAncients,b.UnlockedSharedAncients) &&
            Same(a.SharedRelicBuckets,b.SharedRelicBuckets,BucketEqual) && Same(a.PlayerRelicBuckets,b.PlayerRelicBuckets,BucketEqual) &&
            Same(a.ActSelectionGroups,b.ActSelectionGroups,SelectionEqual) && Same(a.OrderedActs,b.OrderedActs,GenerationEqual);
    }
    private static bool BucketEqual(Beta109RelicBucketSnapshot a, Beta109RelicBucketSnapshot b) =>
        a with { OrderedRelics=b.OrderedRelics, OrderedEntries=b.OrderedEntries } == b &&
        Same(a.OrderedRelics,b.OrderedRelics) && Same(a.OrderedEntries,b.OrderedEntries);
    private static bool SelectionEqual(Beta109ActSelectionGroupSnapshot a, Beta109ActSelectionGroupSnapshot b) =>
        a with { EligibleActsInSourceOrder=b.EligibleActsInSourceOrder, CandidatesInSourceOrder=b.CandidatesInSourceOrder } == b &&
        Same(a.EligibleActsInSourceOrder,b.EligibleActsInSourceOrder) && Same(a.CandidatesInSourceOrder,b.CandidatesInSourceOrder);
    private static bool GenerationEqual(Beta109ActGenerationSnapshot a, Beta109ActGenerationSnapshot b) =>
        a with { OrderedEvents=b.OrderedEvents, OrderedGenerateAllEncounters=b.OrderedGenerateAllEncounters,
            OrderedAncients=b.OrderedAncients, OrderedRawEvents=b.OrderedRawEvents, OrderedEligibleEvents=b.OrderedEligibleEvents } == b &&
        Same(a.OrderedEvents,b.OrderedEvents) && Same(a.OrderedAncients,b.OrderedAncients) &&
        Same(a.OrderedRawEvents,b.OrderedRawEvents) && Same(a.OrderedEligibleEvents,b.OrderedEligibleEvents) &&
        Same(a.OrderedGenerateAllEncounters,b.OrderedGenerateAllEncounters,EncounterEqual);
    private static bool EncounterEqual(Beta109EncounterEntrySnapshot a, Beta109EncounterEntrySnapshot b) =>
        a with { Tags=b.Tags, SharesTagsWith=b.SharesTagsWith, SharesTagsWithSourceOrdinals=b.SharesTagsWithSourceOrdinals } == b &&
        Same(a.Tags,b.Tags) && Same(a.SharesTagsWith,b.SharesTagsWith) && Same(a.SharesTagsWithSourceOrdinals,b.SharesTagsWithSourceOrdinals);
}
