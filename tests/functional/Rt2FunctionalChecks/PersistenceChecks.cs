using RolltheSpire2.Ui.Workspace;
using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Infrastructure.DeveloperNotes;
using RolltheSpire2.Presentation.DeveloperNotes;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;

// Managed, fixed fixtures only. Reflection capture uses a fixture ModelDb shape;
// no live ModelDb, SaveManager, RunState, Godot UI or Search host.
internal static class PersistenceChecks
{
    private static readonly JsonSerializerOptions Json = SearchPresetStore.CreateJsonOptions();
    private static readonly ModelKey Character = BaseGameModelKeys.Characters.Silent;
    private static readonly ModelKey Relic = BaseGameModelKeys.Relics.GoldenPearl;
    private static readonly ModelKey Card = new("CARD", "PERSISTENCE_CARD");
    private static readonly ModelKey Event = new("EVENT", "PERSISTENCE_EVENT");
    private static readonly ModelKey Missing = new("RELIC", "PERSISTENCE_MISSING");
    private static readonly DateTimeOffset Captured = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly SearchDraft Empty = new("", "", "", false, "", "", "", false, false, "", false, false, "", "", false);
    private static readonly SearchDraft Intent = Empty with { NeowAny = Relic.Serialized };
    private static readonly RuntimeAuthoritySnapshot Authority = RuntimeAuthoritySnapshot.Unavailable("fixture", "fixture") with
    {
        SemanticUniverse = RuntimeAuthorityDomains.Schema1SemanticDomains.Select(domain => new RuntimeSemanticDomainSnapshot(
            domain, (domain switch
            {
                RuntimeAuthorityDomains.Characters => new[] { Character },
                RuntimeAuthorityDomains.Relics => new[] { Relic },
                RuntimeAuthorityDomains.Cards => new[] { Card },
                RuntimeAuthorityDomains.Events => new[] { Event },
                _ => Array.Empty<ModelKey>()
            }).Select(key => new RuntimeSemanticItem(key.Serialized, [])).ToArray(), true, "synthetic")).ToArray()
    };

    internal static int Run(bool notesOnly = false, bool featurePreferencesOnly = false)
    {
        string root = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "persistence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int checks = 0, failures = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
        void Case(string name, Action<string> action)
        {
            if (notesOnly && !name.StartsWith("notes-", StringComparison.Ordinal)) return;
            if (featurePreferencesOnly && name is not ("in-run-console-preferences" or "in-run-feature-preferences" or
                "in-run-explicit-preferences" or "retired-appearance-migration" or "interface-font-preference")) return;
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            try { action(directory); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex}"); }
        }

        Case("actual-environment-identity-and-migration", directory =>
        {
            var unlockDomains = RuntimeAuthorityDomains.Schema1UnlockDomains.Select(d =>
                new RuntimeUnlockAuthorityDomainSnapshot(d, ["fixture:" + d], true, "fixture")).ToArray();
            RuntimeAuthoritySnapshot Runtime(IReadOnlyList<RuntimeSemanticDomainSnapshot> domains) => Authority with {
                GameVersion = "0.111.0", SemanticUniverse = domains, UnlockAuthority = unlockDomains,
                CurrentUnlockState = unlockDomains.Select(d => new RuntimeCurrentUnlockDomainSnapshot(d.Domain,
                    d.UnlockUniverse, true, "fixture")).ToArray(),
                Fingerprint = RuntimeSemanticFingerprint.Build(1, domains, unlockDomains) };
            var runtime = Runtime(Authority.SemanticUniverse);
            var effect = RolltheSpire2.Tests.FastSearch.Beta110FastSearchFixtureAuthority.CreateBeta111(Character, 10, 0, 1).EffectAuthority!;
            var world = RelicSequenceFixtureFactory.CreateModern().Beta109Generation! with {
                AllCharactersInSourceOrder = [Character, BaseGameModelKeys.Characters.Ironclad] };
            var firstCard = new StartingDeckCard(effect.OrderedDeck![0].CardKey);
            var secondCard = new StartingDeckCard(effect.OrderedDeck.First(c => c.CardKey != firstCard.Id).CardKey);
            var silent = new StartingDeckCharacter(Character, [firstCard, secondCard]);
            var ironclad = new StartingDeckCharacter(BaseGameModelKeys.Characters.Ironclad, [firstCard, secondCard, firstCard]);
            StartingDeckModelDb.AllCharacters = [silent, ironclad];
            RuntimeSnapshotThreadGuard.BindCurrentThread();
            var startingDecks = RuntimeAuthoritySnapshotCapture.CaptureCharacterStartingDecks(typeof(StartingDeckModelDb))!;
            Check(startingDecks.Count == 2 && startingDecks[1].OrderedDeck.Select(c => c.CardKey)
                .SequenceEqual([firstCard.Id, secondCard.Id, firstCard.Id]) && startingDecks[1].OrderedDeck.Select(c => c.PoolOrder).SequenceEqual([0, 1, 2]),
                "Production reflection capture copies every available character's starting deck in effective order with duplicates");
            var unlocks = new MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState { UnlockedEpochs = [], EncountersSeen = [] };
            SearchEnvironmentSignature Capture(string diagnostics, string version = "0.111.0") =>
                SearchEnvironmentSignatureBuilder.FromCapturedInputs(version, runtime, effect, world, unlocks, diagnostics, startingDecks);
            var original = Capture("RT2:mvid=A;UiMod:version=1");
            Check(original.CaptureComplete && original.Fingerprint.Length > 0, "Copied actual content and unlock fixtures provide a complete environment");
            var recaptured = SearchEnvironmentSignatureBuilder.FromCapturedInputs("0.111.0", runtime,
                effect with { CapturedAtUtc = DateTimeOffset.UtcNow, SnapshotFingerprint = "new-execution",
                    CatalogFingerprint = "new-rt2-audit", CaptureDiagnosticCode = "new-diagnostic" },
                world with { SnapshotFingerprint = "new-world-execution", CatalogFingerprint = "new-world-audit",
                    CaptureDiagnosticCode = "new-diagnostic", GenerationRuleFingerprint = "rt2-audit-only-change" }, unlocks,
                "RT2:mvid=B;UiMod:version=2;UiModDll=changed", startingDecks);
            Check(SearchEnvironmentIdentity.Compare(original, recaptured).Status == SearchEnvironmentComparisonStatus.Same &&
                original.RuntimeModuleDiagnostics != recaptured.RuntimeModuleDiagnostics,
                "Restart, RT2 build, pure UI Mod build and capture diagnostics do not change actual environment identity");
            var deckDiagnostics = startingDecks.Select(d => d with {
                OrderedDeck = d.OrderedDeck.Select(c => c with { SourceAssembly = "different-module", SourceModId = "different-build",
                    InstanceId = "different-capture", EligibilityAuthority = "different-evidence", BehaviorMetadataExact = false }).ToArray() }).ToArray();
            Check(SearchEnvironmentIdentity.Compare(original, SearchEnvironmentSignatureBuilder.FromCapturedInputs("0.111.0", runtime,
                effect, world, unlocks, startingDecks: deckDiagnostics)).Status == SearchEnvironmentComparisonStatus.Same,
                "Starting deck module provenance and capture evidence labels do not change the same effective deck inputs");
            var properties = Runtime(Authority.SemanticUniverse.Select(d => d.Domain == RuntimeAuthorityDomains.Cards
                ? d with { Items = d.Items.Select(i => i with { Fields = ["rarity=changed"] }).ToArray() } : d).ToArray());
            var propertyChange = SearchEnvironmentSignatureBuilder.FromCapturedInputs("0.111.0", properties, effect, world, unlocks, startingDecks: startingDecks);
            Check(SearchEnvironmentIdentity.Compare(original, propertyChange).Reasons.Contains("CardCatalogChanged"),
                "Equal content count and IDs cannot hide a real prediction-property change");
            var orderChange = SearchEnvironmentSignatureBuilder.FromCapturedInputs("0.111.0", runtime,
                effect with { CharacterRewardPool = effect.CharacterRewardPool!.Reverse().ToArray() }, world, unlocks, startingDecks: startingDecks);
            Check(SearchEnvironmentIdentity.Compare(original, orderChange).Reasons.Contains("CardCatalogChanged"),
                "Effective pool order participates without sorting it away");
            var worldChange = SearchEnvironmentSignatureBuilder.FromCapturedInputs("0.111.0", runtime, effect,
                world with { AllCharactersInSourceOrder = world.AllCharactersInSourceOrder.Reverse().ToArray() }, unlocks, startingDecks: startingDecks);
            Check(SearchEnvironmentIdentity.Compare(original, worldChange).Reasons.Contains("CharacterCatalogChanged"),
                "Effective character allocation order is content");
            var moreUnlocks = JsonSerializer.Deserialize<MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState>(JsonSerializer.Serialize(unlocks))!;
            moreUnlocks.NumberOfRuns++;
            var unlockChange = SearchEnvironmentSignatureBuilder.FromCapturedInputs("0.111.0", runtime, effect, world, moreUnlocks, startingDecks: startingDecks);
            Check(SearchEnvironmentIdentity.Compare(original, unlockChange).Reasons.SequenceEqual(["UnlockStateChanged"]),
                "Actual saved unlock/discovery progress participates independently");
            var secondRun = JsonSerializer.Deserialize<MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState>(JsonSerializer.Serialize(moreUnlocks))!;
            secondRun.NumberOfRuns = 2;
            Check(SearchEnvironmentIdentity.Compare(unlockChange,
                SearchEnvironmentSignatureBuilder.FromCapturedInputs("0.111.0", runtime, effect, world, secondRun, startingDecks: startingDecks)).Status == SearchEnvironmentComparisonStatus.Same,
                "Later run counts alone do not change the effective environment");
            SearchEnvironmentSignature RecaptureDecks() => SearchEnvironmentSignatureBuilder.FromCapturedInputs("0.111.0", runtime,
                effect, world, unlocks, startingDecks: RuntimeAuthoritySnapshotCapture.CaptureCharacterStartingDecks(typeof(StartingDeckModelDb)));
            ironclad.StartingDeck = [secondCard, secondCard, firstCard];
            var replacedDeck = RecaptureDecks();
            Check(SearchEnvironmentIdentity.Compare(original, replacedDeck).Status == SearchEnvironmentComparisonStatus.Changed &&
                SearchEnvironmentIdentity.Compare(original, replacedDeck).Reasons.SequenceEqual(["CharacterCatalogChanged"]) &&
                original.CardCatalogFingerprint == replacedDeck.CardCatalogFingerprint,
                "Replacing a non-Silent starting card with an existing card changes identity with unchanged global pools");
            ironclad.StartingDeck = [secondCard, firstCard, firstCard];
            Check(SearchEnvironmentIdentity.Compare(original, RecaptureDecks()).Reasons.SequenceEqual(["CharacterCatalogChanged"]),
                "Reordering a non-Silent starting deck changes identity with the same cards and pools");
            ironclad.StartingDeck = [firstCard, secondCard, firstCard];
            Check(SearchEnvironmentIdentity.Compare(original, RecaptureDecks()).Status == SearchEnvironmentComparisonStatus.Same,
                "Restoring the same effective starting decks preserves identity across fresh captures");
            ironclad.StartingDeck = [new StartingDeckCard(firstCard.Id) { CurrentUpgradeLevel = 1 }, secondCard, firstCard];
            Check(SearchEnvironmentIdentity.Compare(original, RecaptureDecks()).Reasons.SequenceEqual(["CharacterCatalogChanged"]),
                "Non-Silent starting card instance properties use the same production card projection as the prediction deck");
            ironclad.StartingDeck = [firstCard, secondCard, firstCard];
            Check(startingDecks[1].OrderedDeck.Select(c => c.CardKey).SequenceEqual([firstCard.Id, secondCard.Id, firstCard.Id]),
                "Captured starting deck evidence does not retain mutable game-model collections");
            var missingDeck = SearchEnvironmentSignatureBuilder.FromCapturedInputs("0.111.0", runtime, effect, world, unlocks,
                startingDecks: startingDecks.Take(1).ToArray());
            Check(!missingDeck.CaptureComplete && SearchEnvironmentIdentity.Compare(original, missingDeck).Status == SearchEnvironmentComparisonStatus.Unknown,
                "Missing one actual character's deck is unavailable evidence rather than a comparable partial environment");
            StartingDeckModelDb.AllCharacters = [silent, new MissingStartingDeckCharacter(BaseGameModelKeys.Characters.Ironclad)];
            Check(RuntimeAuthoritySnapshotCapture.CaptureCharacterStartingDecks(typeof(StartingDeckModelDb)) is null,
                "Production capture cannot silently omit an available character whose starting deck is unavailable");
            StartingDeckModelDb.AllCharacters = [silent, ironclad];
            string PartyEnvironment(MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState? state) =>
                QueryHistoryIdentity.EnvironmentKey(Workbench() with { Players = [new(0, Character, state, "fixture")] }, "same-content");
            Check(PartyEnvironment(moreUnlocks) == PartyEnvironment(secondRun) &&
                PartyEnvironment(unlocks) != PartyEnvironment(moreUnlocks) && PartyEnvironment(null) != PartyEnvironment(unlocks),
                "Query records preserve first-run and missing-unlock distinctions without splitting every later run");
            var context = RolltheSpire2.Tests.FastSearch.Beta110FastSearchFixtureAuthority.CreateBeta111(Character, 10, 0, 1)
                .WithWorldAuthority(RelicSequenceFixtureFactory.CreateModern());
            JsonNode? ExecutionInput(int? runs) => RuntimePredictionInputIdentity.AuthorityPayload(
                context.WithWorldAuthority(context.WorldAuthority! with { EncounterNumberOfRuns = runs }));
            Check(JsonNode.DeepEquals(ExecutionInput(1), ExecutionInput(2)) &&
                !JsonNode.DeepEquals(ExecutionInput(0), ExecutionInput(1)) && !JsonNode.DeepEquals(ExecutionInput(null), ExecutionInput(0)),
                "Typed resume authority normalizes later runs while preserving first-run and unknown encounter facts");
            Check(moreUnlocks.NumberOfRuns == 1 && secondRun.NumberOfRuns == 2,
                "Identity normalization does not mutate captured unlock data");
            Check(SearchEnvironmentIdentity.Compare(original, Capture("same-modules", "0.112.0")).Reasons.Contains("GameVersionChanged"),
                "The actual game version remains an environment component");
            var oldJson = JsonSerializer.SerializeToNode(original)!.AsObject(); oldJson.Remove("IdentityVersion");
            var legacy = oldJson.Deserialize<SearchEnvironmentSignature>()!;
            Check(legacy.IdentityVersion == 0 && SearchEnvironmentIdentity.Compare(legacy, original).Status == SearchEnvironmentComparisonStatus.Unknown,
                "An old mixed hash is unavailable evidence even when its text happens to equal today's hash");
            var oldDeckCoverage = original.Snapshot(); oldDeckCoverage.IdentityVersion = 2;
            Check(SearchEnvironmentIdentity.Compare(oldDeckCoverage, original).Status == SearchEnvironmentComparisonStatus.Unknown,
                "The previous Silent-only identity cannot declare a change merely because all-character coverage uses a new schema");
            legacy.GameVersionIdentity = "0.110.0";
            var partiallyKnown = SearchEnvironmentIdentity.Compare(legacy, original);
            Check(partiallyKnown.Status == SearchEnvironmentComparisonStatus.Changed && partiallyKnown.Reasons.Contains("GameVersionChanged") &&
                partiallyKnown.Reasons.Contains("EnvironmentComponentsUnavailable"), "A proven old version difference does not invent missing content comparisons");
            Check(SearchEnvironmentIdentity.Describe(SearchEnvironmentIdentity.Compare(original, propertyChange), false)
                    .StartsWith("此记录与当前游戏环境不同：", StringComparison.Ordinal) &&
                SearchEnvironmentIdentity.Describe(SearchEnvironmentIdentity.Compare(null, original), false).Contains("无法判定", StringComparison.Ordinal),
                "Historical notices identify this record and distinguish changed from uncomparable");

            var store = Workspace(directory); var draft = Workbench();
            store.EnsureEnvironment(original); store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("original", draft));
            store.AppendResult(PersistableCandidate("000000000001"), "original"); store.FlushAll();
            var record = store.RecentQueries.Single(); var association = store.ResultAssociation("000000000001")!;
            store = Workspace(directory); store.EnsureEnvironment(recaptured);
            Check(!store.EnvironmentResetOccurred && store.ResultCountForQuery(draft) == 1 &&
                store.RecentQueries.Single().Id == record.Id && store.RecentQueries.Single().EnvironmentSignature!.Fingerprint == original.Fingerprint,
                "Restart and module-only changes preserve current history and captured provenance");
            store.FlushAll();
            string state = Path.Combine(directory, "RolltheSpire2", "state");
            var workspaceJson = JsonNode.Parse(File.ReadAllText(Path.Combine(state, "search_workspace.json")))!;
            workspaceJson["QueryHistory"]![0]!.AsObject().Remove("EnvironmentSignature");
            File.WriteAllText(Path.Combine(state, "search_workspace.json"), workspaceJson.ToJsonString());
            var environmentJson = JsonNode.Parse(File.ReadAllText(Path.Combine(state, "search_environment.json")))!.AsObject();
            environmentJson.Remove("IdentityVersion");
            File.WriteAllText(Path.Combine(state, "search_environment.json"), environmentJson.ToJsonString());
            store = Workspace(directory); store.EnsureEnvironment(recaptured);
            var historical = store.RecentQueries.Single();
            Check(!store.EnvironmentResetOccurred && historical.EnvironmentSignature is null &&
                store.EnvironmentComparison(historical).Status == SearchEnvironmentComparisonStatus.Unknown &&
                historical.Id == association.SearchRecordId && historical.Results.Single().SourceAssociation == association &&
                store.ResultCountForQuery(draft) == 0,
                "Migration neither relabels old origins as current nor falsely declares an environment change");
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("new-capture", draft));
            store.AppendResult(PersistableCandidate("000000000002"), "new-capture");
            Check(store.RecentQueries.Count == 2 && store.ResultAssociation("000000000002")!.SearchRecordId != association.SearchRecordId &&
                store.Workspace.QueryHistory.Single(q => q.Id == historical.Id).Results.Single().SourceAssociation == association,
                "New observations remain separate even when an old opaque record ID collides");
            store.EnsureEnvironment(propertyChange);
            Check(store.EnvironmentResetOccurred && store.EnvironmentResetReason.Contains("CardCatalogChanged", StringComparison.Ordinal) &&
                store.Workspace.QueryHistory.Single(q => q.Id == historical.Id).EnvironmentSignature is null,
                "A genuine content change explains invalidation while preserving unknown historical evidence");
        });

        Case("bundled-text-resources", directory =>
        {
            foreach (string language in new[] { "zh", "en" })
            {
                var text = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13(language);
                Check(text.TryGet("shell.settings", out var settings) && settings == text.Get("shell.settings"),
                    "Explicit lookup returns the actual localized value");
                Check(!text.TryGet("l10n.fixture.absent", out var absent) && absent.Length == 0 &&
                    text.Get("l10n.fixture.absent") == "Missing translation",
                    "Lookup distinguishes a missing key without relying on rendered sentinel text");
            }
            var assembly = typeof(DeveloperNotesDocumentProvider).Assembly;
            var catalog = assembly.GetType("RolltheSpire2.Presentation.Localization.JsonLocalizationCatalog", true)!;
            var loadCatalog = catalog.GetMethod("Load")!;
            string[] articleStems = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("RolltheSpire2.Texts.zh.Encyclopedia.", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal))
                .Select(name => name["RolltheSpire2.Texts.zh.Encyclopedia.".Length..^3]).Order(StringComparer.Ordinal).ToArray();
            string[] englishArticleStems = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("RolltheSpire2.Texts.en.Encyclopedia.", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal))
                .Select(name => name["RolltheSpire2.Texts.en.Encyclopedia.".Length..^3]).Order(StringComparer.Ordinal).ToArray();
            Check(articleStems.Length > 0 && articleStems.SequenceEqual(englishArticleStems),
                "Every bundled encyclopedia article has both language resources");
            foreach (string language in new[] { "zh", "en" })
            {
                Check(!assembly.GetManifestResourceNames().Contains($"RolltheSpire2.Texts.{language}.ContentNames.fallback.json"),
                    "Retired static content names are not bundled: " + language);
                Check(((IReadOnlyDictionary<string, string>)loadCatalog.Invoke(null, new object[] { "game_content", language })!).Count == 0,
                    "Retired content-name group has no catalog or loose-resource lookup: " + language);
                foreach (var (group, path) in new[]
                {
                    ("rt2_ui_legacy", "UI.legacy"), ("rt2_ui_v13", "UI.workbench"),
                    ("seed_presets", "SeedLibrary.presets"),
                    ("seed_recommendations", "SeedLibrary.seeds")
                })
                {
                    string resource = $"RolltheSpire2.Texts.{language}.{path}.json";
                    using var stream = assembly.GetManifestResourceStream(resource);
                    Check(stream is not null, "Bundled text resource exists: " + resource);
                    if (group.StartsWith("seed_", StringComparison.Ordinal))
                    {
                        var expected = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream!)!;
                        var actual = (IReadOnlyDictionary<string, Dictionary<string, string>>)catalog.GetMethod("LoadEntries")!
                            .Invoke(null, new object[] { group, language })!;
                        Check(expected.Count > 0 && expected.Count == actual.Count && expected.All(entry =>
                            actual.TryGetValue(entry.Key, out var fields) && fields.Count == entry.Value.Count &&
                            entry.Value.All(field => fields.TryGetValue(field.Key, out string? value) && value == field.Value)),
                            "Entry catalog preserves grouped fields: " + resource);
                    }
                    else
                    {
                        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(stream!)!;
                        var actual = (IReadOnlyDictionary<string, string>)loadCatalog.Invoke(null, new object[] { group, language })!;
                        Check(expected.Count > 0 && expected.Count == actual.Count &&
                            expected.All(pair => actual.TryGetValue(pair.Key, out string? value) && value == pair.Value),
                            "Catalog loads the complete bundled language: " + resource);
                    }
                }
                foreach (string chapter in DeveloperNotesChapterIds.All)
                {
                    string resource = $"RolltheSpire2.Texts.{language}.DeveloperNotes.{chapter}.json";
                    using var stream = assembly.GetManifestResourceStream(resource);
                    Check(stream is not null, "Bundled notes chapter exists: " + resource);
                }
                foreach (string stem in articleStems)
                {
                    string resource = $"RolltheSpire2.Texts.{language}.Encyclopedia.{stem}.md";
                    using var stream = assembly.GetManifestResourceStream(resource);
                    Check(stream is not null, "Bundled encyclopedia article exists: " + resource);
                    using var reader = new StreamReader(stream!);
                    Check(!string.IsNullOrWhiteSpace(reader.ReadToEnd()), "Bundled encyclopedia article is readable: " + resource);
                }
            }
            using var index = assembly.GetManifestResourceStream("RolltheSpire2.Infrastructure.DeveloperNotes.index.json");
            Check(index is not null, "Notes metadata index remains embedded separately from localized text");
        });

        Case("developer-preset-localization", directory =>
        {
            var store = Workspace(directory);
            var presets = store.Presets.GetAll().Where(p => p.Source == SearchPresetSource.BuiltIn).ToArray();
            Check(presets.Length == 4, "All four developer presets remain available");
            foreach (var preset in presets)
            {
                string before = JsonSerializer.Serialize(SearchPresetStore.ToPersisted(preset, Json), Json);
                string english = preset.TitleFor("en");
                Check(english.Length > 0 && english != preset.Title && preset.DescriptionFor("en").Length > 0 &&
                    preset.DescriptionFor("en") != preset.Description, "Developer preset has both English fields: " + preset.Id);
                Check(preset.TitleFor("zh") == preset.Title && preset.DescriptionFor("zh") == preset.Description &&
                    preset.TitleFor("en") == english, "English/Chinese/English switching preserves original text");
                Check(new[] { preset.Title, preset.Description, english, preset.DescriptionFor("en") }
                    .All(text => preset.SearchText.Contains(text, StringComparison.OrdinalIgnoreCase)),
                    "Both language titles and descriptions are searchable regardless of display language");
                Check(before == JsonSerializer.Serialize(SearchPresetStore.ToPersisted(preset, Json), Json),
                    "Display resolution does not change ID, query, icons or persisted metadata");
                var older = SearchPresetStore.Normalize(SearchPresetStore.ToPersisted(preset, Json), SearchPresetSource.BuiltIn, Json);
                Check(older.TitleFor("en") == preset.Title && older.DescriptionFor("en") == preset.Description,
                    "Older preset JSON without English fields falls back to original Chinese");
            }
            var source = presets[0];
            var missingDescription = source with { BuiltInDescription = new() { Zh = source.Description, En = "  " } };
            Check(missingDescription.TitleFor("en") == source.TitleFor("en") && missingDescription.DescriptionFor("en") == source.Description &&
                source.TitleFor("unknown") == source.Title, "Fallbacks are per field and unknown languages use Chinese");

            var saved = store.Presets.SaveUserPresetFromExisting(source.Title, source.Description, source.VisualIcons, source);
            Check(saved.Source == SearchPresetSource.User && saved.TitleFor("en") == source.Title && saved.DescriptionFor("en") == source.Description,
                "User copy of a developer preset retains literal editable text");
            Check(!saved.SearchText.Contains(source.TitleFor("en"), StringComparison.Ordinal), "User copy does not retain hidden developer search aliases");
            Check(store.Presets.TryGetUserByName(source.Title, out var found) && found.Id == saved.Id &&
                !store.Presets.TryGetUserByName(source.TitleFor("en"), out _), "Save-by-name lookup uses raw text only");
            var replaced = store.Presets.SaveUserPresetFromExisting(source.Title, source.Description, source.VisualIcons, source);
            Check(replaced.Id == saved.Id && Same(replaced.Workbench, source.Workbench) && Same(replaced.VisualIcons, source.VisualIcons),
                "Save by original title reuses user ID and preserves query/icons");
            var historyDraft = saved.Workbench! with { Version = 3 };
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("localized-preset-fixture", historyDraft));
            store.AppendResult(PersistableCandidate("000000000001"), "localized-preset-fixture");
            var edited = store.Presets.UpdateUserMetadata(saved.Id, "My own title 我的标题", "My own description 我的说明", saved.VisualIcons);
            store.FlushAll(); store = Workspace(directory);
            var loaded = store.Presets.GetAll().Single(p => p.Id == edited.Id);
            Check(loaded.TitleFor("en") == edited.Title && loaded.TitleFor("zh") == edited.Title &&
                loaded.DescriptionFor("en") == edited.Description && loaded.DescriptionFor("zh") == edited.Description,
                "User metadata edits remain literal across reload and language switching");
            Check(store.QueriesForPreset(loaded).Single().Results.Count == 1 &&
                store.PresetsForQuery(QueryHistoryIdentity.Key(historyDraft)).Single().Id == saved.Id,
                "User preset rename and display language retain the original history association");
            var exported = SearchPresetStore.ToPersisted(loaded, Json);
            exported.EnglishTitle = "Must not replace user title"; exported.EnglishDescription = "Must not replace user description";
            var imported = SearchPresetStore.Normalize(exported, SearchPresetSource.User, Json);
            Check(imported.TitleFor("en") == edited.Title && imported.DescriptionFor("en") == edited.Description,
                "Translation fields in imported user JSON cannot override user-authored text");
            Check(!JsonSerializer.Serialize(SearchPresetStore.ToPersisted(imported, Json), Json).Contains("EnglishTitle", StringComparison.Ordinal),
                "User exports omit developer translation metadata");
            var changed = source with { Title = "Edited title", Description = "Edited description" };
            Check(changed.TitleFor("en") == changed.Title && changed.DescriptionFor("en") == changed.Description,
                "Stale translations cannot hide edited source text");
        });

        Case("result-unfiltered-provenance-roundtrip", directory =>
        {
            var store = Workspace(directory);
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "fixture-environment" });
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("candidate-mode", Workbench()));
            var candidate = PersistableCandidate("000000000007") with { IsUnverified = true, IsUnfiltered = true };
            store.AppendResult(candidate, "candidate-mode"); store.FlushAll(); store = Workspace(directory);
            Check(store.Workspace.Results.Single() is { IsUnverified: true, IsUnfiltered: true } &&
                store.RecentQueries.Single().Results.Single() is { IsUnverified: true, IsUnfiltered: true },
                "Candidate publication retains both unverified and unfiltered facts after restart");
            store.AppendResult(candidate with { IsUnfiltered = false, ValidationDisposition = SearchDisposition.Unknown,
                ValidationIssue = "FixtureMissingObservation" }, "candidate-mode");
            store.FlushAll(); store = Workspace(directory);
            Check(store.Workspace.Results.Single() is { IsUnverified: true, IsUnfiltered: false,
                ValidationDisposition: SearchDisposition.Unknown, ValidationIssue: "FixtureMissingObservation" } &&
                store.RecentQueries.Single().Results.Single().HasIncompleteValidation,
                "Incomplete checking retains the actual disposition and reason through result/history reload");
            store.AppendResult(candidate with { IsUnverified = false, IsUnfiltered = false,
                ValidationDisposition = SearchDisposition.Match, ValidationIssue = "" }, "confirmed-mode");
            store.FlushAll(); store = Workspace(directory);
            Check(store.Workspace.Results.Single() is { IsUnverified: false, IsUnfiltered: false } &&
                store.RecentQueries.Single().Results.Single() is { IsUnverified: false, IsUnfiltered: false },
                "A later confirmed hit of the same seed replaces both candidate flags without duplication");
        });

        Case("treasure-source-selected-witness-roundtrip", directory =>
        {
            const string selected = "choice.1.opening", alternate = "choice.2.opening", outcome = "taken";
            var candidate = PersistableCandidate("000000000010") with
            {
                Witnesses = [new("000000000010", selected, [], ["TreasureRelics.Slot1", "TreasureRelics.Slot2"], "", null, []) { OutcomeId = outcome }],
                MatchEvidence = [
                    new("TreasureRelicAtEncounter", Relic, selected, OutcomeId: outcome, Ordinal: 2, ConditionId: "TreasureRelics.Slot1"),
                    new("TreasureRelicObtainedAtOpening", Missing, selected, OutcomeId: outcome, ConditionId: "TreasureRelics.Slot2"),
                    new("TreasureRelicAtEncounter", Missing, alternate, OutcomeId: outcome, Ordinal: 1, ConditionId: "TreasureRelics.Slot1"),
                    new("TreasureRelicAtEncounter", Missing, selected, OutcomeId: "skipped", Ordinal: 1, ConditionId: "TreasureRelics.Slot1")]
            };
            PersistedTreasureSource[] expected = [new(Relic.Serialized, 1, 2, false), new(Missing.Serialized, 2, null, true)];
            Check(SearchResultProjection.FromCandidate(candidate, "treasure").TreasureSources.SequenceEqual(expected),
                "Only the selected opening and outcome supply chest or early-acquisition receipts");
            var global = candidate with { MatchEvidence = [new("TreasureRelicAtEncounter", Relic,
                Ordinal: 1, ConditionId: "TreasureRelics.Slot1")] };
            Check(SearchResultProjection.TreasureSources(global).SequenceEqual([new(Relic.Serialized, 1, 1, false)]),
                "Explicit global baseline receipts survive an independent routed witness");
            Check(SearchResultProjection.TreasureSources(candidate with { ValidationDisposition = SearchDisposition.Unknown }).Count == 0 &&
                SearchResultProjection.TreasureSources(candidate with { Witnesses = [] }).Count == 0,
                "Unresolved or unwitnessed results cannot claim target acquisition");
            var store = Workspace(directory);
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "treasure-source-environment" });
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("treasure", Workbench()));
            store.AppendResult(candidate, "treasure"); store.FlushAll(); store = Workspace(directory);
            var saved = store.Workspace.Results.Single();
            Check(saved.TreasureSources.SequenceEqual(expected) && store.RecentQueries.Single().Results.Single().TreasureSources.SequenceEqual(expected),
                "Selected target receipts survive workspace and history save/reload");
            var provenance = SeedLibraryResultProvenance.From(saved, saved.SourceAssociation);
            Check(provenance.TreasureSources.SequenceEqual(expected) &&
                SeedLibraryResultProvenance.From(candidate, null).TreasureSources.SequenceEqual(expected),
                "Live and saved favorite paths retain the same selected receipts");
            saved.ValidationDisposition = SearchDisposition.Unknown;
            Check(saved.TreasureSources.Count == 0 && (provenance with { ValidationDisposition = SearchDisposition.NoMatch }).TreasureSources.Count == 0,
                "Changed validation state cannot retain visible Match receipts");
            var old = JsonSerializer.Deserialize<PersistedSearchResult>("{\"Seed\":\"000000000010\",\"IsUnverified\":false}", Json)!;
            Check(old.TreasureSources.Count == 0, "Older saved Match rows receive no invented acquisition source");
        });

        Case("result-support-scope-roundtrip", directory =>
        {
            var store = Workspace(directory);
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "scope-environment" });
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("scope", Workbench()));
            var evidence = new RequestSupportEvidence(true, [new("fixture-mod", "1.0")], "fixture-loaded-mods",
                new(RequestFactSource.ExplicitRequest, Vanilla111EpochBaseline.EpochIds, true, [], 1, "fixture-unlocks"),
                new(RequestFactSource.ExplicitRequest, [], true, "fixture-discovery"));
            var authority = new RuntimeContextAuthoritySnapshot(CharacterIdentity.FromKey(Character), 10, 0, 1,
                true, true, true, 4, 2, "0.111.0", RuntimeProfileId.Beta111, true, null, "sts2",
                SourceAuthority.OfficialRuntimeExact, SnapshotCompleteness.Complete, IdentityResolutionStatus.Exact,
                predictionGameMode: WorldGameMode.Singleplayer, supportEvidence: evidence);
            var candidate = PersistableCandidate("000000000008") with { Authority = authority };
            Check(!candidate.IsUnverified && candidate.SupportScope.State == RequestSupportScopeState.OutsideDeclaredBaseline,
                "A modeled Match in a Mod environment does not become an in-baseline correctness claim");
            store.AppendResult(candidate, "scope"); store.FlushAll(); store = Workspace(directory);
            var saved = store.Workspace.Results.Single();
            Check(!saved.IsUnverified && saved.SupportScopeState == RequestSupportScopeState.OutsideDeclaredBaseline &&
                Same(saved.SupportScope, candidate.SupportScope) && Same(store.RecentQueries.Single().Results.Single().SupportScope, candidate.SupportScope),
                "Results and history retain frozen support reasons and source evidence independently of model validation");
            Check(SeedPredictionRequest.TryCreate(candidate.Seed, authority.Character, 10, 1, 0, authority, false,
                out var request, out _), "Scope fixture request binds to its authority");
            var context = SeedPredictionDocument.CreateContext(request!, "0.111.0", RuntimeProfileId.Beta111);
            Check(ReferenceEquals(context.SupportScope, authority.SupportScope),
                "Prediction documents retain the same frozen scope assessment as their request");
            var legacy = JsonSerializer.Deserialize<PersistedSearchResult>("{\"Seed\":\"000000000009\",\"IsUnverified\":false}", Json)!;
            Check(!legacy.IsUnverified && legacy.SupportScope is null && legacy.SupportScopeState == RequestSupportScopeState.Unknown,
                "Old model-confirmed rows without scope evidence remain scope-unknown");
        });

        Case("query-history-merge-quota-preset", directory =>
        {
            var store = Workspace(directory);
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "fixture-environment" });
            var draft = Workbench();
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("run-a", draft));
            for (int i = 1; i <= 35; i++) store.AppendResult(PersistableCandidate(i.ToString("D12")), "run-a");
            Check(store.RecentQueries.Count == 1 && store.RecentQueries[0].Results.Count == 30 && store.DisplayResults.Count == 35,
                "History keeps 30 distinct seeds without limiting current-process display");
            Check(store.RecentQueries[0].Results[0].Seed == "000000000006", "Oldest unbookmarked query result rolls off");
            store.SaveResultProgress(new(SearchRunState.Completed, 100, 35, 100, 35, 3600, 1, default, "", false));
            store.FlushAll(); store = Workspace(directory);
            Check(store.DisplayResults.Count == 30, "Only bounded rows survive restart");
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("different-runtime-fingerprint", draft));
            store.AppendResult(PersistableCandidate("000000000035") with { IsUnverified = true }, "different-runtime-fingerprint");
            store.AppendResult(PersistableCandidate("000000000036"), "different-runtime-fingerprint");
            store.SaveResultProgress(new(SearchRunState.Cancelled, 100, 2, 100, 30, 3600, 1, default, "", true));
            Check(store.RecentQueries.Count == 1 && store.RecentQueries[0].RunCount == 2 && store.RecentQueries[0].ElapsedSeconds == 7200,
                "Same intent and environment merge across execution fingerprints and accumulate durations including cancellation");
            Check(store.RecentQueries[0].Results.Count == 30 && !store.RecentQueries[0].Results.Single(r => r.Seed.EndsWith("035")).IsUnverified,
                "Repeated unchecked hits do not duplicate or downgrade a verified seed");
            var capture = Capture(Intent) with { Workbench = draft };
            var preset = store.Presets.SaveUserPreset("history fixture", "", [], capture);
            store.PruneAndFlushQueryHistory(); store.SetQueryHistoryLimit(1);
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("other", draft with { Ascension = 9 }));
            Check(store.RecentQueries.Count == 1 && store.QueriesForPreset(preset).Single().Results.Count == 30,
                "Preset protects its results outside the recent-history window");
            store.FlushAll(); store = Workspace(directory);
            Check(store.Preferences.QueryHistoryLimit == 1 && store.QueriesForPreset(preset).Single().Results.Count == 30, "Quota and pinned results survive reopen");
            var renamed = store.Presets.UpdateUserMetadata(preset.Id, "renamed", "", []);
            Check(store.QueriesForPreset(renamed).Single().RunCount == 2, "Preset renaming retains its query association");
            var changed = store.Presets.SaveUserPreset("renamed", "", [], capture with { Workbench = draft with { Ascension = 8 }, Ascension = 8 });
            store.PruneAndFlushQueryHistory();
            Check(store.QueriesForPreset(changed).Count == 0, "Changing a preset query cannot inherit old results or fabricate a run");
            Check(!store.Workspace.QueryHistory.Any(q => q.QueryKey == QueryHistoryIdentity.Key(draft)), "Unpinned old query is eligible for history eviction");
        });

        Case("preset-environment-update-only", directory =>
        {
            var store = Workspace(directory);
            var draft = Workbench();
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "original-environment" });
            var preset = store.Presets.SaveUserPreset("unchanged intent", "note", [], Capture(Intent) with { Workbench = draft });
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("run", draft));
            store.AppendResult(PersistableCandidate("000000000111"), "run");
            string originalKey = QueryHistoryIdentity.Key(preset.Workbench!);
            var history = store.RecentQueries.Single();
            var changed = store.Presets.UpdateUserEnvironment(preset.Id, "new-environment");
            store.FlushAll(); store = Workspace(directory);
            var loaded = store.Presets.GetAll().Single(p => p.Id == preset.Id);
            Check(loaded.EnvironmentFingerprint == "new-environment" && loaded.Id == preset.Id && loaded.Title == preset.Title &&
                loaded.Description == preset.Description && QueryHistoryIdentity.Key(loaded.Workbench!) == originalKey,
                "Environment refresh survives reopen without changing preset intent or information");
            Check(store.QueriesForPreset(loaded).Single().Id == history.Id && store.QueriesForPreset(loaded).Single().Results.Count == 1 &&
                store.QueriesForPreset(loaded).Single().RuntimeEnvironmentFingerprint == "original-environment",
                "Refreshing preset environment never relabels historical results");
            bool refused = false;
            try { store.Presets.UpdateUserEnvironment(preset.Id, ""); } catch (InvalidOperationException) { refused = true; }
            Check(refused && store.Presets.GetAll().Single(p => p.Id == preset.Id).EnvironmentFingerprint == "new-environment", "Unavailable current environment preserves the old marker");
        });

        Case("unchanged-environment-commit-and-retry", directory =>
        {
            var store = Workspace(directory);
            var current = new SearchEnvironmentSignature { CaptureComplete = true, Fingerprint = "same-environment",
                RuntimeModuleDiagnostics = "build-a" };
            store.EnsureEnvironment(current); store.FlushAll();
            string path = Path.Combine(directory, "RolltheSpire2", "state", "search_environment.json");
            var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, stamp);
            for (int i = 0; i < 20; i++) { store.EnsureEnvironment(current.Snapshot()); store.FlushAll(); }
            Check(File.GetLastWriteTimeUtc(path) == stamp,
                "Twenty unchanged captures/close flushes do not serialize or rewrite the committed environment");
            current.RuntimeModuleDiagnostics = "build-b";
            store.EnsureEnvironment(current);
            Check(JsonSerializer.Deserialize<SearchEnvironmentSignature>(File.ReadAllText(path), Json)!.RuntimeModuleDiagnostics == "build-b",
                "Persisted diagnostics changes still commit even when semantic environment identity is unchanged");
            string retryDirectory = Path.Combine(directory, "retry");
            var retry = Workspace(retryDirectory);
            string blocked = Path.Combine(retryDirectory, "RolltheSpire2", "state", "search_environment.json");
            Directory.CreateDirectory(blocked);
            retry.EnsureEnvironment(current);
            Check(retry.LastPersistenceFailure is not null, "A failed atomic environment commit remains observable");
            Directory.Delete(blocked);
            retry.FlushAll();
            Check(File.Exists(blocked) && JsonSerializer.Deserialize<SearchEnvironmentSignature>(File.ReadAllText(blocked), Json)!.Fingerprint == current.Fingerprint,
                "A failed environment commit retains dirty state and succeeds on the next flush");
            Console.WriteLine("EnvironmentCommitEvidence unchangedCaptureAndFlushPairs=20 unchangedEnvironmentWrites=0;failedCommitRetried=true");
        });

        Case("preset-marker-evidence-and-association-reuse", directory =>
        {
            var store = Workspace(directory);
            var draft = Workbench();
            var current = new SearchEnvironmentSignature { CaptureComplete = true, Fingerprint = "current-marker" };
            store.EnsureEnvironment(current);
            var saved = store.Presets.SaveUserPreset("marker fixture", "", [], Capture(Intent) with { Workbench = draft });
            Check(store.RecentQueries.Count == 0 && SearchEnvironmentIdentity.PresetMarkerNoticeKey(saved, current).EndsWith("preset_marker_missing", StringComparison.Ordinal),
                "A new preset without a marker has its own missing marker notice without inventing historical evidence");
            var same = store.Presets.UpdateUserEnvironment(saved.Id, current.Fingerprint);
            Check(store.RecentQueries.Count == 0 && SearchEnvironmentIdentity.PresetMarkerNoticeKey(same, current).EndsWith("preset_marker_same", StringComparison.Ordinal),
                "A saved matching marker is comparable without first running this query");
            var changed = current.Snapshot(); changed.Fingerprint = "different-marker";
            Check(SearchEnvironmentIdentity.PresetMarkerNoticeKey(same, changed).EndsWith("preset_marker_different", StringComparison.Ordinal),
                "A different preset marker reports only the marker difference");
            var failed = current.Snapshot(); failed.CaptureComplete = false; failed.CaptureIssue = "FixtureCaptureFailure";
            Check(SearchEnvironmentIdentity.PresetMarkerNoticeKey(same, failed).EndsWith("current_unavailable", StringComparison.Ordinal) &&
                SearchEnvironmentIdentity.Compare(current, failed).Reasons.Contains("CurrentEnvironmentUnavailable") &&
                SearchEnvironmentIdentity.Compare(null, current).Reasons.Contains("HistoricalEnvironmentUnavailable"),
                "Current capture failure and missing historical evidence remain distinct reasons");
            Check(SearchEnvironmentIdentity.Describe(SearchEnvironmentIdentity.Compare(current, failed), true).Contains("FixtureCaptureFailure", StringComparison.Ordinal),
                "Current capture failure retains the actual capture reason");
            Check(SearchEnvironmentIdentity.PresetMarkerNoticeKey(same with { Source = SearchPresetSource.BuiltIn }, failed) == "",
                "Developer presets do not claim a personal saved marker");
            string key = store.QueryKeyForPreset(same);
            var links = store.PresetsForQuery(key);
            var catalog = store.Presets.GetAll();
            for (int i = 0; i < 100; i++)
                Check(ReferenceEquals(key, store.QueryKeyForPreset(same)) && ReferenceEquals(links, store.PresetsForQuery(key)) &&
                    ReferenceEquals(catalog, store.Presets.GetAll()), "Unchanged browsing reuses its exact semantic key, association index and sorted catalog");
            Check(key == QueryHistoryIdentity.Key(same.Workbench!), "Cached keys preserve the original canonical algorithm");
            var renamed = store.Presets.UpdateUserMetadata(same.Id, "renamed marker", "", []);
            Check(ReferenceEquals(key, store.QueryKeyForPreset(renamed)) && store.PresetsForQuery(key).Single().Title == renamed.Title,
                "Metadata edits refresh associations while retaining the frozen intent key");
            var edited = store.Presets.SaveUserPreset(renamed.Title, "", [], Capture(Intent) with { Workbench = draft with { Ascension = 9 }, Ascension = 9 });
            Check(store.QueryKeyForPreset(edited) != key && store.PresetsForQuery(key).Count == 0 && store.PresetsForQuery(store.QueryKeyForPreset(edited)).Single().Id == saved.Id,
                "Real intent replacement invalidates associations without changing identity normalization");
            Check(store.Presets.DeleteUserPreset(edited.Id) && store.PresetsForQuery(store.QueryKeyForPreset(edited)).Count == 0,
                "Deleting a preset invalidates its query association");
            store.FlushAll();
            var reopened = Workspace(directory);
            Check(!reopened.Presets.GetAll().Any(p => p.Id == saved.Id) && reopened.RecentQueries.Count == 0,
                "Reload uses committed preset changes and does not fabricate history");
            Console.WriteLine("PresetBrowseEvidence repeatedReads=100 stableKeyReference=true stableAssociationReference=true stableSortedCatalogReference=true;editDeleteInvalidated=true");
        });

        Case("query-history-identity", directory =>
        {
            var draft = Workbench();
            var a = new EventSequenceSearchCondition(1, null, SearchSequenceRangeMode.FirstN, 3, new([Event], [], []));
            var b = a with { RangeValue = 5 };
            string key = QueryHistoryIdentity.Key(draft with { Query = draft.Query with { EventSequenceConstraints = [a, b] } });
            Check(key == QueryHistoryIdentity.Key(draft with { Query = draft.Query with { EventSequenceConstraints = [b, a, a] } }),
                "Predicate display order and duplicate conjuncts do not split query history");
            Check(key != QueryHistoryIdentity.Key(draft with { Ascension = 9, Query = draft.Query with { EventSequenceConstraints = [a, b] } }), "Ascension remains part of query identity");
            var first = draft with { Query = draft.Query with { OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, [Relic, Missing], BonesRouteOrderMode.ExactOrder) } };
            var second = draft with { Query = draft.Query with { OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, [Missing, Relic], BonesRouteOrderMode.ExactOrder) } };
            Check(QueryHistoryIdentity.Key(first) != QueryHistoryIdentity.Key(second), "Ordered pickup is never normalized as an unordered set");
            var store = Workspace(directory);
            var context = new SeedLibraryContext("0.111.0", RuntimeProfileId.Beta111, WorldGameMode.Singleplayer, 10,
                [new(0, Character, new(), "fixture", AncientOptionConditionProfile.BroadDefault, SearchQuery.Empty, null)]);
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("one", draft, context));
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("two", draft, context with { GameVersion = "next-version" }));
            Check(store.RecentQueries.Count == 2, "Different game environments do not relabel old results as current matches");
        });

        Case("preset-current-environment-and-missing-mod", directory =>
        {
            var unlocks = new MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState();
            var modCharacter = new ModelKey("CHARACTER", "MODDED_FIXTURE_HERO");
            var draft = Workbench() with { Version = 4, Character = modCharacter,
                GameVersion = "old-game", Profile = RuntimeProfileId.Beta111, Mode = WorldGameMode.Multiplayer,
                Players = [new(0, modCharacter, unlocks, "CapturedLobbySlot"), new(1, Character, unlocks, "CapturedLobbySlot")],
                Query = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty), new(1, ModelKeySetFilter.Empty)] } };
            var store = Workspace(directory);
            var saved = store.Presets.SaveUserPreset("Mod hero", "Keep my character", [], Capture(Intent) with { CharacterKey = modCharacter, Workbench = draft });
            var reopened = Workspace(directory).Presets.GetAll().Single(p => p.Id == saved.Id);
            var intent = reopened.Workbench!;
            Check(intent.Version == 5 && intent.GameVersion.Length == 0 && intent.Profile is null &&
                intent.Players.All(p => p.Unlocks is null && p.UnlockSource == "CurrentContext"), "Preset persists unlock policy, not old game authority or slot snapshots");
            var missing = SearchPresetCompatibilityResolver.ResolveWorkbench(reopened, Authority);
            Check(!missing.CanLoad && missing.Unresolved.Any(r => r.StableIdentity == modCharacter.Serialized) &&
                reopened.Workbench!.Character == modCharacter, "Missing mod character is reported without replacing the saved character");
            var modAuthority = Authority with { SemanticUniverse = Authority.SemanticUniverse.Select(d => d.Domain == RuntimeAuthorityDomains.Characters
                ? d with { Items = d.Items.Append(new RuntimeSemanticItem(modCharacter.Serialized, [])).ToArray() } : d).ToArray() };
            Check(SearchPresetCompatibilityResolver.ResolveWorkbench(reopened, modAuthority).CanLoad, "Registered mod character resolves by stable identity in the current catalog");
            var current = new[] { new MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState(), new MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState() };
            var slots = new List<int>();
            var bound = intent.BindPresetIntent(RolltheSpire2.Bootstrap.ModRuntimeSnapshot.NotInitialized, slot => { slots.Add(slot); return current[slot]; });
            Check(slots.SequenceEqual(new[] { 0, 1 }) && bound.Players.All(p => p.Unlocks is not null) &&
                !ReferenceEquals(bound.Players[0].Unlocks, current[0]), "Every current-context slot reads and copies its current unlock state");
            Check(QueryHistoryIdentity.Key(draft) == QueryHistoryIdentity.Key(bound), "Rebinding runtime snapshots does not change intent");
            bound.Players[0].RequireUnlocks().NumberOfRuns = 73;
            Check(QueryHistoryIdentity.Key(draft) == QueryHistoryIdentity.Key(bound) &&
                QueryHistoryIdentity.EnvironmentKey(draft, "same-runtime") != QueryHistoryIdentity.EnvironmentKey(bound, "same-runtime"),
                "Actual slot unlock differences split environment, not query intent");
            Check(QueryHistoryIdentity.Key(intent) != QueryHistoryIdentity.Key(intent with { Players = [intent.Players[0] with { UnlockSource = "AssumedFullyUnlocked" }, intent.Players[1]] }),
                "Explicit unlock policy remains part of user intent");
            bool rejected = false;
            try { intent.BindPresetIntent(RolltheSpire2.Bootstrap.ModRuntimeSnapshot.NotInitialized, _ => throw new InvalidOperationException("no-lobby")); }
            catch (InvalidOperationException ex) { rejected = ex.Message == "no-lobby"; }
            Check(rejected && intent.Players.All(p => p.Unlocks is null), "Missing current state never falls back to saved snapshots");
        });

        Case("query-environment-navigation-and-evidence", directory =>
        {
            var store = Workspace(directory);
            var draft = Workbench();
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "environment-a" });
            store.ActivateQueryResults(draft);
            store.FlushAll(); store = Workspace(directory);
            Check(store.RecentQueries.Count == 0, "Opening an empty results page must not fabricate a historical run after restart");
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("a", draft));
            store.AppendResult(PersistableCandidate("000000000001"), "a");
            var first = store.RecentQueries.Single();
            var evidence = store.ResultAssociation("000000000001");
            Check(evidence is { Kind: SeedQueryAssociationKind.VerifiedFromSearch } && evidence.SearchRecordId == first.Id && evidence.EnvironmentFingerprint.Length > 0,
                "Search provenance includes intent, environment and record identity");
            store.ActivateQueryResults(draft with { Ascension = 9 });
            Check(store.DisplayResults.Count == 0 && first.Results.Count == 1, "Editing a different query shows no results without erasing the previous record");
            store.ActivateQueryResults(draft);
            Check(store.DisplayResults.Count == 1, "Returning to the same query/environment restores its results");
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "environment-b" });
            Check(store.ResultCountForQuery(draft) == 0, "Changed environment cannot present old results as current");
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("b", draft));
            store.AppendResult(PersistableCandidate("000000000002") with { IsUnverified = true }, "b");
            Check(store.RecentQueries.Count == 2 && store.ResultAssociation("000000000002") is
                { Kind: SeedQueryAssociationKind.UnverifiedFromSearch } unverified && unverified.SearchRecordId != first.Id,
                "Different environments split records; skipped checking preserves origin without match evidence");
            store.EnsureEnvironment(new() { CaptureComplete = false, CaptureIssue = "capture-failed" });
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("unknown", draft));
            store.AppendResult(PersistableCandidate("000000000003"), "unknown");
            Check(store.RecentQueries.Count == 3 && store.ResultAssociation("000000000003")?.Kind == SeedQueryAssociationKind.LegacyUnspecified,
                "Failed capture cannot reuse the old environment or invent confirmed evidence");
            var bookmarkStore = new SeedLibraryStore(Path.Combine(directory, "bookmarks"), []);
            var context = new SeedLibraryContext("0.111.0", RuntimeProfileId.Beta111, WorldGameMode.Singleplayer, 10,
                [new(0, Character, new(), "fixture", AncientOptionConditionProfile.BroadDefault, SearchQuery.Empty, null)]);
            bookmarkStore.Save("000000000001", context, "", evidence!.QueryKey, evidence);
            store.SetQueryHistoryLimit(1);
            Check(!store.Workspace.QueryHistory.Any(q => q.Id == first.Id) &&
                new SeedLibraryStore(Path.Combine(directory, "bookmarks"), []).GetAll().Single().QueryAssociations.Single() == evidence,
                "Favorite provenance survives original history eviction");
        });

        Case("query-history-current-preset-identity", directory =>
        {
            var store = Workspace(directory);
            var legacy = store.Presets.SaveUserPreset("legacy", "", [], Capture(Intent));
            var migratedIntent = legacy.Workbench! with { Version = 3 };
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("legacy-run", migratedIntent));
            store.AppendResult(PersistableCandidate("000000000001"), "legacy-run");
            store.SetQueryHistoryLimit(1);
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("next", Workbench() with { Ascension = 9 }));
            store.FlushAll(); store = Workspace(directory);
            Check(store.QueriesForPreset(legacy).Single().Results.Count == 1, "Migrated preset retains results through its complete current intent");
            Check(store.PresetsForQuery(QueryHistoryIdentity.Key(migratedIntent)).Single().Id == legacy.Id, "Seed-library grouping uses current preset intent identity");
        });

        Case("query-history-migration-protection", directory =>
        {
            var store = Workspace(directory);
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("old", Workbench()));
            store.AppendResult(PersistableCandidate("000000000001"), "old"); store.FlushAll();
            string file = Path.Combine(directory, "RolltheSpire2", "state", "search_workspace.json");
            var old = JsonNode.Parse(File.ReadAllText(file))!.AsObject(); old.Remove("QueryHistory"); old.Remove("ActiveHistoryId");
            File.WriteAllText(file, old.ToJsonString()); store = Workspace(directory);
            Check(store.RecentQueries.Single().Results.Single().Seed == "000000000001", "Pre-history latest batch migrates without losing its seed");
            Check(store.RecentQueries.Single().IdentityVersion == 1 && !store.RecentQueries.Single().EnvironmentKnown,
                "Migration does not assign current environment evidence to old results");
            store.FlushAll();
            var future = JsonNode.Parse(File.ReadAllText(file))!; future["QueryHistory"]![0]!["Draft"]!["Version"] = 999;
            string original = future.ToJsonString(); File.WriteAllText(file, original);
            store = Workspace(directory);
            Check(store.QueryHistoryIssue.Length > 0, "Unsupported historical query reports an issue without crashing the workbench");
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("new", Workbench())); store.FlushAll();
            Check(File.ReadAllText(file) == original, "Unknown history payload remains protected from overwrite");
        });

        Case("notes-independent-acknowledgements", directory =>
        {
            var provider = new DeveloperNotesDocumentProvider();
            var zhResult = provider.Resolve("zh");
            var enResult = provider.Resolve("en");
            var zh = zhResult.Document!;
            var en = enResult.Document!;
            Check(zh is not null && en is not null, $"Both indexed bundled languages load: {zhResult.Issue}; {enResult.Issue}");
            Check(zh!.Chapters.Select(c => c.Id).SequenceEqual(new[] { "release", "next", "positioning", "devlog", "contributors" }), "Contributors remains a permanent final notes chapter");
            Check(zh.Chapters.Select(c => (c.Id, c.RequiresAcknowledgement, c.AcknowledgementVersion))
                .SequenceEqual(en!.Chapters.Select(c => (c.Id, c.RequiresAcknowledgement, c.AcknowledgementVersion))), "Languages share acknowledgement metadata");
            var store = Workspace(directory);
            var release = zh.Chapters.Single(c => c.Id == DeveloperNotesChapterIds.Release);
            var positioning = zh.Chapters.Single(c => c.Id == DeveloperNotesChapterIds.Positioning);
            foreach (var document in new[] { zh, en })
            {
                var contributors = document.Chapters.Single(c => c.Id == DeveloperNotesChapterIds.Contributors);
                Check(!contributors.RequiresAcknowledgement && !store.NeedsNotesAcknowledgement(contributors),
                    "Contributors adds no mandatory reading or reminder in either language");
                Check(contributors.Blocks.Where(b => b.Type == DeveloperNotesBlockKinds.Heading).Select(b => b.Text)
                    .SequenceEqual(new[] { "James Zhou", "Egoistic今宵" }), "Both languages preserve Owner-confirmed public names and order");
                string content = string.Join("\n", contributors.Blocks.Select(b => b.Text));
                Check(content.Contains("@JamesZhou233", StringComparison.Ordinal) && content.Contains("@shion0528", StringComparison.Ordinal) &&
                    content.Contains("https://github.com/FalseOcean/rollthespire2/pull/4", StringComparison.Ordinal) &&
                    content.Contains("https://github.com/shion0528/rollthespire2/releases/tag/v1.3.5-resource.17", StringComparison.Ordinal),
                    "Both languages retain public accounts and actual contribution sources");
            }
            Check(zh.Chapters.Where(store.NeedsNotesAcknowledgement).Select(c => c.Id)
                .SequenceEqual(new[] { "release", "positioning" }), "New reader sees exactly the two required chapters");
            store.AcknowledgeNotesChapter(release);
            store = Workspace(directory);
            Check(!store.NeedsNotesAcknowledgement(en.Chapters.Single(c => c.Id == "release")) && store.NeedsNotesAcknowledgement(positioning),
                "Restart and language change retain release confirmation without acknowledging positioning");
            Check(!store.NeedsNotesAcknowledgement(release with { Title = "Copy edit" }), "Copy edits keep the explicit reading revision");
            Check(store.NeedsNotesAcknowledgement(release with { AcknowledgementVersion = "next-revision" }), "Only revised chapter becomes unread again");
            store.AcknowledgeNotesChapter(positioning);
            Check(!zh.Chapters.Any(store.NeedsNotesAcknowledgement), "Both required acknowledgements clear the reminder");
            foreach (var chapter in zh.Chapters)
            {
                var updated = chapter with { RequiresAcknowledgement = true, AcknowledgementVersion = "future" };
                Check(store.NeedsNotesAcknowledgement(updated), "Any chapter supports a future explicit reminder");
                store.AcknowledgeNotesChapter(updated);
                Check(!Workspace(directory).NeedsNotesAcknowledgement(updated), "Per-chapter future acknowledgement survives reload");
            }
            var fallback = new DeveloperNotesDocumentProvider(name =>
            {
                if (name == "positioning.zh-CN") return null;
                string resource = name == "index" ? "RolltheSpire2.Infrastructure.DeveloperNotes.index.json" :
                    $"RolltheSpire2.Texts.{(name.EndsWith(".zh-CN", StringComparison.Ordinal) ? "zh" : "en")}.DeveloperNotes.{name[..name.LastIndexOf('.')]}.json";
                return typeof(DeveloperNotesDocumentProvider).Assembly.GetManifestResourceStream(resource);
            }).Resolve("zh");
            Check(fallback.IsAvailable && fallback.UsedEnglishFallback && fallback.ResolvedLocale == "en-US", "Missing localized article retains complete English fallback");
        });

        Case("notes-legacy-confirmation", directory =>
        {
            string state = Path.Combine(directory, "RolltheSpire2", "state");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "user_preferences.json"), "{\"ReadReleaseNotesVersion\":\"1.3.3\",\"ReadNotesVersions\":null}");
            var store = Workspace(directory);
            var chapters = new DeveloperNotesDocumentProvider().Resolve("en").Document!.Chapters;
            var release = chapters.Single(c => c.Id == "release");
            Check(!store.NeedsNotesAcknowledgement(release with { AcknowledgementVersion = "1.3.3" }), "Legacy release acknowledgement migrates even with explicit null map");
            Check(store.NeedsNotesAcknowledgement(release), "Legacy 1.3.3 confirmation leaves the current release unread");
            Check(store.NeedsNotesAcknowledgement(chapters.Single(c => c.Id == "positioning")), "Legacy global acknowledgement never marks positioning read");
        });

        Case("in-run-console-preferences", directory =>
        {
            var store = Workspace(directory);
            Check(!store.Preferences.ShowInRunConsole && store.Preferences.InRunConsoleVerticalPercent == 50 &&
                store.Preferences.InRunConsoleScalePercent == 100, "Console starts disabled, centered and at normal scale");
            store.SetInRunConsole(false, 80, 120);
            store = Workspace(directory);
            Check(!store.Preferences.ShowInRunConsole && store.Preferences.InRunConsoleVerticalPercent == 80 &&
                store.Preferences.InRunConsoleScalePercent == 120, "Console visibility, position and scale survive reopen together");
            store.SetInRunConsole(true, -1, 999);
            store = Workspace(directory);
            Check(store.Preferences.ShowInRunConsole && store.Preferences.InRunConsoleVerticalPercent == 10 &&
                store.Preferences.InRunConsoleScalePercent == 125, "Console position and scale respect supported bounds");
            store.SetInRunConsole(true, double.NaN, double.PositiveInfinity);
            store = Workspace(directory);
            Check(store.Preferences.InRunConsoleVerticalPercent == 50 && store.Preferences.InRunConsoleScalePercent == 100,
                "Non-finite console values recover to finite defaults before persistence");
            string preferences = Path.Combine(directory, "RolltheSpire2", "state", "user_preferences.json");
            File.WriteAllText(preferences, "{\"SchemaVersion\":1,\"ShowInRunPredictionEntry\":false}");
            store = Workspace(directory);
            Check(!store.Preferences.ShowInRunConsole && store.Preferences.InRunConsoleVerticalPercent == 50 &&
                store.Preferences.InRunConsoleScalePercent == 100 && !store.Preferences.ShowInRunPredictionEntry,
                "Missing console preference defaults off while preserving the existing prediction setting");
        });

        Case("language-preferences-live-cache", directory =>
        {
            var first = SearchWorkspace.Open(directory);
            var stale = SearchWorkspace.Open(directory);
            first.SetLanguageOverride("en");
            stale.SetShowInRunPredictionEntry(!stale.Preferences.ShowInRunPredictionEntry);
            Check(stale.Preferences.LanguageOverride == "en" && SearchWorkspace.Open(directory).Preferences.LanguageOverride == "en",
                "An older workspace saving other preferences retains the current language override");
            stale.SetLanguageOverride("zh");
            first.SetSkipExactValidation(!first.Preferences.SkipExactValidation);
            Check(first.Preferences.LanguageOverride == "zh", "Language changes remain shared within one directory");
            var isolated = SearchWorkspace.Open(Path.Combine(directory, "other"));
            isolated.SetLanguageOverride("en");
            Check(SearchWorkspace.Open(directory).Preferences.LanguageOverride == "zh",
                "A temporary workspace cannot replace the real directory language override");
            first.SetLanguageOverride("");
            Check(SearchWorkspace.Open(directory).Preferences.LanguageOverride == string.Empty, "Follow-game language remains an unset override");
        });

        Case("in-run-feature-preferences", directory =>
        {
            var store = Workspace(directory);
            Check(!store.Preferences.OperationPredictionEnabled && !store.Preferences.MapPredictionEnabled &&
                !store.Preferences.ShowInRunPredictionEntry && !store.Preferences.ShowInRunConsole,
                "All four in-run features default off when preferences are missing");
            store.SetOperationPredictionEnabled(true);
            store.SetMapPredictionEnabled(true);
            var stale = Workspace(directory);
            store.SetOperationPredictionEnabled(false);
            Check(!Workspace(directory).Preferences.OperationPredictionEnabled && store.Preferences.MapPredictionEnabled,
                "Operation preference saves immediately without disabling map prediction");
            stale.SetMapPredictionEnabled(false);
            var reopened = Workspace(directory);
            Check(!reopened.Preferences.OperationPredictionEnabled && !reopened.Preferences.MapPredictionEnabled,
                "An older workspace cannot overwrite the other feature's newer choice");
            store.SetShowInRunPredictionEntry(false);
            store.SetInRunConsole(true, 50, 100);
            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "RolltheSpire2", "state", "user_preferences.json"))))
                Check(!saved.RootElement.GetProperty("OperationPredictionEnabled").GetBoolean() &&
                    !saved.RootElement.GetProperty("MapPredictionEnabled").GetBoolean(),
                    "An older workspace flushing unrelated preferences cannot overwrite either feature choice on disk");
            var other = Workspace(Path.Combine(directory, "other-workspace"));
            Check(!other.Preferences.OperationPredictionEnabled && !other.Preferences.MapPredictionEnabled &&
                !Workspace(directory).Preferences.OperationPredictionEnabled,
                "Temporary directories keep independent feature caches");
            store.SetOperationPredictionEnabled(true);
            reopened = Workspace(directory);
            Check(reopened.Preferences.OperationPredictionEnabled && !reopened.Preferences.MapPredictionEnabled &&
                reopened.Preferences.ShowInRunConsole && !reopened.Preferences.ShowInRunPredictionEntry,
                "Re-enabling one feature preserves other feature, console and entry preferences");
        });

        Case("in-run-explicit-preferences", directory =>
        {
            string state = Path.Combine(directory, "RolltheSpire2", "state");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "user_preferences.json"),
                """{"OperationPredictionEnabled":true,"MapPredictionEnabled":true,"ShowInRunPredictionEntry":true,"ShowInRunConsole":true,"FontChoice":"game-sans"}""");
            var store = Workspace(directory);
            Check(store.Preferences.OperationPredictionEnabled && store.Preferences.MapPredictionEnabled &&
                store.Preferences.ShowInRunPredictionEntry && store.Preferences.ShowInRunConsole,
                "Existing explicit opt-ins survive the new defaults");
            store.SetInRunConsole(false, 50, 100);
            var reopened = Workspace(directory);
            Check(reopened.Preferences.OperationPredictionEnabled && reopened.Preferences.MapPredictionEnabled &&
                reopened.Preferences.ShowInRunPredictionEntry && !reopened.Preferences.ShowInRunConsole &&
                reopened.Preferences.FontChoice == "game-sans", "An explicit off and unrelated preferences survive reopen");
        });

        Case("retired-appearance-migration", directory =>
        {
            foreach (string scheme in new[] { "stars", "custom", "ink" })
            {
                string userRoot = Path.Combine(directory, scheme);
                string state = Path.Combine(userRoot, "RolltheSpire2", "state");
                Directory.CreateDirectory(state);
                string path = Path.Combine(state, "user_preferences.json");
                var before = JsonNode.Parse("""{"SchemaVersion":1,"Appearance":{"Scheme":"custom","Background":"FF0000","Panel":"00FF00","Accent":"0000FF","Text":"111111"},"FontChoice":"game-sans","OperationPredictionEnabled":true,"MapPredictionEnabled":false,"ShowInRunPredictionEntry":true,"ShowInRunConsole":false,"SkipExactValidation":true,"FuturePreference":{"value":42}}""")!.AsObject();
                before["Appearance"]!["Scheme"] = scheme;
                File.WriteAllText(path, before.ToJsonString());
                var store = Workspace(userRoot);
                var after = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                before.Remove("Appearance");
                Check(store.Preferences.Appearance is null && JsonNode.DeepEquals(before, after),
                    "Only old Appearance is removed; every other JSON value survives: " + scheme);
                Check(store.Preferences.OperationPredictionEnabled && !store.Preferences.MapPredictionEnabled &&
                    store.Preferences.ShowInRunPredictionEntry && !store.Preferences.ShowInRunConsole &&
                    store.Preferences.FontChoice == "game-sans" && store.Preferences.SkipExactValidation,
                    "Explicit switches and unrelated settings survive appearance retirement: " + scheme);
                string once = File.ReadAllText(path);
                _ = Workspace(userRoot);
                Check(File.ReadAllText(path) == once, "Appearance migration is idempotent: " + scheme);
            }
            foreach (var (name, raw) in new[]
            {
                ("future", """{"SchemaVersion":999,"Appearance":{"Scheme":"stars"},"FuturePreference":42}"""),
                ("unreadable", "{broken preferences")
            })
            {
                string userRoot = Path.Combine(directory, name);
                string state = Path.Combine(userRoot, "RolltheSpire2", "state");
                Directory.CreateDirectory(state);
                string path = Path.Combine(state, "user_preferences.json");
                File.WriteAllText(path, raw);
                _ = Workspace(userRoot);
                string? preserved = name == "unreadable"
                    ? Directory.GetFiles(state, "user_preferences.json.corrupt-*").SingleOrDefault()
                    : path;
                Check(preserved is not null && File.Exists(preserved) && File.ReadAllText(preserved) == raw &&
                    (name != "unreadable" || !File.Exists(path)),
                    "Appearance migration preserves protected input and existing quarantine behavior: " + name);
            }
        });

        Case("crystal-sphere-preferences", directory =>
        {
            var store = Workspace(directory);
            Check(store.Preferences.CrystalSphereAvoidCurse && store.Preferences.CrystalSphereIncludeDriftwood &&
                !store.Preferences.CrystalSphereIncludeEnchantments && !store.Preferences.CrystalSphereTransparentMask,
                "Missing Crystal preferences retain the original opt-in defaults");
            var stale = Workspace(directory);
            store.SetCrystalSphereOptions(avoidCurse: false, includeDriftwood: false);
            stale.SetCrystalSphereOptions(includeEnchantments: true, transparentMask: true);
            store.SetShowInRunPredictionEntry(true);
            var reopened = Workspace(directory);
            Check(!reopened.Preferences.CrystalSphereAvoidCurse && !reopened.Preferences.CrystalSphereIncludeDriftwood &&
                reopened.Preferences.CrystalSphereIncludeEnchantments && reopened.Preferences.CrystalSphereTransparentMask,
                "Individual switches and unrelated older workspace saves preserve all latest Crystal choices");
            var isolated = Workspace(Path.Combine(directory, "other"));
            Check(isolated.Preferences.CrystalSphereAvoidCurse && isolated.Preferences.CrystalSphereIncludeDriftwood &&
                !isolated.Preferences.CrystalSphereIncludeEnchantments && !isolated.Preferences.CrystalSphereTransparentMask,
                "Temporary workspaces have independent Crystal preferences");
            string state = Path.Combine(directory, "RolltheSpire2", "state");
            string freshDirectory = Path.Combine(directory, "fresh-process-copy");
            string freshState = Path.Combine(freshDirectory, "RolltheSpire2", "state");
            Directory.CreateDirectory(freshState);
            File.Copy(Path.Combine(state, "user_preferences.json"), Path.Combine(freshState, "user_preferences.json"));
            var fresh = Workspace(freshDirectory);
            Check(!fresh.Preferences.CrystalSphereAvoidCurse && !fresh.Preferences.CrystalSphereIncludeDriftwood &&
                fresh.Preferences.CrystalSphereIncludeEnchantments && fresh.Preferences.CrystalSphereTransparentMask &&
                fresh.Preferences.ShowInRunPredictionEntry,
                "A directory without a memory cache restores the four switches from disk and keeps unrelated preferences");
        });

        Case("null-preferences-and-worker-budget", directory =>
        {
            string state = Path.Combine(directory, "RolltheSpire2", "state");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "user_preferences.json"), "{\"SchemaVersion\":1,\"WorkbenchFlags\":null,\"SearchWorkerBudget\":100}");
            var store = Workspace(directory);
            Check(store.Preferences.WorkbenchFlags.Count == 0, "Explicit null flags recover to empty preferences");
            Check(store.Preferences.SearchWorkerBudget == 64, "Persisted worker budget obeys factory bounds");
            foreach (int processors in new[] { 1, 2, 32, 128, 130, 256 })
            {
                int workers = SearchWorkspace.ResolveSearchWorkers(null, processors);
                Check(workers is >= 1 and <= 64, "Default worker budget is valid for high-core devices");
            }
            Check(SearchWorkspace.ResolveSearchWorkers(3, 256) == 3, "Explicit legal budget survives");
            Check(SearchWorkspace.ResolveSearchWorkers(null, 24) == 12 &&
                SearchWorkspace.ResolveSearchWorkers(null, 7) == 3 &&
                SearchWorkspace.ResolveSearchWorkers(null, 1) == 1, "Automatic workers use half the logical processors, minimum one");
            store.SetSearchWorkerBudget(5);
            store = Workspace(directory);
            Check(store.Preferences.SearchWorkerBudget == 5, "Edited worker budget persists");
            var runtimeSettings = new RolltheSpire2.Ui.Settings.RuntimePredictionSettings();
            store.ApplyPreferencesToRuntimeSettings(runtimeSettings);
            Check(runtimeSettings.SearchWorkerCount == 5, "Worker preference reaches captured runtime settings");
            store.SetSearchWorkerBudget(null);
            store = Workspace(directory);
            store.ApplyPreferencesToRuntimeSettings(runtimeSettings);
            Check(store.Preferences.SearchWorkerBudget is null && runtimeSettings.SearchWorkerCount ==
                SearchWorkspace.ResolveSearchWorkers(null, Environment.ProcessorCount), "Reset restores automatic budget after reopen");
            store.SetSearchWorkerBudget(0);
            Check(Workspace(directory).Preferences.SearchWorkerBudget == 1, "Worker editor minimum is persisted");
            store.SetSearchWorkerBudget(200);
            Check(Workspace(directory).Preferences.SearchWorkerBudget == 64, "Worker editor respects execution budget ceiling");
        });

        Case("interface-font-preference", directory =>
        {
            var store = Workspace(directory);
            Check(store.Preferences.FontChoice == "game-serif", "Missing preferences default to game Source Han Serif");
            foreach (string choice in new[] { "game-serif", "game-sans" })
            {
                store.SetFontChoice(choice);
                var restored = Workspace(directory);
                Check(restored.Preferences.FontChoice == choice, "Font preference survives reopen: " + choice);
                Check(restored.Preferences.Appearance is null, "Retired colors do not affect the font choice");
            }
            File.WriteAllText(Path.Combine(directory, "RolltheSpire2", "state", "user_preferences.json"), "{\"FontChoice\":\"source-han\"}");
            Check(Workspace(directory).Preferences.FontChoice == "game-serif", "Legacy embedded font migrates to the current default on load");
            Check(!typeof(SearchWorkspace).Assembly.GetManifestResourceNames().Any(n => n.Contains("SourceHanSans")),
                "The retired font is no longer embedded in the package");
            store.SetFontChoice("engine-default");
            Check(Workspace(directory).Preferences.FontChoice == "game-serif", "Unsupported choices default to game Source Han Serif");
        });

        Case("active-history-compatibility-projection", directory =>
        {
            var store = Workspace(directory); var draft = Workbench();
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "projection-environment" });
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("projection-query", draft));
            store.AppendResult(PersistableCandidate("000000000011"), "projection-query");
            var progress = new SearchProgressSnapshot(SearchRunState.Completed, 17, 1, 100, 30, 2, 8.5,
                SearchDisposition.Match, "", false);
            var continuation = new SearchContinuationSnapshot("projection-query", "projection-snapshot", 17, [], [11UL]);
            store.SaveCheckpoint(progress, continuation, 17UL); store.FlushAll();
            string path = Path.Combine(directory, "RolltheSpire2", "state", "search_workspace.json");
            string original = File.ReadAllText(path);
            var node = JsonNode.Parse(original)!;
            node["Results"] = new JsonArray(); node["ResultProgress"] = null; node["ResultContinuation"] = null;
            File.WriteAllText(path, node.ToJsonString());
            var restored = Workspace(directory); var history = restored.RecentQueries.Single();
            Check(restored.Workspace.Results.Single().Seed == "000000000011" &&
                ReferenceEquals(restored.Workspace.ResultProgress, history.Progress) &&
                ReferenceEquals(restored.Workspace.ResultContinuation, history.Continuation) &&
                restored.Workspace.QueryFingerprint == "projection-query" &&
                restored.Workspace.ResultBatchId == store.Workspace.ResultBatchId &&
                restored.Workspace.ResultsStartedAtUtc == store.Workspace.ResultsStartedAtUtc,
                "Restore generates compatibility fields from history while retaining the actual batch metadata");
            restored.ActivateQueryResults(draft with { Ascension = 9 });
            Check(restored.Workspace.Results.Count == 0 && restored.Workspace.ResultProgress is null && restored.Workspace.ResultContinuation is null,
                "Selecting an empty history clears all accumulated compatibility fields together");
            restored.ActivateQueryResults(draft);
            Check(restored.Workspace.Results.Single().Seed == "000000000011" &&
                ReferenceEquals(restored.Workspace.ResultProgress, history.Progress) && ReferenceEquals(restored.Workspace.ResultContinuation, history.Continuation),
                "Selecting saved history uses the same accumulated-state projection as restore");
            node = JsonNode.Parse(original)!; node["QueryHistory"] = new JsonArray();
            File.WriteAllText(path, node.ToJsonString());
            var migrated = Workspace(directory);
            Check(migrated.RecentQueries.Single().Progress?.ScannedCount == 17 &&
                migrated.RecentQueries.Single().Continuation?.NextOrdinal == 17 && migrated.Workspace.Results.Single().Seed == "000000000011",
                "Legacy active fields migrate into history before the compatibility projection is regenerated");
        });

        Case("detached-terminal-handoff", directory =>
        {
            var store = SearchWorkspace.Open(directory);
            Check(!store.Runtime.IsPolling && !store.Runtime.HasSearchInFlight,
                "Shared Open/preference access is passive and does not bind a frame receiver");
            var tail = new TailSession(PersistableCandidate("000000000000") with { IsUnverified = true });
            store.Runtime.InstallStartedSession(tail, "detached");
            var otherShell = SearchWorkspace.Open(directory);
            Check(ReferenceEquals(store, otherShell) && otherShell.Runtime.HasSearchInFlight,
                "Reopened shell shares the running search owner and cannot overwrite its cursor");
            store.Runtime.Tick();
            Check(!store.Runtime.Receiving, "Completion boundary observed by the shared receiver");
            store.Runtime.Tick();
            Check(!otherShell.Runtime.HasSearchInFlight, "Completed handoff allows a new search");
            store.FlushAll();
            var reloaded = Workspace(directory);
            Check(reloaded.Workspace.Results.Count == 1 && reloaded.Workspace.Results[0].IsUnverified,
                "Late candidate persists exactly once with provenance after UI loss");
            Check(reloaded.CurrentNextCursorOrdinal == 17, "Final safe cursor survives UI loss");
        });

        Case("shared-owner-frozen-receiver-and-cleanup", directory =>
        {
            var store = SearchWorkspace.Open(directory);
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "shared-owner-env" });
            var draft = Workbench();
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("original", draft));
            var history = store.RecentQueries.Single();
            var pending = new SearchPendingCandidate(5, "000000000005", "Exact", "OriginalFault", "original-owner-pending",
                "original", "original-snapshot", Captured);
            var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var session = new ControlledCleanupSession(PersistableCandidate("000000000011"),
                new("original", "original-snapshot", 17, [pending], [11UL]), cleanup.Task);
            store.Runtime.InstallStartedSession(session, "original");
            store.Runtime.Cancel();
            Check(session.Cancelled && store.Runtime.Receiving, "Close cancellation leaves the same owner responsible for its unread tail");
            // Change the selected receiver through an existing storage command,
            // as another shared shell can do; the running owner keeps its target.
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("other", draft with { Ascension = 9 }));
            string active = store.Workspace.ActiveHistoryId;
            var other = store.RecentQueries.Single(record => record.Id == active);
            store.Runtime.Tick();
            Check(!store.Runtime.Receiving && store.Runtime.CleanupCount == 1 && !store.Runtime.HasSearchInFlight &&
                store.Workspace.ActiveHistoryId == active && store.DisplayResults.Count == 0 && store.Workspace.ResultProgress is null &&
                other.Results.Count == 0 && other.Continuation is null && history.Results.Single().Seed == "000000000011" &&
                history.Continuation?.PendingCandidates.Single() == pending && history.ElapsedSeconds == 2 && store.CurrentNextCursorOrdinal == 17,
                "The closed session receives/checkpoints only to its frozen original history while cleanup stays independent of new-search availability");
            store.Runtime.InstallStartedSession(new TailSession(PersistableCandidate("000000000022")), "other");
            store.Runtime.Tick();
            Check(store.DisplayResults.Single().Seed == "000000000022" && history.Results.Count == 1 && store.Runtime.CleanupCount == 1,
                "A new search receives normally while the former session's cleanup is pending");
            var failure = new IOException("original-cleanup-failure", new InvalidOperationException("original-cleanup-inner"));
            cleanup.SetException(failure);
            store.Runtime.Tick(.6);
            Check(store.Runtime.CleanupCount == 0 && !store.Runtime.HasSearchInFlight &&
                history.Progress!.CleanupIssue.Contains(failure.ToString(), StringComparison.Ordinal) &&
                store.AuxiliaryFailures.Any(detail => detail.Contains(history.Id, StringComparison.Ordinal) && detail.Contains(failure.ToString(), StringComparison.Ordinal)),
                "Late cleanup keeps its original batch/history/cause without acquiring execution authority");
            var reloaded = Workspace(directory);
            Check(reloaded.RecentQueries.Single(record => record.Id == history.Id).Progress!.CleanupIssue.Contains("original-cleanup-inner", StringComparison.Ordinal) &&
                reloaded.Workspace.ActiveHistoryId == active && reloaded.DisplayResults.Single().Seed == "000000000022",
                "Cleanup diagnostics persist with no view and do not replace another receiver's result projection");
        });

        Case("auxiliary-recovery-is-stage-local-and-durable", directory =>
        {
            var store = Workspace(directory);
            var admission = new InvalidOperationException("integration.transform.capacity", new Exception("legacy-admission-inner"));
            var draftSave = new InvalidOperationException("integration.transform.capacity", new Exception("legacy-draft-inner"));
            var presentation = new IOException("current-results-render-failure", new Exception("current-render-inner"));
            store.RecordPresentationFailure(admission, "QueryAdmission");
            store.RecordPresentationFailure(draftSave, "DraftSave");
            store.RecordPresentationFailure(presentation, "ResultPresentation");
            store.FlushAll();
            // Reopen the same saved legacy notices before any recovery. A read
            // alone must not claim that either admission or saving succeeded.
            store = Workspace(directory);
            Check(store.Workspace.AuxiliaryIssues.Count == 3, "Reopen preserves unresolved legacy and real current failure notices");
            string path = WorkbenchPath(directory);
            store.SaveWorkbench(Workbench());
            Check(!store.Workspace.AuxiliaryIssues.Any(detail => detail.StartsWith("DraftSave: ", StringComparison.Ordinal)) &&
                store.Workspace.AuxiliaryIssues.Any(detail => detail.StartsWith("QueryAdmission: ", StringComparison.Ordinal)) &&
                store.Workspace.AuxiliaryIssues.Any(detail => detail.EndsWith(presentation.ToString(), StringComparison.Ordinal)),
                "Successful draft persistence recovers only its responsibility, retaining admission and actual display failures");
            store.ResolvePresentationFailure("QueryAdmission");
            Check(store.AuxiliaryFailures.Any(detail => detail.EndsWith(admission.ToString(), StringComparison.Ordinal)) &&
                store.AuxiliaryFailures.Any(detail => detail.EndsWith(draftSave.ToString(), StringComparison.Ordinal)),
                "Recovery retains original nested evidence in the existing diagnostic collection");
            store.FlushAll();
            store = Workspace(directory);
            Check(store.Workspace.AuxiliaryIssues.Single().EndsWith(presentation.ToString(), StringComparison.Ordinal) &&
                store.LoadWorkbench()!.Ascension == Workbench().Ascension,
                "Recovered notices stay recovered after reopen without replacing intent or unrelated current display failure");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Exception? failed = null;
                try { store.SaveWorkbench(Workbench() with { Ascension = 7 }); }
                catch (IOException exception) { failed = exception; store.RecordPresentationFailure(exception, "DraftSave"); }
                Check(failed?.InnerException is IOException or UnauthorizedAccessException &&
                    store.LastPersistenceFailure is IOException or UnauthorizedAccessException &&
                    store.Workspace.AuxiliaryIssues.Any(detail => detail.StartsWith("DraftSave: ", StringComparison.Ordinal)) &&
                    store.Workspace.AuxiliaryIssues.Any(detail => detail.StartsWith("Persistence:query_workbench.json: ", StringComparison.Ordinal)),
                    "A real failed write retains its original cause and both current save notices");
            }
            store.SaveWorkbench(Workbench() with { Ascension = 7 });
            store.FlushAll();
            var reopened = Workspace(directory);
            Check(reopened.LoadWorkbench()!.Ascension == 7 && reopened.LastPersistenceFailure is null &&
                reopened.Workspace.AuxiliaryIssues.Single().EndsWith(presentation.ToString(), StringComparison.Ordinal),
                "Successful retry clears draft/file notices durably while preserving the current render failure");
            reopened.ResolvePresentationFailure("ResultPresentation");
            reopened.FlushAll();
            Check(Workspace(directory).Workspace.AuxiliaryIssues.Count == 0,
                "Only a successful result presentation acknowledgement recovers the final display failure");
        });

        Case("cleanup-without-progress-durable", directory =>
        {
            var store = Workspace(directory);
            var failure = new IOException("cleanup-without-progress", new InvalidOperationException("original-no-progress-cause"));
            store.RecordCleanupFailure("original-history", "original-batch", null, failure);
            Check(!store.HasPendingWorkspaceCommit && Workspace(directory).Workspace.AuxiliaryIssues.Any(detail =>
                detail.Contains("original-batch", StringComparison.Ordinal) && detail.EndsWith(failure.ToString(), StringComparison.Ordinal)),
                "A final auxiliary cleanup cause commits without a page or previous progress snapshot");
        });

        Case("pending-atomic-result-cursor-handoff", directory =>
        {
            var store = Workspace(directory);
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "env-a" });
            store.ObserveSafeNextCursor(3); store.FlushAll();
            var draft = Workbench();
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("semantic", draft));
            store.FlushAll();
            string state = Path.Combine(directory, "RolltheSpire2", "state");
            string workspace = Path.Combine(state, "search_workspace.json");
            string cursor = Path.Combine(state, "search_cursor.json");
            var original = new InvalidOperationException("outer-pending-fault", new IOException("original-inner-fault"));
            var pending = new SearchPendingCandidate(5, "000000000005", "Exact", "ExactFailed", original.ToString(),
                "semantic", "snapshot", Captured);
            var continuation = new SearchContinuationSnapshot("semantic", "snapshot", 17, [pending], [11UL]);
            var session = new CheckpointSession(PersistableCandidate("000000000011"), continuation);
            store.Runtime.InstallStartedSession(session, "semantic");
            using (var locked = new FileStream(workspace, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int accepted = 0;
                session.BeforeAcknowledgement = () => { Check(store.DisplayResults.Count == 1, "In-memory receipt exists before ACK"); accepted++; };
                store.Runtime.Tick();
                Check(!store.Runtime.Receiving, "Terminal handoff accepts before presentation");
                Check(accepted == 1 && session.Acknowledged, "Candidate is appended in memory before ACK/checkpoint");
                try { throw new IOException("injected-result-render-failure"); }
                catch (Exception exception) { store.RecordPresentationFailure(exception, "ResultPresentation"); }
                store.FlushAll();
                Check(store.DisplayResults.Count == 1 && store.DisplayPendingCandidates.Count == 1,
                    "Save/render failures cannot lose accepted results or pending or count pending as results");
                Check(store.HasUnsavedSearchState, "Failed workspace commit remains explicitly unsaved");
                var saveFailure = store.LastPersistenceFailure;
                Check(saveFailure is IOException or UnauthorizedAccessException &&
                    saveFailure.StackTrace?.Contains("SearchPersistenceFile.WriteAtomic", StringComparison.Ordinal) == true &&
                    store.AuxiliaryFailures.Any(detail => detail.EndsWith(saveFailure.ToString(), StringComparison.Ordinal)),
                    "Uncommitted search state retains the concrete original file failure, stack and full diagnostic text");
                Check(JsonSerializer.Deserialize<SearchCursorDocument>(File.ReadAllText(cursor), Json)!.NextCursor == 3 &&
                    Workspace(directory).CurrentNextCursorOrdinal == 3, "Cursor cannot pass uncommitted result/pending state");
            }
            store.Runtime.Tick(); store.FlushAll();
            var reloaded = Workspace(directory);
            Check(reloaded.CurrentNextCursorOrdinal == 17 && reloaded.Workspace.Results.Count == 1 &&
                reloaded.DisplayPendingCandidates.Single().ExceptionText == original.ToString(),
                "One workspace commit restores results, continuation, cursor and complete exception chain");
            Check(reloaded.Workspace.ResultProgress is { State: SearchRunState.Completed, PendingCandidateCount: 1 } &&
                reloaded.Workspace.ResultProgress.CleanupIssue == "cleanup-original", "Final progress retains pending and cleanup evidence");
            Check(reloaded.ContinuationFor(draft, LegacyContinuationPlan("semantic", "snapshot")) is not null &&
                reloaded.ContinuationFor(draft, LegacyContinuationPlan("semantic", "other-snapshot")) is null, "Only exact query/snapshot continuation can be retried");
            for (int i = 0; i < 35; i++) reloaded.AppendResult(PersistableCandidate((100 + i).ToString("D12")), "semantic");
            reloaded.SetQueryHistoryLimit(1);
            reloaded.BindPreparedResultWorkspace(reloaded.PrepareResultWorkspace("new-query", draft with { Ascension = 9 }));
            reloaded.SetQueryHistoryLimit(1); reloaded.FlushAll();
            Check(Workspace(directory).PendingHistories.Single().Continuation!.PendingCandidates.Single().Seed == pending.Seed,
                "Pending history survives both result quota and normal history eviction");
            reloaded.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "env-b" });
            Check(reloaded.ContinuationFor(draft, LegacyContinuationPlan("semantic", "snapshot")) is null && reloaded.PendingHistories.Count == 1,
                "Changed environment preserves historical pending without reusing it as current evidence");
        });

        Case("result-ownership-prepared-before-start", directory =>
        {
            var store = Workspace(directory);
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "ownership-env" });
            var oldDraft = Workbench();
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("old-query", oldDraft));
            store.AppendResult(PersistableCandidate("000000000001"), "old-query");
            string oldHistory = store.Workspace.ActiveHistoryId, oldBatch = store.Workspace.ResultBatchId;
            var cards = new ModelKey?[] { Card };
            var newDraft = oldDraft with { Ascension = 9, Query = oldDraft.Query with
                { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, cards) } };
            var prepared = store.PrepareResultWorkspace("new-query", newDraft);
            Check(store.Workspace.ActiveHistoryId == oldHistory && store.Workspace.ResultBatchId == oldBatch &&
                store.DisplayResults.Single().Seed == "000000000001" && store.Workspace.QueryHistory.Count == 1,
                "Preparing a new receiver cannot alter the current history or results before session start succeeds");
            cards[0] = Missing;
            Check(prepared.Workspace.ResultDraft!.Query.CombatCardRewards!.Slots.Single() == Card,
                "Prepared request is frozen independently of later editor-owned slot mutations");
            bool invalid = false;
            try { store.PrepareResultWorkspace("invalid-query", newDraft with { Query = null! }); }
            catch (NullReferenceException) { invalid = true; }
            Check(invalid && store.Workspace.ActiveHistoryId == oldHistory && store.Workspace.ResultBatchId == oldBatch,
                "A structural preparation error occurs before start and leaves the old receiver untouched");
            store.BindPreparedResultWorkspace(prepared);
            store.AppendResult(PersistableCandidate("000000000002"), "new-query");
            Check(store.Workspace.ActiveHistoryId != oldHistory && store.Workspace.QueryFingerprint == "new-query" &&
                store.Workspace.QueryHistory.Single(history => history.Id == oldHistory).Results.Single().Seed == "000000000001" &&
                store.DisplayResults.Single().Seed == "000000000002",
                "Binding installs the new query's receiver atomically and cannot append its result to the old history");
            store.FlushAll();
            var reloaded = Workspace(directory);
            Check(reloaded.Workspace.QueryFingerprint == "new-query" && reloaded.Workspace.ResultDraft!.Ascension == 9 &&
                reloaded.Workspace.QueryHistory.Single(history => history.Id == oldHistory).Results.Single().Seed == "000000000001",
                "Committed ownership preserves both the frozen current request and the former query's results");
        });

        Case("presentation-cannot-interrupt-owner-checkpoint", directory =>
        {
            var store = Workspace(directory);
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "notification-env" });
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("semantic", Workbench()));
            var pending = new SearchPendingCandidate(5, "000000000005", "Exact", "OriginalFault", "original-pending-cause",
                "semantic", "snapshot", Captured);
            var continuation = new SearchContinuationSnapshot("semantic", "snapshot", 17, [pending], [11UL]);
            var session = new CheckpointSession(PersistableCandidate("000000000011"), continuation);
            store.Runtime.InstallStartedSession(session, "semantic");
            var renderFailure = new InvalidOperationException("accepted-listener-render-fault", new IOException("original-listener-cause"));
            long priorRevision = store.ResultsRevision;
            store.Runtime.Tick();
            try { throw renderFailure; }
            catch (Exception exception) { store.RecordPresentationFailure(exception, "ResultPresentation"); }
            bool complete = !store.Runtime.Receiving;
            Check(complete && session.Acknowledged && store.DisplayResults.Count == 1 && store.ResultsRevision > priorRevision,
                "A failed renderer reads an already accepted owner revision and cannot change terminal delivery or lose the result");
            Check(store.Workspace.ResultContinuation == continuation && store.DisplayPendingCandidates.Single() == pending &&
                store.Workspace.ResultProgress is { State: SearchRunState.Completed, PendingCandidateCount: 1 },
                "The same handoff captures continuation and final progress before a renderer can fail");
            Check(store.AuxiliaryFailures.Any(detail => detail.EndsWith(renderFailure.ToString(), StringComparison.Ordinal)),
                "Renderer failure retains its complete original exception chain");
            store.Runtime.Tick(); store.FlushAll();
            var reloaded = Workspace(directory);
            Check(!store.Runtime.HasSearchInFlight && reloaded.CurrentNextCursorOrdinal == 17 &&
                reloaded.Workspace.Results.Count == 1 && reloaded.DisplayPendingCandidates.Single() == pending,
                "Renderer failure cannot block owner release or the atomic result/pending/cursor commit");
        });

        Case("pending-snapshot-history-retention", directory =>
        {
            var store = Workspace(directory);
            var draft = Workbench();
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "same-env" });
            var pending = new SearchPendingCandidate(7, "000000000007", "Exact", "OriginalFault", "full-original-cause",
                "semantic", "old-snapshot", Captured);
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("semantic", draft));
            store.Runtime.InstallStartedSession(new CheckpointSession(PersistableCandidate("000000000001"),
                new("semantic", "old-snapshot", 10, [pending], [])), "semantic");
            store.Runtime.Tick();
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("semantic", draft));
            store.Runtime.InstallStartedSession(new CheckpointSession(PersistableCandidate("000000000002"),
                new("semantic", "new-snapshot", 20, [], [])), "semantic");
            store.Runtime.Tick();
            store.FlushAll();
            var reloaded = Workspace(directory);
            Check(reloaded.DisplayPendingCandidates.Count == 0 && reloaded.PendingHistories.Count == 1,
                "A new snapshot does not inherit the old pending fault as a current condition failure");
            Check(SearchWorkspace.PendingForHistory(reloaded.PendingHistories.Single()).Single() == pending,
                "The old snapshot's pending seed and original cause survive replacement and restart");
            Check(reloaded.ContinuationFor(draft, LegacyContinuationPlan("semantic", "old-snapshot"))?.PendingCandidates.Single() == pending &&
                reloaded.ContinuationFor(draft, LegacyContinuationPlan("semantic", "new-snapshot"))?.PendingCandidates.Count == 0 &&
                reloaded.ContinuationFor(draft, LegacyContinuationPlan("semantic", "unseen-snapshot")) is null,
                "Explicit restarts select only the exact retained snapshot, including its own pending work");
        });

        Case("retained-search-batches", directory =>
        {
            var store = Workspace(directory);
            foreach (int playerCount in new[] { 1, 2 })
            {
                var players = Enumerable.Range(0, playerCount).Select(slot => new SeedLibraryPlayer(slot,
                    slot == 0 ? Character : BaseGameModelKeys.Characters.Ironclad,
                    JsonSerializer.Deserialize<MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState>("{}")!,
                    "fixture-" + slot, AncientOptionConditionProfile.BroadDefault,
                    playerCount == 1 ? SearchQuery.Empty : SearchQuery.Empty with { OpeningRoute = new(Relic) }, null)).ToArray();
                var context = new SeedLibraryContext("0.111.0", RuntimeProfileId.Beta111,
                    playerCount == 1 ? WorldGameMode.Singleplayer : WorldGameMode.Multiplayer, 10, players);
                var draft = Workbench();
                var candidate = PersistableCandidate("000000000001") with
                { Witnesses = [new("000000000001", "choice.2.saved-opening", [], [], "", null, [])] };
                if (playerCount > 1)
                {
                    draft = draft with { Version = 4, Mode = WorldGameMode.Multiplayer, GameVersion = "0.111.0", Profile = RuntimeProfileId.Beta111,
                        Players = players.Select(p => new WorkbenchPlayerDraft(p.Slot, p.Character, p.Unlocks, p.UnlockSource)).ToArray(),
                        Query = SearchQuery.Empty with { Players = players.Select(p => new PlayerOfferQuery(p.Slot, ModelKeySetFilter.Empty)
                            { Conditions = p.OpeningPremise }).ToArray() } };
                    var party = new PartySeedInformation(candidate.Seed, 1, "0.111.0", 10, "fixture", [], [],
                        players.Select(p => new PlayerInitialOffers(p.Slot, p.Character, [Relic]) { UnlockSource = p.UnlockSource }).ToArray())
                    { Transactions = players.Select(p => new RolltheSpire2.Core.Effects.PartyNeowResult(p.Slot, Relic,
                        RolltheSpire2.Core.Effects.PartyNeowIntent.ResultTarget, [], [], []) { OpeningRouteId = "saved-opening-" + p.Slot }).ToArray() };
                    candidate = candidate with { Document = new SeedPredictionDocument { Context = null!, PredictorId = "fixture",
                        OriginalSeed = candidate.Seed, CanonicalSeed = candidate.Seed, Sections = [], Warnings = [], Diagnostics = [],
                        OverallStatus = SeedPredictionOverallStatus.Unknown, Party = party } };
                }
                store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("retained-" + playerCount, draft, context, skipExactValidation: true));
                string batchId = store.Workspace.ResultBatchId;
                store.AppendResult(candidate, store.Workspace.QueryFingerprint);
                store.AppendResult(PersistableCandidate("000000000002") with { IsUnverified = true }, store.Workspace.QueryFingerprint);
                store.SaveResultProgress(new(SearchRunState.Cancelled, 321, 1, 1000, 30, 2, 160,
                    SearchDisposition.Match, "", true));
                store.SaveWorkbench(Workbench() with { Ascension = 1 });
                players[0] = players[0] with { Character = BaseGameModelKeys.Characters.Necrobinder };
                store.FlushAll();
                store = Workspace(directory);
                Check(store.Workspace.ResultBatchId == batchId && store.Workspace.Results.Count == 2, "Restart retains exactly the latest batch");
                Check(store.LoadWorkbench()!.Ascension == 1 && store.Workspace.ResultDraft!.Ascension == 10 &&
                    store.Workspace.ResultContext!.Players[0].Character == Character, "Saved context is detached from later editor and roster changes");
                Check(store.Workspace.ResultProgress?.ScannedCount == 321 && store.Workspace.ResultsStartedAtUtc.HasValue,
                    "Historical count, elapsed progress and timestamp survive");
                var saved = store.Workspace.Results[0];
                Check(!saved.IsUnverified && store.Workspace.Results[1].IsUnverified && store.Workspace.ResultsSkipExactValidation,
                    "Restart preserves verified and unverified provenance independently");
                SeedLibraryStore.ValidateContext(saved.Context!);
                Check(saved.Context!.Players.Count == playerCount && saved.Context.Players.All(p =>
                    p.Selection!.Route == (playerCount == 1 ? "saved-opening" : "saved-opening-" + p.Slot)),
                    "Each result preserves its concrete opening and party slot ownership");
                Check(saved.Context.Players[0].Selection!.ChoiceSlot == (playerCount == 1 ? 2 : 1), "Original option selection survives");
            }
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "old", GameVersionIdentity = "old" });
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "new", GameVersionIdentity = "new" });
            Check(Workspace(directory).Workspace.Results.Count == 2, "Environment changes preserve historical seeds");
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("next")); store.FlushAll();
            Check(Workspace(directory).Workspace.Results.Count == 0 && Workspace(directory).Workspace.ResultBatchId.Length > 0,
                "Only successful start replaces the batch, including zero-result searches");
            store.AppendResult(PersistableCandidate("000000000003"), "legacy"); store.FlushAll();
            Check(Workspace(directory).Workspace.Results[0].Context is null, "Older context-free rows remain readable without invented context");
        });

        Case("workbench-prepared-missing-and-fresh-bytes", directory =>
        {
            var store = Workspace(directory);
            var missing = Task.Run(store.PrepareWorkbenchRead).GetAwaiter().GetResult();
            Check(missing.Draft is null && missing.Error is null, "Prepared missing file is distinct from a parse failure");
            store.AdoptPreparedWorkbenchRead(missing);
            Check(store.LoadWorkbench() is null, "An unchanged missing file restores no draft");
            var first = Workbench() with { Ascension = 1 };
            store.SaveWorkbench(first);
            string path = WorkbenchPath(directory);
            byte[] original = File.ReadAllBytes(path);
            var prepared = Task.Run(store.PrepareWorkbenchRead).GetAwaiter().GetResult();
            Check(ReferenceEquals(store.RefreshPreparedWorkbenchRead(prepared), prepared), "Pure refresh reuses the same receipt for unchanged full bytes");
            Check(prepared.Error is null && prepared.Draft is not null && store.LastPersistenceFailure is null &&
                original.SequenceEqual(File.ReadAllBytes(path)), "Worker preparation only reads and parses the original bytes");
            store.AdoptPreparedWorkbenchRead(prepared);
            Check(ReferenceEquals(store.LoadWorkbench(), prepared.Draft), "An unchanged file reuses the prepared DTO");

            var beforeEdit = store.PrepareWorkbenchRead();
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            string changed = File.ReadAllText(path).Replace("\"Ascension\": 1", "\"Ascension\": 2", StringComparison.Ordinal);
            File.WriteAllText(path, changed);
            File.SetLastWriteTimeUtc(path, timestamp);
            Check(File.ReadAllBytes(path).Length == original.Length, "External replacement fixture has identical size and timestamp");
            store.AdoptPreparedWorkbenchRead(beforeEdit);
            Check(!ReferenceEquals(store.RefreshPreparedWorkbenchRead(beforeEdit), beforeEdit), "Pure refresh replaces a receipt after same-size raw-byte changes");
            Check(store.LoadWorkbench()!.Ascension == 2, "Adoption rereads full bytes and detects same-size external replacement");

            var beforeSave = store.PrepareWorkbenchRead();
            store.SaveWorkbench(first);
            store.AdoptPreparedWorkbenchRead(beforeSave);
            Check(store.LoadWorkbench()!.Ascension == 1, "A save supersedes an in-flight prepared draft");
            var sameBytesBeforeSave = store.PrepareWorkbenchRead();
            store.SaveWorkbench(first);
            store.AdoptPreparedWorkbenchRead(sameBytesBeforeSave);
            Check(!ReferenceEquals(store.LoadWorkbench(), sameBytesBeforeSave.Draft), "Saving invalidates receipts even when written bytes are unchanged");

            var beforeDelete = store.PrepareWorkbenchRead();
            File.Delete(path);
            store.AdoptPreparedWorkbenchRead(beforeDelete);
            Check(store.LoadWorkbench() is null, "External deletion supersedes a prepared DTO");
            store.AdoptPreparedWorkbenchRead(store.PrepareWorkbenchRead());
            File.WriteAllBytes(path, original);
            Check(store.LoadWorkbench()!.Ascension == 1, "A newly created draft supersedes prepared absence");
            bool wrongDirectory = false;
            try { Workspace(Path.Combine(directory, "other")).AdoptPreparedWorkbenchRead(prepared); }
            catch (ArgumentException) { wrongDirectory = true; }
            Check(wrongDirectory, "Prepared receipts cannot cross workspace directories");
        });

        Case("workbench-prepared-failure-and-recovery", directory =>
        {
            var store = Workspace(directory);
            string path = WorkbenchPath(directory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            const string raw = "{\"Query\":";
            File.WriteAllText(path, raw);
            var failed = Task.Run(store.PrepareWorkbenchRead).GetAwaiter().GetResult();
            Check(failed.Draft is null && failed.Error is JsonException && store.LastPersistenceFailure is null &&
                File.ReadAllText(path) == raw && Directory.GetFiles(Path.GetDirectoryName(path)!, "*.corrupt-*").Length == 0,
                "Worker parse failure retains the original exception without quarantine or diagnostics");
            store.AdoptPreparedWorkbenchRead(failed);
            InvalidDataException? rejected = null;
            try { store.LoadWorkbench(); } catch (InvalidDataException exception) { rejected = exception; }
            Check(rejected?.Message == "integration.load_shape" && ReferenceEquals(rejected.InnerException, failed.Error) &&
                ReferenceEquals(store.LastPersistenceFailure, failed.Error), "Main-thread adoption reports the original prepared cause");
            Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.corrupt-*").Any(p => File.ReadAllText(p) == raw),
                "Quarantine occurs only when the prepared failure is actually adopted");
            var fresh = Workbench() with { Ascension = 3 };
            store.RecoverWorkbench(fresh);
            store.AdoptPreparedWorkbenchRead(failed);
            Check(Same(fresh.WithoutCapturedAuthority(), store.LoadWorkbench()), "Explicit recovery invalidates the failed receipt");

            File.WriteAllText(path, raw);
            var repairedExternally = store.PrepareWorkbenchRead();
            File.WriteAllText(path, JsonSerializer.Serialize(fresh.WithoutCapturedAuthority(), Json));
            store.AdoptPreparedWorkbenchRead(repairedExternally);
            Check(store.LoadWorkbench()!.Ascension == 3 && File.Exists(path), "A repaired file supersedes a prepared parse error without quarantining the repair");
        });

        Case("workbench-solo-and-party", directory =>
        {
            var store = Workspace(directory);
            Check(store.LoadWorkbench() is null, "Missing draft is distinct from corruption");
            var solo = Workbench();
            store.SaveWorkbench(solo);
            Check(Same(solo.WithoutCapturedAuthority(), Workspace(directory).LoadWorkbench()), "Solo intent round trip");
            foreach (int count in new[] { 2, 3, 4 })
            {
                var players = Enumerable.Range(0, count).Select(slot => new WorkbenchPlayerDraft(slot, Character,
                    JsonSerializer.Deserialize<MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState>("{}")!, "Synthetic")
                { AncientEditor = new(0, 1, [new([], true, [new("RELIC", "SEAT_" + slot)], 1)]) }).ToArray();
                var query = SearchQuery.Empty with { Players = Enumerable.Range(0, count).Select(slot =>
                    new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)
                    {
                        Conditions = SearchQuery.Empty with { CombatCardRewards = new(6, CombatRewardSequenceOrderMode.Ordered,
                            [null, null, null, null, null, new("CARD", "SEAT_" + slot)]) }
                    }).ToArray() };
                var party = solo with { Version = 4, Mode = WorldGameMode.Multiplayer, Players = players, Query = query };
                store.SaveWorkbench(party);
                var loaded = Workspace(directory).LoadWorkbench()!;
                Check(Same(party, loaded), "Party draft preserves roster, personal queries and editor snapshots");
                foreach (int slot in Enumerable.Range(0, count).Reverse())
                    Check(loaded.Query.Players.Single(p => p.Slot == slot).Conditions.CombatCardRewards!.Slots[5] == new ModelKey("CARD", "SEAT_" + slot),
                        "Reading another seat retains personal condition ownership");
                store.SaveWorkbench(solo);
                Check(Same(solo.WithoutCapturedAuthority(), Workspace(directory).LoadWorkbench()), "Switch back to solo persists solo intent");
            }
            var legacy = JsonSerializer.SerializeToNode(solo, Json)!;
            legacy["Version"] = 1;
            legacy.AsObject().Remove("AncientEditor");
            legacy.AsObject().Remove("Players");
            File.WriteAllText(WorkbenchPath(directory), legacy.ToJsonString());
            Check(Workspace(directory).LoadWorkbench() is { Version: 1, Players.Count: 0 }, "v1 optional fields retain defaults");
        });

        foreach (string shape in new[] { "truncated", "null", "unknown", "missing-query", "null-query", "invalid-key" })
            Case("workbench-" + shape, directory =>
            {
                var node = JsonSerializer.SerializeToNode(Workbench(), Json)!;
                if (shape == "unknown") node["Query"]!["FutureCondition"] = true;
                if (shape == "missing-query") node.AsObject().Remove("Query");
                if (shape == "null-query") node["Query"] = null;
                if (shape == "invalid-key") node["Character"] = "not-a-model-key";
                string raw = shape == "truncated" ? "{\"Query\":" : shape == "null" ? "null" : node.ToJsonString();
                Directory.CreateDirectory(Path.GetDirectoryName(WorkbenchPath(directory))!);
                File.WriteAllText(WorkbenchPath(directory), raw);
                bool rejected = false;
                try { Workspace(directory).LoadWorkbench(); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Invalid document must signal load failure");
                Check(Directory.GetFiles(Path.GetDirectoryName(WorkbenchPath(directory))!, "query_workbench.json*").Any(p => File.ReadAllText(p) == raw),
                    "Original invalid bytes retained");
            });

        Case("workspace-future-and-old", directory =>
        {
            string path = Path.Combine(directory, "RolltheSpire2", "state", "search_workspace.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // This format is read compatibility only; current starts write result/history state.
            File.WriteAllText(path, JsonSerializer.Serialize(new SearchWorkspaceDocument
            { CharacterKey = Character.Serialized, Ascension = 10, Draft = Intent,
                RunDraft = new("000000000000", 1, 1, 1) }, Json));
            Check(Same(Intent, Workspace(directory).Workspace.Draft), "Legacy workspace semantic draft round trip");
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            node["SchemaVersion"] = 99;
            string future = node.ToJsonString(); File.WriteAllText(path, future);
            var protectedStore = Workspace(directory);
            protectedStore.BindPreparedResultWorkspace(protectedStore.PrepareResultWorkspace("current", Workbench()));
            protectedStore.FlushWorkspace();
            Check(File.ReadAllText(path) == future, "Future workspace remains untouched");
            node["SchemaVersion"] = 1;
            node["Draft"]!["CombatRewardDraft"] = new JsonObject { ["Rows"] = new JsonArray() };
            node.AsObject().Remove("RunDraft");
            File.WriteAllText(path, node.ToJsonString());
            var old = Workspace(directory).Workspace;
            Check(old.Draft!.NeowAny == Relic.Serialized && !old.Draft.CombatRewardDraft.HasAnyValue && old.RunDraft is null,
                "Existing old battle-bundle/run-draft migration remains scoped to those blocks");
        });

        int futureSchemaCase = 0;
        foreach (string schemaFields in new[] { "\"SchemaVersion\":99", "\"SchemaVersion\":99,\"schemaVERSION\":1",
            "\"SchemaVersion\":1,\"schemaVERSION\":99" })
            Case("workspace-future-enum-" + futureSchemaCase++, directory =>
            {
                string path = Path.Combine(directory, "RolltheSpire2", "state", "search_workspace.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string original = "{" + schemaFields + ",\"Results\":[{\"ValidationDisposition\":\"FutureDisposition\"}]}";
                File.WriteAllText(path, original);
                var store = Workspace(directory);
                Check(store.LastPersistenceFailure is InvalidDataException failure && failure.Message == "UnsupportedFutureSchema:99",
                    "Header rejects every future schema occurrence before deserializing unknown enum values");
                store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("current", Workbench()));
                store.FlushAll();
                Check(File.ReadAllText(path) == original && Directory.GetFiles(Path.GetDirectoryName(path)!, "*.corrupt-*").Length == 0,
                    "Future schema remains protected in place with its original bytes");
            });

        Case("workbench-write-failure-retry", directory =>
        {
            var store = Workspace(directory); store.SaveWorkbench(Workbench());
            string path = WorkbenchPath(directory), before = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    bool failed = false;
                    try { store.SaveWorkbench(Workbench() with { Ascension = 7 }); } catch (IOException) { failed = true; }
                    Check(failed && File.ReadAllText(path) == before, "Failed replace reports error and preserves committed bytes");
                }
                Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp*").Any(), "Failed writes clean their own temporary files");
            }
            File.WriteAllText(path + ".tmp-interrupted", "{\"uncommitted\":true}");
            Check(Workspace(directory).LoadWorkbench()!.Ascension == 10, "Interrupted write cannot supersede committed draft");
            store.SaveWorkbench(Workbench() with { Ascension = 7 });
            Check(Workspace(directory).LoadWorkbench()!.Ascension == 7, "Retry commits after lock released");
        });

        foreach (string shape in new[] { "future-version", "future-schema", "unknown", "truncated" })
            Case("workbench-explicit-recovery-" + shape, directory =>
            {
                string path = WorkbenchPath(directory);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var node = JsonSerializer.SerializeToNode(Workbench(), Json)!;
                if (shape == "future-version") node["Version"] = 99;
                if (shape == "future-schema") node["SchemaVersion"] = 99;
                if (shape == "unknown") node["Query"]!["FutureCondition"] = true;
                string raw = shape == "truncated" ? "{\"Query\":" : node.ToJsonString();
                File.WriteAllText(path, raw);
                var store = Workspace(directory);
                var prepared = Task.Run(store.PrepareWorkbenchRead).GetAwaiter().GetResult();
                Check(prepared.Error is not null && File.ReadAllText(path) == raw && store.LastPersistenceFailure is null,
                    "Preparing unsupported or malformed intent has no persistence side effects");
                store.AdoptPreparedWorkbenchRead(prepared);
                bool rejected = false;
                try { store.LoadWorkbench(); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Recovery begins from a visible load failure");
                string[] quarantined = Directory.GetFiles(Path.GetDirectoryName(path)!, "query_workbench.json.corrupt-*");
                bool originalInPlace = File.Exists(path);
                if (originalInPlace)
                {
                    bool protectedWrite = false;
                    try { store.SaveWorkbench(Workbench()); } catch (IOException) { protectedWrite = true; }
                    Check(protectedWrite && File.ReadAllText(path) == raw, "Normal save cannot replace unknown/future draft");
                }
                var fresh = Workbench() with { Query = SearchQuery.Empty, Ascension = 0 };
                store.RecoverWorkbench(fresh);
                Check(Same(fresh, Workspace(directory).LoadWorkbench()), "Explicit recovery commits the new draft");
                string[] backups = Directory.GetFiles(Path.GetDirectoryName(path)!, "query_workbench.json.recovery-*");
                Check(originalInPlace ? backups.Length == 1 && File.ReadAllText(backups[0]) == raw : quarantined.Length > 0,
                    "Original intent is backed up before replacement or remains quarantined");
                Check(quarantined.All(p => File.Exists(p) && File.ReadAllText(p) == raw), "Recovery leaves existing corrupt evidence intact");
                store.SaveWorkbench(fresh with { Ascension = 3 });
                Check(Workspace(directory).LoadWorkbench()!.Ascension == 3, "Only successful recovery releases write protection");
            });

        foreach (bool allowBackupRead in new[] { false, true })
            Case("workbench-recovery-lock-" + allowBackupRead, directory =>
            {
                string path = WorkbenchPath(directory);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string raw = JsonSerializer.Serialize(Workbench() with { Version = 99 }, Json);
                File.WriteAllText(path, raw);
                var store = Workspace(directory);
                try { store.LoadWorkbench(); } catch (InvalidDataException) { }
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, allowBackupRead ? FileShare.Read : FileShare.None))
                {
                    bool failed = false;
                    try { store.RecoverWorkbench(Workbench()); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
                    Check(failed, allowBackupRead ? "Locked destination rejects recovery commit" : "Unreadable original rejects recovery backup");
                }
                Check(File.ReadAllText(path) == raw, "Failed preservation or commit leaves original bytes untouched");
                Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp-*").Any(), "Failed recovery cleans only its temporary output");
                string[] backups = Directory.GetFiles(Path.GetDirectoryName(path)!, "query_workbench.json.recovery-*");
                Check(allowBackupRead ? backups.Length == 1 && File.ReadAllText(backups[0]) == raw : backups.Length == 0,
                    "Successful backup survives a later commit failure");
                bool stillProtected = false;
                try { store.SaveWorkbench(Workbench()); } catch (IOException) { stillProtected = true; }
                Check(stillProtected && File.ReadAllText(path) == raw, "Recovery failure does not silently release protection");
                store.RecoverWorkbench(Workbench());
                Check(Workspace(directory).LoadWorkbench()!.Version == 3, "Explicit recovery retries after lock release");
                Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "query_workbench.json.recovery-*").All(p => File.ReadAllText(p) == raw),
                    "Recovery retry never replaces previous backup evidence");
            });

        Case("cursor-continuous-checkpoint", directory =>
        {
            var store = Workspace(directory);
            string path = Path.Combine(directory, "RolltheSpire2", "state", "search_cursor.json");
            for (ulong i = 1; i <= 16; i++) { store.ObserveSafeNextCursor(i); store.Tick(0.1); }
            ulong first = Workspace(directory).CurrentNextCursorOrdinal;
            Check(first is >= 15 and <= 16, "Continuous 0.1s progress still checkpoints within 1.5s");
            for (ulong i = 17; i <= 32; i++) { store.ObserveSafeNextCursor(i); store.Tick(0.1); }
            Check(Workspace(directory).CurrentNextCursorOrdinal is >= 30 and <= 32, "Checkpoint clock restarts after successful flush");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                store.ObserveSafeNextCursor(33); store.Tick(1.5);
                Check(JsonSerializer.Deserialize<SearchCursorDocument>(File.ReadAllText(path), Json)!.NextCursor < 33,
                    "Failed cursor mirror write preserves its old bytes");
                Check(Workspace(directory).CurrentNextCursorOrdinal == 33,
                    "Workspace commit is authoritative when the cursor mirror write fails");
            }
            for (ulong i = 34; i <= 49; i++) { store.ObserveSafeNextCursor(i); store.Tick(0.1); }
            Check(Workspace(directory).CurrentNextCursorOrdinal is >= 48 and <= 49, "Dirty checkpoint retries despite continuing progress");
        });

        Case("workspace-continuous-results", directory =>
        {
            var store = Workspace(directory);
            for (int i = 1; i <= 8; i++) { store.AppendResult(PersistableCandidate(i.ToString("D12")), "fixture"); store.Tick(0.1); }
            Check(Workspace(directory).Workspace.Results.Count >= 6, "Incoming results persist without waiting for an idle interval");
            for (int i = 9; i <= 16; i++) { store.AppendResult(PersistableCandidate(i.ToString("D12")), "fixture"); store.Tick(0.1); }
            Check(Workspace(directory).Workspace.Results.Count >= 12, "Workspace checkpoint repeats during ongoing delivery");
            store.FlushWorkspace();
            Check(Workspace(directory).Workspace.Results.Count == 16, "Final flush persists the remaining results exactly once");
            for (int i = 0; i < 8; i++) { store.SetLastPage("page-" + i); store.Tick(0.1); }
            Check(!File.Exists(Path.Combine(directory, "RolltheSpire2", "state", "user_preferences.json")), "Preference edits retain debounce behavior");
            store.Tick(0.6);
            Check(Workspace(directory).Preferences.LastPage == "page-7", "Preference debounce commits after edits settle");
        });

        Case("preset-roundtrip-and-old-schema", directory =>
        {
            var store = new SearchPresetStore(directory);
            var saved = store.Save("fixture", "description", [SearchPresetVisualIconRef.FromRelic(Relic)], Capture(Intent));
            var loaded = new SearchPresetStore(directory).GetPresets().Single();
            Check(Same(saved.Workbench, loaded.Workbench), "Current intent round trip");
            Check(Resolve(loaded).CanLoad, "Known content fully resolves");
            var exported = SearchPresetStore.ToPersisted(loaded, Json);
            var imported = SearchPresetStore.Normalize(JsonSerializer.Deserialize<SearchPresetStore.PersistedSearchPreset>(JsonSerializer.Serialize(exported, Json), Json)!, SearchPresetSource.User, Json);
            Check(Same(imported.Workbench, Capture(Intent).Workbench) && Same(imported.Workbench, loaded.Workbench), "Export/import DTO preserves exact query payload");
            var old = new SearchPresetStore.PersistedSearchPreset { SchemaVersion = 1, Id = "user:legacy", Name = "old",
                CharacterKey = Character.Serialized, Draft = JsonSerializer.SerializeToElement(Intent, Json), VisualRelicKey = Relic.Serialized };
            var projected = SearchPresetStore.Normalize(old, SearchPresetSource.User, Json);
            Check(projected.Title == "old" && projected.VisualIcons.Single().StableContentIdentity == Relic.Serialized && Same(projected.Workbench, Capture(Intent).Workbench), "v1 name/icon/query projection");
            var sparse = JsonNode.Parse("{\"NeowAny\":\"" + Relic.Serialized + "\"}")!;
            Check(Resolve(Normalize(sparse)).CanLoad, "Missing optional legacy fields retain established defaults");
            sparse["StartSeed"] = "old-run-cursor"; sparse["ScanCount"] = 123; sparse["TargetMatchCount"] = 1; sparse["WorkerCount"] = 2;
            Check(Resolve(Normalize(sparse)).CanLoad, "Retired run controls remain readable and inert");
            Check(new BuiltInSearchPresetProvider().GetPresets().Count == 4 && new BuiltInSearchPresetProvider().GetPresets().All(p => p.QueryResolved), "All four bundled presets retain typed query compatibility");
        });

        foreach (string shape in new[] { "unknown", "nested-unknown", "null-block", "missing-block-members" })
            Case("preset-" + shape, directory =>
            {
                var query = JsonSerializer.SerializeToNode(Intent, Json)!;
                if (shape == "unknown") query["FutureCondition"] = new JsonObject { ["Required"] = true };
                if (shape == "nested-unknown") query["NeowRouteDraft"]!["FutureCondition"] = true;
                if (shape == "null-block") query["CombatRewardDraft"] = null;
                if (shape == "missing-block-members") query["CombatRewardDraft"] = new JsonObject();
                var preset = Normalize(query);
                var resolution = Resolve(preset);
                Check(!resolution.CanLoad && preset.UnresolvedAsset is not null && resolution.Issue.Length > 0, "Unrepresentable query is explicit, never Full");
                var store = new SearchPresetStore(directory);
                var copy = store.SaveFromExisting("preserved", "", [], preset);
                store.UpdateMetadata(copy.Id, "renamed", "updated", []);
                var reloaded = new SearchPresetStore(directory).GetPresets().Single();
                Check(JsonNode.DeepEquals(query, JsonNode.Parse(reloaded.UnresolvedAsset!.OriginalJson)!["QuerySnapshot"]), "Unresolved query survives save-as and metadata edits");
            });

        Case("preset-imported-file-identity", directory =>
        {
            var store = new SearchPresetStore(directory);
            var saved = store.Save("original", "", [], Capture(Intent));
            string path = Directory.GetFiles(Path.Combine(directory, "search_presets"), "*.json").Single();
            string importedPath = Path.Combine(Path.GetDirectoryName(path)!, "000-imported.json");
            File.Move(path, importedPath);
            store = new SearchPresetStore(directory);
            store.UpdateMetadata(saved.Id, "renamed", "", []);
            Check(new SearchPresetStore(directory).GetPresets().Single().Title == "renamed", "Update operates on imported source file");
            Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.json").Length == 1, "Update creates no stale duplicate");
            Check(store.Delete(saved.Id) && new SearchPresetStore(directory).GetPresets().Count == 0, "Delete cannot resurrect imported preset on restart");
        });

        Case("preset-legacy-source-and-deletion-overlay", directory =>
        {
            string folder = Path.Combine(directory, "search_presets"); Directory.CreateDirectory(folder);
            var old = new SearchPresetStore.PersistedSearchPreset { SchemaVersion = 2, Id = "user:legacy-overlay",
                Title = "legacy", CharacterKey = Character.Serialized, QuerySnapshot = JsonSerializer.SerializeToElement(Intent, Json) };
            string path = Path.Combine(folder, "source.json"), original = JsonSerializer.Serialize(old, Json);
            File.WriteAllText(path, original);
            var store = new SearchPresetStore(directory);
            Check(store.GetPresets().Single().Workbench is { Version: 5 } && Directory.GetFiles(folder).Length == 1 && File.ReadAllText(path) == original,
                "Legacy reads create current memory intent without writing or changing source bytes");
            store.UpdateMetadata(old.Id, "converted", "", []);
            var reopened = new SearchPresetStore(directory);
            Check(reopened.GetPresets().Single().Title == "converted" && reopened.LoadIssues.Count == 0 && File.ReadAllText(path) == original,
                "Explicit metadata save writes current overlay which outranks preserved old source");
            string overlay = Directory.GetFiles(folder).Single(file => file != path);
            using (var locked = new FileStream(overlay, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(!reopened.Delete(old.Id) && reopened.GetPresets().Count == 1 && File.ReadAllText(path) == original,
                    "Failed deletion marker preserves memory and both committed assets");
            Check(reopened.Delete(old.Id) && new SearchPresetStore(directory).GetPresets().Count == 0 && File.ReadAllText(path) == original,
                "Migration deletion marker prevents resurrection while retaining old source bytes");
        });

        Case("temporary-legacy-source-preserved", directory =>
        {
            var old = new SearchPresetStore.PersistedSearchPreset { SchemaVersion = 1, Id = "temporary:old", CharacterKey = Character.Serialized,
                Draft = JsonSerializer.SerializeToElement(Intent, Json), CreatedAtUtc = Captured };
            string path = Path.Combine(directory, "temporary_search_presets.json"), original = JsonSerializer.Serialize(new[] { old }, Json);
            File.WriteAllText(path, original);
            var store = new TemporarySearchPresetStore(directory);
            Check(store.GetPresets().Single().Workbench is { Version: 5 } && !File.Exists(Path.Combine(directory, "temporary_search_presets.format4.json")),
                "Temporary legacy history migrates in memory without writes");
            var preferred = SearchPresetStore.ToPersisted(Definition(Intent) with
                { Id = "temporary:preferred", Source = SearchPresetSource.Temporary }, Json);
            string current = Path.Combine(directory, "temporary_search_presets.format4.json");
            string preferredBytes = JsonSerializer.Serialize(new[] { preferred }, Json);
            File.WriteAllText(current, preferredBytes);
            Check(File.ReadAllText(path) == original && new TemporarySearchPresetStore(directory).GetPresets().Single().Id == preferred.Id &&
                File.ReadAllText(current) == preferredBytes,
                "Existing current history wins over legacy history without changing either original file");
            File.WriteAllText(current, "[{");
            Check(new TemporarySearchPresetStore(directory).GetPresets().Count == 0 && File.ReadAllText(path) == original,
                "Unreadable preferred history reports its own failure without silently replacing it by the legacy file");
        });

        Case("preset-id-is-not-path", directory =>
        {
            string outside = Path.Combine(directory, "outside.json"); File.WriteAllText(outside, "preserve");
            string presets = Path.Combine(directory, "search_presets"); Directory.CreateDirectory(presets);
            var payload = SearchPresetStore.ToPersisted(Definition(Intent), Json); payload.Id = "user:../outside";
            File.WriteAllText(Path.Combine(presets, "imported.json"), JsonSerializer.Serialize(payload, Json));
            var store = new SearchPresetStore(directory);
            var entry = store.GetPresets().Single();
            store.UpdateMetadata(entry.Id, "updated", "", []);
            Check(File.ReadAllText(outside) == "preserve", "Imported identity cannot redirect update out of preset directory");
            Check(store.Delete(entry.Id) && File.ReadAllText(outside) == "preserve", "Imported identity cannot redirect delete");
        });

        Case("preset-write-failure", directory =>
        {
            var store = new SearchPresetStore(directory);
            var saved = store.Save("old", "", [], Capture(Intent));
            string path = Directory.GetFiles(Path.Combine(directory, "search_presets"), "*.json").Single(), before = File.ReadAllText(Directory.GetFiles(Path.Combine(directory, "search_presets"), "*.json").Single());
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false;
                try { store.UpdateMetadata(saved.Id, "new", "", []); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
                Check(failed && store.GetPresets().Single().Title == "old" && File.ReadAllText(path) == before, "Failed save preserves in-memory and committed asset");
                Check(!store.Delete(saved.Id) && store.GetPresets().Count == 1, "Failed delete preserves catalog entry");
                Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp*").Any(), "Failed preset write cleans temporary file");
            }
            File.WriteAllText(path + ".tmp", "interrupted");
            Check(new SearchPresetStore(directory).GetPresets().Single().Title == "old", "Preset loader ignores incomplete temporary file");
            store.UpdateMetadata(saved.Id, "new", "", []);
            Check(new SearchPresetStore(directory).GetPresets().Single().Title == "new", "Preset retry succeeds");
        });

        Case("preset-invalid-files", directory =>
        {
            string folder = Path.Combine(directory, "search_presets"); Directory.CreateDirectory(folder);
            var future = SearchPresetStore.ToPersisted(Definition(Intent), Json); future.SchemaVersion = 99;
            var originals = new Dictionary<string, string> { ["future.json"] = JsonSerializer.Serialize(future, Json), ["null.json"] = "null", ["truncated.json"] = "{\"private\":\"DO_NOT_DISPLAY\"", ["empty.json"] = "" };
            foreach (var pair in originals) File.WriteAllText(Path.Combine(folder, pair.Key), pair.Value);
            var store = new SearchPresetStore(directory);
            Check(store.GetPresets().Count == 0, "Bad entries are skipped without preventing catalog access");
            Check(store.LoadIssues.Count == originals.Count, "Every skipped user file provides a read issue");
            var issues = new SearchPresetCatalog(directory).LoadIssues;
            Check(issues.Any(issue => issue.StartsWith("future.json:", StringComparison.Ordinal) && issue.Contains("UnsupportedFutureSchema:99")) &&
                issues.Any(issue => issue.StartsWith("truncated.json:", StringComparison.Ordinal)), "Catalog surfaces future and invalid file issues");
            Check(issues.All(issue => !issue.Contains(directory, StringComparison.Ordinal) && !issue.Contains("DO_NOT_DISPLAY", StringComparison.Ordinal)),
                "User-visible issues contain file names and reason codes, never paths or file contents");
            store.Save("valid", "", [], Capture(Intent));
            foreach (var pair in originals) Check(File.ReadAllText(Path.Combine(folder, pair.Key)) == pair.Value, "Bad/future source file is preserved on unrelated save");
        });

        foreach (string shape in new[] { "truncated", "future", "invalid-entry" })
            Case("temporary-preserve-" + shape, directory =>
            {
                string path = Path.Combine(directory, "temporary_search_presets.json");
                var entry = SearchPresetStore.ToPersisted(Definition(Intent) with { Id = "temporary:fixture", Source = SearchPresetSource.Temporary }, Json);
                if (shape == "future") entry.SchemaVersion = 99;
                string raw = shape == "truncated" ? "[{" : shape == "invalid-entry" ? "[null]" : JsonSerializer.Serialize(new[] { entry }, Json);
                File.WriteAllText(path, raw);
                var store = new TemporarySearchPresetStore(directory);
                Check(store.GetPresets().Count == 0, "Unreadable/future history is not accepted as current intent");
                Check(store.LoadIssues.Count == 1 && new SearchPresetCatalog(directory).LoadIssues
                    .Any(issue => issue.StartsWith("temporary_search_presets.json:", StringComparison.Ordinal)),
                    "Protected temporary history reports an actionable catalog issue");
                Check(File.ReadAllText(path) == raw && new TemporarySearchPresetStore(directory).GetPresets().Count == 0,
                    "Repeated reading of failed history preserves its original bytes and unresolved state");
            });

        Case("temporary-capacity-and-interruption", directory =>
        {
            var entries = Enumerable.Range(0, 6).Select(index => SearchPresetStore.ToPersisted(Definition(Intent) with
                { Id = "temporary:capacity" + index, Source = SearchPresetSource.Temporary, Ascension = index,
                    Workbench = Definition(Intent).Workbench! with { Ascension = index }, CreatedAtUtc = Captured.AddSeconds(index) }, Json))
                .OrderByDescending(entry => entry.CreatedAtUtc).ToArray();
            string path = Path.Combine(directory, "temporary_search_presets.format4.json");
            string before = JsonSerializer.Serialize(entries, Json); File.WriteAllText(path, before);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(new TemporarySearchPresetStore(directory).GetPresets().Select(p => p.Ascension).SequenceEqual(new[] { 5, 4, 3, 2, 1 }),
                    "Existing temporary assets retain the five-entry display capacity");
                Check(File.ReadAllText(path) == before && !Directory.EnumerateFiles(directory, "*.tmp*").Any(),
                    "Reading a locked history never writes, compacts or creates temporary files");
            }
            File.WriteAllText(path + ".tmp", "[");
            Check(new TemporarySearchPresetStore(directory).GetPresets().First().Ascension == 5 && File.ReadAllText(path) == before,
                "Reload ignores an orphan temporary file and preserves the complete source");
        });

        Case("temporary-mixed-history", directory =>
        {
            var valid = SearchPresetStore.ToPersisted(Definition(Intent) with { Id = "temporary:known", Source = SearchPresetSource.Temporary }, Json);
            var future = SearchPresetStore.ToPersisted(Definition(Intent) with { Id = "temporary:future", Source = SearchPresetSource.Temporary }, Json);
            future.SchemaVersion = 99;
            string raw = JsonSerializer.Serialize(new[] { valid, future }, Json), path = Path.Combine(directory, "temporary_search_presets.json");
            File.WriteAllText(path, raw);
            var store = new TemporarySearchPresetStore(directory);
            Check(store.GetPresets().Single().Id == "temporary:known", "Mixed history keeps its readable entries available");
            Check(File.ReadAllText(path) == raw && new TemporarySearchPresetStore(directory).GetPresets().Count == 1,
                "Reload keeps readable history without compacting over unknown entries");
        });

        foreach (string shape in new[] { "future", "unknown-query" })
        Case("temporary-" + shape + "-tail", directory =>
        {
            var entries = Enumerable.Range(0, 6).Select(index => SearchPresetStore.ToPersisted(Definition(Intent) with
                { Id = "temporary:tail" + index, Source = SearchPresetSource.Temporary }, Json)).ToArray();
            if (shape == "future") entries[5].SchemaVersion = 99;
            else
            {
                var query = JsonSerializer.SerializeToNode(Intent, Json)!;
                query["FutureCondition"] = true;
                entries[5].QuerySnapshot = JsonSerializer.SerializeToElement(query, Json);
            }
            string raw = JsonSerializer.Serialize(entries, Json), path = Path.Combine(directory, "temporary_search_presets.json");
            File.WriteAllText(path, raw);
            var store = new TemporarySearchPresetStore(directory);
            Check(store.GetPresets().Count == 5 && store.LoadIssues.Any(issue => issue.Contains(shape == "future"
                    ? "UnsupportedFutureSchema:99" : "PresetQueryShapeUnresolved")),
                "History validates and reports unknown entries beyond the visible capacity");
            Check(new TemporarySearchPresetStore(directory).GetPresets().Count == 5 && File.ReadAllText(path) == raw,
                "Reload retains visible entries and never rewrites the unreadable tail");
        });

        foreach (string shape in new[] { "single-token", "set-token", "ordered-token", "sequence-token", "typed-invalid", "relic-range", "event-act", "shop-card", "shop-relic", "typed-fallback" })
            Case("compatibility-" + shape, _ =>
            {
                SearchDraft draft = shape switch
                {
                    "single-token" => Intent with { RequiredFinalCurse = "invalid" },
                    "set-token" => Intent with { BannedFinalCurses = "invalid" },
                    "ordered-token" => Intent with { BonesAcquisitionOrder = "invalid" },
                    "sequence-token" => Intent with { RelicSequenceConditions = "Common FirstN 3 ANY invalid" },
                    "typed-invalid" => Intent with { RelicSequenceDraft = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 2, new([default], [], []))] },
                    "relic-range" => Intent with { RelicSequenceDraft = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 0, new([Relic], [], []))] },
                    "event-act" => Intent with { EventSequenceDraft = [new(0, null, SearchSequenceRangeMode.FirstN, 2, new([Event], [], []))] },
                    "shop-card" => Intent with { MerchantColorlessSequenceDraft = [new(3, CombatRewardSequenceOrderMode.Unordered, MerchantColorlessSlot.Rare, [null, new("CARD", "MISSING"), Card])] },
                    "shop-relic" => Intent with { RelicShopSequenceDraft = [new(3, CombatRewardSequenceOrderMode.Ordered, [Relic, null, Missing])] },
                    _ => Intent with { RelicSequenceDraft = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 2, new([Missing], [], []))], RelicSequenceConditions = "Common FirstN 2 ANY " + Relic.Serialized }
                };
                var resolution = Resolve(Definition(draft) with { ConditionCount = 2 });
                Check(!resolution.CanLoad && (resolution.Unresolved.Count > 0 || resolution.Issue.Length > 0),
                    "Malformed or missing condition makes complete preset unavailable, never a partial projection");
                var preserved = Definition(draft);
                Check(preserved.Workbench is not null || preserved.UnresolvedAsset is not null,
                    "Unavailable content retains full current intent or opaque original asset");
                if (preserved.Workbench is { } current)
                {
                    Check(current.Query.LegacyNeow.NeowRelics.Any.Contains(Relic), "Independent condition remains in stored intent");
                    if (shape == "typed-fallback") Check(current.Query.RelicSequenceConstraints.Single().Keys.Any.Contains(Missing),
                        "Typed missing condition remains authored; inert fallback never revives");
                    if (shape == "shop-card") Check(current.Query.MerchantColorlessSequenceConditions.Single().Slots.Contains(new ModelKey("CARD", "MISSING")),
                        "Missing shop card remains in its complete sequence");
                    if (shape == "shop-relic") Check(current.Query.RelicShopSequenceConditions.Single().Slots.Contains(Missing),
                        "Missing shop relic remains in its complete sequence");
                }
            });

        Case("compatibility-existing-policy", _ =>
        {
            var missing = Empty with { NeowAny = Missing.Serialized };
            Check(!Resolve(Definition(missing)).CanLoad, "No remaining authored conditions is unavailable");
            var group = Intent with { NeowBan = Missing.Serialized, RequiredFinalCurse = Card.Serialized };
            var groupPreset = Definition(group) with { ConditionCount = 2 };
            Check(!Resolve(groupPreset).CanLoad && groupPreset.Workbench!.Query.LegacyNeow.NeowRelics.Any.Contains(Relic) &&
                groupPreset.Workbench.Query.LegacyNeow.NeowRelics.Ban.Contains(Missing),
                "Any/All/Ban intent remains whole instead of removing a missing member or condition");
            Check(SearchPresetCompatibilityResolver.ResolveWorkbench(Definition(Intent), RuntimeAuthoritySnapshot.Unavailable("fixture", "fixture")).Issue == "CurrentSemanticAuthorityIncomplete",
                "Incomplete authority remains explicit unavailable");
            var legacy = Intent with { RelicSequenceConditions = "COMMON FIRST 2 ANY " + Relic.Serialized,
                EventSequenceConditions = "A2 SHARED FIRST 3 ALL " + Event.Serialized };
            Check(Resolve(Definition(legacy)).CanLoad, "Existing legacy sequence grammar converts at read boundary");
            var eventFallback = Intent with { EventSequenceDraft = [new(2, null, SearchSequenceRangeMode.FirstN, 2, new([new("EVENT", "MISSING")], [], []))],
                EventSequenceConditions = legacy.EventSequenceConditions };
            var eventPreset = Definition(eventFallback);
            Check(!Resolve(eventPreset).CanLoad && eventPreset.Workbench!.Query.EventSequenceConstraints.Single().Keys.Any.Contains(new("EVENT", "MISSING")),
                "Typed event priority is preserved even when its target is missing");
            foreach (bool bothMissing in new[] { false, true })
            {
                var morphic = Intent with { MorphicGroveContainsCard = bothMissing ? new("CARD", "MISSING") : Card,
                    MorphicGroveSecondCard = new("CARD", "MISSING_SECOND") };
                var morphicPreset = Definition(morphic);
                Check(!Resolve(morphicPreset).CanLoad && morphicPreset.Workbench!.Query.EventResultConditions.Single() is
                    { MorphicGroveSecondCard: { } second } && second == new ModelKey("CARD", "MISSING_SECOND"),
                    "Morphic multiset is retained as one authored condition; missing identity blocks complete use");
            }
            var shopOnly = Empty with { RelicShopSequenceDraft = [new(1, CombatRewardSequenceOrderMode.Ordered, [Missing])] };
            Check(!Resolve(Definition(shopOnly) with { ConditionCount = 0 }).CanLoad, "Missing legacy condition count includes modern shop sequences");
            var inactiveShopSlots = Intent with { RelicShopSequenceDraft = [new(1, CombatRewardSequenceOrderMode.Ordered, [Relic, Missing])],
                MerchantColorlessSequenceDraft = [new(1, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Rare, [Card, new("CARD", "MISSING")])] };
            var inactive = Definition(inactiveShopSlots);
            Check(!Resolve(inactive).CanLoad && inactive.Workbench!.Query.RelicShopSequenceConditions.Single().Slots.Contains(Missing),
                "Current template retains dormant shop selections and requires them to resolve indivisibly");
        });

        Console.WriteLine($"PERSISTENCE checks={checks}; failures={failures}; fixtures={root}");
        return failures == 0 ? 0 : 1;
    }

    private sealed class TailSession(SearchCandidate candidate) : IProductionSearchSession
    {
        private bool _complete, _read;
        public Task Completion { get { _complete = true; return Task.CompletedTask; } }
        public void Cancel() { }
        public bool TryReadCandidate(out SearchCandidate? value)
        {
            value = _complete && !_read ? candidate : null;
            if (value is null) return false;
            _read = true; return true;
        }
        public ulong GetSafeNextOrdinal() => _complete ? 17UL : 3UL;
        public bool TryGetSafeNextOrdinal(out ulong value) { value = GetSafeNextOrdinal(); return true; }
        public SearchProgressSnapshot GetProgress() => new(SearchRunState.Completed, 17, 0, 100, 30, 2, 8.5,
            SearchDisposition.NoMatch, "", false) { UnverifiedCandidateCount = 1 };
        public SearchDiagnosticSummarySnapshot GetDiagnosticSummary() => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ControlledCleanupSession(SearchCandidate candidate, SearchContinuationSnapshot continuation, Task cleanup) : IProductionSearchSession
    {
        private bool _read;
        internal bool Cancelled;
        public Task Completion => Task.CompletedTask;
        public void Cancel() => Cancelled = true;
        public bool TryReadCandidate(out SearchCandidate? value)
        { value = _read ? null : candidate; _read = true; return value is not null; }
        public SearchContinuationSnapshot CaptureContinuation() => continuation;
        public ulong GetSafeNextOrdinal() => continuation.NextOrdinal;
        public bool TryGetSafeNextOrdinal(out ulong ordinal) { ordinal = continuation.NextOrdinal; return true; }
        public SearchProgressSnapshot GetProgress() => new(SearchRunState.Cancelled, 17, 1, 100, 30, 2, 8.5, SearchDisposition.Match, "", false);
        public SearchDiagnosticSummarySnapshot GetDiagnosticSummary() => throw new NotSupportedException();
        public ValueTask DisposeAsync() => new(cleanup);
    }
    private sealed class CheckpointSession(SearchCandidate candidate, SearchContinuationSnapshot continuation) : IProductionSearchSession
    {
        private bool _read;
        internal bool Acknowledged;
        internal Action? BeforeAcknowledgement;
        public Task Completion => Task.CompletedTask;
        public void Cancel() { }
        public bool TryReadCandidate(out SearchCandidate? value)
        { value = _read ? null : candidate; _read = true; return value is not null; }
        public void AcknowledgeCandidate(string seed)
        { if (seed != candidate.Seed) throw new InvalidOperationException("WrongAcknowledgement"); BeforeAcknowledgement?.Invoke(); Acknowledged = true; }
        public SearchContinuationSnapshot CaptureContinuation()
        { if (!Acknowledged) throw new InvalidOperationException("CheckpointBeforeAcceptance"); return continuation; }
        public ulong GetSafeNextOrdinal() => continuation.NextOrdinal;
        public bool TryGetSafeNextOrdinal(out ulong ordinal) { ordinal = continuation.NextOrdinal; return true; }
        public SearchProgressSnapshot GetProgress() => new(SearchRunState.Completed, 17, 1, 100, 30, 2, 8.5,
            SearchDisposition.Match, "", false) { PendingCandidateCount = 1, CleanupIssue = "cleanup-original" };
        public SearchDiagnosticSummarySnapshot GetDiagnosticSummary() => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static class StartingDeckModelDb
    {
        public static object[] AllCharacters { get; set; } = [];
    }
    private sealed class StartingDeckCharacter(ModelKey id, StartingDeckCard[] deck)
    {
        public ModelKey Id { get; } = id;
        public StartingDeckCard[] StartingDeck { get; set; } = deck;
    }
    private sealed class MissingStartingDeckCharacter(ModelKey id)
    {
        public ModelKey Id { get; } = id;
    }
    private sealed class StartingDeckCard(ModelKey id)
    {
        public ModelKey Id { get; } = id;
        public string Rarity => "Basic";
        public string Type => "Attack";
        public string[] Tags => [];
        public bool IsUpgradable => true;
        public bool IsRemovable => true;
        public int MaxUpgradeLevel => 1;
        public int CurrentUpgradeLevel { get; init; }
        public bool CanBeGeneratedByModifiers => true;
        public bool CanBeGeneratedInCombat => true;
        public bool IsDiscovered => true;
        public string MultiplayerConstraint => "None";
    }
    // A real compiled fixture exercises production lookup; explicit legacy identities model old disk records.
    private static readonly Lazy<ExactSearchExecutionRequest> ContinuationFixture = new(() =>
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(
            Environment.CurrentDirectory, "tools/ProductionSearchHost/Corpus/v1/context.json")).Context;
        return ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(SearchQuery.Empty, context),
            new("000000000000", 1, 1, 1)).Plan ?? throw new InvalidOperationException("ContinuationFixtureInvalid");
    });
    private static ExactSearchExecutionRequest LegacyContinuationPlan(string semanticFingerprint, string snapshotFingerprint) =>
        ContinuationFixture.Value with { SnapshotFingerprint = snapshotFingerprint,
            CompiledSearch = ContinuationFixture.Value.CompiledSearch with { SemanticFingerprint = semanticFingerprint } };
    private static SearchWorkspace Workspace(string directory) => new(directory, false);
    private static string WorkbenchPath(string directory) => Path.Combine(directory, "RolltheSpire2", "state", "query_workbench.json");
    private static WorkbenchSearchDraft Workbench() => new(Character, 10, SearchQuery.Empty with { OpeningRoute = new(Relic) }, AncientOptionConditionProfile.BroadDefault);
    // Only AppendResult's persisted DTO fields are populated; no prediction runs.
    private static SearchCandidate PersistableCandidate(string seed) => new(seed, RuntimeProfileId.Beta111, Character, 10, "fixture", null!,
        new SeedPredictionDocument { Context = null!, PredictorId = "fixture", OriginalSeed = seed, CanonicalSeed = seed,
            Sections = [], Warnings = [], Diagnostics = [], OverallStatus = SeedPredictionOverallStatus.Unknown },
        RolltheSpire2.Tests.FastSearch.Beta110FastSearchFixtureAuthority.CreateBeta111(Character, 10, 0, 1), [], [], []);
    private static SearchPresetCapture Capture(SearchDraft draft) => new(Character, 10, WorkbenchQueryCompiler.CreatePresetIntent(draft, Character, 10), 1, SearchPresetProbabilitySnapshot.Unavailable(Captured), SearchPresetProvenance.Unknown("fixture", Captured), Captured);
    private static SearchPresetDefinition Definition(SearchDraft draft) => SearchPresetStore.Normalize(new SearchPresetStore.PersistedSearchPreset
    { SchemaVersion = 2, Id = "user:fixture", Title = "fixture", CharacterKey = Character.Serialized,
      QuerySnapshot = JsonSerializer.SerializeToElement(draft, Json), CreatedAtUtc = Captured }, SearchPresetSource.User, Json);
    private static SearchPresetDefinition Normalize(JsonNode query) => SearchPresetStore.Normalize(new SearchPresetStore.PersistedSearchPreset
    { SchemaVersion = 2, Id = "user:fixture", Title = "fixture", CharacterKey = Character.Serialized, ConditionCount = 1, QuerySnapshot = JsonSerializer.SerializeToElement(query) }, SearchPresetSource.User, Json);
    private static SearchPresetWorkbenchLoadResolution Resolve(SearchPresetDefinition preset) => SearchPresetCompatibilityResolver.ResolveWorkbench(preset, Authority);
    private static bool Same<T>(T a, T b) => JsonNode.DeepEquals(JsonSerializer.SerializeToNode(a, Json), JsonSerializer.SerializeToNode(b, Json));
}
