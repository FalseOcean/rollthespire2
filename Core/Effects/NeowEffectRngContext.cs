using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Effects;

internal sealed class NeowEffectRngContext
{
    private NeowEffectRngContext(
        Xoshiro256StarStar rewards,
        Xoshiro256StarStar transformations,
        Xoshiro256StarStar niche,
        Xoshiro256StarStar combatPotionGeneration)
    {
        Rewards = rewards;
        Transformations = transformations;
        Niche = niche;
        CombatPotionGeneration = combatPotionGeneration;
    }

    public Xoshiro256StarStar Rewards { get; }
    public Xoshiro256StarStar Transformations { get; }
    public Xoshiro256StarStar Niche { get; }
    public Xoshiro256StarStar CombatPotionGeneration { get; }

    public static NeowEffectRngContext Create(
        IRuntimeProfile profile,
        string canonicalSeed,
        int playerSlotIndex) =>
        CreateFromRootHash(profile, profile.ComputeRootSeed(canonicalSeed), playerSlotIndex);

    public static NeowEffectRngContext CreateFromRootHash(
        IRuntimeProfile profile,
        ulong rootHash,
        int playerSlotIndex)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ulong playerRoot = unchecked(rootHash + (ulong)playerSlotIndex);
        return new NeowEffectRngContext(
            new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(playerRoot, "rewards")),
            new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(playerRoot, "transformations")),
            new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(rootHash, "niche")),
            new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(rootHash, "combat_potion_generation")));
    }

    public NeowEffectRngContext Clone() => new(
        Rewards.Clone(),
        Transformations.Clone(),
        Niche.Clone(),
        CombatPotionGeneration.Clone());

    internal NeowEffectRngContext WithShared(Xoshiro256StarStar niche, Xoshiro256StarStar potions) =>
        new(Rewards, Transformations, niche, potions);
}
