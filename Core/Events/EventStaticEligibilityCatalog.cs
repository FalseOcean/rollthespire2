using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Events;

public enum EventStaticEligibilityDecision
{
    KeepConservatively,
    RejectByActIndex,
    RejectByPlayerMode,
    RejectByUnlockState,
    RejectAlwaysDisallowed
}

public sealed record EventStaticEligibilityResult(
    EventStaticEligibilityDecision Decision,
    string RuleId,
    IReadOnlyList<int> AllowedActs,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode,
    EventCandidateEligibilityKind EligibilityKind = EventCandidateEligibilityKind.RuntimeDependent)
{
    public bool ShouldReject => Decision != EventStaticEligibilityDecision.KeepConservatively;
    public bool IsRuntimeDependent => !ShouldReject && EligibilityKind == EventCandidateEligibilityKind.RuntimeDependent;
}

/// <summary>
/// Run-start effective-candidate eligibility projection. It never executes live
/// EventModel.IsAllowed and never fabricates future RunState. Beta111 can reject
/// immutable necessary conditions (Act, player mode/count, unlock facts and the
/// replacement-only WarHistorianRepy identity); any remaining gold/HP/deck/relic/
/// potion/route/visited/hook dependency stays RuntimeDependent.
/// </summary>
public static class EventStaticEligibilityCatalog
{
    public const string CatalogVersion = "event-effective-candidate-beta111-v4";

    private sealed record Rule(
        EventStaticEligibilityDecision Decision,
        string RuleId,
        IReadOnlyList<int> AllowedActs);

    private static readonly IReadOnlyDictionary<string, Rule> Stable107Rules = BuildStable107Rules();
    private static readonly IReadOnlyDictionary<string, Rule> Beta110Rules = BuildBeta110Rules();
    private static readonly IReadOnlyDictionary<string, Rule> Beta111Rules = BuildBeta111Rules();

    private static readonly IReadOnlySet<string> Beta111RuntimeDependentEvents = new HashSet<string>(
        new[]
        {
            "Amalgamator", "ByrdonisNest", "ColossalFlower", "CrystalSphere",
            "DenseVegetation", "EndlessConveyor", "FakeMerchant", "FieldOfManSizedHoles",
            "GraveOfTheForgotten", "JungleMazeAdventure", "LuminousChoir", "MorphicGrove",
            "PunchOff", "RanwidTheElder", "RelicTrader", "RoundTeaParty", "SlipperyBridge",
            "SpiralingWhirlpool", "StoneOfAllTime", "TeaMaster", "TheFutureOfPotions",
            "TheLegendsWereTrue", "TrashHeap", "UnrestSite", "WaterloggedScriptorium",
            "WelcomeToWongos", "WhisperingHollow", "WoodCarvings", "ZenWeaver"
        }.Select(Normalize),
        StringComparer.Ordinal);

    // Vanilla Beta111 events with no remaining dynamic IsAllowed dependency at this
    // projection layer. Unknown/custom events deliberately stay RuntimeDependent.
    private static readonly IReadOnlySet<string> Beta111KnownStaticEligibleEvents = new HashSet<string>(
        new[]
        {
            "AromaOfChaos", "AbyssalBaths", "BattlewornDummy", "BrainLeech", "Bugslayer",
            "ColorfulPhilosophers", "DollRoom", "DoorsOfLightAndDark", "DrowningBeacon",
            "HungryForMushrooms", "InfestedAutomaton", "LostWisp", "PotionCourier",
            "Reflections", "RoomFullOfCheese", "SapphireSeed", "SelfHelpBook",
            "SpiritGrafter", "SunkenStatue", "SunkenTreasury", "Symbiote", "TabletOfTruth",
            "TheLanternKey", "ThisOrThat", "TinkerTime", "Trial", "Wellspring"
        }.Select(Normalize),
        StringComparer.Ordinal);

    public static PredictionPrecision ProjectionPrecision(RuntimeProfileId profileId) => profileId switch
    {
        RuntimeProfileId.Stable107 => PredictionPrecision.Exact,
        RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 => PredictionPrecision.Exact,
        RuntimeProfileId.Beta109 => PredictionPrecision.Partial,
        _ => PredictionPrecision.Unknown
    };

    public static string ProjectionAuthorityCode(RuntimeProfileId profileId) => profileId switch
    {
        RuntimeProfileId.Stable107 => "Stable107DirectSourceAuditedStaticCandidateContract",
        RuntimeProfileId.Beta110 => "Beta110StaticCandidateActMaskContractExact",
        RuntimeProfileId.Beta111 => "Beta111EffectiveCandidateImmutableNecessaryConditionsSourceAudited",
        RuntimeProfileId.Beta109 => "Stable107DonorProjectionPendingModernConditionValidation",
        _ => "UnsupportedProfile"
    };

    public static EventStaticEligibilityResult Evaluate(
        ModelKey eventKey,
        int act,
        RuntimeProfileId profileId) =>
        Evaluate(eventKey, act, profileId, EventPoolSourceKind.Shared, null);

    public static EventStaticEligibilityResult Evaluate(
        ModelKey eventKey,
        int act,
        RuntimeProfileId profileId,
        EventPoolSourceKind source) =>
        Evaluate(eventKey, act, profileId, source, null);

    public static EventStaticEligibilityResult Evaluate(
        ModelKey eventKey,
        int act,
        RuntimeProfileId profileId,
        EventPoolSourceKind source,
        EventImmutableEligibilityContext? immutableContext)
    {
        PredictionPrecision precision = ProjectionPrecision(profileId);
        string profileEvidence = profileId switch
        {
            RuntimeProfileId.Stable107 => "stable107-static-candidate-contract",
            RuntimeProfileId.Beta110 => "beta110-static-candidate-contract",
            RuntimeProfileId.Beta111 => "beta111-effective-candidate-source-audited",
            RuntimeProfileId.Beta109 => "beta109-historical-donor-provisional",
            _ => "unsupported-profile"
        };
        IReadOnlyDictionary<string, Rule> rules = profileId switch
        {
            RuntimeProfileId.Beta111 => Beta111Rules,
            RuntimeProfileId.Beta110 => Beta110Rules,
            _ => Stable107Rules
        };

        string normalized = Normalize(eventKey.Entry);
        Rule? rule = normalized.Length > 0 && rules.TryGetValue(normalized, out Rule? found) ? found : null;

        if (rule?.Decision == EventStaticEligibilityDecision.RejectAlwaysDisallowed)
        {
            return Reject(rule, precision,
                $"event-pool-sequence.static-rule.always-disallowed.{profileEvidence}");
        }

        // Act-local membership already encodes the selected Act variant. Shared events
        // still need their direct CurrentActIndex necessary condition, including mixed
        // predicates in Beta111.
        if (source == EventPoolSourceKind.Shared &&
            rule is { AllowedActs.Count: > 0 } &&
            !rule.AllowedActs.Contains(act))
        {
            return new EventStaticEligibilityResult(
                EventStaticEligibilityDecision.RejectByActIndex,
                rule.RuleId,
                rule.AllowedActs,
                precision,
                $"event-pool-sequence.static-rule.act-index-reject.{profileEvidence}",
                EventCandidateEligibilityKind.StaticEligible);
        }

        EventImmutableEligibilityContext context = immutableContext ?? EventImmutableEligibilityContext.Unknown;
        if (profileId == RuntimeProfileId.Beta111)
        {
            EventStaticEligibilityResult? immutableDecision = EvaluateBeta111ImmutableContext(
                normalized, context, precision, profileEvidence);
            if (immutableDecision is not null)
            {
                // Context-specific positive facts may collapse a mixed predicate to
                // StaticEligible (DenseVegetation/JungleMazeAdventure in SP).
                if (immutableDecision.ShouldReject ||
                    immutableDecision.EligibilityKind == EventCandidateEligibilityKind.StaticEligible)
                {
                    return immutableDecision;
                }
            }
        }

        EventCandidateEligibilityKind eligibility = profileId == RuntimeProfileId.Beta111
            ? ClassifyBeta111Retained(normalized, context)
            : EventCandidateEligibilityKind.RuntimeDependent;
        string ruleId = rule?.RuleId ?? "NoAuditedStaticExclusion";
        IReadOnlyList<int> allowedActs = rule?.AllowedActs ?? Array.Empty<int>();
        return new EventStaticEligibilityResult(
            EventStaticEligibilityDecision.KeepConservatively,
            ruleId,
            allowedActs,
            precision,
            eligibility == EventCandidateEligibilityKind.RuntimeDependent
                ? $"event-pool-sequence.static-rule.runtime-dependent.{profileEvidence}"
                : $"event-pool-sequence.static-rule.static-eligible.{profileEvidence}",
            eligibility);
    }

    private static EventStaticEligibilityResult? EvaluateBeta111ImmutableContext(
        string normalized,
        EventImmutableEligibilityContext context,
        PredictionPrecision precision,
        string profileEvidence)
    {
        if (normalized == Normalize("FakeMerchant") && context.IsMultiplayerExact)
        {
            return new EventStaticEligibilityResult(
                EventStaticEligibilityDecision.RejectByPlayerMode,
                "SinglePlayerOnly:FakeMerchant",
                new[] { 2, 3 },
                precision,
                $"event-pool-sequence.static-rule.player-mode-reject.{profileEvidence}",
                EventCandidateEligibilityKind.StaticEligible);
        }

        if ((normalized == Normalize("DenseVegetation") || normalized == Normalize("JungleMazeAdventure")) &&
            context.IsSinglePlayerExact)
        {
            return new EventStaticEligibilityResult(
                EventStaticEligibilityDecision.KeepConservatively,
                "SinglePlayerFastPath:" + normalized,
                Array.Empty<int>(),
                precision,
                $"event-pool-sequence.static-rule.singleplayer-static-eligible.{profileEvidence}",
                EventCandidateEligibilityKind.StaticEligible);
        }

        if (normalized == Normalize("ColorfulPhilosophers") &&
            context.IsSinglePlayerExact && context.CharacterCardPoolCountExact)
        {
            if (context.CharacterCardPoolCount <= 1)
            {
                return new EventStaticEligibilityResult(
                    EventStaticEligibilityDecision.RejectByUnlockState,
                    "CharacterCardPools>1:ColorfulPhilosophers",
                    Array.Empty<int>(),
                    precision,
                    $"event-pool-sequence.static-rule.unlock-reject.{profileEvidence}",
                    EventCandidateEligibilityKind.StaticEligible);
            }

            return new EventStaticEligibilityResult(
                EventStaticEligibilityDecision.KeepConservatively,
                "CharacterCardPools>1:ColorfulPhilosophers",
                Array.Empty<int>(),
                precision,
                $"event-pool-sequence.static-rule.unlock-static-eligible.{profileEvidence}",
                EventCandidateEligibilityKind.StaticEligible);
        }

        return null;
    }

    private static EventCandidateEligibilityKind ClassifyBeta111Retained(
        string normalized,
        EventImmutableEligibilityContext context)
    {
        if ((normalized == Normalize("DenseVegetation") || normalized == Normalize("JungleMazeAdventure")) &&
            context.IsSinglePlayerExact)
        {
            return EventCandidateEligibilityKind.StaticEligible;
        }
        if (normalized == Normalize("ColorfulPhilosophers"))
        {
            return context.IsSinglePlayerExact && context.CharacterCardPoolCountExact && context.CharacterCardPoolCount > 1
                ? EventCandidateEligibilityKind.StaticEligible
                : EventCandidateEligibilityKind.RuntimeDependent;
        }
        if (Beta111RuntimeDependentEvents.Contains(normalized))
        {
            return EventCandidateEligibilityKind.RuntimeDependent;
        }
        return Beta111KnownStaticEligibleEvents.Contains(normalized)
            ? EventCandidateEligibilityKind.StaticEligible
            : EventCandidateEligibilityKind.RuntimeDependent;
    }

    private static EventStaticEligibilityResult Reject(
        Rule rule,
        PredictionPrecision precision,
        EvidenceCode evidenceCode) => new(
            rule.Decision,
            rule.RuleId,
            rule.AllowedActs,
            precision,
            evidenceCode,
            EventCandidateEligibilityKind.StaticEligible);

    private static IReadOnlyDictionary<string, Rule> BuildStable107Rules()
    {
        var rules = NewRuleDictionary();
        AddFullActRules(rules);
        AddReplacementOnlyRule(rules);
        return rules;
    }

    private static IReadOnlyDictionary<string, Rule> BuildBeta110Rules()
    {
        var rules = NewRuleDictionary();
        // Preserve the existing Beta110 source contract: only pure Act predicates
        // were audited there. Beta111 receives the expanded mixed-predicate audit.
        AddActRule(rules, "BrainLeech", 1, 2);
        AddActRule(rules, "DollRoom", 2);
        AddActRule(rules, "PotionCourier", 2, 3);
        AddActRule(rules, "RoomFullOfCheese", 1, 2);
        AddActRule(rules, "Symbiote", 2, 3);
        AddReplacementOnlyRule(rules);
        return rules;
    }

    private static IReadOnlyDictionary<string, Rule> BuildBeta111Rules()
    {
        var rules = NewRuleDictionary();
        AddFullActRules(rules);
        AddReplacementOnlyRule(rules);
        return rules;
    }

    private static void AddFullActRules(Dictionary<string, Rule> rules)
    {
        AddActRule(rules, "BrainLeech", 1, 2);
        AddActRule(rules, "CrystalSphere", 2, 3);
        AddActRule(rules, "DollRoom", 2);
        AddActRule(rules, "FakeMerchant", 2, 3);
        AddActRule(rules, "PotionCourier", 2, 3);
        AddActRule(rules, "RanwidTheElder", 2, 3);
        AddActRule(rules, "RelicTrader", 2, 3);
        AddActRule(rules, "RoomFullOfCheese", 1, 2);
        AddActRule(rules, "StoneOfAllTime", 2);
        AddActRule(rules, "Symbiote", 2, 3);
        AddActRule(rules, "TeaMaster", 1, 2);
        AddActRule(rules, "TheLegendsWereTrue", 1);
        AddActRule(rules, "WelcomeToWongos", 2);
    }

    private static Dictionary<string, Rule> NewRuleDictionary() =>
        new(StringComparer.Ordinal);

    private static void AddActRule(Dictionary<string, Rule> rules, string entry, params int[] allowedActs) =>
        rules[Normalize(entry)] = new Rule(
            EventStaticEligibilityDecision.RejectByActIndex,
            "ActIndex:" + entry,
            Array.AsReadOnly(allowedActs));

    private static void AddReplacementOnlyRule(Dictionary<string, Rule> rules)
    {
        rules[Normalize("WarHistorianRepy")] = new Rule(
            EventStaticEligibilityDecision.RejectAlwaysDisallowed,
            "ReplacementOnly:WarHistorianRepy",
            Array.Empty<int>());
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
