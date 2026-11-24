using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    // Opt-in process only: real packages and initializers, never a fabricated
    // Loaded record. Copies isolate any third-party package-local persistence.
    private static async Task LoadRfCoexistencePackages(string output)
    {
        var args = OS.GetCmdlineUserArgs();
        string Argument(string flag, string? fallback = null)
        {
            int at = Array.IndexOf(args, flag);
            return at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) :
                fallback ?? throw new ArgumentException("RfCoexistence requires " + flag);
        }
        string receipt = Path.Combine(output, "operation-rf-load-result.json");
        Directory.CreateDirectory(output);
        var identities = new List<object>();
        void Write(bool passed, string stage, Exception? error = null) => File.WriteAllText(receipt,
            JsonSerializer.Serialize(new { passed, stage, error = error?.ToString(), packages = identities,
                mods = ModManager.Mods.Select(mod => new { id = mod.manifest?.id, mod.manifest?.version,
                    state = mod.state.ToString(), errors = mod.errors?.Select(e => e.ToString()),
                    assemblies = mod.assemblies.Select(a => new { a.FullName, a.Location }) }) },
                new JsonSerializerOptions { WriteIndented = true }));
        string stage = "copy-installed-packages";
        try
        {
            if (ModManager.State != ModManagerState.None || ModManager.Mods.Count != 0)
                throw new InvalidOperationException("RF package check requires a fresh isolated ModManager before ModelDb initialization");
            string CopyPackage(string source, string id, string manifest)
            {
                string target = Path.Combine(output, "rf-packages", id);
                if (Directory.Exists(target)) throw new IOException("RF package output already exists: " + target);
                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(source, file);
                    string copied = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
                    File.Copy(file, copied);
                    if (Path.GetExtension(file) is ".dll" or ".pck" || relative == manifest ||
                        relative.EndsWith(".manifest", StringComparison.Ordinal))
                    {
                        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                        if (hash != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copied))))
                            throw new IOException("RF fixture copy hash mismatch: " + file);
                        identities.Add(new { id, relative, source = file, copy = copied, sha256 = hash });
                    }
                }
                if (!File.Exists(Path.Combine(target, manifest))) throw new FileNotFoundException(manifest, source);
                return target;
            }
            string ritsu = CopyPackage(Argument("--ritsulib-package",
                "D:/Game/Steam/steamapps/workshop/content/2868840/3747602295"), "STS2-RitsuLib", "mod_manifest.json");
            string rf = CopyPackage(Argument("--rf-package",
                "D:/Game/Steam/steamapps/workshop/content/2868840/3747531952"), "RandomForeseer", "RandomForeseer.json");

            // The native loaders select a variant using real release metadata.
            // Stock Godot does not live beside the game's release_info.json.
            stage = "bind-native-game-release-metadata";
            string releasePath = Path.Combine(Path.GetDirectoryName(Argument("--game-pck"))!, "release_info.json");
            var release = JsonSerializer.Deserialize<ReleaseInfo>(File.ReadAllText(releasePath))
                ?? throw new InvalidDataException("Missing game release metadata: " + releasePath);
            var version = SemanticVersion.FromString(release.Version);
            var releaseManager = ReleaseInfoManager.Instance;
            typeof(ReleaseInfoManager).GetField("<ReleaseInfo>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(releaseManager, release);
            typeof(ReleaseInfoManager).GetField("<SemVer>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(releaseManager, version);
            identities.Add(new { id = "native-game-release", source = releasePath,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(releasePath))), release.Version });

            stage = "native-mod-manager-initialize";
            TestMode.IsOn = false;
            var settings = new ModSettings { PlayerAgreedToModLoading = true, ModList =
            [ new() { Id = "STS2-RitsuLib", Source = ModSource.ModsDirectory },
              new() { Id = "RandomForeseer", Source = ModSource.ModsDirectory } ] };
            await ModManager.Initialize(new RfPackageFileIo(ritsu, rf), settings, version);
            foreach (string id in new[] { "STS2-RitsuLib", "RandomForeseer" })
            {
                var mod = ModManager.Mods.Single(m => m.manifest?.id == id);
                if (mod.state != ModLoadState.Loaded || mod.errors is { Count: > 0 } || mod.assemblies.Count == 0)
                    throw new InvalidOperationException("Native package initialization failed: " + id +
                        ";state=" + mod.state + ";errors=" + string.Join(";", mod.errors?.Select(e => e.ToString()) ?? []) +
                        ";assemblies=" + string.Join(";", mod.assemblies.Select(a => a.FullName)));
            }
            // RF's workshop loader does not associate its implementation with
            // ModManager, unlike RitsuLib. Prove the implementation itself is
            // loaded from the copied package and owns installed native patches.
            stage = "verify-loaded-rf-implementation";
            string rfVersion = ModManager.Mods.Single(m => m.manifest?.id == "RandomForeseer").manifest!.version!;
            string implementationPath = Path.GetFullPath(Path.Combine(rf, "lib", rfVersion, "RandomForeseer.dll"));
            var implementation = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly =>
                !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location) &&
                Path.GetFullPath(assembly.Location).Equals(implementationPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("RF implementation not loaded from expected copied package: " + implementationPath);
            string loadedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(implementation.Location)));
            string installedImplementation = Path.Combine(Argument("--rf-package",
                "D:/Game/Steam/steamapps/workshop/content/2868840/3747531952"), "lib", rfVersion, "RandomForeseer.dll");
            if (loadedHash != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installedImplementation))))
                throw new InvalidOperationException("Loaded RF implementation differs from installed package: " + implementationPath);
            var ownedPatches = Harmony.GetAllPatchedMethods().SelectMany(method =>
            {
                var info = Harmony.GetPatchInfo(method)!;
                return info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                    .Where(patch => patch.PatchMethod.Module.Assembly == implementation)
                    .Select(patch => new { target = method.DeclaringType?.FullName + "." + method.Name,
                        owner = patch.owner, patch = patch.PatchMethod.DeclaringType?.FullName + "." + patch.PatchMethod.Name });
            }).ToArray();
            identities.Add(new { id = "loaded-rf-implementation", implementation.FullName,
                location = implementation.Location, sha256 = loadedHash, patchCount = ownedPatches.Length,
                patchOwners = ownedPatches.Select(patch => patch.owner).Distinct().ToArray(), patches = ownedPatches });
            if (ownedPatches.Length == 0)
                throw new InvalidOperationException("Loaded RF implementation owns no installed native Harmony patches: " + implementation.FullName);
            if (!RandomForeseerCompatibility.Capture().IsLoaded)
                throw new InvalidOperationException("RT2 did not recognize RF after actual native initialization");
            Write(true, "native-packages-initialized");
            GD.Print("OPERATION_RF_LOAD_PASS packages=STS2-RitsuLib,RandomForeseer;nativeInitializers=true");
        }
        catch (Exception error) { Write(false, stage, error); throw; }
    }

    // Restrict native discovery to the two copied manifests. The native manager
    // still parses, orders, resolves dependencies and invokes real initializers.
    // No save-copy/write operation is permitted through this fixture adapter.
    private sealed class RfPackageFileIo(string ritsu, string rf) : IModManagerFileIo
    {
        private readonly string _virtualMods = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "mods");
        private bool IsRoot(string path) => Path.GetFullPath(path).Equals(Path.GetFullPath(_virtualMods), StringComparison.OrdinalIgnoreCase);
        public string[] GetFilesAt(string path) => path == ritsu ? ["mod_manifest.json"] :
            path == rf ? ["RandomForeseer.json"] : [];
        public string[] GetDirectoriesAt(string path) => IsRoot(path) ? [ritsu, rf] : [];
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => IsRoot(path) || path == ritsu || path == rf;
        public Stream OpenStream(string path, Godot.FileAccess.ModeFlags mode) => mode == Godot.FileAccess.ModeFlags.Read
            ? File.OpenRead(path) : throw new InvalidOperationException("RF check refuses ModManager writes: " + path);
        public void MakeDirRecursive(string path) => throw new InvalidOperationException("RF check refuses directory mutation: " + path);
        public Error CopyFile(string sourcePath, string destinationPath) =>
            throw new InvalidOperationException("RF check refuses save-copy mutation: " + destinationPath);
    }

    private static async Task RunRfCoexistenceChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player, NTopBar topBar,
        Control ransack, Func<string> signature, Action<bool, string> check)
    {
        string receipt = Path.Combine(output, "operation-rf-coexistence-result.json");
        var evidence = new List<object>();
        var preferences = new SearchWorkspace(OS.GetUserDataDir(), false);
        var originalModes = preferences.Preferences.OperationPredictionModes;
        bool originalEnabled = preferences.Preferences.OperationPredictionEnabled;
        string original = signature();
        var bottles = new List<PotionModel>();
        NPotionHolder? holder = null;
        bool passed = false;
        string? failure = null;
        object? Field(object value, string name) => PredictorOperationCapture.ReadNullable(value, name);
        NeowOperationHoverController Controller() => hoverRoot.GetNode<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        NeowOperationOverlay? View() => Controller().GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
        OperationPreview? Preview() => View() is { Visible: true } view ? Field(view, "_preview") as OperationPreview : null;
        async Task Frames(int count = 2)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(15)) await Frames(1);
            check(ready(), "RF coexistence " + reason);
        }
        async Task Focus(Control owner)
        {
            NeowOperationHoverController.Leave(owner);
            viewport.PushInput(new InputEventMouseMotion { Position = owner.GetGlobalRect().GetCenter(),
                GlobalPosition = owner.GetGlobalRect().GetCenter() }, true);
            NeowOperationHoverController.Focus(owner, runtime.Detection.NormalizedVersion);
            await Frames();
        }
        void NoCapture(string name)
        {
            var controller = Controller();
            check(Field(controller, "_snapshot") == null &&
                Field(controller, "_work") == null && Preview() == null &&
                PredictorOperationCapture.Read<NeowOperationHoverController.HoverTiming>(controller, "_timing").CaptureMs == 0,
                name + " has no detached capture, worker or RT2 display");
            evidence.Add(new { name, capture = false, worker = false, displayed = false });
        }
        try
        {
            File.WriteAllText(receipt, "{\"passed\":false,\"stage\":\"running\"}");
            check(RandomForeseerCompatibility.Capture().IsLoaded && ModManager.Mods.Count == 2 && ModManager.Mods
                .Where(m => m.manifest?.id is "RandomForeseer" or "STS2-RitsuLib")
                .All(m => m.state == ModLoadState.Loaded && m.errors == null),
                "coexistence uses actual loaded RF and RitsuLib without initialization errors");
            preferences.SetOperationPredictionEnabled(true);
            preferences.SetOperationPredictionMode("event:POTION_COURIER", OperationPredictionMode.Auto);
            await Focus(ransack); await Frames(4); NoCapture("loaded-RF Ransack Auto");
            string before = signature();
            preferences.SetOperationPredictionMode("event:POTION_COURIER", OperationPredictionMode.On);
            await Until(() => Preview() is { } p && p.Source.Entry == "POTION_COURIER" && p.Faults.IsEmpty &&
                p.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows).Any(r => r.Models.Any(m => m.Category == "POTION")),
                "Ransack On publishes real potion identities without refocusing");
            evidence.Add(new { name = "loaded-RF Ransack On", sameOwner = ReferenceEquals(Field(Controller(), "_owner"), ransack), displayed = true });
            check(signature() == before, "loaded RF and RT2 On leave Ransack native state unchanged");
            await Frames(3);
            using (var image = root.GetViewport().GetTexture().GetImage())
                image.SavePng(Path.Combine(output, "operation-rf-ransack-on.png"));
            preferences.SetOperationPredictionMode("event:POTION_COURIER", OperationPredictionMode.Auto);
            await Until(() => Preview() == null, "Ransack returns to suppressed Auto without refocusing");
            await Frames(3);
            NoCapture("loaded-RF Ransack On-to-Auto");
            preferences.SetOperationPredictionMode("event:POTION_COURIER", OperationPredictionMode.Off);
            await Frames(3); NoCapture("loaded-RF Ransack Off");
            NeowOperationHoverController.Leave(ransack);

            PotionModel Add(string key)
            {
                var potion = ModelDb.GetById<PotionModel>(new("POTION", key)).ToMutable();
                check(player.AddPotionInternal(potion, silent: true).success, "RF fixture adds actual " + key);
                bottles.Add(potion); return potion;
            }
            var attack = Add("ATTACK_POTION");
            Add("SKILL_POTION");
            holder = NPotionHolder.Create(true);
            var container = topBar.GetNode<NPotionContainer>("%PotionContainer");
            typeof(NTopBar).GetProperty(nameof(NTopBar.PotionContainer))!.SetValue(topBar, container);
            container.AddChild(holder); holder.Position = new(50, 0); holder.AddPotion(NPotion.Create(attack)!);
            await Frames();
            preferences.SetOperationPredictionMode("potion:ATTACK_POTION", OperationPredictionMode.Auto);
            preferences.SetOperationPredictionMode("extra:potion-order", OperationPredictionMode.Auto);
            await Focus(holder);
            await Until(() => Preview() is { } p && p.Faults.IsEmpty && p.Branches.SelectMany(b => b.Sections)
                .Any(s => s.FeatureId == "extra:potion-order" && s.CardGroups.Length > 0),
                "Auto retains real bottle-order supplement beside loaded RF");
            check(Preview()!.Branches.SelectMany(b => b.Sections).All(s => s.FeatureId != "potion:ATTACK_POTION"),
                "Auto bottle-order output excludes RF duplicate base section");
            var old = Preview();
            var rng = run.Rng.CombatCardGeneration;
            var state = rng.ToSerializable();
            try
            {
                rng.NextInt(); before = signature();
                var observed = (PredictorOperationPotionObservation)Field(Controller(), "_potionObservation")!;
                var expected = PotionOperationPreview.Build(PredictorOperationPotionTargetCapture.CaptureLive(run,
                    player.NetId, runtime.Detection.NormalizedVersion, observed.Source),
                    demand: new OperationPotionDemand(false, true, false));
                await Until(() => Preview() is { } p && !ReferenceEquals(p, old) && p.Faults.IsEmpty,
                    "same bottle refreshes retained supplement after relevant real RNG changes");
                check(JsonSerializer.Serialize(Preview()) == JsonSerializer.Serialize(expected),
                    "refreshed supplement matches current captured RNG and inventory");
                check(ReferenceEquals(Field(Controller(), "_owner"), holder) && signature() == before,
                    "supplement refresh keeps native owner and consumes no live state");
                evidence.Add(new { name = "loaded-RF potion order Auto and RNG refresh", displayed = true, sameOwner = true });
                await Frames(3);
                using var image = root.GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(output, "operation-rf-potion-supplement.png"));
            }
            finally { rng.LoadFromSerializable(state); }
            preferences.SetOperationPredictionMode("extra:potion-order", OperationPredictionMode.Off);
            await Until(() => Preview() == null, "disabling the only supplement suppresses Auto potion without refocusing");
            await Frames(3);
            NoCapture("loaded-RF potion Auto with order Off");
            preferences.SetOperationPredictionMode("potion:ATTACK_POTION", OperationPredictionMode.On);
            await Until(() => Preview() is { } p && p.Branches.SelectMany(b => b.Sections)
                .Any(s => s.FeatureId == "potion:ATTACK_POTION" && s.CardGroups.Length > 0),
                "explicit potion On restores base output with RF loaded");
            preferences.SetOperationPredictionMode("extra:potion-order", OperationPredictionMode.On);
            preferences.SetOperationPredictionMode("potion:ATTACK_POTION", OperationPredictionMode.Off);
            await Until(() => Preview() == null, "source Off hides its bottle-order output even with supplement On");
            await Frames(3);
            NoCapture("loaded-RF potion source Off");
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            NeowOperationHoverController.Leave(ransack);
            if (holder != null) { NeowOperationHoverController.Leave(holder); holder.Free(); }
            foreach (var potion in bottles) if (player.PotionSlots.Contains(potion)) player.DiscardPotionInternal(potion, silent: true);
            foreach (var entry in OperationPredictionCatalog.Entries)
                preferences.SetOperationPredictionMode(entry.Id, originalModes.Get(entry.Id));
            preferences.SetOperationPredictionEnabled(originalEnabled);
            bool restored = signature() == original;
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = passed && restored, failure, restored,
                rf = RandomForeseerCompatibility.Capture(), evidence,
                scope = "actual installed package copies, native initializers, controlled native Ransack and held-potion surfaces; not natural-run RF display/real-game acceptance" },
                new JsonSerializerOptions { WriteIndented = true }));
            if (failure == null) check(restored, "RF coexistence restores the complete native fixture state");
        }
        GD.Print("OPERATION_RF_COEXISTENCE_PASS actualInitializers=true;ransackTriState=true;potionSupplement=true;sameHoverRefresh=true");
    }
}
