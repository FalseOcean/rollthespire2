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
            Console.WriteLine($"CARD_COLLECTION_STORE checks={count};failures=0;isolation=passed;frozenInputs=preserved");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
