using System.Diagnostics;
using System.Text.Json;
using Godot;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Ui.Components;

internal static partial class UiSurfaceChecks
{
    internal static async Task RunGpuInitializationAsync(Node root, string output, string gamePck)
    {
        Directory.CreateDirectory(output);
        // The product panel uses the game's shared fonts, like the workspace
        // checks. Mount real resources before constructing any themed control.
        if (!ProjectSettings.LoadResourcePack(gamePck, false))
            throw new InvalidOperationException("GpuInitializationSurface.NativeResourcePackUnavailable");
        root.GetWindow().Size = new(1280, 768);
        int checks = 0;
        void Require(bool value, string detail)
        { if (!value) throw new InvalidOperationException("GpuInitializationSurface:" + detail); checks++; }
        var tracker = FamilyDeviceProfileFoundation.Initialization;
        Require(tracker.Capture().State == FamilyGpuInitializationState.Unverified, "opening host is unverified");
        var initialEnvironment = tracker.Capture();
        tracker.ObserveEnvironment(initialEnvironment.EnvironmentKey, false, initialEnvironment.RenderingBackend);
        var unavailable = new GpuInitializationPanel("zh", () => false);
        var unavailableCompact = new GpuInitializationPanel("en", () => false, compact: true);
        Require(unavailable.Visible && unavailableCompact.Visible, "unavailable compute is visible in full and compact panels");
        Require(unavailable.GetNode<Label>("GpuInitializationState").Text.Contains("无法使用") &&
            unavailableCompact.GetNode<Label>("GpuInitializationState").Text.Contains("unavailable"),
            "unavailable compute has a localized status");
        Require(unavailable.GetNode<Label>("GpuInitializationDetails").Text.Contains("尚未尝试") &&
            unavailableCompact.GetNode<Label>("GpuInitializationDetails").Text.Contains("was not attempted"),
            "unavailable rendering environment is distinguished from a creation failure");
        Require(unavailableCompact.GetNode<Label>("GpuInitializationDetails").Text.Contains("CPU") &&
            !unavailable.GetNode<Button>("RetryGpuInitialization").Visible &&
            !unavailableCompact.GetNode<Button>("RetryGpuInitialization").Visible,
            "unavailable compute explains CPU availability without offering a device retry");
        tracker.ObserveEnvironment(initialEnvironment.EnvironmentKey, initialEnvironment.MainDeviceAvailable, initialEnvironment.RenderingBackend);
        unavailableCompact._Process(0);
        Require(!unavailableCompact.Visible, "unverified available environment stays quiet in compact mode");
        unavailable.Free(); unavailableCompact.Free();
        int creations = 0;
        await using (var failure = new FamilyGpuExecutionOwner(() => { creations++; return null; }, tracker))
        {
            try { await failure.ExecuteAsync(_ => true, CancellationToken.None); }
            catch (InvalidOperationException ex)
            { Require(ex.Message.Contains("CreateLocalRenderingDevice returned null"), "native-null evidence"); }
        }
        FamilyDeviceProfileFoundation.CaptureAvailabilityOnMainThread();
        Require(!FamilyDeviceProfileFoundation.GpuAvailable && tracker.Capture().LastFailure is not null,
            "main device capture does not reset local creation failure");
        try { await using var blocked = new FamilyGpuExecutionOwner(() => { creations++; return null; }, tracker); }
        catch (InvalidOperationException ex) { Require(ex.Message == "FamilyGpuInitializationRetryRequired", "known failure blocks admission"); }
        Require(creations == 1, "known failure does not re-enter native creation");
        File.WriteAllText(Path.Combine(output, "failed-state.json"), JsonSerializer.Serialize(tracker.Capture(), new JsonSerializerOptions { WriteIndented = true }));

        var surface = new PanelContainer { Position = new(24, 24), Size = new(1232, 672) };
        surface.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("131d29"),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 24, ContentMarginBottom = 24 });
        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", 28); surface.AddChild(columns);
        bool busy = false;
        var zh = new GpuInitializationPanel("zh", () => busy) { CustomMinimumSize = new(576, 0) };
        var en = new GpuInitializationPanel("en", () => busy) { CustomMinimumSize = new(576, 0) };
        columns.AddChild(zh); columns.AddChild(en); root.AddChild(surface);
        async Task Render(string name)
        {
            for (int i = 0; i < 3; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            RenderingServer.ForceDraw();
            using var image = root.GetViewport().GetTexture().GetImage();
            image.SavePng(Path.Combine(output, name + ".png"));
        }
        await Render("gpu-initialization-failed");
        var button = zh.GetNode<Button>("RetryGpuInitialization");
        Require(!button.Disabled && button.Text == "重试 GPU 初始化", "Chinese retry action available");
        Require(en.GetNode<Label>("GpuInitializationState").Text.Contains("creation failed"), "English failure state");
        Require(zh.GetNode<Label>("GpuInitializationDetails").Text.Contains("CreateLocalRenderingDevice"), "visible failure stage");
        busy = true;
        button.EmitSignal(Button.SignalName.Pressed);
        Require(creations == 1 && tracker.Capture().State == FamilyGpuInitializationState.Failed, "busy search refuses retry");
        busy = false;
        long previousAttempt = tracker.Capture().Attempt;
        button.EmitSignal(Button.SignalName.Pressed);
        var retry = FamilyDeviceProfileFoundation.RetryTask ?? throw new InvalidOperationException("ProbeMissing");
        Require(tracker.Capture().Attempt > previousAttempt, "button starts a fresh explicit creation attempt");
        var timer = Stopwatch.StartNew();
        while (!retry.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(10))
            await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(retry.IsCompletedSuccessfully && retry.Result.Length == 0, "explicit button probe creates and releases a real device");
        Require(tracker.Capture().State == FamilyGpuInitializationState.Available &&
            tracker.Capture().ActiveOwners == 0 && tracker.Capture().LastFailure is not null,
            "success restores eligibility while preserving diagnostic history");

        await using (var owner = new FamilyGpuExecutionOwner())
        {
            try
            {
                await owner.ExecuteAsync(rd => FamilyGpuComputeUtility.CompileShader(rd,
                    "#version 450\nthis_is_not_valid_glsl", "InitializationChecks.BadShader"), CancellationToken.None);
                throw new InvalidOperationException("BadShaderAccepted");
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("FamilyGpu.ShaderCompileFailed:"))
            { Require(true, "bad shader rejected by its own operation"); }
            Require(tracker.Capture().State == FamilyGpuInitializationState.Available && FamilyDeviceProfileFoundation.GpuAvailable,
                "shader failure does not disable compute device");
            uint value = await owner.ExecuteAsync(rd =>
            {
                var owned = new List<Rid>();
                Rid Own(Rid rid) { owned.Add(rid); return rid; }
                try
                {
                    var shader = Own(FamilyGpuComputeUtility.CompileShader(rd,
                        "#version 450\nlayout(local_size_x=1) in; layout(set=0,binding=0,std430) buffer Data { uint values[]; } data; void main(){data.values[0]=42u;}", "InitializationChecks.Control"));
                    var pipeline = Own(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, "InitializationChecks.Control"));
                    var buffer = Own(FamilyGpuComputeUtility.CreateStorageBuffer(rd, new byte[4]));
                    var uniforms = Own(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, new[] { buffer }));
                    long list = rd.ComputeListBegin();
                    try { rd.ComputeListBindComputePipeline(list, pipeline); rd.ComputeListBindUniformSet(list, uniforms, 0); rd.ComputeListDispatch(list, 1, 1, 1); }
                    finally { rd.ComputeListEnd(); }
                    FamilyGpuComputeUtility.SubmitAndSync(rd, "InitializationChecks.Control");
                    return BitConverter.ToUInt32(rd.BufferGetData(buffer));
                }
                finally { FamilyGpuComputeUtility.FreeAll(rd, owned); }
            }, CancellationToken.None);
            Require(value == 42, "same owner executes valid shader after failed shader");
        }
        await Render("gpu-initialization-recovered");
        Require(tracker.Capture().ActiveOwners == 0 && FamilyDeviceProfileFoundation.Capture().FamilyComputeRuntimeObserved,
            "device release preserves successful creation evidence");
        File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new { passed = true, checks,
            backend = RenderingServer.GetCurrentRenderingDriverName(), simulatedCreationFailure = true,
            actualExplicitRetry = true, validControl = 42, affectedGtx1650CompatibilityVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
        surface.QueueFree();
        Console.WriteLine($"GpuInitializationSurface=PASS;checks={checks};shaderIsolation=true;validControl=42");
    }
}
