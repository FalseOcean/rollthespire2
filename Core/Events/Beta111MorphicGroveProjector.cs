using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Events;

// Event-owned inputs only. These are conditional observations, not a continuation RunState.
// Order means the enumeration delivered by CardSelectCmd, not an inferred UI click
// order. The vanilla screen stores selections in a HashSet; a UI adapter must bind
// the delivered order explicitly, particularly for targets with different pools.
public sealed record MorphicGroveCommitment(string FirstInstanceId, string SecondInstanceId)
{
    // Explicit Group choice with Owner's default two initial basic-card targets.
    public static MorphicGroveCommitment InitialBasics { get; } = new("basic:0", "basic:1");
}

// VanillaLocalDependenciesBound covers the local dependency slice, not merely the
// Group method body: captured pools/unlocks/player mode remain valid through both
// draws, and no intervening custom hook changes event RNG or the second target.
// NoDeckMutationHooks is separately required for final card metadata. Neither flag
// is inferred from the seed or from an uninspected relic/mod list.
public sealed record MorphicGrovePremises(
    string EventOccurrenceBasis,
    bool VanillaLocalDependenciesBound,
    bool NoDeckMutationHooks,
    int? EntryFloor);

public sealed record MorphicGroveCard(
    ModelKey CardKey, string PoolId, string CardType, string Rarity,
    int UpgradeLevel, int MaxUpgradeLevel, bool CanUpgrade, bool CanTransform,
    string? EnchantmentId);

public sealed record MorphicGroveTarget(
    string InstanceId, MorphicGroveCard Original,
    IReadOnlyList<MorphicGroveCard>? OrderedSourceCandidates);

public sealed record MorphicGroveTransform(
    string OriginalInstanceId, MorphicGroveCard? RawReplacement,
    MorphicGroveCard? FinalReplacement, int? FloorAddedToDeck,
    PredictionPrecision RawPrecision, PredictionPrecision FinalPrecision);

public sealed record MorphicGroveProjection(
    MorphicGroveCommitment Commitment, MorphicGrovePremises Premises,
    ulong EventSeed, IReadOnlyList<MorphicGroveTransform> Transforms,
    IReadOnlyList<WorldRngTraceEntry> RngTrace, string Evidence);

public enum MorphicGrovePredicateResult { Match, NoMatch, Unknown }

/// <summary>
/// Conditional Beta111 Group projection. Candidate ordering/metadata comes from the
/// source CardFactory capture, not Neow's narrower transform approximation.
/// No occurrence proof, game state, Family allocation or ABI1 change is implied.
/// </summary>
public static class Beta111MorphicGroveProjector
{
    public const string EventEntry = "MORPHIC_GROVE";

    public static MorphicGroveProjection Project(ulong rootHash,
        MorphicGroveCommitment commitment, MorphicGrovePremises premises,
        IReadOnlyList<MorphicGroveTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(premises);
        ArgumentNullException.ThrowIfNull(targets);
        if (string.IsNullOrWhiteSpace(premises.EventOccurrenceBasis))
            throw new ArgumentException("An explicit MorphicGrove occurrence premise is required.");
        if (premises.EntryFloor < 0) throw new ArgumentOutOfRangeException(nameof(premises));
        if (commitment.FirstInstanceId == commitment.SecondInstanceId)
            throw new ArgumentException("Group requires two distinct card instances.");
        var byId = targets.ToDictionary(t => t.InstanceId, StringComparer.Ordinal);
        var rng = Beta109WorldRng.CreateEventLocal(rootHash, 0, true, EventEntry);
        var results = new List<MorphicGroveTransform>();
        foreach (string id in new[] { commitment.FirstInstanceId, commitment.SecondInstanceId })
        {
            if (!byId.TryGetValue(id, out var target))
                throw new ArgumentException($"Missing committed target {id}.");
            if (!target.Original.CanTransform || target.Original.CardType == "Quest")
                throw new ArgumentException($"Illegal Group target {id}.");
            MorphicGroveCard? raw = null;
            if (premises.VanillaLocalDependenciesBound)
            {
                if (target.OrderedSourceCandidates is { } pool)
                {
                    if (pool.Count == 0) throw new InvalidOperationException("Source-authoritative transform pool is empty.");
                    raw = pool[rng.NextInt(pool.Count, $"Group:{id}")];
                }
                else
                {
                    // Vanilla NextItem takes one draw independent of the unknown pool's
                    // size. Preserve the second closed target; this is not a pool fallback.
                    rng.NextDouble($"Group:{id}:unknown-pool-one-draw");
                }
            }
            bool finalKnown = raw != null && premises.NoDeckMutationHooks;
            results.Add(new(id, raw, finalKnown ? raw : null,
                finalKnown ? premises.EntryFloor : null,
                raw != null ? PredictionPrecision.Exact : PredictionPrecision.Unknown,
                finalKnown ? PredictionPrecision.Exact : PredictionPrecision.Unknown));
        }
        return new(commitment, premises,
            Beta109WorldRng.DeriveEventLocalSeed(rootHash, 0, true, EventEntry),
            results.AsReadOnly(), rng.Trace.ToArray(),
            "Beta111 MorphicGrove.Group / CardFactory source capture; conditional, not occurrence proof or full continuation");
    }

    public static MorphicGrovePredicateResult Contains(MorphicGroveProjection projection,
        ModelKey card, bool finalDeckCard)
    {
        if (!card.IsValid) throw new ArgumentException("Predicate requires a valid card identity.");
        bool unknown = false;
        foreach (var transform in projection.Transforms)
        {
            var value = finalDeckCard ? transform.FinalReplacement : transform.RawReplacement;
            if (value?.CardKey == card) return MorphicGrovePredicateResult.Match;
            unknown |= value == null;
        }
        return unknown ? MorphicGrovePredicateResult.Unknown : MorphicGrovePredicateResult.NoMatch;
    }

    public static MorphicGrovePredicateResult ContainsPair(MorphicGroveProjection projection,
        ModelKey first, ModelKey second, bool finalDeckCard)
    {
        if (!first.IsValid || !second.IsValid || projection.Transforms.Count != 2)
            throw new ArgumentException("Morphic Grove pair requires two card identities and two transformations.");
        var a = finalDeckCard ? projection.Transforms[0].FinalReplacement : projection.Transforms[0].RawReplacement;
        var b = finalDeckCard ? projection.Transforms[1].FinalReplacement : projection.Transforms[1].RawReplacement;
        bool possible = (a is null || a.CardKey == first) && (b is null || b.CardKey == second) ||
                        (a is null || a.CardKey == second) && (b is null || b.CardKey == first);
        return !possible ? MorphicGrovePredicateResult.NoMatch : a is null || b is null
            ? MorphicGrovePredicateResult.Unknown : MorphicGrovePredicateResult.Match;
    }
}
