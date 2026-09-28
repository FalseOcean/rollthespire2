using System.Collections;
using System.Reflection;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    private static readonly IReadOnlyDictionary<int, string[]> LegacyActOrderByIndex =
        new Dictionary<int, string[]>
        {
            [1] = new[] { "OVERGROWTH", "UNDERDOCKS" },
            [2] = new[] { "HIVE" },
            [3] = new[] { "GLORY" }
        };

    private static readonly IReadOnlyDictionary<string, string[]> LegacyBossOrderByAct =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["OVERGROWTH"] = new[] { "CEREMONIAL_BEAST_BOSS", "THE_KIN_BOSS", "VANTOM_BOSS" },
            ["UNDERDOCKS"] = new[] { "LAGAVULIN_MATRIARCH_BOSS", "SOUL_FYSH_BOSS", "WATERFALL_GIANT_BOSS" },
            ["HIVE"] = new[] { "KAISER_CRAB_BOSS", "KNOWLEDGE_DEMON_BOSS", "THE_INSATIABLE_BOSS" },
            ["GLORY"] = new[] { "AEONGLASS_BOSS", "QUEEN_BOSS", "TEST_SUBJECT_BOSS" }
        };

    private static readonly IReadOnlyDictionary<string, string[]> LegacyAncientOrderByAct =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["OVERGROWTH"] = new[] { "NEOW" },
            ["UNDERDOCKS"] = new[] { "NEOW" },
            ["HIVE"] = new[] { "OROBAS", "PAEL", "TEZCATARA" },
            ["GLORY"] = new[] { "NONUPEIPE", "TANX", "VAKUU" }
        };

    public static WorldAuthoritySnapshot CaptureWorld(
        IRuntimeProfile profile,
        string rawSeed,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        bool? noRunModifiers,
        NeowEffectAuthoritySnapshot? effects,
        WorldGameMode predictionGameMode = WorldGameMode.Unknown,
        PredictionGameModeAuthority predictionGameModeAuthority = PredictionGameModeAuthority.Unknown,
        string gameVersion = "",
        string act1OverrideRaw = "random",
        object? explicitUnlockState = null,
        IReadOnlyList<ModelKey>? orderedCharacters = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
        if (assembly is null)
        {
            return MissingWorld(profile.ProfileId, "Sts2AssemblyNotLoaded");
        }

        try
        {
            Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
            if (modelDb is null)
            {
                return MissingWorld(profile.ProfileId, "ModelDbNotFound");
            }

            string unlockSource = "explicit-party-slot";
            object? unlockState = explicitUnlockState ?? TryCaptureCurrentUnlockState(assembly, out unlockSource);
            bool unlockStateExact = unlockState is not null;
            ModelListCapture sharedEventsCapture = ReadStaticModelsWorld(modelDb,
                "AllSharedEvents", "SharedEvents");
            Beta109EventCatalogAuthoritySnapshot capturedEventAuthority = CaptureBeta109EventAuthority(
                assembly,
                unlockState,
                sharedEventsCapture);
            IReadOnlyList<WorldActGroupSnapshot> groups = CaptureActGroups(
                modelDb,
                profile.ProfileId,
                unlockState,
                capturedEventAuthority);
            ModelListCapture sharedAncientsCapture = ReadSharedAncientsWorld(modelDb, unlockState);
            ModelListCapture unlockedCharactersCapture = ReadUnlockedCharactersWorld(modelDb, unlockState);
            // Relic catalogs are runtime-authoritative and may legitimately contain models
            // supplied by enabled content mods. Unlike Act/Event/Encounter authority, the
            // option-binding catalog only needs a stable ModelKey for every returned entry;
            // assembly ownership is not part of the identity contract.
            ModelListCapture relicCatalogCapture = ReadStaticRuntimeModelKeysWorld(
                modelDb,
                "AllRelics",
                "Relics");
            Beta109StaticAuthorityCapture? modernStaticAuthority = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
                ? CaptureBeta109StaticAuthority(
                    profile.ProfileId,
                    assembly,
                    modelDb,
                    unlockState,
                    character,
                    playersCount,
                    playerSlotIndex,
                    predictionGameMode,
                    predictionGameModeAuthority,
                    act1OverrideRaw, orderedCharacters)
                : null;
            IReadOnlyList<ModelKey> fallbackRelics = (effects?.SharedRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>())
                .Concat(effects?.CharacterRelicPoolSource ?? Array.Empty<NeowEffectRelicSnapshot>())
                .Select(relic => relic.RelicKey)
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            IReadOnlyList<ModelKey> ancientOptionRelicCatalog = relicCatalogCapture.Keys.Count > 0
                ? relicCatalogCapture.Keys
                : fallbackRelics;

            IReadOnlyList<ModelKey> sharedEvents = modernStaticAuthority?.SharedEvents ?? sharedEventsCapture.Keys;
            IReadOnlyList<ModelKey> sharedAncients = modernStaticAuthority?.UnlockedSharedAncients ?? sharedAncientsCapture.Keys;
            IReadOnlyList<ModelKey> unlockedCharacters = unlockedCharactersCapture.Keys;
            bool unlockedCharactersExact = unlockedCharactersCapture.Exact;
            bool ancientOptionRelicCatalogExact = relicCatalogCapture.Exact && relicCatalogCapture.Keys.Count > 0;

            bool generationInputsPresent = groups.Count > 0 &&
                                           groups.All(group => group.Acts.Count > 0) &&
                                           groups.SelectMany(group => group.Acts).All(act =>
                                               act.Events.Count + sharedEvents.Count > 0 &&
                                               act.WeakEncounters.Count > 0 &&
                                               act.RegularEncounters.Count > 0 &&
                                               act.EliteEncounters.Count > 0 &&
                                               act.Bosses.Count > 0 &&
                                               act.Ancients.Count > 0);
            bool allActInputsExact = groups.SelectMany(group => group.Acts)
                .All(act => act.GenerationInputsExact);
            bool legacyCatalogOrderExact = profile.ProfileId == RuntimeProfileId.Stable107 &&
                                           unlockStateExact &&
                                           generationInputsPresent &&
                                           allActInputsExact &&
                                           sharedEventsCapture.Exact &&
                                           sharedAncientsCapture.Exact &&
                                           sharedAncients.Count > 0;
            bool modernStaticFoundationExact = modernStaticAuthority is not null &&
                                               modernStaticAuthority.ActSelectionAuthorityExact &&
                                               modernStaticAuthority.ModeFactsExact &&
                                               modernStaticAuthority.OrderedActs.Count > 0 &&
                                               modernStaticAuthority.OrderedActs.All(act => act.HasExactGenerationInputs) &&
                                               modernStaticAuthority.SharedEventCatalogExact &&
                                               modernStaticAuthority.AllSharedAncientCatalogExact &&
                                               modernStaticAuthority.UnlockedSharedAncientCatalogExact &&
                                               modernStaticAuthority.UnlockFactsExact &&
                                               modernStaticAuthority.RelicInitializationExact &&
                                               modernStaticAuthority.IsVanilla;
            bool catalogOrderExact = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
                ? modernStaticFoundationExact
                : legacyCatalogOrderExact;
            bool relicSourceExact = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
                ? modernStaticAuthority?.RelicInitializationExact == true
                : effects?.HasExactRelicBagSourcePools == true;
            bool hasCatalog = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
                ? modernStaticAuthority?.OrderedActs.Count > 0
                : groups.Count > 0 && groups.All(group => group.Acts.Count > 0);
            SourceAuthority authority = hasCatalog && catalogOrderExact && relicSourceExact
                ? SourceAuthority.OfficialRuntimeExact
                : SourceAuthority.Incomplete;
            SnapshotCompleteness completeness = hasCatalog && relicSourceExact
                ? SnapshotCompleteness.Complete
                : hasCatalog ? SnapshotCompleteness.Partial : SnapshotCompleteness.Missing;

            IEnumerable<string> descriptors = groups.SelectMany(group => group.Acts.SelectMany(DescribeAct))
                .Concat(sharedEvents.Select(key => "shared-event:" + key.Serialized))
                .Concat(capturedEventAuthority.EpochsInFilterOrder.Select(epoch =>
                    "event-epoch:" + epoch.EpochId + ":" + epoch.IsRevealed + ":" +
                    epoch.MembershipExact + ":" + epoch.RevealFactExact + ":" +
                    string.Join(",", epoch.OrderedMemberKeys.Select(key => key.Serialized))))
                .Concat(new[]
                {
                    "event-shared-order-exact:" + capturedEventAuthority.SharedRawOrderExact,
                    "event-filter-order-exact:" + capturedEventAuthority.EpochFilterOrderExact,
                    "event-authority-evidence:" + capturedEventAuthority.EvidenceCode
                })
                .Concat(sharedAncients.Select(key => "shared-ancient:" + key.Serialized))
                .Concat(unlockedCharacters.Select(key => "character:" + key.Serialized))
                .Concat(new[] { "characters-exact:" + unlockedCharactersExact })
                .Concat(ancientOptionRelicCatalog.Select(key => "option-relic:" + key.Serialized))
                .Concat(new[] { "option-relic-catalog-exact:" + ancientOptionRelicCatalogExact })
                .Concat(modernStaticAuthority is null
                    ? Array.Empty<string>()
                    : new[]
                    {
                        ModernWorldEvidencePrefix(profile.ProfileId, "static-authority:") + modernStaticAuthority.DiagnosticCode,
                        ModernWorldEvidencePrefix(profile.ProfileId, "act-selection-exact:") + modernStaticAuthority.ActSelectionAuthorityExact,
                        ModernWorldEvidencePrefix(profile.ProfileId, "relic-init-exact:") + modernStaticAuthority.RelicInitializationExact,
                        ModernWorldEvidencePrefix(profile.ProfileId, "relic-shop-eligibility-exact:") + modernStaticAuthority.RelicShopEligibilityAuthorityExact
                    });
            string catalogFingerprint = Fingerprint("world-catalog-v2", descriptors);
            string snapshotFingerprint = Fingerprint("world-authority-v2", new[]
            {
                profile.ProfileId.ToString(),
                character.CharacterKey.Serialized,
                catalogFingerprint,
                effects?.RelicBagFingerprint ?? "missing-relic-sources",
                unlockStateExact.ToString(),
                unlockSource,
                catalogOrderExact.ToString(),
                relicSourceExact.ToString(),
                completeness.ToString()
            });

            Beta109WorldGenerationSnapshot? beta109 = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
                ? BuildBeta109WorldGenerationSnapshot(
                    profile,
                    rawSeed,
                    character,
                    ascension,
                    playersCount,
                    playerSlotIndex,
                    noRunModifiers,
                    modernStaticAuthority!,
                    effects,
                    ancientOptionRelicCatalog,
                    ancientOptionRelicCatalogExact,
                    unlockedCharacters,
                    unlockedCharactersExact,
                    unlockStateExact,
                    unlockSource,
                    catalogFingerprint,
                    gameVersion)
                : null;
            if (beta109 is not null)
            {
                catalogFingerprint = beta109.CatalogFingerprint;
                snapshotFingerprint = beta109.SnapshotFingerprint;
            }

            string captureDiagnostic = RuntimeProfilePolicies.IsModernCore(profile.ProfileId)
                ? beta109?.CaptureDiagnosticCode ?? (profile.ProfileId switch
                {
                    RuntimeProfileId.Beta111 => "Beta111WorldSnapshotMissing",
                    RuntimeProfileId.Beta110 => "Beta110WorldSnapshotMissing",
                    _ => "Beta109HistoricalWorldSnapshotMissing"
                })
                : !unlockStateExact
                    ? "Stable107WorldUnlockStateUnavailable:" + unlockSource
                    : !catalogOrderExact
                        ? "Stable107WorldGenerationInputsIncompleteOrNonVanilla"
                        : string.Empty;

            return new WorldAuthoritySnapshot(
                profile.ProfileId,
                groups,
                sharedEvents,
                sharedAncients,
                effects?.SharedRelicPoolSource,
                effects?.CharacterRelicPoolSource,
                unlockedCharacters,
                unlockedCharactersExact,
                ancientOptionRelicCatalog,
                ancientOptionRelicCatalogExact,
                catalogOrderExact,
                relicSourceExact,
                authority,
                completeness,
                catalogFingerprint,
                snapshotFingerprint,
                DateTimeOffset.UtcNow,
                captureDiagnostic,
                beta109)
            {
                EventAuthority = beta109?.EventAuthority ?? capturedEventAuthority,
                EncounterNumberOfRuns = unlockState is null ? null : ReadProperty(unlockState, "NumberOfRuns") as int?
            };
        }
        catch (Exception ex)
        {
            return MissingWorld(profile.ProfileId, "WorldReflectionCaptureFailed:" + ex.GetType().Name);
        }
    }

    private static IReadOnlyList<WorldActGroupSnapshot> CaptureActGroups(
        Type modelDb,
        RuntimeProfileId profileId,
        object? unlockState,
        Beta109EventCatalogAuthoritySnapshot eventAuthority)
    {
        object? source = ReadStaticMemberWorld(modelDb, "ActsByIndex") ??
                         ReadStaticMemberWorld(modelDb, "Acts") ??
                         ReadStaticMemberWorld(modelDb, "AllActs");
        var groups = new List<(int Act, List<object> Models)>();
        if (source is not null)
        {
            foreach (object item in Enumerate(source))
            {
                if (TryReadKeyValueWorld(item, out int act, out object? value))
                {
                    groups.Add((act, Enumerate(value).ToList()));
                }
            }
        }

        if (groups.Count == 0 && source is not null)
        {
            List<object> nested = Enumerate(source).ToList();
            for (int index = 0; index < nested.Count; index++)
            {
                List<object> acts = Enumerate(nested[index]).ToList();
                if (acts.Count > 0) groups.Add((index + 1, acts));
            }
        }

        if (groups.Count == 0 && source is not null)
        {
            foreach (object actModel in Enumerate(source))
            {
                if (!TryModelKey(actModel, out ModelKey actKey)) continue;
                int act = InferActIndexWorld(actKey.Entry);
                if (act <= 0) continue;
                int existingIndex = groups.FindIndex(group => group.Act == act);
                if (existingIndex < 0) groups.Add((act, new List<object> { actModel }));
                else groups[existingIndex].Models.Add(actModel);
            }
        }

        var output = new List<WorldActGroupSnapshot>();
        foreach ((int act, List<object> models) in groups.OrderBy(group => group.Act))
        {
            WorldActSnapshot[] acts = models
                .Select(model => CaptureActWorld(model, act, profileId, unlockState, eventAuthority))
                .Where(snapshot => snapshot is not null)
                .Cast<WorldActSnapshot>()
                .ToArray();
            if (profileId == RuntimeProfileId.Stable107)
            {
                acts = ApplyKnownActOrderWorld(acts, act);
            }
            if (acts.Length > 0) output.Add(new WorldActGroupSnapshot(act, acts));
        }
        return output;
    }

    private static WorldActSnapshot? CaptureActWorld(
        object model,
        int act,
        RuntimeProfileId profileId,
        object? unlockState,
        Beta109EventCatalogAuthoritySnapshot eventAuthority)
    {
        if (!TryModelKey(model, out ModelKey actKey)) return null;

        int? knownWeak = KnownWeakEncounterSlotsWorld(actKey.Entry);
        int? knownTotal = KnownTotalNormalRoomsWorld(actKey.Entry);
        int weakSlots = TryReadInt(model,
                            "NumberOfWeakEncounters", "NumWeakEncounters", "WeakEncounterCount", "WeakEncounterSlots") ??
                        knownWeak ?? 0;
        int totalNormalRooms = TryReadInt(model,
                                   "TotalNormalRooms", "NumNormalRooms", "NormalRoomCount") ??
                               knownTotal ?? weakSlots;

        ModelListCapture rawEventsCapture = ReadInstanceModelsWorld(model, "AllEvents", "Events");
        ModelListCapture eventsCapture = ReadUnlockedInstanceModelsWorld(
            model,
            unlockState,
            "GetUnlockedEvents",
            "AllEvents", "Events");
        IReadOnlyList<ModelKey> eligibleEvents = FilterBeta109Events(
            rawEventsCapture.Keys,
            eventAuthority.OrderedSharedEventsRaw,
            eventAuthority);
        EncounterListCapture weakCapture = ReadEncountersWorld(model,
            "AllWeakEncounters", "WeakEncounters");
        EncounterListCapture regularCapture = ReadEncountersWorld(model,
            "AllRegularEncounters", "AllNormalEncounters", "RegularEncounters", "NormalEncounters", "AllEncounters", "Encounters");
        EncounterListCapture eliteCapture = ReadEncountersWorld(model,
            "AllEliteEncounters", "EliteEncounters");
        ModelListCapture bossCapture = ReadInstanceModelsWorld(model,
            "AllBossEncounters", "BossEncounters", "Bosses");
        ModelListCapture ancientCapture = ReadUnlockedInstanceModelsWorld(
            model,
            unlockState,
            "GetUnlockedAncients",
            "AllAncients", "Ancients", "AncientEvents");

        IReadOnlyList<ModelKey> bosses = bossCapture.Keys;
        IReadOnlyList<ModelKey> ancients = ancientCapture.Keys;
        bool bossOrderExact = bossCapture.Exact;
        bool ancientOrderExact = ancientCapture.Exact;
        if (profileId == RuntimeProfileId.Stable107)
        {
            (bosses, bossOrderExact) = ApplyKnownOrderWorld(
                bosses,
                LegacyBossOrderByAct.GetValueOrDefault(actKey.Entry),
                bossOrderExact);
            (ancients, ancientOrderExact) = ApplyKnownOrderWorld(
                ancients,
                LegacyAncientOrderByAct.GetValueOrDefault(actKey.Entry),
                ancientOrderExact);
        }

        bool officialAct = IsOfficialSts2ModelWorld(model);
        bool shapeExact = profileId == RuntimeProfileId.Stable107 &&
                          knownWeak.HasValue && knownTotal.HasValue &&
                          weakSlots == knownWeak.Value && totalNormalRooms == knownTotal.Value;
        bool generationInputsExact = officialAct &&
                                     shapeExact &&
                                     eventsCapture.Exact &&
                                     weakCapture.Exact &&
                                     regularCapture.Exact &&
                                     eliteCapture.Exact &&
                                     bossOrderExact &&
                                     ancientOrderExact;

        return new WorldActSnapshot(
            act,
            actKey,
            Math.Max(0, weakSlots),
            Math.Max(weakSlots, totalNormalRooms),
            eventsCapture.Keys,
            weakCapture.Items,
            regularCapture.Items,
            eliteCapture.Items,
            bosses,
            ancients,
            generationInputsExact)
        {
            OrderedRawEvents = rawEventsCapture.Keys,
            OrderedEligibleEvents = eligibleEvents,
            RawEventCatalogOrderExact = rawEventsCapture.Exact,
            EventEpochMembershipExact = eventAuthority.EpochMembershipExact,
            EventEpochRevealFactsExact = eventAuthority.EpochRevealFactsExact,
            EligibleEventOrderExact = rawEventsCapture.Exact && eventAuthority.HasExactFilteringAuthority
        };
    }

    private static ModelListCapture ReadUnlockedInstanceModelsWorld(
        object owner,
        object? unlockState,
        string exactMethodName,
        params string[] fallbackNames)
    {
        if (unlockState is not null)
        {
            MethodInfo[] methods = owner.GetType().GetMethods(PublicInstance | BindingFlags.NonPublic)
                .Where(method => string.Equals(method.Name, exactMethodName, StringComparison.Ordinal))
                .OrderBy(method => method.GetParameters().Length)
                .ToArray();
            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(unlockState)) continue;
                try
                {
                    object? value = method.Invoke(owner, new[] { unlockState });
                    // An empty unlocked result is a valid exact catalog. The old
                    // helper treated every empty enumerable as missing and forced
                    // a fallback to the unfiltered backing collection.
                    return CapturePossiblyEmptyModelListWorld(value);
                }
                catch
                {
                    // Fail closed into the diagnostic fallback below.
                }
            }
        }

        ModelListCapture fallback = ReadInstanceModelsWorld(owner, fallbackNames);
        return fallback with { Exact = false };
    }

    private static ModelListCapture ReadSharedAncientsWorld(Type modelDb, object? unlockState)
    {
        ModelListCapture direct = ReadStaticModelsWorld(modelDb,
            "SharedAncients", "AllSharedAncients", "AllSharedAncientEvents");
        if (direct.Keys.Count > 0) return direct;

        if (unlockState is not null)
        {
            foreach (string methodName in new[] { "GetUnlockedSharedAncients", "GetSharedAncients" })
            {
                foreach (MethodInfo method in modelDb.GetMethods(PublicStatic | BindingFlags.NonPublic)
                             .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal)))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(unlockState)) continue;
                    try
                    {
                        ModelListCapture capture = CaptureModelListWorld(method.Invoke(null, new[] { unlockState }));
                        if (capture.Keys.Count > 0) return capture;
                    }
                    catch
                    {
                    }
                }
            }
        }

        return new ModelListCapture(Array.Empty<ModelKey>(), false);
    }

    private static ModelListCapture ReadUnlockedCharactersWorld(Type modelDb, object? unlockState)
    {
        if (unlockState is not null)
        {
            ModelListCapture capture = CaptureRuntimeModelKeyListWorld(ReadProperty(unlockState, "Characters"));
            if (capture.Keys.Count > 0) return capture;
        }
        ModelListCapture fallback = ReadStaticModelsWorld(modelDb, "AllCharacters", "Characters");
        return fallback with { Exact = false };
    }

    private static EncounterListCapture ReadEncountersWorld(object owner, params string[] names)
    {
        foreach (string name in names)
        {
            List<object> models = Enumerate(ReadProperty(owner, name)).ToList();
            if (models.Count == 0) continue;
            bool exact = models.All(IsOfficialSts2ModelWorld);
            WorldEncounterSnapshot[] items = models.Select(model =>
            {
                TryModelKey(model, out ModelKey key);
                string[] tags = Enumerate(ReadProperty(model, "Tags"))
                    .Select(tag => tag.ToString() ?? string.Empty)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return new WorldEncounterSnapshot(key, tags);
            }).Where(item => item.EncounterKey.IsValid).ToArray();
            return new EncounterListCapture(items, exact && items.Length == models.Count);
        }
        return new EncounterListCapture(Array.Empty<WorldEncounterSnapshot>(), false);
    }

    private static ModelListCapture ReadInstanceModelsWorld(object owner, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = ReadProperty(owner, name);
            ModelListCapture capture = CaptureModelListWorld(value);
            if (capture.Keys.Count > 0) return capture;
        }
        return new ModelListCapture(Array.Empty<ModelKey>(), false);
    }

    private static ModelListCapture ReadStaticModelsWorld(Type owner, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = ReadStaticMemberWorld(owner, name);
            ModelListCapture capture = CaptureModelListWorld(value);
            if (capture.Keys.Count > 0) return capture;
        }
        return new ModelListCapture(Array.Empty<ModelKey>(), false);
    }

    /// <summary>
    /// Captures runtime ModelKey source order without requiring each model implementation
    /// type to live in sts2.dll. This is intentionally narrow: it is used for ordered
    /// identity catalogs such as unlocked characters and relic option binding, where every
    /// entry is authoritative once its full ModelKey and source ordinal are preserved.
    /// </summary>
    private static ModelListCapture ReadStaticRuntimeModelKeysWorld(Type owner, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = ReadStaticMemberWorld(owner, name);
            ModelListCapture capture = CaptureRuntimeModelKeyListWorld(value);
            if (capture.Keys.Count > 0) return capture;
        }
        return new ModelListCapture(Array.Empty<ModelKey>(), false);
    }

    private static ModelListCapture CaptureRuntimeModelKeyListWorld(object? value)
    {
        List<object> models = Enumerate(value).ToList();
        if (models.Count == 0) return new ModelListCapture(Array.Empty<ModelKey>(), false);

        var keys = new List<ModelKey>(models.Count);
        bool exact = true;
        foreach (object model in models)
        {
            if (!TryModelKey(model, out ModelKey key))
            {
                exact = false;
                continue;
            }
            keys.Add(key);
        }

        return new ModelListCapture(keys, exact && keys.Count == models.Count);
    }

    private static ModelListCapture CaptureModelListWorld(object? value)
    {
        List<object> models = Enumerate(value).ToList();
        if (models.Count == 0) return new ModelListCapture(Array.Empty<ModelKey>(), false);
        var keys = new List<ModelKey>(models.Count);
        bool exact = true;
        foreach (object model in models)
        {
            if (!TryModelKey(model, out ModelKey key))
            {
                exact = false;
                continue;
            }
            keys.Add(key);
            exact &= IsOfficialSts2ModelWorld(model);
        }
        return new ModelListCapture(keys, exact && keys.Count == models.Count);
    }

    private static object? ReadStaticMemberWorld(Type type, string name)
    {
        try
        {
            return type.GetProperty(name, PublicStatic | BindingFlags.NonPublic)?.GetValue(null) ??
                   type.GetField(name, PublicStatic | BindingFlags.NonPublic)?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadKeyValueWorld(object item, out int key, out object? value)
    {
        key = 0;
        value = null;
        object? rawKey = item is DictionaryEntry entry ? entry.Key : ReadProperty(item, "Key");
        value = item is DictionaryEntry dictionaryEntry ? dictionaryEntry.Value : ReadProperty(item, "Value");
        if (rawKey is int direct)
        {
            key = direct;
            return value is not null;
        }
        if (rawKey is IConvertible convertible)
        {
            try
            {
                key = convertible.ToInt32(System.Globalization.CultureInfo.InvariantCulture);
                return value is not null;
            }
            catch
            {
            }
        }
        return false;
    }

    private static WorldActSnapshot[] ApplyKnownActOrderWorld(WorldActSnapshot[] source, int act)
    {
        if (!LegacyActOrderByIndex.TryGetValue(act, out string[]? order))
        {
            return source.Select(item => item with { GenerationInputsExact = false }).ToArray();
        }
        var rank = order.Select((entry, index) => (entry, index))
            .ToDictionary(item => NormalizeTokenWorld(item.entry), item => item.index, StringComparer.Ordinal);
        bool exact = source.Length == order.Length &&
                     source.All(item => rank.ContainsKey(NormalizeTokenWorld(item.ActKey.Entry)));
        return source
            .OrderBy(item => rank.GetValueOrDefault(NormalizeTokenWorld(item.ActKey.Entry), int.MaxValue))
            .ThenBy(item => item.ActKey.Entry, StringComparer.Ordinal)
            .Select(item => item with { GenerationInputsExact = item.GenerationInputsExact && exact })
            .ToArray();
    }

    private static (IReadOnlyList<ModelKey> Ordered, bool Exact) ApplyKnownOrderWorld(
        IReadOnlyList<ModelKey> source,
        IReadOnlyList<string>? order,
        bool sourceExact)
    {
        if (order is null || order.Count == 0 || source.Count == 0) return (source, false);
        var rank = order.Select((entry, index) => (entry, index))
            .ToDictionary(item => NormalizeTokenWorld(item.entry), item => item.index, StringComparer.Ordinal);
        bool exact = sourceExact && source.Count == order.Count &&
                     source.All(key => rank.ContainsKey(NormalizeTokenWorld(key.Entry)));
        IReadOnlyList<ModelKey> ordered = source
            .OrderBy(key => rank.GetValueOrDefault(NormalizeTokenWorld(key.Entry), int.MaxValue))
            .ThenBy(key => key.Entry, StringComparer.Ordinal)
            .ToArray();
        return (ordered, exact);
    }

    private static int InferActIndexWorld(string entry)
    {
        string normalized = NormalizeTokenWorld(entry);
        if (normalized is "OVERGROWTH" or "UNDERDOCKS") return 1;
        if (normalized == "HIVE") return 2;
        if (normalized == "GLORY") return 3;
        return 0;
    }

    private static int? KnownWeakEncounterSlotsWorld(string entry) => NormalizeTokenWorld(entry) switch
    {
        "OVERGROWTH" => 3,
        "UNDERDOCKS" => 3,
        "HIVE" => 2,
        "GLORY" => 2,
        _ => null
    };

    private static int? KnownTotalNormalRoomsWorld(string entry) => NormalizeTokenWorld(entry) switch
    {
        "OVERGROWTH" => 15,
        "UNDERDOCKS" => 15,
        "HIVE" => 14,
        "GLORY" => 13,
        _ => null
    };

    private static bool IsOfficialSts2ModelWorld(object model)
    {
        Type type = model.GetType();
        if (string.Equals(type.Name, "ModelId", StringComparison.Ordinal))
        {
            return string.Equals(type.Assembly.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(type.Assembly.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTokenWorld(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static IEnumerable<string> DescribeAct(WorldActSnapshot act)
    {
        yield return $"act:{act.Act}:{act.ActKey.Serialized}:{act.WeakEncounterSlots}:{act.TotalNormalRooms}:{act.GenerationInputsExact}";
        foreach (ModelKey key in act.Events) yield return "event:" + key.Serialized;
        foreach (ModelKey key in act.OrderedRawEvents) yield return "raw-event:" + key.Serialized;
        foreach (ModelKey key in act.OrderedEligibleEvents) yield return "eligible-event:" + key.Serialized;
        yield return $"event-authority:{act.RawEventCatalogOrderExact}/{act.EventEpochMembershipExact}/{act.EventEpochRevealFactsExact}/{act.EligibleEventOrderExact}";
        foreach (WorldEncounterSnapshot item in act.WeakEncounters) yield return "weak:" + item.EncounterKey.Serialized + ":" + string.Join(",", item.Tags);
        foreach (WorldEncounterSnapshot item in act.RegularEncounters) yield return "regular:" + item.EncounterKey.Serialized + ":" + string.Join(",", item.Tags);
        foreach (WorldEncounterSnapshot item in act.EliteEncounters) yield return "elite:" + item.EncounterKey.Serialized + ":" + string.Join(",", item.Tags);
        foreach (ModelKey key in act.Bosses) yield return "boss:" + key.Serialized;
        foreach (ModelKey key in act.Ancients) yield return "ancient:" + key.Serialized;
    }

    private static Beta109WorldGenerationSnapshot BuildBeta109WorldGenerationSnapshot(
        IRuntimeProfile profile,
        string rawSeed,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        bool? noRunModifiers,
        Beta109StaticAuthorityCapture authority,
        NeowEffectAuthoritySnapshot? effects,
        IReadOnlyList<ModelKey> ancientOptionRelicCatalog,
        bool ancientOptionRelicCatalogExact,
        IReadOnlyList<ModelKey> unlockedCharacters,
        bool unlockedCharactersExact,
        bool unlockStateExact,
        string unlockSource,
        string baseCatalogFingerprint,
        string gameVersion)
    {
        bool oldSeed = !string.IsNullOrWhiteSpace(rawSeed) &&
                       rawSeed.StartsWith("old", StringComparison.OrdinalIgnoreCase);
        string canonicalSeed = string.Empty;
        bool canonical = !oldSeed && profile.TryCanonicalizeSeed(rawSeed, out canonicalSeed, out _);
        ulong actSelectionRoot = oldSeed
            ? XxHash64.HashUtf8(rawSeed, 0UL)
            : canonical ? profile.ComputeRootSeed(canonicalSeed) : 0UL;
        ulong runSeedRoot = canonical ? profile.ComputeRootSeed(canonicalSeed) : 0UL;
        bool actSelectionRootExact = oldSeed || canonical;
        bool runSeedRootExact = canonical;
        Beta109RunSeedHashKind runSeedHashKind = canonical
            ? Beta109RunSeedHashKind.ModernXxHash64
            : oldSeed ? Beta109RunSeedHashKind.LegacyOldHash32 : Beta109RunSeedHashKind.Unknown;
        Beta109OldSeedBranchStatus oldSeedStatus = canonical
            ? Beta109OldSeedBranchStatus.NewSeedHashed
            : oldSeed ? Beta109OldSeedBranchStatus.UnsupportedOldSeed : Beta109OldSeedBranchStatus.Unknown;

        IReadOnlyList<Beta109AncientEventContextSnapshot> ancientEventContexts =
            BuildBeta109AncientEventContexts(
                profile.ProfileId,
                authority,
                effects,
                ancientOptionRelicCatalog,
                ancientOptionRelicCatalogExact,
                runSeedRoot,
                runSeedRootExact,
                character,
                playerSlotIndex,
                noRunModifiers,
                unlockedCharacters,
                unlockedCharactersExact);

        string catalogFingerprint = Fingerprint(
            profile.ProfileId switch
            {
                RuntimeProfileId.Beta111 => "beta111-runtime-world-catalog-v1",
                RuntimeProfileId.Beta110 => "beta110-runtime-world-catalog-v1",
                _ => "beta109-runtime-world-catalog-v5"
            },
            authority.OrderedActs.SelectMany(act => new[]
                {
                    $"act:{act.Act}:{act.ActKey.Serialized}:{act.CatalogOrderExact}:{act.RoomShapeExact}",
                    "raw-events:" + string.Join(",", act.OrderedRawEvents.Select(key => key.Serialized)),
                    "eligible-events:" + string.Join(",", act.OrderedEligibleEvents.Select(key => key.Serialized)),
                    $"event-authority:{act.RawEventCatalogOrderExact}/{act.EventEpochMembershipExact}/{act.EventEpochRevealFactsExact}/{act.EligibleEventOrderExact}",
                    "encounters:" + string.Join(",", act.OrderedGenerateAllEncounters.Select(item =>
                        $"{item.SourceOrdinal}/{item.ReferenceIdentityId}/{item.EncounterKey.Serialized}/" +
                        $"{item.RoomType}/{item.IsWeak}/{item.Weight}/" +
                        string.Join("+", item.ExactTagConflictSourceOrdinals))),
                    $"encounter-authority:{act.GenerateAllEncountersOrderExact}/" +
                    $"{act.EncounterClassificationExact}/{act.EncounterTagIdentityExact}/" +
                    $"{act.EncounterTagComparerExact}/{act.EncounterReferenceIdentityExact}/" +
                    $"{act.EncounterWeightModelExact}/{act.EncounterRetryShapeExact}/" +
                    $"{act.EliteEncounterSlots}/{act.EncounterCatalogOfficialVanilla}/" +
                    act.EncounterAuthorityEvidenceCode,
                    "ancients:" + string.Join(",", act.OrderedAncients.Select(key => key.Serialized))
                })
                .Concat(authority.SharedEvents.Select(key => "shared-event:" + key.Serialized))
                .Concat(authority.EventAuthority.EpochsInFilterOrder.Select(epoch =>
                    "event-epoch:" + epoch.EpochId + ":" + epoch.IsRevealed + ":" +
                    epoch.MembershipExact + ":" + epoch.RevealFactExact + ":" +
                    string.Join(",", epoch.OrderedMemberKeys.Select(key => key.Serialized))))
                .Concat(authority.AllSharedAncients.Select(key => "all-shared-ancient:" + key.Serialized))
                .Concat(authority.UnlockedSharedAncients.Select(key => "unlocked-shared-ancient:" + key.Serialized))
                .Concat(authority.SharedRelicBuckets.Select(bucket =>
                    "shared-relic-bucket:" + bucket.BucketId + ":" + string.Join(",", bucket.OrderedEntries.Select(entry =>
                        $"{entry.RelicKey.Serialized}/{entry.IsAllowedInShops}/{entry.ShopEligibilityExact}"))))
                .Concat(authority.PlayerRelicBuckets.Select(bucket =>
                    "player-relic-bucket:" + bucket.BucketId + ":" + string.Join(",", bucket.OrderedEntries.Select(entry =>
                        $"{entry.RelicKey.Serialized}/{entry.IsAllowedInShops}/{entry.ShopEligibilityExact}"))))
                .Concat(ancientEventContexts.Select(context =>
                    "ancient-option-catalog:" + context.Act + ":" + context.AncientKey.Serialized + ":" +
                    context.IsShared + ":" + (context.Catalog?.CatalogFingerprint ?? "missing")))
                .Append("base-catalog:" + baseCatalogFingerprint));
        string unlockFingerprint = Fingerprint(
            profile.ProfileId switch
            {
                RuntimeProfileId.Beta111 => "beta111-world-unlock-v1",
                RuntimeProfileId.Beta110 => "beta110-world-unlock-v1",
                _ => "beta109-world-unlock-v2"
            }, new[]
        {
            unlockStateExact.ToString(),
            unlockSource,
            authority.UnlockFactsExact.ToString(),
            string.Join(",", unlockedCharacters.Select(key => key.Serialized)),
            string.Join(",", authority.UnlockedSharedAncients.Select(key => key.Serialized)),
            string.Join(";", authority.ActSelectionGroups.Select(group =>
                $"{group.Act}:{group.SelectionMode}:{string.Join(",", group.EligibleActsInSourceOrder.Select(key => key.Serialized))}:" +
                string.Join(",", group.Candidates.Select(candidate =>
                    $"{candidate.CandidateOrdinal}/{candidate.ActKey.Serialized}/{candidate.IsDefault}/{candidate.IsUnlocked}/{candidate.DiscoveredInSingleplayer}/{candidate.DeterministicFirstEligible}/{candidate.PredicateExact}")))),
            authority.IsMultiplayer.ToString(),
            authority.TestModeIsOff.ToString(),
            authority.Act1OverrideRaw,
            string.Join(",", authority.LobbyPlayers.Select(player =>
                $"{player.Slot}/{player.CharacterKey.Serialized}/{player.IsRandomCharacter}/{player.Exact}")),
            string.Join(",", authority.AllCharactersInSourceOrder.Select(key => key.Serialized)),
            string.Join(",", unlockedCharacters.Select(key => key.Serialized)),
            unlockedCharactersExact.ToString()
        });
        Beta109RuntimeValidationAuthorityDecision acceptedValidation =
            Beta109AcceptedRuntimeValidationAuthority.Resolve(gameVersion);
        IReadOnlyList<string> acceptedFixtureIds = acceptedValidation.FixtureIds;
        Beta109FixtureValidationStatus acceptedFixtureStatus = acceptedValidation.Status;

        string generationRuleFingerprint = Fingerprint(
            profile.ProfileId switch
            {
                RuntimeProfileId.Beta111 => "beta111-world-generation-rules-v1",
                RuntimeProfileId.Beta110 => "beta110-world-generation-rules-v1",
                _ => "beta109-world-generation-rules-v6"
            }, new[]
        {
            "AG-011:DirectSourceAudited",
            "AG-012:DirectSourceAudited",
            "runtime-authority-capture:DirectSourceAudited",
            "act_selection:lobby-local",
            "run-root:separate-from-act-selection-root",
            "identity:up_front",
            "options:event-local",
            "relic-bags:donor-first-first-seen-rarity-order",
            "relic-bags:shared-all-rarities",
            "relic-bags:player-shared-plus-character-filter-normal-shop",
            "shared-ancients:unlock-state-subset",
            "events:act-all-events+shared-events",
            "events:epoch-filter-event1>event2>event3",
            "events:unstable-shuffle-n-minus-one",
            "encounters:vanilla-generate-all-order",
            "encounters:monster-isweak-classification",
            "encounters:uniform-weight-one",
            "encounters:full-bag-rejection-fallback",
            "encounters:source-reference-identity",
            "ancient-options:eventmodel-beginevent-local-root",
            "ancient-options:generated-order-is-display-order",
            "ancient-options:darv-modern-eleven-valid-sets",
            "ancient-options:route-dependent-opening-projection-partial",
            "a10-second-boss:exclude-first",
            "accepted-runtime-validation:" + acceptedValidation.EvidenceCode,
            "accepted-runtime-fixtures:" + string.Join(",", acceptedFixtureIds)
        });
        Beta109UpFrontPrefixSnapshot prefix = authority.RelicInitializationExact &&
                                                authority.ModeFactsExact &&
                                                authority.IsVanilla &&
                                                noRunModifiers == true &&
                                                runSeedRootExact
            ? new Beta109UpFrontPrefixSnapshot(
                Beta109UpFrontPrefixAuthorityKind.ReplayFromRunStart,
                Beta109UpFrontReplayOrigin.RunStartBeforeRelicInitialization,
                Checkpoint: null,
                PriorInputsExact: true,
                EvidenceCode: "RuntimeCatalogReplayFromRunStart")
            : Beta109UpFrontPrefixSnapshot.Missing(
                !authority.RelicInitializationExact
                    ? "MissingRelicInitialization"
                    : !authority.ModeFactsExact
                        ? "MissingModeFacts"
                        : noRunModifiers != true
                            ? "UnknownHookOrModifier"
                            : "MissingRunSeedRoot");
        string snapshotFingerprint = Fingerprint(
            profile.ProfileId switch
            {
                RuntimeProfileId.Beta111 => "beta111-world-snapshot-v1",
                RuntimeProfileId.Beta110 => "beta110-world-snapshot-v1",
                _ => "beta109-world-snapshot-v6"
            }, new[]
        {
            canonicalSeed,
            actSelectionRoot.ToString(System.Globalization.CultureInfo.InvariantCulture),
            runSeedRoot.ToString(System.Globalization.CultureInfo.InvariantCulture),
            runSeedHashKind.ToString(),
            character.CharacterKey.Serialized,
            ascension.ToString(System.Globalization.CultureInfo.InvariantCulture),
            playersCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            playerSlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            authority.Act1OverrideRaw,
            authority.GameMode.ToString(),
            catalogFingerprint,
            unlockFingerprint,
            generationRuleFingerprint,
            GameVersionDetector.NormalizeVersion(gameVersion),
            acceptedFixtureStatus.ToString(),
            string.Join(",", acceptedFixtureIds),
            authority.DiagnosticCode
        });

        return new Beta109WorldGenerationSnapshot(
            Profile: profile.ProfileId,
            OriginalSeed: rawSeed ?? string.Empty,
            CanonicalSeed: canonicalSeed,
            ActSelectionRoot: actSelectionRoot,
            ActSelectionRootExact: actSelectionRootExact,
            RunSeedRoot: runSeedRoot,
            RunSeedRootExact: runSeedRootExact,
            RunSeedHashKind: runSeedHashKind,
            OldSeedBranchStatus: oldSeedStatus,
            SelectedActs: Array.Empty<ModelKey>(),
            SelectedActProvenance: SelectedActProvenance.Missing,
            SelectedActsExact: false,
            ActSelectionGroups: authority.ActSelectionGroups,
            ActSelectionAuthorityExact: authority.ActSelectionAuthorityExact,
            IsMultiplayer: authority.IsMultiplayer,
            IsMultiplayerExact: authority.IsMultiplayerExact,
            TestModeIsOff: authority.TestModeIsOff,
            TestModeFactExact: authority.TestModeFactExact,
            TutorialBossOverrideWillApply: authority.TutorialBossOverrideWillApply,
            TutorialBossOverrideAuthorityExact: authority.TutorialBossOverrideAuthorityExact,
            Act1OverrideRaw: authority.Act1OverrideRaw,
            Act1OverrideResolvedKey: authority.Act1OverrideResolvedKey,
            Act1OverrideExact: authority.Act1OverrideExact,
            LobbyPlayers: authority.LobbyPlayers,
            AllCharactersInSourceOrder: authority.AllCharactersInSourceOrder,
            RandomCharacterAuthorityExact: authority.RandomCharacterAuthorityExact,
            UnlockedCharacters: unlockedCharacters,
            UnlockedCharactersExact: unlockedCharactersExact,
            CharacterKey: character.CharacterKey,
            Ascension: ascension,
            GameMode: authority.GameMode,
            ModeFactsExact: authority.ModeFactsExact,
            PlayerCount: Math.Max(1, playersCount),
            OrderedActCatalog: authority.OrderedActs,
            SharedEvents: authority.SharedEvents,
            SharedEventCatalogExact: authority.SharedEventCatalogExact,
            AllSharedAncients: authority.AllSharedAncients,
            AllSharedAncientCatalogExact: authority.AllSharedAncientCatalogExact,
            SharedAncients: authority.UnlockedSharedAncients,
            SharedAncientCatalogExact: authority.UnlockedSharedAncientCatalogExact,
            UnlockFactsExact: authority.UnlockFactsExact,
            SharedRelicBuckets: authority.SharedRelicBuckets,
            PlayerRelicBuckets: authority.PlayerRelicBuckets,
            RelicInitializationExact: authority.RelicInitializationExact,
            UpFrontPrefix: prefix,
            AncientEventContexts: ancientEventContexts,
            NoUnknownHooksOrModifiers: noRunModifiers == true && character.IsKnownVanilla && authority.IsVanilla,
            IsVanilla: authority.IsVanilla,
            DirectSourceAudited: true,
            FixtureValidationStatus: acceptedFixtureStatus,
            VerifiedRealGameFixtureIds: acceptedFixtureIds,
            CatalogFingerprint: catalogFingerprint,
            UnlockFingerprint: unlockFingerprint,
            GenerationRuleFingerprint: generationRuleFingerprint,
            SnapshotFingerprint: snapshotFingerprint,
            CaptureDiagnosticCode: authority.DiagnosticCode)
        {
            RequestedGameMode = authority.RequestedGameMode,
            RequestedGameModeExact = authority.RequestedGameModeExact,
            GameModeEvidenceCode = authority.GameModeEvidenceCode,
            EventAuthority = authority.EventAuthority,
            SharedRelicPoolOrderExact = authority.SharedRelicPoolOrderExact,
            CharacterRelicPoolOrderExact = authority.CharacterRelicPoolOrderExact,
            WorldGenerationHooksExact = noRunModifiers == true && authority.IsVanilla,
            RelicRarityAuthorityExact = authority.RelicRarityAuthorityExact,
            RelicShopEligibilityAuthorityExact = authority.RelicShopEligibilityAuthorityExact,
            PlayerRelicPoolCompositionExact = authority.PlayerRelicPoolCompositionExact,
            RelicAuthorityEvidenceCode = authority.RelicAuthorityEvidenceCode
        };
    }

    private static string ModernWorldEvidencePrefix(RuntimeProfileId profileId, string suffix) => profileId switch
    {
        RuntimeProfileId.Beta111 => "beta111-" + suffix,
        RuntimeProfileId.Beta110 => "beta110-" + suffix,
        _ => "beta109-historical-" + suffix
    };

    private static WorldAuthoritySnapshot MissingWorld(RuntimeProfileId profileId, string issue) => new(
        profileId,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        null,
        false,
        false,
        false,
        SourceAuthority.Unknown,
        SnapshotCompleteness.Missing,
        string.Empty,
        string.Empty,
        null,
        issue);

    private sealed record ModelListCapture(IReadOnlyList<ModelKey> Keys, bool Exact);
    private sealed record EncounterListCapture(IReadOnlyList<WorldEncounterSnapshot> Items, bool Exact);
}
