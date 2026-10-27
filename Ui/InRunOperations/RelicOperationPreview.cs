using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunOperations;

// A detached presentation projection. Every conditional path is an explicit
// Operation input sequence; there is no default choice or native model here.
internal static class RelicOperationPreview
{
    private const int MaximumEvaluations = 512, MaximumDepth = 32;
    private sealed record Observation(ModelKey Source, OperationPreviewRow? Row = null, OperationPreviewCardGroup? Group = null);
    private sealed record Path(ImmutableArray<Observation> Observations, ImmutableArray<string> Pending, bool Faulted = false);
    private sealed record Choice(ImmutableArray<PredictorInput> Inputs, ModelKey Source);

    internal static OperationPreview Build(PredictorOperationSnapshot snapshot, long actualOfferId, CancellationToken token = default,
        bool includeContinuation = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Scene is not PredictorOperationAncientScene ancient)
            throw new ArgumentException("RelicPreviewRequiresActualAncientScene", nameof(snapshot));
        var offer = ancient.Offers.SingleOrDefault(o => o.Id == actualOfferId)
            ?? throw new ArgumentException("NeowPreviewOfferNotInSnapshot", nameof(actualOfferId));
        return Build(snapshot, [new ChooseAncientOffer(actualOfferId)], offer.Relic, token, includeContinuation);
    }

    internal static OperationPreview Build(PredictorOperationSnapshot snapshot,
        ImmutableArray<PredictorInput> rootInputs, ModelKey source, CancellationToken token = default,
        bool includeContinuation = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (rootInputs.IsDefault || source.Category != "RELIC" || !source.IsValid)
            throw new ArgumentException("RelicPreviewRootInvalid");
        var faults = ImmutableArray.CreateBuilder<OperationPreviewFault>();
        var before = PredictorOperation.Preview(snapshot, ImmutableArray<PredictorInput>.Empty, token);
        PredictorOperationResult initial;
        try { initial = PredictorOperation.Preview(snapshot, rootInputs, token); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return new(source, [], [], false, [], []) { Faults = [new("ChooseAncientOffer", rootInputs, ex)] }; }
        if (initial.Disposition != PredictorInputResult.Accepted)
            return new(source, [], [], false, [], ["OfferSelection:" + initial.Disposition]);

        if (source.Entry is "NEW_LEAF" or "ASTROLABE")
            return TransformAlternatives(snapshot, rootInputs, source, before, initial, token);

        var rootBones = source.Entry == "NEOWS_BONES"
            ? initial.Rewards.Where(r => r.Origin == PredictorRewardOrigin.BonesInternal).ToImmutableArray() : [];
        var orders = includeContinuation && rootBones.Length == 2
            ? new[] { rootBones, rootBones.Reverse().ToImmutableArray() }
            : new[] { ImmutableArray<PredictorReward>.Empty };
        int evaluations = 1;
        var branches = ImmutableArray.CreateBuilder<OperationPreviewBranch>();
        foreach (var order in orders)
        {
            var leaves = new List<Path>();
            var observations = Observe(snapshot.Context, snapshot.State, before,
                initial, source, source, rootBones);
            Explore(rootInputs, initial, source, observations, 0);
            var reasons = leaves.SelectMany(p => p.Pending).Distinct().ToImmutableArray();
            branches.Add(Aggregate(order.Select(r => r.Key).ToImmutableArray(), leaves, reasons));

            void Explore(ImmutableArray<PredictorInput> inputs, PredictorOperationResult result, ModelKey currentSource,
                ImmutableArray<Observation> seen, int depth)
            {
                token.ThrowIfCancellationRequested();
                // This is an intentional display scope, not a missing player
                // choice. Keep the initial hidden contents without exploring
                // hypothetical claims or adding a manufactured pending reason.
                if (!includeContinuation && source.Entry is "NEOWS_BONES" or "LARGE_CAPSULE")
                { leaves.Add(new(seen, [])); return; }
                if (result.Waiting.Phase == PredictorPhase.Complete)
                { leaves.Add(new(seen, [])); return; }
                if (result.Waiting.Phase is PredictorPhase.Shop or PredictorPhase.EventChoice or PredictorPhase.RestSite ||
                    result.Waiting.Phase == PredictorPhase.Rewards &&
                    !before.Rewards.IsEmpty && !result.Rewards.IsEmpty &&
                    result.Rewards.All(r => before.Rewards.Any(p => p.Id == r.Id)))
                { leaves.Add(new(seen, [])); return; }
                if (depth >= MaximumDepth)
                { leaves.Add(new(seen, ["ContinuationDepthLimit:32:" + result.Waiting.Phase])); return; }
                // Immediate offerings already answer this hover. Only the two
                // composite obtains require conditional choices to uncover
                // their subsequent automatic outcomes.
                if (source.Entry is not ("NEOWS_BONES" or "LARGE_CAPSULE"))
                { leaves.Add(new(seen, ["AwaitingPlayerChoice:" + result.Waiting.Phase])); return; }
                if (rootBones.Length == 2 && result.Waiting.Phase == PredictorPhase.Rewards &&
                    BonesClaimsSettled(result, rootBones))
                { leaves.Add(new(seen, [])); return; }
                var choices = Choices(result, currentSource, order).ToArray();
                if (choices.Length == 0)
                { leaves.Add(new(seen, ["UnresolvedPlayerChoice:" + result.Waiting.Phase])); return; }
                var alternatives = result.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && r.Kind == PredictorRewardKind.Card && r.CanReroll)
                    ? ImmutableArray.Create("RewardRerollAlternativesNotEnumerated") : ImmutableArray<string>.Empty;
                if (result.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && r.Kind == PredictorRewardKind.Card) &&
                    result.State.Relics.Any(r => !r.Melted && r.Key.Entry == "PAELS_WING"))
                    alternatives = alternatives.Add("RewardSacrificeAlternativesNotEnumerated");
                foreach (var choice in choices)
                {
                    token.ThrowIfCancellationRequested();
                    if (evaluations >= MaximumEvaluations)
                    { leaves.Add(new(seen, alternatives.Add("ContinuationEvaluationLimit:512:" + result.Waiting.Phase))); break; }
                    evaluations++;
                    var nextInputs = inputs.AddRange(choice.Inputs);
                    PredictorOperationResult next;
                    try { next = PredictorOperation.Preview(snapshot, nextInputs, token); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        faults.Add(new("Continue:" + result.Waiting.Phase, nextInputs, ex));
                        leaves.Add(new(seen, alternatives, true)); continue;
                    }
                    if (next.Disposition != PredictorInputResult.Accepted)
                    { leaves.Add(new(seen, alternatives.Add("ContinuationInput:" + result.Waiting.Phase + ":" + next.Disposition))); continue; }
                    int beforeLeaves = leaves.Count;
                    Explore(nextInputs, next, choice.Source,
                        seen.AddRange(Observe(snapshot.Context, result.State, result, next, choice.Source, source, rootBones)), depth + 1);
                    if (!alternatives.IsEmpty)
                        for (int i = beforeLeaves; i < leaves.Count; i++) leaves[i] = leaves[i] with { Pending = leaves[i].Pending.AddRange(alternatives) };
                }
            }
        }
        var all = branches.ToImmutable();
        // Keep the real generation order as the representative. A missing
        // continuation or a failed path cannot prove the two orders equal.
        if (rootBones.Length == 2 && faults.Count == 0 && all.Length == 2 &&
            SufficientForOrderComparison(all[0]) && SufficientForOrderComparison(all[1]) && SameVisibleResults(all[0], all[1]))
            all = [all[0]];
        var contained = rootBones.Select(r => r.Key).ToImmutableArray();
        // A shared root curse is a statement about every pickup order. A union
        // here would lose which order produces a certain (or conditional)
        // curse. Keep those outcomes in their branch unless all agree.
        var curses = source.Entry == "NEOWS_BONES" ? SharedBonesCurse(all, source) : [];
        return new(source, contained, curses, false, all, []) { Faults = faults.ToImmutable() };
    }

    private static ImmutableArray<PredictorCard> SharedBonesCurse(ImmutableArray<OperationPreviewBranch> branches, ModelKey source)
    {
        PredictorCard? shared = null;
        foreach (var branch in branches)
        {
            var rows = branch.Sections.Where(s => s.Source == source).SelectMany(s => s.Rows)
                .Where(r => r.Action == OperationPreviewAction.AddCard && r.Cards.Any(c => c.Type == EffectCardType.Curse)).ToArray();
            if (rows.Length != 1 || rows[0].Possible || rows[0].Cards is not [var curse] ||
                shared != null && (shared with { Id = 0 }) != (curse with { Id = 0 })) return [];
            shared ??= curse;
        }
        return shared == null ? [] : [shared];
    }

    private static bool SufficientForOrderComparison(OperationPreviewBranch branch) => !branch.Faulted &&
        (branch.FullyEnumerated || branch.PendingReasons.Length > 0 &&
            branch.PendingReasons.All(p => p.StartsWith("AwaitingPlayerChoice:", StringComparison.Ordinal)) &&
            branch.Sections.Any(s => !s.CardGroups.IsEmpty));

    // A single observed transition only: the caller owns continuation and
    // completeness. Public/fixed additions are deliberately not inferred.
    internal static ImmutableArray<OperationPreviewSection> ObserveResult(PredictorContext context,
        PredictorState before, PredictorOperationResult? previous, PredictorOperationResult after, ModelKey source) =>
        Aggregate([], [new(Observe(context, before, previous, after, source, source, []), [])], []).Sections;

    internal static OperationPreview BuildTransformTargets(PredictorOperationSnapshot snapshot, ModelKey source,
        ImmutableArray<long> orderedPrefix, CancellationToken token = default)
    {
        var present = PredictorOperation.Preview(snapshot, ImmutableArray<PredictorInput>.Empty, token);
        if (source.Entry is not ("NEW_LEAF" or "ASTROLABE") || orderedPrefix.IsDefaultOrEmpty ||
            orderedPrefix.Distinct().Count() != orderedPrefix.Length ||
            orderedPrefix.Any(id => !present.Waiting.Selection.CandidateCardIds.Contains(id)) ||
            source.Entry == "NEW_LEAF" && (present.Waiting.Phase != PredictorPhase.TransformCards || orderedPrefix.Length != 1) ||
            source.Entry == "ASTROLABE" && (present.Waiting.Phase != PredictorPhase.ObtainDeckCards ||
                orderedPrefix.Length > present.Waiting.Selection.MaximumCardCount))
            throw new ArgumentException("RelicPreviewTargetPrefixInvalid", nameof(orderedPrefix));
        // Target enumeration produces the requested primary card outcomes.
        // There is no separate continuation explorer here; completing a target
        // may synchronously settle its parent, whose display the caller filters.
        return TransformAlternatives(snapshot, [], source, present, present, token, orderedPrefix);
    }

    private static OperationPreview TransformAlternatives(PredictorOperationSnapshot snapshot,
        ImmutableArray<PredictorInput> rootInputs, ModelKey source, PredictorOperationResult before,
        PredictorOperationResult initial, CancellationToken token, ImmutableArray<long> orderedPrefix = default)
    {
        var faults = ImmutableArray.CreateBuilder<OperationPreviewFault>();
        var pending = ImmutableArray.CreateBuilder<string>();
        var outcomes = new Dictionary<int, List<(PredictorCard Card, int Rank)>>();
        var extras = new Dictionary<string, (Observation Value, int Paths)>();
        int examined = 0;
        // A direct ancient obtain has no suspended parent to resume after the
        // transform. Equal target cards then differ only by the removed
        // instance: Transform uses their definition/pool and adds a fresh
        // prototype through the same listeners. Enumerate that input once.
        // Suspended pickers retain exact instance paths because completing
        // one can resume a parent (e.g. Capsule -> Whetstone) with new targets.
        bool cardAddHooks = snapshot.State.Relics.Any(r => !r.Melted && r.Key.Entry is
            "MOLTEN_EGG" or "TOXIC_EGG" or "FROZEN_EGG" or "FRESNEL_LENS" or "BING_BONG" or
            "LUCKY_FYSH" or "DARKSTONE_PERIAPT" or "BOOK_OF_FIVE_RINGS");
        Func<long, PredictorCard>? equivalentTarget = snapshot.Scene is PredictorOperationAncientScene &&
            rootInputs is [ChooseAncientOffer] && !cardAddHooks && snapshot.Context.Modifiers.IsEmpty
            ? id => initial.State.Card(id) with { Id = 0 } : null;
        bool waiting = source.Entry == "NEW_LEAF" ? initial.Waiting.Phase == PredictorPhase.TransformCards :
            initial.Waiting.Phase == PredictorPhase.ObtainDeckCards;
        IEnumerable<ImmutableArray<PredictorInput>> Inputs()
        {
            if (!waiting) { yield return rootInputs; yield break; }
            var selection = initial.Waiting.Selection;
            if (source.Entry == "NEW_LEAF")
            {
                IEnumerable<long> candidates = orderedPrefix.IsDefault
                    ? initial.State.Deck.Where(selection.CandidateCardIds.Contains) : orderedPrefix;
                if (equivalentTarget != null) candidates = candidates.DistinctBy(equivalentTarget);
                foreach (long id in candidates)
                { token.ThrowIfCancellationRequested(); yield return rootInputs.Add(new SelectTransformationCard(id)); }
            }
            else
                for (int count = selection.MinimumCardCount; count <= selection.MaximumCardCount; count++)
                    foreach (var ids in OrderedTargets(initial.State.Deck.Where(selection.CandidateCardIds.Contains).ToImmutableArray(), count,
                        orderedPrefix.IsDefault ? [] : orderedPrefix, token, equivalentTarget))
                        yield return rootInputs.Add(new ChooseObtainDeckCards(ids));
        }
        foreach (var inputs in Inputs())
        {
            token.ThrowIfCancellationRequested();
            examined++;
            PredictorOperationResult result;
            try { result = inputs == rootInputs ? initial : PredictorOperation.Preview(snapshot, inputs, token); }
            catch (Exception error) when (error is not OperationCanceledException)
            { faults.Add(new("TransformAlternatives:" + source.Entry, inputs, error)); continue; }
            if (result.Disposition != PredictorInputResult.Accepted)
            { pending.Add("TransformationInput:" + result.Disposition); continue; }
            var transformations = result.Effects.SelectMany(e => e.Transformations).ToArray();
            var orderedTargets = inputs.LastOrDefault() switch
            {
                ChooseObtainDeckCards choice => choice.CardIds,
                SelectTransformationCard choice => ImmutableArray.Create(choice.CardId),
                _ => ImmutableArray<long>.Empty
            };
            if (!orderedTargets.IsEmpty)
                transformations = orderedTargets.Select(id => transformations.Single(change => change.OriginalId == id)).ToArray();
            for (int ordinal = 0; ordinal < transformations.Length; ordinal++)
            {
                var change = transformations[ordinal];
                var card = result.RandomUpgrades.SelectMany(u => u.Before).FirstOrDefault(c => c.Id == change.ReplacementId)
                    ?? result.State.Card(change.ReplacementId);
                if (!outcomes.TryGetValue(ordinal, out var cards)) outcomes.Add(ordinal, cards = []);
                // Tuple traversal order is not source order for later slots.
                // Equivalent native copies may share one numerical query; their
                // provenance still includes the earliest interchangeable copy.
                int rank = equivalentTarget == null ? initial.State.Deck.IndexOf(change.OriginalId) :
                    initial.State.Deck.FindIndex(id => equivalentTarget(id) == equivalentTarget(change.OriginalId));
                cards.Add((card, rank));
            }
            // W/WP effects remain attributed to their own source, never folded
            // into the transform's candidate group.
            var observations = Observe(snapshot.Context, snapshot.State, before, result, source, source, [])
                .Where(o => o.Source != source || o.Row?.Action != OperationPreviewAction.TransformCard).ToImmutableArray();
            foreach (var group in observations.GroupBy(ObservationSignature))
            {
                if (extras.TryGetValue(group.Key, out var existing)) extras[group.Key] = (existing.Value, existing.Paths + 1);
                else extras.Add(group.Key, (group.First(), 1));
            }
        }
        var extraObservations = extras.Values.Select(item => item.Value with
        {
            Row = item.Value.Row is { } row ? row with { Possible = item.Paths != examined } : null,
            Group = item.Value.Group is { } group ? group with { Possible = item.Paths != examined } : null
        }).ToImmutableArray();
        var sections = Aggregate([], [new(extraObservations, [])], []).Sections.ToBuilder();
        var groups = outcomes.OrderBy(p => p.Key).Select(p => new OperationPreviewCardGroup(-101 - p.Key,
            p.Value.OrderBy(item => item.Rank).DistinctBy(item => item.Card with { Id = 0 })
                .Select(item => item.Card).ToImmutableArray(), 0, 1, false, true)
            { Label = source.Entry == "ASTROLABE" ? (p.Key + 1).ToString() : null,
                EarliestFirst = true, SuppressPossiblePrefix = true }).ToImmutableArray();
        if (!groups.IsEmpty) sections.Insert(0, new(source, [], groups)
            { Category = RolltheSpire2.Ui.Settings.OperationPredictionCategory.CardTransform });
        return new(source, [], [], false,
            [new([], sections.ToImmutable(), pending.Distinct().ToImmutableArray(), examined,
                faults.Count == 0 && pending.Count == 0) { Faulted = faults.Count > 0 }], [])
            { Faults = faults.ToImmutable() };
    }

    private static IEnumerable<ImmutableArray<long>> OrderedTargets(ImmutableArray<long> candidates, int count,
        ImmutableArray<long> selected, CancellationToken token, Func<long, PredictorCard>? equivalentTarget = null)
    {
        token.ThrowIfCancellationRequested();
        if (selected.Length > count) yield break;
        if (selected.Length == count) { yield return selected; yield break; }
        var equivalent = equivalentTarget == null ? null : new HashSet<PredictorCard>();
        foreach (long id in candidates)
            if (!selected.Contains(id) && (equivalent == null || equivalent.Add(equivalentTarget!(id))))
                foreach (var result in OrderedTargets(candidates, count, selected.Add(id), token, equivalentTarget)) yield return result;
    }

    private static string ObservationSignature(Observation observation) => observation.Source + ":" + (observation.Row is { } row
        ? row.Action + ":" + string.Join("|", row.Models) + ":" + string.Join("|", row.Cards)
        : observation.Group!.Id + ":" + observation.Group.MinimumSelect + ":" + observation.Group.MaximumSelect + ":" +
            observation.Group.CanSkip + ":" + string.Join("|", observation.Group.Cards));

    private static bool SameVisibleResults(OperationPreviewBranch left, OperationPreviewBranch right)
    {
        // Id identifies a simulated instance, not another visible card. Every
        // other card field remains in record equality, including persistent
        // costs, enchantment, level, room counters and local keyword metadata.
        static bool Card(PredictorCard a, PredictorCard b) => (a with { Id = 0 }) == (b with { Id = 0 });
        static bool Row(OperationPreviewRow a, OperationPreviewRow b) => a.Action == b.Action && a.Possible == b.Possible &&
            SameMultiset(a.Models, b.Models, (x, y) => x == y) && SameMultiset(a.Cards, b.Cards, Card);
        static bool Group(OperationPreviewCardGroup a, OperationPreviewCardGroup b) => a.MinimumSelect == b.MinimumSelect &&
            a.MaximumSelect == b.MaximumSelect && a.CanSkip == b.CanSkip && a.Possible == b.Possible &&
            SameMultiset(a.Cards, b.Cards, Card);
        static bool Section(OperationPreviewSection a, OperationPreviewSection b) => a.Source == b.Source &&
            SameMultiset(a.Rows, b.Rows, Row) && SameMultiset(a.CardGroups, b.CardGroups, Group);
        return SameMultiset(left.PendingReasons, right.PendingReasons, (a, b) => a == b) &&
            SameMultiset(left.Sections, right.Sections, Section);
    }

    private static bool SameMultiset<T>(IEnumerable<T> left, IEnumerable<T> right, Func<T, T, bool> equal)
    {
        var remaining = right.ToList();
        foreach (var item in left)
        {
            int index = remaining.FindIndex(candidate => equal(item, candidate));
            if (index < 0) return false;
            remaining.RemoveAt(index);
        }
        return remaining.Count == 0;
    }

    private static bool SameCards(ImmutableArray<PredictorCard> left, ImmutableArray<PredictorCard> right) =>
        left.Length == right.Length && left.Zip(right).All(pair =>
            (pair.First with { Id = 0 }) == (pair.Second with { Id = 0 }));

    private static ImmutableArray<Observation> Observe(PredictorContext context, PredictorState before, PredictorOperationResult? previous,
        PredictorOperationResult after, ModelKey currentSource, ModelKey rootSource, ImmutableArray<PredictorReward> bones)
    {
        var seen = ImmutableArray.CreateBuilder<Observation>();
        var addedRelics = after.State.Relics.Where(r => !before.Relics.Any(p => p.Id == r.Id)).ToArray();
        var automaticRelics = addedRelics.Where(r => r.Key != rootSource && r.Key != currentSource).Select(r => r.Key).ToImmutableArray();
        if (!automaticRelics.IsEmpty) seen.Add(new(currentSource, new(OperationPreviewAction.ObtainRelic, automaticRelics, [], false)));
        // A parent's already visible rewards are not consequences of this
        // obtain. Compare content rather than record identity/subscription
        // bookkeeping: Silken Tress can replace candidates under the same ID.
        bool NewReward(PredictorReward reward) => previous == null || !previous.Rewards.Any(r =>
            r.Id == reward.Id && r.Status == PredictorRewardStatus.Open && r.Kind == reward.Kind &&
            r.Key == reward.Key && SameCards(r.Cards, reward.Cards));
        var relicRewards = after.Rewards.Where(r => NewReward(r) && r.Status == PredictorRewardStatus.Open && r.Kind == PredictorRewardKind.Relic &&
            r.Origin != PredictorRewardOrigin.BonesInternal).Select(r => r.Key).ToImmutableArray();
        if (!relicRewards.IsEmpty) seen.Add(new(currentSource, new(OperationPreviewAction.ObtainRelic, relicRewards, [], false)));
        var potionRewards = after.Rewards.Where(r => NewReward(r) && r.Status == PredictorRewardStatus.Open &&
            r.Kind == PredictorRewardKind.Potion).Select(r => r.Key).ToImmutableArray();
        if (!potionRewards.IsEmpty) seen.Add(new(currentSource, new(OperationPreviewAction.GeneratePotion, potionRewards, [], false)));
        foreach (var reward in after.Rewards.Where(r => NewReward(r) && r.Status == PredictorRewardStatus.Open))
        {
            if (reward.Kind == PredictorRewardKind.Card && currentSource.Entry != "DUSTY_TOME")
                seen.Add(new(currentSource, Group: new(reward.Id, reward.Cards, 1, 1, reward.CanSkip, false)));
        }
        if (currentSource.Entry != "DUSTY_TOME" && after.Waiting.Phase is (PredictorPhase.ObtainChooseCard or PredictorPhase.ObtainChooseManyCards) &&
            (previous == null || previous.Waiting.Phase != after.Waiting.Phase ||
                !SameCards(previous.Waiting.Cards, after.Waiting.Cards)))
            seen.Add(new(currentSource, Group: new(-1, after.Waiting.Cards,
                0,
                after.Waiting.Phase == PredictorPhase.ObtainChooseCard ? 1 : after.Waiting.Cards.Length, true, false)));
        for (int i = 0; i < after.Waiting.Bundles.Length; i++)
            if (previous == null || previous.Waiting.Phase != after.Waiting.Phase || i >= previous.Waiting.Bundles.Length ||
                !SameCards(previous.Waiting.Bundles[i], after.Waiting.Bundles[i]))
                seen.Add(new(currentSource, Group: new(-2 - i, after.Waiting.Bundles[i], after.Waiting.Bundles[i].Length,
                    after.Waiting.Bundles[i].Length, false, false)));
        // Deck targets are inputs already visible in native selection. Choices
        // may enumerate them below, but only their hidden random consequences
        // belong in this projection. Dusty Tome's prepared card is also public.
        foreach (var upgrade in after.RandomUpgrades.Skip(previous?.RandomUpgrades.Length ?? 0))
            seen.Add(new(upgrade.Source, new(OperationPreviewAction.UpgradeCard, [], upgrade.After, false)));
        var effects = after.Effects.Skip(previous?.Effects.Length ?? 0).ToArray();
        var transformed = effects.SelectMany(e => e.Transformations)
            .Where(t => after.State.Cards.Any(c => c.Id == t.ReplacementId)).Select(t => after.State.Card(t.ReplacementId)).ToImmutableArray();
        foreach (var effect in effects)
        {
            ModelKey? transformSource = effect.Action switch
            {
                "PandorasBoxTransform" => new("RELIC", "PANDORAS_BOX"),
                "NewLeafTransform" => new("RELIC", "NEW_LEAF"),
                "AstrolabeTransform" => new("RELIC", "ASTROLABE"),
                "LeafyPoulticeTransform" => new("RELIC", "LEAFY_POULTICE"),
                _ => null
            };
            if (transformSource is { } transform)
            {
                var cards = effect.Transformations.Select(t => after.State.Card(t.ReplacementId))
                    .Select(c => after.RandomUpgrades.Skip(previous?.RandomUpgrades.Length ?? 0)
                        .SelectMany(u => u.Before).FirstOrDefault(p => p.Id == c.Id) ?? c).ToImmutableArray();
                if (!cards.IsEmpty) seen.Add(new(transform, new(OperationPreviewAction.TransformCard, [], cards, false)));
            }
            ModelKey? upgradeSource = effect.Action switch
            {
                "SandCastleUpgrade" => new("RELIC", "SAND_CASTLE"),
                "FragrantMushroomUpgrade" => new("RELIC", "FRAGRANT_MUSHROOM"),
                _ => null
            };
            if (upgradeSource is { } upgrade && !effect.Targets.IsEmpty)
                seen.Add(new(upgrade, new(OperationPreviewAction.UpgradeCard, [], effect.Targets.Select(after.State.Card).ToImmutableArray(), false)));
        }
        if (addedRelics.Any(r => r.Key.Entry == "BEAUTIFUL_BRACELET"))
        {
            var enchanted = after.State.Cards.Where(c => c.Enchantment?.Entry == "SWIFT" && c.EnchantmentAmount == 2 &&
                (!before.Cards.Any(p => p.Id == c.Id) || before.Card(c.Id).Enchantment != c.Enchantment ||
                 before.Card(c.Id).EnchantmentAmount != c.EnchantmentAmount)).ToImmutableArray();
            if (!enchanted.IsEmpty) seen.Add(new(new("RELIC", "BEAUTIFUL_BRACELET"), new(OperationPreviewAction.EnchantCard, [], enchanted, false)));
        }
        var addedCards = after.State.Deck.Where(id => !before.Deck.Contains(id)).Select(after.State.Card).ToArray();
        var capeIds = effects.Where(e => e.Action == "DistinguishedCapeCards").SelectMany(e => e.Targets).ToHashSet();
        var capeCurses = addedCards.Where(c => capeIds.Contains(c.Id) && c.Type == EffectCardType.Curse).ToImmutableArray();
        if (!capeCurses.IsEmpty) seen.Add(new(new("RELIC", "DISTINGUISHED_CAPE"), new(OperationPreviewAction.AddCard, [], capeCurses, false)));
        if (currentSource.Entry == "ARCANE_SCROLL" || addedRelics.Any(r => r.Key.Entry == "ARCANE_SCROLL"))
        {
            // Large Capsule's two fixed basic cards are public native text;
            // Arcane Scroll's generated rare card is the hidden outcome.
            var arcane = addedCards.Where(c => !capeIds.Contains(c.Id) && !transformed.Any(t => t.Id == c.Id) &&
                c.Type != EffectCardType.Curse && c.Key != context.Catalog?.CharacterStrikeKey &&
                c.Key != context.Catalog?.CharacterDefendKey)
                .Select(c => after.RandomUpgrades.Skip(previous?.RandomUpgrades.Length ?? 0)
                    .SelectMany(u => u.Before).FirstOrDefault(p => p.Id == c.Id) ?? c).ToImmutableArray();
            if (!arcane.IsEmpty) seen.Add(new(new("RELIC", "ARCANE_SCROLL"), new(OperationPreviewAction.AddCard, [], arcane, false)));
        }
        // CompleteNode archives/clears CompletedChildRewards. The final curse
        // is the last automatic deck addition before the Bones obtain returns.
        bool BonesCompleted(PredictorOperationResult? result) => result != null && bones.Length == 2 &&
            (result.Waiting.Phase == PredictorPhase.Complete || BonesClaimsSettled(result, bones));
        if (BonesCompleted(after) && !BonesCompleted(previous))
        {
            var finalCurse = addedCards.TakeLast(1).Where(c => c.Type == EffectCardType.Curse).ToImmutableArray();
            if (!finalCurse.IsEmpty) seen.Add(new(rootSource, new(OperationPreviewAction.AddCard, [], finalCurse, false)));
        }
        var potions = effects.SelectMany(e => e.PotionAttempts.Select(a => a.Potion).Concat(e.GeneratedPotions))
            .Concat(after.State.PotionSlots.Where(p => p != null && !before.PotionSlots.Any(q => q?.Id == p.Id)).Select(p => p!))
            .DistinctBy(p => p.Id).Select(p => p.Key).ToImmutableArray();
        if (!potions.IsEmpty && currentSource.Entry != "NEOWS_SACRIFICE")
        {
            int obtained = after.State.PotionSlots.Count(p => p != null && !before.PotionSlots.Any(q => q?.Id == p.Id));
            seen.Add(new(currentSource, new OperationPreviewRow(OperationPreviewAction.GeneratePotion, potions, [], false)
            { Label = obtained == potions.Length ? null : $"GeneratedObtained:{potions.Length}:{obtained}" }));
        }
        return seen.ToImmutable();
    }

    private static bool BonesClaimsSettled(PredictorOperationResult result, ImmutableArray<PredictorReward> bones) =>
        !result.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && bones.Any(b => b.Id == r.Id)) &&
        bones.All(b => result.CompletedChildRewards.Any(r => r.Id == b.Id && r.Status == PredictorRewardStatus.Taken));

    private static IEnumerable<Choice> Choices(PredictorOperationResult result, ModelKey source, ImmutableArray<PredictorReward> order)
    {
        var waiting = result.Waiting;
        if (waiting.Phase == PredictorPhase.TransformCards)
        { foreach (long id in waiting.Selection.CandidateCardIds) yield return new([new SelectTransformationCard(id)], source); yield break; }
        if (waiting.Phase == PredictorPhase.ObtainDeckCards)
        {
            foreach (var ids in Combinations(waiting.Selection.CandidateCardIds, waiting.Selection.MinimumCardCount, waiting.Selection.MaximumCardCount))
                yield return new([new ChooseObtainDeckCards(ids)], source);
            yield break;
        }
        if (waiting.Phase == PredictorPhase.ObtainChooseCard)
        {
            foreach (var card in waiting.Cards) yield return new([new ChooseObtainCard(card.Id)], source);
            yield return new([new ChooseObtainCard(null)], source); yield break;
        }
        if (waiting.Phase == PredictorPhase.ObtainChooseManyCards)
        {
            foreach (var ids in Combinations(waiting.Cards.Select(c => c.Id).ToImmutableArray(), 0, waiting.Cards.Length))
                yield return new([new ChooseObtainManyCards(ids)], source);
            yield break;
        }
        if (waiting.Phase == PredictorPhase.ChooseBundle)
        { for (int i = 0; i < waiting.Bundles.Length; i++) yield return new([new ChooseScrollBoxBundle(i)], source); yield break; }
        if (waiting.Phase != PredictorPhase.Rewards) yield break;
        var open = result.Rewards.Where(r => r.Status == PredictorRewardStatus.Open).ToArray();
        var ordered = order.FirstOrDefault(r => open.Any(o => o.Id == r.Id));
        if (ordered != null)
        { yield return new([new TakeReward(ordered.Id)], ordered.Key); yield break; }
        foreach (var reward in open)
        {
            if (reward.Kind == PredictorRewardKind.Card)
            {
                foreach (var card in reward.Cards)
                    yield return new([new EnterCardSelection(reward.Id), new TakeReward(reward.Id, card.Id)], source);
            }
            else if (reward.Kind == PredictorRewardKind.Relic)
                yield return new([new TakeReward(reward.Id)], reward.Key);
            else if (reward.Kind is PredictorRewardKind.Gold or PredictorRewardKind.SpecialCard ||
                reward.Kind == PredictorRewardKind.Potion && result.State.PotionSlots.Any(p => p == null))
                yield return new([new TakeReward(reward.Id)], source);
        }
        if (!open.Any(r => r.Mandatory)) yield return new([new LeaveRewards()], source);
    }

    private static IEnumerable<ImmutableArray<long>> Combinations(ImmutableArray<long> candidates, int minimum, int maximum)
    {
        // Generation is bounded as well as evaluation: a large Sea Glass set
        // cannot allocate its power set before the caller enforces its budget.
        int yielded = 0;
        for (int count = minimum; count <= maximum; count++)
        foreach (var ids in Pick(0, count, []))
        { yield return ids; if (++yielded > MaximumEvaluations) yield break; }
        IEnumerable<ImmutableArray<long>> Pick(int start, int remaining, ImmutableArray<long> selected)
        {
            if (remaining == 0) { yield return selected; yield break; }
            for (int i = start; i <= candidates.Length - remaining; i++)
                foreach (var ids in Pick(i + 1, remaining - 1, selected.Add(candidates[i]))) yield return ids;
        }
    }

    private static OperationPreviewBranch Aggregate(ImmutableArray<ModelKey> order, List<Path> paths, ImmutableArray<string> pending)
    {
        // The final Bones draw is after every child choice. Different paths
        // can allocate different instance IDs before drawing the same visible
        // curse; those IDs do not make its identity conditional. Incomplete
        // alternatives before that draw still cannot prove it certain.
        bool BonesCurse(Observation o) => o.Source.Entry == "NEOWS_BONES" &&
            o.Row is { Action: OperationPreviewAction.AddCard } row && row.Cards.All(c => c.Type == EffectCardType.Curse);
        string Signature(Observation o) => o.Source + ":" + (o.Row is { } row
            ? row.Action + ":" + string.Join("|", row.Models) + ":" +
                string.Join("|", BonesCurse(o) ? row.Cards.Select(c => c with { Id = 0 }) : row.Cards)
            : o.Group!.Id + ":" + o.Group.MinimumSelect + ":" + o.Group.MaximumSelect + ":" + o.Group.CanSkip + ":" + string.Join("|", o.Group.Cards));
        var observations = paths.SelectMany((p, index) => p.Observations.Select(o => (Observation: o, Path: index)))
            .GroupBy(item => Signature(item.Observation)).Select(group =>
            {
                var item = group.First().Observation;
                bool possible = item.Row?.Possible == true || item.Group?.Possible == true ||
                    group.Select(g => g.Path).Distinct().Count() != paths.Count ||
                    BonesCurse(item) && paths.Any(p => p.Faulted || !p.Pending.IsEmpty);
                return item with { Row = item.Row is { } row ? row with { Possible = possible } : null,
                    Group = item.Group is { } cards ? cards with { Possible = possible } : null };
            }).ToArray();
        var sections = observations.GroupBy(o => o.Source).Select(group => new OperationPreviewSection(group.Key,
            group.Where(o => o.Row != null).Select(o => o.Row!).GroupBy(r => r.Action).Select(rows =>
                rows.Count() == 1 ? rows.Single() : new OperationPreviewRow(rows.Key,
                    rows.SelectMany(r => r.Models).Distinct().ToImmutableArray(),
                    rows.SelectMany(r => r.Cards).Distinct().ToImmutableArray(), rows.Any(r => r.Possible))).ToImmutableArray(),
            group.Where(o => o.Group != null).Select(o => o.Group!).ToImmutableArray())).ToImmutableArray();
        bool faulted = paths.Any(p => p.Faulted);
        return new(order, sections, pending, paths.Count, pending.IsEmpty && !faulted) { Faulted = faulted };
    }
}
