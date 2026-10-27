using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private static readonly string[][] TutorialMonsterCards =
    [
        ["SETUP_STRIKE", "TREMBLE", "BLOOD_WALL"],
        ["BREAKTHROUGH", "INFLAME", "ANGER"],
        ["IRON_WAVE", "DISMANTLE", "CINDER"],
        ["STOMP", "SHRUG_IT_OFF", "ARMAMENTS"],
        ["THUNDERCLAP", "SETUP_STRIKE", "RAGE"],
        ["BATTLE_TRANCE", "TRUE_GRIT", "UPPERCUT"],
        ["BLOODLETTING", "WHIRLWIND", "TREMBLE"]
    ];

    // Null means WithRewardsFromRoom continues to its ordinary room rewards.
    // These plans preserve the manually-set CardReward constructor and its
    // distinct ForRoom reroll recipe while using the ordinary set pipeline.
    internal ImmutableArray<PredictorRewardPlan>? TryTutorialRewardPlans(
        PredictorCombatKind kind, PredictorEncounterDefinition encounter)
    {
        if (Context.NumberOfRuns != 0 || Context.RevealedEpochs.Length != 0 ||
            Context.Character.Entry != "IRONCLAD") return null;
        if (kind == PredictorCombatKind.Boss && Working.Position.Act == 3) return null;
        var plans = ImmutableArray.CreateBuilder<PredictorRewardPlan>();
        var reroll = new PredictorRewardPlan(PredictorRewardKind.Card, Odds: kind switch
        {
            PredictorCombatKind.Elite => PredictorCardOdds.Elite,
            PredictorCombatKind.Boss => PredictorCardOdds.Boss,
            _ => PredictorCardOdds.Regular
        }, EncounterSource: true);
        void AddManualCards(params string[] names) => plans.Add(new(PredictorRewardKind.Card,
            EncounterSource: true, NoCardPoolModifications: true,
            PrefilledCardKeys: names.Select(n => new ModelKey("CARD", n)).ToImmutableArray(),
            ManualRerollRecipe: reroll));

        MapPointType pointType = kind switch
        {
            PredictorCombatKind.Monster => MapPointType.Monster,
            PredictorCombatKind.Elite => MapPointType.Elite,
            _ => MapPointType.Boss
        };
        int mapCount = kind == PredictorCombatKind.Monster
            ? RoomHistory.Count(v => v.CombatKinds.Contains(PredictorCombatKind.Monster))
            : RoomHistory.Count(v => v.PointType == pointType);
        // Standalone source checks have no map-visit record. Their combat count
        // is a controlled equivalent only for repeated ordinary battles.
        if (Working.Visits.IsEmpty && (Context.Live?.PreviousVisits.IsEmpty ?? true) && kind == PredictorCombatKind.Monster)
            mapCount = Working.CombatCount;
        switch (kind)
        {
            case PredictorCombatKind.Monster when mapCount is >= 1 and <= 7:
                plans.Add(new(PredictorRewardKind.Gold, 10, 20));
                string? potion = mapCount switch
                {
                    3 => "FIRE_POTION", 5 => "STRENGTH_POTION", 7 => "ENERGY_POTION", _ => null
                };
                if (potion != null) plans.Add(new(PredictorRewardKind.Potion,
                    PrefilledPotionKey: new("POTION", potion)));
                AddManualCards(TutorialMonsterCards[mapCount - 1]);
                return plans.ToImmutable();
            case PredictorCombatKind.Elite when mapCount is 1 or 2:
                plans.Add(new(PredictorRewardKind.Gold, encounter.MinimumGold, encounter.MaximumGold));
                if (mapCount == 1) plans.Add(new(PredictorRewardKind.Potion,
                    PrefilledPotionKey: new("POTION", "BLOCK_POTION")));
                string relic = mapCount == 1 ? "VAJRA" : "ORNAMENTAL_FAN";
                var key = new ModelKey("RELIC", relic);
                bool inBag = Working.PersonalBag.Buckets.Any(b => b.Entries.Contains(key));
                plans.Add(new(PredictorRewardKind.Relic, PrefilledRelicKey: inBag ? key : null));
                AddManualCards(mapCount == 1 ? ["BLUDGEON", "PYRE", "EVIL_EYE"] :
                    ["PILLAGE", "RAMPAGE", "FLAME_BARRIER"]);
                return plans.ToImmutable();
            case PredictorCombatKind.Boss when mapCount == 1:
                plans.Add(new(PredictorRewardKind.Gold, encounter.MinimumGold, encounter.MaximumGold));
                AddManualCards("PRIMAL_FORCE", "DEMON_FORM", "THRASH");
                return plans.ToImmutable();
            default: return null;
        }
    }
}
