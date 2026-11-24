namespace RolltheSpire2.Search.Contracts;

// Execution policy, never part of the authored map predicate. Missing legacy
// settings preserve full generation. A running request owns an immutable copy.
public sealed record MapLossyOptions(bool Enabled = false, int GpuBatchSize = 16384)
{
    public const int DefaultGpuBatchSize = 16384;
    public const string CurrentStrategy = "AssignmentRound1.Properties9.Drawing5.Multiword.20261028.v3";
    public static MapLossyOptions Default { get; } = new();
    public static IReadOnlyList<int> SupportedGpuBatchSizes { get; } =
        Array.AsReadOnly(new[] { 8192, 16384, 32768, 65536 });

    public MapLossyOptions Normalize() => this with
    {
        GpuBatchSize = SupportedGpuBatchSizes.Contains(GpuBatchSize) ? GpuBatchSize : DefaultGpuBatchSize
    };

    public string Identity => Enabled ? CurrentStrategy + ":batch=" + GpuBatchSize : "Full";
}
