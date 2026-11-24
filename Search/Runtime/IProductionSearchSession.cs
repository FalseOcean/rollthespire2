using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Runtime;

public interface IProductionSearchSession : IAsyncDisposable
{
    Task Completion { get; }
    void Cancel();
    bool TryReadCandidate(out SearchCandidate? candidate);
    SearchProgressSnapshot GetProgress();
    SearchDiagnosticSummarySnapshot GetDiagnosticSummary();
    ulong GetSafeNextOrdinal();
    bool TryGetSafeNextOrdinal(out ulong nextOrdinal);
    SearchContinuationSnapshot? CaptureContinuation() => null;
    void AcknowledgeCandidate(string seed) { }
}
