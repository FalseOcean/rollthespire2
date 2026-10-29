using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Environment = System.Environment;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Test;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    // Enter after loading the native PCK and before the ordinary party host
    // initializes ModManager/ModelDb. All state belongs to this isolated host.
    internal static async Task RunOverallLatencyAsync(string output, string pck)
    {
        Directory.CreateDirectory(output);
        string stage = "initialize-isolated-save";
        var receipts = new List<object>();
        var packages = new List<object>();
        var tailJobs = new List<(string Name, PredictorCrystalSnapshot Source, CrystalReachabilityTable Table, ImmutableArray<CrystalRewardOption> Targets)>();
        bool tailLanguage = Environment.GetEnvironmentVariable("RT2_CRYSTAL_OVERALL_TAIL_LANGUAGE") == "1";
        string seed = Environment.GetEnvironmentVariable("RT2_CRYSTAL_OVERALL_SEED") ?? "000000000000";
        int actIndex = int.Parse(Environment.GetEnvironmentVariable("RT2_CRYSTAL_OVERALL_ACT_INDEX") ?? "0");
        if (actIndex is < 0 or > 2) throw new ArgumentOutOfRangeException("RT2_CRYSTAL_OVERALL_ACT_INDEX", "Expected 0..2");
        int queryMs = OverallBudget("RT2_CRYSTAL_OVERALL_QUERY_MS", 2000);
        string choicesPath = Path.GetFullPath(Environment.GetEnvironmentVariable("RT2_CRYSTAL_OVERALL_CHOICES") ??
            Path.Combine(output, "natural-choices.json"));
        bool frozenChoices = File.Exists(choicesPath);
        var choices = frozenChoices ? ProductionSearchReplay.Read<Dictionary<string, ImmutableArray<CrystalRewardOption>>>(choicesPath) : [];
        void Save(string status, Exception? error = null) => ProductionSearchReplay.Write(
            Path.Combine(output, "overall-latency.json"), new { schema = 1, status, stage, seed, actIndex, ascension = 10, queryMs, tailLanguage,
                scheduling = "One wall budget; selection root exclusions then ProjectKnown; alternating Gold75ms/Candidates150ms as production UI; completed lanes stop",
                productMvid = typeof(PredictorCrystalSnapshot).Module.ModuleVersionId,
                runtimeVersion = Environment.Version.ToString(), framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                choicesPath, frozenChoices, packages, receipts,
                error = error?.ToString(),
                scope = "Actual Watcher package/native generated boards; fresh host/test run; no injected reward witnesses or solver-affecting relics; adaptive natural picker choices are recorded; cutoffs remain pending; no normal-play acquisition or full-game parity claim" });
        Save("running");
        try
        {
            TestMode.IsOn = true;
            var save = new SaveManager(new MockGodotFileIo("user://crystal-overall-memory"), true);
            SaveManager.MockInstanceForTesting(save);
            save.InitSettingsDataForTest(); save.InitPrefsDataForTest();
            stage = "load-real-watcher-package"; Save("running");
            string installed = Path.GetFullPath(Environment.GetEnvironmentVariable("RT2_CRYSTAL_WATCHER_PACKAGE") ??
                "D:/Game/Steam/steamapps/workshop/content/2868840/3747526116");
            string copied = Path.Combine(output, "packages", "Watcher");
            if (Directory.Exists(copied)) throw new IOException("Use a fresh output directory: " + copied);
            foreach (string file in Directory.EnumerateFiles(installed, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(installed, file), target = Path.Combine(copied, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                Check(hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))), "Watcher package copy hash");
                packages.Add(new { relative, source = file, copy = target, sha256 = hash });
            }
            string releasePath = Path.Combine(Path.GetDirectoryName(pck)!, "release_info.json");
            var release = JsonSerializer.Deserialize<ReleaseInfo>(File.ReadAllText(releasePath))!;
            var version = SemanticVersion.FromString(release.Version);
            var releaseManager = ReleaseInfoManager.Instance;
            typeof(ReleaseInfoManager).GetField("<ReleaseInfo>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(releaseManager, release);
            typeof(ReleaseInfoManager).GetField("<SemVer>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(releaseManager, version);
            TestMode.IsOn = false;
            await ModManager.Initialize(new OverallWatcherFileIo(copied), new ModSettings {
                PlayerAgreedToModLoading = true, ModList = [new() { Id = "Watcher", Source = ModSource.ModsDirectory }] }, version);
            var mod = ModManager.Mods.Single(m => m.manifest?.id == "Watcher");
            packages.Add(new { id = "Watcher", mod.manifest!.version, state = mod.state.ToString(),
                errors = mod.errors?.Select(e => e.ToString()).ToArray(), assemblies = mod.assemblies.Select(a => a.FullName).ToArray() });
            Check(mod.state == ModLoadState.Loaded && mod.errors is not { Count: > 0 } && mod.assemblies.Count > 0,
                "Actual Watcher package and initializer must load successfully");
            var watcherAssembly = mod.assemblies.Single(a => a.GetName().Name == "Watcher");
            var installedPatches = Harmony.GetAllPatchedMethods().SelectMany(method =>
            {
                var info = Harmony.GetPatchInfo(method)!;
                return info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                    .Where(patch => patch.PatchMethod.Module.Assembly == watcherAssembly)
                    .Select(patch => new { target = method.DeclaringType?.FullName + "." + method.Name,
                        owner = patch.owner, patch = patch.PatchMethod.DeclaringType?.FullName + "." + patch.PatchMethod.Name,
                        patchType = patch.PatchMethod.DeclaringType?.FullName });
            }).ToArray();
            var annotatedPatchTypes = watcherAssembly.GetTypes().Where(t => t.GetCustomAttributes<HarmonyPatch>(false).Any())
                .Select(t => t.FullName).ToArray();
            var unobservedAnnotatedTypes = annotatedPatchTypes.Except(installedPatches.Select(p => p.patchType)).ToArray();
            var characterGetter = typeof(ModelDb).GetProperty(nameof(ModelDb.AllCharacters))!.GetMethod!;
            var characterPatches = Harmony.GetPatchInfo(characterGetter);
            bool characterCatalogPatched = characterPatches != null && characterPatches.Prefixes.Concat(characterPatches.Postfixes)
                .Concat(characterPatches.Transpilers).Concat(characterPatches.Finalizers)
                .Any(p => p.PatchMethod.Module.Assembly == watcherAssembly);
            packages.Add(new { id = "watcher-installed-harmony-evidence", installedPatchMethods = installedPatches.Length,
                installedPatchTypes = installedPatches.Select(p => p.patchType).Distinct().Count(),
                annotatedPatchTypes = annotatedPatchTypes.Length, unobservedAnnotatedTypes, characterCatalogPatched, installedPatches,
                scope = "Direct Harmony registrations, not the mod's Loaded status or log text; unobserved annotated types are diagnostic, not a claim that every optional platform patch should run" });
            Save("running");
            Check(installedPatches.Length > 0 && characterCatalogPatched,
                "Watcher initializer must install real Harmony patches including its character catalog patch; runtime=" + Environment.Version);
            stage = "initialize-native-catalog"; Save("running");
            TestMode.IsOn = true;
            // Real OneTimeInitialization builds AssemblyInfo only after mod
            // initialization. An earlier snapshot leaves Loaded mods unmapped.
            AssemblyInfo.Init();
            var modelTypes = watcherAssembly.GetTypes().Where(t => typeof(AbstractModel).IsAssignableFrom(t) && !t.IsAbstract).ToArray();
            var unassociatedTypes = modelTypes.Where(t => !ReferenceEquals(AssemblyInfo.ModForType(t, out _), mod)).Select(t => t.FullName).ToArray();
            packages.Add(new { id = "watcher-assembly-map", modelTypes = modelTypes.Length, unassociatedTypes,
                initializedAfterModLoading = true, scope = "Native ContentSorter ownership/ID map; this isolated experiment has no multiplayer peers" });
            Check(unassociatedTypes.Length == 0, "Every native Watcher model is associated before serialization cache initialization");
            ModelDb.Init();
            MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init(); ModelDb.InitIds();
            save.InitProfileId(1); save.InitProgressData(); save.SettingsSave.Language = "eng";
            MegaCrit.Sts2.Core.Localization.LocManager.Initialize(); RuntimeSnapshotThreadGuard.BindCurrentThread();
            var watcher = ModelDb.AllCharacters.Single(c => c.GetType().Assembly != typeof(Ironclad).Assembly &&
                c.GetType().Assembly.GetName().Name!.Contains("Watcher", StringComparison.OrdinalIgnoreCase));
            Check(watcher.CardPool.AllCards.Any(c => c.GetType().Assembly == watcher.GetType().Assembly),
                "Real Watcher dynamic card pool contributes actual mod cards");
            if (Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION") == "1")
            {
                await RunInteractionLatencyAsync(output, watcher);
                stage = "interaction-suite-complete"; Save("complete");
                return;
            }
            foreach (var character in new CharacterModel[] { ModelDb.Character<Ironclad>(), watcher })
            {
                stage = "capture-" + character.Id.Entry; Save("running");
                var player = Player.CreateForNewRun(character, UnlockState.all, 1);
                var run = RunState.CreateForTest([player], seed: seed, ascensionLevel: 10); run.CurrentActIndex = actIndex;
                player.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
                run.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState), run.Rng.UpFront);
                const BindingFlags bind = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var stateProperty = typeof(RunManager).GetProperty("State", bind)!;
                var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", bind)!;
                var oldRun = stateProperty.GetValue(RunManager.Instance); var oldAscension = ascensionProperty.GetValue(RunManager.Instance);
                stateProperty.SetValue(RunManager.Instance, run);
                ascensionProperty.SetValue(RunManager.Instance, new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(10));
                LocalContext.NetId = player.NetId;
                try
                {
                    var ev = (CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();
                    await ev.BeginEvent(player, null, false);
                    string fingerprint = CrystalSphereLiveCapture.SceneFingerprint(run, player, ev, null);
                    var captured = CrystalSphereLiveCapture.CaptureScene(run, player, ev, null, "0.111.0");
                    foreach (var branch in captured.Branches)
                    {
                        string name = character.Id.Entry + "-" + branch.Mode;
                        stage = name; Save("running");
                        ProductionSearchReplay.Write(Path.Combine(output, name + "-snapshot.json"), branch.Snapshot);
                        RunBoard(name, branch.Snapshot);
                    }
                    Check(fingerprint == CrystalSphereLiveCapture.SceneFingerprint(run, player, ev, null),
                        "Overall latency search preserves actual native run/player/event");
                }
                finally { stateProperty.SetValue(RunManager.Instance, oldRun); ascensionProperty.SetValue(RunManager.Instance, oldAscension); }
            }
            // Run after every normal board/selection measurement and after its
            // workers have joined. Results never enter any measured session.
            foreach (var job in tailJobs.SelectMany(job => job.Targets.Select(target =>
                (job.Name, job.Source, job.Table, Target: target))).Take(3))
            {
                var target = job.Target;
                stage = job.Name + "-tail-language";
                var diagnosticWatch = Stopwatch.StartNew();
                try
                {
                    var bound = new PredictorCrystalOffsetBound(job.Source, job.Table, true, rootCardSlots: true);
                    var result = PredictorCrystalGoalLanguage.Solve(job.Source, job.Table, [target], true,
                        TimeSpan.FromSeconds(2), sharedBound: bound, rootCardSlots: true);
                    receipts.Add(new { kind = "tail-language-diagnostic", name = job.Name, target,
                        elapsedMs = diagnosticWatch.Elapsed.TotalMilliseconds, result.Verdict,
                        result.RewardStates, result.LanguageNodes, result.GeometryNodes, result.NegativeKind,
                        hasPlan = result.Plan != null, scope = "Outside UI timing; cold pending target; shared immutable table and fresh bound; no witness/session feedback" });
                    Console.WriteLine($"CRYSTAL_OVERALL_TAIL case={job.Name};target={target.CacheKey};ms={diagnosticWatch.Elapsed.TotalMilliseconds:F3};verdict={result.Verdict};rewardStates={result.RewardStates};geometryNodes={result.GeometryNodes};hasPlan={result.Plan != null};scope=outside-ui-timing");
                }
                catch (Exception error)
                {
                    receipts.Add(new { kind = "tail-language-diagnostic", name = job.Name, target,
                        elapsedMs = diagnosticWatch.Elapsed.TotalMilliseconds, error = error.ToString(),
                        scope = "Auxiliary diagnostic failed outside UI timing; original normal-query receipts retained" });
                    Console.WriteLine($"CRYSTAL_OVERALL_TAIL_FAILED case={job.Name};target={target.CacheKey};error={error.GetType().Name};scope=outside-ui-timing");
                }
                Save("running");
            }
            stage = "complete"; Save("complete");
            Console.WriteLine("CRYSTAL_OVERALL_LATENCY_PASS realWatcher=true;boards=3-and-6;injectedWitnesses=0;nativeStateUnmodified=true;see-json-for-pending-and-gold-gap");
        }
        catch (Exception error) { Save("failed", error); throw; }

        void RunBoard(string name, PredictorCrystalSnapshot source)
        {
            if (!frozenChoices) { choices.TryAdd(name, []); ProductionSearchReplay.Write(choicesPath, choices); }
            var session = new PredictorCrystalReachability(source, true, potionScenarioBound: true,
                workers: source.Remaining > 3 ? Math.Clamp(Environment.ProcessorCount - 1, 1, 4) : 1,
                targetDirected: source.Remaining > 3, includeRerolls: true, goalLanguageProof: true, includeEnchantments: true);
            using var lifetime = new CancellationTokenSource();
            var selectionCancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            ImmutableArray<CrystalRewardOption> selected = [];
            ImmutableArray<CrystalRewardOption> pickerOptions = [];
            try
            {
                for (int count = 0; count <= 3; count++)
                {
                    var watch = Stopwatch.StartNew();
                    if (count > 0)
                    {
                        // SelectOption cancels the previous worker queue before
                        // publishing selection-scoped cached facts.
                        selectionCancel.Cancel(); selectionCancel.Dispose();
                        selectionCancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    }
                    var projection = session.PeekKnown(selected);
                    CrystalReachabilityUpdate? update = null;
                    bool candidatesComplete = false;
                    double candidateSeconds = 0, goldSeconds = 0, rootExclusionMs = 0, reprojectionMs = 0;
                    double? candidateReadyMs = null;
                    double? firstAvailableMs = projection.Available.Count > 0 ? watch.Elapsed.TotalMilliseconds : null;
                    double? firstExcludedMs = null;
                    double? firstNewPickerExcludedMs = null;
                    var priorPickerOptions = pickerOptions.Where(c => !selected.Contains(c)).ToImmutableArray();
                    var excluded = new HashSet<CrystalRewardOption>();
                    var ticks = new List<object>();
                    var tableField = typeof(PredictorCrystalReachability).GetField("_table", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var rootMethod = typeof(PredictorCrystalReachability).GetMethod("RootCardExclusions", BindingFlags.Instance | BindingFlags.NonPublic);
                    void Trace(string phase) => ticks.Add(new { phase, ms = watch.Elapsed.TotalMilliseconds,
                        candidateSeconds, goldSeconds, available = projection.Available.Count, excluded = excluded.Count,
                        candidatesComplete, projection.Gold, projection.GoldUpperBound, projection.GoldComplete });
                    void PublishExcluded(ImmutableArray<CrystalRewardOption> values)
                    {
                        if (values.IsEmpty) return;
                        excluded.UnionWith(values);
                        firstExcludedMs ??= watch.Elapsed.TotalMilliseconds;
                        if (values.Any(priorPickerOptions.Contains)) firstNewPickerExcludedMs ??= watch.Elapsed.TotalMilliseconds;
                        if (tableField.GetValue(session) != null) candidateReadyMs ??= watch.Elapsed.TotalMilliseconds;
                        Trace("excluded-publication");
                    }
                    if (count > 0)
                    {
                        var synchronous = Stopwatch.StartNew();
                        var knownOptions = priorPickerOptions;
                        // The same host can run against the frozen baseline DLL:
                        // old UI only removes previously certified conflicts.
                        PublishExcluded(rootMethod != null
                            ? (ImmutableArray<CrystalRewardOption>)rootMethod.Invoke(session, [selected, knownOptions])!
                            : knownOptions.Where(c => !selected.Contains(c) && session.IsCertifiedConflict(selected.Add(c))).ToImmutableArray());
                        rootExclusionMs = synchronous.Elapsed.TotalMilliseconds;
                        Trace("selection-root-exclusions");
                        synchronous.Restart();
                        projection = session.ProjectKnown(selected, selectionCancel.Token, known =>
                        {
                            projection = known;
                            if (known.Available.Count > 0) firstAvailableMs ??= watch.Elapsed.TotalMilliseconds;
                            Trace("selection-projection-publication");
                        });
                        reprojectionMs = synchronous.Elapsed.TotalMilliseconds;
                        Trace("selection-reprojected");
                    }
                    var gold = projection;
                    while (watch.ElapsedMilliseconds < queryMs && (!candidatesComplete || !gold.GoldComplete))
                    {
                        if (!gold.GoldComplete)
                        {
                            var laneWatch = Stopwatch.StartNew();
                            gold = session.AdvanceGold(selected, TimeSpan.FromMilliseconds(Math.Min(75, Math.Max(1, queryMs - watch.ElapsedMilliseconds))), selectionCancel.Token);
                            goldSeconds += laneWatch.Elapsed.TotalSeconds;
                            projection = session.PeekKnown(selected);
                            if (projection.Available.Count > 0) firstAvailableMs ??= watch.Elapsed.TotalMilliseconds;
                            Trace("gold-slice");
                        }
                        if (!candidatesComplete && watch.ElapsedMilliseconds < queryMs)
                        {
                            var laneWatch = Stopwatch.StartNew();
                            update = session.Advance(selected, TimeSpan.FromMilliseconds(Math.Min(150, Math.Max(1, queryMs - watch.ElapsedMilliseconds))),
                                selectionCancel.Token, PublishExcluded);
                            candidateSeconds += laneWatch.Elapsed.TotalSeconds;
                            candidatesComplete = update.Complete;
                            PublishExcluded(update.ExcludedCandidates);
                            if (tableField.GetValue(session) != null) candidateReadyMs ??= watch.Elapsed.TotalMilliseconds;
                            projection = session.PeekKnown(selected);
                            if (projection.Available.Count > 0) firstAvailableMs ??= watch.Elapsed.TotalMilliseconds;
                            Trace("candidate-slice");
                        }
                    }
                    double wallMs = watch.Elapsed.TotalMilliseconds;
                    var table = tableField.GetValue(session) as CrystalReachabilityTable;
                    var candidates = table?.Candidates.Where(c => !selected.Contains(c)).ToArray() ?? [];
                    Check(!excluded.Any(projection.Available.ContainsKey), "Candidate positive/exclusion consistency");
                    string? rarity = selected.FirstOrDefault(c => c.Kind == PredictorRewardKind.Card) is { } first ? Rarity(first) : null;
                    var sameRarity = candidates.Where(c => c.Kind == PredictorRewardKind.Card && Rarity(c) == rarity).ToArray();
                    if (tailLanguage && count == 0 && table != null)
                        tailJobs.Add((name, source, table, candidates.Where(c => !excluded.Contains(c) && !projection.Available.ContainsKey(c))
                            .OrderBy(c => c.CacheKey, StringComparer.Ordinal).Take(3).ToImmutableArray()));
                    gold = projection;
                    Check(gold.GoldUpperBound == null || gold.GoldUpperBound >= gold.Gold, "Gold upper bound retains incumbent");
                    if (gold.SelectedPlan is { } plan)
                    {
                        var replay = PredictorCrystalExplorer.Replay(source, plan.Steps);
                        Check(replay.Rewards.Where(r => r.Kind == PredictorRewardKind.Gold).Sum(r => r.GoldAmount) == gold.Gold,
                            "Published gold has a replayable endpoint");
                        Check(selected.IsEmpty || PredictorCrystalExplorer.VerifySelection(source, new(plan.Steps, replay.Rewards), selected) != null,
                            "Published gold preserves every naturally selected target");
                    }
                    receipts.Add(new { name, source.Context.Character, source.Remaining, source.State.Relics, count, selected,
                        candidateReadyMs, firstAvailableMs, firstExcludedMs, firstNewPickerExcludedMs,
                        newPickerExcludedCount = excluded.Count(priorPickerOptions.Contains),
                        wallMs, candidateSeconds, goldSeconds, rootExclusionMs, reprojectionMs,
                        rootExclusionMode = rootMethod == null ? "cached-certified-conflicts" : "prepared-root-card-exclusions",
                        candidateDomainReady = table != null, candidateCount = candidates.Length, available = candidates.Count(projection.Available.ContainsKey),
                        excluded = excluded.Count, pending = candidates.Count(c => !excluded.Contains(c) && !projection.Available.ContainsKey(c)),
                        pendingKeys = candidates.Where(c => !excluded.Contains(c) && !projection.Available.ContainsKey(c)).Select(c => c.CacheKey).Order().ToArray(),
                        Complete = candidatesComplete, selectedWitness = projection.SelectedPlan != null, rarity,
                        sameRarityCount = sameRarity.Length, sameRarityExcluded = sameRarity.Count(excluded.Contains),
                        excludedKeys = excluded.Select(c => c.CacheKey).Order().ToArray(),
                        availableKeys = projection.Available.Keys.Select(c => c.CacheKey).Order().ToArray(),
                        progress = update?.Progress, ticks, gold.Gold, gold.GoldUpperBound,
                        gold.GoldComplete, goldWitness = gold.SelectedPlan != null });
                    Save("running");
                    Console.WriteLine($"CRYSTAL_OVERALL_QUERY case={name};selected={count};wallMs={wallMs:F3};candidateSeconds={candidateSeconds:F3};goldSeconds={goldSeconds:F3};readyMs={candidateReadyMs:F3};available={projection.Available.Count};excluded={excluded.Count};complete={candidatesComplete};gold={gold.Gold};upper={gold.GoldUpperBound};goldComplete={gold.GoldComplete}");
                    // SelectOption carries forward only the previous visible
                    // Available+pending domain, never already excluded entries.
                    pickerOptions = candidates.Where(c => !excluded.Contains(c) && !selected.Contains(c)).ToImmutableArray();
                    if (count == 3) break;
                    // Only options actually published to this selection's picker
                    // can become the next user choice. No oracle witness seeding.
                    CrystalRewardOption? next;
                    if (frozenChoices)
                    {
                        var requested = choices.GetValueOrDefault(name, []);
                        next = requested.Length > count ? requested[count] : null;
                        if (next != null && !projection.Available.ContainsKey(next))
                        {
                            receipts.Add(new { name, stage = "frozen-natural-choice-not-ready", afterCount = count, target = next, pending = true });
                            Save("running"); break;
                        }
                    }
                    else next = projection.Available.Keys.Where(c => !selected.Contains(c) && c.Key.IsValid)
                        .OrderBy(c => c.Kind == PredictorRewardKind.Card ? 0 : 1).ThenBy(c => c.CacheKey, StringComparer.Ordinal).FirstOrDefault();
                    if (next == null)
                    {
                        receipts.Add(new { name, stage = "natural-selection-unavailable", afterCount = count, pending = true });
                        Save("running"); break;
                    }
                    if (!frozenChoices) { choices[name] = selected.Add(next); ProductionSearchReplay.Write(choicesPath, choices); }
                    selected = selected.Add(next);
                }
            }
            finally { lifetime.Cancel(); JoinOverallWorkers(session); selectionCancel.Dispose(); }
        }
    }

    private static string Rarity(CrystalRewardOption option) =>
        ModelDb.GetById<CardModel>(new(option.Key.Category, option.Key.Entry)).Rarity.ToString();

    private static int OverallBudget(string name, int fallback)
    {
        int value = int.Parse(Environment.GetEnvironmentVariable(name) ?? fallback.ToString());
        if (value is < 1 or > 10000) throw new ArgumentOutOfRangeException(name, "Expected 1..10000 ms");
        return value;
    }

    private static void JoinOverallWorkers(PredictorCrystalReachability session)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        foreach (string ownerField in new[] { "_languageQueries", "_goldQueries" })
        {
            var owners = (IDictionary)typeof(PredictorCrystalReachability).GetField(ownerField, fields)!.GetValue(session)!;
            foreach (var owner in owners.Values)
            {
                string field = ownerField == "_languageQueries" ? "Worker" : "_languageTask";
                if (owner!.GetType().GetField(field, fields)?.GetValue(owner) is not Task worker) continue;
                Check(Task.WaitAny([worker], TimeSpan.FromSeconds(5)) == 0, "Overall worker exits after fixture cancellation");
                try { worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            }
        }
    }

    // Native discovery sees exactly the copied package. This host never writes
    // to installed mods or gives ModManager a real save filesystem.
    private sealed class OverallWatcherFileIo(string package) : IModManagerFileIo
    {
        private readonly string _root = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "mods");
        private bool IsRoot(string path) => Path.GetFullPath(path).Equals(Path.GetFullPath(_root), StringComparison.OrdinalIgnoreCase);
        public string[] GetFilesAt(string path) => path == package ? ["Watcher.json"] : [];
        public string[] GetDirectoriesAt(string path) => IsRoot(path) ? [package] : [];
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => IsRoot(path) || path == package;
        public Stream OpenStream(string path, Godot.FileAccess.ModeFlags mode) => mode == Godot.FileAccess.ModeFlags.Read
            ? File.OpenRead(path) : throw new InvalidOperationException("Overall fixture refuses writes: " + path);
        public void MakeDirRecursive(string path) => throw new InvalidOperationException("Overall fixture refuses directory writes: " + path);
        public Error CopyFile(string sourcePath, string destinationPath) => throw new InvalidOperationException("Overall fixture refuses save copies: " + destinationPath);
    }
}
