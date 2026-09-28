using System.Reflection;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>Minimal topology-neutral RenderingDevice helpers used by GPU Families.</summary>
internal static class FamilyGpuComputeUtility
{
    private const string VisibleSeedRootHashMarker = "/*__RT2_VISIBLE_SEED_ROOT_HASH__*/";
    private const string VisibleSeedRootHashCommonSuffix = "VisibleSeedRootHashCommon.glsl";

    public static Rid CompileShader(RenderingDevice rd, string sourceText, string name)
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        var source = new RDShaderSource { SourceCompute = sourceText };
        RDShaderSpirV spirV = rd.ShaderCompileSpirVFromSource(source, allowCache: true);
        string error = spirV.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException("FamilyShaderCompile:" + error);
        double compileMs=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        string sourceHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sourceText)));
        string spirvHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(spirV.GetStageBytecode(RenderingDevice.ShaderStage.Compute)));
        started=System.Diagnostics.Stopwatch.GetTimestamp();
        Rid shader=rd.ShaderCreateFromSpirV(spirV, name);
        RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=ShaderModuleReady;name={name};ownerThreadId={System.Environment.CurrentManagedThreadId};sourceSha256={sourceHash};spirvSha256={spirvHash};compileSpirvMs={compileMs:F4};moduleMs={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F4};allowCache=true");
        return shader;
    }

    internal static Rid CreateComputePipeline(RenderingDevice rd,Rid shader)
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        Rid pipeline=rd.ComputePipelineCreate(shader);
        RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundDetail($"searchStartup=true;phase=ComputePipelineReady;ownerThreadId={System.Environment.CurrentManagedThreadId};elapsedMs={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F4}");
        return pipeline;
    }

    public static Rid CreateStorageBuffer(RenderingDevice rd, byte[] bytes)
    {
        if (bytes.Length == 0) bytes = new byte[sizeof(uint)];
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        Rid buffer=rd.StorageBufferCreate(checked((uint)bytes.Length), bytes);
        TraceBuffer(started,bytes.Length);return buffer;
    }

    public static Rid CreateZeroedStorageBuffer(RenderingDevice rd, int byteCount)
    {
        if (byteCount < sizeof(uint)) byteCount = sizeof(uint);
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        Rid buffer=rd.StorageBufferCreate(checked((uint)byteCount), Array.Empty<byte>());
        TraceBuffer(started,byteCount);return buffer;
    }

    private static void TraceBuffer(long started,int bytes) => RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundDetail(
        $"searchStartup=true;phase=BufferReady;ownerThreadId={System.Environment.CurrentManagedThreadId};bytes={bytes};elapsedMs={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F4}");

    public static Rid CreateUniformSet(RenderingDevice rd, Rid shader, IReadOnlyList<Rid> buffers)
    {
        var uniforms = new Godot.Collections.Array<RDUniform>();
        for (int index = 0; index < buffers.Count; index++)
        {
            var uniform = new RDUniform
            {
                UniformType = RenderingDevice.UniformType.StorageBuffer,
                Binding = index
            };
            uniform.AddId(buffers[index]);
            uniforms.Add(uniform);
        }
        return rd.UniformSetCreate(uniforms, shader, 0);
    }

    public static string LoadEmbeddedShader(string suffix)
    {
        Assembly assembly = typeof(FamilyGpuComputeUtility).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource) ??
                              throw new InvalidOperationException("Embedded Family shader missing: " + suffix);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string LoadFamilyShaderWithVisibleSeedRootHash(string suffix)
    {
        string source = LoadEmbeddedShader(suffix);
        if (!source.Contains(VisibleSeedRootHashMarker, StringComparison.Ordinal))
            throw new InvalidOperationException("Family shader root-hash marker missing: " + suffix);
        return source.Replace(
            VisibleSeedRootHashMarker,
            LoadEmbeddedShader(VisibleSeedRootHashCommonSuffix),
            StringComparison.Ordinal);
    }

    public static byte[] ToBytes(uint[] values)
    {
        byte[] bytes = new byte[checked(values.Length * sizeof(uint))];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static byte[] ToBytesNonEmpty(uint[] values) => values.Length == 0 ? new byte[sizeof(uint)] : ToBytes(values);

    public static uint[] FromUInt32Bytes(byte[] bytes)
    {
        if ((bytes.Length & 3) != 0) throw new InvalidDataException("Family GPU uint buffer is not aligned.");
        var output = new uint[bytes.Length / sizeof(uint)];
        Buffer.BlockCopy(bytes, 0, output, 0, bytes.Length);
        return output;
    }

    public static void Update(RenderingDevice rd, Rid buffer, uint[] values, string label)
    {
        byte[] bytes = ToBytes(values);
        UpdateBytes(rd, buffer, bytes, label);
    }

    public static void UpdateBytes(RenderingDevice rd, Rid buffer, byte[] bytes, string label)
    {
        Error error = rd.BufferUpdate(buffer, 0u, checked((uint)bytes.Length), bytes);
        if (error != Error.Ok) throw new InvalidOperationException(label + ":" + error);
    }

    public static void FreeAll(RenderingDevice rd, List<Rid> owned, bool failOnError = false)
    {
        List<Exception>? failures = null;
        for (int index = owned.Count - 1; index >= 0; index--)
        {
            try { rd.FreeRid(owned[index]); }
            catch (Exception ex) { if (failOnError) (failures ??= []).Add(ex); }
        }
        owned.Clear();
        if (failures is not null) throw new AggregateException("PrivateGpu.ResourceReleaseFailed", failures);
    }
}
