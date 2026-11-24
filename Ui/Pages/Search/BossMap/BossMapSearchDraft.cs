using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Pages.Search.BossMap;

internal sealed record BossMapVariantSearchDraft(
    int Act,
    ModelKey ActKey,
    bool HasSelectableVariant,
    bool IsVariantActive,
    IReadOnlyList<ModelKey> AllBossKeys,
    IReadOnlyList<ModelKey> FirstBossAny,
    IReadOnlyList<ModelKey> SecondBossAny);

internal sealed record BossMapSearchDraft(
    IReadOnlyList<BossMapVariantSearchDraft> Rows,
    bool CatalogBound,
    bool IncludeSecondAct3Boss)
{
    public static BossMapSearchDraft Empty { get; } =
        new(Array.Empty<BossMapVariantSearchDraft>(), false, false);
}
