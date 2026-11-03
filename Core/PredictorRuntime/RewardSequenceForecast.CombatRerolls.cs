using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;

namespace RolltheSpire2.Core.PredictorRuntime;

internal static partial class RewardSequenceForecast
{
    private sealed partial class Worker
    {
        private const int CombatRerollDisplayLimit = 8;
        private static readonly ModelKey Driftwood = new("RELIC", "DRIFTWOOD");
        private static readonly ModelKey Candy = new("RELIC", "LASTING_CANDY");
        private static readonly ModelKey WhiteStar = new("RELIC", "WHITE_STAR");

        private ModelKey? CombatRerollIcon(PredictorOperationSnapshot snapshot, PredictorCombatKind room,
            PredictorReward reward)
        {
            if (reward.Origin != PredictorRewardOrigin.Combat || !reward.RerollIsCardReward ||
                reward.RerollRecipe is not { } plan) return null;
            var ordinaryOdds = room switch { PredictorCombatKind.Elite => PredictorCardOdds.Elite,
                PredictorCombatKind.Boss => PredictorCardOdds.Boss, _ => PredictorCardOdds.Regular };
            // Native RewardsSet, TheHunt and PrayerWheel all use ForRoom.
            // Compare the authored recipe, never the visible cards or inventory
            // alone. Capture materializes the native ordered pool separately.
            bool NativePlan(PredictorCardOdds odds) => SamePlan(plan with { CardPoolOverride = default },
                new(PredictorRewardKind.Card, Odds: odds, EncounterSource: true, IsFromCombat: plan.IsFromCombat));
            if (plan.Odds == ordinaryOdds && NativePlan(ordinaryOdds))
                return CandyApplies(snapshot, plan, reward.RerollIsCardReward) ? Candy : Driftwood;
            if (room == PredictorCombatKind.Elite && !plan.IsFromCombat && NativePlan(PredictorCardOdds.Boss) &&
                snapshot.State.Relics.Any(r => !r.Melted && r.Key == WhiteStar)) return WhiteStar;
            return null;
        }

        private bool CandyApplies(PredictorOperationSnapshot snapshot, PredictorRewardPlan plan, bool isCardReward)
        {
            bool active = snapshot.State.Relics.Any(r => !r.Melted && r.Key == Candy &&
                CardRewardRelicRules.CandyExtraCardCount(
                    RuntimeProfilePolicies.LastingCandyCounter(snapshot.Context.RuntimeProfile.ProfileId),
                    r.CombatRewardsSeen, isCardReward, plan.EncounterSource, plan.IsFromCombat) > 0);
            if (!active) return false;
            var resolved = plan;
            return PredictorRewardGeneration.ResolveCardPool(snapshot.Context, snapshot.State, ref resolved, isCardReward)
                .Any(c => c.CardType == EffectCardType.Power);
        }

        private sealed record CombatRerollFamily(ModelKey Icon, ImmutableArray<PredictorReward> Rewards);
        private sealed record CombatRerollOutcome(PredictorReward Reward, List<ImmutableArray<ModelKey>> Orders);

        private OperationForecast BuildCombatRerolls(PredictorOperationSnapshot snapshot,
            PredictorOperationResult before, PredictorReward target, PredictorOperationResult firstResult,
            ModelKey source, ModelKey targetIcon, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var groups = ImmutableArray.CreateBuilder<OperationOutcomeChoices>();
            groups.Add(CardGroup(firstResult.Rewards.Single(r => r.Id == target.Id), 1) with
                { IconPaths = [new([[targetIcon]])] });
            var faults = ImmutableArray.CreateBuilder<OperationForecastFault>();
            var families = new List<CombatRerollFamily>();
            bool excluded = false;
            bool ambiguousIcons = false;
            var room = ((PredictorOperationRewardsScene)snapshot.Scene).CombatKind!.Value;
            foreach (var reward in before.Rewards.Where(r => r.Id != target.Id && CanReroll(r)))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (reward.RerollRecipe == null)
                        throw new NotImplementedException("PredictorCardRewardRerollRecipeMissing");
                    if (CombatRerollIcon(snapshot, room, reward) is not { } icon)
                    { excluded = true; continue; }
                    if (icon == targetIcon && !SameRecipe(snapshot, target, reward) ||
                        families.Any(f => f.Icon == icon && !SameRecipe(snapshot, f.Rewards[0], reward)))
                        ambiguousIcons = true;
                    int family = families.FindIndex(f => f.Icon == icon && f.Rewards[0].CanSkip == reward.CanSkip &&
                        SameRecipe(snapshot, f.Rewards[0], reward));
                    if (family < 0) families.Add(new(icon, [reward]));
                    else families[family] = families[family] with { Rewards = families[family].Rewards.Add(reward) };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    excluded = true;
                    faults.Add(new("RewardRerollRecipeComparison:" + reward.Id, [], ex));
                }
            }

            // The three vanilla icons identify recipe classes, not reward IDs.
            // A captured non-equivalent recipe sharing an icon cannot be named
            // unambiguously by this vocabulary. Retain the direct observation.
            if (ambiguousIcons)
                return new(source, [], [], false,
                    [new([], [new(source, [], groups.ToImmutable())], [], 1, false)], [])
                    { OptionKey = "REROLL", Faults = faults.ToImmutable(), Notices = ["RewardRerollCombatRecipes"] };
            bool complete = !excluded && faults.Count == 0;
            int examined = 1;
            int otherCount = families.Sum(f => f.Rewards.Length);
            for (int length = 1; length <= otherCount; length++)
            {
                var outcomes = new List<CombatRerollOutcome>();
                bool overflow = false;
                foreach (var prefix in FamilyPrefixes(families, new int[families.Count], length, token))
                {
                    ImmutableArray<PredictorInput> inputs = [new ExitCardSelection()];
                    try
                    {
                        foreach (var reward in prefix)
                            inputs = inputs.Add(new EnterCardSelection(reward.Id)).Add(new RerollCardReward(reward.Id))
                                .Add(new ExitCardSelection());
                        inputs = inputs.Add(new EnterCardSelection(target.Id)).Add(new RerollCardReward(target.Id));
                        var result = RequireAccepted(execution.Evaluate(inputs, token), "CombatRewardRerollSequence");
                        examined++;
                        var final = result.Rewards.Single(r => r.Id == target.Id);
                        var order = prefix.Select(r => families.Single(f => f.Rewards.Contains(r)).Icon)
                            .Append(targetIcon).ToImmutableArray();
                        var outcome = outcomes.FirstOrDefault(o => SameDisplayedCards(o.Reward.Cards, final.Cards));
                        if (outcome == null) outcomes.Add(new(final, [order]));
                        else if (!outcome.Orders.Any(o => o.SequenceEqual(order))) outcome.Orders.Add(order);
                        if (groups.Count + outcomes.Count > CombatRerollDisplayLimit)
                        { overflow = true; break; }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        faults.Add(new("CombatRewardRerollSequence:" + (length + 1), inputs, ex));
                        complete = false;
                    }
                }
                // The display limit admits whole depths, never an arbitrary
                // subset of same-depth results. Earlier depths remain useful.
                if (overflow) { complete = false; break; }
                foreach (var outcome in outcomes)
                    groups.Add(CardGroup(outcome.Reward, length + 1) with
                        { IconPaths = OperationIconPaths.Compress(outcome.Orders, token) });
            }
            return new(source, [], [], false,
                [new([], [new(source, [], groups.ToImmutable())], [], examined, complete)], [])
            {
                OptionKey = "REROLL", Faults = faults.ToImmutable(),
                Notices = excluded ? ["RewardRerollCombatRecipes"] : []
            };
        }

        // Equal recipes have identical draws and after-generation hooks. Choose
        // the next concrete reward from each family, avoiding permutations of
        // reward IDs. Different recipe orders always replay independently;
        // equal displayed cards never authorize merging their continuations.
        private IEnumerable<ImmutableArray<PredictorReward>> FamilyPrefixes(List<CombatRerollFamily> families,
            int[] used, int remaining, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (remaining == 0) { yield return []; yield break; }
            for (int i = 0; i < families.Count; i++)
            {
                if (used[i] >= families[i].Rewards.Length) continue;
                var reward = families[i].Rewards[used[i]++];
                foreach (var tail in FamilyPrefixes(families, used, remaining - 1, token))
                    yield return tail.Insert(0, reward);
                used[i]--;
            }
        }

        private bool SameDisplayedCards(ImmutableArray<PredictorCard> left, ImmutableArray<PredictorCard> right) =>
            left.Length == right.Length && left.Zip(right).All(pair =>
                (pair.First with { Id = 0 }) == (pair.Second with { Id = 0 }));

    }
}
