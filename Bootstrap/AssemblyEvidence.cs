using System.Security.Cryptography;

namespace RolltheSpire2.Bootstrap;

internal sealed record AssemblyEvidence(
    string AssemblyPath,
    string Sha256,
    bool IsExact,
    string FailureReason)
{
    public static AssemblyEvidence Capture()
    {
        try
        {
            string path = typeof(AssemblyEvidence).Assembly.Location;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new AssemblyEvidence(path ?? string.Empty, string.Empty, false, "Loaded mod assembly path is unavailable.");
            }

            using FileStream stream = File.OpenRead(path);
            string sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return new AssemblyEvidence(path, sha256, true, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return new AssemblyEvidence(string.Empty, string.Empty, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
