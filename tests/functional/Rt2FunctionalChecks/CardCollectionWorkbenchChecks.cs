using System.Text.Json;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Tests.FastSearch;

// Native editor/preset transactions, without starting a search or changing a run.
internal static class CardCollectionWorkbenchChecks
{
    internal static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("NativeCardCollectionWorkbench");
        string directory = Path.Combine(Path.GetTempPath(), "RolltheSpire2-card-collection-workbench", Guid.NewGuid().ToString("N"));
        var collections = new CardCollectionStore(directory);
        var keys = ModelDb.AllCards.Take(3).Select(c => new ModelKey(c.Id.Category, c.Id.Entry)).ToArray();
        var saved = collections.Save(null, "Retained collection", [keys[0]]);
        var frame = new QueryWorkbenchFrame(runtime, new SearchWorkspace(directory, false));
        frame.ConfigureCardCollections(collections);
        int checks = 0;
        void Check(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException("CardCollectionWorkbench: " + message); }
        WorkbenchSearchDraft Draft(CardCollectionSnapshot snapshot, int count = 3) => new(
            BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty with { CombatCardExclusion = new(count, snapshot.Cards.ToArray()) },
            AncientOptionConditionProfile.BroadDefault)
        { CombatRewardEditor = CombatRewardSearchDraft.Empty with { ExcludedCollection = snapshot, ExclusionBattleCount = count } };
        void Same(WorkbenchSearchDraft draft, CardCollectionSnapshot snapshot, int count, string message)
        {
            Check(draft.Query.CombatCardExclusion is { } exclusion && exclusion.BattleCount == count &&
                exclusion.CardKeys.Count == snapshot.Cards.Count && new HashSet<ModelKey>(exclusion.CardKeys, ModelKeyComparer.Instance).SetEquals(snapshot.Cards), message + " query members/range");
            var actual = draft.CombatRewardEditor?.ExcludedCollection;
            Check(actual is not null && actual.Id == snapshot.Id && actual.Name == snapshot.Name && actual.Revision == snapshot.Revision &&
                actual.Cards.Count == snapshot.Cards.Count && new HashSet<ModelKey>(actual.Cards, ModelKeyComparer.Instance).SetEquals(snapshot.Cards), message + " snapshot identity/revision/members");
        }
        try
        {
            var editor = (CombatRewardSearchEditor)typeof(QueryWorkbenchFrame).GetField("_combatEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(frame)!;
            editor.Size = new(980, 680);
            editor.Refresh("en", JsonUiTextProvider.CreateUi13("en"), BaseGameModelKeys.Characters.Ironclad, 10, 1, 0);
            var selector = (OptionButton)editor.FindChild("CombatCardExclusionCollection", true, false);
            Check(frame.CaptureDraft().Query.CombatCardExclusion is null, "initial rendered collection selection is disabled");
            selector.Select(1);
            selector.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Same(frame.CaptureDraft(), saved, 3, "actual selector event enables default three-combat request");
            Button RangeButton(string name) => (Button)editor.FindChild(name, true, false);
            RangeButton("CombatCardExclusionIncrease").EmitSignal(BaseButton.SignalName.Pressed);
            Same(frame.CaptureDraft(), saved, 4, "actual increase button updates exported query range");
            RangeButton("CombatCardExclusionDecrease").EmitSignal(BaseButton.SignalName.Pressed);
            RangeButton("CombatCardExclusionDecrease").EmitSignal(BaseButton.SignalName.Pressed);
            Same(frame.CaptureDraft(), saved, 2, "actual decrease button updates exported query range");

            var mutableMembers = saved.Cards.ToList();
            frame.RestoreDraft(Draft(saved with { Cards = mutableMembers }, 6), render: false);
            mutableMembers.Add(keys[1]);
            Same(frame.CaptureDraft(), saved, 6, "editor detaches the selected collection");
            Check(frame.CaptureDraft().Query.CombatCardRewards is null && frame.CaptureDraft().Query.CombatPotionRewards is null,
                "negative collection works without positive combat targets");
            collections.Save(saved.Id, "Changed collection", [keys[1], keys[2]]);
            Same(frame.CaptureDraft(), saved, 6, "library edits do not change an existing request");

            var presetStore = new SearchPresetStore(Path.Combine(directory, "presets"));
            var unordered = saved with { Cards = keys.OrderByDescending(k => k.Serialized, StringComparer.Ordinal).ToArray() };
            var beforeNormalization = Draft(unordered, 4);
            var normalizedQuery = beforeNormalization.Compile(runtime, out _).NormalizedQuery;
            Check(!normalizedQuery.CombatCardExclusion!.CardKeys.SequenceEqual(unordered.Cards, ModelKeyComparer.Instance),
                "fixture exercises actual compiler reordering rather than identical authored order");
            var normalized = beforeNormalization with { Query = normalizedQuery };
            SearchPresetCompatibilityResolver.ValidateWorkbenchShape(normalized);
            frame.RestoreDraft(normalized, render: false);
            Same(frame.CaptureDraft(), unordered, 4, "compiled set order does not invalidate its collection snapshot");
            var reorderedPreset = presetStore.Save("Normalized collection", "", [], frame.CapturePreset(normalized));
            frame.ApplyPreset(reorderedPreset, render: false);
            Same(frame.CaptureDraft(), unordered, 4, "normalized query and authored snapshot survive real preset save and use");
            frame.RestoreDraft(Draft(saved, 6), render: false);
            var preset = presetStore.Save("Collection search", "", [], frame.CapturePreset());
            var reloaded = new SearchPresetStore(Path.Combine(directory, "presets")).GetPresets().Single(p => p.Id == preset.Id);
            Check(reloaded.Workbench is not null, "new snapshot survives strict preset persistence");
            collections.Delete(saved.Id);
            frame.ApplyPreset(reloaded, render: false);
            Same(frame.CaptureDraft(), saved, 6, "deleted library source does not block saved snapshot use");

            var phantom = new ModelKey("CARD", "RT2_UNAVAILABLE_COLLECTION_MEMBER");
            var unavailable = saved with { Cards = new[] { keys[0], phantom } };
            frame.RestoreDraft(Draft(unavailable), render: false);
            var missingPreset = presetStore.Save("Unavailable member", "", [], frame.CapturePreset());
            var resolution = SearchPresetCompatibilityResolver.ResolveWorkbench(missingPreset, RuntimeAuthorityEnvironment.Current.Authority);
            Check(resolution.CanLoad, "legally authored missing collection member remains executable as unverified");
            frame.ApplyPreset(missingPreset, render: false);
            Same(frame.CaptureDraft(), unavailable, 3, "preset use never filters missing identities");
            var compiled = frame.CaptureDraft().Compile(runtime, out var authority);
            Check(authority.AvailableCardIdentities is { } identities && !identities.Contains(phantom, ModelKeyComparer.Instance),
                "exclusion-only compile captures all registered card identities without inventing missing content");
            Check(compiled.Query.CombatCardExclusion?.CardKeys.Contains(phantom, ModelKeyComparer.Instance) == true,
                "missing member survives compilation");

            string json = JsonSerializer.Serialize(frame.CaptureDraft(), SearchPresetStore.CreateJsonOptions());
            var history = JsonSerializer.Deserialize<WorkbenchSearchDraft>(json, SearchPresetStore.CreateJsonOptions())!;
            Same(history, unavailable, 3, "history/workspace typed draft round-trip");
            var old = Draft(saved) with { Query = SearchQuery.Empty, CombatRewardEditor = null };
            frame.RestoreDraft(old, render: false);
            Check(frame.CaptureDraft().Query.CombatCardExclusion is null, "old draft without collection sidecar remains disabled");

            void Reject(WorkbenchSearchDraft bad, string message)
            {
                bool rejected = false;
                try { SearchPresetCompatibilityResolver.ValidateWorkbenchShape(bad); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, message);
            }
            Reject(Draft(saved with { Cards = Array.Empty<ModelKey>() }), "empty collection cannot become an enabled preset condition");
            Reject(Draft(saved) with { Query = SearchQuery.Empty }, "snapshot cannot silently replace or restore an absent query condition");
            Reject(Draft(saved with { Cards = new[] { BaseGameModelKeys.Characters.Ironclad } }), "non-card members are rejected");
            Reject(Draft(saved with { Cards = new[] { default(ModelKey) } }), "malformed snapshot identities remain invalid input");
            Reject(Draft(saved with { Cards = new[] { keys[0], keys[0] } }), "duplicate snapshot members remain invalid input");
            Reject(Draft(saved) with { CombatRewardEditor = CombatRewardSearchDraft.Empty with { ExcludedCollection = saved with { Cards = null! } } },
                "null snapshot members remain invalid input");
            Reject(Draft(saved) with { Query = SearchQuery.Empty with { CombatCardExclusion = new(3, null!) } },
                "null execution members remain invalid input");
            Reject(Draft(saved, 7), "out-of-scope battle range is rejected");

            var party = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
                SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = Draft(saved, 2).Query },
                    new(1, ModelKeySetFilter.Empty) { Conditions = Draft(unavailable, 5).Query }] }, AncientOptionConditionProfile.BroadDefault)
            { Version = 4, Mode = WorldGameMode.Multiplayer, GameVersion = runtime.Detection.NormalizedVersion, Profile = runtime.Profile.ProfileId,
                Players = [new(0, BaseGameModelKeys.Characters.Ironclad, UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")
                    { CombatRewardEditor = Draft(saved, 2).CombatRewardEditor },
                    new(1, BaseGameModelKeys.Characters.Silent, UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")
                    { CombatRewardEditor = Draft(unavailable, 5).CombatRewardEditor }] };
            frame.RestoreDraft(party, render: false);
            var restoredParty = frame.CaptureDraft();
            for (int seat = 0; seat < 2; seat++)
            {
                var expected = seat == 0 ? saved : unavailable;
                int count = seat == 0 ? 2 : 5;
                Same(Draft(expected, count) with { Query = restoredParty.Query.Players[seat].Conditions,
                    CombatRewardEditor = restoredParty.Players[seat].CombatRewardEditor }, expected, count, "party seat " + seat);
            }
            var partyPreset = presetStore.Save("Party collection search", "", [], frame.CapturePreset());
            frame.ApplyPreset(partyPreset, render: false);
            Check(frame.CaptureDraft().Query.Players[1].Conditions.CombatCardExclusion!.CardKeys.Contains(phantom, ModelKeyComparer.Instance),
                "party preset preserves each seat's missing-card obligation");
            Check(!frame.HasActiveSearch, "all checks leave search stopped");
            Console.WriteLine($"CARD_COLLECTION_WORKBENCH checks={checks};failures=0;fixtures={directory};seedScans=0");
            return 0;
        }
        finally { frame.Free(); }
    }
}
