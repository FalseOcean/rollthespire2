using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.Shell;

// Session-only native facts. A Standard seed reference keeps its existing seed-library context.
internal sealed record LiveRunPredictionPremises(
    string GameMode, bool IsCustomGame, bool StartedWithNeow, ImmutableArray<PredictorRunModifier> Modifiers)
{
    // On 0.111, Custom was a GameMode. On 0.113 it is a separate native flag.
    private static readonly Func<IRunState, bool>? ReadIsCustom = typeof(IRunState).GetProperty("IsCustomGame")
        ?.GetMethod?.CreateDelegate<Func<IRunState, bool>>();

    // This also labels maps and saved contexts, which still use Standard's
    // discovery ordering. Shared RNG initialization alone does not change that.
    internal bool IsStandardReference =>
        GameMode != "Standard" || IsCustomGame || !StartedWithNeow || !Modifiers.IsEmpty;

    internal bool IsOrdinaryDreadOpening =>
        GameMode == "Dread" && !IsCustomGame && StartedWithNeow && Modifiers.IsEmpty;

    internal static LiveRunPredictionPremises Capture(IRunState run)
    {
        string mode = run.GameMode.ToString();
        return new(mode, ReadIsCustom?.Invoke(run) ?? mode == "Custom", run.ExtraFields.StartedWithNeow,
            run.Modifiers.Select(modifier => new PredictorRunModifier(
                new ModelKey(modifier.Id.Category, modifier.Id.Entry),
                modifier is CharacterCards cards
                    ? new ModelKey(cards.CharacterModel.Category, cards.CharacterModel.Entry)
                    : null)).ToImmutableArray());
    }
}
