using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

/// <summary>
/// Raw replacements for the existing E initial-Basic contract. Event occurrence,
/// option selection and an unchanged captured pool are premises, not route proofs.
/// Shared by information prediction and E; no Search condition is needed to predict.
/// </summary>
internal static class Beta111EventTransformProjector
{
    internal static int DrawCount(string entry) => entry switch
    {
        "MORPHIC_GROVE" or "TRIAL" => 2,
        "SYMBIOTE" or "AROMA_OF_CHAOS" or "WHISPERING_HOLLOW" => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(entry))
    };

    internal static IReadOnlyList<MorphicGroveCard> Project(ulong root, int slot,
        string entry, IReadOnlyList<MorphicGroveCard> pool)
    {
        int count = DrawCount(entry);
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        if (pool.Count == 0) throw new ArgumentException("EventTransform.EmptySourcePool", nameof(pool));
        if (count == 1) return [DrawSingle(root, slot, entry, pool)];
        var rng = Beta109WorldRng.CreateEventLocal(root, slot, entry == "MORPHIC_GROVE", entry);
        if (entry == "TRIAL" && rng.NextInt(3, "Accept:case") != 2) return [];
        var first = pool[rng.NextInt(pool.Count, "transform:0")];
        return [first, pool[rng.NextInt(pool.Count, "transform:1")]];
    }

    // Single-draw entries only; callers retain their own target/pool admission and precision.
    internal static MorphicGroveCard DrawSingle(ulong root, int slot,
        string entry, IReadOnlyList<MorphicGroveCard> pool)
    {
        var rng = Beta109WorldRng.CreateEventLocal(root, slot, false, entry);
        // Whisper.CalculateVars always precedes GenerateInitialOptions/Hug.
        if (entry == "WHISPERING_HOLLOW") rng.NextInt(19, "CalculateVars:gold-minus-nine");
        return pool[rng.NextInt(pool.Count, "transform:0")];
    }
}
