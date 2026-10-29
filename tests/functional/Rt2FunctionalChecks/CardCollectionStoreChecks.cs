using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Ui.Persistence;

// Temporary files and exact identities only; does not need a running game/catalog.
internal static class CardCollectionStoreChecks
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "RolltheSpire2-card-collections", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException(message);
        }
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or KeyNotFoundException or NotSupportedException)
            { rejected = true; }
            Check(rejected, message);
        }
        try
        {
            var store = new CardCollectionStore(root);
            var unknown = new ModelKey("CARD", "UNKNOWN_MOD_CARD");
            var other = new ModelKey("CARD", "OTHER_CARD");
            var members = new List<ModelKey> { unknown };
            CardCollectionSnapshot first = store.Save(null, "  我的牌集  ", members);
            members.Add(other);
            Check(first.Name == "我的牌集" && first.Revision == 1 && first.Cards.SequenceEqual(new[] { unknown }),
                "New assets normalize the name, start at revision one and detach caller input");
            Check(first.Contains(unknown) && !first.Contains(new ModelKey("CARD", "unknown_mod_card")),
                "Membership uses exact base model identity");
            Reject(() => ((IList<ModelKey>)first.Cards)[0] = other,
                "Returned members are immutable");
            Check(store.TryGet(first.Id, out CardCollectionSnapshot loaded) && loaded.Cards.SequenceEqual(new[] { unknown }),
                "Saved unknown catalog identities remain available");
            CardCollectionSnapshot copy = store.Save(null, first.Name, first.Cards);
            Check(copy.Id != first.Id && copy.Revision == 1 && store.GetAll().Count == 2,
                "Copy creates an independent asset even with the same name");
            CardCollectionSnapshot renamed = store.Save(first.Id, "renamed", first.Cards);
            CardCollectionSnapshot edited = store.Save(first.Id, "renamed", [other]);
            Check(renamed.Revision == 2 && edited.Revision == 3 && edited.Id == first.Id,
                "Rename and edit preserve id and increment revision");
            Check(first.Name == "我的牌集" && first.Revision == 1 && first.Contains(unknown),
                "Previously captured snapshots keep their name, revision and members after edit");
            var reopened = new CardCollectionStore(root);
            Check(reopened.TryGet(copy.Id, out var reloadedCopy) && reloadedCopy.Contains(unknown) &&
                reopened.TryGet(first.Id, out var reloadedEdit) && reloadedEdit.Revision == 3 && reloadedEdit.Contains(other),
                "Card identities, metadata and revisions survive reopen without a catalog");
            CardCollectionSnapshot empty = store.Save(null, "empty", []);
            Check(empty.Cards.Count == 0, "An empty collection can be saved for later editing");
            Reject(() => store.Save(null, " ", []), "Blank names are rejected");
            Reject(() => store.Save(null, "invalid", [default]), "Missing identities are rejected");
            Reject(() => store.Save(null, "invalid", [new ModelKey("RELIC", "UNKNOWN")]), "Non-card members are rejected");
            Reject(() => store.Save(null, "invalid", [new ModelKey("CARD", "A:B")]), "Unserializable identities are rejected");
            Reject(() => store.Save(null, "duplicate", [unknown, unknown]), "Duplicate membership is rejected without dropping authored input");
            Reject(() => store.Save("missing", "missing", []), "An update to a missing asset does not create a replacement");
            Check(store.Delete(copy.Id) && !store.Delete(copy.Id) && !new CardCollectionStore(root).TryGet(copy.Id, out _),
                "Delete persists and missing delete reports false");

            string directory = Path.Combine(root, "card_collections");
            string malformedPath = Path.Combine(directory, "broken.json");
            string malformed = "{not valid json";
            File.WriteAllText(malformedPath, malformed);
            string futurePath = Path.Combine(directory, "future.json");
            string future = "{\"SchemaVersion\":999,\"FutureField\":true}";
            File.WriteAllText(futurePath, future);
            store.Refresh();
            Check(store.LoadIssues.Count == 2 && store.LoadIssues.Any(issue => issue.Contains(malformedPath, StringComparison.Ordinal)) &&
                store.LoadIssues.Any(issue => issue.Contains(futurePath, StringComparison.Ordinal) && issue.Contains("999", StringComparison.Ordinal)),
                "Refresh exposes original read causes and paths while retaining independent valid assets");
            store.Save(first.Id, "still valid", [unknown]);
            Check(File.ReadAllText(malformedPath) == malformed && File.ReadAllText(futurePath) == future,
                "Reading and saving another asset preserve malformed and future originals");

            string livePath = Directory.GetFiles(directory, "*.json").Single(path =>
                path != malformedPath && path != futurePath &&
                JsonNode.Parse(File.ReadAllText(path))!["Id"]!.GetValue<string>() == first.Id);
            string liveBytes = File.ReadAllText(livePath);
            File.WriteAllText(livePath, future);
            Reject(() => store.Save(first.Id, "stale", []), "A stale editor cannot overwrite externally changed future data");
            Reject(() => store.Delete(first.Id), "A stale delete cannot remove externally changed data");
            Check(File.ReadAllText(livePath) == future && store.TryGet(first.Id, out var beforeFailedEdit) && beforeFailedEdit.Name == "still valid",
                "Failed writes preserve both disk and last successful in-memory revision");
            File.WriteAllText(livePath, liveBytes);
            store.Refresh();

            // Deny sharing on the destination, producing an actual filesystem
            // failure without relying on platform-specific ACLs or disk capacity.
            using (var held = new FileStream(livePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Reject(() => store.Save(first.Id, "cannot write", []), "Disk access failure is returned to the caller");
                Reject(() => store.Delete(first.Id), "Disk delete failure is returned to the caller");
            }
            Check(File.ReadAllText(livePath) == liveBytes && store.TryGet(first.Id, out var afterFailedWrite) && afterFailedWrite.Name == "still valid",
                "Disk failure never advances revision or drops the asset");
            Check(Directory.GetFiles(directory, "*.tmp-*").Length == 0,
                "Failed writes leave no loadable temporary assets");

            string blockedRoot = Path.Combine(root, "blocked");
            File.WriteAllText(blockedRoot, "occupied by a file");
            var blockedStore = new CardCollectionStore(blockedRoot);
            Reject(() => blockedStore.Save(null, "cannot create", [unknown]), "Directory creation failures retain their actual exception");
            Check(blockedStore.GetAll().Count == 0 && File.ReadAllText(blockedRoot) == "occupied by a file",
                "Failed initial writes create no in-memory success and preserve the blocking file");
            RunDrawingPreferenceChecks(Path.Combine(root, "drawing-preference"), Check);
            Console.WriteLine($"CARD_COLLECTION_STORE checks={count};failures=0;isolation=passed;frozenInputs=preserved");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void RunDrawingPreferenceChecks(string root, Action<bool, string> check)
    {
        var preference = new DrawingCardCollectionPreferenceStore(root);
        string path = Path.Combine(root, DrawingCardCollectionPreferenceStore.FileName);
        check(preference.Load() is null && !Directory.Exists(root),
            "A missing drawing preference uses the built-in default without creating state");
        preference.Save(null);
        check(new DrawingCardCollectionPreferenceStore(root).Load() is null,
            "An explicit built-in drawing selection survives reopening");

        var card = new ModelKey("CARD", "UNKNOWN_MOD_CARD");
        var other = new ModelKey("CARD", "OTHER_CARD");
        var cards = new List<ModelKey> { card };
        var selected = new CardCollectionSnapshot("collection:selected", "绘线关注", 7, cards)
        { UpdatedAtUtc = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero) };
        preference.Save(selected);
        cards.Add(other);
        CardCollectionSnapshot? loaded = new DrawingCardCollectionPreferenceStore(root).Load();
        check(loaded is not null && loaded.Id == selected.Id && loaded.Name == selected.Name &&
            loaded.Revision == 7 && loaded.UpdatedAtUtc == selected.UpdatedAtUtc && loaded.Cards.SequenceEqual(new[] { card }),
            "Drawing selection roundtrips captured metadata and exact unknown identities, detached from caller mutations");
        check(loaded is not null && !ReferenceEquals(loaded.Cards, preference.Load()!.Cards) &&
            ((IList<ModelKey>)loaded.Cards).IsReadOnly,
            "Each drawing preference load returns a detached immutable member list");

        var assets = new CardCollectionStore(root);
        CardCollectionSnapshot asset = assets.Save(null, "original asset", [card]);
        preference.Save(asset);
        assets.Save(asset.Id, "new revision", [other]);
        assets.Delete(asset.Id);
        loaded = preference.Load();
        check(loaded is not null && loaded.Id == asset.Id && loaded.Name == asset.Name && loaded.Revision == 1 &&
            loaded.Cards.SequenceEqual(new[] { card }),
            "Drawing retains the selected revision after its independent asset is edited and deleted");
        preference.Save(new CardCollectionSnapshot("collection:empty", "empty", 2, []));
        loaded = preference.Load();
        check(loaded is not null && loaded.Id == "collection:empty" && loaded.Cards.Count == 0,
            "An empty custom drawing collection remains a custom selection");
        preference.Save(null);
        check(preference.Load() is null, "Selecting built-in drawing default replaces a previous custom selection");

        void RejectLoad(string content, Func<Exception, bool> expected, string message)
        {
            File.WriteAllText(path, content);
            byte[] before = File.ReadAllBytes(path);
            Exception? failure = null;
            try { _ = preference.Load(); }
            catch (Exception exception) { failure = exception; }
            check(failure is not null && expected(failure) && File.ReadAllBytes(path).SequenceEqual(before), message);
        }
        RejectLoad("{invalid json", exception => exception is JsonException,
            "Malformed drawing preference retains its bytes and original JSON failure");
        RejectLoad("{\"SchemaVersion\":999,\"FutureField\":true}",
            exception => exception is InvalidDataException && exception.Message.Contains("999", StringComparison.Ordinal),
            "Future drawing preference retains bytes and reports its unsupported schema before unknown fields");
        RejectLoad("{\"SchemaVersion\":1}", exception => exception is JsonException,
            "A missing drawing selection field is invalid rather than an implicit default");
        RejectLoad("{\"SchemaVersion\":1,\"Selection\":{\"Id\":\"selected\",\"Name\":\"invalid\",\"Revision\":0,\"Cards\":[]}}",
            exception => exception is InvalidDataException,
            "Invalid drawing snapshot metadata cannot become the built-in default");
        RejectLoad("{\"SchemaVersion\":1,\"Selection\":{\"Id\":\"selected\",\"Name\":\"invalid\",\"Revision\":1,\"Cards\":[\"RELIC:UNKNOWN\"]}}",
            exception => exception is InvalidDataException,
            "Drawing preference rejects non-card identities without changing original data");

        preference.Save(asset);
        byte[] validBytes = File.ReadAllBytes(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Exception? failure = null;
            try { _ = preference.Load(); }
            catch (Exception exception) { failure = exception; }
            check(failure is IOException, "Drawing preference read access failure reaches its caller without fallback");
            failure = null;
            try { preference.Save(null); }
            catch (Exception exception) { failure = exception; }
            Console.WriteLine($"DRAWING_PREFERENCE_EXPECTED_SAVE_FAILURE type={failure?.GetType().FullName};cause={failure?.Message}");
            check(failure is IOException or UnauthorizedAccessException,
                "Drawing preference atomic write failure reaches its caller with the original filesystem cause: " + failure?.GetType().FullName);
        }
        check(File.ReadAllBytes(path).SequenceEqual(validBytes) && preference.Load()!.Id == asset.Id &&
            Directory.GetFiles(root, "*.tmp-*").Length == 0,
            "Failed drawing preference writes preserve the last committed selection and clean temporary files");
    }
}
