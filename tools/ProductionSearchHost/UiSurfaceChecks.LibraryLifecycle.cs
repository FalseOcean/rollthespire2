using System.Reflection;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    // Called by an initialized native UI host; no alternate off-tree execution
    // path, real search, or production-state write is involved.
    internal static async Task RunLibraryLifecycleChecksAsync(Node host, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, flags)!.GetValue(instance)!;
        int checks = 0;
        void Check(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException("LIBRARY_LIFECYCLE " + message); }
        async Task Frames(int count)
        { for (int i = 0; i < count; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Ready(SeedLibraryCanvas library)
        {
            for (int i = 0; i < 1200 && !library.IsPresentationReady; i++) await Frames(1);
            Check(library.IsPresentationReady, "view reaches terminal presentation over actual frames");
            Check(!library.HasPresentationFailure, "completed view did not fail during presentation");
        }

        string state = Path.Combine(output, "library-lifecycle-state", Guid.NewGuid().ToString("N"));
        var persistence = new SearchWorkspace(state, false);
        var entries = Enumerable.Range(0, 96).Select(i => new SeedLibraryEntry("lifecycle:" + i.ToString("D3"),
            SeedLibrarySource.Developer, "LIFECYCLE" + i.ToString("D3"), "", null, DateTimeOffset.UtcNow.AddSeconds(-i))
            { Title = "Lifecycle " + i.ToString("D3") }).ToArray();
        var store = new SeedLibraryStore(state, entries);
        var library = new SeedLibraryCanvas(store, persistence);
        Button[] Rows() => library.FindChildren("*", "Button", true, false).OfType<Button>()
            .Where(button => button.HasMeta("entry_id")).ToArray();
        var surface = new Control { Size = new Vector2(1500, 800) };
        host.AddChild(surface);
        try
        {
            Check(Rows().Length == 0 && !library.IsPresentationReady, "constructor queues first list rather than building rows off-tree");
            surface.AddChild(library); library.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            library.AttachOverlay(surface);
            await Frames(3);
            Check(Rows().Length < entries.Length && !library.IsPresentationReady, "initial list is bounded across real frames");
            library.Hide();
            int pausedRows = Rows().Length;
            await Frames(4);
            Check(Rows().Length == pausedRows && !library.IsPresentationReady, "hidden view pauses unfinished work");
            var filter = Field<LineEdit>(library, "_filter");
            filter.Text = "Lifecycle 095";
            filter.EmitSignal(LineEdit.SignalName.TextChanged, filter.Text);
            filter.Text = "Lifecycle 094";
            filter.EmitSignal(LineEdit.SignalName.TextChanged, filter.Text);
            library.Show(); await Ready(library);
            Check(Rows().Length == 1 && Rows()[0].GetMeta("entry_id").AsString() == "lifecycle:094",
                "superseded filter work never appends stale rows");
            filter.Text = ""; filter.EmitSignal(LineEdit.SignalName.TextChanged, filter.Text); await Ready(library);
            var rowIds = Rows().Select(row => row.GetInstanceId()).ToArray();
            Rows().Single(row => row.GetMeta("entry_id").AsString() == "lifecycle:090").EmitSignal(BaseButton.SignalName.Pressed);
            await Ready(library);
            Check(Rows().Select(row => row.GetInstanceId()).SequenceEqual(rowIds) &&
                Rows().Single(row => row.GetMeta("selected").AsBool()).GetMeta("entry_id").AsString() == "lifecycle:090",
                "detail selection retains every row instance and selected identity");

            var context = new SeedLibraryContext("0.111.0", Beta111Profile.Instance.ProfileId,
                WorldGameMode.Singleplayer, 0, []);
            library.OpenSave("LIFECYCLE094", context, "authored note", initialTitle: "authored title");
            var note = Field<SeedLibraryNoteOverlay>(library, "_note");
            Field<LineEdit>(note, "_titleInput").Text = "uncommitted title";
            Field<TextEdit>(note, "_note").Text = "uncommitted note";
            library.Refresh("en");
            await Frames(4);
            Check(note.IsOpen && Field<LineEdit>(note, "_titleInput").Text == "uncommitted title" &&
                Field<TextEdit>(note, "_note").Text == "uncommitted note" && !library.IsPresentationReady,
                "refresh preserves modal draft and pauses its presentation");
            library.CloseModal(); await Ready(library);
            Check(filter.Text.Length == 0 && Rows().Single(row => row.GetMeta("selected").AsBool())
                .GetMeta("entry_id").AsString() == "lifecycle:090", "language refresh preserves filter and selection");

            var conditions = SearchQuery.Empty with { RelicSequenceConstraints = Enumerable.Range(1, 40)
                .Select(i => new RelicSequenceSearchCondition(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, i,
                    new([BaseGameModelKeys.Relics.GoldenPearl], [], []))).ToArray() };
            var draft = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 0,
                conditions, AncientOptionConditionProfile.BroadDefault);
            var query = new QueryHistoryEntry { Id = "lifecycle-history", QueryKey = "lifecycle-query", Draft = draft,
                RunCount = 1, LastUsedAtUtc = DateTimeOffset.UtcNow,
                Results = Enumerable.Range(0, 80).Select(i => new PersistedSearchResult { Seed = "HISTORY" + i.ToString("D3") }).ToList() };
            persistence.Workspace.QueryHistory.Add(query);
            library.ShowSection(SeedLibraryCanvas.LibrarySection.History);
            for (int i = 0; i < 400 && library.FindChild("LibraryConditionSummary", true, false) is null; i++) await Frames(1);
            await Frames(1);
            var summary = library.FindChild("LibraryConditionSummary", true, false);
            Check(summary is not null && !library.IsPresentationReady &&
                summary.FindChildren("*", "Label", true, false).Count < 40, "large authored condition summary is also bounded across frames");
            await Ready(library);
            Check(summary!.FindChildren("*", "Label", true, false).OfType<Label>().Count(label => label.Text.StartsWith("R · ", StringComparison.Ordinal)) == 40,
                "resumable summary preserves every authored condition line");
            ((Button)library.FindChild("HistoryResults", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(3);
            Check(!library.IsPresentationReady && library.FindChildren("HistoryResultRow*", "HBoxContainer", true, false).Count < query.Results.Count,
                "history result controls are consumed in bounded batches");
            library.ShowSection(SeedLibraryCanvas.LibrarySection.Favorites); await Ready(library);
            Check(Rows().Length == entries.Length && library.FindChildren("HistoryResultRow*", "HBoxContainer", true, false).Count == 0,
                "section switch cancels pending detail rows without losing favorites");
            library.ShowSection(SeedLibraryCanvas.LibrarySection.History); await Ready(library);
            var resultRows = library.FindChildren("HistoryResultRow*", "HBoxContainer", true, false);
            Check(resultRows.Count == query.Results.Count && resultRows[0].Name == "HistoryResultRowHISTORY079" &&
                resultRows[resultRows.Count - 1].Name == "HistoryResultRowHISTORY000", "all saved result identities arrive in original newest-first order");

            library.RefreshEntries();
            surface.RemoveChild(library);
            Check(Field<object?>(library, "_presentation") is null && !library.IsPresentationReady &&
                Field<object?>(library, "_saveInformation") is null && Field<object?>(library, "_note") is null,
                "ExitTree releases queued data and modal references");
            library.QueueFree(); await Frames(3);
            Check(!GodotObject.IsInstanceValid(note), "view retirement releases owned sibling overlay");
            Console.WriteLine($"LibraryLifecycle checks={checks};PASS;deferredConstructor;boundedFrames;supersession;hide;selection;modalDraft;history;retirement");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(library)) library.QueueFree();
            surface.QueueFree();
        }
    }
}
