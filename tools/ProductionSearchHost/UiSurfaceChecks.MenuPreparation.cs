using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunMenuPreparationChecksAsync(Node root, ModRuntimeSnapshot runtime, Action<bool, string> check)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        string fixtureRoot = Path.Combine(OS.GetUserDataDir(), "menu-preparation-checks", Guid.NewGuid().ToString("N"));
        string nativeBefore = MenuNativeSignature();
        var observations = new List<object>();
        var authored = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice) },
            AncientOptionConditionProfile.BroadDefault);
        (SearchWorkspace Store, string File) Fixture(string name)
        {
            string folder = Path.Combine(fixtureRoot, name);
            var store = new SearchWorkspace(folder, initializeSearchCursor: false);
            store.SaveWorkbench(authored);
            return (store, Path.Combine(folder, "RolltheSpire2", "state", "query_workbench.json"));
        }
        async Task Frame() => await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Ready(MenuWorkspacePreparation preparation)
        {
            int frames = 0;
            var published = RuntimeAuthorityEnvironment.Current;
            while (!preparation.IsReady && frames++ < 1500)
            {
                preparation.Advance(TimeSpan.FromMilliseconds(2));
                if (!preparation.IsReady) await Frame();
            }
            check(preparation.IsReady, "menu candidate completes through bounded idle slices and worker derivation");
            check(ReferenceEquals(published, RuntimeAuthorityEnvironment.Current), "idle preparation never publishes shared environment Current");
        }
        void Fresh(MenuWorkspaceEntryInputs entry)
        {
            var fresh = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(runtime.Profile,
                ProfileSeedGenerator.CreateProbeSeed(runtime.Profile), CharacterIdentity.FromKey(entry.Character),
                entry.Ascension, runtime.Detection.DisplayVersion, predictionGameMode: WorldGameMode.Singleplayer,
                predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
            check(MenuSameValues(entry.Solo, fresh), "entry solo snapshot matches complete fresh native authority including source provenance");
            check(entry.Solo.Character.CharacterKey == entry.Character && entry.Solo.Ascension == entry.Ascension,
                "entry selected character and ascension own the returned solo authority");
            check(MenuSameValues(entry.Environment, SearchEnvironmentSignatureBuilder.Capture(runtime)),
                "entry environment signature equals current complete native capture");
            check(entry.Freshness.Length > 0, "entry provides editor input freshness");
        }

        var immediateFixture = Fixture("immediate");
        using (var immediate = new MenuWorkspacePreparation(runtime, immediateFixture.Store))
        {
            var published = RuntimeAuthorityEnvironment.Current;
            check(!immediate.IsReady && immediate.NativeSlices == 0, "immediate entry requires no prior idle frame");
            check(ReferenceEquals(published, RuntimeAuthorityEnvironment.Current), "constructing preparation does not publish Current");
            var entry = immediate.CompleteForEntry();
            check(immediate.IsReady, "immediate entry retains its completed derivations rather than rescheduling them behind the open surface");
            check(entry.Character == authored.Character && entry.Ascension == authored.Ascension, "immediate entry follows saved authored context");
            Fresh(entry);
            check(MenuSameValues(immediateFixture.Store.LoadWorkbench(), authored), "immediate adoption preserves the complete saved draft");
            observations.Add(new { Phase = "immediate", State = immediate.Status });
        }

        var partialFixture = Fixture("partial");
        using (var partial = new MenuWorkspacePreparation(runtime, partialFixture.Store))
        {
            var published = RuntimeAuthorityEnvironment.Current;
            partial.Advance(TimeSpan.FromTicks(1));
            check(!partial.IsReady && partial.NativeSlices == 1, "one minimal slice leaves partial preparation resumable");
            check(ReferenceEquals(published, RuntimeAuthorityEnvironment.Current), "partial candidate does not publish Current");
            Fresh(partial.CompleteForEntry());
            observations.Add(new { Phase = "partial", State = partial.Status });
        }

        var readyFixture = Fixture("ready");
        using (var ready = new MenuWorkspacePreparation(runtime, readyFixture.Store))
        {
            await Ready(ready);
            check(ready.ReusedContexts == 0, "idle preparation has not certified cross-frame contexts before entry");
            var first = ready.CompleteForEntry();
            check(ready.ReusedContexts == 2, "ready entry reuses environment and selected derivations only after matching actual fresh inputs");
            Fresh(first);
            var second = ready.CompleteForEntry();
            check(ready.ReusedContexts == 4 && ReferenceEquals(first.Solo, second.Solo),
                "same-context entry retains the same derived authority after another full observation");
            observations.Add(new { Phase = "ready-reused", State = ready.Status });
        }

        var retiredFixture = Fixture("retired");
        var retired = new MenuWorkspacePreparation(runtime, retiredFixture.Store);
        retired.Advance(TimeSpan.FromTicks(1));
        var beforeRetirement = RuntimeAuthorityEnvironment.Current;
        retired.Dispose();
        retired.Advance(TimeSpan.FromMilliseconds(2));
        for (int i = 0; i < 3; i++) await Frame();
        bool retiredRejected = false;
        try { retired.CompleteForEntry(); }
        catch (InvalidOperationException ex) when (ex.Message == "MenuPreparationAccountRetired") { retiredRejected = true; }
        check(retiredRejected && !retired.IsReady && ReferenceEquals(beforeRetirement, RuntimeAuthorityEnvironment.Current),
            "retired partial cursors and late worker completions cannot publish or enter");
        check(MenuSameValues(retiredFixture.Store.LoadWorkbench(), authored), "retirement leaves authored draft intact");

        var accountFixture = Fixture("account");
        using (var account = new MenuWorkspacePreparation(runtime, accountFixture.Store))
        {
            account.Advance(TimeSpan.FromTicks(1));
            var save = SaveManager.Instance;
            int originalAccount = save.CurrentProfileId;
            ProgressState originalProgress = save.Progress;
            var published = RuntimeAuthorityEnvironment.Current;
            try
            {
                save.InitProfileId(originalAccount + 1);
                save.InitProgressData();
                account.Advance(TimeSpan.FromMilliseconds(2));
                bool rejected = false;
                try { account.CompleteForEntry(); }
                catch (InvalidOperationException ex) when (ex.Message == "MenuPreparationAccountRetired") { rejected = true; }
                check(rejected && ReferenceEquals(published, RuntimeAuthorityEnvironment.Current),
                    "changed account rejects the old session before publishing any candidate");
            }
            finally
            {
                save.InitProfileId(originalAccount);
                save.InitProgressData();
                save.Progress = originalProgress;
            }
            check(save.CurrentProfileId == originalAccount && ReferenceEquals(save.Progress, originalProgress),
                "account probe restores original profile and exact progress object");
        }

        var changedFixture = Fixture("external-draft-replacement");
        using (var changed = new MenuWorkspacePreparation(runtime, changedFixture.Store))
        {
            await Ready(changed);
            byte[] originalBytes = File.ReadAllBytes(changedFixture.File);
            DateTime originalTime = File.GetLastWriteTimeUtc(changedFixture.File);
            string replacement = Encoding.UTF8.GetString(originalBytes)
                .Replace(authored.Character.Serialized, BaseGameModelKeys.Characters.Defect.Serialized, StringComparison.Ordinal)
                .Replace("\"Ascension\": 10", "\"Ascension\": 20", StringComparison.Ordinal);
            byte[] replacementBytes = Encoding.UTF8.GetBytes(replacement);
            check(replacementBytes.Length <= originalBytes.Length && !replacementBytes.SequenceEqual(originalBytes),
                "external draft probe constructs distinct authored bytes");
            byte[] sameLength = Encoding.UTF8.GetBytes(replacement + new string(' ', originalBytes.Length - replacementBytes.Length));
            File.WriteAllBytes(changedFixture.File, sameLength);
            File.SetLastWriteTimeUtc(changedFixture.File, originalTime);
            check(new FileInfo(changedFixture.File).Length == originalBytes.Length && File.GetLastWriteTimeUtc(changedFixture.File) == originalTime,
                "external replacement preserves file size and timestamp");
            var entry = changed.CompleteForEntry();
            check(entry.Character == BaseGameModelKeys.Characters.Defect && entry.Ascension == 20,
                "entry observes complete current draft bytes and reselects changed character and ascension");
            Fresh(entry);
            var loaded = changedFixture.Store.LoadWorkbench();
            check(loaded is not null && loaded.Character == entry.Character && loaded.Ascension == entry.Ascension &&
                MenuSameValues(loaded.Query, authored.Query), "replacement keeps all authored conditions through selected-context rebinding");
            observations.Add(new { Phase = "external-draft-replacement", State = changed.Status });
        }

        var invalidFixture = Fixture("invalid-draft");
        using (var invalid = new MenuWorkspacePreparation(runtime, invalidFixture.Store))
        {
            invalid.Advance(TimeSpan.FromTicks(1));
            byte[] invalidBytes = Encoding.UTF8.GetBytes("{\"Query\":");
            File.WriteAllBytes(invalidFixture.File, invalidBytes);
            var originalFailure = invalidFixture.Store.PrepareWorkbenchRead().Error;
            check(originalFailure is JsonException, "invalid fixture retains its original strict JSON parse error");
            invalid.CompleteForEntry();
            check(File.ReadAllBytes(invalidFixture.File).SequenceEqual(invalidBytes) &&
                Directory.GetFiles(Path.GetDirectoryName(invalidFixture.File)!, "query_workbench.json.corrupt-*").Length == 0,
                "preparation and entry adoption do not quarantine or overwrite unreadable authored bytes");
            Exception? loadFailure = null;
            try { invalidFixture.Store.LoadWorkbench(); } catch (Exception ex) { loadFailure = ex; }
            check(loadFailure is InvalidDataException { Message: "integration.load_shape", InnerException: JsonException } &&
                loadFailure.InnerException!.Message == originalFailure!.Message,
                "normal LoadWorkbench still reports original parse cause after preparation instead of an empty draft");
            var quarantined = Directory.GetFiles(Path.GetDirectoryName(invalidFixture.File)!, "query_workbench.json.corrupt-*");
            check(quarantined.Length == 1 && File.ReadAllBytes(quarantined[0]).SequenceEqual(invalidBytes),
                "normal-load quarantine preserves invalid bytes only inside the isolated fixture");
        }

        // One real content mutation verifies the menu-to-entry consumer seam;
        // the separate observation checks cover the full catalog/matrix probes.
        var contentFixture = Fixture("starter-change");
        using (var content = new MenuWorkspacePreparation(runtime, contentFixture.Store))
        {
            await Ready(content);
            var starter = ModelDb.Character<Ironclad>().StartingDeck.First();
            var upgradeField = typeof(CardModel).GetField("_currentUpgradeLevel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? originalLevel = upgradeField.GetValue(starter);
            try
            {
                upgradeField.SetValue(starter, 1);
                var entry = content.CompleteForEntry();
                check(content.ReusedContexts < 2, "changed actual starting deck rejects at least its dependent selected candidate");
                Fresh(entry);
            }
            finally { upgradeField.SetValue(starter, originalLevel); }
            observations.Add(new { Phase = "starter-change", State = content.Status });
        }
        check(nativeBefore == MenuNativeSignature(), "menu preparation checks restore original native models, profile progress and any existing RunState/RNG");
        GD.Print("MENU_PREPARATION_PASS " + JsonSerializer.Serialize(new { FixtureRoot = fixtureRoot, Observations = observations }));
    }
}
