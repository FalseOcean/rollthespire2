using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum WorldFastAncientOptionGeneratorKind : byte
{
    None,
    HookDeniedProceed,
    Darv,
    Orobas,
    Pael,
    Tezcatara,
    Nonupeipe,
    Tanx,
    Vakuu
}

internal enum WorldFastAncientOptionPoolRole : byte
{
    WrapperProceed,
    DarvValid,
    DarvDustyTome,
    OrobasPool1True,
    OrobasPool1False,
    OrobasPool2,
    OrobasPool3Touch,
    OrobasPool3Tooth,
    PaelPool1,
    PaelPool2Base,
    PaelPool2Goopy,
    PaelPool2Removable,
    PaelPool2Growth,
    PaelPool3Base,
    PaelPool3NoEventPet,
    TezcataraPool1Base,
    TezcataraPool1BasicStrike,
    TezcataraPool2,
    TezcataraPool3,
    NonupeipeBase,
    NonupeipeSwift,
    TanxBase,
    TanxInstinct,
    VakuuPool1,
    VakuuPool2,
    VakuuPool3
}

internal readonly record struct WorldFastAncientOptionPool(
    WorldFastAncientOptionPoolRole Role,
    int SourceOrdinal,
    ushort[] Values);

/// <summary>
/// One immutable event-local option projection. EventEntryHash and the player/shared
/// slot inputs are sufficient to derive the per-seed RNG root without retaining a
/// production context object in the worker.
/// </summary>
internal sealed record WorldFastAncientOptionPlan(
    ushort AncientId,
    WorldFastAncientOptionGeneratorKind Generator,
    int PlayerSlot,
    bool IsShared,
    ulong EventEntryHash,
    WorldFastAncientOptionPool[] Pools,
    ushort CurrentCharacterId,
    ushort[] OtherUnlockedCharacters,
    ushort SeaGlassOptionId,
    bool SeaGlassTargetAuthorityExact,
    int MaxScratchCount,
    string AuthorityFingerprint)
{
    public bool IsExact => Generator != WorldFastAncientOptionGeneratorKind.None;
}

internal readonly record struct WorldFastAncientBranchOptionPredicate(
    ushort AncientId,
    ushort[] OptionAny,
    ushort[] SeaGlassTargetAny,
    bool OptionFastReject,
    bool SeaGlassFastReject,
    bool OptionAlwaysReject,
    bool SeaGlassAlwaysReject)
{
    public bool HasOptionCondition => OptionAny.Length > 0 || OptionAlwaysReject;
    public bool HasSeaGlassCondition => SeaGlassTargetAny.Length > 0 || SeaGlassAlwaysReject;
}

internal sealed record WorldFastAncientOptionCompilation(
    WorldFastAncientOptionPlan[] Plans,
    WorldFastAncientBranchOptionPredicate[] BranchPredicates,
    WorldFastDenseSetPredicate[] LegacyOptionPredicates,
    WorldFastDenseSetPredicate[] LegacySeaGlassPredicates,
    int AncientOptionPredicateCount,
    int SeaGlassPredicateCount,
    int AncientOptionExactOnlyPredicateCount,
    int SeaGlassExactOnlyPredicateCount,
    int MaxScratchCount,
    bool AlwaysReject,
    string AuthorityFingerprint);

internal readonly record struct WorldFastAncientOptionProjection(
    ushort Option0,
    ushort Option1,
    ushort Option2,
    byte OptionCount,
    ushort SeaGlassTarget,
    bool SeaGlassVisible,
    bool SeaGlassTargetExact);

internal enum WorldFastAncientOptionEvaluation : byte
{
    Skipped,
    Match,
    Reject,
    ConservativeKeep
}

internal readonly record struct WorldFastAncientOptionEvaluationResult(
    WorldFastAncientOptionEvaluation Evaluation,
    bool OptionStageEvaluated,
    bool OptionStagePassed,
    bool SeaGlassStageEvaluated,
    bool SeaGlassStagePassed);

internal static class Beta110AncientOptionFastPlanCompiler
{
    private const int MaximumPoolSize = 512;
    private const int MaximumScratchSize = 1024;

    public static WorldFastAncientOptionCompilation Compile(
        Beta109WorldGenerationSnapshot generation,
        NeowSearchFilter filter,
        int act,
        IReadOnlyList<ModelKey> possibleAncients,
        Func<ModelKey, ushort> add,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey,
        int playerSlot = 0)
    {
        AncientSearchBranchCondition[] branches = filter.AncientBranchConditions
            .Where(item => item.IsValid && item.Act == act)
            .ToArray();
        ActModelKeySetFilter[] legacyOptions = filter.AncientOptionFilters
            .Where(item => !item.IsEmpty && item.Act == act)
            .ToArray();
        ActModelKeySetFilter[] legacySeaGlass = filter.AncientSeaGlassTargetFilters
            .Where(item => !item.IsEmpty && item.Act == act)
            .ToArray();

        bool hasOptionRequest = branches.Any(item => item.OptionAny.Count > 0) || legacyOptions.Length > 0;
        bool hasSeaGlassRequest = branches.Any(item => item.SeaGlassTargetAny.Count > 0) || legacySeaGlass.Length > 0;
        if (!hasOptionRequest && !hasSeaGlassRequest)
        {
            return new WorldFastAncientOptionCompilation(
                Array.Empty<WorldFastAncientOptionPlan>(),
                Array.Empty<WorldFastAncientBranchOptionPredicate>(),
                Array.Empty<WorldFastDenseSetPredicate>(),
                Array.Empty<WorldFastDenseSetPredicate>(),
                0, 0, 0, 0, 0, false, string.Empty);
        }

        ModelKey[] requestedAncients = branches.Select(item => item.AncientKey)
            .Concat(legacyOptions.Length > 0 || legacySeaGlass.Length > 0 ? possibleAncients : Array.Empty<ModelKey>())
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();

        var plans = new List<WorldFastAncientOptionPlan>();
        var exactByAncient = new Dictionary<ModelKey, WorldFastAncientOptionPlan>(ModelKeyComparer.Instance);
        foreach (ModelKey ancient in requestedAncients)
        {
            WorldFastAncientOptionPlan? plan = TryCompilePlan(generation, filter, act, ancient, add, playerSlot);
            if (plan is null) continue;
            plans.Add(plan);
            exactByAncient[ancient] = plan;
        }

        var branchPredicates = new List<WorldFastAncientBranchOptionPredicate>();
        int optionFastCount = 0;
        int seaFastCount = 0;
        int optionExactOnlyCount = 0;
        int seaExactOnlyCount = 0;
        bool alwaysReject = false;

        foreach (AncientSearchBranchCondition branch in branches)
        {
            bool hasExactPlan = exactByAncient.TryGetValue(branch.AncientKey, out WorldFastAncientOptionPlan? optionPlan);
            ushort ancientId = denseByKey.TryGetValue(branch.AncientKey, out ushort knownAncient)
                ? knownAncient
                : Beta110WorldFastPlan.InvalidDenseId;

            ushort[] optionAny = CompileAvailableIds(branch.OptionAny, denseByKey);
            bool optionAlwaysReject = branch.OptionAny.Count > 0 && optionAny.Length == 0;
            bool optionFast = branch.OptionAny.Count > 0 && hasExactPlan;
            if (branch.OptionAny.Count > 0)
            {
                if (optionFast) optionFastCount++;
                else optionExactOnlyCount++;
            }

            ushort[] seaTargets = CompileAvailableIds(branch.SeaGlassTargetAny, denseByKey);
            bool seaAlwaysReject = branch.SeaGlassTargetAny.Count > 0 && seaTargets.Length == 0;
            bool seaFast = branch.SeaGlassTargetAny.Count > 0 &&
                           hasExactPlan &&
                           optionPlan!.Generator == WorldFastAncientOptionGeneratorKind.Orobas &&
                           optionPlan.SeaGlassTargetAuthorityExact;
            if (branch.SeaGlassTargetAny.Count > 0)
            {
                if (seaFast) seaFastCount++;
                else seaExactOnlyCount++;
            }

            // Branch conditions are OR-scoped by Ancient identity within an Act.
            // An impossible target on one branch cannot globally reject another
            // Ancient branch that may still satisfy the query.
            branchPredicates.Add(new WorldFastAncientBranchOptionPredicate(
                ancientId,
                optionAny,
                seaTargets,
                optionFast,
                seaFast,
                optionAlwaysReject,
                seaAlwaysReject));
        }

        bool allPossibleOptionsExact = possibleAncients.All(exactByAncient.ContainsKey);
        WorldFastDenseSetPredicate[] compiledLegacyOptions = Array.Empty<WorldFastDenseSetPredicate>();
        if (legacyOptions.Length > 0)
        {
            if (allPossibleOptionsExact)
            {
                compiledLegacyOptions = legacyOptions.Select(item => CompileSet(item.Keys, denseByKey)).ToArray();
                optionFastCount += compiledLegacyOptions.Length;
                alwaysReject |= compiledLegacyOptions.Any(item => item.AlwaysReject);
            }
            else
            {
                optionExactOnlyCount += legacyOptions.Length;
            }
        }

        // Sea Glass is Orobas-only. Other Ancient identities satisfy the existing
        // legacy target contract as not-applicable; exact Orobas target authority is
        // therefore sufficient for safe rejection.
        WorldFastDenseSetPredicate[] compiledLegacySeaGlass = Array.Empty<WorldFastDenseSetPredicate>();
        if (legacySeaGlass.Length > 0)
        {
            WorldFastAncientOptionPlan? oro = plans.FirstOrDefault(item =>
                item.Generator == WorldFastAncientOptionGeneratorKind.Orobas);
            if (oro is not null && oro.SeaGlassTargetAuthorityExact ||
                !possibleAncients.Any(k => Normalize(k.Entry) == "OROBAS"))
            {
                compiledLegacySeaGlass = legacySeaGlass.Select(item => CompileSet(item.Keys, denseByKey)).ToArray();
                seaFastCount += compiledLegacySeaGlass.Length;
                alwaysReject |= compiledLegacySeaGlass.Any(item => item.AlwaysReject);
            }
            else
            {
                seaExactOnlyCount += legacySeaGlass.Length;
            }
        }

        int maxScratch = plans.Count == 0 ? 0 : plans.Max(item => item.MaxScratchCount);
        string authorityFingerprint = Fingerprint(new[]
        {
            generation.SnapshotFingerprint,
            filter.AncientOptionConditions.Fingerprint,
            act.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(';', plans.Select(item => item.AuthorityFingerprint)),
            optionFastCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            seaFastCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            optionExactOnlyCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            seaExactOnlyCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

        return new WorldFastAncientOptionCompilation(
            plans.ToArray(),
            branchPredicates.ToArray(),
            compiledLegacyOptions,
            compiledLegacySeaGlass,
            optionFastCount,
            seaFastCount,
            optionExactOnlyCount,
            seaExactOnlyCount,
            maxScratch,
            alwaysReject,
            authorityFingerprint);
    }

    private static WorldFastAncientOptionPlan? TryCompilePlan(
        Beta109WorldGenerationSnapshot generation,
        NeowSearchFilter filter,
        int act,
        ModelKey ancient,
        Func<ModelKey, ushort> add,
        int playerSlot)
    {
        string normalized = Normalize(ancient.Entry);
        WorldFastAncientOptionGeneratorKind generator = normalized switch
        {
            "DARV" => WorldFastAncientOptionGeneratorKind.Darv,
            "OROBAS" => WorldFastAncientOptionGeneratorKind.Orobas,
            "PAEL" => WorldFastAncientOptionGeneratorKind.Pael,
            "TEZCATARA" => WorldFastAncientOptionGeneratorKind.Tezcatara,
            "NONUPEIPE" => WorldFastAncientOptionGeneratorKind.Nonupeipe,
            "TANX" => WorldFastAncientOptionGeneratorKind.Tanx,
            "VAKUU" => WorldFastAncientOptionGeneratorKind.Vakuu,
            _ => WorldFastAncientOptionGeneratorKind.None
        };
        if (generator == WorldFastAncientOptionGeneratorKind.None) return null;
        if (!generation.AllowsAncientOptionFastSearch(ancient.Entry)) return null;

        Beta109AncientEventContextSnapshot? context = generation.AncientEventContexts
            .FirstOrDefault(item => item.Act == act && item.AncientKey == ancient && item.PlayerSlot == playerSlot) ??
            generation.AncientEventContexts.FirstOrDefault(item =>
                item.Act == act && item.AncientKey == ancient && item.IsShared);
        if (context is null ||
            !generation.LobbyPlayers.Any(player => player.Slot == playerSlot && player.CharacterKey == context.CharacterKey) ||
            string.IsNullOrWhiteSpace(context.EventIdEntry) ||
            !context.EventContextExact ||
            !context.EventRngRootExact ||
            !context.ModifierFactsExact ||
            context.HookDecision == Beta109HookDecision.Unknown)
        {
            return null;
        }
        if (context.HookDecision == Beta109HookDecision.Deny)
        {
            // Production's hook-denied precision path does not apply the named
            // Ancient simple-condition exemption. Keep Fast reject authority only
            // when the same full dynamic-fact authority is available.
            if (!context.DynamicFactsExact) return null;
            Beta109AncientOptionCatalogSnapshot? deniedCatalog = context.Catalog;
            Beta109NamedOptionPoolSnapshot? proceedPool = deniedCatalog?.Pools.FirstOrDefault(item =>
                string.Equals(item.PoolId, "wrapper.proceed", StringComparison.Ordinal));
            if (deniedCatalog is null ||
                !deniedCatalog.CatalogExact ||
                proceedPool is null ||
                proceedPool.SourceOrdinal < 0 ||
                !proceedPool.OrderExact ||
                !proceedPool.FilterResultExact ||
                proceedPool.OrderedOptions.Count != 1)
            {
                return null;
            }
            return new WorldFastAncientOptionPlan(
                add(ancient),
                WorldFastAncientOptionGeneratorKind.HookDeniedProceed,
                context.PlayerSlot,
                context.IsShared,
                XxHash64.HashUtf8(context.EventIdEntry, 0UL),
                new[]
                {
                    new WorldFastAncientOptionPool(
                        WorldFastAncientOptionPoolRole.WrapperProceed,
                        proceedPool.SourceOrdinal,
                        new[] { add(proceedPool.OrderedOptions[0]) })
                },
                add(context.CharacterKey),
                Array.Empty<ushort>(),
                Beta110WorldFastPlan.InvalidDenseId,
                false,
                0,
                Fingerprint(new[] { context.ContextFingerprint, deniedCatalog.CatalogFingerprint, "HookDeniedProceed" }));
        }

        bool simpleConditionAuthority = normalized is "PAEL" or "OROBAS" or "TEZCATARA" or
            "NONUPEIPE" or "TANX" or "DARV";
        if (!context.DynamicFactsExact && !simpleConditionAuthority) return null;
        Beta109AncientOptionCatalogSnapshot? catalog = context.Catalog;
        if (catalog is null ||
            !catalog.CatalogExact ||
            catalog.Pools.Any(pool => pool.SourceOrdinal < 0 || !pool.OrderExact || !pool.FilterResultExact))
        {
            return null;
        }
        Beta109AncientOptionSupport expectedCapability = generator switch
        {
            WorldFastAncientOptionGeneratorKind.Darv or WorldFastAncientOptionGeneratorKind.Orobas =>
                Beta109AncientOptionSupport.PureInstanceProjection,
            WorldFastAncientOptionGeneratorKind.Pael or
            WorldFastAncientOptionGeneratorKind.Tezcatara or
            WorldFastAncientOptionGeneratorKind.Nonupeipe or
            WorldFastAncientOptionGeneratorKind.Tanx =>
                Beta109AncientOptionSupport.DeckFactPredicate,
            WorldFastAncientOptionGeneratorKind.Vakuu =>
                Beta109AncientOptionSupport.PureShuffleTake,
            _ => Beta109AncientOptionSupport.Unknown
        };
        if (catalog.Capability != expectedCapability) return null;

        var pools = new List<WorldFastAncientOptionPool>();
        bool AddPool(WorldFastAncientOptionPoolRole role, string poolId, bool required = true)
        {
            Beta109NamedOptionPoolSnapshot? source = catalog.Pools.FirstOrDefault(item =>
                string.Equals(item.PoolId, poolId, StringComparison.Ordinal));
            if (source is null || source.OrderedOptions.Count == 0)
                return !required;
            if (source.OrderedOptions.Count > MaximumPoolSize) return false;
            pools.Add(new WorldFastAncientOptionPool(
                role,
                source.SourceOrdinal,
                source.OrderedOptions.Select(add).ToArray()));
            return true;
        }
        bool AddConditionalPool(WorldFastAncientOptionPoolRole role, string poolId, bool include) =>
            !include || AddPool(role, poolId);

        bool poolsValid = generator switch
        {
            WorldFastAncientOptionGeneratorKind.Darv => CompileDarvPools(catalog, filter.AncientOptionConditions, pools, add),
            WorldFastAncientOptionGeneratorKind.Orobas =>
                AddPool(WorldFastAncientOptionPoolRole.OrobasPool1True, "orobas.pool1.true") &&
                AddPool(WorldFastAncientOptionPoolRole.OrobasPool1False, "orobas.pool1.false") &&
                AddPool(WorldFastAncientOptionPoolRole.OrobasPool2, "orobas.pool2") &&
                AddConditionalPool(WorldFastAncientOptionPoolRole.OrobasPool3Touch, "orobas.pool3.touch",
                    filter.AncientOptionConditions.OrobasTouchOfOrobasConditionMet) &&
                AddConditionalPool(WorldFastAncientOptionPoolRole.OrobasPool3Tooth, "orobas.pool3.tooth",
                    filter.AncientOptionConditions.OrobasArchaicToothConditionMet),
            WorldFastAncientOptionGeneratorKind.Pael =>
                AddPool(WorldFastAncientOptionPoolRole.PaelPool1, "pael.pool1") &&
                AddPool(WorldFastAncientOptionPoolRole.PaelPool2Base, "pael.pool2.base") &&
                AddConditionalPool(WorldFastAncientOptionPoolRole.PaelPool2Goopy, "pael.pool2.goopy",
                    filter.AncientOptionConditions.PaelGoopyDefendCardsAtLeast3) &&
                AddConditionalPool(WorldFastAncientOptionPoolRole.PaelPool2Removable, "pael.pool2.removable",
                    filter.AncientOptionConditions.PaelRemovableCardsAtLeast5) &&
                AddPool(WorldFastAncientOptionPoolRole.PaelPool2Growth, "pael.pool2.growth") &&
                AddPool(WorldFastAncientOptionPoolRole.PaelPool3Base, "pael.pool3.base") &&
                AddConditionalPool(WorldFastAncientOptionPoolRole.PaelPool3NoEventPet, "pael.pool3.no-event-pet",
                    filter.AncientOptionConditions.PaelAllowLegionNoEventPet),
            WorldFastAncientOptionGeneratorKind.Tezcatara =>
                AddPool(WorldFastAncientOptionPoolRole.TezcataraPool1Base, "tezcatara.pool1.base") &&
                AddConditionalPool(WorldFastAncientOptionPoolRole.TezcataraPool1BasicStrike, "tezcatara.pool1.basic-strike",
                    filter.AncientOptionConditions.TezcataraHasBasicStrike) &&
                AddPool(WorldFastAncientOptionPoolRole.TezcataraPool2, "tezcatara.pool2") &&
                AddPool(WorldFastAncientOptionPoolRole.TezcataraPool3, "tezcatara.pool3"),
            WorldFastAncientOptionGeneratorKind.Nonupeipe =>
                AddPool(WorldFastAncientOptionPoolRole.NonupeipeBase, "nonupeipe.pool.base") &&
                AddConditionalPool(WorldFastAncientOptionPoolRole.NonupeipeSwift, "nonupeipe.pool.swift",
                    filter.AncientOptionConditions.NonupeipeSwiftEnchantableAtLeast4),
            WorldFastAncientOptionGeneratorKind.Tanx =>
                AddPool(WorldFastAncientOptionPoolRole.TanxBase, "tanx.pool.base") &&
                AddConditionalPool(WorldFastAncientOptionPoolRole.TanxInstinct, "tanx.pool.instinct",
                    filter.AncientOptionConditions.TanxInstinctEnchantableAtLeast3),
            WorldFastAncientOptionGeneratorKind.Vakuu =>
                AddPool(WorldFastAncientOptionPoolRole.VakuuPool1, "vakuu.pool1") &&
                AddPool(WorldFastAncientOptionPoolRole.VakuuPool2, "vakuu.pool2") &&
                AddPool(WorldFastAncientOptionPoolRole.VakuuPool3, "vakuu.pool3"),
            _ => false
        };
        if (!poolsValid) return null;

        ushort currentCharacter = add(context.CharacterKey);
        ushort[] otherCharacters = context.UnlockedCharacters
            .Where(key => key.IsValid && key.Category == BaseGameModelKeys.Categories.Character && key != context.CharacterKey)
            .Select(add)
            .ToArray();
        ushort seaGlass = Beta110WorldFastPlan.InvalidDenseId;
        if (generator == WorldFastAncientOptionGeneratorKind.Orobas)
        {
            ModelKey? key = catalog.Pool("orobas.pool1.false")
                .FirstOrDefault(item => string.Equals(item.Entry, "SEA_GLASS", StringComparison.Ordinal));
            if (key.HasValue && key.Value.IsValid) seaGlass = add(key.Value);
        }

        int maxScratch = CalculateMaxScratch(generator, pools);
        if (maxScratch > MaximumScratchSize) return null;
        string authorityFingerprint = Fingerprint(new[]
        {
            generation.SnapshotFingerprint,
            context.ContextFingerprint,
            catalog.CatalogFingerprint,
            filter.AncientOptionConditions.Fingerprint,
            generator.ToString(),
            string.Join(';', pools.OrderBy(item => item.SourceOrdinal)
                .Select(item => $"{item.Role}:{item.SourceOrdinal}:{string.Join(',', item.Values)}")),
            string.Join(',', otherCharacters)
        });

        return new WorldFastAncientOptionPlan(
            add(ancient),
            generator,
            context.PlayerSlot,
            context.IsShared,
            XxHash64.HashUtf8(context.EventIdEntry, 0UL),
            pools.OrderBy(item => item.SourceOrdinal).ToArray(),
            currentCharacter,
            otherCharacters,
            seaGlass,
            generator == WorldFastAncientOptionGeneratorKind.Orobas &&
                generation.HasExactOrobasSeaGlassTargetAuthority &&
                context.UnlockedCharacterSourceOrderExact &&
                otherCharacters.Length > 0,
            maxScratch,
            authorityFingerprint);
    }

    private static bool CompileDarvPools(
        Beta109AncientOptionCatalogSnapshot catalog,
        AncientOptionConditionProfile conditions,
        List<WorldFastAncientOptionPool> output,
        Func<ModelKey, ushort> add)
    {
        foreach (Beta109NamedOptionPoolSnapshot pool in catalog.PoolsWithPrefix("darv.valid."))
        {
            if (!conditions.DarvAllowPandorasBoxRelicSet &&
                string.Equals(pool.PoolId, "darv.valid.pandoras-box", StringComparison.Ordinal))
                continue;
            if (pool.OrderedOptions.Count == 0 || pool.OrderedOptions.Count > MaximumPoolSize) return false;
            output.Add(new WorldFastAncientOptionPool(
                WorldFastAncientOptionPoolRole.DarvValid,
                pool.SourceOrdinal,
                pool.OrderedOptions.Select(add).ToArray()));
        }
        Beta109NamedOptionPoolSnapshot? dusty = catalog.Pools.FirstOrDefault(item =>
            string.Equals(item.PoolId, "darv.dusty-tome", StringComparison.Ordinal));
        if (dusty is null || dusty.OrderedOptions.Count != 1) return false;
        output.Add(new WorldFastAncientOptionPool(
            WorldFastAncientOptionPoolRole.DarvDustyTome,
            dusty.SourceOrdinal,
            dusty.OrderedOptions.Select(add).ToArray()));
        return output.Count(item => item.Role == WorldFastAncientOptionPoolRole.DarvValid) > 0;
    }

    private static int CalculateMaxScratch(
        WorldFastAncientOptionGeneratorKind generator,
        IReadOnlyList<WorldFastAncientOptionPool> pools) => generator switch
        {
            WorldFastAncientOptionGeneratorKind.Darv => pools.Count(item => item.Role == WorldFastAncientOptionPoolRole.DarvValid),
            WorldFastAncientOptionGeneratorKind.Orobas =>
                PoolLength(pools, WorldFastAncientOptionPoolRole.OrobasPool3Touch) +
                PoolLength(pools, WorldFastAncientOptionPoolRole.OrobasPool3Tooth),
            WorldFastAncientOptionGeneratorKind.Pael => Math.Max(
                2 * (PoolLength(pools, WorldFastAncientOptionPoolRole.PaelPool2Base) +
                     PoolLength(pools, WorldFastAncientOptionPoolRole.PaelPool2Goopy) +
                     PoolLength(pools, WorldFastAncientOptionPoolRole.PaelPool2Removable)) +
                PoolLength(pools, WorldFastAncientOptionPoolRole.PaelPool2Growth),
                PoolLength(pools, WorldFastAncientOptionPoolRole.PaelPool3Base) +
                PoolLength(pools, WorldFastAncientOptionPoolRole.PaelPool3NoEventPet)),
            WorldFastAncientOptionGeneratorKind.Tezcatara =>
                PoolLength(pools, WorldFastAncientOptionPoolRole.TezcataraPool1Base) +
                PoolLength(pools, WorldFastAncientOptionPoolRole.TezcataraPool1BasicStrike),
            WorldFastAncientOptionGeneratorKind.Nonupeipe =>
                PoolLength(pools, WorldFastAncientOptionPoolRole.NonupeipeBase) +
                PoolLength(pools, WorldFastAncientOptionPoolRole.NonupeipeSwift),
            WorldFastAncientOptionGeneratorKind.Tanx =>
                PoolLength(pools, WorldFastAncientOptionPoolRole.TanxBase) +
                PoolLength(pools, WorldFastAncientOptionPoolRole.TanxInstinct),
            WorldFastAncientOptionGeneratorKind.Vakuu => pools.Max(item => item.Values.Length),
            _ => 0
        };

    private static int PoolLength(
        IReadOnlyList<WorldFastAncientOptionPool> pools,
        WorldFastAncientOptionPoolRole role)
    {
        foreach (WorldFastAncientOptionPool pool in pools)
            if (pool.Role == role) return pool.Values.Length;
        return 0;
    }

    private static ushort[] CompileAvailableIds(
        IReadOnlyList<ModelKey> keys,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey) => keys
        .Distinct(ModelKeyComparer.Instance)
        .Where(denseByKey.ContainsKey)
        .Select(key => denseByKey[key])
        .ToArray();

    private static WorldFastDenseSetPredicate CompileSet(
        ModelKeySetFilter keys,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey)
    {
        ushort[] any = CompileAvailableIds(keys.Any, denseByKey);
        ushort[] all = CompileAvailableIds(keys.All, denseByKey);
        ushort[] ban = CompileAvailableIds(keys.Ban, denseByKey);
        bool reject = (keys.Any.Count > 0 && any.Length == 0) || all.Length != keys.All.Distinct(ModelKeyComparer.Instance).Count();
        return new WorldFastDenseSetPredicate(any, all, ban, reject);
    }

    private static string Normalize(string entry) =>
        new(entry.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Fingerprint(IEnumerable<string> values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values))))
            .ToLowerInvariant();
}

internal static class Beta110AncientOptionFastStage
{
    public static WorldFastAncientOptionEvaluationResult Evaluate(
        ulong rootHash,
        ushort ancientId,
        WorldFastActPlan act)
    {
        WorldFastAncientOptionEvaluationResult? rejected = null;
        foreach (WorldFastAncientBranchOptionPredicate item in act.AncientBranchOptionPredicates)
        {
            if (item.AncientId != ancientId) continue;
            var row = EvaluateRow(rootHash, ancientId, act, item);
            if (row.Evaluation != WorldFastAncientOptionEvaluation.Reject) return row;
            rejected = row;
        }
        return rejected ?? EvaluateRow(rootHash, ancientId, act, null);
    }

    private static WorldFastAncientOptionEvaluationResult EvaluateRow(ulong rootHash, ushort ancientId,
        WorldFastActPlan act, WorldFastAncientBranchOptionPredicate? branch)
    {
        bool optionRequested = branch is { HasOptionCondition: true } || act.AncientOptionPredicates.Length > 0;
        bool seaRequested = branch is { HasSeaGlassCondition: true } || act.SeaGlassTargetPredicates.Length > 0;
        if (!optionRequested && !seaRequested)
            return new WorldFastAncientOptionEvaluationResult(WorldFastAncientOptionEvaluation.Skipped, false, false, false, false);

        WorldFastAncientOptionPlan? plan = null;
        foreach (WorldFastAncientOptionPlan item in act.AncientOptionPlans)
        {
            if (item.AncientId != ancientId) continue;
            plan = item;
            break;
        }

        bool branchOptionExact = branch is not { HasOptionCondition: true } || branch.Value.OptionFastReject;
        bool legacyOptionExact = act.AncientOptionPredicates.Length == 0 || plan is not null;
        bool optionFastActive = optionRequested && plan is not null && branchOptionExact && legacyOptionExact;
        bool branchSeaExact = branch is not { HasSeaGlassCondition: true } || branch.Value.SeaGlassFastReject;
        bool legacySeaExact = act.SeaGlassTargetPredicates.Length == 0 ||
            (plan is not null && (plan.Generator != WorldFastAncientOptionGeneratorKind.Orobas || plan.SeaGlassTargetAuthorityExact));
        bool seaFastActive = seaRequested && plan is not null && branchSeaExact && legacySeaExact;
        if (!optionFastActive && !seaFastActive)
        {
            return new WorldFastAncientOptionEvaluationResult(
                WorldFastAncientOptionEvaluation.ConservativeKeep,
                false, false, false, false);
        }

        if (!TryProject(rootHash, plan!, out WorldFastAncientOptionProjection projection))
        {
            return new WorldFastAncientOptionEvaluationResult(
                WorldFastAncientOptionEvaluation.ConservativeKeep,
                false, false, false, false);
        }

        Span<ushort> options = stackalloc ushort[3];
        if (projection.OptionCount > 0) options[0] = projection.Option0;
        if (projection.OptionCount > 1) options[1] = projection.Option1;
        if (projection.OptionCount > 2) options[2] = projection.Option2;
        ReadOnlySpan<ushort> visible = options[..projection.OptionCount];

        if (optionFastActive)
        {
            if (branch is { HasOptionCondition: true } &&
                (branch.Value.OptionAlwaysReject || !ContainsAny(visible, branch.Value.OptionAny)))
            {
                return new WorldFastAncientOptionEvaluationResult(
                    WorldFastAncientOptionEvaluation.Reject, true, false, false, false);
            }
            foreach (WorldFastDenseSetPredicate predicate in act.AncientOptionPredicates)
            {
                if (!Matches(visible, predicate))
                {
                    return new WorldFastAncientOptionEvaluationResult(
                        WorldFastAncientOptionEvaluation.Reject, true, false, false, false);
                }
            }
        }

        if (seaFastActive)
        {
            if (branch is { HasSeaGlassCondition: true } &&
                (branch.Value.SeaGlassAlwaysReject ||
                 !projection.SeaGlassVisible ||
                 !projection.SeaGlassTargetExact ||
                 !branch.Value.SeaGlassTargetAny.Contains(projection.SeaGlassTarget)))
            {
                return new WorldFastAncientOptionEvaluationResult(
                    WorldFastAncientOptionEvaluation.Reject, optionFastActive, optionFastActive, true, false);
            }
            if (act.SeaGlassTargetPredicates.Length > 0 && projection.SeaGlassVisible)
            {
                if (!projection.SeaGlassTargetExact)
                {
                    return new WorldFastAncientOptionEvaluationResult(
                        WorldFastAncientOptionEvaluation.ConservativeKeep,
                        optionFastActive, optionFastActive, false, false);
                }
                Span<ushort> target = stackalloc ushort[] { projection.SeaGlassTarget };
                foreach (WorldFastDenseSetPredicate predicate in act.SeaGlassTargetPredicates)
                {
                    if (!Matches(target, predicate))
                    {
                        return new WorldFastAncientOptionEvaluationResult(
                            WorldFastAncientOptionEvaluation.Reject, optionFastActive, optionFastActive, true, false);
                    }
                }
            }
        }

        bool exactOnlyRemainder = (optionRequested && !optionFastActive) || (seaRequested && !seaFastActive);
        return new WorldFastAncientOptionEvaluationResult(
            exactOnlyRemainder ? WorldFastAncientOptionEvaluation.ConservativeKeep : WorldFastAncientOptionEvaluation.Match,
            optionFastActive,
            optionFastActive,
            seaFastActive,
            seaFastActive);
    }

    private static bool TryProject(
        ulong rootHash,
        WorldFastAncientOptionPlan plan,
        out WorldFastAncientOptionProjection projection) =>
        TryProjectCore(rootHash, plan, out projection, out _);

    internal static bool TryProjectForGpuParity(
        ulong rootHash,
        WorldFastAncientOptionPlan plan,
        out WorldFastAncientOptionProjection projection,
        out UpFrontRngCheckpoint finalState) =>
        TryProjectCore(rootHash, plan, out projection, out finalState);

    private static bool TryProjectCore(
        ulong rootHash,
        WorldFastAncientOptionPlan plan,
        out WorldFastAncientOptionProjection projection,
        out UpFrontRngCheckpoint finalState)
    {
        projection = default;
        finalState = default;
        if (plan.Generator == WorldFastAncientOptionGeneratorKind.HookDeniedProceed)
        {
            ReadOnlySpan<ushort> proceed = Pool(plan, WorldFastAncientOptionPoolRole.WrapperProceed);
            if (proceed.Length != 1) return false;
            projection = new WorldFastAncientOptionProjection(
                proceed[0],
                Beta110WorldFastPlan.InvalidDenseId,
                Beta110WorldFastPlan.InvalidDenseId,
                1,
                Beta110WorldFastPlan.InvalidDenseId,
                false,
                false);
            return true;
        }

        unchecked
        {
            long signedRoot = (long)rootHash;
            long signedWithSlot = signedRoot + (plan.IsShared ? 0L : plan.PlayerSlot);
            ulong seed = (ulong)signedWithSlot + plan.EventEntryHash;
            var rng = new Beta110FastRng(seed);
            Span<ushort> scratch = stackalloc ushort[plan.MaxScratchCount];
            Span<ushort> output = stackalloc ushort[3];
            int outputCount = 0;
            ushort target = Beta110WorldFastPlan.InvalidDenseId;
            bool targetExact = false;

            switch (plan.Generator)
            {
                case WorldFastAncientOptionGeneratorKind.Darv:
                {
                    int count = 0;
                    foreach (WorldFastAncientOptionPool pool in plan.Pools)
                    {
                        if (pool.Role != WorldFastAncientOptionPoolRole.DarvValid) continue;
                        if (pool.Values.Length == 0 || count >= scratch.Length) return false;
                        scratch[count++] = pool.Values[rng.NextInt(pool.Values.Length)];
                    }
                    rng.UnstableShuffle(scratch[..count]);
                    bool dusty = rng.NextBool();
                    int take = Math.Min(dusty ? 2 : 3, count);
                    for (int index = 0; index < take; index++) output[outputCount++] = scratch[index];
                    if (dusty)
                    {
                        ReadOnlySpan<ushort> dustyPool = Pool(plan, WorldFastAncientOptionPoolRole.DarvDustyTome);
                        if (dustyPool.Length != 1 || outputCount >= 3) return false;
                        output[outputCount++] = dustyPool[0];
                    }
                    break;
                }
                case WorldFastAncientOptionGeneratorKind.Orobas:
                {
                    if (plan.OtherUnlockedCharacters.Length > 0)
                    {
                        target = plan.OtherUnlockedCharacters[rng.NextInt(plan.OtherUnlockedCharacters.Length)];
                        targetExact = plan.SeaGlassTargetAuthorityExact;
                    }
                    bool special = rng.NextFloat() < 0.3333333f;
                    ReadOnlySpan<ushort> firstPool = Pool(plan, special
                        ? WorldFastAncientOptionPoolRole.OrobasPool1True
                        : WorldFastAncientOptionPoolRole.OrobasPool1False);
                    ReadOnlySpan<ushort> secondPool = Pool(plan, WorldFastAncientOptionPoolRole.OrobasPool2);
                    int thirdCount = AppendTwoPools(plan, scratch,
                        WorldFastAncientOptionPoolRole.OrobasPool3Touch,
                        WorldFastAncientOptionPoolRole.OrobasPool3Tooth);
                    if (firstPool.Length == 0 || secondPool.Length == 0) return false;
                    output[0] = firstPool[rng.NextInt(firstPool.Length)];
                    output[1] = secondPool[rng.NextInt(secondPool.Length)];
                    if (thirdCount == 0) { rng.NextInt(1); outputCount = 2; }
                    else { output[2] = scratch[rng.NextInt(thirdCount)]; outputCount = 3; }
                    break;
                }
                case WorldFastAncientOptionGeneratorKind.Pael:
                {
                    ReadOnlySpan<ushort> first = Pool(plan, WorldFastAncientOptionPoolRole.PaelPool1);
                    if (first.Length == 0) return false;
                    output[0] = first[rng.NextInt(first.Length)];
                    int baseCount = AppendThreePools(plan, scratch,
                        WorldFastAncientOptionPoolRole.PaelPool2Base,
                        WorldFastAncientOptionPoolRole.PaelPool2Goopy,
                        WorldFastAncientOptionPoolRole.PaelPool2Removable);
                    if (baseCount <= 0 || baseCount * 2 > scratch.Length) return false;
                    scratch[..baseCount].CopyTo(scratch[baseCount..]);
                    int secondCount = baseCount * 2;
                    secondCount += AppendPool(plan, scratch[secondCount..], WorldFastAncientOptionPoolRole.PaelPool2Growth);
                    if (secondCount <= 0) return false;
                    output[1] = scratch[rng.NextInt(secondCount)];
                    int thirdCount = AppendTwoPools(plan, scratch,
                        WorldFastAncientOptionPoolRole.PaelPool3Base,
                        WorldFastAncientOptionPoolRole.PaelPool3NoEventPet);
                    if (thirdCount <= 0) return false;
                    output[2] = scratch[rng.NextInt(thirdCount)];
                    outputCount = 3;
                    break;
                }
                case WorldFastAncientOptionGeneratorKind.Tezcatara:
                {
                    int firstCount = AppendTwoPools(plan, scratch,
                        WorldFastAncientOptionPoolRole.TezcataraPool1Base,
                        WorldFastAncientOptionPoolRole.TezcataraPool1BasicStrike);
                    ReadOnlySpan<ushort> second = Pool(plan, WorldFastAncientOptionPoolRole.TezcataraPool2);
                    ReadOnlySpan<ushort> third = Pool(plan, WorldFastAncientOptionPoolRole.TezcataraPool3);
                    if (firstCount == 0 || second.Length == 0 || third.Length == 0) return false;
                    output[0] = scratch[rng.NextInt(firstCount)];
                    output[1] = second[rng.NextInt(second.Length)];
                    output[2] = third[rng.NextInt(third.Length)];
                    outputCount = 3;
                    break;
                }
                case WorldFastAncientOptionGeneratorKind.Nonupeipe:
                {
                    int count = AppendTwoPools(plan, scratch,
                        WorldFastAncientOptionPoolRole.NonupeipeBase,
                        WorldFastAncientOptionPoolRole.NonupeipeSwift);
                    if (count == 0) return false;
                    rng.UnstableShuffle(scratch[..count]);
                    outputCount = Math.Min(3, count);
                    scratch[..outputCount].CopyTo(output);
                    break;
                }
                case WorldFastAncientOptionGeneratorKind.Tanx:
                {
                    int count = AppendTwoPools(plan, scratch,
                        WorldFastAncientOptionPoolRole.TanxBase,
                        WorldFastAncientOptionPoolRole.TanxInstinct);
                    if (count == 0) return false;
                    rng.UnstableShuffle(scratch[..count]);
                    outputCount = Math.Min(3, count);
                    scratch[..outputCount].CopyTo(output);
                    break;
                }
                case WorldFastAncientOptionGeneratorKind.Vakuu:
                {
                    if (!ShuffleTakeOne(plan, WorldFastAncientOptionPoolRole.VakuuPool1, scratch, ref rng, out ushort first) ||
                        !ShuffleTakeOne(plan, WorldFastAncientOptionPoolRole.VakuuPool2, scratch, ref rng, out ushort second) ||
                        !ShuffleTakeOne(plan, WorldFastAncientOptionPoolRole.VakuuPool3, scratch, ref rng, out ushort third))
                        return false;
                    output[0] = first;
                    output[1] = second;
                    output[2] = third;
                    outputCount = 3;
                    break;
                }
                default:
                    return false;
            }

            bool seaVisible = false;
            for (int index = 0; index < outputCount; index++)
                if (output[index] == plan.SeaGlassOptionId) seaVisible = true;
            projection = new WorldFastAncientOptionProjection(
                outputCount > 0 ? output[0] : Beta110WorldFastPlan.InvalidDenseId,
                outputCount > 1 ? output[1] : Beta110WorldFastPlan.InvalidDenseId,
                outputCount > 2 ? output[2] : Beta110WorldFastPlan.InvalidDenseId,
                checked((byte)outputCount),
                target,
                seaVisible,
                seaVisible && targetExact);
            finalState = rng.CaptureCheckpoint();
            return true;
        }
    }

    private static ReadOnlySpan<ushort> Pool(
        WorldFastAncientOptionPlan plan,
        WorldFastAncientOptionPoolRole role)
    {
        foreach (WorldFastAncientOptionPool pool in plan.Pools)
            if (pool.Role == role) return pool.Values;
        return ReadOnlySpan<ushort>.Empty;
    }

    private static int AppendPool(
        WorldFastAncientOptionPlan plan,
        Span<ushort> destination,
        WorldFastAncientOptionPoolRole role)
    {
        ReadOnlySpan<ushort> source = Pool(plan, role);
        if (source.Length > destination.Length) return -1;
        source.CopyTo(destination);
        return source.Length;
    }

    private static int AppendTwoPools(
        WorldFastAncientOptionPlan plan,
        Span<ushort> destination,
        WorldFastAncientOptionPoolRole first,
        WorldFastAncientOptionPoolRole second)
    {
        int count = AppendPool(plan, destination, first);
        if (count < 0) return -1;
        int added = AppendPool(plan, destination[count..], second);
        return added < 0 ? -1 : count + added;
    }

    private static int AppendThreePools(
        WorldFastAncientOptionPlan plan,
        Span<ushort> destination,
        WorldFastAncientOptionPoolRole first,
        WorldFastAncientOptionPoolRole second,
        WorldFastAncientOptionPoolRole third)
    {
        int count = AppendTwoPools(plan, destination, first, second);
        if (count < 0) return -1;
        int added = AppendPool(plan, destination[count..], third);
        return added < 0 ? -1 : count + added;
    }

    private static bool ShuffleTakeOne(
        WorldFastAncientOptionPlan plan,
        WorldFastAncientOptionPoolRole role,
        Span<ushort> scratch,
        ref Beta110FastRng rng,
        out ushort value)
    {
        value = Beta110WorldFastPlan.InvalidDenseId;
        ReadOnlySpan<ushort> source = Pool(plan, role);
        if (source.Length == 0 || source.Length > scratch.Length) return false;
        source.CopyTo(scratch);
        rng.UnstableShuffle(scratch[..source.Length]);
        value = scratch[0];
        return true;
    }

    private static bool ContainsAny(ReadOnlySpan<ushort> values, ReadOnlySpan<ushort> targets)
    {
        foreach (ushort target in targets)
            if (values.Contains(target)) return true;
        return false;
    }

    private static bool Matches(ReadOnlySpan<ushort> values, WorldFastDenseSetPredicate predicate)
    {
        if (predicate.AlwaysReject) return false;
        if (predicate.Any.Length > 0 && !ContainsAny(values, predicate.Any)) return false;
        foreach (ushort target in predicate.All)
            if (!values.Contains(target)) return false;
        foreach (ushort target in predicate.Ban)
            if (values.Contains(target)) return false;
        return true;
    }
}
