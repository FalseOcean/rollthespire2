using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Effects.Shadow;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Effects;

internal sealed partial class NeowEffectProjectionEngine
{
    private readonly IRuntimeProfile _profile;
    private readonly RuntimeProfileId _profileId;
    private readonly CardBaseOddsPolicy _baseOddsPolicy;
    private readonly string _evidencePrefix;
    private readonly ulong? _trustedRootHash;
    private readonly RuntimeContextAuthoritySnapshot _analysisAuthority;
    private readonly bool _enableComplexBonesDeckInteractions;
    // Null means concrete replay. A supplied map is the explicit multiplayer
    // premise: unlisted Capsule obtains have no immediate side effects.
    internal IReadOnlyDictionary<ModelKey, IReadOnlySet<ModelKey>>? AuthoredCapsuleEffects { get; init; }


    public NeowEffectProjectionEngine(
        IRuntimeProfile profile,
        string evidencePrefix,
        string canonicalSeed,
        RuntimeContextAuthoritySnapshot analysisAuthority,
        bool enableComplexBonesDeckInteractions = false)
        : this(
            profile,
            evidencePrefix,
            string.IsNullOrWhiteSpace(canonicalSeed)
                ? null
                : profile.ComputeRootSeed(canonicalSeed),
            analysisAuthority,
            enableComplexBonesDeckInteractions)
    {
    }

    internal NeowEffectProjectionEngine(
        IRuntimeProfile profile,
        string evidencePrefix,
        ulong rootHash,
        RuntimeContextAuthoritySnapshot analysisAuthority,
        bool enableComplexBonesDeckInteractions = false)
        : this(profile, evidencePrefix, (ulong?)rootHash, analysisAuthority, enableComplexBonesDeckInteractions)
    {
    }

    private NeowEffectProjectionEngine(
        IRuntimeProfile profile,
        string evidencePrefix,
        ulong? rootHash,
        RuntimeContextAuthoritySnapshot analysisAuthority,
        bool enableComplexBonesDeckInteractions)
    {
        _profile = profile;
        _profileId = profile.ProfileId;
        _baseOddsPolicy = RuntimeProfilePolicies.BaseOddsPolicy(_profileId);
        _evidencePrefix = evidencePrefix;
        _trustedRootHash = rootHash;
        _analysisAuthority = analysisAuthority;
        _enableComplexBonesDeckInteractions = enableComplexBonesDeckInteractions;
    }

    public NeowEffectProjection Project(ModelKey relicKey) => Project(relicKey, null);

    internal NeowEffectProjection Project(ModelKey relicKey, NeowEffectRngContext? openingRng)
    {
        NeowEffectImplementationStatus implementation =
            NeowEffectCoverageRegistry.GetImplementation(_profileId, relicKey);
        if (implementation == NeowEffectImplementationStatus.NotApplicable)
        {
            return NeowEffectProjection.NotApplicable(relicKey, Evidence(relicKey, "not-applicable"));
        }

        if (implementation == NeowEffectImplementationStatus.NotImplemented)
        {
            return NeowEffectProjection.NotImplemented(relicKey, Evidence(relicKey, "not-implemented"));
        }

        if (implementation == NeowEffectImplementationStatus.UnsupportedEffectType)
        {
            return NeowEffectProjection.Unsupported(relicKey, Evidence(relicKey, "unsupported-effect-type"));
        }

        NeowEffectAuthoritySnapshot? effectAuthority = _analysisAuthority.EffectAuthority;
        if (effectAuthority is
            {
                AuthoritySource: NeowEffectAuthoritySource.RuntimeSnapshot
            } && effectAuthority.CapturedProfileId != _profileId)
        {
            return NeowEffectProjection.UnknownAuthority(
                relicKey,
                Evidence(relicKey, "runtime-snapshot-profile-mismatch"));
        }

        NeowEffectWorkingState? state = null;
        if (effectAuthority is not null && _trustedRootHash.HasValue)
        {
            // A concrete-root Predictor call must not need Search to have populated
            // the captured bag for that root. Rebuild only for bag-consuming entries;
            // other Neow domains retain their independent authority requirements.
            if (RuntimeProfilePolicies.IsModernCore(_profileId) &&
                (relicKey == BaseGameModelKeys.Relics.NeowsBones ||
                 relicKey == BaseGameModelKeys.Relics.SmallCapsule || relicKey == BaseGameModelKeys.Relics.LargeCapsule))
            {
                if (effectAuthority.HasExactRelicBagSourcePools)
                {
                    effectAuthority = effectAuthority with
                    {
                        OrderedRelicBag = NeowRewardGenerator.BuildOrderedRelicBag(_profile, _trustedRootHash.Value, _analysisAuthority),
                        RelicBagExact = true
                    };
                }
                else if (effectAuthority.AuthoritySource == NeowEffectAuthoritySource.RuntimeSnapshot)
                {
                    // A captured order without reconstructible sources has no proof
                    // that it belongs to this requested root. Keep explicit fixture
                    // authority available to existing seed-bound reference fixtures.
                    effectAuthority = effectAuthority with { OrderedRelicBag = null, RelicBagExact = false };
                }
            }
            state = new NeowEffectWorkingState(
                effectAuthority,
                effectAuthority.OrderedDeck is null ? null : new NeowShadowDeck(effectAuthority.OrderedDeck),
                effectAuthority.OrderedRelicBag is null
                    ? null
                    : effectAuthority.OrderedRelicBag.OrderBy(item => item.BagOrder).ToList(),
                openingRng ?? NeowEffectRngContext.CreateFromRootHash(
                    _profile,
                    _trustedRootHash.Value,
                    _analysisAuthority.PlayerSlotIndex));
        }

        NeowEffectProjection projection = ProjectInternal(relicKey, state, recursionDepth: 0);
        return state is null
            ? projection
            : AttachOpeningRewardContinuations(relicKey, state, projection);
    }

    private NeowEffectProjection ProjectInternal(
        ModelKey relicKey,
        NeowEffectWorkingState? state,
        int recursionDepth)
    {
        if (recursionDepth > 4)
        {
            return NeowEffectProjection.Unsupported(relicKey, Evidence(relicKey, "nested-depth-exceeded"));
        }

        if (relicKey == BaseGameModelKeys.Relics.CursedPearl)
        {
            AddSyntheticCardToDeck(state?.Deck, BaseGameModelKeys.Cards.Greed, "cursed-pearl-greed");
            return Exact(relicKey, new[]
            {
                Group("cursed-pearl", 0, EffectSelectionPolicy.NoPlayerChoice, EffectPredictionScope.ImmediateOptionEffect,
                    "CursedPearl.AfterObtained", new[]
                    {
                        Item(PredictedEffectKind.AddCard, BaseGameModelKeys.Cards.Greed, null, 0, Evidence(relicKey, "add-greed")),
                        Item(PredictedEffectKind.ChangeGold, null, 333, 1, Evidence(relicKey, "gain-gold"))
                    })
            });
        }

        if (relicKey == BaseGameModelKeys.Relics.DowsingRod)
        {
            AddSyntheticCardToDeck(state?.Deck, BaseGameModelKeys.Cards.Dowsing, "dowsing-rod-card");
            return Exact(relicKey, new[]
            {
                Group("dowsing-rod", 0, EffectSelectionPolicy.NoPlayerChoice, EffectPredictionScope.ImmediateOptionEffect,
                    "DowsingRod.AfterObtained", new[]
                    {
                        Item(PredictedEffectKind.AddCard, BaseGameModelKeys.Cards.Dowsing, null, 0, Evidence(relicKey, "add-dowsing"))
                    })
            });
        }

        if (relicKey == BaseGameModelKeys.Relics.GoldenPearl)
        {
            return Exact(relicKey, new[]
            {
                Group("golden-pearl", 0, EffectSelectionPolicy.NoPlayerChoice, EffectPredictionScope.ImmediateOptionEffect,
                    "GoldenPearl.AfterObtained", new[]
                    {
                        Item(PredictedEffectKind.ChangeGold, null, 150, 0, Evidence(relicKey, "gain-gold"))
                    })
            });
        }

        if (relicKey == BaseGameModelKeys.Relics.NeowsTorment)
        {
            AddSyntheticCardToDeck(state?.Deck, BaseGameModelKeys.Cards.NeowsFury, "neows-torment-card",
                EffectCardType.Attack, canUpgrade: true, rarity: EffectCardRarity.Ancient);
            return Exact(relicKey, new[]
            {
                Group("neows-torment", 0, EffectSelectionPolicy.NoPlayerChoice, EffectPredictionScope.ImmediateOptionEffect,
                    "NeowsTorment.AfterObtained", new[]
                    {
                        Item(PredictedEffectKind.AddCard, BaseGameModelKeys.Cards.NeowsFury, null, 0, Evidence(relicKey, "add-neows-fury"))
                    })
            });
        }

        if (relicKey == BaseGameModelKeys.Relics.NutritiousOyster)
        {
            return Exact(relicKey, new[]
            {
                Group("nutritious-oyster", 0, EffectSelectionPolicy.NoPlayerChoice, EffectPredictionScope.ImmediateOptionEffect,
                    "NutritiousOyster.AfterObtained", new[]
                    {
                        Item(PredictedEffectKind.ChangeMaxHp, null, 11, 0, Evidence(relicKey, "gain-max-hp"))
                    })
            });
        }

        if (relicKey == BaseGameModelKeys.Relics.NeowsSacrifice)
        {
            AddSyntheticCardToDeck(state?.Deck, BaseGameModelKeys.Cards.Guilty, "neows-sacrifice-guilty");

            // Release policy (Beta111 closeout): potion-slot sufficiency is not product
            // prediction authority. The generated Ambrosia identity is deterministic;
            // incomplete inventory/application semantics may remain Partial without
            // downgrading the observable generated result used by Predictor/Exact.
            bool fullInventoryApplicationExact =
                CanUseVanillaSinglePlayerNeowOpeningPotionInvariant(state) ||
                (state?.Authority.PotionProcurementHooksNoOpExact == true &&
                 state.Authority.CurrentPotionCount.HasValue &&
                 state.Authority.PotionCapacity.HasValue &&
                 state.Authority.CurrentPotionCount.Value < state.Authority.PotionCapacity.Value);
            EvidenceCode potionEvidence = fullInventoryApplicationExact
                ? Evidence(relicKey, "potion-inventory-application-exact")
                : Evidence(relicKey, "release-policy-potion-slot-sufficiency-not-authority");
            var items = new List<PredictedEffect>
            {
                Item(
                    fullInventoryApplicationExact ? PredictedEffectKind.AddPotion : PredictedEffectKind.AttemptAddPotion,
                    BaseGameModelKeys.Potions.Ambergris,
                    null,
                    0,
                    potionEvidence,
                    PredictionPrecision.Exact),
                Item(PredictedEffectKind.AddCard, BaseGameModelKeys.Cards.Guilty, null, 1, Evidence(relicKey, "add-guilty"))
            };
            PredictedEffectGroup group = Group(
                "neows-sacrifice", 0, EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionScope.ImmediateOptionEffect, "NeowsSacrifice.AfterObtained", items);
            return BuildGeneratedPotionReleaseProjection(
                relicKey,
                group,
                fullInventoryApplicationExact,
                potionEvidence);
        }

        if (relicKey == BaseGameModelKeys.Relics.ArcaneScroll)
        {
            return ProjectArcaneScroll(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.HeftyTablet)
        {
            return ProjectHeftyTablet(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.LeadPaperweight)
        {
            return ProjectLeadPaperweight(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.LostCoffer)
        {
            return ProjectLostCoffer(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.Kaleidoscope)
        {
            return ProjectKaleidoscope(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.ScrollBoxes)
        {
            return ProjectScrollBoxes(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.PhialHolster)
        {
            return ProjectPhialHolster(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.LeafyPoultice)
        {
            return ProjectLeafyPoultice(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.NeowsTalisman)
        {
            return ProjectNeowsTalisman(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.PreciseScissors)
        {
            return ProjectPreciseScissors(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.PrecariousShears)
        {
            return ProjectPrecariousShears(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.Pomander)
        {
            return ProjectPomander(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.NewLeaf)
        {
            return ProjectNewLeaf(relicKey, state);
        }

        if (relicKey == BaseGameModelKeys.Relics.SmallCapsule)
        {
            return ProjectCapsule(relicKey, state, count: 1, includeFixedCards: false, recursionDepth);
        }

        if (relicKey == BaseGameModelKeys.Relics.LargeCapsule)
        {
            return ProjectCapsule(relicKey, state, count: 2, includeFixedCards: true, recursionDepth);
        }

        if (relicKey == BaseGameModelKeys.Relics.NeowsBones)
        {
            return ProjectBones(relicKey, state, recursionDepth);
        }

        if (relicKey == BaseGameModelKeys.Relics.MassiveScroll)
        {
            if (_analysisAuthority.PlayersCount <= 1)
                return NeowEffectProjection.NotApplicable(relicKey, Evidence(relicKey, "single-player-not-applicable"));
            if (state?.Authority.HasExactCharacterRewardPool != true || !state.Authority.HasExactColorlessRewardPool)
                return Unknown(relicKey, "multiplayer-card-pools-missing");
            var pool = state.Authority.CharacterRewardPool!.Concat(state.Authority.ColorlessRewardPool!)
                .Where(c => c.IsMultiplayerOnly).DistinctBy(c => c.CardKey)
                .Select((c, index) => c with { PoolOrder = index }).ToArray();
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            var effects = new List<PredictedEffect>();
            for (int i = 0; i < 3; i++)
            {
                var card = NeowRewardGenerator.CreateCard(state.Rng.Rewards, pool, _analysisAuthority.Ascension,
                    excluded, forcedRarity: null, consumeUpgradeRoll: true, baseOddsPolicy: _baseOddsPolicy);
                if (card is null) return Unknown(relicKey, "multiplayer-card-offer-incomplete");
                effects.AddRange(EffectsForGeneratedCard(card, $"massive-scroll.offer.{i}", effects.Count, relicKey));
            }
            return Exact(relicKey, [Group("massive-scroll-offer", 0, EffectSelectionPolicy.ChooseOneOrSkip,
                EffectPredictionScope.ImmediateOptionEffect, "MassiveScroll card reward", effects)]);
        }

        if (relicKey == BaseGameModelKeys.Relics.SilkenTress)
        {
            PredictedEffectGroup immediate = Group(
                "silken-tress-immediate", 0, EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionScope.ImmediateOptionEffect, "SilkenTress.AfterObtained", new[]
                {
                    Item(PredictedEffectKind.SetGold, null, 0, 0, Evidence(relicKey, "lose-all-gold"))
                });
            PredictedEffectGroup future = FutureGroup(relicKey, 1, "SilkenTress future Glam card-reward hook");
            EvidenceCode evidence = Evidence(relicKey, "immediate-exact-future-description");
            return NeowEffectProjection.Partial(
                relicKey, new[] { immediate, future }, PredictionWarningCode.ComplexRouteNotEvaluated,
                evidence) with
            {
                ProductRelevantProjectionPrecision = PredictionPrecision.Exact,
                FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Partial,
                ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Evaluated,
                DependencyImpact = EffectDependencyImpact.None(evidence)
            };
        }

        if (IsFutureRuntimeRelic(relicKey))
        {
            EvidenceCode evidence = Evidence(relicKey, "full-effect-future-runtime-partial-product-opening-exact");
            return NeowEffectProjection.ProductExactNoVisibleEffects(
                relicKey,
                evidence,
                FullEffectSemanticsCompleteness.Partial) with
            {
                DependencyImpact = EffectDependencyImpact.None(evidence)
            };
        }

        return NeowEffectProjection.NotImplemented(relicKey, Evidence(relicKey, "not-implemented"));
    }

    private NeowEffectProjection ProjectArcaneScroll(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state?.Authority.HasExactCharacterRewardIdentityPool != true)
        {
            return Unknown(relicKey, "character-reward-identity-pool-required");
        }

        GeneratedCard? generated = NeowRewardGenerator.CreateCard(
            state.Rng.Rewards,
            state.Authority.CharacterRewardPool!,
            _analysisAuthority.Ascension,
            new HashSet<string>(StringComparer.Ordinal),
            EffectCardRarity.Rare,
            consumeUpgradeRoll: false,
            baseOddsPolicy: _baseOddsPolicy);
        if (generated is null)
        {
            return Unknown(relicKey, "rare-character-pool-empty");
        }

        // This is an actual generated card, not a descriptive synthetic effect.
        // Preserve type/upgradeability for later authored W/WP acquisition.
        state.Deck?.Add(generated.Card with {
            InstanceId = "arcane-scroll-card",
            PoolOrder = state.Deck.Cards.Count == 0 ? 0 : state.Deck.Cards.Max(c => c.PoolOrder) + 1
        });
        return Exact(relicKey, new[]
        {
            Group("arcane-scroll", 0, EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionScope.ImmediateOptionEffect, "ArcaneScroll.AfterObtained",
                EffectsForGeneratedCard(generated, "arcane-scroll.offer.0", 0, relicKey))
        });
    }

    private NeowEffectProjection ProjectHeftyTablet(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        PredictedEffect fixedInjury = Item(
            PredictedEffectKind.AddCard, BaseGameModelKeys.Cards.Injury, null, 0,
            Evidence(relicKey, "add-injury"));
        if (state?.Authority.HasExactCharacterRewardPool != true)
        {
            EvidenceCode evidence = Evidence(relicKey, "rare-offer-unknown-fixed-injury-exact");
            return NeowEffectProjection.Partial(
                relicKey,
                new[]
                {
                    Group("hefty-tablet-fixed", 1, EffectSelectionPolicy.NoPlayerChoice,
                        EffectPredictionScope.ImmediateOptionEffect, "HeftyTablet fixed Injury", new[] { fixedInjury })
                },
                PredictionWarningCode.EffectSnapshotIncomplete,
                evidence) with
            {
                DependencyImpact = EffectDependencyImpact.RewardsOnlyPartial(
                    PredictionWarningCode.EffectSnapshotIncomplete,
                    evidence)
            };
        }

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var offerEffects = new List<PredictedEffect>();
        for (int i = 0; i < 3; i++)
        {
            GeneratedCard? card = NeowRewardGenerator.CreateCard(
                state.Rng.Rewards, state.Authority.CharacterRewardPool!, _analysisAuthority.Ascension,
                excluded, EffectCardRarity.Rare, consumeUpgradeRoll: false,
                baseOddsPolicy: _baseOddsPolicy);
            if (card is null)
            {
                return Unknown(relicKey, "rare-character-offer-incomplete");
            }
            offerEffects.AddRange(EffectsForGeneratedCard(card, $"hefty-tablet.offer.{i}", offerEffects.Count, relicKey));
        }

        AddSyntheticCardToDeck(state.Deck, BaseGameModelKeys.Cards.Injury, "hefty-tablet-injury");
        return Exact(relicKey, new[]
        {
            Group("hefty-tablet-offer", 0, EffectSelectionPolicy.ChooseOneOrSkip,
                EffectPredictionScope.ImmediateOptionEffect, "HeftyTablet card reward", offerEffects,
                rngEvidenceCode: "rewards:rare-selection:no-upgrade-roll"),
            Group("hefty-tablet-fixed", 1, EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionScope.ImmediateOptionEffect, "HeftyTablet fixed Injury", new[] { fixedInjury })
        });
    }

    private NeowEffectProjection ProjectLeadPaperweight(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state?.Authority.HasExactColorlessRewardPool != true ||
            state.Authority.ColorlessRewardPool is null)
        {
            return Unknown(relicKey, "colorless-reward-pool-required");
        }

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var effects = new List<PredictedEffect>();
        for (int i = 0; i < 2; i++)
        {
            GeneratedCard? card = NeowRewardGenerator.CreateCard(
                state.Rng.Rewards, state.Authority.ColorlessRewardPool, _analysisAuthority.Ascension,
                excluded, forcedRarity: null, consumeUpgradeRoll: true,
                baseOddsPolicy: _baseOddsPolicy);
            if (card is null)
            {
                return Unknown(relicKey, "colorless-offer-incomplete");
            }
            effects.AddRange(EffectsForGeneratedCard(card, $"lead-paperweight.offer.{i}", effects.Count, relicKey));
        }

        return Exact(relicKey, new[]
        {
            Group("lead-paperweight-offer", 0, EffectSelectionPolicy.ChooseOneOrSkip,
                EffectPredictionScope.ImmediateOptionEffect, "LeadPaperweight card reward", effects,
                rngEvidenceCode: "rewards:rarity-selection+card-selection+upgrade-roll-per-card")
        });
    }

    private NeowEffectProjection ProjectLostCoffer(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state?.Authority.HasExactCharacterRewardPool != true || state.Authority.HasExactPotions != true)
        {
            return Unknown(relicKey, "card-and-potion-pools-required");
        }

        var cardEffects = new List<PredictedEffect>();
        var excludedCards = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 3; i++)
        {
            GeneratedCard? card = NeowRewardGenerator.CreateCard(
                state.Rng.Rewards, state.Authority.CharacterRewardPool!, _analysisAuthority.Ascension,
                excludedCards, forcedRarity: null, consumeUpgradeRoll: true,
                baseOddsPolicy: _baseOddsPolicy);
            if (card is null)
            {
                return Unknown(relicKey, "lost-coffer-card-offer-incomplete");
            }
            cardEffects.AddRange(EffectsForGeneratedCard(card, $"lost-coffer.card.{i}", cardEffects.Count, relicKey));
        }

        NeowEffectPotionSnapshot? potion = NeowRewardGenerator.CreatePotion(
            state.Rng.Rewards, state.Authority.PotionPool!, new HashSet<ModelKey>());
        if (potion is null)
        {
            return Unknown(relicKey, "lost-coffer-potion-offer-incomplete");
        }

        PredictedEffectGroup[] groups = new[]
        {
            Group("lost-coffer-cards", 0, EffectSelectionPolicy.ChooseOneOrSkip,
                EffectPredictionScope.ImmediateOptionEffect, "LostCoffer card reward", cardEffects,
                bundleId: "lost-coffer.cards", bundleOrder: 0),
            Group("lost-coffer-potion", 1, EffectSelectionPolicy.OptionalClaim,
                EffectPredictionScope.ImmediateOptionEffect, "LostCoffer potion reward", new[]
                {
                    Item(PredictedEffectKind.AddPotion, potion.PotionKey, null, 0,
                        Evidence(relicKey, "potion-offer"), offerItemId: "lost-coffer.potion.0")
                }, bundleId: "lost-coffer.potion", bundleOrder: 1)
        };
        // Game source populates the CardReward and PotionReward before the reward UI
        // is shown. There is no player-choice-delayed potion RNG phase here.
        return Exact(relicKey, groups);
    }

    private NeowEffectProjection ProjectKaleidoscope(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state?.Authority.HasExactOtherCharacterPools != true ||
            state.Authority.OtherCharacterPools is null)
        {
            return Unknown(relicKey, "other-character-pools-required");
        }

        if (state.Authority.OtherCharacterPools.Count < 3)
        {
            return Unknown(relicKey, "insufficient-other-character-pools");
        }

        var groups = new List<PredictedEffectGroup>();
        for (int groupIndex = 0; groupIndex < 2; groupIndex++)
        {
            List<CharacterCardPoolSnapshot> pools = state.Authority.OtherCharacterPools
                .OrderBy(pool => pool.PoolOrder)
                .ToList();
            state.Rng.Niche.UnstableShuffle(pools);
            var effects = new List<PredictedEffect>();
            int itemOrder = 0;
            foreach (CharacterCardPoolSnapshot pool in pools.Take(3))
            {
                GeneratedCard? card = NeowRewardGenerator.CreateCard(
                    state.Rng.Rewards, pool.Cards, _analysisAuthority.Ascension,
                    new HashSet<string>(StringComparer.Ordinal), forcedRarity: null, consumeUpgradeRoll: true,
                    baseOddsPolicy: _baseOddsPolicy);
                if (card is null)
                {
                    return Unknown(relicKey, "kaleidoscope-pool-offer-incomplete");
                }
                effects.AddRange(EffectsForGeneratedCard(
                    card, $"kaleidoscope.{groupIndex}.{pool.PoolId}", itemOrder, relicKey));
                itemOrder = effects.Count;
            }

            groups.Add(Group(
                $"kaleidoscope-group-{groupIndex + 1}", groupIndex,
                EffectSelectionPolicy.ChooseOneOrSkip,
                EffectPredictionScope.ImmediateOptionEffect,
                $"Kaleidoscope reward {groupIndex + 1}", effects,
                bundleId: $"kaleidoscope.bundle.{groupIndex + 1}", bundleOrder: groupIndex));
        }
        return Exact(relicKey, groups);
    }

    private NeowEffectProjection ProjectScrollBoxes(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state?.Authority.HasExactCharacterRewardPool != true ||
            state.Authority.CharacterRewardPool is null)
        {
            return Unknown(relicKey, "character-reward-pool-required");
        }

        List<NeowEffectCardSnapshot> common = state.Authority.CharacterRewardPool
            .Where(card => card.Rarity == EffectCardRarity.Common)
            .OrderBy(card => card.PoolOrder)
            .ToList();
        List<NeowEffectCardSnapshot> uncommon = state.Authority.CharacterRewardPool
            .Where(card => card.Rarity == EffectCardRarity.Uncommon)
            .OrderBy(card => card.PoolOrder)
            .ToList();
        if (common.Count < 4 || uncommon.Count < 2)
        {
            return NeowEffectProjection.NotApplicable(relicKey, Evidence(relicKey, "pool-count-requirement-not-met"));
        }

        var used = new HashSet<ModelKey>();
        var groups = new List<PredictedEffectGroup>();
        for (int bundleIndex = 0; bundleIndex < 2; bundleIndex++)
        {
            var effects = new List<PredictedEffect>();
            bool clawBundle = _analysisAuthority.Character.CharacterKey == BaseGameModelKeys.Characters.Defect &&
                              state.Rng.Rewards.NextInt(100) < 1;
            if (clawBundle)
            {
                ModelKey claw = state.Authority.ClawKey ?? BaseGameModelKeys.Cards.Claw;
                effects.Add(Item(
                    PredictedEffectKind.AddCard, claw, null, 0,
                    Evidence(relicKey, "defect-claw-bundle"), multiplicity: 3,
                    offerItemId: $"scroll-boxes.bundle.{bundleIndex}.claw"));
            }
            else
            {
                for (int i = 0; i < 2; i++)
                {
                    List<NeowEffectCardSnapshot> candidates = common.Where(card => !used.Contains(card.CardKey)).ToList();
                    if (candidates.Count == 0)
                    {
                        return Unknown(relicKey, "scroll-boxes-common-pool-exhausted");
                    }
                    NeowEffectCardSnapshot selected = candidates[state.Rng.Rewards.NextInt(candidates.Count)];
                    used.Add(selected.CardKey);
                    effects.Add(Item(PredictedEffectKind.AddCard, selected.CardKey, null, effects.Count,
                        Evidence(relicKey, "bundle-common"), offerItemId: $"scroll-boxes.bundle.{bundleIndex}"));
                }
                List<NeowEffectCardSnapshot> uncommonCandidates = uncommon.Where(card => !used.Contains(card.CardKey)).ToList();
                if (uncommonCandidates.Count == 0)
                {
                    return Unknown(relicKey, "scroll-boxes-uncommon-pool-exhausted");
                }
                NeowEffectCardSnapshot uncommonSelected = uncommonCandidates[state.Rng.Rewards.NextInt(uncommonCandidates.Count)];
                used.Add(uncommonSelected.CardKey);
                effects.Add(Item(PredictedEffectKind.AddCard, uncommonSelected.CardKey, null, effects.Count,
                    Evidence(relicKey, "bundle-uncommon"), offerItemId: $"scroll-boxes.bundle.{bundleIndex}"));
            }

            groups.Add(Group(
                $"scroll-boxes-bundle-{bundleIndex + 1}", bundleIndex,
                EffectSelectionPolicy.ChooseExactlyOne,
                EffectPredictionScope.FinitePlayerChoiceRoutes,
                $"ScrollBoxes bundle {bundleIndex + 1}", effects,
                selectionSetId: "scroll-boxes.bundle-choice",
                bundleId: $"scroll-boxes.bundle.{bundleIndex + 1}",
                bundleOrder: bundleIndex));
        }
        return Exact(relicKey, groups);
    }

    private NeowEffectProjection ProjectPhialHolster(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        PredictedEffect capacity = Item(PredictedEffectKind.ChangePotionCapacity, null, 1, 0,
            Evidence(relicKey, "gain-potion-slot"));
        if (state?.Authority.HasExactPotions != true)
        {
            EvidenceCode evidence = Evidence(relicKey, "potions-unknown-capacity-exact");
            return NeowEffectProjection.Partial(
                relicKey,
                new[]
                {
                    Group("phial-holster-capacity", 0, EffectSelectionPolicy.NoPlayerChoice,
                        EffectPredictionScope.ImmediateOptionEffect, "PhialHolster potion capacity", new[] { capacity })
                },
                PredictionWarningCode.EffectSnapshotIncomplete,
                evidence) with
            {
                // CombatPotionGeneration is independent from the Niche curse stream.
                DependencyImpact = EffectDependencyImpact.None(evidence)
            };
        }

        bool vanillaOpeningInvariant = CanUseVanillaSinglePlayerNeowOpeningPotionInvariant(state);
        bool exactSnapshotCapacity = state.Authority.PotionProcurementHooksNoOpExact &&
                                     state.Authority.CurrentPotionCount.HasValue &&
                                     state.Authority.PotionCapacity.HasValue;
        bool snapshotCanFitBoth = exactSnapshotCapacity &&
                                  state.Authority.CurrentPotionCount!.Value + 2 <=
                                  state.Authority.PotionCapacity!.Value + 1;
        bool fullInventoryApplicationExact = vanillaOpeningInvariant || snapshotCanFitBoth;
        var items = new List<PredictedEffect> { capacity };
        var excluded = new HashSet<ModelKey>();
        for (int i = 0; i < 2; i++)
        {
            NeowEffectPotionSnapshot? potion = NeowRewardGenerator.CreatePotion(
                state.Rng.CombatPotionGeneration, state.Authority.PotionPool!, excluded);
            if (potion is null)
            {
                // A selected rarity bucket with no eligible potion has no game-source
                // fallback to the full pool. Treat the dynamic authority as incomplete.
                return Unknown(relicKey, "potion-rarity-pool-incomplete");
            }
            items.Add(Item(
                fullInventoryApplicationExact ? PredictedEffectKind.AddPotion : PredictedEffectKind.AttemptAddPotion,
                potion.PotionKey,
                null,
                items.Count,
                fullInventoryApplicationExact
                    ? Evidence(relicKey, "potion-inventory-application-exact")
                    : Evidence(relicKey, "release-policy-potion-slot-sufficiency-not-authority"),
                PredictionPrecision.Exact));
        }

        PredictedEffectGroup group = Group("phial-holster", 0, EffectSelectionPolicy.NoPlayerChoice,
            EffectPredictionScope.ImmediateOptionEffect, "PhialHolster.AfterObtained", items);
        EvidenceCode projectionEvidence = fullInventoryApplicationExact
            ? Evidence(relicKey, "potion-inventory-application-exact")
            : Evidence(relicKey, "release-policy-potion-slot-sufficiency-not-authority");
        return BuildGeneratedPotionReleaseProjection(
            relicKey,
            group,
            fullInventoryApplicationExact,
            projectionEvidence);
    }

    private NeowEffectProjection ProjectLeafyPoultice(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        PredictedEffect hp = Item(PredictedEffectKind.ChangeMaxHp, null, -12, 0,
            Evidence(relicKey, "lose-max-hp"));
        if (state?.Authority.HasExactDeck != true || !state.Authority.HasExactTransformPool)
        {
            EvidenceCode evidence = Evidence(relicKey, "deck-or-transform-pool-unknown");
            return NeowEffectProjection.Partial(
                relicKey,
                new[] { Group("leafy-poultice-hp", 0, EffectSelectionPolicy.NoPlayerChoice,
                    EffectPredictionScope.ImmediateOptionEffect, "LeafyPoultice max HP", new[] { hp }) },
                PredictionWarningCode.EffectSnapshotIncomplete,
                evidence) with
            {
                DependencyImpact = EffectDependencyImpact.TransformationsAndShadowPartial(
                    PredictionWarningCode.EffectSnapshotIncomplete,
                    evidence)
            };
        }

        var items = new List<PredictedEffect> { hp };
        NeowEffectCardSnapshot? strike = state.Deck!.Cards.FirstOrDefault(card => card.IsBasic && card.IsStrike);
        NeowEffectCardSnapshot? defend = state.Deck.Cards.FirstOrDefault(card => card.IsBasic && card.IsDefend);
        if (strike is not null)
        {
            if (!TryTransformCard(state, strike, state.Rng.Transformations, out ModelKey replacement))
            {
                return Unknown(relicKey, "strike-transform-pool-empty");
            }
            items.Add(Item(PredictedEffectKind.TransformCard, replacement, null, items.Count,
                Evidence(relicKey, "transform-strike"), sourceKey: strike.CardKey));
        }
        if (defend is not null)
        {
            if (!TryTransformCard(state, defend, state.Rng.Transformations, out ModelKey replacement))
            {
                return Unknown(relicKey, "defend-transform-pool-empty");
            }
            items.Add(Item(PredictedEffectKind.TransformCard, replacement, null, items.Count,
                Evidence(relicKey, "transform-defend"), sourceKey: defend.CardKey));
        }

        return Exact(relicKey, new[]
        {
            Group("leafy-poultice", 0, EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionScope.ImmediateOptionEffect, "LeafyPoultice.AfterObtained", items)
        });
    }

    private NeowEffectProjection ProjectNeowsTalisman(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state?.Authority.HasExactDeck != true)
        {
            return Unknown(relicKey, "ordered-deck-required");
        }

        var items = new List<PredictedEffect>();
        NeowEffectCardSnapshot? strike = state.Deck!.Cards.LastOrDefault(card => card.IsBasic && card.IsStrike);
        NeowEffectCardSnapshot? defend = state.Deck.Cards.LastOrDefault(card => card.IsBasic && card.IsDefend);
        foreach (NeowEffectCardSnapshot? candidate in new[] { strike, defend })
        {
            if (candidate is not null && state.Deck.TryUpgrade(candidate.InstanceId, out NeowEffectCardSnapshot upgraded))
            {
                items.Add(Item(PredictedEffectKind.UpgradeCard, upgraded.CardKey, 1, items.Count,
                    Evidence(relicKey, candidate.IsStrike ? "upgrade-last-strike" : "upgrade-last-defend"),
                    sourceKey: candidate.CardKey,
                    offerItemId: candidate.InstanceId));
            }
        }
        if (items.Count == 0)
        {
            items.Add(Item(PredictedEffectKind.DescriptionOnly, null, 0, 0,
                Evidence(relicKey, "no-eligible-basic-targets")));
        }

        return Exact(relicKey, new[]
        {
            Group("neows-talisman", 0, EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionScope.ImmediateOptionEffect, "NeowsTalisman.AfterObtained", items)
        });
    }

    private NeowEffectProjection ProjectPreciseScissors(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state is null)
        {
            return Unknown(relicKey, "effect-authority-required");
        }
        if (state.Authority.HasExactDeck != true || state.Deck is null)
        {
            return Unknown(relicKey, "ordered-deck-required");
        }
        bool any = state.Deck.Cards.Any(card =>
            card.CanRemove && card.IsBasic && (card.IsStrike || card.IsDefend));
        return any
            ? ProjectStandalonePlayerChoiceCapability(
                relicKey,
                "remove-one-basic-strike-or-defend",
                selectionCount: 1)
            : NeowEffectProjection.NotApplicable(
                relicKey,
                Evidence(relicKey, "no-removable-basic-strike-or-defend"));
    }

    private NeowEffectProjection ProjectPrecariousShears(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state is null)
        {
            return Unknown(relicKey, "effect-authority-required");
        }
        if (state.Authority.HasExactDeck != true || state.Deck is null)
        {
            return Unknown(relicKey, "ordered-deck-required");
        }
        int strikes = state.Deck.Cards.Count(card => card.CanRemove && card.IsBasic && card.IsStrike);
        int defends = state.Deck.Cards.Count(card => card.CanRemove && card.IsBasic && card.IsDefend);
        bool any = strikes >= 2 || defends >= 2 || (strikes >= 1 && defends >= 1);
        return any
            ? ProjectStandalonePlayerChoiceCapability(
                relicKey,
                "remove-two-basic-cards",
                selectionCount: 2)
            : NeowEffectProjection.NotApplicable(
                relicKey,
                Evidence(relicKey, "no-legal-two-card-basic-removal-route"));
    }

    private NeowEffectProjection ProjectPomander(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state is null)
        {
            return Unknown(relicKey, "effect-authority-required");
        }
        if (state.Authority.HasExactDeck != true || state.Deck is null)
        {
            return Unknown(relicKey, "ordered-deck-required");
        }
        bool any = state.Deck.Cards.Any(card =>
            card.IsBasic && !card.IsStrike && !card.IsDefend && card.CanUpgrade);
        return any
            ? ProjectStandalonePlayerChoiceCapability(
                relicKey,
                "upgrade-one-basic-non-strike-defend",
                selectionCount: 1)
            : NeowEffectProjection.NotApplicable(
                relicKey,
                Evidence(relicKey, "no-upgradable-basic-non-strike-defend"));
    }

    private NeowEffectProjection ProjectNewLeaf(ModelKey relicKey, NeowEffectWorkingState? state)
    {
        if (state is null)
        {
            return Unknown(relicKey, "effect-authority-required");
        }
        if (state.Authority.HasExactDeck != true || !state.Authority.HasExactTransformPool || state.Deck is null)
        {
            return Unknown(relicKey, "ordered-deck-and-transform-pool-required");
        }

        NeowEffectCardSnapshot? canonicalTarget = NewLeafNormalizedSourcePolicy.Select(
            state.Deck.Cards,
            state.Authority.CharacterStrikeKey);
        if (canonicalTarget is null)
        {
            bool hasRealLegalChoice = state.Deck.Cards.Any(card =>
                card.IsBasic && (card.IsStrike || card.IsDefend) &&
                card.CardType is not (EffectCardType.Curse or EffectCardType.Status));
            return hasRealLegalChoice
                ? ProjectStandalonePlayerChoiceCapability(
                    relicKey,
                    "transform-one-basic-strike-or-defend",
                    selectionCount: 1)
                : NeowEffectProjection.NotApplicable(
                    relicKey,
                    Evidence(relicKey, "no-valid-basic-strike-defend-transform-targets"));
        }

        // Product policy: ordinary prediction and Search normalize New Leaf to
        // the current character's first remaining Basic Strike. We intentionally
        // do not require every legal Strike/Defend target to share an identical
        // pool, and we do not create player-choice branches for this surface.
        // Full complex-choice analysis below still enumerates real legal targets.
        NeowEffectCardSnapshot[] canonicalCandidates = NewLeafNormalizedSourcePolicy.BuildTransformCandidates(
            state.Authority.TransformPool!,
            canonicalTarget);
        if (canonicalCandidates.Length == 0)
        {
            return Unknown(relicKey, "normalized-basic-strike-transform-pool-empty");
        }

        Xoshiro256StarStar rng = RuntimeProfilePolicies.IsModernCore(_profileId)
            ? state.Rng.Niche
            : state.Rng.Transformations;
        NeowEffectCardSnapshot replacement = canonicalCandidates[rng.NextInt(canonicalCandidates.Length)];
        if (!state.Deck.TryTransform(canonicalTarget.InstanceId, replacement.CardKey))
        {
            return Unknown(relicKey, "normalized-basic-strike-transform-target-missing");
        }

        EvidenceCode evidence = Evidence(
            relicKey,
            "normalized-first-basic-strike-target-product-exact");
        PredictedEffect transform = Item(
            PredictedEffectKind.TransformCard,
            replacement.CardKey,
            null,
            0,
            evidence,
            sourceKey: canonicalTarget.CardKey,
            offerItemId: canonicalTarget.InstanceId) with
        {
            IsProductRelevant = true,
            IsPlayerChoiceEffect = false
        };
        PredictedEffectGroup group = Group(
            "new-leaf-normalized-basic-strike-transform",
            0,
            EffectSelectionPolicy.ChooseExactlyOne,
            EffectPredictionScope.ImmediateOptionEffect,
            "NewLeaf normalized Basic Strike product projection",
            new[] { transform },
            requiredSelectionCount: 1,
            rngEvidenceCode: RuntimeProfilePolicies.IsModernCore(_profileId)
                ? "niche:new-leaf-normalized-basic-strike-transform"
                : "transformations:new-leaf-normalized-basic-strike-transform",
            gameSelectionPolicy: EffectGameSelectionPolicy.PlayerChoice);
        NeowEffectProjection exactProduct = Exact(relicKey, new[] { group });
        return exactProduct with
        {
            Precision = PredictionPrecision.Partial,
            ProductRelevantProjectionPrecision = PredictionPrecision.Exact,
            ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Evaluated,
            FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Partial,
            EvidenceCode = evidence,
            DependencyImpact = EffectDependencyImpact.None(evidence)
        };
    }

    private static List<NeowEffectCardSnapshot> BuildNewLeafTransformCandidates(
        IReadOnlyList<NeowEffectCardSnapshot> transformPool,
        NeowEffectCardSnapshot source) =>
        NewLeafNormalizedSourcePolicy.BuildTransformCandidates(transformPool, source).ToList();

    private NeowEffectProjection ProjectStandalonePlayerChoiceCapability(
        ModelKey relicKey,
        string policyId,
        int selectionCount)
    {
        string groupId = relicKey.Entry.ToLowerInvariant() + "-player-choice-capability";
        PredictedEffect summary = Item(
            PredictedEffectKind.DescriptionOnly,
            relicKey,
            selectionCount,
            0,
            Evidence(relicKey, "base-player-choice-deck-mutation-available")) with
        {
            IsProductRelevant = true,
            NormalViewDetailLevel = EffectPresentationDetailLevel.CompactSummary,
            AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
            DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
            CompactSummaryKind = EffectCompactSummaryKind.PlayerChoiceDeckMutation
        };
        NeowEffectProjection exact = Exact(relicKey, new[]
        {
            Group(
                groupId,
                0,
                selectionCount == 2 ? EffectSelectionPolicy.ChooseExactlyN : EffectSelectionPolicy.ChooseExactlyOne,
                EffectPredictionScope.ImmediateOptionEffect,
                policyId,
                new[] { summary },
                requiredSelectionCount: selectionCount,
                normalViewDetailLevel: EffectPresentationDetailLevel.CompactSummary,
                advancedViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                diagnosticViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                compactSummaryKind: EffectCompactSummaryKind.PlayerChoiceDeckMutation)
        });
        return exact with
        {
            Precision = PredictionPrecision.Partial,
            ProductRelevantProjectionPrecision = PredictionPrecision.Exact,
            ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Evaluated,
            FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Partial,
            EvidenceCode = Evidence(relicKey, "base-player-choice-projection-exact")
        };
    }

    private NeowEffectProjection ProjectCapsule(
        ModelKey relicKey,
        NeowEffectWorkingState? state,
        int count,
        bool includeFixedCards,
        int recursionDepth,
        bool executeNestedAutomaticEffects = true)
    {
        var groups = new List<PredictedEffectGroup>();
        if (state?.Authority.HasExactRelicBag != true || state.RelicBag is null)
        {
            if (includeFixedCards && state?.Authority.CharacterStrikeKey is not null && state.Authority.CharacterDefendKey is not null)
            {
                AddSyntheticCardToDeck(state.Deck, state.Authority.CharacterStrikeKey.Value, "large-capsule-fixed-strike", EffectCardType.Attack, isBasic: true, isStrike: true);
                AddSyntheticCardToDeck(state.Deck, state.Authority.CharacterDefendKey.Value, "large-capsule-fixed-defend", EffectCardType.Skill, isBasic: true, isDefend: true);
                groups.Add(Group("large-capsule-fixed-cards", 1, EffectSelectionPolicy.NoPlayerChoice,
                    EffectPredictionScope.ImmediateOptionEffect, "LargeCapsule fixed cards", new[]
                    {
                        Item(PredictedEffectKind.AddCard, state.Authority.CharacterStrikeKey, null, 0,
                            Evidence(relicKey, "fixed-strike")) with
                        {
                            NormalViewDetailLevel = EffectPresentationDetailLevel.Hidden,
                            AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                            DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                            CompactSummaryKind = EffectCompactSummaryKind.FixedStarterPair
                        },
                        Item(PredictedEffectKind.AddCard, state.Authority.CharacterDefendKey, null, 1,
                            Evidence(relicKey, "fixed-defend")) with
                        {
                            NormalViewDetailLevel = EffectPresentationDetailLevel.Hidden,
                            AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                            DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                            CompactSummaryKind = EffectCompactSummaryKind.FixedStarterPair
                        }
                    },
                    normalViewDetailLevel: EffectPresentationDetailLevel.CompactSummary,
                    advancedViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                    diagnosticViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                    compactSummaryKind: EffectCompactSummaryKind.FixedStarterPair));
                EvidenceCode evidence = Evidence(relicKey, "relic-bag-unknown");
                return NeowEffectProjection.Partial(relicKey, groups,
                    PredictionWarningCode.EffectSnapshotIncomplete, evidence) with
                {
                    DependencyImpact = EffectDependencyImpact.ConservativeUnknownImmediateHook(
                        PredictionWarningCode.EffectSnapshotIncomplete,
                        evidence)
                };
            }
            return Unknown(relicKey, "ordered-relic-bag-required");
        }

        var nestedItems = new List<PredictedEffect>();
        bool nestedFullExact = true;
        bool nestedProductExact = true;
        ProductRelevantProjectionStatus nestedProductStatus = ProductRelevantProjectionStatus.Evaluated;
        EffectDependencyImpact capsuleImpact = EffectDependencyImpact.None(
            Evidence(relicKey, "capsule-dependency-domains-initial"));
        for (int i = 0; i < count; i++)
        {
            EffectRelicRarity rarity = NeowRewardGenerator.RollRelicRarity(state.Rng.Rewards);
            NeowEffectRelicSnapshot? pulled = NeowRewardGenerator.PullRelicWithFallback(state.RelicBag, rarity);
            if (pulled is null)
            {
                EvidenceCode evidence = Evidence(relicKey, "relic-bag-exhausted");
                return NeowEffectProjection.Partial(relicKey, groups,
                    PredictionWarningCode.EffectPoolEmpty, evidence) with
                {
                    DependencyImpact = EffectDependencyImpact.ConservativeUnknownImmediateHook(
                        PredictionWarningCode.EffectPoolEmpty,
                        evidence)
                };
            }

            string relicNodeId = $"{relicKey.Serialized}.nested-relic.{i}";
            nestedItems.Add(Item(
                PredictedEffectKind.AddRelic,
                pulled.RelicKey,
                null,
                nestedItems.Count,
                Evidence(relicKey, "nested-relic-obtain"),
                offerItemId: $"capsule.relic.{i}") with
            {
                EffectNodeId = relicNodeId,
                Relation = PredictedEffectRelation.NestedRelic,
                IsProductRelevant = true
            });

            if (AuthoredCapsuleEffects is { } authored &&
                (!authored.TryGetValue(relicKey, out var modeled) || !modeled.Contains(pulled.RelicKey)))
                continue;

            // When an earlier player choice was deliberately not evaluated by
            // product policy, Capsule contents remain objective seed facts, but
            // Whetstone/War Paint targets must not be projected from an invented
            // fallback deck. Keep the nested relic and suppress only the dependent
            // automatic effect.
            if (!executeNestedAutomaticEffects &&
                pulled.NestedEffectKind is NestedRelicEffectKind.Whetstone or NestedRelicEffectKind.WarPaint)
            {
                nestedFullExact = false;
                continue;
            }

            NeowEffectProjection nested = ProjectOrdinaryNestedRelic(
                pulled,
                state,
                recursionDepth + 1,
                nestedItems.Count);
            foreach (PredictedEffectGroup nestedGroup in nested.EffectGroups.OrderBy(group => group.GroupOrder))
            {
                PredictedEffect[] productItems = nestedGroup.OrderedItems
                    .OrderBy(item => item.ItemOrder)
                    .Where(item => item.IsProductRelevant && item.Kind != PredictedEffectKind.DescriptionOnly)
                    .ToArray();
                if (productItems.Length == 0)
                {
                    continue;
                }

                bool automaticUpgrade = productItems.Any(item => item.Kind == PredictedEffectKind.UpgradeCard);
                string? automaticNodeId = null;
                if (automaticUpgrade)
                {
                    automaticNodeId = relicNodeId + ".automatic";
                    nestedItems.Add(new PredictedEffect(
                        PredictedEffectKind.AutomaticEffect,
                        null,
                        null,
                        nested.ProductRelevantProjectionPrecision,
                        nested.Warnings.Select(warning => warning.Code).Distinct().ToArray(),
                        Evidence(pulled.RelicKey, "nested-automatic-effect"),
                        ItemOrder: nestedItems.Count,
                        Multiplicity: 1,
                        SourceStep: $"{relicKey.Entry} -> {pulled.RelicKey.Entry} automatic effect",
                        SourceRelicKey: pulled.RelicKey,
                        SourceEffectGroupId: nestedGroup.SourceEffectGroupId ?? nestedGroup.GroupId,
                        Phase: PredictedEffectPhase.NestedObtain,
                        EffectNodeId: automaticNodeId,
                        ParentEffectNodeId: relicNodeId,
                        Relation: PredictedEffectRelation.NestedAutomaticEffect,
                        IsProductRelevant: true,
                        NormalViewDetailLevel: EffectPresentationDetailLevel.CompactSummary,
                        AdvancedViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                        DiagnosticViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                        CompactSummaryKind: EffectCompactSummaryKind.Applied));
                }

                foreach (PredictedEffect nestedItem in productItems)
                {
                    string nodeId = automaticNodeId is null
                        ? relicNodeId + ".effect." + nestedItems.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : automaticNodeId + ".target." + nestedItems.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    nestedItems.Add(nestedItem with
                    {
                        ItemOrder = nestedItems.Count,
                        SourceStep = $"{relicKey.Entry} -> {pulled.RelicKey.Entry} -> {nestedItem.SourceStep}",
                        EffectNodeId = nodeId,
                        ParentEffectNodeId = automaticNodeId ?? relicNodeId,
                        Relation = automaticNodeId is null
                            ? PredictedEffectRelation.NestedAutomaticEffect
                            : PredictedEffectRelation.AffectedTarget,
                        IsProductRelevant = true,
                        NormalViewDetailLevel = automaticNodeId is null
                            ? EffectPresentationDetailLevel.Detailed
                            : EffectPresentationDetailLevel.Hidden,
                        AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                        DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                        CompactSummaryKind = automaticNodeId is null
                            ? EffectCompactSummaryKind.None
                            : EffectCompactSummaryKind.Applied
                    });
                }
            }
            nestedFullExact &= nested.Precision == PredictionPrecision.Exact;
            nestedProductExact &= nested.ProductRelevantProjectionPrecision == PredictionPrecision.Exact;
            if (nested.ProductRelevantProjectionStatus != ProductRelevantProjectionStatus.Evaluated)
            {
                nestedProductStatus = nested.ProductRelevantProjectionStatus;
            }
            capsuleImpact = capsuleImpact.Merge(
                ResolveCapsuleNestedBonesDependencyImpact(pulled, nested));
        }

        bool smallCapsule = relicKey == BaseGameModelKeys.Relics.SmallCapsule;
        groups.Add(Group(
            smallCapsule ? "small-capsule-assume-obtain" : "large-capsule-forced-obtain",
            0,
            smallCapsule ? EffectSelectionPolicy.NoPlayerChoice : EffectSelectionPolicy.ForcedObtain,
            EffectPredictionScope.NestedObtain,
            smallCapsule ? "SmallCapsule product route assumes obtain" : "LargeCapsule ordered forced obtain",
            nestedItems,
            gameSelectionPolicy: smallCapsule
                ? EffectGameSelectionPolicy.OptionalObtain
                : EffectGameSelectionPolicy.ForcedObtain,
            routeEnumerationPolicy: smallCapsule
                ? EffectRouteEnumerationPolicy.AssumeObtainOnly
                : EffectRouteEnumerationPolicy.FollowGameSelection,
            policyEvidenceCodes: smallCapsule
                ? new EvidenceCode[]
                {
                    "SmallCapsuleSkippableInGame",
                    "SmallCapsuleObtainAssumedByProductPolicy"
                }
                : Array.Empty<EvidenceCode>()));

        if (includeFixedCards)
        {
            if (state.Authority.CharacterStrikeKey is null || state.Authority.CharacterDefendKey is null)
            {
                EvidenceCode evidence = Evidence(relicKey, "fixed-card-identities-missing");
                return NeowEffectProjection.Partial(relicKey, groups,
                    PredictionWarningCode.EffectSnapshotIncomplete, evidence) with
                {
                    ProductRelevantProjectionPrecision = PredictionPrecision.Partial,
                    FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Partial,
                    ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Evaluated,
                    DependencyImpact = capsuleImpact.Merge(
                        EffectDependencyImpact.ShadowOnlyPartial(
                            PredictionWarningCode.EffectSnapshotIncomplete,
                            evidence))
                };
            }
            AddSyntheticCardToDeck(state.Deck, state.Authority.CharacterStrikeKey.Value, "large-capsule-fixed-strike", EffectCardType.Attack, isBasic: true, isStrike: true);
            AddSyntheticCardToDeck(state.Deck, state.Authority.CharacterDefendKey.Value, "large-capsule-fixed-defend", EffectCardType.Skill, isBasic: true, isDefend: true);
            groups.Add(Group("large-capsule-fixed-cards", 1, EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionScope.ImmediateOptionEffect, "LargeCapsule fixed cards", new[]
                {
                    Item(PredictedEffectKind.AddCard, state.Authority.CharacterStrikeKey, null, 0,
                        Evidence(relicKey, "fixed-strike")) with
                    {
                        NormalViewDetailLevel = EffectPresentationDetailLevel.Hidden,
                        AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                        DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                        CompactSummaryKind = EffectCompactSummaryKind.FixedStarterPair
                    },
                    Item(PredictedEffectKind.AddCard, state.Authority.CharacterDefendKey, null, 1,
                        Evidence(relicKey, "fixed-defend")) with
                    {
                        NormalViewDetailLevel = EffectPresentationDetailLevel.Hidden,
                        AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                        DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                        CompactSummaryKind = EffectCompactSummaryKind.FixedStarterPair
                    }
                },
                normalViewDetailLevel: EffectPresentationDetailLevel.CompactSummary,
                advancedViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                diagnosticViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                compactSummaryKind: EffectCompactSummaryKind.FixedStarterPair));
        }

        NeowEffectProjection projection = nestedFullExact
            ? Exact(relicKey, groups)
            : NeowEffectProjection.Partial(
                relicKey,
                groups,
                PredictionWarningCode.EffectNestedObtainIncomplete,
                Evidence(relicKey, "nested-hook-partial"));
        return projection with
        {
            ProductRelevantProjectionPrecision = nestedProductExact
                ? PredictionPrecision.Exact
                : PredictionPrecision.Partial,
            ProductRelevantProjectionStatus = nestedProductStatus,
            FullEffectSemanticsCompleteness = nestedFullExact
                ? FullEffectSemanticsCompleteness.Complete
                : FullEffectSemanticsCompleteness.Partial,
            DependencyImpact = capsuleImpact
        };
    }

    private NeowEffectProjection ProjectOrdinaryNestedRelic(
        NeowEffectRelicSnapshot relic,
        NeowEffectWorkingState state,
        int recursionDepth,
        int itemOrderOffset)
    {
        if (recursionDepth > 4)
        {
            return NeowEffectProjection.Unsupported(relic.RelicKey, Evidence(relic.RelicKey, "nested-depth-exceeded"));
        }
        if (!relic.NestedClassificationExact)
        {
            return NeowEffectProjection.Unsupported(
                relic.RelicKey,
                Evidence(relic.RelicKey, "nested-hook-classification-incomplete"));
        }
        return relic.NestedEffectKind switch
        {
            NestedRelicEffectKind.NoTrackedImmediateEffect => NeowEffectProjection.Exact(
                relic.RelicKey,
                new[] { Group($"nested-{relic.RelicKey.Entry}-no-tracked-effect", 0,
                    EffectSelectionPolicy.NoPlayerChoice, EffectPredictionScope.NestedObtain,
                    "Audited no tracked immediate effect", new[]
                    {
                        Item(PredictedEffectKind.DescriptionOnly, relic.RelicKey, 0, itemOrderOffset,
                            Evidence(relic.RelicKey, "no-tracked-immediate-effect"))
                    }) },
                Evidence(relic.RelicKey, "nested-no-tracked-effect")),
            NestedRelicEffectKind.Whetstone => ProjectUpgradeOrdinaryRelic(
                relic.RelicKey, state, EffectCardType.Attack, "Whetstone", itemOrderOffset),
            NestedRelicEffectKind.WarPaint => ProjectUpgradeOrdinaryRelic(
                relic.RelicKey, state, EffectCardType.Skill, "WarPaint", itemOrderOffset),
            _ => NeowEffectProjection.Unsupported(
                relic.RelicKey,
                Evidence(relic.RelicKey, "unknown-ordinary-relic-hook")) with
            {
                DependencyImpact = EffectDependencyImpact.ConservativeUnknownImmediateHook(
                    PredictionWarningCode.EffectNestedObtainIncomplete,
                    Evidence(relic.RelicKey, "unknown-ordinary-relic-hook-continuity"))
            }
        };
    }

    private NeowEffectProjection ProjectUpgradeOrdinaryRelic(
        ModelKey relicKey,
        NeowEffectWorkingState state,
        EffectCardType type,
        string source,
        int itemOrderOffset)
    {
        if (state.Deck is null || !state.Authority.DeckExact)
        {
            return Unknown(relicKey, "deck-required-for-upgrade-relic");
        }
        List<NeowEffectCardSnapshot> candidates = state.Deck.Cards
            .Where(card => card.CardType == type && card.CanUpgrade)
            .OrderBy(card => card.CardKey.Serialized, StringComparer.Ordinal)
            .ThenBy(card => card.InstanceId, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
        {
            return NeowEffectProjection.Exact(
                relicKey,
                new[] { Group($"{source.ToLowerInvariant()}-no-candidates", 0,
                    EffectSelectionPolicy.NoPlayerChoice, EffectPredictionScope.NestedObtain,
                    $"{source}.AfterObtained", new[]
                    {
                        Item(PredictedEffectKind.DescriptionOnly, null, 0, itemOrderOffset,
                            Evidence(relicKey, "no-upgrade-candidates"))
                    }) },
                Evidence(relicKey, "exact-no-upgrade-candidates"));
        }
        state.Rng.Niche.UnstableShuffle(candidates);
        var items = new List<PredictedEffect>();
        foreach (NeowEffectCardSnapshot card in candidates.Take(2))
        {
            if (state.Deck.TryUpgrade(card.InstanceId, out NeowEffectCardSnapshot upgraded))
            {
                items.Add(Item(PredictedEffectKind.UpgradeCard, upgraded.CardKey, 1,
                    itemOrderOffset + items.Count, Evidence(relicKey, "upgrade-target"),
                    sourceKey: card.CardKey, offerItemId: card.InstanceId));
            }
        }
        return Exact(relicKey, new[]
        {
            Group($"nested-{source.ToLowerInvariant()}", 0, EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionScope.NestedObtain, $"{source}.AfterObtained",
                items.Select(item => item with
                {
                    NormalViewDetailLevel = EffectPresentationDetailLevel.Hidden,
                    AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                    DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                    CompactSummaryKind = EffectCompactSummaryKind.Applied
                }).ToArray(),
                normalViewDetailLevel: EffectPresentationDetailLevel.CompactSummary,
                advancedViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                diagnosticViewDetailLevel: EffectPresentationDetailLevel.Detailed,
                compactSummaryKind: EffectCompactSummaryKind.Applied)
        });
    }

    private sealed record OfferSelectionCard(
        PredictedEffect AddEffect,
        IReadOnlyList<PredictedEffect> Effects,
        NeowEffectCardSnapshot CardTemplate);

    private sealed record OfferSelectionOption(
        string OptionId,
        IReadOnlyList<OfferSelectionCard> Cards,
        bool IsSkip);

    private sealed record OfferSelectionStep(
        string StepId,
        IReadOnlyList<OfferSelectionOption> Options);

    private sealed record RawFiniteChoiceOutcome(
        NeowEffectWorkingState State,
        PlayerChoicePolicyDescriptor ChoicePolicy,
        IReadOnlyList<PredictedEffect> OrderedItems,
        string ChoiceDeckFingerprint,
        string RelevantRngFingerprint);

    private sealed record FiniteBonesRouteOutcome(
        NeowEffectWorkingState State,
        string RouteId,
        IReadOnlyList<PredictedEffect> OrderedItems,
        PredictionPrecision Precision,
        PlayerChoicePolicyDescriptor ChoicePolicy,
        string ChoiceDeckFingerprint,
        string RelevantRngFingerprint,
        int RawLegalSelectionCount,
        int DistinctDeckStateCount,
        int EquivalentSelectionCount);

    private IReadOnlyList<FiniteBonesRouteOutcome>? EnumerateOfferSelectionBonesRoutes(
        ModelKey selected,
        NeowEffectWorkingState postObjective,
        NeowEffectProjection objectiveProjection)
    {
        if (postObjective.Authority.HasExactDeck != true || postObjective.Deck is null)
        {
            return null;
        }

        IReadOnlyList<OfferSelectionStep>? stepsResult = BuildOfferSelectionSteps(
            postObjective.Authority,
            objectiveProjection.EffectGroups);
        if (stepsResult is null)
        {
            return null;
        }
        OfferSelectionStep[] steps = stepsResult.ToArray();
        if (steps.Length == 0)
        {
            return Array.Empty<FiniteBonesRouteOutcome>();
        }

        var combinations = new List<IReadOnlyList<OfferSelectionOption>>
        {
            Array.Empty<OfferSelectionOption>()
        };
        foreach (OfferSelectionStep step in steps)
        {
            var next = new List<IReadOnlyList<OfferSelectionOption>>();
            foreach (IReadOnlyList<OfferSelectionOption> prefix in combinations)
            {
                foreach (OfferSelectionOption option in step.Options)
                {
                    next.Add(prefix.Concat(new[] { option }).ToArray());
                    if (next.Count > postObjective.Authority.BranchBudget)
                    {
                        return null;
                    }
                }
            }
            combinations = next;
        }

        var raw = new List<RawFiniteChoiceOutcome>();
        for (int combinationIndex = 0; combinationIndex < combinations.Count; combinationIndex++)
        {
            IReadOnlyList<OfferSelectionOption> combination = combinations[combinationIndex];
            NeowEffectWorkingState route = postObjective.Clone();
            var choiceEffects = new List<PredictedEffect>();
            var selectedKeys = new List<ModelKey>();
            var upgradeLevels = new List<int>();
            var policyParts = new List<string>();
            int selectedChoiceCount = 0;

            for (int stepIndex = 0; stepIndex < combination.Count; stepIndex++)
            {
                OfferSelectionOption option = combination[stepIndex];
                policyParts.Add($"{steps[stepIndex].StepId}:{option.OptionId}");
                if (option.IsSkip)
                {
                    continue;
                }

                selectedChoiceCount++;
                for (int cardIndex = 0; cardIndex < option.Cards.Count; cardIndex++)
                {
                    OfferSelectionCard card = option.Cards[cardIndex];
                    if (!TryAddOfferedCardToDeck(
                            route.Deck!,
                            card,
                            selected,
                            stepIndex,
                            combinationIndex,
                            cardIndex,
                            out NeowEffectCardSnapshot added))
                    {
                        return null;
                    }

                    selectedKeys.Add(card.CardTemplate.CardKey);
                    upgradeLevels.Add(added.UpgradeLevel);
                    foreach (PredictedEffect effect in card.Effects.OrderBy(effect => effect.ItemOrder))
                    {
                        choiceEffects.Add(effect with
                        {
                            ItemOrder = choiceEffects.Count,
                            SourceRelicKey = selected,
                            SourceEffectGroupId = selected.Serialized + ".offer-selection",
                            IsPlayerChoiceEffect = true,
                            IsProductRelevant = true,
                            NormalViewDetailLevel = EffectPresentationDetailLevel.Hidden,
                            AdvancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
                            DiagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed
                        });
                    }
                }
            }

            PlayerChoicePolicyKind policyKind = selectedChoiceCount == 0
                ? PlayerChoicePolicyKind.SkipOfferedCard
                : steps.Length > 1 || selectedKeys.Count > selectedChoiceCount
                    ? PlayerChoicePolicyKind.ChooseOfferedCardCombination
                    : PlayerChoicePolicyKind.ChooseOfferedCard;
            var policy = new PlayerChoicePolicyDescriptor(
                "offer-selection:" + string.Join(";", policyParts),
                policyKind,
                selectedKeys,
                selectedChoiceCount,
                upgradeLevels);
            raw.Add(new RawFiniteChoiceOutcome(
                route,
                policy,
                choiceEffects,
                BuildObservableDeckFingerprint(route.Deck!.Cards),
                BuildWorkingStateContinuationFingerprint(route)));
        }

        return AggregateFiniteChoiceRoutes(selected, raw);
    }

    private static IReadOnlyList<OfferSelectionStep>? BuildOfferSelectionSteps(
        NeowEffectAuthoritySnapshot authority,
        IReadOnlyList<PredictedEffectGroup> effectGroups)
    {
        PredictedEffectGroup[] candidateGroups = effectGroups
            .Where(group => group.SelectionPolicy is EffectSelectionPolicy.ChooseExactlyOne or
                EffectSelectionPolicy.ChooseOneOrSkip)
            .Where(group => group.OrderedItems.Any(item => item.Kind == PredictedEffectKind.AddCard))
            .OrderBy(group => group.GroupOrder)
            .ToArray();
        if (candidateGroups.Length == 0)
        {
            return Array.Empty<OfferSelectionStep>();
        }

        var steps = new List<OfferSelectionStep>();
        foreach (IGrouping<string, PredictedEffectGroup> unit in candidateGroups
                     .GroupBy(
                         group => string.IsNullOrWhiteSpace(group.SelectionSetId)
                             ? "group:" + group.GroupId
                             : "selection-set:" + group.SelectionSetId,
                         StringComparer.Ordinal)
                     .OrderBy(group => group.Min(item => item.GroupOrder)))
        {
            PredictedEffectGroup[] unitGroups = unit
                .OrderBy(group => group.GroupOrder)
                .ToArray();
            bool alternativeGroups = unitGroups.Length > 1 &&
                                     !string.IsNullOrWhiteSpace(unitGroups[0].SelectionSetId);
            if (alternativeGroups)
            {
                var alternatives = new List<OfferSelectionOption>();
                foreach (PredictedEffectGroup group in unitGroups)
                {
                    OfferSelectionOption? option = BuildOfferSelectionOption(
                        authority,
                        group.BundleId ?? group.GroupId,
                        group.OrderedItems);
                    if (option is null)
                    {
                        return null;
                    }
                    alternatives.Add(option);
                }

                EffectSelectionPolicy policy = unitGroups.Any(group =>
                    group.SelectionPolicy == EffectSelectionPolicy.ChooseOneOrSkip)
                    ? EffectSelectionPolicy.ChooseOneOrSkip
                    : EffectSelectionPolicy.ChooseExactlyOne;
                if (policy == EffectSelectionPolicy.ChooseOneOrSkip)
                {
                    alternatives.Add(new OfferSelectionOption(
                        unitGroups[0].SelectionSetId + ".skip",
                        Array.Empty<OfferSelectionCard>(),
                        IsSkip: true));
                }
                steps.Add(new OfferSelectionStep(
                    unitGroups[0].SelectionSetId!,
                    alternatives));
                continue;
            }

            foreach (PredictedEffectGroup group in unitGroups)
            {
                var options = new List<OfferSelectionOption>();
                foreach (IGrouping<string, PredictedEffect> optionEffects in group.OrderedItems
                             .Where(item => item.Kind == PredictedEffectKind.AddCard)
                             .GroupBy(
                                 item => item.OfferItemId ?? $"{group.GroupId}.item.{item.ItemOrder}",
                                 StringComparer.Ordinal)
                             .OrderBy(option => option.Min(item => item.ItemOrder)))
                {
                    PredictedEffect[] related = group.OrderedItems
                        .Where(item => string.Equals(
                            item.OfferItemId ?? $"{group.GroupId}.item.{item.ItemOrder}",
                            optionEffects.Key,
                            StringComparison.Ordinal))
                        .OrderBy(item => item.ItemOrder)
                        .ToArray();
                    OfferSelectionOption? option = BuildOfferSelectionOption(
                        authority,
                        optionEffects.Key,
                        related);
                    if (option is null)
                    {
                        return null;
                    }
                    options.Add(option);
                }

                if (group.SelectionPolicy == EffectSelectionPolicy.ChooseOneOrSkip)
                {
                    options.Add(new OfferSelectionOption(
                        group.GroupId + ".skip",
                        Array.Empty<OfferSelectionCard>(),
                        IsSkip: true));
                }
                if (options.Count == 0)
                {
                    return null;
                }
                steps.Add(new OfferSelectionStep(group.GroupId, options));
            }
        }

        return steps;
    }

    private static OfferSelectionOption? BuildOfferSelectionOption(
        NeowEffectAuthoritySnapshot authority,
        string optionId,
        IReadOnlyList<PredictedEffect> effects)
    {
        var cards = new List<OfferSelectionCard>();
        foreach (PredictedEffect add in effects
                     .Where(item => item.Kind == PredictedEffectKind.AddCard && item.TargetKey.HasValue)
                     .OrderBy(item => item.ItemOrder))
        {
            NeowEffectCardSnapshot? template = FindCardTemplate(authority, add.TargetKey!.Value);
            if (template is null)
            {
                return null;
            }

            PredictedEffect[] cardEffects = effects
                .Where(item => item.ItemOrder == add.ItemOrder ||
                    (item.Kind == PredictedEffectKind.UpgradeCard &&
                     string.Equals(item.OfferItemId, add.OfferItemId, StringComparison.Ordinal) &&
                     (item.SourceKey == add.TargetKey ||
                      (!item.SourceKey.HasValue && item.TargetKey == add.TargetKey))))
                .OrderBy(item => item.ItemOrder)
                .ToArray();
            cards.Add(new OfferSelectionCard(
                add,
                cardEffects,
                template));
        }

        return cards.Count == 0
            ? null
            : new OfferSelectionOption(optionId, cards, IsSkip: false);
    }

    private static NeowEffectCardSnapshot? FindCardTemplate(
        NeowEffectAuthoritySnapshot authority,
        ModelKey cardKey)
    {
        IEnumerable<NeowEffectCardSnapshot> candidates =
            (authority.CharacterRewardPool ?? Array.Empty<NeowEffectCardSnapshot>())
            .Concat(authority.ColorlessRewardPool ?? Array.Empty<NeowEffectCardSnapshot>())
            .Concat(authority.TransformPool ?? Array.Empty<NeowEffectCardSnapshot>())
            .Concat((authority.OtherCharacterPools ?? Array.Empty<CharacterCardPoolSnapshot>())
                .SelectMany(pool => pool.Cards));
        return candidates.FirstOrDefault(card => card.CardKey == cardKey);
    }

    private static bool TryAddOfferedCardToDeck(
        NeowShadowDeck deck,
        OfferSelectionCard card,
        ModelKey sourceRelic,
        int stepIndex,
        int combinationIndex,
        int cardIndex,
        out NeowEffectCardSnapshot added)
    {
        PredictedEffect? upgrade = card.Effects.FirstOrDefault(effect =>
            effect.Kind == PredictedEffectKind.UpgradeCard);
        int multiplicity = Math.Max(1, card.AddEffect.Multiplicity);
        int upgradeLevel = upgrade is null
            ? card.CardTemplate.UpgradeLevel
            : Math.Min(card.CardTemplate.MaxUpgradeLevel, card.CardTemplate.UpgradeLevel + 1);
        NeowEffectCardSnapshot? first = null;
        for (int copyIndex = 0; copyIndex < multiplicity; copyIndex++)
        {
            int order = deck.Cards.Count == 0 ? 0 : deck.Cards.Max(item => item.PoolOrder) + 1;
            NeowEffectCardSnapshot copy = card.CardTemplate with
            {
                InstanceId = $"{sourceRelic.Entry.ToLowerInvariant()}.offer.{stepIndex}.{combinationIndex}.{cardIndex}.{copyIndex}",
                CardKey = upgrade?.TargetKey ?? card.CardTemplate.CardKey,
                PoolOrder = order,
                UpgradeLevel = upgradeLevel,
                CanUpgrade = upgradeLevel < card.CardTemplate.MaxUpgradeLevel,
                SourceAssembly = null,
                SourceModId = null
            };
            deck.Add(copy);
            first ??= copy;
        }
        added = first!;
        return first is not null;
    }

    private IReadOnlyList<FiniteBonesRouteOutcome>? EnumerateFiniteBonesRoutes(
        ModelKey selected,
        NeowEffectWorkingState preChoice)
    {
        if (preChoice.Authority.HasExactDeck != true || preChoice.Deck is null)
        {
            return null;
        }

        var raw = new List<RawFiniteChoiceOutcome>();
        if (selected == BaseGameModelKeys.Relics.PreciseScissors)
        {
            foreach (NeowEffectCardSnapshot card in preChoice.Deck.Cards.Where(card =>
                         card.CanRemove && card.IsBasic && (card.IsStrike || card.IsDefend)))
            {
                NeowEffectWorkingState route = preChoice.Clone();
                if (!route.Deck!.TryRemove(card.InstanceId)) continue;
                PlayerChoicePolicyKind kind = card.IsStrike
                    ? PlayerChoicePolicyKind.RemoveStrike
                    : PlayerChoicePolicyKind.RemoveDefend;
                var policy = new PlayerChoicePolicyDescriptor(
                    card.IsStrike ? "remove-strike" : "remove-defend",
                    kind,
                    new[] { card.CardKey },
                    1,
                    new[] { card.UpgradeLevel });
                raw.Add(new RawFiniteChoiceOutcome(
                    route,
                    policy,
                    new[]
                    {
                        Item(PredictedEffectKind.RemoveCard, card.CardKey, 1, 0,
                            Evidence(selected, "remove-basic-choice"),
                            sourceKey: card.CardKey,
                            offerItemId: card.InstanceId) with
                        {
                            IsPlayerChoiceEffect = true,
                            IsProductRelevant = true
                        }
                    },
                    BuildObservableDeckFingerprint(route.Deck.Cards),
                    BuildWorkingStateContinuationFingerprint(route)));
            }
            return AggregateFiniteChoiceRoutes(selected, raw);
        }

        if (selected == BaseGameModelKeys.Relics.PrecariousShears)
        {
            NeowEffectCardSnapshot[] strikes = preChoice.Deck.Cards
                .Where(card => card.CanRemove && card.IsBasic && card.IsStrike).ToArray();
            NeowEffectCardSnapshot[] defends = preChoice.Deck.Cards
                .Where(card => card.CanRemove && card.IsBasic && card.IsDefend).ToArray();
            var pairs = new List<(NeowEffectCardSnapshot First, NeowEffectCardSnapshot Second)>();
            AddPairs(strikes, strikes, true, pairs);
            AddPairs(defends, defends, true, pairs);
            AddPairs(strikes, defends, false, pairs);
            foreach ((NeowEffectCardSnapshot first, NeowEffectCardSnapshot second) in pairs)
            {
                NeowEffectWorkingState route = preChoice.Clone();
                if (!route.Deck!.TryRemove(first.InstanceId) || !route.Deck.TryRemove(second.InstanceId)) continue;
                bool ss = first.IsStrike && second.IsStrike;
                bool dd = first.IsDefend && second.IsDefend;
                PlayerChoicePolicyKind kind = ss
                    ? PlayerChoicePolicyKind.RemoveStrikeStrike
                    : dd
                        ? PlayerChoicePolicyKind.RemoveDefendDefend
                        : PlayerChoicePolicyKind.RemoveStrikeDefend;
                string policyId = ss ? "remove-ss" : dd ? "remove-dd" : "remove-sd";
                var policy = new PlayerChoicePolicyDescriptor(
                    policyId,
                    kind,
                    new[] { first.CardKey, second.CardKey },
                    2,
                    new[] { first.UpgradeLevel, second.UpgradeLevel });
                raw.Add(new RawFiniteChoiceOutcome(
                    route,
                    policy,
                    new[]
                    {
                        Item(PredictedEffectKind.RemoveCard, first.CardKey, 1, 0,
                            Evidence(selected, "remove-first-basic"), sourceKey: first.CardKey, offerItemId: first.InstanceId) with
                        {
                            IsPlayerChoiceEffect = true,
                            IsProductRelevant = true
                        },
                        Item(PredictedEffectKind.RemoveCard, second.CardKey, 1, 1,
                            Evidence(selected, "remove-second-basic"), sourceKey: second.CardKey, offerItemId: second.InstanceId) with
                        {
                            IsPlayerChoiceEffect = true,
                            IsProductRelevant = true
                        }
                    },
                    BuildObservableDeckFingerprint(route.Deck.Cards),
                    BuildWorkingStateContinuationFingerprint(route)));
            }
            return AggregateFiniteChoiceRoutes(selected, raw);
        }

        if (selected == BaseGameModelKeys.Relics.Pomander)
        {
            foreach (NeowEffectCardSnapshot card in preChoice.Deck.Cards.Where(card =>
                         card.IsBasic && !card.IsStrike && !card.IsDefend && card.CanUpgrade))
            {
                NeowEffectWorkingState route = preChoice.Clone();
                if (!route.Deck!.TryUpgrade(card.InstanceId, out NeowEffectCardSnapshot upgraded)) continue;
                var policy = new PlayerChoicePolicyDescriptor(
                    "upgrade-starter-card:" + card.CardKey.Serialized,
                    PlayerChoicePolicyKind.UpgradeStarterCard,
                    new[] { card.CardKey },
                    1,
                    new[] { card.UpgradeLevel });
                raw.Add(new RawFiniteChoiceOutcome(
                    route,
                    policy,
                    new[]
                    {
                        Item(PredictedEffectKind.UpgradeCard, upgraded.CardKey, 1, 0,
                            Evidence(selected, "upgrade-basic-choice"), sourceKey: card.CardKey, offerItemId: card.InstanceId) with
                        {
                            IsPlayerChoiceEffect = true,
                            IsProductRelevant = true
                        }
                    },
                    BuildObservableDeckFingerprint(route.Deck.Cards),
                    BuildWorkingStateContinuationFingerprint(route)));
            }
            return AggregateFiniteChoiceRoutes(selected, raw);
        }

        if (selected == BaseGameModelKeys.Relics.NewLeaf)
        {
            if (!preChoice.Authority.HasExactTransformPool)
            {
                return null;
            }
            foreach (NeowEffectCardSnapshot target in preChoice.Deck.Cards.Where(card =>
                         card.IsBasic && (card.IsStrike || card.IsDefend) &&
                         card.CardType is not (EffectCardType.Curse or EffectCardType.Status)))
            {
                List<NeowEffectCardSnapshot> candidates = BuildNewLeafTransformCandidates(
                    preChoice.Authority.TransformPool!,
                    target);
                if (candidates.Count == 0) continue;
                NeowEffectWorkingState route = preChoice.Clone();
                Xoshiro256StarStar rng = RuntimeProfilePolicies.IsModernCore(_profileId)
                    ? route.Rng.Niche
                    : route.Rng.Transformations;
                NeowEffectCardSnapshot replacement = candidates[rng.NextInt(candidates.Count)];
                if (!route.Deck!.TryTransform(target.InstanceId, replacement.CardKey)) continue;
                var policy = new PlayerChoicePolicyDescriptor(
                    target.IsStrike ? "transform-strike" : "transform-defend",
                    target.IsStrike ? PlayerChoicePolicyKind.TransformStrike : PlayerChoicePolicyKind.TransformDefend,
                    new[] { target.CardKey },
                    1,
                    new[] { target.UpgradeLevel });
                raw.Add(new RawFiniteChoiceOutcome(
                    route,
                    policy,
                    new[]
                    {
                        Item(PredictedEffectKind.TransformCard, replacement.CardKey, 1, 0,
                            Evidence(selected, RuntimeProfilePolicies.IsModernCore(_profileId)
                                ? "target-specific-transform-niche"
                                : "target-specific-transform-transformations"),
                            sourceKey: target.CardKey,
                            offerItemId: target.InstanceId) with
                        {
                            IsPlayerChoiceEffect = true,
                            IsProductRelevant = true
                        }
                    },
                    BuildObservableDeckFingerprint(route.Deck.Cards),
                    BuildWorkingStateContinuationFingerprint(route)));
            }
            return AggregateFiniteChoiceRoutes(selected, raw);
        }

        return null;
    }

    private static IReadOnlyList<FiniteBonesRouteOutcome> AggregateFiniteChoiceRoutes(
        ModelKey selected,
        IReadOnlyList<RawFiniteChoiceOutcome> raw)
    {
        if (raw.Count == 0)
        {
            return Array.Empty<FiniteBonesRouteOutcome>();
        }

        var results = new List<FiniteBonesRouteOutcome>();
        foreach (IGrouping<string, RawFiniteChoiceOutcome> policyGroup in raw
                     .GroupBy(route => route.ChoicePolicy.StableKey, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            IGrouping<string, RawFiniteChoiceOutcome>[] deckGroups = policyGroup
                .GroupBy(route => string.Join("|",
                    route.ChoiceDeckFingerprint,
                    route.RelevantRngFingerprint), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            int rawPolicyCount = policyGroup.Count();
            int distinctPolicyDeckStates = deckGroups.Length;
            for (int index = 0; index < deckGroups.Length; index++)
            {
                RawFiniteChoiceOutcome representative = deckGroups[index].First();
                string routeFingerprint = FingerprintBonesParts(
                    "finite-choice-route-id-v1",
                    new[]
                    {
                        selected.Serialized,
                        representative.ChoicePolicy.StableKey,
                        representative.ChoiceDeckFingerprint,
                        representative.RelevantRngFingerprint
                    });
                string routeId = selected.Entry.ToLowerInvariant() + "." +
                                 representative.ChoicePolicy.PolicyId.Replace(':', '-') + "." +
                                 routeFingerprint[..16];
                results.Add(new FiniteBonesRouteOutcome(
                    representative.State,
                    routeId,
                    representative.OrderedItems,
                    PredictionPrecision.Exact,
                    representative.ChoicePolicy,
                    representative.ChoiceDeckFingerprint,
                    representative.RelevantRngFingerprint,
                    rawPolicyCount,
                    distinctPolicyDeckStates,
                    deckGroups[index].Count()));
            }
        }
        return results;
    }

    private static NeowEffectCardSnapshot[] DistinctByCardState(IEnumerable<NeowEffectCardSnapshot> source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<NeowEffectCardSnapshot>();
        foreach (NeowEffectCardSnapshot card in source.OrderBy(card => card.PoolOrder))
        {
            string key = string.Join(":",
                card.CardKey.Serialized,
                card.UpgradeLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                card.MaxUpgradeLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                card.IsBasic ? "B" : "N",
                card.IsStrike ? "S" : "-",
                card.IsDefend ? "D" : "-",
                card.CanUpgrade ? "U" : "-",
                card.CanRemove ? "R" : "-");
            if (seen.Add(key))
            {
                result.Add(card);
            }
        }
        return result.ToArray();
    }

    private static void AddPairs(
        IReadOnlyList<NeowEffectCardSnapshot> left,
        IReadOnlyList<NeowEffectCardSnapshot> right,
        bool sameSet,
        ICollection<(NeowEffectCardSnapshot First, NeowEffectCardSnapshot Second)> output)
    {
        for (int i = 0; i < left.Count; i++)
        {
            int start = sameSet ? i + 1 : 0;
            for (int j = start; j < right.Count; j++)
            {
                output.Add((left[i], right[j]));
            }
        }
    }

    private static string BuildObservableDeckFingerprint(IEnumerable<NeowEffectCardSnapshot> cards) =>
        string.Join("|", cards
            .OrderBy(card => card.PoolOrder)
            .Select(card => string.Join(":",
                card.CardKey.Serialized,
                card.UpgradeLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                card.MaxUpgradeLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                card.IsBasic ? "B" : "N",
                card.IsStrike ? "S" : "-",
                card.IsDefend ? "D" : "-",
                card.CanUpgrade ? "U" : "-",
                card.CanRemove ? "R" : "-")));

    private bool TryTransformCard(
        NeowEffectWorkingState state,
        NeowEffectCardSnapshot source,
        Xoshiro256StarStar rng,
        out ModelKey replacement)
    {
        List<NeowEffectCardSnapshot> candidates = state.Authority.TransformPool!
            .Where(card => string.Equals(card.PoolId, source.PoolId, StringComparison.Ordinal))
            .Where(card => card.CardKey != source.CardKey)
            .Where(card => card.Rarity is not EffectCardRarity.Basic and not EffectCardRarity.Ancient)
            .OrderBy(card => card.PoolOrder)
            .ToList();
        if (candidates.Count == 0)
        {
            replacement = default;
            return false;
        }
        replacement = candidates[rng.NextInt(candidates.Count)].CardKey;
        return state.Deck!.TryTransform(source.InstanceId, replacement);
    }

    private NeowEffectProjection BuildGeneratedPotionReleaseProjection(
        ModelKey relicKey,
        PredictedEffectGroup group,
        bool fullInventoryApplicationExact,
        EvidenceCode evidence)
    {
        NeowEffectProjection exactGeneratedResult =
            NeowEffectProjection.Exact(relicKey, new[] { group }, evidence);
        if (fullInventoryApplicationExact)
        {
            return exactGeneratedResult with
            {
                DependencyImpact = EffectDependencyImpact.None(evidence)
            };
        }

        return exactGeneratedResult with
        {
            // Deliberate temporary release policy: exact generated potion identities
            // remain product-authoritative even when final inventory application is
            // not fully modeled. This does not change Potion RNG/pool consumption.
            Precision = PredictionPrecision.Partial,
            ProductRelevantProjectionPrecision = PredictionPrecision.Exact,
            ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Evaluated,
            FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Partial,
            Warnings = new[]
            {
                new PredictionWarning(PredictionWarningCode.EffectSnapshotIncomplete, relicKey, evidence)
            },
            DependencyImpact = EffectDependencyImpact.None(evidence)
        };
    }

    private EffectDependencyImpact ResolveCapsuleNestedBonesDependencyImpact(
        NeowEffectRelicSnapshot relic,
        NeowEffectProjection projection)
    {
        EffectDependencyImpact projectedImpact =
            ResolveProjectionDependencyImpact(relic.RelicKey, projection);

        if (relic.RelicKey == BaseGameModelKeys.OrdinaryRelics.Whetstone ||
            relic.RelicKey == BaseGameModelKeys.OrdinaryRelics.WarPaint)
        {
            // Whetstone / War Paint retain the existing deck-coupled path. Their
            // automatic upgrade replay may consume Niche RNG and mutate the deck,
            // so no observation-domain override is applied here.
            return projectedImpact;
        }

        // Beta111 Owner-frozen execution policy for Capsule/Gashapon -> Bones Final
        // Curse: every nested relic other than Whetstone / War Paint is exact NoImpact
        // for the Final Curse dependency domain. Full obtain semantics may still be
        // Partial/Unknown; only the unrelated blanket continuity downgrade is removed.
        EvidenceCode finalCurseNoImpactEvidence = Evidence(
            relic.RelicKey,
            "capsule-final-curse-owner-policy-non-wwp-no-impact");
        BonesContinuationDomainState exact =
            BonesContinuationDomainState.Exact(finalCurseNoImpactEvidence);
        return projectedImpact with
        {
            NicheRng = exact,
            GeneratedCursePool = exact,
            UnknownHook = exact,
            NestedObtain = exact,
            PlayerChoice = exact
        };
    }

    /// <summary>
    /// Audited vanilla opening invariant used only for the normal single-player
    /// Neow context. At A10 the player still has two potion slots. In a two-relic
    /// Bones pair, LostCoffer can precede the current relic with at most one potion;
    /// PhialHolster adds one slot before producing two potions; and both cannot
    /// precede Neow's Sacrifice in the same pair. Therefore Ambrosia, and the two
    /// PhialHolster potions, have sufficient capacity without runtime inventory
    /// counters. Modded, multiplayer, profile-mismatched, or otherwise incomplete
    /// contexts never use this proof.
    /// </summary>
    private bool CanUseVanillaSinglePlayerNeowOpeningPotionInvariant(NeowEffectWorkingState? state) =>
        state is not null &&
        state.Authority.HasExactFoundation &&
        state.Authority.CapturedProfileId == _profileId &&
        state.Authority.CurrentPotionCount is null &&
        state.Authority.PotionCapacity is null &&
        _analysisAuthority.IsVanilla &&
        _analysisAuthority.Character.IsKnownVanilla &&
        _analysisAuthority.PlayersCount == 1 &&
        _analysisAuthority.PlayerSlotIndex == 0 &&
        _analysisAuthority.NoRunModifiers == true &&
        _analysisAuthority.VanillaNeowCatalogExact == true &&
        string.IsNullOrWhiteSpace(_analysisAuthority.SourceModId) &&
        (_analysisAuthority.IsStable107Context || _analysisAuthority.IsBeta109Context);

    private static bool IsFutureRuntimeRelic(ModelKey key) =>
        key == BaseGameModelKeys.Relics.BoomingConch ||
        key == BaseGameModelKeys.Relics.FishingRod ||
        key == BaseGameModelKeys.Relics.WingedBoots ||
        key == BaseGameModelKeys.Relics.StoneHumidifier ||
        key == BaseGameModelKeys.Relics.LavaRock ||
        key == BaseGameModelKeys.Relics.SilverCrucible;

    private NeowEffectProjection Exact(ModelKey relicKey, IReadOnlyList<PredictedEffectGroup> groups) =>
        NeowEffectProjection.Exact(relicKey, groups, Evidence(relicKey, "exact"));

    private NeowEffectProjection Unknown(ModelKey relicKey, string reason) =>
        NeowEffectProjection.UnknownAuthority(relicKey, Evidence(relicKey, reason));

    private EvidenceCode Evidence(ModelKey key, string suffix) =>
        $"{_evidencePrefix}.{key.Entry.ToLowerInvariant().Replace('_', '-')}.{suffix}";

    private PredictedEffectGroup FutureGroup(ModelKey relicKey, int order, string sourceStep) => Group(
        $"future-{relicKey.Entry.ToLowerInvariant()}", order,
        EffectSelectionPolicy.NoPlayerChoice, EffectPredictionScope.FutureRuntimeEffect,
        sourceStep,
        new[]
        {
            Item(PredictedEffectKind.FutureRuntimeEffect, relicKey, null, 0,
                Evidence(relicKey, "future-runtime"), PredictionPrecision.DescriptionOnly)
        });

    private static PredictedEffectGroup Group(
        string groupId,
        int groupOrder,
        EffectSelectionPolicy policy,
        EffectPredictionScope scope,
        string sourceStep,
        IReadOnlyList<PredictedEffect> items,
        int? requiredSelectionCount = null,
        string? selectionSetId = null,
        string? bundleId = null,
        int? bundleOrder = null,
        string? rngEvidenceCode = null,
        EffectGameSelectionPolicy gameSelectionPolicy = EffectGameSelectionPolicy.Unspecified,
        EffectRouteEnumerationPolicy routeEnumerationPolicy = EffectRouteEnumerationPolicy.FollowGameSelection,
        IReadOnlyList<EvidenceCode>? policyEvidenceCodes = null,
        EffectPresentationDetailLevel normalViewDetailLevel = EffectPresentationDetailLevel.Detailed,
        EffectPresentationDetailLevel advancedViewDetailLevel = EffectPresentationDetailLevel.Detailed,
        EffectPresentationDetailLevel diagnosticViewDetailLevel = EffectPresentationDetailLevel.Detailed,
        EffectCompactSummaryKind compactSummaryKind = EffectCompactSummaryKind.None) => new(
            groupId,
            groupOrder,
            policy,
            EffectPredictionDomain.Neow,
            scope,
            sourceStep,
            items,
            requiredSelectionCount,
            selectionSetId,
            bundleId,
            bundleOrder,
            RngEvidenceCode: rngEvidenceCode,
            GameSelectionPolicy: gameSelectionPolicy,
            RouteEnumerationPolicy: routeEnumerationPolicy,
            PolicyEvidenceCodes: policyEvidenceCodes,
            NormalViewDetailLevel: normalViewDetailLevel,
            AdvancedViewDetailLevel: advancedViewDetailLevel,
            DiagnosticViewDetailLevel: diagnosticViewDetailLevel,
            CompactSummaryKind: compactSummaryKind);

    private static PredictedEffect Item(
        PredictedEffectKind kind,
        ModelKey? targetKey,
        int? amount,
        int itemOrder,
        EvidenceCode evidenceCode,
        PredictionPrecision precision = PredictionPrecision.Exact,
        IReadOnlyList<PredictionWarningCode>? warnings = null,
        int multiplicity = 1,
        ModelKey? sourceKey = null,
        string? offerItemId = null) => new(
            kind,
            targetKey,
            amount,
            precision,
            warnings ?? Array.Empty<PredictionWarningCode>(),
            evidenceCode,
            itemOrder,
            multiplicity,
            "AfterObtained",
            sourceKey,
            offerItemId);

    private IReadOnlyList<PredictedEffect> EffectsForGeneratedCard(
        GeneratedCard generated,
        string offerItemId,
        int startOrder,
        ModelKey sourceRelic)
    {
        var effects = new List<PredictedEffect>
        {
            Item(PredictedEffectKind.AddCard, generated.Card.CardKey, null, startOrder,
                Evidence(sourceRelic, "generated-card"), offerItemId: offerItemId)
        };
        if (generated.Upgraded)
        {
            effects.Add(Item(PredictedEffectKind.UpgradeCard,
                generated.Card.UpgradeTargetKey ?? generated.Card.CardKey,
                1,
                startOrder + 1,
                Evidence(sourceRelic, "generated-card-upgrade"),
                sourceKey: generated.Card.CardKey,
                offerItemId: offerItemId));
        }
        return effects;
    }

    private static void AddSyntheticCardToDeck(
        NeowShadowDeck? deck,
        ModelKey cardKey,
        string instanceId,
        EffectCardType cardType = EffectCardType.Other,
        bool isBasic = false,
        bool isStrike = false,
        bool isDefend = false,
        bool canUpgrade = false,
        EffectCardRarity rarity = EffectCardRarity.Special)
    {
        if (deck is null)
        {
            return;
        }

        int order = deck.Cards.Count == 0
            ? 0
            : deck.Cards.Max(card => card.PoolOrder) + 1;
        deck.Add(new NeowEffectCardSnapshot(
            instanceId,
            cardKey,
            order,
            rarity,
            cardType,
            IsBasic: isBasic,
            IsStrike: isStrike,
            IsDefend: isDefend,
            CanUpgrade: canUpgrade,
            CanRemove: true,
            PoolId: "neow-effect"));
    }
}
