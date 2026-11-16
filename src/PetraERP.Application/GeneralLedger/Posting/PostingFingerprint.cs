using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PetraERP.Application.GeneralLedger.Posting;

/// <summary>Versioned semantic request digest, not an authentication credential.</summary>
public sealed record PostingFingerprint
{
    private PostingFingerprint(string hex) => Hex = hex;

    public string Hex { get; }

    public static PostingFingerprint FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != 64)
        {
            throw new FormatException("A SHA-256 fingerprint must contain 64 hexadecimal characters.");
        }

        return new PostingFingerprint(Convert.ToHexString(Convert.FromHexString(hex)));
    }

    internal static PostingFingerprint Calculate(PostingRequest request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string[] fields =
        [
            "PetraERP.JournalPosting.v1",
            request.Operation switch
            {
                PostingOperation.PostJournal => "journal.post",
                PostingOperation.PostJournalReversal => "journal.reverse",
                _ => throw new InvalidOperationException("Unsupported posting fingerprint operation."),
            },
            request.Scope.CompanyId.ToString("D"),
            request.Scope.BranchId.ToString("D"),
            request.Scope.ActorUserId.ToString("D"),
            request.JournalId.ToString("D"),
            request.ExpectedVersion.ToBase64(),
            request.OriginalJournalId?.ToString("D") ?? string.Empty,
            request.OriginalExpectedVersion?.ToBase64() ?? string.Empty,
            request.Reason.Value,
        ];
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return new PostingFingerprint(Convert.ToHexString(hash.GetHashAndReset()));
    }
}
