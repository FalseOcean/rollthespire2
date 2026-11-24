using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Core.PredictorRuntime;

// One selected sequence, projected only through the detached operation model.
// Original IDs are retained explicitly; generated IDs do not encode slot order.
internal static class TransformSelectionForecast
{
    internal sealed record Result(long OriginalId, PredictorCard Card);

    internal static ModelKey? Source(PredictorOperationSnapshot snapshot) => snapshot.Scene switch
    {
        PredictorOperationEventScene { AwaitingCards: true } scene when IsRandomEventTransform(scene.Event.Entry, scene.Branch) => scene.Event,
        PredictorOperationObtainScene { Kind: PredictorOperationObtainChildKind.NewLeaf } scene => scene.Source,
        PredictorOperationObtainScene { Kind: PredictorOperationObtainChildKind.Deck,
            DeckChoice.Operation: PredictorObtainDeckOperation.Astrolabe } scene => scene.Source,
        PredictorOperationShopScene { PendingRelic.Choice.Operation: PredictorObtainDeckOperation.Astrolabe } scene =>
            scene.PendingRelic!.Choice.Source,
        _ => null
    };

    // These parents finish the Astrolabe obtain without another deck-dependent
    // choice or effect to include in the hover. A composite parent can resume
    // additional work with the transformed deck.
    internal static long? DirectAstrolabeRelicId(PredictorOperationSnapshot snapshot) => snapshot.Scene switch
    {
        PredictorOperationObtainScene { Source.Entry: "ASTROLABE", Kind: PredictorOperationObtainChildKind.Deck,
            Destination: PredictorObtainReturn.Ancient, Parent: PredictorOperationAncientScene } scene => scene.RelicId,
        PredictorOperationObtainScene { Source.Entry: "ASTROLABE", Kind: PredictorOperationObtainChildKind.Deck,
            Destination: PredictorObtainReturn.Reward, Parent: PredictorOperationRewardsScene } scene => scene.RelicId,
        PredictorOperationShopScene { PendingRelic.Choice.Source.Entry: "ASTROLABE" } scene => scene.PendingRelic.Choice.RelicId,
        _ => null
    };

    internal static bool IsRandomEventTransform(string entry, string? branch) => (entry, branch) is
        ("AROMA_OF_CHAOS", "LET_GO") or ("MORPHIC_GROVE", "GROUP") or
        ("SYMBIOTE", "KILL_WITH_FIRE") or ("WHISPERING_HOLLOW", "HUG") or
        ("ENDLESS_CONVEYOR", "GRAB_JELLY_LIVER") or ("TRIAL", "NONDESCRIPT_INNOCENT");

    internal static PredictorInput Input(PredictorOperationSnapshot snapshot, ImmutableArray<long> orderedIds)
    {
        if (Source(snapshot) is not { } source) throw new NotSupportedException("TransformSelectionSourceUnsupported");
        if (snapshot.Scene is PredictorOperationEventScene) return new SelectEventCards(orderedIds);
        if (source.Entry == "NEW_LEAF")
            return orderedIds is [var only] ? new SelectTransformationCard(only) :
                throw new ArgumentException("NewLeafTransformRequiresOneCard", nameof(orderedIds));
        return new ChooseObtainDeckCards(orderedIds);
    }

    internal static ImmutableArray<Result> Build(PredictorOperationSnapshot snapshot, ImmutableArray<long> orderedIds,
        CancellationToken token = default)
    {
        if (orderedIds.IsDefaultOrEmpty || orderedIds.Distinct().Count() != orderedIds.Length)
            throw new ArgumentException("TransformSelectionIdsInvalid", nameof(orderedIds));
        var result = PredictorOperation.Preview(snapshot, Input(snapshot, orderedIds), token);
        return Read(result, orderedIds);
    }

    internal static ImmutableArray<Result> Read(PredictorOperationResult result, ImmutableArray<long> orderedIds)
    {
        if (result.Disposition != PredictorInputResult.Accepted)
            throw new InvalidOperationException("TransformSelectionRejected:" + result.Disposition);
        var changes = result.Effects.SelectMany(effect => effect.Transformations).ToArray();
        return orderedIds.Select(id =>
        {
            var change = changes.Single(change => change.OriginalId == id);
            // Preserve the final result state, including Astrolabe's upgrade
            // and actual automatic add-card hooks, rather than its prototype.
            return new Result(id, result.State.Card(change.ReplacementId));
        }).ToImmutableArray();
    }
}
