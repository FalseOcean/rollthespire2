using System.Collections.Immutable;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Tests.FastSearch;

internal static class LiveRunSeedPremisesChecks
{
    internal static int Run()
    {
        NativeWorkbenchCheckEnvironment.Initialize("DreadOpeningPremises");
        if (!Enum.TryParse<GameMode>("Dread", out var dreadMode))
            throw new NotSupportedException("The Dread opening check requires the 0.113 game assembly.");
        var createRun = typeof(RunState).GetMethod(nameof(RunState.CreateForNewRun))!;
        var runModels = typeof(RunState).GetProperty("RunModels")!;
        var generateRooms = typeof(ActModel).GetMethod("GenerateRooms", [typeof(RunState)])
            ?? typeof(ActModel).GetMethod("GenerateRooms", [typeof(IRunState)])!;
        int pairs = 0;
        CharacterModel[] characters = [ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(),
            ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>(), ModelDb.Character<Defect>(),
            ModelDb.GetById<CharacterModel>(new ModelId("CHARACTER", "FOOL"))];
        foreach (var character in characters)
        foreach (string seed in new[] { "6Q6NTNQGP3T0", "6Q6B0U2XA308", "000000000000" })
        {
            var standard = Create(GameMode.Standard);
            var dread = Create(dreadMode);
            Require(LiveRunPredictionPremises.Capture(dread).IsOrdinaryDreadOpening, "Dread opening context");
            Require(JsonSerializer.Serialize(standard.Acts.Select(a => a.ToSave())) ==
                JsonSerializer.Serialize(dread.Acts.Select(a => a.ToSave())), "Native upfront rooms/ancients/bosses");
            foreach (var stream in Enum.GetValues<RunRngType>())
                Require(standard.Rng.GetRng(stream).NextInt() == dread.Rng.GetRng(stream).NextInt(),
                    "Native shared RNG " + stream);
            foreach (var stream in Enum.GetValues<PlayerRngType>())
                Require(standard.Players[0].PlayerRng.GetRng(stream).NextInt() ==
                    dread.Players[0].PlayerRng.GetRng(stream).NextInt(), "Native personal RNG " + stream);
            pairs++;

            RunState Create(GameMode mode)
            {
                Player[] players = [Player.CreateForNewRun(character, UnlockState.all, 1)];
                var run = (RunState)createRun.Invoke(null, [players, ActModel.GetDefaultList().Select(a => a.ToMutable()).ToArray(),
                    Array.Empty<ModifierModel>(), mode, false, 10, seed])!;
                run.ExtraFields.StartedWithNeow = true;
                run.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(run.UnlockState), run.Rng.UpFront);
                run.Players[0].PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
                foreach (var model in (IEnumerable<AbstractModel>)runModels.GetValue(run)!)
                    model.GetType().GetMethod("OnRunCreated", [typeof(RunState)])!.Invoke(model, [run]);
                foreach (var act in run.Acts) generateRooms.Invoke(act, [run]);
                return run;
            }
        }

        var ordinary = new LiveRunPredictionPremises("Dread", false, true, []);
        Require(ordinary.IsOrdinaryDreadOpening && ordinary.IsStandardReference,
            "Dread has concise copy but preserves the report's Standard context");
        Require(!(ordinary with { GameMode = "Standard" }).IsStandardReference, "Standard context remains direct");
        Require(!(ordinary with { IsCustomGame = true }).IsOrdinaryDreadOpening &&
            !(ordinary with { StartedWithNeow = false }).IsOrdinaryDreadOpening,
            "Only an ordinary Dread opening gets the concise copy");
        Require((ordinary with { IsCustomGame = true }).IsStandardReference, "Custom context retained");
        Require((ordinary with { StartedWithNeow = false }).IsStandardReference, "Missing Neow context retained");
        Require((ordinary with { Modifiers = ImmutableArray.Create(new PredictorRunModifier(
            new ModelKey("MODIFIER", "CHARACTER_CARDS"), new ModelKey("CHARACTER", "SILENT"))) }).IsStandardReference,
            "Actual modifier context retained");
        Require((ordinary with { GameMode = "Blitz" }).IsStandardReference, "Blitz remains distinct");
        Require((ordinary with { GameMode = "Daily" }).IsStandardReference, "Daily remains distinct");
        Console.WriteLine($"LIVE_RUN_SEED_PREMISES_PASS nativePairs={pairs}; six characters; shared/personal streams and upfront rooms; context variants");
        return 0;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
