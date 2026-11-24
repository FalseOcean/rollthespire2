using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record ProvideRoomMaximumHp(int MaximumHp) : PredictorInput;

internal sealed partial class PredictorRun
{
    private PredictorPhase? _terminalResumePhase;

    private void PauseForTerminalEntry()
    {
        if (!Context.HasRunModifier("TERMINAL")) return;
        _terminalResumePhase = Phase;
        Phase = PredictorPhase.RoomEntryHealth;
        _requestId++;
    }

    private PredictorInputResult SubmitRoomMaximumHp(ProvideRoomMaximumHp input)
    {
        if (_terminalResumePhase is not { } resume || input.MaximumHp < 1) return PredictorInputResult.Invalid;
        // For a live valid player HP <= maxHP. Terminal does nothing at max=1;
        // otherwise LoseMaxHp(1) cannot kill, consume Fairy/Tail, or draw RNG.
        // HP remains outside canonical state; retain the actual max-HP command.
        if (input.MaximumHp > 1)
            Working = Working with { CurrentHealthEffects = Working.CurrentHealthEffects.Add(
                new(new ModelKey("MODIFIER", "TERMINAL"), 0, -1, 0)) };
        Accept(new("TerminalRoomEntry", 0, [], null, null) { SourceMaximumHp = input.MaximumHp });
        _terminalResumePhase = null;
        Phase = resume;
        if (resume == PredictorPhase.Complete) CompleteNode();
        return PredictorInputResult.Accepted;
    }
}
