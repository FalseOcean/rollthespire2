using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Ui.Pages.Search.BossMap;

internal sealed record BossMapVariantDefinition(
    int Act,
    ModelKey ActKey,
    IReadOnlyList<ModelKey> Bosses,
    bool IdentityAvailable,
    string EvidenceCode);

internal sealed record BossMapActSectionDefinition(
    int Act,
    IReadOnlyList<BossMapVariantDefinition> Variants,
    bool ShowSecondBoss);

/// <summary>
/// Main-thread UI metadata projected from the immutable production world catalog.
/// It does not select an Act, predict a Boss, or advance any RNG stream.
/// </summary>
internal sealed record BossMapSearchUiCatalog(
    RuntimeProfileId ProfileId,
    IReadOnlyList<BossMapActSectionDefinition> Sections,
    string EvidenceCode)
{
    private const int DoubleBossAscension = 10;

    public static BossMapSearchUiCatalog Empty(RuntimeProfileId profileId, string evidenceCode) =>
        new(profileId, Array.Empty<BossMapActSectionDefinition>(), evidenceCode);

    public static BossMapSearchUiCatalog FromAuthority(
        RuntimeProfileId profileId,
        int ascension,
        WorldAuthoritySnapshot? world)
    {
        if (world is null)
        {
            return Empty(profileId, "boss-map-search-ui-world-authority-missing");
        }

        IReadOnlyList<BossMapVariantDefinition> variants =
            RuntimeProfilePolicies.IsModernCore(profileId)
                ? BuildModern(world.Beta109Generation)
                : BuildLegacy(world);

        BossMapActSectionDefinition[] sections = variants
            .GroupBy(variant => variant.Act)
            .OrderBy(group => group.Key)
            .Select(group => new BossMapActSectionDefinition(
                group.Key,
                group.ToArray(),
                group.Key == 3 && ascension >= DoubleBossAscension))
            .ToArray();

        return new BossMapSearchUiCatalog(
            profileId,
            sections,
            RuntimeProfilePolicies.IsModernCore(profileId)
                ? "modern-world-runtime-act-and-boss-catalog"
                : "legacy-world-runtime-act-and-boss-catalog");
    }

    private static IReadOnlyList<BossMapVariantDefinition> BuildModern(
        Beta109WorldGenerationSnapshot? world)
    {
        if (world is null)
        {
            return Array.Empty<BossMapVariantDefinition>();
        }

        var output = new List<BossMapVariantDefinition>();
        foreach (Beta109ActSelectionGroupSnapshot group in world.ActSelectionGroups.OrderBy(item => item.Act))
        {
            IReadOnlyList<ModelKey> keys = group.EligibleActsInSourceOrder.Count > 0
                ? group.EligibleActsInSourceOrder
                : world.OrderedActCatalog
                    .Where(act => act.Act == group.Act)
                    .Select(act => act.ActKey)
                    .ToArray();

            foreach (ModelKey key in keys.Distinct(ModelKeyComparer.Instance))
            {
                Beta109ActGenerationSnapshot? act = world.OrderedActCatalog
                    .FirstOrDefault(candidate => candidate.Act == group.Act && candidate.ActKey == key);
                output.Add(new BossMapVariantDefinition(
                    group.Act,
                    key,
                    act?.Bosses ?? Array.Empty<ModelKey>(),
                    act is not null && act.Bosses.Count > 0,
                    act?.EncounterAuthorityEvidenceCode ?? "modern-act-catalog-entry-missing"));
            }
        }

        if (output.Count == 0)
        {
            output.AddRange(world.OrderedActCatalog
                .OrderBy(act => act.Act)
                .Select(act => new BossMapVariantDefinition(
                    act.Act,
                    act.ActKey,
                    act.Bosses,
                    act.Bosses.Count > 0,
                    act.EncounterAuthorityEvidenceCode)));
        }

        return output;
    }

    private static IReadOnlyList<BossMapVariantDefinition> BuildLegacy(WorldAuthoritySnapshot world)
    {
        if (world.ActGroups is null)
        {
            return Array.Empty<BossMapVariantDefinition>();
        }

        return world.ActGroups
            .OrderBy(group => group.Act)
            .SelectMany(group => group.Acts.Select(act => new BossMapVariantDefinition(
                group.Act,
                act.ActKey,
                act.Bosses,
                act.Bosses.Count > 0,
                act.GenerationInputsExact
                    ? "legacy-act-catalog-exact"
                    : "legacy-act-catalog-partial")))
            .ToArray();
    }
}
