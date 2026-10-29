using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.FamilyExecution;

internal static class ShionNeowOptimizationChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Require(bool condition, string name) { checks++; if (!condition) throw new InvalidDataException("ShionN." + name); }
        Require(NeowGpuOptimizations.Parse(null) == NeowGpuOptimizations.Production, "DefaultOn");
        Require(NeowGpuOptimizations.Parse("off") == default, "ExplicitOff");
        Require(NeowGpuOptimizations.Production.LeafyLocalPrefix, "LocalPrefixOwnerTrialDefaultOn");
        Require(NeowGpuOptimizations.Parse("on").LeafyLocalPrefix && NeowGpuOptimizations.Parse("").LeafyLocalPrefix &&
            NeowGpuOptimizations.Parse(" ").LeafyLocalPrefix, "LocalPrefixNormalLaunchDefaultOn");
        var localPrefix = NeowGpuOptimizations.Parse("leafy-local-prefix");
        Require(localPrefix.LeafyLocalPrefix && !localPrefix.LeafyLazy && !localPrefix.DrawIntervals &&
            !localPrefix.GpuPrefix && !localPrefix.CurseFirst, "LocalPrefixIsolatedOption");
        var combinedPrefix = NeowGpuOptimizations.Parse("on,leafy-local-prefix");
        Require(combinedPrefix == (NeowGpuOptimizations.Production with { LeafyLocalPrefix = true }), "LocalPrefixProductionComparison");
        Require(NeowGpuOptimizations.Parse("leafy-local-prefix,on") == NeowGpuOptimizations.Production, "OnRestoresProduction");
        foreach (string mode in new[] { "leafy-lazy", "draw-intervals", "gpu-prefix", "n-curse-first", "leafy-local-prefix" })
            Require(NeowGpuOptimizations.Parse(mode).Revision.Length > 0, "Identity." + mode);
        try { NeowGpuOptimizations.Parse("missing"); throw new InvalidDataException("ShionN.UnknownAccepted"); }
        catch (InvalidOperationException) { checks++; }

        // Every bucket boundary, its predecessor, duplicates, empty and complete
        // sets exercise the exact GPU integer map; no CPU binary64 substitution.
        foreach (int bound in new[] { 1, 2, 3, 7, 10, 64, 131, 4096 })
        {
            foreach (int modulus in new[] { 1, 2, 3, 7 })
            {
                Require(NeowStaticDrawGateCompiler.TryCompileRanks(bound, i => i % modulus == 0,
                    bound, out var gate), "CompileRanks");
                for (int rank = 0; rank <= bound; rank++)
                {
                    ulong boundary = NeowStaticDrawGateCompiler.Boundary(rank, bound);
                    foreach (ulong m in new[] { boundary, boundary == 0 ? 0 : boundary - 1 })
                    {
                        bool expected = m < NeowStaticDrawGateCompiler.MantissaSpace &&
                            (int)(((UInt128)m * (uint)bound) >> 53) % modulus == 0;
                        Require(gate!.Contains(m) == expected, "BoundaryMembership");
                    }
                }
            }
        }
        Require(NeowStaticDrawGateCompiler.TryCompilePool([9, 3, 9, 4, 9], 5, [9u], 3, out var duplicates)
            && duplicates!.Intervals.Count == 3, "DuplicatePoolPositions");
        Require(!NeowStaticDrawGateCompiler.TryCompilePool([9, 3, 9, 4, 9], 5, [9u], 2, out _), "ExplicitIntervalBudget");

        foreach (ulong start in new[] { 0UL, 1UL, NeowPrefixHashCache.BlockSize - 7UL,
            NeowPrefixHashCache.BlockSize - 1UL, NeowPrefixHashCache.SpaceSize - (1UL << 24) })
        {
            uint[] table = NeowPrefixHashCache.BuildTable(start, 1 << 24);
            Require(table.Length <= NeowPrefixHashCache.MaximumTableWords, "BoundedTable");
            foreach (int offset in new[] { 0, 1, 6, 7, 8, 511, 512, 513, 1_336_335, 1_336_336, (1 << 24) - 1 })
            {
                ulong ordinal = start + (uint)offset;
                uint position = table[0] + (uint)offset;
                int row = (int)(position / NeowPrefixHashCache.BlockSize), at = 4 + row * 4;
                ulong prefix = table[at] | ((ulong)table[at + 1] << 32);
                ulong hash = NeowPrefixHashCache.ComputeRootHashFromPrefix(prefix,
                    NeowPrefixHashCache.EncodeTail4(position % NeowPrefixHashCache.BlockSize));
                Require(hash == XxHash64.HashUtf8(VisibleSeed(ordinal), 0), "PrefixHashFullSeedParity");
            }
        }
        Require(NeowPrefixHashCache.BuildTable(NeowPrefixHashCache.SpaceSize, 0).Length == 4, "EmptyAtEnd");
        var meta = new uint[90];
        meta[6] = 1; meta[8] = 2; meta[12] = meta[13] = 255; meta[25] = 0; meta[26] = 1;
        meta[27] = Beta110FastRelicCatalog.NewLeaf; meta[28] = Beta110FastRelicCatalog.LeafyPoultice;
        meta[56] = 2; meta[66] = meta[67] = 1; meta[82] = 2; meta[83] = 1;
        var pools = new uint[34];
        pools[26] = 0; pools[27] = 1; pools[28] = 0; pools[29] = 0;
        pools[32] = 0; pools[33] = 2;
        var plan = new NeowFamilyGpuPlan(meta, pools, [101, 202], [], [], [meta[27], meta[28]], new uint[226]);
        var ordinaryLeafy = plan with { Meta = (uint[])meta.Clone() };
        ordinaryLeafy.Meta[56] = ordinaryLeafy.Meta[66] = 0;
        ordinaryLeafy.Meta[70] = 1;
        foreach (var numeric in new[] { NeowGpuNumericalVariant.NativeFloat64, NeowGpuNumericalVariant.IntegerUnitFloat })
        {
            var numericLeafy = ordinaryLeafy with { NumericalVariant = numeric };
            Require(localPrefix.Applicable(numericLeafy, false).Revision == ".LeafyDenseLocalPrefix.20261027.v1", "LocalPrefixDenseIdentity." + numeric);
            Require(combinedPrefix.Applicable(numericLeafy, false) ==
                (NeowGpuOptimizations.Production.Applicable(numericLeafy, false) with { LeafyLocalPrefix = true }), "LocalPrefixRetainsProduction." + numeric);
            Require(localPrefix.Applicable(numericLeafy, true) == default, "LocalPrefixCompactOff." + numeric);
            Require(combinedPrefix.Applicable(numericLeafy, true) == NeowGpuOptimizations.Production.Applicable(numericLeafy, true), "LocalPrefixCompactIdentity." + numeric);
            Require(localPrefix.Applicable(numericLeafy, false, ordinary: false) == default, "LocalPrefixCompositeOff." + numeric);
        }
        foreach (string excluded in new[] { "staged", "party", "non-leafy" })
        {
            var excludedPlan = ordinaryLeafy with { Meta = (uint[])ordinaryLeafy.Meta.Clone() };
            if (excluded == "staged") excludedPlan.Meta[66] = 1;
            if (excluded == "party") excludedPlan.Meta[6] = 2;
            if (excluded == "non-leafy") excludedPlan.Meta[70] = 0;
            Require(localPrefix.Applicable(excludedPlan, false) == default, "LocalPrefixExcluded." + excluded);
            Require(combinedPrefix.Applicable(excludedPlan, false) == NeowGpuOptimizations.Production.Applicable(excludedPlan, false), "LocalPrefixExcludedIdentity." + excluded);
        }
        var order = NeowInternalFilterOrder.Compile(plan);
        Require(order.Enabled && order.NicheAdvances.SequenceEqual(new uint[] { 1 }), "FinalCurseFixedFootprint");
        Require(order.Source.Contains("final_curse_matches", StringComparison.Ordinal), "CanonicalFinalPredicate");
        var unknown = plan with { Bones = [250, meta[28]], Meta = (uint[])meta.Clone() };
        unknown.Meta[27] = 250;
        Require(!NeowInternalFilterOrder.Compile(unknown).Enabled, "UnknownFootprintRetainsCanonical");
        var upgrades = plan with { HasAuthoredUpgrades = true, Bones = [Beta110FastRelicCatalog.SmallCapsule, meta[28]], Meta = (uint[])meta.Clone(), Conditions = new uint[226] };
        upgrades.Meta[27] = Beta110FastRelicCatalog.SmallCapsule;
        upgrades.Conditions[192] = uint.MaxValue;
        Require(!NeowInternalFilterOrder.Compile(upgrades).Enabled, "UnknownAuthoredAdvanceRetainsCanonical");
        Require(NeowGpuOptimizations.Parse("n-curse-first").Applicable(plan, true).Revision.Length == 0, "CompactCanonicalIdentity");
        Console.WriteLine($"ShionNeowOptimization=PASS;checks={checks};gpuMappingOnly=true;nativeGpuNotRun=true");
        return 0;
    }

    private static string VisibleSeed(ulong ordinal)
    {
        const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        Span<char> seed = stackalloc char[12];
        for (int i = 11; i >= 0; i--) { seed[i] = alphabet[(int)(ordinal % 34)]; ordinal /= 34; }
        return new(seed);
    }
}
