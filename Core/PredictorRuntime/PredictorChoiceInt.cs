using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// The contract identifies the caller's ordered, mutually exclusive alternatives,
// not this particular property's numeric contributions. Reuse across properties
// is valid only when ID, count and ordered meaning agree.
internal sealed record PredictorChoiceIntSource(string ChoiceId, string AlternativesContract, int AlternativeCount);

// A proof enclosure, not a list of reachable values or a promise that either
// endpoint is attainable. Equal endpoints do prove one exact integer.
internal readonly record struct PredictorChoiceIntBounds(long Lower, long Upper);

// Pure detached integer expressions. Affine contributions are combined per
// original choice before bounding: independently bounding card populations
// would lose their mutual exclusion. Nonlinear clamps retain their exact input
// expression; no Cartesian product or endpoint substitution is used.
internal sealed class PredictorChoiceInt : IEquatable<PredictorChoiceInt>
{
    private static readonly ImmutableDictionary<string, ImmutableArray<long>> EmptyChoices =
        ImmutableDictionary.Create<string, ImmutableArray<long>>(StringComparer.Ordinal);
    private static readonly ImmutableDictionary<string, PredictorChoiceIntSource> EmptySources =
        ImmutableDictionary.Create<string, PredictorChoiceIntSource>(StringComparer.Ordinal);
    private readonly long _constant;
    // Alternative zero is absorbed into _constant. All stored vectors start at
    // zero, so combining contributions also has a canonical cancellation form.
    private readonly ImmutableDictionary<string, ImmutableArray<long>> _choices;
    // Each atom means max(0, key's exact expression), multiplied by its value.
    private readonly ImmutableDictionary<PredictorChoiceInt, long> _clamps;
    // Keep contracts even after numeric cancellation or multiplication by zero.
    // A later incompatible declaration of that same source must still fail.
    private readonly ImmutableDictionary<string, PredictorChoiceIntSource> _sources;
    private readonly int _hashCode;

    private PredictorChoiceInt(long constant,
        ImmutableDictionary<string, ImmutableArray<long>> choices,
        ImmutableDictionary<PredictorChoiceInt, long> clamps,
        ImmutableDictionary<string, PredictorChoiceIntSource> sources)
    {
        _constant = constant;
        _choices = choices;
        _clamps = clamps;
        _sources = sources;
        Bounds = CalculateBounds();
        _hashCode = CalculateHashCode();
    }

    internal PredictorChoiceIntBounds Bounds { get; }
    internal long? KnownValue => Bounds.Lower == Bounds.Upper ? Bounds.Lower : null;
    internal bool IsAffine => _clamps.IsEmpty;
    internal ImmutableArray<PredictorChoiceIntSource> Sources => _sources.Values
        .OrderBy(s => s.ChoiceId, StringComparer.Ordinal).ToImmutableArray();

    internal static PredictorChoiceInt Constant(long value) =>
        new(value, EmptyChoices, ImmutableDictionary<PredictorChoiceInt, long>.Empty, EmptySources);

    internal static PredictorChoiceInt Alternatives(string choiceId, string alternativesContract,
        params long[] values)
    {
        if (string.IsNullOrWhiteSpace(choiceId) || string.IsNullOrWhiteSpace(alternativesContract))
            throw new ArgumentException("PredictorChoiceIdentityOrContractMissing");
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0) throw new ArgumentException("PredictorChoiceAlternativesEmpty");
        var source = new PredictorChoiceIntSource(choiceId, alternativesContract, values.Length);
        return FromValues(choiceId, values.ToImmutableArray(), EmptySources.Add(choiceId, source));
    }

    private static PredictorChoiceInt FromValues(string choiceId, ImmutableArray<long> values,
        ImmutableDictionary<string, PredictorChoiceIntSource> sources)
    {
        long baseline = values[0];
        var offsets = values.Select(v => checked(v - baseline)).ToImmutableArray();
        return new(baseline, offsets.All(v => v == 0) ? EmptyChoices : EmptyChoices.Add(choiceId, offsets),
            ImmutableDictionary<PredictorChoiceInt, long>.Empty, sources);
    }

    internal PredictorChoiceInt Add(PredictorChoiceInt other) => Combine(other, 1);
    internal PredictorChoiceInt Subtract(PredictorChoiceInt other) => Combine(other, -1);

    private PredictorChoiceInt Combine(PredictorChoiceInt other, long sign)
    {
        ArgumentNullException.ThrowIfNull(other);
        var sources = MergeSources(other);
        var choices = _choices.ToBuilder();
        foreach (var (id, right) in other._choices)
        {
            var combined = choices.TryGetValue(id, out var left)
                ? left.Select((v, i) => checked(v + checked(sign * right[i]))).ToImmutableArray()
                : right.Select(v => checked(sign * v)).ToImmutableArray();
            if (combined.All(v => v == 0)) choices.Remove(id);
            else choices[id] = combined;
        }
        var clamps = _clamps.ToBuilder();
        foreach (var (input, coefficient) in other._clamps)
        {
            clamps.TryGetValue(input, out long prior);
            long next = checked(prior + checked(sign * coefficient));
            if (next == 0) clamps.Remove(input);
            else clamps[input] = next;
        }
        return new(checked(_constant + checked(sign * other._constant)), choices.ToImmutable(),
            clamps.ToImmutable(), sources);
    }

    private ImmutableDictionary<string, PredictorChoiceIntSource> MergeSources(PredictorChoiceInt other)
    {
        var sources = _sources.ToBuilder();
        foreach (var (id, source) in other._sources)
        {
            if (sources.TryGetValue(id, out var existing) && existing != source)
                throw new ArgumentException("PredictorChoiceSchemaConflict:" + id);
            sources[id] = source;
        }
        return sources.ToImmutable();
    }

    internal PredictorChoiceInt Scale(long factor)
    {
        if (factor == 0) return new(0, EmptyChoices, ImmutableDictionary<PredictorChoiceInt, long>.Empty, _sources);
        if (factor == 1) return this;
        var choices = _choices.ToImmutableDictionary(p => p.Key,
            p => p.Value.Select(v => checked(v * factor)).ToImmutableArray(), StringComparer.Ordinal);
        var clamps = _clamps.ToImmutableDictionary(p => p.Key, p => checked(p.Value * factor));
        return new(checked(_constant * factor), choices, clamps, _sources);
    }

    internal PredictorChoiceInt ClampNonNegative()
    {
        if (Bounds.Lower >= 0) return this;
        if (Bounds.Upper <= 0)
            return new(0, EmptyChoices, ImmutableDictionary<PredictorChoiceInt, long>.Empty, _sources);
        // One live choice is cheap to project exactly, including other sources
        // whose contributions already cancelled. No cross-source enumeration.
        if (_clamps.IsEmpty && _choices.Count == 1)
        {
            var (id, offsets) = _choices.Single();
            return FromValues(id, offsets.Select(v => Math.Max(0, checked(_constant + v))).ToImmutableArray(), _sources);
        }
        return new(0, EmptyChoices, ImmutableDictionary<PredictorChoiceInt, long>.Empty.Add(this, 1), _sources);
    }

    internal PredictorChoiceInt SubtractAndClamp(PredictorChoiceInt amount) => Subtract(amount).ClampNonNegative();
    internal PredictorChoiceInt SubtractAndClamp(long amount) => (this - amount).ClampNonNegative();

    // Exact evaluation of a caller-supplied witness. Missing assignments for
    // numerically cancelled choices are unnecessary; contracts are still kept.
    internal long Evaluate(IReadOnlyDictionary<string, int> alternatives)
    {
        ArgumentNullException.ThrowIfNull(alternatives);
        foreach (var (id, source) in _sources)
            if (alternatives.TryGetValue(id, out int index) && (index < 0 || index >= source.AlternativeCount))
                throw new ArgumentOutOfRangeException(nameof(alternatives), "PredictorChoiceAlternativeInvalid:" + id);
        long value = _constant;
        foreach (var (id, offsets) in _choices)
        {
            if (!alternatives.TryGetValue(id, out int index))
                throw new ArgumentException("PredictorChoiceAssignmentMissing:" + id, nameof(alternatives));
            value = checked(value + offsets[index]);
        }
        foreach (var (input, coefficient) in _clamps)
            value = checked(value + checked(coefficient * Math.Max(0, input.Evaluate(alternatives))));
        return value;
    }

    private PredictorChoiceIntBounds CalculateBounds()
    {
        long lower = _constant, upper = _constant;
        foreach (var offsets in _choices.Values)
        {
            lower = checked(lower + offsets.Min());
            upper = checked(upper + offsets.Max());
        }
        foreach (var (input, coefficient) in _clamps)
        {
            long a = checked(coefficient * Math.Max(0, input.Bounds.Lower));
            long b = checked(coefficient * Math.Max(0, input.Bounds.Upper));
            lower = checked(lower + Math.Min(a, b));
            upper = checked(upper + Math.Max(a, b));
        }
        return new(lower, upper);
    }

    public bool Equals(PredictorChoiceInt? other) => ReferenceEquals(this, other) || other != null &&
        _hashCode == other._hashCode && _constant == other._constant &&
        _sources.Count == other._sources.Count && _sources.All(p => other._sources.TryGetValue(p.Key, out var s) && p.Value == s) &&
        _choices.Count == other._choices.Count && _choices.All(p => other._choices.TryGetValue(p.Key, out var v) && p.Value.SequenceEqual(v)) &&
        _clamps.Count == other._clamps.Count && _clamps.All(p => other._clamps.TryGetValue(p.Key, out long v) && p.Value == v);

    public override bool Equals(object? obj) => obj is PredictorChoiceInt other && Equals(other);
    public override int GetHashCode() => _hashCode;

    private int CalculateHashCode()
    {
        int hash = _constant.GetHashCode();
        foreach (var (id, source) in _sources) hash ^= HashCode.Combine(id, source);
        foreach (var (id, offsets) in _choices)
        {
            var vector = new HashCode();
            foreach (long value in offsets) vector.Add(value);
            hash ^= HashCode.Combine(id, vector.ToHashCode());
        }
        foreach (var (input, coefficient) in _clamps) hash ^= HashCode.Combine(input, coefficient);
        return hash;
    }

    public static PredictorChoiceInt operator +(PredictorChoiceInt a, PredictorChoiceInt b) => a.Add(b);
    public static PredictorChoiceInt operator -(PredictorChoiceInt a, PredictorChoiceInt b) => a.Subtract(b);
    public static PredictorChoiceInt operator +(PredictorChoiceInt a, long b) => a.Add(Constant(b));
    public static PredictorChoiceInt operator +(long a, PredictorChoiceInt b) => Constant(a).Add(b);
    public static PredictorChoiceInt operator -(PredictorChoiceInt a, long b) => a.Subtract(Constant(b));
    public static PredictorChoiceInt operator -(long a, PredictorChoiceInt b) => Constant(a).Subtract(b);
    public static PredictorChoiceInt operator *(PredictorChoiceInt a, long b) => a.Scale(b);
    public static PredictorChoiceInt operator *(long a, PredictorChoiceInt b) => b.Scale(a);
}
