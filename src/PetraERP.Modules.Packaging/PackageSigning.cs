using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PetraERP.Modules.Packaging;

/// <summary>
/// Who a package may come from. Matching a publisher NAME is not trust: anyone can write a name into a manifest. A publisher is
/// trusted only if the installation holds its public key, the manifest carries a signature that verifies against that key, and
/// the publisher has not been revoked.
/// </summary>
public sealed record PublisherTrust(IReadOnlyDictionary<string, string> PublicKeys, IReadOnlySet<string> RevokedPublishers)
{
    public static PublisherTrust Empty { get; } = new(new Dictionary<string, string>(), new HashSet<string>());
}

/// <summary>ECDSA P-256 / SHA-256 signature over the canonical manifest (everything except the signature and fingerprint fields).</summary>
public static class PackageSigning
{
    private static readonly JsonSerializerOptions CanonicalOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The exact bytes that are signed; file hashes are inside the manifest, so the whole package content is covered.</summary>
    public static byte[] CanonicalBytes(ModuleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest with { Signature = null, ContentFingerprint = null }, CanonicalOptions));
    }

    public static string Sign(ModuleManifest manifest, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        return Convert.ToBase64String(privateKey.SignData(CanonicalBytes(manifest), HashAlgorithmName.SHA256));
    }

    /// <summary>Public keys are exchanged as Base64 SubjectPublicKeyInfo.</summary>
    public static string ExportPublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    public static bool Verify(ModuleManifest manifest, string publicKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(manifest.Signature) || string.IsNullOrWhiteSpace(publicKeyBase64)) return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyData(CanonicalBytes(manifest), Convert.FromBase64String(manifest.Signature), HashAlgorithmName.SHA256);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
