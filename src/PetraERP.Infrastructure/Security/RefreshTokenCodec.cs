using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PetraERP.Infrastructure.Security;

/// <summary>
/// Refresh tokens are 256 random bits encoded as unpadded Base64URL (43 characters). Their high
/// entropy makes an unsalted SHA-256 digest a sufficient at-rest verifier; the raw value is never stored.
/// </summary>
internal static class RefreshTokenCodec
{
    internal const int TokenBytes = 32;
    internal const int EncodedLength = 43;

    internal static (string Token, byte[] Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        var token = Base64Url.EncodeToString(bytes);
        CryptographicOperations.ZeroMemory(bytes);
        return (token, HashToken(token));
    }

    /// <summary>Returns null for any value that is not a well-formed token, without throwing.</summary>
    internal static byte[]? TryHash(string? token)
    {
        if (token is null || token.Length != EncodedLength || !Base64Url.IsValid(token.AsSpan(), out var decodedLength)
            || decodedLength != TokenBytes)
        {
            return null;
        }

        return HashToken(token);
    }

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));
}
