using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Test;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

internal static partial class UiSurfaceChecks
{
    // Rendering-only fixtures: no seed scan, session, planner or compute dispatch.
    internal static async Task RunPresetLibraryAsync(Node host, string output, string pck)
    {
        Directory.CreateDirectory(output);
        if (!ProjectSettings.LoadResourcePack(pck, false)) throw new InvalidOperationException("PresetUi.GameResourcesUnavailable");
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard).Assembly);
        TestMode.IsOn = true;
        typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!.SetValue(null,
            MegaCrit.Sts2.Core.Modding.ModManagerState.Initialized);
        ModelDb.Init(); MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init(); ModelDb.InitIds();
        var mockSave = new SaveManager(new MockGodotFileIo("user://preset-library-surface-memory"), true);
        SaveManager.MockInstanceForTesting(mockSave);
        SaveManager.Instance.InitSettingsDataForTest(); SaveManager.Instance.InitPrefsDataForTest();
        SaveManager.Instance.InitProfileId(1); SaveManager.Instance.InitProgressData();
        SaveManager.Instance.SettingsSave.Language = "eng"; MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        TestMode.IsOn = false;
        if (!ReferenceEquals(SaveManager.Instance, mockSave)) throw new InvalidOperationException("PresetUi.MockSaveManagerLost");
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance,
            Detection = new("0.111.0", "0.111.0", "beta", "PresetLibrarySurface", true, "") };
        var authority = RuntimeAuthoritySnapshotCapture.Capture(runtime.Detection.DisplayVersion);
        typeof(RuntimeAuthorityEnvironment).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
            new RuntimeAuthorityEnvironmentSnapshot(authority, RuntimeAuthorityInterpreter.Interpret(authority), ""));
        host.GetWindow().Size = new Vector2I(1600, 900);
        RenderingServer.SetDefaultClearColor(new Color("131C29"));
        var canvas = new Control { Size = new Vector2(1600, 900) }; host.AddChild(canvas);
        var shell = new WorkspaceShell(); canvas.AddChild(shell);
        string state = Path.Combine(output, "ui-state", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(state);
        var legacyTemporary = new SearchPresetStore.PersistedSearchPreset
        {
            SchemaVersion = 3, Id = "temporary:legacy-ui-fixture", CharacterKey = BaseGameModelKeys.Characters.Ironclad.Serialized,
            Ascension = 10, CreatedAtUtc = DateTimeOffset.UtcNow,
            WorkbenchSnapshot = JsonSerializer.SerializeToElement(new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad,
                10, SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault), SearchPresetStore.CreateJsonOptions())
        };
        // InitializeWithPersistence accepts the user-data root; preset stores
        // read from the workspace state directory below that root.
        string presetState = Path.Combine(state, "RolltheSpire2", "state");
        Directory.CreateDirectory(presetState);
        string legacyTemporaryPath = Path.Combine(presetState, "temporary_search_presets.json");
        string legacyTemporaryJson = JsonSerializer.Serialize(new[] { legacyTemporary }, SearchPresetStore.CreateJsonOptions());
        File.WriteAllText(legacyTemporaryPath, legacyTemporaryJson);
        shell.InitializeWithPersistence(runtime, state);

        if (OS.GetCmdlineUserArgs().Contains("--library-layout-only", StringComparer.Ordinal))
        {
            try { await RunLibraryLayoutAsync(host, shell, output); }
            finally { canvas.QueueFree(); }
            return;
        }

        if (OS.GetCmdlineUserArgs().Contains("--card-collections-only", StringComparer.Ordinal))
        {
            try { await RunCardCollectionsAsync(host, shell, output); }
            finally { canvas.QueueFree(); }
            return;
        }

        int checks = 0;
        void Check(bool value, string description)
        { checks++; if (!value) throw new InvalidOperationException("PRESET_LIBRARY_SURFACE " + description); }
        T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
        object? Call(object instance, string name, params object?[] args) => instance.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(instance, args);
        void Navigate(string name)
        {
            MethodInfo method = shell.GetType().GetMethod("SelectTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(shell, [Enum.Parse(method.GetParameters()[0].ParameterType, name), null, null]);
        }
        async Task Render(string name)
        {
            for (int i = 0; i < 3; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            // Occluded test windows may skip automatic draws; request this screenshot explicitly.
            RenderingServer.ForceDraw();
            Check(host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "screenshot " + name);
        }
        bool Fits(Control control)
        {
            Rect2 outer = shell.GetGlobalRect(), inner = control.GetGlobalRect();
            return inner.Position.X >= outer.Position.X - 1 && inner.Position.Y >= outer.Position.Y - 1 &&
                inner.End.X <= outer.End.X + 1 && inner.End.Y <= outer.End.Y + 1;
        }
        void CheckActions(Node root)
        {
            foreach (Node node in root.FindChildren("*", "Button", true, false))
                if (node is Button { Disabled: true } button && button.IsVisibleInTree())
                    Check(!string.IsNullOrWhiteSpace(button.Text) || button.GetChildCount() > 0, "disabled action is labelled");
        }

        var persistence = Field<SearchWorkspace>(shell, "_persistence");
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var references = frame;

        Call(shell, "EnsureSeedLibrary");
        var library = Field<SeedLibraryCanvas>(shell, "_seedLibrary");
        async Task LibraryReady()
        {
            for (int i = 0; i < 1200 && (!library.IsVisibleInTree() || !library.IsPresentationReady); i++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(library.IsVisibleInTree() && library.IsPresentationReady, "Library presentation finishes over actual frames");
            Check(!library.HasPresentationFailure, "Library presentation does not fail before its controls are inspected");
        }
        var seeds = Field<SeedLibraryStore>(library, "_store");
        ModelKey[] characters = [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent,
            BaseGameModelKeys.Characters.Defect, BaseGameModelKeys.Characters.Regent];
        SearchQuery opening = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl) };
        var solo = new WorkbenchSearchDraft(characters[0], 10, opening, AncientOptionConditionProfile.BroadDefault);
        var party = new WorkbenchSearchDraft(characters[0], 10, SearchQuery.Empty with
        {
            Players = characters.Select((_, slot) => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)
                { Conditions = opening }).ToArray()
        }, AncientOptionConditionProfile.BroadDefault)
        {
            Version = 4, Mode = WorldGameMode.Multiplayer, GameVersion = runtime.Detection.NormalizedVersion,
            Profile = runtime.Profile.ProfileId, ObservationVersion = OrderedPartyAuthority.ObservationVersion,
            Players = characters.Select((character, slot) => new WorkbenchPlayerDraft(slot, character,
                UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")).ToArray()
        };
        var soloCapture = frame.CapturePreset(solo);
        var environment = RuntimeAuthorityEnvironment.Current;
        Check(!string.IsNullOrWhiteSpace(environment.Authority.Fingerprint.OverallSemanticHash) &&
            soloCapture.Provenance.SemanticFingerprint == environment.Authority.Fingerprint.OverallSemanticHash &&
            soloCapture.Provenance.FingerprintSchemaVersion == environment.Authority.FingerprintSchemaVersion &&
            soloCapture.Provenance.EnvironmentStatus == environment.Interpretation.EnvironmentStatus.ToString() &&
            soloCapture.Provenance.CapturedAtUtc == soloCapture.CapturedAtUtc,
            "Preset capture retains known runtime provenance and its capture time");
        var fixtureSearch = solo.Compile(runtime, out var fixtureAuthority);
        var expectedProbability = SearchProbabilityPresentationBuilder.Build(fixtureSearch);
        Check(expectedProbability.Status == SearchProbabilityQuickViewStatus.Complete && expectedProbability.TotalProbability is > 0 and <= 1,
            "Golden Pearl metadata fixture has a priced nonempty probability");
        Check(soloCapture.SavedProbability == SearchPresetProbabilitySnapshot.From(expectedProbability, soloCapture.CapturedAtUtc),
            "Preset probability snapshot equals the current probability presentation");
        var soloSaved = persistence.Presets.SaveUserPreset("Solo · UI fixture", "Synthetic UI data only",
            [SearchPresetVisualIconRef.FromRelic(BaseGameModelKeys.Relics.GoldenPearl)], soloCapture);
        Check(soloSaved.Provenance == soloCapture.Provenance && soloSaved.SavedProbability == soloCapture.SavedProbability,
            "Saving retains captured provenance and probability");
        FieldInfo environmentField = typeof(RuntimeAuthorityEnvironment).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var unavailable = RuntimeAuthorityEnvironmentSnapshot.Unavailable(runtime.Detection.NormalizedVersion, "PresetSurfaceUnavailableFixture");
        try
        {
            environmentField.SetValue(null, unavailable);
            var unknownCapture = frame.CapturePreset(solo);
            Check(string.IsNullOrEmpty(unknownCapture.Provenance.SemanticFingerprint) &&
                unknownCapture.Provenance.EnvironmentStatus == unavailable.Interpretation.EnvironmentStatus.ToString() &&
                unknownCapture.Provenance.UnlockKind == SearchPresetUnlockKinds.Unknown,
                "Unavailable environment remains explicit unknown provenance");
            Check(unknownCapture.SavedProbability == SearchPresetProbabilitySnapshot.From(expectedProbability, unknownCapture.CapturedAtUtc),
                "Unavailable historical provenance does not discard independently available probability");
            string metadataDirectory = Path.Combine(state, "metadata-fixture");
            var unknownSaved = new SearchPresetStore(metadataDirectory).Save("Unknown provenance fixture", "", [], unknownCapture);
            var unknownReloaded = new SearchPresetStore(metadataDirectory).GetPresets().Single(p => p.Id == unknownSaved.Id);
            Check(unknownReloaded.Provenance == unknownCapture.Provenance && unknownReloaded.SavedProbability == unknownCapture.SavedProbability,
                "Unknown provenance and available probability survive saving and reopening");
        }
        finally { environmentField.SetValue(null, environment); }
        var partyCapture = frame.CapturePreset(party);
        Check(partyCapture.Provenance.SemanticFingerprint == environment.Authority.Fingerprint.OverallSemanticHash &&
            partyCapture.Provenance.UnlockKind == SearchPresetUnlockKinds.Unknown,
            "Party captures runtime provenance without mislabelling all seats with local unlock coverage");
        var saved = persistence.Presets.SaveUserPreset("4P · 整桌预设 / Party preset", "四位玩家的完整开局条件 · Four ordered players", [], partyCapture);
        SeedLibraryContext context = SeedLibraryContextCapture.ForSearch(party, runtime);
        const string seed = "000000000000";
        const string note = "中文备注 🌱 — café — Ελληνικά\n这是临时界面测试数据，不是开发者推荐。\nTemporary UI fixture; this is not a recommended seed.\n[plain text] <no markup>";
        seeds.Save(seed, context, note);
        var developerSeed = seeds.GetAll().Single(e => e.Source == SeedLibrarySource.Developer && e.Seed == "R93J1P8CQA34");
        Check(developerSeed.CanOpen && developerSeed.Context is { Players.Count: 2, Ascension: 10 }, "Owner-provided developer recommendation is available");
        Check(JsonSerializer.Serialize(developerSeed.Context!.Players[0].Unlocks, SeedLibraryStore.CreateJsonOptions()) ==
            JsonSerializer.Serialize(UnlockState.all.ToSerializable(), SeedLibraryStore.CreateJsonOptions()), "Curated full-unlock configuration matches current game authority");
        var soloContext = SeedLibraryContextCapture.ForSearch(solo, runtime);
        persistence.BindPreparedResultWorkspace(persistence.PrepareResultWorkspace("library-ui-run", solo, soloContext));
        foreach (string resultSeed in new[] { "000000000111", "000000000112", "000000000113" })
        {
            var candidate = new SearchCandidate(resultSeed, RuntimeProfileId.Beta111, solo.Character, solo.Ascension, "library-ui-run", null!,
                new RolltheSpire2.Core.Prediction.SeedPredictionDocument { Context = null!, PredictorId = "fixture",
                    OriginalSeed = resultSeed, CanonicalSeed = resultSeed, Sections = [], Warnings = [], Diagnostics = [],
                    OverallStatus = RolltheSpire2.Core.Prediction.SeedPredictionOverallStatus.Unknown }, fixtureAuthority, [], [], []);
            persistence.AppendResult(candidate, "library-ui-run");
        }
        var history = persistence.RecentQueries.Single();
        var bookmarked = seeds.Save("000000000111", soloContext, note, history.QueryKey, persistence.ResultAssociation("000000000111"));
        Check(persistence.Presets.GetAll().Any(p => p.Source == SearchPresetSource.Temporary), "Legacy temporary fixture remains readable");
        Check(File.ReadAllText(legacyTemporaryPath) == legacyTemporaryJson, "Legacy temporary fixture reads preserve original bytes");
        frame.RestoreDraft(solo, false);
        Button Action(string name) => (Button)library.FindChild(name, true, false);
        async Task SelectEntry(string id)
        {
            await LibraryReady();
            var item = library.FindChildren("LibraryItem*", "Button", true, false).OfType<Button>()
                .Single(b => b.GetMeta("entry_id").AsString() == id);
            item.EmitSignal(BaseButton.SignalName.Pressed);
            await LibraryReady();
        }
        try
        {
            foreach (string language in new[] { "zh", "en" })
            {
                SaveManager.Instance.SettingsSave.Language = language == "zh" ? "zhs" : "eng";
                MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
                persistence.SetLanguageOverride(language); Call(shell, "RefreshLanguage");
                Navigate("Search"); Call(frame, "OpenPresets");
                await LibraryReady();
                Check(library.IsVisibleInTree() && library.Section == SeedLibraryCanvas.LibrarySection.Presets,
                    "Search preset entry navigates to the full Seed library");
                Check(!Field<Button>(shell, "_seeds").Disabled && !Field<Button>(shell, "_search").Disabled,
                    "Library browsing does not lock top-level navigation");
                Check(library.FindChildren("SeedLibrary*", "Button", true, false).Count == 3, "Exactly three library sections");
                Check(!library.FindChildren("LibraryItem*", "Button", true, false).OfType<Button>()
                    .Any(b => b.GetMeta("entry_id").AsString().StartsWith("temporary:")), "Legacy history is absent from presets");
                await SelectEntry(soloSaved.Id);
                await Render("library-presets-" + language);
                Check(Fits(Action("UseLibraryPreset")) && Fits(Action("ViewPresetHistory")) &&
                    Fits(library.FindChild("LibraryDetailPanel", true, false) as Control ?? throw new Exception()), "Full-page preset controls fit");
                var presetListItem = library.FindChildren("LibraryItem*", "Button", true, false).OfType<Button>()
                    .Single(b => b.GetMeta("entry_id").AsString() == soloSaved.Id);
                Check(presetListItem.FindChildren("PresetRelicIcon*", "TextureRect", true, false).Count == 1,
                    "Personal preset's chosen relic icon is visible in the left list");
                Check(Action("UpdatePresetEnvironment").GetGlobalRect().Position.Y > Action("UseLibraryPreset").GetGlobalRect().End.Y + 100 &&
                    Fits(Action("DeleteLibraryPreset")), "Preset management stays apart from primary actions at the bottom");
                Check(!library.FindChildren("*", "Label", true, false).OfType<Label>()
                    .Any(l => l.Text.Contains("Best Effort") || l.Text.Contains(history.EnvironmentFingerprint)),
                    "Internal compatibility jargon and environment hashes are hidden");
                string originalKey = QueryHistoryIdentity.Key(soloSaved.Workbench!);
                Action("UpdatePresetEnvironment").EmitSignal(BaseButton.SignalName.Pressed);
                await LibraryReady();
                var updated = persistence.Presets.GetAll().Single(p => p.Id == soloSaved.Id);
                Check(updated.EnvironmentFingerprint == persistence.CurrentEnvironmentFingerprint &&
                    QueryHistoryIdentity.Key(updated.Workbench!) == originalKey && history.Results.Count == 3,
                    "Environment update preserves conditions and historical results");

                Action("EditLibraryPreset").EmitSignal(BaseButton.SignalName.Pressed);
                var save = Field<SearchPresetSaveTransactionOverlay>(frame, "_presetSave");
                Check(save.IsOpen && Field<Button>(shell, "_seeds").Disabled, "Metadata transaction alone locks navigation");
                Field<LineEdit>(save, "_titleInput").Text = "Renamed · " + language;
                Field<Button>(save, "_save").EmitSignal(BaseButton.SignalName.Pressed);
                await LibraryReady();
                Check(!save.IsOpen && library.IsVisibleInTree() && !Field<Button>(shell, "_search").Disabled,
                    "Saving metadata returns to the same full-page library");
                Check(persistence.QueriesForPreset(persistence.Presets.GetAll().Single(p => p.Id == soloSaved.Id)).Single().Id == history.Id,
                    "Renaming retains linked history");

                await SelectEntry(saved.Id);
                await Render("library-party-preset-" + language);
                await SelectEntry(soloSaved.Id);
                library.SearchBusy = () => true; library._Process(1.1);
                Check(Action("UseLibraryPreset").Disabled && Action("UpdatePresetEnvironment").Disabled,
                    "Active search disables context-changing operations");
                library.SearchBusy = () => references.HasActiveSearch; library._Process(1.1);
                frame.RestoreDraft(solo with { Ascension = 9 }, false);
                Action("UseLibraryPreset").EmitSignal(BaseButton.SignalName.Pressed);
                Check(!library.IsVisibleInTree() && frame.CaptureDraft().Ascension == 10 && !frame.HasActiveSearch,
                    "Use preset returns to Search and loads without starting a session");

                Call(frame, "OpenPresets"); await SelectEntry(soloSaved.Id);
                Action("ViewPresetHistory").EmitSignal(BaseButton.SignalName.Pressed);
                await LibraryReady();
                Check(library.Section == SeedLibraryCanvas.LibrarySection.History &&
                    library.FindChildren("LibraryItem*", "Button", true, false).Count == 1, "Preset history uses a scoped history view");
                await Render("library-history-results-" + language);
                Check(Action("HistoryConditions").GetParent() != Action("ReloadHistory").GetParent(),
                    "History view tabs are separate from the reload action");
                Check(library.FindChildren("HistoryResultRow*", "HBoxContainer", true, false).Count == 3,
                    "Results are displayed inline beside the selected search");
                Check(!library.FindChildren("*", "Button", true, false).OfType<Button>()
                    .Any(b => b.IsVisibleInTree() && b.Text is "删除" or "Delete"), "History offers no delete action");
                Action("HistoryConditions").EmitSignal(BaseButton.SignalName.Pressed);
                await LibraryReady();
                Check(library.FindChild("LibraryConditionSummary", true, false) is SearchConditionSummary,
                    "Conditions replace results within the right pane");
                await Render("library-history-conditions-" + language);
                Action("ReloadHistory").EmitSignal(BaseButton.SignalName.Pressed);
                Check(frame.CaptureDraft().Ascension == 10 && !library.IsVisibleInTree() && !frame.HasActiveSearch,
                    "Reload history returns to Search without running it");

                Navigate("Seeds"); library.ShowSection(SeedLibraryCanvas.LibrarySection.Favorites);
                await SelectEntry(bookmarked.Id);
                await Render("library-favorites-" + language);
                Check(new[] { "CopyFavorite", "PredictFavorite", "EditFavorite", "DeleteFavorite" }.All(n => Fits(Action(n))),
                    "The four seed actions are consistently visible");
                Check(Action("EditFavorite").GetGlobalRect().Position.Y > Action("PredictFavorite").GetGlobalRect().End.Y + 100,
                    "Favorite management stays below its content, separate from prediction");
                Action("CopyFavorite").EmitSignal(BaseButton.SignalName.Pressed);
                string copiedSeed = "";
                // Windows clipboard observers may briefly hold the clipboard after a write.
                for (int attempt = 0; attempt < 3 && copiedSeed != bookmarked.Seed; attempt++)
                {
                    await host.ToSignal(host.GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
                    copiedSeed = DisplayServer.ClipboardGet();
                }
                Check(copiedSeed == bookmarked.Seed, "Copy uses the selected seed");
                Action("EditFavorite").EmitSignal(BaseButton.SignalName.Pressed);
                var noteDialog = Field<SeedLibraryNoteOverlay>(library, "_note");
                Check(Field<TextEdit>(noteDialog, "_note").Text == note, "Edit information preserves Unicode notes");
                string seedTitle = language == "zh" ? "珍珠开局 🌱" : "Golden Pearl 🌱";
                Field<LineEdit>(noteDialog, "_titleInput").Text = seedTitle;
                await Render("library-seed-edit-" + language);
                Field<Button>(noteDialog, "_save").EmitSignal(BaseButton.SignalName.Pressed);
                Check(seeds.GetAll().Single(e => e.Id == bookmarked.Id).Title == seedTitle &&
                    seeds.GetAll().Single(e => e.Id == bookmarked.Id).Note == note,
                    "Editing a title preserves the seed's note and identity");
                Field<LineEdit>(library, "_filter").Text = seedTitle;
                Field<LineEdit>(library, "_filter").EmitSignal(LineEdit.SignalName.TextChanged, seedTitle);
                await LibraryReady();
                Check(library.FindChildren("LibraryItem*", "Button", true, false).Count == 1, "Favorite title participates in search");
                Field<LineEdit>(library, "_filter").Text = "";
                Field<LineEdit>(library, "_filter").EmitSignal(LineEdit.SignalName.TextChanged, "");
                await LibraryReady();
                await Render("library-titled-seed-" + language);
                Action("ViewFavoriteSource").EmitSignal(BaseButton.SignalName.Pressed);
                await LibraryReady();
                Check(library.Section == SeedLibraryCanvas.LibrarySection.History &&
                    library.FindChildren("HistoryResultRow*", "HBoxContainer", true, false).Count == 3,
                    "Favorite source opens its original search results");

                library.ShowSection(SeedLibraryCanvas.LibrarySection.Presets);
                var builtIn = persistence.Presets.GetAll().First(p => p.Source == SearchPresetSource.BuiltIn);
                await SelectEntry(builtIn.Id);
                Check(library.FindChild("SaveLibraryPreset", true, false) is null &&
                    library.FindChild("LibraryEnvironmentNotice", true, false) is null &&
                    !Field<VBoxContainer>(library, "_detailFooter").Visible,
                    "Developer presets have no save/management controls or user-environment notice");
                await Render("library-developer-preset-" + language);
                CheckActions(library);

                library.ShowSection(SeedLibraryCanvas.LibrarySection.Favorites); await SelectEntry(developerSeed.Id);
                await Render("library-developer-seed-" + language);
                Check(new[] { "CopyDeveloperSeed", "PredictDeveloperSeed", "FavoriteDeveloperSeed" }.All(n => Fits(Action(n))) &&
                    library.FindChild("EditFavorite", true, false) is null && library.FindChild("DeleteFavorite", true, false) is null,
                    "Developer seed has copy/predict/favorite actions and remains read-only");
                Check(library.FindChildren("PresetRelicIcon*", "TextureRect", true, false).Count >= 2,
                    "Developer seed icons appear beside list and detail text");
                Field<LineEdit>(library, "_filter").Text = language == "zh" ? "变形灵林谷" : "Morphic Grove";
                Field<LineEdit>(library, "_filter").EmitSignal(LineEdit.SignalName.TextChanged, Field<LineEdit>(library, "_filter").Text);
                await LibraryReady();
                Check(library.FindChildren("LibraryItem*", "Button", true, false).Count == 1, "Recommendation description is searchable in both languages");
                Field<LineEdit>(library, "_filter").Text = "";
                Field<LineEdit>(library, "_filter").EmitSignal(LineEdit.SignalName.TextChanged, "");
                await LibraryReady();
                await SelectEntry(developerSeed.Id);
                Action("FavoriteDeveloperSeed").EmitSignal(BaseButton.SignalName.Pressed);
                Check(Field<LineEdit>(noteDialog, "_titleInput").Text == developerSeed.TitleFor(language) &&
                    Field<TextEdit>(noteDialog, "_note").Text.Contains(language == "zh" ? "游玩说明" : "How to play"),
                    "Developer favorite dialog starts with the localized title and play instructions");
                Field<Button>(noteDialog, "_save").EmitSignal(BaseButton.SignalName.Pressed);
                Check(seeds.TryFind(developerSeed.Seed, developerSeed.Context!, out var copiedRecommendation) &&
                    copiedRecommendation.Context!.Players.Count == 2, "Favorite uses a detached copy of the whole party");
                // Remove this temporary personal copy before the other-language check.
                Check(seeds.DeleteUser(copiedRecommendation.Id) && seeds.GetAll().Any(e => e.Id == developerSeed.Id),
                    "Deleting a personal copy leaves developer recommendation intact");
                library.RefreshEntries();

                // Palette remains live after replacing the old modal with this workspace.
                Navigate("Settings");
                await AwaitPageReady(host, shell);
                Call(shell, "SelectSettingsCategory", 1);
                await AwaitPageReady(host, shell);
                var scheme = Field<OptionButton>(shell, "_colorScheme");
                scheme.Select(1); scheme.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                Navigate("Seeds"); library.ShowSection(SeedLibraryCanvas.LibrarySection.Presets); await SelectEntry(soloSaved.Id);
                await Render("library-starfield-" + language);
                Check(persistence.Preferences.Appearance.Scheme == "stars", "Library inherits the selected palette");
                Navigate("Settings");
                await AwaitPageReady(host, shell);
                scheme.Select(0); scheme.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            }
            // A preset can retain the source search beyond the recent-history limit.
            history.LastUsedAtUtc = DateTimeOffset.UtcNow.AddDays(-1);
            persistence.BindPreparedResultWorkspace(persistence.PrepareResultWorkspace("library-ui-later-run", party, context));
            persistence.SetQueryHistoryLimit(1);
            Check(!persistence.RecentQueries.Any(q => q.Id == history.Id) &&
                persistence.Workspace.QueryHistory.Any(q => q.Id == history.Id), "Fixture source is retained outside recent history");
            Navigate("Seeds"); library.ShowSection(SeedLibraryCanvas.LibrarySection.Favorites); await SelectEntry(bookmarked.Id);
            Action("ViewFavoriteSource").EmitSignal(BaseButton.SignalName.Pressed);
            await LibraryReady();
            Check(Field<string>(library, "_selectedHistory") == history.Id &&
                library.FindChildren("HistoryResultRow*", "HBoxContainer", true, false).Count == 3,
                "Favorite source selects the original retained search, not the latest run");
            Action("ClearHistoryFilter").EmitSignal(BaseButton.SignalName.Pressed);
            await LibraryReady();
            Check(Field<string>(library, "_selectedHistory") == persistence.RecentQueries.Single().Id,
                "All history returns from source scope to the recent list");

            var disposablePreset = persistence.Presets.SaveUserPreset("Deletion fixture", "", [], soloCapture);
            library.ShowSection(SeedLibraryCanvas.LibrarySection.Presets); await SelectEntry(disposablePreset.Id);
            var confirmation = Field<SearchConfirmationTransactionOverlay>(library, "_confirmation");
            Action("DeleteLibraryPreset").EmitSignal(BaseButton.SignalName.Pressed);
            Check(confirmation.IsOpen && Field<Button>(shell, "_seeds").Disabled, "Preset deletion requires its local confirmation");
            confirmation.Cancel();
            Check(persistence.Presets.GetAll().Any(p => p.Id == disposablePreset.Id), "Cancel preserves the preset");
            Action("DeleteLibraryPreset").EmitSignal(BaseButton.SignalName.Pressed);
            Field<Button>(confirmation, "_confirm").EmitSignal(BaseButton.SignalName.Pressed);
            Check(!persistence.Presets.GetAll().Any(p => p.Id == disposablePreset.Id) &&
                persistence.Workspace.QueryHistory.Single(q => q.Id == history.Id).Results.Count == 3 &&
                seeds.GetAll().Any(e => e.Id == bookmarked.Id) && !Field<Button>(shell, "_seeds").Disabled,
                "Preset deletion removes only that preset while other links preserve results and favorites");
            var disposableSeed = seeds.Save("000000000999", soloContext, "Deletion fixture");
            library.ShowSection(SeedLibraryCanvas.LibrarySection.Favorites); await SelectEntry(disposableSeed.Id);
            Action("DeleteFavorite").EmitSignal(BaseButton.SignalName.Pressed);
            Field<Button>(confirmation, "_confirm").EmitSignal(BaseButton.SignalName.Pressed);
            Check(!seeds.GetAll().Any(e => e.Id == disposableSeed.Id) && seeds.GetAll().Any(e => e.Id == bookmarked.Id) &&
                history.Results.Count == 3, "Deleting a favorite retains independent history and other favorites");
            Check(!frame.HasActiveSearch, "No production Search session started");
            library.ShowSection(SeedLibraryCanvas.LibrarySection.Favorites); await SelectEntry(developerSeed.Id);
            Action("PredictDeveloperSeed").EmitSignal(BaseButton.SignalName.Pressed);
            var predictor = Field<RolltheSpire2.Ui.Pages.Analysis.AnalysisPage>(shell, "_predictor");
            var predictorController = Field<RolltheSpire2.Ui.Controllers.AnalysisPageController>(shell, "_predictorController");
            foreach (int slot in new[] { 0, 1 })
            {
                if (slot != 0) Call(predictorController, "SelectPartyPlayer", slot);
                Check(predictor.LastRequest is { PlayersCount: 2, Ascension: 10 } request &&
                    request.PlayerSlotIndex == slot && request.Character.CharacterKey == BaseGameModelKeys.Characters.Ironclad &&
                    predictor.LastDocument?.CanonicalSeed == developerSeed.Seed,
                    "Developer prediction restores both Ironclads and the authored seed");
                Check(predictor.LastDocument!.Sections.SelectMany(s => s.NeowChoices).Any(c =>
                    c.SlotIndex == predictor.PreferredOpeningChoiceSlotIndex && c.RelicKey == BaseGameModelKeys.Relics.LeafyPoultice),
                    "Each seat restores Leafy Poultice rather than an unrelated default opening");
            }
            // A checked-but-unresolved candidate is visually distinct and cannot
            // acquire a favorite's verified-search association from this page.
            persistence.BindPreparedResultWorkspace(persistence.PrepareResultWorkspace("uncertain-ui-fixture", solo, soloContext));
            persistence.AppendResult(new SearchCandidate("000000000222", RuntimeProfileId.Beta111,
                solo.Character, solo.Ascension, "uncertain-ui-fixture", null!, null, fixtureAuthority, [], [], [])
                { ValidationDisposition = SearchDisposition.Unknown, ValidationIssue = "FixtureObservationUnavailable" }, "uncertain-ui-fixture");
            frame.RestoreDraft(solo, false);
            Navigate("Search"); frame.ShowLastResults();
            var resultRows = Field<VBoxContainer>(frame, "_resultRows");
            var reasonButton = resultRows.FindChildren("*", "Button", true, false).OfType<Button>()
                .Single(b => b.Text == "View reason");
            reasonButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Render("unverified-results");
            Check(reasonButton.Size.X >= 80 && reasonButton.GetGlobalRect().End.X <= resultRows.GetGlobalRect().End.X && Fits(reasonButton),
                "Unverified reason action stays within the results column instead of clipping after row actions");
            Check(resultRows.FindChildren("*", "Label", true, false).OfType<Label>()
                .Any(l => l.IsVisibleInTree() && l.Text.Contains("FixtureObservationUnavailable")),
                "Unknown result retains an expandable reason in the Search result page");
            Check(persistence.ResultAssociation("000000000222") is { Kind: SeedQueryAssociationKind.UnverifiedFromSearch },
                "Incomplete checking retains unverified search provenance without becoming confirmed");
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordEvidence(
                "IssueSurfaceFixture", "NativeUiCheck", "Background", "issues.prediction_failed",
                "Retained native UI fixture cause", correlationId: "fixture-request");
            Call(shell, "RefreshIssues");
            var issueEntry = Field<Button>(shell, "_issuesEntry");
            Check(issueEntry.Visible && Fits(issueEntry), "Issue entry is visible within the shell");
            issueEntry.EmitSignal(BaseButton.SignalName.Pressed);
            var issuePanel = Field<PanelContainer>(shell, "_issuesPanel");
            await Render("runtime-notices");
            Check(Fits(issuePanel) && issuePanel.FindChildren("*", "TextEdit", true, false).OfType<TextEdit>()
                .Any(t => t.Text.Contains("fixture-request") && t.Text.Contains("Retained native UI fixture cause")),
                "Issue details retain correlation and cause without replacing the workspace");
            shell.HandleBack();
            Check(Field<PanelContainer?>(shell, "_issuesPanel") is null && shell.IsVisibleInTree(),
                "Back closes issue details before closing the workspace");
            GD.Print($"PRESET_LIBRARY_SURFACE checks={checks};failures=0;languages=zh,en;layout=master-detail;sections=3;seedScans=0;state={state}");
        }
        finally
        {
            while (library.CloseModal()) { }
            frame.CloseModal(); canvas.QueueFree();
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
