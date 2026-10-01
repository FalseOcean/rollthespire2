using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorCardChanges
{
    internal static PredictorCardDefinition Definition(PredictorContext context, ModelKey key) =>
        (context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing")).CardDefinitions.Single(d => d.Prototype.Key == key);
}
