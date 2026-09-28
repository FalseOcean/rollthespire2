using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

// C-private real Capsule pulls. Only candidate-local bag indices are mutable;
// the captured pool and the public ordinal payload remain immutable.
internal static class CombatRewardCapsuleReplay
{
    internal static int Initialize(ulong root, Beta110FastEffectCatalog catalog, Span<ushort> bag)
    {
        var upfront = new Beta110FastRng(unchecked(root + XxHash64.Hash("up_front"u8, 0)));
        foreach (int length in catalog.SharedRelicConsumeShuffleLengths) upfront.ConsumeUnstableShuffle(length);
        int offset = 0;
        foreach (var bucket in catalog.PlayerRelicBuckets)
        {
            bucket.RelicIndexes.CopyTo(bag[offset..]);
            upfront.UnstableShuffle(bag.Slice(offset, bucket.RelicIndexes.Length));
            offset += bucket.RelicIndexes.Length;
        }
        return offset;
    }

    internal static ushort Pull(Beta110FastEffectCatalog catalog, Span<ushort> bag, ref Beta110FastRng rewards)
    {
        float roll = rewards.NextFloat();
        int rarity = roll < .5f ? 0 : roll < .83f ? 1 : 2;
        for (; rarity < 3; rarity++)
        for (int index = 0; index < bag.Length; index++)
        {
            ushort entry = bag[index];
            if (entry == ushort.MaxValue || (int)catalog.OrdinaryRelics[entry].Rarity != rarity) continue;
            bag[index] = ushort.MaxValue;
            return entry;
        }
        return ushort.MaxValue; // Circlet: no held effect and no bag insertion.
    }
}
