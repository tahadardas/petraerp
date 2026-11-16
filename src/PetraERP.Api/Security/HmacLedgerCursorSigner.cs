using System.Security.Cryptography;
using PetraERP.Application.GeneralLedger.Reports;

namespace PetraERP.Api.Security;

/// <summary>
/// Authenticates ledger paging cursors with HMAC-SHA256 so a client cannot forge a running balance or watermark.
/// The key comes from PETRAERP_LEDGER_CURSOR_KEY (shared by every API instance); without it a per-process random key is used,
/// which is still safe, only a cursor does not survive a restart or travel between instances (the client reloads page one).
/// </summary>
public sealed class HmacLedgerCursorSigner : ILedgerCursorSigner
{
    private const int TagLength = 32;
    private readonly byte[] key;

    public HmacLedgerCursorSigner(byte[]? key = null)
    {
        this.key = key is { Length: >= 16 } ? key : RandomNumberGenerator.GetBytes(32);
    }

    public static HmacLedgerCursorSigner FromEnvironment()
    {
        var text = Environment.GetEnvironmentVariable("PETRAERP_LEDGER_CURSOR_KEY");
        return new HmacLedgerCursorSigner(string.IsNullOrWhiteSpace(text) ? null : SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    public string Protect(ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[payload.Length + TagLength];
        payload.CopyTo(buffer);
        HMACSHA256.HashData(key, payload, buffer.AsSpan(payload.Length));
        return Convert.ToBase64String(buffer).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public bool TryUnprotect(string token, out byte[] payload)
    {
        payload = [];
        if (string.IsNullOrEmpty(token) || token.Length > 4096) return false;
        var padded = token.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (raw.Length <= TagLength) return false;
        var body = raw.AsSpan(0, raw.Length - TagLength);
        var expected = HMACSHA256.HashData(key, body);
        if (!CryptographicOperations.FixedTimeEquals(expected, raw.AsSpan(raw.Length - TagLength))) return false;
        payload = body.ToArray();
        return true;
    }
}
