using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static class UnknownPrefixEditorChecks
{
    internal static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("SharedUnknownPrefixEditor");
        string path = Path.Combine(System.Environment.CurrentDirectory, ".build-evidence", "unknown-prefix-editor", Guid.NewGuid().ToString("N"));
        var store = new SearchWorkspace(path, false);
        var frame = new QueryWorkbenchFrame(runtime, store) { Size = new(1536, 772) };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Private(object owner, string name) => owner.GetType().GetField(name, flags)!.GetValue(owner)!;
        int checks = 0;
        void Check(bool ok, string reason) { checks++; if (!ok) throw new InvalidDataException("UnknownPrefixEditor." + reason); }
        var draft = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 2 }, AncientOptionConditionProfile.BroadDefault);
        try
        {
            frame.RestoreDraft(draft, render: false);
            var events = (EventSearchEditor)Private(frame, "_eventEditor");
            var map = (ActInformationSearchEditor)Private(frame, "_actInformationEditor");
            Check(ReferenceEquals(events.UnknownRoomPrefix, Private(map, "_unknownRoomPrefix")), "BothPagesBindOneState");
            Check(frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == 2, "OldEventQueryUnchanged");
            map.SelectPage(true);
            typeof(ActInformationSearchEditor).GetMethod("Render", flags)!.Invoke(map, []);
            map.FindChildren("MapUnknownRoomPrefix", "OptionButton", true, false).OfType<OptionButton>().Single()
                .EmitSignal(OptionButton.SignalName.ItemSelected, 6L);
            var changed = frame.CaptureDraft();
            Check(changed.Query.FirstActUnknownMonsterPrefixCount == 3 && changed.Query.FirstActUnknownEventPrefixCount == 0 &&
                events.UnknownRoomPrefix.Selection == 6, "MapEditImmediatelyUpdatesEventOwner");
            Check(SearchQueryProjection.CountConditions(changed.Query) == 1 && map.MapConditionCount() == 1 && events.AuthoredConditionCount() == 1,
                "TwoPageBadgesOneQueryPredicate");
            typeof(EventSearchEditor).GetMethod("Render", flags)!.Invoke(events, []);
            events.FindChildren("FirstActUnknownEventPrefix", "OptionButton", true, false).OfType<OptionButton>().Single()
                .EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Check(events.UnknownRoomPrefix.EventCount == 1 && events.UnknownRoomPrefix.MonsterCount == 0 &&
                ((UnknownRoomPrefixEditorState)Private(map, "_unknownRoomPrefix")).Selection == 1,
                "EventEditImmediatelyUpdatesMapState");
            foreach (string page in new[] { "map", "events" })
            {
                frame.RestoreDraft(changed, render: false);
                typeof(QueryWorkbenchFrame).GetField("_selectedDomain", flags)!.SetValue(frame, page);
                frame.ClearCategoryConditions();
                var cleared = frame.CaptureDraft();
                Check(cleared.Query.FirstActUnknownMonsterPrefixCount == 0 && cleared.Query.FirstActUnknownEventPrefixCount == 0,
                    "EitherPageClearClearsSharedPredicate:" + page);
                frame.UndoClearConditions();
                Check(frame.CaptureDraft().Query.FirstActUnknownMonsterPrefixCount == 3 && frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == 0,
                    "EitherPageUndoRestoresOnce:" + page);
            }
            frame.ClearAllConditions();
            Check(frame.CaptureDraft().Query.FirstActUnknownMonsterPrefixCount == 0, "ClearAllOnce");
            frame.UndoClearConditions();
            Check(frame.CaptureDraft().Query.FirstActUnknownMonsterPrefixCount == 3, "UndoAllOnce");
            store.SaveWorkbench(changed); store.FlushAll();
            var saved = new SearchWorkspace(path, false).LoadWorkbench()!;
            frame.RestoreDraft(saved, render: false);
            Check(frame.CaptureDraft().Query.FirstActUnknownMonsterPrefixCount == 3 &&
                JsonSerializer.Serialize(frame.CaptureDraft().Query) == JsonSerializer.Serialize(changed.Query), "PersistenceNoSecondCopy");
            var conflict = draft with { Query = draft.Query with { FirstActUnknownMonsterPrefixCount = 3 } };
            frame.RestoreDraft(conflict, render: false);
            Check(frame.CaptureDraft().Query.FirstActUnknownEventPrefixCount == 2 &&
                frame.CaptureDraft().Query.FirstActUnknownMonsterPrefixCount == 3 && events.UnknownRoomPrefix.Selection == 7,
                "ImportedContradictionRetainedAndVisible");
            Console.WriteLine($"UNKNOWN_PREFIX_EDITOR=PASS;checks={checks};mapEventShared=true;clearUndo=true;legacyRestore=true");
            return 0;
        }
        finally { frame.Free(); }
    }
}
