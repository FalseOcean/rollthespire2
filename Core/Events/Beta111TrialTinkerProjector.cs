using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

internal static class Beta111TrialTinkerProjector
{
    internal sealed record TinkerOption(int CardType, IReadOnlyList<int> Riders);

    internal static int TrialCase(ulong root, int slot) =>
        Beta111EventRules.TrialCase(EventRng(root, slot, "TRIAL"));

    internal static IReadOnlyList<TinkerOption> TinkerOptions(ulong root, int slot)
    {
        var rng = EventRng(root, slot, "TINKER_TIME");
        int[] types = Beta111EventRules.TinkerTypeOffers(rng);
        // Each selected type is an alternative from the current post-type checkpoint.
        return types.Select(type => new TinkerOption(type,
            Beta111EventRules.TinkerRiderOffers(rng.Clone(), type))).ToArray();
    }

    internal static bool TinkerContains(ulong root, int slot, int type, int? rider)
    {
        var rng = EventRng(root, slot, "TINKER_TIME");
        if (!Beta111EventRules.TinkerTypeOffers(rng).Contains(type)) return false;
        return rider is null || Beta111EventRules.TinkerRiderOffers(rng, type).Contains(rider.Value);
    }

    private static Xoshiro256StarStar EventRng(ulong root, int slot, string entry) =>
        new(Beta109WorldRng.DeriveEventLocalSeed(root, slot, false, entry));
}
