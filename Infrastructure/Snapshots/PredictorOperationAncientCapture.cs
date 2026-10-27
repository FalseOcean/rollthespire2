using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorOperationCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class PredictorOperationAncientCapture
{
    internal static PredictorOperationScene Capture(RunState run, AncientEventModel ancient,
        PredictorCatalog catalog, Node? root = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var owner = ancient.Owner ?? throw new InvalidOperationException("OperationAncientOwnerMissing");
        var options = ReadNullable(ancient, "_currentOptions") as IReadOnlyList<EventOption>
            ?? throw new InvalidOperationException("OperationAncientOffersMissing");
        var offers = options.Where(o => o.Relic != null && !o.WasChosen).Select(o =>
        {
            var relic = o.Relic!;
            long? starterCard = relic is ArchaicTooth { StarterCard.Id: { } cardKey }
                ? owner.Deck.Cards.Where(c => c.Id == cardKey).Select(c => (long?)Id(c)).FirstOrDefault() : null;
            long? starterRelic = relic is TouchOfOrobas { StarterRelic: { } relicKey }
                ? owner.Relics.Where(r => r.Id == relicKey).Select(r => (long?)Id(r)).FirstOrDefault() : null;
            return new PredictorAncientOffer(Id(o), Key(relic), !o.IsLocked,
                relic is SeaGlass { CharacterId: { } character } ? Key(character) : null,
                starterCard, starterRelic,
                relic is DustyTome { AncientCard: { } card } ? Key(card) : null, null);
        }).ToImmutableArray();
        var parent = new PredictorOperationAncientScene(Key(ancient), offers);
        if (ObservedObtainOrigin(owner, root) is { } origin)
            return CaptureObtainChild(owner, root, catalog, parent, PredictorObtainReturn.Ancient,
                Id(origin.Relic), origin);
        var rewardSet = CurrentNativeRewards(run, owner, root);
        if (rewardSet != null && ObservedRewardSetObtainSource(rewardSet) is { } rewardOrigin)
            return CaptureObtainChild(owner, root, catalog, parent, PredictorObtainReturn.Ancient,
                Id(rewardOrigin.Relic), rewardOrigin, CaptureRewardsSet(rewardSet, owner, catalog));
        if (options.Any(o => o.WasChosen && !o.IsProceed) || offers.IsEmpty)
            throw new NotSupportedException("OperationAncientObtainContinuationPending");
        return parent;
    }
}
