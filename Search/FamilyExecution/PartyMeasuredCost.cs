using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.FamilyExecution;

// A bounded local envelope for the current family-major production path. These
// three public ABI1 timings include each wrapper's inner submits, syncs and final
// readback; private intermediate ordinals are deliberately not charged again.
// They are not peak throughput or a cross-device reference table.
internal static class PartyMeasuredCost
{
    private sealed record Anchor(double CpuNs, double CpuFloorMs, double D3d12FloorMs,
        double D3d12Ns, double D3d12SetupMs, double VulkanFloorMs, double VulkanNs,
        double VulkanSetupMs, double BaselineOutputRate, double BaselineWork);

    private static readonly IReadOnlyDictionary<string, Anchor> Anchors = new Dictionary<string, Anchor>
    {
        ["N.Neow"] = new(2000, .35, 5.2, 1.8, 650, 12, 4.5, 120, .0001, 4),
        ["S.MerchantShopColorless"] = new(67, .001, 9.3, .4, 300, 66, .5, 23, .00001, 35),
        ["R.Relic"] = new(840, .001, 8.5, .7, 1000, 13, 2, 20, .00001, 206),
        ["C.CombatReward"] = new(1620, .04, 2.4, 4.5, 6500, 8.3, 3.8, 34, .0001, 1559),
        ["W.World"] = new(7070, .02, 1.2, 17.3, 500, 8.3, 22, 9, .29, 1),
        ["A.AncientOption"] = new(435, .001, 4.7, .2, 1100, 8.3, .2, 18, .012, 1),
        ["E.EventResult"] = new(136, .001, 2.5, .9, 350, 8.3, .1, 11, .0001, 1)
    };

    internal static (double Ms, double SetupMs, string Evidence)? Price(
        string familyId, double inputs, int stages, bool gpu, bool privateChain,
        int capacity, double work, double survival, int cpuWorkers = 1)
    {
        if (inputs < 0 || !double.IsFinite(inputs) || stages < 1 || capacity < 1 ||
            work <= 0 || !double.IsFinite(work) || survival is < 0 or > 1 ||
            !Anchors.TryGetValue(familyId, out Anchor? anchor)) return null;
        var device = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        if (gpu)
        {
            if (device.RenderingBackend is not ("d3d12" or "vulkan") ||
                !device.GpuIdentity.Contains("RTX 4060 Laptop GPU", StringComparison.OrdinalIgnoreCase) ||
                stages > 1 && !privateChain) return null;
        }
        else if (!device.CpuIdentity.Contains("Intel64 Family 6 Model 183", StringComparison.Ordinal)) return null;

        double scale = work / anchor.BaselineWork;
        if (scale is < .25 or > 16) return null;
        double outputExtra = inputs * Math.Max(0, survival - anchor.BaselineOutputRate);
        if (!gpu)
        {
            // N is the only wrapper whose selected CPU worker count changes this
            // measured path. P8 has a real small-window regression, so no linear
            // worker extrapolation is used. Other party CPU stages select P1.
            double ns = familyId == "N.Neow" ? cpuWorkers switch
            {
                1 => 5800,
                >= 2 and <= 4 => 2000,
                >= 5 and <= 8 => 8000,
                _ => double.NaN
            } : cpuWorkers == 1 ? anchor.CpuNs : double.NaN;
            if (!double.IsFinite(ns)) return null;
            double ms = anchor.CpuFloorMs + inputs * ns * scale / 1e6 + outputExtra * 8 / 1e6;
            return (ms, 0, "LocalMeasuredCPU;Beta111FourSeatFamily;64And8192Roots;" +
                "P1P4P8Compared;SteadyInvokeAsyncAbi1;ShapeWorkEnvelope;OutputDelta;" +
                "CpuIdentity=" + device.CpuIdentity);
        }
        int referenceStages = familyId == "W.World" ? 1 : 4;
        bool vulkan = device.RenderingBackend == "vulkan";
        double referenceFloor = vulkan ? anchor.VulkanFloorMs : anchor.D3d12FloorMs;
        double slope = vulkan ? anchor.VulkanNs : anchor.D3d12Ns;
        double floor = stages == referenceStages ? referenceFloor :
            1 + (referenceFloor - 1) * stages / referenceStages;
        double windows = inputs <= 0 ? 0 : Math.Ceiling(inputs / capacity);
        double gpuMs = windows * floor + inputs * slope * scale / 1e6 +
            outputExtra * 25 / 1e6;
        double setup = (vulkan ? anchor.VulkanSetupMs : anchor.D3d12SetupMs) *
            (familyId == "W.World" ? 1 : stages / 4d);
        return (gpuMs, setup,
            "LocalMeasuredGPU;RTX4060Laptop;Backend=" + device.RenderingBackend + ";Beta111FourSeatFamily;" +
            "64_262144_1048576Roots;ThreeSteadyInvokeAsyncAbi1;ColdSetupBound;" +
            "PerWindowSyncFloor;ShapeWorkEnvelope;FinalPublicOutputDelta;" +
            "PrivateIntermediatePayloadReadbackBytes=0");
    }
}
