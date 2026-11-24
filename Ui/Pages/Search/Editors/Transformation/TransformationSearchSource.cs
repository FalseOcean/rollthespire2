using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed record TransformationSearchSource(string Id, ModelKey Identity, int ResultCount, ModelKey? OtherRelic = null)
{
    internal const string AstrolabeId = "A.ASTROLABE";
    internal const string PandoraId = "A.PANDORAS_BOX";

    internal int AncientAct { get; init; }
    internal int EventAct { get; init; }
    internal bool HasIndependentResult { get; init; }
    internal int? PandoraCountOverride { get; init; }
    internal bool IsAncient => IsAncientId(Id);
    internal string AncientPremiseKey => Id == AstrolabeId
        ? "query.transform.ancient.premise.astrolabe" : "query.transform.ancient.premise.pandora";

    internal static bool IsAncientId(string id) => id is AstrolabeId or PandoraId;
    internal static string? AncientSourceId(ModelKey key) => key.Entry switch
    {
        "ASTROLABE" => AstrolabeId,
        "PANDORAS_BOX" => PandoraId,
        _ => null
    };
}

internal sealed record AncientTransformationResult(TransformationSearchSource Source, IReadOnlyList<ModelKey> Cards)
{
    internal bool IsAvailable { get; init; } = true;
}
