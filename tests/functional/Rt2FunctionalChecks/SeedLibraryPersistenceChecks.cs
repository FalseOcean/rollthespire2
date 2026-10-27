using RolltheSpire2.Ui.Workspace;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;

// Fixed data and temporary directories only; no Godot host, prediction or game state.
internal static class SeedLibraryPersistenceChecks
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "RolltheSpire2-seed-library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException(message);
        }
        try
        {
            var store = new SeedLibraryStore(root, []);
            var solo = Context(1);
            foreach (var disposition in Enum.GetValues<SearchDisposition>())
            {
                string expected = disposition switch {
                    SearchDisposition.Match => "workflow.verified", SearchDisposition.Unknown => "workflow.validation_unknown",
                    SearchDisposition.Unsupported => "workflow.validation_unsupported", SearchDisposition.NoMatch => "workflow.validation_mismatch",
                    SearchDisposition.Cancelled => "workflow.validation_cancelled", _ => "workflow.unverified" };
                Check(SearchResultValidationDisplay.StatusKey(disposition) == expected,
                    "Manual receipt projects its actual disposition through the common status API");
            }
            foreach (bool unverified in new[] { false, true })
                foreach (bool explicitNull in new[] { false, true })
                {
                    string json = "{\"IsUnverified\":" + (unverified ? "true" : "false") +
                        (explicitNull ? ",\"ValidationDisposition\":null}" : "}");
                    var old = JsonSerializer.Deserialize<PersistedSearchResult>(json, SeedLibraryStore.CreateJsonOptions())!;
                    Check(old.ValidationDisposition is null && old.IsUnverified == unverified &&
                        SearchResultValidationDisplay.StatusKey(old) == (unverified ? "workflow.unverified" : "workflow.verified"),
                        "Missing or null old JSON disposition retains the historical IsUnverified display fallback");
                }
            CheckResultProvenance(root, solo, Check);
            CheckHistoricalEnvironmentProvenance(root, solo, Check);
            var saved = store.Save(" o1abcdef2345 ", solo, "first note");
            Check(saved.Seed == "01ABCDEF2345" && saved.Source == SeedLibrarySource.User, "Seed uses profile canonicalization");
            var reopened = new SeedLibraryStore(root, []).GetAll().Single();
            string oldFile = Directory.GetFiles(Path.Combine(root, "seed_library"), "*.json").Single();
            var oldDocument = JsonNode.Parse(File.ReadAllText(oldFile))!.AsObject();
            oldDocument.Remove("title"); oldDocument.Remove("Title");
            File.WriteAllText(oldFile, oldDocument.ToJsonString());
            var untitled = new SeedLibraryStore(root, []).GetAll().Single();
            Check(untitled.Title == "" && untitled.DisplayTitle == saved.Seed && untitled.Note == "first note",
                "Older favorites without titles preserve notes and use the seed as their display title");
            Check(reopened.CanOpen && reopened.Context!.Players[0].Character == solo.Players[0].Character &&
                reopened.Context.Players[0].AncientPremises == solo.Players[0].AncientPremises && reopened.Note == "first note",
                "Solo intent and note survive reopen");
            Check(reopened.Context!.Players[0].OpeningPremise.OpeningRoute is null &&
                reopened.Context.Players[0].Selection is { ChoiceSlot: 2, Explicit: true, Route: "fixture-route-0" },
                "Solo explicit choice retains its meaning without carrying Search targets");
            var updated = store.Save(saved.Seed, solo, "updated");
            Check(updated.Id == saved.Id && store.GetAll().Count == 1, "Same seed and context update one entry");
            store.Save(saved.Seed, solo, "linked", "query-a");
            store.Save(saved.Seed, solo, "linked again", "query-b");
            store.Save(saved.Seed, solo, "note-only update");
            var linked = new SeedLibraryStore(root, []).GetAll().Single();
            Check(linked.QueryKeys.SequenceEqual(new[] { "query-a", "query-b" }), "Multiple source query links survive note edits and restart");
            Check(linked.QueryAssociations.All(a => a.Kind == SeedQueryAssociationKind.UserAssigned), "Manual links never imply search validation");
            var evidence = new SeedQueryAssociation("query-b", SeedQueryAssociationKind.VerifiedFromSearch, "environment-a", "record-a");
            store.Save(saved.Seed, solo, "verified in recorded environment", "query-b", evidence);
            store.SetQueryLinks(saved.Id, ["query-b"]);
            Check(new SeedLibraryStore(root, []).GetAll().Single().QueryKeys.SequenceEqual(new[] { "query-b" }), "Removing a query link preserves the favorite");
            Check(new SeedLibraryStore(root, []).GetAll().Single().QueryAssociations.Contains(evidence), "Historical match evidence survives bookmark edits and reopen");
            var titled = store.UpdateInformation(saved.Id, "  珍珠开局 🌱  ", "中文备注\nsecond line");
            var titledReloaded = new SeedLibraryStore(root, []).GetAll().Single();
            Check(titledReloaded.Title == "珍珠开局 🌱" && titledReloaded.Note == "中文备注\nsecond line" &&
                titledReloaded.Id == saved.Id && titledReloaded.QueryAssociations.Contains(evidence) && titled.RawContextJson == saved.RawContextJson,
                "Title and note edits persist without changing identity, context or source evidence");
            var repeated = store.Save(saved.Seed, solo, "saved again");
            Check(repeated.Id == saved.Id && repeated.Title == titledReloaded.Title, "Saving an existing seed without a title retains its chosen title");
            Check(store.UpdateInformation(saved.Id, "", "kept").DisplayTitle == saved.Seed,
                "Clearing a title restores the seed-code label");
            var sameRoute = solo with { Players = [solo.Players[0] with { Selection = solo.Players[0].Selection! with { ChoiceSlot = 3 } }] };
            Check(store.Save(saved.Seed, sameRoute, "same route").Id == saved.Id,
                "Concrete route identity takes precedence over redundant display slot");
            var neutral = solo with { Players = [solo.Players[0] with { OpeningPremise = SearchQuery.Empty, Selection = null }] };
            var neutralEntry = store.Save("555555555555", neutral, "neutral");
            var displayed = neutral with { Players = [neutral.Players[0] with { Selection = new(1, "display-default", false) }] };
            Check(store.Save("555555555555", displayed, "displayed").Id == neutralEntry.Id,
                "Uncommitted display choice does not create a second context");

            var authority = RuntimeAuthoritySnapshot.Unavailable("fixture", "fixture") with
            {
                SemanticUniverse = RuntimeAuthorityDomains.Schema1SemanticDomains.Select(domain => new RuntimeSemanticDomainSnapshot(
                    domain, (domain switch
                    {
                        RuntimeAuthorityDomains.Characters => new[] { BaseGameModelKeys.Characters.Silent, BaseGameModelKeys.Characters.Ironclad },
                        RuntimeAuthorityDomains.Relics => new[] { BaseGameModelKeys.Relics.GoldenPearl },
                        _ => Array.Empty<ModelKey>()
                    }).Select(key => new RuntimeSemanticItem(key.Serialized, [])).ToArray(), true, "fixture")).ToArray()
            };
            SeedLibraryStore.ValidateRuntimeReferences(solo, authority);
            bool missingRejected = false;
            var missingBase = Context(2);
            var missing = missingBase with { Players = [missingBase.Players[0] with { OpeningPremise = SearchQuery.Empty with
                { OpeningRoute = new(new("RELIC", "MISSING_FIXTURE_RELIC")) } }, missingBase.Players[1]] };
            try { SeedLibraryStore.ValidateRuntimeReferences(missing, authority); }
            catch (InvalidDataException ex) { missingRejected = ex.Message.Contains("MISSING_FIXTURE_RELIC"); }
            Check(missingRejected, "Missing opening identity reports an actionable reference failure");

            foreach (int players in new[] { 2, 4 })
            {
                var party = Context(players);
                var entry = store.Save(saved.Seed, party, players + "P");
                var viewAnotherSeat = store.Save(saved.Seed, party with { SelectedSlot = players - 1 }, "seat changed");
                Check(entry.Id == viewAnotherSeat.Id, "Selected display seat is not a duplicate context");
                var loaded = new SeedLibraryStore(root, []).GetAll().Single(e => e.Id == entry.Id).Context!;
                Check(loaded.Mode == WorldGameMode.Multiplayer && loaded.Players.Count == players && loaded.SelectedSlot == players - 1,
                    "Whole party context survives restart");
                for (int slot = 0; slot < players; slot++)
                    Check(loaded.Players[slot].Slot == slot && loaded.Players[slot].Character == party.Players[slot].Character &&
                        loaded.Players[slot].UnlockSource == "Fixture-" + slot && loaded.Players[slot].Selection!.Route == "fixture-route-" + slot,
                        "Slot ownership, unlock provenance and opening selection remain distinct");
                var different = party with { Ascension = 1 };
                Check(store.Save(saved.Seed, different, "other ascension").Id != entry.Id,
                    "Different context retains separate seed entry");
            }

            var mutable = Context(2);
            var slots = (SeedLibraryPlayer[])mutable.Players;
            var detached = store.Save("222222222222", mutable, "detach");
            slots[0] = slots[0] with { Character = BaseGameModelKeys.Characters.Necrobinder };
            Check(detached.Context!.Players[0].Character != slots[0].Character, "Saved roster detaches from the active draft");
            var unsupported = solo with { Players = [solo.Players[0] with { OpeningPremise = SearchQuery.Empty with
                { RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1,
                    new([], [BaseGameModelKeys.Relics.GoldenPearl], []))] } }] };
            bool rejected = false;
            try { store.Save("333333333333", unsupported, "invalid"); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Search predicates cannot enter seed-library opening context");
            rejected = false;
            var ignoredLegacy = Context(2);
            ignoredLegacy = ignoredLegacy with { Players = [ignoredLegacy.Players[0] with { OpeningPremise = SearchQuery.Empty with
                { LegacyNeow = LegacyNeowSemanticConstraints.Empty with { RequireSmallCapsule = true } } }, ignoredLegacy.Players[1]] };
            try { store.Save("333333333333", ignoredLegacy, "invalid legacy"); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Ignored legacy N flags cannot masquerade as a supported opening context");
            var soloTargets = solo with { Players = [solo.Players[0] with { OpeningPremise = SearchQuery.Empty with
                { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones), OpeningRouteRelicRequirement = new(
                    BaseGameModelKeys.Relics.NeowsBones, [BaseGameModelKeys.Relics.NewLeaf, BaseGameModelKeys.Relics.LeafyPoultice], BonesRouteOrderMode.ExactOrder) },
                Selection = null }] };
            var authored = store.Save("333333333333", soloTargets, "authored opening, no witness");
            var authoredReloaded = new SeedLibraryStore(root, []).GetAll().Single(e => e.Id == authored.Id);
            Check(authoredReloaded.CanOpen && authoredReloaded.Context!.Players[0].Selection is null &&
                authoredReloaded.Context.Players[0].OpeningPremise.OpeningRouteRelicRequirement!.RequiredRelicKeys.SequenceEqual(
                    soloTargets.Players[0].OpeningPremise.OpeningRouteRelicRequirement!.RequiredRelicKeys),
                "Solo authored Bones order survives persistence separately from a Match witness");

            string protect = Path.Combine(root, "protection");
            var protectedStore = new SeedLibraryStore(protect, []);
            protectedStore.Save(saved.Seed, solo, "preserve");
            string file = Directory.GetFiles(Path.Combine(protect, "seed_library"), "*.json").Single();
            string original = File.ReadAllText(file);
            var future = JsonNode.Parse(original)!;
            future["SchemaVersion"] = 99;
            string futureRaw = future.ToJsonString();
            File.WriteAllText(file, futureRaw);
            protectedStore = new SeedLibraryStore(protect, []);
            Check(protectedStore.GetAll().Count == 0 && protectedStore.LoadIssues.Count == 1, "Future schema signals protected load issue");
            protectedStore.Save("444444444444", solo, "new entry");
            Check(File.ReadAllText(file) == futureRaw, "New saves never overwrite future-schema bytes");

            var unknown = JsonNode.Parse(original)!;
            unknown["Context"]!["FuturePremise"] = true;
            string contextRaw = unknown["Context"]!.ToJsonString();
            File.WriteAllText(file, unknown.ToJsonString());
            protectedStore = new SeedLibraryStore(protect, []);
            var unavailable = protectedStore.GetAll().Single(e => e.Seed == saved.Seed);
            Check(!unavailable.CanOpen && unavailable.Context is null && unavailable.Issue.Length > 0,
                "Unknown context cannot masquerade as a complete seed entry");
            protectedStore.UpdateNote(unavailable.Id, "note-only change");
            Check(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(file))!["Context"], JsonNode.Parse(contextRaw)),
                "Note edits preserve unknown authored context");
            File.WriteAllText(file, "{broken");
            protectedStore = new SeedLibraryStore(protect, []);
            Check(protectedStore.LoadIssues.Count == 1 && File.ReadAllText(file) == "{broken", "Corrupt document remains intact");

            var malicious = JsonNode.Parse(original)!;
            malicious["Id"] = "user:../outside";
            File.WriteAllText(file, malicious.ToJsonString());
            protectedStore = new SeedLibraryStore(protect, []);
            protectedStore.UpdateNote("user:../outside", "safe loaded path");
            Check(File.ReadAllText(file).Contains("safe loaded path") && !File.Exists(Path.Combine(protect, "outside.json")),
                "External identity is never treated as a filesystem path");
            Check(!protectedStore.DeleteUser("developer:example"), "User deletion cannot target developer collection");
            Check(protectedStore.DeleteUser("user:../outside") && !File.Exists(file), "Deletion uses the actual loaded file path");

            var recommendations = new DeveloperSeedLibraryProvider();
            var recommended = recommendations.GetEntries().Single(e => e.Seed == "R93J1P8CQA34");
            Check(recommendations.LoadIssues.Count == 0 && recommended.CanOpen &&
                recommended.TitleFor("zh") == "双人黑拥添柴无惧" && recommended.TitleFor("en").Contains("Two Ironclads"),
                "Owner-provided bilingual recommendation loads from embedded resources");
            Check(recommended.Context is { Mode: WorldGameMode.Multiplayer, Ascension: 10, Players.Count: 2 } &&
                recommended.Context.Players.All(p => p.Character == BaseGameModelKeys.Characters.Ironclad &&
                    p.OpeningPremise.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.LeafyPoultice),
                "Recommendation carries the complete two-Ironclad Leafy opening configuration");
            Check(recommended.FavoriteNote("zh").Contains("游玩说明") && recommended.FavoriteNote("en").Contains("How to play") &&
                recommended.QueryAssociations.Count == 0, "Play instructions are copied as notes, not fabricated Exact evidence");
            var fallback = new DeveloperSeedText { Zh = "说明" };
            Check(fallback.Resolve("en") == "说明", "Missing English has readable fallback");

            string recommendationText = File.ReadAllText("DeveloperSeeds/two-ironclads-stoke.json");
            var recommendationData = JsonNode.Parse(recommendationText)!.AsObject();
            Check(!recommendationData.ContainsKey("Title") && !recommendationData.ContainsKey("Note") &&
                !recommendationData["DeveloperDetails"]!.AsObject().ContainsKey("Title") &&
                !recommendationData["DeveloperDetails"]!.AsObject().ContainsKey("Description") &&
                !recommendationData["DeveloperDetails"]!.AsObject().ContainsKey("Instructions") &&
                recommended.Note == "这颗种子中，两位铁甲战士在变形灵林谷变化初始牌，可以得到相同的黑暗之拥与无惧疼痛。",
                "Developer data excludes presentation text while localized join preserves the distinct raw fallback note");
            // An arbitrary factory is still permitted to supply an ordinary player
            // export with literal text, independently of the bundled text catalog.
            recommendationData["Title"] = "Fixture title";
            recommendationData["Note"] = "Fixture note";
            recommendationData["DeveloperDetails"]!["Title"] = JsonSerializer.SerializeToNode(
                new DeveloperSeedText { Zh = "Fixture Chinese title", En = "Fixture English title" });
            recommendationText = recommendationData.ToJsonString();
            string resourceName = DeveloperSeedLibraryProvider.ResourcePrefix + "two-ironclads-stoke.json";
            var invalidDetails = JsonNode.Parse(recommendationText)!;
            invalidDetails["DeveloperDetails"]!["Title"] = null;
            var partial = new DeveloperSeedLibraryProvider(new (string, Func<string>)[] {
                (resourceName, () => recommendationText),
                (DeveloperSeedLibraryProvider.ResourcePrefix + "broken.json", () => "{broken"),
                (DeveloperSeedLibraryProvider.ResourcePrefix + "invalid-details.json", () => invalidDetails.ToJsonString()),
                (resourceName, () => recommendationText)
            });
            Check(partial.GetEntries().Count == 1 && partial.LoadIssues.Count == 3,
                "Malformed, invalid-metadata and duplicate bundled entries remain local and visible as load issues");
            Check(partial.GetEntries().Single().Title == "Fixture title" &&
                partial.GetEntries().Single().Note == "Fixture note" &&
                partial.GetEntries().Single().TitleFor("en") == "Fixture English title",
                "Custom developer resource factories preserve their own literal fallback and bilingual text");

            var copies = new SeedLibraryStore(Path.Combine(root, "developer-copy"), recommendations.GetEntries());
            var personalCopy = copies.Save(recommended.Seed, recommended.Context!, recommended.FavoriteNote("zh"), title: recommended.TitleFor("zh"));
            copies.UpdateInformation(personalCopy.Id, "我的双铁甲", "自己的备注");
            var afterRestart = new SeedLibraryStore(Path.Combine(root, "developer-copy"), recommendations.GetEntries());
            var own = afterRestart.GetAll().Single(e => e.Source == SeedLibrarySource.User);
            Check(own.Title == "我的双铁甲" && own.Note == "自己的备注" && own.Context!.Players.Count == 2 &&
                own.DeveloperDetails is null && own.QueryAssociations.Count == 0,
                "Personal copy survives restart independently of the read-only recommendation");
            Check(JsonSerializer.Serialize(own.Context, SeedLibraryStore.CreateJsonOptions()) ==
                JsonSerializer.Serialize(recommended.Context, SeedLibraryStore.CreateJsonOptions()), "Copy preserves the entire party context");
            Check(afterRestart.TryFind(recommended.Seed, recommended.Context!, out var existingCopy) && existingCopy.Id == personalCopy.Id,
                "Repeated favorite detects the existing personal entry");
            Check(!afterRestart.DeleteUser(recommended.Id) && afterRestart.DeleteUser(personalCopy.Id) &&
                afterRestart.GetAll().Single().TitleFor("zh") == "双人黑拥添柴无惧", "Personal edits/deletion never change the bundled entry");
            Console.WriteLine($"PASS seed-library-persistence checks={count}; fixtures={root}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL seed-library-persistence after={count}; fixtures={root}; {ex}");
            return 1;
        }
    }

    private static void CheckResultProvenance(string directory, SeedLibraryContext context, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "result-provenance");
        var store = new SeedLibraryStore(path, []);
        var scope = new RequestSupportAssessment(RequestSupportScopeState.Unknown, ["LoadedModsUnknown"],
            RequestSupportEvidence.Unknown, "0.111.0", "UnderDeclaredPlayerOperations");
        var oldMatch = new SeedQueryAssociation("query-a", SeedQueryAssociationKind.VerifiedFromSearch, "environment-a", "record-a");
        var match = new SeedLibraryResultProvenance(false, false, SearchDisposition.Match, "", scope, "query-a", "record-a")
            { TreasureSources = [new("RELIC:JUZU_BRACELET", 1, null, true), new("RELIC:GOLDEN_PEARL", 2, 2, false)] };
        var saved = store.Save("111111111111", context, "confirmed old observation", association: oldMatch, resultProvenance: match);
        check(new SeedLibraryStore(path, []).GetAll().Single().ResultProvenance!.TreasureSources.SequenceEqual(match.TreasureSources),
            "Favorite save/reload retains both opening and encountered-chest target receipts");
        foreach (var disposition in new[] { SearchDisposition.NoMatch, SearchDisposition.Unknown, SearchDisposition.Unsupported,
                     SearchDisposition.Cancelled, SearchDisposition.NotEvaluatedByPolicy })
        {
            var origin = new SeedQueryAssociation("query-b", SeedQueryAssociationKind.UnverifiedFromSearch, "environment-b", "record-b");
            var attempt = new SeedLibraryResultProvenance(true, disposition == SearchDisposition.NotEvaluatedByPolicy,
                disposition, "actual-cause:" + disposition, scope, "query-b", "record-b");
            var latest = store.Save(saved.Seed, context, "latest observation", association: origin, resultProvenance: attempt);
            var reopened = new SeedLibraryStore(path, []).GetAll().Single();
            check(latest.Id == saved.Id && reopened.ResultProvenance?.ValidationDisposition == disposition &&
                reopened.ResultProvenance.ValidationIssue == attempt.ValidationIssue && reopened.ResultProvenance.IsUnverified,
                "Favorite retains actual unresolved/mismatched/cancelled/skipped disposition and cause after reopen");
            check(reopened.QueryAssociations.Contains(oldMatch) && reopened.QueryAssociations.Contains(origin) &&
                reopened.QueryAssociations.All(a => a.QueryKey != "query-b" || a.Kind != SeedQueryAssociationKind.VerifiedFromSearch),
                "Unverified bookmark does not claim new query satisfaction or erase a historical model match");
            check(reopened.ResultProvenance!.SupportScope?.BaselineGameVersion == "0.111.0" &&
                reopened.ResultProvenance.SupportScope.Evidence.ModInspectionEvidence == "LoadedModsNotInspected" &&
                reopened.ResultProvenance.SourceQueryKey == "query-b" && reopened.ResultProvenance.SearchRecordId == "record-b",
                "Favorite preserves frozen request scope and actual source attempt provenance");
            string expectedKey = disposition switch { SearchDisposition.NoMatch => "workflow.validation_mismatch",
                SearchDisposition.Unknown => "workflow.validation_unknown", SearchDisposition.Unsupported => "workflow.validation_unsupported",
                SearchDisposition.Cancelled => "workflow.validation_cancelled", _ => "workflow.unfiltered" };
            check(SearchResultValidationDisplay.StatusKey(reopened.ResultProvenance) == expectedKey,
                "Favorite badge distinguishes mismatch from incomplete checking and raw enumeration");
        }
        var current = store.UpdateInformation(saved.Id, "new title", "new note");
        check(current.ResultProvenance?.ValidationDisposition == SearchDisposition.NotEvaluatedByPolicy,
            "Editing bookmark notes does not reinterpret its validation provenance");
        var incompatible = store.Save(saved.Seed, context, "unresolved", association: oldMatch,
            resultProvenance: match with { IsUnverified = true, ValidationDisposition = SearchDisposition.Unknown, ValidationIssue = "missing-model-input" });
        check(incompatible.QueryAssociations.Any(a => a.SearchRecordId == oldMatch.SearchRecordId && a.Kind == SeedQueryAssociationKind.UnverifiedFromSearch),
            "Known non-Match observation cannot create a new verified association even if its caller supplies one");
        var historical = saved.ResultProvenance! with { SourceEnvironmentFingerprint = oldMatch.EnvironmentFingerprint };
        var changedContext = context with { Ascension = context.Ascension == 0 ? 1 : context.Ascension - 1 };
        var changed = store.Save("222222222222", changedContext, "changed prediction", association: oldMatch, resultProvenance: historical);
        var changedReloaded = new SeedLibraryStore(path, []).GetAll().Single(e => e.Id == changed.Id);
        check(changedReloaded.ResultProvenance is { AppliesToCurrentContext: false, ValidationDisposition: SearchDisposition.Match,
                SourceSeed: "111111111111", SourceEnvironmentFingerprint: "environment-a", SearchRecordId: "record-a" } &&
            changedReloaded.QueryAssociations.Count == 0,
            "Changed seed/context preserves the original result as history without borrowing its verified query association");
        check(changedReloaded.ResultProvenance!.TreasureSources.SequenceEqual(match.TreasureSources),
            "Changed bookmark context preserves its historical source receipts without making a new current-context claim");
        store.DeleteUser(changed.Id);
        string file = Directory.GetFiles(Path.Combine(path, "seed_library"), "*.json").Single();
        var legacy = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        legacy["SchemaVersion"] = 1; legacy.Remove("ResultProvenance");
        File.WriteAllText(file, legacy.ToJsonString());
        var old = new SeedLibraryStore(path, []).GetAll().Single();
        check(old.CanOpen && old.ResultProvenance is null && old.Seed == saved.Seed,
            "Schema1 favorites remain readable without invented result validation evidence");
    }

    private static void CheckHistoricalEnvironmentProvenance(string root, SeedLibraryContext context, Action<bool, string> check)
    {
        string directory = Path.Combine(root, "historical-environment");
        var workspace = new SearchWorkspace(directory, false);
        workspace.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "current-content", GameVersionIdentity = "0.111.0" });
        var draft = new RolltheSpire2.Ui.Workspace.WorkbenchSearchDraft(context.Players[0].Character, context.Ascension,
            SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        var origin = new SeedQueryAssociation(QueryHistoryIdentity.Key(draft), SeedQueryAssociationKind.VerifiedFromSearch,
            "old-mixed-module-hash", "old-source-record");
        var source = new QueryHistoryEntry { Id = origin.SearchRecordId, QueryKey = origin.QueryKey,
            IdentityVersion = 2, EnvironmentKnown = true, EnvironmentFingerprint = origin.EnvironmentFingerprint,
            RuntimeEnvironmentFingerprint = "old-mixed-runtime-hash", Draft = draft, Context = context,
            RunCount = 1, LastUsedAtUtc = DateTimeOffset.UtcNow.AddDays(-1) };
        workspace.Workspace.QueryHistory.Add(source);
        var result = new PersistedSearchResult { Seed = "111111111111", Context = context,
            ValidationDisposition = SearchDisposition.Match, SourceAssociation = origin };
        source.Results.Add(result);
        var library = new SeedLibraryStore(Path.Combine(directory, "bookmarks"), []);
        var saved = library.Save(result.Seed, context, "historical source", origin.QueryKey, origin,
            resultProvenance: SeedLibraryResultProvenance.From(result, origin));
        check(workspace.EnvironmentComparison(source).Status == SearchEnvironmentComparisonStatus.Unknown &&
            saved.ResultProvenance!.SourceEnvironmentFingerprint == origin.EnvironmentFingerprint && saved.QueryAssociations.Single() == origin,
            "Legacy bookmark environment remains unknown and its old source fingerprint is retained");
        var current = workspace.EnsureQuery(draft, context);
        current.RunCount = 1; current.LastUsedAtUtc = DateTimeOffset.UtcNow;
        check(current.Id != source.Id && current.EnvironmentSignature is not null && source.EnvironmentSignature is null &&
            library.GetAll().Single().QueryAssociations.All(a => a.SearchRecordId != current.Id),
            "A current query cannot appropriate a legacy favorite's verified source association");
        workspace.SetQueryHistoryLimit(1);
        var reopened = new SeedLibraryStore(Path.Combine(directory, "bookmarks"), []).GetAll().Single();
        check(!workspace.Workspace.QueryHistory.Any(q => q.Id == source.Id) && reopened.QueryAssociations.Single() == origin &&
            reopened.ResultProvenance!.SourceEnvironmentFingerprint == "old-mixed-module-hash" &&
            workspace.EnvironmentComparison(null).Status == SearchEnvironmentComparisonStatus.Unknown,
            "History eviction preserves favorite provenance without falsely labelling a missing source as changed");
    }

    private static SeedLibraryContext Context(int count) => new("0.111.0", RuntimeProfileId.Beta111,
        count == 1 ? WorldGameMode.Singleplayer : WorldGameMode.Multiplayer, 10,
        Enumerable.Range(0, count).Select(slot => new SeedLibraryPlayer(slot,
            slot % 2 == 0 ? BaseGameModelKeys.Characters.Silent : BaseGameModelKeys.Characters.Ironclad,
            JsonSerializer.Deserialize<SerializableUnlockState>("{}")!, "Fixture-" + slot,
            AncientOptionConditionProfile.BroadDefault,
            count == 1 ? SearchQuery.Empty : SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl) },
            new(2, "fixture-route-" + slot, slot % 2 == 0))).ToArray());
}
