using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    // Two invocations of the existing native host share only files below output.
    // No candidate, search session or returned disposition is supplied by the fixture.
    internal static async Task RunCardCollectionFlowAsync(Node host, string output, string phase)
    {
        if (phase is not ("create" or "reuse")) throw new ArgumentException("CardCollectionFlow.InvalidPhase");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        T Field<T>(object value, string name) => (T)(value.GetType().GetField(name, flags)
            ?? throw new MissingFieldException(value.GetType().Name, name)).GetValue(value)!;
        object? Call(object value, string name, params object?[] args) => (value.GetType().GetMethod(name, flags)
            ?? throw new MissingMethodException(value.GetType().Name, name)).Invoke(value, args);
        int checks = 0;
        void Require(bool ok, string reason)
        { checks++; if (!ok) throw new InvalidOperationException("CardCollectionFlow: " + reason); }
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Press(Button button)
        {
            Require(button.IsVisibleInTree() && !button.Disabled, "Action available: " + button.Name + "/" + button.Text);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        Directory.CreateDirectory(output);
        string receiptPath = Path.Combine(output, "card-collection-flow-" + phase + ".json");
        string userData = Path.Combine(output, "card-collection-flow-user-data");
        string state = Path.Combine(userData, "RolltheSpire2", "state");
        CardCollectionFlowReceipt? previous = null;
        var receipt = new CardCollectionFlowReceipt { Phase = phase, ProcessId = System.Environment.ProcessId,
            ProcessStartedAtUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(), UserDataRoot = userData };
        WorkspaceShell? shell = null;
        Control? canvas = null;
        SearchWorkspace? persistence = null;
        try
        {
            // Godot can byte-load assemblies. Hash the exact prepared file and
            // prove its metadata identity agrees with the actually loaded module.
            CardCollectionFlowBinary Binary(Assembly assembly)
            {
                string path = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/" + assembly.GetName().Name + ".dll");
                byte[] bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);
                using var pe = new PEReader(stream);
                var metadata = pe.GetMetadataReader();
                Guid preparedMvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
                Guid loadedMvid = assembly.ManifestModule.ModuleVersionId;
                Require(preparedMvid == loadedMvid, "Prepared DLL MVID equals loaded module: " + assembly.GetName().Name);
                return new CardCollectionFlowBinary { PreparedPath = path,
                    PreparedSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    PreparedMvid = preparedMvid, LoadedMvid = loadedMvid };
            }
            receipt.ModBinary = Binary(typeof(SearchQuery).Assembly);
            receipt.HostBinary = Binary(typeof(Main).Assembly);
            if (phase == "reuse") previous = JsonSerializer.Deserialize<CardCollectionFlowReceipt>(
                File.ReadAllText(Path.Combine(output, "card-collection-flow-create.json")));
            if (phase == "create") Require(!Directory.Exists(userData), "Create requires a fresh dedicated user-data root; choose a new output");
            else Require(previous is { Passed: true } && previous.ProcessId != receipt.ProcessId &&
                previous.ModBinary.PreparedSha256 == receipt.ModBinary.PreparedSha256 &&
                previous.HostBinary.PreparedSha256 == receipt.HostBinary.PreparedSha256 &&
                previous.ModBinary.LoadedMvid == receipt.ModBinary.LoadedMvid &&
                previous.HostBinary.LoadedMvid == receipt.HostBinary.LoadedMvid && previous.UserDataRoot == userData,
                "Reuse runs in a different process with the identical product and host DLLs and saved root");
            await InitializeNativeWorkspaceAsync(host, output);
            Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard).Assembly);
            // Match the declared vanilla-account fixture already used by MapWitness.
            // The empty fresh-profile mock has neither revealed epochs nor a
            // discovered starting act, which correctly yields Unknown in Exact.
            // Establish progress before any request/environment/editor capture.
            var save = MegaCrit.Sts2.Core.Saves.SaveManager.Instance;
            var progressData = save.Progress.ToSerializable();
            progressData.CharStats = [new() { Id = new("CHARACTER", "IRONCLAD"), TotalLosses = 1 }];
            progressData.Epochs = MegaCrit.Sts2.Core.Timeline.EpochModel.AllEpochIds.Select(id =>
                new MegaCrit.Sts2.Core.Saves.SerializableEpoch(id, MegaCrit.Sts2.Core.Saves.EpochState.Revealed)).ToList();
            string[] discoveredActs = ["OVERGROWTH", "UNDERDOCKS", "HIVE", "GLORY"];
            progressData.DiscoveredActs = discoveredActs.Select(entry => new ModelId("ACT", entry)).ToList();
            var validation = new MegaCrit.Sts2.Core.Saves.Validation.DeserializationContext();
            save.Progress = MegaCrit.Sts2.Core.Saves.ProgressState.FromSerializable(progressData, validation);
            Require(!validation.HasFatal && save.GenerateUnlockStateFromProgress().NumberOfRuns == 1,
                "Declared mock account has one normal run and valid revealed-epoch/discovered-act progress");
            receipt.AccountPremise = "Isolated mock account; one Ironclad loss; every native epoch revealed; base acts discovered; CurrentAccount capture remains production authority.";
            receipt.RevealedEpochCount = progressData.Epochs.Count; receipt.DiscoveredActs = discoveredActs;
            var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance,
                Detection = new("0.111.0", "0.111.0", "beta", "CardCollectionFlow", true, "") };
            RuntimeAuthorityEnvironment.CaptureOnMainThread(runtime);
            canvas = new Control { Size = new Vector2(1600, 900) }; host.AddChild(canvas);
            shell = new WorkspaceShell(); shell.InitializeWithPersistence(runtime, userData); canvas.AddChild(shell); shell.Open();
            await AwaitPageReady(host, shell);
            persistence = Field<SearchWorkspace>(shell, "_persistence");
            persistence.SetLanguageOverride("en"); Call(shell, "RefreshLanguage");
            var workbench = Field<QueryWorkbenchFrame>(shell, "_references");
            void Navigate(string name)
            {
                var method = shell.GetType().GetMethod("SelectTask", flags)!;
                method.Invoke(shell, [Enum.Parse(method.GetParameters()[0].ParameterType, name), null, null]);
            }
            Navigate("Seeds"); Call(shell, "EnsureSeedLibrary");
            var library = Field<SeedLibraryCanvas>(shell, "_seedLibrary");
            async Task ReadyLibrary()
            {
                for (int i = 0; i < 1200 && (!library.IsVisibleInTree() || !library.IsPresentationReady); i++) await Frames(1);
                Require(library.IsVisibleInTree() && library.IsPresentationReady && !library.HasPresentationFailure,
                    "Library completes native presentation"); await Frames();
            }
            Button LibraryAction(string name) => library.FindChild(name, true, false) as Button
                ?? throw new InvalidOperationException("CardCollectionFlow.MissingAction:" + name);
            async Task SelectLibrary(string id)
            {
                await ReadyLibrary();
                Press(library.FindChildren("LibraryItem*", "Button", true, false).OfType<Button>()
                    .Single(button => button.GetMeta("entry_id").AsString() == id));
                await ReadyLibrary();
            }
            library.ShowSection(SeedLibraryCanvas.LibrarySection.CardCollections); await ReadyLibrary();
            var collections = Field<CardCollectionStore>(library, "_cardCollections");
            CardCollectionSnapshot collection;
            if (phase == "create")
            {
                var card = ModelDb.AllCharacters.Single(character => character.Id.Entry == "IRONCLAD").CardPool.AllCards
                    .First(card => card.Rarity.ToString() == "Common" && card.Type.ToString() is "Attack" or "Skill" or "Power");
                var key = new ModelKey(card.Id.Category, card.Id.Entry);
                Press(LibraryAction("NewCardCollection")); await Frames();
                var editor = Field<CardCollectionEditorOverlay>(library, "_collectionEditor");
                Require(editor.IsOpen, "Library opens the real collection editor");
                Field<LineEdit>(editor, "_name").Text = "E02 production flow";
                var filter = Field<LineEdit>(editor, "_search"); filter.Text = key.Serialized;
                filter.EmitSignal(LineEdit.SignalName.TextChanged, filter.Text); await Frames();
                Press(Field<Control>(editor, "_availableRows").GetChildren().OfType<Button>()
                    .Single(button => button.Visible && button.GetMeta("card_key").AsString() == key.Serialized));
                await Frames(); Press(Field<Button>(editor, "_save")); await ReadyLibrary();
                collection = collections.GetAll().Single(item => item.Name == "E02 production flow");
                Require(collection.Revision == 1 && collection.Cards.SequenceEqual(new[] { key }),
                    "Real Library Save commits exactly the authored eligible card identity");
                Navigate("Search"); await AwaitPageReady(host, shell);
                workbench.RestoreDraft(new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
                    SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault), render: false);
                workbench.Refresh("en", JsonUiTextProvider.CreateUi13("en"));
                Press((Button)workbench.FindChild("SearchCategory_combat", true, false)); await Frames();
                var combat = Field<CombatRewardSearchEditor>(workbench, "_combatEditor");
                var scroll = Field<Control>(combat, "_body").GetChildren().OfType<ScrollContainer>().Single();
                scroll.ScrollVertical = 100000; await Frames();
                var selector = (OptionButton)combat.FindChild("CombatCardExclusionCollection", true, false);
                int index = Enumerable.Range(1, selector.ItemCount - 1).Single(i => selector.GetItemText(i).Contains(collection.Name, StringComparison.Ordinal));
                Require(!selector.IsItemDisabled(index), "Search offers the Library-created asset");
                selector.Select(index); selector.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index); await Frames();
                for (int i = 0; i < 2; i++)
                { Press((Button)combat.FindChild("CombatCardExclusionDecrease", true, false)); await Frames(); }
                persistence.SetWorkbenchSearchMode("CPU"); persistence.SetSearchWorkerBudget(1); persistence.SetSkipExactValidation(false);
                Require(persistence.TrySetPersistentOrigin("000000000000", out _, out _), "Set deterministic fixture cursor through the production store");
            }
            else
            {
                collection = collections.GetAll().Single(item => item.Id == previous!.CollectionId);
                Require(collection.Revision == previous!.CollectionRevision && collection.Cards.Select(key => key.Serialized).SequenceEqual(previous.CardKeys),
                    "Restart reloads the original collection identity, revision and members");
                var history = persistence.RecentQueries.Single(item => item.Id == previous.HistoryId);
                var saved = history.Results.Single(item => item.Seed == previous.Seed);
                Require(history.QueryKey == previous.QueryKey && saved.QueryFingerprint == previous.QueryFingerprint &&
                    saved.ValidationDisposition == SearchDisposition.Match && !saved.IsUnverified && !saved.IsUnfiltered,
                    "Restart retains real Exact result confidence and query/history identity");
                var favorite = Field<SeedLibraryStore>(library, "_store").GetAll().Single(item => item.Id == previous.FavoriteId);
                Require(favorite.Seed == previous.Seed && favorite.ResultProvenance is { ValidationDisposition: SearchDisposition.Match,
                    IsUnverified: false, IsUnfiltered: false } provenance && provenance.SearchRecordId == previous.HistoryId &&
                    provenance.SourceQueryKey == previous.QueryKey && favorite.QueryAssociations.Any(a => a.SearchRecordId == previous.HistoryId),
                    "Restart retains favorite identity and verified source provenance");
                receipt.FavoriteId = favorite.Id; receipt.RestoredSeed = saved.Seed;
                // Prove the visible history action restores intent, rather than relying on automatic startup draft restoration.
                workbench.RestoreDraft(new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
                    SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault), render: false);
                library.ShowSection(SeedLibraryCanvas.LibrarySection.History); await ReadyLibrary();
                await SelectLibrary(history.Id); Press(LibraryAction("ReloadHistory")); await AwaitPageReady(host, shell);
                Require(workbench.IsVisibleInTree(), "History Reload conditions navigates to real Search");
            }
            receipt.CollectionId = collection.Id; receipt.CollectionRevision = collection.Revision;
            receipt.CardKeys = collection.Cards.Select(key => key.Serialized).ToArray();
            var draft = workbench.CaptureDraft();
            Require(draft.Query.CombatCardExclusion is { BattleCount: 1 } condition &&
                condition.CardKeys.SequenceEqual(collection.Cards) && draft.CombatRewardEditor?.ExcludedCollection is { } snapshot &&
                snapshot.Id == collection.Id && snapshot.Revision == collection.Revision,
                "Actual selected/restored draft retains the authored exclusion and collection snapshot");
            Require(persistence.Preferences.SearchMode == "CPU" && !persistence.Preferences.SkipExactValidation,
                "The production request uses CPU and real Exact validation");
            Field<SpinBox>(workbench, "_targetCount").Value = 1;
            long beforeRevision = persistence.Runtime.Snapshot.RunRevision;
            Press(Field<Button>(workbench, "_start"));
            Require(persistence.Runtime.Snapshot.RunRevision == beforeRevision + 1, "Real StartSearch admitted and started a new session");
            object execution = Field<object>(persistence.Runtime, "_execution");
            var session = (IProductionSearchSession)execution.GetType().GetProperty("Session", flags)!.GetValue(execution)!;
            receipt.SessionType = session.GetType().FullName!;
            Require(session is ProductionSearchSession, "The admitted session is the real ProductionSearchSession");
            var timer = Stopwatch.StartNew();
            while (persistence.Runtime.Receiving || persistence.Runtime.CleanupCount > 0)
            {
                var observed = persistence.Runtime.Snapshot.Progress;
                if (timer.Elapsed > TimeSpan.FromSeconds(45) || observed?.ScannedCount > 4096)
                { persistence.Runtime.Cancel(); throw new InvalidOperationException("CardCollectionFlow.BoundedSearchExceeded:" + observed); }
                await Frames(1);
            }
            var progress = persistence.Runtime.Snapshot.Progress!;
            receipt.Progress = progress;
            var result = persistence.DisplayResults.Last();
            receipt.Seed = result.Seed; receipt.QueryFingerprint = result.QueryFingerprint;
            receipt.FinalDisposition = result.EffectiveValidationDisposition.ToString();
            receipt.IsUnverified = result.IsUnverified; receipt.IsUnfiltered = result.IsUnfiltered;
            receipt.ValidationIssue = result.ValidationIssue;
            receipt.SupportScopeState = result.SupportScopeState.ToString();
            receipt.SupportReasons = result.SupportScope?.Reasons.ToArray() ?? [];
            Require(progress.State == SearchRunState.Completed && progress.ScannedCount is > 0 and <= 4096 &&
                progress.MatchCount == 1 && progress.UnverifiedCandidateCount == 0 && progress.PendingCandidateCount == 0 &&
                progress.FailureCode.Length == 0 && progress.CleanupIssue.Length == 0,
                "Production scan completes within bound with one Match and no pending/unverified/failure");
            var active = persistence.RecentQueries.Single(item => item.Id == persistence.Workspace.ActiveHistoryId);
            Require(result.ValidationDisposition == SearchDisposition.Match && !result.IsUnverified && !result.IsUnfiltered &&
                active.Results.Any(item => item.Seed == result.Seed && item.QueryFingerprint == result.QueryFingerprint),
                "Production publication saves the real filtered Exact Match into its query history");
            receipt.HistoryId = active.Id; receipt.QueryKey = active.QueryKey; receipt.QueryFingerprint = result.QueryFingerprint;
            receipt.Seed = result.Seed; receipt.RunCount = active.RunCount;
            if (previous is not null) Require(active.Id == previous.HistoryId && active.QueryKey == previous.QueryKey &&
                result.QueryFingerprint == previous.QueryFingerprint && active.RunCount == previous.RunCount + 1 &&
                active.Results.Any(item => item.Seed == previous.Seed), "History reuse searches again under the original identity and retains the first result");
            await Frames();
            if (phase == "create")
            {
                Press(Field<Control>(workbench, "_resultRows").FindChildren("*", "Button", true, false)
                    .OfType<Button>().Single(button => button.Text == "Favorite")); await Frames();
                var note = Field<SeedLibraryNoteOverlay>(library, "_note");
                Require(note.IsOpen, "Real result Favorite opens the product transaction");
                Field<LineEdit>(note, "_titleInput").Text = "E02 verified result";
                Field<TextEdit>(note, "_note").Text = "Created by the bounded native production flow.";
                Press(Field<Button>(note, "_save")); await Frames();
                Require(!note.IsOpen, "Favorite Save committed without leaving the transaction open");
                receipt.FavoriteId = Field<SeedLibraryStore>(library, "_store").GetAll().Single(item =>
                    item.Source == SeedLibrarySource.User && item.Seed == result.Seed).Id;
            }
            shell.CleanupForTopLevelClose(); persistence.FlushAll(); await Frames();
            Require(persistence.Workspace.AuxiliaryIssues.Count == 0, "No hidden persistence or presentation failure");
            receipt.Passed = true;
        }
        catch (Exception exception) { receipt.Failure = exception.ToString(); throw; }
        finally
        {
            receipt.Assertions = checks;
            receipt.Scope = "Native Library authoring/selection; target1 CPU production Exact; automatic result/history persistence and real favorite transaction; separate-process history reload and production reuse. No real-game playthrough or GPU claim.";
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
            shell?.CleanupForTopLevelClose(); canvas?.QueueFree();
        }
    }

    private sealed class CardCollectionFlowReceipt
    {
        public CardCollectionFlowReceipt() { }
        public bool Passed { get; set; }
        public string Phase { get; set; } = "";
        public int ProcessId { get; set; }
        public DateTime ProcessStartedAtUtc { get; set; }
        public CardCollectionFlowBinary ModBinary { get; set; } = new();
        public CardCollectionFlowBinary HostBinary { get; set; } = new();
        public string UserDataRoot { get; set; } = "";
        public string AccountPremise { get; set; } = "";
        public int RevealedEpochCount { get; set; }
        public string[] DiscoveredActs { get; set; } = [];
        public int Assertions { get; set; }
        public string CollectionId { get; set; } = "";
        public long CollectionRevision { get; set; }
        public string[] CardKeys { get; set; } = [];
        public string HistoryId { get; set; } = "";
        public string QueryKey { get; set; } = "";
        public string QueryFingerprint { get; set; } = "";
        public string Seed { get; set; } = "";
        public string RestoredSeed { get; set; } = "";
        public string FavoriteId { get; set; } = "";
        public int RunCount { get; set; }
        public string SessionType { get; set; } = "";
        public string FinalDisposition { get; set; } = "";
        public bool IsUnverified { get; set; }
        public bool IsUnfiltered { get; set; }
        public string ValidationIssue { get; set; } = "";
        public string SupportScopeState { get; set; } = "";
        public string[] SupportReasons { get; set; } = [];
        public SearchProgressSnapshot? Progress { get; set; }
        public string Scope { get; set; } = "";
        public string? Failure { get; set; }
    }

    private sealed class CardCollectionFlowBinary
    {
        public CardCollectionFlowBinary() { }
        public string PreparedPath { get; set; } = "";
        public string PreparedSha256 { get; set; } = "";
        public Guid PreparedMvid { get; set; }
        public Guid LoadedMvid { get; set; }
    }
}
