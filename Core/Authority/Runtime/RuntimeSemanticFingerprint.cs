using System.Security.Cryptography;
using System.Text;

namespace RolltheSpire2.Core.Authority.Runtime;

internal static class RuntimeSemanticFingerprint
{
    public static RuntimeSemanticFingerprintBundle Build(
        int schemaVersion,
        IReadOnlyList<RuntimeSemanticDomainSnapshot> semanticDomains,
        IReadOnlyList<RuntimeUnlockAuthorityDomainSnapshot> unlockDomains)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var issues = new List<string>();

        foreach (string domainName in RuntimeAuthorityDomains.Schema1SemanticDomains)
        {
            RuntimeSemanticDomainSnapshot? domain = semanticDomains.FirstOrDefault(item =>
                string.Equals(item.Domain, domainName, StringComparison.Ordinal));
            if (domain is null || !domain.AuthorityComplete)
            {
                issues.Add("SemanticDomainIncomplete:" + domainName);
                continue;
            }
            hashes[domainName] = HashSemanticDomain(schemaVersion, domain);
        }

        bool unlockComplete = true;
        foreach (string domainName in RuntimeAuthorityDomains.Schema1UnlockDomains)
        {
            RuntimeUnlockAuthorityDomainSnapshot? domain = unlockDomains.FirstOrDefault(item =>
                string.Equals(item.Domain, domainName, StringComparison.Ordinal));
            if (domain is null || !domain.AuthorityComplete)
            {
                unlockComplete = false;
                issues.Add("UnlockUniverseIncomplete:" + domainName);
            }
        }

        string unlockUniverseHash = unlockComplete
            ? HashUnlockUniverse(schemaVersion, unlockDomains)
            : string.Empty;
        bool complete = RuntimeAuthorityDomains.Schema1SemanticDomains.All(hashes.ContainsKey) && unlockComplete;
        string overall = complete
            ? HashOverall(schemaVersion, hashes, unlockUniverseHash)
            : string.Empty;
        return new RuntimeSemanticFingerprintBundle(
            schemaVersion,
            hashes,
            unlockUniverseHash,
            overall,
            complete,
            complete ? "RuntimeSemanticFingerprintComplete" : string.Join("|", issues));
    }

    private static string HashSemanticDomain(int schemaVersion, RuntimeSemanticDomainSnapshot domain)
    {
        var builder = new CanonicalBuilder();
        builder.Token("rt2-runtime-semantic-domain");
        builder.Int(schemaVersion);
        builder.Token(domain.Domain);
        RuntimeSemanticItem[] ordered = domain.Items
            .OrderBy(item => item.StableIdentity, StringComparer.Ordinal)
            .ToArray();
        builder.Int(ordered.Length);
        foreach (RuntimeSemanticItem item in ordered)
        {
            builder.Token(item.StableIdentity);
            builder.Int(item.Fields.Count);
            foreach (string field in item.Fields)
                builder.Token(field ?? string.Empty);
        }
        return Sha256(builder.ToBytes());
    }

    private static string HashUnlockUniverse(
        int schemaVersion,
        IReadOnlyList<RuntimeUnlockAuthorityDomainSnapshot> unlockDomains)
    {
        var builder = new CanonicalBuilder();
        builder.Token("rt2-runtime-unlock-universe");
        builder.Int(schemaVersion);
        foreach (string domainName in RuntimeAuthorityDomains.Schema1UnlockDomains.OrderBy(value => value, StringComparer.Ordinal))
        {
            RuntimeUnlockAuthorityDomainSnapshot domain = unlockDomains.First(item =>
                string.Equals(item.Domain, domainName, StringComparison.Ordinal));
            builder.Token(domainName);
            string[] ordered = domain.UnlockUniverse
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            builder.Int(ordered.Length);
            foreach (string identity in ordered) builder.Token(identity);
        }
        return Sha256(builder.ToBytes());
    }

    private static string HashOverall(
        int schemaVersion,
        IReadOnlyDictionary<string, string> domainHashes,
        string unlockUniverseHash)
    {
        var builder = new CanonicalBuilder();
        builder.Token("rt2-runtime-semantic-overall");
        builder.Int(schemaVersion);
        foreach (string domain in RuntimeAuthorityDomains.Schema1SemanticDomains.OrderBy(value => value, StringComparer.Ordinal))
        {
            builder.Token(domain);
            builder.Token(domainHashes[domain]);
        }
        builder.Token("UnlockUniverse");
        builder.Token(unlockUniverseHash);
        return Sha256(builder.ToBytes());
    }

    private static string Sha256(byte[] payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    private sealed class CanonicalBuilder
    {
        private readonly MemoryStream _stream = new();

        public void Int(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            _stream.Write(bytes);
        }

        public void Token(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            Int(bytes.Length);
            _stream.Write(bytes, 0, bytes.Length);
        }

        public byte[] ToBytes() => _stream.ToArray();
    }
}
