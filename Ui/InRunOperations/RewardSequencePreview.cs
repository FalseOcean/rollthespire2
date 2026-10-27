using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunOperations;

// Projection only: each continuation replays real inputs on the detached
// Operation snapshot. The hovered offer always owns the first operation.
internal static class RewardSequencePreview
{
    private static readonly OperationDisplayStyle D1 = new(OperationTextMode.None, OperationCardLayout.Parallel);
    private static readonly OperationDisplayStyle D3 = new(OperationTextMode.None, OperationCardLayout.Groups);
    private static readonly OperationDisplayStyle A1 = new(OperationTextMode.GroupRows, OperationCardLayout.Parallel);

    internal static OperationPreview BuildReroll(PredictorOperationSnapshot snapshot,
        PredictorOperationResult before, PredictorReward current, PredictorOperationResult firstResult,
        ModelKey source, CancellationToken token = default)
    {
        var first = firstResult.Rewards.Single(r => r.Id == current.Id);
        var groups = ImmutableArray.CreateBuilder<OperationPreviewCardGroup>();
        groups.Add(CardGroup(first, 1));
        var faults = ImmutableArray.CreateBuilder<OperationPreviewFault>();
        ImmutableArray<PredictorInput> inputs = [new RerollCardReward(current.Id)];
        string? label = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var available = before.Rewards.Where(CanReroll).ToImmutableArray();
            if (snapshot.Scene is PredictorOperationEventScene { Event.Entry: "COLORFUL_PHILOSOPHERS" })
            {
                label = "RewardRerollOrdinal";
                BuildPhilosophers(snapshot, current, available, groups, faults, token);
            }
            else
            {
                foreach (var missing in available.Where(r => r.RerollRecipe == null))
                    faults.Add(new("RewardRerollRecipeComparison:" + missing.Id, [],
                        new NotImplementedException("PredictorCardRewardRerollRecipeMissing")));
                var matching = available.Where(r => SameRecipe(snapshot, current, r)).ToImmutableArray();
                if (matching.Length != available.Length) label = "RewardRerollSameRecipe";
                var result = firstResult;
                foreach (var candidate in matching.Where(r => r.Id != current.Id))
                {
                    token.ThrowIfCancellationRequested();
                    // Only this captured reward set participates. Never enter
                    // another obtain's rewards after a parent has completed.
                    if (!result.Rewards.Any(r => r.Id == candidate.Id && CanReroll(r))) continue;
                    inputs = inputs.Add(new ExitCardSelection()).Add(new EnterCardSelection(candidate.Id))
                        .Add(new RerollCardReward(candidate.Id));
                    result = RequireAccepted(PredictorOperation.Preview(snapshot, inputs, token), "RewardRerollSequence");
                    groups.Add(CardGroup(result.Rewards.Single(r => r.Id == candidate.Id), groups.Count + 1));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            faults.Add(new("RewardRerollSequence", inputs, ex));
        }
        var style = groups.Count > 1 ? D3 : D1;
        // Retain the established one-off card presentation when no future
        // operation exists. Sequence labels belong to actual multi-step views.
        if (groups.Count == 1)
        {
            groups[0] = groups[0] with { Label = null };
            label = null;
        }
        var section = new OperationPreviewSection(source, [], groups.ToImmutable()) { Style = style, Label = label };
        return new(source, [], [], false, [new([], [section], [], 1, faults.Count == 0)], [])
        { Style = style, OptionKey = "REROLL", Faults = faults.ToImmutable() };
    }

    // The caller keeps the existing first sacrifice's complete projection
    // (including its upgrades and optional Orrery restock). This adds only
    // subsequent relic identities and the currently visible Wing progress.
    internal static OperationPreview BuildSacrifice(PredictorOperationSnapshot snapshot,
        PredictorOperationResult before, PredictorReward current, PredictorOperationResult firstResult,
        PredictorRelic wing, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var sections = ImmutableArray.CreateBuilder<OperationPreviewSection>();
        var faults = ImmutableArray.CreateBuilder<OperationPreviewFault>();
        ImmutableArray<PredictorInput> inputs = [new SacrificeCardReward(current.Id, wing.Id)];
        var result = firstResult;
        int step = 1;
        try
        {
            foreach (var candidate in before.Rewards.Where(r => r.Id != current.Id &&
                         r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open))
            {
                token.ThrowIfCancellationRequested();
                // Linked siblings may have retired; the last sacrifice can
                // already have returned to the shop or to an outer reward set.
                if (!result.Rewards.Any(r => r.Id == candidate.Id && r.Status == PredictorRewardStatus.Open)) continue;
                if (result.Waiting.Phase != PredictorPhase.Rewards)
                    throw new InvalidOperationException("RewardSacrificeSequencePending:" + result.Waiting.Phase);
                var previous = result;
                inputs = inputs.Add(new EnterCardSelection(candidate.Id)).Add(new SacrificeCardReward(candidate.Id, wing.Id));
                result = RequireAccepted(PredictorOperation.Preview(snapshot, inputs, token), "RewardSacrificeSequence");
                step++;
                var obtained = result.State.Relics.FirstOrDefault(r => !previous.State.Relics.Any(old => old.Id == r.Id));
                if (obtained != null)
                    sections.Add(new(wing.Key, [new(OperationPreviewAction.ObtainRelic, [obtained.Key], [], false)], [])
                    { Style = A1, Label = "RewardSacrificeStep:" + step });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            faults.Add(new("RewardSacrificeSequence", inputs, ex));
        }
        return new(wing.Key, [], [], false,
            sections.Count == 0 ? [] : [new([], sections.ToImmutable(), [], 1, faults.Count == 0)], [])
        { Style = A1, OptionKey = "SACRIFICE", Faults = faults.ToImmutable(),
            Notices = ["RewardSacrificeProgress:" + wing.RewardsSacrificed % 2] };
    }

    private static bool CanReroll(PredictorReward reward) => reward.Kind == PredictorRewardKind.Card &&
        reward.Status == PredictorRewardStatus.Open && reward.CanReroll;

    private static OperationPreviewCardGroup CardGroup(PredictorReward reward, int step) =>
        new(reward.Id, reward.Cards, 1, 1, reward.CanSkip, false)
        { Label = "RewardRerollStep:" + step, EarliestFirst = true };

    private static PredictorOperationResult RequireAccepted(PredictorOperationResult result, string stage) =>
        result.Disposition == PredictorInputResult.Accepted ? result :
            throw new InvalidOperationException(stage + ":" + result.Disposition + ":" + result.Waiting.Phase);

    private static bool SameRecipe(PredictorOperationSnapshot snapshot, PredictorReward left, PredictorReward right)
    {
        if (left.RerollIsCardReward != right.RerollIsCardReward) return false;
        if (left.RerollRecipe is not { } a || right.RerollRecipe is not { } b) return false;
        // IsFromCombat affects this recipe only through Candy's currently
        // active Power insertion. Existing option contents/count are irrelevant.
        a = EffectiveCombatFlag(snapshot, a, left.RerollIsCardReward);
        b = EffectiveCombatFlag(snapshot, b, right.RerollIsCardReward);
        return SamePlan(a, b);
    }

    private static PredictorRewardPlan EffectiveCombatFlag(PredictorOperationSnapshot snapshot,
        PredictorRewardPlan plan, bool isCardReward)
    {
        if (!plan.IsFromCombat) return plan;
        bool candy = isCardReward && plan.EncounterSource && snapshot.State.Relics.Any(r => !r.Melted &&
            r.Key.Entry == "LASTING_CANDY" && r.CombatRewardsSeen % 2 == 1);
        if (candy)
        {
            var resolved = plan;
            candy = PredictorRewardGeneration.ResolveCardPool(snapshot.Context, snapshot.State, ref resolved, isCardReward)
                .Any(c => c.CardType == EffectCardType.Power);
        }
        return candy ? plan : plan with { IsFromCombat = false };
    }

    private static bool SamePlan(PredictorRewardPlan? a, PredictorRewardPlan? b)
    {
        if (a == null || b == null) return a == b;
        // Record equality alone compares ImmutableArray backing storage. Keep
        // ordered pool identity and every other authored recipe field instead.
        return SameKeys(a.CardPoolOverride, b.CardPoolOverride) && SameKeys(a.PrefilledCardKeys, b.PrefilledCardKeys) &&
            SamePlan(a.ManualRerollRecipe, b.ManualRerollRecipe) &&
            (a with { CardPoolOverride = default, PrefilledCardKeys = default, ManualRerollRecipe = null }) ==
            (b with { CardPoolOverride = default, PrefilledCardKeys = default, ManualRerollRecipe = null });
    }

    private static bool SameKeys(ImmutableArray<ModelKey> a, ImmutableArray<ModelKey> b) =>
        a.IsDefault == b.IsDefault && (a.IsDefault || a.SequenceEqual(b));

    private static void BuildPhilosophers(PredictorOperationSnapshot snapshot, PredictorReward target,
        ImmutableArray<PredictorReward> available, ImmutableArray<OperationPreviewCardGroup>.Builder groups,
        ImmutableArray<OperationPreviewFault>.Builder faults, CancellationToken token)
    {
        var others = available.Where(r => r.Id != target.Id).ToImmutableArray();
        // Vanilla has three offers. Enumerating their finite prefixes is at
        // most five paths including the already computed first-position path.
        // In particular Lens before WingCharm may consume zero versus one
        // Niche draw in a preceding rarity group.
        for (int length = 1; length <= others.Length; length++)
        {
            var alternatives = new List<ImmutableArray<PredictorCard>>();
            bool incomplete = false;
            foreach (var prefix in Prefixes(others, length))
            {
                ImmutableArray<PredictorInput> path = [new ExitCardSelection()];
                try
                {
                    foreach (var previous in prefix)
                        path = path.Add(new EnterCardSelection(previous.Id)).Add(new RerollCardReward(previous.Id))
                            .Add(new ExitCardSelection());
                    path = path.Add(new EnterCardSelection(target.Id)).Add(new RerollCardReward(target.Id));
                    var result = RequireAccepted(PredictorOperation.Preview(snapshot, path, token), "PhilosophersRerollOrdinal");
                    alternatives.Add(result.Rewards.Single(r => r.Id == target.Id).Cards);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    faults.Add(new("PhilosophersRerollOrdinal:" + (length + 1), path, ex));
                    incomplete = true;
                }
            }
            if (alternatives.Count == 0) continue;
            // A failed required prefix cannot establish a unique projection.
            // Preserve known earlier positions and the original failure cause.
            if (incomplete) continue;
            var cards = alternatives[0];
            for (int slot = 0; slot < cards.Length; slot++)
            {
                var first = cards[slot];
                if (alternatives.Any(other => other[slot].Enchantment != first.Enchantment ||
                        other[slot].EnchantmentAmount != first.EnchantmentAmount))
                    cards = cards.SetItem(slot, first with { Enchantment = null, EnchantmentAmount = 0 });
            }
            groups.Add(new(target.Id, cards, 1, 1, target.CanSkip, false)
                { Label = "RewardRerollStep:" + (length + 1), EarliestFirst = true });
        }
    }

    private static IEnumerable<ImmutableArray<PredictorReward>> Prefixes(ImmutableArray<PredictorReward> rewards, int length)
    {
        foreach (var reward in rewards)
        {
            if (length == 1) yield return [reward];
            else
                foreach (var tail in Prefixes(rewards.Remove(reward), length - 1))
                    yield return tail.Insert(0, reward);
        }
    }
}
