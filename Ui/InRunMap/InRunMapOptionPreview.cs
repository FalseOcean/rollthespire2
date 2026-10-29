using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.InRunOperations;

namespace RolltheSpire2.Ui.InRunMap;

// The route owns these observations. This adapter only gives the existing
// operation display its presentation data; it never captures the native room,
// runs an operation against an invented state or authors a route choice.
internal static class InRunMapOptionPreview
{
    // Acquisitions and reward receipts are two observations of the same obtain.
    // Preserve multiplicity while giving each taken receipt one display owner.
    internal static PredictorEventOptionResultKnowledge WithoutTakenRewardAcquisitions(
        PredictorEventOptionResultKnowledge result, ImmutableArray<PredictorCombatRewardKnowledge> rewards)
    {
        var takenRelics = rewards.Where(observation => observation.IsKnown &&
                observation.Value is { Kind: PredictorRewardKind.Relic, Status: PredictorRewardStatus.Taken })
            .Select(observation => observation.Value!.Key).GroupBy(key => key)
            .ToDictionary(group => group.Key, group => group.Count());
        var takenCards = rewards.Where(observation => observation.IsKnown &&
                observation.Value is { Kind: PredictorRewardKind.Card or PredictorRewardKind.SpecialCard, Status: PredictorRewardStatus.Taken })
            .Select(observation =>
            {
                var reward = observation.Value!;
                return observation.AppliedChoice?.CardIndex is { } index && index >= 0 && index < reward.Cards.Length
                    ? reward.Cards[index] : reward.Cards.Length == 1 ? reward.Cards[0] : null;
            })
            .OfType<PredictorCard>().Select(CardIdentity).GroupBy(card => card)
            .ToDictionary(group => group.Key, group => group.Count());
        return result with
        {
            ObtainedRelics = result.ObtainedRelics.Where(relic => Keep(takenRelics, relic.Key)).ToImmutableArray(),
            GeneratedCards = result.GeneratedCards.Where(card => !card.IsKnown || Keep(takenCards, CardIdentity(card.Value))).ToImmutableArray()
        };

        static PredictorCard CardIdentity(PredictorCard card) => card with { Id = 0, FloorAdded = 0 };
        static bool Keep<T>(Dictionary<T, int> counts, T identity) where T : notnull
        {
            if (counts.GetValueOrDefault(identity) == 0) return true;
            counts[identity]--;
            return false;
        }
    }

    // Only the replayed reward receipt owns the result. The adapter never
    // compares final inventories to guess an ordered obtain effect history.
    internal static OperationPreview BuildReward(ModelKey source, string operation,
        PredictorCombatRewardKnowledge? result, ImmutableArray<PredictorCombatRewardKnowledge> rewards,
        ImmutableArray<string> notices, ImmutableArray<string> pending, bool cancelledAlternative = false)
    {
        var sections = ImmutableArray.CreateBuilder<OperationPreviewSection>();
        var offer = result?.OfferBinding?.Value ?? (result?.IsKnown == true ? result.Value : null);
        if (cancelledAlternative && result is { DefaultSkipped: true, AppliedChoice: null } && offer is { } original)
            sections.Add(new(source, [], [new(0, original.Cards, 1, 1, original.CanSkip, false)]));
        else if (result is { AppliedChoice: { } choice } && offer is { } reward)
        {
            if (choice.SacrificeRelicId != null)
            {
                if (result.SacrificeRelic is { } relic)
                    sections.Add(new(source, [new(OperationPreviewAction.ObtainRelic, [relic], [], false)], []));
                if (result.RequiredChoices is { } child) AddChild(child);
                // Completed optional children retain their own edit receipt,
                // even when the parent obtain has already returned.
                foreach (var completed in rewards.Where(candidate => candidate.CompletedEdit?.RewardParents.Any(parent =>
                    parent.Choice.SacrificeRelicId == choice.SacrificeRelicId &&
                    parent.Choice.Occurrence == choice.Occurrence &&
                    parent.Choice.ExpectedReward.Kind == choice.ExpectedReward.Kind &&
                    parent.Choice.ExpectedReward.Origin == choice.ExpectedReward.Origin &&
                    parent.Choice.ExpectedReward.Key == choice.ExpectedReward.Key) == true))
                    AddReward(completed, completed.CompletedEdit!.Source);
            }
            else if (choice.CardIndex is { } index && !choice.Skip &&
                reward.Status == PredictorRewardStatus.Taken && index >= 0 && index < reward.Cards.Length)
                sections.Add(new(source, [new(OperationPreviewAction.AddCard, [], [reward.Cards[index]], false)], []));
            else if (!choice.Skip && choice.Reroll)
                sections.Add(new(source, [], [new(0, reward.Cards, 1, 1, reward.CanSkip, false)]));
        }
        var style = new OperationDisplayStyle(OperationTextMode.GroupRows, OperationCardLayout.Parallel);
        // Map callers already translate and scope their causes. Sending them
        // again as raw reason codes would add a second generic input warning.
        return new(source, [], [], false,
            [new([], sections.ToImmutable(), [], 1, pending.IsEmpty && result?.SacrificeRelic == null)], [])
        { OptionKey = operation, Notices = notices, NoticesAreLocalized = true, Style = style };

        void AddReward(PredictorCombatRewardKnowledge observation, ModelKey owner)
        {
            var rows = ImmutableArray.CreateBuilder<OperationPreviewRow>();
            var groups = ImmutableArray.CreateBuilder<OperationPreviewCardGroup>();
            AddRewards([observation], rows, groups);
            if (rows.Count != 0 || groups.Count != 0) sections.Add(new(owner, rows.ToImmutable(), groups.ToImmutable()));
            if (observation.RequiredChoices is { } child) AddChild(child);
        }
        void AddChild(PredictorObtainChoiceKnowledge child)
        {
            var groups = ImmutableArray.CreateBuilder<OperationPreviewCardGroup>();
            if (child.PendingCauses.IsEmpty)
            {
                if (!child.Cards.IsEmpty)
                    groups.Add(new(-1, child.Cards, child.MinimumCards, child.MaximumCards, child.MinimumCards == 0, false));
                for (int index = 0; index < child.Bundles.Length; index++)
                    groups.Add(new(-2 - index, child.Bundles[index], 1, 1, false, false));
            }
            if (groups.Count != 0) sections.Add(new(child.Source, [], groups.ToImmutable()));
            foreach (var reward in child.Rewards) AddReward(reward, child.Source);
        }
    }

    internal static OperationPreview Build(ModelKey source, string option,
        PredictorEventOptionResultKnowledge result, ImmutableArray<string> notices,
        Func<PredictorEventOptionKnowledge, string>? optionLabel = null)
    {
        result = WithoutTakenRewardAcquisitions(result, result.Rewards);
        var sections = ImmutableArray.CreateBuilder<OperationPreviewSection>();
        var rows = ImmutableArray.CreateBuilder<OperationPreviewRow>();
        var deckRows = ImmutableArray.CreateBuilder<OperationPreviewRow>();
        bool defaultDeckGroups = !HasDedicatedDeckLayout(source);
        var generated = result.GeneratedCards.Where(card => card.IsKnown).Select(card => card.Value)
            .Where(card => !result.ChangedCards.Any(change => change.Action == "Transform" && change.After == card)).ToImmutableArray();
        if (!generated.IsEmpty)
        {
            if (defaultDeckGroups)
                foreach (var cards in generated.Chunk(10)) deckRows.Add(new(OperationPreviewAction.AddCard, [], cards.ToImmutableArray(), false));
            else rows.Add(new(OperationPreviewAction.AddCard, [], generated, false));
        }
        var relics = result.ObtainedRelics.Select(relic => relic.Key).ToImmutableArray();
        if (!relics.IsEmpty) rows.Add(new(OperationPreviewAction.ObtainRelic, relics, [], false));
        foreach (var change in result.ChangedCards)
        {
            var action = change.Action switch
            {
                "Transform" => OperationPreviewAction.TransformCard,
                "Remove" => OperationPreviewAction.RemoveCard,
                "Upgrade" => OperationPreviewAction.UpgradeCard,
                "Downgrade" => OperationPreviewAction.DowngradeCard,
                "Enchant" => OperationPreviewAction.EnchantCard,
                _ => (OperationPreviewAction?)null
            };
            if (action is { } display)
            {
                var card = change.After ?? change.Before;
                if (!defaultDeckGroups) rows.Add(new(display, [], [card], false));
                else if (deckRows.Count != 0 && deckRows[^1] is { } previous && previous.Action == display && previous.Cards.Length < 10)
                    deckRows[^1] = previous with { Cards = previous.Cards.Add(card) };
                else deckRows.Add(new(display, [], [card], false));
            }
        }
        // Only unspecified deck changes receive the new default. Actual offers,
        // reward opportunities and child groups retain their IDs and layouts.
        if (deckRows.Count != 0)
            sections.Add(new(source, deckRows.ToImmutable(), [])
            { Style = new(OperationTextMode.GroupRows, OperationCardLayout.Groups) });
        var groups = ImmutableArray.CreateBuilder<OperationPreviewCardGroup>();
        if (!result.OfferedCards.IsEmpty)
            groups.Add(new(-1, result.OfferedCards, result.CardsRequired, result.CardsRequired, false, false));
        AddRewards(result.Rewards, rows, groups);
        if (rows.Count > 0 || groups.Count > 0) sections.Add(new(source, rows.ToImmutable(), groups.ToImmutable()));
        foreach (var child in result.RequiredChoices) AddChild(child);
        var branches = ImmutableArray.CreateBuilder<OperationPreviewBranch>();
        foreach (var child in result.Options.Where(child => child.Enabled != false && child.ResultPreview != null))
        {
            var nested = Build(source, child.Key, child.ResultPreview!, [], optionLabel);
            branches.AddRange(nested.Branches.Select(branch => branch with
            { Sections = sections.ToImmutable().AddRange(branch.Sections),
                Label = string.Join(" · ", new[] { optionLabel?.Invoke(child) ?? child.Key, branch.Label }.Where(value => !string.IsNullOrWhiteSpace(value))) }));
        }
        if (branches.Count == 0) branches.Add(new([], sections.ToImmutable(), [], 1, result.Advanced));
        return new(source, [], [], false, branches.ToImmutable(), [])
        { OptionKey = option, Notices = notices, NoticesAreLocalized = true };

        void AddChild(PredictorObtainChoiceKnowledge child)
        {
            var childRows = ImmutableArray.CreateBuilder<OperationPreviewRow>();
            var childGroups = ImmutableArray.CreateBuilder<OperationPreviewCardGroup>();
            if (child.PendingCauses.IsEmpty && !child.Cards.IsEmpty)
                childGroups.Add(new(-2, child.Cards, child.MinimumCards, child.MaximumCards, child.MinimumCards == 0, false));
            if (child.PendingCauses.IsEmpty)
                for (int index = 0; index < child.Bundles.Length; index++)
                    childGroups.Add(new(-3 - index, child.Bundles[index], 1, 1, false, false) { Label = (index + 1).ToString() });
            AddRewards(child.Rewards, childRows, childGroups);
            if (childRows.Count > 0 || childGroups.Count > 0)
                sections.Add(new(child.Source, childRows.ToImmutable(), childGroups.ToImmutable()));
            foreach (var nested in child.Rewards.Select(reward => reward.RequiredChoices).OfType<PredictorObtainChoiceKnowledge>()) AddChild(nested);
        }
    }

    // Dedicated source/branch policies remain owned by the shared display policy.
    private static bool HasDedicatedDeckLayout(ModelKey source) => OperationDisplayPolicy.HasAuthoredSourceStyle(source);

    private static void AddRewards(ImmutableArray<PredictorCombatRewardKnowledge> rewards,
        ImmutableArray<OperationPreviewRow>.Builder rows, ImmutableArray<OperationPreviewCardGroup>.Builder groups)
    {
        foreach (var observation in rewards)
        {
            if (!observation.IsKnown && !observation.CardOptions.IsEmpty)
            {
                // These marginal cards mask uncertain enchantments and carry
                // no executable reward snapshot. The event hover names the
                // missing enchantment facts while preserving known cards.
                groups.Add(new(0, observation.CardOptions.Select(card => card.Value).ToImmutableArray(), 1, 1, false, false));
                continue;
            }
            if (!observation.IsKnown) continue;
            if (observation.Value is not { } reward) continue;
            if (reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard && !reward.Cards.IsEmpty)
                groups.Add(new(reward.Id, reward.Cards, 1, 1, reward.CanSkip, false));
            else if (reward.Kind is PredictorRewardKind.Relic or PredictorRewardKind.Potion)
                rows.Add(new(reward.Kind == PredictorRewardKind.Relic ? OperationPreviewAction.ObtainRelic :
                    OperationPreviewAction.GeneratePotion, [reward.Key], [], false));
        }
    }
}
