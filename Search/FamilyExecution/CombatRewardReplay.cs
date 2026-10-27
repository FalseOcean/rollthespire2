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
    private static readonly ulong RewardsHash = XxHash64.Hash("rewards"u8, 0UL);
    private static readonly ulong NicheHash = XxHash64.Hash("niche"u8, 0UL);
    private readonly int _playerSlot, _ascension, _bonesPoolCount, _players;
    private readonly bool _defect;
    private readonly bool _authoredNicheUnbound;
    private readonly CombatRewardCardSelection _cardSelection;
    internal Beta110FastEffectCatalog Catalog { get; }
    internal Beta110CombatRewardFastPlan Plan { get; }
    internal string CpuRevision => "C.CombatReward.Cpu.AuthoredPrefix.20261013.v2" +
        (UsesQueryLiteralPartyOpening ? ".PartyQueryLiteralOpening.20261027.v1" : "") +
        (Plan.NeedsCompleteMatchingReference ? ".CompletePositiveMatching.20261026.v1" : "");
    internal int RouteCount { get; }
    internal bool CanUsePotionPrefix { get; }
    internal int? PrecedingNicheDraws { get; }
    internal int[] CapsuleNicheAdvances { get; } = new int[4];
    internal bool KaleidoscopeCountOnly { get; }
    internal bool UsesQueryLiteralPartyOpening { get; }
    internal string ConservativeOpeningReason { get; } = "";
    internal bool ConservativelyKeeps => ConservativeOpeningReason.Length != 0;

    internal static bool TryCompile(ExactSearchExecutionRequest request, out Beta110FastEffectCatalog catalog,
        out Beta110CombatRewardFastPlan plan, out string issue)
    {
        var keys = request.Evaluation.NormalCombatRewardConditions.SelectMany(c => c.Cards.Any.Concat(c.Cards.All)
            .Concat(c.Cards.Ban).Concat(c.Potions.Any).Concat(c.Potions.All).Concat(c.Potions.Ban));
        catalog = Beta110FastEffectCatalogCompiler.Compile(request.Authority.EffectAuthority, keys);
        if (request.Authority.PlayersCount > 1) catalog = CombatRewardPartyCatalog.Project(request, catalog);
        plan = Beta110CombatRewardFastPlanCompiler.CompileForFamily(request, catalog);
        issue = plan.DisableReason;
        if (plan.Enabled && (catalog.CombatRewardCardPool.TotalCount < 3 || !catalog.CombatRewardPotionPool.HasAtLeastPerRarity(1)))
        {
            issue = catalog.CombatRewardCardPool.TotalCount < 3 ? "CombatRewardCardPoolUnavailable" : "CombatRewardPotionPoolUnavailable";
            return false;
        }
        return plan.Enabled;
    }

    internal CombatRewardReplay(ExactSearchExecutionRequest request, int? precedingNicheDraws = 0,
        Beta110FastEffectCatalog? catalog = null, Beta110CombatRewardFastPlan? plan = null)
    {
        if (precedingNicheDraws < 0) throw new ArgumentOutOfRangeException(nameof(precedingNicheDraws));
        PrecedingNicheDraws = precedingNicheDraws;
        if (catalog is null || plan is null) TryCompile(request, out catalog, out plan, out _);
        Catalog = catalog;
        _cardSelection = new(Catalog);
        Plan = plan;
        if (request.Authority.PlayersCount > 1 && request.CompiledSearch.NormalizedQuery.OpeningRoute is null)
            Plan = Plan with { OpeningConsumption = Beta110CombatRewardOpeningConsumptionProjection.Empty,
                ExplicitContext = Beta110CombatRewardExplicitContext.Empty(request.ProfileId),
                Fingerprint = Plan.Fingerprint + ":PartyNeutralOpening" };
        if (!Plan.Enabled) throw new InvalidOperationException("C.CombatReward.CompilationFailure:" + Plan.DisableReason);
        _playerSlot = request.Authority.PlayerSlotIndex;
        _players = request.Authority.PlayersCount;
        if (_players == 1 && (_playerSlot != 0 || precedingNicheDraws != 0))
            throw new InvalidOperationException("C.SingleplayerContainsPartyContinuation");
        _ascension = request.Ascension;
        _defect = request.CharacterKey == BaseGameModelKeys.Characters.Defect;
        // Only the shuffle's draw count is needed; no actual pair observation.
        _bonesPoolCount = request.Authority.EffectAuthority?.BonesEligibleRelics?
            .Count(key => key != BaseGameModelKeys.Relics.NeowsBones) ?? 0;
        if (Plan.OpeningConsumption.ReplayBonesOffer &&
            (_bonesPoolCount < 2 || request.Authority.EffectAuthority?.BonesEligibilityExact != true))
        {
            ConservativeOpeningReason = "BonesShufflePoolAuthorityUnavailable";
        }
        bool fixedOrder = request.CompiledSearch.NormalizedQuery.OpeningRouteRelicRequirement?.OrderMode == BonesRouteOrderMode.ExactOrder ||
            request.Evaluation.RequiredBonesAcquisitionOrder.Count == 2;
        byte[] children = Plan.OpeningConsumption.OrderedRelicIds;
        UsesQueryLiteralPartyOpening = _players > 1 &&
            (Plan.OpeningConsumption.ReplayBonesOffer && children.Length < 2 ||
             children.Any(id => id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule));
        RouteCount = Plan.OpeningConsumption.ReplayBonesOffer && children.Length == 2 && !fixedOrder ? 2 : 1;
        KaleidoscopeCountOnly = Catalog.OtherCharacterPools.Length >= 3 &&
            Catalog.OtherCharacterPools.All(p => p.TotalCount > 0);
        var grants = ExplicitOpeningRelicFacts.EnumerateGuaranteedRouteGrants(request.CompiledSearch.NormalizedQuery);
        // C-only numerical donor: singleton Any is a guarantee, and the newly
        // recognized Bones grants identify an authored Capsule source. Never
        // rewrite the request used by N, Exact, persistence or probability.
        var evaluation = request.Evaluation;
        var capsule = evaluation.CapsuleContainedRelics;
        var upgradeRequest = request with { Evaluation = evaluation with {
            RequiredBonesCombination = Plan.OpeningConsumption.ReplayBonesOffer
                ? evaluation.RequiredBonesCombination.Concat(grants).Distinct(ModelKeyComparer.Instance).ToArray()
                : evaluation.RequiredBonesCombination,
            CapsuleContainedRelics = capsule.Any.Count == 1
                ? new([], capsule.All.Append(capsule.Any[0]).Distinct(ModelKeyComparer.Instance).ToArray(), capsule.Ban)
                : capsule
        } };
        var upgrades = NeowAuthoredUpgradeContinuation.Compile(upgradeRequest);
        var heldFacts = ExplicitOpeningRelicFacts.EnumerateGuaranteed(request.CompiledSearch.NormalizedQuery);
        _authoredNicheUnbound = upgrades is not null &&
            !children.Any(id => id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule) &&
            heldFacts.Any(key => key == BaseGameModelKeys.OrdinaryRelics.Whetstone || key == BaseGameModelKeys.OrdinaryRelics.WarPaint);
        foreach (var grant in grants)
            if (!Beta110FastRelicCatalog.TryGetId(grant, out _))
                ConservativeOpeningReason = "AuthoredOpeningOpcodeUnavailable:" + grant;
        for (int route = 0; route < RouteCount; route++)
        for (int index = 0; index < children.Length; index++)
        {
            byte current = children[route == 0 ? index : children.Length - 1 - index];
            byte previous = index == 0 ? Beta110FastRelicCatalog.InvalidId : children[route == 0 ? index - 1 : children.Length - index];
            CapsuleNicheAdvances[route * 2 + index] = upgrades?.Advance(current, previous) ?? 0;
        }
        if (request.CompiledSearch.NormalizedQuery.OpeningRoute is not null)
        {
            // Only guaranteed acquisitions participate, including source-ambiguous
            // nested requirements. Alternatives do not become held facts.
            var nested = heldFacts.Except(grants, ModelKeyComparer.Instance);
            foreach (var key in nested)
                if (!VanillaRelicRewardEffects.TryGet(request.ProfileId, key, out var effects) ||
                    !effects.NestedOnObtainPreservesRewardContinuation ||
                    !effects.IsHeldNeutralForNormalCombatReward && !OpeningCombatRewardImpactAdapterRegistry.TryResolve(request.ProfileId, key, out _))
                { ConservativeOpeningReason = "AuthoredNestedRewardEffectUnresolved:" + key; break; }
        }
        if (!KaleidoscopeCountOnly && children.Contains(Beta110FastRelicCatalog.Kaleidoscope) &&
            (!PrecedingNicheDraws.HasValue || CapsuleNicheAdvances.Any(n => n < 0) || _authoredNicheUnbound))
            ConservativeOpeningReason = "KaleidoscopeUnknownArrivalWithoutFixedDrawProof";
        foreach (byte source in children.Distinct())
        {
            bool ready = source switch
            {
                Beta110FastRelicCatalog.ArcaneScroll => Catalog.CharacterRewardPool.Rare.Length > 0,
                Beta110FastRelicCatalog.MassiveScroll when _players > 1 => Catalog.MultiplayerRewardPool.TotalCount >= 3,
                Beta110FastRelicCatalog.HeftyTablet => Catalog.CharacterRewardPool.Rare.Length >= 3,
                Beta110FastRelicCatalog.LeadPaperweight => Catalog.ColorlessRewardPool.TotalCount >= 2,
                Beta110FastRelicCatalog.LostCoffer => Catalog.CharacterRewardPool.TotalCount >= 3 && Catalog.PotionPool.HasAtLeastPerRarity(1),
                Beta110FastRelicCatalog.ScrollBoxes => Catalog.CharacterRewardPool.Common.Length >= 4 && Catalog.CharacterRewardPool.Uncommon.Length >= 2,
                Beta110FastRelicCatalog.Kaleidoscope => Catalog.OtherCharacterPools.Length >= 3 && Catalog.OtherCharacterPools.All(p => p.TotalCount > 0),
                _ => true
            };
            if (!ready) ConservativeOpeningReason = "OpeningConsumptionAuthorityUnavailable:Source" + source;
        }
        CanUsePotionPrefix = request.Authority.CanUseCurrentModel && request.Authority.PlayersCount == 1 &&
            request.ProfileId == Compatibility.RuntimeProfileId.Beta111 && Plan.CardPoolAuthorityExact && Plan.PotionPoolAuthorityExact &&
            Plan.Predicates.Length > 0 && Plan.Predicates.All(p => !p.IsAnyBattle && !p.HasCardPredicate);
    }

    internal Beta110OpeningRewardState Opening(ulong root, int route) => Opening(root, route, out _);

    internal Beta110OpeningRewardState Opening(ulong root, int route, out UpFrontRngCheckpoint? nicheState)
    {
        if (ConservativelyKeeps) throw new InvalidOperationException("C.CombatReward.OpeningUnresolved:" + ConservativeOpeningReason);
        if ((uint)route >= (uint)RouteCount) throw new ArgumentOutOfRangeException(nameof(route));
        var opening = _players > 1 ? OpeningParty(root, route, out nicheState) : OpeningSingleplayer(root, route, out nicheState);
        if (_authoredNicheUnbound) nicheState = null;
        return opening;
    }

    private Beta110OpeningRewardState OpeningSingleplayer(ulong root, int route, out UpFrontRngCheckpoint? nicheState)
    {
        var rewards = new Beta110FastRng(unchecked(root + RewardsHash));
        var niche = new Beta110FastRng(unchecked(root + NicheHash));
        bool nicheKnown = true;
        if (Plan.OpeningConsumption.ReplayBonesOffer) rewards.ConsumeUnstableShuffle(_bonesPoolCount);
        ReadOnlySpan<byte> children = Plan.OpeningConsumption.OrderedRelicIds;
        for (int i = 0; i < children.Length; i++)
        {
            byte id = children[route == 0 ? i : children.Length - 1 - i];
            if (!CombatRewardOpeningReplay.TryReplayQueryLiteralRelicRewardsConsumption(
                    id, Catalog, _ascension, _defect, ref rewards, ref niche, requireAuthority: false,
                    multiplayer: false, nicheKnown: nicheKnown))
                throw new InvalidOperationException("C.CombatReward.OpeningConsumptionFailed:" + id);
            int advance = CapsuleNicheAdvances[route * 2 + i];
            if (advance < 0) nicheKnown = false;
            else if (nicheKnown) for (int draw = 0; draw < advance; draw++) _ = niche.NextDouble();
        }
        if (Plan.OpeningConsumption.ReplayBonesOffer && nicheKnown) _ = niche.NextDouble();
        nicheState = nicheKnown ? niche.CaptureCheckpoint() : null;
        return CompleteOpening(rewards, new Beta110OpeningRewardState { RewardInfluenceAuthorityExact = true });
    }

    private Beta110OpeningRewardState CompleteOpening(Beta110FastRng rewards, Beta110OpeningRewardState held)
    {
        var opening = new Beta110OpeningRewardState
        {
            Rewards = rewards, PotionOdds = 0.4f, CardRarityOffset = -0.05f,
            InfluenceFlags = held.InfluenceFlags, AdditionalCardRewardCount = held.AdditionalCardRewardCount,
            FixedGoldAmount = held.FixedGoldAmount,
            // Exact within this authored model, not a claim about unknown world state.
            RewardsContinuationAuthorityExact = true, RewardInfluenceAuthorityExact = held.RewardInfluenceAuthorityExact
        };
        Plan.ExplicitContext.ApplyTo(ref opening);
        return opening;
    }

    internal bool Matches(ulong root) => MatchesCore(root, false);

    private bool MatchesCore(ulong root, bool potionPrefix)
    {
        if (ConservativelyKeeps) return true;
        var first = Opening(root, 0);
        bool firstPass = Evaluate(first, potionPrefix);
        if (RouteCount == 1) return firstPass;
        var second = Opening(root, 1);
        // Evaluate every distinct authored predecessor, including error checking.
        return second.EquivalentTo(first) ? firstPass : (Evaluate(second, potionPrefix) | firstPass);
    }

    internal bool MatchesPotionPrefix(ulong root)
    {
        if (!CanUsePotionPrefix) throw new InvalidOperationException("C.PotionPrefixNotAdmitted");
        return MatchesCore(root, true);
    }

    private bool Evaluate(Beta110OpeningRewardState opening, bool potionPrefix) =>
        !opening.RewardInfluenceAuthorityExact ||
        (potionPrefix ? Beta110CombatRewardFastStage.EvaluatePotionPrefixForFamily(opening, Plan, Catalog, _ascension, _cardSelection) :
            Beta110CombatRewardFastStage.EvaluateForFamily(opening, Plan, Catalog, _ascension, _cardSelection)).Status ==
            Beta110CombatRewardRouteProjectionStatus.ExactProjection;
}
