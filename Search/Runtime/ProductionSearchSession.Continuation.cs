using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Runtime;

public sealed partial class ProductionSearchSession
{
    public SearchContinuationSnapshot? CaptureContinuation() => _recovery.CaptureContinuation();

    public void AcknowledgeCandidate(string seed) => _recovery.AcknowledgeCandidate(seed);

    private async ValueTask<bool> AdmitNewCandidateAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var (bytes, accountingIncomplete, receiptChange) = _recovery.CaptureNewCandidateAdmission(_pendingPayloadLimitBytes);
            if (bytes < _pendingPayloadLimitBytes) return true;
            if (receiptChange is not null)
            {
                // The task was captured under the ledger lock, so an ACK between
                // inspection and await cannot be lost. No Exact work is repeated.
                _recoveryReceiptWaitStarted?.Invoke();
                await receiptChange.WaitAsync(token).ConfigureAwait(false);
                continue;
            }
            var failure = new InvalidOperationException(
                $"SearchRecoveryCapacityReached:pendingCompactJsonBytes={bytes};limitBytes={_pendingPayloadLimitBytes};" +
                $"payloadAccountingIncomplete={accountingIncomplete};pendingPreserved=true");
            Interlocked.CompareExchange(ref _recoveryCapacityFailure, failure, null);
            _lifetime.Cancel();
            return false;
        }
    }

    private async Task RetryPendingCandidatesAsync(CancellationToken token)
    {
        if (_plan.RunOptions.SkipExactValidation) return;
        SearchPendingCandidate[] pending = _recovery.PendingRetries();
        foreach (var candidate in pending)
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _targetReached) != 0) break;
            if (await EvaluateCandidateAsync(new SearchBatch(candidate.Ordinal, 1), 0, token, retrying: true).ConfigureAwait(false))
                _recovery.CompletePendingRetry(candidate.Ordinal);
        }
    }

    private async Task RecoverUnreceivedCandidatesAsync(CancellationToken token)
    {
        ulong[] recovery = _recovery.UnreceivedRecoveries();
        foreach (ulong ordinal in recovery)
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _targetReached) != 0) break;
            if (!_recovery.ShouldRecoverDelivery(ordinal)) continue;
            bool handled;
            try
            {
                handled = await EvaluateCandidateAsync(new SearchBatch(ordinal, 1), 0, token,
                    retrying: true).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
            {
                RetainFailedCandidate(ordinal, VisibleSeedCandidateCodec.FormatOrdinal(_profile, ordinal), exception);
                handled = true;
            }
            if (!handled) continue;
            _recovery.CompleteDeliveryRecovery(ordinal);
        }
    }

    private void RetainFailedCandidate(ulong ordinal, string seed, Exception exception)
    {
        _recovery.RetainFailure(ordinal, seed, exception);
        RecordCandidateFailure(ordinal, seed, exception);
    }
}
