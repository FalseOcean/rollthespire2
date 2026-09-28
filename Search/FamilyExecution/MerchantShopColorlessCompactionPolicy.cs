namespace RolltheSpire2.Search.FamilyExecution;

internal enum MerchantShopColorlessCompactionPath
{
    AtomicAppendHostSort,
    StableOrderedCompaction
}

internal enum MerchantShopColorlessCompactionMode
{
    Auto,
    ForceAtomic,
    ForceStable
}

internal readonly record struct MerchantShopColorlessCompactionDecision(
    MerchantShopColorlessCompactionMode Mode,
    MerchantShopColorlessCompactionPath Path,
    double? AnalyticalFinalSurvival,
    double StableThreshold,
    string Reason)
{
    internal string PhysicalImplementationRevision =>
        Path == MerchantShopColorlessCompactionPath.StableOrderedCompaction
            ? FamilyPhysicalImplementationRevisions.MerchantShopColorlessStableOrderedCompaction
            : FamilyPhysicalImplementationRevisions.MerchantShopColorlessAtomicAppendHostSort;
}

/// <summary>
/// S-private physical implementation policy. It consumes only the already-resolved
/// analytical Final Survival; runtime output, Hardware, Cost and Planner are absent.
/// </summary>
internal static class MerchantShopColorlessCompactionPolicy
{
    internal const double StableThreshold = 0.01d;
    private const string ModeVariable = "RT2_S_COMPACTION_MODE";
    private const string LegacyForceStableVariable = "RT2_S_STABLE_ORDERED_COMPACTION";

    internal static MerchantShopColorlessCompactionDecision Select(FamilySurvivalProjection survival)
    {
        ArgumentNullException.ThrowIfNull(survival);
        MerchantShopColorlessCompactionMode mode = ReadMode();
        double? probability = survival.SurvivalProbability;
        if (mode == MerchantShopColorlessCompactionMode.ForceAtomic)
        {
            return new MerchantShopColorlessCompactionDecision(
                mode, MerchantShopColorlessCompactionPath.AtomicAppendHostSort,
                probability, StableThreshold, "DeveloperOverrideForceAtomic");
        }
        if (mode == MerchantShopColorlessCompactionMode.ForceStable)
        {
            return new MerchantShopColorlessCompactionDecision(
                mode, MerchantShopColorlessCompactionPath.StableOrderedCompaction,
                probability, StableThreshold, "DeveloperOverrideForceStable");
        }
        if (probability is double resolved && resolved >= StableThreshold)
        {
            return new MerchantShopColorlessCompactionDecision(
                mode, MerchantShopColorlessCompactionPath.StableOrderedCompaction,
                resolved, StableThreshold, "AnalyticalFinalSurvivalAtOrAboveThreshold");
        }
        return new MerchantShopColorlessCompactionDecision(
            mode, MerchantShopColorlessCompactionPath.AtomicAppendHostSort,
            probability, StableThreshold,
            probability.HasValue
                ? "AnalyticalFinalSurvivalBelowThreshold"
                : "AnalyticalFinalSurvivalUnresolvedConservativeAtomic");
    }

    private static MerchantShopColorlessCompactionMode ReadMode()
    {
        string? configured = System.Environment.GetEnvironmentVariable(ModeVariable)?.Trim();
        if (!string.IsNullOrEmpty(configured))
        {
            if (Enum.TryParse(configured, ignoreCase: true, out MerchantShopColorlessCompactionMode parsed))
                return parsed;
            throw new InvalidOperationException($"SFamilyCompactionModeInvalid:{configured}");
        }

        string? legacyForceStable = System.Environment.GetEnvironmentVariable(LegacyForceStableVariable)?.Trim();
        return string.Equals(legacyForceStable, "1", StringComparison.Ordinal)
            ? MerchantShopColorlessCompactionMode.ForceStable
            : MerchantShopColorlessCompactionMode.Auto;
    }
}
