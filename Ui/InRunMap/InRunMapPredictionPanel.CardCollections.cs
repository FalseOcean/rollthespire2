using RolltheSpire2.Core.Collections;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapPredictionPanel
{
    internal const string DefaultOverviewCardCollectionId = "builtin:all-rare";
    private CardCollectionSnapshot? _overviewCardCollection;

    internal bool UsesDefaultOverviewCardCollection => _overviewCardCollection == null;

    // The built-in collection follows the actual forecast catalog. A custom
    // collection retains its selected revision until explicitly replaced;
    // editing/deleting its saved asset cannot mutate this captured selection.
    internal CardCollectionSnapshot? OverviewCardCollection => ResolveOverviewCardCollection()?.Copy();

    private CardCollectionSnapshot? ResolveOverviewCardCollection()
    {
        if (_overviewCardCollection != null) return _overviewCardCollection;
        if (_prediction.Snapshot?.Run.Context.Catalog is not { } catalog) return null;
        var cards = catalog.CardDefinitions
            .Where(definition => string.Equals(definition.Rarity, "Rare", StringComparison.OrdinalIgnoreCase))
            .Select(definition => definition.Prototype.Key)
            .Distinct(RolltheSpire2.Core.Identity.ModelKeyComparer.Instance).ToArray();
        return new(DefaultOverviewCardCollectionId, _text.Get("predictor.map.card_collection.all_rare"), 1,
            Array.AsReadOnly(cards));
    }

    internal void UseOverviewCardCollection(CardCollectionSnapshot collection)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(collection.Cards);
        _overviewCardCollection = collection.Copy();
        RefreshOverviewCardCollection();
    }

    internal void UseDefaultOverviewCardCollection()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (_overviewCardCollection == null) return;
        _overviewCardCollection = null;
        RefreshOverviewCardCollection();
    }

    private void RefreshOverviewCardCollection()
    {
        UpdateCollectionPicker();
        // Reproject published observations only. Neither this preference nor
        // its future picker owns route plans, forecast execution or real RNG.
        if (_overview != null && IsInstanceValid(_overview) && _featureEnabled && PredictionEnabled && _screen.IsOpen)
            RenderInformation();
    }
}
