using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.FamilyExecution;

// Candidate-local value state. It never crosses ABI1 and contains no Relic Bag.
internal readonly record struct NeowReplayObservation(byte First, byte Second, byte Curse,
    byte BonesFirst, byte BonesSecond, Beta110FastRng RewardsAfterBones, bool IdentityPass);

// Mode dispatch for prerequisite consumers. Production N binds its matcher once.
internal static class NeowFamilyReplay
{
    internal static readonly ulong RewardsHash = NeowSingleplayerReplay.RewardsHash;
    internal static ReadOnlySpan<byte> Positives => NeowPartyReplay.Positives;
    internal static Func<ulong, bool> Bind(NeowReplayPlan plan)
    {
        plan.ValidateExecutionScope();
        return plan.Authority.PlayersCount == 1
            ? root => NeowSingleplayerReplay.Matches(root, plan)
            : root => NeowPartyReplay.Matches(root, plan);
    }
    internal static NeowReplayObservation Observe(ulong root, NeowReplayPlan plan) => plan.Authority.PlayersCount == 1
        ? NeowSingleplayerReplay.Observe(root, plan) : NeowPartyReplay.Observe(root, plan);
    internal static byte ObserveCurse(ulong root, NeowReplayPlan plan, out Beta110FastRng rng) => plan.Authority.PlayersCount == 1
        ? NeowSingleplayerReplay.ObserveCurse(root, plan, out rng) : NeowPartyReplay.ObserveCurse(root, plan, out rng);
    internal static bool Matches(ulong root, NeowReplayPlan plan) => plan.Authority.PlayersCount == 1
        ? NeowSingleplayerReplay.Matches(root, plan) : NeowPartyReplay.Matches(root, plan);
    internal static IEnumerable<(byte First, byte Second)> Routes(NeowReplayPlan plan, NeowReplayObservation observation) => plan.Authority.PlayersCount == 1
        ? NeowSingleplayerReplay.Routes(plan, observation) : NeowPartyReplay.Routes(plan, observation);
    internal static bool Route(ulong root, NeowReplayPlan plan, byte first, byte second, Beta110FastRng afterBones) => plan.Authority.PlayersCount == 1
        ? NeowSingleplayerReplay.Route(root, plan, first, second, afterBones)
        : NeowPartyReplay.Route(root, plan, first, second, afterBones);
}
