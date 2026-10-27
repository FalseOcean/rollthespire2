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
        ReadOnlySpan<byte> children = Plan.OpeningConsumption.OrderedRelicIds;
        // Only the authored mechanics participate. Unknown grants and Capsule
        // contents are neutral; their identity does not enter C's per-root state.
        if (Plan.OpeningConsumption.ReplayBonesOffer) rewards.ConsumeUnstableShuffle(_bonesPoolCount);
        for (int i = 0; i < children.Length; i++)
        {
            byte id = children[route == 0 ? i : children.Length - 1 - i];
            if (!CombatRewardOpeningReplay.TryReplayQueryLiteralRelicRewardsConsumption(
                    id, Catalog, _ascension, _defect, ref rewards, ref niche, requireAuthority: false, multiplayer: _players > 1,
                    nicheKnown: nicheKnown))
                throw new InvalidOperationException("C.CombatReward.OpeningConsumptionFailed:" + id);
            int advance = CapsuleNicheAdvances[route * 2 + i];
            if (advance < 0) nicheKnown = false;
            else if (nicheKnown) for (int draw = 0; draw < advance; draw++) _ = niche.NextDouble();
        }
        if (Plan.OpeningConsumption.ReplayBonesOffer && nicheKnown) _ = niche.NextDouble(); // Final generated curse, after both obtains.
        nicheState = nicheKnown ? niche.CaptureCheckpoint() : null;
        return CompleteOpening(rewards, new Beta110OpeningRewardState { RewardInfluenceAuthorityExact = true });
    }
}
