using System.Runtime.InteropServices;
using System.Text;

namespace RolltheSpire2.Search.Runtime;

internal sealed record SearchGpuPowerReading(
    DateTimeOffset SampledAt,
    string AdapterName,
    double? AverageWatts,
    double? InstantWatts,
    double? ConfiguredLimitWatts,
    double? EnforcedLimitWatts,
    bool? SoftwarePowerCapActive,
    uint? GraphicsClockMhz,
    uint? TemperatureC,
    string? UnavailableReason,
    string? Details = null);

// The caller supplies the search RenderingDevice's name. Only a unique match
// in NVML's enumeration can be sampled. No native access happens on construction.
internal sealed class SearchGpuPowerSensor(string adapterName)
{
    // NVML calls cannot be canceled once in the driver. Limit how long the
    // caller waits and leave the slot with that worker until it really exits.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(1);
    private static int _workerActive;
    private readonly string _adapterName = adapterName.Trim();

    internal async Task<SearchGpuPowerReading> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return Unavailable("NvmlWindowsOnly");
        if (_adapterName.Length == 0) return Unavailable("SearchAdapterNameUnavailable");
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(token);
        CancellationToken probeToken = probe.Token;
        if (Interlocked.CompareExchange(ref _workerActive, 1, 0) != 0)
            return Unavailable("NvmlProbeInProgress");

        var sample = Task.Run(() =>
        {
            try
            {
                probeToken.ThrowIfCancellationRequested();
                return ReadOnWorker(probeToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return Unavailable("NvmlProbeFailed", error.ToString());
            }
            finally { Volatile.Write(ref _workerActive, 0); }
        });

        try { return await sample.WaitAsync(ReadTimeout, token).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            probe.Cancel();
            return Unavailable("NvmlProbeTimedOut");
        }
    }

    private SearchGpuPowerReading ReadOnWorker(CancellationToken token)
    {
        if (!NativeLibrary.TryLoad("nvml.dll", typeof(SearchGpuPowerSensor).Assembly,
                DllImportSearchPath.System32, out nint library))
            return Unavailable("NvmlSystemLibraryUnavailable");

        try
        {
            token.ThrowIfCancellationRequested();
            var shutdown = GetExport<Control>(library, "nvmlShutdown");
            int status = GetExport<Control>(library, "nvmlInit_v2")();
            if (status != 0) return Unavailable("NvmlInitializationFailed", $"nvmlInit_v2={status}");

            SearchGpuPowerReading reading;
            int shutdownStatus;
            try { reading = ReadInitialized(library, token); }
            finally { shutdownStatus = shutdown(); }

            if (shutdownStatus == 0) return reading;
            string cleanup = $"nvmlShutdown={shutdownStatus}";
            return reading with { Details = reading.Details is null ? cleanup : $"{reading.Details}, {cleanup}" };
        }
        finally { NativeLibrary.Free(library); }
    }

    private SearchGpuPowerReading ReadInitialized(nint library, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int status = GetExport<DeviceCount>(library, "nvmlDeviceGetCount_v2")(out uint count);
        if (status != 0) return Unavailable("NvmlAdapterEnumerationFailed", $"nvmlDeviceGetCount_v2={status}");

        var getHandle = GetExport<DeviceByIndex>(library, "nvmlDeviceGetHandleByIndex_v2");
        var getName = GetExport<DeviceName>(library, "nvmlDeviceGetName");
        var nameBuffer = new byte[96]; // NVML_DEVICE_NAME_V2_BUFFER_SIZE
        nint matchedDevice = 0;
        for (uint index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            status = getHandle(index, out nint device);
            if (status != 0) return Unavailable("NvmlAdapterEnumerationFailed", $"nvmlDeviceGetHandleByIndex_v2[{index}]={status}");
            status = getName(device, nameBuffer, (uint)nameBuffer.Length);
            if (status != 0) return Unavailable("NvmlAdapterEnumerationFailed", $"nvmlDeviceGetName[{index}]={status}");
            int end = Array.IndexOf(nameBuffer, (byte)0);
            if (end < 0) return Unavailable("NvmlAdapterEnumerationFailed", $"nvmlDeviceGetName[{index}]=UnterminatedName");
            string name = Encoding.UTF8.GetString(nameBuffer, 0, end).Trim();
            if (!string.Equals(name, _adapterName, StringComparison.OrdinalIgnoreCase)) continue;
            if (matchedDevice != 0) return Unavailable("NvmlSearchAdapterNameAmbiguous");
            matchedDevice = device;
        }
        if (matchedDevice == 0) return Unavailable("NvmlSearchAdapterNotFound");

        token.ThrowIfCancellationRequested();
        var details = new List<string>();
        double? average = null;
        double? instant = null;
        if (TryExport<FieldValues>(library, "nvmlDeviceGetFieldValues") is { } getFields)
        {
            // NVML_FI_DEV_POWER_AVERAGE and NVML_FI_DEV_POWER_INSTANT, GPU scope 0.
            // nvmlDeviceGetPowerUsage is architecture-dependent, so cannot stand in for either field.
            FieldValue[] power = [new() { FieldId = 185 }, new() { FieldId = 186 }];
            status = getFields(matchedDevice, power.Length, power);
            if (status == 0)
            {
                average = ReadWatts(power[0], details);
                instant = ReadWatts(power[1], details);
            }
            else details.Add($"nvmlDeviceGetFieldValues={status}");
        }
        else details.Add("nvmlDeviceGetFieldValues=MissingExport");

        double? configuredLimit = ReadDeviceUInt(library, matchedDevice, "nvmlDeviceGetPowerManagementLimit", details, token) / 1000d;
        double? enforcedLimit = ReadDeviceUInt(library, matchedDevice, "nvmlDeviceGetEnforcedPowerLimit", details, token) / 1000d;
        bool? softwareCap = ReadSoftwarePowerCap(library, matchedDevice, details, token);
        uint? clock = ReadDeviceUInt(library, matchedDevice, "nvmlDeviceGetClockInfo", details, token, kind: 0); // NVML_CLOCK_GRAPHICS
        uint? temperature = ReadDeviceUInt(library, matchedDevice, "nvmlDeviceGetTemperature", details, token, kind: 0); // NVML_TEMPERATURE_GPU

        return new(DateTimeOffset.UtcNow, _adapterName, average, instant, configuredLimit, enforcedLimit,
            softwareCap, clock, temperature,
            average is null && instant is null && enforcedLimit is null ? "NvmlPowerMetricsUnavailable" : null,
            details.Count > 0 ? string.Join(", ", details) : null);
    }

    private static double? ReadWatts(FieldValue field, List<string> details)
    {
        if (field.Status == 0 && field.ValueType == 1) // NVML_VALUE_TYPE_UNSIGNED_INT
            return (uint)field.Value / 1000d;
        details.Add($"nvmlDeviceGetFieldValues[{field.FieldId}]={field.Status}, valueType={field.ValueType}");
        return null;
    }

    private static uint? ReadDeviceUInt(nint library, nint device, string name, List<string> details,
        CancellationToken token, uint? kind = null)
    {
        token.ThrowIfCancellationRequested();
        if (kind is uint selector && TryExport<DeviceUIntByKind>(library, name) is { } byKind)
        {
            int status = byKind(device, selector, out uint value);
            if (status == 0) return value;
            details.Add($"{name}={status}");
        }
        else if (kind is null && TryExport<DeviceUInt>(library, name) is { } query)
        {
            int status = query(device, out uint value);
            if (status == 0) return value;
            details.Add($"{name}={status}");
        }
        else details.Add($"{name}=MissingExport");
        return null;
    }

    private static bool? ReadSoftwarePowerCap(nint library, nint device, List<string> details, CancellationToken token)
    {
        const ulong softwarePowerCap = 0x4; // nvmlClocksEventReasonSwPowerCap
        token.ThrowIfCancellationRequested();
        ulong? supported = ReadClockReasons(library, device, "Supported", details);
        if (supported is null) return null;
        if ((supported.Value & softwarePowerCap) == 0)
        {
            details.Add("nvmlClocksEventReasonSwPowerCap=Unsupported");
            return null;
        }
        token.ThrowIfCancellationRequested();
        ulong? current = ReadClockReasons(library, device, "Current", details);
        return current is ulong reasons ? (reasons & softwarePowerCap) != 0 : null;
    }

    private static ulong? ReadClockReasons(nint library, nint device, string selection, List<string> details)
    {
        string name = $"nvmlDeviceGet{selection}ClocksEventReasons";
        var query = TryExport<DeviceULong>(library, name);
        if (query is null)
        {
            name = $"nvmlDeviceGet{selection}ClocksThrottleReasons"; // Earlier name for the same bitmask.
            query = TryExport<DeviceULong>(library, name);
        }
        if (query is null) details.Add($"{name}=MissingExport");
        else
        {
            int status = query(device, out ulong value);
            if (status == 0) return value;
            details.Add($"{name}={status}");
        }
        return null;
    }

    private SearchGpuPowerReading Unavailable(string reason, string? details = null) =>
        new(DateTimeOffset.UtcNow, _adapterName, null, null, null, null, null, null, null, reason, details);

    private static T GetExport<T>(nint library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static T? TryExport<T>(nint library, string name) where T : Delegate =>
        NativeLibrary.TryGetExport(library, name, out nint address) ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;

    // Windows NVML nvmlFieldValue_t. nvmlValue_t is an eight-byte union.
    // https://docs.nvidia.com/deploy/nvml-api/latest/api/structnvmlFieldValue__t.html
    [StructLayout(LayoutKind.Sequential)]
    private struct FieldValue
    {
        public uint FieldId;
        public uint ScopeId;
        public long Timestamp;
        public long LatencyUsec;
        public int ValueType;
        public int Status;
        public ulong Value;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Control();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceCount(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceByIndex(uint index, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceName(nint device, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] name, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FieldValues(nint device, int count, [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] FieldValue[] values);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceUInt(nint device, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceUIntByKind(nint device, uint kind, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceULong(nint device, out ulong value);
}
