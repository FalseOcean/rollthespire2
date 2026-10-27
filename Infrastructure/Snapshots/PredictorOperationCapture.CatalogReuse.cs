using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorLiveRunCapture
{
    // Owned by a captured snapshot, not a process-wide content/result cache.
    // The native canonical model contract forbids gameplay mutation of these
    // definitions (AbstractModel.AssertMutable; CardModel.ToMutable). Keep the
    // actual canonical reference: replacing registered content must
    // never inherit old upgrade definitions. Mutable and custom models are not
    // eligible. These references stay on the main-thread capture side; workers
    // continue to receive detached DTOs only.
    private static readonly ConditionalWeakTable<object,
        Dictionary<CardModel, ImmutableArray<PredictorCardLevelMetadata>>> SnapshotCatalogLevels = new();

    internal sealed class CatalogLevels
    {
        private readonly Dictionary<CardModel, ImmutableArray<PredictorCardLevelMetadata>>? _previous;
        private readonly Dictionary<CardModel, ImmutableArray<PredictorCardLevelMetadata>> _captured =
            new(ReferenceEqualityComparer.Instance);
        private readonly PredictorOperationCapture.CaptureTiming? _timing;

        internal CatalogLevels(object? previous, PredictorOperationCapture.CaptureTiming? timing = null)
        {
            if (previous != null) SnapshotCatalogLevels.TryGetValue(previous, out _previous);
            _timing = timing;
        }

        internal ImmutableArray<PredictorCardLevelMetadata> Capture(CardModel canonical)
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            bool stable = canonical.IsCanonical && !canonical.IsMock && canonical.GetType().Assembly == typeof(CardModel).Assembly;
            ImmutableArray<PredictorCardLevelMetadata> levels;
            if (stable && _previous != null && _previous.TryGetValue(canonical, out levels))
            {
                if (_timing != null) _timing.CatalogLevelReuses++;
            }
            else
            {
                levels = PredictorSeedCapture.CaptureLevels(canonical);
                if (_timing != null) _timing.CatalogLevelReads++;
            }
            if (stable) _captured.Add(canonical, levels);
            return levels;
        }

        internal void Remember(object snapshot) => SnapshotCatalogLevels.Add(snapshot, _captured);
    }
}
