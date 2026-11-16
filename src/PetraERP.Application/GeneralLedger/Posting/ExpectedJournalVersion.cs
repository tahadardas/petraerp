using System.Buffers.Binary;

namespace PetraERP.Application.GeneralLedger.Posting;

/// <summary>Opaque expected eight-byte database version, distinct from the domain revision counter.</summary>
public sealed record ExpectedJournalVersion
{
    private readonly ulong value;

    private ExpectedJournalVersion(ulong value) => this.value = value;

    public static ExpectedJournalVersion Create(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != sizeof(ulong))
        {
            throw new ArgumentException("A journal concurrency version must contain exactly eight bytes.", nameof(bytes));
        }

        return new ExpectedJournalVersion(BinaryPrimitives.ReadUInt64BigEndian(bytes));
    }

    public static ExpectedJournalVersion FromBase64(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length != 12)
        {
            throw new FormatException("A canonical eight-byte Base64 version is required.");
        }

        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length != sizeof(ulong) || Convert.ToBase64String(bytes) != encoded)
        {
            throw new FormatException("A canonical eight-byte Base64 version is required.");
        }

        return Create(bytes);
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    public string ToBase64() => Convert.ToBase64String(ToBytes());
}
