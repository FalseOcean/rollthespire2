using System.Diagnostics;
using System.Threading;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Rewards;

// Method reference: shion0528 (Egoistic今宵), v1.3.5-resource.17,
// da82f7f9023c3d3598d50b8642e93f2801c37ccc. Independently adapted to the
// current Exact request lifetime. Enabled by default for applicable requests;
// an explicit off mode retains same-binary comparisons.
internal sealed class PreparedCombatRewardPools
{
    private readonly RuntimeProfileId _profile;
    private readonly bool _multiplayer;
    internal NeowEffectAuthoritySnapshot OwnedAuthority { get; }
    internal IReadOnlyList<NeowEffectCardSnapshot> Cards { get; }
    internal IReadOnlyList<NeowEffectPotionSnapshot> Potions { get; }
    internal string CardPoolFingerprint { get; }
    internal string PotionPoolFingerprint { get; }

    private PreparedCombatRewardPools(RuntimeProfileId profile, bool multiplayer,
        NeowEffectAuthoritySnapshot owned, IReadOnlyList<NeowEffectCardSnapshot> cards,
        IReadOnlyList<NeowEffectPotionSnapshot> potions)
    {
        _profile = profile; _multiplayer = multiplayer; OwnedAuthority = owned;
        Cards = Array.AsReadOnly(cards.ToArray());
        Potions = Array.AsReadOnly(potions.ToArray());
        CardPoolFingerprint = NormalCombatRewardSequencePredictor.BuildCardPoolFingerprint(Cards);
        PotionPoolFingerprint = NormalCombatRewardSequencePredictor.BuildPotionPoolFingerprint(Potions);
    }

    internal static PreparedCombatRewardPools? TryCreate(RuntimeProfileId profile,
        NeowEffectAuthoritySnapshot? authority, WorldAuthoritySnapshot? world)
    {
        if (profile is not (RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111) || authority is null ||
            authority.CapturedProfileId != profile || !authority.HasExactFoundation ||
            !authority.CharacterRewardPoolExact || !authority.PotionPoolExact ||
            authority.CharacterRewardPool is null || authority.PotionPool is null) return null;
        // IReadOnlyList may wrap a caller-owned array. Own both raw snapshots so
        // later seed-dependent authority reprojections retain immutable inputs.
        var owned = authority with
        {
            CharacterRewardPool = Array.AsReadOnly(authority.CharacterRewardPool.ToArray()),
            PotionPool = Array.AsReadOnly(authority.PotionPool.ToArray())
        };
        var (cards, potions) = NormalCombatRewardSequencePredictor.BuildPoolInputs(owned, world);
        if (NormalCombatRewardSequencePredictor.ValidatePools(profile, cards, potions) is not null) return null;
        return new(profile, world?.Beta109Generation?.IsMultiplayer == true, owned, cards, potions);
    }

    internal bool IsCompatible(RuntimeProfileId profile, NeowEffectAuthoritySnapshot authority,
        WorldAuthoritySnapshot? world) => profile == _profile && authority.CapturedProfileId == _profile &&
        (world?.Beta109Generation?.IsMultiplayer == true) == _multiplayer &&
        ReferenceEquals(authority.CharacterRewardPool, OwnedAuthority.CharacterRewardPool) &&
        ReferenceEquals(authority.PotionPool, OwnedAuthority.PotionPool);
}

internal sealed record CombatRewardPoolPreparationEvidence(bool Enabled, long PreparationAttempts,
    long PreparedCount, string PreparationDisposition, double PreparationMilliseconds,
    long PredictionCalls, long PoolResolutionCalls, long PreparedHits, long UnpreparedBuilds, long CompatibilityMisses);

// One compiled solo Exact request owns this bounded context. With-copies share
// it; a newly compiled request gets a new context. No seed/RNG/result is stored.
internal sealed class CombatRewardPoolPreparation
{
    internal bool Enabled { get; }
    internal PreparedCombatRewardPools? Pools { get; private set; }
    private long _attempts, _calls, _resolutions, _hits, _builds, _misses;
    private double _preparationMs;
    private string _disposition;
    internal CombatRewardPoolPreparation(bool enabled)
    { Enabled = enabled; _disposition = enabled ? "NotPrepared" : "DisabledByOption"; }

    // Called once at compilation, before the context is published to workers.
    internal void Prepare(RuntimeProfileId profile, NeowEffectAuthoritySnapshot? authority, WorldAuthoritySnapshot? world)
    {
        if (!Enabled) return;
        if (_attempts != 0) throw new InvalidOperationException("RewardPools.PreparationAlreadyAttempted");
        _attempts = 1;
        var clock = Stopwatch.StartNew();
        _disposition = "PreparationExceptionOriginalPathRetained";
        try
        {
            Pools = PreparedCombatRewardPools.TryCreate(profile, authority, world);
            _disposition = Pools is null ? "IneligibleOrInvalidPoolsOriginalPathRetained" : "PreparedOwnedFixedPools";
        }
        finally { _preparationMs = clock.Elapsed.TotalMilliseconds; }
    }

    internal void ObservePrediction() => Interlocked.Increment(ref _calls);
    internal PreparedCombatRewardPools? Resolve(RuntimeProfileId profile, NeowEffectAuthoritySnapshot authority,
        WorldAuthoritySnapshot? world)
    {
        Interlocked.Increment(ref _resolutions);
        if (Pools is { } prepared)
        {
            if (prepared.IsCompatible(profile, authority, world))
            { Interlocked.Increment(ref _hits); return prepared; }
            Interlocked.Increment(ref _misses);
        }
        Interlocked.Increment(ref _builds);
        return null;
    }
    internal CombatRewardPoolPreparationEvidence Capture() => new(Enabled, _attempts, Pools is null ? 0 : 1,
        _disposition, _preparationMs, Interlocked.Read(ref _calls), Interlocked.Read(ref _resolutions),
        Interlocked.Read(ref _hits), Interlocked.Read(ref _builds), Interlocked.Read(ref _misses));
}
