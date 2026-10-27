using System.Diagnostics;
using System.Reflection;
using RolltheSpire2.Core.Authority.Runtime;

namespace RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;

/// <summary>A menu-owned cursor. Native objects live only here and are released on completion/disposal.</summary>
internal sealed class RuntimeAuthorityObservationCursor : IDisposable
{
    private readonly string _gameVersion;
    private IEnumerator<object?>? _steps;
    private RuntimeAuthorityObservedInputs? _result;
    private bool _disposed;

    internal RuntimeAuthorityObservationCursor(string gameVersion)
    {
        _gameVersion = gameVersion ?? string.Empty;
        _steps = RuntimeAuthoritySnapshotCapture.ObserveInputs(_gameVersion, value => _result = value).GetEnumerator();
    }

    public bool IsCompleted => _result is not null;
    public RuntimeAuthorityObservedInputs Result => _result ?? throw new InvalidOperationException("Observation is incomplete.");

    /// <summary>Soft budget: an individual native getter/enumeration cannot be interrupted.</summary>
    public void Advance(TimeSpan budget)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsCompleted || budget <= TimeSpan.Zero) return;
        long started = Stopwatch.GetTimestamp();
        do { Step(); }
        while (!IsCompleted && Stopwatch.GetElapsedTime(started) < budget);
    }

    /// <summary>A cursor advanced across frames remains a candidate, never fresh authority.</summary>
    public RuntimeAuthorityObservedInputs CompleteOnMainThread()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (!IsCompleted) Step();
        return Result;
    }

    private void Step()
    {
        try
        {
            if (_steps?.MoveNext() != true)
            {
                if (_result is null) throw new InvalidOperationException("Observation ended without inputs.");
                _steps?.Dispose();
                _steps = null;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(ex, "RuntimeAuthorityCaptureFailed",
                nameof(RuntimeAuthoritySnapshotCapture), "EnvironmentCapture", "issues.capture_failed");
            _result = RuntimeAuthorityObservedInputs.Unavailable(_gameVersion,
                "RuntimeAuthorityCaptureFailed:" + ex.GetType().Name + ":" + RuntimeAuthoritySnapshotCapture.CompactObservationError(ex.Message));
            _steps?.Dispose();
            _steps = null;
        }
    }

    public void Dispose()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        _steps?.Dispose();
        _steps = null;
        _disposed = true;
    }
}

/// <summary>Self-contained copied primitive facts. Hashing/interpretation may run off-thread.</summary>
internal sealed class RuntimeAuthorityObservedInputs
{
    private readonly RuntimeSemanticDomainSnapshot[] _semantic;
    private readonly RuntimeUnlockAuthorityDomainSnapshot[] _authority;
    private readonly RuntimeCurrentUnlockDomainSnapshot[] _current;
    private readonly string[] _issues;
    private readonly string? _unavailable;
    public string GameVersion { get; }
    public DateTimeOffset CapturedAtUtc { get; }

    internal RuntimeAuthorityObservedInputs(string gameVersion, IEnumerable<RuntimeSemanticDomainSnapshot> semantic,
        IEnumerable<RuntimeUnlockAuthorityDomainSnapshot> authority, IEnumerable<RuntimeCurrentUnlockDomainSnapshot> current,
        IEnumerable<string> issues, string? unavailable = null)
    {
        GameVersion = gameVersion;
        _semantic = semantic.Select(domain => domain with { Items = Array.AsReadOnly(domain.Items.Select(item =>
            item with { Fields = Array.AsReadOnly(item.Fields.ToArray()) }).ToArray()) }).ToArray();
        _authority = authority.Select(domain => domain with { UnlockUniverse = Array.AsReadOnly(domain.UnlockUniverse.ToArray()) }).ToArray();
        _current = current.Select(domain => domain with { CurrentUnlocked = Array.AsReadOnly(domain.CurrentUnlocked.ToArray()) }).ToArray();
        _issues = issues.Distinct(StringComparer.Ordinal).ToArray();
        _unavailable = unavailable;
        CapturedAtUtc = DateTimeOffset.UtcNow;
    }

    internal static RuntimeAuthorityObservedInputs Unavailable(string gameVersion, string issue) =>
        new(gameVersion, Array.Empty<RuntimeSemanticDomainSnapshot>(), Array.Empty<RuntimeUnlockAuthorityDomainSnapshot>(),
            Array.Empty<RuntimeCurrentUnlockDomainSnapshot>(), new[] { issue }, issue);

    public RuntimeAuthoritySnapshot Derive()
    {
        if (_unavailable is not null)
            return RuntimeAuthoritySnapshot.Unavailable(GameVersion, _unavailable) with { CapturedAtUtc = CapturedAtUtc };
        RuntimeSemanticFingerprintBundle fingerprint = RuntimeSemanticFingerprint.Build(
            RuntimeAuthorityFingerprintSchema.Current, _semantic, _authority);
        string[] issues = fingerprint.Complete ? _issues : _issues.Append(fingerprint.EvidenceCode).Distinct(StringComparer.Ordinal).ToArray();
        return new RuntimeAuthoritySnapshot(GameVersion, RuntimeAuthorityFingerprintSchema.Current,
            Array.AsReadOnly(_semantic), Array.AsReadOnly(_authority), Array.AsReadOnly(_current), fingerprint,
            CapturedAtUtc, Array.AsReadOnly(issues));
    }

    public bool SemanticEquals(RuntimeAuthorityObservedInputs? other) => other is not null &&
        GameVersion == other.GameVersion && _unavailable == other._unavailable &&
        _issues.SequenceEqual(other._issues, StringComparer.Ordinal) &&
        _semantic.Length == other._semantic.Length && _semantic.Zip(other._semantic).All(pair =>
            pair.First.Domain == pair.Second.Domain && pair.First.AuthorityComplete == pair.Second.AuthorityComplete &&
            pair.First.EvidenceCode == pair.Second.EvidenceCode && pair.First.Items.Count == pair.Second.Items.Count &&
            pair.First.Items.Zip(pair.Second.Items).All(items => items.First.StableIdentity == items.Second.StableIdentity &&
                items.First.Fields.SequenceEqual(items.Second.Fields, StringComparer.Ordinal))) &&
        _authority.Length == other._authority.Length && _authority.Zip(other._authority).All(pair =>
            pair.First.Domain == pair.Second.Domain && pair.First.AuthorityComplete == pair.Second.AuthorityComplete &&
            pair.First.EvidenceCode == pair.Second.EvidenceCode && pair.First.UnlockUniverse.SequenceEqual(pair.Second.UnlockUniverse, StringComparer.Ordinal)) &&
        _current.Length == other._current.Length && _current.Zip(other._current).All(pair =>
            pair.First.Domain == pair.Second.Domain && pair.First.StateComplete == pair.Second.StateComplete &&
            pair.First.EvidenceCode == pair.Second.EvidenceCode && pair.First.CurrentUnlocked.SequenceEqual(pair.Second.CurrentUnlocked, StringComparer.Ordinal));
}

internal static partial class RuntimeAuthoritySnapshotCapture
{
    internal static string CompactObservationError(string value) => Compact(value);

    internal static IEnumerable<object?> ObserveInputs(string gameVersion, Action<RuntimeAuthorityObservedInputs> completed)
    {
        var issues = new List<string>();
        Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(value =>
            string.Equals(value.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
        if (assembly is null) { completed(RuntimeAuthorityObservedInputs.Unavailable(gameVersion, "RuntimeAuthoritySts2AssemblyMissing")); yield break; }
        Type? modelDb = FindType(assembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
        if (modelDb is null) { completed(RuntimeAuthorityObservedInputs.Unavailable(gameVersion, "RuntimeAuthorityModelDbMissing")); yield break; }
        yield return null;
        var cards = ReadStaticModelsRaw(modelDb, "AllCardPools", issues);
        yield return null;
        var relics = ReadStaticModelsRaw(modelDb, "AllRelicPools", issues);
        yield return null;
        var potions = ReadStaticModelsRaw(modelDb, "AllPotionPools", issues);
        yield return null;
        var acts = ReadActs(modelDb, issues);
        PoolMembershipCapture cardMembership = new(new Dictionary<string, string[]>(), false);
        PoolMembershipCapture relicMembership = cardMembership, potionMembership = cardMembership;
        foreach (var step in ObservePoolMembership(cards, "AllCards", issues, "Cards", value => cardMembership = value)) yield return step;
        foreach (var step in ObservePoolMembership(relics, "AllRelics", issues, "Relics", value => relicMembership = value)) yield return step;
        foreach (var step in ObservePoolMembership(potions, "AllPotions", issues, "Potions", value => potionMembership = value)) yield return step;
        var semantic = new List<RuntimeSemanticDomainSnapshot>();
        foreach (var step in ObserveCharacters(modelDb, semantic.Add)) yield return step;
        foreach (var step in ObserveCards(modelDb, cardMembership, semantic.Add)) yield return step;
        foreach (var step in ObserveRelics(modelDb, relicMembership, semantic.Add)) yield return step;
        foreach (var step in ObserveAncients(modelDb, acts, semantic.Add)) yield return step;
        foreach (var step in ObservePotions(potions, potionMembership, semantic.Add)) yield return step;
        foreach (var step in ObserveEvents(assembly, modelDb, acts, semantic.Add)) yield return step;
        foreach (var step in ObserveBosses(acts, semantic.Add)) yield return step;
        yield return null;
        object? currentUnlock = TryCaptureCurrentUnlockState(assembly, out string currentEvidence);
        yield return null;
        object? allUnlock = TryCaptureAllUnlockState(assembly, out string allEvidence);
        var authority = new List<RuntimeUnlockAuthorityDomainSnapshot>();
        var current = new List<RuntimeCurrentUnlockDomainSnapshot>();
        foreach (var step in ObserveUnlockDomains(cards, relics, potions, acts, currentUnlock, allUnlock,
                     currentEvidence, allEvidence, authority, current)) yield return step;
        completed(new RuntimeAuthorityObservedInputs(gameVersion, semantic, authority, current, issues));
    }

    private static IEnumerable<object?> ObserveUnlockDomains(IReadOnlyList<object> cards, IReadOnlyList<object> relics,
        IReadOnlyList<object> potions, IReadOnlyList<object> acts, object? currentUnlock, object? allUnlock,
        string currentEvidence, string allEvidence, List<RuntimeUnlockAuthorityDomainSnapshot> authority,
        List<RuntimeCurrentUnlockDomainSnapshot> current)
    {
        yield return null;
        var allCharacters = ReadModelIdentityProperty(allUnlock, "Characters");
        var currentCharacters = ReadModelIdentityProperty(currentUnlock, "Characters");
        Add(RuntimeAuthorityDomains.Characters, allCharacters, currentCharacters, ":Characters");
        foreach (var domain in new[] {
                     (RuntimeAuthorityDomains.Cards, cards, "GetUnlockedCards"),
                     (RuntimeAuthorityDomains.Relics, relics, "GetUnlockedRelics"),
                     (RuntimeAuthorityDomains.Ancients, acts, "GetUnlockedAncients"),
                     (RuntimeAuthorityDomains.Potions, potions, "GetUnlockedPotions") })
        {
            var all = new HashSet<string>(StringComparer.Ordinal);
            var unlocked = new HashSet<string>(StringComparer.Ordinal);
            bool ancient = domain.Item1 == RuntimeAuthorityDomains.Ancients;
            bool allExact = allUnlock is not null && (ancient || domain.Item2.Count > 0);
            bool currentExact = currentUnlock is not null && (ancient || domain.Item2.Count > 0);
            if (ancient)
            {
                yield return null;
                var sharedAll = ReadModelIdentityProperty(allUnlock, "SharedAncients");
                var sharedCurrent = ReadModelIdentityProperty(currentUnlock, "SharedAncients");
                allExact &= sharedAll.Exact;
                currentExact &= sharedCurrent.Exact;
                all.UnionWith(sharedAll.Values);
                unlocked.UnionWith(sharedCurrent.Values);
            }
            foreach (object pool in domain.Item2)
            {
                yield return null;
                var poolAll = InvokeUnlockedAcrossPools(new[] { pool }, domain.Item3, allUnlock);
                all.UnionWith(poolAll.Identities);
                allExact &= poolAll.Exact;
                yield return null;
                var poolCurrent = InvokeUnlockedAcrossPools(new[] { pool }, domain.Item3, currentUnlock);
                unlocked.UnionWith(poolCurrent.Identities);
                currentExact &= poolCurrent.Exact;
            }
            string suffix = ancient ? ";UnlockState.SharedAncients+ActModel.GetUnlockedAncients"
                : $":{domain.Item3};poolCount={domain.Item2.Count}";
            Add(domain.Item1, (all.OrderBy(value => value, StringComparer.Ordinal).ToArray(), allExact),
                (unlocked.OrderBy(value => value, StringComparer.Ordinal).ToArray(), currentExact), suffix);
        }
        void Add(string domain, (string[] Values, bool Exact) all, (string[] Values, bool Exact) unlocked, string suffix)
        {
            authority.Add(new(domain, all.Values, all.Exact, allEvidence + suffix));
            current.Add(new(domain, unlocked.Values, unlocked.Exact, currentEvidence + suffix));
        }
    }
}
