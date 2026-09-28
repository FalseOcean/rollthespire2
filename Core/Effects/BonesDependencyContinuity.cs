using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects;

/// <summary>
/// Reliability of one dependency domain after replaying a Bones acquisition step.
/// This is intentionally separate from the parent relic/route precision: a Partial
/// reward projection does not automatically invalidate an independent Niche stream.
/// </summary>
public enum BonesContinuityStatus
{
    Exact,
    Partial,
    Unknown,
    Invalidated
}

public sealed record BonesContinuationDomainState(
    BonesContinuityStatus Status,
    IReadOnlyList<PredictionWarningCode> WarningCodes,
    IReadOnlyList<EvidenceCode> EvidenceCodes)
{
    public bool IsExact => Status == BonesContinuityStatus.Exact;

    public static BonesContinuationDomainState Exact(EvidenceCode evidenceCode) =>
        new(BonesContinuityStatus.Exact, Array.Empty<PredictionWarningCode>(), new[] { evidenceCode });

    public static BonesContinuationDomainState Partial(
        PredictionWarningCode warningCode,
        EvidenceCode evidenceCode) =>
        new(BonesContinuityStatus.Partial, new[] { warningCode }, new[] { evidenceCode });

    public static BonesContinuationDomainState Unknown(
        PredictionWarningCode warningCode,
        EvidenceCode evidenceCode) =>
        new(BonesContinuityStatus.Unknown, new[] { warningCode }, new[] { evidenceCode });

    public static BonesContinuationDomainState Invalidated(
        PredictionWarningCode warningCode,
        EvidenceCode evidenceCode) =>
        new(BonesContinuityStatus.Invalidated, new[] { warningCode }, new[] { evidenceCode });

    public BonesContinuationDomainState Merge(BonesContinuationDomainState next)
    {
        ArgumentNullException.ThrowIfNull(next);
        BonesContinuityStatus status = Severity(next.Status) > Severity(Status) ? next.Status : Status;
        return new BonesContinuationDomainState(
            status,
            WarningCodes.Concat(next.WarningCodes).Distinct().ToArray(),
            EvidenceCodes.Concat(next.EvidenceCodes).Where(code => code.IsValid).Distinct().ToArray());
    }

    private static int Severity(BonesContinuityStatus status) => status switch
    {
        BonesContinuityStatus.Exact => 0,
        BonesContinuityStatus.Partial => 1,
        BonesContinuityStatus.Unknown => 2,
        BonesContinuityStatus.Invalidated => 3,
        _ => 3
    };
}

/// <summary>
/// Dependency-domain continuity carried by each raw Bones route. The final curse
/// depends on GeneratedCursePool + Niche + absence of an unresolved immediate hook
/// + completion of all required player-choice branches. Other domains remain visible
/// for diagnostics and order-impact evidence but do not automatically gate the curse.
/// </summary>
public sealed record BonesDependencyContinuity(
    BonesContinuationDomainState RewardsRng,
    BonesContinuationDomainState NicheRng,
    BonesContinuationDomainState TransformationsRng,
    BonesContinuationDomainState ShadowDeck,
    BonesContinuationDomainState RelicBag,
    BonesContinuationDomainState GeneratedCursePool,
    BonesContinuationDomainState UnknownHook,
    BonesContinuationDomainState NestedObtain,
    BonesContinuationDomainState PlayerChoice)
{
    public bool CanProjectFinalCurse =>
        NicheRng.IsExact &&
        GeneratedCursePool.IsExact &&
        UnknownHook.IsExact &&
        NestedObtain.IsExact &&
        PlayerChoice.IsExact;

    public IReadOnlyList<PredictionWarningCode> ContinuationWarningCodes =>
        new[] { NicheRng, GeneratedCursePool, UnknownHook, NestedObtain, PlayerChoice }
            .SelectMany(domain => domain.WarningCodes)
            .Distinct()
            .ToArray();

    public IReadOnlyList<EvidenceCode> ContinuationEvidenceCodes =>
        new[] { NicheRng, GeneratedCursePool, UnknownHook, NestedObtain, PlayerChoice }
            .SelectMany(domain => domain.EvidenceCodes)
            .Where(code => code.IsValid)
            .Distinct()
            .ToArray();

    public BonesDependencyContinuity Merge(EffectDependencyImpact impact)
    {
        ArgumentNullException.ThrowIfNull(impact);
        return new BonesDependencyContinuity(
            RewardsRng.Merge(impact.RewardsRng),
            NicheRng.Merge(impact.NicheRng),
            TransformationsRng.Merge(impact.TransformationsRng),
            ShadowDeck.Merge(impact.ShadowDeck),
            RelicBag.Merge(impact.RelicBag),
            GeneratedCursePool.Merge(impact.GeneratedCursePool),
            UnknownHook.Merge(impact.UnknownHook),
            NestedObtain.Merge(impact.NestedObtain),
            PlayerChoice.Merge(impact.PlayerChoice));
    }

    public static BonesDependencyContinuity CreateInitial(
        NeowEffectAuthoritySnapshot authority,
        EvidenceCode evidenceCode)
    {
        ArgumentNullException.ThrowIfNull(authority);
        BonesContinuationDomainState exact = BonesContinuationDomainState.Exact(evidenceCode);
        BonesContinuationDomainState deck = authority.HasExactDeck
            ? exact
            : BonesContinuationDomainState.Unknown(
                PredictionWarningCode.EffectSnapshotIncomplete,
                new EvidenceCode(evidenceCode.ToString() + ".deck-incomplete"));
        BonesContinuationDomainState bag = authority.HasExactRelicBag
            ? exact
            : BonesContinuationDomainState.Unknown(
                PredictionWarningCode.EffectSnapshotIncomplete,
                new EvidenceCode(evidenceCode.ToString() + ".relic-bag-incomplete"));
        BonesContinuationDomainState cursePool = authority.GeneratedCursePool is not { Count: > 0 }
            ? BonesContinuationDomainState.Unknown(
                PredictionWarningCode.EffectPoolEmpty,
                new EvidenceCode(evidenceCode.ToString() + ".curse-pool-empty"))
            : authority.CursePoolExact
                ? exact
                : BonesContinuationDomainState.Unknown(
                    PredictionWarningCode.EffectAuthorityIncomplete,
                    new EvidenceCode(evidenceCode.ToString() + ".curse-pool-authority-incomplete"));

        return new BonesDependencyContinuity(
            exact,
            exact,
            exact,
            deck,
            bag,
            cursePool,
            exact,
            exact,
            exact);
    }
}

/// <summary>
/// Domain-local effect of one projector execution. Exact is the identity element.
/// A known Partial projector may explicitly identify only the affected domains;
/// parent precision alone never invalidates unrelated streams. Unsupported or
/// Unknown projections use the conservative unknown-hook fallback.
/// </summary>
public sealed record EffectDependencyImpact(
    BonesContinuationDomainState RewardsRng,
    BonesContinuationDomainState NicheRng,
    BonesContinuationDomainState TransformationsRng,
    BonesContinuationDomainState ShadowDeck,
    BonesContinuationDomainState RelicBag,
    BonesContinuationDomainState GeneratedCursePool,
    BonesContinuationDomainState UnknownHook,
    BonesContinuationDomainState NestedObtain,
    BonesContinuationDomainState PlayerChoice)
{
    public static EffectDependencyImpact None(EvidenceCode evidenceCode)
    {
        BonesContinuationDomainState exact = BonesContinuationDomainState.Exact(evidenceCode);
        return new EffectDependencyImpact(exact, exact, exact, exact, exact, exact, exact, exact, exact);
    }

    public static EffectDependencyImpact RewardsOnlyPartial(
        PredictionWarningCode warningCode,
        EvidenceCode evidenceCode)
    {
        EffectDependencyImpact exact = None(evidenceCode);
        return exact with
        {
            RewardsRng = BonesContinuationDomainState.Partial(warningCode, evidenceCode)
        };
    }

    public static EffectDependencyImpact ShadowOnlyPartial(
        PredictionWarningCode warningCode,
        EvidenceCode evidenceCode)
    {
        EffectDependencyImpact exact = None(evidenceCode);
        return exact with
        {
            ShadowDeck = BonesContinuationDomainState.Partial(warningCode, evidenceCode)
        };
    }

    public static EffectDependencyImpact TransformationsAndShadowPartial(
        PredictionWarningCode warningCode,
        EvidenceCode evidenceCode)
    {
        EffectDependencyImpact exact = None(evidenceCode);
        BonesContinuationDomainState partial = BonesContinuationDomainState.Partial(warningCode, evidenceCode);
        return exact with
        {
            TransformationsRng = partial,
            ShadowDeck = partial
        };
    }

    public static EffectDependencyImpact ConservativeUnknownImmediateHook(
        PredictionWarningCode warningCode,
        EvidenceCode evidenceCode)
    {
        EffectDependencyImpact exact = None(evidenceCode);
        return exact with
        {
            NicheRng = BonesContinuationDomainState.Unknown(warningCode, evidenceCode),
            ShadowDeck = BonesContinuationDomainState.Unknown(warningCode, evidenceCode),
            GeneratedCursePool = BonesContinuationDomainState.Unknown(warningCode, evidenceCode),
            UnknownHook = BonesContinuationDomainState.Unknown(warningCode, evidenceCode),
            NestedObtain = BonesContinuationDomainState.Unknown(warningCode, evidenceCode)
        };
    }

    public static EffectDependencyImpact FiniteChoiceNotEvaluated(
        RuntimeProfileId profileId,
        ModelKey relicKey,
        EvidenceCode evidenceCode)
    {
        EffectDependencyImpact exact = None(evidenceCode);
        BonesContinuationDomainState invalidChoice = BonesContinuationDomainState.Invalidated(
            PredictionWarningCode.ComplexResultNotEvaluatedByPolicy,
            evidenceCode);
        EffectDependencyImpact result = exact with
        {
            ShadowDeck = invalidChoice,
            PlayerChoice = invalidChoice
        };

        if (relicKey == BaseGameModelKeys.Relics.NewLeaf)
        {
            result = RuntimeProfilePolicies.IsModernCore(profileId)
                ? result with { NicheRng = invalidChoice }
                : result with { TransformationsRng = invalidChoice };
        }

        return result;
    }

    public EffectDependencyImpact Merge(EffectDependencyImpact next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return new EffectDependencyImpact(
            RewardsRng.Merge(next.RewardsRng),
            NicheRng.Merge(next.NicheRng),
            TransformationsRng.Merge(next.TransformationsRng),
            ShadowDeck.Merge(next.ShadowDeck),
            RelicBag.Merge(next.RelicBag),
            GeneratedCursePool.Merge(next.GeneratedCursePool),
            UnknownHook.Merge(next.UnknownHook),
            NestedObtain.Merge(next.NestedObtain),
            PlayerChoice.Merge(next.PlayerChoice));
    }
}
