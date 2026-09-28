using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class CombatRewardReplay
{
    private Beta110OpeningRewardState OpeningParty(ulong root, int route, out UpFrontRngCheckpoint? nicheState)
    {
        var rewards = new Beta110FastRng(unchecked(root + (ulong)_playerSlot + RewardsHash));
        var niche = new Beta110FastRng(unchecked(root + NicheHash));
        bool nicheKnown = PrecedingNicheDraws.HasValue;
        for (int draw = 0; draw < PrecedingNicheDraws.GetValueOrDefault(); draw++) _ = niche.NextDouble();
        Span<byte> actualPool = stackalloc byte[ReplayActualBonesPair ? BonesPool.Length : 0];
        ReadOnlySpan<byte> children = Plan.OpeningConsumption.OrderedRelicIds;
        Span<ushort> bag = stackalloc ushort[CapsuleHeldReplay ? Beta110FastEffectCatalogCompiler.MaximumRelicBagEntries : 0];
        int bagCount = -1;
        var held = new Beta110OpeningRewardState { RewardInfluenceAuthorityExact = true };
        if (ReplayActualBonesPair)
        {
            BonesPool.CopyTo(actualPool);
            rewards.UnstableShuffle(actualPool);
        }
        else if (Plan.OpeningConsumption.ReplayBonesOffer) rewards.ConsumeUnstableShuffle(_bonesPoolCount);
        int childCount = ReplayActualBonesPair ? 2 : children.Length;
        for (int i = 0; i < childCount; i++)
        {
            int at = route == 0 ? i : childCount - 1 - i;
            byte id = ReplayActualBonesPair ? actualPool[at] : children[at];
            if (CapsuleHeldReplay && id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule)
            {
                if (bagCount < 0) bagCount = CombatRewardCapsuleReplay.Initialize(root, Catalog, bag);
                int pulls = id == Beta110FastRelicCatalog.SmallCapsule ? 1 : 2;
                for (int pull = 0; pull < pulls; pull++)
                {
                    ushort index = CombatRewardCapsuleReplay.Pull(Catalog, bag[..bagCount], ref rewards);
                    if (index == ushort.MaxValue) continue;
                    var relic = Catalog.OrdinaryRelics[index];
                    var impact = relic.RewardCapability;
                    held.RewardInfluenceAuthorityExact &= impact.InfluenceSupported;
                    if (ExplicitHeldIds.Contains(relic.DenseId)) continue;
                    held.InfluenceFlags |= impact.InfluenceFlags & ~Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation;
                    held.AdditionalCardRewardCount += impact.AdditionalCardRewardCount;
                    held.FixedGoldAmount += impact.FixedGoldAmount;
                }
            }
            else if (!CombatRewardOpeningReplay.TryReplayQueryLiteralRelicRewardsConsumption(
                    id, Catalog, _ascension, _defect, ref rewards, ref niche, requireAuthority: false, multiplayer: _players > 1,
                    nicheKnown: nicheKnown))
                throw new InvalidOperationException("C.CombatReward.OpeningConsumptionFailed:" + id);
            int advance = ReplayActualBonesPair ? DynamicCapsuleNicheUnknown &&
                id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule ? -1 : 0 :
                CapsuleNicheAdvances[route * 2 + i];
            if (advance < 0) nicheKnown = false;
            else if (nicheKnown) for (int draw = 0; draw < advance; draw++) _ = niche.NextDouble();
        }
        if (Plan.OpeningConsumption.ReplayBonesOffer && nicheKnown) _ = niche.NextDouble(); // Final generated curse, after both obtains.
        nicheState = nicheKnown ? niche.CaptureCheckpoint() : null;
        return CompleteOpening(rewards, held);
    }
}
