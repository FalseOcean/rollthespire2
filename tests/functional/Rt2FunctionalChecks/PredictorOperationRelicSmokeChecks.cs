using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorOperationRelicSmokeChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("OPERATION_RELIC_SMOKE " + message); }

    // This is a captured-context admission/computation smoke, not independent
    // parity for every original AfterObtained coroutine or runtime acceptance.
    internal static void Run(PredictorOperationSnapshot captured)
    {
        Check(captured.Context.Operation?.PlayerCount > 1, "Massive Scroll fixture requires an actual multiplayer capture");
        var catalog = captured.Context.Catalog!;
        string[] sources = ["ARCANE_SCROLL", "HEFTY_TABLET", "LEAD_PAPERWEIGHT", "MASSIVE_SCROLL", "LOST_COFFER",
            "KALEIDOSCOPE", "SCROLL_BOXES", "GLASS_EYE", "SEA_GLASS", "ORRERY", "SILKEN_TRESS", "LARGE_CAPSULE",
            "SMALL_CAPSULE", "CALLING_BELL", "TOY_BOX", "NEOWS_BONES", "PHIAL_HOLSTER", "ALCHEMICAL_COFFER",
            "CAULDRON", "DISTINGUISHED_CAPE", "LEAFY_POULTICE", "NEW_LEAF", "ASTROLABE", "PANDORAS_BOX",
            "BEAUTIFUL_BRACELET", "SAND_CASTLE", "FRAGRANT_MUSHROOM", "WAR_PAINT", "WHETSTONE"];
        foreach (string source in sources)
        {
            try
            {
                var key = new ModelKey("RELIC", source);
                var original = captured.State;
                long offerId = original.NextInstanceId;
                var state = original with { NextInstanceId = offerId + 1 };
                ModelKey? target = source == "SEA_GLASS" ? catalog.CharacterPools.First().Character : null;
                var offer = new PredictorAncientOffer(offerId, key, true, target, null, null, null, null);
                var scene = new PredictorOperationAncientScene(new("EVENT", "NEOW"), [offer]);
                var result = PredictorOperation.Preview(captured with { Fingerprint = "relic-smoke:" + source, State = state, Scene = scene },
                    new ChooseAncientOffer(offerId));
                Check(result.Disposition == PredictorInputResult.Accepted && result.State.Relics.Any(r => r.Id == state.NextInstanceId && r.Key == key) &&
                    result.State.Relics.Count(r => r.Key == key) == state.Relics.Count(r => r.Key == key) + 1,
                    source + " did not acquire exactly the prepared offer");
                result.State.Validate();
                if (source == "ARCANE_SCROLL") Check(result.State.Deck.Length == state.Deck.Length + 1, source + " lost its rare card");
                if (source == "PHIAL_HOLSTER") Check(result.State.PotionSlots.Length == state.PotionSlots.Length + 1, source + " lost slot");
                if (source == "ALCHEMICAL_COFFER") Check(result.State.PotionSlots.Length == state.PotionSlots.Length + 4, source + " lost slots");
                if (source == "DISTINGUISHED_CAPE") Check(result.State.Deck.Length == state.Deck.Length + 5, source + " lost card quantity");
                if (source == "NEOWS_BONES") Check(result.Rewards.Length == 2 && result.Rewards.All(r => r.Mandatory), source + " lost grouped mandatory rewards");
                if (source == "TOY_BOX") Check(result.Rewards.Length == 5 && result.Rewards.All(r => r.IsWaxRelic), source + " lost wax reward group");
                if (source is "HEFTY_TABLET" or "LEAD_PAPERWEIGHT" or "MASSIVE_SCROLL")
                    Check(result.Waiting.Phase == PredictorPhase.ObtainChooseCard && !result.Waiting.Cards.IsEmpty,
                        source + " lost populated pending choice");
                if (source == "SEA_GLASS") Check(result.Waiting.Phase == PredictorPhase.ObtainChooseManyCards && !result.Waiting.Cards.IsEmpty,
                    source + " lost target-specific grouped cards");
                Check(captured.State == original, source + " changed captured input");
            }
            catch (Exception ex) { throw new InvalidOperationException("Operation captured-context relic source failed: " + source, ex); }
        }
        Console.WriteLine("OPERATION_RELIC_SMOKE actual multiplayer capture context: all 29 sources through prepared-offer facade to immediate outcome/next choice PASS");
    }
}
