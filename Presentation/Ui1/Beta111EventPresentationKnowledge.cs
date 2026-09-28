using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Presentation.Ui1;

/// <summary>
/// Beta111-only presentation knowledge for Event occurrence-condition badges/tooltips.
/// This is not Search/Probability/Planner authority and it must never participate in
/// Event queue generation or eligibility decisions. The catalog is sourced from the
/// 2026-08-18 Beta111 Event Knowledge Reference supplied for Predictor UI work.
/// </summary>
public static class Beta111EventPresentationKnowledge
{
    public const string CatalogVersion = "beta111-event-predictor-presentation-conditions-20260818-r2";
    public const string ConditionLocalizationPrefix = "ui1.analysis.event_condition.";
    public const string RuntimeConditionLocalizationPrefix = "ui1.analysis.event_runtime_condition.";

    private static readonly IReadOnlySet<string> DocumentedConditionalEvents = new HashSet<string>(
        new[]
        {
            "Amalgamator", "BrainLeech", "ByrdonisNest", "ColorfulPhilosophers",
            "ColossalFlower", "CrystalSphere", "DenseVegetation", "DollRoom",
            "EndlessConveyor", "FakeMerchant", "FieldOfManSizedHoles",
            "GraveOfTheForgotten", "JungleMazeAdventure", "LuminousChoir",
            "MorphicGrove", "PotionCourier", "PunchOff", "RanwidTheElder",
            "Reflections", "RelicTrader", "RoomFullOfCheese", "RoundTeaParty",
            "SlipperyBridge", "SpiralingWhirlpool", "StoneOfAllTime", "Symbiote",
            "TeaMaster", "TheFutureOfPotions", "TheLegendsWereTrue", "TrashHeap",
            "UnrestSite", "WarHistorianRepy", "WaterloggedScriptorium",
            "WelcomeToWongos", "WhisperingHollow", "WoodCarvings", "ZenWeaver"
        }.Select(Normalize),
        StringComparer.Ordinal);

    // Predictor presentation policy: these identities retain a player-relevant live eligibility
    // requirement after the current Act candidate projection has already proven Act/Epoch gates.
    // This set intentionally excludes Pure-Act-only Events, Reflections (Epoch-only), and
    // WarHistorianRepy (replacement-only and already cleaned from the ordinary queue).
    private static readonly IReadOnlySet<string> ResidualRuntimeConditionalEvents = new HashSet<string>(
        new[]
        {
            "Amalgamator", "ByrdonisNest", "ColorfulPhilosophers", "ColossalFlower",
            "CrystalSphere", "DenseVegetation", "EndlessConveyor", "FakeMerchant",
            "FieldOfManSizedHoles", "GraveOfTheForgotten", "JungleMazeAdventure",
            "LuminousChoir", "MorphicGrove", "PunchOff", "RanwidTheElder",
            "RelicTrader", "RoundTeaParty", "SlipperyBridge", "SpiralingWhirlpool",
            "StoneOfAllTime", "TeaMaster", "TheFutureOfPotions", "TheLegendsWereTrue",
            "TrashHeap", "UnrestSite", "WaterloggedScriptorium", "WelcomeToWongos",
            "WhisperingHollow", "WoodCarvings", "ZenWeaver"
        }.Select(Normalize),
        StringComparer.Ordinal);

    public static bool HasDocumentedCondition(RuntimeProfileId profileId, ModelKey eventKey) =>
        profileId == RuntimeProfileId.Beta111 &&
        eventKey.IsValid &&
        DocumentedConditionalEvents.Contains(Normalize(eventKey.Entry));

    public static bool ShouldShowRuntimeConditionMarker(
        RuntimeProfileId profileId,
        ModelKey eventKey,
        int playersCount)
    {
        if (profileId != RuntimeProfileId.Beta111 || !eventKey.IsValid)
        {
            return false;
        }

        string id = Normalize(eventKey.Entry);
        if (!ResidualRuntimeConditionalEvents.Contains(id))
        {
            return false;
        }

        // DenseVegetation/JungleMazeAdventure are explicitly unrestricted in single-player;
        // their HP thresholds are multiplayer-only live eligibility.
        if (playersCount <= 1 && (id == Normalize("DenseVegetation") || id == Normalize("JungleMazeAdventure")))
        {
            return false;
        }

        return true;
    }

    public static string RuntimeConditionLocalizationKey(
        RuntimeProfileId profileId,
        ModelKey eventKey,
        int playersCount) =>
        ShouldShowRuntimeConditionMarker(profileId, eventKey, playersCount)
            ? RuntimeConditionLocalizationPrefix + Normalize(eventKey.Entry)
            : string.Empty;

    // Broad knowledge lookup retained for documentation/knowledge consumers. Predictor tiles use
    // RuntimeConditionLocalizationKey instead so already-satisfied Act/Epoch gates are not repeated.
    public static string ConditionLocalizationKey(RuntimeProfileId profileId, ModelKey eventKey) =>
        HasDocumentedCondition(profileId, eventKey)
            ? ConditionLocalizationPrefix + Normalize(eventKey.Entry)
            : string.Empty;

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
