using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class ScopedTransformationPools
{
    // Each inner array is one physical draw. Both orders start at the same word
    // and reach the same post-pickup cursor. Later Ancient and event draws are
    // shared by both witnesses. Second is omitted for a fixed or equivalent
    // order. An unknown sequence has no result.
    internal static (ModelKey[][] First, ModelKey[][]? Second)? Bones(
        SearchModel model, TransformationAggregateCondition condition)
    {
        if (!SupportsPersonalStream(model, condition) ||
            model.Authority.EffectAuthority is not { HasExactDeck: true, OrderedDeck: { } deck } effects ||
            effects.CapturedProfileId != model.Authority.ProfileId) return null;
        var leafy = BaseGameModelKeys.Relics.LeafyPoultice;
        var newLeaf = BaseGameModelKeys.Relics.NewLeaf;
        ModelKey companion;
        if (condition.IsBones) companion = condition.BonesCompanion;
        else
        {
            // A T row that only observes the Ancient still starts after the
            // pair selected on the N page. Require the actual pair to be bound.
            var required = model.Evaluation.RequiredBonesCombination;
            if (model.Evaluation.NeowRoute?.RouteRelicKey != BaseGameModelKeys.Relics.NeowsBones ||
                required.Count != 2 || !required.Contains(leafy) || required[0] == required[1]) return null;
            companion = required[0] == leafy ? required[1] : required[0];
        }
        bool both = !condition.IsBones || condition.PickupOrder == TransformationPickupOrder.Any;
        bool leafyFirst = both || condition.LeafyFirst;
        if (model.Evaluation.RequiredBonesAcquisitionOrder is { Count: 2 } order)
        {
            bool orderedLeafyFirst = order[0] == leafy;
            if (!both && orderedLeafyFirst != leafyFirst) return null;
            leafyFirst = orderedLeafyFirst;
            both = false;
        }

        if (companion != newLeaf)
        {
            bool ancientReadsCursor = condition.UsesAncient && condition.AncientOpportunityCount > 0 &&
                (condition.ResultRequirements.Count == 0 || condition.ResultRequirements.Any(row =>
                    row.Sources.Contains("A." + condition.RequiredAncientKey.Entry)));
            bool companionCanPrecede = both || !leafyFirst;
            if ((companionCanPrecede || ancientReadsCursor) &&
                !CompanionPreservesDraws(model, effects, companion, companionCanPrecede)) return null;
            return Opening(model, leafy, effects) is { Length: 2 } draws ? (draws, null) : null;
        }

        // Both pickups select initial Basics. ShadowDeck.TryTransform clears
        // their Basic flags. Removing those instances suffices to reconstruct
        // the next selection regardless of which replacement card was rolled.
        var orderedDeck = deck.OrderBy(card => card.PoolOrder).ToArray();
        ModelKey[][]? Pickups(bool startWithLeafy)
        {
            var firstPickup = startWithLeafy ? leafy : newLeaf;
            var secondPickup = startWithLeafy ? newLeaf : leafy;
            var firstDraws = Opening(model, firstPickup, effects);
            if (firstDraws is null) return null;
            NeowEffectCardSnapshot[] taken;
            if (startWithLeafy)
            {
                var strike = orderedDeck.First(c => c.IsBasic && c.IsStrike);
                var defend = orderedDeck.First(c => c.IsBasic && c.IsDefend);
                if (strike.InstanceId == defend.InstanceId) return null;
                taken = [strike, defend];
            }
            else
            {
                var strike = NewLeafNormalizedSourcePolicy.Select(orderedDeck, effects.CharacterStrikeKey);
                if (strike is null) return null;
                taken = [strike];
            }
            var remaining = orderedDeck.Where(card => !taken.Any(source => source.InstanceId == card.InstanceId)).ToArray();
            var secondDraws = Opening(model, secondPickup, effects with { OrderedDeck = remaining });
            return secondDraws is null ? null : [..firstDraws, ..secondDraws];
        }

        var first = Pickups(leafyFirst);
        if (first is null) return null;
        if (!both) return (first, null);
        var second = Pickups(!leafyFirst);
        if (second is null) return null;
        return first.Zip(second).All(pair => pair.First.SequenceEqual(pair.Second))
            ? (first, null) : (first, second);
    }

    private static bool CompanionPreservesDraws(SearchModel model, NeowEffectAuthoritySnapshot effects,
        ModelKey companion, bool precedesLeafy)
    {
        if (!Beta110FastRelicCatalog.TryGetId(companion, out byte id)) return false;
        if (id is Beta110FastRelicCatalog.LargeCapsule or Beta110FastRelicCatalog.SmallCapsule)
            return !precedesLeafy && OpeningTransformationContinuation.HasNativeCapsuleCursor(model.Authority);
        if (!NeowBonesOrderReuse.PreservesTransformationStream(id)) return false;
        if (!precedesLeafy) return true;
        if (id == Beta110FastRelicCatalog.NeowsTalisman)
        {
            // In-place upgrades can supply a different identity in a captured
            // custom deck, changing which identity Leafy's pool excludes.
            var basics = effects.OrderedDeck!.Where(c => c.IsBasic && (c.IsStrike || c.IsDefend));
            return basics.All(c => !c.CanUpgrade || c.UpgradeTargetKey is null || c.UpgradeTargetKey == c.CardKey);
        }
        int removals = id switch
        {
            Beta110FastRelicCatalog.PreciseScissors => 1,
            Beta110FastRelicCatalog.PrecariousShears => 2,
            _ => 0
        };
        if (removals == 0) return true;
        // Every legal removal choice leaves a Strike and Defend from the
        // same captured source; otherwise order can change even the draw count.
        bool Survives(NeowEffectCardSnapshot[] cards) => cards.Length > removals &&
            cards.All(c => c.CardKey == cards[0].CardKey && c.PoolId == cards[0].PoolId);
        return Survives(effects.OrderedDeck!.Where(c => c.IsBasic && c.IsStrike).ToArray()) &&
            Survives(effects.OrderedDeck!.Where(c => c.IsBasic && c.IsDefend).ToArray());
    }
}
