using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed record AnalysisRequestDraft(
    string RawSeed,
    ModelKey CharacterKey,
    int Ascension,
    int PlayersCount = 1,
    int PlayerSlotIndex = 0)
{
    public AncientOptionConditionProfile AncientOptionConditions { get; init; } = AncientOptionConditionProfile.BroadDefault;
}
