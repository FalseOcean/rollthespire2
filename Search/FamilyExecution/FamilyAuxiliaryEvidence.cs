using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Diagnostics;

namespace RolltheSpire2.Search.FamilyExecution;

// Optional estimates and observations never decide whether a candidate passed.
// Call only around auxiliary work, never around numerical execution or ABI validation.
internal static class FamilyAuxiliaryEvidence
{
    internal static T Read<T>(string source, string stage, Func<T> read, T unavailable,
        string? correlationId = null)
    {
        try { return read(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Report(error, source, stage, correlationId);
            return unavailable;
        }
    }

    internal static void Report(Exception error, string source, string stage, string? correlationId = null)
    {
        RuntimeLog.TryBackgroundWarning("searchAuxiliaryFailed=true;source=" + source + ";stage=" + stage +
            ";exceptionJson=" + RuntimeLog.SafeJson(error.ToString()));
        try
        {
            RuntimeIssueLedger.Current.Record(error, "SearchAuxiliaryFailed", source, stage,
                "issues.capture_failed", RuntimeIssueSeverity.Warning, correlationId);
        }
        catch (Exception reportingError) when (reportingError is not OutOfMemoryException)
        {
            RuntimeLog.TryBackgroundWarning("searchAuxiliaryDiagnosticFailed=true;originalExceptionJson=" +
                RuntimeLog.SafeJson(error.ToString()) + ";reportingExceptionJson=" + RuntimeLog.SafeJson(reportingError.ToString()));
        }
    }
}
