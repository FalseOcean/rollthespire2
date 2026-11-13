using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class PartyPhase1Checks
{
    // Focused native route: --run-entry --entry-extended-party. This exercises the
    // actual in-run overlay/controller, while the independent native Neow oracle
    // owns a different RunState and never buys or grants anything in the live one.
    private static async Task CheckExtendedPartyRunEntry(Node host, GameVersionDetection detection, string output)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, fields)!.GetValue(owner)!;
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("Extended live party: " + message); }
        var json = new JsonSerializerOptions { IncludeFields = true };
        var receipts = new List<object>();
        var profile = Beta111Profile.Instance;
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = profile, Detection = detection,
            Compatibility = RuntimeProfileRegistry.Resolve(detection) };
        var previousLocal = LocalContext.NetId;
        bool previousTestMode = TestMode.IsOn;
        var previousWindowSize = host.GetWindow().Size;
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard).Assembly);
        var store = SearchWorkspace.Open(OS.GetUserDataDir());
        string OfflineContext() => JsonSerializer.Serialize(new { store.PredictorContext.Party,
            store.PredictorContext.PartyActive, store.PredictorContext.PartySeed,
            store.PredictorContext.PlayersCount, store.PredictorContext.PlayerSlotIndex }, json);
        string offlineBefore = OfflineContext();
        Check(SeedPredictionInputLimits.MaximumPlayers == 4 &&
            (int)typeof(QueryWorkbenchFrame).GetField("MaximumPreviewPlayers", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()! == 4,
            "offline request and Search configuration limits remain four");
        try
        {
            host.GetWindow().Size = new Vector2I(1600, 900);
            for (int frame = 0; frame < 3; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (int count in new[] { 5, 8, 16 })
            {
                const string seed = "R93FXJN77VRC";
                Player[] CreatePlayers() => Enumerable.Range(0, count).Select(slot => Player.CreateForNewRun(
                    ModelDb.AllCharacters.Single(c => c.Id.Entry == BaseGameModelKeys.Characters.All[slot % BaseGameModelKeys.Characters.All.Count].Entry),
                    slot % 2 == 0 ? UnlockState.all : UnlockState.none, (ulong)(1000 + slot * 17))).ToArray();
                TestMode.IsOn = true;
                var players = CreatePlayers();
                var run = RunState.CreateForTest(players, gameMode: GameMode.Custom, ascensionLevel: 10, seed: seed);
                var oraclePlayers = CreatePlayers();
                var oracle = RunState.CreateForTest(oraclePlayers, gameMode: GameMode.Custom, ascensionLevel: 10, seed: seed);
                var expectedOffers = new string[count][];
                RunManager.Instance.SetUpTest(oracle, new NetSingleplayerGameService(), shouldSave: false);
                try
                {
                    for (int slot = 0; slot < count; slot++)
                    {
                        var neow = ModelDb.Event<Neow>().ToMutable();
                        await neow.BeginEvent(oraclePlayers[slot], null, false);
                        expectedOffers[slot] = neow.CurrentOptions.Select(option => option.Relic!.Id.Entry).ToArray();
                    }
                }
                finally { RunManager.Instance.CleanUp(); }
                TestMode.IsOn = false;
                LocalContext.NetId = players[^1].NetId;
                RuntimeAuthorityEnvironment.CaptureOnMainThread(runtime);
                string Signature() => JsonSerializer.Serialize(new { run.Rng.StringSeed, Rng = run.Rng.ToSerializable(),
                    Players = run.Players.Select(p => new { p.NetId, State = p.ToSerializable(), Rng = p.PlayerRng.ToSerializable() }).ToArray() }, json);
                string before = Signature();
                var captured = WorkspaceShell.CaptureRunPredictionContext(run, "0.111.0", profile.ProfileId,
                    LocalContext.NetId, new PredictorContextDocument());
                Check(captured.Players.Count == count && captured.SelectedSlot == count - 1,
                    $"{count}p captures the full roster and last local slot");
                Check(captured.Players.Select(p => p.Character.Entry).SequenceEqual(players.Select(p => p.Character.Id.Entry)) &&
                    captured.Players.Select(p => JsonSerializer.Serialize(p.Unlocks, json)).SequenceEqual(
                        players.Select(p => JsonSerializer.Serialize(p.UnlockState.ToSerializable(), json))),
                    $"{count}p captures actual repeated characters and per-player unlocks");
                bool offlineRejected = false;
                try { SeedLibraryStore.ValidateContext(captured); }
                catch (InvalidDataException) { offlineRejected = true; }
                Check(offlineRejected, $"{count}p context cannot enter the offline seed library");
                var stack = new EntryOverlayStackFixture { Size = new Vector2(1600, 900) };
                host.AddChild(stack);
                var overlay = new RunPredictionOverlay();
                try
                {
                    overlay.Initialize(runtime, run, stack, null);
                    overlay.Open();
                    var shell = Field<WorkspaceShell>(overlay, "_shell");
                    var page = Field<AnalysisPage>(shell, "_predictor");
                    var bar = Field<AnalysisRequestBar>(page, "_requestBar");
                    var selector = Field<OptionButton>(bar, "_partyPlayer");
                    Check(selector.ItemCount == count && selector.Selected == count - 1,
                        $"{count}p player selector exposes all slots and selects the local player");
                    Check(Field<Label>(bar, "_partySummary").Text.Contains($"P1 → P{count}", StringComparison.Ordinal),
                        $"{count}p pickup-order premise names the actual complete party");
                    var visited = new List<object>();
                    void CheckSelected(int slot)
                    {
                        var request = page.LastRequest;
                        var facts = page.LastFacts;
                        Check(request is not null && request.PlayersCount == count && request.PlayerSlotIndex == slot &&
                            request.Character.CharacterKey.Entry == players[slot].Character.Id.Entry,
                            $"{count}p P{slot + 1} has actual request identity/count");
                        Check(page.CurrentDraft.PlayersCount == count && page.CurrentDraft.PlayerSlotIndex == slot && selector.Selected == slot,
                            $"{count}p P{slot + 1} controls preserve the selected high slot");
                        Check(facts is { InputStatus: SeedPredictionInputStatus.Ready, Neow: not null } &&
                            facts.Neow.Choices.Select(choice => choice.RelicKey.Entry).SequenceEqual(expectedOffers[slot]),
                            $"{count}p P{slot + 1} displayed N identities match separate native BeginEvent");
                        Check(facts is { World: not null, Relics: not null, CombatRewards: not null } &&
                            facts.Context.PlayerSlotIndex == slot && facts.Context.PlayersCount == count &&
                            facts.Context.CharacterKey == request!.Character.CharacterKey &&
                            facts.Context.UnlockSnapshotFingerprint == request.Authority.UnlockSnapshotFingerprint &&
                            request.Authority.WorldAuthority?.Beta109Generation?.PersonalPlayerSlot == slot,
                            $"{count}p P{slot + 1} World, Relics and CombatRewards return under the actual personal context");
                        Check(!facts!.Diagnostics.Concat(facts.World!.Diagnostics).Concat(facts.Relics!.Diagnostics)
                                .Concat(facts.CombatRewards!.Diagnostics).Any(diagnostic => diagnostic.Code == PredictionDiagnosticCodes.Exception),
                            $"{count}p P{slot + 1} information domains contain no exception diagnostic");
                        Check(request!.Authority.AllCharacterCardPoolsUnlocked == (slot % 2 == 0),
                            $"{count}p P{slot + 1} retains its actual personal unlock authority");
                        Check(!SeedPredictionRequest.TryCreate(seed, request.Character, request.Ascension, count, slot,
                            request.Authority, request.AncientOptionConditions, request.Domains,
                            request.RelicSequencePreviewCount, false, out _, out var error) && error == SeedPredictionRequestError.PlayersCountOutOfRange,
                            $"{count}p offline request factory still refuses expanded counts");
                        Check(Signature() == before, $"{count}p P{slot + 1} display leaves the native run unchanged");
                        visited.Add(new { Slot = slot, Character = request.Character.CharacterKey.Entry,
                            Offers = facts.Neow!.Choices.Select(choice => choice.RelicKey.Entry).ToArray(),
                            WorldBoss = facts.World.BossStatus.ToString(), WorldAncient = facts.World.AncientStatus.ToString(),
                            Relics = facts.Relics.Status.ToString(), CombatRewards = facts.CombatRewards.Status.ToString() });
                    }
                    CheckSelected(count - 1);
                    int? explicitChoice = null;
                    foreach (int slot in new[] { 0, count / 2, count - 1, count / 2 })
                    {
                        selector.Select(slot);
                        selector.EmitSignal(OptionButton.SignalName.ItemSelected, (long)slot);
                        CheckSelected(slot);
                        if (slot == 0)
                        {
                            var alternative = page.LastFacts!.Neow!.Choices.First(choice =>
                                choice.SlotIndex != page.PreferredOpeningChoiceSlotIndex && choice.OpeningRewardContinuations?.Routes.Count > 0);
                            typeof(AnalysisPage).GetMethod("ApplyOpeningSelection", fields)!.Invoke(page,
                                [alternative.RelicKey, alternative.OpeningRewardContinuations!.Routes[0].Route.RouteId]);
                            explicitChoice = alternative.SlotIndex;
                            Check(page.PreferredOpeningChoiceSlotIndex == explicitChoice,
                                $"{count}p player can choose another opening within the captured roster");
                        }
                    }
                    int selectedBeforeClose = selector.Selected;
                    for (int reopen = 0; reopen < 2; reopen++)
                    {
                        typeof(RunPredictionOverlay).GetMethod("Close", fields)!.Invoke(overlay, null);
                        Check(!overlay.IsOpen, $"{count}p native close removes the overlay entry");
                        overlay.Open();
                        Check(ReferenceEquals(page, Field<AnalysisPage>(shell, "_predictor")) && overlay.IsOpen,
                            $"{count}p reopen retains the Analysis page");
                        CheckSelected(selectedBeforeClose);
                    }
                    selector.Select(0);
                    selector.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
                    Check(page.PreferredOpeningChoiceSlotIndex == explicitChoice,
                        $"{count}p explicit opening survives switching and both overlay reopens");
                    selector.Select(selectedBeforeClose);
                    selector.EmitSignal(OptionButton.SignalName.ItemSelected, (long)selectedBeforeClose);
                    foreach (string name in new[] { "_railSolo", "_railMulti", "_partyConfig", "_railCharacter" })
                    {
                        var control = Field<Control>(bar, name);
                        Check(!control.IsVisibleInTree() || control is BaseButton { Disabled: true },
                            $"{count}p live roster control {name} cannot author an offline context");
                    }
                    Check(!Field<LineEdit>(bar, "_seedInput").Editable,
                        $"{count}p seed remains tied to the actual run");
                    Check(Field<AscensionStepper>(bar, "_ascension").GetChildren().OfType<BaseButton>().All(button => button.Disabled),
                        $"{count}p ascension remains tied to the actual run");
                    Check(!page.CanFavoriteVisibleSeed && !Field<Button>(page, "_favoriteSeed").IsVisibleInTree() &&
                        !Field<Button>(bar, "_copySeed").Disabled,
                        $"{count}p expanded session keeps seed copy available without publishing an offline favorite");
                    Check(OfflineContext() == offlineBefore, $"{count}p session does not overwrite offline party memory");
                    for (int frame = 0; frame < 3; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (DisplayServer.GetName() != "headless")
                    {
                        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"live-party-{count}p.png"));
                        if (count == 16)
                        {
                            selector.ShowPopup();
                            for (int frame = 0; frame < 3; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                            var popup = selector.GetPopup();
                            Check(popup.Visible && popup.ItemCount == 16 && popup.GetItemText(15).StartsWith("P16", StringComparison.Ordinal),
                                "expanded native player dropdown exposes the actual sixteenth slot");
                            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                            Check(popup.GetTexture().GetImage().SavePng(Path.Combine(output, "live-party-16p-player-dropdown.png")) == Error.Ok,
                                "save the complete sixteen-player popup viewport");
                            host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "live-party-16p-dropdown-open.png"));
                            popup.Hide();
                        }
                    }
                    receipts.Add(new { Players = count, LocalSlot = count - 1, SelectedAfterReopen = selectedBeforeClose,
                        Visited = visited, NativeOfferParity = true, NativeRunUnchanged = Signature() == before,
                        OfflineMemoryUnchanged = OfflineContext() == offlineBefore });
                    if (count == 16)
                    {
                        // Intentional offline navigation changes its own preferences.
                        // The subsequent live reimport must preserve that new memory,
                        // rather than treating the same seed as a valid live cache hit.
                        var controller = Field<AnalysisPageController>(shell, "_predictorController");
                        controller.OpenPredictionContext(seed, captured with
                        {
                            Players = captured.Players.Take(2).ToArray(), SelectedSlot = 0
                        });
                        Check(page.LastRequest is { PlayersCount: 2, PlayerSlotIndex: 0 } && controller.CurrentRunPredictionContext is null,
                            "same-seed offline navigation replaces the expanded live document");
                        string offlineAfterImport = OfflineContext();
                        typeof(RunPredictionOverlay).GetMethod("Close", fields)!.Invoke(overlay, null);
                        overlay.Open();
                        Check(selector.ItemCount == 16 && selector.Selected == 15,
                            "reopening after same-seed offline navigation restores all sixteen actual players and the local slot");
                        CheckSelected(15);
                        Check(controller.CurrentRunPredictionContext is { } restored && restored.Context.Players.Count == 16 &&
                            OfflineContext() == offlineAfterImport,
                            "expanded live reimport restores its session without overwriting intentionally imported offline memory");
                        receipts.Add(new { Players = 16, Scenario = "SameSeedOfflineImportThenLiveReopen", RestoredLocalSlot = 15,
                            NativeRunUnchanged = Signature() == before, OfflineMemoryUnchangedAfterIntentionalImport = OfflineContext() == offlineAfterImport });
                    }
                }
                finally
                {
                    overlay.Retire(); stack.Clear(); host.RemoveChild(stack); stack.QueueFree();
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                Check(Signature() == before, $"{count}p retirement does not mutate the live run");
            }
            File.WriteAllText(Path.Combine(output, "live-party-information.json"),
                JsonSerializer.Serialize(new { Checks = checks, Cases = receipts }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"LIVE_PARTY_INFORMATION_PASS checks={checks};players=5,8,16;nativeNeowParity=true;switchAndReopen=true;sameSeedOfflineImportRecovery=true;liveSessionOfflineWrites=false;liveMutation=false");
        }
        finally { LocalContext.NetId = previousLocal; TestMode.IsOn = previousTestMode; host.GetWindow().Size = previousWindowSize; }
    }
}
