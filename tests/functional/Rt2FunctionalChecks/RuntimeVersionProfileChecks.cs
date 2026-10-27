using RolltheSpire2.Ui.Workspace;
using System.Text.Json;
using System.Collections.Immutable;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Enumeration;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

// Fixed managed cases. No game startup, GPU, candidate scan or player-data writes.
internal static class RuntimeVersionProfileChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        void Refuses(Action action, string label)
        {
            try { action(); }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith(ProfileSeedGenerator.UnavailableCode, StringComparison.Ordinal))
            { Check(true, label); return; }
            throw new InvalidOperationException(label + " did not explicitly reject the profile");
        }
        CheckRequestSupportScope(Check);
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "runtime-version-profile", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "release_info.json");
        File.WriteAllText(file, "{\"version\":\"v0.110.1\",\"branch\":\"file-fixture\"}");
        var runtimeVersion = GameVersionDetector.Detect(() => ("v0.111.0", "runtime-fixture"), [file]);
        Check(runtimeVersion.IsExact && runtimeVersion.NormalizedVersion == "0.111.0" && runtimeVersion.Branch == "runtime-fixture" &&
            runtimeVersion.Evidence.StartsWith("runtime:"), "Runtime release identity takes precedence over a conflicting file");
        Check(typeof(GameVersionDetector).GetMethod(nameof(GameVersionDetector.Detect), Type.EmptyTypes) is not null,
            "File-only tools retain the parameterless detector entry point");
        var inaccessiblePaths = Enumerable.Repeat("fixture", 1).Select<string, string>(_ => throw new IOException("path-fixture-failure"));
        var withoutPaths = GameVersionDetector.Detect(() => ("v0.111.0", "runtime-fixture"), inaccessiblePaths);
        Check(withoutPaths.IsExact && withoutPaths.NormalizedVersion == "0.111.0",
            "A usable runtime version never enumerates fallback paths");
        var failedPaths = GameVersionDetector.Detect(() => throw new IOException("reader-fixture-failure"), inaccessiblePaths);
        Check(!failedPaths.IsExact && RuntimeProfileRegistry.Select(failedPaths) == UnsupportedProfile.Instance &&
            failedPaths.Evidence.Contains("reader-fixture-failure") && failedPaths.Evidence.Contains("path-fixture-failure"),
            "Runtime and fallback path failures retain both causes without aborting startup or inventing a profile");
        foreach (Func<(string Version, string Branch)?> reader in new Func<(string, string)?>[]
                 { () => null, () => ("UNKNOWN", "fixture"), () => throw new IOException("reader-fixture-failure") })
        {
            var fallback = GameVersionDetector.Detect(reader, [file]);
            Check(fallback.IsExact && fallback.NormalizedVersion == "0.110.1" && fallback.Branch == "file-fixture" &&
                fallback.Evidence.Contains("runtime-unavailable:") && fallback.Evidence.Contains("release_info.json:"),
                "Missing, invalid or throwing runtime reader uses the file and retains its failed-source evidence");
            var absent = GameVersionDetector.Detect(reader, [Path.Combine(directory, "missing.json")]);
            Check(!absent.IsExact && absent.FailureReason.Length > 0 && RuntimeProfileRegistry.Select(absent) == UnsupportedProfile.Instance,
                "Neither source available must retain Unsupported");
        }
        var unsupportedVersion = GameVersionDetector.Detect(() => ("v0.107.1", "runtime-fixture"), [file]);
        Check(unsupportedVersion.IsExact && RuntimeProfileRegistry.Select(unsupportedVersion) == UnsupportedProfile.Instance,
            "A detected unsupported runtime version must not be replaced by a supported file version");
        foreach (string content in new[] { "[]", "{\"version\":42}", "{\"version\":\"0..111.0\"}", "{" })
        {
            File.WriteAllText(file, content);
            Check(!GameVersionDetector.Detect(() => null, [file]).IsExact, "Malformed release metadata is not an exact version");
        }
        var newer = GameVersionDetector.Detect(() => ("v0.112.0-beta+fixture", "future-fixture"), []);
        var resolution = RuntimeProfileRegistry.Resolve(newer);
        Check(resolution.IsProvisional && resolution.AllowsProductionSearch && !resolution.RuntimeAccepted &&
              ProfileSeedGenerator.CreateProbeSeed(RuntimeProfileRegistry.Select(newer)) == "000000000000",
            "Existing provisional policy remains usable and unverified");
        Check(Base34SeedEnumerator.ZeroSeed(Beta111Profile.Instance) == "000000000000" &&
              new Base34SeedEnumerator(Beta111Profile.Instance, "000000000000").SeedAt(1) == "000000000001",
            "Supported seed enumeration is unchanged");

        var character = BaseGameModelKeys.Characters.Silent;
        var draft = new WorkbenchSearchDraft(character, 10, SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        var unsupported = ModRuntimeSnapshot.NotInitialized;
        var signature = SearchEnvironmentSignatureBuilder.Capture(unsupported);
        Check(!signature.CaptureComplete && signature.Fingerprint.Length == 0 &&
            signature.CaptureIssue.StartsWith(ProfileSeedGenerator.UnavailableCode), "Environment capture explicitly reports unavailable profile");
        Refuses(() => WorkbenchQueryCompiler.CompileAuthoredQuery(SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault,
            unsupported, character, 10, out _), "Query compiler");
        Refuses(() => draft.Compile(unsupported, out _), "Workbench compile");
        Refuses(() => NeowSearchUiCatalog.CaptureForPicker(unsupported, character, 10, 1, 0, null!), "N picker before runtime capture");
        Refuses(() => CombatRewardSearchUiCatalog.CaptureForPicker(unsupported, character, 10, 1, 0, null!), "C picker before runtime capture");

        foreach (IRuntimeProfile profile in new IRuntimeProfile[] { UnsupportedProfile.Instance,
            new BrokenProfile(12, ""), new BrokenProfile(0, "01"), new BrokenProfile(-1, "01"),
            new BrokenProfile(int.MaxValue, "01"), new BrokenProfile(12, "00"), new BrokenProfile(12, "#0"),
            new BrokenProfile(12, "01", false), new BrokenProfile(12, "01", Id: (RuntimeProfileId)999) })
        {
            Check(!ProfileSeedGenerator.TryCreateProbeSeed(profile, out string seed, out string issue) && seed.Length == 0 &&
                issue.StartsWith(ProfileSeedGenerator.UnavailableCode), "Invalid metadata has an explicit reason and no seed");
            Refuses(() => Base34SeedEnumerator.ZeroSeed(profile), "Probe seed");
            Refuses(() => new Base34SeedEnumerator(profile, "000000000000"), "Enumerator construction");
            Check(!SearchEnvironmentSignatureBuilder.Capture(unsupported with { Profile = profile }).CaptureComplete,
                "Invalid metadata cannot produce an environment identity");
        }

        var store = new SearchWorkspace(directory, false);
        store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "valid-fixture" });
        store.SaveWorkbench(draft);
        store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("fixture", draft));
        var now = DateTimeOffset.UtcNow;
        var capture = new SearchPresetCapture(character, 10, null!, 0, SearchPresetProbabilitySnapshot.Unavailable(now),
            SearchPresetProvenance.Unknown("fixture", now), now) { Workbench = draft };
        var preset = store.Presets.SaveUserPreset("Keep this preset", "fixture notes", [], capture);
        store.FlushAll();
        string savedDraft = JsonSerializer.Serialize(store.LoadWorkbench());
        var before = Directory.GetFiles(Path.Combine(directory, "RolltheSpire2"), "*.json", SearchOption.AllDirectories)
            // Current environment diagnostics may change; authored data must not.
            .Where(path => Path.GetFileName(path) != "search_environment.json")
            .ToDictionary(path => path, File.ReadAllText);
        store.EnsureEnvironment(signature);
        store.FlushAll();
        Check(!store.EnvironmentResetOccurred && JsonSerializer.Serialize(store.LoadWorkbench()) == savedDraft && store.RecentQueries.Count == 1 &&
            store.Presets.GetAll().Any(p => p.Id == preset.Id), "Unavailable profile preserves loaded draft, history and preset");
        Check(before.All(pair => File.ReadAllText(pair.Key) == pair.Value), "Failed environment capture does not overwrite authored/history JSON");
        var reopened = new SearchWorkspace(directory, false);
        reopened.EnsureEnvironment(signature);
        Check(reopened.LoadWorkbench() is not null && reopened.RecentQueries.Count == 1 &&
            reopened.Presets.GetAll().Any(p => p.Id == preset.Id), "Saved data remains readable after reopening and applying unavailable-profile environment capture");
        Console.WriteLine($"RUNTIME_VERSION_PROFILE PASS checks={checks};managedOnly=true;AndroidAcceptancePending=true");
        return 0;
    }

    private static void CheckRequestSupportScope(Action<bool, string> check)
    {
        var full = new RequestUnlockEvidence(RequestFactSource.ExplicitRequest, Vanilla111EpochBaseline.EpochIds,
            true, [new ModelKey("ENCOUNTER", "DECLARED")], 7, "ExplicitSeatFixture");
        var discovery = new RequestDiscoveryEvidence(RequestFactSource.ExplicitRequest,
            [new ModelKey("ACT", "OVERGROWTH")], true, "ExplicitDiscoveryFixture");
        var evidence = new RequestSupportEvidence(true, [], "LoadedModsFixture", full, discovery);
        RuntimeContextAuthoritySnapshot Context(RequestSupportEvidence? facts = null, string version = "0.111.0", int count = 1,
            ModelKey? character = null) => new(CharacterIdentity.FromKey(character ?? BaseGameModelKeys.Characters.Silent),
                10, 0, count, true, true, true, 4, 2, version, RuntimeProfileId.Beta111, true, null, "sts2",
                SourceAuthority.OfficialRuntimeExact, SnapshotCompleteness.Complete, IdentityResolutionStatus.Exact,
                predictionGameMode: count == 1 ? WorldGameMode.Singleplayer : WorldGameMode.Multiplayer,
                predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest, supportEvidence: facts);
        var baseline = Context(evidence);
        var serializedScope = JsonSerializer.Deserialize<RequestSupportAssessment>(JsonSerializer.Serialize(baseline.SupportScope))!;
        check(serializedScope.BaselineGameVersion == "0.111.0" && serializedScope.PlayerOperationCondition == "UnderDeclaredPlayerOperations",
            "Saved scope retains its own frozen baseline version and declared operations qualification");
        check(baseline.SupportScope.State == RequestSupportScopeState.WithinDeclaredBaseline,
            "Complete request evidence describes declared baseline membership conditional on player operations");
        check(Context().SupportScope.State == RequestSupportScopeState.Unknown,
            "Vanilla character, exact profile and character pool booleans cannot invent missing request environment evidence");
        check(Context(evidence with { LoadedModsInspected = false }).SupportScope.State == RequestSupportScopeState.Unknown,
            "Unavailable Mod inspection stays unknown despite exact model inputs");
        var modded = Context(evidence with { LoadedOtherMods = [new("OtherMod", "1.0")] });
        check(modded.SupportScope.State == RequestSupportScopeState.OutsideDeclaredBaseline && modded.CanUseCurrentModel,
            "Loaded other Mod remains outside commitment while existing model remains usable");
        check(Context(evidence, count: 2).SupportScope.State == RequestSupportScopeState.OutsideDeclaredBaseline,
            "Party request stays outside solo commitment even with explicit fully unlocked seats");
        var partial = full with { RevealedEpochs = full.RevealedEpochs.Remove("EVENT3_EPOCH") };
        check(Context(evidence with { Unlock = partial }).SupportScope.Reasons.Contains("RequestEpochNotUnlocked:EVENT3_EPOCH"),
            "Actual missing request Epoch wins over full account-like card pool booleans");
        var missing = full with { Complete = false };
        check(Context(evidence with { Unlock = missing }).SupportScope.State == RequestSupportScopeState.Unknown,
            "Missing actual request Epoch capture never defaults to full unlock");
        check(Context(evidence, version: "0.112.0").SupportScope.State == RequestSupportScopeState.OutsideDeclaredBaseline,
            "Future semantic profile reuse cannot extend the declared 0.111.0 commitment");
        check(Context(evidence, version: "unknown").SupportScope.State == RequestSupportScopeState.Unknown,
            "Unknown exact game version is unknown scope, not rejection");
        check(Context(evidence, character: new ModelKey("CHARACTER", "MODDED")).SupportScope.State == RequestSupportScopeState.OutsideDeclaredBaseline,
            "Modded character identity is outside commitment even if legacy IsVanilla field says true");
        check(Context(evidence with { Discovery = RequestDiscoveryEvidence.Unknown }).SupportScope.State == RequestSupportScopeState.WithinDeclaredBaseline,
            "Unknown discovery does not redefine full Epoch unlock or baseline scope; model owns required discovery facts");
        check(Context(evidence with { Discovery = discovery with { Acts = [] }, Unlock = full with { SeenEncounters = [] } }).SupportScope.State == RequestSupportScopeState.WithinDeclaredBaseline,
            "Partial discovered Acts and seen encounters are separate from full Epoch unlock");
        check(ReferenceEquals(baseline.WithEffectAuthority(null).SupportEvidence, evidence) &&
            ReferenceEquals(baseline.WithWorldAuthority(null).SupportEvidence, evidence) &&
            ReferenceEquals(baseline.WithEffectAuthority(null).SupportScope, baseline.SupportScope) &&
            ReferenceEquals(baseline.WithWorldAuthority(null).SupportScope, baseline.SupportScope),
            "Replacing simulation DTOs preserves the frozen request support evidence");
        check(baseline.SupportEvidence.Unlock.NumberOfRuns == 7 && baseline.SupportEvidence.Unlock.RevealedEpochs.SequenceEqual(Vanilla111EpochBaseline.EpochIds) &&
            baseline.SupportEvidence.Discovery.Source == RequestFactSource.ExplicitRequest,
            "Actual unlock/discovery primitives and source are retained without substituting account or all state");
    }

    private sealed record BrokenProfile(int SeedLength, string SeedAlphabet, bool SupportsSeedRng = true,
        RuntimeProfileId Id = RuntimeProfileId.Beta111) : IRuntimeProfile
    {
        public RuntimeProfileId ProfileId => Id;
        public RuntimeCapability Capability => RuntimeCapability.SeedAnalysisTypedNeowEffects;
        public bool TryCanonicalizeSeed(string rawSeed, out string canonicalSeed, out string issue) =>
            Beta111Profile.Instance.TryCanonicalizeSeed(rawSeed, out canonicalSeed, out issue);
        public ulong ComputeRootSeed(string seed) => throw new NotSupportedException();
        public ulong DeriveNamedStreamSeed(ulong root, string name) => throw new NotSupportedException();
        public ulong DeriveEventStreamSeed(ulong root, int slot, string entry) => throw new NotSupportedException();
    }
}
