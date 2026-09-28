using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

internal static class PartyNeowQuery
{
    internal static bool HasTransactions(SearchQuery q) => q.Players.Any(p => p.SelectedOption is not null);
    internal static void Validate(PlayerOfferQuery player)
    {
        if (player.SelectedOption is not { } plan)
        {
            if (player.Results.Count > 0) throw new ArgumentException("Party.N.ResultRequiresSelectedOption");
            return;
        }
        if (!Enum.IsDefined(plan.Intent)) throw new ArgumentException("Party.N.InvalidIntent");
        if (PartyNeowAdmission.UnsupportedReason(plan.Option) is { } reason) throw new ArgumentException(reason);
        if (plan.Intent == PartyNeowIntent.PremiseOnly && player.Results.Count > 0) throw new ArgumentException("Party.N.PremiseCannotHaveResultPredicate");
        if (plan.Choices.Select(c => c.GroupId).Distinct().Count() != plan.Choices.Count ||
            plan.Choices.Any(c => !PartyNeowAdmission.ChoiceDomains(plan.Option).Any(d => d.Id == c.GroupId && c.Index >= (d.Skip ? -1 : 0) && c.Index < d.Count)))
            throw new ArgumentException("Party.N.IllegalConcreteChoice");
        foreach (var c in player.Results)
        {
            if (c.SourceRelicKey != plan.Option || c.IsEmpty || c.OutputKeys.Any(k => !k.IsValid || k.Category != (c.OutputKind == NeowStructuredOutputKind.Potion ? "POTION" : "CARD")))
                throw new ArgumentException("Party.N.InvalidResultPredicate");
            bool valid = plan.Option.Entry switch
            {
                "ARCANE_SCROLL" => c.Kind == NeowStructuredConditionKind.ExactSingle && c.Scope == NeowStructuredEffectScope.ProductRelevantEffects && c.OutputKind == NeowStructuredOutputKind.Card && c.OutputKeys.Count == 1,
                "HEFTY_TABLET" or "LEAD_PAPERWEIGHT" => SingleCard(c),
                "LOST_COFFER" => c.Kind == NeowStructuredConditionKind.ExactSingle && c.Scope == NeowStructuredEffectScope.SelectableOfferGroups && c.OutputKind is NeowStructuredOutputKind.Card or NeowStructuredOutputKind.Potion && c.OutputKeys.Count == 1,
                "PHIAL_HOLSTER" => c.Kind == NeowStructuredConditionKind.ExactUnorderedPair && c.Scope == NeowStructuredEffectScope.GeneratedPotions && c.OutputKind == NeowStructuredOutputKind.Potion && c.OutputKeys.Count is 1 or 2,
                "KALEIDOSCOPE" => c.Kind == NeowStructuredConditionKind.IndependentOfferGroupTargets && c.Scope == NeowStructuredEffectScope.SelectableOfferGroups && c.OutputKind == NeowStructuredOutputKind.Card && c.OutputKeys.Count is 1 or 2 && (c.KaleidoscopeGroupOrder != KaleidoscopeGroupOrderMode.ExactOrder || c.KaleidoscopePositionalSlots.Count == 2 && c.KaleidoscopePositionalSlots.Where(k => k.HasValue).Select(k => k!.Value).SequenceEqual(c.OutputKeys)),
                "SCROLL_BOXES" => c.Scope == NeowStructuredEffectScope.SelectableOfferGroups && c.OutputKind == NeowStructuredOutputKind.Card && (c.Kind == NeowStructuredConditionKind.StructuredCardComposition && c.OutputKeys.Count is >= 1 and <= 3 || c.Kind == NeowStructuredConditionKind.SpecialOffer && c.SpecialOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw),
                _ => false
            };
            if (!valid || c.Scope == NeowStructuredEffectScope.TransformResults || !Enum.IsDefined(c.KaleidoscopeGroupOrder))
                throw new ArgumentException("Party.N.UnsupportedResultShape");
            if (c.Kind != NeowStructuredConditionKind.SpecialOffer && c.SpecialOffer != NeowSpecialOfferKind.None ||
                c.SourceRelicKey != BaseGameModelKeys.Relics.Kaleidoscope && (c.KaleidoscopeGroupOrder != KaleidoscopeGroupOrderMode.AnyOrder || c.KaleidoscopePositionalSlots.Count > 0))
                throw new ArgumentException("Party.N.ExtraneousResultPremise");
        }
        static bool SingleCard(NeowStructuredEffectSearchCondition c) => c.Kind == NeowStructuredConditionKind.ExactSingle && c.Scope == NeowStructuredEffectScope.SelectableOfferGroups && c.OutputKind == NeowStructuredOutputKind.Card && c.OutputKeys.Count == 1;
    }

    internal static PartySeedInformation? Project(string seed, CompiledSearch compiled, bool enforcePredicates = true)
    {
        var information = PartySeedInformation.Project(seed, compiled.Context.Party!);
        if (!enforcePredicates)
            compiled = SearchCompiler.Compile(SearchQuery.Empty with { Players = compiled.NormalizedQuery.Players.Select(p =>
                p with { Offers = ModelKeySetFilter.Empty, Results = [] }).ToArray() }, compiled.Context);
        return Complete(information, compiled);
    }
    internal static PartySeedInformation? Complete(PartySeedInformation information, CompiledSearch compiled, CancellationToken cancellationToken = default)
    {
        var query = compiled.NormalizedQuery;
        if (!PartyInitialQuery.MatchesOffers(query, information) || !PartyInitialQuery.MatchesWorld(query, information)) return null;
        var participants = query.Players.Where(p => p.SelectedOption is not null).Select(p => new PartyNeowParticipant(p.Slot, p.SelectedOption!)).ToArray();
        if (participants.Length == 0) return information;
        var witness = PartyNeowProjection.Find(information, compiled.Context.Party!, participants, results =>
            query.Players.All(p => p.Results.All(c => Matches(c, results.Single(r => r.Slot == p.Slot)))), cancellationToken);
        return witness is null ? null : information with { Transactions = witness };
    }

    internal static bool Matches(NeowStructuredEffectSearchCondition condition, PartyNeowResult result)
    {
        var groups = result.Results.Where(g => condition.Scope != NeowStructuredEffectScope.SelectableOfferGroups ||
            g.SelectionPolicy is EffectSelectionPolicy.ChooseOneOrSkip or EffectSelectionPolicy.OptionalClaim or EffectSelectionPolicy.ChooseExactlyOne).ToArray();
        ModelKey[] Keys(IEnumerable<PredictedEffectGroup> source) => source.SelectMany(g => g.OrderedItems)
            .Where(e => e.TargetKey.HasValue && (condition.OutputKind == NeowStructuredOutputKind.Potion
                ? e.Kind is PredictedEffectKind.AddPotion or PredictedEffectKind.AttemptAddPotion : e.Kind == PredictedEffectKind.AddCard))
            .SelectMany(e => Enumerable.Repeat(e.TargetKey!.Value, e.Multiplicity)).ToArray();
        if (condition.Kind == NeowStructuredConditionKind.SpecialOffer)
            return Keys(groups).Count(k => k == BaseGameModelKeys.Cards.Claw) == 3;
        if (condition.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder)
            return condition.KaleidoscopePositionalSlots.Select((key, i) => key is null || Keys(groups.Where(g => g.GroupOrder == i)).Contains(key.Value)).All(b => b);
        var remaining = Keys(groups).ToList();
        return condition.OutputKeys.All(key => remaining.Remove(key));
    }
}
