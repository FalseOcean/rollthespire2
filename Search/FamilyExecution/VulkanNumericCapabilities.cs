using System.Runtime.InteropServices;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// Physical support, not logical-device feature enablement or successful kernel
// execution. Main and local RenderingDevices can select different physical GPUs.
internal sealed record VulkanNumericCapabilitySnapshot(
    string EnvironmentKey, string Scope, string RenderingBackend, string DeviceName,
    ulong InstanceHandle, ulong PhysicalDeviceHandle, bool? ShaderFloat64, bool? ShaderInt64,
    string Reason, string Details, string LoaderName = "", string CleanupDetails = "")
{
    internal bool FeaturesKnown => ShaderFloat64.HasValue && ShaderInt64.HasValue;
}

internal static class VulkanNumericCapabilities
{
    // Vulkan 1.0 VkPhysicalDeviceFeatures has 55 consecutive VkBool32 fields.
    // VkBool32 is uint32_t, so the complete output is 220 bytes. Zero-based
    // shaderFloat64/shaderInt64 indices are 39/40 (byte offsets 156/160).
    // https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceFeatures.html
    // https://docs.vulkan.org/refpages/latest/refpages/source/VkBool32.html
    internal const int FeatureCount = 55;
    internal const int FeatureBytes = FeatureCount * sizeof(uint);
    internal const int ShaderFloat64Index = 39;
    internal const int ShaderInt64Index = 40;

    // VKAPI_CALL is stdcall on 32-bit Windows and the platform C ABI elsewhere.
    // Winapi selects that platform default; nint preserves dispatchable handles.
    // Android ARM32's Vulkan hard-float convention has no floating parameters
    // or return registers to alter for these two pointer-only signatures.
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint GetInstanceProcAddr(nint instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GetPhysicalDeviceFeatures(nint physicalDevice, nint features);

    // Called after main-thread environment capture. All RD internals and the
    // native query run together on the render thread while its device is alive.
    // No device/queue/resource is created, submitted, freed or taken over here.
    internal static Task<VulkanNumericCapabilitySnapshot> CaptureAsync(string environmentKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentKey);
        const string scope = "ObservedMainRenderingDevice";
        var completion = new TaskCompletionSource<VulkanNumericCapabilitySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            RenderingServer.CallOnRenderThread(Callable.From(() =>
            {
                try
                {
                    if (!RenderingServer.IsOnRenderThread())
                    {
                        completion.TrySetResult(Unknown(environmentKey, scope, "Unknown", "RenderThreadNotCurrent", "No RD handles were read."));
                        return;
                    }
                    string backend = RenderingServer.GetCurrentRenderingDriverName();
                    if (!string.Equals(backend, "vulkan", StringComparison.OrdinalIgnoreCase))
                    {
                        completion.TrySetResult(Unknown(environmentKey, scope, backend, "BackendIsNotVulkan", "No native loader or RD handles were accessed."));
                        return;
                    }
                    var device = RenderingServer.GetRenderingDevice();
                    completion.TrySetResult(device is null
                        ? Unknown(environmentKey, scope, backend, "RenderingDeviceUnavailable", "GetRenderingDevice returned null.")
                        : CaptureOnDeviceOwnerThread(device, environmentKey, backend, scope));
                }
                catch (Exception error)
                {
                    completion.TrySetResult(Unknown(environmentKey, scope, "Unknown", "RenderThreadCaptureFailed", error.ToString()));
                }
            }));
        }
        catch (Exception error)
        {
            completion.TrySetResult(Unknown(environmentKey, scope, "Unknown", "RenderThreadDispatchFailed", error.ToString()));
        }
        return completion.Task;
    }

    // The caller must already own this RD's thread and keep it alive for this
    // synchronous call. Existing local owners can use this without a second
    // device creation. A main-RD caller must use CallOnRenderThread instead.
    internal static VulkanNumericCapabilitySnapshot CaptureOnDeviceOwnerThread(RenderingDevice device,
        string environmentKey, string renderingBackend, string scope = "ObservedLocalRenderingDevice")
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentKey);
        if (!string.Equals(renderingBackend, "vulkan", StringComparison.OrdinalIgnoreCase))
            return Unknown(environmentKey, scope, renderingBackend, "BackendIsNotVulkan", "No native loader or RD handles were accessed.");

        nint library = 0, storage = 0;
        string loader = "", name = "", stage = "ReadRenderingDeviceHandles";
        ulong instance = 0, physical = 0;
        VulkanNumericCapabilitySnapshot result;
        var cleanup = new List<string>();
        try
        {
            name = device.GetDeviceName();
            // Godot 4.5.1 rendering_device.cpp get_driver_resource has a render-
            // thread guard. Vulkan returns VkInstance/VkPhysicalDevice for these
            // two enum values; RID and index are ignored for them.
            // https://github.com/godotengine/godot/blob/4.5.1-stable/servers/rendering/rendering_device.cpp
            // https://github.com/godotengine/godot/blob/4.5.1-stable/drivers/vulkan/rendering_device_driver_vulkan.cpp
            instance = device.GetDriverResource(RenderingDevice.DriverResource.TopmostObject, default, 0);
            physical = device.GetDriverResource(RenderingDevice.DriverResource.PhysicalDevice, default, 0);
            if (instance == 0 || physical == 0 || (IntPtr.Size == 4 && ((instance | physical) >> 32) != 0))
                throw new InvalidOperationException($"InvalidVulkanDispatchableHandles:instance=0x{instance:x};physicalDevice=0x{physical:x};pointerBytes={IntPtr.Size}");

            stage = "LoadVulkanLoader";
            // Same standard loader names used by Godot's bundled volk. Only
            // acquire/release our own loader reference; never change DLL paths,
            // copy a loader, or unload a handle borrowed from Godot.
            // https://github.com/godotengine/godot/blob/4.5.1-stable/thirdparty/volk/volk.c
            string[] loaders = OperatingSystem.IsWindows() ? ["vulkan-1.dll"] :
                OperatingSystem.IsAndroid() ? ["libvulkan.so"] :
                OperatingSystem.IsLinux() ? ["libvulkan.so.1", "libvulkan.so"] : [];
            foreach (string candidate in loaders)
                if (NativeLibrary.TryLoad(candidate, out library)) { loader = candidate; break; }
            if (library == 0)
                throw new DllNotFoundException("NativeLibrary.TryLoad returned false for the standard Vulkan loader; candidates=" + string.Join(',', loaders));

            stage = "ResolveVulkanQuery";
            if (!NativeLibrary.TryGetExport(library, "vkGetInstanceProcAddr", out nint address) || address == 0)
                throw new EntryPointNotFoundException("vkGetInstanceProcAddr was not exported by " + loader);
            var getProc = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddr>(address);
            // Resolve against this RD's VkInstance, not a null instance or an
            // independently enumerated/default adapter.
            // https://docs.vulkan.org/refpages/latest/refpages/source/vkGetInstanceProcAddr.html
            nint queryAddress = getProc(unchecked((nint)instance), "vkGetPhysicalDeviceFeatures");
            if (queryAddress == 0) throw new EntryPointNotFoundException("vkGetPhysicalDeviceFeatures was not resolved for the observed VkInstance.");
            var query = Marshal.GetDelegateForFunctionPointer<GetPhysicalDeviceFeatures>(queryAddress);

            stage = "QueryPhysicalDeviceFeatures";
            storage = Marshal.AllocHGlobal(FeatureBytes);
            int[] words = Enumerable.Repeat(unchecked((int)0xcccccccc), FeatureCount).ToArray();
            Marshal.Copy(words, 0, storage, FeatureCount);
            // The official function writes all 55 fields; no GPU work, wait,
            // instance creation or feature enablement is performed.
            // https://docs.vulkan.org/refpages/latest/refpages/source/vkGetPhysicalDeviceFeatures.html
            query(unchecked((nint)physical), storage);
            Marshal.Copy(storage, words, 0, FeatureCount);
            for (int i = 0; i < words.Length; i++)
                if (words[i] is not 0 and not 1)
                    throw new InvalidDataException($"InvalidVkBool32Output:index={i};raw=0x{unchecked((uint)words[i]):x8};expected=VK_FALSE(0)orVK_TRUE(1)");
            GC.KeepAlive(getProc);
            GC.KeepAlive(query);
            result = new(environmentKey, scope, renderingBackend, name, instance, physical,
                words[ShaderFloat64Index] == 1, words[ShaderInt64Index] == 1, "VulkanPhysicalDeviceFeaturesQueried",
                $"shaderFloat64={words[ShaderFloat64Index]};shaderInt64={words[ShaderInt64Index]};bytes={FeatureBytes};physicalSupportOnly=true;logicalEnablementUnverified=true;kernelExecutionUnverified=true", loader);
        }
        catch (Exception error)
        {
            result = new(environmentKey, scope, renderingBackend, name, instance, physical, null, null,
                stage + "Failed", error.ToString(), loader);
        }
        finally
        {
            if (storage != 0)
                try { Marshal.FreeHGlobal(storage); } catch (Exception error) { cleanup.Add("FreeFeatureStorage:" + error); }
            if (library != 0)
                try { NativeLibrary.Free(library); } catch (Exception error) { cleanup.Add("ReleaseVulkanLoaderReference:" + error); }
        }
        return result with { CleanupDetails = string.Join("\n", cleanup) };
    }

    private static VulkanNumericCapabilitySnapshot Unknown(string environmentKey, string scope, string backend,
        string reason, string details) => new(environmentKey, scope, backend, "", 0, 0, null, null, reason, details);
}
