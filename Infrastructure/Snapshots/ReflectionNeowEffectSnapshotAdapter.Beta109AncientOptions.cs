using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    private static readonly IReadOnlyDictionary<string, string> Beta109AncientRuntimeTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NEOW"] = "MegaCrit.Sts2.Core.Models.Events.Neow",
            ["DARV"] = "MegaCrit.Sts2.Core.Models.Events.Darv",
            ["OROBAS"] = "MegaCrit.Sts2.Core.Models.Events.Orobas",
            ["PAEL"] = "MegaCrit.Sts2.Core.Models.Events.Pael",
            ["TEZCATARA"] = "MegaCrit.Sts2.Core.Models.Events.Tezcatara",
            ["NONUPEIPE"] = "MegaCrit.Sts2.Core.Models.Events.Nonupeipe",
            ["TANX"] = "MegaCrit.Sts2.Core.Models.Events.Tanx",
            ["VAKUU"] = "MegaCrit.Sts2.Core.Models.Events.Vakuu"
        };

    /// <summary>
    /// Main-thread projection of the directly audited 0.109.1 Ancient option
    /// definitions into immutable DTOs. This method never instantiates an Event,
    /// invokes a Hook, executes SetupForPlayer, or advances a live game RNG.
    /// Route-dependent predicates are represented as an explicit opening-state
    /// projection and therefore remain Partial until route-state authority exists.
    /// </summary>
    private static IReadOnlyList<Beta109AncientEventContextSnapshot> BuildBeta109AncientEventContexts(
        RuntimeProfileId profileId,
        Beta109StaticAuthorityCapture authority,
        NeowEffectAuthoritySnapshot? effects,
        IReadOnlyList<ModelKey> runtimeRelicCatalog,
        bool runtimeRelicCatalogExact,
        ulong runSeedRoot,
        bool runSeedRootExact,
        CharacterIdentity character,
        int playerSlotIndex,
        bool? noRunModifiers,
        IReadOnlyList<ModelKey> unlockedCharacters,
        bool unlockedCharactersExact)
    {
        var output = new List<Beta109AncientEventContextSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Beta109ActGenerationSnapshot act in authority.OrderedActs.OrderBy(item => item.Act))
        {
            foreach (ModelKey ancient in act.OrderedAncients)
            {
                AddContext(act.Act, ancient, fromSharedAncientAllocation: false);
            }

            // UnlockState.SharedAncients describes the UpFront candidate-allocation pool.
            // It is NOT EventModel.IsShared. Vanilla Beta109/Beta110 Ancient EventModels
            // audited below retain EventModel.IsShared == false, so their event-local RNG
            // still includes the player slot even when identity came from SharedAncients.
            foreach (ModelKey ancient in authority.UnlockedSharedAncients)
            {
                AddContext(act.Act, ancient, fromSharedAncientAllocation: true);
            }
        }

        return output;

        void AddContext(int act, ModelKey ancient, bool fromSharedAncientAllocation)
        {
            if (!ancient.IsValid || string.IsNullOrWhiteSpace(ancient.Entry)) return;
            string normalized = NormalizeAncientEntry(ancient.Entry);
            bool eventIsSharedAuthorityExact = TryResolveAuditedVanillaAncientEventIsShared(
                profileId, ancient, out bool eventIsShared, out string eventIsSharedEvidence);
            string identity = $"{act}:{ancient.Serialized}:{fromSharedAncientAllocation}:{eventIsShared}:{playerSlotIndex}";
            if (!seen.Add(identity)) return;

            Beta109AncientOptionCatalogSnapshot? catalog = BuildBeta109AncientOptionCatalog(
                profileId,
                ancient,
                act,
                effects,
                runtimeRelicCatalog,
                runtimeRelicCatalogExact,
                out bool dynamicFactsExact,
                out string projectionEvidence);

            bool modifierFactsExact = noRunModifiers == true;
            Beta109HookDecision hookDecision = modifierFactsExact && authority.IsVanilla
                ? Beta109HookDecision.Allow
                : Beta109HookDecision.Unknown;
            bool eventContextExact = runSeedRootExact &&
                                     eventIsSharedAuthorityExact &&
                                     character.IsValid &&
                                     catalog is { CatalogExact: true } &&
                                     modifierFactsExact;
            int currentActIndex = Math.Max(0, act - 1);
            ulong eventRoot = runSeedRootExact && eventIsSharedAuthorityExact
                ? Beta109WorldRng.DeriveEventLocalSeed(runSeedRoot, playerSlotIndex, eventIsShared, ancient.Entry)
                : 0UL;
            Beta109DeckFactSnapshot deckFacts = CaptureBeta109AncientDeckFacts(effects);
            Dictionary<string, bool> booleanFacts = BuildBeta109AncientBooleanFacts(
                normalized,
                deckFacts,
                modifierFactsExact);
            Dictionary<string, int> integerFacts = new(StringComparer.Ordinal)
            {
                ["opening.basicStrikeCount"] = deckFacts.BasicStrikeCount,
                ["opening.removableCardCount"] = deckFacts.RemovableCardCount,
                ["opening.goopyEnchantableCount"] = deckFacts.GoopyEnchantableCount,
                ["opening.swiftEnchantableCount"] = deckFacts.SwiftEnchantableCount,
                ["opening.instinctEnchantableCount"] = deckFacts.InstinctEnchantableCount
            };
            string runtimeType = Beta109AncientRuntimeTypes.TryGetValue(normalized, out string? typeName)
                ? typeName
                : string.Empty;
            string contextFingerprint = Fingerprint(
                RuntimeProfilePolicies.CatalogFingerprintPrefix(profileId) + "-ancient-option-context-v3",
                new[]
            {
                RuntimeProfilePolicies.AuthorityId(profileId),
                RuntimeProfilePolicies.AuditFingerprint(profileId),
                act.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ancient.Serialized,
                fromSharedAncientAllocation.ToString(),
                eventIsShared.ToString(),
                eventIsSharedAuthorityExact.ToString(),
                eventIsSharedEvidence,
                currentActIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                character.CharacterKey.Serialized,
                string.Join(",", unlockedCharacters.Select(key => key.Serialized)),
                unlockedCharactersExact.ToString(),
                deckFacts.BasicStrikeCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                deckFacts.RemovableCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                deckFacts.Exact.ToString(),
                modifierFactsExact.ToString(),
                dynamicFactsExact.ToString(),
                hookDecision.ToString(),
                catalog?.CatalogFingerprint ?? "missing-catalog",
                eventRoot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                projectionEvidence
            });

            output.Add(new Beta109AncientEventContextSnapshot(
                Act: act,
                AncientKey: ancient,
                EventIdEntry: ancient.Entry,
                PlayerSlot: playerSlotIndex,
                IsShared: eventIsShared,
                CurrentActIndex: currentActIndex,
                CharacterKey: character.CharacterKey,
                UnlockedCharacters: unlockedCharacters.ToArray(),
                DeckFacts: deckFacts,
                BooleanFacts: booleanFacts,
                IntegerFacts: integerFacts,
                HookDecision: hookDecision,
                EventContextExact: eventContextExact,
                DynamicFactsExact: dynamicFactsExact,
                ModifierFactsExact: modifierFactsExact,
                Catalog: catalog,
                ContextFingerprint: contextFingerprint)
            {
                RuntimeTypeName = runtimeType,
                UnlockedCharacterSourceOrderExact = unlockedCharactersExact,
                EventRngRoot = eventRoot,
                EventRngRootExact = runSeedRootExact && eventIsSharedAuthorityExact,
                CandidateFromSharedAncientAllocation = fromSharedAncientAllocation,
                EventIsSharedAuthorityExact = eventIsSharedAuthorityExact,
                EventIsSharedAuthorityEvidence = eventIsSharedEvidence,
                EventRngFormulaVersion = "EventModel.BeginEvent.0.109.1",
                ProjectionPolicy = dynamicFactsExact
                    ? "StaticIdentityAndAppearanceAuthority"
                    : "OpeningStateProjectionPartial",
                AuthorityEvidenceCode = projectionEvidence
            });
        }
    }

    private static bool TryResolveAuditedVanillaAncientEventIsShared(
        RuntimeProfileId profileId,
        ModelKey ancient,
        out bool isShared,
        out string evidence)
    {
        isShared = false;
        evidence = string.Empty;
        if (!ancient.IsValid || string.IsNullOrWhiteSpace(ancient.Entry))
            return false;

        string normalized = NormalizeAncientEntry(ancient.Entry);
        if (!Beta109AncientRuntimeTypes.ContainsKey(normalized))
            return false;

        // Source authority: EventModel.IsShared defaults false and the audited vanilla
        // Ancient classes do not override it. SharedAncients is an UpFront allocation
        // catalog only; it does not select the multiplayer shared-event RNG formula.
        if (RuntimeProfilePolicies.IsModernCore(profileId))
        {
            evidence = "SourceAudit:EventModel.IsShared=false;VanillaAncientNoOverride;SharedAncientAllocationSeparate";
            return true;
        }

        return false;
    }

    private static Beta109DeckFactSnapshot CaptureBeta109AncientDeckFacts(
        NeowEffectAuthoritySnapshot? effects)
    {
        IReadOnlyList<NeowEffectCardSnapshot> deck = effects?.OrderedDeck ?? Array.Empty<NeowEffectCardSnapshot>();
        bool deckExact = effects?.HasExactDeck == true;
        int basicStrike = deck.Count(card => card.IsBasic && card.IsStrike);
        int removable = deck.Count(card => card.CanRemove);

        // The existing effect snapshot intentionally does not expose Goopy/Swift/
        // Instinct enchantability. Do not guess these values from CardType or names.
        return new Beta109DeckFactSnapshot(
            BasicStrikeCount: basicStrike,
            RemovableCardCount: removable,
            GoopyEnchantableCount: 0,
            SwiftEnchantableCount: 0,
            InstinctEnchantableCount: 0,
            Exact: deckExact);
    }

    private static Dictionary<string, bool> BuildBeta109AncientBooleanFacts(
        string ancient,
        Beta109DeckFactSnapshot deck,
        bool modifierFactsExact)
    {
        var facts = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["projection.openingDeck"] = true,
            ["modifier.deckCleared"] = false,
            ["modifier.factsExact"] = modifierFactsExact,
            ["pael.goopyAtLeast3"] = deck.GoopyEnchantableCount >= 3,
            ["pael.removableAtLeast5"] = deck.RemovableCardCount >= 5,
            ["pael.noEventPet"] = true,
            ["tezcatara.basicStrikePresent"] = deck.BasicStrikeCount > 0,
            ["nonupeipe.swiftAtLeast4"] = deck.SwiftEnchantableCount >= 4,
            ["tanx.instinctAtLeast3"] = deck.InstinctEnchantableCount >= 3,
            ["orobas.touchAllowed"] = true,
            ["orobas.toothAllowed"] = true,
            ["vakuu.willKillPlayerKnown"] = false
        };
        facts["projection.routeStateExact"] = ancient is "DARV" or "VAKUU";
        return facts;
    }

    private static Beta109AncientOptionCatalogSnapshot? BuildBeta109AncientOptionCatalog(
        RuntimeProfileId profileId,
        ModelKey ancient,
        int act,
        NeowEffectAuthoritySnapshot? effects,
        IReadOnlyList<ModelKey> runtimeRelicCatalog,
        bool runtimeRelicCatalogExact,
        out bool dynamicFactsExact,
        out string evidence)
    {
        string entry = NormalizeAncientEntry(ancient.Entry);
        dynamicFactsExact = false;
        evidence = profileId switch
        {
            RuntimeProfileId.Beta111 => "Beta111AncientOptionSemanticCompatible",
            RuntimeProfileId.Beta110 => "Beta110AncientOptionSourceAuditedPendingRuntimeValidation",
            _ => "Beta109AncientOptionHistoricalDonorAudited"
        };

        IReadOnlyList<Beta109NamedOptionPoolSnapshot> pools;
        Beta109AncientOptionSupport capability;
        bool mutableProjectionExact = true;
        var variantData = new Dictionary<string, string>(StringComparer.Ordinal);

        switch (entry)
        {
            case "NEOW":
            {
                capability = Beta109AncientOptionSupport.PureBranch;
                string[] curses =
                {
                    "CURSED_PEARL", "DOWSING_ROD", "HEFTY_TABLET", "LARGE_CAPSULE",
                    "LEAFY_POULTICE", "NEOWS_BONES", "NEOWS_SACRIFICE",
                    "PRECARIOUS_SHEARS", "SILKEN_TRESS", "SILVER_CRUCIBLE"
                };
                string[] positives = BaseGameModelKeys.Relics.AllNeow
                    .Select(key => key.Entry)
                    .Where(item => !curses.Contains(item, StringComparer.Ordinal))
                    .ToArray();
                string[] pairEntries =
                {
                    "LAVA_ROCK", "SMALL_CAPSULE", "NUTRITIOUS_OYSTER",
                    "STONE_HUMIDIFIER", "NEOWS_TALISMAN", "POMANDER"
                };
                string[] positiveBase = positives
                    .Where(item => !pairEntries.Contains(item, StringComparer.Ordinal))
                    .ToArray();
                pools = new[]
                {
                    Pool("neow.curse", 0, curses),
                    Pool("neow.positive.base", 1, positiveBase),
                    Pool("neow.pair.capsule", 2, "LAVA_ROCK", "SMALL_CAPSULE"),
                    Pool("neow.pair.oyster", 3, "NUTRITIOUS_OYSTER", "STONE_HUMIDIFIER"),
                    Pool("neow.pair.talisman", 4, "NEOWS_TALISMAN", "POMANDER"),
                    Pool("neow.allowed-positive", 5, positives)
                };
                dynamicFactsExact = false;
                evidence += ":NeowModernPoolDeltaCaptured";
                break;
            }
            case "DARV":
            {
                capability = Beta109AncientOptionSupport.PureInstanceProjection;
                var darv = new List<Beta109NamedOptionPoolSnapshot>
                {
                    Pool("darv.valid.astrolabe", 0, "ASTROLABE"),
                    Pool("darv.valid.black-star", 1, "BLACK_STAR"),
                    Pool("darv.valid.calling-bell", 2, "CALLING_BELL"),
                    Pool("darv.valid.empty-cage", 3, "EMPTY_CAGE"),
                    Pool("darv.valid.pandoras-box", 4, "PANDORAS_BOX") with
                    {
                        FilterPredicateId = "user.darv.allowPandorasBoxSet",
                        FilterResultExact = true,
                        IsDynamicProjection = true
                    },
                    Pool("darv.valid.runic-pyramid", 5, "RUNIC_PYRAMID"),
                    Pool("darv.valid.snecko-eye", 6, "SNECKO_EYE")
                };
                int currentActIndex = Math.Max(0, act - 1);
                if (currentActIndex == 1)
                {
                    darv.Add(Pool("darv.valid.ectoplasm", 7, "ECTOPLASM") with
                    {
                        FilterPredicateId = "CurrentActIndex==1",
                        FilterResultExact = true
                    });
                    darv.Add(Pool("darv.valid.sozu", 8, "SOZU") with
                    {
                        FilterPredicateId = "CurrentActIndex==1",
                        FilterResultExact = true
                    });
                }
                if (currentActIndex >= 1)
                {
                    darv.Add(Pool("darv.valid.philosophers-stone", 9, "PHILOSOPHERS_STONE") with
                    {
                        FilterPredicateId = "CurrentActIndex>=1",
                        FilterResultExact = true
                    });
                    darv.Add(Pool("darv.valid.velvet-choker", 10, "VELVET_CHOKER") with
                    {
                        FilterPredicateId = "CurrentActIndex>=1",
                        FilterResultExact = true
                    });
                }
                darv.Add(Pool("darv.dusty-tome", 11, "DUSTY_TOME") with
                {
                    IsDynamicProjection = true,
                    InstanceProjectionExact = false
                });
                pools = darv;
                mutableProjectionExact = false;
                dynamicFactsExact = true;
                variantData["darv.dusty-tome.variant"] = "PlayerRng.RewardsProjectionPending";
                variantData["darv.validSetCount"] = darv.Count(pool => pool.PoolId.StartsWith("darv.valid.", StringComparison.Ordinal))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                evidence += ":Darv11SetReimplementation";
                break;
            }
            case "OROBAS":
                capability = Beta109AncientOptionSupport.PureInstanceProjection;
                pools = new[]
                {
                    Pool("orobas.pool1.true", 0, "ELECTRIC_SHRYMP", "GLASS_EYE", "PRISMATIC_GEM") with { IsDynamicProjection = true },
                    Pool("orobas.pool1.false", 1, "ELECTRIC_SHRYMP", "GLASS_EYE", "SEA_GLASS") with { IsDynamicProjection = true },
                    Pool("orobas.pool2", 2, "ALCHEMICAL_COFFER", "DRIFTWOOD", "RADIANT_PEARL", "SAND_CASTLE"),
                    Pool("orobas.pool3.touch", 3, "TOUCH_OF_OROBAS") with
                    {
                        FilterPredicateId = "user.orobas.touchAllowed",
                        IsDynamicProjection = true,
                        InstanceProjectionExact = false
                    },
                    Pool("orobas.pool3.tooth", 4, "ARCHAIC_TOOTH") with
                    {
                        FilterPredicateId = "user.orobas.toothAllowed",
                        IsDynamicProjection = true,
                        InstanceProjectionExact = false
                    }
                };
                mutableProjectionExact = false;
                evidence += ":OrobasModernPoolOrderCaptured";
                break;
            case "PAEL":
            {
                capability = Beta109AncientOptionSupport.DeckFactPredicate;
                pools = new[]
                {
                    Pool("pael.pool1", 0, "PAELS_FLESH", "PAELS_HORN", "PAELS_TEARS"),
                    Pool("pael.pool2.base", 1, "PAELS_WING"),
                    Pool("pael.pool2.goopy", 2, "PAELS_CLAW") with
                    {
                        FilterPredicateId = "user.pael.goopyAtLeast3",
                        IsDynamicProjection = true
                    },
                    Pool("pael.pool2.removable", 3, "PAELS_TOOTH") with
                    {
                        FilterPredicateId = "user.pael.removableAtLeast5",
                        IsDynamicProjection = true
                    },
                    Pool("pael.pool2.growth", 4, "PAELS_GROWTH"),
                    Pool("pael.pool3.base", 5, "PAELS_EYE", "PAELS_BLOOD"),
                    Pool("pael.pool3.no-event-pet", 6, "PAELS_LEGION") with
                    {
                        FilterPredicateId = "user.pael.noEventPet",
                        IsDynamicProjection = true
                    }
                };
                evidence += ":SimpleConditionCatalogCaptured";
                break;
            }
            case "TEZCATARA":
                capability = Beta109AncientOptionSupport.DeckFactPredicate;
                pools = new[]
                {
                    Pool("tezcatara.pool1.base", 0, "VERY_HOT_COCOA", "YUMMY_COOKIE"),
                    Pool("tezcatara.pool1.basic-strike", 1, "NUTRITIOUS_SOUP") with
                    {
                        FilterPredicateId = "user.tezcatara.basicStrikePresent",
                        IsDynamicProjection = true
                    },
                    Pool("tezcatara.pool2", 2, "BIIIG_HUG", "STORYBOOK", "TOASTY_MITTENS"),
                    Pool("tezcatara.pool3", 3, "GOLDEN_COMPASS", "PUMPKIN_CANDLE", "TOY_BOX", "SEAL_OF_GOLD")
                };
                evidence += ":SimpleConditionCatalogCaptured";
                break;
            case "NONUPEIPE":
                capability = Beta109AncientOptionSupport.DeckFactPredicate;
                pools = new[]
                {
                    Pool("nonupeipe.pool.base", 0,
                        "BLESSED_ANTLER", "BRILLIANT_SCARF", "DELICATE_FROND", "DIAMOND_DIADEM",
                        "FUR_COAT", "GLITTER", "JEWELRY_BOX", "LOOMING_FRUIT", "SIGNET_RING"),
                    Pool("nonupeipe.pool.swift", 1, "BEAUTIFUL_BRACELET") with
                    {
                        FilterPredicateId = "user.nonupeipe.swiftAtLeast4",
                        IsDynamicProjection = true
                    }
                };
                evidence += ":SimpleConditionCatalogCaptured";
                break;
            case "TANX":
                capability = Beta109AncientOptionSupport.DeckFactPredicate;
                pools = new[]
                {
                    Pool("tanx.pool.base", 0,
                        "CLAWS", "CROSSBOW", "IRON_CLUB", "MEAT_CLEAVER", "SAI",
                        "SPIKED_GAUNTLETS", "TANXS_WHISTLE", "THROWING_AXE", "WAR_HAMMER"),
                    Pool("tanx.pool.instinct", 1, "TRI_BOOMERANG") with
                    {
                        FilterPredicateId = "user.tanx.instinctAtLeast3",
                        IsDynamicProjection = true
                    }
                };
                evidence += ":SimpleConditionCatalogCaptured";
                break;
            case "VAKUU":
                capability = Beta109AncientOptionSupport.PureShuffleTake;
                pools = new[]
                {
                    Pool("vakuu.pool1", 0, "BLOOD_SOAKED_ROSE", "WHISPERING_EARRING", "FIDDLE"),
                    Pool("vakuu.pool2", 1, "PRESERVED_FOG", "SERE_TALON", "DISTINGUISHED_CAPE"),
                    Pool("vakuu.pool3", 2, "CHOICES_PARADOX", "MUSIC_BOX", "LORDS_PARASOL", "JEWELED_MASK")
                };
                dynamicFactsExact = true;
                variantData["vakuu.warningRelic"] = "SERE_TALON";
                evidence += ":VakuuModernWarningDeltaCaptured";
                break;
            default:
                return null;
        }

        bool allRuntimeKeysExact = true;
        var reboundPools = new List<Beta109NamedOptionPoolSnapshot>(pools.Count);
        foreach (Beta109NamedOptionPoolSnapshot pool in pools.OrderBy(item => item.SourceOrdinal))
        {
            var rebound = new List<ModelKey>(pool.OrderedOptions.Count);
            bool poolKeysExact = true;
            foreach (ModelKey key in pool.OrderedOptions)
            {
                ModelKey bound = BindBeta109AncientOptionRelic(
                    key.Entry,
                    runtimeRelicCatalog,
                    runtimeRelicCatalogExact,
                    out bool keyExact);
                rebound.Add(bound);
                poolKeysExact &= keyExact;
            }
            allRuntimeKeysExact &= poolKeysExact;
            reboundPools.Add(pool with
            {
                OrderedOptions = rebound.ToArray(),
                OrderExact = pool.OrderExact && poolKeysExact
            });
        }

        string fingerprint = Fingerprint(
            RuntimeProfilePolicies.CatalogFingerprintPrefix(profileId) + "-ancient-option-catalog-v3",
            new[]
            {
                RuntimeProfilePolicies.AuthorityId(profileId),
                RuntimeProfilePolicies.AuditFingerprint(profileId),
                ancient.Serialized,
                entry,
                capability.ToString(),
                mutableProjectionExact.ToString(),
                allRuntimeKeysExact.ToString(),
                evidence
            }.Concat(reboundPools.Select(pool =>
                $"{pool.SourceOrdinal}:{pool.PoolId}:{pool.OrderExact}:{pool.InstanceProjectionExact}:" +
                string.Join(",", pool.OrderedOptions.Select(key => key.Serialized)))));

        return new Beta109AncientOptionCatalogSnapshot(
            AncientKey: ancient,
            EventIdEntry: ancient.Entry,
            Capability: capability,
            Pools: reboundPools,
            VariantData: variantData,
            CatalogExact: runtimeRelicCatalogExact && allRuntimeKeysExact,
            MutableInstanceProjectionExact: mutableProjectionExact,
            CatalogFingerprint: fingerprint);

        Beta109NamedOptionPoolSnapshot Pool(string id, int ordinal, params string[] optionEntries) => new(
            PoolId: id,
            OrderedOptions: optionEntries.Select(value => new ModelKey(BaseGameModelKeys.Categories.Relic, value)).ToArray(),
            OrderExact: true,
            InstanceProjectionExact: true)
        {
            SourceOrdinal = ordinal,
            FilterResultExact = true
        };
    }

    private static ModelKey BindBeta109AncientOptionRelic(
        string entry,
        IReadOnlyList<ModelKey> runtimeRelicCatalog,
        bool runtimeRelicCatalogExact,
        out bool exact)
    {
        ModelKey fallback = new(BaseGameModelKeys.Categories.Relic, entry);
        if (!runtimeRelicCatalogExact)
        {
            exact = false;
            return fallback;
        }

        string token = NormalizeAncientEntry(entry);
        ModelKey[] matches = runtimeRelicCatalog
            .Where(key => string.Equals(key.Category, BaseGameModelKeys.Categories.Relic, StringComparison.Ordinal) &&
                          string.Equals(NormalizeAncientEntry(key.Entry), token, StringComparison.Ordinal))
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        exact = matches.Length == 1;
        return exact ? matches[0] : fallback;
    }

    private static string NormalizeAncientEntry(string value) =>
        new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
}
