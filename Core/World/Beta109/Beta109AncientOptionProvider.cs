using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.World.Beta109;

public sealed record Beta109AncientOptionPrediction(
    PredictionPrecision Precision,
    IReadOnlyList<AncientOptionPredictionResult> Options,
    int RngCallCount,
    IReadOnlyList<WorldRngTraceEntry> Trace,
    EvidenceCode EvidenceCode,
    string IssueCode)
{
    public AncientOptionsEvaluationStatus EvaluationStatus { get; init; } = AncientOptionsEvaluationStatus.UnknownMissingAuthority;
}

public static class Beta109AncientOptionProvider
{
    private delegate OptionGenerationResult OptionGenerator(
        Beta109WorldGenerationSnapshot snapshot,
        Beta109AncientEventContextSnapshot context,
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng,
        AncientOptionConditionProfile conditions);

    private sealed record OptionGenerationResult(
        IReadOnlyList<(ModelKey Key, string? Variant)> Options,
        IReadOnlyDictionary<int, AncientOptionCharacterTargetProjection> CharacterTargets)
    {
        public static OptionGenerationResult Plain(
            IReadOnlyList<(ModelKey Key, string? Variant)> options) =>
            new(options, new Dictionary<int, AncientOptionCharacterTargetProjection>());
    }

    private sealed record GeneratorRegistration(
        Beta109AncientOptionSupport Capability,
        OptionGenerator Generate);

    private static readonly IReadOnlyDictionary<string, GeneratorRegistration> Generators =
        new Dictionary<string, GeneratorRegistration>(StringComparer.Ordinal)
        {
            ["DARV"] = new(Beta109AncientOptionSupport.PureInstanceProjection,
                (_, _, catalog, rng, conditions) => OptionGenerationResult.Plain(GenerateDarv(catalog, rng, conditions))),
            ["OROBAS"] = new(Beta109AncientOptionSupport.PureInstanceProjection,
                (snapshot, context, catalog, rng, conditions) => GenerateOrobas(snapshot, context, catalog, rng, conditions)),
            ["PAEL"] = new(Beta109AncientOptionSupport.DeckFactPredicate,
                (_, _, catalog, rng, conditions) => OptionGenerationResult.Plain(GeneratePael(catalog, rng, conditions))),
            ["TEZCATARA"] = new(Beta109AncientOptionSupport.DeckFactPredicate,
                (_, _, catalog, rng, conditions) => OptionGenerationResult.Plain(GenerateTezcatara(catalog, rng, conditions))),
            ["NONUPEIPE"] = new(Beta109AncientOptionSupport.DeckFactPredicate,
                (_, _, catalog, rng, conditions) => OptionGenerationResult.Plain(GenerateNonupeipe(catalog, rng, conditions))),
            ["TANX"] = new(Beta109AncientOptionSupport.DeckFactPredicate,
                (_, _, catalog, rng, conditions) => OptionGenerationResult.Plain(GenerateTanx(catalog, rng, conditions))),
            ["VAKUU"] = new(Beta109AncientOptionSupport.PureShuffleTake,
                (_, _, catalog, rng, _) => OptionGenerationResult.Plain(GenerateVakuu(catalog, rng)))
        };
    public static Beta109AncientOptionPrediction Predict(
        Beta109WorldGenerationSnapshot snapshot,
        int act,
        ModelKey ancientKey,
        int playerSlotIndex,
        ModelKey characterKey,
        bool runtimeAuthorityExact,
        AncientOptionConditionProfile? conditions = null)
        => PredictCore(snapshot, act, ancientKey, playerSlotIndex, characterKey,
            runtimeAuthorityExact, conditions, independentEventLocal: false);

    // A's hypothetical option observation does not require SelectedActs to have
    // been materialized by World. Keep the canonical generators and immutable
    // event authority checks, using the existing event-local Fast admission.
    // The public predictor / Production Exact authority above is unchanged.
    internal static Beta109AncientOptionPrediction PredictEventLocal(
        Beta109WorldGenerationSnapshot snapshot, int act, ModelKey ancientKey,
        int playerSlotIndex, ModelKey characterKey, AncientOptionConditionProfile conditions)
        => PredictCore(snapshot, act, ancientKey, playerSlotIndex, characterKey,
            true, conditions, independentEventLocal: true);

    private static Beta109AncientOptionPrediction PredictCore(
        Beta109WorldGenerationSnapshot snapshot, int act, ModelKey ancientKey,
        int playerSlotIndex, ModelKey characterKey, bool runtimeAuthorityExact,
        AncientOptionConditionProfile? conditions, bool independentEventLocal)
    {
        AncientOptionConditionProfile effectiveConditions = conditions ?? AncientOptionConditionProfile.BroadDefault;
        string normalizedAncient = NormalizeEntry(ancientKey.Entry);
        if (string.Equals(normalizedAncient, "NEOW", StringComparison.Ordinal))
        {
            // Neow choices/effects have one canonical owner in the Neow predictor.
            // Ancient identity remains visible, but this domain does not require an
            // event-option catalog/context or consume a second option RNG stream.
            return new Beta109AncientOptionPrediction(
                PredictionPrecision.DescriptionOnly,
                Array.Empty<AncientOptionPredictionResult>(),
                0,
                Array.Empty<WorldRngTraceEntry>(),
                Evidence(snapshot, "ancient.neow-options-owned-by-canonical-neow"),
                "CanonicalNeowPredictorOwnsOptions")
            {
                EvaluationStatus = AncientOptionsEvaluationStatus.NotEvaluatedByPolicy
            };
        }

        Beta109AncientEventContextSnapshot? context = snapshot.AncientEventContexts
            .FirstOrDefault(item => item.Act == act && item.AncientKey == ancientKey && item.PlayerSlot == playerSlotIndex) ??
            snapshot.AncientEventContexts.FirstOrDefault(item =>
                item.Act == act && item.AncientKey == ancientKey && item.IsShared);
        if (context is null)
        {
            return Unknown(snapshot, "MissingEventContext");
        }
        if (context.CharacterKey != characterKey ||
            (!context.IsShared && context.PlayerSlot != playerSlotIndex) ||
            string.IsNullOrWhiteSpace(context.EventIdEntry))
        {
            return Unknown(snapshot, "EventContextRequestMismatch");
        }
        if (snapshot.GameMode == WorldGameMode.Multiplayer && context.IsShared)
        {
            return Unsupported(snapshot, "MultiplayerSharedAncientSynchronizationUnsupported");
        }
        if (context.HookDecision == Beta109HookDecision.Unknown)
        {
            return Unknown(snapshot, "UnknownHook");
        }
        if (!context.EventRngRootExact)
        {
            return Unknown(snapshot, "MissingEventRngRootAuthority");
        }
        ulong expectedEventRoot = Beta109WorldRng.DeriveEventLocalSeed(
            snapshot.RunSeedRoot,
            context.PlayerSlot,
            context.IsShared,
            context.EventIdEntry);
        if (context.EventRngRoot != expectedEventRoot)
        {
            throw new InvalidOperationException("EventRngRootMismatch:ExactAncientContext:" + context.AncientKey.Serialized);
        }
        if (context.Catalog is null || !context.AncientKey.IsValid ||
            !string.Equals(context.EventIdEntry, context.Catalog.EventIdEntry, StringComparison.Ordinal))
        {
            return Unknown(snapshot, "MissingCatalog");
        }
        if (context.HookDecision == Beta109HookDecision.Deny)
        {
            IReadOnlyList<ModelKey> proceed = context.Catalog.Pool("wrapper.proceed");
            if (proceed.Count != 1) return Unknown(snapshot, "HookDeniedProceedCatalogMissing");
            PredictionPrecision deniedPrecision = AllowsExact(snapshot, context, runtimeAuthorityExact,
                independentEventLocal: independentEventLocal)
                ? PredictionPrecision.Exact
                : PredictionPrecision.Partial;
            return new Beta109AncientOptionPrediction(
                deniedPrecision,
                new[]
                {
                    new AncientOptionPredictionResult(
                        1,
                        proceed[0],
                        deniedPrecision,
                        Evidence(snapshot, "ancient.hook-denied-proceed"))
                    {
                        AuthorityFingerprint = context.ContextFingerprint,
                        IsSelectable = false,
                        IsLocked = true,
                        IsProceed = true,
                        AppearancePrecision = deniedPrecision,
                        SelectabilityPrecision = deniedPrecision,
                        StableTextKey = "PROCEED"
                    }
                },
                0,
                Array.Empty<WorldRngTraceEntry>(),
                Evidence(snapshot, "ancient.hook-denied-proceed"),
                deniedPrecision == PredictionPrecision.Exact ? string.Empty : OptionIssue(snapshot, context, normalizedAncient))
            {
                EvaluationStatus = AncientOptionsEvaluationStatus.EvaluatedLocked
            };
        }

        Beta109AncientOptionCatalogSnapshot catalog = context.Catalog;
        Beta109WorldRng rng = Beta109WorldRng.CreateEventLocal(
            snapshot.RunSeedRoot,
            context.PlayerSlot,
            context.IsShared,
            context.EventIdEntry);

        if (!Generators.TryGetValue(normalizedAncient, out GeneratorRegistration? registration))
        {
            return Unsupported(snapshot, "UnsupportedOptionGenerator:" + normalizedAncient);
        }
        if (catalog.Capability != registration.Capability)
        {
            return Unknown(snapshot, $"OptionCapabilityMismatch:{catalog.Capability}:{registration.Capability}");
        }

        OptionGenerationResult generation = registration.Generate(snapshot, context, catalog, rng, effectiveConditions);

        if (generation.Options.Count == 0)
        {
            if (string.Equals(normalizedAncient, "DEPRECATEDANCIENTEVENT", StringComparison.Ordinal))
            {
                return new Beta109AncientOptionPrediction(
                    PredictionPrecision.Exact,
                    Array.Empty<AncientOptionPredictionResult>(),
                    rng.CallCount,
                    rng.Trace.ToArray(),
                    Evidence(snapshot, "ancient.options-known-empty"),
                    string.Empty)
                {
                    EvaluationStatus = AncientOptionsEvaluationStatus.EvaluatedKnownEmpty
                };
            }
            return catalog.Capability is Beta109AncientOptionSupport.UnsupportedHookedObtain or
                   Beta109AncientOptionSupport.Unknown
                ? Unsupported(snapshot, "UnsupportedOptionGenerator")
                : Unknown(snapshot, "OptionGeneratorInputsIncomplete");
        }

        bool conditionAuthorityExact = HasSimpleConditionAuthority(normalizedAncient);

        // Option identity/order and full mutable-instance projection are separate
        // precision domains. Orobas Touch/Archaic Tooth setup and Darv Dusty Tome
        // setup can remain incomplete without making the already generated relic
        // ModelKeys or their visible order uncertain. Search filters consume only
        // OptionPrecision; post-obtain/setup completeness remains represented by
        // the aggregate prediction Precision and IssueCode.
        bool optionIdentityExact =
            AllowsExact(snapshot, context, runtimeAuthorityExact, conditionAuthorityExact, independentEventLocal) &&
            catalog.CatalogExact &&
            catalog.Pools.All(pool =>
                pool.SourceOrdinal >= 0 &&
                pool.OrderExact &&
                pool.FilterResultExact) &&
            context.EventContextExact &&
            (context.DynamicFactsExact || conditionAuthorityExact) &&
            context.ModifierFactsExact;
        bool fullProjectionExact =
            optionIdentityExact &&
            catalog.Pools.All(pool => pool.InstanceProjectionExact) &&
            catalog.MutableInstanceProjectionExact;

        PredictionPrecision optionIdentityPrecision = optionIdentityExact
            ? PredictionPrecision.Exact
            : PredictionPrecision.Partial;
        PredictionPrecision projectionPrecision = fullProjectionExact
            ? PredictionPrecision.Exact
            : PredictionPrecision.Partial;
        AncientOptionPredictionResult[] options = generation.Options.Select((item, index) =>
            new AncientOptionPredictionResult(
                index + 1,
                item.Key,
                optionIdentityPrecision,
                Evidence(snapshot, "direct-source.ancient.event-local-option"),
                item.Variant)
            {
                RngTrace = rng.Trace.ToArray(),
                AuthorityFingerprint = context.ContextFingerprint,
                AppearancePrecision = optionIdentityPrecision,
                SelectabilityPrecision = optionIdentityPrecision,
                StableTextKey = item.Key.Entry,
                CharacterTarget = generation.CharacterTargets.TryGetValue(index, out AncientOptionCharacterTargetProjection? target)
                    ? target
                    : null
            }).ToArray();

        return new Beta109AncientOptionPrediction(
            projectionPrecision,
            options,
            rng.CallCount,
            rng.Trace.ToArray(),
            Evidence(snapshot, "direct-source.ancient.event-local-option"),
            fullProjectionExact ? string.Empty : OptionIssue(snapshot, context, normalizedAncient))
        {
            EvaluationStatus = AncientOptionsEvaluationStatus.EvaluatedNonEmpty
        };
    }

    private static IReadOnlyList<(ModelKey Key, string? Variant)> GenerateDarv(
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng,
        AncientOptionConditionProfile conditions)
    {
        var source = new List<ModelKey>();
        foreach (Beta109NamedOptionPoolSnapshot pool in catalog.PoolsWithPrefix("darv.valid."))
        {
            if (!conditions.DarvAllowPandorasBoxRelicSet &&
                string.Equals(pool.PoolId, "darv.valid.pandoras-box", StringComparison.Ordinal))
            {
                continue;
            }
            if (pool.OrderedOptions.Count > 0)
                source.Add(rng.NextModelKey(pool.OrderedOptions, "darv:valid-set:" + pool.PoolId));
        }
        rng.UnstableShuffle(source, "darv:selected-shuffle");
        bool dustyBranch = rng.NextBool("darv:dusty-tome-branch");
        if (dustyBranch)
        {
            ModelKey dusty = catalog.Pool("darv.dusty-tome").Single();
            string variant = catalog.VariantData.TryGetValue("darv.dusty-tome.variant", out string? value)
                ? value
                : "rewards-projection-pending";
            return source.Take(2).Select(key => (key, (string?)null)).Append((dusty, variant)).ToArray();
        }
        return source.Take(3).Select(key => (key, (string?)null)).ToArray();
    }

    private static OptionGenerationResult GenerateOrobas(
        Beta109WorldGenerationSnapshot snapshot,
        Beta109AncientEventContextSnapshot context,
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng,
        AncientOptionConditionProfile conditions)
    {
        ModelKey? selectedCharacter = null;
        AncientOptionCharacterTargetProjection targetProjection;
        ModelKey[] candidates = context.UnlockedCharacters
            .Where(key =>
                key.IsValid &&
                key.Category == BaseGameModelKeys.Categories.Character &&
                key != context.CharacterKey)
            .ToArray();
        if (candidates.Length > 0)
        {
            int selectedIndex = rng.NextInt(candidates.Length, "orobas:other-character");
            selectedCharacter = candidates[selectedIndex];
            if (context.UnlockedCharacterSourceOrderExact)
            {
                rng.AnnotateLastSelection(selectedCharacter.Value);
                targetProjection = AncientOptionCharacterTargetProjection.Exact(
                    selectedCharacter.Value,
                    Evidence(snapshot, "orobas.sea-glass-target.runtime-unlocked-source-order"));
            }
            else
            {
                targetProjection = AncientOptionCharacterTargetProjection.Unknown(
                    Evidence(snapshot, "orobas.sea-glass-target.target-only-unknown"),
                    "MissingUnlockedCharacterSourceOrderAuthority");
            }
        }
        else
        {
            targetProjection = AncientOptionCharacterTargetProjection.Unknown(
                Evidence(snapshot, "orobas.sea-glass-target.no-other-character-candidate"),
                "NoOtherUnlockedCharacterCandidate");
        }
        bool specialBranch = rng.NextFloat("orobas:sea-glass-branch") < 0.3333333f;
        IReadOnlyList<ModelKey> firstPool = catalog.Pool(specialBranch ? "orobas.pool1.true" : "orobas.pool1.false");
        ModelKey first = rng.NextModelKey(firstPool, "orobas:pool1");
        ModelKey second = rng.NextModelKey(catalog.Pool("orobas.pool2"), "orobas:pool2");
        var pool3 = new List<ModelKey>();
        if (conditions.OrobasTouchOfOrobasConditionMet)
        {
            pool3.AddRange(catalog.Pool("orobas.pool3.touch"));
        }
        if (conditions.OrobasArchaicToothConditionMet)
        {
            pool3.AddRange(catalog.Pool("orobas.pool3.tooth"));
        }
        if (pool3.Count == 0)
        {
            throw new InvalidOperationException("OrobasPool3NoEligiblePresetOption");
        }
        ModelKey third = rng.NextModelKey(pool3, "orobas:pool3");
        string? variant = selectedCharacter.HasValue && string.Equals(first.Entry, "SEA_GLASS", StringComparison.Ordinal)
                          && context.UnlockedCharacterSourceOrderExact
            ? "character=" + selectedCharacter.Value.Serialized
            : null;
        IReadOnlyList<(ModelKey Key, string? Variant)> options =
            new[] { (first, variant), (second, (string?)null), (third, (string?)null) };
        IReadOnlyDictionary<int, AncientOptionCharacterTargetProjection> targets =
            string.Equals(first.Entry, "SEA_GLASS", StringComparison.Ordinal)
                ? new Dictionary<int, AncientOptionCharacterTargetProjection> { [0] = targetProjection }
                : new Dictionary<int, AncientOptionCharacterTargetProjection>();
        return new OptionGenerationResult(options, targets);
    }


    private static IReadOnlyList<(ModelKey Key, string? Variant)> GeneratePael(
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng,
        AncientOptionConditionProfile conditions)
    {
        ModelKey first = rng.NextModelKey(catalog.Pool("pael.pool1"), "pael:pool1");

        var pool2 = catalog.Pool("pael.pool2.base").ToList();
        if (conditions.PaelGoopyDefendCardsAtLeast3)
        {
            pool2.AddRange(catalog.Pool("pael.pool2.goopy"));
        }
        if (conditions.PaelRemovableCardsAtLeast5)
        {
            pool2.AddRange(catalog.Pool("pael.pool2.removable"));
        }
        pool2.AddRange(pool2.ToArray());
        pool2.AddRange(catalog.Pool("pael.pool2.growth"));
        ModelKey second = rng.NextModelKey(pool2, "pael:pool2-weighted");

        var pool3 = catalog.Pool("pael.pool3.base").ToList();
        if (conditions.PaelAllowLegionNoEventPet)
        {
            pool3.AddRange(catalog.Pool("pael.pool3.no-event-pet"));
        }
        ModelKey third = rng.NextModelKey(pool3, "pael:pool3");
        return new[] { (first, (string?)null), (second, (string?)null), (third, (string?)null) };
    }

    private static IReadOnlyList<(ModelKey Key, string? Variant)> GenerateTezcatara(
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng,
        AncientOptionConditionProfile conditions)
    {
        var pool1 = catalog.Pool("tezcatara.pool1.base").ToList();
        if (conditions.TezcataraHasBasicStrike)
        {
            pool1.AddRange(catalog.Pool("tezcatara.pool1.basic-strike"));
        }
        return new[]
        {
            (rng.NextModelKey(pool1, "tezcatara:pool1"), (string?)null),
            (rng.NextModelKey(catalog.Pool("tezcatara.pool2"), "tezcatara:pool2"), (string?)null),
            (rng.NextModelKey(catalog.Pool("tezcatara.pool3"), "tezcatara:pool3"), (string?)null)
        };
    }

    private static IReadOnlyList<(ModelKey Key, string? Variant)> GenerateNonupeipe(
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng,
        AncientOptionConditionProfile conditions)
    {
        var pool = catalog.Pool("nonupeipe.pool.base").ToList();
        if (conditions.NonupeipeSwiftEnchantableAtLeast4)
        {
            pool.AddRange(catalog.Pool("nonupeipe.pool.swift"));
        }
        rng.UnstableShuffle(pool, "nonupeipe:shuffle");
        return pool.Take(3).Select(key => (key, (string?)null)).ToArray();
    }

    private static IReadOnlyList<(ModelKey Key, string? Variant)> GenerateTanx(
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng,
        AncientOptionConditionProfile conditions)
    {
        var pool = catalog.Pool("tanx.pool.base").ToList();
        if (conditions.TanxInstinctEnchantableAtLeast3)
        {
            pool.AddRange(catalog.Pool("tanx.pool.instinct"));
        }
        rng.UnstableShuffle(pool, "tanx:shuffle");
        return pool.Take(3).Select(key => (key, (string?)null)).ToArray();
    }

    private static IReadOnlyList<(ModelKey Key, string? Variant)> GenerateThreeChoice(
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng,
        string prefix)
    {
        return new[]
        {
            (rng.NextModelKey(catalog.Pool(prefix + ".pool1"), prefix + ":pool1"), (string?)null),
            (rng.NextModelKey(catalog.Pool(prefix + ".pool2"), prefix + ":pool2"), (string?)null),
            (rng.NextModelKey(catalog.Pool(prefix + ".pool3"), prefix + ":pool3"), (string?)null)
        };
    }

    private static IReadOnlyList<(ModelKey Key, string? Variant)> GenerateShuffleTake(
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng,
        string poolId,
        int take,
        string stage)
    {
        List<ModelKey> pool = catalog.Pool(poolId).ToList();
        rng.UnstableShuffle(pool, stage + ":shuffle");
        return pool.Take(take).Select(key => (key, (string?)null)).ToArray();
    }

    private static IReadOnlyList<(ModelKey Key, string? Variant)> GenerateVakuu(
        Beta109AncientOptionCatalogSnapshot catalog,
        Beta109WorldRng rng)
    {
        var output = new List<(ModelKey, string?)>();
        foreach (string poolId in new[] { "vakuu.pool1", "vakuu.pool2", "vakuu.pool3" })
        {
            List<ModelKey> pool = catalog.Pool(poolId).ToList();
            rng.UnstableShuffle(pool, poolId + ":shuffle");
            if (pool.Count == 0) throw new InvalidOperationException("VakuuPoolEmpty:" + poolId);
            output.Add((pool[0], null));
        }
        return output;
    }

    private static bool AllowsExact(
        Beta109WorldGenerationSnapshot snapshot,
        Beta109AncientEventContextSnapshot context,
        bool runtimeAuthorityExact,
        bool conditionAuthorityExact = false,
        bool independentEventLocal = false) =>
        runtimeAuthorityExact &&
        (independentEventLocal
            ? snapshot.AllowsAncientOptionFastSearch(context.AncientKey.Entry) ||
              snapshot.AllowsAncientOptionProductionExact(context.AncientKey.Entry)
            : snapshot.AllowsAncientOptionProductionExact(context.AncientKey.Entry)) &&
        context.EventContextExact &&
        context.EventRngRootExact &&
        (context.DynamicFactsExact || conditionAuthorityExact) &&
        context.ModifierFactsExact &&
        context.HookDecision != Beta109HookDecision.Unknown;

    private static string OptionIssue(
        Beta109WorldGenerationSnapshot snapshot,
        Beta109AncientEventContextSnapshot context,
        string normalizedAncient)
    {
        if (context.HookDecision == Beta109HookDecision.Unknown) return "UnknownHook";
        if (!context.EventContextExact) return "MissingEventContext";
        if (!context.EventRngRootExact) return "MissingEventRngRootAuthority";
        ulong expectedEventRoot = Beta109WorldRng.DeriveEventLocalSeed(
            snapshot.RunSeedRoot,
            context.PlayerSlot,
            context.IsShared,
            context.EventIdEntry);
        if (context.EventRngRoot != expectedEventRoot) return "EventRngRootMismatch";
        if (!context.DynamicFactsExact && !HasSimpleConditionAuthority(normalizedAncient))
            return "MissingDynamicFacts";
        if (!context.ModifierFactsExact) return "UnknownModifier";
        if (context.Catalog is null || !context.Catalog.CatalogExact) return "MissingCatalog";
        if (context.Catalog.Pools.Any(pool => pool.SourceOrdinal < 0 || !pool.OrderExact))
            return "MissingOptionPoolOrder";
        if (context.Catalog.Pools.Any(pool => !pool.FilterResultExact))
            return "MissingOptionFilterAuthority";
        if (context.Catalog.Pools.Any(pool => !pool.InstanceProjectionExact)) return "MissingOptionPoolProjection";
        if (!context.Catalog.MutableInstanceProjectionExact) return "MissingInstanceProjection";
        if (!snapshot.AllowsAncientOptionProductionExact(context.AncientKey.Entry))
            return "PendingRealGameAncientOptionFixture:" + NormalizeEntry(context.AncientKey.Entry);
        return "AncientOptionAuthorityPartial";
    }

    private static bool HasSimpleConditionAuthority(string normalizedAncient) =>
        normalizedAncient is "PAEL" or "OROBAS" or "TEZCATARA" or
            "NONUPEIPE" or "TANX" or "DARV";

    private static string NormalizeEntry(string entry) =>
        new(entry.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static Beta109AncientOptionPrediction Unknown(
        Beta109WorldGenerationSnapshot snapshot,
        string issue) => new(
        PredictionPrecision.Unknown,
        Array.Empty<AncientOptionPredictionResult>(),
        0,
        Array.Empty<WorldRngTraceEntry>(),
        Evidence(snapshot, "ancient.options-unknown"),
        issue)
    {
        EvaluationStatus = AncientOptionsEvaluationStatus.UnknownMissingAuthority
    };

    private static Beta109AncientOptionPrediction Unsupported(
        Beta109WorldGenerationSnapshot snapshot,
        string issue) => new(
        PredictionPrecision.Unsupported,
        Array.Empty<AncientOptionPredictionResult>(),
        0,
        Array.Empty<WorldRngTraceEntry>(),
        Evidence(snapshot, "ancient.options-unsupported"),
        issue)
    {
        EvaluationStatus = AncientOptionsEvaluationStatus.Unsupported
    };

    private static string Evidence(Beta109WorldGenerationSnapshot snapshot, string suffix) => snapshot.Profile switch
    {
        RuntimeProfileId.Beta111 => "beta111." + suffix,
        RuntimeProfileId.Beta110 => "beta110." + suffix,
        _ => "beta109-historical-donor." + suffix
    };
}
