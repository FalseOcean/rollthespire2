using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.FamilyExecution;

// Numeric packing adopted from N0/P1. No historical executable plan is created.
internal sealed record NeowFamilyGpuPlan(uint[] Meta, uint[] PoolMeta, uint[] Cards,
    uint[] Strike, uint[] Defend, uint[] Bones, uint[] Conditions)
{
    internal bool HasAuthoredUpgrades { get; init; }
    internal const string DenseRevision = "N.Neow.Gpu.LocalDonor.DenseCarry8.CanonicalAbi1Ready.20260905.v2";
    internal const string CompactRevision = "N.Neow.Gpu.LocalDonor.CompactFullDecode.CanonicalAbi1Ready.20260905.v2";
    internal const string StagedDenseRevision = "N.Neow.Gpu.LocalDonor.DensePreBonesCarry8.CanonicalAbi1Ready.20260905.v2";
    internal const string LeafyDenseRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.DenseCarry8.CanonicalAbi1Ready.20260906.v1";
    internal const string LeafyCompactRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.CompactFullDecode.CanonicalAbi1Ready.20260906.v1";
    internal const string LeafyStagedDenseRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.DensePreBonesCarry8.CanonicalAbi1Ready.20260906.v1";
    internal bool UsesStagedDense => Meta[66] != 0;
    internal bool UsesLeafyPreGate => Meta[70] != 0;
    // Bounded N-private scheduling; explicit controls retain the same-build oracle.
    internal int DirectNestedMode { get; init; }
    internal string DirectNestedRevision(bool compact) => $"N.Neow.Gpu.DirectNested.{DirectNestedName}.{(compact ? "Compact" : UsesStagedDense ? "DensePreBones" : "DenseCarry8")}.CanonicalAbi1Ready.20260909.v1";
    private string DirectNestedName => DirectNestedMode switch {
        1 => "KaleidoscopeFirstReplay", 2 => "KaleidoscopeFirstContinue", 3 => "KaleidoscopeBothReplay",
        4 => "KaleidoscopeLocalReject", 5 => "NewLeafReplay", 6 => "NewLeafPredicateFirst",
        7 => "ScrollSpecialFirst", 8 => "ScrollSpecialLocal", 9 => "KaleidoscopeDirectControl",
        10 => "LostLocal", 11 => "LostRareReplay", 12 => "LostRareContinue", 13 => "LostPotionRareReplay", 14 => "LostDirectControl",
        20 => "LeadRareLocal", 21 => "LeadRareReplay", 22 => "LeadIdentityDirect",
        30 => "ArcanePredicateFirst", 31 => "ArcaneIndexFirst", 32 => "ArcaneDirectControl", 33 => "ArcaneIndexLocal",
        40 => "PhialRareReplay", 41 => "PhialRareLocal", 42 => "PhialIdentityDirect",
        100 => "BonesEarlyAcceptControl", 101 => "BonesKaleidoscopeRareLocalControl",
        102 => "BonesIndependentPairLocal",
        _ => throw new InvalidOperationException("NDirectModeInvalid") };
    internal const int CurseCapacity = 1 << 22;
    internal const int PairCapacity = 1 << 16;
    internal static bool TryCreate(NeowReplayPlan plan, out NeowFamilyGpuPlan? gpu, out string issue)
    {
        gpu = null; issue = "";
        var a = plan.Authority; var c = a.EffectCatalog;
        if (a.EligibleCurseRelicIds.Length > 10 || a.BonesEligibleRelicIds.Length > 64 || c.OtherCharacterPools.Length > 16)
        { issue = "DonorLocalArrayCapacity"; return false; }
        bool rewards = (plan.EnabledDomains & ~(Beta110FastDomain.FinalCurse | Beta110FastDomain.NewLeafTransform |
            Beta110FastDomain.LeafyPoulticeTransforms | Beta110FastDomain.PhialHolsterPotions)) != 0;
        bool niche = rewards || plan.HasFinalCurseFastProjection || (plan.EnabledDomains & Beta110FastDomain.NewLeafTransform) != 0;
        bool transform = (plan.EnabledDomains & Beta110FastDomain.LeafyPoulticeTransforms) != 0;
        bool potion = (plan.EnabledDomains & Beta110FastDomain.PhialHolsterPotions) != 0;
        // Unknown continuation authority must never turn into GPU rejection.
        if ((rewards && (!c.CharacterRewardAuthorityExact || !c.ColorlessRewardAuthorityExact || !c.PotionAuthorityExact ||
                !c.OtherCharacterCardAuthorityExact || c.CharacterRewardPool.Rare.Length < 3 || c.OtherCharacterPools.Length < 3)) ||
            (niche && (!c.OtherCharacterCardAuthorityExact || c.OtherCharacterPools.Length < 3 || c.NewLeafTransformPool.Length == 0)) ||
            (transform && (c.LeafyStrikeTransformPool.Length == 0 || c.LeafyDefendTransformPool.Length == 0)) ||
            (potion && !c.PotionAuthorityExact))
        { issue = "DonorContinuationAuthorityUnavailable"; return false; }
        if ((rewards && (c.CharacterRewardPool.Common.Length < 4 || c.CharacterRewardPool.Uncommon.Length < 2 ||
                c.ColorlessRewardPool.Common.Length + c.ColorlessRewardPool.Uncommon.Length + c.ColorlessRewardPool.Rare.Length < 2 ||
                c.PotionPool.AllAllowed.Length == 0 ||
                c.OtherCharacterPools.Any(pool => pool.Common.Length + pool.Uncommon.Length + pool.Rare.Length == 0))) ||
            (potion && c.PotionPool.AllAllowed.Length < 2))
        { issue = "DonorContinuationPoolTooSmall_CpuReference"; return false; }
        var rows = new uint[32 * 5 + (plan.AuthoredUpgrades is null ? 0 : 66)];
        if (plan.AuthoredUpgrades is { } upgrades)
            for (int i = 0; i < upgrades.Advances.Length; i++) rows[160 + i] = unchecked((uint)upgrades.Advances[i]);
        for (int i = 0; i < 32; i++) { rows[i * 5 + 1] = rows[i * 5 + 2] = rows[i * 5 + 3] = uint.MaxValue; }
        foreach (var group in plan.StructuredConditions.GroupBy(x => x.SourceRelicId))
        {
            int o = group.Key * 5; var conditions = group.ToArray();
            if (conditions.Length > 1 && group.Key is not Beta110FastRelicCatalog.LostCoffer and not Beta110FastRelicCatalog.ScrollBoxes)
            { issue = "DonorMultipleLocalPredicates"; return false; }
            rows[o + 4] = 0x80000000;
            foreach (var condition in conditions)
            {
                if (group.Key == Beta110FastRelicCatalog.LostCoffer)
                {
                    bool card = condition.Kind == Beta110FastStructuredConditionKind.LostCofferCardOffer;
                    int slot = o + (card ? 1 : 2);
                    if (rows[slot] != uint.MaxValue) { issue = "DonorMultipleLostCofferTargets"; return false; }
                    rows[slot] = condition.Target0; rows[o] |= card ? 1u : 2u;
                }
                else if (condition.Kind == Beta110FastStructuredConditionKind.ScrollBoxesTripleClaw) rows[o + 4] |= 2;
                else
                {
                    if (rows[o] != 0) { issue = "DonorMultipleScrollTargets"; return false; }
                    rows[o] = condition.TargetCount; rows[o + 1] = condition.Target0;
                    rows[o + 2] = condition.Target1; rows[o + 3] = condition.Target2;
                    if (group.Key == Beta110FastRelicCatalog.ScrollBoxes) rows[o + 4] |= 1;
                }
            }
        }
        uint[] meta = new uint[85 + plan.StructuredConditions.Length];
        meta[5] = (uint)a.PlayerSlotIndex; meta[6] = (uint)a.PlayersCount; meta[7] = (uint)a.Ascension;
        meta[8] = (uint)a.BonesEligibleRelicIds.Length; meta[9] = (uint)c.OtherCharacterPools.Length;
        meta[12] = plan.First; meta[13] = plan.Second;
        // Adopt P9's immutable first-draw proof. A top-only acceptance skips
        // positives, never the independent Bones/local continuation below it.
        var topGate = NeowNumericCompilation.BuildCurseFirstDrawGateP9(a,
            plan.TopAny, plan.TopAll, plan.TopBan, plan.Selected == 255 ? 0 : Beta110FastRelicCatalog.Bit(plan.Selected),
            plan.RequireBones || plan.Bones, false, false);
        meta[68] = topGate.AcceptedCurseOrdinalMask; meta[69] = topGate.RejectedCurseOrdinalMask;
        // P2 tracks the two required identities instead of a complete Bones array.
        // A proved pair also supplies both local continuation sources.
        ulong requiredPair = plan.BonesAll;
        if (plan.First != 255 && plan.Second != 255)
            requiredPair |= Beta110FastRelicCatalog.Bit(plan.First) | Beta110FastRelicCatalog.Bit(plan.Second);
        byte[] tracked = a.BonesEligibleRelicIds.Where(id => (requiredPair & Beta110FastRelicCatalog.Bit(id)) != 0).ToArray();
        if (plan.Bones && System.Numerics.BitOperations.PopCount(requiredPair) == 2 && tracked.Length == 2)
        {
            meta[67] = 1;
            meta[25] = (uint)Array.IndexOf(a.BonesEligibleRelicIds, tracked[0]);
            meta[26] = (uint)Array.IndexOf(a.BonesEligibleRelicIds, tracked[1]);
            meta[27] = tracked[0]; meta[28] = tracked[1];
            int bonesOrdinal = Array.IndexOf(a.EligibleCurseRelicIds, Beta110FastRelicCatalog.NeowsBones);
            // Tiny synthetic/unusual pools are dense at the pair checkpoint;
            // use the full-capacity fused physical instead of predictable overflow.
            if (a.BonesEligibleRelicIds.Length >= 16 && bonesOrdinal >= 0 && topGate.AcceptedCurseOrdinalMask == (1u << bonesOrdinal) &&
                (topGate.AcceptedCurseOrdinalMask | topGate.RejectedCurseOrdinalMask) == (1u << a.EligibleCurseRelicIds.Length) - 1u)
                meta[66] = 1;
        }
        void U64(int at, ulong value) { meta[at] = (uint)value; meta[at + 1] = (uint)(value >> 32); }
        U64(31, XxHash64.Hash("NEOW"u8, 0)); U64(33, NeowFamilyReplay.RewardsHash);
        U64(35, XxHash64.Hash("niche"u8, 0)); U64(37, XxHash64.Hash("transformations"u8, 0));
        U64(62, XxHash64.Hash("combat_potion_generation"u8, 0));
        meta[39] = (uint)a.EligibleCurseRelicIds.Length;
        U64(40, plan.TopAny); U64(42, plan.TopAll); U64(44, plan.TopBan);
        U64(46, plan.Selected == 255 ? 0 : Beta110FastRelicCatalog.Bit(plan.Selected));
        U64(48, plan.BonesAny); U64(50, plan.BonesAll); U64(52, plan.BonesBan);
        meta[56] = (plan.RequireBones ? 1u : 0) | (plan.Bones ? 2u : 0) | (a.AllCharacterCardPoolsUnlocked ? 4u : 0) |
            (a.ScrollBoxesAllowed ? 8u : 0) | (a.UsesDefectScrollBoxesRule ? 32u : 0);
        meta[58] = (uint)c.LeafyStrikeTransformPool.Length; meta[59] = meta[60] = (uint)c.LeafyDefendTransformPool.Length;
        meta[61] = (uint)c.NewLeafTransformPool.Length;
        for (int i = 0; i < a.EligibleCurseRelicIds.Length; i++) meta[71 + i] = a.EligibleCurseRelicIds[i];
        meta[81] = plan.Selected; meta[82] = (rewards ? 1u : 0) | (niche ? 2u : 0) | (transform ? 4u : 0) | (potion ? 8u : 0);
        meta[83] = plan.HasFinalCurseFastProjection ? 1u : 0; meta[84] = (uint)plan.StructuredConditions.Length;
        for (int i = 0; i < plan.StructuredConditions.Length; i++) meta[85 + i] = plan.StructuredConditions[i].SourceRelicId;
        Beta110FastStructuredCondition[] leafy = plan.StructuredConditions.Where(x =>
            x.SourceRelicId == Beta110FastRelicCatalog.LeafyPoultice &&
            x.Kind == Beta110FastStructuredConditionKind.LeafyPoulticeTransforms).ToArray();
        bool fixedLeafyFirst = plan.Bones && plan.First == Beta110FastRelicCatalog.LeafyPoultice &&
            plan.Second != Beta110FastRelicCatalog.InvalidId && plan.Second != plan.First;
        ulong leafyGolden = Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.LeafyPoultice) |
            Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.GoldenPearl);
        bool exactLeafyGoldenPair = plan.Bones && requiredPair == leafyGolden;
        bool routeSafe = !plan.Bones
            ? plan.Selected == Beta110FastRelicCatalog.LeafyPoultice
            : plan.LeafyBonesInitialTransformInvariant || fixedLeafyFirst || exactLeafyGoldenPair;
        // Transformations is root-derived, but Exact transforms the current first
        // basic Strike/Defend. Bones can hoist only when the captured same-source
        // starter multiplicity survives every mapped vanilla companion, or for
        // a direct Leafy-first / proven deck-neutral GoldenPearl route.
        // 1/1024 is a private physical heuristic, not semantic probability authority.
        if (c.LeafyTransformAuthorityExact && leafy.Length == 1 && leafy[0].TargetCount == 2 && routeSafe)
        {
            long matching = 0;
            foreach (ushort strike in c.LeafyStrikeTransformPool)
            foreach (ushort defend in c.LeafyDefendTransformPool)
                if ((strike == leafy[0].Target0 && defend == leafy[0].Target1) ||
                    (strike == leafy[0].Target1 && defend == leafy[0].Target0)) matching++;
            long outcomes = (long)c.LeafyStrikeTransformPool.Length * c.LeafyDefendTransformPool.Length;
            if (matching > 0 && matching * 1024 <= outcomes) meta[70] = 1;
        }
        // Retain the donor card/potion/curse layout, omitting every Relic Bag field.
        int trailer = 20 + c.OtherCharacterPools.Length * 6;
        uint[] poolMeta = new uint[trailer + 15]; var cards = new List<uint>();
        void Append(ushort[] ids, int index) { poolMeta[index] = (uint)cards.Count; poolMeta[index + 1] = (uint)ids.Length; cards.AddRange(ids.Select(x => (uint)x)); }
        void Pool(Beta110FastCardPool pool, int index) { Append(pool.Common, index); Append(pool.Uncommon, index + 2); Append(pool.Rare, index + 4); }
        Pool(c.CharacterRewardPool, 0); Pool(c.ColorlessRewardPool, 6);
        Append(c.PotionPool.Common, 12); Append(c.PotionPool.Uncommon, 14); Append(c.PotionPool.Rare, 16); Append(c.PotionPool.AllAllowed, 18);
        for (int i = 0; i < c.OtherCharacterPools.Length; i++) Pool(c.OtherCharacterPools[i], 20 + i * 6);
        Append(plan.RequiredFinalCurseIds, trailer + 6); Append(plan.BannedFinalCurseIds, trailer + 8);
        Append(c.GeneratedCurseIds, trailer + 12);
        gpu = new(meta, poolMeta, cards.ToArray(), c.LeafyStrikeTransformPool.Select(x => (uint)x).ToArray(),
            c.LeafyDefendTransformPool.Concat(c.NewLeafTransformPool).Select(x => (uint)x).ToArray(),
            a.BonesEligibleRelicIds.Select(x => (uint)x).ToArray(), rows) { HasAuthoredUpgrades = plan.AuthoredUpgrades is not null };
        string mode = Environment.GetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT") ?? "default";
        int selectedMode = mode switch { "default" => -1, "baseline" => 0, "replay" => 1, "continue" => 2, "both" => 3, "local" => 4,
            "new-replay" => 5, "new-pass" => 6, "scroll-first" => 7, "scroll-local" => 8,
            "identity-direct" => 9,
            "lost-local" => 10, "lost-replay" => 11, "lost-continue" => 12, "lost-potion-first" => 13, "lost-direct" => 14,
            "lead-local" => 20, "lead-first" => 21, "lead-direct" => 22,
            "arcane-first" => 30, "arcane-index-first" => 31, "arcane-direct" => 32, "arcane-index-local" => 33,
            "phial-first" => 40, "phial-local" => 41, "phial-direct" => 42,
            _ => throw new InvalidOperationException("UnknownNDirectExperiment:" + mode) };
        bool direct = plan.DirectNestedVanilla111 && !plan.Bones && !plan.RequireBones && !plan.HasFinalCurseFastProjection && a.PlayersCount == 1 &&
            a.AllCharacterCardPoolsUnlocked && plan.ExactOnly.Length == 0;
        if (direct &&
            plan.Selected == Beta110FastRelicCatalog.Kaleidoscope && plan.StructuredConditions.Length == 1 &&
            plan.StructuredConditions[0] is var k && k.Kind == Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets &&
            k.TargetCount == 2 && c.CardRarityByDenseId[k.Target0] == 3 && c.CardRarityByDenseId[k.Target1] == 3)
            gpu = gpu with { DirectNestedMode = selectedMode == -1 ? 3 : selectedMode <= 4 || selectedMode == 9 ? selectedMode : 0 };
        if (direct && plan.StructuredConditions.Length == 1)
        {
            var condition=plan.StructuredConditions[0];
            if(plan.Selected==Beta110FastRelicCatalog.NewLeaf && condition.Kind==Beta110FastStructuredConditionKind.NewLeafTransform && selectedMode is -1 or 5 or 6)
                gpu=gpu with {DirectNestedMode=selectedMode == -1 ? 6 : selectedMode};
            if(plan.Selected==Beta110FastRelicCatalog.ScrollBoxes && a.UsesDefectScrollBoxesRule &&
                condition.Kind==Beta110FastStructuredConditionKind.ScrollBoxesTripleClaw && selectedMode is -1 or 7 or 8)
                gpu=gpu with {DirectNestedMode=selectedMode == -1 ? 7 : selectedMode};
        }
        // Bounded direct reward scheduling; controls remain available for same-build A/B.
        if (direct)
        {
            var conditions = plan.StructuredConditions;
            if (plan.Selected == Beta110FastRelicCatalog.LostCoffer && conditions.Length is 1 or 2 &&
                conditions.All(x => x.Kind is Beta110FastStructuredConditionKind.LostCofferCardOffer or Beta110FastStructuredConditionKind.LostCofferPotion))
            {
                bool rareCard = conditions.Any(x => x.Kind == Beta110FastStructuredConditionKind.LostCofferCardOffer && c.CardRarityByDenseId[x.Target0] == 3);
                bool rarePotionOnly = conditions.Length == 1 && conditions[0].Kind == Beta110FastStructuredConditionKind.LostCofferPotion && c.PotionPool.Rare.Contains(conditions[0].Target0);
                if (selectedMode == -1)
                    gpu = gpu with { DirectNestedMode = rareCard ? 11 : 10 };
                else if (selectedMode is 10 or 14 || rareCard && selectedMode is 11 or 12 || rarePotionOnly && selectedMode == 13)
                    gpu = gpu with { DirectNestedMode = selectedMode };
            }
            if (conditions.Length == 1)
            {
                var condition = conditions[0];
                if (selectedMode == -1)
                {
                    if (plan.Selected == Beta110FastRelicCatalog.LeadPaperweight && condition.Kind == Beta110FastStructuredConditionKind.LeadPaperweightColorlessOffer)
                        gpu = gpu with { DirectNestedMode = c.CardRarityByDenseId[condition.Target0] == 3 ? 21 : 22 };
                    if (plan.Selected == Beta110FastRelicCatalog.ArcaneScroll && condition.Kind == Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard)
                        gpu = gpu with { DirectNestedMode = 31 };
                    if (plan.Selected == Beta110FastRelicCatalog.PhialHolster && condition.Kind == Beta110FastStructuredConditionKind.PhialHolsterPotions)
                        gpu = gpu with { DirectNestedMode = condition.TargetCount == 2 && c.PotionPool.Rare.Contains(condition.Target0) && c.PotionPool.Rare.Contains(condition.Target1) ? 40 : 42 };
                }
                if (plan.Selected == Beta110FastRelicCatalog.LeadPaperweight && condition.Kind == Beta110FastStructuredConditionKind.LeadPaperweightColorlessOffer &&
                    (selectedMode == 22 || c.CardRarityByDenseId[condition.Target0] == 3 && selectedMode is 20 or 21))
                    gpu = gpu with { DirectNestedMode = selectedMode };
                if (plan.Selected == Beta110FastRelicCatalog.ArcaneScroll && condition.Kind == Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard && selectedMode is >= 30 and <= 33)
                    gpu = gpu with { DirectNestedMode = selectedMode };
                if (plan.Selected == Beta110FastRelicCatalog.PhialHolster && condition.Kind == Beta110FastStructuredConditionKind.PhialHolsterPotions &&
                    (selectedMode == 42 || c.PotionPool.Rare.Contains(condition.Target0) &&
                     (condition.TargetCount == 1 || c.PotionPool.Rare.Contains(condition.Target1)) && selectedMode is 40 or 41))
                    gpu = gpu with { DirectNestedMode = selectedMode };
            }
        }
        string bonesMode = Environment.GetEnvironmentVariable("RT2_N_BONES_EXPERIMENT") ?? "";
        if (bonesMode is not ("" or "baseline" or "early-accept" or "rare-local" or "pair-local"))
            throw new InvalidOperationException("UnknownNBonesExperiment:" + bonesMode);
        // Fixed-pair controls; only independent K + Phial concrete operators are
        // a default. No stateful pickup, Capsule, Final Curse or identity-only tax.
        if ((bonesMode is "" or "early-accept" or "rare-local" or "pair-local") && plan.DirectNestedVanilla111 &&
            plan.Bones && a.PlayersCount == 1 && a.AllCharacterCardPoolsUnlocked &&
            plan.ExactOnly.Length == 0 && plan.StructuredConditions.Length > 0 && !plan.HasFinalCurseFastProjection && meta[67] == 1 &&
            tracked.Contains(Beta110FastRelicCatalog.Kaleidoscope) &&
            tracked.All(id => id is Beta110FastRelicCatalog.Kaleidoscope or Beta110FastRelicCatalog.PhialHolster or
                Beta110FastRelicCatalog.ArcaneScroll or Beta110FastRelicCatalog.NewLeaf))
        {
            bool rareK = plan.StructuredConditions.Any(x => x.SourceRelicId == Beta110FastRelicCatalog.Kaleidoscope &&
                x.Kind == Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets &&
                c.CardRarityByDenseId[x.Target0] == 3 && (x.TargetCount == 1 || c.CardRarityByDenseId[x.Target1] == 3));
            if (bonesMode == "early-accept" || bonesMode == "rare-local" && rareK)
                gpu = gpu with { DirectNestedMode = bonesMode == "early-accept" ? 100 : 101 };
            if (bonesMode is "" or "pair-local")
                gpu = gpu with { DirectNestedMode = tracked.Contains(Beta110FastRelicCatalog.PhialHolster) &&
                    plan.StructuredConditions.All(x => x.Kind is Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets or
                        Beta110FastStructuredConditionKind.PhialHolsterPotions) ? 102 : 0 };
        }
        return true;
    }
}
