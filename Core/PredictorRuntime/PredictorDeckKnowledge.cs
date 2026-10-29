using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorDeckProjectionScope
{
    All, Attacks, Skills, Powers, Upgradable, UpgradableAttack, UpgradableSkill,
    Upgraded, Removable, Transformable, Cloneable, Enchantable,
    EnchantableAttack, EnchantableSkill, EnchantableDefend,
    Basic, BasicStrike, BasicDefend, NonEternal, PaelsTooth
}

[Flags]
internal enum PredictorDeckUncertainFact
{
    None = 0, Membership = 1, Type = 2, Upgrade = 4, Removable = 8,
    Enchantment = 16, Definition = 32, Other = 64, All = 127
}

// OptionId is stable ordered meaning, not a card model key. Null Card is Skip.
// Positive card IDs denote an already generated/returned offer; zero denotes a
// fresh card prototype. Neither scratch additions nor copies gain known IDs.
internal sealed record PredictorDeckRewardAlternative(string OptionId, PredictorCard? Card)
{
    public string? UnsupportedReason { get; init; }
}
internal sealed record PredictorDeckBookCounterDelta(long RelicId, int CardsAdded);
internal sealed record PredictorDeckRewardEffect(string OptionId, bool AdditionsKnown,
    ImmutableArray<PredictorCard> AddedCards, int? GoldDelta,
    ImmutableArray<PredictorDeckBookCounterDelta> BookCounterDeltas, string? UnsupportedReason);
internal sealed record PredictorDeckRewardChoice(string ChoiceId, string NodeId,
    ImmutableArray<PredictorDeckRewardAlternative> Alternatives,
    ImmutableArray<PredictorDeckRewardEffect> Effects, string AlternativesContract);
internal sealed record PredictorDeckKnowledgeGap(string NodeId, string? ChoiceId, string Reason,
    PredictorDeckUncertainFact Facts, ImmutableArray<long> InstanceIds,
    ImmutableArray<EffectCardType> CardTypes, string? SourceOperation = null);
internal sealed record PredictorDeckRandomUpgradeResult(PredictorDeckKnowledge Deck,
    PredictorChoiceInt? Consumption, PredictorDeckProjection Before,
    string SourceNode, string SourceOperation,
    ImmutableArray<PredictorDeckRewardChoice> CandidateSources,
    ImmutableArray<PredictorDeckKnowledgeGap> Gaps);

internal sealed record PredictorDeckProjection(PredictorDeckProjectionScope Scope,
    PredictorChoiceInt? Count, ImmutableArray<PredictorCard> KnownCandidates,
    bool OrderedCandidatesKnown, ImmutableArray<PredictorDeckRewardChoice> Sources,
    ImmutableArray<PredictorDeckKnowledgeGap> Gaps)
{
    // The known subset is in Deck order. It is a complete candidate list only
    // when OrderedCandidatesKnown. Card values prove this scope's reads, not
    // unrelated properties invalidated by some other scope's gap.
    internal long? KnownCount => Count?.KnownValue;
    internal PredictorChoiceInt? UnstableShuffleDraws => Count?.SubtractAndClamp(1);
    internal ImmutableArray<PredictorDeckRewardChoice> UnresolvedCountSources { get; init; } = [];
    internal ImmutableArray<PredictorDeckRewardChoice> UnresolvedCandidateSources { get; init; } = [];
    // Candidate identity gaps do not prevent a separately proven count.
    internal ImmutableArray<PredictorDeckKnowledgeGap> UnresolvedCountGaps { get; init; } = [];
}

// Owns detached deck facts and pending additive choices, not a continuation
// witness. Fixed operations preserve prior choices and gaps. Every new choice
// replays only its own alternatives through the real obtain helper; no cross-
// reward Cartesian product and no advancement of a representative branch.
internal sealed class PredictorDeckKnowledge
{
    private sealed record Segment(ImmutableArray<PredictorCard> FixedCards, PredictorDeckRewardChoice? Choice,
        PredictorDeckKnowledgeGap? UnknownAddition = null, bool ReceiptEffectsSettled = false, bool ExcludesBasicCards = false,
        int? KnownAdditionCount = null);
    private sealed record PossiblyPresentCard(int SegmentIndex, PredictorCard Card);
    private sealed record ScopedGap(PredictorDeckKnowledgeGap Value, int SegmentLimit, bool OnlyUpgradable = false);
    private sealed record CountMutation(PredictorDeckProjectionScope Scope, int SegmentLimit,
        PredictorChoiceInt? Count, string SourceNode, string SourceOperation,
        ImmutableArray<PredictorDeckRewardChoice> CountSources);
    private readonly PredictorContext _context;
    private readonly ImmutableArray<Segment> _segments;
    private readonly ImmutableArray<ScopedGap> _gaps;
    private readonly ImmutableArray<CountMutation> _countMutations;
    private readonly ImmutableArray<PossiblyPresentCard> _possiblyPresent;

    private PredictorDeckKnowledge(PredictorContext context, ImmutableArray<Segment> segments,
        ImmutableArray<ScopedGap> gaps, ImmutableArray<CountMutation> countMutations = default,
        ImmutableArray<PossiblyPresentCard> possiblyPresent = default)
    {
        _context = context; _segments = segments; _gaps = gaps;
        _countMutations = countMutations.IsDefault ? [] : countMutations;
        _possiblyPresent = possiblyPresent.IsDefault ? [] : possiblyPresent;
    }

    internal static PredictorDeckKnowledge Create(PredictorContext context, PredictorState baseState)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(baseState);
        if (context.Catalog == null) throw new ArgumentException("PredictorDeckCatalogMissing");
        baseState.Validate();
        return new(context, [new(baseState.Deck.Select(baseState.Card).ToImmutableArray(), null)], []);
    }

    internal ImmutableArray<PredictorDeckRewardChoice> Choices => _segments.Where(s => s.Choice != null)
        .Select(s => s.Choice!).ToImmutableArray();

    // Complete alternatives of one local event share one choice identity with
    // its Gold/RNG effects. These are already executed outcomes, not a second
    // implementation of card obtain hooks or independent per-card ranges.
    internal PredictorDeckKnowledge AddStateChoice(string choiceId, string nodeId,
        PredictorState before, PredictorState witness,
        ImmutableArray<(string Key, PredictorState State)> outcomes, bool preserveCommonAdditionIds = false)
    {
        if (outcomes.IsDefaultOrEmpty || Choices.Any(c => c.ChoiceId == choiceId))
            throw new ArgumentException("PredictorEventDeckChoiceInvalid");
        var decks = outcomes.Select(o => o.State.Deck.Select(o.State.Card).ToImmutableArray()).ToArray();
        var beforeIds = before.Deck.ToHashSet();
        // A receipt can prove its newly added instance IDs even while their
        // enchantments differ. Preserve those identity carriers for independent
        // removal/upgrade reads; variable allocations remain conditional.
        var stable = preserveCommonAdditionIds ? decks[0].Where(card => !beforeIds.Contains(card.Id) &&
            decks.All(deck => deck.FirstOrDefault(other => other.Id == card.Id) is { } other &&
                (ChangedFacts(card, other) & ~PredictorDeckUncertainFact.Enchantment) == PredictorDeckUncertainFact.None))
            .Select(card => decks.All(deck => deck.Single(other => other.Id == card.Id) == card)
                ? card : card with { Enchantment = null, EnchantmentAmount = 0 }).ToImmutableArray() : [];
        var stableIds = stable.Select(card => card.Id).ToHashSet();
        var uncertainStableIds = stable.Where(card => decks.Any(deck =>
            PredictorCardEnchantment.From(deck.Single(other => other.Id == card.Id)) != PredictorCardEnchantment.From(card)))
            .Select(card => card.Id).ToImmutableArray();
        var alternatives = outcomes.Select(o => new PredictorDeckRewardAlternative(o.Key, null)).ToImmutableArray();
        var effects = outcomes.Select((o, i) => new PredictorDeckRewardEffect(o.Key, true,
            // Existing witness cards belong to their original source. Moving
            // them into this independent choice invents cross-source decks.
            decks[i].Where(c => !beforeIds.Contains(c.Id) && !stableIds.Contains(c.Id))
                .Select(c => c with { Id = 0 }).ToImmutableArray(),
            checked(o.State.GoldBudget - witness.GoldBudget),
            o.State.Relics.Where(r => r.Key.Entry == "BOOK_OF_FIVE_RINGS").Select(r => new PredictorDeckBookCounterDelta(r.Id,
                r.BookOfFiveRingsCardsAdded - (witness.Relics.FirstOrDefault(w => w.Id == r.Id)?.BookOfFiveRingsCardsAdded ?? 0)))
                .ToImmutableArray(), null)).ToImmutableArray();
        string contract = nodeId.Length + ":" + nodeId + string.Concat(alternatives.Select(a => a.OptionId.Length + ":" + a.OptionId));
        var choice = new PredictorDeckRewardChoice(choiceId, nodeId, alternatives, effects, contract);
        var deck = this;
        var changed = before.Deck.Select(before.Card).Where(card =>
            decks.Any(d => d.FirstOrDefault(c => c.Id == card.Id) != card)).ToArray();
        foreach (var card in changed)
        {
            var next = decks.Select(d => d.FirstOrDefault(c => c.Id == card.Id)).ToArray();
            var facts = next.Aggregate(PredictorDeckUncertainFact.None,
                (value, after) => value | ChangedFacts(card, after));
            if (!HasFixedMember(card.Id))
            {
                // A previous choice's representative has no fixed identity.
                // Conservatively affect its possible type, keeping that old
                // choice instead of replaying this witness as an addition.
                deck = deck.MarkExistingChangePending(nodeId, "ConditionalCardEditNotProjected", facts,
                    cardTypes: (facts & PredictorDeckUncertainFact.Type) == 0 ? [card.Type] : [], choiceId: choiceId);
            }
            else if (next.All(c => c == null) && TargetGaps(card.Id, PredictorDeckProjectionScope.All,
                PredictorDeckUncertainFact.None).IsEmpty)
                deck = deck.RemoveKnown(card.Id, absenceKnown: true);
            else if (next[0] is { } common && next.All(c => c == common))
                deck = deck.ReplaceKnown(card.Id, common, append: false, observedBefore: card);
            else
                // Membership stays fixed for property-only alternatives. The
                // old record remains a carrier for unaffected proven fields.
                deck = deck.MarkExistingChangePending(nodeId, "EventCardAlternatives", facts,
                    instanceIds: [card.Id], choiceId: choiceId);
        }
        if (decks.Any(cards => !cards.Where(c => beforeIds.Contains(c.Id)).Select(c => c.Id).SequenceEqual(
            before.Deck.Where(id => cards.Any(c => c.Id == id)))))
        {
            // The additive owner has no ordering-choice representation. Keep
            // numeric deltas below, but do not certify a representative order.
            var ids = before.Deck.Where(HasFixedMember).ToImmutableArray();
            if (!ids.IsEmpty) deck = deck.MarkExistingChangePending(nodeId, "EventCardOrderNotProjected",
                PredictorDeckUncertainFact.Membership, ids, choiceId: choiceId);
        }
        deck = new(_context, deck._segments.Add(new(stable, choice)), deck._gaps, deck._countMutations, deck._possiblyPresent);
        if (!uncertainStableIds.IsEmpty)
            deck = deck.MarkExistingChangePending(nodeId, "CardRewardEnchantmentPending", PredictorDeckUncertainFact.Enchantment,
                instanceIds: uncertainStableIds, choiceId: choiceId);
        // Existing-card effects and additions share the event's choice root.
        // A delta is valid only when its required old-card reads were known;
        // an upgrade witness cannot establish a removed card's upgrade count.
        var mutations = deck._countMutations;
        foreach (var scope in Enum.GetValues<PredictorDeckProjectionScope>())
        {
            var beforeProjection = Project(scope);
            var values = new long[outcomes.Length];
            bool supported = beforeProjection.Count != null;
            for (int option = 0; supported && option < outcomes.Length; option++)
            {
                foreach (var card in changed)
                {
                    var next = decks[option].FirstOrDefault(c => c.Id == card.Id);
                    if (next != null && !ProjectionReadsChanged(card, next, scope)) continue;
                    int segment = _segments.FindIndex(s => s.FixedCards.Any(c => c.Id == card.Id));
                    if (segment < 0 || RelevantGaps(segment, card, scope).Any() || MetadataMissing(card, scope) ||
                        next != null && MetadataMissing(next, scope))
                    { supported = false; break; }
                    values[option] += (next != null && Matches(next, scope) ? 1 : 0) - (Matches(card, scope) ? 1 : 0);
                }
                // Include promoted fixed additions exactly once in the final
                // count boundary; choice effects contain only the remainder.
                foreach (var card in decks[option].Where(card => !beforeIds.Contains(card.Id)))
                {
                    if (MetadataMissing(card, scope)) { supported = false; break; }
                    if (Matches(card, scope)) values[option]++;
                }
            }
            var count = supported ? beforeProjection.Count! + PredictorChoiceInt.Alternatives(choiceId, contract, values) : null;
            var sources = MergeSources(beforeProjection.UnresolvedCountSources,
                supported && values.Distinct().Count() == 1 ? [] : [choice]);
            mutations = mutations.Add(new(scope, deck._segments.Length, count, nodeId,
                "event-state:" + choiceId, sources));
        }
        return new(_context, deck._segments, deck._gaps, mutations, deck._possiblyPresent);
    }

    internal static PredictorDeckUncertainFact ChangedFacts(PredictorCard before, PredictorCard? after)
    {
        if (after == null) return PredictorDeckUncertainFact.Membership;
        var facts = PredictorDeckUncertainFact.None;
        if (before.Key != after.Key || before.MaxUpgradeLevel != after.MaxUpgradeLevel) facts |= PredictorDeckUncertainFact.Definition;
        if (before.Type != after.Type) facts |= PredictorDeckUncertainFact.Type;
        if (before.UpgradeLevel != after.UpgradeLevel || before.HasEternalKeyword != after.HasEternalKeyword ||
            before.HasExhaustKeyword != after.HasExhaustKeyword || before.HasInnateKeyword != after.HasInnateKeyword ||
            before.HasRetainKeyword != after.HasRetainKeyword) facts |= PredictorDeckUncertainFact.Upgrade;
        if (before.Removable != after.Removable) facts |= PredictorDeckUncertainFact.Removable;
        if (before.Enchantment != after.Enchantment || before.EnchantmentAmount != after.EnchantmentAmount)
            facts |= PredictorDeckUncertainFact.Enchantment;
        var unchangedOther = after with { Key = before.Key, MaxUpgradeLevel = before.MaxUpgradeLevel,
            Type = before.Type, UpgradeLevel = before.UpgradeLevel, Removable = before.Removable,
            HasEternalKeyword = before.HasEternalKeyword, HasExhaustKeyword = before.HasExhaustKeyword,
            HasInnateKeyword = before.HasInnateKeyword, HasRetainKeyword = before.HasRetainKeyword,
            Enchantment = before.Enchantment, EnchantmentAmount = before.EnchantmentAmount };
        if (unchangedOther != before) facts |= PredictorDeckUncertainFact.Other;
        return facts;
    }

    // hookKnown certifies the obtain-relevant inventory, modifiers and lifetime
    // sets in this boundary snapshot. It must be false for an unresolved hook
    // environment; an old witness cannot certify those reads. We do not carry
    // the replayed Gold/Book/skip-set state forward as a new concrete premise.
    internal PredictorDeckKnowledge AddRewardChoice(string choiceId, string nodeId,
        ImmutableArray<PredictorDeckRewardAlternative> alternatives,
        PredictorContext context, PredictorState hookState, bool hookKnown)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hookState);
        if (string.IsNullOrWhiteSpace(choiceId) || string.IsNullOrWhiteSpace(nodeId) || alternatives.IsDefaultOrEmpty ||
            alternatives.Any(a => a == null || string.IsNullOrWhiteSpace(a.OptionId)) ||
            alternatives.Select(a => a.OptionId).Distinct(StringComparer.Ordinal).Count() != alternatives.Length)
            throw new ArgumentException("PredictorDeckRewardChoiceInvalid");
        if (context.CatalogFingerprint != _context.CatalogFingerprint || context.Catalog != _context.Catalog)
            throw new ArgumentException("PredictorDeckCatalogChanged");
        var prior = Choices.FirstOrDefault(c => c.ChoiceId == choiceId);
        if (prior != null || _segments.Any(s => s.UnknownAddition?.ChoiceId == choiceId))
            throw new ArgumentException("PredictorDeckChoiceAlreadyPresent:" + choiceId);
        // Length prefixes preserve ordered semantics even when an ID contains
        // separators. Node is part of the source contract, not an inferred key.
        string contract = nodeId.Length + ":" + nodeId + string.Concat(alternatives.Select(a => a.OptionId.Length + ":" + a.OptionId));
        var effects = alternatives.Select(a => ObtainEffect(a, context, hookState, hookKnown)).ToImmutableArray();
        var choice = new PredictorDeckRewardChoice(choiceId, nodeId, alternatives, effects, contract);
        var gaps = _gaps;
        var mutations = _countMutations;
        if (effects.Any(e => e.UnsupportedReason == "CardObtainChangesExistingDeck"))
        {
            var existingGap = new ScopedGap(new(nodeId, choiceId, "CardObtainChangesExistingDeck",
                PredictorDeckUncertainFact.All, [], []), _segments.Length);
            gaps = gaps.Add(existingGap);
            mutations = InvalidateCountMutations(existingGap);
        }
        return new(_context, _segments.Add(new([], choice)), gaps, mutations, _possiblyPresent);
    }

    private static PredictorDeckRewardEffect ObtainEffect(PredictorDeckRewardAlternative alternative,
        PredictorContext context, PredictorState hookState, bool hookKnown)
    {
        PredictorDeckRewardEffect Unsupported(string reason) => new(alternative.OptionId, false, [], null, [], reason);
        if (alternative.UnsupportedReason is { } reason) return Unsupported(reason);
        if (alternative.Card == null) return new(alternative.OptionId, true, [], 0, [], null);
        if (!hookKnown) return Unsupported("CardObtainHooksNotKnown");
        var card = alternative.Card;
        var definition = context.Catalog?.CardDefinitions.FirstOrDefault(d => d.Prototype.Key == card.Key);
        if (definition == null || definition.Levels.IsDefaultOrEmpty ||
            !definition.Levels.Any(l => l.UpgradeLevel == card.UpgradeLevel) ||
            card.UpgradeLevel < card.MaxUpgradeLevel && !definition.Levels.Any(l => l.UpgradeLevel == card.UpgradeLevel + 1))
            return Unsupported("CardObtainMetadataMissing:" + card.Key);
        try { PredictorSettlementEffects.RequireImplementedCard(card); }
        catch (NotImplementedException exception) { return Unsupported(exception.Message); }
        // Match TakeReward's registration then AddExistingCard, including the
        // distinction from reward creation's Egg upgrades. This replay has no
        // native objects, RNG calls or existing-deck readers.
        var entry = hookState;
        if (card.Id == 0)
        {
            card = card with { Id = entry.NextInstanceId };
            entry = entry with { NextInstanceId = checked(entry.NextInstanceId + 1) };
        }
        if (card.Id < 1 || card.Id >= entry.NextInstanceId || entry.Deck.Contains(card.Id) || card.Removed)
            throw new ArgumentException("PredictorDeckOfferInstanceInvalid");
        if (!entry.Cards.Any(c => c.Id == card.Id)) entry = entry with { Cards = entry.Cards.Add(card) };
        var result = PredictorObtainSources.AddExistingCard(context, entry, card);
        if (!result.Deck.Take(entry.Deck.Length).SequenceEqual(entry.Deck) ||
            entry.Deck.Any(id => result.Card(id) != entry.Card(id)))
            return Unsupported("CardObtainChangesExistingDeck");
        var added = result.Deck.Skip(entry.Deck.Length).Select(result.Card)
            .Select(c => c with { Id = 0 }).ToImmutableArray();
        var book = result.Relics.Where(r => r.Key.Entry == "BOOK_OF_FIVE_RINGS")
            .Select(r => new PredictorDeckBookCounterDelta(r.Id,
                checked(r.BookOfFiveRingsCardsAdded - entry.Relics.Single(old => old.Id == r.Id).BookOfFiveRingsCardsAdded)))
            .ToImmutableArray();
        return new(alternative.OptionId, true, added, checked(result.GoldBudget - entry.GoldBudget), book, null);
    }

    // These commands accept already obtained/settled facts. They intentionally
    // do not execute obtain hooks again or replace the owner with a witness.
    internal PredictorDeckKnowledge AddKnown(PredictorCard card)
    {
        RequireKnownCard(card);
        if (_segments.SelectMany(s => s.FixedCards).Any(c => c.Id == card.Id) || _possiblyPresent.Any(c => c.Card.Id == card.Id))
            throw new ArgumentException("PredictorDeckKnownInstanceDuplicate");
        return new(_context, _segments.Add(new([card], null)), _gaps, _countMutations, _possiblyPresent);
    }

    // A snapshot's absent ID does not settle an earlier unresolved removal.
    // absenceKnown is a separate proof that this instance is absent in every
    // alternative; ordinary witness synchronization must leave it false.
    internal PredictorDeckKnowledge RemoveKnown(long instanceId, bool absenceKnown = false)
    {
        int segment = _segments.FindIndex(s => s.FixedCards.Any(c => c.Id == instanceId));
        if (segment < 0)
        {
            if (!absenceKnown || !_possiblyPresent.Any(c => c.Card.Id == instanceId))
                throw new ArgumentException("PredictorDeckKnownTargetMissing");
            return new(_context, _segments, _gaps, _countMutations,
                _possiblyPresent.Where(c => c.Card.Id != instanceId).ToImmutableArray());
        }
        var cards = _segments[segment].FixedCards;
        var prior = cards.Single(c => c.Id == instanceId);
        var edited = _segments.SetItem(segment, _segments[segment] with
        { FixedCards = cards.RemoveAt(cards.FindIndex(c => c.Id == instanceId)) });
        var possible = _possiblyPresent.Where(c => c.Card.Id != instanceId).ToImmutableArray();
        if (!absenceKnown && _gaps.Any(g => (g.Value.Facts & PredictorDeckUncertainFact.Membership) != 0 &&
            GapReads(g, segment, prior, PredictorDeckProjectionScope.All)))
            possible = possible.Add(new(segment, prior));
        return new(_context, edited, _gaps, AdjustCountMutations(segment, prior, null), possible);
    }

    internal PredictorDeckKnowledge ReplaceKnown(long instanceId, PredictorCard replacement, bool append = true,
        PredictorCard? observedBefore = null)
    {
        RequireKnownCard(replacement);
        int segment = _segments.FindIndex(s => s.FixedCards.Any(c => c.Id == instanceId));
        if (segment < 0) throw new ArgumentException("PredictorDeckKnownTargetMissing");
        if (replacement.Id != instanceId && _segments.SelectMany(s => s.FixedCards).Any(c => c.Id == replacement.Id))
            throw new ArgumentException("PredictorDeckKnownInstanceDuplicate");
        int index = _segments[segment].FixedCards.FindIndex(c => c.Id == instanceId);
        var prior = _segments[segment].FixedCards[index];
        if (append) return RemoveKnown(instanceId).AddKnown(replacement);
        if (observedBefore != null && (observedBefore.Id != instanceId || replacement.Id != instanceId))
            throw new ArgumentException("PredictorDeckSyncBeforeInvalid");
        // A continuation snapshot contains one witness of an earlier random
        // operation. Preserve unresolved fields rather than importing that
        // witness's upgrade level, enchantment or model qualification.
        var accepted = replacement.Id == instanceId ? PreserveUncertainFields(segment, prior, replacement) : replacement;
        var mutations = AdjustCountMutations(segment, prior, accepted);
        if (observedBefore != null)
            mutations = mutations.Select(m => segment < m.SegmentLimit &&
                ProjectionReadsChanged(observedBefore, replacement, m.Scope) &&
                (RelevantGaps(segment, prior, m.Scope).Any() || MetadataMissing(accepted, m.Scope))
                    ? m with { Count = null } : m).ToImmutableArray();
        return new(_context, _segments.SetItem(segment, _segments[segment] with
        { FixedCards = _segments[segment].FixedCards.SetItem(index, accepted) }), _gaps, mutations, _possiblyPresent);
    }

    // Marks a real missing observation/implementation at its affected read
    // boundary. Type and ID scope limit propagation. The segment limit keeps a
    // later known add outside an earlier random-upgrade/removal operation.
    internal PredictorDeckKnowledge MarkExistingChangePending(string nodeId, string reason,
        PredictorDeckUncertainFact facts, ImmutableArray<long> instanceIds = default,
        ImmutableArray<EffectCardType> cardTypes = default, string? choiceId = null, string? sourceOperation = null)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(reason) || facts == PredictorDeckUncertainFact.None ||
            (facts & ~PredictorDeckUncertainFact.All) != 0 || choiceId != null && string.IsNullOrWhiteSpace(choiceId))
            throw new ArgumentException("PredictorDeckPendingChangeInvalid");
        var ids = instanceIds.IsDefault ? [] : instanceIds;
        var types = cardTypes.IsDefault ? [] : cardTypes;
        if (ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length ||
            types.Any(t => !Enum.IsDefined(t)) || types.Distinct().Count() != types.Length)
            throw new ArgumentException("PredictorDeckPendingScopeInvalid");
        var gap = new PredictorDeckKnowledgeGap(nodeId, choiceId, reason, facts, ids, types, sourceOperation);
        var scoped = new ScopedGap(gap, _segments.Length);
        return new(_context, _segments, _gaps.Add(scoped), InvalidateCountMutations(scoped), _possiblyPresent);
    }

    // Unknown incoming cards are a possible addition, not a fabricated old
    // instance. CardTypes is an explicit possible-type contract, never a
    // representative card value; an empty contract permits every type.
    internal PredictorDeckKnowledge MarkUnknownAddition(string nodeId, string reason,
        string? choiceId = null, ImmutableArray<EffectCardType> cardTypes = default, bool receiptEffectsSettled = false,
        bool excludesBasicCards = false, int? knownCount = null)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(reason) ||
            choiceId != null && string.IsNullOrWhiteSpace(choiceId) || knownCount is < 0)
            throw new ArgumentException("PredictorDeckUnknownAdditionInvalid");
        var types = cardTypes.IsDefault ? [] : cardTypes;
        if (types.Any(t => !Enum.IsDefined(t)) || types.Distinct().Count() != types.Length)
            throw new ArgumentException("PredictorDeckPendingScopeInvalid");
        if (choiceId != null && (Choices.Any(c => c.ChoiceId == choiceId) ||
            _segments.Any(s => s.UnknownAddition?.ChoiceId == choiceId)))
            throw new ArgumentException("PredictorDeckChoiceAlreadyPresent:" + choiceId);
        var gap = new PredictorDeckKnowledgeGap(nodeId, choiceId, reason, PredictorDeckUncertainFact.All, [], types);
        // This explicit producer contract concerns additive Gold/Book effects
        // only. Card identities and their qualification remain unknown. A
        // settled receipt must not execute those hooks a second time here.
        // A producer can separately establish a real receipt count (including
        // copies). It proves only All cardinality, never target identities or
        // any attribute-based eligibility. Hook settlement remains independent.
        return new(_context, _segments.Add(new([], null, gap, receiptEffectsSettled, excludesBasicCards, knownCount)), _gaps, _countMutations, _possiblyPresent);
    }

    internal PredictorDeckKnowledge MarkChoiceAdditionPending(string nodeId, string choiceId,
        ImmutableArray<EffectCardType> cardTypes)
    {
        if (!Choices.Any(c => c.ChoiceId == choiceId))
            return MarkUnknownAddition(nodeId, "PendingEventCardAddition", choiceId, cardTypes);
        // This is an unprojected child of the existing option, not another
        // independent choice. Keep one gap for that option's possible types.
        var prior = _segments.FirstOrDefault(s => s.UnknownAddition?.ChoiceId == choiceId)?.UnknownAddition;
        var types = prior == null ? cardTypes : prior.CardTypes.IsEmpty || cardTypes.IsEmpty ? [] :
            prior.CardTypes.AddRange(cardTypes).Distinct().ToImmutableArray();
        var gap = new PredictorDeckKnowledgeGap(nodeId, choiceId, "PendingEventCardAddition",
            PredictorDeckUncertainFact.All, [], types);
        int index = _segments.FindIndex(s => s.UnknownAddition?.ChoiceId == choiceId);
        return new(_context, index < 0 ? _segments.Add(new([], null, gap)) : _segments.SetItem(index, new([], null, gap)),
            _gaps, _countMutations, _possiblyPresent);
    }

    internal PredictorDeckKnowledge SyncKnown(ImmutableArray<PredictorCard> cards)
    {
        if (cards.IsDefault || cards.Any(c => c == null) || cards.Select(c => c.Id).Distinct().Count() != cards.Length)
            throw new ArgumentException("PredictorDeckSyncInvalid");
        var result = this;
        foreach (var card in cards)
        {
            RequireKnownCard(card);
            var prior = result._segments.SelectMany(s => s.FixedCards).SingleOrDefault(c => c.Id == card.Id)
                ?? throw new ArgumentException("PredictorDeckKnownTargetMissing");
            if (prior != card) result = result.ReplaceKnown(card.Id, card, append: false);
        }
        return result;
    }

    // Consume a population projection, then retain the resulting abstract deck
    // mutation here. A future add is outside this prefix, so it contributes
    // normally instead of inheriting permanent type-wide uncertainty.
    internal PredictorDeckRandomUpgradeResult ApplyRandomUpgrade(string sourceNode, string sourceOperation,
        EffectCardType type, int maximumTargets = 2, ImmutableArray<long> selectedIds = default)
    {
        if (string.IsNullOrWhiteSpace(sourceNode) || string.IsNullOrWhiteSpace(sourceOperation) || maximumTargets < 0 ||
            type is not (EffectCardType.Attack or EffectCardType.Skill))
            throw new ArgumentException("PredictorDeckRandomUpgradeInvalid");
        if (_countMutations.Any(m => m.SourceNode == sourceNode && m.SourceOperation == sourceOperation))
            throw new ArgumentException("PredictorDeckUpgradeOperationAlreadyPresent");
        var scope = type == EffectCardType.Attack ? PredictorDeckProjectionScope.UpgradableAttack : PredictorDeckProjectionScope.UpgradableSkill;
        var before = Project(scope);
        var consumption = before.UnstableShuffleDraws;
        var candidateSources = before.UnresolvedCandidateSources;
        if (selectedIds.IsDefault && before.OrderedCandidatesKnown && before.KnownCount is { } forcedCount &&
            forcedCount <= maximumTargets)
            selectedIds = before.KnownCandidates.Select(c => c.Id).ToImmutableArray();
        if (!selectedIds.IsDefault)
        {
            if (!before.OrderedCandidatesKnown || before.KnownCount is not { } count ||
                selectedIds.Length != Math.Min(count, maximumTargets) || selectedIds.Distinct().Count() != selectedIds.Length ||
                selectedIds.Any(id => !before.KnownCandidates.Any(c => c.Id == id)))
                throw new ArgumentException("PredictorDeckUpgradeTargetsNotProven");
            var changed = before.KnownCandidates.Where(c => selectedIds.Contains(c.Id))
                .Select(c => PredictorCardLevelEffects.Reconcile(_context, c with { UpgradeLevel = c.UpgradeLevel + 1 })).ToImmutableArray();
            var synced = SyncKnown(changed);
            var concrete = new PredictorDeckKnowledge(_context, synced._segments, synced._gaps,
                synced._countMutations.Add(new(scope, _segments.Length, synced.Project(scope).Count, sourceNode, sourceOperation,
                    synced.Project(scope).UnresolvedCountSources)), synced._possiblyPresent);
            return new(concrete, consumption, before, sourceNode, sourceOperation, candidateSources, []);
        }
        if (before.Count?.Bounds.Upper == 0 || maximumTargets == 0)
            return new(this, consumption, before, sourceNode, sourceOperation, candidateSources, []);

        var possible = _segments.SelectMany(s => s.FixedCards.Concat(s.Choice?.Effects.SelectMany(e => e.AddedCards) ?? []))
            .Where(c => c.Type == type && c.UpgradeLevel < c.MaxUpgradeLevel).ToArray();
        bool singleLevel = before.Count != null && possible.All(c => c.MaxUpgradeLevel == 1);
        var after = singleLevel ? before.Count!.SubtractAndClamp(maximumTargets) : null;
        var reason = singleLevel ? "RandomUpgradeTargetIdentityNotKnown" : "RandomUpgradeCountDerivationNotImplemented";
        var gap = new PredictorDeckKnowledgeGap(sourceNode, null, reason,
            PredictorDeckUncertainFact.Upgrade, [], [type], sourceOperation);
        var gaps = _gaps.Add(new(gap, _segments.Length, OnlyUpgradable: true));
        // Reconciliation can change removal qualification only when the actual
        // captured levels differ in Eternal; preserve that concrete reader too.
        bool removalMayChange = possible.Any(UpgradeMayChangeRemoval);
        if (removalMayChange) gaps = gaps.Add(new(gap with { Facts = PredictorDeckUncertainFact.Removable },
            _segments.Length, OnlyUpgradable: true));
        var countSources = singleLevel ? before.UnresolvedCountSources : before.UnresolvedCandidateSources;
        var mutations = _countMutations.Add(new(scope, _segments.Length, after, sourceNode, sourceOperation, countSources));
        var removedFromEligible = after == null ? null : before.Count! - after;
        var allProjection = Project(PredictorDeckProjectionScope.Upgradable);
        var upgradedProjection = Project(PredictorDeckProjectionScope.Upgraded);
        var allUpgradable = allProjection.Count;
        var upgraded = upgradedProjection.Count;
        mutations = mutations.Add(new(PredictorDeckProjectionScope.Upgradable, _segments.Length,
            removedFromEligible == null || allUpgradable == null ? null : allUpgradable - removedFromEligible, sourceNode, sourceOperation,
            MergeSources(allProjection.UnresolvedCountSources, countSources)));
        mutations = mutations.Add(new(PredictorDeckProjectionScope.Upgraded, _segments.Length,
            removedFromEligible == null || upgraded == null ? null : upgraded + removedFromEligible, sourceNode, sourceOperation,
            MergeSources(upgradedProjection.UnresolvedCountSources, countSources)));
        var deck = new PredictorDeckKnowledge(_context, _segments, gaps, mutations, _possiblyPresent);
        return new(deck, consumption, before, sourceNode, sourceOperation, candidateSources,
            gaps.Skip(_gaps.Length).Select(g => g.Value).ToImmutableArray());
    }

    private bool UpgradeMayChangeRemoval(PredictorCard card)
    {
        var definition = _context.Catalog!.CardDefinitions.FirstOrDefault(d => d.Prototype.Key == card.Key);
        if (definition == null || definition.Levels.IsDefaultOrEmpty ||
            !definition.Levels.Any(l => l.UpgradeLevel == card.UpgradeLevel) ||
            !definition.Levels.Any(l => l.UpgradeLevel == card.UpgradeLevel + 1)) return true;
        return PredictorCardLevelEffects.Resolve(_context, card).Eternal !=
            PredictorCardLevelEffects.Resolve(_context, card with { UpgradeLevel = card.UpgradeLevel + 1 }).Eternal;
    }

    private ImmutableArray<CountMutation> AdjustCountMutations(int segmentIndex, PredictorCard prior, PredictorCard? next)
    {
        if (prior == next) return _countMutations;
        return _countMutations.Select(m =>
        {
            if (segmentIndex >= m.SegmentLimit || m.Count == null) return m;
            if (next != null && !ProjectionReadsChanged(prior, next, m.Scope)) return m;
            if (RelevantGaps(segmentIndex, prior, m.Scope).Any() || MetadataMissing(prior, m.Scope) ||
                next != null && MetadataMissing(next, m.Scope)) return m with { Count = null };
            long delta = (next != null && Matches(next, m.Scope) ? 1 : 0) - (Matches(prior, m.Scope) ? 1 : 0);
            return m with { Count = m.Count + delta };
        }).ToImmutableArray();
    }

    private static ImmutableArray<PredictorDeckRewardChoice> MergeSources(
        ImmutableArray<PredictorDeckRewardChoice> first, ImmutableArray<PredictorDeckRewardChoice> second) =>
        first.Concat(second).DistinctBy(c => c.ChoiceId).ToImmutableArray();

    private PredictorCard PreserveUncertainFields(int segmentIndex, PredictorCard prior, PredictorCard next)
    {
        var facts = _gaps.Where(g => segmentIndex < g.SegmentLimit &&
                (!g.OnlyUpgradable || prior.UpgradeLevel < prior.MaxUpgradeLevel) &&
                (g.Value.InstanceIds.IsEmpty || g.Value.InstanceIds.Contains(prior.Id)) &&
                (g.Value.CardTypes.IsEmpty || g.Value.CardTypes.Contains(prior.Type)))
            .Aggregate(PredictorDeckUncertainFact.None, (value, g) => value | g.Value.Facts);
        if ((facts & PredictorDeckUncertainFact.Type) != 0) next = next with { Type = prior.Type };
        if ((facts & PredictorDeckUncertainFact.Definition) != 0) next = next with { Key = prior.Key, MaxUpgradeLevel = prior.MaxUpgradeLevel };
        if ((facts & PredictorDeckUncertainFact.Upgrade) != 0) next = next with
        { UpgradeLevel = prior.UpgradeLevel, MaxUpgradeLevel = prior.MaxUpgradeLevel,
            HasEternalKeyword = prior.HasEternalKeyword, HasExhaustKeyword = prior.HasExhaustKeyword,
            HasInnateKeyword = prior.HasInnateKeyword, HasRetainKeyword = prior.HasRetainKeyword };
        if ((facts & PredictorDeckUncertainFact.Removable) != 0) next = next with { Removable = prior.Removable };
        if ((facts & PredictorDeckUncertainFact.Enchantment) != 0) next = next with
        { Enchantment = prior.Enchantment, EnchantmentAmount = prior.EnchantmentAmount };
        return next;
    }

    // Compare only inputs of the requested qualification. Persistent combat
    // counters and damage/block/cost changes are not upgrade eligibility reads.
    private static bool ProjectionReadsChanged(PredictorCard prior, PredictorCard next,
        PredictorDeckProjectionScope scope)
    {
        if (prior.Id != next.Id || prior.Removed != next.Removed) return true;
        if (RequiredType(scope) is { } type && prior.Type != type && next.Type != type) return false;
        bool upgrade = prior.UpgradeLevel != next.UpgradeLevel;
        bool model = prior.Key != next.Key;
        bool cardType = prior.Type != next.Type;
        bool enchantment = prior.Enchantment != next.Enchantment;
        return scope switch
        {
            PredictorDeckProjectionScope.All => false,
            PredictorDeckProjectionScope.Attacks or PredictorDeckProjectionScope.Skills or PredictorDeckProjectionScope.Powers => cardType,
            PredictorDeckProjectionScope.Upgradable => upgrade || prior.MaxUpgradeLevel != next.MaxUpgradeLevel,
            PredictorDeckProjectionScope.UpgradableAttack or PredictorDeckProjectionScope.UpgradableSkill =>
                cardType || upgrade || prior.MaxUpgradeLevel != next.MaxUpgradeLevel,
            PredictorDeckProjectionScope.Upgraded => upgrade,
            PredictorDeckProjectionScope.Removable => prior.Removable != next.Removable,
            PredictorDeckProjectionScope.PaelsTooth => upgrade || prior.MaxUpgradeLevel != next.MaxUpgradeLevel || prior.Removable != next.Removable,
            PredictorDeckProjectionScope.Transformable => model || prior.Removable != next.Removable,
            PredictorDeckProjectionScope.Cloneable or PredictorDeckProjectionScope.Basic or
                PredictorDeckProjectionScope.BasicStrike or PredictorDeckProjectionScope.BasicDefend => model,
            PredictorDeckProjectionScope.NonEternal => model || upgrade || enchantment,
            _ => model || cardType || upgrade || enchantment
        };
    }

    private ImmutableArray<CountMutation> InvalidateCountMutations(ScopedGap gap) => _countMutations.Select(m =>
        (_possiblyPresent.Any(c => c.SegmentIndex < m.SegmentLimit && GapAffectsProjection(gap, c.SegmentIndex, c.Card, m.Scope)) ||
        _segments.Take(m.SegmentLimit).Select((s, index) => (s, index)).Any(item =>
            item.s.FixedCards.Concat(item.s.Choice?.Effects.SelectMany(e => e.AddedCards) ?? [])
                .Any(card => GapAffectsProjection(gap, item.index, card, m.Scope)))) ? m with { Count = null } : m).ToImmutableArray();

    internal PredictorDeckProjection Project(PredictorDeckProjectionScope scope)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        var count = PredictorChoiceInt.Constant(0);
        bool countSupported = true, complete = true;
        var known = ImmutableArray.CreateBuilder<PredictorCard>();
        var gaps = ImmutableArray.CreateBuilder<PredictorDeckKnowledgeGap>();
        var countGaps = ImmutableArray.CreateBuilder<PredictorDeckKnowledgeGap>();
        var countSources = ImmutableArray.CreateBuilder<PredictorDeckRewardChoice>();
        var candidateSources = ImmutableArray.CreateBuilder<PredictorDeckRewardChoice>();
        for (int index = 0; index < _segments.Length; index++)
        {
            var segment = _segments[index];
            if (segment.UnknownAddition is { } addition && AdditionMayMatch(segment, scope))
            {
                if (scope == PredictorDeckProjectionScope.All && segment.KnownAdditionCount is { } received)
                    count += received;
                else { countSupported = false; countGaps.Add(addition); }
                complete = false;
                gaps.Add(addition);
            }
            foreach (var possible in _possiblyPresent.Where(c => c.SegmentIndex == index))
            {
                var affected = RelevantGaps(index, possible.Card, scope).ToArray();
                if (affected.Length == 0) continue;
                countSupported = false; complete = false;
                gaps.AddRange(affected); countGaps.AddRange(affected);
            }
            foreach (var card in segment.FixedCards)
            {
                var affected = RelevantGaps(index, card, scope).ToArray();
                if (affected.Length != 0)
                { countSupported = false; complete = false; gaps.AddRange(affected); countGaps.AddRange(affected); continue; }
                if (MetadataMissing(card, scope))
                {
                    countSupported = false; complete = false;
                    var gap = new PredictorDeckKnowledgeGap("deck-base", null, "DeckProjectionMetadataMissing:" + card.Key,
                        PredictorDeckUncertainFact.Definition, [card.Id], []);
                    gaps.Add(gap); countGaps.Add(gap);
                    continue;
                }
                if (!Matches(card, scope)) continue;
                known.Add(card);
                count += 1;
            }
            if (segment.Choice is not { } choice)
            { ApplyCountBoundary(index + 1, scope, ref count, ref countSupported, countSources, countGaps); continue; }
            var values = new long[choice.Effects.Length];
            bool blocked = false, candidatePending = false;
            for (int option = 0; option < choice.Effects.Length; option++)
            {
                var effect = choice.Effects[option];
                if (!effect.AdditionsKnown)
                {
                    blocked = true;
                    countSupported = false; complete = false;
                    var gap = new PredictorDeckKnowledgeGap(choice.NodeId, choice.ChoiceId, effect.UnsupportedReason!,
                        PredictorDeckUncertainFact.All, [], []);
                    gaps.Add(gap); countGaps.Add(gap);
                    continue;
                }
                foreach (var card in effect.AddedCards)
                {
                    var affected = RelevantGaps(index, card, scope).ToArray();
                    if (affected.Length != 0)
                    { blocked = true; countSupported = false; complete = false; gaps.AddRange(affected); countGaps.AddRange(affected); continue; }
                    if (MetadataMissing(card, scope))
                    {
                        blocked = true;
                        countSupported = false; complete = false;
                        var gap = new PredictorDeckKnowledgeGap(choice.NodeId, choice.ChoiceId, "DeckProjectionMetadataMissing:" + card.Key,
                            PredictorDeckUncertainFact.Definition, [], []);
                        gaps.Add(gap); countGaps.Add(gap);
                        continue;
                    }
                    if (Matches(card, scope)) { values[option]++; complete = false; candidatePending = true; }
                }
            }
            if (blocked || values.Distinct().Count() > 1) countSources.Add(choice);
            if (blocked || candidatePending) candidateSources.Add(choice);
            count += PredictorChoiceInt.Alternatives(choice.ChoiceId, choice.AlternativesContract, values);
            ApplyCountBoundary(index + 1, scope, ref count, ref countSupported, countSources, countGaps);
        }
        // A proven total equal to the guaranteed subset proves that no other
        // candidates exist. This also recovers the exact empty set after a
        // clamp, without claiming the earlier selected targets were known.
        if (countSupported && count.KnownValue == known.Count)
        { complete = true; candidateSources.Clear(); gaps.Clear(); }
        return new(scope, countSupported ? count : null, known.ToImmutable(), complete,
            Choices, gaps.Distinct().ToImmutableArray())
        { UnresolvedCountSources = countSupported && count.KnownValue != null ? [] : countSources.ToImmutable(),
            UnresolvedCandidateSources = candidateSources.ToImmutable(),
            UnresolvedCountGaps = countSupported ? [] : countGaps.Distinct().ToImmutableArray() };
    }

    private void ApplyCountBoundary(int segmentLimit, PredictorDeckProjectionScope scope,
        ref PredictorChoiceInt count, ref bool supported,
        ImmutableArray<PredictorDeckRewardChoice>.Builder countSources,
        ImmutableArray<PredictorDeckKnowledgeGap>.Builder countGaps)
    {
        var mutation = _countMutations.LastOrDefault(m => m.Scope == scope && m.SegmentLimit == segmentLimit);
        if (mutation == null) return;
        // Once every possible card in an old prefix is proved absent, an
        // obsolete unsupported mutation cannot prevent a freshly proved zero.
        // Unknown additions and retained membership alternatives defeat this.
        if (PrefixCannotContainCandidate(segmentLimit, scope)) return;
        count = mutation.Count ?? PredictorChoiceInt.Constant(0);
        supported = mutation.Count != null;
        // A count proof replaces this whole prefix, including its numeric
        // dependencies. Identity-only choice roots/gaps encountered while
        // scanning raw cards must not contaminate the restored expression.
        countSources.Clear(); countSources.AddRange(mutation.CountSources);
        if (supported) countGaps.Clear();
    }

    private bool PrefixCannotContainCandidate(int segmentLimit, PredictorDeckProjectionScope scope)
    {
        bool MayMatch(int index, PredictorCard card) => RelevantGaps(index, card, scope).Any() ||
            MetadataMissing(card, scope) || Matches(card, scope);
        if (_possiblyPresent.Any(c => c.SegmentIndex < segmentLimit && MayMatch(c.SegmentIndex, c.Card))) return false;
        for (int index = 0; index < segmentLimit; index++)
        {
            var segment = _segments[index];
            if (segment.UnknownAddition != null && AdditionMayMatch(segment, scope) ||
                segment.FixedCards.Any(c => MayMatch(index, c)) || segment.Choice is { } choice &&
                choice.Effects.Any(e => !e.AdditionsKnown || e.AddedCards.Any(c => MayMatch(index, c)))) return false;
        }
        return true;
    }

    private static bool AdditionMayMatch(Segment segment, PredictorDeckProjectionScope scope) =>
        // A producer may prove this static exclusion from the complete source
        // pool without knowing a particular replacement card or its attributes.
        !(segment.ExcludesBasicCards && scope is (PredictorDeckProjectionScope.Basic or
            PredictorDeckProjectionScope.BasicStrike or PredictorDeckProjectionScope.BasicDefend)) &&
        (RequiredType(scope) is not { } type || segment.UnknownAddition!.CardTypes.IsEmpty || segment.UnknownAddition.CardTypes.Contains(type));

    internal bool HasFixedMember(long id) => _segments.Any(s => s.FixedCards.Any(c => c.Id == id));

    // A fixed record may carry an uncertain property. Readers declare the
    // fields their operation consumes instead of demanding the entire deck or
    // accepting unrelated witness fields from a broad membership projection.
    internal PredictorCard? ReadTarget(long instanceId, PredictorDeckProjectionScope scope,
        PredictorDeckUncertainFact requiredFacts = PredictorDeckUncertainFact.All)
    {
        if (!TargetGaps(instanceId, scope, requiredFacts).IsEmpty) return null;
        var card = _segments.SelectMany(s => s.FixedCards).Single(c => c.Id == instanceId);
        return Matches(card, scope) ? card : null;
    }

    internal ImmutableArray<PredictorDeckKnowledgeGap> TargetGaps(long instanceId, PredictorDeckProjectionScope scope,
        PredictorDeckUncertainFact requiredFacts = PredictorDeckUncertainFact.All)
    {
        if (instanceId <= 0 || !Enum.IsDefined(scope) || (requiredFacts & ~PredictorDeckUncertainFact.All) != 0)
            throw new ArgumentException("PredictorDeckTargetReadInvalid");
        int index = _segments.FindIndex(s => s.FixedCards.Any(c => c.Id == instanceId));
        var card = index < 0 ? _possiblyPresent.FirstOrDefault(c => c.Card.Id == instanceId)?.Card :
            _segments[index].FixedCards.Single(c => c.Id == instanceId);
        if (index < 0 && card != null)
            index = _possiblyPresent.First(c => c.Card.Id == instanceId).SegmentIndex;
        var gaps = ImmutableArray.CreateBuilder<PredictorDeckKnowledgeGap>();
        var reads = requiredFacts | Reads(scope);
        if (card != null)
        {
            gaps.AddRange(_gaps.Where(g => index < g.SegmentLimit &&
                (!g.OnlyUpgradable || card.UpgradeLevel < card.MaxUpgradeLevel) &&
                (g.Value.Facts & reads) != 0 &&
                (g.Value.InstanceIds.IsEmpty || g.Value.InstanceIds.Contains(instanceId)) &&
                (g.Value.CardTypes.IsEmpty || g.Value.CardTypes.Contains(card.Type))).Select(g => g.Value));
            if (MetadataMissing(card, scope) || (reads & PredictorDeckUncertainFact.Definition) != 0 &&
                !_context.Catalog!.CardDefinitions.Any(d => d.Prototype.Key == card.Key))
                gaps.Add(new("deck-base", null, "DeckProjectionMetadataMissing:" + card.Key,
                    PredictorDeckUncertainFact.Definition, [instanceId], []));
        }
        if (!HasFixedMember(instanceId))
            gaps.Add(new("deck-base", null, "DeckTargetMembershipNotKnown",
                PredictorDeckUncertainFact.Membership, [instanceId], []));
        return gaps.Distinct().ToImmutableArray();
    }

    internal int GoldProjectionBoundary => _segments.Length;
    internal PredictorChoiceInt? ProjectGoldDelta(int startAt = 0) => ProjectEffect(e => e.GoldDelta, startAt);
    internal PredictorChoiceInt? ProjectBookCounterDelta(long relicId) => ProjectEffect(e =>
        e.AdditionsKnown ? e.BookCounterDeltas.Where(d => d.RelicId == relicId).Sum(d => d.CardsAdded) : null);

    private PredictorChoiceInt? ProjectEffect(Func<PredictorDeckRewardEffect, int?> read, int startAt = 0)
    {
        if (_segments.Skip(startAt).Any(s => s.UnknownAddition != null && !s.ReceiptEffectsSettled)) return null;
        var result = PredictorChoiceInt.Constant(0);
        foreach (var choice in _segments.Skip(startAt).Where(s => s.Choice != null).Select(s => s.Choice!))
        {
            var values = choice.Effects.Select(read).ToArray();
            if (values.Any(v => v == null)) return null;
            result += PredictorChoiceInt.Alternatives(choice.ChoiceId, choice.AlternativesContract,
                values.Select(v => (long)v!.Value).ToArray());
        }
        return result;
    }

    private IEnumerable<PredictorDeckKnowledgeGap> RelevantGaps(int segmentIndex, PredictorCard card,
        PredictorDeckProjectionScope scope) => _gaps.Where(g => GapAffectsProjection(g, segmentIndex, card, scope)).Select(g => g.Value);

    private bool GapAffectsProjection(ScopedGap gap, int segmentIndex, PredictorCard card,
        PredictorDeckProjectionScope scope) => GapReads(gap, segmentIndex, card, scope) &&
        // Membership alone cannot make a card satisfy a known false predicate.
        (((gap.Value.Facts & Reads(scope)) & ~PredictorDeckUncertainFact.Membership) != 0 ||
            MetadataMissing(card, scope) || Matches(card, scope));

    private static bool GapReads(ScopedGap g, int segmentIndex, PredictorCard card,
        PredictorDeckProjectionScope scope) => segmentIndex < g.SegmentLimit &&
            (!g.OnlyUpgradable || card.UpgradeLevel < card.MaxUpgradeLevel) &&
            (g.Value.Facts & Reads(scope)) != 0 &&
            (g.Value.InstanceIds.IsEmpty || g.Value.InstanceIds.Contains(card.Id)) &&
            (g.Value.CardTypes.IsEmpty || g.Value.CardTypes.Contains(card.Type)) &&
            ((g.Value.Facts & PredictorDeckUncertainFact.Type) != 0 ||
                RequiredType(scope) is not { } type || card.Type == type);

    private static EffectCardType? RequiredType(PredictorDeckProjectionScope scope) => scope switch
    {
        PredictorDeckProjectionScope.Attacks or PredictorDeckProjectionScope.UpgradableAttack or
            PredictorDeckProjectionScope.EnchantableAttack => EffectCardType.Attack,
        PredictorDeckProjectionScope.Skills or PredictorDeckProjectionScope.UpgradableSkill or
            PredictorDeckProjectionScope.EnchantableSkill => EffectCardType.Skill,
        PredictorDeckProjectionScope.Powers => EffectCardType.Power,
        _ => null
    };

    private bool MetadataMissing(PredictorCard card, PredictorDeckProjectionScope scope)
    {
        if (RequiredType(scope) is { } type && card.Type != type ||
            (Reads(scope) & PredictorDeckUncertainFact.Definition) == 0) return false;
        var definition = _context.Catalog!.CardDefinitions.FirstOrDefault(d => d.Prototype.Key == card.Key);
        if (definition == null) return true;
        return (scope is PredictorDeckProjectionScope.Enchantable or PredictorDeckProjectionScope.EnchantableAttack or
            PredictorDeckProjectionScope.EnchantableSkill or PredictorDeckProjectionScope.EnchantableDefend or
            PredictorDeckProjectionScope.NonEternal) && (definition.Levels.IsDefaultOrEmpty ||
                !definition.Levels.Any(l => l.UpgradeLevel == card.UpgradeLevel));
    }

    private bool Matches(PredictorCard card, PredictorDeckProjectionScope scope)
    {
        bool Upgradable() => card.UpgradeLevel < card.MaxUpgradeLevel;
        PredictorCardDefinition Definition() => PredictorCardChanges.Definition(_context, card.Key);
        bool Enchantable() => PredictorObtainSources.CanEnchant(_context, card);
        return scope switch
        {
            PredictorDeckProjectionScope.All => true,
            PredictorDeckProjectionScope.Attacks => card.Type == EffectCardType.Attack,
            PredictorDeckProjectionScope.Skills => card.Type == EffectCardType.Skill,
            PredictorDeckProjectionScope.Powers => card.Type == EffectCardType.Power,
            PredictorDeckProjectionScope.Upgradable => Upgradable(),
            PredictorDeckProjectionScope.UpgradableAttack => card.Type == EffectCardType.Attack && Upgradable(),
            PredictorDeckProjectionScope.UpgradableSkill => card.Type == EffectCardType.Skill && Upgradable(),
            PredictorDeckProjectionScope.Upgraded => card.UpgradeLevel > 0,
            PredictorDeckProjectionScope.Removable => card.Removable,
            PredictorDeckProjectionScope.PaelsTooth => card.Removable && Upgradable(),
            PredictorDeckProjectionScope.Transformable => card.Removable && !Definition().IsQuest,
            PredictorDeckProjectionScope.Cloneable => !Definition().IsQuest,
            PredictorDeckProjectionScope.Enchantable => Enchantable(),
            PredictorDeckProjectionScope.EnchantableAttack => card.Type == EffectCardType.Attack && Enchantable(),
            PredictorDeckProjectionScope.EnchantableSkill => card.Type == EffectCardType.Skill && Enchantable(),
            PredictorDeckProjectionScope.EnchantableDefend => Definition().IsDefend && Enchantable(),
            PredictorDeckProjectionScope.Basic => Definition().Rarity == "Basic",
            PredictorDeckProjectionScope.BasicStrike => Definition().Rarity == "Basic" && Definition().IsStrike,
            PredictorDeckProjectionScope.BasicDefend => Definition().Rarity == "Basic" && Definition().IsDefend,
            PredictorDeckProjectionScope.NonEternal => !PredictorCardLevelEffects.Resolve(_context, card).Eternal,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }

    private static PredictorDeckUncertainFact Reads(PredictorDeckProjectionScope scope) =>
        PredictorDeckUncertainFact.Membership | (scope switch
        {
            PredictorDeckProjectionScope.All => PredictorDeckUncertainFact.None,
            PredictorDeckProjectionScope.Attacks or PredictorDeckProjectionScope.Skills or PredictorDeckProjectionScope.Powers => PredictorDeckUncertainFact.Type,
            PredictorDeckProjectionScope.Upgradable or PredictorDeckProjectionScope.Upgraded => PredictorDeckUncertainFact.Upgrade,
            PredictorDeckProjectionScope.UpgradableAttack or PredictorDeckProjectionScope.UpgradableSkill => PredictorDeckUncertainFact.Type | PredictorDeckUncertainFact.Upgrade,
            PredictorDeckProjectionScope.Removable => PredictorDeckUncertainFact.Removable,
            PredictorDeckProjectionScope.PaelsTooth => PredictorDeckUncertainFact.Removable | PredictorDeckUncertainFact.Upgrade,
            PredictorDeckProjectionScope.Transformable => PredictorDeckUncertainFact.Removable | PredictorDeckUncertainFact.Definition,
            PredictorDeckProjectionScope.Cloneable or PredictorDeckProjectionScope.Basic or PredictorDeckProjectionScope.BasicStrike or PredictorDeckProjectionScope.BasicDefend => PredictorDeckUncertainFact.Definition,
            _ => PredictorDeckUncertainFact.Type | PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Definition
        });

    private static void RequireKnownCard(PredictorCard card)
    {
        if (card == null || card.Id < 1 || card.Key.Category != "CARD" || !card.Key.IsValid || card.Removed ||
            !Enum.IsDefined(card.Type) || card.UpgradeLevel < 0 || card.MaxUpgradeLevel < card.UpgradeLevel)
            throw new ArgumentException("PredictorDeckKnownCardInvalid");
    }
}
