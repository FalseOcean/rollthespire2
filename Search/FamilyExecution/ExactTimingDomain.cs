namespace RolltheSpire2.Search.Runtime;

/// <summary>
/// Stable query-domain identity used by current Exact timing evidence.
/// It does not select a physical plan or preserve the retired CostBasedPlannerV1.
/// </summary>
internal enum ExactTimingDomain : byte
{
    Neow = 1,
    Relic = 2,
    AncientOptionPreGate = 3,
    WorldEvent = 4,
    CombatReward = 5
}
