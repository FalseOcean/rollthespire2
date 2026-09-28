using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

// Trial's cosmetic entrant number uses Rng.Chaotic, not the event-local stream.
// Tinker TakeRandom(2) is a full unstable shuffle of three, then Take(2).
internal static class Beta111TrialTinkerProjector
{
    internal static int TrialCase(ulong root, int slot) =>
        Beta109WorldRng.CreateEventLocal(root, slot, false, "TRIAL").NextInt(3, "Accept:case");
    internal static bool TinkerContains(ulong root, int slot, int type, int? rider)
    {
        var rng = Beta109WorldRng.CreateEventLocal(root, slot, false, "TINKER_TIME");
        int[] types = [0, 1, 2];
        for (int i = 2; i > 0; i--) { int j = rng.NextInt(i + 1, "type-shuffle"); (types[i], types[j]) = (types[j], types[i]); }
        if (!types.Take(2).Contains(type)) return false;
        if (rider is null) return true;
        int[] riders = [type * 3, type * 3 + 1, type * 3 + 2];
        for (int i = 2; i > 0; i--) { int j = rng.NextInt(i + 1, "rider-shuffle"); (riders[i], riders[j]) = (riders[j], riders[i]); }
        return riders.Take(2).Contains(rider.Value);
    }
}
