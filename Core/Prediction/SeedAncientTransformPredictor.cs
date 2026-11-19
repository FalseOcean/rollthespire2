using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Transformation;

namespace RolltheSpire2.Core.Prediction;

internal enum SeedAncientTransformStatus
{
    Ready, UnsupportedVersion, UnsupportedParty, ContextUnavailable, OpeningUnavailable,
    MaterialsUnknown, InsufficientBasics, StreamUnknown, PoolUnknown, DrawBudgetExceeded
}

internal sealed record SeedAncientTransformResult(SeedAncientTransformStatus Status,
    IReadOnlyList<TransformationRelicCard> Cards, int? AutomaticCount = null);

/// <summary>Conditional on obtaining the relic and no further personal transform draws or material changes after the opening.</summary>
internal static class SeedAncientTransformPredictor
{
    internal static SeedAncientTransformResult Project(ulong root, SeedPredictionRequest request, SeedPredictionFacts facts,
        ModelKey relic, int? openingChoiceSlot, string openingRouteId, int? pandoraCount = null)
    {
        bool astrolabe = relic switch
        {
            { Category: "RELIC", Entry: "ASTROLABE" } => true,
            { Category: "RELIC", Entry: "PANDORAS_BOX" } => false,
            _ => throw new ArgumentOutOfRangeException(nameof(relic))
        };
        if (pandoraCount < 0 || astrolabe && pandoraCount.HasValue)
            throw new ArgumentOutOfRangeException(nameof(pandoraCount));

        var authority = request.Authority;
        var semantics = authority.VersionResolution.SemanticProfileId;
        if (authority.ProfileId != RuntimeProfileId.Beta111 ||
            semantics is not (RngSemanticProfileId.Modern113 or RngSemanticProfileId.Modern110))
            return new(SeedAncientTransformStatus.UnsupportedVersion, []);
        if (request.PlayersCount != 1) return new(SeedAncientTransformStatus.UnsupportedParty, []);
        if (facts.Context.RequestId != request.RequestId || facts.InputStatus != SeedPredictionInputStatus.Ready)
            return new(SeedAncientTransformStatus.ContextUnavailable, []);

        var opening = ResolveOpening(request, facts, openingChoiceSlot, openingRouteId);
        if (opening is null) return new(SeedAncientTransformStatus.OpeningUnavailable, []);
        int? automatic = astrolabe ? TransformationRelicProjector.AstrolabeTargetCount : opening.BasicStrikeDefendCount;
        int? count = pandoraCount ?? automatic;
        // An empty Pandora target list never reads RNG. For a nonempty list
        // the older shared Niche cursor cannot be known from the opening alone.
        if (count == 0) return new(SeedAncientTransformStatus.Ready, [], automatic);
        if (semantics != RngSemanticProfileId.Modern113)
            return new(SeedAncientTransformStatus.UnsupportedVersion, [], automatic);
        if (astrolabe && opening.BasicCount < TransformationRelicProjector.AstrolabeTargetCount)
            return new(SeedAncientTransformStatus.InsufficientBasics, []);
        if (count is null || astrolabe && opening.BasicCount is null)
            return new(SeedAncientTransformStatus.MaterialsUnknown, []);
        if (count > TransformationRelicProjector.ProjectionDrawBudget)
            return new(SeedAncientTransformStatus.DrawBudgetExceeded, [], automatic);
        if (opening.StreamOffset is not { } offset) return new(SeedAncientTransformStatus.StreamUnknown, [], automatic);
        var projector = TransformationRelicProjector.Capture(authority.EffectAuthority);
        if (projector is null) return new(SeedAncientTransformStatus.PoolUnknown, [], automatic);
        return new(SeedAncientTransformStatus.Ready, projector.Project(Beta111Profile.Instance, root, request.PlayerSlotIndex,
            semantics, offset, count.Value, upgrade: astrolabe), automatic);
    }

    private static OpeningTransformationContinuation? ResolveOpening(SeedPredictionRequest request, SeedPredictionFacts facts,
        int? choiceSlot, string routeId)
    {
        if (choiceSlot is null && string.IsNullOrEmpty(routeId))
            return OpeningTransformationContinuation.Initial(request.Authority.EffectAuthority);
        var choice = facts.Neow?.Choices.FirstOrDefault(candidate => candidate.SlotIndex == choiceSlot);
        if (choice is null) return null;
        if (choice.RelicKey == BaseGameModelKeys.Relics.NeowsBones)
            return choice.BonesOutcome?.OriginalRoutes.FirstOrDefault(route => route.RouteId == routeId)?.TransformationContinuation;
        if (!string.IsNullOrEmpty(routeId) && choice.OpeningRewardContinuations?.Routes.Any(route => route.Route.RouteId == routeId) != true)
            return null;
        return choice.TransformationContinuation;
    }
}
