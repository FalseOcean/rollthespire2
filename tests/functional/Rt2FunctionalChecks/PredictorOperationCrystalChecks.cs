using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorOperationCrystalChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static void Run(PredictorRun baseline)
    {
        var context = baseline.Context with { Operation = new(1, 0, 1, []) };
        var state = baseline.Working with
        {
            Relics = [], OpeningCompleted = true,
            PersonalBag = new(false, [new(new("RELIC", "WHETSTONE"), "Common")],
                [new("Common", [new("RELIC", "WHETSTONE")])], [])
        };
        // This curse was revealed before capture; its actual deck effect is
        // already present and must never be applied again by the importer.
        state = PredictorObtainSources.AddCard(context, state, new ModelKey("CARD", "DOUBT"));
        int originalDoubts = state.Deck.Count(id => state.Card(id).Key.Entry == "DOUBT");
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        for (int i = 0; rng.Clone().NextFloat() >= .5f; i++)
        { Check(i < 64, "Crystal controlled common cursor unavailable"); rng.NextFloat(); }
        var hidden = new bool[121]; hidden[7 * 11 + 7] = hidden[9 * 11 + 9] = true;
        var occupancy = Enumerable.Repeat(-1, 121).ToArray();
        for (int x = 4; x < 8; x++) for (int y = 4; y < 8; y++) occupancy[x * 11 + y] = 0;
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) occupancy[x * 11 + y] = 1;
        var board = new PredictorOperationCrystalScene(PredictorStreamState.Capture(PredictorStream.Rewards, rng),
            2, PredictorCrystalTool.Big, true, hidden.ToImmutableArray(), occupancy.ToImmutableArray(),
            [new("RELIC", 4, 4, 4, 4, 1, false), new("CURSE", 2, 2, 0, 0, 1, true)], [1]);
        var snapshot = new PredictorOperationSnapshot("crystal:before", context, state, board, 60, 80);
        var first = PredictorOperation.Preview(snapshot, [new SelectCrystalTool(PredictorCrystalTool.Small), new RevealCrystalCell(7, 7)]);
        var afterFirst = first.CrystalBoard ?? throw new InvalidOperationException("Crystal operation lost current board");
        Check(first.Disposition == PredictorInputResult.Accepted && afterFirst.Remaining == 1 &&
            afterFirst.Items[0].Revealed && first.Rewards.IsEmpty && first.State == state,
            "Partial Crystal reveal generated an identity/receipt too early or replayed an old curse");
        var second = PredictorOperation.Preview(snapshot with { Fingerprint = "crystal:after-one", State = first.State, Scene = afterFirst }, new RevealCrystalCell(9, 9));
        Check(second.Rewards.Single().Key == new ModelKey("RELIC", "WHETSTONE") &&
            second.State.Deck.Count(id => second.State.Card(id).Key.Entry == "DOUBT") == originalDoubts,
            "Final Crystal reveal lost reward identity or duplicated a previously revealed curse");
        // Compare the newly imported board against the established isolated
        // Crystal engine. This checks adapter parity, not new native evidence.
        var existing = PredictorRun.FromCrystal(new(baseline.Context, state, board.EventRng, board.Remaining,
            board.Tool, board.PlacedAllItems, board.Hidden, board.Occupancy, board.Items, board.Revealed));
        foreach (PredictorInput input in new PredictorInput[] { new SelectCrystalTool(PredictorCrystalTool.Small),
            new RevealCrystalCell(7, 7), new RevealCrystalCell(9, 9) })
            Check(existing.Submit(existing.Request!, input) == PredictorInputResult.Accepted, "Existing Crystal fixture rejected input");
        Check(second.Rewards.SequenceEqual(existing.Rewards) && second.State.Streams.SequenceEqual(existing.Working.Streams),
            "Operation board import changed existing Crystal generation/RNG");
        var finalBoard = second.CrystalBoard!;
        var rewards = new PredictorOperationEventScene(new("EVENT", "CRYSTAL_SPHERE"), finalBoard.EventRng, [], new(60, 80))
        { RewardScene = new(second.Rewards) };
        var obtained = PredictorOperation.Preview(snapshot with { Fingerprint = "crystal:reward", State = second.State, Scene = rewards },
            new TakeReward(second.Rewards[0].Id));
        Check(obtained.State.Relics.Count(r => r.Key.Entry == "WHETSTONE") == 1 &&
            obtained.State.Deck.Any(id => obtained.State.Card(id).UpgradeLevel > state.Card(id).UpgradeLevel),
            "Operation Crystal reward did not use full automatic relic obtain settlement");
        Check(state.Deck.Count(id => state.Card(id).Key.Entry == "DOUBT") == originalDoubts && state.Relics.IsEmpty,
            "Crystal operation changed captured source state");
        Console.WriteLine("OPERATION_CRYSTAL current board/one click/no early identities/old curse preservation/existing-engine parity/full obtain PASS");
    }
}
