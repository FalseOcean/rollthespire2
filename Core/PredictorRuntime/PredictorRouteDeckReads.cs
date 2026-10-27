using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private const PredictorDeckUncertainFact RouteLevelEditFacts = PredictorDeckUncertainFact.Definition |
        PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Enchantment;
    // Qualification proves which picker is offered. Execution additionally
    // reads the fields used by its concrete operation; a clone, for example,
    // must retain its source's complete persistent payload.
    private sealed record RouteDeckRead(PredictorDeckProjectionScope Scope,
        PredictorDeckUncertainFact Qualification = PredictorDeckUncertainFact.None,
        PredictorDeckUncertainFact Execution = PredictorDeckUncertainFact.All, bool AddsCards = false);

    private static RouteDeckRead RouteEventDeckRead(string? key, string? branch) => (key, branch) switch
    {
        ("ZEN_WEAVER", _) or ("DOORS_OF_LIGHT_AND_DARK", "DARK") =>
            new(PredictorDeckProjectionScope.Removable, Execution: PredictorDeckUncertainFact.Definition),
        ("WELLSPRING", "BATHE") or
        ("FIELD_OF_MAN_SIZED_HOLES", "RESIST") or ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH") =>
            new(PredictorDeckProjectionScope.Removable, Execution: PredictorDeckUncertainFact.Definition, AddsCards: true),
        ("AMALGAMATOR", _) => new(branch == "COMBINE_STRIKES" ? PredictorDeckProjectionScope.BasicStrike :
            PredictorDeckProjectionScope.BasicDefend, PredictorDeckUncertainFact.Removable,
            PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Removable, AddsCards: true),
        ("WOOD_CARVINGS", "BIRD" or "TORUS") => new(PredictorDeckProjectionScope.Transformable,
            Execution: PredictorDeckUncertainFact.Definition, AddsCards: true),
        ("AROMA_OF_CHAOS", "LET_GO") or ("SYMBIOTE", "KILL_WITH_FIRE") or ("WHISPERING_HOLLOW", "HUG") or
        ("MORPHIC_GROVE", "GROUP") or ("TRIAL", "NONDESCRIPT_INNOCENT") or
        ("ENDLESS_CONVEYOR", "GRAB_JELLY_LIVER") => new(PredictorDeckProjectionScope.Transformable,
            Execution: PredictorDeckUncertainFact.Definition, AddsCards: true),
        ("AROMA_OF_CHAOS", "MAINTAIN_CONTROL") or ("SAPPHIRE_SEED", "EAT") or
        ("SPIRIT_GRAFTER", "REJECTION") or ("TRIAL", "MERCHANT_INNOCENT") =>
            new(PredictorDeckProjectionScope.Upgradable, Execution: RouteLevelEditFacts),
        // These two donor predicates use static Unplayable metadata, rather
        // than CanEnchant's per-level qualification and quest exclusion.
        ("SYMBIOTE", "APPROACH") or ("STONE_OF_ALL_TIME", "PUSH") or
        ("SAPPHIRE_SEED", "PLANT") or ("FIELD_OF_MAN_SIZED_HOLES", "ENTER_YOUR_HOLE") =>
            new(PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.Type |
                PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Definition,
                RouteLevelEditFacts | PredictorDeckUncertainFact.Type),
        ("SELF_HELP_BOOK", "READ_THE_BACK") => new(PredictorDeckProjectionScope.EnchantableAttack, Execution: RouteLevelEditFacts),
        ("SELF_HELP_BOOK", "READ_PASSAGE") => new(PredictorDeckProjectionScope.EnchantableSkill,
            Execution: RouteLevelEditFacts | PredictorDeckUncertainFact.Other),
        ("SELF_HELP_BOOK", "READ_ENTIRE_BOOK") => new(PredictorDeckProjectionScope.Enchantable, Execution: RouteLevelEditFacts),
        ("WOOD_CARVINGS", "SNAKE") or ("SPIRALING_WHIRLPOOL", "OBSERVE") or
        ("GRAVE_OF_THE_FORGOTTEN", "CONFRONT") or ("WATERLOGGED_SCRIPTORIUM", _) =>
            new(PredictorDeckProjectionScope.Enchantable, Execution: RouteLevelEditFacts),
        _ => new(PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.All)
    };

    private static RouteDeckRead RouteObtainDeckRead(PredictorRun run) => run.Phase switch
    {
        PredictorPhase.TransformCards => new(PredictorDeckProjectionScope.Transformable,
            Execution: PredictorDeckUncertainFact.Definition),
        PredictorPhase.RewardRemoveCards => new(PredictorDeckProjectionScope.Removable,
            Execution: PredictorDeckUncertainFact.Definition),
        PredictorPhase.ObtainDeckCards => run._obtainDeckOperation switch
        {
            PredictorObtainDeckOperation.Remove => new(PredictorDeckProjectionScope.Removable,
                Execution: PredictorDeckUncertainFact.Definition),
            PredictorObtainDeckOperation.Astrolabe => new(PredictorDeckProjectionScope.Transformable,
                Execution: PredictorDeckUncertainFact.Definition),
            PredictorObtainDeckOperation.Upgrade => new(PredictorDeckProjectionScope.Upgradable, Execution: RouteLevelEditFacts),
            PredictorObtainDeckOperation.Enchant => new(PredictorDeckProjectionScope.Enchantable, Execution: RouteLevelEditFacts),
            PredictorObtainDeckOperation.Clone => new(PredictorDeckProjectionScope.Cloneable),
            // Claws copies upgrade/enchantment fields into Maul; Pael stores
            // the complete original card for a later reader.
            _ => new(PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.All)
        },
        _ => new(PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.All)
    };

    private static PredictorDeckKnowledge RouteDeckKnowledge(PredictorRun run)
    {
        run._preview.Partial?.Synchronize(run.Working);
        return run._preview.Partial?.Deck ?? PredictorDeckKnowledge.Create(run.Context, run.Working);
    }

    private static ImmutableArray<PredictorCombatPendingCause> RouteDeckProjectionCauses(PredictorRun run,
        PredictorDeckProjection projection, List<RouteDependency> dependencies) => run._preview.Partial != null
        ? run._preview.Partial.ProjectionCauses(projection, count: false)
        : projection.OrderedCandidatesKnown && projection.Gaps.IsEmpty ? [] : CausesFor(dependencies, PredictorCombatDomain.Deck);

    private static ImmutableArray<PredictorCombatPendingCause> RouteEventPickerPopulationCauses(PredictorRun run,
        string key, string branch, List<RouteDependency> dependencies)
    {
        var read = RouteEventDeckRead(key, branch);
        var deck = RouteDeckKnowledge(run);
        var projection = deck.Project(read.Scope);
        var gaps = projection.Gaps.AddRange(projection.KnownCandidates.SelectMany(card =>
            deck.TargetGaps(card.Id, read.Scope, RouteEventQualificationFacts(card, key, branch, read)))).Distinct().ToImmutableArray();
        return RouteDeckProjectionCauses(run, projection with { Gaps = gaps }, dependencies);
    }

    private static bool IsKnownRouteEventCandidate(PredictorRun run, long id, string key, string branch,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
    {
        var read = RouteEventDeckRead(key, branch);
        var card = run.Working.Card(id);
        return IsKnownRouteDeckTarget(run, new(id, card.Key, card.FloorAdded), dependencies, knownInstanceIds,
            read.Scope, RouteEventQualificationFacts(card, key, branch, read));
    }

    private static PredictorDeckUncertainFact RouteEventQualificationFacts(PredictorCard card, string key,
        string branch, RouteDeckRead read) => read.Qualification |
        // GainsBlockForFresnel's MAD_SCIENCE exception reads its persistent
        // Tinker Time type, while ordinary cards only read frozen metadata.
        ((key, branch, card.Key.Entry) == ("SELF_HELP_BOOK", "READ_PASSAGE", "MAD_SCIENCE")
            ? PredictorDeckUncertainFact.Other : PredictorDeckUncertainFact.None);

    private static ImmutableArray<PredictorCombatPendingCause> RouteEventPickerReadCauses(PredictorRun run,
        string key, string branch, List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
    {
        if (RouteEventPicker(run, key, branch) is not { } picker) return [];
        int guaranteed = picker.Candidates.Count(id => IsKnownRouteEventCandidate(run, id, key, branch,
            dependencies, knownInstanceIds));
        // A strict larger lower bound proves that BeginEventCardSelection
        // cannot execute its zero/exact automatic cases.
        if (guaranteed > picker.Requested) return [];
        var population = RouteEventPickerPopulationCauses(run, key, branch, dependencies);
        if (!population.IsEmpty || picker.Candidates.Length != picker.Requested) return population;
        var read = RouteEventDeckRead(key, branch);
        var targets = RouteDeckTargetCauses(run, picker.Candidates.Select(id => new PredictorPlannedCard(id,
            run.Working.Card(id).Key, run.Working.Card(id).FloorAdded)), dependencies, knownInstanceIds,
            read.Scope, read.Execution);
        return read.AddsCards || picker.Candidates.Any(id => run.Working.Card(id).Key.Entry == "SPOILS_MAP")
            ? PredictorRouteCauseEvidence.Normalize(targets.AddRange(
            CausesFor(dependencies, PredictorCombatDomain.Relics))) : targets;
    }
}
