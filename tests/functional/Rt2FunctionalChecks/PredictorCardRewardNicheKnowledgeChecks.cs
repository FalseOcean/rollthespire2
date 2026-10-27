using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorCardRewardNicheKnowledgeChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    internal static void Run(PredictorRun captured)
    {
        var context = captured.Context;
        var eligible = captured.Working.Deck.Select(captured.Working.Card)
            .Where(card => PredictorObtainSources.CanEnchant(context, card)).DistinctBy(card => card.Key).Take(2).ToArray();
        Check(eligible.Length == 2, "Wing Charm fixture requires two actual eligible source cards");
        var enchanted = eligible[0] with { Enchantment = new("ENCHANTMENT", "GLAM"), EnchantmentAmount = 1 };
        var pending = PredictorRouteCauseEvidence.Bind(new("EnemyCreationCount", "Actual Fabricator count unavailable",
            PredictorCombatDomain.NicheRng, PredictorEnemyCreationSource.FabricatorBots), "niche-source");

        PredictorState Entry(params string[] relics)
        {
            var state = captured.Working with { Relics = [] };
            foreach (string relic in relics) state = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", relic));
            return state;
        }
        (PredictorState State, PredictorReward Reward) Populate(PredictorState state, params PredictorCard[] cards)
        {
            var modified = PredictorRewardGeneration.ModifyManualCardRewardOptions(context, ref state, cards, true, out var scope);
            return (state, new(1, PredictorRewardKind.Card, PredictorRewardOrigin.Event, default, modified)
                { ManuallySetCards = true, CardRewardNicheScope = scope });
        }

        foreach (int count in new[] { 0, 1, 2 })
        {
            var input = eligible.Take(count).Append(enchanted).ToArray();
            var entry = Entry("WING_CHARM");
            var generated = Populate(entry, input);
            var observation = PredictorRun.CardRewardKnowledge(generated.Reward, [], [pending]);
            int beforeCalls = entry.Streams.Single(stream => stream.Stream == PredictorStream.Niche).Calls;
            Check(generated.State.Streams.Single(stream => stream.Stream == PredictorStream.Niche).Calls - beforeCalls ==
                    (count == 0 ? 0 : 1),
                "Native NextItem consumes no Niche with zero targets and exactly one even with one target");
            Check(observation.IsKnown == (count <= 1) && observation.CardOptions.Length == input.Length &&
                observation.CardOptions.Last() is { IsKnown: true, WingCharmEligible: false } unaffected &&
                    unaffected.Value.Enchantment == enchanted.Enchantment && unaffected.Value.EnchantmentAmount == enchanted.EnchantmentAmount,
                "Known eligibility and preexisting enchantment must remain independent of a different target's missing Niche");
            Check(observation.CardOptions.Take(count).All(option => option.WingCharmEligible == true &&
                (count <= 1 ? option.IsKnown : !option.IsKnown && option.Value.Enchantment == null &&
                    option.EnchantmentPendingCauses.Single().Identity == pending.Identity)),
                "Only multiple actual eligible targets have an unresolved enchantment; one target is determined");

            var actualTargets = new HashSet<string>();
            for (int offset = 0; offset < 16; offset++)
            {
                var shifted = entry;
                for (int draw = 0; draw < offset; draw++) shifted = shifted.Draw(PredictorStream.Niche, 0, 17).State;
                var witness = Populate(shifted, input).Reward;
                for (int index = 0; index < input.Length; index++)
                {
                    var card = witness.Cards[index] with { Id = 0 };
                    var known = observation.CardOptions[index];
                    Check(known.IsKnown ? known.Value == card :
                        known.Value == (card with { Enchantment = null, EnchantmentAmount = 0 }),
                        "Every published card field must match each concrete Niche witness; only marked enchantment fields may differ");
                }
                actualTargets.Add(string.Join(",", witness.Cards.Select((card, index) =>
                    card.Enchantment?.Entry == "SWIFT" ? index.ToString() : "-")));
            }
            Check(count < 2 || actualTargets.Count > 1, "Multi-target witnesses must demonstrate the real target uncertainty");
            if (count == 1)
                Check(observation.Value!.Cards[0].Enchantment?.Entry == "SWIFT",
                    "One eligible target's actual Swift result remains fully known despite unknown incoming cursor");
        }

        var beforeWing = Populate(Entry("GLITTER", "WING_CHARM"), eligible);
        Check(PredictorRun.CardRewardKnowledge(beforeWing.Reward, [], [pending]).IsKnown &&
            beforeWing.Reward.CardRewardNicheScope!.EligibleIndices.IsEmpty,
            "Earlier deterministic enchantment determines zero Wing Charm targets at the actual source boundary");
        var afterWing = Populate(Entry("WING_CHARM", "GLITTER"), eligible);
        Check(PredictorRun.CardRewardKnowledge(afterWing.Reward, [], [pending]).CardOptions.All(option =>
            !option.IsKnown && option.Value.Enchantment == null),
            "Later Glitter must not promote the witness's Swift-versus-Glam enchantments to known facts");
        var identityUnknown = PredictorRun.CardRewardKnowledge(afterWing.Reward,
            [pending with { Code = "RewardsCursorUnknown", AffectedDomains = PredictorCombatDomain.RewardsRng }], [pending]);
        Check(identityUnknown.CardOptions.IsEmpty && identityUnknown.Value == null,
            "Card identity uncertainty cannot acquire known options from the enchantment-only projection");
        var plans = new PredictorRewardPlan[]
        {
            new(PredictorRewardKind.Gold, 10, 20), new(PredictorRewardKind.Potion), new(PredictorRewardKind.Relic),
            new(PredictorRewardKind.Card, PrefilledCardKeys: eligible.Select(card => card.Key).ToImmutableArray())
        };
        var baseline = PredictorRewardGeneration.Generate(context, Entry("WING_CHARM"), plans);
        for (int offset = 1; offset <= 4; offset++)
        {
            var shifted = Entry("WING_CHARM");
            for (int draw = 0; draw < offset; draw++) shifted = shifted.Draw(PredictorStream.Niche, 0, 17).State;
            var alternative = PredictorRewardGeneration.Generate(context, shifted, plans);
            Check(baseline.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards) ==
                    alternative.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards) &&
                baseline.Rewards.Where(reward => reward.Kind != PredictorRewardKind.Card).All(reward =>
                    alternative.Rewards.Any(other => other.Kind == reward.Kind && other.Key == reward.Key && other.GoldAmount == reward.GoldAmount)),
                "Actual reward population keeps Gold, Potion, Relic and the Rewards cursor independent of Wing Charm's Niche target");
        }
        Console.WriteLine("RUNTIME_CARD_REWARD_NICHE_PASS native zero/one/many consumption; actual eligibility; known identities and unaffected enchantments; inventory-order hooks; 16 concrete cursor witnesses");
    }
}
