using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Persistence;

internal static class EventOpportunityPersistenceChecks
{
    internal static int Run()
    {
        int checks = 0;
        var json = SearchPresetStore.CreateJsonOptions();
        void Check(bool valid, string message)
        {
            checks++;
            if (!valid) throw new InvalidOperationException("EventOpportunityPersistence: " + message);
        }
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 10, 0, 1, "0.113.1");
        var context = new SearchContext(RuntimeProfileId.Beta111, authority.Character.CharacterKey, 10, authority,
            new("0.113.1", "0.113.1", "beta", "event-opportunity-fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
        CompiledSearch Compile(params EventResultSearchCondition[] conditions) =>
            SearchCompiler.Compile(SearchQuery.Empty with { EventResultConditions = conditions }, context);
        var rarity = new EventResultSearchCondition(EventResultConditionKind.EventRelicRarityRoll, new("EVENT", "RANWID_THE_ELDER"))
        {
            RelicRarityBranch = EventRelicRarityBranch.GiveRelic,
            RelicRaritySelection = EventRelicRaritySelection.Any,
            RelicRarity = EffectRelicRarity.Rare
        };
        var crystal = new EventResultSearchCondition(EventResultConditionKind.CrystalSphereRewards, new("EVENT", "CRYSTAL_SPHERE"))
        {
            CrystalDivinations = 6, CrystalCards = CrystalSphereSeedCards.Rare | CrystalSphereSeedCards.Common,
            RelicRarity = EffectRelicRarity.Uncommon
        };
        var conditions = new[]
        {
            rarity, crystal,
            new(EventResultConditionKind.DollRoomRandomRelic, EventOpportunityCatalog.Dolls[0]),
            new(EventResultConditionKind.DollRoomTakeSomeTimeRelic, EventOpportunityCatalog.Dolls[1]),
            new(EventResultConditionKind.MuseumOfTortureOfferedRelic, EventOpportunityCatalog.MuseumRelics[1]),
            new(EventResultConditionKind.ThermaeNonupeipeDrinkRelic, EventOpportunityCatalog.ThermaeRelics[8])
        };
        foreach (var condition in conditions)
        {
            Check(condition.IsValid, "new condition admitted");
            var restored = JsonSerializer.Deserialize<EventResultSearchCondition>(JsonSerializer.Serialize(condition, json), json)!;
            Check(restored == condition, "all authored target fields round trip");
        }
        var original = Compile(rarity);
        foreach (var edited in new[]
        {
            rarity with { RelicRarity = EffectRelicRarity.Common },
            rarity with { RelicRaritySelection = EventRelicRaritySelection.All },
            rarity with { RelicRarityBranch = EventRelicRarityBranch.GiveGold, RelicRaritySelection = EventRelicRaritySelection.First }
        })
        {
            var changed = Compile(edited);
            Check(original.SemanticFingerprint != changed.SemanticFingerprint, "rarity semantic identity includes every field");
            Check(SearchContinuationIdentity.For(original) != SearchContinuationIdentity.For(changed), "rarity continuation identity separates targets");
        }
        foreach (var edited in new[]
        {
            crystal with { CrystalDivinations = 3 },
            crystal with { CrystalCards = CrystalSphereSeedCards.Rare },
            crystal with { RelicRarity = EffectRelicRarity.Common }
        })
        {
            Check(Compile(crystal).SemanticFingerprint != Compile(edited).SemanticFingerprint, "crystal semantic identity separates targets");
            Check(SearchContinuationIdentity.For(Compile(crystal)) != SearchContinuationIdentity.For(Compile(edited)), "crystal continuation identity separates targets");
        }
        var state = new EventEditorStateSnapshot([], -1, -1, -1, null, ["CRYSTAL_SPHERE"])
            { OpportunityResults = conditions };
        var stateRoundTrip = JsonSerializer.Deserialize<EventEditorStateSnapshot>(JsonSerializer.Serialize(state, json), json)!;
        Check(stateRoundTrip.OpportunityResults.SequenceEqual(conditions), "dormant editor target rows survive");
        Check(stateRoundTrip.InactiveResultEvents.SequenceEqual(state.InactiveResultEvents), "inactive state survives independently");
        string oldRow = JsonSerializer.Serialize(new EventResultSearchCondition(EventResultConditionKind.TrialCase, new("EVENT", "TRIAL"))
            { TrialCase = TrialCaseTarget.Merchant });
        Check(!oldRow.Contains("Crystal") && !oldRow.Contains("RelicRarity"), "absent new properties preserve legacy query payload");
        foreach (var tuple in new[]
        {
            (conditions[2], 1d / 3), (conditions[3], 2d / 3),
            (conditions[4], 1d / 3), (conditions[5], 1d / 9)
        })
        {
            var estimate = EventResultProbabilityEstimator.EstimateSingle(SearchSelectivityInput.From(Compile(tuple.Item1)), tuple.Item1);
            Check(estimate.Probability is { } p && Math.Abs(p - tuple.Item2) < 1e-12, "fixed-pool marginal");
        }
        var dollJoint = Compile(conditions[2], conditions[3]);
        Check(EventResultProbabilityEstimator.EstimateBlock(SearchSelectivityInput.From(dollJoint), dollJoint.NormalizedQuery.EventResultConditions, out _) is null,
            "alternative doll branches do not multiply shared stream marginals");
        Check(EventResultProbabilityEstimator.EstimateSingle(SearchSelectivityInput.From(Compile(crystal)), crystal).Probability is null,
            "crystal solver has no invented probability");
        var three = crystal with { CrystalDivinations = 3 };
        Check(EventResultProbabilityEstimator.EstimateSingle(SearchSelectivityInput.From(Compile(three)), three).Probability == 0,
            "three moves cannot uncover 4x4 relic");
        Check(!(rarity with { RelicRaritySelection = EventRelicRaritySelection.Third }).IsValid, "third result invalid for a two-draw branch");
        Check(!(crystal with { CrystalDivinations = 4 }).IsValid, "only native payment branches");
        Console.WriteLine($"EventOpportunityPersistenceChecks passed: {checks}");
        return 0;
    }
}
