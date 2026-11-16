using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace PetraERP.Application.Common;

/// <summary>
/// Opaque eight-byte SQL Server rowversion exposed as canonical Base64 (12 characters).
/// It is compared byte for byte; clients must never increment, decode or interpret it.
/// </summary>
public sealed record ConcurrencyToken
{
    private readonly ulong value;

    private ConcurrencyToken(ulong value) => this.value = value;

    public static ConcurrencyToken FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != sizeof(ulong))
        {
            throw new ArgumentException("A concurrency version must contain exactly eight bytes.", nameof(bytes));
        }

        return new ConcurrencyToken(BinaryPrimitives.ReadUInt64BigEndian(bytes));
    }

    public static bool TryParse(string? encoded, [NotNullWhen(true)] out ConcurrencyToken? token)
    {
        token = null;
        if (encoded is null || encoded.Length != 12)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[9];
        if (!Convert.TryFromBase64String(encoded, bytes, out var written) || written != sizeof(ulong))
        {
            return false;
        }

        var parsed = FromBytes(bytes[..sizeof(ulong)]);
        if (!string.Equals(parsed.ToBase64(), encoded, StringComparison.Ordinal))
        {
            return false;
        }

        token = parsed;
        return true;
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    public string ToBase64() => Convert.ToBase64String(ToBytes());

    public bool Matches(ReadOnlySpan<byte> current) =>
        current.Length == sizeof(ulong) && BinaryPrimitives.ReadUInt64BigEndian(current) == value;

    public override string ToString() => ToBase64();
}
