using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class TransformationSearchEditor
{
    // A removed source suspends its takeover draft. Missing observations for a
    // still-declared source must remain visible when building the query.
    private bool IsSourceDeclared(string id) => _declaredSourceIds is null || _declaredSourceIds.Contains(id) ||
        id is not ("N.LeafyPoultice" or "N.NewLeaf" or "N.BonesLeafyNewLeaf" or "N.BonesLeafyOther" or
            "E.MORPHIC_GROVE" or "E.AROMA_OF_CHAOS" or "E.WHISPERING_HOLLOW" or "E.SYMBIOTE" or "E.TRIAL" or
            TransformationSearchSource.AstrolabeId or TransformationSearchSource.PandoraId);

    private IEnumerable<string> ActiveTakeovers(GroupDraft group) => group.TakenOver.Where(IsSourceDeclared);

    private static bool HasResultTarget(GroupDraft group) => group.Objective == 0 ? group.RareCount > 0 : group.Cards.Count > 0;

    private long ResultCount(GroupDraft group) => _sources.Where(source => group.TakenOver.Contains(source.Id))
        .Sum(source => (long)(SourceCount(source) ?? 0));

    private int? SourceCount(TransformationSearchSource source) => source.Id == TransformationSearchSource.PandoraId
        ? source.PandoraCountOverride ?? _draft.PandoraCountOverride ?? GetAutomaticPandoraCount(source) : source.ResultCount;

    internal int? GetAutomaticPandoraCount(TransformationSearchSource source) => GetAutomaticPandoraCount(
        _openingRelics, _sources.Where(eventSource => eventSource.EventAct > 0 && eventSource.EventAct < source.AncientAct &&
            (eventSource.HasIndependentResult || IsTakenOver(_seat, eventSource.Id))).Sum(eventSource => eventSource.ResultCount));

    private int? GetAutomaticPandoraCount(IEnumerable<ModelKey> openingRelics, int priorEventTransforms)
    {
        if (_startingStrikeDefendCount is not { } initial) return null;
        var opening = openingRelics.ToHashSet(ModelKeyComparer.Instance);
        return Math.Max(0, initial - priorEventTransforms
            - (opening.Contains(BaseGameModelKeys.Relics.NewLeaf) ? 1 : 0)
            - (opening.Contains(BaseGameModelKeys.Relics.LeafyPoultice) ? 2 : 0)
            + (opening.Contains(BaseGameModelKeys.Relics.LargeCapsule) ? 2 : 0));
    }

    private int? GetAutomaticPandoraCount(SearchQuery query, IReadOnlySet<string> active, int ancientAct)
    {
        var eventIds = active.Where(id => id.StartsWith("E.", StringComparison.Ordinal)).Select(id => id[2..])
            .Concat(query.EventResultConditions.Where(condition => EventResultTransformSemantics.IsTransform(condition.Kind))
                .Select(condition => EventResultTransformSemantics.Entry(condition.Kind)))
            .ToHashSet(StringComparer.Ordinal);
        var priorEvents = query.EventSequenceConstraints.Where(condition => !condition.IsEmpty && condition.Act < ancientAct)
            .SelectMany(condition => condition.Keys.All.Concat(condition.Keys.Any.Count == 1 ? condition.Keys.Any : []))
            .Where(key => eventIds.Contains(key.Entry)).Distinct(ModelKeyComparer.Instance);
        int priorCount = priorEvents.Sum(key => RetainedSource("E." + key.Entry).ResultCount);
        IEnumerable<ModelKey> openingRelics = query.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones
            ? query.OpeningRouteRelicRequirement?.RequiredRelicKeys ?? []
            : query.OpeningRoute is { } opening ? [opening.RouteRelicKey] : [];
        return GetAutomaticPandoraCount(openingRelics, priorCount);
    }

    internal TransformationAggregateCondition? ExportCondition(SearchQuery query,
        IReadOnlyList<AncientTransformationResult>? ancientResults = null)
    {
        var groups = Groups.Select(group => (Group: group, Sources: ActiveTakeovers(group).Order(StringComparer.Ordinal).ToArray()))
            .Where(entry => entry.Sources.Length > 0).ToArray();
        var active = groups.SelectMany(group => group.Sources).ToHashSet(StringComparer.Ordinal);
        var local = (ancientResults ?? []).Where(result => !active.Contains(result.Source.Id)).ToArray();
        if (groups.Length == 0 && local.Length == 0) return query.TransformationAggregate;
        if (local.Length > 1)
            throw new InvalidOperationException("query.transform.ancient.exclusive");

        string[] missing = active.Where(id => !_sources.Any(source => source.Id == id))
            .Concat(local.Where(result => !result.IsAvailable).Select(result => result.Source.Id)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("TransformationDeclaredSourcesUnavailable:" + string.Join(",", missing));
        active.UnionWith(local.Select(result => result.Source.Id));
        if (active.Contains(TransformationSearchSource.AstrolabeId) && active.Contains(TransformationSearchSource.PandoraId))
            throw new InvalidOperationException("query.transform.ancient.exclusive");

        var opening = active.Contains("N.LeafyPoultice") ? TransformationOpening.LeafyPoultice :
            active.Contains("N.NewLeaf") ? TransformationOpening.NewLeaf :
            active.Contains("N.BonesLeafyNewLeaf") ? TransformationOpening.BonesLeafyNewLeaf :
            active.Contains("N.BonesLeafyOther") ? TransformationOpening.BonesLeafyOther : TransformationOpening.None;
        var order = ResolvePickupOrder(opening, query.OpeningRouteRelicRequirement);
        var companion = opening == TransformationOpening.BonesLeafyOther
            ? query.OpeningRouteRelicRequirement?.RequiredRelicKeys.FirstOrDefault(key => key != BaseGameModelKeys.Relics.LeafyPoultice)
            : null;
        var requirements = groups.Select(group => MakeRequirement(group.Group, group.Sources)).ToList();
        if (local.SingleOrDefault() is { } localResult)
            requirements.Add(new([localResult.Source.Id], TransformationAggregatePredicate.ContainsMultiset, 0, localResult.Cards));
        if (!active.Contains("N.BonesLeafyNewLeaf") && query.TransformationAggregate is
            { Opening: TransformationOpening.BonesLeafyNewLeaf, UsesExtendedRequirements: false,
                UsesEvents: false, Predicate: TransformationAggregatePredicate.ContainsMultiset } originalNeow)
        {
            // N still owns its local joint-three-slot request until the player
            // takes over that source. Both scopes must observe one pickup order.
            opening = originalNeow.Opening;
            order = originalNeow.PickupOrder;
            companion = originalNeow.CompanionRelic;
            requirements.Add(new(originalNeow.SourceCounts().Keys.ToArray(), originalNeow.Predicate,
                originalNeow.MinimumRareCount, originalNeow.TargetMultiset));
        }

        var ancient = local.SingleOrDefault()?.Source ?? _sources.FirstOrDefault(source => source.IsAncient && active.Contains(source.Id));
        bool pandora = ancient?.Id == TransformationSearchSource.PandoraId;
        int? automatic = pandora ? GetAutomaticPandoraCount(query, active, ancient!.AncientAct) : null;
        int? countOverride = pandora ? ancient!.PandoraCountOverride ?? _draft.PandoraCountOverride : null;
        if (pandora && automatic is null && !countOverride.HasValue)
            throw new InvalidOperationException("query.transform.pandora.automatic_unavailable");

        var first = requirements[0];
        var result = new TransformationAggregateCondition(opening, order, active.Contains("E.MORPHIC_GROVE"),
            active.Contains("E.AROMA_OF_CHAOS"), active.Contains("E.WHISPERING_HOLLOW"), first.Predicate,
            first.MinimumRareCount, first.TargetMultiset)
        {
            CompanionRelic = companion,
            Symbiote = active.Contains("E.SYMBIOTE"),
            TrialNondescript = active.Contains("E.TRIAL"),
            AncientSource = ancient is null ? TransformationAncientSource.None : pandora
                ? TransformationAncientSource.PandorasBox : TransformationAncientSource.Astrolabe,
            AncientAct = ancient?.AncientAct ?? 0,
            PandoraCountOverride = countOverride,
            PandoraAutomaticCount = automatic ?? 0,
            ResultRequirements = _draft.ExplicitRequirements || requirements.Count > 1 || ancient is not null ||
                first.Predicate == TransformationAggregatePredicate.ContainsMultisetAndRareCount ? requirements.ToArray() : []
        };

        var sourceCounts = result.SourceCounts();
        foreach (var requirement in requirements)
        {
            long capacity = requirement.Sources.Sum(id => (long)sourceCounts[id]);
            bool rareOnly = requirement.Predicate == TransformationAggregatePredicate.RareCountAtLeast;
            if (rareOnly && requirement.MinimumRareCount < 1 || !rareOnly && requirement.TargetMultiset.Count < 1 ||
                requirement.MinimumRareCount > capacity || requirement.TargetMultiset.Count > capacity)
                throw new InvalidOperationException("integration.transform.capacity");
        }
        return result;
    }

    private static TransformationPickupOrder ResolvePickupOrder(TransformationOpening opening, OpeningRouteRelicRequirement? pair)
    {
        if (opening is not (TransformationOpening.BonesLeafyNewLeaf or TransformationOpening.BonesLeafyOther) ||
            pair is not { OrderMode: BonesRouteOrderMode.ExactOrder, RequiredRelicKeys.Count: 2 })
            return TransformationPickupOrder.Any;
        bool leafyFirst = pair.RequiredRelicKeys[0] == BaseGameModelKeys.Relics.LeafyPoultice;
        return opening == TransformationOpening.BonesLeafyOther
            ? leafyFirst ? TransformationPickupOrder.LeafyThenCompanion : TransformationPickupOrder.CompanionThenLeafy
            : leafyFirst ? TransformationPickupOrder.LeafyThenNewLeaf : TransformationPickupOrder.NewLeafThenLeafy;
    }

    private static TransformationResultRequirement MakeRequirement(GroupDraft group, IReadOnlyList<string> sources) => new(
        sources, group.Objective switch
        {
            0 => TransformationAggregatePredicate.RareCountAtLeast,
            2 => TransformationAggregatePredicate.ContainsMultisetAndRemainingRare,
            _ when group.CardsRareCount > 0 => TransformationAggregatePredicate.ContainsMultisetAndRareCount,
            _ => TransformationAggregatePredicate.ContainsMultiset
        },
        group.Objective == 0 ? group.RareCount : group.Objective == 1 ? group.CardsRareCount : 0,
        group.Objective == 0 ? [] : group.Cards.ToArray());

    internal void ImportQuery(SearchQuery query)
    {
        CopyGroup(new GroupDraft(), _draft);
        _draft.AdditionalGroups.Clear();
        _draft.KnownSources.Clear();
        _draft.PandoraCountOverride = null;
        _draft.ExplicitRequirements = false;
        _draft.SingleCombination = query.TransformationAggregate is null;
        if (query.TransformationAggregate is not { } transform) return;
        // The original joint-three-slot editor owns its old N-only shape. A T
        // sidecar, when present, can still restore an authored T takeover.
        if (transform is { Opening: TransformationOpening.BonesLeafyNewLeaf, UsesExtendedRequirements: false,
            UsesEvents: false, Predicate: TransformationAggregatePredicate.ContainsMultiset }) return;

        var requirements = transform.ResultRequirements.Count > 0 ? transform.ResultRequirements :
            [new(transform.SourceCounts().Keys.ToArray(), transform.Predicate, transform.MinimumRareCount, transform.TargetMultiset)];
        for (int index = 0; index < requirements.Count; index++)
        {
            TransformationResultRequirement requirement = requirements[index];
            GroupDraft group = index == 0 ? _draft : new GroupDraft();
            group.TakenOver.UnionWith(requirement.Sources);
            group.Objective = requirement.Predicate switch
            {
                TransformationAggregatePredicate.RareCountAtLeast => 0,
                TransformationAggregatePredicate.ContainsMultisetAndRemainingRare => 2,
                _ => 1
            };
            group.RareCount = requirement.Predicate == TransformationAggregatePredicate.RareCountAtLeast ? requirement.MinimumRareCount : 0;
            group.CardsRareCount = requirement.Predicate == TransformationAggregatePredicate.ContainsMultisetAndRareCount
                ? requirement.MinimumRareCount : 0;
            group.Cards.AddRange(requirement.TargetMultiset);
            if (index > 0) _draft.AdditionalGroups.Add(group);
        }
        _draft.PandoraCountOverride = transform.PandoraCountOverride;
        _draft.ExplicitRequirements = transform.ResultRequirements.Count > 0;
    }
}
