using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace RolltheSpire2.Ui.Settings;

internal enum OperationPredictionCategory
{
    RewardReroll,
    RewardSacrifice,
    ShopRestock,
    CardTransform,
    RelicEffect,
    EventOption,
    PotionResult
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationPredictionMode>))]
internal enum OperationPredictionMode { Auto, On, Off }

internal sealed record OperationPredictionPreferences
{
    // Categories are batch commands over leaves, never inherited defaults.
    public ImmutableDictionary<string, OperationPredictionMode> Entries { get; init; } =
        ImmutableDictionary.Create<string, OperationPredictionMode>(StringComparer.Ordinal);

    internal OperationPredictionMode Get(string id) => Entries.TryGetValue(id, out var mode) ? mode : OperationPredictionMode.Auto;

    internal OperationPredictionMode Get(OperationPredictionCategory category)
    {
        var modes = OperationPredictionCatalog.ForCategory(category).Select(entry => Get(entry.Id)).Distinct().Take(2).ToArray();
        return modes.Length == 1 ? modes[0] : OperationPredictionMode.Auto;
    }

    internal OperationPredictionPreferences With(string id, OperationPredictionMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return this with { Entries = mode == OperationPredictionMode.Auto ? Entries.Remove(id) : Entries.SetItem(id, mode) };
    }

    internal OperationPredictionPreferences With(OperationPredictionCategory category, OperationPredictionMode mode)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var updated = this;
        foreach (var entry in OperationPredictionCatalog.ForCategory(category)) updated = updated.With(entry.Id, mode);
        return updated;
    }

    internal OperationPredictionPreferences Normalize() => this with
    {
        Entries = (Entries ?? ImmutableDictionary<string, OperationPredictionMode>.Empty)
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && Enum.IsDefined(pair.Value) && pair.Value != OperationPredictionMode.Auto)
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
    };
}
