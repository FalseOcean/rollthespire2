using RolltheSpire2.Ui.Workspace;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Ui.Pages.Search.Editors;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Ancient;
using RolltheSpire2.Ui.Pages.Search.BossMap;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Tests.FastSearch;

// Fixed intent/compile/UI checks only: no planner, seed scan, worker, GPU or game run.
internal static class PresetWorkbenchChecks
{
    private static readonly JsonSerializerOptions Json = SearchPresetStore.CreateJsonOptions();
    private static readonly DateTimeOffset Captured = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly ModelKey[] Characters = [BaseGameModelKeys.Characters.Ironclad,
        BaseGameModelKeys.Characters.Silent, BaseGameModelKeys.Characters.Defect, BaseGameModelKeys.Characters.Regent];
    private static int _checks;
    private static string _evidence = "";

    internal static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("NativePresetWorkbench");
        string directory = Path.Combine(Path.GetTempPath(), "RolltheSpire2-preset-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _evidence = directory;
        var persistence = new SearchWorkspace(directory, false);
        var frame = new QueryWorkbenchFrame(runtime, persistence) { Size = new(1536, 772) };
        var store = new SearchPresetStore(directory);

        CheckLegacy(runtime, frame, store, directory);
        var solo = BuildDraft(runtime, 1);
        CheckSharedEditorInputs(runtime, directory, solo);
        CheckRetainedShellHotOpen(runtime, directory);
        var soloPreset = RoundTrip(runtime, frame, store, directory, solo, "solo");
        CheckPoolRebind(runtime, frame, store, directory, solo);
        CheckUnavailableAuthoredIdentityRetention(runtime, frame);
        CheckAutomaticRestorePreservesSource(runtime, directory, solo);
        CheckUnvisitedPartyAncientSeat(runtime, directory);
        CheckBossOnlyPreset(runtime, frame, store, solo);
        WorkbenchSearchDraft? lastParty = null;
        foreach (int count in new[] { 2, 4 })
        {
            var party = BuildDraft(runtime, count);
            var semanticOnly = party with {
                PartyAncientEditor = null,
                Players = party.Players.Select(p => p with { AncientEditor = null }).ToArray(),
                Query = party.Query with { LegacyWorld = party.Query.LegacyWorld with { AncientIdentityFilters = [] } }
            };
            int capturesBeforeParty = frame.EditorInputCounts.AuthorityCaptures;
            frame.RestoreDraft(semanticOnly, render: false);
            Check(frame.EditorInputCounts.AuthorityCaptures - capturesBeforeParty == count,
                count + "P restore captures each party slot only once across import passes");
            AssertCompiled(party.Compile(runtime, out _), frame.CaptureDraft().Compile(runtime, out _),
                count + "P semantic-only Ancient branches survive missing UI metadata");
            var preset = RoundTrip(runtime, frame, store, directory, party, count + "P");
            lastParty = frame.CaptureDraft();
            frame.ApplyPreset(soloPreset, render: false);
            AssertSameIntent(runtime, solo, frame.CaptureDraft(), "party to solo preset");
            frame.SetSearchMode(true, render: false);
            AssertSameIntent(runtime, lastParty, frame.CaptureDraft(), "ordinary switch restores independent party draft");
            frame.SetSearchMode(false, render: false);
            AssertSameIntent(runtime, solo, frame.CaptureDraft(), "ordinary switch restores independent solo draft");
            frame.ApplyPreset(preset, render: false);
            AssertSameIntent(runtime, lastParty, frame.CaptureDraft(), "solo to party preset restores complete roster");
        }
        CheckProtection(runtime, frame, directory, lastParty!);
        Check(!frame.HasActiveSearch, "All preset operations leave Search stopped");
        Console.WriteLine($"PRESET_WORKBENCH checks={_checks};failures=0;fixtures={directory};seedScans=0");
        frame.Free();
        return 0;
    }

    private static void CheckSharedEditorInputs(ModRuntimeSnapshot runtime, string directory, WorkbenchSearchDraft authored)
    {
        var frame = new QueryWorkbenchFrame(runtime, new SearchWorkspace(Path.Combine(directory, "shared-inputs"), false));
        var globalField = typeof(RuntimeAuthorityEnvironment).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var originalGlobal = RuntimeAuthorityEnvironment.Current;
        var harmony = new Harmony("rt2.workbench.shared-inputs.deck");
        try
        {
            frame.RestoreDraft(authored, render: false);
            Check(frame.EditorInputCounts == (1, 1), "Cold saved draft restore shares one input observation and one effect/world authority across all editors and both import passes");
            AssertSameIntent(runtime, authored, frame.CaptureDraft(), "Shared-input restore retains every authored condition");
            int cold = frame.EditorInputCounts.AuthorityCaptures;
            frame.RefreshEnvironmentInputs(); frame.RestoreDraft(authored, render: false);
            Check(frame.EditorInputCounts.AuthorityCaptures == cold, "Unchanged hot open and repeated restore reuse captured authority");

            var preset = SearchPresetStore.BuildFromCapture("user:shared-inputs", SearchPresetSource.User,
                "shared inputs", "", [], Capture(authored), Json);
            var fresh = new QueryWorkbenchFrame(runtime, new SearchWorkspace(Path.Combine(directory, "shared-use"), false));
            try
            {
                fresh.ApplyPreset(preset, render: false);
                Check(fresh.EditorInputCounts == (1, 1), "Cold preset Use shares one observation/authority across expected compile, probe restore, semantic comparison and real restore");
                AssertSameIntent(runtime, authored, fresh.CaptureDraft(), "Shared-input preset transaction retains complete intent");
            }
            finally { fresh.Free(); }

            frame.RestoreDraft(authored with { Ascension = 9 }, render: false);
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Ascension changes invalidate the shared request authority");
            frame.RestoreDraft(authored, render: false);
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Returning to a prior request observes it again rather than retaining an unbounded request cache");
            var empty = authored with { Query = SearchQuery.Empty, AncientEditor = null, NeowEditor = null,
                EventEditor = null, TransformationEditor = null, Character = BaseGameModelKeys.Characters.Silent };
            frame.RestoreDraft(empty, render: false);
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Character changes invalidate the shared request authority");

            // Change a different character's real starting-deck getter. No selected
            // character change, global hash update or timing delay can trigger this.
            harmony.Patch(AccessTools.PropertyGetter(typeof(Defect), nameof(CharacterModel.StartingDeck)),
                postfix: new HarmonyMethod(typeof(PresetWorkbenchChecks), nameof(AppendStartingDeckCard)));
            frame.RefreshEnvironmentInputs();
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "A non-selected character starting-deck change invalidates hot-open inputs");
            harmony.UnpatchAll(harmony.Id);
            frame.RefreshEnvironmentInputs();
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Restoring the other character starting deck invalidates again");

            var changed = originalGlobal.Authority with { Fingerprint = originalGlobal.Authority.Fingerprint with
                { OverallSemanticHash = "controlled-content-change" } };
            globalField.SetValue(null, originalGlobal with { Authority = changed });
            frame.RefreshEnvironmentInputs();
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Changed observed runtime content invalidates hot-open authority");
            globalField.SetValue(null, originalGlobal);
            frame.RefreshEnvironmentInputs();
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Restored runtime content is observed again");

            harmony.Patch(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.GenerateUnlockStateFromProgress)),
                postfix: new HarmonyMethod(typeof(PresetWorkbenchChecks), nameof(ChangeObservedProgress)));
            frame.RefreshEnvironmentInputs();
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Changed actual profile progress invalidates hot-open input authority");
            harmony.UnpatchAll(harmony.Id);
            frame.RefreshEnvironmentInputs();
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Restoring actual profile progress invalidates again");

            var profileField = typeof(SaveManager).GetField("_currentProfileId", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? originalProfile = profileField.GetValue(SaveManager.Instance);
            try
            {
                profileField.SetValue(SaveManager.Instance, SaveManager.Instance.CurrentProfileId + 1);
                frame.RefreshEnvironmentInputs();
                Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Account identity change invalidates even with identical unlock values");
            }
            finally { profileField.SetValue(SaveManager.Instance, originalProfile); }
            frame.RefreshEnvironmentInputs();
            Check(frame.EditorInputCounts.AuthorityCaptures == ++cold, "Returning account identity invalidates the prior observation");

            var table = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
                SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty), new(1, ModelKeySetFilter.Empty)] },
                AncientOptionConditionProfile.BroadDefault)
            {
                Version = 4, Mode = WorldGameMode.Multiplayer, GameVersion = runtime.Detection.NormalizedVersion,
                Profile = runtime.Profile.ProfileId,
                Players = [new(0, BaseGameModelKeys.Characters.Ironclad, UnlockState.all.ToSerializable(), "AssumedFullyUnlocked"),
                    new(1, BaseGameModelKeys.Characters.Silent, UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")]
            };
            frame.RestoreDraft(table, render: false); cold += 2;
            Check(frame.EditorInputCounts.AuthorityCaptures == cold, "Entering a table captures the two slot inputs once");
            table = table with { Players = table.Players.Select((p, i) => i == 1 ? p with
                { Character = BaseGameModelKeys.Characters.Defect } : p).ToArray() };
            frame.RestoreDraft(table, render: false); cold += 2;
            Check(frame.EditorInputCounts.AuthorityCaptures == cold, "Changing the roster invalidates the whole ordered table authority");
            var changedUnlock = UnlockState.all.ToSerializable(); changedUnlock.NumberOfRuns++;
            table = table with { Players = table.Players.Select((p, i) => i == 1 ? p with { Unlocks = changedUnlock } : p).ToArray() };
            frame.RestoreDraft(table, render: false); cold += 2;
            Check(frame.EditorInputCounts.AuthorityCaptures == cold, "Changing another seat's unlock input invalidates the ordered table authority");

            var key = typeof(QueryWorkbenchFrame).GetField("_analysisKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pending = typeof(QueryWorkbenchFrame).GetField("_probabilityPending", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var hide = typeof(QueryWorkbenchFrame).GetMethod("SuspendProbabilityPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
            key.SetValue(frame, "completed-observed-inputs"); pending.SetValue(frame, false); hide.Invoke(frame, null);
            Check((string)key.GetValue(frame)! == "completed-observed-inputs", "Hiding preserves the completed preview identity");
            pending.SetValue(frame, true); hide.Invoke(frame, null);
            Check((string)key.GetValue(frame)! == "" && !(bool)pending.GetValue(frame)!, "Hiding invalidates pending preview publication and requests recalculation on return");
            Console.WriteLine($"WORKBENCH_CONTEXT_REUSE coldRestoreAuthority=1;coldUseAuthority=1;unchangedHotAuthority=0;observations={frame.EditorInputCounts.Observations};authorities={frame.EditorInputCounts.AuthorityCaptures};allEditors=7;sourceCountsOnly=false");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            globalField.SetValue(null, originalGlobal);
            frame.Free();
        }
    }

    private static void CheckRetainedShellHotOpen(ModRuntimeSnapshot runtime, string directory)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        const string epoch = "IRONCLAD2_EPOCH";
        // Initialize() installed an in-memory SaveManager. Mutate only its real
        // progress data, using the same reveal consumed by native card pools.
        var progress = SaveManager.Instance.Progress;
        var epochs = (List<SerializableEpoch>)typeof(ProgressState).GetField("_epochs", flags)!.GetValue(progress)!;
        var originalEpochs = epochs.ToArray();
        var host = new Godot.Control { Size = new(1600, 900) };
        var shell = new WorkspaceShell { Visible = false };
        using var oldEntered = new ManualResetEventSlim();
        using var releaseOld = new ManualResetEventSlim();
        Task? oldWorker = null;
        try
        {
            epochs.Clear();
            epochs.AddRange(Vanilla111EpochBaseline.EpochIds.Select(id =>
                new SerializableEpoch(id, id == epoch ? EpochState.Obtained : EpochState.Revealed)));
            shell.InitializeWithPersistence(runtime, Path.Combine(directory, "retained-shell-hot-open"));
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).CurrentScene.AddChild(host);
            host.AddChild(shell);
            var frame = (QueryWorkbenchFrame)typeof(WorkspaceShell).GetField("_references", flags)!.GetValue(shell)!;
            var neow = (NeowSearchEditor)typeof(QueryWorkbenchFrame).GetField("_neowEditor", flags)!.GetValue(frame)!;
            NeowSearchUiCatalog Catalog() => (NeowSearchUiCatalog)typeof(NeowSearchEditor)
                .GetProperty("_catalog", flags)!.GetValue(neow)!;
            string EditorContext()
            {
                var draft = typeof(NeowSearchEditor).GetField("_draft", flags)!.GetValue(neow)!;
                return (string)draft.GetType().GetField("Context")!.GetValue(draft)!;
            }
            object Field(string name) => typeof(QueryWorkbenchFrame).GetField(name, flags)!.GetValue(frame)!;
            string[] DisplayedValues() => ((Godot.Label?[])Field("_expectationValues")).Select(label => label!.Text).ToArray();
            IEnumerable<string> LabelTexts(Godot.Node node) => node.GetChildren().SelectMany(child =>
                child is Godot.Label label ? new[] { label.Text } : LabelTexts(child));
            string DisplayedFamilies() => string.Join("\n", LabelTexts((Godot.Node)Field("_familyRows")));
            var update = typeof(QueryWorkbenchFrame).GetMethod("UpdateProbabilityPanel", flags)!;
            var poll = typeof(QueryWorkbenchFrame).GetMethod("PollProbabilityPreview", flags)!;
            var running = typeof(WorkbenchProbabilityPreviewQueue).GetField("_running", flags)!;
            bool Update() { var draft = frame.CaptureDraft(); return (bool)update.Invoke(frame, [draft, JsonSerializer.Serialize(draft)])!; }
            void AwaitPreview() => Check(SpinWait.SpinUntil(() =>
            {
                poll.Invoke(frame, null);
                return !(bool)Field("_probabilityPending");
            }, 30000), "Retained shell current probability preview completes");

            var newlyUnlocked = MegaCrit.Sts2.Core.Timeline.Epochs.Ironclad2Epoch.Cards
                .Select(c => new ModelKey(c.Id.Category, c.Id.Entry)).ToArray();
            Check(newlyUnlocked.All(k => !Catalog().CharacterCards.Contains(k)), "Obtained but unrevealed native card epoch is absent from the picker");
            var authored = new WorkbenchSearchDraft(Characters[0], 10, SearchQuery.Empty with
            {
                OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl),
                CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [Catalog().CommonCharacterCards.First()])
            }, AncientOptionConditionProfile.BroadDefault);
            frame.RestoreDraft(authored, render: false);
            string authoredQuery = JsonSerializer.Serialize(frame.CaptureDraft().Query);
            var requests = new System.Collections.Concurrent.ConcurrentQueue<WorkbenchProbabilityPreviewQueue.Request>();
            int blockOld = 0;
            var queue = new WorkbenchProbabilityPreviewQueue(request =>
            {
                requests.Enqueue(request);
                if (Interlocked.Exchange(ref blockOld, 0) == 1)
                {
                    oldEntered.Set();
                    if (!releaseOld.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("RetainedShellOldPreviewRelease");
                }
                return WorkbenchProbabilityPreview.Compute(request.Compiled, request.Target, request.Workers, request.CpuOnly);
            });
            typeof(QueryWorkbenchFrame).GetField("_probabilityPreview", flags)!.SetValue(frame, queue);

            shell.Open();
            Check(frame.IsVisibleInTree() && Update(), "First actual retained WorkspaceShell.Open admits its current preview");
            AwaitPreview();
            Check(((System.Collections.ICollection)Field("_familyEntries")).Count > 0,
                "First actual open publishes a completed family probability preview");
            var firstRequest = requests.Last();
            var firstCatalog = Catalog();
            string firstContext = EditorContext(), completedKey = (string)Field("_analysisKey");
            int authorities = frame.EditorInputCounts.AuthorityCaptures, completedRequests = requests.Count;
            string completedText = (string)Field("_expectationText");
            var completedValues = DisplayedValues(); string completedFamilies = DisplayedFamilies();
            shell.CleanupForTopLevelClose(); shell.Open();
            Check(frame.IsVisibleInTree() && DisplayedValues().SequenceEqual(completedValues) && DisplayedFamilies() == completedFamilies,
                "Unchanged actual Close/Open preserves completed displayed probability, ETA and family readings before any update");
            Check(Update() && !(bool)Field("_probabilityPending") && (string)Field("_analysisKey") == completedKey &&
                (string)Field("_expectationText") == completedText && requests.Count == completedRequests &&
                frame.EditorInputCounts.AuthorityCaptures == authorities && ReferenceEquals(Catalog(), firstCatalog),
                "Unchanged actual Close/Open reuses authority, picker and completed probability preview");

            // Start from a genuinely completed preview, without invalidating it or
            // manually updating/polling between the real reveal and visible Open.
            Check(completedValues[0].StartsWith("1 / ", StringComparison.Ordinal) && completedFamilies.Length > 0,
                "Completed-preview reopen fixture displays a numeric probability and family readings");
            shell.CleanupForTopLevelClose();
            progress.RevealEpoch(epoch);
            shell.Open();
            var reopenedValues = DisplayedValues();
            var note = (Godot.Label)Field("_expectationNote");
            Check(frame.IsVisibleInTree() && reopenedValues[0] != completedValues[0] && reopenedValues[1] == "—" && reopenedValues[2] == "—" &&
                note.IsVisibleInTree() && note.Text.Length > 0 && Field("_previewEta") is null,
                "Changed-environment actual reopen removes completed displayed probability and ETA before any update or poll");
            Check(((System.Collections.ICollection)Field("_familyEntries")).Count == 0 &&
                ((Godot.Node)Field("_familyRows")).GetChildren().All(child => !child.Name.ToString().StartsWith("ConditionEstimate_", StringComparison.Ordinal)) &&
                DisplayedFamilies().Length > 0 && DisplayedFamilies() != completedFamilies,
                "Changed-environment actual reopen replaces old rendered family readings with a pending display before any update or poll");
            Check(JsonSerializer.Serialize(frame.CaptureDraft().Query) == authoredQuery && newlyUnlocked.All(Catalog().CharacterCards.Contains),
                "Completed-preview actual reopen retains authored conditions and shows the revealed card catalog");
            Check(Update(), "Completed-preview changed reopen admits the current probability request");
            AwaitPreview();
            var revealedRequest = requests.Last();
            var revealedInputs = Field("_editorInputs");
            var revealedShared = (EditorRefreshContext)revealedInputs.GetType().GetField("_solo", flags)!.GetValue(revealedInputs)!;
            Check(((System.Collections.ICollection)Field("_familyEntries")).Count > 0 &&
                DisplayedValues()[0].StartsWith("1 / ", StringComparison.Ordinal) &&
                ReferenceEquals(revealedRequest.Compiled.Context.Authority, revealedShared.Authority) &&
                EditorContext().EndsWith(revealedShared.Key, StringComparison.Ordinal) &&
                newlyUnlocked.All(k => revealedRequest.Compiled.Context.Authority.EffectAuthority!.CharacterRewardPool!.Any(c => c.CardKey == k)),
                "Completed-preview changed reopen publishes a preview sharing the newly revealed picker authority");

            // Reset only the in-memory progress fixture so the original pending
            // old-worker rejection scenario still exercises the same real reveal.
            shell.CleanupForTopLevelClose();
            epochs.Clear();
            epochs.AddRange(Vanilla111EpochBaseline.EpochIds.Select(id =>
                new SerializableEpoch(id, id == epoch ? EpochState.Obtained : EpochState.Revealed)));
            shell.Open();
            Check(Update(), "Restored unrevealed fixture admits its current preview");
            AwaitPreview();
            Interlocked.Exchange(ref blockOld, 1);
            typeof(QueryWorkbenchFrame).GetMethod("InvalidateProbabilityPreview", flags)!.Invoke(frame, null);
            Check(Update() && oldEntered.Wait(TimeSpan.FromSeconds(5)), "Old-environment probability is running before actual close");
            oldWorker = (Task)running.GetValue(queue)!;
            shell.CleanupForTopLevelClose();
            progress.RevealEpoch(epoch);
            Check(newlyUnlocked.All(k => ModelDb.Character<Ironclad>().CardPool.GetUnlockedCards(
                    SaveManager.Instance.GenerateUnlockStateFromProgress(), MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint.SingleplayerOnly)
                .Any(c => c.Id.Category == k.Category && c.Id.Entry == k.Entry)),
                "Real mock progress reveal changes the native available-card pool");
            shell.Open(); // The outer Observe -> nested Refresh path missed by direct-frame checks.
            Check(newlyUnlocked.All(Catalog().CharacterCards.Contains) && !ReferenceEquals(Catalog(), firstCatalog) && EditorContext() != firstContext,
                "Actual retained WorkspaceShell.Open rebinds picker and editor context after a real card epoch reveal");
            Check(JsonSerializer.Serialize(frame.CaptureDraft().Query) == authoredQuery,
                "Changed-environment actual reopen retains every authored condition");
            Check(Update() && (bool)Field("_probabilityPending"), "New-environment actual reopen requests a current probability compilation");
            string currentKey = (string)Field("_analysisKey");
            releaseOld.Set();
            Check(oldWorker.Wait(TimeSpan.FromSeconds(30)), "Old-environment worker finishes after reopen");
            poll.Invoke(frame, null);
            Check((bool)Field("_probabilityPending") && (string)Field("_analysisKey") == currentKey &&
                ((System.Collections.ICollection)Field("_familyEntries")).Count == 0,
                "Late old-environment completion cannot publish over the new pending preview");
            AwaitPreview();
            var currentRequest = requests.Last();
            var editorInputs = Field("_editorInputs");
            var shared = (EditorRefreshContext)editorInputs.GetType().GetField("_solo", flags)!.GetValue(editorInputs)!;
            Check(!ReferenceEquals(currentRequest.Compiled.Context.Authority, firstRequest.Compiled.Context.Authority) &&
                ReferenceEquals(currentRequest.Compiled.Context.Authority, shared.Authority) && EditorContext().EndsWith(shared.Key, StringComparison.Ordinal) &&
                newlyUnlocked.All(k => currentRequest.Compiled.Context.Authority.EffectAuthority!.CharacterRewardPool!.Any(c => c.CardKey == k)),
                "Picker editor context and current probability compilation share the same newly unlocked authority");
            authorities = frame.EditorInputCounts.AuthorityCaptures; completedRequests = requests.Count;
            completedKey = (string)Field("_analysisKey"); var currentCatalog = Catalog();
            completedValues = DisplayedValues(); completedFamilies = DisplayedFamilies();
            shell.CleanupForTopLevelClose(); shell.Open();
            Check(DisplayedValues().SequenceEqual(completedValues) && DisplayedFamilies() == completedFamilies,
                "Unchanged actual reopen after reveal preserves completed displayed values and family readings before any update");
            Check(Update() && !(bool)Field("_probabilityPending") && requests.Count == completedRequests &&
                (string)Field("_analysisKey") == completedKey && frame.EditorInputCounts.AuthorityCaptures == authorities && ReferenceEquals(Catalog(), currentCatalog),
                "Unchanged actual reopen after reveal retains the completed new-environment preview and authority");
            Console.WriteLine($"WORKBENCH_RETAINED_SHELL_HOT_OPEN epoch={epoch};newCards={newlyUnlocked.Length};actualOpenClose=true;lateOldPreviewRejected=true;unchangedAuthority=0");
        }
        finally
        {
            releaseOld.Set();
            oldWorker?.Wait(TimeSpan.FromSeconds(30));
            epochs.Clear(); epochs.AddRange(originalEpochs);
            if (shell.GetParent() is not null) shell.GetParent().RemoveChild(shell);
            shell.Free();
            if (host.GetParent() is not null) host.GetParent().RemoveChild(host);
            host.Free();
        }
    }

    private static void ChangeObservedProgress(ref UnlockState __result)
    {
        var changed = __result.ToSerializable();
        changed.NumberOfRuns++;
        __result = UnlockState.FromSerializable(changed);
    }

    private static void AppendStartingDeckCard(ref IEnumerable<CardModel> __result)
    {
        var cards = __result.ToArray();
        __result = cards.Append(cards[0]).ToArray();
    }

    private static void CheckLegacy(ModRuntimeSnapshot runtime, QueryWorkbenchFrame frame, SearchPresetStore store, string directory)
    {
        SearchDraft legacy = new("", "", "", false, "", "", "", false, false, "", false, false, "", "", false)
        { NeowAny = BaseGameModelKeys.Relics.GoldenPearl.Serialized };
        var expectedIntent = new WorkbenchSearchDraft(Characters[0], 8, SearchQuery.Empty with
        { LegacyNeow = LegacyNeowSemanticConstraints.Empty with { NeowRelics = new([BaseGameModelKeys.Relics.GoldenPearl], [], []) } },
            AncientOptionConditionProfile.BroadDefault);
        var expected = expectedIntent.Compile(runtime, out _);
        foreach (int version in new[] { 1, 2 })
        {
            var persisted = new SearchPresetStore.PersistedSearchPreset
            {
                SchemaVersion = version, Id = "user:legacy" + version, Name = "legacy " + version,
                Title = version == 2 ? "legacy " + version : "", CharacterKey = Characters[0].Serialized, Ascension = 8,
                ConditionCount = 1, VisualRelicKey = BaseGameModelKeys.Relics.GoldenPearl.Serialized
            };
            if (version == 1) persisted.Draft = JsonSerializer.SerializeToElement(legacy, Json);
            else persisted.QuerySnapshot = JsonSerializer.SerializeToElement(legacy, Json);
            string folder = Path.Combine(directory, "search_presets");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "legacy" + version + ".json"), JsonSerializer.Serialize(persisted, Json));
            var loaded = new SearchPresetStore(directory).GetPresets().Single(p => p.Id == persisted.Id);
            Check(loaded.Workbench!.Query.OpeningRoute is null && loaded.Workbench.Query.LegacyNeow.NeowRelics.Any.Single() == BaseGameModelKeys.Relics.GoldenPearl,
                "Legacy offer identity remains an offered fact, never a selected pickup route or an empty query");
            frame.ApplyPreset(loaded, render: false);
            var actual = frame.CaptureDraft().Compile(runtime, out _);
            AssertCompiled(expected, actual, "v" + version + " legacy migration");
            Check(frame.CaptureDraft().Mode == WorldGameMode.Singleplayer && frame.CaptureDraft().Players.Count == 0,
                "Legacy applies as solo");
            var migrated = store.Save("migrated " + version, "legacy edit", [], Capture(frame.CaptureDraft()));
            Check(migrated.Workbench is not null && migrated.UnresolvedAsset is null, "Resaved legacy uses typed Workbench schema");
            frame.ApplyPreset(migrated, render: false);
            AssertCompiled(expected, frame.CaptureDraft().Compile(runtime, out _), "legacy resave compiles identically");
        }
        var official = new BuiltInSearchPresetProvider().GetPresets();
        Check(official.Count == 4, "Four official sources remain present");
        foreach (var preset in official)
        {
            frame.ApplyPreset(preset, render: false);
            var assembly = typeof(BuiltInSearchPresetProvider).Assembly;
            var resource = assembly.GetManifestResourceNames().Single(name =>
                name.StartsWith(BuiltInSearchPresetProvider.ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                BuiltInSearchPresetProvider.BuildOfficialId(name) == preset.Id);
            var source = ReadOfficial(resource);
            Check(source.Title.Length == 0 && source.Description.Length == 0 &&
                source.EnglishTitle is null && source.EnglishDescription is null &&
                preset.Title.Length > 0 && preset.Description.Length > 0 && preset.TitleFor("en") != preset.Title,
                "Bundled text is joined by stable identity while source JSON retains only preset data");
            var customSource = ReadOfficial(resource);
            customSource.Title = "Custom fixture title";
            customSource.Description = "Custom fixture description";
            customSource.EnglishTitle = "Custom translated title";
            customSource.EnglishDescription = "Custom translated description";
            var custom = BuiltInSearchPresetProvider.LoadFailSoft([resource], _ =>
                new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(customSource, Json))).Single();
            Check(custom.Title == customSource.Title && custom.Description == customSource.Description &&
                custom.TitleFor("en") == customSource.EnglishTitle && custom.DescriptionFor("en") == customSource.EnglishDescription,
                "Custom resource factory preserves its own text even when its stable ID matches a bundled preset");
            var oldDraft = JsonSerializer.Deserialize<SearchDraft>(source.QuerySnapshot, Json)!;
            var original = WorkbenchQueryCompiler.CompileAuthoredDraft(oldDraft, runtime, preset.CharacterKey, preset.Ascension, out _);
            SearchPresetStore.PersistedSearchPreset ReadOfficial(string name)
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                return JsonSerializer.Deserialize<SearchPresetStore.PersistedSearchPreset>(stream, Json)!;
            }
            var actual = frame.CaptureDraft().Compile(runtime, out _);
            var oldShop = original.Query.RelicSequenceConstraints.SingleOrDefault(c => c.Lane == RelicSequenceKind.Shop);
            if (oldShop is not null)
            {
                // Both shipped legacy shop requirements are one target in the
                // first draw: the new ordered first-shop slot is the same predicate.
                Check(oldShop.RangeValue == 1 && oldShop.Keys.Any.Count == 1 && oldShop.Keys.All.Count == 0 && oldShop.Keys.Ban.Count == 0,
                    "Bundled shop migration fixture is a single first-draw target");
                var shop = actual.Query.RelicShopSequenceConditions.Single();
                Check(shop.Count == 1 && shop.OrderMode == CombatRewardSequenceOrderMode.Ordered && shop.Slots.Single() == oldShop.Keys.Any.Single(),
                    "Official first-shop target retains its identity and exact position");
                original = SearchCompiler.Compile(original.Query with
                {
                    RelicSequenceConstraints = original.Query.RelicSequenceConstraints.Where(c => c.Lane != RelicSequenceKind.Shop).ToArray(),
                    RelicShopSequenceConditions = [new(1, CombatRewardSequenceOrderMode.Ordered, [oldShop.Keys.Any.Single()])]
                }, original.Context);
            }
            AssertCompiled(original, actual, "official " + preset.Id);
            Console.WriteLine("PRESET_OFFICIAL applied=" + preset.Id);
            Check(!frame.HasActiveSearch, "Official apply never starts Search");
        }
    }

    private static SearchPresetDefinition RoundTrip(ModRuntimeSnapshot runtime, QueryWorkbenchFrame frame,
        SearchPresetStore store, string directory, WorkbenchSearchDraft intended, string title)
    {
        // Compare fixture to editors before accepting any UI normalization as truth.
        frame.RestoreDraft(intended, render: false);
        AssertSameIntent(runtime, intended, frame.CaptureDraft(), title + " editor restore");
        var saved = store.Save(title, "table conditions", [], Capture(frame.CaptureDraft()));
        Check(saved.Workbench is not null && saved.UnresolvedAsset is null, title + " uses typed Workbench payload");
        Check(saved.Workbench is { Version: 5, GameVersion.Length: 0, Profile: null } &&
            saved.Workbench.Players.All(p => p.Unlocks is null), title + " is an unbound template in memory and on disk");
        var dto = SearchPresetStore.ToPersisted(saved, Json);
        Check(dto.SchemaVersion == 4, "New preset envelope uses v4 independently of game/template versions");
        string exported = JsonSerializer.Serialize(dto, Json);
        Check(!exported.Contains("OrderedSourceCandidates", StringComparison.Ordinal) &&
              !exported.Contains("MorphicGroveScenario\":{", StringComparison.Ordinal) &&
              !exported.Contains("WorkerCount", StringComparison.Ordinal) && !exported.Contains("ScanCount", StringComparison.Ordinal),
            "Preset omits captured pools and execution controls");
        // The DTO is the file interchange contract for ordinary/official preset files.
        var imported = SearchPresetStore.Normalize(JsonSerializer.Deserialize<SearchPresetStore.PersistedSearchPreset>(exported, Json)!,
            SearchPresetSource.User, Json);
        frame.ApplyPreset(imported, render: false);
        AssertSameIntent(runtime, intended, frame.CaptureDraft(), title + " file export/import");

        var reopened = new SearchPresetStore(directory).GetPresets().Single(p => p.Id == saved.Id);
        var fresh = new QueryWorkbenchFrame(runtime, new SearchWorkspace(Path.Combine(directory, title), false));
        fresh.ApplyPreset(reopened, render: false);
        AssertSameIntent(runtime, intended, fresh.CaptureDraft(), title + " save/reopen/fresh editors");
        fresh.Free();

        var edited = intended with { Query = intended.Query with
        { StandardMaps = [new(1, StandardMapMetric.ReachableMaxRest, StandardMapComparison.AtLeast, 2, true)] } };
        frame.RestoreDraft(edited, render: false);
        saved = store.Save(title, "updated table", [], Capture(frame.CaptureDraft()));
        reopened = new SearchPresetStore(directory).GetPresets().Single(p => p.Id == saved.Id);
        Check(reopened.Description == "updated table", "Existing preset update persists metadata");
        frame.ApplyPreset(reopened, render: false);
        AssertSameIntent(runtime, edited, frame.CaptureDraft(), title + " reload/edit/resave");
        var copy = store.SaveFromExisting(title + " copy", "copy", [], reopened);
        store.UpdateMetadata(copy.Id, title + " renamed", "renamed description", []);
        var renamed = new SearchPresetStore(directory).GetPresets().Single(p => p.Id == copy.Id);
        Check(renamed.Title == title + " renamed" && renamed.Description == "renamed description", "Rename and description persist");
        AssertSameIntent(runtime, edited, renamed.Workbench!, "Metadata editing retains table intent");
        Check(store.Delete(copy.Id) && new SearchPresetStore(directory).GetPresets().All(p => p.Id != copy.Id), "Delete removes selected copy only");
        Check(!frame.HasActiveSearch, "Apply does not launch Search");
        frame.RestoreDraft(intended, render: false);
        return store.Save(title, "table conditions", [], Capture(frame.CaptureDraft()));
    }

    internal static WorkbenchSearchDraft BuildDraft(ModRuntimeSnapshot runtime, int count)
    {
        var empty = new WorkbenchSearchDraft(Characters[0], 10, SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        var unlocks = Enumerable.Range(0, count).Select(_ => UnlockState.all.ToSerializable()).ToArray();
        OrderedPartyAuthority? party = count == 1 ? null : PartyRuntimeAuthorityCapture.Capture(runtime.Profile, "000000000000",
            Characters.Take(count).ToArray(), unlocks,
            10, runtime.Detection.NormalizedVersion, Enumerable.Repeat("AssumedFullyUnlocked", count).ToArray());
        empty.Compile(runtime, out var soloAuthority);
        var authority = party?.Players[0] ?? soloAuthority;
        var world = BossMapSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId, 10, authority.WorldAuthority);
        var variant = world.Sections.First().Variants.First();
        var ancientRows = Enumerable.Range(0, count).Select(i => AncientSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId,
            Characters[i], party?.Players[i].WorldAuthority ?? soloAuthority.WorldAuthority).Sections.First().Rows
            .Where(r => r.IdentityAvailable && r.Options.Count(o => !AncientOptionConditionProfile.AuthoredOptionKeys.Contains(o.OptionKey)) >= 2).ToArray()).ToArray();
        var ancient = ancientRows[0].First(r => ancientRows.All(rows => rows.Any(other => other.AncientKey == r.AncientKey)));
        var personalQueries = new List<PlayerOfferQuery>();
        var players = new List<WorkbenchPlayerDraft>();
        AncientEditorStateSnapshot? soloEditor = null;
        for (int slot = 0; slot < count; slot++)
        {
            var catalog = NeowSearchUiCatalog.CaptureForPicker(runtime, Characters[slot], 10, count, slot,
                count == 1 ? MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress() : UnlockState.FromSerializable(unlocks[slot]));
            var relics = RelicSequenceSearchUiCatalog.FromAuthority(runtime.Profile.ProfileId,
                party?.Players[slot].WorldAuthority ?? soloAuthority.WorldAuthority);
            var row = ancientRows[slot].Single(r => r.AncientKey == ancient.AncientKey);
            var unconditionalOptions = row.Options.Where(o => !AncientOptionConditionProfile.AuthoredOptionKeys.Contains(o.OptionKey)).ToArray();
            ModelKey option = unconditionalOptions[slot % unconditionalOptions.Length].OptionKey;
            ModelKey other = unconditionalOptions.First(o => o.OptionKey != option).OptionKey;
            var state = new AncientEditorStateSnapshot(0, 1,
            [new([new(2, ancient.AncientKey, [option], null)], false, [], 2),
             new([new(2, ancient.AncientKey, [other], null)], false, [], 2),
             new([], false, [], 1)]);
            var local = SearchQuery.Empty with
            {
                OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl),
                RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 3 + slot,
                    new([relics.LaneCandidates[RelicSequenceKind.Common].First()], [], []))],
                EventResultConditions = [new(EventResultConditionKind.FakeMerchantOfferedFakeRelic, Beta111EventResultCatalog.FakeMerchantRelics[slot])],
                AncientBranches = [new(2, ancient.AncientKey, [option], [])],
                MerchantColorlessSequenceConditions = [new(3, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Uncommon,
                    [null, catalog.ColorlessCards.First(k => catalog.CardPickerMetadata[k].Rarity == RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon), null])],
                CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Ordered, [null, catalog.CommonCharacterCards.First(), null]),
                CombatPotionRewards = new(3, CombatRewardSequenceOrderMode.Ordered,
                    [new(CombatPotionSlotRequirement.NoDrop, null), new(CombatPotionSlotRequirement.DropAny, null), new(CombatPotionSlotRequirement.Neutral, null)])
            };
            personalQueries.Add(new(slot, ModelKeySetFilter.Empty) { Conditions = local,
                AncientPremises = AncientOptionConditionProfile.BroadDefault with
                    { TezcataraHasBasicStrike = slot % 2 != 0, DarvAllowPandorasBoxRelicSet = slot == 0 } });
            players.Add(new(slot, Characters[slot], unlocks[slot], "AssumedFullyUnlocked") { AncientEditor = state });
            if (slot == 0) soloEditor = state;
        }
        var shared = SearchQuery.Empty with
        {
            VariantBossBranches = [new(1, variant.ActKey, new([variant.Bosses.First()], [], []), ModelKeySetFilter.Empty, false)],
            StandardMaps = [new(1, StandardMapMetric.ReachableMaxElite, StandardMapComparison.AtLeast, 2, true)]
        };
        if (count == 1) return empty with { AncientEditor = soloEditor, AncientPremises = personalQueries[0].AncientPremises, Query = personalQueries[0].Conditions with
            { OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl), VariantBossBranches = shared.VariantBossBranches, StandardMaps = shared.StandardMaps } };
        return empty with
        {
            Version = 4, Mode = WorldGameMode.Multiplayer, Players = players, GameVersion = runtime.Detection.NormalizedVersion,
            Profile = runtime.Profile.ProfileId,
            ObservationVersion = OrderedPartyAuthority.ObservationVersion,
            PartyAncientEditor = new(false, [new(2, [ancient.AncientKey]), new(3, [])], [new(2, [ancient.AncientKey]), new(3, [])]),
            Query = shared with { Players = personalQueries, LegacyWorld = LegacyWorldSemanticConstraints.Empty with
                { AncientIdentityFilters = [new(2, new([ancient.AncientKey], [], []))] } }
        };
    }

    private static void CheckPoolRebind(ModRuntimeSnapshot runtime, QueryWorkbenchFrame frame, SearchPresetStore store, string directory, WorkbenchSearchDraft solo)
    {
        solo.Compile(runtime, out var authority);
        var kind = EventResultConditionKind.MorphicGroveGroupInitialBasicsContains;
        var scenario = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredEventResult(authority, kind);
        ModelKey target = scenario.Targets[0].OrderedSourceCandidates![0].CardKey;
        var authored = solo with { Query = solo.Query with { EventResultConditions =
            [new(kind, target) { MorphicGroveSecondCard = target, MorphicGroveScenario = scenario }] } };
        var saved = store.Save("pool rebound", "", [], Capture(authored));
        var loaded = new SearchPresetStore(directory).GetPresets().Single(p => p.Id == saved.Id);
        Check(loaded.Workbench!.Query.EventResultConditions.Single().MorphicGroveScenario is null &&
            !JsonSerializer.Serialize(loaded.Workbench, Json).Contains("OrderedSourceCandidates", StringComparison.Ordinal),
            "An actual captured event pool is removed from the preset");
        var compiled = loaded.Workbench.BindPresetIntent(runtime, _ => throw new InvalidOperationException("SoloHasNoPartySlot")).Compile(runtime, out _);
        Check(compiled.Query.EventResultConditions.Single().MorphicGroveScenario is { Targets.Count: > 0 } rebound &&
            rebound.Targets[0].OrderedSourceCandidates is { Count: > 0 }, "Compile rebinds event pool from current authority");
        var expected = authored.Compile(runtime, out _);
        void CheckRebound(CompiledSearch actual, string name)
        {
            var before = expected.Query.EventResultConditions.Single().MorphicGroveScenario!;
            var after = actual.Query.EventResultConditions.Single().MorphicGroveScenario!;
            Check(before.Premises == after.Premises && JsonNode.DeepEquals(JsonSerializer.SerializeToNode(before.Targets, Json),
                JsonSerializer.SerializeToNode(after.Targets, Json)), name + ": ordered captured targets and premises");
            // Fresh authority captures contain diagnostic timestamps. Preserve
            // independently rebound pool contents while sharing only that capture
            // identity for a semantic comparison under the actual compile context.
            var sameCapture = actual.Query with { EventResultConditions = actual.Query.EventResultConditions.Select(c =>
                c with { MorphicGroveScenario = new(before.Authority, after.Premises, after.Targets) }).ToArray() };
            AssertCompiled(expected, SearchCompiler.Compile(sameCapture, actual.Context), name);
        }
        CheckRebound(compiled, "Rebound pool retains authored event semantics");
        frame.ApplyPreset(loaded, render: false);
        CheckRebound(frame.CaptureDraft().Compile(runtime, out _), "Event preset applies through editor staging");
    }

    private static void CheckProtection(ModRuntimeSnapshot runtime, QueryWorkbenchFrame frame, string directory, WorkbenchSearchDraft party)
    {
        var valid = SearchPresetStore.BuildFromCapture("user:protection", SearchPresetSource.User, "protection", "", [], Capture(party), Json);
        var node = JsonSerializer.SerializeToNode(SearchPresetStore.ToPersisted(valid, Json), Json)!;
        foreach (string shape in new[] { "unknown-field", "future-workbench", "missing-player", "unknown-reference", "party-transform" })
        {
            var damaged = node.DeepClone();
            var workbench = damaged["WorkbenchSnapshot"]!;
            if (shape == "unknown-field") workbench["Query"]!["FuturePredicate"] = true;
            if (shape == "future-workbench") workbench["Version"] = 99;
            if (shape == "missing-player") workbench["Players"]![1] = null;
            if (shape == "unknown-reference") workbench["Query"]!["Players"]![0]!["Conditions"]!["AncientBranches"]![0]!["OptionAny"]![0] = "RELIC:PRESET_UNKNOWN_REFERENCE";
            if (shape == "party-transform") workbench["Query"]!["Players"]![0]!["Conditions"]!["TransformationAggregate"] =
                JsonSerializer.SerializeToNode(new TransformationAggregateCondition(TransformationOpening.None, TransformationPickupOrder.Any,
                    true, false, false, TransformationAggregatePredicate.RareCountAtLeast, 1, []), Json);
            var definition = SearchPresetStore.Normalize(JsonSerializer.Deserialize<SearchPresetStore.PersistedSearchPreset>(damaged.ToJsonString(), Json)!,
                SearchPresetSource.User, Json);
            var before = frame.CaptureDraft();
            bool rejected = false;
            try { frame.ApplyPreset(definition, render: false); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidDataException) { rejected = true; }
            Check(rejected, shape + " must not report a successful partial load");
            AssertSameIntent(runtime, before, frame.CaptureDraft(), shape + " leaves current editors intact");
            var protectedStore = new SearchPresetStore(Path.Combine(directory, shape));
            var copy = protectedStore.SaveFromExisting("preserved", "", [], definition);
            protectedStore.UpdateMetadata(copy.Id, "renamed", "raw retained", []);
            var reloaded = new SearchPresetStore(Path.Combine(directory, shape)).GetPresets().Single();
            var preserved = reloaded.UnresolvedAsset is { } asset
                ? JsonNode.Parse(asset.OriginalJson)!["WorkbenchSnapshot"] : JsonSerializer.SerializeToNode(reloaded.Workbench, Json);
            Check(JsonNode.DeepEquals(workbench, preserved), shape + " raw or complete typed payload survives save-as and metadata edit");
        }
        string folder = Path.Combine(directory, "bad-files", "search_presets");
        Directory.CreateDirectory(folder);
        var future = node.DeepClone(); future["SchemaVersion"] = 99;
        var futureField = node.DeepClone(); futureField["FutureTableContext"] = new JsonObject { ["Required"] = true };
        var raw = new Dictionary<string, string> { ["truncated.json"] = "{\"WorkbenchSnapshot\":", ["future.json"] = future.ToJsonString(),
            ["future-field.json"] = futureField.ToJsonString() };
        foreach (var entry in raw) File.WriteAllText(Path.Combine(folder, entry.Key), entry.Value);
        var store = new SearchPresetStore(Directory.GetParent(folder)!.FullName);
        Check(store.GetPresets().Count == 0, "Corrupt and future-envelope files are unavailable");
        store.Save("valid neighbor", "", [], Capture(party));
        foreach (var entry in raw) Check(File.ReadAllText(Path.Combine(folder, entry.Key)) == entry.Value, "Unrelated save preserves bad/future file bytes");
    }

    private static SearchPresetCapture Capture(WorkbenchSearchDraft draft) => new(draft.Character, draft.Ascension, draft.ToPresetIntent(), 1,
        SearchPresetProbabilitySnapshot.Unavailable(Captured), SearchPresetProvenance.Unknown("0.111.0", Captured), Captured) { Workbench = draft };

    private static void CheckUnvisitedPartyAncientSeat(ModRuntimeSnapshot runtime, string directory)
    {
        var path = Path.Combine(directory, "unvisited-ancient-seat");
        var persistence = new SearchWorkspace(path, false);
        var frame = new QueryWorkbenchFrame(runtime, persistence) { Size = new(1536, 772) };
        QueryWorkbenchFrame? reopened = null;
        QueryWorkbenchFrame? applied = null;
        QueryWorkbenchFrame? inconsistent = null;
        try
        {
            frame.Refresh("en", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en"));
            frame.SetSearchMode(true, render: false);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var ancient = (AncientSearchEditor)typeof(QueryWorkbenchFrame).GetField("_ancientEditor", flags)!.GetValue(frame)!;
            var fixture = BuildDraft(runtime, 2);
            var local = fixture.Query.Players[0].Conditions;
            var selected = local.AncientBranches.Single();
            var personal = new AncientEditorStateSnapshot(0, 2,
                [new([new(selected.Act, selected.AncientKey, selected.OptionAny, null)], false, [], 1),
                 new([], false, [], 1), new([], false, [], 1)]);
            ancient.ImportSharedPartyQuery(SearchQuery.Empty with { LegacyWorld = LegacyWorldSemanticConstraints.Empty with
                { AncientIdentityFilters = [new(selected.Act, new([selected.AncientKey], [], []))] } },
                new(false, [new(selected.Act, [selected.AncientKey])], []));
            ancient.ImportQuery(SearchQuery.Empty with { AncientBranches = [selected] }, AncientOptionConditionProfile.BroadDefault, personal);

            void CheckSeats(WorkbenchSearchDraft draft, string stage)
            {
                Check(draft.Query.LegacyWorld.AncientIdentityFilters.Single().Keys.Any.Single() == selected.AncientKey &&
                    draft.Query.Players[0].Conditions.AncientBranches.Single().OptionAny.SequenceEqual(selected.OptionAny) &&
                    draft.Query.Players[1].Conditions.AncientBranches.Single().AncientKey == selected.AncientKey &&
                    draft.Query.Players[1].Conditions.AncientBranches.Single().OptionAny.Count == 0 &&
                    draft.Players[1].AncientEditor!.Modes.All(mode => mode.Rows.All(row => row.Options.Count == 0)),
                    stage + " keeps shared identity and P1's personal option out of unvisited P2");
            }
            CheckSeats(frame.CaptureDraft(), "Fresh 2P capture");
            // Use the ordinary automatic-save path before ever selecting P2.
            typeof(QueryWorkbenchFrame).GetMethod("SaveDraft", flags)!.Invoke(frame, []);
            persistence.FlushAll();
            reopened = new QueryWorkbenchFrame(runtime, new SearchWorkspace(path, false)) { Size = new(1536, 772) };
            reopened.Refresh("en", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en"));
            CheckSeats(reopened.CaptureDraft(), "Autosave/reopen");
            var preset = persistence.Presets.SaveUserPreset("unvisited P2", "", [], frame.CapturePreset());
            applied = new QueryWorkbenchFrame(runtime, new SearchWorkspace(Path.Combine(path, "applied"), false));
            applied.ApplyPreset(preset, render: false);
            CheckSeats(applied.CaptureDraft(), "Save/apply preset");

            var original = frame.CaptureDraft();
            var legacyBad = original with { Players = original.Players.Select(player => player.Slot == 1
                ? player with { AncientEditor = original.Players[0].AncientEditor } : player).ToArray() };
            var badPath = Path.Combine(path, "legacy-sidecar-conflict");
            var badPersistence = new SearchWorkspace(badPath, false);
            badPersistence.SaveWorkbench(legacyBad); badPersistence.FlushAll();
            var savedBefore = JsonSerializer.Serialize(badPersistence.LoadWorkbench(), Json);
            inconsistent = new QueryWorkbenchFrame(runtime, badPersistence) { Size = new(1536, 772) };
            inconsistent.Refresh("en", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en"));
            Check((bool)typeof(QueryWorkbenchFrame).GetField("_loadFailed", flags)!.GetValue(inconsistent)! &&
                JsonSerializer.Serialize(new SearchWorkspace(badPath, false).LoadWorkbench(), Json) == savedBefore,
                "An older conflicting P2 sidecar cannot replace its query and automatic restore preserves the original saved draft");

            foreach (int mode in new[] { 1, 2 })
            {
                // Reproduce the old normal 2P capture shape with only P1's
                // advanced Neow preference; P2 has never been selected.
                var neowKeys = new[] { BaseGameModelKeys.Relics.GoldenPearl };
                ModelKeySetFilter neowFilter = mode == 1 ? new(neowKeys, [], []) : new([], neowKeys, []);
                var neowQuery = SearchQuery.Empty with { LegacyNeow = LegacyNeowSemanticConstraints.Empty with { NeowRelics = neowFilter } };
                var neowSidecar = new AncientEditorStateSnapshot(mode, mode,
                    [new([], false, [], 1), new([], mode == 1, mode == 1 ? neowKeys : [], 1),
                     new([], mode == 2, mode == 2 ? neowKeys : [], 1)]);
                ancient.ImportSharedPartyQuery(SearchQuery.Empty, new(true, [], []));
                ancient.ImportQuery(neowQuery, AncientOptionConditionProfile.BroadDefault, neowSidecar);
                var authoredNeow = frame.CaptureDraft();
                void CheckNeowSeats(WorkbenchSearchDraft draft, string stage) => Check(
                    JsonSerializer.Serialize(draft.Query.Players[0].Conditions.LegacyNeow.NeowRelics, Json) == JsonSerializer.Serialize(neowFilter, Json) &&
                    draft.Query.Players[1].Conditions.LegacyNeow.NeowRelics.IsEmpty &&
                    draft.Players[1].AncientEditor!.Modes.All(savedMode => !savedMode.NeowEnabled && savedMode.NeowOptions.Count == 0),
                    stage + " retains advanced Neow mode " + mode + " only for P1 while P2 stays independent");
                CheckNeowSeats(authoredNeow, "Fresh capture");
                typeof(QueryWorkbenchFrame).GetMethod("SaveDraft", flags)!.Invoke(frame, []);
                persistence.FlushAll();
                var validNeow = new QueryWorkbenchFrame(runtime, new SearchWorkspace(path, false)) { Size = new(1536, 772) };
                var conflictingNeowPath = Path.Combine(path, "legacy-neow-sidecar-conflict-" + mode);
                var conflictingNeowStore = new SearchWorkspace(conflictingNeowPath, false);
                var conflictingNeow = new QueryWorkbenchFrame(runtime, conflictingNeowStore) { Size = new(1536, 772) };
                try
                {
                    validNeow.Refresh("en", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en"));
                    CheckNeowSeats(validNeow.CaptureDraft(), "Autosave/reopen");
                    var legacyNeowBad = authoredNeow with { Players = authoredNeow.Players.Select(player => player.Slot == 1
                        ? player with { AncientEditor = authoredNeow.Players[0].AncientEditor } : player).ToArray() };
                    Check(legacyNeowBad.Query.Players.All(player => player.Conditions.AncientBranches.Count == 0 &&
                        player.Conditions.LegacyWorld.AncientOptionFilters.Count == 0) &&
                        legacyNeowBad.Query.Players[1].Conditions.LegacyNeow.NeowRelics.IsEmpty &&
                        legacyNeowBad.Players[1].AncientEditor!.Modes[mode].NeowEnabled,
                        "Historical copied sidecar fixture conflicts only in P2's active Neow authoring, without Ancient rows");
                    conflictingNeowStore.SaveWorkbench(legacyNeowBad); conflictingNeowStore.FlushAll();
                    string authoredFile = Path.Combine(conflictingNeowPath, "RolltheSpire2", "state", "query_workbench.json");
                    byte[] originalBytes = File.ReadAllBytes(authoredFile);
                    conflictingNeow.Refresh("en", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en"));
                    typeof(QueryWorkbenchFrame).GetMethod("SaveDraft", flags)!.Invoke(conflictingNeow, []);
                    conflictingNeowStore.FlushAll();
                    Check((bool)typeof(QueryWorkbenchFrame).GetField("_loadFailed", flags)!.GetValue(conflictingNeow)! &&
                        (string)typeof(QueryWorkbenchFrame).GetField("_restoreFailure", flags)!.GetValue(conflictingNeow)! == "integration.load_shape" &&
                        File.ReadAllBytes(authoredFile).SequenceEqual(originalBytes),
                        "Historical advanced Neow-only P2 sidecar conflict blocks automatic restore/resave and preserves the original file byte for byte");
                }
                finally { conflictingNeow.Free(); validNeow.Free(); }
            }
        }
        finally { inconsistent?.Free(); applied?.Free(); reopened?.Free(); frame.Free(); }
    }

    private static void CheckBossOnlyPreset(ModRuntimeSnapshot runtime, QueryWorkbenchFrame frame,
        SearchPresetStore store, WorkbenchSearchDraft solo)
    {
        var boss = solo.Query.VariantBossBranches.Single().FirstBoss.Any.Single();
        var intended = solo with { AncientEditor = new(0, 2,
                [new([], false, [], 1), new([], false, [], 1), new([], false, [], 1)]),
            NeowEditor = null, EventEditor = null, TransformationEditor = null,
            Query = SearchQuery.Empty with { LegacyWorld = LegacyWorldSemanticConstraints.Empty with
                { BossOrdinalFilters = [new(1, 1, new([boss], [], []))] } } };
        frame.RestoreDraft(intended, render: false);
        var saved = store.Save("Boss without variant", "", [], frame.CapturePreset());
        frame.ApplyPreset(saved, render: false);
        Check(frame.CaptureDraft().Query.VariantBossBranches.Count == 0 &&
            frame.CaptureDraft().Query.LegacyWorld.BossOrdinalFilters.Single().Keys.Any.Single() == boss,
            "Boss-only preset saves/applies without inventing an explicit variant");
        AssertSameIntent(runtime, intended, frame.CaptureDraft(), "Boss-only preset intent");
    }

    private static void CheckUnavailableAuthoredIdentityRetention(ModRuntimeSnapshot runtime, QueryWorkbenchFrame frame)
    {
        var card = new ModelKey("CARD", "MISSING_MOD_CARD");
        var relic = new ModelKey("RELIC", "MISSING_MOD_RELIC");
        var ancient = new ModelKey("EVENT", "MISSING_MOD_ANCIENT");
        var option = new ModelKey("RELIC", "MISSING_MOD_OPTION");
        var eventKey = new ModelKey("EVENT", "MISSING_MOD_EVENT");
        var variant = new ModelKey("ACT", "MISSING_MOD_ACT");
        var boss = new ModelKey("ENCOUNTER", "MISSING_MOD_BOSS");
        var query = SearchQuery.Empty with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.Kaleidoscope),
            StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.Kaleidoscope,
                NeowStructuredConditionKind.IndependentOfferGroupTargets, NeowStructuredEffectScope.SelectableOfferGroups,
                NeowStructuredOutputKind.Card, [card], AllowDuplicateOutputs: true)],
            AncientBranches = [new(2, ancient, [option], [])],
            RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 2, new([relic], [], []))],
            EventSequenceConstraints = [new(1, null, SearchSequenceRangeMode.FirstN, 3, new([eventKey], [], []))],
            EventResultConditions = [new(EventResultConditionKind.TrashHeapDiveRelic, relic)],
            MerchantColorlessSequenceConditions = [new(2, CombatRewardSequenceOrderMode.Ordered, MerchantColorlessSlot.Uncommon, [card, null])],
            RelicShopSequenceConditions = [new(2, CombatRewardSequenceOrderMode.Ordered, [relic, null])],
            CombatCardRewards = new(2, CombatRewardSequenceOrderMode.Ordered, [card, null]),
            VariantBossBranches = [new(1, variant, new([boss], [], []), ModelKeySetFilter.Empty, false)]
        };
        var authored = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10, query, AncientOptionConditionProfile.BroadDefault);
        frame.RestoreDraft(authored, render: false);
        foreach (int ascension in new[] { 10, 9, 10 })
        {
            // Rebind to current catalogs; unavailable stable identities remain authored.
            frame.RestoreDraft(authored with { Ascension = ascension }, render: false);
            var restored = frame.CaptureDraft().Query;
            Check(restored.StructuredOpeningEffects.Single().OutputKeys.Contains(card), "N retains unavailable Mod target on catalog refresh");
            Check(restored.AncientBranches.Single().AncientKey == ancient && restored.AncientBranches.Single().OptionAny.Contains(option), "A retains unavailable identity and option");
            Check(restored.RelicSequenceConstraints.Single().Keys.Any.Contains(relic), "R retains unavailable target and authored lane");
            Check(restored.EventSequenceConstraints.Single().Keys.Any.Contains(eventKey) && restored.EventResultConditions.Single().TargetKey == relic, "E retains queue and result identities");
            Check(restored.MerchantColorlessSequenceConditions.Single().Slots[0] == card && restored.RelicShopSequenceConditions.Single().Slots[0] == relic, "S retains unavailable card/relic targets");
            Check(restored.CombatCardRewards!.Slots[0] == card, "C exports its authored target independently of picker availability");
            Check(restored.VariantBossBranches.Single().VariantKey == variant && restored.VariantBossBranches.Single().FirstBoss.Any.Contains(boss), "W retains simple unavailable Act/Boss identities");
        }
        frame.RestoreDraft(authored with { Query = SearchQuery.Empty with { OpeningRoute = new(relic) } }, render: false);
        Check(frame.CaptureDraft().Query.OpeningRoute?.RouteRelicKey == relic, "Unknown Mod opening identity is never replaced by an empty query");
        var text = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh");
        var transform = new TransformationSearchEditor((_, _) => { });
        try
        {
            var condition = new TransformationAggregateCondition(TransformationOpening.LeafyPoultice, TransformationPickupOrder.Any,
                false, false, false, TransformationAggregatePredicate.ContainsMultiset, 0, [card]);
            var transformQuery = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice), TransformationAggregate = condition };
            // Production binds the seat before restoring its authored draft.
            transform.Refresh("zh", text, 0, [new("N.LeafyPoultice", BaseGameModelKeys.Relics.LeafyPoultice, 3)], [], render: false);
            transform.ImportQuery(transformQuery);
            transform.Refresh("zh", text, 0, [], [], render: false);
            bool missingSource = false;
            try { transform.ExportCondition(transformQuery); }
            catch (InvalidOperationException ex) { missingSource = ex.Message.Contains("TransformationDeclaredSourcesUnavailable:N.LeafyPoultice", StringComparison.Ordinal); }
            Check(missingSource, "T retains missing source and explains its exact identity rather than weakening the query");
            transform.Refresh("zh", text, 0, [new("N.LeafyPoultice", BaseGameModelKeys.Relics.LeafyPoultice, 3)], [], render: false);
            Check(transform.ExportCondition(transformQuery)!.TargetMultiset.Single() == card,
                "T target survives unavailable pool/source and is restored when the declared source rebinds");
        }
        finally { transform.Free(); }
    }

    private static void CheckAutomaticRestorePreservesSource(ModRuntimeSnapshot runtime, string directory, WorkbenchSearchDraft solo)
    {
        var path = Path.Combine(directory, "restore-source-preservation");
        var store = new SearchWorkspace(path, false);
        // Duplicate equivalent OR terms have a different representation, not a new
        // impossibility or a reason to change the seed set or block the UI.
        var source = solo with { Query = solo.Query with { VariantBossBranches = solo.Query.VariantBossBranches.Concat(solo.Query.VariantBossBranches).ToArray() } };
        store.SaveWorkbench(source); store.FlushAll();
        var before = JsonSerializer.Serialize(store.LoadWorkbench(), Json);
        store.SaveWorkbenchPreferences(new Dictionary<string, bool>(), "events");
        store.AdoptPreparedWorkbenchRead(Task.Run(store.PrepareWorkbenchRead).GetAwaiter().GetResult());
        var restored = new QueryWorkbenchFrame(runtime, store) { Size = new Godot.Vector2(1536, 772) };
        try
        {
            restored.Refresh("zh", RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("zh"));
            // This fixture deliberately duplicates an equivalent boss OR term.
            // The editor represents that term once; its compiled fingerprint is
            // syntactic, so compare the complete original solo intent here and
            // separately require byte-for-byte persisted source preservation below.
            AssertSameIntent(runtime, solo, restored.CaptureDraft(), "Prepared first restore retains complete semantic intent");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var rendered = (HashSet<string>)typeof(QueryWorkbenchFrame).GetField("_renderedEditorDomains", flags)!.GetValue(restored)!;
            Check(rendered.SetEquals(["events"]), "First restore renders the saved active domain after final semantic binding");
            restored.CloseSearch();
            Check(JsonSerializer.Serialize(new SearchWorkspace(path, false).LoadWorkbench(), Json) == before,
                "Automatic restore and page close preserve the original authored draft representation");
            Check(!restored.HasActiveSearch, "Restore never dispatches Search");
        }
        finally { restored.Free(); }
    }

    private static void AssertSameIntent(ModRuntimeSnapshot runtime, WorkbenchSearchDraft expected, WorkbenchSearchDraft actual, string name)
    {
        if (expected.Version == 5) expected = expected.BindPresetIntent(runtime, _ => throw new InvalidOperationException("UnexpectedCurrentContextFixture"));
        if (actual.Version == 5) actual = actual.BindPresetIntent(runtime, _ => throw new InvalidOperationException("UnexpectedCurrentContextFixture"));
        AssertCompiled(expected.Compile(runtime, out _), actual.Compile(runtime, out _), name);
        Check(expected.Mode == actual.Mode && expected.Players.Count == actual.Players.Count, name + ": mode/count");
        Check(expected.Players.Select(p => (p.Slot, p.Character, p.UnlockSource)).SequenceEqual(actual.Players.Select(p => (p.Slot, p.Character, p.UnlockSource))), name + ": ordered roster/sources");
        Check(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected.Players.Select(p => p.Unlocks), Json),
            JsonSerializer.SerializeToNode(actual.Players.Select(p => p.Unlocks), Json)), name + ": unlock configuration");
        bool ancientMatches = JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected.AncientEditor, Json), JsonSerializer.SerializeToNode(actual.AncientEditor, Json)) &&
              JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected.PartyAncientEditor, Json), JsonSerializer.SerializeToNode(actual.PartyAncientEditor, Json)) &&
              JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected.Players.Select(p => p.AncientEditor), Json), JsonSerializer.SerializeToNode(actual.Players.Select(p => p.AncientEditor), Json));
        if (!ancientMatches)
        {
            string path = Path.Combine(_evidence, "ancient-mismatch.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { name,
                expected = new { expected.AncientEditor, expected.PartyAncientEditor, Players = expected.Players.Select(p => new { p.Slot, p.AncientEditor }) },
                actual = new { actual.AncientEditor, actual.PartyAncientEditor, Players = actual.Players.Select(p => new { p.Slot, p.AncientEditor }) } }, Json));
            Console.WriteLine("PRESET_ANCIENT_MISMATCH " + path);
        }
        Check(ancientMatches, name + ": active and inactive Ancient editor intent");
    }

    private static void AssertCompiled(CompiledSearch expected, CompiledSearch actual, string name)
    {
        if (expected.SemanticFingerprint != actual.SemanticFingerprint)
        {
            string path = Path.Combine(_evidence, "compiled-mismatch.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { name,
                expected = new { expected.SemanticFingerprint, expected.NormalizedQuery },
                actual = new { actual.SemanticFingerprint, actual.NormalizedQuery } }, Json));
            Console.WriteLine("PRESET_MISMATCH " + path);
        }
        Check(expected.SemanticFingerprint == actual.SemanticFingerprint, name + ": compiled query fingerprint");
        Check(expected.Context.CharacterKey == actual.Context.CharacterKey && expected.Context.Ascension == actual.Context.Ascension &&
            expected.Context.ProfileId == actual.Context.ProfileId && expected.Context.UnlockSnapshotFingerprint == actual.Context.UnlockSnapshotFingerprint &&
            expected.Context.EvaluationAssumptions == actual.Context.EvaluationAssumptions && expected.Context.Party?.Fingerprint == actual.Context.Party?.Fingerprint,
            name + ": compiled context");
    }

    private static void Check(bool condition, string name)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("PRESET_WORKBENCH " + name);
    }
}
