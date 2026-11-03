using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction;

public sealed record AncientPremiseObservationFailure(int Act, ModelKey Ancient, Exception Error);

/// <summary>
/// Bounded, request-owned Ancient option alternatives. Independent observations
/// stay shared. Preparation retains the existing per-Ancient 8/4 combination
/// cache; selecting an alternative never replays an entire seed or a route.
/// </summary>
public sealed class SeedAncientPremiseVariants
{
    private sealed record Variants(ModelKey[] Keys, AncientPredictionResult[] Results)
    {
        internal AncientPredictionResult Select(AncientOptionConditionProfile conditions)
        {
            int mask = 0;
            for (int i = 0; i < Keys.Length; i++)
                if (conditions.TryGetOptionEligibility(Keys[i], out bool eligible) && eligible) mask |= 1 << i;
            return Results[mask];
        }
    }

    private readonly SeedPredictionRequest _request;
    private readonly SeedPredictionFacts _facts;
    private readonly Dictionary<(int Act, ModelKey Ancient), Variants> _variants = [];
    public IReadOnlyList<AncientPremiseObservationFailure> Failures { get; }

    private SeedAncientPremiseVariants(SeedPredictionRequest request, SeedPredictionFacts facts)
    {
        _request = request; _facts = facts;
        var failures = new List<AncientPremiseObservationFailure>();
        IRuntimeProfile profile = request.Authority.ProfileId switch
        {
            RuntimeProfileId.Beta111 => Beta111Profile.Instance,
            RuntimeProfileId.Beta110 => Beta110Profile.Instance,
            RuntimeProfileId.Beta109 => Beta109Profile.Instance,
            RuntimeProfileId.Stable107 => Stable107Profile.Instance,
            _ => UnsupportedProfile.Instance
        };
        foreach (var ancient in facts.World?.Ancients ?? [])
        {
            var keys = AncientOptionConditionProfile.AuthoredOptionKeys
                .Where(k => AncientForOption(k) == ancient.AncientKey.Entry).ToArray();
            if (keys.Length == 0) continue;
            try
            {
                var results = new AncientPredictionResult[1 << keys.Length];
                for (int mask = 0; mask < results.Length; mask++)
                {
                    var conditions = request.AncientOptionConditions;
                    for (int i = 0; i < keys.Length; i++)
                        conditions = conditions.WithOptionEligibility(keys[i], (mask & (1 << i)) != 0);
                    var world = request.TrustedRootHashInput is { } root
                        ? WorldPredictionEngine.PredictFromRootHash(profile, root.RootHash, root.SeedIdentity,
                            request.Ascension, request.PlayerSlotIndex, request.Character.CharacterKey,
                            request.Authority.WorldAuthority, request.Authority.EffectAuthority, conditions)
                        : WorldPredictionEngine.Predict(profile, facts.CanonicalSeed,
                            request.Ascension, request.PlayerSlotIndex, request.Character.CharacterKey,
                            request.Authority.WorldAuthority, request.Authority.EffectAuthority, conditions);
                    results[mask] = world.Ancients.FirstOrDefault(a => a.Act == ancient.Act && a.AncientKey == ancient.AncientKey)
                        ?? throw new InvalidOperationException("AncientPremiseIdentityUnavailable");
                }
                _variants[(ancient.Act, ancient.AncientKey)] = new(keys, results);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Only this Ancient's edited premise is unavailable. Other
                // variants and the original facts remain usable with this cause.
                failures.Add(new(ancient.Act, ancient.AncientKey, error));
            }
        }
        Failures = failures.ToArray();
    }

    public static SeedAncientPremiseVariants Prepare(SeedPredictionRequest request, SeedPredictionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(facts);
        if (request.RequestId != facts.Context.RequestId ||
            request.Authority.UnlockSnapshotFingerprint != facts.Context.UnlockSnapshotFingerprint ||
            request.Authority.CatalogFingerprint != facts.Context.CatalogFingerprint)
            throw new ArgumentException("AncientPremiseFactsContextMismatch");
        return new(request, facts);
    }

    public bool HasVariants(int act, ModelKey ancient) => _variants.ContainsKey((act, ancient));

    public bool CanRevise(AncientOptionConditionProfile conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        return (_facts.World?.Ancients ?? []).All(ancient =>
            _variants.ContainsKey((ancient.Act, ancient.AncientKey)) ||
            !AncientOptionConditionProfile.AuthoredOptionKeys.Where(k => AncientForOption(k) == ancient.AncientKey.Entry)
                .Any(k => conditions.TryGetOptionEligibility(k, out bool next) &&
                    _request.AncientOptionConditions.TryGetOptionEligibility(k, out bool previous) && next != previous));
    }

    public SeedPredictionRevision Revise(AncientOptionConditionProfile conditions)
    {
        if (!CanRevise(conditions))
            throw new InvalidOperationException("AncientPremiseRevisionUnavailable",
                Failures.FirstOrDefault()?.Error);
        var request = _request.WithAncientOptionConditions(conditions);
        return new(request, _facts with
        {
            Context = _facts.Context with { RequestId = request.RequestId },
            World = _facts.World is { } world ? world with
            {
                Ancients = world.Ancients.Select(a => _variants.TryGetValue((a.Act, a.AncientKey), out var variants)
                    ? variants.Select(conditions) : a).ToArray()
            } : null,
            Diagnostics = _facts.Diagnostics.Select(d => d.Code == PredictionDiagnosticCodes.RequestId
                ? d with { Value = request.RequestId.Serialized } : d).ToArray()
        });
    }

    private static string AncientForOption(ModelKey key) => key.Entry switch
    {
        "PAELS_CLAW" or "PAELS_LEGION" or "PAELS_TOOTH" => "PAEL",
        "ARCHAIC_TOOTH" or "TOUCH_OF_OROBAS" => "OROBAS",
        "NUTRITIOUS_SOUP" => "TEZCATARA",
        "BEAUTIFUL_BRACELET" => "NONUPEIPE",
        "TRI_BOOMERANG" => "TANX",
        "PANDORAS_BOX" => "DARV",
        _ => ""
    };
}
