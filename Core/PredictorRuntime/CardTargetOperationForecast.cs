using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// An additional target is a conditional input, never a native selection.
// Each query restores the captured suspended picker, including paid obtains.
internal static class CardTargetOperationForecast
{
    internal static OperationForecast Build(PredictorOperationSnapshot snapshot,long cardId,
        CancellationToken token=default,bool includeContinuation=true,bool includeCombat=true)
    {
        var worker = new Worker(snapshot);
        return worker.Finish(worker.Build(snapshot,cardId,token,includeContinuation,includeCombat)) with {Activity=OperationForecastActivity.ProposedTargets};
    }

    private sealed class Worker(PredictorOperationSnapshot captured)
    {
        private readonly OperationForecastExecution execution = new(captured);
        internal OperationForecast Finish(OperationForecast result) => result with
        { Fingerprint=captured.Fingerprint, Evaluations=result.Evaluations.AddRange(execution.Evaluations) };

        internal OperationForecast Build(PredictorOperationSnapshot snapshot, long hoveredCardId,
            CancellationToken token = default, bool includeContinuation = true, bool includeCombat = true)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            token.ThrowIfCancellationRequested();
            if (TransformSelectionForecast.Source(snapshot) is { } source)
                return TransformTargets(snapshot, source, hoveredCardId, token);
            if (snapshot.Scene is PredictorOperationEventScene { AwaitingCards: true } ev)
                return EventTargets(snapshot, ev, hoveredCardId, token, includeContinuation, includeCombat);
            return Empty(snapshot.State.Cards.FirstOrDefault(c => c.Id == hoveredCardId)?.Key ?? new("CARD", "UNKNOWN"));
        }

        private OperationForecast TransformTargets(PredictorOperationSnapshot snapshot, ModelKey source,
            long hovered, CancellationToken token)
        {
            var present = execution.Evaluate(ImmutableArray<PredictorInput>.Empty, token);
            var selection = present.Waiting.Selection;
            var candidates = snapshot.State.Deck.Where(selection.CandidateCardIds.Contains).ToImmutableArray();
            var selected = snapshot.SelectedCardIds.IsEmpty && snapshot.Scene is PredictorOperationEventScene ev
                ? ev.SelectedCardIds : snapshot.SelectedCardIds;
            int required = selection.MaximumCardCount;
            if (required <= 0 || !candidates.Contains(hovered) || selected.IsDefault ||
                selected.Distinct().Count() != selected.Length || selected.Any(id => !candidates.Contains(id)) ||
                selected.Length > required || selected.Length == required && !selected.Contains(hovered)) return Empty(source);
            int current = selected.Contains(hovered) ? selected.IndexOf(hovered) : selected.Length;
            var otherSelected = selected.Where(id => id != hovered).ToImmutableArray();
            var groups = ImmutableArray.CreateBuilder<OperationOutcomeChoices>();
            var extras = new Dictionary<string, OperationOutcomeSource>();
            var faults = ImmutableArray.CreateBuilder<OperationForecastFault>();
            bool representativeFillers = FillerTargetsAreIndependent(snapshot, source, candidates);
            int examined = 0;
            for (int slot = 0; slot < required; slot++)
            {
                var cards = new List<PredictorCard>();
                int faultsBefore = faults.Count;
                bool reduceSlot = representativeFillers;
                foreach (var targets in Targets(slot, []))
                {
                    examined++;
                    var input = TransformSelectionForecast.Input(snapshot, targets);
                    try
                    {
                        var result = execution.Evaluate([input], token);
                        var forecast = TransformSelectionForecast.Read(result, targets).Single(item => item.OriginalId == hovered).Card;
                        if (!cards.Any(card => (card with { Id = 0 }) == (forecast with { Id = 0 }))) cards.Add(forecast);
                        foreach (var section in RelicOperationForecast.ObserveResult(snapshot.Context, snapshot.State, present, result, source))
                        {
                            var extra = section with { Rows = section.Rows.Where(row => row.Action is not
                                (OperationOutcomeAction.TransformCard or OperationOutcomeAction.RemoveCard))
                                .Select(row => row with { Possible = true }).ToImmutableArray() };
                            if (!extra.Rows.IsEmpty || !extra.CardGroups.IsEmpty) extras.TryAdd(SectionSignature(extra), extra);
                        }
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        faults.Add(new("HoveredTransform:" + source.Entry + ":" + (slot + 1), [input], error));
                        // Preserve the failure and other independent paths if the
                        // representative input itself cannot complete.
                        reduceSlot = false;
                    }
                    if (reduceSlot) break;
                }
                if (cards.Count > 0) groups.Add(new(-101 - slot, cards.ToImmutableArray(), 1, 1, false, cards.Count > 1 || faults.Count != faultsBefore)
                    { Kind = (slot + 1).ToString(), ProposedTarget = true });
            }
            var sections = ImmutableArray.CreateBuilder<OperationOutcomeSource>();
            if (groups.Count > 0) sections.Add(new(source, [], groups.ToImmutable())
                { Category = OperationOutcomeCategory.CardTransform });
            sections.AddRange(extras.Values);
            return new(source, [], [], false,
                [new([], sections.ToImmutable(), [], examined, faults.Count == 0)
                    { Faulted = faults.Count > 0 }], [])
            { Faults = faults.ToImmutable(), OptionKey = (snapshot.Scene as PredictorOperationEventScene)?.Branch,
                TargetTransformation = true, ProposedTargetOrdinal = current };

            IEnumerable<ImmutableArray<long>> Targets(int hoveredSlot, ImmutableArray<long> prefix)
            {
                token.ThrowIfCancellationRequested();
                if (prefix.Length == required) { yield return prefix; yield break; }
                int index = prefix.Length;
                int otherIndex = index < hoveredSlot ? index : index - 1;
                IEnumerable<long> next = index == hoveredSlot ? [hovered] : otherIndex < otherSelected.Length
                    ? [otherSelected[otherIndex]] : candidates.Where(id => id != hovered && !otherSelected.Contains(id));
                foreach (long id in next)
                    if (!prefix.Contains(id))
                        foreach (var result in Targets(hoveredSlot, prefix.Add(id))) yield return result;
            }
        }

        private bool FillerTargetsAreIndependent(PredictorOperationSnapshot snapshot, ModelKey source,
            ImmutableArray<long> candidates)
        {
            if (source.Entry != "ASTROLABE" || !snapshot.Context.Modifiers.IsEmpty ||
                snapshot.Context.Operation is not { } operation || operation.OpaqueModels.IsDefault) return false;
            // A direct obtain ends at its ordinary parent. A suspended composite
            // parent could resume Whetstone or another deck-dependent effect, whose
            // results and extra panels still require the full ordered paths.
            bool direct = snapshot.Scene is PredictorOperationShopScene { PendingRelic: not null } or
                PredictorOperationObtainScene { Destination: PredictorObtainReturn.Ancient, Parent: PredictorOperationAncientScene } or
                PredictorOperationObtainScene { Destination: PredictorObtainReturn.Reward, Parent: PredictorOperationRewardsScene };
            if (!direct || operation.IgnoresBehavior(snapshot.Context.Character) || candidates.Any(id =>
                operation.IgnoresBehavior(snapshot.State.Card(id).Key) || snapshot.State.Card(id).Enchantment is { } enchantment &&
                    operation.IgnoresBehavior(enchantment))) return false;
            if (snapshot.State.Relics.Any(relic => !relic.Melted && (operation.IgnoresBehavior(relic.Key) || relic.Key.Entry is
                "MOLTEN_EGG" or "TOXIC_EGG" or "FROZEN_EGG" or "FRESNEL_LENS" or "BING_BONG" or
                "LUCKY_FYSH" or "DARKSTONE_PERIAPT" or "BOOK_OF_FIVE_RINGS"))) return false;
            // Native Astrolabe and the detached Transform each draw once per target;
            // NextInt uses one NextDouble regardless of pool size. With no add-card
            // listeners or resumed parent, fillers cannot change the hovered card at
            // a given ordinal, even across character/colorless pools. Use one legal
            // complete tuple per slot through the existing operation authority.
            return true;
        }

        private OperationForecast EventTargets(PredictorOperationSnapshot snapshot,
            PredictorOperationEventScene scene, long hovered, CancellationToken token, bool includeContinuation, bool includeCombat)
        {
            var selected = snapshot.SelectedCardIds.IsEmpty ? scene.SelectedCardIds : snapshot.SelectedCardIds;
            var prefix = Prefix(selected, hovered, scene.CardCandidates, scene.CardsRequired);
            if (prefix.IsEmpty) return Empty(scene.Event);
            var slots = Enumerable.Range(0, scene.CardsRequired).Select(_ => new List<PredictorCard>()).ToArray();
            var sections = ImmutableArray.CreateBuilder<OperationOutcomeSource>();
            var sectionSignatures = new HashSet<string>();
            var faults = ImmutableArray.CreateBuilder<OperationForecastFault>();
            var pending = ImmutableArray.CreateBuilder<string>();
            var branchPending = ImmutableArray.CreateBuilder<string>();
            var notices = ImmutableArray.CreateBuilder<string>();
            var evaluations = ImmutableArray.CreateBuilder<OperationForecastEvaluation>();
            var combatEvaluations = ImmutableArray.CreateBuilder<OperationForecastCombatEvaluation>();
            int examined = 0;
            bool complete = true, faulted = false;
            foreach (var targets in Complete(scene.CardCandidates, scene.CardsRequired, prefix, token))
            {
                token.ThrowIfCancellationRequested();
                examined++;
                var result = Conditional(EventOperationForecast.BuildSelected(snapshot, targets, token, includeContinuation, includeCombat));
                faults.AddRange(result.Faults);
                evaluations.AddRange(result.Evaluations);combatEvaluations.AddRange(result.CombatEvaluations);
                pending.AddRange(result.PendingReasons);
                notices.AddRange(result.Notices);
                foreach (var branch in result.Branches)
                {
                    complete &= branch.FullyEnumerated;
                    faulted |= branch.Faulted;
                    branchPending.AddRange(branch.PendingReasons);
                    foreach (var section in branch.Sections)
                    {
                        bool TransformSlot(OperationOutcomeChoices group, out int slot)
                        {
                            slot = -1;
                            return section.Source == scene.Event && int.TryParse(group.Kind, out int number) &&
                                (slot = number - 1) >= 0 && slot < slots.Length && group.Id == -1 - slot;
                        }
                        foreach (var group in section.CardGroups)
                            if (TransformSlot(group, out int slot))
                                foreach (var card in group.Cards)
                                    if (!slots[slot].Any(existing => (existing with { Id = 0 }) == (card with { Id = 0 })))
                                        slots[slot].Add(card);
                        var extra = section with
                        {
                            Rows = section.Rows.Where(r => r.Action is not
                                (OperationOutcomeAction.RemoveCard or OperationOutcomeAction.EnchantCard)).ToImmutableArray(),
                            CardGroups = section.CardGroups.Where(group => !TransformSlot(group, out _)).ToImmutableArray()
                        };
                        if ((!extra.Rows.IsEmpty || !extra.CardGroups.IsEmpty) && sectionSignatures.Add(SectionSignature(extra)))
                            sections.Add(extra);
                    }
                }
            }
            int hoveredSlot = prefix.IndexOf(hovered);
            var groups = slots.Select((cards, slot) => new OperationOutcomeChoices(-1 - slot,
                    cards.ToImmutableArray(), 1, 1, false, cards.Count > 1 || !complete || pending.Count > 0 || faults.Count > 0)
                    { Kind = (slot + 1).ToString(), ProposedTarget = true })
                .Where(group => !group.Cards.IsEmpty).ToImmutableArray();
            if (!groups.IsEmpty)
            {
                sections.Insert(0, new(scene.Event, [], groups) { Kind = "CardTargetConditional",
                    Category = OperationOutcomeCategory.CardTransform });
                if (groups.Length > 1 && examined > 1) notices.Add("EventTransformSlotAlternativesRelated");
            }
            if (sections.Count == 0 && faults.Count == 0 && pending.Count == 0 && branchPending.Count == 0) return Empty(scene.Event);
            // Each group is the marginal result for one exact input position under
            // this prefix. Cross-slot choices are related; no Cartesian combination
            // or explicit tuple branch is claimed by this compact presentation.
            var combined = new OperationForecastBranch([], sections.ToImmutable(), branchPending.Distinct().ToImmutableArray(),
                examined, complete && pending.Count == 0 && faults.Count == 0) { Faulted = faulted || faults.Count > 0 };
            return new(scene.Event, [], [], false, [combined], pending.Distinct().ToImmutableArray())
            { Faults = faults.ToImmutable(), OptionKey = scene.Branch, ProposedTargetOrdinal = hoveredSlot,
                Notices = notices.Distinct().ToImmutableArray(), Evaluations=evaluations.ToImmutable(), CombatEvaluations=combatEvaluations.ToImmutable() };
        }

        private string SectionSignature(OperationOutcomeSource section) => section.Source + ":" + section.Kind + ":" +
            string.Join(";", section.Rows.Select(row => row.Action + ":" + row.Kind + ":" + string.Join("|", row.Models) + ":" +
                string.Join("|", row.Cards.Select(card => card with { Id = 0 })))) + ":" +
            string.Join(";", section.CardGroups.Select(group => group.Id + ":" + group.Kind + ":" + group.MinimumSelect + ":" +
                group.MaximumSelect + ":" + group.CanSkip + ":" + string.Join("|", group.Cards.Select(card => card with { Id = 0 }))));

        private ImmutableArray<long> Prefix(ImmutableArray<long> selected, long hovered,
            ImmutableArray<long> candidates, int maximum)
        {
            if (selected.IsDefault || selected.Distinct().Count() != selected.Length || !candidates.Contains(hovered) ||
                selected.Any(id => !candidates.Contains(id))) return [];
            var prefix = selected.Contains(hovered) ? selected : selected.Add(hovered);
            return prefix.Length <= maximum ? prefix : [];
        }

        private IEnumerable<ImmutableArray<long>> Complete(ImmutableArray<long> candidates, int required,
            ImmutableArray<long> prefix, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (prefix.Length == required) { yield return prefix; yield break; }
            foreach (long id in candidates)
                if (!prefix.Contains(id))
                    foreach (var result in Complete(candidates, required, prefix.Add(id), token)) yield return result;
        }

        private OperationForecast Conditional(OperationForecast preview) => preview with
        {
            Branches = preview.Branches.Select(branch => branch with
            {
                Sections = branch.Sections.Select(section => section with
                {
                    Kind = section.Kind ?? "CardTargetConditional",
                    Rows = section.Rows.Select(row => row with { Possible = true }).ToImmutableArray(),
                    CardGroups = section.CardGroups.Select(group => group with
                        { Possible = group.Possible && (!branch.FullyEnumerated || group.Cards.Length != 1) }).ToImmutableArray()
                }).ToImmutableArray()
            }).ToImmutableArray()
        };

        private OperationForecast Empty(ModelKey source) => new(source, [], [], false, [], []);
    }
}
