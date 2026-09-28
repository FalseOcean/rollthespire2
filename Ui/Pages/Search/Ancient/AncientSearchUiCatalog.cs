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
    bool IdentityAvailable,
    string EvidenceCode);

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
            return new AncientRowDefinition(
                act,
                ancientKey,
                options,
                true,
                context.Catalog.CatalogFingerprint);
        }

        string[] entries = LegacyOptionEntries(ancientKey.Entry, act);
        var legacyOptions = new List<AncientOptionCandidate>(entries.Length);
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
        }
        return new AncientRowDefinition(
            act,
            ancientKey,
            legacyOptions.ToArray(),
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
