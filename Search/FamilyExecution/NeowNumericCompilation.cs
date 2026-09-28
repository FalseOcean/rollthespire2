using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class NeowNumericCompilation
{

    internal static IEnumerable<ModelKey> EnumerateFinalCurseRequiredKeys(NeowSearchFilter filter)
    {
        if (filter.RequiredFinalCurse is { } required) yield return required;
        foreach (NeowStructuredEffectSearchCondition condition in filter.StructuredNeowEffects)
        {
            if (condition.Scope != NeowStructuredEffectScope.FinalCurse ||
                condition.OutputKind != NeowStructuredOutputKind.Curse ||
                condition.OutputKeys.Count != 1 ||
                condition.SourceRelicKey != BaseGameModelKeys.Relics.NeowsBones)
                continue;
            yield return condition.OutputKeys[0];
        }
    }

    internal static bool TryCompileCurseIds(
        IEnumerable<ModelKey> keys,
        Beta110FastEffectCatalog catalog,
        out ushort[] ids)
    {
        var output = new List<ushort>();
        foreach (ModelKey key in keys.Distinct(ModelKeyComparer.Instance))
        {
            if (!catalog.TryGetDenseId(key, out ushort id) ||
                !catalog.GeneratedCurseIds.Contains(id))
            {
                ids = Array.Empty<ushort>();
                return false;
            }
            output.Add(id);
        }
        ids = output.ToArray();
        return true;
    }

    internal static bool TryCompileStructuredCondition(
        NeowStructuredEffectSearchCondition condition,
        Beta110FastEffectCatalog catalog,
        out Beta110FastStructuredCondition compiled,
        out Beta110FastDomain domain)
    {
        compiled = default;
        domain = Beta110FastDomain.None;
        if (!Beta110FastRelicCatalog.TryGetId(condition.SourceRelicKey, out byte sourceId)) return false;

        Beta110FastStructuredConditionKind kind;
        bool authorityExact;
        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule &&
            condition.Kind == NeowStructuredConditionKind.ExactUnorderedPair &&
            condition.Scope == NeowStructuredEffectScope.NestedRelics &&
            condition.OutputKind == NeowStructuredOutputKind.Relic &&
            condition.OutputKeys.Count is 1 or 2)
        {
            kind = Beta110FastStructuredConditionKind.LargeCapsuleNestedRelics;
            domain = Beta110FastDomain.CapsuleNestedRelics;
            authorityExact = catalog.RelicBagAuthorityExact;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule &&
                 condition.Kind == NeowStructuredConditionKind.ExactSingle &&
                 condition.Scope == NeowStructuredEffectScope.NestedRelics &&
                 condition.OutputKind == NeowStructuredOutputKind.Relic &&
                 condition.OutputKeys.Count == 1)
        {
            kind = Beta110FastStructuredConditionKind.SmallCapsuleNestedRelic;
            domain = Beta110FastDomain.CapsuleNestedRelics;
            authorityExact = catalog.RelicBagAuthorityExact;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope &&
                 condition.Kind == NeowStructuredConditionKind.IndependentOfferGroupTargets &&
                 condition.Scope == NeowStructuredEffectScope.SelectableOfferGroups &&
                 condition.OutputKind == NeowStructuredOutputKind.Card &&
                 condition.OutputKeys.Count is 1 or 2)
        {
            kind = Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets;
            domain = Beta110FastDomain.KaleidoscopeOffers;
            authorityExact = catalog.OtherCharacterCardAuthorityExact;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.ScrollBoxes &&
                 condition.Kind == NeowStructuredConditionKind.StructuredCardComposition &&
                 condition.Scope == NeowStructuredEffectScope.SelectableOfferGroups &&
                 condition.OutputKind == NeowStructuredOutputKind.Card &&
                 condition.OutputKeys.Count is >= 1 and <= 3)
        {
            kind = Beta110FastStructuredConditionKind.ScrollBoxesCardComposition;
            domain = Beta110FastDomain.ScrollBoxesOffers;
            authorityExact = catalog.CharacterCardAuthorityExact;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.ScrollBoxes &&
                 condition.Kind == NeowStructuredConditionKind.SpecialOffer &&
                 condition.Scope == NeowStructuredEffectScope.SelectableOfferGroups &&
                 condition.OutputKind == NeowStructuredOutputKind.Card &&
                 condition.SpecialOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw)
        {
            kind = Beta110FastStructuredConditionKind.ScrollBoxesTripleClaw;
            domain = Beta110FastDomain.ScrollBoxesOffers;
            authorityExact = catalog.CharacterCardAuthorityExact;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.ArcaneScroll &&
                 IsExactSingle(condition, NeowStructuredEffectScope.ProductRelevantEffects, NeowStructuredOutputKind.Card))
        {
            kind = Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard;
            domain = Beta110FastDomain.ArcaneScrollOffer;
            authorityExact = catalog.CharacterRewardAuthorityExact && catalog.CharacterRewardPool.Rare.Length > 0;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.MassiveScroll &&
                 IsExactSingle(condition, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card))
        {
            kind = Beta110FastStructuredConditionKind.MassiveScrollOffer;
            domain = Beta110FastDomain.MassiveScrollOffer;
            authorityExact = catalog.CharacterRewardAuthorityExact && catalog.ColorlessRewardAuthorityExact && catalog.MultiplayerRewardPool.TotalCount >= 3;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.HeftyTablet &&
                 IsExactSingle(condition, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card))
        {
            kind = Beta110FastStructuredConditionKind.HeftyTabletRareOffer;
            domain = Beta110FastDomain.HeftyTabletOffer;
            authorityExact = catalog.CharacterRewardAuthorityExact && catalog.CharacterRewardPool.Rare.Length >= 3;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.LeadPaperweight &&
                 IsExactSingle(condition, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card))
        {
            kind = Beta110FastStructuredConditionKind.LeadPaperweightColorlessOffer;
            domain = Beta110FastDomain.LeadPaperweightOffer;
            authorityExact = catalog.ColorlessRewardAuthorityExact;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.LostCoffer &&
                 IsExactSingle(condition, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card))
        {
            kind = Beta110FastStructuredConditionKind.LostCofferCardOffer;
            domain = Beta110FastDomain.LostCofferOffer;
            authorityExact = catalog.CharacterRewardAuthorityExact;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.LostCoffer &&
                 IsExactSingle(condition, NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Potion))
        {
            kind = Beta110FastStructuredConditionKind.LostCofferPotion;
            domain = Beta110FastDomain.LostCofferOffer;
            authorityExact = catalog.CharacterRewardAuthorityExact && catalog.PotionAuthorityExact;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.PhialHolster &&
                 condition.Kind == NeowStructuredConditionKind.ExactUnorderedPair &&
                 condition.Scope == NeowStructuredEffectScope.GeneratedPotions &&
                 condition.OutputKind == NeowStructuredOutputKind.Potion &&
                 condition.OutputKeys.Count is 1 or 2)
        {
            kind = Beta110FastStructuredConditionKind.PhialHolsterPotions;
            domain = Beta110FastDomain.PhialHolsterPotions;
            authorityExact = catalog.PotionAuthorityExact && catalog.PotionPool.HasAtLeastPerRarity(2);
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.LeafyPoultice &&
                 condition.Kind == NeowStructuredConditionKind.ExactUnorderedPair &&
                 condition.Scope == NeowStructuredEffectScope.TransformResults &&
                 condition.OutputKind == NeowStructuredOutputKind.Card &&
                 condition.OutputKeys.Count is 1 or 2)
        {
            kind = Beta110FastStructuredConditionKind.LeafyPoulticeTransforms;
            domain = Beta110FastDomain.LeafyPoulticeTransforms;
            authorityExact = catalog.LeafyTransformAuthorityExact;
        }
        else if (condition.SourceRelicKey == BaseGameModelKeys.Relics.NewLeaf &&
                 IsExactSingle(condition, NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card))
        {
            kind = Beta110FastStructuredConditionKind.NewLeafTransform;
            domain = Beta110FastDomain.NewLeafTransform;
            authorityExact = catalog.NewLeafTransformAuthorityExact;
        }
        else return false;

        if (!authorityExact) return false;
        Span<ushort> targets = stackalloc ushort[3];
        targets.Fill(Beta110FastDenseId.Invalid);
        for (int index = 0; index < condition.OutputKeys.Count; index++)
        {
            if (!catalog.TryGetDenseId(condition.OutputKeys[index], out targets[index])) return false;
        }
        if (kind == Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard &&
            !catalog.CharacterRewardPool.Rare.Contains(targets[0])) return false;
        if (kind == Beta110FastStructuredConditionKind.HeftyTabletRareOffer &&
            !catalog.CharacterRewardPool.Rare.Contains(targets[0])) return false;
        if (kind == Beta110FastStructuredConditionKind.LeadPaperweightColorlessOffer &&
            !Contains(catalog.ColorlessRewardPool, targets[0])) return false;
        if (kind == Beta110FastStructuredConditionKind.LostCofferCardOffer &&
            !Contains(catalog.CharacterRewardPool, targets[0])) return false;
        if (kind == Beta110FastStructuredConditionKind.LostCofferPotion &&
            !catalog.PotionPool.AllAllowed.Contains(targets[0])) return false;
        if (kind == Beta110FastStructuredConditionKind.PhialHolsterPotions)
        {
            for (int index = 0; index < condition.OutputKeys.Count; index++)
            {
                if (!catalog.PotionPool.AllAllowed.Contains(targets[index])) return false;
            }
        }
        if (kind == Beta110FastStructuredConditionKind.LeafyPoulticeTransforms)
        {
            for (int index = 0; index < condition.OutputKeys.Count; index++)
            {
                ushort target = targets[index];
                if (!catalog.LeafyStrikeTransformPool.Contains(target) &&
                    !catalog.LeafyDefendTransformPool.Contains(target)) return false;
            }
        }
        if (kind == Beta110FastStructuredConditionKind.NewLeafTransform &&
            !catalog.NewLeafTransformPool.Contains(targets[0])) return false;
        if ((kind is Beta110FastStructuredConditionKind.LargeCapsuleNestedRelics or
                     Beta110FastStructuredConditionKind.SmallCapsuleNestedRelic) &&
            condition.OutputKeys.Count > 0)
        {
            for (int index = 0; index < condition.OutputKeys.Count; index++)
            {
                ushort target = targets[index];
                if (target != catalog.CapsuleCircletId &&
                    !catalog.OrdinaryRelics.Any(relic => relic.DenseId == target)) return false;
            }
        }
        if (kind == Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets)
        {
            for (int index = 0; index < condition.OutputKeys.Count; index++)
            {
                ushort target = targets[index];
                if (!catalog.OtherCharacterPools.Any(pool =>
                        pool.Common.Contains(target) || pool.Uncommon.Contains(target) || pool.Rare.Contains(target)))
                    return false;
            }
        }
        if (kind == Beta110FastStructuredConditionKind.ScrollBoxesCardComposition)
        {
            int common = 0;
            int uncommon = 0;
            for (int index = 0; index < condition.OutputKeys.Count; index++)
            {
                ushort target = targets[index];
                if (target >= catalog.CardRarityByDenseId.Length) return false;
                switch (catalog.CardRarityByDenseId[target])
                {
                    case 1: common++; break;
                    case 2: uncommon++; break;
                    default: return false;
                }
            }
            if (common > 2 || uncommon > 1) return false;
        }
        compiled = new Beta110FastStructuredCondition(
            sourceId,
            kind,
            targets[0],
            targets[1],
            targets[2],
            checked((byte)condition.OutputKeys.Count),
            condition.AllowDuplicateOutputs);
        if (kind == Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets &&
            condition.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder)
        {
            if (condition.KaleidoscopePositionalSlots.Count != 2) return false;
            ushort first = Beta110FastDenseId.Invalid, second = Beta110FastDenseId.Invalid;
            if (condition.KaleidoscopePositionalSlots[0] is { } a && !catalog.TryGetDenseId(a, out first) ||
                condition.KaleidoscopePositionalSlots[1] is { } b && !catalog.TryGetDenseId(b, out second)) return false;
            compiled = compiled with { OrderedKaleidoscope = true, KaleidoscopeFirstTarget = first, KaleidoscopeSecondTarget = second };
        }
        return true;
    }

    private static bool IsExactSingle(
        NeowStructuredEffectSearchCondition condition,
        NeowStructuredEffectScope scope,
        NeowStructuredOutputKind outputKind) =>
        condition.Kind == NeowStructuredConditionKind.ExactSingle &&
        condition.Scope == scope &&
        condition.OutputKind == outputKind &&
        condition.OutputKeys.Count == 1;

    private static bool Contains(Beta110FastCardPool pool, ushort target) =>
        pool.Common.Contains(target) || pool.Uncommon.Contains(target) || pool.Rare.Contains(target);

    internal static Beta110FastNeowAuthority BuildAuthority(
        SearchExecutionRequest plan,
        Beta110FastEffectCatalog effectCatalog,
        bool needsBones)
    {
        bool baseIdentityExact = Beta110FastRelicCatalog.IsAbiCompatible &&
                                 RuntimeProfilePolicies.UsesBeta110SharedAlgorithms(plan.ProfileId) &&
                                 plan.Authority.ProfileId == plan.ProfileId &&
                                 plan.Authority.IsProductionBetaProjectionAuthorityExact &&
                                 plan.Authority.AllCharacterCardPoolsUnlocked.HasValue &&
                                 plan.Authority.IsScrollBoxesAllowed.HasValue &&
                                 plan.Authority.PlayersCount > 0 &&
                                 plan.Authority.PlayerSlotIndex >= 0 &&
                                 plan.Authority.PlayerSlotIndex < plan.Authority.PlayersCount;
        var eligibleCurseIds = new List<byte>();
        IReadOnlyList<ModelKey> eligibleCursePool = Array.Empty<ModelKey>();
        bool curseIdentityExact = baseIdentityExact &&
                                  ModernNeowIdentityPredictor.TryGetEligibleCursePool(plan.Authority, out eligibleCursePool);
        if (curseIdentityExact)
        {
            foreach (ModelKey key in eligibleCursePool)
            {
                if (!Beta110FastRelicCatalog.TryGetId(key, out byte id))
                {
                    curseIdentityExact = false;
                    eligibleCurseIds.Clear();
                    break;
                }
                eligibleCurseIds.Add(id);
            }
            curseIdentityExact &= eligibleCurseIds.Count is > 0 and <= 32;
        }
        bool identityExact = baseIdentityExact && curseIdentityExact;
        NeowEffectAuthoritySnapshot? effects = plan.Authority.EffectAuthority;
        var bones = new List<byte>();
        bool bonesExact = !needsBones;
        if (needsBones)
        {
            bonesExact = effects?.HasExactBonesPools == true;
            if (bonesExact)
            {
                foreach (ModelKey key in effects!.BonesEligibleRelics!)
                {
                    if (key == BaseGameModelKeys.Relics.NeowsBones) continue;
                    if (!Beta110FastRelicCatalog.TryGetId(key, out byte id))
                    {
                        bonesExact = false;
                        bones.Clear();
                        break;
                    }
                    bones.Add(id);
                }
                bonesExact &= bones.Count is >= 2 and <= 64;
            }
        }

        string fingerprint = Fingerprint(new[]
        {
            plan.Authority.UnlockSnapshotFingerprint,
            plan.Authority.CatalogFingerprint,
            plan.Authority.EffectSnapshotFingerprint,
            effectCatalog.Fingerprint,
            plan.Authority.PlayerSlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            plan.Authority.PlayersCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            plan.Ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
            plan.Authority.AllCharacterCardPoolsUnlocked?.ToString() ?? "null",
            plan.Authority.IsScrollBoxesAllowed?.ToString() ?? "null",
            curseIdentityExact.ToString(),
            identityExact.ToString(),
            bonesExact.ToString(),
            string.Join(",", eligibleCurseIds),
            string.Join(",", bones)
        });
        return new Beta110FastNeowAuthority(
            plan.Authority.PlayerSlotIndex,
            plan.Authority.PlayersCount,
            plan.Ascension,
            plan.Authority.AllCharacterCardPoolsUnlocked == true,
            plan.Authority.IsScrollBoxesAllowed == true,
            plan.CharacterKey == BaseGameModelKeys.Characters.Defect,
            bones.ToArray(),
            eligibleCurseIds.ToArray(),
            curseIdentityExact,
            identityExact,
            bonesExact,
            effectCatalog,
            fingerprint);
    }

    internal static Beta110CurseFirstDrawGateP9 BuildCurseFirstDrawGateP9(
        Beta110FastNeowAuthority authority,
        ulong topAny,
        ulong topAll,
        ulong topBan,
        ulong selectedRouteMask,
        bool requireBones,
        bool requiresBonesProjection,
        bool hasStructuredOrFinalCurseRemainder)
    {
        if (!authority.CurseIdentityAuthorityExact || authority.EligibleCurseRelicIds.Length == 0)
            return new Beta110CurseFirstDrawGateP9(0, 0, 0, Beta110CurseFirstDrawGateDispositionP9.Disabled, "CurseAuthorityUnavailable");

        ulong knownCurseMask = 0UL;
        foreach (ModelKey key in ModernNeowIdentityPredictor.BaseCursePoolForFiltering)
        {
            if (Beta110FastRelicCatalog.TryGetId(key, out byte id))
                knownCurseMask |= Beta110FastRelicCatalog.Bit(id);
        }
        ulong positiveAny = topAny & ~knownCurseMask;
        ulong positiveAll = topAll & ~knownCurseMask;
        ulong positiveBan = topBan & ~knownCurseMask;
        bool selectedRouteIsCurse = selectedRouteMask != 0 && (selectedRouteMask & knownCurseMask) != 0;
        bool selectedRouteIsPositive = selectedRouteMask != 0 && !selectedRouteIsCurse;
        uint accepted = 0;
        uint rejected = 0;
        byte bonesOrdinal = byte.MaxValue;
        byte bonesId = Beta110FastRelicCatalog.NeowsBones;

        for (int ordinal = 0; ordinal < authority.EligibleCurseRelicIds.Length; ordinal++)
        {
            byte curseId = authority.EligibleCurseRelicIds[ordinal];
            ulong curseBit = Beta110FastRelicCatalog.Bit(curseId);
            uint ordinalBit = 1u << ordinal;
            if (curseId == bonesId) bonesOrdinal = checked((byte)ordinal);

            bool fails = false;
            bool unresolved = false;

            if (topAny != 0)
            {
                if ((topAny & curseBit) != 0) { }
                else if (positiveAny != 0) unresolved = true;
                else fails = true;
            }

            ulong requiredCurseBits = topAll & knownCurseMask;
            if (requiredCurseBits != 0 && requiredCurseBits != curseBit) fails = true;
            if (positiveAll != 0) unresolved = true;
            if ((topBan & curseBit) != 0) fails = true;
            if (positiveBan != 0) unresolved = true;

            if (selectedRouteIsCurse && selectedRouteMask != curseBit) fails = true;
            if (selectedRouteIsPositive) unresolved = true;

            if (requireBones && curseId != bonesId) fails = true;
            if (requiresBonesProjection)
            {
                if (curseId != bonesId) fails = true;
                else unresolved = true;
            }
            if (hasStructuredOrFinalCurseRemainder) unresolved = true;

            if (fails) rejected |= ordinalBit;
            else if (!unresolved) accepted |= ordinalBit;
        }

        uint all = authority.EligibleCurseRelicIds.Length == 32
            ? uint.MaxValue
            : (1u << authority.EligibleCurseRelicIds.Length) - 1u;
        Beta110CurseFirstDrawGateDispositionP9 disposition;
        string reason;
        bool bonesContinuation = requiresBonesProjection && bonesOrdinal != byte.MaxValue &&
                                 rejected == (all & ~(1u << bonesOrdinal)) && accepted == 0;
        if (bonesContinuation)
        {
            disposition = Beta110CurseFirstDrawGateDispositionP9.BonesContinuation;
            reason = "AuthorityOrdinalGateThenBonesContinuation";
        }
        else if ((accepted | rejected) == all && all != 0)
        {
            disposition = Beta110CurseFirstDrawGateDispositionP9.TerminalDecision;
            reason = "FirstDrawFullyDeterminesNeowIdentityPredicate";
        }
        else if (accepted != 0 || rejected != 0)
        {
            disposition = Beta110CurseFirstDrawGateDispositionP9.PartialDecision;
            reason = "FirstDrawPartiallyDeterminesNeowIdentityPredicate";
        }
        else
        {
            disposition = Beta110CurseFirstDrawGateDispositionP9.Disabled;
            reason = "PositiveContinuationRequiredForEveryCurseDraw";
        }
        return new Beta110CurseFirstDrawGateP9(
            checked((byte)authority.EligibleCurseRelicIds.Length), accepted, rejected, disposition, reason);
    }

    internal static bool TryBuildMask(IEnumerable<ModelKey> keys, out ulong mask)
    {
        mask = 0;
        foreach (ModelKey key in keys)
        {
            if (!Beta110FastRelicCatalog.TryGetId(key, out byte id))
            {
                mask = 0;
                return false;
            }
            mask |= Beta110FastRelicCatalog.Bit(id);
        }
        return true;
    }

    private static string Fingerprint(IEnumerable<string> values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values)))).ToLowerInvariant();
}
