using System.Reflection;
using System.Collections.Immutable;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Core.PredictorRuntime;

internal static partial class UiSurfaceChecks
{
    internal static async Task RunPredictorAsync(Node host, string output, string pck)
    {
        Directory.CreateDirectory(output);
        // The lightweight Godot host needs the same installed Spine extension
        // that the game uses for its native monster visual scenes.
        if (!ClassDB.ClassExists("SpineSprite") && OperatingSystem.IsWindows())
        {
            string library = Path.Combine(Path.GetDirectoryName(pck)!, "libspine_godot.windows.template_release.x86_64.dll");
            if (!File.Exists(library)) throw new Exception("Installed game Spine library missing");
            string extension = Path.Combine(output, "game-spine.gdextension");
            File.WriteAllText(extension, "[configuration]\nentry_symbol = \"spine_godot_library_init\"\ncompatibility_minimum = \"4.1\"\n[libraries]\nwindows.x86_64 = \"" + library.Replace('\\', '/') + "\"\n");
            var loaded = GDExtensionManager.LoadExtension(extension);
            if (!ClassDB.ClassExists("SpineSprite")) throw new Exception("Game Spine extension unavailable: " + loaded);
        }
        if (!ProjectSettings.LoadResourcePack(pck, false)) throw new Exception("Game PCK missing");
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard).Assembly);
        typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State", BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, MegaCrit.Sts2.Core.Modding.ModManagerState.Initialized);
        MegaCrit.Sts2.Core.Models.ModelDb.Init(); MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init(); MegaCrit.Sts2.Core.Models.ModelDb.InitIds();
        MegaCrit.Sts2.Core.Saves.SaveManager.MockInstanceForTesting(new MegaCrit.Sts2.Core.Saves.SaveManager(
            new MegaCrit.Sts2.Core.Saves.Test.MockGodotFileIo("user://predictor-surface-memory"), true));
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitProfileId(1);
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language = "eng";
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        host.GetWindow().Size = new Vector2I(1600, 900);
        RenderingServer.SetDefaultClearColor(new Color("131C29"));
        var asm = typeof(SearchCompiler).Assembly;
        asm.GetType("RolltheSpire2.Infrastructure.Snapshots.RuntimeSnapshotThreadGuard", true)!
            .GetMethod("BindCurrentThread", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        object New(string name, params object?[] args) => Activator.CreateInstance(asm.GetType("RolltheSpire2." + name, true)!, args)!;
        object? Call(object value, string name, params object?[] args)
        {
            var method = value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var parameters = method.GetParameters();
            return method.Invoke(value, args.Concat(parameters.Skip(args.Length).Select(p => p.DefaultValue)).ToArray());
        }
        object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
        var runtimeType = asm.GetType("RolltheSpire2.Bootstrap.ModRuntimeSnapshot", true)!;
        var runtime = runtimeType.GetProperty("NotInitialized")!.GetValue(null)!;
        runtimeType.GetProperty("Profile")!.SetValue(runtime, Beta111Profile.Instance);
        var detection = new GameVersionDetection("0.111.0", "0.111.0", "beta", "PredictorSurface", true, "");
        runtimeType.GetProperty("Detection")!.SetValue(runtime, detection);
        var icons = New("Ui.Icons.ReflectionGameIconResolver", "predictor-surface");
        var tooltip = (Control)New("Ui.Tooltips.AnchoredTooltipHost", New("Infrastructure.ContentNames.RuntimeRelicTooltipResolver"), New("Infrastructure.ContentNames.RuntimePotionTooltipResolver"), New("Infrastructure.ContentNames.RuntimeCardTooltipResolver"));
        var page = (Control)New("Ui.Pages.Analysis.AnalysisPage", runtime, icons, New("Ui.Icons.NativeCharacterPoolIconProvider", icons), BaseGameModelKeys.Characters.All, tooltip);
        var canvas = new Control { Position = new Vector2(32, 96), Size = new Vector2(1536, 772) };
        host.AddChild(canvas); canvas.AddChild(page); host.AddChild(tooltip);
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(ProjectSettings.GlobalizePath("res://Corpus/v1/context.json")).Context;
        // Encounter replay parity is unchanged; this pass checks presentation only.
        var fixtureAuthority = context.Authority.WithWorldAuthority(context.Authority.WorldAuthority! with { EncounterNumberOfRuns = 1 });
        string? layoutSeed = System.Environment.GetEnvironmentVariable("RT2_PREDICTOR_LAYOUT_SEED");
        if (!SeedPredictionRequest.TryCreate(layoutSeed ?? "00000103CP3W", context.Authority.Character, 10, 1, 0, fixtureAuthority,
            context.EvaluationAssumptions.AncientEligibilityAssumptions, SeedPredictionDomainSelection.All, SeedPredictionInputLimits.MaximumRelicSequencePreviewCount,
            false, out var request, out var issue)) throw new Exception(issue.ToString());
        var document = RuntimeProfileRegistry.Predict(detection, request!);
        if (document.Sections.SelectMany(s => s.EncounterSequences).Sum(a => a.Normal.Count + a.Elite.Count) == 0) throw new Exception("Encounter document not connected");
        System.Collections.IDictionary Maps() => (System.Collections.IDictionary)Field(page, "_overviewMaps");
        System.Collections.IDictionary Routes() => (System.Collections.IDictionary)Field(page, "_overviewRoutes");
        System.Collections.IDictionary Metrics() => (System.Collections.IDictionary)Field(page, "_overviewRouteMetrics");
        object Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
        int checks = 0;
        int eventResultChecks = 0;
        int tiedRouteChecks = 0;
        if (OS.GetCmdlineUserArgs().Contains("--seed-event-options-only"))
        {
            string[] entries = ["DOLL_ROOM", "DENSE_VEGETATION", "JUNGLE_MAZE_ADVENTURE", "LOST_WISP",
                "LUMINOUS_CHOIR", "CRYSTAL_SPHERE", "SUNKEN_TREASURY", "SUNKEN_STATUE", "THIS_OR_THAT", "WHISPERING_HOLLOW"];
            var eventUnlocks = new MegaCrit.Sts2.Core.Unlocks.UnlockState(
                MegaCrit.Sts2.Core.Timeline.EpochModel.AllEpochIds.ToImmutableArray(),
                MegaCrit.Sts2.Core.Models.ModelDb.AllEncounters.Select(e => e.Id).ToImmutableArray(), 1).ToSerializable();
            foreach (string language in new[] { "zh", "en" })
            {
                MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language = language == "zh" ? "zhs" : "eng";
                MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
                var text = JsonUiTextProvider.CreatePredictorUi13(language);
                var names = (RolltheSpire2.Presentation.ContentNames.IGameContentNameResolver)
                    asm.GetType("RolltheSpire2.Infrastructure.ContentNames.RuntimeGameContentNameResolver")!
                        .GetMethod("Create")!.Invoke(null, [language])!;
                Call(page, "ApplyLocalization", text, names);
                Call(page, "ShowDocument", request, document, eventUnlocks);
                var model = (SeedAnalysisViewModel)Field(page, "_viewModel");
                var template = model.EventPoolSequenceDomain.Items.SelectMany(a => a.Entries).First();
                foreach (string entry in entries)
                {
                    Call(page, "DetachOpeningDonors");
                    var center = (Control)Field(page, "_wbCenter");
                    foreach (var child in center.GetChildren()) { center.RemoveChild(child); child.QueueFree(); }
                    var key = new ModelKey("EVENT", entry);
                    var display = GameContentDisplayPresentationBuilder.Build(key,
                        RolltheSpire2.Presentation.ContentNames.GameContentKind.Event, names, false);
                    Call(page, "RenderEventDetails", center, template with { EventDisplay = display });
                    for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    var expected = RolltheSpire2.Core.Events.Beta111EventOptionResultProjector.Project(
                        Beta111Profile.Instance.ComputeRootSeed(document.CanonicalSeed), request!.PlayerSlotIndex, entry);
                    var options = center.FindChild("StableEventOptions", true, false);
                    if (options is null || options.GetChildCount() != expected.Count)
                        throw new Exception("Seed event options missing: " + entry);
                    string[] labels = Descendants(center).OfType<Label>().Select(l => l.Text).ToArray();
                    foreach (var option in expected)
                    {
                        if (!labels.Contains(text.Get($"predictor.event.stable.{entry}.{option.OptionKey}")))
                            throw new Exception("Localized option missing: " + entry);
                        if (option.GoldAmount is { } gold)
                        {
                            bool cost = entry is "LUMINOUS_CHOIR" or "CRYSTAL_SPHERE" or "WHISPERING_HOLLOW";
                            if (!labels.Contains(text.Format(cost ? "predictor.event.stable.cost" : "predictor.event.stable.gold", gold)))
                                throw new Exception("Gold result missing: " + entry);
                        }
                        foreach (var relic in option.Relics)
                            if (!labels.Contains(names.Resolve(relic, RolltheSpire2.Presentation.ContentNames.GameContentKind.Relic)))
                                throw new Exception("Doll identity missing");
                        checks++;
                    }
                    if (labels.Contains(text.Get("predictor.design.event_result_unavailable")))
                        throw new Exception("Known result incorrectly marked unavailable: " + entry);
                    if (entry == "WHISPERING_HOLLOW" && (!labels.Contains(text.Get("predictor.event.transform_scope")) ||
                        center.FindChild("EventTransformResults", true, false) is null))
                        throw new Exception("Whispering Hollow lost the existing Basic-transform premise/result");
                    if (entry is "DOLL_ROOM" or "JUNGLE_MAZE_ADVENTURE" or "WHISPERING_HOLLOW")
                    {
                        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"event-{language}-{entry}.png"));
                    }
                    var cache = (System.Collections.IDictionary)Field(page, "_stableEventResults");
                    var saved = cache[entry];
                    var scratch = new VBoxContainer();
                    Call(page, "RenderStableEventResults", scratch, entry);
                    if (!ReferenceEquals(saved, cache[entry])) throw new Exception("Event projection recomputed within context");
                    scratch.Free();
                    checks += 2;
                }
                Call(page, "ClearWorkbenchProjections");
                if (((System.Collections.IDictionary)Field(page, "_stableEventResults")).Count != 0)
                    throw new Exception("Context clear retained event option values");
                checks++;
            }
            canvas.QueueFree(); tooltip.QueueFree();
            for (int frame = 0; frame < 3; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"SEED_EVENT_OPTIONS_PASS checks={checks};events=10;languages=zh,en;existingTransformRetained;contextCache");
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--runtime-route"))
        {
            MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress = new MegaCrit.Sts2.Core.Saves.ProgressState();
            // The page must use this declared prediction context, independently
            // of the empty mock account configured above.
            var declaredUnlocks = new MegaCrit.Sts2.Core.Unlocks.UnlockState(
                MegaCrit.Sts2.Core.Timeline.EpochModel.AllEpochIds.ToImmutableArray(),
                MegaCrit.Sts2.Core.Models.ModelDb.AllEncounters.Select(e => e.Id).ToImmutableArray(), 1);
            var routeUnlocks = declaredUnlocks.ToSerializable();
            if (MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable()
                .UnlockedEpochs.SequenceEqual(routeUnlocks.UnlockedEpochs))
                throw new Exception("Route UI fixture needs distinct account and declared unlocks");
            var discovered = fixtureAuthority.WorldAuthority!.Beta109Generation!.ActSelectionGroups
                .SelectMany(g => g.Candidates).Where(c => c.DiscoveredInSingleplayer).Select(c => c.ActKey).Distinct().ToArray();
            var previousTestMode = MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn;
            RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot routeAuthority;
            try
            {
                MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
                routeAuthority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(Beta111Profile.Instance,
                    request!.OriginalSeed, request.Character, request.Ascension, "0.111.0",
                    predictionGameMode: RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer,
                    predictionGameModeAuthority: RolltheSpire2.Core.Authority.PredictionGameModeAuthority.ExplicitRequest,
                    explicitUnlockState: declaredUnlocks, explicitDiscoveredActs: discovered);
            }
            finally { MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = previousTestMode; }
            // Repeated host runs retain user:// route boundaries. Choose a new,
            // logged fixture identity rather than deleting or rewriting an old
            // frozen run, whose unlock facts may intentionally differ.
            string? routeSeed = null;
            ulong firstOrdinal = (ulong)DateTime.UtcNow.Ticks;
            for (ulong offset = 0; offset < 256; offset++)
            {
                string candidate = RolltheSpire2.Search.FamilyExecution.VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, firstOrdinal + offset);
                var offers = RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(candidate, routeAuthority, Beta111Profile.Instance, true);
                if (!offers.RelicKeys.Contains(BaseGameModelKeys.Relics.GoldenPearl)) continue;
                string key = candidate + "/" + request!.Character.CharacterKey.Serialized + "/" + request.Ascension;
                string stored = Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "state", "routes",
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))) + ".json");
                if (File.Exists(stored)) continue;
                routeSeed = candidate; break;
            }
            if (routeSeed is null) throw new Exception("No fresh Golden Pearl route fixture within 256 identity probes");
            try
            {
                MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
                routeAuthority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(Beta111Profile.Instance,
                    routeSeed, request!.Character, request.Ascension, "0.111.0",
                    predictionGameMode: RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer,
                    predictionGameModeAuthority: RolltheSpire2.Core.Authority.PredictionGameModeAuthority.ExplicitRequest,
                    explicitUnlockState: declaredUnlocks, explicitDiscoveredActs: discovered);
            }
            finally { MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = previousTestMode; }
            GD.Print("PREDICTOR_RUNTIME_UI_FIXTURE seed=" + routeSeed + ";freshStoredBoundary=true;opening=GOLDEN_PEARL");
            if (!SeedPredictionRequest.TryCreate(routeSeed, request!.Character, request.Ascension, 1, 0, routeAuthority,
                context.EvaluationAssumptions.AncientEligibilityAssumptions, SeedPredictionDomainSelection.All,
                SeedPredictionInputLimits.MaximumRelicSequencePreviewCount, false, out var routeRequest, out issue))
                throw new Exception("Declared route request: " + issue);
            var routeDocument = RuntimeProfileRegistry.Predict(detection, routeRequest!);
            foreach (string language in new[] { "zh", "en" })
            {
                MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language = language == "zh" ? "zhs" : "eng";
                MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
                var names = asm.GetType("RolltheSpire2.Infrastructure.ContentNames.RuntimeGameContentNameResolver")!.GetMethod("Create")!.Invoke(null, [language])!;
                Call(page, "ApplyLocalization", JsonUiTextProvider.CreatePredictorUi13(language), names);
                Call(page, "ShowDocument", routeRequest, routeDocument, routeUnlocks);
                page.GetType().GetField("_routeSubview", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, true);
                Call(page, "RenderWorkbench");
                for (int frame = 0; frame < 10; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                var routeRun = Field(page, "_routeRuntime") ?? throw new Exception("Route Runtime did not initialize from declared prediction facts");
                var routeContext = routeRun.GetType().GetProperty("Context", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(routeRun)!;
                var epochs = ((System.Collections.IEnumerable)Property(routeContext, "RevealedEpochs")).Cast<string>();
                var encounters = ((System.Collections.IEnumerable)Property(routeContext, "SeenEncounters")).Cast<ModelKey>();
                if (!epochs.SequenceEqual(routeUnlocks.UnlockedEpochs) ||
                    !encounters.SequenceEqual(routeUnlocks.EncountersSeen.Select(k => new ModelKey(k.Category, k.Entry))) ||
                    (int)Property(routeContext, "NumberOfRuns") != routeUnlocks.NumberOfRuns ||
                    (string)Property(routeContext, "Seed") != routeSeed)
                    throw new Exception("Route UI did not retain the fresh declared prediction/unlock context");
                var map = Call(routeRun, "RouteMap", 1, default(CancellationToken))!;
                var routes = New("Core.Prediction.Maps.MapRouteSet", map, null, false);
                var path = (Array)Call(routes, "At", System.Numerics.BigInteger.Zero)!;
                var edits = (System.Collections.IDictionary)Field(page, "_routeEdits");
                var create = typeof(System.Collections.Immutable.ImmutableArray).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsArray);
                edits[1] = create.MakeGenericMethod(path.GetType().GetElementType()!).Invoke(null, [path]);
                Call(page, "RenderWorkbench");
                IEnumerable<Button> Buttons(Node n) => n.GetChildren().SelectMany(c => (c is Button b ? new[] { b } : Array.Empty<Button>()).Concat(Buttons(c)));
                var text = JsonUiTextProvider.CreatePredictorUi13(language);
                PredictorRun CurrentRun() => (PredictorRun)Field(page, "_routeRuntime");
                IEnumerable<Node> Descendants(Node n) => n.GetChildren().SelectMany(c => new[] { c }.Concat(Descendants(c)));
                if (page.FindChild("RouteResourceBar", true, false) is not Control ||
                    page.FindChild("RouteRelicBar", true, false) is not Control)
                    throw new Exception("Runtime resource/relic bars missing");
                var routeMap = (Control)page.FindChild("RuntimeRouteMap", true, false)!;
                if (routeMap == null) throw new Exception("Runtime map missing");
                var deckButton = Buttons(page).Single(b => b.Text.StartsWith(text.Get("predictor.runtime.deck").Split('{')[0]));
                deckButton.EmitSignal(Button.SignalName.Pressed);
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                var deckDialog = page.GetChildren().OfType<AcceptDialog>().Single(d => d.Visible);
                deckDialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                var commit = Buttons(page).Single(b => b.Text == text.Get("predictor.runtime.commit_route"));
                commit.EmitSignal(Button.SignalName.Pressed);
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Call(routeRun, "Project") == null) throw new Exception("Route projection missing");
                object Working() => routeRun.GetType().GetProperty("Working", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(routeRun)!;
                if (!(bool)Property(Working(), "OpeningCompleted"))
                {
                    int goldBeforeOpening = (int)Property(Working(), "GoldBudget");
                    await PressRoute("advance");
                    if (Property(Call(routeRun, "Project")!, "Phase").ToString() != "OpeningChoice")
                        throw new Exception("Fresh route did not enter its real Opening choice");
                    string pearl = ((RolltheSpire2.Presentation.ContentNames.IGameContentNameResolver)names)
                        .Resolve(BaseGameModelKeys.Relics.GoldenPearl, RolltheSpire2.Presentation.ContentNames.GameContentKind.Relic);
                    Buttons((Node)Field(page, "_wbCenter")).Single(b => b.Text == pearl).EmitSignal(Button.SignalName.Pressed);
                    for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (Field(page, "_routeMessage").ToString()!.Length != 0 ||
                        !(bool)Property(Working(), "OpeningCompleted") ||
                        (int)Property(Working(), "GoldBudget") != goldBeforeOpening + 150)
                        throw new Exception("Fresh route Opening choice did not settle Golden Pearl before combat");
                }
                // Exercise the product settings callback, including its saved
                // preference, while the current committed state stays untouched.
                var defaultsBoundary = CurrentRun().Working;
                Buttons(page).Single(b => b.Text == text.Get("predictor.runtime.combat_defaults_settings"))
                    .EmitSignal(Button.SignalName.Pressed);
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                var defaultsDialog = page.GetChildren().OfType<AcceptDialog>().Single(d => d.Visible && !d.IsQueuedForDeletion());
                Descendants(defaultsDialog).OfType<Label>().Single(b => b.Text == text.Get("predictor.runtime.lava_active"))
                    .GetParent().GetChildren().OfType<CheckButton>().Single().ButtonPressed = false;
                Buttons(defaultsDialog).Single(b => b.Text == text.Get("predictor.runtime.save_combat_defaults"))
                    .EmitSignal(Button.SignalName.Pressed);
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Field(page, "_routeMessage").ToString()!.Length != 0 ||
                    !ReferenceEquals(defaultsBoundary, CurrentRun().Working) ||
                    CurrentRun().CombatDefaults.RelicEffectEnabled(new("RELIC", "LAVA_LAMP")) ||
                    page.GetChildren().OfType<AcceptDialog>().Any(d => !d.IsQueuedForDeletion()))
                    throw new Exception("Combat defaults settings did not save independently of current state or close its dialog");
                int goldBeforeBattle = CurrentRun().Working.GoldBudget;
                var deckBeforeBattle = CurrentRun().Working.Deck;
                var advance = Buttons(page).Single(b => b.Text == text.Get("predictor.runtime.advance"));
                if (advance.Disabled) throw new Exception("Committed route cannot advance");
                advance.EmitSignal(Button.SignalName.Pressed);
                for (int frame = 0; frame < 10; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Field(page, "_routeMessage").ToString()!.Length != 0) throw new Exception("Route UI advance failed");
                var live = Field(page, "_routeRuntime"); page.Hide(); page.Show();
                if (!ReferenceEquals(live, Field(page, "_routeRuntime"))) throw new Exception("Hide reset active transaction");
                if (page.Size.X > 1536 || page.Size.Y > 772) throw new Exception("Route UI overflow: " + page.Size);
                async Task PressRoute(string key)
                {
                    Buttons(page).Single(b => b.Text == text.Get("predictor.runtime." + key)).EmitSignal(Button.SignalName.Pressed);
                    for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (Field(page, "_routeMessage").ToString()!.Length != 0) throw new Exception("Route input failed: " + key);
                    routeRun = Field(page, "_routeRuntime");
                }
                if (Call(Field(page, "_routeRuntime"), "Project")!.GetType().GetProperty("Phase")!.GetValue(Call(Field(page, "_routeRuntime"), "Project"))!.ToString() != "Rewards")
                    throw new Exception("Ordinary battle did not automatically show rewards");
                if (Buttons(page).Any(b => b.TooltipText == text.Get("predictor.runtime.no_additional_inventory") || b.Text == text.Get("predictor.runtime.review_combat")))
                    throw new Exception("Redundant battle confirmation visible");
                if (!CurrentRun().Rewards.Any(r => r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open) ||
                    CurrentRun().Rewards.Any(r => r.Kind != PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open) ||
                    !CurrentRun().Working.Deck.SequenceEqual(deckBeforeBattle) ||
                    !Buttons((Node)Field(page, "_wbCenter")).Any(b => b.Text == text.Get("predictor.runtime.reward.Card")))
                    throw new Exception("Automatic pickup did not retain a visible pending card choice after independent rewards");
                string Offers() => System.Text.Json.JsonSerializer.Serialize(CurrentRun().Rewards.Select(r =>
                    new { r.Id, r.Kind, r.Key, r.Cards, r.GoldAmount }));
                string offersBeforeReorder = Offers();
                await PressRoute("pickup_reorder");
                if (CurrentRun().AutomaticRewardPickup || Offers() != offersBeforeReorder ||
                    CurrentRun().Rewards.Any(r => r.Status != PredictorRewardStatus.Open) ||
                    CurrentRun().Working.GoldBudget != goldBeforeBattle)
                    throw new Exception("Manual pickup ordering did not restore the same offers and pre-pickup budget");
                var pickupToggle = Buttons(page).OfType<CheckButton>().Single(b => b.Text == text.Get("predictor.runtime.pickup_defaults"));
                if (pickupToggle.ButtonPressed) throw new Exception("Manual ordering did not expose the disabled automatic pickup control");
                pickupToggle.ButtonPressed = true;
                for (int frame = 0; frame < 6; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Field(page, "_routeMessage").ToString()!.Length != 0 || !CurrentRun().AutomaticRewardPickup ||
                    Offers() != offersBeforeReorder || !CurrentRun().Rewards.Any(r => r.Kind == PredictorRewardKind.Gold && r.Status == PredictorRewardStatus.Taken) ||
                    !CurrentRun().Rewards.Any(r => r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open))
                    throw new Exception("Re-enabling automatic pickup changed the generated offers or consumed the pending card choice");
                var activeBeforeResume = CurrentRun();
                string workingBeforeResume = System.Text.Json.JsonSerializer.Serialize(activeBeforeResume.Working);
                string rewardsBeforeResume = System.Text.Json.JsonSerializer.Serialize(activeBeforeResume.Rewards);
                await PressRoute("resume_boundary");
                if (ReferenceEquals(activeBeforeResume, CurrentRun()) || CurrentRun().Phase != PredictorPhase.Rewards ||
                    System.Text.Json.JsonSerializer.Serialize(CurrentRun().Working) != workingBeforeResume ||
                    System.Text.Json.JsonSerializer.Serialize(CurrentRun().Rewards) != rewardsBeforeResume ||
                    CurrentRun().CombatDefaults.RelicEffectEnabled(new("RELIC", "LAVA_LAMP")))
                    throw new Exception("Resume lost unfinished rewards, pickup effects, or combat preferences");
                await PressRoute("adjust_combat");
                await PressRoute("apply_combat");
                await PressRoute("leave_rewards");
                await PressRoute("resume_boundary");
                // Fresh initialization adds an Opening history entry as well
                // as this battle. Target the completed battle's displayed floor,
                // rather than assuming the history has only one rewind control.
                var resumed = (RolltheSpire2.Core.PredictorRuntime.PredictorRun)Field(page, "_routeRuntime");
                int visitsBeforeRewind = resumed.Working.Visits.Length;
                int combatsBeforeRewind = resumed.Working.CombatCount;
                int checkpointsBeforeRewind = resumed.Checkpoints.Length;
                var completedBattle = resumed.Working.Visits.Last();
                if (completedBattle.Room != RolltheSpire2.Core.PredictorRuntime.PredictorRoomKind.Monster)
                    throw new Exception("Rewind fixture did not finish its ordinary battle");
                Buttons((Node)Field(page, "_wbCenter")).Single(b => b.IsVisibleInTree() && !b.IsQueuedForDeletion() &&
                    b.TooltipText == text.Get("predictor.runtime.rewind") && b.Text == completedBattle.TotalFloor.ToString())
                    .EmitSignal(Button.SignalName.Pressed);
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Field(page, "_routeMessage").ToString()!.Length != 0 ||
                    resumed.Checkpoints.Length != checkpointsBeforeRewind - 1 ||
                    resumed.Working.Visits.Length != visitsBeforeRewind - 1 ||
                    resumed.Working.CombatCount != combatsBeforeRewind - 1 || !resumed.Working.OpeningCompleted)
                    throw new Exception("Route UI rewind did not restore the battle entry and retain completed Opening");
                // Add one conditional-healing dependency to a detached fixture
                // boundary. Enter the real next node through its UI; missing HP
                // must leave independent card/gold preview visible and unchanged.
                var previewBoundary = PredictorSettlementEffects.ObtainRelic(resumed.Working, new("RELIC", "MEAT_ON_THE_BONE"));
                var previewRun = new PredictorRun(resumed.Context, previewBoundary);
                previewRun.RestoreRoutes(resumed.Routes);
                page.GetType().GetField("_routeRuntime", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, previewRun);
                Call(page, "RenderWorkbench");
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                await PressRoute("advance");
                var preview = previewRun.PreviewCurrentCombat() ?? throw new Exception("Missing-health combat preview absent");
                var previewBlock = page.FindChild("RouteCombatPreview", true, false) as Control;
                string[] previewLabels = previewBlock == null ? [] : Descendants(previewBlock).OfType<Label>().Select(l => l.Text).ToArray();
                var knownGold = preview.Rewards.FirstOrDefault(r => r.Kind == PredictorRewardKind.Gold && r.Value != null)?.Value;
                if (previewRun.Phase != PredictorPhase.CombatFacts || previewBlock == null || knownGold == null ||
                    !preview.PendingCauses.Any(p => p.Code == "VictoryHealth") ||
                    !preview.Rewards.Any(r => r.Kind == PredictorRewardKind.Card && r.Value != null) ||
                    !previewLabels.Contains(text.Get("predictor.runtime.gold") + " · " + knownGold.GoldAmount) ||
                    !previewLabels.Contains(text.Get("predictor.runtime.combat_pending.VictoryHealth")))
                    throw new Exception("Missing victory HP hid independent rewards or omitted its localized pending reason");
                string previewState = System.Text.Json.JsonSerializer.Serialize(previewRun.Working);
                Call(page, "RenderWorkbench");
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (System.Text.Json.JsonSerializer.Serialize(previewRun.Working) != previewState)
                    throw new Exception("Rendering an unresolved combat preview consumed working state");

                // Reuse this native context/catalog with a finite route whose
                // next decisions are combat -> fire -> combat. UI planning must
                // never submit the hypothetical choices to its actual run.
                var forecastTypes = new[] { RolltheSpire2.Core.Prediction.Maps.MapPointType.Ancient,
                    RolltheSpire2.Core.Prediction.Maps.MapPointType.Monster,
                    RolltheSpire2.Core.Prediction.Maps.MapPointType.RestSite,
                    RolltheSpire2.Core.Prediction.Maps.MapPointType.Monster,
                    RolltheSpire2.Core.Prediction.Maps.MapPointType.Boss };
                var forecastPoints = forecastTypes.Select((type, row) => new RolltheSpire2.Core.Prediction.Maps.MapPredictionPoint(
                    new(3, row), type, true, row == forecastTypes.Length - 1 ? [] :
                        new[] { new RolltheSpire2.Core.Prediction.Maps.MapPosition(3, row + 1) })).ToArray();
                var forecastMap = resumed.RouteMap(1) with { Points = forecastPoints };
                var forecastState = resumed.Working with
                {
                    OpeningCompleted = true, Relics = [], Position = new(1, 1, "route-ui-forecast:entry"),
                    Maps = [new(1, forecastMap, [forecastPoints[0].Position])],
                    Visits = [new(1, forecastPoints[0].Position, forecastTypes[0], PredictorRoomKind.Opening, null, 1)],
                    Acts = resumed.Working.Acts.SetItem(0, resumed.Working.Acts[0] with
                    { NormalEncounters = [new("ENCOUNTER", "FUZZY_WURM_CRAWLER_WEAK")], NormalCursor = 0 })
                };
                forecastState = PredictorSettlementEffects.ObtainRelic(forecastState, new("RELIC", "MEAT_ON_THE_BONE"));
                forecastState = PredictorSettlementEffects.ObtainRelic(forecastState, new("RELIC", "WHITE_BEAST_STATUE"));
                var forecastRun = new PredictorRun(resumed.Context, forecastState);
                forecastRun.SetRoute(1, forecastPoints.Select(p => p.Position).ToImmutableArray());
                forecastRun.AdvanceRoute();
                page.GetType().GetField("_routeRuntime", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, forecastRun);
                page.GetType().GetField("_routeEditingCombat", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, false);
                page.GetType().GetField("_routeShowContinuation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, false);
                page.GetType().GetField("_routeContinuationLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, 3);
                Call(page, "RenderWorkbench");
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                string forecastWorking = System.Text.Json.JsonSerializer.Serialize(forecastRun.Working);
                string forecastCheckpoints = System.Text.Json.JsonSerializer.Serialize(forecastRun.Checkpoints);
                var forecastRequest = forecastRun.Request;
                var forecastPhase = forecastRun.Phase;
                void ForecastDidNotExecute()
                {
                    if (System.Text.Json.JsonSerializer.Serialize(CurrentRun().Working) != forecastWorking ||
                        System.Text.Json.JsonSerializer.Serialize(CurrentRun().Checkpoints) != forecastCheckpoints ||
                        CurrentRun().Phase != forecastPhase)
                        throw new Exception("Route forecast or plan editing executed a hypothetical choice on the actual run");
                }
                Control ForecastPanel() => page.FindChild("RouteContinuation", true, false) as Control ??
                    throw new Exception("Route forecast button did not open the continuation panel");
                string[] ForecastLabels() => Descendants(ForecastPanel()).OfType<Label>().Select(l => l.Text).ToArray();
                void ForecastTextIsLocalized()
                {
                    if (ForecastLabels().Any(t => t.Contains("predictor.runtime.", StringComparison.Ordinal)) ||
                        Buttons(ForecastPanel()).Any(b => b.Text.Contains("predictor.runtime.", StringComparison.Ordinal) || b.TooltipText.Length != 0))
                        throw new Exception("Route forecast exposed a localization key or an unstyled button tooltip");
                }
                var showForecast = Buttons(page).Single(b => b.Text == text.Get("predictor.runtime.plan.show"));
                if (showForecast.TooltipText.Length != 0) throw new Exception("Route forecast toggle exposed an unstyled tooltip");
                await PressRoute("plan.show");
                if (!ForecastLabels().Contains(text.Get("predictor.runtime.plan.premise")) ||
                    !ForecastLabels().Any(t => t.Contains(text.Get("predictor.runtime.plan.pending.RestChoice"), StringComparison.Ordinal)) ||
                    !ForecastLabels().Any(t => t.Contains(text.Get("predictor.runtime.combat_pending.VictoryHealth"), StringComparison.Ordinal)))
                    throw new Exception("Route forecast omitted its localized premise, manual fire boundary, or originating health cause");
                ForecastDidNotExecute(); ForecastTextIsLocalized();
                await PressRoute("rest.Smith");
                var target = forecastRun.Working.Deck.Select(forecastRun.Working.Card).First(c => c.UpgradeLevel < c.MaxUpgradeLevel);
                string targetText = (string)Call(page, "RCard", target)!;
                Buttons(ForecastPanel()).Single(b => b.Text == targetText).EmitSignal(Button.SignalName.Pressed);
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Field(page, "_routeMessage").ToString()!.Length != 0) throw new Exception("Forecast smith target click failed");
                await PressRoute("plan.leave_rest");
                var authoredRest = forecastRun.PlannedNodes.Single(p => p.RestActions.Length > 0);
                var forecast = forecastRun.PreviewRouteContinuation(forecastRun.PlannedNodes, 3);
                if (authoredRest.RestActions is not [{ Action: PredictorRestAction.Smith, SmithCard: not null }] ||
                    authoredRest.RestActions[0].SmithCard!.InstanceId != target.Id || !authoredRest.LeaveRest ||
                    forecast.Nodes.Length != 3 || !forecast.Nodes[1].Advanced || forecast.Nodes[2].Room != PredictorRoomKind.Monster ||
                    forecastRun.Request != forecastRequest)
                    throw new Exception("Forecast smith and leave clicks did not preserve intent and reveal the following combat");
                ForecastDidNotExecute(); ForecastTextIsLocalized();
                await PressRoute("resume_boundary");
                forecastRun = CurrentRun();
                var restoredRest = forecastRun.PlannedNodes.Single(p => p.NodeId == authoredRest.NodeId);
                if (!restoredRest.LeaveRest || restoredRest.RestActions[0].SmithCard != authoredRest.RestActions[0].SmithCard)
                    throw new Exception("Resume lost the saved future smith/leave plan");
                ForecastDidNotExecute();
                // Preserve a previously authored target whose identity no longer
                // binds after an earlier change; display its pending cause.
                var invalidatedTarget = restoredRest.RestActions[0].SmithCard! with { InstanceId = target.Id + 1_000_000 };
                var invalidatedPlan = restoredRest with { RestActions = [restoredRest.RestActions[0] with { SmithCard = invalidatedTarget }] };
                Call(page, "EditRoutePlan", invalidatedPlan);
                for (int frame = 0; frame < 4; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Field(page, "_routeMessage").ToString()!.Length != 0 ||
                    !ForecastLabels().Any(t => t.Contains(text.Get("predictor.runtime.plan.pending.PlannedRestTargetPending"), StringComparison.Ordinal)) ||
                    !forecastRun.PlannedNodes.Single(p => p.NodeId == authoredRest.NodeId).LeaveRest ||
                    forecastRun.PlannedNodes.Single(p => p.NodeId == authoredRest.NodeId).RestActions[0].SmithCard != invalidatedTarget)
                    throw new Exception("Invalidated forecast smith target was silently replaced or lost its localized pending cause");
                ForecastDidNotExecute(); ForecastTextIsLocalized();
                await PressRoute("resume_boundary");
                if (CurrentRun().PlannedNodes.Single(p => p.NodeId == authoredRest.NodeId).RestActions[0].SmithCard != invalidatedTarget)
                    throw new Exception("Resume discarded a currently unbound future target");
                ForecastDidNotExecute(); ForecastTextIsLocalized();
                GD.Print("PREDICTOR_RUNTIME_FORECAST_UI_PASS language=" + language +
                    ";combat-fire-combat;show-click;smith-target/leave-clicks;localized-cause;pure-render/edit;plan-resume;invalidated-target-retained;unstyledTooltips=0");
                page.GetType().GetField("_routeShowContinuation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, false);
                page.GetType().GetField("_routeRuntime", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, resumed);
                Call(page, "SaveRouteBoundary");
                routeRun = resumed;
                // Existing Predictor tool app has its own user://, not the game profile.
                // Restore only the completed boundary before exercising the other language.
                Call(Field(page, "_routeRuntime"), "RestartCurrentNode"); Call(page, "RenderWorkbench");
                if (DisplayServer.GetName() != "headless")
                {
                    await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "runtime-route-" + language + ".png"));
                }
            }
            if (page.GetChildren().OfType<AcceptDialog>().Any(d => !d.IsQueuedForDeletion()))
                throw new Exception("Route UI left a dialog attached after confirmation");
            canvas.QueueFree(); tooltip.QueueFree();
            // Main quits once this task completes. Let the scene deletion queue
            // and TreeExiting handlers run before native renderer shutdown.
            for (int frame = 0; frame < 3; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (GodotObject.IsInstanceValid(canvas) || GodotObject.IsInstanceValid(page) || GodotObject.IsInstanceValid(tooltip))
                throw new Exception("Route UI queued nodes did not leave the scene tree before cleanup completed");
            if (DisplayServer.GetName() != "headless")
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("PREDICTOR_RUNTIME_UI_CLEANUP_PASS queued nodes freed;dialogs=0");
            GD.Print("PREDICTOR_RUNTIME_UI_PASS declared prediction/unlock init independent of account; bilingual combat defaults; automatic pickup retains cards; manual reorder preserves offers; active draft resume; missing-health independent preview; combat edit/history rewind; hide retains transaction; bounded layout");
            return;
        }
        if (!string.IsNullOrWhiteSpace(layoutSeed))
        {
            MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language = "zhs";
            MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
            canvas.RemoveChild(page); page.QueueFree();
            canvas.Position = Vector2.Zero; canvas.Size = new Vector2(1600, 900);
            var layoutShell = new RolltheSpire2.Ui.Shell.WorkspaceShell(); canvas.AddChild(layoutShell);
            layoutShell.InitializeWithPersistence((RolltheSpire2.Bootstrap.ModRuntimeSnapshot)runtime, Path.Combine(output, "state"));
            Call(layoutShell, "CreatePredictor", new object?[] { null });
            page = (Control)Field(layoutShell, "_predictor");
            var selectTask = layoutShell.GetType().GetMethod("SelectTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
            selectTask.Invoke(layoutShell, [Enum.Parse(selectTask.GetParameters()[0].ParameterType, "Analysis"), null, null]);
            Call(page, "ApplyLocalization", JsonUiTextProvider.CreatePredictorUi13("zh"),
                asm.GetType("RolltheSpire2.Infrastructure.ContentNames.RuntimeGameContentNameResolver")!.GetMethod("Create")!.Invoke(null, ["zh"]));
            Call(page, "ShowDocument", request, document, null);
            var model = (SeedAnalysisViewModel)Field(page, "_viewModel");
            var bones = model.NeowChoices.First(c => c.RelicKey == BaseGameModelKeys.Relics.NeowsBones);
            Call(page, "ApplyOpeningSelection", bones.RelicKey, bones.OpeningRoutes.First().RouteId);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 15 && page.FindChild("Act1OverviewMap", true, false)?.GetChildCount() == 0)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            for (int frame = 0; frame < 50; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "reported-seed.png"));
            foreach (Control c in new[] { page, (Control)Field(page, "_workbenchRoot"), (Control)Field(page, "_wbCenter"), (Control)Field(page, "_wbRight"),
                (Control)page.FindChild("Act1OverviewMap", true, false), (Control)page.FindChild("PredictorMainScroll", true, false) })
                GD.Print($"PREDICTOR_LAYOUT name={c.Name};rect={c.GetGlobalRect()};minimum={c.GetCombinedMinimumSize()}");
            var mapSurface = (Control)page.FindChild("Act1OverviewMap", true, false);
            var taskCanvas = (Control)Field(layoutShell, "_content");
            var bounds = taskCanvas.GetGlobalRect();
            if (!bounds.Encloses(page.GetGlobalRect()) || !bounds.Encloses(mapSurface.GetGlobalRect()))
                throw new Exception("Predictor layout exceeds the task canvas");
            GD.Print($"PREDICTOR_LAYOUT_PASS seed={layoutSeed};mapWithinCanvas=true;reproduction=not-observed");
            canvas.QueueFree();
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            return;
        }
        // Target the reported missing-skin encounters independently of this seed's queue.
        foreach (string entry in new[] { "BOWLBUGS_WEAK", "BOWLBUGS_NORMAL", "SCROLLS_OF_BITING_WEAK", "SCROLLS_OF_BITING_NORMAL" })
        {
            var provider = Field(page, "_encounterPortraits");
            var portrait = await (Task<Texture2D?>)provider.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
                .Invoke(provider, [new ModelKey("ENCOUNTER", entry)])!;
            if (portrait is null) throw new Exception("Reported encounter portrait missing: " + entry);
            using var portraitImage = portrait.GetImage();
            portraitImage.SavePng(Path.Combine(output, "portrait-" + entry.ToLowerInvariant() + ".png"));
        }
        foreach (string language in new[] { "zh", "en" })
        {
            MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language = language == "zh" ? "zhs" : "eng";
            MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
            var names = asm.GetType("RolltheSpire2.Infrastructure.ContentNames.RuntimeGameContentNameResolver")!.GetMethod("Create")!.Invoke(null, [language])!;
            Call(page, "ApplyLocalization", JsonUiTextProvider.CreatePredictorUi13(language), names);
            Call(page, "ShowDocument", request, document, null);
            var tabs = (List<Button>)Field(page, "_wbTabs");
            if (tabs.Count != 4) throw new Exception("Expected four Predictor pages");
            if (Maps().Count != 1 || !((Control)page.FindChild("PredictorAuxiliaryRail", true, false)).Visible)
                throw new Exception("Opening map must be visible by default");
            tabs[0].EmitSignal(Button.SignalName.Pressed);
            ((Button)page.FindChild("OverviewTab", true, false)).EmitSignal(Button.SignalName.Pressed);
            var firstMapTask = Maps()[1];
            tabs[1].EmitSignal(Button.SignalName.Pressed);
            tabs[0].EmitSignal(Button.SignalName.Pressed);
            if (!ReferenceEquals(firstMapTask, Maps()[1])) throw new Exception("Navigation restarted in-flight map prediction");
            for (int index = 0; index < tabs.Count; index++)
            {
                GD.Print($"PREDICTOR_SURFACE_PAGE language={language};page={index};phase=begin");
                tabs[index].EmitSignal(Button.SignalName.Pressed);
                if (index < 3)
                {
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (wait.Elapsed.TotalSeconds < 15 && page.FindChild($"Act{index + 1}OverviewMap", true, false)?.GetChildCount() == 0)
                        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                for (int frame = 0; frame < 40; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"predictor-{language}-{index}.png"));
                var rail = (Control)page.FindChild("PredictorContextRail", true, false);
                if (Math.Abs(rail.Size.X - 208) > 1 ||
                    Math.Abs(((Control)page.FindChild("PredictorContextInset", true, false)).Size.X - 224) > 1 || page.Size.X > 1536 || page.Size.Y > 772)
                {
                    void Dump(Node n, int depth) { if (n is Control c && c.Visible) GD.Print(new string(' ', depth) + c.Name + " size=" + c.Size + " min=" + c.GetCombinedMinimumSize()); if(depth<4)foreach(var child in n.GetChildren())Dump(child,depth+1); }
                    Dump(page,0);
                    throw new Exception("Predictor columns overflow: " + page.Size + " rail=" + rail.Size);
                }
                var aux = (Control)page.FindChild("PredictorAuxiliaryRail", true, false);
                if (aux.Visible && aux.GetGlobalRect().End.X > 1569) throw new Exception("Auxiliary overflow");
                if (aux.Visible != (index < 3)) throw new Exception("Map rail visibility incorrect");
                if (index == 3)
                    foreach (var lane in ((SeedAnalysisViewModel)Field(page, "_viewModel")).RelicSequenceDomain.Items.Where(l => l.Kind != RolltheSpire2.Core.Relics.RelicSequenceKind.Shop))
                        if (lane.FullEntries.Count != lane.TotalCount || !lane.FullEntries.Take(lane.Entries.Count).Select(e => e.RelicDisplay.ModelKey).SequenceEqual(lane.Entries.Select(e => e.RelicDisplay.ModelKey)))
                            throw new Exception("Full relic display lost bag order or entries");
                if (index < 3)
                {
                    var map = (Control?)page.FindChild($"Act{index + 1}OverviewMap", true, false);
                    if (map is null || map.GetChildCount() == 0) throw new Exception("Map not populated for act " + (index + 1));
                    if ((bool)map.GetType().GetProperty("ReadOnly")!.GetValue(map)!) throw new Exception("Map drawing is disabled");
                    var maximums = (GridContainer)page.FindChild("MapMaximums", true, false);
                    var maximumButtons = maximums.GetChildren().OfType<Button>().ToArray();
                    if (maximums.Columns != 4 || maximumButtons.Length != 4 || maximumButtons.Any(b =>
                        b.FindChildren("*", "TextureRect", true, false).Cast<TextureRect>().Single().Texture is null ||
                        !int.TryParse(((Label)b.FindChild("MaximumValue", true, false)).Text, out _) || Math.Abs(b.Position.Y - maximumButtons[0].Position.Y) > 1))
                        throw new Exception("Map maximums must be one row of native icons and counts");
                    var characterPicker = (OptionButton)Field(Field(page, "_requestBar"), "_railCharacter");
                    if (characterPicker.GetItemIcon(characterPicker.Selected) is null) throw new Exception("Character portrait missing");
                    var mapTaskBeforeDraw = Maps()[index + 1];
                    var unknownType = Enum.Parse(asm.GetType("RolltheSpire2.Core.Prediction.Maps.MapPointType")!, "Unknown");
                    var unknownRoutes = New("Core.Prediction.Maps.MapRouteSet", Field(map, "_map"), unknownType, false);
                    var path = ((System.Collections.IEnumerable)Call(unknownRoutes, "At", System.Numerics.BigInteger.Zero)!).Cast<object>().ToArray();
                    Vector2 Position(object point) => map.GetGlobalTransformWithCanvas() * (Vector2)Call(map, "StrokePosition",
                        New("Core.Prediction.Maps.MapStrokePoint", Convert.ToDouble(point.GetType().GetProperty("Column")!.GetValue(point)),
                            Convert.ToDouble(point.GetType().GetProperty("Row")!.GetValue(point))))!;
                    void Mouse(InputEvent input) => host.GetViewport().PushInput(input);
                    var strokeTrace = new List<string>();
                    void TraceStroke() => strokeTrace.Add($"{Property(map, "IsDrawing")}:{((System.Collections.ICollection)Field(map, "_stroke")).Count}");
                    Mouse(new InputEventMouseMotion { Position = Position(path[0]) });
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Mouse(new InputEventMouseButton { Position = Position(path[0]), ButtonIndex = MouseButton.Right, Pressed = true, ButtonMask = MouseButtonMask.Right });
                    TraceStroke();
                    foreach (var point in path.Skip(1))
                    {
                        Mouse(new InputEventMouseMotion { Position = Position(point), ButtonMask = MouseButtonMask.Right });
                        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                        TraceStroke();
                    }
                    Mouse(new InputEventMouseButton { Position = Position(path[^1]), ButtonIndex = MouseButton.Right, Pressed = false });
                    int overlays = map.GetChildren().OfType<TextureRect>().Count(t => t.Name.ToString().StartsWith("ResolvedUnknown_"));
                    int unknownCount = (int)unknownRoutes.GetType().GetProperty("Value")!.GetValue(unknownRoutes)!;
                    if (!Routes().Contains(index + 1) || unknownCount == 0 || overlays != unknownCount || !ReferenceEquals(mapTaskBeforeDraw, Maps()[index + 1]))
                    {
                        host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "failed-map-stroke.png"));
                        throw new Exception($"Right-drag failed: language={language}; act={index + 1}; route={Routes().Contains(index + 1)}; " +
                            $"overlays={overlays}/{unknownCount}; sameMap={ReferenceEquals(mapTaskBeforeDraw, Maps()[index + 1])}; " +
                            $"mapRect={map.GetGlobalRect()}; stroke={string.Join(',', strokeTrace)}");
                    }
                    if (Metrics().Contains(index + 1) || ((Control)page.FindChild("MapRouteBrowser", true, false)).Visible)
                        throw new Exception("Manual drawing retained a maximum filter");
                    foreach (string metricName in new[] { "RestSite", "Unknown" })
                    {
                        var metric = Enum.Parse(unknownType.GetType(), metricName);
                        var candidates = New("Core.Prediction.Maps.MapRouteSet", Field(map, "_map"), metric, false);
                        var count = (System.Numerics.BigInteger)Property(candidates, "Count");
                        ((Button)page.FindChild("MapMaximum" + metricName, true, false)).EmitSignal(Button.SignalName.Pressed);
                        var previous = (Button)page.FindChild("PreviousMapRoute", true, false);
                        var next = (Button)page.FindChild("NextMapRoute", true, false);
                        var label = (Label)page.FindChild("MapRouteCount", true, false);
                        System.Numerics.BigInteger Rank() => (System.Numerics.BigInteger)Call(candidates, "Rank", Property(Routes()[index + 1]!, "Coordinates"))!;
                        void CheckSelection(System.Numerics.BigInteger expectedRank)
                        {
                            var coordinates = ((System.Collections.IEnumerable)Property(Routes()[index + 1]!, "Coordinates")).Cast<object>().ToHashSet();
                            var expectedRooms = ((System.Collections.IEnumerable)Property(Field(map, "_map"), "Points")).Cast<object>()
                                .Where(p => Property(p, "Type").Equals(unknownType) && coordinates.Contains(Property(p, "Position")))
                                .Select(p => Property(p, "Position")).ToHashSet();
                            var actualRooms = ((System.Collections.IDictionary)Field(map, "_resolvedRooms")).Keys.Cast<object>().ToHashSet();
                            if (Rank() != expectedRank || !Equals(Metrics()[index + 1], metric) ||
                                label.Text != JsonUiTextProvider.CreatePredictorUi13(language).Format("predictor.design.map_route_count", expectedRank + 1, count) ||
                                !actualRooms.SetEquals(expectedRooms) || !ReferenceEquals(mapTaskBeforeDraw, Maps()[index + 1]))
                                throw new Exception("Maximum route browsing lost its rank, room overlays or cached map");
                        }
                        var firstRank = Rank();
                        CheckSelection(firstRank);
                        if (!((Control)page.FindChild("MapRouteBrowser", true, false)).Visible || previous.Disabled != (count == 1) || next.Disabled != (count == 1))
                            throw new Exception("Route browser availability does not match tied route count");
                        if (count > 1)
                        {
                            var firstRoute = Routes()[index + 1];
                            next.EmitSignal(Button.SignalName.Pressed);
                            CheckSelection((firstRank + 1) % count);
                            if (ReferenceEquals(firstRoute, Routes()[index + 1])) throw new Exception("Next retained the same route");
                            previous.EmitSignal(Button.SignalName.Pressed);
                            CheckSelection(firstRank);
                            tiedRouteChecks++;
                        }
                    }
                    var combat = (Control)page.FindChild("CombatDetails", true, false);
                    if (index > 0 && !combat.Visible) throw new Exception("Later Act combat hidden");
                    var encounterArt = combat.FindChildren("Encounter*", "Button", true, false).OfType<Button>()
                        .SelectMany(b => b.GetChildren().OfType<TextureRect>()).ToArray();
                    if (encounterArt.Length == 0 || encounterArt.Any(t => t.Texture is not ImageTexture))
                        throw new Exception("Native encounter portraits did not populate");
                    var eventStrip = (Container)page.FindChild("EventQueue", true, false);
                    var eventTiles = eventStrip.GetChildren().OfType<Button>().ToArray();
                    if (((GridContainer)eventStrip).Columns != 8 || eventTiles.Any(t => Math.Abs(t.Position.Y - eventTiles[0].Position.Y) > 1))
                        throw new Exception("Default events must share one row");
                    var expectedEvents = ((SeedAnalysisViewModel)Field(page, "_viewModel")).EventPoolSequenceDomain.Items.Single(a => a.Act == index + 1).Entries.OrderBy(e => e.Ordinal).ToArray();
                    if (eventTiles.Length != Math.Min(8, expectedEvents.Length)) throw new Exception("Event preview must contain eight candidates");
                    for (int e = 0; e < eventTiles.Length; e++)
                    {
                        eventTiles[e].EmitSignal(Control.SignalName.MouseEntered);
                        if (((Label)Field(tooltip, "_title")).Text != expectedEvents[e].EventDisplay.DisplayName)
                            throw new Exception("Event official tooltip lost");
                        eventTiles[e].EmitSignal(Control.SignalName.MouseExited);
                    }
                    var mapTask = Maps()[index + 1];
                    var projection = Call(page, "BuildEventResultProjection");
                    var eliteSection = (Control)page.FindChild("EliteQueueSection", true, false);
                    var normalSection = (Control)page.FindChild("NormalQueueSection", true, false);
                    var bossIdentities = (Control)page.FindChild("BossIdentities", true, false);
                    if (eventStrip.GetGlobalRect().Position.Y >= combat.GetGlobalRect().Position.Y ||
                        !eliteSection.IsVisibleInTree() || !normalSection.IsVisibleInTree())
                        throw new Exception("Overview must place events before the visible combat queues");
                    var mainScroll = (ScrollContainer)page.FindChild("PredictorMainScroll", true, false);
                    if (index > 0 && mainScroll.GetVScrollBar().MaxValue > mainScroll.GetVScrollBar().Page + 1)
                        throw new Exception("Default later-Act overview should fit without vertical scrolling");
                    eventTiles[0].EmitSignal(Button.SignalName.Pressed);
                    if (!(bool)((Control)page.FindChild("EventDetails", true, false)).Visible) throw new Exception("Event details did not open");
                    if (eliteSection.Visible || normalSection.Visible || !bossIdentities.IsVisibleInTree())
                        throw new Exception("Event focus must hide encounter queues and retain Boss identities");
                    for (int frame = 0; frame < 6; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"predictor-{language}-{index}-event-focus.png"));
                    eventTiles[0].EmitSignal(Button.SignalName.Pressed);
                    if (((Control)page.FindChild("EventDetails", true, false)).Visible) throw new Exception("Event details did not close");
                    if (!eliteSection.IsVisibleInTree() || !normalSection.IsVisibleInTree())
                        throw new Exception("Closing event details must restore both encounter queues");
                    var expand = (Button)page.FindChild("EventQueueExpand", true, false);
                    if (expectedEvents.Length > 8)
                    {
                        expand.EmitSignal(Button.SignalName.Pressed);
                        if (eventStrip.GetChildCount() != expectedEvents.Length) throw new Exception("Expanded events missing candidates");
                        int resultEvent = Array.FindIndex(expectedEvents, entry =>
                            entry.EventDisplay.ModelKey.Entry == "FAKE_MERCHANT");
                        if (resultEvent >= 0)
                        {
                            eventStrip.GetChild<Button>(resultEvent).EmitSignal(Button.SignalName.Pressed);
                            for (int frame = 0; frame < 6; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                            var items = (GridContainer)page.FindChild("EventResultItems", true, false);
                            if (items.GetChildCount() != 6 || items.Columns != 3 || eliteSection.Visible || normalSection.Visible)
                                throw new Exception("Six-result event must use a compact grid with encounter queues hidden");
                            eventResultChecks++;
                            eventStrip.GetChild<Button>(resultEvent).EmitSignal(Button.SignalName.Pressed);
                        }
                        expand.EmitSignal(Button.SignalName.Pressed);
                        if (eventStrip.GetChildCount() != 8) throw new Exception("Collapsed events did not restore eight icons");
                    }
                    tabs[index].EmitSignal(Button.SignalName.Pressed);
                    page.Hide(); page.Show();
                    if (!ReferenceEquals(map, page.FindChild($"Act{index + 1}OverviewMap", true, false)) ||
                        !ReferenceEquals(mapTask, Maps()[index + 1]) || !ReferenceEquals(combat, page.FindChild("CombatDetails", true, false)) ||
                        !ReferenceEquals(projection, Call(page, "BuildEventResultProjection")))
                        throw new Exception("Event clicks, active tabs or visibility changes rebuilt prediction work");
                    foreach (var icon in map.GetChildren().OfType<TextureRect>())
                        if (icon.Position.Y < -1 || icon.Position.Y + icon.Size.Y > map.Size.Y + 1) throw new Exception("Map clipped vertically");
                }
                if (index == 1) await RunAncientPremiseChecksAsync((RolltheSpire2.Ui.Pages.Analysis.AnalysisPage)page);
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"predictor-{language}-{index}.png"));
                if (index == 3)
                {
                    ((Button)page.FindChild("FullRelicQueueTab", true, false)).EmitSignal(Button.SignalName.Pressed);
                    for (int frame = 0; frame < 5; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"predictor-{language}-relic-names.png"));
                    ((Button)page.FindChild("RelicQueueDisplayMode", true, false)).EmitSignal(Button.SignalName.Pressed);
                    for (int frame = 0; frame < 5; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"predictor-{language}-relic-icons.png"));
                    ((Button)page.FindChild("RelicQueueDisplayMode", true, false)).EmitSignal(Button.SignalName.Pressed);
                    ((Button)page.FindChild("ShopPreviewTab", true, false)).EmitSignal(Button.SignalName.Pressed);
                }
                checks++;
            }
            var savedRoute = Routes()[1];
            tabs[0].EmitSignal(Button.SignalName.Pressed);
            if (page.FindChild("Act1OverviewMap", true, false).GetChildren().OfType<TextureRect>().All(t => !t.Name.ToString().StartsWith("ResolvedUnknown_")))
                throw new Exception("Act navigation lost the drawn route");
            if (!ReferenceEquals(savedRoute, Routes()[1]) || !((Control)page.FindChild("MapRouteBrowser", true, false)).Visible || Metrics()[1]?.ToString() != "Unknown")
                throw new Exception("Act navigation lost the selected maximum route");
            ((Button)page.FindChild("ClearMapRoute", true, false)).EmitSignal(Button.SignalName.Pressed);
            if (Metrics().Contains(1) || ((Control)page.FindChild("MapRouteBrowser", true, false)).Visible)
                throw new Exception("Clearing the route retained its maximum filter");
            tabs[1].EmitSignal(Button.SignalName.Pressed);
            if (Routes().Contains(1) || !Routes().Contains(2) || page.FindChild("Act2OverviewMap", true, false).GetChildren().OfType<TextureRect>().Any(t => t.Name.ToString().StartsWith("ResolvedUnknown_")))
                throw new Exception("Clearing an earlier route retained stale later-Act room results");
            tabs[0].EmitSignal(Button.SignalName.Pressed);
            ((Button)page.FindChild("OpeningPredictionTab", true, false)).EmitSignal(Button.SignalName.Pressed);
            if (page.FindChild("EventQueue", true, false) is not null) throw new Exception("Opening view contains overview events");
            var vm = (SeedAnalysisViewModel)Field(page, "_viewModel");
            var merchantProjection = Call(page, "BuildMerchantColorlessProjection");
            var bones = vm.NeowChoices.First(c => c.RelicKey == BaseGameModelKeys.Relics.NeowsBones);
            foreach (var route in bones.OpeningRoutes)
            {
                Call(page, "ApplyOpeningSelection", bones.RelicKey, route.RouteId);
                for (int frame = 0; frame < 8; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (page.Size.X > 1536 || page.Size.Y > 772) throw new Exception("Bones opening overflow");
                if ((string)Field(page, "_preferredOpeningRouteId") != route.RouteId) throw new Exception("Opening route lost");
                if (!ReferenceEquals(vm, Field(page, "_viewModel")) || !ReferenceEquals(firstMapTask, Maps()[1]) ||
                    !ReferenceEquals(merchantProjection, Call(page, "BuildMerchantColorlessProjection")))
                    throw new Exception("Opening selection rebuilt presentation or projections");
                host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"predictor-{language}-bones-{route.RouteOrder}.png"));
            }
            // The reported Capsule opening has multiple card rewards per battle: exercise
            // the dense layout and distinguish conditional absence from unavailable data.
            if (!SeedPredictionRequest.TryCreate("MSF4M05B3PZQ", context.Authority.Character, 10, 1, 0, fixtureAuthority,
                context.EvaluationAssumptions.AncientEligibilityAssumptions, SeedPredictionDomainSelection.All, SeedPredictionInputLimits.MaximumRelicSequencePreviewCount,
                false, out var capsuleRequest, out issue)) throw new Exception(issue.ToString());
            Call(page, "ShowDocument", capsuleRequest, RuntimeProfileRegistry.Predict(detection, capsuleRequest!));
            var capsuleVm = (SeedAnalysisViewModel)Field(page, "_viewModel");
            var capsule = capsuleVm.NeowChoices.Single(c => c.RelicKey == BaseGameModelKeys.Relics.LargeCapsule);
            Call(page, "ApplyOpeningSelection", capsule.RelicKey, capsule.OpeningRoutes.First().RouteId);
            ((ScrollContainer)page.FindChild("PredictorMainScroll", true, false)).ScrollVertical = 0;
            var capsuleRoute = capsuleVm.NormalCombatRewardDomain.Items.Single(r => r.RouteGroupId == (string)Field(page, "_preferredRewardRouteGroupId"));
            if (capsuleRoute.Battles.Count != 3 || capsuleRoute.Battles.Any(b => b.CardRewards.Count != 2))
                throw new Exception("Capsule fixture must contain three battles with two reward groups each");
            var textProvider = JsonUiTextProvider.CreatePredictorUi13(language);
            foreach (var battle in capsuleRoute.Battles)
            {
                var outcome = page.FindChild($"Battle{battle.BattleOrdinal}Outcome", true, false);
                for (int group = 0; group < battle.CardRewards.Count; group++)
                {
                    var cards = (GridContainer)outcome.FindChild($"CardReward{group + 1}", true, false);
                    var expected = battle.CardRewards[group].Cards;
                    if (cards.GetChildCount() != expected.Count || !cards.GetChildren().OfType<Control>().Select(c => c.GetChild<Label>(1).Text)
                        .SequenceEqual(expected.Select(c => c.CardDisplay.DisplayName + c.DisplaySuffix)))
                        throw new Exception("Compact rewards lost card count, grouping, order or upgrade suffix");
                }
                if (!battle.Potion.Generated && (string)Call(page, "BattlePotionStatus", battle)! != textProvider.Get("predictor.design.potion_none"))
                    throw new Exception("Conditional no-drop shown as unknown");
            }
            var absent = capsuleRoute.Battles.First(b => !b.Potion.Generated);
            var unknown = absent with { Potion = absent.Potion with { Precision = PredictionPrecision.Unknown } };
            if ((string)Call(page, "BattlePotionStatus", unknown)! != textProvider.Get("predictor.design.potion_unknown") ||
                (string)Call(page, "BattlePotionStatus", absent with { ReasonCode = "UnsupportedRewardHook" })! != textProvider.Get("predictor.design.potion_unknown"))
                throw new Exception("Unavailable potion evidence was incorrectly presented as no drop");
            var rewards = (Control)page.FindChild("OpeningBattleRewards", true, false);
            var assumptionText = (Control)page.FindChild("RewardAssumptionsText", true, false);
            var assumptionToggle = (Button)page.FindChild("RewardAssumptionsToggle", true, false);
            if (assumptionText.Visible) throw new Exception("Reward premises should start collapsed");
            assumptionToggle.EmitSignal(Button.SignalName.Pressed);
            if (!assumptionText.Visible) throw new Exception("Reward premises did not open");
            assumptionToggle.EmitSignal(Button.SignalName.Pressed);
            if (assumptionText.Visible || !ReferenceEquals(rewards, page.FindChild("OpeningBattleRewards", true, false)) ||
                !ReferenceEquals(capsuleVm, Field(page, "_viewModel"))) throw new Exception("Premise toggle rebuilt prediction");
            for (int frame = 0; frame < 10; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (page.Size.X > 1536 || page.Size.Y > 772 || rewards.Size.Y > 350) throw new Exception("Compact Capsule reward layout overflow: " + rewards.Size);
            GD.Print($"PREDICTOR_COMPACT_REWARDS language={language};height={rewards.Size.Y};rewardGroups=6");
            host.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"predictor-{language}-capsule.png"));
            if (page.FindChild("PinOpening", true, false) is not null || page.FindChild("MapRouteDetails", true, false) is not null)
                throw new Exception("Retired Predictor actions remain exposed");
            if (((GridContainer)page.FindChild("OpeningBattleRewards", true, false)).Columns != 1)
                throw new Exception("Opening rewards must use the current selection only");
            var oldEvents = Call(page, "BuildEventResultProjection");
            if (!SeedPredictionRequest.TryCreate("K5ET15V6EN5C", context.Authority.Character, 10, 1, 0, fixtureAuthority,
                context.EvaluationAssumptions.AncientEligibilityAssumptions, SeedPredictionDomainSelection.All, SeedPredictionInputLimits.MaximumRelicSequencePreviewCount,
                false, out var replacement, out issue)) throw new Exception(issue.ToString());
            var replacementDocument = RuntimeProfileRegistry.Predict(detection, replacement!);
            Call(page, "ShowDocument", replacement, replacementDocument);
            if (Maps().Count != 1 || Routes().Count != 0 || Metrics().Count != 0 || ReferenceEquals(oldEvents, Call(page, "BuildEventResultProjection")) ||
                ReferenceEquals(merchantProjection, Call(page, "BuildMerchantColorlessProjection")))
                throw new Exception("Changed context reused stale projections");
            ((Button)page.FindChild("OverviewTab", true, false)).EmitSignal(Button.SignalName.Pressed);
            if (ReferenceEquals(firstMapTask, Maps()[1])) throw new Exception("Changed context reused an old map");
            var bar = Field(page, "_requestBar");
            // Prediction accepts nonempty custom seeds (including "!"). Empty
            // input exercises the actual invalid-edit path without a controller.
            ((LineEdit)Field(bar, "_seedInput")).Text = string.Empty;
            ((LineEdit)Field(bar, "_seedInput")).EmitSignal(LineEdit.SignalName.TextChanged, string.Empty);
            tabs[2].EmitSignal(Button.SignalName.Pressed);
            if ((bool)Field(page, "_wbHasResult")) throw new Exception("Invalid seed retained old results");
            if (Maps().Count != 0 || Routes().Count != 0 || Metrics().Count != 0) throw new Exception("Invalid seed retained cached maps or selected routes");
        }
        canvas.QueueFree(); tooltip.QueueFree();
        for (int frame = 0; frame < 3; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (eventResultChecks == 0) throw new Exception("Fixture did not exercise the six-result event layout");
        GD.Print($"PREDICTOR_PAGES_PASS pages={checks};languages=zh,en;eventFocus=true;defaultOverviewFits=true;eventResultGrids={eventResultChecks}");
        if (OS.GetCmdlineUserArgs().Contains("--predictor-pages-only")) return;
        var shellHost = new Control { Size = new Vector2(1600, 900) }; host.AddChild(shellHost);
        var shell = (Control)New("Ui.Shell.WorkspaceShell");
        Call(shell, "InitializeWithPersistence", runtime, Path.Combine(output, "integration-state", Guid.NewGuid().ToString("N")));
        shellHost.AddChild(shell);
        var select = shell.GetType().GetMethod("SelectTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Call(shell, "SelectTask", Enum.Parse(select.GetParameters()[0].ParameterType, "Analysis"));
        for (int frame = 0; frame < 6; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        var installedPage = (Control)Field(shell, "_predictor");
        if (!installedPage.Visible || (Control)installedPage.GetParent() != (Control)Field(shell, "_content")) throw new Exception("Shell analysis navigation not connected");
        if (!((Button)Field(installedPage, "_favoriteSeed")).Visible) throw new Exception("Main Predictor must expose its connected favorite action");
        await RunIntegrationChecks((RolltheSpire2.Ui.Shell.WorkspaceShell)shell, request!, document);
        GD.Print("UI_WORKSPACE_INTEGRATION_PASS legacyShellRemoved=true");
        shellHost.QueueFree();
        for (int frame = 0; frame < 3; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (tiedRouteChecks == 0) throw new Exception("Fixture did not exercise tied maximum routes");
        GD.Print($"PREDICTOR_SURFACE_PASS pages={checks};tiedRouteChecks={tiedRouteChecks};realDocument=true;interactiveMaps=3;drawnUnknownRooms=true;contextWidth=224;eventPreview=8;projectionReuse=true;contextInvalidation=true");
    }

}
