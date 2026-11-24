using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Test;
using MegaCrit.Sts2.Core.TestSupport;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Tests.FastSearch;

internal static class NativeWorkbenchCheckEnvironment
{
    internal static ModRuntimeSnapshot Initialize(string purpose)
    {
        if (Environment.GetEnvironmentVariable("RT2_AUTHORED_GODOT_RUNNER") != "1")
            throw new InvalidOperationException("Use AuthoredEventChecks native host and RT2_NATIVE_CHECK for this check");
        var root = new DirectoryInfo(Environment.CurrentDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RolltheSpire2.csproj"))) root = root.Parent;
        Environment.CurrentDirectory = root!.FullName;
        TestMode.IsOn = true;
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!.SetValue(null,
            MegaCrit.Sts2.Core.Modding.ModManagerState.Initialized);
        ModelDb.Init(); MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init(); ModelDb.InitIds();
        var mockSave = new SaveManager(new MockGodotFileIo("user://workbench-checks-memory"), true);
        SaveManager.MockInstanceForTesting(mockSave);
        SaveManager.Instance.InitSettingsDataForTest();
        SaveManager.Instance.InitPrefsDataForTest();
        SaveManager.Instance.InitProfileId(1);
        SaveManager.Instance.InitProgressData();
        SaveManager.Instance.SettingsSave.Language = "eng";
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        // Native catalog initialization uses mock saves. Standard party authority
        // rejects WorldGameMode.Test; the same mock survives this runtime-mode flip.
        TestMode.IsOn = false;
        if (!ReferenceEquals(SaveManager.Instance, mockSave))
            throw new InvalidOperationException("NativeWorkbenchCheck.MockSaveManagerLost");
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance,
            Detection = new("0.111.0", "0.111.0", "beta", purpose, true, "") };
        WorkspaceShell.InitializeSearchEvidenceOnMainThread();
        // Use the real catalog capture without the main-menu wrapper's
        // user-data baseline-candidate file writer.
        var authority = RuntimeAuthoritySnapshotCapture.Capture(runtime.Detection.DisplayVersion);
        typeof(RuntimeAuthorityEnvironment).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
            new RuntimeAuthorityEnvironmentSnapshot(authority, RuntimeAuthorityInterpreter.Interpret(authority), ""));
        return runtime;
    }
}
