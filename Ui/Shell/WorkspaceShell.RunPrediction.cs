using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    internal void OpenRunPrediction(IRunState run)
    {
        var context = CaptureRunPredictionContext(run, _runtime.Detection.NormalizedVersion, _runtime.Profile.ProfileId,
            LocalContext.NetId, _persistence!.PredictorContext);
        _partyExpected = null; _partyResultDraft = null;
        if (_predictor is null) CreatePredictor();
        _predictorController!.ClearSeedLibraryOverrides();
        _predictorController.OpenPredictionContext(run.Rng.StringSeed, context);
        SelectTask(Workspace.Analysis);
        _predictor!.ShowLibraryReceipt(_languageCode == "zh"
            ? "当前对局的开局预测；领取顺序请与实机保持一致。"
            : "Opening prediction for the current run. Match pickup order to your actual play.");
    }

    // Copy context only. Never use or advance the real run's RNG, deck or reward state.
    internal static SeedLibraryContext CaptureRunPredictionContext(IRunState run, string version,
        Compatibility.RuntimeProfileId profile, ulong? localPlayer, PredictorContextDocument saved)
    {
        var mode = run.Players.Count > 1 ? WorldGameMode.Multiplayer : WorldGameMode.Singleplayer;
        var players = run.Players.Select((p, slot) => new SeedLibraryPlayer(slot,
            new ModelKey(p.Character.Id.Category, p.Character.Id.Entry), LobbyUnlockReadout.Copy(p.UnlockState.ToSerializable()),
            "CapturedRunSlot", AncientOptionConditionProfile.BroadDefault, SearchQuery.Empty, null)).ToArray();
        int selected = Math.Max(0, run.Players.ToList().FindIndex(p => p.NetId == localPlayer));
        // Preserve intentional opening selections only for this exact seed/roster/unlock context.
        // A prediction left open for another seed must never overwrite the live run's context.
        if (mode == WorldGameMode.Multiplayer && saved.PartySeed == run.Rng.StringSeed && saved.Party is { } previous &&
            previous.Mode == mode && previous.GameVersion == version && previous.Profile == profile &&
            previous.Ascension == run.AscensionLevel && previous.Players.Count == players.Length &&
            previous.Players.Where((p, i) => p.Slot != i || p.Character != players[i].Character ||
                System.Text.Json.JsonSerializer.Serialize(p.Unlocks) != System.Text.Json.JsonSerializer.Serialize(players[i].Unlocks)).Any() == false)
            players = players.Select((p,i) => p with { AncientPremises = previous.Players[i].AncientPremises,
                OpeningPremise = previous.Players[i].OpeningPremise, Selection = previous.Players[i].Selection }).ToArray();
        else if (mode == WorldGameMode.Singleplayer && saved.PlayersCount == 1 && saved.Seed == run.Rng.StringSeed &&
            saved.CharacterKey == players[0].Character.Serialized && saved.Ascension == run.AscensionLevel)
            players[0] = players[0] with { AncientPremises = saved.AncientOptionConditions,
                Selection = new(saved.PreferredOpeningChoiceSlotIndex >= 0 ? saved.PreferredOpeningChoiceSlotIndex : null,
                    saved.PreferredOpeningRouteId, true) };
        return new(version, profile, mode, run.AscensionLevel, players, selected);
    }
}
