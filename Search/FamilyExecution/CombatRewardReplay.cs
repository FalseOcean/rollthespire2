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
    internal Beta110FastEffectCatalog Catalog { get; }
    internal Beta110CombatRewardFastPlan Plan { get; }
    internal int RouteCount { get; }
    internal bool CanUsePotionPrefix { get; }
    internal int? PrecedingNicheDraws { get; }
    internal int[] CapsuleNicheAdvances { get; } = new int[4];
    internal bool KaleidoscopeCountOnly { get; }
    internal bool ReplayActualBonesPair { get; }
    internal byte[] BonesPool { get; } = [];
    internal bool DynamicCapsuleNicheUnknown { get; }
    internal bool CapsuleHeldReplay { get; }
    internal HashSet<ushort> ExplicitHeldIds { get; }
    internal string ConservativeOpeningReason { get; } = "";
    internal bool ConservativelyKeeps => ConservativeOpeningReason.Length != 0;

    internal CombatRewardReplay(ExactSearchExecutionRequest request, int? precedingNicheDraws = 0)
    {
        if (precedingNicheDraws < 0) throw new ArgumentOutOfRangeException(nameof(precedingNicheDraws));
        PrecedingNicheDraws = precedingNicheDraws;
        var keys = request.Evaluation.NormalCombatRewardConditions.SelectMany(c => c.Cards.Any.Concat(c.Cards.All)
            .Concat(c.Cards.Ban).Concat(c.Potions.Any).Concat(c.Potions.All).Concat(c.Potions.Ban));
        Catalog = Beta110FastEffectCatalogCompiler.Compile(request.Authority.EffectAuthority, keys);
        if (request.Authority.PlayersCount > 1) Catalog = CombatRewardPartyCatalog.Project(request, Catalog);
        Plan = Beta110CombatRewardFastPlanCompiler.CompileForFamily(request, Catalog);
        if (request.Authority.PlayersCount > 1 && request.CompiledSearch.NormalizedQuery.OpeningRoute is null)
            Plan = Plan with { OpeningConsumption = Beta110CombatRewardOpeningConsumptionProjection.Empty,
                ExplicitContext = Beta110CombatRewardExplicitContext.Empty(request.ProfileId),
                Fingerprint = Plan.Fingerprint + ":PartyNeutralOpening" };
        if (!Plan.Enabled) throw new InvalidOperationException("C.CombatReward.CompilationFailure:" + Plan.DisableReason);
        _playerSlot = request.Authority.PlayerSlotIndex;
        _players = request.Authority.PlayersCount;
        if (_players == 1 && (_playerSlot != 0 || precedingNicheDraws != 0 || Plan.HasDistinctBattleAssignments))
            throw new InvalidOperationException("C.SingleplayerContainsPartyContinuation");
        _ascension = request.Ascension;
        _defect = request.CharacterKey == BaseGameModelKeys.Characters.Defect;
        // Only the shuffle's draw count is needed; no actual pair observation.
        _bonesPoolCount = request.Authority.EffectAuthority?.BonesEligibleRelics?
            .Count(key => key != BaseGameModelKeys.Relics.NeowsBones) ?? 0;
        if (Plan.OpeningConsumption.ReplayBonesOffer && _bonesPoolCount < 2)
        {
            if (_players == 1) throw new InvalidOperationException("C.CombatReward.BonesShufflePoolUnavailable");
            ConservativeOpeningReason = "BonesShufflePoolUnavailable";
        }
        bool fixedOrder = request.CompiledSearch.NormalizedQuery.OpeningRouteRelicRequirement?.OrderMode == BonesRouteOrderMode.ExactOrder ||
            request.Evaluation.RequiredBonesAcquisitionOrder.Count == 2;
        ReplayActualBonesPair = _players > 1 && Plan.OpeningConsumption.ReplayBonesOffer &&
            Plan.OpeningConsumption.OrderedRelicIds.Length < 2;
        RouteCount = ReplayActualBonesPair || Plan.OpeningConsumption.ReplayBonesOffer && Plan.OpeningConsumption.OrderedRelicIds.Length == 2 && !fixedOrder ? 2 : 1;
        KaleidoscopeCountOnly = Catalog.OtherCharacterPools.Length >= 3 &&
            Catalog.OtherCharacterPools.All(p => p.TotalCount > 0);
        var upgrades = NeowAuthoredUpgradeContinuation.Compile(request);
        DynamicCapsuleNicheUnknown = upgrades is not null;
        byte[] children = Plan.OpeningConsumption.OrderedRelicIds;
        if (ReplayActualBonesPair)
        {
            BonesPool = (request.Authority.EffectAuthority!.BonesEligibleRelics ?? [])
                .Where(k => k != BaseGameModelKeys.Relics.NeowsBones)
                .Select(k => Beta110FastRelicCatalog.TryGetId(k, out byte id) ? id : Beta110FastRelicCatalog.InvalidId).ToArray();
            if (request.Authority.EffectAuthority.BonesEligibilityExact != true || BonesPool.Length > 32 ||
                BonesPool.Any(id => id == Beta110FastRelicCatalog.InvalidId))
                ConservativeOpeningReason = "ActualBonesPoolOutsideAuditedNumericReplay";
            else if (BonesPool.Any(id => !HasNeutralObservableHeldImpact(request.ProfileId, id)))
                ConservativeOpeningReason = "ActualBonesHeldRewardImpactRequiresRealRoute";
        }
        for (int route = 0; route < RouteCount; route++)
        for (int index = 0; index < children.Length; index++)
        {
            byte current = children[route == 0 ? index : children.Length - 1 - index];
            byte previous = index == 0 ? Beta110FastRelicCatalog.InvalidId : children[route == 0 ? index - 1 : children.Length - index];
            CapsuleNicheAdvances[route * 2 + index] = upgrades?.Advance(current, previous) ?? 0;
        }
        if (_players > 1 && request.CompiledSearch.NormalizedQuery.OpeningRoute is not null)
        {
            var nested = RolltheSpire2.Search.Semantics.PartyInitialQuery.CapsuleEffectPremise(
                request.CompiledSearch.NormalizedQuery).Values.SelectMany(keys => keys);
            foreach (var key in nested.Distinct())
                if (!VanillaRelicRewardEffects.TryGet(request.ProfileId, key, out var effects) ||
                    !effects.NestedOnObtainPreservesRewardContinuation ||
                    !effects.IsHeldNeutralForNormalCombatReward && !OpeningCombatRewardImpactAdapterRegistry.TryResolve(request.ProfileId, key, out _))
                { ConservativeOpeningReason = "AuthoredNestedRewardEffectUnresolved:" + key; break; }
        }
        CapsuleHeldReplay = _players > 1 && children.Concat(ReplayActualBonesPair ? BonesPool : [])
            .Any(id => id is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule);
        ExplicitHeldIds = Plan.ExplicitContext.SourceRelicKeys.Select(k => Catalog.TryGetDenseId(k, out var id) ? id : ushort.MaxValue).ToHashSet();
        if (CapsuleHeldReplay && !Catalog.RelicBagAuthorityExact)
            ConservativeOpeningReason = "CapsulePersonalInitialBagAuthorityUnavailable";
        if (!KaleidoscopeCountOnly && (children.Contains(Beta110FastRelicCatalog.Kaleidoscope) ||
            ReplayActualBonesPair && BonesPool.Contains(Beta110FastRelicCatalog.Kaleidoscope)) &&
            (!PrecedingNicheDraws.HasValue || CapsuleNicheAdvances.Any(n => n < 0) ||
                ReplayActualBonesPair && DynamicCapsuleNicheUnknown))
            ConservativeOpeningReason = "KaleidoscopeUnknownArrivalWithoutFixedDrawProof";
        CanUsePotionPrefix = request.Authority.CanUseCurrentModel && request.Authority.PlayersCount == 1 &&
            request.ProfileId == Compatibility.RuntimeProfileId.Beta111 && Plan.CardPoolAuthorityExact && Plan.PotionPoolAuthorityExact &&
            Plan.Predicates.Length > 0 && Plan.Predicates.All(p => !p.IsAnyBattle && !p.HasCardPredicate);
    }

    internal static bool HasNeutralObservableHeldImpact(Compatibility.RuntimeProfileId profile, byte id)
    {
        var key = Beta110FastRelicCatalog.KeyOf(id);
        if (!VanillaRelicRewardEffects.TryGet(profile, key, out var effect)) return false;
        if (effect.IsHeldNeutralForNormalCombatReward) return true;
        // Among current Neow ids, only Silken Tress and Silver Crucible have
        // non-neutral held adapters. Core applies their enchant/upgrade mutations
        // after identity and natural upgrade rolls, without consuming more RNG.
        // C predicates observe identity, potion and gold, not these mutations.
        return OpeningCombatRewardImpactAdapterRegistry.TryResolve(profile, key, out var adapter) &&
            adapter.Operations.All(operation => operation.Kind is
                OpeningCombatRewardImpactOperationKind.ForceUpgradeCardType or
                OpeningCombatRewardImpactOperationKind.UpgradeNextCardRewards or
                OpeningCombatRewardImpactOperationKind.EnchantFirstCardRewardWithGlam);
    }

    internal Beta110OpeningRewardState Opening(ulong root, int route) => Opening(root, route, out _);

    internal Beta110OpeningRewardState Opening(ulong root, int route, out UpFrontRngCheckpoint? nicheState)
    {
        if (ConservativelyKeeps) throw new InvalidOperationException("C.CombatReward.OpeningUnresolved:" + ConservativeOpeningReason);
        if ((uint)route >= (uint)RouteCount) throw new ArgumentOutOfRangeException(nameof(route));
        return _players > 1 ? OpeningParty(root, route, out nicheState) : OpeningSingleplayer(root, route, out nicheState);
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
        (potionPrefix ? Beta110CombatRewardFastStage.EvaluatePotionPrefixForFamily(opening, Plan, Catalog, _ascension) :
            Beta110CombatRewardFastStage.EvaluateForFamily(opening, Plan, Catalog, _ascension)).Status ==
            Beta110CombatRewardRouteProjectionStatus.ExactProjection;
}
