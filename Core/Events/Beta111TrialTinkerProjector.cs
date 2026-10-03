using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

// Trial's cosmetic entrant number uses Rng.Chaotic, not the event-local stream.
// Tinker TakeRandom(2) is a full unstable shuffle of three, then Take(2).
internal static class Beta111TrialTinkerProjector
{
    internal sealed record TinkerOption(int CardType, IReadOnlyList<int> Riders);

    internal static int TrialCase(ulong root, int slot) =>
        Beta109WorldRng.CreateEventLocal(root, slot, false, "TRIAL").NextInt(3, "Accept:case");

    // Each type is an alternative continuation from the same post-type RNG state.
    // Replaying the type shuffle avoids advancing one branch through another.
    internal static IReadOnlyList<TinkerOption> TinkerOptions(ulong root, int slot)
    {
        var rng = Beta109WorldRng.CreateEventLocal(root, slot, false, "TINKER_TIME");
        int[] types = ShuffleTypes(rng);
        return types.Take(2).Select(type =>
        {
            var branch = Beta109WorldRng.CreateEventLocal(root, slot, false, "TINKER_TIME");
            ShuffleTypes(branch);
            return new TinkerOption(type, ShuffleRiders(branch, type).Take(2).ToArray());
        }).ToArray();
    }

    internal static bool TinkerContains(ulong root, int slot, int type, int? rider)
    {
        var rng = Beta109WorldRng.CreateEventLocal(root, slot, false, "TINKER_TIME");
        int[] types = ShuffleTypes(rng);
        if (!types.Take(2).Contains(type)) return false;
        if (rider is null) return true;
        return ShuffleRiders(rng, type).Take(2).Contains(rider.Value);
    }

    private static int[] ShuffleTypes(Beta109WorldRng rng)
    {
        int[] types = [0, 1, 2];
        for (int i = 2; i > 0; i--) { int j = rng.NextInt(i + 1, "type-shuffle"); (types[i], types[j]) = (types[j], types[i]); }
        return types;
    }

    private static int[] ShuffleRiders(Beta109WorldRng rng, int type)
    {
        int[] riders = [type * 3, type * 3 + 1, type * 3 + 2];
        for (int i = 2; i > 0; i--) { int j = rng.NextInt(i + 1, "rider-shuffle"); (riders[i], riders[j]) = (riders[j], riders[i]); }
        return riders;
    }
}
