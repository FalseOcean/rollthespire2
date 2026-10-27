using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorRouteRewardAlternativeKnowledge(string Key, long RelicId,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    internal bool IsKnown => PendingCauses.IsEmpty;
}

internal sealed partial class PredictorRun
{
    private sealed record RouteRewardResult(string NodeId, PredictorReward Source,
        PredictorPlannedRewardChoice Choice, ImmutableArray<PredictorRouteRewardAlternativeKnowledge> Alternatives,
        int AppliedChildren = 0, int? SacrificeCount = null, ModelKey? SacrificeRelic = null);

    private static void ValidatePlannedRewardAlternatives(PredictorPlannedRewardChoice choice)
    {
        RequirePlan(!choice.Children.IsDefault && choice.SacrificeRelicId is not < 1);
        RequirePlan(!choice.Reroll || choice.ExpectedReward.Kind == PredictorRewardKind.Card && choice.ExpectedReward.CanReroll);
        RequirePlan(choice.ExpectedRerolledReward == null || choice.Reroll &&
            choice.ExpectedRerolledReward.Kind == PredictorRewardKind.Card &&
            choice.ExpectedRerolledReward.HasRerolled && !choice.ExpectedRerolledReward.CanReroll);
        RequirePlan(!choice.Reroll || choice.ExpectedRerolledReward != null || choice.CardIndex == null && !choice.Skip);
        RequirePlan(choice.SacrificeRelicId == null || choice.ExpectedReward.Kind == PredictorRewardKind.Card &&
            !choice.Reroll && !choice.Skip && choice.CardIndex == null);
        RequirePlan(choice.SacrificeRelicId != null ||
            choice.ExpectedReward.Kind == PredictorRewardKind.Relic && !choice.Skip || choice.Children.IsEmpty);
        if (choice.ExpectedRerolledReward is { } rerolled)
            ValidatePlannedNodes([new("rerolled-offer-validation") { RewardChoices = [new(rerolled, choice.Occurrence)] }]);
        foreach (var child in choice.Children) ValidatePlannedObtainChild(child);
    }

    private static ImmutableArray<PredictorRouteRewardAlternativeKnowledge> ObserveRouteRewardAlternatives(
        PredictorRun run, PredictorReward reward, List<RouteDependency> dependencies,
        ImmutableArray<PredictorCombatPendingCause> offerCauses)
    {
        if (reward.Kind != PredictorRewardKind.Card) return [];
        var options = ImmutableArray.CreateBuilder<PredictorRouteRewardAlternativeKnowledge>();
        if (reward.CanSkip && !reward.Mandatory) options.Add(new("SKIP", 0, offerCauses));
        if (reward.CanReroll) options.Add(new("REROLL", 0, PredictorRouteCauseEvidence.Normalize(offerCauses.AddRange(
            RerollReadCauses(run, reward, dependencies)))));
        foreach (var wing in run.Working.Relics.Where(r => !r.Melted && r.Key.Entry == "PAELS_WING"))
            options.Add(new("SACRIFICE", wing.Id, PredictorRouteCauseEvidence.Normalize(offerCauses.AddRange(
                SacrificeReadCauses(run, wing, dependencies)))));
        if (options.Count > 2) throw new InvalidOperationException("More than 2 card reward alternatives are not supported.");
        return options.ToImmutable();
    }

    private static PredictorCombatDomain RerollReadDomains(PredictorRun run, PredictorReward reward) =>
        PredictorCombatDomain.RewardsRng | PredictorCombatDomain.Relics | PredictorCombatDomain.CardRewardHookState |
        (CombatCardReadsRarityOffset(run.Context, reward) ? PredictorCombatDomain.CardRarityOdds : PredictorCombatDomain.None) |
        (PredictorSettlementEffects.Has(run.Working, "WING_CHARM") ? PredictorCombatDomain.NicheRng : PredictorCombatDomain.None);

    private static ImmutableArray<PredictorCombatPendingCause> RerollReadCauses(PredictorRun run, PredictorReward reward,
        List<RouteDependency> dependencies) => PredictorRouteCauseEvidence.Normalize(
        CausesFor(dependencies, RerollReadDomains(run, reward)).AddRange(CombatCardReadsRarityOffset(run.Context, reward)
            ? dependencies.Select(d => d.Cause).Where(cause => CombatOddsCauseApplies(cause, "odds.cardRarity")) : []));

    private static ImmutableArray<PredictorCombatPendingCause> SacrificeReadCauses(PredictorRun run,
        PredictorRelic wing, List<RouteDependency> dependencies)
    {
        var causes = CausesFor(dependencies, PredictorCombatDomain.Relics | PredictorCombatDomain.RewardAlternativeState);
        if (wing.RewardsSacrificed % 2 == 1 || !causes.IsEmpty)
        {
            causes = causes.AddRange(CausesFor(dependencies, PredictorCombatDomain.RewardsRng | PredictorCombatDomain.RelicRewards));
            if (causes.IsEmpty)
            {
                var state = run.Working;
                var key = PredictorRewardGeneration.PullRelic(run.Context, ref state);
                causes = causes.AddRange(ObtainCauses(run, key, dependencies, entryOnly: true));
            }
        }
        return PredictorRouteCauseEvidence.Normalize(causes);
    }

    private static PredictorCombatRewardKnowledge WithRouteRewardResult(PredictorRun run,
        PredictorReward reward, PredictorCombatRewardKnowledge knowledge, List<RouteDependency> dependencies)
    {
        if (!run._preview.Rewards.Results.TryGetValue(reward.Id, out var result))
            return WithRouteRewardDisposition(run, reward, knowledge with
                { Alternatives = ObserveRouteRewardAlternatives(run, reward, dependencies, knowledge.PendingCauses) });
        return WithRouteRewardDisposition(run, reward, knowledge with { Alternatives = result.Alternatives, AppliedChoice = result.Choice,
            AppliedChildren = result.AppliedChildren, SacrificeCount = result.SacrificeCount, SacrificeRelic = result.SacrificeRelic });
    }

    private static ImmutableArray<PredictorCombatRewardKnowledge> RetainRouteAlternativeSources(PredictorRun run,
        ImmutableArray<PredictorCombatRewardKnowledge> rewards, List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
    {
        if (!run._sacrificePendingRewards.IsEmpty)
        {
            var required = ObserveRouteObtainChoice(run, dependencies, knownInstanceIds);
            for (int pendingIndex = run._sacrificePendingRewards.Length - 1; pendingIndex >= 0; pendingIndex--)
            {
                long id = run._sacrificePendingRewards[pendingIndex];
                if (!run._preview.Rewards.SacrificeSourceRewards.TryGetValue(id, out var sources)) continue;
                var frame = run._obtainFrames.FirstOrDefault(f => f.Destination == PredictorObtainReturn.RewardAlternative && f.SourceId == id);
                if (frame != null) sources = frame.ParentRewards;
                var prior = run._preview.Rewards.SacrificeSourceKnowledge[id];
                rewards = sources.Select((source, index) => WithRouteRewardResult(run, source,
                    index < prior.Length ? prior[index] with { Value = prior[index].IsKnown ? DisplayReward(source) : null } :
                        new(source.Kind, DisplayReward(source), []), dependencies) with
                    { RequiredChoices = source.Id == id ? required : null }).ToImmutableArray();
                if (pendingIndex > 0 && run._preview.Rewards.Results.TryGetValue(run._sacrificePendingRewards[pendingIndex - 1], out var parent))
                    required = new(PredictorPhase.Rewards, parent.SacrificeRelic ?? required!.Source,
                        [], [], [], rewards, []);
            }
        }
        return rewards;
    }

    private static ImmutableArray<PredictorCombatRewardKnowledge> ObserveCompletedRouteRestRewards(PredictorRun run,
        ImmutableArray<PredictorReward> sourceRewards, ImmutableArray<PredictorCombatRewardKnowledge> source,
        List<RouteDependency> dependencies)
    {
        if (run.Phase != PredictorPhase.RestSite || !run.Rewards.IsEmpty) return ObserveRouteRewards(run, dependencies, source);
        // Taking or sacrificing the last Dream Catcher reward immediately drains
        // the RestHeal frame. Its actual completed children own the final offer;
        // the restored parent's empty Rewards must not erase that observation.
        return sourceRewards.Select((reward, index) =>
        {
            var completed = run.CompletedChildRewards.LastOrDefault(child => child.Id == reward.Id) ?? reward;
            bool chosen = run._preview.Rewards.Results.TryGetValue(reward.Id, out var result) &&
                (result.Choice.CardIndex != null || result.Choice.Skip || result.Choice.SacrificeRelicId != null);
            if (!chosen && !run._preview.Rewards.DefaultCardSkips.Contains(reward.Id)) completed = completed with { Status = reward.Status };
            var knowledge = index < source.Length ? source[index] : new(reward.Kind, DisplayReward(reward), []);
            var observation = knowledge with { Value = knowledge.IsKnown ? DisplayReward(completed) : null };
            return WithRouteRewardResult(run, completed, observation, dependencies);
        }).ToImmutableArray();
    }

    private static void RecordRouteRewardChoice(PredictorRun run, PredictorReward reward,
        PredictorPlannedRewardChoice choice, List<RouteDependency> dependencies)
    {
        if (run._preview.Rewards.Results.TryGetValue(reward.Id, out var prior))
            run._preview.Rewards.Results[reward.Id] = prior with { Choice = choice };
        else run._preview.Rewards.Results[reward.Id] = new(run.Working.Position.NodeId, reward, choice,
            ObserveRouteRewardAlternatives(run, reward, dependencies, []));
    }

    // Null means ordinary receipt/skip. True means this action was accepted;
    // false leaves a precise local binding or obtain-child remainder.
    private static bool? ApplyRouteRewardAlternative(PredictorRun run, PredictorReward reward,
        PredictorPlannedRewardChoice choice, List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token,
        out PredictorReward current, out PredictorPlannedRewardChoice followup)
    {
        current = reward; followup = choice;
        if (!choice.Reroll && choice.SacrificeRelicId == null) return null;
        bool Pending(string code, ImmutableArray<PredictorCombatPendingCause> parents = default)
        {
            var actual = run.Rewards.FirstOrDefault(r => r.Id == reward.Id) ?? reward;
            local.Add(RewardChoiceCause(run, actual, code, PredictorCombatDomain.Continuation, dependencies, parents));
            return false;
        }
        var options = ObserveRouteRewardAlternatives(run, reward, dependencies,
            ObserveRouteRewards(run, dependencies)[run.Rewards.IndexOf(reward)].PendingCauses);
        var alternative = options.FirstOrDefault(option => choice.Reroll ? option.Key == "REROLL" :
            option.Key == "SACRIFICE" && option.RelicId == choice.SacrificeRelicId);
        if (alternative == null) return Pending("PlannedRewardAlternativeUnavailable");
        if (!alternative.IsKnown) return Pending("PlannedRewardAlternativeDependent", alternative.PendingCauses);
        // Preserve the source eligibility before generation or obtain hooks can
        // replace it. Only an Accepted input publishes the authored choice.
        if (!reward.CardSelectionEntered &&
            run.Submit(run.Request!, new EnterCardSelection(reward.Id), token) != PredictorInputResult.Accepted)
            return Pending("PlannedRewardPending");
        PredictorInput input = choice.Reroll ? new RerollCardReward(reward.Id) :
            new SacrificeCardReward(reward.Id, choice.SacrificeRelicId!.Value);
        ModelKey? sacrificeRelic = null;
        if (choice.SacrificeRelicId is { } wingId && run.Working.Relics.Single(r => r.Id == wingId).RewardsSacrificed % 2 == 1)
        {
            var beforeObtain = run.Working;
            sacrificeRelic = PredictorRewardGeneration.PullRelic(run.Context, ref beforeObtain);
        }
        var sourceRewards = run.Rewards;
        var sourceKnowledge = ObserveRouteRewards(run, dependencies);
        var accepted = new RouteRewardResult(run.Working.Position.NodeId, reward, choice, options);
        if (run.Submit(run.Request!, input, token) != PredictorInputResult.Accepted)
            return Pending("PlannedRewardPending");
        run._preview.Rewards.Results[reward.Id] = accepted;
        if (choice.Reroll)
        {
            run._preview.Rewards.Results[reward.Id] = accepted with { Choice = choice with
                { CardIndex = null, Skip = false, ExpectedRerolledReward = null } };
            current = run.Rewards.Single(r => r.Id == reward.Id);
            if (choice.ExpectedRerolledReward == null) return true;
            if (!SameOffer(current, choice.ExpectedRerolledReward)) return Pending("PlannedRerolledRewardChanged");
            followup = choice with { ExpectedReward = choice.ExpectedRerolledReward, Reroll = false,
                ExpectedRerolledReward = null };
            return null;
        }
        if (run._sacrificePendingRewardId == reward.Id)
        { run._preview.Rewards.SacrificeSourceRewards[reward.Id] = sourceRewards; run._preview.Rewards.SacrificeSourceKnowledge[reward.Id] = sourceKnowledge; }
        var wing = run.Working.Relics.Single(r => r.Id == choice.SacrificeRelicId);
        run._preview.Rewards.Results[reward.Id] = accepted with { SacrificeCount = wing.RewardsSacrificed,
            SacrificeRelic = sacrificeRelic };
        for (int index = 0; index < choice.Children.Length; index++)
        {
            if (!ApplyRouteObtainChild(run, choice.Children[index], dependencies, knownInstanceIds, local, token)) return false;
            run._preview.Rewards.Results[reward.Id] = run._preview.Rewards.Results[reward.Id] with { AppliedChildren = index + 1 };
        }
        ContinueLiveDefaultCardRewardChildren(run, dependencies, token);
        if (run._sacrificePendingRewardId == reward.Id)
        {
            // The source is suspended inside the ordinary obtain transaction;
            // its reward list can be replaced, so use the retained source cause.
            local.Add(RouteCause("RewardChildChoice", run.Working.Position.NodeId, PredictorCombatDomain.Continuation,
                occurrenceKey: PredictorRouteCauseEvidence.Key("sacrifice-child", choice.Occurrence.ToString())));
            return false;
        }
        return true;
    }
    private sealed record PendingRouteRewardAlternatives(ImmutableArray<PredictorDeckRewardAlternative> Options,
        PredictorCombatDomain Domains, ImmutableArray<PredictorCard> PossibleCards, bool ChangesExistingDeck)
    {
        public ImmutableArray<PredictorCombatPendingCause> ReadCauses { get; init; } = [];
    }

    private static PredictorRun RewardAlternativeWitness(PredictorRun run, PredictorReward reward) =>
        new(run.Context, run.Working)
        {
            Phase = PredictorPhase.Rewards, Rewards = run.Rewards, _cardSelection = reward.Id,
            _eventRng = run._eventRng == null ? null : PredictorStreamState.Capture(PredictorStream.Niche, run._eventRng).Restore()
        };

    private static PredictorCombatDomain ChangedRewardAlternativeDomains(PredictorState before, PredictorState after)
    {
        var domains = PredictorCombatDomain.None;
        foreach (var stream in before.Streams)
            if (after.Streams.Single(s => s.Stream == stream.Stream) != stream) domains |= StreamKnowledgeDomain(stream.Stream);
        if (before.NextInstanceId != after.NextInstanceId) domains |= PredictorCombatDomain.InstanceIds;
        if (before.Odds.CardRarityOffset != after.Odds.CardRarityOffset) domains |= PredictorCombatDomain.CardRarityOdds;
        if (before.GoldBudget != after.GoldBudget) domains |= PredictorCombatDomain.Gold;
        if (!before.CurrentHealthEffects.SequenceEqual(after.CurrentHealthEffects)) domains |= PredictorCombatDomain.Health;
        if (!before.PotionSlots.SequenceEqual(after.PotionSlots)) domains |= PredictorCombatDomain.PotionInventory;
        if (before.Deck.Any(id => !after.Deck.Contains(id) || before.Card(id) != after.Card(id)) ||
            after.Deck.Any(id => !before.Deck.Contains(id))) domains |= PredictorCombatDomain.Deck;
        if (!before.PersonalBag.Equals(after.PersonalBag) || !before.SharedBag.Equals(after.SharedBag)) domains |= PredictorCombatDomain.RelicRewards;
        return domains;
    }

    private static PendingRouteRewardAlternatives PendingRewardAlternatives(PredictorRun run, PredictorReward reward,
        List<RouteDependency> dependencies)
    {
        if (reward.Kind != PredictorRewardKind.Card) return new([], PredictorCombatDomain.None, [], false);
        var options = ImmutableArray.CreateBuilder<PredictorDeckRewardAlternative>();
        var possible = ImmutableArray.CreateBuilder<PredictorCard>();
        var parents = ImmutableArray.CreateBuilder<PredictorCombatPendingCause>();
        var domains = PredictorCombatDomain.None;
        bool changesExisting = false;
        // Mirror the native max-two-alternatives rule even for unselected offers.
        _ = ObserveRouteRewardAlternatives(run, reward, dependencies, []);
        if (reward.CanReroll)
        {
            parents.AddRange(RerollReadCauses(run, reward, dependencies));
            if (RerollReadCauses(run, reward, dependencies).IsEmpty)
            {
                var witness = RewardAlternativeWitness(run, reward with { CardSelectionEntered = true });
                witness.Rewards = witness.Rewards.Select(r => r.Id == reward.Id ? r with { CardSelectionEntered = true } : r).ToImmutableArray();
                if (witness.SubmitRerollCardReward(new(reward.Id)) != PredictorInputResult.Accepted)
                    throw new InvalidOperationException("PredictorPendingRerollWitnessInvalid");
                var rerolled = witness.Rewards.Single(r => r.Id == reward.Id);
                for (int index = 0; index < rerolled.Cards.Length; index++)
                {
                    var card = rerolled.Cards[index] with { Id = 0 };
                    options.Add(new("reroll:card:" + index, card)); possible.Add(card);
                }
                if (reward.CanSkip && !reward.Mandatory) options.Add(new("reroll:skip", null));
                domains |= ChangedRewardAlternativeDomains(run.Working, witness.Working);
                if (!run.Working.Relics.SequenceEqual(witness.Working.Relics)) domains |= PredictorCombatDomain.CardRewardHookState;
            }
            else
            {
                var recipe = reward.RerollRecipe ?? throw new NotImplementedException("PredictorCardRewardRerollRecipeMissing");
                var keys = PredictorRewardGeneration.ResolveCardPool(run.Context, run.Working, ref recipe, reward.RerollIsCardReward)
                    .Select(c => c.CardKey).ToHashSet();
                possible.AddRange(run.Context.Catalog!.CardDefinitions.Where(d => keys.Contains(d.Prototype.Key)).Select(d => d.Prototype));
                options.Add(new("reroll:pending", null) { UnsupportedReason = "RerollOfferDependsOnIncomingState" });
                domains |= PredictorCombatDomain.RewardsRng | PredictorCombatDomain.InstanceIds |
                    PredictorCombatDomain.CardRewardHookState;
                if (recipe.EncounterSource || recipe.ForceRarityOddsChange) domains |= PredictorCombatDomain.CardRarityOdds;
                if (PredictorSettlementEffects.Has(run.Working, "WING_CHARM")) domains |= PredictorCombatDomain.NicheRng;
            }
        }
        foreach (var wing in run.Working.Relics.Where(r => !r.Melted && r.Key.Entry == "PAELS_WING"))
        {
            parents.AddRange(SacrificeReadCauses(run, wing, dependencies));
            domains |= PredictorCombatDomain.RewardAlternativeState;
            var sacrifice = new PredictorDeckRewardAlternative("sacrifice:" + wing.Id, null);
            bool parityKnown = CausesFor(dependencies, PredictorCombatDomain.RewardAlternativeState | PredictorCombatDomain.Relics).IsEmpty;
            if (!parityKnown || wing.RewardsSacrificed % 2 == 1)
            {
                domains |= PredictorCombatDomain.Relics | PredictorCombatDomain.RelicRewards |
                    PredictorCombatDomain.RewardsRng | PredictorCombatDomain.InstanceIds;
                if (parityKnown && SacrificeReadCauses(run, wing, dependencies).IsEmpty)
                {
                    var witness = RewardAlternativeWitness(run, reward);
                    witness.Rewards = witness.Rewards.Select(r => r.Id == reward.Id ? r with { CardSelectionEntered = true } : r).ToImmutableArray();
                    if (witness.SubmitSacrificeCardReward(new(reward.Id, wing.Id)) != PredictorInputResult.Accepted)
                        throw new InvalidOperationException("PredictorPendingSacrificeWitnessInvalid");
                    domains |= ChangedRewardAlternativeDomains(run.Working, witness.Working);
                    changesExisting |= run.Working.Deck.Any(id => !witness.Working.Deck.Contains(id) || run.Working.Card(id) != witness.Working.Card(id));
                    var gained = witness.Working.Relics.Skip(run.Working.Relics.Length).Select(r => r.Key).ToArray();
                    foreach (var key in gained)
                        domains |= witness._sacrificePendingRewardId != null ? PendingRelicObtainWriteDomains(key) :
                            PendingRelicObtainWriteDomains(key) & PredictorCombatDomain.Health;
                    changesExisting |= witness._sacrificePendingRewardId != null && gained.Any(key => PendingRelicChangesExistingDeck(key));
                    possible.AddRange(witness.Working.Deck.Where(id => !run.Working.Deck.Contains(id)).Select(witness.Working.Card));
                    if ((domains & PredictorCombatDomain.Deck) != 0)
                        sacrifice = sacrifice with { UnsupportedReason = "SacrificeRelicObtainDeckEffectPending" };
                }
                else
                {
                    // Unknown prior parity or bag/cursor admits only keys in the
                    // actual captured personal bag, plus vanilla's Circlet leaf.
                    var keys = run.Working.PersonalBag.Buckets.SelectMany(b => b.Entries)
                        .Concat(run.Working.PersonalBag.MultiplayerFallback).Append(new ModelKey("RELIC", "CIRCLET"))
                        .Where(key => PredictorRewardGeneration.RelicAllowed(run.Context, run.Working, key)).Distinct();
                    foreach (var key in keys)
                    {
                        domains |= PendingRelicObtainWriteDomains(key);
                        changesExisting |= PendingRelicChangesExistingDeck(key);
                    }
                    if ((domains & PredictorCombatDomain.Deck) != 0)
                        sacrifice = sacrifice with { UnsupportedReason = "SacrificeRelicIdentityOrObtainPending" };
                }
            }
            options.Add(sacrifice);
        }
        return new(options.ToImmutable(), domains, possible.ToImmutable(), changesExisting)
        { ReadCauses = PredictorRouteCauseEvidence.Normalize(parents) };
    }

    // Immediate and still-open AfterObtained effects. Passive inventory readers
    // use Relics separately; an odd wing counter does not invalidate membership.
    private static PredictorCombatDomain PendingRelicObtainWriteDomains(ModelKey key)
    {
        var domains = ObtainReadDomains(key) & (PredictorCombatDomain.Deck | PredictorCombatDomain.NicheRng |
            PredictorCombatDomain.RewardsRng | PredictorCombatDomain.CombatPotionRng | PredictorCombatDomain.PotionInventory);
        if (key.Entry is "CURSED_PEARL" or "DOWSING_ROD" or "NEOWS_TORMENT" or "BLOOD_SOAKED_ROSE" or "JEWELRY_BOX" or
            "STORYBOOK" or "TANXS_WHISTLE" or "PAELS_HORN" or "DUSTY_TOME" or "DISTINGUISHED_CAPE" or "CALLING_BELL")
            domains |= PredictorCombatDomain.Deck;
        if (key.Entry is "GOLDEN_PEARL" or "SILKEN_TRESS" or "OLD_COIN" or "SIGNET_RING" or "CURSED_PEARL") domains |= PredictorCombatDomain.Gold;
        if (key.Entry is "NUTRITIOUS_OYSTER" or "BIG_MUSHROOM" or "MANGO" or "PEAR" or "STRAWBERRY" or "LEES_WAFFLE" or
            "FAKE_LEES_WAFFLE" or "FAKE_MANGO" or "LOOMING_FRUIT" or "FRAGRANT_MUSHROOM" or "SERE_TALON" or
            "PRECARIOUS_SHEARS" or "LEAFY_POULTICE") domains |= PredictorCombatDomain.Health;
        if (key.Entry is "POTION_BELT" or "PHIAL_HOLSTER" or "NEOWS_SACRIFICE" or "ALCHEMICAL_COFFER" or "CAULDRON" or "LOST_COFFER")
            domains |= PredictorCombatDomain.PotionInventory;
        if (key.Entry is "WING_CHARM" or "FRESNEL_LENS" or "GLITTER" or "LAVA_LAMP" or "MOLTEN_EGG" or "TOXIC_EGG" or
            "FROZEN_EGG" or "SILVER_CRUCIBLE" or "SILKEN_TRESS") domains |= PredictorCombatDomain.CardRewards;
        if ((domains & PredictorCombatDomain.Deck) != 0) domains |= PredictorCombatDomain.InstanceIds;
        if (key.Entry == "GOLDEN_COMPASS") domains |= PredictorCombatDomain.Map;
        return domains;
    }

    private static bool PendingRelicChangesExistingDeck(ModelKey key) =>
        (ObtainReadDomains(key) & PredictorCombatDomain.Deck) != 0 && key.Entry is not ("SEA_GLASS" or "ORRERY" or "GLASS_EYE") ||
        key.Entry is "WHETSTONE" or "WAR_PAINT";

}
