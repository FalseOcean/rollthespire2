using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.World.Beta109;

public sealed class Beta109WorldRng
{
    private readonly Xoshiro256StarStar _rng;
    private readonly string _streamDomain;
    private readonly List<WorldRngTraceEntry> _trace = new();

    private Beta109WorldRng(Xoshiro256StarStar rng, string streamDomain)
    {
        _rng = rng;
        _streamDomain = streamDomain;
    }

    public int CallCount => _rng.CallCount;
    public IReadOnlyList<WorldRngTraceEntry> Trace => _trace;

    public static Beta109WorldRng CreateNamed(ulong runSeedRoot, string streamName)
    {
        string normalizedStreamName = streamName ?? string.Empty;
        ulong seed = unchecked(runSeedRoot + XxHash64.HashUtf8(normalizedStreamName, 0UL));
        return new Beta109WorldRng(new Xoshiro256StarStar(seed), normalizedStreamName);
    }

    /// <summary>
    /// Creates a local named RNG using the same constructor semantics as the game.
    /// This is intentionally separate from RunRngSet: act_selection exists only in
    /// StartRunLobby before the persistent run RNG set is constructed.
    /// </summary>
    public static Beta109WorldRng CreateLobbyLocal(ulong rootSeed, string localName)
    {
        ulong seed = unchecked(rootSeed + XxHash64.HashUtf8(localName ?? string.Empty, 0UL));
        return new Beta109WorldRng(new Xoshiro256StarStar(seed), "lobby_local:" + localName);
    }

    public static Beta109WorldRng CreateEventLocal(
        ulong runSeedRoot,
        int playerSlot,
        bool isShared,
        string eventEntry)
    {
        ulong eventSeed = DeriveEventLocalSeed(runSeedRoot, playerSlot, isShared, eventEntry);
        return new Beta109WorldRng(new Xoshiro256StarStar(eventSeed), "event-local");
    }

    public static Beta109WorldRng FromCheckpoint(
        Beta109RngStateSnapshot checkpoint,
        string streamDomain = "up_front") => new(
        Xoshiro256StarStar.FromState(
            checkpoint.S0,
            checkpoint.S1,
            checkpoint.S2,
            checkpoint.S3,
            checkpoint.CallCount),
        streamDomain);

    public static ulong DeriveEventLocalSeed(
        ulong runSeedRoot,
        int playerSlot,
        bool isShared,
        string eventEntry)
    {
        unchecked
        {
            long signedRoot = (long)runSeedRoot;
            long signedWithSlot = signedRoot + (isShared ? 0L : playerSlot);
            return (ulong)signedWithSlot + XxHash64.HashUtf8(eventEntry ?? string.Empty, 0UL);
        }
    }

    public int NextInt(
        int maxExclusive,
        string sourceStage,
        WorldRngConsumptionShape consumptionShape = WorldRngConsumptionShape.Fixed)
    {
        int result = _rng.NextInt(maxExclusive);
        _trace.Add(new WorldRngTraceEntry(
            _trace.Count,
            _streamDomain,
            sourceStage,
            "NextInt",
            _rng.CallCount,
            consumptionShape,
            Bound: maxExclusive,
            IntResult: result));
        return result;
    }

    public bool NextBool(string sourceStage) => NextInt(2, sourceStage) == 0;

    public double NextDouble(
        string sourceStage,
        WorldRngConsumptionShape consumptionShape = WorldRngConsumptionShape.Fixed)
    {
        double result = _rng.NextDouble();
        _trace.Add(new WorldRngTraceEntry(
            _trace.Count,
            _streamDomain,
            sourceStage,
            "NextDouble",
            _rng.CallCount,
            consumptionShape,
            DoubleResult: result));
        return result;
    }

    public float NextFloat(string sourceStage)
    {
        float result = _rng.NextFloat();
        _trace.Add(new WorldRngTraceEntry(
            _trace.Count,
            _streamDomain,
            sourceStage,
            "NextFloat",
            _rng.CallCount,
            WorldRngConsumptionShape.Fixed,
            DoubleResult: result));
        return result;
    }

    public T? NextItemOrDefault<T>(IReadOnlyList<T> source, string sourceStage)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count == 0) return default;
        int index = NextInt(source.Count, sourceStage);
        return source[index];
    }

    public ModelKey NextModelKey(IReadOnlyList<ModelKey> source, string sourceStage)
    {
        if (source.Count == 0) throw new InvalidOperationException("Beta109WorldCandidatePoolEmpty:" + sourceStage);
        int index = NextInt(source.Count, sourceStage);
        ModelKey selected = source[index];
        WorldRngTraceEntry previous = _trace[^1];
        _trace[^1] = previous with { SelectedKey = selected };
        return selected;
    }

    public void UnstableShuffle<T>(IList<T> values, string sourceStage)
    {
        ArgumentNullException.ThrowIfNull(values);
        for (int count = values.Count; count > 1; count--)
        {
            int selected = NextInt(
                count,
                sourceStage + $":tail{count - 1}",
                WorldRngConsumptionShape.Shuffle);
            int tail = count - 1;
            (values[selected], values[tail]) = (values[tail], values[selected]);
        }
    }

    public void AnnotateLastSelection(ModelKey selectedKey)
    {
        if (_trace.Count == 0)
        {
            return;
        }

        WorldRngTraceEntry previous = _trace[^1];
        _trace[^1] = previous with { SelectedKey = selectedKey };
    }

    public Beta109RngStateSnapshot CaptureState()
    {
        (ulong s0, ulong s1, ulong s2, ulong s3) = _rng.State;
        return new Beta109RngStateSnapshot(s0, s1, s2, s3, _rng.CallCount);
    }
}
