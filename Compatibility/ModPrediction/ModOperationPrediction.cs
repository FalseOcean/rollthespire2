using RolltheSpire2.Compatibility.ModPrediction.Hextech;
using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Compatibility.ModPrediction;

// Internal optional integrations, not a public SDK. Recognition and capture
// run on the main thread. A recognized operation owns its result even when
// capture fails: it must never fall through to unrelated vanilla semantics.
internal interface IModOperationPrediction
{
    OperationPredictionEntry Setting { get; }
    bool TryMatch(Control owner, out RunState? run, out ModelKey source, out long identity);
    ModOperationSnapshot Capture(Control owner, RunState run);
    bool WantsPrediction(OperationPredictionMode mode, RandomForeseerSnapshot other);
}

// Implementations contain detached values only. No Control, Player, RunState,
// native model, or callback closing over those objects may enter the worker.
internal abstract record ModOperationSnapshot(string Fingerprint)
{
    internal abstract OperationPreview Predict(CancellationToken token);
}

internal sealed record ModPredictionModule(string Id, ImmutableArray<IModOperationPrediction> Operations)
{
    internal Action<Control, string>? BindSurface { get; init; }
}
internal sealed record ModOperationMatch(IModOperationPrediction Capability, RunState Run,
    ModelKey Source, long Identity);

internal static class ModOperationPredictions
{
    internal static readonly ImmutableArray<ModPredictionModule> Modules = [HextechPredictionModule.Create()];
    internal static IEnumerable<OperationPredictionEntry> Settings =>
        Modules.SelectMany(module => module.Operations).Select(operation => operation.Setting);

    internal static ModOperationMatch? Match(Control owner)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ModOperationMatch? match = null;
        foreach (var module in Modules)
        foreach (var operation in module.Operations)
        {
            if (!operation.TryMatch(owner, out var run, out var source, out long identity)) continue;
            // These are alternative owners, not composable rule effects. An
            // overlap needs an explicit implementation rather than list order.
            if (match != null) throw new InvalidOperationException(
                $"ModOperationOwnershipConflict:{match.Capability.Setting.Id}:{operation.Setting.Id}");
            match = new(operation, run ?? throw new InvalidOperationException("ModOperationRunMissing"), source, identity);
        }
        return match;
    }
}
