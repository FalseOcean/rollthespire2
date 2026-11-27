using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

internal static partial class SmallCandidateExactChecks
{
    private static async Task CheckPartyPublicTailAsync(PreparedSearch template)
    {
        const int window = PrivateOrdinalBuffer.Capacity;
        var root = new SearchBatch(37, window + 5);
        ulong[] prefix = [window - 2, window - 1, window, window + 1, window + 4];
        var visits = new List<SearchBatch>();
        // The first seat is more selective; both seats share the same family ID.
        var first = PublicStage("party-public", (_, input, _) =>
        {
            Check(input.IsDense && input.Batch == root, "first seat receives the complete promoted dense window");
            return FamilyCandidateSet.FromSortedAbi1(input.Batch, prefix);
        });
        var second = PublicStage("party-public", (_, input, services) =>
        {
            visits.Add(input.Batch);
            Check(services.BudgetPublicTailBindings && !input.IsDense, "second seat uses ordinary bounded GPU bindings");
            ulong[] local = input.ExportAbi1().ToArray();
            ulong[] expected = input.Batch.BatchBase == root.BatchBase ? [window - 2, window - 1] : [0, 1, 4];
            Check(local.SequenceEqual(expected), "second seat receives ordinals relative to its local window");
            return FamilyCandidateSet.FromSortedAbi1(input.Batch, local.Where(n => n % 2 == 0).ToArray());
        });
        first = first with { WindowCapacity = window, MaximumGpuRootWindow = 0,
            LargeWindowRole = GpuLargeWindowRole.Dense, SupportsStandaloneLargeWindow = true,
            Preparation = first.Preparation with { Survival = FamilySurvivalProjection.Resolved(first.FamilyId, .001, "Fixture") } };
        second = second with { WindowCapacity = window, MaximumGpuRootWindow = 0,
            Preparation = second.Preparation with { Survival = FamilySurvivalProjection.Resolved(second.FamilyId, .99, "Fixture") } };
        var party = PartyFamily.Prepare(template.Model, FilterExecutionPreferences.From(template.RunOptions),
            [new(0, first), new(1, second)]);
        var large = GpuRootWindow.PrepareStandalone(party, 2 * window);
        Check(large.WindowCapacity == 2 * window && large.MaximumGpuRootWindow >= large.WindowCapacity,
            "public multi-seat dense prefix admits the large root window");
        await using var services = new FilterRuntimeServices(requiresGpu: false);
        var execution = large.CreateExecution!(large, services);
        try
        {
            var result = await execution.FilterAsync(services, FamilyCandidateSet.Dense(root), CancellationToken.None);
            Check(result.Batch == root && result.ExportAbi1().Span.SequenceEqual(new ulong[] { window - 2, window, window + 4 }) &&
                visits.SequenceEqual([new(root.BatchBase, window), new(root.BatchBase + window, 5)]),
                "public second seat restores sorted root ordinals on both sides of the 16M boundary");
        }
        finally { await execution.DisposeAsync(services); }

        int compactCalls = 0;
        var compact = PublicStage("full-uint-ordinals", (_, input, context) =>
        {
            compactCalls++;
            Check(context.BudgetPublicTailBindings && input.Batch == root &&
                input.ExportAbi1().Span.SequenceEqual(prefix), "compact GPU input retains the full root span and budget");
            return input;
        });
        var compactSeat = compact.CreateExecution!(compact, services);
        try
        {
            var result = await GpuRootWindow.FilterPublicTailAsync(compactSeat, services,
                FamilyCandidateSet.FromSortedAbi1(root, prefix), CancellationToken.None, supportsWideCompactOrdinals: true);
            Check(compactCalls == 1 && result.ExportAbi1().Span.SequenceEqual(prefix) && !services.BudgetPublicTailBindings,
                "a uint-addressed public seat needs a single dispatch across the 16M boundary");
        }
        finally { await compactSeat.DisposeAsync(services); }
    }
}
