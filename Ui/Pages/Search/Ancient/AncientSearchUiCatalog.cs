using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Ui.Pages.Search.Ancient;

internal enum AncientAdditionalConditionKind
{
    None,
    SeaGlassCharacterTarget
}

internal sealed record AncientOptionCandidate(
    ModelKey OptionKey,
    bool IsAvailable,
    AncientAdditionalConditionKind AdditionalConditionKind,
    string EvidenceCode);

internal sealed record AncientRowDefinition(
    int Act,
    ModelKey AncientKey,
    IReadOnlyList<AncientOptionCandidate> Options,
    IReadOnlyDictionary<ModelKey, string> OfferExclusionGroups,
    bool IdentityAvailable,
    string EvidenceCode)
{
    public bool CanOfferTogether(IEnumerable<ModelKey> targets)
    {
        ModelKey[] selected = targets.Distinct(ModelKeyComparer.Instance).ToArray();
        if (selected.Length > 3 || selected.Any(key => !Options.Any(option => option.OptionKey == key))) return false;
        return selected
            .Select(key => OfferExclusionGroups.TryGetValue(key, out string? group) ? group : string.Empty)
            .Where(group => !string.IsNullOrEmpty(group))
            .GroupBy(group => group, StringComparer.Ordinal)
            .All(group => group.Count() == 1);
    }
}

internal sealed record AncientActSectionDefinition(
    int Act,
    IReadOnlyList<AncientRowDefinition> Rows);

/// <summary>
/// Main-thread UI metadata derived from the immutable world-authority capture.
/// Modern runtime option candidates come directly from the production option catalogs.
/// Stable107 candidates mirror the directly audited arrays in
/// Stable107AncientOptionPredictor and are rebound to the runtime relic catalog.
/// This type never predicts an Ancient or advances RNG.
/// </summary>
internal sealed record AncientSearchUiCatalog(
    RuntimeProfileId ProfileId,
    ModelKey CharacterKey,
    IReadOnlyList<AncientActSectionDefinition> Sections,
    IReadOnlyList<ModelKey> SeaGlassTargetCharacters,
    bool CharacterTargetAuthorityExact,
    string EvidenceCode)
{
    private static readonly string[] Act2AncientEntries = { "OROBAS", "PAEL", "TEZCATARA" };
    private static readonly string[] Act3AncientEntries = { "NONUPEIPE", "TANX", "VAKUU" };

    public static AncientSearchUiCatalog Empty(
        RuntimeProfileId profileId,
        ModelKey characterKey,
        string evidenceCode) =>
        new(profileId, characterKey, Array.Empty<AncientActSectionDefinition>(),
            Array.Empty<ModelKey>(), false, evidenceCode);

    public static AncientSearchUiCatalog FromAuthority(
        RuntimeProfileId profileId,
        ModelKey characterKey,
        WorldAuthoritySnapshot? world)
    {
        if (world is null)
        {
            RuntimeLog.Warn(
                $"Ancient UI catalog unavailable: profile={profileId}; character={characterKey.Serialized}; " +
                "reason=world-authority-missing");
            return Empty(profileId, characterKey, "ancient-search-ui-world-authority-missing");
        }

        IReadOnlyList<ModelKey> runtimeRelics = world.AncientOptionRelicCatalog ?? Array.Empty<ModelKey>();
        var sections = new List<AncientActSectionDefinition>(2);
        foreach (int act in new[] { 2, 3 })
        {
            IReadOnlyList<ModelKey> actAncients = ResolveActAncients(profileId, world, act);
            var rows = new List<AncientRowDefinition>(4);
            foreach (string entry in act == 2 ? Act2AncientEntries : Act3AncientEntries)
            {
                ModelKey key = BindAncient(entry, actAncients, out bool exact);
                rows.Add(BuildRow(profileId, world, runtimeRelics, act, key, exact));
            }

            IReadOnlyList<ModelKey> shared = ResolveSharedAncients(profileId, world);
            ModelKey darv = BindAncient("DARV", shared, out bool darvExact);
            rows.Add(BuildRow(profileId, world, runtimeRelics, act, darv, darvExact));
            sections.Add(new AncientActSectionDefinition(act, rows));
        }

        IReadOnlyList<ModelKey> targets = (world.UnlockedCharacters ?? Array.Empty<ModelKey>())
            .Where(key => key.IsValid &&
                          key.Category == BaseGameModelKeys.Categories.Character &&
                          key != characterKey)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();

        AncientRowDefinition[] allRows = sections.SelectMany(section => section.Rows).ToArray();
        foreach (AncientRowDefinition row in allRows.Where(row =>
                     !row.IdentityAvailable || row.Options.Count == 0))
        {
            RuntimeLog.Warn(
                $"Ancient UI row pending: act={row.Act}; ancient={row.AncientKey.Serialized}; " +
                $"identityAvailable={row.IdentityAvailable.ToString().ToLowerInvariant()}; " +
                $"options={row.Options.Count}; reason={row.EvidenceCode}");
        }

        RuntimeLog.Ui(
            $"Ancient UI catalog prepared: profile={profileId}; character={characterKey.Serialized}; " +
            $"sections={sections.Count}; rows={allRows.Length}; " +
            $"options={allRows.Sum(row => row.Options.Count)}; " +
            $"pendingRows={allRows.Count(row => !row.IdentityAvailable || row.Options.Count == 0)}; " +
            $"targetCharacters={targets.Count}; targetAuthorityExact={world.UnlockedCharactersExact.ToString().ToLowerInvariant()}");

        return new AncientSearchUiCatalog(
            profileId,
            characterKey,
            sections,
            targets,
            world.UnlockedCharactersExact,
            RuntimeProfilePolicies.IsModernCore(profileId)
                ? profileId switch
                {
                    RuntimeProfileId.Beta111 => "beta111-ancient-search-ui-from-event-context-catalog-owner-smoke-pending",
                    RuntimeProfileId.Beta110 => "beta110-ancient-search-ui-from-event-context-catalog-pending-validation",
                    _ => "beta109-historical-ancient-search-ui-from-event-context-catalog"
                }
                : "stable107-ancient-search-ui-from-audited-predictor-catalog");
    }

    private static AncientRowDefinition BuildRow(
        RuntimeProfileId profileId,
        WorldAuthoritySnapshot world,
        IReadOnlyList<ModelKey> runtimeRelics,
        int act,
        ModelKey ancientKey,
        bool identityExact)
    {
        if (!identityExact)
        {
            return new AncientRowDefinition(
                act,
                ancientKey,
                Array.Empty<AncientOptionCandidate>(),
                new Dictionary<ModelKey, string>(ModelKeyComparer.Instance),
                false,
                "ancient-identity-runtime-binding-pending");
        }

        if (RuntimeProfilePolicies.IsModernCore(profileId))
        {
            Beta109AncientEventContextSnapshot? context = world.Beta109Generation?.AncientEventContexts
                .FirstOrDefault(item => item.Act == act && item.AncientKey == ancientKey);
            if (context?.Catalog is null)
            {
                return new AncientRowDefinition(
                    act,
                    ancientKey,
                    Array.Empty<AncientOptionCandidate>(),
                    new Dictionary<ModelKey, string>(ModelKeyComparer.Instance),
                    true,
                    profileId switch
                    {
                        RuntimeProfileId.Beta111 => "beta111-ancient-option-catalog-owner-smoke-pending",
                        RuntimeProfileId.Beta110 => "beta110-ancient-option-catalog-pending-validation",
                        _ => "beta109-historical-ancient-option-catalog-pending"
                    });
            }

            AncientOptionCandidate[] options = context.Catalog.Pools
                .OrderBy(pool => pool.SourceOrdinal < 0 ? int.MaxValue : pool.SourceOrdinal)
                .ThenBy(pool => pool.PoolId, StringComparer.Ordinal)
                .SelectMany(pool => pool.OrderedOptions)
                .Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance)
                .Select(key => Candidate(
                    key,
                    context.Catalog.CatalogExact,
                    profileId switch
                    {
                        RuntimeProfileId.Beta111 => "beta111-event-context-option-catalog-owner-smoke-pending",
                        RuntimeProfileId.Beta110 => "beta110-event-context-option-catalog-pending-validation",
                        _ => "beta109-historical-event-context-option-catalog"
                    }))
                .ToArray();
            IReadOnlyDictionary<ModelKey, string> groups = BuildOfferExclusionGroups(
                ancientKey, context.Catalog.Pools);
            return new AncientRowDefinition(
                act,
                ancientKey,
                options,
                groups,
                true,
                context.Catalog.CatalogFingerprint);
        }

        string[] entries = LegacyOptionEntries(ancientKey.Entry, act);
        var legacyOptions = new List<AncientOptionCandidate>(entries.Length);
        var legacyGroups = new Dictionary<ModelKey, string>(ModelKeyComparer.Instance);
        foreach (string entry in entries)
        {
            ModelKey key = BindRelic(
                entry,
                runtimeRelics,
                world.AncientOptionRelicCatalogExact,
                out bool exact);
            legacyOptions.Add(Candidate(
                key,
                exact,
                exact
                    ? "stable107-runtime-option-key-bound"
                    : "stable107-runtime-option-key-pending"));
            string group = LegacyOfferExclusionGroup(ancientKey.Entry, entry, act);
            if (!string.IsNullOrEmpty(group)) legacyGroups[key] = group;
        }
        return new AncientRowDefinition(
            act,
            ancientKey,
            legacyOptions.ToArray(),
            legacyGroups,
            true,
            "stable107-direct-predictor-option-catalog");
    }

    private static AncientOptionCandidate Candidate(ModelKey key, bool available, string evidence) =>
        new(
            key,
            available,
            string.Equals(key.Entry, "SEA_GLASS", StringComparison.Ordinal)
                ? AncientAdditionalConditionKind.SeaGlassCharacterTarget
                : AncientAdditionalConditionKind.None,
            evidence);

    private static IReadOnlyDictionary<ModelKey, string> BuildOfferExclusionGroups(
        ModelKey ancient,
        IReadOnlyList<Beta109NamedOptionPoolSnapshot> pools)
    {
        string normalized = Normalize(ancient.Entry);
        var groups = new Dictionary<ModelKey, string>(ModelKeyComparer.Instance);
        foreach (Beta109NamedOptionPoolSnapshot pool in pools)
        {
            string group = ModernOfferExclusionGroup(normalized, pool.PoolId);
            if (string.IsNullOrEmpty(group)) continue;
            foreach (ModelKey option in pool.OrderedOptions)
                groups[option] = group;
        }
        return groups;
    }

    private static string ModernOfferExclusionGroup(string ancient, string poolId) => ancient switch
    {
        "OROBAS" when poolId.StartsWith("orobas.pool1.", StringComparison.Ordinal) => "orobas.slot1",
        "OROBAS" when poolId == "orobas.pool2" => "orobas.slot2",
        "OROBAS" when poolId.StartsWith("orobas.pool3.", StringComparison.Ordinal) => "orobas.slot3",
        "PAEL" when poolId == "pael.pool1" => "pael.slot1",
        "PAEL" when poolId.StartsWith("pael.pool2.", StringComparison.Ordinal) => "pael.slot2",
        "PAEL" when poolId.StartsWith("pael.pool3.", StringComparison.Ordinal) => "pael.slot3",
        "TEZCATARA" when poolId.StartsWith("tezcatara.pool1.", StringComparison.Ordinal) => "tezcatara.slot1",
        "TEZCATARA" when poolId == "tezcatara.pool2" => "tezcatara.slot2",
        "TEZCATARA" when poolId == "tezcatara.pool3" => "tezcatara.slot3",
        "VAKUU" when poolId == "vakuu.pool1" => "vakuu.slot1",
        "VAKUU" when poolId == "vakuu.pool2" => "vakuu.slot2",
        "VAKUU" when poolId == "vakuu.pool3" => "vakuu.slot3",
        "DARV" when poolId.StartsWith("darv.valid.", StringComparison.Ordinal) => poolId,
        _ => string.Empty
    };

    private static string LegacyOfferExclusionGroup(string ancient, string option, int act)
    {
        string normalizedAncient = Normalize(ancient);
        string normalizedOption = Normalize(option);
        return normalizedAncient switch
        {
            "OROBAS" when normalizedOption is "ELECTRICSHRYMP" or "GLASSEYE" or "SANDCASTLE" or "PRISMATICGEM" or "SEAGLASS" => "orobas.slot1",
            "OROBAS" when normalizedOption is "ALCHEMICALCOFFER" or "DRIFTWOOD" or "RADIANTPEARL" => "orobas.slot2",
            "OROBAS" when normalizedOption is "TOUCHOFOROBAS" or "ARCHAICTOOTH" => "orobas.slot3",
            "PAEL" when normalizedOption is "PAELSFLESH" or "PAELSHORN" or "PAELSTEARS" => "pael.slot1",
            "PAEL" when normalizedOption is "PAELSWING" or "PAELSCLAW" or "PAELSTOOTH" or "PAELSGROWTH" => "pael.slot2",
            "PAEL" when normalizedOption is "PAELSEYE" or "PAELSBLOOD" or "PAELSLEGION" => "pael.slot3",
            "TEZCATARA" when normalizedOption is "VERYHOTCOCOA" or "YUMMYCOOKIE" or "NUTRITIOUSSOUP" => "tezcatara.slot1",
            "TEZCATARA" when normalizedOption is "BIIIGHUG" or "STORYBOOK" or "TOASTYMITTENS" => "tezcatara.slot2",
            "TEZCATARA" when normalizedOption is "GOLDENCOMPASS" or "PUMPKINCANDLE" or "TOYBOX" or "SEALOFGOLD" => "tezcatara.slot3",
            "VAKUU" when normalizedOption is "BLOODSOAKEDROSE" or "WHISPERINGEARRING" or "FIDDLE" => "vakuu.slot1",
            "VAKUU" when normalizedOption is "PRESERVEDFOG" or "SERETALON" or "DISTINGUISHEDCAPE" => "vakuu.slot2",
            "VAKUU" when normalizedOption is "CHOICESPARADOX" or "MUSICBOX" or "LORDSPARASOL" or "JEWELEDMASK" => "vakuu.slot3",
            "DARV" when act == 2 && normalizedOption is "ECTOPLASM" or "SOZU" => "darv.act2.energy",
            "DARV" when act == 3 && normalizedOption is "PHILOSOPHERSSTONE" or "VELVETCHOKER" => "darv.act3.energy",
            _ => string.Empty
        };
    }

    private static IReadOnlyList<ModelKey> ResolveActAncients(
        RuntimeProfileId profileId,
        WorldAuthoritySnapshot world,
        int act)
    {
        if (RuntimeProfilePolicies.IsModernCore(profileId))
        {
            return world.Beta109Generation?.OrderedActCatalog
                .Where(item => item.Act == act)
                .SelectMany(item => item.OrderedAncients)
                .Where(key => !string.Equals(key.Entry, "NEOW", StringComparison.Ordinal) &&
                              !string.Equals(key.Entry, "DARV", StringComparison.Ordinal))
                .Distinct(ModelKeyComparer.Instance)
                .ToArray() ?? Array.Empty<ModelKey>();
        }

        return (world.ActGroups ?? Array.Empty<WorldActGroupSnapshot>())
            .Where(group => group.Act == act)
            .SelectMany(group => group.Acts)
            .SelectMany(item => item.Ancients)
            .Where(key => !string.Equals(key.Entry, "NEOW", StringComparison.Ordinal) &&
                          !string.Equals(key.Entry, "DARV", StringComparison.Ordinal))
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
    }

    private static IReadOnlyList<ModelKey> ResolveSharedAncients(
        RuntimeProfileId profileId,
        WorldAuthoritySnapshot world) =>
        (RuntimeProfilePolicies.IsModernCore(profileId)
                ? world.Beta109Generation?.SharedAncients
                : world.SharedAncients)
            ?.Where(key => !string.Equals(key.Entry, "NEOW", StringComparison.Ordinal))
            .Distinct(ModelKeyComparer.Instance)
            .ToArray() ?? Array.Empty<ModelKey>();

    private static ModelKey BindAncient(string entry, IReadOnlyList<ModelKey> candidates, out bool exact)
    {
        ModelKey[] matches = candidates
            .Where(key => key.IsValid &&
                          key.Category == BaseGameModelKeys.Categories.Event &&
                          string.Equals(Normalize(key.Entry), Normalize(entry), StringComparison.Ordinal))
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        exact = matches.Length == 1;
        return exact ? matches[0] : new ModelKey(BaseGameModelKeys.Categories.Event, entry);
    }

    private static ModelKey BindRelic(
        string entry,
        IReadOnlyList<ModelKey> runtimeRelics,
        bool catalogExact,
        out bool exact)
    {
        ModelKey[] matches = runtimeRelics
            .Where(key => key.IsValid &&
                          key.Category == BaseGameModelKeys.Categories.Relic &&
                          string.Equals(Normalize(key.Entry), Normalize(entry), StringComparison.Ordinal))
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        exact = catalogExact && matches.Length == 1;
        return matches.Length == 1
            ? matches[0]
            : new ModelKey(BaseGameModelKeys.Categories.Relic, entry);
    }

    private static string[] LegacyOptionEntries(string ancientEntry, int act) =>
        Normalize(ancientEntry) switch
        {
            "OROBAS" => new[]
            {
                "ELECTRIC_SHRYMP", "GLASS_EYE", "SAND_CASTLE", "PRISMATIC_GEM", "SEA_GLASS",
                "ALCHEMICAL_COFFER", "DRIFTWOOD", "RADIANT_PEARL", "TOUCH_OF_OROBAS", "ARCHAIC_TOOTH"
            },
            "PAEL" => new[]
            {
                "PAELS_FLESH", "PAELS_HORN", "PAELS_TEARS", "PAELS_WING", "PAELS_CLAW",
                "PAELS_TOOTH", "PAELS_GROWTH", "PAELS_EYE", "PAELS_BLOOD", "PAELS_LEGION"
            },
            "TEZCATARA" => new[]
            {
                "VERY_HOT_COCOA", "YUMMY_COOKIE", "NUTRITIOUS_SOUP", "BIIIG_HUG", "STORYBOOK",
                "TOASTY_MITTENS", "GOLDEN_COMPASS", "PUMPKIN_CANDLE", "TOY_BOX", "SEAL_OF_GOLD"
            },
            "NONUPEIPE" => new[]
            {
                "BLESSED_ANTLER", "BRILLIANT_SCARF", "DELICATE_FROND", "DIAMOND_DIADEM", "FUR_COAT",
                "GLITTER", "JEWELRY_BOX", "LOOMING_FRUIT", "SIGNET_RING", "BEAUTIFUL_BRACELET"
            },
            "TANX" => new[]
            {
                "CLAWS", "CROSSBOW", "IRON_CLUB", "MEAT_CLEAVER", "SAI", "SPIKED_GAUNTLETS",
                "TANXS_WHISTLE", "THROWING_AXE", "WAR_HAMMER", "TRI_BOOMERANG"
            },
            "VAKUU" => new[]
            {
                "BLOOD_SOAKED_ROSE", "WHISPERING_EARRING", "FIDDLE", "PRESERVED_FOG", "SERE_TALON",
                "DISTINGUISHED_CAPE", "CHOICES_PARADOX", "MUSIC_BOX", "LORDS_PARASOL", "JEWELED_MASK"
            },
            "DARV" when act == 2 => new[]
            {
                "ASTROLABE", "BLACK_STAR", "CALLING_BELL", "EMPTY_CAGE", "PANDORAS_BOX",
                "RUNIC_PYRAMID", "SNECKO_EYE", "ECTOPLASM", "SOZU", "DUSTY_TOME"
            },
            "DARV" when act == 3 => new[]
            {
                "ASTROLABE", "BLACK_STAR", "CALLING_BELL", "EMPTY_CAGE", "PANDORAS_BOX",
                "RUNIC_PYRAMID", "SNECKO_EYE", "PHILOSOPHERS_STONE", "VELVET_CHOKER", "DUSTY_TOME"
            },
            _ => Array.Empty<string>()
        };

    private static string Normalize(string value) =>
        new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
}
