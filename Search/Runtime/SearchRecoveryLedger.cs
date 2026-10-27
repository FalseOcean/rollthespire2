using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using System.Text.Json;

namespace RolltheSpire2.Search.Runtime;

// The scan frontier and recovery obligations are one atomic persistence view.
// These sets deliberately coexist: a failed retry can also await delivery, and
// handled-on-resume prevents a second attempt without pretending it was ACKed.
internal sealed class SearchRecoveryLedger
{
    private readonly object _gate = new();
    private readonly ExactSearchExecutionRequest _plan;
    private readonly IRuntimeProfile _profile;
    private readonly string _continuationFingerprint;
    private readonly ulong _scanStart;
    private readonly SortedDictionary<ulong, SearchPendingCandidate> _pending = [];
    private readonly HashSet<ulong> _acknowledged = [];
    private readonly HashSet<ulong> _awaitingDelivery = [];
    private readonly HashSet<ulong> _deliveryRecovery = [];
    private readonly HashSet<ulong> _handledOnResume = [];
    private ulong _safeNextOrdinal;
    private bool _initialized;
    private IReadOnlyList<SearchPendingCandidate>? _pendingSnapshot;
    private SearchContinuationSnapshot? _continuationSnapshot;
    private readonly Func<SearchPendingCandidate, long> _measurePayload;
    private readonly Dictionary<ulong, long> _pendingPayloadSizes = [];
    private readonly Dictionary<ulong, Exception> _payloadAccountingFailures = [];
    private long _pendingPayloadBytes;
    private TaskCompletionSource? _receiptChange;

    internal SearchRecoveryLedger(ExactSearchExecutionRequest plan, IRuntimeProfile profile,
        SearchContinuationSnapshot? continuation, Func<SearchPendingCandidate, long>? measurePayload = null)
    {
        _plan = plan;
        _profile = profile;
        _measurePayload = measurePayload ?? (candidate => JsonSerializer.SerializeToUtf8Bytes(candidate).LongLength);
        _continuationFingerprint = SearchContinuationIdentity.For(plan);
        VisibleSeedCandidateCodec.TryParseOrdinal(profile, plan.CanonicalStartSeed, out _scanStart, out _, out _);
        if (continuation is null) return;
        if (!SearchContinuationIdentity.Matches(continuation, plan, _continuationFingerprint))
            throw new ArgumentException("SearchContinuationIdentityMismatch", nameof(continuation));
        foreach (var pending in continuation.PendingCandidates)
        {
            bool matchesSource = pending.ContinuationFingerprint.Length > 0
                ? pending.ContinuationFingerprint == _continuationFingerprint
                : pending.QueryFingerprint == continuation.SemanticFingerprint &&
                    pending.SnapshotFingerprint == continuation.SnapshotFingerprint;
            if (!matchesSource ||
                !VisibleSeedCandidateCodec.TryParseOrdinal(profile, pending.Seed, out var ordinal, out _, out _) ||
                ordinal != pending.Ordinal)
                throw new ArgumentException("SearchPendingCandidateIdentityMismatch", nameof(continuation));
            StorePending(pending.ContinuationFingerprint.Length > 0 ? pending :
                pending with { ContinuationFingerprint = _continuationFingerprint });
        }
        _acknowledged.UnionWith(continuation.PublishedOrdinals);
        if (continuation.UnreceivedOrdinals is not { } unreceived) return;
        ulong spaceSize = VisibleSeedCandidateCodec.SpaceSize(profile);
        foreach (ulong ordinal in unreceived)
        {
            if (ordinal >= spaceSize)
                throw new ArgumentException("SearchUnreceivedOrdinalInvalid", nameof(continuation));
            if (!_acknowledged.Contains(ordinal)) _deliveryRecovery.Add(ordinal);
        }
    }

    internal int PendingCount { get { lock (_gate) return _pending.Count; } }
    // Canonical compact pending-record JSON only: not RAM or whole workspace size.
    internal (long Bytes, Exception? AccountingFailure) CapturePendingPayload()
    {
        lock (_gate) return (_pendingPayloadBytes, _payloadAccountingFailures.Count == 0 ? null : _payloadAccountingFailures.Values.First());
    }

    internal (long Bytes, bool AccountingIncomplete, Task? ReceiptChange) CaptureNewCandidateAdmission(long allowance)
    {
        lock (_gate)
        {
            long bytes = _pendingPayloadBytes;
            Task? receiptChange = null;
            if (bytes >= allowance)
            {
                long unresolvedBytes = bytes;
                foreach (ulong ordinal in _awaitingDelivery)
                    if (_pendingPayloadSizes.TryGetValue(ordinal, out long deliveredBytes)) unresolvedBytes -= deliveredBytes;
                if (unresolvedBytes < allowance)
                    receiptChange = (_receiptChange ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
            return (bytes, _payloadAccountingFailures.Count > 0, receiptChange);
        }
    }

    private void PulseReceiptChange()
    {
        var change = _receiptChange;
        _receiptChange = null;
        change?.TrySetResult();
    }
    internal ulong SafeNextOrdinal { get { lock (_gate) return _initialized ? _safeNextOrdinal : 0UL; } }

    internal bool TryGetSafeNextOrdinal(out ulong nextOrdinal)
    {
        lock (_gate)
        {
            nextOrdinal = _safeNextOrdinal;
            return _initialized;
        }
    }

    internal bool TryAdvanceCursor(ulong nextOrdinal, out ulong currentOrdinal)
    {
        lock (_gate)
        {
            currentOrdinal = _safeNextOrdinal;
            if (_initialized && nextOrdinal < _safeNextOrdinal) return false;
            if (!_initialized || nextOrdinal > _safeNextOrdinal)
            {
                _safeNextOrdinal = nextOrdinal;
                _continuationSnapshot = null;
            }
            _initialized = true;
            return true;
        }
    }

    internal SearchContinuationSnapshot? CaptureContinuation()
    {
        lock (_gate)
        {
            if (!_initialized) return null;
            if (_continuationSnapshot is { } unchanged) return unchanged;
            ulong frontier = _safeNextOrdinal;
            // Pending retries and restored deliveries have independent recovery
            // ownership. An explicit origin survives recovery of older outputs.
            foreach (ulong ordinal in _awaitingDelivery)
                if (!_pending.ContainsKey(ordinal) &&
                    (!_deliveryRecovery.Contains(ordinal) || ordinal >= _scanStart))
                    frontier = Math.Min(frontier, ordinal);
            // Immutable lists may be shared by cursor-only checkpoints. A pending
            // mutation replaces the list, so older checkpoints never change.
            _pendingSnapshot ??= Array.AsReadOnly(_pending.Values.ToArray());
            return _continuationSnapshot = new(_plan.CompiledSearch.SemanticFingerprint, _plan.SnapshotFingerprint, frontier,
                _pendingSnapshot,
                Array.AsReadOnly(_acknowledged.Where(o => o >= frontier).Order().ToArray()))
            {
                ContinuationFingerprint = _continuationFingerprint,
                UnreceivedOrdinals = Array.AsReadOnly(_deliveryRecovery.Concat(_awaitingDelivery).Distinct().Order().ToArray())
            };
        }
    }

    internal void BeginDelivery(ulong ordinal)
    {
        lock (_gate)
            if (_awaitingDelivery.Add(ordinal)) { _continuationSnapshot = null; PulseReceiptChange(); }
    }

    internal void AbandonDelivery(ulong ordinal)
    {
        lock (_gate)
            if (_awaitingDelivery.Remove(ordinal)) { _continuationSnapshot = null; PulseReceiptChange(); }
    }

    internal void AcknowledgeCandidate(string seed)
    {
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(_profile, seed, out var ordinal, out _, out var issue))
            throw new ArgumentException("SearchDeliverySeedInvalid:" + issue, nameof(seed));
        lock (_gate)
        {
            if (!_awaitingDelivery.Remove(ordinal)) return;
            _continuationSnapshot = null;
            _acknowledged.Add(ordinal);
            _deliveryRecovery.Remove(ordinal);
            // A successful retry retains its original failure until receipt.
            RemovePending(ordinal);
            PulseReceiptChange();
        }
    }

    internal bool WasAlreadyHandled(ulong ordinal)
    {
        lock (_gate)
            return _acknowledged.Contains(ordinal) || _handledOnResume.Contains(ordinal) ||
                (_plan.RunOptions.SkipExactValidation && _pending.ContainsKey(ordinal));
    }

    internal SearchPendingCandidate[] PendingRetries()
    {
        lock (_gate) return _pending.Values.ToArray();
    }

    internal ulong[] UnreceivedRecoveries()
    {
        lock (_gate) return _deliveryRecovery.Order().ToArray();
    }

    internal bool ShouldRecoverDelivery(ulong ordinal)
    {
        // The pending retry owns its original failure, including under SkipExact.
        lock (_gate) return !_pending.ContainsKey(ordinal) && !_handledOnResume.Contains(ordinal);
    }

    internal void CompletePendingRetry(ulong ordinal)
    {
        lock (_gate) _handledOnResume.Add(ordinal);
    }

    internal void CompleteDeliveryRecovery(ulong ordinal)
    {
        lock (_gate)
        {
            _handledOnResume.Add(ordinal);
            // Published output belongs to delivery until ACK; a rejection is
            // resolved, and a fault now belongs to the independent pending set.
            if (!_awaitingDelivery.Contains(ordinal) && _deliveryRecovery.Remove(ordinal))
                _continuationSnapshot = null;
        }
    }

    internal void RetainFailure(ulong ordinal, string seed, Exception exception)
    {
        lock (_gate)
        {
            _pending.TryGetValue(ordinal, out var previous);
            string details = previous is null ? exception.ToString() : previous.ExceptionText +
                "\n\nretryQuery=" + _plan.CompiledSearch.SemanticFingerprint +
                ";retrySnapshot=" + _plan.SnapshotFingerprint + "\n" + exception;
            StorePending(new(ordinal, seed, "CandidateCheck", "CandidateCheckFailed:" + exception.GetType().Name, details,
                previous?.QueryFingerprint ?? _plan.CompiledSearch.SemanticFingerprint,
                previous?.SnapshotFingerprint ?? _plan.SnapshotFingerprint,
                previous?.FirstFailedAtUtc ?? DateTimeOffset.UtcNow, (previous?.Attempts ?? 0) + 1)
            { ContinuationFingerprint = _continuationFingerprint });
            _deliveryRecovery.Remove(ordinal);
        }
    }

    internal void ResolveCandidate(ulong ordinal, bool published = false)
    {
        lock (_gate)
        {
            if (!published || !_awaitingDelivery.Contains(ordinal)) RemovePending(ordinal);
            if (!published && _deliveryRecovery.Remove(ordinal)) _continuationSnapshot = null;
        }
    }

    private void StorePending(SearchPendingCandidate candidate)
    {
        // Retain the real failure before optional accounting. Accounting cannot
        // erase/replace a cause or claim that an unmeasured payload is small.
        _pending[candidate.Ordinal] = candidate;
        _pendingSnapshot = null;
        _continuationSnapshot = null;
        PulseReceiptChange();
        if (_pendingPayloadSizes.Remove(candidate.Ordinal, out long old)) _pendingPayloadBytes -= old;
        _payloadAccountingFailures.Remove(candidate.Ordinal);
        try
        {
            long size = _measurePayload(candidate);
            if (size < 0) throw new InvalidOperationException("SearchRecoveryPayloadSizeNegative");
            long total = checked(_pendingPayloadBytes + size);
            _pendingPayloadSizes[candidate.Ordinal] = size;
            _pendingPayloadBytes = total;
        }
        catch (Exception exception)
        {
            _payloadAccountingFailures[candidate.Ordinal] = exception;
            try
            {
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception,
                    "SearchRecoveryPayloadAccountingFailed", nameof(SearchRecoveryLedger), "PendingPayloadAccounting",
                    "issues.diagnostics_failed", Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning,
                    correlationId: $"seed={candidate.Seed};ordinal={candidate.Ordinal};pendingPreserved=true");
            }
            catch { /* The original pending cause already has independent ownership. */ }
        }
    }

    private void RemovePending(ulong ordinal)
    {
        if (!_pending.Remove(ordinal)) return;
        if (_pendingPayloadSizes.Remove(ordinal, out long size)) _pendingPayloadBytes -= size;
        _payloadAccountingFailures.Remove(ordinal);
        _pendingSnapshot = null;
        _continuationSnapshot = null;
        PulseReceiptChange();
    }
}
