namespace RolltheSpire2.Core.Identity;

/// <summary>
/// Exact game-model identity. Category and Entry are preserved exactly as supplied
/// by an audited game/catalog boundary. No case folding, underscore removal, alias
/// compression, or display-name comparison is permitted in business code.
/// </summary>
public readonly struct ModelKey : IEquatable<ModelKey>
{
    private readonly string? _category;
    private readonly string? _entry;

    public ModelKey(string? category, string? entry)
    {
        _category = category ?? string.Empty;
        _entry = entry ?? string.Empty;
    }

    public string Category => _category ?? string.Empty;
    public string Entry => _entry ?? string.Empty;
    public bool IsValid => Category.Length > 0 && Entry.Length > 0;
    public bool IsEmpty => !IsValid;
    public string Serialized => $"{Category}:{Entry}";

    public bool Equals(ModelKey other) =>
        string.Equals(Category, other.Category, StringComparison.Ordinal) &&
        string.Equals(Entry, other.Entry, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ModelKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(Category),
        StringComparer.Ordinal.GetHashCode(Entry));

    public override string ToString() => Serialized;

    public static bool operator ==(ModelKey left, ModelKey right) => left.Equals(right);
    public static bool operator !=(ModelKey left, ModelKey right) => !left.Equals(right);

    public static bool TryParseExact(string? serialized, out ModelKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(serialized))
        {
            return false;
        }

        int separator = serialized.IndexOf(':');
        if (separator <= 0 || separator >= serialized.Length - 1 ||
            serialized.LastIndexOf(':') != separator)
        {
            return false;
        }

        key = new ModelKey(serialized[..separator], serialized[(separator + 1)..]);
        return key.IsValid;
    }
}

public sealed class ModelKeyComparer : IEqualityComparer<ModelKey>
{
    public static ModelKeyComparer Instance { get; } = new();

    private ModelKeyComparer() { }

    public bool Equals(ModelKey left, ModelKey right) => left.Equals(right);
    public int GetHashCode(ModelKey value) => value.GetHashCode();
}
