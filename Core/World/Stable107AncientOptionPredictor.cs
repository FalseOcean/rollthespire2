using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.World;

/// <summary>
/// Stable107 Ancient option identity predictor. The g10ab5 broad/custom preset
/// predicates are supplied by AncientOptionConditionProfile and are shared with
/// Beta109 Analysis/Search. No live RunState is read or mutated.
/// </summary>
internal static class Stable107AncientOptionPredictor
{
    public static (IReadOnlyList<AncientOptionPredictionResult> Options, PredictionPrecision Precision, int RngCalls, EvidenceCode Evidence)
        Predict(
            string canonicalSeed,
            int act,
            ModelKey ancientKey,
            ModelKey characterKey,
            int playerSlotIndex,
            WorldAuthoritySnapshot world,
            AncientOptionConditionProfile ancientOptionConditions)
    {
        if (!ancientKey.IsValid)
        {
            return Unknown("batch-a.stable107.ancient-option.invalid-identity");
        }

        if (string.Equals(ancientKey.Entry, "NEOW", StringComparison.Ordinal))
        {
            return (Array.Empty<AncientOptionPredictionResult>(), PredictionPrecision.Exact, 0,
                "batch-a.stable107.ancient-option.neow-opening-covered-by-neow-domain");
        }

        ulong root = Stable107Profile.Instance.ComputeRootSeed(canonicalSeed);
        int slot = string.Equals(ancientKey.Entry, "DARV", StringComparison.Ordinal) ? 0 : playerSlotIndex;
        ulong eventSeed = Stable107Profile.Instance.DeriveEventStreamSeed(root, slot, ancientKey.Entry);
        const string directEvidence = "batch-a.stable107.ancient-option.direct-donor-algorithm";

        var raw = ancientKey.Entry switch
        {
            "PAEL" => ResolveConsensus(
                new[] { Run(eventSeed, (options, rng) => PredictPael(
                    options,
                    rng,
                    ancientOptionConditions.PaelGoopyDefendCardsAtLeast3,
                    ancientOptionConditions.PaelRemovableCardsAtLeast5,
                    ancientOptionConditions.PaelAllowLegionNoEventPet)) },
                directEvidence + ".user-assumption-pael"),
            "OROBAS" => ResolveConsensus(
                new[] { Run(eventSeed, (options, rng) => PredictOrobas(
                    options,
                    rng,
                    characterKey,
                    world.UnlockedCharacters ?? Array.Empty<ModelKey>(),
                    world.UnlockedCharactersExact,
                    ancientOptionConditions.OrobasTouchOfOrobasConditionMet,
                    ancientOptionConditions.OrobasArchaicToothConditionMet)) },
                directEvidence + ".orobas-user-assumption"),
            "TEZCATARA" => ResolveConsensus(
                new[] { Run(eventSeed, (options, rng) =>
                    PredictTezcatara(options, rng, ancientOptionConditions.TezcataraHasBasicStrike)) },
                directEvidence + ".user-assumption-basic-strike"),
            "NONUPEIPE" => ResolveConsensus(
                new[] { Run(eventSeed, (options, rng) =>
                    PredictNonupeipe(options, rng, ancientOptionConditions.NonupeipeSwiftEnchantableAtLeast4)) },
                directEvidence + ".user-assumption-swift"),
            "TANX" => ResolveConsensus(
                new[] { Run(eventSeed, (options, rng) =>
                    PredictTanx(options, rng, ancientOptionConditions.TanxInstinctEnchantableAtLeast3)) },
                directEvidence + ".user-assumption-instinct"),
            "VAKUU" => ResolveConsensus(
                new[] { Run(eventSeed, PredictVakuu) },
                directEvidence + ".vakuu"),
            "DARV" => ResolveConsensus(
                new[] { Run(eventSeed, (output, rng) => PredictDarv(
                    output,
                    rng,
                    act,
                    ancientOptionConditions.DarvAllowPandorasBoxRelicSet)) },
                directEvidence + ".user-assumption-darv"),
            _ => (Array.Empty<AncientOptionPredictionResult>(), PredictionPrecision.Unsupported, 0,
                "batch-a.stable107.ancient-option.identity-not-implemented")
        };
        return BindRuntimeModelKeys(raw, world.AncientOptionRelicCatalog, world.AncientOptionRelicCatalogExact);
    }

    private static (IReadOnlyList<AncientOptionPredictionResult> Options, PredictionPrecision Precision, int RngCalls, EvidenceCode Evidence)
        BindRuntimeModelKeys(
            (IReadOnlyList<AncientOptionPredictionResult> Options, PredictionPrecision Precision, int RngCalls, EvidenceCode Evidence) raw,
            IReadOnlyList<ModelKey>? runtimeRelics,
            bool runtimeCatalogExact)
    {
        if (raw.Precision != PredictionPrecision.Exact || raw.Options.Count == 0)
        {
            return raw;
        }
        if (!runtimeCatalogExact || runtimeRelics is null || runtimeRelics.Count == 0)
        {
            return Unknown(raw.Evidence + ".runtime-model-catalog-missing-or-inexact");
        }

        var bound = new List<AncientOptionPredictionResult>(raw.Options.Count);
        foreach (AncientOptionPredictionResult option in raw.Options)
        {
            string token = NormalizeIdentityToken(option.OptionKey.Entry);
            ModelKey[] matches = runtimeRelics
                .Where(key => key.Category == BaseGameModelKeys.Categories.Relic &&
                              string.Equals(NormalizeIdentityToken(key.Entry), token, StringComparison.Ordinal))
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            if (matches.Length != 1)
            {
                return Unknown(raw.Evidence + ".runtime-model-key-not-unique:" + token);
            }
            bound.Add(option with { OptionKey = matches[0] });
        }
        return (bound, PredictionPrecision.Exact, raw.RngCalls, raw.Evidence + ".runtime-model-key-bound");
    }

    private static string NormalizeIdentityToken(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static IReadOnlyList<OptionScenario> BuildPaelScenarios(ulong eventSeed)
    {
        // Ancient options are generated later in the run. The opening deck snapshot cannot
        // prove later removable-card counts, enchantability, or event-pet state. Replay all
        // legal boolean states and emit only a seed-objective consensus.
        IReadOnlyList<bool> goopyValues = Both;
        IReadOnlyList<bool> removableValues = Both;
        IReadOnlyList<bool> legionValues = Both;
        var output = new List<OptionScenario>();
        foreach (bool goopy in goopyValues)
        foreach (bool removable in removableValues)
        foreach (bool legion in legionValues)
        {
            output.Add(Run(eventSeed, (options, rng) =>
                PredictPael(options, rng, goopy, removable, legion)));
        }
        return output;
    }

    private static IReadOnlyList<OptionScenario> BuildOrobasScenarios(
        ulong eventSeed,
        ModelKey characterKey,
        IReadOnlyList<ModelKey>? unlockedCharacters,
        bool unlockedCharacterSourceOrderExact)
    {
        // TouchOfOrobas and ArchaicTooth eligibility depend on state outside the
        // current immutable authority. Replay all four pools and emit only consensus.
        var output = new List<OptionScenario>();
        foreach (bool touch in Both)
        foreach (bool tooth in Both)
        {
            output.Add(Run(eventSeed, (options, rng) =>
                PredictOrobas(
                    options,
                    rng,
                    characterKey,
                    unlockedCharacters ?? Array.Empty<ModelKey>(),
                    unlockedCharacterSourceOrderExact,
                    touch,
                    tooth)));
        }
        return output;
    }

    private static IReadOnlyList<OptionScenario> BuildTezcataraScenarios(ulong eventSeed) =>
        Both.Select(value => Run(eventSeed, (options, rng) => PredictTezcatara(options, rng, value))).ToArray();

    private static IReadOnlyList<OptionScenario> BooleanScenarios(
        ulong eventSeed,
        Action<List<AncientOptionPredictionResult>, Xoshiro256StarStar, bool> generator) =>
        Both.Select(value => Run(eventSeed, (options, rng) => generator(options, rng, value))).ToArray();

    private static OptionScenario Run(
        ulong eventSeed,
        Action<List<AncientOptionPredictionResult>, Xoshiro256StarStar> generator)
    {
        var rng = new Xoshiro256StarStar(eventSeed);
        var options = new List<AncientOptionPredictionResult>();
        generator(options, rng);
        return new OptionScenario(options, rng.CallCount);
    }

    private static (IReadOnlyList<AncientOptionPredictionResult> Options, PredictionPrecision Precision, int RngCalls, EvidenceCode Evidence)
        ResolveConsensus(IReadOnlyList<OptionScenario> scenarios, EvidenceCode exactEvidence)
    {
        if (scenarios.Count == 0)
        {
            return Unknown(exactEvidence + ".no-scenario");
        }

        OptionScenario first = scenarios[0];
        bool exact = scenarios.All(candidate =>
            candidate.RngCalls == first.RngCalls &&
            SequenceEquals(candidate.Options, first.Options));
        if (!exact)
        {
            return Unknown(exactEvidence + ".ambiguous-fail-closed");
        }

        AncientOptionPredictionResult[] options = first.Options
            .Select(option => option with { OptionPrecision = PredictionPrecision.Exact })
            .ToArray();
        return (options, PredictionPrecision.Exact, first.RngCalls, exactEvidence);
    }

    private static bool SequenceEquals(
        IReadOnlyList<AncientOptionPredictionResult> left,
        IReadOnlyList<AncientOptionPredictionResult> right)
    {
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index++)
        {
            AncientOptionPredictionResult a = left[index];
            AncientOptionPredictionResult b = right[index];
            if (a.Ordinal != b.Ordinal || a.OptionKey != b.OptionKey ||
                !string.Equals(a.VariantId, b.VariantId, StringComparison.Ordinal) ||
                !CharacterTargetEquals(a.CharacterTarget, b.CharacterTarget))
            {
                return false;
            }
        }
        return true;
    }

    private static bool CharacterTargetEquals(
        AncientOptionCharacterTargetProjection? left,
        AncientOptionCharacterTargetProjection? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.CharacterKey == right.CharacterKey &&
               left.Precision == right.Precision &&
               left.EvidenceCode == right.EvidenceCode &&
               string.Equals(left.IssueCode, right.IssueCode, StringComparison.Ordinal);
    }

    private static (IReadOnlyList<AncientOptionPredictionResult>, PredictionPrecision, int, EvidenceCode)
        Unknown(EvidenceCode evidence) =>
        (Array.Empty<AncientOptionPredictionResult>(), PredictionPrecision.Unknown, 0, evidence);


    private static IReadOnlyList<bool> Both { get; } = new[] { false, true };

    private static void PredictPael(
        List<AncientOptionPredictionResult> output,
        Xoshiro256StarStar rng,
        bool goopyDefendAtLeastThree,
        bool removableAtLeastFive,
        bool allowLegionNoEventPet)
    {
        Add(output, Next(new[] { "PAELS_FLESH", "PAELS_HORN", "PAELS_TEARS" }, rng), "pael.pool1");

        var pool2 = new List<string> { "PAELS_WING" };
        if (goopyDefendAtLeastThree) pool2.Add("PAELS_CLAW");
        if (removableAtLeastFive) pool2.Add("PAELS_TOOTH");
        pool2.AddRange(pool2.ToArray());
        pool2.Add("PAELS_GROWTH");
        Add(output, Next(pool2, rng), "pael.pool2-weighted");

        var pool3 = new List<string> { "PAELS_EYE", "PAELS_BLOOD" };
        if (allowLegionNoEventPet) pool3.Add("PAELS_LEGION");
        Add(output, Next(pool3, rng), "pael.pool3");
    }

    private static void PredictOrobas(
        List<AncientOptionPredictionResult> output,
        Xoshiro256StarStar rng,
        ModelKey characterKey,
        IReadOnlyList<ModelKey> unlockedCharacters,
        bool unlockedCharacterSourceOrderExact,
        bool touchCondition,
        bool toothCondition)
    {
        List<ModelKey> otherCharacters = unlockedCharacters
            .Where(key =>
                key.IsValid &&
                key.Category == BaseGameModelKeys.Categories.Character &&
                key != characterKey)
            .ToList();

        ModelKey? selectedCharacter = null;
        AncientOptionCharacterTargetProjection targetProjection;
        if (otherCharacters.Count > 0)
        {
            selectedCharacter = otherCharacters[rng.NextInt(otherCharacters.Count)];
            targetProjection = unlockedCharacterSourceOrderExact
                ? AncientOptionCharacterTargetProjection.Exact(
                    selectedCharacter.Value,
                    "stable107.orobas.sea-glass-target.runtime-unlocked-source-order")
                : AncientOptionCharacterTargetProjection.Unknown(
                    "stable107.orobas.sea-glass-target.target-only-unknown",
                    "MissingUnlockedCharacterSourceOrderAuthority");
        }
        else
        {
            targetProjection = AncientOptionCharacterTargetProjection.Unknown(
                "stable107.orobas.sea-glass-target.no-other-character-candidate",
                "NoOtherUnlockedCharacterCandidate");
        }
        string dynamicOption = (float)rng.NextDouble() < (1.0f / 3.0f) ? "PRISMATIC_GEM" : "SEA_GLASS";
        string first = Next(new[] { "ELECTRIC_SHRYMP", "GLASS_EYE", "SAND_CASTLE", dynamicOption }, rng);
        bool seaGlassSelected = string.Equals(first, "SEA_GLASS", StringComparison.Ordinal);
        string? variant = seaGlassSelected && selectedCharacter.HasValue && unlockedCharacterSourceOrderExact
            ? "character=" + selectedCharacter.Value.Serialized
            : null;
        Add(
            output,
            first,
            "orobas.pool1",
            variant,
            seaGlassSelected ? targetProjection : null);
        Add(output, Next(new[] { "ALCHEMICAL_COFFER", "DRIFTWOOD", "RADIANT_PEARL" }, rng), "orobas.pool2");

        var pool3 = new List<string>();
        if (touchCondition) pool3.Add("TOUCH_OF_OROBAS");
        if (toothCondition) pool3.Add("ARCHAIC_TOOTH");
        if (pool3.Count == 0)
        {
            // Donor used a display-only locked fallback. Preserve it only inside the
            // scenario comparison; ambiguity prevents it from escaping as a game ModelKey.
            Add(output, "LOCKED_OROBAS_OPTION", "orobas.pool3-locked-sentinel");
        }
        else
        {
            Add(output, Next(pool3, rng), "orobas.pool3");
        }
    }

    private static void PredictTezcatara(
        List<AncientOptionPredictionResult> output,
        Xoshiro256StarStar rng,
        bool hasBasicStrike)
    {
        var pool1 = new List<string> { "VERY_HOT_COCOA", "YUMMY_COOKIE" };
        if (hasBasicStrike) pool1.Add("NUTRITIOUS_SOUP");
        Add(output, Next(pool1, rng), "tezcatara.pool1");
        Add(output, Next(new[] { "BIIIG_HUG", "STORYBOOK", "TOASTY_MITTENS" }, rng), "tezcatara.pool2");
        Add(output, Next(new[] { "GOLDEN_COMPASS", "PUMPKIN_CANDLE", "TOY_BOX", "SEAL_OF_GOLD" }, rng), "tezcatara.pool3");
    }

    private static void PredictNonupeipe(
        List<AncientOptionPredictionResult> output,
        Xoshiro256StarStar rng,
        bool swiftEnchantableAtLeastFour)
    {
        var pool = new List<string>
        {
            "BLESSED_ANTLER", "BRILLIANT_SCARF", "DELICATE_FROND", "DIAMOND_DIADEM",
            "FUR_COAT", "GLITTER", "JEWELRY_BOX", "LOOMING_FRUIT", "SIGNET_RING"
        };
        if (swiftEnchantableAtLeastFour) pool.Add("BEAUTIFUL_BRACELET");
        rng.UnstableShuffle(pool);
        foreach (string entry in pool.Take(3)) Add(output, entry, "nonupeipe.shuffle-take3");
    }

    private static void PredictTanx(
        List<AncientOptionPredictionResult> output,
        Xoshiro256StarStar rng,
        bool instinctEnchantableAtLeastThree)
    {
        var pool = new List<string>
        {
            "CLAWS", "CROSSBOW", "IRON_CLUB", "MEAT_CLEAVER", "SAI", "SPIKED_GAUNTLETS",
            "TANXS_WHISTLE", "THROWING_AXE", "WAR_HAMMER"
        };
        if (instinctEnchantableAtLeastThree) pool.Add("TRI_BOOMERANG");
        rng.UnstableShuffle(pool);
        foreach (string entry in pool.Take(3)) Add(output, entry, "tanx.shuffle-take3");
    }

    private static void PredictVakuu(List<AncientOptionPredictionResult> output, Xoshiro256StarStar rng)
    {
        Add(output, FirstAfterShuffle(new[] { "BLOOD_SOAKED_ROSE", "WHISPERING_EARRING", "FIDDLE" }, rng), "vakuu.pool1");
        Add(output, FirstAfterShuffle(new[] { "PRESERVED_FOG", "SERE_TALON", "DISTINGUISHED_CAPE" }, rng), "vakuu.pool2");
        Add(output, FirstAfterShuffle(new[] { "CHOICES_PARADOX", "MUSIC_BOX", "LORDS_PARASOL", "JEWELED_MASK" }, rng), "vakuu.pool3");
    }

    private static void PredictDarv(
        List<AncientOptionPredictionResult> output,
        Xoshiro256StarStar rng,
        int act,
        bool allowPandora)
    {
        var sets = new List<string[]>
        {
            new[] { "ASTROLABE" }, new[] { "BLACK_STAR" }, new[] { "CALLING_BELL" },
            new[] { "EMPTY_CAGE" }
        };
        if (allowPandora) sets.Add(new[] { "PANDORAS_BOX" });
        sets.Add(new[] { "RUNIC_PYRAMID" });
        sets.Add(new[] { "SNECKO_EYE" });
        if (act == 2) sets.Add(new[] { "ECTOPLASM", "SOZU" });
        else if (act == 3) sets.Add(new[] { "PHILOSOPHERS_STONE", "VELVET_CHOKER" });

        // NextItem consumes one draw even for singleton sets.
        var source = sets.Select(set => Next(set, rng)).ToList();
        rng.UnstableShuffle(source);
        bool dustyTome = rng.NextBool();
        foreach (string entry in source.Take(dustyTome ? 2 : 3)) Add(output, entry, "darv.relic-set-shuffle");
        if (dustyTome) Add(output, "DUSTY_TOME", "darv.dusty-tome");
    }

    private static string Next(IReadOnlyList<string> source, Xoshiro256StarStar rng) =>
        source[rng.NextInt(source.Count)];

    private static string FirstAfterShuffle(IEnumerable<string> source, Xoshiro256StarStar rng)
    {
        var items = source.ToList();
        rng.UnstableShuffle(items);
        return items[0];
    }

    private static void Add(
        List<AncientOptionPredictionResult> output,
        string entry,
        string evidence,
        string? variantId = null,
        AncientOptionCharacterTargetProjection? characterTarget = null) =>
        output.Add(new AncientOptionPredictionResult(
            output.Count + 1,
            new ModelKey(BaseGameModelKeys.Categories.Relic, entry),
            PredictionPrecision.Exact,
            $"batch-a.stable107.ancient-option.{evidence}",
            variantId)
        {
            CharacterTarget = characterTarget
        });

    private sealed record OptionScenario(
        IReadOnlyList<AncientOptionPredictionResult> Options,
        int RngCalls);
}
